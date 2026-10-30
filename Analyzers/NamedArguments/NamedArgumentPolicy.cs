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
    private readonly string assemblyRegistrations;
    private readonly ImmutableHashSet<IMethodSymbol> builtins;
    private readonly ConcurrentDictionary<string, ImmutableHashSet<IMethodSymbol>> registrations = new(StringComparer.Ordinal);

    private NamedArgumentPolicy(Compilation compilation)
    {
        this.compilation = compilation;
        builtins = PositionalFrameworkMethods.Collect(compilation);
        marker = PolicyAttributeIdentity.Resolve(compilation, "CodingRules.AllowPositionalArgumentsAttribute");
        assemblyRegistrations = AssemblyRegistrations(compilation);
    }

    public static NamedArgumentPolicy For(Compilation compilation) => Policies.GetValue(compilation, value => new NamedArgumentPolicy(value));

    public bool Exempt(IMethodSymbol method, SyntaxTree tree, AnalyzerConfigOptionsProvider options, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var declaration = Declaration(method);
        if (builtins.Contains(declaration) || EqualityName(declaration) || Annotated(declaration))
        {
            return true;
        }

        return Registered(ConfiguredRegistrations(tree, options), token).Contains(declaration);
    }

    // Registrations from the most specific configuration source, followed by the
    // assembly's own [AllowExternalPositionalArguments] entries.
    private string ConfiguredRegistrations(SyntaxTree tree, AnalyzerConfigOptionsProvider options)
    {
        var configured = options.GetOptions(tree).TryGetValue(Option, out var file) ? file
            : options.GlobalOptions.TryGetValue(Option, out var global) ? global
            : options.GlobalOptions.TryGetValue("build_property.CodingRulesAllowPositionalArguments", out var property) ? property
            : "";
        return configured.Length == 0 ? assemblyRegistrations : configured + "|" + assemblyRegistrations;
    }

    private ImmutableHashSet<IMethodSymbol> Registered(string raw, CancellationToken token)
    {
        if (registrations.TryGetValue(raw, out var resolved))
        {
            return resolved;
        }

        resolved = Resolve(raw, token);
        token.ThrowIfCancellationRequested();
        return registrations.GetOrAdd(raw, resolved);
    }

    private static string AssemblyRegistrations(Compilation compilation)
    {
        var external = PolicyAttributeIdentity.Resolve(compilation, "CodingRules.AllowExternalPositionalArgumentsAttribute");
        if (external is null)
        {
            return "";
        }

        var entries = compilation.Assembly.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, external)
                && attribute.ConstructorArguments.Length == 2
                && attribute.ConstructorArguments.All(argument => argument.Value is string))
            .Select(attribute =>
            {
                var assembly = (string)attribute.ConstructorArguments[0].Value!;
                var id = (string)attribute.ConstructorArguments[1].Value!;
                return assembly + "::" + Uri.EscapeDataString(id);
            });
        return string.Join("|", entries);
    }

    internal static IMethodSymbol Declaration(IMethodSymbol method)
    {
        var original = (method.ReducedFrom ?? method).OriginalDefinition;
        return original.PartialDefinitionPart ?? original;
    }

    private static bool EqualityName(IMethodSymbol method) => method.ReturnType.SpecialType == SpecialType.System_Boolean
        && method.Name.StartsWith("Equals", StringComparison.Ordinal)
        && (method.Name.Length == 6 || method.Name.Substring(6).All(character => character >= '0' && character <= '9'));

    private bool Annotated(IMethodSymbol method)
    {
        if (marker is null)
        {
            return false;
        }

        return HasMarker(method) || method.PartialImplementationPart is { } implementation && HasMarker(implementation);
    }

    private bool HasMarker(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, marker));

    private ImmutableHashSet<IMethodSymbol> Resolve(string raw, CancellationToken token)
    {
        var result = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var entry in raw.Split('|'))
        {
            token.ThrowIfCancellationRequested();
            if (ResolveEntry(entry) is { } method)
            {
                result.Add(method);
            }
        }

        return result.ToImmutable();
    }

    // An entry is "Assembly::EncodedDeclarationId". It must name exactly one
    // referenced assembly and exactly one supported method declared there.
    private IMethodSymbol? ResolveEntry(string entry)
    {
        var split = entry.IndexOf("::", StringComparison.Ordinal);
        if (split <= 0)
        {
            return null;
        }

        var assembly = entry.Substring(0, split).Trim();
        var id = Decode(entry.Substring(split + 2).Trim());
        if (id is null || !id.StartsWith("M:", StringComparison.Ordinal) || !NamesOneAssembly(assembly))
        {
            return null;
        }

        var candidates = DocumentationCommentId.GetSymbolsForDeclarationId(id, compilation)
            .OfType<IMethodSymbol>()
            .Select(Declaration)
            .Where(method => method.ContainingAssembly.Name == assembly
                && Supported(method.MethodKind)
                && DocumentationCommentId.CreateDeclarationId(method) == id)
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
            .ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private bool NamesOneAssembly(string name) => compilation.SourceModule.ReferencedAssemblySymbols
        .Append(compilation.Assembly)
        .Where(assembly => assembly.Name == name)
        .Select(assembly => assembly.Identity)
        .Distinct()
        .Count() == 1;

    // Roslyn treats # and ; as inline comments in native analyzer config values.
    // Decode only the ID transport field, after separating entries. The decoded
    // value still must exactly roundtrip through Roslyn's declaration-ID API.
    private static string? Decode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '%')
            {
                continue;
            }

            var isEscape = index + 2 < value.Length && Uri.IsHexDigit(value[index + 1]) && Uri.IsHexDigit(value[index + 2]);
            if (!isEscape)
            {
                return null;
            }

            index += 2;
        }

        return Uri.UnescapeDataString(value);
    }

    private static bool Supported(MethodKind kind) => kind
        is MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation
        or MethodKind.Constructor or MethodKind.DelegateInvoke;
}
