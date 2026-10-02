using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;

namespace CodingRules;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NestedReturnDecisionCodeFixProvider))]
[Shared]
public sealed class NestedReturnDecisionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(
            DiagnosticIds.NestedTernaryReturn,
            DiagnosticIds.NestedCoalesceReturn,
            DiagnosticIds.NestedNullableCallReturn,
            DiagnosticIds.NestedBooleanReturn);

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        return ReturnDecisionRewrites.RegisterCodeFixesAsync(
            context,
            title: "Make nested return outcomes explicit",
            equivalenceKey: nameof(NestedReturnDecisionCodeFixProvider),
            requireBlockParent: false,
            allowThrowArms: true);
    }
}
