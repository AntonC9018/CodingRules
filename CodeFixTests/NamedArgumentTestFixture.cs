using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

internal static class NamedArgumentTestFixture
{
    public static Document Document(string source, string? config = null)
    {
        var document = StatementOperationTestFixture.Document(source, config);
        var path = typeof(AllowPositionalArgumentsAttribute).Assembly.Location;
        if (!document.Project.MetadataReferences.OfType<PortableExecutableReference>().Any(reference => reference.FilePath == path))
            document = document.Project.AddMetadataReference(MetadataReference.CreateFromFile(path)).GetDocument(document.Id)!;
        return document;
    }

    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new NamedArgumentAnalyzer()), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ToImmutableArray();
    }
    public static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await new NamedArgumentCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }
    public static async Task<Document> Fix(Document document, Diagnostic? diagnostic = null)
    {
        diagnostic ??= (await Diagnostics(document)).First();
        var action = Assert.Single(await Actions(document, diagnostic));
        var applied = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = applied.ChangedSolution.GetDocument(document.Id)!;
        await StatementOperationTestFixture.Compiles(changed);
        return changed;
    }
}
