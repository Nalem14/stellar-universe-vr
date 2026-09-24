# PARITY — Stellar Universe VR ↔ `actionjs.php`

Généré depuis `action-api.json` (152 actions), `actionjs.php` et un grep des deux clients. Référence web : `/Users/thommy/Websites/stellar-universe`. Roadmap : [`ROADMAP.md`](ROADMAP.md).

**Règle** : chaque feature livrée met à jour sa ligne. Avant de coder, lire l'implémentation web (colonne *Web*) + `model/*.php`. On adapte la jouabilité au pont VR ; le contrat serveur reste strict. La VR ne renvoie **jamais** au web.

## Légende

- **Statut** : `Branché` (appel fonctionnel en VR, UX à finir) · `Démo` (appelé mais valeurs en dur / réponse ignorée ou brute) · `Cassé` · `À faire` · `Hors scope` (legacy / serveur only / interdit par AGENTS).
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
| Flotte | 12 | 23 | 52 % |
| Vaisseau / chantier | 3 | 9 | 33 % |
| Planète / bâtiments / recherche | 7 | 17 | 41 % |
| Combat | 8 | 14 | 57 % |
| Jumpgate | 1 | 2 | 50 % |
| Stargate | 1 | 7 | 14 % |
| Social (chat, mail) | 2 | 11 | 18 % |
| Guerre | 1 | 9 | 11 % |
| Alliance | 1 | 17 | 6 % |
| Empire / progression / shop | 4 | 25 | 16 % |
| **Total** | **49** | **152** | **32 %** |

Appelées par le client web : 134/152. « Appelée » ≠ « finie » : voir la colonne *Statut*.

## Auth

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `Login` | W | email, password | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché |  |
| `LoginToken` | W | token | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché | Token minté sur le casque, lié à l'IP |
| `Register` | W | email, password, username | `App/AuthManager.cs` | — | Sas (Menu) | — | Branché |  |

## Méta / boot

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `GetConfigs` | R | — | `App/DiplomacyIndex.cs` +1 | `scripts/configs.js` | Système (boot) | P0 | Branché |  |
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
| `GetEmpirePlanets` | R | empire | `Vfx/BridgeViewTeleporter.cs` | `ui/WarsWindowUI.js` | Comms | P0 | Démo | **Bug** : VR appelle sans `empire` (`BridgeViewTeleporter.cs:221`) |
| `GetPlanet` | R | id | — | `objects/planet.js` | Science | P5 | À faire |  |
| `GetSystemAnomalies` | R | systemid | — | `ui/StarWindowUI.js` | Science | P5 | À faire |  |
| `GetSystems` | R | — | `App/BridgeSystemLoader.cs` +2 | `scenes/galaxy.js` | Holo table | P0 | Branché |  |
| `ScanAnomaly` | W | anomaly, fleet | — | `ui/StarWindowUI.js` | Science | P5 | À faire |  |

