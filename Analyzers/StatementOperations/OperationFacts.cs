using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

/// <summary>Style facts about evaluated source syntax, without assuming purity.</summary>
internal sealed class OperationFacts
{
    public ExpressionSyntax Owner { get; }
    public ConditionalExpressionSyntax? Conditional { get; private set; }
    public bool Nested { get; private set; }
    public bool Combined { get; private set; }
    public bool HasReason => Conditional is not null || Nested || Combined;

    private OperationFacts(ExpressionSyntax owner) => Owner = owner;

    public static OperationFacts Create(ExpressionSyntax expression, SemanticModel model)
    {
        var facts = new OperationFacts(expression);
        var nodes = EvaluationNodes(expression).OfType<ExpressionSyntax>().ToArray();
        var calculations = nodes.Count(node => IsCalculation(node, model));
        facts.Conditional = nodes.OfType<ConditionalExpressionSyntax>()
            .FirstOrDefault(conditional => !IsNamedOrDirectReturn(conditional));
        facts.Combined = calculations > 1 || nodes.Any(node => IsCombination(node, model))
            || nodes.Any(node => IsMutation(node) && IsConsumedMutation(node));
        if (expression.Parent is ArgumentSyntax argument && IsOrdinaryArgument(argument, model))
        {
            facts.Nested = calculations > 1 || nodes.Any(node => IsProducer(node, model)
                || node is ConditionalExpressionSyntax);
        }

        return facts;
    }

    public static bool IsOrdinaryArgument(ArgumentSyntax argument, SemanticModel model) => argument.Parent?.Parent switch
    {
        InvocationExpressionSyntax invocation => model.GetOperation(invocation) is IInvocationOperation,
        BaseObjectCreationExpressionSyntax creation => model.GetOperation(creation) is IObjectCreationOperation,
        _ => false,
    };

    public static IEnumerable<SyntaxNode> EvaluationNodes(ExpressionSyntax expression)
    {
        return expression.DescendantNodesAndSelf(node => node is not AnonymousFunctionExpressionSyntax
            && (node is not ExpressionSyntax value || !IsNameof(value)));
    }

    private static bool IsNameof(ExpressionSyntax expression) => expression is InvocationExpressionSyntax
        { Expression: IdentifierNameSyntax { Identifier.Text: "nameof" } };

    public static bool IsProducer(ExpressionSyntax expression, SemanticModel model) => expression switch
    {
        InvocationExpressionSyntax invocation => !IsNameof(invocation) && invocation.ArgumentList.Arguments.Count != 0
            && model.GetOperation(invocation) is IInvocationOperation && !IsPipeline(invocation, model),
        BaseObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments.Count > 0
            && model.GetOperation(creation) is IObjectCreationOperation && creation.Initializer is null,
        CastExpressionSyntax cast => IsSupportedCast(cast, model),
        InterpolatedStringExpressionSyntax interpolation => model.GetTypeInfo(interpolation).ConvertedType?.SpecialType == SpecialType.System_String,
        _ => IsMutation(expression),
    };

    public static bool IsPipeline(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var type = (method?.ReducedFrom ?? method)?.ContainingType;
        if (IsLinq(invocation, model)) return true;
        if (type?.SpecialType != SpecialType.System_String || method?.Name != "Join") return false;
        return invocation.ArgumentList.Arguments.Any(argument => EvaluationNodes(argument.Expression)
            .OfType<InvocationExpressionSyntax>().Any(call => IsLinq(call, model)));
    }

    private static bool IsLinq(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var method = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var type = (method?.ReducedFrom ?? method)?.ContainingType;
        return type?.ContainingNamespace.ToDisplayString() == "System.Linq"
            && (type.Name is "Enumerable" or "Queryable" && type.ContainingAssembly.Name is "System.Linq" or "System.Linq.Queryable" or "System.Core" or "netstandard"
                || type.Name == "ImmutableArrayExtensions" && type.ContainingAssembly.Name == "System.Collections.Immutable");
    }

