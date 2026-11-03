using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CodingRules;

/// <summary>
/// Recognises symbols shipped by the .NET platform. Rules whose safety depends
/// on a framework API's documented behavior must not trust a source or
/// third-party type that merely reuses its name.
/// </summary>
internal static class FrameworkSymbols
{
    // Strong-name tokens of the platform's reference, implementation and
    // out-of-band package assemblies (mscorlib/System.Core, System.Private.CoreLib,
    // System.Runtime facades/System.Collections.Immutable, netstandard/System.Memory/System.Text.Json).
    private static readonly ImmutableHashSet<string> PlatformTokens = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "b77a5c561934e089",
        "7cec85d7bea7798e",
        "b03f5f7f11d50a3a",
        "cc7b13ffcd2ddd51");

    public static bool IsPlatformAssembly(IAssemblySymbol? assembly)
    {
        if (assembly is null)
        {
            return false;
        }

        var token = assembly.Identity.PublicKeyToken;
        return !token.IsDefaultOrEmpty
            && PlatformTokens.Contains(string.Concat(token.Select(value => value.ToString("x2"))));
    }

    /// <summary>The platform's own definition of <paramref name="metadataName"/>, if this compilation binds it.</summary>
    public static bool IsPlatformType(ITypeSymbol? type, string metadataName, Compilation compilation) =>
        type is not null
        && IsPlatformAssembly(type.ContainingAssembly)
        && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, compilation.GetTypeByMetadataName(metadataName));
}
