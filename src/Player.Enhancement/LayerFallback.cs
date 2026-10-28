namespace Player.Enhancement;

/// <summary>
/// Steps a preset's shader chain down when the GPU can't keep up. mpv's <c>frame-drop-count</c> (frames the
/// video output dropped because rendering was late) is sampled while playing; too many drops inside
/// <see cref="Window"/> moves one level down: heaviest layer → lighter layer → built-in scalers.
/// The level reached is remembered per (preset, source height) for the session, so the next file of the
/// same kind starts where the GPU coped instead of stuttering again.
/// </summary>
public sealed class LayerFallback
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);
    /// <summary>Shader compile and the first frames after a (re)load drop frames on any GPU; ignore them.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(4);
    public const int DropThreshold = 12;

    readonly Dictionary<(string Preset, int Height), int> _reached = [];
    readonly Queue<(TimeSpan At, long Drops)> _samples = new();
    (string Preset, int Height) _key;
    TimeSpan _armedAt;
    long _lastDrops = -1;

    /// <summary>Index into the preset's <see cref="ImagePreset.Shaders"/>; equal to its count means built-in only.</summary>
    public int Level { get; private set; }
    public bool HasFallenBack { get; private set; }

    /// <summary>New file or preset: resume from the level this kind of video needed before.</summary>
    public void Start(string presetId, int sourceHeight, TimeSpan now)
    {
        _key = (presetId, sourceHeight);
        Level = _reached.GetValueOrDefault(_key);
        HasFallenBack = Level > 0;
        Rebase(now);
    }

    /// <summary>Seek, unpause, or shader reload: drop history no longer says anything about steady-state rendering.</summary>
    public void Rebase(TimeSpan now)
    {
        _samples.Clear();
        _lastDrops = -1;
        _armedAt = now + Grace;
    }

    /// <summary>Feeds the current drop counter. Returns true when it stepped one level down.</summary>
    public bool Sample(TimeSpan now, long drops, int chainLength)
    {
        if (Level >= chainLength) return false; // already on built-in scalers
        if (drops < _lastDrops) _samples.Clear(); // mpv resets the counter per file
        _lastDrops = drops;
        if (now < _armedAt) return false;

        _samples.Enqueue((now, drops));
        while (_samples.Count > 0 && now - _samples.Peek().At > Window) _samples.Dequeue();
        if (drops - _samples.Peek().Drops < DropThreshold) return false;

        Level++;
        HasFallenBack = true;
        _reached[_key] = Math.Max(_reached.GetValueOrDefault(_key), Level);
        Rebase(now);
        return true;
    }

    /// <summary>User asked for the full chain again (e.g. re-selected the preset).</summary>
    public void Forget(string presetId)
    {
        foreach (var k in _reached.Keys.Where(k => k.Preset == presetId).ToList()) _reached.Remove(k);
    }
}
