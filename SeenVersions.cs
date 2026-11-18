using System.Text.RegularExpressions;

namespace WoWAddonUpdater;

/// <summary>
/// Many addons tell each other their version in game (guild/group addon messages) and print
/// "a new version is available". Some also save what they heard in their SavedVariables file
/// (WTF\Account\...\SavedVariables\&lt;AddonFolder&gt;.lua), which WoW writes on logout or /reload.
/// Reading those files (read-only) lets manual-download addons show "Update available" too.
/// </summary>
public static class SeenVersions
{
    // An explicit "newest version seen" setting, e.g. ["newestSeenVersion"] = "2.0.10".
    private static readonly Regex NewestKey = new(
        @"\[""(?<k>[A-Za-z_]*(?:newest|latest|highest|newer|max)[A-Za-z_]*version[A-Za-z_]*)""\]\s*=\s*""(?<v>[^""\r\n]{1,40})""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Plain version fields, e.g. other players' records: ["version"] = "16001-41".
    private static readonly Regex VersionKey = new(
        @"\[""version""\]\s*=\s*""(?<v>[^""\r\n]{1,40})""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const long MaxFileSize = 32L * 1024 * 1024;

    /// <summary>
    /// The highest version newer than <paramref name="installed"/> found in the addon's saved files,
    /// or null. Only values shaped like the installed version count (2.0.10 vs 2.0.9, 16001-42 vs
    /// 16001-41), so unrelated "version" fields are ignored.
    /// </summary>
    public static string? FindNewer(string addOnsPath, IEnumerable<string> folders, string? installed)
    {
        if (string.IsNullOrWhiteSpace(installed)) return null;
        var accountRoot = AccountFolder(addOnsPath);
        if (accountRoot == null) return null;

        string? best = null;
        foreach (var file in SavedFiles(accountRoot, folders))
        {
            string text;
            try
            {
                var info = new FileInfo(file);
                if (info.Length == 0 || info.Length > MaxFileSize) continue;
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                text = reader.ReadToEnd();
            }
            catch { continue; }

            foreach (var m in NewestKey.Matches(text).Concat(VersionKey.Matches(text)))
            {
                var v = m.Groups["v"].Value.Trim();
                if (!SameShape(v, installed)) continue;
                if (Compare(v, installed) <= 0) continue;
                if (best == null || Compare(v, best) > 0) best = v;
            }
        }
        return best;
    }

    /// <summary>...\_forever_\Interface\AddOns → ...\_forever_\WTF\Account</summary>
    private static string? AccountFolder(string addOnsPath)
    {
        try
        {
            var gameFolder = Directory.GetParent(Path.GetFullPath(addOnsPath))?.Parent;
            if (gameFolder == null) return null;
            var account = Path.Combine(gameFolder.FullName, "WTF", "Account");
            return Directory.Exists(account) ? account : null;
        }
        catch { return null; }
    }

    /// <summary>Account-wide (Account\NAME\SavedVariables) and per-character (Account\NAME\Realm\Char\SavedVariables) files.</summary>
    private static IEnumerable<string> SavedFiles(string accountRoot, IEnumerable<string> folders)
    {
        var names = folders.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f + ".lua").ToList();
        var result = new List<string>();
        try
        {
            foreach (var account in Directory.EnumerateDirectories(accountRoot))
            {
                var dirs = new List<string> { Path.Combine(account, "SavedVariables") };
                foreach (var realm in Directory.EnumerateDirectories(account))
                    foreach (var character in Directory.EnumerateDirectories(realm))
                        dirs.Add(Path.Combine(character, "SavedVariables"));

                foreach (var dir in dirs.Where(Directory.Exists))
                    foreach (var name in names)
                    {
                        var file = Path.Combine(dir, name);
                        if (File.Exists(file)) result.Add(file);
                    }
            }
        }
        catch { }
        return result;
    }

    /// <summary>Digits replaced by one placeholder: "16001-41" and "16001-42" match, "2" and "2.0.9" don't.</summary>
    private static bool SameShape(string a, string b) =>
        string.Equals(Shape(a), Shape(b), StringComparison.OrdinalIgnoreCase);

    private static string Shape(string v) => Regex.Replace(v.Trim(), @"\d+", "9");

    /// <summary>Compares the numbers in two same-shaped versions, left to right.</summary>
    public static int Compare(string a, string b)
    {
        var x = Numbers(a);
        var y = Numbers(b);
        for (var i = 0; i < Math.Max(x.Count, y.Count); i++)
        {
            var c = (i < x.Count ? x[i] : 0).CompareTo(i < y.Count ? y[i] : 0);
            if (c != 0) return c;
        }
        return 0;
    }

    private static List<long> Numbers(string v) =>
        Regex.Matches(v, @"\d+").Select(m => long.TryParse(m.Value.Length > 18 ? m.Value[..18] : m.Value, out var n) ? n : 0).ToList();
}
