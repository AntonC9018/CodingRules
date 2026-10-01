using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace CodingRules;

/// <summary>Exact framework definitions, resolved once for each compilation.</summary>
internal sealed class PipelineCatalog
{
    private static readonly ConditionalWeakTable<Compilation, PipelineCatalog> Catalogs = new();
    private readonly HashSet<IMethodSymbol> projections = new(SymbolEqualityComparer.Default);
    private readonly HashSet<IMethodSymbol> materializers = new(SymbolEqualityComparer.Default);
    public static PipelineCatalog For(Compilation compilation) => Catalogs.GetValue(compilation, value => new PipelineCatalog(value));

    private PipelineCatalog(Compilation compilation)
    {
        foreach (var name in new[] { "System.Linq.Enumerable", "System.Linq.ImmutableArrayExtensions",
                     "System.Collections.Immutable.ImmutableDictionary", "System.Collections.Immutable.ImmutableSortedDictionary" })
        {
            var type = compilation.GetTypeByMetadataName(name);
            if (type is null || type.Locations.Any(location => location.IsInSource)) continue;
            var assembly = type.ContainingAssembly.Identity;
            if (assembly.PublicKeyToken.IsDefaultOrEmpty || (name.Contains("Immutable")
                    ? assembly.Name != "System.Collections.Immutable"
                    : assembly.Name is not "System.Linq" and not "System.Core" and not "netstandard")) continue;
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>().Where(method => method.IsExtensionMethod && method.IsStatic))
            {
                if (method.Name == "Select" && method.TypeParameters.Length == 2 && method.Parameters.Length == 2
                    && method.Parameters[1].Name == "selector" && IsSelector(method.Parameters[1].Type, indexed: true)
                    && SelectorTypes(method.Parameters[1].Type, method.TypeParameters[0], method.TypeParameters[1])) projections.Add(method);
                var immutableArray = name == "System.Linq.ImmutableArrayExtensions";
                var terminal = name == "System.Linq.Enumerable" ? method.Name is "ToDictionary" or "ToLookup"
                    : immutableArray ? method.Name == "ToDictionary"
                    : name == "System.Collections.Immutable.ImmutableDictionary" ? method.Name == "ToImmutableDictionary"
                    : name == "System.Collections.Immutable.ImmutableSortedDictionary" && method.Name == "ToImmutableSortedDictionary";
                if (!terminal || method.TypeParameters.Length is not 2 and not 3 || method.Parameters.Length < 2
                    || method.Parameters[0].Type.OriginalDefinition.ToDisplayString() != (immutableArray
                        ? "System.Collections.Immutable.ImmutableArray<T>" : "System.Collections.Generic.IEnumerable<T>")) continue;
                if (method.Parameters[1].Name != "keySelector" || !IsSelector(method.Parameters[1].Type, indexed: false)) continue;
                var sourceType = method.TypeParameters[immutableArray ? method.TypeParameters.Length - 1 : 0];
                var keyType = method.TypeParameters[immutableArray ? 0 : 1];
                if (method.Parameters[0].Type is not INamedTypeSymbol source || !SymbolEqualityComparer.Default.Equals(source.TypeArguments[0], sourceType)
                    || !SelectorTypes(method.Parameters[1].Type, sourceType, keyType)) continue;
                if (method.TypeParameters.Length == 3 && (method.Parameters.Length < 3
                        || method.Parameters[2].Name != "elementSelector" || !IsSelector(method.Parameters[2].Type, indexed: false)
                        || !SelectorTypes(method.Parameters[2].Type, sourceType, method.TypeParameters[immutableArray ? 1 : 2]))) continue;
                if (method.Parameters.Skip(method.TypeParameters.Length == 3 ? 3 : 2).Any(parameter => parameter.Name is not "comparer" and not "keyComparer" and not "valueComparer")) continue;
                materializers.Add(method);
            }
        }
    }

    private static bool IsSelector(ITypeSymbol type, bool indexed) => type is INamedTypeSymbol named
        && named.ContainingNamespace.ToDisplayString() == "System" && named.Name == "Func"
        && (named.Arity == 2 || indexed && named.Arity == 3 && named.TypeArguments[1].SpecialType == SpecialType.System_Int32);
    private static bool SelectorTypes(ITypeSymbol selector, ITypeSymbol source, ITypeSymbol result) => selector is INamedTypeSymbol named
        && SymbolEqualityComparer.Default.Equals(named.TypeArguments[0], source) && SymbolEqualityComparer.Default.Equals(named.TypeArguments.Last(), result);
    internal static IMethodSymbol Definition(IMethodSymbol method) => (method.ReducedFrom ?? method).OriginalDefinition;
    public bool IsMaterializer(IMethodSymbol method) => materializers.Contains(Definition(method));
    public bool IsProjection(IMethodSymbol method, IParameterSymbol parameter) => projections.Contains(Definition(method)) && parameter.Name == "selector"
        || IsMaterializer(method) && parameter.Name is "keySelector" or "elementSelector";

    public IMethodSymbol? IdentityPair(IMethodSymbol method)
    {
        if (!IsMaterializer(method) || method.TypeArguments.Length != 2) return null;
        var full = method.ReducedFrom ?? method;
        var candidate = materializers.SingleOrDefault(other => other.ContainingType.Equals(full.ContainingType, SymbolEqualityComparer.Default)
            && other.Name == full.Name && other.TypeParameters.Length == 3 && other.Parameters.Length == full.Parameters.Length + 1
            && other.Parameters.Skip(3).Select(parameter => parameter.Name).SequenceEqual(full.Parameters.Skip(2).Select(parameter => parameter.Name)));
        if (candidate is null) return null;
        return full.ContainingType.Name == "ImmutableArrayExtensions" ? candidate.Construct(method.TypeArguments[0], method.TypeArguments[1], method.TypeArguments[1])
            : candidate.Construct(method.TypeArguments[0], method.TypeArguments[1], method.TypeArguments[0]);
    }
}
