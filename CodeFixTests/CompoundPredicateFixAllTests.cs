using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Xunit;

namespace CodingRules;

public sealed class CompoundPredicateFixAllTests
{
    [Theory]
    [InlineData(FixAllScope.Document)]
    [InlineData(FixAllScope.Project)]
    [InlineData(FixAllScope.Solution)]
    public async Task SequentialEditsIncludeNewDepthFindingsAndAreStable(FixAllScope scope)
    {
        const string source = """
            using System.Linq;
            class C {
                static bool M(int[] ids) => ids.Any(a => ids.Any(b => ids.Any(c => ids.Any(d => d == a))));
                static bool N(int[] ids) => ids.Any(id => id > 0 && ids.Any(p => p == id));
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source);
        var changed = await FixAll(document, scope);
        await StatementOperationTestFixture.Compiles(changed);
        Assert.Empty(await CompoundPredicateTestFixture.Diagnostics(changed));
        var again = await FixAll(changed, scope);
        Assert.Equal((await changed.GetTextAsync()).ToString(), (await again.GetTextAsync()).ToString());
    }

    [Fact]
    public async Task UnsafeEarlierFindingDoesNotBlockSafeLaterFinding()
    {
        const string source = """
            using System.Linq;
            class C {
                static bool First(int[] ids) => ids.Any(id => id > 0 && ids.Any(p => p == id));
                static int Line([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
                static int Observed() => Line();
                static bool Last(int[] ids) => ids.Any(id => id > 0 && ids.Any(p => p == id));
            }
            """;
        var document = CompoundPredicateTestFixture.Document(source);
        var changed = await FixAll(document, FixAllScope.Document);
        await StatementOperationTestFixture.Compiles(changed);
        var remaining = Assert.Single(await CompoundPredicateTestFixture.Diagnostics(changed));
        Assert.Empty(await CompoundPredicateTestFixture.Actions(changed, remaining));
        Assert.Contains("static bool First(int[] ids) => ids.Any(id => id > 0", (await changed.GetTextAsync()).ToString());
        Assert.Contains("return true;", (await changed.GetTextAsync()).ToString());
    }

    private static async Task<Document> FixAll(Document document, FixAllScope scope)
    {
        var provider = new CompoundPredicateCodeFixProvider();
        var context = new FixAllContext(document, provider, scope, CompoundPredicateCodeFixProvider.BlockKey,
            provider.FixableDiagnosticIds, new Diagnostics(), CancellationToken.None);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context))!;
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None))
            .OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    private sealed class Diagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document,
            CancellationToken cancellationToken) => await CompoundPredicateTestFixture.Diagnostics(document);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project,
            CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project,
            CancellationToken cancellationToken)
        {
            var result = new List<Diagnostic>();
            foreach (var document in project.Documents)
            {
                result.AddRange(await CompoundPredicateTestFixture.Diagnostics(document));
            }

            return result;
        }
    }
}
