using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

/// <summary>
/// Answers "does the compiler report a problem inside this node?" for rewrite
/// planning. Asking the semantic model per span re-binds and re-analyzes the
/// enclosing member on every call, and plans ask several times per candidate.
/// Each member's diagnostics are therefore computed once per semantic model; a
/// diagnostic inside the node is also inside its member, so filtering the
/// member's diagnostics by span gives the same answer.
/// </summary>
internal static class CompilerDiagnostics
{
    private static readonly ConditionalWeakTable<SemanticModel, ConcurrentDictionary<SyntaxNode, ImmutableArray<Diagnostic>>> Members = new();

    public static bool HasError(SemanticModel model, SyntaxNode node, CancellationToken token) =>
        Within(model, node, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public static bool HasErrorOrWarning(SemanticModel model, SyntaxNode node, CancellationToken token) =>
        Within(model, node, token).Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);

    private static IEnumerable<Diagnostic> Within(SemanticModel model, SyntaxNode node, CancellationToken token)
    {
        var member = node.SyntaxTree == model.SyntaxTree ? Member(node) : null;
        if (member is null)
        {
            return model.GetDiagnostics(node.Span, token);
        }

        var diagnostics = Members.GetValue(model, _ => new ConcurrentDictionary<SyntaxNode, ImmutableArray<Diagnostic>>())
            .GetOrAdd(member, scope => model.GetDiagnostics(scope.Span, token));
        return diagnostics.Where(diagnostic => Intersects(diagnostic, model.SyntaxTree, node.Span));
    }

    private static SyntaxNode? Member(SyntaxNode node) => node.AncestorsAndSelf().FirstOrDefault(ancestor =>
        ancestor is MemberDeclarationSyntax and not BaseTypeDeclarationSyntax and not BaseNamespaceDeclarationSyntax);

    // Mirrors the semantic model's own span filter: any location in this tree that
    // intersects the span.
    private static bool Intersects(Diagnostic diagnostic, SyntaxTree tree, TextSpan span) =>
        new[] { diagnostic.Location }.Concat(diagnostic.AdditionalLocations).Any(location =>
            location.SourceTree == tree && span.IntersectsWith(location.SourceSpan));
}
