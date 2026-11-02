using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

/// <summary>
/// A lambda passed to a call in a method chain stays readable while its
/// expression body reads only its own inputs. Another lambda inside that body
/// hides a second stage; naming it is a design choice, so this is a manual finding.
/// Nested catalogued predicates belong to CR0403's compact-nesting policy.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NestedChainLambdaAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.NestedChainLambda,
        "Name the inner stage of a nested chain lambda",
        "Move the lambda nested in this chain lambda into a named function or a block with named steps",
        "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var catalog = PredicateCatalog.For(start.Compilation);
            start.RegisterSyntaxNodeAction(node =>
            {
                var lambda = (LambdaExpressionSyntax)node.Node;
                if (IsReported(lambda, node.SemanticModel, catalog)
                    && !lambda.Ancestors().OfType<LambdaExpressionSyntax>().Any(outer => IsReported(outer, node.SemanticModel, catalog)))
                {
                    node.ReportDiagnostic(Diagnostic.Create(Rule, lambda.GetLocation()));
                }
            }, SyntaxKind.SimpleLambdaExpression, SyntaxKind.ParenthesizedLambdaExpression);
        });
    }

    private static bool IsReported(LambdaExpressionSyntax lambda, SemanticModel model, PredicateCatalog catalog)
    {
        if (lambda.Body is not ExpressionSyntax body || lambda.ContainsDiagnostics || !IsChainArgument(lambda)
            || ExpressionTrees.Contains(lambda, model))
        {
            return false;
        }

        // A curried body such as `row => cell => ...` is accepted, with or without parentheses.
        var nested = Parentheses.Unwrap(body).DescendantNodes().OfType<LambdaExpressionSyntax>().ToArray();
        if (nested.Length == 0)
        {
            return false;
        }

        var predicateNesting = CompoundPredicateFacts.IsPredicate(lambda, model, catalog)
            && nested.All(inner => CompoundPredicateFacts.IsPredicate(inner, model, catalog));
        return !predicateNesting;
    }

    // An argument of a call made through a member access: one link of a method chain.
    private static bool IsChainArgument(LambdaExpressionSyntax lambda)
    {
        SyntaxNode value = lambda;
        while (value.Parent is ParenthesizedExpressionSyntax parentheses)
        {
            value = parentheses;
        }

        return value.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax } };
    }
}
