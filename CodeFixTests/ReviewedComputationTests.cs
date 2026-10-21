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
        { "static void M(Exception ex) { throw StreamingFailure(new CliOutputStreamingException(\"Calendar output streaming failed.\", ex)); }", "CR0300" },
        { "static Exception M(string[] outcomes, Exception inner) => new CalendarStreamingException(new Result(outcomes.Any(x => x == \"completed\") ? 6 : 1, [\"Output streaming failed.\"], false), inner);", "CR0301" },
        { "static string M(string? selected) { var path = selected is { } cache ? ResolvePath(cache) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), \"ScheduleLib\", \"cache\"); return path; }", "CR0300" },
        { "static object M<T>(Node node, JsonObject action) => node.GetOrAdd(new NodeDataKey<T>(new(action[\"key\"]!.GetValue<string>().Split('.')[0])));", "CR0300" },
        { "static string[] M(string? profile, string[] parts) { var path = profile is null ? new[] { \"defaults\" }.Concat(parts).ToArray() : new[] { \"profiles\", profile }.Concat(parts).ToArray(); return path; }", "CR0301" },
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
}
