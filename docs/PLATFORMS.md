# Plateformes et entrées — casque · PC · mobile

Un seul jeu, trois corps. Ce document est le **contrat d’entrées** : toute interaction du jeu doit marcher dans les trois colonnes ci-dessous avant d’être livrée (règle AGENTS.md, « Plateformes et entrées »).

## 1. Détection

`Core.App.PcPlatformBoot` décide au démarrage (avant la première scène) :

| Ordre | Condition | Mode |
|---|---|---|
| 1 | argument `-mobile` / `-touch` | Mobile |
| 2 | argument `-desktop` / `-novr` | Desktop |
| 3 | argument `-vr` | VR |
| 4 | un loader XR est actif | VR |
| 5 | appareil Android Quest / Oculus / Meta | VR |
| 6 | plateforme mobile (`Application.isMobilePlatform`) | Mobile |
| 7 | sinon | Desktop |

OpenXR ne démarre **jamais tout seul** (« Initialize XR on Startup » coupé sur Android et Standalone) : `Core.App.XrStartup` l’allume sur un Quest ou avec `-vr`, aux mêmes moments que XR Management. Un APK unique sert le Quest et les téléphones : sur un téléphone, OpenXR démarré sans runtime laissait l’écran noir (musique seule). `-vr` sans runtime qui répond retombe sur l’écran plat.

`IsVr`, `IsDesktop`, `IsMobile`, `IsFlatScreen` (= Desktop ou Mobile). Le Play Mode de l’Editor sans loader XR est en **Desktop** : c’est le chemin PC réel.

Sur écran plat, `SetupDesktopRig` réutilise le rig XR de la scène : capsule `CharacterController`, caméra à `WorldScale.EyeStanding`, suivi de tête coupé, manettes et mains masquées, puis le contrôleur de la plateforme et son HUD.

## 2. Commandes

| Action | Casque | PC (clavier-souris) | Mobile (tactile) |
|---|---|---|---|
| Se déplacer | room-scale, téléport court dans la pièce | ZQSD / WASD / flèches, Maj = courir | joystick (moitié gauche de l’écran), à fond = courir |
| Regarder | la tête ; snap-turn 45° | souris (curseur capturé) ; **Tab** libère / recapture le curseur | glisser sur la moitié droite |
| Viser | rayon de la manette | réticule central (curseur capturé) ou curseur libre | le doigt |
| Interagir (bouton 3D, objet, porte, fauteuil) | poke / gâchette | **E** ou **clic gauche** | tap |
| Écrans holo (boutons, champs, listes) | rayon / poke | réticule + E / clic (vue FPS), ou curseur libre + clic | tap |
| **Saisir / poser** (vaisseau sur la table, point de route, module du chantier, échantillon et cristaux du labo) | saisir à la main / gâchette, relâcher sur la cible | **E** sur l’objet visé (« [E] Saisir … ») → il suit le réticule → **clic gauche** (ou E) pour poser sur la cible, **clic droit / Échap** pour le reposer | **tap** sur l’objet → il suit le centre de l’écran → **tap** sur la cible pour poser, bouton *Annuler* |
| Table holo : choisir un vaisseau, une cible, un point de route | gâchette sur le token | clic gauche / E (survol au réticule) | tap |
| Table holo : zoom / galaxie ↔ système | pincer à deux mains | molette sur la table (autour du point visé) | pincer (au fauteuil) |
| Table holo : déplacer / tourner | deux mains | clic molette glissé (déplacer) ; clic droit glissé (tourner le système, déplacer la galaxie) | deux doigts : glisser, tourner (au fauteuil) |
| Plateau hex (combat) | gâchette / doigt sur une case | clic / E sur la case visée | tap sur la case |
| Écran principal | saisir l’image : glisser, écarter, double tape | molette (zoom), clic droit glissé (tourner autour), double clic (rend la main au réalisateur) | pincer, double tap |
| Bracelet (heure, alerte, affaires) | tourner le poignet | maintenir **I** | bouton *Bracelet* |
| Texte | clavier holo (`HoloKeyboard`) | clavier physique | clavier natif |
| Menu rapide | bouton menu gauche | Échap (debout) ; réglages souris (sensibilité, axe inversé) | bouton *Menu* |
| S’asseoir / se lever (poste de commandement) | s’asseoir / bouton | E sur le fauteuil ; Espace ou Échap pour se lever | tap sur le fauteuil ; bouton *Se lever* |
| Postes au fauteuil | se tourner vers le poste | F flottes · M carte · C comms · O opérations · T tactique | barre de boutons tactiles |

Tant qu’un champ de texte a le focus (`PcPlatformBoot.IsTyping`), aucune touche de jeu ne réagit (déplacement, E, Échap/M, raccourcis du fauteuil, saisie).

