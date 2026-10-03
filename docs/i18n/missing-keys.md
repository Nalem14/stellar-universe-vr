# Clés i18n manquantes — à intégrer côté serveur

Ce fichier recense les clés dont le client VR a besoin et qui manquent dans `assets/langs/{en,fr}.json` du repo web (servi par `GetTranslations`).

**Circuit** :
- L'agent VR liste ici la clé, avec ses textes EN et FR.
- Thommy l'intègre côté web (admin Translate, ou commit direct sur `assets/langs/*.json`) — désormais dans **les dix langues** d'un coup : EN et FR tels que listés, les huit autres traduites en reprenant la terminologie déjà en place.
- La ligne est ensuite retirée de ce fichier.
- L'outillage est dans [`docs/tools/i18n.py`](../tools/i18n.py) : `check` (état des dix langues), `pending` (clés listées ici et encore absentes), `add translations.json` (pose les clés dans les dix langues, après la dernière de leur famille) et `cleanup` (retire les sections intégrées et ajoute la ligne de traçabilité). Il lit les sections « À intégrer » **quelle que soit leur mise en forme** — séparateurs `---` absents, colonne de contexte en plus, ordre des sections — donc il n'y a rien à réécrire à chaque lot.

Tant qu'une clé est absente, la VR affiche **la clé brute**. En Editor, elle est aussi ajoutée à `su-missing-trans-keys.txt`. Le client n'invente jamais de texte de secours.

**Conventions** :
- **Clé native d'abord** : si une clé web existe déjà (même avec une autre casse), le client VR l'utilise telle quelle.
- **UI propre à la VR** : `vr.<zone>.<nom>`, par exemple `vr.menu.continue`.
- **Répliques crew** : `crew.<role>.<event>.<n>`. Les rôles sont `helm`, `tactical`, `engineering`, `science`, `comms` et `ops`. `<n>` part de 1, et `BarkDirector` tire une variante au hasard sans répétition.
- **Paramètres** : `{0}`, `{1}`… (`Trans.Format`, `string.Format` en culture invariante).
- Pour les répliques, les ordres et les échecs, le ton reprend celui du journal de colonisation web : l'équipage s'adresse au joueur en « Commandant ».

## À intégrer — PC (clavier / souris) et mobile (tactile)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.pc.prompt` | <b>[E]</b> or <b>[Left click]</b> {0} | <b>[E]</b> ou <b>[Clic gauche]</b> {0} | Invite sous le réticule PC ; {0} = l'action (libellé du bouton visé, ou une des clés ci-dessous) |
| `vr.pc.sit` | Take the command seat | S'asseoir au poste de commandement | Invite PC sur le fauteuil |
| `vr.pc.door` | Go through the door | Passer la porte | Invite PC sur une porte |
| `vr.pc.interact` | Interact | Interagir | Invite PC sur un objet saisissable (caisse du marché, jeton…) |
| `vr.pc.commandBar` | <b>[F]</b> Fleets  •  <b>[M]</b> Galaxy / system map  •  <b>[C]</b> Comms  •  <b>[O]</b> Operations  •  <b>[T]</b> Tactical  •  <b>[Space]</b> Stand up | <b>[F]</b> Flottes  •  <b>[M]</b> Carte galaxie / système  •  <b>[C]</b> Comms  •  <b>[O]</b> Opérations  •  <b>[T]</b> Tactique  •  <b>[Espace]</b> Se lever | Barre de raccourcis PC, assis au poste de commandement |
| `vr.pc.cursorHint` | <b>[Tab]</b> Free cursor  •  <b>[I]</b> Wrist  •  <b>[Esc]</b> Menu | <b>[Tab]</b> Curseur libre  •  <b>[I]</b> Bracelet  •  <b>[Échap]</b> Menu | Rappel PC en bas à droite |
| `vr.pc.grabOnly` | <b>[E]</b> Grab | <b>[E]</b> Saisir | Invite PC sur un objet saisissable sans nom |
| `vr.pc.grabPrompt` | <b>[E]</b> Grab {0} | <b>[E]</b> Saisir {0} | Invite PC sur un objet saisissable ; {0} = son nom (vaisseau, module…) |
| `vr.pc.dropHint` | <b>[Left click]</b> Drop  •  <b>[Right click]</b> / <b>[Esc]</b> Put back | <b>[Clic gauche]</b> Poser  •  <b>[Clic droit]</b> / <b>[Échap]</b> Reposer | Invite PC pendant qu'un objet est tenu |
| `vr.mobile.dropHint` | Tap the target to drop it | Touchez la cible pour poser | Bandeau mobile pendant qu'un objet est tenu |
| `vr.mobile.wrist` | Status | Bracelet | Bouton mobile : affiche / masque le bracelet (heure, alerte, affaires) |
| `vr.menu.mouseSens` | Mouse sensitivity | Sensibilité souris | Menu rapide (PC) |
| `vr.menu.invertY` | Invert vertical look | Inverser l'axe vertical | Menu rapide (PC) |
| `vr.pc.standUp` | Stand up | Se lever | Bouton tactile du poste de commandement (mobile) |
| `vr.mobile.map` | Map | Carte | Bouton tactile : bascule galaxie / système de la table holo |

