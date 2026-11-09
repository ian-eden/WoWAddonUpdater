using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace WoWAddonUpdater;

/// <summary>
/// Addons the user downloads by hand (CurseForge and other sites without a public API):
/// recognising their page links, and spotting the zip when it lands in the Downloads folder.
/// </summary>
public static class ManualPages
{
    /// <summary>
    /// Accepts a CurseForge addon link (normalised to https://www.curseforge.com/wow/addons/slug)
    /// or any other http(s) page that isn't GitHub. Returns a starting name for the list.
    /// </summary>
    public static bool TryParse(string input, out string pageUrl, out string name)
    {
        pageUrl = name = "";
        var s = input.Trim();

        var cf = Regex.Match(s, @"^(?:https?://)?(?:www\.)?curseforge\.com/wow/addons/([\w-]+)", RegexOptions.IgnoreCase);
        if (cf.Success)
        {
            var slug = cf.Groups[1].Value.ToLowerInvariant();
            pageUrl = $"https://www.curseforge.com/wow/addons/{slug}";
            name = NameFromSlug(slug);
            return true;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        pageUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var last = uri.Segments.Select(x => x.Trim('/')).LastOrDefault(x => x.Length > 0);
        name = last != null ? NameFromSlug(Uri.UnescapeDataString(last)) : uri.Host;
        return true;
    }

    public static string ShortText(string pageUrl)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri)) return pageUrl;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (host.Equals("curseforge.com", StringComparison.OrdinalIgnoreCase))
            return "curseforge: " + uri.Segments.Last().Trim('/');
        return host + uri.AbsolutePath.TrimEnd('/');
    }

    private static string NameFromSlug(string slug)
    {
        var words = slug.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var name = string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
        return name.Length > 0 ? name : slug;
    }

    // ---- Downloads folder ------------------------------------------------------

    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

    /// <summary>The user's Downloads folder, including when it has been moved to another drive.</summary>
    public static string DownloadsFolder()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var p) == 0)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(p);
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) return path;
                }
                finally { Marshal.FreeCoTaskMem(p); }
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>
    /// Watches a folder for a newly downloaded .zip. Call <see cref="Poll"/> about once a second;
    /// it returns a zip once the browser has finished writing it.
    /// </summary>
    public sealed class DownloadWatcher
    {
        private readonly string _folder;
        private readonly Dictionary<string, (long Length, DateTime Written)> _before;
        private readonly Dictionary<string, long> _lastSize = new(StringComparer.OrdinalIgnoreCase);

        public DownloadWatcher(string folder)
        {
            _folder = folder;
            _before = Zips().ToDictionary(f => f.FullName, f => (f.Length, f.LastWriteTimeUtc), StringComparer.OrdinalIgnoreCase);
        }

        public string Folder => _folder;

        public string? Poll()
        {
            foreach (var f in Zips())
            {
                if (_before.TryGetValue(f.FullName, out var old) && old.Length == f.Length && old.Written == f.LastWriteTimeUtc)
                    continue;                                     // was already there
                if (f.Length == 0) continue;                      // Firefox's placeholder while downloading
                if (File.Exists(f.FullName + ".part")) continue;  // Firefox still downloading

                // The size must be the same on two polls in a row, and the browser must have let go of it.
                var stable = _lastSize.TryGetValue(f.FullName, out var last) && last == f.Length;
                _lastSize[f.FullName] = f.Length;
                if (!stable) continue;
                try
                {
                    using var _ = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.None);
                }
                catch (IOException) { continue; }
                return f.FullName;
            }
            return null;
        }

        private IEnumerable<FileInfo> Zips()
        {
            try
            {
                return new DirectoryInfo(_folder).EnumerateFiles("*.zip", SearchOption.TopDirectoryOnly).ToList();
            }
            catch { return Enumerable.Empty<FileInfo>(); }
        }
    }
}
