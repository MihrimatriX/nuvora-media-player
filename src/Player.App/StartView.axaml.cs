using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

namespace Player.App;

public partial class StartView : UserControl
{
    public static readonly IValueConverter PillText =
        new FuncValueConverter<bool, string>(hasHero => Loc.T(hasHero ? "start.pillResume" : "start.pillWelcome"));

    public StartView() => InitializeComponent();

    OverlayWindow Overlay => (OverlayWindow)TopLevel.GetTopLevel(this)!;

    void OnOpenFile(object? sender, RoutedEventArgs e) => Overlay.OpenFiles();
    void OnOpenFolder(object? sender, RoutedEventArgs e) => Overlay.OpenFolder();
    void OnAllRecents(object? sender, RoutedEventArgs e) => Overlay.ShowRecentsMenu((Control)sender!);
}
