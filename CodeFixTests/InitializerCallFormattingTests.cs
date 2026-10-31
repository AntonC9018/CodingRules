using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Xunit;

namespace CodingRules;

public sealed partial class FormattingReuseTests
{
    [Fact]
    public async Task ExpandsSingleMemberInitializer()
    {
        var document = Document("class C { C Value = new() { Number = 1 }; public int Number; }");
        Assert.Contains(await Diagnostics(document), d => d.Id == "CR0801");
        var changed = await Formatter.FormatAsync(await FixAll(document));
        Assert.Contains("new()\n", (await changed.GetTextAsync()).ToString());
        await AssertStableLayout(document, changed);
    }

    [Fact]
    public async Task LeadingCommentDoesNotPushSingleMemberInitializerAcrossTheLine()
    {
        var document = Document("class C { public int Value; void Use(C x) {} void M() { Use(\n    /* keep */ new C { Value = 1 }); } }");
        var changed = await Formatter.FormatAsync(await FixAll(document));
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("/* keep */", text);
        Assert.DoesNotContain("/* keep */ new", text);
        Assert.DoesNotContain(new string(' ', 30) + "{", text);
        await AssertStableLayout(document, changed);
    }

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
    public async Task FormatsPinnedHandlerRequestsWithoutExtractingInitializerValues()
    {
        // Reduced AppTasks.cs:73/127 cases at ScheduleLib 12e601e7b62511bdecc44e280407fee12147b230.
        var document = Document("""
            using System.Text;
            using System.Threading;
            using System.Threading.Tasks;
            class OutputDirectoryFilesProvider(object directory) { }
            class Request
            {
                public OutputDirectoryFilesProvider FilesProvider = null!;
                public CancellationToken CancellationToken;
                public string[] Groups = [];
                public StringBuilder StringBuilder = null!;
            }
            class Handler
            {
                public Task Run(Request request) { return Task.CompletedTask; }
            }
            class C
            {
                public object OutputDirectory = new();
                public CancellationToken CancellationToken;
                async Task M(C c, Handler handler, StringBuilder sb)
                {
                    await handler.Run(
                        new()
                        {
                            FilesProvider = new OutputDirectoryFilesProvider(c.OutputDirectory),
                            CancellationToken = c.CancellationToken,
                        });
                    await handler.Run(new() { Groups = ["IA2401", "I2301"], StringBuilder = sb, });
                }
            }
            """);
        Assert.Equal(new[] { "CR0802", "CR0801" }, (await Diagnostics(document)).Select(item => item.Id));
        var operations = await OperationFindings(document);
        var changed = await Formatter.FormatAsync(await FixAll(document));
        Assert.Contains("""
                    await handler.Run(new()
                    {
                        FilesProvider = new OutputDirectoryFilesProvider(c.OutputDirectory),
                        CancellationToken = c.CancellationToken,
                    });
                    await handler.Run(new()
                    {
                        Groups = ["IA2401", "I2301"],
                        StringBuilder = sb,
                    });
            """, (await changed.GetTextAsync()).ToString());
        Assert.Equal(operations, await OperationFindings(changed));
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
        var changed = await Formatter.FormatAsync(await FixAll(document));
        // Reuse Roslyn's lambda indentation after joining the constructor.
        Assert.Contains("""
                    updates.Add(new DelegateUpdater<T>((sp, value) =>
                        {
                            object? current = value;
                            Console.WriteLine(current);
                        }));
            """, (await changed.GetTextAsync()).ToString());
        await AssertStableLayout(document, changed);
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
        // A1 must neither add operation findings nor extract these deliberately
        // inline values. CR0300's argument policy is independent of the #19 fix.
        var before = await OperationFindings(document);
        var changed = await FixAll(document);
        Assert.Equal(before, await OperationFindings(changed));
        await AssertStableLayout(document, await Formatter.FormatAsync(changed));
    }

