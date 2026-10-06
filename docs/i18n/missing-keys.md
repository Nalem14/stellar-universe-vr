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
- `vr.menu.invertY`, `vr.menu.mouseSens`, `vr.mobile.dropHint`, `vr.mobile.map`, `vr.mobile.wrist`, `vr.pc.commandBar`, `vr.pc.cursorHint`, `vr.pc.door`, `vr.pc.dropHint`, `vr.pc.grabOnly`, `vr.pc.grabPrompt`, `vr.pc.interact`, `vr.pc.prompt`, `vr.pc.sit`, `vr.pc.standUp` dans **les dix langues**.
- `vr.dock.filter.family`, `vr.dock.filter.hull`, `vr.dock.guide.bow`, `vr.dock.guide.cells`, `vr.dock.guide.pickShip`, `vr.dock.guide.place`, `vr.dock.guide.preview`, `vr.dock.guide.remove`, `vr.dock.guide.take`, `vr.research.feature.radarRange` dans **les dix langues**.
- `vr.travel.warn.conventional`, `vr.travel.warn.hyperCrystal`, `vr.travel.warn.hyperModules`, `vr.travel.warn.speedOne` dans **les dix langues**.
- `cargoOnlyFuelReserve`, `vr.fuel.kept`, `vr.fuel.reserve` dans **les dix langues** (réserve carburant par vaisseau, `SetFleetFuelReserve`).


## Salle de recherche (bonus, effets chiffrés) et descriptions des recherches (intégré web, dix langues)

Clés du client (dix langues) :

| Clé | FR | EN |
|---|---|---|
| `vr.research.bonuses` | Bonus de recherche | Research bonuses |
| `vr.research.bonusesEmpty` | Aucun bonus : terminez une recherche pour en débloquer | No bonus yet: finish a research to unlock one |
| `vr.research.allModules` | tous les modules | every module |
| `vr.research.perLevelShort` | par niveau | per level |
| `vr.research.now` | actuel {0} | now {0} |
| `vr.research.cap` | plafond {0} | cap {0} |
| `vr.research.effect.speed` | {0} vitesse — {1} | {0} speed — {1} |
| `vr.research.effect.damage` | {0} dégâts — {1} | {0} damage — {1} |
| `vr.research.effect.armor` | {0} blindage — {1} | {0} armor — {1} |
| `vr.research.effect.shield` | {0} boucliers — {1} | {0} shields — {1} |
| `vr.research.effect.power` | {0} production d'énergie | {0} energy output |
| `vr.research.effect.solarPower` | {0} production des centrales solaires | {0} solar plant output |
| `vr.research.effect.mine` | {0} production de minerai et de cristal | {0} mineral and crystal output |
| `vr.research.effect.food` | {0} production de biomasse | {0} biomass output |
| `vr.research.effect.relation` | {0} relations diplomatiques | {0} diplomatic relations |
| `vr.research.effect.constructionTime` | {0} durée de construction (bâtiments, troupes, défenses) | {0} build time (buildings, troops, defenses) |
| `vr.research.effect.buildingTime` | {0} durée d'amélioration des bâtiments | {0} building upgrade time |
| `vr.research.effect.homeAndFarmBuildingTime` | {0} durée de construction des habitations et des fermes | {0} housing and farm build time |
| `vr.research.effect.colonizationTime` | {0} durée de colonisation | {0} colonization time |
| `vr.research.effect.researchTime` | {0} durée de recherche | {0} research time |
| `vr.research.effect.habitability` | {0} habitabilité des planètes | {0} planet habitability |
| `vr.research.effect.prlRange` | {0} portée du Bond PRL | {0} PRL Bond range |
| `vr.research.effect.scannerRange` | {0} portée des scanners | {0} scanner range |
| `vr.research.col.utility` / `.combat` / `.propulsion` | Utilitaire / Combat / Propulsion | Utility / Combat / Propulsion |
| `vr.research.allWeapons` | toutes les armes | every weapon |
| `vr.research.stat.<stat>` (17) | libellés courts du tableau des bonus (Vitesse, Dégâts, Blindage…) | short bonus table labels (Speed, Damage, Armor…) |
| `vr.dock.effectiveHint` | Valeurs finales, recherches comprises · Coque = armure des modules + 50 de structure par module, comme en combat | Final values, research included · Hull = the modules' armor + 50 structure per module, as in battle |
| `vr.diplo.ranking`, `vr.diplo.rank.empire` / `.economy` / `.research` / `.fleet` / `.defense` / `.score` | Classement des empires / Empire / Économie / Recherche / Flotte / Défense / Score | Empire ranking / Empire / Economy / Research / Fleet / Defense / Score (intégré web, dix langues) |

