namespace CodepostEx.Core;

public static class Out
{
    public static bool Silent { get; set; }

    public static void Plus(string msg)  => Write("[+]", ConsoleColor.Green,      msg);
    public static void Minus(string msg) => Write("[-]", ConsoleColor.Red,        msg);
    public static void Star(string msg)  => Write("[*]", ConsoleColor.Yellow,     msg);
    public static void Info(string msg)  => Write("[i]", ConsoleColor.Cyan,       msg);
    public static void Warn(string msg)  => Write("[!]", ConsoleColor.DarkYellow, msg);
    public static void Item(string msg)  => Write("[^]", ConsoleColor.DarkCyan, msg);
    public static void Blank()           { if (!Silent) Console.WriteLine(); }

    private static void Write(string tag, ConsoleColor color, string msg)
    {
        if (Silent) return;
        Console.ForegroundColor = color;
        Console.Write(tag);
        Console.ResetColor();
        Console.WriteLine($" {msg}");
    }
}
