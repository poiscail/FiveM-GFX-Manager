// Serveurs « pure » (sv_pureLevel) : ils refusent les fichiers du jeu modifiés, donc les packs son
// (qui remplacent les .rpf de GTA V\x64\audio). On les retire le temps d'y jouer, puis on les remet.

using System;
using System.Collections.Generic;
using System.Linq;

namespace FiveMGfxManager
{
    public static partial class Core
    {
        // Packs son retirés en attente d'être remis (vide : sons actifs)
        public static List<string> CutSoundPacks() => LoadConfig().SonsCoupes ?? new List<string>();

        public static List<PackInfo> InstalledSoundPacks() => InstalledPacks().Where(p => p.Type == "Son").ToList();

        // Retire tous les packs son installés et retient lesquels remettre ; renvoie leur nombre
        public static int CutSounds(string fivemApp, Action<string> log, Action<double> progress)
        {
            var packs = InstalledSoundPacks();
            if (packs.Count == 0) throw new GfxException("Aucun pack son n'est installé : rien à couper.");
            var orphans = packs.Where(p => p.LibId == null || GetLibraryPack(p.LibId) == null).ToList();
            if (orphans.Count > 0)
                throw new GfxException("Ces packs son ne sont pas dans la bibliothèque, ils ne pourraient pas être remis :\n" +
                                       string.Join("\n", orphans.Select(p => "•  " + p.Nom)) +
                                       "\n\nAjoute-les à la bibliothèque, ou retire-les à la main.");
            var cut = CutSoundPacks();
            for (int i = 0; i < packs.Count; i++)
            {
                UninstallPack(packs[i].Id, fivemApp, log, p => progress((i + p) / packs.Count));
                if (!cut.Contains(packs[i].LibId)) cut.Add(packs[i].LibId);
                var cfg = LoadConfig();
                cfg.SonsCoupes = cut;
                SaveConfig(cfg);
            }
            log($"Sons d'origine remis ({packs.Count} pack{(packs.Count > 1 ? "s" : "")} son retiré{(packs.Count > 1 ? "s" : "")}) : " +
                "tu peux rejoindre les serveurs « pure ».");
            return packs.Count;
        }

        // Réinstalle les packs son retirés par CutSounds ; renvoie le nombre remis
        public static int RestoreSounds(string fivemApp, Action<string> log, Action<double> progress)
        {
            var cut = CutSoundPacks();
            if (cut.Count == 0) throw new GfxException("Aucun pack son à remettre.");
            int done = 0;
            for (int i = 0; i < cut.Count; i++)
            {
                string id = cut[i];
                var pack = GetLibraryPack(id);
                if (pack == null) log("  ! un pack son a été supprimé de la bibliothèque : pas remis.");
                else if (IsInstalled(id)) log($"  « {pack.Nom} » est déjà installé.");
                else if (ConflictingPacks(id).Any())
                    log($"  ! « {pack.Nom} » pas remis : un autre pack installé modifie les mêmes fichiers " +
                        $"({string.Join(", ", ConflictingPacks(id).Select(p => p.Nom))}).");
                else
                {
                    InstallLibraryPack(id, fivemApp, log, p => progress((i + p) / cut.Count));
                    done++;
                }
            }
            var cfg = LoadConfig();
            cfg.SonsCoupes = null;
            SaveConfig(cfg);
            log($"{done} pack{(done > 1 ? "s" : "")} son remis.");
            return done;
        }
    }
}
