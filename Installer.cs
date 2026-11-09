using System.IO.Compression;

namespace WoWAddonUpdater;

public static class Installer
{
    /// <summary>
    /// Scratch folder created next to AddOns (in Interface\), so moving folders into
    /// AddOns stays on the same drive and is instant. WoW only loads AddOns\, so it ignores this.
    /// </summary>
    private const string WorkFolderName = ".WoWAddonUpdater-work";

    /// <summary>
    /// Downloads the zip, extracts it to a staging folder, then swaps the addon's folders in
    /// AddOns. Old folders are moved aside first and restored if anything fails.
    /// Returns the folder names now installed.
    /// </summary>
    public static async Task<List<string>> InstallAsync(
        GitHubClient gh, TrackedAddon addon, ReleaseCandidate release, string addOnsPath,
        IProgress<int> progress, Action<string> log, CancellationToken ct)
    {
        using var staged = StagedInstall.Create(addOnsPath, log);
        var zipPath = Path.Combine(staged.Job, "download.zip");
        await gh.DownloadAsync(release.DownloadUrl, zipPath, progress, ct);
        await ExtractIntoAsync(staged, zipPath, log, ct);
        return await CommitAsync(staged, addon.InstalledFolders, log, ct);
    }

    /// <summary>
    /// Extracts a zip the user downloaded themselves into a staging folder, so its folders,
    /// version and title can be checked before <see cref="CommitAsync"/> puts it in AddOns.
    /// Dispose the result to clean up.
    /// </summary>
    public static async Task<StagedInstall> StageZipAsync(string zipPath, string addOnsPath, Action<string> log, CancellationToken ct)
    {
        var staged = StagedInstall.Create(addOnsPath, log);
        try
        {
            await ExtractIntoAsync(staged, zipPath, log, ct);
            return staged;
        }
        catch
        {
            staged.Dispose();
            throw;
        }
    }

    /// <summary>Swaps the staged folders into AddOns. Returns the folder names now installed.</summary>
    public static async Task<List<string>> CommitAsync(StagedInstall staged, IReadOnlyCollection<string> previousFolders,
        Action<string> log, CancellationToken ct)
    {
        try
        {
            var notes = await Task.Run(() => Swap(staged.AddOns, staged.Items, staged.Backup, previousFolders.ToList()), ct);
            foreach (var n in notes) log(n);
            return staged.Folders.ToList();
        }
        catch (RollbackFailedException)
        {
            staged.KeepJob = true; // the old files are still in the backup folder
            throw;
        }
    }

    private static async Task ExtractIntoAsync(StagedInstall staged, string zipPath, Action<string> log, CancellationToken ct)
    {
        log("extracting…");
        List<(string Name, string Source)> items;
        List<string> skipped;
        try
        {
            (items, skipped) = await Task.Run(() => ExtractAddonFolders(zipPath, staged.Staging), ct);
        }
        catch (InvalidDataException)
        {
            throw new UpdaterException($"{Path.GetFileName(zipPath)} isn't a valid zip file (or it didn't finish downloading).");
        }
        foreach (var s in skipped) log($"ignored '{s}' in the zip (no .toc file, so not an addon folder)");
        if (items.Count == 0) throw new UpdaterException("The zip has no addon folders (no .toc files found).");
        staged.Items = items;

        // The main folder is usually the one with the shortest name (Questie vs QuestieDB, XLoot vs XLoot_Frame).
        var main = items.OrderBy(i => i.Name.Length).First();
        staged.Version = ReadTocField(main.Source, main.Name, "Version");
        staged.Title = CleanTitle(ReadTocField(main.Source, main.Name, "Title"));
    }

    /// <summary>Reads "## Version:" from an installed addon's .toc, if there is one.</summary>
    public static string? ReadTocVersion(string addOnsPath, string folder) =>
        ReadTocField(Path.Combine(addOnsPath, folder), folder, "Version");

    private static string? ReadTocField(string dir, string folder, string field)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            var toc = Path.Combine(dir, folder + ".toc");
            if (!File.Exists(toc)) toc = Directory.EnumerateFiles(dir, "*.toc").FirstOrDefault() ?? "";
            if (toc.Length == 0) return null;

