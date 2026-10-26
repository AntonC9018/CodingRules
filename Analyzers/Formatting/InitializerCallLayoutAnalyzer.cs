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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(InitializerRule, ArgumentRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.ObjectInitializerExpression, SyntaxKind.WithInitializerExpression, SyntaxKind.ArgumentList);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var text = context.Node.SyntaxTree.GetText(context.CancellationToken);
        if (GetChanges(context.Node, text).IsEmpty) return;
        var rule = context.Node is InitializerExpressionSyntax ? InitializerRule : ArgumentRule;
        context.ReportDiagnostic(Diagnostic.Create(rule, context.Node.GetFirstToken().GetLocation()));
    }

    public static ImmutableArray<TextChange> GetChanges(SyntaxNode node, SourceText text)
    {
        if (node.ContainsDiagnostics || node.ContainsDirectives) return ImmutableArray<TextChange>.Empty;
        if (node is ArgumentListSyntax arguments) return ArgumentChanges(arguments, text);
        if (node is not InitializerExpressionSyntax initializer)
            return ImmutableArray<TextChange>.Empty;
        var recordUpdate = initializer.IsKind(SyntaxKind.WithInitializerExpression);
        if ((!recordUpdate && !initializer.IsKind(SyntaxKind.ObjectInitializerExpression))
            || initializer.Expressions.Count < 1) return ImmutableArray<TextChange>.Empty;

        var changes = ImmutableArray.CreateBuilder<TextChange>();
        var newline = text.Lines.Select(line => text.ToString(TextSpan.FromBounds(line.End, line.EndIncludingLineBreak)))
            .FirstOrDefault(value => value.Length > 0) ?? "\n";
        var previous = initializer.OpenBraceToken;
        if (initializer.Parent is BaseObjectCreationExpressionSyntax creation)
        {
            var previousToken = creation.ArgumentList?.CloseParenToken ?? creation.GetFirstToken();
            if (creation is ObjectCreationExpressionSyntax explicitCreation && explicitCreation.ArgumentList is null)
                previousToken = explicitCreation.Type.GetLastToken();
            AddLineBreak(previousToken, initializer.OpenBraceToken, text, newline, changes);
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

    private static ImmutableArray<TextChange> ArgumentChanges(ArgumentListSyntax arguments, SourceText text)
    {
        // Invocation arguments only. Constructor/attribute/indexer lists and multi-argument calls are separate policy.
        if (arguments.Parent is not InvocationExpressionSyntax || arguments.Arguments.Count != 1)
            return ImmutableArray<TextChange>.Empty;
        var argument = arguments.Arguments[0];
        if (argument.Expression is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
            || argument.NameColon is not null || !argument.RefKindKeyword.IsKind(SyntaxKind.None))
            return ImmutableArray<TextChange>.Empty;
        var gap = TextSpan.FromBounds(arguments.OpenParenToken.Span.End, argument.SpanStart);
        var whitespace = text.ToString(gap);
        if (!whitespace.All(char.IsWhiteSpace) || whitespace.IndexOfAny(['\r', '\n']) < 0)
            return ImmutableArray<TextChange>.Empty;
        return ImmutableArray.Create(new TextChange(gap, ""));
    }
}
