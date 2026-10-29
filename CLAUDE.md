# FiveM GFX Manager — contexte du projet

Ce fichier résume tout ce qui a été fait sur ce projet (conversation avec Claude, 28/10/2026),
pour pouvoir continuer le travail en local. Langue du projet et de l'utilisateur : **français**.

**Consigne :** après chaque modification demandée et terminée, commiter et pousser sur GitHub
(`poiscail/FiveM-GFX-Manager`, branche `main`), et publier une nouvelle release si l'appli a changé.
Ne pas demander de confirmation : faire, puis dire ce qui a été fait.

## Demande de départ

Créer un logiciel Windows pour FiveM qui :

1. prend un **pack graphique compressé**, le **décompresse** et met les dossiers **au bon endroit**,
   en **remplaçant** les fichiers déjà existants ;
2. garde une **copie des dossiers d'origine sans mods**, pour pouvoir **retirer le pack** ;
3. permet d'**installer ReShade** sur FiveM.

Demande suivante : **ne rien avoir à installer** (pas de Python, pas de 7-Zip) → un `.exe` autonome.

## Historique

1. **Version 1 en Python/Tkinter** (abandonnée) : elle demandait Python et 7-Zip. Elle a été
   remplacée par la version C# ci-dessous et ne fait pas partie de ce dépôt.
2. **Version 2 en C# WinForms, .NET Framework 4.8** (version actuelle) : .NET 4.8 est déjà présent
   sur Windows 10 et 11, donc aucune installation n'est nécessaire. Les bibliothèques sont intégrées
   dans le `.exe` avec Costura.Fody, ce qui donne un seul fichier d'environ 1,8 Mo.
3. Compilée sous Linux avec le SDK .NET 8, puis testée sous Mono avec un écran virtuel (Xvfb) et
   des clics automatisés (xdotool).
4. Mise en dépôt git (branche `main`), avec ce fichier de contexte.

## Architecture

