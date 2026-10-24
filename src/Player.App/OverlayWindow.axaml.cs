using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Player.Enhancement;

namespace Player.App;

/// <summary>Transparent, owned window drawn over the video HWND. All input and all UI live here.</summary>
public partial class OverlayWindow : Window
{
    const double ResizeGrip = 7, CornerGrip = 14;
    static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    // Created once: PointerMoved runs for every mouse move and a Cursor wraps a native handle.
    static readonly Cursor SizeWestEast = new(StandardCursorType.SizeWestEast), SizeNorthSouth = new(StandardCursorType.SizeNorthSouth),
        TopLeftCorner = new(StandardCursorType.TopLeftCorner), TopRightCorner = new(StandardCursorType.TopRightCorner);
    static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#E60E1016"));

    readonly PlayerViewModel _vm = null!;
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly DispatcherTimer _osdTimer = new() { Interval = TimeSpan.FromSeconds(1.8) };
    readonly WheelSteps _videoWheel = new(), _volumeWheel = new();
    int _lastHoverBucket = -1;
    Point _lastPointer;
    bool _menuOpen;
    bool _chromeVisible = true;
    bool _isDraggingSplit;
    Key? _bypassKey;
    PointerPressedEventArgs? _dragStart;

    public OverlayWindow() => InitializeComponent();