## Flotte

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddFleetOrderStep` | W | fleet, step | `Vfx/AlcoveSystems.cs` | `objects/fleet.js` | Helm | P4 | Démo |  |
| `ClearFleetOrderQueue` | W | fleet | `Vfx/AlcoveSystems.cs` | `objects/fleet.js` | Helm | P4 | Démo |  |
| `Colonize` | W | ship, planet | `Vfx/AlcoveSystems.cs` | `objects/fleet.js` | Ops | P5 | Démo |  |
| `DepositCargo` | W | fleet, planet, mineral?, crystal?, biomass? | `Vfx/CrewDialogue.cs` | `objects/fleet.js` | Ops | P5 | Branché |  |
| `ExplorePlanet` | W | fleet, planet | `Vfx/AlcoveSystems.cs` +1 | `objects/fleet.js` | Science | P5 | Démo |  |
| `GetAllFleets` | R | — | `App/BridgeSystemLoader.cs` +3 | `scenes/galaxy.js` | Helm | P0 | Branché | Cache serveur 3 s ; VR poll 2,5 s → passer ≥ 3,5 s. Traite les files de flotte. |
| `GetAllFleetsAround` | R | — | — | `scenes/galaxy.js` | Helm | — | Hors scope | Interdit en VR (règle AGENTS) — `GetAllFleets` + filtre client |
| `GetSystemAsteroids` | R | systemid | — | `scenes/system.js` | Helm | P5 | À faire |  |
| `HarvestAsteroid` | W | fleet, asteroid | `Vfx/CrewStationsBuilder.cs` | `objects/fleet.js` | Engineering | P5 | Branché |  |
| `LoadTroops` | W | fleet, planet, troops | — | `objects/fleet.js` | Tactical | P5 | À faire |  |
| `MoveFleetToAsteroid` | W | fleet, asteroid, hyperspace? | `Vfx/CrewDialogue.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | idem hyperspace |
| `MoveFleetToPlanet` | W | fleet, planet, hyperspace? | `Vfx/CrewDialogue.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | idem hyperspace |
| `MoveFleetToSystem` | W | fleet, pos, hyperspace? | `Vfx/CrewDialogue.cs` +2 | `objects/fleet.js` | Helm | P4 | Branché | `hyperspace` = 1 par défaut ; `hyperspace=0` pour sublight |
| `PrlBondFleetToSystem` | W | fleet, system?, pos? | — | `objects/fleet.js` | Helm | P4 | À faire |  |
| `ProcessFleetOrderQueue` | W | fleet | — | — | Helm | — | Hors scope | Géré par cron + `GetAllFleets` ; pas d'UI |
| `RemoveFleetOrderStep` | W | fleet, stepIndex | — | `objects/fleet.js` | Helm | P4 | À faire |  |
| `RenameFleet` | W | id, name | — | `objects/fleet.js` | Helm | P5 | À faire |  |
| `SetFleetOrderQueue` | W | fleet, queue, loop? | — | `objects/fleet.js` | Helm | P4 | À faire |  |
| `SpeedupFleetTravel` | W | fleet | — | `scripts/helper.js` | Helm | P5 | À faire |  |
| `ToggleFleetQueueLoop` | W | fleet, loop? | — | `objects/fleet.js` | Helm | P4 | À faire |  |
| `UnloadTroops` | W | fleet, planet, troops | — | `objects/fleet.js` | Tactical | P5 | À faire |  |
| `UpdateFleetDefendPosition` | W | id, position | `Vfx/CrewStationsBuilder.cs` | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `WithdrawCargo` | W | fleet, planet, mineral?, crystal?, biomass? | `Vfx/CrewDialogue.cs` | `objects/fleet.js` | Ops | P5 | Branché |  |

## Vaisseau / chantier

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddShip` | W | type, planet | `Vfx/AlcoveSystems.cs` | `objects/fleet.js` | Engineering | P5 | Démo | VR : ShipCore codé en dur |
| `AddToFleet` | W | fleet, ship, planet? | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `CancelQueuedShip` | W | id | — | `scenes/planet.js` | Engineering | P5 | À faire |  |
| `DelShip` | W | ship | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `DelToFleet` | W | fleet, ship | — | `objects/fleet.js` | Engineering | P5 | À faire |  |
| `GetShipLayout` | R | fleet | `Vfx/AlcoveSystems.cs` +1 | `ui/ShipBuilderUI.js` | Engineering | P5 | Branché |  |
| `PlaceShipModule` | W | ship, fleet, gx, gy | `Vfx/AlcoveSystems.cs` | `ui/ShipBuilderUI.js` | Engineering | P5 | Démo |  |
| `RemoveShipModule` | W | ship | — | `ui/ShipBuilderUI.js` | Engineering | P5 | À faire |  |
| `SpeedupShipyard` | W | planet, ship? | — | `scenes/planet.js` | Engineering | P5 | À faire |  |

