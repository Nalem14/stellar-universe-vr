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
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Holo
{
    /// <summary>Marks a queue waypoint on the table (rays / pokes on it belong to <see cref="QueuePathView"/>).</summary>
    public sealed class HoloQueueNode : MonoBehaviour
    {
        /// <summary>Queue step indices this waypoint stands for (consecutive steps on one target share it).</summary>
        public readonly List<int> Steps = new();
        public HoloTokenKind Kind;
        public int TargetId;
    }

    /// <summary>One stop of a ship's planned route (table and exterior share it).</summary>
    public readonly struct RouteStop
    {
        public readonly int Step;
        public readonly string Type;
        public readonly HoloTokenKind Kind;
        public readonly int TargetId;
        public readonly float X;
        public readonly float Y;
        public readonly bool Current;

        public RouteStop(int step, string type, HoloTokenKind kind, int targetId, float x, float y, bool current)
        {
            Step = step;
            Type = type;
            Kind = kind;
            TargetId = targetId;
            X = x;
            Y = y;
            Current = current;
        }
    }

    /// <summary>
    /// Holo table v2 H3 — the order queue as an editable 3D path (docs/design/HOLOTABLE.md §4). For the ship
    /// picked on the table (else the one we are aboard): a flowing amber route from the ship through every
    /// step still to run, one numbered waypoint per target (current one cyan), a jump marker on the rim toward
    /// another system, the loop's closing leg dimmer.
    /// Trigger on a waypoint → plate beside it: remove that step, loop on / off, clear the queue. Grip a
    /// waypoint and drop it on another world or rock field → that step now goes there (SetFleetOrderQueue
    /// with the steps still to do). The same route is raised for the real system outside
    /// (<see cref="RouteChanged"/> → ExteriorRouteFx).
    /// </summary>
    public sealed class QueuePathView : MonoBehaviour
    {
        const float Lift = 0.065f;
        const int MaxNodes = 10;
        const float RayLength = 4f;
        const float DropRadius = 0.16f;
        static readonly Color Pending = new(1f, 0.66f, 0.24f, 1f);
        static readonly Color Now = new(0.35f, 1f, 1f, 1f);

        /// <summary>fleet id, stops (in run order), loop — empty stops = no route.</summary>
        public static event Action<int, IReadOnlyList<RouteStop>, bool> RouteChanged;

        HoloZoneMap _map;
        FocusContext _focus;
        CicArtKit _art;
        LineRenderer _line;
        LineRenderer _loopLine;
        Material _flowMat;
        readonly List<Vector3> _points = new();
        readonly List<Vector3> _curve = new();
        readonly List<Node> _nodes = new();
        readonly List<RouteStop> _stops = new();
        MaterialPropertyBlock _mpb;
        static Mesh _diamond;

        int _fleetId;
        string _signature;
        Node _hover;
        Node _held;
        NearFarInteractor[] _rays = Array.Empty<NearFarInteractor>();
        XRPokeInteractor[] _pokes = Array.Empty<XRPokeInteractor>();
        readonly Dictionary<XRPokeInteractor, Node> _touch = new();
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Collider[] _overlap = new Collider[8];
        float _nextScan;
        bool _busy;

        sealed class Node
        {
            public GameObject Root;
            public HoloQueueNode Tag;
            public MeshRenderer Mesh;
            public TextMeshPro Label;
            public XRGrabInteractable Grab;
            public Vector3 Home;
            public bool Current;
            public float Born;
        }

        public static QueuePathView Attach(HoloZoneMap map, FocusContext focus, CicArtKit art)
        {
            if (map == null || map.ContentRoot == null)
                return null;
            var go = new GameObject("QueuePath");
            go.transform.SetParent(map.ContentRoot, false);
            var view = go.AddComponent<QueuePathView>();
            view._map = map;
            view._focus = focus;
            view._art = art;
            view._mpb = new MaterialPropertyBlock();
            view.BuildLines();
            map.TokensRebuilt += view.Refresh;
            if (focus != null)
                focus.FleetsChanged += view.Refresh;
            return view;
        }

        void Start()
        {
            var tc = TacticalCommand.Instance;
            if (tc != null)
                tc.Changed += OnSelection;
        }

        void OnDestroy()
        {
            if (_map != null)
                _map.TokensRebuilt -= Refresh;
            if (_focus != null)
                _focus.FleetsChanged -= Refresh;
            var tc = TacticalCommand.Instance;
            if (tc != null)
                tc.Changed -= OnSelection;
        }

        int _selSeen = -1;

        void OnSelection()
        {
            var sel = TacticalCommand.Instance != null ? TacticalCommand.Instance.SelectedFleetId : 0;
            if (sel == _selSeen)
                return;
            _selSeen = sel;
            Refresh();
        }

        void BuildLines()
        {
            // Flowing route: the move-ghost chevrons scroll along an additive line (one material, offset per frame).
            var shader = Shader.Find("SU/ParticleGlow");
            _flowMat = shader != null ? new Material(shader) { name = "SU_QueueFlow" } : CombatFxKit.Beam();
            if (_flowMat.HasProperty("_MainTex"))
                _flowMat.mainTexture = _art.MoveGhost != null ? _art.MoveGhost : CombatFxKit.BeamTexture();
            if (_flowMat.HasProperty("_Color"))
                _flowMat.SetColor("_Color", Color.white);
            if (_flowMat.HasProperty("_EmissionMul"))
                _flowMat.SetFloat("_EmissionMul", 2.2f);
            _line = Line("Route", 0.007f);
            _loopLine = Line("LoopLeg", 0.004f);
        }

        LineRenderer Line(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.sharedMaterial = _flowMat;
            lr.textureMode = LineTextureMode.Tile;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 2;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        // ── Route ─────────────────────────────────────────────────────────────────

        FocusFleet RouteFleet()
        {
            var sel = TacticalCommand.Instance != null ? TacticalCommand.Instance.SelectedFleetId : 0;
            var f = sel > 0 ? _focus?.FindFleet(sel) : null;
            if (f != null && _focus.IsMine(f) && f.Queue.Count > 0)
                return f;
            return _focus?.FindViewFleet();
        }

        void Refresh()
        {
            if (_held != null)
                return;
            var fleet = RouteFleet();
            var sig = Signature(fleet);
            if (sig == _signature)
                return;
            _signature = sig;
            Rebuild(fleet);
        }

        string Signature(FocusFleet f)
        {
            if (f == null || _map == null || _map.ShowingGalaxy)
                return "none";
            var h = f.Id * 31 + OrderQueue.RunningStep(f) * 7 + (f.QueueLoop ? 1 : 0) + f.PlanetId * 13 + f.AsteroidId * 17;
            foreach (var s in f.Queue)
                h = h * 31 + (s.Type?.GetHashCode() ?? 0) + s.TargetId + (int)s.X * 3 + (int)s.Y * 5;
            return h + "|" + _map.Tokens.Count;
        }

        void Rebuild(FocusFleet ship)
        {
            _points.Clear();
            _stops.Clear();
            _fleetId = ship?.Id ?? 0;
            var used = 0;
            var from = ship != null ? Find(HoloTokenKind.Fleet, ship.Id) : null;
            if (ship != null && from != null && ship.Queue.Count > 0 && !_map.ShowingGalaxy)
            {
                _points.Add(Local(from.transform.position));
                var running = OrderQueue.RunningStep(ship);
                var start = ship.QueueLoop ? 0 : running;
                Node last = null;
                for (var i = start; i < ship.Queue.Count; i++)
                {
                    var step = ship.Queue[i];
                    var current = i == running;
                    Vector3 p;
                    HoloTokenKind kind;
                    if (step.Type == "moveToSystem")
                    {
                        kind = HoloTokenKind.System;
                        p = RimToward(step.X, step.Y);
                    }
                    else
                    {
                        kind = OrderQueue.TargetsAsteroid(step.Type) ? HoloTokenKind.Asteroid : HoloTokenKind.Planet;
                        var token = Find(kind, step.TargetId);
                        if (token == null)
                            continue;
                        p = Local(token.transform.position) + Vector3.up * Lift;
                    }

                    _stops.Add(new RouteStop(i, step.Type, kind, step.TargetId, step.X, step.Y, current));
                    // Consecutive steps on one target (go, then explore) share a waypoint.
                    if (last != null && last.Tag.Kind == kind && last.Tag.TargetId == step.TargetId && kind != HoloTokenKind.System)
                    {
                        last.Tag.Steps.Add(i);
                        last.Current |= current;
                        continue;
                    }

                    if (used >= MaxNodes)
                        continue;
                    last = NodeAt(used++, p, kind, step.TargetId, i, current);
                    _points.Add(p);
                }
            }

            for (var i = used; i < _nodes.Count; i++)
                _nodes[i].Root.SetActive(false);
            foreach (var n in _nodes)
                if (n.Root.activeSelf)
                    Paint(n, ship);

            DrawLines(ship != null && ship.QueueLoop);
            RouteChanged?.Invoke(_fleetId, _stops, ship != null && ship.QueueLoop);
        }

        Node NodeAt(int index, Vector3 pos, HoloTokenKind kind, int targetId, int step, bool current)
        {
            while (_nodes.Count <= index)
                _nodes.Add(CreateNode(_nodes.Count));
            var n = _nodes[index];
            var wasOff = !n.Root.activeSelf;
            n.Root.SetActive(true);
            n.Root.transform.localPosition = pos;
            n.Home = pos;
            n.Tag.Steps.Clear();
            n.Tag.Steps.Add(step);
            n.Tag.Kind = kind;
            n.Tag.TargetId = targetId;
            n.Current = current;
            if (wasOff)
                n.Born = Time.unscaledTime + index * 0.06f;
            // Rim jump markers are shown, not dragged (a system is not a table target).
            n.Grab.enabled = kind != HoloTokenKind.System;
            return n;
        }

        Node CreateNode(int i)
        {
            var go = new GameObject("Waypoint" + i);
            go.transform.SetParent(transform, false);
            var n = new Node { Root = go, Tag = go.AddComponent<HoloQueueNode>() };
            var gem = new GameObject("Gem");
            gem.transform.SetParent(go.transform, false);
            gem.transform.localScale = new Vector3(0.012f, 0.02f, 0.012f);
            gem.AddComponent<MeshFilter>().sharedMesh = Diamond();
            n.Mesh = gem.AddComponent<MeshRenderer>();
            var shader = Shader.Find("SU/HoloCrystal");
            n.Mesh.sharedMaterial = shader != null ? SharedGemMat(shader) : _art.Lit(Texture2D.whiteTexture, Pending, 2f);
            n.Mesh.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var spin = gem.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 70f;
            spin.BobMeters = 0.003f;

            var labelRoot = new GameObject("Label").transform;
            labelRoot.SetParent(go.transform, false);
            labelRoot.localPosition = new Vector3(0f, 0.03f, 0f);
            labelRoot.gameObject.AddComponent<BillboardFace>();
            n.Label = UiKit.Label(labelRoot, "Text", string.Empty, Vector3.zero, 0.3f, 0.0095f, UiKit.TextBright);
            n.Label.richText = true;
            n.Label.outlineWidth = 0.2f;
            n.Label.outlineColor = new Color32(2, 10, 16, 230);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.024f;
            col.isTrigger = true;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            n.Grab = go.AddComponent<XRGrabInteractable>();
            n.Grab.colliders.Clear();
            n.Grab.colliders.Add(col);
            n.Grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            n.Grab.throwOnDetach = false;
            n.Grab.useDynamicAttach = true;
            n.Grab.trackRotation = false;
            n.Grab.selectEntered.AddListener(_ => OnGrab(n));
            n.Grab.selectExited.AddListener(_ => OnDrop(n));
            return n;
        }

        static Material _gemMat;

        static Material SharedGemMat(Shader shader)
        {
            if (_gemMat != null)
                return _gemMat;
            _gemMat = new Material(shader) { name = "SU_QueueGem", enableInstancing = true };
            return _gemMat;
        }

        void Paint(Node n, FocusFleet ship)
        {
            var c = n.Current ? Now : Pending;
            _mpb.Clear();
            _mpb.SetColor("_Color", c * 0.5f);
            _mpb.SetColor("_Emission", c);
            _mpb.SetFloat("_EmissionMul", n.Current ? 1.8f : 1.2f);
            _mpb.SetFloat("_Rim", 2.4f);
            _mpb.SetFloat("_Pulse", n.Current ? 1.4f : 0.3f);
            n.Mesh.SetPropertyBlock(_mpb);

            // "2 · Explore  Ceres" — numbers are the run order (1 = next), verbs are the web's step keys.
            var parts = new List<string>();
            foreach (var i in n.Tag.Steps)
            {
                var rank = ship != null ? RunRank(ship, i) : i + 1;
                parts.Add("<b>" + rank + "</b> " + Trans.Get(OrderQueue.StepKey(ship.Queue[i].Type)));
            }

            var where = n.Tag.Kind == HoloTokenKind.System && ship != null
                ? GalaxyCatalog.Coordinates(ship.Queue[n.Tag.Steps[0]].X, ship.Queue[n.Tag.Steps[0]].Y)
                : string.Empty;
            n.Label.text = string.Join("  ·  ", parts) + (where.Length > 0 ? "  <color=#9fd8ff>" + where + "</color>" : string.Empty);
            n.Label.color = n.Current ? new Color(0.75f, 1f, 1f, 1f) : new Color(1f, 0.85f, 0.6f, 1f);
        }

        static int RunRank(FocusFleet ship, int step) => OrderQueue.RunRank(ship, step);

        void DrawLines(bool loop)
        {
            _curve.Clear();
            for (var i = 1; i < _points.Count; i++)
                Arc(_points[i - 1], _points[i], _curve, i == 1);
            _line.positionCount = _curve.Count;
            if (_curve.Count >= 2)
                _line.SetPositions(_curve.ToArray());
            _line.enabled = _curve.Count >= 2;
            _line.startColor = new Color(1f, 0.72f, 0.3f, 0.95f);
            _line.endColor = new Color(1f, 0.85f, 0.5f, 0.7f);

            // A loop closes back on its first waypoint.
            if (loop && _points.Count > 2)
            {
                _curve.Clear();
                Arc(_points[_points.Count - 1], _points[1], _curve, true);
                _loopLine.positionCount = _curve.Count;
                _loopLine.SetPositions(_curve.ToArray());
                _loopLine.startColor = new Color(1f, 0.7f, 0.3f, 0.45f);
                _loopLine.endColor = new Color(1f, 0.7f, 0.3f, 0.2f);
                _loopLine.enabled = true;
            }
            else
                _loopLine.enabled = false;
        }

        static void Arc(Vector3 a, Vector3 b, List<Vector3> into, bool first)
        {
            const int seg = 14;
            var lift = Mathf.Clamp((b - a).magnitude * 0.22f, 0.012f, 0.1f);
            for (var k = first ? 0 : 1; k <= seg; k++)
            {
                var u = k / (float)seg;
                into.Add(Vector3.Lerp(a, b, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * lift));
            }
        }

        /// <summary>A jump step: marker on the rim in the direction of that system (galaxy map layout).</summary>
        Vector3 RimToward(float x, float y)
        {
            var dir = new Vector2(1f, 0f);
            if (GalaxyCatalog.TryGet(_focus.SystemId, out var here))
            {
                var d = new Vector2(x - here.X, y - here.Y);
                if (d.sqrMagnitude > 1e-4f)
                    dir = d.normalized;
            }

            return new Vector3(dir.x, 0f, dir.y) * (WorldScale.HoloDiscRadius * 0.86f) + Vector3.up * (HoloZoneMap.DioramaLift + 0.02f);
        }

        HoloToken Find(HoloTokenKind kind, int id)
        {
            foreach (var t in _map.Tokens)
                if (t != null && t.Kind == kind && t.Id == id)
                    return t;
            return null;
        }

        Vector3 Local(Vector3 world) => transform.InverseTransformPoint(world);

        // ── Interaction ───────────────────────────────────────────────────────────

        void Update()
        {
            if (_flowMat != null && _line.enabled)
                _flowMat.mainTextureOffset = new Vector2(-Time.unscaledTime * 0.6f, 0f);
            AnimateNodes();
            if (_held != null)
            {
                FollowHeld();
                return;
            }

            if (_map == null || _nodes.Count == 0 || _map.ShowingGalaxy || _map.InteractionLocked)
                return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _rays = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
                _pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsSortMode.None);
            }

            var console = OrderConsole.Instance;
            var busy = _busy || (console != null && console.IsOpen);
            Node aimed = null;
            foreach (var ray in _rays)
            {
                if (ray == null || !ray.isActiveAndEnabled)
                    continue;
                if (ray.TryGetCurrentUIRaycastResult(out RaycastResult ui) && ui.isValid)
                    continue;
                var n = RayNode(ray.transform.position, ray.transform.forward);
                if (n != null && aimed == null)
                    aimed = n;
                if (!busy && n != null && ray.activateInput.ReadWasPerformedThisFrame())
                {
                    AsyncTap.Run(Menu(n));
                    break;
                }
            }

            foreach (var poke in _pokes)
            {
                if (poke == null || !poke.isActiveAndEnabled)
                    continue;
                var tip = poke.attachTransform != null ? poke.attachTransform.position : poke.transform.position;
                var k = Physics.OverlapSphereNonAlloc(tip, 0.02f, _overlap, ~0, QueryTriggerInteraction.Collide);
                Node touching = null;
                for (var i = 0; i < k && touching == null; i++)
                {
                    var tag = _overlap[i].GetComponentInParent<HoloQueueNode>();
                    if (tag != null)
                        touching = NodeOf(tag);
                }

                _touch.TryGetValue(poke, out var before);
                if (!busy && touching != null && touching != before)
                    AsyncTap.Run(Menu(touching));
                _touch[poke] = touching;
            }

            SetHover(aimed);
        }

        Node RayNode(Vector3 origin, Vector3 dir)
        {
            var n = Physics.RaycastNonAlloc(origin, dir, _hits, RayLength, ~0, QueryTriggerInteraction.Collide);
            Node best = null;
            var bestD = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var tag = _hits[i].collider.GetComponentInParent<HoloQueueNode>();
                if (tag == null || _hits[i].distance >= bestD)
                    continue;
                bestD = _hits[i].distance;
                best = NodeOf(tag);
            }

            return best;
        }

        Node NodeOf(HoloQueueNode tag)
        {
            foreach (var n in _nodes)
                if (n.Tag == tag && n.Root.activeSelf)
                    return n;
            return null;
        }

        void SetHover(Node n)
        {
            if (n == _hover)
                return;
            _hover = n;
            if (n != null)
                CicCue.Hover(n.Root.transform.position);
        }

        void AnimateNodes()
        {
            var now = Time.unscaledTime;
            foreach (var n in _nodes)
            {
                if (!n.Root.activeSelf)
                    continue;
                // Waypoints drop in one after another along the route, then breathe when aimed at.
                var born = Mathf.Clamp01((now - n.Born) / 0.35f);
                var k = MotionEase.SmoothOut(born) * (n == _hover || n == _held ? 1.35f : 1f);
                n.Root.transform.localScale = Vector3.one * Mathf.Max(0.001f, k);
            }
        }

        async Task Menu(Node n)
        {
            var ship = _focus?.FindFleet(_fleetId);
            var console = OrderConsole.Instance;
            if (ship == null || console == null || n.Tag.Steps.Count == 0)
                return;
            var options = new List<OrderConsole.Option>();
            foreach (var i in n.Tag.Steps)
                if (i < ship.Queue.Count)
                    options.Add(new OrderConsole.Option(
                        Trans.Get("vr.table.removeStep") + "  ·  " + RunRank(ship, i) + ". " + OrderQueue.Describe(ship.Queue[i], _focus),
                        true, UiKit.Danger, i));
            options.Add(new OrderConsole.Option(Trans.Get("orderQueueLoop") + "  ·  " +
                                                Trans.Get(ship.QueueLoop ? "queueLoopEnabled" : "queueLoopDisabled"),
                true, UiKit.Cyan, "loop"));
            options.Add(new OrderConsole.Option(Trans.Get("clearQueue"), true, UiKit.Danger, "clear"));
            CicCue.Hover(n.Root.transform.position);
            var choice = await console.AskAt(n.Root.transform.position, Trans.Get("orderQueue"), options);
            if (choice == null)
                return;
            _busy = true;
            try
            {
                ApiResult r;
                string ok;
                switch (choice)
                {
                    case int step:
                        r = await OrderQueue.Remove(ship, step);
                        ok = "queueStepRemoved";
                        break;
                    case "loop":
                        r = await OrderQueue.SetLoop(ship, !ship.QueueLoop);
                        ok = ship.QueueLoop ? "queueLoopDisabled" : "queueLoopEnabled";
                        break;
                    default:
                        r = await OrderQueue.Clear(ship);
                        ok = "queueCleared";
                        break;
                }

                await Done(r, ok, n.Root.transform.position);
            }
            finally
            {
                _busy = false;
            }
        }

        void OnGrab(Node n)
        {
            _held = n;
            CicCue.Hover(n.Root.transform.position);
        }

        void FollowHeld()
        {
            // The route stretches to the waypoint in hand; the would-be target glows under it.
            var i = _nodes.IndexOf(_held) + 1;
            if (i > 0 && i < _points.Count)
            {
                _points[i] = Local(_held.Root.transform.position);
                DrawLines(false);
            }
        }

        void OnDrop(Node n)
        {
            _held = null;
            var target = DropTarget(n.Root.transform.position);
            n.Root.transform.localPosition = n.Home;
            _signature = null;
            if (target == null || (target.Kind == n.Tag.Kind && target.Id == n.Tag.TargetId))
            {
                Refresh();
                return;
            }

            AsyncTap.Run(Retarget(n, target));
        }

        HoloToken DropTarget(Vector3 world)
        {
            HoloToken best = null;
            var bestD = DropRadius;
            foreach (var t in _map.Tokens)
            {
                if (t == null || (t.Kind != HoloTokenKind.Planet && t.Kind != HoloTokenKind.Asteroid))
                    continue;
                var p = t.transform.position;
                var d = Vector2.Distance(new Vector2(world.x, world.z), new Vector2(p.x, p.z));
                if (d < bestD)
                {
                    bestD = d;
                    best = t;
                }
            }

            return best;
        }

        /// <summary>Send the waypoint's steps to another target, keeping the rest of the plan as it is.</summary>
        async Task Retarget(Node n, HoloToken target)
        {
            var ship = _focus?.FindFleet(_fleetId);
            if (ship == null || ship.Queue.Count == 0)
            {
                Refresh();
                return;
            }

            var start = ship.QueueLoop ? 0 : OrderQueue.RunningStep(ship);
            var steps = new JArray();
            for (var i = start; i < ship.Queue.Count; i++)
            {
                var step = ship.Queue[i];
                if (!n.Tag.Steps.Contains(i))
                {
                    steps.Add(OrderQueue.ToJson(step));
                    continue;
                }

                var type = RetargetType(step.Type, target.Kind);
                if (type == null)
                {
                    CicCue.Fail(target.transform.position);
                    _map.SetReadout(Trans.Get("vr.table.notATarget"));
                    Refresh();
                    return;
                }

                steps.Add(OrderQueue.Retarget(step, type, target.Id));
            }

            _busy = true;
            try
            {
                var r = await OrderQueue.Replace(ship, steps, ship.QueueLoop);
                await Done(r, "vr.table.queueRerouted", target.transform.position);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Same step on the new target; a move converts between world and rock field, work steps don't.</summary>
        static string RetargetType(string type, HoloTokenKind kind)
        {
            if (kind == HoloTokenKind.Planet)
                return OrderQueue.TargetsPlanet(type) ? type : type == "moveToAsteroid" ? "moveToPlanet" : null;
            if (kind == HoloTokenKind.Asteroid)
                return OrderQueue.TargetsAsteroid(type) ? type : type == "moveToPlanet" ? "moveToAsteroid" : null;
            return null;
        }

        async Task Done(ApiResult r, string okKey, Vector3 at)
        {
            if (r.Ok)
                CicCue.Ok(at);
            else
                CicCue.Fail(at);
            _map.SetReadout(r.Ok ? Trans.Get(okKey) : string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error);
            _signature = null;
            var poller = FindFirstObjectByType<FleetPoller>();
            if (poller != null)
                await poller.PollNow();
            Refresh();
        }

        static Mesh Diamond()
        {
            if (_diamond != null)
                return _diamond;
            // Octahedron with split facets (flat shading for the crystal shader).
            var tip = new Vector3(0f, 1f, 0f);
            var bot = new Vector3(0f, -1f, 0f);
            var ring = new[] { new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f) };
            var v = new List<Vector3>();
            var t = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 4];
                foreach (var apex in new[] { tip, bot })
                {
                    var i0 = v.Count;
                    v.Add(apex);
                    v.Add(apex == tip ? b : a);
                    v.Add(apex == tip ? a : b);
                    t.Add(i0);
                    t.Add(i0 + 1);
                    t.Add(i0 + 2);
                }
            }

            _diamond = new Mesh { name = "QueueGem" };
            _diamond.SetVertices(v);
            _diamond.SetTriangles(t, 0);
            _diamond.RecalculateNormals();
            _diamond.RecalculateBounds();
            return _diamond;
        }
    }
}
