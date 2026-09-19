using System.Drawing.Drawing2D;
using static DisplayChanger.Native.NativeMethods;

namespace DisplayChanger;

/// <summary>
/// Small always-on-top pane in the bottom-right corner of the primary display. It shows one category
/// (Display / Output / Input), the devices being cycled through and which one is current. Every call to
/// <see cref="Present"/> replaces the contents in place, so rapid hotkey presses update a single pane
/// instead of queueing a notification per press. It never takes focus and fades out after a short hold.
/// </summary>
public sealed class OverlayWindow : Form
{
    /// <summary>One row in the pane.</summary>
    /// <param name="Text">Device name.</param>
    /// <param name="Selected">True for the current device; drawn highlighted.</param>
    public sealed record Entry(string Text, bool Selected);

    private static readonly Color PanelColor = Color.FromArgb(32, 32, 32);
    private static readonly Color BorderColor = Color.FromArgb(72, 72, 72);
    private static readonly Color TitleColor = Color.FromArgb(165, 165, 165);
    private static readonly Color TextColor = Color.FromArgb(225, 225, 225);
    private static readonly Color SelectedTextColor = Color.White;
    private static readonly Color SelectedRowColor = Color.FromArgb(58, 58, 58);
    private static readonly Color AccentColor = Color.FromArgb(96, 205, 255);
    private static readonly Color NoteColor = Color.FromArgb(200, 170, 90);

    // Logical (96 dpi) metrics; scaled per monitor at layout time.
    private const int Inset = 14;
    private const int EdgeMargin = 16;
    private const int TitleHeight = 18;
    private const int TitleGap = 8;
    private const int RowHeight = 30;
    private const int RowTextPad = 12;
    private const int AccentBarWidth = 3;
    private const int NoteGap = 6;
    private const int NoteHeight = 18;
    private const int MinWidth = 280;
    private const int MaxWidth = 460;
    private const int CornerRadius = 8;

    private const int HoldMs = 2200;
    private const int FadeIntervalMs = 25;
    private const double FadeStep = 0.10;

