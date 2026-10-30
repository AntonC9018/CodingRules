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
        marker = PolicyAttributeIdentity.Resolve(compilation, "CodingRules.ConfigurationBuilderAttribute");
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
}
