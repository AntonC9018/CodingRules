using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class OperationPlan
{
    public ExpressionSyntax Owner { get; }
    public ExpressionSyntax ExtractionRoot { get; private set; }
    public ExpressionSyntax? ExpansionRoot { get; private set; }
    public SemanticModel Model { get; }
    public StatementSyntax? Statement { get; }
    public SyntaxNode? ExpressionBody { get; }
    public EqualsValueClauseSyntax? MemberInitializer { get; }
    public SyntaxNode DeclarationSpace { get; }
    public List<ISymbol> BridgeVariables { get; } = new();
    public bool CanExtract { get; private set; }
    public bool CanExpand => ExpansionRoot is not null;

    private OperationPlan(ExpressionSyntax owner, SemanticModel model)
    {
        Owner = owner;
        ExtractionRoot = owner;
        Model = model;
        Statement = ConditionHosts.FindStatement(owner);
        ExpressionBody = owner.Ancestors().TakeWhile(node => node is not StatementSyntax and not MemberDeclarationSyntax)
            .FirstOrDefault(node => node is ArrowExpressionClauseSyntax or LambdaExpressionSyntax { Body: ExpressionSyntax });
        MemberInitializer = owner.Ancestors().TakeWhile(node => node is not StatementSyntax
                and not ArrowExpressionClauseSyntax and not AnonymousFunctionExpressionSyntax)
            .OfType<EqualsValueClauseSyntax>().FirstOrDefault(initializer => initializer.Parent is PropertyDeclarationSyntax
                || initializer.Parent?.Parent?.Parent is FieldDeclarationSyntax);
        DeclarationSpace = owner.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            ?? owner.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() ?? owner.SyntaxTree.GetRoot();
    }

    public static OperationPlan? Create(ExpressionSyntax owner, SemanticModel model, CancellationToken token)
    {
        if (RequiresConstant(owner) || !SafeSyntax(owner, model, token) || !CanLower(owner, model)) return null;
        var plan = new OperationPlan(owner, model);
        if (plan.Statement is null && plan.ExpressionBody is null && plan.MemberInitializer is null) return null;
        if (plan.DeclarationSpace.DescendantNodes().Any(node => node is LabeledStatementSyntax or GotoStatementSyntax)) return null;

        // Pattern/filter/header variables are unavailable where a helper is declared.
        // Bridge the complete evaluation site, so its parameterized invocation is
        // not another nested argument operation.
        var filter = owner.Ancestors().FirstOrDefault(node => node is CatchFilterClauseSyntax or WhenClauseSyntax);
        var loopRoot = plan.Statement switch
        {
            ForStatementSyntax loop when loop.Condition?.Span.Contains(owner.Span) == true => loop.Condition,
            ForStatementSyntax loop => loop.Incrementors.FirstOrDefault(value => value.Span.Contains(owner.Span)),
            _ => null,
        };
        var site = filter switch
        {
            CatchFilterClauseSyntax clause => clause.FilterExpression,
            WhenClauseSyntax clause => clause.Condition,
            _ => loopRoot,
        };
        var flow = model.AnalyzeDataFlow(owner);
        if (flow?.Succeeded != true) return null;
        var symbols = flow.ReadInside.Concat(flow.WrittenInside).Distinct(SymbolEqualityComparer.Default).ToArray();
        var insertion = plan.Statement?.SpanStart ?? (plan.ExpressionBody is LambdaExpressionSyntax { Body: ExpressionSyntax lambdaBody }
            ? lambdaBody.SpanStart : plan.ExpressionBody?.SpanStart) ?? plan.MemberInitializer!.SpanStart;
        var unavailable = symbols.Where(symbol => symbol is (ILocalSymbol or IParameterSymbol)
            && !model.LookupSymbols(insertion, name: symbol.Name).Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, symbol))).ToArray();
        if (unavailable.Length != 0 && site is not null)
        {
            if (!SafeSyntax(site, model, token) || !CanLower(site, model)) return null;
            plan.ExtractionRoot = site;
            flow = model.AnalyzeDataFlow(site);
            if (flow?.Succeeded != true) return null;
            symbols = flow.ReadInside.Concat(flow.WrittenInside).Distinct(SymbolEqualityComparer.Default).ToArray();
            plan.BridgeVariables.AddRange(unavailable);
        }

        var declared = new HashSet<ISymbol>(flow.VariablesDeclared, SymbolEqualityComparer.Default);
        var writes = flow.WrittenInside.Any(symbol => !declared.Contains(symbol));
        var externalDeclarations = declared.Any(symbol => plan.DeclarationSpace.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Any(identifier => !plan.ExtractionRoot.Span.Contains(identifier.Span)
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol)));
        var unsafeCapture = symbols.Any(CannotCapture);
        var structThis = model.GetEnclosingSymbol(owner.SpanStart)?.ContainingType?.IsValueType == true
            && OperationFacts.EvaluationNodes(plan.ExtractionRoot).OfType<ExpressionSyntax>().Any(value => model.GetOperation(value) switch
            {
                IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } => true,
                IFieldReferenceOperation { Instance: IInstanceReferenceOperation } => true,
                IPropertyReferenceOperation { Instance: IInstanceReferenceOperation } => true,
                IInvocationOperation { Instance: IInstanceReferenceOperation } => true,
                _ => false,
            });
        var type = model.GetTypeInfo(plan.ExtractionRoot).Type ?? model.GetTypeInfo(plan.ExtractionRoot).ConvertedType;
        plan.CanExtract = !writes && !externalDeclarations && !unsafeCapture && !structThis
            && (unavailable.Length == 0 || site is not null) && IsRenderable(type)
            && !HasNullableDependency(plan.ExtractionRoot, model)
            && plan.BridgeVariables.All(symbol => IsRenderable(SymbolType(symbol)));
        if (plan.MemberInitializer is not null && symbols.Any(symbol => symbol is ILocalSymbol or IParameterSymbol)) plan.CanExtract = false;

        var expansion = FindExpansionRoot(plan);
        if (expansion is not null && SafeSyntax(expansion, model, token) && CanLower(expansion, model)
            && SupportsExpansion(expansion, plan, model)) plan.ExpansionRoot = expansion;
        return plan.CanExtract || plan.CanExpand ? plan : null;
    }

    // CR04 uses the existing evaluator for independently reached aggregate
    // components. This factory does not change CR03 ownership or feasibility.
    internal static OperationPlan? ProjectionComponent(ExpressionSyntax owner, SemanticModel model, CancellationToken token) =>
        SafeSyntax(owner, model, token) && CanLower(owner, model) ? new OperationPlan(owner, model) : null;

    private static bool RequiresConstant(ExpressionSyntax owner) => owner.Ancestors()
        .TakeWhile(node => node is not AnonymousFunctionExpressionSyntax and not BaseMethodDeclarationSyntax
            and not LocalFunctionStatementSyntax and not AccessorDeclarationSyntax)
        .Any(node => node is ConstantPatternSyntax or RelationalPatternSyntax or CaseSwitchLabelSyntax
            || node is LocalDeclarationStatementSyntax local && local.Modifiers.Any(SyntaxKind.ConstKeyword)
            || node is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword));

    private static ExpressionSyntax? FindExpansionRoot(OperationPlan plan)
    {
        if (plan.ExpressionBody is ArrowExpressionClauseSyntax arrow) return arrow.Expression;
        if (plan.ExpressionBody is LambdaExpressionSyntax { Body: ExpressionSyntax body }) return body;
        return plan.Statement switch
        {
            LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 } local when local.Modifiers.Count == 0
                && local.UsingKeyword.IsKind(SyntaxKind.None) && local.AwaitKeyword.IsKind(SyntaxKind.None) => local.Declaration.Variables[0].Initializer?.Value,
            ReturnStatementSyntax statement => statement.Expression,
            ExpressionStatementSyntax statement => statement.Expression,
            IfStatementSyntax statement when statement.Condition.Span.Contains(plan.Owner.Span) => statement.Condition,
            ThrowStatementSyntax statement => statement.Expression,
            _ => null,
        };
    }

    private static bool SupportsExpansion(ExpressionSyntax root, OperationPlan plan, SemanticModel model)
    {
        if (!root.Span.Contains(plan.Owner.Span)) return false;
        foreach (var call in OperationFacts.EvaluationNodes(root).OfType<ExpressionSyntax>())
        {
            if (call is InvocationExpressionSyntax invocation && NeedsArgumentLowering(invocation, model)
                && !CanSequenceCall(model.GetOperation(invocation))) return false;
            if (call is BaseObjectCreationExpressionSyntax creation && creation.ArgumentList?.Arguments.Any(argument => HasLowering(argument.Expression, model)) == true
                && !CanSequenceCall(model.GetOperation(creation))) return false;
        }

        return true;
    }

    public static bool NeedsArgumentLowering(InvocationExpressionSyntax invocation, SemanticModel model, bool conditions = false) =>
        invocation.ArgumentList.Arguments.Any(argument => HasLowering(argument.Expression, model, conditions))
        || invocation.Expression is MemberAccessExpressionSyntax member && HasLowering(member.Expression, model, conditions);

    public static bool HasLowering(ExpressionSyntax expression, SemanticModel model, bool conditions = false) =>
        OperationFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>().Any(node => OperationFacts.IsProducer(node, model)
            || OperationFacts.IsCalculation(node, model) || node is ConditionalExpressionSyntax
            || conditions && ConditionFacts.IsProducer(node, model));

    internal static bool HasConditionReason(ExpressionSyntax expression, SemanticModel model) =>
        OperationFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>().Any(value => model.GetTypeInfo(value).Type?.SpecialType == SpecialType.System_Boolean
            && ConditionFacts.Create(value, model, default)?.HasReason == true);

    private static bool CanSequenceCall(IOperation? operation)
    {
        var arguments = operation switch
        {
            IInvocationOperation invocation => invocation.Arguments,
            IObjectCreationOperation creation => creation.Arguments,
            _ => default,
        };
        return !arguments.IsDefault && arguments.All(argument => argument.Parameter?.RefKind == RefKind.None
                && argument.ArgumentKind != ArgumentKind.ParamArray && argument.Parameter.Type.IsRefLikeType == false)
            && operation is not IInvocationOperation { Instance.Type.IsValueType: true };
    }

    private static bool CanLower(ExpressionSyntax expression, SemanticModel model)
    {
        var conditions = HasConditionReason(expression, model);
        foreach (var node in OperationFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>())
        {
            if (!HasLowering(node, model, conditions)) continue;
            switch (node)
            {
                case ParenthesizedExpressionSyntax or IdentifierNameSyntax or LiteralExpressionSyntax or ThisExpressionSyntax
                    or BaseExpressionSyntax or DefaultExpressionSyntax or TypeOfExpressionSyntax: break;
                case InvocationExpressionSyntax invocation:
                    if (model.GetOperation(invocation) is not IInvocationOperation { TargetMethod.ReturnsByRef: false, TargetMethod.ReturnsByRefReadonly: false }) return false;
                    if (NeedsArgumentLowering(invocation, model, conditions) && !CanSequenceCall(model.GetOperation(invocation))) return false;
                    break;
                case BaseObjectCreationExpressionSyntax creation:
                    if (creation.Initializer is not null || model.GetOperation(creation) is not IObjectCreationOperation) return false;
                    if (creation.ArgumentList?.Arguments.Any(argument => HasLowering(argument.Expression, model)) == true
                        && !CanSequenceCall(model.GetOperation(creation))) return false;
                    break;
                case BinaryExpressionSyntax binary:
                    if (binary.IsKind(SyntaxKind.CoalesceExpression) || model.GetOperation(binary) is not IBinaryOperation { OperatorMethod: null, IsLifted: false }
                        || model.GetTypeInfo(binary).Type?.TypeKind == TypeKind.Dynamic) return false;
                    break;
                case PrefixUnaryExpressionSyntax unary:
                    if (model.GetOperation(unary) is not IUnaryOperation { OperatorMethod: null, IsLifted: false }
                        && !OperationFacts.IsMutation(unary)) return false;
                    break;
                case PostfixUnaryExpressionSyntax postfix:
                    if (!postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) && !OperationFacts.IsMutation(postfix)) return false;
                    break;
                case ConditionalExpressionSyntax conditional:
                    if (!IsRenderable(model.GetTypeInfo(conditional).Type ?? model.GetTypeInfo(conditional).ConvertedType)) return false;
                    break;
                case CastExpressionSyntax cast:
                    if (model.GetConversion(cast).IsUserDefined) return false;
                    break;
                case MemberAccessExpressionSyntax member:
                    if (HasLowering(member.Expression, model) && model.GetTypeInfo(member.Expression).Type?.IsValueType == true) return false;
                    break;
                case ElementAccessExpressionSyntax element:
                    if (model.GetTypeInfo(element.Expression).Type?.IsReferenceType != true
                        || model.GetSymbolInfo(element).Symbol is IPropertySymbol { ReturnsByRef: true } or IPropertySymbol { ReturnsByRefReadonly: true }) return false;
                    break;
                case AssignmentExpressionSyntax assignment:
                    if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) || !SafeLeft(assignment.Left, model)) return false;
                    break;
                case InterpolatedStringExpressionSyntax interpolation:
                    if (model.GetTypeInfo(interpolation).ConvertedType?.SpecialType != SpecialType.System_String) return false;
                    if (interpolation.Contents.OfType<InterpolationSyntax>().Count() > 1
                        && interpolation.Contents.OfType<InterpolationSyntax>().Any(item => HasLowering(item.Expression, model))) return false;
                    break;
                case ThrowExpressionSyntax: break;
                default: return false;
            }
        }

        return true;
    }

    private static bool SafeLeft(ExpressionSyntax left, SemanticModel model) => left switch
    {
        IdentifierNameSyntax => model.GetSymbolInfo(left).Symbol is ILocalSymbol { RefKind: RefKind.None }
            or IParameterSymbol { RefKind: RefKind.None } or IFieldSymbol or IPropertySymbol,
        MemberAccessExpressionSyntax member => model.GetTypeInfo(member.Expression).Type?.IsReferenceType == true,
        ElementAccessExpressionSyntax element => model.GetTypeInfo(element.Expression).Type?.IsReferenceType == true,
        _ => false,
    };

    private static bool SafeSyntax(ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        if (expression.ContainsDiagnostics || expression.ContainsDirectives || ConditionHosts.IsInsideExpressionTree(expression, model)) return false;
        if (expression.Ancestors().Any(node => node is ConstructorInitializerSyntax or AttributeSyntax or UnsafeStatementSyntax or FixedStatementSyntax)) return false;
        var formattable = model.Compilation.GetTypeByMetadataName("System.FormattableString");
        if (expression.AncestorsAndSelf().OfType<CastExpressionSyntax>().Any(cast =>
                SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(cast).Type, formattable))) return false;
        if (expression.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().Any(call => CallerInformation.HasDefaults(call, model))) return false;
        if (expression.DescendantTrivia().Any(trivia => trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia or SyntaxKind.DisabledTextTrivia)) return false;
        foreach (var value in OperationFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>())
        {
            if (value is AwaitExpressionSyntax or RefExpressionSyntax or StackAllocArrayCreationExpressionSyntax
                or ImplicitStackAllocArrayCreationExpressionSyntax or PointerTypeSyntax) return false;
            var type = model.GetTypeInfo(value).Type;
            if (type is { TypeKind: TypeKind.Dynamic or TypeKind.Pointer or TypeKind.FunctionPointer or TypeKind.Error }
                || type?.IsRefLikeType == true) return false;
            if (value is InvocationExpressionSyntax invocation && CallerInformation.HasDefaults(invocation, model)) return false;
            if (value is InterpolatedStringExpressionSyntax && (SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(value).ConvertedType, formattable)
                || value.Ancestors().OfType<CastExpressionSyntax>().Any(cast => SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(cast).Type, formattable)))) return false;
        }

        return !model.GetDiagnostics(expression.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    private static bool HasNullableDependency(ExpressionSyntax expression, SemanticModel model) =>
        OperationFacts.EvaluationNodes(expression).OfType<IdentifierNameSyntax>().Any(identifier =>
            SymbolType(model.GetSymbolInfo(identifier).Symbol)?.NullableAnnotation == NullableAnnotation.Annotated
            && model.GetTypeInfo(identifier).Nullability.FlowState == NullableFlowState.NotNull
            && model.GetTypeInfo(identifier).Type?.IsReferenceType == true
            && !IsInitializedVar(model.GetSymbolInfo(identifier).Symbol));

    private static bool IsInitializedVar(ISymbol? symbol) => symbol is ILocalSymbol local
        && local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax { Initializer.Value: ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
            or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax } variable
        && variable.Parent is VariableDeclarationSyntax { Type.IsVar: true };

    private static bool CannotCapture(ISymbol symbol) => symbol is IParameterSymbol { RefKind: not RefKind.None }
        or ILocalSymbol { RefKind: not RefKind.None } || SymbolType(symbol)?.IsRefLikeType == true;

    internal static ITypeSymbol? SymbolType(ISymbol? symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null,
    };

    private static bool IsRenderable(ITypeSymbol? type) => type is not null && !type.IsAnonymousType
        && !type.IsRefLikeType && type.TypeKind is not TypeKind.Dynamic and not TypeKind.Pointer and not TypeKind.FunctionPointer and not TypeKind.Error;
}
