using System.Globalization;
using System.Text;

namespace Player.Enhancement;

/// <summary>
/// A colour grade ("look"), applied on the GPU by Assets/Shaders/looks.glsl at the MAIN hook: RGB that is still
/// gamma-encoded, so the numbers act on what the eye sees. In order: an S-curve, an affine channel matrix, then
/// luma-weighted split toning. Every step is linear in strength, so a weaker look is the same shader with smaller
/// numbers and changing it never recompiles anything.
/// </summary>
/// <param name="Matrix">Row-major 3×3: out.r = row 0 · rgb, and so on.</param>
/// <param name="Offset">Added after the matrix; lifts the blacks for faded looks.</param>
/// <param name="Curve">−1 flattens, 0 leaves alone, 1 is a full smoothstep S-curve.</param>
/// <param name="Shadows">Tint added in the dark parts; its own luma is removed so brightness stays put.</param>
/// <param name="Highlights">Same for the bright parts.</param>
public sealed record ColorLook(string Id, double[] Matrix, double[] Offset, double Curve, double[] Shadows, double[] Highlights)
{
    /// <summary>The CPU twin of looks.glsl (without grain, vignette and bars), for previews and tests. Values are 0..1.</summary>
    public (double R, double G, double B) Apply(double r, double g, double b, double strength = 1)
    {
        var p = ColorLooks.Scaled(this, strength);
        double[] c = [SCurve(r, p.Curve), SCurve(g, p.Curve), SCurve(b, p.Curve)];
        var o = new double[3];
        for (var i = 0; i < 3; i++)
            o[i] = p.Matrix[i * 3] * c[0] + p.Matrix[i * 3 + 1] * c[1] + p.Matrix[i * 3 + 2] * c[2] + p.Offset[i];
        var l = ColorLooks.Luma(Math.Clamp(o[0], 0, 1), Math.Clamp(o[1], 0, 1), Math.Clamp(o[2], 0, 1));
        var t = new double[3];
        for (var i = 0; i < 3; i++) t[i] = p.Shadows[i] * (1 - l) * (1 - l) + p.Highlights[i] * l * l;
        var tl = ColorLooks.Luma(t[0], t[1], t[2]);
        return (Math.Clamp(o[0] + t[0] - tl, 0, 1), Math.Clamp(o[1] + t[1] - tl, 0, 1), Math.Clamp(o[2] + t[2] - tl, 0, 1));
    }

    static double SCurve(double x, double k)
    {
        x = Math.Clamp(x, 0, 1);
        return x + k * (x * x * (3 - 2 * x) - x);
    }
}

public static class ColorLooks
{
    /// <summary>Rec. 709 luma weights, the same ones the shader uses.</summary>
    static readonly double[] W = [0.2126, 0.7152, 0.0722];

    static readonly double[] Zero = [0, 0, 0];

    /// <summary>Cinemascope, the target of the letterbox bars.</summary>
    public const double ScopeAspect = 2.39;

    // Tuned on a hue band, a grey ramp and skin / sky / foliage swatches; each one should stay watchable at 100 %.
    public static readonly IReadOnlyList<ColorLook> All =
    [
        new("none", Identity(), Zero, 0, Zero, Zero),
        new("warm", Mul(Scale(1.05, 1.0, 0.88), Saturation(1.06)), [0.01, 0.005, 0], 0.06, Zero, [0.03, 0.012, -0.03]),
        new("cool", Mul(Scale(0.92, 0.99, 1.08), Saturation(0.96)), [0, 0.003, 0.012], 0.06, [-0.02, 0, 0.03], Zero),
        new("teal-orange", Saturation(1.1), Zero, 0.25, [-0.07, 0.02, 0.08], [0.09, 0.02, -0.09]),
        new("technicolor", Mul([1.10, -0.05, -0.05, -0.04, 1.08, -0.04, -0.05, -0.05, 1.10], Saturation(1.32)), Zero, 0.3,
            [0, 0, 0.01], [0.02, 0.01, -0.02]),
        new("bleach", Mul(Scale(0.98, 1.0, 1.02), Saturation(0.42)), Zero, 0.55, [0, 0.008, 0.015], Zero),
        new("matte", Mul(Scale(0.86, 0.86, 0.86), Saturation(0.85)), [0.08, 0.075, 0.07], 0.12, [0, 0, 0.01], [0.01, 0.005, -0.01]),
        new("vintage", Mul(Scale(0.94, 0.88, 0.76), Saturation(0.7)), [0.065, 0.05, 0.03], 0.1, [0, 0.01, 0.02], [0.04, 0.025, -0.03]),
        new("sepia", Tone(1.02, 0.84, 0.61), [0.03, 0.02, 0], 0.12, Zero, Zero),
        new("mono", Tone(1, 1, 1), Zero, 0.15, Zero, Zero),
        new("noir", Tone(1.06, 1.06, 1.06), [-0.025, -0.025, -0.025], 0.7, Zero, Zero),
        new("neon", Saturation(1.25), Zero, 0.2, [0.08, -0.05, 0.12], [-0.07, 0.05, 0.06]),
    ];

