using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedComputationTests
{
    private const string Prelude = """
        using System;
        using System.IO;
        using System.Linq;
        using System.Collections.Generic;
        using System.Text.Json.Nodes;
        class C
        {
            record Result(int Exit, string[] Errors, bool ReportRemaining);
            class CliOutputStreamingException(string message, Exception inner) : Exception(message, inner);
            class CalendarStreamingException(Result result, Exception inner) : Exception("stream", inner);
            record Key(string Value);
            record NodeDataKey<T>(Key Key);
            class Node { public object GetOrAdd<T>(NodeDataKey<T> key) => key; }
            interface IKeyEqualityComparer<T> { }
            static Exception StreamingFailure(Exception inner) => inner;
            static string ResolvePath(string path) => Path.GetFullPath(path);
        """;

    public static TheoryData<string, string> Cases => new()
    {
        { "static Exception M(string[] outcomes, Exception inner) => new CalendarStreamingException(new Result(outcomes.Any(x => x == \"completed\") ? 6 : 1, [\"Output streaming failed.\"], false), inner);", "CR0301" },
        { "static string M(string? selected) { var path = selected is { } cache ? ResolvePath(cache) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), \"ScheduleLib\", \"cache\"); return path; }", "CR0300" },
        { "static object M<T>(Node node, JsonObject action) => node.GetOrAdd(new NodeDataKey<T>(new(action[\"key\"]!.GetValue<string>().Split('.')[0])));", "CR0300" },
        { "static string[] M(string? profile, string[] parts) { var path = profile is null ? new[] { \"defaults\" }.Concat(parts).ToArray() : new[] { \"profiles\", profile }.Concat(parts).ToArray(); return path; }", "CR0301" },
        { "static int M(Type value) => value.GetGenericArguments().Length + 1;", "CR0302" },
        { "static bool M(IServiceProvider provider, Type value, int length) { if (length != 0 && provider.GetService(typeof(IKeyEqualityComparer<>).MakeGenericType(value.GetGenericArguments()[0])) is null) { return false; } return true; }", "CR0300" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PinnedNonMessageComputationsHaveSavedFixes(string member, string id)
    {
        var document = StatementOperationTestFixture.Document(Prelude + member + "}");
        await StatementOperationTestFixture.Compiles(document);
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Equal(id, diagnostic.Id);
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, action.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(changed);
            for (var pass = 0; pass < 5 && (await StatementOperationTestFixture.Diagnostics(changed)).Length != 0; pass++)
                changed = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(changed, action.EquivalenceKey!));
            Assert.Empty(await StatementOperationTestFixture.Diagnostics(changed));
        }
    }

    [Theory]
    [InlineData("static void M(Exception ex) { throw StreamingFailure(new CliOutputStreamingException(\"Calendar output streaming failed.\", ex)); }")]
    [InlineData("static int M(bool flag) { var value = flag ? 1 : 2; return value; }")]
    [InlineData("static string[] M(string[] parts) => new[] { \"defaults\" }.Concat(parts).ToArray();")]
    [InlineData("static int M(string[] values) => values.Any(x => x.Length > 0) ? 6 : 1;")]
    [InlineData("static TimeSpan M() => TimeSpan.FromMinutes(90);")]
    [InlineData("static Type[] M(Type value) { var arguments = value.GetGenericArguments(); return arguments; }")]
    public async Task SimpleNamedValuesAndLinearPipelinesRemainAccepted(string member)
    {
        var document = StatementOperationTestFixture.Document(Prelude + member + "}");
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task ReviewedServiceReceiverChainRemainsAccepted()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            interface ISlugProvider { Task<string> SlugMap(CancellationToken cancellationToken); }
            class Services { public T GetRequiredService<T>() => default!; }
            class Context { public Services Services { get; } = new(); public CancellationToken CancellationToken { get; } }
            class C
            {
                static async Task<string> M(Context c)
                {
                    var slugLookup = await c.Services.GetRequiredService<ISlugProvider>()
                        .SlugMap(cancellationToken: c.CancellationToken);
                    return slugLookup;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task SharedPackagedFindingsHaveExactLocationsAndRuntimeSafeActions()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Computations", "Findings.cs"));
        var document = StatementOperationTestFixture.Document(source);
        var before = await StatementOperationTestFixture.Run(document);
        var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
        Assert.Equal(5, diagnostics.Length);
        var conditions = await InlineConditionTestFixture.Diagnostics(document);
        Assert.Equal("CR0203", Assert.Single(conditions).Id);
        foreach (var diagnostic in diagnostics.Concat(conditions))
        {
            var position = diagnostic.Location.GetLineSpan().StartLinePosition;
            var line = (await document.GetTextAsync()).Lines[position.Line].ToString();
            var marker = line.Split("// ")[1].Split('|').Select(value => value.Trim())
                .Single(value => value.StartsWith(diagnostic.Id + ":", StringComparison.Ordinal));
            var anchor = marker[(diagnostic.Id.Length + 2)..];
            Assert.Equal(line.IndexOf(anchor, StringComparison.Ordinal), position.Character);
            var actions = diagnostic.Id == "CR0203"
                ? await InlineConditionTestFixture.Actions(document, diagnostic)
                : await StatementOperationTestFixture.Actions(document, diagnostic);
            Assert.NotEmpty(actions);
            foreach (var action in actions)
            {
                var operation = Assert.Single((await action.GetOperationsAsync(default))
                    .OfType<Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation>());
                var changed = await StatementOperationTestFixture.Reparse(operation.ChangedSolution.GetDocument(document.Id)!);
                Assert.Equal(before, await StatementOperationTestFixture.Run(changed));
            }
        }
        var fixedDocument = await ReviewedComputationRuntimeTests.FixOperations(document, StatementOperationCodeFixProvider.ExtractKey);
        fixedDocument = await InlineConditionTestFixture.Reparse(await InlineConditionTestFixture.Fix(fixedDocument,
            InlineConditionCodeFixProvider.ExtractKey));
        Assert.Equal(before, await StatementOperationTestFixture.Run(fixedDocument));
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(fixedDocument));
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(fixedDocument));
    }

    [Fact]
    public async Task SharedPackagedAcceptedCasesRemainSilent()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Computations", "Accepted.cs"));
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("flag /* retain branch comment */")]
    [InlineData("span.IsEmpty")]
    public async Task CommentAndRefLikeBoundariesRetainTheirSource(string condition)
    {
        var source = "using System; using System.Linq; class C { static int[] M(bool flag, ReadOnlySpan<int> span, int[] parts) { var path = "
            + condition + " ? new[] { 1 }.Concat(parts).ToArray() : new[] { 2 }.Concat(parts).ToArray(); return path; } }";
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("// <auto-generated/>\n", null)]
    [InlineData("", "root = true\n[*.cs]\ndotnet_diagnostic.CR0301.severity = none\n")]
    [InlineData("#pragma warning disable CR0301\n", null)]
    public async Task NewConditionalCoverageHonorsGeneratedCodeAndConfiguration(string prefix, string? config)
    {
        var document = StatementOperationTestFixture.Document(prefix
            + "using System.Linq; class C { static int[] M(bool flag, int[] parts) => flag ? new[] { 1 }.Concat(parts).ToArray() : new[] { 2 }.Concat(parts).ToArray(); }", config);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("class Provider { public object? GetService(System.Type type) => null; }")]
    [InlineData("namespace System { interface IServiceProvider { object? GetService(Type type); } } class Provider : System.IServiceProvider { public object? GetService(System.Type type) => null; }")]
    public async Task UncataloguedServiceMethodsRemainAccepted(string declarations)
    {
        var document = InlineConditionTestFixture.Document(declarations
            + " class C { static bool M(Provider provider, System.Type type) { if (provider.GetService(type) is null) return false; return true; } }");
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task SourceImitationOfTypeMetadataRemainsAccepted()
    {
        const string source = """
            namespace System { class Type { public int[] GetGenericArguments() => []; } }
            class C
            {
                static int Use(int[] values) => values.Length;
                static int M(System.Type type) => Use(type.GetGenericArguments());
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        await StatementOperationTestFixture.Compiles(document);
        Assert.Empty(await StatementOperationTestFixture.Diagnostics(document));
    }
}
