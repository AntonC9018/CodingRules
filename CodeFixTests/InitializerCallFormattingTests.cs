using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Xunit;

namespace CodingRules;

public sealed partial class FormattingReuseTests
{
    [Theory]
    [InlineData("new()", "new()")]
    [InlineData("new Payload()", "new Payload()")]
    public async Task ExpandsPinnedA1InitializerAndJoinsSoleConstruction(string creation, string expectedCreation)
    {
        var document = Document($$"""
            class Payload { public int Context; public int AppTask; public int Services; }
            class C
            {
                void ExecuteTask(Payload value) { }
                void Run(int context, int option, int services)
                {
                    ExecuteTask(
                        {{creation}} { Context = context, AppTask = option, Services = services, });
                }
            }
            """);
        Assert.Equal(new[] { "CR0802", "CR0801" }, (await Diagnostics(document)).Select(item => item.Id));
        var changed = await Formatter.FormatAsync(await FixAll(document));
        Assert.Contains($$"""
                    ExecuteTask({{expectedCreation}}
                    {
                        Context = context,
                        AppTask = option,
                        Services = services,
                    });
            """, (await changed.GetTextAsync()).ToString());
        await AssertStableLayout(document, changed);
    }

    [Fact]
    public async Task JoinsPinnedExplicitDelegateConstructorWithoutChangingLambda()
    {
        var document = Document("""
            using System;
            using System.Collections.Generic;
            class DelegateUpdater<T>(Action<object, T> update) { }
            class C
            {
                void Run<T>(List<DelegateUpdater<T>> updates)
                {
                    updates.Add(
                        new DelegateUpdater<T>((sp, value) =>
                        {
                            object? current = value;
                            Console.WriteLine(current);
                        }));
                }
            }
            """);
        var diagnostic = Assert.Single(await Diagnostics(document));
        Assert.Equal("CR0802", diagnostic.Id);
        var changed = await FixAll(document);
        Assert.Contains("updates.Add(new DelegateUpdater<T>((sp, value) =>", (await changed.GetTextAsync()).ToString());
        await AssertStableLayout(document, await Formatter.FormatAsync(changed));
    }

    [Fact]
    public async Task NestedInitializersAndCommentsKeepTokenTextAndEvaluationOrder()
    {
        var original = Document("""
            public class Payload
            {
                public int A;
                public int B;
                public Payload? Child;
            }
            public class C
            {
                public static string Run()
                {
                    string trace = "";
                    int Next(int n) { trace += n; return n; }
                    void Use(Payload p) { trace += p.Child!.B; }
                    Use(
                        new() { A = Next(1), /* between */ Child = new Payload { A = Next(2), B = Next(3) }, B = Next(4), /* trailing */ });
                    return trace;
                }
            }
            """);
        Assert.Equal("12343", await StatementOperationTestFixture.Run(original));
        var changed = await Formatter.FormatAsync(await FixAll(original));
        Assert.Equal("12343", await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
        Assert.Contains("/* between */", (await changed.GetTextAsync()).ToString());
        Assert.Contains("/* trailing */", (await changed.GetTextAsync()).ToString());
        await AssertStableLayout(original, changed);
    }

    [Theory]
    [InlineData("\n", "    ")]
    [InlineData("\r\n", "\t")]
    public async Task InitializerFixUsesConsumerIndentationAndLineEndings(string newline, string indentation)
    {
        var source = "class C\n{\n    public int A;\n    public int B;\n    void M()\n    {\n        Use(new C { A = 1, B = 2 });\n    }\n    void Use(C c) { }\n}";
        source = source.Replace("    ", indentation).Replace("\n", newline);
        var style = indentation == "\t" ? "tab" : "space";
        var document = Document(source, $"root = true\n[*]\nindent_style = {style}\nindent_size = 4\ntab_width = 4\nend_of_line = {(newline == "\n" ? "lf" : "crlf")}\n");
        var changed = await Formatter.FormatAsync(await FixAll(document));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains(newline + string.Concat(Enumerable.Repeat(indentation, 3)) + "A = 1,", text);
        if (newline == "\r\n") Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        await AssertStableLayout(document, changed);
    }

    [Theory]
    [InlineData("Use(new() { A = 1 });")]
    [InlineData("Use(new C());")]
    [InlineData("Use(new()\n        {\n            A = 1, // first\n            B = 2,\n        });")]
    [InlineData("Use( // keep explanation\n            new C());")]
    [InlineData("Use(\n            /* keep explanation */ new C());")]
    [InlineData("Use(value:\n            new C());")]
    [InlineData("Use(\n            new C(), 1);")]
    [InlineData("Use(\n            Factory());")]
    [InlineData("new C(\n            new C());")]
    public async Task AcceptsAdjacentConstructorsAndLeavesOtherArgumentPolicies(string statement)
    {
        var document = Document($$"""
            class C
            {
                public int A;
                public int B;
                public C() { }
                public C(C value) { }
                void Use(C value, int other = 0) { }
                C Factory() { return new C(); }
                void M()
                {
                    {{statement}}
                }
            }
            """);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await Diagnostics(document));
        Assert.Equal((await document.GetTextAsync()).ToString(), (await (await FixAll(document)).GetTextAsync()).ToString());
    }

