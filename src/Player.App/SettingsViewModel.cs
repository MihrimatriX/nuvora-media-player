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
    [ObservableProperty, NotifyPropertyChangedFor(nameof(KeyText))] public partial string Binding { get; set; } = binding;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(KeyText))] public partial bool IsCapturing { get; set; }
    public string KeyText => IsCapturing ? "Bir tuşa basın…" : Shortcuts.Display(Binding);
}

/// <summary>Edits a copy of the settings; nothing changes until "Kaydet".</summary>
public sealed partial class SettingsViewModel(PlayerViewModel player) : ObservableObject
{
    public static readonly string[] Accents = ["#6D5AE6", "#00A2FF", "#D6245F", "#0F766E", "#C2410C"];
    public static readonly string[] AccentNames = ["Nuvora İris", "Buz Mavisi (Ice Blue)", "Ahududu", "Deniz Yeşili", "Kehribar"];
    public static readonly int[] SeekSteps = [3, 5, 10, 15, 30, 60];
    public static readonly int[] VolumeSteps = [1, 2, 5, 10];
    public static readonly double[] SubtitleScales = [0.7, 0.85, 1, 1.15, 1.3, 1.5, 1.8];
    public static readonly (int Value, string Label)[] SubtitlePositions =
        [(100, "Normal (%100)"), (95, "Biraz yukarı (%95)"), (90, "Yukarı (%90)"), (85, "Daha yukarı (%85)")];
    public static readonly (string Value, string Label)[] WheelActions =
        [("volume", "Ses düzeyi"), ("seek", "İleri / geri sar"), ("speed", "Oynatma hızı")];
    public static readonly (string Value, string Label)[] LoopModes =
        [("off", "Kapalı"), ("file", "Dosyayı tekrarla"), ("playlist", "Listeyi tekrarla")];
    public static readonly (string Value, string Label)[] HwdecModes =
        [("auto-safe", "Otomatik (NVIDIA PureVideo / D3D11)"), ("nvdec", "NVIDIA PureVideo (NVDEC)"), ("d3d11va", "Direct3D 11 (d3d11va)"), ("dxva2", "Intel QuickSync / DXVA2"), ("auto-copy", "Otomatik Kopyalama (auto-copy)")];
    public static readonly (string Value, string Label)[] DeinterlaceModes =
        [("auto", "Otomatik (En iyi kalite)"), ("yes", "Her zaman açık"), ("no", "Kapalı")];
    public static readonly (string Value, string Label)[] ScreenFormats =
        [("auto", "Otomatik"), ("16:9", "16:9"), ("16:10", "16:10"), ("21:9", "21:9 (Sinema)"), ("4:3", "4:3")];
    public static readonly (string Value, string Label)[] DisplayFormats =
        [("normal", "Normal (Sığdır)"), ("stretch", "Tam Ekran (Genişlet)"), ("zoom", "Yakınlaştır (Kırp)")];
    public static readonly (string Value, string Label)[] CinematicFormats =
        [("normal", "Normal (Letterbox)"), ("zoom", "Tam Ekran (Doldur / Kırp)"), ("half-zoom", "Yumuşak Yakınlaştır (%50)")];
    public static readonly (string Value, string Label)[] VideoOutputs =
        [("gpu-next", "gpu-next (Direct3D 11 · En hızlı)"), ("gpu", "gpu (Uyumluluk)")];
    public static readonly (int Value, string Label)[] FpsOptions =
        [(0, "Kaynak FPS"), (60, "60 FPS"), (90, "90 FPS"), (120, "120 FPS"), (-1, "Ekran Hz (VSync)")];
    public static readonly (string Value, string Label)[] MotionScalers =
        [("oversample", "Oversample (Keskin)"), ("mitchell", "Mitchell (Dengeli)"), ("sphinx", "Sphinx (Sinematik)"), ("catmull_rom", "Catmull-Rom")];
    public static readonly (string Value, string Label)[] ToneMappings =
        [("auto", "Otomatik"), ("bt.2446a", "BT.2446a (ITU)"), ("spline", "Spline"), ("hable", "Hable (Sinematik)"), ("mobius", "Mobius")];
    public static readonly (string Value, string Label)[] AudioOutputModes =
        [("stereo", "Stereo (2.0)"), ("stereo-surround", "Stereo Surround"), ("5.1", "5.1 Çevresel Ses"), ("7.1", "7.1 Çevresel Ses"), ("spdif", "S/PDIF Bitstream"), ("hdmi", "HDMI Bitstream")];
    public static readonly (string Value, string Label)[] ExportEncoders =
        [("nvenc", "NVIDIA® NVENC"), ("amf", "AMD AMF / VCE"), ("qsv", "Intel® QuickSync (QSV)"), ("cpu", "Yazılım (CPU x264/x265)")];
    public static readonly (string Value, string Label)[] ExportDeinterlaceModes =
        [("auto", "Otomatik (En iyi kalite)"), ("disabled", "Kapalı")];
    public static readonly string[] ScreenshotFormats = ["png", "jpg", "webp"];
    public static readonly (string Value, string Label)[] SubtitleColors =
        [("#FFFFFFFF", "Beyaz"), ("#FFFFE14D", "Sarı"), ("#FFD6E4FF", "Açık mavi"), ("#FF80F5FF", "Camgöbeği"), ("#FFA8FFB2", "Açık yeşil")];

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
        "video" => "Video, Motion² ve FPS",
        "ses" => "Ses ve Bitstream",
        "altyazi" => "Altyazı",
        "disaaktar" => "Dışa Aktar ve Donanım Kodlama",
        "kisayollar" => "Klavye Kısayolları",
        "hakkinda" => "Hakkında",
        _ => "Genel Ayarlar",
    };
    public string ThemeText
    {
        get
        {
            var idx = Array.FindIndex(Accents, a => string.Equals(a, Draft.Accent, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? AccentNames[idx] : AccentNames[0];
        }
    }
    public string SeekStepText => $"{Draft.SeekStepSeconds} sn";
    public string VolumeStepText => $"%{Draft.VolumeStep}";
    public string WheelActionText => WheelActions.FirstOrDefault(w => w.Value == Draft.WheelAction).Label ?? "Ses düzeyi";
    public string LoopModeText => LoopModes.FirstOrDefault(l => l.Value == Draft.LoopMode).Label ?? "Kapalı";
    public string HwdecModeText => HwdecModes.FirstOrDefault(h => h.Value == Draft.HwdecMode).Label ?? Draft.HwdecMode;
    public string DeinterlaceModeText => DeinterlaceModes.FirstOrDefault(d => d.Value == Draft.DeinterlaceMode).Label ?? "Otomatik (En iyi kalite)";
    public string ScreenFormatText => ScreenFormats.FirstOrDefault(s => s.Value == Draft.ScreenFormat).Label ?? Draft.ScreenFormat;
    public string DisplayFormat43Text => DisplayFormats.FirstOrDefault(d => d.Value == Draft.DisplayFormat43).Label ?? "Normal (Sığdır)";
    public string DisplayFormat169Text => DisplayFormats.FirstOrDefault(d => d.Value == Draft.DisplayFormat169).Label ?? "Normal (Sığdır)";
    public string DisplayFormatCinematicText => CinematicFormats.FirstOrDefault(d => d.Value == Draft.DisplayFormatCinematic).Label ?? "Normal (Letterbox)";
    public string TargetFpsText => FpsOptions.FirstOrDefault(f => f.Value == Draft.TargetFps).Label ?? "Kaynak FPS";
    public string MotionScalerText => MotionScalers.FirstOrDefault(m => m.Value == Draft.MotionScaler).Label ?? Draft.MotionScaler;
    public string ToneMappingText => ToneMappings.FirstOrDefault(t => t.Value == Draft.ToneMapping).Label ?? Draft.ToneMapping;
    public string SubtitleScaleText => $"%{(Draft.SubtitleScale * 100).ToString("0", CultureInfo.InvariantCulture)}";
    public string SubtitlePosText => SubtitlePositions.FirstOrDefault(p => p.Value == Draft.SubtitlePosition).Label ?? $"%{Draft.SubtitlePosition}";
    public string DefaultPresetText => ImagePresets.Find(Draft.DefaultPreset).Name;
    public string VideoOutputText => VideoOutputs.FirstOrDefault(v => v.Value == Draft.VideoOutput).Label ?? Draft.VideoOutput;
    public string ScreenshotFormatText => Draft.ScreenshotFormat.ToUpperInvariant();
    public string SubtitleColorText => SubtitleColors.FirstOrDefault(c => c.Value == Draft.SubtitleColor).Label ?? "Özel";
    public string AudioOutputModeText => AudioOutputModes.FirstOrDefault(a => a.Value == Draft.AudioOutputMode).Label ?? "Stereo Surround";
    public string AudioDeviceText => player.AudioDevices.FirstOrDefault(d => d.Name == Draft.AudioDevice)?.Description ?? "Otomatik";
    public string SpdifDeviceText => player.AudioDevices.FirstOrDefault(d => d.Name == Draft.SpdifDevice)?.Description ?? "Otomatik (Varsayılan S/PDIF)";
    public string HdmiDeviceText => player.AudioDevices.FirstOrDefault(d => d.Name == Draft.HdmiDevice)?.Description ?? "Otomatik (Varsayılan HDMI)";
    public string ExportEncoderText => ExportEncoders.FirstOrDefault(e => e.Value == Draft.ExportEncoder).Label ?? "NVIDIA® NVENC";
    public string ExportDeinterlaceText => ExportDeinterlaceModes.FirstOrDefault(d => d.Value == Draft.ExportDeinterlace).Label ?? "Otomatik (En iyi kalite)";

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
        ? $"Kayıtlı ({SupportedFormatCount} video, ses ve oynatma listesi formatı). Windows Varsayılan Uygulamalar'dan Nuvora Player'ı seçebilirsiniz."
        : $"{SupportedFormatCount} farklı video, ses ve liste formatını (MKV, MP4, AVI, MOV, HEVC, AV1, FLAC, MP3, M3U8…) Windows ile ilişkilendirin.";
    public string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "";

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
            nameof(ThemeText), nameof(SeekStepText), nameof(VolumeStepText), nameof(WheelActionText), nameof(LoopModeText),
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
