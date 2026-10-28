# FiveM GFX Manager

Logiciel Windows (aucune installation nécessaire) pour :

- garder une **bibliothèque de packs** : on lui donne des archives (`.zip`, `.7z`, `.rar`),
  des dossiers ou des `.rpf` (bouton ou glisser-déposer), il les range et les stocke ;
- choisir quel pack **installer** dans `FiveM.app` : packs **graphiques**, packs **son**
  et **remplacements d'armes** (plusieurs packs à la fois, conflits détectés) ;
- **ajouter des mods** à un pack existant, changer son type, le supprimer ;
- retirer un pack et remettre les fichiers d'origine ;
- faire une sauvegarde complète d'origine (sans mods) de `citizen`, `mods`, `plugins` et la restaurer ;
- installer / désinstaller ReShade dans `FiveM.app\plugins` (téléchargement automatique + shaders) ;
- vider le cache FiveM.

Formats de packs acceptés : arborescence `citizen` / `mods` / `plugins`, format GTA `x64/...`
ou `update/...` (copié dans `mods`, typique des packs son), ou fichiers `.rpf` seuls dont on connaît
le chemin dans GTA V (`RESIDENT.rpf`, `WEAPONS_PLAYER.rpf`, `EXPLOSIONS.rpf` → `mods\x64\audio\sfx\`,
`update.rpf` → `mods\update\`, `x64a…w.rpf` / `common.rpf` → `mods\`). FiveM ne charge un `.rpf` de
`mods` que s'il a le même chemin que dans le jeu : un `.rpf` seul au nom inconnu est refusé.

## Compiler

Nécessite le SDK .NET 8 (`winget install Microsoft.DotNet.SDK.8`).

```bash
dotnet build -c Release
```

Le fichier `bin/Release/net48/FiveM-GFX-Manager.exe` est autonome (bibliothèques intégrées)
et fonctionne sur Windows 10/11 sans rien installer.

## Tests

```bash
cd tests
dotnet run -- <dossier-de-test>
```

## Structure

- `Core.cs` : logique (bibliothèque, packs, sauvegardes, ReShade)
- `MainForm.cs` : interface graphique (WinForms)
- `Program.cs` : point d'entrée
- `tests/` : tests de la logique
