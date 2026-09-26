using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// Shipyard tab of the dock's hangar screen (web planet.js "Chantier" card): the module being built with
    /// its Nova finish, the planet_ship_queue with cancel, and the module catalog by family (GetConfigs.shipstats)
    /// with cost, time and requirements — Build, or Add to queue when the yard is busy. One module per order
    /// (AddShip has no quantity). Finished modules land in the hangar, i.e. on the rack.
    /// </summary>
    public sealed class ShipyardPanel
    {
        const int PerPage = 4;

        readonly RectTransform _body;
        readonly EconomyService _eco;
        readonly Func<int> _planet;
        readonly Action<string, bool> _status;
        readonly Action _changed;
        readonly List<(TMP_Text, Func<string>)> _live = new();
        readonly List<(Image, Func<float>)> _bars = new();
        ModuleFamily _family = ModuleFamily.Core;
        int _page;
        bool _busy;

        public ShipyardPanel(RectTransform body, EconomyService eco, Func<int> planet, Action<string, bool> status,
            Action changed)
        {
            _body = body;
            _eco = eco;
            _planet = planet;
            _status = status;
            _changed = changed;
        }

        public void Tick()
        {
            foreach (var (t, v) in _live)
                if (t != null)
                    t.text = v();
            foreach (var (img, v) in _bars)
                if (img != null)
                    img.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v()), 1f);
        }

        public void Render()
        {
            _live.Clear();
            _bars.Clear();
            for (var i = _body.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_body.GetChild(i).gameObject);
            if (!_eco.TryGet(_planet(), out var planet))
            {
                Text(Trans.Get("Loading"), 0f, 60f, 860f, 20f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                return;
            }

            var y = RenderActive(planet, 250f);
            y = RenderQueue(planet, y);
            RenderCatalog(planet, y - 8f);
        }

        // ── Active build + queue ──────────────────────────────────────────────────

        static JObject Active(PlanetEconomy p)
        {
            var q = p.Raw?["shipQueue"] as JObject;
            return q != null && FocusContext.AsLong(q["endTime"]) > FleetOrderGate.UnixNow() ? q : null;
        }

        static JArray Queued(PlanetEconomy p) => p.Raw?["shipQueueList"] as JArray;

        static int MaxQueue(PlanetEconomy p)
        {
            var n = FocusContext.AsInt(p.Raw?["maxQueue"]);
            return n > 0 ? n : 2;
        }

        static int Occupied(PlanetEconomy p) => (Active(p) != null ? 1 : 0) + (Queued(p)?.Count ?? 0);

        float RenderActive(PlanetEconomy p, float y)
        {
            var active = Active(p);
            Text(Trans.Format("vr.ops.queueSlots", Occupied(p), MaxQueue(p)), -440f, y, 300f, 17f, DiegeticUi.CyanDim);
            if (active == null)
            {
                Text(Trans.Get("vr.yard.idle"), -130f, y, 560f, 17f, DiegeticUi.CyanDim);
                return y - 44f;
            }

            var type = FocusContext.AsString(active["type"]);
            var end = FocusContext.AsLong(active["endTime"]);
            var total = FocusContext.AsFloat(ModuleCatalog.Stats(type)?["time"]);
            Text("<b>" + Trans.Get(type) + "</b>", -130f, y, 330f, 19f, UiKit.TextBright);
            var remain = Text(string.Empty, 200f, y, 110f, 17f, DiegeticUi.CyanDim);
            _live.Add((remain, () => Core.Holo.TravelPlanner.TimeText(end - FleetOrderGate.UnixNow())));
            Bar(new Vector2(-130f, y - 26f), new Vector2(440f, 7f),
                () => ServerTimers.Shipyard(p.Id) ??
                      (total <= 0f ? 0f : 1f - Mathf.Clamp01((end - FleetOrderGate.UnixNow()) / total)));
            var cost = BuildingCatalog.SpeedupCost(end - FleetOrderGate.UnixNow());
            var canPay = cost == 0 || _eco.Nova >= cost;
            var finish = Btn(cost == 0 ? Trans.Format("vr.ops.finish", Trans.Get("free"))
                    : Trans.Format("vr.ops.finish", cost + " " + Trans.Get("nova")), 370f, y - 8f, 190f, 46f,
                () => AsyncTap.Run(Order("SpeedupShipyard", new Dictionary<string, string> { { "planet", p.Id.ToString() } },
                    "speedupShipyardSuccess")), canPay ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
            finish.interactable = canPay;
            return y - 62f;
        }

        float RenderQueue(PlanetEconomy p, float y)
        {
            var rows = Queued(p);
            if (rows == null)
                return y;
            for (var i = 0; i < rows.Count && i < 3; i++)
            {
                var row = rows[i];
                var id = FocusContext.AsInt(row["id"]);
                var type = FocusContext.AsString(row["ship_type"]);
                Text((i + 2) + ".  " + Trans.Get(type) + "   <size=85%><color=#7fd8ff>" +
                     Core.Holo.TravelPlanner.TimeText(FocusContext.AsFloat(row["duration"])) + "</color></size>",
                    -440f, y, 620f, 17f, UiKit.TextBright);
                // Server reads `id` (planet_ship_queue row); `queue_id` is only the web's alias.
                Btn(Trans.Get("cancel"), 370f, y, 190f, 40f,
                    () => AsyncTap.Run(Order("CancelQueuedShip", new Dictionary<string, string> { { "id", id.ToString() } },
                        "vr.ops.cancelled")), DiegeticUi.BtnStyle.Danger);
                y -= 46f;
            }

            return y - 4f;
        }

        // ── Catalog ───────────────────────────────────────────────────────────────

        void RenderCatalog(PlanetEconomy p, float y)
        {
            var families = (ModuleFamily[])Enum.GetValues(typeof(ModuleFamily));
            for (var i = 0; i < families.Length; i++)
            {
                var f = families[i];
                var col = i % 5;
                var row = i / 5;
                var b = Btn(Trans.Get(ModuleCatalog.FamilyKey(f)), -360f + col * 180f, y - row * 42f, 172f, 38f,
                    () =>
                    {
                        _family = f;
                        _page = 0;
                        Render();
                    }, f == _family ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                var chip = b.GetComponentInChildren<TMP_Text>();
                chip.color = f == _family ? UiKit.TextBright : ModuleCatalog.Accent(f);
            }

            y -= 2 * 42f + 10f;
            var types = new List<string>();
            foreach (var t in ModuleCatalog.Types())
                if (ModuleCatalog.Family(t) == _family)
                    types.Add(t);
            var pages = Mathf.Max(1, Mathf.CeilToInt(types.Count / (float)PerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var busy = Active(p) != null;
            var full = Occupied(p) >= MaxQueue(p);
            for (var i = _page * PerPage; i < types.Count && i < (_page + 1) * PerPage; i++)
            {
                var type = types[i];
                var st = ModuleCatalog.Stats(type);
                var mineral = FocusContext.AsInt(st?["cost"]?["mineral"]);
                var crystal = FocusContext.AsInt(st?["cost"]?["crystal"]);
                var time = FocusContext.AsFloat(st?["time"]);
                var lockKey = Locked(p, st, out var lockLevel, out var lockOn);
                var afford = p.Mineral >= mineral && p.Crystal >= crystal;
                Text("<b>" + Trans.Get(type) + "</b>", -440f, y + 9f, 470f, 18f, UiKit.TextBright);
                Text(Trans.Get("vr.res.mineral") + " " + mineral + "  ·  " + Trans.Get("vr.res.crystal") + " " + crystal +
                     "  ·  " + Core.Holo.TravelPlanner.TimeText(time), -440f, y - 13f, 470f, 14f,
                    afford ? new Color(0.78f, 0.9f, 0.96f, 1f) : UiKit.Danger);

                string label;
                var style = DiegeticUi.BtnStyle.Cyan;
                var enabled = true;
                if (lockKey != null)
                {
                    label = Trans.Format("vr.ops.requires", Trans.Get(lockOn), lockLevel);
                    style = DiegeticUi.BtnStyle.Ghost;
                    enabled = false;
                }
                else if (full)
                {
                    label = Trans.Get("queueFull");
                    style = DiegeticUi.BtnStyle.Ghost;
                    enabled = false;
                }
                else if (!afford)
                {
                    label = Trans.Get("notEnoughRessource");
                    style = DiegeticUi.BtnStyle.Ghost;
                    enabled = false;
                }
                else if (busy)
                {
                    label = Trans.Get("addToQueue");
                    style = DiegeticUi.BtnStyle.Amber;
                }
                else
                {
                    label = Trans.Get("vr.yard.build");
                }

                var t = type;
                var btn = Btn(label, 300f, y, 280f, 46f, () => AsyncTap.Run(Order("AddShip",
                    new Dictionary<string, string> { { "type", t }, { "planet", p.Id.ToString() } }, "shipInBuild")), style);
                btn.interactable = enabled;
                y -= 56f;
            }

            if (pages > 1)
            {
                Btn("‹", -80f, -300f, 70f, 42f, () => { _page = (_page - 1 + pages) % pages; Render(); }, DiegeticUi.BtnStyle.Ghost);
                Text((_page + 1) + " / " + pages, 0f, -300f, 90f, 17f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                Btn("›", 80f, -300f, 70f, 42f, () => { _page = (_page + 1) % pages; Render(); }, DiegeticUi.BtnStyle.Ghost);
            }
        }

        /// <summary>
        /// First unmet requirement (shipstats.requiert): orbitShipyard / academy are planet buildings, anything
        /// else an empire research — as AddShip checks it.
        /// </summary>
        string Locked(PlanetEconomy p, JObject st, out int level, out string on)
        {
            level = 0;
            on = null;
            if (!(st?["requiert"] is JObject req))
                return null;
            foreach (var r in req.Properties())
            {
                var need = FocusContext.AsInt(r.Value);
                var have = r.Name is "orbitShipyard" or "academy" ? p.Level(r.Name) : _eco.ResearchLevel(r.Name);
                if (have >= need)
                    continue;
                level = need;
                on = r.Name;
                return "locked";
            }

            return null;
        }

        async Task Order(string action, Dictionary<string, string> q, string okKey)
        {
            if (_busy)
                return;
            _busy = true;
            // Any order may replace the running job: its server progress is read again.
            ServerTimers.Invalidate();
            try
            {
                var r = await ActionJs.Get(action, q);
                if (r.Ok)
                {
                    CicCue.Ok(_body.position);
                    _status(Trans.Get(okKey), false);
                }
                else
                {
                    CicCue.Fail(_body.position);
                    _status(string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error, true);
                }

                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Engineering, action, r,
                    q.TryGetValue("type", out var t) ? Trans.Get(t) : string.Empty);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
            _changed?.Invoke();
        }

        // ── Widgets ───────────────────────────────────────────────────────────────

        TMP_Text Text(string text, float x, float y, float width, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var left = align is TextAlignmentOptions.MidlineLeft;
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(left ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            return t;
        }

        Button Btn(string label, float x, float y, float w, float h, Action act, DiegeticUi.BtnStyle style)
        {
            var b = DiegeticUi.HoloButton(_body, label, new Vector2(x, y), new Vector2(w, h), () => act(), style);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 10f;
            t.fontSizeMax = Mathf.Min(18f, h * 0.42f);
            return b;
        }

        void Bar(Vector2 pos, Vector2 size, Func<float> value)
        {
            var back = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(_body, false);
            var brt = back.GetComponent<RectTransform>();
            brt.sizeDelta = size;
            brt.anchoredPosition = pos;
            var bimg = back.GetComponent<Image>();
            bimg.color = new Color(0.1f, 0.25f, 0.32f, 0.9f);
            bimg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            var fimg = fill.GetComponent<Image>();
            fimg.color = UiKit.Amber;
            fimg.raycastTarget = false;
            _bars.Add((fimg, value));
        }
    }
}
