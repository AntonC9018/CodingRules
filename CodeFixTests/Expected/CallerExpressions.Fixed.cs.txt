using System;
using System.Runtime.CompilerServices;

#pragma warning disable CR0300, CR0302
public static class CallerExpressionCases
{
    public static void Run()
    {
        Console.WriteLine(Capture(new CallerPayload(line: 0) { A = 1, B = 2 }));
    }

    private static string Capture(CallerPayload value, [CallerArgumentExpression("value")] string expression = "") { return expression; }
}
