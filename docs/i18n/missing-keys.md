# Clés i18n manquantes — à intégrer côté serveur

Ce fichier recense les clés dont le client VR a besoin et qui manquent dans `assets/langs/{en,fr}.json` du repo web (servi par `GetTranslations`).

**Circuit** :
- L'agent VR liste ici la clé, avec ses textes EN et FR.
- Thommy l'intègre côté web (admin Translate → `assets/langs/{fr,en}.json`, ou commit direct sur ces fichiers).
- La ligne est ensuite retirée de ce fichier.

Tant qu'une clé est absente, la VR affiche **la clé brute**. En Editor, elle est aussi ajoutée à `su-missing-trans-keys.txt`. Le client n'invente jamais de texte de secours.

**Conventions** :
- **Clé native d'abord** : si une clé web existe déjà (même avec une autre casse), le client VR l'utilise telle quelle.
- **UI propre à la VR** : `vr.<zone>.<nom>`, par exemple `vr.menu.continue`.
- **Répliques crew** : `crew.<role>.<event>.<n>`. Les rôles sont `helm`, `tactical`, `engineering`, `science`, `comms` et `ops`. `<n>` part de 1, et `BarkDirector` tire une variante au hasard sans répétition.
- **Paramètres** : `{0}`, `{1}`… (`Trans.Format`, `string.Format` en culture invariante).
- Pour les répliques, les ordres et les échecs, le ton reprend celui du journal de colonisation web : l'équipage s'adresse au joueur en « Commandant ».

---

## Intégré côté web

- Clés `vr.*`, `crew.*`, `buildingDesc_*`, `colonyLog_*`, `relation_*` dans `assets/langs/{fr,en}.json`.
- Remaps client (§1) : le code VR utilise les clés natives (`fleets`, `asteroidField`, `moveToSystem`, `defendPosition*`, `explorePlanet`, `depositCargo`, `withdrawCargo`, `harvestAsteroid`, `attackOrbit`, `cancel`, `sublight` / `hyperdrive`, `buildingDesc_<type>`, plaques `vr.station.*`, etc.).

---


## À intégrer (P3 — création d'empire)

