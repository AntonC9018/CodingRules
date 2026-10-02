using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace CodingRules;

/// <summary>Only the exact APIs present in the consumer's framework references.</summary>
internal sealed class SpanCatalog
{
    private static readonly ConditionalWeakTable<Compilation, SpanCatalog> Catalogs = new();
    private readonly Dictionary<IMethodSymbol, IMethodSymbol> trims = new(SymbolEqualityComparer.Default);
    public IMethodSymbol? FullSpan { get; }
    public IMethodSymbol? ConstantSpan { get; }
    public IMethodSymbol? Equal { get; }
    public IPropertySymbol? Length { get; }
    public static SpanCatalog For(Compilation compilation) => Catalogs.GetValue(compilation, value => new SpanCatalog(value));

    private SpanCatalog(Compilation compilation)
    {
        var text = compilation.GetSpecialType(SpecialType.System_String);
        var character = compilation.GetSpecialType(SpecialType.System_Char);
        var integer = compilation.GetSpecialType(SpecialType.System_Int32);
        var boolean = compilation.GetSpecialType(SpecialType.System_Boolean);
        var span = compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
        var extensions = compilation.GetTypeByMetadataName("System.MemoryExtensions");
        if (!Framework(text) || !Framework(span) || !Framework(extensions)) return;
        var charSpan = span!.Construct(character);
        var methods = extensions!.GetMembers().OfType<IMethodSymbol>().Where(method => method.IsStatic && method.IsExtensionMethod
            && method.Parameters.All(parameter => parameter.RefKind == RefKind.None)).ToArray();
        FullSpan = methods.SingleOrDefault(method => method.Name == "AsSpan" && method.Arity == 0
            && Same(method.ReturnType, charSpan) && Parameters(method, text, integer, integer));
        if (FullSpan is null) return;
        ConstantSpan = methods.SingleOrDefault(method => method.Name == "AsSpan" && method.Arity == 0
            && Same(method.ReturnType, charSpan) && Parameters(method, text));
        var equal = methods.SingleOrDefault(method => method.Name == "SequenceEqual" && method.Arity == 1
            && Same(method.ReturnType, boolean) && Parameters(method, span.Construct(method.TypeParameters[0]), span.Construct(method.TypeParameters[0])));
        Equal = equal?.Construct(character);
        Length = text.GetMembers("Length").OfType<IPropertySymbol>().SingleOrDefault(property => !property.IsStatic
            && property.Parameters.Length == 0 && Same(property.Type, integer));
        foreach (var name in new[] { "Trim", "TrimStart", "TrimEnd" })
        {
            var original = text.GetMembers(name).OfType<IMethodSymbol>().SingleOrDefault(method => !method.IsStatic && method.Arity == 0
                && method.Parameters.Length == 0 && Same(method.ReturnType, text));
            var replacement = methods.SingleOrDefault(method => method.Name == name && method.Arity == 0
                && Same(method.ReturnType, charSpan) && Parameters(method, charSpan));
            if (original is not null && replacement is not null) trims.Add(original, replacement);
        }
    }

    public IMethodSymbol? Replacement(IMethodSymbol method) => trims.TryGetValue(method, out var replacement) ? replacement : null;
    private static bool Parameters(IMethodSymbol method, params ITypeSymbol[] types) => method.Parameters.Length == types.Length
        && method.Parameters.Select(parameter => parameter.Type).Zip(types, Same).All(value => value);
    private static bool Same(ITypeSymbol left, ITypeSymbol right) => SymbolEqualityComparer.Default.Equals(left, right);
    private static bool Framework(INamedTypeSymbol? type) => type is not null && !type.Locations.Any(location => location.IsInSource)
        && type.ContainingAssembly.Identity.Name is "System.Private.CoreLib" or "mscorlib" or "System.Runtime" or "netstandard" or "System.Memory"
        && new[] { "7cec85d7bea7798e", "b77a5c561934e089", "b03f5f7f11d50a3a", "cc7b13ffcd2ddd51" }
            .Contains(string.Concat(type.ContainingAssembly.Identity.PublicKeyToken.Select(value => value.ToString("x2"))));
}
