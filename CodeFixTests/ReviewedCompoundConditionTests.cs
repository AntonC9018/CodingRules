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
    [Theory]
    [InlineData("error is TokenResponseException || error is GooglePreSendTransportException")]
    [InlineData("code is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.UnprocessableEntity")]
    [InlineData("value >= 0 && value <= 100")]
    [InlineData("value is < 0 or > 100")]
    [InlineData("text is not null && text.Length > 0")]
    [InlineData("error is GoogleApiException { HttpStatusCode: HttpStatusCode.Unauthorized }")]
    public async Task AcceptedSameValueInvariantsAndNamedPatternsStayAllowed(string condition)
    {
        var document = InlineConditionTestFixture.Document(Declarations
            + "void Accepted(Exception error, HttpStatusCode code, int value, string? text) { if (" + condition + ") { } } }");
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("400 or 403 or 404 or 409 or 412")]
    [InlineData("400 or 403 or 404 or 409 or 412 or 422 or 429")]
    [InlineData("400 or 403 or 404 or 409 or 412 or 412")]
    [InlineData("not (400 or 403 or 404 or 409 or 412 or 422)")]
    public async Task UnreviewedStatusSetsRemainForAgentReview(string pattern)
    {
        var source = Status.Replace("400 or 403 or 404 or 409 or 412 or 422", pattern);
        var document = InlineConditionTestFixture.Document(source);
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("json")]
    [InlineData("authentication")]
    public async Task GeneratedExamplesAreExcluded(string example)
    {
        var source = example switch { "json" => Json, "authentication" => Authentication, _ => Status };
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(InlineConditionTestFixture.Document("// <auto-generated/>\n" + source)));
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(InlineConditionTestFixture.Document(source).WithFilePath("/Generated.g.cs")));
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(InlineConditionTestFixture.Document(
            source.Replace("class C", "[System.CodeDom.Compiler.GeneratedCode(\"test\", \"1\")] class C"))));
    }

    [Theory]
    [InlineData("400 or /* retain policy note */ 403 or 404 or 409 or 412 or 422")]
    public async Task StatusCommentsAreNotMoved(string pattern)
    {
        var document = InlineConditionTestFixture.Document(Status.Replace("400 or 403 or 404 or 409 or 412 or 422", pattern));
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task UnresolvedOrNonFrameworkStatusTypesAreNotGuessed()
    {
        var document = InlineConditionTestFixture.Document(Status.Replace("public HttpStatusCode HttpStatusCode", "public int HttpStatusCode"));
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
        var fake = Status.Replace("class C", "enum Lookalike { Zero } class C")
            .Replace("public HttpStatusCode HttpStatusCode", "public Lookalike HttpStatusCode");
        document = InlineConditionTestFixture.Document(fake);
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task ThrowGuardWithImplicitCallerInformationRemainsManual()
    {
        var source = Json.Replace("const int Success = 0;", """
            const int Success = 0;
            static JsonException Failure([System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => new JsonException(line.ToString());
            """).Replace("new JsonException(\"Operation requires string key and operation members.\")", "Failure()");
        var document = InlineConditionTestFixture.Document(source);
        await InlineConditionTestFixture.Compiles(document);
        Assert.Empty(await InlineConditionTestFixture.Diagnostics(document));
    }

    [Fact]
    public async Task SharedPackageFixturesOfferValidatedActionsAtEveryFinding()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "ReviewedConditions", "Findings.cs"));
        var document = InlineConditionTestFixture.Document(source);
        var before = await InlineConditionTestFixture.Run(document);
        var diagnostics = await InlineConditionTestFixture.Diagnostics(document);
        Assert.Equal(6, diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            var actions = await InlineConditionTestFixture.Actions(document, diagnostic);
            Assert.True(actions.Count != 0, diagnostic.ToString());
            foreach (var action in actions)
            {
                var operation = Assert.Single((await action.GetOperationsAsync(System.Threading.CancellationToken.None))
                    .OfType<Microsoft.CodeAnalysis.CodeActions.ApplyChangesOperation>());
                var changed = operation.ChangedSolution.GetDocument(document.Id)!;
                var saved = await InlineConditionTestFixture.Reparse(changed);
                Assert.Equal(before, await InlineConditionTestFixture.Run(saved));
                Assert.Equal(diagnostics.Length - 1, (await InlineConditionTestFixture.Diagnostics(saved)).Length);
            }
        }
    }

}