## Planète / bâtiments / recherche

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AnswerPlanetDecision` | W | planet, decision, choice | — | `objects/planet.js` | Ops | P5 | À faire |  |
| `BuildDefenseUnit` | W | planet, type, qty | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Tactical | P5 | Démo | VR : MissileTurret×1 codé en dur |
| `CancelQueuedBuilding` | W | id | — | `scenes/planet.js` | Ops | P5 | À faire |  |
| `CancelQueuedResearch` | W | id | — | `scenes/research.js` | Science | P5 | À faire |  |
| `CheckBuildingQueue` | R | planet | — | `view/game.php` | Ops | P5 | À faire |  |
| `CheckResearchQueue` | R | — | — | `view/game.php` | Science | P5 | À faire |  |
| `CheckShipQueue` | R | planet | `Vfx/AlcoveSystems.cs` | `view/game.php` | Engineering | P5 | Démo |  |
| `DowngradeBuilding` | W | buildingtype, planet | — | `objects/planet.js` | Ops | P5 | À faire |  |
| `GetPlanetDecisions` | R | planet | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Ops | P5 | Démo |  |
| `GetResource` | R | planet, raw? | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Ops (poll global) | P0 | Branché | Accumule la production : poll `raw=1` sur **toutes** les planètes (csv ≤ 50) |
| `ImproveResearch` | W | research, planet | `Vfx/AlcoveSystems.cs` | `objects/research.js` | Science | P5 | Démo | VR : `combustion` codé en dur |
| `RecruitTroop` | W | planet, type, qty | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Tactical | P5 | Démo | VR : Infantry×1 codé en dur |
| `RefreshStats` | W | planet | — | `objects/planet.js` | Ops | P5 | À faire |  |
| `RenamePlanet` | W | id, name | — | `objects/planet.js` | Ops | P5 | À faire |  |
| `SpeedupBuilding` | W | planet | — | `scenes/planet.js` | Ops | P5 | À faire |  |
| `SpeedupResearch` | W | — | — | `scenes/research.js` | Science | P5 | À faire |  |
| `UpgradeBuilding` | W | buildingtype, planet | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Ops | P5 | Démo | VR : `metalMine` codé en dur |

## Combat

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddFleetToBattle` | W | battleid, fleetid | — | `objects/fleet.js` | Tactical | P5 | À faire |  |
| `BattleDoAction` | W | battleid, fleetid, bship_id, action, subaction, skill_id, target_bship_id, target_q, target_r | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P0 | Cassé | **Cassé** : VR envoie `action=move` → collision avec `action=` ; utiliser `subaction` |
| `BattleEndFleetTurn` | W | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché |  |
| `CheckPlanetAttack` | R | planet | — | `objects/planet.js` | Tactical | P5 | À faire |  |
| `DoTurnBattle` | W | battleid, fleetid, action, target | — | — | Tactical | — | Hors scope | Legacy, non utilisé par le web |
| `FleetAttackPlanet` | W | fleet, planet | `Vfx/CrewStationsBuilder.cs` | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `GetBattle` | R | battleid | — | — | Tactical | — | Hors scope | Legacy |
| `GetBattleState` | R | battleid, fleetid | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché |  |
| `GetMyBattles` | R | — | `Vfx/HexBattleController.cs` | `scenes/galaxy.js` | Tactical | P5 | Branché |  |
| `GetPendingBattles` | R | systemid, planetid | — | — | Tactical | P5 | À faire |  |
| `MakeBattle` | W | systemid, fleets, planetid? | `Vfx/HexBattleController.cs` | `objects/fleet.js` | Tactical | P5 | Branché |  |
| `RemoveFleetFromBattle` | W | battleid, fleetid | — | `scenes/battle.js` | Tactical | P5 | À faire |  |
| `SetFleetState` | W | battleid, fleetid, auto, ready | `Vfx/HexBattleController.cs` | `scenes/battle.js` | Tactical | P5 | Branché |  |
| `UpdateBattle` | W | battleid | `Vfx/HexBattleController.cs` | `objects/fleet.js` | Tactical | P5 | Branché |  |

