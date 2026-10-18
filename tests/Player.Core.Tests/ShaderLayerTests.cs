using Player.Enhancement;

namespace Player.Core.Tests;

public class ShaderLayerTests
{
    static readonly string ShaderDir = Path.Combine(FindRepoRoot(), "src", "Player.App", "Assets", "Shaders");

    [Fact]
    public void EveryLayerFileShipsWithItsLicense()
    {
        foreach (var file in ShaderLayers.All.SelectMany(l => l.Files))
        {
            var path = Path.Combine(ShaderDir, file);
            Assert.True(File.Exists(path), $"missing {file}");
            var dir = Path.GetDirectoryName(path)!;
            Assert.True(File.Exists(Path.Combine(dir, "LICENSE")) || File.Exists(Path.Combine(dir, "COPYING.LESSER")), $"no license next to {file}");
            Assert.Contains("//!HOOK", File.ReadAllText(path));
        }
    }

    [Fact]
    public void OffHasNoShadersAndOthersEndOnBuiltIn()
    {
        Assert.Empty(ImagePresets.Off.Shaders);
        Assert.All(ImagePresets.All.Skip(1), p => Assert.NotEmpty(p.Shaders));
    }

    [Theory]
    [InlineData(720, 1280, 720, 1920, 1080, 1.5)]   // 720p fullscreen on 1080p
    [InlineData(1080, 1920, 1080, 1920, 1080, 1.0)] // native
    [InlineData(1080, 1920, 1080, 960, 1000, 0.5)]  // width-limited window
    [InlineData(480, 853, 480, 3840, 2160, 4.5)]    // anamorphic DVD on 4K
    [InlineData(0, 1920, 1080, 1920, 1080, 0.0)]    // unknown
    public void UpscaleFactorFitsDisplaySizeIntoOsd(double h, double dw, double dh, double ow, double oh, double expected) =>
        Assert.Equal(expected, ShaderLayers.UpscaleFactor(h, dw, dh, ow, oh), 2);

    [Fact]
    public void ActiveLayerTellsLoadedFromWaiting()
    {
        var balanced = ImagePresets.Find("balanced");
        Assert.Equal(LayerState.Shader, ActiveLayer.Classify(balanced, ShaderLayers.Fsrcnnx, 1.5, false));
        Assert.Equal(LayerState.ShaderFellBack, ActiveLayer.Classify(ImagePresets.Find("max"), ShaderLayers.Fsrcnnx, 1.5, true));
        Assert.Equal(LayerState.Waiting, ActiveLayer.Classify(balanced, ShaderLayers.Fsrcnnx, 1.0, false));
        Assert.Equal(LayerState.GpuBehind, ActiveLayer.Classify(balanced, null, 1.5, true));
        Assert.Equal(LayerState.RestoreOnly, ActiveLayer.Classify(ImagePresets.Find("anime"), ShaderLayers.Anime4KFast, 1.0, false));
        Assert.Equal(LayerState.BuiltIn, ActiveLayer.Classify(ImagePresets.Off, null, 2, false));
    }

    static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void FallbackIgnoresGraceThenStepsDownOnSustainedDrops()
    {
        var f = new LayerFallback();
        f.Start("max", 1080, S(0));
        Assert.False(f.Sample(S(1), 50, 2)); // shader compile stall inside the grace period
        long drops = 50;
        var stepped = false;
        for (var t = 5.0; t < 8 && !stepped; t += 0.2) stepped = f.Sample(S(t), drops += 1, 2);
        Assert.True(stepped);
        Assert.Equal(1, f.Level);
        Assert.True(f.HasFallenBack);
    }

    [Fact]
    public void FallbackToleratesOccasionalDrops()
    {
        var f = new LayerFallback();
        f.Start("balanced", 720, S(0));
        for (var i = 0; i < 40; i++) Assert.False(f.Sample(S(5 + i), i, 1)); // one drop per second
        Assert.Equal(0, f.Level);
    }

    [Fact]
    public void FallbackStopsAtBuiltInAndIsRememberedPerHeight()
    {
        var f = new LayerFallback();
        f.Start("balanced", 2160, S(0));
        for (var i = 0; i < 20; i++) f.Sample(S(5 + i * 0.1), i, 1);
        Assert.Equal(1, f.Level);
        Assert.False(f.Sample(S(30), 1000, 1)); // nothing lighter than built-in

        f.Start("balanced", 2160, S(40));
        Assert.Equal(1, f.Level); // same kind of video: start where it coped
        f.Start("balanced", 720, S(50));
        Assert.Equal(0, f.Level);
        f.Forget("balanced");
        f.Start("balanced", 2160, S(60));
        Assert.Equal(0, f.Level);
    }

    static string FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "NuvoraPlayer.slnx"))) return d.FullName;
        throw new DirectoryNotFoundException("NuvoraPlayer.slnx");
    }
}
