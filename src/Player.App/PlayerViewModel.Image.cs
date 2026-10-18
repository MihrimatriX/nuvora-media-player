using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.Enhancement;

namespace Player.App;

public sealed record PresetChoice(ImagePreset Preset, bool IsSelected)
{
    public string Name => PlayerViewModel.PresetLabel(Preset);
    public string Description => Loc.T($"preset.{Preset.Id}.desc");
}
public sealed record FpsChoice(int Value, string Label, bool IsSelected);

/// <summary>"Görüntü" tab: presets, active layer, 60/90/120 FPS motion interpolation, picture adjustments, original-image bypass.</summary>
public sealed partial class PlayerViewModel
{
    /// <summary>Colour profile ids; the label is Loc "profile.{id}".</summary>
    public static readonly string[] Profiles = ["default", "vivid", "cinema", "anime", "evening"];
    public static readonly (int Value, string Label)[] FpsTargets =
        [(0, "fps.source"), (60, "60 FPS"), (90, "90 FPS"), (120, "120 FPS"), (-1, "VSync")]; // labels go through Loc.T

    public GpuInfo? Gpu { get; }
    public string GpuName => Gpu?.Name ?? Loc.T("gpu.notDetected");
    public bool HasRtxVideo => Gpu?.HasRtxVideo == true;
    public string RtxBadge => Loc.T(HasRtxVideo ? "gpu.rtxReady" : "gpu.rtxNeeded");
    public string GpuNote => Loc.T(HasRtxVideo ? "gpu.noteRtx" : "gpu.noteDefault");

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PresetChoices), nameof(ActiveLayer), nameof(EnhanceLabel), nameof(PresetName), nameof(AfterBadgeText))]
    public partial string PresetId { get; set; } = "balanced";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveLayer), nameof(BypassLabel), nameof(EnhanceLabel))]
    public partial bool IsBypassed { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveLayer), nameof(BypassLabel), nameof(EnhanceLabel), nameof(AfterBadgeText))]
    public partial bool IsSplitCompare { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SplitPercentText))]
    public partial double SplitRatio { get; set; } = 0.5;

    double _videoSplitRatio = 0.5;
    bool _shaderLoaded;

    static string CompareShaderPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", "compare.glsl").Replace('\\', '/');

    [ObservableProperty, NotifyPropertyChangedFor(nameof(FpsChoices), nameof(FpsBadge), nameof(ActiveLayer), nameof(EnhanceLabel), nameof(SmoothMotion), nameof(AfterBadgeText))]
    public partial int TargetFps { get; set; }

    public bool SmoothMotion
    {
        get => TargetFps != 0;
        set => TargetFps = value ? (TargetFps != 0 ? TargetFps : 60) : 0;
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ContrastText), nameof(AfterBadgeText))] public partial double Contrast { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SaturationText), nameof(AfterBadgeText))] public partial double Saturation { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BrightnessText), nameof(AfterBadgeText))] public partial double Brightness { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SharpnessText), nameof(AfterBadgeText))] public partial double Sharpness { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(GammaText), nameof(AfterBadgeText))] public partial double Gamma { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HueText), nameof(AfterBadgeText))] public partial double Hue { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AfterBadgeText), nameof(ProfileLabel))] public partial string ProfileName { get; set; } = "default";
    public string ProfileLabel => Loc.T($"profile.{ProfileName}");
    public static string PresetLabel(ImagePreset preset) => Loc.T($"preset.{preset.Id}");
    static string LayerLabel(ImagePreset preset) => preset.Id == "off" ? Loc.T("preset.off") : Loc.F("preset.layerBuiltIn", preset.Scaler);

    ImagePreset CurrentPreset => ImagePresets.Find(PresetId);
    public IEnumerable<PresetChoice> PresetChoices => ImagePresets.All.Select(p => new PresetChoice(p, p.Id == PresetId));
    public IEnumerable<FpsChoice> FpsChoices => FpsTargets.Select(f => new FpsChoice(f.Value, Loc.T(f.Label), f.Value == TargetFps));
    public string PresetName => PresetLabel(CurrentPreset);
    public string SplitPercentText => $"%{(SplitRatio * 100):0}";

    public string FpsBadge => TargetFps switch
    {
        > 0 => $"{TargetFps} FPS",
        -1 => "VSync",
        _ => "",
    };

    public string ActiveLayer => IsBypassed
        ? Loc.T("layer.original")
        : IsSplitCompare
            ? Loc.F("layer.compare", PresetName)
            : FpsBadge.Length > 0 ? $"{LayerLabel(CurrentPreset)} · {FpsBadge}" : LayerLabel(CurrentPreset);

    public string EnhanceLabel
    {
        get
        {
            if (IsBypassed) return Loc.T("enhance.originalImage");
            if (IsSplitCompare) return Loc.F("layer.compare", PresetName);
            var baseLabel = CurrentPreset.Id == "off" ? Loc.T("enhance.off") : $"{PresetName} · {CurrentPreset.Scaler}";
            return FpsBadge.Length > 0 ? $"{baseLabel} · {FpsBadge}" : baseLabel;
        }
    }

    public string AfterBadgeText
    {
        get
        {
            var preset = CurrentPreset.Id == "off" ? Loc.T("preset.balanced") : PresetName;
            if (ProfileName != "default") return $"{preset} · {ProfileLabel}";
            if (Math.Abs(Sharpness) > 0.5 || Math.Abs(Saturation) > 0.5 || Math.Abs(Contrast) > 0.5)
                return $"{preset} · {Loc.F("badge.sharpness", SharpnessText)}";
            return FpsBadge.Length > 0 ? $"{preset} · {FpsBadge}" : $"{preset} · {CurrentPreset.Scaler}";
        }
    }

    public string BypassLabel => Loc.T(IsSplitCompare ? "bypass.close" : "bypass.open");
    public string ContrastText => Signed(Contrast);
    public string SaturationText => Signed(Saturation);
    public string BrightnessText => Signed(Brightness);
    public string SharpnessText => Signed(Sharpness);
    public string GammaText => Signed(Gamma);
    public string HueText => Signed(Hue);

    [RelayCommand] void SelectPreset(string id) => PresetId = id;
    [RelayCommand] void SelectFps(int fps) => TargetFps = fps;

    [RelayCommand]
    public void ToggleBypass() => ToggleSplitCompare();

    [RelayCommand]
    public void ToggleSplitCompare()
    {
        if (!IsSplitCompare && PresetId == "off")
            PresetId = "balanced";
        IsSplitCompare = !IsSplitCompare;
    }

    public void SetSplitPosition(double windowRatio, double videoRatio)
    {
        SplitRatio = Math.Clamp(windowRatio, 0.04, 0.96);
        _videoSplitRatio = Math.Clamp(videoRatio, 0.0, 1.0);
        if (IsSplitCompare && !IsBypassed) UpdateCompareShaderOpts();
    }

    [RelayCommand]
    public void CycleFps()
    {
        var idx = Array.FindIndex(FpsTargets, f => f.Value == TargetFps);
        TargetFps = FpsTargets[(idx + 1) % FpsTargets.Length].Value;
    }

    [RelayCommand]
    public void ResetPicture()
    {
        ProfileName = "default";
        (Contrast, Saturation, Brightness, Sharpness, Gamma, Hue) = (0, 0, 0, 0, 0, 0);
    }

    public void ApplyProfile(string name)
    {
        ProfileName = name;
        (Contrast, Saturation, Brightness, Sharpness, Gamma, Hue) = name switch
        {
            "vivid" => (6, 18, 0, 35, 2, 0),
            "cinema" => (8, 6, -2, 15, -3, -3),
            "anime" => (6, 22, 1, 45, 0, 0),
            "evening" => (4, -6, -8, 0, -4, -2),
            _ => (0, 0, 0, 0, 0, 0),
        };
    }

    partial void OnPresetIdChanged(string value) => ApplyImage();
    partial void OnIsBypassedChanged(bool value) { ApplyImage(); ApplyFps(); }
    partial void OnIsSplitCompareChanged(bool value) => ApplyImage();
    partial void OnSplitRatioChanged(double value)
    {
        if (Math.Abs(_videoSplitRatio - value) > 0.0001) _videoSplitRatio = value;
        if (IsSplitCompare && !IsBypassed) UpdateCompareShaderOpts();
    }
    partial void OnTargetFpsChanged(int value) { ApplyFps(); OnPropertyChanged(nameof(TechInfo)); }
    partial void OnContrastChanged(double value) => ApplyImage();
    partial void OnSaturationChanged(double value) => ApplyImage();
    partial void OnBrightnessChanged(double value) => ApplyImage();
    partial void OnSharpnessChanged(double value) => ApplyImage();
    partial void OnGammaChanged(double value) => ApplyImage();
    partial void OnHueChanged(double value) => ApplyImage();

    void ApplyFps()
    {
        _player.Command("vf", "remove", "@fps");
        var activeFps = IsBypassed ? 0 : TargetFps;
        if (activeFps > 0)
        {
            var useGpuMotion2 = _settings.Motion2Uhd4K && _height >= 1400;
            if (useGpuMotion2)
            {
                // Motion² UHD/4K: keep zero-copy GPU decoding and run temporal interpolation 100% on GPU shaders.
                _player.Command("set", "hwdec", _settings.HardwareDecoding ? _settings.HwdecMode : "no");
                _player.Command("set", "override-display-fps", Inv(activeFps));
            }
            else
            {
                // Allow hardware decoder to feed the FFmpeg framerate filter via copyback when needed.
                if (_settings.HardwareDecoding) _player.Command("set", "hwdec", "auto-copy");
                _player.Command("set", "override-display-fps", "0");
                _player.Command("vf", "add", $"@fps:lavfi=[framerate=fps={activeFps}:interp_start=15:interp_end=240:scene=8.2]");
            }
            _player.Command("set", "interpolation", "yes");
            _player.Command("set", "video-sync", "display-resample");
            _player.Command("set", "tscale", _settings.MotionScaler);
        }
        else if (activeFps == -1)
        {
            _player.Command("set", "hwdec", _settings.HardwareDecoding ? _settings.HwdecMode : "no");
            _player.Command("set", "override-display-fps", "0");
            _player.Command("set", "interpolation", "yes");
            _player.Command("set", "video-sync", "display-resample");
            _player.Command("set", "tscale", _settings.MotionScaler);
        }
        else
        {
            _player.Command("set", "hwdec", _settings.HardwareDecoding ? _settings.HwdecMode : "no");
            _player.Command("set", "override-display-fps", "0");
            _player.Command("set", "interpolation", "no");
            _player.Command("set", "video-sync", "audio");
        }
    }

    /// <summary>Applies scaler presets, picture adjustments, or the interactive split-screen comparison shader.</summary>
    void ApplyImage()
    {
        if (IsSplitCompare && !IsBypassed)
        {
            var splitPreset = CurrentPreset.Id == "off" ? ImagePresets.Find("balanced") : CurrentPreset;
            foreach (var (k, v) in splitPreset.Options) _player.Command("set", k, v);
            // Neutralize global mpv picture properties so left side ("ÖNCE") is untouched and right side ("SONRA") is driven by compare.glsl.
            _player.Command("set", "contrast", "0");
            _player.Command("set", "saturation", "0");
            _player.Command("set", "brightness", "0");
            _player.Command("set", "gamma", "0");
            _player.Command("set", "hue", "0");
            _player.Command("set", "sharpen", "0");

            if (!_shaderLoaded && File.Exists(CompareShaderPath))
            {
                _player.Command("change-list", "glsl-shaders", "set", CompareShaderPath);
                _shaderLoaded = true;
            }
            UpdateCompareShaderOpts();
            return;
        }

        if (_shaderLoaded)
        {
            _player.Command("change-list", "glsl-shaders", "clr", "");
            _shaderLoaded = false;
        }

        var preset = IsBypassed ? ImagePresets.Off : CurrentPreset;
        foreach (var (k, v) in preset.Options) _player.Command("set", k, v);
        _player.Command("set", "contrast", Inv(IsBypassed ? 0 : Contrast));
        _player.Command("set", "saturation", Inv(IsBypassed ? 0 : Saturation));
        _player.Command("set", "brightness", Inv(IsBypassed ? 0 : Brightness));
        _player.Command("set", "gamma", Inv(IsBypassed ? 0 : Gamma));
        _player.Command("set", "hue", Inv(IsBypassed ? 0 : Hue));
        // Preset base sharpness + user slider (-50..100 mapped to -1.0..+2.5 in mpv's GPU sharpen filter).
        var presetSharp = IsBypassed ? 0 : preset.Id switch { "anime" => 0.45, "max" => 0.35, "balanced" => 0.20, _ => 0 };
        var sharp = IsBypassed ? 0 : Math.Clamp(presetSharp + Sharpness / 50.0, -1.0, 2.5);
        _player.Command("set", "sharpen", Inv(sharp));
    }

    void UpdateCompareShaderOpts()
    {
        var preset = CurrentPreset.Id == "off" ? ImagePresets.Find("balanced") : CurrentPreset;
        var presetSharp = preset.Id switch { "anime" => 0.85, "max" => 0.70, "balanced" => 0.50, _ => 0.35 };
        var sharp = Math.Clamp(presetSharp + Sharpness / 35.0, -1.5, 3.5);
        var presetBoost = preset.Id switch { "anime" => 0.85, "max" => 0.75, "balanced" => 0.50, _ => 0.25 };
        var opts = $"split={Inv(_videoSplitRatio)},sharp={Inv(sharp)},cont={Inv(Contrast)},sat={Inv(Saturation)},bright={Inv(Brightness)},gam={Inv(Gamma)},hue_deg={Inv(Hue)},preset_boost={Inv(presetBoost)}";
        _player.Command("set", "glsl-shader-opts", opts);
    }

    static string Signed(double v) => v > 0 ? $"+{v:0}" : $"{v:0}";
}
