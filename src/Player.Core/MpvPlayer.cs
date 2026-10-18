using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using static Player.Core.MpvNative;

namespace Player.Core;

/// <summary>Thin libmpv wrapper. Every call is non-blocking on the mpv side; events are read on a
/// dedicated thread and handed to the dispatcher given to the constructor.</summary>
public sealed class MpvPlayer : IPlayerService
{
    /// <summary>Client API major version this wrapper's struct layouts are written against.</summary>
    public const int RequiredApiMajor = 2;

    // High-frequency properties: the event thread keeps only the newest value and posts at most one update
    // at a time, so a 60 fps time-pos stream cannot queue up behind a busy UI thread.
    static readonly HashSet<string> Coalesced = ["time-pos", "demuxer-cache-time", "estimated-vf-fps"];

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

    readonly Action<Action> _post;
    readonly ConcurrentDictionary<string, object?> _latest = new();
    readonly ConcurrentDictionary<string, byte> _queued = new();
    readonly ConcurrentDictionary<ulong, string> _pending = new(); // reply id -> command name, for error logs
    long _nextReply;
    // Guards _ctx: the event thread destroys the handle, so callers on other threads must not race it.
    readonly Lock _gate = new();
    nint _ctx;
    Thread? _events;
    volatile bool _disposing;

    public event Action<string, object?>? PropertyChanged;
    public event Action<string>? PlaybackError;
    public event Action? SeekCompleted;
    public event Action<string>? Log;

    /// <param name="post">Runs an event handler on the consumer's thread (the UI dispatcher in the app).
    /// Null runs handlers on the mpv event thread, which suits tests and headless use.</param>
    public MpvPlayer(Action<Action>? post = null) => _post = post ?? (a => a());

    /// <summary>The loaded DLL's client API version as (major, minor), or null when it cannot be loaded.</summary>
    public static (int Major, int Minor)? ApiVersion()
    {
        try
        {
            var v = mpv_client_api_version();
            return ((int)(v >> 16), (int)(v & 0xFFFF));
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    public void Initialize(nint windowHandle, IReadOnlyDictionary<string, string> options)
    {
        if (_ctx != 0) throw new InvalidOperationException("Already initialized.");
        // Struct layouts in MpvNative follow client API 2.x; a 1.x DLL (mpv < 0.33) would be read wrong.
        var api = mpv_client_api_version();
        if (api >> 16 != RequiredApiMajor)
            throw new InvalidOperationException(
                $"libmpv-2.dll istemci API'si {api >> 16}.{api & 0xFFFF}; {RequiredApiMajor}.x gerekli. README'deki sürümü kullanın.");
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
        mpv_request_log_messages(_ctx, "warn");
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
        // A unique reply id lets the COMMAND_REPLY event name the command that failed.
        var id = (ulong)Interlocked.Increment(ref _nextReply);
        if (args.Length > 0) _pending[id] = args[0];
        var utf8 = new nint[args.Length + 1]; // last slot stays 0 = terminator
        try
        {
            for (var i = 0; i < args.Length; i++) utf8[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            // mpv_command_async only queues the command, so holding the lock here is cheap.
            lock (_gate)
                if (_ctx == 0 || mpv_command_async(_ctx, id, utf8) < 0) _pending.TryRemove(id, out _);
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
            Log?.Invoke("mpv did not shut down within 3 s; leaving the handle to the event thread");
    }

    void SetOption(string name, string value)
    {
        var err = mpv_set_option_string(_ctx, name, value);
        // Options unknown to an older libmpv build are not fatal; the player works without them.
        if (err < 0) Log?.Invoke($"mpv option {name}={value}: {ErrorString(err)}");
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
                    if (Coalesced.Contains(name)) PostLatest(name, value);
                    else _post(() => PropertyChanged?.Invoke(name, value));
                    break;
                case EventPlaybackRestart:
                    _post(() => SeekCompleted?.Invoke());
                    break;
                case EventCommandReply:
                    if (_pending.TryRemove(ev.ReplyUserdata, out var command) && ev.Error < 0)
                    {
                        var failure = $"mpv command '{command}' failed: {ErrorString(ev.Error)}";
                        _post(() => Log?.Invoke(failure));
                    }
                    break;
                case EventLogMessage:
                    var m = Marshal.PtrToStructure<MpvEventLogMessage>(ev.Data);
                    var line = $"mpv [{Marshal.PtrToStringUTF8(m.Prefix)}] {Marshal.PtrToStringUTF8(m.Level)}: {Marshal.PtrToStringUTF8(m.Text)?.TrimEnd()}";
                    _post(() => Log?.Invoke(line));
                    break;
                case EventEndFile:
                    var end = Marshal.PtrToStructure<MpvEventEndFile>(ev.Data);
                    if (end.Reason == EndFileReasonError)
                    {
                        var msg = ErrorString(end.Error);
                        _post(() => PlaybackError?.Invoke(msg));
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

    void PostLatest(string name, object? value)
    {
        _latest[name] = value;
        if (!_queued.TryAdd(name, 0)) return; // an update is already on its way and will read the newest value
        _post(() =>
        {
            _queued.TryRemove(name, out _);
            if (_latest.TryGetValue(name, out var newest)) PropertyChanged?.Invoke(name, newest);
        });
    }
}
