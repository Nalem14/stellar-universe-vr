using System.Collections.Generic;
using Core.App;
using Core.Stations;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// The aft bulkhead's two backlit wall displays, either side of the corridor door (a Star Trek "MSD"):
    /// to port, the ship we stand on — its 9×9 module plan in family colours, its hull and shield gauges (what it fights
    /// with, live during a battle), the sums of its modules, and the sheet of the module under the pointer (or, at a
    /// citadel, the city plan with the world's own hull and shield); to starboard, the system plot — star, orbits, worlds
    /// in their owners' colours, our ships and contacts. Each is a static texture redrawn only when the focus
    /// changes (≤ 1 per second), on one world canvas: nothing per frame.
    /// </summary>
    public sealed class BridgeWallDisplays : MonoBehaviour
    {
        const int PlotSize = 256;
        static readonly Vector2 PanelSize = new(2.3f, 1.12f);
        static readonly Color Ink = new(0.02f, 0.05f, 0.08f, 1f);

        FocusContext _focus;
        Texture2D _msdTex;
        Texture2D _plotTex;
        TMP_Text _msdTitle;
        TMP_Text _msdBody;
        TMP_Text _plotTitle;
        TMP_Text _plotBody;
        HoloScreen _msd;
        HoloScreen _plot;
        bool _dirty = true;
        float _next;
        EconomyService _eco;
        (Image Fill, TMP_Text Label) _hullBar;
        (Image Fill, TMP_Text Label) _shieldBar;
        /// <summary>The module under the pointer on the port plan (null = the summary).</summary>
        FocusShipModule _hovered;
        string _summary = string.Empty;
        float _gaugeNext;

        public static BridgeWallDisplays Build(CicEnvironment host, FocusContext focus)
        {
            var go = new GameObject("BridgeWallDisplays");
            go.transform.SetParent(host.transform, false);
            var d = go.AddComponent<BridgeWallDisplays>();
            d._focus = focus;
            var yaw = Quaternion.Euler(0f, BridgeShell.EdgeYaw(BridgeShell.AftWall) + 180f, 0f);
            var len = BridgeShell.EdgeLength(BridgeShell.AftWall);
            d._msd = Panel(go.transform, "ShipDisplay", BridgeShell.EdgePoint(BridgeShell.AftWall, len * 0.5f - BridgeShell.AftBayX, 0.07f, 1.72f), yaw);
            d._plot = Panel(go.transform, "SystemDisplay", BridgeShell.EdgePoint(BridgeShell.AftWall, len * 0.5f + BridgeShell.AftBayX, 0.07f, 1.72f), yaw);
            d._msdTex = NewTex("SU_ShipDisplay");
            d._plotTex = NewTex("SU_SystemDisplay");
            Layout(d._msd, d._msdTex, out d._msdTitle, out d._msdBody);
            Layout(d._plot, d._plotTex, out d._plotTitle, out d._plotBody);
            d.BuildGauges();
            d.BuildHover();
            if (focus != null)
            {
                focus.Changed += d.MarkDirty;
                focus.FleetsChanged += d.MarkDirty;
            }

            return d;
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= MarkDirty;
                _focus.FleetsChanged -= MarkDirty;
            }

            if (_eco != null)
                _eco.Changed -= MarkDirty;

            if (_msdTex != null)
                Destroy(_msdTex);
            if (_plotTex != null)
                Destroy(_plotTex);
        }

        void MarkDirty() => _dirty = true;

        Pose _msdShip;
        Pose _plotShip;
        bool _posed;

        /// <summary>
        /// On the aft bulkhead of a ship; on the station's curved wall over the two credenzas, either side of
        /// the door (port display to port); on the citadel's narthex wall between the portal and the guardians.
        /// </summary>
        public void SetLayout(ViewMode mode)
        {
            if (_msd == null || _plot == null)
                return;
            if (!_posed)
            {
                _msdShip = new Pose(_msd.transform.localPosition, _msd.transform.localRotation);
                _plotShip = new Pose(_plot.transform.localPosition, _plot.transform.localRotation);
                _posed = true;
            }

            Place(_msd.transform, _msdShip, mode);
            Place(_plot.transform, _plotShip, mode);
        }

        static void Place(Transform t, Pose ship, ViewMode mode)
        {
            if (mode == ViewMode.Ship)
            {
                t.SetLocalPositionAndRotation(ship.position, ship.rotation);
                return;
            }

            if (mode == ViewMode.City)
            {
                t.SetLocalPositionAndRotation(new Vector3(Mathf.Sign(ship.position.x) * 2.8f, ship.position.y + 0.2f, CitadelHall.NarthexZ + 0.14f), ship.rotation);
                return;
            }

            var deg = ship.position.x < 0f ? 200f : 160f;
            t.SetLocalPositionAndRotation(StationCommandShell.OnWall(deg, 0.16f, ship.position.y),
                Quaternion.Euler(0f, deg - 180f, 0f) * ship.rotation);
        }

        static HoloScreen Panel(Transform parent, string name, Vector3 pos, Quaternion rot)
        {
            // Screen front is its local −z: turn it to face into the room from the aft bulkhead.
            var screen = HoloScreen.Create(parent, name, PanelSize, pos, rot, " ");
            screen.SetAccent(CicArtKit.Cyan, 0.45f);
            return screen;
        }

        static Texture2D NewTex(string name) =>
            new(PlotSize, PlotSize, TextureFormat.RGBA32, false) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };

        static void Layout(HoloScreen screen, Texture2D tex, out TMP_Text title, out TMP_Text body)
        {
            var px = screen.PixelSize;
            var img = new GameObject("Plot", typeof(RectTransform), typeof(RawImage));
            img.transform.SetParent(screen.Content, false);
            var rt = img.GetComponent<RectTransform>();
            var side = px.y * 0.78f;
            rt.sizeDelta = new Vector2(side, side);
            rt.anchoredPosition = new Vector2(-px.x * 0.5f + side * 0.5f + 50f, -px.y * 0.04f);
            var raw = img.GetComponent<RawImage>();
            raw.texture = tex;
            raw.raycastTarget = false;
            var textX = -px.x * 0.5f + side + 100f;
            var textW = px.x * 0.5f - textX + px.x * 0.5f - 50f;
            title = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(textX + textW * 0.5f, px.y * 0.3f), new Vector2(textW, 90f), 62f,
                UiKit.TextBright, TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
            body = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(textX + textW * 0.5f, -px.y * 0.1f), new Vector2(textW, px.y * 0.6f), 42f,
                UiKit.TextDim, TextAlignmentOptions.TopLeft);
            body.enableWordWrapping = true;
            body.lineSpacing = 12f;
        }

        // ── Hull and shield gauges, the module sheet on hover ─────────────────────

        void BuildGauges()
        {
            var px = _msd.PixelSize;
            var side = px.y * 0.78f;
            var x = -px.x * 0.5f + side + 100f;
            var w = px.x * 0.5f - x - 60f;
            _hullBar = Gauge(_msd.Content, "Hull", new Vector2(x, px.y * 0.17f), w, new Color(1f, 0.69f, 0.29f, 0.9f));
            _shieldBar = Gauge(_msd.Content, "Shield", new Vector2(x, px.y * 0.08f), w, new Color(0.35f, 0.85f, 1f, 0.9f));
            // The summary starts under the gauges and keeps to their width (it ran past the frame).
            var rt = _msdBody.rectTransform;
            rt.anchoredPosition = new Vector2(x + w * 0.5f, -px.y * 0.17f);
            rt.sizeDelta = new Vector2(w, px.y * 0.44f);
        }

        static (Image, TMP_Text) Gauge(Transform parent, string name, Vector2 left, float width, Color fill)
        {
            var bg = new GameObject(name + "Gauge", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(parent, false);
            var rt = bg.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = left;
            rt.sizeDelta = new Vector2(width, 46f);
            var bgi = bg.GetComponent<Image>();
            bgi.color = new Color(0.05f, 0.12f, 0.16f, 0.9f);
            bgi.raycastTarget = false;
            var fg = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fg.transform.SetParent(bg.transform, false);
            var ft = fg.GetComponent<RectTransform>();
            ft.anchorMin = Vector2.zero;
            ft.anchorMax = Vector2.one;
            ft.offsetMin = new Vector2(3f, 3f);
            ft.offsetMax = new Vector2(-3f, -3f);
            var fi = fg.GetComponent<Image>();
            fi.color = fill;
            fi.raycastTarget = false;
            var label = DiegeticUi.HoloLabel(bg.transform, string.Empty, Vector2.zero, new Vector2(width - 20f, 44f), 32f,
                UiKit.TextBright, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            return (fi, label);
        }

        static void SetGauge((Image Fill, TMP_Text Label) g, string label, float cur, float max)
        {
            if (g.Fill == null)
                return;
            var k = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
            var ft = g.Fill.rectTransform;
            ft.anchorMax = new Vector2(Mathf.Max(0.001f, k), 1f);
            var text = label + "  " + ScreenKit.Num(Mathf.Max(0f, cur)) + " / " + ScreenKit.Num(Mathf.Max(0f, max));
            if (g.Label.text != text)
                g.Label.text = text;
        }

        /// <summary>
        /// Hull and shield now: a battle row of this ship (or this world) while it fights, else the full figures it
        /// would enter a fight with (hull = armor + structure per module, research included — the battle's own).
        /// </summary>
        void UpdateGauges()
        {
            var fleet = _focus?.FindViewFleet();
            float hull = 0f, hullMax = 0f, shield = 0f, shieldMax = 0f;
            var battle = HexBattleController.Instance != null ? HexBattleController.Instance.State : null;
            BattleShip row = null;
            if (battle != null)
                foreach (var b in battle.Ships)
                    if (fleet != null ? !b.IsPlanet && b.FleetId == fleet.Id : b.IsPlanet && b.PlanetId == _focus.RotundaPlanetId)
                        row = b;
            if (row != null)
            {
                hull = row.Hp;
                hullMax = row.MaxHp;
                shield = row.Shield;
                shieldMax = row.MaxShield;
            }
            else if (fleet != null)
            {
                hullMax = hull = fleet.HullTotal > 0f ? fleet.HullTotal : fleet.ArmorTotal;
                shieldMax = shield = fleet.ShieldTotal;
            }
            else if (EconomyService.Instance != null && EconomyService.Instance.TryGet(_focus.RotundaPlanetId, out var planet))
            {
                hullMax = hull = FocusContext.AsFloat(planet.Raw?["combatArmor"]);
                shieldMax = shield = FocusContext.AsFloat(planet.Raw?["combatShield"]);
            }

            var on = hullMax > 0f || shieldMax > 0f;
            _hullBar.Fill.transform.parent.gameObject.SetActive(on);
            _shieldBar.Fill.transform.parent.gameObject.SetActive(on);
            if (!on)
                return;
            SetGauge(_hullBar, Trans.Get("vr.battle.hull"), hull, hullMax);
            SetGauge(_shieldBar, Trans.Get("shield"), shield, shieldMax);
        }

        void BuildHover()
        {
            var plot = _msd.Content.Find("Plot");
            if (plot == null)
                return;
            plot.GetComponent<RawImage>().raycastTarget = true;
            var hover = plot.gameObject.AddComponent<PlotHover>();
            hover.Moved = OnPlotPointer;
        }

        /// <summary>Pointer over the port plan at <paramref name="uv"/> (0..1, origin bottom-left; null = left it).</summary>
        void OnPlotPointer(Vector2? uv)
        {
            FocusShipModule found = null;
            var fleet = _focus?.FindViewFleet();
            if (uv is { } p && fleet != null)
            {
                var gx = Mathf.Clamp(Mathf.FloorToInt(p.x * 9f), 0, 8);
                var gy = 8 - Mathf.Clamp(Mathf.FloorToInt(p.y * 9f), 0, 8);
                foreach (var m in fleet.Modules)
                    if (m.OnGrid && m.GridX == gx && m.GridY == gy)
                        found = m;
            }

            if (found == _hovered)
                return;
            _hovered = found;
            ShowBody();
        }

        void ShowBody()
        {
            if (_hovered == null)
            {
                Set(_msdBody, _summary);
                return;
            }

            var type = _hovered.Type;
            var fam = ModuleCatalog.Family(type);
            var stats = ModuleCatalog.StatsLine(type);
            var sb = new System.Text.StringBuilder();
            sb.Append("<b><color=#").Append(ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(fam))).Append('>')
                .Append(Trans.Get(ModuleCatalog.NameKey(type))).Append("</color></b>  <size=80%>")
                .Append(Trans.Get(ModuleCatalog.FamilyKey(fam))).Append("</size>\n");
            if (stats.Length > 0)
                sb.Append(stats).Append('\n');
            sb.Append("<size=78%>").Append(ModuleCatalog.Description(type)).Append("</size>");
            Set(_msdBody, sb.ToString());
        }

        /// <summary>Pointer move / exit / tap on the plan image, as a UV on it (one path for ray, mouse and touch).</summary>
        sealed class PlotHover : MonoBehaviour, IPointerMoveHandler, IPointerExitHandler, IPointerClickHandler
        {
            public System.Action<Vector2?> Moved;

            public void OnPointerMove(PointerEventData e) => Report(e);
            public void OnPointerClick(PointerEventData e) => Report(e);
            public void OnPointerExit(PointerEventData e) => Moved?.Invoke(null);

            void Report(PointerEventData e)
            {
                var rt = (RectTransform)transform;
                var cam = e.pressEventCamera != null ? e.pressEventCamera : e.enterEventCamera;
                var world = e.pointerCurrentRaycast.worldPosition;
                Vector2 local;
                if (world != Vector3.zero)
                    local = rt.InverseTransformPoint(world);
                else if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, cam, out local))
                    return;
                var r = rt.rect;
                Moved?.Invoke(new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height));
            }
        }

        void Update()
        {
            // A level finished or started below: the city plan follows (the service exists once the bridge has booted).
            if (_eco == null && EconomyService.Instance != null)
            {
                _eco = EconomyService.Instance;
                _eco.Changed += MarkDirty;
            }

            // In a fight the gauges follow the battle board (its state is diffed every 2.5 s): twice a second.
            if (_focus != null && HexBattleController.Instance != null && HexBattleController.Instance.State != null &&
                Time.unscaledTime >= _gaugeNext)
            {
                _gaugeNext = Time.unscaledTime + 0.5f;
                UpdateGauges();
            }

            if (!_dirty || Time.unscaledTime < _next || _focus == null)
                return;
            _dirty = false;
            _next = Time.unscaledTime + 1f;
            var accent = _focus.Mode switch
            {
                ViewMode.City => CityExterior.CitadelGold,
                ViewMode.Station => CicArtKit.Amber,
                _ => CicArtKit.Cyan
            };
            _msd.SetAccent(accent, 0.45f);
            _plot.SetAccent(accent, 0.45f);
            DrawShip(accent);
            DrawSystem(accent);
            UpdateGauges();
        }

        // ── Port: the ship (or the station) ──────────────────────────────────────

        void DrawShip(Color accent)
        {
            var px = Clear(_msdTex);
            var fleet = _focus.FindViewFleet();
            if (fleet == null)
            {
                DrawCity(px, accent);
                return;
            }

            const float cell = PlotSize / 9f;
            var mods = fleet.Modules;
            var any = false;
            foreach (var m in mods)
            {
                if (!m.OnGrid)
                    continue;
                any = true;
                var col = ModuleCatalog.Accent(ModuleCatalog.Family(m.Type));
                var x0 = m.GridX * cell + 3f;
                var y0 = (8 - m.GridY) * cell + 3f;
                Rect(px, x0, y0, cell - 6f, cell - 6f, col * 0.55f + Ink);
                Rect(px, x0, y0, cell - 6f, 3f, col);
                Rect(px, x0, y0 + cell - 9f, cell - 6f, 3f, col);
                Rect(px, x0, y0, 3f, cell - 6f, col);
                Rect(px, x0 + cell - 9f, y0, 3f, cell - 6f, col);
            }

            // The grid itself, faint, so an empty or loading layout still reads as the 9×9 hull.
            for (var i = 0; i <= 9; i++)
            {
                Rect(px, i * cell - 0.5f, 0f, 1f, PlotSize, new Color(accent.r, accent.g, accent.b, 1f) * 0.18f + Ink);
                Rect(px, 0f, i * cell - 0.5f, PlotSize, 1f, new Color(accent.r, accent.g, accent.b, 1f) * 0.18f + Ink);
            }

            Apply(_msdTex, px);
            Set(_msdTitle, "<noparse>" + (string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name) + "</noparse>");
            _msdTitle.color = accent;
            if (!any)
            {
                Set(_msdBody, Trans.Get("Loading"));
                return;
            }

            var s = ModuleCatalog.Sum(mods);
            var families = new SortedDictionary<ModuleFamily, int>();
            foreach (var m in mods)
            {
                var f = ModuleCatalog.Family(m.Type);
                families[f] = families.TryGetValue(f, out var n) ? n + 1 : 1;
            }

            var sb = new System.Text.StringBuilder();
            // Hull and shield are the gauges above; then what it hits with, how fast, how much it carries.
            sb.Append(Trans.Get("damage")).Append("  ").Append(Mathf.RoundToInt(s.Damage)).Append("    ")
                .Append(Trans.Get("speed")).Append("  ").Append(Mathf.RoundToInt(s.Speed)).AppendLine();
            sb.Append(Trans.Get("cargo")).Append("  ").Append(Mathf.RoundToInt(s.Cargo)).AppendLine().AppendLine();
            foreach (var kv in families)
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(kv.Key))).Append("><b>—</b></color> ")
                    .Append(Trans.Get(ModuleCatalog.FamilyKey(kv.Key))).Append(" ").Append(kv.Value).Append("   ");
            _summary = sb.ToString();
            ShowBody();
        }

        // ── Port, at a citadel: the city plan ────────────────────────────────────

        static readonly List<(BuildingDef Def, int Level)> Built = new();

        /// <summary>
        /// The city we stand over, drawn from its real buildings (EconomyService): the citadel at the heart, then one
        /// district per building built — a sector between boulevards, as deep as its level (log scale) and filled with
        /// city blocks in the building's colour, denser the higher the level. Beside it, every building and its level.
        /// </summary>
        void DrawCity(Color32[] px, Color accent)
        {
            var planetId = _focus.RotundaPlanetId;
            Built.Clear();
            var eco = EconomyService.Instance;
            if (eco != null && planetId > 0 && eco.TryGet(planetId, out var planet))
                foreach (var def in BuildingCatalog.All)
                {
                    var level = planet.Level(def.Type);
                    if (level > 0)
                        Built.Add((def, level));
                }

            var c = new Vector2(PlotSize * 0.5f, PlotSize * 0.5f);
            const float inner = 27f;
            const float outer = 124f;
            var n = Built.Count;
            var maxLevel = 1;
            foreach (var b in Built)
                maxLevel = Mathf.Max(maxLevel, b.Level);
            var span = n > 0 ? Mathf.PI * 2f / n : Mathf.PI * 2f;
            var street = (Color)Ink + accent * 0.12f;
            var avenue = accent * 0.55f + (Color)Ink;
            var seed = (uint)(planetId * 2654435761u);
            for (var y = 0; y < PlotSize; y++)
                for (var x = 0; x < PlotSize; x++)
                {
                    var d = new Vector2(x + 0.5f - c.x, y + 0.5f - c.y);
                    var r = d.magnitude;
                    if (r < inner || r > outer + 2f || n == 0)
                        continue;
                    var ang = Mathf.Atan2(d.y, d.x) + Mathf.PI; // 0 … 2π
                    var i = Mathf.Min(n - 1, (int)(ang / span));
                    var local = ang - i * span;
                    var arc = local * r; // arc length from the sector's start (px)
                    var arcEnd = span * r;
                    var (def, level) = Built[i];
                    var depth = inner + 8f + Mathf.Log(1f + level) / Mathf.Log(1f + maxLevel) * (outer - inner - 8f);
                    // Boulevards between districts: bright through the city, a faint track across the plain.
                    if (arc < 2f || arcEnd - arc < 2f)
                    {
                        if (r < outer)
                            px[y * PlotSize + x] = r < depth ? avenue : street;
                        continue;
                    }
                    if (r > depth)
                    {
                        // The district's edge, bright; beyond it the open plain: dark ground, speckled fields.
                        if (r - depth < 1.6f)
                            px[y * PlotSize + x] = def.Accent;
                        else if (r < outer)
                        {
                            var g = Hash(seed ^ (uint)((x / 3) * 73856093 ^ (y / 3) * 19349663));
                            var ground = (Color)Ink + accent * (0.06f + (g & 31) / 31f * 0.07f);
                            ground.a = 1f;
                            px[y * PlotSize + x] = ground;
                        }

                        continue;
                    }

                    // Ring roads every 11 px, cross streets every 9 px of arc: city blocks between them.
                    var band = (int)((r - inner) / 11f);
                    var inBand = (r - inner) % 11f;
                    var cellIndex = (int)(arc / 9f);
                    var inCell = arc % 9f;
                    if (inBand < 1.4f || inCell < 1.4f)
                    {
                        px[y * PlotSize + x] = band % 3 == 0 && inBand < 1.4f ? avenue : street;
                        continue;
                    }

                    // Built-up density grows with the level; each block's brightness varies (rooftops, lights).
                    var h = Hash(seed ^ (uint)(i * 7919 + band * 104729 + cellIndex * 1299709));
                    var density = 0.35f + 0.6f * Mathf.Clamp01(Mathf.Log(1f + level) / Mathf.Log(30f));
                    if ((h & 1023) / 1023f > density)
                    {
                        px[y * PlotSize + x] = (Color)Ink + def.Accent * 0.1f;
                        continue;
                    }

                    var lum = 0.45f + ((h >> 10) & 255) / 255f * 0.45f;
                    var col = def.Accent * lum;
                    // A lit window here and there.
                    if (((h >> 18) & 7) == 0 && (int)inBand % 3 == 1 && (int)inCell % 3 == 1)
                        col = Color.Lerp(def.Accent, Color.white, 0.7f);
                    col.a = 1f;
                    px[y * PlotSize + x] = col;
                }

            // The citadel: tower at the heart, its wall ring, the outer ring road.
            Ring(px, c, outer + 1f, 1.5f, accent * 0.5f + (Color)Ink);
            Disc(px, c, inner - 7f, (Color)Ink + accent * 0.2f);
            Ring(px, c, inner - 3f, 3f, accent);
            Disc(px, c, 11f, Color.Lerp(accent, Color.white, 0.4f));
            Ring(px, c, 6f, 2f, accent * 0.6f + (Color)Ink);
            Apply(_msdTex, px);

            var name = BridgeViewscreen.StationPlanetName(_focus);
            Set(_msdTitle, string.IsNullOrEmpty(name) ? Trans.Get("vr.view.citadelHeader") : "<noparse>" + name + "</noparse>");
            _msdTitle.color = accent;
            if (Built.Count == 0)
            {
                Set(_msdBody, Trans.Get(eco == null ? "Loading" : "vr.view.cityEmpty"));
                return;
            }

            // Every building, highest level first, in its district's colour.
            Built.Sort((a, b) => b.Level.CompareTo(a.Level));
            var sb = new System.Text.StringBuilder();
            for (var k = 0; k < Built.Count; k++)
            {
                var (def, level) = Built[k];
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(def.Accent)).Append("><b>—</b></color> ")
                    .Append(Trans.Get(def.NameKey)).Append("  <b>").Append(level).Append("</b>");
                sb.Append(k % 2 == 0 ? "      " : "\n");
            }

            Set(_msdBody, sb.ToString());
        }

        static uint Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352dU;
            x ^= x >> 15;
            x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        // ── Starboard: the system plot ───────────────────────────────────────────

        void DrawSystem(Color accent)
        {
            var px = Clear(_plotTex);
            var c = new Vector2(PlotSize * 0.5f, PlotSize * 0.5f);
            Disc(px, c, 13f, new Color(1f, 0.85f, 0.45f, 1f));
            Disc(px, c, 20f, new Color(1f, 0.85f, 0.45f, 1f) * 0.25f + Ink, onlyEmpty: true);
            var maxSlot = 1;
            foreach (var p in _focus.Planets)
                maxSlot = Mathf.Max(maxSlot, p.Slot);
            float OrbitR(int slot) => 28f + (PlotSize * 0.5f - 40f) * Mathf.Clamp01(slot / (float)Mathf.Max(3, maxSlot));
            var me = FocusContext.OwnedUserId();
            var mine = 0;
            foreach (var p in _focus.Planets)
            {
                var r = OrbitR(p.Slot);
                Ring(px, c, r, 1f, accent * 0.35f + Ink);
                var a = p.Slot * 2.39996f + p.Id * 0.1f;
                var pos = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var owned = me > 0 && p.UserId == me;
                if (owned)
                    mine++;
                var tint = p.UserId > 0 ? DiplomacyIndex.Tint(DiplomacyIndex.Resolve(p.UserId)) : new Color(0.75f, 0.8f, 0.85f, 1f);
                Disc(px, pos, owned ? 8f : 6f, tint);
                if (p.Id == _focus.RotundaPlanetId || _focus.FindViewFleet()?.PlanetId == p.Id)
                    Ring(px, pos, 13f, 2f, Color.white);
            }

            var now = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var contacts = 0;
            foreach (var f in _focus.Fleets)
            {
                if (!_focus.VisibleInFocus(f, now))
                    continue;
                var planet = _focus.FindPlanet(f.PlanetId);
                var r = planet != null ? OrbitR(planet.Slot) + 12f : PlotSize * 0.5f - 16f;
                var a = planet != null ? planet.Slot * 2.39996f + planet.Id * 0.1f + 0.25f + (f.Id % 5) * 0.08f : f.Id * 0.7f;
                var pos = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                var tint = DiplomacyIndex.Tint(DiplomacyIndex.ResolveFleet(f));
                Tri(px, pos, f.Id == _focus.ViewFleetId ? 7f : 5f, tint);
                if (!_focus.IsMine(f))
                    contacts++;
            }

            Apply(_plotTex, px);
            Set(_plotTitle, _focus.HasSystem ? BridgeViewscreen.SystemLabel(_focus) : Trans.Get("Loading"));
            _plotTitle.color = accent;
            var sb = new System.Text.StringBuilder();
            sb.Append(Trans.Get("planets")).Append("  ").Append(_focus.Planets.Count);
            if (mine > 0)
                sb.Append("  ·  ").Append(Trans.Format("vr.screen.ours", mine));
            sb.AppendLine().Append(Trans.Format("vr.screen.contacts", contacts));
            if (_focus.Asteroids.Count > 0)
                sb.AppendLine().Append(Trans.Get("asteroidField")).Append("  ").Append(_focus.Asteroids.Count);
            Set(_plotBody, sb.ToString());
        }

        // ── Raster helpers (a 256² texture, redrawn on change only) ───────────────

        static Color32[] _scratch;

        static Color32[] Clear(Texture2D tex)
        {
            _scratch ??= new Color32[PlotSize * PlotSize];
            var ink = (Color32)Ink;
            for (var i = 0; i < _scratch.Length; i++)
                _scratch[i] = ink;
            return _scratch;
        }

        static void Apply(Texture2D tex, Color32[] px)
        {
            tex.SetPixels32(px);
            tex.Apply(false, false);
        }

        static void Put(Color32[] px, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= PlotSize || y >= PlotSize)
                return;
            px[y * PlotSize + x] = (Color32)new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }

        static void Rect(Color32[] px, float x, float y, float w, float h, Color c)
        {
            for (var yy = Mathf.FloorToInt(y); yy < Mathf.CeilToInt(y + h); yy++)
            for (var xx = Mathf.FloorToInt(x); xx < Mathf.CeilToInt(x + w); xx++)
                Put(px, xx, yy, c);
        }

        static void Disc(Color32[] px, Vector2 c, float r, Color col, bool onlyEmpty = false)
        {
            var ink = (Color32)Ink;
            for (var y = Mathf.FloorToInt(c.y - r); y <= Mathf.CeilToInt(c.y + r); y++)
            for (var x = Mathf.FloorToInt(c.x - r); x <= Mathf.CeilToInt(c.x + r); x++)
            {
                if ((new Vector2(x, y) - c).sqrMagnitude > r * r)
                    continue;
                if (onlyEmpty && x >= 0 && y >= 0 && x < PlotSize && y < PlotSize && !px[y * PlotSize + x].Equals(ink))
                    continue;
                Put(px, x, y, col);
            }
        }

        static void Ring(Color32[] px, Vector2 c, float r, float w, Color col)
        {
            var steps = Mathf.Max(24, Mathf.CeilToInt(r * 6.3f));
            for (var i = 0; i < steps; i++)
            {
                var a = i / (float)steps * Mathf.PI * 2f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                Rect(px, p.x - w * 0.5f, p.y - w * 0.5f, w, w, col);
            }
        }

        static void Line(Color32[] px, Vector2 a, Vector2 b, float w, Color col)
        {
            var steps = Mathf.CeilToInt(Vector2.Distance(a, b));
            for (var i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)Mathf.Max(1, steps));
                Rect(px, p.x - w * 0.5f, p.y - w * 0.5f, w, w, col);
            }
        }

        static void Tri(Color32[] px, Vector2 c, float r, Color col)
        {
            for (var y = -r; y <= r; y++)
            {
                var half = (y + r) * 0.5f;
                Rect(px, c.x - half, c.y - y, half * 2f, 1f, col);
            }
        }

        static void Set(TMP_Text t, string s)
        {
            if (t.text != s)
                t.text = s;
        }
    }
}
