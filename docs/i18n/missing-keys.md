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
- Clés P3 (création d'empire : `vr.create.*`, `flagShape_*`, `traitEffect_*`), P5 cale sèche (`vr.dock.*`, `vr.yard.*`, `vr.module.family.*`), P5 laboratoire (`vr.lab.*`, `vr.research.*`), P5 relevé planétaire (`credits`, `vr.survey.*`) et P5.5 table tactique v2 (`vr.table.*`, `vr.seat.*`) dans `assets/langs/{fr,en}.json`.
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

## P5.5 H3 — combat sur la table

Les noms de compétences utilisent les clés natives `battleSkill_<id>`. Les boutons Déplacer et Fin du tour utilisent `move` et `vr.tactical.endTurn`. Le bouclier utilise `shield`.

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.battle.round` | Round {0} | Manche {0} | Bandeau au-dessus du plateau |
| `vr.battle.yourTurn` | Your turn! | À vous ! | Bandeau |
| `vr.battle.theirTurn` | {0} is acting | Tour de {0} | Bandeau, {0} = vaisseau actif |
| `vr.battle.pending` | Preparing — battle starts in {0} | Préparation — début dans {0} | Bataille en attente, {0} = m:ss |
| `vr.battle.waitingFoe` | Ready — waiting for the enemy | Prêt — en attente de l'adversaire | En attente |
| `vr.battle.ready` | Ready for battle | Prêt au combat | Bouton (SetFleetState) |
| `vr.battle.withdraw` | Withdraw the ship | Retirer le vaisseau | Bouton (RemoveFleetFromBattle, en attente) |
| `vr.battle.leave` | Leave the table | Quitter la table | Bouton : rend la table au système, sans abandonner le combat |
| `vr.battle.rejoin` | Battle in progress — rejoin | Combat en cours — rejoindre | Bouton pulsant sur le rebord |
| `vr.battle.victory` | VICTORY | VICTOIRE | Bannière de fin |
| `vr.battle.defeat` | DEFEAT | DÉFAITE | Bannière de fin |
| `vr.battle.over` | Battle over | Combat terminé | Fin sans vaisseau à nous |
| `vr.battle.destroyed` | DESTROYED | DÉTRUIT | Texte flottant |
| `vr.battle.stats` | AP {0}/{1} · MP {2}/{3} | PA {0}/{1} · PM {2}/{3} | Fiche du vaisseau |
| `vr.battle.hull` | Hull | Coque | Fiche du vaisseau |
| `vr.battle.pmLeft` | {0} MP | {0} PM | Sous-titre du bouton Déplacer |
| `vr.battle.pmCost` | {0} MP | {0} PM | Étiquette de l'arc de déplacement |
| `vr.battle.apCost` | {0} AP | {0} PA | Coût d'une compétence |
| `vr.battle.range` | Range {0} | PO {0} | Portée d'une compétence ({0} = « 1-4 » ou « 3 ») |
| `vr.battle.self` | Self | Sur soi | Compétence sans cible |
| `vr.battle.cooldown` | Recharging · {0} turns | Recharge · {0} tours | Compétence en recharge |
| `vr.battle.pickMove` | Aim at a lit cell to move | Visez une case allumée pour vous déplacer | Consigne |
| `vr.battle.pickTarget` | Aim at an enemy ship | Visez un vaisseau ennemi | Consigne / refus |
| `vr.battle.pickAlly` | Aim at one of your ships | Visez un de vos vaisseaux | Consigne / refus (soin) |
| `vr.battle.pickHex` | Aim at a cell in range | Visez une case à portée | Consigne (zone, saut) |
| `vr.battle.outOfRange` | Out of range ({0}/{1} cells) | Hors de portée ({0}/{1} cases) | Refus local |
| `vr.battle.tooClose` | Too close (min {0} cells) | Trop proche (min {0} cases) | Refus local |
| `vr.battle.tooFar` | Too far ({0}/{1} MP) | Trop loin ({0}/{1} PM) | Refus local |
| `vr.battle.status.ionized` | ION | ION | Statut (ionisé : pas de compétence) |
| `vr.battle.status.jammed` | JAM | BROUIL. | Statut (brouillé : −40 % dégâts) |
| `vr.battle.status.gravity` | GRAV | GRAV | Statut (puits gravitationnel : immobile) |
| `vr.battle.status.overheated` | HEAT | SURCH. | Statut (surchauffe) |
| `vr.battle.status.armor` | ARMOUR | BLINDÉ | Blindage renforcé actif |
| `vr.battle.status.stealth` | CLOAK | VOILÉ | Voile d'invisibilité actif |
| `vr.battle.err.no_pm` | Not enough MP | Pas assez de PM | Code serveur `no_pm` |
| `vr.battle.err.no_ap` | Not enough AP | Pas assez de PA | `no_ap` |
| `vr.battle.err.out_of_range` | Out of range | Hors de portée | `out_of_range` |
| `vr.battle.err.too_close` | Too close | Trop proche | `too_close` |
| `vr.battle.err.hex_occupied` | Cell occupied | Case occupée | `hex_occupied` |
| `vr.battle.err.on_cooldown` | Still recharging | Encore en recharge | `on_cooldown` |
| `vr.battle.err.gravity_field` | Held by a gravity well | Retenu par un puits gravitationnel | `gravity_field` |
| `vr.battle.err.ionized` | Systems ionised | Systèmes ionisés | `ionized` |
| `vr.battle.err.friendly_fire` | That is one of ours | C'est un des nôtres | `friendly_fire` |
| `vr.battle.err.no_target` | No target there | Aucune cible ici | `no_target` |
| `vr.battle.err.cannot_heal_enemy` | Cannot repair an enemy | Impossible de réparer un ennemi | `cannot_heal_enemy` |
| `vr.battle.err.invalid_target` | Invalid target | Cible invalide | `invalid_target` |
| `vr.battle.err.target_required` | A target is required | Une cible est requise | `target_required` |
| `vr.battle.err.shipNotAlive` | Ship destroyed | Vaisseau détruit | `shipNotAlive` |
| `vr.battle.err.notYourShip` | Not your ship | Pas votre vaisseau | `notYourShip` |
| `vr.battle.err.battleNotActive` | The battle is not running | Le combat n'est pas en cours | `battleNotActive` |
| `vr.battle.err.skill_not_found` | Unknown skill | Compétence inconnue | `skill_not_found` |
