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
| **Saisir / poser** (token de flotte, module, objet) | saisir à la main / gâchette, relâcher sur la cible | **touche de saisie** sur l’objet visé → il suit le réticule → **clic gauche** pour poser sur la cible, **clic droit / Échap** pour annuler | **tap** sur l’objet → il suit le doigt / le centre → **tap** sur la cible pour poser, bouton *Annuler* |
| Table holo : zoom / galaxie ↔ système | pincer à deux mains | molette | pincer (au fauteuil) |
| Table holo : tourner / déplacer | deux mains | clic-glisser | deux doigts (au fauteuil) |
| Texte | clavier holo (`HoloKeyboard`) | clavier physique | clavier natif |
| Menu rapide | bouton menu gauche | Échap (debout) | bouton *Menu* |
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
| Sas : connexion, inscription, plaque Communauté | ✅ | ✅ (curseur libre au sas, Entrée / Tab) | ✅ (tap, clavier natif) | `5214e67` : un seul EventSystem |
| Marche dans les pièces, portes | ✅ | ✅ | ✅ | portes par proximité + panneau |
| Écrans holo (stations, salles) | ✅ | ✅ (curseur libre ou réticule, `f22d5a0`) | ✅ | |
| Boutons 3D (`PokeButton`), objets (`XRSimpleInteractable`) | ✅ | ✅ | ✅ | portée 9 m |
| Bourse : caisses, berceau, pad | ✅ | ✅ | ✅ | |
| Fauteuil, raccourcis de postes | ✅ | ✅ | ✅ | |
| Table holo : saisir / déposer un token, files, outils | ✅ | en cours | en cours | saisie écran plat (§3.3) |
| Plateau hex (combat) | ✅ | en cours | en cours | |
| Écran principal (gestes) | ✅ | en cours | en cours | |
| Chantier : glisser les modules sur la grille 9×9 | ✅ | en cours | en cours | saisie écran plat (§3.3) |
| Labo, orrery, porte des étoiles | ✅ | en cours | en cours | |
| Poignet / consoles de bras | ✅ | en cours | en cours | manettes masquées sur écran plat |

« En cours » = revue des trois entrées en cours (ROADMAP, phase PX). Une ligne passe à ✅ quand l’interaction est faite **et** vérifiée (Editor pour le PC ; appareil pour le mobile et le casque — sinon le noter).
