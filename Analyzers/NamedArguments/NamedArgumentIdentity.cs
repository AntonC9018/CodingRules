using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

// Structural identities deliberately retain assembly identity and constructions.
// Display strings and OriginalDefinition alone lose those facts across compilations.
internal static class NamedArgumentIdentity
{
    public static string Symbol(ISymbol? symbol)
    {
        if (symbol is null) return "-";
        if (symbol is ITypeSymbol type) return Type(type);
        if (symbol is IParameterSymbol parameter) return Symbol(parameter.ContainingSymbol) + "/p" + parameter.Ordinal + ":" + parameter.RefKind + ":" + Type(parameter.Type);
        if (symbol is IMethodSymbol method)
            return method.ContainingAssembly.Identity + "/" + Type(method.ContainingType) + "/" + method.MethodKind + "/"
                + (DocumentationCommentId.CreateDeclarationId(NamedArgumentPolicy.Declaration(method)) ?? method.MetadataName)
                + "<" + string.Join(";", method.TypeArguments.Select(Type)) + ">"
                + (method.ReducedFrom is null ? "" : "/reduced:" + Symbol(method.ReducedFrom))
                + "/" + string.Join(";", method.Parameters.Select(parameter => parameter.RefKind + ":" + Type(parameter.Type))) + "/" + Type(method.ReturnType);
        return symbol.ContainingAssembly?.Identity + "/" + Symbol(symbol.ContainingType)
            + "/" + symbol.Kind + "/" + (DocumentationCommentId.CreateDeclarationId(symbol.OriginalDefinition) ?? symbol.MetadataName);
    }

    public static string Type(ITypeSymbol? type) => type switch
    {
        null => "-",
        IArrayTypeSymbol array => "array" + array.Rank + "(" + Type(array.ElementType) + ")",
        IPointerTypeSymbol pointer => "pointer(" + Type(pointer.PointedAtType) + ")",
        ITypeParameterSymbol parameter => "parameter:" + parameter.TypeParameterKind + ":" + parameter.Ordinal,
        INamedTypeSymbol { IsAnonymousType: true } anonymous => "anonymous(" + string.Join(";", anonymous.GetMembers().OfType<IPropertySymbol>()
            .Select(property => property.Name + ":" + Type(property.Type))) + ")",
        INamedTypeSymbol named => named.ContainingAssembly.Identity + "/" + DocumentationCommentId.CreateDeclarationId(named.OriginalDefinition)
            + "/outer(" + Type(named.ContainingType) + ")<" + string.Join(";", named.TypeArguments.Select(Type)) + ">",
        _ => type.TypeKind + ":" + type.SpecialType + ":" + type.MetadataName,
    };

    public static string Conversion(CommonConversion conversion) => conversion.Exists + ":" + conversion.IsIdentity + ":" + conversion.IsImplicit
        + ":" + conversion.IsNumeric + ":" + conversion.IsNullable + ":" + conversion.IsReference + ":" + conversion.IsUserDefined + ":" + Symbol(conversion.MethodSymbol);

    public static string Operation(IOperation? operation)
    {
        if (operation is null) return "-";
        var detail = operation switch
        {
            IInvocationOperation call => Symbol(call.TargetMethod) + ":virtual=" + call.IsVirtual,
            IObjectCreationOperation creation => Symbol(creation.Constructor),
            IArgumentOperation argument => Symbol(argument.Parameter) + ":" + argument.ArgumentKind + ":" + Conversion(argument.InConversion) + ":" + Conversion(argument.OutConversion),
            IConversionOperation conversion => Conversion(conversion.Conversion) + ":" + conversion.IsChecked + ":" + conversion.IsTryCast,
            IPropertyReferenceOperation property => Symbol(property.Property),
            IFieldReferenceOperation field => Symbol(field.Field),
            IEventReferenceOperation eventReference => Symbol(eventReference.Event),
            IMethodReferenceOperation reference => Symbol(reference.Method),
            IParameterReferenceOperation parameter => Symbol(parameter.Parameter),
            ILocalReferenceOperation local => local.Local.Name + ":" + Type(local.Local.Type) + ":" + local.Local.RefKind,
            ITypeOfOperation typeOf => Type(typeOf.TypeOperand),
            IBinaryOperation binary => binary.OperatorKind + ":" + binary.IsChecked + ":" + binary.IsLifted + ":" + Symbol(binary.OperatorMethod),
            IUnaryOperation unary => unary.OperatorKind + ":" + unary.IsChecked + ":" + unary.IsLifted + ":" + Symbol(unary.OperatorMethod),
            _ => "",
        };
        var constant = operation.ConstantValue.HasValue ? operation.ConstantValue.Value is null ? "null" : operation.ConstantValue.Value.GetType().FullName
            + ":" + Convert.ToString(operation.ConstantValue.Value, System.Globalization.CultureInfo.InvariantCulture) : "none";
        return operation.Kind + ":" + operation.IsImplicit + ":" + Type(operation.Type) + ":" + constant + ":" + detail
            + "[" + string.Join(";", operation.ChildOperations.Select(Operation)) + "]";
    }
}
