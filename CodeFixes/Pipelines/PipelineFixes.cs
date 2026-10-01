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
        if (key == PipelineCodeFixProvider.SelectorsKey ? selector is null
            : projection is null || extract && !projection.CanExtract || !extract && projection.Lowering is null) return null;
        var root = selector is not null ? SelectorRewrites.Rewrite(selector) : ProjectionRewrites.Rewrite(projection!, extract);
        root = root.ReplaceNodes(root.GetAnnotatedNodes(ProjectionRewrites.Generated), (_, node) => node.WithAdditionalAnnotations(Formatter.Annotation));
        var changed = document.WithSyntaxRoot(root);
        var options = await changed.GetOptionsAsync(token).ConfigureAwait(false);
        var originalText = await document.GetTextAsync(token).ConfigureAwait(false);
        var original = originalText.ToString();
        var firstLine = original.IndexOf('\n');
        var newline = firstLine > 0 && original[firstLine - 1] == '\r' ? "\r\n" : "\n";
        if (document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(owner.SyntaxTree).TryGetValue("end_of_line", out var configured))
            newline = configured switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" };
        changed = await Formatter.FormatAsync(changed, Formatter.Annotation, options.WithChangedOption(FormattingOptions.NewLine,
            document.Project.Language, newline), token).ConfigureAwait(false);
        var text = await changed.GetTextAsync(token).ConfigureAwait(false);
        if (originalText.ContentEquals(text)) return null;
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)owner.SyntaxTree.Options, owner.SyntaxTree.FilePath, token);
        var parsed = tree.GetRoot(token);
        var after = model.Compilation.ReplaceSyntaxTree(owner.SyntaxTree, tree).GetSemanticModel(tree);
        var beforeMessages = Messages(model, token);
        if (Messages(after, token).Any(pair => !beforeMessages.TryGetValue(pair.Key, out var count) || pair.Value > count)) return null;
        var formatted = (await changed.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var originalCalls = owner.DescendantNodesAndSelf().OfType<ExpressionSyntax>().Where(value => value is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
            .Where(value => value != owner || selector is null).ToDictionary(OperationEvaluator.Key, value => Binding(value, model));
        var seen = new HashSet<string>();
        if (selector is not null)
        {
            // Naming/insertion leaves every original argument subtree in its
            // original source order. Map occurrences by relative ordinal, not a set.
            var selected = parsed.FindNode(formatted.GetAnnotatedNodes(ProjectionRewrites.Selected).Single().Span) as InvocationExpressionSyntax;
            if (selected is null || after.GetOperation(selected) is not IInvocationOperation bound) return null;
            var expected = selector.IdentityPair ?? selector.Operation.TargetMethod;
            if (Method(bound.TargetMethod) != Method(expected) || after.GetTypeInfo(selected).Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                != model.GetTypeInfo(owner).Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) return null;
            var oldValues = selector.Call.ArgumentList.Arguments.SelectMany(argument => argument.DescendantNodesAndSelf().OfType<ExpressionSyntax>()
                .Where(value => value is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)).ToArray();
            var newValues = selected.ArgumentList.Arguments.Take(selector.Call.ArgumentList.Arguments.Count).SelectMany(argument => argument.DescendantNodesAndSelf()
                .OfType<ExpressionSyntax>().Where(value => value is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)).ToArray();
            if (oldValues.Length != newValues.Length || oldValues.Where((value, index) => Binding(value, model) != Binding(newValues[index], after)).Any()) return null;
            for (var index = 0; index < selector.Call.ArgumentList.Arguments.Count; index++)
            {
                var oldArgument = selector.Call.ArgumentList.Arguments[index];
                var newArgument = selected.ArgumentList.Arguments[index];
                if (model.GetOperation(oldArgument) is not IArgumentOperation oldBound || after.GetOperation(newArgument) is not IArgumentOperation newBound
                    || oldBound.Parameter?.Name != newBound.Parameter?.Name
                    || OperationEvaluator.ConversionFingerprint(model.GetConversion(oldArgument.Expression)) != OperationEvaluator.ConversionFingerprint(after.GetConversion(newArgument.Expression))) return null;
            }
            if (SelectorPlan.Create(selected, after, PipelineCatalog.For(after.Compilation)) is not null) return null;
        }
        else
        {
            foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.BindingAnnotation).Concat(formatted.GetAnnotatedNodes(ProjectionRewrites.OriginalCall)))
            {
                var keyData = node.GetAnnotations(OperationEvaluator.BindingAnnotation).Concat(node.GetAnnotations(ProjectionRewrites.OriginalCall)).Single().Data!;
                if (!originalCalls.TryGetValue(keyData, out var binding) || !seen.Add(keyData)
                    || Binding(parsed.FindNode(node.Span, getInnermostNodeForTie: true), after) != binding) return null;
            }
            if (seen.Count != originalCalls.Count) return null;
            foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.ConversionAnnotation))
            {
                if (parsed.FindNode(node.Span, getInnermostNodeForTie: true) is not ExpressionSyntax value
                    || OperationEvaluator.ConversionFingerprint(after.GetConversion(value)) != node.GetAnnotations(OperationEvaluator.ConversionAnnotation).Single().Data) return null;
            }
            foreach (var node in formatted.GetAnnotatedNodes(ProjectionRewrites.Selected))
            {
                var selected = parsed.FindNode(node.Span, getInnermostNodeForTie: true);
                if (selected is LambdaExpressionSyntax remaining && ProjectionPlan.Create(remaining, after, PipelineCatalog.For(after.Compilation), token) is not null) return null;
                var oldStage = owner.Parent?.Parent?.Parent as InvocationExpressionSyntax;
                var newStage = selected.Ancestors().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                if (oldStage is null || newStage is null || Binding(oldStage, model) != Binding(newStage, after)
                    || model.GetTypeInfo(owner).ConvertedType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        != after.GetTypeInfo(selected).ConvertedType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) return null;
            }
            if (projection!.Lowering is not null && formatted.GetAnnotatedNodes(OperationEvaluator.LinearizedAnnotation).Any(node =>
                    parsed.FindNode(node.Span, getInnermostNodeForTie: true) is ExpressionSyntax value && OperationFacts.Create(value, after).HasReason)) return null;
        }
        // Never carry occurrence annotations into the next Fix All iteration.
        // Each plan starts from the same saved-source view as the compiler.
        return document.WithText(text);
    }

    private static Dictionary<string, int> Messages(SemanticModel model, CancellationToken token) => model.GetDiagnostics(cancellationToken: token)
        .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
        .GroupBy(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage()).ToDictionary(group => group.Key, group => group.Count());
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
