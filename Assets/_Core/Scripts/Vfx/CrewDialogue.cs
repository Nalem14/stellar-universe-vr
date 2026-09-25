using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Star Trek Bridge Crew–style: ray-select a crew mannequin → world-space dialogue
    /// with context orders for the inhabited ship or orbital station.
    /// </summary>
    public sealed class CrewDialogue : MonoBehaviour
    {
        /// <summary>Bridge stations (CrewStationsBuilder.Stations). Domain split follows docs/ROADMAP.md P5.</summary>
        public enum Role
        {
            Helm,
            Tactical,
            Engineering,
            Science,
            Comms,
            Ops
        }

        enum DropGroup
        {
            None = 0,
            Planets,
            Asteroids,
            Jumps,
        }

        static CrewDialogue s_Open;

        FocusContext _focus;
        CicArtKit _art;
        Color _accent;
        Role _role;
        HoloZoneMap _map;
        FleetPoller _poller;
        HexBattleController _hex;
        Transform _anchor;
        Canvas _canvas;
        RectTransform _listRoot;
        TMP_Text _title;
        TMP_Text _subtitle;
        readonly List<GameObject> _rows = new();
        readonly List<GalaxyCatalog.Star> _near = new();
        bool _open;
        float _nextSigPoll;
        long _lastSig = -1;
        int _rebuildGen;
        DropGroup _dropOpen = DropGroup.None;
        bool _rebuildBusy;

        public static void Attach(Transform mannequin, CicEnvironment host, CicArtKit art, Color accent,
            Role role, HexBattleController hex, HoloZoneMap map, FleetPoller poller,
            FocusContext focus, BridgeSystemLoader loader)
        {
            if (mannequin == null || art == null)
                return;

            var hit = mannequin.Find("DialogueHit");
            if (hit == null)
            {
                var hitGo = new GameObject("DialogueHit");
                hit = hitGo.transform;
                hit.SetParent(mannequin, false);
                // Seated body only (seat top → helmet), so rays aimed past the officer are not stolen.
                hit.localPosition = new Vector3(0f, 0.45f, 0.05f);
                var col = hitGo.AddComponent<CapsuleCollider>();
                col.height = 1.0f;
                col.radius = 0.26f;
                col.center = Vector3.zero;
                col.isTrigger = false;
                var rb = hitGo.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            var dialogue = hit.GetComponent<CrewDialogue>();
            if (dialogue == null)
                dialogue = hit.gameObject.AddComponent<CrewDialogue>();

            dialogue._focus = focus;
            dialogue._art = art;
            dialogue._accent = accent;
            dialogue._role = role;
            dialogue._hex = hex;
            dialogue._map = map;
            dialogue._poller = poller;
            dialogue._anchor = hit;
            dialogue.EnsureInteractable();
            dialogue.EnsurePanel(host != null ? host.transform : mannequin.root);
        }

        FocusContext Focus => _focus ?? FocusContext.Current;

        void EnsureInteractable()
        {
            var xi = GetComponent<XRSimpleInteractable>();
            if (xi == null)
                xi = gameObject.AddComponent<XRSimpleInteractable>();
            xi.selectEntered.RemoveListener(OnSelected);
            xi.selectEntered.AddListener(OnSelected);
        }

        void EnsurePanel(Transform room)
        {
            if (_canvas != null)
                return;

            var root = new GameObject("CrewDialoguePanel");
            root.transform.SetParent(room, false);
            root.SetActive(false);

            _canvas = DiegeticUi.WorldCanvas(root.transform, "DialogueCanvas", new Vector2(640f, 560f),
                Vector3.zero, Quaternion.identity, 0.00105f);

            var frame = DiegeticUi.HoloFrame(_canvas.transform, new Vector2(620f, 540f),
                Trans.Get("CommandBridge"));
            _title = DiegeticUi.HoloLabel(frame, RoleTitle(), new Vector2(0f, 180f),
                new Vector2(560f, 40f), 24f, _accent);
            _subtitle = DiegeticUi.HoloLabel(frame, string.Empty, new Vector2(0f, 146f),
                new Vector2(560f, 32f), 16f, DiegeticUi.CyanDim);

            var listGo = new GameObject("Rows", typeof(RectTransform));
            listGo.transform.SetParent(frame, false);
            _listRoot = listGo.GetComponent<RectTransform>();
            _listRoot.sizeDelta = new Vector2(580f, 340f);
            _listRoot.anchoredPosition = new Vector2(0f, -10f);

            DiegeticUi.HoloButton(frame, Trans.Get("close"), new Vector2(0f, -230f), new Vector2(220f, 48f),
                Close, DiegeticUi.BtnStyle.Ghost);
        }

        void OnSelected(SelectEnterEventArgs _)
        {
            if (_open && s_Open == this)
            {
                Close();
                return;
            }

            Open();
        }

        void Open()
        {
            if (s_Open != null && s_Open != this)
                s_Open.Close();

            s_Open = this;
            _open = true;
            _lastSig = -1;
            _dropOpen = DropGroup.None;
            if (_canvas != null)
            {
                var root = _canvas.transform.parent;
                root.gameObject.SetActive(true);
                PlacePanel();
            }

            // The addressed officer turns to the captain while the repeater is open.
            var officer = GetComponentInParent<CrewOfficer>();
            var cam = Camera.main;
            if (officer != null && cam != null)
                officer.LookAt(cam.transform.position);

            CicCue.Ok(transform.position);
            Core.Utils.AsyncTap.Run(RebuildAsync());
        }

        public void Close()
        {
            _open = false;
            _dropOpen = DropGroup.None;
            GetComponentInParent<CrewOfficer>()?.LookAt(null);
            if (s_Open == this)
                s_Open = null;
            ClearRows();
            if (_canvas != null && _canvas.transform.parent != null)
                _canvas.transform.parent.gameObject.SetActive(false);
        }

        /// <summary>
        /// Repeater screen of this officer's station, opened within arm's reach of the captain
        /// (~0.95 m from the eyes), on the bearing of the officer being addressed so the reply reads as
        /// theirs, just under eye line and turned once toward the captain. Never a head-locked billboard,
        /// never out at the station (2 m+ away: unreadable in the headset).
        /// </summary>
        void PlacePanel()
        {
            if (_canvas == null || _anchor == null)
                return;
            var root = _canvas.transform.parent;
            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : _anchor.position + Vector3.back * 2f;
            var toOfficer = Vector3.ProjectOnPlane(_anchor.position - eye, Vector3.up);
            if (toOfficer.sqrMagnitude < 0.01f)
                toOfficer = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            toOfficer.Normalize();
            // Keep it in the comfortable field of view: lean toward the officer, at most 28° off gaze.
            var look = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : toOfficer;
            if (look.sqrMagnitude > 0.01f)
            {
                look.Normalize();
                var angle = Mathf.Clamp(Vector3.SignedAngle(look, toOfficer, Vector3.up), -MaxBearing, MaxBearing);
                toOfficer = Quaternion.AngleAxis(angle, Vector3.up) * look;
            }

            var p = eye + toOfficer * ReachDistance;
            p.y = eye.y - 0.1f;
            root.position = p;
            Core.UI.ScreenMount.FaceViewer(root, eye, 1f);
        }

        const float ReachDistance = 0.95f;
        const float MaxBearing = 28f;

        void Update()
        {
            if (!_open)
                return;
            if (Time.unscaledTime < _nextSigPoll)
                return;
            _nextSigPoll = Time.unscaledTime + 0.45f;
            var focus = Focus;
            var fleet = focus?.FindViewFleet();
            long sig;
            if (fleet == null)
                sig = focus != null ? (-10L - focus.ViewPlanetId) : -2;
            else
            {
                var hex = _hex != null && _hex.IsActive ? 1 : 0;
                sig = fleet.Id ^ (fleet.PlanetId * 17) ^ (fleet.AsteroidId * 31) ^
                      (fleet.IsInBattle ? 1 : 0) ^ fleet.DestTime ^ fleet.AttackEndTime ^
                      fleet.HarvestEndTime ^ fleet.ExploreEndTime ^ (hex * 997);
            }

            if (sig == _lastSig)
                return;
            _lastSig = sig;
            Core.Utils.AsyncTap.Run(RebuildAsync());
        }

        void OnDestroy()
        {
            if (s_Open == this)
                s_Open = null;
        }

        async Task RebuildAsync()
        {
            var gen = ++_rebuildGen;
            while (_rebuildBusy)
                await Task.Yield();
            if (gen != _rebuildGen)
                return;

            _rebuildBusy = true;
            try
            {
                ClearRows();
                if (_listRoot == null)
                    return;

                if (_title != null)
                    _title.text = RoleTitle();

                var focus = Focus;
                var fleet = focus?.FindViewFleet();
                if (_subtitle != null)
                    _subtitle.text = Subtitle(focus, fleet);

                if (focus == null)
                {
                    AddStatus(Trans.Get("Loading"));
                    return;
                }

                // Virtual orbital station: no inhabited ship, so no ship orders; planet-side consoles of each
                // station (colony, shipyard, defences, research, comms) land with docs/ROADMAP.md P5.
                // Boarding a ship goes through the view teleporter; Helm is not even present here.
                if (fleet == null)
                {
                    AddStatus(Trans.Get("vr.crew.standby"));
                    return;
                }

                if (!FleetOrderGate.CanMove(fleet) && _role == Role.Helm)
                {
                    AddStatus(Trans.Get(FleetOrderGate.BusyKey(fleet)));
                    return;
                }

                switch (_role)
                {
                    case Role.Helm:
                        await BuildHelm(focus, fleet);
                        break;
                    case Role.Tactical:
                        BuildTactical(focus, fleet);
                        break;
                    case Role.Engineering:
                        BuildEngineering(focus, fleet);
                        break;
                    case Role.Science:
                        BuildScience(focus, fleet);
                        break;
                    case Role.Ops:
                        BuildOps(focus, fleet);
                        break;
                    // Comms: no fleet order in the API; chat / mail / diplomacy consoles land in P5.
                }

                if (gen != _rebuildGen)
                    return;
                if (_rows.Count == 0)
                    AddStatus(Trans.Get("vr.crew.standby"));
            }
            finally
            {
                _rebuildBusy = false;
            }
        }

        static string Subtitle(FocusContext focus, FocusFleet fleet)
        {
            if (fleet != null)
            {
                var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
                return name + "  ·  #" + fleet.Id;
            }

            if (focus != null && focus.ViewPlanetId > 0)
            {
                var planet = focus.FindPlanet(focus.ViewPlanetId);
                var name = planet != null && !string.IsNullOrEmpty(planet.Name)
                    ? planet.Name
                    : "#" + focus.ViewPlanetId;
                return Trans.Get("planets") + "  ·  " + name;
            }

            return Trans.Get("Loading");
        }

        async Task BuildHelm(FocusContext focus, FocusFleet fleet)
        {
            await GalaxyCatalog.EnsureLoaded();

            if (FleetOrderGate.CanGoToStar(fleet) &&
                GalaxyCatalog.TryGet(focus.SystemId, out var here))
            {
                var sysName = here.Label;
                var hx = here.X;
                var hy = here.Y;
                AddAction(MoveLabel("moveToSystem", sysName),
                    () => MoveToSystem(fleet.Id, here.Id, hx, hy, sysName));
            }

            var planets = new List<FocusPlanet>();
            var seenPlanet = new HashSet<int>();
            foreach (var planet in focus.Planets)
            {
                if (!FleetOrderGate.CanMoveToPlanet(fleet, planet.Id))
                    continue;
                if (!seenPlanet.Add(planet.Id))
                    continue;
                planets.Add(planet);
            }

            if (planets.Count == 1)
            {
                var p = planets[0];
                var pid = p.Id;
                AddAction(MoveLabel("moveToPlanet", PlanetLabel(p)),
                    () => MoveToPlanet(fleet.Id, pid));
            }
            else if (planets.Count > 1)
            {
                AddDropdown(Trans.Get("moveToPlanet"), planets.Count, DropGroup.Planets);
                if (_dropOpen == DropGroup.Planets)
                {
                    BeginDropTray(planets.Count);
                    for (var i = 0; i < planets.Count; i++)
                    {
                        var p = planets[i];
                        var pid = p.Id;
                        AddDropOption(DestLabel(PlanetLabel(p)),
                            () => MoveToPlanet(fleet.Id, pid));
                    }
                }
            }

            var rocks = new List<FocusAsteroid>();
            var seenRock = new HashSet<int>();
            foreach (var rock in focus.Asteroids)
            {
                if (!FleetOrderGate.CanMoveToAsteroid(fleet, rock.Id))
                    continue;
                if (!seenRock.Add(rock.Id))
                    continue;
                rocks.Add(rock);
            }

            if (rocks.Count == 1)
            {
                var aid = rocks[0].Id;
                AddAction(MoveLabel("moveToAsteroidField", Trans.Get("asteroidField") + " #" + aid),
                    () => MoveToAsteroid(fleet.Id, aid));
            }
            else if (rocks.Count > 1)
            {
                AddDropdown(Trans.Get("moveToAsteroidField"), rocks.Count, DropGroup.Asteroids);
                if (_dropOpen == DropGroup.Asteroids)
                {
                    BeginDropTray(rocks.Count);
                    for (var i = 0; i < rocks.Count; i++)
                    {
                        var aid = rocks[i].Id;
                        AddDropOption(DestLabel(Trans.Get("asteroidField") + " #" + aid),
                            () => MoveToAsteroid(fleet.Id, aid));
                    }
                }
            }

            if (FleetOrderGate.CanJumpSystem(fleet))
            {
                GalaxyCatalog.CollectNearest(focus.SystemId, 4, _near);
                if (_near.Count == 1)
                {
                    var star = _near[0];
                    var label = star.Label;
                    var sx = star.X;
                    var sy = star.Y;
                    AddAction(MoveLabel("moveToSystem", label),
                        () => MoveToSystem(fleet.Id, star.Id, sx, sy, label),
                        DiegeticUi.BtnStyle.Amber);
                }
                else if (_near.Count > 1)
                {
                    AddDropdown(Trans.Get("moveToSystem"), _near.Count, DropGroup.Jumps);
                    if (_dropOpen == DropGroup.Jumps)
                    {
                        BeginDropTray(_near.Count);
                        for (var i = 0; i < _near.Count; i++)
                        {
                            var star = _near[i];
                            var label = star.Label;
                            var sx = star.X;
                            var sy = star.Y;
                            AddDropOption(DestLabel(label),
                                () => MoveToSystem(fleet.Id, star.Id, sx, sy, label),
                                DiegeticUi.BtnStyle.Amber);
                        }
                    }
                }
            }
        }

        void BuildTactical(FocusContext focus, FocusFleet fleet)
        {
            var planetLabel = PlanetLabel(focus.FindPlanet(fleet.PlanetId), fleet.PlanetId);

            if (_hex != null && _hex.IsActive)
                AddAction(Trans.Get("vr.tactical.endTurn"), () => _hex.EndTurn());

            if (FleetOrderGate.CanStance(fleet))
            {
                AddAction(ActionLabel("defendPositionRunOut", planetLabel),
                    () => Stance(fleet.Id, "RUN_AWAY"));
                AddAction(ActionLabel("defendPositionPlanet", planetLabel),
                    () => Stance(fleet.Id, "ATTACK_ATTACKER"));
                AddAction(ActionLabel("defendPositionAttacker", planetLabel),
                    () => Stance(fleet.Id, "ATTACK_PLANET"));
            }

            // Engage a hostile ship in system (web: right-click enemy fleet → startTacticalBattle).
            if (_hex != null && !fleet.IsInBattle && FleetOrderGate.CanStance(fleet))
            {
                var now = FleetOrderGate.UnixNow();
                var shown = 0;
                foreach (var target in focus.Fleets)
                {
                    if (shown >= 4)
                        break;
                    if (target.Id == fleet.Id || target.IsInBattle || !target.VisibleIn(focus.SystemId, now))
                        continue;
                    var stance = DiplomacyIndex.ResolveFleet(target);
                    if (stance != EmpireStance.Enemy && stance != EmpireStance.Pirate && !target.IsPirate)
                        continue;
                    var t = target;
                    var name = string.IsNullOrEmpty(t.Name) ? "#" + t.Id : t.Name;
                    AddAction(ActionLabel("attack", name), () => Engage(fleet, t), DiegeticUi.BtnStyle.Danger);
                    shown++;
                }
            }

            if (FleetOrderGate.CanSiege(fleet, focus))
            {
                AddAction(ActionLabel("attackOrbit", planetLabel),
                    () => Issue("FleetAttackPlanet", new Dictionary<string, string>
                    {
                        { "fleet", fleet.Id.ToString() },
                        { "planet", fleet.PlanetId.ToString() }
                    }));
            }
        }

        void BuildEngineering(FocusContext focus, FocusFleet fleet)
        {
            if (FleetOrderGate.CanMine(fleet))
            {
                AddAction(ActionLabel("harvestAsteroid", Trans.Get("asteroidField") + " #" + fleet.AsteroidId),
                    () => Issue("HarvestAsteroid", new Dictionary<string, string>
                    {
                        { "fleet", fleet.Id.ToString() },
                        { "asteroid", fleet.AsteroidId.ToString() }
                    }));
            }

        }

        /// <summary>Science: surveys (ExplorePlanet → research points). Anomalies / research tree: P5.</summary>
        void BuildScience(FocusContext focus, FocusFleet fleet)
        {
            if (FleetOrderGate.CanExplore(fleet))
            {
                var planetLabel = PlanetLabel(focus.FindPlanet(fleet.PlanetId), fleet.PlanetId);
                AddAction(ActionLabel("explorePlanet", planetLabel),
                    () => Issue("ExplorePlanet", new Dictionary<string, string>
                    {
                        { "fleet", fleet.Id.ToString() },
                        { "planet", fleet.PlanetId.ToString() }
                    }));
            }

        }

        /// <summary>Ops: cargo logistics with the planet in orbit. Colonies / buildings: P5.</summary>
        void BuildOps(FocusContext focus, FocusFleet fleet)
        {
            // Colonize the planet in orbit with a ColonyShip module (server: unowned, habitability >= 6,
            // fleet idle). Same module lookup as the web (objects/fleet.js colonizePlanet).
            var orbit = focus.FindPlanet(fleet.PlanetId);
            var colonyModule = ColonyModuleId(fleet);
            if (orbit != null && orbit.UserId == 0 && colonyModule > 0 && FleetOrderGate.CanStance(fleet) &&
                (orbit.Habitability == 0 || orbit.Habitability >= 6))
            {
                AddAction(ActionLabel("Colonize", PlanetLabel(orbit)),
                    () => Issue("Colonize", new Dictionary<string, string>
                    {
                        { "ship", colonyModule.ToString() },
                        { "planet", orbit.Id.ToString() }
                    }));
            }

            if (FleetOrderGate.CanCargo(fleet, focus))
            {
                var planetLabel = PlanetLabel(focus.FindPlanet(fleet.PlanetId), fleet.PlanetId);
                AddAction(ActionLabel("depositCargo", planetLabel),
                    () => Issue("DepositCargo", new Dictionary<string, string>
                    {
                        { "fleet", fleet.Id.ToString() },
                        { "planet", fleet.PlanetId.ToString() }
                    }));
                AddAction(ActionLabel("withdrawCargo", planetLabel),
                    () => Issue("WithdrawCargo", new Dictionary<string, string>
                    {
                        { "fleet", fleet.Id.ToString() },
                        { "planet", fleet.PlanetId.ToString() }
                    }));
            }
        }

        static string ActionLabel(string verbKey, string target)
        {
            var verb = Trans.Get(verbKey);
            if (string.IsNullOrEmpty(target))
                return verb;
            return verb + "  ·  " + target;
        }

        /// <summary>Helm move rows — same action keys as <see cref="HoloOrderPreview"/>.</summary>
        static string MoveLabel(string moveActionKey, string target) =>
            ActionLabel(moveActionKey, target);

        static string DestLabel(string target) =>
            string.IsNullOrEmpty(target) ? "→" : "→  " + target;

        static string PlanetLabel(FocusPlanet planet, int fallbackId = 0)
        {
            if (planet != null)
            {
                if (!string.IsNullOrEmpty(planet.Name))
                    return planet.Name;
                return "#" + planet.Id;
            }

            return fallbackId > 0 ? "#" + fallbackId : "#";
        }

        string RoleTitle()
        {
            switch (_role)
            {
                case Role.Helm:
                    return Trans.Get("vr.station.helm");
                case Role.Tactical:
                    return Trans.Get("vr.station.tactical");
                case Role.Engineering:
                    return Trans.Get("vr.station.engineering");
                case Role.Science:
                    return Trans.Get("vr.station.science");
                case Role.Comms:
                    return Trans.Get("vr.station.comms");
                case Role.Ops:
                    return Trans.Get("vr.station.ops");
                default:
                    return Trans.Get("CommandBridge");
            }
        }

        float _listCursorY = 108f;

        void ResetListCursor() => _listCursorY = 108f;

        void AddDropdown(string title, int count, DropGroup group)
        {
            var open = _dropOpen == group;
            var headerH = 54f;
            var btn = DiegeticUi.HoloSelect(_listRoot, title, count, open,
                new Vector2(0f, _listCursorY), new Vector2(540f, headerH), () =>
                {
                    _dropOpen = open ? DropGroup.None : group;
                    _lastSig = -1;
                    Core.Utils.AsyncTap.Run(RebuildAsync());
                });
            _rows.Add(btn.gameObject);
            _listCursorY -= headerH + 6f;
        }

        void BeginDropTray(int optionCount)
        {
            if (_listRoot == null || optionCount <= 0)
                return;
            const float optH = 44f;
            const float pad = 10f;
            var trayH = optionCount * (optH + 4f) + pad;
            var trayY = _listCursorY - trayH * 0.5f + 4f;
            var tray = DiegeticUi.HoloSelectTray(_listRoot, new Vector2(0f, trayY),
                new Vector2(528f, trayH));
            tray.SetAsFirstSibling(); // behind options added after
            _rows.Add(tray.gameObject);
            _listCursorY -= 6f;
        }

        void AddDropOption(string label, System.Func<Task> act,
            DiegeticUi.BtnStyle style = DiegeticUi.BtnStyle.Ghost)
        {
            if (_listRoot == null)
                return;
            const float h = 44f;
            var btn = DiegeticUi.HoloSelectOption(_listRoot, label, new Vector2(8f, _listCursorY),
                new Vector2(500f, h), () => Core.Utils.AsyncTap.Run(RunOrder(act, refreshAfter: true)), style);
            _rows.Add(btn.gameObject);
            _listCursorY -= h + 4f;
        }

        void AddAction(string label, System.Func<Task> act,
            DiegeticUi.BtnStyle style = DiegeticUi.BtnStyle.Cyan, bool refreshAfter = true)
        {
            if (_listRoot == null)
                return;
            const float h = 48f;
            var btn = DiegeticUi.HoloButton(_listRoot, label, new Vector2(0f, _listCursorY),
                new Vector2(540f, h),
                () => Core.Utils.AsyncTap.Run(RunOrder(act, refreshAfter)), style);
            _rows.Add(btn.gameObject);
            _listCursorY -= h + 6f;
        }

        void AddStatus(string label)
        {
            if (_listRoot == null)
                return;
            var tmp = DiegeticUi.HoloLabel(_listRoot, label, new Vector2(0f, _listCursorY),
                new Vector2(540f, 48f), 20f, DiegeticUi.CyanDim);
            _rows.Add(tmp.gameObject);
            _listCursorY -= 52f;
        }

        void ClearRows()
        {
            _rows.Clear();
            ResetListCursor();
            if (_listRoot == null)
                return;
            // Immediate destroy — deferred Destroy left duplicate rows when Rebuild raced.
            for (var i = _listRoot.childCount - 1; i >= 0; i--)
            {
                var c = _listRoot.GetChild(i).gameObject;
                DestroyImmediate(c);
            }
        }

        /// <summary>
        /// Leaves the button's onClick stack first (rows are DestroyImmediate'd on rebuild), then awaits the
        /// order itself, and only then polls — the follow-up poll must see the server state after the order.
        /// </summary>
        async Task RunOrder(System.Func<Task> act, bool refreshAfter)
        {
            await Task.Yield();
            if (act != null)
                await act();
            if (refreshAfter)
                await AfterOrder();
        }

        async Task AfterOrder()
        {
            if (_poller != null)
                await _poller.PollNow();
            await RebuildAsync();
        }

        async Task MoveToPlanet(int fleetId, int planetId) =>
            await Issue("MoveFleetToPlanet", new Dictionary<string, string>
            {
                { "fleet", fleetId.ToString() },
                { "planet", planetId.ToString() }
            });

        async Task MoveToAsteroid(int fleetId, int asteroidId) =>
            await Issue("MoveFleetToAsteroid", new Dictionary<string, string>
            {
                { "fleet", fleetId.ToString() },
                { "asteroid", asteroidId.ToString() }
            });

        /// <summary>
        /// Interstellar jump: quoted on the captain's lectern (sub-light / hyperspace / Bond PRL, as the web
        /// star menu) and sent only on Confirm — never the server's implicit hyperspace default.
        /// </summary>
        async Task MoveToSystem(int fleetId, int systemId, float x, float y, string systemLabel)
        {
            var fleet = Focus?.FindFleet(fleetId);
            if (fleet == null)
                return;
            var (sent, result, barkAction) =
                await Core.Holo.TravelPlanner.AskAndSend(fleet, systemId, x, y, systemLabel);
            if (!sent)
                return;
            if (!result.Ok)
            {
                CicCue.Fail(transform.position);
                _map?.SetReadout(string.IsNullOrEmpty(result.Error) ? Trans.Get("vr.common.error") : result.Error);
            }
            else
            {
                CicCue.Ok(transform.position);
                _map?.SetReadout(Trans.Get(result.NoticeKey ?? "vr.common.ok"));
            }

            Core.Crew.BarkDirector.Instance?.OrderResult(Role.Helm, barkAction, result, systemLabel);
        }

        async Task Issue(string action, Dictionary<string, string> query, string target = null)
        {
            _map?.SetReadout(Trans.Get("Loading"));
            var result = await ActionJs.Get(action, query);
            if (!result.Ok)
            {
                CicCue.Fail(transform.position);
                // Server errors are already localized (error:{Lang(key)}); never show the raw action id.
                _map?.SetReadout(string.IsNullOrEmpty(result.Error) ? Trans.Get("vr.common.error") : result.Error);
                Core.Crew.BarkDirector.Instance?.OrderResult(_role, action, result, target);
                return;
            }

            CicCue.Ok(transform.position);
            var notice = result.NoticeKey;
            _map?.SetReadout(Trans.Get(notice ?? "vr.common.ok"));
            Core.Crew.BarkDirector.Instance?.OrderResult(_role, action, result, target ?? DescribeTarget(query));
        }

        /// <summary>{0} of a crew line from the order's own params (planet / asteroid).</summary>
        string DescribeTarget(Dictionary<string, string> query)
        {
            var focus = Focus;
            if (query != null && query.TryGetValue("planet", out var p) && int.TryParse(p, out var pid))
                return PlanetLabel(focus?.FindPlanet(pid), pid);
            if (query != null && query.TryGetValue("asteroid", out var a))
                return Trans.Get("asteroidField") + " #" + a;
            return string.Empty;
        }

        static int ColonyModuleId(FocusFleet fleet)
        {
            foreach (var m in fleet.Modules)
            {
                if (m != null && string.Equals(m.Type, "ColonyShip", System.StringComparison.Ordinal))
                    return m.Id;
            }

            return 0;
        }

        async Task Engage(FocusFleet mine, FocusFleet target)
        {
            // Open space against pirates; otherwise the orbit we hold (web startTacticalBattle).
            var planetId = target.IsPirate || DiplomacyIndex.ResolveFleet(target) == EmpireStance.Pirate
                ? 0
                : mine.PlanetId;
            _map?.SetReadout(Trans.Get("Loading"));
            var result = await _hex.MakeBattle(new[] { mine.Id, target.Id }, planetId);
            Core.Crew.BarkDirector.Instance?.OrderResult(Role.Tactical, "MakeBattle", result,
                string.IsNullOrEmpty(target.Name) ? "#" + target.Id : target.Name);
            if (result.Ok)
            {
                CicCue.Ok(transform.position);
                _map?.SetReadout(Trans.Get("tacticalBattle"));
            }
            else
            {
                CicCue.Fail(transform.position);
                _map?.SetReadout(string.IsNullOrEmpty(result.Error) ? Trans.Get("vr.common.error") : result.Error);
            }
        }

        Task Stance(int fleetId, string position) =>
            Issue("UpdateFleetDefendPosition", new Dictionary<string, string>
            {
                { "id", fleetId.ToString() },
                { "position", position }
            });
    }
}