## Intégré côté web

- Clés `vr.*`, `crew.*`, `buildingDesc_*`, `colonyLog_*`, `relation_*` dans `assets/langs/{fr,en}.json`.
- Clés des Quartiers du commandant (`vr.quarters.*`, `traitEffect_defense` / `trade` / `diplomacy`) et toasts du Hub web (`authorityUpdated`, `policyAdded`) dans `assets/langs/{fr,en}.json`.
- Clés P3 (création d'empire : `vr.create.*`, `flagShape_*`, `traitEffect_*`), P5 cale sèche (`vr.dock.*`, `vr.yard.*`, `vr.module.family.*`), P5 laboratoire (`vr.lab.*`, `vr.research.*`), P5 relevé planétaire (`credits`, `vr.survey.*`) et P5.5 table tactique (`vr.table.*`, `vr.seat.*`, `vr.battle.*`) dans `assets/langs/{fr,en}.json`.
- Clés P5 cale sèche — modèles (`vr.dock.templatePlaced`, `fleetMustBeDocked`, `templateNotFound`, `templateEmpty`, `notFound`), P5.5 H3b file d'ordres 3D (`vr.table.removeStep`, `vr.table.queueRerouted`), P5 Tactical armurerie (`vr.armory.*`, `crew.tactical.*`) et P5 Comms (`vr.comms.*`, `vr.gate.*`, `crew.comms.*`) dans `assets/langs/{fr,en}.json`. Chambre diplomatique (`vr.diplo.*`, `crew.comms.warLaunched` / `peace` / `warWon` / `warLost` / `application`), aussi réutilisées par `wars-window.hbs` / `alliance-window.hbs`, avec les confirmations web (`confirm*`) et le gabarit des e-mails externes (`email_*`). `fr` et `en` ont désormais exactement le même jeu de clés.
- Contenu lu dynamiquement par le VR (noms = type du serveur) : troupes `Infantry`, `HeavyTrooper`, `ExoArmorTrooper`, `CyberneticVanguard`, `CombatDroneSquad`, `SynthWarrior` et défenses `MissileTurret`, `FlakCannon`, `PlasmaBattery`, `IonDefenseGrid`, `RailgunBastion`, `QuantumShieldArray` (affichés par `ResearchCatalog.Unlocks`) ; descriptions `descBondPRLModule` et `descTroopBay` (en). Le VR retombe sur la casse native (`Trans.Get`) pour `ScienceModule` / `TroopBay`, absents en PascalCase côté web.
- Remaps client (§1) : le code VR utilise les clés natives (`fleets`, `asteroidField`, `moveToSystem`, `defendPosition*`, `explorePlanet`, `depositCargo`, `withdrawCargo`, `harvestAsteroid`, `attackOrbit`, `cancel`, `sublight` / `hyperdrive`, `buildingDesc_<type>`, plaques `vr.station.*`, etc.).

- Helm (P5 — portail de saut, accélération, astéroïdes) : `vr.jumpgate.ready` / `recharging` / `none` et répliques `crew.helm.jumpgate.1` / `jumpgate.2` / `speedup.1` dans `assets/langs/{fr,en}.json`.

- P7 alertes du vaisseau : `crew.tactical.alertRed.1` / `.2`, `alertAmber.1` / `.2`, `alertClear.1` / `.2` dans **les dix langues**, terminologie alignée sur `vr.screen.redAlert` et `vr.screen.hostiles`.
- `vr.tutorial.step{1..16}.text` dans **les dix langues** (titres et boutons réutilisent les clés natives `tutorial.stepN.title`, `guide`, `previous`, `next`, `skip`, `end`).
- `vr.ops.allAnswered` dans **les dix langues**.
- `vr.watch.battle`, `vr.watch.battleWait`, `vr.watch.board`, `vr.watch.building`, `vr.watch.enter`, `vr.watch.explore`, `vr.watch.harvest`, `vr.watch.idle`, `vr.watch.link`, `vr.watch.research`, `vr.watch.siege`, `vr.watch.transit`, `vr.watch.yourTurn` dans **les dix langues**.
- `vr.pirate.leaves`, `vr.pirate.name` dans **les dix langues**.
- `vr.console.amberAlert`, `vr.console.battleStations`, `vr.console.clear`, `vr.console.noTraffic`, `vr.console.ready`, `vr.console.unavailable`, `vr.fx.glow`, `vr.fx.off`, `vr.fx.on` dans **les dix langues**.
- `vr.menu.recenter`, `vr.menu.title`, `vr.menu.turn`, `vr.menu.turnSmooth`, `vr.menu.turnSnap`, `vr.menu.volume` dans **les dix langues**.
- `vr.menu.move`, `vr.menu.moveSmooth`, `vr.menu.moveTeleport`, `vr.menu.seated`, `vr.menu.vignette` dans **les dix langues**.
- `vr.survey.active`, `vr.survey.done`, `vr.survey.gained` dans **les dix langues**.
- `vr.dock.pickShipFirst`, `vr.ops.loadEnergy`, `vr.ops.loadJobs`, `vr.ops.loadPower` dans **les dix langues**.
- `vr.coords.go`, `vr.coords.none`, `vr.coords.title`, `vr.galaxy.send`, `vr.galaxy.zoomHere` dans **les dix langues**.
- `vr.cargo.max`, `vr.cargo.step`, `vr.dock.shelf.confirmBuild`, `vr.dock.shelf.confirmQueue`, `vr.dock.shelf.fabricate`, `vr.dock.shelf.grab`, `vr.dock.shelf.inProduction`, `vr.dock.shelf.inStock`, `vr.dock.shelf.showAll`, `vr.dock.shelf.showStock`, `vr.dock.shelf.title`, `vr.helm.returnHome`, `vr.mining.active`, `vr.mining.done`, `vr.mining.haul`, `vr.ops.loadWorkforce`, `vr.queue.busyHint`, `vr.queue.chainHint`, `vr.queue.hint`, `vr.watch.researchWhere`, `vr.watch.shipyard`, `vr.yard.planetStock` dans **les dix langues**.
- `descCloneBay`, `descCommandBridge`, `descCommunicationArray`, `descCorridor`, `descCrewQuarters`, `descDroneBay`, `descEnergyBattery`, `descEnergyReactor`, `descGunTurret`, `descHatchCorridor`, `descHeatCannon`, `descHydroponicBay`, `descKitchen`, `descLuxuryQuarters`, `descMedicalBay`, `descMindControlModule`, `descOpenBay`, `descOxygenSystem`, `descPrlBond`, `descResearchLab`, `descRestArea`, `descShower`, `descSolarPanel`, `descStargateTriangulation`, `descStasisPod`, `descToilet`, `seasonTier_bronze`, `seasonTier_diamond`, `seasonTier_gold`, `seasonTier_grandmaster`, `seasonTier_master`, `seasonTier_platinum`, `seasonTier_silver`, `vr.research.effect`, `vr.research.feature.defenses`, `vr.research.feature.jumpgate`, `vr.research.feature.prlRange`, `vr.research.feature.triangulation`, `vr.research.feature.troops`, `vr.research.kind.feature`, `vr.research.kind.research`, `vr.season.allianceHint`, `vr.season.breakdown`, `vr.season.cycle`, `vr.season.endRewards`, `vr.season.maxTier`, `vr.season.members`, `vr.season.nextTier`, `vr.season.noAccolades`, `vr.season.noAlliances`, `vr.season.noScores`, `vr.season.none`, `vr.season.novaClaimed`, `vr.season.objectives`, `vr.season.pantheonEmpty`, `vr.season.podium.first`, `vr.season.podium.second`, `vr.season.podium.third`, `vr.season.podium.top10`, `vr.season.podium.top20`, `vr.season.points`, `vr.season.rank`, `vr.season.rankPoints`, `vr.season.score`, `vr.season.score.anomaly`, `vr.season.score.battleWon`, `vr.season.score.bounty`, `vr.season.score.daily`, `vr.season.score.defenseHeld`, `vr.season.score.monthly`, `vr.season.score.planetConquered`, `vr.season.score.stargateCapture`, `vr.season.score.stargateColony`, `vr.season.score.weekly`, `vr.season.seeQuarters`, `vr.season.standing`, `vr.season.tier`, `vr.season.titleHint`, `vr.watch.console.comms`, `vr.watch.console.ops`, `vr.watch.consoles` dans **les dix langues**.
- `vr.comms.readAll`, `vr.comms.readAllDone`, `vr.comms.readAllPartial`, `vr.dock.printer.done`, `vr.dock.printer.idle`, `vr.dock.printer.left`, `vr.dock.printer.queued`, `vr.dock.printer.title`, `vr.gate.ownAddress`, `vr.gate.thisGate`, `vr.quarters.bossGo`, `vr.quarters.bossHere`, `vr.quarters.bossHow`, `vr.quarters.bossNoShip`, `vr.quarters.bossWhere`, `vr.shop.perk.explore_speed`, `vr.shop.perk.mining_speed`, `vr.shop.perk.move_speed`, `vr.shop.perk.production_multiplier`, `vr.shop.perk.queues`, `vr.shop.perk.research_speed`, `vr.shop.perk.xp_multiplier` dans **les dix langues**.
- `vr.dock.recycler.confirm`, `vr.dock.recycler.go`, `vr.dock.recycler.hint`, `vr.dock.recycler.title`, `vr.dock.store.empty`, `vr.dock.store.title`, `vr.dock.takeFromStore` dans **les dix langues**.
- `vr.alert.auto`, `vr.alert.manual`, `vr.alert.normal`, `vr.alert.standDown` dans **les dix langues**.
- `battleSkill_aegis_bubble`, `battleSkill_dock_repair`, `battleSkill_flak_barrage`, `battleSkill_garrison_counter`, `battleSkill_orbital_blackout`, `battleSkill_orbital_cannonade`, `battleSkill_planetary_battery`, `battleSkill_reactor_overcharge`, `battleSkill_station_command`, `vr.battle.err.cannot_move`, `vr.battle.immobile`, `vr.battle.overcharge`, `vr.battle.retreated`, `vr.dock.immobile`, `vr.dock.needStationCore`, `vr.dock.newStation`, `vr.dock.templateStation`, `vr.helm.stationAnchored`, `vr.menu.community`, `vr.menu.discord`, `vr.menu.open`, `vr.menu.website`, `vr.module.compatShip`, `vr.module.compatStation`, `vr.module.filterShip`, `vr.module.filterStation`, `vr.view.citadelHeader`, `vr.view.city`, `vr.view.noStation` dans **les dix langues**.
- `conventionalDrive`, `sublightWithCrystal` dans **les dix langues** (elles n'existaient qu'en fr/en après `f03197d`).
- `crew.engineering.cargoLooted.1`, `crew.ops.marketDispatch.1`, `crew.ops.marketReturned.1`, `crew.tactical.convoyLost.1`, `listingNotActive`, `listingNotFoundOrNotYours`, `originPlanetNotFound` dans **les dix langues**.


## Anomalies (intégré web)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `anomaly_*` / `anomalyDesc_*` | (4 types) | (4 types) | ✅ fr/en — display by `type`, spawn stores keys |
| `vr.anomaly.preview` | +{0} research · +{1} minerals · +{2} crystals · difficulty {3} | +{0} recherche · +{1} minéraux · +{2} cristaux · difficulté {3} | ✅ fr/en |
| `vr.anomaly.rewards` | +{0} research · +{1} minerals · +{2} crystals · +{3} XP | +{0} recherche · +{1} minéraux · +{2} cristaux · +{3} XP | ✅ fr/en |

## Contrats (intégré web)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `bounty_*` / `bountyDesc_*` | (3 types) | (3 types) | ✅ fr/en — display by `target_type` |
| `bounty_need_fleet_onsite` | Send one of your ships… | Envoyez un de vos vaisseaux… | ✅ CompleteBounty |
| `vr.bounty.*` | — | — | ✅ fr/en |

## Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
| `assets/langs/{de,es,it,pt,ru,ko,ja,zh}.json` | Traduits depuis l'anglais (97-99 %) | Même jeu de clés et même ordre que `en.json` ; l'outil admin Translate permet la relecture clé par clé ; toute clé non traduite retombe sur l'anglais |

