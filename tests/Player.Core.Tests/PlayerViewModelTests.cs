using Player.App;

namespace Player.Core.Tests;

// The view model calls Loc.SetLanguage, which is global, so it shares the Loc collection.
[Collection(nameof(LocTests))]
public class PlayerViewModelTests
{
    readonly FakePlayerService _mpv = new();
    readonly PlayerViewModel _vm;

    public PlayerViewModelTests() => _vm = new PlayerViewModel(_mpv, new AppSettings { Language = "tr" });

    /// <summary>File names in mpv's glsl-shaders list as it stands: the appends after the last clear.</summary>
    List<string> Shaders()
    {
        var list = _mpv.Named("change-list");
        var clear = list.FindLastIndex(c => c == "change-list glsl-shaders clr ");
        return list.Skip(clear + 1).Select(c => Path.GetFileName(c.Split(' ')[^1])).ToList();
    }

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
    public void PictureSlider_OnlySendsTheChangedProperty()
    {
        _vm.Contrast = 5;
        _mpv.Commands.Clear();

        _vm.Contrast = 6; // re-setting scale/deband would reinit the renderer on every slider step
        Assert.Equal(["set contrast 6"], _mpv.Named("set"));
    }

    [Fact]
    public void BypassRoundTrip_RestoresFpsFilterWithoutRepeatingIt()
    {
        _vm.TargetFps = 60;
        _vm.IsBypassed = true;
        _vm.IsBypassed = false;
        _vm.IsSplitCompare = true; // re-applies the picture but leaves FPS alone: the vf chain must not be rebuilt
        var add = "vf add @fps:lavfi=[framerate=fps=60:interp_start=15:interp_end=240:scene=8.2]";
        Assert.Equal(["vf remove @fps", add, "vf remove @fps", "vf remove @fps", add], _mpv.Named("vf"));
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
    public void Volume_StaleConfirmationFromMpv_DoesNotPullTheSliderBack()
    {
        _mpv.Commands.Clear();
        _vm.Volume = 50;
        _vm.Volume = 60; // the user keeps dragging
        _mpv.Raise("volume", 50.0); // mpv confirms the first value only now

        Assert.Equal(60, _vm.Volume);
        Assert.Equal(["set volume 50", "set volume 60"], _mpv.Named("set").Where(c => c.StartsWith("set volume")));

        _mpv.Raise("volume", 60.0); // caught up
        _mpv.Raise("volume", 35.0); // a later change from elsewhere is applied
        Assert.Equal(35, _vm.Volume);
        Assert.Equal(2, _mpv.Named("set").Count(c => c.StartsWith("set volume"))); // and not sent back
    }

    [Fact]
    public void Volume_ChangedWhileMuted_Unmutes()
    {
        _mpv.Raise("mute", true);
        _vm.Volume = 40;

        Assert.False(_vm.IsMuted);
        Assert.Contains("set mute no", _mpv.Named("set"));
    }

    [Theory]
    [InlineData(47, 1, 50)]
    [InlineData(50, 1, 55)]
    [InlineData(47, -1, 45)]
    [InlineData(50, -1, 45)]
    [InlineData(98, 1, 100)] // capped at volume-max without boost
    public void StepVolume_MovesToTheNextMultipleOfTheStep(double from, int direction, double expected)
    {
        _vm.Volume = from;
        _vm.StepVolume(direction);

        Assert.Equal(expected, _vm.Volume);
        Assert.Equal($"set volume {expected}", _mpv.Named("set").Last(c => c.StartsWith("set volume")));
    }

    [Fact]
    public void ToggleMute_ShowsTheNewStateAtOnce()
    {
        string? osd = null;
        _vm.OsdRequested += m => osd = m;

        _vm.ToggleMuteCommand.Execute(null);

        Assert.True(_vm.IsMuted);
        Assert.True(_vm.IsSilent);
        Assert.Equal("set mute yes", _mpv.Named("set").Last());
        Assert.Equal("Ses: Kapalı", osd);
    }

    [Fact]
    public void Play_AtTheEndOfTheFile_StartsOver()
    {
        _mpv.Raise("pause", true);
        _mpv.Raise("eof-reached", true);

        _vm.TogglePauseCommand.Execute(null);

        Assert.Equal("seek 0 absolute", Assert.Single(_mpv.Named("seek")));
        Assert.Equal("set pause no", _mpv.Named("set").Last());
        Assert.Empty(_mpv.Named("cycle"));
    }

    [Fact]
    public void NextAndPrevious_AtTheEndsOfThePlaylist_GiveFeedbackInsteadOfNothing()
    {
        var osd = new List<string>();
        _vm.OsdRequested += osd.Add;
        _mpv.Raise("playlist", """[{"filename":"a.mkv","current":true},{"filename":"b.mkv"}]""");

        _vm.PreviousCommand.Execute(null); // first file: back to its start
        Assert.Equal("seek 0 absolute", Assert.Single(_mpv.Named("seek")));
        Assert.Empty(_mpv.Named("playlist-prev"));

        _vm.NextCommand.Execute(null);
        Assert.Single(_mpv.Named("playlist-next"));

        _mpv.Raise("playlist", """[{"filename":"a.mkv"},{"filename":"b.mkv","current":true}]""");
        _vm.NextCommand.Execute(null); // last file
        Assert.Single(_mpv.Named("playlist-next"));
        Assert.Equal("Listenin sonundasınız", osd[^1]);
    }

    [Fact]
    public void Next_BeforeThePlaylistIsKnown_LeavesItToMpv()
    {
        _vm.NextCommand.Execute(null);
        Assert.Single(_mpv.Named("playlist-next"));
    }

    [Fact]
    public void TimePos_IsPacedButSeeksAndPausesGoThrough()
    {
        _mpv.Raise("duration", 7200.0);
        var updates = 0;
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PlayerViewModel.Position)) updates++; };

        _mpv.Raise("time-pos", 100.0);
        _mpv.Raise("time-pos", 100.04); // next frame, a few ms later
        _mpv.Raise("time-pos", 100.08);
        Assert.Equal(1, updates);
        Assert.Equal(100.08, _vm.Position); // the value itself is always current

        _mpv.Raise("time-pos", 900.0); // a seek
        Assert.Equal(2, updates);

        _mpv.Raise("time-pos", 900.04);
        _mpv.Raise("pause", true); // paused: show the exact frame
        Assert.Equal(3, updates);
        Assert.Equal("00:15:00", _vm.PositionText);
    }

    [Fact]
    public void StatsSummary_IsOnlyRecomputedWhileShown()
    {
        var notified = 0;
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PlayerViewModel.StatsSummary)) notified++; };

        _mpv.Raise("estimated-vf-fps", 23.976);
        Assert.Equal(0, notified);

        _vm.ShowStats = true;
        _mpv.Raise("estimated-vf-fps", 24.0);
        Assert.Equal(2, notified);
    }

    [Fact]
    public void SubtitleMargin_OnlySentWhenItChanges()
    {
        _vm.SetSubtitleMargin(120.2);
        _vm.SetSubtitleMargin(119.9);
        _vm.SetSubtitleMargin(34);

        Assert.Equal(["set sub-margin-y 120", "set sub-margin-y 34"], _mpv.Named("set").Where(c => c.Contains("sub-margin-y")));
    }

    [Fact]
    public void Look_LoadsTheFilterShaderOnce_AndOnlyUpdatesItsOptions()
    {
        _vm.SelectLookCommand.Execute("teal-orange");
        Assert.Equal("looks.glsl", Shaders()[^1]); // after the upscaler, so it grades the upscaled picture
        Assert.Single(_mpv.Named("change-list"), c => c.Contains("looks.glsl"));
        var opts = _mpv.Named("set").Last(c => c.StartsWith("set glsl-shader-opts"));
        Assert.Contains("split=0,", opts);
        Assert.Contains("lk_curve=0.25", opts);

        _vm.LookStrength = 50; // a slider: options only, the shader stays loaded
        Assert.Single(_mpv.Named("change-list"), c => c.Contains("looks.glsl"));
        Assert.Contains("lk_curve=0.125", _mpv.Named("set").Last(c => c.StartsWith("set glsl-shader-opts")));

        _vm.ResetLooks();
        Assert.DoesNotContain("looks.glsl", Shaders());
    }

    [Fact]
    public void Look_InSplitCompare_GradesOnlyTheAfterSide()
    {
        _vm.LookId = "noir";
        _vm.IsSplitCompare = true;
        _vm.SetSplitPosition(0.3, 0.3);

        var opts = _mpv.Named("set").Last(c => c.StartsWith("set glsl-shader-opts"));
        Assert.StartsWith("set glsl-shader-opts split=0.3,sharp=", opts);
        Assert.Contains("lk_m0=", opts);
        Assert.Equal(1, opts.Split(',').Count(o => o.StartsWith("split=") || o.Contains(" split=")));
    }

    [Fact]
    public void Look_IsLeftOutOfTheOriginalView()
    {
        _vm.LookId = "sepia";
        Assert.Contains("looks.glsl", Shaders());

        _vm.IsBypassed = true;
        Assert.Empty(Shaders());
    }

    [Fact]
    public void Letterbox_FollowsTheVideoShape()
    {
        _mpv.Raise("path", "a.mkv");
        _mpv.Raise("video-params/dw", 1920.0);
        _mpv.Raise("video-params/dh", 1080.0);
        _vm.Letterbox = true;
        Assert.Contains("lk_bars=0.12808", _mpv.Named("set").Last(c => c.StartsWith("set glsl-shader-opts")));

        Assert.Contains("looks.glsl", Shaders());

        _vm.SetRotation(90); // drawn before rotation: a sideways picture gets none
        Assert.DoesNotContain("looks.glsl", Shaders());
    }

    [Fact]
    public void UserPreset_RemembersTheFilters()
    {
        _vm.LookId = "vintage";
        (_vm.LookStrength, _vm.Grain, _vm.Vignette, _vm.Letterbox) = (70, 30, 40, true);
        Assert.True(_vm.SaveUserPreset("Film"));
        _vm.ResetLooks();

        _vm.ApplyUserPreset(_vm.UserPresetList.Single(p => p.Name == "Film"));

        Assert.Equal(("vintage", 70.0, 30.0, 40.0, true), (_vm.LookId, _vm.LookStrength, _vm.Grain, _vm.Vignette, _vm.Letterbox));
    }

    [Fact]
    public void SpeedUp_StepsThroughThePresetList()
    {
        _mpv.Raise("speed", 1.0);
        _vm.SpeedUpCommand.Execute(null);
        Assert.Equal("set speed 1.25", _mpv.Named("set").Last(c => c.StartsWith("set speed")));
    }
}
