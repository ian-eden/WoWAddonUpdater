using System.Diagnostics;

namespace WoWAddonUpdater;

public sealed class MainForm : Form
{
    private readonly ConfigStore _store = new();
    private readonly AppConfig _cfg;
    private bool _busy;

    private readonly TextBox _urlBox = new()
    {
        Dock = DockStyle.Fill,
        PlaceholderText = "Paste a GitHub link, e.g. https://github.com/Xuerian/XLoot, and press Enter",
    };
    private readonly Button _addBtn = MakeButton("Add");
    private readonly ListView _list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = true,
        HideSelection = false,
    };
    private readonly Button _checkBtn = MakeButton("Check for updates");
    private readonly Button _updateAllBtn = MakeButton("Update all");
    private readonly Button _updateSelBtn = MakeButton("Update selected");
    private readonly Button _removeBtn = MakeButton("Remove");
    private readonly Button _findBtn = MakeButton("Find installed addons…");
    private readonly Button _settingsBtn = MakeButton("Settings…");
    private readonly Label _pathLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 6, 3, 6) };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 14, Visible = false };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f),
    };

    // App self-update bar (hidden until a newer release is found).
    private readonly FlowLayoutPanel _appUpdateBar = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        Visible = false,
        BackColor = Color.FromArgb(255, 243, 205),
        Padding = new Padding(6, 4, 6, 4),
        Margin = new Padding(3, 0, 3, 8),
    };
    private readonly Label _appUpdateLabel = new() { AutoSize = true, Margin = new Padding(3, 7, 6, 3), ForeColor = Color.Black };
    private readonly LinkLabel _appUpdateNotes = new() { Text = "What's new", AutoSize = true, Margin = new Padding(3, 7, 12, 3) };
    private readonly Button _appUpdateBtn = MakeButton("Update now");
    private AppRelease? _appRelease;

    public MainForm()
    {
        _cfg = _store.Load();

        Text = $"WoW Addon Updater {AppInfo.VersionText}";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(980, 620);
        MinimumSize = new Size(720, 460);

        _list.Columns.Add("Addon", 190);
        _list.Columns.Add("Installed", 160);
        _list.Columns.Add("Latest", 160);
        _list.Columns.Add("Status", 260);
        _list.Columns.Add("Source", 180);

        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(new Label { Text = "Add addon:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        top.Controls.Add(_urlBox, 1, 0);
        top.Controls.Add(_addBtn, 2, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        buttons.Controls.AddRange(new Control[] { _checkBtn, _updateAllBtn, _updateSelBtn, _removeBtn, _findBtn, _settingsBtn });

        _appUpdateBar.Controls.AddRange(new Control[] { _appUpdateLabel, _appUpdateNotes, _appUpdateBtn });
        _appUpdateBtn.BackColor = SystemColors.Control;
        _appUpdateNotes.LinkClicked += (_, _) =>
        {
            if (_appRelease != null) Process.Start(new ProcessStartInfo(_appRelease.PageUrl) { UseShellExecute = true });
        };
        _appUpdateBtn.Click += async (_, _) => await UpdateAppAsync();

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        root.Controls.Add(_appUpdateBar, 0, 0);
        root.Controls.Add(top, 0, 1);
        root.Controls.Add(_list, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        root.Controls.Add(_pathLabel, 0, 4);
        root.Controls.Add(_progress, 0, 5);
        root.Controls.Add(_log, 0, 6);
        Controls.Add(root);

        _list.ContextMenuStrip = BuildContextMenu();

        _addBtn.Click += async (_, _) => await AddFromBoxAsync();
        _urlBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await AddFromBoxAsync();
        };
        _checkBtn.Click += async (_, _) => await RunAsync(() => CheckAsync(_cfg.Addons.ToList()));
        _updateAllBtn.Click += async (_, _) => await RunAsync(() => UpdateAsync(_cfg.Addons.ToList(), reinstall: false));
        _updateSelBtn.Click += async (_, _) => await RunAsync(() => UpdateAsync(SelectedAddons(), reinstall: true));
        _removeBtn.Click += (_, _) => RemoveSelected();
        _settingsBtn.Click += (_, _) => OpenSettings();
        _findBtn.Click += async (_, _) => await FindAddonsAsync(automatic: false);
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => OpenOnGitHub();
        Shown += async (_, _) => await OnShownAsync();
    }

    // ---- startup -------------------------------------------------------------

    private async Task OnShownAsync()
    {
        if (_store.LoadError != null) Log(_store.LoadError);

        AppUpdater.CleanUpInBackground();
        _ = CheckAppUpdateAsync(); // runs alongside the addon check

        if (!Directory.Exists(_cfg.AddOnsPath))
        {
            var found = ConfigStore.DetectAddOnsPath(_cfg.Flavor);
            if (found != null)
            {
                _cfg.AddOnsPath = found;
                Directory.CreateDirectory(found);
                Save();
                Log($"Found your AddOns folder: {found}");
            }
        }

        RefreshList();
        UpdateButtons();

        if (!Directory.Exists(_cfg.AddOnsPath))
        {
            MessageBox.Show(this, "Couldn't find your AddOns folder automatically. Please choose it in Settings.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenSettings();
            return;
        }

        if (_cfg.Addons.Count == 0)
            await FindAddonsAsync(automatic: true); // first run: offer what's already installed
        else
            await RunAsync(() => CheckAsync(_cfg.Addons.ToList()));
    }

    // ---- app self-update -----------------------------------------------------

    private async Task CheckAppUpdateAsync()
    {
        try
        {
            _appRelease = await AppUpdater.CheckAsync(_cfg.GitHubToken, CancellationToken.None);
            if (_appRelease == null) return;

            _appUpdateLabel.Text = $"A new version of WoW Addon Updater is available: {_appRelease.Version.Major}." +
                                   $"{_appRelease.Version.Minor}.{_appRelease.Version.Build} (you have {AppInfo.VersionText}).";
            _appUpdateBar.Visible = true;
            Log($"App update available: {_appRelease.Tag}.");
        }
        catch (Exception ex)
        {
            Log($"Couldn't check for app updates: {ex.Message}");
        }
    }

    private async Task UpdateAppAsync()
    {
        if (_appRelease == null) return;
        if (_busy)
        {
            Log("Wait for the current addon check or update to finish, then try again.");
            return;
        }

        _busy = true;
        UpdateButtons();
        _appUpdateBtn.Enabled = false;
        _progress.Value = 0;
        _progress.Visible = true;
        Log($"Downloading WoW Addon Updater {_appRelease.Tag}…");
        try
        {
            Save();
            await AppUpdater.InstallAndRestartAsync(_appRelease, new Progress<int>(p => _progress.Value = Math.Clamp(p, 0, 100)),
                CancellationToken.None);
            Close(); // the new version has been started
        }
        catch (Exception ex)
        {
            Log($"App update failed: {ex.Message}");
            var open = MessageBox.Show(this, $"The update didn't work:\n\n{ex.Message}\n\nOpen the download page instead?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (open == DialogResult.Yes) Process.Start(new ProcessStartInfo(AppInfo.ReleasesUrl) { UseShellExecute = true });
            _appUpdateBtn.Enabled = true;
            _busy = false;
            _progress.Visible = false;
            UpdateButtons();
        }
    }

    // ---- actions -------------------------------------------------------------

    private async Task RunAsync(Func<Task> work)
    {
        if (_busy) return;
        if (!Directory.Exists(_cfg.AddOnsPath))
        {
            Log("Set your AddOns folder in Settings first.");
            return;
        }

        _busy = true;
        UpdateButtons();
        try { await work(); }
        catch (Exception ex) { Log("Error: " + ex.Message); }
        finally
        {
            _busy = false;
            _progress.Visible = false;
            Save();
            UpdateButtons();
        }
    }

    private async Task AddFromBoxAsync()
    {
        var text = _urlBox.Text.Trim();
        if (text.Length == 0) return;

        if (!GitHubClient.TryParseRepo(text, out var owner, out var repo))
        {
            Log(text.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? "Couldn't read that link. Expected https://github.com/owner/repo"
                : "Only GitHub links are supported for now (e.g. https://github.com/Xuerian/XLoot).");
            return;
        }

        var key = $"{owner}/{repo}".ToLowerInvariant();
        var existing = _cfg.Addons.FirstOrDefault(a => a.Key == key);
        if (existing != null)
        {
            Log($"{existing.Name} is already in the list.");
            return;
        }

        var addon = new TrackedAddon { Owner = owner, Repo = repo, Name = repo };
        _cfg.Addons.Add(addon);
        Save();
        _urlBox.Clear();
        RefreshList();
        Log($"Added {owner}/{repo}.");

        await RunAsync(() => CheckAsync(new List<TrackedAddon> { addon }));
    }

    private async Task CheckAsync(List<TrackedAddon> addons)
    {
        if (addons.Count == 0) return;
        Log($"Checking {addons.Count} addon(s)…");

        using var gh = new GitHubClient(_cfg.GitHubToken);
        await Task.WhenAll(addons.Select(a => CheckOneAsync(gh, a)));

        var available = addons.Count(NeedsUpdate);
        Log(available == 0 ? "Everything is up to date." : $"{available} update(s) available.");
    }

    private async Task CheckOneAsync(GitHubClient gh, TrackedAddon a)
    {
        a.Activity = "Checking…";
        UpdateRow(a);
        try
        {
            a.Latest = await gh.FindLatestAsync(a, _cfg.Flavor, CancellationToken.None);
            a.Error = null;
            if (!string.IsNullOrWhiteSpace(a.Latest.DisplayName)) a.Name = a.Latest.DisplayName!;
            LearnInterfaces(a.Latest);
        }
        catch (Exception ex) when (ex is UpdaterException or HttpRequestException or TaskCanceledException)
        {
            a.Latest = null;
            a.Error = ex.Message;
            Log($"{a.Name}: {ex.Message}");
        }
        finally
        {
            a.Activity = null;
            UpdateRow(a);
        }
    }

    private async Task UpdateAsync(List<TrackedAddon> addons, bool reinstall)
    {
        if (addons.Count == 0)
        {
            Log("Nothing selected.");
            return;
        }

        using var gh = new GitHubClient(_cfg.GitHubToken);

        var unchecked_ = addons.Where(a => a.Latest == null).ToList();
        if (unchecked_.Count > 0) await Task.WhenAll(unchecked_.Select(a => CheckOneAsync(gh, a)));

        var todo = addons.Where(a => a.Latest != null && (reinstall || NeedsUpdate(a))).ToList();
        if (todo.Count == 0)
        {
            Log("Nothing to update.");
            return;
        }

        var ok = 0;
        foreach (var a in todo)
        {
            var rel = a.Latest!;
            Log($"{a.Name}: downloading {rel.AssetName} ({rel.Size / 1048576.0:0.0} MB)…");

            _progress.Value = 0;
            _progress.Visible = true;
            a.Activity = "Downloading…";
            UpdateRow(a);
            var progress = new Progress<int>(pct =>
            {
                _progress.Value = Math.Clamp(pct, 0, 100);
                if (a.Activity == null) return; // late report after the install finished
                a.Activity = $"Downloading {pct}%";
                UpdateRow(a);
            });

            try
            {
                var folders = await Installer.InstallAsync(gh, a, rel, _cfg.AddOnsPath, progress,
                    msg => Log($"{a.Name}: {msg}"), CancellationToken.None);

                a.InstalledTag = rel.Tag;
                a.InstalledAsset = rel.AssetName;
                a.InstalledAt = DateTimeOffset.Now;
                a.InstalledFolders = folders;
                a.Error = null;
                ok++;
                Save();
                Log($"{a.Name}: installed {rel.Tag} ({string.Join(", ", folders)})");
            }
            catch (Exception ex)
            {
                a.Error = ex.Message;
                Log($"{a.Name}: {ex.Message}");
            }
            finally
            {
                a.Activity = null;
                UpdateRow(a);
            }
        }

        _progress.Visible = false;
        Log($"Done: {ok} of {todo.Count} updated." + (ok > 0 ? " If WoW is open, type /reload (or restart it) to load them." : ""));
    }

    private async Task FindAddonsAsync(bool automatic)
    {
        if (_busy) return;
        if (!Directory.Exists(_cfg.AddOnsPath))
        {
            Log("Set your AddOns folder in Settings first.");
            return;
        }

        List<Suggestion> found;
        _busy = true;
        UpdateButtons();
        try
        {
            Log("Looking for addons in your AddOns folder…");
            var catalog = await AddonScanner.LoadCatalogAsync(CancellationToken.None);
            found = AddonScanner.Scan(_cfg.AddOnsPath, catalog, _cfg);
        }
        catch (Exception ex)
        {
            Log("Couldn't scan the AddOns folder: " + ex.Message);
            return;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }

        if (found.Count == 0)
        {
            Log(automatic
                ? "Paste a GitHub link above to add your first addon."
                : "No new addons found that this app knows about. You can still paste GitHub links above.");
            return;
        }

        Log($"Found {found.Count} addon(s) that can be updated from GitHub.");
        using var dlg = new FindAddonsForm(found);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var added = new List<TrackedAddon>();
        foreach (var s in dlg.Selected)
        {
            if (_cfg.Addons.Any(a => a.Key == s.Key)) continue;
            var addon = new TrackedAddon { Owner = s.Owner, Repo = s.Repo, Name = s.Name };
            _cfg.Addons.Add(addon);
            added.Add(addon);
        }
        if (added.Count == 0) return;

        Save();
        RefreshList();
        UpdateButtons();
        Log($"Added {string.Join(", ", added.Select(a => a.Name))}. Click \"Update all\" once to install the latest versions.");
        await RunAsync(() => CheckAsync(added));
    }

    private void RemoveSelected()
    {
        var sel = SelectedAddons();
        if (sel.Count == 0) return;

        var names = string.Join(", ", sel.Select(a => a.Name));
        var answer = MessageBox.Show(this,
            $"Stop tracking {names}?\n\nThe addon files stay installed; they just won't be updated any more.",
            Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        foreach (var a in sel) _cfg.Addons.Remove(a);
        Save();
        RefreshList();
        Log($"Removed {names} from the list.");
    }

    private void OpenSettings()
    {
        using var dlg = new SettingsForm(_cfg, _store.FilePath, _store.IsPortable);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var flavorChanged = !string.Equals(dlg.Flavor, _cfg.Flavor, StringComparison.OrdinalIgnoreCase);
        _cfg.AddOnsPath = dlg.AddOnsPath;
        _cfg.Flavor = dlg.Flavor;
        _cfg.GitHubToken = dlg.Token;
        if (flavorChanged)
        {
            foreach (var a in _cfg.Addons) { a.Latest = null; a.Error = null; }
        }
        Save();
        RefreshList();
        UpdateButtons();
        Log("Settings saved.");
    }

    // ---- context menu --------------------------------------------------------

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        var openGitHub = new ToolStripMenuItem("Open on GitHub", null, (_, _) => OpenOnGitHub());
        var openFolder = new ToolStripMenuItem("Open addon folder", null, (_, _) => OpenAddonFolder());
        var prerelease = new ToolStripMenuItem("Include pre-releases (betas)");
        prerelease.Click += async (_, _) =>
        {
            var sel = SelectedAddons();
            var value = !sel.All(a => a.AllowPrerelease);
            foreach (var a in sel) { a.AllowPrerelease = value; a.Latest = null; }
            Save();
            await RunAsync(() => CheckAsync(sel));
        };
        var remove = new ToolStripMenuItem("Remove from list", null, (_, _) => RemoveSelected());

        menu.Items.AddRange(new ToolStripItem[] { openGitHub, openFolder, prerelease, new ToolStripSeparator(), remove });
        menu.Opening += (_, e) =>
        {
            var sel = SelectedAddons();
            if (sel.Count == 0) { e.Cancel = true; return; }
            prerelease.Checked = sel.All(a => a.AllowPrerelease);
            prerelease.Enabled = remove.Enabled = !_busy;
        };
        return menu;
    }

    private void OpenOnGitHub()
    {
        var a = SelectedAddons().FirstOrDefault();
        if (a != null) Process.Start(new ProcessStartInfo(a.Url + "/releases") { UseShellExecute = true });
    }

    private void OpenAddonFolder()
    {
        var a = SelectedAddons().FirstOrDefault();
        if (a == null || !Directory.Exists(_cfg.AddOnsPath)) return;
        var folder = a.InstalledFolders.Select(f => Path.Combine(_cfg.AddOnsPath, f)).FirstOrDefault(Directory.Exists)
                     ?? GuessFolder(a)
                     ?? _cfg.AddOnsPath;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\""));
    }

    // ---- list display --------------------------------------------------------

    private bool NeedsUpdate(TrackedAddon a) =>
        a.Latest != null &&
        (a.InstalledTag != a.Latest.Tag || a.InstalledAsset != a.Latest.AssetName || FoldersMissing(a));

    private bool FoldersMissing(TrackedAddon a) =>
        a.InstalledFolders.Count > 0 && a.InstalledFolders.Any(f => !Directory.Exists(Path.Combine(_cfg.AddOnsPath, f)));

    private string? GuessFolder(TrackedAddon a)
    {
        foreach (var name in new[] { a.Repo, a.Name.Replace(" ", "") })
        {
            var p = Path.Combine(_cfg.AddOnsPath, name);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    private (string Text, Color Color) StatusOf(TrackedAddon a)
    {
        if (a.Activity != null) return (a.Activity, SystemColors.WindowText);
        if (a.Error != null) return ("⚠ " + a.Error, Color.Firebrick);
        if (a.Latest == null) return ("Not checked yet", SystemColors.GrayText);

        var unverified = FlavorConfirmed(a) ? "" : $" (not confirmed for {FlavorDisplayName(_cfg.Flavor)})";
        if (a.InstalledTag == null) return ("Not installed by this app yet – Update to install" + unverified, Color.DarkOrange);
        if (FoldersMissing(a)) return ("Files missing – Update to reinstall", Color.DarkOrange);
        if (NeedsUpdate(a)) return ("Update available" + unverified, Color.DarkOrange);
        return ("Up to date" + unverified, Color.ForestGreen);
    }

    /// <summary>
    /// release.json confirms the flavor directly. Without one, fall back to the installed
    /// files: a flavor-specific .toc or a known interface number in "## Interface:".
    /// </summary>
    private bool FlavorConfirmed(TrackedAddon a)
    {
        if (a.Latest?.FlavorVerified == true) return true;

        var folders = a.InstalledFolders.Count > 0
            ? a.InstalledFolders
            : GuessFolder(a) is { } g ? new List<string> { Path.GetFileName(g) } : new List<string>();
        var interfaces = _cfg.KnownInterfaces.TryGetValue(_cfg.Flavor.ToLowerInvariant(), out var list) ? list : new List<int>();
        return folders.Any(f => Installer.TocSupportsFlavor(_cfg.AddOnsPath, f, _cfg.Flavor, interfaces));
    }

    private void LearnInterfaces(ReleaseCandidate c)
    {
        if (!c.FlavorVerified || c.FlavorInterfaces.Length == 0) return;
        var key = _cfg.Flavor.ToLowerInvariant();
        if (!_cfg.KnownInterfaces.TryGetValue(key, out var list)) _cfg.KnownInterfaces[key] = list = new List<int>();
        foreach (var i in c.FlavorInterfaces)
            if (!list.Contains(i)) list.Add(i);
    }

    private static string FlavorDisplayName(string flavor) => flavor.ToLowerInvariant() switch
    {
        "forever" => "WoW Forever",
        "mainline" => "Retail",
        "classic" => "Classic Era",
        _ => flavor,
    };

    private void RefreshList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var a in _cfg.Addons.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = new ListViewItem(new[] { "", "", "", "", "" }) { Tag = a, Name = a.Key, UseItemStyleForSubItems = false };
            _list.Items.Add(item);
            FillRow(item, a);
        }
        _list.EndUpdate();

        _pathLabel.Text = Directory.Exists(_cfg.AddOnsPath)
            ? $"AddOns folder: {_cfg.AddOnsPath}    ·    Game version: {_cfg.Flavor}"
            : "AddOns folder not set. Open Settings.";
    }

    private void UpdateRow(TrackedAddon a)
    {
        var item = _list.Items[a.Key];
        if (item != null) FillRow(item, a);
    }

    private void FillRow(ListViewItem item, TrackedAddon a)
    {
        string installed;
        if (a.InstalledTag != null)
        {
            installed = a.InstalledTag;
        }
        else
        {
            var folder = GuessFolder(a);
            var toc = folder == null ? null : Installer.ReadTocVersion(_cfg.AddOnsPath, Path.GetFileName(folder));
            installed = toc != null ? $"? (folder says {toc})" : folder != null ? "? (already in AddOns)" : "–";
        }

        var latest = a.Latest == null ? "" : a.Latest.Tag + (a.Latest.Prerelease ? " (beta)" : "");
        var (status, color) = StatusOf(a);

        item.SubItems[0].Text = a.Name;
        item.SubItems[1].Text = installed;
        item.SubItems[2].Text = latest;
        item.SubItems[3].Text = status;
        item.SubItems[3].ForeColor = color;
        item.SubItems[4].Text = $"github.com/{a.Owner}/{a.Repo}";
    }

    private List<TrackedAddon> SelectedAddons() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => (TrackedAddon)i.Tag!).ToList();

    private void UpdateButtons()
    {
        var hasSel = _list.SelectedItems.Count > 0;
        var hasAny = _cfg.Addons.Count > 0;
        _addBtn.Enabled = !_busy;
        _checkBtn.Enabled = !_busy && hasAny;
        _updateAllBtn.Enabled = !_busy && hasAny;
        _updateSelBtn.Enabled = !_busy && hasSel;
        _removeBtn.Enabled = !_busy && hasSel;
        _settingsBtn.Enabled = !_busy;
        _findBtn.Enabled = !_busy;
    }

    private void Save()
    {
        try { _store.Save(_cfg); }
        catch (Exception ex) { Log($"Couldn't save settings to {_store.FilePath}: {ex.Message}"); }
    }

    private void Log(string message) =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private static Button MakeButton(string text) =>
        new() { Text = text, AutoSize = true, Padding = new Padding(6, 2, 6, 2) };
}
