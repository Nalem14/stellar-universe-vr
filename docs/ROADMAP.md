# ROADMAP — Stellar Universe VR (2026)

> État au 2026-09-24. Remplace [`archive/AUDIT-FAISABILITE.md`](archive/AUDIT-FAISABILITE.md), qui est obsolète.
> Parité action par action : [`PARITY.md`](PARITY.md). Clés i18n à intégrer : [`i18n/missing-keys.md`](i18n/missing-keys.md). Règles : [`../AGENTS.md`](../AGENTS.md).

## Où on en est

Le pont est un **prototype**. On y trouve une holomap à la table, trois stations crew esquissées, des alcôves de démo et un combat hex. On en vise un **produit Quest** au niveau de Star Trek Bridge Crew / BattleGroup VR, en parité avec **toutes** les actions de `action-api.json`.

Aujourd'hui, 49 des 152 actions sont appelées (32 %), et une bonne partie ne l'est qu'en mode démo.

La plupart des défauts visibles viennent de quelques **causes structurelles** (§1), pas d'un manque de polish. On pose donc les fondations (§2) avant d'empiler de nouvelles features (§3).

Principes non négociables, rappelés ici :
- **La VR se suffit à elle-même.** Le jeu ne renvoie jamais le joueur vers le web. Chaque feature web a son équivalent diegetic à bord.
- **Le web est la référence de fonctionnement.** Avant de coder une feature, on lit son implémentation web. On adapte la jouabilité au pont ; le contrat serveur, lui, reste strict.
- **Art livré avec la feature**, dans le budget Quest (72 FPS).

---

## 1. Audit — causes racines

| # | Problème | Cause | Fichiers |
|---|---|---|---|
| A | La pièce, les tokens et la position du joueur sont réinitialisés à chaque poll | `FocusContext.ApplyFleetsBody` émet toujours `Changed` → `PutPlayerOnDeck`, `HoloZoneMap.Rebuild` (1 Canvas par label), `BridgeDressing.Apply` | `App/FocusContext.cs:168-250`, `App/BridgeViewRig.cs`, `Vfx/HoloZoneMap.cs`, `App/BridgeDirector.cs` |
| A2 | Une réponse de flotte sur deux est périmée | Le serveur met `GetAllFleets` en cache 3 s (`actionjs.php:622`), alors que la VR poll toutes les 2,5 s | `App/FleetPoller.cs` |
| B | Les boutons 3D sont moches ou écrasés | Cube étiré avec un TMP enfant (texte déformé), texture sur les 6 faces, parents non uniformes (ArmPad, table à 0,02 en Y), pas de `XRPokeFilter` | `Vfx/DiegeticUi.cs:433-585`, `App/CaptainCommandMode.cs`, `Vfx/AlcoveSystems.cs` |
| C | Des écrans sont dans le mauvais sens | Labels figés vers -Z sans tenir compte du yaw du meuble ; TP derrière le fauteuil ; panneau crew en billboard full-face qui suit la tête et flotte au-dessus de la carte ; officiers de dos | `Vfx/DiegeticUi.cs:547`, `Vfx/AlcoveSystems.cs:475`, `Vfx/CrewDialogue.cs:124,185`, `Vfx/BillboardFace.cs`, `Vfx/CrewStationsBuilder.cs` |
| D | Les shaders `SU/*` sont probablement absents du build | Chargés seulement par `Shader.Find`, pas dans Always Included | `Vfx/CicArtKit.cs:77`, `ProjectSettings/GraphicsSettings.asset` |
| E | Le fondu est invisible dans le casque | Il est rendu sur un Canvas ScreenSpaceOverlay | `Vfx/ViewFade.cs:30` |
| F | L'éclairage et le bloom n'ont pas d'effet sur Quest | L'URP Performance coupe le HDR et les lumières additionnelles ; post-processing désactivé sur la caméra XR ; pièce 100 % primitives runtime (pas de batching, pas de bake) | `Assets/Settings/Project Configuration/Performance URP Config.asset`, `Vfx/CicEnvironment.cs`, `Vfx/CicBridgeInterior.cs` |
| G | Le crew n'a aucun comportement de NPC | Pas de répliques, de sous-titres ni de portrait ; 4 chemins d'ordres parallèles ; résultat API ignoré ; `DestroyImmediate` pendant `onClick` | `Vfx/CrewDialogue.cs`, `Vfx/CrewStationsBuilder.cs` |
| H | Les alcôves sont de simples démos | Valeurs en dur (metalMine, combustion, Infantry, qty 1) ; réponses ignorées ou affichées en JSON brut | `Vfx/AlcoveSystems.cs` |
| I | Perfs | `XrSimulatorSceneHook` appelle `FindObjectsByType` à chaque frame pendant 12 s, device compris ; `GetComponentsInChildren` par frame (`SystemExterior`) ; scan des input devices par frame (`HoloMapController`) ; `EmissionPulse` via `r.material` ; ~14 point lights inutiles ; prefabs Resources inutilisés (613 KB) ; log `ActionJs` à chaque appel | multiples |
| J | Architecture | `Core.Vfx` mélange UI, réseau et gameplay ; god classes (HoloZoneMap 950 lignes, CrewDialogue 767, FocusContext 716) ; `BridgeDirector` câble tout à la main ; code mort (`ViewFleetOrders`, `ViewportSystemView`) | — |
| K | Bugs API | `GetEmpirePlanets` appelé sans `empire` (`BridgeViewTeleporter.cs:221`) ; l'ordre et son poll de suivi partent en parallèle ; `Trans` se déclare chargé même en cas d'échec | — |
| K2 | **Le combat hex est cassé** depuis la dernière version web | `HexBattleController.cs:310` envoie `action=move`, qui entre en collision avec `action=BattleDoAction`. Il faut utiliser `subaction` | `Vfx/HexBattleController.cs` |
| L | Parité | 49/152 actions (32 %) : alliance 6 %, guerre 11 %, stargate 14 %, empire 16 %, social 18 % | [`PARITY.md`](PARITY.md) |

