using Player.Enhancement;

namespace Player.Core.Tests;

public class ImagePresetTests
{
    [Fact]
    public void EveryPresetSetsTheSameKeys()
    {
        // Switching presets must overwrite every option the previous one touched.
        var keys = ImagePresets.All[0].Options.Keys.Order().ToList();
        Assert.All(ImagePresets.All, p => Assert.Equal(keys, p.Options.Keys.Order().ToList()));
    }

    [Fact]
    public void UnknownIdFallsBackToBalanced() => Assert.Equal("balanced", ImagePresets.Find("nope").Id);
}
