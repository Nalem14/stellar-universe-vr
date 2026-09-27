using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// What makes a system feel inhabited and alive through the windows, on top of its star, worlds and
    /// ships. Two sources, so it is always the same system when you come back:
    /// <list type="bullet">
    /// <item><b>Real</b>: the server's anomalies drawn in space where the table shows them (derelict wreck,
    /// precursor beacon, crystal monolith, ion rift), civilian traffic between the worlds that are settled.</item>
    /// <item><b>Seeded by the system id</b> (persistent, VR-only, not game data): nebula storms with
    /// lightning, thunderstorms on some worlds (seeded by the planet), a distant black hole, a pulsar's sweeping beams (hot stars), comets on long orbits, pods of
    /// void fauna, and the drifting dust that shows the ship moving.</item>
    /// </list>
    /// Built once per system under the exterior content (hidden with it between systems); one Update drives
    /// everything; shared meshes and materials, pooled particles — a few dozen draw calls at most.
    /// </summary>
    public sealed partial class SystemAmbience : MonoBehaviour
    {
        SystemExterior _ext;
        FocusContext _focus;
        int _system;
        Transform _root;
        Color _star = Color.white;
        string _starKey = string.Empty;
        Transform _ship;

        public static SystemAmbience Build(Transform content, SystemExterior ext, FocusContext focus, Color starColor)
        {
            var go = new GameObject("SystemAmbience");
            go.transform.SetParent(content, false);
            var a = go.AddComponent<SystemAmbience>();
            a._ext = ext;
            a._focus = focus;
            a._system = focus != null ? focus.SystemId : 0;
            a._root = go.transform;
            a._star = starColor;
            a._starKey = focus != null ? (focus.SystemTypeKey ?? string.Empty).ToLowerInvariant() : string.Empty;
            a.Compose();
            return a;
        }

        void Compose()
        {
            var rig = FindFirstObjectByType<BridgeViewRig>();
            _ship = rig != null ? rig.ViewShip : null;
            BuildDust();
            if (_system <= 0)
                return;
            if (Roll(1) < 0.45f)
                BuildStorm();
            if (Roll(2) < 0.12f)
                BuildBlackHole();
            var hot = _starKey is "blue" or "b" or "white" or "w";
            if (Roll(3) < (hot ? 0.5f : 0.08f))
                BuildPulsar();
            if (Roll(4) < 0.45f)
                BuildComets(Roll(5) < 0.4f ? 2 : 1);
            if (Roll(6) < 0.32f)
                BuildFauna(3 + (int)(Roll(7) * 3f));
            BuildTraffic();
            BuildWeather();
            BindAnomalies();
        }

        void OnDestroy() => UnbindAnomalies();

        void Update()
        {
            var dt = Time.deltaTime;
            var t = Time.time;
            TickDust();
            TickStorm(t);
            TickBlackHole(t);
            TickPulsar(dt);
            TickComets(t);
            TickFauna(t, dt);
            TickTraffic(dt);
            TickWeather(t);
            TickAnomalies(t, dt);
        }

        // ── Seed ─────────────────────────────────────────────────────────────────

        /// <summary>Stable 0..1 for this system and <paramref name="salt"/> (same every visit, every headset).</summary>
        float Roll(int salt) => Hash01(_system * 7919 + salt * 104729);

        static float Hash01(int seed)
        {
            unchecked
            {
                var x = (uint)seed;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>A bearing on the ecliptic and a distance, both seeded.</summary>
        Vector3 Place(int salt, float minR, float maxR, float lift)
        {
            var a = Roll(salt) * Mathf.PI * 2f;
            var r = Mathf.Lerp(minR, maxR, Roll(salt + 1));
            return new Vector3(Mathf.Cos(a) * r, WorldScale.EclipticHeight + (Roll(salt + 2) - 0.5f) * 2f * lift, Mathf.Sin(a) * r);
        }

        // ── Shared art ───────────────────────────────────────────────────────────

        static readonly Dictionary<int, Material> GlowMats = new();
        static readonly Dictionary<int, Material> BeamMats = new();

        /// <summary>Additive soft glow in a colour (cached per colour).</summary>
        static Material GlowMat(Color c) => Tinted(GlowMats, c, CombatFxKit.DotTexture(), 2.2f);

        /// <summary>Additive beam (soft across its width, v) in a colour.</summary>
        static Material BeamMat(Color c) => Tinted(BeamMats, c, CombatFxKit.BeamTexture(), 2f);

        static Material Tinted(Dictionary<int, Material> cache, Color c, Texture tex, float mul)
        {
            var key = ((Color32)c).GetHashCode();
            if (cache.TryGetValue(key, out var m) && m != null)
                return m;
            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("SU/UnlitEmissive");
            m = new Material(shader) { name = "SU_AmbienceGlow", mainTexture = tex };
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", c);
            if (m.HasProperty("_EmissionMul"))
                m.SetFloat("_EmissionMul", mul);
            cache[key] = m;
            return m;
        }

        /// <summary>A camera-facing glow sprite.</summary>
        Transform Sprite(Transform parent, string name, Vector3 local, float size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = GlowMat(color);
            Quiet(r);
            go.AddComponent<BillboardFace>();
            return go.transform;
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        static Mesh _quad;

        static Mesh QuadMesh()
        {
            if (_quad != null)
                return _quad;
            _quad = new Mesh
            {
                name = "SU_AmbienceQuad",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
                colors = new[] { Color.white, Color.white, Color.white, Color.white }
            };
            _quad.RecalculateNormals();
            _quad.RecalculateBounds();
            return _quad;
        }

        // ── Dust: shows the ship moving ─────────────────────────────────────────

        ParticleSystem _dust;

        void BuildDust()
        {
            var go = new GameObject("SpaceDust");
            go.transform.SetParent(_root, false);
            _dust = go.AddComponent<ParticleSystem>();
            var main = _dust.main;
            main.startLifetime = 14f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
            main.startColor = Color.Lerp(_star, new Color(0.7f, 0.85f, 1f), 0.5f) * new Color(1f, 1f, 1f, 0.3f);
            main.maxParticles = 220;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = _dust.emission;
            em.rateOverTime = 16f;
            // A shell round the ship: grains are born well outside the hull (never in the rooms).
            var shape = _dust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 95f;
            shape.radiusThickness = 0.8f;
            shape.scale = new Vector3(1f, 0.4f, 1f);
            var col = _dust.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = CombatFxKit.Glow();
            Quiet(r);
            // Pre-fill so the first frame is not empty space.
            _dust.Simulate(14f, true, true);
            _dust.Play();
        }

        ParticleSystem.Particle[] _grains;
        float _nextSweep;

        void TickDust()
        {
            // The emitter rides along with the inhabited ship; the grains stay where they were born.
            if (_dust == null || _ship == null)
                return;
            _dust.transform.position = _ship.position;
            // As the ship flies through them, grains that would cross the hull (and the bridge in it) go out.
            if (Time.time < _nextSweep)
                return;
            _nextSweep = Time.time + 0.15f;
            _grains ??= new ParticleSystem.Particle[_dust.main.maxParticles];
            var n = _dust.GetParticles(_grains);
            var c = _ship.position;
            var hit = false;
            for (var i = 0; i < n; i++)
            {
                if ((_grains[i].position - c).sqrMagnitude > HullClear * HullClear)
                    continue;
                _grains[i].remainingLifetime = 0f;
                hit = true;
            }

            if (hit)
                _dust.SetParticles(_grains, n);
        }

        /// <summary>No grain nearer the ship's centre than this (hull half-span plus margin).</summary>
        const float HullClear = WorldScale.ShipSpan * 0.9f;

        // ── Nebula storm: lightning in a cloud ──────────────────────────────────

        readonly List<Transform> _clouds = new();
        LineRenderer _bolt;
        ParticleSystem _flashes;
        float _nextStrike;
        float _boltUntil;
        readonly Vector3[] _boltPts = new Vector3[14];
        Color _stormTint;

        void BuildStorm()
        {
            foreach (Transform c in _root.parent)
                if (c.name.StartsWith("Nebula_", System.StringComparison.Ordinal))
                    _clouds.Add(c);
            if (_clouds.Count == 0)
                return;
            _stormTint = Roll(11) < 0.5f ? new Color(0.65f, 0.8f, 1f) : new Color(1f, 0.6f, 0.95f);
            _flashes = CombatFxKit.Burst(_root, "StormFlash", 24, 0.5f, gravity: false, stretch: false);
            var go = new GameObject("StormBolt");
            go.transform.SetParent(_root, false);
            _bolt = go.AddComponent<LineRenderer>();
            _bolt.useWorldSpace = true;
            _bolt.positionCount = _boltPts.Length;
            _bolt.widthMultiplier = 2.4f;
            _bolt.widthCurve = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.2f));
            _bolt.sharedMaterial = BeamMat(Color.Lerp(_stormTint, Color.white, 0.5f));
            _bolt.textureMode = LineTextureMode.Stretch;
            _bolt.enabled = false;
            Quiet(_bolt);
            _nextStrike = Time.time + 1.5f;
        }

        void TickStorm(float t)
        {
            if (_bolt == null)
                return;
            if (_bolt.enabled && t >= _boltUntil)
                _bolt.enabled = false;
            if (t < _nextStrike)
                return;
            // Strikes come in bursts: a doublet now and then, a long hush between.
            _nextStrike = t + (Random.value < 0.3f ? Random.Range(0.12f, 0.3f) : Random.Range(2.5f, 8f));
            var cloud = _clouds[Random.Range(0, _clouds.Count)];
            if (cloud == null)
                return;
            var s = cloud.lossyScale;
            Vector3 P(float x, float y) => cloud.TransformPoint(new Vector3(x, y, 0f));
            var a = P(Random.Range(-0.35f, 0.35f), Random.Range(-0.3f, 0.3f));
            var b = P(Random.Range(-0.35f, 0.35f), Random.Range(-0.3f, 0.3f));
            var span = (b - a).magnitude;
            var side = Vector3.Cross((b - a).normalized, cloud.forward).normalized;
            for (var i = 0; i < _boltPts.Length; i++)
            {
                var u = i / (_boltPts.Length - 1f);
                var jag = i == 0 || i == _boltPts.Length - 1 ? 0f : Random.Range(-1f, 1f) * span * 0.08f;
                _boltPts[i] = Vector3.Lerp(a, b, u) + side * jag + cloud.forward * -2f;
            }

            _bolt.SetPositions(_boltPts);
            _bolt.enabled = true;
            _boltUntil = t + Random.Range(0.07f, 0.16f);
            var mid = (a + b) * 0.5f;
            CombatFxKit.Emit(_flashes, mid, _stormTint * 0.9f, Mathf.Max(s.x, s.y) * 0.45f, 0.35f);
            CombatFxKit.Emit(_flashes, mid, Color.white * 0.7f, span * 0.35f, 0.18f);
        }
    }
}
