using System.Collections.Generic;
using Core.App;
using Core.Stations;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// The aft bulkhead's two backlit wall displays, either side of the corridor door (a Star Trek "MSD"):
    /// to port, the ship we stand on — its 9×9 module plan in family colours with the sums of its modules (or, at
    /// an orbital station, the station's ring over its world); to starboard, the system plot — star, orbits, worlds
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

        public static BridgeWallDisplays Build(CicEnvironment host, FocusContext focus)
        {
            var go = new GameObject("BridgeWallDisplays");
            go.transform.SetParent(host.transform, false);
            var d = go.AddComponent<BridgeWallDisplays>();
            d._focus = focus;
            var yaw = Quaternion.Euler(0f, BridgeShell.EdgeYaw(BridgeShell.AftWall) + 180f, 0f);
            var len = BridgeShell.EdgeLength(BridgeShell.AftWall);
            d._msd = Panel(go.transform, "ShipDisplay", BridgeShell.EdgePoint(BridgeShell.AftWall, len * 0.5f - 2.35f, 0.07f, 1.72f), yaw);
            d._plot = Panel(go.transform, "SystemDisplay", BridgeShell.EdgePoint(BridgeShell.AftWall, len * 0.5f + 2.35f, 0.07f, 1.72f), yaw);
            d._msdTex = NewTex("SU_ShipDisplay");
            d._plotTex = NewTex("SU_SystemDisplay");
            Layout(d._msd, d._msdTex, out d._msdTitle, out d._msdBody);
            Layout(d._plot, d._plotTex, out d._plotTitle, out d._plotBody);
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

            if (_msdTex != null)
                Destroy(_msdTex);
            if (_plotTex != null)
                Destroy(_plotTex);
        }

        void MarkDirty() => _dirty = true;

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

        void Update()
        {
            if (!_dirty || Time.unscaledTime < _next || _focus == null)
                return;
            _dirty = false;
            _next = Time.unscaledTime + 1f;
            var accent = _focus.ViewFleetId > 0 ? CicArtKit.Cyan : CicArtKit.Amber;
            _msd.SetAccent(accent, 0.45f);
            _plot.SetAccent(accent, 0.45f);
            DrawShip(accent);
            DrawSystem(accent);
        }

        // ── Port: the ship (or the station) ──────────────────────────────────────

        void DrawShip(Color accent)
        {
            var px = Clear(_msdTex);
            var fleet = _focus.FindViewFleet();
            if (fleet == null)
            {
                // Station: its ring and spokes over the world it orbits.
                var c = new Vector2(PlotSize * 0.5f, PlotSize * 0.5f);
                Disc(px, c, 34f, new Color(0.25f, 0.55f, 0.8f, 1f));
                Ring(px, c, 92f, 6f, accent);
                Ring(px, c, 70f, 2f, accent * 0.6f);
                for (var i = 0; i < 6; i++)
                {
                    var d = new Vector2(Mathf.Cos(i * Mathf.PI / 3f), Mathf.Sin(i * Mathf.PI / 3f));
                    Line(px, c + d * 40f, c + d * 90f, 3f, accent * 0.8f);
                    Disc(px, c + d * 92f, 9f, Color.Lerp(accent, Color.white, 0.4f));
                }

                Apply(_msdTex, px);
                Set(_msdTitle, Trans.Get("vr.view.stationHeader"));
                Set(_msdBody, Trans.Format("vr.view.orbiting", BridgeViewscreen.StationPlanetName(_focus)));
                _msdTitle.color = accent;
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
            sb.Append(Trans.Get("armor")).Append("  ").Append(Mathf.RoundToInt(s.Armor)).Append("    ")
                .Append(Trans.Get("shield")).Append("  ").Append(Mathf.RoundToInt(s.Shield)).AppendLine();
            sb.Append(Trans.Get("damage")).Append("  ").Append(Mathf.RoundToInt(s.Damage)).Append("    ")
                .Append(Trans.Get("speed")).Append("  ").Append(Mathf.RoundToInt(s.Speed)).AppendLine();
            sb.Append(Trans.Get("cargo")).Append("  ").Append(Mathf.RoundToInt(s.Cargo)).AppendLine().AppendLine();
            foreach (var kv in families)
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(kv.Key))).Append("><b>—</b></color> ")
                    .Append(Trans.Get(ModuleCatalog.FamilyKey(kv.Key))).Append(" ").Append(kv.Value).Append("   ");
            Set(_msdBody, sb.ToString());
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
                if (p.Id == _focus.ViewPlanetId || _focus.FindViewFleet()?.PlanetId == p.Id)
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
