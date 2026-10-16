using Avalonia.Input;
using Player.App;

namespace Player.Core.Tests;

public class ShortcutsTests
{
    [Fact]
    public void Matches_NamedKey_ChecksKeyAndModifiers()
    {
        Assert.True(Shortcuts.Matches("Space", Key.Space, " ", KeyModifiers.None));
        Assert.False(Shortcuts.Matches("Space", Key.Space, " ", KeyModifiers.Control));
        Assert.True(Shortcuts.Matches("Ctrl+Up", Key.Up, null, KeyModifiers.Control));
        Assert.False(Shortcuts.Matches("Ctrl+Up", Key.Up, null, KeyModifiers.None));
    }

    [Fact]
    public void Matches_CharacterKey_FollowsSymbolAndCtrl()
    {
        // Turkish Q produces '.' or '\' with different physical keys / AltGr; symbol match handles layout differences.
        Assert.True(Shortcuts.Matches(".", Key.OemQuestion, ".", KeyModifiers.None));
        Assert.True(Shortcuts.Matches(@"\", Key.OemBackslash, @"\", KeyModifiers.Control | KeyModifiers.Alt)); // AltGr
        Assert.True(Shortcuts.Matches("Ctrl+O", Key.O, "\u000f", KeyModifiers.Control));
        Assert.False(Shortcuts.Matches("Z", Key.Z, "z", KeyModifiers.Control));
    }

    [Fact]
    public void FromKey_IgnoresBareModifiers_AndFormatsBindings()
    {
        Assert.Null(Shortcuts.FromKey(Key.LeftCtrl, null, KeyModifiers.Control));
        Assert.Null(Shortcuts.FromKey(Key.RightShift, null, KeyModifiers.Shift));
        Assert.Equal("Space", Shortcuts.FromKey(Key.Space, " ", KeyModifiers.None));
        Assert.Equal("Ctrl+PageDown", Shortcuts.FromKey(Key.PageDown, null, KeyModifiers.Control));
        Assert.Equal("Z", Shortcuts.FromKey(Key.Z, "z", KeyModifiers.None));
        Assert.Equal("Ctrl+O", Shortcuts.FromKey(Key.O, "o", KeyModifiers.Control));
        Assert.Equal(@"\", Shortcuts.FromKey(Key.OemBackslash, @"\", KeyModifiers.Control | KeyModifiers.Alt));
    }

    [Fact]
    public void Find_And_Display_RespectCustomAndUnboundKeys()
    {
        var custom = new Dictionary<string, string>
        {
            ["pause"] = "K",
            ["mute"] = "", // unbound
        };
        Assert.Equal("pause", Shortcuts.Find(custom, Key.K, "k", KeyModifiers.None));
        Assert.Null(Shortcuts.Find(custom, Key.Space, " ", KeyModifiers.None));
        Assert.Null(Shortcuts.Find(custom, Key.M, "m", KeyModifiers.None));
        Assert.Equal("Boşluk", Shortcuts.Display("Space"));
        Assert.Equal("Ctrl+↑", Shortcuts.Display("Ctrl+Up"));
        Assert.Equal("—", Shortcuts.Display(""));
        Assert.Equal("split-compare", Shortcuts.Find(custom, Key.C, "c", KeyModifiers.None));
    }
}