    private readonly System.Windows.Forms.Timer _holdTimer = new() { Interval = HoldMs };
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = FadeIntervalMs };

    private string _category = string.Empty;
    private IReadOnlyList<Entry> _entries = [];
    private string? _note;

    private float _scale = 1f;
    private Font? _titleFont;
    private Font? _rowFont;
    private Font? _noteFont;
    private bool _drawOwnCorners;

    public OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = PanelColor;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Text = "DisplayChanger";

        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); _fadeTimer.Start(); };
        _fadeTimer.Tick += (_, _) => FadeTick();
        MouseDown += (_, _) => Dismiss();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    /// <summary>
    /// Shows (or updates) the pane for <paramref name="category"/>. Any previous content, including a
    /// different category, is replaced outright and the hide timer restarts.
    /// </summary>
    /// <param name="category">Heading, e.g. "Output".</param>
    /// <param name="entries">Rows to list, in cycle order.</param>
    /// <param name="note">Optional one-line remark under the list, e.g. why nothing changed.</param>
    public void Present(string category, IReadOnlyList<Entry> entries, string? note = null)
    {
        _category = category;
        _entries = entries;
        _note = string.IsNullOrWhiteSpace(note) ? null : note;

        _fadeTimer.Stop();
        _holdTimer.Stop();
        Opacity = 1.0;

        Relayout();

        if (!Visible) Show();
        else Invalidate();

        _holdTimer.Start();
    }

    /// <summary>Hides the pane immediately.</summary>
    public void Dismiss()
    {
        _holdTimer.Stop();
        _fadeTimer.Stop();
        Hide();
        Opacity = 1.0;
    }

    // ---- Layout ---------------------------------------------------------------------------------

    private void Relayout()
    {
        var (work, dpi) = PrimaryWorkArea();
        _scale = dpi / 96f;
        RebuildFonts();

        int padding = S(Inset);
        int rowHeight = S(RowHeight);
        int rowTextPad = S(RowTextPad);

        // Width: widest row (or title / note), clamped.
        int contentWidth = TextRenderer.MeasureText(_category.ToUpperInvariant(), _titleFont).Width;
        foreach (var e in _entries)
            contentWidth = Math.Max(contentWidth, TextRenderer.MeasureText(e.Text, _rowFont).Width + rowTextPad * 2);
        if (_note is not null)
            contentWidth = Math.Max(contentWidth, TextRenderer.MeasureText(_note, _noteFont).Width);

        int width = Math.Clamp(contentWidth + padding * 2, S(MinWidth), S(MaxWidth));

        int height = padding + S(TitleHeight) + S(TitleGap) + rowHeight * Math.Max(_entries.Count, 0);
        if (_note is not null) height += S(NoteGap) + S(NoteHeight);
        height += padding;

        int margin = S(EdgeMargin);
        var bounds = new Rectangle(work.Right - margin - width, work.Bottom - margin - height, width, height);
        Bounds = bounds;

        if (_drawOwnCorners)
        {
            Region?.Dispose();
            Region = new Region(RoundedRect(new Rectangle(0, 0, width, height), S(CornerRadius)));
        }

        Invalidate();
    }

    private void RebuildFonts()
    {
        _titleFont?.Dispose();
        _rowFont?.Dispose();
        _noteFont?.Dispose();
        _titleFont = new Font("Segoe UI", 11f * _scale, FontStyle.Bold, GraphicsUnit.Pixel);
        _rowFont = new Font("Segoe UI", 14f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
        _noteFont = new Font("Segoe UI", 12f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private int S(int logical) => (int)Math.Round(logical * _scale);

    /// <summary>Work area (taskbar excluded) and effective DPI of whichever display is primary right now.</summary>
    private static (Rectangle Work, uint Dpi) PrimaryWorkArea()
    {
        var monitor = MonitorFromPoint(new POINT { X = 0, Y = 0 }, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        var work = GetMonitorInfo(monitor, ref info) ? info.rcWork.ToRectangle() : Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);

        uint dpi = 96;
        try
        {
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dx, out _) == 0 && dx > 0) dpi = dx;
        }
        catch (Exception) { /* pre-8.1 or missing shcore; 96 is fine */ }

        return (work, dpi);
    }

    // ---- Painting -------------------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(PanelColor);

        int padding = S(Inset);
        int rowHeight = S(RowHeight);
        int rowTextPad = S(RowTextPad);
        int x = padding;
        int y = padding;
        int innerWidth = ClientSize.Width - padding * 2;

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        TextRenderer.DrawText(g, _category.ToUpperInvariant(), _titleFont, new Rectangle(x, y, innerWidth, S(TitleHeight)), TitleColor, flags);
        y += S(TitleHeight) + S(TitleGap);

        foreach (var entry in _entries)
        {
            var row = new Rectangle(x, y, innerWidth, rowHeight);
            if (entry.Selected)
            {
                using var fill = new SolidBrush(SelectedRowColor);
                using var path = RoundedRect(row, S(4));
                g.FillPath(fill, path);

                using var accent = new SolidBrush(AccentColor);
                var bar = new Rectangle(row.Left, row.Top + S(6), S(AccentBarWidth), row.Height - S(12));
                using var barPath = RoundedRect(bar, Math.Max(1, S(AccentBarWidth) / 2));
                g.FillPath(accent, barPath);
            }

            var textRect = new Rectangle(row.Left + rowTextPad, row.Top, row.Width - rowTextPad * 2, row.Height);
            TextRenderer.DrawText(g, entry.Text, _rowFont, textRect, entry.Selected ? SelectedTextColor : TextColor, flags);
            y += rowHeight;
        }

        if (_note is not null)
        {
            y += S(NoteGap);
            TextRenderer.DrawText(g, _note, _noteFont, new Rectangle(x, y, innerWidth, S(NoteHeight)), NoteColor, flags);
        }

        if (_drawOwnCorners)
        {
            using var pen = new Pen(BorderColor);
            using var border = RoundedRect(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), S(CornerRadius));
            g.DrawPath(pen, border);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0 || r.Width <= 0 || r.Height <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ---- Window plumbing ------------------------------------------------------------------------

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Windows 11 draws proper anti-aliased rounded corners and a border for us; on Windows 10 fall back to a region.
        int corner = DWMWCP_ROUND;
        _drawOwnCorners = DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int)) != 0;
        if (!_drawOwnCorners)
        {
            int colorRef = BorderColor.R | (BorderColor.G << 8) | (BorderColor.B << 16);
            _ = DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DPICHANGED)
        {
            // We size and position ourselves from the target monitor's DPI, so skip WinForms' automatic rescale.
            Relayout();
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Never let a user gesture close the pane; it lives as long as the tray app.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Dismiss();
            return;
        }
        base.OnFormClosing(e);
    }

    private void FadeTick()
    {
        double next = Opacity - FadeStep;
        if (next <= 0.05)
        {
            _fadeTimer.Stop();
            Hide();
            Opacity = 1.0;
            return;
        }
        Opacity = next;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _holdTimer.Dispose();
            _fadeTimer.Dispose();
            _titleFont?.Dispose();
            _rowFont?.Dispose();
            _noteFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
