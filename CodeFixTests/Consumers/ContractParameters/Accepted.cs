using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ContractParameters;

public sealed class Import : ImportBase, IImport, IFactory<Import>
{
    public Import(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation)
        : base(lesson, day, time, cancellation) { }
    public void Execute(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
    public void Default(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
    public override void Visit(int first, int second, int third, int fourth) { }
    public override void Merge(string lesson, DayOfWeek day, TimeSpan time, IReadOnlyList<int> groups, string? alternative) { }
    public static Import Create(int first, int second, int third, int fourth) { return null!; }
}

public class ExplicitImport : IImport
{
    void IImport.Execute(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
    void IImport.Default(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
}

public class VirtualImport : IImport
{
    public virtual void Execute(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
    public virtual void Default(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
}

public abstract class ReabstractImport : ImportBase
{
    protected ReabstractImport() : base("", default, default, default) { }
    public abstract override void Visit(int first, int second, int third, int fourth);
}

// Q18: external identity, rather than the method's spelling, imposes this shape.
public class SequenceSegment : ISpanFormattable
{
    public virtual bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        return false;
    }
    public string ToString(string? format, IFormatProvider? provider) { return ""; }
}

public readonly struct ExplicitSegment : ISpanFormattable
{
    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        charsWritten = 0;
        return false;
    }
    public string ToString(string? format, IFormatProvider? provider) { return ""; }
}

// Reduced reviewed Finish/Edit signatures. Count alone leaves these static APIs allowed.
public static class CliConfigExecution
{
    public static T Finish<T>(object context, int exit, string[] errors, T data, string verb) { return data; }
}

public static class CliSettings
{
    public static void Edit(object arguments, int scope, string key, string? value, bool unset,
        CancellationToken cancellationToken, string? invocationDirectory = null, string? userFile = null,
        string? operation = null, string? item = null) { }
    public static Task Serialize(object schedule, System.IO.Stream outputFile, string hash, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class OrdinaryImport(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation)
{
    public string Lesson { get; } = lesson;
    public DayOfWeek Day { get; } = day;
    public TimeSpan Time { get; } = time;
    public CancellationToken Cancellation { get; } = cancellation;
    public void AddOrMergeLesson(string lesson, DayOfWeek day, TimeSpan time, IReadOnlyList<int> groups, string? alternative = null) { }
    public void Run()
    {
        void Handler(string lesson, DayOfWeek day, TimeSpan time, IReadOnlyList<int> groups, string? alternative) { }
        ImportedLessonHandler handler = Handler;
        handler("", default, default, [], null);
    }
}

public interface ICompact
{
    void Execute(int first, int second, CancellationToken cancellation);
    static void Helper(int first, int second, int third, int fourth) { }
    private void HelperInstance(int first, int second, int third, int fourth) { }
}

public abstract class CompactBase
{
    protected CompactBase(int first, int second, CancellationToken cancellation) { }
    public abstract void Execute(int first, int second, CancellationToken cancellation);
}

public delegate void CompactHandler(int first, int second, CancellationToken cancellation);
public readonly record struct LessonInput(string Lesson, DayOfWeek Day, TimeSpan Time, IReadOnlyList<int> Groups, string? Alternative);
public delegate void GroupedLessonHandler(LessonInput input);
