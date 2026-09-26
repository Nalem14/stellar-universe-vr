using System.Collections.Generic;
using System.Text;
using Core.App;
using Core.Holo;
using Core.Stations;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Jumpgates in the shared <see cref="SystemExterior"/>: every world of ours in the focused system with a
    /// Jumpgate (planets.jumpgate &gt; 0) has one standing off its orbit — a machined twelve-sided ring on its
    /// axis of travel, four fold emitters, violet lamps when ready, amber while recharging
    /// (jumpgateReadyAt). A ship folding away through it (<see cref="JumpgateNetwork.Jumped"/>) opens the
    /// fold field, streaks into it and vanishes in a flash; a ship of ours arriving at a gate world in
    /// transit opens it the other way. Shared meshes and lamp materials; one horizon material per gate
    /// (a system holds one or two), its renderer off while closed (no idle fill-rate).
    /// </summary>
    public sealed class ExteriorJumpgateFx : MonoBehaviour
    {
        const float Outer = 15f;
        const float OpenSeconds = 0.45f;
        const float HoldSeconds = 2.6f;
        const float CloseSeconds = 0.7f;
        const int StreakPool = 3;

        static Mesh _ringMesh;
        static Mesh _lampMesh;
        static Mesh _discMesh;
        static Material _lampReady;
        static Material _lampRecharge;

        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int SurgeId = Shader.PropertyToID("_Surge");

        SystemExterior _exterior;
        FocusContext _focus;
        EconomyService _eco;
        Transform _root;
        ParticleSystem _flash;
        string _sig = string.Empty;
        float _nextLampCheck;
        readonly HashSet<int> _seenFleets = new();
        int _seenSystem = -1;

        sealed class Gate
        {
            public int PlanetId;
            public Transform Tr;
            public Transform Ring;
            public MeshRenderer Lamps;
            public MeshRenderer Field;
            public Material FieldMat;
            public float Standoff;
            public float Angle;
            /// <summary>Seconds since the field started opening (-1 = closed).</summary>
            public float T = -1f;
        }

        sealed class Streak
        {
            public TrailRenderer Trail;
            public Vector3 From;
            public Gate Gate;
            public float T = -1f;
        }

        readonly List<Gate> _gates = new();
        readonly List<Streak> _streaks = new();

        public static ExteriorJumpgateFx Instance { get; private set; }

        public static ExteriorJumpgateFx Attach(SystemExterior exterior, FocusContext focus, EconomyService eco)
        {
            var fx = exterior.gameObject.AddComponent<ExteriorJumpgateFx>();
            Instance = fx;
            fx._exterior = exterior;
            fx._focus = focus;
            fx._eco = eco;
            fx.Build();
            if (focus != null)
            {
                focus.Changed += fx.Refresh;
                focus.FleetsChanged += fx.WatchArrivals;
            }

            if (eco != null)
                eco.Changed += fx.Refresh;
            JumpgateNetwork.Jumped += fx.OnJumped;
            return fx;
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= Refresh;
                _focus.FleetsChanged -= WatchArrivals;
            }

            if (_eco != null)
                _eco.Changed -= Refresh;
            JumpgateNetwork.Jumped -= OnJumped;
            if (Instance == this)
                Instance = null;
            foreach (var g in _gates)
                if (g.FieldMat != null)
                    Destroy(g.FieldMat);
        }

        void Build()
        {
            _root = new GameObject("JumpgateFx").transform;
            _root.SetParent(transform, false);
            _flash = CombatFxKit.Burst(_root, "FoldFlash", 96, 1.1f, gravity: false, stretch: true);
            for (var i = 0; i < StreakPool; i++)
            {
                var go = new GameObject("FoldStreak" + i);
                go.transform.SetParent(_root, false);
                var trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.35f;
                trail.minVertexDistance = 1.5f;
                trail.widthMultiplier = 3.2f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                trail.colorGradient = Fade(JumpgateNetwork.JumpTint);
                trail.sharedMaterial = CombatFxKit.Beam();
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
                go.SetActive(false);
                _streaks.Add(new Streak { Trail = trail });
            }
        }

        static Gradient Fade(Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // ── Which worlds have a gate ──────────────────────────────────────────────

        void Refresh()
        {
            if (_focus == null || _eco == null)
                return;
            var sig = new StringBuilder();
            sig.Append(_focus.SystemId).Append(':');
            var wanted = new List<int>();
            foreach (var p in _focus.Planets)
            {
                if (!OwnedPlanets.Contains(p.Id) || !_eco.TryGet(p.Id, out var e) || e.Level("jumpgate") <= 0)
                    continue;
                wanted.Add(p.Id);
                sig.Append(p.Id).Append(',');
            }

            var s = sig.ToString();
            if (s == _sig)
                return;
            _sig = s;
            foreach (var g in _gates)
            {
                if (g.FieldMat != null)
                    Destroy(g.FieldMat);
                if (g.Tr != null)
                    Destroy(g.Tr.gameObject);
            }

            _gates.Clear();
            foreach (var id in wanted)
                _gates.Add(MakeGate(id));
            UpdateLamps();
        }

        Gate MakeGate(int planetId)
        {
            var root = new GameObject("Jumpgate_" + planetId).transform;
            root.SetParent(_root, false);
            root.localScale = Vector3.one * Outer;

            // Lathe meshes turn about Y: the ring child maps that axis onto the gate's travel axis (+Z).
            var ring = new GameObject("Ring").transform;
            ring.SetParent(root, false);
            ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(ring, "Hull", RingMesh, DefensePlatformKit.HullMat());
            var lamps = Part(ring, "Lamps", LampMesh, LampReady);

            var fieldMat = FieldMaterial();
            var field = Part(root, "FoldField", DiscMesh, fieldMat);
            field.transform.localScale = new Vector3(0.8f, 0.8f, 1f);
            field.enabled = false;

            var p = _focus.FindPlanet(planetId);
            var radius = WorldScale.PlanetRadius(p != null ? p.Slot : 1);
            return new Gate
            {
                PlanetId = planetId,
                Tr = root,
                Ring = ring,
                Lamps = lamps,
                Field = field,
                FieldMat = fieldMat,
                Standoff = radius + Outer * 2.4f,
                Angle = 35f + planetId * 47 % 290
            };
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        // ── Loop ──────────────────────────────────────────────────────────────────

        void Update()
        {
            if (_gates.Count == 0 && !AnyStreak())
                return;
            var dt = Time.deltaTime;
            foreach (var g in _gates)
            {
                if (g.Tr == null || !Pose(g, out var pos, out var rot))
                    continue;
                g.Tr.SetPositionAndRotation(pos, rot);
                // The ring turns slowly on its axis; the emitters sweep the field.
                g.Ring.Rotate(0f, 4f * dt, 0f, Space.Self);
                AnimateField(g, dt);
            }

            foreach (var s in _streaks)
                AnimateStreak(s, dt);

            if (Time.unscaledTime >= _nextLampCheck)
            {
                _nextLampCheck = Time.unscaledTime + 1f;
                UpdateLamps();
            }
        }

        bool AnyStreak()
        {
            foreach (var s in _streaks)
                if (s.T >= 0f)
                    return true;
            return false;
        }

        bool Pose(Gate g, out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;
            if (!_exterior.TryGetPlanet(g.PlanetId, out var planet))
                return false;
            var a = g.Angle * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            pos = planet.position + outward * g.Standoff + Vector3.up * (Outer * 0.35f);
            // Travel axis along the orbit (tangent), so a ship leaving the world crosses the ring.
            var tangent = new Vector3(-outward.z, 0f, outward.x);
            rot = Quaternion.LookRotation(tangent, Vector3.up);
            return true;
        }

        void UpdateLamps()
        {
            if (_eco == null)
                return;
            foreach (var g in _gates)
            {
                if (g.Lamps == null || !_eco.TryGet(g.PlanetId, out var e))
                    continue;
                var mat = JumpgateNetwork.RechargeLeft(e) > 0 ? LampRecharge : LampReady;
                if (g.Lamps.sharedMaterial != mat)
                    g.Lamps.sharedMaterial = mat;
            }
        }

        void AnimateField(Gate g, float dt)
        {
            if (g.T < 0f)
                return;
            g.T += dt;
            float open;
            if (g.T < OpenSeconds)
                open = Mathf.SmoothStep(0f, 1f, g.T / OpenSeconds);
            else if (g.T < OpenSeconds + HoldSeconds)
                open = 1f;
            else if (g.T < OpenSeconds + HoldSeconds + CloseSeconds)
                open = 1f - Mathf.SmoothStep(0f, 1f, (g.T - OpenSeconds - HoldSeconds) / CloseSeconds);
            else
            {
                g.T = -1f;
                g.Field.enabled = false;
                return;
            }

            g.FieldMat.SetFloat(OpenId, open);
            var surge = g.T < OpenSeconds * 2f ? Mathf.Sin(g.T / (OpenSeconds * 2f) * Mathf.PI) : 0f;
            g.FieldMat.SetFloat(SurgeId, surge);
        }

        void Open(Gate g)
        {
            if (g.T >= 0f && g.T < OpenSeconds + HoldSeconds)
            {
                g.T = Mathf.Min(g.T, OpenSeconds);
                return;
            }

            g.T = 0f;
            g.FieldMat.SetFloat(OpenId, 0f);
            g.Field.enabled = true;
            if (g.Tr == null)
                return;
            var c = g.Tr.position;
            var axis = g.Tr.forward;
            for (var i = 0; i < 26; i++)
            {
                var a = i / 26f * Mathf.PI * 2f;
                var rim = g.Tr.TransformPoint(new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.8f);
                CombatFxKit.Emit(_flash, rim, JumpgateNetwork.JumpTint, 2.4f, 0.7f, (c - rim) * 1.6f + axis * 6f);
            }
        }

        // ── Departures and arrivals ───────────────────────────────────────────────

        /// <summary>World pose of the gate standing off <paramref name="planetId"/> (forward = its travel axis).</summary>
        public bool TryGatePose(int planetId, out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = Quaternion.identity;
            var g = GateAt(planetId);
            return g != null && Pose(g, out pos, out rot);
        }

        /// <summary>Open the fold field of the gate at <paramref name="planetId"/> (the inhabited ship comes through).</summary>
        public void OpenGate(int planetId)
        {
            var g = GateAt(planetId);
            if (g != null)
                Open(g);
        }

        Gate GateAt(int planetId)
        {
            foreach (var g in _gates)
                if (g.PlanetId == planetId)
                    return g;
            return null;
        }

        void OnJumped(int fleetId, int originPlanet, int targetPlanet)
        {
            var gate = GateAt(originPlanet);
            if (gate == null)
                return;
            Open(gate);
            if (!_exterior.TryGetFleet(fleetId, out var ship))
                return;
            foreach (var s in _streaks)
            {
                if (s.T >= 0f)
                    continue;
                s.From = ship.position;
                s.Gate = gate;
                s.T = 0f;
                s.Trail.transform.position = s.From;
                s.Trail.Clear();
                s.Trail.gameObject.SetActive(true);
                s.Trail.emitting = true;
                break;
            }

            // The real hull folds with it (the next poll drops it from this system).
            ship.gameObject.SetActive(false);
        }

        void AnimateStreak(Streak s, float dt)
        {
            if (s.T < 0f)
                return;
            s.T += dt;
            const float dive = 0.9f;
            if (s.Gate == null || s.Gate.Tr == null)
            {
                End(s);
                return;
            }

            var center = s.Gate.Tr.position;
            var beyond = center + s.Gate.Tr.forward * (Outer * 1.6f);
            if (s.T < dive)
            {
                var k = s.T / dive;
                s.Trail.transform.position = Vector3.Lerp(s.From, center, k * k);
            }
            else if (s.T < dive + 0.12f)
            {
                s.Trail.transform.position = Vector3.Lerp(center, beyond, (s.T - dive) / 0.12f);
            }
            else if (s.T < dive + 0.14f)
            {
                s.Trail.emitting = false;
                for (var i = 0; i < 18; i++)
                    CombatFxKit.Emit(_flash, center, Color.white, 3.2f, 0.5f, Random.onUnitSphere * 14f);
                s.T = dive + 0.2f;
            }
            else if (s.T > dive + 0.6f)
            {
                End(s);
            }
        }

        static void End(Streak s)
        {
            s.T = -1f;
            s.Trail.emitting = false;
            s.Trail.gameObject.SetActive(false);
        }

        /// <summary>A ship of ours newly here, docked at a gate world while in transit, came through the gate.</summary>
        void WatchArrivals()
        {
            if (_focus == null)
                return;
            var sys = _focus.SystemId;
            var now = FleetOrderGate.UnixNow();
            var me = FocusContext.OwnedUserId();
            var first = sys != _seenSystem;
            if (first)
            {
                _seenSystem = sys;
                _seenFleets.Clear();
            }

            foreach (var f in _focus.Fleets)
            {
                if (!f.IsPresentIn(sys))
                    continue;
                if (!_seenFleets.Add(f.Id) || first || !f.IsOwnedBy(me) || !f.IsMoving(now))
                    continue;
                var transit = GameConfig.JumpgateTransitSeconds > 0f ? GameConfig.JumpgateTransitSeconds : 60f;
                // A gate jump stamps the target world at once with only the short transit left; a sub-light
                // arrival from another system carries no planet.
                if (f.PlanetId > 0 && f.DestTime - now <= transit + 5f && GateAt(f.PlanetId) is { } gate)
                    Open(gate);
            }
        }

        // ── Art (built once, shared) ──────────────────────────────────────────────

        static Mesh RingMesh => _ringMesh != null ? _ringMesh : _ringMesh = BuildRing();
        static Mesh LampMesh => _lampMesh != null ? _lampMesh : _lampMesh = BuildLamps();
        static Mesh DiscMesh => _discMesh != null ? _discMesh : _discMesh = GateRing.DiscMesh(12, 48);

        static Material LampReady => _lampReady != null ? _lampReady : _lampReady = Lamp("SU_JumpgateReady", JumpgateNetwork.JumpTint, 3.4f);
        static Material LampRecharge => _lampRecharge != null ? _lampRecharge : _lampRecharge = Lamp("SU_JumpgateRecharge", new Color(1f, 0.6f, 0.2f, 1f), 1.8f);

        static Material Lamp(string name, Color c, float mul)
        {
            var shader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = name, enableInstancing = true };
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Emission")) m.SetColor("_Emission", c);
            if (m.HasProperty("_EmissionMul")) m.SetFloat("_EmissionMul", mul);
            return m;
        }

        static Material FieldMaterial()
        {
            var shader = Shader.Find("SU/EventHorizon");
            Material m;
            if (shader != null)
            {
                m = new Material(shader) { name = "SU_FoldField" };
                m.SetColor("_Core", new Color(0.3f, 0.12f, 0.75f, 1f));
                m.SetColor("_Glow", new Color(0.8f, 0.62f, 1f, 1f));
                m.SetFloat("_SurgeDepth", 0.35f);
                m.SetFloat("_Intensity", 1.9f);
            }
            else
            {
                m = new Material(CombatFxKit.Glow()) { name = "SU_FoldFieldFallback" };
            }

            m.SetFloat(OpenId, 0f);
            return m;
        }

        /// <summary>Twelve-sided machined section, four emitter blocks along the axis, four radial spines.</summary>
        static Mesh BuildRing()
        {
            var b = new DefensePlatformKit.Builder();
            b.Lathe(new[]
            {
                new Vector2(0.82f, -0.07f), new Vector2(0.86f, -0.1f), new Vector2(0.97f, -0.1f), new Vector2(1f, -0.06f),
                new Vector2(1f, 0.06f), new Vector2(0.97f, 0.1f), new Vector2(0.86f, 0.1f), new Vector2(0.82f, 0.07f),
                new Vector2(0.82f, -0.07f)
            }, 12, true, Matrix4x4.identity);
            for (var i = 0; i < 4; i++)
            {
                var yaw = Quaternion.Euler(0f, 45f + i * 90f, 0f);
                // Fold emitters: long blocks through the ring, proud on both faces.
                b.Box(yaw * new Vector3(0f, 0f, 0.91f), yaw, new Vector3(0.1f, 0.34f, 0.16f));
                // Radial spines with a counterweight.
                b.Box(yaw * new Vector3(0f, 0f, 1.1f), yaw, new Vector3(0.05f, 0.05f, 0.22f));
                b.Box(yaw * new Vector3(0f, 0f, 1.22f), yaw, new Vector3(0.12f, 0.09f, 0.07f));
            }

            return b.ToMesh("SU_JumpgateRing");
        }

        /// <summary>Inner lip band and the emitter tips (both faces).</summary>
        static Mesh BuildLamps()
        {
            var b = new DefensePlatformKit.Builder();
            b.Lathe(new[] { new Vector2(0.815f, -0.035f), new Vector2(0.815f, 0.035f) }, 24, false, Matrix4x4.identity);
            for (var i = 0; i < 4; i++)
            {
                var yaw = Quaternion.Euler(0f, 45f + i * 90f, 0f);
                foreach (var side in new[] { -1f, 1f })
                    b.Box(yaw * new Vector3(0f, side * 0.175f, 0.88f), yaw, new Vector3(0.06f, 0.012f, 0.06f));
            }

            return b.ToMesh("SU_JumpgateLamps");
        }
    }
}
