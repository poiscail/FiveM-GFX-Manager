using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FiveMGfxManager
{
    // Réglages graphiques du jeu (%APPDATA%\CitizenFX\gta5_settings.xml, lu par FiveM à la place du settings.xml de GTA V)
    public static partial class Core
    {
        public static readonly string[] GraphicsProfiles = { "Performance", "Équilibré", "Qualité" };

        // valeurs communes aux trois profils
        static readonly Dictionary<string, string> GraphicsCommon = new Dictionary<string, string>
        {
            ["ReflectionMSAA"] = "0", ["MSAA"] = "0", ["TXAA_Enabled"] = "false", ["FXAA_Enabled"] = "true",
            ["AnisotropicFiltering"] = "16", ["DoF"] = "false", ["MotionBlurStrength"] = "0.000000", ["MaxLodScale"] = "0.000000",
            ["HdStreamingInFlight"] = "false", ["VSync"] = "0", ["PedLodBias"] = "0.000000", ["VehicleLodBias"] = "0.000000",
        };

        static readonly Dictionary<string, Dictionary<string, string>> GraphicsValues = new Dictionary<string, Dictionary<string, string>>
        {
            ["Performance"] = new Dictionary<string, string>
            {
                ["Tessellation"] = "0", ["LodScale"] = "0.700000", ["ShadowQuality"] = "1", ["ReflectionQuality"] = "0",
                ["SSAO"] = "0", ["TextureQuality"] = "1", ["ParticleQuality"] = "0", ["WaterQuality"] = "0",
                ["GrassQuality"] = "0", ["ShaderQuality"] = "1", ["Shadow_SoftShadows"] = "0", ["Shadow_Distance"] = "0.500000",
                ["Shadow_LongShadows"] = "false", ["Shader_SSA"] = "false", ["CityDensity"] = "0.500000",
                ["PedVarietyMultiplier"] = "0.500000", ["VehicleVarietyMultiplier"] = "0.500000", ["PostFX"] = "1",
            },
            ["Équilibré"] = new Dictionary<string, string>
            {
                ["Tessellation"] = "2", ["LodScale"] = "1.000000", ["ShadowQuality"] = "2", ["ReflectionQuality"] = "1",
                ["SSAO"] = "2", ["TextureQuality"] = "2", ["ParticleQuality"] = "1", ["WaterQuality"] = "2",
                ["GrassQuality"] = "1", ["ShaderQuality"] = "2", ["Shadow_SoftShadows"] = "2", ["Shadow_Distance"] = "1.000000",
                ["Shadow_LongShadows"] = "false", ["Shader_SSA"] = "true", ["CityDensity"] = "0.800000",
                ["PedVarietyMultiplier"] = "0.800000", ["VehicleVarietyMultiplier"] = "0.800000", ["PostFX"] = "3",
            },
            ["Qualité"] = new Dictionary<string, string>
            {
                ["Tessellation"] = "3", ["LodScale"] = "1.000000", ["ShadowQuality"] = "3", ["ReflectionQuality"] = "2",
                ["SSAO"] = "2", ["TextureQuality"] = "2", ["ParticleQuality"] = "2", ["WaterQuality"] = "2",
                ["GrassQuality"] = "2", ["ShaderQuality"] = "2", ["Shadow_SoftShadows"] = "4", ["Shadow_Distance"] = "1.500000",
                ["Shadow_LongShadows"] = "true", ["Shader_SSA"] = "true", ["CityDensity"] = "1.000000",
                ["PedVarietyMultiplier"] = "1.000000", ["VehicleVarietyMultiplier"] = "1.000000", ["PostFX"] = "3",
            },
        };

        public static string GraphicsSettingsPath()
        {
            string path = Environment.GetEnvironmentVariable("FIVEM_GFX_SETTINGS");
            return string.IsNullOrEmpty(path)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "gta5_settings.xml")
                : path;
        }

        static string GraphicsDir() => Path.Combine(DataDir(), "graphismes");
        static string GraphicsOriginal() => Path.Combine(GraphicsDir(), "origine.xml");

        public static bool HasGraphicsBackup() => File.Exists(GraphicsOriginal());

        static Dictionary<string, string> ProfileValues(string profile)
        {
            if (!GraphicsValues.TryGetValue(profile, out var values)) throw new GfxException($"Profil graphique inconnu : {profile}");
            var all = new Dictionary<string, string>(GraphicsCommon);
            foreach (var kv in values) all[kv.Key] = kv.Value;
            return all;
        }

        static string ReadSettings()
        {
            string path = GraphicsSettingsPath();
            if (!File.Exists(path))
                throw new GfxException("Fichier des réglages introuvable :\n" + path + "\n\nLance FiveM une fois pour qu'il le crée.");
            return File.ReadAllText(path);
        }

        static string SettingValue(string xml, string key)
        {
            var m = Regex.Match(xml, "<" + Regex.Escape(key) + @"\s+value=""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        static bool SameValue(string a, string b) =>
            a == b || (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                       double.TryParse(b, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) &&
                       Math.Abs(x - y) < 1e-4);

        // Profil dont toutes les valeurs correspondent aux réglages actuels (null : réglages personnalisés ou fichier absent)
        public static string CurrentGraphicsProfile()
        {
            if (!File.Exists(GraphicsSettingsPath())) return null;
            string xml = File.ReadAllText(GraphicsSettingsPath());
            return GraphicsProfiles.FirstOrDefault(p => ProfileValues(p).All(kv =>
            {
                string v = SettingValue(xml, kv.Key);
                return v == null || SameValue(v, kv.Value);
            }));
        }

        // La première fois, les réglages du joueur sont gardés dans graphismes\origine.xml
        static void BackupGraphics()
        {
            Directory.CreateDirectory(GraphicsDir());
            if (!HasGraphicsBackup()) CopyFile(GraphicsSettingsPath(), GraphicsOriginal());
            CopyFile(GraphicsSettingsPath(), Path.Combine(GraphicsDir(), "avant_dernier_changement.xml"));
        }

        public static void ApplyGraphicsProfile(string profile, Action<string> log)
        {
            var values = ProfileValues(profile);
            string xml = ReadSettings();
            BackupGraphics();
            int changed = 0;
            foreach (var kv in values)
            {
                var re = new Regex("(<" + Regex.Escape(kv.Key) + @"\s+value="")[^""]*("")");
                if (!re.IsMatch(xml)) continue;
                xml = re.Replace(xml, "${1}" + kv.Value + "${2}", 1);
                changed++;
            }
            // sans SMC_USER, le jeu remet ses réglages automatiques au démarrage
            xml = Regex.Replace(xml, @"<configSource>[^<]*</configSource>", "<configSource>SMC_USER</configSource>");
            RecordFile($"Graphismes : profil « {profile} »", GraphicsSettingsPath());
            WriteSettings(xml);
            log($"Profil graphique « {profile} » appliqué ({changed} réglages).");
        }

        // Remplace les réglages par un fichier complet (profil de jeu enregistré)
        public static void ApplyGraphicsFile(string file, Action<string> log)
        {
            ReadSettings();
            BackupGraphics();
            RecordFile("Graphismes : réglages d'un profil", GraphicsSettingsPath());
            WriteSettings(File.ReadAllText(file));
            log("Réglages graphiques du profil remis.");
        }

        public static void RestoreGraphics(Action<string> log)
        {
            if (!HasGraphicsBackup()) throw new GfxException("Aucune sauvegarde des réglages graphiques.");
            RecordFile("Graphismes : réglages d'origine remis", GraphicsSettingsPath());
            WriteSettings(File.ReadAllText(GraphicsOriginal()));
            log("Réglages graphiques d'origine remis.");
        }

        static void WriteSettings(string xml)
        {
            string path = GraphicsSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) MakeWritable(path);
            File.WriteAllText(path, xml);
        }
    }
}
