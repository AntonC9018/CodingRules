using System;
using System.Globalization;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

public static class C
{
    private static string identifier = "";

    // Complete A10 call at ScheduleLib 12e601e, CalendarSync.cs:232-247.
    public static void Desired(int index, Event ev)
    {
        Report(
            "desired-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "completed",
            new
            {
                index,
                summary = ev.Summary,
                start = ev.Start?.DateTimeDateTimeOffset?.ToString(
                    "o",
                    System.Globalization.CultureInfo.InvariantCulture),
                end = ev.End?.DateTimeDateTimeOffset?.ToString(
                    "o",
                    System.Globalization.CultureInfo.InvariantCulture),
                recurrence = ev.Recurrence,
            },
            $"Create event [{index}]: {ev.Summary}; {ev.Start?.DateTimeDateTimeOffset}–{ev.End?.DateTimeDateTimeOffset}; {string.Join(", ", ev.Recurrence ?? [])}");
    }

    // Complete A10 acquisition at the same pin, CliSettings.Writes.cs:106-109.
    public static async Task<string> Lock(string? file, CancellationToken cancellationToken)
    {
        await using var lease = await LocalFileLock.Acquire(
            file + ".lock",
            cancellationToken,
            wait: true);
        return lease.Path;
    }

    private static void Report(string machine, string status, object data, string human) { identifier = machine; }
    private static void Report(FormattableString machine, string status, object data, string human) { identifier = "wrong overload"; }

    // Accepted constant, numeric, object, dynamic and user-defined additions.
    public const string Constant = "a" + "b";
    public static int Numeric(int a, int b) => a + b;
    public static string Mixed(int a, int b) => "sum=" + (a + b);
    public static string ObjectValue(object value) => value + ".lock";
    public static dynamic DynamicValue(dynamic value) => value + ".lock";
    public static string Overloaded(Text value) => value + ".lock";
    public static string Existing(string file) => $"{file}.lock";
    public static Expression<Func<string, string>> Tree = file => file + ".lock";

    public static string Run()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            var culture = (CultureInfo)new CultureInfo("fr-FR").Clone();
            culture.NumberFormat.NegativeSign = "~";
            CultureInfo.CurrentCulture = culture;
            Desired(-123, new Event());
            return string.Join("|", identifier, Lock(null, default).GetAwaiter().GetResult(),
                Lock("calendar", default).GetAwaiter().GetResult(), (-123).ToString());
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}

public sealed class Event
{
    public string? Summary { get; set; }
    public EventDate? Start { get; set; }
    public EventDate? End { get; set; }
    public string[]? Recurrence { get; set; }
}
public sealed class EventDate { public DateTimeOffset? DateTimeDateTimeOffset { get; set; } }
public readonly struct Text { public static string operator +(Text value, string suffix) => suffix; }
public static class LocalFileLock
{
    public static ValueTask<Lease> Acquire(string path, CancellationToken cancellationToken, bool wait) => new(new Lease(path));
    public static ValueTask<Lease> Acquire(object path, CancellationToken cancellationToken, bool wait) => new(new Lease("wrong overload"));
}
public sealed class Lease(string path) : IAsyncDisposable
{
    public string Path { get; } = path;
    public ValueTask DisposeAsync() => default;
}
