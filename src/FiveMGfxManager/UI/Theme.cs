using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FiveMGfxManager
{
    // Couleurs, polices et outils de dessin du thème sombre
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(18, 20, 25);
        public static readonly Color Sidebar = Color.FromArgb(12, 14, 18);
        public static readonly Color Card = Color.FromArgb(27, 30, 37);
        public static readonly Color Border = Color.FromArgb(40, 44, 53);
        public static readonly Color Field = Color.FromArgb(34, 38, 47);
        public static readonly Color FieldBorder = Color.FromArgb(56, 61, 73);
        public static readonly Color Hover = Color.FromArgb(44, 49, 60);
        public static readonly Color Fg = Color.FromArgb(236, 238, 242);
        public static readonly Color Muted = Color.FromArgb(146, 153, 166);
        public static readonly Color Accent = Color.FromArgb(245, 166, 35);
        public static readonly Color OnAccent = Color.FromArgb(24, 18, 8);
        public static readonly Color Danger = Color.FromArgb(232, 89, 82);
        public static readonly Color Ok = Color.FromArgb(76, 194, 125);

        public static Color TypeColor(string type) =>
            type == "Son" ? Color.FromArgb(45, 212, 191)
            : type == "Armes" ? Color.FromArgb(248, 113, 113)
            : type == Core.ModType ? Color.FromArgb(251, 191, 36)
            : Color.FromArgb(167, 139, 250);

        static readonly HashSet<string> Installed = InstalledFonts();

        static HashSet<string> InstalledFonts()
        {
            try { using (var f = new InstalledFontCollection()) return new HashSet<string>(f.Families.Select(x => x.Name)); }
            catch { return new HashSet<string>(); }
        }

        public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);

        public static Font Semibold(float size) => Installed.Contains("Segoe UI Semibold")
            ? new Font("Segoe UI Semibold", size) : new Font("Segoe UI", size, FontStyle.Bold);

        // Icônes : police de Windows 11 (Segoe Fluent Icons) ou de Windows 10 (Segoe MDL2 Assets)
        static readonly string IconFamily = new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }.FirstOrDefault(Installed.Contains);

        public static Font Icons(float size) => IconFamily == null ? null : new Font(IconFamily, size);

        public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int radius, Color fill, Color? border = null)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Round(r, radius))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                if (border != null)
                    using (var p = new Pen(border.Value)) g.DrawPath(p, path);
            }
            g.SmoothingMode = old;
        }

        // Pastille colorée (type de pack, état)
        public static Size BadgeSize(string text, Font font, float k)
        {
            var t = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(t.Width + (int)(22 * k), t.Height + (int)(8 * k));
        }

        public static void DrawBadge(Graphics g, Rectangle cell, string text, Color color, Color back, Font font, float k)
        {
            var size = BadgeSize(text, font, k);
            var r = new Rectangle(cell.X, cell.Y + (cell.Height - size.Height) / 2, Math.Min(size.Width, cell.Width), size.Height);
            FillRound(g, r, r.Height / 2, Mix(back, color, 0.18f));
            int dot = (int)(6 * k);
            using (var b = new SolidBrush(color))
            {
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(b, r.X + (int)(8 * k), r.Y + (r.Height - dot) / 2, dot, dot);
                g.SmoothingMode = old;
            }
            var tr = Rectangle.FromLTRB(r.X + (int)(17 * k), r.Y, r.Right, r.Bottom);
            TextRenderer.DrawText(g, text, font, tr, Mix(color, Color.White, 0.25f),
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                                  TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        // ---------------------------------------------- fenêtres sombres --

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        // Barre de titre sombre (Windows 10 20H1 et plus)
        public static void DarkTitleBar(Form f)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }

        // Barres de défilement sombres
        public static void DarkScrollbars(Control c)
        {
            void Apply() { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } }
            if (c.IsHandleCreated) Apply();
            else c.HandleCreated += (s, e) => Apply();
        }
    }

    // Icônes de la police Segoe (Fluent / MDL2)
    static class Glyph
    {
        public const string Packs = "";
        public const string Mods = "\uEA86";
        public const string Backup = "";
        public const string Reshade = "";
        public const string Game = "";
        public const string Add = "";
        public const string Folder = "";
        public const string OpenFolder = "";
        public const string Search = "";
        public const string Download = "";
        public const string Remove = "";
        public const string Delete = "";
        public const string Tag = "";
        public const string Clear = "";
        public const string Check = "";
        public const string Warning = "";
        public const string Save = "";
        public const string History = "";
        public const string Activity = "";
        public const string Info = "";
    }
}
