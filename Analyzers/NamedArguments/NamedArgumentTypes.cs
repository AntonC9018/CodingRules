using System.Collections.Generic;
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
        if (type is not INamedTypeSymbol named) return SymbolEqualityComparer.Default.GetHashCode(type);
        var hash = SymbolEqualityComparer.Default.GetHashCode(named.OriginalDefinition);
        if (named.ContainingType is not null) hash = unchecked(hash * 31 + GetHashCode(named.ContainingType));
        foreach (var argument in named.TypeArguments) hash = unchecked(hash * 31 + GetHashCode(argument));
        return hash;
    }

    private static ITypeSymbol Underlying(ITypeSymbol type) => type is INamedTypeSymbol { IsTupleType: true, TupleUnderlyingType: { } underlying } ? underlying : type;
}
