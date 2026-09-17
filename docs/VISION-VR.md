# Vision SU:VR — même jeu, autre corps

**Décision produit (2026-09-17) : scope B.**  
Client VR **complet** de Stellar Universe : **toutes** les features du web / `actionjs.php`, même compte, même galaxie persistante, **multiplateforme**.  
Ce n’est **pas** un compagnon, **pas** un port RTS god-cam, **pas** un 4X autonome.

Inspiration UX : [BattleGroup VR](https://www.meta.com/experiences/battlegroupvr/4459505850829471/) — on commande depuis **le pont d’un vaisseau**, la carte est un **hologramme à portée de main**, le combat se voit par les hublots. Le joueur n’est pas une caméra qui vole.

---

## 1. Promesse

Tu n’ouvres pas « l’UI Stellaris en floating canvases ».  
Tu **es** le commandant, **à bord** : pont de **n’importe lequel de tes vaisseaux**, ou — à défaut de flotte — une **fausse station en orbite** de la planète dont tu prends la vue. Jamais un avatar qui marche au sol.

Le web reste le client « bureau » (listes, grilles, clics).  
Le casque est le client « être là » : mêmes ordres, autre geste, autre lieu.

| | Web | SU:VR |
|---|---|---|
| Où tu es | Page galaxie / planète | CIC : pont d’un **vaisseau à toi**, ou fausse **station orbitale** ; hublots = ce focus |
| Carte | 2D cliquable | Table holo dans la pièce |
| Ordre de flotte | Bouton + dropdown | Token de flotte posé sur une étoile / orbite |
| Construction | Liste d’upgrades | Maquette de planète sur un pupitre |
| Combat hex | Grille à l’écran | Le plateau holo **devient** le hex |
| Chat / mail | Panneau latéral | Mur comms |
| Locomotion | Scroll / zoom | Room-scale dans le pont. Quart : tu es déjà dans ta pièce |

Le serveur ne change pas. `changesystem` = ce que voient les hublots + le focus de la table. `MoveFleet*` = les vaisseaux, pas toi.

---

## 2. Pourquoi ce format (et pas un RTS VR)

Un RTS classique en VR casse trois choses :

1. **Échelle** — une galaxie 1:1 autour de la tête est illisible et injouable sur Quest.
2. **Confort** — caméra libre + stratégie = nausée. BattleGroup ancre le joueur ; le monde vient à lui.
3. **Identité** — SU est un MMO lent (files de 12 h, jumps à `desttime`). Tu vis dans un **CIC**, tu ne « voles » pas de soleil en soleil.

Donc :

- Le joueur **ne voyage pas** de système en système. Il **téléporte sa vue** : il *habite* un autre CIC. Les flottes, elles, bougent avec `MoveFleet*`.
- **TP de vue, dans cet ordre :** (1) n’importe quel **vaisseau / flotte que tu possèdes** → tu es sur **son** pont ; (2) à défaut (pas de flotte, ou tu vises une planète) → n’importe quelle **planète** → tu es dans une **fausse station orbitale** au-dessus, pas à la surface.
- Les distances galactiques n’existent que **sur la table**.
- Le 1:1, c’est **l’intérieur du CIC** (pont vaisseau ou fausse station) et, par les hublots, le **système / l’orbite courants**. En **quart passthrough**, le 1:1 c’est **ta pièce** + un petit cluster holo — jamais la galaxie au sol.

---

## 2.1 Passthrough = mode quart (pas un second jeu)

SU a des **temps morts longs** : jump `desttime`, siège `attackEndTime`, colo 12 h, tour hex en attente de l’adversaire. Rester enfermé dans le pont Synty pendant 40 min, c’est absurde. Le passthrough sert à **rester casqué dans ta pièce**, avec un **brin de CIC** — pas à rejouer tout le jeu en AR.

Le pont immersif reste le client **complet**. Le passthrough est un **quart** : tu vois ta chambre, plus quelques hologrammes additifs. Tu ne peux **pas tout faire**. Un geste (viser les tempes / bouton « CIC ») te ramène à bord.

Lore : liaison distante vers la station. Tu n’es plus « dans » le vaisseau ; tu gardes un terminal fantôme.

### Que voit-on ?

Empilement, dans cet ordre :

1. **Ta pièce réelle** (caméras Quest) — meubles, mains, sol. Rien d’opaque qui la cache.
2. **Aucun mur Synty**, aucun hublot plein cadre, aucune galaxie 1:1 par terre.
3. Un **cluster holo** collé à un ancre (poitrine / table réelle si un plan est là, sinon 40 cm devant le sternum) :
   - verre fumé léger, unlit / additive, ~30–50 cm de large
   - **une** affaire à la fois (celle qui justifie le quart)
4. Toasts d’alerte au bord du champ (arrivée, à toi de jouer, file finie) — les SFX `success` / `dradis` déjà là.

Pas de skybox. Pas de mix pont + caméra (flicker). Fade 0,5 s pont → pièce, et l’inverse.

| Situation | Ce que le cluster montre | Ce que tu peux faire | Ce que tu ne peux pas |
|---|---|---|---|
| **Jump / transit long** (`desttime` loin) | 2 systèmes (from → dest) + pip flotte + ETA | Annuler / forcer sublight si l’API le permet ; chatter ; rentrer au CIC | Chantier, planète, stargate, poser un nouveau jump « comme sur la table » |
| **Siège / harvest / explore** (timer flotte) | Token + jauge `attackEndTime` / `harvestEndTime` / `exploreEndTime` | Attendre, chatter, rentrer au CIC à l’impact | Micro du combat planétaire |
| **Hex — pas ton tour** | Mini plateau **gris**, « en attente de {nom} » | Voir l’état, rentrer au CIC | `BattleDoAction` (plateau trop petit, lumière réelle pourrie) |
| **Hex — à toi de jouer** | Mini plateau **allumé** **ou** prompt « retour CIC » | Idéal : **forcer le pont** (1 geste). Optionnel plus tard : un coup simple sur le mini | Éditer la flotte, stargate |
| **File bâtiment / recherche** | 1 planète ou 1 tech + `working` | Rien d’autre que regarder + notif de fin | Upgrade, 9×9, maquette complète |
| **Rien de long en cours** | Passthrough **off** par défaut | — | Le quart n’est pas l’écran titre |

Proposition d’entrée (pas un mode settings caché) :

- Si un busy **> ~2 min**, le fauteuil / un bandeau propose **« Quart »** (passthrough). Refusable.
- Si busy **< ~30 s**, on reste au pont (le fade ne vaut pas le coup).
- Alerte `desttime` / `active_bship_id` à toi → pulse + option **« À bord »**.
- Device sans passthrough (Quest 2, beaucoup de PCVR) : le quart n’existe pas. Le pont suffit.

### Ce que ce n’est pas

- Un second client AR à parité features
- Le tutoriel Mixed Reality (coaching « tap a plane »)
- Des Canvas web collés dans le salon
- Une excuse pour ne pas construire le pont

Le template OpenXR passthrough sert **ce** mode-là. La détection de plan est optionnelle (ancre table) ; sans plan, le cluster suit le sternum.

---

## 3. Le lieu : CIC

Un seul « niveau » habité, réutilisé toute la session.

### 3.1 Pont / passerelle (pièce principale)

- Room-scale ~3×3 m + téléport court **dans** la pièce.
- **Table holo** au centre : carte interactive (voir §4).
- **Hublots** : système actuellement focalisé (`user.systemid`). Soleil du bon type, planètes sur `slot`, lumières de flottes locales (`GetAllFleetsAround`). En transit (amiral en jump) : tunnel / étoiles filantes (`hyperportal` SFX déjà là).
- **Fauteuil de commandement** (option) : snap-turn, moins de locomotion, table toujours à portée.

### 3.2 Où tu « es » (TP de vue)

Tu ne pilotes pas à la main. Tu **habites un CIC** ; le serveur vole. Un geste sur la table (token flotte / globe planète) **téléporte ta vue** : même pièce Unity, autre *contexte* (hublots + skin + focus API). Toi tu ne bouges pas dans la galaxie.

Priorité :

1. **N’importe quel vaisseau à toi** (`fleets` dont `userid` = toi). Tu es sur **son** pont. Hublots = système de **cette** flotte (`fleet.systemid`) ; si `desttime > now`, hyperspace / tunnel. L’ordre « monter à bord » **ne déplace pas** la flotte.
2. **À défaut, n’importe quelle planète** (y compris une que tu ne possèdes pas : observation). Tu n’atterris **pas**. Tu apparais dans une **fausse station en orbite** — intérieur CIC, hublots = la planète en contrebas + le système. Lore : relais d’observation / avant-poste fantôme, pas un bâtiment `stargate` réel.

| Mode | Quand | Intérieur 1:1 | Hublots | API (vue, pas mouvement) |
|---|---|---|---|---|
| **Pont vaisseau** | Token d’une de tes flottes, ou reprise de session sur l’amiral | Skin pont (Synty plus tard) | Système de **cette** flotte ; jump si busy | `changesystem(fleet.systemid)` ; `changeplanet` si `planetid > 0` |
| **Fausse station** | Pas de flotte, ou drop sur une planète | Skin station orbitale (même kit, autre dressing) | Orbite de **cette** planète | `changeplanet(planet.id)` + `changesystem(planet.systemid)` |
| **Défaut boot** | Login | Amiral si une flotte existe, sinon station sur une planète possédée, sinon première planète connue | Comme ci-dessus | Boot API déjà prévu (`changesystem` vers un système possédé) |

Même locomotion (room-scale **dans** la pièce). Changer de vaisseau / de planète = fade court + hublots / skybox d’orbite, **pas** une nouvelle scène « surface ».

Interdit : walker au sol, TP dans l’espace sans coque, god-cam entre les deux.

### 3.3 Alcôves (les features, en meubles)

Marcher 2 m, ce n’est pas changer de scène Unity. Ce sont des **stations diegetic** autour du pont :

| Alcôve | Features web (actions) | Interaction VR |
|---|---|---|
| **Table holo** | Galaxie, systèmes, flottes, files d’ordres, siège, hex battle | Grab / drop / pinch zoom |
| **Astrométrie** (hublot + table) | `GetSystems`, anomalies, asteroids, bounties | Zoom système, scan |
| **Planète** (maquette) | `GetResource raw=1`, bâtiments, troupes, défenses, décisions | Poke un bâtiment = upgrade ; jauges physiques |
| **Chantier** (grille 9×9) | `AddShip`, `PlaceShipModule`, `AddToFleet`… | Poser des modules sur une coque holo |
| **Recherche** | `ImproveResearch`, `ExplorePlanet` (ScienceModule) | Arbre en volume, pas une liste HTML |
| **Porte des étoiles** | Adresses, connexion, missions | Cadran d’adresse + vortex dans la baie |
| **Comms** | Chat, mail, MP, news, activity | Grand écran mural, clavier VR / dictée plus tard |
| **Diplomatie** | Relations, alliance, guerres | Cartes d’empires sur un second plateau |
| **Intendance** | Shop Nova, objectifs, flag | Vitrine / console |

Pas de menu hamburger. Si une feature n’a pas de **meuble**, elle n’est pas encore dans le jeu VR.

`PolygonSciFiSpace` = intérieur du pont. Le quart passthrough n’en a pas besoin (cluster holo seulement).  
À jeter : coaching cards. Détection de plan = option pour coller le cluster à une vraie table.

---

## 4. La table holo (cœur du contrôle)

Comme BattleGroup : **la carte vient à la main**, les vrais volumes restent par la vitre.

### 4.1 Échelles (emboîtées, pas trois apps)

1. **Galaxie** — points `systems.x/y` (disque, Y≈0). Pinch = zoom. Un système tenu = preview. Drop d’un token flotte = `MoveFleetToSystem` (`pos` = `"x.y"`).
2. **Système** — étoile + orbites = `planet.slot`. Astéroïdes. Flottes locales interpolées (`from`/`dest`/`desttime`, snapshot `Time.time` à l’ordre).
3. **Planète** — globe + bâtiments comme plots. Ressources qui montent (`GetResource` tick).
4. **Hex** — seulement si `isInBattle` / `GetBattleState` : la table **se transforme** en plateau hex. `BattleDoAction` = poser une pièce. Ce n’est **pas** un dogfight par les hublots (les hublots peuvent montrer un spectacle, le plateau est la vérité).

### 4.2 Gestes d’ordre (slice puis le reste)

- **Déplacer** : grab token flotte → drop système / planète / astéroïde → `MoveFleetTo*`. Feedback `ok` vs `ok:sublight_no_crystal`. Token busy (timer visuel) si `desttime > now`.
- **File** : perles sur le token = `SetFleetOrderQueue` / steps spec.
- **Coloniser** : ColonyShip en orbite + drop « colonize » sur planète `userid=0` habitability ≥ 6.
- **Construire** : poke plot bâtiment → `UpgradeBuilding` ; anneau de timer = `working`.
- **Interdit UX** : caméra libre galactique, minimap collée au visage, recréer les écrans web en World Space Canvas dans le vide.

Confort : table à hauteur de poitrine, 0,8–1,2 m. Joueur debout ou assis. Snap turn 45°. Pas de smooth locomotion galactique.

---

## 5. Parité features (cible B) ≠ tout livrer d’un coup

**Cible** = chaque catégorie de [action-api.json](https://stellar-universe.com/action-api.json) a un meuble et des gestes.  
**Livraison** = tranches, mais **l’archi du CIC est posée dès la première slice** (pièce + table + hublots). Ne pas construire un overlay compagnon « qu’on enrichira ». Le **quart passthrough** vient **après** qu’un jump / un hex existent (sinon il n’y a rien à veiller).

Ordre qui respecte le lieu :

1. Réseau (`ActionJs` selon spec) + boot VR API  
2. **Pièce pont** Synty + locomotion interne  
3. Hublots = système focalisé (`changesystem`)  
4. Table holo galaxie/système + token flotte + un `MoveFleet*`  
5. Maquette planète + `GetResource` + un upgrade  
6. Chantier 9×9, comms  
7. Hex **sur la table**  
8. **Quart passthrough** (cluster holo + fade) branché sur `desttime` / tours hex  
9. Stargate, alliance, guerres, shop  

Le web peut rester en avance : un bouton manquant en VR n’est pas une feature « absente du jeu », c’est une alcôve pas encore construite. Le compte, lui, est le même.

---

## 6. Multiplateforme

- Un empire, des clients. Quest / PCVR ici ; navigateur et app ailleurs.
- Token **minté sur l’appareil** (IP). Pas de token web collé dans le casque.
- Pas de simu locale. Tick = API. La VR n’a pas de règles d’économie différentes.
- Conflit web + casque en parallèle : le serveur tranche (busy flags). La table se contente de **poll** et d’afficher l’état.

---

## 7. Ce que ça change pour le code

Le proto 2022.3 n’est **pas** le socle : **nouveau projet Unity 6 LTS**, template VR, assets volés. Voir audit §8.

- Scène de jeu = **intérieur pont** + rig XR. Galaxie = table, pas le world. Passthrough = couche caméra + cluster holo, **sans** détruire le pont (on le cache).
- `GameLoader` ne doit plus spawner toute la galaxie dans le world 1:1. Galaxie = renderer de **table**. Système courant = renderer **hublot**.
- Positions : `x,y` et `slot` déterministes, jamais `Random` pour l’univers.
- Prefabs vaisseaux : miniatures de table + silhouettes lointaines hublot, pas 200 hulls physiques.
- UI XRI : poke/grab sur des **objets de pièce**, pas des Canvas d’écran.
- Audio : `ambient` / `eraSpace` dans le pont ; `travelengine` / `startHyperspace` / `hyperportal` quand l’amiral jump ; `click` sur la table.

---

## 8. Anti-patterns (rejetés)

- Scope A « compagnon 5 boutons ».  
- God-cam RTS, même « jolie ».  
- Recoller l’HTML du web en panneaux flottants.  
- Tutoriel Mixed Reality (coaching cards comme gameplay).  
- Faire du passthrough un **second jeu** à parité (chantier, stargate, hex dense). C’est un **quart** : pièce réelle + cluster, features réduites.  
- Dogfight 1ère personne comme vérité du combat hex.  
- Téléporter le joueur **à la surface** d’une planète pour « jouer ». Le TP de vue = pont d’un **vaisseau à toi** ou **fausse station orbitale**, jamais un walker.  
- Réécrire le serveur « pour la VR ».  
- Forcer le passthrough : sans caméras, le pont seul.

---

## 9. Phrase de pitch

**Stellar Universe VR : tu commandes le même empire que sur le web, depuis un CIC. Tu te TP dans n’importe lequel de tes vaisseaux ; à défaut, dans une fausse station en orbite d’une planète. La galaxie est sur la table. Les hublots, c’est seulement là où tu regardes.**
