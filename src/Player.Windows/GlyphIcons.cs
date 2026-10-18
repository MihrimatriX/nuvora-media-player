using System.Numerics;
using System.Runtime.Versioning;
using Player.Windows.Interop;

namespace Player.Windows;

public enum Glyph { Play, Pause, Previous, Next }

/// <summary>Draws the thumbnail-toolbar glyphs in code, so no .ico assets are needed and they stay crisp at any icon size.</summary>
public static class GlyphIcons
{
    // Convex polygons on a 16×16 grid, in the shapes of the overlay's transport buttons.
    static readonly Vector2[][] PlayShape = [[new(4.5f, 2.5f), new(13.5f, 8), new(4.5f, 13.5f)]];
    static readonly Vector2[][] PauseShape = [Rect(3.5f, 2.5f, 3.5f, 11), Rect(9, 2.5f, 3.5f, 11)];
    static readonly Vector2[][] PreviousShape = [Rect(2.5f, 3, 2, 10), [new(13.5f, 3), new(13.5f, 13), new(5, 8)]];
    static readonly Vector2[][] NextShape = [[new(2.5f, 3), new(11, 8), new(2.5f, 13)], Rect(11.5f, 3, 2, 10)];

    const int Supersample = 4;

    /// <summary>White glyph on transparent, as straight-alpha 0xAARRGGBB pixels, rows top to bottom.</summary>
    public static uint[] Rasterize(Glyph glyph, int size)
    {
        var shapes = glyph switch
        {
            Glyph.Play => PlayShape,
            Glyph.Pause => PauseShape,
            Glyph.Previous => PreviousShape,
            _ => NextShape,
        };
        var pixels = new uint[size * size];
        var scale = 16f / size;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var hits = 0;
            for (var sy = 0; sy < Supersample; sy++)
            for (var sx = 0; sx < Supersample; sx++)
            {
                var p = new Vector2(x + (sx + 0.5f) / Supersample, y + (sy + 0.5f) / Supersample) * scale;
                if (shapes.Any(s => Contains(s, p))) hits++;
            }
            var alpha = (uint)(hits * 255 / (Supersample * Supersample));
            pixels[y * size + x] = alpha << 24 | 0x00FFFFFF;
        }
        return pixels;
    }

    /// <summary>A small-icon-sized HICON for the glyph. The caller owns it and frees it with <see cref="Destroy"/>.</summary>
    [SupportedOSPlatform("windows")]
    public static unsafe nint Create(Glyph glyph)
    {
        var size = User32.GetSystemMetrics(User32.SmCxSmIcon);
        if (size <= 0) size = 16;
        var header = new Gdi32.BitmapInfoHeader
        {
            Size = (uint)sizeof(Gdi32.BitmapInfoHeader), Width = size, Height = -size, // negative: top-down rows
            Planes = 1, BitCount = 32,
        };
        var color = Gdi32.CreateDIBSection(0, header, 0, out var bits, 0, 0);
        var mask = Gdi32.CreateBitmap(size, size, 1, 1, null);
        try
        {
            Rasterize(glyph, size).CopyTo(new Span<uint>(bits, size * size));
            return User32.CreateIconIndirect(new User32.IconInfo { IsIcon = 1, Mask = mask, Color = color });
        }
        finally
        {
            Gdi32.DeleteObject(color);
            Gdi32.DeleteObject(mask);
        }
    }

    [SupportedOSPlatform("windows")]
    public static void Destroy(nint icon)
    {
        if (icon != 0) User32.DestroyIcon(icon);
    }

    static Vector2[] Rect(float x, float y, float w, float h) => [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)];

    /// <summary>Point in convex polygon: on the same side of every edge, whichever the winding.</summary>
    static bool Contains(Vector2[] polygon, Vector2 p)
    {
        var sign = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            var s = Math.Sign(cross);
            if (s == 0) continue;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }
}