## Jumpgate

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `GetJumpgateDestinations` | R | planet | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Helm | P5 | Démo |  |
| `SendFleetToJumpgate` | W | fleet, targetPlanet | — | `objects/planet.js` | Helm | P5 | À faire |  |

## Stargate

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `CloseStargateConnection` | W | planet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `DispatchStargateMission` | W | originPlanet, missionType, mineral?, crystal?, biomass?, troopType?, troopQty? | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `GetKnownAddresses` | R | planet | `Vfx/AlcoveSystems.cs` | `objects/planet.js` | Comms | P5 | Démo |  |
| `GetStargateConnectionStatus` | R | planet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `GetStargateMissions` | R | — | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `OpenStargateConnection` | W | originPlanet, targetPlanet | — | `objects/planet.js` | Comms | P5 | À faire |  |
| `ResolveStargateAddress` | W | originPlanet, address | — | `objects/planet.js` | Comms | P5 | À faire |  |

## Social (chat, mail)

| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |
|---|---|---|---|---|---|---|---|---|
| `AddChat` | W | message | — | `objects/chat.js` | Comms | P5 | À faire |  |
| `DeleteMail` | W | id | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `GetChat` | R | lastid? | `Vfx/AlcoveSystems.cs` | `objects/chat.js` | Comms | P5 | Démo | VR affiche du JSON brut |
| `GetMail` | R | id | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `GetMailUnreadCount` | R | — | — | `ui/MailWindowUI.js` | Comms | P5 | À faire |  |
| `GetMails` | R | folder | `Vfx/AlcoveSystems.cs` | `ui/MailWindowUI.js` | Comms | P5 | Démo | VR affiche du JSON brut |
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
| `GetMyWars` | R | — | `Vfx/AlcoveSystems.cs` | `ui/WarsWindowUI.js` | Comms | P5 | Démo |  |
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
| `GetMyAlliance` | R | — | `App/DiplomacyIndex.cs` +1 | `ui/AllianceWindowUI.js` | Comms | P5 | Branché |  |
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
| `EquipShopItem` | W | item | — | `ui/ShopWindowUI.js` | Conseil | P6 | À faire |  |
| `GetAchievements` | R | — | — | `scenes/galaxy.js` | Conseil | P6 | À faire |  |
| `GetActivity` | R | lastid | — | `objects/activity.js` | Comms | P6 | À faire |  |
| `GetAuthorities` | R | — | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `GetDailyObjectives` | R | — | `Vfx/AlcoveSystems.cs` | — | Conseil | P6 | Démo | Web utilise `GetProgressionObjectives` |
| `GetEmpire` | R | user | — | `scripts/user.js` | Comms | P5 | À faire |  |
| `GetEmpires` | R | — | `App/DiplomacyIndex.cs` +1 | `objects/empire.js` | Comms | P5 | Branché |  |
| `GetMeEmpire` | R | — | `App/AuthManager.cs` +1 | `objects/policy.js` | Système (boot) | P0 | Branché |  |
| `GetMonthlyObjectives` | R | — | — | — | Conseil | P6 | À faire |  |
| `GetNovaTopupHistory` | R | — | — | — | Conseil | P6 | À faire |  |
| `GetNovaTopupPacks` | R | — | — | `ui/ShopWindowUI.js` | Conseil | P6 | À faire |  |
| `GetPolitics` | R | — | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `GetProgressionObjectives` | R | — | — | `ui/ProgressionWindowUI.js` | Conseil | P6 | À faire |  |
| `GetRelation` | R | user1, user2 | — | `objects/empire.js` | Comms | P5 | À faire |  |
| `GetShopData` | R | — | `Vfx/AlcoveSystems.cs` | `ui/ShopWindowUI.js` | Conseil | P6 | Démo |  |
| `GetSpeciesTraits` | R | — | — | `objects/specy.js` | Conseil | P6 | À faire |  |
| `GetSpeciesTypes` | R | — | — | `objects/specy.js` | Conseil | P6 | À faire |  |
| `GetWeeklyObjectives` | R | — | — | — | Conseil | P6 | À faire |  |
| `RenameEmpire` | W | name | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `SetAuthority` | W | authority | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `SetPolitics` | W | category, option | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `UpdateEmpireFlag` | W | flag | — | `ui/EmpireHubUI.js` | Conseil | P6 | À faire |  |
| `UpdateSpecy` | W | empire, name?, type_id?, traits? | — | `objects/specy.js` | Conseil | P6 | À faire |  |

