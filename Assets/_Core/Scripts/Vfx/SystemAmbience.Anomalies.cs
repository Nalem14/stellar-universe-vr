using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The system's real anomalies (<see cref="AnomalyService"/>, GetSystemAnomalies) out in space, on the same
    /// bearing and orbit band as their token on the table: the wreck of an ancient frigate tumbling apart, a
    /// sealed precursor beacon, a resonant crystal asteroid, an unstable ion rift. Scanned (gone from the
    /// server) = gone from space.
    /// </summary>
    public sealed partial class SystemAmbience
    {
        sealed class Site
        {
            public Anomaly A;
            public Transform Root;
            public Transform Spin;
            public Transform[] Parts;
            public Vector3[] Drift;
            public Renderer Flicker;
            public ParticleSystem Fx;
            public float Next;
            public LineRenderer Arc;
            public float ArcUntil;
        }

        readonly List<Site> _sites = new();
        bool _anomaliesBound;

        void BindAnomalies()
        {
            if (AnomalyService.Instance == null)
                return;
            AnomalyService.Instance.Changed += OnAnomalies;
            _anomaliesBound = true;
            OnAnomalies(_system);
        }

        void UnbindAnomalies()
        {
            if (_anomaliesBound && AnomalyService.Instance != null)
                AnomalyService.Instance.Changed -= OnAnomalies;
        }

        void OnAnomalies(int systemId)
        {
            if (systemId != _system || this == null)
                return;
            var live = AnomalyService.Instance.For(_system);
            for (var i = _sites.Count - 1; i >= 0; i--)
            {
                var keep = false;
                foreach (var a in live)
                    keep |= a.Id == _sites[i].A.Id;
                if (keep)
                    continue;
                if (_sites[i].Root != null)
                    Destroy(_sites[i].Root.gameObject);
                _sites.RemoveAt(i);
            }

            foreach (var a in live)
            {
                var have = false;
                foreach (var s in _sites)
                    have |= s.A.Id == a.Id;
                if (!have)
                    _sites.Add(BuildSite(a));
            }
        }

        /// <summary>The table's bearing (HoloZoneMap.PlaceAnomaly): orbit band 2 + id % 4, half a step out.</summary>
        public static Vector3 AnomalyPosition(Anomaly a)
        {
            var slot = 2 + a.Id % 4;
            var r = WorldScale.OrbitRadius(slot) + WorldScale.OrbitStep * 0.5f;
            unchecked
            {
                var x = (uint)(a.Id * 71 + 3) * 2654435761u;
                var ang = (x % 6283) / 1000f;
                return new Vector3(Mathf.Cos(ang) * r, WorldScale.EclipticHeight + 6f, Mathf.Sin(ang) * r);
            }
        }

        Site BuildSite(Anomaly a)
        {
            var root = new GameObject("Anomaly_" + a.Id).transform;
            root.SetParent(_root, false);
            root.localPosition = AnomalyPosition(a);
            var tint = HoloZoneMap.AnomalyTint(a.Type);
            var site = new Site { A = a, Root = root };
            Sprite(root, "Beacon", Vector3.zero, 26f, tint * 0.35f);
            switch (a.Type)
            {
                case "derelict_ship":
                    Wreck(site);
                    break;
                case "precursor_cache":
                    Beacon(site, tint);
                    break;
                case "crystal_monolith":
                    Crystals(site, tint);
                    break;
                default:
                    Rift(site, tint);
                    break;
            }

            return site;
        }

        void Wreck(Site s)
        {
            // An ancient frigate broken in three, the pieces drifting apart, a light still flickering.
            var hull = UI.UiKit.Chassis;
            (Vector3 pos, Vector3 size, Vector3 rot)[] chunks =
            {
                (new Vector3(0f, 0f, 6f), new Vector3(5f, 3.2f, 13f), new Vector3(8f, 0f, 12f)),
                (new Vector3(1.5f, -1f, -6f), new Vector3(5.5f, 3f, 9f), new Vector3(-14f, 20f, -6f)),
                (new Vector3(-4f, 2f, -1f), new Vector3(2.2f, 1.2f, 5f), new Vector3(30f, -40f, 50f))
            };
            s.Parts = new Transform[chunks.Length];
            s.Drift = new Vector3[chunks.Length];
            for (var i = 0; i < chunks.Length; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Chunk" + i;
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(s.Root, false);
                c.transform.localPosition = chunks[i].pos;
                c.transform.localScale = chunks[i].size;
                c.transform.localRotation = Quaternion.Euler(chunks[i].rot);
                var r = c.GetComponent<MeshRenderer>();
                r.sharedMaterial = hull;
                Quiet(r);
                s.Parts[i] = c.transform;
                s.Drift[i] = new Vector3(Hash01(a(i, 1)) - 0.5f, Hash01(a(i, 2)) - 0.5f, Hash01(a(i, 3)) - 0.5f) * 6f;
            }

            s.Flicker = Sprite(s.Root, "Lamp", new Vector3(1.2f, 1.8f, 8f), 3f, new Color(1f, 0.55f, 0.25f)).GetComponent<Renderer>();
            s.Fx = CombatFxKit.Burst(s.Root, "Sparks", 40, 1.2f, gravity: false, stretch: false);
            return;

            int a(int i, int k) => s.A.Id * 97 + i * 13 + k;
        }

        void Beacon(Site s, Color tint)
        {
            // A dark octahedron with rings of light turning round it; it pulses every few seconds.
            var core = new GameObject("Monolith");
            core.transform.SetParent(s.Root, false);
            core.transform.localScale = new Vector3(6f, 10f, 6f);
            core.AddComponent<MeshFilter>().sharedMesh = Octahedron();
            var r = core.AddComponent<MeshRenderer>();
            r.sharedMaterial = UI.UiKit.Bezel;
            Quiet(r);
            s.Spin = new GameObject("Rings").transform;
            s.Spin.SetParent(s.Root, false);
            for (var i = 0; i < 2; i++)
            {
                var ring = new GameObject("Ring" + i);
                ring.transform.SetParent(s.Spin, false);
                ring.transform.localRotation = Quaternion.Euler(i == 0 ? 70f : -35f, i * 60f, 0f);
                ring.transform.localScale = Vector3.one * (i == 0 ? 13f : 17f);
                ring.AddComponent<MeshFilter>().sharedMesh = SystemBodyKit.RingMesh();
                var rr = ring.AddComponent<MeshRenderer>();
                rr.sharedMaterial = GlowMat(tint * 0.5f);
                Quiet(rr);
            }

            s.Fx = CombatFxKit.Burst(s.Root, "Pulse", 20, 2.2f, gravity: false, stretch: false);
        }

        void Crystals(Site s, Color tint)
        {
            // A cluster of hexagonal prisms growing out of a common root, shards orbiting it.
            var shader = Shader.Find("SU/HoloCrystal");
            var mat = shader != null ? new Material(shader) { name = "SU_AnomalyCrystal" } : GlowMat(tint);
            if (shader != null)
            {
                mat.SetColor("_Color", tint);
                mat.SetColor("_Emission", tint);
                mat.SetFloat("_EmissionMul", 1.2f);
            }

            for (var i = 0; i < 7; i++)
            {
                var p = new GameObject("Prism" + i);
                p.transform.SetParent(s.Root, false);
                var h = 6f + Hash01(s.A.Id * 17 + i) * 9f;
                p.transform.localRotation = Quaternion.Euler((Hash01(s.A.Id + i * 3) - 0.5f) * 80f, i * 51f, (Hash01(s.A.Id + i * 5) - 0.5f) * 80f);
                p.transform.localScale = new Vector3(1.6f + h * 0.08f, h, 1.6f + h * 0.08f);
                p.AddComponent<MeshFilter>().sharedMesh = Prism();
                var r = p.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                Quiet(r);
            }

            s.Spin = new GameObject("Shards").transform;
            s.Spin.SetParent(s.Root, false);
            for (var i = 0; i < 6; i++)
            {
                var sh = new GameObject("Shard" + i);
                sh.transform.SetParent(s.Spin, false);
                var ang = i / 6f * Mathf.PI * 2f;
                sh.transform.localPosition = new Vector3(Mathf.Cos(ang) * 16f, (i % 2 - 0.5f) * 5f, Mathf.Sin(ang) * 16f);
                sh.transform.localScale = new Vector3(0.9f, 2.4f, 0.9f);
                sh.transform.localRotation = Quaternion.Euler(i * 40f, i * 70f, 0f);
                sh.AddComponent<MeshFilter>().sharedMesh = Prism();
                var r = sh.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                Quiet(r);
            }
        }

        void Rift(Site s, Color tint)
        {
            // A tear standing in space: a tall lens of light, arcs cracking round it, matter drawn in.
            s.Spin = new GameObject("Lens").transform;
            s.Spin.SetParent(s.Root, false);
            for (var i = 0; i < 3; i++)
            {
                var q = new GameObject("Sheet" + i);
                q.transform.SetParent(s.Spin, false);
                q.transform.localRotation = Quaternion.Euler(0f, i * 60f, 0f);
                q.transform.localScale = new Vector3(9f, 34f, 1f);
                q.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
                var r = q.AddComponent<MeshRenderer>();
                r.sharedMaterial = GlowMat(Color.Lerp(tint, Color.white, i == 0 ? 0.5f : 0f) * (i == 0 ? 0.8f : 0.45f));
                Quiet(r);
            }

            s.Fx = CombatFxKit.Burst(s.Root, "Draw", 80, 2.5f, gravity: false, stretch: false);
            var go = new GameObject("Arc");
            go.transform.SetParent(s.Root, false);
            s.Arc = go.AddComponent<LineRenderer>();
            s.Arc.useWorldSpace = true;
            s.Arc.positionCount = 10;
            s.Arc.widthMultiplier = 0.9f;
            s.Arc.sharedMaterial = BeamMat(Color.Lerp(tint, Color.white, 0.5f));
            s.Arc.enabled = false;
            Quiet(s.Arc);
        }

        readonly Vector3[] _arcPts = new Vector3[10];

        void TickAnomalies(float t, float dt)
        {
            foreach (var s in _sites)
            {
                if (s.Root == null)
                    continue;
                s.Root.Rotate(0f, dt * 2f, 0f, Space.Self);
                if (s.Spin != null)
                    s.Spin.Rotate(dt * 7f, dt * 13f, 0f, Space.Self);
                if (s.Parts != null)
                    for (var i = 0; i < s.Parts.Length; i++)
                        s.Parts[i].Rotate(s.Drift[i] * dt, Space.Self);
                if (s.Flicker != null)
                    s.Flicker.enabled = Mathf.PerlinNoise(t * 3f, s.A.Id) > 0.45f;
                if (s.Arc != null && s.Arc.enabled && t >= s.ArcUntil)
                    s.Arc.enabled = false;
                if (t < s.Next)
                    continue;

                var tint = HoloZoneMap.AnomalyTint(s.A.Type);
                var at = s.Root.position;
                switch (s.A.Type)
                {
                    case "derelict_ship":
                        s.Next = t + Random.Range(1.5f, 5f);
                        var p = s.Parts[Random.Range(0, s.Parts.Length)].position + Random.insideUnitSphere * 3f;
                        for (var k = 0; k < 6; k++)
                            CombatFxKit.Emit(s.Fx, p, new Color(1f, 0.7f, 0.35f), 0.8f, 1f, Random.insideUnitSphere * 9f);
                        break;
                    case "precursor_cache":
                        s.Next = t + 3.2f;
                        CombatFxKit.Emit(s.Fx, at, tint, 30f, 1.6f);
                        CombatFxKit.Emit(s.Fx, at, Color.white * 0.6f, 12f, 0.8f);
                        break;
                    case "crystal_monolith":
                        s.Next = t + 99f;
                        break;
                    default:
                        s.Next = t + 0.08f;
                        var dir = Random.onUnitSphere;
                        CombatFxKit.Emit(s.Fx, at + dir * 28f, tint * 0.8f, Random.Range(1.2f, 2.5f), 2.4f, -dir * 12f);
                        if (s.Arc != null && Random.value < 0.06f)
                        {
                            var a = at + Random.onUnitSphere * 5f + Vector3.up * Random.Range(-14f, 14f);
                            var b = at + Random.onUnitSphere * 18f;
                            for (var k = 0; k < _arcPts.Length; k++)
                                _arcPts[k] = Vector3.Lerp(a, b, k / 9f) + (k == 0 || k == 9 ? Vector3.zero : Random.insideUnitSphere * 2.5f);
                            s.Arc.SetPositions(_arcPts);
                            s.Arc.enabled = true;
                            s.ArcUntil = t + 0.1f;
                        }

                        break;
                }
            }
        }

        static Mesh _octa;
        static Mesh _prism;

        static Mesh Octahedron()
        {
            if (_octa != null)
                return _octa;
            var top = new Vector3(0f, 1f, 0f);
            var bot = new Vector3(0f, -1f, 0f);
            Vector3[] eq = { new(1f, 0f, 0f), new(0f, 0f, 1f), new(-1f, 0f, 0f), new(0f, 0f, -1f) };
            var v = new List<Vector3>();
            var t = new List<int>();
            for (var i = 0; i < 4; i++)
            {
                var a = eq[i];
                var b = eq[(i + 1) % 4];
                // Flat-shaded faces: own vertices per face.
                foreach (var f in new[] { (top, b, a), (bot, a, b) })
                {
                    t.Add(v.Count);
                    v.Add(f.Item1);
                    t.Add(v.Count);
                    v.Add(f.Item2);
                    t.Add(v.Count);
                    v.Add(f.Item3);
                }
            }

            Outward(v, t, Vector3.zero);
            _octa = new Mesh { name = "SU_Octahedron", vertices = v.ToArray(), triangles = t.ToArray() };
            _octa.RecalculateNormals();
            _octa.RecalculateBounds();
            return _octa;
        }

        /// <summary>A hexagonal prism with pointed ends (0..1 tall on +y), flat-shaded.</summary>
        static Mesh Prism()
        {
            if (_prism != null)
                return _prism;
            var v = new List<Vector3>();
            var t = new List<int>();
            var tip = new Vector3(0f, 1f, 0f);
            var root = Vector3.zero;
            for (var i = 0; i < 6; i++)
            {
                var a0 = i / 6f * Mathf.PI * 2f;
                var a1 = (i + 1) / 6f * Mathf.PI * 2f;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0.15f, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0.15f, Mathf.Sin(a1) * 0.5f);
                var q0 = p0 + Vector3.up * 0.65f;
                var q1 = p1 + Vector3.up * 0.65f;
                void Tri(Vector3 a, Vector3 b, Vector3 c)
                {
                    t.Add(v.Count);
                    v.Add(a);
                    t.Add(v.Count);
                    v.Add(b);
                    t.Add(v.Count);
                    v.Add(c);
                }

                Tri(p0, q1, p1);
                Tri(p0, q0, q1);
                Tri(q0, tip, q1);
                Tri(p0, p1, root);
            }

            Outward(v, t, new Vector3(0f, 0.5f, 0f));
            _prism = new Mesh { name = "SU_CrystalPrism", vertices = v.ToArray(), triangles = t.ToArray() };
            _prism.RecalculateNormals();
            _prism.RecalculateBounds();
            return _prism;
        }
    
        /// <summary>Wind every triangle of a convex shape to face away from <paramref name="centre"/> (Unity: clockwise = front).</summary>
        static void Outward(List<Vector3> v, List<int> t, Vector3 centre)
        {
            for (var i = 0; i < t.Count; i += 3)
            {
                var a = v[t[i]];
                var b = v[t[i + 1]];
                var c = v[t[i + 2]];
                var n = Vector3.Cross(b - a, c - a);
                if (Vector3.Dot(n, (a + b + c) / 3f - centre) < 0f)
                    (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
        }
    }
}
