using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExplicitReturnDecisionCodeFixProvider))]
[Shared]
public sealed class ExplicitReturnDecisionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.ExplicitReturnDecision);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        var finalReturn = node.FirstAncestorOrSelf<ReturnStatementSyntax>();
        if (finalReturn?.Parent is not BlockSyntax)
        {
            return;
        }

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return;
        }

        if (!ReturnDecisionAnalysis.TryGetKind(finalReturn, semanticModel, context.CancellationToken, out var kind))
        {
            return;
        }

        var action = CodeAction.Create(
            "Make return outcomes explicit",
            cancellationToken => ApplyFixAsync(context.Document, finalReturn, kind, cancellationToken),
            nameof(ExplicitReturnDecisionCodeFixProvider));
        context.RegisterCodeFix(action, diagnostic);
    }

    private static async Task<Document> ApplyFixAsync(
        Document document,
        ReturnStatementSyntax finalReturn,
        ReturnDecisionKind kind,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || semanticModel is null || finalReturn.Parent is not BlockSyntax block)
        {
            return document;
        }

        var replacement = CreateReplacement(finalReturn, kind, semanticModel);
        if (replacement.Count == 0)
        {
            return document;
        }

        replacement[0] = replacement[0].WithLeadingTrivia(finalReturn.GetLeadingTrivia());
        var lastIndex = replacement.Count - 1;
        replacement[lastIndex] = replacement[lastIndex].WithTrailingTrivia(finalReturn.GetTrailingTrivia());

        var newStatements = block.Statements.ReplaceRange(finalReturn, replacement);
        var newBlock = block.WithStatements(newStatements).WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(block, newBlock);
        return document.WithSyntaxRoot(newRoot);
    }

    private static List<StatementSyntax> CreateReplacement(
        ReturnStatementSyntax finalReturn,
        ReturnDecisionKind kind,
        SemanticModel semanticModel)
    {
        var expression = ReturnDecisionAnalysis.Unwrap(finalReturn.Expression!);
        switch (kind)
        {
            case ReturnDecisionKind.Conditional:
                return CreateConditionalReplacement((ConditionalExpressionSyntax)expression, semanticModel);
            case ReturnDecisionKind.Coalesce:
                return CreateCoalesceReplacement(
                    (BinaryExpressionSyntax)expression,
                    semanticModel,
                    finalReturn);
            case ReturnDecisionKind.Boolean:
                return CreateBooleanReplacement(expression);
            case ReturnDecisionKind.NullableCall:
                return CreateNullableCallReplacement(expression, semanticModel, finalReturn);
            default:
                return new List<StatementSyntax>();
        }
    }

    private static List<StatementSyntax> CreateConditionalReplacement(
        ConditionalExpressionSyntax conditional,
        SemanticModel semanticModel)
    {
        var condition = conditional.Condition;
        var found = conditional.WhenTrue;
        var fallback = conditional.WhenFalse;
        if (ReturnDecisionAnalysis.IsFallback(found) && !ReturnDecisionAnalysis.IsFallback(fallback))
        {
            condition = Negate(condition, semanticModel);
            found = conditional.WhenFalse;
            fallback = conditional.WhenTrue;
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
        ReturnStatementSyntax finalReturn)
    {
        var name = FindAvailableName(semanticModel, finalReturn);
        var leftType = semanticModel.GetTypeInfo(coalesce.Left).Type;
        var nullableValue = ReturnDecisionAnalysis.IsNullableValueType(leftType);
        var conditionText = nullableValue ? name + ".HasValue" : name + " is not null";
        var foundText = nullableValue ? name + ".Value" : name;
        var condition = SyntaxFactory.ParseExpression(conditionText);
        var found = SyntaxFactory.ParseExpression(foundText);
        return new List<StatementSyntax>
        {
            CreateLocal(name, coalesce.Left),
            CreateIfReturn(condition, found),
            SyntaxFactory.ReturnStatement(coalesce.Right),
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
        ReturnStatementSyntax finalReturn)
    {
        var name = FindAvailableName(semanticModel, finalReturn);
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

    private static LocalDeclarationStatementSyntax CreateLocal(string name, ExpressionSyntax value)
    {
        var initializer = SyntaxFactory.EqualsValueClause(value);
        var variable = SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(name));
        variable = variable.WithInitializer(initializer);
        var variables = SyntaxFactory.SingletonSeparatedList(variable);
        var declaration = SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"), variables);
        return SyntaxFactory.LocalDeclarationStatement(declaration);
    }

    private static string FindAvailableName(SemanticModel semanticModel, ReturnStatementSyntax finalReturn)
    {
        var symbols = semanticModel.LookupSymbols(finalReturn.SpanStart);
        var block = (BlockSyntax)finalReturn.Parent!;
        var names = block.DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(token => token.ValueText);
        var identifiers = new HashSet<string>(names);
        var name = "result";
        var suffix = 1;
        while (symbols.Any(symbol => symbol.Name == name) || identifiers.Contains(name))
        {
            name = "result" + suffix;
            suffix++;
        }

        return name;
    }

    private static ExpressionSyntax Negate(ExpressionSyntax condition, SemanticModel semanticModel)
    {
        var unwrapped = ReturnDecisionAnalysis.Unwrap(condition);
        if (unwrapped is BinaryExpressionSyntax binary
            && semanticModel.GetOperation(binary) is IBinaryOperation { OperatorMethod: null }
            && TryGetInverse(binary, out var inverse, out var token))
        {
            var operatorToken = SyntaxFactory.Token(token).WithTriviaFrom(binary.OperatorToken);
            return SyntaxFactory.BinaryExpression(inverse, binary.Left, operatorToken, binary.Right);
        }

        var parenthesized = SyntaxFactory.ParenthesizedExpression(condition);
        return SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, parenthesized);
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
}
