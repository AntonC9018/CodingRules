using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

/// <summary>A new member can affect lookup in other partials and derived source types.</summary>
internal static class SpanNames
{
    private static readonly ConditionalWeakTable<Compilation, ImmutableHashSet<string>> SourceNames = new();

    public static string MemberName(TypeDeclarationSyntax declaration, SemanticModel model, string stem, CancellationToken token)
    {
        // Build once per compilation, never by replacing a compilation per candidate.
        // Cancellation leaves no incomplete entry in the weak cache.
        if (!SourceNames.TryGetValue(model.Compilation, out var names))
        {
            var builder = ImmutableHashSet.CreateBuilder<string>();
            foreach (var tree in model.Compilation.SyntaxTrees)
            {
                token.ThrowIfCancellationRequested();
                foreach (var identifier in tree.GetRoot(token).DescendantTokens().Where(value => value.IsKind(SyntaxKind.IdentifierToken)))
                {
                    token.ThrowIfCancellationRequested();
                    builder.Add(identifier.ValueText);
                }
            }
            names = SourceNames.GetValue(model.Compilation, _ => builder.ToImmutable());
        }

        var reserved = names.ToBuilder();
        for (var containing = model.GetDeclaredSymbol(declaration, token); containing is not null; containing = containing.ContainingType)
            for (var type = containing; type is not null; type = type.BaseType)
                foreach (var member in type.GetMembers())
                {
                    token.ThrowIfCancellationRequested();
                    reserved.Add(member.Name);
                }
        var name = stem;
        var suffix = 2;
        while (reserved.Contains(name))
        {
            token.ThrowIfCancellationRequested();
            name = stem + suffix++;
        }
        return name;
    }
}