    [Theory]
    [InlineData("capture", false)]
    [InlineData("capture", true)]
    [InlineData("capture-join", false)]
    [InlineData("capture-join", true)]
    [InlineData("constructor", false)]
    [InlineData("constructor", true)]
    [InlineData("downstream-expand", false)]
    [InlineData("downstream-expand", true)]
    [InlineData("downstream-join", false)]
    [InlineData("downstream-join", true)]
    public async Task InitializerLayoutPreservesCallerInformation(string scenario, bool fixAll)
    {
        var statement = scenario switch
        {
            "capture" => "return Capture(new Payload { A = 1, B = 2 });",
            "capture-join" => "return Capture(Identity(\n            new Payload(line: 0)));",
            "constructor" => "Use(\n            new Payload()); return result;",
            "downstream-expand" => "Use(new Payload(line: 0) { A = 1, B = 2 }); return Later();",
            _ => "Use(\n            new Payload(line: 0)); return Later();",
        };
        var laterReturn = scenario.StartsWith("downstream", StringComparison.Ordinal) ? "Line()" : "\"unused\"";
        var document = Document($$"""
            using System.Runtime.CompilerServices;
            public class Payload
            {
                public int A;
                public int B;
                public int Line;
                public Payload([CallerLineNumber] int line = 0) { Line = line; }
            }
            public class C
            {
                static string result = "";
                static void Use(Payload value) { result = value.Line.ToString(); }
                static Payload Identity(Payload value) { return value; }
                static string Capture(Payload value, [CallerArgumentExpression("value")] string expression = "") { return expression; }
                public static string Run()
                {
                    {{statement}}
                }
                static string Later()
                {
                    return {{laterReturn}};
                }
                static string Line([CallerLineNumber] int line = 0) { return line.ToString(); }
            }
            """);
        document = await Formatter.FormatAsync(document);
        var expected = await StatementOperationTestFixture.Run(document);
        var id = scenario is "capture" or "downstream-expand" ? "CR0801" : "CR0802";
        Document changed;
        if (fixAll)
        {
            changed = await ExportedInitializerFixAll(document, id);
        }
        else
        {
            var diagnostic = Assert.Single((await Diagnostics(document)).Where(item => item.Id == id));
            var actions = new List<CodeAction>();
            await new InitializerCallLayoutCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
                (action, _) => actions.Add(action), CancellationToken.None));
            changed = document;
            if (actions.Count > 0)
            {
                var operations = await Assert.Single(actions).GetOperationsAsync(CancellationToken.None);
                changed = Assert.Single(operations.OfType<ApplyChangesOperation>()).ChangedSolution.GetDocument(document.Id)!;
            }
        }
        changed = await StatementOperationTestFixture.Reparse(await Formatter.FormatAsync(changed));
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
        Assert.Equal((await document.GetTextAsync()).ToString(), (await changed.GetTextAsync()).ToString());
        var repeated = await ExportedInitializerFixAll(changed, id);
        Assert.Equal((await changed.GetTextAsync()).ToString(), (await repeated.GetTextAsync()).ToString());
    }

    [Theory]
    [InlineData("CR0801")]
    [InlineData("CR0802")]
    public async Task InitializerFixAllSkipsUnsafeSitesAndStillFormatsSafeSites(string id)
    {
        var document = Document("""
            using System.Runtime.CompilerServices;
            public class Payload { public int A; public int B; }
            public class C
            {
                static void Use(Payload value) { }
                static string Capture(Payload value, [CallerArgumentExpression("value")] string expression = "") { return expression; }
                public static string Run()
                {
                    var expression = Capture(new Payload { A = 1, B = 2 });
                    Use(
                        new Payload { A = 3, B = 4 });
                    return expression;
                }
            }
            """);
        var expected = await StatementOperationTestFixture.Run(document);
        // CLI hosts may have no action key when their first diagnostic is unsafe.
        var changed = await ExportedInitializerFixAll(document, id, null);
        var text = (await changed.GetTextAsync()).ToString();
        Assert.Contains("Capture(new Payload { A = 1, B = 2 })", text);
        if (id == "CR0801") Assert.Contains("A = 3,\n", text);
        else Assert.Contains("Use(new Payload", text);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(await StatementOperationTestFixture.Reparse(changed)));
        Assert.Equal(text, (await (await ExportedInitializerFixAll(changed, id, null)).GetTextAsync()).ToString());
    }

    private static async Task<Document> ExportedInitializerFixAll(Document document, string id, string? key = nameof(InitializerCallLayoutCodeFixProvider))
    {
        var provider = new InitializerCallLayoutCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Document, key,
            new[] { id }, new InitializerDiagnostics(id), CancellationToken.None);
        var action = await provider.GetFixAllProvider().GetFixAsync(context);
        if (action is null) return document;
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        return operations.OfType<ApplyChangesOperation>().SingleOrDefault()?.ChangedSolution.GetDocument(document.Id) ?? document;
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
        // The CLI groups both IDs under this provider and reuses the selected
        // action's key when fixing the other ID. Exercise that shared key.
        var otherDiagnostic = (await Diagnostics(document)).First(diagnostic => diagnostic.Id != id);
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(document, otherDiagnostic,
            (action, _) => actions.Add(action), CancellationToken.None));
        var key = Assert.Single(actions).EquivalenceKey;
        var context = new FixAllContext(document, provider, FixAllScope.Document, key,
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
