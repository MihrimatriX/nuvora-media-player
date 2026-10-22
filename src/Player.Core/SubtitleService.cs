using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Player.Core;

public record SubtitleItem(
    string Id,
    string ReleaseName,
    string FileName,
    string Language,
    string LanguageCode,
    string Format,
    double Rating,
    int Downloads,
    string DownloadUrl
)
{
    public string DisplayTitle => !string.IsNullOrWhiteSpace(ReleaseName) ? ReleaseName : FileName;
    public string StatsText => $"★ {Rating:0.0} · {Downloads:N0} indirme · {Format.ToUpperInvariant()}";
    public string LangBadge => LanguageCode.ToUpperInvariant();
}

public static class SubtitleService
{
    static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "TemporaryUserAgent" } }
    };

    /// <summary>Cleans raw file names into a clean search query (e.g., "The.Matrix.1999.1080p.BluRay.x264" -> "The Matrix 1999")</summary>
    public static string CleanTitleForSearch(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var name = Path.GetFileNameWithoutExtension(raw);
        name = Regex.Replace(name, @"[\._\-\+]", " ");
        name = Regex.Replace(name, @"(?i)\b(1080p|720p|2160p|4k|bluray|bdrip|webrip|web-dl|x264|x265|hevc|dvdrip|aac|rarbg|yts|proper|repack)\b.*$", "");
        return Regex.Replace(name, @"\s+", " ").Trim();
    }

    public static async Task<List<SubtitleItem>> SearchAsync(string query, string lang = "tur", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var cleanQuery = Uri.EscapeDataString(query.Trim());
        var langParam = string.IsNullOrWhiteSpace(lang) || lang == "all" ? "all" : lang;
        var url = $"https://rest.opensubtitles.org/search/query-{cleanQuery}/sublanguageid-{langParam}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await Client.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return [];

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var list = new List<SubtitleItem>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                var id = elem.TryGetProperty("IDSubtitleFile", out var idE) ? idE.GetString() ?? "" : "";
                var rel = elem.TryGetProperty("MovieReleaseName", out var relE) ? relE.GetString()?.Trim() ?? "" : "";
                var file = elem.TryGetProperty("SubFileName", out var fileE) ? fileE.GetString()?.Trim() ?? "" : "";
                var langName = elem.TryGetProperty("LanguageName", out var lE) ? lE.GetString() ?? "Türkçe" : "Türkçe";
                var langCode = elem.TryGetProperty("ISO639", out var isoE) ? isoE.GetString() ?? "tr" : "tr";
                var fmt = elem.TryGetProperty("SubFormat", out var fmtE) ? fmtE.GetString() ?? "srt" : "srt";
                var dl = elem.TryGetProperty("SubDownloadLink", out var dlE) ? dlE.GetString() ?? "" : "";

                var rating = 0.0;
                if (elem.TryGetProperty("SubRating", out var ratE))
                {
                    if (ratE.ValueKind == JsonValueKind.Number) rating = ratE.GetDouble();
                    else if (ratE.ValueKind == JsonValueKind.String && double.TryParse(ratE.GetString(), out var r)) rating = r;
                }

                var downloads = 0;
                if (elem.TryGetProperty("SubDownloadsCnt", out var downE))
                {
                    if (downE.ValueKind == JsonValueKind.Number) downloads = downE.GetInt32();
                    else if (downE.ValueKind == JsonValueKind.String && int.TryParse(downE.GetString(), out var d)) downloads = d;
                }

                if (!string.IsNullOrWhiteSpace(dl))
                {
                    list.Add(new SubtitleItem(id, rel, file, langName, langCode, fmt, rating, downloads, dl));
                }
            }

            return list;
        }
        catch
        {
            return [];
        }
    }

    public static async Task<string?> DownloadAndDecompressAsync(SubtitleItem item, string targetDir, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(item.DownloadUrl)) return null;
        try
        {
            Directory.CreateDirectory(targetDir);
            using var res = await Client.GetAsync(item.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return null;

            await using var stream = await res.Content.ReadAsStreamAsync(ct);
            await using var gzip = new GZipStream(stream, CompressionMode.Decompress);

            var safeName = string.Join("_", item.FileName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = $"subtitle_{item.Id}.{item.Format}";
            if (!safeName.EndsWith("." + item.Format, StringComparison.OrdinalIgnoreCase))
                safeName += "." + item.Format;

            var targetFile = Path.Combine(targetDir, safeName);
            await using var fs = File.Create(targetFile);
            await gzip.CopyToAsync(fs, ct);

            return targetFile;
        }
        catch
        {
            return null;
        }
    }
}

