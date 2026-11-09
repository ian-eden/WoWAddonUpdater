namespace WoWAddonUpdater;

public static class AppInfo
{
    /// <summary>GitHub repository that hosts this app's own releases. Empty owner = no update checks.</summary>
    public const string RepoOwner = "ian-eden";
    public const string RepoName = "WoWAddonUpdater";

    /// <summary>The release file this build updates itself from (see build.ps1).</summary>
#if SELF_CONTAINED
    public const string AssetName = "WoWAddonUpdater.exe";
#else
    public const string AssetName = "WoWAddonUpdater-lite.exe";
#endif

    public static Version Version { get; } = Normalize(typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0));

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>The Pyroforce flame icon, for title bars and the taskbar.</summary>
    public static Icon? Icon { get; } = LoadIcon();

    private static Icon? LoadIcon()
    {
        try
        {
            using var s = typeof(AppInfo).Assembly.GetManifestResourceStream("app.ico");
            return s is null ? null : new Icon(s);
        }
        catch { return null; }
    }

    public static string ReleasesUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases";

    /// <summary>Fills missing parts with 0 so 1.2.0 and 1.2.0.0 compare as equal.</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
