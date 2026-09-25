# PARITY — Stellar Universe VR ↔ `actionjs.php`

Généré depuis `action-api.json` (154 actions), `actionjs.php` et un grep des deux clients. Référence web : `/Users/thommy/Websites/stellar-universe`. Roadmap : [`ROADMAP.md`](ROADMAP.md).

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
| Méta / boot | 2 | 5 | 40 % |
| Caméra (vue) | 2 | 2 | 100 % |
| Galaxie | 2 | 8 | 25 % |
| Flotte | 15 | 23 | 65 % |
| Vaisseau / chantier | 1 | 9 | 11 % |
| Planète / bâtiments / recherche | 8 | 17 | 47 % |
| Combat | 8 | 14 | 57 % |
| Jumpgate | 0 | 2 | 0 % |
| Stargate | 0 | 7 | 0 % |
| Social (chat, mail) | 0 | 11 | 0 % |
| Guerre | 0 | 9 | 0 % |
| Alliance | 1 | 17 | 6 % |
| Empire / progression / shop | 7 | 27 | 26 % |
| **Total** | **49** | **154** | **32 %** |

Appelées par le client web : 134/154. « Appelée » ≠ « finie » : voir la colonne *Statut*.

## Auth

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `Login` | W | email, password | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché |  |
| `LoginToken` | W | token | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché | Token minté sur le casque, lié à l'IP |
| `Register` | W | email, password, username | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché |  |

## Méta / boot

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `GetConfigs` | R | — | `App/DiplomacyIndex.cs` +2 | `scripts/configs.js` | Système (boot) | P0 | Branché |  |
| `GetEventData` | R | — | — | `ui/ProgressionWindowUI.js` | Conseil | P0 | À faire |  |
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
| `ClaimBounty` | W | bounty | — | `ui/BountiesWindowUI.js` | Science | P5 | À faire |  |
| `CompleteBounty` | W | bounty | — | `ui/BountiesWindowUI.js` | Science | P5 | À faire |  |
| `GetBounties` | R | — | — | `ui/BountiesWindowUI.js` | Science | P5 | À faire |  |
| `GetEmpirePlanets` | R | empire | `App/OwnedPlanets.cs` | `ui/WarsWindowUI.js` | Système (boot) | P5 | Branché | `OwnedPlanets` : mes planètes fraîches (id, `systemid`, slot — web `c94c803`), au boot et après une fondation ; `GetSystems` en secours seulement |
| `GetPlanet` | R | id | — | `objects/planet.js` | Science | P5 | À faire |  |
| `GetSystemAnomalies` | R | systemid | — | `ui/StarWindowUI.js` | Science | P5 | À faire |  |
| `GetSystems` | R | — | `App/BridgeSystemLoader.cs` +2 | `scenes/galaxy.js` | Holo table | P4 | Branché | Galaxie complète sur la table (LOD, territoires par détenteur) |
| `ScanAnomaly` | W | anomaly, fleet | — | `ui/StarWindowUI.js` | Science | P5 | À faire |  |

