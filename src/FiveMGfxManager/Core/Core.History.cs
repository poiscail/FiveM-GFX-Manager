using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace FiveMGfxManager
{
    // Une action faite par le logiciel, avec de quoi l'annuler
    [DataContract]
    public class HistoryEntry
    {
        [DataMember] public string Id;
        [DataMember] public string Date;
        [DataMember] public string Action;   // texte affiché
        [DataMember] public string Type;     // install, retrait, fichier
        [DataMember] public string Cible;    // id d'installation (install), id bibliothèque (retrait), chemin du fichier (fichier)
        [DataMember] public string Copie;    // copie du fichier avant l'action (null : le fichier n'existait pas)
        [DataMember] public bool Annule;
    }

    // Historique de tout ce que le logiciel a changé, chaque action annulable
    public static partial class Core
    {
        const int HistoryMax = 300;

        static string HistoryDir() => Path.Combine(DataDir(), "historique");
        static string HistoryPath() => Path.Combine(DataDir(), "historique.json");

        // la plus récente en premier
        public static List<HistoryEntry> ListHistory() =>
            ReadJson<List<HistoryEntry>>(HistoryPath()) ?? new List<HistoryEntry>();

        static void SaveHistory(List<HistoryEntry> list)
        {
            foreach (var old in list.Skip(HistoryMax).Where(e => e.Copie != null))
                RemoveFile(Path.Combine(HistoryDir(), old.Copie));
            WriteJson(HistoryPath(), list.Take(HistoryMax).ToList());
        }

        // l'historique ne doit jamais faire échouer l'action elle-même
        static void Record(HistoryEntry e)
        {
            try
            {
                var list = ListHistory();
                e.Id = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 4);
                e.Date = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
                list.Insert(0, e);
                SaveHistory(list);
            }
            catch { }
        }

        static void RecordInstall(string action, string installId) =>
            Record(new HistoryEntry { Action = action, Type = "install", Cible = installId });

        static void RecordRemoval(string action, string libId) =>
            Record(new HistoryEntry { Action = action, Type = "retrait", Cible = libId });

        // À appeler juste avant de modifier le fichier : il est copié pour pouvoir revenir en arrière
        static void RecordFile(string action, string path)
        {
            string copy = null;
            try
            {
                if (File.Exists(path))
                {
                    copy = Guid.NewGuid().ToString("N") + Path.GetExtension(path);
                    CopyFile(path, Path.Combine(HistoryDir(), copy));
                }
            }
            catch { return; }
            Record(new HistoryEntry { Action = action, Type = "fichier", Cible = path, Copie = copy });
        }

        // Pourquoi l'action ne peut pas (ou plus) être annulée, null si elle peut l'être
        public static string WhyNotUndoable(HistoryEntry e)
        {
            if (e.Annule) return "Déjà annulée.";
            switch (e.Type)
            {
                case "install":
                    return InstalledPacks().Any(p => p.Id == e.Cible) ? null : "Ce pack n'est plus installé.";
                case "retrait":
                    if (e.Cible == null || !Directory.Exists(LibraryContent(e.Cible))) return "Ce pack n'est plus dans la bibliothèque.";
                    return IsInstalled(e.Cible) ? "Ce pack est déjà réinstallé." : null;
                case "fichier":
                    return e.Copie == null || File.Exists(Path.Combine(HistoryDir(), e.Copie)) ? null : "La copie du fichier a disparu.";
            }
            return "Action inconnue.";
        }

        public static void UndoHistory(string id, string fivemApp, Action<string> log, Action<double> progress)
        {
            var list = ListHistory();
            var e = list.FirstOrDefault(x => x.Id == id) ?? throw new GfxException("Action introuvable dans l'historique.");
            string why = WhyNotUndoable(e);
            if (why != null) throw new GfxException(why);
            log("Annulation : " + e.Action);
            switch (e.Type)
            {
                case "install":
                    UninstallPack(e.Cible, fivemApp, log, progress);
                    break;
                case "retrait":
                    InstallLibraryPack(e.Cible, fivemApp, log, progress);
                    break;
                case "fichier":
                    if (e.Copie == null) RemoveFile(e.Cible);
                    else CopyFile(Path.Combine(HistoryDir(), e.Copie), e.Cible);
                    log("  fichier remis comme avant : " + e.Cible);
                    break;
            }
            // relu : l'annulation elle-même a pu ajouter des lignes, marquées annulées elles aussi
            // pour que « Annuler la dernière action » remonte l'historique au lieu de refaire
            var before = new HashSet<string>(list.Select(x => x.Id));
            list = ListHistory();
            foreach (var x in list.Where(x => x.Id == id || !before.Contains(x.Id))) x.Annule = true;
            SaveHistory(list);
            progress(1);
        }

        // Dernière action pas encore annulée (null : rien à annuler)
        public static HistoryEntry LastUndoable() => ListHistory().FirstOrDefault(e => !e.Annule);

        public static void ClearHistory()
        {
            if (Directory.Exists(HistoryDir())) RemoveTree(HistoryDir());
            RemoveFile(HistoryPath());
        }
    }
}
