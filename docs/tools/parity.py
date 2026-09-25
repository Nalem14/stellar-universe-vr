import json,os,re,subprocess
WEB='/Users/thommy/Websites/stellar-universe'; VR='/Users/thommy/Unity/Games/stellar-universe-vr'
d=json.load(open(f'{WEB}/action-api.json')); A=d['actions']
php=open(f'{WEB}/actionjs.php').read()
registered=set(re.findall(r'addAction\("([A-Za-z]+)"',php))|set(re.findall(r"\$_GET\['action'\]\s*==\s*\"([A-Za-z]+)\"",php))
def files(root,exts):
    out=[]
    for dp,_,fs in os.walk(root):
        for f in fs:
            if f.endswith(exts): out.append(os.path.join(dp,f))
    return out
vrf={p:open(p,errors='ignore').read() for p in files(f'{VR}/Assets/_Core/Scripts',('.cs',))}
webf={}
for sub in ('assets/js/src','view','controller','assets/views'):
    for p in files(f'{WEB}/{sub}',('.js','.php','.hbs')): webf[p]=open(p,errors='ignore').read()
def find(name,fs,root):
    pat=re.compile(r'["\']'+name+r'["\'&]|action='+name+r'\b')
    hits=[os.path.relpath(p,root) for p,t in fs.items() if pat.search(t)]
    return sorted(hits)
