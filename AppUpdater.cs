using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace WoWAddonUpdater;

public sealed record AppRelease(Version Version, string Tag, string PageUrl, string DownloadUrl, long Size, string? Sha256);

/// <summary>
/// Updates this app from its own GitHub releases. A running exe can't be overwritten on
/// Windows, but it can be renamed: so the new exe is downloaded next to it, the running one
/// is renamed to .old, the new one takes its name, and the app restarts. The next start
/// deletes the .old file. Settings (WoWAddonUpdater.json) are untouched.
/// </summary>
public static class AppUpdater
{
    public static bool Enabled => AppInfo.RepoOwner.Length > 0;

    private static string ExePath => Environment.ProcessPath ?? throw new UpdaterException("Can't find the app's own exe.");
    private static string OldPath => ExePath + ".old";
    private static string NewPath => ExePath + ".new";

    /// <summary>Deletes leftovers from a previous update. The old process may take a moment to exit, so retry.</summary>
    public static void CleanUpInBackground() => _ = Task.Run(async () =>
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                if (File.Exists(OldPath)) File.Delete(OldPath);
                if (File.Exists(NewPath)) File.Delete(NewPath);
                return;
            }
            catch { await Task.Delay(500); }
        }
    });

    /// <summary>The newest release if it's newer than this build and has a file for this variant; otherwise null.</summary>
    public static async Task<AppRelease?> CheckAsync(string? token, CancellationToken ct)
    {
        if (!Enabled) return null;

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"WoWAddonUpdater/{AppInfo.VersionText}");
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{AppInfo.RepoOwner}/{AppInfo.RepoName}/releases/latest");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null; // no releases yet
        if (!resp.IsSuccessStatusCode) throw new UpdaterException($"GitHub returned {(int)resp.StatusCode} {resp.ReasonPhrase}.");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var rel = await JsonSerializer.DeserializeAsync<GhRelease>(stream, cancellationToken: ct);
        if (rel == null || !TryParseVersion(rel.TagName, out var version) || version <= AppInfo.Version) return null;

        var asset = rel.Assets.FirstOrDefault(a => a.Name.Equals(AppInfo.AssetName, StringComparison.OrdinalIgnoreCase));
        if (asset == null) return null;

        var sha = asset.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d[7..] : null;
        return new AppRelease(version, rel.TagName, rel.HtmlUrl, asset.DownloadUrl, asset.Size, sha);
    }

    /// <summary>Downloads, verifies and swaps in the new exe, then starts it. The caller should exit right after.</summary>
    public static async Task InstallAndRestartAsync(AppRelease release, IProgress<int> progress, CancellationToken ct)
    {
        var exe = ExePath;
        try
        {
            using (var gh = new GitHubClient(null))
                await gh.DownloadAsync(release.DownloadUrl, NewPath, progress, ct);
            Verify(release);
        }
        catch
        {
            TryDelete(NewPath);
            throw;
        }

        try
        {
            File.Move(exe, OldPath, overwrite: true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            TryDelete(NewPath);
            throw new UpdaterException(
                $"Windows didn't allow replacing the app in {Path.GetDirectoryName(exe)} ({ex.Message}). " +
                "Download the new version from the releases page instead.");
        }

        try
        {
            File.Move(NewPath, exe);
        }
        catch
        {
            File.Move(OldPath, exe); // put the working version back
            TryDelete(NewPath);
            throw;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    private static void Verify(AppRelease release)
    {
        var file = new FileInfo(NewPath);
        if (release.Size > 0 && file.Length != release.Size)
            throw new UpdaterException($"The download was incomplete ({file.Length:N0} of {release.Size:N0} bytes).");

        if (release.Sha256 != null)
        {
            using var s = file.OpenRead();
            var hash = Convert.ToHexString(SHA256.HashData(s));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new UpdaterException("The downloaded file didn't match GitHub's checksum, so it wasn't installed.");
        }

        using var f = file.OpenRead();
        if (f.ReadByte() != 'M' || f.ReadByte() != 'Z')
            throw new UpdaterException("The download isn't a Windows program, so it wasn't installed.");
    }

    private static bool TryParseVersion(string tag, out Version version)
    {
        var s = tag.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut >= 0) s = s[..cut];
        if (Version.TryParse(s, out var v)) { version = AppInfo.Normalize(v); return true; }
        version = new Version(0, 0);
        return false;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
