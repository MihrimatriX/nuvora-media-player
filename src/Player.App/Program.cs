using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;

namespace Player.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Crash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Crash(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };
        BuildAvaloniaApp()
            .AfterSetup(_ => Dispatcher.UIThread.UnhandledException += OnUiException)
            .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }

    /// <summary>Exceptions from UI handlers (including async void ones such as the file pickers).
    /// File-system failures leave no broken state behind, so the player keeps running; anything else
    /// is logged and still brings the app down rather than continuing in an unknown state.</summary>
    static void OnUiException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Crash(e.Exception, "Dispatcher.UnhandledException");
        e.Handled = e.Exception is IOException or UnauthorizedAccessException;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .ConfigureFonts(fonts => fonts.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:Nuvora", UriKind.Absolute), new Uri("avares://NuvoraPlayer/Assets/Fonts", UriKind.Absolute))))
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Nuvora#Manrope" })
        .LogToTrace();
}
