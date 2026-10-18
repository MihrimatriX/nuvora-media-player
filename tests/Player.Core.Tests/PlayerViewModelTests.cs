using Player.App;

namespace Player.Core.Tests;

public class PlayerViewModelTests
{
    readonly FakePlayerService _mpv = new();
    readonly PlayerViewModel _vm;

    public PlayerViewModelTests() => _vm = new PlayerViewModel(_mpv, new AppSettings());

    [Fact]
    public void Scrubbing_SendsOneKeyframeSeekAtATime_ThenExactOnRelease()
    {
        _vm.IsScrubbing = true;
        _vm.Position = 10;
        _vm.Position = 20;
        _vm.Position = 30;

        Assert.Equal(["seek 10 absolute+keyframes"], _mpv.Named("seek"));

        _mpv.CompleteSeek(); // mpv finished the first seek: only the newest target is sent
        Assert.Equal(["seek 10 absolute+keyframes", "seek 30 absolute+keyframes"], _mpv.Named("seek"));

        _vm.IsScrubbing = false;
        Assert.Equal("seek 30 absolute+exact", _mpv.Named("seek")[^1]);
    }

    [Fact]
    public void Position_WithoutScrubbing_DoesNotSeek()
    {
        _vm.Position = 42;
        Assert.Empty(_mpv.Named("seek"));
    }

    [Fact]
    public void TimePos_IsIgnoredWhileScrubbing()
    {
        _vm.IsScrubbing = true;
        _vm.Position = 50;
        _mpv.Raise("time-pos", 12.0);
        Assert.Equal(50, _vm.Position);
    }

    [Fact]
    public void Open_ReplacesWithFirstAndAppendsTheRest()
    {
        _vm.Open(["a.mkv", "b.mkv", "c.mkv"]);
        Assert.Equal(["loadfile a.mkv replace", "loadfile b.mkv append", "loadfile c.mkv append"], _mpv.Named("loadfile"));
    }

    [Fact]
    public void PlayerProperties_MapToViewModelState()
    {
        _mpv.Raise("pause", true);
        _mpv.Raise("idle-active", false);
        _mpv.Raise("duration", 3600.0);
        _mpv.Raise("track-list", """[{"id":1,"type":"audio","lang":"tur","codec":"ac3","selected":true,"external":false}]""");

        Assert.True(_vm.IsPaused);
        Assert.False(_vm.IsIdle);
        Assert.Equal("01:00:00", _vm.DurationText);
        Assert.Equal("TUR · ac3", Assert.Single(_vm.Tracks).Label);
    }

    [Fact]
    public void Chapters_ResolveByTime()
    {
        _mpv.Raise("chapter-list", """[{"title":"Giriş","time":0},{"title":"Orta","time":600},{"title":"Son","time":1200}]""");

        Assert.Equal(-1, _vm.ChapterAt(-1));
        Assert.Equal(0, _vm.ChapterAt(10));
        Assert.Equal(1, _vm.ChapterAt(600));
        Assert.Equal(2, _vm.ChapterAt(5000));
        Assert.Equal("Bölüm 2 · Orta", _vm.ChapterLabel(1));
    }

    [Fact]
    public void PlaybackError_IsShownToTheUser()
    {
        _mpv.RaiseError("unrecognized file format");
        Assert.Equal("Dosya oynatılamadı: unrecognized file format", _vm.ErrorText);
    }

    [Fact]
    public void VideoSize_IsReportedOncePerFile()
    {
        var sizes = new List<(double, double)>();
        _vm.VideoSizeKnown += (w, h) => sizes.Add((w, h));

        _mpv.Raise("path", "a.mkv");
        _mpv.Raise("video-params/dw", 1920.0);
        _mpv.Raise("video-params/dh", 1080.0);
        _mpv.Raise("video-params/dh", 1080.0); // same file, no second fit
        _mpv.Raise("path", "b.mkv");
        _mpv.Raise("video-params/dw", 1280.0);
        _mpv.Raise("video-params/dh", 720.0);

        Assert.Equal([(1920.0, 1080.0), (1280.0, 720.0)], sizes);
    }

    [Theory]
    [InlineData(0, 0.0)]      // inside the left pillarbox
    [InlineData(960, 0.5)]    // centre of the window = centre of the video
    [InlineData(1920, 1.0)]   // inside the right pillarbox
    public void ComputeVideoSplit_AccountsForPillarbox(double x, double expected)
    {
        _mpv.Raise("path", "a.mkv");
        _mpv.Raise("video-params/dw", 1440.0); // 4:3 video in a 16:9 window: 240 px bars each side
        _mpv.Raise("video-params/dh", 1080.0);

        Assert.Equal(expected, _vm.ComputeVideoSplit(x, 1920, 1080), 3);
    }

    [Fact]
    public void SpeedUp_StepsThroughThePresetList()
    {
        _mpv.Raise("speed", 1.0);
        _vm.SpeedUpCommand.Execute(null);
        Assert.Equal("set speed 1.25", _mpv.Named("set").Last(c => c.StartsWith("set speed")));
    }
}
