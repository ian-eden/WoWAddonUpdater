using System.Drawing.Drawing2D;

namespace WoWAddonUpdater;

/// <summary>Colours and small custom controls for the app's look (matches the flame icon).</summary>
public static class Theme
{
    public static readonly Color Accent = Color.FromArgb(232, 89, 12);
    public static readonly Color AccentHover = Color.FromArgb(246, 112, 32);
    public static readonly Color AccentDown = Color.FromArgb(196, 70, 8);
    public static readonly Color AccentSoft = Color.FromArgb(255, 234, 218);

    public static readonly Color HeaderLeft = Color.FromArgb(62, 26, 38);
    public static readonly Color HeaderRight = Color.FromArgb(24, 13, 20);
    public static readonly Color HeaderText = Color.White;
    public static readonly Color HeaderSubText = Color.FromArgb(222, 196, 190);
    public static readonly Color HeaderButton = Color.FromArgb(86, 44, 56);
    public static readonly Color HeaderButtonHover = Color.FromArgb(112, 58, 72);

    public static readonly Color Window = Color.FromArgb(246, 243, 241);
    public static readonly Color Surface = Color.White;
    public static readonly Color AltRow = Color.FromArgb(250, 248, 247);
    public static readonly Color Border = Color.FromArgb(224, 218, 214);
    public static readonly Color Text = Color.FromArgb(36, 30, 33);
    public static readonly Color Muted = Color.FromArgb(120, 110, 112);

    public static readonly Color Good = Color.FromArgb(40, 150, 64);
    public static readonly Color Warn = Color.FromArgb(222, 112, 20);
    public static readonly Color Bad = Color.FromArgb(200, 36, 44);
    public static readonly Color Info = Color.FromArgb(86, 104, 150);

    public static readonly Color NoticeBack = Color.FromArgb(255, 241, 222);
    public static readonly Color WaitBack = Color.FromArgb(228, 238, 252);

    public static Button PrimaryButton(string text) =>
        FlatButton(text, Accent, Color.White, AccentHover, AccentDown, null);

    public static Button SecondaryButton(string text) =>
        FlatButton(text, Surface, Text, Color.FromArgb(250, 240, 234), Color.FromArgb(244, 226, 214), Border);

    public static Button HeaderFlatButton(string text) =>
        FlatButton(text, HeaderButton, HeaderText, HeaderButtonHover, HeaderLeft, null);

    private static Button FlatButton(string text, Color back, Color fore, Color hover, Color down, Color? border)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            Padding = new Padding(10, 4, 10, 4),
            Margin = new Padding(0, 0, 8, 0),
        };
        b.FlatAppearance.BorderSize = border == null ? 0 : 1;
        if (border != null) b.FlatAppearance.BorderColor = border.Value;
        b.FlatAppearance.MouseOverBackColor = hover;
        b.FlatAppearance.MouseDownBackColor = down;
        return b;
    }

    /// <summary>A white box with a thin border around a borderless control (text box, list, log).</summary>
    public static Panel Card(Control inner, Padding padding)
    {
        var card = new Panel { BackColor = Surface, Padding = padding, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        card.Resize += (_, _) => card.Invalidate();
        inner.Dock = DockStyle.Fill;
        card.Controls.Add(inner);
        return card;
    }

    public static ToolStripRenderer MenuRenderer { get; } = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => AccentSoft;
        public override Color MenuItemBorder => Color.FromArgb(246, 196, 160);
        public override Color MenuBorder => Border;
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color CheckBackground => AccentSoft;
        public override Color CheckSelectedBackground => AccentSoft;
        public override Color CheckPressedBackground => AccentSoft;
    }
}

/// <summary>The dark flame-coloured band at the top of the window.</summary>
public sealed class HeaderPanel : Panel
{
    public HeaderPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var r = ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;
        using (var bg = new LinearGradientBrush(r, Theme.HeaderLeft, Theme.HeaderRight, LinearGradientMode.Horizontal))
            e.Graphics.FillRectangle(bg, r);

        // Thin ember line along the bottom edge.
        var line = new Rectangle(0, r.Bottom - 3, r.Width, 3);
        using var ember = new LinearGradientBrush(line, Color.FromArgb(255, 196, 64), Theme.Accent, LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(ember, line);
    }
}

/// <summary>
/// Details list drawn by hand: taller rows, soft alternating stripes, an orange selection,
/// and a coloured dot in front of the status text.
/// </summary>
public sealed class ModernListView : ListView
{
    /// <summary>Column that gets a coloured dot (drawn in the sub-item's ForeColor).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int DotColumn { get; set; } = -1;

