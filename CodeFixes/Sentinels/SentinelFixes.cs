using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace CodingRules;

internal static class SentinelFixes
{
    public static async Task<Document?> ChangeAsync(Document document, ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        var host = expression.AncestorsAndSelf()
            .Select(node => SentinelHost.Create(node, model, token))
            .FirstOrDefault(item => item is not null);
        if (host is null)
        {
            return null;
        }

        var result = new SentinelSummaries(model.Compilation).Get(host, model, token);
        var site = result.Returns.FirstOrDefault(item => item.Expression.Span == expression.Span);
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (site is null || !SentinelPlan.Feasible(site, host, model, token) || root is null)
        {
            return null;
        }

        var proposed = document.WithSyntaxRoot(Rewrite(root, host, site, expression, model, token));
        proposed = await GeneratedSyntaxFormatter.FormatAsync(proposed, document, token).ConfigureAwait(false);
        var saved = document.WithText(await proposed.GetTextAsync(token).ConfigureAwait(false));
        var afterModel = await saved.GetSemanticModelAsync(token).ConfigureAwait(false);
        if (afterModel is null)
        {
            return null;
        }

        var afterRoot = afterModel.SyntaxTree.GetRoot(token);
        var preservesBehavior = CallsKeepBindings(root, model, afterRoot, afterModel, token)
            && SameCompilerMessages(model, afterModel, token)
            && ReducesSentinelSites(host, result, model, afterRoot, afterModel, token);
        return preservesBehavior ? saved : null;
    }

    // A local that already holds the sentinel is returned as -1 directly;
    // otherwise the value is guarded so a -1 result is returned explicitly.
    private static SyntaxNode Rewrite(SyntaxNode root, SentinelHost host, SentinelReturn site, ExpressionSyntax expression,
        SemanticModel model, CancellationToken token)
    {
        var sentinel = PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)));
        var isLocal = SentinelSearch.Operation(expression, model, token) is ILocalReferenceOperation { Local.RefKind: RefKind.None };
        if (isLocal && site.Value!.Outcomes == SearchOutcomes.Sentinel && expression.Parent is ReturnStatementSyntax)
        {
            return root.ReplaceNode(expression, sentinel.WithTriviaFrom(expression));
        }

        var statements = new List<StatementSyntax>();
        ExpressionSyntax value;
        if (isLocal)
        {
            value = expression.WithoutTrivia();
        }
        else
        {
            var scope = host.Declaration.FirstAncestorOrSelf<TypeDeclarationSyntax>() ?? host.Declaration;
            var name = new EvaluationNames(scope, model).Fresh("sentinelResult");
            value = IdentifierName(name);
            statements.Add(LocalDeclarationStatement(VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword)))
                .WithVariables(SingletonSeparatedList(VariableDeclarator(name).WithInitializer(EqualsValueClause(expression.WithoutTrivia())))))
                .WithAdditionalAnnotations(Formatter.Annotation));
        }

        statements.Add(IfStatement(BinaryExpression(SyntaxKind.EqualsExpression, value, sentinel), Block(ReturnStatement(sentinel)))
            .WithAdditionalAnnotations(Formatter.Annotation));
        statements.Add(ReturnStatement(value).WithAdditionalAnnotations(Formatter.Annotation));
        return expression.Parent is ReturnStatementSyntax returned
            ? ReplaceReturn(root, returned, statements)
            : ReplaceExpressionBody(root, host, statements);
    }

    private static SyntaxNode ReplaceReturn(SyntaxNode root, ReturnStatementSyntax returned, List<StatementSyntax> statements)
    {
        statements[0] = statements[0].WithLeadingTrivia(returned.GetLeadingTrivia().AddRange(returned.ReturnKeyword.TrailingTrivia));
        statements[statements.Count - 1] = statements[statements.Count - 1].WithTrailingTrivia(returned.GetTrailingTrivia());
        if (returned.Parent is not BlockSyntax block)
        {
            return root.ReplaceNode(returned, Block(statements));
        }

        var index = block.Statements.IndexOf(returned);
        return root.ReplaceNode(block, block.WithStatements(block.Statements.RemoveAt(index).InsertRange(index, statements)));
    }

    // The returned expression is the host's whole expression body.
    private static SyntaxNode ReplaceExpressionBody(SyntaxNode root, SentinelHost host, List<StatementSyntax> statements)
    {
        var expressionBody = host.Body.Parent!;
        if (expressionBody is ArrowExpressionClauseSyntax arrow)
        {
            statements[0] = statements[0].WithLeadingTrivia(arrow.ArrowToken.TrailingTrivia);
        }

        // The removed semicolon carried the declaration's trailing trivia.
        var block = Block(statements)
            .WithTrailingTrivia(host.Declaration.GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);
        return ConditionSiteRewrites.ReplaceExpressionBody(root, expressionBody, block);
    }

    // Every original call is compared in source order, including neighboring
    // bodies and implicit caller constants. New guards contain no calls.
    private static bool CallsKeepBindings(SyntaxNode root, SemanticModel model, SyntaxNode afterRoot, SemanticModel afterModel,
        CancellationToken token)
    {
        var beforeCalls = root.DescendantNodes().Where(Occurrence).Select(node => BindingIdentity.Call(model.GetOperation(node, token)));
        var afterCalls = afterRoot.DescendantNodes().Where(Occurrence).Select(node => BindingIdentity.Call(afterModel.GetOperation(node, token)));
        return beforeCalls.SequenceEqual(afterCalls);
    }

    private static bool SameCompilerMessages(SemanticModel before, SemanticModel after, CancellationToken token) =>
        CompilerMessages(before, token).SequenceEqual(CompilerMessages(after, token));

    private static IEnumerable<string> CompilerMessages(SemanticModel model, CancellationToken token) => model
        .GetDiagnostics(cancellationToken: token)
        .Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
        .Select(item => item.Id + ":" + item.GetMessage())
        .OrderBy(item => item);

    // The rewritten host has the same search outcomes and fewer fixable sentinel returns.
    private static bool ReducesSentinelSites(SentinelHost host, SentinelResult result, SemanticModel model, SyntaxNode afterRoot,
        SemanticModel afterModel, CancellationToken token)
    {
        var afterDeclaration = afterRoot.DescendantNodesAndSelf()
            .FirstOrDefault(node => node.RawKind == host.Declaration.RawKind && node.SpanStart == host.Declaration.SpanStart);
        var afterHost = afterDeclaration is null ? null : SentinelHost.Create(afterDeclaration, afterModel, token);
        if (afterHost is null)
        {
            return false;
        }

        var afterResult = new SentinelSummaries(afterModel.Compilation).Get(afterHost, afterModel, token);
        var beforeSites = result.Returns.Count(item => SentinelPlan.Feasible(item, host, model, token));
        var afterSites = afterResult.Returns.Count(item => SentinelPlan.Feasible(item, afterHost, afterModel, token));
        return afterSites < beforeSites && afterResult.Outcomes == result.Outcomes;
    }

    private static bool Occurrence(SyntaxNode node) => NamedArgumentSite.IsHost(node) || node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax;
}
