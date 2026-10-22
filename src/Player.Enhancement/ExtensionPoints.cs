namespace Player.Enhancement;

// Extension points the spec reserves for later. Nothing implements them yet and no UI points at them.

/// <summary>
/// Frame interpolation beyond mpv's own (interpolation + display-resample, and the FFmpeg framerate filter):
/// SVP or RIFE running in VapourSynth, handed to mpv as <c>vf=vapoursynth=&lt;script.vpy&gt;</c>.
/// </summary>
public interface IFrameInterpolator
{
    string Name { get; }

    /// <summary>VapourSynth runtime, plugin and models present (e.g. vs-rife + a TensorRT/NCNN model).</summary>
    bool IsAvailable { get; }

    /// <summary>The vf entry to add for this source/target rate, or null when it cannot do this conversion.</summary>
    string? VideoFilter(double sourceFps, double targetFps);
}
// TODO: a VapourSynth-backed IFrameInterpolator (SVP or RIFE). It replaces the @fps lavfi filter in
// PlayerViewModel.ApplyFps and, like that filter, needs copy-back decoding, so it also rules out DriverLayer.

/// <summary>
/// Everything that changes an offline remaster's output. It is the cache key: a cached file is reused only
/// when every field matches, so a new setting that affects the picture must be added here.
/// </summary>
public sealed record RemasterKey(string SourcePath, long SourceSize, DateTime SourceModifiedUtc, string PresetId, string ModelId, int ScaleFactor);

/// <summary>
/// Offline "Remaster": processes a whole file ahead of time and caches the result. Must run in a separate
/// helper process so a crash or a long GPU job never takes playback down.
/// </summary>
public interface IOfflineRemaster
{
    /// <summary>The cached output for <paramref name="key"/>, or null when it has not been rendered.</summary>
    string? Cached(RemasterKey key);

    /// <summary>Renders <paramref name="key"/> and returns the output path. Progress is 0..1.</summary>
    Task<string> RenderAsync(RemasterKey key, IProgress<double>? progress, CancellationToken cancel);
}
// TODO: helper-process implementation of IOfflineRemaster, with a cache folder under AppSettings.ConfigDir.
