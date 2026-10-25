using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FluentChainLayoutAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.FluentChainLayout,
        "Start subsequent chained calls on new lines",
        "Start this subsequent chained call on a new line",
        "Formatting", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var call = (InvocationExpressionSyntax)context.Node;
        var text = call.SyntaxTree.GetText(context.CancellationToken);
        if (!TryGetBoundary(call, out var receiver, out var separator) || call.ContainsDiagnostics) return;
        if (text.Lines.GetLineFromPosition(receiver.Span.End).LineNumber
            != text.Lines.GetLineFromPosition(separator.SpanStart).LineNumber) return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, separator.GetLocation()));
    }

    public static bool TryGetChange(InvocationExpressionSyntax call, SourceText text, out TextChange change)
    {
        change = default;
        if (call.ContainsDiagnostics || call.ContainsDirectives
            || !TryGetBoundary(call, out var receiver, out var separator)) return false;
        var gap = TextSpan.FromBounds(receiver.Span.End, separator.SpanStart);
        var whitespace = text.ToString(gap);
        if (!whitespace.All(char.IsWhiteSpace) || whitespace.IndexOfAny(['\r', '\n']) >= 0) return false;
        var line = text.Lines.GetLineFromPosition(receiver.SpanStart);
        var indentation = new string(text.ToString(line.Span).TakeWhile(character => character is ' ' or '\t').ToArray());
        var newline = text.Lines.Select(item => text.ToString(TextSpan.FromBounds(item.End, item.EndIncludingLineBreak)))
            .FirstOrDefault(value => value.Length > 0) ?? "\n";
        change = new TextChange(gap, newline + indentation + "    ");
        return true;
    }

    public static bool TryGetBoundary(InvocationExpressionSyntax call, out ExpressionSyntax receiver, out SyntaxToken separator)
    {
        if (call.Expression is MemberAccessExpressionSyntax member && HasCallResult(member.Expression))
        {
            receiver = member.Expression;
            separator = member.OperatorToken;
            return true;
        }
        if (call.Expression is MemberBindingExpressionSyntax)
        {
            var conditional = call.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault();
            if (conditional is not null && HasCallResult(conditional.Expression))
            {
                receiver = conditional.Expression;
                separator = conditional.OperatorToken;
                return true;
            }
        }
        receiver = null!;
        separator = default;
        return false;
    }

    private static bool HasCallResult(ExpressionSyntax receiver) => receiver switch
    {
        InvocationExpressionSyntax => true,
        ParenthesizedExpressionSyntax parentheses => HasCallResult(parentheses.Expression),
        PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) => HasCallResult(suppression.Operand),
        MemberAccessExpressionSyntax member => HasCallResult(member.Expression),
        ConditionalAccessExpressionSyntax conditional => conditional.WhenNotNull is InvocationExpressionSyntax
            || HasCallResult(conditional.WhenNotNull),
        _ => false,
    };
}
