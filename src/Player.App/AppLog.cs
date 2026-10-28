using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace Player.App;

/// <summary>Plain-text log files under <c>ConfigDir/logs</c>. Never throws: logging must not turn one failure into two.</summary>
/// <remarks>Info and warnings are written by a background thread: mpv's warnings arrive on the UI thread, and a broken
/// file can produce one per frame, each of which used to open, append to and close the log there.</remarks>
public static class AppLog
{
    static readonly Lock Gate = new();
    static readonly BlockingCollection<string> Pending = new(boundedCapacity: 2000);

    static AppLog()
    {
        new Thread(Drain) { IsBackground = true, Name = "app-log", Priority = ThreadPriority.BelowNormal }.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
    }

    static string Dir => Path.Combine(AppSettings.ConfigDir, "logs");

    public static void Info(string message) => Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} INFO {message}");

    public static void Warn(string message) => Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} WARN {message}");

    /// <summary>One file per crash, so a report can be attached as is. Written at once: the process may be going down.</summary>
    public static void Crash(Exception? ex, string source)
    {
        var text = new StringBuilder()
            .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {source}")
            .AppendLine($"{Environment.OSVersion} · .NET {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")}")
            .AppendLine(ex?.ToString() ?? "(no exception object)")
            .ToString();
        Debug.WriteLine(text);
        Flush();
        Append($"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt", text);
    }

    static void Enqueue(string line)
    {
        Debug.WriteLine(line);
        // A full queue means the disk cannot keep up with a log flood; dropping lines beats stalling the caller.
        Pending.TryAdd(line);
    }

    static void Drain()
    {
        foreach (var line in Pending.GetConsumingEnumerable())
        {
            var batch = new StringBuilder(line).AppendLine();
            while (batch.Length < 64 * 1024 && Pending.TryTake(out var next)) batch.AppendLine(next);
            Append("app.log", batch.ToString(), newLine: false);
        }
    }

    /// <summary>Writes whatever is queued, on the calling thread.</summary>
    static void Flush()
    {
        var batch = new StringBuilder();
        while (Pending.TryTake(out var line)) batch.AppendLine(line);
        if (batch.Length > 0) Append("app.log", batch.ToString(), newLine: false);
    }

    static void Append(string file, string text, bool newLine = true)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, file), newLine ? text + Environment.NewLine : text);
            }
        }
        catch
        {
            // Read-only portable folder, full disk: the Debug output is all we can do.
        }
    }
}
