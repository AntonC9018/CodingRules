public static class Consumer
{
    public static bool Check(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        return value.Contains('x');
    }
}
