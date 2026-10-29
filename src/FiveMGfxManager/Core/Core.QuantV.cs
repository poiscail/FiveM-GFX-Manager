// Mise à jour de QuantV dans les packs de la bibliothèque

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FiveMGfxManager
{
    public class QuantVPack
    {
        public LibraryPack Pack;
        public bool Installe;
        public List<string> Fichiers;   // fichiers QuantV du pack (chemins relatifs)
        public string Rpf;              // .rpf principal (mods\aQuantV.rpf…), ou null
        public DateTime? DateRpf;
    }

    public static partial class Core
    {
        // ------------------------------------------------------------------ //
        //  QuantV : repérage dans les packs et remplacement par une nouvelle version
        // ------------------------------------------------------------------ //

        static bool IsQuantV(string rel) => rel.IndexOf("quantv", StringComparison.OrdinalIgnoreCase) >= 0;

        static string ParentDir(string rel)
        {
            int i = rel.LastIndexOf('/');
            return i < 0 ? "" : rel.Substring(0, i);
        }

        // Packs de la bibliothèque qui contiennent QuantV
        public static List<QuantVPack> FindQuantV()
        {
            var installed = new HashSet<string>(InstalledPacks().Select(p => p.LibId).Where(id => id != null));
            var list = new List<QuantVPack>();
            foreach (var pack in ListLibrary())
            {
                string content = LibraryContent(pack.Id);
                var files = ListFiles(content).Where(IsQuantV).ToList();
                if (files.Count == 0) continue;
                string rpf = files.Where(f => f.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                                  .OrderByDescending(f => new FileInfo(P(content, f)).Length).FirstOrDefault();
                list.Add(new QuantVPack
                {
                    Pack = pack, Installe = installed.Contains(pack.Id), Fichiers = files, Rpf = rpf,
                    DateRpf = rpf == null ? (DateTime?)null : File.GetLastWriteTime(P(content, rpf)),
                });
            }
            return list;
        }

        // Dossier Téléchargements de Windows (FIVEM_GFX_DOWNLOADS le remplace pour les tests)
        static string DownloadsDir() => Environment.GetEnvironmentVariable("FIVEM_GFX_DOWNLOADS")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        static readonly string[] QuantVExts = { ".zip", ".7z", ".rar", ".rpf" };

        // QuantV est payant (Patreon / Discord) : impossible de le télécharger à la place de l'utilisateur.
        // On prend la plus récente archive ou .rpf « QuantV » de Téléchargements arrivée après la version
        // actuelle (import du pack ou dernière mise à jour), null si aucune.
        public static string FindQuantVDownload()
        {
            string dir = DownloadsDir();
            var packs = FindQuantV();
            if (packs.Count == 0 || !Directory.Exists(dir)) return null;
            var since = new DateTime(LoadConfig().QuantVMaj);
            foreach (var q in packs)
                if (DateTime.TryParseExact(q.Pack.Id.Substring(0, Math.Min(15, q.Pack.Id.Length)), "yyyyMMdd_HHmmss",
                                           null, System.Globalization.DateTimeStyles.None, out var d) && d > since)
                    since = d;
            return Directory.GetFiles(dir)
                            .Where(f => IsQuantV(Path.GetFileName(f)) && QuantVExts.Contains(Path.GetExtension(f).ToLowerInvariant())
                                        && File.GetLastWriteTime(f) > since)
                            .OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
        }

        // Remplace QuantV dans tous les packs qui le contiennent par la version de source
        // (archive, dossier ou .rpf). Les packs installés sont retirés puis réinstallés.
        // Les anciens fichiers QuantV sont sauvegardés dans quantv\<date>\.
        public static int UpdateQuantV(string source, string fivemApp, Action<string> log, Action<double> progress)
        {
            var targets = FindQuantV();
            if (targets.Count == 0)
                throw new GfxException("Aucun pack de la bibliothèque ne contient QuantV.\n" +
                                       "Ajoute d'abord ton pack QuantV dans l'onglet Packs.");
            return WithTempDir(work =>
            {
                string staged = StageSource(source, work, log, p => progress(p * 0.4));
                var news = ListFiles(staged);
                if (!news.Any(IsQuantV))
                    throw new GfxException($"Aucun fichier QuantV dans « {SourceName(source)} ».\n" +
                                           "Choisis l'archive, le dossier ou le .rpf de la nouvelle version de QuantV.");
                var newSet = new HashSet<string>(news, StringComparer.OrdinalIgnoreCase);
                var newDirs = new HashSet<string>(news.Select(ParentDir), StringComparer.OrdinalIgnoreCase);

                // la nouvelle version ne doit pas toucher les fichiers d'un autre pack installé
                foreach (var t in targets.Where(t => t.Installe))
                {
                    foreach (var other in InstalledPacks().Where(i => i.LibId != t.Pack.Id))
                    {
                        var om = ReadJson<Manifest>(ManifestPath(other.Id));
                        var clash = om == null ? new List<string>() : Touched(om).Where(newSet.Contains).ToList();
                        if (clash.Count > 0)
                            throw new GfxException($"La nouvelle version touche des fichiers du pack « {other.Nom} » :\n  " +
                                                   string.Join("\n  ", clash.Take(5)) +
                                                   $"\n\nRetire d'abord « {other.Nom} », puis réessaie.");
                    }
                }

                string backupRoot = Path.Combine(DataDir(), "quantv", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                for (int n = 0; n < targets.Count; n++)
                {
                    var t = targets[n];
                    var pack = t.Pack;
                    string content = LibraryContent(pack.Id);
                    Action<double> step = p => progress(0.4 + (n + p) / targets.Count * 0.6);
                    var inst = t.Installe ? InstalledPacks().FirstOrDefault(i => i.LibId == pack.Id) : null;
                    if (inst != null)
                    {
                        if (!IsValidFivemApp(fivemApp))
                            throw new GfxException("Le dossier FiveM.app est invalide. Choisis le bon dossier en haut.");
                        UninstallPack(inst.Id, fivemApp, log, p => step(p * 0.3));
                    }

                    // anciens fichiers QuantV qui disparaissent : ceux des dossiers fournis par la nouvelle version
                    // (ex. mods\aQuantV.rpf remplacé par mods\QuantV_4.rpf)
                    var old = ListFiles(content).Where(IsQuantV).ToList();
                    var gone = old.Where(f => !newSet.Contains(f) && newDirs.Contains(ParentDir(f))).ToList();
                    string backup = Path.Combine(backupRoot, pack.Id);
                    int kept = 0, replaced = 0, added = 0;
                    foreach (var rel in old.Where(f => newSet.Contains(f)).Concat(gone))
                        CopyFile(P(content, rel), P(backup, rel));
                    foreach (var rel in gone) RemoveFile(P(content, rel));
                    foreach (var rel in news)
                    {
                        string dst = P(content, rel);
                        bool exists = File.Exists(dst);
                        // les réglages (.ini) déjà dans le pack sont gardés
                        if (exists && rel.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) { kept++; continue; }
                        CopyFile(P(staged, rel), dst);
                        if (exists) replaced++; else added++;
                    }
                    pack.Taille = FolderSize(content);
                    SaveLibraryPack(pack);
                    log($"QuantV mis à jour dans « {pack.Nom} » : {replaced} remplacés, {added} ajoutés, {gone.Count} supprimés" +
                        (kept > 0 ? $", {kept} réglages .ini gardés." : "."));

                    if (inst != null)
                        InstallLibraryPack(pack.Id, fivemApp, log, p => step(0.3 + p * 0.7));
                }
                if (Directory.Exists(backupRoot)) log("Ancienne version de QuantV sauvegardée dans " + backupRoot);
                var cfg = LoadConfig();
                cfg.QuantVMaj = DateTime.Now.Ticks;
                SaveConfig(cfg);
                progress(1);
                return targets.Count;
            });
        }
    }
}
