using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

internal static class InlineConditionTestFixture
{
    private static readonly ImmutableArray<MetadataReference> References = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToImmutableArray<MetadataReference>();

    public static Document Document(string source, string? config = null)
    {
        var workspace = new AdhocWorkspace();
        var project = workspace.AddProject("Conditions", LanguageNames.CSharp)
            .WithMetadataReferences(References)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.CSharp12))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        if (config is not null)
        {
            project = project.AddAnalyzerConfigDocument(".editorconfig", Microsoft.CodeAnalysis.Text.SourceText.From(config),
                filePath: "/.editorconfig").Project;
        }

        return project.AddDocument("Test.cs", source, filePath: "/Test.cs");
    }

    public static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document, bool includeReturns = false)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var analyzers = includeReturns ? ImmutableArray.Create<DiagnosticAnalyzer>(new InlineConditionAnalyzer(),
            new ExplicitReturnDecisionAnalyzer(), new NestedReturnDecisionAnalyzer())
            : ImmutableArray.Create<DiagnosticAnalyzer>(new InlineConditionAnalyzer());
        var diagnostics = await compilation!.WithAnalyzers(analyzers, document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start).ThenBy(diagnostic => diagnostic.Id).ToImmutableArray();
    }

    public static async Task<List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        var context = new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None);
        await new InlineConditionCodeFixProvider().RegisterCodeFixesAsync(context);
        return actions;
    }

    public static async Task<Document> Fix(Document document, string key, string? id = null)
    {
        var diagnostic = (await Diagnostics(document)).First(diagnostic => id is null || diagnostic.Id == id);
        var actions = await Actions(document, diagnostic);
        if (actions.Any(action => action.EquivalenceKey == InlineConditionCodeFixProvider.ExtractKey))
        {
            Assert.Equal(InlineConditionCodeFixProvider.ExtractKey, actions[0].EquivalenceKey);
        }
        var action = Assert.Single(actions.Where(action => action.EquivalenceKey == key));
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        return operation.ChangedSolution.GetDocument(document.Id)!;
    }

    public static async Task Compiles(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = compilation!.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error
            || diagnostic.Id.StartsWith("CS86", StringComparison.Ordinal)).ToArray();
        Assert.True(diagnostics.Length == 0, string.Join(Environment.NewLine, diagnostics.Select(diagnostic => diagnostic.ToString())));
    }

    public static async Task<string> Run(Document document)
    {
        await Compiles(document);
        var compilation = await document.Project.GetCompilationAsync();
        using var stream = new MemoryStream();
        var emitted = compilation!.Emit(stream);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(stream);
            return (string)assembly.GetType("C")!.GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!.Invoke(null, null)!;
        }
        finally
        {
            context.Unload();
        }
    }
}
