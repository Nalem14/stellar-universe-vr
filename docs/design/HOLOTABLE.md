# Table tactique v2 — conception

> Statut : **validé** (2026-09-25) — flottant, viser→viser principal, la table vient au capitaine assis. **H1 livré** (Editor). **H2a livré** : saisir / tourner / mettre à l'échelle, boutons Recentrer et Galaxie ⟷ Système. **H2b livré** : galaxie en volume (étoiles flottantes en billboard), territoires façon Stellaris aux couleurs des drapeaux (remplissage + bordures en rideaux), emblèmes drapeau + nom, noyau galactique, faisceau « vous êtes ici », viser→viser sur n'importe quelle étoile, transition de dépliage. **H3a livré** : le combat prend la table et la rend, viser→viser sur le plateau, tirs joués sur la table, dehors et à bord (voir la [ROADMAP](../ROADMAP.md) P5.5). Remplace la holomap actuelle (P5.5 de la [ROADMAP](../ROADMAP.md)).
> Référence : **BattleGroup VR**. Une vraie scène 3D en miniature sous les yeux. On vise un vaisseau, puis on vise où il doit aller : l'ordre est donné. On attrape la scène pour la tourner ou la grossir, et l'état se lit sur les modèles eux-mêmes.

## 1. Pourquoi on refait

Retour joueur : la table est illisible, peu intuitive, et en dessous des pièces (cale sèche, labo). L'audit du code confirme que le problème vient de la conception, pas du polish :

| Problème | Cause |
|---|---|
| Visuel plat et confus | Système fait de quads empilés à quelques mm (plaque, rose, anneaux, halos, stub galaxie) : z-fighting, surcharge additive, rien n'a de volume |
| On ne sait pas quoi faire | Seul geste : saisir un jeton et le lâcher près d'une cible. Aucune cible surlignée ; rayon de lâcher de 0,7 m (tout « accroche ») ; pupitre fixe loin du point de lâcher |
| Modes cachés | La galaxie ne s'ouvre qu'en sur-dézoomant ; aucun bouton, aucun indice |
| Jetons qui se volent le rayon | Sphères de saisie de 0,32 m sur un disque de 0,95 m |
| Étiquettes partout | « Nom #id » sur tout, préfixes, readout écrasé de toutes parts |
| Combat hexagonal | Posé sur la carte vivante et jamais refermé (le mode ne se quitte plus) |
| Gestes en conflit | Zoom à deux mains sur le grip brut contre la saisie XRI ; stick droit pris au zoom |

## 2. Principes

1. **Un diorama, pas un plan.** Le système flotte au-dessus de la table dans un cône de projection :
   - étoile en petite sphère lumineuse avec couronne ;
   - planètes en sphères éclairées (même shader que les hublots, en miniature) sur des orbites fines ;
   - ceintures d'astéroïdes en particules de roche ;
   - vaisseaux en **mini-coques** holo (silhouette dérivée de la grille 9×9, un mesh par flotte, matériau partagé) ;
   - anomalies en gyroscope.
2. **Viser → viser.** Premier geste, gâchette sur un de nos vaisseaux : il est sélectionné, un anneau et sa fiche compacte apparaissent (vitesse, cargo, état). Second geste, gâchette sur une destination : l'ordre part.
   - Poke à portée de main, rayon au-delà.
   - La saisie-glisser reste possible, mais ce n'est plus le geste principal.
3. **Ce qui est possible se voit.** Dès qu'un vaisseau est sélectionné :
   - les cibles valides s'allument (planète, astéroïde, anomalie, étoile en galaxie) et les autres s'éteignent ;
   - survoler une cible trace l'**arc de trajet fantôme** avec l'ETA et le coût écrits dessus.
4. **La confirmation naît sur la cible.** Plus de pupitre fixe. Une petite plaque s'ouvre à côté de la destination, avec :
   - les modes : sous-lumière, hyperespace, bond PRL, chacun avec son devis ;
   - « Ajouter à la file » ;
   - Confirmer / Annuler.
   - Ordre en un geste si l'option est unique (réglage).
5. **On manipule la scène à la main.**
   - Un grip dans le vide : on saisit le diorama pour le tourner ou le déplacer.
   - Deux grips : on le met à l'échelle.
   - Un bouton physique « Système ⟷ Galaxie » sur le rebord de la table, en plus du geste de dézoom.
   - Transition animée : le système se replie dans son étoile, qui prend place dans la galaxie, et inversement.
6. **Lisible par défaut.**
   - Étiquettes seulement sur nos vaisseaux (nom court), sur la cible survolée et sur la sélection. Aucun « #id ».
   - Une seule bande d'état sur le rebord, qui dit ce qui se passe : « Sélectionné : X — visez une destination ».
   - Couleur = diplomatie ; forme = nature.
7. **Galaxie claire.**
   - Étoiles lumineuses, territoires teintés, nos vaisseaux en flèches.
   - Un faisceau « vous êtes ici » sur le système habité.
   - La galaxie se tourne et se grossit comme le système.
8. **Le combat prend la table.** Quand une bataille commence, le diorama du système s'efface et le plateau de combat se déploie. En fin de bataille, il se replie et le système revient.

## 3. Budget Quest

- Diorama du système : environ 30 objets.
  - Planètes : un mesh sphère partagé et un matériau par type de planète.
  - Coques holo : un mesh combiné par flotte, un matériau holo partagé.
  - Ceinture d'astéroïdes : un seul système de particules.
- Étiquettes en TMP 3D, pas de Canvas par jeton ; au plus environ 12 étiquettes visibles.
- Galaxie : garder le mesh unique d'étoiles actuel (déjà efficace).
- Aucun rebuild complet au poll : on met à jour les diffs, comme aujourd'hui pour les flottes.

## 4. Découpage

| Étape | Contenu | Sortie |
|---|---|---|
| **H1 — Diorama + viser→viser** | Système en 3D (étoile, planètes, orbites, ceintures, mini-coques, anomalies) ; sélection, cibles surlignées, arc fantôme avec ETA, plaque de confirmation sur la cible (mêmes ordres serveur qu'aujourd'hui) ; bande d'état ; nettoyage des couches | Captures POV debout et assis, avant/après |
| **H2 — Navigation + galaxie** | Saisir / tourner / mettre à l'échelle ; bouton de niveau + transition ; galaxie refaite (faisceau « vous êtes ici », territoires, flèches) ; ordres en galaxie | Captures + parcours complet d'un saut |
| **H3 — Files, combat, finition** | Files d'ordres en chemins 3D éditables ; combat qui prend la table et la rend ; sons, animations de déploiement | Parcours combat |

Contrat serveur inchangé : `MoveFleet*`, `PrlBondFleetToSystem`, files d'ordres, `ScanAnomaly`, `BattleDoAction`… Seule l'interface change.

## 5. À trancher

1. **Hologramme flottant au-dessus de la table** (recommandé : du volume, visible assis comme debout) **ou posé à plat sur le plateau** ?
2. **Viser→viser en geste principal**, la saisie-glisser en second (recommandé) ?
3. **Capitaine assis** : la table vient à lui (le diorama glisse vers le fauteuil et grossit) ou il agit au rayon ?
