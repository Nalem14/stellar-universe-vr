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

---

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

## À intégrer — descriptions des modules « pièces » (cale sèche, fiche du fabricateur)

Les 24 modules ajoutés en bloc à `$SHIPSTATS` (GunTurret → Toilet) n'ont pas de `desc<Type>` ; la fiche de survol du fabricateur et la ligne d'état de la cale sèche (`ModuleCatalog.Description`) affichent la clé brute en attendant. Textes calés sur `shipstats` et sur la compétence de combat de `model/battle.php`.

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `descGunTurret` | Light gun turret. Cheap, quick to build, fires at close range every turn. | Tourelle légère. Peu coûteuse, vite construite, elle tire à courte portée à chaque tour. | Fiche module (cale sèche) |
| `descEnergyBattery` | Energy battery. Reinforces the shields and can release an electric discharge that ionizes nearby enemies. | Batterie énergétique. Renforce les boucliers et peut libérer une décharge électrique qui ionise les ennemis proches. | Fiche module (cale sèche) |
| `descCrewQuarters` | Crew quarters. Sturdy living space that thickens the hull and keeps the crew's morale up in battle. | Quartiers d'équipage. Espace de vie robuste qui épaissit la coque et soutient le moral de l'équipage au combat. | Fiche module (cale sèche) |
| `descLuxuryQuarters` | Luxury quarters. Heavily armored suites with their own shielding; a well-rested crew recovers faster. | Quartiers de luxe. Suites lourdement blindées et protégées par bouclier ; un équipage reposé récupère plus vite. | Fiche module (cale sèche) |
| `descCloneBay` | Clone bay. Grows replacement crew in an emergency, restoring a large share of the ship's hull. | Baie de clonage. Produit un équipage de remplacement en urgence et restaure une grande partie de la coque. | Fiche module (cale sèche) |
| `descCommandBridge` | Command bridge. Armored, shielded and armed nerve centre; its tactical orders recharge the shields. | Pont de commandement. Centre névralgique blindé, protégé et armé ; ses ordres tactiques rechargent les boucliers. | Fiche module (cale sèche) |
| `descCommunicationArray` | Communication array. Long-range transmitters that jam the enemy's systems for several turns. | Réseau de communication. Émetteurs longue portée qui brouillent les systèmes ennemis pendant plusieurs tours. | Fiche module (cale sèche) |
| `descCorridor` | Corridor. Cheap armored passage linking the modules; it can be barricaded under fire. | Corridor. Passage blindé bon marché qui relie les modules ; il peut être barricadé sous le feu. | Fiche module (cale sèche) |
| `descOpenBay` | Open bay. Light, roomy hold space that adds cargo and lets the ship manoeuvre more freely. | Baie ouverte. Espace de soute léger qui ajoute du cargo et laisse le vaisseau manœuvrer plus librement. | Fiche module (cale sèche) |
| `descHatchCorridor` | Hatch corridor. Reinforced corridor with airtight hatches, sealed in battle to harden the hull. | Sas-couloir. Corridor renforcé aux sas étanches, verrouillés au combat pour durcir la coque. | Fiche module (cale sèche) |
| `descDroneBay` | Drone bay. Launches swarms of combat drones that strike an area around their target. | Baie de drones. Lance des essaims de drones de combat qui frappent toute une zone autour de la cible. | Fiche module (cale sèche) |
| `descEnergyReactor` | Energy reactor. Feeds the shields and the engines; an overload grants extra movement in battle. | Réacteur énergétique. Alimente boucliers et moteurs ; une surcharge offre des déplacements supplémentaires au combat. | Fiche module (cale sèche) |
| `descHeatCannon` | Heat cannon. Heavy thermal weapon whose shots overheat the enemy's systems. | Canon thermique. Arme thermique lourde dont les tirs mettent en surchauffe les systèmes ennemis. | Fiche module (cale sèche) |
| `descHydroponicBay` | Hydroponic bay. Large growing bay that adds a lot of cargo space and feeds the crew. | Baie hydroponique. Grande serre qui ajoute beaucoup de cargo et nourrit l'équipage. | Fiche module (cale sèche) |
| `descKitchen` | Kitchen. Feeds the crew: a little armor, speed and cargo, and combat rations that patch the hull. | Cuisine. Nourrit l'équipage : un peu d'armure, de vitesse et de cargo, et des rations de combat qui réparent la coque. | Fiche module (cale sèche) |
| `descResearchLab` | Research lab. On-board laboratory whose tactical analysis jams the enemy's systems. | Laboratoire de recherche. Laboratoire embarqué dont l'analyse tactique brouille les systèmes ennemis. | Fiche module (cale sèche) |
| `descMedicalBay` | Medical bay. Advanced care that restores a large share of the hull of the ship and its neighbours. | Infirmerie. Soins avancés qui restaurent une grande partie de la coque du vaisseau et de ses voisins. | Fiche module (cale sèche) |
| `descMindControlModule` | Mind control module. Psychic wave technology that pins enemy ships in place for several turns. | Contrôle mental. Technologie d'onde psychique qui cloue les vaisseaux ennemis sur place pendant plusieurs tours. | Fiche module (cale sèche) |
| `descOxygenSystem` | Oxygen system. Life support that slightly reinforces the hull and vents oxygen to patch nearby damage. | Système à oxygène. Support vital qui renforce un peu la coque et libère de l'oxygène pour réparer les dégâts proches. | Fiche module (cale sèche) |
| `descRestArea` | Rest area. Lets the crew recover quickly between engagements; a little armor and speed. | Espace de repos. Permet à l'équipage de récupérer vite entre deux engagements ; un peu d'armure et de vitesse. | Fiche module (cale sèche) |
| `descShower` | Shower. Decontamination unit that clears the ship's negative effects in battle. | Douche. Unité de décontamination qui purge les effets négatifs du vaisseau au combat. | Fiche module (cale sèche) |
| `descSolarPanel` | Solar panel. Shields the ship without burning crystal and recharges its shields every turn. | Panneau solaire. Protège le vaisseau sans consommer de cristal et recharge ses boucliers à chaque tour. | Fiche module (cale sèche) |
| `descStasisPod` | Stasis pod. Projects a stasis field that holds enemy ships in place for several turns. | Pod de stase. Projette un champ de stase qui immobilise les vaisseaux ennemis pendant plusieurs tours. | Fiche module (cale sèche) |
| `descToilet` | Toilet. Cheap and small, a little armor and cargo; its toxic waste can overheat a close enemy. | Toilettes. Petites et bon marché, un peu d'armure et de cargo ; leurs déchets toxiques peuvent surchauffer un ennemi proche. | Fiche module (cale sèche) |

