namespace Player.Core.Tests;

static class TestConfig
{
    /// <summary>AppSettings uses a "portable" folder next to the binary when present: keep tests out of the user's real settings.</summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Isolate() => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "portable"));
}

/// <summary>Records commands instead of talking to libmpv, and lets a test play the mpv side.</summary>
sealed class FakePlayerService : IPlayerService
{
    public List<string[]> Commands { get; } = [];
    public List<string> Observed { get; } = [];
    public IReadOnlyDictionary<string, string>? Options { get; private set; }

    public event Action<string, object?>? PropertyChanged;
    public event Action<string>? PlaybackError;
    public event Action? SeekCompleted;
    public event Action<string>? Log;

    public void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options) => Options = options;
    public void Observe(string name, PropertyKind kind) => Observed.Add(name);
    public void Command(params string[] args) => Commands.Add(args);
    public void Dispose() { }

    public void Raise(string name, object? value) => PropertyChanged?.Invoke(name, value);
    public void RaiseError(string message) => PlaybackError?.Invoke(message);
    public void CompleteSeek() => SeekCompleted?.Invoke();
    public void RaiseLog(string message) => Log?.Invoke(message);

    /// <summary>Commands whose first argument is <paramref name="name"/>, joined for readable asserts.</summary>
    public List<string> Named(string name) => Commands.Where(c => c[0] == name).Select(c => string.Join(' ', c)).ToList();
}
