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

## À intégrer — forteresses orbitales, sièges planétaires, cité, plaque Communauté (web 69d40af / 600347f)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `battleSkill_planetary_battery` | Ground-to-Space Battery | Batterie Sol-Espace | Compétence de la planète en siège (GetSkillsForPlanet, nom serveur en dur) |
| `battleSkill_flak_barrage` | Planetary Flak Barrage | Barrage Flak Planétaire | idem |
| `battleSkill_garrison_counter` | Garrison Counter-Offensive | Contre-Offensive de Garnison | idem |
| `battleSkill_station_command` | Orbital Command | Commandement Orbital | StationCore (BATTLE_SKILL_DEFS) |
| `battleSkill_orbital_cannonade` | Orbital Cannonade | Canonnière Orbitale | OrbitalDefenseBattery |
| `battleSkill_aegis_bubble` | Aegis Bubble | Bulle Égide | PlanetaryShieldProjector |
| `battleSkill_orbital_blackout` | Orbital Blackout | Brouillage Orbital | OrbitalJammingArray |
| `battleSkill_dock_repair` | Dock Repair | Réparation de Quai | OrbitalGantry |
| `battleSkill_reactor_overcharge` | Citadel Overcharge | Surcharge de Citadelle | CitadelReactor |
| `vr.battle.immobile` | Fixed position | Position fixe | Plateau : touche Déplacer d'une planète / station, et refus d'un déplacement |
| `vr.battle.err.cannot_move` | This unit holds a fixed position. | Cette unité tient une position fixe. | Erreur serveur `cannot_move` (BattleDoAction) |
| `vr.battle.retreated` | Retreated | Repli effectué | Jeton qui quitte le plateau ; bandeau de fin quand tous nos vaisseaux se sont repliés |
| `vr.battle.overcharge` | Overcharge! Shields up, EMP purged | Surcharge ! Boucliers rechargés, IEM purgée | Effet de `reactor_overcharge` |
| `vr.dock.newStation` | + New orbital station | + Nouvelle station orbitale | Cale sèche : AddToFleet avec un StationCore |
| `vr.dock.needStationCore` | Print a Station Core to found an orbital station | Imprimez un Cœur de Station pour fonder une station orbitale | Sous le bouton grisé |
| `vr.dock.immobile` | 0 · anchored | 0 · immobile | Vitesse d'une station dans la fiche de la cale |
| `vr.dock.templateStation` | Blueprints cannot be applied to an orbital station | Les gabarits ne s'appliquent pas à une station orbitale | Refus local (ApplyShipTemplate renverrait le StationCore au hangar, voir PARITY) |
| `vr.module.compatShip` | SHIP | VAISSEAU | Pastille cyan (web 🚀 SHIP, 600347f) |
| `vr.module.compatStation` | STATION | STATION | Pastille ambre (web 🛰 STATION) |
| `vr.module.filterShip` | Ships | Vaisseaux | Filtre du catalogue du chantier |
| `vr.module.filterStation` | Stations | Stations | Filtre du catalogue du chantier |
| `vr.helm.stationAnchored` | Anchored in orbit of {0} — an orbital station has no engine. | Ancrée en orbite de {0} — une station orbitale n'a pas de moteur. | Barre à bord d'une station ({0} = planète) |
| `vr.view.citadelHeader` | Citadel | Citadelle | Plaque et écran mural en vue planète (cité) |
| `vr.view.city` | Citadel of {0} | Citadelle de {0} | Ligne d'état en vue cité ({0} = planète) |
| `vr.view.noStation` | No orbital station yet — assemble a Station Core at the dry dock. | Aucune station orbitale — assemblez un Cœur de Station au chantier naval. | Onglet Stations du téléporteur, vide |
| `vr.menu.community` | Community | Communauté | Plaque du sas (site + Discord) |
| `vr.menu.website` | Official website | Site officiel | Plaque du sas |
| `vr.menu.discord` | Discord | Discord | Plaque du sas |
| `vr.menu.open` | Open in the headset | Ouvrir dans le casque | Plaque du sas : ouvre l'URL dans le navigateur du Quest |

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

