using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class NamedArgumentPlan
{
    public NamedArgumentSite Site { get; }
    public ImmutableArray<int> Indices { get; }
    private NamedArgumentPlan(NamedArgumentSite site, ImmutableArray<int> indices) { Site = site; Indices = indices; }

    public static NamedArgumentPlan? Create(SyntaxNode owner, SemanticModel model, AnalyzerConfigOptionsProvider options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var site = NamedArgumentSite.Create(owner, model, token);
        if (site is null || owner.ContainsDiagnostics || owner.ContainsDirectives
            || owner is ExpressionSyntax expression && ConditionHosts.IsInsideExpressionTree(expression, model)
            || model.GetDiagnostics(owner.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            || NamedArgumentPolicy.For(model.Compilation).Exempt(site.Method, owner.SyntaxTree, options, token)) return null;
        var indices = ImmutableArray.CreateBuilder<int>();
        var groups = site.BoundArguments.Select(argument => argument.Parameter!).GroupBy(parameter => GroupType(parameter.Type), SymbolEqualityComparer.Default);
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            if (group.Key is not ITypeSymbol { TypeKind: not TypeKind.Error and not TypeKind.Dynamic }
                || group.Distinct(SymbolEqualityComparer.Default).Count() < 2) continue;
            for (var index = 0; index < site.Arguments.Length; index++)
                if (!NamedArgumentSite.Named(site.Arguments[index]) && SymbolEqualityComparer.Default.Equals(GroupType(site.BoundArguments[index].Parameter!.Type), group.Key)) indices.Add(index);
        }
        if (indices.Count == 0) return null;
        if (((CSharpParseOptions)owner.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp7_2)
            for (var index = indices.Min(); index < site.Arguments.Length; index++)
                if (!NamedArgumentSite.Named(site.Arguments[index]) && !indices.Contains(index)) indices.Add(index);
        var ordered = indices.OrderBy(index => index).ToImmutableArray();
        if (ordered.Any(index => site.BoundArguments[index].ArgumentKind == ArgumentKind.ParamArray
            || !SyntaxFacts.IsValidIdentifier(site.BoundArguments[index].Parameter!.Name)
                && SyntaxFacts.GetKeywordKind(site.BoundArguments[index].Parameter!.Name) == SyntaxKind.None)) return null;
        if (NamedArgumentObservers.Affected(owner, model, token) || !Correspondence(site, ordered, model, token)) return null;
        return new NamedArgumentPlan(site, ordered);
    }

    public SyntaxNode Rewrite() => Site.Owner.ReplaceNodes(Indices.Select(index => Site.Arguments[index]), (old, _) =>
        NamedArgumentSite.Name(old, Site.BoundArguments[Site.Arguments.IndexOf(old)].Parameter!.Name));

    private static ITypeSymbol GroupType(ITypeSymbol type) => type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } ? underlying : type;

    // Naming leaves the original argument correspondence applicable. It can only
    // enable a new overload by changing that overload's source-to-parameter map.
    // Reject any alternative with such a remap, even if isolated speculation
    // would happen to reject it after losing target typing or outer inference.
    // This proof works in primary bases, attributes and other declaration contexts
    // without constructing a replacement compilation for an analyzer candidate.
    private static bool Correspondence(NamedArgumentSite site, ImmutableArray<int> indices, SemanticModel model, CancellationToken token)
    {
        IEnumerable<IMethodSymbol> alternatives;
        if (site.Method.MethodKind == MethodKind.Constructor) alternatives = site.Method.ContainingType.InstanceConstructors;
        else if (site.Method.MethodKind == MethodKind.DelegateInvoke || site.Method.MethodKind == MethodKind.LocalFunction) alternatives = new[] { site.Method };
        else if (site.Owner is InvocationExpressionSyntax call) alternatives = model.GetMemberGroup(call.Expression, token).OfType<IMethodSymbol>();
        else return false;
        var found = false;
        foreach (var alternative in alternatives)
        {
            token.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(alternative, site.Method)) found = true;
            if (SymbolEqualityComparer.Default.Equals(NamedArgumentPolicy.Declaration(alternative), NamedArgumentPolicy.Declaration(site.Method))) continue;
            foreach (var index in indices)
            {
                var original = site.BoundArguments[index].Parameter!;
                var parameter = (alternative.ReducedFrom ?? alternative).Parameters.FirstOrDefault(value => value.Name == original.Name);
                if (parameter is not null && parameter.Ordinal != original.Ordinal) return false;
            }
        }
        // Reduced symbols returned by GetMemberGroup may be unconstructed; their
        // declaration equality is sufficient because substitution is unchanged.
        return found || alternatives.Any(alternative => SymbolEqualityComparer.Default.Equals(
            NamedArgumentPolicy.Declaration(alternative), NamedArgumentPolicy.Declaration(site.Method)));
    }
}
