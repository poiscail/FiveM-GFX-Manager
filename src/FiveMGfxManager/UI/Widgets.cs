using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FiveMGfxManager
{
    // Contrôles dessinés à la main pour le thème sombre (coins arrondis, survol, icônes)

    enum ButtonKind { Primary, Secondary, Danger, Ghost, Nav }

    class FlatButton : Control
    {
        public ButtonKind Kind;
        public string Glyph;
        bool hover, down, selected;
        Font iconFont;

        public FlatButton(string text, string glyph = null, ButtonKind kind = ButtonKind.Secondary)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Text = text;
            Glyph = glyph;
            Kind = kind;
            Cursor = Cursors.Hand;
            TabStop = true;
            Height = kind == ButtonKind.Nav ? 42 : 36;
            Padding = new Padding(14, 0, 14, 0);
            Margin = new Padding(0, 0, 8, 0);
        }

        public bool Selected
        {
            get => selected;
            set { selected = value; Invalidate(); }
        }

        float K => DeviceDpi / 96f;
        Font IconFont => iconFont ?? (iconFont = Theme.Icons(Font.SizeInPoints));
        bool HasGlyph => !string.IsNullOrEmpty(Glyph) && IconFont != null;

        public override Size GetPreferredSize(Size proposedSize)
        {
            int w = Padding.Horizontal + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            if (HasGlyph) w += GlyphWidth + (Text.Length > 0 ? (int)(9 * K) : 0);
            return new Size(w, Height);
        }

        int GlyphWidth => TextRenderer.MeasureText(Glyph, IconFont, Size.Empty, TextFormatFlags.NoPadding).Width;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var parent = Parent?.BackColor ?? Theme.Bg;
            g.Clear(parent);

            Color? back = null, border = null;
            Color fore = Theme.Fg, glyph = Theme.Fg;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    back = down ? Theme.Mix(Theme.Accent, Color.Black, 0.15f)
                         : hover ? Theme.Mix(Theme.Accent, Color.White, 0.15f) : Theme.Accent;
                    fore = glyph = Theme.OnAccent;
                    break;
                case ButtonKind.Danger:
                    back = Theme.Mix(parent, Theme.Danger, down ? 0.36f : hover ? 0.28f : 0.16f);
                    fore = glyph = Theme.Mix(Theme.Danger, Color.White, 0.2f);
                    break;
                case ButtonKind.Secondary:
                    back = down ? Theme.Field : hover ? Theme.Hover : Theme.Field;
                    border = hover ? Theme.Mix(Theme.FieldBorder, Color.White, 0.12f) : Theme.FieldBorder;
                    glyph = Theme.Muted;
                    break;
                case ButtonKind.Ghost:
                    if (hover) back = Theme.Mix(parent, Color.White, down ? 0.04f : 0.07f);
                    fore = hover ? Theme.Fg : Theme.Muted;
                    glyph = Theme.Muted;
                    break;
                case ButtonKind.Nav:
                    if (selected) back = Theme.Mix(parent, Theme.Accent, 0.13f);
                    else if (hover) back = Theme.Mix(parent, Color.White, 0.05f);
                    fore = selected || hover ? Theme.Fg : Theme.Muted;
                    glyph = selected ? Theme.Accent : Theme.Muted;
                    break;
            }
            if (!Enabled)
            {
                if (back != null) back = Theme.Mix(back.Value, parent, 0.45f);
                fore = Theme.Mix(fore, back ?? parent, 0.55f);
                glyph = Theme.Mix(glyph, back ?? parent, 0.55f);
            }

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = (int)(8 * K);
            if (back != null) Theme.FillRound(g, r, radius, back.Value, border);
            if (Kind == ButtonKind.Nav && selected)
                Theme.FillRound(g, new Rectangle(0, Height / 4, (int)(3 * K), Height / 2), (int)(2 * K), Theme.Accent);
            if (Focused && ShowFocusCues)
                Theme.FillRound(g, r, radius, Color.Transparent, Theme.Accent);

            bool left = Kind == ButtonKind.Nav;
            int textW = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            int glyphW = HasGlyph ? GlyphWidth : 0;
            int gap = HasGlyph && Text.Length > 0 ? (int)(9 * K) : 0;
            int x = left ? Padding.Left : Math.Max(Padding.Left, (Width - glyphW - gap - textW) / 2);
            if (HasGlyph)
            {
                TextRenderer.DrawText(g, Glyph, IconFont, new Rectangle(x, 0, glyphW, Height), glyph,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                x += glyphW + gap;
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.FromLTRB(x, 0, Width - Padding.Right / 2, Height), fore,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                                  TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { down = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnFontChanged(EventArgs e)
        {
            iconFont = null;
            base.OnFontChanged(e);
        }
    }

    // Panneau à coins arrondis
    class Card : Panel
    {
        public Card()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Card;
            Padding = new Padding(18);
            Margin = new Padding(0);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
            Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), (int)(10 * DeviceDpi / 96f),
                            BackColor, Theme.Border);
        }
    }

    // Zone de texte arrondie (avec texte d'aide quand elle est vide)
    class InputBox : Panel
    {
        public readonly TextBox Box = new TextBox();
        string placeholder;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, string lParam);

        public InputBox()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Theme.Field;
            Height = 36;
            Margin = new Padding(0, 0, 8, 0);
            Cursor = Cursors.IBeam;
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Field;
            Box.ForeColor = Theme.Fg;
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.HandleCreated += (s, e) => ApplyPlaceholder();
            Controls.Add(Box);
            Click += (s, e) => Box.Focus();
        }

        public string Placeholder
        {
            get => placeholder;
            set { placeholder = value; ApplyPlaceholder(); }
        }

        void ApplyPlaceholder()
        {
            if (Box.IsHandleCreated && placeholder != null)
                try { SendMessage(Box.Handle, 0x1501, (IntPtr)1, placeholder); } catch { } // EM_SETCUEBANNER
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = (int)(11 * DeviceDpi / 96f);
            Box.SetBounds(pad, (Height - Box.Height) / 2, Math.Max(10, Width - 2 * pad), Box.Height);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Bg);
            Theme.FillRound(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), (int)(7 * DeviceDpi / 96f),
                            Theme.Field, Box.Focused ? Theme.Accent : Theme.FieldBorder);
        }
    }

    // Choix parmi quelques valeurs (boutons collés)
    class Segmented : Control
    {
        readonly string[] items;
        int selected, hover = -1;

        public Segmented(string[] items)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            this.items = items;
            Height = 36;
            Cursor = Cursors.Hand;
            TabStop = true;
            Margin = new Padding(0, 0, 12, 0);
        }

        public string SelectedItem
        {
            get => items[selected];
            set { selected = Math.Max(0, Array.IndexOf(items, value)); Invalidate(); }
        }

        float K => DeviceDpi / 96f;
        int Inset => (int)(3 * K);

        int[] Widths() => items.Select(t => TextRenderer.MeasureText(t, Font, Size.Empty, TextFormatFlags.NoPadding).Width
                                            + (int)(40 * K)).ToArray();

        public override Size GetPreferredSize(Size proposedSize) => new Size(Widths().Sum() + 2 * Inset, Height);

        Rectangle ItemRect(int i)
        {
            var w = Widths();
            return new Rectangle(Inset + w.Take(i).Sum(), Inset, w[i], Height - 2 * Inset - 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            Theme.FillRound(g, new Rectangle(0, 0, Width - 1, Height - 1), (int)(8 * K), Theme.Field,
                            Focused && ShowFocusCues ? Theme.Accent : Theme.FieldBorder);
            for (int i = 0; i < items.Length; i++)
            {
                var r = ItemRect(i);
                bool sel = i == selected;
                if (sel) Theme.FillRound(g, r, (int)(6 * K), Theme.Hover, Theme.Mix(Theme.Hover, Color.White, 0.08f));
                else if (i == hover) Theme.FillRound(g, r, (int)(6 * K), Theme.Mix(Theme.Field, Color.White, 0.04f));
                int dot = (int)(7 * K);
                var color = Theme.TypeColor(items[i]);
                using (var b = new SolidBrush(sel ? color : Theme.Mix(color, Theme.Field, 0.5f)))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(b, r.X + (int)(14 * K), r.Y + (r.Height - dot) / 2, dot, dot);
                }
                TextRenderer.DrawText(g, items[i], Font, Rectangle.FromLTRB(r.X + (int)(27 * K), r.Y, r.Right, r.Bottom),
                                      sel ? Theme.Fg : Theme.Muted,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < items.Length; i++)
                if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int h = HitTest(e.Location);
            if (h >= 0) { selected = h; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) selected = Math.Max(0, selected - 1);
            if (e.KeyCode == Keys.Right) selected = Math.Min(items.Length - 1, selected + 1);
            Invalidate();
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // Interrupteur on / off avec son texte
    class Toggle : Control
    {
        bool on;

        public Toggle(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Text = text;
            Height = 30;
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        public bool Checked
        {
            get => on;
            set { on = value; Invalidate(); }
        }

        float K => DeviceDpi / 96f;

        public override Size GetPreferredSize(Size proposedSize) =>
            new Size((int)(50 * K) + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width, Height);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            int h = (int)(20 * K), w = (int)(38 * K);
            var track = new Rectangle(0, (Height - h) / 2, w, h);
            Theme.FillRound(g, track, h / 2, on ? Theme.Accent : Theme.FieldBorder,
                            Focused && ShowFocusCues ? Theme.Fg : (Color?)null);
            int knob = h - (int)(6 * K);
            using (var b = new SolidBrush(on ? Theme.OnAccent : Theme.Fg))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(b, on ? track.Right - knob - (int)(3 * K) : track.X + (int)(3 * K), track.Y + (h - knob) / 2, knob, knob);
            }
            TextRenderer.DrawText(g, Text, Font, Rectangle.FromLTRB(w + (int)(12 * K), 0, Width, Height), Theme.Fg,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) Checked = !Checked;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // Barre de progression fine
    class SlimProgress : Control
    {
        int value;
        public int Maximum = 1000;

        public SlimProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            Height = 6;
        }

        public int Value
        {
            get => value;
            set { this.value = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Bg);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Height / 2, Theme.Field);
            int w = (int)((long)(Width - 1) * value / Math.Max(1, Maximum));
            if (w > 0) Theme.FillRound(g, new Rectangle(0, 0, Math.Max(w, Height), Height - 1), Height / 2, Theme.Accent);
        }
    }

    // Pastille : type d'un pack, état…
    class Badge : Control
    {
        Color color = Theme.Muted;

        public Badge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            Margin = new Padding(0, 0, 6, 0);
            AutoSize = true;
        }

        public void Set(string text, Color c)
        {
            Text = text;
            color = c;
            Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize) => Theme.BadgeSize(Text, Font, DeviceDpi / 96f);

        protected override void OnPaint(PaintEventArgs e)
        {
            var back = Parent?.BackColor ?? Theme.Bg;
            e.Graphics.Clear(back);
            Theme.DrawBadge(e.Graphics, ClientRectangle, Text, color, back, Font, DeviceDpi / 96f);
        }
    }

    // Liste dessinée à la main, sans scintillement
    class DarkListView : ListView
    {
        public DarkListView()
        {
            DoubleBuffered = true;
            OwnerDraw = true;
        }

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // Thème sombre de Windows pour la liste (barres de défilement) et pour l'en-tête
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                SetWindowTheme(Handle, "DarkMode_ItemsView", null);
                var header = SendMessage(Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero) SetWindowTheme(header, "DarkMode_ItemsView", null);
            }
            catch { }
        }
    }
}