## 3. Règles pour coder une interaction

1. **Objet 3D** : un collider + `XRSimpleInteractable` (ou `PokeButton`). Les trois entrées déclenchent `hoverEntered` / `selectEntered` / `selectExited` (PC : `PcInteractionRaycaster` ; mobile : `MobileTouchController`).
2. **Ne jamais dépendre de `args.interactorObject`**, d’une pose de manette ou d’une main : sur écran plat l’événement n’en porte pas. Lire la position visée par le rayon de la plateforme (rayon manette / réticule / doigt).
3. **Saisir / glisser** : chaque geste « attraper puis déposer » a son chemin écran plat (§2, ligne *Saisir / poser*) : la saisie démarre sur la touche (PC) ou le tap (mobile), l’objet suit le rayon de visée, le dépôt se fait au clic / tap sur une cible valide, l’annulation au clic droit / Échap / bouton. Mêmes cibles valides, mêmes appels API qu’en casque.
4. **Écrans** : créer les Canvas avec `DiegeticUi.WorldCanvas` (caméra d’événements, raycasters selon la plateforme). Jamais de Canvas Screen Space pour une feature.
5. **Actions d’entrée** : pas de binding seulement `<XRController>` ; ajouter clavier / souris et un geste ou un bouton tactile.
6. **Texte** : `TMP_InputField` (attaché à `HoloKeyboard` en casque).
7. **EventSystem** : uniquement `PcPlatformBoot.EnsureSingleEventSystem()` (ou `DiegeticUi.EnsureEventSystem()`).
8. **Placer le joueur** : `XrPlacement.PlaceHead` ; les contrôleurs écran plat suivent l’orientation imposée.
9. **Textes d’aide** (invites, touches, boutons tactiles) : `Trans.Get` comme tout le reste (clés `vr.pc.*`, `vr.mobile.*`).

## 4. État par zone

| Zone | Casque | PC | Mobile | Notes |
|---|---|---|---|---|
| Sas : connexion, inscription, plaque Communauté | ✅ | ✅ | ✅ | un seul EventSystem ; curseur libre au sas ; Tab = champ suivant |
| Marche dans les pièces, portes | ✅ | ✅ | ✅ | capsule écran plat r 0.35 m ; locomotion XR coupée sur écran plat ; galerie de la porte des étoiles et galerie de la citadelle fermées |
| Écrans holo (stations, salles) | ✅ | ✅ | ✅ | réticule ou curseur libre ; un clic sur un écran ne traverse plus vers l’objet derrière |
| Boutons 3D, objets, fauteuil, crew | ✅ | ✅ | ✅ | portée 9 m ; sur mobile le bouton remonte après le tap |
| Bourse : caisses, berceau, pad | ✅ | ✅ | ✅ | |
| Table holo : choisir / ordonner (point → point) | ✅ | ✅ | ✅ | `TacticalCommand` + `FlatPointer` |
| Table holo : saisir un vaisseau / un point de route | ✅ | ✅ | ✅ | `FlatGrab` ; mobile : reposer un point sans cible ouvre son menu |
| Table holo : zoom, déplacer, tourner | ✅ | ✅ | ✅ | `PcHoloMapInput` / `MobileHoloMapInput` |
| Plateau hex (combat) | ✅ | ✅ | ✅ | case visée / tapée ; touches de console inchangées |
| Écran principal (gestes) | ✅ | ✅ | ✅ | molette / clic droit / double clic ; pincement |
| Chantier : modules sur la grille 9×9, recycleur, impression | ✅ | ✅ | ✅ | casque : grip ou gâchette sur un bloc (`TriggerGrab`) ; le stick droit continue de tourner le joueur pendant le port (pas de manipulation de l'objet tenu, `ComfortSettings`) ; écran plat : `FlatGrab` ; hologramme (module absent du hangar) : deux gâchettes / clics / taps = impression ; filtres du magasin = boutons poussoirs |
| Labo : échantillon, cristaux en file | ✅ | ✅ | ✅ | `FlatCarry` ; boutons d’écran toujours là |
| Orrery, porte des étoiles, quartiers, diplomatie | ✅ | ✅ | ✅ | objets simples + écrans |
| Bracelet | ✅ | ✅ (I) | ✅ (bouton) | posé en bas à gauche de la vue |
| Mode quart (passthrough) | ✅ | — | — | casque seulement (`WatchMode.Supported`) |

Vérifié dans l’Editor en mode PC (Play Mode réel, compte propriétaire) : connexion, pont, salles, saisie d’un vaisseau sur la table et annulation, sans exception. **Non vérifié** : un téléphone réel (tactile) et le Quest réel — à faire sur appareil.