### Dernières évolutions du serveur web à intégrer

- Nouvelles actions : `SpeedupFleetTravel`, `CancelQueuedBuilding`, `CancelQueuedShip`, `CancelQueuedResearch`. `PrlBondFleetToSystem` est maintenant documentée (`fleet`, plus `system` ou `pos`).
- **Vraies files d'attente** pour la construction, la recherche et le chantier (`model/construction_queue.php`, erreur `queueFull`). Les UI doivent afficher et éditer ces files, pas un job unique.
- `GetConfigs` expose `prlBond`, `sublightSpeedCap`, `hyperspaceCrystalCostPerDistance`, `systemTravelSecondsPerDistance` et `systemTravelDurationMin`. **Les ETA et les coûts affichés viennent de là**, jamais de constantes locales.
- `hyperspace` vaut 1 par défaut : il faut envoyer `hyperspace=0` pour voyager en sublight.
- Le Sensor Array devient le Deep Space Scanner, l'arbre de recherche est réorganisé, et la galaxie est placée selon les `x,y` de la DB.

### Modèle serveur à respecter

La simulation est **paresseuse** :
- `GetAllFleets` traite les files d'ordres de flotte.
- `GetResource` accumule la production et résout les missions stargate.

La VR doit donc appeler `GetResource raw=1` sur **toutes** ses planètes (csv, ≤ 50) à cadence régulière.

### Écarts serveur (détail dans [`PARITY.md`](PARITY.md))

- **Aucune action de création d'empire.** Un compte créé en VR est bloqué tant que `CreateEmpire` n'existe pas (spec dans PARITY).
- `ok:sublight_not_enough_modules` est absent de `response.success_forms`.
- Plusieurs libellés web sont codés en dur, sans clé i18n (voir [`i18n/missing-keys.md`](i18n/missing-keys.md) §4).

---

## 2. Architecture cible (fondations)

### 2.1 État et événements
- `FocusContext` émet des événements granulaires, calculés par diff de signature : `SystemChanged`, `FleetsDelta` (added / removed / moved), `ViewChanged`.
- **Aucun rebuild complet sur un poll.**
- `PutPlayerOnDeck` n'est appelé que sur un TP ou un `ViewChanged`.

### 2.2 Services — `Core.App.Services`
- **`OrderService` unique** : Issue → `ApiResult` → toast + bip + réplique crew → poll de suivi, lancé **après** la réponse.
  - Les codes `error:` bruts connus sont mappés sur des clés `Trans`. Sinon, on affiche le message serveur tel quel (il est déjà localisé).
  - Il remplace les 4 chemins actuels : `CrewDialogue.Issue`, `CrewOrderBridge`, `HoloFleetOrders.ResolveDrop` et les lambdas d'`AlcoveSystems`.
- **`PollScheduler`** :

  | Appel | Cadence |
  |---|---|
  | `GetAllFleets` | 3,5 s (au-dessus du cache serveur) |
  | `GetResource raw=1`, toutes les planètes | 10 s |
  | `GetMeEmpire` | 10 s |
  | `GetMailUnreadCount` | 15 s |
  | `GetChat` | 3 s, seulement si Comms est ouvert |
  | `GetBattleState` | 2,5 s, en combat |

  Le scheduler déduplique et coalesce les requêtes, et ne logue rien sur le réseau dans le build joueur. Règle AGENTS maintenue : `GetAllFleets` seulement, jamais `GetAllFleetsAround`.

