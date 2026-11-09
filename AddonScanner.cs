using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WoWAddonUpdater;

public sealed class CatalogEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("repo")] public string Repo { get; set; } = "";
    [JsonPropertyName("folders")] public List<string> Folders { get; set; } = new();
}

public sealed class Catalog
{
    [JsonPropertyName("addons")] public List<CatalogEntry> Addons { get; set; } = new();
}

/// <summary>An installed addon we can offer to track.</summary>
public sealed record Suggestion(string Name, string Owner, string Repo, string Folder, bool Known)
{
    public string Key => $"{Owner}/{Repo}".ToLowerInvariant();
}

/// <summary>Finds addons in the AddOns folder that can be updated from GitHub.</summary>
public static class AddonScanner
{
    private static string RemoteCatalogUrl =>
        $"https://raw.githubusercontent.com/{AppInfo.RepoOwner}/{AppInfo.RepoName}/main/catalog.json";

    /// <summary>The latest catalog from GitHub, or the copy built into the exe if that fails.</summary>
    public static async Task<List<CatalogEntry>> LoadCatalogAsync(CancellationToken ct)
    {
        if (AppInfo.RepoOwner.Length > 0)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"WoWAddonUpdater/{AppInfo.VersionText}");
                var json = await http.GetStringAsync(RemoteCatalogUrl, ct);
                var remote = JsonSerializer.Deserialize<Catalog>(json);
                if (remote?.Addons is { Count: > 0 }) return remote.Addons;
            }
            catch { /* offline or file missing: use the built-in copy */ }
        }

        await using var s = typeof(AddonScanner).Assembly.GetManifestResourceStream("catalog.json");
        if (s == null) return new();
        return (await JsonSerializer.DeserializeAsync<Catalog>(s, cancellationToken: ct))?.Addons ?? new();
    }

    public static List<Suggestion> Scan(string addOnsPath, List<CatalogEntry> catalog, AppConfig cfg)
    {
        var results = new List<Suggestion>();
        if (!Directory.Exists(addOnsPath)) return results;

        var tracked = cfg.Addons.Select(a => a.Key).ToHashSet();
        var trackedFolders = cfg.Addons.SelectMany(a => a.InstalledFolders).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Real folders only: links/junctions are someone's dev copy and must stay hands-off.
        var folders = Directory.GetDirectories(addOnsPath)
            .Where(d => !Installer.IsLink(d))
            .Select(d => Path.GetFileName(d))
            .ToList();
        var present = folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Known addons from the catalog.
        foreach (var entry in catalog)
        {
            if (!GitHubClient.TryParseRepo(entry.Repo, out var owner, out var repo)) continue;
            var match = entry.Folders.FirstOrDefault(present.Contains);
            if (match == null) continue;

            // Its modules (XLoot_Frame, EllesmereUIBags, ...) belong to it too.
            foreach (var f in folders.Where(f => entry.Folders.Any(main => f.StartsWith(main, StringComparison.OrdinalIgnoreCase))))
                claimed.Add(f);

            var s = new Suggestion(entry.Name, owner, repo, match, Known: true);
            if (!tracked.Contains(s.Key) && results.All(r => r.Key != s.Key)) results.Add(s);
        }

        // 2. Other addons that name a GitHub repo in their .toc (## X-Website: https://github.com/...).
        foreach (var folder in folders)
        {
            if (claimed.Contains(folder) || trackedFolders.Contains(folder)) continue;
            var url = FindGitHubLinkInToc(Path.Combine(addOnsPath, folder));
            if (url == null || !GitHubClient.TryParseRepo(url, out var owner, out var repo)) continue;

            var s = new Suggestion(folder, owner, repo, folder, Known: false);
            if (!tracked.Contains(s.Key) && results.All(r => r.Key != s.Key)) results.Add(s);
        }

        return results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? FindGitHubLinkInToc(string dir)
    {
        try
        {
            foreach (var toc in Directory.EnumerateFiles(dir, "*.toc"))
            {
                foreach (var line in File.ReadLines(toc).Take(80))
                {
                    if (!line.TrimStart().StartsWith("## X-", StringComparison.OrdinalIgnoreCase)) continue;
                    var m = Regex.Match(line, @"https?://(?:www\.)?github\.com/[\w.-]+/[\w.-]+", RegexOptions.IgnoreCase);
                    if (m.Success) return m.Value;
                }
            }
        }
        catch { }
        return null;
    }
}
