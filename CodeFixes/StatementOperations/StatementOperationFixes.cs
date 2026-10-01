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

internal static class StatementOperationFixes
{
    public static async Task<Document> ApplyAsync(Document document, OperationPlan plan, bool extract, CancellationToken token)
    {
        var root = OperationSiteRewrites.Rewrite(plan, extract);
        root = root.ReplaceNodes(root.GetAnnotatedNodes(OperationSiteRewrites.Generated)
            .Concat(root.GetAnnotatedNodes(ConditionSiteRewrites.Generated)), (_, node) => node.WithAdditionalAnnotations(Formatter.Annotation));
        var changed = document.WithSyntaxRoot(root);
        var options = await changed.GetOptionsAsync(token).ConfigureAwait(false);
        var text = (await document.GetTextAsync(token).ConfigureAwait(false)).ToString();
        var first = text.IndexOf('\n');
        var newline = first > 0 && text[first - 1] == '\r' ? "\r\n" : "\n";
        var config = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GetOptions(plan.Owner.SyntaxTree);
        if (config.TryGetValue("end_of_line", out var configured))
            newline = configured switch { "crlf" => "\r\n", "cr" => "\r", _ => "\n" };
        return await Formatter.FormatAsync(changed, Formatter.Annotation,
            options.WithChangedOption(FormattingOptions.NewLine, document.Project.Language, newline), token).ConfigureAwait(false);
    }

    public static async Task<bool> IsValidAsync(Document document, OperationPlan plan, bool extract, CancellationToken token)
    {
        var changed = await ApplyAsync(document, plan, extract, token).ConfigureAwait(false);
        var text = await changed.GetTextAsync(token).ConfigureAwait(false);
        if ((await document.GetTextAsync(token).ConfigureAwait(false)).ContentEquals(text)) return false;
        var tree = CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)plan.Owner.SyntaxTree.Options, plan.Owner.SyntaxTree.FilePath, token);
        var model = plan.Model.Compilation.ReplaceSyntaxTree(plan.Owner.SyntaxTree, tree).GetSemanticModel(tree);
        var before = CompilerMessages(plan.Model, token);
        if (CompilerMessages(model, token).Any(pair => !before.TryGetValue(pair.Key, out var count) || pair.Value > count)) return false;
        var formatted = (await changed.GetSyntaxRootAsync(token).ConfigureAwait(false))!;
        var parsed = tree.GetRoot(token);
        var selectedRoot = extract ? plan.ExtractionRoot : plan.ExpansionRoot!;
        var originalCalls = OperationFacts.EvaluationNodes(selectedRoot).Where(node => node is InvocationExpressionSyntax or BaseObjectCreationExpressionSyntax)
            .ToDictionary(OperationEvaluator.Key, node => Binding(node, plan.Model));
        var seen = new HashSet<string>();
        foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.BindingAnnotation))
        {
            var original = node.GetAnnotations(OperationEvaluator.BindingAnnotation).Single().Data!;
            if (!originalCalls.TryGetValue(original, out var binding)) continue;
            if (!seen.Add(original) || Binding(parsed.FindNode(node.Span, getInnermostNodeForTie: true), model) != binding) return false;
        }
        if (seen.Count != originalCalls.Count) return false;
        foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.ConversionAnnotation))
        {
            var value = parsed.FindNode(node.Span, getInnermostNodeForTie: true) as ExpressionSyntax;
            if (value is null || OperationEvaluator.ConversionFingerprint(model.GetConversion(value))
                    != node.GetAnnotations(OperationEvaluator.ConversionAnnotation).Single().Data) return false;
        }
        foreach (var node in formatted.GetAnnotatedNodes(OperationEvaluator.LinearizedAnnotation)
                     .Concat(formatted.GetAnnotatedNodes(OperationSiteRewrites.Selected)))
        {
            var value = parsed.FindNode(node.Span, getInnermostNodeForTie: true) as ExpressionSyntax;
            if (value is null || OperationFacts.Create(value, model).HasReason) return false;
        }
        // Annotations locate occurrences only for validation. The saved tree is
        // classified from scratch and has no annotation-based exemptions.
        return Violations(parsed, model) < Violations(plan.Owner.SyntaxTree.GetRoot(token), plan.Model);
    }

    private static int Violations(SyntaxNode root, SemanticModel model) => root.DescendantNodes()
        .Where(StatementOperationAnalyzer.IsCandidate).Select(StatementOperationAnalyzer.Owner)
        .Where(expression => expression is not null).Count(expression => OperationFacts.Create(expression!, model).HasReason);

    private static Dictionary<string, int> CompilerMessages(SemanticModel model, CancellationToken token) =>
        model.GetDiagnostics(cancellationToken: token).Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .GroupBy(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage()).ToDictionary(group => group.Key, group => group.Count());

    private static string Binding(SyntaxNode node, SemanticModel model)
    {
        var operation = model.GetOperation(node);
        var method = operation switch { IInvocationOperation call => call.TargetMethod, IObjectCreationOperation creation => creation.Constructor, _ => null };
        var arguments = operation switch { IInvocationOperation call => call.Arguments, IObjectCreationOperation creation => creation.Arguments, _ => default };
        var instance = operation is IInvocationOperation invocation ? invocation.Instance?.Type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) : null;
        return method?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ":" + instance + ":" + string.Join(";", arguments.Select(argument =>
            argument.Parameter?.Ordinal + ":" + argument.Parameter?.RefKind + ":" + argument.ArgumentKind + ":"
            + argument.Parameter?.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            + (argument.IsImplicit && argument.ArgumentKind == ArgumentKind.DefaultValue ? ":" + argument.Value.ConstantValue.Value : "")));
    }
}



