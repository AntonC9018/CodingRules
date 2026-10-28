using System;
using System.Runtime.CompilerServices;

public static class ChoppedArgumentObservers
{
    [ModuleInitializer]
    internal static void Validate() => Run();

    public static void Run()
    {
        _ = Use(7,
            8);
        _ = new ChoppedArgumentValue(7,
            8);

        string expression = Capture(Use(7, 8));
        if (expression != "Use(7, 8)")
            throw new InvalidOperationException($"Unexpected caller expression: {expression}");

        int callerLine = Line();
        if (callerLine != 20)
            throw new InvalidOperationException($"Unexpected caller line: {callerLine}");
    }

    private static int Use(int first, int second) => first + second;
    private static int Line([CallerLineNumber] int line = 0) => line;
    private static string Capture(int value,
        [CallerArgumentExpression("value")] string expression = "") => expression;
}
