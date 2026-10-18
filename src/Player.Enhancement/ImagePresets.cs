namespace Player.Enhancement;

/// <summary>
/// An image-quality preset: a fixed set of mpv properties plus an optional GPU shader chain.
/// <paramref name="Shaders"/> is ordered heaviest first; <see cref="LayerFallback"/> walks it down and
/// ends on the built-in scaler in <paramref name="Scaler"/>.
/// </summary>
public sealed record ImagePreset(string Id, string Name, string Description, string Scaler,
    IReadOnlyDictionary<string, string> Options, IReadOnlyList<ShaderLayer> Shaders)
{
    public string BuiltInLabel => Id == "off" ? "Kapalı" : $"Yerleşik · {Scaler}";
}

public static class ImagePresets
{
    // Layer 2 (GPU shaders) sits on top of the built-in scalers: when a shader's //!WHEN guard is false
    // (no upscaling) or the chain fell back, the built-in options below are what renders.
    // Every preset sets the same keys, so switching presets never leaves stale values behind.
    public static readonly IReadOnlyList<ImagePreset> All =
    [
        new("off", "Kapalı", "Kaynak görüntü", "bilinear",
            Opts("bilinear", "bilinear", "bilinear", deband: false, hq: false), []),
        new("balanced", "Dengeli", "Canlı çekim, FSRCNNX", "spline36",
            Opts("spline36", "spline36", "mitchell", deband: false, hq: true), [ShaderLayers.Fsrcnnx]),
        new("anime", "Anime", "Çizgi ve düz renk, Anime4K", "lanczos + deband",
            Opts("ewa_lanczossharp", "spline36", "mitchell", deband: true, hq: true), [ShaderLayers.Anime4KFast, ShaderLayers.Anime4KLite]),
        new("max", "Maksimum", "En yüksek kalite, ArtCNN", "ewa_lanczossharp",
            Opts("ewa_lanczossharp", "ewa_lanczossharp", "mitchell", deband: true, hq: true), [ShaderLayers.ArtCnn, ShaderLayers.Fsrcnnx]),
    ];

    public static ImagePreset Off => All[0];

    public static ImagePreset Find(string id) => All.FirstOrDefault(p => p.Id == id) ?? All[1];

    static Dictionary<string, string> Opts(string scale, string cscale, string dscale, bool deband, bool hq) => new()
    {
        ["scale"] = scale,
        ["cscale"] = cscale,
        ["dscale"] = dscale,
        ["deband"] = deband ? "yes" : "no",
        ["sigmoid-upscaling"] = hq ? "yes" : "no",
        ["correct-downscaling"] = hq ? "yes" : "no",
    };
}

/// <summary>What is really rendering right now, for the "Aktif katman" chip.</summary>
public static class ActiveLayer
{
    /// <param name="shader">The loaded layer, or null when none is loaded (preset has none, disabled, or fell back).</param>
    /// <param name="upscale">From <see cref="ShaderLayers.UpscaleFactor"/>; 0 = not known yet.</param>
    public static string Describe(ImagePreset preset, ShaderLayer? shader, double upscale, bool fellBack)
    {
        if (shader is null)
            return fellBack && preset.Shaders.Count > 0 ? $"{preset.BuiltInLabel} · GPU yetişmedi" : preset.BuiltInLabel;
        var suffix = fellBack ? " · düşürüldü" : "";
        if (upscale <= 0 || upscale > shader.MinUpscale) return $"Shader · {shader.Name}{suffix}";
        // Loaded, but the video is shown at (or below) its own size, so mpv skips the upscaling passes.
        return shader.RestoresAtNative
            ? $"Shader · {shader.Name} (yalnız onarım){suffix}"
            : $"{preset.BuiltInLabel} · {shader.Name} beklemede";
    }
}
