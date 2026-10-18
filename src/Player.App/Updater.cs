using Velopack;
using Velopack.Sources;

namespace Player.App;

/// <summary>Background updates for builds installed with Setup.exe. Portable zips and dev runs are not
/// Velopack installs and skip it. A found update is downloaded and applied silently once the player exits,
/// so playback is never interrupted.</summary>
public static class Updater
{
    const string RepoUrl = "https://github.com/MihrimatriX/nuvora-media-player";

    /// <summary>Version waiting to be applied on exit, or null.</summary>
    public static string? Pending { get; private set; }

    public static async Task CheckAsync(AppSettings settings)
    {
        if (!settings.CheckUpdates) return;
        try
        {
            var mgr = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!mgr.IsInstalled) return;
            if (await mgr.CheckForUpdatesAsync() is not { } update) return;
            await mgr.DownloadUpdatesAsync(update);
            mgr.WaitExitThenApplyUpdates(update.TargetFullRelease, silent: true, restart: false);
            Pending = update.TargetFullRelease.Version.ToString();
            AppLog.Info($"update {Pending} downloaded, applies on exit");
        }
        catch (Exception e)
        {
            // Offline, rate-limited, or a broken release: the player keeps working; next launch retries.
            AppLog.Warn($"update check failed: {e.Message}");
        }
    }
}
