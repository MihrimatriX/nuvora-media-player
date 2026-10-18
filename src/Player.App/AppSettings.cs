using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace Player.App;

public sealed class AppSettings
{
    // Genel
    public string Language { get; set; } = "tr";
    public string Accent { get; set; } = "#6D5AE6";
    public bool AlwaysOnTop { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public bool SmartSeek { get; set; } = true;
    public bool HideTopBar { get; set; }
    public string WheelAction { get; set; } = "volume"; // "volume" | "seek" | "speed"
    // Oynatma
    public bool Resume { get; set; } = true;
    public bool AutoNext { get; set; } = true;
    public bool StartFullscreen { get; set; }
    public bool PlayInFullscreen { get; set; }
    public bool FitWindowToVideo { get; set; } = true;
    public string LoopMode { get; set; } = "off"; // "off" | "file" | "playlist"
    public int SeekStepSeconds { get; set; } = 10;
    public string ScreenshotDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Nuvora Player");
    // Video & FPS
    public bool HardwareDecoding { get; set; } = true;
    public string HwdecMode { get; set; } = "auto-safe"; // "auto-safe" | "d3d11va" | "nvdec" | "dxva2" | "auto-copy"
    public string VideoOutput { get; set; } = "gpu-next";
    public bool Deinterlace { get; set; } = true;
    public string DeinterlaceMode { get; set; } = "auto"; // "auto" | "yes" | "no"
    public bool AutoInterlaceDetection { get; set; } = true;
    public bool Motion2Uhd4K { get; set; } = true;
    public bool SmoothMotionByDefault { get; set; }
    public int TargetFps { get; set; } // 0 = Kaynak, -1 = Ekran Hz, 60 / 90 / 120 = Hedef FPS
    public string MotionScaler { get; set; } = "oversample"; // "oversample" | "mitchell" | "sphinx" | "catmull_rom"
    public string ToneMapping { get; set; } = "auto"; // "auto" | "bt.2446a" | "spline" | "hable" | "mobius"
    public string DefaultPreset { get; set; } = "balanced";
    public bool ShaderUpscaling { get; set; } = true; // GPU shader layer (FSRCNNX / ArtCNN / Anime4K) on top of the presets
    public bool AutoFallback { get; set; } = true; // step down the shader chain when frames drop
    public string ScreenshotFormat { get; set; } = "png";
    public string ScreenFormat { get; set; } = "16:9"; // "auto" | "16:9" | "16:10" | "21:9" | "4:3"
    public string DisplayFormat43 { get; set; } = "normal"; // "normal" | "stretch" | "zoom"
    public string DisplayFormat169 { get; set; } = "normal"; // "normal" | "stretch" | "zoom"
    public string DisplayFormatCinematic { get; set; } = "normal"; // "normal" | "zoom" | "half-zoom"
    // Ses
    public string AudioOutputMode { get; set; } = "stereo-surround"; // "stereo" | "stereo-surround" | "5.1" | "7.1" | "spdif" | "hdmi"
    public string AudioLanguages { get; set; } = "tur,eng,jpn";
    public string AudioDevice { get; set; } = "auto";
    public string SpdifDevice { get; set; } = "auto";
    public string HdmiDevice { get; set; } = "auto";
    public bool AudioExclusive { get; set; }
    public int LipsyncMs { get; set; } // -500 .. +500 ms
    public bool NormalizeByDefault { get; set; }
    public bool BoostByDefault { get; set; }
    public int VolumeStep { get; set; } = 5;
    public double Volume { get; set; } = 100;
    // Altyazı
    public string SubtitleLanguages { get; set; } = "tur,eng";
    public double SubtitleScale { get; set; } = 1;
    public int SubtitlePosition { get; set; } = 100; // 50..105 (mpv sub-pos)
    public bool AutoLoadSubtitles { get; set; } = true;
    public string SubtitleColor { get; set; } = "#FFFFFFFF";
    public bool SubtitleBackground { get; set; }
    public bool OverrideAssStyles { get; set; }
    // Dışa Aktar (Export)
    public bool ExportHwDecode { get; set; } = true;
    public bool ExportHwEncode { get; set; } = true;
    public string ExportEncoder { get; set; } = "nvenc"; // "nvenc" | "amf" | "qsv" | "cpu"
    public string ExportDeinterlace { get; set; } = "auto"; // "auto" | "disabled"
    public string ExportDirectory { get; set; } =
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) is { Length: > 0 } v
            ? v
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
    // Kısayollar: action id → key binding ("Space", "Ctrl+O", "."); missing ids use Shortcuts.Defaults.
    public Dictionary<string, string> Shortcuts { get; set; } = [];

