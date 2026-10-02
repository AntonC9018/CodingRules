using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class SentinelSearch
{
    public static IOperation? Operation(ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses) expression = parentheses.Expression;
        return Unwrap(model.GetOperation(expression, token));
    }
    public static IOperation? Unwrap(IOperation? operation)
    {
        while (true)
        {
            if (operation is IConversionOperation { Conversion.IsIdentity: true } conversion) { operation = conversion.Operand; continue; }
            if (operation is IParenthesizedOperation parenthesized) { operation = parenthesized.Operand; continue; }
            return operation;
        }
    }
    public static bool Catalogue(IMethodSymbol method, Compilation compilation)
    {
        if (method.ReturnType.SpecialType != SpecialType.System_Int32 || method.Name is not ("IndexOf" or "LastIndexOf")) return false;
        var definition = method.OriginalDefinition;
        var type = definition.ContainingType;
        var identity = type.ContainingAssembly.Identity;
        var key = string.Concat(identity.PublicKeyToken.Select(value => value.ToString("x2")));
        if (identity.Name is not ("mscorlib" or "System.Private.CoreLib" or "System.Collections" or "netstandard" or "System.Runtime")
            || key is not ("b77a5c561934e089" or "7cec85d7bea7798e" or "b03f5f7f11d50a3a" or "cc7b13ffcd2ddd51")) return false;
        return SymbolEqualityComparer.Default.Equals(type, compilation.GetSpecialType(SpecialType.System_String))
            || SymbolEqualityComparer.Default.Equals(type, compilation.GetTypeByMetadataName("System.Collections.Generic.List`1"));
    }

    public static bool Loop(SentinelHost host, SemanticModel model, CancellationToken token)
    {
        if (host.Body is not BlockSyntax block || block.Statements.Count < 2
            || block.Statements.Take(block.Statements.Count - 2).Any(statement => statement is not LocalDeclarationStatementSyntax)
            || block.Statements[block.Statements.Count - 2] is not ForStatementSyntax loop
            || block.Statements.Last() is not ReturnStatementSyntax { Expression: { } fallback }
            || model.GetConstantValue(fallback, token).Value is not int value || value != -1
            || loop.Declaration is not { Variables.Count: 1 } declaration || loop.Initializers.Count != 0
            || declaration.Variables[0].Initializer?.Value is not { } initial
            || model.GetConstantValue(initial, token).Value is not int zero || zero != 0
            || model.GetDeclaredSymbol(declaration.Variables[0], token) is not ILocalSymbol counter
            || counter.Type.SpecialType != SpecialType.System_Int32
            || loop.Condition is null || Operation(loop.Condition, model, token) is not IBinaryOperation { OperatorKind: BinaryOperatorKind.LessThan, OperatorMethod: null, IsLifted: false } condition
            || condition.LeftOperand is not ILocalReferenceOperation left || !SymbolEqualityComparer.Default.Equals(left.Local, counter)
            || condition.RightOperand is not IPropertyReferenceOperation { Property.Name: "Length", Instance: { } instance } length
            || instance.Type is not IArrayTypeSymbol { IsSZArray: true }
            || length.Property.ContainingType.SpecialType != SpecialType.System_Array
            || Variable(instance) is not { } array
            || array is not (ILocalSymbol { RefKind: RefKind.None } or IParameterSymbol { RefKind: RefKind.None })
            || loop.Incrementors.Count != 1
            || model.GetOperation(loop.Incrementors[0], token) is not IIncrementOrDecrementOperation { Kind: OperationKind.Increment, OperatorMethod: null, Target: ILocalReferenceOperation increment }
            || !SymbolEqualityComparer.Default.Equals(increment.Local, counter)) return false;
        if (block.Statements.Count > 2 && (block.Statements.Count != 3 || array is not ILocalSymbol
            || block.Statements[0] is not LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } local
            || !SymbolEqualityComparer.Default.Equals(model.GetDeclaredSymbol(local.Declaration.Variables[0], token), array)
            || local.Declaration.Variables[0].Initializer is null)) return false;
        var body = loop.Statement is BlockSyntax inner && inner.Statements.Count == 1 ? inner.Statements[0] : loop.Statement;
        if (body is not IfStatementSyntax { Else: null } branch) return false;
        var success = branch.Statement is BlockSyntax arm && arm.Statements.Count == 1 ? arm.Statements[0] : branch.Statement;
        if (success is not ReturnStatementSyntax { Expression: { } result }
            || Operation(result, model, token) is not ILocalReferenceOperation returned
            || !SymbolEqualityComparer.Default.Equals(returned.Local, counter)) return false;
        var predicate = model.GetOperation(branch.Condition, token);
        if (predicate is null || !predicate.DescendantsAndSelf().OfType<IArrayElementReferenceOperation>().Any(element =>
                SymbolEqualityComparer.Default.Equals(Variable(element.ArrayReference), array)
                && element.Indices.Length == 1 && element.Indices[0] is ILocalReferenceOperation index
                && SymbolEqualityComparer.Default.Equals(index.Local, counter))) return false;
        // Only the loop's own increment may mutate its counter. Re-reading the same
        // local/parameter array is safe only while its reference cannot change.
        foreach (var node in host.Body.DescendantNodes().Where(node => node is ExpressionSyntax))
        {
            token.ThrowIfCancellationRequested();
            var operation = model.GetOperation(node, token);
            if (operation is IAssignmentOperation assignment && IsTracked(Variable(assignment.Target), array, counter)) return false;
            if (operation is IIncrementOrDecrementOperation update && IsTracked(Variable(update.Target), array, counter)
                && node != loop.Incrementors[0]) return false;
            if (operation is IArgumentOperation { Parameter.RefKind: not RefKind.None } argument && IsTracked(Variable(argument.Value), array, counter)) return false;
            if (node.Ancestors().TakeWhile(parent => parent != host.Body).Any(SentinelHost.Nested)
                && IsTracked(Variable(operation), array, counter)) return false;
        }
        if (host.Body.DescendantNodes().OfType<ArgumentSyntax>().Any(argument => argument.RefKindKeyword.RawKind != 0
            && IsTracked(Variable(model.GetOperation(argument.Expression, token)), array, counter))) return false;
        return true;
    }

    public static ISymbol? Variable(IOperation? operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        _ => null,
    };
    private static bool IsTracked(ISymbol? symbol, ISymbol array, ISymbol counter) => symbol is not null
        && (SymbolEqualityComparer.Default.Equals(symbol, array) || SymbolEqualityComparer.Default.Equals(symbol, counter));
}
