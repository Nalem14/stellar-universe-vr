using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// (AddShip has no quantity). Finished modules land in the hangar, i.e. on the rack. The dock's planet pays
    /// (AddShip planet=): its stock heads the tab, and each cost it cannot cover reads red.
    /// </summary>
    public sealed class ShipyardPanel
    {
        const int PerPage = 4;
        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        static readonly string[] BaseResources = { "mineral", "crystal" };

        readonly RectTransform _body;
        readonly EconomyService _eco;
        readonly Func<int> _planet;
        readonly Action<string, bool> _status;
        readonly Action _changed;
        readonly List<(TMP_Text, Func<string>)> _live = new();
        readonly List<(Image, Func<float>)> _bars = new();
        readonly List<string> _resources = new();
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

            var y = RenderStock(250f);
            y = RenderActive(planet, y);
            y = RenderQueue(planet, y);
            RenderCatalog(planet, y - 8f);
        }

        // ── Planet stock ──────────────────────────────────────────────────────────

        /// <summary>
        /// What the dock's planet holds of every resource a module can cost (GetConfigs.shipstats cost keys),
        /// read live from the EconomyService cache (its own 10 s poll + a refresh after each order).
        /// </summary>
        float RenderStock(float y)
        {
            _resources.Clear();
            _resources.AddRange(BaseResources);
            foreach (var t in ModuleCatalog.Types())
            {
                if (!(ModuleCatalog.Stats(t)?["cost"] is JObject cost))
                    continue;
                foreach (var c in cost.Properties())
                    if (!_resources.Contains(c.Name))
                        _resources.Add(c.Name);
            }

            var strip = new GameObject("StockStrip", typeof(RectTransform), typeof(Image));
            strip.transform.SetParent(_body, false);
            var srt = strip.GetComponent<RectTransform>();
            srt.sizeDelta = new Vector2(900f, 34f);
            srt.anchoredPosition = new Vector2(0f, y);
            var img = strip.GetComponent<Image>();
            img.color = new Color(0.1f, 0.35f, 0.22f, 0.35f);
            img.raycastTarget = false;
            var edge = new GameObject("StockEdge", typeof(RectTransform), typeof(Image));
            edge.transform.SetParent(strip.transform, false);
            var ert = edge.GetComponent<RectTransform>();
            ert.anchorMin = new Vector2(0f, 0f);
            ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 0.5f);
            ert.sizeDelta = new Vector2(5f, 0f);
            ert.anchoredPosition = Vector2.zero;
            var eimg = edge.GetComponent<Image>();
            eimg.color = new Color(0.4f, 0.95f, 0.55f, 0.9f);
            eimg.raycastTarget = false;

            var line = Text(string.Empty, -430f, y, 870f, 16f, UiKit.TextBright);
            line.text = StockLine();
            _live.Add((line, () => StockLine()));
            return y - 36f;
        }

        string StockLine()
        {
            if (!_eco.TryGet(_planet(), out var p))
                return Trans.Get("Loading");
            var name = !string.IsNullOrEmpty(p.Name) ? p.Name : "#" + p.Id;
            var line = "<color=#7fd8ff>" + Trans.Format("vr.yard.planetStock", name) + "</color>   ";
            for (var i = 0; i < _resources.Count; i++)
            {
                if (i > 0)
                    line += "  ·  ";
                line += Trans.Get("vr.res." + _resources[i]) + " <b>" + Num(Have(p, _resources[i])) + "</b>";
            }

            return line;
        }

        /// <summary>Planet stock of a cost key (AddShip compares planet[key] with the cost).</summary>
        static float Have(PlanetEconomy p, string key) => key switch
        {
            "mineral" => p.Mineral,
            "crystal" => p.Crystal,
            "biomass" => p.Biomass,
            _ => FocusContext.AsFloat(p.Raw?[key])
        };

        static string Num(float v) => Mathf.FloorToInt(v).ToString("N0", Fr);

        static bool Affordable(PlanetEconomy p, JObject st)
        {
            if (!(st?["cost"] is JObject cost))
                return true;
            foreach (var c in cost.Properties())
                if (Have(p, c.Name) < FocusContext.AsFloat(c.Value))
                    return false;
            return true;
        }

        /// <summary>"Mineral 1 800 · Crystal 600 · 2 min", each cost the planet lacks in red.</summary>
        string CostLine(JObject st, float time)
        {
            var line = string.Empty;
            _eco.TryGet(_planet(), out var p);
            if (st?["cost"] is JObject cost)
                foreach (var c in cost.Properties())
                {
                    var need = FocusContext.AsFloat(c.Value);
                    if (need <= 0f)
                        continue;
                    var ok = p != null && Have(p, c.Name) >= need;
                    line += (ok ? "<color=#c7e6f5>" : "<color=#ff6a5a>") + Trans.Get("vr.res." + c.Name) + " " + Num(need) +
                            "</color>  ·  ";
                }

            return line + "<color=#c7e6f5>" + Core.Holo.TravelPlanner.TimeText(time) + "</color>";
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
            Text(Boosters.QueueHint(), -440f, y - 22f, 300f, 13f, DiegeticUi.CyanDim);
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
                var time = FocusContext.AsFloat(st?["time"]);
                var lockKey = Locked(p, st, out var lockLevel, out var lockOn);
                var afford = Affordable(p, st);
                // Family tick, name, what the module brings (non-zero shipstats), then what it costs.
                var tick = new GameObject("Tick", typeof(RectTransform), typeof(Image));
                tick.transform.SetParent(_body, false);
                var trt = tick.GetComponent<RectTransform>();
                trt.sizeDelta = new Vector2(5f, 50f);
                trt.anchoredPosition = new Vector2(-448f, y);
                var timg = tick.GetComponent<Image>();
                timg.color = ModuleCatalog.Accent(ModuleCatalog.Family(type));
                timg.raycastTarget = false;
                Text("<b>" + Trans.Get(type) + "</b>", -440f, y + 17f, 470f, 17f, UiKit.TextBright);
                var stats = Text(ModuleCatalog.StatsLine(type), -440f, y, 470f, 13f, UiKit.TextBright);
                stats.enableAutoSizing = true;
                stats.fontSizeMin = 10f;
                stats.fontSizeMax = 13f;
                stats.textWrappingMode = TextWrappingModes.NoWrap;
                // Live: the planet keeps producing between renders (EconomyService poll), costs turn back to white.
                var costText = Text(CostLine(st, time), -440f, y - 17f, 470f, 13f, Color.white);
                _live.Add((costText, () => CostLine(st, time)));

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
                Btn("‹", -80f, -322f, 70f, 42f, () => { _page = (_page - 1 + pages) % pages; Render(); }, DiegeticUi.BtnStyle.Ghost);
                Text((_page + 1) + " / " + pages, 0f, -322f, 90f, 17f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                Btn("›", 80f, -322f, 70f, 42f, () => { _page = (_page + 1) % pages; Render(); }, DiegeticUi.BtnStyle.Ghost);
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

        // ── Shelf fabricator (the dock's module wall reads and orders through the same rules) ──────────

        /// <summary>The dock planet's cost line for one module (each cost it cannot cover in red) + build time.</summary>
        public string CostText(string type)
        {
            var st = ModuleCatalog.Stats(type);
            return CostLine(st, FocusContext.AsFloat(st?["time"]));
        }

        /// <summary>True when the dock's planet holds every resource the module costs right now.</summary>
        public bool CanAfford(string type) => _eco.TryGet(_planet(), out var p) && Affordable(p, ModuleCatalog.Stats(type));

        /// <summary>The module is being built or waits in the planet's shipyard queue.</summary>
        public bool InProduction(string type)
        {
            if (string.IsNullOrEmpty(type) || !_eco.TryGet(_planet(), out var p))
                return false;
            if (Active(p) is { } a && FocusContext.AsString(a["type"]) == type)
                return true;
            if (Queued(p) is { } rows)
                foreach (var row in rows)
                    if (FocusContext.AsString(row["ship_type"]) == type)
                        return true;
            return false;
        }

        /// <summary>
        /// Why AddShip would refuse this module on the dock's planet (localised, same order as the catalog's
        /// button: requirement, full queue, resources), or null when it can go.
        /// </summary>
        public string BuildBlocker(string type)
        {
            if (!_eco.TryGet(_planet(), out var p))
                return Trans.Get("Loading");
            var st = ModuleCatalog.Stats(type);
            if (st == null)
                return Trans.Get("vr.common.error");
            if (Locked(p, st, out var level, out var on) != null)
                return Trans.Format("vr.ops.requires", Trans.Get(on), level);
            if (Occupied(p) >= MaxQueue(p))
                return Trans.Get("queueFull");
            return Affordable(p, st) ? null : Trans.Get("notEnoughRessource");
        }

        /// <summary>
        /// The module on the yard's bed right now (shipQueue): its type, end (unix s), progress 0..1 (server
        /// CheckShipQueue read, else the time left over the module's build time) and how many orders wait behind it.
        /// </summary>
        public bool TryActiveJob(out string type, out long end, out float progress, out int queued)
        {
            type = null;
            end = 0;
            progress = 0f;
            queued = 0;
            if (!_eco.TryGet(_planet(), out var p))
                return false;
            queued = Queued(p)?.Count ?? 0;
            if (!(Active(p) is { } a))
                return false;
            type = FocusContext.AsString(a["type"]);
            end = FocusContext.AsLong(a["endTime"]);
            var total = FocusContext.AsFloat(ModuleCatalog.Stats(type)?["time"]);
            progress = ServerTimers.Shipyard(p.Id) ??
                       (total <= 0f ? 0f : 1f - Mathf.Clamp01((end - FleetOrderGate.UnixNow()) / total));
            return !string.IsNullOrEmpty(type);
        }

        /// <summary>The yard is busy: a new order joins the queue (web « Ajouter à la file »).</summary>
        public bool YardBusy => _eco.TryGet(_planet(), out var p) && Active(p) != null;

        /// <summary>One AddShip for the dock's planet (same order as the catalog's Build button).</summary>
        public Task Build(string type) => Order("AddShip",
            new Dictionary<string, string> { { "type", type }, { "planet", _planet().ToString() } }, "shipInBuild");

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
