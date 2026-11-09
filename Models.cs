using System.Text.Json.Serialization;

namespace WoWAddonUpdater;

/// <summary>Everything saved to WoWAddonUpdater.json next to the exe.</summary>
public sealed class AppConfig
{
    public string AddOnsPath { get; set; } = "";

    /// <summary>Flavor name as used in BigWigs-packager release.json files ("forever", "mainline", "classic", ...).</summary>
    public string Flavor { get; set; } = "forever";

    /// <summary>Optional GitHub personal access token. Raises the API limit from 60 to 5000 requests/hour.</summary>
    public string? GitHubToken { get; set; }

    /// <summary>
    /// Interface numbers seen for each flavor in release.json files (e.g. forever → 16001).
    /// Used to confirm addons without a release.json by reading their .toc "## Interface:" line.
    /// </summary>
    public Dictionary<string, List<int>> KnownInterfaces { get; set; } = new() { ["forever"] = new() { 16001 } };

    public List<TrackedAddon> Addons { get; set; } = new();
}

public sealed class TrackedAddon
{
    public string Owner { get; set; } = "";
    public string Repo { get; set; } = "";
    public string Name { get; set; } = "";
    public bool AllowPrerelease { get; set; }

    // What this app last installed.
    public string? InstalledTag { get; set; }
    public string? InstalledAsset { get; set; }
    public DateTimeOffset? InstalledAt { get; set; }
    public List<string> InstalledFolders { get; set; } = new();

    // Runtime-only state.
    [JsonIgnore] public ReleaseCandidate? Latest { get; set; }
    [JsonIgnore] public string? Error { get; set; }
    [JsonIgnore] public string? Activity { get; set; }

    [JsonIgnore] public string Key => $"{Owner}/{Repo}".ToLowerInvariant();
    [JsonIgnore] public string Url => $"https://github.com/{Owner}/{Repo}";
}

/// <summary>The release + zip chosen for the configured flavor.</summary>
public sealed record ReleaseCandidate(
    string Tag,
    string AssetName,
    string DownloadUrl,
    long Size,
    DateTimeOffset Published,
    bool Prerelease,
    string? DisplayName,
    bool FlavorVerified,
    int[] FlavorInterfaces);

public class UpdaterException(string message) : Exception(message);

public sealed class RollbackFailedException(string message) : UpdaterException(message);

// ---- GitHub API shapes -------------------------------------------------------

public sealed class GhRelease
{
    [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    [JsonPropertyName("assets")] public List<GhAsset> Assets { get; set; } = new();
}

public sealed class GhAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    /// <summary>"sha256:&lt;hex&gt;", computed by GitHub when the file was uploaded.</summary>
    [JsonPropertyName("digest")] public string? Digest { get; set; }
}

// ---- release.json written by the BigWigs packager ------------------------------

public sealed class ReleaseManifest
{
    [JsonPropertyName("releases")] public List<ManifestEntry>? Releases { get; set; }
}

public sealed class ManifestEntry
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("nolib")] public bool Nolib { get; set; }
    [JsonPropertyName("metadata")] public List<ManifestFlavor> Metadata { get; set; } = new();
}

public sealed class ManifestFlavor
{
    [JsonPropertyName("flavor")] public string Flavor { get; set; } = "";
    [JsonPropertyName("interface")] public int Interface { get; set; }
}
