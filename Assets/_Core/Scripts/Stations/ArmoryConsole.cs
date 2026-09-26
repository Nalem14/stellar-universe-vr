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
    /// Tactical station console — the web "Base Militaire" tab (scenes/planet.js _renderBaseMilitaire) plus
    /// the fleet troop-bay popups (objects/planet.js loadTroopsFor / unloadTroopsFor) and battle
    /// reinforcement (objects/fleet.js reinforceBattle), on one holo screen the Tactical officer brings up.
    /// Garrison: Academy batches (RecruitTroop, qty 1–500, one batch per planet at a time).
    /// Defences: Defense Factory batches (BuildDefenseUnit). Troop bay: a docked ship of ours loads from /
    /// unloads to the garrison (LoadTroops / UnloadTroops, {type: qty}). Operations: our sieges and the
    /// battles we are in (GetMyBattles) — rejoin on the table, reinforce a pending one (AddFleetToBattle).
    /// </summary>
    public sealed class ArmoryConsole : MonoBehaviour
    {
        enum Tab
        {
            Garrison,
            Defenses,
            Bay,
            Operations
        }

        static readonly Vector2 Size = new(1.1f, 0.7f);
        static readonly Color Accent = new(1f, 0.42f, 0.32f, 1f);
        const float Reach = 1.25f;
        const float MaxBearing = 20f;
        const float BattlesEvery = 5f;
        static readonly int[] Steps = { -100, -10, -1, 1, 10, 100 };

        public static ArmoryConsole Instance { get; private set; }
        public bool IsOpen => _open;

        HoloScreen _screen;
        RectTransform _frame;
        RectTransform _body;
        TMP_Text _planetLabel;
        TMP_Text _status;
        readonly TMP_Text[] _chips = new TMP_Text[5];
        readonly Button[] _tabs = new Button[4];
        readonly List<(TMP_Text Text, Func<string> Value)> _live = new();
        readonly List<(Image Fill, Func<float> Value)> _bars = new();
        readonly List<GalaxyCatalog.PlanetRef> _owned = new();
        readonly List<FocusFleet> _docked = new();

        EconomyService _eco;
        FocusContext _focus;
        FleetPoller _poller;
        HexBattleController _hex;
        Transform _anchor;
        bool _open;
        int _planetId;
        int _shipId;
        Tab _tab;
        int _qty = 10;
        float _nextLive;
        bool _busy;

        JArray _battles;
        float _battlesAt;
        bool _battlesLoading;

        public static ArmoryConsole Build(Transform room, EconomyService eco, FocusContext focus, FleetPoller poller,
            HexBattleController hex)
        {
            var rig = new GameObject("ArmoryConsoleRig").transform;
            rig.SetParent(room, false);
            var console = rig.gameObject.AddComponent<ArmoryConsole>();
            console._eco = eco;
            console._focus = focus;
            console._poller = poller;
            console._hex = hex;
            console.BuildScreen(rig);
            rig.gameObject.SetActive(false);
            return console;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            Unhook();
            if (Instance == this)
                Instance = null;
        }

        void Hook()
        {
            Unhook();
            if (_eco != null)
                _eco.Changed += OnData;
            if (_focus != null)
                _focus.FleetsChanged += OnData;
            if (SiegeWatch.Instance != null)
                SiegeWatch.Instance.Changed += OnData;
        }

        void Unhook()
        {
            if (_eco != null)
                _eco.Changed -= OnData;
            if (_focus != null)
                _focus.FleetsChanged -= OnData;
            if (SiegeWatch.Instance != null)
                SiegeWatch.Instance.Changed -= OnData;
        }

        // ── Shell ──────────────────────────────────────────────────────────────────

        void BuildScreen(Transform rig)
        {
            _screen = HoloScreen.Create(rig, "ArmoryConsole", Size, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.armory.title"));
            _screen.SetAccent(Accent, 0.5f);
            _frame = _screen.Content;

            Button(_frame, "‹", new Vector2(-505f, 245f), new Vector2(64f, 46f), () => StepPlanet(-1),
                DiegeticUi.BtnStyle.Ghost);
            _planetLabel = DiegeticUi.HoloLabel(_frame, string.Empty, new Vector2(-195f, 245f),
                new Vector2(540f, 46f), 26f, UiKit.TextBright);
            _planetLabel.fontStyle = FontStyles.Bold;
            _planetLabel.enableAutoSizing = true;
            _planetLabel.fontSizeMin = 18f;
            _planetLabel.fontSizeMax = 26f;
            Button(_frame, "›", new Vector2(115f, 245f), new Vector2(64f, 46f), () => StepPlanet(1),
                DiegeticUi.BtnStyle.Ghost);
            Button(_frame, Trans.Get("close"), new Vector2(465f, 245f), new Vector2(140f, 46f), Close,
                DiegeticUi.BtnStyle.Ghost);

            for (var i = 0; i < _chips.Length; i++)
            {
                var chip = DiegeticUi.HoloSelectTray(_frame, new Vector2(-440f + i * 220f, 186f), new Vector2(208f, 58f));
                _chips[i] = DiegeticUi.HoloLabel(chip, string.Empty, Vector2.zero, new Vector2(196f, 54f), 17f,
                    UiKit.TextBright);
                _chips[i].lineSpacing = -12f;
            }

            var tabKeys = new[] { "garrison", "vr.armory.tab.defenses", "vr.armory.tab.bay", "vr.armory.tab.ops" };
            for (var i = 0; i < _tabs.Length; i++)
            {
                var tab = (Tab)i;
                _tabs[i] = Button(_frame, Trans.Get(tabKeys[i]), new Vector2(-405f + i * 270f, 128f),
                    new Vector2(258f, 46f), () => SetTab(tab), DiegeticUi.BtnStyle.Ghost);
            }

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            bodyGo.transform.SetParent(_frame, false);
            _body = bodyGo.GetComponent<RectTransform>();
            _body.sizeDelta = new Vector2(1060f, 420f);

            _status = DiegeticUi.HoloLabel(_frame, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f),
                18f, DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);
        }

        static Button Button(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick,
            DiegeticUi.BtnStyle style) =>
            DiegeticUi.HoloButton(parent, label, pos, size, () => onClick?.Invoke(), style);

        // ── Open / close ───────────────────────────────────────────────────────────

        /// <summary>
        /// Bring the console up on the Tactical officer's bearing. <paramref name="preferredPlanet"/>: the
        /// world in orbit or the station's planet when it is ours; <paramref name="preferredShip"/>: the ship
        /// we are aboard (troop bay).
        /// </summary>
        public void Open(Transform officerAnchor, int preferredPlanet, int preferredShip)
        {
            _anchor = officerAnchor;
            _open = true;
            gameObject.SetActive(true);
            Place();
            Hook();
            _eco.CollectOwned(_owned);
            _planetId = PickPlanet(preferredPlanet);
            _shipId = preferredShip;
            _tab = SiegeOrBattleNow() ? Tab.Operations : Tab.Garrison;
            _battles = null;
            SetStatus(string.Empty);
            Render();
            CicCue.Ok(transform.position);
            Run(_eco.RefreshNow());
        }

        public void Close()
        {
            if (_open && _anchor != null)
                _anchor.GetComponentInParent<CrewOfficer>()?.LookAt(null);
            _open = false;
            Unhook();
            gameObject.SetActive(false);
        }

        bool SiegeOrBattleNow()
        {
            if (SiegeWatch.Instance != null && SiegeWatch.Instance.Sieges.Count > 0)
                return true;
            if (_focus == null)
                return false;
            foreach (var f in _focus.Fleets)
                if (f.IsInBattle && _focus.IsMine(f))
                    return true;
            return false;
        }

        int PickPlanet(int preferred)
        {
            foreach (var p in _owned)
                if (p.Id == preferred)
                    return preferred;
            foreach (var p in _owned)
                if (p.Id == _planetId)
                    return _planetId;
            return _owned.Count > 0 ? _owned[0].Id : 0;
        }

        void Place()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            var eye = cam.transform.position;
            var look = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
            if (look.sqrMagnitude < 0.01f)
                look = Vector3.forward;
            look.Normalize();
            var dir = look;
            if (_anchor != null)
            {
                var toOfficer = Vector3.ProjectOnPlane(_anchor.position - eye, Vector3.up);
                if (toOfficer.sqrMagnitude > 0.01f)
                {
                    var angle = Mathf.Clamp(Vector3.SignedAngle(look, toOfficer.normalized, Vector3.up), -MaxBearing,
                        MaxBearing);
                    dir = Quaternion.AngleAxis(angle, Vector3.up) * look;
                }
            }

            var p = eye + dir * Reach;
            // Above the table rim (its buttons would otherwise cut the lower rows).
            p.y = eye.y - 0.06f;
            transform.position = p;
            ScreenMount.FaceViewer(transform, eye, 1f, 6f);
        }

        void OnData()
        {
            if (_open && !_busy)
                Render();
        }

        void StepPlanet(int delta)
        {
            if (_owned.Count == 0)
                return;
            var i = _owned.FindIndex(p => p.Id == _planetId);
            i = ((i < 0 ? 0 : i) + delta + _owned.Count) % _owned.Count;
            _planetId = _owned[i].Id;
            _shipId = 0;
            SetStatus(string.Empty);
            Render();
        }

        void SetTab(Tab tab)
        {
            _tab = tab;
            SetStatus(string.Empty);
            if (tab == Tab.Operations)
                _battles = null;
            Render();
        }

        void SetStatus(string text, bool error = false)
        {
            _status.text = text ?? string.Empty;
            _status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        static void Run(Task task) => AsyncTap.Run(task);

        // ── Render ────────────────────────────────────────────────────────────────

        void Render()
        {
            _live.Clear();
            _bars.Clear();
            for (var i = _body.childCount - 1; i >= 0; i--)
                DestroyImmediate(_body.GetChild(i).gameObject);

            for (var i = 0; i < _tabs.Length; i++)
                SetButtonStyle(_tabs[i], (Tab)i == _tab);

            _eco.TryGet(_planetId, out var planet);
            var star = GalaxyCatalog.TryGet(planet?.SystemId ?? 0, out var s) ? s.Label : string.Empty;
            var name = planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + _planetId;
            _planetLabel.text = _planetId <= 0
                ? Trans.Get("vr.ops.noPlanet")
                : name + "   <size=70%><color=#7fd8ff>" + star + "</color></size>";
            RenderChips(planet);

            if (_tab == Tab.Operations)
            {
                RenderOperations();
                return;
            }

            if (planet == null)
            {
                Line(Trans.Get(_planetId <= 0 ? "vr.ops.noPlanet" : "Loading"), 0f, 60f, 22f, DiegeticUi.CyanDim);
                return;
            }

            switch (_tab)
            {
                case Tab.Garrison:
                    RenderUnits(planet, TroopCatalog.Troops, true);
                    break;
                case Tab.Defenses:
                    RenderUnits(planet, TroopCatalog.Defenses, false);
                    break;
                case Tab.Bay:
                    RenderBay(planet);
                    break;
            }
        }

        static void SetButtonStyle(Button b, bool active)
        {
            var label = b.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = active ? UiKit.Cyan : new Color(0.7f, 0.85f, 0.92f, 0.8f);
            var img = b.GetComponent<Image>();
            if (img != null)
                img.color = active ? new Color(0.6f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.55f);
        }

        void RenderChips(PlanetEconomy p)
        {
            if (p == null)
            {
                foreach (var c in _chips)
                    c.text = string.Empty;
                return;
            }

            _chips[0].text = Head("vr.res.mineral") + Num(p.Mineral);
            _chips[1].text = Head("vr.res.crystal") + Num(p.Crystal);
            _chips[2].text = Head("vr.res.biomass") + Num(p.Biomass);
            _chips[3].text = Head("garrison") + Num(TroopCatalog.Total(TroopCatalog.Counts(p.Raw?["troops"]))) +
                             "   <size=75%>" + Trans.Get("academy") + " " +
                             TroopCatalog.BuiltLevel(p, "academy") + "</size>";
            _chips[4].text = Head("vr.armory.tab.defenses") +
                             Num(TroopCatalog.Total(TroopCatalog.Counts(p.Raw?["defenseUnits"]))) +
                             "   <size=75%>" + Trans.Get("level") + " " +
                             TroopCatalog.BuiltLevel(p, "defenseFactory") + "</size>";
        }

        static string Head(string key) => "<size=75%><color=#7fd8ff>" + Trans.Get(key) + "</color></size>\n";

        static string Num(float v) => Mathf.RoundToInt(v).ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

        TMP_Text Line(string text, float x, float y, float size, Color color, float width = 1000f,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = DiegeticUi.HoloLabel(_body, text, new Vector2(x, y), new Vector2(width, size * 1.9f), size, color,
                align);
            t.richText = true;
            return t;
        }

        void AccentBar(float y, Color color)
        {
            var bar = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(_body, false);
            var rt = bar.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(8f, 42f);
            rt.anchoredPosition = new Vector2(-522f, y);
            var img = bar.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
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

        /// <summary>Shared batch size (web: one number field per card) — −100 … +100 around the count; ↥ on a card = its max.</summary>
        void Stepper(float y)
        {
            Line(Trans.Get("vr.armory.qty"), -420f, y, 19f, DiegeticUi.CyanDim, 180f, TextAlignmentOptions.MidlineLeft);
            for (var i = 0; i < Steps.Length; i++)
            {
                var step = Steps[i];
                var x = -250f + i * 88f + (i >= 3 ? 110f : 0f);
                DiegeticUi.HoloButton(_body, (step > 0 ? "+" : "−") + Mathf.Abs(step), new Vector2(x, y),
                    new Vector2(80f, 42f), () =>
                    {
                        _qty = Mathf.Clamp(_qty + step, 1, TroopCatalog.MaxBatch);
                        Render();
                    }, DiegeticUi.BtnStyle.Ghost);
            }

            Line("<b>" + _qty + "</b>", 25f, y, 26f, UiKit.TextBright, 100f);
        }

        // ── Garrison / defences ───────────────────────────────────────────────────

        void RenderUnits(PlanetEconomy planet, IReadOnlyList<UnitDef> units, bool troops)
        {
            var building = troops ? "academy" : "defenseFactory";
            var built = TroopCatalog.BuiltLevel(planet, building);
            var level = planet.Level(building);
            var counts = TroopCatalog.Counts(planet.Raw?[troops ? "troops" : "defenseUnits"]);

            if (built <= 0)
            {
                Line(Trans.Get(troops ? "noAcademy" : "noDefenseFactory"), 0f, 40f, 22f, UiKit.Amber);
                Line(OwnedLine(counts), 0f, -20f, 19f, DiegeticUi.CyanDim);
                return;
            }

            var raw = planet.Raw;
            var working = FocusContext.AsLong(raw?[troops ? "troopWorking" : "defenseWorking"]);
            if (working > FleetOrderGate.UnixNow())
            {
                RenderBatch(raw, units, troops, working);
                Line(OwnedLine(counts), 0f, -150f, 19f, DiegeticUi.CyanDim);
                return;
            }

            var computer = _eco.ResearchLevel("computer");
            Stepper(82f);

            for (var i = 0; i < units.Count && i < 6; i++)
            {
                var def = units[i];
                var y = 24f - i * 50f;
                var locked = def.Level > level;
                var q = TroopCatalog.Quote(planet, def, _qty, computer);
                AccentBar(y, locked ? new Color(0.5f, 0.6f, 0.65f, 0.35f) : Accent);

                counts.TryGetValue(def.Type, out var have);
                var stats = troops
                    ? Trans.Format("vr.armory.stats", def.Attack, def.Defense)
                    : Trans.Format("vr.armory.defStats", def.Defense);
                var owned = have > 0 ? "  <color=#4dffa0>×" + Num(have) + "</color>" : string.Empty;
                Line("<b>" + Trans.Get(def.Type) + "</b>" + owned + "\n<size=72%><color=#7fd8ff>" + stats +
                     "</color></size>", -330f, y, 19f, locked ? UiKit.TextDim : UiKit.TextBright, 360f,
                    TextAlignmentOptions.MidlineLeft).lineSpacing = -18f;

                if (locked)
                {
                    Line(Trans.Format("vr.armory.requires", Trans.Get(def.Building), def.Level), 170f, y, 17f,
                        UiKit.Amber, 560f);
                    continue;
                }

                var cost = Line(CostText(q), 70f, y, 16f,
                    q.Affordable ? new Color(0.78f, 0.9f, 0.96f, 1f) : UiKit.Danger, 400f, TextAlignmentOptions.MidlineLeft);
                cost.enableAutoSizing = true;
                cost.fontSizeMin = 11f;
                cost.fontSizeMax = 16f;
                cost.textWrappingMode = TextWrappingModes.NoWrap;
                var type = def.Type;
                var go = DiegeticUi.HoloButton(_body,
                    Trans.Get(troops ? "vr.armory.train" : "vr.armory.build") + " ×" + _qty, new Vector2(380f, y),
                    new Vector2(210f, 44f), () => Run(Order(troops, type)),
                    q.Affordable ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                go.interactable = q.Affordable;
                DiegeticUi.HoloButton(_body, "»", new Vector2(505f, y), new Vector2(44f, 44f), () =>
                {
                    _qty = Mathf.Clamp(q.MaxQty, 1, TroopCatalog.MaxBatch);
                    SetStatus(Trans.Format("vr.armory.max", q.MaxQty));
                    Render();
                }, DiegeticUi.BtnStyle.Ghost);
            }
        }

        string OwnedLine(Dictionary<string, int> counts)
        {
            if (counts.Count == 0)
                return Trans.Get(_tab == Tab.Garrison ? "vr.armory.noTroop" : "vr.armory.noDefense");
            var parts = new List<string>();
            foreach (var kv in counts)
                parts.Add("<b>" + Num(kv.Value) + "</b> " + Trans.Get(kv.Key));
            return string.Join("   ·   ", parts);
        }

        void RenderBatch(JObject raw, IReadOnlyList<UnitDef> units, bool troops, long end)
        {
            var type = FocusContext.AsString(raw[troops ? "troopWorkingType" : "defenseWorkingType"]);
            var qty = FocusContext.AsInt(raw[troops ? "troopWorkingQty" : "defenseWorkingQty"]);
            UnitDef def = null;
            foreach (var u in units)
                if (u.Type == type)
                    def = u;
            var duration = def != null
                ? def.Time * qty * (100f - (_eco.ResearchLevel("computer") + 1)) / 100f
                : 0f;

            Line(Trans.Format(troops ? "vr.armory.training" : "vr.armory.building", qty, Trans.Get(type)), 0f, 40f, 24f,
                UiKit.Amber);
            var remain = Line(string.Empty, 0f, 0f, 20f, DiegeticUi.CyanDim, 520f);
            _live.Add((remain, () => Core.Holo.TravelPlanner.TimeText(end - FleetOrderGate.UnixNow())));
            Bar(new Vector2(0f, -36f), new Vector2(620f, 10f),
                () => duration <= 0f ? 0f : 1f - Mathf.Clamp01((end - FleetOrderGate.UnixNow()) / duration));
        }

        static string CostText(in UnitQuote q)
        {
            var parts = new List<string>(4);
            if (q.Mineral > 0)
                parts.Add(Trans.Get("vr.res.mineral") + " " + Num(q.Mineral));
            if (q.Crystal > 0)
                parts.Add(Trans.Get("vr.res.crystal") + " " + Num(q.Crystal));
            if (q.Biomass > 0)
                parts.Add(Trans.Get("vr.res.biomass") + " " + Num(q.Biomass));
            parts.Add(Core.Holo.TravelPlanner.TimeText(q.Seconds));
            return string.Join("  ·  ", parts);
        }

        async Task Order(bool troops, string type)
        {
            if (_busy)
                return;
            _busy = true;
            var action = troops ? "RecruitTroop" : "BuildDefenseUnit";
            try
            {
                SetStatus(Trans.Get("Loading"));
                var result = await ActionJs.Get(action, new Dictionary<string, string>
                {
                    { "planet", _planetId.ToString() },
                    { "type", type },
                    { "qty", _qty.ToString() }
                });
                var (ok, message) = Interpret(result,
                    troops ? "troopRecruitmentStarted" : "defenseUnitBuildStarted");
                Feedback(ok, _qty + " × " + Trans.Get(type) + " · " + message);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Tactical, action, result,
                    Trans.Get(type));
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
        }

        // ── Troop bay ─────────────────────────────────────────────────────────────

        void CollectDocked()
        {
            _docked.Clear();
            if (_focus == null)
                return;
            var now = FleetOrderGate.UnixNow();
            foreach (var f in _focus.Fleets)
            {
                if (!_focus.IsMine(f) || f.PlanetId != _planetId || f.IsMoving(now))
                    continue;
                _docked.Add(f);
            }

            // Ships with a Troop Bay first, then by id (stable).
            _docked.Sort((a, b) => a.TroopCargo > 0 != b.TroopCargo > 0 ? (a.TroopCargo > 0 ? -1 : 1) : a.Id.CompareTo(b.Id));
        }

        void RenderBay(PlanetEconomy planet)
        {
            CollectDocked();
            if (_docked.Count == 0)
            {
                Line(Trans.Get("vr.armory.noShip"), 0f, 40f, 22f, DiegeticUi.CyanDim);
                return;
            }

            var ship = _docked.Find(f => f.Id == _shipId) ?? _docked[0];
            _shipId = ship.Id;

            if (_docked.Count > 1)
            {
                DiegeticUi.HoloButton(_body, "‹", new Vector2(-505f, 82f), new Vector2(56f, 42f), () => StepShip(-1),
                    DiegeticUi.BtnStyle.Ghost);
                DiegeticUi.HoloButton(_body, "›", new Vector2(-150f, 82f), new Vector2(56f, 42f), () => StepShip(1),
                    DiegeticUi.BtnStyle.Ghost);
            }

            var shipName = string.IsNullOrEmpty(ship.Name) ? "#" + ship.Id : ship.Name;
            Line("<b>" + shipName + "</b>", -328f, 82f, 21f, UiKit.TextBright, 290f);
            var used = ship.TroopsAboard;
            Line(Trans.Format("vr.armory.bay", Num(used), Num(ship.TroopCargo)), 20f, 82f, 19f,
                ship.TroopCargo > 0 && used >= ship.TroopCargo ? UiKit.Amber : DiegeticUi.CyanDim, 300f,
                TextAlignmentOptions.MidlineLeft);
            Bar(new Vector2(20f + 150f, 62f), new Vector2(300f, 6f),
                () => ship.TroopCargo <= 0 ? 0f : Mathf.Clamp01(ship.TroopsAboard / (float)ship.TroopCargo));

            if (ship.TroopCargo <= 0 && used <= 0)
            {
                Line(Trans.Get("vr.armory.noBay"), 0f, -10f, 21f, UiKit.Amber);
                return;
            }

            var garrison = TroopCatalog.Counts(planet.Raw?["troops"]);
            var free = Mathf.Max(0, ship.TroopCargo - used);
            var rows = 0;
            foreach (var def in TroopCatalog.Troops)
            {
                garrison.TryGetValue(def.Type, out var ground);
                ship.Troops.TryGetValue(def.Type, out var aboard);
                if (ground <= 0 && aboard <= 0)
                    continue;
                var y = 20f - rows * 50f;
                rows++;
                if (rows > 5)
                    break;
                AccentBar(y, Accent);
                Line("<b>" + Trans.Get(def.Type) + "</b>", -360f, y, 19f, UiKit.TextBright, 300f,
                    TextAlignmentOptions.MidlineLeft);
                Line(Trans.Format("vr.armory.ground", Num(ground)) + "     " + Trans.Format("vr.armory.aboard", Num(aboard)),
                    -30f, y, 17f, DiegeticUi.CyanDim, 300f, TextAlignmentOptions.MidlineLeft);

                var type = def.Type;
                var load = Mathf.Min(ground, free);
                var up = DiegeticUi.HoloButton(_body, Trans.Get("loadTroops") + " " + Num(load),
                    new Vector2(290f, y), new Vector2(190f, 44f),
                    () => Run(Transfer(ship.Id, true, new Dictionary<string, int> { { type, load } })),
                    load > 0 ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                up.interactable = load > 0;
                var down = DiegeticUi.HoloButton(_body, Trans.Get("unloadTroops") + " " + Num(aboard),
                    new Vector2(470f, y), new Vector2(160f, 44f),
                    () => Run(Transfer(ship.Id, false, new Dictionary<string, int> { { type, aboard } })),
                    aboard > 0 ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
                down.interactable = aboard > 0;
            }

            if (rows == 0)
            {
                Line(Trans.Get("vr.armory.noTroop"), 0f, -10f, 21f, DiegeticUi.CyanDim);
                return;
            }

            // Everything at once: the server fills the bay type by type (LoadTroops stops at freeSpace).
            if (free > 0 && TroopCatalog.Total(garrison) > 0)
                DiegeticUi.HoloButton(_body, Trans.Get("vr.armory.loadAll"), new Vector2(290f, -245f),
                    new Vector2(190f, 44f), () => Run(Transfer(ship.Id, true, garrison)), DiegeticUi.BtnStyle.Cyan);
            if (used > 0)
                DiegeticUi.HoloButton(_body, Trans.Get("vr.armory.unloadAll"), new Vector2(470f, -245f),
                    new Vector2(160f, 44f), () => Run(Transfer(ship.Id, false, new Dictionary<string, int>(ship.Troops))),
                    DiegeticUi.BtnStyle.Amber);
        }

        void StepShip(int delta)
        {
            if (_docked.Count == 0)
                return;
            var i = _docked.FindIndex(f => f.Id == _shipId);
            i = ((i < 0 ? 0 : i) + delta + _docked.Count) % _docked.Count;
            _shipId = _docked[i].Id;
            Render();
        }

        async Task Transfer(int fleetId, bool load, Dictionary<string, int> troops)
        {
            if (_busy || troops.Count == 0)
                return;
            _busy = true;
            var action = load ? "LoadTroops" : "UnloadTroops";
            try
            {
                var map = new JObject();
                foreach (var kv in troops)
                    if (kv.Value > 0)
                        map[kv.Key] = kv.Value;
                SetStatus(Trans.Get("Loading"));
                var result = await ActionJs.Get(action, new Dictionary<string, string>
                {
                    { "fleet", fleetId.ToString() },
                    { "planet", _planetId.ToString() },
                    { "troops", map.ToString(Newtonsoft.Json.Formatting.None) }
                });
                var (ok, message) = Interpret(result, load ? "troopsLoaded" : "troopsUnloaded");
                var moved = ok ? MovedUnits(result.Body) : 0;
                Feedback(ok, ok ? message + " · " + Num(moved) : message);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Tactical, action, result, string.Empty);
                if (ok && moved > 0)
                    CombatEvents.RaiseTroopTransfer(fleetId, _planetId, !load, moved);
            }
            finally
            {
                _busy = false;
            }

            if (_poller != null)
                await _poller.PollNow();
            await _eco.RefreshNow();
        }

        /// <summary>LoadTroops / UnloadTroops answer the {type: qty} actually moved.</summary>
        static int MovedUnits(string body)
        {
            if (string.IsNullOrEmpty(body) || body[0] != '{')
                return 0;
            try
            {
                var n = 0;
                foreach (var kv in JObject.Parse(body))
                    n += FocusContext.AsInt(kv.Value);
                return n;
            }
            catch
            {
                return 0;
            }
        }

        // ── Operations: sieges and battles ────────────────────────────────────────

        void RenderOperations()
        {
            var y = 82f;
            var sieges = SiegeWatch.Instance != null ? SiegeWatch.Instance.Sieges : null;
            if (sieges != null)
            {
                foreach (var s in sieges)
                {
                    if (y < -240f)
                        break;
                    RenderSiege(s, y);
                    y -= 54f;
                }
            }

            if (_battles == null || Time.unscaledTime - _battlesAt > BattlesEvery)
            {
                if (!_battlesLoading)
                    Run(LoadBattles());
            }

            var shown = 0;
            if (_battles != null)
            {
                foreach (var b in _battles)
                {
                    if (y < -240f)
                        break;
                    y = RenderBattle(b, y);
                    shown++;
                }
            }

            if (shown == 0 && (sieges == null || sieges.Count == 0))
                Line(Trans.Get(_battles == null ? "Loading" : "vr.armory.noOps"), 0f, 20f, 22f, DiegeticUi.CyanDim);
        }

        void RenderSiege(Siege s, float y)
        {
            DiegeticUi.HoloSelectTray(_body, new Vector2(0f, y), new Vector2(1060f, 48f));
            AccentBar(y, s.OurAttack ? UiKit.Amber : UiKit.Danger);
            var star = GalaxyCatalog.Label(s.SystemId);
            Line(Trans.Format(s.OurAttack ? "vr.armory.siegeOurs" : "vr.armory.siegeTheirs", s.PlanetName) +
                 "   <size=75%><color=#7fd8ff>" + star + "</color></size>", -250f, y, 19f, UiKit.TextBright, 520f,
                TextAlignmentOptions.MidlineLeft);
            var end = s.EndTime;
            var t = Line(string.Empty, 250f, y, 18f, s.OurAttack ? UiKit.Amber : UiKit.Danger, 480f,
                TextAlignmentOptions.MidlineRight);
            _live.Add((t, () =>
            {
                var left = end - FleetOrderGate.UnixNow();
                return left > 0
                    ? Trans.Format("vr.armory.siegeLands", Core.Holo.TravelPlanner.TimeText(left))
                    : Trans.Get("vr.armory.siegeResolving");
            }));
        }

        float RenderBattle(JToken b, float y)
        {
            var id = FocusContext.AsInt(b["id"]);
            var myFleet = FocusContext.AsInt(b["myFleetId"]);
            var system = FocusContext.AsInt(b["systemid"]);
            var planet = FocusContext.AsInt(b["planetid"]);
            var pending = FocusContext.AsString(b["state"]) == "0";
            var me = _focus?.FindFleet(myFleet);
            var where = planet > 0 && GalaxyCatalog.TryGetPlanet(planet, out var pr) && !string.IsNullOrEmpty(pr.Name)
                ? pr.Name + " · " + GalaxyCatalog.Label(system)
                : GalaxyCatalog.Label(system);

            DiegeticUi.HoloSelectTray(_body, new Vector2(0f, y), new Vector2(1060f, 48f));
            AccentBar(y, pending ? UiKit.Amber : UiKit.Danger);
            var shipName = b["nearby"] != null ? Trans.Get("vr.armory.nearby")
                : me != null && !string.IsNullOrEmpty(me.Name) ? me.Name : "#" + myFleet;
            Line(Trans.Format(pending ? "vr.armory.pending" : "vr.armory.active", where) +
                 "   <size=75%><color=#7fd8ff>" + shipName + "</color></size>", -230f, y, 19f, UiKit.TextBright, 560f,
                TextAlignmentOptions.MidlineLeft);
            if (_hex != null && myFleet > 0)
                DiegeticUi.HoloButton(_body, Trans.Get("vr.battle.rejoin"), new Vector2(420f, y),
                    new Vector2(200f, 42f), () =>
                    {
                        _hex.Open(id, myFleet);
                        Close();
                    }, DiegeticUi.BtnStyle.Danger);
            y -= 54f;

            // Reinforcements only while the fight is being set up (server: battleAlreadyStarted otherwise).
            if (!pending || _focus == null)
                return y;
            var now = FleetOrderGate.UnixNow();
            var offered = 0;
            foreach (var f in _focus.Fleets)
            {
                if (offered >= 3 || y < -240f)
                    break;
                // Any of our idle ships in the same system (the web offers it from the fighting ship's menu).
                if (!_focus.IsMine(f) || f.IsInBattle || f.Id == myFleet || f.SystemId != system || f.IsMoving(now))
                    continue;
                var fleet = f;
                var label = Trans.Get("reinforceBattle") + "  ·  " + (string.IsNullOrEmpty(f.Name) ? "#" + f.Id : f.Name);
                DiegeticUi.HoloButton(_body, label, new Vector2(120f, y), new Vector2(560f, 42f),
                    () => Run(Reinforce(id, fleet)), DiegeticUi.BtnStyle.Cyan);
                y -= 48f;
                offered++;
            }

            return y;
        }

        async Task LoadBattles()
        {
            _battlesLoading = true;
            try
            {
                var res = await ActionJs.Get("GetMyBattles");
                _battlesAt = Time.unscaledTime;
                var list = new JArray();
                if (res.Ok && !string.IsNullOrEmpty(res.Body))
                {
                    try
                    {
                        if (JToken.Parse(res.Body) is JArray arr)
                            list = arr;
                    }
                    catch
                    {
                        // Keep the empty list.
                    }
                }

                await AddNearbyPending(list);
                _battles = list;
            }
            finally
            {
                _battlesLoading = false;
            }

            if (_open && _tab == Tab.Operations && !_busy)
                Render();
        }

        /// <summary>
        /// Battles forming in the system in view that we are not part of (GetPendingBattles): open space
        /// (planetid 0, pirates) and the worlds that matter here — ours and the one our ship orbits. Our idle
        /// ships there can reinforce while it is being set up. At most four reads per refresh.
        /// </summary>
        async Task AddNearbyPending(JArray list)
        {
            var sys = _focus != null ? _focus.SystemId : 0;
            if (sys <= 0)
                return;
            var mine = new HashSet<int>();
            foreach (var b in list)
                mine.Add(FocusContext.AsInt(b["id"]));
            var planets = new List<int> { 0 };
            var view = _focus.FindViewFleet();
            if (view != null && view.PlanetId > 0)
                planets.Add(view.PlanetId);
            foreach (var p in _focus.Planets)
                if (planets.Count < 4 && OwnedPlanets.Contains(p.Id) && !planets.Contains(p.Id))
                    planets.Add(p.Id);

            var reads = new List<Task<ApiResult>>();
            foreach (var pid in planets)
                reads.Add(ActionJs.Get("GetPendingBattles", new Dictionary<string, string>
                {
                    { "systemid", sys.ToString() },
                    { "planetid", pid.ToString() }
                }));
            await Task.WhenAll(reads);
            foreach (var r in reads)
            {
                if (!r.Result.Ok || string.IsNullOrEmpty(r.Result.Body))
                    continue;
                JArray rows;
                try
                {
                    rows = JToken.Parse(r.Result.Body) as JArray;
                }
                catch
                {
                    continue;
                }

                if (rows == null)
                    continue;
                foreach (var b in rows)
                {
                    if (!mine.Add(FocusContext.AsInt(b["id"])) || b is not JObject o)
                        continue;
                    o["myFleetId"] = 0;
                    o["nearby"] = true;
                    list.Add(o);
                }
            }
        }

        async Task Reinforce(int battleId, FocusFleet fleet)
        {
            if (_busy)
                return;
            _busy = true;
            var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
            try
            {
                var result = await ActionJs.Get("AddFleetToBattle", new Dictionary<string, string>
                {
                    { "battleid", battleId.ToString() },
                    { "fleetid", fleet.Id.ToString() }
                });
                var (ok, message) = Interpret(result, "reinforceBattle");
                Feedback(ok, name + " · " + message);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Tactical, "AddFleetToBattle", result, name);
                if (ok && _hex != null)
                {
                    // Web: the reinforcing ship opens the battle scene.
                    _hex.Open(battleId, fleet.Id);
                    _busy = false;
                    Close();
                    if (_poller != null)
                        await _poller.PollNow();
                    return;
                }
            }
            finally
            {
                _busy = false;
            }

            _battles = null;
            if (_open)
                Render();
        }

        // ── Results ───────────────────────────────────────────────────────────────

        static (bool ok, string message) Interpret(ApiResult r, string okKey)
        {
            if (!r.Ok)
                return (false, string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error);
            return (true, Trans.Get(okKey));
        }

        void Feedback(bool ok, string message)
        {
            if (ok)
                CicCue.Ok(transform.position);
            else
                CicCue.Fail(transform.position);
            SetStatus(message, !ok);
        }

        void Update()
        {
            if (!_open || Time.unscaledTime < _nextLive)
                return;
            _nextLive = Time.unscaledTime + 0.5f;
            foreach (var (text, value) in _live)
                if (text != null)
                    text.text = value();
            foreach (var (fill, value) in _bars)
                if (fill != null)
                    fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value()), 1f);

            // Battle list refresh while on the operations tab.
            if (_tab == Tab.Operations && !_battlesLoading && !_busy && Time.unscaledTime - _battlesAt > BattlesEvery)
                Run(LoadBattles());
        }
    }
}
