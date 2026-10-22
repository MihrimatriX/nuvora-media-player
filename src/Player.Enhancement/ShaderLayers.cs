namespace Player.Enhancement;

/// <summary>
/// One GPU shader layer: an ordered set of mpv user shaders (glsl-shaders) shipped in Assets/Shaders.
/// <paramref name="MinUpscale"/> mirrors the shaders' own <c>//!WHEN</c> guard: below it mpv skips the
/// upscaling passes, so the layer is loaded but not doing anything.
/// </summary>
public sealed record ShaderLayer(string Id, string Name, IReadOnlyList<string> Files, double MinUpscale, bool RestoresAtNative = false);

public static class ShaderLayers
{
    // Luma doubler, light enough for 1080p → 4K on a GTX 1060-class card. LGPL-3.0.
    public static readonly ShaderLayer Fsrcnnx = new("fsrcnnx", "FSRCNNX x2",
        ["FSRCNNX/FSRCNNX_x2_8-0-4-1.glsl"], 1.3);

    // Newer luma doubler (compute shaders), sharper than FSRCNNX and roughly twice the cost. MIT.
    public static readonly ShaderLayer ArtCnn = new("artcnn", "ArtCNN C4F16",
        ["ArtCNN/ArtCNN_C4F16.glsl"], 1.3);

    // Anime4K v4 "Mode A (Fast)" from the upstream mpv guide. Restore runs at any size. MIT.
    public static readonly ShaderLayer Anime4KFast = new("anime4k-a", "Anime4K A",
    [
        "Anime4K/Anime4K_Clamp_Highlights.glsl",
        "Anime4K/Anime4K_Restore_CNN_M.glsl",
        "Anime4K/Anime4K_Upscale_CNN_x2_M.glsl",
        "Anime4K/Anime4K_AutoDownscalePre_x2.glsl",
        "Anime4K/Anime4K_AutoDownscalePre_x4.glsl",
        "Anime4K/Anime4K_Upscale_CNN_x2_S.glsl",
    ], 1.2, RestoresAtNative: true);

    // Same family without the restore pass and with the small upscaler only.
    public static readonly ShaderLayer Anime4KLite = new("anime4k-lite", "Anime4K Lite",
    [
        "Anime4K/Anime4K_Clamp_Highlights.glsl",
        "Anime4K/Anime4K_Upscale_CNN_x2_S.glsl",
    ], 1.2);

    public static IReadOnlyList<ShaderLayer> All { get; } = [ArtCnn, Fsrcnnx, Anime4KFast, Anime4KLite];

    /// <summary>
    /// How much mpv scales the source to fit the window: the video's display size (aspect-corrected)
    /// fitted into the OSD area, divided by the coded height. 0 while anything is unknown.
    /// </summary>
    public static double UpscaleFactor(double sourceHeight, double displayWidth, double displayHeight, double osdWidth, double osdHeight)
    {
        if (sourceHeight <= 0 || displayWidth <= 0 || displayHeight <= 0 || osdWidth <= 0 || osdHeight <= 0) return 0;
        var fit = Math.Min(osdWidth / displayWidth, osdHeight / displayHeight);
        return displayHeight * fit / sourceHeight;
    }
}
