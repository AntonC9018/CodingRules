using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class ProjectionPlan
{
    public LambdaExpressionSyntax Lambda { get; }
    public SemanticModel Model { get; }
    public IMethodSymbol Callable { get; }
    public OperationPlan? Lowering { get; }
    public bool CanExtract => Renderable(Callable.ReturnType);
    public ProjectionPlan(LambdaExpressionSyntax lambda, SemanticModel model, IMethodSymbol callable, OperationPlan? lowering)
    {
        Lambda = lambda;
        Model = model;
        Callable = callable;
        Lowering = lowering;
    }

    public static ProjectionPlan? Create(LambdaExpressionSyntax lambda, SemanticModel model, PipelineCatalog catalog, CancellationToken token)
    {
        if (lambda.Parent is not ArgumentSyntax argument || argument.Parent?.Parent is not InvocationExpressionSyntax call
            || model.GetOperation(call) is not IInvocationOperation invocation
            || model.GetOperation(argument) is not IArgumentOperation { Parameter: not null } bound
            || !catalog.IsProjection(invocation.TargetMethod, bound.Parameter)
            || ConditionHosts.IsInsideExpressionTree(lambda, model) || lambda.AsyncKeyword.RawKind != 0
            || model.GetSymbolInfo(lambda).Symbol is not IMethodSymbol { ReturnsVoid: false } callable
            || !Safe(lambda, model, token)) return null;
        OperationPlan? lowering = null;
        if (lambda.Body is ExpressionSyntax body)
        {
            if (!PipelineFacts.HasComposition(body, model)) return null;
            lowering = OperationPlan.Create(body, model, token);
            if (lowering?.CanExpand != true)
            {
                if (!SupportsAggregate(body, model, token)) return null;
                lowering = OperationPlan.ProjectionComponent(AggregateComponents(body).First(), model, token);
                if (lowering is null) return null;
            }
        }
        else if (lambda.Body is not BlockSyntax block || !PipelineFacts.NeedsHelper(block, model) || !Renderable(callable.ReturnType)) return null;
        return new ProjectionPlan(lambda, model, callable, lowering);
    }

    internal static bool Renderable(ITypeSymbol type) => !type.IsAnonymousType && !type.IsRefLikeType
        && type.TypeKind is not TypeKind.Error and not TypeKind.Pointer and not TypeKind.FunctionPointer and not TypeKind.Dynamic
        && (type is not INamedTypeSymbol named || named.TypeArguments.All(Renderable))
        && (type is not IArrayTypeSymbol array || Renderable(array.ElementType));

    internal static System.Collections.Generic.IEnumerable<ExpressionSyntax> AggregateComponents(ExpressionSyntax body) => body switch
    {
        TupleExpressionSyntax tuple => tuple.Arguments.Select(argument => argument.Expression),
        BaseObjectCreationExpressionSyntax { Initializer: not null } creation => (creation.ArgumentList?.Arguments.Select(argument => argument.Expression) ?? System.Array.Empty<ExpressionSyntax>())
            .Concat(creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>().Select(assignment => assignment.Right)),
        _ => System.Array.Empty<ExpressionSyntax>(),
    };
    private static bool SupportsAggregate(ExpressionSyntax body, SemanticModel model, CancellationToken token)
    {
        if (body is not TupleExpressionSyntax && body is not BaseObjectCreationExpressionSyntax { Initializer: not null }) return false;
        if (body is BaseObjectCreationExpressionSyntax creation)
        {
            if (model.GetTypeInfo(creation).Type?.IsReferenceType != true || creation.ArgumentList?.Arguments.Count > 0
                || creation.Initializer!.Expressions.Any(value => value is not AssignmentExpressionSyntax { Left: IdentifierNameSyntax } assignment
                    || model.GetSymbolInfo(assignment.Left).Symbol is not IFieldSymbol { IsRequired: false } and not IPropertySymbol { IsRequired: false, SetMethod.IsInitOnly: false })) return false;
            if (model.GetTypeInfo(creation).Type is INamedTypeSymbol type && HasRequiredMembers(type)) return false;
        }
        return AggregateComponents(body).All(component => model.GetTypeInfo(component).ConvertedType is { } type && Renderable(type)
            && OperationPlan.ProjectionComponent(component, model, token) is not null);
    }

    private static bool Safe(LambdaExpressionSyntax lambda, SemanticModel model, CancellationToken token)
    {
        if (lambda.ContainsDiagnostics || lambda.ContainsDirectives || lambda.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
            || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            || lambda.Ancestors().Any(node => node is UnsafeStatementSyntax or FixedStatementSyntax)
            || model.GetDiagnostics(lambda.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return false;
        // Moving a whole block also moves its nested callables. Inserting the
        // external helper can move an enclosing call's original source site.
        // Both boundaries must retain caller constants before a plan is offered.
        if (CallerInformation.AffectedDefaults(lambda, InsertionSite(lambda) ?? lambda, model)) return false;
        foreach (var node in PipelineFacts.StageNodes(lambda.Body))
        {
            if (node is AwaitExpressionSyntax or YieldStatementSyntax or GotoStatementSyntax or LabeledStatementSyntax
                or RefExpressionSyntax or StackAllocArrayCreationExpressionSyntax or ImplicitStackAllocArrayCreationExpressionSyntax) return false;
            if (node is ExpressionSyntax expression)
            {
                var type = model.GetTypeInfo(expression).Type;
                if (type is not null && !Renderable(type) && !type.IsAnonymousType) return false;
                if (model.GetOperation(expression) is IInstanceReferenceOperation { Type.IsValueType: true }) return false;
            }
        }
        var flow = model.AnalyzeDataFlow(lambda.Body);
        return flow?.Succeeded == true && !flow.Captured.Any(symbol => symbol is IParameterSymbol { RefKind: not RefKind.None }
            or ILocalSymbol { RefKind: not RefKind.None } || OperationPlan.SymbolType(symbol)?.IsRefLikeType == true);
    }

    internal static SyntaxNode? InsertionSite(LambdaExpressionSyntax lambda) => lambda.Ancestors().FirstOrDefault(node =>
        node is StatementSyntax and not BlockSyntax || node is ArrowExpressionClauseSyntax
        || node is LambdaExpressionSyntax { Body: ExpressionSyntax }
        || node is EqualsValueClauseSyntax && (node.Parent is PropertyDeclarationSyntax || node.Parent?.Parent?.Parent is FieldDeclarationSyntax));

    private static bool HasRequiredMembers(INamedTypeSymbol type) => type.GetMembers().Any(member =>
        member is IFieldSymbol { IsRequired: true } or IPropertySymbol { IsRequired: true })
        || type.BaseType is not null && HasRequiredMembers(type.BaseType);
}
