using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Player.App;

/// <summary>Registers Nuvora Player as a media handler for the current user (HKCU, no admin rights).
/// Covers all video, audio, and playlist formats decoded by libmpv / FFmpeg.</summary>
/// <remarks>Explorer draws a file with its ProgId's DefaultIcon unless a thumbnail provider produces a picture.
/// 0.1 gave every format one ProgId whose icon was the app logo and no thumbnail provider, so once Nuvora was the
/// default player, videos without a system-registered extractor (MKV, WebM, TS…) showed the logo instead of a frame.
/// Each kind now has its own ProgId and document icon, and the video one names Windows' own extractor.</remarks>
public static partial class FileAssociation
{
    const string AppName = "Nuvora Player";
    const string ExeName = "NuvoraPlayer.exe";
    const string CapabilitiesKey = @"Software\Clients\Media\Nuvora Player\Capabilities";

    // The video ProgId keeps 0.1's name: "default app" choices users already made point at it.
    const string VideoProgId = "NuvoraPlayer.Media", AudioProgId = "NuvoraPlayer.Audio", PlaylistProgId = "NuvoraPlayer.Playlist";
    static readonly string[] ProgIds = [VideoProgId, AudioProgId, PlaylistProgId];

    /// <summary>IThumbnailProvider, and Windows' Media Foundation video thumbnail extractor (mediametadatahandler.dll)
    /// that HKLM registers for .mp4 and friends. Any format Media Foundation can decode gets a frame.</summary>
    const string ThumbnailProviderKey = @"ShellEx\{e357fccd-a995-4576-b01f-234630154e96}";
    const string VideoThumbnailExtractor = "{c5a40261-cd64-4ccf-84cb-c394da41d590}";

    /// <param name="TypeName">Loc key of the name Explorer shows in the "Type" column.</param>
    /// <param name="Icon">Assets/FileTypes/{Icon}.ico.</param>
    /// <param name="PerceivedType">Lets Explorer use the generic video / audio handlers for formats Windows does not know.</param>
    sealed record Kind(string ProgId, string TypeName, string Icon, string? PerceivedType, string[] Extensions);

    static IEnumerable<Kind> Kinds =>
    [
        new(VideoProgId, "assoc.videoType", "video", "video", VideoExtensions),
        new(AudioProgId, "assoc.audioType", "audio", "audio", AudioExtensions),
        new(PlaylistProgId, "assoc.playlistType", "playlist", null, PlaylistExtensions),
    ];

    public static readonly string[] VideoExtensions =
    [
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".qt", ".wmv", ".asf", ".webm", ".flv", ".f4v",
        ".ts", ".m2ts", ".mts", ".tp", ".trp", ".mpg", ".mpeg", ".mpe", ".mpv", ".m2v", ".mp2v",
        ".vob", ".ifo", ".ogv", ".ogm", ".3gp", ".3g2", ".3gp2", ".3gpp", ".rm", ".rmvb",
        ".divx", ".xvid", ".dv", ".mxf", ".nut", ".hevc", ".h264", ".264", ".265", ".av1", ".vp9",
        ".y4m", ".wtv", ".dvr-ms", ".amv", ".nsv", ".bik", ".smk", ".roq", ".mk3d",
    ];

    public static readonly string[] AudioExtensions =
    [
        ".mp3", ".flac", ".m4a", ".m4b", ".m4r", ".aac", ".ogg", ".oga", ".opus", ".wav", ".wave",
        ".wma", ".ac3", ".eac3", ".dts", ".dtshd", ".truehd", ".mlp", ".ape", ".wv", ".tta", ".tak",
        ".alac", ".aiff", ".aif", ".aifc", ".au", ".snd", ".amr", ".awb", ".spx", ".mpc", ".mp2",
        ".mpa", ".ra", ".dsf", ".dff", ".caf", ".mka", ".weba", ".oma", ".ac4",
    ];

    public static readonly string[] PlaylistExtensions =
    [
        ".m3u", ".m3u8", ".pls", ".cue", ".asx", ".xspf", ".wax", ".wvx",
    ];

    public static readonly string[] SubtitleExtensions =
    [
        ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".smi", ".sami", ".lrc", ".pgs", ".ttml", ".dfxp", ".sbv",
    ];

    public static readonly string[] Extensions = [.. VideoExtensions, .. AudioExtensions, .. PlaylistExtensions];

    [SupportedOSPlatform("windows")]
    public static bool IsRegistered =>
        Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications")?.GetValue(AppName) is not null;

