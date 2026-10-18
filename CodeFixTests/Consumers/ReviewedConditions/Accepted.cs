using System;
using System.Net;

internal static class Accepted
{
    public static bool Range(int value) => value >= 0 && value <= 100;
    public static bool Outside(int value) => value is < 0 or > 100;
    public static bool Text(string? value) => value is not null && value.Length > 0;
    public static bool Types(Exception error) => error is OperationCanceledException || error is ArgumentException;
    public static bool Named(HttpStatusCode status) => status is HttpStatusCode.BadRequest
        or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict
        or HttpStatusCode.PreconditionFailed or HttpStatusCode.UnprocessableEntity;

    // No status policy was accepted for this different set. Keep it for agent review.
    public static void Unreviewed()
    {
        try { throw new GoogleApiException(429); }
        catch (GoogleApiException e) when ((int)e.HttpStatusCode is 408 or 429 or 503) { }
    }
}
