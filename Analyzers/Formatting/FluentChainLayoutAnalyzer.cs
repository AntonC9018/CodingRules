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
        if (IsShortChain(call, text)) return;
        if (!TryGetBoundary(call, out var receiver, out var separator) || call.ContainsDiagnostics) return;
        if (text.Lines.GetLineFromPosition(receiver.Span.End).LineNumber
            != text.Lines.GetLineFromPosition(separator.SpanStart).LineNumber) return;
        context.ReportDiagnostic(Diagnostic.Create(Rule, separator.GetLocation()));
    }

    public static bool TryGetChange(InvocationExpressionSyntax call, SourceText text, out TextChange change)
    {
        change = default;
        if (IsShortChain(call, text)) return false;
        if (call.ContainsDiagnostics || call.ContainsDirectives
            || !TryGetBoundary(call, out var receiver, out var separator)) return false;
        var gap = TextSpan.FromBounds(receiver.Span.End, separator.SpanStart);
        var whitespace = text.ToString(gap);
        if (!whitespace.All(char.IsWhiteSpace) || whitespace.IndexOfAny(['\r', '\n']) >= 0) return false;
        var line = text.Lines.GetLineFromPosition(receiver.SpanStart);
        var indentation = new string(text.ToString(line.Span).TakeWhile(character => character is ' ' or '\t').ToArray());
        var newline = text.Lines.Select(item => text.ToString(TextSpan.FromBounds(item.End, item.EndIncludingLineBreak)))
            .FirstOrDefault(value => value.Length > 0) ?? "\n";
        var continuation = TryGetArgumentChange(call, text, out _) ? "        " : "    ";
        change = new TextChange(gap, newline + indentation + continuation);
        return true;
    }

    public static bool TryGetArgumentChange(InvocationExpressionSyntax call, SourceText text, out TextChange change)
    {
        change = default;
        var argument = call.Ancestors().OfType<ArgumentSyntax>().FirstOrDefault();
        if (argument?.Parent is not ArgumentListSyntax list) return false;
        if (text.Lines.GetLineFromPosition(list.OpenParenToken.Span.End).LineNumber
            != text.Lines.GetLineFromPosition(argument.SpanStart).LineNumber) return false;
        var gap = TextSpan.FromBounds(list.OpenParenToken.Span.End, argument.SpanStart);
        if (!text.ToString(gap).All(char.IsWhiteSpace)) return false;
        var line = text.Lines.GetLineFromPosition(list.Parent!.SpanStart);
        var indentation = new string(text.ToString(line.Span).TakeWhile(c => c is ' ' or '\t').ToArray());
        var newline = text.Lines.Select(item => text.ToString(TextSpan.FromBounds(item.End, item.EndIncludingLineBreak)))
            .FirstOrDefault(value => value.Length > 0) ?? "\n";
        change = new TextChange(gap, newline + indentation + "    ");
        return true;
    }

    private static bool IsShortChain(InvocationExpressionSyntax call, SourceText text)
    {
        // Count calls along the receiver, not calls in argument expressions.
        ExpressionSyntax outer = call;
        while (true)
        {
            if (outer.Parent is MemberAccessExpressionSyntax member && member.Expression == outer) outer = member;
            else if (outer.Parent is InvocationExpressionSyntax next && next.Expression == outer) outer = next;
            else if (outer.Parent is ConditionalAccessExpressionSyntax conditional
                && (conditional.Expression == outer || conditional.WhenNotNull == outer)) outer = conditional;
            else if (outer.Parent is ParenthesizedExpressionSyntax parentheses && parentheses.Expression == outer) outer = parentheses;
            else if (outer.Parent is CastExpressionSyntax cast && cast.Expression == outer) outer = cast;
            else if (outer.Parent is AwaitExpressionSyntax awaitExpression && awaitExpression.Expression == outer) outer = awaitExpression;
            else if (outer.Parent is CheckedExpressionSyntax checkedExpression && checkedExpression.Expression == outer) outer = checkedExpression;
            else if (outer.Parent is ElementAccessExpressionSyntax element && element.Expression == outer) outer = element;
            else if (outer.Parent is PostfixUnaryExpressionSyntax suppression
                && suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) && suppression.Operand == outer) outer = suppression;
            else break;
        }
        var count = CountReceiverCalls(outer);
        if (count > 2) return false;
        var first = text.Lines.GetLineFromPosition(outer.SpanStart);
        var last = text.Lines.GetLineFromPosition(outer.Span.End);
        return first.LineNumber == last.LineNumber && first.Span.Length <= 90;
    }

    private static int CountReceiverCalls(ExpressionSyntax expression) => expression switch
    {
        InvocationExpressionSyntax call => 1 + CountReceiverCalls(call.Expression),
        MemberAccessExpressionSyntax member => CountReceiverCalls(member.Expression),
        ConditionalAccessExpressionSyntax conditional => CountReceiverCalls(conditional.Expression) + CountReceiverCalls(conditional.WhenNotNull),
        ParenthesizedExpressionSyntax parentheses => CountReceiverCalls(parentheses.Expression),
        CastExpressionSyntax cast => CountReceiverCalls(cast.Expression),
        AwaitExpressionSyntax awaitExpression => CountReceiverCalls(awaitExpression.Expression),
        CheckedExpressionSyntax checkedExpression => CountReceiverCalls(checkedExpression.Expression),
        ElementAccessExpressionSyntax element => CountReceiverCalls(element.Expression),
        PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) => CountReceiverCalls(suppression.Operand),
        _ => 0,
    };

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
        CastExpressionSyntax cast => HasCallResult(cast.Expression),
        AwaitExpressionSyntax awaitExpression => HasCallResult(awaitExpression.Expression),
        CheckedExpressionSyntax checkedExpression => HasCallResult(checkedExpression.Expression),
        ElementAccessExpressionSyntax element => HasCallResult(element.Expression),
        PostfixUnaryExpressionSyntax suppression when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression) => HasCallResult(suppression.Operand),
        MemberAccessExpressionSyntax member => HasCallResult(member.Expression),
        ConditionalAccessExpressionSyntax conditional => conditional.WhenNotNull is InvocationExpressionSyntax
            || HasCallResult(conditional.WhenNotNull),
        _ => false,
    };
}
