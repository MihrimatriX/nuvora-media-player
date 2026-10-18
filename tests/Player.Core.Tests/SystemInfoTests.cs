using Player.App;

namespace Player.Core.Tests;

[Collection(nameof(LocTests))]
public class SystemInfoTests
{
    public SystemInfoTests() => Loc.SetLanguage("tr");

    [Theory]
    [InlineData(false, "auto-safe", "d3d11va", "Yazılım (CPU)")]
    [InlineData(true, "auto-safe", null, "Otomatik (auto-safe)")]
    [InlineData(true, "nvdec", null, "NVDEC")]
    [InlineData(true, "auto-safe", "d3d11va", "D3D11VA")]
    [InlineData(true, "auto-copy", "nvdec-copy", "NVDEC (kopya)")]
    [InlineData(true, "auto-safe", "no", "Yazılım (CPU)")]
    public void Decoder_PrefersLastUsedValue(bool hw, string mode, string? last, string expected) =>
        Assert.Equal(expected, SystemInfo.Decoder(hw, mode, last));

    [Fact]
    public void AudioOutput_ShowsBackendAndMode()
    {
        Assert.Equal("WASAPI · Özel kip", SystemInfo.AudioOutput("wasapi", exclusive: true));
        Assert.Equal("PULSE · Paylaşımlı", SystemInfo.AudioOutput("pulse", exclusive: false));
    }

    [Theory]
    [InlineData(0, "0 dk")]
    [InlineData(59, "0 dk")]
    [InlineData(125, "2 dk")]
    [InlineData(3 * 3600 + 15 * 60, "3 sa 15 dk")]
    public void WatchTime_FormatsHoursAndMinutes(double seconds, string expected) =>
        Assert.Equal(expected, SystemInfo.WatchTime(seconds));
}
