using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Microsoft.Extensions.DependencyInjection;
using Player.Core;

namespace Player.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Accessibility.Register();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection()
                .AddSingleton(AppSettings.Load())
                .AddSingleton<IPlayerService>(_ =>
                {
                    // mpv events reach the view model on the UI thread, whichever thread resolves the service.
                    var player = new MpvPlayer(a => Dispatcher.UIThread.Post(a));
                    player.Log += AppLog.Warn;
                    return player;
                })
                .AddSingleton<PlayerViewModel>()
                .BuildServiceProvider();

            var settings = services.GetRequiredService<AppSettings>();
            var vm = services.GetRequiredService<PlayerViewModel>();
            SetAccent(settings.Accent);
            var window = new MainWindow(vm, desktop.Args ?? []);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => services.Dispose();

            if (Program.Instance is { } instance)
            {
                instance.ArgumentsReceived += args => Dispatcher.UIThread.Post(() =>
                {
                    vm.Open(args);
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                });
                instance.StartListening();
            }
            _ = Updater.CheckAsync(settings);
        }
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Re-tints every accent token. Brushes are looked up dynamically, so open windows update live.</summary>
    public static void SetAccent(string hex)
    {
        if (!Color.TryParse(hex, out var c)) return;
        var r = Current!.Resources;
        r["Accent"] = new SolidColorBrush(c);
        r["AccentTint"] = new SolidColorBrush(c, 0.18);
        r["AccentChip"] = new SolidColorBrush(c, 0.24);
        r["AccentSoft"] = new SolidColorBrush(c, 0.28);
        r["AccentPress"] = new SolidColorBrush(c, 0.30);
        r["AccentHalo"] = new SolidColorBrush(c, 0.35);
        r["SystemAccentColor"] = c; // Fluent bits we did not restyle (focus rings, menus)
        // Accent as text or focus ring must still reach WCAG AA on the dark surfaces; text on an accent fill picks white or ink.
        var text = ColorContrast.Lighten(c, ColorContrast.Surface);
        r["AccentText"] = new SolidColorBrush(text);
        r["FocusRing"] = new SolidColorBrush(text);
        r["OnAccent"] = new SolidColorBrush(ColorContrast.ReadableOn(c));
    }
}

public static class AppBuilderExtensions
{
    /// <summary>Registers the embedded Manrope / IBM Plex Mono collection and makes Manrope the default.
    /// Shared by the app and the headless UI tests.</summary>
    public static AppBuilder UseNuvoraFonts(this AppBuilder builder) => builder
        .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:Nuvora", UriKind.Absolute), new Uri("avares://NuvoraPlayer/Assets/Fonts", UriKind.Absolute))))
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Nuvora#Manrope" });
}
