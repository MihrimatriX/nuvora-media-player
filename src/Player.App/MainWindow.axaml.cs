using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Player.App;

/// <summary>Hosts only the video HWND. Controls live in <see cref="OverlayWindow"/> because nothing
/// Avalonia draws can appear above a native child window (airspace).</summary>
public partial class MainWindow : Window
{
    readonly OverlayWindow _overlay = null!;
    readonly PlayerViewModel? _vm;
    double _videoW = 1280, _videoH = 720;
    IDisposable? _shell;

    public MainWindow() => InitializeComponent();

    public MainWindow(PlayerViewModel vm, string[] args)
    {
        InitializeComponent();
        _vm = vm;
        _overlay = new OverlayWindow(vm);
        Topmost = vm.Settings.AlwaysOnTop;
        if (vm.Settings.StartFullscreen) WindowState = WindowState.FullScreen;

        vm.VideoSizeKnown += (w, h) =>
        {
            _videoW = w;
            _videoH = h;
            if (vm.Settings.PlayInFullscreen && WindowState != WindowState.FullScreen)
                WindowState = WindowState.FullScreen;
            else if (vm.Settings.FitWindowToVideo)
                FitToVideo(w, h);
        };
        vm.WindowScaleRequested += scale =>
        {
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            FitToVideo(_videoW * scale, _videoH * scale, allowUpscale: true);
        };
        // NativeControlHost recreates its HWND if the control is detached and re-attached. mpv cannot move to a
        // new "wid" at runtime, so a second handle means the video surface is gone: say so instead of
        // initializing mpv twice (which fails with "Already initialized").
        var previewStarted = false;
        PreviewVideo.HandleCreated += wid =>
        {
            if (previewStarted)
            {
                AppLog.Warn("preview video HWND was recreated; SmartSeek thumbnails stop until restart");
                return;
            }
            previewStarted = true;
            vm.StartThumbnailer(wid);
        };
        var videoStarted = false;
        Video.HandleCreated += wid =>
        {
            if (videoStarted)
            {
                AppLog.Warn("video HWND was recreated after mpv started");
                vm.ErrorText = "Video yüzeyi yeniden oluşturuldu. Oynatıcıyı yeniden başlatın.";
                return;
            }
            videoStarted = true;
            try
            {
                vm.Start(wid);
                vm.Open(args);
            }
            catch (Exception e) when (e is DllNotFoundException or InvalidOperationException)
            {
                vm.ErrorText = e is DllNotFoundException
                    ? Loc.T("error.libmpvMissing")
                    : e.Message;
            }
        };
        Opened += (_, _) =>
        {
            _overlay.Show(this); // owned: stays above us, minimizes and closes with us
            SyncOverlay();
            RoundOverlayCorners();
        };
        PositionChanged += (_, _) => SyncOverlay();
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) _shell = ShellIntegration.TryAttach(this, vm);
        Closing += (_, _) =>
        {
            _shell?.Dispose();
            vm.Shutdown(); // before our HWND dies, mpv may still be rendering into it
        };
        AddHandler(KeyDownEvent, (_, e) => _overlay.HandleKey(e), RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => _overlay.HandleKeyUp(e), RoutingStrategies.Tunnel);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ClientSizeProperty || change.Property == WindowStateProperty) SyncOverlay();
        if (change.Property == WindowStateProperty && _vm is not null)
        {
            RoundOverlayCorners();
            // The bar's full-screen and maximize buttons show what pressing them will do.
            _vm.IsFullScreen = WindowState == WindowState.FullScreen;
            _vm.IsMaximized = WindowState == WindowState.Maximized;
        }
        // A topmost owner climbs above a non-topmost owned window: the controls would hide behind the video.
        if (change.Property == TopmostProperty && _overlay is not null) _overlay.Topmost = Topmost;
    }

    /// <summary>Resize to the video's aspect ratio (no black bars), at most 85 % of the screen, and re-center.</summary>
    public void FitToVideo(double videoWidth, double videoHeight, bool allowUpscale = false)
    {
        if (WindowState != WindowState.Normal || Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea;
        var dpi = screen.Scaling;
        var maxHeight = area.Height * 0.85 / dpi;
        var maxScale = Math.Min(area.Width * 0.85 / dpi / videoWidth, maxHeight / videoHeight);
        var scale = allowUpscale ? Math.Min(1.5, maxScale) : Math.Min(1, maxScale);
        var width = Math.Max(MinWidth, videoWidth * scale);
        // MinWidth widens portrait videos; capping the height keeps them on screen (with side bars).
        var height = Math.Clamp(width * videoHeight / videoWidth, MinHeight, Math.Max(MinHeight, maxHeight));
        Width = width;
        Height = height;
        Position = new PixelPoint(area.X + (int)((area.Width - width * dpi) / 2), area.Y + (int)((area.Height - height * dpi) / 2));
    }

    void SyncOverlay()
    {
        if (_overlay is null) return; // OnPropertyChanged runs during InitializeComponent
        // Maximized borderless windows overhang the screen by the frame width; keep the overlay on the visible part.
        var margin = OffScreenMargin;
        _overlay.Position = this.PointToScreen(new Point(margin.Left, margin.Top));
        _overlay.Width = Math.Max(1, ClientSize.Width - margin.Left - margin.Right);
        _overlay.Height = Math.Max(1, ClientSize.Height - margin.Top - margin.Bottom);
    }

    /// <summary>Windows 11 rounds our corners but not the borderless overlay's, so the header and bar shades it draws
    /// squared them off whenever the controls showed. Maximized and full screen stay square, like the main window.</summary>
    void RoundOverlayCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || _overlay?.TryGetPlatformHandle() is not { Handle: var hwnd }) return;
        var preference = WindowState == WindowState.Normal ? 2 : 1; // DWMWCP_ROUND : DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
    }

    [System.Runtime.InteropServices.LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