| Fichier | Rôle |
|---|---|
| `src/FiveMGfxManager/Core/Core.cs` | Classe statique `Core` (partielle) : config, chemins FiveM.app / GTA V (`GtaPath`), utilitaires + classes JSON (`Config`, `Manifest`, `PackInfo`, `BackupInfo`, `ReshadeState`) |
| `Core/Core.Archives.cs` | Décompression (SharpCompress), `NormalizePack`, chemin GTA des `.rpf` seuls (`GtaRpfIndex`, `LooseRpfPath`) |
| `Core/Core.Rpf.cs` | Lecteur / écrivain RPF7 non chiffré, paquets de mods FiveM (`assembly.xml`, `WriteModPackage`) |
| `Core/Core.Install.cs` | Installer / retirer un pack, conflits, `HandOverToReshade`, cache |
| `Core/Core.Library.cs` | Bibliothèque de packs (import, mods ajoutés, type, renommage, suppression) et dossiers de rangement (`LibraryPack.Dossier` + `bibliotheque\dossiers.json`, dans le logiciel seulement) |
| `Core/Core.Weapons.cs` | Armes suivies : `LibraryPack.Armes` (fichiers streamés des paquets `mods\*.rpf` à `assembly.xml`, calculé à l'import ou à la demande), `WeaponConflicts` (armes déjà remplacées par un pack installé), `ShareWeapons` à l'installation (`keepOld` : fichiers laissés à l'ancien pack ; sinon le nouveau gagne) qui refait nos paquets `gfx_*` sans les fichiers perdus (`FilterPackage`, paquets d'autres auteurs laissés tels quels avec un avertissement) et note `Manifest.Exclues` (fichier → pack qui le garde) ; `GiveBackWeapons` au retrait rend les fichiers |
| `Core/Core.PureMode.cs` | Serveurs « pure » : `CutSounds` retire les packs son installés et note leurs `LibId` dans `Config.SonsCoupes`, `RestoreSounds` les réinstalle (sauf conflit ou pack supprimé) ; bouton « Couper / Remettre les sons » de la barre latérale |
| `Core/Core.Scan.cs` | Recherche des sons d'armes déjà présents avant un pack son (`FindWeaponSounds`, lecteur RPF7 non chiffré `RpfEntries`) |
| `Core/Core.Backup.cs` | Sauvegarde et restauration d'origine |
| `Core/Core.Reshade.cs` | Téléchargement / installation / désinstallation de ReShade ; `ReshadeDllVersion` (version lue dans `plugins\dxgi.dll`), `UpdateReshade` (remplace seulement le dll si reshade.me a plus récent, refuse si un pack est par-dessus, marche aussi pour le ReShade d'un pack) |
| `Core/Core.QuantV.cs` | Onglet QuantV : repère QuantV dans les packs et le remplace par une nouvelle version |
| `Core/Core.Graphics.cs` | Profils graphiques Performance / Équilibré / Qualité dans `%APPDATA%\CitizenFX\gta5_settings.xml` (`FIVEM_GFX_SETTINGS` pour les tests), sauvegarde `graphismes\origine.xml` |
| `Core/Core.Presets.cs` | Presets ReShade de `plugins` (liste, preset actif, copie), mode performance, rangement des dossiers de shaders inutilisés (`shaders_ranges`) |
| `Core/Core.Profiles.cs` | Profils complets (`profils.json` : packs + preset + mode perf + copie des graphismes), taille installée, alerte VRAM (`FIVEM_GFX_VRAM` pour les tests) |
| `UI/MainForm.cs` | Interface WinForms (barre latérale + pages Packs / Mods / Sauvegarde / ReShade / QuantV, liste de packs + détails, journal). Les dossiers sont des lignes de la liste (`PackRow.Folder`, ligne `Up` « Retour » ; `folderFilter` = dossier ouvert) : double-clic pour ouvrir, glisser-déposer interne (`PackDrag`) pour ranger. Les opérations tournent en arrière-plan (`Task.Run`) |
| `Core/Core.Maintenance.cs` | Aperçu d'installation (`PreviewInstall`), fichiers suspects (`SuspiciousFiles`, `PackSuspects` mis en cache dans `LibraryPack.Suspects`), vérification après une mise à jour du jeu (`Manifest.Tailles` = tailles écrites, `CheckInstalledPacks`, `RepairPack` ; `.ini/.txt/.cfg/.json/.log` ignorés), `LaunchFivem` |
| `UI/MainForm.Tools.cs` | Pages Graphismes / Presets ReShade / Profils (classe partielle) |
| `UI/MainForm.Extras.cs` | Recherche (Ctrl+F), tri des colonnes (`sortColumn`), étoiles cliquables (`NoteColumn`), favoris / commentaire, détails Note / Installation, vérification au démarrage (`StartupChecks`), menus Lancer FiveM / Outils, annuler (Ctrl+Z), export / import, mise à jour du logiciel |
| `UI/Theme.cs`, `UI/Widgets.cs` | Couleurs du thème sombre, contrôles dessinés à la main (boutons, segments, `DarkListView`…) |
| `Program.cs` | Point d'entrée : DPI, TLS 1.2/1.3, styles visuels |
| `FiveMGfxManager.csproj` | net48, WinExe, SharpCompress 0.50.7, Costura.Fody 6.2.0, versions des dépendances System.* fixées |
| `app.manifest`, `Assets/icon.ico` | Manifeste Windows (asInvoker), icône |
| `tests/FiveMGfxManager.Tests/` | Programme de test en .NET 8 qui compile les fichiers `Core/` (141 vérifications) |
| `docs/LISEZMOI.txt` | Notice livrée dans le zip de la release |

### Fonctionnement

- **FiveM.app** : détecté dans `%LOCALAPPDATA%\FiveM\FiveM.app`, sinon `X:\FiveM\FiveM.app`.
  Un dossier est jugé valide s'il contient `citizen`, `data` ou `plugins`.
- **Données du logiciel** : `%APPDATA%\FiveMGfxManager\`
  (on peut changer ce chemin avec la variable d'environnement `FIVEM_GFX_DATA`, utilisé par les tests)
  - `config.json` : chemin FiveM, pack installé, état de ReShade
  - `sauvegarde_origine\` : copie complète de citizen/mods/plugins + `info.json`
  - `packs_installes\<id>\manifest.json` + `fichiers_remplaces\`
  - `reshade_sauvegarde\` : fichiers écrasés par ReShade (par exemple le `dxgi.dll` d'un pack)
- **Installer un pack** : décompression (SharpCompress, qui gère zip/7z/rar ; extraction séquentielle
  pour les archives solides et les 7z) dans un dossier temporaire. `FindPackRoot` cherche ensuite le
  dossier le moins profond qui contient `citizen`, `mods` ou `plugins`. Copie dans FiveM.app : un
  fichier qui existe déjà est d'abord sauvegardé puis remplacé. Le manifeste liste les fichiers
  ajoutés, les fichiers remplacés et les dossiers créés. En cas d'erreur, tout est annulé.
  **Plusieurs packs peuvent être installés en même temps** (`Config.PacksInstalles`). Un pack qui
  touche un fichier déjà modifié par un autre pack est refusé (le message nomme l'autre pack).
  L'interface propose de remplacer un pack « Graphique » déjà installé.
- **Bibliothèque** (`bibliotheque\<id>\pack.json` + `contenu\`) : les packs importés (archive,
  dossier, `.rpf` seul, glisser-déposer) y sont stockés déjà normalisés en arborescence
  citizen/mods/plugins. Chaque pack a un type (`Graphique`, `Son`, `Armes`) et une liste de mods
  ajoutés. `NormalizePack` accepte : une racine citizen/mods/plugins ; un format GTA
  (`x64/...` ou `update/...` avec des `.rpf`, typique des packs son) → copié dans `mods\` ;
  des `.rpf` seuls → leur chemin GTA. `GtaPath` trouve GTA V (`FIVEM_GFX_GTA` pour les tests, sinon
  `IVPath` de `FiveM.app\CitizenFX.ini`, sinon registre Rockstar / Steam / Epic ; dossier valide s'il
  contient `x64a.rpf`) et `GtaRpfIndex` liste ses `.rpf` (sauf `mods\`). `LooseRpfPath` cherche le
  nom dans cet index ; s'il y est plusieurs fois (`dlc.rpf`), on garde le chemin qui partage le plus
  de dossiers de fin avec le chemin du fichier dans le pack (`mpheist\dlc.rpf`), sinon refus
  « existe à plusieurs endroits ». Nom absent de GTA V (ex. `aQuantV.rpf`) → pack .rpf pour FiveM, posé
  à la racine de `mods` (mis dans un paquet s'il n'a pas d'`assembly.xml`). Sans GTA V : règles fixes (sons `SfxRpfs` → `mods\x64\audio\sfx\`,
  `update.rpf` → `mods\update\`, `x64?.rpf`/`common.rpf` → `mods\`). Les `.rpf` introuvables sont
  refusés, car FiveM ne charge un `.rpf` de mods que s'il a le même chemin que dans GTA
  (https://openiv.com/?p=1132). FiveM ne charge un `.rpf` de la racine de `mods` que s'il contient
  un `assembly.xml` (citizen-mod-loader-five, `ModPackage.cpp`) : les fichiers streamés seuls
  (`.ydr`, `.ytd`… `StreamExts`) vont dans un paquet `mods\gfx_<nom>.rpf`, et un `.rpf` de la racine
  sans `assembly.xml` dont la racine a des fichiers streamés est enveloppé dans un paquet
  (`Core.Rpf.cs` : lecteur / écrivain RPF7 OPEN, `WriteModPackage`). Ces règles valent aussi dans
  `mods\` d'un pack et au chemin OpenIV (`NeedsPackage` : tout `.rpf` de `mods`, paquet remis à la
  racine). Pack de plusieurs armes : archives imbriquées décompressées (`ExpandNestedArchives`, seulement
  sans dossier citizen/mods/plugins), doublons identiques gardés une fois (`SameContent`), versions
  différentes d'un même fichier streamé refusées, `.rpf` différents au même nom renommés `_2`. Les autres fichiers bruts
  (`.awc`, `.meta`…) sont refusés avec un message qui conseille OpenIV. Un pack au contenu identique (chemins + tailles) à un pack déjà dans
  la bibliothèque est refusé.
- **Ajouter des mods à un pack** : fusionne dans `contenu\` (refusé si le pack est installé ;
  l'interface propose retrait → ajout → réinstallation).
- Ancienne config (`PackInstalle`, un seul pack) convertie automatiquement au chargement.
- **Retirer un pack** : supprime les fichiers ajoutés, remet les fichiers remplacés, supprime les
  dossiers créés s'ils sont vides. Un dossier créé encore utilisé par un autre pack installé est
  transféré dans le manifeste de cet autre pack (pour être supprimé plus tard).
- **Retrait multiple** : la liste accepte plusieurs packs sélectionnés (Ctrl/Maj, clic droit, Suppr).
  À l'installation, `ConflictingPacks(libId)` donne les packs installés qui touchent les mêmes
  fichiers (ex. un autre pack son) : l'interface propose de les retirer avant (sinon refus).
- **Sons dans GTA V** : FiveM ne lit pas les sons du jeu placés dans `FiveM.app\mods\x64\audio`
  (tutos des packs son : « pas dans le dossier mods »). `InstallContent` met `Manifest.DossierGta =
  GtaPath()` quand le pack a des fichiers `mods/x64/audio/...` ; `Dest(m, fivemApp, rel)` les envoie
  alors vers `GTA V\x64\audio\...` (sauvegarde des originaux dans `fichiers_remplaces\`, comme les
  autres). Les chemins du manifeste restent `mods/x64/...`. Ancien manifeste sans `DossierGta` :
  tout reste dans FiveM.app. `RestoreOriginalBackup` remet d'abord les sons de GTA V des packs
  installés. `FindWeaponSounds` signale une copie restée dans `FiveM.app\mods\x64\audio` (déplaçable).
- **Fichiers seuls pour citizen** : sans dossier citizen/mods/plugins ni x64/update, `NormalizePack`
  range les fichiers non-.rpf via `LooseCitizenPath` dans `citizen/common/data/` (chemin `…common/data/…`
  du pack, sinon dossier `effects`/`timecycle`/`ui`/`gta5`, sinon règles : `.ypt` → effects,
  `.gfx`/`frontend.xml` → ui, `water.xml` → levels/gta5, `*timecycle*.xml` → timecycle). Seules les
  extensions `CitizenExts` sont prises (pas les lisezmoi). Ils peuvent être mélangés avec des `.rpf`.
- **Onglet Mods** : type `Core.ModType` ("Mod", hors de `PackTypes`). L'onglet Mods de `MainForm` est
  la même page que Packs (même `Body`), filtrée par `ModsView`. `NormalizePack(..., mod: true)` ne range
  pas de fichiers dans citizen : seuls les `.rpf` sont gardés (chemin GTA, sinon racine de `mods\`,
  même sans GTA V) ; un mod sans `.rpf` est refusé. Le type d'un mod ne se change pas.
- **Onglet QuantV** : `FindQuantV()` liste les packs de la bibliothèque dont un fichier contient « quantv ».
  `UpdateQuantV(source, …)` range la source (`StageSource`), exige un fichier QuantV, puis pour chaque pack :
  retire le pack s'il est installé, sauvegarde les anciens fichiers QuantV dans `quantv\<date>\`, supprime
  les anciens fichiers QuantV des dossiers fournis par la nouvelle version (ex. rpf renommé), copie le
  reste (les `.ini` déjà présents sont gardés : réglages), et réinstalle le pack.
  `FindQuantVDownload()` : QuantV est payant (pas de téléchargement auto) ; prend la plus récente archive
  ou `.rpf` « quantv » de Téléchargements (`FIVEM_GFX_DOWNLOADS` en test) plus récente que l'import du
  pack (date de son id) et que `Config.QuantVMaj` (mis à la fin de `UpdateQuantV`). Bouton « Chercher la
  nouvelle version ».
- **Restaurer d'origine** : supprime citizen/mods/plugins et recopie la sauvegarde. Remet aussi à
  zéro l'état du pack et de ReShade.
- **ReShade** : lit reshade.me et cherche `/downloads/ReShade_Setup_X.Y.Z.exe` (version normale,
  **pas** la version Add-on, que FiveM bloque). L'installateur contient un zip collé à la fin de
  l'exe, dont les positions internes sont relatives au début du zip : on calcule
  `start = eocd - cdSize - cdOffset` et on lit `ReShade64.dll` à partir de là. Ce fichier est copié
  en `plugins\dxgi.dll`. Les shaders viennent de
  `github.com/crosire/reshade-shaders/archive/refs/heads/slim.zip` (adresse modifiable avec
  `FIVEM_GFX_SHADERS_URL`, pour les tests) et vont dans `plugins\reshade-shaders\Shaders|Textures`.
  `ReShade.ini` est créé avec des chemins absolus et la touche Home (36). On peut aussi appliquer un
  preset (`ReShadePreset.ini`). On peut fournir son propre `ReShade_Setup.exe` ou `ReShade64.dll`.
- **Vider le cache** : `data\cache`, `data\server-cache`, `data\server-cache-priv`.
- **FiveM ouvert ?** On cherche un processus qui s'appelle exactement `FiveM` ou qui commence par
  `FiveM_`, sans compter ce logiciel lui-même.

## Bugs trouvés et corrigés

- La détection de FiveM se repérait **elle-même** (`FiveM-GFX-Manager` commence par « FiveM »).
  Corrigé : nom exact `FiveM` ou préfixe `FiveM_`, et on exclut le PID du logiciel.
- SharpCompress : `ExtractAllEntries()` ne marche que pour les archives solides et les 7z. Pour les
  autres, on extrait entrée par entrée.
- Conflits de versions entre les dépendances System.* sur net48 : versions fixées dans le csproj.
- Les liens dans le journal s'affichaient en bleu illisible : `DetectUrls = false`.
- Revue du 28/10/2026 : `.rpf` seuls rangés à plat dans `mods\` (inutiles pour FiveM) → chemin GTA ;
  doublons dans la bibliothèque et dans la liste des mods ; bibliothèque triée sur la date texte
  (jj/MM) → tri par id ; retrait d'un pack qui supprimait le `dxgi.dll` de ReShade ; pack laissé
  désinstallé si l'ajout de mods échouait ; import multiple arrêté au premier fichier refusé ;
  double-clic pendant une opération ; code dupliqué (`StageSource`, `ManifestPath`, `Touched`).

## Tests effectués

- `tests/` : zip, 7z et RAR5 ; remplacement puis retrait ; dossiers créés supprimés ; installation
  depuis un dossier ; restauration d'origine ; vrai téléchargement de ReShade 6.8.0 (dll de 5,2 Mo) ;
  shaders (archive locale simulée) ; désinstallation de ReShade qui remet le dll du pack ; cache ;
  refus d'un pack en conflit ; bibliothèque (import, packs son au format x64, `.rpf` seuls rangés à
  leur chemin GTA, `.rpf` inconnu refusé, doublon refusé, ordre, refus des `.awc` bruts, `.ydr`/`.ytd` mis dans un paquet FiveM lisible, `.rpf` sans assembly.xml enveloppé, pack multi-armes (zip dans le zip, doublons), variantes refusées, chemin OpenIV en dossiers, 3 packs
  installés ensemble, ajout de mods sans doublon, changement de type, suppression, retrait complet
  propre) ; sons d'armes trouvés avant install (sous-dossier de mods, dans un .rpf, GTA V\mods), mis de côté puis remis ; pack + ReShade sur le même `dxgi.dll` (dans les deux ordres, et ReShade désinstallé
  pendant qu'un pack est installé par-dessus) ; `.rpf` seuls rangés grâce à un faux dossier GTA V
  (`POLICE_SCANNER.rpf`, `mpheist\dlc.rpf`, `dlc.rpf` ambigu refusé) ; conversion de l'ancienne
  config. **Tout passe (50).**
- Interface testée sous Mono : installation et retrait d'un pack 7z, les 3 onglets, installation de
  ReShade.
- **Testé sur Windows 11 (28/10/2026)** : compilation avec le SDK .NET 8, tous les tests passent
  (y compris le vrai téléchargement des shaders depuis GitHub), logique du `.exe` net48 vérifiée
  (zip/7z/RAR5, installation + retrait), interface affichée correctement à 125 % (3 onglets).
  Le test des shaders exigeait plus de 5 `.fx`, mais la branche `slim` n'en a que 5 : test corrigé.
- Pas testé : avec un vrai FiveM installé et en jeu.
- Compilation : si le chemin du projet est trop long (> ~150 caractères), Costura échoue
  (« Path length is too large ») → compiler depuis un chemin court (ou `subst`), mais pas
  depuis la racine d'un lecteur (`W:\`), sinon MSBuild ne trouve plus les `.cs`.

## Compiler et tester

```bash
dotnet build src/FiveMGfxManager -c Release   # -> src/FiveMGfxManager/bin/Release/net48/FiveM-GFX-Manager.exe (+ .exe.config)
cd tests/FiveMGfxManager.Tests && dotnet run -c Release -- <dossier-vide-de-test>
```

Le dossier de test doit contenir `MonPack.zip`, `MonPack.7z` et `Rar5.rar`, ainsi que `src/` (un
pack décompressé avec `citizen/common/data/timecycle.xml`, `mods/vfx.rpf`, `plugins/dxgi.dll`,
`README.txt`). Pour tester les shaders sans GitHub, mettre `FIVEM_GFX_SHADERS_URL=file://.../slim.zip`.

## Limites connues / idées pour la suite

- Seuls `citizen`, `mods` et `plugins` de FiveM.app sont gérés (les `.rpf` de GTA remplacés passent
  par `mods\`). Modifier GTA V lui-même (OpenIV) n'est pas pris en charge.
- Archives protégées par mot de passe non prises en charge (il faut passer par « Dossier… »).
- ReShade installé après un pack qui a le même fichier (`dxgi.dll`) : au retrait du pack, le fichier
  reste et ReShade reprend la sauvegarde d'origine (`HandOverToReshade`).
- Pack installé **par-dessus** ReShade (même fichier) : autorisé. Le fichier de ReShade est
  sauvegardé comme un fichier remplacé ordinaire et noté dans `Manifest.SurReshade` ; il revient au
  retrait du pack. Si ReShade est désinstallé avant, le fichier du pack reste en place et la sauvegarde
  du pack reçoit l'original d'avant ReShade (ou le fichier passe dans `Ajoutes` s'il n'existait pas).
- Le `.exe` n'est pas signé : Windows SmartScreen affiche un avertissement au premier lancement.
- Les packs son/armes ne marchent que s'ils sont livrés au format `x64/...` / `update/...` ou en
  `.rpf` seul dont le chemin est trouvé dans GTA V (ou au nom connu, liste `SfxRpfs`, sans GTA V) :
  remplacer directement des fichiers bruts dans les `.rpf` de GTA (OpenIV) n'est pas géré.
- Idées : mise à jour de ReShade, sélecteur de dossier plus moderne, ordre de priorité entre packs.
