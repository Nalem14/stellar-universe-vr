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
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Stations
{
    /// <summary>
    /// The gate base — the Stargate of one of our worlds as a place (web "Base Militaire" Stargate card,
    /// scenes/planet.js). You stand in the command gallery above the embarkation hall: the gate at the end
    /// of the causeway, the command console on the left, the mission log on the right.
    /// No connection: dial an address (ResolveStargateAddress — dialing it right IS the discovery) or pick a
    /// known one (GetKnownAddresses: status, owner, distance, energy load) → OpenStargateConnection: the
    /// track spins, six locks seat, the horizon bursts. Open from here: choose a mission (colonize, attack,
    /// pillage, explore, troops, resources) → DispatchStargateMission, and the team walks through. Opened
    /// from the far side: alarm, red horizon, close it (CloseStargateConnection). GetStargateConnectionStatus
    /// every 3 s and GetStargateMissions every 10 s while inside; missions resolve server-side on GetResource.
    /// </summary>
    public sealed class GateRoom : MonoBehaviour
    {
        static readonly Vector3 WorldOrigin = new(-160f, -3000f, 0f);
        static readonly Vector3 Stand = Vector3.zero;
        public static readonly Color Accent = new(0.62f, 0.5f, 1f, 1f);
        const float HallFloor = -1.4f;
        const float HallWidth = 13f;
        const float HallHeight = 6.8f;
        const float GalleryEdge = 1.5f;
        const float HallEnd = 15f;
        static readonly Vector3 GatePos = new(0f, HallFloor + 0.3f + GateRing.Outer, 10.5f);
        const float StatusEvery = 3f;
        const float MissionsEvery = 10f;
        const int TargetsPerPage = 5;
        static readonly string[] MissionTypes = { "colonize", "attack", "pillage", "explore", "sendTroops", "sendResources" };

        public static GateRoom Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        EconomyService _eco;
        GateRing _gate;
        TextMeshPro _banner;
        GateRoomDecor.Refs _decor;
        readonly int[] _lampState = { -1, -1, -1, -1, -1, -1 };
        readonly List<Renderer> _alarmStrips = new();
        Material _stripCalm;
        Material _stripAlarm;
        Light _hallLight;

        HoloScreen _console;
        RectTransform _consoleBody;
        TMP_Text _planetLabel;
        TMP_Text _status;
        GameObject _dialGroup;
        TMP_InputField _dial;
        HoloScreen _journal;
        RectTransform _journalBody;
        readonly List<(TMP_Text Text, Func<string> Value)> _live = new();
        readonly List<(TMP_Text Text, Func<string> Value)> _liveJournal = new();

        readonly List<int> _gated = new();
        int _planetId;
        JObject _conn;
        JArray _known;
        JObject _composed;
        int _selected;
        int _page;
        JArray _missions;
        string _mission = "explore";
        string _troopType = string.Empty;
        int _qty = 10;
        readonly float[] _cargo = new float[3];
        bool _busy;
        float _statusAt;
        float _missionsAt;
        float _nextLive;
        bool _alarm;
        float _klaxonAt;

        // ── Build ─────────────────────────────────────────────────────────────────

        public static GateRoom Build(CicArtKit art, EconomyService eco)
        {
            var go = new GameObject("GateBase");
            go.transform.position = WorldOrigin;
            var room = go.AddComponent<GateRoom>();
            room._art = art;
            room._eco = eco;
            room.BuildHall();
            room._gate = GateRing.Build(go.transform, GatePos, art);
            room.BuildScreens();
            go.SetActive(false);
            return room;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!solid)
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void BuildHall()
        {
            var deck = _art.DeckMat(0.4f);
            var wall = _art.DarkPanel(0.28f);
            var rib = _art.MetalPanel(0.4f);
            var violet = _art.Lit(Texture2D.whiteTexture, Accent, 2.6f);
            var cyan = _art.CyanEmit(2f);
            _stripCalm = violet;
            _stripAlarm = _art.Lit(Texture2D.whiteTexture, new Color(1f, 0.18f, 0.12f, 1f), 3.2f);
            var half = HallWidth * 0.5f;

            // Command gallery (player level) and its rail over the hall.
            Box("GalleryFloor", new Vector3(0f, -0.05f, -1f), new Vector3(HallWidth, 0.1f, 5f), deck, solid: true);
            Box("GalleryFront", new Vector3(0f, HallFloor * 0.5f - 0.05f, GalleryEdge), new Vector3(HallWidth, -HallFloor, 0.2f), wall);
            Box("Rail", new Vector3(0f, 1.02f, GalleryEdge - 0.08f), new Vector3(HallWidth - 1f, 0.06f, 0.08f), rib);
            Box("RailGlow", new Vector3(0f, 0.98f, GalleryEdge - 0.03f), new Vector3(HallWidth - 1.2f, 0.015f, 0.02f), cyan);
            for (var i = -5; i <= 5; i++)
                Box("Stanchion" + i, new Vector3(i * 1.15f, 0.5f, GalleryEdge - 0.08f), new Vector3(0.05f, 1f, 0.05f), rib);
            var glass = _art.Holo(Texture2D.whiteTexture, new Color(0.4f, 0.6f, 1f, 0.12f));
            Box("RailGlass", new Vector3(0f, 0.5f, GalleryEdge - 0.06f), new Vector3(HallWidth - 1f, 0.9f, 0.01f), glass);

            // Embarkation hall below: floor, walls with ribs, a violet base line that goes red on alarm.
            Box("HallFloor", new Vector3(0f, HallFloor - 0.05f, (GalleryEdge + HallEnd) * 0.5f),
                new Vector3(HallWidth, 0.1f, HallEnd - GalleryEdge), deck, solid: true);
            Box("Ceiling", new Vector3(0f, HallFloor + HallHeight, (HallEnd - 3.5f) * 0.5f), new Vector3(HallWidth, 0.1f, HallEnd + 3.5f), wall);
            Box("BackWall", new Vector3(0f, HallFloor + HallHeight * 0.5f, HallEnd), new Vector3(HallWidth, HallHeight, 0.2f), wall);
            Box("GalleryBack", new Vector3(0f, HallFloor + HallHeight * 0.5f, -3.5f), new Vector3(HallWidth, HallHeight, 0.2f), wall);
            for (var side = -1; side <= 1; side += 2)
            {
                Box(side < 0 ? "WallL" : "WallR", new Vector3(side * half, HallFloor + HallHeight * 0.5f, (HallEnd - 3.5f) * 0.5f),
                    new Vector3(0.2f, HallHeight, HallEnd + 3.5f), wall, solid: true);
                for (var k = 0; k < 7; k++)
                {
                    var z = -2.5f + k * 2.8f;
                    Box("Rib", new Vector3(side * (half - 0.15f), HallFloor + HallHeight * 0.5f, z),
                        new Vector3(0.25f, HallHeight, 0.35f), rib);
                    var strip = Box("Strip", new Vector3(side * (half - 0.11f), HallFloor + 0.2f, z + 1.4f),
                        new Vector3(0.02f, 0.03f, 2.3f), violet);
                    _alarmStrips.Add(strip.GetComponent<MeshRenderer>());
                    var crown = Box("Crown", new Vector3(side * (half - 0.11f), HallFloor + HallHeight - 0.5f, z + 1.4f),
                        new Vector3(0.02f, 0.025f, 2.3f), violet);
                    _alarmStrips.Add(crown.GetComponent<MeshRenderer>());
                }
            }

            // Causeway from the team door to the gate dais; light strips along its edges.
            Box("Causeway", new Vector3(0f, HallFloor + 0.12f, 6.8f), new Vector3(2.6f, 0.24f, 7.4f), rib, solid: true);
            for (var side = -1; side <= 1; side += 2)
                Box("CausewayLight", new Vector3(side * 1.28f, HallFloor + 0.245f, 6.8f), new Vector3(0.04f, 0.012f, 7.2f), cyan);
            Box("Dais", new Vector3(0f, HallFloor + 0.15f, GatePos.z), new Vector3(8f, 0.3f, 2.4f), rib, solid: true);
            Box("DaisGlow", new Vector3(0f, HallFloor + 0.305f, GatePos.z + 1.2f), new Vector3(7.6f, 0.012f, 0.05f), violet);

            // Team door on the left wall of the hall, lit frame.
            Box("TeamDoor", new Vector3(-half + 0.12f, HallFloor + 1.3f, 3.6f), new Vector3(0.06f, 2.6f, 2f),
                _art.Lit(Texture2D.whiteTexture, new Color(0.03f, 0.06f, 0.08f), 0.2f));
            Box("TeamDoorLintel", new Vector3(-half + 0.14f, HallFloor + 2.7f, 3.6f), new Vector3(0.06f, 0.06f, 2.2f), cyan);

            // Address banner above the gate.
            _banner = UiKit.Label(transform, "AddressBanner", string.Empty,
                GatePos + new Vector3(0f, GateRing.Outer + 0.75f, 0.2f), 7f, 0.55f, Accent);
            _banner.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _banner.fontStyle = FontStyles.Bold;
            _banner.characterSpacing = 12f;

            var fill = new GameObject("HallLight").AddComponent<Light>();
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = new Vector3(0f, HallFloor + HallHeight - 0.6f, 6f);
            fill.type = LightType.Point;
            fill.range = 16f;
            fill.intensity = 1.6f;
            fill.color = new Color(0.7f, 0.8f, 1f);
            fill.shadows = LightShadows.None;
            _hallLight = fill;

            _decor = GateRoomDecor.Build(transform, _art, HallFloor, HallWidth, HallHeight, GalleryEdge, GatePos, Accent);

            RoomDoor.Build(transform, "DoorToBridge", new Vector3(2.2f, 0f, -3.35f), 0f, Trans.Get("vr.gate.leave"),
                UiKit.Amber, _art, () => Inside, () => AsyncTap.Run(Leave()));
        }

        void BuildScreens()
        {
            _console = HoloScreen.Create(transform, "GateConsole", new Vector2(1.1f, 0.7f),
                new Vector3(-1.05f, 1.28f, 0.85f), Quaternion.identity, Trans.Get("vr.gate.title"));
            _console.SetAccent(Accent, 0.5f);
            // On its desk's arm, turned to the captain's stand.
            _console.transform.position = _decor.ConsoleMount.position;
            ScreenMount.FaceViewer(_console.transform, transform.TransformPoint(new Vector3(0f, 1.6f, 0f)), 1f, 14f);
            var frame = _console.Content;

            DiegeticUi.HoloButton(frame, "‹", new Vector2(-505f, 245f), new Vector2(64f, 46f), () => StepPlanet(-1),
                DiegeticUi.BtnStyle.Ghost);
            _planetLabel = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(-195f, 245f), new Vector2(540f, 46f), 24f,
                UiKit.TextBright);
            _planetLabel.fontStyle = FontStyles.Bold;
            _planetLabel.richText = true;
            DiegeticUi.HoloButton(frame, "›", new Vector2(115f, 245f), new Vector2(64f, 46f), () => StepPlanet(1),
                DiegeticUi.BtnStyle.Ghost);

            _dialGroup = new GameObject("DialBar", typeof(RectTransform));
            _dialGroup.transform.SetParent(frame, false);
            _dialGroup.GetComponent<RectTransform>().sizeDelta = _console.PixelSize;
            _dial = DiegeticUi.HoloField(_dialGroup.transform, "Address", "XX-XX-XX-XX-XX-XX", new Vector2(-170f, 180f),
                new Vector2(560f, 50f), TouchScreenKeyboardType.ASCIICapable);
            _dial.characterLimit = 17;
            _dial.onSubmit.AddListener(_ => Run(Resolve()));
            DiegeticUi.HoloButton(_dialGroup.transform, Trans.Get("vr.gate.locate"), new Vector2(230f, 180f),
                new Vector2(220f, 50f), () => Run(Resolve()), DiegeticUi.BtnStyle.Cyan);

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(frame, false);
            _consoleBody = body.GetComponent<RectTransform>();
            _consoleBody.sizeDelta = new Vector2(1060f, 420f);
            _status = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(-190f, -305f), new Vector2(660f, 40f), 18f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft);

            _journal = HoloScreen.Create(transform, "GateJournal", new Vector2(0.8f, 0.62f), new Vector3(1.15f, 1.24f, 0.8f),
                Quaternion.identity, Trans.Get("vr.gate.journal"));
            _journal.SetAccent(Accent, 0.4f);
            _journal.transform.position = _decor.JournalMount.position;
            ScreenMount.FaceViewer(_journal.transform, transform.TransformPoint(new Vector3(0f, 1.6f, 0f)), 1f, 14f);
            var jb = new GameObject("Body", typeof(RectTransform));
            jb.transform.SetParent(_journal.Content, false);
            _journalBody = jb.GetComponent<RectTransform>();
            _journalBody.sizeDelta = new Vector2(760f, 520f);
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        public async Task Enter()
        {
            if (Inside || DryDock.Inside || ResearchLab.Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
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
            await _eco.RefreshNow();
            CollectGated();
            if (!_gated.Contains(_planetId))
                _planetId = _gated.Count > 0 ? _gated[0] : 0;
            _known = null;
            _conn = null;
            _composed = null;
            _selected = 0;
            await RefreshStatus();
            AsyncTap.Run(LoadKnown());
            AsyncTap.Run(LoadMissions());
            if (_conn != null)
                _gate.OpenNow(!FocusContext.AsBool(_conn["isOrigin"]));
            RenderAll();
            await fade.FadeIn();
            CicCue.Ok(transform.position + Vector3.up);
        }

        async Task Leave()
        {
            if (!Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Inside = false;
            SetAlarm(false);
            var rig = FindFirstObjectByType<XROrigin>();
            var bridge = FindFirstObjectByType<BridgeViewRig>();
            if (rig != null && bridge != null && bridge.BridgeMount != null)
            {
                rig.transform.SetParent(bridge.BridgeMount, false);
                bridge.PutPlayerOnDeck();
            }

            gameObject.SetActive(false);
            await fade.FadeIn();
        }

        /// <summary>Our planets with a built Stargate (the one under construction does not count, as on the web).</summary>
        void CollectGated()
        {
            _gated.Clear();
            foreach (var p in OwnedPlanets.All)
                if (_eco.TryGet(p.Id, out var e) && TroopCatalog.BuiltLevel(e, "stargate") > 0)
                    _gated.Add(p.Id);
        }

        public bool AnyGate()
        {
            CollectGated();
            return _gated.Count > 0;
        }

        void StepPlanet(int delta)
        {
            if (_gated.Count < 2 || _busy)
                return;
            var i = _gated.IndexOf(_planetId);
            _planetId = _gated[((i < 0 ? 0 : i) + delta + _gated.Count) % _gated.Count];
            _known = null;
            _composed = null;
            _selected = 0;
            _conn = null;
            if (_gate.IsOpen)
                _gate.Close();
            SetStatus(string.Empty);
            AsyncTap.Run(RefreshAfterSwitch());
        }

        async Task RefreshAfterSwitch()
        {
            await RefreshStatus();
            await LoadKnown();
            RenderAll();
        }

        static void Run(Task t) => AsyncTap.Run(t);

        void SetStatus(string text, bool error = false)
        {
            _status.text = text ?? string.Empty;
            _status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        // ── Server ────────────────────────────────────────────────────────────────

        async Task RefreshStatus()
        {
            if (_planetId <= 0)
                return;
            var planet = _planetId;
            var res = await ActionJs.Get("GetStargateConnectionStatus",
                new Dictionary<string, string> { { "planet", planet.ToString() } });
            if (planet != _planetId || !res.Ok)
                return;
            JObject conn = null;
            try
            {
                conn = JToken.Parse(res.Body) as JObject;
            }
            catch
            {
                // "null" = no connection.
            }

            if (conn != null && FocusContext.AsString(conn["phase"]) != "open")
                conn = null;
            var had = _conn != null;
            _conn = conn;
            if (!Inside)
                return;
            if (conn != null && !_gate.IsOpen && !_gate.Busy)
                _gate.OpenNow(!FocusContext.AsBool(conn["isOrigin"]));
            else if (conn == null && had && (_gate.IsOpen || _gate.Busy))
                _gate.Close();
            SetAlarm(conn != null && !FocusContext.AsBool(conn["isOrigin"]));
            if (had != (conn != null))
                RenderAll();
        }

        async Task LoadKnown()
        {
            if (_planetId <= 0)
                return;
            var res = await ActionJs.Get("GetKnownAddresses", new Dictionary<string, string> { { "planet", _planetId.ToString() } });
            _known = ParseArray(res);
            if (!res.Ok)
                SetStatus(res.Error, true);
            if (Inside)
                RenderAll();
        }

        async Task LoadMissions()
        {
            var res = await ActionJs.Get("GetStargateMissions");
            var list = ParseArray(res);
            // A pending mission that is now resolved: the officer reports it.
            if (_missions != null)
            {
                foreach (var m in list)
                {
                    var id = FocusContext.AsInt(m["id"]);
                    foreach (var old in _missions)
                    {
                        if (FocusContext.AsInt(old["id"]) != id || FocusContext.AsString(old["status"]) != "pending" ||
                            FocusContext.AsString(m["status"]) == "pending")
                            continue;
                        var ok = FocusContext.AsString(m["result"]) == "success";
                        Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, ok ? "gateSuccess" : "gateFailed", 2,
                            FocusContext.AsString(m["targetPlanetName"]));
                    }
                }
            }

            _missions = list;
            if (Inside)
                RenderJournal();
        }

        async Task Resolve()
        {
            var address = (_dial.text ?? string.Empty).Trim();
            if (address.Length == 0 || _planetId <= 0 || _busy)
                return;
            _busy = true;
            try
            {
                var res = await ActionJs.Get("ResolveStargateAddress", new Dictionary<string, string>
                {
                    { "originPlanet", _planetId.ToString() },
                    { "address", address }
                });
                if (!res.Ok)
                {
                    Fail(res.Error);
                    return;
                }

                _composed = JToken.Parse(res.Body) as JObject;
                if (_composed != null)
                {
                    _selected = FocusContext.AsInt(_composed["id"]);
                    _page = 0;
                    SetStatus(Trans.Format("vr.gate.located", Name(_composed)));
                    CicCue.Ok(_console.transform.position);
                    // Dialing it right is a discovery: it joins the known list.
                    _known = null;
                    await LoadKnown();
                }
            }
            catch
            {
                Fail(Trans.Get("vr.common.error"));
            }
            finally
            {
                _busy = false;
            }

            RenderAll();
        }

        async Task OpenGate()
        {
            var target = Target(_selected);
            if (target == null || _busy || _gate.Busy)
                return;
            _busy = true;
            try
            {
                var res = await ActionJs.Get("OpenStargateConnection", new Dictionary<string, string>
                {
                    { "originPlanet", _planetId.ToString() },
                    { "targetPlanet", FocusContext.AsString(target["id"]) }
                });
                if (!res.Ok)
                {
                    Fail(res.Error);
                    return;
                }

                SetStatus(Trans.Format("vr.gate.dialing", Name(target)));
                _banner.text = FocusContext.AsString(target["address"]);
                _gate.Dial(FocusContext.AsString(target["address"]), () =>
                {
                    SetStatus(Trans.Format("vr.gate.open", Name(target)));
                    Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "stargateOpen", 2);
                });
                await RefreshStatus();
            }
            finally
            {
                _busy = false;
            }

            RenderAll();
        }

        async Task CloseGate()
        {
            if (_busy || _planetId <= 0)
                return;
            _busy = true;
            try
            {
                var res = await ActionJs.Get("CloseStargateConnection", new Dictionary<string, string>
                {
                    { "planet", _planetId.ToString() }
                });
                if (!res.Ok)
                {
                    Fail(res.Error);
                    return;
                }

                _gate.Close();
                _conn = null;
                _banner.text = string.Empty;
                SetAlarm(false);
                SetStatus(Trans.Get("vr.gate.closed"));
            }
            finally
            {
                _busy = false;
            }

            RenderAll();
        }

        async Task Dispatch()
        {
            if (_busy || _conn == null)
                return;
            var args = new Dictionary<string, string>
            {
                { "originPlanet", _planetId.ToString() },
                { "missionType", _mission }
            };
            var troops = TroopMission(_mission);
            if (troops)
            {
                if (string.IsNullOrEmpty(_troopType))
                {
                    Fail(Trans.Get("notEnoughTroops"));
                    return;
                }

                args["troopType"] = _troopType;
                args["troopQty"] = _qty.ToString();
            }
            else if (_mission == "sendResources")
            {
                args["mineral"] = Mathf.RoundToInt(_cargo[0]).ToString();
                args["crystal"] = Mathf.RoundToInt(_cargo[1]).ToString();
                args["biomass"] = Mathf.RoundToInt(_cargo[2]).ToString();
            }

            _busy = true;
            try
            {
                var res = await ActionJs.Get("DispatchStargateMission", args);
                if (!res.Ok)
                {
                    Fail(res.Error);
                    return;
                }

                GateWalker.Dispatch(transform, _gate, _mission, troops ? _qty : 0, TeamPath);
                SetStatus(Trans.Format("vr.gate.dispatched", Trans.Get("vr.gate.mission." + _mission)));
                CicCue.Ok(_console.transform.position);
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "gateDispatch", 2,
                    Trans.Get("vr.gate.mission." + _mission));
                for (var i = 0; i < _cargo.Length; i++)
                    _cargo[i] = 0f;
                await _eco.RefreshNow();
                await LoadMissions();
            }
            finally
            {
                _busy = false;
            }

            RenderAll();
        }

        /// <summary>Team door → causeway → through the horizon (room local).</summary>
        static readonly Vector3[] TeamPath =
        {
            new(-HallWidth * 0.5f + 0.6f, HallFloor, 3.6f), new(-1.8f, HallFloor, 3.6f), new(0f, HallFloor + 0.24f, 3.8f),
            new(0f, HallFloor + 0.24f, GatePos.z - 1.2f), new(0f, HallFloor + 0.3f, GatePos.z + 0.4f)
        };

        static bool TroopMission(string type) => type is "attack" or "pillage" or "explore" or "sendTroops";

        void Fail(string message)
        {
            SetStatus(string.IsNullOrEmpty(message) ? Trans.Get("vr.common.error") : message, true);
            CicCue.Fail(_console.transform.position);
            Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "fail", 2);
        }

        static JArray ParseArray(ApiResult res)
        {
            if (!res.Ok || string.IsNullOrEmpty(res.Body))
                return new JArray();
            try
            {
                return JToken.Parse(res.Body) as JArray ?? new JArray();
            }
            catch
            {
                return new JArray();
            }
        }

        // ── Render ────────────────────────────────────────────────────────────────

        void RenderAll()
        {
            RenderConsole();
            RenderJournal();
        }

        TMP_Text Line(RectTransform parent, string text, float x, float y, float size, Color color, float width,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var t = DiegeticUi.HoloLabel(parent, text, new Vector2(x, y), new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void Clear(RectTransform body)
        {
            for (var i = body.childCount - 1; i >= 0; i--)
                DestroyImmediate(body.GetChild(i).gameObject);
        }

        static string Num(float v) => Mathf.RoundToInt(v).ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

        static string Name(JToken p)
        {
            var n = FocusContext.AsString(p?["name"]);
            return string.IsNullOrEmpty(n) ? "#" + FocusContext.AsString(p?["id"]) : n;
        }

        JToken Target(int id)
        {
            if (_composed != null && FocusContext.AsInt(_composed["id"]) == id)
                return _composed;
            if (_known == null)
                return null;
            foreach (var t in _known)
                if (FocusContext.AsInt(t["id"]) == id)
                    return t;
            return null;
        }

        void RenderConsole()
        {
            _live.Clear();
            Clear(_consoleBody);
            _eco.TryGet(_planetId, out var planet);
            var star = GalaxyCatalog.TryGet(planet?.SystemId ?? 0, out var s) ? s.Label : string.Empty;
            _planetLabel.text = _planetId <= 0
                ? Trans.Get("vr.gate.noGate")
                : (planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + _planetId) +
                  "   <size=70%><color=#7fd8ff>" + star + "</color></size>";

            _dialGroup.SetActive(_planetId > 0 && _conn == null);
            if (_planetId <= 0)
            {
                Line(_consoleBody, Trans.Get("vr.gate.noGateHint"), 0f, 60f, 21f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                if (string.IsNullOrEmpty(_banner.text))
                    _banner.text = string.Empty;
                return;
            }

            if (_conn == null)
                RenderTargets();
            else if (FocusContext.AsBool(_conn["isOrigin"]))
                RenderMission(planet);
            else
                RenderIncoming();
        }

        void RenderTargets()
        {
            if (!_gate.Busy)
                _banner.text = string.Empty;
            var list = new List<JToken>();
            if (_composed != null)
                list.Add(_composed);
            // Never the base itself: the server refuses a connection to its own origin.
            if (_known != null)
                foreach (var t in _known)
                    if (FocusContext.AsInt(t["id"]) != _planetId &&
                        (_composed == null || FocusContext.AsInt(t["id"]) != FocusContext.AsInt(_composed["id"])))
                        list.Add(t);

            if (_known == null && _composed == null)
            {
                Line(_consoleBody, Trans.Get("Loading"), 0f, 60f, 21f, DiegeticUi.CyanDim, 1000f, TextAlignmentOptions.Center);
                return;
            }

            if (list.Count == 0)
            {
                Line(_consoleBody, Trans.Get("vr.gate.noAddress"), 0f, 60f, 19f, DiegeticUi.CyanDim, 1000f,
                    TextAlignmentOptions.Center);
                return;
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(list.Count / (float)TargetsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            var first = _page * TargetsPerPage;
            for (var i = first; i < list.Count && i < first + TargetsPerPage; i++)
            {
                var t = list[i];
                var id = FocusContext.AsInt(t["id"]);
                var y = 112f - (i - first) * 54f;
                var picked = id == _selected;
                var row = DiegeticUi.HoloButton(_consoleBody, string.Empty, new Vector2(0f, y), new Vector2(1060f, 48f), () =>
                {
                    _selected = id;
                    RenderConsole();
                }, picked ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                row.GetComponent<Image>().color = picked ? new Color(0.6f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.22f);
                var status = FocusContext.AsString(t["status"]);
                var owner = FocusContext.AsString(t["ownerName"]);
                Line(_consoleBody, "<b>" + Name(t) + "</b>  <size=80%><color=" + StatusColor(status) + ">" +
                                   Trans.Get("vr.gate.status." + status) + "</color>" +
                                   (string.IsNullOrEmpty(owner) ? string.Empty : " · <noparse>" + owner + "</noparse>") +
                                   "</size>", -300f, y, 19f, UiKit.TextBright, 440f);
                Line(_consoleBody, "<mspace=0.62em>" + FocusContext.AsString(t["address"]) + "</mspace>", 90f, y, 18f,
                    new Color(0.8f, 0.7f, 1f, 1f), 300f);
                Line(_consoleBody, Trans.Format("vr.gate.cost", FocusContext.AsString(t["distance"]),
                    FocusContext.AsString(t["energyLoad"])), 380f, y, 15f, DiegeticUi.CyanDim, 260f,
                    TextAlignmentOptions.MidlineRight);
            }

            if (pages > 1)
            {
                DiegeticUi.HoloButton(_consoleBody, "‹", new Vector2(-470f, -205f), new Vector2(64f, 44f), () =>
                {
                    _page = (_page - 1 + pages) % pages;
                    RenderConsole();
                }, DiegeticUi.BtnStyle.Ghost);
                Line(_consoleBody, (_page + 1) + " / " + pages, -390f, -205f, 19f, DiegeticUi.CyanDim, 100f,
                    TextAlignmentOptions.Center);
                DiegeticUi.HoloButton(_consoleBody, "›", new Vector2(-310f, -205f), new Vector2(64f, 44f), () =>
                {
                    _page = (_page + 1) % pages;
                    RenderConsole();
                }, DiegeticUi.BtnStyle.Ghost);
            }

            var target = Target(_selected);
            var open = DiegeticUi.HoloButton(_consoleBody,
                target != null ? Trans.Format("vr.gate.openTo", Name(target)) : Trans.Get("vr.gate.pick"),
                new Vector2(270f, -205f), new Vector2(500f, 56f), () => Run(OpenGate()),
                target != null ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            open.interactable = target != null && !_gate.Busy;
        }

        static string StatusColor(string status) => status switch
        {
            "own" => "#4dffa0",
            "ally" => "#7fd8ff",
            "enemy" => "#ff6b5b",
            _ => "#c8b6ff"
        };

        void ConnectionHeader(bool incoming)
        {
            var other = _conn["otherPlanet"];
            var name = other != null ? Name(other) : "?";
            Line(_consoleBody, "<b>" + Trans.Format(incoming ? "vr.gate.incoming" : "vr.gate.openTowards", name) + "</b>",
                -250f, 180f, 22f, incoming ? UiKit.Danger : new Color(0.8f, 0.7f, 1f, 1f), 540f);
            var expires = FocusContext.AsLong(_conn["expiresAt"]);
            var info = Line(_consoleBody, string.Empty, -250f, 140f, 17f, DiegeticUi.CyanDim, 540f);
            var distance = FocusContext.AsString(_conn["distance"]);
            var energy = FocusContext.AsString(_conn["energyLoad"]);
            _live.Add((info, () =>
                Trans.Format("vr.gate.closesIn", Core.Holo.TravelPlanner.TimeText(expires - FleetOrderGate.UnixNow())) +
                "   ·   " + Trans.Format("vr.gate.cost", distance, energy)));
            DiegeticUi.HoloButton(_consoleBody, Trans.Get("vr.gate.close"), new Vector2(420f, 170f), new Vector2(190f, 56f),
                () => Run(CloseGate()), DiegeticUi.BtnStyle.Danger);
        }

        void RenderIncoming()
        {
            ConnectionHeader(true);
            Line(_consoleBody, Trans.Get("vr.gate.incomingHint"), 0f, 30f, 19f, UiKit.TextDim, 1000f,
                TextAlignmentOptions.Center);
        }

        void RenderMission(PlanetEconomy planet)
        {
            ConnectionHeader(false);
            for (var i = 0; i < MissionTypes.Length; i++)
            {
                var type = MissionTypes[i];
                var b = DiegeticUi.HoloButton(_consoleBody, Trans.Get("vr.gate.mission." + type),
                    new Vector2(-445f + i * 178f, 80f), new Vector2(170f, 48f), () =>
                    {
                        _mission = type;
                        RenderConsole();
                    }, _mission == type ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
                var l = b.GetComponentInChildren<TMP_Text>();
                l.enableAutoSizing = true;
                l.fontSizeMin = 11f;
                l.fontSizeMax = 17f;
            }

            Line(_consoleBody, Trans.Get("vr.gate.missionHint." + _mission), 0f, 30f, 16f, UiKit.TextDim, 1040f,
                TextAlignmentOptions.Center);

            var ready = true;
            if (TroopMission(_mission))
                ready = RenderTroops(planet);
            else if (_mission == "sendResources")
                ready = RenderCargo(planet);
            else if (_mission == "colonize")
                Line(_consoleBody, Trans.Format("vr.gate.homeLevel", planet != null ? planet.Level("home") : 0), 0f, -40f, 19f,
                    UiKit.Amber, 1000f, TextAlignmentOptions.Center);

            var send = DiegeticUi.HoloButton(_consoleBody, Trans.Get("vr.gate.dispatch"), new Vector2(330f, -225f),
                new Vector2(360f, 58f), () => Run(Dispatch()), ready ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            send.interactable = ready;
        }

        bool RenderTroops(PlanetEconomy planet)
        {
            var garrison = TroopCatalog.Counts(planet?.Raw?["troops"]);
            if (garrison.Count == 0)
            {
                Line(_consoleBody, Trans.Get("vr.armory.noTroop"), 0f, -40f, 19f, UiKit.Amber, 1000f,
                    TextAlignmentOptions.Center);
                return false;
            }

            if (!garrison.ContainsKey(_troopType))
                foreach (var kv in garrison)
                {
                    _troopType = kv.Key;
                    break;
                }

            var x = -445f;
            foreach (var kv in garrison)
            {
                var type = kv.Key;
                var b = DiegeticUi.HoloButton(_consoleBody, Trans.Get(type) + "  ×" + Num(kv.Value), new Vector2(x, -30f),
                    new Vector2(200f, 44f), () =>
                    {
                        _troopType = type;
                        RenderConsole();
                    }, type == _troopType ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
                var l = b.GetComponentInChildren<TMP_Text>();
                l.enableAutoSizing = true;
                l.fontSizeMin = 10f;
                l.fontSizeMax = 16f;
                x += 210f;
                if (x > 400f)
                    break;
            }

            var have = garrison[_troopType];
            _qty = Mathf.Clamp(_qty, 1, Mathf.Max(1, have));
            var steps = new[] { -100, -10, -1, 1, 10, 100 };
            for (var i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                DiegeticUi.HoloButton(_consoleBody, (step > 0 ? "+" : "−") + Mathf.Abs(step),
                    new Vector2(-470f + i * 84f + (i >= 3 ? 120f : 0f), -110f), new Vector2(76f, 42f), () =>
                    {
                        _qty = Mathf.Clamp(_qty + step, 1, Mathf.Max(1, have));
                        RenderConsole();
                    }, DiegeticUi.BtnStyle.Ghost);
            }

            Line(_consoleBody, "<b>" + _qty + "</b>", -470f + 2 * 84f + 102f, -110f, 26f, UiKit.TextBright, 100f,
                TextAlignmentOptions.Center);
            DiegeticUi.HoloButton(_consoleBody, Trans.Format("vr.armory.max", have), new Vector2(160f, -110f),
                new Vector2(150f, 42f), () =>
                {
                    _qty = have;
                    RenderConsole();
                }, DiegeticUi.BtnStyle.Ghost);
            return true;
        }

        bool RenderCargo(PlanetEconomy planet)
        {
            var keys = new[] { "vr.res.mineral", "vr.res.crystal", "vr.res.biomass" };
            var stock = planet != null ? new[] { planet.Mineral, planet.Crystal, planet.Biomass } : new float[3];
            var any = false;
            for (var i = 0; i < 3; i++)
            {
                var k = i;
                var y = -20f - i * 56f;
                _cargo[k] = Mathf.Clamp(_cargo[k], 0f, stock[k]);
                any |= _cargo[k] > 0f;
                Line(_consoleBody, Trans.Get(keys[k]), -470f, y, 18f, DiegeticUi.CyanDim, 160f);
                var steps = new[] { -10000, -1000, 1000, 10000 };
                for (var j = 0; j < steps.Length; j++)
                {
                    var step = steps[j];
                    DiegeticUi.HoloButton(_consoleBody, (step > 0 ? "+" : "−") + Num(Mathf.Abs(step)),
                        new Vector2(-300f + j * 110f + (j >= 2 ? 170f : 0f), y), new Vector2(102f, 42f), () =>
                        {
                            _cargo[k] = Mathf.Clamp(_cargo[k] + step, 0f, stock[k]);
                            RenderConsole();
                        }, DiegeticUi.BtnStyle.Ghost);
                }

                Line(_consoleBody, "<b>" + Num(_cargo[k]) + "</b> <size=70%>/ " + Num(stock[k]) + "</size>", -50f, y, 19f,
                    UiKit.TextBright, 170f, TextAlignmentOptions.Center);
            }

            return any;
        }

        void RenderJournal()
        {
            _liveJournal.Clear();
            Clear(_journalBody);
            if (_missions == null)
            {
                Line(_journalBody, Trans.Get("Loading"), 0f, 60f, 20f, DiegeticUi.CyanDim, 700f, TextAlignmentOptions.Center);
                return;
            }

            if (_missions.Count == 0)
            {
                Line(_journalBody, Trans.Get("vr.gate.noMission"), 0f, 60f, 19f, DiegeticUi.CyanDim, 700f,
                    TextAlignmentOptions.Center);
                return;
            }

            for (var i = 0; i < _missions.Count && i < 8; i++)
            {
                var m = _missions[i];
                var y = 190f - i * 56f;
                var type = FocusContext.AsString(m["missionType"]);
                var target = FocusContext.AsString(m["targetPlanetName"]);
                if (string.IsNullOrEmpty(target))
                    target = "#" + FocusContext.AsString(m["targetPlanetId"]);
                Line(_journalBody, "<b>" + Trans.Get("vr.gate.mission." + type) + "</b>  <size=80%><color=#c8b6ff>→ <noparse>" +
                                   target + "</noparse></color></size>", -130f, y + 11f, 17f, UiKit.TextBright, 480f);
                var pending = FocusContext.AsString(m["status"]) == "pending";
                if (pending)
                {
                    var at = FocusContext.AsLong(m["resolveAt"]);
                    var t = Line(_journalBody, string.Empty, -130f, y - 13f, 15f, UiKit.Amber, 480f);
                    _liveJournal.Add((t, () => Trans.Format("vr.gate.pending",
                        Core.Holo.TravelPlanner.TimeText(at - FleetOrderGate.UnixNow()))));
                }
                else
                {
                    var ok = FocusContext.AsString(m["result"]) == "success";
                    Line(_journalBody, ResultLabel(m), -130f, y - 13f, 15f, ok ? UiKit.Ok : UiKit.Danger, 480f);
                }
            }
        }

        /// <summary>
        /// ResolveStargateMission's resultDetail codes (web _stargateResultLabel); failure texts come from the
        /// server's Lang() and are shown as they are.
        /// </summary>
        static string ResultLabel(JToken m)
        {
            var d = FocusContext.AsString(m["resultDetail"]);
            if (d == "delivered")
                return Trans.Get("vr.gate.result.delivered");
            if (d.StartsWith("delivered:", StringComparison.Ordinal))
            {
                var capped = d.Contains(";capped");
                var parts = new List<string>();
                foreach (var kv in d.Substring(10).Split(';')[0].Split(','))
                {
                    var p = kv.Split('=');
                    if (p.Length == 2 && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0f)
                        parts.Add(Num(v) + " " + Trans.Get("vr.res." + p[0]));
                }

                if (parts.Count == 0)
                    return Trans.Get(capped ? "vr.gate.result.nothing" : "vr.gate.result.delivered");
                return Trans.Format(capped ? "vr.gate.result.partial" : "vr.gate.result.deliveredOf", string.Join(", ", parts));
            }

            if (d == "colonized")
                return Trans.Get("vr.gate.result.colonized");
            if (d == "repelled")
                return Trans.Get("vr.gate.result.repelled");
            if (d.StartsWith("researchPoints:", StringComparison.Ordinal))
                return Trans.Format("vr.gate.result.research", d.Substring(15));
            if (d.StartsWith("captured", StringComparison.Ordinal))
                return Trans.Format("vr.gate.result.captured", Losses(d));
            if (d.StartsWith("pillaged", StringComparison.Ordinal))
                return Trans.Format("vr.gate.result.pillaged", Losses(d));
            return string.IsNullOrEmpty(d) ? Trans.Get("vr.gate.result.done") : "<noparse>" + d + "</noparse>";
        }

        static string Losses(string d)
        {
            var i = d.IndexOf("losses:", StringComparison.Ordinal);
            return i >= 0 ? d.Substring(i + 7) : "0";
        }

        // ── Decor that follows the gate ───────────────────────────────────────────

        void SyncDecor()
        {
            for (var i = 0; i < 6; i++)
            {
                var lit = !_gate.LockLit(i) ? 0 : _gate.Incoming ? 2 : 1;
                if (lit == _lampState[i])
                    continue;
                _lampState[i] = lit;
                GateRoomDecor.SetLamp(_decor.PedestalLamps[i],
                    lit == 0 ? new Color(0.3f, 0.14f, 0.06f) : lit == 2 ? new Color(1f, 0.2f, 0.12f) : new Color(1f, 0.6f, 0.2f),
                    lit == 0 ? 0.6f : 3f);
            }

            // Wall monitor: this base, its own address (GetKnownAddresses lists it), the gate's state.
            _eco.TryGet(_planetId, out var planet);
            var name = planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : string.Empty;
            var own = _planetId > 0 ? Target(_planetId) : null;
            var state = _conn == null ? "vr.gate.state.idle" : _gate.Incoming ? "vr.gate.state.incoming" : "vr.gate.state.open";
            var text = _planetId <= 0
                ? Trans.Get("vr.gate.noGate")
                : "<size=55%><color=#9fdcff>" + name + "</color></size>\n<b>" +
                  (own != null ? FocusContext.AsString(own["address"]) : "··-··-··-··-··-··") + "</b>\n<size=55%>" +
                  Trans.Get(state) + "</size>";
            if (_decor.WallMonitor.text != text)
                _decor.WallMonitor.text = text;
        }

        // ── Alarm ─────────────────────────────────────────────────────────────────

        void SetAlarm(bool on)
        {
            if (_alarm == on)
                return;
            _alarm = on;
            foreach (var r in _alarmStrips)
                if (r != null)
                    r.sharedMaterial = on ? _stripAlarm : _stripCalm;
            if (on)
            {
                _klaxonAt = 0f;
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Comms, "gateIncoming", 3);
            }
        }

        void Update()
        {
            if (!Inside)
                return;
            var now = Time.unscaledTime;
            if (now >= _nextLive)
            {
                _nextLive = now + 0.5f;
                foreach (var (text, value) in _live)
                    if (text != null)
                        text.text = value();
                foreach (var (text, value) in _liveJournal)
                    if (text != null)
                        text.text = value();
            }

            SyncDecor();

            if (_alarm)
            {
                _hallLight.color = Color.Lerp(new Color(0.7f, 0.8f, 1f), new Color(1f, 0.2f, 0.15f),
                    0.5f + 0.5f * Mathf.Sin(now * 5f));
                if (now >= _klaxonAt)
                {
                    _klaxonAt = now + 2.4f;
                    CicCue.Klaxon(_gate.transform.position);
                }
            }
            else
            {
                _hallLight.color = new Color(0.7f, 0.8f, 1f);
            }

            if (_busy)
                return;
            if (now >= _statusAt)
            {
                _statusAt = now + StatusEvery;
                Run(RefreshStatus());
            }

            if (now >= _missionsAt)
            {
                _missionsAt = now + MissionsEvery;
                Run(LoadMissions());
            }
        }
    }
}