## Flotte

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddFleetOrderStep` | W | fleet, step | `Holo/OrderQueue.cs` +1 | `objects/fleet.js` | Helm | P4 | Branché | Étape JSON : `targetId` (planète / astéroïde) ; `moveToSystem` avec **`x`,`y`** (le serveur accepte aussi `targetX`/`targetY`) |
| `ClearFleetOrderQueue` | W | fleet | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché |  |
| `Colonize` | W | ship, planet | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Ops | `ship` = id du module `ColonyShip` ; planète libre, habitabilité ≥ 6 | Branché | `ship` = id du module `ColonyShip` ; planète libre, habitabilité ≥ 6 |
| `DepositCargo` | W | fleet, planet, mineral?, crystal?, biomass? | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Ops | P5 | Branché |  |
| `ExplorePlanet` | W | fleet, planet | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Science | P5 | Branché |  |
| `GetAllFleets` | R | — | `App/BridgeSystemLoader.cs` +3 | `scenes/galaxy.js` | Helm | P0 | Branché | Cache serveur 3 s → poll VR 3,5 s. Traite les files de flotte. `user` = PublicUser (id/username) ; cache fleets purgé au hit |
| `GetAllFleetsAround` | R | — | — | `scenes/galaxy.js` | Helm | — | Hors scope | Interdit en VR (règle AGENTS) — `GetAllFleets` + filtre client |
| `GetSystemAsteroids` | R | systemid | — | `scenes/system.js` | Helm | P5 | À faire |  |
| `HarvestAsteroid` | W | fleet, asteroid | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Engineering | P5 | Branché |  |
| `LoadTroops` | W | fleet, planet, troops | — | `objects/fleet.js` | Tactical | P5 | À faire |  |
| `MoveFleetToAsteroid` | W | fleet, asteroid, hyperspace? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | idem hyperspace |
| `MoveFleetToPlanet` | W | fleet, planet, hyperspace? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | idem hyperspace |
| `MoveFleetToSystem` | W | fleet, pos, hyperspace? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | Toujours `hyperspace` explicite (0 sous-lumière / 1 hyperespace) après devis au pupitre (`TravelPlanner`, formules serveur) ; `ok:sublight_*` → `ApiResult.NoticeKey` |
| `PrlBondFleetToSystem` | W | fleet, system?, pos? | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Helm | P4 | Branché | Devis portée / coût / recharge au pupitre (`TravelPlanner`, distance sur `visual_x/visual_y` comme le serveur), `fleet` + `system` + `pos` ; lâcher sur une étoile de la galaxie |
| `ProcessFleetOrderQueue` | W | fleet | — | — | Helm | — | Hors scope | Géré par cron + `GetAllFleets` ; pas d'UI |
| `RemoveFleetOrderStep` | W | fleet, stepIndex | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché | `stepIndex` 0-based, repeater Helm |
| `RenameFleet` | W | id, name | — | `objects/fleet.js` | Helm | P5 | À faire |  |
| `SetFleetOrderQueue` | W | fleet, queue, loop? | — | `objects/fleet.js` | Helm | P4 | À faire |  |
| `SpeedupFleetTravel` | W | fleet | — | `scripts/helper.js` | Helm | P5 | À faire |  |
| `ToggleFleetQueueLoop` | W | fleet, loop? | `Holo/OrderQueue.cs` | `objects/fleet.js` | Helm | P4 | Branché | `loop` explicite 0/1 |
| `UnloadTroops` | W | fleet, planet, troops | — | `objects/fleet.js` | Tactical | P5 | À faire |  |
| `UpdateFleetDefendPosition` | W | id, position | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `WithdrawCargo` | W | fleet, planet, mineral?, crystal?, biomass? | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Ops | P5 | Branché |  |

## Vaisseau / chantier

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddShip` | W | type, planet | — | `objects/fleet.js` | Engineering | P5 | À faire | VR : ShipCore codé en dur |
| `AddToFleet` | W | fleet, ship, planet? | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `CancelQueuedShip` | W | id, queue_id | — | `scenes/planet.js` | Engineering | P5 | À faire |  |
| `DelShip` | W | ship | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `DelToFleet` | W | fleet, ship | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `GetShipLayout` | R | fleet | `Vfx/FleetShipView.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché |  |
| `PlaceShipModule` | W | ship, fleet, gx, gy | — | `ui/ShipBuilderUI.js` | Engineering | P5 | À faire |  |
| `RemoveShipModule` | W | ship | — | `ui/ShipBuilderUI.js` | Engineering | P5 | À faire |  |
| `SpeedupShipyard` | W | planet, ship? | — | `scenes/planet.js` | Engineering | P5 | À faire |  |

## Planète / bâtiments / recherche

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AnswerPlanetDecision` | W | planet, decision, choice | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | `decision` = **`decision_key`** (chaîne), pas l'id ; `choice` yes/no ; erreurs i18n |
| `BuildDefenseUnit` | W | planet, type, qty | — | `objects/planet.js` | Tactical | P5 | À faire | VR : MissileTurret×1 codé en dur |
| `CancelQueuedBuilding` | W | id, queue_id | `Stations/OpsConsole.cs` | `scenes/planet.js` | Ops | P5 | Branché | Param `id` ou `queue_id` ; échecs en `error:<clé>` ; file = `buildingtype` + `duration` |
| `CancelQueuedResearch` | W | id, queue_id | — | `scenes/research.js` | Science | P5 | À faire |  |
| `CheckBuildingQueue` | R | planet | — | `view/game.php` | Ops | P5 | À faire | `percent` basé sur `workingStart` + `working` (fallback legacy) |
| `CheckResearchQueue` | R | — | — | `view/game.php` | Science | P5 | À faire |  |
| `CheckShipQueue` | R | planet | — | `view/game.php` | Engineering | P5 | À faire |  |
| `DowngradeBuilding` | W | buildingtype, planet | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | Immédiat, sans remboursement : confirmation en deux temps |
| `GetPlanetDecisions` | R | planet | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | 10 décisions / planète / heure, `nextRefreshAt` → compte à rebours ; titres via `decision_*` / `decisionDesc_*` |
| `GetResource` | R | planet, raw? | `App/EconomyService.cs` | `objects/planet.js` | Ops (poll global) | P0 | Branché | `EconomyService` : `raw=1` sur **toutes** les planètes (csv ≤ 50) toutes les 10 s — accumule la production et fait avancer les files. `user` = PublicUser (id/username) seulement |
| `ImproveResearch` | W | research, planet | — | `objects/research.js` | Science | P5 | À faire | Clé réelle `combustionDrive` — démo retirée en P1c |
| `RecruitTroop` | W | planet, type, qty | — | `objects/planet.js` | Tactical | P5 | À faire | VR : Infantry×1 codé en dur |
| `RefreshStats` | W | planet | — | `objects/planet.js` | Ops | P5 | À faire |  |
| `RenamePlanet` | W | id, name | `Stations/OpsConsole.cs` | `objects/planet.js` | Ops | P5 | Branché | Serveur : 1–32 chars, lettres/chiffres/espaces/-_. ; `error:invalidPlanetName` |
| `SpeedupBuilding` | W | planet | `Stations/OpsConsole.cs` | `scenes/planet.js` | Ops | P5 | Branché | Coût Nova affiché avant (gratuit ≤ 60 s, sinon max(10, ⌈6·min^0.82⌉)) |
| `SpeedupResearch` | W | — | — | `scenes/research.js` | Science | P5 | À faire |  |
| `UpgradeBuilding` | W | buildingtype, planet | `Crew/CrewLines.cs` +1 | `objects/planet.js` | Ops | P5 | Branché | Console Ops : devis serveur (coût × niveau cible, temps × computer), « Ajouter à la file » si chantier actif ; réponse texte (niveau) ou JSON `queued` |

