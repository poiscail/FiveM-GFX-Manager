# FiveM GFX Manager

Logiciel Windows (aucune installation nécessaire) pour :

- installer un pack graphique FiveM (`.zip`, `.7z`, `.rar` ou dossier) dans `FiveM.app`
  en remplaçant les fichiers existants (avec copie des fichiers remplacés) ;
- retirer le pack et remettre les fichiers d'origine ;
- faire une sauvegarde complète d'origine (sans mods) de `citizen`, `mods`, `plugins` et la restaurer ;
- installer / désinstaller ReShade dans `FiveM.app\plugins` (téléchargement automatique + shaders) ;
- vider le cache FiveM.

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

- `Core.cs` : logique (packs, sauvegardes, ReShade)
- `MainForm.cs` : interface graphique (WinForms)
- `Program.cs` : point d'entrée
- `tests/` : tests de la logique
