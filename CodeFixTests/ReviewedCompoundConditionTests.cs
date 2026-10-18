using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace CodingRules;

public sealed class ReviewedCompoundConditionTests
{
    private const string Declarations = """
        using System;
        using System.Collections.Generic;
        using System.Net;
        using System.Text.Json;
        using System.Text.Json.Nodes;
        using System.Threading;
        class GooglePreSendTransportException : Exception { }
        class TokenResponseException : Exception { }
        class GoogleApiException : Exception { public HttpStatusCode HttpStatusCode { get; set; } }
        class C
        {
        const int Success = 0;
        """;

    public static string Config => Declarations + """
        int Finish<T>(int exit, string verb, T data)
        {
            if (exit == Success && verb == "profiles" && data is IReadOnlyList<string> profiles)
            {
                return profiles.Count;
            }
            return -1;
        }
        }
        """;

    public static string Json => Declarations + """
        string ValidateOperation(JsonObject action)
        {
            if (action["key"] is not JsonValue keyValue ||
                !keyValue.TryGetValue<string>(out var key) ||
                action["operation"] is not JsonValue opValue ||
                !opValue.TryGetValue<string>(out var operation))
            {
                throw new JsonException("Operation requires string key and operation members.");
            }
            return key + operation;
        }
        }
        """;

    public static string Cancellation => Declarations + """
        void Apply(CancellationToken token)
        {
            try { throw new Exception(); }
            catch (Exception error) when (token.IsCancellationRequested &&
                error is OperationCanceledException or GooglePreSendTransportException) { }
        }
        }
        """;

    public static string Authentication => Declarations + """
        void ClassifyGoogle(Exception error)
        {
            if (error is TokenResponseException ||
                error is GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.Unauthorized }) { }
        }
        }
        """;

    public static string Status => Declarations + """
        void Apply()
        {
            try { throw new GoogleApiException(); }
            catch (GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 403 or 404 or 409 or 412 or 422) { }
        }
        }
        """;

    [Theory]
    [InlineData("config", "CR0201")]
    [InlineData("json", "CR0203")]
    [InlineData("cancellation", "CR0202")]
    [InlineData("authentication", "CR0202")]
    [InlineData("status", "CR0203")]
    public async Task ExistingFamiliesFixPinnedExamples(string example, string? expected)
    {
        var source = example switch
        {
            "config" => Config,
            "json" => Json,
            "cancellation" => Cancellation,
            "authentication" => Authentication,
            _ => Status,
        };
        var document = InlineConditionTestFixture.Document(source);
        await InlineConditionTestFixture.Compiles(document);
        var diagnostics = await InlineConditionTestFixture.Diagnostics(document);
        Assert.Equal(expected is null ? System.Array.Empty<string>() : new[] { expected }, diagnostics.Select(d => d.Id));
        foreach (var diagnostic in diagnostics)
        {
            var actions = await InlineConditionTestFixture.Actions(document, diagnostic);
            Assert.NotEmpty(actions);
            foreach (var action in actions)
            {
                var changed = await InlineConditionTestFixture.Fix(document, action.EquivalenceKey!);
                await InlineConditionTestFixture.Compiles(await InlineConditionTestFixture.Reparse(changed));
                Assert.Empty(await InlineConditionTestFixture.Diagnostics(changed));
            }
        }
    }
}
