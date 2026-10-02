using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class SpanPlan
{
    public InvocationExpressionSyntax Trim { get; }
    public ExpressionSyntax Inspection { get; }
    public ExpressionSyntax Receiver { get; }
    public string? Constant { get; }
    public bool Negate { get; }
    public StatementSyntax? Statement { get; }
    public SyntaxNode? ExpressionBody { get; }
    public TypeDeclarationSyntax? MemberType { get; }
    public SyntaxNode Space { get; }
    public SpanCatalog Catalog { get; }
    public IMethodSymbol SpanTrim { get; }
    public bool Nullable { get; }
    public bool StaticLocal { get; }

    private SpanPlan(InvocationExpressionSyntax trim, ExpressionSyntax inspection, ExpressionSyntax receiver, string? constant,
        bool negate, SemanticModel model, SpanCatalog catalog, IMethodSymbol spanTrim)
    {
        Trim = trim; Inspection = inspection; Receiver = receiver; Constant = constant; Negate = negate; Catalog = catalog; SpanTrim = spanTrim;
        Statement = ConditionHosts.FindStatement(inspection);
        ExpressionBody = inspection.Ancestors().TakeWhile(node => node is not StatementSyntax and not MemberDeclarationSyntax)
            .FirstOrDefault(node => node is ArrowExpressionClauseSyntax or LambdaExpressionSyntax { Body: ExpressionSyntax });
        var initializer = inspection.Ancestors().TakeWhile(node => node is not StatementSyntax and not ArrowExpressionClauseSyntax and not AnonymousFunctionExpressionSyntax)
            .OfType<EqualsValueClauseSyntax>().FirstOrDefault(value => value.Parent is PropertyDeclarationSyntax || value.Parent?.Parent?.Parent is FieldDeclarationSyntax);
        MemberType = initializer?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        Space = inspection.FirstAncestorOrSelf<TypeDeclarationSyntax>() ?? inspection.SyntaxTree.GetRoot();
        var language = ((CSharpParseOptions)inspection.SyntaxTree.Options).LanguageVersion;
        StaticLocal = language >= LanguageVersion.CSharp8;
        Nullable = language >= LanguageVersion.CSharp8 && (model.GetNullableContext(inspection.SpanStart) & NullableContext.AnnotationsEnabled) != 0;
    }

    public static SpanPlan? Create(InvocationExpressionSyntax trim, SemanticModel model, SpanCatalog catalog, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (trim.Expression is not MemberAccessExpressionSyntax access || trim.ArgumentList.Arguments.Count != 0
            || model.GetOperation(trim, token) is not IInvocationOperation call || catalog.Replacement(call.TargetMethod) is not { } spanTrim) return null;
        var value = (ExpressionSyntax)trim;
        while (value.Parent is ParenthesizedExpressionSyntax parentheses) value = parentheses;
        ExpressionSyntax inspection;
        string? constant = null;
        var negate = false;
        if (value.Parent is MemberAccessExpressionSyntax length && length.Expression == value
            && model.GetOperation(length, token) is IPropertyReferenceOperation property
            && SymbolEqualityComparer.Default.Equals(property.Property, catalog.Length)) inspection = length;
        else if (value.Parent is BinaryExpressionSyntax binary && binary.Kind() is SyntaxKind.EqualsExpression or SyntaxKind.NotEqualsExpression
            && model.GetOperation(binary, token) is IBinaryOperation { OperatorMethod: null, IsLifted: false } operation
            && operation.LeftOperand.Type?.SpecialType == SpecialType.System_String && operation.RightOperand.Type?.SpecialType == SpecialType.System_String
            && catalog.Equal is not null && catalog.ConstantSpan is not null)
        {
            var other = binary.Left == value ? binary.Right : binary.Left;
            if (model.GetConstantValue(other, token) is not { HasValue: true, Value: string text }) return null;
            constant = text; inspection = binary; negate = binary.IsKind(SyntaxKind.NotEqualsExpression);
        }
        else return null;
        if (access.Expression.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(node =>
                model.GetOperation(node, token) is IInvocationOperation nested && catalog.Replacement(nested.TargetMethod) is not null)) return null;
        if (inspection.ContainsDiagnostics || inspection.ContainsDirectives || ConditionHosts.IsInsideExpressionTree(inspection, model)
            || inspection.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
            || inspection.Ancestors().Any(node => node is ConstructorInitializerSyntax or AttributeSyntax or UnsafeStatementSyntax or FixedStatementSyntax
                || node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
            || inspection.Ancestors().OfType<MemberDeclarationSyntax>().Any(node => node.Modifiers.Any(SyntaxKind.UnsafeKeyword))
            || model.GetDiagnostics(inspection.Span, token).Any(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)) return null;
        // The new parameterized call would introduce a CR0300 operation in an
        // outer argument, or around a producing receiver. Keep older policy intact.
        if (inspection.Ancestors().TakeWhile(node => node is not StatementSyntax and not ArrowExpressionClauseSyntax and not AnonymousFunctionExpressionSyntax)
                .Any(node => node is ArgumentSyntax || node is ExpressionSyntax expression
                    && (OperationFacts.IsCalculation(expression, model) || expression is InterpolatedStringExpressionSyntax
                        || expression is CastExpressionSyntax cast && OperationFacts.IsSupportedCast(cast, model)))
            || OperationFacts.EvaluationNodes(access.Expression).OfType<ExpressionSyntax>().Any(node => OperationFacts.IsProducer(node, model))) return null;
        var plan = new SpanPlan(trim, inspection, access.Expression, constant, negate, model, catalog, spanTrim);
        if (plan.Statement is null && plan.ExpressionBody is null && plan.MemberType is null
            || plan.MemberType is null && ((CSharpParseOptions)trim.SyntaxTree.Options).LanguageVersion < LanguageVersion.CSharp7
            || plan.Space.DescendantNodes().Any(node => node is GotoStatementSyntax or LabeledStatementSyntax)) return null;
        if (plan.MemberType is not null && model.GetNullableContext(plan.MemberType.CloseBraceToken.SpanStart)
            != model.GetNullableContext(inspection.SpanStart)) return null;
        var insertion = (SyntaxNode?)plan.Statement ?? plan.ExpressionBody ?? plan.MemberType!;
        return CallerInformation.AffectedDefaults(inspection, insertion, model) || NamedArgumentObservers.Affected(inspection, model, token) ? null : plan;
    }
}
