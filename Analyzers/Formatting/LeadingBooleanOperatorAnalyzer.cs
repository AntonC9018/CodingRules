using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LeadingBooleanOperatorAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.LeadingBooleanOperator,
        "Place wrapped Boolean operators at the start of the continuation line",
        "Place '{0}' at the start of the continuation line",
        "Formatting",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.LogicalAndExpression, SyntaxKind.LogicalOrExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;
        var text = binary.SyntaxTree.GetText(context.CancellationToken);
        if (!TryGetChange(binary, text, out _)) return;
        var diagnostic = Diagnostic.Create(Rule, binary.OperatorToken.GetLocation(), binary.OperatorToken.Text);
        context.ReportDiagnostic(diagnostic);
    }

    public static bool TryGetChange(BinaryExpressionSyntax binary, SourceText text, out TextChange change)
    {
        change = default;
        if (!binary.IsKind(SyntaxKind.LogicalAndExpression) && !binary.IsKind(SyntaxKind.LogicalOrExpression)) return false;
        if (binary.ContainsDiagnostics || binary.Left.IsMissing || binary.Right.IsMissing) return false;
        var before = TextSpan.FromBounds(binary.Left.Span.End, binary.OperatorToken.SpanStart);
        var after = TextSpan.FromBounds(binary.OperatorToken.Span.End, binary.Right.SpanStart);
        var beforeText = text.ToString(before);
        var afterText = text.ToString(after);
        // Move only whitespace. Comments and directives keep their exact attachment.
        if (!beforeText.All(char.IsWhiteSpace) || !afterText.All(char.IsWhiteSpace)) return false;
        var lineBreak = afterText.IndexOfAny(['\r', '\n']);
        if (beforeText.IndexOfAny(['\r', '\n']) >= 0 || lineBreak < 0) return false;
        var span = TextSpan.FromBounds(before.Start, after.End);
        var continuation = afterText.AsSpan(lineBreak).ToString();
        change = new TextChange(span, continuation + binary.OperatorToken.Text + " ");
        return true;
    }
}
