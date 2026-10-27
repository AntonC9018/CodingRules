using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace CodingRules;

public sealed class ReviewedComputationRuntimeTests
{
    [Fact]
    public async Task ReflectionKeepsGuardedLookupOrderAndNamesTheServiceResult()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Reflection;
            public class C
            {
                static string events = "";
                interface IKeyEqualityComparer<T> { }
                sealed class TrackingType(Type type) : TypeDelegator(type)
                {
                    public override Type[] GetGenericArguments() { events += "type;"; return typeImpl.GetGenericArguments(); }
                }
                sealed class Provider(int mode) : IServiceProvider
                {
                    public object? GetService(Type serviceType)
                    {
                        events += "service;";
                        if (mode == 2) throw new InvalidOperationException();
                        return mode == 0 ? null : new object();
                    }
                }
                static bool Check(IServiceProvider provider, Type value, int length)
                {
                    if (length != 0 && provider.GetService(typeof(IKeyEqualityComparer<>).MakeGenericType(value.GetGenericArguments()[0])) is null)
                        return false;
                    return true;
                }
                public static string Run()
                {
                    events = "";
                    var generic = new TrackingType(typeof(List<int>));
                    var scalar = new TrackingType(typeof(int));
                    var absent = new Provider(0);
                    var present = new Provider(1);
                    var failing = new Provider(2);
                    var skipped = Check(absent, scalar, 0);
                    events += skipped + ";";
                    var missing = Check(absent, generic, 1);
                    events += missing + ";";
                    var found = Check(present, generic, 1);
                    events += found + ";";
                    try { Check(absent, scalar, 1); } catch (IndexOutOfRangeException) { events += "index;"; }
                    try { Check(failing, generic, 1); } catch (InvalidOperationException) { events += "failure;"; }
                    return events;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        const string expected = "True;type;service;False;type;service;True;type;index;type;service;failure;";
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        document = await FixOperations(document, StatementOperationCodeFixProvider.ExtractKey);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        Assert.Equal("CR0203", Assert.Single(await InlineConditionTestFixture.Diagnostics(document)).Id);
        foreach (var key in new[] { InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey })
        {
            var changed = await InlineConditionTestFixture.Reparse(await InlineConditionTestFixture.Fix(document, key));
            Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
            // The lookup is a named producer before the null test, still reached
            // only in the original short-circuited branch.
            var text = (await changed.GetTextAsync()).ToString();
            Assert.Contains(" = provider.GetService(", text);
        }
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task ConditionalPipelinesKeepLazyEnumerationAndExceptions(string key)
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public class C
            {
                static string events = "";
                static IEnumerable<string> Parts(bool fail)
                {
                    events += "enumerate;";
                    if (fail) throw new InvalidOperationException();
                    yield return "key";
                }
                static string[] Build(string? profile, bool fail)
                {
                    var parts = Parts(fail);
                    var path = profile is null
                        ? new[] { "defaults" }.Concat(parts).ToArray()
                        : new[] { "profiles", profile }.Concat(parts).ToArray();
                    return path;
                }
                public static string Run()
                {
                    events = "";
                    var defaults = Build(null, false);
                    var profile = Build("user", false);
                    try { Build(null, true); } catch (InvalidOperationException) { events += "failure;"; }
                    return events + string.Join("/", defaults) + ":" + string.Join("/", profile);
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        const string expected = "enumerate;enumerate;enumerate;failure;defaults/key:profiles/user/key";
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var changed = await FixOperations(document, key);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
    }

    [Theory]
    [InlineData(false, true, "ctor;append;")]
    [InlineData(true, false, "ctor;literal;append;")]
    [InlineData(true, true, "ctor;literal;get;use;format;append;")]
    public async Task InnerExtractionKeepsHandlerGating(bool enabled, bool appendEnabled, string expected)
    {
        var source = """
            using System;
            using System.Runtime.CompilerServices;
            public class C
            {
                public static bool Enabled;
                public static bool AppendEnabled;
                public static string Events = "";
                static int Get(int value) { Events += "get;"; return value; }
                static int Use(int value) { Events += "use;"; return value; }
                class ListStringBuilder { public void Append(Handler handler) { Events += "append;"; } }
                [InterpolatedStringHandler]
                public ref struct Handler
                {
                    public Handler(int literalLength, int formattedCount, out bool enabled) { Events += "ctor;"; enabled = Enabled; }
                    public bool AppendLiteral(string text) { Events += "literal;"; return AppendEnabled; }
                    public bool AppendFormatted(int value) { Events += "format;"; return true; }
                }
                public static string Run()
                {
                    Events = "";
                    Enabled = ENABLED;
                    AppendEnabled = APPEND_ENABLED;
                    var list = new ListStringBuilder();
                    list.Append($"value:{Use(Get(1))}");
                    return Events;
                }
            }
            """;
        source = source.Replace("APPEND_ENABLED", appendEnabled ? "true" : "false")
            .Replace("ENABLED", enabled ? "true" : "false");
        var document = StatementOperationTestFixture.Document(source);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var diagnostic = Assert.Single(await StatementOperationTestFixture.Diagnostics(document));
        var actions = await StatementOperationTestFixture.Actions(document, diagnostic);
        Assert.Equal(StatementOperationCodeFixProvider.ExtractKey, Assert.Single(actions).EquivalenceKey);
        var changed = await FixOperations(document, StatementOperationCodeFixProvider.ExtractKey);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
    }

    [Theory]
    [InlineData(StatementOperationCodeFixProvider.ExtractKey)]
    [InlineData(StatementOperationCodeFixProvider.ExpandKey)]
    public async Task NestedLookupKeepsReceiverConstructionOrderAndJsonFailure(string key)
    {
        const string source = """
            using System;
            using System.Text.Json.Nodes;
            public class C
            {
                static string events = "";
                static bool invalid;
                sealed class Key
                {
                    public string Value { get; }
                    public Key(string value) { events += "key;"; Value = value; }
                }
                sealed class NodeDataKey<T>
                {
                    public Key Key { get; }
                    public NodeDataKey(Key value) { events += "data;"; Key = value; }
                }
                sealed class Node
                {
                    public Key GetOrAdd<T>(NodeDataKey<T> key) { events += "lookup;"; return key.Key; }
                }
                static Node GetNode() { events += "receiver;"; return new Node(); }
                static JsonObject GetAction()
                {
                    events += "action;";
                    if (invalid) return new JsonObject { ["key"] = 1 };
                    return new JsonObject { ["key"] = "section.value" };
                }
                static Key Lookup() => GetNode().GetOrAdd(new NodeDataKey<object>(new(GetAction()["key"]!.GetValue<string>().Split('.')[0])));
                public static string Run()
                {
                    events = "";
                    invalid = false;
                    var key = Lookup();
                    events += key.Value + ";";
                    invalid = true;
                    try { Lookup(); } catch (InvalidOperationException) { events += "failure;"; }
                    return events;
                }
            }
            """;
        var document = StatementOperationTestFixture.Document(source);
        const string expected = "receiver;action;key;data;lookup;section;receiver;action;failure;";
        Assert.Equal(expected, await StatementOperationTestFixture.Run(document));
        var changed = await FixOperations(document, key);
        Assert.Equal(expected, await StatementOperationTestFixture.Run(changed));
    }

    internal static async Task<Document> FixOperations(Document document, string key)
    {
        for (var pass = 0; pass < 12; pass++)
        {
            var diagnostics = await StatementOperationTestFixture.Diagnostics(document);
            if (diagnostics.Length == 0) return document;
            var actions = await StatementOperationTestFixture.Actions(document, diagnostics[0]);
            Assert.NotEmpty(actions);
            var selected = actions.FirstOrDefault(action => action.EquivalenceKey == key) ?? actions[0];
            document = await StatementOperationTestFixture.Reparse(await StatementOperationTestFixture.Fix(document, selected.EquivalenceKey!));
            await StatementOperationTestFixture.Compiles(document);
        }
        Assert.Fail("Operation fixes did not converge in twelve passes.");
        return document;
    }
}
