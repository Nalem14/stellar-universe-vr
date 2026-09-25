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

## Diplomatie (P5 Comms — à intégrer)

| Clé | EN | FR | Contexte |
|---|---|---|---|
| `vr.diplo.enter` | Diplomacy chamber | Chambre diplomatique | Panneau de la porte, cloison arrière du pont |
| `vr.diplo.leave` | Back to the bridge | Retour au pont | Porte de sortie de la chambre |
| `vr.diplo.open` | Diplomacy | Diplomatie | Bouton de la console Comms |
| `vr.diplo.title` | Chancellery | Chancellerie | Écran de droite |
| `vr.diplo.dossier` | Empire dossier | Dossier d'empire | Écran de gauche |
| `vr.diplo.tab.wars` | Conflicts | Conflits | Onglet |
| `vr.diplo.tab.alliance` | Alliance | Alliance | Onglet |
| `vr.diplo.tab.registry` | Registry | Registre | Onglet (GetAlliances) |
| `vr.diplo.pickHint` | Point at a flag on the orrery to open its dossier. | Pointez un drapeau de l'orrery pour ouvrir son dossier. |  |
| `vr.diplo.dossierHint` | Declaring war sets your demands: planets and/or resources, won by force or by capitulation. | Déclarer la guerre fixe vos exigences : planètes et/ou ressources, obtenues par la force ou par capitulation. |  |
| `vr.diplo.stats` | {0} planet(s) · {1} ship(s) · score {2} | {0} planète(s) · {1} vaisseau(x) · score {2} | GetEmpires planets / fleets / score |
| `vr.diplo.relation` | Relation {0} / 100 | Relation {0} / 100 | GetRelation |
| `vr.diplo.member` | Member of your alliance | Membre de votre alliance |  |
| `vr.diplo.attacker` | You are the attacker | Vous êtes l'attaquant |  |
| `vr.diplo.defender` | You are the defender | Vous êtes le défenseur |  |
| `vr.diplo.seeWar` | Open the conflict | Ouvrir le conflit |  |
| `vr.diplo.compose` | War demands | Exigences de guerre | Composeur DeclareWar |
| `vr.diplo.stock` | Known reserves: {0} minerals · {1} crystals · {2} biomass | Réserves connues : {0} minerai · {1} cristal · {2} biomasse | Somme GetEmpirePlanets |
| `vr.diplo.confirm` | Confirm? | Confirmer ? | Deuxième pression d'un ordre irréversible |
| `vr.diplo.declared` | War declared on {0}. | Guerre déclarée à {0}. |  |
| `vr.diplo.invited` | Invitation sent to {0}. | Invitation envoyée à {0}. |  |
| `vr.diplo.cancelInvite` | Withdraw the invitation | Retirer l'invitation | CancelAllianceInvite |
| `vr.diplo.inviteCancelled` | Invitation withdrawn. | Invitation retirée. |  |
| `vr.diplo.applied` | Application sent to {0}. | Candidature envoyée à {0}. |  |
| `vr.diplo.cancelApply` | Withdraw the application | Retirer la candidature | CancelAllianceInvite (candidature) |
| `vr.diplo.applicationCancelled` | Application withdrawn. | Candidature retirée. |  |
| `vr.diplo.noWar` | Your empire is at peace. | Votre empire est en paix. |  |
| `vr.diplo.noWarHint` | No conflict under way. Wars are declared from an empire's dossier. | Aucun conflit en cours. Une guerre se déclare depuis le dossier d'un empire. |  |
| `vr.diplo.demands` | Demands: {0} | Exigences : {0} |  |
| `vr.diplo.planetsN` | {0} planet(s) | {0} planète(s) |  |
| `vr.diplo.endedOn` | Ended on {0} | Terminée le {0} | endedAt |
| `vr.diplo.peaceFromMe` | Peace offer sent — awaiting their answer. | Offre de paix envoyée — en attente de réponse. |  |
| `vr.diplo.peaceFromThem` | {0} offers peace. | {0} propose la paix. |  |
| `vr.diplo.peaceOffered` | Peace offered to {0}. | Paix proposée à {0}. |  |
| `vr.diplo.peaceWithdrawn` | Peace offer to {0} withdrawn. | Offre de paix à {0} retirée. |  |
| `vr.diplo.peaceSigned` | Peace signed with {0}. | Paix signée avec {0}. |  |
| `vr.diplo.peaceRefused` | Peace offer from {0} refused. | Offre de paix de {0} refusée. |  |
| `vr.diplo.surrendered` | You abandoned the war against {0}. | Vous avez abandonné la guerre contre {0}. | SurrenderWar |
| `vr.diplo.capitulated` | You accepted the demands of {0}. | Vous avez accepté les exigences de {0}. | AcceptWarDemands |
| `vr.diplo.surrenderHint` | Ends the war, without reparations. | Met fin à la guerre, sans réparations. |  |
| `vr.diplo.capitulateHint` | The attacker receives all its demands now. | L'attaquant reçoit immédiatement toutes ses exigences. |  |
| `vr.diplo.you` | you | vous | Membre = soi |
| `vr.diplo.save` | Save | Enregistrer | Description d'alliance |
| `vr.diplo.saved` | Description saved. | Description enregistrée. |  |
| `vr.diplo.kicked` | {0} was expelled. | {0} a été exclu. |  |
| `vr.diplo.transferred` | Leadership handed to {0}. | Direction transmise à {0}. |  |
| `vr.diplo.promoted` | {0} promoted to officer. | {0} promu officier. |  |
| `vr.diplo.demoted` | {0} demoted to member. | {0} rétrogradé membre. |  |
| `vr.diplo.applicationAccepted` | Application accepted. | Candidature acceptée. |  |
| `vr.diplo.applicationDeclined` | Application declined. | Candidature refusée. |  |
| `vr.diplo.disbanded` | Alliance disbanded. | Alliance dissoute. |  |
| `vr.diplo.left` | You left the alliance. | Vous avez quitté l'alliance. |  |
| `vr.diplo.noInvite` | No invitation pending. | Aucune invitation en attente. |  |
| `vr.diplo.joined` | Welcome to the alliance. | Bienvenue dans l'alliance. |  |
| `vr.diplo.inviteDeclined` | Invitation declined. | Invitation refusée. |  |
| `vr.diplo.nameTagRequired` | Enter a name and a tag. | Saisissez un nom et un tag. |  |
| `vr.diplo.founded` | Alliance {0} founded. | Alliance {0} fondée. |  |
| `vr.diplo.registryHint` | Existing alliances are listed in the Registry: apply to join one. | Les alliances existantes figurent au Registre : candidatez pour en rejoindre une. |  |
| `vr.diplo.noAlliances` | No alliance registered yet. | Aucune alliance répertoriée. |  |
| `vr.diplo.unaligned` | Unaligned | Non aligné | Blason, sans alliance |
| `crew.comms.warLaunched.1` | Declaration transmitted. We are at war with {0}, Commander. | Déclaration transmise. Nous sommes en guerre contre {0}, Commandant. | Après DeclareWar |
| `crew.comms.peace.1` | Peace is signed with {0}. | La paix est signée avec {0}. | Guerre terminée status=peace |
| `crew.comms.warWon.1` | Victory, Commander! {0} yields to our demands. | Victoire, Commandant ! {0} cède à nos exigences. | Fin de guerre gagnée |
| `crew.comms.warLost.1` | The war against {0} is lost, Commander. | La guerre contre {0} est perdue, Commandant. | Fin de guerre perdue |
| `crew.comms.application.1` | {0} asks to join the alliance. | {0} demande à rejoindre l'alliance. | Nouvelle candidature (officiers) |

---

## Intégré côté web

- Clés `vr.*`, `crew.*`, `buildingDesc_*`, `colonyLog_*`, `relation_*` dans `assets/langs/{fr,en}.json`.
- Clés P3 (création d'empire : `vr.create.*`, `flagShape_*`, `traitEffect_*`), P5 cale sèche (`vr.dock.*`, `vr.yard.*`, `vr.module.family.*`), P5 laboratoire (`vr.lab.*`, `vr.research.*`), P5 relevé planétaire (`credits`, `vr.survey.*`) et P5.5 table tactique (`vr.table.*`, `vr.seat.*`, `vr.battle.*`) dans `assets/langs/{fr,en}.json`.
- Clés P5 cale sèche — modèles (`vr.dock.templatePlaced`, `fleetMustBeDocked`, `templateNotFound`, `templateEmpty`, `notFound`), P5.5 H3b file d'ordres 3D (`vr.table.removeStep`, `vr.table.queueRerouted`), P5 Tactical armurerie (`vr.armory.*`, `crew.tactical.*`) et P5 Comms (`vr.comms.*`, `vr.gate.*`, `crew.comms.*`) dans `assets/langs/{fr,en}.json`. `fr` et `en` ont désormais exactement le même jeu de clés.
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
