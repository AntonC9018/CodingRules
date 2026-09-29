using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

/// <summary>
/// Statement builders and fix plumbing shared by the return-decision code
/// fixers. The parent-shape replacement (<see cref="ApplyAsync"/>) inserts
/// generated statements at arbitrary depth and is reusable by the fixers of
/// later tickets (inline conditions, argument structure, sentinels).
/// </summary>
internal static class ReturnDecisionRewrites
{
    /// <summary>
    /// Applies the replacement, lays it out with the Roslyn formatter so the
    /// document's editorconfig options drive spacing and braces, and aligns
    /// the generated line endings with the document's existing convention.
    /// </summary>
    public static async Task<Document> ApplyFormattedAsync(
        Document document,
        ReturnStatementSyntax returnStatement,
        List<StatementSyntax> replacement,
        CancellationToken cancellationToken)
    {
        var changed = await ApplyAsync(document, returnStatement, replacement, cancellationToken).ConfigureAwait(false);
        var root = await changed.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return changed;
        }

        var endOfLine = FindDocumentEndOfLine(root);
        var options = await changed.GetOptionsAsync(cancellationToken).ConfigureAwait(false);
        var alignedOptions = options.WithChangedOption(FormattingOptions.NewLine, changed.Project.Language, endOfLine);
        var formatted = await Formatter.FormatAsync(changed, Formatter.Annotation, alignedOptions, cancellationToken).ConfigureAwait(false);

        var formattedRoot = await formatted.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (formattedRoot is null)
        {
            return formatted;
        }

        var containers = formattedRoot.GetAnnotatedNodes(Formatter.Annotation).ToList();
        if (containers.Count == 0)
        {
            return formatted;
        }

