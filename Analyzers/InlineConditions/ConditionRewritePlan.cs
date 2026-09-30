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
        if (!CanMoveSyntax(expression, model, token) || !CanLowerOperations(expression, model))
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
        var unsafeCapture = symbols.Any(CannotCapture);
        var containing = model.GetEnclosingSymbol(expression.SpanStart, token);
        var structThis = containing?.ContainingType?.IsValueType == true && CapturesThis(expression, model);
        var nullableDependency = NeedsNullableFlow(expression, symbols, model);

        plan.CanExtract = ConditionSiteRewrites.SupportsExtraction(expression);
        if (writes) plan.CanExtract = false;
        if (unsafeCapture) plan.CanExtract = false;
        if (structThis) plan.CanExtract = false;
        if (externalDeclarations.Count != 0) plan.CanExtract = false;
        if (nullableDependency) plan.CanExtract = false;
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
                && ConditionFacts.Unwrap(expression) == ConditionFacts.Unwrap(flowIf.Condition)
                && IsFlowPreserving(flowIf, externalDeclarations, plan.DeclarationSpace, model);
            if (WouldHideReturnWarning(plan, token))
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

    private static bool CanLowerOperations(ExpressionSyntax expression, SemanticModel model)
    {
        foreach (var node in ConditionFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>())
        {
            if (!ConditionFacts.EvaluationNodes(node).Skip(1).Any(child => ConditionFacts.IsProducer(child, model)))
            {
                continue;
            }

            var supported = node switch
            {
                ParenthesizedExpressionSyntax => true,
                BinaryExpressionSyntax binary => !binary.IsKind(SyntaxKind.CoalesceExpression)
                    && model.GetOperation(binary) is IBinaryOperation operation
                    && (operation.OperatorMethod is null || model.ClassifyConversion(binary.Left, operation.LeftOperand.Type!).IsIdentity
                        && model.ClassifyConversion(binary.Right, operation.RightOperand.Type!).IsIdentity)
                    && model.GetTypeInfo(binary).Type?.TypeKind != TypeKind.Dynamic,
                PrefixUnaryExpressionSyntax unary => unary.Kind() is SyntaxKind.LogicalNotExpression
                    or SyntaxKind.UnaryMinusExpression or SyntaxKind.UnaryPlusExpression or SyntaxKind.BitwiseNotExpression,
                ConditionalExpressionSyntax conditional => model.GetTypeInfo(conditional).Type is { IsRefLikeType: false }
                    && conditional.WhenTrue is not ThrowExpressionSyntax && conditional.WhenFalse is not ThrowExpressionSyntax,
                InvocationExpressionSyntax invocation => model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method
                    && (method.IsStatic || method.ContainingType.IsReferenceType) && !method.ReturnsByRef && !method.ReturnsByRefReadonly,
                ElementAccessExpressionSyntax element => model.GetTypeInfo(element.Expression).Type?.IsReferenceType == true
                    && model.GetSymbolInfo(element).Symbol is not IPropertySymbol { ReturnsByRef: true }
                    and not IPropertySymbol { ReturnsByRefReadonly: true },
                MemberAccessExpressionSyntax => true,
                CastExpressionSyntax => true,
                _ => false,
            };
            if (!supported)
            {
                return false;
            }
        }

        return true;
    }

    private static ITypeSymbol? SymbolType(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        _ => null,
    };

    private static bool CanMoveSyntax(ExpressionSyntax expression, SemanticModel model, CancellationToken token)
    {
        if (ConditionHosts.IsInsideExpressionTree(expression, model)) return false;
        if (expression.ContainsDiagnostics) return false;
        if (expression.ContainsDirectives) return false;
        if (expression.DescendantTrivia().Any(IsUnsafeTrivia)) return false;
        if (ConditionFacts.EvaluationNodes(expression).Any(node => node is AwaitExpressionSyntax or RefExpressionSyntax)) return false;
        if (model.GetDiagnostics(expression.Span, token).Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)) return false;
        return true;
    }

    private static bool IsUnsafeTrivia(SyntaxTrivia trivia) => trivia.Kind() is SyntaxKind.SingleLineCommentTrivia
        or SyntaxKind.MultiLineCommentTrivia or SyntaxKind.DisabledTextTrivia;

    private static bool CannotCapture(ISymbol symbol)
    {
        if (symbol is IParameterSymbol { RefKind: not RefKind.None }) return true;
        if (symbol is ILocalSymbol { RefKind: not RefKind.None }) return true;
        if (symbol is not ILocalSymbol and not IParameterSymbol) return false;
        if (SymbolType(symbol)?.IsRefLikeType == true) return true;
        return false;
    }

    private static bool NeedsNullableFlow(ExpressionSyntax expression, List<ISymbol> symbols, SemanticModel model)
    {
        var host = expression.Ancestors().OfType<IfStatementSyntax>().FirstOrDefault();
        if (host is null) return false;
        foreach (var symbol in symbols)
        {
            if (SymbolType(symbol)?.NullableAnnotation != NullableAnnotation.Annotated) continue;
            foreach (var identifier in host.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (expression.Span.Contains(identifier.Span)) continue;
                if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, symbol)) continue;
                if (model.GetTypeInfo(identifier).Nullability.FlowState == NullableFlowState.NotNull) return true;
            }
        }

        return false;
    }

    private static bool WouldHideReturnWarning(ConditionRewritePlan plan, CancellationToken token)
    {
        if (!plan.CanExpand) return false;
        if (plan.Statement?.Parent is not BlockSyntax block) return false;
        if (!ReturnDecisionAnalysis.IsFunctionBody(block)) return false;
        if (!ReturnDecisionAnalysis.TryGetFinalReturn(block, out var finalReturn)) return false;
        if (!ReturnDecisionAnalysis.HasTopLevelGuard(block)) return false;
        if (!ReturnDecisionAnalysis.TryGetKind(finalReturn, plan.Model, token, out _)) return false;
        var remaining = block.WithStatements(block.Statements.Remove(plan.Statement));
        if (ReturnDecisionAnalysis.HasTopLevelGuard(remaining)) return false;
        return true;
    }

    private static bool CapturesThis(ExpressionSyntax expression, SemanticModel model)
    {
        foreach (var node in ConditionFacts.EvaluationNodes(expression).OfType<ExpressionSyntax>())
        {
            var instance = model.GetOperation(node) switch
            {
                IFieldReferenceOperation field => field.Instance,
                IPropertyReferenceOperation property => property.Instance,
                IInvocationOperation invocation => invocation.Instance,
                IInstanceReferenceOperation reference => reference,
                _ => null,
            };
            if (instance is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
            {
                return true;
            }
        }

        return false;
    }

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