    public ModernListView()
    {
        DoubleBuffered = true;
        OwnerDraw = true;
        View = View.Details;
        FullRowSelect = true;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
    }

    /// <summary>Row height in pixels at 96 DPI (a 1-pixel-wide image list is the standard trick to set it).</summary>
    public void SetRowHeight(int logicalHeight)
    {
        SmallImageList = new ImageList { ImageSize = new Size(1, LogicalToDeviceUnits(logicalHeight)) };
    }

    /// <summary>Stretches the last column to fill the width, so there's no empty strip or sideways scrollbar.</summary>
    public void FillLastColumn()
    {
        if (Columns.Count == 0) return;
        var others = 0;
        for (var i = 0; i < Columns.Count - 1; i++) others += Columns[i].Width;
        // ClientSize can lag behind the vertical scrollbar appearing, so leave room for it ourselves.
        var rowsHigh = Items.Count > 0 && SmallImageList != null
            ? Items.Count * SmallImageList.ImageSize.Height + LogicalToDeviceUnits(28) > Height
            : false;
        var width = Width - 2 - (rowsHigh ? SystemInformation.VerticalScrollBarWidth : 0);
        var w = Math.Min(ClientSize.Width, width) - others - 1;
        var target = Math.Max(LogicalToDeviceUnits(120), w);
        if (Columns[^1].Width != target) Columns[^1].Width = target;
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FillLastColumn();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    /// <summary>The last column always fills the width, so the sideways scrollbar is never needed: keep it hidden.</summary>
    protected override void WndProc(ref Message m)
    {
        const int WM_NCCALCSIZE = 0x0083, GWL_STYLE = -16;
        const long WS_HSCROLL = 0x00100000;
        if (m.Msg == WM_NCCALCSIZE && IsHandleCreated)
        {
            var style = (long)GetWindowLongPtr(Handle, GWL_STYLE);
            if ((style & WS_HSCROLL) != 0) SetWindowLongPtr(Handle, GWL_STYLE, (IntPtr)(style & ~WS_HSCROLL));
        }
        base.WndProc(ref m);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        FillLastColumn();
    }

    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        if (e.ColumnIndex != Columns.Count - 1) BeginInvoke(FillLastColumn);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Color.FromArgb(250, 247, 245))) g.FillRectangle(bg, e.Bounds);
        using (var pen = new Pen(Theme.Border))
        {
            g.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (e.ColumnIndex > 0) g.DrawLine(pen, e.Bounds.Left, e.Bounds.Top + 6, e.Bounds.Left, e.Bounds.Bottom - 7);
        }
        var r = Rectangle.FromLTRB(e.Bounds.Left + LogicalToDeviceUnits(10), e.Bounds.Top, e.Bounds.Right - 4, e.Bounds.Bottom);
        using var font = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(g, e.Header?.Text ?? "", font, r, Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) => e.DrawDefault = false; // drawn per sub-item

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item == null || e.SubItem == null) return;
        var g = e.Graphics;
        var bounds = e.Bounds;
        var back = e.Item.Selected ? Theme.AccentSoft : e.ItemIndex % 2 == 1 ? Theme.AltRow : Theme.Surface;
        using (var bg = new SolidBrush(back)) g.FillRectangle(bg, bounds);

        if (e.Item.Selected && e.ColumnIndex == 0)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, bounds.Left, bounds.Top, LogicalToDeviceUnits(3), bounds.Height);
        }

        var left = bounds.Left + LogicalToDeviceUnits(10);
        var fore = e.ColumnIndex == 0 ? Theme.Text : e.SubItem.ForeColor;
        if (fore == SystemColors.WindowText || fore.IsEmpty) fore = Theme.Text;

        if (e.ColumnIndex == DotColumn && e.SubItem.Text.Length > 0)
        {
            var d = LogicalToDeviceUnits(8);
            var oldMode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var dot = new SolidBrush(fore)) g.FillEllipse(dot, left, bounds.Top + (bounds.Height - d) / 2f, d, d);
            g.SmoothingMode = oldMode;
            left += d + LogicalToDeviceUnits(7);
        }

        var r = Rectangle.FromLTRB(left, bounds.Top, bounds.Right - 4, bounds.Bottom);
        using var bold = e.ColumnIndex == 0 ? new Font(Font, FontStyle.Bold) : null;
        TextRenderer.DrawText(g, e.SubItem.Text, bold ?? Font, r, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }
}
