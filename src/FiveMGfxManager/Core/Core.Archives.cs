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
        // Choix de la variante par l'utilisateur : (question, variantes) → variante choisie, null = annuler.
        // Non défini (tests) : la variante proposée en premier est gardée.
        public static Func<string, List<string>, string> ChooseVariant;

        static string StageSource(string source, string work, Action<string> log, Action<double> progress, bool mod = false)
        {
            string staged = Path.Combine(work, "range");
            NormalizePack(OpenSource(source, work, log, progress), staged, log, mod, SourceName(source));
            return staged;
        }

        // Décompresse source si besoin et renvoie le dossier à analyser (work est supprimé par l'appelant).
        static string OpenSource(string source, string work, Action<string> log, Action<double> progress)
        {
            string dir = Path.Combine(work, "source");
            if (Directory.Exists(source))
            {
                if (TryFindPackRoot(source).root != null || !NestedArchives(source).Any()) return source;
                // archives dans le dossier : décompressées dans une copie (le dossier de l'utilisateur ne change pas)
                foreach (var rel in ListFiles(source)) CopyFile(P(source, rel), P(dir, rel));
            }
            else if (!File.Exists(source)) throw new GfxException("Fichier introuvable : " + source);
            else if (source.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                // un mod seul (son, arme…) : il ira dans mods\
                CopyFile(source, Path.Combine(dir, Path.GetFileName(source)));
                progress(1);
            }
            else
            {
                ExtractArchive(source, dir, log, progress);
            }
            if (TryFindPackRoot(dir).root == null) ExpandNestedArchives(dir, log);
            return dir;
        }

        static readonly string[] ArchiveExts = { ".zip", ".7z", ".rar" };

        static IEnumerable<string> NestedArchives(string dir) =>
            Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .Where(f => ArchiveExts.Contains(Path.GetExtension(f).ToLowerInvariant()));

        // Pack d'armes livré en plusieurs archives (une par arme) : chacune est décompressée à sa place
        static void ExpandNestedArchives(string dir, Action<string> log)
        {
            for (int level = 0; level < 3; level++)
            {
                var archives = NestedArchives(dir).ToList();
                if (archives.Count == 0) return;
                foreach (var a in archives)
                {
                    string target = Path.Combine(Path.GetDirectoryName(a), Path.GetFileNameWithoutExtension(a));
                    while (Directory.Exists(target) || File.Exists(target)) target += "_";
                    try
                    {
                        ExtractArchive(a, target, _ => { }, _ => { });
                        log("Archive dans le pack décompressée : " + RelPath(dir, a));
                    }
                    catch (Exception e)
                    {
                        log("  ! " + RelPath(dir, a) + " ignorée : " + e.Message.Split('\n')[0]);
                    }
                    MakeWritable(a);
                    File.Delete(a);
                }
            }
        }

        static bool SameContent(string a, string b)
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            using (var fa = File.OpenRead(a))
            using (var fb = File.OpenRead(b))
            {
                var ba = new byte[81920];
                var bb = new byte[81920];
                int n;
                while ((n = fa.Read(ba, 0, ba.Length)) > 0)
                {
                    int m = 0;
                    while (m < n) { int r = fb.Read(bb, m, n - m); if (r <= 0) return false; m += r; }
                    for (int i = 0; i < n; i++) if (ba[i] != bb[i]) return false;
                }
                return true;
            }
        }

        // Range le contenu d'un pack dans dest\citizen|mods|plugins :
        //  - pack avec citizen / mods / plugins : copié tel quel ;
        //  - pack « OpenIV » avec x64\… ou update\… (sons, armes) : mis dans mods\ en gardant le chemin ;
        //  - fichiers .rpf seuls : mis dans mods\ ;
        //  - fichiers streamés seuls (.ydr, .ytd… des skins d'armes) : mis dans un paquet FiveM mods\gfx_<nom>.rpf.
        // Un .rpf posé à la racine de mods\ sans assembly.xml est ignoré par FiveM : il est mis dans un paquet.
        // Un mod (mod = true) ne garde que ses .rpf : ses autres fichiers seuls ne vont pas dans citizen.
        static List<string> NormalizePack(string folder, string dest, Action<string> log, bool mod = false, string packName = null)
        {
            var files = new List<(string rel, string src)>();
            var streamed = new List<string>();   // empaquetés dans le fichier de src null
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
                // .ydr, .ytd… seuls dans mods\ (chemin OpenIV « …\weapons.rpf\ » en dossiers) : FiveM ne les lit pas là
                var loose = files.Where(f => f.rel.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) && IsStreamFile(f.rel)).ToList();
                files.RemoveAll(loose.Contains);
                streamed.AddRange(loose.Select(f => f.src));
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
                    streamed.AddRange(Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories).Where(f => IsStreamFile(f)));
                }
                else
                {
                    var citizen = mod ? new List<(string rel, string src)>() : Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                                           .Where(f => !f.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                                           .Select(f => (rel: LooseCitizenPath(f, folder), src: f))
                                           .Where(x => x.rel != null).ToList();
                    if (citizen.Count > 0)
                    {
                        files.AddRange(citizen);
                        log($"{citizen.Count} fichier(s) placé(s) dans citizen\\common\\data : " +
                            string.Join(", ", citizen.Select(x => x.rel.Substring(CitizenData.Length))));
                    }
                    // .ydr, .ytd… seuls : FiveM les streame depuis un paquet, à la place des fichiers du jeu de même nom
                    streamed = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                                        .Where(f => IsStreamFile(f) && (mod || LooseCitizenPath(f, folder) == null)).ToList();
                    var rpfs = Directory.EnumerateFiles(folder, "*.rpf", SearchOption.AllDirectories).ToList();
                    if (rpfs.Count > 0)
                    {
                        // FiveM ne charge un .rpf de mods\ que s'il a le même chemin que dans GTA V :
                        // on cherche ce chemin dans le dossier du jeu
                        var gta = GtaRpfIndex(log);
                        var placed = rpfs.Select(f => (f, rel: LooseRpfPath(f, folder, gta))).ToList();
                        // un nom qui n'existe pas dans GTA V est un pack de mods pour FiveM (ex : aQuantV.rpf) :
                        // posé à la racine de mods\ (mis dans un paquet s'il n'a pas d'assembly.xml)
                        // (sans GTA V trouvé : seulement un mod, ou un .rpf de fichiers streamés comme weapons.rpf)
                        var addons = placed
                            .Where(x => x.rel == null && !gta.ContainsKey(Path.GetFileName(x.f)) &&
                                        (gta.Count > 0 || mod || RpfRootFiles(x.f)?.Any(n => IsStreamFile(n) ||
                                            n.Equals("assembly.xml", StringComparison.OrdinalIgnoreCase)) == true))
                            .Select(x => x.f).ToList();
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
                    }
                    else if (citizen.Count == 0 && streamed.Count == 0 && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                                      .Any(f => RawGameExts.Contains(Path.GetExtension(f).ToLowerInvariant())))
                    {
                        throw new GfxException("Ce pack contient des fichiers GTA V « bruts » (.awc, .meta…) " +
                                               "qui ne sont pas dans un .rpf.\nFiveM ne peut pas les charger tels quels : " +
                                               "il faut d'abord les mettre dans un .rpf avec OpenIV,\n" +
                                               "ou utiliser une version du mod faite pour FiveM.");
                    }
                    else if (streamed.Count > 0) { }
                    else if (mod)
                    {
                        throw new GfxException("Aucun fichier .rpf ni dossier « mods » dans ce mod.\n" +
                                               "Seuls les .rpf vont dans FiveM.app\\mods : ajoute-le plutôt comme pack.");
                    }
                    else if (citizen.Count == 0)
                    {
                        throw new GfxException("Impossible de trouver un dossier « citizen », « mods » ou « plugins », " +
                                               "ni de fichier .rpf dans le pack.\nCe pack n'a pas la structure attendue pour FiveM.app.");
                    }
                }
            }
            // .ydr, .ytd… seuls : FiveM les streame depuis un paquet, à la place des fichiers du jeu de même nom.
            // Pack de plusieurs armes : tous dans le même paquet ; un même fichier présent deux fois n'est gardé qu'une fois.
            if (streamed.Count > 0)
            {
                // variantes (Glow / Regular, Variant 1 / Variant 2…) : FiveM n'en charge qu'une, on garde une variante entière
                for (;;)
                {
                    var clash = streamed.GroupBy(f => Path.GetFileName(f).ToLowerInvariant())
                                        .Select(g => g.Where((f, i) => !g.Take(i).Any(o => SameContent(o, f))).ToList())
                                        .FirstOrDefault(v => v.Count > 1);
                    if (clash == null) break;
                    var segs = clash.Select(f => RelPath(folder, f).Split('/').Reverse().Skip(1).Reverse().ToArray()).ToList();
                    int common = 0;
                    while (segs.All(s => s.Length > common && s[common].Equals(segs[0][common], StringComparison.OrdinalIgnoreCase))) common++;
                    if (segs.Any(s => s.Length == common)) break; // une version dans le dossier parent des autres : refus plus bas
                    var roots = segs.Select(s => string.Join("\\", s.Take(common + 1))).Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(r => Regex.IsMatch(r.Substring(r.LastIndexOf('\\') + 1), "regular|normal|default|standard|original|base",
                                                                RegexOptions.IgnoreCase) ? 0 : 1)
                                    .ThenBy(r => r, StringComparer.OrdinalIgnoreCase).ToList();
                    if (ChooseVariant != null)
                    {
                        string pick = ChooseVariant($"Le pack contient plusieurs versions de « {Path.GetFileName(clash[0])} ».\n" +
                                                    "FiveM ne peut en charger qu'une : quelle variante installer ?", roots)
                                      ?? throw new GfxException("Ajout annulé : aucune variante choisie.");
                        roots.Remove(pick);
                        roots.Insert(0, pick);
                    }
                    streamed = streamed.Where(f => !roots.Skip(1).Any(r => RelPath(folder, f).Replace('/', '\\')
                                                        .StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
                    log($"Variante « {roots[0]} » gardée, " +
                        string.Join(", ", roots.Skip(1).Select(r => $"« {r} »")) + " ignorée(s).\n" +
                        "  Pour une autre variante, ajoute seulement son dossier (« Ajouter un dossier… »).");
                }
                var kept = new List<string>();
                foreach (var g in streamed.GroupBy(f => Path.GetFileName(f).ToLowerInvariant()))
                {
                    var versions = g.Where((f, i) => !g.Take(i).Any(o => SameContent(o, f))).ToList();
                    if (versions.Count > 1)
                        throw new GfxException($"Plusieurs versions différentes de « {Path.GetFileName(g.First())} » dans le pack :\n  " +
                                               string.Join("\n  ", versions.Select(f => Path.GetDirectoryName(RelPath(folder, f)) is var d && d != "" ? d : "(racine)")) +
                                               "\n\nFiveM ne peut en charger qu'une : ajoute seulement le dossier de la version voulue " +
                                               "(« Ajouter un dossier… »).");
                    kept.Add(versions[0]);
                }
                streamed = kept;
                string package = "mods/" + PackageFileName(packName);
                files.Add((package, null));
                log($"{streamed.Count} fichier(s) streamé(s) mis dans le paquet FiveM {package} : " +
                    string.Join(", ", streamed.Take(8).Select(Path.GetFileName)) + (streamed.Count > 8 ? ", …" : ""));
            }
            if (files.Count == 0) throw new GfxException("Le pack ne contient aucun fichier.");

            // .rpf de mods\ sans assembly.xml avec des fichiers streamés à sa racine (weapons.rpf…) : FiveM l'ignore,
            // même au chemin OpenIV (mods\update\…\dlc.rpf\x64\models\cdimages\weapons.rpf) : mis dans un paquet à la racine de mods\
            var wrap = new HashSet<string>(files.Where(f => f.src != null && NeedsPackage(f.rel, f.src, log)).Select(f => f.src));
            files = files.Select(f => wrap.Contains(f.src) ? ("mods/" + Path.GetFileName(f.rel), f.src) : f).ToList();

            // même fichier en double (une copie par arme) : gardé une fois ; .rpf différents au même nom : renommés
            var final = new List<(string rel, string src)>();
            foreach (var f in files)
            {
                var same = final.Where(x => x.rel.Equals(f.rel, StringComparison.OrdinalIgnoreCase)).ToList();
                if (same.Count == 0) { final.Add(f); continue; }
                if (f.src != null && same.Any(x => x.src != null && SameContent(x.src, f.src))) continue;
                if (!Regex.IsMatch(f.rel, @"^mods/[^/]+\.rpf$", RegexOptions.IgnoreCase))
                    throw new GfxException($"Deux fichiers différents s'appellent « {Path.GetFileName(f.rel)} » dans le pack.\n" +
                                           "Décompresse-le toi-même et range-le dans un dossier « citizen » ou « mods ».");
                string baseName = f.rel.Substring(0, f.rel.Length - 4), rel;
                int n = 2;
                do rel = baseName + "_" + n++ + ".rpf"; while (final.Any(x => x.rel.Equals(rel, StringComparison.OrdinalIgnoreCase)));
                log($"Deux .rpf différents s'appellent {Path.GetFileName(f.rel)} : le second devient {Path.GetFileName(rel)}.");
                final.Add((rel, f.src));
            }
            files = final;

            foreach (var (rel, src) in files)
            {
                if (src == null)
                    WriteModPackage(P(dest, rel), packName ?? "Pack", streamed, new List<string>());
                else if (wrap.Contains(src))
                {
                    WriteModPackage(P(dest, rel), Path.GetFileNameWithoutExtension(rel), new List<string>(), new List<string> { src });
                    log($"{Path.GetFileName(rel)} n'a pas d'assembly.xml (FiveM l'ignorerait) : mis dans un paquet FiveM.");
                }
                else
                    CopyFile(src, P(dest, rel));
            }
            return files.Select(f => f.rel).ToList();
        }

        // .rpf de mods\ sans assembly.xml mais avec des fichiers streamés (.ydr, .ytd…) à sa racine
        static bool NeedsPackage(string rel, string src, Action<string> log)
        {
            if (!Regex.IsMatch(rel, @"^mods/.+\.rpf$", RegexOptions.IgnoreCase) || IsTopLevelRpf(Path.GetFileName(rel)))
                return false;
            var entries = ReadRpfEntries(src);
            if (entries == null)
            {
                if (IsEncryptedRpf(src) && !rel.Substring(5).Contains("/"))
                    log($"  ! {Path.GetFileName(rel)} est chiffré : impossible de vérifier qu'il contient un assembly.xml. " +
                        "S'il n'en a pas, FiveM l'ignorera (ré-enregistre-le non chiffré avec OpenIV).");
                return false;
            }
            var rootFiles = entries.Where(e => !e.Dossier && !e.Path.Contains("/")).Select(e => e.Path).ToList();
            return !rootFiles.Contains("assembly.xml", StringComparer.OrdinalIgnoreCase) && rootFiles.Any(IsStreamFile);
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


        // Fichiers seuls qui vont dans citizen\common\data (guides FiveM) :
        //  BloodFX (.ypt) → effects\, kill effects (timecycle…xml) → timecycle\, roue des armes et
        //  position de la map (.gfx, frontend.xml…) → ui\, no water (water.xml) → levels\gta5\.
        // Un dossier du pack nommé comme la destination (…\common\data\…, effects, timecycle, ui, gta5) l'emporte.
        static readonly string[] UiXml = { "frontend.xml", "pausemenu.xml", "hud.xml", "minimap.xml" };
        const string CitizenData = "citizen/common/data/";
        static readonly string[] CitizenExts = { ".ypt", ".xml", ".gfx", ".ytd", ".dat", ".meta", ".ymt" };

        static string LooseCitizenPath(string file, string packFolder)
        {
            string rel = RelPath(packFolder, file), name = Path.GetFileName(file), lower = name.ToLowerInvariant();
            if (!CitizenExts.Contains(Path.GetExtension(lower))) return null; // lisezmoi, images…
            int at = rel.IndexOf("common/data/", StringComparison.OrdinalIgnoreCase);
            if (at >= 0) return CitizenData + rel.Substring(at + "common/data/".Length);
            var dirs = rel.ToLowerInvariant().Split('/').Reverse().Skip(1).ToList();
            foreach (var d in dirs)
            {
                if (d == "effects") return CitizenData + "effects/" + name;
                if (d == "timecycle") return CitizenData + "timecycle/" + name;
                if (d == "ui") return CitizenData + "ui/" + name;
                if (d == "gta5") return CitizenData + "levels/gta5/" + name;
            }
            string ext = Path.GetExtension(lower);
            if (ext == ".ypt") return CitizenData + "effects/" + name;
            if (ext == ".gfx" || UiXml.Contains(lower)) return CitizenData + "ui/" + name;
            if (lower == "water.xml") return CitizenData + "levels/gta5/" + name;
            if (ext == ".xml" && lower.Contains("timecycle")) return CitizenData + "timecycle/" + name;
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
