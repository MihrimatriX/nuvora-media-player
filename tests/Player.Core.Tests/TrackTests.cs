using Player.Core;

namespace Player.Core.Tests;

public class TrackTests
{
    [Fact]
    public void ParsesMpvTrackListJson()
    {
        const string json = """
            [{"id":1,"type":"video","codec":"hevc","selected":true,"external":false},
             {"id":2,"type":"audio","lang":"tur","title":"Dublaj","selected":false},
             {"id":1,"type":"sub","external":true,"selected":true,"external-filename":"x.srt"}]
            """;

        var tracks = Track.ParseList(json);

        Assert.Equal(3, tracks.Count);
        Assert.True(tracks[0].Selected);
        Assert.Equal("hevc", tracks[0].Label);
        Assert.Equal("Dublaj · TUR", tracks[1].Label);
        Assert.Equal("#1", tracks[2].Label);
        Assert.True(tracks[2].External);
    }
}