### 2.3 Kit UI diegetic — `Core.UI`
Ce kit remplace les deux systèmes actuels de `DiegeticUi`.
- **`HoloScreen` + `ScreenMount`** :
  - La pose est relative au meuble, orientée vers un point d'usage explicite (siège de l'opérateur ou captain), avec une inclinaison de 15 à 25°.
  - **Pas de billboard sur les écrans.** Il reste réservé aux labels de la holomap, en yaw seulement.
- **Widgets UGUI World Space 9-slice** :
  - Button (poke **et** ray, `XRPokeFilter`), Toggle, Slider, Tabs, Stepper qty, Dropdown.
  - ScrollList virtualisée (pool), QueueList (réordonner / annuler), ProgressBar/Timer, Toast.
  - Saisie au clavier système Quest.
- **Boutons physiques 3D** : mesh biseauté généré en Editor (`.asset` committé), matériau partagé, course de poke, montés sur un `ButtonSocket` non scalé. Plus jamais d'enfant sous une primitive étirée.
- **Règles** : 1 Canvas par écran (pas par label), atlas partagé, TMP SDF.

### 2.4 Crew NPC — `Core.Crew`
- **`CrewMember`** : rôle, nom, portrait holo, idle. Son regard et son torse se tournent vers le captain quand il parle.
- **`CrewStation`** : console de l'opérateur, plus un **écran répétiteur** qui s'ouvre face au captain quand on interpelle l'officier.
- **`BarkDirector`** : un événement de jeu déclenche une réplique tirée au hasard, sans répétition, avec priorité et cooldown. Les répliques passent par une file : un seul officier parle à la fois.
- **`DialogueView`** : sous-titre ou bulle ancré au NPC, portrait, effet typewriter, bip radio procédural (`CicCue` étendu). Pas de voix. Les choix de réponse sont des ordres qui passent par `OrderService`.
- **Données** : `CrewLines.asset` (ScriptableObject) associe un événement à des clés `crew.<role>.<event>.<n>`. Tout le texte passe par `Trans.Get` (clés dans [`i18n/missing-keys.md`](i18n/missing-keys.md) §3).

### 2.5 Namespaces
- `Core.UI` (kit, écrans), `Core.Crew`, `Core.Holo` (carte, hex), `Core.App.Services`. `Core.Vfx` ne garde que le rendu pur.
- Un `BridgeInstaller` déclaratif remplace le `Start` monolithique de `BridgeDirector`.

