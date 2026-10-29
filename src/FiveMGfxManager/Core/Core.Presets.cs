using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FiveMGfxManager
{
    public class ReshadePreset
    {
        public string Nom;     // nom du fichier (ex. QuantV.preset.ini)
        public string Chemin;
        public bool Actif;
        public int Effets;     // nombre de techniques activées
    }

    // Presets ReShade de FiveM.app\plugins, mode performance, rangement des shaders inutilisés
    public static partial class Core
    {
        static string ReshadeIni(string fivemApp) => P(fivemApp, "plugins/ReShade.ini");
        static string ShadersDir(string fivemApp) => P(fivemApp, "plugins/reshade-shaders/Shaders");
        public static string RetiredShadersDir() => Path.Combine(DataDir(), "shaders_ranges");

        // ------------------------------------------------- fichier .ini --

        // Valeur de « clé = valeur » (espaces autour du = acceptés, ReShade en écrit parfois), null si autre clé
        static string KeyValue(string line, string key)
        {
            line = line.Trim();
            if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase)) return null;
            string rest = line.Substring(key.Length).TrimStart();
            return rest.StartsWith("=") ? rest.Substring(1).Trim() : null;
        }

        public static string IniGet(string path, string section, string key)
        {
            if (!File.Exists(path)) return null;
            string cur = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) cur = line.Substring(1, line.Length - 2);
                else if (cur.Equals(section, StringComparison.OrdinalIgnoreCase) && KeyValue(line, key) != null)
                    return KeyValue(line, key);
            }
            return null;
        }

        public static void IniSet(string path, string section, string key, string value)
        {
            var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
            int start = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (start < 0)
            {
                if (lines.Count > 0 && lines.Last().Trim() != "") lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
            }
            else
            {
                int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith("["));
                if (end < 0) end = lines.Count;
                int at = lines.FindIndex(start + 1, end - start - 1, l => KeyValue(l, key) != null);
                if (at >= 0) lines[at] = key + "=" + value;
                else
                {
                    // après la dernière ligne non vide de la section
                    int ins = end;
                    while (ins > start + 1 && lines[ins - 1].Trim() == "") ins--;
                    lines.Insert(ins, key + "=" + value);
                }
            }
            if (File.Exists(path)) MakeWritable(path);
            File.WriteAllLines(path, lines);
        }

        // ------------------------------------------------------ presets --

        static bool IsPreset(string file)
        {
            try { return File.ReadLines(file).Take(50).Any(l => KeyValue(l, "Techniques") != null); }
            catch { return false; }
        }

        static string ActivePresetPath(string fivemApp)
        {
            string v = IniGet(ReshadeIni(fivemApp), "GENERAL", "PresetPath");
            if (string.IsNullOrWhiteSpace(v)) return null;
            try { return Path.GetFullPath(Path.IsPathRooted(v) ? v : Path.Combine(P(fivemApp, "plugins"), v)); }
            catch { return null; }
        }

        public static List<ReshadePreset> ListReshadePresets(string fivemApp)
        {
            string dir = P(fivemApp, "plugins");
            if (!Directory.Exists(dir)) return new List<ReshadePreset>();
            string active = ActivePresetPath(fivemApp);
            return Directory.GetFiles(dir, "*.ini")
                .Where(f => !Path.GetFileName(f).Equals("ReShade.ini", StringComparison.OrdinalIgnoreCase) && IsPreset(f))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .Select(f => new ReshadePreset
                {
                    Nom = Path.GetFileName(f), Chemin = f,
                    Actif = string.Equals(Path.GetFullPath(f), active, StringComparison.OrdinalIgnoreCase),
                    Effets = PresetTechniques(f).Count,
                }).ToList();
        }

        public static string ActivePresetName(string fivemApp) => ListReshadePresets(fivemApp).FirstOrDefault(p => p.Actif)?.Nom;

        // Techniques activées : « Nom@Fichier.fx »
        static List<string> PresetTechniques(string file)
        {
            string value = File.ReadLines(file).Select(l => KeyValue(l, "Techniques")).FirstOrDefault(v => v != null);
            return value == null ? new List<string>()
                : value.Split(',').Select(t => t.Trim()).Where(t => t != "").ToList();
        }

        public static void SetActivePreset(string fivemApp, string name, Action<string> log)
        {
            string file = P(fivemApp, "plugins/" + name);
            if (!File.Exists(file)) throw new GfxException("Preset introuvable : " + name);
            if (!File.Exists(ReshadeIni(fivemApp))) throw new GfxException("ReShade.ini introuvable : installe d'abord ReShade.");
            IniSet(ReshadeIni(fivemApp), "GENERAL", "PresetPath", @".\" + name);
            log($"Preset actif : {name}");
        }

        public static string DuplicatePreset(string fivemApp, string name, string newName, Action<string> log)
        {
            newName = newName.Trim();
            if (!newName.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) newName += ".ini";
            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new GfxException("Nom de fichier invalide : " + newName);
            string src = P(fivemApp, "plugins/" + name), dst = P(fivemApp, "plugins/" + newName);
            if (File.Exists(dst)) throw new GfxException($"Le preset « {newName} » existe déjà.");
            File.Copy(src, dst);
            log($"Preset copié : {name} → {newName}");
            return newName;
        }

        // ------------------------------------------- mode performance --

        // Mode performance de ReShade : les shaders sont compilés une fois pour toutes, plus de FPS en jeu
        public static bool ReshadePerformanceMode(string fivemApp) =>
            IniGet(ReshadeIni(fivemApp), "GENERAL", "PerformanceMode") == "1";

        public static void SetReshadePerformanceMode(string fivemApp, bool on, Action<string> log)
        {
            if (!File.Exists(ReshadeIni(fivemApp))) throw new GfxException("ReShade.ini introuvable : installe d'abord ReShade.");
            IniSet(ReshadeIni(fivemApp), "GENERAL", "PerformanceMode", on ? "1" : "0");
            log("Mode performance ReShade " + (on ? "activé." : "désactivé."));
        }

        // --------------------------------------- shaders inutilisés --

        // Sous-dossiers de Shaders dont aucun fichier n'est utilisé par les presets (ni inclus par un shader utilisé)
        public static List<string> UnusedShaderFolders(string fivemApp)
        {
            string root = ShadersDir(fivemApp);
            if (!Directory.Exists(root)) return new List<string>();
            var all = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList();
            var byName = all.GroupBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            // fichiers .fx des techniques de tous les presets, puis leurs #include
            var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var todo = new Queue<string>();
            foreach (var preset in ListReshadePresets(fivemApp))
                foreach (var t in PresetTechniques(preset.Chemin))
                {
                    string fx = t.Contains("@") ? t.Substring(t.IndexOf('@') + 1) : null;
                    if (fx != null && byName.TryGetValue(fx, out var files))
                        foreach (var f in files) if (needed.Add(f)) todo.Enqueue(f);
                }
            var include = new Regex(@"#\s*include\s*[""<]([^"">]+)["">]");
            while (todo.Count > 0)
            {
                string f = todo.Dequeue();
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                foreach (Match m in include.Matches(text))
                    if (byName.TryGetValue(Path.GetFileName(m.Groups[1].Value), out var files))
                        foreach (var x in files) if (needed.Add(x)) todo.Enqueue(x);
            }

            return Directory.GetDirectories(root)
                .Where(d => !needed.Any(n => n.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                .Select(Path.GetFileName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Range les dossiers inutilisés hors de FiveM (rien n'est supprimé) : ReShade compile moins de shaders
        public static int RetireUnusedShaders(string fivemApp, Action<string> log)
        {
            if (ListReshadePresets(fivemApp).Count == 0) throw new GfxException("Aucun preset ReShade trouvé : impossible de savoir quels shaders sont utilisés.");
            var unused = UnusedShaderFolders(fivemApp);
            foreach (var name in unused)
            {
                string dst = Path.Combine(RetiredShadersDir(), name);
                if (Directory.Exists(dst)) RemoveTree(dst);
                MoveTree(Path.Combine(ShadersDir(fivemApp), name), dst);
                log("  rangé : " + name);
            }
            log(unused.Count == 0 ? "Aucun dossier de shaders inutilisé." : $"{unused.Count} dossier(s) de shaders rangé(s) dans {RetiredShadersDir()}");
            return unused.Count;
        }

        public static List<string> RetiredShaderFolders() =>
            Directory.Exists(RetiredShadersDir())
                ? Directory.GetDirectories(RetiredShadersDir()).Select(Path.GetFileName).ToList() : new List<string>();

        public static void RestoreRetiredShaders(string fivemApp, Action<string> log)
        {
            foreach (var name in RetiredShaderFolders())
            {
                string dst = Path.Combine(ShadersDir(fivemApp), name);
                if (Directory.Exists(dst)) RemoveTree(dst);
                MoveTree(Path.Combine(RetiredShadersDir(), name), dst);
                log("  remis : " + name);
            }
        }

        static void MoveTree(string src, string dst)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            try { Directory.Move(src, dst); return; }
            catch (IOException) { } // autre disque
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                CopyFile(f, Path.Combine(dst, f.Substring(src.Length + 1)));
            RemoveTree(src);
        }
    }
}
