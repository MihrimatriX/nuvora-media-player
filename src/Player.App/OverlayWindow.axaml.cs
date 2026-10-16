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
using Player.Enhancement;

namespace Player.App;

/// <summary>Transparent, owned window drawn over the video HWND. All input and all UI live here.</summary>
public partial class OverlayWindow : Window
{
    const double ResizeGrip = 7, CornerGrip = 14;
    static readonly Cursor HiddenCursor = new(StandardCursorType.None);
    static readonly IBrush TickBrush = new SolidColorBrush(Color.Parse("#E60E1016"));

    readonly PlayerViewModel _vm = null!;
    readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    DispatcherTimer? _osdTimer;
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
        HitArea.PointerWheelChanged += (_, e) => _vm.HandleWheel(Math.Sign(e.Delta.Y));
        VolumeSlider.PointerWheelChanged += (_, e) => { _vm.StepVolume(Math.Sign(e.Delta.Y)); e.Handled = true; };
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, (_, e) => HandleKey(e), RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => HandleKeyUp(e), RoutingStrategies.Tunnel);

        // While the seek bar is held, the view drives the position instead of mpv.
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => _vm.IsScrubbing = true, RoutingStrategies.Tunnel, true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => _vm.IsScrubbing = false, RoutingStrategies.Tunnel, true);
        SeekHost.SizeChanged += (_, _) => UpdateSeekDecor();
        SeekHost.PointerMoved += OnSeekHover;
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
        if (_isDraggingSplit)
        {
            Root.Cursor = new Cursor(StandardCursorType.SizeWestEast);
            return;
        }
        var edge = GetResizeEdge(p);
        if (edge is null)
        {
            Root.Cursor = _chromeVisible ? Cursor.Default : HiddenCursor;
            return;
        }
        var type = edge.Value switch
        {
            WindowEdge.West or WindowEdge.East => StandardCursorType.SizeWestEast,
            WindowEdge.North or WindowEdge.South => StandardCursorType.SizeNorthSouth,
            WindowEdge.NorthWest or WindowEdge.SouthEast => StandardCursorType.TopLeftCorner,
            WindowEdge.NorthEast or WindowEdge.SouthWest => StandardCursorType.TopRightCorner,
            _ => StandardCursorType.Arrow,
        };
        Root.Cursor = new Cursor(type);
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
            case nameof(PlayerViewModel.Duration) or nameof(PlayerViewModel.CacheTime) or nameof(PlayerViewModel.Chapters)
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
        if (e.Key == Key.Escape)
        {
            if (_vm.IsPanelOpen) _vm.IsPanelOpen = false;
            else if (_vm.IsSplitCompare) _vm.IsSplitCompare = false;
            else if (Main is { WindowState: WindowState.FullScreen }) ToggleFullScreen();
            else return;
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; return; } // fixed second key for full screen

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
            case "loop": _vm.CycleLoop(); break;
            case "ab-loop": _vm.AbLoop(); break;
            case "aspect": _vm.CycleAspect(); break;
            case "fill": _vm.ToggleFillCommand.Execute(null); break;
            case "rotate": _vm.Rotate(); break;
            case "ontop": _vm.ToggleAlwaysOnTop(); break;
            case "screenshot": _vm.ScreenshotCommand.Execute(null); break;
            case "stats": _vm.ToggleStats(); break;
            case "panel": _vm.TogglePanelCommand.Execute(null); break;
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

    void UpdateSeekDecor()
    {
        var width = SeekHost.Bounds.Width;
        var duration = _vm.Duration;
        BufferBar.Width = duration > 0 ? Math.Clamp(_vm.CacheTime / duration, 0, 1) * width : 0;
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
        _osdTimer?.Stop();
        _osdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _osdTimer.Tick += (_, _) =>
        {
            _osdTimer.Stop();
            OsdPill.IsVisible = false;
        };
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
            Title = "Medya dosyası aç",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new("Tüm medya dosyaları") { Patterns = FileAssociation.Extensions.Select(x => "*" + x).ToList() },
                new("Video dosyaları") { Patterns = FileAssociation.VideoExtensions.Select(x => "*" + x).ToList() },
                new("Ses dosyaları") { Patterns = FileAssociation.AudioExtensions.Select(x => "*" + x).ToList() },
                new("Oynatma listeleri") { Patterns = FileAssociation.PlaylistExtensions.Select(x => "*" + x).ToList() },
                new("Tüm dosyalar") { Patterns = ["*.*"] },
            ],
        });
        _vm.Open(LocalPaths(files));
    }

    public async void OpenFolder()
    {
        // mpv expands a directory into a playlist itself.
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Klasör aç" });
        _vm.Open(LocalPaths(folders));
    }

    async void AddSubtitle()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Altyazı yükle",
            FileTypeFilter = [new("Altyazı") { Patterns = FileAssociation.SubtitleExtensions.Select(x => "*" + x).ToList() }],
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
        foreach (var (type, header) in new[] { ("sub", "Altyazı"), ("audio", "Ses"), ("video", "Video") })
        {
            var items = _vm.Tracks.Where(t => t.Type == type)
                .Select(t => (object)Menus.Item(t.Label, t.Selected, () => _vm.SelectTrack(type, t.Id.ToString(CultureInfo.InvariantCulture))))
                .ToList();
            if (type == "sub")
            {
                items.Insert(0, Menus.Item("Kapalı", !_vm.Tracks.Any(t => t.Type == "sub" && t.Selected), () => _vm.SelectTrack("sub", "no")));
                items.Add(new Separator());
                items.Add(Menus.Action("Dosyadan yükle…", AddSubtitle));
                items.Add(Menus.Action("Boyutu büyüt (+%10)", () => _vm.StepSubtitleScale(0.1)));
                items.Add(Menus.Action("Boyutu küçült (−%10)", () => _vm.StepSubtitleScale(-0.1)));
                items.Add(Menus.Action("Yukarı taşı", () => _vm.StepSubtitlePos(-2)));
                items.Add(Menus.Action("Aşağı taşı", () => _vm.StepSubtitlePos(2)));
            }
            groups.Add(new MenuItem { Header = header, ItemsSource = items, IsEnabled = items.Count > 0 });
        }
        return groups;
    }

    void OnSpeed(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!,
        PlayerViewModel.Speeds.Select(s => Menus.Item($"{s.ToString("0.##", CultureInfo.InvariantCulture)}×",
            Math.Abs(s - _vm.Speed) < 0.01, () => _vm.SetSpeed(s))));

    void OnFpsMenu(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!,
        PlayerViewModel.FpsTargets.Select(f => Menus.Item(f.Label, f.Value == _vm.TargetFps, () => _vm.TargetFps = f.Value)));

    void OnLoop(object? sender, RoutedEventArgs e) => ShowMenu((Control)sender!,
    [
        Menus.Item("Tekrar kapalı", _vm.LoopMode == "off" && !_vm.IsAbLoopActive, () => _vm.SetLoop("off")),
        Menus.Item("Dosyayı tekrarla (L)", _vm.LoopMode == "file", () => _vm.SetLoop("file")),
        Menus.Item("Oynatma listesini tekrarla", _vm.LoopMode == "playlist", () => _vm.SetLoop("playlist")),
        new Separator(),
        Menus.Action(_vm.IsAbLoopActive ? $"A–B Döngüsü ({_vm.LoopLabel}) — Sıfırla (B)" : "A–B Döngüsü başlangıç noktası seç (B)", () => _vm.AbLoop()),
    ]);

    void OnAspectMenu(object? sender, RoutedEventArgs e)
    {
        var items = new List<object>
        {
            Menus.Item("Pencereye sığdır (Letterbox)", !_vm.IsFilled, () => { if (_vm.IsFilled) _vm.ToggleFill(); }),
            Menus.Item("Pencereyi doldur / Kırp (Z)", _vm.IsFilled, () => { if (!_vm.IsFilled) _vm.ToggleFill(); }),
            new Separator(),
        };
        items.AddRange(PlayerViewModel.AspectRatios.Select(a =>
            (object)Menus.Item($"En-Boy: {a.Label}", a.Value == _vm.AspectRatio, () => _vm.SetAspect(a.Value))));
        items.Add(new Separator());
        items.AddRange(PlayerViewModel.Rotations.Select(r =>
            (object)Menus.Item($"Döndür: {r}°", r == _vm.Rotation, () => _vm.SetRotation(r))));
        items.Add(Menus.Item("Yatay çevir (Ayna)", _vm.IsMirrored, () => _vm.ToggleMirror()));
        ShowMenu((Control)sender!, items);
    }

    void OnPlaylist(object? sender, RoutedEventArgs e)
    {
        var items = _vm.Playlist.Select((p, i) => (object)Menus.Item(p.Label, p.Current, () => _vm.PlayIndex(i))).ToList();
        if (items.Count == 0) items.Add(new MenuItem { Header = "Oynatma listesi boş", IsEnabled = false });
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
            items.Add(Menus.Action("Geçmişi temizle", () => _vm.ClearRecents()));
        }
        ShowMenu(anchor, items);
    }

    // ---------------- right-click context menu ----------------

    void ShowContextMenu(Control anchor)
    {
        var items = new List<object>();

        if (!_vm.IsIdle)
        {
            items.Add(Menus.Action(_vm.IsPaused ? "Oynat (Boşluk)" : "Duraklat (Boşluk)", () => _vm.TogglePauseCommand.Execute(null)));
            items.Add(Menus.Item("Önce / Sonra karşılaştırma çubuğu (C)", _vm.IsSplitCompare, () => _vm.ToggleSplitCompareCommand.Execute(null)));
            items.Add(new Separator());
        }

        // Dosya Aç
        var recents = _vm.AllRecents
            .Select(r => (object)Menus.Action($"{r.Title} ({r.Remaining})", () => _vm.OpenRecentCommand.Execute(r)))
            .ToList();
        if (recents.Count > 0)
        {
            recents.Add(new Separator());
            recents.Add(Menus.Action("Geçmişi temizle", () => _vm.ClearRecents()));
        }
        items.Add(Menus.Action("Dosya aç… (Ctrl+O)", OpenFiles));
        items.Add(Menus.Action("Klasör aç…", OpenFolder));
        if (recents.Count > 0)
            items.Add(new MenuItem { Header = "Son oynatılanlar", ItemsSource = recents });

        if (!_vm.IsIdle)
        {
            items.Add(new Separator());

            // 1. Oynatma hızı
            var speedItems = PlayerViewModel.Speeds
                .Select(s => (object)Menus.Item($"{s.ToString("0.##", CultureInfo.InvariantCulture)}×", Math.Abs(s - _vm.Speed) < 0.01, () => _vm.SetSpeed(s)))
                .ToList();
            items.Add(new MenuItem { Header = $"Oynatma hızı ({_vm.SpeedText})", ItemsSource = speedItems });

            // 2. Kare hızı / Akıcı hareket
            var fpsLabel = _vm.TargetFps > 0 ? $"{_vm.TargetFps} FPS" : (_vm.TargetFps == -1 ? "VSync" : "Orijinal");
            var fpsItems = PlayerViewModel.FpsTargets
                .Select(f => (object)Menus.Item(f.Label, f.Value == _vm.TargetFps, () => _vm.TargetFps = f.Value))
                .ToList();
            items.Add(new MenuItem { Header = $"Kare hızı / Akıcı hareket ({fpsLabel})", ItemsSource = fpsItems });

            // 3. Görüntü kalitesi & Ölçekleyici
            var activePresetName = ImagePresets.All.FirstOrDefault(p => p.Id == _vm.PresetId)?.Name ?? "Özel";
            var presetItems = ImagePresets.All
                .Select(p => (object)Menus.Item($"{p.Name} ({p.Scaler})", p.Id == _vm.PresetId, () => _vm.PresetId = p.Id))
                .ToList();
            items.Add(new MenuItem { Header = $"Görüntü kalitesi & Ölçekleyici ({activePresetName})", ItemsSource = presetItems });

            // 4. Renk profili
            var profileItems = PlayerViewModel.Profiles
                .Select(p => (object)Menus.Item(p, p == _vm.ProfileName, () => _vm.ApplyProfile(p)))
                .ToList();
            items.Add(new MenuItem { Header = $"Renk profili ({_vm.ProfileName})", ItemsSource = profileItems });

            // 5. Altyazı parçaları & Ayarlar
            var subList = _vm.Tracks.Where(t => t.Type == "sub").ToList();
            var subItems = new List<object>
            {
                Menus.Item("Kapalı", !subList.Any(t => t.Selected), () => _vm.SelectTrack("sub", "no")),
            };
            foreach (var t in subList)
                subItems.Add(Menus.Item(t.Label, t.Selected, () => _vm.SelectTrack("sub", t.Id.ToString(CultureInfo.InvariantCulture))));
            subItems.Add(new Separator());
            subItems.Add(Menus.Action("Dosyadan yükle…", AddSubtitle));
            subItems.Add(Menus.Action("Boyutu büyüt (+%10)", () => _vm.StepSubtitleScale(0.1)));
            subItems.Add(Menus.Action("Boyutu küçült (−%10)", () => _vm.StepSubtitleScale(-0.1)));
            subItems.Add(Menus.Action("Yukarı taşı", () => _vm.StepSubtitlePos(-2)));
            subItems.Add(Menus.Action("Aşağı taşı", () => _vm.StepSubtitlePos(2)));
            items.Add(new MenuItem { Header = "Altyazı parçaları", ItemsSource = subItems });

            // 6. Ses parçaları & Çıkış
            var audioList = _vm.Tracks.Where(t => t.Type == "audio").ToList();
            var audioItems = new List<object>();
            foreach (var t in audioList)
                audioItems.Add(Menus.Item(t.Label, t.Selected, () => _vm.SelectTrack("audio", t.Id.ToString(CultureInfo.InvariantCulture))));
            if (audioList.Count > 0) audioItems.Add(new Separator());
            audioItems.Add(Menus.Item("Sessiz (M)", _vm.IsMuted, () => _vm.ToggleMuteCommand.Execute(null)));
            audioItems.Add(Menus.Item("Ses yükseltme (%200 Boost)", _vm.BoostEnabled, () => _vm.BoostEnabled = !_vm.BoostEnabled));
            audioItems.Add(Menus.Item("Ses normalizasyonu (dynaudnorm)", _vm.Normalize, () => _vm.Normalize = !_vm.Normalize));
            audioItems.Add(new Separator());
            audioItems.AddRange(_vm.AudioDevices.Select(d =>
                (object)Menus.Item(d.Description, d.Name == _vm.Settings.AudioDevice, () => _vm.SelectAudioDevice(d.Name))));
            items.Add(new MenuItem { Header = "Ses parçaları & Çıkış", ItemsSource = audioItems });

            // 7. En-Boy & Döndürme
            var aspectItems = new List<object>
            {
                Menus.Item("Pencereye sığdır (Letterbox)", !_vm.IsFilled, () => { if (_vm.IsFilled) _vm.ToggleFill(); }),
                Menus.Item("Pencereyi doldur / Kırp (Z)", _vm.IsFilled, () => { if (!_vm.IsFilled) _vm.ToggleFill(); }),
                new Separator(),
            };
            aspectItems.AddRange(PlayerViewModel.AspectRatios.Select(a =>
                (object)Menus.Item($"En-Boy: {a.Label}", a.Value == _vm.AspectRatio, () => _vm.SetAspect(a.Value))));
            aspectItems.Add(new Separator());
            aspectItems.AddRange(PlayerViewModel.Rotations.Select(r =>
                (object)Menus.Item($"Döndür: {r}°", r == _vm.Rotation, () => _vm.SetRotation(r))));
            aspectItems.Add(Menus.Item("Yatay çevir (Ayna)", _vm.IsMirrored, () => _vm.ToggleMirror()));
            items.Add(new MenuItem { Header = "En-Boy & Döndürme", ItemsSource = aspectItems });

            // 8. Döngü & Tekrar
            var loopItems = new List<object>
            {
                Menus.Item("Tekrar kapalı", _vm.LoopMode == "off" && !_vm.IsAbLoopActive, () => _vm.SetLoop("off")),
                Menus.Item("Dosyayı tekrarla (L)", _vm.LoopMode == "file", () => _vm.SetLoop("file")),
                Menus.Item("Listeyi tekrarla", _vm.LoopMode == "playlist", () => _vm.SetLoop("playlist")),
                new Separator(),
                Menus.Action(_vm.IsAbLoopActive ? $"A–B Döngüsü ({_vm.LoopLabel}) — Sıfırla (B)" : "A–B Döngüsü seç (B)", () => _vm.AbLoop()),
            };
            items.Add(new MenuItem { Header = $"Döngü ({_vm.LoopLabel})", ItemsSource = loopItems });

            if (_vm.Chapters.Count > 0)
            {
                var chItems = _vm.Chapters
                    .Select((c, i) => (object)Menus.Item($"{PlayerViewModel.Format(c.Time)} · {_vm.ChapterLabel(i)}", i == _vm.ChapterIndex, () => _vm.SeekToChapter(i)))
                    .ToList();
                items.Add(new MenuItem { Header = $"Bölümler ({_vm.Chapters.Count})", ItemsSource = chItems });
            }

            items.Add(new Separator());
            items.Add(Menus.Action("Kare yakala (S)", () => _vm.ScreenshotCommand.Execute(null)));
            items.Add(Menus.Action("Kare yakalama klasörünü aç", () => _vm.OpenScreenshotFolder()));
        }

        items.Add(new Separator());
        items.Add(Menus.Item("Tam ekran (F)", Main?.WindowState == WindowState.FullScreen, ToggleFullScreen));
        items.Add(Menus.Item("Her zaman üstte (T)", _vm.Settings.AlwaysOnTop, () => _vm.ToggleAlwaysOnTop()));
        if (!_vm.IsIdle)
        {
            items.Add(Menus.Item("Medya istatistikleri (I)", _vm.ShowStats, () => _vm.ToggleStats()));
            items.Add(Menus.Action("Görüntü & Ses panelini aç (E)", () => _vm.TogglePanelCommand.Execute(null)));
        }
        items.Add(Menus.Action("Ayarlar… (Ctrl+,)", () => _vm.OpenSettingsCommand.Execute(null)));

        ShowMenu(anchor, items, atPointer: true);
    }
}
