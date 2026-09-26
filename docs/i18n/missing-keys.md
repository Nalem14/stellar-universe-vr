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

## Quartiers du commandant (P6 — à intégrer)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.quarters.enter` | Captain's quarters | Quartiers du commandant | Panneau de la porte (tribord arrière du pont) |
| `vr.quarters.leave` | Back to the bridge | Retour au pont | Porte de la cabine |
| `vr.quarters.empire` | Empire | Empire | Console du bureau |
| `vr.quarters.species` | Species | Espèce | Onglet (la clé native `specy` n'a pas de texte EN) |
| `vr.quarters.log` | Captain's log | Journal de bord | Onglet GetActivity |
| `vr.quarters.objectives` | Objectives | Objectifs | Onglet |
| `vr.quarters.event` | Event | Événement | Onglet GetEventData |
| `vr.quarters.tokens` | Tokens: {0} rename · {1} reconfiguration | Jetons : {0} renommage · {1} reconfiguration | renameTokens / reconfigTokens |
| `vr.quarters.renameHint` | Renaming the empire uses a rename token. | Renommer l'empire consomme un jeton de renommage. |  |
| `vr.quarters.saveFlag` | Save the flag | Enregistrer le drapeau | UpdateEmpireFlag |
| `vr.quarters.flagSaved` | Flag saved. | Drapeau enregistré. |  |
| `vr.quarters.applyAuthority` | Adopt this authority | Adopter cette autorité | SetAuthority, en deux temps |
| `vr.quarters.oneToken` | 1 reconfiguration token | 1 jeton de reconfiguration | Coût affiché |
| `vr.quarters.authoritySet` | Authority updated. | Autorité mise à jour. |  |
| `vr.quarters.policyAdded` | Ethic adopted. | Éthique adoptée. | AddEmpirePolicy |
| `vr.quarters.politicsSet` | Policy updated. | Politique mise à jour. | SetPolitics |
| `vr.quarters.applySpecies` | Apply the new species | Appliquer la nouvelle espèce | UpdateSpecy type / traits |
| `vr.quarters.traitsNeeded` | Pick 2 positive and 2 negative traits. | Choisissez 2 traits positifs et 2 négatifs. |  |
| `vr.quarters.specySaved` | Species updated. | Espèce mise à jour. |  |
| `vr.quarters.noLog` | The log is empty. | Le journal est vide. |  |
| `vr.quarters.resetsIn` | Resets in {0} | Réinitialisation dans {0} | Objectifs |
| `vr.quarters.noEvent` | No event under way. | Aucun événement en cours. |  |
| `vr.quarters.endsIn` | Ends in {0} | Se termine dans {0} | Événement |
| `vr.quarters.myDamage` | Your damage: {0} ({1} %) | Vos dégâts : {0} ({1} %) | Boss mondial |
| `vr.quarters.noDamage` | You have not engaged it yet. | Vous ne l'avez pas encore engagé. |  |
| `vr.quarters.bossPool` | Reward pool: {0} Nova · {1} XP | Butin à partager : {0} Nova · {1} XP |  |
| `vr.quarters.buyFor` | Buy · {0} | Acheter · {0} | BuyShopItem, en deux temps |
| `vr.quarters.equipped` | Equipped. | Équipé. | EquipShopItem |
| `vr.quarters.topupUnavailable` | Top-up is not available from the headset yet. | La recharge n'est pas encore disponible depuis le casque. | Onglet Nova (aucun chemin de paiement VR) |
| `vr.quarters.topupHistory` | Top-up history | Historique des recharges | GetNovaTopupHistory |
| `vr.quarters.noTopup` | No top-up yet. | Aucune recharge pour l'instant. |  |
| `traitEffect_defense` | defense | défense | Effets de politique (GetPolitics) |
| `traitEffect_trade` | trade | commerce | Effets de politique |
| `traitEffect_diplomacy` | diplomacy | diplomatie | Effets de politique |

---

## Intégré côté web

- Clés `vr.*`, `crew.*`, `buildingDesc_*`, `colonyLog_*`, `relation_*` dans `assets/langs/{fr,en}.json`.
- Clés P3 (création d'empire : `vr.create.*`, `flagShape_*`, `traitEffect_*`), P5 cale sèche (`vr.dock.*`, `vr.yard.*`, `vr.module.family.*`), P5 laboratoire (`vr.lab.*`, `vr.research.*`), P5 relevé planétaire (`credits`, `vr.survey.*`) et P5.5 table tactique (`vr.table.*`, `vr.seat.*`, `vr.battle.*`) dans `assets/langs/{fr,en}.json`.
- Clés P5 cale sèche — modèles (`vr.dock.templatePlaced`, `fleetMustBeDocked`, `templateNotFound`, `templateEmpty`, `notFound`), P5.5 H3b file d'ordres 3D (`vr.table.removeStep`, `vr.table.queueRerouted`), P5 Tactical armurerie (`vr.armory.*`, `crew.tactical.*`) et P5 Comms (`vr.comms.*`, `vr.gate.*`, `crew.comms.*`) dans `assets/langs/{fr,en}.json`. Chambre diplomatique (`vr.diplo.*`, `crew.comms.warLaunched` / `peace` / `warWon` / `warLost` / `application`), aussi réutilisées par `wars-window.hbs` / `alliance-window.hbs`, avec les confirmations web (`confirm*`) et le gabarit des e-mails externes (`email_*`). `fr` et `en` ont désormais exactement le même jeu de clés.
- Contenu lu dynamiquement par le VR (noms = type du serveur) : troupes `Infantry`, `HeavyTrooper`, `ExoArmorTrooper`, `CyberneticVanguard`, `CombatDroneSquad`, `SynthWarrior` et défenses `MissileTurret`, `FlakCannon`, `PlasmaBattery`, `IonDefenseGrid`, `RailgunBastion`, `QuantumShieldArray` (affichés par `ResearchCatalog.Unlocks`) ; descriptions `descBondPRLModule` et `descTroopBay` (en). Le VR retombe sur la casse native (`Trans.Get`) pour `ScienceModule` / `TroopBay`, absents en PascalCase côté web.
- Remaps client (§1) : le code VR utilise les clés natives (`fleets`, `asteroidField`, `moveToSystem`, `defendPosition*`, `explorePlanet`, `depositCargo`, `withdrawCargo`, `harvestAsteroid`, `attackOrbit`, `cancel`, `sublight` / `hyperdrive`, `buildingDesc_<type>`, plaques `vr.station.*`, etc.).

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
