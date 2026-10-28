namespace Player.Enhancement;

public enum DriverVsr { None, Nvidia, Intel }

/// <summary>
/// Layer 1: the GPU driver's own video super resolution (RTX VSR / Intel VSR) and RTX Video HDR, through mpv's
/// d3d11vpp filter. It works on D3D11 frames, so the CPU framerate filter rules it out. When it fails (old Intel
/// iGPU, driver without VSR) mpv logs it and the app drops to layer 2. The driver may also switch VSR off on its
/// own under GPU load without telling mpv; that cannot be seen from here.
/// </summary>
public static class DriverLayer
{
    public const string Label = "vsr";

    /// <summary>At or below this the video is shown at about its own size: nothing to upscale.</summary>
    public const double MinUpscale = 1.05;

    public static DriverVsr Detect(GpuInfo? gpu) => gpu switch
    {
        { HasRtxVideo: true } => DriverVsr.Nvidia,
        { VendorId: GpuInfo.Intel } => DriverVsr.Intel, // Arc / 10th gen and newer; older parts fail and fall back
        _ => DriverVsr.None,
    };

    public static string Name(DriverVsr vsr) => vsr == DriverVsr.Intel ? "Intel VSR" : "RTX VSR";

    /// <summary>The d3d11vpp filter, or null when the layer has nothing to do.</summary>
    /// <param name="upscale">The video is being enlarged and driver upscaling is wanted.</param>
    public static string? Filter(DriverVsr vsr, bool upscale, bool hdr)
    {
        upscale &= vsr != DriverVsr.None;
        hdr &= vsr == DriverVsr.Nvidia;
        if (!upscale && !hdr) return null;
        List<string> opts = [];
        if (upscale)
        {
            // VSR takes 8-bit input; the HDR path must keep the processor's 10-bit output instead.
            if (!hdr) opts.Add("format=nv12");
            // ponytail: fixed 2x (mpv scales the rest); a size-matched factor would rebuild the vf chain on every resize.
            opts.Add("scale=2");
            opts.Add($"scaling-mode={(vsr == DriverVsr.Intel ? "intel" : "nvidia")}");
        }
        if (hdr) opts.Add("nvidia-true-hdr");
        return "d3d11vpp=" + string.Join(':', opts);
    }

    public static bool Scales(string? filter) => filter?.Contains("scale=") == true;
    public static bool Hdr(string? filter) => filter?.Contains("nvidia-true-hdr") == true;

    /// <summary>An mpv log line saying the d3d11vpp filter could not be created or was disabled.
    /// A failed "vf remove @vsr" (nothing to remove yet) is not one.</summary>
    public static bool IsFailure(string logLine) =>
        !logLine.Contains(" remove ", StringComparison.Ordinal)
        && (logLine.Contains("d3d11vpp", StringComparison.OrdinalIgnoreCase) || logLine.Contains("@" + Label, StringComparison.Ordinal))
        && (logLine.Contains("fail", StringComparison.OrdinalIgnoreCase) || logLine.Contains("error", StringComparison.OrdinalIgnoreCase)
            || logLine.Contains("Disabling", StringComparison.Ordinal));
}
