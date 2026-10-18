using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Player.App;

/// <summary>UI strings (Strings/tr.json, Strings/en.json). XAML binds through the indexer with {app:T key},
/// so switching language updates every open view; C# uses <see cref="T"/> / <see cref="F"/> and listens to
/// <see cref="LanguageChanged"/> for computed text.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Languages = ["tr", "en"];
    // Order matters: the tables must exist before Instance's constructor reads them.
    static readonly Dictionary<string, Dictionary<string, string>> Tables = Languages.ToDictionary(l => l, Load);
    public static Loc Instance { get; } = new();
    Dictionary<string, string> _current = Tables["tr"];

    public string Language { get; private set; } = "tr";

    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? LanguageChanged;

    public string this[string key] => Lookup(key);

    /// <summary>"auto" follows the Windows display language: Turkish for tr-*, English otherwise.</summary>
    public static string Resolve(string setting) =>
        Languages.Contains(setting) ? setting
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr" ? "tr" : "en";

    public static void SetLanguage(string setting)
    {
        var lang = Resolve(setting);
        if (lang == Instance.Language) return;
        Instance.Language = lang;
        Instance._current = Tables[lang];
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item")); // Avalonia indexer bindings listen for "Item"
        LanguageChanged?.Invoke();
    }

    public static string T(string key) => Instance.Lookup(key);
    public static string F(string key, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(key), args);
    public static bool Has(string lang, string key) => Tables[lang].ContainsKey(key);
    public static IReadOnlyCollection<string> Keys(string lang) => Tables[lang].Keys;

    // Missing in the current language → Turkish (the source language) → the key itself, so a gap is visible but harmless.
    string Lookup(string key) =>
        _current.TryGetValue(key, out var s) || Tables["tr"].TryGetValue(key, out s) ? s : key;

    static Dictionary<string, string> Load(string lang)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Player.App.Strings.{lang}.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}

/// <summary>{app:T key}: a live binding to the localized string.</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
}
