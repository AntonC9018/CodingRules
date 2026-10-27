using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

// Roslynator 5.0.1's common fixer discards arrow-token trivia and recognizes
// only framework Task/ValueTask async returns. Exclude those unsafe shapes.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeExpressionBodySuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor ArrowTrivia = new(
        "CRSPR0801", "RCS1016", "Arrow-token comments require manual block conversion.");

    private static readonly SuppressionDescriptor AsyncContract = new(
        "CRSPR0802", "RCS1016", "Custom async return contracts require manual block conversion.");

    public override ImmutableArray<SuppressionDescriptor> SupportedSuppressions =>
        ImmutableArray.Create(ArrowTrivia, AsyncContract);

    public override void ReportSuppressions(SuppressionAnalysisContext context)
    {
        foreach (var diagnostic in context.ReportedDiagnostics)
        {
            if (diagnostic.Id != "RCS1016") continue;
            var tree = diagnostic.Location.SourceTree;
            if (tree is null) continue;
            var root = tree.GetRoot(context.CancellationToken);
            var arrow = root.FindNode(diagnostic.Location.SourceSpan)
                .AncestorsAndSelf().OfType<ArrowExpressionClauseSyntax>().FirstOrDefault();
            if (arrow is null) continue;

            if (HasUnsafeArrowTrivia(arrow))
            {
                context.ReportSuppression(Suppression.Create(ArrowTrivia, diagnostic));
                continue;
            }

            if (HasUnsupportedAsyncReturn(arrow, context.GetSemanticModel(tree), context.CancellationToken))
                context.ReportSuppression(Suppression.Create(AsyncContract, diagnostic));
        }
    }

    // Shared with the exported fixer: CLI hosts can omit diagnostic suppressors.
    public static bool HasUnsafeArrowTrivia(ArrowExpressionClauseSyntax arrow) =>
        arrow.ArrowToken.LeadingTrivia.Concat(arrow.ArrowToken.TrailingTrivia)
            .Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia));

    public static bool HasUnsupportedAsyncReturn(ArrowExpressionClauseSyntax arrow, SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (arrow.Parent is not MethodDeclarationSyntax && arrow.Parent is not LocalFunctionStatementSyntax) return false;
        var method = model.GetDeclaredSymbol(arrow.Parent, cancellationToken) as IMethodSymbol;
        if (method is null || !method.IsAsync || method.ReturnsVoid) return false;
        return !IsSupportedAsyncReturn(method.ReturnType, model.Compilation);
    }

    private static bool IsSupportedAsyncReturn(ITypeSymbol type, Compilation compilation)
    {
        var definition = type.OriginalDefinition;
        return new[]
        {
            "System.Threading.Tasks.Task", "System.Threading.Tasks.Task`1",
            "System.Threading.Tasks.ValueTask", "System.Threading.Tasks.ValueTask`1",
        }.Any(name => SymbolEqualityComparer.Default.Equals(definition, compilation.GetTypeByMetadataName(name)));
    }
}
