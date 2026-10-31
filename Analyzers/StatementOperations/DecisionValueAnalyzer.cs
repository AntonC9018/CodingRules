using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DecisionValueAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.DecisionValueFunction,
        "Name a branching decision with a local function",
        "Calculate '{0}' in a local function with explicit returns",
        "Readability", DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "A Boolean local assigned by every branch of an immediately following decision should be a named local function. Safe cases have a validated extraction; other cases remain manual findings.");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.LocalDeclarationStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;
        if (!TryGetDecision(declaration, context.SemanticModel, out var symbol, out _)) return;
        var variable = declaration.Declaration.Variables[0];
        context.ReportDiagnostic(Diagnostic.Create(Rule, variable.Identifier.GetLocation(), symbol.Name));
    }

    public static bool TryGetDecision(LocalDeclarationStatementSyntax declaration, SemanticModel model,
        out ILocalSymbol symbol, out IfStatementSyntax decision)
    {
        symbol = null!;
        decision = null!;
        if (declaration.ContainsDiagnostics || declaration.ContainsDirectives
            || declaration.Parent is not BlockSyntax block || declaration.Declaration.Variables.Count != 1) return false;
        var variable = declaration.Declaration.Variables[0];
        if (variable.Initializer is not null
            || model.GetDeclaredSymbol(variable) is not ILocalSymbol { Type.SpecialType: SpecialType.System_Boolean } local) return false;
        var index = block.Statements.IndexOf(declaration);
        if (index + 1 >= block.Statements.Count || block.Statements[index + 1] is not IfStatementSyntax next
            || next.ContainsDiagnostics || next.ContainsDirectives) return false;
        if (!AssignsEveryBranch(next, local, model)) return false;
        symbol = local;
        decision = next;
        return true;
    }

    private static bool AssignsEveryBranch(StatementSyntax statement, ILocalSymbol symbol, SemanticModel model)
    {
        if (statement is BlockSyntax { Statements.Count: 1 } block) return AssignsEveryBranch(block.Statements[0], symbol, model);
        if (statement is IfStatementSyntax { Else: { } alternative } decision)
        {
            if (Reads(decision.Condition, symbol, model)) return false;
            return AssignsEveryBranch(decision.Statement, symbol, model)
                && AssignsEveryBranch(alternative.Statement, symbol, model);
        }
        return statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && assignment.Left is IdentifierNameSyntax
            && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(assignment.Left).Symbol, symbol)
            && !Reads(assignment.Right, symbol, model);
    }

    private static bool Reads(SyntaxNode node, ILocalSymbol symbol, SemanticModel model) =>
        SymbolReferences.In(node, symbol, model).Any();
}
