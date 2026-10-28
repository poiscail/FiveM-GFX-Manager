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

        readonly TextBox fivemBox = new TextBox();
        readonly TextBox packBox = new TextBox();
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
            ClientSize = new Size(860, 660);
            MinimumSize = new Size(760, 600);
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
            AddTab(tabBar, pages, "Pack graphique", BuildPackTab());
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

            var row = Row(Panel, new Padding(0));
            row.Controls.Add(Lbl("Pack :"), 0, 0);
            row.Controls.Add(Box(packBox), 1, 0);
            row.Controls.Add(Btn("Archive…", ChoosePackArchive), 2, 0);
            row.Controls.Add(Btn("Dossier…", ChoosePackFolder), 3, 0);
            page.Controls.Add(row);

            page.Controls.Add(Lbl(
                "Formats acceptés : .zip, .7z, .rar ou dossier déjà décompressé (aucun logiciel externe nécessaire).\n" +
                "Le pack est décompressé, les dossiers citizen / mods / plugins sont mis au bon endroit dans FiveM.app\n" +
                "et les fichiers déjà existants sont remplacés (une copie est gardée pour pouvoir les remettre).", Muted));

            btns.Controls.Add(AccentBtn("Installer le pack", InstallPack));
            btns.Controls.Add(DangerBtn("Retirer le pack", UninstallPack));
            btns.Controls.Add(Btn("Vider le cache FiveM", ClearCache));
            btns.Controls.Add(Btn("Ouvrir FiveM.app", OpenFivem));
            page.Controls.Add(btns);
            return page;
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
            var pack = Core.InstalledPack();
            packStatus.Text = pack != null ? $"Pack installé : {pack.Nom}  (le {pack.Date})" : "Aucun pack installé";
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

        void ChoosePackArchive()
        {
            var f = PickFile("Choisir le pack graphique", "Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|Tous les fichiers|*.*");
            if (f != null) packBox.Text = f;
        }

        void ChoosePackFolder()
        {
            var d = PickFolder("Choisir le dossier du pack (déjà décompressé)");
            if (d != null) packBox.Text = d;
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

        void InstallPack()
        {
            string fivem = FivemPath();
            if (fivem == null) return;
            string source = Clean(packBox.Text);
            if (source == "" || (!File.Exists(source) && !Directory.Exists(source)))
            {
                MessageBox.Show(this, "Choisis d'abord une archive ou un dossier de pack graphique.", "Pack",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!FivemClosed()) return;

            var steps = new List<Action<Action<string>, Action<double>>>();
            if (!Core.HasOriginalBackup() &&
                Ask("Sauvegarde d'origine", "Aucune sauvegarde d'origine n'existe.\n\n" +
                    "La créer maintenant avant d'installer le pack ? (recommandé)\n" +
                    "⚠ FiveM doit être actuellement SANS mods."))
                steps.Add((log, p) => Core.CreateOriginalBackup(fivem, log, p));

            var current = Core.InstalledPack();
            if (current != null)
            {
                if (!Ask("Remplacer le pack", $"Le pack « {current.Nom} » est déjà installé.\nLe retirer et installer le nouveau ?"))
                    return;
                steps.Add((log, p) => Core.UninstallPack(fivem, log, p));
            }
            steps.Add((log, p) => Core.InstallPack(source, fivem, log, p));
            RunTask((log, p) => { foreach (var s in steps) s(log, p); });
        }

        void UninstallPack()
        {
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (Core.InstalledPack() == null)
            {
                Info("Pack", "Aucun pack installé par ce logiciel.\n" +
                             "Tu peux utiliser « Restaurer FiveM d'origine » dans l'onglet Sauvegarde.");
                return;
            }
            if (Ask("Retirer le pack", "Retirer le pack graphique et remettre les fichiers d'origine ?"))
                RunTask((log, p) => Core.UninstallPack(fivem, log, p));
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
            if (Core.InstalledPack() != null) msg += "\n\n⚠ Un pack est actuellement installé : il serait inclus dans la sauvegarde !";
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