            var prefix = $"## {field}:";
            foreach (var line in File.ReadLines(toc).Take(60))
            {
                var t = line.Trim();
                if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var v = t[prefix.Length..].Trim();
                    return v.Length == 0 || v.Contains('@') ? null : v; // "@project-version@" = unpackaged dev copy
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Removes WoW colour codes (|cffRRGGBB ... |r) from a .toc title.</summary>
    private static string? CleanTitle(string? title)
    {
        if (title == null) return null;
        var t = System.Text.RegularExpressions.Regex.Replace(title, @"\|c[0-9a-fA-F]{8}|\|r", "").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// True if an installed addon folder declares support for the flavor: either a
    /// flavor-specific .toc (e.g. Questie_Forever.toc) or a "## Interface:" number we know
    /// belongs to that flavor.
    /// </summary>
    public static bool TocSupportsFlavor(string addOnsPath, string folder, string flavor, IReadOnlyCollection<int> interfaces)
    {
        try
        {
            var dir = Path.Combine(addOnsPath, folder);
            if (!Directory.Exists(dir)) return false;

            foreach (var toc in Directory.EnumerateFiles(dir, "*.toc"))
            {
                var name = Path.GetFileNameWithoutExtension(toc);
                if (name.EndsWith("_" + flavor, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("-" + flavor, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (interfaces.Count == 0) continue;
                foreach (var line in File.ReadLines(toc).Take(60))
                {
                    var t = line.Trim();
                    if (!t.StartsWith("## Interface:", StringComparison.OrdinalIgnoreCase)) continue;
                    var numbers = t["## Interface:".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                    if (numbers.Any(n => int.TryParse(n, out var i) && interfaces.Contains(i))) return true;
                }
            }
        }
        catch { }
        return false;
    }

    public static bool IsLink(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static (List<(string Name, string Source)> Items, List<string> Skipped) ExtractAddonFolders(string zipPath, string staging)
    {
        var stagingFull = Path.GetFullPath(staging).TrimEnd('\\') + '\\';

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                var rel = entry.FullName.Replace('\\', '/');
                if (rel.Length == 0) continue;
                if (rel.Contains(':'))
                    throw new UpdaterException($"Refusing to extract a suspicious path from the zip: {entry.FullName}");

                var dest = Path.GetFullPath(Path.Combine(stagingFull, rel));
                if (!dest.StartsWith(stagingFull, StringComparison.OrdinalIgnoreCase))
                    throw new UpdaterException($"Refusing to extract a path outside the target folder: {entry.FullName}");

                if (rel.EndsWith('/')) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                entry.ExtractToFile(dest, overwrite: true);
            }
        }

        // Normal release zips hold addon folders at the top. Some wrap them in one extra folder
        // (e.g. "Repo-1.2/"); step into it when the top level has no addon folders.
        var root = staging;
        for (var depth = 0; depth < 2; depth++)
        {
            var dirs = Directory.GetDirectories(root);
            if (dirs.Any(HasToc) || HasToc(root)) break;
            if (dirs.Length == 1) root = dirs[0]; else break;
        }

        var items = new List<(string, string)>();
        var skipped = new List<string>();

        if (HasToc(root) && root != staging)
        {
            // The .toc files sit directly in the wrapper folder: it *is* the addon. Name it after its .toc.
            var name = Directory.GetFiles(root, "*.toc")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n!.Length)
                .First()!;
            items.Add((name, root));
            return (items, skipped);
        }

        foreach (var dir in Directory.GetDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (HasToc(dir)) items.Add((name, dir)); else skipped.Add(name);
        }
        return (items, skipped);
    }

    private static bool HasToc(string dir) => Directory.EnumerateFiles(dir, "*.toc").Any();

    private static List<string> Swap(string addOns, List<(string Name, string Source)> items, string backup, List<string> previousFolders)
    {
        var notes = new List<string>();
        var newNames = items.Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Folders from the last install that this version no longer ships (removed modules).
        var obsolete = previousFolders.Where(p => !newNames.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var name in newNames.Concat(obsolete))
        {
            if (!IsSafeFolderName(name)) throw new UpdaterException($"Unsafe folder name '{name}' in the zip.");
        }
        foreach (var name in newNames)
        {
            var target = Path.Combine(addOns, name);
            if (Directory.Exists(target) && IsLink(target))
                throw new UpdaterException(
                    $"AddOns\\{name} is a link (junction) to another folder, probably your own dev copy. " +
                    "It was left alone; remove the link or this addon from the list.");
        }

        var movedOut = new List<string>();
        var movedIn = new List<string>();
        try
        {
            foreach (var name in newNames.Concat(obsolete))
            {
                var target = Path.Combine(addOns, name);
                if (!Directory.Exists(target)) continue;
                if (IsLink(target)) { notes.Add($"left AddOns\\{name} alone (it's a link)"); continue; }
                Directory.Move(target, Path.Combine(backup, name));
                movedOut.Add(name);
            }

            foreach (var (name, source) in items)
            {
                Directory.Move(source, Path.Combine(addOns, name));
                movedIn.Add(name);
            }

            var removed = obsolete.Where(movedOut.Contains).ToList();
            if (removed.Count > 0) notes.Add($"removed folders the new version no longer has: {string.Join(", ", removed)}");
        }
        catch (Exception ex)
        {
            try
            {
                foreach (var name in movedIn) Directory.Delete(Path.Combine(addOns, name), recursive: true);
                foreach (var name in movedOut) Directory.Move(Path.Combine(backup, name), Path.Combine(addOns, name));
            }
            catch (Exception rex)
            {
                throw new RollbackFailedException(
                    $"Install failed ({ex.Message}) and putting the old files back also failed ({rex.Message}). " +
                    $"Your old files are in {backup}.");
            }
            throw new UpdaterException($"Install failed, old version kept: {ex.Message} " +
                                       "(If WoW or another program has files open in that folder, close it and try again.)");
        }
        return notes;
    }

    private static bool IsSafeFolderName(string name) =>
        name.Length > 0 && name != "." && name != ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// A temporary job folder next to AddOns holding an extracted zip ("new") and, during the
    /// swap, the old version ("old"). Disposing deletes it, unless a failed rollback left the
    /// user's old files in it.
    /// </summary>
    public sealed class StagedInstall : IDisposable
    {
        private readonly Action<string> _log;
        private readonly string _workRoot;

        public string AddOns { get; }
        public string Job { get; }
        public string Staging => Path.Combine(Job, "new");
        public string Backup => Path.Combine(Job, "old");
        internal bool KeepJob { get; set; }
        internal List<(string Name, string Source)> Items { get; set; } = new();

        public IReadOnlyList<string> Folders => Items.Select(i => i.Name).ToList();
        public string? Version { get; internal set; }
        public string? Title { get; internal set; }

        private StagedInstall(string addOns, string workRoot, string job, Action<string> log)
        {
            AddOns = addOns;
            _workRoot = workRoot;
            Job = job;
            _log = log;
        }

        internal static StagedInstall Create(string addOnsPath, Action<string> log)
        {
            var addOns = Path.GetFullPath(addOnsPath);
            if (!Directory.Exists(addOns)) throw new UpdaterException($"AddOns folder not found: {addOns}");
            var parent = Directory.GetParent(addOns)?.FullName
                ?? throw new UpdaterException("The AddOns folder can't be a drive root.");

            var workRoot = Path.Combine(parent, WorkFolderName);
            var job = Path.Combine(workRoot, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}");
            var s = new StagedInstall(addOns, workRoot, job, log);
            Directory.CreateDirectory(s.Staging);
            Directory.CreateDirectory(s.Backup);
            return s;
        }

        public void Dispose()
        {
            if (KeepJob) return;
            try { if (Directory.Exists(Job)) Directory.Delete(Job, recursive: true); }
            catch (Exception ex) { _log($"couldn't delete temp folder {Job}: {ex.Message}"); }
            try { if (!Directory.EnumerateFileSystemEntries(_workRoot).Any()) Directory.Delete(_workRoot); } catch { }
        }
    }
}
