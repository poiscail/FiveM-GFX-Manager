// Installation et retrait des packs dans FiveM.app

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
    public static partial class Core
    {
        // ------------------------------------------------------------------ //
        //  Installation des packs dans FiveM.app (plusieurs à la fois)
        // ------------------------------------------------------------------ //

        public static List<PackInfo> InstalledPacks() => LoadConfig().PacksInstalles;

        // moveAside : sons d'armes trouvés par FindWeaponSounds à mettre de côté (remis au retrait du pack)
        public static Manifest InstallLibraryPack(string id, string fivemApp, Action<string> log, Action<double> progress,
                                                  List<SoundHit> moveAside = null)
        {
            var pack = GetLibraryPack(id);
            if (IsInstalled(id)) throw new GfxException($"Le pack « {pack.Nom} » est déjà installé.");
            var m = InstallContent(LibraryContent(id), pack.Nom, pack.Type, id, pack.Source, fivemApp, log, progress);
            var moved = (moveAside ?? new List<SoundHit>()).Where(h => h.Deplacable && File.Exists(h.Chemin))
                                                            .Select(h => h.Rel).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in moved)
            {
                string b = P(Path.Combine(InstallsDir(), m.Id, "sons_deplaces"), rel);
                CopyFile(P(fivemApp, rel), b);
                RemoveFile(P(fivemApp, rel));
                m.Deplaces.Add(rel);
                log("Son d'arme mis de côté : " + rel.Replace('/', '\\'));
            }
            if (m.Deplaces.Count > 0) WriteJson(ManifestPath(m.Id), m);
            RecordInstall($"Installé : {pack.Nom}", m.Id);
            return m;
        }

        // Packs installés qui modifient déjà des fichiers de ce pack de la bibliothèque
        // (ex. un autre pack son) : il faut les retirer avant de l'installer.
        public static List<PackInfo> ConflictingPacks(string libId)
        {
            var mine = new HashSet<string>(ListFiles(LibraryContent(libId)), StringComparer.OrdinalIgnoreCase);
            return InstalledPacks().Where(i => i.LibId != libId).Where(i =>
            {
                var m = ReadJson<Manifest>(ManifestPath(i.Id));
                return m != null && Touched(m).Any(mine.Contains);
            }).ToList();
        }

        // Installe directement une archive, un dossier ou un .rpf (sans passer par la bibliothèque).
        public static Manifest InstallPack(string source, string fivemApp, Action<string> log, Action<double> progress,
                                           string type = "Graphique")
        {
            if (!IsValidFivemApp(fivemApp))
                throw new GfxException("Le dossier FiveM.app est invalide. Choisis le bon dossier en haut.");
            var m = WithTempDir(work =>
                InstallContent(StageSource(source, work, log, p => progress(p * 0.5)), SourceName(source), type, null,
                               source, fivemApp, log, p => progress(0.5 + p * 0.5)));
            RecordInstall($"Installé : {m.Nom}", m.Id);
            return m;
        }

        static string ManifestPath(string installId) => Path.Combine(InstallsDir(), installId, "manifest.json");

        // Fichiers de FiveM.app écrits par un pack installé
        static IEnumerable<string> Touched(Manifest m) =>
            (m.Ajoutes ?? new List<string>()).Concat(m.Remplaces ?? new List<string>());

        // FiveM ne lit pas les sons du jeu (x64\audio\…) dans FiveM.app\mods : comme le disent les tutos
        // des packs son, ils vont directement dans le dossier de GTA V (l'original est sauvegardé et remis au retrait).
        static bool IsGtaAudio(string rel) => rel.StartsWith("mods/x64/audio/", StringComparison.OrdinalIgnoreCase);

        // Fichier réellement écrit pour un chemin du pack
        static string Dest(Manifest m, string fivemApp, string rel) =>
            m.DossierGta != null && IsGtaAudio(rel) ? P(m.DossierGta, rel.Substring("mods/".Length)) : P(fivemApp, rel);

        static Manifest InstallContent(string content, string packName, string type, string libId, string source,
                                       string fivemApp, Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp))
                throw new GfxException("Le dossier FiveM.app est invalide. Choisis le bon dossier en haut.");

            var files = ListFiles(content);
            if (files.Count == 0) throw new GfxException("Le pack ne contient aucun fichier.");

            var mine = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var cfgBefore = LoadConfig();
            // fichiers de ReShade que le pack remplace : la version ReShade est sauvegardée comme un fichier
            // remplacé ordinaire, et revient au retrait du pack
            var rsClash = (cfgBefore.Reshade?.Fichiers ?? new List<string>()).Where(mine.Contains).ToList();
            if (rsClash.Count > 0)
                log($"Le pack remplace des fichiers de ReShade ({string.Join(", ", rsClash)}) : " +
                    "ils seront remis au retrait du pack.");
            // deux packs installés ne doivent jamais toucher le même fichier (sinon le retrait casserait l'autre)
            foreach (var other in cfgBefore.PacksInstalles)
            {
                var om = ReadJson<Manifest>(ManifestPath(other.Id));
                if (om == null) continue;
                var clash = Touched(om).Where(mine.Contains).ToList();
                if (clash.Count > 0)
                    throw new GfxException($"Ce pack remplace des fichiers déjà modifiés par le pack « {other.Nom} » :\n  " +
                                           string.Join("\n  ", clash.Take(5)) + (clash.Count > 5 ? $"\n  … (+{clash.Count - 5})" : "") +
                                           $"\n\nRetire d'abord « {other.Nom} », puis réessaie.");
            }

            Directory.CreateDirectory(InstallsDir());
            string packDir = NewIdDir(InstallsDir(), out string packId);
            string backupDir = Path.Combine(packDir, "fichiers_remplaces");
            Directory.CreateDirectory(backupDir);

            var manifest = new Manifest { Nom = packName, Id = packId, Source = source, Date = Now(), SurReshade = rsClash };
            if (files.Any(IsGtaAudio))
            {
                manifest.DossierGta = GtaPath();
                if (manifest.DossierGta != null)
                    log($"Sons placés dans le dossier de GTA V ({manifest.DossierGta}\\x64\\audio) : " +
                        "les originaux sont sauvegardés et remis au retrait du pack.");
                else
                    log("! GTA V introuvable : les sons sont posés dans FiveM.app\\mods, où FiveM risque de ne pas les lire.");
            }

            // dossiers qui n'existaient pas avant (pour pouvoir les supprimer ensuite)
            var newDirs = new HashSet<string>();
            foreach (var rel in files)
            {
                if (manifest.DossierGta != null && IsGtaAudio(rel)) continue; // dossiers du jeu : on ne les supprime jamais
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
                    string rel = files[i];
                    string dst = Dest(manifest, fivemApp, rel);
                    if (File.Exists(dst))
                    {
                        CopyFile(dst, P(backupDir, rel));
                        manifest.Remplaces.Add(rel);
                    }
                    else
                    {
                        manifest.Ajoutes.Add(rel);
                    }
                    CopyFile(P(content, rel), dst);
                    if (i % 25 == 0 || i == files.Count - 1) progress((i + 1) / (double)files.Count);
                }
            }
            catch
            {
                log("Erreur pendant la copie, annulation des modifications…");
                Revert(manifest, fivemApp, backupDir, log);
                RemoveTree(packDir);
                throw;
            }

            WriteJson(ManifestPath(packId), manifest);
            var cfg = LoadConfig();
            cfg.PacksInstalles.Add(new PackInfo { Nom = packName, Id = packId, Date = manifest.Date, Type = type, LibId = libId });
            SaveConfig(cfg);
            log($"Pack « {packName} » installé : {manifest.Ajoutes.Count} ajoutés, {manifest.Remplaces.Count} remplacés.");
            return manifest;
        }

        static void Revert(Manifest m, string fivemApp, string backupDir, Action<string> log)
        {
            foreach (var rel in m.Ajoutes ?? new List<string>())
                RemoveFile(Dest(m, fivemApp, rel));
            foreach (var rel in m.Remplaces ?? new List<string>())
            {
                string b = P(backupDir, rel);
                if (File.Exists(b)) CopyFile(b, Dest(m, fivemApp, rel));
                else log("  ! sauvegarde manquante : " + rel);
            }
            // on supprime les dossiers créés par le pack, du plus profond au moins profond
            foreach (var d in (m.DossiersCrees ?? new List<string>()).OrderByDescending(x => x.Count(c => c == '/')))
            {
                try { Directory.Delete(P(fivemApp, d)); }
                catch { } // pas vide (autre contenu ajouté depuis) : on le garde
            }
        }

        // Retire un pack installé (id d'installation) et remet les fichiers qu'il avait remplacés.
        public static void UninstallPack(string packId, string fivemApp, Action<string> log, Action<double> progress)
        {
            var info = InstalledPacks().FirstOrDefault(p => p.Id == packId)
                ?? throw new GfxException("Ce pack n'est pas installé.");
            string packDir = Path.Combine(InstallsDir(), info.Id);
            var manifest = ReadJson<Manifest>(ManifestPath(info.Id))
                ?? throw new GfxException("Le journal d'installation est introuvable.\n" +
                                          "Utilise « Restaurer FiveM d'origine » à la place.");

            log($"Retrait du pack « {info.Nom} »…");
            progress(0.3);
            var cfg = LoadConfig();
            string backupDir = Path.Combine(packDir, "fichiers_remplaces");
            HandOverToReshade(manifest, cfg, backupDir, log);
            Revert(manifest, fivemApp, backupDir, log);
            foreach (var rel in manifest.Deplaces ?? new List<string>())
            {
                string b = P(Path.Combine(packDir, "sons_deplaces"), rel);
                if (!File.Exists(b)) log("  ! son mis de côté introuvable : " + rel);
                else if (File.Exists(P(fivemApp, rel))) log("  ! " + rel + " existe déjà : son mis de côté pas remis");
                else { CopyFile(b, P(fivemApp, rel)); log("Son d'arme remis : " + rel.Replace('/', '\\')); }
            }
            progress(1);
            RemoveTree(packDir);
            cfg.PacksInstalles.RemoveAll(p => p.Id == packId);

            // dossiers créés par ce pack mais encore utilisés par un autre : c'est l'autre pack qui les supprimera
            var kept = (manifest.DossiersCrees ?? new List<string>()).Where(d => Directory.Exists(P(fivemApp, d))).ToList();
            foreach (var other in cfg.PacksInstalles)
            {
                string path = ManifestPath(other.Id);
                var om = ReadJson<Manifest>(path);
                if (om == null || kept.Count == 0) continue;
                om.DossiersCrees = om.DossiersCrees ?? new List<string>();
                var mine = Touched(om).ToList();
                var taken = kept.Where(d => !om.DossiersCrees.Contains(d) &&
                                            mine.Any(f => f.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase))).ToList();
                if (taken.Count == 0) continue;
                om.DossiersCrees.AddRange(taken);
                WriteJson(path, om);
                kept.RemoveAll(taken.Contains);
            }
            SaveConfig(cfg);
            log($"Pack retiré : {manifest.Ajoutes.Count} fichiers supprimés, {manifest.Remplaces.Count} fichiers d'origine remis.");
            RecordRemoval($"Retiré : {info.Nom}", info.LibId);
        }

        // ReShade installé après le pack a écrasé certains de ses fichiers (ex : plugins\dxgi.dll) :
        // on les laisse en place et c'est ReShade qui remettra l'original à sa désinstallation.
        static void HandOverToReshade(Manifest m, Config cfg, string packBackupDir, Action<string> log)
        {
            var rs = cfg.Reshade;
            if (rs == null) return;
            rs.EnsureInit();
            // (les fichiers installés par-dessus ReShade reprennent simplement leur version ReShade)
            var over = m.SurReshade ?? new List<string>();
            var shared = Touched(m).Where(f => rs.Fichiers.Contains(f, StringComparer.OrdinalIgnoreCase) &&
                                               !over.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            if (shared.Count == 0) return;
            string rsBackupDir = Path.Combine(DataDir(), "reshade_sauvegarde");
            foreach (var rel in shared)
            {
                string key = rs.Sauvegardes.Keys.FirstOrDefault(k => k.Equals(rel, StringComparison.OrdinalIgnoreCase)) ?? rel;
                string old = P(packBackupDir, rel);
                if ((m.Remplaces ?? new List<string>()).Contains(rel) && File.Exists(old))
                {
                    // l'original d'avant le pack devient la sauvegarde de ReShade
                    string b = P(rsBackupDir, key);
                    CopyFile(old, b);
                    rs.Sauvegardes[key] = b;
                }
                else if (rs.Sauvegardes.TryGetValue(key, out var b))
                {
                    // le fichier n'existait pas avant le pack : ReShade devra simplement le supprimer
                    RemoveFile(b);
                    rs.Sauvegardes.Remove(key);
                }
                m.Ajoutes?.Remove(rel);
                m.Remplaces?.Remove(rel);
                log($"  {rel} est utilisé par ReShade : laissé en place.");
            }
            SaveConfig(cfg);
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

    }
}
