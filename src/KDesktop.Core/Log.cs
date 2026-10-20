using System.Collections.Concurrent;
using System.Text;

namespace KDesktop.Core;

/// <summary>Minimal asynchronous file logger. Never throws.</summary>
public static class Log
{
    private const long MaxFileSize = 2 * 1024 * 1024;
    private static readonly BlockingCollection<string> Queue = new(new ConcurrentQueue<string>(), 4096);
    private static readonly Thread Writer;

    public static event Action<string>? Written;

    static Log()
    {
        Writer = new Thread(WriteLoop) { IsBackground = true, Name = "K-Desktop log", Priority = ThreadPriority.BelowNormal };
        Writer.Start();
    }

    /// <summary>File name without extension: «kdesktop», or «assistant» in the assistant process.</summary>
    private static string _name = "kdesktop";

    private static string FilePath => Path.Combine(AppPaths.LogDir, _name + ".log");

    /// <summary>Writes to another file of the Logs folder (call before the first line).</summary>
    public static void UseFile(string name) => _name = name;

    public static void Info(string message) => Enqueue("INF", message);
    public static void Warn(string message) => Enqueue("WRN", message);
    public static void Error(string message, Exception? ex = null) =>
        Enqueue("ERR", ex is null ? message : $"{message}: {ex}");

    private static void Enqueue(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        Queue.TryAdd(line);
        if (level != "INF") Engine.ExecutionFeedback.OnLog(level, message);
        try { Written?.Invoke(line); } catch { /* ignore */ }
    }

    /// <summary>Blocks briefly until pending lines are written (used on shutdown).</summary>
    public static void Flush(int timeoutMs = 1000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (Queue.Count > 0 && sw.ElapsedMilliseconds < timeoutMs) Thread.Sleep(10);
    }

    private static void WriteLoop()
    {
        foreach (var line in Queue.GetConsumingEnumerable())
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogDir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxFileSize)
                {
                    var old = Path.Combine(AppPaths.LogDir, _name + ".old.log");
                    File.Copy(FilePath, old, true);
                    File.Delete(FilePath);
                }
                var sb = new StringBuilder(line).AppendLine();
                while (Queue.TryTake(out var more)) sb.AppendLine(more);
                File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* logging must never crash the app */ }
        }
    }
}
