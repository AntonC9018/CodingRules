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
