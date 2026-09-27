using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>The deep sky of a system: a distant black hole, a pulsar's beams, comets on long orbits.</summary>
    public sealed partial class SystemAmbience
    {
        // ── Black hole ───────────────────────────────────────────────────────────

        Transform _hole;
        Transform[] _jets;
        ParticleSystem _infall;
        Vector3 _holeAxis;
        float _nextInfall;
        static Material _diskMat;
        static Material _horizonMat;

        void BuildBlackHole()
        {
            // Far out beyond the last worlds, above the ecliptic: a rogue hole the system skirts.
            var pos = Place(21, WorldScale.OrbitBase + WorldScale.OrbitStep * 8.5f, WorldScale.OrbitBase + WorldScale.OrbitStep * 10f, 90f);
            var go = new GameObject("BlackHole");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(12f + Roll(22) * 18f, Roll(23) * 360f, Roll(24) * 10f);
            _hole = go.transform;
            _holeAxis = _hole.up;

            // Event horizon: pure black, it eats the stars behind it.
            var horizon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            horizon.name = "Horizon";
            Destroy(horizon.GetComponent<Collider>());
            horizon.transform.SetParent(_hole, false);
            horizon.transform.localScale = Vector3.one * 34f;
            var hr = horizon.GetComponent<MeshRenderer>();
            hr.sharedMaterial = HorizonMat();
            Quiet(hr);

            // Two nested disks (bright inner, broad outer), the photon ring, a warm halo.
            Disk(_hole, 160f, 1f);
            Disk(_hole, 62f, 1.8f);
            Sprite(_hole, "PhotonRing", Vector3.zero, 70f, new Color(1f, 0.85f, 0.65f, 1f) * 0.6f);
            Sprite(_hole, "Halo", Vector3.zero, 330f, new Color(1f, 0.45f, 0.2f, 1f) * 0.18f);

            // Relativistic jets along the spin axis.
            _jets = new Transform[2];
            for (var i = 0; i < 2; i++)
            {
                var jet = Beam(_hole, "Jet" + i, 520f, 14f, new Color(0.55f, 0.75f, 1f, 1f) * 0.55f);
                jet.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 90f : -90f);
                _jets[i] = jet;
            }

            _infall = CombatFxKit.Burst(_hole, "Infall", 90, 6f, gravity: false, stretch: false);
        }

        void Disk(Transform parent, float radius, float intensity)
        {
            var d = new GameObject("Disk");
            d.transform.SetParent(parent, false);
            d.transform.localScale = Vector3.one * radius;
            d.AddComponent<MeshFilter>().sharedMesh = SystemBodyKit.RingMesh();
            var r = d.AddComponent<MeshRenderer>();
            r.sharedMaterial = DiskMat();
            Quiet(r);
            if (intensity != 1f)
            {
                var mpb = new MaterialPropertyBlock();
                mpb.SetFloat("_Intensity", 1.6f * intensity);
                mpb.SetFloat("_Speed", 0.5f);
                r.SetPropertyBlock(mpb);
            }
        }

        static Material DiskMat()
        {
            if (_diskMat != null)
                return _diskMat;
            var s = Shader.Find("SU/AccretionDisk");
            _diskMat = s != null ? new Material(s) { name = "SU_AccretionDisk" } : GlowMat(new Color(1f, 0.6f, 0.25f));
            return _diskMat;
        }

        static Material HorizonMat()
        {
            if (_horizonMat != null)
                return _horizonMat;
            _horizonMat = new Material(Shader.Find("SU/UnlitEmissive")) { name = "SU_EventHorizon" };
            _horizonMat.SetColor("_Color", Color.black);
            if (_horizonMat.HasProperty("_Emission"))
                _horizonMat.SetColor("_Emission", Color.black);
            return _horizonMat;
        }

        void TickBlackHole(float t)
        {
            if (_hole == null)
                return;
            // Jets breathe; matter spirals in.
            var k = 0.85f + 0.15f * Mathf.Sin(t * 1.3f);
            for (var i = 0; i < _jets.Length; i++)
                _jets[i].localScale = new Vector3(1f, k, k);
            if (t < _nextInfall)
                return;
            _nextInfall = t + 0.12f;
            var a = Random.value * Mathf.PI * 2f;
            var r = Random.Range(120f, 190f);
            var local = new Vector3(Mathf.Cos(a) * r, Random.Range(-4f, 4f), Mathf.Sin(a) * r);
            var world = _hole.TransformPoint(local);
            // Mostly round the hole, a little inward: it spirals.
            var tangent = _hole.TransformDirection(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            var inward = (_hole.position - world).normalized;
            CombatFxKit.Emit(_infall, world, new Color(1f, 0.6f, 0.3f, 0.8f), Random.Range(4f, 9f), 6f, tangent * 34f + inward * 22f);
        }

        // ── Pulsar ───────────────────────────────────────────────────────────────

        Transform _pulsar;

        void BuildPulsar()
        {
            var go = new GameObject("PulsarBeams");
            go.transform.SetParent(_root, false);
            go.transform.localPosition = Vector3.zero;
            _pulsar = go.transform;
            var tilt = new GameObject("Axis").transform;
            tilt.SetParent(_pulsar, false);
            tilt.localRotation = Quaternion.Euler(0f, 0f, 18f + Roll(31) * 22f);
            var c = Color.Lerp(_star, Color.white, 0.5f) * 0.45f;
            for (var i = 0; i < 2; i++)
            {
                var b = Beam(tilt, "Beam" + i, 1000f, 26f, c);
                b.localRotation = Quaternion.Euler(0f, i == 0 ? 0f : 180f, 0f);
            }
        }

        void TickPulsar(float dt)
        {
            // One slow sweep every ~6 s: the lighthouse crosses the ship now and then.
            if (_pulsar != null)
                _pulsar.Rotate(0f, dt * 60f, 0f, Space.Self);
        }

        // ── Comets ───────────────────────────────────────────────────────────────

        sealed class Comet
        {
            public Transform Head;
            public ParticleSystem Tail;
            public float A;
            public float E;
            public float Period;
            public float Phase;
            public Quaternion Plane;
            public float Carry;
        }

        readonly List<Comet> _comets = new();

        void BuildComets(int count)
        {
            for (var i = 0; i < count; i++)
            {
                var salt = 40 + i * 7;
                var c = new Comet
                {
                    A = Mathf.Lerp(360f, 620f, Roll(salt)),
                    E = Mathf.Lerp(0.55f, 0.8f, Roll(salt + 1)),
                    Period = Mathf.Lerp(220f, 420f, Roll(salt + 2)),
                    Phase = Roll(salt + 3),
                    Plane = Quaternion.Euler((Roll(salt + 4) - 0.5f) * 40f, Roll(salt + 5) * 360f, 0f)
                };
                var head = new GameObject("Comet" + i).transform;
                head.SetParent(_root, false);
                Sprite(head, "Coma", Vector3.zero, 9f, new Color(0.75f, 0.95f, 1f, 1f));
                Sprite(head, "Nucleus", Vector3.zero, 2.5f, Color.white);
                c.Head = head;
                c.Tail = CombatFxKit.Burst(_root, "CometTail" + i, 360, 6f, gravity: false, stretch: false);
                _comets.Add(c);
            }
        }

        void TickComets(float t)
        {
            if (_comets.Count == 0)
                return;
            var star = _root.position;
            foreach (var c in _comets)
            {
                // Kepler: mean anomaly → eccentric anomaly (a few Newton steps) → position; fast at perihelion.
                var m = (t / c.Period + c.Phase) * Mathf.PI * 2f;
                var e = m;
                for (var k = 0; k < 4; k++)
                    e -= (e - c.E * Mathf.Sin(e) - m) / (1f - c.E * Mathf.Cos(e));
                var b = c.A * Mathf.Sqrt(1f - c.E * c.E);
                var local = new Vector3(c.A * (Mathf.Cos(e) - c.E), 0f, b * Mathf.Sin(e));
                var pos = _root.TransformPoint(c.Plane * local + Vector3.up * WorldScale.EclipticHeight);
                c.Head.position = pos;

                // The tail streams away from the star, longer the closer it passes.
                var away = pos - star;
                var dist = Mathf.Max(1f, away.magnitude);
                away /= dist;
                var heat = Mathf.Clamp01(260f / dist);
                c.Carry += Time.deltaTime * Mathf.Lerp(10f, 55f, heat);
                while (c.Carry >= 1f)
                {
                    c.Carry -= 1f;
                    var ion = Random.value < 0.6f;
                    var v = away * Mathf.Lerp(14f, 34f, heat) * (ion ? 1.3f : 0.8f) + Random.insideUnitSphere * 2.5f;
                    var col = ion ? new Color(0.5f, 0.85f, 1f, 0.55f) : new Color(1f, 0.9f, 0.7f, 0.4f);
                    CombatFxKit.Emit(c.Tail, pos + Random.insideUnitSphere * 1.5f, col, Random.Range(2f, 4.5f) * (0.6f + heat), Random.Range(4f, 6f), v);
                }
            }
        }
    }
}
