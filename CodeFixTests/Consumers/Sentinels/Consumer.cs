public static class Consumer
{
    private static int Search(string text) => text.IndexOf('x');
    private static int Forward(string text) => Search(text);
    public static bool Run(string text) => Forward(text) >= 0;
}
