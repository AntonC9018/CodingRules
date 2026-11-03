using System;
using System.IO;
using System.Threading;

namespace OverloadConfiguration;

public static class Parser
{
    public static void Parse(string input) { }
    public static void Parse(ReadOnlySpan<char> input) { }
}

// Configuration alternatives each take one parameter; required steps remain
// a manual review and runtime validation concern, not a RequiredStep analyzer.
public sealed class ConfigurationBuilder
{
    private object? input;
    private object? output;
    public void Input(object value) { input = value; }
    public void Output(Stream value) { output = value; }
    public void Output(string path) { output = path; }
    public void Execute(CancellationToken cancellation)
    {
        if (input is null) throw new InvalidOperationException("Input is required");
        if (output is null) throw new InvalidOperationException("Output is required");
    }
}

public class SeparateShapes
{
    // Different parameter counts are outside initial coverage.
    public void Arity(object input, Stream output) { }
    public void Arity(object input, string path, CancellationToken cancellation) { }

    // Two type differences are outside initial coverage.
    public void Multiple(int input, Stream output) { }
    public void Multiple(long input, string path) { }

    public static void Staticness(object input, Stream output) { }
    public void Staticness(object input, string path) { }

    public void Generic<T>(object input, Stream output) { }
    public void Generic<T, U>(object input, string path) { }

    // Parameter shape must match even when its type is the alternative slot.
    public void Reference(object input, ref Stream output) { }
    public void Reference(object input, string path) { }
    public void Defaults(object input, Stream? output = null) { }
    public void Defaults(object input, string path) { }
    public void Variable(object input, params Stream[] output) { }
    public void Variable(object input, string[] paths) { }
}

// Similar names on unrelated or base/derived types do not form a family.
public class First { public void Save(object input, Stream output) { } }
public class Second { public void Save(object input, string path) { } }
public class Derived : First { public void Save(object input, string path) { } }

public static class Calls
{
    public static void Use(dynamic target)
    {
        target.Save(new object(), "out");
        Console.WriteLine("{0}", 1);
        Console.WriteLine("{0}", new object());
    }
}
