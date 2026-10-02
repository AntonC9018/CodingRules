using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StatementOperationCodeFixProvider)), Shared]
public sealed class StatementOperationCodeFixProvider : CodeFixProvider
{
    public const string ExtractKey = "StatementOperations.ExtractLocalFunction";
    public const string ExpandKey = "StatementOperations.ExpandStatements";
    public override ImmutableArray<string> FixableDiagnosticIds => StatementOperationAnalyzer.Priority.Select(rule => rule.Id).ToImmutableArray();
    public override FixAllProvider GetFixAllProvider() => new StatementOperationFixAllProvider();

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null) return;
        if (root.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax expression) return;
        var plan = OperationPlan.Create(expression, model, context.CancellationToken);
        if (plan is null) return;
        if (plan.CanExtract && await StatementOperationFixes.IsValidAsync(context.Document, plan, true, context.CancellationToken).ConfigureAwait(false))
            context.RegisterCodeFix(CodeAction.Create(plan.MemberInitializer is not null ? "Extract value to private helper" : "Extract value to local function",
                token => StatementOperationFixes.ApplyAsync(context.Document, plan, true, token), ExtractKey), context.Diagnostics);
        if (plan.CanExpand && await StatementOperationFixes.IsValidAsync(context.Document, plan, false, context.CancellationToken).ConfigureAwait(false))
            context.RegisterCodeFix(CodeAction.Create("Expand value into statements",
                token => StatementOperationFixes.ApplyAsync(context.Document, plan, false, token), ExpandKey), context.Diagnostics);
    }
}
