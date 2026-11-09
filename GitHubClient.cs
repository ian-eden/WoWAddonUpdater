using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoWAddonUpdater;

public sealed class GitHubClient : IDisposable
{
    private const int ReleasesToScan = 15;

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Words in a zip name that mean "built for some other game version".</summary>
    private static readonly HashSet<string> FlavorWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "forever", "classic", "vanilla", "era", "bcc", "tbc", "wrath", "wotlk", "cata", "mists", "mop", "retail", "mainline",
    };

    private readonly HttpClient _http;
    private readonly string? _token;

    public GitHubClient(string? token)
    {
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("WoWAddonUpdater/0.1");
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Accepts https://github.com/owner/repo (with any trailing path) or owner/repo.</summary>
    public static bool TryParseRepo(string input, out string owner, out string repo)
    {
        owner = repo = "";
        var s = input.Trim();
        var m = Regex.Match(s, @"^(?:https?://)?(?:www\.)?github\.com/([\w.-]+)/([\w.-]+)", RegexOptions.IgnoreCase);
        if (!m.Success) m = Regex.Match(s, @"^([\w.-]+)/([\w.-]+)$");
        if (!m.Success) return false;

        owner = m.Groups[1].Value;
        repo = m.Groups[2].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
        return owner.Length > 0 && repo.Length > 0;
    }

    /// <summary>
    /// Newest release with a zip for <paramref name="flavor"/>. Uses release.json when the
    /// author publishes one (BigWigs packager); otherwise guesses from the zip names.
    /// </summary>
    public async Task<ReleaseCandidate> FindLatestAsync(TrackedAddon addon, string flavor, CancellationToken ct)
    {
        var releases = await GetReleasesAsync(addon.Owner, addon.Repo, ct).ConfigureAwait(false);
        if (releases.Count == 0)
            throw new UpdaterException("No releases on GitHub. The author may only publish downloads elsewhere.");

        var usable = releases
            .Where(r => !r.Draft && (addon.AllowPrerelease || !r.Prerelease))
            .OrderByDescending(r => r.PublishedAt ?? r.CreatedAt)
            .ToList();
        if (usable.Count == 0)
            throw new UpdaterException("Only pre-releases found. Right-click → Include pre-releases to allow them.");

        var skippedForFlavor = 0;
        foreach (var rel in usable)
        {
            var zips = rel.Assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();
            if (zips.Count == 0) continue;

            var manifestAsset = rel.Assets.FirstOrDefault(a => a.Name.Equals("release.json", StringComparison.OrdinalIgnoreCase));
            if (manifestAsset != null)
            {
                var manifest = await TryGetManifestAsync(manifestAsset.DownloadUrl, ct).ConfigureAwait(false);
                if (manifest?.Releases is { Count: > 0 } entries)
                {
                    var forFlavor = entries
                        .Where(e => e.Metadata.Any(m => m.Flavor.Equals(flavor, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    var entry = forFlavor.FirstOrDefault(e => !e.Nolib) ?? forFlavor.FirstOrDefault();
                    if (entry == null) { skippedForFlavor++; continue; }

                    var asset = zips.FirstOrDefault(z => z.Name.Equals(entry.Filename, StringComparison.OrdinalIgnoreCase));
                    var interfaces = entry.Metadata
                        .Where(m => m.Flavor.Equals(flavor, StringComparison.OrdinalIgnoreCase) && m.Interface > 0)
                        .Select(m => m.Interface).Distinct().ToArray();
                    if (asset != null) return ToCandidate(rel, asset, entry.Name, verified: true, interfaces);
                    continue;
                }
            }

            var guess = GuessZip(zips, flavor);
            if (guess != null) return ToCandidate(rel, guess, null, verified: false, Array.Empty<int>());
        }

        throw new UpdaterException(skippedForFlavor > 0
            ? $"None of the recent releases list support for '{flavor}'."
            : "No downloadable .zip in the recent releases.");
    }

    public async Task DownloadAsync(string url, string destPath, IProgress<int>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new UpdaterException($"Download failed: {(int)resp.StatusCode} {resp.ReasonPhrase}");

        var total = resp.Content.Headers.ContentLength;
        await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(destPath);

        var buffer = new byte[81920];
        long done = 0;
        var lastPercent = -1;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            done += n;
            if (total is > 0)
            {
                var pct = (int)(done * 100 / total.Value);
                if (pct != lastPercent) { lastPercent = pct; progress?.Report(pct); }
            }
        }
    }

    private async Task<List<GhRelease>> GetReleasesAsync(string owner, string repo, CancellationToken ct)
    {
        var url = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases?per_page={ReleasesToScan}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (_token != null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new UpdaterException("Repository not found. Check the link.");
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
            throw new UpdaterException("GitHub rejected the token in Settings.");
        if ((resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) && Header(resp, "x-ratelimit-remaining") == "0")
        {
            var when = long.TryParse(Header(resp, "x-ratelimit-reset"), out var unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("t")
                : "later";
            throw new UpdaterException($"GitHub's hourly limit was reached; it resets at {when}. A token in Settings raises the limit.");
        }
        if (!resp.IsSuccessStatusCode)
            throw new UpdaterException($"GitHub returned {(int)resp.StatusCode} {resp.ReasonPhrase}.");

        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<List<GhRelease>>(stream, Json, ct).ConfigureAwait(false) ?? new();
    }

    private async Task<ReleaseManifest?> TryGetManifestAsync(string url, CancellationToken ct)
    {
        try
        {
            await using var stream = await _http.GetStreamAsync(url, ct).ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<ReleaseManifest>(stream, Json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return null; // fall back to guessing from zip names
        }
    }

    private static GhAsset? GuessZip(List<GhAsset> zips, string flavor)
    {
        static string[] Words(string name) =>
            Regex.Split(Path.GetFileNameWithoutExtension(name).ToLowerInvariant(), "[^a-z0-9]+");

        var exact = zips.FirstOrDefault(z => Words(z.Name).Contains(flavor.ToLowerInvariant()));
        if (exact != null) return exact;

        var neutral = zips.FirstOrDefault(z => !Words(z.Name).Any(w => FlavorWords.Contains(w) || w == "nolib"));
        if (neutral != null) return neutral;

        return zips.Count == 1 ? zips[0] : null;
    }

    private static ReleaseCandidate ToCandidate(GhRelease rel, GhAsset asset, string? displayName, bool verified, int[] interfaces) =>
        new(rel.TagName, asset.Name, asset.DownloadUrl, asset.Size, rel.PublishedAt ?? rel.CreatedAt, rel.Prerelease, displayName, verified, interfaces);

    private static string? Header(HttpResponseMessage resp, string name) =>
        resp.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
