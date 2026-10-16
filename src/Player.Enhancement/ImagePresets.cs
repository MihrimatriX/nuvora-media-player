namespace Player.Enhancement;

/// <summary>An image-quality preset: a fixed set of mpv properties.</summary>
public sealed record ImagePreset(string Id, string Name, string Description, string Scaler, IReadOnlyDictionary<string, string> Options)
{
    public string LayerLabel => Id == "off" ? "Kapalı" : $"Yerleşik · {Scaler}";
}

public static class ImagePresets
{
    // ponytail: mpv built-in scalers only. FSRCNNX / Anime4K / ArtCNN shaders (layer 2) and
    // RTX/Intel VSR (layer 1) arrive in phase 3 and slot in as extra options on these same presets.
    // Every preset sets the same keys, so switching presets never leaves stale values behind.
    public static readonly IReadOnlyList<ImagePreset> All =
    [
        new("off", "Kapalı", "Kaynak görüntü", "bilinear", Opts("bilinear", "bilinear", "bilinear", deband: false, hq: false)),
        new("balanced", "Dengeli", "Canlı çekim, düşük yük", "spline36", Opts("spline36", "spline36", "mitchell", deband: false, hq: true)),
        new("anime", "Anime", "Çizgi ve düz renk", "lanczos + deband", Opts("ewa_lanczossharp", "spline36", "mitchell", deband: true, hq: true)),
        new("max", "Maksimum", "En yüksek kalite", "ewa_lanczossharp", Opts("ewa_lanczossharp", "ewa_lanczossharp", "mitchell", deband: true, hq: true)),
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
