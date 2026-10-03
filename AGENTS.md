# AGENTS.md — Stellar Universe

Tu es l’agent de **Stellar Universe**, le client 3D multiplateforme du MMO [stellar-universe.com](https://www.stellar-universe.com) :
- **VR** : Meta Quest en autonome, et PCVR via OpenXR ;
- **PC** : Windows / Mac, clavier et souris en vue à la première personne ;
- **Mobile** : Android / iOS, tactile.

Même jeu, même compte, même API que le web — **autre corps**. Une seule base de code, un seul jeu complet sur les trois : aucune plateforme n’est une version « allégée ».

Ce dépôt est un **jeu en production**, pas un proto, pas un spike, pas une démo technique. Chaque scène, mesh, lumière, shader, son et bouton doit pouvoir figurer dans un trailer — en casque comme sur écran.

> Le dépôt et certains docs gardent le nom historique « VR » (`stellar-universe-vr`, `VISION-VR.md`, clés `vr.*`). Ce n’est pas un périmètre : tout ce qui est écrit ici vaut pour les trois plateformes.

---

## Livrable art (non négociable)

Pour **chaque** objectif / feature / fix visuel : **générer et committer** tout ce qui manque pour que ça tienne debout **maintenant** — textures, materials, shaders, meshes, VFX, audio, prefabs `Resources/`, `.meta` Unity.  
**Interdit** : livrer du code qui suppose un asset « plus tard », un rose magenta / Unlit/Color par défaut, un cube gris, une sphère blanche, un `Shader.Find` sans fallback art soigné, ou une scène qui « marchera quand le pack art arrivera ».  
Si ça n’existe pas dans le dépôt → **on le fabrique** (procédural Editor, PNG/shader maison, mesh soigné). Pas de ticket « art TBD ».

---

## Perfs (non négociable)

Le **plancher** est le **Quest** (Android ARM64, thermals réels, 72 FPS en stéréo). Ce qui tient sur Quest tient sur mobile et sur PC ; l’inverse est faux. Toute feature se conçoit **avec** ce budget, pas après.

- **Mobile** : même budget que Quest, plus la batterie et la chauffe (sessions longues, écran allumé). Pas d’effet réservé « parce que c’est un téléphone récent ».
- **PC** : peut monter la résolution et la qualité, jamais exiger plus que le Quest pour que le jeu soit beau.
- Penser draw calls, overdraw, fill-rate, GC, allocations `Update` / poll réseau, particules, lights, shadows, `Find*` / `GetComponent` en boucle.
- Préférer pooling, dirty-flag / events, poll ActionJs cadencé, LOD / culling, un seul système extérieur partagé (pas N clones), materials partagés, pas de `new Material` / `Instantiate` massifs par frame.
- Extérieur / hublots : **un** `SystemExterior` partagé (pas un diorama par vitre). **Une** hull procédurale par flotte (`ShipHullBuilder`), materials partagés, pas de `new Material` par primitive et par frame.
- Si un choix est beau mais casse 72 FPS casque → on choisit autrement ou on allège. Documenter le trade-off si doute.

---

## Barre visuelle (non négociable)

- **Toujours à la première personne**, sur les trois plateformes : le joueur *habite* un lieu (sas, pont, salles). Jamais une caméra RTS, jamais une god-cam, jamais un menu 2D « Unity UI par défaut ».
- **L’interface du jeu est dans la pièce** (écrans holo, pupitres, table, objets). Sur écran plat, l’overlay est limité au **strict nécessaire pour jouer** : réticule, invite contextuelle (« [E] Passer la porte »), joystick et boutons tactiles, rappel des touches. Aucune feature ne vit dans un Canvas Screen Space.
- Références : [BattleGroup VR](https://www.meta.com/experiences/battlegroupvr/4459505850829471/), Elite Dangerous (CIC), Homeworld (table), AAA Quest (Asgard’s Wrath / Red Matter pour la matière et la lumière).
- Recette d’un plan qui en jette : **bloom maîtrisé**, émissifs cyan / ambre, métal brossé, hublots = espace, poussière volumétrique, holographie animée (scanlines, fresnel, rotation lente), audio d’ambiance. Pas de directional unique + skybox default.
- Flottes par les hublots : **coque 9×9** (`Core.Vfx.ShipHullBuilder` + `Resources/Ships/`), un type de module = une silhouette (moteur, arme, cargo…), **volume** et liaisons lisses — pas deux sphères, pas un empilement de cubes. Ta propre hull est **cachée** à bord (`BridgeViewRig`).
- Interdit : placeholders gris, coaching MR, passthrough par défaut, gizmos laissés dans la build, logs d’URL, « cube Table » sans matériau.
- Packs art externes (Synty, Suns, audio…) s’intègrent **dans** cette barre s’ils arrivent — ils ne la remplacent pas et on n’attend pas après eux pour que le pont soit déjà beau.

---

## Plateformes et entrées (non négociable)

**Tout doit fonctionner sur les trois plateformes** : déplacement, rotation, interactions, saisie de texte, gestes de table, combat, salles. Une feature qui ne marche qu’en casque, ou qu’à la souris, n’est pas livrée. Contrat détaillé : [`docs/PLATFORMS.md`](docs/PLATFORMS.md).

- **Détection** : `Core.App.PcPlatformBoot` (`Mode` = `VR` / `Desktop` / `Mobile`, `IsFlatScreen`). Arguments de lancement `-vr`, `-desktop`, `-mobile` pour forcer. Un Quest est toujours VR.
- **Un seul chemin d’interaction par objet**, lu par les trois entrées :
  - objets 3D → `XRSimpleInteractable` (ou `PokeButton`, qui en porte un) ; le PC (`PcInteractionRaycaster`, réticule / souris, E ou clic) et le mobile (`MobileTouchController`, tap) déclenchent les mêmes `hoverEntered` / `selectEntered` / `selectExited` ;
  - écrans → Canvas World Space créé par `DiegeticUi.WorldCanvas` (caméra d’événements et raycasters réglés selon la plateforme) ;
  - portes → `RoomDoor` (proximité de la caméra + panneau de commande).
- **Les handlers ne lisent jamais `args.interactorObject`** (ni pose de manette, ni main) sans repli : sur PC et mobile, l’événement n’a pas d’interactor. Un geste qui demande une main en mouvement (saisir, glisser, deux mains) a **toujours** son équivalent écran plat : clic-glisser à la souris, appui-glisser au doigt, molette / pincement.
- **Pas d’`InputAction` liée seulement à `<XRController>`** : chaque action a son binding clavier/souris et son geste tactile (ou un bouton tactile).
- **Saisie de texte** : `TMP_InputField` partout. Casque → `HoloKeyboard` ; PC → clavier physique ; mobile → clavier natif. Tant qu’un champ a le focus (`PcPlatformBoot.IsTyping`), aucune touche de jeu ne réagit (déplacement, E, menu, raccourcis).
- **Un seul EventSystem** (`PcPlatformBoot.EnsureSingleEventSystem`) : jamais de `new GameObject("EventSystem")` ailleurs.
- **Placement du joueur** : toujours via `XrPlacement.PlaceHead` ; les contrôleurs PC / mobile suivent l’orientation qu’il impose.
- **Confort** : VR = locomotion dans la pièce, snap-turn, vignette (`ComfortSettings`) ; PC = ZQSD/WASD + souris, Tab libère le curseur ; mobile = joystick gauche, glisser à droite pour regarder, tap pour agir.

---

## Produit (verrouillé)

**Scope B** : parité features avec **toutes** les actions de `action-api.json` / `actionjs.php` (le nombre évolue à chaque release web — suivi action par action dans [`docs/PARITY.md`](docs/PARITY.md)). Pas un compagnon, pas un 4X autonome. Roadmap : [`docs/ROADMAP.md`](docs/ROADMAP.md).

**Format** : tu habites un **CIC**. Carte = table holo à portée de main.  
**TP de vue** (détail [`docs/VISION-VR.md`](docs/VISION-VR.md) §3.2) : le pont de n’importe lequel de **tes** vaisseaux, la rotonde de commandement d’une de **tes** stations orbitales, ou le sommet de la citadelle d’une de **tes** planètes (la cité vue d’en haut). Hublots = ce focus (`changesystem` / `changeplanet`). Les vaisseaux bougent avec `MoveFleet*`, pas le joueur.

**Passthrough Quest** = mode **quart** (long jump / bataille), casque seulement : pièce réelle + petit cluster holo, features réduites. Le pont reste le jeu complet. N’existe pas sur PC ni mobile.

**Comptes** : token minté **sur l’appareil** (lié à l’IP). Ne jamais coller un token web ni partager un token entre appareils.

Contrat API — lire `protocol` + `auth` + `response` **avant** de coder :  
https://stellar-universe.com/action-api.json

Vision / échelles : [`docs/VISION-VR.md`](docs/VISION-VR.md), [`docs/SCALE.md`](docs/SCALE.md). Plateformes : [`docs/PLATFORMS.md`](docs/PLATFORMS.md).

Langue joueur : **français** par défaut, dix langues via `Trans` + `GetTranslations`.  
**Interdit** : littéraux joueur dans l’UI, le gameplay **et les HUD écran plat** (invites, touches, boutons tactiles). Toujours `Trans.Get("key")` / `Trans.Format`.  
Clé native du dump `GetTranslations` d’abord. **Si absente** : afficher la **clé** telle quelle + (Editor only) append dans `Application.persistentDataPath/su-missing-trans-keys.txt` pour les rajouter côté serveur. Pas de fallback inventé dans le client. Build joueur : warning console seulement, **pas** de fichier.  
Code / IDs : **anglais**, namespaces `Core.*`.

---

## Client web de référence

Le client web (Phaser 3 + PHP) est disponible en local : **`/Users/thommy/Websites/stellar-universe`**.  
`action-api.json` donne le **contrat** ; le client web donne le **contexte** (intention, ordre des appels, cas d'erreur, feedback joueur).

**Règle** : avant de coder ou de modifier une feature, **lire son implémentation web** — UI (`assets/js/src/ui/`, `assets/views/*.hbs`), objet JS (`assets/js/src/objects/`, `scenes/`), handler (`actionjs.php`) et règles métier (`model/*.php`). Comprendre comment la feature fonctionne **avant** de l'adapter.

**Adapter, pas copier** : la jouabilité est repensée pour le POV du pont (stations, crew, table holo, gestes), et chaque geste a sa forme casque, souris et tactile. La compatibilité serveur, elle, reste **stricte** : mêmes actions, mêmes params, mêmes états, mêmes enchaînements.

**Deux clients indépendants** : le web est une référence **pour les développeurs uniquement**. Le jeu ne renvoie **jamais** le joueur vers le web pour jouer — pas de « faites-le sur le site », pas de lien ni de QR vers une feature. Chaque feature web a son équivalent dans le jeu (création d'empire, admin d'empire / alliance, shop…). S'il manque une action serveur, on la **spécifie** dans `docs/PARITY.md` et on la demande côté web ; on ne contourne pas par le site.
**Seule exception** : la plaque **Communauté** du sas (`Core.UI.CommunityPlaque`) — le site et l'invitation Discord (https://discord.gg/KFrJKQMEGH), QR + ouverture dans le navigateur de l’appareil. Rien d'autre.

Points d'entrée utiles :

| Chemin | Contenu |
|---|---|
| `actionjs.php` | Dispatch `addAction(name, params, fn)` — source de vérité des params réels |
| `action-api.json` | Contrat documenté (params, retours, `poll_after`, notes `vr_client`) |
| `model/*.php` | Règles métier par domaine (fleet, battle, construction_queue, stargate…) |
| `include/config.production.php` | Équilibrage (surchargé par la DB, exposé via `GetConfigs`) — ne jamais recopier ces valeurs en dur |
| `assets/js/src/` | Client Phaser : `scenes/`, `objects/`, `ui/`, `scripts/helper.js` (`doAction`) |
| `assets/langs/{en,fr}.json` | Clés i18n servies par `GetTranslations` |

Le serveur simule **paresseusement** : `GetAllFleets` traite les files d'ordres de flotte, `GetResource` accumule la production et résout les missions stargate. `GetAllFleets` est mis en cache **3 s** côté serveur — ne pas poller plus vite.

Le web évolue : avant chaque feature, `git log` / `git diff` sur le repo web pour repérer les changements d'API (params renommés, nouvelles actions, nouvelles erreurs). Le repo web est en **lecture seule** depuis ici : aucune modification sans demande explicite. Les clés i18n manquantes vont dans [`docs/i18n/missing-keys.md`](docs/i18n/missing-keys.md) (clé + FR + EN) pour intégration côté serveur.

Mettre à jour `docs/PARITY.md` à chaque feature livrée : `python3 docs/tools/parity.py` régénère la matrice (croise `ActionJs.Get("…")` du C#, `action-api.json` et le web) ; notes et statuts dans le script, écarts serveur à la main en fin de fichier.

---

## Flux scènes

Toujours **à la première personne** (casque, ou caméra FPS sur écran). Chaque scène a un XR Origin (rig template VR, réutilisé tel quel sur écran plat par `PcPlatformBoot.SetupDesktopRig`), une pièce éclairée, une raison d’être là.

| # | Scène | Rôle |
|---|--------|------|
| 0 | **Boot** | Mise sous tension du CIC. Logo / titre holo, audio spatialisé, plateforme détectée, XR prêt en casque, preload. **Pas** d’UI desktop. Enchaîne vers Menu. |
| 1 | **Menu** | Sas / observatoire. Le joueur est **debout dans la pièce**. Connexion / inscription / continuer = **console diegetic** : rayon / poke en casque, souris (curseur libre au sas) sur PC, tap sur mobile ; clavier holo / physique / natif. Pas un pause menu 2D. |
| 2 | **Bridge** | Pont de commandement. Table holo, hublots = système focalisé (extérieur partagé), boot API après login. Locomotion **dans la pièce seulement** (room-scale, ZQSD, joystick). |

`AuthManager` est `DontDestroyOnLoad` (créé au Boot). Le token ne traverse pas les machines : `LoginToken` échoue si l’IP a changé → retour Menu, nouvelle connexion sur l’appareil.

---

## Stack

- Unity **6.3 LTS** (`6000.3.x`), template **VR** (pas MR), OpenXR. Cibles : Quest (Android **ARM64**, min SDK **32**), PC Windows / Mac (Standalone, PCVR si casque), mobile Android / iOS. Internet **ON**.
- Bundle : `com.stellaruniverse.vr` (identifiant historique, ne pas changer : il porte les données des joueurs Quest). Company `StellarUniverse`, product `Stellar Universe`. Sur PC / Mac, les PlayerPrefs suivent le nom du produit : le renommage depuis « Stellar Universe VR » a demandé une reconnexion unique.
- Entrées : Input System (XRI en casque, `InputSystemUIInputModule` sur écran plat, EnhancedTouch sur mobile).
- Gameplay uniquement dans `Assets/_Core/` (créer), namespaces `Core.*`.
- GET `https://www.stellar-universe.com/actionjs.php` seulement. **Jamais de POST JSON.** Url-encode. Parser le body **texte** : `error:` = échec (HTTP 200 ≠ succès). Succès = JSON, `ok`, `ok:sublight_no_crystal`, `ok:conventional_drive`, ou body vide.

---

## Conventions

- Nouveau gameplay : `Assets/_Core/Scripts/`, `Core` / `Core.App` / `Core.Entity` / `Core.UI` / `Core.Utils` / `Core.Vfx`.
- Ordre spatial : **1:1 = la pièce**. Système courant = **hublots**. Galaxie = **hologramme sur la table**. Constantes : `Core.Vfx.WorldScale` — contrat [`docs/SCALE.md`](docs/SCALE.md). Interdit : `Instantiate` 1:1 de toute la galaxie, `Random` pour l’univers, mètres inventés hors de `WorldScale`.
- Flottes : **lexique legacy API** — `fleets.id` / « fleet » = **un vaisseau** (une coque + **modules** 9×9). `ships[]` / `GetShipLayout` = **modules** de ce vaisseau (pas des vaisseaux frères). Historiquement « fleet of ships » ; aujourd’hui une entité `fleet` = un ship. Plusieurs `fleets.id` = plusieurs vaisseaux. Grille modules : `grid_x,grid_y` (cœur **4,4**). Snapshot : **`GetAllFleets` seulement** (jamais `GetAllFleetsAround`) puis filtre client — `userid` = moi, `systemid` / `dest` = système focalisé (présents et en approche). Spawn `FleetShipView` depuis `SystemExterior`. Textures : `Resources/Ships/`. Shader `SU/HullMetal`.
- UI joueur = objets de pièce (poke / rayon / souris / tap). TextMeshPro + `Trans.Get("key")` uniquement. Missing = clé affichée ; log fichier **Editor only**.
- Prefabs runtime : `Resources/CIC/` (pièce) ; `Resources/Ships/` (coques).
- **Vue habitée** : persistée localement (`BridgeViewAnchor` / PlayerPrefs) — vaisseau, station ou planète → citadelle. Le serveur ne stocke que le système (god-cam web).
- Commits **descriptifs** (pas `wip` / `jsp`).

## Vérification

Pas de casque ni de téléphone dans l’environnement agent par défaut. Compiler via **Unity CLI 6** (`unity run`). Le **MCP Unity** (`unity mcp --project-path <repo>`) permet d'inspecter scènes, hiérarchie et console depuis l'agent.

- **PC** : le Play Mode de l’Editor démarre en mode `Desktop` (aucun loader XR actif) — c’est le chemin PC réel, testable ici (connexion, pont, salles, console sans exception).
- **VR** : en Editor seulement via le simulateur XR ; le Quest réel (gestes, 72 FPS, confort) n’est **pas** vérifiable ici — le dire.
- **Mobile** : build avec `-mobile` ou appareil réel ; le tactile n’est **pas** vérifiable dans l’Editor — le dire.
- Toute feature : passer en revue ses trois chemins d’entrée (voir [`docs/PLATFORMS.md`](docs/PLATFORMS.md)) avant de la déclarer finie.

Comparer le comportement avec le **client web** (mêmes appels, mêmes réponses, mêmes erreurs) avant de déclarer une feature finie.  
Si un flux n’est pas testable ici, **le dire**. Ne pas prétendre un Play Mode Quest ou un test sur téléphone.  
Captures de vérification (`capture_game_view` / `capture_scene_view`) : dossier **`Screenshots/`** à la racine du projet (ignoré par git), jamais `/tmp` ni `Assets/`.
