// Décompression des archives et préparation des packs (normalisation en citizen / mods / plugins)

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
        //  Décompression (zip / 7z / rar intégrés, aucun logiciel externe)
        // ------------------------------------------------------------------ //

        public static void ExtractArchive(string archivePath, string dest, Action<string> log, Action<double> progress)
        {
            Directory.CreateDirectory(dest);
            log("Décompression de " + Path.GetFileName(archivePath) + "…");
            try
            {
                using (var archive = ArchiveFactory.OpenArchive(archivePath))
                {
                    int total = Math.Max(1, archive.Entries.Count(e => !e.IsDirectory));
                    int done = 0;
                    var opts = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
                    if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
                    {
                        // archives "solides" : extraction séquentielle (beaucoup plus rapide)
                        using (var reader = archive.ExtractAllEntries())
                            while (reader.MoveToNextEntry())
                            {
                                if (reader.Entry.IsDirectory) continue;
                                reader.WriteEntryToDirectory(dest, opts);
                                progress(++done / (double)total);
                            }
                    }
                    else
                    {
                        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                        {
                            entry.WriteToDirectory(dest, opts);
                            progress(++done / (double)total);
                        }
                    }
                }
            }
            catch (GfxException) { throw; }
            catch (Exception e) when (e is InvalidOperationException || e is SharpCompressException)
            {
                throw new GfxException("Impossible de décompresser l'archive (format non pris en charge, " +
                                       "archive protégée par mot de passe ou endommagée).\n\n" + e.Message +
                                       "\n\nAstuce : décompresse-la toi-même puis choisis « Dossier… ».");
            }
        }

        // Cherche le dossier qui contient citizen / mods / plugins (même dans des sous-dossiers).
        public static (string root, Dictionary<string, string> found) FindPackRoot(string folder)
        {
            var r = TryFindPackRoot(folder);
            if (r.root == null)
                throw new GfxException("Impossible de trouver un dossier « citizen », « mods » ou « plugins » dans le pack.\n" +
                                       "Ce pack n'a pas la structure attendue pour FiveM.app.");
            return r;
        }

        static (string root, Dictionary<string, string> found) TryFindPackRoot(string folder)
        {
            string bestRoot = null;
            Dictionary<string, string> bestFound = null;
            int bestDepth = int.MaxValue;
            var stack = new Stack<(string dir, int depth)>();
            stack.Push((folder, 0));
            while (stack.Count > 0)
            {
                var (dir, depth) = stack.Pop();
                var found = new Dictionary<string, string>();
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub).ToLowerInvariant();
                    if (FivemDirs.Contains(name))
                        found[name] = sub;
                    else
                        stack.Push((sub, depth + 1)); // on ne descend pas dans les dossiers FiveM eux-mêmes
                }
                if (found.Count > 0 && depth < bestDepth)
                {
                    bestDepth = depth;
                    bestRoot = dir;
                    bestFound = found;
                }
            }
            return (bestRoot, bestFound);
        }


        // ------------------------------------------------------------------ //
        //  Préparation d'un pack (graphique, son, armes)
        // ------------------------------------------------------------------ //

        // Crée un dossier temporaire pour f et le supprime ensuite.
        static T WithTempDir<T>(Func<string, T> f)
        {
            string work = NewTempDir("fivem_gfx_");
            try { return f(work); }
            finally { try { RemoveTree(work); } catch { } }
        }

        // Décompresse et range source dans work\range, puis renvoie ce dossier.
        static string StageSource(string source, string work, Action<string> log, Action<double> progress)
        {
            string staged = Path.Combine(work, "range");
            NormalizePack(OpenSource(source, work, log, progress), staged, log);
            return staged;
        }

        // Décompresse source si besoin et renvoie le dossier à analyser (work est supprimé par l'appelant).
        static string OpenSource(string source, string work, Action<string> log, Action<double> progress)
        {
            if (Directory.Exists(source)) return source;
            if (!File.Exists(source)) throw new GfxException("Fichier introuvable : " + source);
            string dir = Path.Combine(work, "source");
            if (source.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                // un mod seul (son, arme…) : il ira dans mods\
                CopyFile(source, Path.Combine(dir, Path.GetFileName(source)));
                progress(1);
            }
            else
            {
                ExtractArchive(source, dir, log, progress);
            }
            return dir;
        }

        // Range le contenu d'un pack dans dest\citizen|mods|plugins :
        //  - pack avec citizen / mods / plugins : copié tel quel ;
        //  - pack « OpenIV » avec x64\… ou update\… (sons, armes) : mis dans mods\ en gardant le chemin ;
        //  - fichiers .rpf seuls : mis dans mods\.
        static List<string> NormalizePack(string folder, string dest, Action<string> log)
        {
            var files = new List<(string rel, string src)>();
            var (root, found) = TryFindPackRoot(folder);
            if (root != null)
            {
                log("Dossiers trouvés dans le pack : " + string.Join(", ", found.Keys.OrderBy(k => k)));
                var ignored = Directory.GetFileSystemEntries(root).Select(Path.GetFileName)
                                       .Where(n => !FivemDirs.Contains(n.ToLowerInvariant())).ToList();
                if (ignored.Count > 0)
                    log("Ignorés (pas des dossiers FiveM.app) : " + string.Join(", ", ignored));
                foreach (var kv in found)
                    files.AddRange(ListFiles(kv.Value).Select(rel => (kv.Key + "/" + rel, P(kv.Value, rel))));
            }
            else
            {
                string gameRoot = FindGameRoot(folder);
                if (gameRoot != null)
                {
                    log("Pack au format GTA V (x64 / update) : placé dans mods\\ en gardant les chemins.");
                    foreach (var sub in Directory.GetDirectories(gameRoot))
                    {
                        string name = Path.GetFileName(sub).ToLowerInvariant();
                        if (name != "x64" && name != "update") continue;
                        files.AddRange(ListFiles(sub).Where(r => r.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                                                     .Select(rel => ("mods/" + name + "/" + rel, P(sub, rel))));
                    }
                    // .rpf de la racine du jeu (x64e.rpf…) livrés à côté de x64\ / update\
                    foreach (var f in Directory.GetFiles(gameRoot, "*.rpf").Where(f => IsTopLevelRpf(Path.GetFileName(f))))
                        files.Add(("mods/" + Path.GetFileName(f), f));
                }
                else
                {
                    var rpfs = Directory.EnumerateFiles(folder, "*.rpf", SearchOption.AllDirectories).ToList();
                    if (rpfs.Count > 0)
                    {
                        // FiveM ne charge un .rpf de mods\ que s'il a le même chemin que dans GTA V
                        var unknown = rpfs.Select(Path.GetFileName).Where(n => LooseRpfPath(n) == null).Distinct().ToList();
                        if (unknown.Count > 0)
                            throw new GfxException("Impossible de savoir où vont ces .rpf dans GTA V : " + string.Join(", ", unknown) +
                                                   "\nFiveM ne les charge que s'ils sont au même chemin que dans le jeu " +
                                                   "(ex : mods\\x64\\audio\\sfx\\RESIDENT.rpf).\n\n" +
                                                   "Prends la version du mod qui contient les dossiers (x64\\…, update\\…), " +
                                                   "ou range-les toi-même dans un dossier « mods » avec le bon chemin, " +
                                                   "puis utilise « Ajouter un dossier… ».");
                        foreach (var f in rpfs)
                            files.Add((LooseRpfPath(Path.GetFileName(f)), f));
                        log($"{rpfs.Count} fichier(s) .rpf placé(s) à leur chemin GTA V dans mods\\ : " +
                            string.Join(", ", files.Select(x => x.rel)));
                        var dup = files.GroupBy(f => f.rel.ToLowerInvariant()).FirstOrDefault(g => g.Count() > 1);
                        if (dup != null)
                            throw new GfxException($"Deux fichiers s'appellent « {Path.GetFileName(dup.Key)} » dans le pack.\n" +
                                                   "Décompresse-le toi-même et range-le dans un dossier « mods ».");
                    }
                    else if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                                      .Any(f => RawGameExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
                    {
                        throw new GfxException("Ce pack contient des fichiers GTA V « bruts » (.ytd, .yft, .awc…) " +
                                               "qui ne sont pas dans un .rpf.\nFiveM ne peut pas les charger tels quels : " +
                                               "il faut d'abord les mettre dans un .rpf avec OpenIV,\n" +
                                               "ou utiliser une version du mod faite pour FiveM.");
                    }
                    else
                    {
                        throw new GfxException("Impossible de trouver un dossier « citizen », « mods » ou « plugins », " +
                                               "ni de fichier .rpf dans le pack.\nCe pack n'a pas la structure attendue pour FiveM.app.");
                    }
                }
            }
            if (files.Count == 0) throw new GfxException("Le pack ne contient aucun fichier.");
            foreach (var (rel, src) in files)
                CopyFile(src, P(dest, rel));
            return files.Select(f => f.rel).ToList();
        }

        // .rpf de la racine de GTA V (common.rpf, x64a.rpf … x64w.rpf)
        static bool IsTopLevelRpf(string name) => Regex.IsMatch(name, @"^(common|x64[a-w])\.rpf$", RegexOptions.IgnoreCase);

        // Chemin dans FiveM.app d'un .rpf livré seul (null : on ne sait pas où il va)
        static string LooseRpfPath(string name)
        {
            if (IsTopLevelRpf(name)) return "mods/" + name;
            if (name.Equals("update.rpf", StringComparison.OrdinalIgnoreCase)) return "mods/update/" + name;
            if (SfxRpfs.Contains(name.ToLowerInvariant())) return "mods/x64/audio/sfx/" + name;
            return null;
        }

        // Dossier le moins profond qui contient x64\ ou update\ avec des .rpf dedans
        static string FindGameRoot(string folder)
        {
            var queue = new Queue<string>();
            queue.Enqueue(folder);
            while (queue.Count > 0)
            {
                string dir = queue.Dequeue();
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    string name = Path.GetFileName(sub).ToLowerInvariant();
                    if ((name == "x64" || name == "update") &&
                        Directory.EnumerateFiles(sub, "*.rpf", SearchOption.AllDirectories).Any())
                        return dir;
                }
                foreach (var sub in Directory.GetDirectories(dir)) queue.Enqueue(sub);
            }
            return null;
        }

        static string SourceName(string source) => Directory.Exists(source)
            ? Path.GetFileName(source.TrimEnd('\\', '/'))
            : Path.GetFileNameWithoutExtension(source);

    }
}
