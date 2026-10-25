public static class Findings
{
    public static bool First(bool flag)
    {
        bool value; // CR0303: value
        if (flag) { value = true; } else { value = false; }
        return value;
    }

    public static bool Nested(bool first, bool second)
    {
        bool result; // CR0303: result
        if (first) { result = true; }
        else { if (second) { result = false; } else { result = first; } }
        return result;
    }
}
