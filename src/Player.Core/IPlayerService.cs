namespace Player.Core;

/// <summary>Values match mpv_format so they can be passed straight through.</summary>
public enum PropertyKind { String = 1, Flag = 3, Double = 5 }

public interface IPlayerService : IDisposable
{
    /// <summary>Observed property changed. Raised through the service's dispatcher (the UI thread in the app).
    /// Value is double / bool / string, or null when the property is unavailable.</summary>
    event Action<string, object?>? PropertyChanged;

    /// <summary>A file failed to load or play (corrupt, unsupported, missing).</summary>
    event Action<string>? PlaybackError;

    /// <summary>Playback resumed after a seek or a file load (mpv PLAYBACK_RESTART).</summary>
    event Action? SeekCompleted;

    /// <summary>Diagnostics: mpv warnings and errors, failed commands, rejected options.</summary>
    event Action<string>? Log;

    void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options);
    void Observe(string name, PropertyKind kind);

    /// <summary>Fire-and-forget mpv input command, e.g. ("seek", "10", "relative").</summary>
    void Command(params string[] args);
}
