using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Player.Windows.Interop;

namespace Player.Windows;

/// <summary>Values match TBPFLAG.</summary>
public enum TaskbarProgressState { None = 0, Indeterminate = 1, Normal = 2, Error = 4, Paused = 8 }

public enum MediaButton { Play, Pause, Next, Previous, Stop }

/// <summary>Tooltips for the thumbnail toolbar, passed in so localisation stays in the app.</summary>
public sealed record ThumbBarText(string Previous, string Play, string Pause, string Next);

/// <summary>Previous / play-pause / next buttons under the taskbar thumbnail, plus playback progress on the taskbar button.
/// Feed every window message to <see cref="HandleMessage"/>; the buttons are (re)added whenever Explorer
/// announces the taskbar button, which also covers an Explorer restart.</summary>
[SupportedOSPlatform("windows")]
public sealed class TaskbarButtons : IDisposable
{
    const uint IdPrevious = 1, IdPlayPause = 2, IdNext = 3;
    static readonly uint TaskbarButtonCreated = User32.RegisterWindowMessageW("TaskbarButtonCreated");

    readonly nint _hwnd;
    readonly ThumbBarText _text;
    readonly nint _iconPlay, _iconPause, _iconPrevious, _iconNext;
    ITaskbarList3? _taskbar;
    bool _added, _playing, _canSkip = true;
    TaskbarProgressState _state;
    ulong _progress = ulong.MaxValue;

    /// <summary>Raised on the window's thread when a thumbnail button is clicked.</summary>
    public event Action<MediaButton>? Clicked;

    public TaskbarButtons(nint hwnd, ThumbBarText text)
    {
        _hwnd = hwnd;
        _text = text;
        _iconPlay = GlyphIcons.Create(Glyph.Play);
        _iconPause = GlyphIcons.Create(Glyph.Pause);
        _iconPrevious = GlyphIcons.Create(Glyph.Previous);
        _iconNext = GlyphIcons.Create(Glyph.Next);
    }

    /// <summary>Returns true when the message was a thumbnail button click and needs no further processing.</summary>
    public bool HandleMessage(uint msg, nint wParam)
    {
        if (msg == TaskbarButtonCreated && msg != 0)
        {
            Attach();
            return false;
        }
        if (msg != User32.WmCommand || (int)((wParam >> 16) & 0xFFFF) != User32.ThbnClicked) return false;
        switch ((uint)(wParam & 0xFFFF))
        {
            case IdPrevious: Clicked?.Invoke(MediaButton.Previous); return true;
            case IdNext: Clicked?.Invoke(MediaButton.Next); return true;
            case IdPlayPause: Clicked?.Invoke(_playing ? MediaButton.Pause : MediaButton.Play); return true;
            default: return false;
        }
    }

    public void SetPlaying(bool playing)
    {
        if (_playing == playing) return;
        _playing = playing;
        Push();
    }

    public void SetCanSkip(bool canSkip)
    {
        if (_canSkip == canSkip) return;
        _canSkip = canSkip;
        Push();
    }

    /// <summary>Progress on the taskbar button; <paramref name="fraction"/> is 0..1 and ignored unless the state shows a bar.</summary>
    public void SetProgress(TaskbarProgressState state, double fraction)
    {
        var value = (ulong)Math.Round(Math.Clamp(fraction, 0, 1) * 1000);
        if (state == _state && (value == _progress || state is TaskbarProgressState.None or TaskbarProgressState.Indeterminate)) return;
        if (state != _state) _taskbar?.SetProgressState(_hwnd, state);
        _state = state;
        _progress = value;
        if (state is TaskbarProgressState.Normal or TaskbarProgressState.Paused or TaskbarProgressState.Error)
            _taskbar?.SetProgressValue(_hwnd, value, 1000);
    }

    void Attach()
    {
        try
        {
            if (_taskbar is null)
            {
                var taskbar = ComObjects.Create<ITaskbarList3>(ComObjects.ClsidTaskbarList);
                if (taskbar.HrInit() < 0) return;
                _taskbar = taskbar;
            }
            _added = false;
            Push();
            // A new taskbar button starts with no progress; replay ours.
            if (_state != TaskbarProgressState.None)
            {
                _taskbar.SetProgressState(_hwnd, _state);
                _taskbar.SetProgressValue(_hwnd, _progress, 1000);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            _taskbar = null; // no shell (e.g. Server Core): stay silent, playback is unaffected
        }
    }

    unsafe void Push()
    {
        if (_taskbar is null) return;
        var buttons = stackalloc ThumbButton[3];
        Fill(ref buttons[0], IdPrevious, _iconPrevious, _text.Previous, _canSkip);
        Fill(ref buttons[1], IdPlayPause, _playing ? _iconPause : _iconPlay, _playing ? _text.Pause : _text.Play, true);
        Fill(ref buttons[2], IdNext, _iconNext, _text.Next, _canSkip);
        // Buttons can be added only once per taskbar button; afterwards they are updated in place.
        if (!_added) _added = _taskbar.ThumbBarAddButtons(_hwnd, 3, buttons) >= 0;
        else _taskbar.ThumbBarUpdateButtons(_hwnd, 3, buttons);
    }

    static void Fill(ref ThumbButton button, uint id, nint icon, string tip, bool enabled)
    {
        button.Mask = ThumbButton.MaskIcon | ThumbButton.MaskTooltip | ThumbButton.MaskFlags;
        button.Id = id;
        button.Icon = icon;
        button.Flags = enabled ? ThumbButton.FlagEnabled : ThumbButton.FlagDisabled;
        button.SetTip(tip);
    }

    public void Dispose()
    {
        if (_taskbar is not null && _state != TaskbarProgressState.None) _taskbar.SetProgressState(_hwnd, TaskbarProgressState.None);
        _taskbar = null;
        GlyphIcons.Destroy(_iconPlay);
        GlyphIcons.Destroy(_iconPause);
        GlyphIcons.Destroy(_iconPrevious);
        GlyphIcons.Destroy(_iconNext);
    }
}
