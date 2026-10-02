using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace CodingRules;

public sealed class SentinelRewriteTests
{
    [Theory]
    [InlineData("ref int alias = ref value; alias = -2;")]
    [InlineData("ref int alias = ref value; ref int second = ref alias; second = -2;")]
    [InlineData("int other = 0; ref int alias = ref other; alias = ref value; alias = -2;")]
    public async Task RefAliasMutationCannotCertifyOrRewriteOriginalLocal(string mutation)
    {
        var document = SentinelTestFixture.Document($$"""
            public class C {
                private static int Absent(string text) { var index = text.IndexOf('x'); if (index >= 0) throw new System.Exception(); return -1; }
                private static int Forward() { var value = Absent(""); {{mutation}} return value; }
                public static string Run() => Forward().ToString();
            }
            """);
        var saved = await StatementOperationTestFixture.Reparse(document);
        Assert.Equal("-2", await StatementOperationTestFixture.Run(saved));
        Assert.Empty(await SentinelTestFixture.Diagnostics(saved));
    }

    [Theory]
    [InlineData("ref readonly int alias = ref value; return value;")]
    [InlineData("ref int alias = ref value; return alias;")]
    [InlineData("return Read(ref value);")]
    public async Task RefExposureAndRefLocalProvenanceRemainUnknown(string statements)
    {
        var document = SentinelTestFixture.Document("class C { private static ref int Read(ref int value) => ref value; private static int Search() => \"abc\".IndexOf('x'); public static int Forward() { var value = Search(); " + statements + " } }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("{ return Absent(Argument()); }", "", "argument;search;-1")]
    [InlineData("=> Absent(Argument());", "", "argument;search;-1")]
    [InlineData("{ return ((int)(Absent(Argument()))); }", "", "argument;search;-1")]
    [InlineData("{ return Absent(Argument()); }", "x", "argument;search;throw")]
    [InlineData("=> Absent(Argument());", "x", "argument;search;throw")]
    [InlineData("{ return Absent(Argument()); }", "null", "argument;search;throw")]
    public async Task SentinelOnlyInvocationIsStagedAndPreservesExceptions(string body, string input, string expected)
    {
        var argument = input == "null" ? "null!" : "\"" + input + "\"";
        var document = SentinelTestFixture.Document($$"""
            public class C {
                static string log = "";
                static string Argument() { log += "argument;"; return {{argument}}; }
                private static int Absent(string text) { log += "search;"; var index = text.IndexOf('x'); if (index >= 0) throw new System.Exception(); return -1; }
                private static int Forward() {{body}}
                public static string Run() { log = ""; try { var value = Forward(); return log + value; } catch { return log + "throw"; } }
            }
            """);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var diagnostic = Assert.Single(await SentinelTestFixture.Diagnostics(document));
        var changed = await SentinelTestFixture.Fix(document, diagnostic);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
        Assert.Contains("int sentinelResult =", (await changed.GetTextAsync()).ToString());
        Assert.Empty(await SentinelTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("", -1, false)]
    [InlineData("x", 0, false)]
    [InlineData("abx", 2, false)]
    [InlineData("x", 0, true)]
    public async Task RuntimePreservesSingleEvaluationOrderOutcomesAndExceptions(string text, int expected, bool throws)
    {
        var document = SentinelTestFixture.Document($$"""
            using System;
            public class C {
                static string log = "";
                static C Receiver() { log += "receiver;"; return new C(); }
                static string Argument() { log += "argument;"; if ({{throws.ToString().ToLowerInvariant()}}) throw new Exception(); return "{{text}}"; }
                private int Search(string value) { log += "search;"; return value.IndexOf('x'); }
                private static int Forward() { log += "before;"; return Receiver().Search(Argument()); }
                public static string Run() { log = ""; try { var value = Forward(); return log + value; } catch { return log + "throw"; } }
            }
            """);
        var original = await StatementOperationTestFixture.Run(document);
        Assert.Equal(throws ? "before;receiver;argument;throw" : "before;receiver;argument;search;" + expected, original);
        var changed = await SentinelTestFixture.Fix(document);
        Assert.Equal(original, await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await SentinelTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task SavedLocalUsesExistingEvaluationAndSentinelOnlyArmUsesLiteral()
    {
        var document = SentinelTestFixture.Document("class C { private int Search() => \"abc\".IndexOf('x'); private int Forward() { var value = Search(); if (value == -1) return value; return value; } }");
        var diagnostic = Assert.Single(await SentinelTestFixture.Diagnostics(document));
        var changed = await SentinelTestFixture.Fix(document, diagnostic);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("if (value == -1) return -1;", text);
        Assert.Equal(1, text.Split("Search();").Length - 1);
        Assert.Empty(await SentinelTestFixture.Diagnostics(changed));
    }

    [Fact]
    public async Task SourceBoundariesLocalFunctionsAndMultiHopSummarySurviveFixes()
    {
        var document = SentinelTestFixture.Document("class C { private static int Search() => \"abc\".IndexOf('x'); private static int Middle() => Search(); private static int Forward() { int Local() => Middle(); return Local(); } }");
        Assert.Equal(3, (await SentinelTestFixture.Diagnostics(document)).Length);
        while (!(await SentinelTestFixture.Diagnostics(document)).IsEmpty) document = await SentinelTestFixture.Fix(document);
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("private int Forward() => Search(); static void Next() { Observe(); } static void Observe([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {}")]
    [InlineData("private int Forward() => Search(); public int this[int index, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0] => line; private int Next() => this[0];")]
    [InlineData("private int Forward() => Search(); [A] class Next {} class A : System.Attribute { public A([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {} }")]
    [InlineData("private int Forward() => Search(); class Base { public Base([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) {} } class Next : Base { public Next() : base() {} }")]
    public async Task AllLaterImplicitCallerConstantsExcludeActions(string members)
    {
        var document = SentinelTestFixture.Document("class C { private int Search() => \"abc\".IndexOf('x'); " + members + " }");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await SentinelTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task ExplicitCallerConstantsAndCheckedContextArePreserved()
    {
        var document = SentinelTestFixture.Document("class C { private int Search([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => \"abc\".IndexOf('x'); private int Forward() { checked { return Search(line: 42); } } }");
        var changed = await SentinelTestFixture.Fix(document);
        Assert.Contains("Search(line: 42)", (await changed.GetTextAsync()).ToString());
        Assert.Contains("checked", (await changed.GetTextAsync()).ToString());
        Assert.Empty(await SentinelTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("private int Forward() { return Search(/* preserve */); }")]
    [InlineData("private int Forward() {\n#if DEBUG\nreturn Search();\n#else\nreturn 0;\n#endif\n}")]
    [InlineData("static System.Linq.Expressions.Expression<System.Func<int>> expr = () => Search();")]
    [InlineData("private int Forward() { label: return Search(); }")]
    [InlineData("private int Forward() => Search() + 0;")]
    public async Task UnsafeOrUnsupportedRewriteShapesAreSkipped(string member)
        => Assert.Empty(await SentinelTestFixture.Diagnostics(SentinelTestFixture.Document("class C { private static int Search() => \"abc\".IndexOf('x'); " + member + " }")));

    [Fact]
    public async Task TriviaFreshNamesAndEditorconfigSurviveSaving()
    {
        var document = SentinelTestFixture.Document("class C { private int Search() => \"abc\".IndexOf('x'); private int Forward() { int sentinelResult = 0; System.Console.WriteLine(sentinelResult);\n// before\nreturn Search(); // after\n} }",
            "root=true\n[*.cs]\nindent_style=space\nindent_size=2\n");
        var changed = await SentinelTestFixture.Fix(document);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("sentinelResult2", text); Assert.Contains("// before", text); Assert.Contains("// after", text);
        Assert.Empty(await SentinelTestFixture.Diagnostics(changed));
    }

    [Theory]
    [InlineData("private int Forward() => /* stage */ Search(); // after\n")]
    [InlineData("private int Forward() { return /* stage */ Search(); } // after\n")]
    public async Task ReturnAndArrowExteriorCommentsSurvive(string member)
    {
        var document = SentinelTestFixture.Document("class C { private int Search() => \"abc\".IndexOf('x'); " + member + " }");
        var changed = await SentinelTestFixture.Fix(document);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("/* stage */", text); Assert.Contains("// after", text);
        Assert.DoesNotContain("\r\n", text);
    }

    [Theory]
    [InlineData(FixAllScope.Document)]
    [InlineData(FixAllScope.Project)]
    [InlineData(FixAllScope.Solution)]
    public async Task FixAllCrossDocumentCalleeFirstAndIdempotent(FixAllScope scope)
    {
        const string source = "partial class C { private static int Search() => \"abc\".IndexOf('x'); private static int Middle() => Search(); private static int Forward() => Middle(); public int Api() => Search(); }";
        var document = SentinelTestFixture.Document(source);
        var second = document.Project.AddDocument("Second.cs", "partial class C { private int Other() => Middle(); }", filePath: "/Second.cs");
        var otherProject = ProjectId.CreateNewId(); var otherDocument = DocumentId.CreateNewId(otherProject);
        var solution = second.Project.Solution.AddProject(otherProject, "Other", "Other", LanguageNames.CSharp)
            .WithProjectMetadataReferences(otherProject, document.Project.MetadataReferences)
            .WithProjectParseOptions(otherProject, document.Project.ParseOptions!)
            .WithProjectCompilationOptions(otherProject, document.Project.CompilationOptions!)
            .AddDocument(otherDocument, "Other.cs", SourceText.From(source.Replace("class C", "class D")), filePath: "/Other.cs");
        document = solution.GetDocument(document.Id)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var provider = new PrimitiveSentinelCodeFixProvider();
        var context = new FixAllContext(document, provider, scope, PrimitiveSentinelCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), timeout.Token);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
        foreach (var changed in operation.ChangedSolution.Projects.SelectMany(project => project.Documents))
        {
            var included = scope == FixAllScope.Solution || changed.Project.Id == document.Project.Id && (scope != FixAllScope.Document || changed.Id == document.Id);
            if (!included) { Assert.Equal((await solution.GetDocument(changed.Id)!.GetTextAsync()).ToString(), (await changed.GetTextAsync()).ToString()); continue; }
            await StatementOperationTestFixture.Compiles(changed);
            var tree = await changed.GetSyntaxTreeAsync();
            Assert.DoesNotContain(await SentinelTestFixture.Diagnostics(changed), item => item.Location.SourceTree == tree && item.Id == "CR0601");
            var again = new FixAllContext(changed, provider, FixAllScope.Document, PrimitiveSentinelCodeFixProvider.Key, provider.FixableDiagnosticIds, new Diagnostics(), timeout.Token);
            var repeated = await provider.GetFixAllProvider().GetFixAsync(again);
            var repeat = Assert.Single((await repeated!.GetOperationsAsync(timeout.Token)).OfType<ApplyChangesOperation>());
            Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeat.ChangedSolution.GetDocument(changed.Id)!.GetTextAsync()).ToString());
        }
    }
    private sealed class Diagnostics : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken token)
        { var tree = await document.GetSyntaxTreeAsync(token); return (await SentinelTestFixture.Diagnostics(document)).Where(item => item.Location.SourceTree == tree); }
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken token) => Task.FromResult(Enumerable.Empty<Diagnostic>());
        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken token)
        { var result = new List<Diagnostic>(); foreach (var document in project.Documents) result.AddRange(await GetDocumentDiagnosticsAsync(document, token)); return result; }
    }
}
