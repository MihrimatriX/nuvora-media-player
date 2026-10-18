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
    double _videoW = 1280, _videoH = 720;
    IDisposable? _shell;

    public MainWindow() => InitializeComponent();

    public MainWindow(PlayerViewModel vm, string[] args)
    {
        InitializeComponent();
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
        PreviewVideo.HandleCreated += wid => vm.StartThumbnailer(wid);
        Video.HandleCreated += wid =>
        {
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
    }

    /// <summary>Resize to the video's aspect ratio (no black bars), at most 85 % of the screen, and re-center.</summary>
    public void FitToVideo(double videoWidth, double videoHeight, bool allowUpscale = false)
    {
        if (WindowState != WindowState.Normal || Screens.ScreenFromWindow(this) is not { } screen) return;
        var area = screen.WorkingArea;
        var dpi = screen.Scaling;
        var maxScale = Math.Min(area.Width * 0.85 / dpi / videoWidth, area.Height * 0.85 / dpi / videoHeight);
        var scale = allowUpscale ? Math.Min(1.5, maxScale) : Math.Min(1, maxScale);
        var width = Math.Max(MinWidth, videoWidth * scale);
        var height = Math.Max(MinHeight, width * videoHeight / videoWidth);
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
}