## Combat

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddFleetToBattle` | W | battleid, fleetid | — | `objects/fleet.js` | Tactical | P5 | À faire |  |
| `BattleDoAction` | W | battleid, fleetid, bship_id, action, subaction, skill_id, target_bship_id, target_q, target_r | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P0 | Branché | Envoyer `subaction` + `battle_subaction` (jamais `action`) ; `skill_id` optionnel (omit / vide / `0` pour un move) |
| `BattleEndFleetTurn` | W | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché |  |
| `CheckPlanetAttack` | R | planet | — | `objects/planet.js` | Tactical | P5 | À faire |  |
| `DoTurnBattle` | W | battleid, fleetid, action, target | — | — | Tactical | — | Hors scope | Legacy, non utilisé par le web |
| `FleetAttackPlanet` | W | fleet, planet | `Crew/CrewLines.cs` +1 | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `GetBattle` | R | battleid | — | — | Tactical | — | Hors scope | Legacy |
| `GetBattleState` | R | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché |  |
| `GetMyBattles` | R | — | `Vfx/HexBattleController.cs` | `scenes/galaxy.js` | Tactical | P5 | Branché |  |
| `GetPendingBattles` | R | systemid, planetid | — | — | Tactical | P5 | À faire |  |
| `MakeBattle` | W | systemid, fleets, planetid? | `Crew/CrewLines.cs` +2 | `objects/fleet.js` | Tactical | fleets = mon vaisseau + cibles, `planetid` seulement si ≠ 0 (0 vs pirates), puis `UpdateBattle` (web startTacticalBattle) | Branché | fleets = mon vaisseau + cibles, `planetid` seulement si ≠ 0 (0 vs pirates), puis `UpdateBattle` (web startTacticalBattle) |
| `RemoveFleetFromBattle` | W | battleid, fleetid | — | `scenes/battle.js` | Tactical | P5 | À faire |  |
| `SetFleetState` | W | battleid, fleetid, auto, ready | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché |  |
| `UpdateBattle` | W | battleid | `Vfx/HexBattleController.cs` | `objects/fleet.js` | Tactical | P5 | Branché |  |

## Jumpgate

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `GetJumpgateDestinations` | R | planet | — | `objects/planet.js` | Helm | P5 | À faire |  |
| `SendFleetToJumpgate` | W | fleet, targetPlanet | — | `objects/planet.js` | Helm | P5 | À faire |  |

## Stargate

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `CloseStargateConnection` | W | planet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `DispatchStargateMission` | W | originPlanet, missionType, mineral?, crystal?, biomass?, troopType?, troopQty? | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `GetKnownAddresses` | R | planet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `GetStargateConnectionStatus` | R | planet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `GetStargateMissions` | R | — | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `OpenStargateConnection` | W | originPlanet, targetPlanet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `ResolveStargateAddress` | W | originPlanet, address | — | `objects/planet.js` | Comms | P5 | À faire |  |

## Social (chat, mail)

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddChat` | W | message | — | `objects/chat.js` | Comms | P5 | À faire |  |
| `DeleteMail` | W | id | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `GetChat` | R | lastid? | — | `objects/chat.js` | Comms | P5 | À faire | VR affiche du JSON brut |
| `GetMail` | R | id | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `GetMailUnreadCount` | R | — | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `GetMails` | R | folder | — | `ui/MailWindowUI.js` | Comms | P5 | À faire | VR affiche du JSON brut |
| `GetPrivateConversations` | R | — | — | `ui/PanelChatUI.js` | Comms | P5 | À faire |  |
| `GetPrivateMessages` | R | contact_id, lastid | — | `ui/PanelChatUI.js` | Comms | P5 | À faire |  |
| `SearchPlayers` | R | query | — | — | Comms | P5 | À faire |  |
| `SendMail` | W | recipient, subject, content | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `SendPrivateMessage` | W | contact_id, message | — | — | Comms | P5 | À faire |  |