STATION={'auth':'Sas (Menu)','camera':'Pont — TP de vue','meta':'Système (boot)','galaxy':'Science','fleet':'Helm','ship':'Engineering','planet':'Ops','combat':'Tactical','stargate':'Comms','jumpgate':'Helm','social':'Comms','alliance':'Comms','war':'Comms','empire':'Conseil'}
PHASE={'auth':'—','camera':'—','meta':'P0','galaxy':'P5','fleet':'P5','ship':'P5','planet':'P5','combat':'P5','stargate':'P5','jumpgate':'P5','social':'P5','alliance':'P5','war':'P5','empire':'P6'}
OV_ST={'GetEmpirePlanets':'Système (boot)','GetBounties':'Science','ClaimBounty':'Science','CompleteBounty':'Science','GetSystemAnomalies':'Science','ScanAnomaly':'Science','GetPlanet':'Science','GetSystemAnomalies':'`AnomalyService` : une lecture par système visité (le serveur fait apparaître une anomalie à 45 % quand il n\'y en a pas) ; titres par type (`anomaly_<type>`), pas le texte FR stocké','ScanAnomaly':'Vaisseau scanneur (ScienceModule / SensorArray / DeepSpaceScanner) déposé sur le jeton, devis des gains au pupitre ; ou répéteur Science','GetBounties':'Tableau des contrats du labo, relu toutes les 20 s dans la salle (le serveur en génère quand < 4 ouverts)','ClaimBounty':'Tableau du labo','CompleteBounty':'Remise au tableau du labo ; serveur exige flotte empire dans `target_systemid` (`bounty_need_fleet_onsite`) ; `reward_credits` → cristal','GetPlanet':'Relevé planétaire Science (répéteur → écran face au captain), planètes du système en vue ; `user` jamais gardé','ImproveResearch':'Science','SpeedupResearch':'Science','CheckResearchQueue':'Science','CancelQueuedResearch':'Science','GetSystemAsteroids':'Helm','RecruitTroop':'Tactical','BuildDefenseUnit':'Tactical','LoadTroops':'Tactical','UnloadTroops':'Tactical','FleetAttackPlanet':'Tactical','UpdateFleetDefendPosition':'Tactical','Colonize':'Ops','ExplorePlanet':'Science','HarvestAsteroid':'Engineering','DepositCargo':'Ops','WithdrawCargo':'Ops','GetDailyObjectives':'Conseil','GetShopData':'Conseil','GetActivity':'Comms','GetMeEmpire':'Système (boot)','GetEmpires':'Comms','GetRelation':'Comms','GetEmpire':'Comms','GetSystems':'Holo table','changesystem':'Pont — TP de vue','GetLatestNews':'Sas (Menu)','GetGameAnnouncements':'Sas (Menu)','GetEventData':'Conseil','SpeedupFleetTravel':'Helm','AddShip':'Engineering','CheckShipQueue':'Engineering','CancelQueuedShip':'Engineering','SpeedupShipyard':'Engineering','GetResource':'Ops (poll global)','GetConfigs':'Système (boot)','GetTranslations':'Système (boot)'}
OV_PH={'AddFleetOrderStep':'Étape JSON : `targetId` (planète / astéroïde) ; `moveToSystem` avec **`x`,`y`** (alias `targetX`/`targetY` acceptés)','RemoveFleetOrderStep':'`stepIndex` 0-based, repeater Helm','ToggleFleetQueueLoop':'`loop` explicite 0/1','MakeBattle':'fleets = mon vaisseau + cibles, `planetid` seulement si ≠ 0 (0 vs pirates), puis `UpdateBattle` (web startTacticalBattle)','Colonize':'`ship` = id du module `colonyShip` (legacy `ColonyShip` OK) ; planète libre, habitabilité ≥ 6','MoveFleetToSystem':'P4','MoveFleetToPlanet':'P4','MoveFleetToAsteroid':'P4','PrlBondFleetToSystem':'P4','SetFleetOrderQueue':'P4','AddFleetOrderStep':'P4','RemoveFleetOrderStep':'P4','ClearFleetOrderQueue':'P4','ToggleFleetQueueLoop':'P4','GetAllFleets':'P0','GetResource':'P0','GetMeEmpire':'P0','GetConfigs':'P0','GetTranslations':'P0','GetSystems':'P4','BattleDoAction':'P0','GetActivity':'P6','GetLatestNews':'P6','GetGameAnnouncements':'P6','GetEmpires':'P5','GetRelation':'P5','GetEmpire':'P5','GetMyWars':'P5','GetMyAlliance':'P5','GetEmpirePlanets':'P5','CreateEmpire':'P3','GetLeaderTraits':'P3'}
NOTES={'CheckShipQueue':'`percent` = elapsed/SHIPSTATS.time (plus time()/endTime)','BattleDoAction':'Envoyer `subaction` + `battle_subaction` (jamais `action`) ; `skill_id` optionnel (omit / vide / `0` pour un move)','PlaceShipModule':'Cale sèche : caisse posée à la main (ou case visée) ; serveur + VR : adjacence 4-voisins, cœur, planète, MAX_FLEET_SIZE, cache fleet_stats_','RemoveShipModule':'Cale sèche : en deux temps ; refusé si le retrait couperait le vaisseau du cœur','AddToFleet':'`fleet=0` + ShipCore → JSON `{ok,fleet}` (systemid=planète) ; refuse notYourShip sans supprimer','DelShip':'Râtelier du hangar : destruction en deux temps','RenameFleet':'Cale sèche, clavier Quest','GetShipLayout':'Coque 1:1 dans la cale + hublots (`ShipHullBuilder`)','GetEmpirePlanets':'`OwnedPlanets` : mes planètes fraîches (id, `systemid`, slot — web `c94c803`), au boot et après une fondation ; `GetSystems` en secours seulement','GetAllFleets':'Cache serveur 3 s → poll VR 3,5 s. Traite les files de flotte. `user` = PublicUser (id/username) ; cache fleets purgé au hit','GetResource':'`EconomyService` : `raw=1` sur **toutes** les planètes (csv ≤ 50) toutes les 10 s — accumule la production et fait avancer les files. `user` = PublicUser (id/username) seulement','GetAllFleetsAround':'Interdit en VR (règle AGENTS) — `GetAllFleets` + filtre client','ProcessFleetOrderQueue':'Géré par cron + `GetAllFleets` ; pas d\'UI','DoTurnBattle':'Legacy, non utilisé par le web','GetBattle':'Legacy','UpgradeBuilding':'Console Ops : devis serveur (coût × niveau cible, temps × computer), « Ajouter à la file » si chantier actif ; réponse texte (niveau) ou JSON `queued`','CancelQueuedBuilding':'Param `id` ou `queue_id` ; échecs en `error:<clé>` ; file = `buildingtype` + `duration`','AnswerPlanetDecision':'`decision` = **`decision_key`** (chaîne), pas l\'id ; `choice` yes/no ; erreurs i18n','GetPlanetDecisions':'10 décisions / planète / heure, `nextRefreshAt` → compte à rebours ; titres via `decision_*` / `decisionDesc_*`','SpeedupBuilding':'Coût Nova affiché avant (gratuit ≤ 60 s, sinon max(10, ⌈6·min^0.82⌉))','DowngradeBuilding':'Immédiat, sans remboursement : confirmation en deux temps','RenamePlanet':'Serveur : 1–32 chars, lettres/chiffres/espaces/-_. ; `error:invalidPlanetName`','CheckBuildingQueue':'`percent` basé sur `workingStart` + `working` (fallback legacy)','CreateEmpire':'Compte sans empire → SAS ; miroir create-empire.php ; `GetMeEmpire` renvoie `error:noEmpire`','GetLeaderTraits':'Liste lore pour CreateEmpire','GetMeEmpire':'`error:noEmpire` si compte sans empire (flux CreateEmpire)','ImproveResearch':'Labo Science : cristal de la constellation → synthétiseur (ou bouton) ; `planet` = meilleur researchLab ; corps vide = lancé, JSON `{queued,targetLevel}` = en file ; prérequis `GetConfigs.researchs.requiert`','SpeedupResearch':'Écran du synthétiseur ; coût Nova affiché (gratuit ≤ 60 s) ; sans param','CancelQueuedResearch':'Param `id` (ligne empire_research_queue, tech en `research`) ; cristal arraché de son pad ou ×','RecruitTroop':'VR : Infantry×1 codé en dur','BuildDefenseUnit':'VR : MissileTurret×1 codé en dur','AddShip':'Onglet Chantier de la cale : catalogue par famille (shipstats), prérequis `requiert`, Construire / + File ; réponse texte ou JSON `queued`','SpeedupShipyard':'Coût Nova affiché (gratuit ≤ 60 s)','CancelQueuedShip':'Param `id` (ligne planet_ship_queue)','AddFleetOrderStep':'Étape JSON : `targetId` (planète / astéroïde) ; `moveToSystem` avec **`x`,`y`** (le serveur accepte aussi `targetX`/`targetY`)','RemoveFleetOrderStep':'`stepIndex` 0-based, repeater Helm','ToggleFleetQueueLoop':'`loop` explicite 0/1','MakeBattle':'fleets = mon vaisseau + cibles, `planetid` seulement si ≠ 0 (0 vs pirates), puis `UpdateBattle` (web startTacticalBattle)','Colonize':'`ship` = id du module `colonyShip` (legacy `ColonyShip` OK) ; planète libre, habitabilité ≥ 6','MoveFleetToSystem':'Toujours `hyperspace` explicite (0 sous-lumière / 1 hyperespace) après devis au pupitre (`TravelPlanner`, formules serveur) ; `ok:sublight_*` → `ApiResult.NoticeKey`','PrlBondFleetToSystem':'Devis portée / coût / recharge au pupitre (`TravelPlanner`, distance sur `visual_x/visual_y` comme le serveur), `fleet` + `system` + `pos` ; lâcher sur une étoile de la galaxie','GetSystems':'Galaxie complète sur la table (LOD, territoires par détenteur)','MoveFleetToPlanet':'idem hyperspace','MoveFleetToAsteroid':'idem hyperspace','GetChat':'VR affiche du JSON brut','GetMails':'VR affiche du JSON brut','LoginToken':'Token minté sur le casque, lié à l\'IP','GetDailyObjectives':'Web utilise `GetProgressionObjectives`'}
LEG={'auth':'Auth','meta':'Méta / boot','camera':'Caméra (vue)','galaxy':'Galaxie','fleet':'Flotte','ship':'Vaisseau / chantier','planet':'Planète / bâtiments / recherche','combat':'Combat','stargate':'Stargate','jumpgate':'Jumpgate','social':'Social (chat, mail)','alliance':'Alliance','war':'Guerre','empire':'Empire / progression / shop'}
order=['auth','meta','camera','galaxy','fleet','ship','planet','combat','jumpgate','stargate','social','war','alliance','empire']
rows={c:[] for c in order}; used_total=0; web_total=0
for name,v in sorted(A.items()):
    c=v['category']; req=[p['name'] for p in v.get('params',[])]; opt=[p['name']+'?' for p in v.get('optional_params',[])]
    vr=find(name,vrf,f'{VR}/Assets/_Core/Scripts'); web=find(name,webf,WEB)
    used_total+=bool(vr); web_total+=bool(web)
    rw='R' if re.match(r'^(Get|Check|Search|change)',name) else 'W'
    status='Branché' if vr else 'À faire'
    if name in ('GetAllFleetsAround','ProcessFleetOrderQueue','DoTurnBattle','GetBattle'): status='Hors scope'
    if vr and name in ('RecruitTroop','BuildDefenseUnit','GetChat','GetMails','GetShopData','GetDailyObjectives','GetKnownAddresses','GetJumpgateDestinations','CheckShipQueue','GetMyWars'): status='Démo'
    rows[c].append((name,rw,', '.join(req+opt) or '—', ('`'+vr[0]+'`'+(f' +{len(vr)-1}' if len(vr)>1 else '')) if vr else '—', ('`'+web[0].replace('assets/js/src/','')+'`') if web else '—', OV_ST.get(name,STATION[c]), OV_PH.get(name,PHASE[c]) if status!='Hors scope' else '—', status, NOTES.get(name,'')))
