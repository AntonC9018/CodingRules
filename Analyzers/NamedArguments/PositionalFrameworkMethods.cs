using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CodingRules;

/// <summary>
/// Framework methods whose arguments read clearly by position: symmetric
/// operands (<c>Math.Min</c>, <c>string.Equals</c>), an old/new pair
/// (<c>string.Replace</c>) and ordered path segments (<c>Path.Combine</c>).
/// </summary>
internal static class PositionalFrameworkMethods
{
    private static readonly string[] DeclaringTypes = { "System.Math", "System.MathF", "System.String", "System.IO.Path" };

    public static ImmutableHashSet<IMethodSymbol> Collect(Compilation compilation)
    {
        var methods = ImmutableHashSet.CreateBuilder<IMethodSymbol>(SymbolEqualityComparer.Default);
        foreach (var name in DeclaringTypes)
        {
            var declaringType = compilation.GetTypeByMetadataName(name);
            if (declaringType is null)
            {
                continue;
            }

            methods.UnionWith(declaringType.GetMembers().OfType<IMethodSymbol>().Where(IsPositional));
        }

        return methods.ToImmutable();
    }

    private static bool IsPositional(IMethodSymbol method)
    {
        var isPlainFrameworkMethod = !method.Locations.Any(location => location.IsInSource)
            && method.Arity == 0
            && method.Parameters.All(parameter => parameter.RefKind == RefKind.None)
            && FrameworkSymbols.IsPlatformAssembly(method.ContainingAssembly);
        if (!isPlainFrameworkMethod)
        {
            return false;
        }

        return IsMinOrMax(method) || IsStringEquals(method) || IsStringReplace(method) || IsPathCombine(method);
    }

    private static bool IsMinOrMax(IMethodSymbol method)
    {
        var typeName = method.ContainingType.ToDisplayString();
        if (typeName is not ("System.Math" or "System.MathF")
            || !method.IsStatic
            || method.Name is not ("Min" or "Max")
            || method.Parameters.Length != 2)
        {
            return false;
        }

        var operandType = method.Parameters[0].Type;
        var isSymmetric = SymbolEqualityComparer.Default.Equals(operandType, method.Parameters[1].Type)
            && SymbolEqualityComparer.Default.Equals(operandType, method.ReturnType);
        if (!isSymmetric)
        {
            return false;
        }

        return typeName == "System.MathF"
            ? operandType.SpecialType == SpecialType.System_Single
            : IsNumeric(operandType.SpecialType);
    }

    private static bool IsNumeric(SpecialType type) => type
        is SpecialType.System_Byte or SpecialType.System_SByte
        or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32
        or SpecialType.System_Int64 or SpecialType.System_UInt64
        or SpecialType.System_IntPtr or SpecialType.System_UIntPtr
        or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;

    // string.Equals(string, string) and string.Equals(string, string, StringComparison).
    private static bool IsStringEquals(IMethodSymbol method)
    {
        if (method.ContainingType.SpecialType != SpecialType.System_String
            || !method.IsStatic
            || method.Name != "Equals"
            || method.ReturnType.SpecialType != SpecialType.System_Boolean)
        {
            return false;
        }

        var parameters = method.Parameters;
        return parameters.Length is 2 or 3
            && StartsWithTwoStrings(parameters)
            && (parameters.Length == 2 || IsStringComparison(parameters[2].Type));
    }

    // Replace(char, char), Replace(string, string), Replace(string, string, StringComparison)
    // and Replace(string, string, bool, CultureInfo).
    private static bool IsStringReplace(IMethodSymbol method)
    {
        if (method.ContainingType.SpecialType != SpecialType.System_String
            || method.IsStatic
            || method.Name != "Replace"
            || method.ReturnType.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        var parameters = method.Parameters;
        return parameters.Length switch
        {
            2 => parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_Char)
                || StartsWithTwoStrings(parameters),
            3 => StartsWithTwoStrings(parameters) && IsStringComparison(parameters[2].Type),
            4 => StartsWithTwoStrings(parameters)
                && parameters[2].Type.SpecialType == SpecialType.System_Boolean
                && parameters[3].Type is INamedTypeSymbol { MetadataName: "CultureInfo" } culture
                && culture.ContainingNamespace.ToDisplayString() == "System.Globalization",
            _ => false,
        };
    }

    // Path.Combine with two to four string segments; the params overload stays named.
    private static bool IsPathCombine(IMethodSymbol method) =>
        method.ContainingType.ToDisplayString() == "System.IO.Path"
        && method.IsStatic
        && method.Name == "Combine"
        && method.ReturnType.SpecialType == SpecialType.System_String
        && method.Parameters.Length is 2 or 3 or 4
        && method.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_String);

    private static bool StartsWithTwoStrings(ImmutableArray<IParameterSymbol> parameters) =>
        parameters[0].Type.SpecialType == SpecialType.System_String
        && parameters[1].Type.SpecialType == SpecialType.System_String;

    private static bool IsStringComparison(ITypeSymbol type) => type.ToDisplayString() == "System.StringComparison";
}
