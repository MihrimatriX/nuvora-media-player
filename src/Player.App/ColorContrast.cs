using Avalonia.Media;

namespace Player.App;

/// <summary>WCAG 2.x contrast helpers, used to derive readable brushes from the user's accent colour.</summary>
public static class ColorContrast
{
    /// <summary>Darkest surface accent-coloured text sits on (sheets, panels).</summary>
    public static readonly Color Surface = Color.Parse("#16191F");
    static readonly Color Ink = Color.Parse("#0B0C12");

    public static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Ratio(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>White or near-black, whichever reads better on <paramref name="background"/>.</summary>
    public static Color ReadableOn(Color background) =>
        Ratio(Colors.White, background) >= Ratio(Ink, background) ? Colors.White : Ink;

    /// <summary>Mixes <paramref name="color"/> towards white until it reaches <paramref name="minRatio"/> on <paramref name="background"/>.</summary>
    public static Color Lighten(Color color, Color background, double minRatio = 4.5)
    {
        for (var t = 0.0; t <= 1.0; t += 0.05)
        {
            var c = Color.FromRgb(Mix(color.R, t), Mix(color.G, t), Mix(color.B, t));
            if (Ratio(c, background) >= minRatio) return c;
        }
        return Colors.White;
    }

    static byte Mix(byte v, double t) => (byte)Math.Round(v + (255 - v) * t);
}
