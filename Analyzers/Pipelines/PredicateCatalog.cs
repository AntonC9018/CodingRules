using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace CodingRules;

/// <summary>The framework Enumerable predicate slots, including indexed Where.</summary>
internal sealed class PredicateCatalog
{
    private static readonly ConditionalWeakTable<Compilation, PredicateCatalog> Catalogs = new();
    private readonly HashSet<IMethodSymbol> methods = new(SymbolEqualityComparer.Default);

    public static PredicateCatalog For(Compilation compilation) =>
        Catalogs.GetValue(compilation, value => new PredicateCatalog(value));

    private PredicateCatalog(Compilation compilation)
    {
        var enumerable = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var func = compilation.GetTypeByMetadataName("System.Func`2");
        var indexedFunc = compilation.GetTypeByMetadataName("System.Func`3");
        var sequence = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        if (enumerable is null || !FrameworkSymbols.IsPlatformAssembly(enumerable.ContainingAssembly))
        {
            return;
        }

        foreach (var method in enumerable.GetMembers().OfType<IMethodSymbol>())
        {
            if (method.Name is not "Any" and not "All" and not "Where"
                || !method.IsExtensionMethod || !method.IsStatic || method.TypeParameters.Length != 1
                || method.Parameters.Length != 2
                || method.Parameters[0].Type is not INamedTypeSymbol source
                || !SymbolEqualityComparer.Default.Equals(source.OriginalDefinition, sequence)
                || !SymbolEqualityComparer.Default.Equals(source.TypeArguments[0], method.TypeParameters[0])
                || method.Parameters[1].Type is not INamedTypeSymbol predicate
                || predicate.TypeArguments.Length is not 2 and not 3)
            {
                continue;
            }

            var indexed = predicate.TypeArguments.Length == 3;
            if (!SymbolEqualityComparer.Default.Equals(predicate.OriginalDefinition, indexed ? indexedFunc : func)
                || !SymbolEqualityComparer.Default.Equals(predicate.TypeArguments[0], method.TypeParameters[0])
                || predicate.TypeArguments[predicate.TypeArguments.Length - 1].SpecialType != SpecialType.System_Boolean
                || indexed && (method.Name != "Where"
                    || predicate.TypeArguments[1].SpecialType != SpecialType.System_Int32))
            {
                continue;
            }

            methods.Add(method);
        }
    }

    public bool Contains(IMethodSymbol method) => methods.Contains(PipelineCatalog.Definition(method));
}
