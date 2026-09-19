# AGENTS.md — Stellar Universe VR

Tu es l’agent de **Stellar Universe VR** : client Quest / OpenXR du MMO [stellar-universe.com](https://www.stellar-universe.com).  
Même jeu, même compte, même API que le web — **autre corps**.

Ce dépôt est un **jeu en production**, pas un proto, pas un spike, pas une démo technique. Chaque scène, mesh, lumière, shader, son et bouton doit pouvoir figurer dans un trailer Quest.

---

## Livrable art (non négociable)

Pour **chaque** objectif / feature / fix visuel : **générer et committer** tout ce qui manque pour que ça tienne debout **maintenant** — textures, materials, shaders, meshes, VFX, audio, prefabs `Resources/`, `.meta` Unity.  
**Interdit** : livrer du code qui suppose un asset « plus tard », un rose magenta / Unlit/Color par défaut, un cube gris, une sphère blanche, un `Shader.Find` sans fallback art soigné, ou une scène qui « marchera quand le pack art arrivera ».  
Si ça n’existe pas dans le dépôt → **on le fabrique** (procédural Editor, PNG/shader maison, mesh soigné). Pas de ticket « art TBD ».

---

## Perfs (non négociable)

Cible **Quest** (Android ARM64, thermals réels). Toute feature se conçoit **avec** le budget, pas après.

- Penser draw calls, overdraw, fill-rate, GC, allocations `Update` / poll réseau, particules, lights, shadows, `Find*` / `GetComponent` en boucle.
- Préférer pooling, dirty-flag / events, poll ActionJs cadencé, LOD / culling, un seul système extérieur partagé (pas N clones), materials partagés, pas de `new Material` / `Instantiate` massifs par frame.
- Extérieur / hublots : **un** `SystemExterior` partagé (pas un diorama par vitre). **Une** hull procédurale par flotte (`ShipHullBuilder`), materials partagés, pas de `new Material` par primitive et par frame.
- Si un choix est beau mais casse 72 FPS casque → on choisit autrement ou on allège. Documenter le trade-off si doute.

---

## Barre visuelle (non négociable)

- **POV VR** : le joueur *habite* un lieu (sas, menu, pont). Jamais une caméra RTS, jamais un Canvas Screen Space Overlay comme produit, jamais un menu 2D flottant « Unity UI par défaut ».
- Références : [BattleGroup VR](https://www.meta.com/experiences/battlegroupvr/4459505850829471/), Elite Dangerous (CIC), Homeworld (table), AAA Quest (Asgard’s Wrath / Red Matter pour la matière et la lumière).
- Recette d’un plan qui en jette : **bloom maîtrisé**, émissifs cyan / ambre, métal brossé, hublots = espace, poussière volumétrique, holographie animée (scanlines, fresnel, rotation lente), audio d’ambiance. Pas de directional unique + skybox default.
- Flottes par les hublots : **coque 9×9** (`Core.Vfx.ShipHullBuilder` + `Resources/Ships/`), un type de module = une silhouette (moteur, arme, cargo…), **volume** et liaisons lisses — pas deux sphères, pas un empilement de cubes. Ta propre hull est **cachée** à bord (`BridgeViewRig`).
- Interdit : placeholders gris, coaching MR, passthrough par défaut, gizmos laissés dans la build, logs d’URL, « cube Table » sans matériau.
- Packs art externes (Synty, Suns, audio…) s’intègrent **dans** cette barre s’ils arrivent — ils ne la remplacent pas et on n’attend pas après eux pour que le pont soit déjà beau.

---

## Produit (verrouillé)

**Scope B** : parité features avec `actionjs.php` (147 actions). Pas un compagnon, pas un 4X autonome.

**Format** : tu habites un **CIC**. Carte = table holo à portée de main.  
**TP de vue** (détail [`docs/VISION-VR.md`](docs/VISION-VR.md) §3.2) : n’importe lequel de **tes** vaisseaux (tu es sur **son** pont) ; à défaut, n’importe quelle **planète** → **fausse station en orbite**, pas la surface. Hublots = ce focus (`changesystem` / `changeplanet`). Les vaisseaux bougent avec `MoveFleet*`, pas le joueur.

**Passthrough Quest** = mode **quart** (long jump / bataille) : pièce réelle + petit cluster holo, features réduites. Le pont reste le jeu complet.

**Multiplateforme** : token minté **sur le casque** (lié à l’IP). Ne pas coller un token web.

Contrat API — lire `protocol` + `auth` + `response` **avant** de coder :  
https://stellar-universe.com/action-api.json

Vision / échelles : [`docs/VISION-VR.md`](docs/VISION-VR.md), [`docs/SCALE.md`](docs/SCALE.md).

Langue joueur : **français** (i18n EN/FR via `Trans` + `GetTranslations`).  
**Interdit** : littéraux joueur FR/EN dans UI / gameplay. Toujours `Trans.Get("key")`.  
Clé native du dump `GetTranslations` d’abord. **Si absente** : afficher la **clé** telle quelle + (Editor only) append dans `Application.persistentDataPath/su-missing-trans-keys.txt` pour les rajouter côté serveur. Pas de fallback inventé dans le client. Build joueur : warning console seulement, **pas** de fichier.  
Code / IDs : **anglais**, namespaces `Core.*`.

---

## Flux scènes (POV VR)

Toujours **première personne casque**. Chaque scène a un XR Origin (rig template VR), une pièce éclairée, une raison d’être là.

| # | Scène | Rôle |
|---|--------|------|
| 0 | **Boot** | Mise sous tension du CIC. Logo / titre holo, spatialize l’audio, XR prêt, preload. **Pas** d’UI desktop. Enchaîne vers Menu. |
| 1 | **Menu** | Sas / observatoire. Le joueur est **debout dans la pièce**. Connexion / inscription / continuer = **console diegetic** (poke / ray XRI + clavier système Quest). Pas un pause menu 2D. |
| 2 | **Bridge** | Pont de commandement. Table holo, hublots = système focalisé (extérieur partagé), boot API après login. Locomotion **dans la pièce seulement**. |

`AuthManager` est `DontDestroyOnLoad` (créé au Boot). Le token ne traverse pas les machines : `LoginToken` échoue si l’IP a changé → retour Menu, relogin casque.

---

## Stack

- Unity **6.3 LTS** (`6000.3.x`), template **VR** (pas MR). OpenXR, Android **ARM64**, min SDK **32**, internet **ON**.
- Bundle : `com.stellaruniverse.vr`. Company `StellarUniverse`, product `Stellar Universe VR`.
- Gameplay uniquement dans `Assets/_Core/` (créer), namespaces `Core.*`.
- GET `https://www.stellar-universe.com/actionjs.php` seulement. **Jamais de POST JSON.** Url-encode. Parser le body **texte** : `error:` = échec (HTTP 200 ≠ succès). Succès = JSON, `ok`, `ok:sublight_no_crystal`, ou body vide.

---

## Conventions

- Nouveau gameplay : `Assets/_Core/Scripts/`, `Core` / `Core.App` / `Core.Entity` / `Core.UI` / `Core.Utils` / `Core.Vfx`.
- Ordre spatial : **1:1 = la pièce**. Système courant = **hublots**. Galaxie = **hologramme sur la table**. Constantes : `Core.Vfx.WorldScale` — contrat [`docs/SCALE.md`](docs/SCALE.md). Interdit : `Instantiate` 1:1 de toute la galaxie, `Random` pour l’univers, mètres inventés hors de `WorldScale`.
- Flottes : **lexique legacy API** — `fleets.id` / « fleet » = **un vaisseau** (une coque + **modules** 9×9). `ships[]` / `GetShipLayout` = **modules** de ce vaisseau (pas des vaisseaux frères). Historiquement « fleet of ships » ; aujourd’hui une entité `fleet` = un ship. Plusieurs `fleets.id` = plusieurs vaisseaux. Grille modules : `grid_x,grid_y` (cœur **4,4**). Snapshot : **`GetAllFleets` seulement** (jamais `GetAllFleetsAround`) puis filtre client — `userid` = moi, `systemid` / `dest` = système focalisé (présents et en approche). Spawn `FleetShipView` depuis `SystemExterior`. Textures : `Resources/Ships/`. Shader `SU/HullMetal`.
- UI joueur = objets de pièce (poke / ray). TextMeshPro + `Trans.Get("key")` uniquement. Missing = clé affichée ; log fichier **Editor only**.
- Prefabs runtime : `Resources/CIC/` (pièce) ; `Resources/Ships/` (coques).
- **Vue habitée** : persistée localement (`BridgeViewAnchor` / PlayerPrefs) — ship ou planet→station. Le serveur ne stocke que le système (god-cam web).
- Commits **descriptifs** (pas `wip` / `jsp`).

## Vérification

Pas de casque dans l’environnement agent par défaut. Compiler via **Unity CLI 6** (`unity run`).  
Si un flux XR n’est pas testable ici, **le dire**. Ne pas prétendre un Play Mode Quest.
