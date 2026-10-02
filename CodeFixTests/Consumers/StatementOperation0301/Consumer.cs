public static class Consumer
{
    private static int Use(int value) => value;
    public static int Compute(bool flag)
    {
        return Use(flag ? 1 : 2);
    }
}
