using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// A world's fuel refinery (web RefineryWindowUI, model/fuel.php) as a place: the hydroponics bay of
    /// <see cref="RefineryDecor"/>, entered from the concourse or the citadel gallery of one of our worlds once the
    /// empire knows Fuel Synthesis. Each piece of the bay is one of the refinery's five stages and shows its level;
    /// a holo panel on a lectern beside it reads the stage (name, what it does, level / max, the next level's cost
    /// and time, its research needs) and upgrades it (UpgradeRefinery — the culture columns build the refinery on a
    /// world without one). The status console at the entrance reads the stock, what the refinery makes and eats an
    /// hour, the quality of its fuel, the stage in work with its progress and the Nova finish (SpeedupRefinery).
    /// GetRefinery every few seconds while inside (the server brings the refinery up to now on it); every failure
    /// is the server's own translated <c>error:</c>.
    /// </summary>
    public sealed class RefineryRoom : MonoBehaviour
    {
        sealed class StagePanel
        {
            public string Key;
            public HoloScreen Screen;
            public RectTransform Body;
            public TMP_Text Line;
        }

        static readonly Vector3 WorldOrigin = new(160f, -3000f, 200f);
        static readonly Vector3 Stand = Vector3.zero;
        public static readonly Color Accent = new(0.62f, 1f, 0.32f, 1f);
        static readonly Vector2 StatusSize = new(1.1f, 0.7f);
        static readonly Vector2 StageSize = new(0.8f, 0.56f);
        const float RefreshEvery = 5f;

        public static RefineryRoom Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        EconomyService _economy;
        RefineryDecor _decor;
        HoloScreen _status;
        RectTransform _statusBody;
        TMP_Text _statusLine;
        readonly List<StagePanel> _panels = new();

        int _planetId;
        RefineryState _state;
        int _signature;
        bool _busy;
        bool _transit;
        bool _loading;
        float _nextRefresh;
        float _nextTick;

        // Live readouts, updated in place (no rebuild): the stock gauge, the stage in work.
        RectTransform _fuelFill;
        TMP_Text _fuelCaption;
        readonly List<(TMP_Text label, RectTransform fill)> _live = new();

        // ── Build ─────────────────────────────────────────────────────────────────

        public static RefineryRoom Build(CicArtKit art, EconomyService economy)
        {
            var go = new GameObject("FuelRefinery");
            go.transform.position = WorldOrigin;
            var room = go.AddComponent<RefineryRoom>();
            room._art = art;
            room._economy = economy;
            room.BuildBay();
            room.BuildScreens();
            go.SetActive(false);
            return room;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Inside = false;
            }
        }

        void BuildBay()
        {
            _decor = RefineryDecor.Build(transform, _art, Accent);
            // Three lights for the HullInterior pieces and the doors (Quest: no shadows): a white-green key over the
            // basin, the grow racks' violet either side.
            var names = new[] { "RefineryKey", "RefineryGrowL", "RefineryGrowR" };
            var spots = new[] { new Vector3(0f, RefineryDecor.Height - 0.8f, 4.6f), new Vector3(-4.2f, 3.9f, 5f), new Vector3(4.2f, 3.9f, 5f) };
            var colours = new[] { new Color(0.9f, 1f, 0.94f), new Color(0.82f, 0.55f, 1f), new Color(0.82f, 0.55f, 1f) };
            for (var i = 0; i < names.Length; i++)
            {
                var l = new GameObject(names[i]).AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.transform.localPosition = spots[i];
                l.type = LightType.Point;
                l.shadows = LightShadows.None;
                l.color = colours[i];
                l.intensity = i == 0 ? 3.4f : 1.8f;
                l.range = i == 0 ? 16f : 8f;
            }

            RoomLightRig.Attach(transform, names).Radius = 14f;
            RoomDoor.Build(transform, "DoorToBridge", new Vector3(0f, 0f, RefineryDecor.BackZ + 0.18f), 0f, Trans.Get("vr.dock.leave"),
                UiKit.Cyan, _art, () => Inside, () => AsyncTap.Run(Leave()));
        }

        void BuildScreens()
        {
            _status = HoloScreen.Create(transform, "RefineryStatus", StatusSize, Vector3.zero, Quaternion.identity, Trans.Get("fuelRefinery"));
            _status.SetAccent(Accent, 0.5f);
            GateRoomDecor.SeatOnArm(_status.transform, _decor.StatusMount, StatusSize.y);
            _statusBody = ScreenKit.Body(_status.Content);
            _statusLine = DiegeticUi.HoloLabel(_status.Content, string.Empty, new Vector2(0f, -305f), new Vector2(1000f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.Center);

            foreach (var key in RefineryState.Stages)
            {
                var screen = HoloScreen.Create(transform, "Stage_" + key, StageSize, Vector3.zero, Quaternion.identity,
                    Trans.Get("refineryStage_" + key));
                screen.SetAccent(Accent, 0.45f);
                GateRoomDecor.SeatOnArm(screen.transform, _decor.StageMount(key), StageSize.y);
                _panels.Add(new StagePanel
                {
                    Key = key,
                    Screen = screen,
                    Body = ScreenKit.Body(screen.Content),
                    Line = DiegeticUi.HoloLabel(screen.Content, string.Empty, new Vector2(0f, -240f), new Vector2(740f, 34f), 16f,
                        DiegeticUi.CyanDim, TextAlignmentOptions.Center)
                });
            }
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        /// <summary>Into the refinery of <paramref name="planetId"/> (the world under the station or city we stand in).</summary>
        public async Task Enter(int planetId)
        {
            if (DiplomacyRoom.InRoomBeyondCorridor || _transit)
                return;
            _transit = true;
            var fade = ViewFade.Ensure();
            try
            {
                await fade.FadeOut();
                if (CorridorRoom.Inside)
                    CorridorRoom.Instance.Depart();
                // Over our ship, the star in the window above the tanks.
                RoomPlacement.OverShip(transform, Vector3.forward);
                gameObject.SetActive(true);
                var rig = FindFirstObjectByType<XROrigin>();
                if (rig != null)
                {
                    rig.transform.SetParent(transform, false);
                    rig.transform.localPosition = Stand;
                    rig.transform.localRotation = Quaternion.identity;
                    XrPlacement.PlaceHead(rig, transform.TransformPoint(Stand), transform.forward);
                }

                Inside = true;
                if (_planetId != planetId)
                {
                    _state = null;
                    _decor.Apply(null);
                }

                _planetId = planetId;
                Say(_statusLine, string.Empty, false);
                foreach (var p in _panels)
                    Say(p.Line, string.Empty, false);
                RenderAll();
            }
            finally
            {
                // Whatever happened above, the veil lifts: never leave the player blind in the bay.
                await fade.FadeIn();
                _transit = false;
            }

            CicCue.Ok(transform.position + Vector3.up);
            await Load();
        }

        async Task Leave()
        {
            if (!Inside || _transit)
                return;
            _transit = true;
            var fade = ViewFade.Ensure();
            try
            {
                await fade.FadeOut();
                Inside = false;
                CorridorRoom.ReturnPlayer(CorridorRoom.Slot.RefineryStarboard);
                gameObject.SetActive(false);
            }
            finally
            {
                await fade.FadeIn();
                _transit = false;
            }
        }

#if UNITY_EDITOR
        /// <summary>Verification: dress the bay from an injected read (no server, no XR), as if just entered.</summary>
        public void EditorShow(RefineryState state)
        {
            gameObject.SetActive(true);
            Inside = true;
            _planetId = state != null ? state.PlanetId : 0;
            Adopt(state);
            RenderAll();
        }

        public static void EditorReset() => Inside = false;
#endif

        // ── Data ──────────────────────────────────────────────────────────────────

        async Task Load()
        {
            if (_loading || _planetId <= 0)
                return;
            _loading = true;
            var asked = _planetId;
            try
            {
                var s = await RefineryState.Fetch(asked);
                if (!Inside || asked != _planetId)
                    return;
                if (s == null)
                {
                    if (_state == null)
                        Say(_statusLine, Trans.Get("vr.common.error"), true);
                    return;
                }

                Adopt(s);
            }
            finally
            {
                _loading = false;
                _nextRefresh = Time.time + RefreshEvery;
            }
        }

        /// <summary>Take a fresh read: dress the bay, re-render the screens if anything they show changed, else update the live readouts.</summary>
        void Adopt(RefineryState s)
        {
            if (s == null)
                return;
            var before = _state;
            _state = s;
            _decor.Apply(s);
            // A stage that finished since the last read (its timer ran out, or a Nova finish): its level went up.
            var done = before?.WorkingStage;
            if (!string.IsNullOrEmpty(done) && s.Built && s.Level(done) > before.Level(done))
            {
                CicCue.Success(_status.transform.position);
                Say(_statusLine, Trans.Get("refineryUpgradeDone"), false);
            }

            var sig = Signature(s);
            if (sig != _signature)
            {
                _signature = sig;
                RenderAll();
            }
            else
                UpdateLive();
        }

        /// <summary>What the screens show, minus the live parts (stock, countdowns): a change re-renders them.</summary>
        int Signature(RefineryState s)
        {
            unchecked
            {
                var h = 17;
                h = h * 31 + (s.Built ? 1 : 0) + (s.Unlocked ? 2 : 0) + (_busy ? 4 : 0);
                h = h * 31 + (s.WorkingStage ?? string.Empty).GetHashCode();
                h = h * 31 + (int)s.WorkingUntil;
                h = h * 31 + Mathf.RoundToInt(s.FuelPerHour * 10f);
                h = h * 31 + Mathf.RoundToInt(s.CrystalPerHour * 10f);
                h = h * 31 + Mathf.RoundToInt(s.BiomassPerHour * 10f);
                h = h * 31 + Mathf.RoundToInt(s.Capacity);
                h = h * 31 + Mathf.RoundToInt(s.PumpPerMinute);
                h = h * 31 + Mathf.RoundToInt(s.SpeedFactor * 1000f) + Mathf.RoundToInt(s.CostFactor * 1000f) * 7;
                foreach (var key in RefineryState.Stages)
                {
                    h = h * 31 + s.Level(key);
                    // The cost colours follow the world's stock.
                    if (s.ByKey.TryGetValue(key, out var st) && st.NextCost != null)
                        foreach (var kv in st.NextCost)
                            h = h * 31 + (Stock(kv.Key) >= kv.Value ? 1 : 0);
                }

                h = h * 31 + (_economy != null ? _economy.Nova : 0);
                return h;
            }
        }

        void Update()
        {
            if (!Inside)
                return;
            if (Time.time >= _nextTick)
            {
                _nextTick = Time.time + 1f;
                UpdateLive();
                // The stage in work ran out: ask now (the server finishes it on the read).
                if (_state != null && !string.IsNullOrEmpty(_state.WorkingStage) && !_state.Busy)
                    _nextRefresh = Mathf.Min(_nextRefresh, Time.time + 1.5f);
            }

            if (Time.time >= _nextRefresh && !_busy && !_loading && !_transit)
            {
                _nextRefresh = Time.time + RefreshEvery;
                AsyncTap.Run(Load());
            }
        }

        void UpdateLive()
        {
            var s = _state;
            if (s == null)
                return;
            if (_fuelFill != null)
                _fuelFill.anchorMax = new Vector2(s.Fill, 1f);
            if (_fuelCaption != null)
                _fuelCaption.text = FuelCaption(s);
            var now = FleetOrderGate.UnixNow();
            var progress = Progress(s, now);
            var remain = ScreenKit.Remaining(s.WorkingUntil - now).TrimStart('~');
            foreach (var (label, fill) in _live)
            {
                if (label != null)
                    label.text = remain;
                if (fill != null)
                    fill.anchorMax = new Vector2(progress, 1f);
            }
        }

        static float Progress(RefineryState s, long now)
        {
            var span = s.WorkingUntil - s.WorkingStart;
            return span > 0 ? Mathf.Clamp01((now - s.WorkingStart) / (float)span) : 1f;
        }

        // ── Orders ────────────────────────────────────────────────────────────────

        async Task Upgrade(string stage)
        {
            var panel = _panels.Find(p => p.Key == stage);
            if (_busy || _state == null || panel == null || !CanUpgrade(stage))
                return;
            _busy = true;
            Say(panel.Line, string.Empty, false);
            RenderAll();
            try
            {
                var (s, r) = await RefineryState.Upgrade(_planetId, stage);
                if (!Inside)
                    return;
                if (!r.Ok)
                {
                    CicCue.Fail(panel.Screen.transform.position);
                    Say(panel.Line, ServerError(r), true);
                    return;
                }

                CicCue.Success(panel.Screen.transform.position);
                Say(panel.Line, Trans.Get("refineryUpgradeStarted"), false);
                Adopt(s);
            }
            finally
            {
                _busy = false;
                _nextRefresh = Mathf.Min(_nextRefresh, Time.time + 0.5f);
                // Buttons live again (failure included).
                if (Inside)
                    RenderAll();
            }
        }

        async Task Speedup()
        {
            if (_busy || _state == null || !_state.Busy)
                return;
            _busy = true;
            Say(_statusLine, string.Empty, false);
            RenderAll();
            try
            {
                var (s, r) = await RefineryState.Speedup(_planetId);
                if (!Inside)
                    return;
                if (!r.Ok)
                {
                    CicCue.Fail(_status.transform.position);
                    Say(_statusLine, ServerError(r), true);
                    return;
                }

                // The reply carries the finished stage: Adopt announces it (said here only when it could not be read).
                if (s == null)
                {
                    CicCue.Success(_status.transform.position);
                    Say(_statusLine, Trans.Get("refineryUpgradeDone"), false);
                }

                Adopt(s);
            }
            finally
            {
                _busy = false;
                _nextRefresh = Mathf.Min(_nextRefresh, Time.time + 0.5f);
                // Buttons live again (failure included).
                if (Inside)
                    RenderAll();
            }
        }

        static string ServerError(ApiResult r) => ScreenKit.Verbatim(string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error);

        bool CanUpgrade(string key)
        {
            var s = _state;
            if (_busy || s == null || !s.Unlocked || s.Busy || !s.ByKey.TryGetValue(key, out var st) || st.AtMax)
                return false;
            return s.Built || key == "culture";
        }

        // ── Screens ───────────────────────────────────────────────────────────────

        void RenderAll()
        {
            _live.Clear();
            RenderStatus();
            foreach (var p in _panels)
                RenderStage(p);
            _signature = _state != null ? Signature(_state) : 0;
        }

        void RenderStatus()
        {
            ScreenKit.Clear(_statusBody);
            _fuelFill = null;
            _fuelCaption = null;
            var b = _statusBody;
            ScreenKit.Line(b, "<b>" + ScreenKit.Verbatim(PlanetName(_planetId)) + "</b>", 0f, 222f, 22f, UiKit.TextBright, 1000f,
                TextAlignmentOptions.Center);
            var s = _state;
            if (s == null)
            {
                ScreenKit.Line(b, Trans.Get("Loading"), 0f, 60f, 22f, UiKit.TextDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            if (!s.Unlocked)
            {
                ScreenKit.Line(b, Trans.Get("needFuelSynthesis"), 0f, 130f, 26f, UiKit.Amber, 1000f, TextAlignmentOptions.Center);
                ScreenKit.Para(b, Trans.Get("fuelRefineryDesc"), 0f, 20f, 19f, UiKit.TextDim, 940f, 140f, TextAlignmentOptions.Top);
                return;
            }

            if (!s.Built)
            {
                // A dormant bay: the console offers to plant the first culture column (that builds the refinery).
                ScreenKit.Para(b, Trans.Get("fuelRefineryDesc"), 0f, 120f, 19f, UiKit.TextDim, 940f, 110f, TextAlignmentOptions.Top);
                s.ByKey.TryGetValue("culture", out var culture);
                ScreenKit.Line(b, "<b>" + Trans.Get("refineryStage_culture") + "</b>  " + Trans.Get("lvl") + " 1", 0f, 30f, 22f, Accent, 1000f,
                    TextAlignmentOptions.Center);
                if (culture != null && culture.NextCost != null)
                {
                    ScreenKit.Line(b, CostText(culture), 0f, -12f, 18f, UiKit.TextBright, 1000f, TextAlignmentOptions.Center);
                    ScreenKit.Line(b, Core.Holo.TravelPlanner.TimeText(culture.NextSeconds).TrimStart('~'), 0f, -46f, 17f, UiKit.TextDim, 1000f,
                        TextAlignmentOptions.Center);
                }

                ScreenKit.Btn(b, Trans.Get("build"), 0f, -130f, 440f, 66f, () => AsyncTap.Run(Upgrade("culture")), DiegeticUi.BtnStyle.Cyan,
                    CanUpgrade("culture"));
                return;
            }

            // Stock, what the refinery makes and eats, the quality of its fuel.
            (_fuelFill, _fuelCaption) = LiveGauge(b, 0f, 160f, 960f, s.Fill, Accent, FuelCaption(s), 34f);
            var dim = ScreenKit.Hex(UiKit.TextDim);
            ScreenKit.Line(b, "<color=" + dim + ">" + Trans.Get("refineryMakes") + "</color>  <b>+" + ScreenKit.Num(s.FuelPerHour, 1) + " " +
                              Trans.Get("fuel") + "</b> " + Trans.Get("perHour"), 0f, 106f, 19f, UiKit.TextBright, 960f,
                TextAlignmentOptions.Center);
            ScreenKit.Line(b, "<color=" + dim + ">" + Trans.Get("refineryEats") + "</color>  <b>−" + ScreenKit.Num(s.CrystalPerHour, 1) + " " +
                              Trans.Get(MarketService.ResourceKey("crystal")) + "</b> · <b>−" + ScreenKit.Num(s.BiomassPerHour, 1) + " " +
                              Trans.Get(MarketService.ResourceKey("biomass")) + "</b> " + Trans.Get("perHour"), 0f, 70f, 19f, UiKit.TextBright,
                960f, TextAlignmentOptions.Center);
            ScreenKit.Line(b, "<color=" + dim + ">" + Trans.Get("fuelQuality") + "</color>  " + Trans.Get("speed") + " <b>×" +
                              ScreenKit.Num(s.SpeedFactor, 2) + "</b> · " + Trans.Get("fuelConsumption") + " <b>×" + ScreenKit.Num(s.CostFactor, 2) +
                              "</b>", 0f, 34f, 19f, UiKit.TextBright, 960f, TextAlignmentOptions.Center);
            if (s.PumpPerMinute > 0f)
                ScreenKit.Line(b, "<color=" + dim + ">" + Trans.Get("refineryStage_pump") + "</color>  <b>" + ScreenKit.Num(s.PumpPerMinute) + " " +
                                  Trans.Get("fuel") + "</b> " + Trans.Get("perMinute"), 0f, -2f, 19f, UiKit.TextBright, 960f,
                    TextAlignmentOptions.Center);

            if (!s.Busy)
            {
                ScreenKit.Para(b, Trans.Get("fuelRefineryDesc"), 0f, -120f, 16f, UiKit.TextDim, 900f, 90f, TextAlignmentOptions.Top);
                return;
            }

            // The stage in work: progress and the Nova finish.
            var now = FleetOrderGate.UnixNow();
            var next = s.Level(s.WorkingStage) + 1;
            ScreenKit.Line(b, "<b>" + Trans.Get("refineryStage_" + s.WorkingStage) + "</b>  " + Trans.Get("lvl") + " " + next, -200f, -60f, 20f,
                UiKit.Amber, 560f);
            var remain = ScreenKit.Line(b, ScreenKit.Remaining(s.WorkingUntil - now).TrimStart('~'), 330f, -60f, 20f, UiKit.Amber, 300f,
                TextAlignmentOptions.MidlineRight);
            var (fill, _) = LiveGauge(b, 0f, -100f, 960f, Progress(s, now), UiKit.Amber, null, 16f);
            _live.Add((remain, fill));
            var cost = BuildingCatalog.SpeedupCost(s.WorkingUntil - now);
            var nova = _economy != null ? _economy.Nova : 0;
            var canPay = cost == 0 || _economy == null || nova >= cost;
            var label = cost == 0 ? Trans.Get("finishFree") : Trans.Format("vr.ops.finish", cost + " " + Trans.Get("nova"));
            ScreenKit.Btn(b, label, 0f, -175f, 460f, 62f, () => AsyncTap.Run(Speedup()), canPay ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost,
                canPay && !_busy);
        }

        void RenderStage(StagePanel p)
        {
            ScreenKit.Clear(p.Body);
            var b = p.Body;
            var s = _state;
            RefineryState.Stage st = null;
            s?.ByKey.TryGetValue(p.Key, out st);
            var level = s != null && s.Built ? s.Level(p.Key) : 0;
            var max = st?.Max ?? 0;
            ScreenKit.Line(b, "<b>" + Trans.Get("lvl") + " " + level + "</b>" + (max > 0 ? " / " + max : string.Empty), 0f, 178f, 26f, Accent, 720f,
                TextAlignmentOptions.Center);
            ScreenKit.Para(b, Trans.Get("refineryStageDesc_" + p.Key), 0f, 118f, 16f, UiKit.TextDim, 720f, 74f, TextAlignmentOptions.Top);
            if (s == null || st == null)
            {
                ScreenKit.Line(b, Trans.Get("Loading"), 0f, 20f, 18f, UiKit.TextDim, 720f, TextAlignmentOptions.Center);
                return;
            }

            var working = s.Busy && s.WorkingStage == p.Key;
            if (working)
            {
                var now = FleetOrderGate.UnixNow();
                ScreenKit.Line(b, "<b>" + Trans.Get("lvl") + " " + (level + 1) + "</b>", -150f, 40f, 20f, UiKit.Amber, 380f);
                var remain = ScreenKit.Line(b, ScreenKit.Remaining(s.WorkingUntil - now).TrimStart('~'), 190f, 40f, 20f, UiKit.Amber, 300f,
                    TextAlignmentOptions.MidlineRight);
                var (fill, _) = LiveGauge(b, 0f, 4f, 700f, Progress(s, now), UiKit.Amber, null, 14f);
                _live.Add((remain, fill));
            }
            else if (st.AtMax)
                ScreenKit.Line(b, Trans.Get("maxLevelReached"), 0f, 30f, 20f, UiKit.Ok, 720f, TextAlignmentOptions.Center);
            else
            {
                ScreenKit.Line(b, CostText(st), 0f, 48f, 17f, UiKit.TextBright, 740f, TextAlignmentOptions.Center);
                ScreenKit.Line(b, Core.Holo.TravelPlanner.TimeText(st.NextSeconds).TrimStart('~'), 0f, 16f, 16f, UiKit.TextDim, 720f,
                    TextAlignmentOptions.Center);
                if (st.NextRequires != null && st.NextRequires.Count > 0)
                    ScreenKit.Line(b, RequiresText(st), 0f, -16f, 16f, UiKit.TextBright, 720f, TextAlignmentOptions.Center);
            }

            // Why the button is grey, in the server's own words.
            string why = null;
            if (!s.Unlocked)
                why = "needFuelSynthesis";
            else if (!s.Built && p.Key != "culture")
                why = "refineryNeedsCulture";
            else if (s.Busy && !working)
                why = "refineryBusy";
            if (why != null)
                ScreenKit.Line(b, Trans.Get(why), 0f, -58f, 16f, UiKit.Amber, 740f, TextAlignmentOptions.Center);

            if (!st.AtMax)
            {
                var label = !s.Built && p.Key == "culture" ? Trans.Get("build") : Trans.Get("upgrade");
                var key = p.Key;
                ScreenKit.Btn(b, label, 0f, -130f, 380f, 64f, () => AsyncTap.Run(Upgrade(key)), DiegeticUi.BtnStyle.Cyan, CanUpgrade(key));
            }
        }

        /// <summary>The next level's price, each resource red when the world's stock falls short.</summary>
        string CostText(RefineryState.Stage st)
        {
            var parts = new List<string>();
            foreach (var kv in st.NextCost)
            {
                var have = Stock(kv.Key);
                var color = have < 0f || have >= kv.Value ? UiKit.TextBright : UiKit.Danger;
                parts.Add("<color=" + ScreenKit.Hex(color) + ">" + Trans.Get(MarketService.ResourceKey(kv.Key)) + " <b>" + ScreenKit.Num(kv.Value) +
                          "</b></color>");
            }

            return string.Join("  ·  ", parts);
        }

        string RequiresText(RefineryState.Stage st)
        {
            var parts = new List<string>();
            foreach (var kv in st.NextRequires)
            {
                var ok = _economy == null || _economy.ResearchLevel(kv.Key) >= kv.Value;
                parts.Add("<color=" + ScreenKit.Hex(ok ? UiKit.Ok : UiKit.Danger) + ">" + Trans.Get(kv.Key) + " " + Trans.Get("lvl") + " " + kv.Value +
                          "</color>");
            }

            return string.Join("  ·  ", parts);
        }

        /// <summary>The refinery world's stock of a resource (GetResource heartbeat), −1 when unknown.</summary>
        float Stock(string resource)
        {
            if (_economy == null || !_economy.Planets.TryGetValue(_planetId, out var p))
                return -1f;
            return resource switch
            {
                "mineral" => p.Mineral,
                "crystal" => p.Crystal,
                "biomass" => p.Biomass,
                _ => -1f
            };
        }

        static string FuelCaption(RefineryState s) =>
            Trans.Get("fuel") + "  <b>" + ScreenKit.Num(Mathf.Floor(s.Fuel)) + "</b> / " + ScreenKit.Num(Mathf.Floor(s.Capacity));

        static string PlanetName(int id)
        {
            foreach (var p in OwnedPlanets.All)
                if (p.Id == id)
                    return p.Name;
            return string.Empty;
        }

        static void Say(TMP_Text line, string text, bool error)
        {
            if (line == null)
                return;
            line.text = text ?? string.Empty;
            line.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        /// <summary><see cref="ScreenKit.Gauge"/> whose fill and caption stay reachable (updated in place each second).</summary>
        static (RectTransform fill, TMP_Text caption) LiveGauge(RectTransform parent, float x, float y, float width, float value, Color color,
            string caption, float height)
        {
            var bg = new GameObject("Gauge", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(parent, false);
            var rt = bg.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
            var img = bg.GetComponent<Image>();
            img.color = new Color(0.1f, 0.18f, 0.22f, 0.85f);
            img.raycastTarget = false;
            var fg = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fg.transform.SetParent(bg.transform, false);
            var ft = fg.GetComponent<RectTransform>();
            ft.anchorMin = Vector2.zero;
            ft.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            ft.offsetMin = new Vector2(2f, 2f);
            ft.offsetMax = new Vector2(-2f, -2f);
            var fi = fg.GetComponent<Image>();
            fi.color = color;
            fi.raycastTarget = false;
            TMP_Text label = null;
            if (!string.IsNullOrEmpty(caption))
                label = ScreenKit.Line(parent, caption, x, y, Mathf.Min(19f, height * 0.6f), UiKit.TextBright, width, TextAlignmentOptions.Center);
            return (ft, label);
        }
    }
}
