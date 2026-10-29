using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace FiveMGfxManager
{
    // Pages Graphismes, Presets ReShade et Profils
    public partial class MainForm
    {
        StatusCard graphicsCard;
        readonly System.Collections.Generic.Dictionary<string, FlatButton> graphicsButtons =
            new System.Collections.Generic.Dictionary<string, FlatButton>();
        readonly DarkListView presetList = new DarkListView();
        readonly DarkListView profileList = new DarkListView();
        readonly Toggle perfToggle = new Toggle("Mode performance ReShade (plus de FPS, réglages figés en jeu)");
        Page presetsPage;

        // ------------------------------------------------------- pages --

        Control BuildGraphicsPage()
        {
            var page = Stack();
            graphicsCard = new StatusCard();
            AddRow(page, graphicsCard);
            AddRow(page, TextCard("Profils",
                "Performance :  ombres et reflets bas, herbe et densité réduites. Le plus de FPS.\n" +
                "Équilibré :  ombres douces, reflets moyens, textures hautes. Beau et fluide (conseillé).\n" +
                "Qualité :  ombres très hautes, herbe et particules hautes, densité max. Pour une bonne carte graphique.\n" +
                "Tes réglages actuels sont sauvegardés la première fois et peuvent être remis. FiveM doit être fermé.", 150));
            var btns = ButtonBar();
            foreach (var name in Core.GraphicsProfiles)
            {
                var b = Btn(name, () => ApplyGraphics(name), ButtonKind.Secondary, Glyph.Graphics);
                graphicsButtons[name] = b;
                btns.Controls.Add(b);
            }
            btns.Controls.Add(Btn("Remettre mes réglages d'origine", RestoreGraphics, ButtonKind.Danger, Glyph.History));
            btns.Controls.Add(Btn("Ouvrir le dossier", () => Explore(Path.GetDirectoryName(Core.GraphicsSettingsPath())),
                                  ButtonKind.Ghost, Glyph.OpenFolder));
            AddRow(page, btns);
            return page;
        }

        Control BuildPresetsPage()
        {
            var page = FillStack();
            page.Controls.Add(ListCard(presetList, ("PRESET", 0), ("EFFETS", 90), ("ÉTAT", 120)), 0, 0);
            presetList.DoubleClick += (s, e) => { if (!busy) ActivatePreset(); };

            var btns = ButtonBar();
            btns.Controls.Add(Btn("Utiliser ce preset", ActivatePreset, ButtonKind.Primary, Glyph.Check));
            btns.Controls.Add(Btn("Dupliquer…", DuplicatePreset, ButtonKind.Secondary, Glyph.Copy));
            btns.Controls.Add(Btn("Ouvrir le dossier", () => { var f = FivemPath(); if (f != null) Explore(Path.Combine(f, "plugins")); },
                                  ButtonKind.Ghost, Glyph.OpenFolder));
            AddRow(page, btns);

            var perf = ButtonBar();
            perfToggle.AutoSize = true;
            perfToggle.Margin = new Padding(0, 6, 24, 0);
            perfToggle.Click += (s, e) => TogglePerformance();
            tips.SetToolTip(perfToggle, "Les shaders ne sont plus recompilés à chaque chargement : moins de saccades et plus de FPS.\n" +
                                        "Désactive-le pour modifier le preset dans le menu ReShade (touche Début).");
            perf.Controls.Add(perfToggle);
            perf.Controls.Add(Btn("Ranger les shaders inutilisés", RetireShaders, ButtonKind.Secondary, Glyph.Clear));
            perf.Controls.Add(Btn("Remettre les shaders rangés", RestoreShaders, ButtonKind.Ghost, Glyph.History));
            AddRow(page, perf);
            return page;
        }

        Control BuildProfilesPage()
        {
            var page = FillStack();
            page.Controls.Add(ListCard(profileList, ("PROFIL", 180), ("CONTENU", 0), ("ENREGISTRÉ", 140)), 0, 0);
            profileList.DoubleClick += (s, e) => { if (!busy) ApplyProfile(); };
            var btns = ButtonBar();
            btns.Controls.Add(Btn("Appliquer le profil", ApplyProfile, ButtonKind.Primary, Glyph.Play));
            btns.Controls.Add(Btn("Appliquer et lancer FiveM", ApplyProfileAndLaunch, ButtonKind.Secondary, Glyph.Play));
            btns.Controls.Add(Btn("Enregistrer la config actuelle…", SaveProfile, ButtonKind.Secondary, Glyph.Save));
            btns.Controls.Add(Btn("Supprimer", DeleteProfile, ButtonKind.Danger, Glyph.Delete));
            AddRow(page, btns);
            AddRow(page, Lbl("Installe tes packs, choisis ton preset et tes graphismes, puis « Enregistrer la config actuelle ».\n" +
                             "« Appliquer » retire les packs qui ne sont pas dans le profil et installe ceux qui manquent.",
                             Theme.Muted, new Padding(0, 12, 0, 0)));
            return page;
        }

        // Pile dont la première ligne (la liste) prend toute la hauteur libre
        static TableLayoutPanel FillStack()
        {
            var t = Stack();
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return t;
        }

        // Liste simple au thème sombre ; une colonne de largeur 0 prend la place libre
        Card ListCard(DarkListView list, params (string Title, int Width)[] cols)
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 6, 8), Margin = new Padding(0) };
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.BackColor = Theme.Card;
            list.ForeColor = Theme.Fg;
            list.BorderStyle = BorderStyle.None;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.ShowItemToolTips = true;
            list.SmallImageList = new ImageList { ImageSize = new Size(1, LogicalToDeviceUnits(38)) };
            foreach (var c in cols) list.Columns.Add(c.Title, LogicalToDeviceUnits(Math.Max(c.Width, 100)));
            int fill = Array.FindIndex(cols, c => c.Width == 0);
            void Fit()
            {
                int other = list.Columns.Cast<ColumnHeader>().Where(c => c.Index != fill).Sum(c => c.Width);
                list.Columns[fill].Width = Math.Max(LogicalToDeviceUnits(120), list.ClientSize.Width - other - 1);
            }
            list.Resize += (s, e) => Fit();

            var headerFont = Theme.Ui(8F, FontStyle.Bold);
            var nameFont = Theme.Semibold(10F);
            list.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var r = Rectangle.FromLTRB(e.Bounds.Left + LogicalToDeviceUnits(12), e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, headerFont, r, Theme.Muted,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += (s, e) =>
            {
                var g = e.Graphics;
                var bounds = e.Bounds;
                if (e.ColumnIndex == 0) bounds.Width = list.Columns[0].Width;
                bool sel = e.Item.Selected;
                var back = sel ? Theme.Mix(Theme.Card, Theme.Accent, 0.12f) : Theme.Card;
                using (var b = new SolidBrush(back)) g.FillRectangle(b, bounds);
                using (var p = new Pen(Theme.Mix(Theme.Card, Theme.Border, 0.7f)))
                    g.DrawLine(p, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
                float k = DeviceDpi / 96f;
                if (e.ColumnIndex == 0 && sel)
                    Theme.FillRound(g, new Rectangle(bounds.Left, bounds.Top + (int)(8 * k), (int)(3 * k), bounds.Height - (int)(17 * k)),
                                    (int)(2 * k), Theme.Accent);
                var cell = Rectangle.FromLTRB(bounds.Left + (int)(12 * k), bounds.Top, bounds.Right - (int)(6 * k), bounds.Bottom - 1);
                // l'élément actif (Tag = true) est écrit en vert dans la dernière colonne
                var color = e.ColumnIndex == 0 ? Theme.Fg
                          : e.ColumnIndex == list.Columns.Count - 1 && Equals(e.Item.Tag, true) ? Theme.Ok : Theme.Muted;
                TextRenderer.DrawText(g, e.SubItem.Text, e.ColumnIndex == 0 ? nameFont : list.Font, cell, color,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                                      TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            };
            card.Controls.Add(list);
            return card;
        }

        // ---------------------------------------------------- états --

        void RefreshGraphics()
        {
            string path = Core.GraphicsSettingsPath();
            if (!File.Exists(path))
            {
                graphicsCard.Set(Glyph.Warning, Theme.Accent, "Réglages du jeu introuvables", "Lance FiveM une fois pour qu'il crée " + path);
                return;
            }
            string profile = Core.CurrentGraphicsProfile();
            // le bouton du profil en place passe en orange
            foreach (var kv in graphicsButtons)
            {
                kv.Value.Kind = kv.Key == profile ? ButtonKind.Primary : ButtonKind.Secondary;
                kv.Value.Invalidate();
            }
            graphicsCard.Set(profile != null ? Glyph.Check : Glyph.Graphics, profile != null ? Theme.Ok : Theme.Muted,
                             profile != null ? $"Profil actuel : {profile}" : "Réglages personnalisés",
                             (Core.HasGraphicsBackup() ? "Réglages d'origine sauvegardés" : "Aucune modification faite par le logiciel") +
                             "  ·  " + path);
        }

        string ValidFivem()
        {
            string p = Clean(fivemBox.Text);
            return Core.IsValidFivemApp(p) ? p : null;
        }

        void RefreshPresets()
        {
            string fivem = ValidFivem();
            var presets = fivem == null ? new System.Collections.Generic.List<ReshadePreset>() : Core.ListReshadePresets(fivem);
            string selected = presetList.SelectedItems.Count > 0 ? presetList.SelectedItems[0].Text : null;
            presetList.BeginUpdate();
            presetList.Items.Clear();
            foreach (var p in presets)
            {
                var item = new ListViewItem(new[] { p.Nom, p.Effets.ToString(), p.Actif ? "Actif" : "" }) { Tag = p.Actif };
                presetList.Items.Add(item);
                if (p.Nom == selected) item.Selected = true;
            }
            presetList.EndUpdate();

            bool perf = fivem != null && Core.ReshadePerformanceMode(fivem);
            perfToggle.Checked = perf;
            int retired = Core.RetiredShaderFolders().Count;
            presetsPage.Subtitle = fivem == null ? "Choisis d'abord le dossier FiveM.app."
                : presets.Count == 0 ? @"Aucun preset dans FiveM.app\plugins."
                : $"{presets.Count} preset{(presets.Count > 1 ? "s" : "")}  ·  actif : {presets.FirstOrDefault(p => p.Actif)?.Nom ?? "aucun"}" +
                  $"  ·  mode performance {(perf ? "activé" : "désactivé")}" +
                  (retired > 0 ? $"  ·  {retired} dossier{(retired > 1 ? "s" : "")} de shaders rangé{(retired > 1 ? "s" : "")}" : "");
            if (current == presetsPage) pageSubtitle.Text = presetsPage.Subtitle;
        }

        void RefreshProfiles()
        {
            string selected = profileList.SelectedItems.Count > 0 ? profileList.SelectedItems[0].Text : null;
            profileList.BeginUpdate();
            profileList.Items.Clear();
            foreach (var p in Core.ListProfiles())
            {
                string desc = Core.DescribeProfile(p);
                var item = new ListViewItem(new[] { p.Nom, desc, p.Date ?? "" }) { ToolTipText = desc };
                profileList.Items.Add(item);
                if (p.Nom == selected) item.Selected = true;
            }
            profileList.EndUpdate();
        }

        // ---------------------------------------------------- actions --

        static void Explore(string dir)
        {
            if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
        }

        void ApplyGraphics(string profile)
        {
            if (!FivemClosed()) return;   // le jeu réécrit le fichier en quittant
            RunTask((log, p) => Core.ApplyGraphicsProfile(profile, log));
        }

        void RestoreGraphics()
        {
            if (!Core.HasGraphicsBackup())
            {
                Info("Graphismes", "Le logiciel n'a encore rien modifié : tes réglages sont déjà ceux d'origine.");
                return;
            }
            if (FivemClosed() && Ask("Graphismes", "Remettre les réglages graphiques que tu avais avant d'utiliser le logiciel ?"))
                RunTask((log, p) => Core.RestoreGraphics(log));
        }

        string SelectedPreset()
        {
            if (presetList.SelectedItems.Count > 0) return presetList.SelectedItems[0].Text;
            Info("Presets", "Choisis d'abord un preset dans la liste.");
            return null;
        }

        void ActivatePreset()
        {
            string name = SelectedPreset();
            string fivem = name == null ? null : FivemPath();
            if (fivem != null && FivemClosed()) RunTask((log, p) => Core.SetActivePreset(fivem, name, log));
        }

        void DuplicatePreset()
        {
            string name = SelectedPreset();
            string fivem = name == null ? null : FivemPath();
            if (fivem == null) return;
            string copy = AskText("Dupliquer le preset", "Nom de la copie :",
                                  Path.GetFileNameWithoutExtension(name) + " - copie");
            if (copy != null) RunTask((log, p) => Core.DuplicatePreset(fivem, name, copy, log));
        }

        void TogglePerformance()
        {
            bool on = perfToggle.Checked;
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed() || busy)
            {
                perfToggle.Checked = !on;
                return;
            }
            RunTask((log, p) => Core.SetReshadePerformanceMode(fivem, on, log));
        }

        void RetireShaders()
        {
            string fivem = FivemPath();
            if (fivem == null) return;
            var unused = Core.UnusedShaderFolders(fivem);
            if (unused.Count == 0)
            {
                Info("Shaders", "Tous les dossiers de shaders sont utilisés par tes presets.");
                return;
            }
            if (FivemClosed() && Ask("Ranger les shaders",
                    $"Ces dossiers ne sont utilisés par aucun preset de FiveM.app\\plugins :\n" +
                    string.Join("\n", unused.Take(15).Select(n => "  • " + n)) + (unused.Count > 15 ? $"\n  … (+{unused.Count - 15})" : "") +
                    "\n\nLes ranger hors de FiveM ? ReShade chargera plus vite.\n(rien n'est supprimé : « Remettre les shaders rangés » les remet)"))
                RunTask((log, p) => Core.RetireUnusedShaders(fivem, log));
        }

        void RestoreShaders()
        {
            if (Core.RetiredShaderFolders().Count == 0)
            {
                Info("Shaders", "Aucun dossier de shaders rangé.");
                return;
            }
            string fivem = FivemPath();
            if (fivem != null && FivemClosed()) RunTask((log, p) => Core.RestoreRetiredShaders(fivem, log));
        }

        string SelectedProfile()
        {
            if (profileList.SelectedItems.Count > 0) return profileList.SelectedItems[0].Text;
            Info("Profils", "Choisis d'abord un profil dans la liste.");
            return null;
        }

        void SaveProfile()
        {
            string name = AskText("Enregistrer un profil",
                                  "Nom du profil (packs installés, preset ReShade et graphismes actuels) :",
                                  profileList.SelectedItems.Count > 0 ? profileList.SelectedItems[0].Text : "Profil RP");
            if (name == null) return;
            if (Core.ListProfiles().Any(p => p.Nom.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) &&
                !Ask("Profils", $"Le profil « {name.Trim()} » existe déjà. Le remplacer ?"))
                return;
            string fivem = ValidFivem();
            RunTask((log, p) => Core.SaveCurrentAsProfile(name, fivem, log));
        }

        void ApplyProfile()
        {
            string name = SelectedProfile();
            string fivem = name == null ? null : FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (!Core.HasOriginalBackup() &&
                !Ask("Sauvegarde d'origine", "Aucune sauvegarde d'origine n'existe. Appliquer le profil quand même ?"))
                return;
            RunTask((log, p) => Core.ApplyProfile(name, fivem, log, p));
        }

        void DeleteProfile()
        {
            string name = SelectedProfile();
            if (name != null && Ask("Profils", $"Supprimer le profil « {name} » ?\n(les packs restent installés et dans la bibliothèque)"))
            {
                Core.DeleteProfile(name);
                RefreshProfiles();
            }
        }

        // Petite fenêtre pour saisir un nom
        string AskText(string title, string prompt, string value)
        {
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Theme.Card, ForeColor = Theme.Fg,
                Font = Font, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20),
                AutoScaleMode = AutoScaleMode.None,
            })
            {
                f.HandleCreated += (s, e) => Theme.DarkTitleBar(f);
                var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                var input = new InputBox { Width = LogicalToDeviceUnits(380), Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(18)) };
                input.Box.Text = value;
                var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
                var ok = new FlatButton("OK", Glyph.Check, ButtonKind.Primary) { AutoSize = true, Height = LogicalToDeviceUnits(36) };
                ok.Padding = new Padding(LogicalToDeviceUnits(16), 0, LogicalToDeviceUnits(16), 0);
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                var cancel = new FlatButton("Annuler", null, ButtonKind.Ghost) { AutoSize = true, Height = LogicalToDeviceUnits(36) };
                cancel.Padding = ok.Padding;
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                flow.Controls.Add(Lbl(prompt, Theme.Muted, new Padding(0, 0, 0, LogicalToDeviceUnits(8))));
                flow.Controls.Add(input);
                flow.Controls.Add(buttons);
                f.Controls.Add(flow);
                f.Shown += (s, e) => { input.Box.Focus(); input.Box.SelectAll(); };
                input.Box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; }
                    if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel;
                };
                if (f.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(input.Box.Text)) return null;
                return input.Box.Text.Trim();
            }
        }
    }
}
