public static class Consumer
{
    private static int Copy(int source, int destination) => source + destination;
    public static int Run() => Copy(1, 2);
}
