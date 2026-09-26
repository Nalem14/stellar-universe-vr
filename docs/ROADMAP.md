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
  - écran principal incurvé dans la cloison avant (P5.6) ;
  - **6 stations** en fer à cheval (Helm, Tactical, Engineering, Science, Comms, Ops), avec les consoles tournées vers l'avant pour leur opérateur et les répétiteurs orientés vers le captain ;
  - TP dans le champ de vision.
- Arm pads du fauteuil : raccourcis du captain (alerte, focus flotte, zoom holo).
- Toutes les alcôves migrées vers le kit, avec de vrais choix à la place des valeurs en dur.

### P2 — Crew NPC vivant
- Framework §2.4. Les 6 officiers sont des figures stylisées en uniforme, casque et visière, en mesh procédural, avec idle.
- Répliques : accusé d'ordre, échec, départ et arrivée, contact ou ennemi, bataille, cargo plein, file terminée, recherche terminée, idle chatter.
- Interaction : on pointe l'officier → il se tourne et son répétiteur s'ouvre face au captain → on choisit un ordre → réplique de confirmation.

### P3 — Création d'empire en VR ✅ (Editor ; création réelle non testée)
`CreateEmpire` livré côté serveur (`7580501`). Assistant en 6 étapes sur la console du sas (`UI/EmpireCreationWizard.cs`), drapeau peint comme `empireFlag.js` (`UI/FlagPainter.cs`) en aperçu et en hologramme.
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
| **Helm** | ✅ `MoveFleet*` (sublight / hyperspace explicites), `PrlBondFleetToSystem`, file d'ordres, `RenameFleet` (P4 / cale sèche). ✅ **Portail de Saut** (Editor ; aucun monde à portail sur le compte, saut non testé) : vaisseau à quai sur un de nos mondes équipés → autres mondes-portails cerclés de violet sur la table (`GetJumpgateDestinations`), option « Portail de Saut » en tête du pupitre ou menu du répéteur Helm, refus miroir du serveur (recharge, ressources d'origine) ; dehors : portail annulaire en orbite de nos mondes équipés (voyants violets / ambre en recharge), champ de pliage, traînée et éclair au départ, ouverture à l'arrivée. ✅ **Terminer le voyage** pour Nova (vaisseau en route sélectionné ou répéteur) — **bloqué serveur** : `SpeedupFleetTravel` sans handler. ✅ **Réserves d'astéroïdes en direct** (`GetSystemAsteroids`, testé) : jeton, arc, pupitre et répéteur, amas qui rétrécit sur la table et dehors, champ épuisé retiré. Helm complet côté VR. |
| **Tactical** | ✅ Combat sur la table (P5.5 H3a). ✅ Console **Armurerie** (Editor ; un recrutement réel) ouverte par l'officier Tactique, à bord comme en station : garnison (`RecruitTroop` par lots 1–500, coût / durée, lot en cours), défenses (`BuildDefenseUnit`), soute à troupes d'un vaisseau à quai (`LoadTroops` / `UnloadTroops`, non testé : aucun vaisseau avec soute), opérations (nos sièges + combats `GetMyBattles`, rejoindre sur la table, renfort `AddFleetToBattle` pendant la préparation). ✅ `SiegeWatch` : `CheckPlanetAttack` dès l'échéance d'un siège qui nous touche, répliques attaque / issue. ✅ Dans l'espace réel : plateformes de défense en orbite de nos mondes, navettes de troupes, bombardement de siège et riposte (plateformes), explosion du camp perdant ; à bord : salves et soute ; sur la table : anneau de siège + attache aux assaillants, étincelle de transfert. Reste : `GetPendingBattles` (planète seulement côté serveur), pirates |
| **Engineering** | ✅ Modèles de vaisseaux (Editor ; chargement non testé, aucun vaisseau à quai) : onglet « Modèles » de l'écran du hangar — enregistrer la disposition (clavier Quest), choisir un modèle le **projette** (coque holo dans le berceau, modules sur la table : vert en stock, rouge manquant), charger / supprimer en deux temps. ✅ Cale sèche (Editor) — on y entre à pied par une porte du pont, **seulement en station** au-dessus d'un de nos mondes : salle de contrôle + baie, vaisseau à 1:1 dans son berceau reconstruit à chaque pose (étincelles de soudure), table d'assemblage 9×9, caisse de module à attraper et poser, retrait, nouveau vaisseau depuis un ShipCore, destruction de module, renommage. Onglet Chantier : catalogue par famille avec prérequis, Construire / + File, module en cours + Nova, annulation de file. |
| **Science** | ✅ Laboratoire (Editor) — porte bâbord de la cloison arrière, ouverte depuis un vaisseau comme depuis une station : arbre de recherche en **constellation de cristaux** sur un arc autour du joueur (racines au sol, techs profondes au-dessus ; filaments allumés quand les prérequis sont remplis, règles `GetConfigs.researchs`) ; écran d'analyse (prérequis, déblocages tirés des configs, coût / durée du niveau suivant) ; l'échantillon vient au berceau → on le pose dans le **synthétiseur** (`ImproveResearch`, meilleur labo) ; recherche en cours dans le faisceau + anneau de progression, file sur les pads (arracher un cristal = `CancelQueuedResearch`), Nova (`SpeedupResearch`) ; réplique « recherche terminée ». Contrats ✅ (Editor) : tableau des primes dans le labo (`GetBounties` / `ClaimBounty` / `CompleteBounty`), remise : serveur + client exigent une flotte empire dans le système cible (`bounty_need_fleet_onsite`). Anomalies ✅ (Editor) : lues une fois par système visité (le serveur en fait apparaître à la lecture), jeton gyroscope sur la table, vaisseau scanneur déposé dessus → devis des gains au pupitre → `ScanAnomaly` ; ligne Science au répéteur, répliques détection / scan. Relevé planétaire ✅ (`GetPlanet`, répéteur Science). Science complète. |
| **Comms** | ✅ Console **Communications** (Editor ; lectures réelles, aucun envoi) ouverte directement par l'officier Comms : canal galactique (`GetChat` 3 s à l'écran seulement, `AddChat` + réponses `console:` / `pm_sent:`), privé (conversations, recherche de commandant, fil + envoi), courrier (réception filtrée, envoyés, lecture, répondre « Re: », écrire, suppression en deux temps) ; texte des joueurs affiché tel quel (pas d'injection de balises) ; `CommsService` : non-lus toutes les 15 s, réplique « transmission entrante » + balise pulsante au-dessus de l'officier Comms. ✅ **Base de la porte** (Editor ; porte réelle ouverte et refermée, envoi réel) : pièce à part par la porte centrale arrière du pont — galerie de commandement (pupitres physiques avec écrans sur bras, piédestal de composition, deux techniciens, baies serveurs, moniteur mural avec l'adresse de la base) au-dessus de la salle d'embarquement (chaussée, estrade, porte blindée des équipes, cargaisons, tourelles murales, marquages) ; composition animée (piste de 36 glyphes, six verrous, surge de l'horizon), activation extérieure en alarme rouge, six missions dont l'équipe traverse l'horizon (troupes, chariots, colons), journal des missions. ✅ **Chambre diplomatique** (Editor ; lectures réelles, aucun ordre envoyé) : hémicycle rond par la porte bâbord de la cloison arrière (aussi le bouton Diplomatie de la console Comms), placé au-dessus du vaisseau — ses grandes baies montrent le vrai système (extérieur partagé) sous un ciel d'étoiles commun au pont (`SpaceBackdrop`, un mesh, un draw call) ; orrery des empires (notre drapeau au cœur, les autres en anneau, filaments rouges pulsants en guerre / verts pour l'alliance, pointer un drapeau = dossier), un siège par empire avec son drapeau, bannières des membres et blason de l'alliance, lumières rouges en guerre. Dossier : relation (`GetRelation`), rang, déclaration de guerre avec exigences (planètes via `GetEmpirePlanets`, ressources), invitation / retrait. Chancellerie : conflits (score, paix proposée / acceptée / refusée / retirée, abandon ou capitulation en deux temps), alliance (description, promotion, rétrogradation, transfert, exclusion, candidatures, quitter / dissoudre) ou invitations + fondation, registre (candidater / retirer). `DiplomacyService` (30 s) : répliques Comms (guerre déclarée contre nous, offre de paix, victoire / défaite / paix, invitation, candidature) et teintes `DiplomacyIndex` rafraîchies. Reste : tester les ordres d'écriture avec un second compte (déclaration, paix, invitations) |
| **Ops** | ✅ Console Intendance (Editor) : `GetResource` raw sur toutes les planètes (`EconomyService`, 10 s) ; bâtiments avec devis serveur (améliorer / ajouter à la file / réduire), chantier actif + Nova, annulation de file (`id`) ; décisions horaires ; rapport ; `RenamePlanet` ; réplique « construction terminée ». Troupes et défenses avec quantités ✅ (→ Armurerie Tactical) |

### P5.5 — Refonte de la table holo (retour joueur 2026-09-25)
Conception : [`design/HOLOTABLE.md`](design/HOLOTABLE.md) (diorama 3D, viser→viser, confirmation sur la cible, étapes H1–H3).
La holomap est jugée illisible, peu intuitive et en dessous des pièces (cale sèche, labo), beaucoup plus simples à prendre en main. **Référence : BattleGroup VR** (table tactique : lecture immédiate, sélection et ordres directs à la main, feedback fort). Feature à ré-imaginer de zéro, pas à retoucher :
- lisibilité d'abord : une information par jeton, hiérarchie claire (étoile / planètes / vaisseaux / anomalies), labels toujours lisibles, moins de disques qui se recouvrent ;
- gestes explicites et découvrables (saisir un vaisseau → cibles valides surlignées, pupitre qui dit ce qui va se passer), pas de modes cachés ;
- échelle et mise en scène au niveau des pièces : matière, lumière, animation de « déploiement » de la carte ;
- galaxie ↔ système : transition compréhensible, repère « vous êtes ici » ;
- tester chaque étape à la capture POV avant de la valider.

Avancement : H1, H2a, H2b ✅ (Editor). **H3a combat ✅** (Editor, sur un état de bataille injecté ; bataille réelle non jouée) :
- le combat prend la table (le diorama s'efface, le plateau hexagonal se déplie depuis son centre) et la rend à la fin (bannière Victoire / Défaite, repli) ou au bouton « Quitter la table » ; un bouton pulsant « Rejoindre » reste sur le rebord tant que le combat dure ;
- viser→viser : cases atteignables en vert, compétence armée au pupitre → portée en rouge / vert / violet, aperçu de zone, arc de visée avec coût ou dégâts ; compétences sur soi immédiates ;
- chaque tir est joué **trois fois** : sur le plateau holo, entre les vrais vaisseaux dehors (faisceaux, torpilles, impacts, bouclier, explosion) et à bord du vaisseau habité (alerte rouge sur les bandeaux d'accent + klaxon, coups au but : fracas, lumières qui vacillent, étincelles au plafond ; nos salves : grondement et lueur aux hublots ; pas de secousse caméra) ;
- phase de préparation : Prêt / Retirer le vaisseau, compte à rebours.
**H3b ✅** (Editor, sur de vrais ordres) : la file d'ordres du vaisseau choisi (sinon du vaisseau habité) est un chemin 3D animé, avec une balise numérotée par cible (étape en cours en cyan, saut vers un autre système au bord de la table, retour de boucle plus pâle). Gâchette sur une balise → retirer l'étape, boucle, vider ; balise saisie et lâchée sur une autre planète / un autre champ → étape redirigée. **Même route dans l'espace réel** (tracé depuis le vrai vaisseau, balises à anneau et pilier sur les vrais astres, repère vert sur la cible visée à la table). Les jetons se déploient depuis l'étoile à leur première apparition dans un système.

### P5.6 — Écran principal (idée joueur 2026-09-25)
**Lot 1 ✅** (Editor ; vraies données, pas testé en casque) :
- **Pont refait** : octogone allongé, pans coupés, deux verrières de proue, soffite incliné, plafond à caissons avec puits de lumière au-dessus de la table, nervures du sol au plafond. Nouveau shader `SU/HullInterior`, éclairé par les lumières de la pièce via `RoomLightRig`. Collisions épaisses derrière chaque mur, et `FallGuard` (retour au dernier point sûr en cas de chute) dans toutes les pièces.
- **Écran incurvé** (`BridgeViewscreen`) :
  - caméra de coque 1152×400 à 24 Hz, coupée hors du champ de vision ou hors du pont ;
  - HUD rendu dans le flux : repères sur les astres et les contacts, ruban de cap, grille ;
  - carte de la cible (propriétaire, état, orbite, habitabilité, réserves, aperçu d'ordre), heure locale, transmissions non lues ;
  - boîtier propre, distinct des hublots : bezel, casquette d'émetteurs, piliers, barre d'état animée.
- **Régie de l'écran**, par ordre de priorité :
  1. alerte rouge (l'ennemi cadré, manche, coque) ;
  2. ce que le captain touche ou vise sur la holomap (planète, vaisseau, champ d'astéroïdes, anomalie), maintenu 8 s ;
  3. verrouillage au regard sur un repère ;
  4. destination en transit, ou cible travaillée (récolte, siège, exploration) ;
  5. au repos, la vue avant (vaisseau) ou la planète sous la station.
- **Mode vaisseau ou station** : cyan ou ambre, lumière du plafond plus chaude en station, planète sous la station à l'écran.
- **Couloir** (`CorridorRoom`) : le pont n'a plus qu'une sortie, à l'arrière. Toutes les pièces donnent sur la coursive (labo, cale sèche, diplomatie, quartiers, base Stargate au fond), avec hublots sur l'extérieur partagé. En sortant d'une pièce, on se retrouve devant sa porte dans le couloir.
- Reste : combat plan par plan, siège, arrivée / départ, demandes « À l'écran » à l'équipage, gestes (zoom, orbite), incrustation.

Remplacer les hublots avant par **un très grand écran incurvé** (viewscreen à la Star Trek Bridge Crew) qui rend une caméra dans le `SystemExterior` partagé. Par défaut : la vue **devant le vaisseau**. L'écran « réalise » ensuite la scène selon le contexte, comme un régisseur.
- **Rendu** : une caméra dédiée (RenderTexture ≈ 2048×768, 30–45 Hz sur Quest, sans post-processing propre, culling limité à l'extérieur) projetée sur une surface cylindrique courbée vers le captain ; cadre physique, bord lumineux, scanlines et léger fresnel ; hublots latéraux conservés (profondeur et parallaxe réelles), l'écran avant devient la « fenêtre intelligente ».
- **Modes de caméra (régie automatique, priorités)** : avant (repos) → **suivi de cible** (vaisseau visé à la table ou en combat : caméra épaule, cible cadrée, réticule et fiche) → **combat** (plan large des deux camps, coupe sur le tireur puis l'impact à chaque salve, ralenti sur une destruction) → **siège** (orbite de la planète, bombardement et riposte) → **arrivée / départ** (plan de sortie d'hyperespace, traînée de saut) → **événement** (anomalie détectée, contact hostile, navettes de troupes, construction terminée en orbite). Transitions en fondu « glitch holo » de 0,3 s, jamais de coupe franche pendant un geste.
- **Demander à l'équipage** : pointer un officier ou une ligne de son répéteur → « À l'écran » ; Tactique met la cible, Science l'anomalie ou la planète, Helm la destination, Comms le **correspondant** (portrait holo / drapeau d'empire pendant une transmission, appel de diplomatie ou de guerre), Ops une planète à nous (chantier, défenses). Réplique « À l'écran, Commandant. »
- **Gestes du captain** : pincer-étirer sur l'écran = zoom caméra, glisser = orbiter la cible ; bouton « Vue avant » sur l'accoudoir ; double pression = revenir à la régie auto. Épingler une vue (incrustation) pendant que la principale suit l'action.
- **Incrustations** : bandeau de cible (nom, coque / bouclier), compte à rebours d'assaut ou d'arrivée, vignette picture-in-picture (vue arrière, second vaisseau), alerte rouge qui teinte le cadre.
- **Perfs** : une seule caméra supplémentaire, résolution et fréquence adaptatives, désactivée quand l'écran sort du champ de vision ; aucune géométrie dupliquée (même extérieur que les hublots).
- Dépend de : `SystemExterior`, `CombatEvents` (tirs, sièges, transferts), `TacticalCommand.AimedTarget`, `BarkDirector` (répliques « à l'écran »).

### P6 — Méta
- ✅ **Quartiers du commandant** (Editor ; lectures réelles, `SetPolitics` testé aller-retour, aucun achat) : cabine au-dessus du vaisseau par la porte tribord de la cloison arrière, grande baie sur le vrai système. Bureau en bois sombre avec l'écran **Empire** incliné bas : identité (renommer, drapeau), autorité et éthiques (jetons, deux temps), politiques (8 catégories), espèce (type, traits, un jeton), journal de bord (`GetActivity`). Console **Progression** (niveau, objectifs du jour / semaine / mois, succès filtrés, événement et boss mondial) et **mur des trophées** (18 plaques). Console **Boutique** (boosters, consommables, cosmétiques, titres, packs et historique Nova). Plaque du bureau et vitrine = titre équipé, drapeau, couleur de flotte ; la couleur équipée teinte aussi **nos coques dehors**. Terminal **Comms** mural (la console Comms s'y ancre). Reste : recharge Nova depuis le casque (aucun chemin de paiement VR), lit / étagères perso.
- ✅ **Sas — accueil au login** (Editor ; vraies données) : le sas est refait en rotonde (mur courbe tourné, corniche et coupole à oculus ouvert sur les étoiles, nervures qui suivent le profil, grande baie panoramique sur une planète) ; terminal sur pied tourné. Panneau **Transmissions** sur un lutrin à gauche du terminal : événements (fin, faction, boss mondial) et actualités (image serveur, corps HTML converti, pages) — `GetGameAnnouncements` ; dernière actualité sous le nom du commandant — `GetLatestNews`. **Porte d'embarquement** à tribord : la franchir (ou son bouton) = Continuer.
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
