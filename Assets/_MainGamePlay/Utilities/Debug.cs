using System;

static public class Debug
{
    internal static void Assert(bool test, string msg)
    {
        if (!test)
            throw new Exception(msg);
    }

    internal static void Log(string msg)
    {
        Console.WriteLine(msg);
    }

    internal static void LogError(string msg)
    {
        Console.WriteLine(msg);
        throw new Exception(msg);
    }
}
