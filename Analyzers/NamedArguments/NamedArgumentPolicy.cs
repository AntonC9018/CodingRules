using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

internal sealed class NamedArgumentPolicy
{
    internal const string Option = "dotnet_code_quality.CR0500.allow_positional_arguments";
    private static readonly ConditionalWeakTable<Compilation, NamedArgumentPolicy> Policies = new();
    private readonly Compilation compilation;
    private readonly INamedTypeSymbol? marker;
    private readonly ImmutableHashSet<IMethodSymbol> builtins;
    private readonly ConcurrentDictionary<string, ImmutableHashSet<IMethodSymbol>> registrations = new(StringComparer.Ordinal);

    private NamedArgumentPolicy(Compilation compilation)
    {
        this.compilation = compilation;
        var catalogue = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var name in new[] { "System.Math", "System.MathF", "System.String", "System.IO.Path" })
        {
            var declaringType = compilation.GetTypeByMetadataName(name);
            if (declaringType is null) continue;
            foreach (var method in declaringType.GetMembers().OfType<IMethodSymbol>()) if (Builtin(method)) catalogue.Add(method);
        }
        builtins = catalogue.ToImmutable();
        var assemblies = compilation.SourceModule.ReferencedAssemblySymbols.Where(assembly => assembly.Name == "CodingRules.Shared").ToArray();
        if (assemblies.Length != 1 || assemblies[0].Identity.Version != new Version(1, 0, 0, 0)
            || assemblies[0].Identity.PublicKeyToken.Length != 0) return;
        var type = assemblies[0].GetTypeByMetadataName("CodingRules.AllowPositionalArgumentsAttribute");
        if (type is not { IsSealed: true, DeclaredAccessibility: Accessibility.Public }
            || !SymbolEqualityComparer.Default.Equals(type.BaseType, compilation.GetTypeByMetadataName("System.Attribute"))
            || !type.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility == Accessibility.Public)) return;
        var usages = type.GetAttributes().Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
            compilation.GetTypeByMetadataName("System.AttributeUsageAttribute"))).ToArray();
        if (usages.Length != 1) return;
        var usage = usages[0];
        if (usage.ConstructorArguments.Length != 1 || !Equals(usage.ConstructorArguments[0].Value, 96)
            || !usage.NamedArguments.Any(pair => pair.Key == "AllowMultiple" && Equals(pair.Value.Value, false))
            || !usage.NamedArguments.Any(pair => pair.Key == "Inherited" && Equals(pair.Value.Value, false))) return;
        marker = type;
    }

    public static NamedArgumentPolicy For(Compilation compilation) => Policies.GetValue(compilation, value => new NamedArgumentPolicy(value));

    public bool Exempt(IMethodSymbol method, SyntaxTree tree, AnalyzerConfigOptionsProvider options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var declaration = Declaration(method);
        if (builtins.Contains(declaration) || Annotated(declaration)) return true;
        var raw = options.GetOptions(tree).TryGetValue(Option, out var file) ? file
            : options.GlobalOptions.TryGetValue(Option, out var global) ? global : "";
        if (!registrations.TryGetValue(raw, out var resolved))
        {
            resolved = Resolve(raw, token);
            token.ThrowIfCancellationRequested();
            resolved = registrations.GetOrAdd(raw, resolved);
        }
        return resolved.Contains(declaration);
    }

    internal static IMethodSymbol Declaration(IMethodSymbol method)
    {
        var original = (method.ReducedFrom ?? method).OriginalDefinition;
        return original.PartialDefinitionPart ?? original;
    }

    private bool Annotated(IMethodSymbol method) => marker is not null && new[] { method, method.PartialImplementationPart }
        .Where(part => part is not null).Any(part => part!.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker)));

    private ImmutableHashSet<IMethodSymbol> Resolve(string raw, CancellationToken token)
    {
        var result = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var segment in raw.Split('|'))
        {
            token.ThrowIfCancellationRequested();
            var split = segment.IndexOf("::", StringComparison.Ordinal);
            if (split <= 0) continue;
            var assembly = segment.Substring(0, split).Trim();
            var encoded = segment.Substring(split + 2).Trim();
            var id = Decode(encoded);
            if (id is null) continue;
            if (!id.StartsWith("M:", StringComparison.Ordinal)) continue;
            var identities = compilation.SourceModule.ReferencedAssemblySymbols.Append(compilation.Assembly)
                .Where(value => value.Name == assembly).Select(value => value.Identity).Distinct().ToArray();
            if (identities.Length != 1) continue;
            var symbols = DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation);
            token.ThrowIfCancellationRequested();
            var candidates = symbols.OfType<IMethodSymbol>().Select(Declaration)
                .Where(method => method.ContainingAssembly.Name == assembly && Supported(method.MethodKind)
                    && DocumentationCommentId.CreateDeclarationId(method) == id)
                .Distinct(SymbolEqualityComparer.Default).ToArray();
            if (candidates.Length == 1) result.Add((IMethodSymbol)candidates[0]!);
        }
        return result.ToImmutable();
    }

    // Roslyn treats # and ; as inline comments in native analyzer config values.
    // Decode only the ID transport field, after separating entries. The decoded
    // value still must exactly roundtrip through Roslyn's declaration-ID API.
    private static string? Decode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%') continue;
            if (index + 2 >= value.Length || !Uri.IsHexDigit(value[index + 1]) || !Uri.IsHexDigit(value[index + 2])) return null;
            index += 2;
        }
        return Uri.UnescapeDataString(value);
    }

    private static bool Supported(MethodKind kind) => kind is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation
        or MethodKind.Constructor or MethodKind.DelegateInvoke;

    private static bool Builtin(IMethodSymbol method)
    {
        if (method.Locations.Any(location => location.IsInSource) || method.Arity != 0 || method.Parameters.Any(parameter => parameter.RefKind != RefKind.None)) return false;
        var assembly = method.ContainingAssembly.Identity;
        var expected = assembly.Name switch
        {
            "mscorlib" => "b77a5c561934e089", "System.Private.CoreLib" => "7cec85d7bea7798e",
            "System.Runtime" or "System.Runtime.Extensions" or "System.IO.FileSystem.Primitives" => "b03f5f7f11d50a3a",
            "netstandard" => "cc7b13ffcd2ddd51", _ => "",
        };
        if (expected.Length == 0 || string.Concat(assembly.PublicKeyToken.Select(value => value.ToString("x2"))) != expected) return false;
        var type = method.ContainingType.ToDisplayString();
        var parameters = method.Parameters;
        if (type is "System.Math" or "System.MathF")
            return method.IsStatic && method.Name is "Min" or "Max" && parameters.Length == 2
                && SymbolEqualityComparer.Default.Equals(parameters[0].Type, parameters[1].Type)
                && SymbolEqualityComparer.Default.Equals(parameters[0].Type, method.ReturnType)
                && (type == "System.MathF" ? method.ReturnType.SpecialType == SpecialType.System_Single
                    : method.ReturnType.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16
                        or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
                        or SpecialType.System_UInt64 or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or SpecialType.System_Single
                        or SpecialType.System_Double or SpecialType.System_Decimal);
        var types = parameters.Select(parameter => parameter.Type.SpecialType).ToArray();
        if (method.ContainingType.SpecialType == SpecialType.System_String && method.IsStatic && method.Name == "Equals" && method.ReturnType.SpecialType == SpecialType.System_Boolean)
            return types.Length is 2 or 3 && types.Take(2).All(value => value == SpecialType.System_String)
                && (types.Length == 2 || parameters[2].Type.ToDisplayString() == "System.StringComparison");
        if (method.ContainingType.SpecialType == SpecialType.System_String && !method.IsStatic && method.Name == "Replace" && method.ReturnType.SpecialType == SpecialType.System_String)
            return types.Length == 2 && (types.All(value => value == SpecialType.System_Char) || types.All(value => value == SpecialType.System_String))
                || types.Length == 3 && types.Take(2).All(value => value == SpecialType.System_String) && parameters[2].Type.ToDisplayString() == "System.StringComparison"
                || types.Length == 4 && types.Take(2).All(value => value == SpecialType.System_String) && types[2] == SpecialType.System_Boolean
                    && parameters[3].Type is INamedTypeSymbol { MetadataName: "CultureInfo" } culture
                    && culture.ContainingNamespace.ToDisplayString() == "System.Globalization";
        return type == "System.IO.Path" && method.IsStatic && method.Name == "Combine" && method.ReturnType.SpecialType == SpecialType.System_String
            && types.Length is 2 or 3 or 4 && types.All(value => value == SpecialType.System_String);
    }
}
