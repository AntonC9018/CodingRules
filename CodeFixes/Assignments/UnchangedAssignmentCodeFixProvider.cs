using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UnchangedAssignmentCodeFixProvider)), Shared]
public sealed class UnchangedAssignmentCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(DiagnosticIds.UnchangedAssignment);

    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(async (context, document, diagnostics) =>
    {
        var token = context.CancellationToken;
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        if (root is null) return document;
        var nodes = diagnostics.Select(diagnostic => root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true))
            .OfType<AssignmentExpressionSyntax>().ToArray();
        document = document.WithSyntaxRoot(root.TrackNodes(nodes));
        foreach (var original in nodes)
        {
            root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
            var current = root!.GetCurrentNode(original);
            if (current is not null)
                document = await ChangeAsync(document, current.SpanStart, token).ConfigureAwait(false) ?? document;
        }
        return document;
    });

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var changed = await ChangeAsync(context.Document, context.Span.Start, context.CancellationToken).ConfigureAwait(false);
        if (changed is null) return;
        context.RegisterCodeFix(CodeAction.Create("Use an explicit conditional update", _ => Task.FromResult(changed),
            DiagnosticIds.UnchangedAssignment), context.Diagnostics);
    }

    private static async Task<Document?> ChangeAsync(Document document, int start, CancellationToken token)
    {
        var root = await document.GetSyntaxRootAsync(token).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(token).ConfigureAwait(false);
        var assignment = root?.FindToken(start).Parent?.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
        if (assignment is null || model is null
            || UnchangedAssignmentPlan.Create(assignment, model, token) is not { } plan) return null;
        var changed = document.WithSyntaxRoot(root!.ReplaceNode(plan.Owner, plan.Replacement.WithAdditionalAnnotations(Formatter.Annotation)));
        changed = await Formatter.FormatAsync(changed, Formatter.Annotation, cancellationToken: token).ConfigureAwait(false);
        // Validate the saved text, rather than relying on construction annotations.
        var text = await changed.GetTextAsync(token).ConfigureAwait(false);
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)model.SyntaxTree.Options, model.SyntaxTree.FilePath, token);
        var after = model.Compilation.ReplaceSyntaxTree(model.SyntaxTree, tree).GetSemanticModel(tree);
        var beforeDiagnostics = RelevantDiagnostics(model, token).GroupBy(DiagnosticKey)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var group in RelevantDiagnostics(after, token).GroupBy(DiagnosticKey))
        {
            if (!beforeDiagnostics.TryGetValue(group.Key, out var count) || group.Count() > count) return null;
        }
        // The only removed expressions are proven storage reads/null. Every call,
        // creation and argument conversion must retain its resolved binding.
        if (!Bindings(root!, model, token).SequenceEqual(Bindings(tree.GetRoot(token), after, token))) return null;
        return changed;
    }

    // Locations can move when an arrow becomes a block. Retain multiplicity so
    // an additional occurrence of an existing error/warning still rejects a fix.
    private static System.Collections.Generic.IEnumerable<Diagnostic> RelevantDiagnostics(SemanticModel model, CancellationToken token) =>
        model.GetDiagnostics(cancellationToken: token).Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            || diagnostic.Id.StartsWith("CS86", System.StringComparison.Ordinal));

    private static string DiagnosticKey(Diagnostic diagnostic) => diagnostic.Id + ":" + diagnostic.Severity + ":" + diagnostic.GetMessage();

    private static string[] Bindings(SyntaxNode root, SemanticModel model, CancellationToken token) => root.DescendantNodes()
        .Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax or ElementAccessExpressionSyntax)
        .Select(node => NamedArgumentIdentity.Operation(model.GetOperation(node, token))).ToArray();
}
