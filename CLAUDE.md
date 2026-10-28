# FiveM GFX Manager — contexte du projet

Ce fichier résume tout ce qui a été fait sur ce projet (conversation avec Claude, 28/10/2026),
pour pouvoir continuer le travail en local. Langue du projet et de l'utilisateur : **français**.

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
| `Core.cs` | Toute la logique, sans interface. Classe statique `Core` + classes JSON (`Config`, `Manifest`, `PackInfo`, `BackupInfo`, `ReshadeState`) |
| `MainForm.cs` | Interface WinForms en thème sombre : 3 onglets faits avec des boutons, journal, barre de progression. Les opérations tournent en arrière-plan (`Task.Run`) |
| `Program.cs` | Point d'entrée : DPI, TLS 1.2/1.3, styles visuels |
| `FiveMGfxManager.csproj` | net48, WinExe, SharpCompress 0.50.7, Costura.Fody 6.2.0, versions des dépendances System.* fixées |
| `app.manifest`, `icon.ico` | Manifeste Windows (asInvoker), icône |
| `tests/` | Programme de test en .NET 8 qui compile `../Core.cs` (18 vérifications) |

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
  Un seul pack à la fois : l'interface propose de retirer l'ancien avant d'installer le nouveau.
- **Retirer un pack** : supprime les fichiers ajoutés, remet les fichiers remplacés, supprime les
  dossiers créés s'ils sont vides.
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

## Tests effectués

- `tests/` : zip, 7z et RAR5 ; remplacement puis retrait ; dossiers créés supprimés ; installation
  depuis un dossier ; restauration d'origine ; vrai téléchargement de ReShade 6.8.0 (dll de 5,2 Mo) ;
  shaders (archive locale simulée) ; désinstallation de ReShade qui remet le dll du pack ; cache ;
  refus d'installer un deuxième pack sans retirer le premier. **Tout passe.**
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
dotnet build -c Release          # -> bin/Release/net48/FiveM-GFX-Manager.exe (+ .exe.config)
cd tests && dotnet run -- <dossier-vide-de-test>
```

Le dossier de test doit contenir `MonPack.zip`, `MonPack.7z` et `Rar5.rar`, ainsi que `src/` (un
pack décompressé avec `citizen/common/data/timecycle.xml`, `mods/vfx.rpf`, `plugins/dxgi.dll`,
`README.txt`). Pour tester les shaders sans GitHub, mettre `FIVEM_GFX_SHADERS_URL=file://.../slim.zip`.

## Limites connues / idées pour la suite

- Seuls `citizen`, `mods` et `plugins` de FiveM.app sont gérés. Les packs qui modifient GTA V
  (OpenIV, `update.rpf`, `x64*.rpf`) ne sont pas pris en charge.
- Archives protégées par mot de passe non prises en charge (il faut passer par « Dossier… »).
- Si on installe ReShade puis qu'on retire le pack, le `dxgi.dll` de ReShade peut être supprimé
  (il faut alors réinstaller ReShade).
- Le `.exe` n'est pas signé : Windows SmartScreen affiche un avertissement au premier lancement.
- Idées : glisser-déposer d'une archive, plusieurs packs en mémoire, mise à jour de ReShade,
  sélecteur de dossier plus moderne, vrai test sur Windows.
