using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class BuilderPolicy
{
    private const string SuffixProperty = "build_property.CodingRulesBuilderSuffixes";
    private static readonly ImmutableArray<string> DefaultSuffixes = ImmutableArray.Create("Builder", "Descriptor", "Configurator");
    private readonly ImmutableArray<string> suffixes;
    private readonly INamedTypeSymbol? marker;

    private BuilderPolicy(Compilation compilation, AnalyzerConfigOptionsProvider options)
    {
        suffixes = ReadSuffixes(options.GlobalOptions);
        marker = ResolveMarker(compilation);
    }

    public static BuilderPolicy For(Compilation compilation, AnalyzerConfigOptionsProvider options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The policy includes compiler-visible MSBuild options, which can vary between
        // analyzer runs over the same compilation in IDE hosts.
        return new BuilderPolicy(compilation, options);
    }

    public bool IsBuilder(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return false;
        var definition = named.OriginalDefinition;
        var name = definition.MetadataName;
        var arity = name.IndexOf('`');
        if (arity >= 0) name = name.Substring(0, arity);
        if (suffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal))) return true;
        return marker is not null && definition.GetAttributes().Any(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));
    }

    private static ImmutableArray<string> ReadSuffixes(AnalyzerConfigOptions options)
    {
        if (!options.TryGetValue(SuffixProperty, out var configured)) return DefaultSuffixes;
        var values = configured.Split(',').Select(value => value.Trim())
            .Where(value => value.Length != 0).Distinct(StringComparer.Ordinal).ToImmutableArray();
        return values;
    }

    private static INamedTypeSymbol? ResolveMarker(Compilation compilation)
    {
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols
            .Where(assembly => assembly.Name == "CodingRules.Shared").ToArray();
        if (assemblies.Length != 1 || assemblies[0].Identity.Version != new Version(1, 0, 0, 0)
            || assemblies[0].Identity.PublicKeyToken.Length != 0) return null;
        var type = assemblies[0].GetTypeByMetadataName("CodingRules.ConfigurationBuilderAttribute");
        if (type is not { IsSealed: true, DeclaredAccessibility: Accessibility.Public }
            || !SymbolEqualityComparer.Default.Equals(type.BaseType, compilation.GetTypeByMetadataName("System.Attribute"))
            || !type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public)) return null;
        var usages = type.GetAttributes().Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
            compilation.GetTypeByMetadataName("System.AttributeUsageAttribute"))).ToArray();
        if (usages.Length != 1) return null;
        var usage = usages[0];
        if (usage.ConstructorArguments.Length != 1 || !Equals(usage.ConstructorArguments[0].Value, 12)
            || !usage.NamedArguments.Any(pair => pair.Key == "AllowMultiple" && Equals(pair.Value.Value, false))
            || !usage.NamedArguments.Any(pair => pair.Key == "Inherited" && Equals(pair.Value.Value, false))) return null;
        return type;
    }
}
