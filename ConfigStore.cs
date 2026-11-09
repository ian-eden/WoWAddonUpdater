using System.Text.Json;
using Microsoft.Win32;

namespace WoWAddonUpdater;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "WoWAddonUpdater.json");

    /// <summary>Set when the config file existed but couldn't be read.</summary>
    public string? LoadError { get; private set; }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath)) return new AppConfig();
        try
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new AppConfig();
            cfg.Addons ??= new();
            return cfg;
        }
        catch (Exception ex)
        {
            // Keep the unreadable file instead of overwriting it on the next save.
            var bad = FilePath + ".bad";
            try { File.Copy(FilePath, bad, overwrite: true); } catch { }
            LoadError = $"Couldn't read {FilePath} ({ex.Message}). A copy was saved as {bad}; starting with an empty list.";
            return new AppConfig();
        }
    }

    public void Save(AppConfig cfg)
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonOptions));
        File.Move(tmp, FilePath, overwrite: true);
    }

    /// <summary>Folder name inside the WoW install for each flavor.</summary>
    public static string FlavorFolder(string flavor) => flavor.ToLowerInvariant() switch
    {
        "forever" => "_forever_",
        "mainline" => "_retail_",
        "classic" => "_classic_era_",
        _ => "_classic_",
    };

    /// <summary>Best-effort search for &lt;WoW&gt;\_flavor_\Interface\AddOns.</summary>
    public static string? DetectAddOnsPath(string flavor)
    {
        var roots = new List<string>();

        try
        {
            // Battle.net records the retail path, e.g. C:\Program Files (x86)\World of Warcraft\_retail_\
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft");
            if (key?.GetValue("InstallPath") is string p && p.Length > 0)
            {
                var dir = new DirectoryInfo(p.TrimEnd('\\'));
                roots.Add(dir.Name.StartsWith('_') ? dir.Parent?.FullName ?? dir.FullName : dir.FullName);
            }
        }
        catch { }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var r = drive.RootDirectory.FullName;
            roots.Add(Path.Combine(r, "World of Warcraft"));
            roots.Add(Path.Combine(r, "Games", "World of Warcraft"));
            roots.Add(Path.Combine(r, "Program Files (x86)", "World of Warcraft"));
            roots.Add(Path.Combine(r, "Program Files", "World of Warcraft"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var flavorDir = Path.Combine(root, FlavorFolder(flavor));
            if (Directory.Exists(flavorDir)) return Path.Combine(flavorDir, "Interface", "AddOns");
        }
        return null;
    }
}