## Guerre

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AcceptPeaceOffer` | W | war | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `AcceptWarDemands` | W | war | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `CancelPeaceOffer` | W | war | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `DeclareWar` | W | target | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `DeclinePeaceOffer` | W | war | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `GetMyWars` | R | — | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `GetWarDetails` | R | war | — | — | Comms | P5 | À faire |  |
| `OfferPeace` | W | war | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |
| `SurrenderWar` | W | war | — | `ui/WarsWindowUI.js` | Comms | P5 | À faire |  |

## Alliance

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AcceptAllianceApplication` | W | application | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `AcceptAllianceInvite` | W | invite | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `ApplyToAlliance` | W | alliance | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `CancelAllianceInvite` | W | invite | — | — | Comms | P5 | À faire |  |
| `CreateAlliance` | W | name, tag, description? | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `DeclineAllianceApplication` | W | application | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `DeclineAllianceInvite` | W | invite | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `DisbandAlliance` | W | — | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `GetAllianceInvites` | R | — | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `GetAlliances` | R | — | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `GetMyAlliance` | R | — | `App/DiplomacyIndex.cs` | `ui/AllianceWindowUI.js` | Comms | P5 | Branché |  |
| `InviteToAlliance` | W | target | — | — | Comms | P5 | À faire |  |
| `KickAllianceMember` | W | target | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `LeaveAlliance` | W | — | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `SetAllianceMemberRole` | W | target, role | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `TransferAllianceLeadership` | W | target | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |
| `UpdateAllianceDescription` | W | description? | — | `ui/AllianceWindowUI.js` | Comms | P5 | À faire |  |

