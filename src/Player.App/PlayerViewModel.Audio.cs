using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Player.App;

/// <summary>One speaker of the 5.1 downmix diagram. Index is the input channel (FL FR FC LFE BL BR).</summary>
public sealed partial class ChannelGain(string label, int index) : ObservableObject
{
    public string Label { get; } = label;
    public int Index { get; } = index;
    [ObservableProperty] public partial int Gain { get; set; } = 100;
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

/// <summary>"Ses" tab: boost, normalization, DRC, A/V delays, stereo downmix.</summary>
public sealed partial class PlayerViewModel
{
    public static readonly (string Label, double Value)[] DrcLevels =
        [("Kapalı", 0), ("Hafif", 0.5), ("Sıkıştırılmış", 1), ("Gece modu", 2)];

    int _channelCount;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(VolumeMax))] public partial bool BoostEnabled { get; set; }
    [ObservableProperty] public partial bool Normalize { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DrcLabel))] public partial int DrcIndex { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AudioDelayText))] public partial double AudioDelay { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SubDelayText))] public partial double SubDelay { get; set; }

    public IReadOnlyList<ChannelGain> Channels { get; }
    public double VolumeMax => BoostEnabled ? 200 : 100;
    public string BoostText => $"{Volume:0}%";
    public string DrcLabel => DrcLevels[DrcIndex].Label;
    public string AudioDelayText => Ms(AudioDelay);
    public string SubDelayText => Ms(SubDelay);
    public bool DownmixAvailable => _channelCount > 2;
    public string DownmixTitle => _channelCount > 2 ? $"Stereo downmix · {ChannelLayout(_channelCount)}" : "Stereo downmix · kaynak stereo";

    [RelayCommand] void AudioDelayDown() => _player.Command("add", "audio-delay", "-0.05");
    [RelayCommand] void AudioDelayUp() => _player.Command("add", "audio-delay", "0.05");
    [RelayCommand] void SubDelayDown() => _player.Command("add", "sub-delay", "-0.05");
    [RelayCommand] void SubDelayUp() => _player.Command("add", "sub-delay", "0.05");

    [RelayCommand]
    void SelectChannel(ChannelGain channel)
    {
        foreach (var c in Channels) c.IsSelected = c == channel;
    }

    [RelayCommand]
    void ResetDownmix()
    {
        foreach (var c in Channels) c.Gain = 100;
    }

    partial void OnBoostEnabledChanged(bool value)
    {
        _player.Command("set", "volume-max", value ? "200" : "100");
        if (!value && Volume > 100) Volume = 100;
    }

    partial void OnNormalizeChanged(bool value) => ApplyAudioFilters();

    // ponytail: ad-lavc-ac3drc is read when the decoder opens, so a new level applies from the next file/track.
    partial void OnDrcIndexChanged(int value) =>
        _player.Command("set", "ad-lavc-ac3drc", DrcLevels[value].Value.ToString(CultureInfo.InvariantCulture));

    IReadOnlyList<ChannelGain> CreateChannels()
    {
        // Display order follows the diagram: front row, then rear row.
        ChannelGain[] list = [new("Sol", 0), new("Merkez", 2), new("Sağ", 1), new("Arka sol", 4), new("Bas (LFE)", 3), new("Arka sağ", 5)];
        list[1].IsSelected = true;
        foreach (var c in list)
            c.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ChannelGain.Gain)) ApplyDownmix(); };
        return list;
    }

    public void SetAudioDelayMs(int ms)
    {
        var clamped = Math.Clamp(ms, -500, 500);
        _settings.LipsyncMs = clamped;
        _player.Command("set", "audio-delay", (clamped / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
    }

    void ApplyAudioFilters()
    {
        _player.Command("af", "remove", "@norm");
        _player.Command("af", "remove", "@surround");
        if (Normalize) _player.Command("af", "add", "@norm:lavfi=[dynaudnorm=f=250:g=31]");

        switch (_settings.AudioOutputMode)
        {
            case "stereo":
                _player.Command("set", "audio-spdif", "");
                _player.Command("set", "audio-channels", "stereo");
                break;
            case "stereo-surround":
                _player.Command("set", "audio-spdif", "");
                _player.Command("set", "audio-channels", "stereo");
                _player.Command("af", "add", "@surround:lavfi=[extrastereo=m=1.25]");
                break;
            case "5.1":
                _player.Command("set", "audio-spdif", "");
                _player.Command("set", "audio-channels", "5.1");
                break;
            case "7.1":
                _player.Command("set", "audio-spdif", "");
                _player.Command("set", "audio-channels", "7.1");
                break;
            case "spdif":
                _player.Command("set", "audio-channels", "auto-safe");
                _player.Command("set", "audio-spdif", "ac3,dts");
                if (!string.IsNullOrEmpty(_settings.SpdifDevice) && _settings.SpdifDevice != "auto")
                    _player.Command("set", "audio-device", _settings.SpdifDevice);
                break;
            case "hdmi":
                _player.Command("set", "audio-channels", "auto-safe");
                _player.Command("set", "audio-spdif", "ac3,eac3,dts,dts-hd,truehd");
                if (!string.IsNullOrEmpty(_settings.HdmiDevice) && _settings.HdmiDevice != "auto")
                    _player.Command("set", "audio-device", _settings.HdmiDevice);
                break;
        }
        if (_settings.LipsyncMs != 0)
            _player.Command("set", "audio-delay", (_settings.LipsyncMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture));

        ApplyDownmix();
    }

    /// <summary>5.1 → stereo with per-speaker gains. Untouched gains leave mpv's own downmix in charge.</summary>
    void ApplyDownmix()
    {
        _player.Command("af", "remove", "@downmix");
        if (!DownmixAvailable || Channels.All(c => c.Gain == 100)) return;
        double G(int index, double k) => Channels.First(c => c.Index == index).Gain / 100.0 * k;
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var pan = $"pan=stereo" +
                  $"|FL={F(G(0, 1))}*c0+{F(G(2, 0.707))}*c2+{F(G(3, 0.5))}*c3+{F(G(4, 0.707))}*c4" +
                  $"|FR={F(G(1, 1))}*c1+{F(G(2, 0.707))}*c2+{F(G(3, 0.5))}*c3+{F(G(5, 0.707))}*c5";
        _player.Command("af", "add", $"@downmix:lavfi=[{pan}]");
    }

    static string Ms(double seconds)
    {
        var ms = (int)Math.Round(seconds * 1000);
        return ms < 0 ? $"−{-ms} ms" : $"+{ms} ms";
    }
}
