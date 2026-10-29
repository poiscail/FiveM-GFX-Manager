// Bibliothèque de packs

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace FiveMGfxManager
{
    public static partial class Core
    {
        // ------------------------------------------------------------------ //
        //  Bibliothèque de packs
        // ------------------------------------------------------------------ //

        static string LibraryContent(string id) => Path.Combine(LibraryDir(), id, "contenu");

        public static List<LibraryPack> ListLibrary()
        {
            if (!Directory.Exists(LibraryDir())) return new List<LibraryPack>();
            return Directory.GetDirectories(LibraryDir())
                            .Select(d => ReadJson<LibraryPack>(Path.Combine(d, "pack.json")))
                            .Where(p => p != null)
                            .OrderBy(p => p.Id, StringComparer.Ordinal).ToList(); // l'id commence par yyyyMMdd_HHmmss
        }

        // Empreinte du contenu d'un pack (chemins + tailles), pour repérer les doublons
        static string ContentSignature(string content) => string.Join("\n",
            ListFiles(content).Select(r => r.ToLowerInvariant() + "|" + new FileInfo(P(content, r)).Length)
                              .OrderBy(x => x, StringComparer.Ordinal));

        public static LibraryPack GetLibraryPack(string id) =>
            ReadJson<LibraryPack>(Path.Combine(LibraryDir(), id, "pack.json"))
            ?? throw new GfxException("Ce pack n'existe plus dans la bibliothèque.");

        static void SaveLibraryPack(LibraryPack p) => WriteJson(Path.Combine(LibraryDir(), p.Id, "pack.json"), p);

        public static bool IsInstalled(string libId) => InstalledPacks().Any(p => p.LibId == libId);

        // Copie un pack (archive, dossier ou .rpf) dans la bibliothèque, rangé et prêt à installer.
        public static LibraryPack ImportPack(string source, string type, Action<string> log, Action<double> progress,
                                             string folder = null)
        {
            Directory.CreateDirectory(LibraryDir());
            string dir = NewIdDir(LibraryDir(), out string id);
            string work = NewTempDir("fivem_gfx_");
            try
            {
                string opened = OpenSource(source, work, log, p => progress(p * 0.7));
                var files = NormalizePack(opened, Path.Combine(dir, "contenu"), log, type == ModType, SourceName(source));
                string image = FindPreviewImage(opened);
                if (image != null) CopyFile(image, Path.Combine(dir, "apercu" + Path.GetExtension(image).ToLowerInvariant()));
                string sig = ContentSignature(Path.Combine(dir, "contenu"));
                var same = ListLibrary().FirstOrDefault(o => o.Id != id && ContentSignature(LibraryContent(o.Id)) == sig);
                if (same != null)
                    throw new GfxException($"Ce pack est déjà dans la bibliothèque : « {same.Nom} » (même contenu).");
                var pack = new LibraryPack
                {
                    Id = id, Nom = SourceName(source), Type = type ?? "Graphique", Source = source, Date = Now(), Dossier = folder,
                    Taille = FolderSize(Path.Combine(dir, "contenu")),
                    Armes = StreamedFiles(Path.Combine(dir, "contenu"), files),
                    Suspects = SuspiciousFiles(Path.Combine(dir, "contenu")),
                };
                SaveLibraryPack(pack);
                progress(1);
                log($"Pack « {pack.Nom} » ({pack.Type}) ajouté à la bibliothèque : {files.Count} fichiers, {HumanSize(pack.Taille)}.");
                if (pack.Armes.Count > 0) log("Armes remplacées : " + string.Join(", ", WeaponNames(pack.Armes)));
                if (pack.Suspects.Count > 0)
                    log($"! Attention : {pack.Suspects.Count} fichier(s) suspect(s) dans le pack (programmes, scripts) : " +
                        string.Join(", ", pack.Suspects.Take(5).Select(Path.GetFileName)) + (pack.Suspects.Count > 5 ? "…" : ""));
                return pack;
            }
            catch
            {
                try { RemoveTree(dir); } catch { }
                throw;
            }
            finally
            {
                try { RemoveTree(work); } catch { }
            }
        }

        // Ajoute des mods (archive, dossier ou .rpf) dans un pack de la bibliothèque.
        public static void AddModsToPack(string id, string source, Action<string> log, Action<double> progress)
        {
            var pack = GetLibraryPack(id);
            if (IsInstalled(id))
                throw new GfxException($"Le pack « {pack.Nom} » est installé. Retire-le avant de lui ajouter des mods.");
            WithTempDir(work =>
            {
                string staged = StageSource(source, work, log, p => progress(p * 0.6), pack.Type == ModType);
                var files = ListFiles(staged);
                string content = LibraryContent(id);
                int replaced = files.Count(rel => File.Exists(P(content, rel)));
                foreach (var rel in files)
                    CopyFile(P(staged, rel), P(content, rel));
                string name = SourceName(source);
                pack.Mods = pack.Mods ?? new List<string>();
                if (!pack.Mods.Contains(name, StringComparer.OrdinalIgnoreCase)) pack.Mods.Add(name);
                pack.Taille = FolderSize(content);
                pack.Armes = StreamedFiles(content, ListFiles(content));
                pack.Suspects = SuspiciousFiles(content);
                SaveLibraryPack(pack);
                progress(1);
                log($"Mods « {name} » ajoutés à « {pack.Nom} » : {files.Count} fichiers" +
                    (replaced > 0 ? $" (dont {replaced} remplacés dans le pack)." : "."));
                return 0;
            });
        }

        public static void SetPackType(string id, string type)
        {
            var pack = GetLibraryPack(id);
            pack.Type = type;
            SaveLibraryPack(pack);
            var cfg = LoadConfig();
            foreach (var p in cfg.PacksInstalles.Where(p => p.LibId == id)) p.Type = type;
            SaveConfig(cfg);
        }

        public static void RenamePack(string id, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new GfxException("Le nom ne peut pas être vide.");
            var pack = GetLibraryPack(id);
            pack.Nom = name;
            SaveLibraryPack(pack);
            var cfg = LoadConfig();
            foreach (var p in cfg.PacksInstalles.Where(p => p.LibId == id)) p.Nom = name;
            SaveConfig(cfg);
        }

        // ------------------------------------------------------------------ //
        //  Dossiers de rangement de la bibliothèque (dans le logiciel seulement)
        //  Un sous-dossier s'écrit « Parent/Enfant ».
        // ------------------------------------------------------------------ //

        static string FoldersPath() => Path.Combine(LibraryDir(), "dossiers.json");

        public static string ParentFolder(string folder)
        {
            int i = folder?.LastIndexOf('/') ?? -1;
            return i < 0 ? null : folder.Substring(0, i);
        }

        public static string FolderLeaf(string folder) => folder?.Substring(folder.LastIndexOf('/') + 1);

        // Le dossier est « folder » ou un de ses sous-dossiers
        public static bool IsInFolder(string dossier, string folder) =>
            !string.IsNullOrEmpty(dossier) && (dossier.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
                                               dossier.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        // Dossiers créés (même vides), dossiers des packs et tous leurs parents
        public static List<string> LibraryFolders()
        {
            var saved = ReadJson<List<string>>(FoldersPath()) ?? new List<string>();
            var all = new List<string>();
            foreach (var d in saved.Concat(ListLibrary().Select(p => p.Dossier)).Where(d => !string.IsNullOrEmpty(d)))
                for (string f = d; f != null; f = ParentFolder(f))
                    all.Add(f);
            return all.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // Nom complet propre : « A / B » → « A/B »
        static string FolderName(string name)
        {
            var parts = (name ?? "").Replace('\\', '/').Split('/').Select(s => s.Trim()).ToList();
            if (parts.Any(s => s.Length == 0)) throw new GfxException("Le nom du dossier ne peut pas être vide.");
            return string.Join("/", parts);
        }

        public static void CreateFolder(string name)
        {
            name = FolderName(name);
            var all = LibraryFolders();
            if (all.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new GfxException($"Le dossier « {FolderLeaf(name)} » existe déjà.");
            all.Add(name);
            Directory.CreateDirectory(LibraryDir());
            WriteJson(FoldersPath(), all);
        }

        // Renomme (ou déplace) un dossier, ses sous-dossiers suivent
        public static void RenameFolder(string old, string name)
        {
            name = FolderName(name);
            if (IsInFolder(name, old) && !name.Equals(old, StringComparison.OrdinalIgnoreCase))
                throw new GfxException("Un dossier ne peut pas aller dans lui-même.");
            var all = LibraryFolders();
            if (!old.Equals(name, StringComparison.OrdinalIgnoreCase) && all.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new GfxException($"Le dossier « {FolderLeaf(name)} » existe déjà.");
            string Move(string d) => IsInFolder(d, old) ? name + d.Substring(old.Length) : d;
            foreach (var p in ListLibrary().Where(p => IsInFolder(p.Dossier, old)))
            {
                p.Dossier = Move(p.Dossier);
                SaveLibraryPack(p);
            }
            WriteJson(FoldersPath(), all.Select(Move).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        // Supprime le dossier et ses sous-dossiers : leurs packs restent dans la bibliothèque, dans le dossier parent
        public static void DeleteFolder(string name)
        {
            string parent = ParentFolder(name);
            foreach (var p in ListLibrary().Where(p => IsInFolder(p.Dossier, name)))
            {
                p.Dossier = parent;
                SaveLibraryPack(p);
            }
            WriteJson(FoldersPath(), LibraryFolders().Where(d => !IsInFolder(d, name)).ToList());
        }

        // Range des packs dans un dossier (null : aucun dossier)
        public static void MoveToFolder(IEnumerable<string> ids, string folder)
        {
            folder = string.IsNullOrWhiteSpace(folder) ? null : FolderName(folder);
            if (folder != null)
            {
                var all = LibraryFolders();
                var existing = all.FirstOrDefault(d => d.Equals(folder, StringComparison.OrdinalIgnoreCase));
                if (existing != null) folder = existing;
                else CreateFolder(folder);
            }
            foreach (var id in ids)
            {
                var p = GetLibraryPack(id);
                p.Dossier = folder;
                SaveLibraryPack(p);
            }
        }

        // ------------------------------------------------------------------ //
        //  Favoris, notes et commentaires
        // ------------------------------------------------------------------ //

        public static void SetFavorite(IEnumerable<string> ids, bool on)
        {
            foreach (var id in ids)
            {
                var p = GetLibraryPack(id);
                p.Favori = on;
                SaveLibraryPack(p);
            }
        }

        public static void SetRating(string id, int note)
        {
            var p = GetLibraryPack(id);
            p.Note = Math.Max(0, Math.Min(5, note));
            SaveLibraryPack(p);
        }

        public static void SetComment(string id, string text)
        {
            var p = GetLibraryPack(id);
            p.Commentaire = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            SaveLibraryPack(p);
        }

        public static void DeleteFromLibrary(string id, Action<string> log)
        {
            var pack = GetLibraryPack(id);
            if (IsInstalled(id))
                throw new GfxException($"Le pack « {pack.Nom} » est installé. Retire-le avant de le supprimer.");
            RemoveTree(Path.Combine(LibraryDir(), id));
            log($"Pack « {pack.Nom} » supprimé de la bibliothèque.");
        }

    }
}
