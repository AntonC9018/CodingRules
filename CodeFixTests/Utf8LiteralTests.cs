using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace CodingRules;

public sealed class Utf8LiteralTests
{
    private const string Prelude = """
        using System;
        using System.IO;
        using System.Text;
        public static class C
        {
            static int Length(ReadOnlySpan<byte> bytes) => bytes.Length;
            static int Count(byte[] bytes) => bytes.Length;

        """;

    [Fact]
    public async Task SpanTargetReceivesTheLiteralItself()
    {
        await AssertFix(
            "    static int M() => Length(Encoding.UTF8.GetBytes(\"abc\"));\n}\n",
            "    static int M() => Length(\"abc\"u8);\n}\n");
    }

    [Fact]
    public async Task WholeAssignedOrReturnedArrayCopiesTheLiteral()
    {
        await AssertFix(
            "    static byte[] M()\n    {\n        var bytes = Encoding.UTF8.GetBytes(\"abc\");\n        return bytes;\n    }\n}\n",
            "    static byte[] M()\n    {\n        var bytes = \"abc\"u8.ToArray();\n        return bytes;\n    }\n}\n");
    }

    [Fact]
    public async Task OtherArrayUsesReadAPlaceholderDeclaredBeforeTheStatement()
    {
        await AssertFix(
            "    static int M(Stream stream)\n    {\n        // Write the marker.\n        stream.Write(Encoding.UTF8.GetBytes(\"abc\"), 0, 3);\n        return Count(Encoding.UTF8.GetBytes(\"d\"));\n    }\n}\n",
            "    static int M(Stream stream)\n    {\n        // Write the marker.\n        byte[] TODO_VARIABLE = \"abc\"u8.ToArray();\n        stream.Write(TODO_VARIABLE, 0, 3);\n        return Count(Encoding.UTF8.GetBytes(\"d\"));\n    }\n}\n");
    }

    [Fact]
    public async Task FixAllNumbersPlaceholdersAndKeepsEachArrayFresh()
    {
        var document = Document(Prelude
            + "    public static string Run()\n    {\n        var marker = Count(Encoding.UTF8.GetBytes(\"ab\"));\n"
            + "        var span = Length(Encoding.UTF8.GetBytes(\"abc\"));\n        return marker + \":\" + span + \":\" + Count(Encoding.UTF8.GetBytes(\"d\"));\n    }\n}\n");
        var before = await StatementOperationTestFixture.Run(document);
        var diagnostics = await Diagnostics(document);
        Assert.Equal(3, diagnostics.Length);
        var fixAll = new Utf8LiteralCodeFixProvider().GetFixAllProvider();
        var context = new FixAllContext(document, new Utf8LiteralCodeFixProvider(), FixAllScope.Document, Utf8LiteralCodeFixProvider.Key,
            new[] { DiagnosticIds.Utf8StringLiteral }, new Provider(diagnostics), CancellationToken.None);
        var action = await fixAll.GetFixAsync(context);
        var operation = Assert.Single((await action!.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        Assert.Equal(Prelude
            + "    public static string Run()\n    {\n        byte[] TODO_VARIABLE = \"ab\"u8.ToArray();\n        var marker = Count(TODO_VARIABLE);\n"
            + "        var span = Length(\"abc\"u8);\n        byte[] TODO_VARIABLE_1 = \"d\"u8.ToArray();\n"
            + "        return marker + \":\" + span + \":\" + Count(TODO_VARIABLE_1);\n    }\n}\n",
            (await changed.GetTextAsync()).ToString());
        Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
        Assert.Empty(await Diagnostics(changed));
    }

    [Theory]
    [InlineData("Count(Encoding.UTF8.GetBytes(text))")]
    [InlineData("Count(Encoding.ASCII.GetBytes(\"abc\"))")]
    [InlineData("Count(Encoding.UTF8.GetBytes(\"a\\uD800\"))")]
    [InlineData("Count(Encoding.UTF8.GetBytes($\"{text}\"))")]
    [InlineData("Count(Encoding.UTF8.GetBytes(\"abc\".ToCharArray()))")]
    public async Task OnlyUtf8EncodingOfAWellFormedStringLiteralIsReported(string expression)
    {
        var document = Document(Prelude + "    static int M(string text) => " + expression + ";\n}\n");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
    }

    [Fact]
    public async Task LanguageVersionsBeforeUtf8LiteralsAreNotReported()
    {
        var document = Document(Prelude + "    static int M() => Count(Encoding.UTF8.GetBytes(\"abc\"));\n}\n", LanguageVersion.CSharp10);
        Assert.Empty(await Diagnostics(document));
    }

    [Fact]
    public async Task UseRepeatedByALambdaIsReportedWithoutAFix()
    {
        var document = Document(Prelude
            + "    static Func<int> M() { Func<int> count = () => Count(Encoding.UTF8.GetBytes(\"abc\")); return count; }\n}\n");
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Empty(await Actions(document, diagnostic));
    }

    private static async Task AssertFix(string body, string expected)
    {
        var document = Document(Prelude + body);
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = (await Diagnostics(document)).First();
        var action = Assert.Single(await Actions(document, diagnostic));
        Assert.Equal(Utf8LiteralCodeFixProvider.Key, action.EquivalenceKey);
        var operation = Assert.Single((await action.GetOperationsAsync(CancellationToken.None)).OfType<ApplyChangesOperation>());
        var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
        Assert.Equal(Prelude + expected, (await changed.GetTextAsync()).ToString());
        await StatementOperationTestFixture.Compiles(changed);
    }

    private static Document Document(string source, LanguageVersion language = LanguageVersion.CSharp12) =>
        SpanTestFixture.Document(source, language: language);

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new Utf8LiteralAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, item => item.Id == "AD0001");
        return diagnostics.OrderBy(item => item.Location.SourceSpan.Start).ToImmutableArray();
    }

    private static async Task<System.Collections.Generic.List<CodeAction>> Actions(Document document, Diagnostic diagnostic)
    {
        var actions = new System.Collections.Generic.List<CodeAction>();
        await new Utf8LiteralCodeFixProvider().RegisterCodeFixesAsync(
            new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), CancellationToken.None));
        return actions;
    }

    private sealed class Provider(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<System.Collections.Generic.IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken token) =>
            Task.FromResult<System.Collections.Generic.IEnumerable<Diagnostic>>(diagnostics);
        public override Task<System.Collections.Generic.IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken token) =>
            Task.FromResult<System.Collections.Generic.IEnumerable<Diagnostic>>(diagnostics);
        public override Task<System.Collections.Generic.IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken token) =>
            Task.FromResult<System.Collections.Generic.IEnumerable<Diagnostic>>(diagnostics);
    }
}
