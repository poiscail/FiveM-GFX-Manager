// Installation de ReShade

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
        //  ReShade
        // ------------------------------------------------------------------ //

        public static ReshadeState ReshadeStatus() => LoadConfig().Reshade;

        public static (string url, string version) FindLatestReshadeUrl(Action<string> log)
        {
            log("Recherche de la dernière version de ReShade sur reshade.me…");
            string html = DownloadString(ReshadeSite);
            // version normale (pas _Addon : la version add-on est bloquée par FiveM)
            var best = Regex.Matches(html, @"/downloads/ReShade_Setup_([\d.]+)\.exe").Cast<Match>()
                            .Select(m => (path: m.Value, ver: m.Groups[1].Value))
                            .Where(x => Version.TryParse(x.ver, out _))
                            .OrderByDescending(x => Version.Parse(x.ver))
                            .FirstOrDefault();
            if (best.path == null)
                throw new GfxException("Lien de téléchargement ReShade introuvable.\n" +
                                       "Télécharge ReShade_Setup_x.x.x.exe sur reshade.me et choisis-le manuellement.");
            return (ReshadeSite + best.path, best.ver);
        }

        // Version du ReShade en place dans plugins\dxgi.dll (installé ici ou fourni par un pack), null si inconnue
        public static string ReshadeDllVersion(string fivemApp)
        {
            string dll = Path.Combine(fivemApp, "plugins", "dxgi.dll");
            if (!File.Exists(dll)) return null;
            try
            {
                var v = FileVersionInfo.GetVersionInfo(dll);
                if ((v.ProductName + " " + v.FileDescription).IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) < 0) return null;
                return $"{v.FileMajorPart}.{v.FileMinorPart}.{v.FileBuildPart}";
            }
            catch { return null; }
        }

        // Remplace le dxgi.dll de ReShade par la dernière version sans toucher au ReShade.ini, aux presets
        // ni aux shaders. ReShade fourni par un pack : le nouveau se met par-dessus (celui du pack revient
        // à la désinstallation). Renvoie false si la version en place est déjà la dernière.
        public static bool UpdateReshade(string fivemApp, string setupFile, Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp)) throw new GfxException("Le dossier FiveM.app est invalide.");
            string current = ReshadeDllVersion(fivemApp);
            if (ReshadeStatus() == null && current == null)
                throw new GfxException("ReShade n'est pas installé : utilise « Installer ReShade ».");
            var over = LoadConfig().PacksInstalles.FirstOrDefault(p => (ReadJson<Manifest>(ManifestPath(p.Id))?.SurReshade ?? new List<string>())
                                                                        .Contains("plugins/dxgi.dll", StringComparer.OrdinalIgnoreCase));
            if (over != null)
                throw new GfxException($"Le pack « {over.Nom} » a remplacé le dxgi.dll de ReShade : retire-le avant de mettre ReShade à jour.");
            string work = NewTempDir("fivem_reshade_maj_");
            try
            {
                if (setupFile == null)
                {
                    var (url, latest) = FindLatestReshadeUrl(log);
                    if (Version.TryParse(current ?? "", out var cur) && cur >= Version.Parse(latest))
                    {
                        log($"ReShade {current} est déjà la dernière version.");
                        progress(1);
                        return false;
                    }
                    log(current != null ? $"Mise à jour de ReShade {current} vers {latest}…" : $"Mise à jour vers ReShade {latest}…");
                    setupFile = Path.Combine(work, $"ReShade_Setup_{latest}.exe");
                    Download(url, setupFile, log, p => progress(p * 0.5));
                }
                InstallReshade(fivemApp, setupFile, false, null, log, p => progress(0.5 + p * 0.5));
                return true;
            }
            finally
            {
                try { RemoveTree(work); } catch { }
            }
        }

        // Le setup ReShade contient une archive zip (collée à la fin de l'exe) avec ReShade64.dll.
        static string ExtractReshadeDll(string setupExe, string destDir)
        {
            byte[] data = File.ReadAllBytes(setupExe);
            int eocd = -1;
            for (int i = data.Length - 22; i >= Math.Max(0, data.Length - 70000); i--)
                if (data[i] == 0x50 && data[i + 1] == 0x4B && data[i + 2] == 0x05 && data[i + 3] == 0x06) { eocd = i; break; }
            if (eocd >= 0)
            {
                long cdSize = BitConverter.ToUInt32(data, eocd + 12);
                long cdOffset = BitConverter.ToUInt32(data, eocd + 16);
                long start = Math.Max(0, eocd - cdSize - cdOffset);
                using (var ms = new MemoryStream(data, (int)start, data.Length - (int)start))
                using (var zip = ArchiveFactory.OpenArchive(ms))
                {
                    var entry = zip.Entries.FirstOrDefault(e => !e.IsDirectory &&
                        string.Equals(Path.GetFileName(e.Key), "ReShade64.dll", StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        string outPath = Path.Combine(destDir, "ReShade64.dll");
                        using (var s = entry.OpenEntryStream())
                        using (var o = File.Create(outPath))
                            s.CopyTo(o);
                        return outPath;
                    }
                }
            }
            throw new GfxException("ReShade64.dll introuvable dans ce fichier.\n" +
                                   "Choisis le fichier ReShade_Setup_x.x.x.exe officiel (ou ReShade64.dll).");
        }

        // setupFile : ReShade_Setup_x.exe ou ReShade64.dll (null = téléchargement automatique).
        public static void InstallReshade(string fivemApp, string setupFile, bool withShaders, string preset,
                                          Action<string> log, Action<double> progress)
        {
            if (!IsValidFivemApp(fivemApp)) throw new GfxException("Le dossier FiveM.app est invalide.");
            string plugins = Path.Combine(fivemApp, "plugins");
            Directory.CreateDirectory(plugins);
            string work = NewTempDir("fivem_reshade_");
            var state = ReshadeStatus() ?? new ReshadeState();
            state.EnsureInit();
            string backupDir = Path.Combine(DataDir(), "reshade_sauvegarde");

            void Track(string path)
            {
                string rel = RelPath(fivemApp, path);
                if (state.Fichiers.Contains(rel)) return;
                // un fichier qui existait avant ReShade (ex : dxgi.dll d'un pack) est sauvegardé
                if (File.Exists(path) && !state.Sauvegardes.ContainsKey(rel))
                {
                    string b = P(backupDir, rel);
                    CopyFile(path, b);
                    state.Sauvegardes[rel] = b;
                }
                state.Fichiers.Add(rel);
            }

            try
            {
                progress(0.05);
                string dll, version;
                if (setupFile != null && setupFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    dll = setupFile;
                    version = "(dll manuelle)";
                }
                else
                {
                    if (setupFile == null)
                    {
                        string url;
                        (url, version) = FindLatestReshadeUrl(log);
                        setupFile = Path.Combine(work, "ReShade_Setup.exe");
                        Download(url, setupFile, log, p => progress(0.05 + p * 0.35));
                    }
                    else
                    {
                        var m = Regex.Match(Path.GetFileName(setupFile), @"\d+\.[\d.]*\d");
                        version = m.Success ? m.Value : "?";
                    }
                    log("Extraction de ReShade64.dll…");
                    dll = ExtractReshadeDll(setupFile, work);
                }
                progress(0.45);

                string target = Path.Combine(plugins, "dxgi.dll");
                Track(target);
                CopyFile(dll, target);
                log("ReShade copié dans plugins\\dxgi.dll");

                string shadersDest = Path.Combine(plugins, "reshade-shaders");
                if (withShaders)
                {
                    string zpath = Path.Combine(work, "shaders.zip");
                    Download(ReshadeShadersZip, zpath, log, p => progress(0.45 + p * 0.35));
                    string ex = Path.Combine(work, "shaders");
                    ExtractArchive(zpath, ex, log, p => progress(0.8 + p * 0.1));
                    string top = Directory.GetDirectories(ex).First();
                    if (!Directory.Exists(shadersDest))
                    {
                        string rel = RelPath(fivemApp, shadersDest);
                        if (!state.Dossiers.Contains(rel)) state.Dossiers.Add(rel);
                    }
                    foreach (var sub in new[] { "Shaders", "Textures" })
                    {
                        string s = Path.Combine(top, sub);
                        if (!Directory.Exists(s)) continue;
                        foreach (var rel in ListFiles(s))
                            CopyFile(P(s, rel), P(Path.Combine(shadersDest, sub), rel));
                    }
                    log("Shaders officiels installés dans plugins\\reshade-shaders");
                }
                progress(0.9);

                string ini = Path.Combine(plugins, "ReShade.ini");
                if (!File.Exists(ini))
                {
                    Track(ini);
                    File.WriteAllText(ini,
                        "[GENERAL]\r\n" +
                        $"EffectSearchPaths={Path.Combine(shadersDest, "Shaders")}\\**\r\n" +
                        $"TextureSearchPaths={Path.Combine(shadersDest, "Textures")}\\**\r\n" +
                        $"PresetPath={Path.Combine(plugins, "ReShadePreset.ini")}\r\n" +
                        "\r\n[INPUT]\r\nKeyOverlay=36,0,0,0\r\n" +
                        "\r\n[OVERLAY]\r\nTutorialProgress=4\r\n");
                    log("ReShade.ini créé (touche du menu : Début / Home)");
                }

                if (!string.IsNullOrEmpty(preset))
                {
                    string p = Path.Combine(plugins, "ReShadePreset.ini");
                    Track(p);
                    CopyFile(preset, p);
                    log($"Preset « {Path.GetFileName(preset)} » appliqué.");
                }

                state.Version = ReshadeDllVersion(fivemApp) ?? version;
                state.Date = Now();
                var cfg = LoadConfig();
                cfg.Reshade = state;
                SaveConfig(cfg);
                progress(1);
                log($"ReShade {state.Version} installé. Lance FiveM puis appuie sur Début (Home) en jeu.");
            }
            finally
            {
                try { RemoveTree(work); } catch { }
            }
        }

        public static void UninstallReshade(string fivemApp, Action<string> log, Action<double> progress)
        {
            var state = ReshadeStatus() ?? throw new GfxException("ReShade n'a pas été installé par ce logiciel.");
            state.EnsureInit();
            var packs = LoadConfig().PacksInstalles
                .Select(p => (path: ManifestPath(p.Id), m: ReadJson<Manifest>(ManifestPath(p.Id)), id: p.Id))
                .Where(x => x.m != null).ToList();
            foreach (var rel in state.Fichiers)
            {
                string path = P(fivemApp, rel);
                state.Sauvegardes.TryGetValue(rel, out var backup);
                var owner = packs.FirstOrDefault(x => (x.m.SurReshade ?? new List<string>())
                                                      .Contains(rel, StringComparer.OrdinalIgnoreCase));
                if (owner.m != null)
                {
                    // un pack installé par-dessus ReShade : on garde son fichier, et son retrait remettra
                    // ce qu'il y avait avant ReShade (ou rien)
                    string packCopy = P(Path.Combine(InstallsDir(), owner.id, "fichiers_remplaces"), rel);
                    if (backup != null && File.Exists(backup))
                        CopyFile(backup, packCopy);
                    else
                    {
                        RemoveFile(packCopy);
                        owner.m.Remplaces.RemoveAll(r => r.Equals(rel, StringComparison.OrdinalIgnoreCase));
                        owner.m.Ajoutes.Add(rel);
                    }
                    owner.m.SurReshade.RemoveAll(r => r.Equals(rel, StringComparison.OrdinalIgnoreCase));
                    WriteJson(owner.path, owner.m);
                    log($"Gardé : {rel} (fichier du pack installé par-dessus ReShade)");
                }
                else if (backup != null && File.Exists(backup))
                {
                    CopyFile(backup, path);
                    log("Remis d'origine : " + rel);
                }
                else
                {
                    RemoveFile(path);
                    log("Supprimé : " + rel);
                }
            }
            foreach (var rel in state.Dossiers)
            {
                RemoveTree(P(fivemApp, rel));
                log("Supprimé : " + rel + "\\");
            }
            foreach (var extra in new[] { "ReShade.log", "ReShade64.log" })
                RemoveFile(Path.Combine(fivemApp, "plugins", extra));
            RemoveTree(Path.Combine(DataDir(), "reshade_sauvegarde"));
            var cfg = LoadConfig();
            cfg.Reshade = null;
            SaveConfig(cfg);
            progress(1);
            log("ReShade désinstallé.");
        }
    }
}
