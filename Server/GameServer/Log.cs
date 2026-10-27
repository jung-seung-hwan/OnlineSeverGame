namespace OnlineSeverGame.Server;

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        lock (Gate) Console.WriteLine(line);
    }
}