## Empire / progression / shop

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddEmpirePolicy` | W | policy | — | `objects/policy.js` | Conseil | P6 | À faire |  |
| `BuyShopItem` | W | item | — | `ui/ShopWindowUI.js` | Conseil | P6 | À faire |  |
| `CreateEmpire` | W | empireName, bgColor, shape1, shape2, shape3, color1, color2, color3, authority, ethics, speciesName, speciesType, traitPos1, traitPos2, traitNeg1, traitNeg2, planetName, empireBio, leaderName, leaderSex, leaderTitle, heirTitle, leaderTraits, shipPrefix | `UI/EmpireCreationWizard.cs` | — | Conseil | P3 | Branché | Compte sans empire → SAS ; miroir create-empire.php ; `GetMeEmpire` renvoie `error:noEmpire` |
| `EquipShopItem` | W | item | — | `ui/ShopWindowUI.js` | Conseil | P6 | À faire |  |
| `GetAchievements` | R | — | — | `scenes/galaxy.js` | Conseil | P6 | À faire |  |
| `GetActivity` | R | lastid | — | `objects/activity.js` | Comms | P6 | À faire |  |
| `GetAuthorities` | R | — | `UI/EmpireCreationWizard.cs` | `ui/EmpireHubUI.js` | Conseil | P6 | Branché |  |
| `GetDailyObjectives` | R | — | — | — | Conseil | P6 | À faire | Web utilise `GetProgressionObjectives` |
| `GetEmpire` | R | user | — | `scripts/user.js` | Comms | P5 | À faire |  |
| `GetEmpires` | R | — | `App/DiplomacyIndex.cs` | `objects/empire.js` | Comms | P5 | Branché |  |
| `GetLeaderTraits` | R | — | `UI/EmpireCreationWizard.cs` | — | Conseil | P3 | Branché | Liste lore pour CreateEmpire |
| `GetMeEmpire` | R | — | `App/AuthManager.cs` +1 | `objects/policy.js` | Système (boot) | P0 | Branché | `error:noEmpire` si compte sans empire (flux CreateEmpire) |
| `GetMonthlyObjectives` | R | — | — | — | Conseil | P6 | À faire |  |
| `GetNovaTopupHistory` | R | — | — | — | Conseil | P6 | À faire |  |
| `GetNovaTopupPacks` | R | — | — | `ui/ShopWindowUI.js` | Conseil | P6 | À faire |  |
| `GetPolitics` | R | — | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `GetProgressionObjectives` | R | — | — | `ui/ProgressionWindowUI.js` | Conseil | P6 | À faire |  |
| `GetRelation` | R | user1, user2 | — | `objects/empire.js` | Comms | P5 | À faire |  |
| `GetShopData` | R | — | — | `ui/ShopWindowUI.js` | Conseil | P6 | À faire |  |
| `GetSpeciesTraits` | R | — | `UI/EmpireCreationWizard.cs` | `objects/specy.js` | Conseil | P6 | Branché |  |
| `GetSpeciesTypes` | R | — | `UI/EmpireCreationWizard.cs` | `objects/specy.js` | Conseil | P6 | Branché |  |
| `GetWeeklyObjectives` | R | — | — | — | Conseil | P6 | À faire |  |
| `RenameEmpire` | W | name | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `SetAuthority` | W | authority | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `SetPolitics` | W | category, option | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `UpdateEmpireFlag` | W | flag | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `UpdateSpecy` | W | empire, name?, type_id?, traits? | — | `objects/specy.js` | Conseil | P6 | À faire |  |

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

### Restants

- **`GetActivity`** : entrées toujours stockées en anglais / FR brut en DB — migration clé+params non faite.
- **Flux VR CreateEmpire** : ✅ livré côté VR (`UI/EmpireCreationWizard.cs`) — `error:noEmpire` → assistant du sas → `CreateEmpire` → `GetMeEmpire` → Bridge.
- **Plafonds d'empire** : ✅ `GetConfigs.empire.maxPolicies` / `leaderTraitsMax` (web `faf0614`, en prod) — lus par l'assistant de création.
- **Propriété des planètes** : ✅ cache `GetSystems` vidé à la planète de départ / `ColonizePlanet` / suppression de compte ; `GetEmpirePlanets` renvoie `systemid` + `slot` (web `c94c803`, en prod). Un nouvel empire voyait son monde natal comme non possédé jusqu'à 1 h.
- **Formes du drapeau** : libellés en dur dans `view/create-empire.php` → clés `flagShape_<id>` (missing-keys).

### Spec livrée — `CreateEmpire`

Miroir de `controller/create-empire.php` via `CreateEmpireForUser` : mêmes validations / effets. Auth token. Requis : `empireName`. Optionnels : drapeau, `authority`, `ethics` (csv), espèce / traits, `planetName`, profil lore (`leaderTraits` csv ≤ 3 via `GetLeaderTraits`). Retour = JSON enrichi type `GetMeEmpire`, ou `error:<clé>`.
