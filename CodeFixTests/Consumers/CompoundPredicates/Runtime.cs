using System.Collections.Generic;

static partial class C
{
    private static readonly List<string> Trace = new();

    private static bool IsLab(int id)
    {
        Trace.Add("lab" + id);
        return id > 0;
    }

    public static string Run()
    {
        Trace.Clear();
        Trace.Add(Compound(new[] { 0, 1, 2 }, new[] { 2 }).ToString());
        Trace.Add(Deep(new[] { 0, 1 }, new[] { 0, 1 }).ToString());
        Trace.Add(Deep(new[] { 0 }, new int[0]).ToString());
        Trace.Add(Compound(new int[0], new[] { 0 }).ToString());
        var groups = new[] { new Group(new[] { new Lesson(true) }) };
        Trace.Add(Simple(groups).ToString());
        Trace.Add(BlockBoundary(new[] { 0 }).ToString());
        Trace.Add(NamedBoundary(new[] { 0 }).ToString());
        Trace.Add(Lookalike(new[] { 0 }).ToString());
        return string.Join("|", Trace);
    }
}