    [Fact]
    public async Task KeepsLineCommentsDirectivesGeneratedCodeAndStringValues()
    {
        var document = Document(""""
            public class C
            {
                public string A = "";
                public string B = "";
                public static string Run()
                {
                    var p = new C { A = """
                        raw
                          text
                        """, // retain line comment
                        B = @"verbatim
                text" };
                    return p.A + p.B;
                }
            }
            """");
        var expected = await StatementOperationTestFixture.Run(document);
        var changed = await Formatter.FormatAsync(await FixAll(document));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
        Assert.Contains("// retain line comment", (await changed.GetTextAsync()).ToString());
        await AssertStableLayout(document, changed);
        Assert.Empty(await Diagnostics(Document("""
            class C
            {
                int A; int B;
                C M() { return new C {
            #if DEBUG
                    A = 1,
            #else
                    A = 2,
            #endif
                    B = 3 }; }
            }
            """)));
        Assert.Empty(await Diagnostics(Document("// <auto-generated/>\nclass C { int A; int B; C M() { return new C { A=1, B=2 }; } }")));
    }

    [Fact]
    public async Task FormattingDoesNotAddNamedStepRequirementsForInitializerValues()
    {
        var document = Document("""
            using System;
            class C
            {
                public TimeSpan Duration;
                public int Value;
                void Use(C value) { }
                void M()
                {
                    Use(new() { Duration = TimeSpan.FromMinutes(90), Value = 1 });
                }
            }
            """);
        // Existing initializer false positives belong to #19. A1 must neither
        // add operation findings nor extract these deliberately inline values.
        var before = await OperationFindings(document);
        var changed = await FixAll(document);
        Assert.Equal(before, await OperationFindings(changed));
        await AssertStableLayout(document, await Formatter.FormatAsync(changed));
    }

    private static async Task<string[]> OperationFindings(Document document)
    {
        var root = (await document.GetSyntaxRootAsync())!;
        var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
        return diagnostics.Select(diagnostic => diagnostic.Id + ":" + string.Concat(root.FindNode(diagnostic.Location.SourceSpan)
            .DescendantTokens().Select(token => token.Text))).ToArray();
    }

    [Theory]
    [InlineData("CR0801")]
    [InlineData("CR0802")]
    public async Task ExportedInitializerFixAllHandlesMultipleSitesAndConsumerSeverity(string id)
    {
        const string source = """
            class C
            {
                int A;
                int B;
                void Use(C value) { }
                void M()
                {
                    Use(
                        new() { A = 1, B = 2 });
                    Use(
                        new C { A = 3, B = 4 });
                }
            }
            """;
        var document = Document(source);
        var provider = new InitializerCallLayoutCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, id,
            new[] { id }, new InitializerDiagnostics(id), CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        Assert.NotNull(action);
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var changed = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution.GetDocument(document.Id)!;
        Assert.DoesNotContain(await Diagnostics(changed), diagnostic => diagnostic.Id == id);
        await AssertStableLayout(document, await Formatter.FormatAsync(await FixAll(changed)));
        var disabled = Document(source, $"root = true\n[*]\ndotnet_diagnostic.{id}.severity = none\n");
        Assert.DoesNotContain(await Diagnostics(disabled), diagnostic => diagnostic.Id == id);
    }

    private sealed class InitializerDiagnostics(string id) : FixAllContext.DiagnosticProvider
    {
        public override async Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document,
            CancellationToken cancellationToken) => (await Diagnostics(document)).Where(diagnostic => diagnostic.Id == id);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project,
            CancellationToken cancellationToken) => Task.FromResult(Enumerable.Empty<Diagnostic>());

        public override async Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project,
            CancellationToken cancellationToken)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (var document in project.Documents)
                diagnostics.AddRange(await GetDocumentDiagnosticsAsync(document, cancellationToken));
            return diagnostics;
        }
    }

    [Fact]
    public async Task StandardFormatterAndRcs0053DoNotCloseThePinnedGaps()
    {
        var document = Document("""
            class C
            {
                int A;
                int B;
                void Use(C value) { }
                void M()
                {
                    Use(new() { A = 1, B = 2 });
                    Use(
                        new C
                        {
                            A = 1,
                            B = 2,
                        });
                }
            }
            """, "root = true\n[*]\ncsharp_preserve_single_line_blocks = true\ncsharp_new_line_before_members_in_object_initializers = true\ndotnet_diagnostic.RCS0053.severity = warning\n");
        document = await Formatter.FormatAsync(document);
        Assert.Contains("Use(new() { A = 1, B = 2 });", (await document.GetTextAsync()).ToString());
        var compilation = await document.Project.GetCompilationAsync();
        var upstream = typeof(Roslynator.Formatting.CSharp.AccessorListAnalyzer).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .Single(analyzer => analyzer.SupportedDiagnostics.Any(rule => rule.Id == "RCS0053"));
        var diagnostics = await Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzerExtensions.WithAnalyzers(compilation!,
            System.Collections.Immutable.ImmutableArray.Create(upstream), document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
        Assert.Empty(diagnostics);
        Assert.Equal(new[] { "CR0801", "CR0802" }, (await Diagnostics(document)).Select(item => item.Id));
    }

    private static async Task AssertStableLayout(Document original, Document changed)
    {
        var before = (await original.GetSyntaxRootAsync())!.DescendantTokens().Select(token => (token.RawKind, token.Text));
        var after = (await changed.GetSyntaxRootAsync())!.DescendantTokens().Select(token => (token.RawKind, token.Text));
        Assert.Equal(before, after);
        await StatementOperationTestFixture.Compiles(await StatementOperationTestFixture.Reparse(changed));
        Assert.Empty(await Diagnostics(changed));
        var repeated = await Formatter.FormatAsync(await FixAll(changed));
        Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeated.GetTextAsync()).ToString());
    }
}
