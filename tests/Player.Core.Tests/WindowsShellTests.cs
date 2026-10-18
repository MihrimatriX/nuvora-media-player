using Player.Windows;

namespace Player.Core.Tests;

public class WindowsShellTests
{
    [Theory]
    [InlineData(@"C:\Videos\film.mkv", @"C:\Videos\film.mkv")]
    [InlineData(@"C:\My Videos\film 1.mkv", @"""C:\My Videos\film 1.mkv""")]
    [InlineData(@"\\nas\share\a b\", @"""\\nas\share\a b\\""")]
    [InlineData(@"https://x.test/a b?q=""1""", @"""https://x.test/a b?q=\""1\""""")]
    [InlineData("", @"""""")]
    public void QuoteArgument_round_trips_through_CommandLineToArgv_rules(string input, string expected) =>
        Assert.Equal(expected, JumpList.QuoteArgument(input));

    [Theory]
    [InlineData(Glyph.Play)]
    [InlineData(Glyph.Pause)]
    [InlineData(Glyph.Previous)]
    [InlineData(Glyph.Next)]
    public void Glyphs_are_white_with_antialiased_edges(Glyph glyph)
    {
        foreach (var size in new[] { 16, 20, 24, 32 })
        {
            var pixels = GlyphIcons.Rasterize(glyph, size);
            Assert.Equal(size * size, pixels.Length);
            Assert.All(pixels, p => Assert.Equal(0x00FFFFFFu, p & 0x00FFFFFFu));
            var alphas = pixels.Select(p => p >> 24).ToList();
            Assert.Contains(255u, alphas);                       // solid interior
            if (size == 20) Assert.Contains(alphas, a => a is > 0 and < 255); // soft edges off the pixel grid
            Assert.Equal(0u, alphas[0]);                         // transparent corner
            var coverage = alphas.Sum(a => (double)a) / (255.0 * size * size);
            Assert.InRange(coverage, 0.15, 0.6);
        }
    }

    [Fact]
    public void Pause_glyph_has_a_gap_between_its_bars()
    {
        var pixels = GlyphIcons.Rasterize(Glyph.Pause, 16);
        Assert.Equal(0u, pixels[8 * 16 + 8] >> 24); // between x=7 and x=9
        Assert.Equal(255u, pixels[8 * 16 + 5] >> 24);
        Assert.Equal(255u, pixels[8 * 16 + 10] >> 24);
    }

    [Fact]
    public void Timeline_is_sent_on_jumps_and_periodically_not_every_tick()
    {
        var t = new TimelineThrottle();
        Assert.True(t.ShouldSend(10, 100, 1, 0));        // first
        Assert.False(t.ShouldSend(10.25, 100, 1, 250));  // normal playback, extrapolates fine
        Assert.False(t.ShouldSend(12, 100, 1, 2000));
        Assert.True(t.ShouldSend(40, 100, 1, 2250));     // seek
        Assert.False(t.ShouldSend(41, 100, 1, 3250));
        Assert.True(t.ShouldSend(43, 100, 1, 7250));     // periodic refresh
        Assert.True(t.ShouldSend(43, 100, 0, 7300));     // paused: rate changed
        Assert.False(t.ShouldSend(43, 100, 0, 9000));    // paused position stays put
        t.Invalidate();
        Assert.True(t.ShouldSend(43, 100, 0, 9100));
        Assert.True(t.ShouldSend(43, 120, 0, 9200));     // duration changed
    }
}
