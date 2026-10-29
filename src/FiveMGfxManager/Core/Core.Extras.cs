using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace FiveMGfxManager
{
    // Pack installé qui touche les mêmes fichiers qu'un pack de la bibliothèque
    public class PackConflict
    {
        public PackInfo Pack;
        public List<string> Fichiers = new List<string>(); // même chemin dans FiveM : le nouveau pack remplace l'ancien
        public List<string> Internes = new List<string>(); // même fichier du jeu dans deux .rpf différents
    }

    // Effet d'un preset ReShade qu'on peut couper / remettre
    public class PresetEffect
    {
        public string Id;      // t|Technique@fichier.fx, o|section|clé (0/1), n|section|clé (intensité)
        public string Nom;
        public string Groupe;  // Effets (techniques) ou section du preset
        public bool Actif;
    }

    public class UpdateInfo
    {
        public Version Version;
        public string Url;     // .zip de la nouvelle version
        public string Page;
        public string Notes;
    }

    [DataContract]
    public class ExportInfo
    {
        [DataMember] public string Date;
        [DataMember] public string Version;
        [DataMember] public int Packs;
        [DataMember] public int Profils;
    }

    // Aperçu des packs, conflits, effets des presets, mises à jour, export / import
    public static partial class Core
    {
        // ------------------------------------------------------- aperçu --

        static readonly string[] PreviewExts = { ".jpg", ".jpeg", ".png", ".bmp" };

        static bool IsPreviewImage(string file) => PreviewExts.Contains(Path.GetExtension(file).ToLowerInvariant());

        public static string PackPreview(string id)
        {
            string dir = Path.Combine(LibraryDir(), id);
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "apercu.*").FirstOrDefault(IsPreviewImage) : null;
        }

        public static void SetPackPreview(string id, string image)
        {
            GetLibraryPack(id);
            if (!IsPreviewImage(image)) throw new GfxException("Image non prise en charge : choisis un .jpg, .png ou .bmp.");
            if (!File.Exists(image)) throw new GfxException("Image introuvable : " + image);
            string dir = Path.Combine(LibraryDir(), id), tmp = Path.Combine(dir, "apercu.tmp");
            CopyFile(image, tmp);
            RemovePackPreview(id);
            File.Move(tmp, Path.Combine(dir, "apercu" + Path.GetExtension(image).ToLowerInvariant()));
        }

        public static void RemovePackPreview(string id)
        {
            string old;
            while ((old = PackPreview(id)) != null) RemoveFile(old);
        }

        // La plus grande image fournie avec le pack (les petites icônes sont ignorées)
        static string FindPreviewImage(string folder) =>
            SafeFiles(folder).Where(IsPreviewImage).Select(f => new FileInfo(f))
                             .Where(f => f.Length >= 20 * 1024).OrderByDescending(f => f.Length)
                             .Select(f => f.FullName).FirstOrDefault();

        // ----------------------------------------------------- conflits --

        // Fichiers internes des .rpf d'un pack, rangés par chemin dans le jeu
        static Dictionary<string, string> RpfGameFiles(string content)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(content)) return map;
            foreach (var rel in ListFiles(content).Where(r => r.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
            {
                // un .rpf à la racine de mods contient des chemins du jeu ; ailleurs il remplace le .rpf du même chemin
                string inMods = rel.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ? rel.Substring(5) : rel;
                string prefix = inMods.Contains("/") ? inMods.ToLowerInvariant() + "/" : "";
                foreach (var e in RpfEntries(P(content, rel)))
                {
                    string name = Path.GetFileName(e).ToLowerInvariant();
                    if (name == "content.xml" || name == "setup2.xml" || name == "_manifest.ymf" || name.EndsWith(".rpf")) continue;
                    map[prefix + e] = rel;
                }
            }
            return map;
        }

        public static List<PackConflict> FindConflicts(string libId)
        {
            string content = LibraryContent(libId);
            var mine = new HashSet<string>(ListFiles(content), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> myRpf = null;
            var list = new List<PackConflict>();
            foreach (var i in InstalledPacks().Where(i => i.LibId != libId))
            {
                var c = new PackConflict { Pack = i };
                var m = ReadJson<Manifest>(ManifestPath(i.Id));
                if (m != null) c.Fichiers = Touched(m).Where(mine.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (i.LibId != null && Directory.Exists(LibraryContent(i.LibId)))
                {
                    myRpf = myRpf ?? RpfGameFiles(content);
                    var same = new HashSet<string>(c.Fichiers, StringComparer.OrdinalIgnoreCase);
                    c.Internes = RpfGameFiles(LibraryContent(i.LibId))
                        .Where(kv => myRpf.TryGetValue(kv.Key, out var mineRel) && !same.Contains(mineRel) && !same.Contains(kv.Value))
                        .Select(kv => kv.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                }
                if (c.Fichiers.Count > 0 || c.Internes.Count > 0) list.Add(c);
            }
            return list;
        }

        // Texte pour l'utilisateur : qui gagne pour chaque conflit
        public static string DescribeConflicts(string packName, List<PackConflict> conflicts)
        {
            var sb = new StringBuilder();
            foreach (var c in conflicts)
            {
                if (c.Fichiers.Count > 0)
                    sb.AppendLine($"• « {c.Pack.Nom} » : {c.Fichiers.Count} fichier(s) au même endroit ({Sample(c.Fichiers)}).\n" +
                                  $"   → « {packName} » gagne : « {c.Pack.Nom} » sera retiré.");
                if (c.Internes.Count > 0)
                    sb.AppendLine($"• « {c.Pack.Nom} » : {c.Internes.Count} fichier(s) du jeu modifiés par les deux, dans des .rpf différents ({Sample(c.Internes)}).\n" +
                                  "   → FiveM n'en garde qu'une version, sans ordre garanti : si un effet manque en jeu, retire l'un des deux.");
            }
            return sb.ToString().TrimEnd();
        }

        static string Sample(List<string> files) =>
            string.Join(", ", files.Take(3).Select(f => Path.GetFileName(f.TrimEnd('/')))) + (files.Count > 3 ? "…" : "");

        // ------------------------------------------- effets d'un preset --

        static string EffectValuesPath() => Path.Combine(DataDir(), "effets_valeurs.json");

        // clés d'intensité qu'on peut mettre à 0 (grain, flou, aberration…)
        static readonly Regex StrengthKey = new Regex(@"(grain|noise|dof|blur|vignette|chromatic|aberration|bloom|sharpen)", RegexOptions.IgnoreCase);

        static string PresetFile(string fivemApp, string preset)
        {
            string file = P(fivemApp, "plugins/" + preset);
            if (!File.Exists(file)) throw new GfxException("Preset introuvable : " + preset);
            return file;
        }

        static string Pretty(string key) => Regex.Replace(key.Replace('_', ' '), @"\s+", " ").Trim();

        public static List<PresetEffect> ListPresetEffects(string fivemApp, string preset)
        {
            string file = PresetFile(fivemApp, preset);
            var lines = File.ReadAllLines(file);
            var list = new List<PresetEffect>();

            // techniques : toutes celles connues (TechniqueSorting), cochées si dans Techniques
            var on = new HashSet<string>(PresetTechniques(file), StringComparer.OrdinalIgnoreCase);
            string sorting = lines.Select(l => KeyValue(l, "TechniqueSorting")).FirstOrDefault(v => v != null) ?? "";
            var all = on.Concat(sorting.Split(',').Select(t => t.Trim()).Where(t => t != ""))
                        .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var t in all)
                list.Add(new PresetEffect
                {
                    Id = "t|" + t, Groupe = "Effets", Actif = on.Contains(t),
                    Nom = (t.Contains("@") ? t.Substring(0, t.IndexOf('@')) : t) + (t.Contains("@") ? "  (" + t.Substring(t.IndexOf('@') + 1) + ")" : ""),
                });

            // options 0/1 et intensités de chaque section
            var saved = ReadJson<Dictionary<string, string>>(EffectValuesPath()) ?? new Dictionary<string, string>();
            string section = null;
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2); continue; }
                int eq = line.IndexOf('=');
                if (section == null || eq <= 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                bool disable = key.StartsWith("Disable_", StringComparison.OrdinalIgnoreCase);
                if (value == "0" || value == "1")
                    list.Add(new PresetEffect
                    {
                        Id = "o|" + section + "|" + key, Groupe = section,
                        Nom = disable ? Pretty(key.Substring(8)) : Pretty(key),
                        Actif = disable ? value == "0" : value == "1",
                    });
                else if (StrengthKey.IsMatch(key) && !value.Contains(",") &&
                         double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) &&
                         (v != 0 || saved.ContainsKey(preset + "|" + section + "|" + key)))
                    list.Add(new PresetEffect
                    {
                        Id = "n|" + section + "|" + key, Groupe = section, Nom = Pretty(key) + " (intensité " + value + ")",
                        Actif = v != 0,
                    });
            }
            return list;
        }

        public static void SetPresetEffect(string fivemApp, string preset, string effectId, bool on, Action<string> log)
        {
            string file = PresetFile(fivemApp, preset);
            var e = ListPresetEffects(fivemApp, preset).FirstOrDefault(x => x.Id == effectId)
                ?? throw new GfxException("Effet introuvable dans le preset : " + effectId);
            if (e.Actif == on) return;
            var parts = effectId.Split('|');
            RecordFile($"Preset {preset} : {e.Nom.Split('(')[0].Trim()} " + (on ? "activé" : "coupé"), file);

            if (parts[0] == "t")
            {
                var lines = File.ReadAllLines(file).ToList();
                int at = lines.FindIndex(l => KeyValue(l, "Techniques") != null);
                var order = ListPresetEffects(fivemApp, preset).Where(x => x.Id.StartsWith("t|")).Select(x => x.Id.Substring(2)).ToList();
                var active = PresetTechniques(file);
                if (on) active.Add(parts[1]); else active.RemoveAll(t => t.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
                string value = string.Join(",", order.Where(t => active.Contains(t, StringComparer.OrdinalIgnoreCase)));
                string newLine = (at >= 0 && lines[at].Contains(" =") ? "Techniques = " : "Techniques=") + value;
                if (at >= 0) lines[at] = newLine; else lines.Insert(0, newLine);
                MakeWritable(file);
                File.WriteAllLines(file, lines);
            }
            else if (parts[0] == "o")
            {
                bool disable = parts[2].StartsWith("Disable_", StringComparison.OrdinalIgnoreCase);
                IniSet(file, parts[1], parts[2], (on ^ disable) ? "1" : "0");
            }
            else
            {
                // l'intensité d'origine est gardée pour la remettre
                var saved = ReadJson<Dictionary<string, string>>(EffectValuesPath()) ?? new Dictionary<string, string>();
                string k = preset + "|" + parts[1] + "|" + parts[2];
                if (!on)
                {
                    saved[k] = IniGet(file, parts[1], parts[2]);
                    IniSet(file, parts[1], parts[2], "0");
                }
                else
                {
                    IniSet(file, parts[1], parts[2], saved.TryGetValue(k, out var old) && old != null ? old : "1");
                    saved.Remove(k);
                }
                WriteJson(EffectValuesPath(), saved);
            }
            log($"{preset} : {e.Nom.Split('(')[0].Trim()} " + (on ? "activé." : "coupé."));
        }

        // Intensités coupées par le logiciel (affichées comme coupées et remises à leur valeur d'origine)
        public static bool EffectWasCut(string preset, string effectId)
        {
            var parts = effectId.Split('|');
            if (parts[0] != "n") return false;
            var saved = ReadJson<Dictionary<string, string>>(EffectValuesPath()) ?? new Dictionary<string, string>();
            return saved.ContainsKey(preset + "|" + parts[1] + "|" + parts[2]);
        }

        // -------------------------------------------- mises à jour --

        const string ReleasesApi = "https://api.github.com/repos/poiscail/FiveM-GFX-Manager/releases/latest";

        public static Version CurrentVersion() => typeof(Core).Assembly.GetName().Version;

        // Dernière version publiée sur GitHub, null si pas plus récente (ou pas de connexion)
        public static UpdateInfo CheckForUpdate()
        {
            string url = Environment.GetEnvironmentVariable("FIVEM_GFX_UPDATE_URL") ?? ReleasesApi;
            string json;
            string tmp = Path.Combine(Path.GetTempPath(), "fivem_gfx_update_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            try
            {
                Download(url, tmp, s => { }, p => { });
                json = File.ReadAllText(tmp);
            }
            catch { return null; }
            finally { try { File.Delete(tmp); } catch { } }

            var tag = Regex.Match(json, @"""tag_name""\s*:\s*""v?([0-9][0-9.]*)""");
            if (!tag.Success || !Version.TryParse(tag.Groups[1].Value, out var latest)) return null;
            var cur = CurrentVersion();
            if (Normalize(latest) <= Normalize(cur)) return null;
            var zip = Regex.Matches(json, @"""browser_download_url""\s*:\s*""([^""]+\.zip)""").Cast<Match>()
                           .Select(m => m.Groups[1].Value).FirstOrDefault();
            var page = Regex.Match(json, @"""html_url""\s*:\s*""([^""]+/releases/[^""]+)""");
            var body = Regex.Match(json, @"""body""\s*:\s*""((?:[^""\\]|\\.)*)""");
            return new UpdateInfo
            {
                Version = latest, Url = zip, Page = page.Success ? page.Groups[1].Value : null,
                Notes = body.Success ? Regex.Unescape(body.Groups[1].Value) : "",
            };
        }

        static Version Normalize(Version v) => new Version(v.Major, v.Minor, Math.Max(0, v.Build));

        // Télécharge la nouvelle version et prépare un script qui remplace l'exe une fois le logiciel fermé.
        // Renvoie le script à lancer juste avant de quitter.
        public static string PrepareUpdate(UpdateInfo info, string exePath, int pid, Action<string> log, Action<double> progress)
        {
            if (string.IsNullOrEmpty(info.Url)) throw new GfxException("Pas de fichier .zip dans cette version : télécharge-la depuis la page GitHub.");
            string work = NewTempDir("fivem_gfx_maj_");
            string zip = Path.Combine(work, "maj.zip");
            Download(info.Url, zip, log, p => progress(p * 0.8));
            string folder = OpenSource(zip, Path.Combine(work, "x"), log, p => progress(0.8 + p * 0.2));
            string newExe = SafeFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(Path.GetFileName(exePath), StringComparison.OrdinalIgnoreCase))
                ?? SafeFiles(folder).FirstOrDefault(f => f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                ?? throw new GfxException("Le .zip de la mise à jour ne contient pas de .exe.");
            string dir = Path.GetDirectoryName(exePath);
            var bat = new StringBuilder();
            bat.AppendLine("@echo off");
            bat.AppendLine("chcp 65001 >nul");
            bat.AppendLine(":attente");
            bat.AppendLine($"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto attente)");
            bat.AppendLine($"copy /y \"{newExe}\" \"{exePath}\" >nul");
            foreach (var extra in Directory.GetFiles(Path.GetDirectoryName(newExe)).Where(f => f != newExe &&
                         (f.EndsWith(".config", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))))
            {
                // l'ancien .exe.config porte le nom de l'exe
                string name = ConfigName(extra, newExe, exePath);
                bat.AppendLine($"copy /y \"{extra}\" \"{Path.Combine(dir, name)}\" >nul");
            }
            bat.AppendLine($"start \"\" \"{exePath}\"");
            bat.AppendLine($"rmdir /s /q \"{work}\"");
            string script = Path.Combine(Path.GetTempPath(), "fivem_gfx_maj_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bat");
            File.WriteAllText(script, bat.ToString(), new UTF8Encoding(false));
            progress(1);
            log($"Version {info.Version} téléchargée : le logiciel va redémarrer.");
            return script;
        }

        static string ConfigName(string file, string newExe, string exePath)
        {
            string n = Path.GetFileName(file);
            string newBase = Path.GetFileName(newExe);
            return n.StartsWith(newBase, StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(exePath) + n.Substring(newBase.Length) : n;
        }

        // --------------------------------------- export / import --

        public const string ExportFolderName = "FiveM-GFX-Manager-sauvegarde";

        // ce qui suffit pour tout réinstaller sur un autre PC
        static readonly string[] ExportItems = { "bibliotheque", "profils", "profils.json", "effets_valeurs.json" };

        // Copie la bibliothèque et les profils dans dest\FiveM-GFX-Manager-sauvegarde (clé USB, OneDrive…).
        // Les fichiers déjà à jour ne sont pas recopiés.
        public static string ExportData(string dest, Action<string> log, Action<double> progress)
        {
            if (!Directory.Exists(dest)) throw new GfxException("Dossier introuvable : " + dest);
            string target = Path.GetFileName(dest.TrimEnd('\\', '/')).Equals(ExportFolderName, StringComparison.OrdinalIgnoreCase)
                ? dest : Path.Combine(dest, ExportFolderName);
            var files = new List<(string src, string dst)>();
            foreach (var item in ExportItems)
            {
                string src = Path.Combine(DataDir(), item);
                if (File.Exists(src)) files.Add((src, Path.Combine(target, item)));
                else if (Directory.Exists(src))
                    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
                        files.Add((f, Path.Combine(target, item, RelPath(src, f).Replace('/', Path.DirectorySeparatorChar))));
            }
            long total = Math.Max(1, files.Sum(f => new FileInfo(f.src).Length)), done = 0;
            int copied = 0;
            foreach (var (src, dst) in files)
            {
                var a = new FileInfo(src);
                var b = new FileInfo(dst);
                if (!b.Exists || b.Length != a.Length || b.LastWriteTimeUtc < a.LastWriteTimeUtc)
                {
                    CopyFile(src, dst);
                    copied++;
                }
                done += a.Length;
                progress((double)done / total);
            }
            // packs supprimés de la bibliothèque depuis le dernier export
            string libOut = Path.Combine(target, "bibliotheque");
            if (Directory.Exists(libOut))
                foreach (var d in Directory.GetDirectories(libOut))
                    if (!Directory.Exists(Path.Combine(LibraryDir(), Path.GetFileName(d)))) RemoveTree(d);
            var info = new ExportInfo
            {
                Date = Now(), Version = CurrentVersion().ToString(3), Packs = ListLibrary().Count, Profils = ListProfiles().Count,
            };
            WriteJson(Path.Combine(target, "sauvegarde.json"), info);
            progress(1);
            log($"Sauvegarde dans {target} : {info.Packs} pack(s), {info.Profils} profil(s), {copied} fichier(s) copiés ({HumanSize(total)}).");
            return target;
        }

        public static ExportInfo ReadExportInfo(string folder)
        {
            var info = ReadJson<ExportInfo>(Path.Combine(folder, "sauvegarde.json"));
            if (info == null && Directory.Exists(Path.Combine(folder, ExportFolderName)))
                info = ReadJson<ExportInfo>(Path.Combine(folder, ExportFolderName, "sauvegarde.json"));
            return info;
        }

        // Ajoute les packs et profils d'une sauvegarde (rien n'est installé dans FiveM : il suffit ensuite d'appliquer un profil)
        public static void ImportData(string folder, Action<string> log, Action<double> progress)
        {
            if (!File.Exists(Path.Combine(folder, "sauvegarde.json")) && Directory.Exists(Path.Combine(folder, ExportFolderName)))
                folder = Path.Combine(folder, ExportFolderName);
            if (!File.Exists(Path.Combine(folder, "sauvegarde.json")))
                throw new GfxException($"Ce dossier n'est pas une sauvegarde de FiveM GFX Manager (choisis le dossier « {ExportFolderName} »).");

            string libIn = Path.Combine(folder, "bibliotheque");
            var packs = Directory.Exists(libIn) ? Directory.GetDirectories(libIn) : new string[0];
            var sigs = ListLibrary().Select(o => ContentSignature(LibraryContent(o.Id))).ToList();
            int added = 0, skipped = 0;
            for (int i = 0; i < packs.Length; i++)
            {
                string id = Path.GetFileName(packs[i]);
                var p = ReadJson<LibraryPack>(Path.Combine(packs[i], "pack.json"));
                string content = Path.Combine(packs[i], "contenu");
                if (p == null || !Directory.Exists(content)) continue;
                if (Directory.Exists(Path.Combine(LibraryDir(), id)) || sigs.Contains(ContentSignature(content)))
                {
                    skipped++;
                }
                else
                {
                    foreach (var f in Directory.EnumerateFiles(packs[i], "*", SearchOption.AllDirectories))
                        CopyFile(f, Path.Combine(LibraryDir(), id, RelPath(packs[i], f).Replace('/', Path.DirectorySeparatorChar)));
                    log("  pack ajouté : " + p.Nom);
                    added++;
                }
                progress((i + 1.0) / Math.Max(1, packs.Length) * 0.9);
            }

            // profils : ceux du même nom sont remplacés par ceux de la sauvegarde
            var imported = ReadJson<List<GameProfile>>(Path.Combine(folder, "profils.json")) ?? new List<GameProfile>();
            if (imported.Count > 0)
            {
                var mine = ListProfiles();
                foreach (var p in imported)
                {
                    mine.RemoveAll(x => x.Nom == p.Nom);
                    mine.Add(p);
                    if (p.Graphismes != null && File.Exists(Path.Combine(folder, "profils", p.Graphismes)))
                        CopyFile(Path.Combine(folder, "profils", p.Graphismes), Path.Combine(ProfilesDir(), p.Graphismes));
                }
                SaveProfiles(mine);
            }
            string values = Path.Combine(folder, "effets_valeurs.json");
            if (File.Exists(values) && !File.Exists(EffectValuesPath())) CopyFile(values, EffectValuesPath());
            progress(1);
            log($"Sauvegarde importée : {added} pack(s) ajouté(s)" + (skipped > 0 ? $", {skipped} déjà présent(s)" : "") +
                $", {imported.Count} profil(s).");
        }
    }
}
