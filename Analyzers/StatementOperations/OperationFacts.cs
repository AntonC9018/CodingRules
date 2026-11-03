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
        if (expression.Parent is ArgumentSyntax && IsSimpleJsonFieldRead(expression, model))
        {
            return facts;
        }
        var nodes = EvaluationNodes(expression).OfType<ExpressionSyntax>().ToArray();
        var calculations = nodes.Count(node => IsCalculation(node, model));
        facts.Conditional = nodes.OfType<ConditionalExpressionSyntax>()
            .FirstOrDefault(conditional => !IsNamedOrDirectReturn(conditional)
                || HasComposedPipelineBranch(conditional, model));
        facts.Combined = calculations > 1 || nodes.Any(node => IsCombination(node, model))
            || nodes.Any(node => IsMutation(node) && IsConsumedMutation(node));
        if (expression.Parent is ArgumentSyntax argument && IsOrdinaryArgument(argument, model))
        {
            var allowed = SingleSimpleValue(argument, nodes, model);
            facts.Nested = calculations > 1 || nodes.Any(node => !allowed.Contains(node)
                && (IsProducer(node, model) || node is ConditionalExpressionSyntax));
        }

        return facts;
    }

    /// <summary>
    /// The work an argument may do inline: the only call, construction or
    /// interpolation among its siblings, when every input it reads is simple.
    /// A call may continue a chain on one simple receiver; each link is allowed.
    /// </summary>
    private static IReadOnlyCollection<SyntaxNode> SingleSimpleValue(ArgumentSyntax argument, ExpressionSyntax[] evaluated, SemanticModel model)
    {
        var none = System.Array.Empty<SyntaxNode>();
        if (argument.Parent is not ArgumentListSyntax arguments
            || arguments.Arguments.Count(item => IsComposite(Parentheses.Unwrap(item.Expression), model)) != 1)
        {
            return none;
        }

        var value = Parentheses.Unwrap(argument.Expression);
        if (value is BaseObjectCreationExpressionSyntax creation && model.GetOperation(creation) is IObjectCreationOperation)
        {
            // The direct construction and one simple interpolation among its own
            // arguments are allowed. Other input computations retain their existing
            // diagnostics, including nested construction and arithmetic.
            var message = creation.ArgumentList?.Arguments.Select(item => Parentheses.Unwrap(item.Expression))
                .OfType<InterpolatedStringExpressionSyntax>().Where(item => IsSimpleInterpolation(item, model)).Take(2).ToArray();
            var allowed = message is { Length: 1 } ? new SyntaxNode[] { creation, message[0] } : new SyntaxNode[] { creation };
            return evaluated.Any(node => !allowed.Contains(node)
                && (node is BaseObjectCreationExpressionSyntax or ConditionalExpressionSyntax
                    || IsProducer(node, model) || IsCalculation(node, model))) ? none : allowed;
        }

        if (value is InterpolatedStringExpressionSyntax interpolation)
        {
            return IsSimpleInterpolation(interpolation, model) ? new SyntaxNode[] { interpolation } : none;
        }

        var chain = new List<SyntaxNode>();
        return value is InvocationExpressionSyntax call && IsSimpleChain(call, chain, model) ? chain : none;
    }

    private static bool IsSimpleInterpolation(InterpolatedStringExpressionSyntax interpolation, SemanticModel model) =>
        interpolation.Contents.OfType<InterpolationSyntax>().All(hole => IsSimpleInput(hole.Expression, model));

    private static bool IsComposite(ExpressionSyntax value, SemanticModel model) =>
        value is BaseObjectCreationExpressionSyntax creation && model.GetOperation(creation) is IObjectCreationOperation
        || EvaluationNodes(value).OfType<ExpressionSyntax>().Any(node => IsProducer(node, model));

    // A call whose arguments are simple inputs, on a simple receiver or on another such call.
    private static bool IsSimpleChain(InvocationExpressionSyntax call, List<SyntaxNode> chain, SemanticModel model)
    {
        if (model.GetOperation(call) is not IInvocationOperation
            || call.ArgumentList.Arguments.Any(argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)
                || !IsSimpleInput(argument.Expression, model)))
        {
            return false;
        }

        chain.Add(call);
        return call.Expression switch
        {
            SimpleNameSyntax => true,
            MemberAccessExpressionSyntax { Expression: var receiver } => Parentheses.Unwrap(receiver) is InvocationExpressionSyntax previous
                ? IsSimpleChain(previous, chain, model) : IsSimpleInput(receiver, model),
            _ => false,
        };
    }

    /// <summary>A literal, variable, member read, type name or lambda: an input that does no work of its own.</summary>
    internal static bool IsSimpleInput(ExpressionSyntax expression, SemanticModel model) => Parentheses.Unwrap(expression) switch
    {
        LiteralExpressionSyntax or IdentifierNameSyntax or ThisExpressionSyntax or PredefinedTypeSyntax
            or AnonymousFunctionExpressionSyntax or TypeOfExpressionSyntax or DefaultExpressionSyntax => true,
        PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax } negative => negative.IsKind(SyntaxKind.UnaryMinusExpression),
        MemberAccessExpressionSyntax member => model.GetSymbolInfo(member).Symbol is not IMethodSymbol
            && IsSimpleInput(member.Expression, model),
        InvocationExpressionSyntax invocation => IsNameof(invocation),
        _ => false,
    };

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
        InvocationExpressionSyntax invocation => !IsNameof(invocation)
            && !IsReviewedRuntimeTypeAssertion(invocation, model)
            && (invocation.ArgumentList.Arguments.Count != 0 || IsGenericArgumentLookup(invocation, model))
            && model.GetOperation(invocation) is IInvocationOperation && !IsPipeline(invocation, model),
        BaseObjectCreationExpressionSyntax creation => creation.ArgumentList?.Arguments.Count > 0
            && model.GetOperation(creation) is IObjectCreationOperation && creation.Initializer is null,
        // A cast alone is an accepted argument; computations inside it remain visible.
        CastExpressionSyntax => false,
        InterpolatedStringExpressionSyntax interpolation => model.GetTypeInfo(interpolation).ConvertedType?.SpecialType == SpecialType.System_String,
        _ => IsMutation(expression),
    };

    private static bool IsSimpleJsonFieldRead(ExpressionSyntax expression, SemanticModel model)
    {
        if (Parentheses.Unwrap(expression) is not InvocationExpressionSyntax scalar
            || scalar.ArgumentList.Arguments.Count != 0
            || model.GetSymbolInfo(scalar).Symbol is not IMethodSymbol reader
            || reader.Name is not ("GetString" or "GetBoolean" or "GetInt32" or "GetInt64"
                or "GetDouble" or "GetDecimal" or "GetArrayLength")
            || !IsJsonElement(reader.ContainingType, model)
            || scalar.Expression is not MemberAccessExpressionSyntax access
            || Parentheses.Unwrap(access.Expression) is not InvocationExpressionSyntax property
            || property.ArgumentList.Arguments.Count != 1
            || model.GetSymbolInfo(property).Symbol is not IMethodSymbol { Name: "GetProperty" } lookup
            || !IsJsonElement(lookup.ContainingType, model)
            || property.Expression is not MemberAccessExpressionSyntax receiver
            || !IsDirectJsonInput(receiver.Expression)
            || !IsDirectJsonInput(property.ArgumentList.Arguments[0].Expression))
        {
            return false;
        }

        return true;
    }

    private static bool IsJsonElement(INamedTypeSymbol type, SemanticModel model) =>
        FrameworkSymbols.IsPlatformType(type, "System.Text.Json.JsonElement", model.Compilation);

    private static bool IsDirectJsonInput(ExpressionSyntax expression) => Parentheses.Unwrap(expression) switch
    {
        IdentifierNameSyntax or LiteralExpressionSyntax or ThisExpressionSyntax => true,
        MemberAccessExpressionSyntax member => IsDirectJsonInput(member.Expression),
        _ => false,
    };

    private static bool IsReviewedRuntimeTypeAssertion(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol { Name: "IsAssignableTo" } method
            || !FrameworkSymbols.IsPlatformType(method.ContainingType, "System.Type", model.Compilation)
            || invocation.ArgumentList.Arguments.Count != 1
            || Parentheses.Unwrap(invocation.ArgumentList.Arguments[0].Expression) is not TypeOfExpressionSyntax
            || invocation.Expression is not MemberAccessExpressionSyntax access
            || Parentheses.Unwrap(access.Expression) is not InvocationExpressionSyntax getType
            || model.GetSymbolInfo(getType).Symbol is not IMethodSymbol
                { Name: "GetType", Parameters.Length: 0, ContainingType.SpecialType: SpecialType.System_Object }
            || getType.Expression is not MemberAccessExpressionSyntax receiver
            || Parentheses.Unwrap(receiver.Expression) is not IdentifierNameSyntax)
        {
            return false;
        }

        var argument = invocation.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
        return argument?.Parent?.Parent is InvocationExpressionSyntax assertion
            && model.GetOperation(argument) is IArgumentOperation { Parameter.Ordinal: 0 }
            && model.GetSymbolInfo(assertion).Symbol is IMethodSymbol { Name: "Assert" } assertMethod
            && FrameworkSymbols.IsPlatformType(assertMethod.ContainingType, "System.Diagnostics.Debug", model.Compilation);
    }

    // Type metadata calculation is the reviewed parameterless exception. Ordinary
    // factories and business-service receiver chains retain their existing policy.
    private static bool IsGenericArgumentLookup(InvocationExpressionSyntax invocation, SemanticModel model) =>
        model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "GetGenericArguments", Parameters.Length: 0 } method
        && FrameworkSymbols.IsPlatformType(method.ContainingType, "System.Type", model.Compilation);

    internal static bool HasComposedPipelineBranch(ConditionalExpressionSyntax conditional, SemanticModel model) =>
        HasComposedPipeline(conditional.WhenTrue, model) || HasComposedPipeline(conditional.WhenFalse, model);

    private static bool HasComposedPipeline(ExpressionSyntax expression, SemanticModel model) =>
        EvaluationNodes(expression).OfType<InvocationExpressionSyntax>().Count(call => IsPipeline(call, model)) > 1;

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
            && type.Name is "Enumerable" or "Queryable" or "ImmutableArrayExtensions"
            && FrameworkSymbols.IsPlatformAssembly(type.ContainingAssembly);
    }

    public static bool IsCalculation(ExpressionSyntax expression, SemanticModel model)
    {
        if (IsSimpleIndexArithmetic(expression, model)) return false;
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
            if (Parentheses.Unwrap(unary.Operand) is LiteralExpressionSyntax) return false;
            return model.GetOperation(unary) is IUnaryOperation { OperatorMethod: null, IsLifted: false } operation
                && IsNumeric(operation.Type);
        }

        return false;
    }

    // a[j - 1] reads one element: an index offset of simple inputs is part of the access.
    private static bool IsSimpleIndexArithmetic(ExpressionSyntax expression, SemanticModel model) =>
        expression is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.AddExpression or SyntaxKind.SubtractExpression
        && UnwrapParent(binary).Parent is ArgumentSyntax { Parent: BracketedArgumentListSyntax }
        && IsSimpleInput(binary.Left, model) && IsSimpleInput(binary.Right, model)
        && Parentheses.Unwrap(binary.Left) is not AnonymousFunctionExpressionSyntax
        && Parentheses.Unwrap(binary.Right) is not AnonymousFunctionExpressionSyntax;

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
        // A lookup followed by a cast still expresses one lookup. Work inside
        // its arguments is classified independently rather than charging for
        // the cast as another producer/consumer pair.
        if (expression is CastExpressionSyntax) return false;
        if (!IsCalculation(expression, model) && expression is not InterpolatedStringExpressionSyntax) return false;
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
        // Initializer members assign a value without forwarding the assignment's result.
        if (parent.IsKind(SyntaxKind.ObjectInitializerExpression) || parent.IsKind(SyntaxKind.WithInitializerExpression)) return false;
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

    private static ExpressionSyntax UnwrapParent(ExpressionSyntax expression)
    {
        while (expression.Parent is ParenthesizedExpressionSyntax parentheses) expression = parentheses;
        return expression;
    }
}
