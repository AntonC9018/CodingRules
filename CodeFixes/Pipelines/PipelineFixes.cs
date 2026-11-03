using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace CodingRules;

internal static class PipelineFixes
{
    public static async Task<Document?> ChangeAsync(Document document, SyntaxNode owner, SemanticModel model, string key, CancellationToken token)
    {
        var catalog = PipelineCatalog.For(model.Compilation);
        var projection = owner is LambdaExpressionSyntax lambda ? ProjectionPlan.Create(lambda, model, catalog, token) : null;
        var selector = owner is InvocationExpressionSyntax call ? SelectorPlan.Create(call, model, catalog) : null;
        var extract = key == PipelineCodeFixProvider.ExtractKey;
        var applies = key == PipelineCodeFixProvider.SelectorsKey
            ? selector is not null
            : projection is not null && (extract ? projection.CanExtract : projection.Lowering is not null);
        if (!applies)
        {
            return null;
        }

        var root = selector is not null ? SelectorRewrites.Rewrite(selector) : ProjectionRewrites.Rewrite(projection!, extract);
        root = root.ReplaceNodes(root.GetAnnotatedNodes(ProjectionRewrites.Generated), (_, node) => node.WithAdditionalAnnotations(Formatter.Annotation));
        var changed = await GeneratedSyntaxFormatter.FormatAsync(document.WithSyntaxRoot(root), document, token).ConfigureAwait(false);
        var originalText = await document.GetTextAsync(token).ConfigureAwait(false);
        var text = await changed.GetTextAsync(token).ConfigureAwait(false);
        if (originalText.ContentEquals(text))
        {
            return null;
        }

        var after = SavedSource.Compile(model, text, token);
        if (SavedSource.AddsCompilerMessages(model, after, token))
        {
            return null;
        }

        var saved = new SavedRewrite(model, after, (await changed.GetSyntaxRootAsync(token).ConfigureAwait(false))!, after.SyntaxTree.GetRoot(token));
        var preservesBehavior = selector is not null
            ? SelectorPreservesBehavior(selector, owner, saved)
            : ProjectionPreservesBehavior(projection!, owner, saved, token);

        // Never carry occurrence annotations into the next Fix All iteration.
        // Each plan starts from the same saved-source view as the compiler.
        return preservesBehavior ? document.WithText(text) : null;
    }

    /// <summary>The original model, the saved model, the annotated formatted root and the reparsed saved root.</summary>
    private sealed record SavedRewrite(SemanticModel Model, SemanticModel After, SyntaxNode Formatted, SyntaxNode Parsed)
    {
        public SyntaxNode Find(SyntaxNode formattedNode) => Parsed.FindNode(formattedNode.Span, getInnermostNodeForTie: true);
    }

    private static bool SelectorPreservesBehavior(SelectorPlan selector, SyntaxNode owner, SavedRewrite saved)
    {
        var (model, after) = (saved.Model, saved.After);
        if (saved.Find(saved.Formatted.GetAnnotatedNodes(ProjectionRewrites.Selected).Single()) is not InvocationExpressionSyntax selected
            || after.GetOperation(selected) is not IInvocationOperation bound)
        {
            return false;
        }

        var expected = selector.IdentityPair ?? selector.Operation.TargetMethod;
        if (Method(bound.TargetMethod) != Method(expected) || TypeName(after.GetTypeInfo(selected).Type) != TypeName(model.GetTypeInfo(owner).Type))
        {
            return false;
        }

        // Naming/insertion leaves every original argument subtree in its
        // original source order. Map occurrences by relative ordinal, not a set.
        var oldArguments = selector.Call.ArgumentList.Arguments;
        var newArguments = selected.ArgumentList.Arguments.Take(oldArguments.Count).ToArray();
        var oldCalls = oldArguments.SelectMany(Calls).ToArray();
        var newCalls = newArguments.SelectMany(Calls).ToArray();
        if (oldCalls.Length != newCalls.Length
            || oldCalls.Where((value, index) => Binding(value, model) != Binding(newCalls[index], after)).Any())
        {
            return false;
        }

        for (var index = 0; index < oldArguments.Count; index++)
        {
            if (!ArgumentKeepsBinding(oldArguments[index], model, newArguments[index], after))
            {
                return false;
            }
        }

        return SelectorPlan.Create(selected, after, PipelineCatalog.For(after.Compilation)) is null;
    }

