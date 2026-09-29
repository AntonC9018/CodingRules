using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ExplicitReturnDecisionCodeFixProvider))]
[Shared]
public sealed class ExplicitReturnDecisionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.ExplicitReturnDecision);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        return ReturnDecisionRewrites.RegisterCodeFixesAsync(
            context,
            title: "Make return outcomes explicit",
            equivalenceKey: nameof(ExplicitReturnDecisionCodeFixProvider),
            requireBlockParent: true,
            allowThrowArms: false);
    }
}
