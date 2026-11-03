using System;
using System.Runtime.CompilerServices;

public static class ChoppedArgumentObservers
{
    [ModuleInitializer]
    internal static void Validate() => Run();

    public static void Run()
    {
        _ = UseNumbers(first: 7,
            second: 8);
        _ = new ChoppedArgumentValue(first: 7,
            second: 8);

        string expression = Capture(UseNumbers(first: 7, second: 8));
        if (expression != "UseNumbers(first: 7, second: 8)")
        {
            throw new InvalidOperationException("Caller expression changed during formatting.");
        }

        int callerLine = Line();
        if (callerLine != 22)
        {
            throw new InvalidOperationException("Caller line changed during formatting.");
        }
    }

    private static int UseNumbers(int first, int second) => first + second;
    private static int Line([CallerLineNumber] int line = 0) => line;
    private static string Capture(int value,
        [CallerArgumentExpression("value")] string expression = "") => expression;
}