## À intégrer — laboratoire : effets et déblocages des recherches

La fiche d'analyse du laboratoire (`ResearchLab.DetailText`) affiche l'effet (`desc<Tech>`), les prérequis puis tous les déblocages niveau par niveau (`ResearchCatalog.Unlocks`) : bâtiments, modules, défenses, troupes, recherches suivantes et fonctionnalités. `descPrlBond` / `descStargateTriangulation` manquent aussi au panneau de recherche web (`research.js`), qui n'affiche alors aucune description. `colonisation` lit la clé native `descColonization`.

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.research.effect` | Effect | Effet | Titre de section, fiche recherche |
| `vr.research.kind.feature` | Feature | Fonctionnalité | Type de déblocage (comme `vr.research.kind.module`) |
| `vr.research.kind.research` | Research | Recherche | Type de déblocage : recherche suivante de l'arbre |
| `vr.research.feature.jumpgate` | Instant jumps between your Jumpgates | Sauts instantanés entre vos Portails de Saut | Fonctionnalité apportée par le bâtiment `jumpgate` |
| `vr.research.feature.troops` | Ground troop training | Recrutement de troupes au sol | Fonctionnalité apportée par `academy` |
| `vr.research.feature.defenses` | Planetary defense construction | Construction de défenses planétaires | Fonctionnalité apportée par `defenseFactory` |
| `vr.research.feature.prlRange` | PRL Bond range +{0}% per level | Portée du Bond PRL +{0} % par niveau | {0} = `prlBond.rangePerResearchLevel` / `baseRange` (GetConfigs) |
| `vr.research.feature.triangulation` | Each level reveals the address of an unknown Stargate | Chaque niveau révèle l'adresse d'une Porte des Étoiles inconnue | `stargateTriangulation` (GrantStargateTriangulationDiscovery) |
| `descPrlBond` | PRL Bond research. Unlocks the PRL Bond module, an instant short-range jump to any system paid in crystal; each level extends its range. | Recherche Bond PRL. Débloque le module Bond PRL, un saut instantané à courte portée vers n'importe quel système, payé en cristal ; chaque niveau étend sa portée. | Effet, fiche recherche (VR + web) |
| `descStargateTriangulation` | Triangulation of the Stargate network. Each level pinpoints the address of a Stargate your empire does not know yet. | Triangulation du réseau des Portes des Étoiles. Chaque niveau localise l'adresse d'une Porte des Étoiles encore inconnue de votre empire. | Effet, fiche recherche (VR + web) |

## À intégrer — quart : consoles disposées dans la pièce réelle

Lanceur à droite du cluster du quart (passthrough) ; les répéteurs reprennent les titres `vr.station.*` déjà en place.

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.watch.consoles` | Consoles | Consoles | Titre du lanceur de consoles du quart (colonne à droite du cluster) |
| `vr.watch.console.comms` | Comms console | Console comms | Touche du lanceur : la console Comms complète (canal, privé, courrier) posée dans la pièce |
| `vr.watch.console.ops` | Ops console | Console ops | Touche du lanceur : la console Ops complète (bâtiments, file, décisions) posée dans la pièce |