    private static bool ArgumentKeepsBinding(ArgumentSyntax oldArgument, SemanticModel model, ArgumentSyntax newArgument, SemanticModel after) =>
        model.GetOperation(oldArgument) is IArgumentOperation oldBound
        && after.GetOperation(newArgument) is IArgumentOperation newBound
        && oldBound.Parameter?.Name == newBound.Parameter?.Name
        && OperationEvaluator.ConversionFingerprint(model.GetConversion(oldArgument.Expression))
            == OperationEvaluator.ConversionFingerprint(after.GetConversion(newArgument.Expression));

    private static bool ProjectionPreservesBehavior(ProjectionPlan projection, SyntaxNode owner, SavedRewrite saved, CancellationToken token)
    {
        var (model, after, formatted) = (saved.Model, saved.After, saved.Formatted);
        var originalCalls = Calls(owner).ToDictionary(OperationEvaluator.Key, value => Binding(value, model));
        var seen = new HashSet<string>();
        foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.BindingAnnotation).Concat(formatted.GetAnnotatedNodes(ProjectionRewrites.OriginalCall)))
        {
            var key = node.GetAnnotations(OperationEvaluator.BindingAnnotation).Concat(node.GetAnnotations(ProjectionRewrites.OriginalCall)).Single().Data!;
            if (!originalCalls.TryGetValue(key, out var binding) || !seen.Add(key) || Binding(saved.Find(node), after) != binding)
            {
                return false;
            }
        }

        if (seen.Count != originalCalls.Count)
        {
            return false;
        }

        foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.ConversionAnnotation))
        {
            if (saved.Find(node) is not ExpressionSyntax value
                || OperationEvaluator.ConversionFingerprint(after.GetConversion(value)) != node.GetAnnotations(OperationEvaluator.ConversionAnnotation).Single().Data)
            {
                return false;
            }
        }

        foreach (var node in formatted.GetAnnotatedNodes(ProjectionRewrites.Selected))
        {
            var selected = saved.Find(node);
            if (selected is LambdaExpressionSyntax remaining && ProjectionPlan.Create(remaining, after, PipelineCatalog.For(after.Compilation), token) is not null)
            {
                return false;
            }

            var oldStage = owner.Parent?.Parent?.Parent as InvocationExpressionSyntax;
            var newStage = selected.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
            if (oldStage is null
                || newStage is null
                || Binding(oldStage, model) != Binding(newStage, after)
                || TypeName(model.GetTypeInfo(owner).ConvertedType) != TypeName(after.GetTypeInfo(selected).ConvertedType))
            {
                return false;
            }
        }

        // A lowered projection must not leave an operation that still needs naming.
        return projection.Lowering is null || !formatted.GetAnnotatedNodes(OperationEvaluator.LinearizedAnnotation)
            .Any(node => saved.Find(node) is ExpressionSyntax value && OperationFacts.Create(value, after).HasReason);
    }

    private static IEnumerable<ExpressionSyntax> Calls(SyntaxNode node) => node.DescendantNodesAndSelf()
        .OfType<ExpressionSyntax>()
        .Where(value => value is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax);

    private static string? TypeName(ITypeSymbol? type) => type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Method(IMethodSymbol method) => (method.ReducedFrom ?? method).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    private static string Binding(SyntaxNode node, SemanticModel model)
    {
        var operation = model.GetOperation(node);
        var method = operation switch { IInvocationOperation call => call.TargetMethod, IObjectCreationOperation creation => creation.Constructor, _ => null };
        var arguments = operation switch { IInvocationOperation call => call.Arguments, IObjectCreationOperation creation => creation.Arguments, _ => default };
        return (method is null ? "" : Method(method)) + ":" + string.Join(";", arguments.Select(argument => argument.Parameter?.Name
            + ":" + argument.Parameter?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + argument.ArgumentKind
            + ":" + argument.Parameter?.RefKind + (argument.IsImplicit && argument.ArgumentKind == ArgumentKind.DefaultValue ? ":" + argument.Value.ConstantValue.Value : "")));
    }
}