| Clé | EN | FR | Usage VR |
|---|---|---|---|
| `vr.create.flagBackground` | Background | Fond | Création d'empire : couleur de fond du drapeau |
| `vr.create.flagShape` | Shape {0} | Forme {0} | Création d'empire : forme n du drapeau |
| `vr.create.traits` | Traits | Traits | Création d'empire : étape traits d'espèce |
| `vr.create.confirm` | Confirmation | Confirmation | Création d'empire : récapitulatif |
| `vr.create.limitReached` | Selection limit reached | Limite de sélection atteinte | Éthiques / traits au-delà du maximum |
| `vr.create.founded` | Empire founded. Welcome aboard, Commander. | Empire fondé. Bienvenue à bord, Commandant. | Retour `CreateEmpire` avant l'entrée sur le pont |
| `flagShape_none` | None | Aucune | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_circle` | Circle | Cercle | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_triangle` | Triangle | Triangle | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_star` | Star | Étoile | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_diamond` | Diamond | Losange | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_stripe` | Stripe | Bande | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_ring` | Ring | Anneau | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `flagShape_cross` | Cross | Croix | Formes du drapeau (en dur dans `view/create-empire.php`) |
| `traitEffect_food` | Food | Nourriture | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_power` | Power | Puissance | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_mine` | Mining | Extraction | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_researchTime` | Research time | Temps de recherche | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_buildingCost` | Building cost | Coût des bâtiments | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_habitability` | Habitability | Habitabilité | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_homeAndFarmBuildingTime` | District build time | Construction des districts | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_buildingTime` | Build time | Temps de construction | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_speed` | Speed | Vitesse | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_damage` | Damage | Dégâts | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_colonizationSpeed` | Colonization speed | Vitesse de colonisation | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_relation` | Relations | Relations | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_armor` | Armor | Armure | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_colonizationTime` | Colonization time | Temps de colonisation | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |
| `traitEffect_mood` | Morale | Morale | Effet d'un trait d'espèce (`GetSpeciesTraits.key`) |


## À intégrer (P5 — cale sèche Engineering)

| Clé | EN | FR | Usage VR |
|---|---|---|---|
| `vr.dock.enter` | Dry dock | Cale sèche | Répéteur Engineering : entrer dans la cale sèche |
| `vr.dock.title` | Dry dock | Cale sèche | Écran vaisseau de la cale |
| `vr.dock.leave` | Back to the bridge | Retour au pont | Bouton de sortie de la cale |
| `vr.dock.newShip` | New ship | Nouveau vaisseau | `AddToFleet fleet=0` avec un ShipCore du hangar |
| `vr.dock.needCore` | Needs a Ship Core in the hangar | Il faut un Cœur de vaisseau au hangar | Nouveau vaisseau indisponible |
| `vr.dock.noShipDocked` | No ship docked at this world. | Aucun vaisseau amarré à ce monde. | Cale sans vaisseau à éditer |
| `vr.dock.pickShip` | Choose a ship to work on. | Choisissez un vaisseau à modifier. | Aucun vaisseau sélectionné |
| `vr.dock.pickModule` | Pick a module on the hangar rack. | Choisissez un module au râtelier du hangar. | Clic sur une case vide sans module choisi |
| `vr.dock.placing` | Placing: {0} — set the crate on a green cell | Pose : {0} — déposez la caisse sur une case verte | Module sélectionné |
| `vr.dock.mustTouch` | A module must touch the ship's structure. | Un module doit toucher la structure du vaisseau. | Case non adjacente |
| `vr.dock.wouldSplit` | Removing it would cut the ship in two. | Le retirer couperait le vaisseau en deux. | Retrait qui isolerait des modules du cœur |
| `vr.dock.confirmRemove` | Point again to take off {0} | Visez encore pour retirer {0} | Retrait en deux temps |
| `vr.dock.removed` | Module back in the hangar | Module rendu au hangar | Retour `RemoveShipModule` |
| `vr.dock.confirmScrap` | Tap again to scrap {0} (no refund) | Touchez encore pour détruire {0} (sans remboursement) | `DelShip` en deux temps |
| `vr.dock.scrapped` | Module scrapped | Module détruit | Retour `DelShip` |
| `vr.dock.renamed` | Ship renamed | Vaisseau renommé | Retour `RenameFleet` |
| `vr.dock.hangarEmpty` | The hangar is empty. | Le hangar est vide. | Râtelier vide |
| `vr.dock.troops` | Troop capacity | Capacité de troupes | Stat de conception (`troopCargo`) |
| `vr.dock.size` | Hull size | Taille de coque | Stat de conception (`size` ; plafond = `GetConfigs.fleet.maxFleetSize`) |
| `vr.yard.idle` | Shipyard idle. | Chantier à l'arrêt. | Onglet chantier : rien en construction |
| `vr.yard.build` | Build | Construire | `AddShip` quand le chantier est libre |
| `vr.module.family.core` | Core | Cœur | Chantier : filtre de famille de modules |
| `vr.module.family.weapon` | Weapons | Armement | Chantier : filtre de famille de modules |
| `vr.module.family.engine` | Propulsion | Propulsion | Chantier : filtre de famille de modules |
| `vr.module.family.defense` | Defense & power | Défense et énergie | Chantier : filtre de famille de modules |
| `vr.module.family.cargo` | Cargo & bays | Cargo et soutes | Chantier : filtre de famille de modules |
| `vr.module.family.troop` | Troops | Troupes | Chantier : filtre de famille de modules |
| `vr.module.family.colony` | Colony | Colonisation | Chantier : filtre de famille de modules |
| `vr.module.family.science` | Science | Science | Chantier : filtre de famille de modules |
| `vr.module.family.special` | Special | Spécial | Chantier : filtre de famille de modules |
| `vr.module.family.life` | Life & structure | Vie et structure | Chantier : filtre de famille de modules |

## À intégrer (P5 — laboratoire Science)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.lab.enter` | Research lab | Laboratoire de recherche | Porte du pont (bâbord arrière) vers le labo |
| `vr.lab.leave` | Bridge | Passerelle | Porte du labo vers le pont |
| `vr.research.pick` | Point at a crystal to study the technology. | Visez un cristal pour étudier la technologie. | Écran d'analyse, rien de sélectionné |
| `vr.research.requires` | Prerequisites | Prérequis | Écran d'analyse (`GetConfigs.researchs.requiert`) |
| `vr.research.unlocks` | Unlocks | Débloque | Écran d'analyse : modules, bâtiments, défenses, troupes qui citent la techno |
| `vr.research.kind.building` | Building | Bâtiment | ✅ web fr/en |
| `vr.research.kind.module` | Module | Module | ✅ web fr/en |
| `vr.research.kind.defense` | Defense | Défense | ✅ web fr/en |
| `vr.research.kind.troop` | Troop | Troupe | ✅ web fr/en |
| `vr.research.nextLevel` | Level {0} | Niveau {0} | Coût du prochain niveau |
| `vr.research.pts` | pts | pts | Unité des points de recherche |
| `vr.research.launch` | Research level {0} | Rechercher le niveau {0} | `ImproveResearch` quand rien ne tourne |
| `vr.research.insertHint` | …or set the sample into the synthesizer | …ou posez l'échantillon dans le synthétiseur | Geste VR : cristal du berceau → synthétiseur |
| `vr.research.idle` | No research running. | Aucune recherche en cours. | Écran du synthétiseur |
| `vr.research.started` | Research started: {0} | Recherche lancée : {0} | Retour `ImproveResearch` (corps vide) |
| `vr.research.queued` | {0} level {1} added to the queue | {0} niveau {1} ajouté à la file | Retour `ImproveResearch` `{queued, targetLevel}` |
| `vr.research.cancelled` | {0} removed from the queue, points refunded | {0} retiré de la file, points remboursés | Retour `CancelQueuedResearch` (cristal arraché du pad ou ×) |