    public static ColorLook None => All[0];

    public static ColorLook Find(string? id) => All.FirstOrDefault(l => l.Id == id) ?? None;

    /// <summary>The look at <paramref name="strength"/> (0..1): every part moved towards "no change".</summary>
    public static ColorLook Scaled(ColorLook look, double strength)
    {
        var s = Math.Clamp(strength, 0, 1);
        var id = Identity();
        return look with
        {
            Matrix = [.. look.Matrix.Select((m, i) => id[i] + s * (m - id[i]))],
            Offset = [.. look.Offset.Select(v => v * s)],
            Curve = look.Curve * s,
            Shadows = [.. look.Shadows.Select(v => v * s)],
            Highlights = [.. look.Highlights.Select(v => v * s)],
        };
    }

    /// <summary>Height of each letterbox bar as a fraction of the frame, so a video of <paramref name="aspect"/> shows as 2.39:1.
    /// 0 when the video is already that wide.</summary>
    public static double LetterboxFraction(double aspect) =>
        aspect <= 0 ? 0 : Math.Max(0, (1 - aspect / ScopeAspect) / 2);

    /// <summary>glsl-shader-opts entries for looks.glsl (without "split", which the caller shares with compare.glsl).
    /// <paramref name="grain"/> and <paramref name="vignette"/> are 0..1, <paramref name="bars"/> from <see cref="LetterboxFraction"/>.</summary>
    public static string ShaderOptions(ColorLook look, double strength, double grain, double vignette, double bars)
    {
        var p = Scaled(look, strength);
        var sb = new StringBuilder();
        void Add(string name, double value) =>
            sb.Append(sb.Length > 0 ? "," : "").Append(name).Append('=').Append(value.ToString("0.#####", CultureInfo.InvariantCulture));
        Add("lk_curve", p.Curve);
        for (var i = 0; i < 9; i++) Add($"lk_m{i}", p.Matrix[i]);
        for (var i = 0; i < 3; i++) Add($"lk_o{i}", p.Offset[i]);
        for (var i = 0; i < 3; i++) Add($"lk_s{i}", p.Shadows[i]);
        for (var i = 0; i < 3; i++) Add($"lk_h{i}", p.Highlights[i]);
        Add("lk_grain", Math.Clamp(grain, 0, 1));
        Add("lk_vig", Math.Clamp(vignette, 0, 1));
        Add("lk_bars", Math.Clamp(bars, 0, 0.45));
        return sb.ToString();
    }

    internal static double Luma(double r, double g, double b) => W[0] * r + W[1] * g + W[2] * b;

    static double[] Identity() => [1, 0, 0, 0, 1, 0, 0, 0, 1];

    static double[] Scale(double r, double g, double b) => [r, 0, 0, 0, g, 0, 0, 0, b];

    /// <summary>Mixes each pixel with its own grey: 0 = black and white, 1 = unchanged, above 1 = more colour.</summary>
    static double[] Saturation(double s)
    {
        var m = new double[9];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                m[row * 3 + col] = (1 - s) * W[col] + (row == col ? s : 0);
        return m;
    }

    /// <summary>Black and white, tinted: every channel is the pixel's luma times its tone.</summary>
    static double[] Tone(double r, double g, double b)
    {
        double[] tone = [r, g, b];
        var m = new double[9];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                m[row * 3 + col] = tone[row] * W[col];
        return m;
    }

    /// <summary>a × b: b is applied first.</summary>
    static double[] Mul(double[] a, double[] b)
    {
        var m = new double[9];
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                m[row * 3 + col] = a[row * 3] * b[col] + a[row * 3 + 1] * b[3 + col] + a[row * 3 + 2] * b[6 + col];
        return m;
    }
}