## À intégrer — saisons de suprématie (tableau de saison du bureau du capitaine)

Le web affiche ses paliers en anglais en dur (`SEASON_TIERS.name`) et ses libellés de saison en français en dur dans `progression-window.hbs` ; la VR passe par des clés. `seasonTier_<clé>` reprend les clés de `SEASON_TIERS` (`model/season.php`).

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `seasonTier_grandmaster` | Grand Marshal | Grand Maréchal | Palier de saison (20 000 pts) |
| `seasonTier_master` | Celestial Archon | Archonte Céleste | Palier de saison (12 000 pts) |
| `seasonTier_diamond` | Bane of Worlds | Fléau des Mondes | Palier de saison (7 000 pts) |
| `seasonTier_platinum` | Supreme Commander | Commandant Suprême | Palier de saison (3 500 pts) |
| `seasonTier_gold` | Stellar Victor | Vainqueur Stellaire | Palier de saison (1 500 pts) |
| `seasonTier_silver` | Sentinel | Sentinelle | Palier de saison (500 pts) |
| `seasonTier_bronze` | Aspirant | Aspirant | Palier de saison (0 pt) |
| `vr.season.none` | No competitive season is running right now. | Aucune saison compétitive en cours. | Tableau de saison sans saison active |
| `vr.season.cycle` | {0}% of the cycle completed | {0} % du cycle accompli | Jauge du cycle de 45 jours |
| `vr.season.standing` | Our standing | Notre rang | Titre de la colonne de gauche (onglet Saison) |
| `vr.season.rankPoints` | Galactic rank #{0} · {1} pts | Rang galactique n°{0} · {1} pts | Rang et score de l'empire du joueur |
| `vr.season.breakdown` | Combat {0} · Conquest {1} · Missions {2} | Combat {0} · Conquête {1} · Missions {2} | Décomposition du score de saison |
| `vr.season.nextTier` | Next tier: {0} · {1} pts to go | Prochain palier : {0} · encore {1} pts | Jauge vers le palier suivant |
| `vr.season.maxTier` | Supreme tier reached! | Palier suprême atteint ! | Palier maximum atteint |
| `vr.season.novaClaimed` | Tier bonuses earned this season: {0} Nova | Bonus de paliers gagnés cette saison : {0} Nova | Nova déjà versés par les paliers |
| `vr.season.allianceHint` | Every point you earn also counts for your alliance's coalition score. | Chaque point gagné compte aussi pour le score de coalition de votre alliance. | Rappel sous le rang / sous le classement des alliances |
| `vr.season.objectives` | Season objectives — supremacy points | Objectifs de saison — points de suprématie | Titre de la colonne des sources de points |
| `vr.season.score.battleWon` | Win a battle | Remporter une bataille | Objectif de saison (+30) |
| `vr.season.score.planetConquered` | Conquer a planet | Conquérir une planète | Objectif de saison (+100) |
| `vr.season.score.defenseHeld` | Repel an attack on a planet | Repousser une attaque sur une planète | Objectif de saison (+60) |
| `vr.season.score.stargateCapture` | Capture a world through a Stargate | Capturer un monde par une Porte des Étoiles | Objectif de saison (+50) |
| `vr.season.score.stargateColony` | Colonize through a Stargate | Coloniser par une Porte des Étoiles | Objectif de saison (+40) |
| `vr.season.score.anomaly` | Scan a cosmic anomaly | Analyser une anomalie cosmique | Objectif de saison (+15) |
| `vr.season.score.bounty` | Complete a contract | Accomplir un contrat | Objectif de saison (+25) |
| `vr.season.score.daily` | Complete a daily objective | Accomplir un objectif quotidien | Objectif de saison (+10) |
| `vr.season.score.weekly` | Complete a weekly objective | Accomplir un objectif hebdomadaire | Objectif de saison (+30) |
| `vr.season.score.monthly` | Complete a monthly objective | Accomplir un objectif mensuel | Objectif de saison (+100) |
| `vr.season.points` | {0} pts | {0} pts | Seuil d'un palier, score au panthéon |
| `vr.season.noScores` | No score recorded this season yet. Send your fleets into battle! | Aucun score enregistré pour l'instant dans cette saison. Lancez vos flottes au combat ! | Classement des empires vide |
| `vr.season.noAlliances` | No alliance ranked yet. Members' points feed their alliance automatically! | Aucune alliance classée pour l'instant. Les points des membres alimentent automatiquement leur alliance ! | Classement des alliances vide |
| `vr.season.rank` | Rank | Rang | En-tête de colonne |
| `vr.season.tier` | Tier | Palier | En-tête de colonne |
| `vr.season.score` | Score | Score | En-tête de colonne |
| `vr.season.members` | Members | Membres | En-tête de colonne (alliances) |
| `vr.season.pantheonEmpty` | The first champions will be enshrined in the Pantheon when the current season closes. | Les premiers champions seront immortalisés au Panthéon à la clôture de la saison en cours. | Panthéon vide |
| `vr.season.endRewards` | End-of-season rewards | Récompenses de fin de saison | Titre de l'onglet Honneurs |
| `vr.season.podium.first` | 1st — Grand Marshal | 1er — Grand Maréchal | Récompense de fin de saison |
| `vr.season.podium.second` | 2nd — Imperial Runner-Up | 2e — Dauphin Impérial | Récompense de fin de saison |
| `vr.season.podium.third` | 3rd — Third Order | 3e — Troisième Ordre | Récompense de fin de saison |
| `vr.season.podium.top10` | Top 10 — Galactic Elite | Top 10 — Élite Galactique | Récompense de fin de saison |
| `vr.season.podium.top20` | Top 20 — Season Veteran | Top 20 — Vétéran de Saison | Récompense de fin de saison |
| `vr.season.titleHint` | Titles are permanent: wear one beside your empire's name. | Les titres sont permanents : portez-en un à côté du nom de votre empire. | Au-dessus de la liste des honneurs |
| `vr.season.noAccolades` | No imperial honours yet. Climb the season rankings to earn permanent titles! | Vous n'avez pas encore débloqué d'honneurs impériaux. Progressez dans les classements de la saison pour obtenir des titres permanents ! | Liste des honneurs vide |
| `vr.season.seeQuarters` | Rankings, objectives and rewards: on the season board in the captain's quarters. | Classement, objectifs et récompenses : sur le tableau de saison du bureau du capitaine. | Fiche de la saison dans les transmissions du sas |
