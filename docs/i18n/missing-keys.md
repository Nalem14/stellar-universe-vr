# Clés i18n manquantes — à intégrer côté serveur

Ce fichier recense les clés dont le client VR a besoin et qui manquent dans `assets/langs/{en,fr}.json` du repo web (servi par `GetTranslations`).

**Circuit** :
- L'agent VR liste ici la clé, avec ses textes EN et FR.
- Thommy l'intègre côté web.
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

## 2. Nouvelles clés UI VR

Ces clés n'ont pas d'équivalent natif dans le dump.

| Clé | EN | FR | Usage VR |
|---|---|---|---|
| `vr.menu.continue` | Continue | Continuer | Console du sas : reprendre la session |
| `vr.menu.noEmpire` | No empire is registered to this commander yet. | Aucun empire n'est encore enregistré pour ce commandant. | Sas : compte sans empire, en attendant `CreateEmpire` |
| `vr.common.ok` | Confirmed | Confirmé | Retour générique d'un ordre réussi |
| `vr.common.error` | Command failed | Échec de la commande | Retour générique d'un échec, quand le serveur n'envoie pas de message |
| `vr.boot.restoreFailed` | Unable to restore the bridge position. | Impossible de restaurer la position du pont. | `SessionBoot` : remplace la clé technique `BootFromAnchorOrDefault` |
| `vr.bridge.moveFleet` | Move ship | Déplacer le vaisseau | Aperçu d'ordre holomap sans cible typée (remplace `MoveFleet`) |
| `vr.tactical.endTurn` | End turn | Fin du tour | Combat hex (`BattleEndFleetTurn`) |
| `vr.engineering.addCore` | Build core | Construire un noyau | Chantier : premier module `ShipCore` |
| `vr.engineering.layout` | Ship layout | Plan du vaisseau | Designer 9×9 |
| `vr.stargate.addresses` | Known addresses | Adresses connues | Stargate (`GetKnownAddresses`) |
| `vr.ops.decide` | Decide | Décider | Décisions planétaires |
| `vr.tactical.troops` | Troops | Troupes | Recrutement et embarquement de troupes |
| `vr.station.helm` | Helm | Navigation | Plaque de station |
| `vr.station.tactical` | Tactical | Tactique | Plaque de station |
| `vr.station.engineering` | Engineering | Ingénierie | Plaque de station |
| `vr.station.science` | Science | Sciences | Plaque de station |
| `vr.station.comms` | Comms | Communications | Plaque de station |
| `vr.station.ops` | Operations | Opérations | Plaque de station |
| `vr.station.council` | Council room | Salle du conseil | Destination TP (admin d'empire) |
| `vr.travel.eta` | ETA {0} | Arrivée dans {0} | Aperçu d'ordre sur la holomap |
| `vr.travel.cost` | Cost: {0} crystal | Coût : {0} cristal | Aperçu d'ordre (hyperspace / Bond PRL) |
| `vr.order.confirm` | Confirm order | Confirmer l'ordre | Holomap : confirmation après le lâcher |

---

## 3. Répliques du crew (P2)

Un événement de jeu peut avoir plusieurs variantes, et `BarkDirector` en tire une au hasard. `{0}` désigne la cible (système, planète, vaisseau), `{1}` une durée ou une quantité.

### Helm — navigation

| Clé | EN | FR |
|---|---|---|
| `crew.helm.greet.1` | Helm standing by, Commander. | Navigation paré, Commandant. |
| `crew.helm.greet.2` | Course plotter is live. Where to? | Calculateur de cap en ligne. Où allons-nous ? |
| `crew.helm.ack.1` | Course laid in for {0}. | Cap établi vers {0}. |
| `crew.helm.ack.2` | Aye, heading for {0}. | Bien reçu, nous faisons route vers {0}. |
| `crew.helm.ack.3` | Engines engaged. {0}, here we come. | Moteurs engagés. En route pour {0}. |
| `crew.helm.hyperspace.1` | Spooling hyperdrive… jump in three, two, one. | Montée en charge de l'hyperpropulseur… saut dans trois, deux, un. |
| `crew.helm.hyperspace.2` | Hyperspace window open. Hold on. | Fenêtre hyperspatiale ouverte. Accrochez-vous. |
| `crew.helm.sublightFallback.1` | Not enough crystal for the jump, Commander. Proceeding at sublight. | Pas assez de cristal pour le saut, Commandant. On continue en subluminique. |
| `crew.helm.sublightModules.1` | We lack jump drives for a hull this size. Going sublight. | Pas assez de modules de saut pour cette coque. Passage en subluminique. |
| `crew.helm.prlBond.1` | Bond PRL engaged. Folding space to {0}. | Liaison PRL engagée. Repli de l'espace vers {0}. |
| `crew.helm.prlRecharge.1` | The PRL bond is still recharging. {1} to go. | La liaison PRL se recharge encore. Encore {1}. |
| `crew.helm.arrive.1` | We've arrived at {0}. | Nous sommes arrivés à {0}. |
| `crew.helm.arrive.2` | Dropping out of transit. {0} on the viewscreen. | Sortie de transit. {0} à l'écran. |
| `crew.helm.busy.1` | She's already under way, Commander. | Le vaisseau est déjà en mouvement, Commandant. |
| `crew.helm.fail.1` | Can't plot that course, Commander. | Impossible de tracer ce cap, Commandant. |
| `crew.helm.idle.1` | Holding station. All thrusters nominal. | Position maintenue. Propulseurs nominaux. |
| `crew.helm.idle.2` | Nav buoys are quiet today. | Les balises de navigation sont calmes aujourd'hui. |

### Tactical — combat

| Clé | EN | FR |
|---|---|---|
| `crew.tactical.greet.1` | Tactical ready. Weapons cold. | Tactique paré. Armes au repos. |
| `crew.tactical.contact.1` | Contact! Unknown vessel entering the system. | Contact ! Vaisseau inconnu entrant dans le système. |
| `crew.tactical.hostile.1` | Hostile signature, Commander. {0}. | Signature hostile, Commandant. {0}. |
| `crew.tactical.hostile.2` | Enemy ships in range. Recommend battle stations. | Vaisseaux ennemis à portée. Je recommande les postes de combat. |
| `crew.tactical.pirate.1` | Pirate raiders spotted, level {1}. | Pillards pirates repérés, niveau {1}. |
| `crew.tactical.battleStart.1` | Engagement started. Shields up. | Engagement commencé. Boucliers levés. |
| `crew.tactical.yourTurn.1` | Our move, Commander. | À nous de jouer, Commandant. |
| `crew.tactical.yourTurn.2` | Firing solution ready. Awaiting your order. | Solution de tir prête. J'attends vos ordres. |
| `crew.tactical.hit.1` | Direct hit! | Coup au but ! |
| `crew.tactical.damaged.1` | We're taking damage! | Nous encaissons ! |
| `crew.tactical.victory.1` | Target neutralized. The field is ours. | Cible neutralisée. Le terrain est à nous. |
| `crew.tactical.defeat.1` | We've lost the engagement, Commander. | Nous avons perdu l'engagement, Commandant. |
| `crew.tactical.siege.1` | Beginning orbital siege of {0}. | Début du siège orbital de {0}. |
| `crew.tactical.stance.1` | Defensive posture updated. | Posture défensive mise à jour. |
| `crew.tactical.underAttack.1` | {0} is under attack! | {0} est attaquée ! |
| `crew.tactical.fail.1` | Negative, Commander. We can't engage. | Négatif, Commandant. Engagement impossible. |
| `crew.tactical.idle.1` | Sensors clear. No threats detected. | Capteurs dégagés. Aucune menace détectée. |

### Engineering — vaisseau, chantier, cargo

| Clé | EN | FR |
|---|---|---|
| `crew.engineering.greet.1` | Engineering here. She's holding together. | Ingénierie. Le vaisseau tient bon. |
| `crew.engineering.ack.1` | On it, Commander. | Je m'en occupe, Commandant. |
| `crew.engineering.harvest.1` | Mining lasers online. Harvesting {0}. | Lasers d'extraction en ligne. Récolte sur {0}. |
| `crew.engineering.cargoFull.1` | Cargo holds are full, Commander. | Les soutes sont pleines, Commandant. |
| `crew.engineering.moduleBuilt.1` | New module ready in the hangar. | Nouveau module prêt au hangar. |
| `crew.engineering.modulePlaced.1` | Module fitted. Hull integrity nominal. | Module monté. Intégrité de coque nominale. |
| `crew.engineering.queueDone.1` | Shipyard queue complete. | File du chantier terminée. |
| `crew.engineering.queueFull.1` | The shipyard queue is full. | La file du chantier est pleine. |
| `crew.engineering.fail.1` | Can't do that with what we've got, Commander. | Impossible avec ce qu'on a, Commandant. |
| `crew.engineering.idle.1` | Reactor humming. Running diagnostics. | Réacteur stable. Diagnostics en cours. |
| `crew.engineering.idle.2` | Someone's been rerouting the coffee line again. | Quelqu'un a encore dérivé la ligne du café. |

### Science — exploration, anomalies, recherche

| Clé | EN | FR |
|---|---|---|
| `crew.science.greet.1` | Science station online. | Station scientifique en ligne. |
| `crew.science.explore.1` | Launching survey of {0}. | Lancement du relevé de {0}. |
| `crew.science.exploreDone.1` | Survey of {0} complete. Fascinating data. | Relevé de {0} terminé. Données fascinantes. |
| `crew.science.anomaly.1` | Anomaly detected in this system. | Anomalie détectée dans ce système. |
| `crew.science.scan.1` | Scanning the anomaly… | Analyse de l'anomalie… |
| `crew.science.researchDone.1` | Research complete: {0}. | Recherche terminée : {0}. |
| `crew.science.researchStart.1` | Research on {0} underway. | Recherche sur {0} en cours. |
| `crew.science.queueFull.1` | Research queue is full. | La file de recherche est pleine. |
| `crew.science.fail.1` | Our instruments can't do that, Commander. | Nos instruments ne le permettent pas, Commandant. |
| `crew.science.idle.1` | Stellar readings are within expected ranges. | Relevés stellaires dans les normes. |

### Comms — messages, diplomatie

| Clé | EN | FR |
|---|---|---|
| `crew.comms.greet.1` | Comms open, Commander. | Communications ouvertes, Commandant. |
| `crew.comms.newMail.1` | Incoming transmission. {1} unread. | Transmission entrante. {1} non lue(s). |
| `crew.comms.chat.1` | Chatter on the galactic channel. | Activité sur le canal galactique. |
| `crew.comms.warDeclared.1` | {0} has declared war on us! | {0} nous a déclaré la guerre ! |
| `crew.comms.peaceOffer.1` | {0} is offering peace terms. | {0} propose des conditions de paix. |
| `crew.comms.allianceInvite.1` | An alliance invitation has arrived. | Une invitation d'alliance est arrivée. |
| `crew.comms.sent.1` | Message sent. | Message transmis. |
| `crew.comms.stargateOpen.1` | Stargate connection established. | Connexion stargate établie. |
| `crew.comms.fail.1` | Transmission failed, Commander. | Échec de la transmission, Commandant. |
| `crew.comms.idle.1` | Subspace is quiet. | Le subespace est calme. |

### Ops — colonies, bâtiments, ressources

| Clé | EN | FR |
|---|---|---|
| `crew.ops.greet.1` | Operations ready. Colonies report in. | Opérations parées. Les colonies font leur rapport. |
| `crew.ops.buildStart.1` | Construction of {0} started. | Construction de {0} lancée. |
| `crew.ops.buildDone.1` | {0} upgraded on {1}. | {0} amélioré(e) sur {1}. |
| `crew.ops.queueFull.1` | Construction queue is full on that world. | La file de construction de ce monde est pleine. |
| `crew.ops.decision.1` | A planetary decision awaits you on {0}. | Une décision planétaire vous attend sur {0}. |
| `crew.ops.colonize.1` | Colony ship deployed to {0}. | Vaisseau colonial déployé vers {0}. |
| `crew.ops.lowResources.1` | Resources are running low, Commander. | Les ressources s'épuisent, Commandant. |
| `crew.ops.cargoDeposited.1` | Cargo transferred to {0}. | Cargaison transférée sur {0}. |
| `crew.ops.fail.1` | We can't afford that right now. | Nous n'en avons pas les moyens pour l'instant. |
| `crew.ops.idle.1` | Production is steady across our worlds. | Production stable sur nos mondes. |

---

## 4. Libellés codés en dur côté web

Ces textes sont en dur dans le code web, sans clé i18n. Ils sont à convertir **côté web**. Le client VR en a besoin pour la parité, et il n'est pas question de les recopier en local.

| Source web | Contenu | Clés suggérées |
|---|---|---|
| `assets/js/src/scenes/planet.js` | Onglets `buildings`, `shipyard`, `baseMilitaire`, `jumpgate`, `decisions`, `stats` (libellés FR en dur) | `planetTab.<id>` |
| `assets/js/src/scenes/planet.js` (~l. 516) | Journal de colonisation, « Journal du Corps Expéditionnaire Colonial » | `colonyLog.title`, `colonyLog.<n>` |
| `assets/js/src/ui/EmpireHubUI.js` | Sections identité / espèce / autorité / éthiques / politiques / vue d'ensemble | `empireHub.<section>` |
| `assets/js/src/scenes/research.js` (`NODES`) | Catégories et couleurs de l'arbre de recherche (FR en dur) | `researchCategory.<id>` |
| `model/battle.php` (`BATTLE_SKILL_DEFS`) | Noms et descriptions des skills de combat (FR) | `battleSkill_<id>`, `battleSkillDesc_<id>` |
| `model/decision.php` (`DECISION_DEFS`) | Titres et descriptions des décisions planétaires (FR uniquement) | `decision_<id>`, `decisionDesc_<id>`, `decisionChoice_<id>_<choice>` |
| `assets/js/src/scenes/system.js` | Libellés de relation de l'overlay tactique | réutiliser `relation_key` → `relation_<key>` |
| `GetActivity` (DB) | Entrées du journal d'activité stockées en anglais brut | clé + paramètres en DB |