extra=sorted(registered-set(A))
out=[]
w=out.append
w('# PARITY — Stellar Universe VR ↔ `actionjs.php`\n')
w(f'Généré depuis `action-api.json` ({len(A)} actions), `actionjs.php` et un grep des deux clients. Référence web : `/Users/thommy/Websites/stellar-universe`. Roadmap : [`ROADMAP.md`](ROADMAP.md).\n')
w('**Règle** : chaque feature livrée met à jour sa ligne. Avant de coder, lire l\'implémentation web (colonne *Web*) + `model/*.php`. On adapte la jouabilité au pont VR ; le contrat serveur reste strict. La VR ne renvoie **jamais** au web.\n')
w('## Légende\n')
w('- **Statut** : `Branché` (appel fonctionnel en VR, UX à finir) · `Démo` (appelé mais valeurs en dur / réponse ignorée ou brute) · `À faire` · `Hors scope` (legacy / serveur only / interdit par AGENTS).')
w('- **Station** : où la feature vit à bord. Helm, Tactical, Engineering, Science, Comms, Ops = stations du pont ; *Conseil* = salle du conseil / bureau du captain (admin empire, progression, shop) ; *Sas* = scène Menu ; *Holo table* = carte.')
w('- **Phase** : voir [`ROADMAP.md`](ROADMAP.md) §3.')
w('- **R/W** : lecture / écriture (heuristique sur le nom). `?` = param optionnel.\n')
w('## Couverture\n')
w('| Domaine | Appelées en VR | Total | % |'); w('|---|---|---|---|')
for c in order:
    n=len(rows[c]); u=sum(1 for r in rows[c] if r[3]!='—'); w(f'| {LEG[c]} | {u} | {n} | {round(100*u/n)} % |')
w(f'| **Total** | **{used_total}** | **{len(A)}** | **{round(100*used_total/len(A))} %** |\n')
w(f'Appelées par le client web : {web_total}/{len(A)}. « Appelée » ≠ « finie » : voir la colonne *Statut*.\n')
for c in order:
    w(f'## {LEG[c]}\n'); w('| Action | R/W | Params | VR | Web | Station | Phase | Statut | Notes |'); w('|---|---|---|---|---|---|---|---|---|')
    for r in rows[c]: w('| `'+r[0]+'` | '+' | '.join(r[1:])+' |')
    w('')
# Hand-written tail (server gaps + CreateEmpire spec) is kept from the current file: edit it in PARITY.md.
import os
_prev=open(f'{VR}/docs/PARITY.md').read() if os.path.exists(f'{VR}/docs/PARITY.md') else ''
_i=_prev.find('## Écarts serveur à traiter côté web')
if _i>=0:
    out.append(_prev[_i:].rstrip('\n')+'\n')
else:
    w('## Écarts serveur à traiter côté web\n')
open(f'{VR}/docs/PARITY.md','w').write('\n'.join(out))
print(used_total,len(A),web_total,extra)
