using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Life in the system: pods of void fauna (seeded — the same pod in the same system every time) and
    /// civilian traffic plying between the worlds that are settled (real: planets with an owner), plus a
    /// few haulers crossing the system edge to edge.
    /// </summary>
    public sealed partial class SystemAmbience
    {
        // ── Void fauna ───────────────────────────────────────────────────────────

        sealed class Creature
        {
            public Transform T;
            public float Lag;
            public Vector3 Side;
            public float Size;
        }

        readonly List<Creature> _pod = new();
        Vector3 _podCentre;
        Vector3 _podAmp;
        Vector3 _podFreq;
        float _podPhase;
        static Mesh _manta;
        static readonly Dictionary<int, Material> MantaMats = new();

        static readonly Color[] FaunaGlows =
        {
            new(0.3f, 0.95f, 1f), new(0.7f, 0.45f, 1f), new(0.4f, 1f, 0.6f), new(1f, 0.7f, 0.35f)
        };

        void BuildFauna(int count)
        {
            _podCentre = Place(51, WorldScale.OrbitBase + WorldScale.OrbitStep * 1.5f, WorldScale.OrbitBase + WorldScale.OrbitStep * 4.5f, 25f);
            _podAmp = new Vector3(170f, 22f, 150f);
            _podFreq = new Vector3(0.021f, 0.05f, 0.033f) * Mathf.Lerp(0.8f, 1.2f, Roll(52));
            _podPhase = Roll(53) * 100f;
            var glow = FaunaGlows[(int)(Roll(54) * FaunaGlows.Length) % FaunaGlows.Length];
            var mat = MantaMat(glow);
            for (var i = 0; i < count; i++)
            {
                var size = i == 0 ? 15f : Mathf.Lerp(8f, 12f, Hash01(_system * 31 + i));
                var go = new GameObject("VoidFauna" + i);
                go.transform.SetParent(_root, false);
                var body = new GameObject("Body");
                body.transform.SetParent(go.transform, false);
                body.transform.localScale = new Vector3(size, size, size * 1.25f);
                body.AddComponent<MeshFilter>().sharedMesh = MantaMesh();
                var r = body.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                Quiet(r);
                // A faint wake from the tail.
                var tail = new GameObject("Wake");
                tail.transform.SetParent(go.transform, false);
                tail.transform.localPosition = new Vector3(0f, 0f, -size * 1.2f);
                var trail = tail.AddComponent<TrailRenderer>();
                trail.time = 6f;
                trail.minVertexDistance = 3f;
                trail.widthMultiplier = size * 0.18f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
                trail.sharedMaterial = BeamMat(glow * 0.35f);
                Quiet(trail);
                _pod.Add(new Creature
                {
                    T = go.transform,
                    Lag = i * 2.6f,
                    Side = new Vector3((i % 2 == 0 ? 1f : -1f) * (i + 1) / 2 * 14f, (Hash01(_system + i) - 0.5f) * 10f, 0f),
                    Size = size
                });
            }
        }

        Vector3 PodPath(float t) =>
            _podCentre + new Vector3(_podAmp.x * Mathf.Sin(_podFreq.x * t + 1.3f), _podAmp.y * Mathf.Sin(_podFreq.y * t),
                _podAmp.z * Mathf.Sin(_podFreq.z * t + 0.4f));

        void TickFauna(float t, float dt)
        {
            if (_pod.Count == 0)
                return;
            foreach (var c in _pod)
            {
                var tt = t + _podPhase - c.Lag;
                var p = PodPath(tt);
                var ahead = PodPath(tt + 0.5f) - p;
                if (ahead.sqrMagnitude < 1e-4f)
                    continue;
                var rot = Quaternion.LookRotation(ahead.normalized, Vector3.up);
                // Formation off the leader's line, banking into the turns.
                var turn = Vector3.SignedAngle(ahead, PodPath(tt + 1.5f) - PodPath(tt + 1f), Vector3.up);
                var bank = Quaternion.AngleAxis(Mathf.Clamp(-turn * 6f, -25f, 25f), Vector3.forward);
                c.T.localPosition = p + rot * c.Side;
                c.T.localRotation = Quaternion.Slerp(c.T.localRotation, rot * bank, dt * 1.5f);
            }
        }

        static Material MantaMat(Color glow)
        {
            var key = ((Color32)glow).GetHashCode();
            if (MantaMats.TryGetValue(key, out var m) && m != null)
                return m;
            var s = Shader.Find("SU/VoidManta");
            m = s != null ? new Material(s) : new Material(Shader.Find("SU/UnlitEmissive"));
            m.name = "SU_VoidFauna";
            if (m.HasProperty("_Glow"))
                m.SetColor("_Glow", glow);
            else if (m.HasProperty("_Emission"))
                m.SetColor("_Emission", glow * 0.4f);
            MantaMats[key] = m;
            return m;
        }

        /// <summary>A manta-like body: x = span −1..1, z = nose +1 … tail −1, a low hump along the spine.</summary>
        static Mesh MantaMesh()
        {
            if (_manta != null)
                return _manta;
            const int nu = 17, nv = 15;
            var v = new Vector3[nu * nv];
            for (var j = 0; j < nv; j++)
            {
                var z = 1f - 2f * j / (nv - 1f);
                var w = Width(z);
                for (var i = 0; i < nu; i++)
                {
                    var u = -1f + 2f * i / (nu - 1f);
                    var x = u * w;
                    var hump = 0.12f * (1f - u * u) * Mathf.Clamp01(w * 2f) * (z > -0.5f ? 1f : 0.3f);
                    // Wing tips sweep back a little.
                    v[j * nu + i] = new Vector3(x, hump, z - 0.25f * Mathf.Abs(u) * w);
                }
            }

            var tris = new List<int>();
            for (var j = 0; j < nv - 1; j++)
            for (var i = 0; i < nu - 1; i++)
            {
                var a = j * nu + i;
                tris.Add(a);
                tris.Add(a + 1);
                tris.Add(a + nu);
                tris.Add(a + 1);
                tris.Add(a + nu + 1);
                tris.Add(a + nu);
            }

            _manta = new Mesh { name = "SU_VoidManta", vertices = v, triangles = tris.ToArray() };
            _manta.RecalculateNormals();
            // The wings beat in the vertex shader: generous bounds.
            _manta.bounds = new Bounds(Vector3.zero, new Vector3(2.4f, 1.4f, 2.6f));
            return _manta;

            static float Width(float z)
            {
                if (z > 0.1f)
                    return Mathf.Lerp(1f, 0.16f, Mathf.Pow((z - 0.1f) / 0.9f, 0.8f));
                if (z > -0.45f)
                    return Mathf.Lerp(1f, 0.12f, Mathf.Pow((0.1f - z) / 0.55f, 0.7f));
                return Mathf.Lerp(0.12f, 0.02f, (-0.45f - z) / 0.55f);
            }
        }

        // ── Civilian traffic ────────────────────────────────────────────────────

        sealed class Hauler
        {
            public Transform T;
            public Renderer[] Parts;
            public Transform[] Engines;
            public Renderer Blink;
            public int To = -1;
            public SpaceRoute Route;
            public float Len;
            public float U;
            public float Speed;
            public float Dwell;
        }

        sealed class Port
        {
            public Transform Body;
            public float Radius;
            public bool Edge;
            public Vector3 EdgePos;
        }

        readonly List<Hauler> _haulers = new();
        readonly List<Port> _ports = new();

        void BuildTraffic()
        {
            var settled = 0;
            if (_focus != null)
                foreach (var p in _focus.Planets)
                {
                    if (p.UserId <= 0 || _ext == null || !_ext.TryGetPlanet(p.Id, out var body))
                        continue;
                    _ports.Add(new Port { Body = body, Radius = WorldScale.PlanetRadius(Mathf.Max(1, p.Slot)) });
                    settled++;
                }

            // Two gates on the system's rim for through-traffic (haulers jump in and out there).
            for (var i = 0; i < 2; i++)
                _ports.Add(new Port { Edge = true, EdgePos = Place(61 + i * 5, WorldScale.OrbitBase + WorldScale.OrbitStep * 7f, WorldScale.OrbitBase + WorldScale.OrbitStep * 8f, 30f) });

            var count = Mathf.Clamp(2 + settled * 2, 2, 7);
            for (var i = 0; i < count; i++)
            {
                var h = NewHauler(i);
                h.To = Random.Range(0, _ports.Count);
                h.T.localPosition = PortPoint(_ports[h.To], i);
                h.Dwell = Random.Range(0f, 10f);
                _haulers.Add(h);
            }
        }

        Hauler NewHauler(int i)
        {
            var go = new GameObject("Hauler" + i);
            go.transform.SetParent(_root, false);
            var body = new GameObject("Hull");
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = Vector3.one * Random.Range(0.7f, 1f);
            // A real 9×9 hull silhouette (civil layouts: drive, holds, core, sensor bow), one mesh, one draw.
            body.AddComponent<MeshFilter>().sharedMesh = HaulerMesh(i % CivilLayouts.Length);
            var r = body.AddComponent<MeshRenderer>();
            r.sharedMaterial = ShipHullBuilder.CivilHull();
            Quiet(r);
            var civil = Random.value < 0.5f ? new Color(1f, 0.72f, 0.35f) : new Color(0.55f, 0.85f, 1f);
            var e0 = Sprite(go.transform, "Engine", new Vector3(-0.8f, 0f, -6f), 2.6f, civil);
            var e1 = Sprite(go.transform, "Engine", new Vector3(0.8f, 0f, -6f), 2.6f, civil);
            var nav = Sprite(go.transform, "Nav", new Vector3(0f, 1.4f, 0.5f), 1.1f, new Color(1f, 0.25f, 0.2f));
            return new Hauler
            {
                T = go.transform,
                Parts = go.GetComponentsInChildren<Renderer>(),
                Engines = new[] { e0, e1 },
                Blink = nav.GetComponent<Renderer>(),
                Speed = Random.Range(22f, 38f)
            };
        }

        Vector3 PortPoint(Port p, int salt)
        {
            if (p.Edge || p.Body == null)
                return p.EdgePos;
            // Just off the world, on a bearing of its own (orbital docks round the planet).
            var a = Hash01(salt * 131 + p.Body.GetInstanceID()) * Mathf.PI * 2f;
            var local = p.Body.localPosition;
            return local + new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) * (p.Radius * 1.35f + 6f);
        }

        void TickTraffic(float dt)
        {
            if (_haulers.Count == 0 || _ports.Count < 2)
                return;
            var blink = Mathf.Repeat(Time.time, 1.4f) < 0.12f;
            for (var i = 0; i < _haulers.Count; i++)
            {
                var h = _haulers[i];
                if (h.Blink != null && h.Blink.enabled != blink && h.Route != null)
                    h.Blink.enabled = blink;
                if (h.Route == null)
                {
                    h.Dwell -= dt;
                    if (h.Dwell > 0f)
                        continue;
                    var from = h.To;
                    var to = Random.Range(0, _ports.Count - 1);
                    if (to >= from)
                        to++;
                    // Rim to rim is a pass-through; never rim to the same rim.
                    var a = h.T.localPosition;
                    var b = PortPoint(_ports[to], i + Random.Range(0, 5));
                    h.Route = _ext != null
                        ? _ext.Route(_root.TransformPoint(a), _root.TransformPoint(b))
                        : SpaceRoute.Line(_root.TransformPoint(a), _root.TransformPoint(b));
                    h.Len = Vector3.Distance(a, b);
                    h.U = 0f;
                    h.To = to;
                    Show(h, true);
                    continue;
                }

                // Ease out of the dock, cruise, ease into the next.
                var along = h.U * h.Len;
                var ease = Mathf.Clamp(Mathf.Min(along, h.Len - along) / 60f, 0.12f, 1f);
                h.U += h.Speed * ease * dt / Mathf.Max(1f, h.Len);
                var pos = h.Route.At(h.U);
                var dir = h.Route.Heading(h.U);
                h.T.position = pos;
                h.T.rotation = Quaternion.Slerp(h.T.rotation, Quaternion.LookRotation(dir, Vector3.up), dt * 2f);
                var burn = 0.7f + ease * 0.6f;
                foreach (var e in h.Engines)
                    e.localScale = Vector3.one * (2.6f * burn);
                if (h.U < 1f)
                    continue;
                h.Route = null;
                h.Dwell = Random.Range(6f, 16f);
                // At a rim gate the hauler jumps out (gone until its next run).
                if (_ports[h.To].Edge)
                    Show(h, false);
                foreach (var e in h.Engines)
                    e.localScale = Vector3.one * 1.2f;
            }
        }

        static void Show(Hauler h, bool on)
        {
            foreach (var r in h.Parts)
                if (r != null)
                    r.enabled = on;
        }

        static readonly (string type, int x, int y)[][] CivilLayouts =
        {
            new[] { ("HyperspaceDrive", 4, 2), ("CargoHold", 4, 3), ("ShipCore", 4, 4), ("CargoHold", 4, 5), ("SensorArray", 4, 6) },
            new[] { ("HyperspaceDrive", 4, 3), ("CargoHold", 3, 4), ("ShipCore", 4, 4), ("CargoHold", 5, 4), ("CargoHold", 4, 5) },
            new[] { ("HyperspaceDrive", 3, 3), ("HyperspaceDrive", 5, 3), ("ShipCore", 4, 4), ("CargoHold", 4, 3), ("ColonyPod", 4, 5), ("SensorArray", 4, 6) }
        };

        static readonly Mesh[] HaulerMeshes = new Mesh[3];

        static Mesh HaulerMesh(int variant)
        {
            if (HaulerMeshes[variant] != null)
                return HaulerMeshes[variant];
            var mods = new List<FocusShipModule>();
            var id = 1;
            foreach (var (type, x, y) in CivilLayouts[variant])
                mods.Add(new FocusShipModule { Id = id++, Type = type, GridX = x, GridY = y });
            return HaulerMeshes[variant] = ShipHullBuilder.SilhouetteMesh(mods, 900 + variant);
        }
    }
}
