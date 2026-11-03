using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InitializerCallLayoutCodeFixProvider)), Shared]
public sealed class InitializerCallLayoutCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        DiagnosticIds.InitializerMemberLayout, DiagnosticIds.SoleConstructorArgumentLayout,
        DiagnosticIds.ChoppedArgumentLayout);

    // Revalidate against each preceding edit, rather than merging independently
    // safe changes whose combined formatting has never been checked.
    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(async (context, document, diagnostics) =>
    {
        foreach (var diagnostic in diagnostics.OrderByDescending(item => item.Location.SourceSpan.Start))
        {
            var root = await document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root?.FindToken(diagnostic.Location.SourceSpan.Start).Parent;
            if (node is InitializerExpressionSyntax or ArgumentListSyntax)
                document = await ApplyAsync(document, node, context.CancellationToken).ConfigureAwait(false);
        }
        return document;
    });

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var node = root?.FindToken(context.Span.Start).Parent;
        if (node is not (InitializerExpressionSyntax or ArgumentListSyntax)) return;
        var changed = await ApplyAsync(context.Document, node, context.CancellationToken).ConfigureAwait(false);
        if (ReferenceEquals(changed, context.Document)) return;
        context.RegisterCodeFix(CodeAction.Create("Apply initializer call layout",
            _ => Task.FromResult(changed), nameof(InitializerCallLayoutCodeFixProvider)), context.Diagnostics);
    }

    private static async Task<Document> ApplyAsync(Document document, SyntaxNode node, CancellationToken token)
    {
        var text = await document.GetTextAsync(token).ConfigureAwait(false);
        var changes = InitializerCallLayoutAnalyzer.GetChanges(node, text);
        if (changes.IsEmpty) return document;
        var changed = document.WithText(text.WithChanges(changes));
        var length = node.Span.Length + changes.Sum(change => (change.NewText?.Length ?? 0) - change.Span.Length);
        changed = await Formatter.FormatAsync(changed, new TextSpan(node.SpanStart, length), cancellationToken: token)
            .ConfigureAwait(false);
        // Start from source text so validation observes compiler values for the
        // final saved layout, including indentation introduced by the formatter.
        changed = document.WithText(await changed.GetTextAsync(token).ConfigureAwait(false));
        var before = await CallerValuesAsync(document, token).ConfigureAwait(false);
        var after = await CallerValuesAsync(changed, token).ConfigureAwait(false);
        if (before is null || after is null || !before.SequenceEqual(after)) return document;
        return changed;
    }

    private static async Task<List<(int Parameter, object? Value)>?> CallerValuesAsync(Document document, CancellationToken token)
    {
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (model is null || root is null) return null;
        var collector = new CallerValueCollector();
        // Scan operation roots across the whole file: observers can enclose the
        // edit or occur in later members. Walking also includes implicit calls
        // (for example collection Add), indexers and attribute constructors.
        foreach (var node in root.DescendantNodesAndSelf())
        {
            token.ThrowIfCancellationRequested();
            if (model.GetOperation(node, token) is { Parent: null } operation) collector.Visit(operation);
        }
        return collector.UnknownValue ? null : collector.Values;
    }

    private sealed class CallerValueCollector : OperationWalker
    {
        public List<(int Parameter, object? Value)> Values { get; } = new();
        public bool UnknownValue { get; private set; }

        public override void VisitArgument(IArgumentOperation operation)
        {
            if (operation.IsImplicit && operation.Parameter is { } parameter
                && parameter.GetAttributes().Any(attribute =>
                    attribute.AttributeClass?.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices"
                    && attribute.AttributeClass.Name is "CallerArgumentExpressionAttribute" or "CallerLineNumberAttribute"
                        or "CallerMemberNameAttribute" or "CallerFilePathAttribute"))
            {
                if (!operation.Value.ConstantValue.HasValue) UnknownValue = true;
                Values.Add((parameter.Ordinal, operation.Value.ConstantValue.HasValue ? operation.Value.ConstantValue.Value : null));
            }
            base.VisitArgument(operation);
        }
    }
}
