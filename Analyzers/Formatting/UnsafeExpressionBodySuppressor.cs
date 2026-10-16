using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

// Roslynator 5.0.1 discards arrow-token trivia and recognizes only framework
// Task/ValueTask async returns. Keep those unsafe conversions out of RCS1016.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnsafeExpressionBodySuppressor : DiagnosticSuppressor
{
    private static readonly SuppressionDescriptor ArrowTrivia = new(
        "CRSPR0801", "RCS1016", "The bundled fixer does not preserve arrow-token comments.");

    private static readonly SuppressionDescriptor AsyncContract = new(
        "CRSPR0802", "RCS1016", "The bundled fixer does not support custom async return contracts.");

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

            if (arrow.ArrowToken.LeadingTrivia.Concat(arrow.ArrowToken.TrailingTrivia)
                .Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            {
                context.ReportSuppression(Suppression.Create(ArrowTrivia, diagnostic));
                continue;
            }

            var model = context.GetSemanticModel(tree);
            if (arrow.Parent is not MethodDeclarationSyntax && arrow.Parent is not LocalFunctionStatementSyntax) continue;
            var method = model.GetDeclaredSymbol(arrow.Parent, context.CancellationToken) as IMethodSymbol;
            if (method is null || !method.IsAsync || method.ReturnsVoid) continue;
            if (IsSupportedAsyncReturn(method.ReturnType, context.Compilation)) continue;
            context.ReportSuppression(Suppression.Create(AsyncContract, diagnostic));
        }
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
