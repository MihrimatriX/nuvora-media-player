using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.Enhancement;

namespace Player.App;

/// <summary>One row of the "Kısayollar" page.</summary>
public sealed partial class ShortcutItem(Shortcuts.Action action, string binding) : ObservableObject
{
    public Shortcuts.Action Action { get; } = action;
    public string Label => Loc.T(Action.Label);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(KeyText))] public partial string Binding { get; set; } = binding;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(KeyText))] public partial bool IsCapturing { get; set; }
    public string KeyText => IsCapturing ? Loc.T("shortcut.pressKey") : Shortcuts.Display(Binding);
}

/// <summary>Edits a copy of the settings; nothing changes until "Kaydet".</summary>
public sealed partial class SettingsViewModel(PlayerViewModel player) : ObservableObject
{
    public static readonly string[] Accents = ["#6D5AE6", "#00A2FF", "#D6245F", "#0F766E", "#C2410C"];
    public static readonly string[] AccentNames = ["accent.iris", "accent.ice", "accent.raspberry", "accent.teal", "accent.amber"]; // Loc keys
    public static readonly int[] SeekSteps = [3, 5, 10, 15, 30, 60];
    public static readonly int[] VolumeSteps = [1, 2, 5, 10];
    public static readonly double[] SubtitleScales = [0.7, 0.85, 1, 1.15, 1.3, 1.5, 1.8];
    // Option labels are Loc keys (technical ones such as "16:9" fall back to the key text itself).
    public static readonly (int Value, string Label)[] SubtitlePositions =
        [(100, "opt.normal100"), (95, "opt.slightlyUp95"), (90, "opt.up90"), (85, "opt.higher85")];
    public static readonly (string Value, string Label)[] WheelActions =
        [("volume", "opt.volume"), ("seek", "opt.seek"), ("speed", "opt.speed")];
    public static readonly (string Value, string Label)[] LoopModes =
        [("off", "opt.off"), ("file", "opt.repeatFile"), ("playlist", "opt.repeatPlaylist")];
    public static readonly (string Value, string Label)[] HwdecModes =
        [("auto-safe", "opt.hwAuto"), ("nvdec", "NVIDIA PureVideo (NVDEC)"), ("d3d11va", "Direct3D 11 (d3d11va)"), ("dxva2", "opt.hwQsv"), ("auto-copy", "opt.hwAutoCopy")];
    public static readonly (string Value, string Label)[] DeinterlaceModes =
        [("auto", "opt.autoBest"), ("yes", "opt.alwaysOn"), ("no", "opt.off")];
    public static readonly (string Value, string Label)[] ScreenFormats =
        [("auto", "opt.auto"), ("16:9", "16:9"), ("16:10", "16:10"), ("21:9", "opt.cinema219"), ("4:3", "4:3")];
    public static readonly (string Value, string Label)[] DisplayFormats =
        [("normal", "opt.normalFit"), ("stretch", "opt.stretch"), ("zoom", "opt.zoomCrop")];
    public static readonly (string Value, string Label)[] CinematicFormats =
        [("normal", "opt.letterbox"), ("zoom", "opt.fillCrop"), ("half-zoom", "opt.softZoom")];
    public static readonly (string Value, string Label)[] VideoOutputs =
        [("gpu-next", "opt.voGpuNext"), ("gpu", "opt.voGpu")];
    public static readonly (int Value, string Label)[] FpsOptions =
        [(0, "opt.sourceFps"), (60, "60 FPS"), (90, "90 FPS"), (120, "120 FPS"), (-1, "opt.vsync")];
    public static readonly (string Value, string Label)[] MotionScalers =
        [("oversample", "opt.oversample"), ("mitchell", "opt.mitchell"), ("sphinx", "opt.sphinx"), ("catmull_rom", "Catmull-Rom")];
    public static readonly (string Value, string Label)[] ToneMappings =
        [("auto", "opt.auto"), ("bt.2446a", "BT.2446a (ITU)"), ("spline", "Spline"), ("hable", "opt.hable"), ("mobius", "Mobius")];
    public static readonly (string Value, string Label)[] AudioOutputModes =
        [("stereo", "Stereo (2.0)"), ("stereo-surround", "Stereo Surround"), ("5.1", "opt.surround51"), ("7.1", "opt.surround71"), ("spdif", "S/PDIF Bitstream"), ("hdmi", "HDMI Bitstream")];
    public static readonly (string Value, string Label)[] ExportEncoders =
        [("nvenc", "NVIDIA® NVENC"), ("amf", "AMD AMF / VCE"), ("qsv", "Intel® QuickSync (QSV)"), ("cpu", "opt.cpuEncoder")];
    public static readonly (string Value, string Label)[] ExportDeinterlaceModes =
        [("auto", "opt.autoBest"), ("disabled", "opt.off")];
    public static readonly string[] ScreenshotFormats = ["png", "jpg", "webp"];
    public static readonly (string Value, string Label)[] SubtitleColors =
        [("#FFFFFFFF", "opt.white"), ("#FFFFE14D", "opt.yellow"), ("#FFD6E4FF", "opt.lightBlue"), ("#FF80F5FF", "opt.cyan"), ("#FFA8FFB2", "opt.lightGreen")];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PageTitle))] public partial string Page { get; set; } = "genel";
    [ObservableProperty] public partial AppSettings Draft { get; set; } = player.Settings.Clone();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DefaultStatus))] public partial bool IsDefaultRegistered { get; set; }
    public ObservableCollection<ShortcutItem> ShortcutItems { get; } = [];
    public ShortcutItem? Capturing => ShortcutItems.FirstOrDefault(s => s.IsCapturing);

    public PlayerViewModel Player => player;
    public bool CanRegisterDefault => OperatingSystem.IsWindows();
    public int SupportedFormatCount => OperatingSystem.IsWindows() ? FileAssociation.Extensions.Length : 80;
    public string PageTitle => Page switch
    {
        "video" => Loc.T("settings.page.video"),
        "ses" => Loc.T("settings.page.audio"),
        "altyazi" => Loc.T("settings.subtitles"),
        "disaaktar" => Loc.T("settings.page.export"),
        "kisayollar" => Loc.T("settings.keyboardShortcuts"),
        "hakkinda" => Loc.T("settings.about"),
        _ => Loc.T("settings.generalSettings"),
    };
    public string ThemeText
    {
        get
        {
            var idx = Array.FindIndex(Accents, a => string.Equals(a, Draft.Accent, StringComparison.OrdinalIgnoreCase));
            return Loc.T(idx >= 0 ? AccentNames[idx] : AccentNames[0]);
        }
    }
    public string SeekStepText => Loc.F("unit.seconds", Draft.SeekStepSeconds);
    public string VolumeStepText => $"%{Draft.VolumeStep}";
    public string LanguageName => Loc.T($"lang.{Draft.Language}");
    public string WheelActionText => Label(WheelActions, Draft.WheelAction, "opt.volume");
    public string LoopModeText => Label(LoopModes, Draft.LoopMode, "opt.off");
    public string HwdecModeText => Label(HwdecModes, Draft.HwdecMode, Draft.HwdecMode);
    public string DeinterlaceModeText => Label(DeinterlaceModes, Draft.DeinterlaceMode, "opt.autoBest");
    public string ScreenFormatText => Label(ScreenFormats, Draft.ScreenFormat, Draft.ScreenFormat);
    public string DisplayFormat43Text => Label(DisplayFormats, Draft.DisplayFormat43, "opt.normalFit");
    public string DisplayFormat169Text => Label(DisplayFormats, Draft.DisplayFormat169, "opt.normalFit");
    public string DisplayFormatCinematicText => Label(CinematicFormats, Draft.DisplayFormatCinematic, "opt.letterbox");
    public string TargetFpsText => Label(FpsOptions, Draft.TargetFps, "opt.sourceFps");
    public string MotionScalerText => Label(MotionScalers, Draft.MotionScaler, Draft.MotionScaler);
    public string ToneMappingText => Label(ToneMappings, Draft.ToneMapping, Draft.ToneMapping);
    public string SubtitleScaleText => $"%{(Draft.SubtitleScale * 100).ToString("0", CultureInfo.InvariantCulture)}";
    public string SubtitlePosText => Label(SubtitlePositions, Draft.SubtitlePosition, $"%{Draft.SubtitlePosition}");
    public string DefaultPresetText => PlayerViewModel.PresetLabel(ImagePresets.Find(Draft.DefaultPreset));
    public string VideoOutputText => Label(VideoOutputs, Draft.VideoOutput, Draft.VideoOutput);
    public string ScreenshotFormatText => Draft.ScreenshotFormat.ToUpperInvariant();
    public string SubtitleColorText => Label(SubtitleColors, Draft.SubtitleColor, "opt.custom");
    public string AudioOutputModeText => Label(AudioOutputModes, Draft.AudioOutputMode, "Stereo Surround");
    public string AudioDeviceText => player.AudioDevices.FirstOrDefault(d => d.Name == Draft.AudioDevice)?.Description ?? Loc.T("opt.auto");
    public string SpdifDeviceText => player.AudioDevices.FirstOrDefault(d => d.Name == Draft.SpdifDevice)?.Description ?? Loc.T("opt.autoSpdif");
    public string HdmiDeviceText => player.AudioDevices.FirstOrDefault(d => d.Name == Draft.HdmiDevice)?.Description ?? Loc.T("opt.autoHdmi");
    public string ExportEncoderText => Label(ExportEncoders, Draft.ExportEncoder, "NVIDIA® NVENC");
    public string ExportDeinterlaceText => Label(ExportDeinterlaceModes, Draft.ExportDeinterlace, "opt.autoBest");

    static string Label<TValue>((TValue Value, string Label)[] options, TValue value, string fallback) =>
        Loc.T(options.FirstOrDefault(o => EqualityComparer<TValue>.Default.Equals(o.Value, value)).Label ?? fallback);

    public double LipsyncValue
    {
        get => Draft.LipsyncMs;
        set
        {
            var ms = (int)Math.Round(value / 10.0) * 10;
            if (Draft.LipsyncMs == ms) return;
            Draft.LipsyncMs = ms;
            player.SetAudioDelayMs(ms);
            OnPropertyChanged(nameof(LipsyncValue));
            OnPropertyChanged(nameof(LipsyncText));
        }
    }
    public string LipsyncText => Draft.LipsyncMs == 0 ? "0 ms" : (Draft.LipsyncMs > 0 ? $"+{Draft.LipsyncMs} ms" : $"−{-Draft.LipsyncMs} ms");

    [RelayCommand]
    void ResetLipsync() => LipsyncValue = 0;

    public string DefaultStatus => IsDefaultRegistered
        ? Loc.F("settings.defaultRegistered", SupportedFormatCount)
        : Loc.F("settings.defaultUnregistered", SupportedFormatCount);
    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";
    public string VersionLine => Loc.F("settings.versionLine", Version);
    public string FormatCountText => Loc.F("settings.formatCount", SupportedFormatCount);

    public void Reload()
    {
        Draft = player.Settings.Clone();
        ShortcutItems.Clear();
        foreach (var a in Shortcuts.All) ShortcutItems.Add(new ShortcutItem(a, Shortcuts.BindingFor(Draft.Shortcuts, a.Id)));
        if (OperatingSystem.IsWindows()) IsDefaultRegistered = FileAssociation.IsRegistered;
        RefreshDraft();
    }

    public void Set(Action<AppSettings> change)
    {
        change(Draft);
        RefreshDraft();
    }

    void RefreshDraft()
    {
        OnPropertyChanged(nameof(Draft));
        foreach (var p in new[]
        {
            nameof(PageTitle), nameof(VersionLine), nameof(FormatCountText), // language may have changed since the last open
            nameof(LanguageName), nameof(ThemeText), nameof(SeekStepText), nameof(VolumeStepText), nameof(WheelActionText), nameof(LoopModeText),
            nameof(HwdecModeText), nameof(DeinterlaceModeText), nameof(ScreenFormatText),
            nameof(DisplayFormat43Text), nameof(DisplayFormat169Text), nameof(DisplayFormatCinematicText),
            nameof(TargetFpsText), nameof(MotionScalerText), nameof(ToneMappingText),
            nameof(SubtitleScaleText), nameof(SubtitlePosText), nameof(DefaultPresetText),
            nameof(VideoOutputText), nameof(ScreenshotFormatText), nameof(SubtitleColorText),
            nameof(AudioOutputModeText), nameof(AudioDeviceText), nameof(SpdifDeviceText), nameof(HdmiDeviceText),
            nameof(LipsyncValue), nameof(LipsyncText), nameof(ExportEncoderText), nameof(ExportDeinterlaceText),
            nameof(DefaultStatus),
        })
            OnPropertyChanged(p);
    }

    // ---- shortcuts ----
    [RelayCommand]
    void StartCapture(ShortcutItem item)
    {
        foreach (var s in ShortcutItems) s.IsCapturing = s == item;
    }

    /// <summary>Assigns the pressed key; a key already used elsewhere moves to this action.</summary>
    public void Assign(string binding)
    {
        if (Capturing is not { } item) return;
        foreach (var other in ShortcutItems.Where(s => s != item && s.Binding == binding)) other.Binding = "";
        item.Binding = binding;
        item.IsCapturing = false;
    }

    public void CancelCapture()
    {
        foreach (var s in ShortcutItems) s.IsCapturing = false;
    }

    partial void OnPageChanged(string value) => CancelCapture();

    [RelayCommand]
    void ResetShortcuts()
    {
        foreach (var s in ShortcutItems) { s.Binding = s.Action.Default; s.IsCapturing = false; }
    }

    // ---- Windows default player ----
    [RelayCommand]
    void RegisterDefault()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileAssociation.Register();
        IsDefaultRegistered = true;
        FileAssociation.OpenDefaultAppsPage();
    }

    [RelayCommand]
    void OpenDefaultApps()
    {
        if (OperatingSystem.IsWindows()) FileAssociation.OpenDefaultAppsPage();
    }

    [RelayCommand]
    void UnregisterDefault()
    {
        if (!OperatingSystem.IsWindows()) return;
        FileAssociation.Unregister();
        IsDefaultRegistered = false;
    }

    [RelayCommand]
    void ClearHistory() => player.ClearRecents();

    [RelayCommand]
    void Save()
    {
        Draft.Shortcuts = ShortcutItems.Where(s => s.Binding != s.Action.Default).ToDictionary(s => s.Action.Id, s => s.Binding);
        player.Settings.CopyFrom(Draft);
        player.ApplySettings();
        player.IsSettingsOpen = false;
    }

    [RelayCommand]
    void Cancel()
    {
        CancelCapture();
        App.SetAccent(player.Settings.Accent); // undo the live accent preview
        player.SetAudioDelayMs(player.Settings.LipsyncMs);
        player.IsSettingsOpen = false;
    }
}