## À intégrer (P5 — anomalies)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `anomaly_derelict_ship` | Ancient frigate wreck | Épave de frégate ancienne | Nom par type (la DB stocke le titre FR en dur) |
| `anomaly_precursor_cache` | Sealed precursor beacon | Balise précurseur scellée | idem |
| `anomaly_crystal_monolith` | Resonant crystal asteroid | Astéroïde cristallin résonnant | idem |
| `anomaly_nebula_rift` | Unstable ion rift | Faille ionique instable | idem |
| `anomalyDesc_derelict_ship` | A scout's hulk drifts in silence. Its data banks may hold precious schematics. | La carcasse d'un vaisseau éclaireur dérive en silence. Ses banques de données peuvent renfermer de précieux schémas. | Description par type |
| `anomalyDesc_precursor_cache` | An artifact of a vanished civilisation pulsing encrypted subspace signals. | Un artefact d'une civilisation disparue émettant des impulsions sub-spatiales cryptées. | idem |
| `anomalyDesc_crystal_monolith` | A space formation saturated with high-energy crystals. | Une formation géologique spatiale saturée de cristaux à haute densité énergétique. | idem |
| `anomalyDesc_nebula_rift` | A temporary tear in space giving off intense radiation, usable for quantum physics. | Une déchirure temporaire du continuum spatial dégageant d'intenses radiations exploitables pour la physique quantique. | idem |
| `vr.anomaly.preview` | +{0} research · +{1} minerals · +{2} crystals · difficulty {3} | +{0} recherche · +{1} minéraux · +{2} cristaux · difficulté {3} | Devis au pupitre / répéteur Science |
| `vr.anomaly.rewards` | +{0} research · +{1} minerals · +{2} crystals · +{3} XP | +{0} recherche · +{1} minéraux · +{2} cristaux · +{3} XP | Retour `ScanAnomaly` |

## Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
