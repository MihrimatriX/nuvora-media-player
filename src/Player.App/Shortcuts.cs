using Avalonia.Input;

namespace Player.App;

/// <summary>Customisable key bindings. A binding is "[Ctrl+][Alt+][Shift+]Key" where Key is either an
/// Avalonia <see cref="Key"/> name ("Space", "PageDown", "F11") or a single typed character (".", "\", "Z").
/// Characters match the typed symbol, so they follow the keyboard layout (Turkish Q moves ',' '.' '\').</summary>
public static class Shortcuts
{
    public sealed record Action(string Id, string Label, string Default);

    public static readonly IReadOnlyList<Action> All =
    [
        new("pause", "Oynat / duraklat", "Space"),
        new("seek-back", "Geri atla", "Left"),
        new("seek-forward", "İleri atla", "Right"),
        new("volume-up", "Sesi aç", "Up"),
        new("volume-down", "Sesi kıs", "Down"),
        new("mute", "Sessiz", "M"),
        new("fullscreen", "Tam ekran", "F"),
        new("frame-back", "Kare geri", ","),
        new("frame-step", "Kare ileri", "."),
        new("prev", "Önceki dosya", "PageUp"),
        new("next", "Sonraki dosya", "PageDown"),
        new("speed-down", "Yavaşlat", "Ctrl+Down"),
        new("speed-up", "Hızlandır", "Ctrl+Up"),
        new("fps-cycle", "FPS hedefi (Kaynak/60/90/120)", "G"),
        new("loop", "Tekrarlama kipi", "L"),
        new("ab-loop", "A–B döngüsü", "B"),
        new("aspect", "En-boy oranı", "A"),
        new("fill", "Sığdır / doldur", "Z"),
        new("rotate", "90° döndür", "R"),
        new("ontop", "Her zaman üstte", "T"),
        new("screenshot", "Kare yakala", "S"),
        new("stats", "Medya istatistikleri", "I"),
        new("panel", "Görüntü & Ses paneli", "E"),
        new("split-compare", "Önce / Sonra karşılaştırma çubuğu", "C"),
        new("compare", "Orijinali göster (basılı tut)", "\\"),
        new("open", "Dosya aç", "Ctrl+O"),
        new("settings", "Ayarlar", "Ctrl+,"),
    ];

    public static string BindingFor(IReadOnlyDictionary<string, string> custom, string id) =>
        custom.TryGetValue(id, out var b) ? b : All.First(a => a.Id == id).Default;

    /// <summary>Which action a key press triggers, or null.</summary>
    public static string? Find(IReadOnlyDictionary<string, string> custom, Key key, string? symbol, KeyModifiers mods) =>
        All.FirstOrDefault(a => Matches(BindingFor(custom, a.Id), key, symbol, mods))?.Id;

    public static bool Matches(string binding, Key key, string? symbol, KeyModifiers mods)
    {
        if (binding.Length == 0) return false;
        var (wantMods, keyPart) = Split(binding);
        if (keyPart.Length == 1)
        {
            // Typed character: Shift/AltGr may be needed to produce it, so only Ctrl (without AltGr) must match.
            var ctrl = mods.HasFlag(KeyModifiers.Control) && !mods.HasFlag(KeyModifiers.Alt);
            // With Ctrl held Windows reports a control character, so letters also match by Key name ("Ctrl+O").
            var hit = string.Equals(symbol, keyPart, StringComparison.OrdinalIgnoreCase)
                      || (char.IsLetter(keyPart[0]) && Enum.TryParse<Key>(keyPart, true, out var letter) && letter == key);
            return hit && ctrl == wantMods.HasFlag(KeyModifiers.Control);
        }
        return Enum.TryParse<Key>(keyPart, out var k) && k == key && mods == wantMods;
    }

    /// <summary>Turns a key press into a binding string; null for bare modifier keys.</summary>
    public static string? FromKey(Key key, string? symbol, KeyModifiers mods)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin)
            return null;
        // Letters, digits and punctuation are stored as the typed character; named keys by their Key name.
        var printable = symbol is { Length: 1 } s && !char.IsWhiteSpace(s[0]) && !char.IsControl(s[0]);
        var keyPart = printable ? symbol!.ToUpperInvariant() : key.ToString();
        var prefix = (mods.HasFlag(KeyModifiers.Control) && !(printable && mods.HasFlag(KeyModifiers.Alt)) ? "Ctrl+" : "")
                     + (!printable && mods.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
                     + (!printable && mods.HasFlag(KeyModifiers.Shift) ? "Shift+" : "");
        return prefix + keyPart;
    }

    /// <summary>"Ctrl+Up" → (Control, "Up"); "Ctrl++" keeps the trailing '+' as the key.</summary>
    static (KeyModifiers, string) Split(string binding)
    {
        var mods = KeyModifiers.None;
        var rest = binding;
        while (true)
        {
            if (rest.StartsWith("Ctrl+") && rest.Length > 5) { mods |= KeyModifiers.Control; rest = rest[5..]; }
            else if (rest.StartsWith("Alt+") && rest.Length > 4) { mods |= KeyModifiers.Alt; rest = rest[4..]; }
            else if (rest.StartsWith("Shift+") && rest.Length > 6) { mods |= KeyModifiers.Shift; rest = rest[6..]; }
            else return (mods, rest);
        }
    }

    /// <summary>Readable label for the key chip ("Boşluk", "←").</summary>
    public static string Display(string binding)
    {
        if (binding.Length == 0) return "—";
        var (mods, key) = Split(binding);
        var name = key switch
        {
            "Space" => "Boşluk", "PageUp" => "PgUp", "PageDown" => "PgDn", "Left" => "←", "Right" => "→",
            "Up" => "↑", "Down" => "↓", "Escape" => "Esc", "Return" => "Enter", "OemComma" => ",", "OemPeriod" => ".",
            _ => key,
        };
        return (mods.HasFlag(KeyModifiers.Control) ? "Ctrl+" : "") + (mods.HasFlag(KeyModifiers.Alt) ? "Alt+" : "")
               + (mods.HasFlag(KeyModifiers.Shift) ? "Shift+" : "") + name;
    }
}
