// Logique du FiveM GFX Manager (sans interface graphique).
//  - Bibliothèque de packs (graphiques, sons, armes) : les archives sont décompressées et stockées
//  - Ajout de mods à un pack de la bibliothèque
//  - Installation de plusieurs packs à la fois dans FiveM.app (refus si deux packs touchent le même fichier)
//  - Sauvegarde des fichiers remplacés + journal pour pouvoir retirer chaque pack
//  - Sauvegarde complète "d'origine" (sans mods) de citizen / mods / plugins
//  - Installation / désinstallation de ReShade dans FiveM.app\plugins

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
    public class GfxException : Exception
    {
        public GfxException(string message) : base(message) { }
    }

    [DataContract]
    public class PackInfo
    {
        [DataMember] public string Nom;
        [DataMember] public string Id;
        [DataMember] public string Date;
        [DataMember] public string Type;
        [DataMember] public string LibId; // pack de la bibliothèque (null : installé hors bibliothèque)
    }

    // Pack stocké dans la bibliothèque (contenu déjà rangé en citizen / mods / plugins)
    [DataContract]
    public class LibraryPack
    {
        [DataMember] public string Id;
        [DataMember] public string Nom;
        [DataMember] public string Type;
        [DataMember] public string Source;
        [DataMember] public string Date;
        [DataMember] public long Taille;
        [DataMember] public List<string> Mods = new List<string>();
    }

    [DataContract]
    public class ReshadeState
    {
        [DataMember] public List<string> Fichiers = new List<string>();
        [DataMember] public List<string> Dossiers = new List<string>();
        [DataMember] public Dictionary<string, string> Sauvegardes = new Dictionary<string, string>();
        [DataMember] public string Version;
        [DataMember] public string Date;

        public void EnsureInit()
        {
            Fichiers = Fichiers ?? new List<string>();
            Dossiers = Dossiers ?? new List<string>();
            Sauvegardes = Sauvegardes ?? new Dictionary<string, string>();
        }
    }

    [DataContract]
    public class Config
    {
        [DataMember] public string FivemApp;
        [DataMember] public PackInfo PackInstalle; // ancien format (un seul pack), converti au chargement
        [DataMember] public List<PackInfo> PacksInstalles = new List<PackInfo>();
        [DataMember] public ReshadeState Reshade;
    }

    [DataContract]
    public class Manifest
    {
        [DataMember] public string Nom;
        [DataMember] public string Id;
        [DataMember] public string Source;
        [DataMember] public string Date;
        [DataMember] public List<string> Ajoutes = new List<string>();
        [DataMember] public List<string> Remplaces = new List<string>();
        [DataMember] public List<string> DossiersCrees = new List<string>();
    }

    [DataContract]
    public class BackupInfo
    {
        [DataMember] public string Date;
        [DataMember] public string FivemApp;
        [DataMember] public List<string> Dossiers = new List<string>();
        [DataMember] public int Fichiers;
    }

    public static class Core
    {
        public const string AppName = "FiveMGfxManager";

        // Dossiers de FiveM.app qu'un pack graphique peut modifier
        public static readonly string[] FivemDirs = { "citizen", "mods", "plugins" };

        public static readonly string[] PackTypes = { "Graphique", "Son", "Armes" };

        // Fichiers GTA V « bruts » : FiveM ne les charge pas dans mods\ s'ils ne sont pas dans un .rpf
        static readonly string[] RawGameExts =
            { ".ytd", ".ydr", ".yft", ".ydd", ".ytyp", ".ymap", ".ybn", ".awc", ".oac", ".rel", ".meta", ".gxt2" };

        // .rpf de sons souvent remplacés (packs de sons d'armes), rangés dans x64\audio\sfx de GTA V
        static readonly string[] SfxRpfs = { "resident.rpf", "weapons_player.rpf", "explosions.rpf" };

        const string ReshadeSite = "https://reshade.me";
        static readonly string ReshadeShadersZip = Environment.GetEnvironmentVariable("FIVEM_GFX_SHADERS_URL")
            ?? "https://github.com/crosire/reshade-shaders/archive/refs/heads/slim.zip";
        const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) FiveMGfxManager/1.0";

        static string Now() => DateTime.Now.ToString("dd/MM/yyyy HH:mm");

        // ------------------------------------------------------------------ //
        //  Chemins / configuration
        // ------------------------------------------------------------------ //

        public static string DataDir()
        {
            string path = Environment.GetEnvironmentVariable("FIVEM_GFX_DATA");
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
            Directory.CreateDirectory(path);
            return path;
        }

        public static string OriginalBackupDir() => Path.Combine(DataDir(), "sauvegarde_origine");
        public static string InstallsDir() => Path.Combine(DataDir(), "packs_installes");
        public static string LibraryDir() => Path.Combine(DataDir(), "bibliotheque");
        static string ConfigPath() => Path.Combine(DataDir(), "config.json");

        public static T ReadJson<T>(string path) where T : class
        {
            try
            {
                using (var fs = File.OpenRead(path))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(fs);
            }
            catch
            {
                return null;
            }
        }

        public static void WriteJson<T>(string path, T obj)
        {
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
                new DataContractJsonSerializer(typeof(T)).WriteObject(fs, obj);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static Config LoadConfig()
        {
            var cfg = ReadJson<Config>(ConfigPath()) ?? new Config();
            cfg.PacksInstalles = cfg.PacksInstalles ?? new List<PackInfo>();
            if (cfg.PackInstalle != null)
            {
                cfg.PackInstalle.Type = cfg.PackInstalle.Type ?? "Graphique";
                cfg.PacksInstalles.Add(cfg.PackInstalle);
                cfg.PackInstalle = null;
            }
            return cfg;
        }

        public static void SaveConfig(Config cfg) => WriteJson(ConfigPath(), cfg);

        public static string DetectFivemApp()
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM", "FiveM.app")
            };
            foreach (char drive in "CDEFG")
                candidates.Add(drive + @":\FiveM\FiveM.app");
            return candidates.FirstOrDefault(IsValidFivemApp);
        }

        public static bool IsValidFivemApp(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
            var names = Directory.GetFileSystemEntries(path).Select(p => Path.GetFileName(p).ToLowerInvariant());
            return names.Any(n => n == "citizen" || n == "data" || n == "plugins");
        }

        public static bool IsFivemRunning()
        {
            try
            {
                // FiveM.exe et ses sous-processus (FiveM_GTAProcess, FiveM_b3095_GTAProcess…),
                // mais pas ce logiciel (FiveM-GFX-Manager)
                int self = Process.GetCurrentProcess().Id;
                return Process.GetProcesses().Any(p => p.Id != self &&
                    (p.ProcessName.Equals("FiveM", StringComparison.OrdinalIgnoreCase) ||
                     p.ProcessName.StartsWith("FiveM_", StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ //
        //  Outils fichiers
        // ------------------------------------------------------------------ //

        static string P(string root, string rel) => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));

        static void MakeWritable(string path)
        {
            try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
        }

        public static void RemoveTree(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                MakeWritable(f);
            Directory.Delete(path, true);
        }

        static void CopyFile(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst)) MakeWritable(dst);
            File.Copy(src, dst, true);
        }

        static void RemoveFile(string path)
        {
            if (!File.Exists(path)) return;
            MakeWritable(path);
            File.Delete(path);
        }

        static string RelPath(string root, string full)
        {
            root = Path.GetFullPath(root).TrimEnd('\\', '/');
            return Path.GetFullPath(full).Substring(root.Length + 1).Replace('\\', '/');
        }

        // Fichiers de root en chemins relatifs (séparateur '/')
        static List<string> ListFiles(string root) =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(f => RelPath(root, f)).ToList();

        static long FolderSize(string path) =>
            Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

        public static string HumanSize(double n)
        {
            foreach (var unit in new[] { "o", "Ko", "Mo", "Go" })
            {
                if (n < 1024) return n.ToString("0") + " " + unit;
                n /= 1024;
            }
            return n.ToString("0.0") + " To";
        }

        // Nouveau sous-dossier daté et unique dans parent (deux opérations peuvent tomber dans la même seconde)
        static string NewIdDir(string parent, out string id)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            id = stamp;
            for (int i = 2; Directory.Exists(Path.Combine(parent, id)); i++)
                id = stamp + "_" + i;
            string dir = Path.Combine(parent, id);
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string NewTempDir(string prefix)
        {
            string d = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(d);
            return d;
        }

        static void Download(string url, string dest, Action<string> log, Action<double> progress)
        {
            log("Téléchargement : " + url);
            var req = WebRequest.Create(url);
            if (req is HttpWebRequest http) http.UserAgent = UserAgent;
            req.Timeout = 60000;
            using (var resp = req.GetResponse())
            using (var input = resp.GetResponseStream())
            using (var output = File.Create(dest))
            {
                long total = resp.ContentLength, done = 0;
                var buf = new byte[256 * 1024];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    done += n;
                    if (total > 0) progress((double)done / total);
                }
            }
        }

        static string DownloadString(string url)
        {
            var req = WebRequest.Create(url);
            if (req is HttpWebRequest http) http.UserAgent = UserAgent;
            req.Timeout = 30000;
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

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
        //  Sauvegarde d'origine (copie complète sans mods)
        // ------------------------------------------------------------------ //

        public static BackupInfo OriginalBackupInfo() => ReadJson<BackupInfo>(Path.Combine(OriginalBackupDir(), "info.json"));
        public static bool HasOriginalBackup() => OriginalBackupInfo() != null;

        public static void CreateOriginalBackup(string fivemApp, Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp)) throw new GfxException("Le dossier FiveM.app est invalide.");
            string dest = OriginalBackupDir();
            string tmp = dest + "_tmp";
            RemoveTree(tmp);
            Directory.CreateDirectory(tmp);

            var present = new List<string>();
            var files = new List<(string dir, string rel)>();
            foreach (var d in FivemDirs)
            {
                string src = Path.Combine(fivemApp, d);
                if (!Directory.Exists(src)) continue;
                present.Add(d);
                Directory.CreateDirectory(Path.Combine(tmp, d));
                files.AddRange(ListFiles(src).Select(rel => (d, rel)));
            }

            log($"Sauvegarde d'origine : {files.Count} fichiers à copier…");
            for (int i = 0; i < files.Count; i++)
            {
                var (d, rel) = files[i];
                CopyFile(P(Path.Combine(fivemApp, d), rel), P(Path.Combine(tmp, d), rel));
                if (i % 50 == 0 || i == files.Count - 1) progress((i + 1) / (double)files.Count);
            }

            WriteJson(Path.Combine(tmp, "info.json"),
                      new BackupInfo { Date = Now(), FivemApp = fivemApp, Dossiers = present, Fichiers = files.Count });
            RemoveTree(dest);
            Directory.Move(tmp, dest);
            log($"Sauvegarde d'origine créée ({HumanSize(FolderSize(dest))}).");
        }

        // Remet FiveM.app exactement dans l'état de la sauvegarde d'origine.
        public static void RestoreOriginalBackup(string fivemApp, Action<string> log, Action<double> progress)
        {
            var info = OriginalBackupInfo() ?? throw new GfxException("Aucune sauvegarde d'origine n'existe.");
            string srcRoot = OriginalBackupDir();

            foreach (var d in FivemDirs)
            {
                string target = Path.Combine(fivemApp, d);
                if (!Directory.Exists(target)) continue;
                log($"Suppression de {d}\\ (version moddée)…");
                RemoveTree(target);
            }

            var files = new List<(string dir, string rel)>();
            foreach (var d in info.Dossiers ?? new List<string>())
            {
                Directory.CreateDirectory(Path.Combine(fivemApp, d));
                files.AddRange(ListFiles(Path.Combine(srcRoot, d)).Select(rel => (d, rel)));
            }

            log($"Restauration de {files.Count} fichiers d'origine…");
            for (int i = 0; i < files.Count; i++)
            {
                var (d, rel) = files[i];
                CopyFile(P(Path.Combine(srcRoot, d), rel), P(Path.Combine(fivemApp, d), rel));
                if (i % 50 == 0 || i == files.Count - 1) progress((i + 1) / (double)files.Count);
            }

            // plus aucun pack ni ReShade n'est installé après une restauration complète
            var cfg = LoadConfig();
            cfg.PacksInstalles.Clear();
            cfg.Reshade = null;
            SaveConfig(cfg);
            RemoveTree(InstallsDir());
            RemoveTree(Path.Combine(DataDir(), "reshade_sauvegarde"));
            log("FiveM.app a été remis dans son état d'origine.");
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
                var files = NormalizePack(folder, Path.Combine(dir, "contenu"), log);
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
                string staged = StageSource(source, work, log, p => progress(p * 0.6));
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

        // ------------------------------------------------------------------ //
        //  Installation des packs dans FiveM.app (plusieurs à la fois)
        // ------------------------------------------------------------------ //

        public static List<PackInfo> InstalledPacks() => LoadConfig().PacksInstalles;

        public static Manifest InstallLibraryPack(string id, string fivemApp, Action<string> log, Action<double> progress)
        {
            var pack = GetLibraryPack(id);
            if (IsInstalled(id)) throw new GfxException($"Le pack « {pack.Nom} » est déjà installé.");
            return InstallContent(LibraryContent(id), pack.Nom, pack.Type, id, pack.Source, fivemApp, log, progress);
        }

        // Installe directement une archive, un dossier ou un .rpf (sans passer par la bibliothèque).
        public static Manifest InstallPack(string source, string fivemApp, Action<string> log, Action<double> progress,
                                           string type = "Graphique")
        {
            if (!IsValidFivemApp(fivemApp))
                throw new GfxException("Le dossier FiveM.app est invalide. Choisis le bon dossier en haut.");
            return WithTempDir(work =>
                InstallContent(StageSource(source, work, log, p => progress(p * 0.5)), SourceName(source), type, null,
                               source, fivemApp, log, p => progress(0.5 + p * 0.5)));
        }

        static string ManifestPath(string installId) => Path.Combine(InstallsDir(), installId, "manifest.json");

        // Fichiers de FiveM.app écrits par un pack installé
        static IEnumerable<string> Touched(Manifest m) =>
            (m.Ajoutes ?? new List<string>()).Concat(m.Remplaces ?? new List<string>());

        static Manifest InstallContent(string content, string packName, string type, string libId, string source,
                                       string fivemApp, Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp))
                throw new GfxException("Le dossier FiveM.app est invalide. Choisis le bon dossier en haut.");

            var files = ListFiles(content);
            if (files.Count == 0) throw new GfxException("Le pack ne contient aucun fichier.");

            var mine = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var cfgBefore = LoadConfig();
            // un fichier de ReShade remplacé par le pack serait perdu au retrait du pack
            var rsClash = (cfgBefore.Reshade?.Fichiers ?? new List<string>()).Where(mine.Contains).ToList();
            if (rsClash.Count > 0)
                throw new GfxException($"Ce pack remplace des fichiers de ReShade : {string.Join(", ", rsClash)}\n\n" +
                                       "Désinstalle ReShade (onglet ReShade), installe le pack, puis réinstalle ReShade.");
            // deux packs installés ne doivent jamais toucher le même fichier (sinon le retrait casserait l'autre)
            foreach (var other in cfgBefore.PacksInstalles)
            {
                var om = ReadJson<Manifest>(ManifestPath(other.Id));
                if (om == null) continue;
                var clash = Touched(om).Where(mine.Contains).ToList();
                if (clash.Count > 0)
                    throw new GfxException($"Ce pack remplace des fichiers déjà modifiés par le pack « {other.Nom} » :\n  " +
                                           string.Join("\n  ", clash.Take(5)) + (clash.Count > 5 ? $"\n  … (+{clash.Count - 5})" : "") +
                                           $"\n\nRetire d'abord « {other.Nom} », puis réessaie.");
            }

            Directory.CreateDirectory(InstallsDir());
            string packDir = NewIdDir(InstallsDir(), out string packId);
            string backupDir = Path.Combine(packDir, "fichiers_remplaces");
            Directory.CreateDirectory(backupDir);

            var manifest = new Manifest { Nom = packName, Id = packId, Source = source, Date = Now() };

            // dossiers qui n'existaient pas avant (pour pouvoir les supprimer ensuite)
            var newDirs = new HashSet<string>();
            foreach (var rel in files)
            {
                var parts = rel.Split('/');
                for (int i = 1; i < parts.Length; i++)
                {
                    string d = string.Join("/", parts.Take(i));
                    if (!Directory.Exists(P(fivemApp, d))) newDirs.Add(d);
                }
            }
            manifest.DossiersCrees = newDirs.OrderBy(d => d).ToList();

            log($"Installation de {files.Count} fichiers…");
            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    string rel = files[i];
                    string dst = P(fivemApp, rel);
                    if (File.Exists(dst))
                    {
                        CopyFile(dst, P(backupDir, rel));
                        manifest.Remplaces.Add(rel);
                    }
                    else
                    {
                        manifest.Ajoutes.Add(rel);
                    }
                    CopyFile(P(content, rel), dst);
                    if (i % 25 == 0 || i == files.Count - 1) progress((i + 1) / (double)files.Count);
                }
            }
            catch
            {
                log("Erreur pendant la copie, annulation des modifications…");
                Revert(manifest, fivemApp, backupDir, log);
                RemoveTree(packDir);
                throw;
            }

            WriteJson(ManifestPath(packId), manifest);
            var cfg = LoadConfig();
            cfg.PacksInstalles.Add(new PackInfo { Nom = packName, Id = packId, Date = manifest.Date, Type = type, LibId = libId });
            SaveConfig(cfg);
            log($"Pack « {packName} » installé : {manifest.Ajoutes.Count} ajoutés, {manifest.Remplaces.Count} remplacés.");
            return manifest;
        }

        static void Revert(Manifest m, string fivemApp, string backupDir, Action<string> log)
        {
            foreach (var rel in m.Ajoutes ?? new List<string>())
                RemoveFile(P(fivemApp, rel));
            foreach (var rel in m.Remplaces ?? new List<string>())
            {
                string b = P(backupDir, rel);
                if (File.Exists(b)) CopyFile(b, P(fivemApp, rel));
                else log("  ! sauvegarde manquante : " + rel);
            }
            // on supprime les dossiers créés par le pack, du plus profond au moins profond
            foreach (var d in (m.DossiersCrees ?? new List<string>()).OrderByDescending(x => x.Count(c => c == '/')))
            {
                try { Directory.Delete(P(fivemApp, d)); }
                catch { } // pas vide (autre contenu ajouté depuis) : on le garde
            }
        }

        // Retire un pack installé (id d'installation) et remet les fichiers qu'il avait remplacés.
        public static void UninstallPack(string packId, string fivemApp, Action<string> log, Action<double> progress)
        {
            var info = InstalledPacks().FirstOrDefault(p => p.Id == packId)
                ?? throw new GfxException("Ce pack n'est pas installé.");
            string packDir = Path.Combine(InstallsDir(), info.Id);
            var manifest = ReadJson<Manifest>(ManifestPath(info.Id))
                ?? throw new GfxException("Le journal d'installation est introuvable.\n" +
                                          "Utilise « Restaurer FiveM d'origine » à la place.");

            log($"Retrait du pack « {info.Nom} »…");
            progress(0.3);
            var cfg = LoadConfig();
            string backupDir = Path.Combine(packDir, "fichiers_remplaces");
            HandOverToReshade(manifest, cfg, backupDir, log);
            Revert(manifest, fivemApp, backupDir, log);
            progress(1);
            RemoveTree(packDir);
            cfg.PacksInstalles.RemoveAll(p => p.Id == packId);

            // dossiers créés par ce pack mais encore utilisés par un autre : c'est l'autre pack qui les supprimera
            var kept = (manifest.DossiersCrees ?? new List<string>()).Where(d => Directory.Exists(P(fivemApp, d))).ToList();
            foreach (var other in cfg.PacksInstalles)
            {
                string path = ManifestPath(other.Id);
                var om = ReadJson<Manifest>(path);
                if (om == null || kept.Count == 0) continue;
                om.DossiersCrees = om.DossiersCrees ?? new List<string>();
                var mine = Touched(om).ToList();
                var taken = kept.Where(d => !om.DossiersCrees.Contains(d) &&
                                            mine.Any(f => f.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase))).ToList();
                if (taken.Count == 0) continue;
                om.DossiersCrees.AddRange(taken);
                WriteJson(path, om);
                kept.RemoveAll(taken.Contains);
            }
            SaveConfig(cfg);
            log($"Pack retiré : {manifest.Ajoutes.Count} fichiers supprimés, {manifest.Remplaces.Count} fichiers d'origine remis.");
        }

        // ReShade installé après le pack a écrasé certains de ses fichiers (ex : plugins\dxgi.dll) :
        // on les laisse en place et c'est ReShade qui remettra l'original à sa désinstallation.
        static void HandOverToReshade(Manifest m, Config cfg, string packBackupDir, Action<string> log)
        {
            var rs = cfg.Reshade;
            if (rs == null) return;
            rs.EnsureInit();
            var shared = Touched(m).Where(f => rs.Fichiers.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            if (shared.Count == 0) return;
            string rsBackupDir = Path.Combine(DataDir(), "reshade_sauvegarde");
            foreach (var rel in shared)
            {
                string key = rs.Sauvegardes.Keys.FirstOrDefault(k => k.Equals(rel, StringComparison.OrdinalIgnoreCase)) ?? rel;
                string old = P(packBackupDir, rel);
                if ((m.Remplaces ?? new List<string>()).Contains(rel) && File.Exists(old))
                {
                    // l'original d'avant le pack devient la sauvegarde de ReShade
                    string b = P(rsBackupDir, key);
                    CopyFile(old, b);
                    rs.Sauvegardes[key] = b;
                }
                else if (rs.Sauvegardes.TryGetValue(key, out var b))
                {
                    // le fichier n'existait pas avant le pack : ReShade devra simplement le supprimer
                    RemoveFile(b);
                    rs.Sauvegardes.Remove(key);
                }
                m.Ajoutes?.Remove(rel);
                m.Remplaces?.Remove(rel);
                log($"  {rel} est utilisé par ReShade : laissé en place.");
            }
            SaveConfig(cfg);
        }

        // Vide le cache FiveM (conseillé après un changement de pack graphique).
        public static void ClearFivemCache(string fivemApp, Action<string> log)
        {
            int removed = 0;
            foreach (var sub in new[] { "cache", "server-cache", "server-cache-priv" })
                foreach (var b in new[] { Path.Combine(fivemApp, "data"), fivemApp })
                {
                    string p = Path.Combine(b, sub);
                    if (!Directory.Exists(p)) continue;
                    RemoveTree(p);
                    removed++;
                    log("Supprimé : " + RelPath(fivemApp, p));
                }
            log(removed > 0 ? "Cache vidé." : "Aucun cache à vider.");
        }

        // ------------------------------------------------------------------ //
        //  ReShade
        // ------------------------------------------------------------------ //

        public static ReshadeState ReshadeStatus() => LoadConfig().Reshade;

        public static (string url, string version) FindLatestReshadeUrl(Action<string> log)
        {
            log("Recherche de la dernière version de ReShade sur reshade.me…");
            string html = DownloadString(ReshadeSite);
            // version normale (pas _Addon : la version add-on est bloquée par FiveM)
            var best = Regex.Matches(html, @"/downloads/ReShade_Setup_([\d.]+)\.exe").Cast<Match>()
                            .Select(m => (path: m.Value, ver: m.Groups[1].Value))
                            .Where(x => Version.TryParse(x.ver, out _))
                            .OrderByDescending(x => Version.Parse(x.ver))
                            .FirstOrDefault();
            if (best.path == null)
                throw new GfxException("Lien de téléchargement ReShade introuvable.\n" +
                                       "Télécharge ReShade_Setup_x.x.x.exe sur reshade.me et choisis-le manuellement.");
            return (ReshadeSite + best.path, best.ver);
        }

        // Le setup ReShade contient une archive zip (collée à la fin de l'exe) avec ReShade64.dll.
        static string ExtractReshadeDll(string setupExe, string destDir)
        {
            byte[] data = File.ReadAllBytes(setupExe);
            int eocd = -1;
            for (int i = data.Length - 22; i >= Math.Max(0, data.Length - 70000); i--)
                if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x05 && data[i + 3] == 0x06) { eocd = i; break; }
            if (eocd >= 0)
            {
                long cdSize = BitConverter.ToUInt32(data, eocd + 12);
                long cdOffset = BitConverter.ToUInt32(data, eocd + 16);
                long start = Math.Max(0, eocd - cdSize - cdOffset);
                using (var ms = new MemoryStream(data, (int)start, data.Length - (int)start))
                using (var zip = ArchiveFactory.OpenArchive(ms))
                {
                    var entry = zip.Entries.FirstOrDefault(e => !e.IsDirectory &&
                        string.Equals(Path.GetFileName(e.Key), "ReShade64.dll", StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        string outPath = Path.Combine(destDir, "ReShade64.dll");
                        using (var s = entry.OpenEntryStream())
                        using (var o = File.Create(outPath))
                            s.CopyTo(o);
                        return outPath;
                    }
                }
            }
            throw new GfxException("ReShade64.dll introuvable dans ce fichier.\n" +
                                   "Choisis le fichier ReShade_Setup_x.x.x.exe officiel (ou ReShade64.dll).");
        }

        // setupFile : ReShade_Setup_x.exe ou ReShade64.dll (null = téléchargement automatique).
        public static void InstallReshade(string fivemApp, string setupFile, bool withShaders, string preset,
                                          Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp)) throw new GfxException("Le dossier FiveM.app est invalide.");
            string plugins = Path.Combine(fivemApp, "plugins");
            Directory.CreateDirectory(plugins);
            string work = NewTempDir("fivem_reshade_");
            var state = ReshadeStatus() ?? new ReshadeState();
            state.EnsureInit();
            string backupDir = Path.Combine(DataDir(), "reshade_sauvegarde");

            void Track(string path)
            {
                string rel = RelPath(fivemApp, path);
                if (state.Fichiers.Contains(rel)) return;
                // un fichier qui existait avant ReShade (ex : dxgi.dll d'un pack) est sauvegardé
                if (File.Exists(path) && !state.Sauvegardes.ContainsKey(rel))
                {
                    string b = P(backupDir, rel);
                    CopyFile(path, b);
                    state.Sauvegardes[rel] = b;
                }
                state.Fichiers.Add(rel);
            }

            try
            {
                progress(0.05);
                string dll, version;
                if (setupFile != null && setupFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    dll = setupFile;
                    version = "(dll manuelle)";
                }
                else
                {
                    if (setupFile == null)
                    {
                        string url;
                        (url, version) = FindLatestReshadeUrl(log);
                        setupFile = Path.Combine(work, "ReShade_Setup.exe");
                        Download(url, setupFile, log, p => progress(0.05 + p * 0.35));
                    }
                    else
                    {
                        var m = Regex.Match(Path.GetFileName(setupFile), @"\d+\.[\d.]*\d");
                        version = m.Success ? m.Value : "?";
                    }
                    log("Extraction de ReShade64.dll…");
                    dll = ExtractReshadeDll(setupFile, work);
                }
                progress(0.45);

                string target = Path.Combine(plugins, "dxgi.dll");
                Track(target);
                CopyFile(dll, target);
                log("ReShade copié dans plugins\\dxgi.dll");

                string shadersDest = Path.Combine(plugins, "reshade-shaders");
                if (withShaders)
                {
                    string zpath = Path.Combine(work, "shaders.zip");
                    Download(ReshadeShadersZip, zpath, log, p => progress(0.45 + p * 0.35));
                    string ex = Path.Combine(work, "shaders");
                    ExtractArchive(zpath, ex, log, p => progress(0.8 + p * 0.1));
                    string top = Directory.GetDirectories(ex).First();
                    if (!Directory.Exists(shadersDest))
                    {
                        string rel = RelPath(fivemApp, shadersDest);
                        if (!state.Dossiers.Contains(rel)) state.Dossiers.Add(rel);
                    }
                    foreach (var sub in new[] { "Shaders", "Textures" })
                    {
                        string s = Path.Combine(top, sub);
                        if (!Directory.Exists(s)) continue;
                        foreach (var rel in ListFiles(s))
                            CopyFile(P(s, rel), P(Path.Combine(shadersDest, sub), rel));
                    }
                    log("Shaders officiels installés dans plugins\\reshade-shaders");
                }
                progress(0.9);

                string ini = Path.Combine(plugins, "ReShade.ini");
                if (!File.Exists(ini))
                {
                    Track(ini);
                    File.WriteAllText(ini,
                        "[GENERAL]\r\n" +
                        $"EffectSearchPaths={Path.Combine(shadersDest, "Shaders")}\\**\r\n" +
                        $"TextureSearchPaths={Path.Combine(shadersDest, "Textures")}\\**\r\n" +
                        $"PresetPath={Path.Combine(plugins, "ReShadePreset.ini")}\r\n" +
                        "\r\n[INPUT]\r\nKeyOverlay=36,0,0,0\r\n" +
                        "\r\n[OVERLAY]\r\nTutorialProgress=4\r\n");
                    log("ReShade.ini créé (touche du menu : Début / Home)");
                }

                if (!string.IsNullOrEmpty(preset))
                {
                    string p = Path.Combine(plugins, "ReShadePreset.ini");
                    Track(p);
                    CopyFile(preset, p);
                    log($"Preset « {Path.GetFileName(preset)} » appliqué.");
                }

                state.Version = version;
                state.Date = Now();
                var cfg = LoadConfig();
                cfg.Reshade = state;
                SaveConfig(cfg);
                progress(1);
                log($"ReShade {version} installé. Lance FiveM puis appuie sur Début (Home) en jeu.");
            }
            finally
            {
                try { RemoveTree(work); } catch { }
            }
        }

        public static void UninstallReshade(string fivemApp, Action<string> log, Action<double> progress)
        {
            var state = ReshadeStatus() ?? throw new GfxException("ReShade n'a pas été installé par ce logiciel.");
            state.EnsureInit();
            foreach (var rel in state.Fichiers)
            {
                string path = P(fivemApp, rel);
                if (state.Sauvegardes.TryGetValue(rel, out var backup) && File.Exists(backup))
                {
                    CopyFile(backup, path);
                    log("Remis d'origine : " + rel);
                }
                else
                {
                    RemoveFile(path);
                    log("Supprimé : " + rel);
                }
            }
            foreach (var rel in state.Dossiers)
            {
                RemoveTree(P(fivemApp, rel));
                log("Supprimé : " + rel + "\\");
            }
            foreach (var extra in new[] { "ReShade.log", "ReShade64.log" })
                RemoveFile(Path.Combine(fivemApp, "plugins", extra));
            RemoveTree(Path.Combine(DataDir(), "reshade_sauvegarde"));
            var cfg = LoadConfig();
            cfg.Reshade = null;
            SaveConfig(cfg);
            progress(1);
            log("ReShade désinstallé.");
        }
    }
}
