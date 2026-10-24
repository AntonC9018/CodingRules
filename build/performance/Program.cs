using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodingRules;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

var documents = args.Length > 0 ? int.Parse(args[0]) : 32;
var edits = args.Length > 1 ? int.Parse(args[1]) : 12;
var unrelatedSites = args.Length > 2 ? int.Parse(args[2]) : 20;
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
await Measure(2, 1, 1, timeout.Token); // JIT and Roslyn warm-up, excluded from timing.
var result = await Measure(documents, edits, unrelatedSites, timeout.Token);
Console.WriteLine(JsonSerializer.Serialize(result));

static async Task<object> Measure(int documentCount, int edits, int unrelatedSites, CancellationToken token)
{
    using var workspace = new AdhocWorkspace();
    var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path));
    var project = workspace.AddProject("Benchmark", LanguageNames.CSharp).WithMetadataReferences(references)
        .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
        .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var target = project.AddDocument("Target.cs", Source("Target", edits), filePath: "/Target.cs");
    project = target.Project;
    for (var index = 1; index < documentCount; index++)
        project = project.AddDocument("Unrelated" + index + ".cs", Source("Unrelated" + index, unrelatedSites), filePath: "/Unrelated" + index + ".cs").Project;
    target = project.GetDocument(target.Id)!;
    var compilation = await project.GetCompilationAsync(token);
    if (compilation!.GetDiagnostics(token).Any(d => d.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Invalid benchmark source");
    var initial = await Diagnostics.Read(target, token);
    if (initial.Length != edits) throw new InvalidOperationException("Unexpected initial diagnostic count");
    var provider = new StatementOperationCodeFixProvider();
    var context = new FixAllContext(target, provider, FixAllScope.Document, StatementOperationCodeFixProvider.ExtractKey,
        provider.FixableDiagnosticIds, new Diagnostics(), token);
    var watch = Stopwatch.StartNew();
    var action = await provider.GetFixAllProvider().GetFixAsync(context);
    var applied = (await action!.GetOperationsAsync(token)).OfType<ApplyChangesOperation>().Single();
    watch.Stop();
    var changed = applied.ChangedSolution.GetDocument(target.Id)!;
    if (!(await Diagnostics.Read(changed, token)).IsEmpty) throw new InvalidOperationException("Fix All did not converge");
    var changedCompilation = await changed.Project.GetCompilationAsync(token);
    if (changedCompilation!.GetDiagnostics(token).Any(d => d.Severity == DiagnosticSeverity.Error)) throw new InvalidOperationException("Fixed source does not compile");
    foreach (var other in project.Documents.Where(d => d.Id != target.Id))
        if (!(await other.GetTextAsync(token)).ContentEquals(await applied.ChangedSolution.GetDocument(other.Id)!.GetTextAsync(token)))
            throw new InvalidOperationException("Document-scope fix changed another document");
    var output = (await changed.GetTextAsync(token)).ToString();
    return new { documentCount, edits, unrelatedSites, elapsedMs = watch.Elapsed.TotalMilliseconds,
        outputSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output))) };
}

static string Source(string name, int count) => "public static class " + name + " { static int Get(int value) => value+1; static int Use(int value) => value; "
    + string.Join(" ", Enumerable.Range(0, count).Select(index => "public static int M" + index + "(int input) => Use(Get(input));")) + " }";

sealed class Diagnostics : FixAllContext.DiagnosticProvider
{
    public static async Task<ImmutableArray<Diagnostic>> Read(Document document, CancellationToken token)
    {
        var model = await document.GetSemanticModelAsync(token);
        return await model!.Compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new StatementOperationAnalyzer()), document.Project.AnalyzerOptions)
            .GetAnalyzerSemanticDiagnosticsAsync(model, null, token);
    }
    public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken) => await Read(document, cancellationToken);
    public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());
    public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
    {
        var result = new List<Diagnostic>();
        foreach (var document in project.Documents) result.AddRange(await Read(document, cancellationToken));
        return result;
    }
}
