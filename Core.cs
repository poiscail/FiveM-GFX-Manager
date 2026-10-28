// Logique du FiveM GFX Manager (sans interface graphique).
//  - Installation d'un pack graphique (.zip / .7z / .rar ou dossier) dans FiveM.app
//  - Sauvegarde des fichiers remplacés + journal pour pouvoir retirer le pack
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
        [DataMember] public PackInfo PackInstalle;
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

        public static Config LoadConfig() => ReadJson<Config>(ConfigPath()) ?? new Config();
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
            if (bestRoot == null)
                throw new GfxException("Impossible de trouver un dossier « citizen », « mods » ou « plugins » dans le pack.\n" +
                                       "Ce pack n'a pas la structure attendue pour FiveM.app.");
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
            cfg.PackInstalle = null;
            cfg.Reshade = null;
            SaveConfig(cfg);
            RemoveTree(InstallsDir());
            RemoveTree(Path.Combine(DataDir(), "reshade_sauvegarde"));
            log("FiveM.app a été remis dans son état d'origine.");
        }

        // ------------------------------------------------------------------ //
        //  Packs graphiques
        // ------------------------------------------------------------------ //

        public static PackInfo InstalledPack() => LoadConfig().PackInstalle;

        public static Manifest InstallPack(string source, string fivemApp, Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp))
                throw new GfxException("Le dossier FiveM.app est invalide. Choisis le bon dossier en haut.");
            if (InstalledPack() != null)
                throw new GfxException("Un pack est déjà installé. Retire-le d'abord.");

            string packName = Directory.Exists(source)
                ? Path.GetFileName(source.TrimEnd('\\', '/'))
                : Path.GetFileNameWithoutExtension(source);
            string work = NewTempDir("fivem_gfx_");
            try
            {
                string extracted;
                if (Directory.Exists(source))
                {
                    extracted = source;
                }
                else
                {
                    extracted = Path.Combine(work, "pack");
                    ExtractArchive(source, extracted, log, p => progress(p * 0.5));
                }

                var (root, found) = FindPackRoot(extracted);
                log("Dossiers trouvés dans le pack : " + string.Join(", ", found.Keys.OrderBy(k => k)));
                var ignored = Directory.GetFileSystemEntries(root).Select(Path.GetFileName)
                                       .Where(n => !FivemDirs.Contains(n.ToLowerInvariant())).ToList();
                if (ignored.Count > 0)
                    log("Ignorés (pas des dossiers FiveM.app) : " + string.Join(", ", ignored));

                var files = new List<(string rel, string src)>();
                foreach (var kv in found)
                    files.AddRange(ListFiles(kv.Value).Select(rel => (kv.Key + "/" + rel, P(kv.Value, rel))));
                if (files.Count == 0) throw new GfxException("Le pack ne contient aucun fichier.");

                string packId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string packDir = Path.Combine(InstallsDir(), packId);
                string backupDir = Path.Combine(packDir, "fichiers_remplaces");
                Directory.CreateDirectory(backupDir);

                var manifest = new Manifest { Nom = packName, Id = packId, Source = source, Date = Now() };

                // dossiers qui n'existaient pas avant (pour pouvoir les supprimer ensuite)
                var newDirs = new HashSet<string>();
                foreach (var (rel, _) in files)
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
                        var (rel, src) = files[i];
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
                        CopyFile(src, dst);
                        if (i % 25 == 0 || i == files.Count - 1) progress(0.5 + 0.5 * (i + 1) / files.Count);
                    }
                }
                catch
                {
                    log("Erreur pendant la copie, annulation des modifications…");
                    Revert(manifest, fivemApp, backupDir, log);
                    RemoveTree(packDir);
                    throw;
                }

                WriteJson(Path.Combine(packDir, "manifest.json"), manifest);
                var cfg = LoadConfig();
                cfg.PackInstalle = new PackInfo { Nom = packName, Id = packId, Date = manifest.Date };
                SaveConfig(cfg);
                log($"Pack « {packName} » installé : {manifest.Ajoutes.Count} ajoutés, {manifest.Remplaces.Count} remplacés.");
                return manifest;
            }
            finally
            {
                try { RemoveTree(work); } catch { }
            }
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

        // Retire le pack installé et remet les fichiers qu'il avait remplacés.
        public static void UninstallPack(string fivemApp, Action<string> log, Action<double> progress)
        {
            var info = InstalledPack() ?? throw new GfxException("Aucun pack n'est installé.");
            string packDir = Path.Combine(InstallsDir(), info.Id);
            var manifest = ReadJson<Manifest>(Path.Combine(packDir, "manifest.json"))
                ?? throw new GfxException("Le journal d'installation est introuvable.\n" +
                                          "Utilise « Restaurer FiveM d'origine » à la place.");

            log($"Retrait du pack « {info.Nom} »…");
            progress(0.3);
            Revert(manifest, fivemApp, Path.Combine(packDir, "fichiers_remplaces"), log);
            progress(1);
            RemoveTree(packDir);
            var cfg = LoadConfig();
            cfg.PackInstalle = null;
            SaveConfig(cfg);
            log($"Pack retiré : {manifest.Ajoutes.Count} fichiers supprimés, {manifest.Remplaces.Count} fichiers d'origine remis.");
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
