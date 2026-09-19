# CIC Holo UI texture pack

World Space diegetic UI sprites for Stellar Universe VR (Bridge Crew–style glass).

Path: `Assets/_Core/Resources/CIC/Ui/`

| Asset | Use |
|-------|-----|
| Panel / PanelDark | Frame backgrounds (9-slice) |
| Header | Context bar |
| BtnCyan* / BtnAmber* / BtnDanger* / BtnGhost* | Buttons idle/hover/pressed/disabled |
| TabIdle / TabActive | Mode tabs |
| Field | TMP input chrome |
| Readout | Status / amber strip |
| Divider | Hairline separator |
| Dot / DotEmpty | Selection indicators |
| RingButton | Circular primary action |
| GlowSoft | Soft bloom under panels |
| CheckOff / CheckOn | Toggles |

Import (9-slice borders): **StellarUniverse → UI → Import Holo UI Sprites**

Runtime load via `DiegeticUi` (`Resources.Load<Sprite>("CIC/Ui/…")`).
