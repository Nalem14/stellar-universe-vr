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

## Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
