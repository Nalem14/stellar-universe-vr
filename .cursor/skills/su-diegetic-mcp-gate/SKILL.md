---
name: su-diegetic-mcp-gate
description: >-
  Beauty-gate diegetic CIC UI for Stellar Universe VR via Unity MCP: Play mode,
  capture Game view PNGs, Read images, fix until pass. Use after Menu, Bridge,
  or Holomap UI changes — never declare OK without reading screenshots.
---

# SU Diegetic MCP Gate

AGENTS.md bar: diegetic CIC only (World Space / mesh + XRI). No Screen Space Overlay product UI. No naked grey cube buttons.

## Procedure (per jalon: Menu | Bridge | Holomap)

1. Confirm Editor ready (`editor_status` / `unity status`).
2. `open_scene` the target scene (Menu or Bridge).
3. `editor_play` — wait until ready.
4. `capture_game_view` → `Assets/Screenshots/verify-<jalon>-<n>.png` (several angles if needed).
5. **Read** each PNG with the image Read tool — do not skip.
6. Fail if any of:
   - Naked grey / default magenta materials as buttons
   - Mirrored / unreadable TMP
   - Empty plates with no label
   - Forms shown when Hub should be (token session)
   - Duplicate order boards (Helm + Captain rail both MoveFleet)
   - Holomap tokens too small to grab / no hover readout
7. Fix code → recompile → re-shot → re-Read until pass.
8. `editor_stop` when done.

## Kit contract

All new pokeables go through `Core.Vfx.DiegeticUi` (`Panel` / `Button` / `Tab` / `Row` / `Plate` / `Readout` / `Label`) + `CicCue`. Kill `AddPoke` naked cubes.

## Skills companions

- `.agents/skills/unity-cli` — drive Editor
- `.agents/skills/optimize-text-mesh-pro` — TMP sizeDelta / atlas
- `.agents/skills/audio-setup-mixers` — route CicCue if mixers exist
