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
    /// <summary>One saved ship design (ship_templates row, GetShipTemplates).</summary>
    public sealed class Blueprint
    {
        public int Id;
        public string Name = string.Empty;
        public readonly List<FocusShipModule> Modules = new();
    }

    /// <summary>
    /// Blueprints tab of the dock's hangar screen (web ShipBuilderUI templates modal, commit a93b82c).
    /// Save the selected ship's layout under a name (Quest keyboard) → SaveShipTemplate; pick a blueprint
    /// and the dock projects it — holo hull in the cradle, its modules on the assembly table, green where
    /// the part is on hand, red where it is missing; Load (twice) → ApplyShipTemplate; Delete (twice) →
    /// DeleteShipTemplate. Availability counts the planet's finished hangar plus the ship's own non-core
    /// modules, which ApplyShipTemplate sends back to the hangar before placing.
    /// </summary>
    public sealed class BlueprintPanel
    {
        const int PerPage = 4;
        const float ArmSeconds = 4f;

        readonly RectTransform _body;
        readonly Func<int> _fleet;
        readonly Func<Dictionary<string, int>> _stock;
        readonly Action<string, bool> _status;
        readonly Action<Blueprint> _preview;
        readonly Func<Task> _applied;
        readonly List<Blueprint> _list = new();
        TMP_InputField _nameField;
        bool _loaded;
        bool _loading;
        bool _busy;
        int _page;
        int _selected;
        string _armed;
        float _armedUntil;

        public BlueprintPanel(RectTransform body, Func<int> fleet, Func<Dictionary<string, int>> stock,
            Action<string, bool> status, Action<Blueprint> preview, Func<Task> applied)
        {
            _body = body;
            _fleet = fleet;
            _stock = stock;
            _status = status;
            _preview = preview;
            _applied = applied;
        }

        public Blueprint Selected => Find(_selected);

        public void Deselect()
        {
            _selected = 0;
            _armed = null;
            _preview(null);
        }

        public void Tick()
        {
            if (_armed != null && Time.unscaledTime > _armedUntil)
            {
                _armed = null;
                Render();
            }
        }

        Blueprint Find(int id)
        {
            foreach (var b in _list)
                if (b.Id == id)
                    return b;
            return null;
        }

        /// <summary>Required parts not on hand for <paramref name="bp"/>, by module type.</summary>
        public Dictionary<string, int> Missing(Blueprint bp)
        {
            var stock = _stock();
            var missing = new Dictionary<string, int>();
            foreach (var m in bp.Modules)
            {
                if (m.Type == ModuleCatalog.Core)
                    continue;
                if (stock.TryGetValue(m.Type, out var n) && n > 0)
                    stock[m.Type] = n - 1;
                else
                    missing[m.Type] = (missing.TryGetValue(m.Type, out var k) ? k : 0) + 1;
            }

            return missing;
        }

        /// <summary>Per module of <paramref name="bp"/>: is it on hand (in placement order, inner cells first)?</summary>
        public bool[] Availability(Blueprint bp)
        {
            var stock = _stock();
            var order = new List<int>();
            for (var i = 0; i < bp.Modules.Count; i++)
                order.Add(i);
            // The server places inner modules first (Manhattan distance to the core) — so do we.
            order.Sort((a, b) => Dist(bp.Modules[a]).CompareTo(Dist(bp.Modules[b])));
            var ok = new bool[bp.Modules.Count];
            foreach (var i in order)
            {
                var t = bp.Modules[i].Type;
                if (t == ModuleCatalog.Core)
                {
                    ok[i] = true;
                    continue;
                }

                if (stock.TryGetValue(t, out var n) && n > 0)
                {
                    stock[t] = n - 1;
                    ok[i] = true;
                }
            }

            return ok;
        }

        static int Dist(FocusShipModule m) =>
            Mathf.Abs(m.GridX - ModuleCatalog.CoreCell) + Mathf.Abs(m.GridY - ModuleCatalog.CoreCell);

        public void Render()
        {
            for (var i = _body.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_body.GetChild(i).gameObject);
            if (!_loaded)
            {
                Text(Trans.Get("Loading"), 0f, 60f, 860f, 20f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                if (!_loading)
                    AsyncTap.Run(Load());
                return;
            }

            // Save the selected ship's layout.
            var fleet = _fleet();
            var y = 250f;
            if (fleet > 0)
            {
                _nameField = DiegeticUi.HoloField(_body, "BlueprintName", Trans.Get("templateNamePlaceholder"),
                    new Vector2(-170f, y), new Vector2(540f, 48f), TouchScreenKeyboardType.Default);
                _nameField.characterLimit = 64;
                Btn(Trans.Get("saveCurrentTemplate"), 300f, y, 220f, 48f, () => AsyncTap.Run(Save()),
                    DiegeticUi.BtnStyle.Amber);
            }
            else
                Text(Trans.Get("blueprintDesignHint"), -440f, y, 880f, 16f, DiegeticUi.CyanDim);

            y -= 66f;
            if (_list.Count == 0)
            {
                Text(Trans.Get("noTemplatesFound"), 0f, y - 40f, 860f, 19f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                return;
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(_list.Count / (float)PerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var first = _page * PerPage;
            for (var i = first; i < _list.Count && i < first + PerPage; i++)
            {
                var bp = _list[i];
                var missing = Missing(bp);
                var lack = 0;
                foreach (var n in missing.Values)
                    lack += n;
                var total = bp.Modules.Count;
                var have = total - lack;
                var tone = lack == 0 ? "#7dffa0" : "#ff8a78";
                var id = bp.Id;
                Btn("<b>" + bp.Name + "</b>   <size=80%>" + Trans.Get("modules") + " " + total +
                    "   <color=" + tone + ">" + have + "/" + total + "</color></size>", -115f, y, 620f, 54f,
                    () => Select(id), id == _selected ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                var armedDelete = _armed == "del" + id;
                Btn(armedDelete ? Trans.Get("validate") : "×", 380f, y, armedDelete ? 130f : 70f, 54f,
                    () => Arm("del" + id, () => AsyncTap.Run(Delete(id))), DiegeticUi.BtnStyle.Danger);
                y -= 62f;
            }

            if (pages > 1)
            {
                Btn("‹", -80f, y, 70f, 44f, () => { _page = (_page - 1 + pages) % pages; Render(); }, DiegeticUi.BtnStyle.Ghost);
                Text((_page + 1) + " / " + pages, 0f, y, 90f, 18f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                Btn("›", 80f, y, 70f, 44f, () => { _page = (_page + 1) % pages; Render(); }, DiegeticUi.BtnStyle.Ghost);
                y -= 54f;
            }

            var sel = Selected;
            if (sel == null)
                return;

            // Selected: what is on hand, then load onto the ship in the cradle.
            var miss = Missing(sel);
            string line;
            if (miss.Count == 0)
                line = "<color=#7dffa0>" + Trans.Get("templateAllAvailable") + "</color>";
            else
            {
                var parts = new List<string>();
                foreach (var kv in miss)
                    parts.Add(kv.Value + "× " + Trans.Get(ModuleCatalog.NameKey(kv.Key)));
                line = "<color=#ff8a78>" + string.Join(", ", parts) + "</color>";
            }

            Text(line, -440f, y, 880f, 15f, UiKit.TextBright);
            y -= 52f;
            if (fleet <= 0)
            {
                Text(Trans.Get("vr.dock.pickShip"), 0f, y, 860f, 17f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                return;
            }

            var armedLoad = _armed == "load" + sel.Id;
            Btn(armedLoad ? Trans.Get("confirmApplyTemplate") : Trans.Get("loadTemplate"), 0f, y, 860f, 54f,
                () => Arm("load" + sel.Id, () => AsyncTap.Run(Apply(sel.Id))),
                armedLoad ? DiegeticUi.BtnStyle.Danger : DiegeticUi.BtnStyle.Amber);
        }

        void Select(int id)
        {
            _armed = null;
            _selected = _selected == id ? 0 : id;
            _preview(Selected);
            CicCue.Hover(_body.position);
            Render();
        }

        /// <summary>Destructive buttons ask twice (no modal in a headset): first press arms for a few seconds.</summary>
        void Arm(string key, Action act)
        {
            if (_busy)
                return;
            if (_armed == key)
            {
                _armed = null;
                act();
                return;
            }

            _armed = key;
            _armedUntil = Time.unscaledTime + ArmSeconds;
            CicCue.Hover(_body.position);
            Render();
        }

        // ── Server ────────────────────────────────────────────────────────────────

        public async Task Load()
        {
            _loading = true;
            try
            {
                var r = await ActionJs.Get("GetShipTemplates");
                _list.Clear();
                if (r.Ok && !string.IsNullOrEmpty(r.Body))
                {
                    try
                    {
                        if (JToken.Parse(r.Body) is JArray arr)
                            foreach (var t in arr)
                                _list.Add(Parse(t));
                    }
                    catch
                    {
                        // Keep the list empty on a malformed body.
                    }
                }
                else if (!r.Ok)
                    _status(ErrorText(r.Error), true);

                _loaded = true;
                if (Find(_selected) == null && _selected != 0)
                    Deselect();
            }
            finally
            {
                _loading = false;
            }

            Render();
        }

        static Blueprint Parse(JToken t)
        {
            var bp = new Blueprint
            {
                Id = FocusContext.AsInt(t["id"]),
                Name = FocusContext.AsString(t["name"])
            };
            if (t["modules"] is JArray mods)
                foreach (var m in mods)
                    bp.Modules.Add(new FocusShipModule
                    {
                        Type = FocusContext.AsString(m["type"]),
                        GridX = FocusContext.AsInt(m["grid_x"]),
                        GridY = FocusContext.AsInt(m["grid_y"])
                    });
            bp.Modules.RemoveAll(m => !m.OnGrid);
            return bp;
        }

        async Task Save()
        {
            var fleet = _fleet();
            if (_busy || fleet <= 0)
                return;
            var name = _nameField != null ? _nameField.text.Trim() : string.Empty;
            if (name.Length == 0)
            {
                CicCue.Fail(_body.position);
                _status(Trans.Get("enterTemplateName"), true);
                return;
            }

            _busy = true;
            try
            {
                var r = await ActionJs.Get("SaveShipTemplate", new Dictionary<string, string>
                {
                    { "fleet", fleet.ToString() },
                    { "name", name }
                });
                Feedback(r, "templateSaved");
                if (r.Ok)
                {
                    try
                    {
                        _selected = FocusContext.AsInt(JToken.Parse(r.Body)["id"]);
                    }
                    catch
                    {
                        _selected = 0;
                    }
                }
            }
            finally
            {
                _busy = false;
            }

            await Load();
            _preview(Selected);
        }

        async Task Delete(int id)
        {
            _busy = true;
            try
            {
                var r = await ActionJs.Get("DeleteShipTemplate", new Dictionary<string, string> { { "id", id.ToString() } });
                Feedback(r, "templateDeleted");
                if (r.Ok && _selected == id)
                    Deselect();
            }
            finally
            {
                _busy = false;
            }

            await Load();
        }

        async Task Apply(int id)
        {
            var fleet = _fleet();
            if (fleet <= 0)
                return;
            _busy = true;
            try
            {
                var r = await ActionJs.Get("ApplyShipTemplate", new Dictionary<string, string>
                {
                    { "fleet", fleet.ToString() },
                    { "template", id.ToString() }
                });
                if (r.Ok)
                {
                    var placed = 0;
                    var missing = 0;
                    try
                    {
                        var o = JToken.Parse(r.Body);
                        placed = FocusContext.AsInt(o["placed"]);
                        missing = FocusContext.AsInt(o["missing"]);
                    }
                    catch
                    {
                        // Body shape is informative only.
                    }

                    CicCue.Ok(_body.position);
                    _status(Trans.Get("templateApplied") + "  " + Trans.Format("vr.dock.templatePlaced", placed) +
                            (missing > 0 ? "  —  " + missing + " " + Trans.Get("templateMissingModules") : string.Empty),
                        missing > 0);
                    Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Engineering, "ApplyShipTemplate", r,
                        Find(id)?.Name ?? string.Empty);
                    Deselect();
                }
                else
                {
                    CicCue.Fail(_body.position);
                    _status(ErrorText(r.Error), true);
                }
            }
            finally
            {
                _busy = false;
            }

            await _applied();
            Render();
        }

        void Feedback(ApiResult r, string okKey)
        {
            if (r.Ok)
            {
                CicCue.Ok(_body.position);
                _status(Trans.Get(okKey), false);
            }
            else
            {
                CicCue.Fail(_body.position);
                _status(ErrorText(r.Error), true);
            }
        }

        /// <summary>These actions answer raw codes (error:fleetMustBeDocked…): read them as keys.</summary>
        static string ErrorText(string e)
        {
            if (string.IsNullOrEmpty(e))
                return Trans.Get("vr.common.error");
            return e.IndexOf(' ') < 0 ? Trans.Get(e) : e;
        }

        // ── Widgets ───────────────────────────────────────────────────────────────

        TMP_Text Text(string text, float x, float y, float width, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var left = align is TextAlignmentOptions.MidlineLeft;
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(left ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            return t;
        }

        Button Btn(string label, float x, float y, float w, float h, Action act, DiegeticUi.BtnStyle style)
        {
            var b = DiegeticUi.HoloButton(_body, label, new Vector2(x, y), new Vector2(w, h), () => act(), style);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.richText = true;
            t.enableAutoSizing = true;
            t.fontSizeMin = 10f;
            t.fontSizeMax = Mathf.Min(18f, h * 0.42f);
            return b;
        }
    }
}
