using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public static class Accepted
{
    private sealed class Options
    {
        public int[] WeeklyLessons { get; set; } = [];
        public int[] Groups { get; set; } = [];
        public TimeSpan LessonDuration { get; set; }
        public int[] TimeSlotStarts { get; set; } = [];
    }
    private static int[] CreateDefaultTimeSlots() => [];
    private static Options Defaults() => new()
    {
        WeeklyLessons = [], Groups = [], LessonDuration = TimeSpan.FromMinutes(90), TimeSlotStarts = CreateDefaultTimeSlots(),
    };
    private interface ISlugProvider { Task<string> SlugMap(CancellationToken cancellationToken); }
    private sealed class Services { public T GetRequiredService<T>() => default!; }
    private sealed class Context { public Services Services { get; } = new(); public CancellationToken CancellationToken { get; } }
    private static async Task<string> Slugs(Context c)
    {
        var slugLookup = await c.Services.GetRequiredService<ISlugProvider>()
            .SlugMap(cancellationToken: c.CancellationToken);
        return slugLookup;
    }
    private static string[] LinearPipeline(string[] parts) => new[] { "defaults" }.Concat(parts).ToArray();
    private static int SimpleDecision(bool flag) => flag ? 6 : 1;
    private static string MultilineLiteral() => """
        accepted multiline
        literal
        """;
    private sealed record Token(int Type, ReadOnlyMemory<char> Value);
    private static class TokenTypeLabels { public static string Get(int type) => "token"; }
    private sealed class ListStringBuilder { public void Append(Handler handler) { } }
    [InterpolatedStringHandler]
    public ref struct Handler
    {
        public Handler(int literalLength, int formattedCount) { }
        public void AppendLiteral(string value) { }
        public void AppendFormatted(string value) { }
        public void AppendFormatted(ReadOnlySpan<char> value) { }
    }
    private static void Lexer(ListStringBuilder list, Token t)
    {
        var type = TokenTypeLabels.Get(t.Type);
        list.Append($"{type} - {t.Value.Span}");
    }
}
