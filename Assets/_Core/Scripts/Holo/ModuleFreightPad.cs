using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Stations;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Holo
{
    /// <summary>
    /// The module freight pad, the cargo pad's twin for ship modules: which hangar modules go up into the hold
    /// (LoadModules, <see cref="FocusFleet.ModuleFreightVolume"/> units each) or which carried ones go down into the
    /// planet's hangar (UnloadModules). One row per module type with its count, − / + and All, a page of six; the
    /// hold line says how many more fit. Also used by the stargate base to pick the modules a shipment carries
    /// (no hold limit there). <see cref="Ask"/> returns the ships ids, or null when cancelled.
    /// </summary>
    public sealed class ModuleFreightPad : MonoBehaviour
    {
        static ModuleFreightPad s_Instance;
        static readonly Vector2 Px = new(700f, 620f);
        static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        const int Rows = 6;

        GameObject _panel;
        TMP_Text _header;
        TMP_Text _hold;
        readonly TMP_Text[] _name = new TMP_Text[Rows];
        readonly TMP_Text[] _value = new TMP_Text[Rows];
        readonly GameObject[] _row = new GameObject[Rows];
        TMP_Text _page;
        /// <summary>Module type → the ids available of it (hangar or hold).</summary>
        readonly List<(string Type, List<int> Ids)> _types = new();
        readonly Dictionary<string, int> _picked = new();
        int _first;
        int _room;
        TaskCompletionSource<List<int>> _pending;

        /// <summary>
        /// Pick modules to move between <paramref name="fleet"/> and its orbited world <paramref name="planetId"/>:
        /// <paramref name="load"/> = from the hangar into the hold, else from the hold into the hangar.
        /// </summary>
        public static Task<List<int>> Ask(FocusFleet fleet, int planetId, string planetName, bool load)
        {
            if (fleet == null || planetId <= 0)
                return Task.FromResult<List<int>>(null);
            var source = load ? HangarModules(planetId) : new List<(int, string)>();
            if (!load)
                foreach (var m in fleet.CarriedModules)
                    source.Add((m.Id, m.Type));
            var room = load ? fleet.CargoFree / FocusFleet.ModuleFreightVolume : int.MaxValue;
            var title = Trans.Get(load ? "vr.freight.load" : "vr.freight.unload") + "  ·  " + planetName;
            var hold = load
                ? Trans.Format("vr.freight.holdLoad", fleet.CarriedModules.Count, room, FocusFleet.ModuleFreightVolume)
                : Trans.Format("vr.freight.holdUnload", fleet.CarriedModules.Count);
            return Instance().Open(title, hold, source, room);
        }

        /// <summary>The stargate base: modules of <paramref name="planetId"/>'s hangar to send with a shipment.</summary>
        public static Task<List<int>> AskHangar(int planetId, string title) =>
            Instance().Open(title, string.Empty, HangarModules(planetId), int.MaxValue);

        static ModuleFreightPad Instance()
        {
            if (s_Instance != null)
                return s_Instance;
            var go = new GameObject("ModuleFreightPad");
            s_Instance = go.AddComponent<ModuleFreightPad>();
            s_Instance.Build();
            return s_Instance;
        }

        /// <summary>Finished modules in the planet's hangar (GetResource.hangar: fleetid 0, endTime passed).</summary>
        public static List<(int Id, string Type)> HangarModules(int planetId)
        {
            var list = new List<(int, string)>();
            var eco = EconomyService.Instance;
            if (eco == null || !eco.TryGet(planetId, out var planet) || !(planet.Raw?["hangar"] is JArray rows))
                return list;
            var now = FleetOrderGate.UnixNow();
            foreach (var r in rows)
                if (r is JObject o && FocusContext.AsInt(o["fleetid"]) == 0 && FocusContext.AsLong(o["endTime"]) <= now)
                    list.Add((FocusContext.AsInt(o["id"]), FocusContext.AsString(o["type"])));
            return list;
        }

        Task<List<int>> Open(string title, string hold, List<(int Id, string Type)> source, int room)
        {
            _pending?.TrySetResult(null);
            _pending = new TaskCompletionSource<List<int>>();
            _types.Clear();
            _picked.Clear();
            _first = 0;
            _room = room;
            var index = new Dictionary<string, int>();
            foreach (var (id, type) in source)
            {
                if (!index.TryGetValue(type, out var i))
                {
                    index[type] = i = _types.Count;
                    _types.Add((type, new List<int>()));
                }

                _types[i].Ids.Add(id);
            }

            _types.Sort((a, b) => string.CompareOrdinal(Trans.Get(ModuleCatalog.NameKey(a.Type)), Trans.Get(ModuleCatalog.NameKey(b.Type))));
            _header.text = title;
            _hold.text = _types.Count == 0 ? Trans.Get("vr.freight.none") : hold;

            var cam = Camera.main;
            if (cam != null)
            {
                var fwd = cam.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-4f)
                    fwd = Vector3.forward;
                fwd.Normalize();
                var at = cam.transform.position + fwd * 0.55f + Vector3.down * 0.14f;
                _panel.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - cam.transform.position, Vector3.up));
                foreach (var t in _panel.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = cam.gameObject.layer;
            }

            Refresh();
            _panel.SetActive(true);
            CicCue.Ok(_panel.transform.position);
            return _pending.Task;
        }

        void Build()
        {
            _panel = new GameObject("ModuleFreightPanel");
            _panel.transform.SetParent(transform, false);
            var canvas = DiegeticUi.WorldCanvas(_panel.transform, "Canvas", Px, Vector3.zero, Quaternion.identity, 0.00072f);
            canvas.sortingOrder = 45;
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            frame.GetComponent<Image>().raycastTarget = false;
            _header = Label(frame, new Vector2(0f, 270f), new Vector2(640f, 40f), 25f, UiKit.Cyan);
            _header.fontStyle = FontStyles.Bold;
            _hold = Label(frame, new Vector2(0f, 232f), new Vector2(640f, 28f), 17f, UiKit.TextBright);

            for (var r = 0; r < Rows; r++)
            {
                var row = r;
                var y = 175f - r * 62f;
                var holder = new GameObject("Row" + r, typeof(RectTransform));
                holder.transform.SetParent(frame, false);
                _row[r] = holder;
                _name[r] = Label(holder.transform, new Vector2(-185f, y), new Vector2(290f, 54f), 18f, UiKit.TextBright,
                    TextAlignmentOptions.MidlineLeft);
                Key(holder.transform, "−", new Vector2(10f, y), new Vector2(58f, 52f), () => Step(row, -1), DiegeticUi.BtnStyle.Cyan);
                _value[r] = Label(holder.transform, new Vector2(85f, y), new Vector2(80f, 52f), 24f, UiKit.Amber);
                _value[r].fontStyle = FontStyles.Bold;
                Key(holder.transform, "+", new Vector2(160f, y), new Vector2(58f, 52f), () => Step(row, 1), DiegeticUi.BtnStyle.Cyan);
                Key(holder.transform, Trans.Get("vr.cargo.max"), new Vector2(250f, y), new Vector2(100f, 52f), () => Step(row, int.MaxValue),
                    DiegeticUi.BtnStyle.Ghost);
            }

            Key(frame, "‹", new Vector2(-150f, -205f), new Vector2(64f, 48f), () => Page(-1), DiegeticUi.BtnStyle.Ghost);
            _page = Label(frame, new Vector2(0f, -205f), new Vector2(200f, 40f), 17f, UiKit.TextDim);
            Key(frame, "›", new Vector2(150f, -205f), new Vector2(64f, 48f), () => Page(1), DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(frame, Trans.Get("cancel"), new Vector2(-150f, -270f), new Vector2(260f, 58f), () => Close(null),
                DiegeticUi.BtnStyle.Ghost);
            DiegeticUi.HoloButton(frame, Trans.Get("validate"), new Vector2(150f, -270f), new Vector2(260f, 58f), Validate,
                DiegeticUi.BtnStyle.Amber);
            _panel.SetActive(false);
        }

        static TMP_Text Label(Transform parent, Vector2 pos, Vector2 size, float font, Color c,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = DiegeticUi.HoloLabel(parent, string.Empty, pos, size, font, c, align);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void Key(Transform parent, string label, Vector2 pos, Vector2 size, System.Action act, DiegeticUi.BtnStyle style)
        {
            var b = DiegeticUi.HoloButton(parent, label, pos, size, () => act(), style);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        int Total()
        {
            var n = 0;
            foreach (var kv in _picked)
                n += kv.Value;
            return n;
        }

        void Step(int row, int delta)
        {
            var i = _first + row;
            if (i >= _types.Count)
                return;
            var (type, ids) = _types[i];
            _picked.TryGetValue(type, out var have);
            var others = Total() - have;
            var cap = Mathf.Min(ids.Count, Mathf.Max(0, _room - others));
            var next = delta == int.MaxValue ? cap : Mathf.Clamp(have + delta, 0, cap);
            if (next == have && delta > 0)
                CicCue.Fail(_panel.transform.position);
            _picked[type] = next;
            Refresh();
        }

        void Page(int delta)
        {
            var pages = Mathf.Max(1, Mathf.CeilToInt(_types.Count / (float)Rows));
            _first = Mathf.Clamp(_first / Rows + delta, 0, pages - 1) * Rows;
            Refresh();
        }

        void Validate()
        {
            var ids = new List<int>();
            foreach (var (type, list) in _types)
                if (_picked.TryGetValue(type, out var n))
                    for (var k = 0; k < n && k < list.Count; k++)
                        ids.Add(list[k]);
            if (ids.Count == 0)
            {
                CicCue.Fail(_panel.transform.position);
                return;
            }

            Close(ids);
        }

        void Close(List<int> result)
        {
            _panel.SetActive(false);
            var p = _pending;
            _pending = null;
            p?.TrySetResult(result);
        }

        void Refresh()
        {
            for (var r = 0; r < Rows; r++)
            {
                var i = _first + r;
                var on = i < _types.Count;
                _row[r].SetActive(on);
                if (!on)
                    continue;
                var (type, ids) = _types[i];
                _picked.TryGetValue(type, out var n);
                var fam = ModuleCatalog.Family(type);
                _name[r].text = "<color=#" + ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(fam)) + ">■</color> " +
                                Trans.Get(ModuleCatalog.NameKey(type)) + "  <size=80%><color=#9fdcff>×" + ids.Count.ToString(Fr) + "</color></size>";
                _value[r].text = n.ToString(Fr);
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(_types.Count / (float)Rows));
            _page.text = (_first / Rows + 1) + " / " + pages;
        }

        void OnDisable()
        {
            _pending?.TrySetResult(null);
            _pending = null;
        }
    }
}