        var normalizedRoot = formattedRoot.ReplaceNodes(
            containers,
            (_, rewrittenNode) => NormalizeEndOfLines(rewrittenNode, endOfLine));
        return formatted.WithSyntaxRoot(normalizedRoot);
    }

    public static List<StatementSyntax> CreateReplacement(
        ReturnStatementSyntax returnStatement,
        ReturnDecisionKind kind,
        SemanticModel semanticModel)
    {
        var expression = ReturnDecisionAnalysis.Unwrap(returnStatement.Expression!);
        switch (kind)
        {
            case ReturnDecisionKind.Conditional:
                return CreateConditionalReplacement((ConditionalExpressionSyntax)expression, semanticModel);
            case ReturnDecisionKind.Coalesce:
                return CreateCoalesceReplacement(
                    (BinaryExpressionSyntax)expression,
                    semanticModel,
                    returnStatement);
            case ReturnDecisionKind.Boolean:
                return CreateBooleanReplacement(TrimBoundaryLineBreaks(expression));
            case ReturnDecisionKind.NullableCall:
                return CreateNullableCallReplacement(TrimBoundaryLineBreaks(expression), semanticModel, returnStatement);
            default:
                return new List<StatementSyntax>();
        }
    }

    /// <summary>
    /// Removes the end-of-line trivia from the first and last tokens of an
    /// expression that a rewrite moves into a new statement. The trivia is
    /// layout residue from the multi-line return it was extracted from;
    /// line breaks inside the expression are kept.
    /// </summary>
    private static ExpressionSyntax TrimBoundaryLineBreaks(ExpressionSyntax expression)
    {
        var firstToken = expression.GetFirstToken(includeZeroWidth: false);
        var leading = firstToken.LeadingTrivia;
        if (leading.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            expression = expression.ReplaceToken(
                firstToken,
                firstToken.WithLeadingTrivia(leading.Where(trivia => !trivia.IsKind(SyntaxKind.EndOfLineTrivia))));
        }

        var lastToken = expression.GetLastToken(includeZeroWidth: false);
        var trailing = lastToken.TrailingTrivia;
        if (trailing.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
        {
            expression = expression.ReplaceToken(
                lastToken,
                lastToken.WithTrailingTrivia(trailing.Where(trivia => !trivia.IsKind(SyntaxKind.EndOfLineTrivia))));
        }

        return expression;
    }

    /// <summary>
    /// Replaces one return statement with the generated statements, in place
    /// for a parent block or switch section, or wrapped in a block when the
    /// return is an embedded (braceless) statement. Leading and trailing
    /// trivia of the return are preserved.
    /// </summary>
    public static async Task<Document> ApplyAsync(
        Document document,
        ReturnStatementSyntax returnStatement,
        List<StatementSyntax> replacement,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        replacement[0] = replacement[0].WithLeadingTrivia(returnStatement.GetLeadingTrivia());
        var lastIndex = replacement.Count - 1;
        replacement[lastIndex] = replacement[lastIndex].WithTrailingTrivia(returnStatement.GetTrailingTrivia());

        SyntaxNode newRoot;
        switch (returnStatement.Parent)
        {
            case BlockSyntax block:
            {
                var newStatements = block.Statements.ReplaceRange(returnStatement, replacement);
                var newBlock = block.WithStatements(newStatements).WithAdditionalAnnotations(Formatter.Annotation);
                newRoot = root.ReplaceNode(block, newBlock);
                break;
            }

            case SwitchSectionSyntax section:
            {
                var newStatements = section.Statements.ReplaceRange(returnStatement, replacement);
                var newSection = section.WithStatements(newStatements).WithAdditionalAnnotations(Formatter.Annotation);
                newRoot = root.ReplaceNode(section, newSection);
                break;
            }

            default:
            {
                var wrapped = SyntaxFactory.Block(replacement);

                // An embedded (braceless) return is replaced by a block inside
                // its owning statement. That statement is the smallest
                // containing statement container, so it carries the formatter
                // annotation and its boundary line endings stay normalized.
                if (returnStatement.Parent is StatementSyntax container)
                {
                    var newContainer = container
                        .ReplaceNode(returnStatement, wrapped)
                        .WithAdditionalAnnotations(Formatter.Annotation);
                    newRoot = root.ReplaceNode(container, newContainer);
                }
                else
                {
                    wrapped = wrapped.WithAdditionalAnnotations(Formatter.Annotation);
                    newRoot = root.ReplaceNode(returnStatement, wrapped);
                }

                break;
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static List<StatementSyntax> CreateConditionalReplacement(
        ConditionalExpressionSyntax conditional,
        SemanticModel semanticModel)
    {
        var condition = TrimBoundaryLineBreaks(conditional.Condition);
        var whenTrue = TrimBoundaryLineBreaks(ReturnDecisionAnalysis.Unwrap(conditional.WhenTrue));
        var whenFalse = TrimBoundaryLineBreaks(ReturnDecisionAnalysis.Unwrap(conditional.WhenFalse));

        if (whenTrue is ThrowExpressionSyntax trueThrow)
        {
            // A throw in the true arm guards without negating the condition.
            return new List<StatementSyntax>
            {
                CreateGuardThrow(condition, trueThrow.Expression),
                SyntaxFactory.ReturnStatement(whenFalse),
            };
        }

        if (whenFalse is ThrowExpressionSyntax falseThrow)
        {
            // A throw in the false arm becomes an inverted guard so the found
            // value still falls through after the guard, matching the fallback
            // direction of the earlier branches.
            return new List<StatementSyntax>
            {
                CreateGuardThrow(Negate(condition, semanticModel), falseThrow.Expression),
                SyntaxFactory.ReturnStatement(whenTrue),
            };
        }

        var found = whenTrue;
        var fallback = whenFalse;
        if (ReturnDecisionAnalysis.NeedsFallbackInversion(conditional))
        {
            condition = Negate(condition, semanticModel);
            found = whenFalse;
            fallback = whenTrue;
        }

        return new List<StatementSyntax>
        {
            CreateIfReturn(condition, found),
            SyntaxFactory.ReturnStatement(fallback),
        };
    }

    private static List<StatementSyntax> CreateCoalesceReplacement(
        BinaryExpressionSyntax coalesce,
        SemanticModel semanticModel,
        ReturnStatementSyntax returnStatement)
    {
        var name = FindAvailableName(semanticModel, returnStatement);
        var leftType = semanticModel.GetTypeInfo(coalesce.Left).Type;
        var nullableValue = ReturnDecisionAnalysis.IsNullableValueType(leftType);
        var conditionText = nullableValue ? name + ".HasValue" : name + " is not null";
        var foundText = nullableValue ? name + ".Value" : name;
        var condition = SyntaxFactory.ParseExpression(conditionText);
        var found = SyntaxFactory.ParseExpression(foundText);
        return new List<StatementSyntax>
        {
            CreateLocal(name, TrimBoundaryLineBreaks(coalesce.Left)),
            CreateIfReturn(condition, found),
            SyntaxFactory.ReturnStatement(TrimBoundaryLineBreaks(coalesce.Right)),
        };
    }

    private static List<StatementSyntax> CreateBooleanReplacement(ExpressionSyntax expression)
    {
        var trueLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression);
        var falseLiteral = SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);
        return new List<StatementSyntax>
        {
            CreateIfReturn(expression, trueLiteral),
            SyntaxFactory.ReturnStatement(falseLiteral),
        };
    }

    private static List<StatementSyntax> CreateNullableCallReplacement(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        ReturnStatementSyntax returnStatement)
    {
        var name = FindAvailableName(semanticModel, returnStatement);
        var condition = SyntaxFactory.ParseExpression(name + " is not null");
        var found = SyntaxFactory.IdentifierName(name);
        var fallback = SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression);
        return new List<StatementSyntax>
        {
            CreateLocal(name, expression),
            CreateIfReturn(condition, found),
            SyntaxFactory.ReturnStatement(fallback),
        };
    }

    private static IfStatementSyntax CreateIfReturn(ExpressionSyntax condition, ExpressionSyntax value)
    {
        var returnStatement = SyntaxFactory.ReturnStatement(value);
        var body = SyntaxFactory.Block(returnStatement);
        return SyntaxFactory.IfStatement(condition, body);
    }

    private static IfStatementSyntax CreateGuardThrow(ExpressionSyntax condition, ExpressionSyntax thrown)
    {
        var body = SyntaxFactory.Block(SyntaxFactory.ThrowStatement(thrown));
        return SyntaxFactory.IfStatement(condition, body);
    }

    private static LocalDeclarationStatementSyntax CreateLocal(string name, ExpressionSyntax value)
    {
        var initializer = SyntaxFactory.EqualsValueClause(value);
        var identifier = SyntaxFactory.Identifier(name);
        var variable = SyntaxFactory.VariableDeclarator(identifier);
        variable = variable.WithInitializer(initializer);
        var variables = SyntaxFactory.SingletonSeparatedList(variable);
        var varType = SyntaxFactory.IdentifierName("var");
        var declaration = SyntaxFactory.VariableDeclaration(varType, variables);
        return SyntaxFactory.LocalDeclarationStatement(declaration);
    }

    private static string FindAvailableName(SemanticModel semanticModel, ReturnStatementSyntax returnStatement)
    {
        var symbols = semanticModel.LookupSymbols(returnStatement.SpanStart);
        var block = returnStatement.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        var names = block is null
            ? Enumerable.Empty<string>()
            : block.DescendantTokens()
                .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
                .Select(token => token.ValueText);
        var identifiers = new HashSet<string>(names);
        var name = "result";
        var suffix = 1;
        while (IsUsedName(symbols, identifiers, name))
        {
            name = "result" + suffix;
            suffix++;
        }

        return name;
    }

    private static bool IsUsedName(
        ImmutableArray<ISymbol> symbols,
        HashSet<string> identifiers,
        string name)
    {
        if (identifiers.Contains(name))
        {
            return true;
        }

        if (symbols.Any(symbol => symbol.Name == name))
        {
            return true;
        }

        return false;
    }

    private static ExpressionSyntax Negate(ExpressionSyntax condition, SemanticModel semanticModel)
    {
        var unwrapped = ReturnDecisionAnalysis.Unwrap(condition);
        if (unwrapped is BinaryExpressionSyntax binary)
        {
            var operation = semanticModel.GetOperation(binary);
            if (operation is IBinaryOperation { OperatorMethod: null })
            {
                if (TryGetInverse(binary, out var inverse, out var token))
                {
                    var operatorToken = SyntaxFactory.Token(token).WithTriviaFrom(binary.OperatorToken);
                    return SyntaxFactory.BinaryExpression(inverse, binary.Left, operatorToken, binary.Right);
                }
            }
        }

        var operand = BindsTighterThanLogicalNot(unwrapped) ? unwrapped : SyntaxFactory.ParenthesizedExpression(condition);
        return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, operand);
    }

    /// <summary>
    /// Reports whether a condition binds tighter than the logical negation
    /// operator, so the guard rewrite can write `!condition` without adding
    /// parentheses.
    /// </summary>
    private static bool BindsTighterThanLogicalNot(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case InvocationExpressionSyntax:
            case ObjectCreationExpressionSyntax:
            case MemberAccessExpressionSyntax:
            case MemberBindingExpressionSyntax:
            case ElementAccessExpressionSyntax:
            case ConditionalAccessExpressionSyntax:
            case IdentifierNameSyntax:
            case LiteralExpressionSyntax:
            case AwaitExpressionSyntax:
            case PostfixUnaryExpressionSyntax:
            case PrefixUnaryExpressionSyntax:
                return true;
            default:
                return false;
        }
    }

    private static bool TryGetInverse(
        BinaryExpressionSyntax expression,
        out SyntaxKind inverse,
        out SyntaxKind token)
    {
        switch (expression.Kind())
        {
            case SyntaxKind.EqualsExpression:
                inverse = SyntaxKind.NotEqualsExpression;
                token = SyntaxKind.ExclamationEqualsToken;
                return true;
            case SyntaxKind.NotEqualsExpression:
                inverse = SyntaxKind.EqualsExpression;
                token = SyntaxKind.EqualsEqualsToken;
                return true;
            default:
                inverse = default;
                token = default;
                return false;
        }
    }

    private static string FindDocumentEndOfLine(SyntaxNode root)
    {
        foreach (var trivia in root.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                return trivia.ToFullString();
            }
        }

        // A document without a single line break has no convention to match;
        // fall back to the same platform default the formatter itself uses.
        return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows) ? "\r\n" : "\n";
    }

    private static SyntaxNode NormalizeEndOfLines(SyntaxNode node, string endOfLine)
    {
        var triviaToReplace = node.DescendantTrivia()
            .Where(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)
                && !string.Equals(trivia.ToFullString(), endOfLine, StringComparison.Ordinal))
            .ToList();
        if (triviaToReplace.Count == 0)
        {
            return node;
        }

        return node.ReplaceTrivia(
            triviaToReplace,
            (originalTrivia, rewrittenTrivia) => SyntaxFactory.EndOfLine(endOfLine));
    }
}
