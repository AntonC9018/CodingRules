using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

// Pinned ScheduleLib 12e601e: exact A3/A11/A15 conditions; framework-only host doubles.
public static class C
{
    private const int Success = 0;
    private static string trace = "";

    private static int Finish<T>(int exit, string verb, T data)
    {
        if (exit == Success && verb == "profiles" && data is IReadOnlyList<string> profiles) { return profiles.Count; } // CR0201: exit == Success
        return -1;
    }

    private static string ValidateOperation(JsonObject action)
    {
        if (action["key"] is not JsonValue keyValue || !keyValue.TryGetValue<string>(out var key) || action["operation"] is not JsonValue opValue || !opValue.TryGetValue<string>(out var operation)) { throw new JsonException("Operation requires string key and operation members."); } return key + operation; // CR0203: action["key"]
    }

    private static void Apply(Exception failure, CancellationToken token)
    {
        try
        {
            try { throw failure; }
            finally { trace += "unwind;"; }
        }
        catch (Exception error) when (token.IsCancellationRequested && error is OperationCanceledException or GooglePreSendTransportException) { trace += "cancelled;"; } // CR0202: token.IsCancellationRequested
        catch (Exception) { trace += "other;"; }
    }

    private static void ClassifyGoogle(Exception error, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested && error is OperationCanceledException or GooglePreSendTransportException) { trace += "cancelled;"; return; } // CR0202: cancellation.IsCancellationRequested
        if (error is TokenResponseException || error is GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.Unauthorized }) { trace += "auth;"; } // CR0202: error is TokenResponseException
    }

    private static void Reject(int status)
    {
        try
        {
            try { throw new GoogleApiException(status); }
            finally { trace += "unwind;"; }
        }
        catch (GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 403 or 404 or 409 or 412 or 422) { trace += "rejected;"; } // CR0203: (int)e.HttpStatusCode
        catch (GoogleApiException) { trace += "other;"; }
    }

    public static string Run()
    {
        trace += Finish(0, "profiles", new[] { "one", "two" });
        trace += Finish(1, "profiles", new[] { "one" });
        trace += Finish(0, "other", new[] { "one" });
        trace += Finish(0, "profiles", new object());
        foreach (var json in new[] { "{}", "{\"key\":1}", "{\"key\":\"k\"}", "{\"key\":\"k\",\"operation\":true}", "{\"key\":\"k\",\"operation\":\"remove\"}" })
        {
            try { trace += ValidateOperation((JsonObject)JsonNode.Parse(json)!); }
            catch (JsonException) { trace += "invalid;"; }
        }
        foreach (var cancelled in new[] { false, true })
        foreach (var error in new Exception[] { new OperationCanceledException(), new GooglePreSendTransportException(), new Exception(), new TokenResponseException(), new GoogleApiException(401), new GoogleApiException(403) })
        {
            Apply(error, new CancellationToken(cancelled));
            ClassifyGoogle(error, new CancellationToken(cancelled));
        }
        for (var code = -1; code <= 600; code++) Reject(code);
        return trace;
    }

    public static void ReadStatus() { trace += "read;"; }
}

internal sealed class GooglePreSendTransportException : Exception { }
internal sealed class TokenResponseException : Exception { }
internal sealed class GoogleApiException : Exception
{
    private readonly int status;
    public GoogleApiException(int status) { this.status = status; }
    public HttpStatusCode HttpStatusCode
    {
        get
        {
            C.ReadStatus();
            if (status < 0) throw new InvalidOperationException();
            return (HttpStatusCode)status;
        }
    }
}