## Écarts serveur à traiter côté web

Le client VR ne contourne jamais un manque serveur par le site. Ces points sont à implémenter / documenter côté `stellar-universe`.

- **`ok:sublight_not_enough_modules`** : renvoyé par `MoveFleet*` mais absent de `response.success_forms`.
- **Libellés codés en dur côté web** (onglets PlanetScene / EmpireHub, catégories de recherche `research.js`, noms de skills `BATTLE_SKILL_DEFS`, `DECISION_DEFS` FR-only, journal de colonisation, entrées `GetActivity` en anglais brut) : besoin de clés i18n — voir [`i18n/missing-keys.md`](i18n/missing-keys.md).
- **Création d'empire : aucune action API** (le web passe par le POST `controller/create-empire.php`). Bloquant pour tout compte créé en VR. Spec ci-dessous.

### Spec proposée — `CreateEmpire`

Miroir exact de `controller/create-empire.php` : mêmes validations, mêmes effets (`AddEmpire` → `SetEmpireAuthority` → `addEmpirePolicy` → `CreateSpecy` / `UpdateSpecy` → `GetFirstPlanet` + `RenamePlanet` → `SetEmpireProfile`). Les noms de params reprennent ceux du formulaire.

- **Auth** : oui (token). GET url-encodé comme toutes les actions. Refus si l'utilisateur a déjà un empire.
- **Requis** : `empireName` (≥ 3 caractères, unique — erreurs `insert3Character`, `empireExist`, `fillAllField`).
- **Drapeau** : `bgColor`, `shape1..3`, `color1..3` (mêmes défauts que le web : `#001f3f`, `none`, `#ff4136` / `#2ecc40` / `#ffdc00`).
- **Gouvernement** : `authority` (id `GetAuthorities`), `ethics` (csv d'ids de policies, tronqué à `EMPIRE_MAX_POLICIES`).
- **Espèce** : `speciesName` (défaut = nom d'empire), `speciesType` (id `GetSpeciesTypes`), `traitPos1`, `traitPos2`, `traitNeg1`, `traitNeg2` (ids `GetSpeciesTraits`, type 1 / type 0).
- **Monde natal** : `planetName` (optionnel ; `GetFirstPlanet` attribue la planète — erreur `noStarterPlanetAvailable`).
- **Profil (lore)** : `empireBio`, `leaderName`, `leaderSex`, `leaderTitle`, `heirTitle`, `leaderTraits` (csv ≤ 3, valeurs de `GetLeaderTraits`), `shipPrefix`.
- **Retour** : JSON `GetMeEmpire` du nouvel empire, ou `error:<message localisé>`.
- **Action de lecture complémentaire** : `GetLeaderTraits` (aujourd'hui interne au formulaire). Les policies viennent de `GetConfigs.policies`.
- **Flux VR** (ROADMAP P3) : le Menu détecte « pas d'empire » via `GetMeEmpire` → séquence diegetic de création dans le sas → `CreateEmpire` → onboarding éthiques / espèce → Bridge.
