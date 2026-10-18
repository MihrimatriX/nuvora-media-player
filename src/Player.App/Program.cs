using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Player.Core;
using Velopack;

namespace Player.App;

static partial class Program
{
    const string InstanceId = "NuvoraPlayer";

    /// <summary>Set when this process is the running player; later launches forward their files to it.</summary>
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    static void Main(string[] args)
    {
        // Must run first: Velopack's install/update/uninstall hooks start the exe and exit here.
        var velopack = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
            velopack.OnBeforeUninstallFastCallback(_ => { if (OperatingSystem.IsWindows()) RemoveFileAssociation(); });
        velopack.Run();

        using var instance = SingleInstance.TryAcquire(InstanceId);
        if (instance is null)
        {
            // Our process got the user's click, so it may hand the foreground to the running one.
            if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(AsfwAny);
            if (SingleInstance.TrySend(InstanceId, [.. args.Select(Absolute)], TimeSpan.FromSeconds(3))) return;
            // Running instance did not answer (hung, shutting down): open in a new window rather than lose the files.
        }
        Instance = instance;

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

    /// <summary>The running instance has a different working directory, so relative paths are resolved here.</summary>
    static string Absolute(string arg) => File.Exists(arg) || Directory.Exists(arg) ? Path.GetFullPath(arg) : arg;

    /// <summary>Uninstall leaves no "Open with Nuvora" entries pointing at a deleted exe.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static void RemoveFileAssociation()
    {
        if (FileAssociation.IsRegistered) FileAssociation.Unregister();
    }

    const int AsfwAny = -1;

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseNuvoraFonts()
        .LogToTrace();
}
