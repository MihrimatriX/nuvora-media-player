using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Player.App;

public partial class SubtitleSearchSheet : UserControl
{
    public static readonly IValueConverter IsTur =
        new FuncValueConverter<string, bool>(l => string.Equals(l, "tur", StringComparison.OrdinalIgnoreCase));

    public static readonly IValueConverter IsEng =
        new FuncValueConverter<string, bool>(l => string.Equals(l, "eng", StringComparison.OrdinalIgnoreCase));

    public static readonly IValueConverter IsAll =
        new FuncValueConverter<string, bool>(l => string.Equals(l, "all", StringComparison.OrdinalIgnoreCase));

    public SubtitleSearchSheet() => InitializeComponent();

    PlayerViewModel? Vm => DataContext as PlayerViewModel;

    void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is { } vm)
        {
            _ = vm.SearchSubtitlesAsync();
            e.Handled = true;
        }
    }

    void OnSetLangTur(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) { vm.SubtitleLang = "tur"; _ = vm.SearchSubtitlesAsync(); }
    }

    void OnSetLangEng(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) { vm.SubtitleLang = "eng"; _ = vm.SearchSubtitlesAsync(); }
    }

    void OnSetLangAll(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) { vm.SubtitleLang = "all"; _ = vm.SearchSubtitlesAsync(); }
    }
}

