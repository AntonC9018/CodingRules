public static class Consumer
{
    private static int Use(int value) => value;
    public static int Compute(int value)
    {
        return Use(value++);
    }
}
