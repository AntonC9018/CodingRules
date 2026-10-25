public static class Accepted
{
    public static bool Initialized(bool flag)
    {
        bool value = false;
        if (flag) { value = true; }
        return value;
    }

    public static bool Partial(bool flag)
    {
        bool value;
        if (flag) { value = true; } else { return false; }
        return value;
    }
}
