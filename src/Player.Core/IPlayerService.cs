namespace Player.Core;

/// <summary>Values match mpv_format so they can be passed straight through.</summary>
public enum PropertyKind { String = 1, Flag = 3, Double = 5 }

public interface IPlayerService : IDisposable
{
    /// <summary>Observed property changed. Raised on the thread that created the service.
    /// Value is double / bool / string, or null when the property is unavailable.</summary>
    event Action<string, object?>? PropertyChanged;

    /// <summary>A file failed to load or play (corrupt, unsupported, missing).</summary>
    event Action<string>? PlaybackError;

    void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options);
    void Observe(string name, PropertyKind kind);

    /// <summary>Fire-and-forget mpv input command, e.g. ("seek", "10", "relative").</summary>
    void Command(params string[] args);
}
