using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InlineConditionCodeFixProvider)), Shared]
public sealed class InlineConditionCodeFixProvider : CodeFixProvider
{
    public const string ExtractKey = "InlineConditions.ExtractLocalFunction";
    public const string ExpandKey = "InlineConditions.ExpandStatements";

    public override ImmutableArray<string> FixableDiagnosticIds => InlineConditionAnalyzer.Priority.Select(rule => rule.Id).ToImmutableArray();

    public override FixAllProvider GetFixAllProvider() => new InlineConditionFixAllProvider();

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return;
        }

        var expression = root.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true) as ExpressionSyntax;
        if (expression is null)
        {
            return;
        }

        var plan = ConditionRewritePlan.Create(expression, model, context.CancellationToken);
        if (plan is null)
        {
            return;
        }

        if (plan.CanExtract)
        {
            var title = expression.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() is FieldDeclarationSyntax or PropertyDeclarationSyntax
                && plan.Statement is null ? "Extract condition to private helper" : "Extract condition to local function";
            context.RegisterCodeFix(CodeAction.Create(title,
                token => InlineConditionFixes.ApplyAsync(context.Document, plan, extract: true, token), ExtractKey), context.Diagnostics);
        }

        if (plan.CanExpand)
        {
            context.RegisterCodeFix(CodeAction.Create("Expand condition into statements",
                token => InlineConditionFixes.ApplyAsync(context.Document, plan, extract: false, token), ExpandKey), context.Diagnostics);
        }
    }
}
