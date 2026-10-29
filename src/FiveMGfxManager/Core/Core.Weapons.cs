// Armes remplacées par les packs
//
// Chaque paquet FiveM (mods\*.rpf avec assembly.xml) remplace des fichiers du jeu (.ydr, .ytd…) par leur nom.
// Deux packs installés qui remplacent la même arme : FiveM n'en garde qu'un, au hasard de l'ordre de chargement.
// On retient donc les fichiers de chaque pack, et l'utilisateur choisit quelle version garder : le fichier est
// retiré du paquet de l'autre pack (et lui revient quand le pack choisi est retiré).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace FiveMGfxManager
{
    // Arme déjà remplacée par un pack installé
    public class WeaponClash
    {
        public string Arme;                                  // ex. w_pi_pistol
        public List<string> Fichiers = new List<string>();   // ex. w_pi_pistol.ydr, w_pi_pistol.ytd
        public PackInfo Pack;                                // pack installé qui la remplace
    }

    public static partial class Core
    {
        const string OwnAuthor = "<displayName>FiveM GFX Manager</displayName>";

        // Paquet à la racine de mods (seuls ceux-là sont chargés par FiveM)
        static bool IsRootPackage(string rel) => Regex.IsMatch(rel, @"^mods/[^/]+\.rpf$", RegexOptions.IgnoreCase);

        // Nom de l'arme d'un fichier : w_pi_pistol+hi.ytd → w_pi_pistol
        public static string WeaponName(string file)
        {
            string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            return n.EndsWith("+hi") ? n.Substring(0, n.Length - 3) : n;
        }

        // Noms des armes d'une liste de fichiers
        public static List<string> WeaponNames(IEnumerable<string> files) =>
            (files ?? Enumerable.Empty<string>()).Select(WeaponName).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Fichiers streamés par un paquet (null : paquet illisible, chiffré)
        static List<string> PackageStreamed(string rpf)
        {
            var entries = ReadRpfEntries(rpf);
            if (entries == null) return null;
            var files = entries.Where(e => !e.Dossier).ToList();
            if (!files.Any(e => e.Path.Equals("assembly.xml", StringComparison.OrdinalIgnoreCase)))
                return new List<string>(); // FiveM l'ignore
            var names = new List<string>();
            foreach (var e in files)
            {
                string n = Path.GetFileName(e.Path).ToLowerInvariant();
                if (IsStreamFile(n)) names.Add(n);
                else if (n.EndsWith(".rpf"))
                    names.AddRange((NestedRpfRootFiles(rpf, e) ?? new List<string>()).Where(IsStreamFile).Select(x => x.ToLowerInvariant()));
            }
            return names;
        }

        // Fichiers de jeu remplacés par les paquets d'un dossier (contenu de pack ou FiveM.app)
        static List<string> StreamedFiles(string root, IEnumerable<string> rels) =>
            rels.Where(IsRootPackage).Select(r => P(root, r)).Where(File.Exists)
                .SelectMany(f => PackageStreamed(f) ?? new List<string>())
                .Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

        // Armes (fichiers) d'un pack de la bibliothèque ; calculées une fois puis retenues
        public static List<string> PackWeapons(LibraryPack p)
        {
            if (p.Armes != null) return p.Armes;
            p.Armes = StreamedFiles(LibraryContent(p.Id), ListFiles(LibraryContent(p.Id)));
            SaveLibraryPack(p);
            return p.Armes;
        }

        // Armes réellement fournies par un pack installé (sans celles laissées à un autre pack)
        static List<string> InstalledWeapons(PackInfo i, Manifest m, string fivemApp)
        {
            if (m.Armes == null)
            {
                var lib = i.LibId == null ? null : ReadJson<LibraryPack>(Path.Combine(LibraryDir(), i.LibId, "pack.json"));
                m.Armes = lib != null ? PackWeapons(lib) : fivemApp != null ? StreamedFiles(fivemApp, Touched(m)) : new List<string>();
            }
            var excl = m.Exclues ?? new Dictionary<string, string>();
            return m.Armes.Where(f => !excl.ContainsKey(f)).ToList();
        }

        // Armes de ce pack déjà remplacées par un pack installé
        public static List<WeaponClash> WeaponConflicts(string libId, string fivemApp = null)
        {
            var mine = new HashSet<string>(PackWeapons(GetLibraryPack(libId)));
            var list = new List<WeaponClash>();
            if (mine.Count == 0) return list;
            foreach (var i in InstalledPacks().Where(i => i.LibId != libId))
            {
                var m = ReadJson<Manifest>(ManifestPath(i.Id));
                if (m == null) continue;
                foreach (var g in InstalledWeapons(i, m, fivemApp).Where(mine.Contains).GroupBy(WeaponName))
                    list.Add(new WeaponClash { Arme = g.Key, Fichiers = g.ToList(), Pack = i });
            }
            return list.OrderBy(c => c.Arme, StringComparer.Ordinal).ToList();
        }

        // Partage des armes après l'installation d'un pack :
        //  keepOld : fichiers que l'utilisateur garde dans les anciens packs (retirés du nouveau) ;
        //  les autres fichiers en commun sont retirés des anciens packs.
        static void ShareWeapons(Manifest m, string libId, string fivemApp, ICollection<string> keepOld, Action<string> log)
        {
            m.Armes = PackWeapons(GetLibraryPack(libId));
            m.Exclues = new Dictionary<string, string>();
            var mine = new HashSet<string>(m.Armes);
            var losers = new List<(PackInfo info, Manifest man)>();
            foreach (var i in InstalledPacks().Where(i => i.Id != m.Id))
            {
                var om = ReadJson<Manifest>(ManifestPath(i.Id));
                if (om == null) continue;
                var shared = InstalledWeapons(i, om, fivemApp).Where(mine.Contains).ToList();
                if (shared.Count == 0) continue;
                om.Exclues = om.Exclues ?? new Dictionary<string, string>();
                var given = new List<string>();
                foreach (var f in shared)
                {
                    if (keepOld != null && keepOld.Contains(f)) m.Exclues[f] = i.Id;
                    else { om.Exclues[f] = m.Id; given.Add(f); }
                }
                if (given.Count > 0)
                {
                    losers.Add((i, om));
                    log($"« {i.Nom} » laisse à « {m.Nom} » : {string.Join(", ", WeaponNames(given))}");
                }
                else WriteJson(ManifestPath(i.Id), om); // garde la liste des armes calculée
            }
            if (m.Exclues.Count > 0)
            {
                log($"Armes gardées dans les anciens packs : {string.Join(", ", WeaponNames(m.Exclues.Keys))}");
                RebuildPackages(m, LibraryContent(libId), fivemApp, log);
            }
            WriteJson(ManifestPath(m.Id), m);
            foreach (var (info, om) in losers)
            {
                RebuildPackages(om, info.LibId == null ? null : LibraryContent(info.LibId), fivemApp, log);
                WriteJson(ManifestPath(info.Id), om);
            }
        }

        // Après le retrait d'un pack : les armes qu'il avait prises reviennent aux autres packs
        // (sauf si un autre pack installé les fournit encore)
        static void GiveBackWeapons(string removedId, string fivemApp, Action<string> log)
        {
            var installed = InstalledPacks();
            var mans = installed.Select(i => (info: i, man: ReadJson<Manifest>(ManifestPath(i.Id)))).Where(x => x.man != null).ToList();
            foreach (var (info, om) in mans)
            {
                var back = (om.Exclues ?? new Dictionary<string, string>()).Where(kv => kv.Value == removedId).Select(kv => kv.Key).ToList();
                if (back.Count == 0) continue;
                var returned = new List<string>();
                foreach (var f in back)
                {
                    // un autre pack fournit encore ce fichier : c'est lui qui le garde
                    var holder = mans.FirstOrDefault(x => x.info.Id != info.Id &&
                                                          InstalledWeapons(x.info, x.man, fivemApp).Contains(f));
                    if (holder.man != null) om.Exclues[f] = holder.info.Id;
                    else { om.Exclues.Remove(f); returned.Add(f); }
                }
                if (returned.Count > 0)
                {
                    RebuildPackages(om, info.LibId == null ? null : LibraryContent(info.LibId), fivemApp, log);
                    log($"« {info.Nom} » reprend : {string.Join(", ", WeaponNames(returned))}");
                }
                WriteJson(ManifestPath(info.Id), om);
            }
        }

        // Réécrit les paquets installés d'un pack sans ses fichiers exclus
        static void RebuildPackages(Manifest m, string content, string fivemApp, Action<string> log)
        {
            var drop = new HashSet<string>((m.Exclues ?? new Dictionary<string, string>()).Keys);
            foreach (var rel in Touched(m).Where(IsRootPackage).ToList())
            {
                string dst = P(fivemApp, rel);
                string src = content != null && File.Exists(P(content, rel)) ? P(content, rel) : dst;
                if (!File.Exists(src))
                {
                    if (drop.Count == 0 || content == null) log($"  ! {rel} introuvable : ses armes ne peuvent pas être remises");
                    continue;
                }
                var streamed = PackageStreamed(src);
                if (streamed == null || streamed.Count == 0) continue;
                WithTempDir(work =>
                {
                    string tmp = Path.Combine(work, Path.GetFileName(rel));
                    int kept = FilterPackage(src, tmp, drop, work);
                    if (kept < 0)
                    {
                        if (streamed.Any(drop.Contains))
                            log($"  ! {Path.GetFileName(rel)} n'a pas été fait par ce logiciel : il ne peut pas être modifié, " +
                                "les deux versions de l'arme restent installées.");
                    }
                    else if (kept == 0) RemoveFile(dst);
                    else CopyFile(tmp, dst);
                    return 0;
                });
            }
        }

        // Recrée un paquet fait par ce logiciel sans les fichiers de drop.
        // Renvoie le nombre de fichiers gardés (0 : rien écrit), -1 si le paquet ne peut pas être modifié.
        static int FilterPackage(string src, string dest, ICollection<string> drop, string work)
        {
            var entries = ReadRpfEntries(src);
            var asmEntry = entries?.FirstOrDefault(e => !e.Dossier && e.Path.Equals("assembly.xml", StringComparison.OrdinalIgnoreCase));
            if (asmEntry == null) return -1;
            string xml = Encoding.UTF8.GetString(ReadRpfData(src, asmEntry));
            if (!xml.Contains(OwnAuthor)) return -1;
            var nm = Regex.Match(xml, "<name>(.*?)</name>", RegexOptions.Singleline);
            string name = nm.Success ? System.Net.WebUtility.HtmlDecode(nm.Groups[1].Value) : Path.GetFileNameWithoutExtension(src);

            string dir = Path.Combine(work, "paquet");
            Directory.CreateDirectory(dir);
            var streamed = new List<string>();
            var rpfs = new List<string>();
            int kept = 0;
            foreach (var e in entries.Where(e => !e.Dossier && e.Path.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(e => e.Path, StringComparer.Ordinal))
            {
                string n = Path.GetFileName(e.Path).ToLowerInvariant();
                if (IsStreamFile(n))
                {
                    if (drop.Contains(n)) continue;
                    string f = Path.Combine(dir, n);
                    ExtractRpfFile(src, e, f);
                    streamed.Add(f);
                    kept++;
                }
                else if (n.EndsWith(".rpf"))
                {
                    string inner = Path.Combine(work, "orig_" + n);
                    ExtractRpfFile(src, e, inner);
                    var ie = ReadRpfEntries(inner);
                    if (ie == null) return -1;
                    var keep = ie.Where(x => !x.Dossier && !(!x.Path.Contains("/") && drop.Contains(x.Path.ToLowerInvariant()))).ToList();
                    int root = keep.Count(x => !x.Path.Contains("/") && IsStreamFile(x.Path));
                    if (root == 0) continue;
                    string f = Path.Combine(dir, n);
                    if (keep.Count == ie.Count(x => !x.Dossier)) File.Copy(inner, f, true);
                    else WriteRpf(f, keep.Select(x => (x.Path, (string)null, ReadRpfData(inner, x))).ToList());
                    rpfs.Add(f);
                    kept += root;
                }
            }
            if (kept > 0) WriteModPackage(dest, name, streamed, rpfs);
            return kept;
        }
    }
}
