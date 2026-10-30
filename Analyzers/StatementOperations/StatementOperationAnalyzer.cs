using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StatementOperationAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor NestedRule = Rule(DiagnosticIds.NestedArgumentOperation,
        "Name nested argument operations", "Calculate this argument in named steps");
    public static readonly DiagnosticDescriptor ConditionalRule = Rule(DiagnosticIds.UnnamedConditionalValue,
        "Name conditional values", "Assign this conditional value to a named variable");
    public static readonly DiagnosticDescriptor CombinedRule = Rule(DiagnosticIds.CombinedStatementOperations,
        "Separate statement operations", "Calculate this value in named steps");
    internal static readonly ImmutableArray<DiagnosticDescriptor> Priority = ImmutableArray.Create(ConditionalRule, NestedRule, CombinedRule);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => Priority;

    // Ownership asks every enclosing candidate for its selection, and each
    // selection plans a rewrite. Without this cache, a nested expression's plan is
    // rebuilt once per nested descendant. The analyzer driver reuses one semantic
    // model per tree, so the cache lives exactly as long as that analysis.
    private static readonly ConditionalWeakTable<SemanticModel, ConcurrentDictionary<ExpressionSyntax, DiagnosticDescriptor?>> Selections = new();

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.Argument, SyntaxKind.ConditionalExpression,
            SyntaxKind.AddExpression, SyntaxKind.SubtractExpression, SyntaxKind.MultiplyExpression,
            SyntaxKind.DivideExpression, SyntaxKind.ModuloExpression, SyntaxKind.LeftShiftExpression,
            SyntaxKind.RightShiftExpression, SyntaxKind.UnsignedRightShiftExpression, SyntaxKind.BitwiseAndExpression,
            SyntaxKind.BitwiseOrExpression, SyntaxKind.ExclusiveOrExpression, SyntaxKind.UnaryPlusExpression,
            SyntaxKind.UnaryMinusExpression, SyntaxKind.BitwiseNotExpression, SyntaxKind.CastExpression,
            SyntaxKind.InterpolatedStringExpression, SyntaxKind.SimpleAssignmentExpression,
            SyntaxKind.PreIncrementExpression, SyntaxKind.PostIncrementExpression,
            SyntaxKind.PreDecrementExpression, SyntaxKind.PostDecrementExpression);
    }

    internal static ExpressionSyntax? Owner(SyntaxNode node) => node is ArgumentSyntax argument ? argument.Expression : node as ExpressionSyntax;

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var expression = Owner(context.Node);
        if (expression is null) return;
        if (context.Node is ExpressionSyntax { Parent: ArgumentSyntax }) return;
        var selections = Selections.GetValue(context.SemanticModel, _ => new());
        var rule = CachedSelect(expression, context, selections);
        if (rule is null) return;
        // Ownership is independent of callback order. An unsafe outer value does
        // not hide a separately feasible inner argument or combination.
        foreach (var ancestor in expression.Ancestors().TakeWhile(node => node is not AnonymousFunctionExpressionSyntax
                     and not LocalFunctionStatementSyntax and not StatementSyntax and not MemberDeclarationSyntax))
        {
            var enclosing = Owner(ancestor);
            if (enclosing is null || enclosing == expression || !IsCandidate(ancestor)) continue;
            if (CachedSelect(enclosing, context, selections) is not null) return;
        }
        context.ReportDiagnostic(Diagnostic.Create(rule, expression.GetLocation()));
    }

    private static DiagnosticDescriptor? CachedSelect(ExpressionSyntax expression, SyntaxNodeAnalysisContext context,
        ConcurrentDictionary<ExpressionSyntax, DiagnosticDescriptor?> selections) =>
        selections.GetOrAdd(expression, value => Select(value, context.SemanticModel,
            context.Options.AnalyzerConfigOptionsProvider, context.CancellationToken));

    internal static bool IsCandidate(SyntaxNode node) => node is ArgumentSyntax or ConditionalExpressionSyntax
        or BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
        or CastExpressionSyntax or InterpolatedStringExpressionSyntax or AssignmentExpressionSyntax;

    internal static DiagnosticDescriptor? Select(ExpressionSyntax expression, SemanticModel model,
        AnalyzerConfigOptionsProvider options, CancellationToken token)
    {
        if (expression.DescendantNodesAndSelf().Concat(expression.Ancestors()).OfType<InterpolatedStringExpressionSyntax>()
            .Any(message => ExceptionContextAnalyzer.IsMessage(message, model))) return null;
        var facts = OperationFacts.Create(expression, model);
        if (!facts.HasReason || OperationPlan.Create(expression, model, token) is null) return null;
        foreach (var rule in Priority)
        {
            if (rule == ConditionalRule ? facts.Conditional is null : rule == NestedRule ? !facts.Nested : !facts.Combined) continue;
            if (!RuleSeverity.IsEnabled(rule.Id, expression.SyntaxTree, model.Compilation, options, token)) continue;
            return rule;
        }
        return null;
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
