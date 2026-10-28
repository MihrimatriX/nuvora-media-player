using System.Runtime.InteropServices;
using System.Text;
using Player.Core;

namespace Player.Core.Tests;

/// <summary>Runs only where libmpv-2.dll can be loaded (CI downloads it with scripts/get-libmpv.ps1).</summary>
public sealed class MpvFactAttribute : FactAttribute
{
    static readonly bool Available = OperatingSystem.IsWindows()
        && NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "libmpv-2.dll"), out _);

    public MpvFactAttribute()
    {
        if (!Available) Skip = "libmpv-2.dll not available (run scripts/get-libmpv.ps1)";
    }
}

/// <summary>Drives the real libmpv headless: no window, vo=null, ao=null.</summary>
public sealed class MpvPlayerTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    static readonly Dictionary<string, string> Headless = new() { ["vo"] = "null", ["ao"] = "null", ["hwdec"] = "no" };

    readonly string _dir = Directory.CreateTempSubdirectory("nuvora-mpv-").FullName;
    readonly MpvPlayer _player;

    public MpvPlayerTests()
    {
        // Created off the xUnit sync context, so events are raised directly on the mpv event thread.
        _player = Task.Run(() => new MpvPlayer()).Result;
        _player.Initialize(0, Headless);
    }

    public void Dispose()
    {
        _player.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [MpvFact]
    public async Task LoadFile_ReportsDuration()
    {
        var duration = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
        _player.PropertyChanged += (name, value) =>
        {
            if (name == "duration" && value is double d) duration.TrySetResult(d);
        };
        _player.Observe("duration", PropertyKind.Double);

        _player.Command("loadfile", WriteWav(seconds: 1.5));

        Assert.Equal(1.5, await duration.Task.WaitAsync(Timeout), precision: 1);
    }

    [MpvFact]
    public async Task LoadFile_Missing_RaisesPlaybackError()
    {
        var error = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _player.PlaybackError += msg => error.TrySetResult(msg);

        _player.Command("loadfile", Path.Combine(_dir, "missing.mkv"));

        Assert.False(string.IsNullOrEmpty(await error.Task.WaitAsync(Timeout)));
    }

    [MpvFact]
    public async Task Dispose_WhilePlaying_ShutsDownCleanly()
    {
        var playing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _player.PropertyChanged += (name, value) =>
        {
            if (name == "time-pos" && value is double) playing.TrySetResult();
        };
        _player.Observe("time-pos", PropertyKind.Double);
        _player.Command("loadfile", WriteWav(seconds: 10));
        await playing.Task.WaitAsync(Timeout);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _player.Dispose();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"shutdown took {sw.Elapsed}");

        _player.Command("seek", "1"); // after Dispose: ignored, must not touch the freed handle
        _player.Observe("pause", PropertyKind.Flag);
    }

    /// <summary>A silent 8 kHz mono PCM file; mpv reads it without any optional decoder.</summary>
    string WriteWav(double seconds)
    {
        const int rate = 8000;
        var samples = (int)(rate * seconds);
        var path = Path.Combine(_dir, $"silence-{seconds}.wav");
        using var w = new BinaryWriter(File.Create(path), Encoding.ASCII);
        w.Write("RIFF"u8); w.Write(36 + samples * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples * 2); w.Write(new byte[samples * 2]);
        return path;
    }
}
