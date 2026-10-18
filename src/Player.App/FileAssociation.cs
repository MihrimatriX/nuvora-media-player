using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Player.App;

/// <summary>Registers Nuvora Player as a media handler for the current user (HKCU, no admin rights).
/// Covers all video, audio, and playlist formats decoded by libmpv / FFmpeg.</summary>
public static partial class FileAssociation
{
    const string AppName = "Nuvora Player";
    const string ExeName = "NuvoraPlayer.exe";
    const string ProgId = "NuvoraPlayer.Media";
    const string CapabilitiesKey = @"Software\Clients\Media\Nuvora Player\Capabilities";

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
        using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
        {
            prog.SetValue("", "Nuvora Player medya dosyası");
            prog.SetValue("FriendlyTypeName", "Nuvora Player medya dosyası");
            prog.CreateSubKey("DefaultIcon").SetValue("", $"\"{exe}\",0");
            prog.CreateSubKey(@"shell\open\command").SetValue("", $"\"{exe}\" \"%1\"");
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
            dirShell.SetValue("", "Nuvora Player ile oynat");
            dirShell.SetValue("Icon", $"\"{exe}\",0");
            dirShell.CreateSubKey("command").SetValue("", $"\"{exe}\" \"%1\"");
        }
        using (var caps = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            caps.SetValue("ApplicationName", AppName);
            caps.SetValue("ApplicationDescription", "Codec paketi gerektirmeyen tüm formatlar için video ve ses oynatıcı");
            using var assoc = caps.CreateSubKey("FileAssociations");
            foreach (var ext in Extensions) assoc.SetValue(ext, ProgId);
        }
        foreach (var ext in Extensions)
        {
            using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
            if (extKey.GetValue("") is null) extKey.SetValue("", ProgId);
            using var open = extKey.CreateSubKey("OpenWithProgids");
            open.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
        using (var apps = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
            apps.SetValue(AppName, CapabilitiesKey);
        SHChangeNotify(0x08000000, 0, 0, 0); // SHCNE_ASSOCCHANGED: refresh Explorer's handler cache
    }

    [SupportedOSPlatform("windows")]
    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\Applications\{ExeName}", false);
        Registry.CurrentUser.DeleteSubKeyTree($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{ExeName}", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\NuvoraPlayer", false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\Media\Nuvora Player", false);
        Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", true)?.DeleteValue(AppName, false);
        foreach (var ext in Extensions)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}", writable: true);
            if (key is null) continue;
            key.OpenSubKey("OpenWithProgids", writable: true)?.DeleteValue(ProgId, false);
            // Register filled the default value only where it was empty; undo exactly that, or the extension
            // keeps pointing at the ProgId deleted above and Explorer shows it with no handler.
            if (key.GetValue("") as string == ProgId) key.DeleteValue("", false);
        }
        SHChangeNotify(0x08000000, 0, 0, 0);
    }

    /// <summary>Settings → Default apps, filtered to Nuvora Player (Windows 11; Windows 10 shows the general page).</summary>
    [SupportedOSPlatform("windows")]
    public static void OpenDefaultAppsPage() =>
        Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={Uri.EscapeDataString(AppName)}") { UseShellExecute = true });

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
