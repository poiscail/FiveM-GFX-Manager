// Sauvegarde d'origine de FiveM.app

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
        //  Sauvegarde d'origine (copie complète sans mods)
        // ------------------------------------------------------------------ //

        public static BackupInfo OriginalBackupInfo() => ReadJson<BackupInfo>(Path.Combine(OriginalBackupDir(), "info.json"));
        public static bool HasOriginalBackup() => OriginalBackupInfo() != null;

        public static void CreateOriginalBackup(string fivemApp, Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp)) throw new GfxException("Le dossier FiveM.app est invalide.");
            string dest = OriginalBackupDir();
            string tmp = dest + "_tmp";
            RemoveTree(tmp);
            Directory.CreateDirectory(tmp);

            var present = new List<string>();
            var files = new List<(string dir, string rel)>();
            foreach (var d in FivemDirs)
            {
                string src = Path.Combine(fivemApp, d);
                if (!Directory.Exists(src)) continue;
                present.Add(d);
                Directory.CreateDirectory(Path.Combine(tmp, d));
                files.AddRange(ListFiles(src).Select(rel => (d, rel)));
            }

            log($"Sauvegarde d'origine : {files.Count} fichiers à copier…");
            for (int i = 0; i < files.Count; i++)
            {
                var (d, rel) = files[i];
                CopyFile(P(Path.Combine(fivemApp, d), rel), P(Path.Combine(tmp, d), rel));
                if (i % 50 == 0 || i == files.Count - 1) progress((i + 1) / (double)files.Count);
            }

            WriteJson(Path.Combine(tmp, "info.json"),
                      new BackupInfo { Date = Now(), FivemApp = fivemApp, Dossiers = present, Fichiers = files.Count });
            RemoveTree(dest);
            Directory.Move(tmp, dest);
            log($"Sauvegarde d'origine créée ({HumanSize(FolderSize(dest))}).");
        }

        // Remet FiveM.app exactement dans l'état de la sauvegarde d'origine.
        public static void RestoreOriginalBackup(string fivemApp, Action<string> log, Action<double> progress)
        {
            var info = OriginalBackupInfo() ?? throw new GfxException("Aucune sauvegarde d'origine n'existe.");
            string srcRoot = OriginalBackupDir();

            // les sons posés dans le dossier de GTA V ne sont pas dans la sauvegarde de FiveM.app : on remet les originaux
            foreach (var p in LoadConfig().PacksInstalles)
            {
                var m = ReadJson<Manifest>(ManifestPath(p.Id));
                if (m?.DossierGta == null) continue;
                log($"Sons d'origine de GTA V remis (pack « {p.Nom} »)…");
                string backupDir = Path.Combine(InstallsDir(), p.Id, "fichiers_remplaces");
                foreach (var rel in (m.Ajoutes ?? new List<string>()).Where(IsGtaAudio))
                    RemoveFile(Dest(m, fivemApp, rel));
                foreach (var rel in (m.Remplaces ?? new List<string>()).Where(IsGtaAudio))
                {
                    string b = P(backupDir, rel);
                    if (File.Exists(b)) CopyFile(b, Dest(m, fivemApp, rel));
                    else log("  ! sauvegarde manquante : " + rel);
                }
            }

            foreach (var d in FivemDirs)
            {
                string target = Path.Combine(fivemApp, d);
                if (!Directory.Exists(target)) continue;
                log($"Suppression de {d}\\ (version moddée)…");
                RemoveTree(target);
            }

            var files = new List<(string dir, string rel)>();
            foreach (var d in info.Dossiers ?? new List<string>())
            {
                Directory.CreateDirectory(Path.Combine(fivemApp, d));
                files.AddRange(ListFiles(Path.Combine(srcRoot, d)).Select(rel => (d, rel)));
            }

            log($"Restauration de {files.Count} fichiers d'origine…");
            for (int i = 0; i < files.Count; i++)
            {
                var (d, rel) = files[i];
                CopyFile(P(Path.Combine(srcRoot, d), rel), P(Path.Combine(fivemApp, d), rel));
                if (i % 50 == 0 || i == files.Count - 1) progress((i + 1) / (double)files.Count);
            }

            // plus aucun pack ni ReShade n'est installé après une restauration complète
            var cfg = LoadConfig();
            cfg.PacksInstalles.Clear();
            cfg.Reshade = null;
            SaveConfig(cfg);
            RemoveTree(InstallsDir());
            RemoveTree(Path.Combine(DataDir(), "reshade_sauvegarde"));
            log("FiveM.app a été remis dans son état d'origine.");
        }

    }
}
