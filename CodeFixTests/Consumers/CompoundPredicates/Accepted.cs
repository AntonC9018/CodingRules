using System;
using System.Linq;

static partial class C
{
    private sealed record Lesson(bool IsLab);
    private sealed record Group(Lesson[] Lessons);

    private static bool Simple(Group[] groups) => groups.Any(group => group.Lessons.Any(lesson => lesson.IsLab));

    private static bool BlockBoundary(int[] ids) => ids.Any(a => ids.Any(b =>
    {
        return ids.Any(c => ids.Any(d => d == c));
    }));

    private static bool NamedBoundary(int[] ids) => ids.Any(a => ids.All(Matches));
    private static bool Matches(int value) => new[] { value }.Any(a => new[] { value }.All(b => b == a));

    private static bool Lookalike(int[] ids) => Fake.Any(ids, id => id > 0 && ids.Any(parent => parent == id));

    private static class Fake
    {
        public static bool Any(int[] source, Func<int, bool> predicate) => true;
    }
}
