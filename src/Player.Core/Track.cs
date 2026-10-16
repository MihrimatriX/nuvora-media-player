using System.Text.Json;

namespace Player.Core;

/// <summary>One entry of mpv's track-list. Type is "video", "audio" or "sub".</summary>
public sealed record Track(long Id, string Type, string? Title, string? Lang, string? Codec, bool Selected, bool External)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>mpv renders node properties (track-list) as JSON when read as a string.</summary>
    public static IReadOnlyList<Track> ParseList(string json) => JsonSerializer.Deserialize<List<Track>>(json, Json) ?? [];

    public string Label
    {
        get
        {
            var text = string.Join(" · ", new[] { Title, Lang?.ToUpperInvariant(), Codec }.Where(s => !string.IsNullOrEmpty(s)));
            return text.Length > 0 ? text : $"#{Id}";
        }
    }
}
