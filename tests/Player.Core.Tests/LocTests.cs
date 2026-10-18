using System.Text.RegularExpressions;
using Player.App;
using Player.Enhancement;

namespace Player.Core.Tests;

/// <summary>Loc.SetLanguage is global; these tests must not run alongside tests that read UI strings.</summary>
[CollectionDefinition(nameof(LocTests), DisableParallelization = true)]
public sealed class LocCollection;

[Collection(nameof(LocTests))]
public partial class LocTests
{
    [Fact]
    public void TurkishAndEnglish_HaveTheSameKeys()
    {
        Assert.Empty(Loc.Keys("tr").Except(Loc.Keys("en")));
        Assert.Empty(Loc.Keys("en").Except(Loc.Keys("tr")));
        Assert.True(Loc.Keys("tr").Count > 300);
    }

    [Fact]
    public void Translations_KeepFormatPlaceholders()
    {
        foreach (var key in Loc.Keys("tr"))
        {
            Loc.SetLanguage("tr");
            var tr = Placeholders().Matches(Loc.T(key)).Select(m => m.Value).Order();
            Loc.SetLanguage("en");
            var en = Placeholders().Matches(Loc.T(key)).Select(m => m.Value).Order();
            Assert.True(tr.SequenceEqual(en), key);
        }
        Loc.SetLanguage("tr");
    }

    [Fact]
    public void EveryKeyUsedInSource_Exists()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "NuvoraPlayer.slnx"))) root = Path.GetDirectoryName(root)!;
        var prefixes = Loc.Keys("tr").Select(k => k[..k.IndexOf('.')]).ToHashSet();
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "src", "Player.App"), "*.*", SearchOption.TopDirectoryOnly)
                     .Where(f => f.EndsWith(".axaml") || f.EndsWith(".cs")))
        {
            var text = File.ReadAllText(file);
            var keys = XamlKey().Matches(text).Select(m => m.Groups[1].Value)
                .Concat(CodeKey().Matches(text).Select(m => m.Groups[1].Value))
                .Where(k => prefixes.Contains(k[..k.IndexOf('.')]) && !k.EndsWith(".json")); // "settings.json" is a file name
            missing.AddRange(keys.Where(k => !Loc.Has("tr", k)).Select(k => $"{Path.GetFileName(file)}: {k}"));
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void DynamicKeys_Exist()
    {
        foreach (var id in PlayerViewModel.Profiles) Assert.True(Loc.Has("en", $"profile.{id}"), id);
        foreach (var p in ImagePresets.All) Assert.True(Loc.Has("en", $"preset.{p.Id}") && Loc.Has("en", $"preset.{p.Id}.desc"), p.Id);
        foreach (var lang in new[] { "auto", "tr", "en" }) Assert.True(Loc.Has("en", $"lang.{lang}"), lang);
    }

    [Fact]
    public void SetLanguage_SwitchesAndNotifiesBindings()
    {
        Loc.SetLanguage("tr");
        var changed = 0;
        Loc.Instance.PropertyChanged += (_, e) => { if (e.PropertyName == "Item") changed++; };
        Loc.SetLanguage("en");
        Assert.Equal("Open file", Loc.Instance["start.openFile"]);
        Loc.SetLanguage("tr");
        Assert.Equal("Dosya aç", Loc.Instance["start.openFile"]);
        Assert.Equal(2, changed);
        Assert.Equal("no.such.key", Loc.T("no.such.key"));
    }

    [GeneratedRegex(@"\{\d+\}")] private static partial Regex Placeholders();
    [GeneratedRegex(@"\{app:T ([a-z]+\.[\w.]+)\}")] private static partial Regex XamlKey();
    [GeneratedRegex(@"""([a-z]+\.[A-Za-z0-9.]+)""")] private static partial Regex CodeKey();
}
