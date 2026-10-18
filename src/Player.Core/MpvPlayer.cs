using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using static Player.Core.MpvNative;

namespace Player.Core;

/// <summary>Thin libmpv wrapper. Every call is non-blocking on the mpv side; events are read on a
/// dedicated thread and posted to the SynchronizationContext of the thread that created this object.</summary>
public sealed class MpvPlayer : IPlayerService
{
    static readonly Dictionary<string, string> Defaults = new()
    {
        ["vo"] = "gpu-next",
        ["gpu-api"] = "d3d11",
        ["hwdec"] = "auto-safe",
        ["hwdec-codecs"] = "all",
        ["vd-lavc-fast"] = "yes",
        ["vd-lavc-threads"] = "0",
        ["cache"] = "auto",
        ["demuxer-max-bytes"] = "128MiB",
        ["demuxer-max-back-bytes"] = "64MiB",
        ["hr-seek"] = "default",
        ["hr-seek-framedrop"] = "yes",
        ["osc"] = "no",
        ["input-default-bindings"] = "no",
        ["keep-open"] = "yes",
        ["idle"] = "yes",
        ["background-color"] = "#0B0C12", // idle/letterbox colour; mpv's idle default is grey
        // The overlay window owns mouse, keyboard and cursor; mpv's own window must ignore them.
        ["input-vo-keyboard"] = "no",
        ["input-cursor"] = "no",
        ["cursor-autohide"] = "no",
    };

    readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    // Guards _ctx: the event thread destroys the handle, so callers on other threads must not race it.
    readonly Lock _gate = new();
    nint _ctx;
    Thread? _events;
    volatile bool _disposing;

    public event Action<string, object?>? PropertyChanged;
    public event Action<string>? PlaybackError;

    public void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options)
    {
        if (_ctx != 0) throw new InvalidOperationException("Already initialized.");
        _ctx = mpv_create();
        if (_ctx == 0) throw new InvalidOperationException("mpv_create failed.");

        SetOption("wid", windowHandle.ToString(CultureInfo.InvariantCulture));
        foreach (var (name, value) in Defaults.Concat(options)) SetOption(name, value);

        var err = mpv_initialize(_ctx);
        if (err < 0)
        {
            mpv_terminate_destroy(_ctx);
            _ctx = 0;
            throw new InvalidOperationException($"mpv_initialize: {ErrorString(err)}");
        }
        _events = new Thread(EventLoop) { IsBackground = true, Name = "mpv-events" };
        _events.Start();
    }

    public void Observe(string name, PropertyKind kind)
    {
        lock (_gate)
            if (_ctx != 0 && !_disposing) mpv_observe_property(_ctx, 0, name, kind);
    }

    public void Command(params string[] args)
    {
        if (_disposing) return;
        Send(args);
    }

    void Send(string[] args)
    {
        var utf8 = new nint[args.Length + 1]; // last slot stays 0 = terminator
        try
        {
            for (var i = 0; i < args.Length; i++) utf8[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            // mpv_command_async only queues the command, so holding the lock here is cheap.
            lock (_gate)
                if (_ctx != 0) mpv_command_async(_ctx, 0, utf8);
        }
        finally
        {
            foreach (var p in utf8) Marshal.FreeCoTaskMem(p);
        }
    }

    public void Dispose()
    {
        if (_disposing) return;
        _disposing = true;
        if (_events is null) return; // never initialized
        // "quit" lets mpv write watch-later state, then emits SHUTDOWN; the event thread destroys the handle.
        Send(["quit"]);
        // mpv_terminate_destroy must not run while another thread is inside mpv_wait_event, so on timeout
        // the handle is left to the event thread (or to process exit) instead of being freed under it.
        if (!_events.Join(TimeSpan.FromSeconds(3)))
            Debug.WriteLine("mpv did not shut down within 3 s; leaving the handle to the event thread");
    }

    void SetOption(string name, string value)
    {
        var err = mpv_set_option_string(_ctx, name, value);
        // Options unknown to an older libmpv build are not fatal; the player works without them.
        if (err < 0) Debug.WriteLine($"mpv option {name}={value}: {ErrorString(err)}");
    }

    void EventLoop()
    {
        while (true)
        {
            var ev = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_ctx, -1));
            switch (ev.EventId)
            {
                case EventShutdown:
                    // Only this thread waits on the handle, so it is the one place that can free it safely.
                    lock (_gate)
                    {
                        mpv_terminate_destroy(_ctx);
                        _ctx = 0;
                    }
                    return;
                case EventPropertyChange:
                    // Event data is only valid until the next mpv_wait_event, so copy it out here.
                    var p = Marshal.PtrToStructure<MpvEventProperty>(ev.Data);
                    var name = Marshal.PtrToStringUTF8(p.Name)!;
                    var value = ReadValue(p);
                    Post(() => PropertyChanged?.Invoke(name, value));
                    break;
                case EventEndFile:
                    var end = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                    if (end.Reason == EndFileReasonError)
                    {
                        var msg = ErrorString(end.Error);
                        Post(() => PlaybackError?.Invoke(msg));
                    }
                    break;
            }
        }
    }

    static object? ReadValue(MpvEventProperty p) => p.Data == 0 ? null : p.Format switch
    {
        PropertyKind.Double => Marshal.PtrToStructure<double>(p.Data),
        PropertyKind.Flag => Marshal.ReadInt32(p.Data) != 0,
        PropertyKind.String => Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(p.Data)),
        _ => null,
    };

    void Post(Action action)
    {
        if (_sync is null) action();
        else _sync.Post(_ => action(), null);
    }
}
