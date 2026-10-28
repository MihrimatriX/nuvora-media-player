using System.ComponentModel;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Threading;
using Player.Windows;

namespace Player.App;

/// <summary>Connects the player to the Windows shell: media keys, the media flyout and lock screen (SMTC),
/// taskbar thumbnail buttons and progress, and recent files in the jump list. Each part fails on its own
/// and only logs; playback never depends on any of them.</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
sealed class ShellIntegration : IDisposable
{
    const string RecentCategory = "Son açılanlar";

    readonly Window _window;
    readonly PlayerViewModel _vm;
    readonly TaskbarButtons? _taskbar;
    readonly MediaSession? _media;
    readonly Win32Properties.CustomWndProcHookCallback _hook;
    string? _jumpListKey;
    int _failures;
    bool _disposed;

    public static ShellIntegration? TryAttach(Window window, PlayerViewModel vm) =>
        window.TryGetPlatformHandle() is { Handle: var hwnd } && hwnd != 0 ? new ShellIntegration(window, vm, hwnd) : null;

    ShellIntegration(Window window, PlayerViewModel vm, nint hwnd)
    {
        _window = window;
        _vm = vm;
        _hook = WndProc;
        _taskbar = Try("taskbar", () => new TaskbarButtons(hwnd, new ThumbBarText("Önceki", "Oynat", "Duraklat", "Sonraki")));
        if (_taskbar is not null)
        {
            _taskbar.Clicked += Execute;
            Win32Properties.AddWndProcHookCallback(window, _hook);
        }
        _media = Try("media controls", () => new MediaSession(hwnd));
        if (_media is not null)
        {
            // SMTC raises these on a thread-pool thread.
            _media.ButtonPressed += b => Dispatcher.UIThread.Post(() => Execute(b));
            _media.SeekRequested += t => Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed) _vm.SeekTo(t.TotalSeconds);
            });
        }
        vm.PropertyChanged += OnViewModelChanged;
        UpdateStatus();
        UpdateMetadata();
        UpdateCanSkip();
        UpdateJumpList();
    }

    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerViewModel.Position) or nameof(PlayerViewModel.Duration):
                UpdateProgress();
                break;
            case nameof(PlayerViewModel.IsPaused) or nameof(PlayerViewModel.IsIdle) or nameof(PlayerViewModel.Speed):
                UpdateStatus();
                break;
            case nameof(PlayerViewModel.Title) or nameof(PlayerViewModel.Artist) or nameof(PlayerViewModel.HasVideo):
                UpdateMetadata();
                break;
            case nameof(PlayerViewModel.Playlist):
                UpdateCanSkip();
                break;
            case nameof(PlayerViewModel.AllRecents):
                UpdateJumpList();
                break;
        }
    }

    void Execute(MediaButton button)
    {
        if (_disposed) return;
        switch (button)
        {
            case MediaButton.Play: _vm.SetPaused(false); break;
            // Stop keeps the file loaded so playback can resume where it was; it acts like pause.
            case MediaButton.Pause or MediaButton.Stop: _vm.SetPaused(true); break;
            case MediaButton.Next: _vm.NextCommand.Execute(null); break;
            case MediaButton.Previous: _vm.PreviousCommand.Execute(null); break;
        }
    }

    void UpdateStatus()
    {
        var playing = !_vm.IsIdle && !_vm.IsPaused;
        _taskbar?.SetPlaying(playing);
        Guard(() =>
        {
            _media?.SetStatus(_vm.IsIdle ? MediaStatus.Closed : _vm.IsPaused ? MediaStatus.Paused : MediaStatus.Playing);
            _media?.SetRate(playing ? _vm.Speed : 0);
        });
        UpdateProgress();
    }

    void UpdateProgress()
    {
        var known = !_vm.IsIdle && _vm.Duration > 0; // live streams have no duration
        _taskbar?.SetProgress(
            !known ? TaskbarProgressState.None : _vm.IsPaused ? TaskbarProgressState.Paused : TaskbarProgressState.Normal,
            known ? _vm.Position / _vm.Duration : 0);
        if (known) Guard(() => _media?.SetTimeline(_vm.Position, _vm.Duration, _vm.IsPaused ? 0 : _vm.Speed));
    }

    void UpdateMetadata()
    {
        if (_vm.IsIdle || _vm.Title.Length == 0) return;
        Guard(() => _media?.SetMetadata(_vm.Title, _vm.Artist, _vm.HasVideo));
    }

    void UpdateCanSkip()
    {
        var canSkip = _vm.Playlist.Count > 1;
        _taskbar?.SetCanSkip(canSkip);
        Guard(() => _media?.SetCanSkip(canSkip));
    }

    void UpdateJumpList()
    {
        var items = _vm.AllRecents
            .Select(r => new JumpListItem(r.Path, r.Title.Length > 0 ? r.Title : Path.GetFileName(r.Path)))
            .ToList();
        var key = string.Join('\n', items.Select(i => i.Path + '\t' + i.Title));
        if (key == _jumpListKey || Environment.ProcessPath is not { } exe) return;
        _jumpListKey = key;
        Guard(() => JumpList.SetRecent(RecentCategory, items, exe));
    }

    nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam, ref bool handled)
    {
        if (_taskbar is not null && _taskbar.HandleMessage(msg, wParam)) handled = true;
        return 0;
    }

    static T? Try<T>(string part, Func<T> create) where T : class
    {
        try { return create(); }
        catch (Exception e)
        {
            AppLog.Warn($"Windows {part} unavailable: {e.Message}");
            return null;
        }
    }

    /// <summary>Shell updates run on every position tick, so a persistent failure is logged only a few times.</summary>
    void Guard(Action action)
    {
        try { action(); }
        catch (Exception e) when (++_failures <= 5) { AppLog.Warn($"Windows shell update failed: {e.Message}"); }
        catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vm.PropertyChanged -= OnViewModelChanged;
        if (_taskbar is not null)
        {
            Win32Properties.RemoveWndProcHookCallback(_window, _hook);
            _taskbar.Dispose();
        }
        Guard(() => _media?.Dispose());
    }
}
