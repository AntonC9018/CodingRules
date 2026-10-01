using System.Collections.Immutable;
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
        var rule = Select(expression, context.SemanticModel, context.Options.AnalyzerConfigOptionsProvider, context.CancellationToken);
        if (rule is null) return;
        // Ownership is independent of callback order. An unsafe outer value does
        // not hide a separately feasible inner argument or combination.
        foreach (var ancestor in expression.Ancestors().TakeWhile(node => node is not AnonymousFunctionExpressionSyntax
                     and not LocalFunctionStatementSyntax and not StatementSyntax and not MemberDeclarationSyntax))
        {
            var enclosing = Owner(ancestor);
            if (enclosing is null || enclosing == expression || !IsCandidate(ancestor)) continue;
            if (Select(enclosing, context.SemanticModel, context.Options.AnalyzerConfigOptionsProvider, context.CancellationToken) is not null) return;
        }
        context.ReportDiagnostic(Diagnostic.Create(rule, expression.GetLocation()));
    }

    internal static bool IsCandidate(SyntaxNode node) => node is ArgumentSyntax or ConditionalExpressionSyntax
        or BinaryExpressionSyntax or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax
        or CastExpressionSyntax or InterpolatedStringExpressionSyntax or AssignmentExpressionSyntax;

    internal static DiagnosticDescriptor? Select(ExpressionSyntax expression, SemanticModel model,
        AnalyzerConfigOptionsProvider options, CancellationToken token)
    {
        var facts = OperationFacts.Create(expression, model);
        if (!facts.HasReason || OperationPlan.Create(expression, model, token) is null) return null;
        foreach (var rule in Priority)
        {
            if (rule == ConditionalRule ? facts.Conditional is null : rule == NestedRule ? !facts.Nested : !facts.Combined) continue;
            var severity = model.Compilation.Options.SyntaxTreeOptionsProvider?.TryGetDiagnosticValue(expression.SyntaxTree,
                rule.Id, token, out var treeSeverity) == true ? treeSeverity
                : model.Compilation.Options.SpecificDiagnosticOptions.TryGetValue(rule.Id, out var value) ? value : ReportDiagnostic.Default;
            if (severity == ReportDiagnostic.Suppress || options.GetOptions(expression.SyntaxTree)
                    .TryGetValue("dotnet_diagnostic." + rule.Id + ".severity", out var configured) && configured == "none") continue;
            return rule;
        }
        return null;
    }

    private static DiagnosticDescriptor Rule(string id, string title, string message) =>
        new(id, title, message, "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
