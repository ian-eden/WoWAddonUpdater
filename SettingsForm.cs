namespace WoWAddonUpdater;

public sealed class SettingsForm : Form
{
    private readonly TextBox _path = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _flavor = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _token = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };

    public string AddOnsPath => _path.Text.Trim();
    public string Flavor => string.IsNullOrWhiteSpace(_flavor.Text) ? "forever" : _flavor.Text.Trim().ToLowerInvariant();
    public string? Token => string.IsNullOrWhiteSpace(_token.Text) ? null : _token.Text.Trim();

    public SettingsForm(AppConfig cfg)
    {
        Text = "Settings";
        Font = new Font("Segoe UI", 9f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        _path.Text = cfg.AddOnsPath;
        _flavor.Items.AddRange(new object[] { "forever", "mainline", "classic", "bcc", "wrath", "cata", "mists" });
        _flavor.Text = cfg.Flavor;
        _token.Text = cfg.GitHubToken ?? "";

        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) => Browse();

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Padding = new Padding(12), Width = 640 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 440));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        grid.Controls.Add(MakeLabel("AddOns folder:"), 0, 0);
        grid.Controls.Add(_path, 1, 0);
        grid.Controls.Add(browse, 2, 0);
        grid.Controls.Add(Hint(@"e.g. D:\Games\World of Warcraft\_forever_\Interface\AddOns"), 1, 1);

        grid.Controls.Add(MakeLabel("Game version:"), 0, 2);
        grid.Controls.Add(_flavor, 1, 2);
        grid.Controls.Add(Hint("Which build of each addon to download. WoW Forever = forever, Retail = mainline."), 1, 3);

        grid.Controls.Add(MakeLabel("GitHub token:"), 0, 4);
        grid.Controls.Add(_token, 1, 4);
        grid.Controls.Add(Hint("Optional. Without one GitHub allows 60 checks an hour, plenty for a few addons. " +
                               "Stored as plain text in WoWAddonUpdater.json."), 1, 5);

        var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Accept();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        grid.Controls.Add(buttons, 0, 6);
        grid.SetColumnSpan(buttons, 3);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = @"Choose the AddOns folder (…\World of Warcraft\_forever_\Interface\AddOns)",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(AddOnsPath) ? AddOnsPath : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) _path.Text = dlg.SelectedPath;
    }

    private void Accept()
    {
        if (!Directory.Exists(AddOnsPath))
        {
            MessageBox.Show(this, "That folder doesn't exist.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!string.Equals(Path.GetFileName(AddOnsPath.TrimEnd('\\')), "AddOns", StringComparison.OrdinalIgnoreCase))
        {
            var answer = MessageBox.Show(this,
                "That folder isn't named \"AddOns\". Addons are installed straight into it, so make sure it's the right one.\n\nUse it anyway?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }
        DialogResult = DialogResult.OK;
        Close();
    }

    private static Label MakeLabel(string text) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) };

    private static Label Hint(string text) =>
        new() { Text = text, AutoSize = true, MaximumSize = new Size(440, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 10) };
}
