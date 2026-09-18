# Échelles spatiales — Stellar Universe VR

Source de vérité runtime : `Core.Vfx.WorldScale`.  
Toute nouvelle géométrie **système / vaisseau / planète** lit ces constantes. Ne pas inventer un mètre « qui a l’air bien » dans un script isolé.

Le joueur ne voyage pas dans la galaxie. Il **habite un CIC**. Ce qu’il voit par les hublots doit rester lisible, cohérent, et à une taille de vaisseau / monde — pas des billes ni des blobs blancs.

---

## 1. Trois emboîtements (jamais mélangés)

| Couche | Où | Unité | Interdit |
|---|---|---|---|
| **Pièce** | Intérieur CIC (pont, sas menu, boot) | **1:1 VR** (mètres réels, room-scale) | Scaler le joueur, le deck, ou la table pour « coller » à l’espace |
| **Système** | Devant les **hublots** (`SystemExterior`) | Stylisé, mais **relatif** : vaisseau ≪ planète ≪ étoile | `Instantiate` 1:1 du système solaire réel |
| **Galaxie** | **Table holo** seulement | Tokens, pas des meshes monde | Galaxie au sol, caméra RTS, minimap collée au visage |

Le CIC **n’est pas** un mesh qui doit rentrer dans la coque 3D. C’est une convention POV (Elite / BattleGroup) : l’intérieur est toujours une pièce jouable ; l’extérieur d’une flotte est sa **grille 9×9**.

Ta propre coque est **cachée** tant que tu es à bord (`BridgeViewRig`) — sinon tu verrais le plafond de la hull depuis le pont.

---

## 2. Pièce (verrouillée)

| Constante | Valeur | Rôle |
|---|---|---|
| `CicDeck` | 12 m | Pont Bridge, locomotion room-scale |
| `CicCeiling` | 3.1 m | Hauteur sous barrot |
| Table holo | ~0.88 m du sol, Ø ~2.4 m | À portée de main, poitrine |
| Hublots | 1.8 × 1.1 m, lisse 1.65 m | Trois ouvertures **réelles** (pas un diorama collé) |

Menu / Boot gardent leurs propres pièces plus petites. Ne pas y coller le système 1:1.

---

## 3. Vaisseau (grille 9×9)

API : `fleets` = un vaisseau assemblé. Modules = `ships[]` (`type`, `grid_x`, `grid_y`). Cœur auto en **(4,4)**. `GetShipLayout` si la poll locale n’a pas la grille (flottes **à toi** seulement).

| Constante | Valeur | Effet |
|---|---|---|
| `ShipGrid` | 9 | Indices 0–8 |
| `ShipCoreCell` | 4 | Centre |
| `ShipCell` | 2.2 m | 1 module = un vrai morceau de coque |
| Coque pleine | 9 × 2.2 = **19.8 m** | Capitals lisibles par les hublots |
| 3–5 modules | ~7–11 m | Corvette / destroyer, plus petit que le pont |

Orientation runtime : **+Z = nez** (armes), **−Z = poupe** (moteurs). `LookRotation` du transit pointe le nez vers `dest`. Heuristique : si les armes sont à `gy` bas (UI web, ligne 0 = avant), on inverse Z.

Sans grille : layout synthétique **stable** (seed = `fleet.id`) — jamais une sphère.

---

## 4. Système (hublots)

Stylisé type Homeworld / BattleGroup : on lit « soleil, mondes, flottes », pas une simu N-body.

| Constante | Valeur | Pourquoi |
|---|---|---|
| `StarRadius` | 36 m | Astre dominant, ne remplit pas tout le hublot depuis l’orbite 1 |
| `PlanetRadiusMin` + `Step` | 14 m + 1.6 m/slot | Mondes **plus gros** que n’importe quelle coque (~20 m) |
| `AsteroidRadius` | 3.6 m | Rocher, plus petit qu’un vaisseau |
| `OrbitBase` | 120 m | Première orbite hors du soleil + marge |
| `OrbitStep` | 58 m | Planètes séparées ; un transit se **voit** |
| `EclipticHeight` | 8 m | Pont un peu au-dessus du plan (hublots = panorama, pas le sol) |
| `BridgeFarClip` | 1100 m | Slot 10 ≈ 640 m |
| `BridgeFogDensity` | 0.0032 | Profondeur sans manger les orbites (l’ancien 0.02 tuait tout à 50 m) |

Parking flotte autour d’un corps :

```
distance = rayon_corps + CicDeck/2 + 10 m
```

Le cube CIC (12 m) ne rentre pas dans la planète. Les flottes d’une même orbite s’écartent en tangente (`FleetLateral` + pas), plus larges qu’une coque 20 m.

Mouvement (`from` / `dest` / `desttime`) : interpolation **dans ce monde**. Un jump inter-système vise le bord (`OrbitBase + OrbitStep * 8`), pas un lerp de 4 m.

---

## 5. Ce que le joueur doit ressentir

Depuis le pont, nez vers l’étoile :

1. **Soleil** — gros disque, jamais un plafond blanc (émission maîtrisée, corona petite).
2. **Ta planète** (si en orbite) — globe sur le flanc / un peu bas, clairement plus large que le hublot proche.
3. **Autres flottes** — silhouettes **modulaires** (cœur, armes, moteurs), accent cyan (à toi) / rouge-ambre (autres), pas des orbes.
4. **Autres planètes** — globes distincts sur leurs orbites, pas collés au soleil.

Si un nouvel objet « paraît trop petit / trop gros », comparer à cette pile : **module 2.2 m < vaisseau ≤ 20 m < planète 28–56 m Ø < étoile 72 m Ø < orbite 120 m+**.

---

## 6. Table holo (`Holo*`)

La galaxie et le système **jouables** sont des maquettes sur la table (~1 m). Ne **jamais** réutiliser `WorldScale.OrbitBase` / rayons monde pour un token de table.

| Constante | Valeur | Rôle |
|---|---|---|
| `HoloDiscRadius` | 0.95 m | Disque jouable sur le plateau Ø 2.4 m |
| `HoloOrbitBase` / `HoloOrbitStep` | 0.18 / 0.085 m | Orbites `planet.slot` sur la carte |
| `HoloStarRadius` | 0.055 m | Étoile token |
| `HoloPlanetRadius` (+ step) | 0.028 + 0.0035 m/slot | Mondes distincts |
| `HoloAsteroidRadius` | 0.012 m | Pip rocher |
| `HoloFleetSize` | 0.032 m | Chevron flotte (pas une sphère) |
| `HoloVolumeHeight` / `HoloTokenLift` | 0.42 / 0.06 m | Colonne projetée + lift tokens |

`ViewportSystemView` (diorama dans le verre) est un reliquat : les hublots Bridge sont des **trous**. Ne pas y recoller un mini-système.

---

## 7. Checklist agent

- [ ] Nouvelle mesh monde → `WorldScale`, pas un literal.
- [ ] Vaisseau → grille 9×9, `ShipHullBuilder`, matériaux métal + émissif. Pas de primitive seule « placeholder ».
- [ ] Planète / étoile plus petits qu’un vaisseau ? Non.
- [ ] Fog / far clip Bridge → `WorldScale.Bridge*`.
- [ ] Locomotion joueur = pièce seulement. Les flottes bougent ; le CIC suit **sa** flotte (`BridgeViewRig`).
