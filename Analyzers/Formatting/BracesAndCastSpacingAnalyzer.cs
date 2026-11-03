using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BracesAndCastSpacingAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor BracesRule = new(DiagnosticIds.RequiredBraces,
        "Use braces for embedded statements", "Use braces around this statement", "Readability", DiagnosticSeverity.Warning, true);
    public static readonly DiagnosticDescriptor CastRule = new(DiagnosticIds.CastSpacing,
        "Separate casts from their operands", "Insert a space after the cast", "Readability", DiagnosticSeverity.Warning, true);
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(BracesRule, CastRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(AnalyzeCast, SyntaxKind.CastExpression);
        context.RegisterSyntaxNodeAction(AnalyzeStatement, SyntaxKind.IfStatement, SyntaxKind.ElseClause,
            SyntaxKind.ForStatement, SyntaxKind.ForEachStatement, SyntaxKind.ForEachVariableStatement,
            SyntaxKind.WhileStatement, SyntaxKind.DoStatement, SyntaxKind.UsingStatement, SyntaxKind.LockStatement, SyntaxKind.FixedStatement);
    }

    private static void AnalyzeCast(SyntaxNodeAnalysisContext context)
    {
        var cast = (CastExpressionSyntax)context.Node;
        var next = cast.Expression.GetFirstToken();
        if (cast.CloseParenToken.Span.End == next.SpanStart && !cast.ContainsDiagnostics)
            context.ReportDiagnostic(Diagnostic.Create(CastRule, cast.CloseParenToken.GetLocation()));
    }

    private static void AnalyzeStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = Embedded(context.Node);
        if (statement is null or BlockSyntax || context.Node is ElseClauseSyntax && statement is IfStatementSyntax) return;
        context.ReportDiagnostic(Diagnostic.Create(BracesRule, statement.GetFirstToken().GetLocation()));
    }

    internal static StatementSyntax? Embedded(SyntaxNode node) => node switch
    {
        IfStatementSyntax conditional => conditional.Statement,
        ElseClauseSyntax alternative => alternative.Statement,
        ForStatementSyntax loop => loop.Statement,
        CommonForEachStatementSyntax loop => loop.Statement,
        WhileStatementSyntax loop => loop.Statement,
        DoStatementSyntax loop => loop.Statement,
        UsingStatementSyntax scope => scope.Statement,
        LockStatementSyntax scope => scope.Statement,
        FixedStatementSyntax scope => scope.Statement,
        _ => null,
    };
}