    public List<RecentItem> Recent { get; set; } = [];

    /// <summary>"portable" folder next to the exe if it exists, otherwise %AppData%\Nuvora Player.</summary>
    public static string ConfigDir { get; } = ResolveConfigDir();

    static string FilePath => Path.Combine(ConfigDir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    static string BackupPath => FilePath + ".bak";

    public static AppSettings Load()
    {
        // A corrupt main file (crash mid-write before Save became atomic, disk error) falls back to the
        // backup Save keeps, so the user does not silently lose shortcuts and history.
        foreach (var path in new[] { FilePath, BackupPath })
        {
            try
            {
                if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                AppLog.Warn($"settings unreadable, trying next copy: {path}: {e.Message}");
            }
        }
        return new();
    }

    /// <summary>Writes to a temp file, then swaps it in with <see cref="File.Replace(string, string, string?)"/>,
    /// which keeps the previous file as settings.json.bak. A crash mid-write leaves the old file intact.</summary>
    public void Save()
    {
        var tmp = FilePath + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                JsonSerializer.Serialize(fs, this, Json);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, BackupPath, ignoreMetadataErrors: true);
            else File.Move(tmp, FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings are not worth crashing playback for; the next Save retries.
            AppLog.Warn($"settings not saved: {e.Message}");
        }
    }

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, Json))!;

    /// <summary>Takes every user-editable value from <paramref name="draft"/>; history and volume stay as they are.</summary>
    public void CopyFrom(AppSettings draft)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
            if (p.CanWrite && p.Name is not (nameof(Recent) or nameof(Volume)))
                p.SetValue(this, p.GetValue(draft));
    }

    public void Remember(RecentItem item)
    {
        Recent.RemoveAll(r => string.Equals(r.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, item);
        if (Recent.Count > 12) Recent.RemoveRange(12, Recent.Count - 12);
    }

    public void ClearRecent()
    {
        Recent.Clear();
        Save();
    }

    static string ResolveConfigDir()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "portable");
        var dir = Directory.Exists(portable)
            ? portable
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Nuvora Player");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>A "Son oynatılanlar" entry, saved when leaving a file.</summary>
public sealed class RecentItem
{
    public string Path { get; set; } = "";
    public string Title { get; set; } = "";
    public string Chapter { get; set; } = "";
    public string Info { get; set; } = "";
    public string Thumb { get; set; } = "";
    public double Position { get; set; }
    public double Duration { get; set; }

    [JsonIgnore] public double Progress => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;

    [JsonIgnore]
    public string Remaining
    {
        get
        {
            var left = TimeSpan.FromSeconds(Math.Max(0, Duration - Position));
            return left.TotalHours >= 1 ? $"{(int)left.TotalHours} sa {left.Minutes} dk kaldı" : $"{Math.Max(1, left.Minutes)} dk kaldı";
        }
    }

    [JsonIgnore] public string Subtitle => Info.Length > 0 ? $"{Remaining} · {Info}" : Remaining;
    [JsonIgnore] public string HeroSubtitle => Chapter.Length > 0 ? $"{Chapter} — {Remaining}" : Remaining;

    Bitmap? _thumbnail;
    [JsonIgnore] public Bitmap? Thumbnail => _thumbnail ??= LoadThumbnail();
    [JsonIgnore] public bool HasThumbnail => Thumbnail is not null;

    Bitmap? LoadThumbnail()
    {
        if (!File.Exists(Thumb)) return null;
        try
        {
            using var ms = new MemoryStream(File.ReadAllBytes(Thumb));
            return new Bitmap(ms);
        }
        catch { return null; }
    }
}
