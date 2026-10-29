using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Microsoft.Win32;

namespace FiveMGfxManager
{
    // Profil de jeu : packs / mods installés + preset ReShade + réglages graphiques, remis d'un clic
    [DataContract]
    public class GameProfile
    {
        [DataMember] public string Nom;
        [DataMember] public List<string> Packs = new List<string>(); // ids de la bibliothèque
        [DataMember] public string Preset;                           // fichier dans plugins (null : inchangé)
        [DataMember] public bool? ModePerformance;
        [DataMember] public string Graphismes;                       // copie de gta5_settings.xml (null : inchangés)
        [DataMember] public string Date;
    }

    public static partial class Core
    {
        static string ProfilesDir() => Path.Combine(DataDir(), "profils");
        static string ProfilesPath() => Path.Combine(DataDir(), "profils.json");

        public static List<GameProfile> ListProfiles()
        {
            var list = ReadJson<List<GameProfile>>(ProfilesPath()) ?? new List<GameProfile>();
            foreach (var p in list) p.Packs = p.Packs ?? new List<string>();
            return list;
        }

        static void SaveProfiles(List<GameProfile> list) => WriteJson(ProfilesPath(), list);

        // Enregistre la configuration actuelle sous ce nom (remplace un profil du même nom)
        public static GameProfile SaveCurrentAsProfile(string name, string fivemApp, Action<string> log)
        {
            name = (name ?? "").Trim();
            if (name == "") throw new GfxException("Donne un nom au profil.");
            var list = ListProfiles();
            var old = list.FirstOrDefault(p => p.Nom.Equals(name, StringComparison.OrdinalIgnoreCase));
            var profile = new GameProfile
            {
                Nom = name, Date = Now(),
                Packs = InstalledPacks().Where(i => i.LibId != null).Select(i => i.LibId).ToList(),
                Preset = fivemApp != null ? ActivePresetName(fivemApp) : null,
                ModePerformance = fivemApp != null && File.Exists(ReshadeIni(fivemApp)) ? ReshadePerformanceMode(fivemApp) : (bool?)null,
            };
            if (File.Exists(GraphicsSettingsPath()))
            {
                Directory.CreateDirectory(ProfilesDir());
                string file = old?.Graphismes ?? NewProfileFile();
                CopyFile(GraphicsSettingsPath(), Path.Combine(ProfilesDir(), file));
                profile.Graphismes = file;
            }
            if (old != null) list.Remove(old);
            list.Add(profile);
            SaveProfiles(list);
            log($"Profil « {name} » enregistré : {profile.Packs.Count} pack(s)" +
                (profile.Preset != null ? $", preset {profile.Preset}" : "") + (profile.Graphismes != null ? ", réglages graphiques" : "") + ".");
            return profile;
        }

        static string NewProfileFile()
        {
            for (int i = 1; ; i++)
                if (!File.Exists(Path.Combine(ProfilesDir(), $"graphismes_{i}.xml"))) return $"graphismes_{i}.xml";
        }

        public static void DeleteProfile(string name)
        {
            var list = ListProfiles();
            var p = list.FirstOrDefault(x => x.Nom == name);
            if (p == null) return;
            if (p.Graphismes != null) RemoveFile(Path.Combine(ProfilesDir(), p.Graphismes));
            list.Remove(p);
            SaveProfiles(list);
        }

        public static string DescribeProfile(GameProfile p)
        {
            var lib = ListLibrary();
            var names = p.Packs.Select(id => lib.FirstOrDefault(l => l.Id == id)?.Nom ?? "(supprimé)").ToList();
            var parts = new List<string> { names.Count == 0 ? "aucun pack" : string.Join(", ", names) };
            if (p.Preset != null) parts.Add("preset " + p.Preset);
            if (p.ModePerformance == true) parts.Add("mode performance");
            if (p.Graphismes != null) parts.Add("graphismes");
            return string.Join("  ·  ", parts);
        }

