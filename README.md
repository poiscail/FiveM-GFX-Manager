# FiveM GFX Manager

Logiciel Windows (aucune installation nécessaire) pour :

- garder une **bibliothèque de packs** : on lui donne des archives (`.zip`, `.7z`, `.rar`),
  des dossiers ou des `.rpf` (bouton ou glisser-déposer), il les range et les stocke ;
- choisir quel pack **installer** dans `FiveM.app` : packs **graphiques**, packs **son**
  et **remplacements d'armes** (plusieurs packs à la fois, conflits détectés) ;
- **ajouter des mods** à un pack existant, changer son type, le **renommer** (F2), le supprimer ;
- ranger les packs dans des **dossiers** affichés en tête de la liste : double-clic pour ouvrir,
  « Retour » pour revenir, **glisser-déposer** des packs sur un dossier pour les y ranger (ou sur
  « Retour » pour les en sortir), archives glissées sur un dossier importées dedans ; clic droit sur un
  dossier pour le renommer (F2) ou le supprimer ;
- **armes suivies** : le logiciel retient quelles armes chaque pack remplace ; si une arme est déjà
  remplacée par un pack installé, il demande pour chacune de **garder l'ancienne ou prendre la
  nouvelle** (le paquet de l'autre pack est refait sans elle, et elle lui revient quand on retire le pack) ;
- retirer un ou plusieurs packs (clic droit, touche Suppr) et remettre les fichiers d'origine ;
- installer un nouveau pack son ou d'armes : l'ancien qui touche les mêmes fichiers est retiré ;
- avant de coller un pack son : chercher dans tous les dossiers et sous-dossiers de FiveM.app (et dans les `.rpf`), ainsi que dans `GTA V\mods`, les sons d'armes déjà présents, et proposer de les mettre de côté (ils reviennent au retrait du pack) ;
- les sons du jeu d'un pack (`x64\audio\...`) sont copiés **directement dans `GTA V\x64\audio\sfx`**
  (FiveM ne les lit pas dans `FiveM.app\mods`, comme l'indiquent les tutos des packs son) ; les
  originaux sont sauvegardés et remis au retrait du pack ou par « Restaurer FiveM d'origine » ;
- bouton **« Couper les sons »** (barre de gauche) pour les serveurs « pure » (`sv_pureLevel`), qui refusent les fichiers du jeu modifiés et font planter FiveM au chargement : les packs son sont retirés d'un clic (sons d'origine de GTA V remis), puis **« Remettre les sons »** les réinstalle ;
- onglet **Mods** : une bibliothèque de mods à part (routes, tracers, effets de feu…) ; on les
  sélectionne et on les installe comme les packs, leurs `.rpf` vont dans `FiveM.app\mods` ;
- fichiers seuls rangés dans `citizen\common\data` comme le font les guides : BloodFX (`.ypt`) →
  `effects\`, kill effects (`timecycle…xml`) → `timecycle\`, roue des armes / position de la map
  (`.gfx`, `frontend.xml`…) → `ui\`, no water (`water.xml`) → `levels\gta5\` ; un dossier du pack
  nommé `effects`, `timecycle`, `ui`, `gta5` ou `common\data\…` donne la destination ;
- skins d'armes en `.ydr` / `.ytd` seuls (dossier `stream` des packs « FiveM ») : mis dans un paquet
  FiveM `mods\gfx_<nom>.rpf` avec `assembly.xml` (sans lui, FiveM ignore les `.rpf` de `mods`) ;
  un `.rpf` d'armes sans `assembly.xml` est mis dans un paquet lui aussi (même au chemin OpenIV
  `…\dlc.rpf\x64\models\cdimages\weapons.rpf`) ; une arme ou un pack de plusieurs armes (archives
  dans l'archive décompressées, fichiers en double gardés une fois) vont dans le même paquet ;
  pack à variantes (Glow / Regular, Variant 1 / 2…) : une fenêtre demande laquelle installer ;
- faire une sauvegarde complète d'origine (sans mods) de `citizen`, `mods`, `plugins` et la restaurer ;
- installer / désinstaller ReShade dans `FiveM.app\plugins` (téléchargement automatique + shaders) ;
- **« Mettre à jour ReShade »** : compare la version en place (lue dans `dxgi.dll`, même celle fournie par un pack) à la dernière de reshade.me et remplace seulement le dll — `ReShade.ini`, presets et shaders sont gardés ;
- onglet **QuantV** : trouve QuantV dans les packs de la bibliothèque et le remplace par la nouvelle
  version donnée (archive, dossier ou `.rpf`) ; un pack installé est réinstallé, les réglages `.ini`
  sont gardés et l'ancienne version est sauvegardée ;
  le bouton **Chercher la nouvelle version** prend tout seul la dernière archive QuantV arrivée dans
  Téléchargements (QuantV est payant : il faut la télécharger soi-même sur Patreon / Discord) ;
- onglet **Graphismes** : trois profils de réglages GTA (Performance / Équilibré / Qualité) écrits dans
  `%APPDATA%\CitizenFX\gta5_settings.xml` ; les réglages d'origine sont sauvegardés et peuvent être remis ;
- onglet **Presets ReShade** : liste les presets de `FiveM.app\plugins`, choisit le preset actif, le duplique,
  active le mode performance de ReShade et range les dossiers de shaders utilisés par aucun preset (restaurables) ;
- onglet **Profils** : enregistre la config actuelle (packs installés, preset, mode performance, graphismes)
  et la remet d'un clic ;
- taille des packs / mods installés et alerte quand ils pèsent plus de la moitié de la mémoire vidéo ;
- vider le cache FiveM ;
- **recherche** dans la liste (Ctrl+F : nom, type, dossier, commentaire), **tri** en cliquant sur les
  colonnes, **sous-dossiers** (un dossier créé dans le dossier ouvert), **favoris** en tête de liste,
  **note** de 1 à 5 étoiles (clic dans la colonne NOTE) et **commentaire** par pack ;
- **aperçu de l'installation** (fichiers remplacés / nouveaux, conflits) et **alerte fichiers suspects**
  (`.exe`, scripts, `.dll` autres que ReShade/ENB) avant d'installer ;
- **vérification après une mise à jour de FiveM / GTA V** : au démarrage, les fichiers des packs
  installés remis d'origine sont détectés et remis d'un clic (Outils → Vérifier les packs installés) ;
- **Lancer FiveM** : ouvre Steam s'il est fermé et attend qu'il soit connecté, puis lance FiveM
  (onglet Profils : « Appliquer et lancer FiveM ») ;
- **Annuler la dernière action** (Ctrl+Z) ;
- **exporter / importer** la bibliothèque, les profils et les réglages (Outils) ;
- **mise à jour automatique** : une nouvelle version publiée sur GitHub est proposée et installée d'un clic.

Formats de packs acceptés : arborescence `citizen` / `mods` / `plugins`, format GTA `x64/...`
ou `update/...` (copié dans `mods`, typique des packs son), ou fichiers `.rpf` seuls. Pour un `.rpf`
seul, le logiciel **cherche son chemin dans le dossier de GTA V** (trouvé via `CitizenFX.ini`, le
registre Steam / Rockstar / Epic) : par exemple `POLICE_SCANNER.rpf` → `mods\x64\audio\sfx\`.
Si le nom existe à plusieurs endroits (`dlc.rpf`), le dossier qui l'entoure dans le pack
départage (`mpheist\dlc.rpf`). Sans GTA V, les noms courants restent reconnus (`RESIDENT.rpf`,
`WEAPONS_PLAYER.rpf`, `EXPLOSIONS.rpf`, `update.rpf`, `x64a…w.rpf`, `common.rpf`).
Un `.rpf` remplace celui du jeu s'il est au même chemin ; un `.rpf` dont le nom n'existe pas dans GTA V
(ex. `aQuantV.rpf`, `WEAPON REPLACE.rpf`) est un pack pour FiveM et va à la racine de `mods`.

Un pack peut être installé **par-dessus ReShade** (même `plugins\dxgi.dll`) : la version de ReShade
est gardée et revient quand on retire le pack.

## Compiler

Nécessite le SDK .NET 8 (`winget install Microsoft.DotNet.SDK.8`).

```bash
dotnet build src/FiveMGfxManager -c Release
```

Le fichier `src/FiveMGfxManager/bin/Release/net48/FiveM-GFX-Manager.exe` est autonome
(bibliothèques intégrées) et fonctionne sur Windows 10/11 sans rien installer.

## Tests

```bash
cd tests/FiveMGfxManager.Tests
dotnet run -c Release -- <dossier-de-test>
```

## Structure

```
src/FiveMGfxManager/
  Core/   Core.cs (config, chemins FiveM / GTA V), Core.Archives.cs (décompression, normalisation),
          Core.Install.cs (packs), Core.Library.cs (bibliothèque), Core.Backup.cs, Core.Reshade.cs,
          Core.QuantV.cs (mise à jour de QuantV), Core.Graphics.cs (profils graphiques),
          Core.Presets.cs (presets ReShade, shaders), Core.Profiles.cs (profils complets, VRAM),
          Core.Maintenance.cs (aperçu, fichiers suspects, vérification / réparation, lancement)
  UI/     MainForm.cs (interface), MainForm.Tools.cs (pages Graphismes / Presets / Profils),
          MainForm.Extras.cs (recherche, tri, notes, outils, annuler, mise à jour),
          Theme.cs (couleurs), Widgets.cs (contrôles dessinés)
  Program.cs, Assets/icon.ico, app.manifest
tests/FiveMGfxManager.Tests/   tests de la logique
docs/LISEZMOI.txt              notice livrée avec le .exe
```
