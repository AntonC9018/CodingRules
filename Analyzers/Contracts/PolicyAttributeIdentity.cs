using Microsoft.CodeAnalysis;

namespace CodingRules;

internal static class PolicyAttributeIdentity
{
    public static INamedTypeSymbol? Resolve(Compilation compilation, string metadataName)
    {
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => assembly.Name == "CodingRules.Shared").ToArray();
        if (assemblies.Length != 1 || assemblies[0].Identity.Version != new Version(1, 0, 0, 0)
            || assemblies[0].Identity.PublicKeyToken.Length != 0) return null;
        var type = assemblies[0].GetTypeByMetadataName(metadataName);
        return type is { IsSealed: true, DeclaredAccessibility: Accessibility.Public }
            && SymbolEqualityComparer.Default.Equals(type.BaseType, compilation.GetTypeByMetadataName("System.Attribute"))
            ? type : null;
    }
}
