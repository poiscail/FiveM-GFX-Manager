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
    public partial class MainForm : Form
    {
        // Ligne de la liste des packs : un pack de la bibliothèque et/ou son installation,
        // un dossier (Folder) ou la ligne « Retour » (Up) quand on est dans un dossier
        class PackRow
        {
            public LibraryPack Lib;
            public PackInfo Inst;
            public string Folder;
            public bool Up;
            public bool IsPack => Folder == null && !Up;
            public string Key => Up ? "up" : Folder != null ? "dir:" + Folder : Lib?.Id ?? "inst:" + Inst.Id;
            public string Nom => Up ? "Retour" : Folder != null ? Core.FolderLeaf(Folder) : Lib?.Nom ?? Inst.Nom;
            public string Type => Lib?.Type ?? Inst?.Type ?? "Graphique";
        }

        const string PackDrag = "FiveMGfxManager.Packs";

        class Page
        {
            public string Title, Subtitle;
            public Control Body;
            public FlatButton Nav;
        }

        readonly InputBox fivemInput = new InputBox();
        readonly InputBox reshadeInput = new InputBox();
        readonly InputBox presetInput = new InputBox();
        readonly Segmented typeBox = new Segmented(Core.PackTypes);
        readonly Toggle shadersCheck = new Toggle("Télécharger aussi les shaders officiels (recommandé)");
        readonly DarkListView packList = new DarkListView();
        readonly Label emptyList = new Label();
        readonly Label pageTitle = new Label();
        readonly Label pageSubtitle = new Label();
        readonly Label fivemState = new Label();
        readonly SlimProgress progress = new SlimProgress();
        readonly RichTextBox logBox = new RichTextBox();
        readonly ToolTip tips = new ToolTip();
        readonly List<FlatButton> actionButtons = new List<FlatButton>();
        readonly List<Page> pages = new List<Page>();
        Page current, modsPage;
        bool busy;
        bool ModsView => current == modsPage;
        string Noun => ModsView ? "mod" : "pack";
        readonly Label typeLabel = Lbl("Type :", Theme.Muted, new Padding(0, 0, 10, 0));

        // Détails du pack sélectionné
        readonly Label detailEmpty = new Label();
        readonly TableLayoutPanel detailBody = new TableLayoutPanel();
        readonly Label detailName = new Label();
        readonly Badge detailType = new Badge();
        readonly Badge detailState = new Badge();
        readonly Label detailSize = new Label(), detailAdded = new Label(), detailInstalled = new Label(),
                       detailSource = new Label(), detailMods = new Label(), detailFolder = new Label(),
                       detailWeapons = new Label(), detailNote = new Label(), detailPreview = new Label();
        FlatButton installBtn, removeBtn, addModsBtn, typeBtn, deleteBtn, folderBtn;
        string folderFilter; // dossier ouvert dans la liste (null : liste principale)
        int dropIndex = -1;  // dossier survolé pendant un glisser-déposer

        // État des pages Sauvegarde et ReShade
        StatusCard backupCard, reshadeCard, quantvCard;
        readonly Label quantvList = new Label();

        TextBox fivemBox => fivemInput.Box;
        TextBox reshadeBox => reshadeInput.Box;
        TextBox presetBox => presetInput.Box;

        public MainForm()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "FiveM GFX Manager";
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            Font = Theme.Ui(10F);
            ClientSize = new Size(1200, 800);
            MinimumSize = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var cfg = Core.LoadConfig();
            fivemBox.Text = cfg.FivemApp ?? Core.DetectFivemApp() ?? "";

            var packBody = BuildPackPage();
            pages.Add(new Page
            {
                Title = "Packs", Body = packBody,
                Nav = new FlatButton("Packs", Glyph.Packs, ButtonKind.Nav),
            });
            // les mods utilisent la même page que les packs, filtrée sur le type Mod
            pages.Add(modsPage = new Page
            {
                Title = "Mods", Body = packBody,
                Nav = new FlatButton("Mods", Glyph.Mods, ButtonKind.Nav),
            });
            pages.Add(new Page
            {
                Title = "Sauvegarde d'origine", Body = BuildBackupPage(),
                Subtitle = "Une copie de FiveM.app sans aucun mod, pour pouvoir tout remettre comme avant.",
                Nav = new FlatButton("Sauvegarde d'origine", Glyph.Backup, ButtonKind.Nav),
            });
            pages.Add(new Page
            {
                Title = "ReShade", Body = BuildReshadePage(),
                Subtitle = "Filtres graphiques en jeu. Le menu s'ouvre avec la touche Début (Home).",
                Nav = new FlatButton("ReShade", Glyph.Reshade, ButtonKind.Nav),
            });
            pages.Add(new Page
            {
                Title = "QuantV", Body = BuildQuantVPage(),
                Subtitle = "Met à jour QuantV dans tes packs : tout seul depuis Téléchargements, ou avec une archive, un dossier ou un .rpf.",
                Nav = new FlatButton("QuantV", Glyph.Update, ButtonKind.Nav),
            });
            pages.Add(new Page
            {
                Title = "Graphismes", Body = BuildGraphicsPage(),
                Subtitle = "Réglages graphiques du jeu en un clic (fichier gta5_settings.xml de FiveM).",
                Nav = new FlatButton("Graphismes", Glyph.Graphics, ButtonKind.Nav),
            });
            pages.Add(presetsPage = new Page
            {
                Title = "Presets ReShade", Body = BuildPresetsPage(),
                Nav = new FlatButton("Presets ReShade", Glyph.Presets, ButtonKind.Nav),
            });
            pages.Add(new Page
            {
                Title = "Profils", Body = BuildProfilesPage(),
                Subtitle = "Packs + preset ReShade + graphismes enregistrés ensemble, remis d'un clic (ex. RP, PvP).",
                Nav = new FlatButton("Profils", Glyph.Profiles, ButtonKind.Nav),
            });

            // la zone principale est ajoutée avant la barre latérale pour être placée à sa droite
            Controls.Add(BuildMain());
            Controls.Add(BuildSidebar());

            ResumeLayout(true);
            ShowPage(pages[0]);
            RefreshStatus();
            Log("Prêt. Ferme FiveM avant toute opération.", Theme.Muted);
            Shown += (s, e) =>
            {
                ActiveControl = pages[0].Nav;
                StartupChecks();
            };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(this);
        }

        // ------------------------------------------------------ structure --

        Control BuildSidebar()
        {
            var side = new TableLayoutPanel
            {
                Dock = DockStyle.Left, Width = 250, BackColor = Theme.Sidebar, ColumnCount = 1,
                Padding = new Padding(14, 20, 14, 16),
            };
            side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // logo + nom
            var brand = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(4, 0, 0, 26) };
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var logo = new Label
            {
                Text = Theme.Icons(1) != null ? Glyph.Game : "G", Font = Theme.Icons(20F) ?? Theme.Semibold(20F),
                ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, 4, 10, 0),
            };
            brand.Controls.Add(logo, 0, 0);
            brand.SetRowSpan(logo, 2);
            brand.Controls.Add(new Label
            {
                Text = "FiveM GFX", Font = Theme.Semibold(14F), ForeColor = Theme.Fg, AutoSize = true, Margin = new Padding(0),
            }, 1, 0);
            var v = typeof(MainForm).Assembly.GetName().Version;
            brand.Controls.Add(new Label
            {
                Text = $"Manager · v{v.Major}.{v.Minor}.{v.Build}", Font = Theme.Ui(9F), ForeColor = Theme.Muted,
                AutoSize = true, Margin = new Padding(1, 0, 0, 0),
            }, 1, 1);
            AddRow(side, brand);

            AddRow(side, Caption("MENU", new Padding(6, 0, 0, 6)));
            foreach (var p in pages)
            {
                var page = p;
                p.Nav.Dock = DockStyle.Fill;
                p.Nav.Margin = new Padding(0, 0, 0, 4);
                p.Nav.Click += (s, e) => ShowPage(page);
                AddRow(side, p.Nav);
            }

            side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            side.Controls.Add(new Panel { Margin = new Padding(0) }, 0, side.RowStyles.Count - 1);

            AddRow(side, Caption("OUTILS", new Padding(6, 0, 0, 6)));
            FlatButton tools = null;
            var launch = SideBtn("Lancer FiveM", Glyph.Play, LaunchFivem);
            tips.SetToolTip(launch, "Ouvre Steam s'il est fermé (et attend qu'il soit connecté), puis FiveM");
            soundsBtn = SideBtn("Couper les sons", Glyph.Sound, ToggleSounds);
            tips.SetToolTip(soundsBtn, "Serveurs « pure » : ils refusent les packs son (FiveM plante au chargement).\n" +
                                       "Retire les packs son pour y jouer, puis remets-les en un clic.");
            tools = SideBtn("Outils…", Glyph.Clear, () => ShowToolsMenu(tools));
            var undo = SideBtn("Annuler la dernière action", Glyph.History, UndoLast);
            tips.SetToolTip(undo, "Ctrl+Z");
            updateBtn = Btn("Mise à jour disponible", InstallUpdate, ButtonKind.Primary, Glyph.Download);
            updateBtn.AutoSize = false;
            updateBtn.Dock = DockStyle.Fill;
            updateBtn.Height = 38;
            updateBtn.Margin = new Padding(0, 6, 0, 2);
            updateBtn.Visible = false;
            foreach (var b in new[] { launch, soundsBtn, undo, tools, updateBtn })
                AddRow(side, b);

            fivemState.AutoSize = true;
            fivemState.Font = Theme.Ui(9F);
            fivemState.Margin = new Padding(8, 14, 0, 0);
            AddRow(side, fivemState);
            fivemBox.TextChanged += (s, e) => UpdateFivemState();
            return side;
        }

        FlatButton SideBtn(string text, string glyph, Action onClick)
        {
            var b = Btn(text, onClick, ButtonKind.Nav, glyph);
            b.Dock = DockStyle.Fill;
            b.Height = 38;
            b.Margin = new Padding(0, 0, 0, 2);
            return b;
        }

        void UpdateFivemState()
        {
            bool ok = Core.IsValidFivemApp(Clean(fivemBox.Text));
            fivemState.Text = ok ? "●  FiveM.app trouvé" : "●  FiveM.app introuvable";
            fivemState.ForeColor = ok ? Theme.Ok : Theme.Danger;
        }

        Control BuildMain()
        {
            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Theme.Bg, Padding = new Padding(30, 22, 30, 22),
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // titre de la page
            pageTitle.AutoSize = true;
            pageTitle.Font = Theme.Semibold(20F);
            pageTitle.Margin = new Padding(0);
            pageSubtitle.AutoSize = true;
            pageSubtitle.ForeColor = Theme.Muted;
            pageSubtitle.Margin = new Padding(1, 2, 0, 16);
            AddRow(main, pageTitle);
            AddRow(main, pageSubtitle);

            // dossier FiveM.app
            var path = new Card { Height = 66, Dock = DockStyle.Fill, Padding = new Padding(16, 14, 14, 14), Margin = new Padding(0, 0, 0, 16) };
            var row = Columns(5);
            row.Controls.Add(GlyphLabel(Glyph.Folder, Theme.Accent), 0, 0);
            row.Controls.Add(Lbl("Dossier FiveM.app", Theme.Fg, new Padding(0, 0, 14, 0)), 1, 0);
            fivemInput.Dock = DockStyle.Fill;
            fivemInput.Placeholder = @"%LOCALAPPDATA%\FiveM\FiveM.app";
            row.Controls.Add(fivemInput, 2, 0);
            row.Controls.Add(Btn("Parcourir…", ChooseFivem, ButtonKind.Secondary, Glyph.OpenFolder), 3, 0);
            row.Controls.Add(Btn("Détecter", DetectFivem, ButtonKind.Secondary, Glyph.Search, last: true), 4, 0);
            path.Controls.Add(row);
            AddRow(main, path);

            // pages
            var host = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 16) };
            foreach (var p in pages.Where(p => !host.Controls.Contains(p.Body)))
            {
                p.Body.Dock = DockStyle.Fill;
                p.Body.Visible = false;
                host.Controls.Add(p.Body);
            }
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.Controls.Add(host, 0, main.RowStyles.Count - 1);

            // journal
            var activity = new Card { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 12) };
            var act = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            act.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var head = Columns(3);
            head.Controls.Add(GlyphLabel(Glyph.Activity, Theme.Muted), 0, 0);
            head.Controls.Add(Lbl("Activité", Theme.Fg, new Padding(0, 0, 18, 0), Theme.Semibold(10.5F)), 1, 0);
            progress.Dock = DockStyle.Fill;
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            progress.Margin = new Padding(0);
            head.Controls.Add(progress, 2, 0);
            head.Margin = new Padding(0, 0, 0, 8);
            AddRow(act, head);
            logBox.Dock = DockStyle.Fill;
            logBox.ReadOnly = true;
            logBox.DetectUrls = false;
            logBox.BackColor = Theme.Card;
            logBox.ForeColor = Theme.Fg;
            logBox.BorderStyle = BorderStyle.None;
            logBox.Font = new Font("Consolas", 9.5F);
            logBox.Margin = new Padding(2, 0, 0, 0);
            Theme.DarkScrollbars(logBox);
            act.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            act.Controls.Add(logBox, 0, 1);
            activity.Controls.Add(act);
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));
            main.Controls.Add(activity, 0, main.RowStyles.Count - 1);
            return main;
        }

        void ShowPage(Page page)
        {
            bool wasMods = ModsView;
            current = page;
            foreach (var p in pages)
            {
                p.Nav.Selected = p == page;
                p.Body.Visible = p.Body == page.Body;
            }
            pageTitle.Text = page.Title;
            pageSubtitle.Text = page.Subtitle;
            if (wasMods != ModsView)
            {
                // passage Packs <-> Mods : même liste, autre filtre
                typeLabel.Visible = typeBox.Visible = !ModsView;
                packList.SelectedItems.Clear();
                folderFilter = null;
                RefreshPackList();
            }
            ShowFolderTitle();
        }

        // titre « Packs › dossier » quand un dossier est ouvert
        void ShowFolderTitle()
        {
            if (current != pages[0] && current != modsPage) return;
            pageTitle.Text = Searching ? $"{current.Title}  ›  recherche"
                           : folderFilter == null ? current.Title : $"{current.Title}  ›  {folderFilter.Replace("/", "  ›  ")}";
        }

        // ---------------------------------------------------- petits outils --

        static void AddRow(TableLayoutPanel t, Control c)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(c, 0, t.RowStyles.Count - 1);
        }

        // Ligne de contrôles : la colonne 2 prend la place libre
        static TableLayoutPanel Columns(int count, int fill = 2)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = count, RowCount = 1, Margin = new Padding(0) };
            for (int i = 0; i < count; i++)
                t.ColumnStyles.Add(i == fill ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        static Label Lbl(string text, Color? color = null, Padding? margin = null, Font font = null)
        {
            var l = new Label
            {
                Text = text, AutoSize = true, ForeColor = color ?? Theme.Muted, Anchor = AnchorStyles.Left,
                Margin = margin ?? new Padding(0),
            };
            if (font != null) l.Font = font;
            return l;
        }

        static Label Caption(string text, Padding margin) =>
            Lbl(text, Theme.Mix(Theme.Muted, Theme.Bg, 0.25f), margin, Theme.Ui(8F, FontStyle.Bold));

        static Label GlyphLabel(string glyph, Color color, float size = 13F) => new Label
        {
            Text = Theme.Icons(size) != null ? glyph : "", Font = Theme.Icons(size) ?? Theme.Ui(size), ForeColor = color,
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 10, 0),
        };

        FlatButton Btn(string text, Action onClick, ButtonKind kind = ButtonKind.Secondary, string glyph = null, bool last = false)
        {
            var b = new FlatButton(text, glyph, kind) { AutoSize = true, Anchor = AnchorStyles.Left };
            if (last) b.Margin = new Padding(0);
            b.Click += (s, e) => onClick();
            actionButtons.Add(b);
            return b;
        }

        static FlowLayoutPanel ButtonBar() => new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 16, 0, 0),
        };

        // ------------------------------------------------------------ Packs --

        Control BuildPackPage()
        {
            var page = new TableLayoutPanel { ColumnCount = 2, Margin = new Padding(0) };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // ajout à la bibliothèque
            var bar = Columns(6, fill: 5);
            bar.Controls.Add(typeLabel, 0, 0);
            typeBox.Anchor = AnchorStyles.Left;
            typeBox.AutoSize = true;
            typeBox.Margin = new Padding(0, 0, 14, 0);
            bar.Controls.Add(typeBox, 1, 0);
            bar.Controls.Add(Btn("Ajouter des archives…", ImportArchives, ButtonKind.Primary, Glyph.Add), 2, 0);
            bar.Controls.Add(Btn("Ajouter un dossier…", ImportFolder, ButtonKind.Secondary, Glyph.Folder), 3, 0);
            folderBtn = Btn("Nouveau dossier…", AskNewFolder, ButtonKind.Ghost, Glyph.Folder);
            folderBtn.Margin = new Padding(14, 0, 0, 0);
            tips.SetToolTip(folderBtn, "Crée un dossier (dans le dossier ouvert). Double-clic sur un dossier pour l'ouvrir, " +
                                       "glisse les packs dessus pour les ranger");
            bar.Controls.Add(folderBtn, 4, 0);
            searchInput.Placeholder = "Rechercher…  (Ctrl+F)";
            searchInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            searchInput.MinimumSize = new Size(LogicalToDeviceUnits(120), 0);
            searchInput.Margin = new Padding(14, 0, 0, 0);
            searchInput.Box.TextChanged += (s, e) => { packList.SelectedItems.Clear(); RefreshPackList(); };
            searchInput.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { searchInput.Box.Text = ""; e.SuppressKeyPress = true; }
                if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Enter)
                {
                    packList.Focus();
                    if (packList.Items.Count > 0 && packList.SelectedItems.Count == 0) packList.Items[0].Selected = packList.Items[0].Focused = true;
                    e.SuppressKeyPress = true;
                }
            };
            tips.SetToolTip(searchInput.Box, "Cherche dans le nom, le type, le dossier et le commentaire (Échap pour effacer)");
            bar.Controls.Add(searchInput, 5, 0);
            bar.Margin = new Padding(0, 0, 0, 14);
            tips.SetToolTip(typeBox, "Type donné aux packs que tu ajoutes (modifiable ensuite)");
            page.Controls.Add(bar, 0, 0);
            page.SetColumnSpan(bar, 2);

            // liste
            var listCard = new Card { Dock = DockStyle.Fill, Padding = new Padding(6, 8, 6, 8), Margin = new Padding(0, 0, 16, 0) };
            packList.Dock = DockStyle.Fill;
            packList.View = View.Details;
            packList.FullRowSelect = true;
            packList.MultiSelect = true;
            packList.HideSelection = false;
            packList.BackColor = Theme.Card;
            packList.ForeColor = Theme.Fg;
            packList.BorderStyle = BorderStyle.None;
            packList.HeaderStyle = ColumnHeaderStyle.Clickable;
            packList.ShowItemToolTips = true;
            // hauteur des lignes
            packList.SmallImageList = new ImageList { ImageSize = new Size(1, LogicalToDeviceUnits(42)) };
            // largeurs en pixels « 96 dpi » : la liste ne suit pas toute seule le zoom de Windows
            packList.Columns.Add("NOM", 300);
            packList.Columns.Add("TYPE", LogicalToDeviceUnits(115));
            packList.Columns.Add("TAILLE", LogicalToDeviceUnits(85));
            packList.Columns.Add("MODS", LogicalToDeviceUnits(65));
            packList.Columns.Add("NOTE", LogicalToDeviceUnits(100));
            packList.Columns.Add("ÉTAT", LogicalToDeviceUnits(130));
            var headerFont = Theme.Ui(8F, FontStyle.Bold);
            var nameFont = Theme.Semibold(10F);
            var smallFont = Theme.Ui(9F);
            packList.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var p = new Pen(Theme.Border))
                    e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                var r = Rectangle.FromLTRB(e.Bounds.Left + LogicalToDeviceUnits(12), e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
                // colonne de tri : ▲ croissant, ▼ décroissant
                string text = e.ColumnIndex == sortColumn ? e.Header.Text + (sortDesc ? "  ▼" : "  ▲") : e.Header.Text;
                TextRenderer.DrawText(e.Graphics, text, headerFont, r, e.ColumnIndex == sortColumn ? Theme.Fg : Theme.Muted,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            };
            packList.ColumnClick += (s, e) => SortBy(e.Column);
            packList.MouseClick += (s, e) => ClickStars(e);
            packList.DrawItem += (s, e) => { };
            packList.DrawSubItem += (s, e) => DrawPackCell(e, nameFont, smallFont);
            packList.Resize += (s, e) => FitColumns();
            packList.SelectedIndexChanged += (s, e) => ShowDetails();
            // double-clic : ouvre un dossier, remonte avec « Retour », installe un pack
            packList.MouseDoubleClick += (s, e) =>
            {
                var r = packList.GetItemAt(e.X, e.Y)?.Tag as PackRow;
                if (r == null) return;
                if (r.Up) OpenFolder(Core.ParentFolder(folderFilter));
                else if (r.Folder != null) OpenFolder(r.Folder);
                else if (!busy && StarAt(e.Location) < 0) InstallSelected();
            };
            // Suppr : retire de FiveM les packs sélectionnés (ou les supprime de la bibliothèque s'ils ne sont pas installés)
            packList.KeyDown += (s, e) =>
            {
                var entry = SelectedEntry();
                if (e.KeyCode == Keys.Back && folderFilter != null && !Searching) { OpenFolder(Core.ParentFolder(folderFilter)); return; }
                if (e.KeyCode == Keys.Enter && entry != null && !entry.IsPack)
                {
                    OpenFolder(entry.Up ? Core.ParentFolder(folderFilter) : entry.Folder);
                    return;
                }
                if (busy) return;
                if (entry?.Folder != null)
                {
                    if (e.KeyCode == Keys.F2) AskRenameFolder(entry.Folder);
                    else if (e.KeyCode == Keys.Delete) AskDeleteFolder(entry.Folder);
                    return;
                }
                if (e.KeyCode == Keys.F2) { RenameSelected(); return; }
                if (e.KeyCode != Keys.Delete || SelectedRows().Count == 0) return;
                if (SelectedRows().Any(r => r.Inst != null)) UninstallSelected();
                else DeleteSelected();
            };
            // glisser des packs sur un dossier (ou sur « Retour ») pour les y ranger
            packList.ItemDrag += (s, e) =>
            {
                var ids = SelectedRows().Where(r => r.Lib != null).Select(r => r.Lib.Id).ToArray();
                if (ids.Length > 0 && !busy) packList.DoDragDrop(new DataObject(PackDrag, ids), DragDropEffects.Move);
            };
            packList.ContextMenuStrip = BuildPackMenu();


            // message quand la bibliothèque est vide
            emptyList.Dock = DockStyle.Fill;
            emptyList.TextAlign = ContentAlignment.MiddleCenter;
            emptyList.ForeColor = Theme.Muted;
            emptyList.Text = EmptyPacks;

            // glisser-déposer des archives / dossiers sur la liste (sur un dossier : importées dedans)
            emptyList.AllowDrop = packList.AllowDrop = true;
            emptyList.DragEnter += (s, e) =>
                e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None;
            emptyList.DragDrop += (s, e) => Import((string[])e.Data.GetData(DataFormats.FileDrop));
            DragEventHandler over = (s, e) =>
            {
                var target = DropTarget(e);
                SetDropIndex(target?.Index ?? -1);
                e.Effect = busy ? DragDropEffects.None
                         : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy
                         : e.Data.GetDataPresent(PackDrag) && target != null ? DragDropEffects.Move
                         : DragDropEffects.None;
            };
            packList.DragEnter += over;
            packList.DragOver += over;
            packList.DragLeave += (s, e) => SetDropIndex(-1);
            packList.DragDrop += (s, e) =>
            {
                var target = (PackRow)DropTarget(e)?.Tag;
                SetDropIndex(-1);
                if (busy) return;
                string dest = target == null ? folderFilter : target.Up ? Core.ParentFolder(folderFilter) : target.Folder;
                if (e.Data.GetDataPresent(PackDrag))
                {
                    if (target != null) Try(() => Core.MoveToFolder((string[])e.Data.GetData(PackDrag), dest));
                }
                else Import((string[])e.Data.GetData(DataFormats.FileDrop), dest);
            };
            listCard.Controls.Add(packList);
            listCard.Controls.Add(emptyList);
            page.Controls.Add(listCard, 0, 1);
            page.Controls.Add(BuildDetails(), 1, 1);
            return page;
        }

        ContextMenuStrip BuildPackMenu()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), Font = Theme.Ui(9.5F), ShowImageMargin = false };
            var install = new ToolStripMenuItem("Installer dans FiveM", null, (s, e) => InstallSelected());
            var remove = new ToolStripMenuItem("Retirer de FiveM", null, (s, e) => UninstallSelected()) { Tag = "danger" };
            var mods = new ToolStripMenuItem("Ajouter des mods au pack…", null, (s, e) => AddMods());
            var type = new ToolStripMenuItem("Changer le type…", null, (s, e) => ChangeType());
            var rename = new ToolStripMenuItem("Renommer…", null, (s, e) => RenameSelected()) { ShortcutKeyDisplayString = "F2" };
            var folder = new ToolStripMenuItem("Ranger dans un dossier");
            folder.DropDown = new ToolStripDropDownMenu { Renderer = new DarkMenuRenderer(), Font = menu.Font, ShowImageMargin = false };
            var fav = new ToolStripMenuItem("Favori", null, (s, e) => ToggleFavorite());
            var note = new ToolStripMenuItem("Note");
            note.DropDown = new ToolStripDropDownMenu { Renderer = new DarkMenuRenderer(), Font = menu.Font, ShowImageMargin = false };
            var comment = new ToolStripMenuItem("Commentaire…", null, (s, e) => EditComment());
            var preview = new ToolStripMenuItem("Aperçu de l'installation…", null, (s, e) => ShowInstallPreview());
            var sep1 = new ToolStripSeparator();
            var sep2 = new ToolStripSeparator();
            var delete = new ToolStripMenuItem("Supprimer de la bibliothèque", null, (s, e) => DeleteSelected()) { Tag = "danger" };
            // clic droit sur un dossier, ou dans le vide
            string dir = null;
            var openDir = new ToolStripMenuItem("Ouvrir", null, (s, e) => OpenFolder(dir));
            var renameDir = new ToolStripMenuItem("Renommer le dossier…", null, (s, e) => AskRenameFolder(dir)) { ShortcutKeyDisplayString = "F2" };
            var deleteDir = new ToolStripMenuItem("Supprimer le dossier", null, (s, e) => AskDeleteFolder(dir)) { Tag = "danger" };
            var newDir = new ToolStripMenuItem("Nouveau dossier…", null, (s, e) => AskNewFolder());
            menu.Items.AddRange(new ToolStripItem[]
            {
                install, preview, remove, sep1, fav, note, comment, mods, type, rename, folder, sep2, delete,
                openDir, renameDir, deleteDir, newDir,
            });
            foreach (ToolStripItem i in menu.Items) { i.ForeColor = Theme.Fg; i.Padding = new Padding(4, 3, 4, 3); }
            menu.Opening += (s, e) =>
            {
                foreach (ToolStripItem i in menu.Items) i.Visible = false;
                var pt = packList.PointToClient(Cursor.Position);
                var hit = packList.GetItemAt(pt.X, pt.Y)?.Tag as PackRow;
                if (busy || hit?.Up == true) { e.Cancel = true; return; }
                if (hit == null) { newDir.Visible = true; return; }
                if (hit.Folder != null)
                {
                    dir = hit.Folder;
                    openDir.Visible = renameDir.Visible = deleteDir.Visible = true;
                    return;
                }
                var rows = SelectedRows();
                if (rows.Count == 0) { e.Cancel = true; return; }
                int inst = rows.Count(r => r.Inst != null), del = rows.Count(r => r.Lib != null && r.Inst == null);
                bool one = rows.Count == 1;
                install.Visible = one && rows[0].Inst == null;
                preview.Visible = install.Visible && rows[0].Lib != null;
                remove.Visible = inst > 0;
                remove.Text = inst > 1 ? $"Retirer les {inst} packs de FiveM" : "Retirer de FiveM";
                mods.Visible = type.Visible = rename.Visible = one && rows[0].Lib != null;
                var libIds = rows.Where(r => r.Lib != null).Select(r => r.Lib.Id).ToList();
                folder.Visible = fav.Visible = note.Visible = libIds.Count > 0;
                comment.Visible = one && rows[0].Lib != null;
                if (folder.Visible)
                {
                    FillFolderMenu(folder.DropDownItems, libIds, rows.First(r => r.Lib != null).Lib.Dossier);
                    var libs = rows.Where(r => r.Lib != null).Select(r => r.Lib).ToList();
                    fav.Checked = libs.All(l => l.Favori);
                    note.DropDownItems.Clear();
                    for (int n = 0; n <= 5; n++)
                    {
                        int value = n;
                        note.DropDownItems.Add(MenuItem(n == 0 ? "Aucune" : new string('★', n) + new string('☆', 5 - n),
                            () => Try(() => { foreach (var id in libIds) Core.SetRating(id, value); }),
                            libs.All(l => l.Note == value)));
                    }
                }
                delete.Visible = del > 0;
                delete.Text = del > 1 ? $"Supprimer les {del} packs de la bibliothèque" : "Supprimer de la bibliothèque";
                bool middle = mods.Visible || folder.Visible;
                sep1.Visible = (install.Visible || remove.Visible) && (middle || delete.Visible);
                sep2.Visible = middle && delete.Visible;
            };
            return menu;
        }

        void DrawPackCell(DrawListViewSubItemEventArgs e, Font nameFont, Font smallFont)
        {
            var r = (PackRow)e.Item.Tag;
            var g = e.Graphics;
            var bounds = e.Bounds;
            if (e.ColumnIndex == 0) bounds.Width = packList.Columns[0].Width;
            bool sel = e.Item.Selected;
            var back = e.ItemIndex == dropIndex ? Theme.Mix(Theme.Card, Theme.Accent, 0.3f)
                     : sel ? Theme.Mix(Theme.Card, Theme.Accent, 0.12f) : Theme.Card;
            using (var b = new SolidBrush(back)) g.FillRectangle(b, bounds);
            using (var p = new Pen(Theme.Mix(Theme.Card, Theme.Border, 0.7f)))
                g.DrawLine(p, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            float k = DeviceDpi / 96f;
            var cell = Rectangle.FromLTRB(bounds.Left + (int)(12 * k), bounds.Top, bounds.Right - (int)(6 * k), bounds.Bottom - 1);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding |
                                          TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
            if (!r.IsPack)
            {
                // dossier / Retour : icône + nom, nombre de packs en dernière colonne
                if (e.ColumnIndex == 0)
                {
                    if (sel) Theme.FillRound(g, new Rectangle(bounds.Left, bounds.Top + (int)(8 * k), (int)(3 * k),
                                                              bounds.Height - (int)(17 * k)), (int)(2 * k), Theme.Accent);
                    using (var icons = Theme.Icons(13F))
                        if (icons != null)
                        {
                            TextRenderer.DrawText(g, r.Up ? Glyph.Back : Glyph.Folder, icons, cell, r.Up ? Theme.Muted : Theme.Accent, flags);
                            cell.X += (int)(28 * k);
                            cell.Width -= (int)(28 * k);
                        }
                    TextRenderer.DrawText(g, e.SubItem.Text, nameFont, cell, r.Up ? Theme.Muted : Theme.Fg, flags);
                }
                else TextRenderer.DrawText(g, e.SubItem.Text, smallFont, cell, Theme.Muted, flags);
                return;
            }
            switch (e.ColumnIndex)
            {
                case 0:
                    if (sel) Theme.FillRound(g, new Rectangle(bounds.Left, bounds.Top + (int)(8 * k), (int)(3 * k),
                                                              bounds.Height - (int)(17 * k)), (int)(2 * k), Theme.Accent);
                    if (r.Lib?.Favori == true)
                    {
                        TextRenderer.DrawText(g, "★", nameFont, cell, Theme.Accent, flags);
                        cell.X += (int)(20 * k);
                        cell.Width -= (int)(20 * k);
                    }
                    TextRenderer.DrawText(g, e.SubItem.Text, nameFont, cell, Theme.Fg, flags);
                    break;
                case 1:
                    Theme.DrawBadge(g, cell, e.SubItem.Text, Theme.TypeColor(e.SubItem.Text), back, smallFont, k);
                    break;
                case NoteColumn:
                    // 5 étoiles, cliquables
                    if (r.Lib == null) break;
                    for (int i = 0; i < 5; i++)
                    {
                        var star = new Rectangle(cell.X + (int)(i * StarWidth * k), cell.Y, (int)(StarWidth * k), cell.Height);
                        TextRenderer.DrawText(g, i < r.Lib.Note ? "★" : "☆", smallFont, star,
                                              i < r.Lib.Note ? Theme.Accent : Theme.Mix(Theme.Muted, Theme.Card, 0.4f),
                                              TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
                    }
                    break;
                case 5:
                    var color = r.Inst != null ? (r.Lib != null ? Theme.Ok : Theme.Accent) : Theme.Muted;
                    int dot = (int)(7 * k);
                    using (var b = new SolidBrush(color))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.FillEllipse(b, cell.X, cell.Y + (cell.Height - dot) / 2, dot, dot);
                    }
                    cell.X += dot + (int)(8 * k);
                    cell.Width -= dot + (int)(8 * k);
                    TextRenderer.DrawText(g, e.SubItem.Text, smallFont, cell, r.Inst != null ? Theme.Fg : Theme.Muted, flags);
                    break;
                default:
                    TextRenderer.DrawText(g, e.SubItem.Text, smallFont, cell, Theme.Muted, flags);
                    break;
            }
        }

        Control BuildDetails()
        {
            var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(20) };

            detailEmpty.Dock = DockStyle.Fill;
            detailEmpty.TextAlign = ContentAlignment.MiddleCenter;
            detailEmpty.ForeColor = Theme.Muted;
            detailEmpty.Text = "Sélectionne un pack dans la liste\npour voir ses détails et l'installer.";

            detailBody.Dock = DockStyle.Fill;
            detailBody.ColumnCount = 1;
            detailBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(detailBody, Caption("PACK SÉLECTIONNÉ", new Padding(0, 0, 0, 6)));

            detailName.Font = Theme.Semibold(14F);
            detailName.AutoSize = false;
            detailName.AutoEllipsis = true;
            detailName.Height = 54;
            detailName.Dock = DockStyle.Fill;
            detailName.Margin = new Padding(0, 0, 0, 6);
            AddRow(detailBody, detailName);

            var badges = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
            badges.Controls.Add(detailType);
            badges.Controls.Add(detailState);
            detailType.Font = detailState.Font = Theme.Ui(9F);
            AddRow(detailBody, badges);

            var grid = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (var (key, value) in new[]
            {
                ("Taille", detailSize), ("Ajouté le", detailAdded), ("Installé le", detailInstalled),
                ("Source", detailSource), ("Mods ajoutés", detailMods), ("Dossier", detailFolder),
                ("Armes", detailWeapons), ("Note", detailNote), ("Installation", detailPreview),
            })
            {
                int row = grid.RowCount++;
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                grid.Controls.Add(new Label
                {
                    Text = key, ForeColor = Theme.Muted, AutoSize = false, Width = 100, Height = 26,
                    TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0),
                }, 0, row);
                value.AutoSize = false;
                value.AutoEllipsis = true;
                value.Height = 26;
                value.Dock = DockStyle.Fill;
                value.TextAlign = ContentAlignment.MiddleLeft;
                value.Margin = new Padding(0);
                grid.Controls.Add(value, 1, row);
            }
            AddRow(detailBody, grid);
            detailNote.Cursor = detailPreview.Cursor = Cursors.Hand;
            detailNote.Click += (s, e) => EditComment();
            detailPreview.Click += (s, e) => ShowInstallPreview();

            detailBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailBody.Controls.Add(new Panel { Margin = new Padding(0) }, 0, detailBody.RowStyles.Count - 1);

            installBtn = Btn("Installer dans FiveM", InstallSelected, ButtonKind.Primary, Glyph.Download);
            removeBtn = Btn("Retirer de FiveM", UninstallSelected, ButtonKind.Danger, Glyph.Remove);
            addModsBtn = Btn("Ajouter des mods au pack…", AddMods, ButtonKind.Secondary, Glyph.Add);
            typeBtn = Btn("Changer le type…", ChangeType, ButtonKind.Secondary, Glyph.Tag);
            deleteBtn = Btn("Supprimer de la bibliothèque", DeleteSelected, ButtonKind.Ghost, Glyph.Delete);
            foreach (var b in new[] { installBtn, removeBtn, addModsBtn, typeBtn, deleteBtn })
            {
                b.AutoSize = false;
                b.Height = b.Kind == ButtonKind.Primary || b.Kind == ButtonKind.Danger ? 42 : 36;
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(0, 0, 0, 8);
                AddRow(detailBody, b);
            }
            deleteBtn.Margin = new Padding(0);

            card.Controls.Add(detailBody);
            card.Controls.Add(detailEmpty);
            return card;
        }

        void ShowDetails()
        {
            var rows = SelectedRows();
            var r = rows.FirstOrDefault();
            detailEmpty.Visible = r == null;
            detailBody.Visible = r != null;
            if (r == null) return;
            if (rows.Count > 1)
            {
                ShowMultiDetails(rows);
                return;
            }
            removeBtn.Text = "Retirer de FiveM";
            deleteBtn.Text = "Supprimer de la bibliothèque";

            detailName.Text = r.Nom;
            tips.SetToolTip(detailName, r.Nom);
            detailType.Set(r.Type, Theme.TypeColor(r.Type));
            detailState.Set(r.Inst == null ? "Non installé" : "Installé", r.Inst == null ? Theme.Muted : Theme.Ok);
            detailSize.Text = r.Lib != null ? Core.HumanSize(r.Lib.Taille) : "—";
            detailAdded.Text = r.Lib?.Date ?? "—";
            detailInstalled.Text = r.Inst?.Date ?? "—";
            detailSource.Text = r.Lib?.Source ?? "Installé hors bibliothèque";
            tips.SetToolTip(detailSource, detailSource.Text);
            bool mods = r.Lib?.Mods != null && r.Lib.Mods.Count > 0;
            detailMods.Text = mods ? $"{r.Lib.Mods.Count} : {string.Join(", ", r.Lib.Mods)}" : "aucun";
            tips.SetToolTip(detailMods, mods ? string.Join("\n", r.Lib.Mods) : "");
            detailFolder.Text = string.IsNullOrEmpty(r.Lib?.Dossier) ? "aucun" : r.Lib.Dossier;
            var weapons = new List<string>();
            try { if (r.Lib != null) weapons = Core.WeaponNames(Core.PackWeapons(r.Lib)); } catch { }
            detailWeapons.Text = weapons.Count == 0 ? "aucune" : $"{weapons.Count} : {string.Join(", ", weapons)}";
            tips.SetToolTip(detailWeapons, string.Join("\n", weapons));
            ShowNoteAndPreview(r);

            installBtn.Visible = r.Inst == null;
            removeBtn.Visible = r.Inst != null;
            addModsBtn.Visible = r.Lib != null;
            typeBtn.Visible = r.Lib != null && r.Type != Core.ModType; // un mod reste un mod
            deleteBtn.Visible = r.Lib != null && r.Inst == null;
        }

        void ShowMultiDetails(List<PackRow> rows)
        {
            int inst = rows.Count(x => x.Inst != null), del = rows.Count(x => x.Lib != null && x.Inst == null);
            string names = string.Join(", ", rows.Select(x => x.Nom));
            detailName.Text = $"{rows.Count} {Noun}s sélectionnés";
            tips.SetToolTip(detailName, string.Join("\n", rows.Select(x => x.Nom)));
            var types = rows.Select(x => x.Type).Distinct().ToList();
            detailType.Set(types.Count == 1 ? types[0] : "Plusieurs types", types.Count == 1 ? Theme.TypeColor(types[0]) : Theme.Muted);
            detailState.Set(inst == 0 ? "Aucun installé" : $"{inst} installé{(inst > 1 ? "s" : "")}", inst == 0 ? Theme.Muted : Theme.Ok);
            detailSize.Text = Core.HumanSize(rows.Where(x => x.Lib != null).Sum(x => x.Lib.Taille));
            detailAdded.Text = detailInstalled.Text = detailMods.Text = detailFolder.Text = detailWeapons.Text =
                detailNote.Text = detailPreview.Text = "—";
            detailPreview.ForeColor = Theme.Fg;
            tips.SetToolTip(detailWeapons, "");
            tips.SetToolTip(detailNote, "");
            tips.SetToolTip(detailPreview, "");
            detailSource.Text = names;
            tips.SetToolTip(detailSource, string.Join("\n", rows.Select(x => x.Nom)));

            installBtn.Visible = addModsBtn.Visible = typeBtn.Visible = false;
            removeBtn.Visible = inst > 0;
            removeBtn.Text = inst > 1 ? $"Retirer les {inst} packs de FiveM" : "Retirer de FiveM";
            deleteBtn.Visible = del > 0;
            deleteBtn.Text = del > 1 ? $"Supprimer les {del} packs de la bibliothèque" : "Supprimer de la bibliothèque";
        }

        void FitColumns()
        {
            int other = packList.Columns.Cast<ColumnHeader>().Skip(1).Sum(c => c.Width);
            packList.Columns[0].Width = Math.Max(LogicalToDeviceUnits(150), packList.ClientSize.Width - other - 1);
        }

        void RefreshPackList()
        {
            var selected = new HashSet<string>(SelectedRows().Select(x => x.Key));
            var installed = Core.InstalledPacks();
            var rows = Core.ListLibrary().Select(l => new PackRow { Lib = l, Inst = installed.FirstOrDefault(i => i.LibId == l.Id) })
                           .ToList();
            // packs installés sans passer par la bibliothèque (anciennes versions du logiciel)
            rows.AddRange(installed.Where(i => i.LibId == null || rows.All(r => r.Lib.Id != i.LibId))
                                   .Select(i => new PackRow { Inst = i }));
            var all = rows;
            var folders = Core.LibraryFolders();
            if (folderFilter != null && !folders.Contains(folderFilter, StringComparer.OrdinalIgnoreCase))
                folderFilter = null; // dossier supprimé ou renommé ailleurs
            bool In(PackRow r, string d) => string.Equals(r.Lib?.Dossier ?? "", d ?? "", StringComparison.OrdinalIgnoreCase);
            bool Under(PackRow r, string d) => Core.IsInFolder(r.Lib?.Dossier, d);
            var view = all.Where(r => (r.Type == Core.ModType) == ModsView).ToList();
            var entries = new List<PackRow>();
            if (Searching)
            {
                // recherche : tous les packs qui correspondent, sans les dossiers
                string q = searchInput.Box.Text.Trim();
                bool Has(string s) => s != null && s.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0;
                rows = view.Where(r => Has(r.Nom) || Has(r.Type) || Has(r.Lib?.Dossier) || Has(r.Lib?.Commentaire)).ToList();
            }
            else
            {
                rows = view.Where(r => In(r, folderFilter)).ToList();
                // sous-dossiers en tête de la liste ; dans un dossier, une ligne « Retour »
                if (folderFilter != null) entries.Add(new PackRow { Up = true });
                foreach (var d in folders.Where(d => string.Equals(Core.ParentFolder(d), folderFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    // un dossier qui ne contient que des mods n'apparaît pas dans les packs (et inversement)
                    if (!view.Any(r => Under(r, d)) && all.Any(r => Under(r, d))) continue;
                    entries.Add(new PackRow { Folder = d });
                }
            }
            entries.AddRange(Sorted(rows));
            ShowFolderTitle();

            packList.BeginUpdate();
            packList.Items.Clear();
            foreach (var r in entries)
            {
                int n = r.Folder != null ? view.Count(x => Under(x, r.Folder)) : 0;
                var item = new ListViewItem(!r.IsPack
                    ? new[] { r.Nom, "", "", "", "", r.Up ? "" : n == 0 ? "vide" : $"{n} {Noun}{(n > 1 ? "s" : "")}" }
                    : new[]
                    {
                        r.Nom,
                        r.Type,
                        r.Lib != null ? Core.HumanSize(r.Lib.Taille) : "",
                        r.Lib?.Mods != null && r.Lib.Mods.Count > 0 ? r.Lib.Mods.Count.ToString() : "",
                        "",
                        r.Inst == null ? "Bibliothèque" : r.Lib == null ? "Installé (hors bibl.)" : "Installé",
                    })
                { Tag = r };
                var tip = new List<string>();
                if (r.Up) tip.Add("Double-clic (ou Retour arrière) pour remonter d'un dossier");
                if (r.Folder != null) tip.Add("Double-clic pour ouvrir · glisse des " + Noun + "s dessus pour les ranger");
                if (Searching && !string.IsNullOrEmpty(r.Lib?.Dossier)) tip.Add("Dossier : " + r.Lib.Dossier.Replace("/", " › "));
                if (!string.IsNullOrEmpty(r.Lib?.Commentaire)) tip.Add("Commentaire : " + r.Lib.Commentaire);
                if (r.Lib?.Mods != null && r.Lib.Mods.Count > 0) tip.Add("Mods ajoutés : " + string.Join(", ", r.Lib.Mods));
                if (r.Lib?.Armes != null && r.Lib.Armes.Count > 0) tip.Add("Armes : " + string.Join(", ", Core.WeaponNames(r.Lib.Armes)));
                item.ToolTipText = string.Join("\n", tip);
                packList.Items.Add(item);
                if (selected.Contains(r.Key)) item.Selected = true;
            }
            packList.EndUpdate();
            FitColumns();
            emptyList.Text = Searching ? $"Aucun {Noun} ne correspond à « {searchInput.Box.Text.Trim()} »."
                           : ModsView ? EmptyMods : EmptyPacks;
            emptyList.Visible = entries.Count == 0;
            if (emptyList.Visible) emptyList.BringToFront();
            ShowDetails();

            foreach (var (page, mods, noun) in new[] { (pages[0], false, "pack"), (modsPage, true, "mod") })
            {
                var mine = all.Where(r => (r.Type == Core.ModType) == mods).ToList();
                int lib = mine.Count(r => r.Lib != null);
                var inst = mine.Where(r => r.Inst != null).Select(r => r.Nom).ToList();
                page.Subtitle = (lib == 0 ? "Bibliothèque vide" : $"{lib} {noun}{(lib > 1 ? "s" : "")} dans la bibliothèque") + "  ·  " +
                                (inst.Count == 0 ? "aucun installé"
                                    : $"installé{(inst.Count > 1 ? "s" : "")} : {string.Join(", ", inst)}");
                long size = Core.InstalledSize(mods);
                if (size > 0) page.Subtitle += $"  ·  {Core.HumanSize(size)} installés";
            }
            // poids total des packs et mods installés comparé à la mémoire de la carte graphique
            string heavy = Core.HeavyModsWarning();
            if (heavy != null) modsPage.Subtitle += "\n" + heavy;
            pageSubtitle.Text = current?.Subtitle;
        }

        PackRow SelectedRow() => SelectedRows().FirstOrDefault();

        // packs sélectionnés (sans les dossiers ni la ligne « Retour »)
        List<PackRow> SelectedRows() => packList.SelectedItems.Cast<ListViewItem>().Select(i => (PackRow)i.Tag).Where(r => r.IsPack).ToList();

        // ligne sélectionnée, dossier compris
        PackRow SelectedEntry() => packList.SelectedItems.Count == 1 ? (PackRow)packList.SelectedItems[0].Tag : null;

        PackRow RequireSelection()
        {
            var r = SelectedRow();
            if (r == null) Info("Packs", "Choisis d'abord un pack dans la liste.");
            return r;
        }

        // ------------------------------------------------ Sauvegarde, ReShade --

        // Carte d'état : grande icône, titre, détail
        class StatusCard : Card
        {
            readonly Label icon, title, detail;

            public StatusCard()
            {
                Dock = DockStyle.Fill;
                Height = 92;
                Padding = new Padding(22, 16, 22, 16);
                Margin = new Padding(0, 0, 0, 16);
                var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
                t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                t.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                icon = new Label
                {
                    Font = Theme.Icons(24F) ?? Theme.Ui(24F), AutoSize = true, Anchor = AnchorStyles.Left,
                    Margin = new Padding(0, 0, 18, 0),
                };
                title = new Label { Font = Theme.Semibold(13F), AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Bottom, Margin = new Padding(0) };
                detail = new Label { ForeColor = Theme.Muted, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(1, 2, 0, 0) };
                t.Controls.Add(icon, 0, 0);
                t.SetRowSpan(icon, 2);
                t.Controls.Add(title, 1, 0);
                t.Controls.Add(detail, 1, 1);
                Controls.Add(t);
            }

            public void Set(string glyph, Color color, string titleText, string detailText)
            {
                icon.Text = Theme.Icons(1) != null ? glyph : "●";
                icon.ForeColor = color;
                title.Text = titleText;
                detail.Text = detailText;
            }
        }

        // Carte avec un titre et un texte d'explication
        static Card TextCard(string title, string text, int height)
        {
            var card = new Card { Dock = DockStyle.Fill, Height = height, Padding = new Padding(22, 18, 22, 18), Margin = new Padding(0) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            AddRow(t, Lbl(title, Theme.Fg, new Padding(0, 0, 0, 10), Theme.Semibold(11F)));
            AddRow(t, Lbl(text, Theme.Muted));
            card.Controls.Add(t);
            return card;
        }

        static TableLayoutPanel Stack()
        {
            var t = new TableLayoutPanel { ColumnCount = 1, Margin = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        Control BuildBackupPage()
        {
            var page = Stack();
            backupCard = new StatusCard();
            AddRow(page, backupCard);
            AddRow(page, TextCard("Comment ça marche",
                "1.   Ferme FiveM et enlève tous tes mods : FiveM doit être PROPRE.\n" +
                "2.   Clique sur « Créer la sauvegarde » : les dossiers citizen, mods et plugins de FiveM.app sont copiés.\n" +
                "3.   En cas de problème, « Restaurer FiveM d'origine » supprime tous les mods, packs et ReShade,\n" +
                "      puis remet cette copie.", 150));
            var btns = ButtonBar();
            btns.Controls.Add(Btn("Créer / mettre à jour la sauvegarde", CreateBackup, ButtonKind.Primary, Glyph.Save));
            btns.Controls.Add(Btn("Restaurer FiveM d'origine", RestoreBackup, ButtonKind.Danger, Glyph.History));
            btns.Controls.Add(Btn("Ouvrir le dossier des sauvegardes", OpenBackups, ButtonKind.Secondary, Glyph.OpenFolder));
            AddRow(page, btns);
            return page;
        }

        Control BuildReshadePage()
        {
            var page = Stack();
            reshadeCard = new StatusCard();
            AddRow(page, reshadeCard);

            var card = new Card { Dock = DockStyle.Fill, Height = 236, Padding = new Padding(22, 18, 22, 18), Margin = new Padding(0) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var title = Lbl("Options d'installation", Theme.Fg, new Padding(0, 0, 0, 12), Theme.Semibold(11F));
            t.Controls.Add(title, 0, 0);
            t.SetColumnSpan(title, 4);

            reshadeInput.Placeholder = "Vide : dernière version téléchargée sur reshade.me";
            presetInput.Placeholder = "Optionnel : fichier .ini";
            int row = 1;
            foreach (var (label, input, choose) in new (string, InputBox, Action)[]
            {
                ("ReShade", reshadeInput, ChooseReshade), ("Preset", presetInput, ChoosePreset),
            })
            {
                var box = input;
                t.Controls.Add(Lbl(label, Theme.Fg, new Padding(0, 0, 16, 0)), 0, row);
                box.Dock = DockStyle.Fill;
                box.Margin = new Padding(0, 0, 8, 8);
                t.Controls.Add(box, 1, row);
                var c = Btn("Choisir…", choose, ButtonKind.Secondary, Glyph.OpenFolder);
                c.Margin = new Padding(0, 0, 8, 8);
                t.Controls.Add(c, 2, row);
                var x = Btn("Effacer", () => box.Box.Text = "", ButtonKind.Ghost);
                x.Margin = new Padding(0, 0, 0, 8);
                t.Controls.Add(x, 3, row);
                row++;
            }
            shadersCheck.Checked = true;
            shadersCheck.AutoSize = true;
            shadersCheck.Margin = new Padding(0, 4, 0, 10);
            t.Controls.Add(shadersCheck, 1, row++);
            var help = Lbl("Tu peux aussi choisir un ReShade_Setup_x.x.x.exe ou un ReShade64.dll déjà téléchargé.\n" +
                           @"ReShade est installé dans FiveM.app\plugins (dxgi.dll).", Theme.Muted);
            t.Controls.Add(help, 1, row);
            t.SetColumnSpan(help, 3);
            card.Controls.Add(t);
            AddRow(page, card);

            var btns = ButtonBar();
            btns.Controls.Add(Btn("Installer ReShade", InstallReshade, ButtonKind.Primary, Glyph.Download));
            var upd = Btn("Mettre à jour ReShade", UpdateReshade, ButtonKind.Secondary, Glyph.Update);
            tips.SetToolTip(upd, "Met la dernière version de ReShade (reshade.me) en gardant ton ReShade.ini, tes presets et tes shaders.\n" +
                                 "Marche aussi pour le ReShade fourni par un pack.");
            btns.Controls.Add(upd);
            btns.Controls.Add(Btn("Désinstaller ReShade", UninstallReshade, ButtonKind.Danger, Glyph.Delete));
            AddRow(page, btns);
            return page;
        }

        Control BuildQuantVPage()
        {
            var page = Stack();
            quantvCard = new StatusCard();
            AddRow(page, quantvCard);

            var card = new Card { Dock = DockStyle.Fill, Height = 230, Padding = new Padding(22, 18, 22, 18), Margin = new Padding(0) };
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            AddRow(t, Lbl("Packs qui contiennent QuantV", Theme.Fg, new Padding(0, 0, 0, 10), Theme.Semibold(11F)));
            quantvList.AutoSize = true;
            quantvList.ForeColor = Theme.Muted;
            AddRow(t, quantvList);
            AddRow(t, Lbl("Glisse ici la nouvelle version de QuantV (.zip, .7z, .rar, dossier ou .rpf), ou utilise les boutons.\n" +
                          "Les fichiers QuantV du pack sont remplacés (tes réglages .ini sont gardés) ; un pack installé\n" +
                          "est retiré puis réinstallé avec la nouvelle version. L'ancienne version est sauvegardée.",
                          Theme.Muted, new Padding(0, 14, 0, 0)));
            card.Controls.Add(t);
            AddRow(page, card);

            var btns = ButtonBar();
            var auto = Btn("Chercher la nouvelle version", UpdateQuantVAuto, ButtonKind.Primary, Glyph.Search);
            tips.SetToolTip(auto, "Prend tout seul la dernière version de QuantV arrivée dans ton dossier Téléchargements\n" +
                                  "et met à jour tes packs avec.");
            btns.Controls.Add(auto);
            btns.Controls.Add(Btn("Avec une archive…", UpdateQuantVFile, ButtonKind.Secondary, Glyph.Update));
            btns.Controls.Add(Btn("Avec un dossier…", UpdateQuantVFolder, ButtonKind.Secondary, Glyph.Folder));
            AddRow(page, btns);

            // glisser-déposer de la nouvelle version n'importe où sur la page
            foreach (var c in new Control[] { page, card, t, quantvList })
            {
                c.AllowDrop = true;
                c.DragEnter += (s, e) =>
                    e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) && !busy ? DragDropEffects.Copy : DragDropEffects.None;
                c.DragDrop += (s, e) =>
                {
                    var f = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (f != null && f.Length > 0) UpdateQuantV(f[0]);
                };
            }
            return page;
        }

        // ------------------------------------------------------- helpers --

        void Log(string msg, Color? color = null)
        {
            logBox.SelectionStart = logBox.TextLength;
            logBox.SelectionLength = 0;
            logBox.SelectionColor = color ?? Theme.Fg;
            logBox.AppendText(msg + Environment.NewLine);
            logBox.ScrollToCaret();
        }

        void RefreshStatus()
        {
            RefreshPackList();
            UpdateFivemState();
            UpdateSoundsBtn();
            var info = Core.OriginalBackupInfo();
            if (info != null)
                backupCard.Set(Glyph.Check, Theme.Ok, "Sauvegarde d'origine prête",
                               $"Créée le {info.Date}  ·  {info.Fichiers} fichiers");
            else
                backupCard.Set(Glyph.Warning, Theme.Accent, "Aucune sauvegarde d'origine",
                               "Crée-la maintenant, pendant que FiveM est sans mods.");
            var rs = Core.ReshadeStatus();
            string fivemOk = ValidFivem();
            string rsVer = fivemOk == null ? null : Core.ReshadeDllVersion(fivemOk);
            if (rs != null)
                reshadeCard.Set(Glyph.Check, Theme.Ok, $"ReShade {rsVer ?? rs.Version} installé", $"Installé le {rs.Date}");
            else if (rsVer != null)
                reshadeCard.Set(Glyph.Check, Theme.Ok, $"ReShade {rsVer} fourni par un pack",
                                "« Mettre à jour ReShade » met la dernière version par-dessus.");
            else
                reshadeCard.Set(Glyph.Reshade, Theme.Muted, "ReShade n'est pas installé",
                                "Il peut être téléchargé et installé automatiquement.");
            if (rsVer != null && !Core.ReshadeHasEffects(fivemOk))
                reshadeCard.Set(Glyph.Warning, Theme.Accent, $"ReShade {rsVer} n'a aucun effet (.fx)",
                                "Installe un pack graphique (QuantV…), ou « Installer ReShade » avec les shaders officiels cochés.");
            var qv = Core.FindQuantV();
            if (qv.Count > 0)
            {
                var last = qv.Where(q => q.DateRpf != null).Select(q => q.DateRpf.Value).DefaultIfEmpty().Max();
                quantvCard.Set(Glyph.Check, Theme.Ok, $"QuantV trouvé dans {qv.Count} pack{(qv.Count > 1 ? "s" : "")}",
                               (qv.Any(q => q.Installe) ? "Installé dans FiveM" : "Pas installé dans FiveM") +
                               (last != default(DateTime) ? $"  ·  version du {last:dd/MM/yyyy}" : ""));
                string nouv = Core.FindQuantVDownload();
                if (nouv != null)
                    quantvCard.Set(Glyph.Download, Theme.Accent, "Nouvelle version de QuantV dans Téléchargements",
                                   $"{Path.GetFileName(nouv)}  ·  clique sur « Chercher la nouvelle version » pour l'installer.");
                quantvList.Text = string.Join("\n", qv.Select(q =>
                    $"•  {q.Pack.Nom}{(q.Installe ? "  (installé)" : "")}  —  {q.Fichiers.Count} fichiers QuantV" +
                    (q.Rpf != null ? $", {q.Rpf.Replace('/', '\\')}" : "")));
            }
            else
            {
                quantvCard.Set(Glyph.Warning, Theme.Accent, "QuantV introuvable",
                               "Aucun pack de la bibliothèque ne contient QuantV. Ajoute-le d'abord dans l'onglet Packs.");
                quantvList.Text = "Aucun.";
            }
            RefreshGraphics();
            RefreshPresets();
            RefreshProfiles();
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
                    Log("✔ Terminé.", Theme.Ok);
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
                Log("✖ " + msg, Theme.Danger);
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
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Theme.Card, ForeColor = Theme.Fg,
                Font = Font, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20),
                AutoScaleMode = AutoScaleMode.None,
            })
            {
                f.HandleCreated += (s, e) => Theme.DarkTitleBar(f);
                var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                var choice = new Segmented(Core.PackTypes) { SelectedItem = current, Height = LogicalToDeviceUnits(36) };
                choice.Size = choice.GetPreferredSize(Size.Empty);
                choice.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(18));
                var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
                var ok = new FlatButton("OK", Glyph.Check, ButtonKind.Primary) { AutoSize = true, Height = LogicalToDeviceUnits(36) };
                ok.Padding = new Padding(LogicalToDeviceUnits(16), 0, LogicalToDeviceUnits(16), 0);
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                var cancel = new FlatButton("Annuler", null, ButtonKind.Ghost) { AutoSize = true, Height = LogicalToDeviceUnits(36) };
                cancel.Padding = ok.Padding;
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                flow.Controls.Add(Lbl("Type du pack :", Theme.Muted, new Padding(0, 0, 0, LogicalToDeviceUnits(8))));
                flow.Controls.Add(choice);
                flow.Controls.Add(buttons);
                f.Controls.Add(flow);
                f.Shown += (s, e) => choice.Focus();
                f.KeyPreview = true;
                f.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) f.DialogResult = DialogResult.OK;
                    if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel;
                };
                return f.ShowDialog(this) == DialogResult.OK ? choice.SelectedItem : null;
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
                Log("FiveM.app trouvé : " + p, Theme.Ok);
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

        // rangé dans le dossier ouvert
        void Import(string[] sources) => Import(sources, folderFilter);

        void Import(string[] sources, string folder)
        {
            if (sources == null || sources.Length == 0 || busy) return;
            string type = ModsView ? Core.ModType : (string)typeBox.SelectedItem;
            RunTask((log, p) =>
            {
                // un fichier refusé n'empêche pas d'importer les suivants
                var errors = new List<string>();
                for (int i = 0; i < sources.Length; i++)
                {
                    int k = i;
                    try { Core.ImportPack(sources[i], type, log, x => p((k + x) / sources.Length), folder); }
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
            if (!ConfirmSuspects(r.Lib)) return;

            var steps = new List<Action<Action<string>, Action<double>>>();
            if (!Core.HasOriginalBackup() &&
                Ask("Sauvegarde d'origine", "Aucune sauvegarde d'origine n'existe.\n\n" +
                    "La créer maintenant avant d'installer le pack ? (recommandé)\n" +
                    "⚠ FiveM doit être actuellement SANS mods."))
                steps.Add((log, p) => Core.CreateOriginalBackup(fivem, log, p));

            // packs déjà installés qui touchent les mêmes fichiers (ex. un autre pack son) : ils doivent être retirés,
            // et un seul pack graphique à la fois
            var conflicts = Core.ConflictingPacks(r.Lib.Id);
            var old = conflicts.Concat(Core.InstalledPacks().Where(i => i.Type == "Graphique" && r.Lib.Type == "Graphique"))
                               .GroupBy(i => i.Id).Select(g => g.First()).ToList();
            var removed = new HashSet<string>();
            var removedIds = new HashSet<string>();
            if (old.Count > 0)
            {
                string list = string.Join("\n", old.Select(i => $"  • {i.Nom} ({i.Type})"));
                if (Ask("Remplacer l'ancien pack",
                        $"Déjà installé{(old.Count > 1 ? "s" : "")} :\n{list}\n\n" +
                        $"Le{(old.Count > 1 ? "s" : "")} retirer de FiveM et installer « {r.Nom} » à la place ?"))
                    foreach (var s in old)
                    {
                        removed.Add(s.Nom);
                        removedIds.Add(s.Id);
                        steps.Add((log, p) => Core.UninstallPack(s.Id, fivem, log, p));
                    }
                else if (conflicts.Count > 0)
                    return; // impossible d'installer par-dessus : les deux packs modifient les mêmes fichiers
            }

            // avant de coller un pack son : sons d'armes déjà présents ailleurs (autres mods, autres dossiers)
            var sounds = Core.FindWeaponSounds(r.Lib.Id, fivem).Where(h => h.Pack == null || !removed.Contains(h.Pack)).ToList();
            var moveAside = new List<SoundHit>();
            if (sounds.Count > 0)
            {
                Log($"Sons d'armes déjà présents ({sounds.Count}) :");
                foreach (var h in sounds) Log("  " + h);
                var movable = sounds.Where(h => h.Deplacable).ToList();
                var others = sounds.Where(h => !h.Deplacable).ToList();
                string Lines(List<SoundHit> l) =>
                    string.Join("\n", l.Take(8).Select(h => "  • " + h)) + (l.Count > 8 ? $"\n  … (+{l.Count - 8}, voir le journal)" : "");
                string note = others.Count == 0 ? "" :
                    $"\n\nAutres sons d'armes trouvés (dans d'autres mods ou hors de FiveM), laissés en place :\n{Lines(others)}";
                if (movable.Count > 0)
                {
                    var res = MessageBox.Show(this,
                        $"Des sons d'armes sont déjà dans FiveM et pourraient passer devant ceux de « {r.Nom} » :\n" +
                        $"{Lines(movable)}{note}\n\nLes mettre de côté ? (ils reviennent si tu retires le pack)\n\n" +
                        "Oui : les mettre de côté et installer\nNon : installer sans y toucher\nAnnuler : ne rien faire",
                        "Sons d'armes trouvés", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (res == DialogResult.Cancel) return;
                    if (res == DialogResult.Yes) moveAside = movable;
                }
                else if (!Ask("Sons d'armes trouvés",
                              $"Des sons d'armes sont déjà présents :\n{Lines(others)}\n\n" +
                              "Ils peuvent changer ou couvrir ceux du pack. Installer quand même ?"))
                    return;
            }

            // armes déjà remplacées par un autre pack installé : on demande laquelle garder
            HashSet<string> keepOld = null;
            List<WeaponClash> clashes;
            try { clashes = Core.WeaponConflicts(r.Lib.Id, fivem).Where(c => !removedIds.Contains(c.Pack.Id)).ToList(); }
            catch (Exception ex) { clashes = new List<WeaponClash>(); Log("Armes : vérification impossible (" + ex.Message + ")"); }
            if (clashes.Count > 0)
            {
                keepOld = AskWeapons(r.Nom, clashes);
                if (keepOld == null) return;
            }

            string id = r.Lib.Id;
            steps.Add((log, p) => Core.InstallLibraryPack(id, fivem, log, p, moveAside, keepOld));
            RunTask((log, p) => { foreach (var s in steps) s(log, p); });
        }

        void UninstallSelected()
        {
            if (RequireSelection() == null) return;
            var inst = SelectedRows().Where(x => x.Inst != null).ToList();
            if (inst.Count == 0)
            {
                Info("Packs", "Aucun pack sélectionné n'est installé.\n" +
                              "Pour l'enlever de la liste, utilise « Supprimer de la bibliothèque ».");
                return;
            }
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            string what = inst.Count == 1 ? $"« {inst[0].Nom} »"
                        : $"ces {inst.Count} packs :\n" + string.Join("\n", inst.Select(x => "  • " + x.Nom)) + "\n\n";
            var ids = inst.Select(x => x.Inst.Id).ToList();
            if (Ask("Retirer de FiveM", $"Retirer {what} de FiveM et remettre les fichiers d'origine ?\n" +
                                        "(ils restent dans la bibliothèque)"))
                RunTask((log, p) => { foreach (var id in ids) Core.UninstallPack(id, fivem, log, p); });
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
            if (r.Type == Core.ModType)
            {
                Info("Mods", "Un mod ne peut pas devenir un pack : ajoute l'archive dans l'onglet Packs.");
                return;
            }
            string type = PickType($"Type de « {r.Nom} »", r.Lib.Type);
            if (type == null || type == r.Lib.Type) return;
            Core.SetPackType(r.Lib.Id, type);
            RefreshStatus();
        }

        void RenameSelected()
        {
            var r = RequireSelection();
            if (r == null || busy) return;
            if (r.Lib == null)
            {
                Info("Packs", "Ce pack a été installé hors bibliothèque : on ne peut pas le renommer.");
                return;
            }
            string name = AskText($"Renommer « {r.Nom} »", "Nouveau nom :", r.Nom);
            if (name == null || name == r.Nom) return;
            Try(() => Core.RenamePack(r.Lib.Id, name));
        }

        // Lance une petite action de la bibliothèque, puis rafraîchit la liste
        void Try(Action a)
        {
            try { a(); }
            catch (Exception ex) { Info("Erreur", ex.Message); }
            RefreshStatus();
        }

        static ToolStripMenuItem MenuItem(string text, Action onClick, bool check = false, string tag = null) =>
            new ToolStripMenuItem(text, null, (s, e) => onClick()) { Checked = check, Tag = tag, ForeColor = Theme.Fg, Padding = new Padding(4, 3, 4, 3) };

        // Sous-menu « Ranger dans un dossier »
        void FillFolderMenu(ToolStripItemCollection items, List<string> ids, string current)
        {
            items.Clear();
            foreach (var d in Core.LibraryFolders())
            {
                string folder = d;
                items.Add(MenuItem(d.Replace("/", "  ›  "), () => Try(() => Core.MoveToFolder(ids, folder)),
                                   string.Equals(d, current, StringComparison.OrdinalIgnoreCase)));
            }
            if (items.Count > 0) items.Add(new ToolStripSeparator());
            items.Add(MenuItem("Nouveau dossier…", () =>
            {
                string name = AskText("Nouveau dossier", "Nom du dossier :", "");
                if (name != null) Try(() => Core.MoveToFolder(ids, name));
            }));
            if (!string.IsNullOrEmpty(current))
                items.Add(MenuItem("Sortir du dossier", () => Try(() => Core.MoveToFolder(ids, null))));
        }

        // Ouvre un dossier dans la liste (null : revient à la liste principale)
        void OpenFolder(string folder)
        {
            string from = folderFilter;
            folderFilter = folder;
            packList.SelectedItems.Clear();
            if (Searching) searchInput.Box.Text = ""; // rafraîchit aussi la liste
            else RefreshPackList();
            // en remontant, le dossier qu'on vient de quitter reste sélectionné
            var back = packList.Items.Cast<ListViewItem>().FirstOrDefault(i => from != null && ((PackRow)i.Tag).Folder == from);
            if (back != null) { back.Selected = back.Focused = true; back.EnsureVisible(); }
            packList.Focus();
        }

        // Nom d'un dossier sans « / » ni « \ » (ils servent aux sous-dossiers), null si annulé
        string AskFolderLeaf(string title, string value)
        {
            while (true)
            {
                string name = AskText(title, "Nom du dossier :", value);
                if (name == null) return null;
                if (name.IndexOfAny(new[] { '/', '\\' }) < 0) return name.Trim();
                Info(title, "Le nom ne peut pas contenir / ou \\.");
                value = name;
            }
        }

        // nouveau dossier dans le dossier ouvert
        void AskNewFolder()
        {
            string name = AskFolderLeaf(folderFilter == null ? "Nouveau dossier" : $"Nouveau dossier dans « {Core.FolderLeaf(folderFilter)} »", "");
            if (name != null) Try(() => Core.CreateFolder(folderFilter == null ? name : folderFilter + "/" + name));
        }

        void AskRenameFolder(string cur)
        {
            string leaf = AskFolderLeaf("Renommer le dossier", Core.FolderLeaf(cur));
            if (leaf == null || leaf == Core.FolderLeaf(cur)) return;
            string parent = Core.ParentFolder(cur);
            string name = parent == null ? leaf : parent + "/" + leaf;
            Try(() =>
            {
                Core.RenameFolder(cur, name);
                if (Core.IsInFolder(folderFilter, cur)) folderFilter = name + folderFilter.Substring(cur.Length);
            });
        }

        void AskDeleteFolder(string cur)
        {
            string parent = Core.ParentFolder(cur);
            string where = parent == null ? "hors dossier" : $"rangés dans « {Core.FolderLeaf(parent)} »";
            if (!Ask("Supprimer le dossier", $"Supprimer le dossier « {Core.FolderLeaf(cur)} » et ses sous-dossiers ?\n" +
                                             $"(ses {Noun}s restent dans la bibliothèque, {where})"))
                return;
            Try(() =>
            {
                Core.DeleteFolder(cur);
                if (Core.IsInFolder(folderFilter, cur)) folderFilter = parent;
            });
        }

        // Dossier ou ligne « Retour » sous la souris pendant un glisser-déposer
        ListViewItem DropTarget(DragEventArgs e)
        {
            var pt = packList.PointToClient(new Point(e.X, e.Y));
            var item = packList.GetItemAt(pt.X, pt.Y);
            return item != null && !((PackRow)item.Tag).IsPack ? item : null;
        }

        void SetDropIndex(int i)
        {
            if (i == dropIndex) return;
            dropIndex = i;
            packList.Invalidate();
        }

        // Armes déjà remplacées par un pack installé : garder l'ancienne ou prendre la nouvelle, arme par arme.
        // Renvoie les fichiers à laisser à l'ancien pack, ou null si annulé.
        HashSet<string> AskWeapons(string pack, List<WeaponClash> clashes)
        {
            using (var f = new Form
            {
                Text = "Armes déjà remplacées", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Theme.Card, ForeColor = Theme.Fg,
                Font = Font, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(20),
                AutoScaleMode = AutoScaleMode.None,
            })
            {
                f.HandleCreated += (s, e) => Theme.DarkTitleBar(f);
                var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                flow.Controls.Add(Lbl($"« {pack} » remplace des armes déjà modifiées par un pack installé.\n" +
                                      "Pour chaque arme, choisis la version à garder :",
                                      Theme.Fg, new Padding(0, 0, 0, LogicalToDeviceUnits(14))));
                string[] choices = { "Garder l'ancienne", "Prendre la nouvelle" };
                var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(14)) };
                var all = new Segmented(choices) { SelectedItem = choices[1], Height = LogicalToDeviceUnits(32) };
                all.Size = all.GetPreferredSize(Size.Empty);
                table.Controls.Add(Lbl("Toutes les armes", Theme.Muted, new Padding(0, 0, LogicalToDeviceUnits(18), LogicalToDeviceUnits(10))), 0, 0);
                all.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(10));
                table.Controls.Add(all, 1, 0);
                var rows = new List<(WeaponClash c, Segmented s)>();
                foreach (var c in clashes)
                {
                    var seg = new Segmented(choices) { SelectedItem = choices[1], Height = LogicalToDeviceUnits(32) };
                    seg.Size = seg.GetPreferredSize(Size.Empty);
                    seg.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6));
                    var label = Lbl($"{c.Arme}  —  déjà dans « {c.Pack.Nom} »", Theme.Fg,
                                    new Padding(0, 0, LogicalToDeviceUnits(18), LogicalToDeviceUnits(6)));
                    tips.SetToolTip(label, string.Join("\n", c.Fichiers));
                    int row = table.RowCount++;
                    table.Controls.Add(label, 0, row);
                    table.Controls.Add(seg, 1, row);
                    rows.Add((c, seg));
                }
                all.SelectedIndexChanged += (s, e) => { foreach (var x in rows) x.s.SelectedItem = all.SelectedItem; };
                var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0) };
                var ok = new FlatButton("Installer", Glyph.Check, ButtonKind.Primary) { AutoSize = true, Height = LogicalToDeviceUnits(36) };
                ok.Padding = new Padding(LogicalToDeviceUnits(16), 0, LogicalToDeviceUnits(16), 0);
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                var cancel = new FlatButton("Annuler", null, ButtonKind.Ghost) { AutoSize = true, Height = LogicalToDeviceUnits(36) };
                cancel.Padding = ok.Padding;
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                flow.Controls.Add(table);
                flow.Controls.Add(buttons);
                f.Controls.Add(flow);
                f.KeyPreview = true;
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                if (f.ShowDialog(this) != DialogResult.OK) return null;
                return new HashSet<string>(rows.Where(x => x.s.SelectedItem == choices[0]).SelectMany(x => x.c.Fichiers),
                                           StringComparer.OrdinalIgnoreCase);
            }
        }

        void DeleteSelected()
        {
            if (RequireSelection() == null) return;
            var rows = SelectedRows();
            var del = rows.Where(x => x.Lib != null && x.Inst == null).ToList();
            if (del.Count == 0)
            {
                Info("Packs", rows.Any(x => x.Lib == null)
                    ? "Ce pack n'est pas dans la bibliothèque. Utilise « Retirer » pour l'enlever de FiveM."
                    : "Ces packs sont installés. Retire-les d'abord de FiveM.");
                return;
            }
            string what = del.Count == 1 ? $"« {del[0].Nom} »"
                        : $"ces {del.Count} packs :\n" + string.Join("\n", del.Select(x => "  • " + x.Nom)) + "\n\n";
            string skipped = rows.Count > del.Count ? "\n(les packs installés sont gardés)" : "";
            var ids = del.Select(x => x.Lib.Id).ToList();
            if (Ask("Supprimer", $"Supprimer {what} de la bibliothèque ?\n(il faudra les importer à nouveau pour les réutiliser){skipped}"))
                RunTask((log, p) => { foreach (var id in ids) Core.DeleteFromLibrary(id, log); });
        }

        void UpdateQuantVAuto()
        {
            if (busy) return;
            string f = Core.FindQuantVDownload();
            if (f != null) { UpdateQuantV(f); return; }
            if (Ask("QuantV", "Aucune nouvelle version de QuantV dans ton dossier Téléchargements.\n\n" +
                              "QuantV est payant : le logiciel ne peut pas le télécharger à ta place. Télécharge la dernière " +
                              "version (Patreon / Discord de QuantV) dans Téléchargements, puis reclique sur ce bouton :\n" +
                              "tout le reste se fait tout seul.\n\nOuvrir la page Patreon de QuantV ?"))
                Process.Start("https://www.patreon.com/QuantV");
        }

        void UpdateQuantVFile()
        {
            var f = PickFile("Nouvelle version de QuantV", PackFilter);
            if (f != null) UpdateQuantV(f);
        }

        void UpdateQuantVFolder()
        {
            var d = PickFolder("Dossier de la nouvelle version de QuantV (déjà décompressée)");
            if (d != null) UpdateQuantV(d);
        }

        void UpdateQuantV(string source)
        {
            if (busy) return;
            var qv = Core.FindQuantV();
            if (qv.Count == 0)
            {
                Info("QuantV introuvable", "Aucun pack de la bibliothèque ne contient QuantV.\nAjoute d'abord ton pack QuantV dans l'onglet Packs.");
                return;
            }
            string fivem = null;
            if (qv.Any(q => q.Installe))
            {
                fivem = FivemPath();
                if (fivem == null || !FivemClosed()) return;
            }
            RunTask((log, p) => Core.UpdateQuantV(source, fivem, log, p));
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

        void UpdateReshade()
        {
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (Core.ReshadeStatus() == null && Core.ReshadeDllVersion(fivem) == null)
            {
                Info("ReShade", "ReShade n'est pas installé : utilise « Installer ReShade ».");
                return;
            }
            // toujours la dernière version de reshade.me (le fichier choisi à la main sert à « Installer ReShade »)
            RunTask((log, p) => Core.UpdateReshade(fivem, null, log, p));
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
