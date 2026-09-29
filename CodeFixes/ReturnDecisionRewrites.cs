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
    /// trivia of the return are preserved, as is trivia between the return
    /// keyword and its expression.
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

        replacement[0] = WithPreservedDecisionTrivia(replacement[0], returnStatement);
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
                // All sections of a switch statement share one declaration
                // space, so generated local declarations are wrapped in a
                // block that scopes them to their own section (including
                // during Fix-All, where two sections would otherwise emit
                // duplicate declarations).
                var statements = replacement.Any(statement => statement is LocalDeclarationStatementSyntax)
                    ? new List<StatementSyntax> { SyntaxFactory.Block(replacement) }
                    : replacement;
                var newStatements = section.Statements.ReplaceRange(returnStatement, statements);
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

    /// <summary>
    /// Moves the trivia that surrounded the return onto the first generated
    /// statement: the statement's own leading trivia, followed by anything
    /// between the return keyword and its expression (typically a comment
    /// annotating the decision), so no comment is dropped by the rewrite.
    /// </summary>
    private static StatementSyntax WithPreservedDecisionTrivia(
        StatementSyntax firstStatement,
        ReturnStatementSyntax returnStatement)
    {
        var firstToken = firstStatement.GetFirstToken(includeZeroWidth: false);
        var leading = returnStatement.GetLeadingTrivia();
        leading = leading.AddRange(returnStatement.ReturnKeyword.TrailingTrivia);
        leading = leading.AddRange(firstToken.LeadingTrivia);
        return firstStatement.ReplaceToken(firstToken, firstToken.WithLeadingTrivia(leading));
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
        var left = TrimBoundaryLineBreaks(coalesce.Left);

        if (ReturnDecisionAnalysis.Unwrap(coalesce.Right) is ThrowExpressionSyntax throwExpression)
        {
            // The left side is evaluated once into a local, a null guard
            // throws, and the saved value falls through as the result.
            var thrown = TrimBoundaryLineBreaks(throwExpression.Expression);
            var condition = SyntaxFactory.ParseExpression(
                nullableValue ? "!" + name + ".HasValue" : name + " is null");
            var found = SyntaxFactory.ParseExpression(nullableValue ? name + ".Value" : name);
            return new List<StatementSyntax>
            {
                CreateLocal(name, left),
                CreateGuardThrow(condition, thrown),
                SyntaxFactory.ReturnStatement(found),
            };
        }

        var conditionText = nullableValue ? name + ".HasValue" : name + " is not null";
        var foundCondition = SyntaxFactory.ParseExpression(conditionText);
        var foundValue = SyntaxFactory.ParseExpression(nullableValue ? name + ".Value" : name);
        return new List<StatementSyntax>
        {
            CreateLocal(name, left),
            CreateIfReturn(foundCondition, foundValue),
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
        var identifiers = new HashSet<string>();

        // The replacement shares the return's parent block, so every
        // declaration inside that block can conflict with a generated local.
        if (returnStatement.Parent is BlockSyntax insertionBlock)
        {
            AddSubtreeIdentifiers(insertionBlock, identifiers);
        }

        // Every declaration space enclosing the return conflicts as well,
        // including declarations that appear after the return: the space of
        // each enclosing block and, inside a switch statement, the space
        // shared by all sections. Locals inside a section's own blocks are
        // scoped to those blocks (the fix wraps generated section
        // declarations in such blocks), so they cannot collide and are
        // skipped to keep the chosen name stable across Fix-All batches.
        foreach (var ancestor in returnStatement.Ancestors())
        {
            if (ancestor is BlockSyntax enclosingBlock && !ReferenceEquals(enclosingBlock, returnStatement.Parent))
            {
                AddBlockSpaceIdentifiers(enclosingBlock, identifiers);
            }
            else if (ancestor is SwitchStatementSyntax switchStatement)
            {
                AddSwitchSpaceIdentifiers(switchStatement, identifiers);
            }
        }

        var name = "result";
        var suffix = 1;
        while (IsUsedName(symbols, identifiers, name))
        {
            name = "result" + suffix;
            suffix++;
        }

        return name;
    }

    /// <summary>
    /// Collects every identifier in the subtree that can conflict with a
    /// declaration introduced into the block, skipping nested function
    /// bodies whose locals live in their own declaration space.
    /// </summary>
    private static void AddSubtreeIdentifiers(SyntaxNode node, HashSet<string> identifiers)
    {
        foreach (var token in node.DescendantTokens(DescendsIntoConflicts))
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                identifiers.Add(token.ValueText);
            }
        }
    }

    private static bool DescendsIntoConflicts(SyntaxNode node)
    {
        if (node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            return false;
        }

        // A local function's name conflicts, but its body has its own
        // declaration space.
        return node is not BlockSyntax { Parent: LocalFunctionStatementSyntax };
    }

    /// <summary>
    /// Collects the identifiers that belong to the block's own declaration
    /// space: its direct declarations and the variables of expressions in
    /// its direct statements, but not declarations scoped to nested blocks
    /// or nested function bodies.
    /// </summary>
    private static void AddBlockSpaceIdentifiers(BlockSyntax block, HashSet<string> identifiers)
    {
        foreach (var statement in block.Statements)
        {
            switch (statement)
            {
                case BlockSyntax:
                    continue;
                case SwitchStatementSyntax switchStatement:
                    AddSwitchSpaceIdentifiers(switchStatement, identifiers);
                    continue;
                default:
                    AddStatementSpaceIdentifiers(statement, identifiers);
                    continue;
            }
        }
    }

    /// <summary>
    /// Collects the identifiers that belong to the declaration space shared
    /// by a switch statement's sections: case label patterns and each
    /// section's direct declarations.
    /// </summary>
    private static void AddSwitchSpaceIdentifiers(SwitchStatementSyntax switchStatement, HashSet<string> identifiers)
    {
        AddStatementSpaceIdentifiers(switchStatement.Expression, identifiers);
        foreach (var section in switchStatement.Sections)
        {
            foreach (var label in section.Labels)
            {
                AddStatementSpaceIdentifiers(label, identifiers);
            }

            foreach (var statement in section.Statements)
            {
                if (statement is BlockSyntax)
                {
                    continue;
                }

                AddStatementSpaceIdentifiers(statement, identifiers);
            }
        }
    }

    /// <summary>
    /// Collects the identifiers visible around the node without descending
    /// into blocks or nested function bodies, whose declarations live in
    /// their own declaration spaces.
    /// </summary>
    private static void AddStatementSpaceIdentifiers(SyntaxNode node, HashSet<string> identifiers)
    {
        foreach (var token in node.DescendantTokens(DescendsIntoNodeSpace))
        {
            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                identifiers.Add(token.ValueText);
            }
        }
    }

    private static bool DescendsIntoNodeSpace(SyntaxNode node)
    {
        if (node is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            return false;
        }

        return node is not BlockSyntax;
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
