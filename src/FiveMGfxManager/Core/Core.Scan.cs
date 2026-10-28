// Recherche des sons d'armes déjà présents (avant d'installer un pack son)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FiveMGfxManager
{
    // Son d'arme trouvé ailleurs que là où le pack va l'écrire
    public class SoundHit
    {
        public string Chemin;   // fichier sur le disque
        public string Dedans;   // chemin dans le .rpf (null : c'est le fichier lui-même)
        public string Rel;      // chemin dans FiveM.app (null : hors de FiveM.app)
        public string Pack;     // pack installé à qui appartient le fichier (null : aucun)
        // fichier seul dans FiveM.app\mods, à personne : on peut le mettre de côté
        public bool Deplacable => Dedans == null && Pack == null && Rel != null &&
                                  Rel.StartsWith("mods/", StringComparison.OrdinalIgnoreCase);
        public override string ToString() =>
            (Rel != null ? Rel.Replace('/', '\\') : Chemin) + (Dedans != null ? " → " + Dedans.Replace('/', '\\') : "") +
            (Pack != null ? $" (pack « {Pack} »)" : "");
    }

    public static partial class Core
    {
        // ------------------------------------------------------------------ //
        //  Sons d'armes déjà présents dans FiveM.app et GTA V\mods
        // ------------------------------------------------------------------ //

        // dossiers de FiveM.app sans mods (cache, journaux…)
        static readonly string[] ScanSkip = { "data", "logs", "crashes", "cache", "server-cache", "server-cache-priv" };

        static readonly Regex WeaponSound = new Regex(@"weapon|gun|_shot|firearm", RegexOptions.IgnoreCase);
        static readonly Regex AudioExt = new Regex(@"\.(awc|rel|dat\d+(\.rel)?|nametable)$", RegexOptions.IgnoreCase);

        // Cherche dans tous les dossiers et sous-dossiers de FiveM.app (et dans les .rpf non chiffrés),
        // puis dans GTA V\mods, les sons d'armes qui pourraient passer devant ceux du pack.
        // Vide si le pack ne contient pas de sons.
        public static List<SoundHit> FindWeaponSounds(string libId, string fivemApp)
        {
            var hits = new List<SoundHit>();
            var files = ListFiles(LibraryContent(libId));
            var audio = files.Where(IsAudioPath).ToList();
            if (audio.Count == 0) return hits;

            var names = new HashSet<string>(audio.Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
            var mine = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in InstalledPacks().Where(i => i.LibId != libId))
            {
                var m = ReadJson<Manifest>(ManifestPath(i.Id));
                if (m != null) foreach (var f in Touched(m)) owner[f] = i.Nom;
            }

            bool Match(string path) =>
                names.Contains(Path.GetFileName(path)) ||
                (IsAudioPath(path) && WeaponSound.IsMatch(Path.GetFileName(path)) &&
                 (AudioExt.IsMatch(path) || path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)));

            void Scan(string root, string fivem)
            {
                foreach (var f in SafeFiles(root))
                {
                    string rel = fivem == null ? null : RelPath(fivem, f);
                    if (rel != null && mine.Contains(rel)) continue; // remplacé normalement par le pack
                    string pack = rel != null && owner.TryGetValue(rel, out var n) ? n : null;
                    string shown = rel ?? f;
                    if (Match(shown))
                        hits.Add(new SoundHit { Chemin = f, Rel = rel, Pack = pack });
                    else if (f.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                        foreach (var e in RpfEntries(f).Where(Match))
                            hits.Add(new SoundHit { Chemin = f, Rel = rel, Pack = pack, Dedans = e });
                }
            }

            if (Directory.Exists(fivemApp))
                foreach (var d in Directory.GetDirectories(fivemApp))
                    if (!ScanSkip.Contains(Path.GetFileName(d).ToLowerInvariant())) Scan(d, fivemApp);
            string gta = GtaPath();
            if (gta != null && Directory.Exists(Path.Combine(gta, "mods"))) Scan(Path.Combine(gta, "mods"), null);
            return hits;
        }

        static bool IsAudioPath(string rel) =>
            rel.Replace('\\', '/').Split('/').Any(p => p.Equals("audio", StringComparison.OrdinalIgnoreCase) ||
                                                       p.Equals("sfx", StringComparison.OrdinalIgnoreCase));

        // fichiers d'un dossier, sans s'arrêter sur un sous-dossier illisible
        static IEnumerable<string> SafeFiles(string dir)
        {
            string[] here, subs;
            try { here = Directory.GetFiles(dir); subs = Directory.GetDirectories(dir); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { yield break; }
            foreach (var f in here) yield return f;
            foreach (var s in subs)
                foreach (var f in SafeFiles(s)) yield return f;
        }

        // ------------------------------------------------------------------ //
        //  Lecture des .rpf (RPF7) non chiffrés, .rpf imbriqués compris
        // ------------------------------------------------------------------ //

        const uint RpfMagic = 0x52504637, RpfOpen = 0x4E45504F;

        // Chemins des fichiers d'un .rpf (vide s'il est chiffré ou illisible)
        public static List<string> RpfEntries(string file)
        {
            var list = new List<string>();
            try
            {
                using (var s = File.OpenRead(file))
                    ReadRpf(s, 0, "", 0, list);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is ArgumentException || e is EndOfStreamException) { }
            return list;
        }

        static void ReadRpf(Stream s, long start, string prefix, int depth, List<string> list)
        {
            var br = new BinaryReader(s);
            s.Position = start;
            uint magic = br.ReadUInt32(), count = br.ReadUInt32(), namesLen = br.ReadUInt32(), enc = br.ReadUInt32();
            if (magic != RpfMagic || (enc != RpfOpen && enc != 0) || count == 0 || count > 500000) return;
            byte[] ent = br.ReadBytes((int)count * 16), names = br.ReadBytes((int)namesLen);
            if (ent.Length < count * 16) return;

            string Name(int i)
            {
                int o = BitConverter.ToUInt16(ent, i * 16);
                if (o >= names.Length) return "";
                int e = Array.IndexOf(names, (byte)0, o);
                return Encoding.ASCII.GetString(names, o, (e < 0 ? names.Length : e) - o);
            }
            void Walk(int i, string path, int level)
            {
                if (i < 0 || i >= count || level > 64) return;
                if (BitConverter.ToUInt32(ent, i * 16 + 4) == 0x7FFFFF00) // dossier
                {
                    int first = BitConverter.ToInt32(ent, i * 16 + 8), n = BitConverter.ToInt32(ent, i * 16 + 12);
                    string sub = i == 0 ? path : path + Name(i) + "/";
                    for (int k = first; k < first + n && k < count; k++)
                        if (k != i) Walk(k, sub, level + 1);
                    return;
                }
                ulong q = BitConverter.ToUInt64(ent, i * 16);
                long size = (long)((q >> 16) & 0xFFFFFF), off = (long)((q >> 40) & 0x7FFFFF);
                string full = path + Name(i);
                list.Add(prefix + full);
                // .rpf imbriqué non compressé : son en-tête est à l'intérieur
                if (size == 0 && depth < 3 && full.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) &&
                    start + off * 512 + 16 < s.Length)
                    ReadRpf(s, start + off * 512, prefix + full + "/", depth + 1, list);
            }
            Walk(0, "", 0);
        }
    }
}
