using System;
using System.Collections.Generic;
using System.Threading;

namespace ContractParameters;

// Q12/Q17 refinement: a trailing cancellation token is execution control, not configuration.
public interface IImport
{
    void Execute(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation);
    void Default(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
}

public interface IFactory<T> where T : IFactory<T>
{
    static abstract T Create(int first, int second, int third, int fourth); // CR1000: Create
}

public abstract class ImportBase
{
    protected ImportBase(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation) { }
    public virtual void Visit(int first, int second, int third, int fourth) { } // CR1000: Visit
    public abstract void Merge(string lesson, DayOfWeek day, TimeSpan time, IReadOnlyList<int> groups, string? alternative); // CR1000: Merge
}

public abstract class PrimaryImport(string lesson, DayOfWeek day, TimeSpan time, CancellationToken cancellation)
{
    public string Lesson { get; } = lesson;
    public DayOfWeek Day { get; } = day;
    public TimeSpan Time { get; } = time;
    public CancellationToken Cancellation { get; } = cancellation;
}

public abstract record ImportRecord(string Lesson, DayOfWeek Day, TimeSpan Time, CancellationToken Cancellation);

// Q20: the PDF/Word delegate is an owned contract; its handler gets no duplicate.
public delegate void ImportedLessonHandler(string lesson, DayOfWeek day, TimeSpan time, IReadOnlyList<int> groups, string? alternative); // CR1000: ImportedLessonHandler
