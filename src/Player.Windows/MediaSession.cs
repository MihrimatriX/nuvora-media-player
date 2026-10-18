using System.Runtime.Versioning;
using Windows.Media;

namespace Player.Windows;

public enum MediaStatus { Closed, Stopped, Playing, Paused }

/// <summary>SystemMediaTransportControls for the main window: hardware media keys, the Windows media flyout
/// and the lock screen. Events arrive on a thread-pool thread; marshal them to the UI thread.</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class MediaSession : IDisposable
{
    readonly SystemMediaTransportControls _smtc;
    readonly TimelineThrottle _timeline = new();

    public event Action<MediaButton>? ButtonPressed;
    public event Action<TimeSpan>? SeekRequested;

    public MediaSession(nint hwnd)
    {
        // Unpackaged desktop apps have no CoreWindow; the interop factory binds SMTC to our HWND instead.
        _smtc = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsStopEnabled = true;
        _smtc.IsNextEnabled = true;
        _smtc.IsPreviousEnabled = true;
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.IsEnabled = false;
        _smtc.ButtonPressed += OnButtonPressed;
        _smtc.PlaybackPositionChangeRequested += OnPositionRequested;
    }

    public void SetMetadata(string title, string? artist, bool isVideo)
    {
        var display = _smtc.DisplayUpdater;
        display.ClearAll();
        display.Type = isVideo ? MediaPlaybackType.Video : MediaPlaybackType.Music;
        if (isVideo)
        {
            display.VideoProperties.Title = title;
            display.VideoProperties.Subtitle = artist ?? "";
        }
        else
        {
            display.MusicProperties.Title = title;
            display.MusicProperties.Artist = artist ?? "";
        }
        display.Update();
    }

    public void SetStatus(MediaStatus status)
    {
        _smtc.IsEnabled = status != MediaStatus.Closed;
        _smtc.PlaybackStatus = status switch
        {
            MediaStatus.Playing => MediaPlaybackStatus.Playing,
            MediaStatus.Paused => MediaPlaybackStatus.Paused,
            MediaStatus.Stopped => MediaPlaybackStatus.Stopped,
            _ => MediaPlaybackStatus.Closed,
        };
        _timeline.Invalidate();
    }

    public void SetCanSkip(bool canSkip)
    {
        _smtc.IsNextEnabled = canSkip;
        _smtc.IsPreviousEnabled = canSkip;
    }

    public void SetRate(double rate)
    {
        _smtc.PlaybackRate = rate;
        _timeline.Invalidate();
    }

    /// <summary>Call on every position tick; the flyout extrapolates on its own, so only jumps and a periodic refresh are sent.</summary>
    public void SetTimeline(double position, double duration, double rate)
    {
        if (!_timeline.ShouldSend(position, duration, rate, Environment.TickCount64)) return;
        var end = TimeSpan.FromSeconds(Math.Max(0, duration));
        _smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            EndTime = end,
            MinSeekTime = TimeSpan.Zero,
            MaxSeekTime = end,
            Position = TimeSpan.FromSeconds(Math.Clamp(position, 0, Math.Max(0, duration))),
        });
    }

    void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        MediaButton? button = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaButton.Play,
            SystemMediaTransportControlsButton.Pause => MediaButton.Pause,
            SystemMediaTransportControlsButton.Stop => MediaButton.Stop,
            SystemMediaTransportControlsButton.Next => MediaButton.Next,
            SystemMediaTransportControlsButton.Previous => MediaButton.Previous,
            _ => null,
        };
        if (button is { } b) ButtonPressed?.Invoke(b);
    }

    void OnPositionRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args) =>
        SeekRequested?.Invoke(args.RequestedPlaybackPosition);

    public void Dispose()
    {
        _smtc.ButtonPressed -= OnButtonPressed;
        _smtc.PlaybackPositionChangeRequested -= OnPositionRequested;
        _smtc.DisplayUpdater.ClearAll();
        _smtc.DisplayUpdater.Update();
        _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
        _smtc.IsEnabled = false;
    }
}

/// <summary>Decides when the SMTC timeline needs a refresh: after a seek (the position left the extrapolated
/// track), when duration or rate change, after <see cref="Invalidate"/>, and otherwise every few seconds.</summary>
public sealed class TimelineThrottle
{
    const long RefreshMs = 5000;
    const double JumpSeconds = 1.5;

    double _position, _duration, _rate;
    long _sentAt;
    bool _dirty = true;

    public void Invalidate() => _dirty = true;

    public bool ShouldSend(double position, double duration, double rate, long nowMs)
    {
        var expected = _position + (nowMs - _sentAt) / 1000.0 * _rate;
        var send = _dirty
            || duration != _duration
            || rate != _rate
            || Math.Abs(position - expected) > JumpSeconds
            || nowMs - _sentAt >= RefreshMs;
        if (!send) return false;
        _dirty = false;
        _position = position;
        _duration = duration;
        _rate = rate;
        _sentAt = nowMs;
        return true;
    }
}
