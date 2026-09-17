# Audit de faisabilité et état du projet

**Projet :** Stellar Universe VR (`stellaruniversevr`)  
**Date de l’audit :** 17 septembre 2026  
**Périmètre :** dépôt Unity actuel + contrat API [action-api.json](https://stellar-universe.com/action-api.json) (v1.0.0, 2026-09-17) + sondage live `actionjs.php`  
**Verdict :** **scope B verrouillé** — client VR **jeu complet**, mêmes features que le web, UX **pont + table holo** (pas un compagnon, pas un RTS god-cam). Le dépôt est aujourd’hui un **prototype arrêté**, pas ce produit.

---

## 1. Synthèse

Stellar Universe VR n’est pas un jeu 4X autonome. C’est un **client Unity XR** destiné à se brancher sur le MMO navigateur déjà en production [Stellar Universe](https://www.stellar-universe.com) (v4.9.2 au 7 septembre 2026).

Le client VR n’a pas à réécrire le 4X : il doit **présenter toutes les features** (`actionjs.php`) depuis un **CIC immersif**. Vision : [`VISION-VR.md`](VISION-VR.md).

C’est la bonne nouvelle : la faisabilité métier ne dépend pas d’inventer un 4X from scratch.  
La mauvaise : le client Unity s’est arrêté au **login + spawn de soleils dans le void**, à l’opposé du format pont / table.

| Question | Réponse |
|---|---|
| Produit visé | **B** — client VR complet, multiplateforme, même compte que le web |
| Format UX | Pont / station + carte holo (inspiration [BattleGroup VR](https://www.meta.com/experiences/battlegroupvr/4459505850829471/)) |
| Le backend est-il vivant ? | Oui. Spec [action-api.json](https://stellar-universe.com/action-api.json) v1.0.0 |
| Le client VR est-il ce produit ? | Non. Login + notifications + soleils 1:1 dans le world |
| Maturité estimée | **~10–20 %** d’une slice « entrer dans le pont et donner un ordre » |
| Parité features web | **&lt; 5 %** câblées ; la cible est 100 % via alcôves |
| Risque principal | Prototype god-cam gelé + UX à reconstruire, pas seulement des DTO |

---

## 2. Ce qu’est le projet

### 2.1 Produit visé

MMO stratégie persistant :

- galaxie spiralée, systèmes et planètes
- empire, éthiques, traits d’espèce, autorité
- bâtiments planétaires (mines, ferme, chantier orbital, labo…)
- flottes **modulaires** (cœur, armes, boucliers, cargo, colonie, modules spéciaux)
- combat au tour par tour **sur grille hexagonale**
- Stargate / missions (coloniser, explorer, attaquer, piller)
- chat, news, classements, événements live

Le site le présente comme un jeu **gratuit, navigateur**, avec app PC/mobile. Ce dépôt est la tentative **Quest / OpenXR**.

### 2.2 Stack observée

| Couche | Choix actuel |
|---|---|
| Moteur | Unity **2022.3.58f1** LTS, URP 14 |
| XR | OpenXR 1.14, XR Interaction Toolkit **3.0.3**, XR Hands, AR Foundation 5.1 (template Mixed Reality) |
| Cible build | Android ARM64, min SDK 32 — cohérent Quest |
| Identifiants | `productName: su`, `companyName: DefaultCompany`, bundle **`com.unity.template.mixedreality`** (jamais rebrandé) |
| Réseau | `HttpClient` GET vers `/actionjs.php?action=…` (spec : GET `$_GET` uniquement, url-encode, token en query) |
| JSON | `JsonUtility` + SimpleJSON (uniquement pour les traductions) |
| Scènes | `Boot` → `Game` |
| Code métier | `Assets/_Core/Scripts/` — **31 fichiers, ~1 610 lignes** |
| Tests / CI / README | Absents |

Le projet est parti du **Unity Mixed Reality Template** (passthrough Quest, coaching cards, plane detection). Ce n’est pas le bon cadre UX pour un space opera : beaucoup de restes MR traînent encore dans `Game.unity`.

---

## 3. État actuel — ce qui marche, ce qui est esquissé, ce qui n’existe pas

### 3.1 Historique git

14 commits, tous en 2025 :

| Période | Contenu |
|---|---|
| 13–15 mars 2025 | Auth, UI login, spawn soleil/planète, LOD lumière, GameState, DTOs univers/flottes |
| 19 avril 2025 | Musique + 5 types d’étoiles |
| 31 juillet 2025 | Commit `jsp` (matériau soleil bleu) — dernier changement |

Le dépôt GitHub privé a été créé le **28 août 2026** (import tardif). Aucune issue, aucune PR, aucun README. **Plus d’un an sans avancée** au moment de l’audit.

### 3.2 Boucle runtime aujourd’hui

```
Boot.unity
  MusicManager (DontDestroyOnLoad)
  ChangeScene.sceneName = "Game"  → charge Game immédiatement

Game.unity
  XR (template MR) + UI spatiale Login / Register
  AuthUI → User.TryLogin / TryRegister / LoginToken
  onLoggedIn → GameLoader.Run()
    GetSystems → Instantiate "Suns/Sun {type}"
    1 prefab planète par planète, position aléatoire autour du soleil
    User.FetchEmpire()
    GameState.Run()
      GetConfigs, news, chat, activités, empires
      coroutine ShipHandler toutes les 15 s (GetAllFleets)
        Spawn() / Update() encore vides
```

### 3.3 Matrice fonctionnelle

| Fonction | Client VR | Backend web |
|---|---|---|
| Login / register / token PlayerPrefs | Prototype | Oui |
| Clavier tactile VR | Esquisse (`TouchScreenKeyboard`) | n/a |
| Notifications toast | Oui | n/a |
| Musique d’ambiance | Oui | n/a |
| Traductions EN/FR | Fetch OK (SimpleJSON) | ~1 415 / 1 422 clés |
| Création d’empire / espèce | Non | Oui |
| Galaxie 3D (soleils par type) | 5 prefabs | 5 classes d’étoiles |
| Planètes visuelles | 1 prefab générique | 21 types |
| Positions planètes / orbites | Aléatoire | slots + type |
| HUD système / planète | Non | Oui |
| Bâtiments, mines, humeur, citoyens | DTO `Planet` seulement | Oui |
| Recherche | DTO cassé (`Dictionary` + JsonUtility) | 13+ techs |
| Construction de modules / flottes | Non | 40+ modules |
| Visualisation flottes | `Spawn()` vide | Oui |
| Déplacement flotte interpolé | Stub `desttime` | Oui |
| Combat hex | Non | Oui |
| Colonisation / attaque / pillage | Non | Oui |
| Chat (lire / envoyer) | API seulement, pas d’UI | Oui |
| News / activity feed | API seulement | Oui |
| Diplomatie / guerres / alliances | Non | Oui |
| Classement | Non | Oui |

### 3.4 Assets

**Utilisés**

- Template MR : UI spatiale poke/ray, XR Origin, input actions
- `_Core/Resources/Suns/` : blue, orange, red, white, yellow
- `_Core/Resources/Planets/Planet 1.prefab`
- Audio (~45 Mo) : ambient, hyperespace, laser, explosion, etc. — **presque tout hors musique est mort** (aucun script ne les joue)

**Présents mais non branchés au gameplay**

- `PolygonSciFiSpace` (~125 Mo, Synty) : hangars, tourelles, props — **zéro référence depuis `_Core`**
- Samples XRI / Hands Interaction / AR Starter
- `CoachingCardRoot` et textes tutoriel Quest (plane detection, passthrough)

**Manquants**

- Prefabs de vaisseaux / flottes
- Variantes de planètes
- UI in-game (carte, planète, flotte, recherche, chat)
- Locomotion à l’échelle galactique (téléport intra-système vs carte)

---

## 4. Architecture technique

### 4.1 Client

Singletons `MonoBehaviour` : `AuthManager`, `GameState`, `GameLoader`, `Notify`, `MusicManager`.  
Pas de `DontDestroyOnLoad` hors musique — acceptable tant qu’on reste sur `Game.unity`.

Les entités (`User`, `Empire`, `Universe`, `System_`, `Planet`, `Ship`, `ShipModule`, `Config`, `Chat`, `News`, `Activity`, `Trans`…) sont surtout des **DTO + `ActionJs.Call`**. Peu de comportement 3D.

`ActionJs` :

- base URL `https://www.stellar-universe.com`
- GET query string : action, params, **token** — **c’est le contrat officiel** ([action-api.json](https://stellar-universe.com/action-api.json) : PHP ne lit que `$_GET`, jamais un body POST)
- timeout 10 s
- convention d’erreur : body `error:…` (HTTP 200 même en échec)
- `UnityWebRequest` est écrit mais **jamais utilisé** (`IEnumerator getRequest` mort)
- **Manque** : url-encode, parse des succès `ok` / body vide, boot `GetMeEmpire` + `GetAllFleetsAround` + `GetResource&raw=1`

### 4.2 API (contrat 2026-09-17)

Source de vérité : **https://stellar-universe.com/action-api.json** — 147 actions, audience `unity-vr-client`. Code : `actionjs.php`.

Sondage live du 17/09/2026 (sans token) : site 200 ; `GetTranslations` JSON public ; `Login` sans params = body vide ; le reste `error:translation.error_not_logged`. Cohérent avec `auth.required_after`.

Points de contrat que le prototype ignore ou inverse :

| Spec | Client actuel |
|---|---|
| UnityWebRequest.Get + body texte | `HttpClient` + `EnsureSuccessStatusCode` (HTTP 200 ≠ succès, mais un vrai 4xx casserait) |
| url-encode toutes les valeurs | concaténation brute |
| Succès = JSON **ou** `ok` **ou** body vide | suppose toujours du JSON |
| Boot : GetConfigs → **GetMeEmpire** → GetSystems → **changesystem** → **GetAllFleetsAround** → **GetResource raw=1** | GetSystems → GetEmpire(user) → GetAllFleets 15 s |
| Token IP-bound, à minter **depuis le casque** | PlayerPrefs token, LoginToken au reboot (échoue si l’IP a changé) |
| `fleet.systemid` déjà = dest au départ du jump ; interpoler en snapshot local | `Ship.HandleMovement` ne lit que `desttime` |
| `changesystem` = caméra, `MoveFleet*` = vaisseaux | aucune des deux familles d’actions |
| Tick 1–3 s local / 2 s ressources | poll global 15 s |
| `JsonUtility` après check `error:` ; maps → Newtonsoft/SimpleJSON | `Config.Fetch` JsonUtility + `Dictionary` (nul) |

Sécurité : le token (et le mot de passe au login) **sont en query par design**. Ce n’est pas un REST moderne ; ne pas “corriger” en POST — le serveur n’en lirait rien. Mitiger : pas de log d’URL, HTTPS, relogin si IP casque ≠ IP de mint. Les dumps bcrypt dans les commentaires C# restent à retirer.

Les commentaires dans `Ship.cs` / `Empire.cs` contiennent des **dumps live**, y compris un **hash bcrypt**. À retirer du dépôt.

### 4.3 Scènes

- **Boot** : caméra classique (pas XR), lumière, musique, `ChangeScene("Game")` dès `Start`.
- **Game** : toute l’app. UI login/register spatiale, `Manager` (Auth + Loader + GameState), Notify, restes du template MR (`CoachingCardRoot`, “Tap anywhere on surface”).

---

## 5. Faisabilité

### 5.1 Ce qui rend le projet viable

1. **Le jeu existe déjà.** Économie, combat, flottes, persistance, i18n, événements : le web les porte. Le VR n’a pas à simuler le tick serveur.
2. **Unity 2022 LTS + XRI 3 + OpenXR** est un socle Quest encore défendable en 2026 (à planifier une montée 6.x plus tard, pas bloquant pour une slice).
3. **Login + spawn d’étoiles** prouvent que le pipe Auth → API → Instantiate 3D fonctionne.
4. **Le contenu audio et Synty** est déjà là pour une V1 visuelle (hangar, vaisseaux, SFX combat) dès qu’on les branche.
5. Cible unique **Quest ARM64** : scope hardware raisonnable.

### 5.2 Ce qui le rend risqué

| Risque | Impact | Pourquoi |
|---|---|---|
| Dérive API | Élevé | Client figé mars–juillet 2025, web en v4.9.2 (sept. 2026). Chaque DTO peut déjà être faux. |
| API GET + secrets en URL | Accepté par contrat | Spec Unity : token (et login) en query, hash **lié à l’IP**. Ne pas passer en POST. Logger les URL reste interdit. |
| `JsonUtility` vs JSON réel | Élevé | Config, modules, politiques : données critiques illisibles. |
| Échelle spatiale | Élevé | `position = (x*100, random, y*100)` + **tous** les systèmes instanciés. Une galaxie réelle explose CPU/GPU Quest et la locomotion. |
| Combat hex en VR | Élevé | Cœur du game web. Porter ça en 3D/XR est un **sous-projet UX** à part, pas un port écran. |
| Template Mixed Reality | Moyen | Coaching cards à retirer. Le passthrough Quest sert le **mode quart** (VISION-VR §2.1), pas le tutoriel plane. |
| Polling 15 s `GetAllFleets` | Moyen | Pas de websocket. Latence, batterie, rate-limit PHP. |
| `HttpClient` + `ConfigureAwait(false)` | Moyen | Fragile sous IL2CPP Android ; UnityWebRequest est le chemin standard. |
| Identité store | Bloquant store | Bundle template, `DefaultCompany`, `ForceInternetPermission: 0`. |
| Dette prototype | Élevé | Bugs d’index flottes, caches `id=0`, notify fermé trop tôt — la slice actuelle n’est pas robuste. |

### 5.3 Décision produit (2026-09-17)

**A. Compagnon VR** — **rejeté.** Ce n’est pas le jeu.

**B. Client VR jeu complet** — **retenu.** Parité `actionjs.php`, multiplateforme, **format pont + table holo** ([VISION-VR.md](VISION-VR.md)). Faisable parce que le serveur existe ; coûteux parce que **toute l’UX actuelle (soleils dans le void, UI login template MR) n’est pas le produit**.

**C. 4X VR autonome** — hors sujet. Pas de second simulateur.

**Verdict de faisabilité : B = oui**, à condition de (1) coller au contrat GET, (2) poser le CIC avant les features, (3) traiter le hex comme plateau sur la table, pas comme un shoot. Le risque n’est plus « trop d’ambition vs compagnon » : c’est **reconstruire le point de vue** tout en portant 147 actions par alcôves.

---

## 6. Bugs et dettes qui cassent déjà la prototype

1. **`GameState.HandleUpdateShip`** indexe `ships[i]` avec `i` = index du tableau **nouveau**, pas le slot existant. Écrasements, NRE, vaisseaux fantômes.
2. **`Ship.Spawn()` / mouvement** : vides. Le poll de 15 s ne produit rien de visible.
3. **`JsonUtility` + `Dictionary`** dans `Config` : researches / shipstats toujours nuls.
4. **Constructeurs `Ship` / `System_`** appellent `Set(id, this)` **avant** désérialisation → cache d’`id == 0`.
5. **`AuthUI.Login/Register`** ferme la notification **avant** la fin de l’async.
6. **`GameState.Stop()`** met `Loading` au lieu de `Stopped` → la coroutine ne s’arrête pas proprement.
7. Tableau flottes **hardcodé à 1 000**.
8. **Planètes** : chargement par type commenté ; positions `Random.insideUnitSphere`, pas d’orbite, pas de slot.
9. **Soleils** : offset Y aléatoire → la galaxie n’est plus un disque lisible.
10. **`ChangeScene`** charge `Game` dès `Start` si `sceneName` est non null — Boot n’est pas un vrai splash XR.
11. **AuthManager** n’est pas persisté ; token seulement en `PlayerPrefs` (clair).
12. Restes **Coaching Cards** MR dans la scène de jeu.

---

## 7. Écart vs le jeu web (ce qu’il reste à porter)

D’après le site et les ~1 400 clés i18n, une parité minimale demanderait au client VR :

1. Onboarding création d’empire / espèce / éthiques  
2. Carte galaxie **streamée / LOD** (pas un Instantiate global)  
3. Fiche système + planète (ressources, bâtiments, file)  
4. Actions : upgrade, recherche, construire module, créer flotte, voyager, coloniser  
5. Représentation 3D des flottes + interpolation `from → dest` jusqu’à `desttime`  
6. Combat : **plateau hex sur la table holo** (pas un dogfight)  
7. Alcôves comms / diplomatie / stargate / shop — mêmes actions, meubles différents  

Sans suivre **action-api.json** (GET, boot VR, interpolation `from`/`desttime`, poll `GetResource`), chaque écran restera un GET fragile de plus. Un REST POST versionné n’existe pas ; ne pas l’attendre.

---

## 8. Reprendre ce proto ou Unity 6 neuf ?

**Ne pas « continuer » ce projet comme s’il était le jeu.**  
`Game.unity` est un **template Mixed Reality** + un spawn d’étoiles 1:1. La vision (pont + table + quart) est l’inverse. Les ~1 610 lignes `_Core` sont un **spike** (auth, DTOs, `ActionJs` GET) — pas une base à étendre (`HandleUpdateShip`, `JsonUtility`+`Dictionary`, `HttpClient`).

**Ne pas jeter l’art.** Soleils, audio, `PolygonSciFiSpace`, le contrat API, les leçons d’auth : ça se **copie**.

| Option | Verdict |
|---|---|
| Étendre `GameLoader` / la scène actuelle | **Non.** On sculpterait le mauvais lieu. |
| Upgrader **ce** repo 2022.3 → Unity 6 in-place | **Non.** Taxe packages (AR Foundation, XRI, URP) **plus** le template MR à vider. |
| Rester en 2022.3, nouvelle scène `Bridge` | Possible pour un week-end. **Mauvais pari produit** : Meta reco Unity **6.1+** / OpenXR ; Oculus XR plugin **deprecated** ; Depth API / occlusion passthrough (quart qui n’avale pas tes mains) = **Unity 6**. |
| **Nouveau projet Unity 6 LTS, VR (pas MR), voler les assets** | **Oui.** C’est la reco. |

### Reco concrète

1. Hub : **Unity 6 LTS** (≥ 6000.0.66, **6.1+** si possible). Template **VR**, pas Mixed Reality. Build profile **Meta Quest**, OpenXR + `com.unity.xr.meta-openxr` (passthrough / quart). Bundle `com.stellaruniverse.vr` dès le jour 1.
2. Importer : `_Core/Resources/Suns|Planets`, `_Core/Audio`, `PolygonSciFiSpace`. Pas `MRTemplateAssets` tutoring, pas `Samples` Hands AR.
3. **Réécrire** `ActionJs` (UnityWebRequest, url-encode, parse spec) et les DTO utiles. Ne pas git-mv les bugs.
4. Première scène = **pont vide + XR Origin + table cube**. Pas de `GetSystems` Instantiate world.
5. Garder `stellaruniversevr` 2022.3 en **référence** (branche / archive), ou écraser `main` une fois le 6 LTS bootable — pas les deux stacks en parallèle longtemps.

Le proto a prouvé : l’API répond, le login VR marche, les prefabs soleil existent. Sa dette (scène, réseau, JSON, identifiants template) coûterait plus cher à soigner qu’un projet propre calé sur le pont.

---

## 9. Score d’état

| Critère | Score | Commentaire |
|---|---|---|
| Clarté du concept | 8/10 | B + pont/table tranchés ([VISION-VR.md](VISION-VR.md)) |
| Avancement client | 2/10 | Auth + soleils dans le void — pas encore le CIC |
| Qualité code / robustesse | 3/10 | Prototype, bugs d’index, JSON cassé |
| Fit XR / Quest | 3/10 | XRI là ; le format produit n’est pas dans la scène |
| Fit backend | 7/10 | Contrat action-api.json v1.0.0 ; prototype encore à côté |
| Contenu art / audio | 5/10 | Synty + SFX là, à brancher sur le pont |
| Gouvernance (docs, tests, CI) | 3/10 | AGENTS + vision + audit ; toujours pas de tests |
| Faisabilité scope B (jeu complet, UX pont) | 6/10 | Oui si CIC d’abord, alcôves ensuite ; hex = plateau |

**Note globale de maturité : 2,5 / 10 — prototype de faisabilité, pas une pre-alpha jouable.**

---

## 10. Conclusion

Le projet est **le même Stellar Universe**, vu du **fauteuil de commandement**. Faisable : serveur + 147 actions + pack intérieur.  
Pas prêt : le prototype spawn des soleils dans le vide, comme un RTS. La suite n’est pas « plus de DTO », c’est **entrer dans le pont**, poser la table, donner un ordre.

Détail du format : [`VISION-VR.md`](VISION-VR.md).
