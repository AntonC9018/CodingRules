using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace CodingRules;

// Roslyn's default equality ignores reference nullability but retains tuple
// labels, including labels nested inside constructed generics/arrays. Compare
// tuple underlying types recursively while retaining every other symbol fact.
internal sealed class NamedArgumentTypes : IEqualityComparer<ITypeSymbol>
{
    public static readonly NamedArgumentTypes Instance = new();
    private NamedArgumentTypes() { }
    public bool Equals(ITypeSymbol? left, ITypeSymbol? right)
    {
        if (SymbolEqualityComparer.Default.Equals(left, right)) return true;
        if (left is null || right is null) return false;
        left = Underlying(left);
        right = Underlying(right);
        if (left is IArrayTypeSymbol a && right is IArrayTypeSymbol b)
            return a.Rank == b.Rank && a.IsSZArray == b.IsSZArray && Equals(a.ElementType, b.ElementType);
        if (left is IPointerTypeSymbol p && right is IPointerTypeSymbol q) return Equals(p.PointedAtType, q.PointedAtType);
        if (left is IFunctionPointerTypeSymbol f && right is IFunctionPointerTypeSymbol g) return SignaturesEqual(f.Signature, g.Signature);
        if (left is not INamedTypeSymbol x || right is not INamedTypeSymbol y
            || !SymbolEqualityComparer.Default.Equals(x.OriginalDefinition, y.OriginalDefinition)
            || !Equals(x.ContainingType, y.ContainingType) || x.TypeArguments.Length != y.TypeArguments.Length) return false;
        for (var index = 0; index < x.TypeArguments.Length; index++)
            if (!Equals(x.TypeArguments[index], y.TypeArguments[index])) return false;
        return true;
    }

    public int GetHashCode(ITypeSymbol type)
    {
        type = Underlying(type);
        if (type is IArrayTypeSymbol array) return unchecked(GetHashCode(array.ElementType) * 31 + array.Rank * 2 + (array.IsSZArray ? 1 : 0));
        if (type is IPointerTypeSymbol pointer) return unchecked(GetHashCode(pointer.PointedAtType) * 31 + 7);
        if (type is IFunctionPointerTypeSymbol function) return SignatureHash(function.Signature);
        if (type is not INamedTypeSymbol named) return SymbolEqualityComparer.Default.GetHashCode(type);
        var hash = SymbolEqualityComparer.Default.GetHashCode(named.OriginalDefinition);
        if (named.ContainingType is not null) hash = unchecked(hash * 31 + GetHashCode(named.ContainingType));
        foreach (var argument in named.TypeArguments) hash = unchecked(hash * 31 + GetHashCode(argument));
        return hash;
    }

    private bool SignaturesEqual(IMethodSymbol left, IMethodSymbol right)
    {
        if (left.CallingConvention != right.CallingConvention || left.RefKind != right.RefKind
            || !Equals(left.ReturnType, right.ReturnType)
            || !Enumerable.SequenceEqual<INamedTypeSymbol>(left.UnmanagedCallingConventionTypes, right.UnmanagedCallingConventionTypes, SymbolEqualityComparer.Default)
            || !left.ReturnTypeCustomModifiers.SequenceEqual(right.ReturnTypeCustomModifiers)
            || !left.RefCustomModifiers.SequenceEqual(right.RefCustomModifiers)
            || left.Parameters.Length != right.Parameters.Length) return false;
        for (var index = 0; index < left.Parameters.Length; index++)
        {
            var first = left.Parameters[index];
            var second = right.Parameters[index];
            if (first.RefKind != second.RefKind || first.ScopedKind != second.ScopedKind || first.IsParams != second.IsParams
                || !Equals(first.Type, second.Type) || !first.CustomModifiers.SequenceEqual(second.CustomModifiers)
                || !first.RefCustomModifiers.SequenceEqual(second.RefCustomModifiers)) return false;
        }
        return true;
    }

    private int SignatureHash(IMethodSymbol signature)
    {
        var hash = unchecked(((int)signature.CallingConvention * 31 + (int)signature.RefKind) * 31 + GetHashCode(signature.ReturnType));
        foreach (var convention in signature.UnmanagedCallingConventionTypes) hash = unchecked(hash * 31 + SymbolEqualityComparer.Default.GetHashCode(convention));
        foreach (var modifier in signature.ReturnTypeCustomModifiers) hash = unchecked(hash * 31 + modifier.GetHashCode());
        foreach (var modifier in signature.RefCustomModifiers) hash = unchecked(hash * 31 + modifier.GetHashCode());
        foreach (var parameter in signature.Parameters)
        {
            hash = unchecked(((hash * 31 + (int)parameter.RefKind) * 31 + (int)parameter.ScopedKind) * 31 + (parameter.IsParams ? 1 : 0));
            hash = unchecked(hash * 31 + GetHashCode(parameter.Type));
            foreach (var modifier in parameter.CustomModifiers) hash = unchecked(hash * 31 + modifier.GetHashCode());
            foreach (var modifier in parameter.RefCustomModifiers) hash = unchecked(hash * 31 + modifier.GetHashCode());
        }
        return hash;
    }

    private static ITypeSymbol Underlying(ITypeSymbol type) => type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } ? underlying : type;
}
