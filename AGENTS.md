# AGENTS.md — Stellar Universe VR

Tu es l’agent de **Stellar Universe VR** : client Quest / OpenXR du MMO [stellar-universe.com](https://www.stellar-universe.com).  
Même jeu, même compte, même API que le web — **autre corps**.

Ce dépôt est un **jeu en production**, pas un proto, pas un spike, pas une démo technique. Chaque scène, mesh, lumière, shader, son et bouton doit pouvoir figurer dans un trailer Quest. Si un asset manque, **on le fabrique** (shader, texture, VFX, mesh procédural soigné) — on ne pose pas un cube gris « pour plus tard ».

---

## Barre visuelle (non négociable)

- **POV VR** : le joueur *habite* un lieu (sas, menu, pont). Jamais une caméra RTS, jamais un Canvas Screen Space Overlay comme produit, jamais un menu 2D flottant « Unity UI par défaut ».
- Références : [BattleGroup VR](https://www.meta.com/experiences/battlegroupvr/4459505850829471/), Elite Dangerous (CIC), Homeworld (table), AAA Quest (Asgard’s Wrath / Red Matter pour la matière et la lumière).
- Recette d’un plan qui en jette : **bloom maîtrisé**, émissifs cyan / ambre, métal brossé, hublots = espace, poussière volumétrique, holographie animée (scanlines, fresnel, rotation lente), audio d’ambiance. Pas de directional unique + skybox default.
- Interdit : placeholders gris, coaching MR, passthrough par défaut, gizmos laissés dans la build, logs d’URL, « cube Table » sans matériau.
- Synty / Suns / audio du spike `Nalem14/stellaruniversevr` (archive) s’intègrent **dans** cette barre, ils ne la remplacent pas. On n’attend pas Synty pour que le pont soit déjà beau.

---

## Produit (verrouillé)

**Scope B** : parité features avec `actionjs.php` (147 actions). Pas un compagnon, pas un 4X autonome.

**Format** : tu habites un **CIC**. Carte = table holo à portée de main.  
**TP de vue** (détail [`docs/VISION-VR.md`](docs/VISION-VR.md) §3.2) : n’importe lequel de **tes** vaisseaux (tu es sur **son** pont) ; à défaut, n’importe quelle **planète** → **fausse station en orbite**, pas la surface. Hublots = ce focus (`changesystem` / `changeplanet`). Les vaisseaux bougent avec `MoveFleet*`, pas le joueur.

**Passthrough Quest** = mode **quart** seulement (long jump / bataille) : pièce réelle + petit cluster holo, features réduites. Le pont reste le jeu complet. Plus tard, pas maintenant.

**Multiplateforme** : token minté **sur le casque** (lié à l’IP). Ne pas coller un token web.

Contrat API — lire `protocol` + `auth` + `response` **avant** de coder :  
https://stellar-universe.com/action-api.json

Vision / audit : [`docs/VISION-VR.md`](docs/VISION-VR.md), [`docs/AUDIT-FAISABILITE.md`](docs/AUDIT-FAISABILITE.md).

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
| 2 | **Bridge** | Pont de commandement. Table holo (visuelle dès maintenant, jouable plus tard), hublots, boot API après login. Locomotion **dans la pièce seulement**. |

Plus tard (pas cette slice) : alcôves planète / chantier / comms, table jouable (`MoveFleet*`), **quart** passthrough.

`AuthManager` est `DontDestroyOnLoad` (créé au Boot). Le token ne traverse pas les machines : `LoginToken` échoue si l’IP a changé → retour Menu, relogin casque.

---

## Stack

- Unity **6.3 LTS** (`6000.3.x`), template **VR** (pas MR). OpenXR, Android **ARM64**, min SDK **32**, internet **ON**.
- Bundle : `com.stellaruniverse.vr`. Company `StellarUniverse`, product `Stellar Universe VR`.
- Gameplay uniquement dans `Assets/_Core/` (créer), namespaces `Core.*`.
- GET `https://www.stellar-universe.com/actionjs.php` seulement. **Jamais de POST JSON.** Url-encode. Parser le body **texte** : `error:` = échec (HTTP 200 ≠ succès). Succès = JSON, `ok`, `ok:sublight_no_crystal`, ou body vide.
- `UnityWebRequest` + `Task.Yield` (pas `HttpClient`, pas `ConfigureAwait(false)`). **Ne jamais logger l’URL** (email / password / token).
- `JsonUtility` pour objets plats. Newtonsoft (déjà dans le projet) pour `GetConfigs` (maps). PHP `empty('0')` : ne pas envoyer `"0"` sur un required qui peut valoir 0 — omettre la clé.
- Spike 2022.3 à **ne pas merger** : `Nalem14/stellaruniversevr` (archive). On y prendra plus tard Synty / Suns / audio, **pas** `Game.unity` ni `GameLoader`.

---

## Première slice (rien d’autre côté features)

1. `Core.Utils.ActionJs` conforme à la spec (GET, encode, `error:`).
2. `AuthManager` DontDestroyOnLoad + `User.Login` / `LoginToken` / `Register` + `Empire.FetchMe` (`GetMeEmpire`).
3. **Boot** scène 0 — titre holo, pas de coaching MR / passthrough.
4. **Menu** scène 1 — console VR POV (Continuer / Connexion / Créer un compte).
5. **Bridge** scène 2 — XR Origin du template + **CIC réel** (plancher, parois, hublots, table holo avec VFX). Purger coaching MR du template.
6. Boot API **après login** : `GetConfigs` → `GetMeEmpire` → `GetSystems` (données seulement, **rien dans le world 1:1**) → `changesystem` vers un système possédé si possible → `GetAllFleetsAround`. Logs propres (noms d’actions, pas les URLs).
7. Identifiants Player + permission Internet Android.

**Stop après ça.** Pas de table holo *jouable*, pas de `MoveFleet`, pas de Synty, pas de quart passthrough.  
Le CIC et le menu doivent **déjà** avoir l’air du jeu final.

---

## Conventions

- Nouveau gameplay : `Assets/_Core/Scripts/`, `Core` / `Core.App` / `Core.Entity` / `Core.UI` / `Core.Utils` / `Core.Vfx`.
- Ordre spatial : **1:1 = la pièce**. Système courant = **hublots**. Galaxie = **hologramme sur la table**. Interdit : `Instantiate` 1:1 de toute la galaxie, `Random` pour l’univers.
- UI joueur = objets de pièce (poke / ray). TextMeshPro + `Trans.Get("key")` uniquement. Missing = clé affichée ; log fichier **Editor only**.
- Prefabs runtime : `Resources/CIC/` pour l’art généré du CIC.
- Commits **descriptifs** (pas `wip` / `jsp`).

## Vérification

Pas de casque dans l’environnement agent par défaut. Compiler via **Unity CLI 6** (`unity run`).  
Si un flux XR n’est pas testable ici, **le dire**. Ne pas prétendre un Play Mode Quest.
