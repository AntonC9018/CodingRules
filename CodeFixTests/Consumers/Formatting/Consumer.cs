public class Consumer
{
    public int Value
    {
        get;
        init;
    }

    public bool And(int value)
    {
        return value >= 0 &&
            value <= 10;
    }

    public bool Or(int value)
    {
        return value == 0 ||
            value == 10;
    }

    public int Method() => System.Math.Abs(
        -1);

    public void VoidMethod() => System.Console.WriteLine(
        1);

    public int Property => System.Math.Abs(
        -2);

    public int Accessor
    {
        get => System.Math.Abs(
            -3);
        set => System.Console.WriteLine(
            value);
    }

    public int LocalFunction()
    {
        int Local() => System.Math.Abs(
            /* preserve argument */ -4);
        return Local();
    }

    public int BlockCommentMethod() => /* preserve method block */ System.Math.Abs(
        -5);

    public int LineCommentMethod() => // preserve method line
        System.Math.Abs(
            -6);

    public int BlockCommentProperty => /* preserve property block */ System.Math.Abs(
        -7);

    public int LineCommentProperty => // preserve property line
        System.Math.Abs(
            -8);

    public int BlockCommentAccessor
    {
        get => /* preserve accessor block */ System.Math.Abs(
            -9);
    }

    public int LineCommentAccessor
    {
        get => // preserve accessor line
            System.Math.Abs(
                -10);
    }

    public int Simple() => 3;
    public int SimpleProperty => 3;
    public int SimpleAccessor { get => 3; }
    public int SimpleBlock() { return 3; }

    public int Arithmetic(int a, int b)
    {
        return a +
            b;
    }

    public bool Comment(bool a, bool b)
    {
        return a && // keep operator attachment
            b;
    }

    public int CommentedAutoProperty
    {
        get; // keep accessor attachment
        init;
    }

    public int Directive => System.Math.Abs(
#if DEBUG
        -1
#else
        -2
#endif
    );
}
