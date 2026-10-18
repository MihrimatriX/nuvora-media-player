using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
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
            // Built on the UI thread, so MpvPlayer captures the UI SynchronizationContext.
            var services = new ServiceCollection()
                .AddSingleton(AppSettings.Load())
                .AddSingleton<IPlayerService, MpvPlayer>()
                .AddSingleton<PlayerViewModel>()
                .BuildServiceProvider();

            SetAccent(services.GetRequiredService<AppSettings>().Accent);
            desktop.MainWindow = new MainWindow(services.GetRequiredService<PlayerViewModel>(), desktop.Args ?? []);
            desktop.Exit += (_, _) => services.Dispose();
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
