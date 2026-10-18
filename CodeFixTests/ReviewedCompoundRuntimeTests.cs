using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedCompoundRuntimeTests
{
    [Fact]
    public async Task ConfigPatternKeepsProfilesScopeAndBranchOutcomes()
    {
        var source = ReviewedCompoundConditionTests.Config.Replace("class C", "public class C")
            .Replace("const int Success = 0;", """
                const int Success = 0;
                public static string Run()
                {
                    var c = new C();
                    return c.Finish(0, "profiles", new[] { "one", "two" }) + ":"
                        + c.Finish(1, "profiles", new[] { "one" }) + ":"
                        + c.Finish(0, "other", new[] { "one" }) + ":"
                        + c.Finish(0, "profiles", new object()) + ":"
                        + c.Finish<object?>(0, "profiles", null);
                }
                """);
        await VerifyEveryAction(source, "CR0201", InlineConditionCodeFixProvider.ExpandKey);
    }

    [Fact]
    public async Task JsonFailureGuardsKeepOutAssignmentsAndFailureTiming()
    {
        var source = ReviewedCompoundConditionTests.Json.Replace("class C", "public class C")
            .Replace("const int Success = 0;", """
                const int Success = 0;
                static int failures;
                static JsonException Failure() { failures++; return new JsonException("invalid"); }
                public static string Run()
                {
                    string result = "";
                    foreach (var json in new[] { "{}", "{\"key\":1}", "{\"key\":\"k\"}",
                        "{\"key\":\"k\",\"operation\":true}", "{\"key\":\"k\",\"operation\":\"remove\"}" })
                    {
                        try { result += new C().ValidateOperation((JsonObject)JsonNode.Parse(json)!); }
                        catch (JsonException e) { result += e.Message; }
                    }
                    return result + failures;
                }
                """).Replace("new JsonException(\"Operation requires string key and operation members.\")", "Failure()");
        var fixedText = await VerifyEveryAction(source, "CR0203", InlineConditionCodeFixProvider.ExpandKey);
        Assert.Contains("out var key", fixedText);
        Assert.Contains("out var operation", fixedText);
        Assert.DoesNotContain("||", fixedText);
    }

    [Fact]
    public async Task CancellationFilterRunsBeforeUnwindAndStillSelectsSameCatch()
    {
        var source = """
            using System;
            using System.Threading;
            class GooglePreSendTransportException : Exception { }
            public class C
            {
                static string trace = "";
                static void Apply(Exception failure, CancellationToken token)
                {
                    try
                    {
                        try { throw failure; }
                        finally { trace += "unwind;"; }
                    }
                    catch (Exception error) when (token.IsCancellationRequested &&
                        error is OperationCanceledException or GooglePreSendTransportException)
                    { trace += "cancelled;"; }
                    catch (Exception) { trace += "other;"; }
                }
                public static string Run()
                {
                    foreach (var cancelled in new[] { false, true })
                    foreach (var error in new Exception[] { new OperationCanceledException(), new GooglePreSendTransportException(), new Exception() })
                        Apply(error, new CancellationToken(cancelled));
                    return trace;
                }
            }
            """;
        var fixedText = await VerifyEveryAction(source, "CR0202", InlineConditionCodeFixProvider.ExtractKey);
        Assert.Contains("when (CheckCondition(error))", fixedText);
    }

    [Fact]
    public async Task AuthenticationPropertyGetterIsReachedOnlyForMatchingType()
    {
        var source = """
            using System;
            using System.Net;
            class TokenResponseException : Exception { }
            class GoogleApiException : Exception
            {
                readonly int value;
                public GoogleApiException(int value) { this.value = value; }
                public HttpStatusCode HttpStatusCode
                {
                    get { C.Trace += "read;"; if (value < 0) throw new InvalidOperationException(); return (HttpStatusCode)value; }
                }
            }
            public class C
            {
                public static string Trace = "";
                static void ClassifyGoogle(Exception error)
                {
                    if (error is TokenResponseException ||
                        error is GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.Unauthorized })
                        Trace += "auth;";
                }
                public static string Run()
                {
                    foreach (var error in new Exception[] { new TokenResponseException(), new Exception(),
                        new GoogleApiException(401), new GoogleApiException(403), new GoogleApiException(-1) })
                    {
                        try { ClassifyGoogle(error); }
                        catch (InvalidOperationException) { Trace += "thrown;"; }
                    }
                    return Trace;
                }
            }
            """;
        await VerifyEveryAction(source, "CR0202", InlineConditionCodeFixProvider.ExtractKey, InlineConditionCodeFixProvider.ExpandKey);
    }

    [Theory]
    [InlineData("400 or 403 or 404 or 409 or 412 or 422")]
    [InlineData("(422 or 412) or (409 or 404) or (403 or 400)")]
    public async Task StatusPredicatePreservesEveryStatusGetterExceptionAndFilterTiming(string numericPattern)
    {
        var source = """
            using System;
            using System.Net;
            class GoogleApiException : Exception
            {
                readonly int value;
                public GoogleApiException(int value) { this.value = value; }
                public HttpStatusCode HttpStatusCode
                {
                    get { C.Trace += "read;"; if (value < 0) throw new InvalidOperationException(); return (HttpStatusCode)value; }
                }
            }
            public class C
            {
                public static string Trace = "";
                static void Apply(int code)
                {
                    bool IsRejectedHttpStatusCode() => false;
                    _ = IsRejectedHttpStatusCode();
                    try
                    {
                        try { throw new GoogleApiException(code); }
                        finally { Trace += "unwind;"; }
                    }
                    catch (GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 403 or 404 or 409 or 412 or 422)
                    { Trace += "rejected;"; }
                    catch (GoogleApiException) { Trace += "other;"; }
                }
                public static string Run()
                {
                    for (int code = -1; code <= 600; code++) Apply(code);
                    return Trace;
                }
            }
            """;
        source = source.Replace("400 or 403 or 404 or 409 or 412 or 422", numericPattern);
        var fixedText = await VerifyEveryAction(source, "CR0203", InlineConditionCodeFixProvider.ExtractKey);
        Assert.Contains("when (IsRejectedHttpStatusCode2(e))", fixedText);
        Assert.DoesNotContain("(int)e.HttpStatusCode is", fixedText);
        foreach (var member in new[] { "BadRequest", "Forbidden", "NotFound", "Conflict", "PreconditionFailed", "UnprocessableEntity" })
            Assert.Contains("global::System.Net.HttpStatusCode." + member, fixedText);
    }

    private static async Task<string> VerifyEveryAction(string source, string id, params string[] expectedKeys)
    {
        var document = InlineConditionTestFixture.Document(source);
        var before = await InlineConditionTestFixture.Run(document);
        var diagnostic = Assert.Single(await InlineConditionTestFixture.Diagnostics(document));
        Assert.Equal(id, diagnostic.Id);
        var actions = await InlineConditionTestFixture.Actions(document, diagnostic);
        Assert.Equal(expectedKeys.OrderBy(key => key), actions.Select(action => action.EquivalenceKey).OrderBy(key => key));
        string text = "";
        foreach (var action in actions)
        {
            var changed = await InlineConditionTestFixture.Fix(document, action.EquivalenceKey!);
            var saved = await InlineConditionTestFixture.Reparse(changed);
            Assert.Equal(before, await InlineConditionTestFixture.Run(saved));
            Assert.Empty(await InlineConditionTestFixture.Diagnostics(saved));
            text = (await saved.GetTextAsync()).ToString();
        }
        return text;
    }
}
