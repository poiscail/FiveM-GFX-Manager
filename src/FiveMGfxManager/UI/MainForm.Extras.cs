// Recherche, tri, notes / favoris, aperçu d'installation, vérification des packs,
// lancement de FiveM, annulation, export / import, mise à jour du logiciel

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
    partial class MainForm
    {
        readonly InputBox searchInput = new InputBox();
        int sortColumn = -1;
        bool sortDesc;
        FlatButton updateBtn, soundsBtn;
        UpdateInfo update;

        const int NoteColumn = 4;
        const int StarWidth = 16;

        bool Searching => searchInput.Box.Text.Trim().Length > 0;

        const string EmptyPacks = "Aucun pack pour l'instant.\n\nGlisse ici tes .zip, .7z, .rar ou .rpf,\nou clique sur « Ajouter des archives… ».";
        const string EmptyMods = "Aucun mod pour l'instant.\n\nGlisse ici tes .rpf, .zip, .7z ou .rar,\nils iront dans FiveM.app\\mods.";

        // ------------------------------------------------------------------ //
        //  Tri des colonnes (favoris en tête par défaut)
        // ------------------------------------------------------------------ //

        IEnumerable<PackRow> Sorted(IEnumerable<PackRow> rows)
        {
            var fav = rows.OrderByDescending(r => r.Lib?.Favori == true);
            switch (sortColumn)
            {
                case 0: return Order(rows, r => r.Nom.ToLowerInvariant());
                case 1: return Order(rows, r => r.Type);
                case 2: return Order(rows, r => r.Lib?.Taille ?? 0);
                case 3: return Order(rows, r => r.Lib?.Mods?.Count ?? 0);
                case NoteColumn: return Order(rows, r => r.Lib?.Note ?? 0);
                case 5: return Order(rows, r => r.Inst != null);
            }
            return fav;
        }

        IEnumerable<PackRow> Order<T>(IEnumerable<PackRow> rows, Func<PackRow, T> key) =>
            sortDesc ? rows.OrderByDescending(key) : rows.OrderBy(key);

        void SortBy(int col)
        {
            if (col == sortColumn) sortDesc = !sortDesc;
            else
            {
                sortColumn = col;
                sortDesc = col > 1; // taille, mods, note, état : les plus grands d'abord
            }
            RefreshPackList();
            packList.Invalidate();
        }

        // ------------------------------------------------------------------ //
        //  Notes, favoris, commentaires
        // ------------------------------------------------------------------ //

        // Étoile (0 à 4) sous la souris dans la colonne NOTE, -1 sinon
        int StarAt(Point p)
        {
            var hit = packList.HitTest(p);
            if (hit.Item == null || !(hit.Item.Tag is PackRow r) || !r.IsPack || r.Lib == null) return -1;
            if (hit.Item.SubItems.Count <= NoteColumn) return -1;
            var bounds = hit.Item.SubItems[NoteColumn].Bounds;
            if (!bounds.Contains(p)) return -1;
            float k = DeviceDpi / 96f;
            int x = p.X - bounds.Left - (int)(12 * k);
            if (x < 0) return -1;
            int star = (int)(x / (StarWidth * k));
            return star < 5 ? star : -1;
        }

        void ClickStars(MouseEventArgs e)
        {
            if (busy || e.Button != MouseButtons.Left) return;
            int star = StarAt(e.Location);
            if (star < 0) return;
            var r = (PackRow)packList.HitTest(e.Location).Item.Tag;
            int note = r.Lib.Note == star + 1 ? 0 : star + 1;
            Try(() => Core.SetRating(r.Lib.Id, note));
        }

        void ToggleFavorite()
        {
            var libs = SelectedRows().Where(r => r.Lib != null).Select(r => r.Lib).ToList();
            if (libs.Count == 0) return;
            bool fav = !libs.All(l => l.Favori);
            Try(() => Core.SetFavorite(libs.Select(l => l.Id).ToList(), fav));
        }

        void EditComment()
        {
            var rows = SelectedRows();
            if (rows.Count != 1 || rows[0].Lib == null) return;
            var lib = rows[0].Lib;
            string text = AskText($"Commentaire · {lib.Nom}", "Commentaire (vide pour l'effacer) :", lib.Commentaire ?? "");
            if (text != null) Try(() => Core.SetComment(lib.Id, text));
        }

        static string Stars(int note) => new string('★', note) + new string('☆', 5 - note);

        // Lignes « Note » et « Installation » des détails
        void ShowNoteAndPreview(PackRow r)
        {
            if (r.Lib == null)
            {
                detailNote.Text = "—";
                detailPreview.Text = "—";
                detailPreview.ForeColor = Theme.Fg;
                tips.SetToolTip(detailNote, null);
                tips.SetToolTip(detailPreview, null);
                return;
            }
            string comment = (r.Lib.Commentaire ?? "").Replace("\r", " ").Replace("\n", " ");
            detailNote.Text = Stars(r.Lib.Note) + (comment.Length > 0 ? "   " + comment : "");
            tips.SetToolTip(detailNote, string.IsNullOrEmpty(r.Lib.Commentaire) ? "Clic pour ajouter un commentaire" : r.Lib.Commentaire);

            detailPreview.ForeColor = Theme.Muted;
            if (r.Inst != null)
            {
                detailPreview.Text = "installé";
                tips.SetToolTip(detailPreview, "Clic pour le détail des fichiers");
                return;
            }
            detailPreview.Text = "calcul…";
            string id = r.Lib.Id, fivem = Clean(fivemBox.Text);
            Task.Run(() => Core.PreviewInstall(id, fivem)).ContinueWith(t =>
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    var cur = SelectedRow();
                    if (cur?.Lib?.Id != id || SelectedRows().Count != 1) return;
                    if (t.Exception != null)
                    {
                        detailPreview.Text = "—";
                        return;
                    }
                    var pv = t.Result;
                    string text = $"{pv.Remplaces.Count} remplacés · {pv.Nouveaux.Count} nouveaux" + (pv.Conflits.Count > 0 ? " · conflit" : "");
                    if (pv.Suspects.Count > 0)
                    {
                        text = $"⚠ {pv.Suspects.Count} fichier(s) suspect(s) · " + text;
                        detailPreview.ForeColor = Theme.Danger;
                    }
                    else detailPreview.ForeColor = pv.Conflits.Count > 0 ? Theme.Accent : Theme.Fg;
                    detailPreview.Text = text;
                    tips.SetToolTip(detailPreview, "Clic pour le détail des fichiers");
                }));
            });
        }

        static string ListLines(IEnumerable<string> list, int max)
        {
            var l = list.ToList();
            return string.Join("\n", l.Take(max).Select(x => "  • " + x)) + (l.Count > max ? $"\n  … (+{l.Count - max})" : "");
        }

        void ShowInstallPreview()
        {
            var r = SelectedRow();
            if (r?.Lib == null || SelectedRows().Count != 1) return;
            InstallPreview pv;
            try { pv = Core.PreviewInstall(r.Lib.Id, Clean(fivemBox.Text)); }
            catch (Exception ex) { Info("Aperçu", ex.Message); return; }
            var parts = new List<string>();
            if (pv.Suspects.Count > 0)
                parts.Add($"⚠ Fichiers suspects (programmes, scripts) :\n{ListLines(pv.Suspects, 10)}");
            parts.Add(pv.Remplaces.Count == 0 ? "Aucun fichier de FiveM remplacé."
                      : $"Fichiers remplacés ({pv.Remplaces.Count}, sauvegardés avant) :\n{ListLines(pv.Remplaces, 12)}");
            parts.Add(pv.Nouveaux.Count == 0 ? "Aucun nouveau fichier."
                      : $"Nouveaux fichiers ({pv.Nouveaux.Count}) :\n{ListLines(pv.Nouveaux, 12)}");
            if (pv.Conflits.Count > 0) parts.Add(Core.DescribeConflicts(r.Lib.Nom, pv.Conflits));
            if (r.Inst != null) parts.Insert(0, "Ce pack est déjà installé.");
            Info($"Aperçu · {r.Lib.Nom}", string.Join("\n\n", parts));
        }

        // Prévient avant d'installer un pack qui contient des programmes ou des scripts
        bool ConfirmSuspects(LibraryPack lib)
        {
            if (lib == null) return true;
            List<string> sus;
            try { sus = Core.PackSuspects(lib); }
            catch { return true; }
            if (sus.Count == 0) return true;
            return MessageBox.Show(this,
                $"« {lib.Nom} » contient des fichiers qui ne devraient pas être dans un pack graphique " +
                $"(programmes ou scripts) :\n{ListLines(sus, 10)}\n\nUn pack graphique n'en a normalement pas besoin. Installer quand même ?",
                "Fichiers suspects", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        // ------------------------------------------------------------------ //
        //  Au démarrage : mise à jour du logiciel, packs abîmés par une mise à jour du jeu
        // ------------------------------------------------------------------ //

        void StartupChecks()
        {
            Task.Run(() => Core.CheckForUpdate()).ContinueWith(t =>
            {
                if (t.Exception != null || t.Result == null || IsDisposed || !IsHandleCreated) return;
                BeginInvoke((Action)(() =>
                {
                    update = t.Result;
                    updateBtn.Text = $"Mise à jour v{update.Version.ToString(3)} disponible";
                    updateBtn.Visible = true;
                    tips.SetToolTip(updateBtn, update.Notes);
                }));
            });
            BeginInvoke((Action)(() => CheckPacks(quiet: true)));
        }

        void CheckPacks(bool quiet)
        {
            if (busy) return;
            string fivem = quiet ? ValidFivem() : FivemPath();
            if (fivem == null) return;
            if (Core.IsFivemRunning())
            {
                if (!quiet) FivemClosed();
                return;
            }
            List<PackDamage> damaged;
            try { damaged = Core.CheckInstalledPacks(fivem); }
            catch (Exception ex)
            {
                if (!quiet) Info("Vérification", ex.Message);
                return;
            }
            if (damaged.Count == 0)
            {
                if (!quiet) Info("Vérification", "Tous les packs installés sont intacts.");
                return;
            }
            string list = ListLines(damaged.Select(d => $"{d.Pack.Nom} : {d.Count} fichier(s)"), 10);
            if (!Ask("Packs à réparer",
                     "Des fichiers de packs installés ont été remis d'origine (mise à jour de FiveM ou de GTA V ?) :\n" +
                     $"{list}\n\nRemettre les fichiers des packs ?"))
                return;
            RunTask((log, p) =>
            {
                for (int i = 0; i < damaged.Count; i++)
                {
                    try { Core.RepairPack(damaged[i].Pack.Id, fivem, log); }
                    catch (GfxException ex) { log("✖ " + ex.Message); }
                    p((i + 1.0) / damaged.Count);
                }
            });
        }

        // ------------------------------------------------------------------ //
        //  Lancer FiveM, outils
        // ------------------------------------------------------------------ //

        ContextMenuStrip DarkMenu() =>
            new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, BackColor = Theme.Card, ForeColor = Theme.Fg };

        // Lance Steam (si besoin, et attend qu'il soit connecté) puis FiveM
        void LaunchFivem()
        {
            if (busy) return;
            if (Core.IsFivemRunning())
            {
                Info("Lancer FiveM", "FiveM est déjà ouvert.");
                return;
            }
            string fivem = FivemPath();
            if (fivem == null) return;
            RunTask((log, p) => Core.LaunchFivem(fivem, log));
        }

        // ------------------------------------------------- serveurs « pure » --

        void UpdateSoundsBtn()
        {
            if (soundsBtn == null) return;
            var cut = Core.CutSoundPacks();
            soundsBtn.Text = cut.Count > 0 ? "Remettre les sons" : "Couper les sons";
            soundsBtn.Glyph = cut.Count > 0 ? Glyph.Mute : Glyph.Sound;
            soundsBtn.Selected = cut.Count > 0;  // surligné tant que les sons sont coupés
            soundsBtn.Invalidate();
        }

        void ToggleSounds()
        {
            if (busy) return;
            var cut = Core.CutSoundPacks();
            if (cut.Count == 0 && Core.InstalledSoundPacks().Count == 0)
            {
                Info("Couper les sons", "Aucun pack son n'est installé : tu peux déjà rejoindre les serveurs « pure ».");
                return;
            }
            string fivem = FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (cut.Count > 0)
            {
                var names = cut.Select(id => Core.GetLibraryPack(id)?.Nom).Where(n => n != null);
                if (!Ask("Remettre les sons", "Réinstaller les packs son ?\n\n" + string.Join("\n", names.Select(n => "•  " + n)) +
                                              "\n\nLes serveurs « pure » te refuseront de nouveau.")) return;
                RunTask((log, p) => Core.RestoreSounds(fivem, log, p));
                return;
            }
            var packs = Core.InstalledSoundPacks();
            if (!Ask("Couper les sons", "Retirer les packs son pour rejoindre un serveur « pure » ?\n\n" +
                                        string.Join("\n", packs.Select(x => "•  " + x.Nom)) +
                                        "\n\nLes sons d'origine de GTA V sont remis. Clique sur « Remettre les sons » " +
                                        "pour les réinstaller ensuite.")) return;
            RunTask((log, p) => Core.CutSounds(fivem, log, p));
        }

        // Page Profils : applique le profil sélectionné puis lance FiveM
        void ApplyProfileAndLaunch()
        {
            string name = SelectedProfile();
            string fivem = name == null ? null : FivemPath();
            if (fivem == null || !FivemClosed()) return;
            if (!Core.HasOriginalBackup() &&
                !Ask("Sauvegarde d'origine", "Aucune sauvegarde d'origine n'existe. Appliquer le profil quand même ?"))
                return;
            RunTask((log, p) =>
            {
                Core.ApplyProfile(name, fivem, log, p);
                Core.LaunchFivem(fivem, log);
            });
        }

        void ShowToolsMenu(FlatButton b)
        {
            var menu = DarkMenu();
            menu.Items.Add(MenuItem("Vider le cache FiveM", ClearCache));
            menu.Items.Add(MenuItem("Ouvrir FiveM.app", OpenFivem));
            menu.Items.Add(MenuItem("Dossier des sauvegardes", OpenBackups));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Vérifier les packs installés", () => CheckPacks(false)));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("Exporter la bibliothèque…", ExportLibrary));
            menu.Items.Add(MenuItem("Importer une sauvegarde…", ImportLibrary));
            menu.Closed += (s, e) => BeginInvoke((Action)menu.Dispose);
            menu.Show(b, 0, b.Height);
        }

        void ExportLibrary()
        {
            if (busy) return;
            string dest = PickFolder("Où enregistrer la sauvegarde (bibliothèque, profils, réglages) ?");
            if (dest == null) return;
            RunTask((log, p) =>
            {
                string folder = Core.ExportData(dest, log, p);
                log("Sauvegarde : " + folder);
            });
        }

        void ImportLibrary()
        {
            if (busy) return;
            string folder = PickFolder($"Choisis le dossier « {Core.ExportFolderName} »");
            if (folder == null) return;
            var info = Core.ReadExportInfo(folder);
            if (info == null)
            {
                Info("Importer", "Ce dossier n'est pas une sauvegarde de FiveM GFX Manager.");
                return;
            }
            if (!Ask("Importer une sauvegarde",
                     $"Sauvegarde du {info.Date} (v{info.Version}) :\n  • {info.Packs} pack(s)\n  • {info.Profils} profil(s)\n\n" +
                     "Les packs et profils absents seront ajoutés. Continuer ?"))
                return;
            RunTask((log, p) => Core.ImportData(folder, log, p));
        }

        // ------------------------------------------------------------------ //
        //  Annuler la dernière action
        // ------------------------------------------------------------------ //

        void UndoLast()
        {
            if (busy) return;
            var e = Core.LastUndoable();
            if (e == null)
            {
                Info("Annuler", "Rien à annuler.");
                return;
            }
            string why = Core.WhyNotUndoable(e);
            if (why != null)
            {
                Info("Annuler", $"« {e.Action} » ne peut pas être annulée :\n{why}");
                return;
            }
            string fivem = null;
            if (e.Type != "fichier")
            {
                fivem = FivemPath();
                if (fivem == null) return;
            }
            if (!FivemClosed()) return;
            if (!Ask("Annuler", $"Annuler la dernière action ?\n\n{e.Action}\n({e.Date})")) return;
            RunTask((log, p) => Core.UndoHistory(e.Id, fivem, log, p));
        }

        // ------------------------------------------------------------------ //
        //  Mise à jour du logiciel
        // ------------------------------------------------------------------ //

        void InstallUpdate()
        {
            if (busy || update == null) return;
            string notes = string.IsNullOrWhiteSpace(update.Notes) ? "" : "\n\n" + update.Notes.Trim();
            if (notes.Length > 800) notes = notes.Substring(0, 800) + "…";
            if (!Ask("Mise à jour", $"Installer la version {update.Version.ToString(3)} ?\n" +
                                    $"Le logiciel va se fermer puis se rouvrir.{notes}"))
                return;
            var info = update;
            string exe = Application.ExecutablePath;
            int pid = Process.GetCurrentProcess().Id;
            RunTask((log, p) =>
            {
                string script = Core.PrepareUpdate(info, exe, pid, log, p);
                BeginInvoke((Action)(() =>
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"")
                    {
                        CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, UseShellExecute = false,
                        WorkingDirectory = Path.GetDirectoryName(script),
                    });
                    Application.Exit();
                }));
            });
        }

        // ------------------------------------------------------------------ //
        //  Raccourcis clavier
        // ------------------------------------------------------------------ //

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z) && !(ActiveControl is TextBoxBase) && !(searchInput.Box.Focused))
            {
                UndoLast();
                return true;
            }
            if (keyData == (Keys.Control | Keys.F) && current != null && current.Body.Contains(packList))
            {
                searchInput.Box.Focus();
                searchInput.Box.SelectAll();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
