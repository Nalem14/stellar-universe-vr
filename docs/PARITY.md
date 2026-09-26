# PARITY — Stellar Universe VR ↔ `actionjs.php`

Généré depuis `action-api.json` (158 actions), `actionjs.php` et un grep des deux clients. Référence web : `/Users/thommy/Websites/stellar-universe`. Roadmap : [`ROADMAP.md`](ROADMAP.md).

**Règle** : chaque feature livrée met à jour sa ligne. Avant de coder, lire l'implémentation web (colonne *Web*) + `model/*.php`. On adapte la jouabilité au pont VR ; le contrat serveur reste strict. La VR ne renvoie **jamais** au web.

## Légende

- **Statut** : `Branché` (appel fonctionnel en VR, UX à finir) · `Démo` (appelé mais valeurs en dur / réponse ignorée ou brute) · `À faire` · `Hors scope` (legacy / serveur only / interdit par AGENTS).
- **Station** : où la feature vit à bord. Helm, Tactical, Engineering, Science, Comms, Ops = stations du pont ; *Conseil* = salle du conseil / bureau du captain (admin empire, progression, shop) ; *Sas* = scène Menu ; *Holo table* = carte.
- **Phase** : voir [`ROADMAP.md`](ROADMAP.md) §3.
- **R/W** : lecture / écriture (heuristique sur le nom). `?` = param optionnel.

## Couverture

| Domaine | Appelées en VR | Total | % |
|---|---|---|---|
| Auth | 3 | 3 | 100 % |
| Méta / boot | 3 | 5 | 60 % |
| Caméra (vue) | 2 | 2 | 100 % |
| Galaxie | 8 | 8 | 100 % |
| Flotte | 19 | 23 | 83 % |
| Vaisseau / chantier | 12 | 13 | 92 % |
| Planète / bâtiments / recherche | 13 | 17 | 76 % |
| Combat | 11 | 14 | 79 % |
| Jumpgate | 0 | 2 | 0 % |
| Stargate | 7 | 7 | 100 % |
| Social (chat, mail) | 11 | 11 | 100 % |
| Guerre | 9 | 9 | 100 % |
| Alliance | 17 | 17 | 100 % |
| Empire / progression / shop | 24 | 27 | 89 % |
| **Total** | **139** | **158** | **88 %** |

Appelées par le client web : 139/158. « Appelée » ≠ « finie » : voir la colonne *Statut*.

## Auth

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `Login` | W | email, password | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché |  |
| `LoginToken` | W | token | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché | Token minté sur le casque, lié à l'IP |
| `Register` | W | email, password, username | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché |  |

## Méta / boot

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `GetConfigs` | R | — | `App/DiplomacyIndex.cs` +4 | `scripts/configs.js` | Système (boot) | P0 | Branché |  |
| `GetEventData` | R | — | `Stations/QuartersRoom.cs` | `ui/ProgressionWindowUI.js` | Quartiers du commandant | P0 | Branché | Onglet Événement : objectifs, boss mondial (PV, nos dégâts, tête du classement) |
| `GetGameAnnouncements` | R | — | — | `scenes/ui.js` | Sas (Menu) | P6 | À faire |  |
| `GetLatestNews` | R | — | — | — | Sas (Menu) | P6 | À faire |  |
| `GetTranslations` | R | — | `Utils/Trans.cs` | — | Système (boot) | P0 | Branché |  |

## Caméra (vue)

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `changeplanet` | R | id | `App/BridgeSystemLoader.cs` | `objects/planet.js` | Pont — TP de vue | — | Branché |  |
| `changesystem` | R | id | `App/BridgeSystemLoader.cs` +1 | `objects/star.js` | Pont — TP de vue | — | Branché |  |

