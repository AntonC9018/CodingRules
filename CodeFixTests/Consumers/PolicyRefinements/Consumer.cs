using System.Linq;
using System.Collections;
using System.Collections.Generic;

[assembly: CodingRules.AllowExternalPositionalArguments(assemblyName: "External", declarationId: "M:External.Api.Compare(System.Int32,System.Int32)~System.Boolean")]

public class WidgetBuilder
{
    public void Configure(int input, int value) { }
    public void Configure(int input, string value) { }
}
public class WidgetDescriptor
{
    public void Configure(int input, int value) { }
    public void Configure(int input, string value) { }
}
public class WidgetConfigurator
{
    public void Configure(int input, int value) { }
    public void Configure(int input, string value) { }
}
public class WidgetSetup
{
    public void Configure(int input, int value) { }
    public void Configure(int input, string value) { }
}
[CodingRules.ConfigurationBuilder]
public class Marked
{
    public void Configure(int input, int value) { }
    public void Configure(int input, string value) { }
}
public class Context { }
public static class Extensions
{
    extension (WidgetBuilder builder)
    {
        public void Apply(int input, int value) { }
        public void Apply(int input, string value) { }
    }
    extension (Context context)
    {
        public void Single(int value) { }
        public void Single(string value) { }
    }
}
public static class Consumer
{
    public static int Run(long pid)
    {
        External.Api.Compare(1, 2);
        External.Api.Same(1, 2);
        if (pid > 0)
            return (int)pid;
        return 0;
    }
}

public sealed record Frame(int State);
public static class NewLayoutCases
{
    public static int[] Chain(int[] values) => values.Where(value => value > 0).Select(value => value).ToArray();
    public static bool Branch(bool value)
    {
        if (value)
        {
            return true;
        }
        else
        {
            return false;
        }
    }
    public static int[] Short(int[] xs) => xs.Skip(1).ToArray();
    public static Frame Update(Frame frame) => frame with { State = 1 };
}
