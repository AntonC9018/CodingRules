using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InitializerCallLayoutAnalyzer : DiagnosticAnalyzer
{
    public static readonly DiagnosticDescriptor InitializerRule = new(
        DiagnosticIds.InitializerMemberLayout, "Place initializer members on separate lines",
        "Place initializer members on separate lines", "Formatting", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ArgumentRule = new(
        DiagnosticIds.SoleConstructorArgumentLayout, "Keep a sole constructor argument adjacent to the call parenthesis",
        "Keep the sole constructor argument adjacent to the opening call parenthesis", "Formatting", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ChoppedArgumentRule = new(
        DiagnosticIds.ChoppedArgumentLayout, "Put the first argument on a new line",
        "Put the first argument of a chopped argument list on a new line", "Formatting",
        DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(InitializerRule, ArgumentRule, ChoppedArgumentRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ObjectInitializerExpression, SyntaxKind.WithInitializerExpression, SyntaxKind.ArgumentList);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var text = context.Node.SyntaxTree.GetText(context.CancellationToken);
        var changes = GetChanges(context.Node, text, out var rule);
        if (changes.IsEmpty) return;
        context.ReportDiagnostic(Diagnostic.Create(rule, context.Node.GetFirstToken().GetLocation()));
    }

    public static ImmutableArray<TextChange> GetChanges(SyntaxNode node, SourceText text) => GetChanges(node, text, out _);

    public static ImmutableArray<TextChange> GetChanges(SyntaxNode node, SourceText text, out DiagnosticDescriptor rule)
    {
        rule = InitializerRule;
        if (node.ContainsDiagnostics || node.ContainsDirectives) return ImmutableArray<TextChange>.Empty;
        if (node is ArgumentListSyntax arguments)
        {
            var changes = ArgumentChanges(arguments, text, out var isSoleConstruction);
            rule = isSoleConstruction ? ArgumentRule : ChoppedArgumentRule;
            return changes;
        }
        if (node is not InitializerExpressionSyntax initializer)
            return ImmutableArray<TextChange>.Empty;
        rule = InitializerRule;
        var recordUpdate = initializer.IsKind(SyntaxKind.WithInitializerExpression);
        if ((!recordUpdate && !initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
            || initializer.Expressions.Count < 1) return ImmutableArray<TextChange>.Empty;

        var changes = ImmutableArray.CreateBuilder<TextChange>();
        var newline = Newline(text);
        var previous = initializer.OpenBraceToken;
        if (initializer.Parent is BaseObjectCreationExpressionSyntax creation)
        {
            var previousToken = creation.ArgumentList?.CloseParenToken ?? creation.GetFirstToken();
            if (creation is ObjectCreationExpressionSyntax explicitCreation && explicitCreation.ArgumentList is null)
                previousToken = explicitCreation.Type.GetLastToken();
            AddLineBreak(previousToken, initializer.OpenBraceToken, text, newline, changes);
            // A block comment immediately before new otherwise becomes the
            // formatter's alignment anchor, pushing the initializer far right.
            var first = creation.GetFirstToken();
            if (first.LeadingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                && text.Lines.GetLineFromPosition(trivia.Span.End).LineNumber
                    == text.Lines.GetLineFromPosition(first.SpanStart).LineNumber))
            {
                var line = text.Lines.GetLineFromPosition(first.SpanStart);
                var indentation = new string(text.ToString(line.Span).TakeWhile(c => c is ' ' or '\t').ToArray());
                changes.Add(new TextChange(new TextSpan(first.SpanStart, 0), newline + indentation));
            }
        }
        if (recordUpdate && initializer.Parent is WithExpressionSyntax update)
        {
            AddLineBreak(update.WithKeyword, initializer.OpenBraceToken, text, newline, changes);
        }
        // Only separators and brace/member boundaries change; values and comments retain their text.
        for (var index = 0; index < initializer.Expressions.Count; index++)
        {
            var member = initializer.Expressions[index];
            AddLineBreak(previous, member.GetFirstToken(), text, newline, changes);
            previous = member.GetLastToken();
            if (index < initializer.Expressions.SeparatorCount) previous = initializer.Expressions.GetSeparator(index);
        }
        var closingChange = changes.Count;
        AddLineBreak(previous, initializer.CloseBraceToken, text, newline, changes);
        if (recordUpdate && initializer.Expressions.SeparatorCount < initializer.Expressions.Count)
        {
            if (changes.Count > closingChange && changes[closingChange].Span.Start == previous.Span.End)
            {
                var gap = changes[closingChange];
                changes[closingChange] = new TextChange(gap.Span, "," + gap.NewText);
            }
            else
            {
                changes.Add(new TextChange(new TextSpan(previous.Span.End, 0), ","));
            }
        }
        return changes.ToImmutable();
    }

    private static void AddLineBreak(SyntaxToken previous, SyntaxToken next, SourceText text, string newline,
        ImmutableArray<TextChange>.Builder changes)
    {
        if (text.Lines.GetLineFromPosition(previous.Span.End).LineNumber
            != text.Lines.GetLineFromPosition(next.SpanStart).LineNumber) return;
        var gap = TextSpan.FromBounds(previous.Span.End, next.SpanStart);
        var line = text.Lines.GetLineFromPosition(next.SpanStart);
        var indentation = new string(text.ToString(line.Span).TakeWhile(character => character is ' ' or '\t').ToArray());
        var replacement = newline + indentation;
        if (text.ToString(gap).All(char.IsWhiteSpace)) changes.Add(new TextChange(gap, replacement));
        else changes.Add(new TextChange(new TextSpan(next.SpanStart, 0), replacement));
    }

    private static ImmutableArray<TextChange> ArgumentChanges(ArgumentListSyntax arguments, SourceText text,
        out bool isSoleConstruction)
    {
        isSoleConstruction = false;
        // Only ordinary invocation and object-creation lists are in scope.
        if (arguments.Parent is InvocationExpressionSyntax && arguments.Arguments.Count == 1)
        {
            var argument = arguments.Arguments[0];
            if ((argument.Expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
                && argument.NameColon is null && argument.RefKindKeyword.IsKind(SyntaxKind.None))
            {
                var gap = TextSpan.FromBounds(arguments.OpenParenToken.Span.End, argument.SpanStart);
                var whitespace = text.ToString(gap);
                if (whitespace.All(char.IsWhiteSpace) && whitespace.IndexOfAny(['\r', '\n']) >= 0)
                {
                    isSoleConstruction = true;
                    return ImmutableArray.Create(new TextChange(gap, ""));
                }
            }
        }

        if (arguments.Parent is not (InvocationExpressionSyntax or ObjectCreationExpressionSyntax
            or ImplicitObjectCreationExpressionSyntax) || arguments.Arguments.Count < 2)
            return ImmutableArray<TextChange>.Empty;

        var first = arguments.Arguments[0];
        // A multiline lambda is an intentional exception to argument-list wrapping.
        if (first.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            return ImmutableArray<TextChange>.Empty;
        if (text.Lines.GetLineFromPosition(arguments.OpenParenToken.Span.End).LineNumber
            != text.Lines.GetLineFromPosition(first.SpanStart).LineNumber)
            return ImmutableArray<TextChange>.Empty;

        var chopped = arguments.Arguments.Skip(1).Any(argument =>
            text.Lines.GetLineFromPosition(argument.SpanStart).LineNumber
                > text.Lines.GetLineFromPosition(first.SpanStart).LineNumber);
        if (!chopped) return ImmutableArray<TextChange>.Empty;

        var gapToFirst = TextSpan.FromBounds(arguments.OpenParenToken.Span.End, first.SpanStart);
        if (!text.ToString(gapToFirst).All(char.IsWhiteSpace)) return ImmutableArray<TextChange>.Empty;
        var newline = Newline(text);
        var changes = ImmutableArray.CreateBuilder<TextChange>();
        AddLineBreak(arguments.OpenParenToken, first.GetFirstToken(), text, newline, changes);
        return changes.ToImmutable();
    }

    private static string Newline(SourceText text) => text.Lines
        .Select(line => text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak)))
        .FirstOrDefault(value => value.Length > 0) ?? "\n";
}
