namespace Player.App;

/// <summary>Start-screen system cards: GPU, decoder, audio output and engine, read from mpv instead of fixed labels.</summary>
public sealed partial class PlayerViewModel
{
    // mpv clears these when playback stops, so the last real value is kept for the idle screen.
    string? _lastHwdec, _lastAo;

    public string GpuDisplayName => string.IsNullOrWhiteSpace(Gpu?.Name) ? Loc.T("sys.gpuUnknown") : Gpu.Name;
    public string HwdecDisplayName => SystemInfo.Decoder(_settings.HardwareDecoding, _settings.HwdecMode, _lastHwdec);
    public string HwdecDetail => SystemInfo.DecoderDetail(_settings.HardwareDecoding, _lastHwdec, Raw("current-vo") ?? _settings.VideoOutput);
    public string AudioBackendDisplayName => SystemInfo.AudioOutput(_lastAo, _settings.AudioExclusive);
    public string AudioDeviceDisplayName =>
        AudioDevices.FirstOrDefault(d => d.Name == _settings.AudioDevice)?.Description ?? _settings.AudioDevice;
    public string MpvEngineDisplayName => string.IsNullOrWhiteSpace(MpvVersion) ? "libmpv" : MpvVersion;
    public string FfmpegDisplayName => Raw("ffmpeg-version") is { Length: > 0 } v ? $"FFmpeg {v}" : "FFmpeg";

    public string TotalWatchTimeText => SystemInfo.WatchTime(_settings.Recent.Sum(r => r.Position));
    public int TotalRecentCount => _settings.Recent.Count;

    void OnSystemProperty(string name, object? value)
    {
        switch (name)
        {
            case "hwdec-current" when value is string { Length: > 0 } h:
                _lastHwdec = h;
                break;
            case "current-ao" when value is string { Length: > 0 } ao:
                _lastAo = ao;
                break;
            case "current-vo" or "ffmpeg-version":
                break;
            default:
                return;
        }
        NotifySystemInfo();
    }

    void NotifySystemInfo()
    {
        OnPropertyChanged(nameof(HwdecDisplayName));
        OnPropertyChanged(nameof(HwdecDetail));
        OnPropertyChanged(nameof(AudioBackendDisplayName));
        OnPropertyChanged(nameof(AudioDeviceDisplayName));
        OnPropertyChanged(nameof(FfmpegDisplayName));
    }

    partial void OnMpvVersionChanged(string value) => OnPropertyChanged(nameof(MpvEngineDisplayName));
}

/// <summary>Label formatting for the system cards; pure so it can be unit tested.</summary>
public static class SystemInfo
{
    /// <summary>Decoder actually used last ("d3d11va" → "D3D11VA"), else the configured mode.</summary>
    public static string Decoder(bool hwEnabled, string mode, string? lastUsed)
    {
        if (!hwEnabled) return Loc.T("sys.software");
        if (lastUsed is null) return mode.StartsWith("auto", StringComparison.Ordinal) ? Loc.F("sys.autoMode", mode) : mode.ToUpperInvariant();
        if (lastUsed == "no") return Loc.T("sys.software");
        return lastUsed.EndsWith("-copy", StringComparison.Ordinal) ? Loc.F("sys.copy", lastUsed[..^5].ToUpperInvariant()) : lastUsed.ToUpperInvariant();
    }

    public static string DecoderDetail(bool hwEnabled, string? lastUsed, string vo)
    {
        var state = !hwEnabled ? Loc.T("sys.hwOff")
            : lastUsed is null ? Loc.T("sys.notPlayedYet")
            : lastUsed == "no" ? Loc.T("sys.hwUnavailable")
            : Loc.T("sys.hwUsed");
        return $"{state} · {vo}";
    }

    /// <summary>"wasapi" + exclusive → "WASAPI · Exclusive". Unknown until mpv opens an audio output.</summary>
    public static string AudioOutput(string? ao, bool exclusive)
    {
        var name = ao is null ? (OperatingSystem.IsWindows() ? "WASAPI" : Loc.T("sys.auto")) : ao.ToUpperInvariant();
        return $"{name} · {(exclusive ? Loc.T("sys.exclusive") : Loc.T("sys.shared"))}";
    }

    public static string WatchTime(double totalSeconds)
    {
        if (totalSeconds < 60) return Loc.F("sys.minutes", 0);
        var t = TimeSpan.FromSeconds(totalSeconds);
        return t.TotalHours >= 1 ? Loc.F("sys.hoursMinutes", (int)t.TotalHours, t.Minutes) : Loc.F("sys.minutes", t.Minutes);
    }
}
