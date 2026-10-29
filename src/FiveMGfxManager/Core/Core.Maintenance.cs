// Aperçu d'installation, fichiers suspects, vérification après une mise à jour du jeu,
// lancement de FiveM

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FiveMGfxManager
{
    // Ce que l'installation d'un pack va faire
    public class InstallPreview
    {
        public List<string> Nouveaux = new List<string>();
        public List<string> Remplaces = new List<string>();
        public List<PackConflict> Conflits = new List<PackConflict>();
        public List<string> Suspects = new List<string>();
    }

    // Pack installé dont des fichiers ont été remis d'origine (mise à jour de FiveM, vérification Steam…)
    public class PackDamage
    {
        public PackInfo Pack;
        public List<string> Manquants = new List<string>();
        public List<string> Modifies = new List<string>();
        public int Count => Manquants.Count + Modifies.Count;
    }


    public static partial class Core
    {
        // ------------------------------------------------------------------ //
        //  Fichiers suspects (programmes, scripts) dans un pack
        // ------------------------------------------------------------------ //

        static readonly HashSet<string> SuspectExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".scr", ".com", ".msi",
            ".jar", ".lnk", ".hta", ".wsf", ".reg", ".pif", ".cpl",
        };

        // .dll normales d'un pack graphique : ReShade / ENB
        static readonly HashSet<string> SafeDlls = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dxgi.dll", "d3d11.dll", "d3d9.dll", "d3d12.dll", "opengl32.dll", "reshade64.dll", "reshade.dll",
        };

        public static List<string> SuspiciousFiles(string content)
        {
            if (!Directory.Exists(content)) return new List<string>();
            return ListFiles(content).Where(rel =>
            {
                string name = Path.GetFileName(rel), ext = Path.GetExtension(rel);
                return SuspectExt.Contains(ext) || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) && !SafeDlls.Contains(name);
            }).ToList();
        }

        // Fichiers suspects d'un pack de la bibliothèque (calculés une fois pour les anciens packs)
        public static List<string> PackSuspects(LibraryPack pack)
        {
            if (pack.Suspects != null) return pack.Suspects;
            pack.Suspects = SuspiciousFiles(LibraryContent(pack.Id));
            try { SaveLibraryPack(pack); } catch { }
            return pack.Suspects;
        }

        // ------------------------------------------------------------------ //
        //  Aperçu de l'installation
        // ------------------------------------------------------------------ //

        public static InstallPreview PreviewInstall(string libId, string fivemApp)
        {
            var pack = GetLibraryPack(libId);
            var files = ListFiles(LibraryContent(libId));
            var m = new Manifest { DossierGta = files.Any(IsGtaAudio) ? GtaPath() : null };
            bool valid = IsValidFivemApp(fivemApp);
            var r = new InstallPreview { Suspects = PackSuspects(pack) };
            foreach (var rel in files)
                (valid && File.Exists(Dest(m, fivemApp, rel)) ? r.Remplaces : r.Nouveaux).Add(rel);
            r.Conflits = FindConflicts(libId);
            return r;
        }

        // ------------------------------------------------------------------ //
        //  Vérification des packs installés (après une mise à jour de FiveM / GTA V)
        // ------------------------------------------------------------------ //

        // Retient la taille des fichiers écrits par chaque pack installé
        public static void SnapshotSizes(string fivemApp)
        {
            foreach (var info in InstalledPacks())
            {
                var m = ReadJson<Manifest>(ManifestPath(info.Id));
                if (m == null) continue;
                m.Tailles = Sizes(m, fivemApp);
                WriteJson(ManifestPath(info.Id), m);
            }
        }

        static Dictionary<string, long> Sizes(Manifest m, string fivemApp)
        {
            var d = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in Touched(m).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var f = new FileInfo(Dest(m, fivemApp, rel));
                if (f.Exists) d[rel] = f.Length;
            }
            return d;
        }

        // Packs installés dont des fichiers ont disparu ou changé depuis l'installation
        public static List<PackDamage> CheckInstalledPacks(string fivemApp)
        {
            var list = new List<PackDamage>();
            if (!IsValidFivemApp(fivemApp)) return list;
            var cfg = LoadConfig();
            var reshade = new HashSet<string>(cfg.Reshade?.Fichiers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var info in cfg.PacksInstalles)
            {
                var m = ReadJson<Manifest>(ManifestPath(info.Id));
                if (m == null) continue;
                if (m.Tailles == null)
                {
                    // installé avant la v1.5 : l'état actuel sert de référence
                    m.Tailles = Sizes(m, fivemApp);
                    WriteJson(ManifestPath(info.Id), m);
                    continue;
                }
                var d = Damage(info, m, fivemApp, reshade);
                if (d.Count > 0) list.Add(d);
            }
            return list;
        }

        static readonly HashSet<string> EditableExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".ini", ".txt", ".cfg", ".json", ".log",
        };

        static PackDamage Damage(PackInfo info, Manifest m, string fivemApp, HashSet<string> reshade)
        {
            var d = new PackDamage { Pack = info };
            var sizes = new Dictionary<string, long>(m.Tailles ?? new Dictionary<string, long>(), StringComparer.OrdinalIgnoreCase);
            foreach (var rel in Touched(m).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                // les réglages (.ini des presets ReShade…) changent normalement après l'installation
                if (reshade.Contains(rel) || EditableExt.Contains(Path.GetExtension(rel)) || !sizes.TryGetValue(rel, out long size)) continue;
                var f = new FileInfo(Dest(m, fivemApp, rel));
                if (!f.Exists) d.Manquants.Add(rel);
                else if (f.Length != size) d.Modifies.Add(rel);
            }
            return d;
        }

        // Remet les fichiers du pack que le jeu a remplacés. Les nouveaux fichiers du jeu deviennent
        // les fichiers d'origine (remis au retrait du pack).
        public static void RepairPack(string installId, string fivemApp, Action<string> log)
        {
            var cfg = LoadConfig();
            var info = cfg.PacksInstalles.FirstOrDefault(p => p.Id == installId)
                ?? throw new GfxException("Ce pack n'est pas installé.");
            var m = ReadJson<Manifest>(ManifestPath(installId))
                ?? throw new GfxException($"Le journal d'installation de « {info.Nom} » est introuvable.");
            string content = info.LibId == null ? null : LibraryContent(info.LibId);
            if (content == null || !Directory.Exists(content))
                throw new GfxException($"« {info.Nom} » n'est pas dans la bibliothèque : retire-le puis réinstalle-le à la main.");
            var reshade = new HashSet<string>(cfg.Reshade?.Fichiers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var d = Damage(info, m, fivemApp, reshade);
            string backupDir = Path.Combine(InstallsDir(), installId, "fichiers_remplaces");
            int n = 0;
            foreach (var rel in d.Manquants.Concat(d.Modifies))
            {
                string src = P(content, rel), dst = Dest(m, fivemApp, rel);
                if (!File.Exists(src)) { log("  ! absent du pack : " + rel); continue; }
                if (File.Exists(dst) && m.Remplaces.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    CopyFile(dst, P(backupDir, rel)); // version mise à jour du jeu
                CopyFile(src, dst);
                n++;
            }
            if (m.Exclues != null && m.Exclues.Count > 0) RebuildPackages(m, content, fivemApp, log);
            SnapshotSizes(fivemApp);
            log($"« {info.Nom} » réparé : {n} fichier(s) remis.");
        }

        // ------------------------------------------------------------------ //
        //  Lancer FiveM
        // ------------------------------------------------------------------ //

        public static string FivemExe(string fivemApp) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(fivemApp).TrimEnd('\\', '/')) ?? "", "FiveM.exe");

        // Lance Steam s'il n'est pas ouvert (FiveM en a besoin pour GTA V), puis FiveM
        public static void LaunchFivem(string fivemApp, Action<string> log)
        {
            string exe = FivemExe(fivemApp);
            if (!File.Exists(exe)) throw new GfxException("FiveM.exe introuvable à côté de FiveM.app :\n" + exe);
            StartSteam(log);
            log("Lancement de FiveM…");
            // lancé par l'Explorateur, comme un double-clic : FiveM ne dépend pas de ce logiciel
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = false });
            for (int i = 0; i < 40 && !IsFivemRunning(); i++) System.Threading.Thread.Sleep(500);
            if (!IsFivemRunning())
            {
                log("  l'Explorateur n'a pas lancé FiveM, nouvel essai direct…");
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                for (int i = 0; i < 40 && !IsFivemRunning(); i++) System.Threading.Thread.Sleep(500);
            }
            if (!IsFivemRunning()) throw new GfxException("FiveM ne s'est pas lancé :\n" + exe);
            log("FiveM lancé.");
        }

        static string SteamExe()
        {
            foreach (var (root, key, value) in new[]
            {
                (Microsoft.Win32.Registry.CurrentUser, @"Software\Valve\Steam", "SteamExe"),
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
            })
            {
                try
                {
                    using (var k = root.OpenSubKey(key))
                    {
                        string v = k?.GetValue(value) as string;
                        if (string.IsNullOrEmpty(v)) continue;
                        v = v.Replace('/', '\\');
                        if (!v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) v = Path.Combine(v, "steam.exe");
                        if (File.Exists(v)) return v;
                    }
                }
                catch { }
            }
            string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
            return File.Exists(def) ? def : null;
        }

        static bool SteamRunning() => Process.GetProcessesByName("steam").Length > 0;

        // Compte Steam connecté (0 tant que Steam démarre ou n'est pas connecté)
        static int SteamUser()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess"))
                    return k?.GetValue("ActiveUser") is int u ? u : 0;
            }
            catch { return 0; }
        }

        static void StartSteam(Action<string> log)
        {
            if (SteamRunning() && SteamUser() != 0) return;
            if (!SteamRunning())
            {
                string steam = SteamExe();
                if (steam == null)
                {
                    log("  Steam introuvable : FiveM est lancé sans lui (version Rockstar / Epic de GTA V ?).");
                    return;
                }
                log("Lancement de Steam…");
                Process.Start(new ProcessStartInfo(steam) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(steam) });
            }
            log("Attente de la connexion à Steam…");
            // jusqu'à 2 minutes (mise à jour de Steam, mot de passe à taper…)
            for (int i = 0; i < 240 && SteamUser() == 0; i++) System.Threading.Thread.Sleep(500);
            if (SteamUser() == 0)
            {
                log("  Steam n'est pas connecté : FiveM est lancé quand même.");
                return;
            }
            System.Threading.Thread.Sleep(3000); // laisse Steam finir de démarrer
            log("Steam prêt.");
        }

    }
}