    [SupportedOSPlatform("windows")]
    public static void Register()
    {
        var exe = Environment.ProcessPath!;
        foreach (var kind in Kinds)
        {
            using var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{kind.ProgId}");
            prog.SetValue("", Loc.T(kind.TypeName));
            prog.SetValue("FriendlyTypeName", Loc.T(kind.TypeName));
            prog.CreateSubKey("DefaultIcon").SetValue("", IconFor(kind, exe));
            prog.CreateSubKey(@"shell\open\command").SetValue("", $"\"{exe}\" \"%1\"");
            if (kind.ProgId != VideoProgId) continue;
            prog.CreateSubKey(ThumbnailProviderKey).SetValue("", VideoThumbnailExtractor);
            prog.SetValue("TypeOverlay", ""); // no player logo stamped on every thumbnail
        }
        // Register in Applications\NuvoraPlayer.exe so Windows "Open with" always lists Nuvora Player.
        using (var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{ExeName}"))
        {
            app.SetValue("FriendlyAppName", AppName);
            app.CreateSubKey("DefaultIcon").SetValue("", $"\"{exe}\",0");
            app.CreateSubKey(@"shell\open\command").SetValue("", $"\"{exe}\" \"%1\"");
            using var types = app.CreateSubKey("SupportedTypes");
            foreach (var ext in Extensions) types.SetValue(ext, "");
        }
        // Register in App Paths so typing NuvoraPlayer in Run/Explorer works.
        using (var appPath = Registry.CurrentUser.CreateSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{ExeName}"))
        {
            appPath.SetValue("", exe);
            appPath.SetValue("Path", Path.GetDirectoryName(exe) ?? "");
        }
        // Folder context menu: "Nuvora Player ile oynat"
        using (var dirShell = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\NuvoraPlayer"))
        {
            dirShell.SetValue("", Loc.T("assoc.playWith"));
            dirShell.SetValue("Icon", $"\"{exe}\",0");
            dirShell.CreateSubKey("command").SetValue("", $"\"{exe}\" \"%1\"");
        }
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", Loc.T("assoc.description"));
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var kind in Kinds)
                foreach (var ext in kind.Extensions) assoc.SetValue(ext, kind.ProgId);
        }
        foreach (var kind in Kinds)
        {
            foreach (var ext in kind.Extensions)
            {
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
                if (extKey.GetValue("") is not string current || ProgIds.Contains(current)) extKey.SetValue("", kind.ProgId);
                // Only fills a gap: .ts, for one, is "text" (TypeScript) on developer machines and stays that way.
                if (kind.PerceivedType is { } perceived && extKey.GetValue("PerceivedType") is null && !SystemPerceivedType(ext))
                    extKey.SetValue("PerceivedType", perceived);
                using var open = extKey.CreateSubKey("OpenWithProgids");
                // 0.1 listed audio and playlists under the video ProgId too; one "Open with" entry per file is enough.
                foreach (var other in ProgIds.Where(p => p != kind.ProgId)) open.DeleteValue(other, false);
                open.SetValue(kind.ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }
        }
        using (var apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            apps.SetValue(AppName, CapabilitiesKey);
        SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED: refresh Explorer's handler cache
    }

    [SupportedOSPlatform("windows")]
    public static void Unregister()
    {
        foreach (var progId in ProgIds) Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{progId}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{ExeName}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{ExeName}", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\NuvoraPlayer", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\Media\Nuvora Player", false);
        Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true)?.DeleteValue(AppName, false);
        foreach (var ext in Extensions)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
            if (key is null) continue;
            using (var open = key.OpenSubKey("OpenWithProgids", writable: true))
                foreach (var progId in ProgIds) open?.DeleteValue(progId, false);
            // Register filled the default value only where it was empty; undo exactly that, or the extension
            // keeps pointing at a ProgId deleted above and Explorer shows it with no handler.
            // PerceivedType stays: it describes the format, and other players' thumbnails use it too.
            if (key.GetValue("") is string v && ProgIds.Contains(v)) key.DeleteValue("", false);
        }
        SHChangeNotify(0x08000000, 0, 0, 0);
    }

    [SupportedOSPlatform("windows")]
    static bool SystemPerceivedType(string ext)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{ext}");
        return key?.GetValue("PerceivedType") is not null;
    }

    /// <summary>The kind's document icon next to the exe; the app icon if the install lacks it.</summary>
    static string IconFor(Kind kind, string exe)
    {
        var icon = Path.Combine(Path.GetDirectoryName(exe) ?? "", "Assets", "FileTypes", kind.Icon + ".ico");
        return File.Exists(icon) ? $"\"{icon}\",0" : $"\"{exe}\",0";
    }

    /// <summary>Settings → Default apps, filtered to Nuvora Player (Windows 11; Windows 10 shows the general page).</summary>
    [SupportedOSPlatform("windows")]
    public static void OpenDefaultAppsPage() =>
        Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={Uri.EscapeDataString(AppName)}") { UseShellExecute = true });

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
