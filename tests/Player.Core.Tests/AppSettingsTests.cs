using Player.App;

namespace Player.Core.Tests;

public class AppSettingsTests
{
    [Fact]
    public void CopyFrom_DoesNotShareShortcutsWithDraft()
    {
        var live = new AppSettings();
        var draft = live.Clone();
        draft.Shortcuts["open"] = "Ctrl+O";

        live.CopyFrom(draft);
        draft.Shortcuts["open"] = "Ctrl+P";

        Assert.Equal("Ctrl+O", live.Shortcuts["open"]);
    }

    [Fact]
    public void CopyFrom_KeepsHistoryVolumeAndSchemaVersion()
    {
        var live = new AppSettings { Volume = 40, SchemaVersion = AppSettings.CurrentSchemaVersion };
        live.Recent.Add(new RecentItem { Path = "a.mkv" });
        var draft = new AppSettings { Volume = 90, SchemaVersion = 0, SeekStepSeconds = 30 };

        live.CopyFrom(draft);

        Assert.Equal(30, live.SeekStepSeconds);
        Assert.Equal(40, live.Volume);
        Assert.Single(live.Recent);
        Assert.Equal(AppSettings.CurrentSchemaVersion, live.SchemaVersion);
    }

    [Fact]
    public void UnversionedFile_IsMigratedToCurrentSchema()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"SeekStepSeconds":5}""")!;
        Assert.Equal(0, old.SchemaVersion);
        Assert.Equal(AppSettings.CurrentSchemaVersion, AppSettings.Migrate(old).SchemaVersion);
        Assert.Equal(5, old.SeekStepSeconds);
    }
}
