using Avalonia.Media;
using Player.App;

namespace Player.Core.Tests;

public class ColorContrastTests
{
    [Fact]
    public void Ratio_MatchesWcagReferenceValues()
    {
        Assert.Equal(21, ColorContrast.Ratio(Colors.White, Colors.Black), 1);
        Assert.Equal(1, ColorContrast.Ratio(Colors.Gray, Colors.Gray), 3);
    }

    [Theory]
    [InlineData("#6D5AE6")]
    [InlineData("#00A2FF")]
    [InlineData("#D6245F")]
    [InlineData("#0F766E")]
    [InlineData("#C2410C")]
    public void EveryAccent_GetsReadableTextAndForeground(string hex)
    {
        var accent = Color.Parse(hex);
        Assert.True(ColorContrast.Ratio(ColorContrast.Lighten(accent, ColorContrast.Surface), ColorContrast.Surface) >= 4.5);
        Assert.True(ColorContrast.Ratio(ColorContrast.ReadableOn(accent), accent) >= 4.5);
    }

    [Fact]
    public void Lighten_KeepsColoursThatAlreadyPass()
    {
        var cyan = Color.Parse("#06B6D4");
        Assert.Equal(cyan, ColorContrast.Lighten(cyan, ColorContrast.Surface));
    }
}
