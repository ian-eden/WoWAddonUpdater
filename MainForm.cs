using System.Diagnostics;

namespace WoWAddonUpdater;

public sealed class MainForm : Form
{
    private readonly ConfigStore _store = new();
    private readonly AppConfig _cfg;
    private bool _busy;

    // Header
    private readonly Label _subtitle = new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        ForeColor = Theme.HeaderSubText,
        BackColor = Color.Transparent,
        TextAlign = ContentAlignment.TopLeft,
    };
    private readonly Button _settingsBtn = Theme.HeaderFlatButton("Settings");

    // Add row
    private readonly TextBox _urlBox = new()
    {
        BorderStyle = BorderStyle.None,
        PlaceholderText = "Paste a GitHub or CurseForge link, e.g. https://github.com/Xuerian/XLoot, and press Enter",
    };
    private readonly Button _addBtn = Theme.SecondaryButton("Add");

    // List + toolbar
    private readonly ModernListView _list = new() { MultiSelect = true, HideSelection = false, DotColumn = 3 };
    private readonly Button _checkBtn = Theme.SecondaryButton("Check for updates");
    private readonly Button _updateAllBtn = Theme.PrimaryButton("Update all");
    private readonly Button _moreBtn = Theme.SecondaryButton("Add addons  ▾");
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 6, Visible = false, Margin = new Padding(0, 0, 0, 8) };
    private readonly TextBox _log = new()
    {
        Multiline = true,
        ReadOnly = true,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Surface,
        ForeColor = Color.FromArgb(80, 72, 76),
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f),
    };

    // App self-update bar (hidden until a newer release is found).
    private readonly FlowLayoutPanel _appUpdateBar = NoticeBar(Theme.NoticeBack);
    private readonly Label _appUpdateLabel = NoticeLabel();
    private readonly LinkLabel _appUpdateNotes = new()
    {
        Text = "What's new",
        AutoSize = true,
        Margin = new Padding(3, 8, 12, 3),
        LinkColor = Theme.AccentDown,
        ActiveLinkColor = Theme.Accent,
    };
    private readonly Button _appUpdateBtn = Theme.PrimaryButton("Update now");
    private AppRelease? _appRelease;

    // "Waiting for your download" bar for manual-download addons (CurseForge etc.).
    private readonly FlowLayoutPanel _waitBar = NoticeBar(Theme.WaitBack);
    private readonly Label _waitLabel = NoticeLabel();
    private readonly Button _waitChooseBtn = Theme.SecondaryButton("Choose the zip…");
    private readonly Button _waitCancelBtn = Theme.SecondaryButton("Cancel");
    private readonly CheckBox _waitDeleteBox = new()
    {
        Text = "Delete the zip after installing",
        AutoSize = true,
        Margin = new Padding(8, 8, 3, 3),
        ForeColor = Theme.Text,
    };
    private CancellationTokenSource? _waitCts;
    private string? _waitChosen;

    public MainForm()
    {
        _cfg = _store.Load();

        Text = $"WoW Addon Updater {AppInfo.VersionText}";
        if (AppInfo.Icon is not null) Icon = AppInfo.Icon;
        Font = new Font("Segoe UI", 9.75f);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(LogicalToDeviceUnits(1000), LogicalToDeviceUnits(680));
        MinimumSize = new Size(LogicalToDeviceUnits(760), LogicalToDeviceUnits(500));

        // ---- header: icon, title, folder, Settings
        var header = new HeaderPanel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(72), Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(12), LogicalToDeviceUnits(16), LogicalToDeviceUnits(14)) };
        var headerGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.Transparent };
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        headerGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        var iconSize = LogicalToDeviceUnits(42);
        var logo = new PictureBox
        {
            Size = new Size(iconSize, iconSize),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0),
            Image = AppInfo.Icon is null ? null : new Icon(AppInfo.Icon, iconSize, iconSize).ToBitmap(),
        };
        var title = new Label
        {
            Text = "WoW Addon Updater",
            AutoSize = true,
            ForeColor = Theme.HeaderText,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 14f),
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left | AnchorStyles.Bottom,
        };
        _subtitle.Margin = new Padding(2, 2, 0, 0);
        _settingsBtn.Margin = new Padding(0);
        var findMoreBtn = Theme.HeaderFlatButton("Find more addons");
        findMoreBtn.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
        findMoreBtn.Click += (_, _) => OpenUrl("https://www.curseforge.com/wow");
        new ToolTip().SetToolTip(findMoreBtn, "Browse WoW addons on CurseForge. Copy an addon's link and paste it into the box below to add it.");
        var headerButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
        };
        headerButtons.Controls.AddRange(new Control[] { findMoreBtn, _settingsBtn });
        headerGrid.Controls.Add(logo, 0, 0);
        headerGrid.SetRowSpan(logo, 2);
        headerGrid.Controls.Add(title, 1, 0);
        headerGrid.Controls.Add(_subtitle, 1, 1);
        headerGrid.Controls.Add(headerButtons, 2, 0);
        headerGrid.SetRowSpan(headerButtons, 2);
        header.Controls.Add(headerGrid);

        // ---- add row
        var add = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
        add.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        add.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var urlCard = Theme.Card(_urlBox, new Padding(LogicalToDeviceUnits(10), LogicalToDeviceUnits(8), LogicalToDeviceUnits(10), LogicalToDeviceUnits(4)));
        var rowHeight = LogicalToDeviceUnits(34);
        urlCard.Dock = DockStyle.None;
        urlCard.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        urlCard.Height = rowHeight;
        urlCard.Margin = new Padding(0, 0, 8, 0);
        urlCard.Click += (_, _) => _urlBox.Focus();
        _addBtn.AutoSize = false;
        _addBtn.Size = new Size(LogicalToDeviceUnits(80), rowHeight);
        _addBtn.Margin = new Padding(0);
        add.Controls.Add(urlCard, 0, 0);
        add.Controls.Add(_addBtn, 1, 0);

        // ---- list
        _list.Columns.Add("Addon", LogicalToDeviceUnits(170));
        _list.Columns.Add("Installed", LogicalToDeviceUnits(115));
        _list.Columns.Add("Latest", LogicalToDeviceUnits(115));
        _list.Columns.Add("Status", LogicalToDeviceUnits(410));
        _list.Columns.Add("Source", LogicalToDeviceUnits(180));
        _list.SetRowHeight(30);
        var listCard = Theme.Card(_list, new Padding(1));

        // ---- toolbar: the two everyday buttons, an "Add addons" menu, and a hint
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 10) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.AddRange(new Control[] { _updateAllBtn, _checkBtn, _moreBtn });
        var hint = new Label
        {
            Text = "Right-click an addon for more options",
            AutoSize = true,
            ForeColor = Theme.Muted,
            Anchor = AnchorStyles.Right,
        };
        toolbar.Controls.Add(buttons, 0, 0);
        toolbar.Controls.Add(hint, 1, 0);

        var moreMenu = new ContextMenuStrip { Renderer = Theme.MenuRenderer, Font = Font };
        moreMenu.Items.Add("Find installed addons…", null, async (_, _) => await FindAddonsAsync(automatic: false));
        moreMenu.Items.Add("Install from a zip file…", null, async (_, _) =>
        {
            var zip = ChooseZip(ManualPages.DownloadsFolder());
            if (zip != null) await RunAsync(() => InstallZipAsync(zip, null, deleteAfter: false));
        });
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add(new ToolStripMenuItem("Tip: paste a link in the box above, or drop a zip on the window") { Enabled = false });
        _moreBtn.Click += (_, _) => moreMenu.Show(_moreBtn, new Point(0, _moreBtn.Height));

        // ---- notice bars
        _waitBar.Controls.AddRange(new Control[] { _waitLabel, _waitChooseBtn, _waitCancelBtn, _waitDeleteBox });
        _waitChooseBtn.Click += (_, _) =>
        {
            var zip = ChooseZip(ManualPages.DownloadsFolder());
            if (zip == null) return;
            _waitChosen = zip;
            _waitCts?.Cancel();
        };
        _waitCancelBtn.Click += (_, _) => _waitCts?.Cancel();
        _waitDeleteBox.CheckedChanged += (_, _) =>
        {
            if (_cfg.DeleteDownloadedZips == _waitDeleteBox.Checked) return;
            _cfg.DeleteDownloadedZips = _waitDeleteBox.Checked;
            Save();
        };

        _appUpdateBar.Controls.AddRange(new Control[] { _appUpdateLabel, _appUpdateNotes, _appUpdateBtn });
        _appUpdateNotes.LinkClicked += (_, _) =>
        {
            if (_appRelease != null) OpenUrl(_appRelease.PageUrl);
        };
        _appUpdateBtn.Click += async (_, _) => await UpdateAppAsync();

        // ---- log
        var logLabel = new Label { Text = "Activity", AutoSize = true, ForeColor = Theme.Muted, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, 4) };
        var logCard = Theme.Card(_log, new Padding(LogicalToDeviceUnits(8), LogicalToDeviceUnits(6), 2, 2));
        logCard.Margin = new Padding(0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(LogicalToDeviceUnits(16), LogicalToDeviceUnits(14), LogicalToDeviceUnits(16), LogicalToDeviceUnits(16)),
            BackColor = Theme.Window,
        };
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // app update bar
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // waiting bar
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // add row
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));      // list
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // toolbar
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // progress
        body.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // "Activity"
        body.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(120))); // log
        body.Controls.Add(_appUpdateBar, 0, 0);
        body.Controls.Add(_waitBar, 0, 1);
        body.Controls.Add(add, 0, 2);
        body.Controls.Add(listCard, 0, 3);
        body.Controls.Add(toolbar, 0, 4);
        body.Controls.Add(_progress, 0, 5);
        body.Controls.Add(logLabel, 0, 6);
        body.Controls.Add(logCard, 0, 7);

        Controls.Add(body);
        Controls.Add(header);

        // Drop a downloaded zip anywhere on the window to install it.
        AllowDrop = _list.AllowDrop = _log.AllowDrop = true;
        foreach (Control c in new Control[] { this, _list, _log })
        {
            c.DragEnter += (_, e) => e.Effect = !_busy && DroppedZip(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
            c.DragDrop += async (_, e) =>
            {
                var zip = DroppedZip(e);
                if (zip != null) await RunAsync(() => InstallZipAsync(zip, null, deleteAfter: false));
            };
        }
        FormClosing += (_, _) => _waitCts?.Cancel();

        _list.ContextMenuStrip = BuildContextMenu();

        _addBtn.Click += async (_, _) => await AddFromBoxAsync();
        _urlBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await AddFromBoxAsync();
        };
        _checkBtn.Click += async (_, _) => await CheckEverythingAsync(userAsked: true);
        _autoTimer.Tick += async (_, _) => await AutoCheckAsync();
        FormClosed += (_, _) => _autoTimer.Dispose();
        _updateAllBtn.Click += async (_, _) => await RunAsync(() => UpdateAsync(_cfg.Addons.ToList(), reinstall: false));
        _settingsBtn.Click += (_, _) => OpenSettings();
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();
        _list.DoubleClick += (_, _) => OpenAddonPage();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !_busy) RemoveSelected();
        };
        Shown += async (_, _) => await OnShownAsync();
    }

    private static FlowLayoutPanel NoticeBar(Color back) => new()
    {
        Dock = DockStyle.Fill,
        AutoSize = true,
        Visible = false,
        BackColor = back,
        Padding = new Padding(8, 5, 8, 5),
        Margin = new Padding(0, 0, 0, 10),
    };

    private static Label NoticeLabel() => new() { AutoSize = true, Margin = new Padding(3, 8, 8, 3), ForeColor = Theme.Text };

    // ---- startup -------------------------------------------------------------

    private async Task OnShownAsync()
    {
        if (_store.LoadError != null) Log(_store.LoadError);

        AppUpdater.CleanUpInBackground();
        _lastCheck = DateTime.Now;
        _autoTimer.Start();
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

    private async Task CheckAppUpdateAsync(bool reportUpToDate = false)
    {
        try
        {
            var found = await AppUpdater.CheckAsync(_cfg.GitHubToken, CancellationToken.None);
            if (found == null)
            {
                if (reportUpToDate && _appRelease == null) Log($"WoW Addon Updater {AppInfo.VersionText} is the latest version.");
                return;
            }

            var isNew = _appRelease?.Tag != found.Tag;
            _appRelease = found;
            _appUpdateLabel.Text = $"A new version of WoW Addon Updater is available: {found.Version.Major}." +
                                   $"{found.Version.Minor}.{found.Version.Build} (you have {AppInfo.VersionText}).";
            _appUpdateBar.Visible = true;
            if (isNew) Log($"App update available: {found.Tag}.");
            RefreshTitle();
        }
        catch (Exception ex)
        {
            Log($"Couldn't check for app updates: {ex.Message}");
        }
    }

    // ---- checking while the app stays open -----------------------------------

    /// <summary>How often to re-check while the window stays open (addon authors push fixes for days after a patch).</summary>
    private static readonly TimeSpan AutoCheckEvery = TimeSpan.FromHours(3);
    private readonly System.Windows.Forms.Timer _autoTimer = new() { Interval = 60_000 };
    private DateTime _lastCheck = DateTime.MinValue;

    /// <summary>
    /// Runs every minute; checks once it has been <see cref="AutoCheckEvery"/> since the last check.
    /// Comparing times (rather than one long timer) also catches up after the PC wakes from sleep.
    /// Only checks; nothing is installed without the user clicking.
    /// </summary>
    private async Task AutoCheckAsync()
    {
        if (_busy || OwnedForms.Length > 0 || DateTime.Now - _lastCheck < AutoCheckEvery) return;
        if (!Directory.Exists(_cfg.AddOnsPath)) return;
        Log($"Automatic check (every {AutoCheckEvery.TotalHours:0} hours while the app is open)…");
        await CheckEverythingAsync(userAsked: false);
    }

    /// <summary>Addons and the app itself.</summary>
    private async Task CheckEverythingAsync(bool userAsked)
    {
        _lastCheck = DateTime.Now;
        var app = CheckAppUpdateAsync(reportUpToDate: userAsked);
        await RunAsync(() => CheckAsync(_cfg.Addons.ToList()));
        await app;
    }

    /// <summary>Shows pending updates in the title, so they're visible on the taskbar without any pop-ups.</summary>
    private void RefreshTitle()
    {
        var n = _cfg.Addons.Count(NeedsUpdate);
        var text = $"WoW Addon Updater {AppInfo.VersionText}";
        if (n > 0) text += $" – {n} update{(n == 1 ? "" : "s")} available";
        if (_appRelease != null) text += " – new app version ready";
        if (Text != text) Text = text;
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
            RefreshTitle();
        }
    }

    private async Task AddFromBoxAsync()
    {
        var text = _urlBox.Text.Trim();
        if (text.Length == 0) return;

        if (!GitHubClient.TryParseRepo(text, out var owner, out var repo))
        {
            if (!text.Contains("github.com", StringComparison.OrdinalIgnoreCase) &&
                ManualPages.TryParse(text, out var page, out var pageName))
            {
                AddManual(page, pageName);
                return;
            }
            Log(text.Contains("github.com", StringComparison.OrdinalIgnoreCase)
                ? "Couldn't read that link. Expected https://github.com/owner/repo"
                : "That doesn't look like a link. Paste a GitHub link (https://github.com/owner/repo) " +
                  "or an addon's page, e.g. https://www.curseforge.com/wow/addons/dungeonjournal");
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

    private void AddManual(string pageUrl, string name)
    {
        var addon = new TrackedAddon { Source = "manual", PageUrl = pageUrl, Name = name };
        var existing = _cfg.Addons.FirstOrDefault(a => a.Key == addon.Key);
        if (existing != null)
        {
            Log($"{existing.Name} is already in the list.");
            return;
        }

        _cfg.Addons.Add(addon);
        Save();
        _urlBox.Clear();
        RefreshList();
        UpdateButtons();
        Log($"Added {name} ({ManualPages.ShortText(pageUrl)}). This app can't check that site for new versions, " +
            "so it's a manual download: right-click it and choose \"Check page for updates / download\". Its page opens; click Download " +
            "there and this app installs the zip as soon as it arrives in your Downloads folder.");
    }

    private async Task CheckAsync(List<TrackedAddon> addons)
    {
        addons = addons.Where(a => !a.IsManual).ToList(); // nothing to check for manual downloads
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

        var manual = addons.Where(a => a.IsManual).ToList();
        var fromGitHub = addons.Where(a => !a.IsManual).ToList();
        if (fromGitHub.Count > 0) await UpdateFromGitHubAsync(fromGitHub, reinstall);
        if (manual.Count == 0) return;

        if (!reinstall)
        {
            // "Update all" would open a browser tab per addon; keep manual downloads one at a time.
            Log($"Skipped {string.Join(", ", manual.Select(a => a.Name))}: manual download. " +
                "Right-click it and choose \"Check page for updates / download\" when you want to.");
            return;
        }

        foreach (var a in manual)
        {
            if (!await UpdateManualAsync(a)) break; // cancelled: don't open the next page
        }
    }

    private async Task UpdateFromGitHubAsync(List<TrackedAddon> addons, bool reinstall)
    {
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

    // ---- manual downloads (CurseForge etc.) ------------------------------------

    /// <summary>Opens the addon's page and installs the zip once it lands in Downloads. False if cancelled.</summary>
    private async Task<bool> UpdateManualAsync(TrackedAddon a)
    {
        string? zip;
        var fromDownloads = false;

        if (string.IsNullOrEmpty(a.PageUrl))
        {
            Log($"{a.Name}: no download page saved for it. Choose the zip you downloaded.");
            zip = ChooseZip(ManualPages.DownloadsFolder());
        }
        else
        {
            var watcher = new ManualPages.DownloadWatcher(ManualPages.DownloadsFolder()); // snapshot before the browser opens
            OpenUrl(a.PageUrl);
            Log($"{a.Name}: opened {a.PageUrl}. Click Download on the page; the zip is installed as soon as it " +
                $"arrives in {watcher.Folder}.");
            a.Activity = "Waiting for your download…";
            UpdateRow(a);
            try { (zip, fromDownloads) = await WaitForDownloadAsync(a, watcher); }
            finally
            {
                a.Activity = null;
                UpdateRow(a);
            }
        }

        if (zip == null)
        {
            Log($"{a.Name}: cancelled.");
            return false;
        }
        if (fromDownloads) Log($"{a.Name}: found {Path.GetFileName(zip)} in Downloads.");
        await InstallZipAsync(zip, a, deleteAfter: fromDownloads && _cfg.DeleteDownloadedZips);
        return true;
    }

    private async Task<(string? Zip, bool FromDownloads)> WaitForDownloadAsync(TrackedAddon a, ManualPages.DownloadWatcher watcher)
    {
        using var cts = new CancellationTokenSource();
        _waitCts = cts;
        _waitChosen = null;
        _waitLabel.Text = $"Waiting for {a.Name}: click Download on the page that just opened in your browser.";
        _waitDeleteBox.Checked = _cfg.DeleteDownloadedZips;
        _waitBar.Visible = true;
        try
        {
            while (true)
            {
                await Task.Delay(1000, cts.Token);
                var found = await Task.Run(watcher.Poll);
                if (found != null) return (found, true);
            }
        }
        catch (OperationCanceledException)
        {
            return (_waitChosen, false); // null = Cancel was clicked
        }
        finally
        {
            _waitBar.Visible = false;
            _waitCts = null;
        }
    }

    /// <summary>
    /// Installs a zip the user downloaded. <paramref name="target"/> is the addon it's for, or null
    /// to work it out from the zip's folders (Install from zip… / drag and drop).
    /// </summary>
    private async Task InstallZipAsync(string zipPath, TrackedAddon? target, bool deleteAfter)
    {
        var fileName = Path.GetFileName(zipPath);
        var label = target?.Name ?? fileName;
        void L(string m) => Log($"{label}: {m}");

        if (target != null) { target.Activity = "Installing…"; UpdateRow(target); }
        try
        {
            using var staged = await Installer.StageZipAsync(zipPath, _cfg.AddOnsPath, L, CancellationToken.None);
            var folders = staged.Folders;
            var what = (staged.Title ?? folders[0]) + (staged.Version != null ? " " + staged.Version : "");
            var folderText = string.Join(", ", folders);

            if (target == null)
            {
                var match = _cfg.Addons.FirstOrDefault(x => x.InstalledFolders.Intersect(folders, StringComparer.OrdinalIgnoreCase).Any());
                if (match is { IsManual: false })
                {
                    Log($"{fileName} is {match.Name}, which this app updates from GitHub. Right-click it and choose \"Update\" instead.");
                    return;
                }
                if (match != null)
                {
                    target = match;
                }
                else
                {
                    var answer = AskWhichAddon(what, folderText, staged.Title ?? folders[0]);
                    if (answer == null) { Log("Cancelled."); return; }
                    target = answer;
                }
                target.Activity = "Installing…";
            }
            else if (target.InstalledFolders.Count > 0 &&
                     !target.InstalledFolders.Intersect(folders, StringComparer.OrdinalIgnoreCase).Any())
            {
                var ok = MessageBox.Show(this,
                    $"{fileName} contains {what} (folders: {folderText}), which doesn't look like {target.Name} " +
                    $"(folders: {string.Join(", ", target.InstalledFolders)}).\n\nInstall it as {target.Name} anyway?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (ok != DialogResult.Yes) { Log($"{target.Name}: not installed."); return; }
            }
            else if (target.InstalledFolders.Count == 0)
            {
                var ok = MessageBox.Show(this,
                    $"Install {what} as {target.Name}?\n\nFrom: {fileName}\nFolders: {folderText}\n\n" +
                    "Folders with the same names in AddOns are replaced (your settings are kept).",
                    Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (ok != DialogResult.OK) { Log($"{target.Name}: not installed."); return; }
            }

            var installed = await Installer.CommitAsync(staged, target.InstalledFolders, L, CancellationToken.None);

            if (staged.Version != null && staged.Version == target.InstalledTag)
                Log($"{target.Name}: that was the same version you already had ({staged.Version}).");
            target.InstalledTag = staged.Version ?? Path.GetFileNameWithoutExtension(zipPath);
            target.InstalledAsset = fileName;
            target.InstalledAt = DateTimeOffset.Now;
            target.InstalledFolders = installed;
            target.Error = null;
            if (staged.Title != null) target.Name = staged.Title;
            if (!_cfg.Addons.Contains(target)) _cfg.Addons.Add(target);
            Save();
            Log($"{target.Name}: installed {target.InstalledTag} ({string.Join(", ", installed)}). " +
                "If WoW is open, type /reload (or restart it) to load it.");

            if (deleteAfter)
            {
                try
                {
                    File.Delete(zipPath);
                    Log($"{target.Name}: deleted {fileName} from Downloads.");
                }
                catch (Exception ex) { Log($"{target.Name}: couldn't delete {fileName}: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            if (target != null) target.Error = ex.Message;
            Log($"{label}: {ex.Message}");
        }
        finally
        {
            if (target != null) target.Activity = null;
            RefreshList();
            UpdateButtons();
        }
    }

    /// <summary>
    /// For a zip that doesn't match anything installed by this app: which list entry it belongs to
    /// (a manual addon not installed yet), or a new entry with an optional download page. Null = cancel.
    /// </summary>
    private TrackedAddon? AskWhichAddon(string what, string folderText, string name)
    {
        var pending = _cfg.Addons.Where(a => a.IsManual && a.InstalledFolders.Count == 0).ToList();
        const string NewEntry = "A new addon (not in the list yet)";

        using var dlg = new Form
        {
            Text = "Install from zip",
            Font = Font,
            Icon = Icon,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            StartPosition = FormStartPosition.CenterParent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label { Text = $"Install {what}?\nFolders: {folderText}", AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(3, 3, 3, 10) });

        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
        if (pending.Count > 0)
        {
            layout.Controls.Add(new Label { Text = "Which addon in your list is this?", AutoSize = true });
            foreach (var p in pending) combo.Items.Add(p.Name);
            combo.Items.Add(NewEntry);
            combo.SelectedIndex = pending.Count == 1 ? 0 : combo.Items.Count - 1;
            layout.Controls.Add(combo);
        }

        var pageLabel = new Label
        {
            Text = "Where do you download it? Paste its page link (e.g. its CurseForge page) so the app can open it " +
                   "next time. You can leave this empty.",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Margin = new Padding(3, 10, 3, 3),
        };
        var pageBox = new TextBox { Width = 460, PlaceholderText = "https://www.curseforge.com/wow/addons/…" };
        layout.Controls.Add(pageLabel);
        layout.Controls.Add(pageBox);
        void SyncPageBox() => pageLabel.Enabled = pageBox.Enabled = pending.Count == 0 || combo.SelectedIndex == combo.Items.Count - 1;
        combo.SelectedIndexChanged += (_, _) => SyncPageBox();
        SyncPageBox();

        var ok = new Button { Text = "Install", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons);
        dlg.Controls.Add(layout);
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;

        string? page = null;
        dlg.FormClosing += (_, e) =>
        {
            if (dlg.DialogResult != DialogResult.OK || !pageBox.Enabled) return;
            var text = pageBox.Text.Trim();
            if (text.Length == 0) { page = null; return; }
            if (ManualPages.TryParse(text, out var parsed, out var _unusedName)) { page = parsed; return; }
            MessageBox.Show(dlg, "That doesn't look like a web page link. Leave it empty or paste the full link (https://…).",
                dlg.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return null;

        if (pending.Count > 0 && combo.SelectedIndex < pending.Count) return pending[combo.SelectedIndex];

        var addon = new TrackedAddon { Source = "manual", PageUrl = page, Name = name };
        var existing = page == null ? null : _cfg.Addons.FirstOrDefault(a => a.Key == addon.Key);
        return existing ?? addon;
    }

    private string? ChooseZip(string startFolder)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Choose the addon zip you downloaded",
            Filter = "Zip files (*.zip)|*.zip",
            InitialDirectory = Directory.Exists(startFolder) ? startFolder : "",
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    private static string? DroppedZip(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
        files[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(files[0])
            ? files[0]
            : null;

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

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
                ? "Paste a GitHub or CurseForge link above to add your first addon."
                : "No new addons found that this app knows about. You can still paste GitHub or CurseForge links above.");
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
        var menu = new ContextMenuStrip { Renderer = Theme.MenuRenderer, Font = Font };
        var update = new ToolStripMenuItem("Update", null, async (_, _) =>
            await RunAsync(() => UpdateAsync(SelectedAddons(), reinstall: true)));
        update.Font = new Font(Font, FontStyle.Bold);
        var openGitHub = new ToolStripMenuItem("Open on GitHub", null, (_, _) => OpenAddonPage());
        var openFolder = new ToolStripMenuItem("Open addon folder", null, (_, _) => OpenAddonFolder());
        var prerelease = new ToolStripMenuItem("Include pre-releases (betas)");
        prerelease.Click += async (_, _) =>
        {
            var sel = SelectedAddons();
            var value = !sel.All(a => a.AllowPrerelease);
            sel = sel.Where(a => !a.IsManual).ToList();
            foreach (var a in sel) { a.AllowPrerelease = value; a.Latest = null; }
            Save();
            await RunAsync(() => CheckAsync(sel));
        };
        var remove = new ToolStripMenuItem("Remove from list", null, (_, _) => RemoveSelected());

        menu.Items.AddRange(new ToolStripItem[] { update, new ToolStripSeparator(), openGitHub, openFolder, prerelease, new ToolStripSeparator(), remove });
        menu.Opening += (_, e) =>
        {
            var sel = SelectedAddons();
            if (sel.Count == 0) { e.Cancel = true; return; }
            var first = sel[0];
            update.Text = sel.All(a => a.IsManual)
                ? (sel.Count == 1 && string.IsNullOrEmpty(first.PageUrl) ? "Install from a zip file…" : "Check page for updates / download…")
                : sel.All(a => !a.IsManual && a.Latest != null && !NeedsUpdate(a)) ? "Reinstall" : "Update";
            update.Enabled = !_busy;
            openGitHub.Text = first.IsManual ? "Open download page" : "Open on GitHub";
            openGitHub.Enabled = !string.IsNullOrEmpty(first.Url);
            prerelease.Visible = sel.Any(a => !a.IsManual);
            prerelease.Checked = sel.Where(a => !a.IsManual).All(a => a.AllowPrerelease);
            prerelease.Enabled = remove.Enabled = !_busy;
        };
        return menu;
    }

    private void OpenAddonPage()
    {
        var a = SelectedAddons().FirstOrDefault();
        if (a?.Url is not { Length: > 0 } url) return;
        OpenUrl(a.IsManual ? url : url + "/releases");
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
            if (string.IsNullOrWhiteSpace(name)) continue; // manual addons have no repo name
            var p = Path.Combine(_cfg.AddOnsPath, name);
            if (Directory.Exists(p)) return p;
        }
        return null;
    }

    private (string Text, Color Color) StatusOf(TrackedAddon a)
    {
        if (a.Activity != null) return (a.Activity, Theme.Accent);
        if (a.Error != null) return (a.Error, Theme.Bad);
        if (a.IsManual)
        {
            if (string.IsNullOrEmpty(a.PageUrl))
                return a.InstalledTag == null || FoldersMissing(a)
                    ? ("Manual download – use Add addons › Install from a zip file", Theme.Warn)
                    : ("Manual download – installed from a zip file", Theme.Info);
            if (a.InstalledTag == null) return ("Manual download – right-click to download it", Theme.Warn);
            if (FoldersMissing(a)) return ("Files missing – right-click to download it again", Theme.Warn);
            return ("Manual download – right-click to check its page for updates", Theme.Info);
        }
        if (a.Latest == null) return ("Not checked yet", Theme.Muted);

        var unverified = FlavorConfirmed(a) ? "" : $" (not confirmed for {FlavorDisplayName(_cfg.Flavor)})";
        if (a.InstalledTag == null) return ("Not installed by this app yet – Update all installs it" + unverified, Theme.Warn);
        if (FoldersMissing(a)) return ("Files missing – Update all reinstalls it", Theme.Warn);
        if (NeedsUpdate(a)) return ("Update available" + unverified, Theme.Warn);
        return ("Up to date" + unverified, Theme.Good);
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

        _subtitle.Text = Directory.Exists(_cfg.AddOnsPath)
            ? $"{FlavorDisplayName(_cfg.Flavor)}   ·   {_cfg.AddOnsPath}"
            : "AddOns folder not set – open Settings";
        _list.FillLastColumn();
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
        item.SubItems[4].Text = a.SourceText;
        item.SubItems[4].ForeColor = Theme.Muted;
        item.SubItems[2].ForeColor = Theme.Muted;
    }

    private List<TrackedAddon> SelectedAddons() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => (TrackedAddon)i.Tag!).ToList();

    private void UpdateButtons()
    {
        var hasAny = _cfg.Addons.Count > 0;
        _addBtn.Enabled = !_busy;
        _checkBtn.Enabled = !_busy && hasAny;
        _updateAllBtn.Enabled = !_busy && hasAny;
        _moreBtn.Enabled = !_busy;
        _settingsBtn.Enabled = !_busy;
    }

    private void Save()
    {
        try { _store.Save(_cfg); }
        catch (Exception ex) { Log($"Couldn't save settings to {_store.FilePath}: {ex.Message}"); }
    }

    private void Log(string message) =>
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
}
