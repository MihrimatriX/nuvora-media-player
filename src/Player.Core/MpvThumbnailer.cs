using System.Globalization;
using System.Runtime.InteropServices;
using static Player.Core.MpvNative;

namespace Player.Core;

/// <summary>Dedicated, silent, background libmpv instance for instant SmartSeek frame thumbnails.</summary>
public sealed class MpvThumbnailer : IDisposable
{
    readonly SynchronizationContext? _sync = SynchronizationContext.Current;
    readonly AutoResetEvent _signal = new(false);
    readonly string _tempFile;
    volatile nint _ctx; // created on the caller's thread, destroyed only by the worker
    Thread? _worker;
    volatile bool _disposed;
    volatile string? _pendingPath;
    string? _loadedPath;
    volatile int _requestedSec = -1;

    public event Action<string, int, byte[]>? ThumbnailReady;

    public MpvThumbnailer(string cacheDir)
    {
        Directory.CreateDirectory(cacheDir);
        _tempFile = Path.Combine(cacheDir, $"seek-preview-{Environment.ProcessId}.jpg");
    }

    public void Initialize(nint previewWindowHandle)
    {
        if (_ctx != 0 || previewWindowHandle == 0) return;
        try
        {
            _ctx = mpv_create();
            if (_ctx == 0) return;

            SetOption("wid", previewWindowHandle.ToString(CultureInfo.InvariantCulture));
            SetOption("vo", "gpu");
            SetOption("gpu-api", "d3d11");
            SetOption("hwdec", "auto-safe");
            SetOption("vd-lavc-fast", "yes");
            SetOption("vd-lavc-skiploopfilter", "all");
            SetOption("aid", "no");
            SetOption("sid", "no");
            SetOption("audio", "no");
            SetOption("pause", "yes");
            SetOption("keep-open", "yes");
            SetOption("idle", "yes");
            SetOption("osc", "no");
            SetOption("input-default-bindings", "no");
            SetOption("input-vo-keyboard", "no");
            SetOption("input-cursor", "no");
            SetOption("hr-seek", "no");
            SetOption("vf", "lavfi=[scale=240:-2:flags=fast_bilinear]");
            SetOption("screenshot-format", "jpg");
            SetOption("screenshot-jpeg-quality", "82");

            if (mpv_initialize(_ctx) < 0)
            {
                mpv_terminate_destroy(_ctx);
                _ctx = 0;
                return;
            }

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "mpv-smartseek" };
            _worker.Start();
        }
        catch
        {
            _ctx = 0;
        }
    }

    public void SetFile(string? path)
    {
        if (_ctx == 0) return;
        _pendingPath = path;
        _requestedSec = -1;
        _signal.Set();
    }

    public void Request(int secondBucket)
    {
        if (_ctx == 0 || secondBucket < 0) return;
        _requestedSec = secondBucket;
        _signal.Set();
    }

    void WorkerLoop()
    {
        try { Work(); }
        finally
        {
            // The worker is the only thread that calls into this handle, so it destroys it: no race with
            // a synchronous mpv_command still in flight when Dispose gives up waiting.
            var ctx = _ctx;
            _ctx = 0;
            if (ctx != 0) mpv_terminate_destroy(ctx);
            try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
        }
    }

    void Work()
    {
        while (!_disposed)
        {
            _signal.WaitOne(250);
            if (_disposed || _ctx == 0) break;

            var targetPath = _pendingPath;
            if (targetPath != _loadedPath)
            {
                _loadedPath = targetPath;
                if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
                {
                    CommandSync("stop");
                    continue;
                }
                DrainEvents();
                CommandSync("loadfile", targetPath, "replace");
                WaitForEvent(EventPlaybackRestart, 1.2);
            }

            var sec = _requestedSec;
            if (sec < 0 || string.IsNullOrEmpty(_loadedPath)) continue;
            _requestedSec = -1;

            var activePath = _loadedPath;
            DrainEvents();
            CommandSync("seek", sec.ToString(CultureInfo.InvariantCulture), "absolute", "keyframes");
            WaitForEvent(EventPlaybackRestart, 0.30);

            // If user already moved to another bucket while we were seeking, skip screenshot to stay lag-free.
            if (_requestedSec >= 0 && _requestedSec != sec) continue;

            try
            {
                if (File.Exists(_tempFile)) File.Delete(_tempFile);
                if (CommandSync("screenshot-to-file", _tempFile, "video") >= 0 && File.Exists(_tempFile))
                {
                    var bytes = File.ReadAllBytes(_tempFile);
                    if (bytes.Length > 0)
                    {
                        if (_sync is null) ThumbnailReady?.Invoke(activePath, sec, bytes);
                        else _sync.Post(_ => ThumbnailReady?.Invoke(activePath, sec, bytes), null);
                    }
                }
            }
            catch
            {
                // Ignore transient I/O errors during rapid scrubbing.
            }
        }
    }

    void DrainEvents()
    {
        while (!_disposed && _ctx != 0)
        {
            var ev = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_ctx, 0));
            if (ev.EventId == 0 || ev.EventId == EventShutdown) break;
        }
    }

    void WaitForEvent(int targetEventId, double maxSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(maxSeconds);
        while (!_disposed && _ctx != 0)
        {
            var remain = (deadline - DateTime.UtcNow).TotalSeconds;
            if (remain <= 0) break;
            var ev = Marshal.PtrToStructure<MpvEvent>(mpv_wait_event(_ctx, Math.Min(0.05, remain)));
            if (ev.EventId == targetEventId || ev.EventId == EventEndFile || ev.EventId == EventShutdown) break;
        }
    }

    void SetOption(string name, string value) => mpv_set_option_string(_ctx, name, value);

    int CommandSync(params string[] args)
    {
        if (_ctx == 0) return -1;
        var utf8 = new nint[args.Length + 1];
        try
        {
            for (var i = 0; i < args.Length; i++) utf8[i] = Marshal.StringToCoTaskMemUTF8(args[i]);
            return mpv_command(_ctx, utf8);
        }
        finally
        {
            foreach (var p in utf8) Marshal.FreeCoTaskMem(p);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _signal.Set();
        if (_worker is null)
        {
            try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
            return;
        }
        // On timeout the worker still owns the handle and frees it when its current command returns.
        _worker.Join(TimeSpan.FromSeconds(1));
    }
}
