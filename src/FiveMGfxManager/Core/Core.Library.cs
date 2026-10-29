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
        public static LibraryPack ImportPack(string source, string type, Action<string> log, Action<double> progress)
        {
            Directory.CreateDirectory(LibraryDir());
            string dir = NewIdDir(LibraryDir(), out string id);
            string work = NewTempDir("fivem_gfx_");
            try
            {
                string folder = OpenSource(source, work, log, p => progress(p * 0.7));
                var files = NormalizePack(folder, Path.Combine(dir, "contenu"), log, type == ModType);
                string sig = ContentSignature(Path.Combine(dir, "contenu"));
                var same = ListLibrary().FirstOrDefault(o => o.Id != id && ContentSignature(LibraryContent(o.Id)) == sig);
                if (same != null)
                    throw new GfxException($"Ce pack est déjà dans la bibliothèque : « {same.Nom} » (même contenu).");
                var pack = new LibraryPack
                {
                    Id = id, Nom = SourceName(source), Type = type ?? "Graphique", Source = source, Date = Now(),
                    Taille = FolderSize(Path.Combine(dir, "contenu")),
                };
                SaveLibraryPack(pack);
                progress(1);
                log($"Pack « {pack.Nom} » ({pack.Type}) ajouté à la bibliothèque : {files.Count} fichiers, {HumanSize(pack.Taille)}.");
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
