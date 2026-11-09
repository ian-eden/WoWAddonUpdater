namespace WoWAddonUpdater;

/// <summary>Lists addons found in the AddOns folder; the user ticks which ones to add.</summary>
public sealed class FindAddonsForm : Form
{
    private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
    private readonly List<Suggestion> _suggestions;

    public List<Suggestion> Selected =>
        _list.CheckedIndices.Cast<int>().Select(i => _suggestions[i]).ToList();

    public FindAddonsForm(List<Suggestion> suggestions)
    {
        _suggestions = suggestions;

        Text = "Addons found";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(620, 400);
        MinimumSize = new Size(460, 300);
        MinimizeBox = false;
        ShowInTaskbar = false;

        foreach (var s in suggestions)
        {
            var note = s.Known ? "" : "   (link found in the addon's files, not checked yet)";
            _list.Items.Add($"{s.Name}   ·   github.com/{s.Owner}/{s.Repo}{note}", isChecked: s.Known);
        }

        var header = new Label
        {
            Text = "These addons in your AddOns folder can be updated from GitHub. Tick the ones to add:",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 8),
        };
        var footer = new Label
        {
            Text = "After adding, click \"Update all\" once so this app installs and tracks the latest version.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(3, 8, 3, 3),
        };

        var add = new Button { Text = "Add selected", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(add);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_list, 0, 1);
        root.Controls.Add(footer, 0, 2);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);

        AcceptButton = add;
        CancelButton = cancel;
    }
}