Descriptions natives réécrites (audit du 2026-10-04, table `$RESEARCH_EFFECTS`), tenues à jour avec les rééquilibrages suivants — propulsion plafonnée au niveau 50, technologie minière, thermodynamique sur le canon thermique, drones sur toutes les ressources, nanites sur les modules, biotechnologie sur les troupes :

| Clé | FR | EN |
|---|---|---|
| `descEnergy` | +3 % de production d'énergie par niveau, toutes centrales. | +3% energy output per level, every plant. |
| `descComputer` | −1 % de durée de construction par niveau (bâtiments, troupes, défenses), jusqu'à −50 %. | −1% build time per level (buildings, troops, defenses), down to −50%. |
| `descCombustionDrive` | +1 % de vitesse par niveau pour les propulseurs à combustion, les boosters et tous les moteurs plus avancés, jusqu'à +50 % (niveau 50). | +1% speed per level for combustion thrusters, boosters and every more advanced engine, up to +50% (level 50). |
| `descImpulsionDrive` | +1,5 % de vitesse par niveau pour les propulseurs à impulsion, à fusion et les moteurs hyperespace, jusqu'à +75 % (niveau 50). | +1.5% speed per level for impulse and fusion thrusters and hyperspace drives, up to +75% (level 50). |
| `descFusionDrive` | +2 % de vitesse par niveau pour les propulseurs à fusion et les moteurs hyperespace, jusqu'à +100 % (niveau 50). | +2% speed per level for fusion thrusters and hyperspace drives, up to +100% (level 50). |
| `descHyperspaceDrive` | +2,5 % de vitesse par niveau pour les moteurs hyperespace, jusqu'à +125 % (niveau 50). | +2.5% speed per level for hyperspace drives, up to +125% (level 50). |
| `descWeapon` | +15 % de dégâts par niveau pour toutes les armes. | +15% damage per level for every weapon. |
| `descLaser` | +15 % de dégâts par niveau pour les canons laser (en plus de l'Armement). | +15% damage per level for laser cannons (on top of Weapons). |
| `descIon` | +20 % de dégâts par niveau pour les canons à ions (en plus de l'Armement). | +20% damage per level for ion cannons (on top of Weapons). |
| `descPlasma` | +25 % de dégâts par niveau pour les canons à plasma (en plus de l'Armement). | +25% damage per level for plasma cannons (on top of Weapons). |
| `descArmor` | +10 % de blindage par niveau pour les vaisseaux, aussi en combat tactique. | +10% armor per level for ships, tactical battles included. |
| `descShield` | +10 % de boucliers par niveau pour les vaisseaux, aussi en combat tactique. | +10% shields per level for ships, tactical battles included. |
| `descSolarTech` | Débloque le Laser minier, indispensable pour miner les astéroïdes ; +10 % de vitesse d'extraction par niveau. | Unlocks the Mining Laser, required to mine asteroids; +10% mining speed per level. |
| `descComms` | +2 % de relations diplomatiques par niveau. | +2% diplomatic relations per level. |
| `descThermodynamics` | Gestion de la chaleur : +20 % de dégâts du canon thermique par niveau. | Heat management: +20% Heat Cannon damage per level. |
| `descDrone` | +5 % de production de minerai, de cristal et de biomasse par niveau (drones d'extraction et agricoles). | +5% mineral, crystal and biomass output per level (mining and farm drones). |
| `descNanite` | −3 % de durée de fabrication des modules de vaisseau (cale sèche) par niveau, jusqu'à −45 %. | −3% ship module build time (dry dock) per level, down to −45%. |
| `descBiotech` | Cuves de clonage : −5 % de durée de recrutement des troupes par niveau, jusqu'à −50 %. | Clone vats: −5% troop training time per level, down to −50%. |
| `descGravityTech` | −5 % de durée de colonisation par niveau, jusqu'à −60 %. | −5% colonization time per level, down to −60%. |
| `descPsiTech` | −5 % de durée de recherche par niveau, jusqu'à −50 %. | −5% research time per level, down to −50%. |

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

## Recycleur, pirates, porte amie, gestes diplomatiques (intégré web)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.dock.recycler.hint` | Drop a module here to recycle it | Déposez un module ici pour le recycler | ✅ 10 langues |
| `vr.dock.recycler.confirm` | Recycle {0}? Given back to the planet: {1} | Recycler {0} ? Rendu à la planète : {1} | ✅ 10 langues |
| `vr.dock.recycler.go` | Recycle | Recycler | ✅ 10 langues |
| `vr.dock.recycled` | Module recycled · {0} | Module recyclé · {0} | ✅ 10 langues |
| `vr.dock.effectiveHint` | Valeurs finales, recherches comprises · Coque = armure des modules + 50 de structure par module, comme en combat | Final values, research included · Hull = the modules' armor + 50 structure per module, as in battle |
| `vr.pirate.levelInfo` | Level {0} / {1}: the higher, the better armed and the bigger the loot | Niveau {0} / {1} : plus il est élevé, plus ils sont armés et plus le butin est gros | ✅ 10 langues |
| `vr.pirate.loot` | Loot: {0} | Butin : {0} | ✅ 10 langues |
| `vr.pirate.odds.good` | {0}: clear advantage | {0} : avantage net | ✅ 10 langues |
| `vr.pirate.odds.even` | {0}: close fight | {0} : combat serré | ✅ 10 langues |
| `vr.pirate.odds.bad` | {0}: too dangerous | {0} : trop dangereux | ✅ 10 langues |
| `vr.gate.incomingFriendly` | Incoming connection from {0} (friendly) | Connexion entrante depuis {0} (ami) | ✅ 10 langues |
| `vr.gate.incomingFriendlyHint` | Opened by yourself or an ally to deliver resources or troops: no danger. | Ouverte par vous-même ou un allié pour livrer des ressources ou des troupes : aucun danger. | ✅ 10 langues |
| `vr.diplo.gestures` | Diplomatic gestures | Gestes diplomatiques | ✅ 10 langues |
| `vr.diplo.gesture.compliment` | Letter of compliments | Lettre de compliments | ✅ 10 langues |
| `vr.diplo.gesture.gift` | Gift | Cadeau | ✅ 10 langues |
| `vr.diplo.gesture.threat` | Threat | Menace | ✅ 10 langues |
| `vr.diplo.gesture.blackmail` | Blackmail | Chantage | ✅ 10 langues |
| `vr.diplo.gestureWait` | Again in {0} | Possible dans {0} | ✅ 10 langues |
| `vr.diplo.giftCost` | Gift: {0}, delivered to their first world | Cadeau : {0}, livrés à leur premier monde | ✅ 10 langues |
| `vr.diplo.gestureDone.compliment` | Letter of compliments sent to {0} | Lettre de compliments envoyée à {0} | ✅ 10 langues |
| `vr.diplo.gestureDone.gift` | Gift delivered to {0} | Cadeau livré à {0} | ✅ 10 langues |
| `vr.diplo.gestureDone.threat` | Threat sent to {0} | Menace adressée à {0} | ✅ 10 langues |
| `vr.diplo.gestureDone.blackmail` | Blackmail pressed on {0} | Chantage exercé sur {0} | ✅ 10 langues |
| `invalidGesture` | Unknown diplomatic gesture | Geste diplomatique inconnu | ✅ 10 langues |
| `cantGestureSelf` | Not toward your own empire | Impossible envers votre propre empire | ✅ 10 langues |
| `gestureCooldown` | This gesture was made toward this empire too recently | Ce geste a déjà été fait récemment envers cet empire | ✅ 10 langues |
| `dg_subject_compliment` | Letter of compliments from {0} | Lettre de compliments de {0} | ✅ 10 langues |
| `dg_content_compliment` | The {0} empire sends its compliments and salutes the greatness of your civilisation. Its diplomats hope for warm relations between your peoples. | L'empire {0} vous adresse ses compliments et salue la grandeur de votre civilisation. Ses diplomates espèrent des relations chaleureuses entre vos peuples. | ✅ 10 langues |
| `dg_subject_gift` | A gift from {0} | Un cadeau de {0} | ✅ 10 langues |
| `dg_content_gift` | The {0} empire offers you a present as a token of friendship: {1} mineral, {2} crystal and {3} biomass, delivered to your first world. | L'empire {0} vous offre un présent en signe d'amitié : {1} minerai, {2} cristal et {3} biomasse, livrés à votre premier monde. | ✅ 10 langues |
| `dg_subject_threat` | Threat from {0} | Menace de {0} | ✅ 10 langues |
| `dg_content_threat` | The {0} empire sends you a blunt warning: any provocation will be punished, and its fleets stand ready. | L'empire {0} vous adresse une mise en garde sans détour : toute provocation sera punie, et ses flottes se tiennent prêtes. | ✅ 10 langues |
| `dg_subject_blackmail` | Blackmail from {0} | Chantage de {0} | ✅ 10 langues |
| `dg_content_blackmail` | The {0} empire claims to hold damaging secrets about your government and demands your compliance. Your relations suffer for it. | L'empire {0} prétend détenir des secrets compromettants sur votre gouvernement et exige votre docilité. Vos relations s'en trouvent dégradées. | ✅ 10 langues |

## Rééquilibrages d'octobre : recherches, minage, combat, fret, limites (intégré web)

| Clé | EN | FR | Lot |
|---|---|---|---|
| `vr.pirate.levelShort` | Level sets their weapons and the loot | Le niveau fixe leur armement et le butin | Pirates (légende du niveau, butin du courrier), nanites |
| `rm_shipbattle_pirate_loot` | Loot seized from the pirates (level {level}): +{xp} XP, +{nova} Nova, +{mineral} mineral, +{crystal} crystal and +{biomass} biomass, delivered to {planet}. | Butin saisi sur les pirates (niveau {level}) : +{xp} XP, +{nova} Nova, +{mineral} minerai, +{crystal} cristal et +{biomass} biomasse, versés à {planet}. | Pirates (légende du niveau, butin du courrier), nanites |
| `vr.research.stat.moduleBuildTime` | Module building | Fabrication des modules | Pirates (légende du niveau, butin du courrier), nanites |
| `descNanite` | −3% ship module build time (dry dock) per level, down to −45%. | −3 % de durée de fabrication des modules de vaisseau (cale sèche) par niveau, jusqu'à −45 %. | Pirates (légende du niveau, butin du courrier), nanites |
| `descBiotech` | Clone vats: −5% troop training time per level, down to −50%. | Cuves de clonage : −5 % de durée de recrutement des troupes par niveau, jusqu'à −50 %. | Biotechnologie |
| `vr.research.stat.troopTrainingTime` | Troop training | Recrutement des troupes | Biotechnologie |
| `descDrone` | +5% mineral, crystal and biomass output per level (mining and farm drones). | +5 % de production de minerai, de cristal et de biomasse par niveau (drones d'extraction et agricoles). | Drones |
| `descThermodynamics` | Heat management: +20% Heat Cannon damage per level. | Gestion de la chaleur : +20 % de dégâts du canon thermique par niveau. | Thermodynamique |
| `solarTech` | Mining Technology | Technologie minière | Technologie minière et Laser minier |
| `descSolarTech` | Unlocks the Mining Laser, required to mine asteroids; +10% mining speed per level. | Débloque le Laser minier, indispensable pour miner les astéroïdes ; +10 % de vitesse d'extraction par niveau. | Technologie minière et Laser minier |
| `MiningLaser` | Mining Laser | Laser minier | Technologie minière et Laser minier |
| `descMiningLaser` | A drilling emitter aimed at the rock. Required to mine an asteroid field; each extra laser mines 50% faster. | Émetteur de forage orienté vers la roche. Indispensable pour miner un champ d'astéroïdes ; chaque laser supplémentaire accélère l'extraction de 50 %. | Technologie minière et Laser minier |
| `needMiningLaser` | A Mining Laser is needed to mine | Laser minier requis pour miner | Technologie minière et Laser minier |
| `vr.research.stat.harvestSpeed` | Mining speed | Vitesse d'extraction | Technologie minière et Laser minier |
| `vr.freight.load` | Load modules | Embarquer des modules | Fret de modules, coque planétaire, aide de la cale |
| `vr.freight.unload` | Unload modules | Débarquer des modules | Fret de modules, coque planétaire, aide de la cale |
| `vr.freight.holdLoad` | {0} module(s) aboard · room for {1} more ({2} hold each) | {0} module(s) à bord · encore {1} de place ({2} de soute chacun) | Fret de modules, coque planétaire, aide de la cale |
| `vr.freight.holdUnload` | {0} module(s) aboard | {0} module(s) à bord | Fret de modules, coque planétaire, aide de la cale |
| `vr.freight.none` | No module available | Aucun module disponible | Fret de modules, coque planétaire, aide de la cale |
| `vr.gate.modules` | Modules: {0} / {1} | Modules : {0} / {1} | Fret de modules, coque planétaire, aide de la cale |
| `vr.gate.pickModules` | Modules to send through the gate | Modules à envoyer par la porte | Fret de modules, coque planétaire, aide de la cale |
| `fleetNotAtPlanet` | The ship is not in orbit of this planet | Le vaisseau n'est pas en orbite de cette planète | Fret de modules, coque planétaire, aide de la cale |
| `moduleNotInHangar` | This module is no longer in the hangar | Ce module n'est plus dans le hangar | Fret de modules, coque planétaire, aide de la cale |
| `noModuleCarried` | No module aboard | Aucun module à bord | Fret de modules, coque planétaire, aide de la cale |
| `vr.armory.defHull` | Planet hull +{0} | Coque planétaire +{0} | Fret de modules, coque planétaire, aide de la cale |
| `vr.armory.defShield` | Planet shield +{0} | Bouclier planétaire +{0} | Fret de modules, coque planétaire, aide de la cale |
| `vr.dock.effectiveHint` | Final values, research included · Hull = the modules' armor + 50 structure per module, as in battle | Valeurs finales, recherches comprises · Coque = armure des modules + 50 de structure par module, comme en combat | Fret de modules, coque planétaire, aide de la cale |
| `vr.dock.limits` | Ships {0} / {1}  ·  Stations {2} / {3}   (one of each per planet owned, one station per orbit) | Vaisseaux {0} / {1}  ·  Stations {2} / {3}   (un de chaque par planète possédée, une station par orbite) | Limites de vaisseaux et de stations, tir ami |
| `stationLimitReached` | Station limit reached (one per planet owned) | Limite de stations atteinte (une par planète possédée) | Limites de vaisseaux et de stations, tir ami |
| `planetHasStation` | An orbital station already holds this planet's orbit | Une station orbitale tient déjà l'orbite de cette planète | Limites de vaisseaux et de stations, tir ami |
| `vr.hunt.notEnemy` | Careful: {0} ship, attacking it will hurt your relations | Attention : vaisseau {0}, l'attaquer dégradera vos relations | Limites de vaisseaux et de stations, tir ami |
| `vr.research.effect.harvestSpeed` | {0} asteroid mining speed | {0} vitesse d'extraction des astéroïdes | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `vr.research.effect.moduleBuildTime` | {0} module build time (dry dock) | {0} durée de fabrication des modules (cale sèche) | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `vr.research.effect.troopTrainingTime` | {0} troop training time | {0} durée de recrutement des troupes | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `descCombustionDrive` | +1% speed per level for combustion thrusters, boosters and every more advanced engine, up to +50% (level 50). | +1 % de vitesse par niveau pour les propulseurs à combustion, les boosters et tous les moteurs plus avancés, jusqu'à +50 % (niveau 50). | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `descImpulsionDrive` | +1.5% speed per level for impulse and fusion thrusters and hyperspace drives, up to +75% (level 50). | +1,5 % de vitesse par niveau pour les propulseurs à impulsion, à fusion et les moteurs hyperespace, jusqu'à +75 % (niveau 50). | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `descFusionDrive` | +2% speed per level for fusion thrusters and hyperspace drives, up to +100% (level 50). | +2 % de vitesse par niveau pour les propulseurs à fusion et les moteurs hyperespace, jusqu'à +100 % (niveau 50). | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `descHyperspaceDrive` | +2.5% speed per level for hyperspace drives, up to +125% (level 50). | +2,5 % de vitesse par niveau pour les moteurs hyperespace, jusqu'à +125 % (niveau 50). | Effets de recherche (minage, fabrication des modules, troupes), propulsion plafonnée |
| `siegePillage` | Siege: pillage | Assiéger : piller | Sièges planétaires : objectif piller / conquérir |
| `siegeConquer` | Siege: conquer | Assiéger : conquérir | Sièges planétaires : objectif piller / conquérir |
| `siegeResultPillaged` | {0} pillaged: {1} mineral, {2} crystal, {3} biomass | {0} pillée : {1} minerai, {2} cristal, {3} biomasse | Sièges planétaires : objectif piller / conquérir |
| `siegeResultConquered` | {0} is now yours | {0} est désormais à vous | Sièges planétaires : objectif piller / conquérir |
| `siegeResultHeld` | {0} held | {0} a tenu | Sièges planétaires : objectif piller / conquérir |
| `vr.battle.objectivePillage` | Objective: bring {0} down and pillage it | Objectif : abattre {0} et la piller | Sièges planétaires : objectif piller / conquérir |
| `vr.battle.objectiveConquer` | Objective: bring {0} down and take it | Objectif : abattre {0} et la conquérir | Sièges planétaires : objectif piller / conquérir |
| `intergalacticNeedsHyperspace` | Only a hyperspace jump crosses the void between two galaxies (hyperspace drives and crystal required). | Seul un saut hyperespace franchit le vide entre deux galaxies (moteurs hyperespace et cristal requis). | Galaxie agrandie, galaxies multiples |

Toutes ✅ dans les dix langues (`docs/tools/i18n.py check` : même jeu et même ordre de clés partout).

## Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
| `assets/langs/{de,es,it,pt,ru,ko,ja,zh}.json` | Traduits depuis l'anglais (97-99 %) | Même jeu de clés et même ordre que `en.json` ; l'outil admin Translate permet la relecture clé par clé ; toute clé non traduite retombe sur l'anglais |