        // Retire les packs absents du profil, installe ceux qui manquent, puis preset et réglages graphiques
        public static void ApplyProfile(string name, string fivemApp, Action<string> log, Action<double> progress)
        {
            var p = ListProfiles().FirstOrDefault(x => x.Nom == name) ?? throw new GfxException($"Profil « {name} » introuvable.");
            log($"Profil « {p.Nom} »");
            var lib = ListLibrary();
            var want = p.Packs.Where(id => lib.Any(l => l.Id == id)).ToList();
            foreach (var id in p.Packs.Except(want)) log($"  ! pack {id} absent de la bibliothèque, ignoré");

            var remove = InstalledPacks().Where(i => i.LibId == null || !want.Contains(i.LibId)).ToList();
            var add = want.Where(id => !IsInstalled(id)).ToList();
            int steps = Math.Max(1, remove.Count + add.Count), done = 0;
            Action<double> sub = x => progress((done + x) / steps);
            foreach (var i in remove) { UninstallPack(i.Id, fivemApp, log, sub); done++; }
            foreach (var id in add) { InstallLibraryPack(id, fivemApp, log, sub); done++; }

            if (p.Preset != null)
            {
                if (File.Exists(P(fivemApp, "plugins/" + p.Preset)) && File.Exists(ReshadeIni(fivemApp))) SetActivePreset(fivemApp, p.Preset, log);
                else log($"  ! preset {p.Preset} introuvable, ignoré");
            }
            if (p.ModePerformance != null && File.Exists(ReshadeIni(fivemApp))) SetReshadePerformanceMode(fivemApp, p.ModePerformance.Value, log);
            if (p.Graphismes != null)
            {
                string file = Path.Combine(ProfilesDir(), p.Graphismes);
                if (File.Exists(file) && File.Exists(GraphicsSettingsPath())) ApplyGraphicsFile(file, log);
            }
            progress(1);
            log($"Profil « {p.Nom} » appliqué.");
        }

        // ------------------------------------------------ poids des mods --

        // Mémoire de la carte graphique (la plus grosse), lue dans le registre ; FIVEM_GFX_VRAM (en Go) pour les tests
        public static long? VideoMemory()
        {
            string env = Environment.GetEnvironmentVariable("FIVEM_GFX_VRAM");
            if (!string.IsNullOrEmpty(env) && double.TryParse(env, System.Globalization.NumberStyles.Float,
                                                              System.Globalization.CultureInfo.InvariantCulture, out var gb))
                return (long)(gb * 1024 * 1024 * 1024);
            try
            {
                long best = 0;
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    foreach (var sub in cls?.GetSubKeyNames() ?? new string[0])
                        try
                        {
                            using (var k = cls.OpenSubKey(sub))
                            {
                                object v = k?.GetValue("HardwareInformation.qwMemorySize");
                                long n = v is long l ? l : v is byte[] b && b.Length >= 8 ? BitConverter.ToInt64(b, 0) : 0;
                                best = Math.Max(best, n);
                            }
                        }
                        catch { }
                return best > 0 ? best : (long?)null;
            }
            catch { return null; }
        }

        public static long InstalledSize(bool modsOnly)
        {
            var lib = ListLibrary();
            return InstalledPacks().Where(i => i.LibId != null && (!modsOnly || i.Type == ModType))
                                   .Sum(i => lib.FirstOrDefault(l => l.Id == i.LibId)?.Taille ?? 0);
        }

        // Avertissement quand les packs et mods installés pèsent plus de la moitié de la mémoire vidéo
        public static string HeavyModsWarning()
        {
            long total = InstalledSize(false);
            long vram = VideoMemory() ?? 8L * 1024 * 1024 * 1024;
            return total > vram / 2
                ? $"⚠ {HumanSize(total)} installés pour {HumanSize(vram)} de VRAM : risque de textures qui chargent mal et de saccades"
                : null;
        }
    }
}
