using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Core.Utils;
using Core.Vfx;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Core.App
{
    /// <summary>What the watch keeps an eye on: one long affair of the empire (docs/VISION-VR.md §2.1).</summary>
    public enum WatchKind
    {
        Battle,
        Transit,
        Siege,
        Harvest,
        Explore,
        Building,
        Shipyard,
        Research
    }

    public sealed class WatchAffair
    {
        public WatchKind Kind;
        /// <summary>Whose affair it is: the ship, the world, or the research itself.</summary>
        public string Title = string.Empty;
        /// <summary>What exactly and where: the route, the world surveyed, the field mined, the building or module.</summary>
        public string Detail = string.Empty;
        public long Start;
        public long End;
        public int FromSystem;
        public int ToSystem;
        /// <summary>Battle only: our ship is up (the watch asks the captain back aboard).</summary>
        public bool YourTurn;
        /// <summary>Building only: the world whose construction this is (the cluster's Build key opens it).</summary>
        public int PlanetId;

        public float Progress(long now) =>
            End <= Start ? 0f : Mathf.Clamp01((now - Start) / (float)(End - Start));
    }

    /// <summary>
    /// The watch ("quart"): passthrough on, the bridge put away, a small holo cluster in the player's real room
    /// for the one long affair that justifies it (a jump, a siege, a survey, a build, a research, a battle
    /// waiting on the other side), plus the station consoles the player sets out in the room
    /// (<see cref="WatchConsoles"/>). Offered from the left arm pad when something runs longer than two minutes
    /// and the headset has passthrough; "Aboard" brings the captain back to the chair. Not a second client:
    /// the only orders given from here go through the bridge's own Comms / Ops consoles, borrowed. The bridge
    /// keeps running (polls, crew, services); the camera simply
    /// renders the Watch layer alone over a transparent background, with the Meta passthrough behind.
    /// </summary>
    public sealed class WatchMode : MonoBehaviour
    {
        public static WatchMode Instance { get; private set; }
        public static bool Inside { get; private set; }

        /// <summary>Only offered when something lasts at least this long (a shorter wait is not worth a fade).</summary>
        public const long OfferSeconds = 120;

        static readonly Vector3 DeckOrigin = new(0f, -6000f, 0f);

        FocusContext _focus;
        Transform _deck;
        WatchCluster _cluster;
        WatchConsoles _consoles;
        Core.UI.ArmKey _offer;
        readonly List<WatchAffair> _affairs = new();
        readonly Dictionary<int, long> _transitSeen = new();
        float _nextScan;
        bool _busy;
        int _watchLayer = -1;
        HexBattleController _hex;

        // Camera state restored when the captain comes back aboard.
        CameraClearFlags _clear;
        Color _background;
        int _mask;
        ARCameraManager _passthrough;
        ARSession _session;

        public IReadOnlyList<WatchAffair> Affairs => _affairs;

        public static WatchMode Build(Transform room, FocusContext focus)
        {
            var go = new GameObject("WatchMode");
            go.transform.SetParent(room, false);
            var w = go.AddComponent<WatchMode>();
            w._focus = focus;
            w._watchLayer = LayerMask.NameToLayer("Watch");
            w.BuildDeck();
            ReachWatchLayer(FindFirstObjectByType<XROrigin>(), w._watchLayer);
            Instance = w;
            return w;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Inside = false;
        }

        /// <summary>The left arm pad button: lit only when a watch is worth it.</summary>
        public void BindOffer(Core.UI.ArmKey button)
        {
            _offer = button;
            RefreshOffer();
        }

        /// <summary>The headset can show the real room (Quest 3 / Pro). The Editor previews it over a dark room.</summary>
        public static bool Supported
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                var list = new List<XRCameraSubsystemDescriptor>();
                SubsystemManager.GetSubsystemDescriptors(list);
                return list.Count > 0;
#endif
            }
        }

        public bool Worthwhile
        {
            get
            {
                var now = FleetOrderGate.UnixNow();
                foreach (var a in _affairs)
                    if (a.Kind == WatchKind.Battle || a.End - now >= OfferSeconds)
                        return true;
                return false;
            }
        }

        void Update()
        {
            if (Time.unscaledTime < _nextScan)
                return;
            _nextScan = Time.unscaledTime + 1f;
            Scan();
            RefreshOffer();
            if (Inside)
            {
                _cluster.Show(_affairs);
                _consoles.WatchPlanet = _cluster.BuildPlanet;
            }
        }

        void RefreshOffer()
        {
            if (_offer != null)
                _offer.Interactive = !Inside && Supported && Worthwhile && !_busy;
        }

        // ── What is running ─────────────────────────────────────────────────────

        void Scan()
        {
            _affairs.Clear();
            var now = FleetOrderGate.UnixNow();
            var me = FocusContext.OwnedUserId();

            if (_hex == null)
                _hex = FindFirstObjectByType<HexBattleController>();
            var hex = _hex;
            if (hex != null && hex.State != null && hex.IsActive)
                _affairs.Add(new WatchAffair
                {
                    Kind = WatchKind.Battle,
                    Title = Trans.Get("vr.watch.battle"),
                    Detail = Trans.Get(hex.State.MyTurn ? "vr.watch.yourTurn" : "vr.watch.battleWait"),
                    YourTurn = hex.State.MyTurn
                });

            if (_focus != null && me > 0)
            {
                foreach (var f in _focus.Fleets)
                {
                    if (f == null || f.UserId != me)
                        continue;
                    var name = string.IsNullOrEmpty(f.Name) ? "#" + f.Id : f.Name;
                    if (f.IsInBattle && (hex == null || !hex.IsActive))
                        _affairs.Add(new WatchAffair { Kind = WatchKind.Battle, Title = name, Detail = Trans.Get("vr.watch.battleWait") });
                    if (f.DestTime > now)
                    {
                        // The departure time is not in the snapshot: the first time we see the ship under way.
                        if (!_transitSeen.TryGetValue(f.Id, out var seen) || seen > now)
                            _transitSeen[f.Id] = seen = now;
                        _affairs.Add(new WatchAffair
                        {
                            Kind = WatchKind.Transit,
                            Title = name,
                            Detail = GalaxyCatalog.Label(f.FromSystemId > 0 ? f.FromSystemId : f.SystemId) + "  →  " +
                                     GalaxyCatalog.Label(f.DestSystemId > 0 ? f.DestSystemId : f.SystemId),
                            Start = seen,
                            End = f.DestTime,
                            FromSystem = f.FromSystemId > 0 ? f.FromSystemId : f.SystemId,
                            ToSystem = f.DestSystemId > 0 ? f.DestSystemId : f.SystemId
                        });
                    }
                    else
                    {
                        _transitSeen.Remove(f.Id);
                    }

                    var where = GalaxyCatalog.Label(f.SystemId);
                    AddTimer(WatchKind.Siege, name, PlanetName(f.PlanetId) + " · " + where, f.AttackEndTime, now);
                    AddTimer(WatchKind.Harvest, name, Trans.Get("asteroidField") + " #" + f.AsteroidId + " · " + where, f.HarvestEndTime, now);
                    AddTimer(WatchKind.Explore, name, PlanetName(f.PlanetId) + " · " + where, f.ExploreEndTime, now);
                }
            }

            var eco = EconomyService.Instance;
            if (eco != null)
            {
                foreach (var p in eco.Planets.Values)
                {
                    var world = string.IsNullOrEmpty(p.Name) ? "#" + p.Id : p.Name;
                    var end = FocusContext.AsLong(p.Raw?["working"]);
                    if (end > now)
                    {
                        var type = FocusContext.AsString(p.Raw["workingtype"]);
                        // The server raises the level when the work starts: this is the level being built.
                        var level = p.Level(type);
                        _affairs.Add(new WatchAffair
                        {
                            Kind = WatchKind.Building,
                            PlanetId = p.Id,
                            Title = world,
                            Detail = Trans.Get(type) + (level > 0 ? " · " + Trans.Get("level") + " " + level : string.Empty),
                            Start = FocusContext.AsLong(p.Raw["workingStart"]),
                            End = end
                        });
                    }

                    // The dry dock's module under construction (GetResource shipQueue).
                    if (p.Raw?["shipQueue"] is JObject ship)
                    {
                        var shipEnd = FocusContext.AsLong(ship["endTime"]);
                        if (shipEnd > now)
                        {
                            var type = FocusContext.AsString(ship["type"]);
                            var total = (long)FocusContext.AsFloat(Core.Stations.ModuleCatalog.Stats(type)?["time"]);
                            _affairs.Add(new WatchAffair
                            {
                                Kind = WatchKind.Shipyard,
                                Title = world,
                                Detail = Trans.Get(type),
                                Start = total > 0 ? shipEnd - total : 0,
                                End = shipEnd
                            });
                        }
                    }
                }

                var rEnd = FocusContext.AsLong(eco.Empire?["working"]);
                if (rEnd > now)
                    _affairs.Add(new WatchAffair
                    {
                        Kind = WatchKind.Research,
                        Title = Trans.Get(FocusContext.AsString(eco.Empire["workingtype"])),
                        Detail = Trans.Get("vr.watch.researchWhere"),
                        Start = FocusContext.AsLong(eco.Empire["workingStart"]),
                        End = rEnd
                    });
            }

            // The one that justifies the watch first: a battle, then fleets, then the queues; soonest first.
            _affairs.Sort((a, b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.End.CompareTo(b.End));
        }

        void AddTimer(WatchKind kind, string name, string detail, long end, long now)
        {
            if (end <= now)
                return;
            _affairs.Add(new WatchAffair
            {
                Kind = kind,
                Title = name,
                Detail = detail,
                Start = 0,
                End = end
            });
        }

        /// <summary>A world's name: the system on the table, else our worlds, else its number.</summary>
        string PlanetName(int planetId)
        {
            var p = _focus?.FindPlanet(planetId);
            if (p != null && !string.IsNullOrEmpty(p.Name))
                return p.Name;
            var eco = EconomyService.Instance;
            if (eco != null && eco.Planets.TryGetValue(planetId, out var mine) && !string.IsNullOrEmpty(mine.Name))
                return mine.Name;
            return "#" + planetId;
        }

        // ── Enter / leave ───────────────────────────────────────────────────────

        /// <summary>From the bridge or any room: the watch hands the player back where they stood.</summary>
        public void Enter()
        {
            if (!Inside && !_busy && Supported)
                AsyncTap.Run(EnterAsync());
        }

        // Where the player stood before the watch (the rig's parent space: rooms ride their ship).
        Transform _returnParent;
        Vector3 _returnFloor;
        Vector3 _returnForward;

        public void Leave()
        {
            if (Inside && !_busy)
                AsyncTap.Run(LeaveAsync());
        }

        async Task EnterAsync()
        {
            _busy = true;
            RefreshOffer();
            try
            {
                var fade = ViewFade.Ensure();
                await fade.FadeOut(0.5f);
                // Out of the chair first: command mode holds the rig on the seat.
                if (CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode)
                    await CaptainCommandMode.Instance.ExitCommandMode();

                var rig = FindFirstObjectByType<XROrigin>();
                var cam = rig != null ? rig.Camera : Camera.main;
                if (rig != null)
                {
                    _returnParent = rig.transform.parent;
                    var head = cam != null ? cam.transform : rig.transform;
                    var floor = new Vector3(head.position.x, rig.transform.position.y, head.position.z);
                    var fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                    if (fwd.sqrMagnitude < 1e-4f)
                        fwd = rig.transform.forward;
                    _returnFloor = _returnParent != null ? _returnParent.InverseTransformPoint(floor) : floor;
                    _returnForward = _returnParent != null ? _returnParent.InverseTransformDirection(fwd.normalized) : fwd.normalized;
                    rig.transform.SetParent(_deck, false);
                    rig.transform.localPosition = Vector3.zero;
                    rig.transform.localRotation = Quaternion.identity;
                    XrPlacement.PlaceHead(rig, _deck.position, _deck.forward);
                    SetLayer(rig.transform, _watchLayer);
                    ReachWatchLayer(rig, _watchLayer);
                }

                SetLayer(fade.transform, _watchLayer);
                if (cam != null)
                    Passthrough(cam, true);
                Inside = true;
                Scan();
                _cluster.Place(cam != null ? cam.transform : _deck);
                _cluster.Show(_affairs);
                // The station consoles the captain set out in the room last time.
                _consoles.Restore(rig != null ? rig.transform : _deck, cam != null ? cam.transform : _deck);
                await fade.FadeIn(0.5f);
                CicCue.Ok(_cluster.transform.position);
            }
            finally
            {
                _busy = false;
                RefreshOffer();
            }
        }

        async Task LeaveAsync()
        {
            _busy = true;
            try
            {
                var fade = ViewFade.Ensure();
                await fade.FadeOut(0.5f);
                // Layout kept, Comms / Ops consoles handed back to the bridge.
                _consoles.Stash();
                var rig = FindFirstObjectByType<XROrigin>();
                var cam = rig != null ? rig.Camera : Camera.main;
                if (cam != null)
                    Passthrough(cam, false);
                Inside = false;
                _cluster.Hide();
                var bridge = FindFirstObjectByType<BridgeViewRig>();
                if (rig != null && _returnParent != null && _returnParent.gameObject.activeInHierarchy &&
                    (bridge == null || _returnParent != bridge.BridgeMount))
                {
                    // Back in the room the watch was taken from, on the same spot.
                    rig.transform.SetParent(_returnParent, false);
                    XrPlacement.PlaceHead(rig, _returnParent.TransformPoint(_returnFloor),
                        _returnParent.TransformDirection(_returnForward));
                }
                else if (rig != null && bridge != null && bridge.BridgeMount != null)
                {
                    rig.transform.SetParent(bridge.BridgeMount, false);
                    bridge.PutPlayerOnDeck();
                }

                _returnParent = null;

                await fade.FadeIn(0.5f);
            }
            finally
            {
                _busy = false;
                RefreshOffer();
            }
        }

        void Passthrough(Camera cam, bool on)
        {
            if (on)
            {
                _clear = cam.clearFlags;
                _background = cam.backgroundColor;
                _mask = cam.cullingMask;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.cullingMask = _watchLayer >= 0 ? 1 << _watchLayer : 0;
#if UNITY_EDITOR
                // No cameras in the Editor: a dim room stands in for the real one.
                cam.backgroundColor = new Color(0.09f, 0.1f, 0.11f, 1f);
#else
                // Transparent black: the compositor shows the passthrough layer behind.
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                if (_session == null)
                {
                    _session = FindFirstObjectByType<ARSession>();
                    if (_session == null)
                        _session = new GameObject("ARSession").AddComponent<ARSession>();
                }

                _session.enabled = true;
                if (_passthrough == null)
                    _passthrough = cam.GetComponent<ARCameraManager>() ?? cam.gameObject.AddComponent<ARCameraManager>();
                _passthrough.enabled = true;
#endif
                return;
            }

            if (_passthrough != null)
                _passthrough.enabled = false;
            if (_session != null)
                _session.enabled = false;
            cam.clearFlags = _clear;
            cam.backgroundColor = _background;
            cam.cullingMask = _mask;
        }

        /// <summary>
        /// The hands' rays and near casters filter physics and world-canvas hits by layer: without the Watch bit
        /// the watch cluster, borrowed consoles, wrist panel and quick menu (all on Watch) can't be pointed at.
        /// Teleport rays keep their mask (a panel must not swallow a teleport). Idempotent.
        /// </summary>
        static void ReachWatchLayer(XROrigin rig, int layer)
        {
            if (rig == null || layer < 0)
                return;
            var bit = 1 << layer;
            foreach (var c in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.CurveInteractionCaster>(true))
                c.raycastMask |= bit;
            foreach (var s in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Casters.SphereInteractionCaster>(true))
                s.physicsLayerMask |= bit;
            foreach (var d in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>(true))
                d.physicsLayerMask |= bit;
            foreach (var p in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRPokeInteractor>(true))
                p.physicsLayerMask |= bit;
            foreach (var r in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(true))
                if (r.name.IndexOf("Teleport", System.StringComparison.OrdinalIgnoreCase) < 0)
                    r.raycastMask |= bit;
        }

        static void SetLayer(Transform t, int layer)
        {
            if (layer < 0 || t == null)
                return;
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++)
                SetLayer(t.GetChild(i), layer);
        }

        void BuildDeck()
        {
            // An empty place far below everything, with a floor to stand on (the player's own room is the view).
            var deck = new GameObject("WatchDeck");
            deck.transform.position = DeckOrigin;
            _deck = deck.transform;
            var floor = new GameObject("Floor");
            floor.transform.SetParent(_deck, false);
            floor.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            floor.AddComponent<BoxCollider>().size = new Vector3(30f, 0.2f, 30f);
            _cluster = WatchCluster.Build(_deck, _watchLayer, Leave);
            _consoles = WatchConsoles.Build(_deck, _cluster.transform, _watchLayer, _focus);
        }
    }
}
