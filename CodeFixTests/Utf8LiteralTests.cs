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
        using System.Linq.Expressions;
        using System.Runtime.CompilerServices;
        using System.Text;
        public static class C
        {
            static int Length(ReadOnlySpan<byte> bytes) => bytes.Length;
            static int Count(byte[] bytes) => bytes.Length;
            static string Text(byte[] bytes, [CallerArgumentExpression("bytes")] string text = "") => text;
            static string SpanText(ReadOnlySpan<byte> bytes, [CallerArgumentExpression("bytes")] string text = "") => text;

        """;

    [Fact]
    public async Task SpanTargetReceivesTheLiteralItself()
    {
        await AssertFix(
            "    static int M() => Length(Encoding.UTF8.GetBytes(\"abc\"));\n}\n",
            "    static int M() => Length(\"abc\"u8);\n}\n");
    }

    [Theory]
    [InlineData("var bytes = Encoding.UTF8.GetBytes(\"abc\"); return bytes;")]
    [InlineData("byte[] bytes; bytes = (Encoding.UTF8.GetBytes(\"abc\")); return bytes;")]
    [InlineData("var holder = new Holder { Bytes = Encoding.UTF8.GetBytes(\"abc\") }; return holder.Bytes;")]
    [InlineData("var all = new byte[1][]; all[0] = Encoding.UTF8.GetBytes(\"abc\"); return all[0];")]
    public async Task ArrayStoredInAByteArrayTargetIsNotReported(string body)
    {
        var document = Document(Prelude + "    sealed class Holder { public byte[] Bytes = Encoding.UTF8.GetBytes(\"x\"); public byte[] Other { get; } = Encoding.UTF8.GetBytes(\"y\"); }\n"
            + "    static byte[] M() { " + body + " }\n}\n");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
    }

    [Fact]
    public async Task ReturnedArrayIsDeclaredAsRet()
    {
        await AssertFix(
            "    static byte[] M()\n    {\n        return Encoding.UTF8.GetBytes(\"abc\");\n    }\n}\n",
            "    static byte[] M()\n    {\n        byte[] ret = \"abc\"u8.ToArray();\n        return ret;\n    }\n}\n");
    }

    [Fact]
    public async Task ReturnedNameAvoidsACollision()
    {
        await AssertFix(
            "    static object M(int ret)\n    {\n        return (Encoding.UTF8.GetBytes(\"abc\"));\n    }\n}\n",
            "    static object M(int ret)\n    {\n        byte[] ret2 = \"abc\"u8.ToArray();\n        return (ret2);\n    }\n}\n");
    }

    [Fact]
    public async Task ExpressionBodiedMethodReturnsRetFromABlock()
    {
        await AssertFix(
            "    static byte[] M() => Encoding.UTF8.GetBytes(\"abc\"); // marker\n}\n",
            "    static byte[] M()\n    {\n        byte[] ret = \"abc\"u8.ToArray();\n        return ret;\n    } // marker\n}\n");
    }

    [Fact]
    public async Task ExpressionBodiedPropertyReturnsRetFromAGetter()
    {
        await AssertFix(
            "    static byte[] Marker => Encoding.UTF8.GetBytes(\"abc\");\n}\n",
            "    static byte[] Marker\n    {\n        get\n        {\n            byte[] ret = \"abc\"u8.ToArray();\n            return ret;\n        }\n    }\n}\n");
    }

    [Fact]
    public async Task ObjectTargetReadsAPlaceholder()
    {
        await AssertFix(
            "    static object M()\n    {\n        object value = Encoding.UTF8.GetBytes(\"abc\");\n        return value;\n    }\n}\n",
            "    static object M()\n    {\n        byte[] TODO_VARIABLE = \"abc\"u8.ToArray();\n        object value = TODO_VARIABLE;\n        return value;\n    }\n}\n");
    }

    [Theory]
    [InlineData("Text(Encoding.UTF8.GetBytes(\"abc\"))")]
    [InlineData("SpanText(Encoding.UTF8.GetBytes(\"abc\"))")]
    public async Task CallerArgumentExpressionTextIsPreservedByDecliningTheFix(string expression)
    {
        var document = Document(Prelude + "    static string M()\n    {\n        var text = " + expression + ";\n        return text;\n    }\n}\n");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Empty(await Actions(document, diagnostic));
    }

    [Fact]
    public async Task ExpressionTreeIsNotReported()
    {
        var document = Document(Prelude
            + "    static Expression<Func<int>> M() => () => Count(Encoding.UTF8.GetBytes(\"abc\"));\n}\n");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
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