## Galaxie

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `ClaimBounty` | W | bounty | `Stations/BountyBoard.cs` | `ui/BountiesWindowUI.js` | Tableau du labo | P5 | Branché |  |
| `CompleteBounty` | W | bounty | `Stations/BountyBoard.cs` | `ui/BountiesWindowUI.js` | Remise au tableau du labo ; serveur exige flotte empire dans `target_systemid` (`bounty_need_fleet_onsite`) ; `reward_credits` → cristal | P5 | Branché |  |
| `GetBounties` | R | — | `Stations/BountyBoard.cs` | `ui/BountiesWindowUI.js` | Tableau des contrats du labo, relu toutes les 20 s dans la salle (le serveur en génère quand < 4 ouverts) | P5 | Branché |  |
| `GetEmpirePlanets` | R | empire | `App/OwnedPlanets.cs` +1 | `ui/WarsWindowUI.js` | Système (boot) | P5 | Branché | `OwnedPlanets` : mes planètes fraîches (id, `systemid`, slot — web `c94c803`), au boot et après une fondation ; `GetSystems` en secours seulement |
| `GetPlanet` | R | id | `Stations/PlanetSurvey.cs` | `objects/planet.js` | Relevé planétaire Science (répéteur → écran face au captain), planètes du système en vue ; `user` jamais gardé | P5 | Branché |  |
| `GetSystemAnomalies` | R | systemid | `App/AnomalyService.cs` | `ui/StarWindowUI.js` | `AnomalyService` : une lecture par système visité (le serveur fait apparaître une anomalie à 45 % quand il n'y en a pas) ; titres par type (`anomaly_<type>`), pas le texte FR stocké | P5 | Branché |  |
| `GetSystems` | R | — | `App/BridgeSystemLoader.cs` +2 | `scenes/galaxy.js` | Holo table | P4 | Branché | Galaxie complète sur la table (LOD, territoires par détenteur) |
| `ScanAnomaly` | W | anomaly, fleet | `App/AnomalyService.cs` +1 | `ui/StarWindowUI.js` | Vaisseau scanneur (ScienceModule / SensorArray / DeepSpaceScanner) déposé sur le jeton, devis des gains au pupitre ; ou répéteur Science | P5 | Branché |  |

## Flotte

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddFleetOrderStep` | W | fleet, step | `Holo/OrderQueue.cs` +1 | `objects/fleet.js` | Helm | P4 | Branché | Étape JSON : `targetId` (planète / astéroïde) ; `moveToSystem` avec **`x`,`y`** (le serveur accepte aussi `targetX`/`targetY`) |
| `ClearFleetOrderQueue` | W | fleet | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché |  |
| `Colonize` | W | ship, planet | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Ops | `ship` = id du module `colonyShip` (legacy `ColonyShip` OK) ; planète libre, habitabilité ≥ 6 | Branché | `ship` = id du module `colonyShip` (legacy `ColonyShip` OK) ; planète libre, habitabilité ≥ 6 |
| `DepositCargo` | W | fleet, planet, mineral?, crystal?, biomass? | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Ops | P5 | Branché |  |
| `ExplorePlanet` | W | fleet, planet | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Science | P5 | Branché |  |
| `GetAllFleets` | R | — | `App/BridgeSystemLoader.cs` +3 | `scenes/galaxy.js` | Helm | P0 | Branché | Cache serveur 3 s → poll VR 3,5 s. Traite les files de flotte. `user` = PublicUser (id/username) ; cache fleets purgé au hit |
| `GetAllFleetsAround` | R | — | — | `scenes/galaxy.js` | Helm | — | Hors scope | Interdit en VR (règle AGENTS) — `GetAllFleets` + filtre client |
| `GetSystemAsteroids` | R | systemid | — | `scenes/system.js` | Helm | P5 | À faire |  |
| `HarvestAsteroid` | W | fleet, asteroid | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Engineering | P5 | Branché |  |
| `LoadTroops` | W | fleet, planet, troops | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché | Soute à troupes de l'Armurerie : `{type: qty}` ; réponse = unités déplacées → navettes dehors + étincelle sur la table |
| `MoveFleetToAsteroid` | W | fleet, asteroid, hyperspace? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | idem hyperspace |
| `MoveFleetToPlanet` | W | fleet, planet, hyperspace? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | idem hyperspace |
| `MoveFleetToSystem` | W | fleet, pos, hyperspace? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | Toujours `hyperspace` explicite (0 sous-lumière / 1 hyperespace) après devis au pupitre (`TravelPlanner`, formules serveur) ; `ok:sublight_*` → `ApiResult.NoticeKey` |
| `PrlBondFleetToSystem` | W | fleet, system?, pos? | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Helm | P4 | Branché | Devis portée / coût / recharge au pupitre (`TravelPlanner`, distance sur `visual_x/visual_y` comme le serveur), `fleet` + `system` + `pos` ; lâcher sur une étoile de la galaxie |
| `ProcessFleetOrderQueue` | W | fleet | — | — | Helm | — | Hors scope | Géré par cron + `GetAllFleets` ; pas d'UI |
| `RemoveFleetOrderStep` | W | fleet, stepIndex | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché | `stepIndex` 0-based : répéteur Helm, ou plaque ouverte sur une balise de la file (table) |
| `RenameFleet` | W | id, name | `Stations/DryDock.cs` | `objects/fleet.js` | Helm | P5 | Branché | Cale sèche, clavier Quest |
| `SetFleetOrderQueue` | W | fleet, queue, loop? | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché | Balise de la file lâchée sur une autre planète / un autre champ : on renvoie les étapes restantes (le serveur remet l’index à 0), champs du serveur conservés |
| `SpeedupFleetTravel` | W | fleet | — | `scripts/helper.js` | Helm | P5 | À faire |  |
| `ToggleFleetQueueLoop` | W | fleet, loop? | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché | `loop` explicite 0/1 |
| `UnloadTroops` | W | fleet, planet, troops | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché | Idem, vers la garnison de nos planètes seulement |
| `UpdateFleetDefendPosition` | W | id, position | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `WithdrawCargo` | W | fleet, planet, mineral?, crystal?, biomass? | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Ops | P5 | Branché |  |

## Vaisseau / chantier

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddShip` | W | type, planet | `Stations/ShipyardPanel.cs` | `objects/fleet.js` | Engineering | P5 | Branché | Onglet Chantier de la cale : catalogue par famille (shipstats), prérequis `requiert`, Construire / + File ; réponse texte ou JSON `queued` |
| `AddToFleet` | W | fleet, ship, planet? | `Stations/DryDock.cs` | `objects/fleet.js` | Engineering | P5 | Branché | `fleet=0` + ShipCore → JSON `{ok,fleet}` (systemid=planète) ; refuse notYourShip sans supprimer |
| `ApplyShipTemplate` | W | fleet, template | `Stations/BlueprintPanel.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché |  |
| `CancelQueuedShip` | W | id, queue_id | `Stations/ShipyardPanel.cs` | `scenes/planet.js` | Engineering | P5 | Branché | Param `id` (ligne planet_ship_queue) |
| `DelShip` | W | ship | `Stations/DryDock.cs` | `objects/fleet.js` | Engineering | P5 | Branché | Râtelier du hangar : destruction en deux temps |
| `DelToFleet` | W | fleet, ship | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `DeleteShipTemplate` | W | id | `Stations/BlueprintPanel.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché |  |
| `GetShipLayout` | R | fleet | `Stations/DryDock.cs` +1 | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché | Coque 1:1 dans la cale + hublots (`ShipHullBuilder`) |
| `GetShipTemplates` | R | — | `Stations/BlueprintPanel.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché |  |
| `PlaceShipModule` | W | ship, fleet, gx, gy | `Stations/DryDock.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché | Cale sèche : caisse posée à la main (ou case visée) ; serveur + VR : adjacence 4-voisins, cœur, planète, MAX_FLEET_SIZE, cache fleet_stats_ |
| `RemoveShipModule` | W | ship | `Stations/DryDock.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché | Cale sèche : en deux temps ; refusé si le retrait couperait le vaisseau du cœur |
| `SaveShipTemplate` | W | fleet, name | `Stations/BlueprintPanel.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché |  |
| `SpeedupShipyard` | W | planet, ship? | `Stations/ShipyardPanel.cs` | `scenes/planet.js` | Engineering | P5 | Branché | Coût Nova affiché (gratuit ≤ 60 s) |

## Planète / bâtiments / recherche

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AnswerPlanetDecision` | W | planet, decision, choice | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | `decision` = **`decision_key`** (chaîne), pas l'id ; `choice` yes/no ; erreurs i18n |
| `BuildDefenseUnit` | W | planet, type, qty | `Crew/CrewLines.cs` +1 | `objects/planet.js` | Tactical | P5 | Branché | Armurerie, onglet Défenses ; plateformes en orbite de nos mondes dans l'espace réel |
| `CancelQueuedBuilding` | W | id, queue_id | `Stations/OpsConsole.cs` | `scenes/planet.js` | Ops | P5 | Branché | Param `id` ou `queue_id` ; échecs en `error:<clé>` ; file = `buildingtype` + `duration` |
| `CancelQueuedResearch` | W | id, queue_id | `Stations/ResearchLab.cs` | `scenes/research.js` | Science | P5 | Branché | Param `id` (ligne empire_research_queue, tech en `research`) ; cristal arraché de son pad ou × |
| `CheckBuildingQueue` | R | planet | — | `view/game.php` | Ops | P5 | À faire | `percent` basé sur `workingStart` + `working` (fallback legacy) |
| `CheckResearchQueue` | R | — | — | `view/game.php` | Science | P5 | À faire |  |
| `CheckShipQueue` | R | planet | — | `view/game.php` | Engineering | P5 | À faire | `percent` = elapsed/SHIPSTATS.time (plus time()/endTime) |
| `DowngradeBuilding` | W | buildingtype, planet | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | Immédiat, sans remboursement : confirmation en deux temps |
| `GetPlanetDecisions` | R | planet | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | 10 décisions / planète / heure, `nextRefreshAt` → compte à rebours ; titres via `decision_*` / `decisionDesc_*` |
| `GetResource` | R | planet, raw? | `App/EconomyService.cs` | `objects/planet.js` | Ops (poll global) | P0 | Branché | `EconomyService` : `raw=1` sur **toutes** les planètes (csv ≤ 50) toutes les 10 s — accumule la production et fait avancer les files. `user` = PublicUser (id/username) seulement |
| `ImproveResearch` | W | research, planet | `Crew/CrewLines.cs` +1 | `objects/research.js` | Science | P5 | Branché | Labo Science : cristal de la constellation → synthétiseur (ou bouton) ; `planet` = meilleur researchLab ; corps vide = lancé, JSON `{queued,targetLevel}` = en file ; prérequis `GetConfigs.researchs.requiert` |
| `RecruitTroop` | W | planet, type, qty | `Crew/CrewLines.cs` +1 | `objects/planet.js` | Tactical | P5 | Branché | Console Armurerie (Tactique) : lot 1–500, coût × qty, durée time×qty×(100−(computer+1))/100, un lot par planète (`troopWorking`) |
| `RefreshStats` | W | planet | — | `objects/planet.js` | Ops | P5 | À faire |  |
| `RenamePlanet` | W | id, name | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | Serveur : 1–32 chars, lettres/chiffres/espaces/-_. ; `error:invalidPlanetName` |
| `SpeedupBuilding` | W | planet | `Stations/OpsConsole.cs` | `scenes/planet.js` | Ops | P5 | Branché | Coût Nova affiché avant (gratuit ≤ 60 s, sinon max(10, ⌈6·min^0.82⌉)) |
| `SpeedupResearch` | W | — | `Stations/ResearchLab.cs` | `scenes/research.js` | Science | P5 | Branché | Écran du synthétiseur ; coût Nova affiché (gratuit ≤ 60 s) ; sans param |
| `UpgradeBuilding` | W | buildingtype, planet | `Crew/CrewLines.cs` +1 | `objects/planet.js` | Ops | P5 | Branché | Console Ops : devis serveur (coût × niveau cible, temps × computer), « Ajouter à la file » si chantier actif ; réponse texte (niveau) ou JSON `queued` |

## Combat

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddFleetToBattle` | W | battleid, fleetid | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché | Onglet Opérations : vaisseau inactif du même système (le serveur ne vérifie pas la position), bataille en préparation ; ouvre le plateau |
| `BattleDoAction` | W | battleid, fleetid, bship_id, action, subaction, skill_id, target_bship_id, target_q, target_r | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P0 | Branché | Plateau de combat sur la table : viser→viser (case = move, compétence armée puis cible) ; `subaction` + `battle_subaction` ; `skill_id` seulement pour une compétence ; erreurs brutes → `vr.battle.err.*` |
| `BattleEndFleetTurn` | W | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché | Bouton Fin du tour du pupitre (et réplique Tactique) |
| `CheckPlanetAttack` | R | planet | `App/SiegeWatch.cs` | `objects/planet.js` | Tactical | P5 | Branché | `SiegeWatch` : appelé dès que `attackEndTime` expire pour un siège qui nous touche (`wip` → relance 5 s, `ok` → résolu) ; bombardement dehors + anneau sur la table |
| `DoTurnBattle` | W | battleid, fleetid, action, target | — | — | Tactical | — | Hors scope | Legacy, non utilisé par le web |
| `FleetAttackPlanet` | W | fleet, planet | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `GetBattle` | R | battleid | — | — | Tactical | — | Hors scope | Legacy |
| `GetBattleState` | R | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché | Plateau diffé toutes les 2,5 s (web) ; tirs rejoués depuis les nouvelles lignes `log` sur la table, dehors et à bord |
| `GetMyBattles` | R | — | `Stations/ArmoryConsole.cs` +1 | `scenes/galaxy.js` | Tactical | P5 | Branché | Seulement si un de nos vaisseaux a `isInBattle` : prend la table (vaisseau habité / système en vue) ou bouton Rejoindre sur le rebord |
| `GetPendingBattles` | R | systemid, planetid | — | — | Tactical | P5 | À faire |  |
| `MakeBattle` | W | systemid, fleets, planetid? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Tactical | fleets = mon vaisseau + cibles, `planetid` seulement si ≠ 0 (0 vs pirates), puis `UpdateBattle` (web startTacticalBattle) | Branché | fleets = mon vaisseau + cibles, `planetid` seulement si ≠ 0 (0 vs pirates), puis `UpdateBattle` (web startTacticalBattle) |
| `RemoveFleetFromBattle` | W | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché | Bouton Retirer le vaisseau, bataille en attente seulement |
| `SetFleetState` | W | battleid, fleetid, auto, ready | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché | Bouton Prêt pendant la préparation (`auto=0`, `ready=1`) ; plus envoyé après MakeBattle (le serveur marque déjà notre camp prêt) |
| `UpdateBattle` | W | battleid | `Vfx/HexBattleController.cs` | `objects/fleet.js` | Tactical | P5 | Branché | Relancé comme le web si en attente ou délai de tour dépassé |

## Jumpgate

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `GetJumpgateDestinations` | R | planet | — | `objects/planet.js` | Helm | P5 | À faire |  |
| `SendFleetToJumpgate` | W | fleet, targetPlanet | — | `objects/planet.js` | Helm | P5 | À faire |  |

## Stargate

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `CloseStargateConnection` | W | planet | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Bouton Fermer la porte (origine ou cible) |
| `DispatchStargateMission` | W | originPlanet, missionType, mineral?, crystal?, biomass?, troopType?, troopQty? | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Six missions ; l'équipe (troupes / chariots / colons) traverse l'horizon |
| `GetKnownAddresses` | R | planet | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Base de la porte : destinations (statut, propriétaire, distance, charge) ; le serveur y inclut la planète d'origine, filtrée côté VR |
| `GetStargateConnectionStatus` | R | planet | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Toutes les 3 s dans la base ; activation extérieure = alarme, horizon rouge |
| `GetStargateMissions` | R | — | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Journal à droite, toutes les 10 s ; codes `resultDetail` traduits, réplique à la résolution |
| `OpenStargateConnection` | W | originPlanet, targetPlanet | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Séquence de composition : piste de glyphes, six verrous, surge de l'horizon |
| `ResolveStargateAddress` | W | originPlanet, address | `Stations/GateRoom.cs` | `objects/planet.js` | Comms | P5 | Branché | Champ d'adresse de la console (clavier Quest) — la composer juste = découverte |

## Social (chat, mail)

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddChat` | W | message | `Stations/CommsConsole.cs` | `objects/chat.js` | Comms | P5 | Branché | Champ du canal (clavier Quest) ; réponses `console:` (notice serveur, affichée dans le canal) et `pm_sent:` (/w) gérées |
| `DeleteMail` | W | id | `Stations/CommsConsole.cs` | `ui/MailWindowUI.js` | Comms | P5 | Branché | En deux temps |
| `GetChat` | R | lastid? | `Stations/CommsConsole.cs` | `objects/chat.js` | Comms | P5 | Branché | Console Comms, onglet Canal : `lastid`, relu toutes les 3 s seulement quand il est à l'écran ; messages du jour (serveur) ; `contact_name` brut (pas le `username` HTML) |
| `GetMail` | R | id | `Stations/CommsConsole.cs` | `ui/MailWindowUI.js` | Comms | P5 | Branché | Lecture (marque lu côté serveur) → badge relu ; Répondre préremplit « Re: » |
| `GetMailUnreadCount` | R | — | `App/CommsService.cs` | `ui/MailWindowUI.js` | Comms | P5 | Branché | `CommsService` toutes les 15 s : réplique Comms + balise « message en attente » au-dessus de l'officier Comms |
| `GetMails` | R | folder | `Stations/CommsConsole.cs` | `ui/MailWindowUI.js` | Comms | P5 | Branché | Onglet Courrier : `folder` inbox / sent, `filter` player / system / battle / diplomacy ; expéditeur 0 → libellé `system` (le serveur écrit « SYSTÈME ») |
| `GetPrivateConversations` | R | — | `Stations/CommsConsole.cs` | `ui/PanelChatUI.js` | Comms | P5 | Branché | Colonne gauche de l'onglet Privé (badge non lus) |
| `GetPrivateMessages` | R | contact_id, lastid | `Stations/CommsConsole.cs` | `ui/PanelChatUI.js` | Comms | P5 | Branché | Fil ouvert, `lastid`, relu toutes les 3 s ; marque lu côté serveur |
| `SearchPlayers` | R | query | `Stations/CommsConsole.cs` | — | Comms | P5 | Branché | Recherche de commandant pour ouvrir un fil (soi-même exclu) |
| `SendMail` | W | recipient, subject, content | `Stations/CommsConsole.cs` | `ui/MailWindowUI.js` | Comms | P5 | Branché | `recipient` = nom ou id ; garde client champs requis |
| `SendPrivateMessage` | W | contact_id, message | `Stations/CommsConsole.cs` | — | Comms | P5 | Branché | Champ du fil |

## Guerre

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AcceptPeaceOffer` | W | war | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Chancellerie, offre de l'adversaire |
| `AcceptWarDemands` | W | war | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Défenseur seulement (capitulation), en deux temps |
| `CancelPeaceOffer` | W | war | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Chancellerie, notre offre en attente |
| `DeclareWar` | W | target | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Dossier de l'empire visé sur l'orrery : planètes exigées (GetEmpirePlanets), ressources, en deux temps ; `planets` csv + `mineral`/`crystal`/`biomass` |
| `DeclinePeaceOffer` | W | war | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Chancellerie, offre de l'adversaire |
| `GetMyWars` | R | — | `App/DiplomacyService.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Démo | `DiplomacyService` toutes les 30 s (répliques Comms : guerre déclarée contre nous, offre de paix, fin de guerre) ; onglet Conflits de la Chancellerie |
| `GetWarDetails` | R | war | `Stations/DiplomacyRoom.cs` | — | Chambre diplomatique | P5 | Branché | Relu à la sélection d'un conflit (Chancellerie) |
| `OfferPeace` | W | war | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Chancellerie, conflit actif |
| `SurrenderWar` | W | war | `Stations/DiplomacyRoom.cs` | `ui/WarsWindowUI.js` | Chambre diplomatique | P5 | Branché | Attaquant seulement, en deux temps |

## Alliance

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AcceptAllianceApplication` | W | application | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Officiers, onglet Alliance (réplique à l'arrivée) |
| `AcceptAllianceInvite` | W | invite | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Onglet Alliance, sans alliance |
| `ApplyToAlliance` | W | alliance | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Registre, sans alliance |
| `CancelAllianceInvite` | W | invite | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Retrait d'une invitation (dossier) ou d'une candidature (Registre), ids retenus sur le casque |
| `CreateAlliance` | W | name, tag, description? | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Nom + tag au clavier Quest (onglet Alliance, sans alliance) |
| `DeclineAllianceApplication` | W | application | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Officiers, onglet Alliance |
| `DeclineAllianceInvite` | W | invite | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Onglet Alliance, sans alliance |
| `DisbandAlliance` | W | — | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Chef, en deux temps |
| `GetAllianceInvites` | R | — | `App/DiplomacyService.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | `DiplomacyService` (réplique à la réception) ; onglet Alliance sans alliance |
| `GetAlliances` | R | — | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Onglet Registre |
| `GetMyAlliance` | R | — | `App/DiplomacyIndex.cs` +1 | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | `DiplomacyService` (30 s) + `DiplomacyIndex` ; onglet Alliance, bannières des membres et blason dans la chambre |
| `InviteToAlliance` | W | target | `Stations/DiplomacyRoom.cs` | — | Chambre diplomatique | P5 | Branché | Dossier d'un empire (officiers) ; `inviteId` retenu sur le casque pour le retrait (aucune action ne liste les invitations envoyées) |
| `KickAllianceMember` | W | target | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Règles web : officiers → membres, chef → tous ; en deux temps |
| `LeaveAlliance` | W | — | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | En deux temps |
| `SetAllianceMemberRole` | W | target, role | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Chef : promouvoir / rétrograder |
| `TransferAllianceLeadership` | W | target | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Chef, en deux temps |
| `UpdateAllianceDescription` | W | description? | `Stations/DiplomacyRoom.cs` | `ui/AllianceWindowUI.js` | Chambre diplomatique | P5 | Branché | Officiers : champ de l'onglet Alliance |

## Empire / progression / shop

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddEmpirePolicy` | W | policy | `Stations/QuartersRoom.cs` | `objects/policy.js` | Quartiers du commandant | P6 | Branché | Éthiques du bureau, jusqu'à `maxPolicies` (+1 au niveau 10), en deux temps (pas de retrait serveur) ; doublons filtrés par `policy_id` |
| `BuyShopItem` | W | item | `Stations/QuartersRoom.cs` | `ui/ShopWindowUI.js` | Quartiers du commandant | P6 | Branché | Deux temps ; montée de niveau annoncée |
| `CreateEmpire` | W | empireName, bgColor, shape1, shape2, shape3, color1, color2, color3, authority, ethics, speciesName, speciesType, traitPos1, traitPos2, traitNeg1, traitNeg2, planetName, empireBio, leaderName, leaderSex, leaderTitle, heirTitle, leaderTraits, shipPrefix | `UI/EmpireCreationWizard.cs` | — | Quartiers du commandant | P3 | Branché | Compte sans empire → SAS ; miroir create-empire.php ; `GetMeEmpire` renvoie `error:noEmpire` |
| `EquipShopItem` | W | item | `Stations/QuartersRoom.cs` | `ui/ShopWindowUI.js` | Quartiers du commandant | P6 | Branché | Titre → plaque du bureau + vitrine ; couleur de flotte → anneau de la vitrine et **nos coques dehors** (`ShipHullBuilder.SetOwnAccent`) |
| `GetAchievements` | R | — | `Stations/QuartersRoom.cs` | `scenes/galaxy.js` | Quartiers du commandant | P6 | Branché | Console Progression (filtres par catégorie) + mur des trophées de la cabine (18 plaques, débloqués en premier) |
| `GetActivity` | R | lastid | `Stations/QuartersRoom.cs` | `objects/activity.js` | Quartiers du commandant | P6 | Branché | Journal de bord du bureau : `lastid`, relu toutes les 10 s à l'écran |
| `GetAuthorities` | R | — | `Stations/QuartersRoom.cs` +1 | `ui/EmpireHubUI.js` | Quartiers du commandant | P6 | Branché |  |
| `GetDailyObjectives` | R | — | — | — | Quartiers du commandant | — | Hors scope | Web utilise `GetProgressionObjectives` |
| `GetEmpire` | R | user | `Stations/DiplomacyRoom.cs` | `scripts/user.js` | Chambre diplomatique | P5 | Branché | Dossier diplomatique : autorité, espèce et éthiques de l'empire visé (`user` = users.id) |
| `GetEmpires` | R | — | `App/DiplomacyIndex.cs` +1 | `objects/empire.js` | Chambre diplomatique | P5 | Branché | `DiplomacyIndex` (teintes des jetons et coques) ; orrery, sièges de l'hémicycle et dossiers de la chambre diplomatique |
| `GetLeaderTraits` | R | — | `UI/EmpireCreationWizard.cs` | — | Quartiers du commandant | P3 | Branché | Liste lore pour CreateEmpire |
| `GetMeEmpire` | R | — | `App/AuthManager.cs` +1 | `objects/policy.js` | Système (boot) | P0 | Branché | `error:noEmpire` si compte sans empire (flux CreateEmpire) |
| `GetMonthlyObjectives` | R | — | — | — | Quartiers du commandant | — | Hors scope | Idem |
| `GetNovaTopupHistory` | R | — | `Stations/QuartersRoom.cs` | — | Quartiers du commandant | P6 | Branché | Historique des recharges |
| `GetNovaTopupPacks` | R | — | `Stations/QuartersRoom.cs` | `ui/ShopWindowUI.js` | Quartiers du commandant | P6 | Branché | Packs affichés (prix serveur) ; aucun chemin de paiement depuis le casque (voir écarts) |
| `GetPolitics` | R | — | `Stations/QuartersRoom.cs` | `ui/EmpireHubUI.js` | Quartiers du commandant | P6 | Branché | Onglet Politique : 8 catégories, effets en % |
| `GetProgressionObjectives` | R | — | `Stations/QuartersRoom.cs` | `ui/ProgressionWindowUI.js` | Quartiers du commandant | P6 | Branché | Objectifs du jour / de la semaine / du mois avec comptes à rebours ; relu toutes les 30 s dans la cabine |
| `GetRelation` | R | user1, user2 | `Stations/DiplomacyRoom.cs` | `objects/empire.js` | Chambre diplomatique | P5 | Branché | Jauge de relation du dossier (user1 = moi, user2 = l'empire visé) |
| `GetShopData` | R | — | `Stations/QuartersRoom.cs` | `ui/ShopWindowUI.js` | Quartiers du commandant | P6 | Démo | Console Boutique : boosters (durée restante), consommables, cosmétiques, titres ; solde Nova et jetons |
| `GetSpeciesTraits` | R | — | `Stations/QuartersRoom.cs` +1 | `objects/specy.js` | Quartiers du commandant | P6 | Branché |  |
| `GetSpeciesTypes` | R | — | `Stations/QuartersRoom.cs` +1 | `objects/specy.js` | Quartiers du commandant | P6 | Branché |  |
| `GetWeeklyObjectives` | R | — | — | — | Quartiers du commandant | — | Hors scope | Idem |
| `RenameEmpire` | W | name | `Stations/QuartersRoom.cs` | `ui/EmpireHubUI.js` | Quartiers du commandant | P6 | Branché | Console du bureau (Quartiers) : clavier Quest, un jeton de renommage |
| `SetAuthority` | W | authority | `Stations/QuartersRoom.cs` | `ui/EmpireHubUI.js` | Quartiers du commandant | P6 | Branché | Changer une autorité déjà choisie coûte un jeton de reconfiguration (deux temps) |
| `SetPolitics` | W | category, option | `Stations/QuartersRoom.cs` | `ui/EmpireHubUI.js` | Quartiers du commandant | P6 | Branché | Option choisie = appliquée (gratuit) ; testé aller-retour sur le compte propriétaire |
| `UpdateEmpireFlag` | W | flag | `Stations/QuartersRoom.cs` | `ui/EmpireHubUI.js` | Quartiers du commandant | P6 | Branché | Éditeur de drapeau du bureau (fond + 3 calques), JSON web empireFlag.js |
| `UpdateSpecy` | W | empire, name?, type_id?, traits? | `Stations/QuartersRoom.cs` | `objects/specy.js` | Quartiers du commandant | P6 | Branché | Nom seul gratuit ; type ou traits = un jeton ; **toujours** `name` + `type_id` + `traits` complets (le serveur réécrit les traits omis) |

## Écarts serveur à traiter côté web

Corrigés dans `stellar-universe` (`7580501`, 2026-09-25) — le client VR ne contourne jamais un manque serveur par le site.

| Point | Statut |
|---|---|
| `BattleDoAction` `skill_id` optionnel (move) | Fait |
| File `moveToSystem` : `x`/`y` (+ accept `targetX`/`targetY`) | Fait |
| `ok:sublight_not_enough_modules` dans `success_forms` | Fait |
| `GetResource` / flottes : `PublicUser` (id/username) + purge cache | Fait |
| `CancelQueuedBuilding` : `id`\|`queue_id` + `error:` + champs `buildingtype`/`duration` | Fait |
| `AnswerPlanetDecision.decision` = string + erreurs i18n | Fait |
| `CheckBuildingQueue.percent` via `workingStart` | Fait |
| `RenamePlanet` validation serveur | Fait |
| Noms natifs `academy` / `defenseFactory` / `stargate` (+ `buildingDesc_*`) | Fait |
| `CreateEmpire` + `GetLeaderTraits` (API) ; `GetMeEmpire` → `error:noEmpire` | Fait |
| i18n web : onglets, EmpireHub, recherche, skills, décisions, journal colonie, relations overlay | Fait |
| Clés `vr.*` + `crew.*` (missing-keys §2–3) dans `assets/langs/{fr,en}.json` | Fait |
| Clés `vr.battle.*` (missing-keys P5.5 H3, combat sur la table) dans `assets/langs/{fr,en}.json` | Fait |
| Clés modèles / file 3D / armurerie (`vr.dock.*`, `vr.table.*`, `vr.armory.*`, `crew.tactical.*`) en fr/en | Fait |
| Clés Comms et porte (`vr.comms.*`, `vr.gate.*`, `crew.comms.*`) en fr/en ; erreurs Comms et courriers système localisés | Fait |

### Restants

- **`GetActivity`** : entrées toujours stockées en anglais / FR brut en DB — migration clé+params non faite.
- **Flux VR CreateEmpire** : ✅ livré côté VR (`UI/EmpireCreationWizard.cs`) — `error:noEmpire` → assistant du sas → `CreateEmpire` → `GetMeEmpire` → Bridge.
- **Plafonds d'empire** : ✅ `GetConfigs.empire.maxPolicies` / `leaderTraitsMax` (web `faf0614`, en prod) — lus par l'assistant de création.
- **Propriété des planètes** : ✅ cache `GetSystems` vidé à la planète de départ / `ColonizePlanet` / suppression de compte ; `GetEmpirePlanets` renvoie `systemid` + `slot` (web `c94c803`, en prod). Un nouvel empire voyait son monde natal comme non possédé jusqu'à 1 h.
- **Formes du drapeau** : ✅ `view/create-empire.php` rend les options des trois sélecteurs via les clés `flagShape_<id>` (fr/en) ; restent les libellés de rangée « Couleur de fond » / « Forme n » en dur dans la vue.

### À corriger côté web (relevés en lisant le chantier / designer, 2026-09-25)

- **⚠ Sécurité — `AddToFleet` supprime le module d'un autre joueur** : ✅ corrigé — `notYourShip` refuse sans `DelShip` ; null-check + `size + ship.size <= MAX_FLEET_SIZE`.
- **Colonisation cassée** : ✅ `Colonize` + clients web/VR acceptent `colonyShip` et legacy `ColonyShip`.
- **`HackingModule` inconstructible** : ✅ `requiert.researchLab` vérifié sur la planète (`notEnoughResearchLab`).
- **`PlaceShipModule`** : ✅ adjacence 4-voisins, cœur (ou premier = ShipCore@4,4), même planète, `MAX_FLEET_SIZE`, bust `fleet_stats_<id>`.
- **`AddToFleet fleet=0`** : ✅ `systemid = planet.systemid` ; réponse `{ok:true,fleet:id}`.
- **`CheckShipQueue.percent`** : ✅ elapsed / `SHIPSTATS[type].time` (même pattern que bâtiments).
- **Erreurs grille i18n** : ✅ `positionOccupied`, `invalidPosition`, `shipNotInHangar`, `cannotRemoveCore`, `shipNeedsCore`, `moduleNotAdjacent`, `shipNotFound`, `error_planet_not_yours`, `notEnoughResearchLab` (fr+en).
- **`MAX_FLEET_SIZE` / `ALLOWED_FLEET_PER_PLANET`** : ✅ exposés dans `GetConfigs.fleet.maxFleetSize` / `allowedFleetPerPlanet` ; lus par `GameConfig` VR.

### À corriger côté web (relevés en lisant la recherche, 2026-09-25)

- **⚠ `ImproveResearch` en file perd un niveau** : ✅ recharge l'empire brut avant débit / `targetLevel` / `UpdateEmpire` ; même garde sur `RenameEmpire`, `SpeedupBuilding`, `ExplorePlanet` (ne plus persister `AdjustEmpireForPendingWork`).
- **File de recherche web : nom vide** : ✅ clients lisent `research` (alias `research_type` / `duration_seconds` renvoyés par `GetEmpireResearchQueue`) ; hbs corrigé.
- **Clé absente `needSearchLab`** : ✅ UI utilise `noResearchLab` ; alias `needSearchLab` ajouté FR/EN.
- **`UNLOCKS` codé en dur en français** : ✅ dérivé des configs (`shipstats` / `troopstats` / `defensestats` + bâtiments gate) ; libellés via i18n / `vr.research.kind.*` ; effets dans `desc<Tech>`.
- **`GetResource.empire` brut** : ✅ `AdjustEmpireForPendingWork` (aligné `GetMeEmpire`).

- **Anomalies : titre / description stockés en français** : ✅ clés `anomaly_<type>` / `anomalyDesc_<type>` en fr/en ; spawn stocke les clés ; web + VR affichent via `type` (lignes legacy FR ignorées).

- **⚠ `CompleteBounty` ne vérifie rien** : ✅ exige une flotte de l'empire dans `target_systemid` (`bounty_need_fleet_onsite`).
- **`reward_credits` jamais versé** : ✅ versé en **cristal** sur la 1ʳᵉ planète (pas de devise crédits) ; chip web = cristal.
- **Contrats : titres / descriptions FR en base** : ✅ `bounty_<type>` / `bountyDesc_<type>` FR/EN ; spawn stocke les clés ; UI par `target_type`.
- **Toast web** : ✅ message `bounty_completed_success` (+ alias `bounty_completed_successfully`).
- **`action-api.json` invalide** : ✅ clé `GetBounties` restaurée (casse après edit ScanAnomaly).

- **`GetPlanet` public par conception** (question de design, pas un bug) : toute planète est lisible par tous (ressources, hangar, chantier, troupes, défenses, adresse de porte), comme le panneau web des planètes étrangères. À trancher si l'on veut du renseignement (exploration / espionnage) ; la VR affiche au relevé des agrégats (défense, garnison, orbite, bâtiments clés).

### À corriger côté web (relevés en lisant le combat et le commit a93b82c, 2026-09-25)

- **⚠ « Surcharge Cybernétique » (`cyber_override`, type `cyber_hack`) ne marchait jamais** : ✅ corrigé — `$targetBship` est résolu comme pour les autres compétences (`target_bship_id`, sinon la case visée `$tQ`/`$tR`) et le tir allié est refusé (`friendly_fire`). La VR envoie déjà `target_bship_id` et `target_q` / `target_r`.
- **Lignes `battle_actions` sans id de compétence** : ✅ `skill` (id) est désormais ajouté à `$result` — les tirs rejoués (les nôtres comme ceux de l'adversaire) portent la compétence exacte au lieu d'être devinés par la forme du résultat. Reste à l'exploiter côté clients.
- **Modèles de vaisseaux (commit a93b82c)** : ✅ documentés dans `action-api.json` (`GetShipTemplates`, `SaveShipTemplate`, `DeleteShipTemplate`, `ApplyShipTemplate` — params, retours, erreurs) ; `action_index` et `action_count` réalignés sur le total réel. `BlueprintPanel.cs` les branche déjà côté VR — ils apparaissent maintenant dans la matrice.
- **⚠ `ApplyShipTemplate` pouvait produire un vaisseau en morceaux** : ✅ corrigé — parcours centre → extérieur avec contrôle d'ancrage 4-voisins (même règle que `PlaceShipModule`), gardes de grille (cases 0–8, case libre), ancre (cœur) ramenée au centre **avant** l'assemblage, budget de coque = somme réelle des `size` encore à bord. Les modules non ancrés ou absents du hangar sont comptés dans `missing` / `missingDetails`.
- **Codes d'erreur sans clé** : ✅ `fleetMustBeDocked`, `templateNotFound`, `templateEmpty`, `notFound` + `vr.dock.templatePlaced` ajoutés en fr/en.
- **Cœur conservé et `$occupied`** : ✅ un cœur conservé hors (4,4) n'est plus rapporté à tort comme manquant, et la case de tout cœur amarré bloque toute pose (plus d'empilement à la même coordonnée).

### Restant côté web (relevé en relisant `ApplyShipTemplate` et le combat, 2026-09-25)

- **`PlaceShipModule` sous-comptait la taille de coque** : ✅ le budget lit maintenant **toutes** les lignes de la flotte (`GetFleetStats` fait pareil) ; seul le contrôle d'adjacence reste limité aux modules posés sur la grille. Contrôle de plafond aligné sur celui de `ApplyShipTemplate`.
- **Pas d'atomicité sur `ApplyShipTemplate`** : ✅ démontage + repose dans une transaction PDO — une exception en cours de boucle laisse la flotte intacte.
- **Ciblage par id non validé (`BattleDoAction`)** : ✅ `BattleResolveTarget` refuse un id étranger à la bataille, et la portée est mesurée sur la **cible résolue** et non sur `target_q`/`target_r`. Vaut pour `attack`, `attack_status`, `debuff`, `heal`, `cyber_hack`.
- **`LogBattleAction` et la cible** : ✅ le log trace la cible réellement touchée (`$result['target']`).
- **`debuff_aoe` centré sur le lanceur** : ✅ aligné sur `attack_aoe` — centré sur la case visée, case visée incluse (l'IEM s'appliquait autour du lanceur et ignorait la cible).
- **`cyber_hack` n'appliquait pas `_CalcDamage`** : ✅ passe par le même calcul que les autres attaques (pénalité « brouillé » comprise).
- **`teleport` (et `move`)** : ✅ cases bornées à l'arène du plateau (`q` −5..5, `r` −4..4, comme `battle.js`).
- **`stealth` / `buff_armor` en valeur absolue** : ✅ `max` — lancer `buff_armor` après `stealth` ne rétrograde plus la réduction de 80 % à 50 %.
- **`ApplyShipTemplate` n'accordait pas `first_ship_assembled`** : ✅ accordé dès qu'un module est assemblé.
- **Modèle corrompu** (entrées dupliquées, cœur hors (4,4)) : ✅ les gardes (case libre, case bornée, cases des cœurs amarrés) empêchent toute corruption de grille ; le comptage `placed`/`missing` reste approximatif sur des données déjà invalides.
- **`ScienceModule`** : laissé en camelCase (`scienceModule`) dans `assets/langs/{fr,en}.json` — le repli de casse de `Trans.Get` le résout.
- **Triangulation stargate (commit a93b82c)** : rien à changer côté VR. La découverte est accordée côté serveur (`ImproveResearch`, rattrapage dans `GetKnownAddresses`) ; les adresses arrivent par `GetKnownAddresses` quand Comms / Stargate sera branché.

### À corriger côté web (relevés en lisant Tactical, 2026-09-25)

- **`AddFleetToBattle` ne vérifiait pas la position** : ✅ même garde que `MakeBattle` (`fleetNotInThisSystem`) — un vaisseau à l'autre bout de la galaxie ne peut plus rejoindre un combat en préparation.
- **Sièges résolus seulement à la lecture** : ✅ le résolveur sort de la closure d'action (`ResolvePlanetAttack($planet)` dans `model/battle.php`, corps déplacé tel quel) et un cron de 5 min le conduit pour les planètes dont `attackEndTime` a expiré, en reprenant sur `wip` — plus besoin qu'un client ouvre la planète. `DoAnAction` passe par `BASE_URL`, donc le résolveur fonctionne aussi hors requête HTTP.
- **`CheckPlanetAttack` mélangeait `echo` et `return`** : ✅ une seule forme de réponse (`return "error:…"`), et l'action délègue au résolveur.
- **`GetPendingBattles` exigeait `planetid > 0`** : ✅ `planetid = 0` (espace ouvert, pirates) est listé, et une planète inconnue renvoie une liste vide au lieu d'une erreur.
- **Codes bruts sans clé** : ✅ `invalid_troop_type`, `invalid_defense_type`, `invalid_troops_payload` passent par `Lang()` (+ clés fr/en).

### Comms — chat / MP / courrier (corrigés, 2026-09-25)

- **Erreurs en français brut** : ✅ `SendMail`, `SendPrivateMessage` et `AddChat` passent par des clés (`missingFields`, `recipientNotFound`, `cantMailYourself`, `mailSendFailed`, `invalidParams`, `pmSendFailed`, `playerNotFoundName`, `cantWhisperYourself`, `permissionDenied`) — `LangFormat` remplit les paramètres (`{0}`).
- **Courriers système écrits en français dans la base** : ✅ `mails.subject_key` / `content_key` / `params_json` (ajoutés par `EnsureMailTables`), résolus à la lecture par `MailLocalizeRow` — la guerre et l'alerte d'attaque passent leurs clés + paramètres, donc le texte s'affiche dans la langue du **destinataire**. Les courriers antérieurs gardent leur texte stocké.
- **`sender_username` = « SYSTÈME » codé en dur** : ✅ `sender_is_system` exposé — les clients n'ont plus à inspecter `sender_id` ni à parser un nom français.
- **`GetChat.username` contient du HTML** : ✅ champ `rank` en clair à côté de `contact_name`.
- **`GetChat` ne servait que les messages du jour** : ✅ un chargement initial (`lastid = 0`) sans message du jour retombe sur les derniers messages — le canal ne paraît plus vide après minuit. Les polls incrémentaux restent inchangés.
- **`DeleteMail` répondait `ok` même si l'id n'existe pas** : ✅ `error:mail_not_found` quand rien n'a été supprimé (id inconnu ou courrier d'un autre).
- **`GetPrivateMessages` marquait tout le fil lu** : ✅ seuls les messages réellement renvoyés par l'appel passent en lu — un poll incrémental ne vide plus le badge avant affichage.

### Comms et sièges (corrigés, 2026-09-26)

- **Langue des textes rendus par le serveur** : ✅ la VR envoie `&lang=<Trans.Lang>` à chaque requête (`DetectLang()` lit `$_GET['lang']` en premier).
- **L'e-mail externe gardait la langue de l'émetteur** : ✅ `users.lang` (migration `v8`) retient la langue de la dernière requête authentifiée du joueur (`RememberUserLang`, un `UPDATE` seulement quand elle change) ; `DispatchExternalEmailNotification` résout l'objet et le contenu à clé ainsi que le gabarit (`email_*`) dans la langue du **destinataire** (`LangIn` / `LangFormatIn`). Le texte d'un joueur reste tel qu'il l'a écrit ; un compte qui n'a jamais rejoué depuis garde l'ancienne langue de repli (celle de la requête).
- **Note** : dix libellés `email_*` (gabarit du courrier externe) ont été reconstruits à l'identique côté EN après un écrasement accidentel du fichier — à relire d'un œil si le ton du gabarit anglais compte.
- **`RUN_AWAY` sans effet sur un joueur humain** : ✅ `FleetRetreatToStar()` fait le retrait dans le processus (orbite quittée vers l'étoile du système, durée minimale de trajet, comme `MoveFleetToSystem` vers son propre système) au lieu de `DoAnAction`, refusé pour un compte humain. Les deux chemins (siège `CheckPlanetAttack`, `ResolvePlanetAttack`) l'utilisent.
- **Siège sur une planète supprimée** : ✅ `ReleaseOrphanSieges()` (appelée par `GetExpiredSiegePlanets`) remet à zéro `attackEndTime` et l'orbite des flottes dont la planète n'existe plus, et de leurs modules.

### Stargate (corrigés, 2026-09-26)

- **`GetKnownAddresses` renvoyait la planète d'origine** : ✅ elle reste dans la liste — les clients y lisent l'adresse de la base — mais porte `isOrigin: true`, et le web l'exclut du sélecteur de cible. Sans ce drapeau, le serveur proposait une destination que `OpenStargateConnection` refuse (`cantTargetOwnPlanet`).
- **Durée `sendTroops` / `sendResources`** : ✅ `GetSetConfig` stocke le tableau **entier**, donc une mission ajoutée après l'écriture de la ligne est absente de la copie stockée et tombait sur le repli codé en dur `3600` (1 h) — et `GetConfigs` servait ce tableau incomplet aux clients (il est documenté dans `action-api.json`). Les défauts vivent dans `$STARGATE_MISSION_TIME_DEFAULTS`, qui sert de repli par mission, et une migration idempotente complète les clés absentes de la ligne en base (garde passée en `v7`). **Une valeur stockée différente reste prioritaire** : si la ligne porte volontairement 3600 pour ces deux clés, elle ne bougera pas.
- **Ordre de déploiement** : les trois commits Stargate vont ensemble — le serveur ajoute `isOrigin` et les codes `failed:*` que seuls le bundle et les langues livrés avec eux savent interpréter. Un déploiement partiel du PHP seul (sans `assets/js/dist/`) afficherait `failed:planetGone` en clair.
- **`resultDetail` d'échec** : ✅ codes au lieu de phrases (`failed:planetGone`, `failed:notColonized`, `failed:alreadyColonized`, `failed:alreadyExplored`) — `Lang()` répondait dans la langue de la **requête**, pas du lecteur. Les deux clients les traduisent (`vr.gate.result.failed.*`).
- **Libellés web codés en dur** : ✅ toute la carte stargate (`scenes/planet.js`) passe par `Helper.lang` et les clés `vr.gate.*` partagées avec la VR — plus de doublon de traduction.
- **Adresse de la planète d'origine** : ✅ disponible dans la liste `GetKnownAddresses` via l'entrée marquée `isOrigin`. Côté VR, `GateRoom` lit toujours l'adresse de la base dans cette liste (moniteur mural) : elle peut aussi venir de `GetPlanet.stargateAddress`, ce qui permettrait de sortir l'origine de la liste plus tard.

### Restant côté VR (relevé en corrigeant la Stargate, 2026-09-26)

- **`ResultLabel` traduit les codes d'échec** (`failed:*`) : ✅ committé avec la chambre diplomatique.
- **La langue de requête** : ✅ `ActionJs.BuildUrl` ajoute `&lang=<Trans.Lang>` à chaque requête (`DetectLang()` lit `$_GET['lang']` en premier).

### Diplomatie (corrigés, 2026-09-26)

- **Invitations envoyées et candidatures introuvables** : ✅ `GetMyAlliance.alliance.invites` (officiers : invitations en attente, avec `empireName`) et `GetMyAlliance.myApplications` (sans alliance : nos candidatures, avec `allianceName` / `allianceTag`). La VR retire invitations et candidatures depuis ces listes (plus d'ids gardés sur le casque).
- **Alliance des autres empires** : ✅ `GetEmpires` porte `allianceId` / `allianceTag` (une requête pour tous) ; le dossier affiche le tag.
- **Stocks d'un autre empire exposés** : ✅ `GetEmpirePlanets` ne renvoie `mineral` / `crystal` / `biomass` que pour notre propre empire (le web ne les lisait pas ; la VR a retiré sa ligne « réserves connues »).
- **Web** : ✅ score de guerre (`warScoreAttacker`), confirmations `confirm*` ajoutées aux langues, libellés FR de `wars-window.hbs` / `alliance-window.hbs` passés en clés (`vr.diplo.*` partagées, `warPickTarget`, `warSelectTarget`, `confirmDeclareWar`).
- **Web** : ✅ la fenêtre Alliance affiche les invitations envoyées (officiers) et nos candidatures en attente, chacune avec un bouton de retrait qui appelle `CancelAllianceInvite` — l'action n'est plus réservée à la VR.

### Quartiers du commandant — relevé (2026-09-26)

- **`UpdateSpecy` sans `traits` corrompt les traits** : le handler passe alors les lignes chargées (tableaux) à `UpdateSpecy()`, qui efface `species_traits` et réinsère un `trait_id` tableau. **Le web déclenche le cas** : `EmpireHubUI` envoie un seul champ à la fois (`onSaveName`, `onChangeType`, `onChangeTrait`). La VR envoie toujours `name` + `type_id` + `traits` complets. Le handler répond aussi un corps vide (la doc annonce du JSON).
- **`AddEmpirePolicy` ne détecte pas les doublons** : la garde compare l'`id` de ligne `empire_policies` à l'id de politique au lieu de `policy_id`. Et `EmpireHubUI.onAddPolicy` appelle une action inexistante `AddPolicy` (seul `objects/policy.js` utilise la bonne).
- **`stripe_checkout.php` lit `$_SESSION['user_id']`**, jamais posé (actionjs utilise `$_SESSION['id']`) : la recharge Nova du web semble toujours répondre 401. Aucun chemin de paiement n'existe pour le casque (pas de token accepté, pas d'action) — à spécifier (achat intégré Quest ou session de paiement par token).
- **Textes serveur sans traduction** : noms / descriptions des politiques (`$POLITICS`) et titres des objectifs d'événement (`title` seul, pas de `title_en`) ne sont qu'en français ; les noms d'objectifs ont `name_en`, les événements `title_en`.
- **Clés manquantes côté web** : `authorityUpdated`, `policyAdded` (toasts du Hub), `traitEffect_defense` / `trade` / `diplomacy` (effets de politique ; proposées dans missing-keys).
- **`GetEmpire` sans garde** : pour un `user` sans empire, la fonction lit des champs d'un `null`.
- **Web — récompense de niveau affichée fausse** : la fenêtre Progression annonce `(niveau+1)×15` Nova, le serveur donne `25 × nouveau niveau` (`AddEmpireXP`).

### Spec livrée — `CreateEmpire`

Miroir de `controller/create-empire.php` via `CreateEmpireForUser` : mêmes validations / effets. Auth token. Requis : `empireName`. Optionnels : drapeau, `authority`, `ethics` (csv), espèce / traits, `planetName`, profil lore (`leaderTraits` csv ≤ 3 via `GetLeaderTraits`). Retour = JSON enrichi type `GetMeEmpire`, ou `error:<clé>`.
