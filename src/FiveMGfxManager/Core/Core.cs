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
        [DataMember] public string Dossier;      // dossier de rangement dans le logiciel (null : aucun)
        [DataMember] public List<string> Armes;  // fichiers du jeu remplacés (.ydr, .ytd…) ; null : pas encore calculé
        [DataMember] public bool Favori;
        [DataMember] public int Note;            // 0 à 5 étoiles
        [DataMember] public string Commentaire;
        [DataMember] public List<string> Suspects; // .exe, scripts… trouvés à l'import (null : pas encore analysé)
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
        [DataMember] public List<string> SonsCoupes; // packs son retirés pour un serveur « pure » (à remettre)
        [DataMember] public long QuantVMaj;          // date (ticks) de la dernière mise à jour de QuantV
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
        // fichiers de ReShade remplacés par le pack (leur version ReShade est dans fichiers_remplaces)
        [DataMember] public List<string> SurReshade = new List<string>();
        // sons d'armes de FiveM.app\mods mis de côté avant l'installation (remis au retrait du pack)
        [DataMember] public List<string> Deplaces = new List<string>();
        // dossier de GTA V où sont allés les sons (mods/x64/audio/… → x64\audio\…) ; null : tout est dans FiveM.app
        [DataMember] public string DossierGta;
        // fichiers du jeu remplacés par les paquets du pack (null : installé avant la v1.4)
        [DataMember] public List<string> Armes;
        // fichiers retirés des paquets du pack → id d'installation du pack qui les fournit à la place
        [DataMember] public Dictionary<string, string> Exclues;
        // taille des fichiers écrits, pour voir ceux qu'une mise à jour de FiveM / GTA a remplacés
        [DataMember] public Dictionary<string, long> Tailles;
    }

    [DataContract]
    public class BackupInfo
    {
        [DataMember] public string Date;
        [DataMember] public string FivemApp;
        [DataMember] public List<string> Dossiers = new List<string>();
        [DataMember] public int Fichiers;
    }

    public static partial class Core
    {
        public const string AppName = "FiveMGfxManager";

        // Dossiers de FiveM.app qu'un pack graphique peut modifier
        public static readonly string[] FivemDirs = { "citizen", "mods", "plugins" };

        public static readonly string[] PackTypes = { "Graphique", "Son", "Armes" };

        // Type des mods (onglet Mods) : leurs .rpf vont dans FiveM.app\mods
        public const string ModType = "Mod";

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

        // Dossier de GTA V : IVPath de FiveM.app\CitizenFX.ini, sinon le registre (Rockstar, Steam, Epic).
        // FIVEM_GFX_GTA le remplace (tests) ; null si GTA V est introuvable.
        public static string GtaPath()
        {
            string env = Environment.GetEnvironmentVariable("FIVEM_GFX_GTA");
            if (env != null) return Directory.Exists(env) ? env : null;
            var candidates = new List<string>();
            try
            {
                string fivem = LoadConfig().FivemApp ?? DetectFivemApp();
                string ini = fivem == null ? null : Path.Combine(fivem, "CitizenFX.ini");
                if (ini != null && File.Exists(ini))
                    candidates.AddRange(File.ReadAllLines(ini)
                                            .Where(l => l.StartsWith("IVPath=", StringComparison.OrdinalIgnoreCase))
                                            .Select(l => l.Substring(7).Trim()));
            }
            catch { }
            foreach (var (key, value) in new[]
            {
                (@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", "InstallFolder"),
                (@"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolderSteam"),
                (@"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolderEpic"),
                (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 271590", "InstallLocation"),
            })
            {
                try
                {
                    using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key))
                        if (k?.GetValue(value) is string s) candidates.Add(s.Trim().Trim('"'));
                }
                catch { }
            }
            // Steam enregistre parfois …\GTAV\GTA5.exe : on garde le dossier
            return candidates.Select(c => c.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(c) : c)
                             .FirstOrDefault(c => !string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "x64a.rpf")));
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

    }
}
