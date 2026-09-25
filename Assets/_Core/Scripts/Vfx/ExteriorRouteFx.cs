using System.Collections.Generic;
using Core.App;
using Core.Holo;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// What the table plans, seen through the windows: the queue route of the ship picked on the table (else
    /// the one we are aboard) drawn in the real system — a flowing line from the real hull through the real
    /// worlds and rock fields, a numbered navigation beacon (ring + light pillar) on each stop, a jump beacon
    /// far out toward another system — and a cyan target beacon on the real body aimed at on the table while a
    /// ship is selected. Pooled: one line, 10 route beacons, 1 aim beacon; positions follow the bodies in
    /// LateUpdate (no allocation per frame).
    /// </summary>
    public sealed class ExteriorRouteFx : MonoBehaviour
    {
        const int MaxBeacons = 10;
        const float JumpBeaconDistance = 900f;
        static readonly Color Pending = new(1f, 0.66f, 0.24f, 1f);
        static readonly Color Now = new(0.35f, 1f, 1f, 1f);

        SystemExterior _exterior;
        CicArtKit _art;
        FocusContext _focus;
        LineRenderer _line;
        Material _flow;
        Material _ringMat;
        Material _pillarMat;
        readonly List<Beacon> _beacons = new();
        Beacon _aim;
        int _fleetId;
        bool _loop;
        readonly List<RouteStop> _stops = new();
        readonly List<Vector3> _pts = new();
        Vector3[] _curve = new Vector3[0];

        sealed class Beacon
        {
            public GameObject Root;
            public Transform Ring;
            public Transform Pillar;
            public TextMeshPro Label;
            public MeshRenderer RingRenderer;
            public Transform Body;
            public Vector3 Fixed;
            public float Radius;
            public Color Color;
        }

        public static ExteriorRouteFx Attach(SystemExterior exterior, CicArtKit art, FocusContext focus)
        {
            var fx = exterior.gameObject.AddComponent<ExteriorRouteFx>();
            fx._exterior = exterior;
            fx._art = art;
            fx._focus = focus;
            fx.Build();
            QueuePathView.RouteChanged += fx.OnRoute;
            return fx;
        }

        void Start()
        {
            if (TacticalCommand.Instance != null)
                TacticalCommand.Instance.Changed += OnAim;
        }

        void OnDestroy()
        {
            QueuePathView.RouteChanged -= OnRoute;
            if (TacticalCommand.Instance != null)
                TacticalCommand.Instance.Changed -= OnAim;
        }

        void Build()
        {
            var shader = Shader.Find("SU/ParticleGlow");
            _flow = shader != null ? new Material(shader) { name = "SU_ExteriorRoute" } : CombatFxKit.Beam();
            if (_flow.HasProperty("_MainTex"))
                _flow.mainTexture = _art.MoveGhost != null ? _art.MoveGhost : CombatFxKit.BeamTexture();
            if (_flow.HasProperty("_EmissionMul"))
                _flow.SetFloat("_EmissionMul", 2f);
            _ringMat = _art.RadarIcon(_art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture, Color.white);
            _pillarMat = CombatFxKit.Beam();

            var go = new GameObject("ExteriorRoute");
            go.transform.SetParent(transform, false);
            _line = go.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.sharedMaterial = _flow;
            _line.textureMode = LineTextureMode.Tile;
            _line.numCornerVertices = 2;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.enabled = false;
            for (var i = 0; i < MaxBeacons; i++)
                _beacons.Add(CreateBeacon("RouteBeacon" + i));
            _aim = CreateBeacon("AimBeacon");
        }

        Beacon CreateBeacon(string name)
        {
            var b = new Beacon { Root = new GameObject(name) };
            b.Root.transform.SetParent(transform, false);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "Ring";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(b.Root.transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            b.RingRenderer = ring.GetComponent<MeshRenderer>();
            b.RingRenderer.sharedMaterial = _ringMat;
            b.RingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.Ring = ring.transform;
            var spin = ring.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 12f;
            spin.BobMeters = 0f;

            // Light pillar: a camera-facing beam quad standing on the body (reads from any window).
            var pillar = new GameObject("Pillar");
            pillar.transform.SetParent(b.Root.transform, false);
            var lr = pillar.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, Vector3.up);
            lr.textureMode = LineTextureMode.Stretch;
            lr.sharedMaterial = _pillarMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            b.Pillar = pillar.transform;

            var labelRoot = new GameObject("Label").transform;
            labelRoot.SetParent(b.Root.transform, false);
            labelRoot.gameObject.AddComponent<BillboardFace>();
            b.Label = UiKit.Label(labelRoot, "Text", string.Empty, Vector3.zero, 0.5f, 0.05f, UiKit.TextBright);
            b.Label.fontStyle = FontStyles.Bold;
            b.Label.outlineWidth = 0.2f;
            b.Label.outlineColor = new Color32(2, 8, 12, 220);
            b.Root.SetActive(false);
            return b;
        }

        // ── Route ─────────────────────────────────────────────────────────────────

        void OnRoute(int fleetId, IReadOnlyList<RouteStop> stops, bool loop)
        {
            _fleetId = fleetId;
            _loop = loop;
            _stops.Clear();
            for (var i = 0; i < stops.Count; i++)
                _stops.Add(stops[i]);

            var used = 0;
            var ship = _focus?.FindFleet(fleetId);
            for (var i = 0; i < _stops.Count && used < MaxBeacons; i++)
            {
                var s = _stops[i];
                // One beacon per target, like the table's waypoints.
                if (i > 0 && _stops[i - 1].Kind == s.Kind && _stops[i - 1].TargetId == s.TargetId && s.Kind != HoloTokenKind.System)
                {
                    var prev = _beacons[used - 1];
                    prev.Label.text += "  ·  " + Rank(ship, s.Step);
                    if (s.Current)
                        Tint(prev, Now);
                    continue;
                }

                var b = _beacons[used];
                if (!Place(b, s))
                    continue;
                b.Label.text = Rank(ship, s.Step) + "  " + Trans.Get(OrderQueue.StepKey(s.Type));
                Tint(b, s.Current ? Now : Pending);
                b.Root.SetActive(true);
                used++;
            }

            for (var i = used; i < _beacons.Count; i++)
                _beacons[i].Root.SetActive(false);
            _line.enabled = used > 0 && _exterior.TryGetFleet(fleetId, out _);
        }

        static string Rank(FocusFleet ship, int step) =>
            (ship != null ? OrderQueue.RunRank(ship, step) : step + 1).ToString();

        bool Place(Beacon b, RouteStop s)
        {
            b.Body = null;
            if (s.Kind == HoloTokenKind.Planet && _exterior.TryGetPlanet(s.TargetId, out var p))
                b.Body = p;
            else if (s.Kind == HoloTokenKind.Asteroid && _exterior.TryGetAsteroid(s.TargetId, out var a))
                b.Body = a;
            else if (s.Kind == HoloTokenKind.System)
            {
                // Out toward that system: a jump beacon on the ecliptic, far beyond the worlds.
                var dir = Vector3.right;
                if (GalaxyCatalog.TryGet(_focus.SystemId, out var here))
                {
                    var d = new Vector2(s.X - here.X, s.Y - here.Y);
                    if (d.sqrMagnitude > 1e-4f)
                        dir = new Vector3(d.normalized.x, 0f, d.normalized.y);
                }

                b.Fixed = _exterior.transform.position + dir * JumpBeaconDistance;
                b.Radius = 30f;
                Size(b);
                return true;
            }
            else
                return false;

            b.Radius = BodyRadius(b.Body);
            Size(b);
            return true;
        }

        static float BodyRadius(Transform body)
        {
            var r = body.GetComponentInChildren<Renderer>();
            return r != null ? Mathf.Max(4f, r.bounds.extents.x) : 12f;
        }

        static void Size(Beacon b)
        {
            b.Ring.localScale = Vector3.one * (b.Radius * 2.6f);
            b.Pillar.localScale = new Vector3(1f, b.Radius * 2.2f, 1f);
            var lr = b.Pillar.GetComponent<LineRenderer>();
            lr.widthMultiplier = Mathf.Max(0.6f, b.Radius * 0.06f);
            b.Label.transform.parent.localPosition = Vector3.up * (b.Radius * 2.4f);
            b.Label.transform.parent.localScale = Vector3.one * Mathf.Max(60f, b.Radius * 4f);
        }

        void Tint(Beacon b, Color c)
        {
            b.Color = c;
            var lr = b.Pillar.GetComponent<LineRenderer>();
            lr.startColor = new Color(c.r, c.g, c.b, 0.8f);
            lr.endColor = new Color(c.r, c.g, c.b, 0f);
            b.Label.color = Color.Lerp(c, Color.white, 0.3f);
            var mpb = new MaterialPropertyBlock();
            b.RingRenderer.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", c);
            mpb.SetColor("_Emission", c);
            b.RingRenderer.SetPropertyBlock(mpb);
        }

        // ── Aim beacon (table hover → the real body) ──────────────────────────────

        void OnAim()
        {
            var t = TacticalCommand.Instance != null ? TacticalCommand.Instance.AimedTarget : null;
            if (t == null)
            {
                _aim.Root.SetActive(false);
                return;
            }

            var stop = new RouteStop(0, "moveToPlanet", t.Kind, t.Id, t.GalaxyX, t.GalaxyY, true);
            if (t.Kind != HoloTokenKind.Planet && t.Kind != HoloTokenKind.Asteroid || !Place(_aim, stop))
            {
                _aim.Root.SetActive(false);
                return;
            }

            _aim.Label.text = string.IsNullOrEmpty(t.DisplayName) ? string.Empty : t.DisplayName;
            Tint(_aim, new Color(0.45f, 1f, 0.6f, 1f));
            _aim.Root.SetActive(true);
        }

        // ── Follow the bodies ─────────────────────────────────────────────────────

        void LateUpdate()
        {
            var now = Time.unscaledTime;
            Follow(_aim, now, 1.2f);
            if (!_line.enabled)
            {
                foreach (var b in _beacons)
                    if (b.Root.activeSelf)
                        Follow(b, now, 1f);
                return;
            }

            _pts.Clear();
            if (_exterior.TryGetFleet(_fleetId, out var ship))
                _pts.Add(ship.position);
            foreach (var b in _beacons)
            {
                if (!b.Root.activeSelf)
                    continue;
                Follow(b, now, 1f);
                _pts.Add(b.Root.transform.position);
            }

            if (_loop && _pts.Count > 2)
                _pts.Add(_pts[1]);
            const int seg = 10;
            var count = _pts.Count < 2 ? 0 : (_pts.Count - 1) * seg + 1;
            if (_curve.Length != count)
                _curve = new Vector3[count];
            for (var i = 1; i < _pts.Count; i++)
            {
                var a = _pts[i - 1];
                var c = _pts[i];
                var lift = Mathf.Min((c - a).magnitude * 0.12f, 60f);
                for (var k = i == 1 ? 0 : 1; k <= seg; k++)
                {
                    var u = k / (float)seg;
                    _curve[(i - 1) * seg + k] = Vector3.Lerp(a, c, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * lift);
                }
            }

            _line.positionCount = count;
            if (count > 0)
                _line.SetPositions(_curve);
            _line.widthMultiplier = 1.4f;
            _line.startColor = new Color(1f, 0.72f, 0.3f, 0.85f);
            _line.endColor = new Color(1f, 0.85f, 0.5f, 0.55f);
            _flow.mainTextureOffset = new Vector2(-now * 0.5f, 0f);
            _flow.mainTextureScale = new Vector2(0.02f, 1f);
        }

        static void Follow(Beacon b, float now, float pulse)
        {
            if (!b.Root.activeSelf)
                return;
            b.Root.transform.position = b.Body != null ? b.Body.position : b.Fixed;
            var k = 1f + Mathf.Sin(now * 2.2f) * 0.05f * pulse;
            b.Ring.localScale = Vector3.one * (b.Radius * 2.6f * k);
        }
    }
}
