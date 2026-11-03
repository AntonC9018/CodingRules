using Microsoft.CodeAnalysis;

namespace CodingRules;

/// <summary>
/// Resolves the policy markers that consumers apply from CodingRules.Shared.
/// Identity is the assembly name and the marker's shape, not its version or
/// signing, so a later version or signing change cannot silently disable every
/// consumer marker. An ambiguous reference resolves to no marker.
/// </summary>
internal static class PolicyAttributeIdentity
{
    private const string AssemblyName = "CodingRules.Shared";

    public static INamedTypeSymbol? Resolve(Compilation compilation, string metadataName)
    {
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => assembly.Name == AssemblyName)
            .Take(2)
            .ToArray();
        if (assemblies.Length != 1)
        {
            return null;
        }

        var type = assemblies[0].GetTypeByMetadataName(metadataName);
        var isMarker = type is { IsSealed: true, DeclaredAccessibility: Accessibility.Public }
            && SymbolEqualityComparer.Default.Equals(type.BaseType, compilation.GetTypeByMetadataName("System.Attribute"));
        return isMarker ? type : null;
    }
}