### 2.6 Rendu Quest
- **La pièce devient un prefab généré en Editor.** Le builder procédural tourne en Editor ; les meshes sont combinés et les matériaux assignés (le `BridgeInterior.prefab` actuel a 160 slots vides). On y gagne le static batching, les lightmaps / l'AO bakés et une source d'art unique.
- **Profil URP Quest dédié** : bloom mobile validé sur device, 1 à 2 lumières temps réel, le reste en émissif + bake.
- `SU/*` en Always Included. Fondu XR via un quad devant la caméra.
- **Budget crew** : 6 officiers en SkinnedMesh léger (≤ 5k tris chacun), matériau partagé, animation procédurale (pas d'Animator lourd).

---

## 3. Phases

Chaque phase livre **l'art complet** de ce qu'elle touche, sans placeholder. Une phase donne un commit ou une PR, et met à jour [`PARITY.md`](PARITY.md).

### P0 — Stabilisation
- **K2 d'abord** (`subaction`), puis A, A2, D, E, F (profil URP), I, K.
- Suppression du code mort et des prefabs inutilisés dans `Resources/`.
- Clés VR alignées sur les clés natives ([`i18n/missing-keys.md`](i18n/missing-keys.md) §1).
- Compte sans empire : le Menu le détecte et affiche un état diegetic propre (`vr.menu.noEmpire`), **sans renvoi au web**.

### P1 — Kit UI et nouveau plan du pont
- Kit `Core.UI` (§2.3) ; pièce en prefab généré en Editor (§2.6).
- Disposition inspirée de Bridge Crew :
  - fauteuil du captain au centre-arrière, table holo devant lui ;
  - viewscreen au-dessus des hublots avant ;
  - **6 stations** en fer à cheval (Helm, Tactical, Engineering, Science, Comms, Ops), avec les consoles tournées vers l'avant pour leur opérateur et les répétiteurs orientés vers le captain ;
  - TP dans le champ de vision.
- Arm pads du fauteuil : raccourcis du captain (alerte, focus flotte, zoom holo).
- Toutes les alcôves migrées vers le kit, avec de vrais choix à la place des valeurs en dur.

### P2 — Crew NPC vivant
- Framework §2.4. Les 6 officiers sont des figures stylisées en uniforme, casque et visière, en mesh procédural, avec idle.
- Répliques : accusé d'ordre, échec, départ et arrivée, contact ou ennemi, bataille, cargo plein, file terminée, recherche terminée, idle chatter.
- Interaction : on pointe l'officier → il se tourne et son répétiteur s'ouvre face au captain → on choisit un ordre → réplique de confirmation.

### P3 — Création d'empire en VR
Dépend de `CreateEmpire` côté serveur (spec dans PARITY).
- Séquence diegetic dans le sas :
  - nom, drapeau holo 3D (fond + 3 formes / couleurs) ;
  - autorité et éthiques (max 2) ;
  - espèce (type, 2 traits positifs + 2 négatifs) ;
  - monde natal ;
  - profil du leader (bio, nom, titre, traits, préfixe des vaisseaux).
- Onboarding forcé comme sur le web (éthiques et espèce valides) avant d'accéder au pont.

### P4 — Holomap produit ✅ (Editor ; non testé en casque)
- ✅ Tokens mis à jour par diff incrémental : un poll ne redessine que les tokens de flotte dont l'état a changé ; changement de système / de vue = redessin complet.
- ✅ Zoom / pan : deux grips au-dessus de la table (écarter = zoomer, déplacer les deux mains = faire glisser la galaxie), ou stick droit (haut = zoom avant). Dézoomer au-delà de la vue système ouvre la galaxie ; zoomer au-delà de son niveau le plus proche revient au système.
- ✅ Galaxie complète sans plafond : tous les systèmes en **un** mesh (`SU/HoloStarField`, teinte par détenteur = territoires web), placés sur `visual_x/visual_y` comme le web. 24 tokens interactifs recyclés près du centre ; la cible d'un lâcher est l'étoile la plus proche de la main parmi **tous** les systèmes. Tes vaisseaux sont posés sur leur étoile et se glissent vers une autre pour un saut devisé.
- ✅ **Au lâcher : preview + confirmation** au pupitre : sublight, hyperspace ou Bond PRL, avec ETA et coût calculés depuis `GetConfigs` (distance PRL sur les coordonnées visuelles, comme le serveur).
- ✅ Trajectoires et file d'ordres éditable (`SetFleetOrderQueue`, `RemoveFleetOrderStep`, `ToggleFleetQueueLoop`).
- Reste : pinch en hand tracking (sans manettes) — à faire avec XR Hands en P7.

### P5 — Stations = domaines de jeu (parité)

| Station | Contenu |
|---|---|
| **Helm** | `MoveFleet*` (sublight / hyperspace explicites), `PrlBondFleetToSystem`, `SpeedupFleetTravel`, `SendFleetToJumpgate`, file d'ordres, `GetSystemAsteroids`, `RenameFleet` |
| **Tactical** | Combat hex sur la table (diff d'état au lieu d'un rebuild toutes les 1,5 s), `CheckPlanetAttack`, `GetPendingBattles`, `AddFleetToBattle` / `RemoveFleetFromBattle`, `LoadTroops` / `UnloadTroops`, stance, pirates |
| **Engineering** | Designer 9×9 en holo (`PlaceShipModule` / `RemoveShipModule`) ; chantier : `AddShip` avec choix du type, `AddToFleet` / `DelToFleet`, `SpeedupShipyard`, file + `CancelQueuedShip` |
| **Science** | `GetSystemAnomalies` / `ScanAnomaly`, `GetPlanet`, bounties ; arbre de recherche en holo (layout à relire dans `research.js`), file + `CancelQueuedResearch` |
| **Comms** | Chat, MP, mail (clavier Quest) ; relations ; guerres (déclaration avec exigences, paix, reddition) ; alliances complètes (création, invitations, candidatures, rôles, exclusion, transfert, dissolution) ; stargate (composition d'adresse, missions) |
| **Ops** | `GetResource` sur toutes les planètes ; bâtiments (choix, file + `CancelQueuedBuilding`, speedup) ; `AnswerPlanetDecision` ; troupes et défenses avec quantités ; `RenamePlanet` |

### P6 — Méta
- **Salle du conseil / bureau du captain**, accessible par TP : administration d'empire **complète** (identité, drapeau, espèce, autorité, éthiques, politiques, jetons).
- Progression, achievements, objectifs ; activity log lu par l'officier Comms ; shop Nova (`BuyShopItem` / `EquipShopItem`) ; annonces et événements saisonniers.
- Tutoriel diegetic guidé par le crew, calé sur les 16 étapes du web.
- Mode quart en passthrough.

### P7 — Audio et finition trailer
- Ambiance par zone ; SFX (hyperspace, alertes, dradis).
- Alertes rouge et ambre qui pilotent l'éclairage et le crew.
- Passe de perfs sur device.

---

## Vérification (toutes phases)

- Compilation via Unity CLI 6 / MCP Unity, puis captures Editor des stations, de la holomap et du crew.
- Comparaison avec le client web : mêmes appels, mêmes réponses, mêmes erreurs.
- Tout ce qui est XR ou device Quest est signalé **non testé en casque** tant qu'il n'a pas été validé sur casque.
