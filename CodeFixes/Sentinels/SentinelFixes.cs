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
        var host = expression.AncestorsAndSelf().Select(node => SentinelHost.Create(node, model, token)).FirstOrDefault(item => item is not null);
        if (host is null) return null;
        var result = new SentinelSummaries(model.Compilation).Get(host, model, token);
        var site = result.Returns.FirstOrDefault(item => item.Expression.Span == expression.Span);
        if (site is null || !SentinelPlan.Feasible(site, host, model, token)) return null;
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (root is null) return null;
        var literal = PrefixUnaryExpression(SyntaxKind.UnaryMinusExpression, LiteralExpression(SyntaxKind.NumericLiteralExpression, Literal(1)));
        SyntaxNode changed;
        if (site.Value!.Outcomes == SearchOutcomes.Sentinel && expression.Parent is ReturnStatementSyntax)
            changed = root.ReplaceNode(expression, literal.WithTriviaFrom(expression));
        else
        {
            var local = SentinelSearch.Operation(expression, model, token) is ILocalReferenceOperation;
            var name = new EvaluationNames(host.Declaration.FirstAncestorOrSelf<TypeDeclarationSyntax>() ?? host.Declaration, model).Fresh("sentinelResult");
            var value = local ? expression.WithoutTrivia() : IdentifierName(name);
            var statements = new System.Collections.Generic.List<StatementSyntax>();
            if (!local) statements.Add(LocalDeclarationStatement(VariableDeclaration(PredefinedType(Token(SyntaxKind.IntKeyword)))
                .WithVariables(SingletonSeparatedList(VariableDeclarator(name).WithInitializer(EqualsValueClause(expression.WithoutTrivia()))))));
            statements.Add(IfStatement(BinaryExpression(SyntaxKind.EqualsExpression, value, literal), Block(ReturnStatement(literal))));
            statements.Add(ReturnStatement(value));
            for (var index = 0; index < statements.Count; index++) statements[index] = statements[index].WithAdditionalAnnotations(Formatter.Annotation);
            if (expression.Parent is ReturnStatementSyntax returned)
            {
                statements[0] = statements[0].WithLeadingTrivia(returned.GetLeadingTrivia().AddRange(returned.ReturnKeyword.TrailingTrivia));
                statements[statements.Count - 1] = statements.Last().WithTrailingTrivia(returned.GetTrailingTrivia());
                changed = returned.Parent switch
                {
                    BlockSyntax block => root.ReplaceNode(block, block.WithStatements(block.Statements.RemoveAt(block.Statements.IndexOf(returned))
                        .InsertRange(block.Statements.IndexOf(returned), statements))),
                    _ => root.ReplaceNode(returned, Block(statements)),
                };
            }
            else
            {
                if (host.Body.Parent is ArrowExpressionClauseSyntax arrow)
                    statements[0] = statements[0].WithLeadingTrivia(arrow.ArrowToken.TrailingTrivia);
                var block = Block(statements).WithTrailingTrivia(host.Declaration.GetTrailingTrivia()).WithAdditionalAnnotations(Formatter.Annotation);
                SyntaxNode? declaration = host.Declaration switch
                {
                    MethodDeclarationSyntax method => method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
                    LocalFunctionStatementSyntax function => function.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
                    OperatorDeclarationSyntax op => op.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
                    ConversionOperatorDeclarationSyntax op => op.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
                    AccessorDeclarationSyntax accessor => accessor.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block),
                    PropertyDeclarationSyntax property => property.WithExpressionBody(null).WithSemicolonToken(default)
                        .WithAccessorList(AccessorList(SingletonList(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(block)))),
                    IndexerDeclarationSyntax indexer => indexer.WithExpressionBody(null).WithSemicolonToken(default)
                        .WithAccessorList(AccessorList(SingletonList(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(block)))),
                    SimpleLambdaExpressionSyntax lambda => lambda.WithBody(block),
                    ParenthesizedLambdaExpressionSyntax lambda => lambda.WithBody(block),
                    _ => null,
                };
                if (declaration is null) return null;
                changed = root.ReplaceNode(host.Declaration, declaration);
            }
        }
        var proposed = document.WithSyntaxRoot(changed);
        var options = await proposed.GetOptionsAsync(token).ConfigureAwait(false);
        var originalText = (await document.GetTextAsync(token).ConfigureAwait(false)).ToString();
        var firstNewline = originalText.IndexOf('\n');
        var newline = firstNewline > 0 && originalText[firstNewline - 1] == '\r' ? "\r\n" : "\n";
        var config = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(expression.SyntaxTree);
        if (config.TryGetValue("end_of_line", out var configured)) newline = configured switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" };
        proposed = await Formatter.FormatAsync(proposed, Formatter.Annotation,
            options.WithChangedOption(FormattingOptions.NewLine, document.Project.Language, newline), token).ConfigureAwait(false);
        var text = await proposed.GetTextAsync(token).ConfigureAwait(false);
        proposed = document.WithText(text);
        var afterModel = await proposed.GetSemanticModelAsync(token).ConfigureAwait(false);
        var afterRoot = await proposed.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (afterModel is null || afterRoot is null) return null;
        // Every original call is compared in source order, including neighboring
        // bodies and implicit caller constants. New guards contain no calls.
        var beforeCalls = root.DescendantNodes().Where(Occurrence).Select(node => Binding(model.GetOperation(node, token))).ToArray();
        var afterCalls = afterRoot.DescendantNodes().Where(Occurrence).Select(node => Binding(afterModel.GetOperation(node, token))).ToArray();
        if (!beforeCalls.SequenceEqual(afterCalls)) return null;
        var beforeErrors = model.GetDiagnostics(cancellationToken: token).Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(item => item.Id + ":" + item.GetMessage()).OrderBy(item => item).ToArray();
        var afterErrors = afterModel.GetDiagnostics(cancellationToken: token).Where(item => item.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(item => item.Id + ":" + item.GetMessage()).OrderBy(item => item).ToArray();
        if (!beforeErrors.SequenceEqual(afterErrors)) return null;
        var afterDeclaration = afterRoot.DescendantNodesAndSelf().FirstOrDefault(node => node.RawKind == host.Declaration.RawKind && node.SpanStart == host.Declaration.SpanStart);
        var afterHost = afterDeclaration is null ? null : SentinelHost.Create(afterDeclaration, afterModel, token);
        if (afterHost is null) return null;
        var afterResult = new SentinelSummaries(afterModel.Compilation).Get(afterHost, afterModel, token);
        var beforeSites = result.Returns.Count(item => SentinelPlan.Feasible(item, host, model, token));
        var afterSites = afterResult.Returns.Count(item => SentinelPlan.Feasible(item, afterHost, afterModel, token));
        if (afterSites >= beforeSites || afterResult.Outcomes != result.Outcomes) return null;
        return proposed;
    }
    private static bool Occurrence(SyntaxNode node) => NamedArgumentSite.IsHost(node) || node is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax;
    private static string Binding(IOperation? operation)
    {
        if (operation is IAttributeOperation attribute) operation = attribute.Operation;
        var symbol = operation switch { IInvocationOperation call => (ISymbol)call.TargetMethod, IObjectCreationOperation creation => creation.Constructor,
            IPropertyReferenceOperation indexer => indexer.Property, _ => null };
        var arguments = operation switch { IInvocationOperation call => call.Arguments, IObjectCreationOperation creation => creation.Arguments,
            IPropertyReferenceOperation indexer => indexer.Arguments, _ => default };
        var receiver = operation switch { IInvocationOperation call => call.Instance, IPropertyReferenceOperation property => property.Instance, _ => null };
        if (arguments.IsDefault) return NamedArgumentIdentity.Operation(operation);
        return NamedArgumentIdentity.Symbol(symbol) + ":" + NamedArgumentIdentity.Type(receiver?.Type)
            + ":" + (operation is IInvocationOperation invocation && invocation.IsVirtual) + ":"
            + string.Join(";", arguments.Select(argument => NamedArgumentIdentity.Symbol(argument.Parameter) + ":" + argument.ArgumentKind
                + ":" + NamedArgumentIdentity.Conversion(argument.InConversion) + ":" + NamedArgumentIdentity.Conversion(argument.OutConversion)
                + ":" + NamedArgumentIdentity.Type(argument.Value.Type) + ":" + argument.Value.ConstantValue));
    }
}
