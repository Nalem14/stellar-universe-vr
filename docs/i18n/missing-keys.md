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
| `vr.dock.size` | Hull size | Taille de coque | Stat de conception (`size`, plafond serveur 100) |
| `positionOccupied` | This cell is already taken. | Cette case est déjà occupée. | Erreur brute `PlaceShipModule` |
| `invalidPosition` | Invalid position on the grid. | Position invalide sur la grille. | Erreur brute `PlaceShipModule` |
| `shipNotInHangar` | This module is not available in the hangar. | Ce module n'est pas disponible au hangar. | Erreur brute `PlaceShipModule` |
| `cannotRemoveCore` | The ship core cannot be removed. | Le cœur du vaisseau ne peut pas être retiré. | Erreur brute `RemoveShipModule` |
| `shipNotFound` | Module not found. | Module introuvable. | Erreur brute `RemoveShipModule` |

## Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
