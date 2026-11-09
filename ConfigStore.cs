using System.Text.Json;
using Microsoft.Win32;

namespace WoWAddonUpdater;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string FileName = "WoWAddonUpdater.json";

    /// <summary>
    /// %AppData%\WoWAddonUpdater\WoWAddonUpdater.json, so the list stays put wherever the exe lives.
    /// Portable mode: if a WoWAddonUpdater.json sits next to the exe, that one is used instead.
    /// </summary>
    public string FilePath { get; }

    public bool IsPortable { get; }

    public ConfigStore()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, FileName);
        IsPortable = File.Exists(portable);
        FilePath = IsPortable
            ? portable
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WoWAddonUpdater", FileName);
    }

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
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
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

    /// <summary>
    /// Best-effort search for &lt;WoW&gt;\_flavor_\Interface\AddOns, in this order:
    /// 1. Battle.net's own list of installed games (finds WoW wherever the player installed it)
    /// 2. The registry entry Battle.net writes for WoW
    /// 3. Common install folders, starting with the default C:\Program Files (x86)\World of Warcraft
    /// </summary>
    public static string? DetectAddOnsPath(string flavor)
    {
        var roots = new List<string>();

        roots.AddRange(BattleNetInstallPaths());

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
            roots.Add(Path.Combine(r, "Program Files (x86)", "World of Warcraft"));
            roots.Add(Path.Combine(r, "Program Files", "World of Warcraft"));
            roots.Add(Path.Combine(r, "World of Warcraft"));
            roots.Add(Path.Combine(r, "Games", "World of Warcraft"));
            roots.Add(Path.Combine(r, "Blizzard", "World of Warcraft"));
        }

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var flavorDir = Path.Combine(root, FlavorFolder(flavor));
            if (Directory.Exists(flavorDir)) return Path.Combine(flavorDir, "Interface", "AddOns");
        }
        return null;
    }

    /// <summary>
    /// Install folders recorded by the Battle.net app in C:\ProgramData\Battle.net\Agent\product.db.
    /// The file is binary, but the paths are stored as plain text, e.g. "D:/Games/World of Warcraft".
    /// </summary>
    private static IEnumerable<string> BattleNetInstallPaths()
    {
        var results = new List<string>();
        try
        {
            var db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Battle.net", "Agent", "product.db");
            if (!File.Exists(db)) return results;

            var text = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(db));
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(text, @"[A-Za-z]:[/\\][^\x00-\x1f""<>|?*]{1,200}"))
            {
                var path = m.Value.Replace('/', '\\').TrimEnd('\\');
                if (path.Contains("Warcraft", StringComparison.OrdinalIgnoreCase)) results.Add(path);
            }
        }
        catch { }
        return results;
    }
}
