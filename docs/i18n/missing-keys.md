# Clés i18n manquantes — à intégrer côté serveur

Ce fichier recense les clés dont le client VR a besoin et qui manquent dans `assets/langs/{en,fr}.json` du repo web (servi par `GetTranslations`).

**Circuit** :
- L'agent VR liste ici la clé, avec ses textes EN et FR.
- Thommy l'intègre côté web (admin Translate → `assets/langs/{fr,en}.json`, ou commit direct sur ces fichiers).
- La ligne est ensuite retirée de ce fichier.

Tant qu'une clé est absente, la VR affiche **la clé brute**. En Editor, elle est aussi ajoutée à `su-missing-trans-keys.txt`. Le client n'invente jamais de texte de secours.

**Conventions** :
- **Clé native d'abord** : si une clé web existe déjà (même avec une autre casse), le client VR l'utilise telle quelle. Elle n'a rien à faire dans ce fichier : voir §1.
- **UI propre à la VR** : `vr.<zone>.<nom>`, par exemple `vr.menu.continue`.
- **Répliques crew** : `crew.<role>.<event>.<n>`. Les rôles sont `helm`, `tactical`, `engineering`, `science`, `comms` et `ops`. `<n>` part de 1, et `BarkDirector` tire une variante au hasard sans répétition.
- **Paramètres** : `{0}`, `{1}`… (`Trans.Format`, `string.Format` en culture invariante).
- Pour les répliques, les ordres et les échecs, le ton reprend celui du journal de colonisation web : l'équipage s'adresse au joueur en « Commandant ».

---

## 1. Correctifs client : la clé native existe déjà

Ce chantier est à faire **dans le code VR** ; rien à ajouter côté serveur. Les clés de gauche sont celles que le code VR passe aujourd'hui à `Trans.Get` et qui n'existent pas dans le dump.

| Clé VR actuelle | Clé native à utiliser | EN / FR natif | Fichier VR |
|---|---|---|---|
| `Alliance` | `alliance` | Alliance / Alliance | `Vfx/AlcoveSystems.cs` |
| `Battle` | `battle` | Battle / Combat | `Vfx/AlcoveSystems.cs` |
| `Defense` | `defense` | Defense / Défense | `Vfx/AlcoveSystems.cs` |
| `Empires` | `empires` | Empires / Empires | `Vfx/AlcoveSystems.cs` |
| `Jumpgate` | `jumpgate` | Jumpgate / Portail de Saut | `Vfx/AlcoveSystems.cs` |
| `Research` | `research` | Research / Recherches | `Vfx/AlcoveSystems.cs` |
| `Shop` | `shop` | Shop / Boutique | `Vfx/AlcoveSystems.cs` |
| `Upgrade` | `upGrade` | Upgrade / Améliorer | `Vfx/AlcoveSystems.cs` |
| `Wars` | `wars` | Wars / Guerres | `Vfx/AlcoveSystems.cs` |
| `Explore` | `explorePlanet` | Explore Planet / Explorer la planète | `Vfx/AlcoveSystems.cs`, `Vfx/CrewDialogue.cs` |
| `Mail` | `mailTitle` | Galactic Mails / Mails Galactiques | `Vfx/AlcoveSystems.cs` |
| `Chat` | `chatPanel` | Messages / Messagerie | `Vfx/AlcoveSystems.cs` |
| `Queue` | `buildingQueue` / `shipyardQueue` / `researchQueue` | File de construction / du chantier naval / de recherche | `Vfx/AlcoveSystems.cs` |
| `Goals` | `dailyObjectives` (+ `weeklyObjectives`, `monthlyObjectives`) | — | `Vfx/AlcoveSystems.cs` |
| `Attack` | `defendPositionAttacker` | Join attack / Participer à l'attaque | `Vfx/CrewDialogue.cs` |
| `Defend` | `defendPositionPlanet` | Defend planet / Défendre la planète | `Vfx/CrewDialogue.cs` |
| `Flee` | `defendPositionRunOut` | Run away / Fuir | `Vfx/CrewDialogue.cs` |
| `Deposit` | `depositCargo` | Deposit / Déposer | `Vfx/CrewDialogue.cs` |
| `Withdraw` | `withdrawCargo` | Load / Charger | `Vfx/CrewDialogue.cs` |
| `Mine` | `harvestAsteroid` | Harvest / Récolter | `Vfx/CrewDialogue.cs` |
| `MoveFleetToSystem` | `moveToSystem` | Move to system / Déplacer vers un système | `Vfx/CrewDialogue.cs`, `Vfx/HoloOrderPreview.cs` |
| `MoveFleetToPlanet` | `moveToPlanet` | Move to planet / Déplacer vers une planète | idem |
| `MoveFleetToAsteroid` | `moveToAsteroidField` | Move to this asteroid field / Se déplacer vers ce champ d'astéroïdes | idem |
| `Siege` | `attackOrbit` | Attack orbit / Attaquer l'orbite | `Vfx/CrewDialogue.cs` |
| — (mode de voyage) | `sublight` / `hyperdrive` | Sub-light / Sous-lumière · Hyperdrive / Hyperespace | Holomap, Helm |
| — (annulation) | `cancel` | Cancel / Annuler | Kit UI |
| `asteroid` | `asteroidField` | asteroid field / champ d'astéroïdes | `Vfx/CrewDialogue.cs`, `Vfx/HoloZoneMap.cs` |
| `spaceships` | `fleets` | Spaceships / vaisseaux | `App/BridgeDirector.cs`, `Vfx/BridgeViewTeleporter.cs`, `Vfx/CrewDialogue.cs`, `Vfx/HexBattleController.cs` |

---

## 2–3. UI VR + crew — intégrés côté web

Livré dans `stellar-universe` : clés `vr.*`, `crew.*`, `buildingDesc_*`, `colonyLog_*`, `relation_*` dans `assets/langs/{fr,en}.json` (même fichiers que l'admin Translate). Rien à rajouter ici.

---

## 4. Restant côté web

| Source web | Restant | Notes |
|---|---|---|
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais / FR brut | Migration future : stocker clé + params, localiser à la lecture |
