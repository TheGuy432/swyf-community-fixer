// The window (WinForms, built into Windows / .NET Framework 4.x). Everything is custom-painted so it looks the same
// on every Windows version, and every size goes through Ui.S() so it stays sharp and correctly sized at any DPI.
// Screens: game not found / game running / choose fixes / undo confirmation / progress / done.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;

static partial class Fixer
{
    static int RunWindow(string gameDir, List<string> test)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new FixerForm(gameDir, test));
        return 0;
    }

    // ---- Look ----
    static class Ui
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }
        public static float Sf(float v) { return v * Scale; }

        public static readonly Color Bg = Color.FromArgb(246, 247, 249);
        public static readonly Color Card = Color.White;
        public static readonly Color Line = Color.FromArgb(222, 226, 231);
        public static readonly Color Ink = Color.FromArgb(28, 33, 39);
        public static readonly Color Muted = Color.FromArgb(98, 108, 120);
        public static readonly Color Accent = Color.FromArgb(31, 122, 92);
        public static readonly Color AccentSoft = Color.FromArgb(225, 242, 234);
        public static readonly Color AccentLine = Color.FromArgb(160, 214, 190);
        public static readonly Color Warn = Color.FromArgb(176, 106, 0);
        public static readonly Color WarnSoft = Color.FromArgb(255, 251, 242);
        public static readonly Color WarnPill = Color.FromArgb(253, 242, 220);
        public static readonly Color WarnLine = Color.FromArgb(236, 196, 120);
        public static readonly Color Bad = Color.FromArgb(178, 52, 58);
        public static readonly Color BadSoft = Color.FromArgb(252, 236, 237);
        public static readonly Color BadLine = Color.FromArgb(230, 170, 174);
        public static readonly Color Blue = Color.FromArgb(42, 95, 168);
        public static readonly Color BlueSoft = Color.FromArgb(226, 236, 249);
        public static readonly Color Grey = Color.FromArgb(120, 127, 134);
        public static readonly Color GreySoft = Color.FromArgb(236, 238, 240);
        public static readonly Color Header = Color.FromArgb(24, 30, 36);
        public static readonly Color HeaderText = Color.FromArgb(170, 180, 190);
        public static readonly Color Mint = Color.FromArgb(76, 196, 151);

        static string family;
        public static string Family
        {
            get
            {
                if (family != null) return family;
                try
                {
                    foreach (string want in new[] { "Segoe UI", "Inter" })
                        foreach (FontFamily f in new InstalledFontCollection().Families)
                            if (f.Name == want) return family = want;
                }
                catch { }
                return family = SystemFonts.MessageBoxFont.FontFamily.Name;
            }
        }
        // size is in pixels at 100 % (96 DPI)
        public static Font F(float size, FontStyle st) { return new Font(Family, Sf(size), st, GraphicsUnit.Pixel); }
        public static Font F(float size) { return F(size, FontStyle.Regular); }
        public static Font Mono(float size) { return new Font(FontFamily.GenericMonospace, Sf(size), FontStyle.Bold, GraphicsUnit.Pixel); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath(); float d = rad * 2;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        public static void Hq(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit; }

        public static void Check(Graphics g, float x, float y, float size, Color c)
        {
            using (var pen = new Pen(c, size * 0.11f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(x + size * 0.25f, y + size * 0.52f), new PointF(x + size * 0.42f, y + size * 0.68f), new PointF(x + size * 0.74f, y + size * 0.33f) });
        }

        public static void Waveform(Graphics g, float x, float cy, float scale, Color c)
        {
            using (var pen = new Pen(c, 3f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float[] h = { 8, 18, 28, 14, 22, 10 };
                for (int i = 0; i < h.Length; i++) g.DrawLine(pen, x + i * 7 * scale, cy - h[i] / 2 * scale, x + i * 7 * scale, cy + h[i] / 2 * scale);
            }
        }

        public static Icon MakeIcon()
        {
            try
            {
                var bmp = new Bitmap(32, 32);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Hq(g); g.Clear(Color.Transparent);
                    using (var p = Round(new RectangleF(0, 0, 31, 31), 7)) using (var b = new SolidBrush(Header)) g.FillPath(b, p);
                    Waveform(g, 6, 16, 0.6f, Mint);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return null; }
        }

        public static int TextHeight(string text, Font f, int width)
        {
            Size sz = TextRenderer.MeasureText(text, f, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            return sz.Height;
        }
    }

    // ---- Controls ----
    sealed class Card : Panel
    {
        public Color Stripe = Color.Empty, Fill = Ui.Card;
        public Card() { DoubleBuffered = true; BackColor = Ui.Bg; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Hq(g); g.Clear(Parent != null ? Parent.BackColor : Ui.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Ui.Round(r, Ui.Sf(10)))
            {
                using (var b = new SolidBrush(Fill)) g.FillPath(b, p);
                using (var pen = new Pen(Stripe.IsEmpty ? Ui.Line : Stripe)) g.DrawPath(pen, p);
            }
        }
    }

    sealed class FlatBtn : Button
    {
        readonly bool primary, danger;
        public FlatBtn(string text, bool primary, bool danger)
        {
            this.primary = primary; this.danger = danger;
            Text = text; FlatStyle = FlatStyle.Flat; Font = Ui.F(14, primary || danger ? FontStyle.Bold : FontStyle.Regular);
            Height = Ui.S(38); Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
            FlatAppearance.BorderSize = 1;
            Restyle();
        }
        public FlatBtn(string text, bool primary) : this(text, primary, false) { }
        protected override void OnEnabledChanged(EventArgs e) { Restyle(); base.OnEnabledChanged(e); }
        void Restyle()
        {
            Color fill = !Enabled ? Ui.GreySoft : danger ? Ui.Bad : primary ? Ui.Accent : Color.White;
            BackColor = fill;
            ForeColor = !Enabled ? Ui.Grey : primary || danger ? Color.White : Ui.Ink;
            FlatAppearance.BorderColor = !Enabled ? Ui.Line : primary || danger ? fill : Ui.Line;
            FlatAppearance.MouseOverBackColor = !Enabled ? fill : danger ? Color.FromArgb(158, 40, 46) : primary ? Color.FromArgb(26, 104, 78) : Color.FromArgb(242, 244, 246);
        }
        // fits the text with some padding, but never narrower than min (unscaled px)
        public FlatBtn Fit(int min)
        {
            int w = TextRenderer.MeasureText(Text, Font).Width + Ui.S(32);
            Width = Math.Max(Ui.S(min), w);
            return this;
        }
    }

    sealed class HeaderBar : Control
    {
        public Action LanguageClicked;
        RectangleF pill;
        public HeaderBar() { DoubleBuffered = true; Dock = DockStyle.Top; Height = Ui.S(74); Cursor = Cursors.Default; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Hq(g); g.Clear(Ui.Header);
            Ui.Waveform(g, Ui.Sf(26), Ui.Sf(37), Ui.Scale, Ui.Mint);
            using (var f = Ui.F(20, FontStyle.Bold)) g.DrawString("SWYF Community Fixer", f, Brushes.White, Ui.Sf(76), Ui.Sf(13));
            // language picker
            using (var f = Ui.F(13, FontStyle.Bold))
            {
                string t = NativeName(LangCode);
                float tw = g.MeasureString(t, f).Width;
                float w = tw + Ui.Sf(52), x = Width - w - Ui.Sf(22);
                pill = new RectangleF(x, Ui.Sf(21), w, Ui.Sf(32));
                using (var p = Ui.Round(pill, Ui.Sf(16)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(36, 44, 52))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(70, 80, 90))) g.DrawPath(pen, p);
                }
                // small globe
                using (var pen = new Pen(Ui.Mint, Ui.Sf(1.4f)))
                {
                    float gx = x + Ui.Sf(11), gy = Ui.Sf(29), gs = Ui.Sf(15);
                    g.DrawEllipse(pen, gx, gy, gs, gs);
                    g.DrawEllipse(pen, gx + gs * 0.28f, gy, gs * 0.44f, gs);
                    g.DrawLine(pen, gx, gy + gs / 2, gx + gs, gy + gs / 2);
                }
                g.DrawString(t, f, Brushes.White, x + Ui.Sf(31), Ui.Sf(29));
                using (var tf = Ui.F(13)) using (var b = new SolidBrush(Ui.HeaderText))
                    g.DrawString("v" + Version + "  ·  " + T("ui.header_tagline"), tf, b, new RectangleF(Ui.Sf(77), Ui.Sf(42), x - Ui.Sf(77) - Ui.Sf(12), Ui.Sf(20)), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
                using (var b = new SolidBrush(Ui.HeaderText))
                    g.FillPolygon(b, new[] { new PointF(x + w - Ui.Sf(17), Ui.Sf(35)), new PointF(x + w - Ui.Sf(9), Ui.Sf(35)), new PointF(x + w - Ui.Sf(13), Ui.Sf(40)) });
            }
        }
        protected override void OnMouseMove(MouseEventArgs e) { Cursor = pill.Contains(e.Location) && Enabled ? Cursors.Hand : Cursors.Default; base.OnMouseMove(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (pill.Contains(e.Location) && LanguageClicked != null) LanguageClicked();
            base.OnMouseClick(e);
        }
        public Point PickerBottomLeft { get { return new Point((int)pill.X, (int)pill.Bottom + Ui.S(4)); } }
    }

    // One fix in the list: checkbox, name, pills, status text. Click (or Space) toggles it.
    sealed class FixRow : Control
    {
        public readonly Item It;
        public Action Changed;
        bool focused;
        // The console status starts with the state ("not installed - ..."); the pill shows that here.
        static string GuiStatus(string s)
        {
            int dash = s.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && dash < 40) s = s.Substring(dash + 3);
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }
        string Status { get { return GuiStatus(It.Status); } }

        public FixRow(Item it, Color bg)
        {
            It = it; BackColor = bg; DoubleBuffered = true;
            SetStyle(ControlStyles.Selectable, it.Available);
            TabStop = it.Available;
            Cursor = it.Available ? Cursors.Hand : Cursors.Default;
        }
        static string PillText(Pill p)
        {
            switch (p)
            {
                case Pill.Recommended: return T("ui.pill_recommended");
                case Pill.Installed: return T("ui.pill_installed");
                case Pill.NotNeeded: return T("ui.pill_not_needed");
                case Pill.NotAvailable: return T("ui.pill_not_available");
                case Pill.Experimental: return T("ui.pill_experimental");
                default: return T("ui.pill_optional");
            }
        }
        List<KeyValuePair<string, Pill?>> Pills()
        {
            var l = new List<KeyValuePair<string, Pill?>>();
            l.Add(new KeyValuePair<string, Pill?>(PillText(It.GetPill()).ToUpperInvariant(), It.GetPill()));
            if (It.Untested && It.Available) l.Add(new KeyValuePair<string, Pill?>(T("ui.pill_untested").ToUpperInvariant(), null));
            return l;
        }

        // Lays out name / pills / status for the given width. Returns the total height.
        int LayoutRow(Graphics g, int width, out RectangleF nameR, out PointF pillAt, out RectangleF statusR)
        {
            float x0 = Ui.Sf(48), w = width - x0 - Ui.Sf(18), y = Ui.Sf(11);
            float pw = 0;
            using (var pf = Ui.F(11, FontStyle.Bold)) foreach (var p in Pills()) pw += g.MeasureString(p.Key, pf).Width + Ui.Sf(10) + Ui.Sf(5);
            using (var nf = Ui.F(15, FontStyle.Bold))
            {
                SizeF one = g.MeasureString(It.Name, nf);
                if (one.Width + Ui.Sf(6) + pw <= w)
                {
                    nameR = new RectangleF(x0, y, one.Width + 2, one.Height);
                    pillAt = new PointF(x0 + one.Width + Ui.Sf(6), y + Ui.Sf(2));
                    y += Math.Max(one.Height, Ui.Sf(21)) + Ui.Sf(2);
                }
                else
                {
                    SizeF wrapped = g.MeasureString(It.Name, nf, (int)w);
                    nameR = new RectangleF(x0, y, w, wrapped.Height);
                    y += wrapped.Height + Ui.Sf(2);
                    pillAt = new PointF(x0, y);
                    y += Ui.Sf(23);
                }
            }
            using (var sf = Ui.F(13))
            {
                SizeF st = g.MeasureString(Status, sf, (int)w);
                statusR = new RectangleF(x0, y, w, st.Height + 2);
                y += st.Height;
            }
            return (int)Math.Ceiling(y + Ui.Sf(12));
        }

        public void FitHeight(int width)
        {
            using (Graphics g = CreateGraphics())
            {
                RectangleF a, c; PointF b;
                Height = LayoutRow(g, width, out a, out b, out c);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Hq(g); g.Clear(BackColor);
            RectangleF nameR, statusR; PointF pillAt;
            LayoutRow(g, Width, out nameR, out pillAt, out statusR);
            bool on = It.Available && It.Wanted;
            Color accent = It.Experimental ? Ui.Warn : Ui.Accent;
            var box = new RectangleF(Ui.Sf(16), nameR.Y + Ui.Sf(1), Ui.Sf(20), Ui.Sf(20));
            using (var p = Ui.Round(box, Ui.Sf(4)))
            {
                if (on) { using (var b = new SolidBrush(accent)) g.FillPath(b, p); }
                else
                {
                    using (var b = new SolidBrush(It.Available ? Color.White : Ui.GreySoft)) g.FillPath(b, p);
                    using (var pen = new Pen(It.Available ? Color.FromArgb(160, 168, 176) : Ui.Line, Ui.Sf(1.5f))) g.DrawPath(pen, p);
                }
            }
            if (on) Ui.Check(g, box.X, box.Y, box.Width, Color.White);
            if (focused && ShowFocusCues)
                using (var pen = new Pen(accent, Ui.Sf(1.5f)) { DashStyle = DashStyle.Dot })
                    g.DrawRectangle(pen, box.X - Ui.Sf(3), box.Y - Ui.Sf(3), box.Width + Ui.Sf(6), box.Height + Ui.Sf(6));

            Color ink = It.Available ? Ui.Ink : Ui.Grey;
            using (var nf = Ui.F(15, FontStyle.Bold)) using (var b = new SolidBrush(ink)) g.DrawString(It.Name, nf, b, nameR);
            float px = pillAt.X;
            using (var pf = Ui.F(11, FontStyle.Bold))
                foreach (var p in Pills())
                {
                    Color fg, bg;
                    if (p.Value == null) { fg = Ui.Warn; bg = Ui.WarnPill; }
                    else switch (p.Value.Value)
                    {
                        case Pill.Recommended: fg = Ui.Accent; bg = Ui.AccentSoft; break;
                        case Pill.Installed: fg = Ui.Blue; bg = Ui.BlueSoft; break;
                        case Pill.Experimental: fg = Ui.Warn; bg = Ui.WarnPill; break;
                        case Pill.Optional: fg = Ui.Muted; bg = Ui.GreySoft; break;
                        default: fg = Ui.Grey; bg = Ui.GreySoft; break;
                    }
                    SizeF sz = g.MeasureString(p.Key, pf);
                    var r = new RectangleF(px, pillAt.Y + Ui.Sf(1), sz.Width + Ui.Sf(10), Ui.Sf(19));
                    using (var path = Ui.Round(r, r.Height / 2)) using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                    using (var b = new SolidBrush(fg)) g.DrawString(p.Key, pf, b, px + Ui.Sf(5), pillAt.Y + Ui.Sf(1) + (r.Height - sz.Height) / 2);
                    px += r.Width + Ui.Sf(5);
                }
            using (var sf = Ui.F(13)) using (var b = new SolidBrush(It.Available ? Ui.Muted : Ui.Grey)) g.DrawString(Status, sf, b, statusR);
        }

        void Toggle()
        {
            if (!It.Available) return;
            It.Wanted = !It.Wanted;
            Invalidate();
            if (Changed != null) Changed();
        }
        protected override void OnClick(EventArgs e) { Focus(); Toggle(); base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Toggle(); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { focused = true; Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { focused = false; Invalidate(); base.OnLostFocus(e); }
    }

    enum StepState { Todo, Busy, Done, Warn, Failed }

    sealed class StepRow : Control
    {
        public StepState State;
        public string Title, Detail;
        public StepRow(string title) { Title = title; Detail = ""; DoubleBuffered = true; BackColor = Ui.Card; Height = Ui.S(56); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Hq(g); g.Clear(BackColor);
            float s = Ui.Sf(22), x = Ui.Sf(18), y = Ui.Sf(8);
            var r = new RectangleF(x, y, s, s);
            switch (State)
            {
                case StepState.Done:
                    using (var b = new SolidBrush(Ui.Accent)) g.FillEllipse(b, r);
                    Ui.Check(g, x, y, s, Color.White); break;
                case StepState.Warn:
                    using (var b = new SolidBrush(Ui.Warn)) g.FillEllipse(b, r);
                    using (var f = Ui.F(14, FontStyle.Bold)) { SizeF z = g.MeasureString("!", f); g.DrawString("!", f, Brushes.White, x + (s - z.Width) / 2, y + (s - z.Height) / 2); }
                    break;
                case StepState.Failed:
                    using (var b = new SolidBrush(Ui.Bad)) g.FillEllipse(b, r);
                    using (var pen = new Pen(Color.White, Ui.Sf(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    { g.DrawLine(pen, x + s * 0.33f, y + s * 0.33f, x + s * 0.67f, y + s * 0.67f); g.DrawLine(pen, x + s * 0.67f, y + s * 0.33f, x + s * 0.33f, y + s * 0.67f); }
                    break;
                case StepState.Busy:
                    using (var pen = new Pen(Ui.Line, Ui.Sf(3))) g.DrawEllipse(pen, x + Ui.Sf(2), y + Ui.Sf(2), s - Ui.Sf(4), s - Ui.Sf(4));
                    using (var pen = new Pen(Ui.Accent, Ui.Sf(3)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, x + Ui.Sf(2), y + Ui.Sf(2), s - Ui.Sf(4), s - Ui.Sf(4), (Environment.TickCount / 4) % 360, 110);
                    break;
                default:
                    using (var pen = new Pen(Ui.Line, Ui.Sf(2))) g.DrawEllipse(pen, x + Ui.Sf(2), y + Ui.Sf(2), s - Ui.Sf(4), s - Ui.Sf(4)); break;
            }
            var nf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
            using (var f = Ui.F(14.5f, FontStyle.Bold)) using (var b = new SolidBrush(State == StepState.Todo ? Ui.Grey : Ui.Ink))
                g.DrawString(Title, f, b, new RectangleF(Ui.Sf(54), Ui.Sf(8), Width - Ui.Sf(70), Ui.Sf(22)), nf);
            if (!string.IsNullOrEmpty(Detail))
                using (var f = Ui.F(13)) using (var b = new SolidBrush(State == StepState.Failed ? Ui.Bad : Ui.Muted))
                    g.DrawString(Detail, f, b, new RectangleF(Ui.Sf(54), Ui.Sf(31), Width - Ui.Sf(70), Ui.Sf(20)), nf);
        }
    }

    sealed class ProgressStrip : Control
    {
        public float Value;
        public ProgressStrip() { DoubleBuffered = true; Height = Ui.S(8); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Hq(g); g.Clear(Ui.Bg);
            using (var p = Ui.Round(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f)) using (var b = new SolidBrush(Ui.Line)) g.FillPath(b, p);
            float w = (Width - 1) * Math.Max(0, Math.Min(1, Value));
            if (w >= Height)
                using (var p = Ui.Round(new RectangleF(0, 0, w, Height - 1), Height / 2f)) using (var b = new SolidBrush(Ui.Accent)) g.FillPath(b, p);
        }
    }

    sealed class Badge : Control
    {
        readonly string n; readonly Color c;
        public Badge(string n, Color c, Color bg) { this.n = n; this.c = c; BackColor = bg; DoubleBuffered = true; Width = Height = Ui.S(22); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Ui.Hq(g); g.Clear(BackColor);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            using (var f = Ui.F(12, FontStyle.Bold)) { var sz = g.MeasureString(n, f); g.DrawString(n, f, Brushes.White, (Width - sz.Width) / 2 + 0.5f, (Height - sz.Height) / 2); }
        }
    }

    // ---- The window ----
    sealed class FixerForm : Form
    {
        const int W = 760;           // content width at 100 %
        readonly HeaderBar header;
        readonly Panel content, footer;
        readonly List<string> test;
        readonly string shotFile;
        string gameDir, notFoundError;
        Session session;
        Action rebuild;              // rebuilds the current screen (after a language switch)
        bool busy;

        // progress / result state
        List<StepRow> stepRows;
        Dictionary<Item, StepRow> rowOf;
        ProgressStrip bar;
        TextBox logBox;
        readonly System.Text.StringBuilder log = new System.Text.StringBuilder();
        System.Text.StringBuilder stepLog;
        StepRow current;
        ApplyResult result;
        Exception error;
        int undoCode, doneSteps;
        bool undoRun, anyWarn;
        System.Windows.Forms.Timer spin;

        public FixerForm(string gameDir, List<string> test)
        {
            this.test = test ?? new List<string>();
            using (Graphics g = CreateGraphics()) Ui.Scale = Math.Max(1f, g.DpiX / 96f);
            AutoScaleMode = AutoScaleMode.None;
            Text = "SWYF Community Fixer";
            Font = Ui.F(14);
            BackColor = Ui.Bg;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Icon ico = Ui.MakeIcon(); if (ico != null) Icon = ico;

            content = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Ui.Bg };
            footer = new Panel { Dock = DockStyle.Bottom, Height = Ui.S(74), BackColor = Ui.Bg };
            header = new HeaderBar();
            header.LanguageClicked = PickLanguage;
            Controls.Add(content); Controls.Add(footer); Controls.Add(header);
            content.BringToFront();
            ClientSize = new Size(Ui.S(W), Ui.S(600));

            shotFile = TestValue("--test-shot=");
            if (shotFile != null) { StartPosition = FormStartPosition.Manual; Location = new Point(0, 0); FormBorderStyle = FormBorderStyle.None; }
            string closeAfter = TestValue("--test-close-after=");
            if (closeAfter != null)
            {
                var t = new System.Windows.Forms.Timer { Interval = int.Parse(closeAfter) };
                t.Tick += delegate { t.Stop(); Close(); };
                t.Start();
            }

            string expand = TestValue("--test-expand=");
            if (expand != null) foreach (string k in expand.Split(',')) expanded.Add(k);
            this.gameDir = gameDir;
            Start();
        }

        string TestValue(string prefix)
        {
            foreach (string a in test) if (a.StartsWith(prefix, StringComparison.Ordinal)) return a.Substring(prefix.Length);
            return null;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            string click = TestValue("--test-click=");
            if (click == null) return;
            var t = new System.Windows.Forms.Timer { Interval = 400 };
            t.Tick += delegate
            {
                t.Stop();
                string tick = TestValue("--test-tick=");
                if (tick != null && session != null)
                    foreach (string k in tick.Split(',')) foreach (Item it in session.Items) if (it.Key == k && it.Available) it.Wanted = true;
                if (click == "apply" && session != null) StartApply(false);
                else if (click == "undo" && session != null) ShowUndoConfirm();
                else if (click == "undo-yes" && session != null) StartApply(true);
            };
            t.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;   // never stop halfway through a file change
            base.OnFormClosing(e);
        }

        // ---- screen plumbing ----
        void Begin()
        {
            content.SuspendLayout();
            foreach (Control c in new List<Control>(Controls_(content))) c.Dispose();
            content.Controls.Clear();
            foreach (Control c in new List<Control>(Controls_(footer))) c.Dispose();
            footer.Controls.Clear();
            content.AutoScrollPosition = new Point(0, 0);
            header.Enabled = !busy;
            header.Invalidate();
        }
        static IEnumerable<Control> Controls_(Control c) { foreach (Control x in c.Controls) yield return x; }

        void End(int contentHeight)
        {
            content.ResumeLayout();
            int want = header.Height + footer.Height + contentHeight + Ui.S(8);
            int max = Screen.FromControl(this).WorkingArea.Height - Ui.S(60);
            ClientSize = new Size(Ui.S(W), Math.Min(want, max));
            if (shotFile == null && Visible)
            {
                Rectangle wa = Screen.FromControl(this).WorkingArea;
                if (Bottom > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - Height);
            }
            if (shotFile != null)
                try { File.WriteAllText(shotFile + ".size", Width + "x" + Height); } catch { }
        }

        int Text_(Control parent, string text, int x, int y, int w, float size, FontStyle st, Color c)
        {
            if (string.IsNullOrEmpty(text)) return y;
            Font f = Ui.F(size, st);
            int h = Ui.TextHeight(text, f, Ui.S(w)) + Ui.S(2);
            var l = new Label { Text = text, Font = f, ForeColor = c, BackColor = Color.Transparent, AutoSize = false, Left = Ui.S(x), Top = y, Width = Ui.S(w), Height = h, UseMnemonic = false };
            parent.Controls.Add(l);
            return y + h;
        }

        void Buttons(Control left, params Control[] right)
        {
            int y = Ui.S(16);
            if (left != null) { left.Left = Ui.S(20); left.Top = y; footer.Controls.Add(left); }
            int x = Ui.S(W - 20);
            for (int i = right.Length - 1; i >= 0; i--)
            {
                x -= right[i].Width;
                right[i].Left = x; right[i].Top = y;
                footer.Controls.Add(right[i]);
                x -= Ui.S(10);
            }
        }

        FlatBtn CloseButton(bool primary)
        {
            var b = new FlatBtn(T("ui.close"), primary).Fit(110);
            b.Click += delegate { Close(); };
            return b;
        }

        void PickLanguage()
        {
            if (busy) return;
            var menu = new ContextMenuStrip { Font = Ui.F(14), ShowImageMargin = false };
            foreach (string code in LanguageCodes())
            {
                string c = code;
                var item = new ToolStripMenuItem(NativeName(c)) { Checked = c == LangCode };
                item.Click += delegate
                {
                    SetLanguage(c);
                    if (session != null) Describe(session);
                    header.Invalidate();
                    if (rebuild != null) rebuild();
                };
                menu.Items.Add(item);
            }
            menu.Show(header, header.PickerBottomLeft);
        }

        // ---- start: find the game, check it isn't running, detect ----
        void Start()
        {
            if (!DisclaimerAccepted()) { ShowDisclaimer(); return; }
            if (gameDir == null) gameDir = FindGameDir();
            if (!IsGameDir(gameDir)) { ShowNotFound(); return; }
            if (GameRunning()) { ShowRunning(); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                session = Detect(gameDir);
            }
            catch (Exception e) { error = e; session = null; ShowDone(); return; }
            finally { Cursor = Cursors.Default; }
            ShowMain();
        }

        // First run: who made this tool and that the developers don't support it. Must be accepted to go on.
        void ShowDisclaimer()
        {
            rebuild = ShowDisclaimer;
            Begin();
            int y = Ui.S(20);
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720), Fill = Ui.WarnSoft, Stripe = Ui.WarnLine };
            int cy = Ui.S(18);
            cy = Text_(c, T("disclaimer.title"), 20, cy, 680, 17, FontStyle.Bold, Ui.Ink) + Ui.S(10);
            foreach (string p in DisclaimerPoints())
            {
                Text_(c, "•", 22, cy, 12, 14, FontStyle.Regular, Ui.Ink);
                cy = Text_(c, p, 40, cy, 660, 14, FontStyle.Regular, Ui.Ink) + Ui.S(8);
            }
            cy = ThreadLink(c, 20, cy + Ui.S(2), 680) + Ui.S(12);
            c.Height = cy;
            content.Controls.Add(c);
            var accept = new FlatBtn(T("disclaimer.accept"), true).Fit(220);
            accept.Click += delegate { AcceptDisclaimer(); Start(); };
            Buttons(null, CloseButton(false), accept);
            End(c.Bottom + Ui.S(16));
        }

        void ShowNotFound()
        {
            rebuild = ShowNotFound;
            Begin();
            int y = Ui.S(20);
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720) };
            int cy = Ui.S(18);
            cy = Text_(c, T("ui.notfound_title"), 20, cy, 680, 17, FontStyle.Bold, Ui.Ink) + Ui.S(6);
            cy = Text_(c, T("ui.notfound_text"), 20, cy, 680, 13.5f, FontStyle.Regular, Ui.Muted) + Ui.S(14);
            var browse = new FlatBtn(T("ui.browse"), true).Fit(200);
            browse.Left = Ui.S(20); browse.Top = cy;
            browse.Click += delegate { Browse(); };
            c.Controls.Add(browse);
            cy = browse.Bottom + Ui.S(12);
            if (notFoundError != null) cy = Text_(c, notFoundError, 20, cy, 680, 13, FontStyle.Bold, Ui.Bad) + Ui.S(6);
            c.Height = cy + Ui.S(8);
            content.Controls.Add(c);
            Buttons(null, CloseButton(false));
            End(c.Bottom + Ui.S(20));
        }

        void Browse()
        {
            using (var d = new FolderBrowserDialog { Description = T("ui.browse_description"), ShowNewFolderButton = false })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string p = d.SelectedPath;
                string inside = Path.Combine(p, GameFolderName);
                if (!IsGameDir(p) && IsGameDir(inside)) p = inside;
                if (!IsGameDir(p)) { notFoundError = T("ui.notfound_wrong_folder", GameExe); ShowNotFound(); return; }
                notFoundError = null;
                gameDir = p;
                Start();
            }
        }

        void ShowRunning()
        {
            rebuild = ShowRunning;
            Begin();
            var c = new Card { Left = Ui.S(20), Top = Ui.S(20), Width = Ui.S(720), Stripe = Ui.WarnLine, Fill = Ui.WarnSoft };
            int cy = Ui.S(18);
            cy = Text_(c, T("ui.running_title"), 20, cy, 680, 17, FontStyle.Bold, Ui.Ink) + Ui.S(6);
            cy = Text_(c, T("ui.running_text"), 20, cy, 680, 13.5f, FontStyle.Regular, Ui.Muted) + Ui.S(16);
            c.Height = cy;
            content.Controls.Add(c);
            var again = new FlatBtn(T("ui.try_again"), true).Fit(130);
            again.Click += delegate { Start(); };
            Buttons(null, CloseButton(false), again);
            End(c.Bottom + Ui.S(20));
        }

        // Game folder + this PC, at the top of the main screen.
        int PcStrip(int y)
        {
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720) };
            string build = session.GameBuild != null ? "  ·  " + T("ui.build", session.GameBuild) : "";
            string path = session.GameDir;
            using (Font f = Ui.F(13.5f))
            {
                // shorten long paths in the middle: D:\SteamLibrary\...\Scam With Your Friends
                int maxW = Ui.S(430) - TextRenderer.MeasureText(build, f).Width;
                while (TextRenderer.MeasureText(path, f).Width > maxW && path.Length > 30)
                {
                    char sep = path.IndexOf('\\') >= 0 ? '\\' : '/';
                    string[] parts = path.Split(sep);
                    if (parts.Length <= 3) { path = "…" + path.Substring(path.Length - 28); break; }
                    var keep = new List<string>(parts);
                    int mid = keep.IndexOf("…");
                    if (mid < 0) { keep[1] = "…"; } else if (mid + 1 < keep.Count - 1) keep.RemoveAt(mid + 1); else break;
                    path = string.Join(sep.ToString(), keep.ToArray());
                }
            }
            string cpuName = assumePc != null ? assumePc.Split('|')[0] : ShortCpuName();
            string gpuName = ShortGpuName();
            string pc = cpuName ?? T("ui.unknown_cpu");
            if (gpuName != null) pc += "  ·  " + gpuName;
            Text_(c, T("ui.game").ToUpperInvariant(), 18, Ui.S(10), 420, 11, FontStyle.Bold, Ui.Muted);
            Text_(c, path + build, 18, Ui.S(27), 440, 13.5f, FontStyle.Regular, Ui.Ink);
            Text_(c, T("ui.your_pc").ToUpperInvariant(), 470, Ui.S(10), 240, 11, FontStyle.Bold, Ui.Muted);
            Text_(c, pc, 470, Ui.S(27), 240, 13.5f, FontStyle.Regular, Ui.Ink);
            c.Height = Ui.S(58);
            content.Controls.Add(c);
            return c.Bottom;
        }

        FlatBtn applyBtn;
        readonly HashSet<string> expanded = new HashSet<string>();   // fixes whose "What does this do?" is open

        void ShowMain()
        {
            rebuild = ShowMain;
            Begin();
            int y = PcStrip(Ui.S(16)) + Ui.S(16);
            y = Text_(content, T("ui.choose_title"), 22, y, 700, 17, FontStyle.Bold, Ui.Ink);
            y = Text_(content, T("ui.choose_subtitle"), 22, y + Ui.S(2), 716, 13, FontStyle.Regular, Ui.Muted) + Ui.S(10);

            var normal = new List<Item>();
            foreach (Item it in session.Items) if (!it.Experimental && it.Available) normal.Add(it);
            foreach (Item it in session.Items) if (!it.Experimental && !it.Available) normal.Add(it);
            y = RowsCard(normal, y, Ui.Card, Color.Empty, null) + Ui.S(14);

            var exp = new List<Item>();
            foreach (Item it in session.Items) if (it.Experimental && it.Available) exp.Add(it);
            foreach (Item it in session.Items) if (it.Experimental && !it.Available && it.ShowAlways) exp.Add(it);
            if (exp.Count > 0) y = RowsCard(exp, y, Ui.WarnSoft, Ui.WarnLine, "⚠  " + T("ui.experimental")) + Ui.S(12);

            bool anyUntested = false;
            foreach (Item it in session.Items) if (it.Untested && it.Available) anyUntested = true;
            if (anyUntested) y = Text_(content, T("ui.untested_note"), 22, y, 716, 12.5f, FontStyle.Regular, Ui.Muted) + Ui.S(4);
            string tn = TranslatorNote();
            if (tn != "") y = Text_(content, tn, 22, y, 716, 12.5f, FontStyle.Italic, Ui.Muted) + Ui.S(4);
            y = Text_(content, T("ui.disclaimer"), 22, y + Ui.S(4), 716, 12.5f, FontStyle.Regular, Ui.Muted) + Ui.S(2);
            y = ThreadLink(content, 22, y + Ui.S(2)) + Ui.S(4);

            var undo = new FlatBtn(T("ui.undo_all"), false).Fit(150);
            undo.Click += delegate { ShowUndoConfirm(); };
            applyBtn = new FlatBtn("", true);
            applyBtn.Click += delegate { StartApply(false); };
            UpdateApply();
            Buttons(undo, CloseButton(false), applyBtn);
            End(y + Ui.S(8));
        }

        int RowsCard(List<Item> items, int y, Color fill, Color stripe, string title)
        {
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720), Fill = fill, Stripe = stripe };
            int cy = Ui.S(6);
            if (title != null) cy = Text_(c, title, 16, Ui.S(10), 600, 12, FontStyle.Bold, Ui.Warn) - Ui.S(4);
            for (int i = 0; i < items.Count; i++)
            {
                var row = new FixRow(items[i], fill) { Left = 1, Top = cy, Width = Ui.S(718) };
                row.FitHeight(row.Width);
                row.Changed = UpdateApply;
                c.Controls.Add(row);
                cy += row.Height;
                cy = RowExtras(c, items[i], cy);
                if (i < items.Count - 1)
                {
                    c.Controls.Add(new Panel { Left = Ui.S(48), Top = cy, Width = Ui.S(650), Height = 1, BackColor = Ui.Line });
                    cy += 1;
                }
            }
            c.Height = cy + Ui.S(6);
            content.Controls.Add(c);
            return c.Bottom;
        }

        // Under each fix: buttons for what the player still has to do, and "What does this do?" with a short explanation.
        int RowExtras(Card c, Item it, int cy)
        {
            int top = cy - Ui.S(4);
            int x = Ui.S(48);
            if (it.NeedDownload)
            {
                var b = new FlatBtn(T("ui.open_download"), true).Fit(160);
                b.Height = Ui.S(30); b.Left = x; b.Top = top;
                b.Click += delegate { OpenUrl(ReleasesUrl); };
                c.Controls.Add(b); b.BringToFront(); x = b.Right + Ui.S(10);
            }
            if (it.NeedCpuOption)
            {
                var copy = new FlatBtn(T("ui.copy_cpu_option"), !it.NeedDownload).Fit(170);
                copy.Height = Ui.S(30); copy.Left = x; copy.Top = top;
                copy.Click += delegate { try { Clipboard.SetText("-whisper-cpu"); copy.Text = T("ui.copied"); } catch { } };
                var steam = new FlatBtn(T("ui.open_steam"), false).Fit(120);
                steam.Height = Ui.S(30); steam.Left = copy.Right + Ui.S(10); steam.Top = top;
                steam.Click += delegate { OpenUrl("steam://open/games"); };
                c.Controls.Add(copy); c.Controls.Add(steam); copy.BringToFront(); steam.BringToFront();
                x = steam.Right + Ui.S(10);
            }
            if (it.NeedDownload || it.NeedCpuOption)
            {
                // After downloading the file or adding the launch option: look again without restarting the tool.
                var again = new FlatBtn(T("ui.check_again"), false).Fit(120);
                again.Height = Ui.S(30);
                again.Left = x;
                if (again.Right > Ui.S(700)) { top += Ui.S(38); again.Left = Ui.S(48); }   // long translations: next line
                again.Top = top;
                again.Click += delegate { session = null; Start(); };
                c.Controls.Add(again); again.BringToFront();
                top += Ui.S(30) + Ui.S(8);
            }

            bool open = expanded.Contains(it.Key);
            var link = new Label { Text = T("ui.what_does_this_do") + (open ? " ▴" : " ▾"), Font = Ui.F(12.5f), ForeColor = Ui.Blue, AutoSize = true, MaximumSize = new Size(Ui.S(650), 0), Left = Ui.S(48), Top = top, Cursor = Cursors.Hand, BackColor = Color.Transparent };
            string key = it.Key;
            link.Click += delegate
            {
                if (!expanded.Remove(key)) expanded.Add(key);
                int scroll = -content.AutoScrollPosition.Y;
                ShowMain();
                content.AutoScrollPosition = new Point(0, scroll);
            };
            c.Controls.Add(link);
            link.BringToFront();
            int y = top + Ui.S(22);
            if (open)
            {
                y = Text_(c, T("ui.info_fixes") + " " + T("info." + key + ".fixes"), 48, y, 650, 12.5f, FontStyle.Regular, Ui.Muted);
                y = Text_(c, T("ui.info_changes") + " " + T("info." + key + ".changes"), 48, y + Ui.S(2), 650, 12.5f, FontStyle.Regular, Ui.Muted);
                y = Text_(c, T("ui.info_undo") + " " + T("info." + key + ".undo"), 48, y + Ui.S(2), 650, 12.5f, FontStyle.Regular, Ui.Muted);
            }
            return y + Ui.S(8);
        }

        // "Questions about this tool? Tiger's fixer thread" link (not the developers' support).
        int ThreadLink(Control parent, int x, int y) { return ThreadLink(parent, x, y, 716); }
        int ThreadLink(Control parent, int x, int y, int w)
        {
            string text = T("ui.thread_link");
            Font f = Ui.F(12.5f);
            int h = Ui.TextHeight(text, f, Ui.S(w)) + Ui.S(2);
            var l = new Label { Text = text, Font = f, ForeColor = Ui.Blue, AutoSize = false, Width = Ui.S(w), Height = h, Left = Ui.S(x), Top = y, Cursor = Cursors.Hand, BackColor = Color.Transparent, UseMnemonic = false };
            l.Click += delegate { OpenUrl(ModThreadUrl); };
            parent.Controls.Add(l);
            return y + h;
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        int ChangeCount()
        {
            int n = 0;
            foreach (Item it in session.Items) if (it.IsChange) n++;
            return n;
        }

        void UpdateApply()
        {
            if (applyBtn == null) return;
            int n = ChangeCount();
            applyBtn.Text = n == 0 ? T("ui.nothing_to_change") : n == 1 ? T("ui.apply_one") : T("ui.apply_n", n);
            applyBtn.Enabled = n > 0;
            int right = applyBtn.Right;
            applyBtn.Fit(150);
            if (right > 0) applyBtn.Left = right - applyBtn.Width;
        }

        void ShowUndoConfirm()
        {
            rebuild = ShowUndoConfirm;
            Begin();
            var c = new Card { Left = Ui.S(20), Top = Ui.S(20), Width = Ui.S(720), Stripe = Ui.BadLine, Fill = Ui.BadSoft };
            int cy = Ui.S(18);
            cy = Text_(c, T("ui.undo_title"), 20, cy, 680, 17, FontStyle.Bold, Ui.Ink) + Ui.S(6);
            cy = Text_(c, T("ui.undo_text"), 20, cy, 680, 13.5f, FontStyle.Regular, Ui.Ink) + Ui.S(8);
            cy = Text_(c, T("ui.undo_launch_note"), 20, cy, 680, 13, FontStyle.Regular, Ui.Muted) + Ui.S(16);
            c.Height = cy;
            content.Controls.Add(c);
            var cancel = new FlatBtn(T("ui.cancel"), false).Fit(110);
            cancel.Click += delegate { ShowMain(); };
            var yes = new FlatBtn(T("ui.undo_yes"), false, true).Fit(180);
            yes.Click += delegate { StartApply(true); };
            Buttons(null, cancel, yes);
            End(c.Bottom + Ui.S(20));
        }

        // ---- progress ----
        void StartApply(bool undo)
        {
            if (GameRunning()) { ShowRunning(); return; }
            undoRun = undo; result = null; error = null; undoCode = 0; doneSteps = 0; anyWarn = false;
            log.Length = 0;
            stepRows = new List<StepRow>();
            rowOf = new Dictionary<Item, StepRow>();
            if (undo) stepRows.Add(new StepRow(T("ui.undo_step")));
            else
                foreach (Item it in session.Items)
                    if (it.IsChange)
                    {
                        var r = new StepRow(it.IsTest ? it.Name : (it.Wanted ? it.Name : T("ui.remove_step", it.Name)));
                        stepRows.Add(r); rowOf[it] = r;
                    }
            stepRows.Add(new StepRow(T("ui.finish_step")));
            busy = true;
            ShowProgress();

            Sink = delegate(string s) { BeginInvoke((Action)delegate { AddLog(s); }); };
            OnStepBegin = delegate(Item it) { BeginInvoke((Action)delegate { StepBegin(it); }); };
            OnStepEnd = delegate(Item it, bool ok) { BeginInvoke((Action)delegate { StepEnd(it, ok); }); };
            var th = new Thread(Worker) { IsBackground = true };
            th.Start();
        }

        void Worker()
        {
            try
            {
                if (undoRun)
                {
                    BeginInvoke((Action)delegate { SetBusy(stepRows[0], T("ui.working")); });
                    DisableOldBepInEx(session.GameDir);
                    undoCode = UndoAll(session.GameDir, session.ModelDir, session.Dll);
                    BeginInvoke((Action)delegate { Finish(stepRows[0], undoCode == 0 ? StepState.Done : StepState.Warn); });
                }
                else result = Apply(session, true);
            }
            catch (Exception e) { error = e; }
            finally
            {
                Sink = null; OnStepBegin = null; OnStepEnd = null;
                BeginInvoke((Action)WorkerDone);
            }
        }

        void WorkerDone()
        {
            busy = false;
            if (spin != null) { spin.Stop(); spin = null; }
            if (error != null && current != null) { current.State = StepState.Failed; current.Detail = error.Message; current.Invalidate(); }
            ShowDone();
        }

        void ShowProgress()
        {
            rebuild = null;
            Begin();
            int y = Ui.S(18);
            y = Text_(content, T(undoRun ? "ui.undoing_title" : "ui.applying_title"), 22, y, 700, 17, FontStyle.Bold, Ui.Ink);
            y = Text_(content, T("ui.applying_subtitle"), 22, y + Ui.S(2), 700, 13, FontStyle.Regular, Ui.Muted) + Ui.S(12);
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720) };
            int cy = Ui.S(8);
            foreach (StepRow r in stepRows) { r.Left = 1; r.Top = cy; r.Width = Ui.S(718); c.Controls.Add(r); cy += r.Height; }
            c.Height = cy + Ui.S(8);
            content.Controls.Add(c);
            y = c.Bottom + Ui.S(18);
            bar = new ProgressStrip { Left = Ui.S(20), Top = y, Width = Ui.S(720) };
            content.Controls.Add(bar);
            y = bar.Bottom + Ui.S(12);
            y = DetailsToggle(y, false);
            var close = CloseButton(false); close.Enabled = false;
            Buttons(null, close);
            End(y + Ui.S(10));
            spin = new System.Windows.Forms.Timer { Interval = 50 };
            spin.Tick += delegate { if (current != null && current.State == StepState.Busy) current.Invalidate(); };
            spin.Start();
        }

        // "Show details" link + the full log (selectable, for screenshots/copying into Discord).
        int DetailsToggle(int y, bool open)
        {
            var link = new Label { Text = T(open ? "ui.hide_details" : "ui.show_details") + (open ? " ▴" : " ▾"), Font = Ui.F(13), ForeColor = Ui.Blue, AutoSize = true, Left = Ui.S(22), Top = y, Cursor = Cursors.Hand, BackColor = Color.Transparent };
            content.Controls.Add(link);
            var copy = new Label { Text = T("ui.copy_details"), Font = Ui.F(13), ForeColor = Ui.Blue, AutoSize = true, Top = y, Cursor = Cursors.Hand, BackColor = Color.Transparent, Visible = open };
            content.Controls.Add(copy);
            copy.Left = link.Right + Ui.S(18);
            logBox = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Left = Ui.S(20), Top = y + Ui.S(26), Width = Ui.S(720), Height = Ui.S(160),
                Font = new Font(FontFamily.GenericMonospace, Ui.Sf(12), GraphicsUnit.Pixel), BackColor = Color.White,
                Text = log.ToString(), Visible = open
            };
            content.Controls.Add(logBox);
            link.Click += delegate
            {
                logBox.Visible = !logBox.Visible;
                copy.Visible = logBox.Visible;
                link.Text = T(logBox.Visible ? "ui.hide_details" : "ui.show_details") + (logBox.Visible ? " ▴" : " ▾");
                if (logBox.Visible)
                {
                    int need = header.Height + footer.Height + logBox.Bottom - content.AutoScrollPosition.Y + Ui.S(16);
                    int max = Screen.FromControl(this).WorkingArea.Height - Ui.S(60);
                    if (ClientSize.Height < need) ClientSize = new Size(ClientSize.Width, Math.Min(need, max));
                    logBox.SelectionStart = logBox.TextLength; logBox.ScrollToCaret(); content.ScrollControlIntoView(logBox);
                }
            };
            copy.Click += delegate
            {
                try { Clipboard.SetText(log.ToString()); copy.Text = T("ui.copied"); } catch { }
            };
            return y + Ui.S(26) + (open ? Ui.S(170) : 0);
        }

        void AddLog(string s)
        {
            log.Append(s);
            if (stepLog != null) stepLog.Append(s);
            if (logBox != null && !logBox.IsDisposed) logBox.AppendText(s.Replace("\r\n", "\n").Replace("\n", "\r\n"));
        }

        void SetBusy(StepRow r, string detail)
        {
            current = r; r.State = StepState.Busy; r.Detail = detail; r.Invalidate();
        }

        void StepBegin(Item it)
        {
            StepRow r;
            if (!rowOf.TryGetValue(it, out r)) return;
            stepLog = new System.Text.StringBuilder();
            SetBusy(r, it.IsTest ? T("ui.gpu_busy") : T("ui.working"));
        }

        void StepEnd(Item it, bool ok)
        {
            StepRow r;
            if (!rowOf.TryGetValue(it, out r)) return;
            if (!ok && !it.IsTest) anyWarn = true;
            Finish(r, ok ? StepState.Done : StepState.Warn);
            r.Detail = Condense(stepLog != null ? stepLog.ToString() : "");
            stepLog = null;
        }

        void Finish(StepRow r, StepState st)
        {
            r.State = st; r.Invalidate();
            doneSteps++;
            if (bar != null) { bar.Value = (float)doneSteps / Math.Max(1, stepRows.Count - 1); bar.Invalidate(); }
        }

        // The step's log lines in one short line: "ggml-base.en.bin converted (3.6 s) · listening window shortened"
        static string Condense(string s)
        {
            var parts = new List<string>();
            foreach (string raw in s.Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("[")) continue;
                parts.Add(l);
            }
            string j = string.Join("  ·  ", parts.ToArray());
            return j.Length > 160 ? j.Substring(0, 157) + "…" : j;
        }

        // ---- done ----
        void ShowDone()
        {
            rebuild = null;   // texts on this screen come from the run; don't rebuild in another language
            Begin();
            int y = Ui.S(16);
            if (error != null)
            {
                y = StatusCard(y, Ui.BadSoft, Ui.BadLine, StepState.Failed, Ui.Bad, T("ui.error_title"), error.Message + "\n" + T("ui.error_text"));
            }
            else if (undoRun)
            {
                y = undoCode == 0
                    ? StatusCard(y, Ui.AccentSoft, Ui.AccentLine, StepState.Done, Ui.Accent, T("ui.undone_title"), T("ui.undone_text"))
                    : StatusCard(y, Ui.WarnSoft, Ui.WarnLine, StepState.Warn, Ui.Warn, T("ui.undone_partly_title"), T("ui.undone_partly_text"));
            }
            else
            {
                int changes = 0;
                foreach (KeyValuePair<Item, StepRow> kv in rowOf)
                    if (!kv.Key.IsTest && (kv.Value.State == StepState.Done || kv.Value.State == StepState.Warn)) changes++;
                string title = changes == 0 ? T("ui.done_title_none") : changes == 1 ? T("ui.done_title_one") : T("ui.done_title", changes);
                y = anyWarn
                    ? StatusCard(y, Ui.WarnSoft, Ui.WarnLine, StepState.Warn, Ui.Warn, T("ui.done_warn_title"), T("ui.done_warn_text"))
                    : StatusCard(y, Ui.AccentSoft, Ui.AccentLine, StepState.Done, Ui.Accent, title, T("ui.done_text"));
                if (result != null && result.Reason != CpuReason.None && !result.HasCpuOption) y = LaunchOptionCard(y + Ui.S(14));
                else if (result != null && result.GpuProblem) y = InfoCard(y + Ui.S(14), T("ui.cpu_option_already"), LayerNote());
                if (result != null && result.BenchLines != null && result.BenchLines.Count > 0)
                    y = InfoCard(y + Ui.S(14), T("ui.bench_title"), string.Join("\n", result.BenchLines.ToArray()));
                if (result != null && result.Probe != null && result.Probe.Outcome == ProbeOutcome.LoadFailed)
                    y = InfoCard(y + Ui.S(14), T("ui.loadfail_title"), T("ui.loadfail_text", result.Probe.Detail));
                y = TipsCard(y + Ui.S(14));
            }
            y = DetailsToggle(y + Ui.S(14), error != null || (undoRun && undoCode != 0));
            y = ThreadLink(content, 22, y + Ui.S(6));
            var again = new FlatBtn(T("ui.change_fixes"), false).Fit(150);
            again.Click += delegate { session = null; Start(); };
            if (session != null && !undoRun)
            {
                var report = new FlatBtn(T("ui.copy_report"), false).Fit(190);
                report.Click += delegate
                {
                    try { Clipboard.SetText(BuildReport(session, result)); report.Text = T("ui.copied"); } catch { }
                };
                Buttons(again, report, CloseButton(true));
            }
            else Buttons(again, CloseButton(true));
            End(y + Ui.S(10));
        }

        int StatusCard(int y, Color fill, Color line, StepState icon, Color ink, string title, string text)
        {
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720), Fill = fill, Stripe = line };
            var ic = new StepRow("") { State = icon, Left = 0, Top = Ui.S(12), Width = Ui.S(48), Height = Ui.S(34), BackColor = fill };
            c.Controls.Add(ic);
            int cy = Ui.S(14);
            cy = Text_(c, title, 54, cy, 640, 16, FontStyle.Bold, ink);
            cy = Text_(c, text, 54, cy + Ui.S(2), 640, 13, FontStyle.Regular, Ui.Ink) + Ui.S(14);
            c.Height = cy;
            content.Controls.Add(c);
            return c.Bottom;
        }

        int InfoCard(int y, string title, string text)
        {
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720), Fill = Ui.WarnSoft, Stripe = Ui.WarnLine };
            int cy = Ui.S(14);
            cy = Text_(c, title, 20, cy, 680, 14.5f, FontStyle.Bold, Ui.Ink);
            if (text != null) cy = Text_(c, text, 20, cy + Ui.S(4), 680, 13, FontStyle.Regular, Ui.Muted);
            c.Height = cy + Ui.S(14);
            content.Controls.Add(c);
            return c.Bottom;
        }

        int LaunchOptionCard(int y)
        {
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720), Fill = Ui.WarnSoft, Stripe = Ui.WarnLine };
            int cy = Ui.S(14);
            cy = Text_(c, T("ui.one_more_step").ToUpperInvariant(), 20, cy, 600, 11.5f, FontStyle.Bold, Ui.Warn) + Ui.S(2);
            string title = result.Reason == CpuReason.GpuTest ? T("ui.launch_title")
                : result.Reason == CpuReason.Language || result.Reason == CpuReason.Bench ? T("ui.launch_title_language") : T("ui.launch_title_speech");
            cy = Text_(c, title, 20, cy, 680, 16, FontStyle.Bold, Ui.Ink);
            cy = Text_(c, CpuReasonText(result.Reason, session, result), 20, cy + Ui.S(2), 680, 13, FontStyle.Regular, Ui.Muted) + Ui.S(10);
            string[] steps = { T("ui.launch_step1"), T("ui.launch_step2"), T("ui.launch_step3") };
            for (int i = 0; i < steps.Length; i++)
            {
                var b = new Badge((i + 1).ToString(), Ui.Warn, Ui.WarnSoft) { Left = Ui.S(20), Top = cy };
                c.Controls.Add(b);
                int ty = Text_(c, steps[i], 52, cy + Ui.S(1), 640, 14, FontStyle.Regular, Ui.Ink);
                cy = Math.Max(ty, b.Bottom) + Ui.S(8);
                if (i == 1)
                {
                    if (!string.IsNullOrEmpty(result.Launch))
                        cy = Text_(c, T("ui.launch_existing", result.Launch), 52, cy - Ui.S(4), 640, 12.5f, FontStyle.Regular, Ui.Muted) + Ui.S(6);
                    var code = new Card { Left = Ui.S(52), Top = cy, Width = Ui.S(200), Height = Ui.S(34) };
                    code.Controls.Add(new Label { Text = "-whisper-cpu", Font = Ui.Mono(14), ForeColor = Ui.Ink, BackColor = Color.Transparent, AutoSize = false, Left = Ui.S(14), Top = Ui.S(8), Width = Ui.S(180), Height = Ui.S(20) });
                    c.Controls.Add(code);
                    var copy = new FlatBtn(T("ui.copy"), true).Fit(80);
                    copy.Height = Ui.S(34); copy.Left = code.Right + Ui.S(10); copy.Top = cy;
                    copy.Click += delegate { try { Clipboard.SetText("-whisper-cpu"); copy.Text = T("ui.copied"); } catch { } };
                    var steam = new FlatBtn(T("ui.open_steam"), false).Fit(120);
                    steam.Height = Ui.S(34); steam.Left = copy.Right + Ui.S(10); steam.Top = cy;
                    steam.Click += delegate { try { Process.Start(new ProcessStartInfo("steam://open/games") { UseShellExecute = true }); } catch { } };
                    c.Controls.Add(copy); c.Controls.Add(steam);
                    cy = code.Bottom + Ui.S(10);
                }
            }
            if (result.KeepHint) cy = Text_(c, T("ui.keep_hint"), 20, cy + Ui.S(2), 680, 13, FontStyle.Regular, Ui.Muted) + Ui.S(4);
            string layers = LayerNote();
            if (layers != null) cy = Text_(c, layers, 20, cy + Ui.S(2), 680, 12.5f, FontStyle.Regular, Ui.Muted) + Ui.S(4);
            c.Height = cy + Ui.S(8);
            content.Controls.Add(c);
            return c.Bottom;
        }

        // After a failed graphics card test: what the second test without Vulkan add-on layers showed. null = no retest.
        string LayerNote()
        {
            if (result == null || result.ProbeNoLayers == null) return null;
            string n = result.ProbeNoLayers.Outcome == ProbeOutcome.Passed ? T("ui.retest_passed") : T("ui.retest_failed");
            if (result.Layers != null && result.Layers.Count > 0) n += " " + T("core.layers_on_pc", string.Join(", ", result.Layers.ToArray()));
            return n;
        }

        int TipsCard(int y)
        {
            string intro;
            List<string> tips = Tips(session, result, out intro);
            if (result != null && result.Probe != null && result.Probe.Outcome == ProbeOutcome.Passed && result.HasCpuOption)
                tips.Insert(0, CpuOptionHelps(session) == CpuReason.None ? T("ui.tip_remove_cpu_option") : T("ui.tip_keep_cpu_option"));
            var c = new Card { Left = Ui.S(20), Top = y, Width = Ui.S(720) };
            int cy = Ui.S(14);
            cy = Text_(c, T("ui.tips_title").ToUpperInvariant(), 20, cy, 600, 11.5f, FontStyle.Bold, Ui.Muted) + Ui.S(4);
            if (intro != null) cy = Text_(c, intro, 20, cy, 680, 13.5f, FontStyle.Regular, Ui.Ink) + Ui.S(2);
            foreach (string t in tips)
            {
                Text_(c, "•", 22, cy, 12, 13.5f, FontStyle.Regular, Ui.Ink);
                cy = Text_(c, t, 38, cy, 662, 13.5f, FontStyle.Regular, Ui.Ink) + Ui.S(4);
            }
            c.Height = cy + Ui.S(10);
            content.Controls.Add(c);
            return c.Bottom;
        }
    }
}
