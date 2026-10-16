using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;

namespace Player.App;

static class Program
{
    [STAThread]
    static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:Nuvora", UriKind.Absolute), new Uri("avares://NuvoraPlayer/Assets/Fonts", UriKind.Absolute))))
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Nuvora#Manrope" })
        .LogToTrace();
}
