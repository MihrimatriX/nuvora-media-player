using System.Diagnostics;
using System.Text;

namespace Player.App;

/// <summary>Plain-text log files under <c>ConfigDir/logs</c>. Never throws: logging must not turn one failure into two.</summary>
public static class AppLog
{
    static readonly Lock Gate = new();

    static string Dir => Path.Combine(AppSettings.ConfigDir, "logs");

    public static void Info(string message) => Append("app.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} INFO {message}");

    public static void Warn(string message) => Append("app.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} WARN {message}");

    /// <summary>One file per crash, so a report can be attached as is.</summary>
    public static void Crash(Exception? ex, string source)
    {
        var text = new StringBuilder()
            .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {source}")
            .AppendLine($"{Environment.OSVersion} · .NET {Environment.Version} · {(Environment.Is64BitProcess ? "x64" : "x86")}")
            .AppendLine(ex?.ToString() ?? "(no exception object)")
            .ToString();
        Append($"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.txt", text);
    }

    static void Append(string file, string text)
    {
        Debug.WriteLine(text);
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(Path.Combine(Dir, file), text + Environment.NewLine);
            }
        }
        catch
        {
            // Read-only portable folder, full disk: the Debug output above is all we can do.
        }
    }
}
