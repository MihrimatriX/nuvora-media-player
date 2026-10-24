using System.Diagnostics;
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

/// <summary>"Görüntü" tab: presets, active layer, 60/90/120 FPS motion interpolation, picture adjustments, original-image bypass.
/// Colour and cinema filters are in .Looks.cs.</summary>
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
    public string GpuNote => Loc.T(
        _vsr != DriverVsr.None && _settings.DriverUpscaling && !_driverFailed ? "gpu.noteDriver"
        : _settings.ShaderUpscaling ? "gpu.noteShaders" : "gpu.noteShadersOff");

    // ---- layer 1: driver VSR / RTX Video HDR (d3d11vpp) ----
    readonly DriverVsr _vsr;
    bool _driverFailed;
    /// <summary>The d3d11vpp filter on the vf chain, null when none.</summary>
    string? _driverFilter;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveLayer), nameof(EnhanceLabel))]
    public partial bool RtxHdr { get; set; }

    /// <summary>RTX Video HDR works on D3D11 frames; the CPU framerate filter (60/90/120 FPS below 4K) rules it out.</summary>
    public bool RtxHdrAvailable => HasRtxVideo && !CpuFps;
    public string RtxHdrTip => Loc.T(!HasRtxVideo ? "panel.rtxNeedsCard" : CpuFps ? "panel.rtxLockedByFps" : "panel.convertsSDRContentToHDR");

    /// <summary>The FFmpeg framerate filter runs on the CPU and needs copy-back decoding (see <see cref="ApplyFps"/>).</summary>
    bool CpuFps => !IsBypassed && TargetFps > 0 && !(_settings.Motion2Uhd4K && _height >= 1400);

    string Hwdec => !_settings.HardwareDecoding ? "no" : CpuFps ? "auto-copy" : _driverFilter is not null ? "d3d11va" : _settings.HwdecMode;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PresetChoices), nameof(ActiveLayer), nameof(EnhanceLabel), nameof(PresetName), nameof(AfterBadgeText))]
    public partial string PresetId { get; set; } = "balanced";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveLayer), nameof(BypassLabel), nameof(EnhanceLabel))]
    public partial bool IsBypassed { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ActiveLayer), nameof(BypassLabel), nameof(EnhanceLabel), nameof(AfterBadgeText))]
    public partial bool IsSplitCompare { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(SplitPercentText))]
    public partial double SplitRatio { get; set; } = 0.5;

    double _videoSplitRatio = 0.5;

    // ---- layer 2: GPU shaders + automatic fallback ----
    readonly LayerFallback _fallback = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    string[] _loadedShaders = [];
    ShaderLayer? _loadedLayer;
    double _osdW, _osdH;

    static string ShaderPath(string relative) => Path.Combine(AppContext.BaseDirectory, "Assets", "Shaders", relative).Replace('\\', '/');

    static string CompareShaderPath => ShaderPath("compare.glsl");

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
    /// <summary>ProfileName prefix of a user preset, followed by its name.</summary>
    public const string UserPrefix = "u:";
    public string ProfileLabel => ProfileName.StartsWith(UserPrefix, StringComparison.Ordinal) ? ProfileName[UserPrefix.Length..] : Loc.T($"profile.{ProfileName}");
    public IReadOnlyList<UserPreset> UserPresetList => _settings.UserPresets;
    /// <summary>The user preset picked last, null when a built-in profile is.</summary>
    public string? CurrentUserPreset => ProfileName.StartsWith(UserPrefix, StringComparison.Ordinal) ? ProfileName[UserPrefix.Length..] : null;
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

    double Upscale => ShaderLayers.UpscaleFactor(_height, _dw, _dh, _osdW, _osdH);

    /// <summary>The shader layer actually handed to mpv (null = built-in scalers only).</summary>
    public string? LoadedShaderName => _loadedLayer?.Name;

    public string ActiveLayer
    {
        get
        {
            if (IsBypassed) return Loc.T("layer.original");
            if (IsSplitCompare) return Loc.F("layer.compare", PresetName);
            var builtIn = LayerLabel(CurrentPreset);
            var layer = DriverLayer.Scales(_driverFilter) ? Loc.F("layer.driver", DriverLayer.Name(_vsr))
                : Player.Enhancement.ActiveLayer.Classify(CurrentPreset, _loadedLayer, Upscale, _fallback.HasFallenBack) switch
            {
                LayerState.GpuBehind => Loc.F("layer.gpuBehind", builtIn),
                LayerState.Shader => Loc.F("layer.shader", _loadedLayer!.Name),
                LayerState.ShaderFellBack => Loc.F("layer.shaderFellBack", _loadedLayer!.Name),
                LayerState.RestoreOnly => Loc.F("layer.restoreOnly", _loadedLayer!.Name),
                LayerState.Waiting => Loc.F("layer.waiting", builtIn, _loadedLayer!.Name),
                _ => builtIn,
            };
            if (DriverLayer.Hdr(_driverFilter)) layer += " · HDR";
            return FpsBadge.Length > 0 ? $"{layer} · {FpsBadge}" : layer;
        }
    }

    /// <summary>Engine name for compact labels: the loaded shader, else the built-in scaler.</summary>
    string Engine => DriverLayer.Scales(_driverFilter) ? DriverLayer.Name(_vsr) : _loadedLayer?.Name ?? CurrentPreset.Scaler;

    public string EnhanceLabel
    {
        get
        {
            if (IsBypassed) return Loc.T("enhance.originalImage");
            if (IsSplitCompare) return Loc.F("layer.compare", PresetName);
            var baseLabel = CurrentPreset.Id == "off" ? Loc.T("enhance.off") : $"{PresetName} · {Engine}";
            return FpsBadge.Length > 0 ? $"{baseLabel} · {FpsBadge}" : baseLabel;
        }
    }

    public string AfterBadgeText
    {
        get
        {
            var preset = CurrentPreset.Id == "off" ? Loc.T("preset.balanced") : PresetName;
            if (HasLook) return $"{preset} · {LookName}";
            if (ProfileName != "default") return $"{preset} · {ProfileLabel}";
            if (Math.Abs(Sharpness) > 0.5 || Math.Abs(Saturation) > 0.5 || Math.Abs(Contrast) > 0.5)
                return $"{preset} · {Loc.F("badge.sharpness", SharpnessText)}";
            return FpsBadge.Length > 0 ? $"{preset} · {FpsBadge}" : $"{preset} · {Engine}";
        }
    }

    public string BypassLabel => Loc.T(IsSplitCompare ? "bypass.close" : "bypass.open");
    public string ContrastText => Signed(Contrast);
    public string SaturationText => Signed(Saturation);
    public string BrightnessText => Signed(Brightness);
    public string SharpnessText => Signed(Sharpness);
    public string GammaText => Signed(Gamma);
    public string HueText => Signed(Hue);

    [RelayCommand]
    void SelectPreset(string id)
    {
        if (id != PresetId) { PresetId = id; return; }
        // Picking the active preset again retries the driver layer and the full shader chain after an automatic fallback.
        if (!_fallback.HasFallenBack && !_driverFailed) return;
        _driverFailed = false;
        _fallback.Forget(id);
        RestartFallback();
        ApplyImage();
    }
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
        if (IsSplitCompare && !IsBypassed) UpdateShaderOpts();
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

    /// <summary>Saves the current picture state under <paramref name="name"/> (overwrites a preset of that name).</summary>
    public bool SaveUserPreset(string name)
    {
        name = UserPresets.CleanName(name);
        if (name.Length == 0) return false;
        UserPresets.Save(_settings.UserPresets, new(name, PresetId, Contrast, Saturation, Brightness, Sharpness, Gamma, Hue, TargetFps,
            LookId, LookStrength, Grain, Vignette, Letterbox));
        _settings.Save();
        ProfileName = UserPrefix + name;
        return true;
    }

    public void ApplyUserPreset(UserPreset p)
    {
        PresetId = ImagePresets.Find(p.BasePreset).Id;
        (Contrast, Saturation, Brightness, Sharpness, Gamma, Hue) = (p.Contrast, p.Saturation, p.Brightness, p.Sharpness, p.Gamma, p.Hue);
        TargetFps = p.TargetFps;
        (LookId, LookStrength, Grain, Vignette, Letterbox) = (ColorLooks.Find(p.Look).Id, p.LookStrength, p.Grain, p.Vignette, p.Letterbox);
        ProfileName = UserPrefix + p.Name;
    }

    public bool RenameUserPreset(string from, string to)
    {
        to = UserPresets.CleanName(to);
        if (!UserPresets.Rename(_settings.UserPresets, from, to)) return false;
        _settings.Save();
        if (CurrentUserPreset == from) ProfileName = UserPrefix + to;
        return true;
    }

    public void DeleteUserPreset(string name)
    {
        if (!UserPresets.Delete(_settings.UserPresets, name)) return;
        _settings.Save();
        if (CurrentUserPreset == name) ProfileName = "default";
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

    partial void OnPresetIdChanged(string value) { RestartFallback(); ApplyImage(); }
    partial void OnRtxHdrChanged(bool value)
    {
        if (value != _settings.RtxVideoHdr) { _settings.RtxVideoHdr = value; _settings.Save(); }
        ApplyImage();
    }
    partial void OnIsBypassedChanged(bool value) { ApplyImage(); ApplyFps(); }
    partial void OnIsSplitCompareChanged(bool value) => ApplyImage();
    partial void OnSplitRatioChanged(double value)
    {
        if (Math.Abs(_videoSplitRatio - value) > 0.0001) _videoSplitRatio = value;
        if (IsSplitCompare && !IsBypassed) UpdateShaderOpts();
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
        var activeFps = IsBypassed ? 0 : TargetFps;
        string? graph = null;
        if (activeFps > 0)
        {
            if (!CpuFps)
            {
                // Motion² UHD/4K: keep zero-copy GPU decoding and run temporal interpolation 100% on GPU shaders.
                Set("override-display-fps", Inv(activeFps));
            }
            else
            {
                // The FFmpeg framerate filter gets hardware-decoded frames through copyback (see Hwdec).
                Set("override-display-fps", "0");
                graph = $"framerate=fps={activeFps}:interp_start=15:interp_end=240:scene=8.2";
            }
            Set("interpolation", "yes");
            Set("video-sync", "display-resample");
            Set("tscale", _settings.MotionScaler);
        }
        else if (activeFps == -1)
        {
            Set("override-display-fps", "0");
            Set("interpolation", "yes");
            Set("video-sync", "display-resample");
            Set("tscale", _settings.MotionScaler);
        }
        else
        {
            Set("override-display-fps", "0");
            Set("interpolation", "no");
            Set("video-sync", "audio");
        }
        // The CPU framerate filter must never see GPU frames: drop it before leaving copyback, add it after entering.
        if (graph is null) SetFilter("vf", "fps", null);
        ApplyImage(); // CPU frames rule the driver layer out, and the driver layer picks the decoder
        if (graph is not null) SetFilter("vf", "fps", graph);
    }

    /// <summary>Layer 1. Puts d3d11vpp on the vf chain when the GPU has VSR / RTX Video HDR and nothing rules it out,
    /// and sets the decoder to match.</summary>
    void ApplyDriver()
    {
        var upscale = _settings.DriverUpscaling && CurrentPreset.Id != "off" && Upscale > DriverLayer.MinUpscale;
        _driverFilter = _driverFailed || IsBypassed || IsSplitCompare || CpuFps ? null : DriverLayer.Filter(_vsr, upscale, RtxHdr);
        SetMpvFilter("vf", DriverLayer.Label, _driverFilter);
        Set("hwdec", Hwdec);
        // Lets mpv switch the swapchain to HDR (when Windows HDR is on) for RTX Video HDR's output.
        Set("target-colorspace-hint", DriverLayer.Hdr(_driverFilter) ? "yes" : "no");
    }

    void OnDriverFailed(string line)
    {
        var name = DriverLayer.Name(_vsr);
        _driverFailed = true;
        AppLog.Warn($"{name} unavailable, using the shader layer: {line}");
        ApplyImage();
        OsdRequested?.Invoke(Loc.F("osd.driverFallback", name));
    }

    /// <summary>Applies scaler presets, picture adjustments, or the interactive split-screen comparison shader.</summary>
    void ApplyImage()
    {
        ApplyDriver();
        if (IsSplitCompare && !IsBypassed)
        {
            var splitPreset = CurrentPreset.Id == "off" ? ImagePresets.Find("balanced") : CurrentPreset;
            foreach (var (k, v) in splitPreset.Options) Set(k, v);
            // Neutralize global mpv picture properties so left side ("ÖNCE") is untouched and right side ("SONRA") is driven by compare.glsl.
            Set("contrast", "0");
            Set("saturation", "0");
            Set("brightness", "0");
            Set("gamma", "0");
            Set("hue", "0");
            Set("sharpen", "0");

            // The split view compares picture adjustments and filters only; CNN layers would land on both halves.
            SetShaders([.. File.Exists(CompareShaderPath) ? [CompareShaderPath] : Array.Empty<string>(), .. LookShaders()], null);
            UpdateShaderOpts();
            return;
        }

        // Driver VSR already doubled the frame: a shader upscaler on top would only cost GPU time.
        ShaderLayer? layer = null;
        var upscalers = IsBypassed || DriverLayer.Scales(_driverFilter) ? [] : DesiredShaders(out layer);
        // Filters go last so they grade the upscaled picture; they also run on top of driver VSR.
        SetShaders([.. upscalers, .. LookShaders()], layer);
        UpdateShaderOpts();

        var preset = IsBypassed ? ImagePresets.Off : CurrentPreset;
        foreach (var (k, v) in preset.Options) Set(k, v);
        Set("contrast", Inv(IsBypassed ? 0 : Contrast));
        Set("saturation", Inv(IsBypassed ? 0 : Saturation));
        Set("brightness", Inv(IsBypassed ? 0 : Brightness));
        Set("gamma", Inv(IsBypassed ? 0 : Gamma));
        Set("hue", Inv(IsBypassed ? 0 : Hue));
        // Preset base sharpness + user slider (-50..100 mapped to -1.0..+2.5 in mpv's GPU sharpen filter).
        var presetSharp = IsBypassed ? 0 : preset.Id switch { "anime" => 0.45, "max" => 0.35, "balanced" => 0.20, _ => 0 };
        var sharp = IsBypassed ? 0 : Math.Clamp(presetSharp + Sharpness / 50.0, -1.0, 2.5);
        Set("sharpen", Inv(sharp));
    }

    /// <summary>compare.glsl's own options; "split" is added by <see cref="UpdateShaderOpts"/>.</summary>
    string CompareShaderOpts()
    {
        var preset = CurrentPreset.Id == "off" ? ImagePresets.Find("balanced") : CurrentPreset;
        var presetSharp = preset.Id switch { "anime" => 0.85, "max" => 0.70, "balanced" => 0.50, _ => 0.35 };
        var sharp = Math.Clamp(presetSharp + Sharpness / 35.0, -1.5, 3.5);
        var presetBoost = preset.Id switch { "anime" => 0.85, "max" => 0.75, "balanced" => 0.50, _ => 0.25 };
        return $"sharp={Inv(sharp)},cont={Inv(Contrast)},sat={Inv(Saturation)},bright={Inv(Brightness)},gam={Inv(Gamma)},hue_deg={Inv(Hue)},preset_boost={Inv(presetBoost)}";
    }

    /// <summary>Files of the chain level the fallback allows; missing files (stripped install) mean built-in.</summary>
    string[] DesiredShaders(out ShaderLayer? layer)
    {
        layer = null;
        var chain = CurrentPreset.Shaders;
        if (!_settings.ShaderUpscaling || _fallback.Level >= chain.Count) return [];
        var candidate = chain[_fallback.Level];
        var files = candidate.Files.Select(ShaderPath).ToArray();
        if (files.FirstOrDefault(f => !File.Exists(f)) is { } missing)
        {
            AppLog.Warn($"Shader missing, using built-in scaler: {missing}");
            return [];
        }
        layer = candidate;
        return files;
    }

    void SetShaders(string[] files, ShaderLayer? layer)
    {
        var changed = !files.SequenceEqual(_loadedShaders);
        _loadedLayer = layer;
        if (changed)
        {
            // One path per command: glsl-shaders is a path list whose separator differs per OS (';' on Windows).
            _player.Command("change-list", "glsl-shaders", "clr", "");
            foreach (var f in files) _player.Command("change-list", "glsl-shaders", "append", f);
            _loadedShaders = files;
            _fallback.Rebase(_clock.Elapsed); // compiling shaders stalls a few frames; don't count those
        }
        NotifyLayer();
    }

    void NotifyLayer()
    {
        OnPropertyChanged(nameof(ActiveLayer));
        OnPropertyChanged(nameof(LoadedShaderName));
        OnPropertyChanged(nameof(EnhanceLabel));
        OnPropertyChanged(nameof(AfterBadgeText));
        OnPropertyChanged(nameof(TechInfo));
        OnPropertyChanged(nameof(GpuNote));
        OnPropertyChanged(nameof(RtxHdrAvailable));
        OnPropertyChanged(nameof(RtxHdrTip));
    }

    void RestartFallback() => _fallback.Start(PresetId, (int)_height, _clock.Elapsed);

    /// <summary>mpv properties that drive the shader layer; called from <see cref="OnPlayerProperty"/>.</summary>
    void OnEnhancementProperty(string name, object? value)
    {
        switch (name)
        {
            // The window size decides whether there is anything for driver VSR to upscale.
            case "osd-dimensions/w": _osdW = value as double? ?? 0; if (_vsr != DriverVsr.None) ApplyImage(); else NotifyLayer(); break;
            case "osd-dimensions/h": _osdH = value as double? ?? 0; if (_vsr != DriverVsr.None) ApplyImage(); else NotifyLayer(); break;
            case "seeking": _fallback.Rebase(_clock.Elapsed); break;
            case "frame-drop-count" when value is double drops && _settings.AutoFallback && _loadedLayer is not null:
                var from = _loadedLayer.Name;
                if (!_fallback.Sample(_clock.Elapsed, (long)drops, CurrentPreset.Shaders.Count)) break;
                ApplyImage();
                var to = _loadedLayer?.Name ?? LayerLabel(CurrentPreset);
                AppLog.Warn($"GPU fell behind ({(long)drops} dropped frames, {_height:0}p): {from} -> {to}");
                OsdRequested?.Invoke(Loc.F("osd.gpuFallback", from, to));
                break;
        }
    }

    static string Signed(double v) => v > 0 ? $"+{v:0}" : $"{v:0}";
}
