using System;

namespace OnlineSeverGame.Server
{
    /// <summary>
    /// Server log lines. The console server keeps the default (stdout); the Unity client points Sink at its own
    /// log when it hosts a room. Sink may be called from any server thread.
    /// </summary>
    public static class Log
    {
        private static readonly object Gate = new();

        public static Action<string> Sink = Console.WriteLine;

        public static void Info(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            lock (Gate) Sink?.Invoke(line);
        }
    }
}