    public OverlayWindow(PlayerViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        _hideTimer.Tick += (_, _) =>
        {
            if (!CanAutoHide()) return; // keep ticking, re-check in 2 s
            _hideTimer.Stop();
            SetChromeVisible(false);
        };
        PointerMoved += (_, e) =>
        {
            var p = e.GetPosition(this);
            UpdateEdgeCursor(p);
            if (Math.Abs(p.X - _lastPointer.X) + Math.Abs(p.Y - _lastPointer.Y) < 3) return; // ignore jitter
            _lastPointer = p;
            Wake();
        };
        // Window edge/corner resizing & right-click context menu: runs in Tunnel before child controls.
        AddHandler(PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel);
        vm.PropertyChanged += OnVmChanged;
        vm.ThumbnailReady += (bucket, bmp) => Dispatcher.UIThread.Post(() =>
        {
            if (bucket == _lastHoverBucket && HoverBubble.IsVisible)
            {
                HoverThumb.Source = bmp;
                HoverThumb.IsVisible = true;
            }
        });
        vm.OsdRequested += msg => Dispatcher.UIThread.Post(() => ShowOsd(msg));

        Root.SizeChanged += (_, _) => UpdateSplitVisual();
        SplitTrack.PointerPressed += OnSplitPressed;
        SplitTrack.PointerMoved += OnSplitMoved;
        SplitTrack.PointerReleased += OnSplitReleased;
        SplitTrack.PointerCaptureLost += (_, _) => _isDraggingSplit = false;
        SplitTrack.DoubleTapped += (_, e) => { SetSplitAt(Bounds.Width * 0.5); e.Handled = true; };

        HitArea.DoubleTapped += (_, _) => ToggleFullScreen();
        // Drag the window from anywhere on the video.
        HitArea.PointerPressed += (_, e) =>
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (e.ClickCount == 1 && props.IsLeftButtonPressed) _dragStart = e;
        };
        HitArea.PointerMoved += (_, e) =>
        {
            if (_dragStart is null) return;
            var d = e.GetPosition(this) - _dragStart.GetPosition(this);
            if (Math.Abs(d.X) + Math.Abs(d.Y) < 5) return;
            var start = _dragStart;
            _dragStart = null;
            if (Main is { WindowState: WindowState.Normal } main) main.BeginMoveDrag(start);
        };
        HitArea.PointerReleased += (_, _) => _dragStart = null;
        HitArea.PointerWheelChanged += (_, e) =>
        {
            var steps = _videoWheel.Take(e.Delta.Y);
            for (var i = 0; i < Math.Abs(steps); i++) _vm.HandleWheel(Math.Sign(steps));
        };
        // The wheel over the speaker or the volume slider always means volume, whatever the video area does.
        VolumeSlider.PointerWheelChanged += OnVolumeWheel;
        MuteButton.PointerWheelChanged += OnVolumeWheel;
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdPill.IsVisible = false;
        };
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, (_, e) => HandleKey(e), RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => HandleKeyUp(e), RoutingStrategies.Tunnel);

        // While the seek bar is held, the view drives the position instead of mpv.
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => _vm.IsScrubbing = true, RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => _vm.IsScrubbing = false, RoutingStrategies.Tunnel, true);
        // A press that ends without a release on the bar (Alt+Tab, a popup taking the mouse) must not leave the bar
        // frozen: while scrubbing, mpv's position updates are ignored.
        Deactivated += (_, _) => _vm.IsScrubbing = false;
        AddHandler(PointerReleasedEvent, (_, _) => _vm.IsScrubbing = false, RoutingStrategies.Tunnel, true);
        SeekHost.SizeChanged += (_, _) => UpdateSeekDecor();
        SeekHost.PointerMoved += (s, e) =>
        {
            if (_vm.IsScrubbing && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) _vm.IsScrubbing = false;
            OnSeekHover(s, e);
        };
        SeekHost.PointerExited += (_, _) => HoverLine.IsVisible = HoverBubble.IsVisible = HoverTrack.IsVisible = false;
        UpdateStepLabels();

        Closing += (_, e) =>
        {
            // Alt+F4 on the overlay should close the player, not just strip its controls.
            if (e.CloseReason == WindowCloseReason.WindowClosing && Owner is Window owner)
            {
                e.Cancel = true;
                owner.Close();
            }
        };
    }

    Window? Main => Owner as Window;

    void OnVolumeWheel(object? sender, PointerWheelEventArgs e)
    {
        var steps = _volumeWheel.Take(e.Delta.Y);
        for (var i = 0; i < Math.Abs(steps); i++) _vm.StepVolume(Math.Sign(steps));
        e.Handled = true;
    }

    // ---------------- interactive before/after split slider ----------------

    void OnSplitPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (!props.IsLeftButtonPressed) return;
        _isDraggingSplit = true;
        e.Pointer.Capture(SplitTrack);
        SetSplitAt(e.GetPosition(Root).X);
        e.Handled = true;
    }

    void OnSplitMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingSplit) return;
        SetSplitAt(e.GetPosition(Root).X);
        e.Handled = true;
    }

    void OnSplitReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingSplit) return;
        _isDraggingSplit = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    void SetSplitAt(double x)
    {
        var w = Root.Bounds.Width;
        var h = Root.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var clampedX = Math.Clamp(x, w * 0.04, w * 0.96);
        var winRatio = clampedX / w;
        var vidRatio = _vm.ComputeVideoSplit(clampedX, w, h);
        _vm.SetSplitPosition(winRatio, vidRatio);
        UpdateSplitVisual();
    }

    void UpdateSplitVisual()
    {
        if (!_vm.IsSplitCompare) return;
        var w = Root.Bounds.Width;
        var h = Root.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var splitX = Math.Clamp(_vm.SplitRatio * w, w * 0.04, w * 0.96);
        var vidRatio = _vm.ComputeVideoSplit(splitX, w, h);
        _vm.SetSplitPosition(_vm.SplitRatio, vidRatio);

        SplitTrack.Height = h;
        Canvas.SetLeft(SplitTrack, Math.Round(splitX - SplitTrack.Width / 2));
        Canvas.SetTop(SplitTrack, 0);

        SplitLeftBadge.Measure(Size.Infinity);
        var leftW = SplitLeftBadge.DesiredSize.Width;
        SplitLeftBadge.IsVisible = splitX >= leftW + 24;
        Canvas.SetLeft(SplitLeftBadge, Math.Round(Math.Max(16, splitX - leftW - 14)));

        SplitRightBadge.Measure(Size.Infinity);
        var rightW = SplitRightBadge.DesiredSize.Width;
        SplitRightBadge.IsVisible = w - splitX >= rightW + 24;
        Canvas.SetLeft(SplitRightBadge, Math.Round(Math.Min(w - rightW - 16, splitX + 14)));
    }

    // ---------------- resize from all 8 edges & corners ----------------

    WindowEdge? GetResizeEdge(Point p)
    {
        if (Main is not { WindowState: WindowState.Normal }) return null;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return null;
        var left = p.X < ResizeGrip;
        var right = p.X >= w - ResizeGrip;
        var top = p.Y < ResizeGrip;
        var bottom = p.Y >= h - ResizeGrip;
        if (!left && !right && !top && !bottom) return null;

        var cLeft = p.X < CornerGrip;
        var cRight = p.X >= w - CornerGrip;
        var cTop = p.Y < CornerGrip;
        var cBottom = p.Y >= h - CornerGrip;
        if (cTop && cLeft) return WindowEdge.NorthWest;
        if (cTop && cRight) return WindowEdge.NorthEast;
        if (cBottom && cLeft) return WindowEdge.SouthWest;
        if (cBottom && cRight) return WindowEdge.SouthEast;
        if (left) return WindowEdge.West;
        if (right) return WindowEdge.East;
        if (top) return WindowEdge.North;
        return WindowEdge.South;
    }

    void UpdateEdgeCursor(Point p)
    {
        Root.Cursor = _isDraggingSplit ? SizeWestEast : GetResizeEdge(p) switch
        {
            null => _chromeVisible ? Cursor.Default : HiddenCursor,
            WindowEdge.West or WindowEdge.East => SizeWestEast,
            WindowEdge.North or WindowEdge.South => SizeNorthSouth,
            WindowEdge.NorthWest or WindowEdge.SouthEast => TopLeftCorner,
            _ => TopRightCorner,
        };
    }

    void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        // Right-click anywhere (except inside a TextBox or while Settings modal is open) opens the context menu at the cursor.
        if (props.IsRightButtonPressed && !_vm.IsSettingsOpen && e.Source is not TextBox)
        {
            ShowContextMenu(Root);
            e.Handled = true;
            return;
        }
        // Left-clicking the window while a flyout menu is open cleanly dismisses it without starting a drag/resize.
        if (Menus.IsOpen && props.IsLeftButtonPressed)
        {
            Menus.HideCurrent();
            e.Handled = true;
            return;
        }
        if (props.IsLeftButtonPressed && GetResizeEdge(e.GetPosition(this)) is { } edge && Main is { } main)
        {
            _dragStart = null;
            main.BeginResizeDrag(edge, e);
            e.Handled = true;
        }
    }

    /// <summary>Hide only while actually watching: playing, untouched, nothing open, no keyboard navigation.</summary>
    bool CanAutoHide() =>
        !_vm.IsPaused && !_vm.IsIdle && !_vm.IsScrubbing && !_vm.IsSheetOpen && !_isDraggingSplit
        && !Bar.IsPointerOver && !Header.IsPointerOver && !SplitTrack.IsPointerOver
        && !_menuOpen
        // A keyboard-focused control must stay visible (":focus-visible" is set for Tab focus, not mouse clicks).
        && !(FocusManager?.GetFocusedElement() is Control { Classes: var c } && c.Contains(":focus-visible"));

    void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.IsPaused) or nameof(PlayerViewModel.IsIdle) or nameof(PlayerViewModel.IsSheetOpen):
                Wake();
                break;
            case nameof(PlayerViewModel.IsSplitCompare) or nameof(PlayerViewModel.SplitRatio) or nameof(PlayerViewModel.AfterBadgeText)
                or nameof(PlayerViewModel.AspectRatio) or nameof(PlayerViewModel.Rotation) or nameof(PlayerViewModel.IsFilled):
                Dispatcher.UIThread.Post(UpdateSplitVisual);
                break;
            case nameof(PlayerViewModel.CacheTime):
                UpdateBuffer();
                break;
            case nameof(PlayerViewModel.Duration) or nameof(PlayerViewModel.Chapters)
                or nameof(PlayerViewModel.AbLoopASeconds) or nameof(PlayerViewModel.AbLoopBSeconds):
                UpdateSeekDecor();
                break;
            case nameof(PlayerViewModel.IsSettingsOpen) when _vm.IsSettingsOpen:
                Settings.Reload();
                break;
            case nameof(PlayerViewModel.Settings):
                UpdateStepLabels();
                if (Main is { } main) main.Topmost = _vm.Settings.AlwaysOnTop;
                break;
        }
    }

    // ---------------- keyboard ----------------

    /// <summary>Global shortcuts from Settings.Shortcuts. Tunnel handler, so a focused button never also eats Space.
    /// While the settings sheet is open only Esc is global; the sheet records keys for the shortcut editor.</summary>
    public void HandleKey(KeyEventArgs e)
    {
        if (e.Handled || e.Source is TextBox) return;
        if (Menus.IsOpen && e.Key == Key.Escape)
        {
            Menus.HideCurrent();
            e.Handled = true;
            return;
        }
        if (_vm.IsSettingsOpen)
        {
            if (Settings.HandleKey(e)) return;
            if (e.Key == Key.Escape) { Settings.Cancel(); e.Handled = true; }
            return;
        }
        if (_vm.IsSubtitleSearchOpen)
        {
            if (e.Key == Key.Escape) { _vm.IsSubtitleSearchOpen = false; e.Handled = true; }
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (_vm.IsPanelOpen) _vm.IsPanelOpen = false;
            else if (_vm.IsPlaylistOpen) _vm.IsPlaylistOpen = false;
            else if (_vm.IsSplitCompare) _vm.IsSplitCompare = false;
            else if (Main is { WindowState: WindowState.FullScreen }) ToggleFullScreen();
            else return;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Shift) != 0 && !_vm.IsIdle)
        {
            _vm.OpenSubtitleSearchCommand.Execute(null);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.U && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            OpenUrl();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; return; } // fixed second key for full screen
        // List navigation keys belong to the focused playlist (Up/Down would otherwise change the volume).
        if (_vm.IsPlaylistOpen && e.Source is Visual v && v.FindAncestorOfType<ListBox>(includeSelf: true) is not null
            && e.Key is Key.Up or Key.Down or Key.Enter or Key.Delete or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            return;

        var action = Shortcuts.Find(_vm.Settings.Shortcuts, e.Key, e.KeySymbol, e.KeyModifiers);
        if (action is null) return;
        e.Handled = true;
        switch (action)
        {
            case "pause": _vm.TogglePauseCommand.Execute(null); break;
            case "seek-back": _vm.SeekBackCommand.Execute(null); break;
            case "seek-forward": _vm.SeekForwardCommand.Execute(null); break;
            case "volume-up": _vm.StepVolume(1); break;
            case "volume-down": _vm.StepVolume(-1); break;
            case "mute": _vm.ToggleMuteCommand.Execute(null); break;
            case "fullscreen": ToggleFullScreen(); break;
            case "frame-back": _vm.FrameBackStepCommand.Execute(null); break;
            case "frame-step": _vm.FrameStepCommand.Execute(null); break;
            case "prev": _vm.PreviousCommand.Execute(null); break;
            case "next": _vm.NextCommand.Execute(null); break;
            case "speed-down": _vm.SpeedDownCommand.Execute(null); break;
            case "speed-up": _vm.SpeedUpCommand.Execute(null); break;
            case "fps-cycle": _vm.CycleFps(); break;
            case "look-cycle": _vm.CycleLook(); break;
            case "loop": _vm.CycleLoop(); break;
            case "ab-loop": _vm.AbLoop(); break;
            case "aspect": _vm.CycleAspect(); break;
            case "fill": _vm.ToggleFillCommand.Execute(null); break;
            case "rotate": _vm.Rotate(); break;
            case "ontop": _vm.ToggleAlwaysOnTop(); break;
            case "screenshot": _vm.ScreenshotCommand.Execute(null); break;
            case "stats": _vm.ToggleStats(); break;
            case "panel": _vm.TogglePanelCommand.Execute(null); break;
            case "playlist": _vm.TogglePlaylist(); break;
            case "split-compare": _vm.ToggleSplitCompareCommand.Execute(null); break;
            case "open": OpenFiles(); break;
            case "settings": _vm.OpenSettingsCommand.Execute(null); break;
            case "compare":
                // Hold to see the untouched image; released in HandleKeyUp.
                if (_bypassKey is null) { _bypassKey = e.Key; _vm.IsBypassed = true; }
                break;
        }
        Wake();
    }

    public void HandleKeyUp(KeyEventArgs e)
    {
        // Match on the physical key: modifiers (AltGr for '\') may be released first.
        if (_bypassKey != e.Key) return;
        _bypassKey = null;
        _vm.IsBypassed = false;
    }

    // ---------------- chrome visibility ----------------

    void Wake()
    {
        SetChromeVisible(true);
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    void SetChromeVisible(bool visible)
    {
        _chromeVisible = visible;
        Chrome.Classes.Set("hidden", !visible);
        var hideHeader = (!visible && !_vm.IsIdle) || (_vm.Settings.HideTopBar && !_vm.IsIdle && _lastPointer.Y > 44);
        Header.Classes.Set("hidden", hideHeader);
        if (GetResizeEdge(_lastPointer) is null)
            Root.Cursor = visible ? Cursor.Default : HiddenCursor;
        if (!_vm.IsIdle && Root.Bounds.Height > 0)
        {
            // Text subtitles sit just above the bar while it shows (mpv measures in 720p units).
            var lift = visible && Bar.IsVisible ? (Root.Bounds.Height - Bar.Bounds.Top + 12) / Root.Bounds.Height * 720 : 34;
            _vm.SetSubtitleMargin(lift);
        }
    }

    // ---------------- seek bar decorations ----------------

    /// <summary>Cache time changes several times a second while buffering; only a whole-pixel change re-lays out the bar.</summary>
    void UpdateBuffer()
    {
        var duration = _vm.Duration;
        var width = duration > 0 ? Math.Round(Math.Clamp(_vm.CacheTime / duration, 0, 1) * SeekHost.Bounds.Width) : 0;
        if (BufferBar.Width != width) BufferBar.Width = width;
    }

    /// <summary>Chapter ticks and the A-B bar: rebuilt only when the file, its chapters, the loop or the bar's size change.</summary>
    void UpdateSeekDecor()
    {
        var width = SeekHost.Bounds.Width;
        var duration = _vm.Duration;
        UpdateBuffer();
        Ticks.Children.Clear();

        if (_vm.AbLoopASeconds is { } loopA && duration > 0)
        {
            var loopB = _vm.AbLoopBSeconds ?? duration;
            var startX = Math.Clamp(loopA / duration, 0, 1) * width;
            var endX = Math.Clamp(loopB / duration, 0, 1) * width;
            AbLoopBar.Margin = new Thickness(startX, 0, 0, 0);
            AbLoopBar.Width = Math.Max(2, endX - startX);
            AbLoopBar.IsVisible = true;
        }
        else
        {
            AbLoopBar.IsVisible = false;
        }

        if (duration <= 0) return;
        foreach (var chapter in _vm.Chapters.Where(c => c.Time > 0))
        {
            var tick = new Rectangle { Width = 3, Height = 6, Fill = TickBrush };
            Canvas.SetLeft(tick, chapter.Time / duration * width);
            Canvas.SetTop(tick, 9);
            Ticks.Children.Add(tick);
        }
    }

    void OnSeekHover(object? sender, PointerEventArgs e)
    {
        var width = SeekHost.Bounds.Width;
        if (!_vm.Settings.SmartSeek || _vm.Duration <= 0 || width <= 0)
        {
            HoverLine.IsVisible = HoverBubble.IsVisible = HoverTrack.IsVisible = false;
            return;
        }
        var x = Math.Clamp(e.GetPosition(SeekHost).X, 0, width);
        var time = x / width * _vm.Duration;
        var chapter = _vm.ChapterLabel(_vm.ChapterAt(time));

        var bmp = _vm.GetOrRequestThumbnail(time, out var bucket);
        _lastHoverBucket = bucket;
        HoverThumb.Source = bmp;
        HoverThumb.IsVisible = bmp != null;

        HoverText.Text = PlayerViewModel.Format(time);
        var delta = time - _vm.Position;
        HoverDelta.Text = delta >= 0 ? $"+{PlayerViewModel.Format(delta)}" : $"-{PlayerViewModel.Format(-delta)}";
        HoverChapter.Text = chapter;
        HoverChapter.IsVisible = !string.IsNullOrEmpty(chapter);

        HoverTrack.Width = x;
        HoverTrack.IsVisible = true;
        HoverLine.Margin = new Thickness(x - 1, 0, 0, 0);
        HoverLine.IsVisible = HoverBubble.IsVisible = true;

        HoverBubble.Measure(Size.Infinity);
        var bubbleW = HoverBubble.DesiredSize.Width;
        Canvas.SetLeft(HoverBubble, Math.Clamp(x - bubbleW / 2, 4, Math.Max(4, width - bubbleW - 4)));
    }

    void UpdateStepLabels() => BackStepText.Text = FwdStepText.Text = _vm.Settings.SeekStepSeconds.ToString(CultureInfo.InvariantCulture);

    void ShowOsd(string text)
    {
        OsdText.Text = text;
        OsdPill.IsVisible = true;
        OsdPill.Opacity = 1;
        _osdTimer.Stop(); // restart: a held key keeps the pill up
        _osdTimer.Start();
    }

    // ---------------- window ----------------

    void ToggleFullScreen()
    {
        if (Main is { } w)
            w.WindowState = w.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
    }

    void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (GetResizeEdge(e.GetPosition(this)) is not null) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && Main is { WindowState: not WindowState.FullScreen } main)
            main.BeginMoveDrag(e);
    }

    void OnHeaderDoubleTapped(object? sender, TappedEventArgs e) => OnMaximize(sender, e);
    void OnMinimize(object? sender, RoutedEventArgs e) { if (Main is { } m) m.WindowState = WindowState.Minimized; }
    void OnClose(object? sender, RoutedEventArgs e) => Main?.Close();
    void OnFullScreen(object? sender, RoutedEventArgs e) => ToggleFullScreen();

    void OnMaximize(object? sender, RoutedEventArgs e)
    {
        if (Main is { } m) m.WindowState = m.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    // ---------------- files ----------------

    void OnOpenFile(object? sender, RoutedEventArgs e) => OpenFiles();

    public async void OpenFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = Loc.T("dialog.openMedia"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new(Loc.T("dialog.allMedia")) { Patterns = FileAssociation.Extensions.Select(x => "*" + x).ToList() },
                new(Loc.T("dialog.videoFiles")) { Patterns = FileAssociation.VideoExtensions.Select(x => "*" + x).ToList() },
                new(Loc.T("dialog.audioFiles")) { Patterns = FileAssociation.AudioExtensions.Select(x => "*" + x).ToList() },
                new(Loc.T("dialog.playlists")) { Patterns = FileAssociation.PlaylistExtensions.Select(x => "*" + x).ToList() },
                new(Loc.T("dialog.allFiles")) { Patterns = ["*.*"] },
            ],
        });
        _vm.Open(LocalPaths(files));
    }

    /// <summary>Adds files to the end of the playlist without interrupting the current one.</summary>
    public async void AppendFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = Loc.T("playlist.addDialog"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new(Loc.T("dialog.allMedia")) { Patterns = FileAssociation.Extensions.Select(x => "*" + x).ToList() },
                new(Loc.T("dialog.allFiles")) { Patterns = ["*.*"] },
            ],
        });
        _vm.AppendToPlaylist(LocalPaths(files));
    }

    public async void OpenFolder()
    {
        // mpv expands a directory into a playlist itself.
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = Loc.T("dialog.openFolder") });
        _vm.Open(LocalPaths(folders));
    }

    public async void OpenUrl()
    {
        var top = TopLevel.GetTopLevel(this);
#pragma warning disable CS0618
        var clip = top?.Clipboard is { } c ? await c.GetTextAsync() : null;
#pragma warning restore CS0618
        var initial = clip is { } s && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            ? s.Trim() : "";
        if (!string.IsNullOrWhiteSpace(initial))
        {
            _vm.Open([initial]);
            _vm.ShowOsd("Ağ akışı başlatılıyor…");
        }
        else
        {
            _vm.ShowOsd("Panoda geçerli bir URL bağlantısı bulunamadı");
        }
    }

    async void AddSubtitle()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = Loc.T("dialog.loadSubtitle"),
            FileTypeFilter = [new(Loc.T("menu.subtitles")) { Patterns = FileAssociation.SubtitleExtensions.Select(x => "*" + x).ToList() }],
        });
        foreach (var path in LocalPaths(files)) _vm.AddSubtitle(path);
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = LocalPaths(e.DataTransfer.TryGetFiles() ?? []);
        if (paths.Count == 1 && !_vm.IsIdle && FileAssociation.SubtitleExtensions.Contains(System.IO.Path.GetExtension(paths[0]).ToLowerInvariant()))
            _vm.AddSubtitle(paths[0]);
        else
            _vm.Open(paths);
    }

    static List<string> LocalPaths(IEnumerable<IStorageItem> items) =>
        items.Select(i => i.TryGetLocalPath()).OfType<string>().ToList();

    // ---------------- menus ----------------

    void ShowMenu(Control anchor, IEnumerable<object> items, bool atPointer = false) =>
        Menus.Show(anchor, items, open => _menuOpen = open, atPointer);

    void OnTracks(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!, BuildTrackGroups());

    List<object> BuildTrackGroups()
    {
        var groups = new List<object>();
        foreach (var (type, header) in new[] { ("sub", Loc.T("menu.subtitles")), ("audio", Loc.T("menu.audio")), ("video", Loc.T("menu.video")) })
        {
            var items = _vm.Tracks.Where(t => t.Type == type)
                .Select(t => (object)Menus.Item(t.Label, t.Selected, () => _vm.SelectTrack(type, t.Id.ToString(CultureInfo.InvariantCulture))))
                .ToList();
            if (type == "sub")
            {
                items.Insert(0, Menus.Item(Loc.T("menu.off"), !_vm.Tracks.Any(t => t.Type == "sub" && t.Selected), () => _vm.SelectTrack("sub", "no")));
                items.Add(new Separator());
                items.Add(Menus.Action(Loc.T("menu.loadFromFile"), AddSubtitle));
                items.Add(Menus.Action(Loc.T("menu.sizeUp"), () => _vm.StepSubtitleScale(0.1)));
                items.Add(Menus.Action(Loc.T("menu.sizeDown"), () => _vm.StepSubtitleScale(-0.1)));
                items.Add(Menus.Action(Loc.T("menu.moveUp"), () => _vm.StepSubtitlePos(-2)));
                items.Add(Menus.Action(Loc.T("menu.moveDown"), () => _vm.StepSubtitlePos(2)));
            }
            groups.Add(new MenuItem { Header = header, ItemsSource = items, IsEnabled = items.Count > 0 });
        }
        return groups;
    }

    void OnSpeed(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!,
        PlayerViewModel.Speeds.Select(s => Menus.Item($"{s.ToString("0.##", CultureInfo.InvariantCulture)}×",
            Math.Abs(s - _vm.Speed) < 0.01, () => _vm.SetSpeed(s))));

    void OnFpsMenu(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!,
        PlayerViewModel.FpsTargets.Select(f => Menus.Item(Loc.T(f.Label), f.Value == _vm.TargetFps, () => _vm.TargetFps = f.Value)));

    void OnLoop(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!,
    [
        Menus.Item(Loc.T("loop.off"), _vm.LoopMode == "off" && !_vm.IsAbLoopActive, () => _vm.SetLoop("off")),
        Menus.Item(Loc.T("menu.repeatFileL"), _vm.LoopMode == "file", () => _vm.SetLoop("file")),
        Menus.Item(Loc.T("menu.repeatPlaylist"), _vm.LoopMode == "playlist", () => _vm.SetLoop("playlist")),
        new Separator(),
        Menus.Action(_vm.IsAbLoopActive ? Loc.F("menu.abReset", _vm.LoopLabel) : Loc.T("menu.abPickStart"), () => _vm.AbLoop()),
    ]);

    void OnAspectMenu(object? sender, RoutedEventArgs e)
    {
        var items = new List<object>
        {
            Menus.Item(Loc.T("menu.fitWindow"), !_vm.IsFilled, () => { if (_vm.IsFilled) _vm.ToggleFill(); }),
            Menus.Item(Loc.T("menu.fillWindow"), _vm.IsFilled, () => { if (!_vm.IsFilled) _vm.ToggleFill(); }),
            new Separator(),
        };
        items.AddRange(PlayerViewModel.AspectRatios.Select(a =>
            (object)Menus.Item(Loc.F("menu.aspectItem", Loc.T(a.Label)), a.Value == _vm.AspectRatio, () => _vm.SetAspect(a.Value))));
        items.Add(new Separator());
        items.AddRange(PlayerViewModel.Rotations.Select(r =>
            (object)Menus.Item(Loc.F("menu.rotateItem", r), r == _vm.Rotation, () => _vm.SetRotation(r))));
        items.Add(Menus.Item(Loc.T("menu.mirror"), _vm.IsMirrored, () => _vm.ToggleMirror()));
        ShowMenu((Control)sender!, items);
    }


    public void ShowRecentsMenu(Control anchor)
    {
        var items = _vm.AllRecents
            .Select(r => (object)Menus.Action($"{r.Title}  ·  {r.Remaining}", () => _vm.OpenRecentCommand.Execute(r)))
            .ToList();
        if (items.Count > 0)
        {
            items.Add(new Separator());
            items.Add(Menus.Action(Loc.T("settings.clearHistory"), () => _vm.ClearRecents()));
        }
        ShowMenu(anchor, items);
    }

    // ---------------- right-click context menu ----------------

    void ShowContextMenu(Control anchor)
    {
        var items = new List<object>();

        if (!_vm.IsIdle)
        {
            items.Add(Menus.Action(_vm.IsPaused ? Loc.T("menu.playSpace") : Loc.T("menu.pauseSpace"), () => _vm.TogglePauseCommand.Execute(null)));
            items.Add(Menus.Item(Loc.T("player.beforeAfterComparisonSliderC"), _vm.IsSplitCompare, () => _vm.ToggleSplitCompareCommand.Execute(null)));
            items.Add(new Separator());
        }

        // Dosya Aç
        var recents = _vm.AllRecents
            .Select(r => (object)Menus.Action($"{r.Title} ({r.Remaining})", () => _vm.OpenRecentCommand.Execute(r)))
            .ToList();
        if (recents.Count > 0)
        {
            recents.Add(new Separator());
            recents.Add(Menus.Action(Loc.T("settings.clearHistory"), () => _vm.ClearRecents()));
        }
        items.Add(Menus.Action(Loc.T("menu.openFile"), OpenFiles));
        items.Add(Menus.Action(Loc.T("menu.openFolder"), OpenFolder));
        if (recents.Count > 0)
            items.Add(new MenuItem { Header = Loc.T("start.recentlyPlayed"), ItemsSource = recents });

        if (!_vm.IsIdle)
        {
            items.Add(new Separator());

            // 1. Oynatma hızı
            var speedItems = PlayerViewModel.Speeds
                .Select(s => (object)Menus.Item($"{s.ToString("0.##", CultureInfo.InvariantCulture)}×", Math.Abs(s - _vm.Speed) < 0.01, () => _vm.SetSpeed(s)))
                .ToList();
            items.Add(new MenuItem { Header = Loc.F("menu.speedHeader", _vm.SpeedText), ItemsSource = speedItems });

            // 2. Kare hızı / Akıcı hareket
            var fpsLabel = _vm.TargetFps > 0 ? $"{_vm.TargetFps} FPS" : (_vm.TargetFps == -1 ? "VSync" : Loc.T("layer.original"));
            var fpsItems = PlayerViewModel.FpsTargets
                .Select(f => (object)Menus.Item(Loc.T(f.Label), f.Value == _vm.TargetFps, () => _vm.TargetFps = f.Value))
                .ToList();
            items.Add(new MenuItem { Header = Loc.F("menu.fpsHeader", fpsLabel), ItemsSource = fpsItems });

            // 3. Görüntü kalitesi & Ölçekleyici
            var activePresetName = ImagePresets.All.FirstOrDefault(p => p.Id == _vm.PresetId) is { } active ? PlayerViewModel.PresetLabel(active) : Loc.T("opt.custom");
            var presetItems = ImagePresets.All
                .Select(p => (object)Menus.Item($"{PlayerViewModel.PresetLabel(p)} ({p.Scaler})", p.Id == _vm.PresetId, () => _vm.PresetId = p.Id))
                .ToList();
            items.Add(new MenuItem { Header = Loc.F("menu.qualityHeader", activePresetName), ItemsSource = presetItems });

            // 4. Renk profili
            var profileItems = PlayerViewModel.Profiles
                .Select(p => (object)Menus.Item(Loc.T($"profile.{p}"), p == _vm.ProfileName, () => _vm.ApplyProfile(p)))
                .ToList();
            items.Add(new MenuItem { Header = Loc.F("menu.profileHeader", _vm.ProfileLabel), ItemsSource = profileItems });

            // 4b. Renk ve sinema filtreleri
            var lookItems = ColorLooks.All
                .Select(l => (object)Menus.Item(Loc.T($"look.{l.Id}"), l.Id == _vm.LookId, () => _vm.LookId = l.Id))
                .ToList();
            lookItems.Add(new Separator());
            lookItems.Add(Menus.Item(Loc.T("panel.grain"), _vm.Grain > 0, () => _vm.ToggleGrain()));
            lookItems.Add(Menus.Item(Loc.T("panel.vignette"), _vm.Vignette > 0, () => _vm.ToggleVignette()));
            lookItems.Add(Menus.Item(Loc.T("panel.letterbox"), _vm.Letterbox, () => _vm.Letterbox = !_vm.Letterbox));
            items.Add(new MenuItem { Header = Loc.F("menu.lookHeader", _vm.LookName), ItemsSource = lookItems });

            // 5. Altyazı parçaları & Ayarlar
            var subList = _vm.Tracks.Where(t => t.Type == "sub").ToList();
            var subItems = new List<object>
            {
                Menus.Item(Loc.T("menu.off"), !subList.Any(t => t.Selected), () => _vm.SelectTrack("sub", "no")),
            };
            foreach (var t in subList)
                subItems.Add(Menus.Item(t.Label, t.Selected, () => _vm.SelectTrack("sub", t.Id.ToString(CultureInfo.InvariantCulture))));
            subItems.Add(new Separator());
            subItems.Add(Menus.Action(Loc.T("menu.loadFromFile"), AddSubtitle));
            subItems.Add(Menus.Action(Loc.T("menu.sizeUp"), () => _vm.StepSubtitleScale(0.1)));
            subItems.Add(Menus.Action(Loc.T("menu.sizeDown"), () => _vm.StepSubtitleScale(-0.1)));
            subItems.Add(Menus.Action(Loc.T("menu.moveUp"), () => _vm.StepSubtitlePos(-2)));
            subItems.Add(Menus.Action(Loc.T("menu.moveDown"), () => _vm.StepSubtitlePos(2)));
            items.Add(new MenuItem { Header = Loc.T("menu.subtitleTracks"), ItemsSource = subItems });

            // 6. Ses parçaları & Çıkış
            var audioList = _vm.Tracks.Where(t => t.Type == "audio").ToList();
            var audioItems = new List<object>();
            foreach (var t in audioList)
                audioItems.Add(Menus.Item(t.Label, t.Selected, () => _vm.SelectTrack("audio", t.Id.ToString(CultureInfo.InvariantCulture))));
            if (audioList.Count > 0) audioItems.Add(new Separator());
            audioItems.Add(Menus.Item(Loc.T("player.muteM"), _vm.IsMuted, () => _vm.ToggleMuteCommand.Execute(null)));
            audioItems.Add(Menus.Item(Loc.T("menu.boost"), _vm.BoostEnabled, () => _vm.BoostEnabled = !_vm.BoostEnabled));
            audioItems.Add(Menus.Item(Loc.T("menu.normalize"), _vm.Normalize, () => _vm.Normalize = !_vm.Normalize));
            audioItems.Add(new Separator());
            audioItems.AddRange(_vm.AudioDevices.Select(d =>
                (object)Menus.Item(d.Description, d.Name == _vm.Settings.AudioDevice, () => _vm.SelectAudioDevice(d.Name))));
            items.Add(new MenuItem { Header = Loc.T("menu.audioTracks"), ItemsSource = audioItems });

            // 7. En-Boy & Döndürme
            var aspectItems = new List<object>
            {
                Menus.Item(Loc.T("menu.fitWindow"), !_vm.IsFilled, () => { if (_vm.IsFilled) _vm.ToggleFill(); }),
                Menus.Item(Loc.T("menu.fillWindow"), _vm.IsFilled, () => { if (!_vm.IsFilled) _vm.ToggleFill(); }),
                new Separator(),
            };
            aspectItems.AddRange(PlayerViewModel.AspectRatios.Select(a =>
                (object)Menus.Item(Loc.F("menu.aspectItem", Loc.T(a.Label)), a.Value == _vm.AspectRatio, () => _vm.SetAspect(a.Value))));
            aspectItems.Add(new Separator());
            aspectItems.AddRange(PlayerViewModel.Rotations.Select(r =>
                (object)Menus.Item(Loc.F("menu.rotateItem", r), r == _vm.Rotation, () => _vm.SetRotation(r))));
            aspectItems.Add(Menus.Item(Loc.T("menu.mirror"), _vm.IsMirrored, () => _vm.ToggleMirror()));
            items.Add(new MenuItem { Header = Loc.T("menu.aspectRotate"), ItemsSource = aspectItems });

            // 8. Döngü & Tekrar
            var loopItems = new List<object>
            {
                Menus.Item(Loc.T("loop.off"), _vm.LoopMode == "off" && !_vm.IsAbLoopActive, () => _vm.SetLoop("off")),
                Menus.Item(Loc.T("menu.repeatFileL"), _vm.LoopMode == "file", () => _vm.SetLoop("file")),
                Menus.Item(Loc.T("loop.playlist"), _vm.LoopMode == "playlist", () => _vm.SetLoop("playlist")),
                new Separator(),
                Menus.Action(_vm.IsAbLoopActive ? Loc.F("menu.abReset", _vm.LoopLabel) : Loc.T("menu.abPick"), () => _vm.AbLoop()),
            };
            items.Add(new MenuItem { Header = Loc.F("menu.loopHeader", _vm.LoopLabel), ItemsSource = loopItems });

            if (_vm.Chapters.Count > 0)
            {
                var chItems = _vm.Chapters
                    .Select((c, i) => (object)Menus.Item($"{PlayerViewModel.Format(c.Time)} · {_vm.ChapterLabel(i)}", i == _vm.ChapterIndex, () => _vm.SeekToChapter(i)))
                    .ToList();
                items.Add(new MenuItem { Header = Loc.F("menu.chaptersHeader", _vm.Chapters.Count), ItemsSource = chItems });
            }

            items.Add(new Separator());
            items.Add(Menus.Action(Loc.T("player.captureFrameS"), () => _vm.ScreenshotCommand.Execute(null)));
            items.Add(Menus.Action(Loc.T("menu.openCaptureFolder"), () => _vm.OpenScreenshotFolder()));
        }

        items.Add(new Separator());
        items.Add(Menus.Item(Loc.T("player.fullScreenF"), Main?.WindowState == WindowState.FullScreen, ToggleFullScreen));
        items.Add(Menus.Item(Loc.T("menu.onTop"), _vm.Settings.AlwaysOnTop, () => _vm.ToggleAlwaysOnTop()));
        if (!_vm.IsIdle)
        {
            items.Add(Menus.Item(Loc.T("player.mediaStatisticsI"), _vm.ShowStats, () => _vm.ToggleStats()));
            items.Add(Menus.Action(Loc.T("menu.openPanel"), () => _vm.TogglePanelCommand.Execute(null)));
        }
        items.Add(Menus.Action(Loc.T("menu.settings"), () => _vm.OpenSettingsCommand.Execute(null)));

        ShowMenu(anchor, items, atPointer: true);
    }
}
