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
| `vr.dock.effectiveHint` | En vert : valeurs réelles du vaisseau (recherches et modules montés compris) | In green: the ship's real values (research and fitted modules included) |
| `vr.diplo.ranking`, `vr.diplo.rank.empire` / `.economy` / `.research` / `.fleet` / `.defense` / `.score` | Classement des empires / Empire / Économie / Recherche / Flotte / Défense / Score | Empire ranking / Empire / Economy / Research / Fleet / Defense / Score (intégré web, dix langues) |

Descriptions natives à réécrire (elles décrivaient un autre jeu — audit du 2026-10-04, nouvelle table `$RESEARCH_EFFECTS`) :

| Clé | FR | EN |
|---|---|---|
| `descEnergy` | +3 % de production d'énergie par niveau, toutes centrales. | +3% energy output per level, every plant. |
| `descComputer` | −1 % de durée de construction par niveau (bâtiments, troupes, défenses), jusqu'à −50 %. | −1% build time per level (buildings, troops, defenses), down to −50%. |
| `descCombustionDrive` | +25 % de vitesse par niveau pour les propulseurs à combustion, les boosters de vitesse et tous les moteurs plus avancés. | +25% speed per level for combustion thrusters, speed boosters and every more advanced engine. |
| `descImpulsionDrive` | +35 % de vitesse par niveau pour les propulseurs à impulsion, à fusion et les moteurs hyperespace. | +35% speed per level for impulse and fusion thrusters and hyperspace drives. |
| `descFusionDrive` | +45 % de vitesse par niveau pour les propulseurs à fusion et les moteurs hyperespace. | +45% speed per level for fusion thrusters and hyperspace drives. |
| `descHyperspaceDrive` | +60 % de vitesse par niveau pour les moteurs hyperespace. | +60% speed per level for hyperspace drives. |
| `descWeapon` | +15 % de dégâts par niveau pour toutes les armes. | +15% damage per level for every weapon. |
| `descLaser` | +15 % de dégâts par niveau pour les canons laser (en plus de l'Armement). | +15% damage per level for laser cannons (on top of Weapons). |
| `descIon` | +20 % de dégâts par niveau pour les canons à ions (en plus de l'Armement). | +20% damage per level for ion cannons (on top of Weapons). |
| `descPlasma` | +25 % de dégâts par niveau pour les canons à plasma (en plus de l'Armement). | +25% damage per level for plasma cannons (on top of Weapons). |
| `descArmor` | +10 % de blindage par niveau pour les vaisseaux, aussi en combat tactique. | +10% armor per level for ships, tactical battles included. |
| `descShield` | +10 % de boucliers par niveau pour les vaisseaux, aussi en combat tactique. | +10% shields per level for ships, tactical battles included. |
| `descSolarTech` | +5 % de production des centrales solaires (au sol et en orbite) par niveau. | +5% solar plant output (ground and orbit) per level. |
| `descComms` | +2 % de relations diplomatiques par niveau. | +2% diplomatic relations per level. |
| `descThermodynamics` | +5 % de production de minerai et de cristal par niveau. | +5% mineral and crystal output per level. |
| `descDrone` | +5 % de production de biomasse par niveau (drones agricoles). | +5% biomass output per level (farm drones). |
| `descNanite` | −3 % de durée d'amélioration des bâtiments par niveau, jusqu'à −45 %. | −3% building upgrade time per level, down to −45%. |
| `descBiotech` | −5 % de durée de construction des habitations et des fermes par niveau, jusqu'à −50 %. | −5% housing and farm build time per level, down to −50%. |
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
| `vr.dock.effectiveHint` | The ship's final values, research and fitted modules included | Valeurs finales du vaisseau, recherches et modules montés compris | ✅ 10 langues |
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

## Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
| `assets/langs/{de,es,it,pt,ru,ko,ja,zh}.json` | Traduits depuis l'anglais (97-99 %) | Même jeu de clés et même ordre que `en.json` ; l'outil admin Translate permet la relecture clé par clé ; toute clé non traduite retombe sur l'anglais |

