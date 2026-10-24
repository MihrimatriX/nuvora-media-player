using System.Globalization;
using System.Text.Json;
using Player.App;
using Player.Enhancement;

namespace Player.Core.Tests;

public class ColorLooksTests
{
    static readonly (double R, double G, double B)[] Samples =
        [(0, 0, 0), (1, 1, 1), (0.5, 0.5, 0.5), (0.86, 0.64, 0.52), (0.45, 0.68, 0.92), (0.9, 0.1, 0.1)];

    [Fact]
    public void None_AndZeroStrength_LeaveColoursAlone()
    {
        foreach (var (r, g, b) in Samples)
        {
            Assert.Equal((r, g, b), Rounded(ColorLooks.None.Apply(r, g, b)));
            foreach (var look in ColorLooks.All)
                Assert.Equal((r, g, b), Rounded(look.Apply(r, g, b, strength: 0)));
        }
    }

    [Theory]
    [InlineData("mono")]
    [InlineData("noir")]
    public void BlackAndWhiteLooks_HaveNoColour(string id)
    {
        foreach (var (r, g, b) in Samples)
        {
            var o = ColorLooks.Find(id).Apply(r, g, b);
            Assert.Equal(o.R, o.G, 6);
            Assert.Equal(o.G, o.B, 6);
        }
    }

    [Fact]
    public void Sepia_IsWarm_AndTealOrange_SplitsShadowsFromSkin()
    {
        var sepia = ColorLooks.Find("sepia").Apply(0.5, 0.5, 0.5);
        Assert.True(sepia.R > sepia.G && sepia.G > sepia.B);

        var look = ColorLooks.Find("teal-orange");
        var shadow = look.Apply(0.15, 0.15, 0.15);
        var skin = look.Apply(0.86, 0.64, 0.52);
        Assert.True(shadow.B > shadow.R); // teal shadows
        Assert.True(skin.R - skin.B > 0.86 - 0.52); // warmer skin
    }

    [Fact]
    public void EveryLook_KeepsAGreyRampInOrder()
    {
        // A grade may tint and bend the curve, but it must never turn darker input into brighter output.
        foreach (var look in ColorLooks.All)
        {
            var last = -1.0;
            for (var v = 0.0; v <= 1.0; v += 0.05)
            {
                var (r, g, b) = look.Apply(v, v, v);
                var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                Assert.True(luma >= last - 1e-9, $"{look.Id} at {v}");
                last = luma;
            }
        }
    }

    [Fact]
    public void ShaderOptions_NameEveryParameterOnce_InInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); // decimal comma
        try
        {
            var opts = ColorLooks.ShaderOptions(ColorLooks.Find("vintage"), 0.5, 0.3, 0.45, 0.128);
            var names = opts.Split(',').Select(o => o.Split('=')[0]).ToList();

            Assert.Equal(23 - 1, names.Count); // every PARAM in looks.glsl except "split"
            Assert.Equal(names.Count, names.Distinct().Count());
            Assert.Contains("lk_grain=0.3", opts);
            Assert.Contains("lk_bars=0.128", opts);
            Assert.DoesNotContain("0,", opts.Replace("lk_m", "").Split('=').Skip(1).First()); // no "0,5"

            var shader = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "looks.glsl"));
            foreach (var name in names) Assert.Contains($"//!PARAM {name}\n", shader.ReplaceLineEndings("\n"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ShaderOptions_StayInsideTheRangesLooksGlslDeclares()
    {
        // mpv refuses a glsl-shader-opts value outside a parameter's MINIMUM / MAXIMUM, and the look would silently not apply.
        var shader = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "looks.glsl")).ReplaceLineEndings("\n");
        var ranges = System.Text.RegularExpressions.Regex
            .Matches(shader, @"//!PARAM (\w+)\n//!TYPE float\n//!MINIMUM (\S+)\n//!MAXIMUM (\S+)\n")
            .ToDictionary(m => m.Groups[1].Value, m => (Min: double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                Max: double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)));
        Assert.Equal(23, ranges.Count); // every parameter declares its range

        foreach (var look in ColorLooks.All)
        foreach (var option in ColorLooks.ShaderOptions(look, 1, 1, 1, ColorLooks.LetterboxFraction(1)).Split(','))
        {
            var (name, value) = (option.Split('=')[0], double.Parse(option.Split('=')[1], CultureInfo.InvariantCulture));
            Assert.InRange(value, ranges[name].Min, ranges[name].Max);
        }
    }

    [Theory]
    [InlineData(16 / 9.0, 0.12808)]
    [InlineData(4 / 3.0, 0.22106)]
    [InlineData(2.39, 0)]
    [InlineData(2.76, 0)]
    public void LetterboxFraction_MakesTheVisiblePicture239(double aspect, double expected) =>
        Assert.Equal(expected, ColorLooks.LetterboxFraction(aspect), 4);

    [Fact]
    public void UserPreset_SavedBeforeFiltersExisted_StillLoads()
    {
        const string old = """{"Name":"Gece","BasePreset":"balanced","Contrast":0,"Saturation":0,"Brightness":-8,"Sharpness":0,"Gamma":0,"Hue":0,"TargetFps":0}""";
        var preset = JsonSerializer.Deserialize<UserPreset>(old)!;

        Assert.Equal(("none", 100.0, 0.0, 0.0, false), (preset.Look, preset.LookStrength, preset.Grain, preset.Vignette, preset.Letterbox));
    }

    [Fact]
    public void WheelSteps_AddsUpSmallDeltas_AndRestartsWhenReversed()
    {
        var wheel = new WheelSteps();
        Assert.Equal(1, wheel.Take(1)); // a mouse notch
        Assert.Equal(0, wheel.Take(0.4)); // touchpad
        Assert.Equal(0, wheel.Take(0.4));
        Assert.Equal(1, wheel.Take(0.4));
        Assert.Equal(0, wheel.Take(-0.6)); // reversing drops what was left over
        Assert.Equal(-1, wheel.Take(-0.6));
        Assert.Equal(2, wheel.Take(2.3));
    }

    static (double, double, double) Rounded((double R, double G, double B) c) =>
        (Math.Round(c.R, 9), Math.Round(c.G, 9), Math.Round(c.B, 9));
}
