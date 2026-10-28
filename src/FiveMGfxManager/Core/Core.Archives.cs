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
                        // FiveM ne charge un .rpf de mods\ que s'il a le même chemin que dans GTA V :
                        // on cherche ce chemin dans le dossier du jeu
                        var gta = GtaRpfIndex(log);
                        var placed = rpfs.Select(f => (f, rel: LooseRpfPath(f, folder, gta))).ToList();
                        // un nom qui n'existe pas dans GTA V est un pack de mods pour FiveM (ex : aQuantV.rpf) :
                        // FiveM charge tout .rpf posé à la racine de mods\
                        var addons = gta.Count == 0 ? new List<string>() : placed
                            .Where(x => x.rel == null && !gta.ContainsKey(Path.GetFileName(x.f))).Select(x => x.f).ToList();
                        placed = placed.Select(x => addons.Contains(x.f) ? (f: x.f, rel: "mods/" + Path.GetFileName(x.f)) : x).ToList();
                        var unknown = placed.Where(x => x.rel == null).Select(x => Path.GetFileName(x.f))
                                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                        if (unknown.Count > 0)
                        {
                            var details = unknown.Select(n => gta.TryGetValue(n, out var where)
                                ? $"{n} (existe à plusieurs endroits dans GTA V : {string.Join(", ", where.Take(4))}" +
                                  (where.Count > 4 ? ", …" : "") + ")"
                                : n);
                            throw new GfxException("Impossible de savoir où vont ces .rpf dans GTA V :\n  " +
                                                   string.Join("\n  ", details) +
                                                   "\n\nFiveM ne les charge que s'ils sont au même chemin que dans le jeu " +
                                                   "(ex : mods\\x64\\audio\\sfx\\RESIDENT.rpf).\n" +
                                                   (gta.Count == 0 ? "GTA V n'a pas été trouvé sur ce PC pour chercher le chemin.\n" : "") +
                                                   "\nPrends la version du mod qui contient les dossiers (x64\\…, update\\…), " +
                                                   "ou range-les toi-même dans un dossier « mods » avec le bon chemin, " +
                                                   "puis utilise « Ajouter un dossier… ».");
                        }
                        files.AddRange(placed.Select(x => (x.rel, x.f)));
                        var atGtaPath = placed.Where(x => !addons.Contains(x.f)).Select(x => x.rel).ToList();
                        if (atGtaPath.Count > 0)
                            log($"{atGtaPath.Count} fichier(s) .rpf placé(s) à leur chemin GTA V dans mods\\ : " +
                                string.Join(", ", atGtaPath));
                        if (addons.Count > 0)
                            log($"{addons.Count} pack(s) .rpf pour FiveM (nom absent de GTA V) posé(s) dans mods\\ : " +
                                string.Join(", ", addons.Select(Path.GetFileName)));
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

        // Tous les .rpf de GTA V, par nom de fichier (chemins relatifs au dossier du jeu)
        static Dictionary<string, List<string>> GtaRpfIndex(Action<string> log)
        {
            var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string gta = GtaPath();
            if (gta == null)
            {
                log("GTA V introuvable : seuls les .rpf les plus courants peuvent être rangés automatiquement.");
                return index;
            }
            try
            {
                foreach (var f in Directory.EnumerateFiles(gta, "*.rpf", SearchOption.AllDirectories))
                {
                    string rel = RelPath(gta, f);
                    if (rel.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)) continue; // dossier mods d'OpenIV
                    string name = Path.GetFileName(rel);
                    if (!index.TryGetValue(name, out var list)) index[name] = list = new List<string>();
                    list.Add(rel);
                }
                log($"Chemins des .rpf cherchés dans GTA V ({gta}).");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log("  ! lecture du dossier de GTA V impossible : " + e.Message);
            }
            return index;
        }

        // Chemin dans FiveM.app d'un .rpf livré seul (null : on ne sait pas où il va).
        // Le nom est cherché dans GTA V ; s'il y est plusieurs fois (dlc.rpf…), on garde l'endroit dont
        // les dossiers ressemblent le plus à ceux du pack (ex : mpheist\dlc.rpf).
        static string LooseRpfPath(string file, string packFolder, Dictionary<string, List<string>> gta)
        {
            string name = Path.GetFileName(file);
            if (gta.TryGetValue(name, out var found))
            {
                string[] Parts(string rel) => rel.ToLowerInvariant().Split('/').Reverse().ToArray();
                var mine = Parts(RelPath(packFolder, file));
                int Score(string rel)
                {
                    var g = Parts(rel);
                    int n = 0;
                    while (n < g.Length && n < mine.Length && g[n] == mine[n]) n++;
                    return n;
                }
                var best = found.GroupBy(Score).OrderByDescending(g => g.Key).First().ToList();
                return best.Count == 1 ? "mods/" + best[0] : null;
            }
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
