using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EarlyReturnLayoutAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ElseAfterExit, "Remove else after a terminating branch",
        "Use early returns instead of this else branch", "Readability",
        DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.IfStatement);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var decision = (IfStatementSyntax)context.Node;
        if (decision.Else is null || decision.Parent is not BlockSyntax || decision.ContainsDirectives) return;
        if (!Terminates(decision.Statement)) return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, decision.Else.ElseKeyword.GetLocation()));
    }

    public static bool Terminates(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax or ThrowStatementSyntax => true,
        BlockSyntax block when block.Statements.Count > 0 => Terminates(block.Statements[block.Statements.Count - 1]),
        IfStatementSyntax { Else: not null } decision => Terminates(decision.Statement) && Terminates(decision.Else.Statement),
        _ => false,
    };
}
