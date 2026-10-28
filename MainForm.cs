using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FiveMGfxManager
{
    public class MainForm : Form
    {
        static readonly Color Bg = Color.FromArgb(22, 24, 29);
        static readonly Color Panel = Color.FromArgb(31, 34, 41);
        static readonly Color Field = Color.FromArgb(42, 46, 55);
        static readonly Color Fg = Color.FromArgb(232, 232, 232);
        static readonly Color Muted = Color.FromArgb(154, 160, 170);
        static readonly Color Accent = Color.FromArgb(245, 166, 35);
        static readonly Color Danger = Color.FromArgb(217, 83, 79);
        static readonly Color Ok = Color.FromArgb(76, 175, 80);
        static readonly Color BtnBg = Color.FromArgb(44, 48, 57);

        // Ligne de la liste des packs : un pack de la bibliothèque et/ou son installation
        class PackRow
        {
            public LibraryPack Lib;
            public PackInfo Inst;
            public string Key => Lib?.Id ?? "inst:" + Inst.Id;
            public string Nom => Lib?.Nom ?? Inst.Nom;
        }

        readonly TextBox fivemBox = new TextBox();
        readonly ListView packList = new ListView();
        readonly ComboBox typeBox = new ComboBox();
        readonly TextBox reshadeBox = new TextBox();
        readonly TextBox presetBox = new TextBox();
        readonly CheckBox shadersCheck = new CheckBox();
        readonly Label packStatus = new Label();
        readonly Label backupStatus = new Label();
        readonly Label reshadeStatus = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly RichTextBox logBox = new RichTextBox();
        readonly List<Button> actionButtons = new List<Button>();
        readonly List<Button> tabButtons = new List<Button>();
        readonly List<Control> tabPages = new List<Control>();
        bool busy;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "FiveM GFX Manager";
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Segoe UI", 9.75F);
            ClientSize = new Size(900, 760);
            MinimumSize = new Size(800, 700);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var cfg = Core.LoadConfig();
            fivemBox.Text = cfg.FivemApp ?? Core.DetectFivemApp() ?? "";

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(16, 10, 16, 14),
                BackColor = Bg,
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            root.Controls.Add(new Label
            {
                Text = "FiveM GFX Manager", AutoSize = true, ForeColor = Accent,
                Font = new Font("Segoe UI", 16F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 6),
            });

            // Dossier FiveM.app
            var top = Row(Panel, new Padding(10));
            top.Controls.Add(Lbl("Dossier FiveM.app :"), 0, 0);
            top.Controls.Add(Box(fivemBox), 1, 0);
            top.Controls.Add(Btn("Parcourir", ChooseFivem), 2, 0);
            top.Controls.Add(Btn("Détecter", DetectFivem), 3, 0);
            root.Controls.Add(top);

            // Onglets
            var tabBar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0), BackColor = Bg };
            var pages = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Panel, Margin = new Padding(0, 0, 0, 8) };
            AddTab(tabBar, pages, "Packs (graphique, son, armes)", BuildPackTab());
            AddTab(tabBar, pages, "Sauvegarde d'origine", BuildBackupTab());
            AddTab(tabBar, pages, "ReShade", BuildReshadeTab());
            root.Controls.Add(tabBar);
            root.Controls.Add(pages);
            SelectTab(0);

            progress.Dock = DockStyle.Fill;
            progress.Height = 14;
            progress.Maximum = 1000;
            progress.Margin = new Padding(0, 0, 0, 6);
            root.Controls.Add(progress);

            logBox.Dock = DockStyle.Fill;
            logBox.ReadOnly = true;
            logBox.DetectUrls = false;
            logBox.BackColor = Panel;
            logBox.ForeColor = Fg;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Consolas", 9F);
            var logHost = new Panel { Dock = DockStyle.Fill, BackColor = Panel, Padding = new Padding(8, 6, 4, 6), Margin = new Padding(0) };
            logHost.Controls.Add(logBox);
            root.Controls.Add(logHost);

            ResumeLayout(true);
            RefreshStatus();
            Log("Prêt. Ferme FiveM avant toute opération.", Muted);
            Shown += (s, e) => ActiveControl = tabButtons[0];
        }

        // ------------------------------------------------------------ UI --

        TableLayoutPanel Row(Color back, Padding pad)
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, BackColor = back, Padding = pad,
                Margin = new Padding(0),
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        Label Lbl(string text, Color? color = null, bool bold = false) => new Label
        {
            Text = text, AutoSize = true, ForeColor = color ?? Fg, Anchor = AnchorStyles.Left,
            Font = bold ? new Font("Segoe UI", 10F, FontStyle.Bold) : Font, Margin = new Padding(3, 6, 3, 6),
        };

        TextBox Box(TextBox b)
        {
            b.Dock = DockStyle.Fill;
            b.BackColor = Field;
            b.ForeColor = Fg;
            b.BorderStyle = BorderStyle.FixedSingle;
            b.Margin = new Padding(6, 5, 6, 5);
            return b;
        }

        Button Btn(string text, Action onClick, Color? back = null, Color? fore = null)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = back ?? BtnBg,
                ForeColor = fore ?? Fg, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(3), Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => onClick();
            actionButtons.Add(b);
            return b;
        }

        Button AccentBtn(string text, Action onClick) =>
            Btn(text, onClick, Accent, Color.FromArgb(17, 17, 17));

        Button DangerBtn(string text, Action onClick) => Btn(text, onClick, Danger, Color.White);

        void AddTab(FlowLayoutPanel bar, Panel host, string title, Control page)
        {
            int index = tabButtons.Count;
            var b = new Button
            {
                Text = "  " + title + "  ", AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 2, 0),
                Padding = new Padding(6, 4, 6, 4), Cursor = Cursors.Hand,
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => SelectTab(index);
            bar.Controls.Add(b);
            tabButtons.Add(b);
            page.Dock = DockStyle.Top;
            host.Controls.Add(page);
            tabPages.Add(page);
        }

        void SelectTab(int index)
        {
            for (int i = 0; i < tabButtons.Count; i++)
            {
                bool sel = i == index;
                tabButtons[i].BackColor = sel ? Panel : BtnBg;
                tabButtons[i].ForeColor = sel ? Accent : Fg;
                tabButtons[i].Font = new Font("Segoe UI", 9.75F, sel ? FontStyle.Bold : FontStyle.Regular);
                tabPages[i].Visible = sel;
            }
        }

        TableLayoutPanel Page(out FlowLayoutPanel buttons)
        {
            var p = new TableLayoutPanel
            {
                ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(14), BackColor = Panel,
            };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, BackColor = Panel, Margin = new Padding(0, 6, 0, 0), WrapContents = true,
            };
            return p;
        }

        Control BuildPackTab()
        {
            var page = Page(out var btns);
            packStatus.AutoSize = true;
            packStatus.ForeColor = Accent;
            packStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            packStatus.Margin = new Padding(3, 0, 3, 8);
            page.Controls.Add(packStatus);

            // Ajout à la bibliothèque
            var row = Row(Panel, new Padding(0));
            row.Controls.Add(Lbl("Type du pack :"), 0, 0);
            typeBox.DropDownStyle = ComboBoxStyle.DropDownList;
            typeBox.FlatStyle = FlatStyle.Flat;
            typeBox.BackColor = Field;
            typeBox.ForeColor = Fg;
            typeBox.MinimumSize = new Size(140, 0);
            typeBox.Anchor = AnchorStyles.Left;
            typeBox.Margin = new Padding(6, 5, 6, 5);
            typeBox.Items.AddRange(Core.PackTypes);
            typeBox.SelectedIndex = 0;
            row.Controls.Add(typeBox, 1, 0);
            row.Controls.Add(AccentBtn("Ajouter des archives…", ImportArchives), 2, 0);
            row.Controls.Add(Btn("Ajouter un dossier…", ImportFolder), 3, 0);
            page.Controls.Add(row);

            // Liste des packs
            packList.View = View.Details;
            packList.FullRowSelect = true;
            packList.MultiSelect = false;
            packList.HideSelection = false;
            packList.BackColor = Field;
            packList.ForeColor = Fg;
            packList.BorderStyle = BorderStyle.None;
            packList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            packList.Height = 190;
            packList.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            packList.Margin = new Padding(3, 8, 3, 4);
            // largeurs en pixels « 96 dpi » : la liste ne suit pas toute seule le zoom de Windows
            packList.Columns.Add("Nom", 300);
            packList.Columns.Add("Type", LogicalToDeviceUnits(95));
            packList.Columns.Add("Taille", LogicalToDeviceUnits(75));
            packList.Columns.Add("Mods ajoutés", LogicalToDeviceUnits(120));
            packList.Columns.Add("État", LogicalToDeviceUnits(190));
            packList.OwnerDraw = true;
            packList.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(BtnBg)) e.Graphics.FillRectangle(b, e.Bounds);
                TextRenderer.DrawText(e.Graphics, e.Header.Text, Font, Rectangle.Inflate(e.Bounds, -4, 0), Muted,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            };
            packList.DrawItem += (s, e) => e.DrawDefault = true;
            packList.DrawSubItem += (s, e) => e.DrawDefault = true;
            packList.Resize += (s, e) => FitColumns();
            packList.DoubleClick += (s, e) => { if (!busy) InstallSelected(); };
            // glisser-déposer des archives / dossiers sur la liste
            packList.AllowDrop = true;
            packList.DragEnter += (s, e) =>
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None;
            packList.DragDrop += (s, e) => Import((string[])e.Data.GetData(DataFormats.FileDrop));
            page.Controls.Add(packList);

            page.Controls.Add(Lbl(
                "Ajoute tes packs (.zip, .7z, .rar, .rpf ou dossier ; tu peux aussi les glisser sur la liste) : ils sont\n" +
                "décompressés et gardés dans la bibliothèque. Choisis ensuite un pack et clique sur « Installer ».\n" +
                "Plusieurs packs peuvent être installés en même temps (ex : graphique + son + armes), tant qu'ils ne\n" +
                "remplacent pas les mêmes fichiers. Les .rpf sont rangés dans mods\\ au même chemin que dans GTA V.", Muted));

            btns.Controls.Add(AccentBtn("Installer", InstallSelected));
            btns.Controls.Add(DangerBtn("Retirer", UninstallSelected));
            btns.Controls.Add(Btn("Ajouter des mods au pack…", AddMods));
            btns.Controls.Add(Btn("Changer le type…", ChangeType));
            btns.Controls.Add(Btn("Supprimer de la bibliothèque", DeleteSelected));
            btns.Controls.Add(Btn("Vider le cache FiveM", ClearCache));
            btns.Controls.Add(Btn("Ouvrir FiveM.app", OpenFivem));
            page.Controls.Add(btns);
            return page;
        }

        void FitColumns()
        {
            int other = packList.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width);
            packList.Columns[0].Width = Math.Max(150, packList.ClientSize.Width - other);
        }

        void RefreshPackList()
        {
            string selected = SelectedRow()?.Key;
            var installed = Core.InstalledPacks();
            var rows = Core.ListLibrary().Select(l => new PackRow { Lib = l, Inst = installed.FirstOrDefault(i => i.LibId == l.Id) })
                           .ToList();
            // packs installés sans passer par la bibliothèque (anciennes versions du logiciel)
            rows.AddRange(installed.Where(i => i.LibId == null || rows.All(r => r.Lib.Id != i.LibId))
                                   .Select(i => new PackRow { Inst = i }));

            packList.BeginUpdate();
            packList.Items.Clear();
            foreach (var r in rows)
            {
                var item = new ListViewItem(new[]
                {
                    r.Nom,
                    r.Lib?.Type ?? r.Inst.Type ?? "Graphique",
                    r.Lib != null ? Core.HumanSize(r.Lib.Taille) : "",
                    r.Lib?.Mods != null && r.Lib.Mods.Count > 0 ? r.Lib.Mods.Count.ToString() : "",
                    r.Inst == null ? "Dans la bibliothèque" : r.Lib == null ? "Installé (hors bibliothèque)" : "✔ Installé",
                })
                {
                    Tag = r,
                    ForeColor = r.Inst != null ? Ok : Fg,
                    BackColor = Field,
                };
                if (r.Lib?.Mods != null && r.Lib.Mods.Count > 0)
                    item.ToolTipText = "Mods ajoutés : " + string.Join(", ", r.Lib.Mods);
                packList.Items.Add(item);
                if (r.Key == selected) item.Selected = true;
            }
            packList.ShowItemToolTips = true;
            packList.EndUpdate();
            FitColumns();

            packStatus.Text = $"{rows.Count(r => r.Lib != null)} pack(s) dans la bibliothèque · " +
                              (installed.Count == 0 ? "aucun installé"
                                  : $"installé(s) : {string.Join(", ", installed.Select(i => i.Nom))}");
        }

        PackRow SelectedRow() => packList.SelectedItems.Count > 0 ? (PackRow)packList.SelectedItems[0].Tag : null;

        PackRow RequireSelection()
        {
            var r = SelectedRow();
            if (r == null) Info("Packs", "Choisis d'abord un pack dans la liste.");
            return r;
        }

        Control BuildBackupTab()
        {
            var page = Page(out var btns);
            backupStatus.AutoSize = true;
            backupStatus.ForeColor = Accent;
            backupStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            backupStatus.Margin = new Padding(3, 0, 3, 8);
            page.Controls.Add(backupStatus);
            page.Controls.Add(Lbl(
                "Fais cette sauvegarde quand FiveM est PROPRE (sans aucun mod).\n" +
                "Elle copie entièrement les dossiers citizen, mods et plugins de FiveM.app.\n" +
                "« Restaurer » supprime tous les mods / packs / ReShade et remet FiveM comme à l'origine.", Muted));
            btns.Controls.Add(AccentBtn("Créer / mettre à jour la sauvegarde", CreateBackup));
            btns.Controls.Add(DangerBtn("Restaurer FiveM d'origine", RestoreBackup));
            btns.Controls.Add(Btn("Ouvrir le dossier des sauvegardes", OpenBackups));
            page.Controls.Add(btns);
            return page;
        }

        Control BuildReshadeTab()
        {
            var page = Page(out var btns);
            reshadeStatus.AutoSize = true;
            reshadeStatus.ForeColor = Accent;
            reshadeStatus.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            reshadeStatus.Margin = new Padding(3, 0, 3, 8);
            page.Controls.Add(reshadeStatus);

            var row1 = Row(Panel, new Padding(0));
            var l1 = Lbl("ReShade :");
            l1.MinimumSize = new Size(70, 0);
            row1.Controls.Add(l1, 0, 0);
            row1.Controls.Add(Box(reshadeBox), 1, 0);
            row1.Controls.Add(Btn("Choisir…", ChooseReshade), 2, 0);
            row1.Controls.Add(Btn("Effacer", () => reshadeBox.Text = ""), 3, 0);
            page.Controls.Add(row1);

            var row2 = Row(Panel, new Padding(0));
            var l2 = Lbl("Preset :");
            l2.MinimumSize = new Size(70, 0);
            row2.Controls.Add(l2, 0, 0);
            row2.Controls.Add(Box(presetBox), 1, 0);
            row2.Controls.Add(Btn("Choisir…", ChoosePreset), 2, 0);
            row2.Controls.Add(Btn("Effacer", () => presetBox.Text = ""), 3, 0);
            page.Controls.Add(row2);

            shadersCheck.Text = "Télécharger les shaders officiels (recommandé)";
            shadersCheck.Checked = true;
            shadersCheck.AutoSize = true;
            shadersCheck.ForeColor = Fg;
            shadersCheck.Margin = new Padding(3, 8, 3, 0);
            page.Controls.Add(shadersCheck);

            page.Controls.Add(Lbl(
                "Laisse « ReShade » vide pour télécharger automatiquement la dernière version sur reshade.me,\n" +
                "ou choisis un ReShade_Setup_x.x.x.exe / ReShade64.dll. Le preset (.ini) est optionnel.\n" +
                "ReShade est installé dans FiveM.app\\plugins (dxgi.dll). En jeu : touche Début (Home).", Muted));

            btns.Controls.Add(AccentBtn("Installer ReShade", InstallReshade));
            btns.Controls.Add(DangerBtn("Désinstaller ReShade", UninstallReshade));
            page.Controls.Add(btns);
            return page;
        }

        // ------------------------------------------------------- helpers --

        void Log(string msg, Color? color = null)
        {
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = color ?? Fg;
            logBox.AppendText(msg + Environment.NewLine);
            logBox.ScrollToCaret();
        }

        void RefreshStatus()
        {
            RefreshPackList();
            var info = Core.OriginalBackupInfo();
            backupStatus.Text = info != null ? $"Sauvegarde du {info.Date}  ({info.Fichiers} fichiers)" : "Aucune sauvegarde d'origine";
            var rs = Core.ReshadeStatus();
            reshadeStatus.Text = rs != null ? $"ReShade {rs.Version} installé (le {rs.Date})" : "ReShade non installé";
        }

        static string Clean(string s) => (s ?? "").Trim().Trim('"');

        string FivemPath()
        {
            string path = Clean(fivemBox.Text);
            if (!Core.IsValidFivemApp(path))
            {
                MessageBox.Show(this, "Choisis le dossier FiveM.app.\nGénéralement :\n%LOCALAPPDATA%\\FiveM\\FiveM.app\n\n" +
                                "(clic droit sur le raccourci FiveM > Ouvrir l'emplacement du fichier)",
                                "FiveM.app introuvable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
            var cfg = Core.LoadConfig();
            if (cfg.FivemApp != path)
            {
                cfg.FivemApp = path;
                Core.SaveConfig(cfg);
            }
            return path;
        }

        bool FivemClosed()
        {
            if (!Core.IsFivemRunning()) return true;
            MessageBox.Show(this, "Ferme complètement FiveM avant de continuer.", "FiveM est ouvert",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        bool Ask(string title, string text) =>
            MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        void Info(string title, string text) =>
            MessageBox.Show(this, text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

        // Exécute le travail en arrière-plan, avec journal et barre de progression.
        void RunTask(Action<Action<string>, Action<double>> work)
        {
            if (busy) return;
            busy = true;
            foreach (var b in actionButtons) b.Enabled = false;
            progress.Value = 0;
            UseWaitCursor = true;

            Action<string> log = m => BeginInvoke((Action)(() => Log(m)));
            Action<double> prog = p => BeginInvoke((Action)(() =>
                progress.Value = Math.Max(0, Math.Min(progress.Maximum, (int)(p * progress.Maximum)))));

            Task.Run(() => work(log, prog)).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                busy = false;
                UseWaitCursor = false;
                foreach (var b in actionButtons) b.Enabled = true;
                RefreshStatus();
                if (t.Exception == null)
                {
                    progress.Value = progress.Maximum;
                    Log("✔ Terminé.", Ok);
                    return;
                }
                var e = t.Exception.GetBaseException();
                string msg = e is GfxException ? e.Message
                    : e is System.Net.WebException
                        ? "Téléchargement impossible : " + e.Message + "\n\nVérifie ta connexion Internet, " +
                          "ou télécharge ReShade_Setup.exe sur reshade.me et choisis-le avec « Choisir… »."
                    : e is UnauthorizedAccessException || e is IOException
                        ? e.Message + "\n\nUn fichier est peut-être utilisé (FiveM ouvert ?) ou protégé."
                        : e.GetType().Name + " : " + e.Message;
                Log("✖ " + msg, Danger);
                MessageBox.Show(this, msg, "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            })));
        }

        string PickFolder(string description)
        {
            using (var d = new FolderBrowserDialog { Description = description, ShowNewFolderButton = false })
                return d.ShowDialog(this) == DialogResult.OK ? d.SelectedPath : null;
        }

        string PickFile(string title, string filter)
        {
            using (var d = new OpenFileDialog { Title = title, Filter = filter })
                return d.ShowDialog(this) == DialogResult.OK ? d.FileName : null;
        }

        string[] PickFiles(string title, string filter)
        {
            using (var d = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true })
                return d.ShowDialog(this) == DialogResult.OK ? d.FileNames : null;
        }

        const string PackFilter = "Packs (*.zip;*.7z;*.rar;*.rpf)|*.zip;*.7z;*.rar;*.rpf|Tous les fichiers|*.*";

        // Petite fenêtre pour choisir un type de pack
        string PickType(string title, string current)
        {
            using (var f = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, BackColor = Panel, ForeColor = Fg, Font = Font,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12),
            })
            {
                var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, BackColor = Panel };
                var combo = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Field,
                    ForeColor = Fg, Width = 200,
                };
                combo.Items.AddRange(Core.PackTypes);
                combo.SelectedItem = Core.PackTypes.Contains(current) ? current : Core.PackTypes[0];
                var ok = new Button
                {
                    Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Accent,
                    ForeColor = Color.FromArgb(17, 17, 17), AutoSize = true, Margin = new Padding(3, 10, 3, 3),
                };
                ok.FlatAppearance.BorderSize = 0;
                flow.Controls.Add(new Label { Text = "Type du pack :", AutoSize = true });
                flow.Controls.Add(combo);
                flow.Controls.Add(ok);
                f.Controls.Add(flow);
                f.AcceptButton = ok;
                return f.ShowDialog(this) == DialogResult.OK ? (string)combo.SelectedItem : null;
            }
        }

        // ------------------------------------------------------- actions --

        void ChooseFivem()
        {
            var d = PickFolder("Choisir le dossier FiveM.app");
            if (d != null) fivemBox.Text = d;
        }

        void DetectFivem()
        {
            var p = Core.DetectFivemApp();
            if (p != null)
            {
                fivemBox.Text = p;
                Log("FiveM.app trouvé : " + p, Ok);
            }
            else
            {
                Info("Non trouvé", "FiveM.app n'a pas été trouvé automatiquement.\nUtilise « Parcourir ».");
            }
        }

        void ImportArchives()
        {
            var files = PickFiles("Ajouter des packs à la bibliothèque", PackFilter);
            if (files != null) Import(files);
        }

        void ImportFolder()
        {
            var d = PickFolder("Choisir le dossier du pack (déjà décompressé)");
            if (d != null) Import(new[] { d });
        }

        void Import(string[] sources)
        {
            if (sources == null || sources.Length == 0 || busy) return;
            string type = (string)typeBox.SelectedItem;
            RunTask((log, p) =>
            {
                // un fichier refusé n'empêche pas d'importer les suivants
                var errors = new List<string>();
                for (int i = 0; i < sources.Length; i++)
                {
                    int k = i;
                    try { Core.ImportPack(sources[i], type, log, x => p((k + x) / sources.Length)); }
                    catch (Exception ex) when (sources.Length > 1)
                    {
                        errors.Add($"{Path.GetFileName(sources[i].TrimEnd('\\', '/'))} : {ex.Message}");
                        log("  ! " + errors.Last());
                    }
                }
                if (errors.Count > 0)
                    throw new GfxException($"{errors.Count} pack(s) sur {sources.Length} non importé(s) :\n\n" +
                                           string.Join("\n\n", errors));
            });
        }

        void InstallSelected()
        {
            var r = RequireSelection();
            if (r == null) return;
            if (r.Inst != null)
            {
                Info("Packs", $"Le pack « {r.Nom} » est déjà installé.");
                return;
            }
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;

            var steps = new List<Action<Action<string>, Action<double>>>();
            if (!Core.HasOriginalBackup() &&
                Ask("Sauvegarde d'origine", "Aucune sauvegarde d'origine n'existe.\n\n" +
                    "La créer maintenant avant d'installer le pack ? (recommandé)\n" +
                    "⚠ FiveM doit être actuellement SANS mods."))
                steps.Add((log, p) => Core.CreateOriginalBackup(fivem, log, p));

            // un seul pack graphique à la fois : on propose de remplacer l'ancien
            var same = Core.InstalledPacks().Where(i => i.Type == r.Lib.Type && r.Lib.Type == "Graphique").ToList();
            if (same.Count > 0 &&
                Ask("Remplacer le pack", $"Le pack graphique « {same[0].Nom} » est déjà installé.\n" +
                    "Le retirer avant d'installer le nouveau ? (conseillé)"))
                foreach (var s in same) steps.Add((log, p) => Core.UninstallPack(s.Id, fivem, log, p));

            string id = r.Lib.Id;
            steps.Add((log, p) => Core.InstallLibraryPack(id, fivem, log, p));
            RunTask((log, p) => { foreach (var s in steps) s(log, p); });
        }

        void UninstallSelected()
        {
            var r = RequireSelection();
            if (r == null) return;
            if (r.Inst == null)
            {
                Info("Packs", $"Le pack « {r.Nom} » n'est pas installé.\n" +
                              "Pour l'enlever de la liste, utilise « Supprimer de la bibliothèque ».");
                return;
            }
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            string id = r.Inst.Id;
            if (Ask("Retirer le pack", $"Retirer « {r.Nom} » de FiveM et remettre les fichiers d'origine ?\n" +
                                       "(il reste dans la bibliothèque)"))
                RunTask((log, p) => Core.UninstallPack(id, fivem, log, p));
        }

        void AddMods()
        {
            var r = RequireSelection();
            if (r == null) return;
            if (r.Lib == null)
            {
                Info("Packs", "Ce pack a été installé hors bibliothèque : on ne peut pas lui ajouter de mods.");
                return;
            }
            string fivem = null;
            if (r.Inst != null)
            {
                if (!Ask("Pack installé", $"« {r.Nom} » est installé. Le retirer, ajouter les mods puis le réinstaller ?"))
                    return;
                fivem = FivemPath();
                if (fivem == null || !FivemClosed()) return;
            }
            var files = PickFiles($"Mods à ajouter dans « {r.Nom} »", PackFilter);
            if (files == null) return;

            string libId = r.Lib.Id, instId = r.Inst?.Id;
            RunTask((log, p) =>
            {
                if (instId != null) Core.UninstallPack(instId, fivem, log, p);
                try
                {
                    foreach (var f in files) Core.AddModsToPack(libId, f, log, p);
                }
                finally
                {
                    // même si un mod est refusé, le pack est remis comme avant
                    if (instId != null) Core.InstallLibraryPack(libId, fivem, log, p);
                }
            });
        }

        void ChangeType()
        {
            var r = RequireSelection();
            if (r == null) return;
            if (r.Lib == null)
            {
                Info("Packs", "Ce pack a été installé hors bibliothèque.");
                return;
            }
            string type = PickType($"Type de « {r.Nom} »", r.Lib.Type);
            if (type == null || type == r.Lib.Type) return;
            Core.SetPackType(r.Lib.Id, type);
            RefreshStatus();
        }

        void DeleteSelected()
        {
            var r = RequireSelection();
            if (r == null) return;
            if (r.Lib == null)
            {
                Info("Packs", "Ce pack n'est pas dans la bibliothèque. Utilise « Retirer » pour l'enlever de FiveM.");
                return;
            }
            if (r.Inst != null)
            {
                Info("Packs", $"« {r.Nom} » est installé. Retire-le d'abord.");
                return;
            }
            string id = r.Lib.Id;
            if (Ask("Supprimer", $"Supprimer « {r.Nom} » de la bibliothèque ?\n(il faudra l'importer à nouveau pour le réutiliser)"))
                RunTask((log, p) => Core.DeleteFromLibrary(id, log));
        }

        void ChooseReshade()
        {
            var f = PickFile("Choisir ReShade_Setup.exe ou ReShade64.dll", "ReShade (*.exe;*.dll)|*.exe;*.dll|Tous les fichiers|*.*");
            if (f != null) reshadeBox.Text = f;
        }

        void ChoosePreset()
        {
            var f = PickFile("Choisir un preset ReShade", "Preset (*.ini;*.txt)|*.ini;*.txt|Tous les fichiers|*.*");
            if (f != null) presetBox.Text = f;
        }

        void ClearCache()
        {
            string fivem = FivemPath();
            if (fivem != null && FivemClosed())
                RunTask((log, p) => Core.ClearFivemCache(fivem, log));
        }

        void CreateBackup()
        {
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            string msg = "Copier citizen, mods et plugins comme sauvegarde d'origine ?\n\n⚠ FiveM doit être SANS mods.";
            if (Core.HasOriginalBackup()) msg += "\n\nL'ancienne sauvegarde sera remplacée.";
            if (Core.InstalledPacks().Count > 0) msg += "\n\n⚠ Des packs sont actuellement installés : ils seraient inclus dans la sauvegarde !";
            if (Ask("Sauvegarde d'origine", msg))
                RunTask((log, p) => Core.CreateOriginalBackup(fivem, log, p));
        }

        void RestoreBackup()
        {
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (!Core.HasOriginalBackup())
            {
                Info("Sauvegarde", "Aucune sauvegarde d'origine n'existe.");
                return;
            }
            if (Ask("Restaurer", "Supprimer TOUS les mods (citizen, mods, plugins) et remettre la sauvegarde d'origine ?"))
                RunTask((log, p) => Core.RestoreOriginalBackup(fivem, log, p));
        }

        void InstallReshade()
        {
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            string setup = Clean(reshadeBox.Text);
            string preset = Clean(presetBox.Text);
            foreach (var f in new[] { setup, preset })
                if (f != "" && !File.Exists(f))
                {
                    MessageBox.Show(this, f, "Fichier introuvable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            bool shaders = shadersCheck.Checked;
            RunTask((log, p) => Core.InstallReshade(fivem, setup == "" ? null : setup, shaders,
                                                    preset == "" ? null : preset, log, p));
        }

        void UninstallReshade()
        {
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (Core.ReshadeStatus() == null)
            {
                Info("ReShade", "ReShade n'a pas été installé avec ce logiciel.");
                return;
            }
            if (Ask("ReShade", "Désinstaller ReShade de FiveM ?"))
                RunTask((log, p) => Core.UninstallReshade(fivem, log, p));
        }

        void OpenFivem()
        {
            string p = FivemPath();
            if (p != null) Process.Start("explorer.exe", "\"" + p + "\"");
        }

        void OpenBackups() => Process.Start("explorer.exe", "\"" + Core.DataDir() + "\"");
    }
}
