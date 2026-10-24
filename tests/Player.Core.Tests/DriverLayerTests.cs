using Player.App;
using Player.Enhancement;

namespace Player.Core.Tests;

public class DriverLayerTests
{
    [Fact]
    public void Filter_PicksVendorModeAndOnlyNvidiaGetsHdr()
    {
        Assert.Equal("d3d11vpp=format=nv12:scale=2:scaling-mode=nvidia", DriverLayer.Filter(DriverVsr.Nvidia, true, false));
        Assert.Equal("d3d11vpp=format=nv12:scale=2:scaling-mode=intel", DriverLayer.Filter(DriverVsr.Intel, true, true));
        Assert.Equal("d3d11vpp=scale=2:scaling-mode=nvidia:nvidia-true-hdr", DriverLayer.Filter(DriverVsr.Nvidia, true, true));
        Assert.Equal("d3d11vpp=nvidia-true-hdr", DriverLayer.Filter(DriverVsr.Nvidia, false, true));
        Assert.Null(DriverLayer.Filter(DriverVsr.Nvidia, false, false));
        Assert.Null(DriverLayer.Filter(DriverVsr.None, true, true));
    }

    [Fact]
    public void Detect_NeedsRtxOrIntel()
    {
        Assert.Equal(DriverVsr.None, DriverLayer.Detect(new GpuInfo("NVIDIA GeForce GTX 1080", GpuInfo.Nvidia)));
        Assert.Equal(DriverVsr.Nvidia, DriverLayer.Detect(new GpuInfo("NVIDIA GeForce RTX 3060", GpuInfo.Nvidia)));
        Assert.Equal(DriverVsr.Intel, DriverLayer.Detect(new GpuInfo("Intel(R) Arc(TM) A770", GpuInfo.Intel)));
        Assert.Equal(DriverVsr.None, DriverLayer.Detect(null));
    }

    [Fact]
    public void IsFailure_IgnoresRemovingAFilterThatIsNotThere()
    {
        Assert.True(DriverLayer.IsFailure("mpv [vf] error: Disabling filter @vsr because it has failed."));
        Assert.True(DriverLayer.IsFailure("mpv command 'vf add @vsr:d3d11vpp=scale=2' failed: error running command"));
        Assert.False(DriverLayer.IsFailure("mpv command 'vf remove @vsr' failed: error running command"));
        Assert.False(DriverLayer.IsFailure("mpv [vo/gpu-next] warn: shader compile (slow!)"));
    }

    [Fact]
    public void UserPresets_RenameRefusesATakenName_SaveOverwritesSameName()
    {
        List<UserPreset> list = [new("Gece", "balanced", 0, 0, -8, 0, 0, 0, 0), new("Spor", "max", 0, 10, 0, 20, 0, 0, 60)];
        Assert.False(UserPresets.Rename(list, "Gece", "spor"));
        Assert.True(UserPresets.Rename(list, "Gece", "GECE")); // case-only change of its own name
        UserPresets.Save(list, list[1] with { Name = "SPOR", Sharpness = 40 });
        Assert.Equal(["GECE", "SPOR"], list.Select(p => p.Name));
        Assert.Equal(40, list[1].Sharpness);
    }
}

// Shares Loc with the other view model tests.
[Collection(nameof(LocTests))]
public class DriverLayerViewModelTests
{
    readonly FakePlayerService _mpv = new();
    readonly PlayerViewModel _vm;

    public DriverLayerViewModelTests()
    {
        _vm = new PlayerViewModel(_mpv, new AppSettings { Language = "tr" }, new GpuInfo("NVIDIA GeForce RTX 3060", GpuInfo.Nvidia));
        // 720p in a 1080p window: upscaled 1.5x
        _mpv.Raise("video-params/dw", 1280.0);
        _mpv.Raise("video-params/dh", 720.0);
        _mpv.Raise("height", 720.0);
        _mpv.Raise("osd-dimensions/w", 1920.0);
        _mpv.Raise("osd-dimensions/h", 1080.0);
    }

    static bool LoadsShaders(FakePlayerService mpv) => mpv.Named("change-list").Any(c => c.Contains("append"));

    [Fact]
    public void UpscaledVideo_UsesRtxVsrInsteadOfShaders()
    {
        Assert.Equal("vf add @vsr:d3d11vpp=format=nv12:scale=2:scaling-mode=nvidia", _mpv.Named("vf")[^1]);
        Assert.Equal("set hwdec d3d11va", _mpv.Named("set").Last(c => c.StartsWith("set hwdec")));
        // Shaders loaded while the window size was unknown are cleared once VSR takes over.
        Assert.Equal("change-list glsl-shaders clr ", _mpv.Named("change-list")[^1]);
        Assert.StartsWith("Sürücü · RTX VSR", _vm.ActiveLayer);
    }

    [Fact]
    public void CpuFramerateFilter_TakesTheDriverLayerOffAndLocksHdr()
    {
        _vm.TargetFps = 60;
        Assert.Contains("vf remove @vsr", _mpv.Named("vf"));
        Assert.Equal("set hwdec auto-copy", _mpv.Named("set").Last(c => c.StartsWith("set hwdec")));
        Assert.False(_vm.RtxHdrAvailable);
    }

    [Fact]
    public void NeuralUltra_KeepsItsReconstructionPassOnTopOfRtxVsr()
    {
        _vm.PresetId = "neural";

        var list = _mpv.Named("change-list");
        var appended = list.Skip(list.FindLastIndex(c => c.EndsWith(" clr ")) + 1).ToList();
        Assert.EndsWith("neural.glsl", Assert.Single(appended)); // VSR upscales; no CNN, but the pass stays
        Assert.StartsWith("Sürücü · RTX VSR · Yapı + Ton", _vm.ActiveLayer);
    }

    [Fact]
    public void DriverFailure_FallsBackToShaders()
    {
        _mpv.Commands.Clear();
        _mpv.RaiseLog("mpv [vf] error: Disabling filter @vsr because it has failed.");
        Assert.Equal(["vf remove @vsr"], _mpv.Named("vf"));
        Assert.True(LoadsShaders(_mpv));
    }
}
