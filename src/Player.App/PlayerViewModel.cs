using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Player.Core;
using Player.Enhancement;

namespace Player.App;

public sealed record Chapter(string? Title, double Time);
public sealed record PlaylistEntry(string Filename, bool Current, string? Title)
{
    public string Label => Title ?? Path.GetFileName(Filename);
}
public sealed record AudioDeviceInfo(string Name, string Description);

/// <summary>Playback state and commands. Image (.Image.cs) and audio (.Audio.cs) panels are partial files.</summary>
public sealed partial class PlayerViewModel : ObservableObject
{
    public static readonly double[] Speeds = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 3, 4];
    public static readonly (string Value, string Label)[] AspectRatios =
        [("auto", "aspect.auto"), ("16:9", "aspect.wide"), ("21:9", "aspect.cinema"), ("4:3", "aspect.classic"), ("1:1", "aspect.square")]; // labels are Loc keys
    public static readonly int[] Rotations = [0, 90, 180, 270];

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static readonly (string Name, PropertyKind Kind)[] Observed =
    [
        ("time-pos", PropertyKind.Double), ("duration", PropertyKind.Double), ("volume", PropertyKind.Double),
        ("speed", PropertyKind.Double), ("pause", PropertyKind.Flag), ("mute", PropertyKind.Flag),
        ("idle-active", PropertyKind.Flag), ("media-title", PropertyKind.String), ("track-list", PropertyKind.String),
        ("path", PropertyKind.String), ("chapter", PropertyKind.Double), ("chapter-list", PropertyKind.String),
        ("demuxer-cache-time", PropertyKind.Double), ("playlist", PropertyKind.String),
        ("width", PropertyKind.Double), ("height", PropertyKind.Double), ("video-format", PropertyKind.String),
        ("container-fps", PropertyKind.Double), ("estimated-vf-fps", PropertyKind.Double),
        ("hwdec-current", PropertyKind.String), ("current-vo", PropertyKind.String),
        ("video-params/pixelformat", PropertyKind.String), ("video-params/hw-pixelformat", PropertyKind.String),
        ("video-params/gamma", PropertyKind.String), ("video-params/colormatrix", PropertyKind.String),
        ("audio-codec-name", PropertyKind.String), ("audio-params/channel-count", PropertyKind.Double),
        ("audio-params/samplerate", PropertyKind.Double),
        ("audio-delay", PropertyKind.Double), ("sub-delay", PropertyKind.Double),
        ("ab-loop-a", PropertyKind.String), ("ab-loop-b", PropertyKind.String),
        ("mpv-version", PropertyKind.String),
        ("video-params/dw", PropertyKind.Double), ("video-params/dh", PropertyKind.Double),
        ("audio-device-list", PropertyKind.String), ("metadata/by-key/artist", PropertyKind.String),
        ("current-ao", PropertyKind.String), ("ffmpeg-version", PropertyKind.String),
        ("frame-drop-count", PropertyKind.Double), ("seeking", PropertyKind.Flag),
        ("osd-dimensions/w", PropertyKind.Double), ("osd-dimensions/h", PropertyKind.Double),
    ];

    /// <summary>Output devices reported by mpv ("auto" first).</summary>
    public IReadOnlyList<AudioDeviceInfo> AudioDevices { get; private set; } = [new("auto", Loc.T("audio.auto"))];

    /// <summary>Display size of a newly opened video (once per file), so the window can fit it without black bars.</summary>
    public event Action<double, double>? VideoSizeKnown;
    /// <summary>Raised when the user picks a window scale factor (0.5x, 1x, 1.5x) from the context menu.</summary>
    public event Action<double>? WindowScaleRequested;
    /// <summary>Raised when a new SmartSeek frame thumbnail is decoded for the hovered timestamp bucket.</summary>
    public event Action<int, Bitmap>? ThumbnailReady;
    /// <summary>Raised when a brief OSD toast pill should appear on screen.</summary>
    public event Action<string>? OsdRequested;
    double _dw, _dh;
    string? _fittedPath;

    readonly IPlayerService _player;
    readonly AppSettings _settings;
    readonly MpvThumbnailer _thumbnailer;
    readonly Dictionary<int, Bitmap> _thumbCache = [];
    readonly Dictionary<string, string?> _raw = []; // stream facts for the tech line and OSD stats
    double _position, _width, _height, _sourceFps, _outputFps, _sampleRate;
    bool _fill;
    string? _path;
    string _abA = "no", _abB = "no";

    public PlayerViewModel(IPlayerService player, AppSettings settings)
    {
        _player = player;
        _settings = settings;
        _thumbnailer = new MpvThumbnailer(ThumbDir);
        _thumbnailer.ThumbnailReady += (file, sec, bytes) =>
        {
            if (file != _path) return;
            try
            {
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                if (_thumbCache.TryGetValue(sec, out var old)) Retire(old);
                _thumbCache[sec] = bmp;
                TrimThumbCache(sec);
                ThumbnailReady?.Invoke(sec, bmp);
            }
            catch { }
        };
        Channels = CreateChannels();
        BoostEnabled = settings.BoostByDefault || settings.Volume > 100;
        Volume = settings.Volume;
        Gpu = GpuInfo.Detect();
        PresetId = settings.DefaultPreset;
        Normalize = settings.NormalizeByDefault;
        TargetFps = settings.TargetFps != 0 ? settings.TargetFps : (settings.SmoothMotionByDefault ? 60 : 0);
        LoopMode = settings.LoopMode;
        Loc.SetLanguage(settings.Language);
        _player.PropertyChanged += OnPlayerProperty;
        _player.SeekCompleted += PumpSeek;
        Loc.LanguageChanged += () => OnPropertyChanged(string.Empty); // every computed label re-reads its text
        _player.PlaybackError += msg => ErrorText = Loc.F("osd.playbackFailed", msg);
    }

    public AppSettings Settings => _settings;

    // ---- playback state ----
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial string? Artist { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DurationText), nameof(RightTimeText))] public partial double Duration { get; set; }
    [ObservableProperty] public partial bool IsPaused { get; set; }
    [ObservableProperty] public partial bool IsMuted { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowTitleBlock), nameof(ShowFileChip))] public partial bool IsIdle { get; set; } = true;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BoostText))] public partial double Volume { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SpeedText))] public partial double Speed { get; set; } = 1;
    [ObservableProperty] public partial string? ErrorText { get; set; }
    [ObservableProperty] public partial IReadOnlyList<Track> Tracks { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<Chapter> Chapters { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<PlaylistEntry> Playlist { get; set; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ChapterText), nameof(ChapterPrefix), nameof(ChapterName))]
    public partial int ChapterIndex { get; set; } = -1;
    [ObservableProperty] public partial double CacheTime { get; set; }
    [ObservableProperty] public partial string MpvVersion { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RightTimeText))] public partial bool ShowRemainingTime { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(LoopLabel), nameof(IsLoopActive))] public partial string LoopMode { get; set; } = "off";
    [ObservableProperty] public partial string AspectRatio { get; set; } = "auto";
    [ObservableProperty] public partial int Rotation { get; set; }
    [ObservableProperty] public partial bool IsMirrored { get; set; }
    [ObservableProperty] public partial bool ShowStats { get; set; }

    public bool IsFilled => _fill;
    public bool HasVideo => _width > 0;
    public bool IsLoopActive => LoopMode != "off" || IsAbLoopActive;
    public bool IsAbLoopActive => _abA != "no";
    public double? AbLoopASeconds => double.TryParse(_abA, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : null;
    public double? AbLoopBSeconds => double.TryParse(_abB, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) ? b : null;
    public string LoopLabel => IsAbLoopActive
        ? Loc.T(_abB != "no" ? "loop.abLoop" : "loop.aSet")
        : Loc.T(LoopMode switch { "file" => "loop.file", "playlist" => "loop.playlist", _ => "loop.off" });

    // ---- sheets ----
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSheetOpen), nameof(ShowTitleBlock), nameof(ShowFileChip), nameof(IsSidePanelOpen))]
    public partial bool IsPanelOpen { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSheetOpen), nameof(ShowTitleBlock), nameof(ShowFileChip))]
    public partial bool IsSettingsOpen { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsImageTab))] public partial bool IsAudioTab { get; set; }
    public bool IsImageTab { get => !IsAudioTab; set => IsAudioTab = !value; }
    public bool IsSheetOpen => IsPanelOpen || IsSettingsOpen || IsPlaylistOpen;
    public bool ShowTitleBlock => !IsIdle && !IsSheetOpen;
    public bool ShowFileChip => !IsIdle && IsSheetOpen;

    /// <summary>Set by the view while the seek bar is held, so slider coercion never triggers a seek.
    /// While held, seeks go to the nearest keyframe and only one is in flight at a time; releasing the bar
    /// lands one exact seek on the final position.</summary>
    public bool IsScrubbing
    {
        get => _scrubbing;
        set
        {
            if (_scrubbing == value) return;
            _scrubbing = value;
            if (value) return;
            _pendingSeek = null;
            _seekInFlight = false;
            _player.Command("seek", Inv(_position), "absolute+exact");
        }
    }

    bool _scrubbing, _seekInFlight;
    double? _pendingSeek;

    void ScrubTo(double seconds)
    {
        _pendingSeek = seconds;
        if (!_seekInFlight) PumpSeek();
    }

    /// <summary>Sends the newest scrub target once mpv has finished the previous seek (PLAYBACK_RESTART).</summary>
    void PumpSeek()
    {
        if (_pendingSeek is not { } target)
        {
            _seekInFlight = false;
            return;
        }
        _pendingSeek = null;
        _seekInFlight = true;
        _player.Command("seek", Inv(target), "absolute+keyframes");
    }

    public double Position
    {
        get => _position;
        set
        {
            _position = value;
            OnPropertyChanged(nameof(PositionText));
            OnPropertyChanged(nameof(RightTimeText));
            if (IsScrubbing) ScrubTo(value);
        }
    }

    public string PositionText => Format(_position);
    public string DurationText => Format(Duration);
    public string RightTimeText => ShowRemainingTime
        ? $"−{Format(Math.Max(0, Duration - _position))}"
        : $"/ {Format(Duration)}";
    public string SpeedText => $"{Speed.ToString("0.0#", CultureInfo.InvariantCulture)}×";
    public string FileChip => _path is null ? "" : Title == Path.GetFileName(_path) ? Title : $"{Title} — {Path.GetFileName(_path)}";
    public string ChapterText => ChapterLabel(ChapterIndex);
    public string ChapterPrefix => ChapterIndex < 0 || ChapterIndex >= Chapters.Count ? "" : Loc.F("chapter.prefix", ChapterIndex + 1) + (ChapterName.Length > 0 ? " · " : "");
    public string ChapterName => ChapterIndex < 0 || ChapterIndex >= Chapters.Count ? "" : Chapters[ChapterIndex].Title ?? "";
    public string OpenKeyText => Shortcuts.Display(Shortcuts.BindingFor(_settings.Shortcuts, "open"));
    public string CompareKeyText => Shortcuts.Display(Shortcuts.BindingFor(_settings.Shortcuts, "compare"));

    /// <summary>"4K · HEVC 10-bit · 24 → 60 FPS · HDR10 · AC-3 5.1"</summary>
    public string TechInfo
    {
        get
        {
            var pix = (Raw("video-params/hw-pixelformat") ?? "") + (Raw("video-params/pixelformat") ?? "");
            var video = Raw("video-format") is { } v ? CodecName(v) + (pix.Contains("10") ? " 10-bit" : "") : null;
            var fps = FpsInfoLabel();
            var hdr = Raw("video-params/gamma") switch { "pq" => "HDR10", "hlg" => "HLG", _ => null };
            var audio = Raw("audio-codec-name") is { } a ? $"{CodecName(a)} {ChannelLayout(_channelCount)}".Trim() : null;
            return string.Join(" · ", new[] { ResolutionLabel(_height), video, fps, hdr, audio }.Where(s => !string.IsNullOrEmpty(s)));
        }
    }

    /// <summary>Multi-line technical stats shown by the OSD overlay ('I' key).</summary>
    public string StatsSummary
    {
        get
        {
            var file = _path is null ? "—" : Path.GetFileName(_path);
            var res = _width > 0 && _height > 0 ? $"{_width:0}×{_height:0} ({ResolutionLabel(_height)})" : "—";
            var pix = Raw("video-params/hw-pixelformat") ?? Raw("video-params/pixelformat") ?? "—";
            var vcodec = Raw("video-format") is { } v ? $"{CodecName(v)} ({pix})" : "—";
            var fpsText = _sourceFps > 0
                ? (TargetFps > 0 ? Loc.F("stats.fpsTarget", _sourceFps.ToString("0.##"), TargetFps) : $"{_sourceFps:0.##} fps" + (_outputFps > 0 ? Loc.F("stats.fpsOutput", _outputFps.ToString("0.#")) : ""))
                : "—";
            var hwdec = Raw("hwdec-current") is { Length: > 0 } h && h != "no" ? h : Loc.T("stats.software");
            var vo = Raw("current-vo") ?? _settings.VideoOutput;
            var acodec = Raw("audio-codec-name") is { } a
                ? $"{CodecName(a)} · {ChannelLayout(_channelCount)}" + (_sampleRate > 0 ? $" · {_sampleRate / 1000:0.#} kHz" : "")
                : "—";
            return Loc.F("stats.summary", file, res, vcodec, fpsText, hwdec, vo, ActiveLayer, acodec);
        }
    }

    string? FpsInfoLabel()
    {
        if (_sourceFps <= 0) return TargetFps > 0 ? $"{TargetFps} FPS" : null;
        var src = $"{_sourceFps:0.##}".Replace(",00", "").Replace(".00", "");
        if (!IsBypassed && TargetFps > 0) return $"{src} → {TargetFps} FPS";
        if (!IsBypassed && TargetFps == -1) return $"{src} FPS · VSync";
        return $"{src} FPS";
    }

    public void Start(nint videoHandle)
    {
        Directory.CreateDirectory(_settings.ScreenshotDirectory);
        Directory.CreateDirectory(ThumbDir);
        var fonts = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
        var options = new Dictionary<string, string>
        {
            ["volume"] = Inv(_settings.Volume),
            ["volume-max"] = BoostEnabled ? "200" : "100",
            ["watch-later-dir"] = Path.Combine(AppSettings.ConfigDir, "watch_later"),
            ["directory-filter-types"] = "video,audio,playlist",
            // Subtitles in the design's type: Manrope, soft shadow, sitting above the control bar.
            ["sub-fonts-dir"] = fonts,
            ["sub-font"] = "Manrope",
            ["sub-bold"] = "yes",
            ["sub-font-size"] = "34",
            ["sub-border-size"] = "1.4",
            ["sub-border-color"] = "#B3000000",
            ["sub-shadow-offset"] = "1.5",
            ["sub-shadow-color"] = "#99000000",
            ["sub-margin-y"] = "34",
        };
        _applied = SettingsOptions();
        foreach (var (k, v) in _applied) options[k] = v;
        _player.Initialize(videoHandle, options);
        foreach (var (name, kind) in Observed) _player.Observe(name, kind);
        ApplyImage();
        ApplyFps();
        ApplyAudioFilters();
        ApplyLoopMode();
    }

    /// <summary>mpv options that come from the settings window; used at start and after "Kaydet".</summary>
    Dictionary<string, string> SettingsOptions() => new()
    {
        ["screenshot-directory"] = _settings.ScreenshotDirectory,
        ["save-position-on-quit"] = _settings.Resume ? "yes" : "no",
        ["resume-playback"] = _settings.Resume ? "yes" : "no",
        // Opening one file queues its siblings, so next/prev and auto-advance walk the folder (mpv >= 0.39).
        ["autocreate-playlist"] = _settings.AutoNext ? "filter" : "no",
        ["hwdec"] = _settings.HardwareDecoding ? _settings.HwdecMode : "no",
        ["alang"] = _settings.AudioLanguages,
        ["slang"] = _settings.SubtitleLanguages,
        ["sub-scale"] = Inv(_settings.SubtitleScale),
        ["sub-pos"] = _settings.SubtitlePosition.ToString(CultureInfo.InvariantCulture),
        ["sub-auto"] = _settings.AutoLoadSubtitles ? "fuzzy" : "no",
        ["vo"] = _settings.VideoOutput,
        ["deinterlace"] = !_settings.Deinterlace || _settings.DeinterlaceMode == "no"
            ? "no"
            : (_settings.DeinterlaceMode == "yes" ? "yes" : (_settings.AutoInterlaceDetection ? "auto" : "no")),
        ["tone-mapping"] = _settings.ToneMapping,
        ["screenshot-format"] = _settings.ScreenshotFormat,
        ["audio-device"] = _settings.AudioDevice,
        ["audio-exclusive"] = _settings.AudioExclusive ? "yes" : "no",
        ["sub-color"] = _settings.SubtitleColor,
        // opaque-box draws the (translucent black) border colour as a box behind each line.
        ["sub-border-style"] = _settings.SubtitleBackground ? "opaque-box" : "outline-and-shadow",
        ["sub-ass-override"] = _settings.OverrideAssStyles ? "force" : "scale",
    };

    Dictionary<string, string> _applied = [];

    public void ApplySettings()
    {
        Directory.CreateDirectory(_settings.ScreenshotDirectory);
        Directory.CreateDirectory(_settings.ExportDirectory);
        // Only changed values: re-setting "vo" or "audio-device" would restart the output even if equal.
        var options = SettingsOptions();
        foreach (var (k, v) in options)
            if (_applied.GetValueOrDefault(k) != v) _player.Command("set", k, v);
        _applied = options;
        PresetId = _settings.DefaultPreset;
        ApplyImage(); // the shader-layer switches may have changed even if the preset did not
        Normalize = _settings.NormalizeByDefault;
        BoostEnabled = _settings.BoostByDefault || Volume > 100;
        TargetFps = _settings.TargetFps != 0 ? _settings.TargetFps : (_settings.SmoothMotionByDefault ? 60 : 0);
        LoopMode = _settings.LoopMode;
        ApplyFps();
        ApplyAudioFilters();
        ApplyDisplayFormat();
        ApplyLoopMode();
        App.SetAccent(_settings.Accent);
        Loc.SetLanguage(_settings.Language);
        _settings.Save();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(OpenKeyText));
        OnPropertyChanged(nameof(CompareKeyText));
        NotifySystemInfo();
    }

    public void StartThumbnailer(nint previewWid) => _thumbnailer.Initialize(previewWid);

    const int ThumbCacheLimit = 90;
    int _hoverBucket = -1;
    List<Bitmap> _retiredThumbs = [], _retiringThumbs = [];

    /// <summary>Evicts the buckets farthest from where the user is scrubbing. The hovered bucket is never
    /// evicted because the seek bubble may be showing it.</summary>
    void TrimThumbCache(int newest)
    {
        while (_thumbCache.Count > ThumbCacheLimit)
        {
            var victim = _thumbCache.Keys.Where(k => k != _hoverBucket && k != newest).MaxBy(k => Math.Abs(k - newest));
            if (!_thumbCache.Remove(victim, out var bmp)) return;
            Retire(bmp);
        }
    }

    /// <summary>Thumbnails hold native Skia memory. A bitmap may still be on screen when it leaves the cache,
    /// so it is disposed one generation later, by which time the seek bubble has moved on.</summary>
    void Retire(Bitmap bmp)
    {
        _retiringThumbs.Add(bmp);
        if (_retiringThumbs.Count < 16) return;
        foreach (var b in _retiredThumbs) b.Dispose();
        (_retiredThumbs, _retiringThumbs) = (_retiringThumbs, []);
    }

    void ClearThumbCache()
    {
        foreach (var bmp in _thumbCache.Values) Retire(bmp);
        _thumbCache.Clear();
        _hoverBucket = -1;
    }

    public Bitmap? GetOrRequestThumbnail(double seconds, out int bucket)
    {
        bucket = Math.Max(0, (int)(seconds / 2.0) * 2);
        _hoverBucket = bucket;
        if (_thumbCache.TryGetValue(bucket, out var bmp)) return bmp;
        _thumbnailer.Request(bucket);
        return null;
    }

    public void ShowOsd(string message) => OsdRequested?.Invoke(message);

    public void Shutdown()
    {
        RememberCurrent();
        _settings.Volume = Volume;
        _settings.Save();
        _thumbnailer.Dispose();
        _player.Dispose();
    }

    // ---- opening ----
    public void Open(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        ErrorText = null;
        SaveResumePoint();
        for (var i = 0; i < paths.Count; i++)
            _player.Command("loadfile", paths[i], i == 0 ? "replace" : "append");
    }

    [RelayCommand] void OpenRecent(RecentItem item) => Open([item.Path]);

    [RelayCommand]
    void PlayFromStart(RecentItem item)
    {
        ErrorText = null;
        SaveResumePoint();
        _player.Command("loadfile", item.Path, "replace", "-1", "start=0");
    }

    [RelayCommand]
    public void ClearRecents()
    {
        _settings.ClearRecent();
        NotifyRecentsChanged();
    }

    public void AddSubtitle(string path) => _player.Command("sub-add", path);
    public void PlayIndex(int index) => _player.Command("playlist-play-index", index.ToString(CultureInfo.InvariantCulture));

    public void SetSpeed(double speed)
    {
        _player.Command("set", "speed", Inv(speed));
        ShowOsd(Loc.F("osd.speed", speed.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    public void SeekRelative(double seconds)
    {
        _player.Command("seek", Inv(seconds), "relative");
        ShowOsd(Loc.F("osd.seconds", seconds > 0 ? $"+{seconds:0}" : $"{seconds:0}"));
    }

    public void SeekToChapter(int index) => _player.Command("set", "chapter", index.ToString(CultureInfo.InvariantCulture));
    public void RequestWindowScale(double scale) => WindowScaleRequested?.Invoke(scale);

    public void SelectTrack(string type, string id) =>
        _player.Command("set", type switch { "video" => "vid", "audio" => "aid", _ => "sid" }, id);

    public void SelectAudioDevice(string name)
    {
        _settings.AudioDevice = name;
        _player.Command("set", "audio-device", name);
        _settings.Save();
    }

    /// <summary>Volume up (+1) or down (−1) by the configured step.</summary>
    public void StepVolume(int direction)
    {
        _player.Command("add", "volume", Inv(direction * _settings.VolumeStep));
        var targetVol = Math.Clamp(Math.Round(Volume + direction * _settings.VolumeStep), 0, VolumeMax);
        ShowOsd(Loc.F("osd.volume", targetVol));
    }

    public void HandleWheel(int direction)
    {
        if (direction == 0) return;
        switch (_settings.WheelAction)
        {
            case "seek": SeekRelative(direction * _settings.SeekStepSeconds); break;
            case "speed": if (direction > 0) SpeedUp(); else SpeedDown(); break;
            default: StepVolume(direction); break;
        }
    }

    public void StepSubtitleScale(double delta)
    {
        _settings.SubtitleScale = Math.Clamp(Math.Round(_settings.SubtitleScale + delta, 2), 0.5, 2.5);
        _player.Command("set", "sub-scale", Inv(_settings.SubtitleScale));
        ShowOsd(Loc.F("osd.subtitleSize", (_settings.SubtitleScale * 100).ToString("0")));
    }

    public void StepSubtitlePos(int delta)
    {
        _settings.SubtitlePosition = Math.Clamp(_settings.SubtitlePosition + delta, 50, 105);
        _player.Command("set", "sub-pos", _settings.SubtitlePosition.ToString(CultureInfo.InvariantCulture));
        ShowOsd(Loc.F("osd.subtitlePos", _settings.SubtitlePosition));
    }

    [RelayCommand] public void ToggleTimeMode() => ShowRemainingTime = !ShowRemainingTime;
    [RelayCommand] public void ToggleStats() => ShowStats = !ShowStats;

    [RelayCommand]
    public void ToggleAlwaysOnTop()
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        _settings.Save();
        ShowOsd(Loc.T(_settings.AlwaysOnTop ? "osd.onTopOn" : "osd.onTopOff"));
        OnPropertyChanged(nameof(Settings));
    }

    [RelayCommand] void SpeedUp() => SetSpeed(Speeds.FirstOrDefault(s => s > Speed + 0.01, Speeds[^1]));
    [RelayCommand] void SpeedDown() => SetSpeed(Speeds.LastOrDefault(s => s < Speed - 0.01, Speeds[0]));

    [RelayCommand]
    public void CycleLoop()
    {
        LoopMode = LoopMode switch { "off" => "file", "file" => "playlist", _ => "off" };
        ApplyLoopMode();
    }

    public void SetLoop(string mode)
    {
        LoopMode = mode;
        ApplyLoopMode();
        ShowOsd(Loc.T(mode switch { "file" => "osd.loopFile", "playlist" => "osd.loopPlaylist", _ => "osd.loopOff" }));
    }

    void ApplyLoopMode()
    {
        _player.Command("set", "loop-file", LoopMode == "file" ? "inf" : "no");
        _player.Command("set", "loop-playlist", LoopMode == "playlist" ? "inf" : "no");
    }

    [RelayCommand]
    public void AbLoop()
    {
        _player.Command("ab-loop");
        ShowOsd(Loc.T(_abA == "no" ? "osd.abSetA" : (_abB == "no" ? "osd.abStart" : "osd.abReset")));
    }

    [RelayCommand]
    public void CycleAspect()
    {
        var idx = Array.FindIndex(AspectRatios, a => a.Value == AspectRatio);
        SetAspect(AspectRatios[(idx + 1) % AspectRatios.Length].Value);
    }

    public void SetAspect(string aspect)
    {
        AspectRatio = aspect;
        _player.Command("set", "video-aspect-override", aspect == "auto" ? "-1" : aspect);
        ShowOsd(Loc.F("osd.aspect", aspect));
    }

    [RelayCommand]
    public void Rotate() => SetRotation((Rotation + 90) % 360);

    public void SetRotation(int degrees)
    {
        Rotation = degrees;
        _player.Command("set", "video-rotate", degrees.ToString(CultureInfo.InvariantCulture));
        ShowOsd(Loc.F("osd.rotate", degrees));
    }

    [RelayCommand]
    public void ToggleMirror()
    {
        IsMirrored = !IsMirrored;
        _player.Command("vf", "remove", "@mirror");
        if (IsMirrored) _player.Command("vf", "add", "@mirror:lavfi=[hflip]");
    }

    [RelayCommand]
    public void OpenScreenshotFolder()
    {
        Directory.CreateDirectory(_settings.ScreenshotDirectory);
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(_settings.ScreenshotDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    public void OpenExportFolder()
    {
        Directory.CreateDirectory(_settings.ExportDirectory);
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(_settings.ExportDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    public void ExportCurrentFrame()
    {
        if (IsIdle) return;
        Directory.CreateDirectory(_settings.ExportDirectory);
        var ext = _settings.ScreenshotFormat.ToLowerInvariant();
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var file = Path.Combine(_settings.ExportDirectory, $"Nuvora-Export-{stamp}.{ext}");
        _player.Command("screenshot-to-file", file, "video");
    }

    public void ApplyDisplayFormat()
    {
        if (_dw <= 0 || _dh <= 0) return;
        var srcAr = _dw / _dh;
        var rule = srcAr < 1.45
            ? _settings.DisplayFormat43
            : (srcAr > 2.0 ? _settings.DisplayFormatCinematic : _settings.DisplayFormat169);

        switch (rule)
        {
            case "stretch":
                var targetAr = _settings.ScreenFormat == "auto" ? "16:9" : _settings.ScreenFormat;
                AspectRatio = targetAr;
                _fill = false;
                _player.Command("set", "video-aspect-override", targetAr);
                _player.Command("set", "panscan", "0");
                break;
            case "zoom":
                AspectRatio = "auto";
                _fill = true;
                _player.Command("set", "video-aspect-override", "-1");
                _player.Command("set", "panscan", "1");
                break;
            case "half-zoom":
                AspectRatio = "auto";
                _fill = true;
                _player.Command("set", "video-aspect-override", "-1");
                _player.Command("set", "panscan", "0.5");
                break;
            default:
                AspectRatio = "auto";
                _fill = false;
                _player.Command("set", "video-aspect-override", "-1");
                _player.Command("set", "panscan", "0");
                break;
        }
        OnPropertyChanged(nameof(IsFilled));
    }

    /// <summary>Lift text subtitles above the control bar while it is visible (720p-relative units).</summary>
    public void SetSubtitleMargin(double scaled) => _player.Command("set", "sub-margin-y", Inv(Math.Round(scaled)));

    // ---- recents ----
    public RecentItem? Hero => _settings.Recent.FirstOrDefault();
    public string HeroSubtitle => Hero?.HeroSubtitle ?? Loc.T("start.heroEmpty");
    public IReadOnlyList<RecentItem> RecentCards => _settings.Recent.Skip(1).Take(3).ToList();
    public IReadOnlyList<RecentItem> AllRecents => _settings.Recent;
    public bool HasHero => Hero is not null;
    public bool HasRecentCards => RecentCards.Count > 0;
    public bool HasMoreRecents => _settings.Recent.Count > 4;

    static string ThumbDir => Path.Combine(AppSettings.ConfigDir, "thumbs");

    void RememberCurrent()
    {
        if (IsIdle || _path is null || Duration <= 0) return;
        var thumb = Path.Combine(ThumbDir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(_path)))[..16] + ".jpg");
        _player.Command("screenshot-to-file", thumb, "video");
        _settings.Remember(new RecentItem
        {
            Path = _path, Title = Title, Chapter = ChapterText, Info = ResolutionLabel(_height) ?? "",
            Position = _position, Duration = Duration, Thumb = thumb,
        });
        NotifyRecentsChanged();
    }

    void NotifyRecentsChanged()
    {
        OnPropertyChanged(nameof(Hero));
        OnPropertyChanged(nameof(HeroSubtitle));
        OnPropertyChanged(nameof(RecentCards));
        OnPropertyChanged(nameof(AllRecents));
        OnPropertyChanged(nameof(HasHero));
        OnPropertyChanged(nameof(HasRecentCards));
        OnPropertyChanged(nameof(HasMoreRecents));
        OnPropertyChanged(nameof(TotalWatchTimeText));
        OnPropertyChanged(nameof(TotalRecentCount));
    }

    // save-position-on-quit only fires on quit; switching files must save explicitly.
    void SaveResumePoint()
    {
        if (IsIdle) return;
        RememberCurrent();
        if (_settings.Resume) _player.Command("write-watch-later-config");
    }

    // ---- commands ----
    [RelayCommand] void TogglePause() => _player.Command("cycle", "pause");
    public void SetPaused(bool paused) => _player.Command("set", "pause", paused ? "yes" : "no");
    public void SeekTo(double seconds) => _player.Command("seek", Inv(seconds), "absolute");
    [RelayCommand] void ToggleMute() => _player.Command("cycle", "mute");
    [RelayCommand] void SeekBack() => _player.Command("seek", Inv(-_settings.SeekStepSeconds), "relative");
    [RelayCommand] void SeekForward() => _player.Command("seek", Inv(_settings.SeekStepSeconds), "relative");
    [RelayCommand] void FrameStep() => _player.Command("frame-step");
    [RelayCommand] void FrameBackStep() => _player.Command("frame-back-step");
    [RelayCommand] void Screenshot() => _player.Command("screenshot", "video");
    [RelayCommand] void Next() { SaveResumePoint(); _player.Command("playlist-next"); }
    [RelayCommand] void Previous() { SaveResumePoint(); _player.Command("playlist-prev"); }
    [RelayCommand] void TogglePanel() { IsPanelOpen = !IsPanelOpen; IsSettingsOpen = false; IsPlaylistOpen = false; }
    [RelayCommand] void OpenSettings() { IsSettingsOpen = true; IsPanelOpen = false; IsPlaylistOpen = false; }

    /// <summary>Fit (letterbox) vs fill (crop to window).</summary>
    [RelayCommand]
    public void ToggleFill()
    {
        _fill = !_fill;
        _player.Command("set", "panscan", _fill ? "1" : "0");
        OnPropertyChanged(nameof(IsFilled));
    }

    partial void OnVolumeChanged(double value) => _player.Command("set", "volume", Inv(value));
    void OnPlayerProperty(string name, object? value)
    {
        switch (name)
        {
            case "time-pos" when !IsScrubbing:
                _position = value as double? ?? 0;
                OnPropertyChanged(nameof(Position));
                OnPropertyChanged(nameof(PositionText));
                OnPropertyChanged(nameof(RightTimeText));
                break;
            case "duration": Duration = value as double? ?? 0; break;
            case "volume": Volume = value as double? ?? Volume; break;
            case "speed": Speed = value as double? ?? 1; break;
            case "pause": IsPaused = value is true; break;
            case "mute": IsMuted = value is true; break;
            case "idle-active":
                IsIdle = value is true;
                if (IsIdle)
                {
                    IsPanelOpen = false;
                    IsPlaylistOpen = false;
                    IsSplitCompare = false;
                }
                break;
            case "media-title": Title = value as string ?? ""; OnPropertyChanged(nameof(FileChip)); OnPropertyChanged(nameof(StatsSummary)); break;
            case "path":
                _path = value as string;
                _dw = _dh = 0;
                _fittedPath = null;
                ClearThumbCache();
                _thumbnailer.SetFile(_path);
                OnPropertyChanged(nameof(FileChip));
                OnPropertyChanged(nameof(StatsSummary));
                break;
            case "track-list": Tracks = value is string t ? Track.ParseList(t) : []; break;
            case "chapter-list": Chapters = value is string c ? JsonSerializer.Deserialize<List<Chapter>>(c, Json) ?? [] : [];
                OnPropertyChanged(nameof(ChapterText));
                OnPropertyChanged(nameof(ChapterPrefix));
                OnPropertyChanged(nameof(ChapterName));
                break;
            case "chapter": ChapterIndex = (int)(value as double? ?? -1); break;
            case "playlist": Playlist = value is string p ? JsonSerializer.Deserialize<List<PlaylistEntry>>(p, Json) ?? [] : []; break;
            case "demuxer-cache-time": CacheTime = value as double? ?? 0; break;
            case "audio-delay": AudioDelay = value as double? ?? 0; break;
            case "sub-delay": SubDelay = value as double? ?? 0; break;
            case "ab-loop-a" or "ab-loop-b":
                if (name == "ab-loop-a") _abA = value as string ?? "no"; else _abB = value as string ?? "no";
                OnPropertyChanged(nameof(IsAbLoopActive));
                OnPropertyChanged(nameof(IsLoopActive));
                OnPropertyChanged(nameof(LoopLabel));
                OnPropertyChanged(nameof(AbLoopASeconds));
                OnPropertyChanged(nameof(AbLoopBSeconds));
                break;
            case "frame-drop-count" or "seeking" or "osd-dimensions/w" or "osd-dimensions/h":
                OnEnhancementProperty(name, value);
                break;
            case "mpv-version": MpvVersion = value as string ?? ""; break;
            case "audio-device-list":
                AudioDevices = (value is string d ? JsonSerializer.Deserialize<List<AudioDeviceInfo>>(d, Json) ?? [] : [])
                    .Select(a => a.Name == "auto" ? a with { Description = Loc.T("audio.auto") } : a)
                    .ToList();
                OnPropertyChanged(nameof(AudioDeviceDisplayName));
                break;
            case "video-params/dw" or "video-params/dh":
                if (name == "video-params/dw") _dw = value as double? ?? 0; else _dh = value as double? ?? 0;
                NotifyLayer();
                if (_dw > 0 && _dh > 0 && _path is not null && _path != _fittedPath)
                {
                    _fittedPath = _path;
                    ApplyDisplayFormat();
                    VideoSizeKnown?.Invoke(_dw, _dh);
                }
                break;
            case "width": _width = value as double? ?? 0; OnPropertyChanged(nameof(StatsSummary)); OnPropertyChanged(nameof(HasVideo)); break;
            case "metadata/by-key/artist": Artist = value as string; break;
            case "height":
                var prevH = _height;
                _height = value as double? ?? 0;
                if ((prevH < 1400) != (_height < 1400) && TargetFps > 0) ApplyFps();
                if (_height > 0 && _height != prevH) { RestartFallback(); ApplyImage(); }
                OnPropertyChanged(nameof(TechInfo));
                OnPropertyChanged(nameof(StatsSummary));
                break;
            case "container-fps": _sourceFps = value as double? ?? 0; OnPropertyChanged(nameof(TechInfo)); OnPropertyChanged(nameof(StatsSummary)); break;
            case "estimated-vf-fps": _outputFps = value as double? ?? 0; OnPropertyChanged(nameof(StatsSummary)); break;
            case "audio-params/samplerate": _sampleRate = value as double? ?? 0; OnPropertyChanged(nameof(StatsSummary)); break;
            case "audio-params/channel-count":
                _channelCount = (int)(value as double? ?? 0);
                OnPropertyChanged(nameof(TechInfo));
                OnPropertyChanged(nameof(StatsSummary));
                OnPropertyChanged(nameof(DownmixTitle));
                OnPropertyChanged(nameof(DownmixAvailable));
                ApplyDownmix();
                break;
            default:
                _raw[name] = value as string;
                OnPropertyChanged(nameof(TechInfo));
                OnPropertyChanged(nameof(StatsSummary));
                OnSystemProperty(name, value);
                break;
        }
    }

    string? Raw(string name) => _raw.GetValueOrDefault(name);

    public string ChapterLabel(int index) =>
        index < 0 || index >= Chapters.Count ? ""
        : Chapters[index].Title is { Length: > 0 } t ? $"{Loc.F("chapter.prefix", index + 1)} · {t}" : Loc.F("chapter.prefix", index + 1);

    public int ChapterAt(double time)
    {
        var i = -1;
        for (var k = 0; k < Chapters.Count && Chapters[k].Time <= time; k++) i = k;
        return i;
    }

    static string? ResolutionLabel(double h) => h switch
    {
        >= 2000 => "4K", >= 1400 => "1440p", >= 1000 => "1080p", >= 700 => "720p", > 0 => $"{h:0}p", _ => null,
    };

    static string CodecName(string codec) => codec switch
    {
        "hevc" => "HEVC", "h264" => "H.264", "av1" => "AV1", "vp9" => "VP9", "vp8" => "VP8", "mpeg2video" => "MPEG-2",
        "ac3" => "AC-3", "eac3" => "E-AC-3", "dts" => "DTS", "truehd" => "TrueHD", "aac" => "AAC", "mp3" => "MP3",
        "opus" => "Opus", "flac" => "FLAC", "vorbis" => "Vorbis",
        _ => codec.ToUpperInvariant(),
    };

    static string ChannelLayout(int channels) => channels switch
    {
        1 => "Mono", 2 => "Stereo", 6 => "5.1", 8 => "7.1", > 0 => $"{channels}ch", _ => "",
    };

    public double DisplayAspect => AspectRatio switch
    {
        "16:9" => 16.0 / 9.0,
        "21:9" => 21.0 / 9.0,
        "4:3" => 4.0 / 3.0,
        "1:1" => 1.0,
        _ => _dw > 0 && _dh > 0 ? _dw / _dh : (_width > 0 && _height > 0 ? _width / _height : 16.0 / 9.0),
    };

    /// <summary>Maps a window X coordinate to the normalized [0..1] video texture X coordinate, accounting for letterbox/pillarbox/crop.</summary>
    public double ComputeVideoSplit(double screenX, double windowW, double windowH)
    {
        if (windowW <= 0 || windowH <= 0) return 0.5;
        var ar = DisplayAspect;
        if (Rotation is 90 or 270 && ar > 0) ar = 1.0 / ar;
        var winAr = windowW / windowH;
        if (!IsFilled && ar > 0 && winAr > ar)
        {
            var videoW = windowH * ar;
            var leftPad = (windowW - videoW) / 2.0;
            return Math.Clamp((screenX - leftPad) / videoW, 0.0, 1.0);
        }
        if (IsFilled && ar > 0 && winAr < ar)
        {
            var fullVideoW = windowH * ar;
            var cropLeft = (fullVideoW - windowW) / 2.0;
            return Math.Clamp((cropLeft + screenX) / fullVideoW, 0.0, 1.0);
        }
        return Math.Clamp(screenX / windowW, 0.0, 1.0);
    }

    // mpv parses numbers with '.', the Turkish culture would format 1,5.
    static string Inv(double d) => d.ToString(CultureInfo.InvariantCulture);
    public static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"hh\:mm\:ss");
}
