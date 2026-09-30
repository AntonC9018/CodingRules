using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal sealed class ConditionRewritePlan
{
    public ExpressionSyntax Expression { get; }
    public SemanticModel Model { get; }
    public StatementSyntax? Statement { get; }
    public SyntaxNode DeclarationSpace { get; }
    public bool CanExtract { get; private set; }
    public bool CanExpand { get; private set; }
    public bool NeedsFlowPreservingIf { get; private set; }
    public List<ILocalSymbol> BridgeVariables { get; } = new();

    private ConditionRewritePlan(ExpressionSyntax expression, SemanticModel model)
    {
        Expression = expression;
        Model = model;
        Statement = ConditionHosts.FindStatement(expression);
        DeclarationSpace = expression.Ancestors().FirstOrDefault(node => node is BaseMethodDeclarationSyntax
            or AccessorDeclarationSyntax or LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax)
            ?? expression.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()
            ?? expression.SyntaxTree.GetRoot();
    }

    public static ConditionRewritePlan? Create(ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        if (ConditionHosts.IsInsideExpressionTree(expression, model)
            || expression.ContainsDiagnostics || expression.ContainsDirectives
            || expression.DescendantTrivia().Any(trivia => trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.DisabledTextTrivia))
            || ConditionFacts.EvaluationNodes(expression).Any(node => node is AwaitExpressionSyntax or RefExpressionSyntax)
            || model.GetDiagnostics(expression.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return null;
        }

        var plan = new ConditionRewritePlan(expression, model);
        foreach (var invocation in ConditionFacts.EvaluationNodes(expression).OfType<InvocationExpressionSyntax>())
        {
            var hasRefArguments = invocation.ArgumentList.Arguments.Any(argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None));
            var hasNestedProducer = ConditionFacts.EvaluationNodes(invocation).Skip(1).Any(node => ConditionFacts.IsProducer(node, model));
            if (hasRefArguments && hasNestedProducer)
            {
                return null;
            }
        }

        if (expression.Ancestors().Any(node => node is CheckedExpressionSyntax or UnsafeStatementSyntax or FixedStatementSyntax))
        {
            return null;
        }

        if (plan.DeclarationSpace.DescendantNodes().Any(node => node is LabeledStatementSyntax or GotoStatementSyntax)
            || HasCallerInformation(expression, model))
        {
            return null;
        }

        var initializer = expression.Parent as EqualsValueClauseSyntax;
        if (initializer?.Parent is VariableDeclaratorSyntax declarator
            && declarator.Parent?.Parent is LocalDeclarationStatementSyntax local
            && (local.Modifiers.Count != 0 || !local.UsingKeyword.IsKind(SyntaxKind.None) || !local.AwaitKeyword.IsKind(SyntaxKind.None)))
        {
            return null;
        }

        if (initializer?.Parent?.Parent?.Parent is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return null;
        }

        var flow = model.AnalyzeDataFlow(expression);
        if (flow?.Succeeded != true)
        {
            return null;
        }

        var declared = new HashSet<ISymbol>(flow.VariablesDeclared, SymbolEqualityComparer.Default);
        var referencedMembers = ConditionFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>()
            .Select(node => model.GetSymbolInfo(node).Symbol).Where(symbol => symbol is IFieldSymbol or IPropertySymbol);
        var symbols = flow.ReadInside.Concat(flow.WrittenInside).Concat(referencedMembers.OfType<ISymbol>())
            .Distinct(SymbolEqualityComparer.Default).ToList();
        var externalDeclarations = declared.Where(symbol => HasExternalReference(symbol, expression, plan.DeclarationSpace, model)).ToList();
        var writes = flow.WrittenInside.Any(symbol => !declared.Contains(symbol));
        var unsafeCapture = symbols.Any(symbol => symbol is IParameterSymbol { RefKind: not RefKind.None }
            or ILocalSymbol { RefKind: not RefKind.None } || SymbolType(symbol)?.IsRefLikeType == true);
        var containing = model.GetEnclosingSymbol(expression.SpanStart, token);
        var structThis = containing?.ContainingType?.IsValueType == true
            && ConditionFacts.EvaluationNodes(expression).Any(node => node is ThisExpressionSyntax
                || model.GetSymbolInfo(node, token).Symbol is IFieldSymbol { IsStatic: false }
                    or IPropertySymbol { IsStatic: false } or IMethodSymbol { IsStatic: false });
        var nullableDependency = expression.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault() is { } ifHost
            && symbols.Any(symbol => SymbolType(symbol)?.NullableAnnotation == NullableAnnotation.Annotated
                && ifHost.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => !expression.Span.Contains(identifier.Span)
                    && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol)
                    && model.GetTypeInfo(identifier).Nullability.FlowState == NullableFlowState.NotNull));

        plan.CanExtract = !writes && !unsafeCapture && !structThis && externalDeclarations.Count == 0 && !nullableDependency;
        plan.NeedsFlowPreservingIf = externalDeclarations.Count != 0 || nullableDependency;
        plan.CanExpand = plan.Statement is not null;
        if (expression.Parent is ArrowExpressionClauseSyntax or LambdaExpressionSyntax)
        {
            plan.CanExpand = true;
        }

        if (expression.Parent is CatchFilterClauseSyntax or WhenClauseSyntax)
        {
            plan.CanExpand = false;
            foreach (var symbol in symbols.OfType<ILocalSymbol>())
            {
                if (!model.LookupSymbols(plan.Statement?.SpanStart ?? plan.DeclarationSpace.SpanStart, name: symbol.Name)
                    .Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, symbol)))
                {
                    plan.BridgeVariables.Add(symbol);
                }
            }

            if (writes || unsafeCapture || plan.BridgeVariables.Any(symbol => symbol.Type.IsRefLikeType))
            {
                plan.CanExtract = false;
            }
        }

        if (plan.NeedsFlowPreservingIf)
        {
            plan.CanExpand = plan.Statement is IfStatementSyntax { Else: null } flowIf
                && IsFlowPreserving(flowIf, externalDeclarations, plan.DeclarationSpace, model);
            if (plan.CanExpand && plan.Statement?.Parent is BlockSyntax block && ReturnDecisionAnalysis.IsFunctionBody(block)
                && ReturnDecisionAnalysis.TryGetFinalReturn(block, out var finalReturn)
                && ReturnDecisionAnalysis.HasTopLevelGuard(block)
                && ReturnDecisionAnalysis.TryGetKind(finalReturn, model, token, out _)
                && !ReturnDecisionAnalysis.HasTopLevelGuard(block.WithStatements(block.Statements.Remove(plan.Statement))))
            {
                plan.CanExpand = false;
            }
        }

        if (plan.Statement is not null && expression.Parent is not CatchFilterClauseSyntax and not WhenClauseSyntax)
        {
            // A declaration placed before the statement cannot capture a local
            // first declared by that statement. The for initializer has its own plan.
            if (symbols.Any(symbol => symbol is ILocalSymbol && !declared.Contains(symbol)
                && !model.LookupSymbols(plan.Statement.SpanStart, name: symbol.Name)
                    .Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, symbol)))
                && plan.Statement is not ForStatementSyntax { Declaration: not null })
            {
                plan.CanExtract = false;
            }
        }

        if (plan.Statement is ForStatementSyntax forLoop && forLoop.Declaration is not null
            && (forLoop.Declaration.Type is RefTypeSyntax || forLoop.Initializers.Count != 0))
        {
            plan.CanExtract = false;
        }

        if (plan.Statement is DoStatementSyntax doLoop && doLoop.Statement.DescendantNodesAndSelf()
            .OfType<ContinueStatementSyntax>().Any(statement => statement.Ancestors().First(node =>
                node is WhileStatementSyntax or ForStatementSyntax or DoStatementSyntax or ForEachStatementSyntax) == doLoop))
        {
            plan.CanExpand = false;
        }

        if (plan.CanExpand && !ConditionSiteRewrites.SupportsExpansion(plan))
        {
            plan.CanExpand = false;
        }

        return plan;
    }

    private static ITypeSymbol? SymbolType(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null,
    };

    private static bool HasExternalReference(ISymbol symbol, ExpressionSyntax expression, SyntaxNode scope, SemanticModel model) =>
        scope.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => !expression.Span.Contains(identifier.Span)
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol));

    private static bool IsFlowPreserving(IfStatementSyntax statement, List<ISymbol> external, SyntaxNode scope, SemanticModel model)
    {
        var expression = ConditionFacts.Unwrap(statement.Condition);
        if (expression is not BinaryExpressionSyntax binary)
        {
            return false;
        }

        if (binary.IsKind(SyntaxKind.LogicalAndExpression))
        {
            var leftmost = binary.Left;
            while (ConditionFacts.Unwrap(leftmost) is BinaryExpressionSyntax left && left.IsKind(SyntaxKind.LogicalAndExpression))
            {
                leftmost = left.Left;
            }

            return external.All(symbol => !HasExternalReference(symbol, statement.Condition, scope, model)
                || symbol.DeclaringSyntaxReferences.Any(reference => leftmost.Span.Contains(reference.Span))
                || !scope.DescendantNodes().OfType<IdentifierNameSyntax>().Any(identifier => identifier.SpanStart > statement.Span.End
                    && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol)));
        }

        return binary.IsKind(SyntaxKind.LogicalOrExpression) && (statement.Statement is ReturnStatementSyntax
                || statement.Statement is BlockSyntax { Statements.Count: 1 } block && block.Statements[0] is ReturnStatementSyntax)
            && statement.Statement.DescendantTrivia().All(trivia => !trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia));
    }

    private static bool HasCallerInformation(ExpressionSyntax expression, SemanticModel model)
    {
        var affectedCalls = ConditionFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>()
            .Concat(expression.Ancestors().OfType<ExpressionSyntax>())
            .Where(node => node is InvocationExpressionSyntax or ObjectCreationExpressionSyntax);
        foreach (var call in affectedCalls)
        {
            var arguments = model.GetOperation(call) switch
            {
                IInvocationOperation invocation => invocation.Arguments,
                IObjectCreationOperation creation => creation.Arguments,
                _ => default,
            };
            if (arguments.IsDefault)
            {
                continue;
            }

            if (arguments.Any(argument => argument.IsImplicit && argument.Parameter?.GetAttributes()
                .Any(attribute => attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
                    && attribute.AttributeClass.Name.StartsWith("Caller", System.StringComparison.Ordinal)) == true))
            {
                return true;
            }
        }

        return false;
    }
}