    public static bool IsCalculation(ExpressionSyntax expression, SemanticModel model)
    {
        if (expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.AddExpression
            or SyntaxKind.SubtractExpression or SyntaxKind.MultiplyExpression or SyntaxKind.DivideExpression
            or SyntaxKind.ModuloExpression or SyntaxKind.LeftShiftExpression or SyntaxKind.RightShiftExpression
            or SyntaxKind.UnsignedRightShiftExpression or SyntaxKind.BitwiseAndExpression
            or SyntaxKind.BitwiseOrExpression or SyntaxKind.ExclusiveOrExpression)
        {
            return model.GetOperation(binary) is IBinaryOperation { OperatorMethod: null, IsLifted: false } operation
                && IsNumeric(operation.Type) && IsNumeric(operation.LeftOperand.Type) && IsNumeric(operation.RightOperand.Type);
        }

        if (expression is PrefixUnaryExpressionSyntax unary && unary.Kind() is SyntaxKind.UnaryPlusExpression
            or SyntaxKind.UnaryMinusExpression or SyntaxKind.BitwiseNotExpression)
        {
            if (Unwrap(unary.Operand) is LiteralExpressionSyntax) return false;
            return model.GetOperation(unary) is IUnaryOperation { OperatorMethod: null, IsLifted: false } operation
                && IsNumeric(operation.Type);
        }

        return false;
    }

    private static bool IsNumeric(ITypeSymbol? type) => type?.SpecialType is SpecialType.System_SByte
        or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
        or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
        or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
        or SpecialType.System_Decimal or SpecialType.System_IntPtr or SpecialType.System_UIntPtr;

    public static bool IsSupportedCast(CastExpressionSyntax cast, SemanticModel model)
    {
        var target = model.GetTypeInfo(cast).Type;
        if (target is null) return false;
        var conversion = model.ClassifyConversion(cast.Expression, target);
        return conversion.Exists && !conversion.IsIdentity && !conversion.IsUserDefined
            && (conversion.IsNumeric || conversion.IsEnumeration || conversion.IsReference)
            && model.GetTypeInfo(cast.Expression).Type?.TypeKind != TypeKind.Dynamic;
    }

    private static bool IsCombination(ExpressionSyntax expression, SemanticModel model)
    {
        if (!IsCalculation(expression, model) && expression is not CastExpressionSyntax and not InterpolatedStringExpressionSyntax) return false;
        if (expression is CastExpressionSyntax cast && !IsSupportedCast(cast, model)) return false;
        if (expression is InterpolatedStringExpressionSyntax interpolation
            && model.GetTypeInfo(interpolation).ConvertedType?.SpecialType != SpecialType.System_String) return false;
        return EvaluationNodes(expression).Skip(1).OfType<ExpressionSyntax>().Any(node => IsProducer(node, model));
    }

    public static bool IsMutation(ExpressionSyntax expression) => expression is AssignmentExpressionSyntax
        || expression.Kind() is SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
            or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression;

    private static bool IsConsumedMutation(ExpressionSyntax expression)
    {
        var parent = UnwrapParent(expression).Parent;
        return parent is not ExpressionStatementSyntax and not ForStatementSyntax and not EqualsValueClauseSyntax
            and not ReturnStatementSyntax and not ArrowExpressionClauseSyntax
            && (parent is not AssignmentExpressionSyntax assignment || assignment.Right != UnwrapParent(expression));
    }

    private static bool IsNamedOrDirectReturn(ConditionalExpressionSyntax conditional)
    {
        var expression = UnwrapParent(conditional);
        return expression.Parent is EqualsValueClauseSyntax or ReturnStatementSyntax or ArrowExpressionClauseSyntax
            || expression.Parent is AssignmentExpressionSyntax assignment && assignment.Right == expression
            || expression.Parent is LambdaExpressionSyntax;
    }

    public static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses) expression = parentheses.Expression;
        return expression;
    }

    private static ExpressionSyntax UnwrapParent(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parentheses) expression = parentheses;
        return expression;
    }
}
