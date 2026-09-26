using System;
using System.Collections.Generic;
using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The gate: a machined ring with a rotating glyph track (the 36 address symbols 0-9 A-Z of the server's
    /// base-36 gate addresses), six locks around it — one per address group — and the event horizon inside.
    /// <see cref="Dial"/> spins the track to each group's symbol in turn and seats a lock, then the horizon
    /// bursts toward the room and settles; an incoming connection opens it without dialing, tinted red.
    /// Local space: ring in XY, centre at the origin, front (the room) toward +Z.
    /// </summary>
    public sealed class GateRing : MonoBehaviour
    {
        public const float Outer = 3.05f;
        public const float Inner = 2.45f;
        const string Symbols = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const float SpinSeconds = 0.62f;
        const float LockSeconds = 0.22f;

        enum State
        {
            Idle,
            Dialing,
            Open,
            Closing
        }

        public bool IsOpen => _state == State.Open;
        public bool Incoming => _incoming && _state == State.Open;

        /// <summary>Lock <paramref name="i"/> is seated (dial progress, or the whole ring once open).</summary>
        public bool LockLit(int i) => _locks[i] != null && _locks[i].sharedMaterial != _lockOff;
        public bool Busy => _state == State.Dialing || _state == State.Closing;
        public Transform Horizon => _horizon.transform;

        Transform _track;
        readonly Renderer[] _locks = new Renderer[6];
        readonly Transform[] _lockBlocks = new Transform[6];
        readonly List<TextMeshPro> _glyphs = new();
        MeshRenderer _horizon;
        Material _horizonMat;
        Material _lockOn;
        Material _lockOff;
        Material _lockAlarm;
        Light _light;
        ParticleSystem _burst;

        State _state;
        string[] _groups = new string[6];
        int _group;
        float _t;
        float _trackFrom;
        float _trackTo;
        float _trackAngle;
        bool _incoming;
        float _open;
        float _surgeT = -1f;
        Action _onOpen;
        int _ripple;

        static readonly int OpenId = Shader.PropertyToID("_Open");
        static readonly int SurgeId = Shader.PropertyToID("_Surge");
        static readonly int AlarmId = Shader.PropertyToID("_Alarm");
        static readonly int[] RippleIds =
        {
            Shader.PropertyToID("_Ripple0"), Shader.PropertyToID("_Ripple1"), Shader.PropertyToID("_Ripple2"),
            Shader.PropertyToID("_Ripple3")
        };

        public static GateRing Build(Transform parent, Vector3 localPos, CicArtKit art)
        {
            var root = new GameObject("Gate");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;
            var gate = root.AddComponent<GateRing>();
            gate.Make(art);
            return gate;
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        void Make(CicArtKit art)
        {
            // Ring metal glows faintly from within (the hall has no extra real-time lights on Quest).
            var metal = new Material(DefensePlatformKit.HullMat()) { name = "SU_GateMetal" };
            if (metal.HasProperty("_EmissionMul")) metal.SetFloat("_EmissionMul", 0.5f);
            if (metal.HasProperty("_Color")) metal.SetColor("_Color", new Color(0.62f, 0.64f, 0.72f, 1f));
            if (metal.HasProperty("_RimColor")) metal.SetColor("_RimColor", new Color(0.55f, 0.45f, 1f, 1f));
            var trim = art.Lit(Texture2D.whiteTexture, new Color(0.62f, 0.5f, 1f, 1f), 2.4f);
            var dark = art.DarkPanel(0.25f);
            _lockOff = art.Lit(Texture2D.whiteTexture, new Color(0.25f, 0.12f, 0.06f, 1f), 0.5f);
            _lockOn = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.55f, 0.18f, 1f), 3.2f);
            _lockAlarm = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.2f, 0.12f, 1f), 3.2f);

            // Outer ring: a stepped, bevelled section, faceted every 7.5° (machined, not a tube).
            Part("Ring", RingMesh(new[]
            {
                new Vector2(Inner + 0.02f, -0.3f), new Vector2(Inner + 0.28f, -0.34f), new Vector2(Outer - 0.12f, -0.34f),
                new Vector2(Outer, -0.2f), new Vector2(Outer, 0.2f), new Vector2(Outer - 0.12f, 0.34f),
                new Vector2(2.86f, 0.34f), new Vector2(2.84f, 0.22f), new Vector2(2.78f, 0.22f), new Vector2(2.76f, 0.34f),
                new Vector2(Inner + 0.1f, 0.34f), new Vector2(Inner, 0.22f), new Vector2(Inner, -0.22f),
                new Vector2(Inner + 0.02f, -0.3f)
            }, 48, true), metal, transform);

            // Lit trims: the inner lip around the horizon and a band round the outer rim.
            Part("InnerLip", RingMesh(new[] { new Vector2(Inner - 0.005f, -0.18f), new Vector2(Inner - 0.005f, 0.18f) }, 72, false),
                trim, transform);
            Part("RimBand", RingMesh(new[] { new Vector2(Outer + 0.005f, 0.05f), new Vector2(Outer + 0.005f, -0.05f) }, 72, false),
                trim, transform);

            // Glyph track: a recessed band that turns inside the front face.
            _track = new GameObject("Track").transform;
            _track.SetParent(transform, false);
            Part("TrackBand", RingMesh(new[]
            {
                new Vector2(2.5f, 0.2f), new Vector2(2.5f, 0.27f), new Vector2(2.74f, 0.27f), new Vector2(2.74f, 0.2f)
            }, 72, false), dark, _track);
            for (var i = 0; i < Symbols.Length; i++)
            {
                var a = (90f - i * 10f) * Mathf.Deg2Rad;
                var t = new GameObject("Glyph" + Symbols[i]).AddComponent<TextMeshPro>();
                t.transform.SetParent(_track, false);
                t.transform.localPosition = new Vector3(Mathf.Cos(a) * 2.62f, Mathf.Sin(a) * 2.62f, 0.28f);
                // Upright toward the centre, readable from the room.
                t.transform.localRotation = Quaternion.Euler(0f, 180f, i * 10f);
                t.text = Symbols[i].ToString();
                t.fontSize = 1.5f;
                t.fontStyle = FontStyles.Bold;
                t.alignment = TextAlignmentOptions.Center;
                t.rectTransform.sizeDelta = new Vector2(0.3f, 0.3f);
                t.color = new Color(0.35f, 0.75f, 0.9f, 0.55f);
                _glyphs.Add(t);
            }

            // Six locks: a wedge clamp over the rim with its lamp; lock 0 at the top (the reading mark).
            for (var i = 0; i < 6; i++)
            {
                var ang = 90f - i * 60f;
                var holder = new GameObject("Lock" + i).transform;
                holder.SetParent(transform, false);
                holder.localRotation = Quaternion.Euler(0f, 0f, ang - 90f);
                var block = new GameObject("Clamp").transform;
                block.SetParent(holder, false);
                block.localPosition = new Vector3(0f, Outer - 0.05f, 0f);
                Part("Wedge", WedgeMesh(), metal, block);
                var lamp = Part("Lamp", WedgeLampMesh(), _lockOff, block);
                _locks[i] = lamp.GetComponent<MeshRenderer>();
                _lockBlocks[i] = block;
            }

            // Horizon: radial disc (rings for the vertex surge / ripples), uv = local xy in -1..1.
            var shader = Shader.Find("SU/EventHorizon");
            _horizonMat = shader != null ? new Material(shader) : art.Holo(Texture2D.whiteTexture, CicArtKit.Cyan);
            _horizonMat.name = "SU_EventHorizon";
            var h = Part("Horizon", DiscMesh(24, 72), _horizonMat, transform);
            h.transform.localScale = new Vector3(Inner + 0.02f, Inner + 0.02f, 1f);
            h.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            _horizon = h.GetComponent<MeshRenderer>();
            _horizon.enabled = false;

            // Foot clamps into the dais.
            for (var side = -1; side <= 1; side += 2)
            {
                var foot = GameObject.CreatePrimitive(PrimitiveType.Cube);
                foot.name = "Foot";
                Destroy(foot.GetComponent<Collider>());
                foot.transform.SetParent(transform, false);
                foot.transform.localPosition = new Vector3(side * 1.55f, -Outer + 0.35f, 0f);
                foot.transform.localRotation = Quaternion.Euler(0f, 0f, side * 28f);
                foot.transform.localScale = new Vector3(0.7f, 0.9f, 0.9f);
                foot.GetComponent<MeshRenderer>().sharedMaterial = metal;
            }

            var lightGo = new GameObject("HorizonLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 1.6f);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 14f;
            _light.intensity = 0f;
            _light.color = new Color(0.45f, 0.7f, 1f);
            _light.shadows = LightShadows.None;

            _burst = CombatFxKit.Burst(transform, "HorizonBurst", 90, 1.2f, gravity: false, stretch: true);
        }

        GameObject Part(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>Revolve a (radius, z) section around the Z axis.</summary>
        static Mesh RingMesh(Vector2[] section, int segments, bool flat)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            var centroid = Vector2.zero;
            foreach (var p in section)
                centroid += p;
            centroid /= section.Length;
            for (var s = 0; s < segments; s++)
            {
                var a0 = s / (float)segments * Mathf.PI * 2f;
                var a1 = (s + 1) / (float)segments * Mathf.PI * 2f;
                for (var i = 0; i < section.Length - 1; i++)
                {
                    var p0 = section[i];
                    var p1 = section[i + 1];
                    var q = new[]
                    {
                        new Vector3(Mathf.Cos(a0) * p0.x, Mathf.Sin(a0) * p0.x, p0.y),
                        new Vector3(Mathf.Cos(a0) * p1.x, Mathf.Sin(a0) * p1.x, p1.y),
                        new Vector3(Mathf.Cos(a1) * p1.x, Mathf.Sin(a1) * p1.x, p1.y),
                        new Vector3(Mathf.Cos(a1) * p0.x, Mathf.Sin(a1) * p0.x, p0.y)
                    };
                    var edge = p1 - p0;
                    var pn = new Vector2(-edge.y, edge.x).normalized;
                    // Outward from the section's centroid, whatever the winding of the outline.
                    if (Vector2.Dot(pn, (p0 + p1) * 0.5f - centroid) < 0f)
                        pn = -pn;
                    Vector3 N(float a) => new Vector3(Mathf.Cos(a) * pn.x, Mathf.Sin(a) * pn.x, pn.y).normalized;
                    var na = flat ? N((a0 + a1) * 0.5f) : N(a0);
                    var nb = flat ? na : N(a1);
                    var b = v.Count;
                    v.AddRange(q);
                    n.Add(na);
                    n.Add(na);
                    n.Add(nb);
                    n.Add(nb);
                    var face = Vector3.Cross(q[1] - q[0], q[2] - q[0]);
                    if (Vector3.Dot(face, na) >= 0f)
                        t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                    else
                        t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }

            var mesh = new Mesh { name = "SU_GateRing" };
            mesh.indexFormat = v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>V clamp riding over the rim (local +Y outward), front face at +Z.</summary>
        static Mesh WedgeMesh()
        {
            var m = new Mesh { name = "SU_GateLock" };
            var p = new[]
            {
                new Vector3(-0.34f, 0.32f, 0.44f), new Vector3(0.34f, 0.32f, 0.44f), new Vector3(0.12f, -0.34f, 0.44f),
                new Vector3(-0.12f, -0.34f, 0.44f),
                new Vector3(-0.34f, 0.32f, -0.44f), new Vector3(0.34f, 0.32f, -0.44f), new Vector3(0.12f, -0.34f, -0.44f),
                new Vector3(-0.12f, -0.34f, -0.44f)
            };
            var faces = new[]
            {
                new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 5, 1, 0 }, new[] { 3, 2, 6, 7 }, new[] { 4, 0, 3, 7 },
                new[] { 1, 5, 6, 2 }
            };
            BuildFaces(m, p, faces);
            return m;
        }

        /// <summary>The lamp inset in the clamp's front face.</summary>
        static Mesh WedgeLampMesh()
        {
            var m = new Mesh { name = "SU_GateLockLamp" };
            var p = new[]
            {
                new Vector3(-0.2f, 0.2f, 0.46f), new Vector3(0.2f, 0.2f, 0.46f), new Vector3(0.06f, -0.2f, 0.46f),
                new Vector3(-0.06f, -0.2f, 0.46f),
                new Vector3(-0.2f, 0.2f, 0.4f), new Vector3(0.2f, 0.2f, 0.4f), new Vector3(0.06f, -0.2f, 0.4f),
                new Vector3(-0.06f, -0.2f, 0.4f)
            };
            BuildFaces(m, p, new[] { new[] { 0, 1, 2, 3 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 4, 5, 1, 0 } });
            return m;
        }

        static void BuildFaces(Mesh m, Vector3[] p, int[][] faces)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            var n = new List<Vector3>();
            var centre = Vector3.zero;
            foreach (var q in p)
                centre += q;
            centre /= p.Length;
            foreach (var f in faces)
            {
                var b = v.Count;
                var normal = Vector3.Cross(p[f[1]] - p[f[0]], p[f[2]] - p[f[0]]).normalized;
                var mid = (p[f[0]] + p[f[1]] + p[f[2]] + p[f[3]]) * 0.25f;
                var outward = Vector3.Dot(normal, mid - centre) >= 0f;
                if (!outward)
                    normal = -normal;
                foreach (var i in f)
                {
                    v.Add(p[i]);
                    n.Add(normal);
                }

                // Triangle (a, b, c) faces the side of cross(b − a, c − a): keep the order when that is outward.
                if (outward)
                    t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                else
                    t.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }

            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
        }

        internal static Mesh DiscMesh(int rings, int segments)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            v.Add(Vector3.zero);
            uv.Add(Vector2.zero);
            for (var r = 1; r <= rings; r++)
            {
                var rad = r / (float)rings;
                for (var s = 0; s < segments; s++)
                {
                    var a = s / (float)segments * Mathf.PI * 2f;
                    var p = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                    v.Add(new Vector3(p.x, p.y, 0f));
                    uv.Add(p);
                }
            }

            for (var s = 0; s < segments; s++)
                t.AddRange(new[] { 0, 1 + (s + 1) % segments, 1 + s });
            for (var r = 1; r < rings; r++)
            {
                var a0 = 1 + (r - 1) * segments;
                var a1 = 1 + r * segments;
                for (var s = 0; s < segments; s++)
                {
                    var s1 = (s + 1) % segments;
                    t.AddRange(new[] { a0 + s, a0 + s1, a1 + s1, a0 + s, a1 + s1, a1 + s });
                }
            }

            var m = new Mesh { name = "SU_EventHorizonDisc" };
            m.SetVertices(v);
            m.SetUVs(0, uv);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, new Vector3(2.2f, 2.2f, 6f));
            return m;
        }

        // ── Sequences ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Dial <paramref name="address"/> ("XX-XX-XX-XX-XX-XX"): spin to each group's symbol, seat its lock,
        /// then open. <paramref name="onOpen"/> fires when the horizon stands.
        /// </summary>
        public void Dial(string address, Action onOpen)
        {
            var parts = (address ?? string.Empty).Split('-');
            for (var i = 0; i < 6; i++)
                _groups[i] = i < parts.Length && parts[i].Length > 0 ? parts[i] : "00";
            ResetLocks();
            _incoming = false;
            _onOpen = onOpen;
            _group = 0;
            BeginSpin();
            _state = State.Dialing;
        }

        /// <summary>A connection already stands (entering the room, or dialed from the far side).</summary>
        public void OpenNow(bool incoming)
        {
            _incoming = incoming;
            for (var i = 0; i < 6; i++)
                _locks[i].sharedMaterial = incoming ? _lockAlarm : _lockOn;
            if (_state == State.Open)
            {
                _horizonMat.SetFloat(AlarmId, incoming ? 1f : 0f);
                return;
            }

            _onOpen = null;
            Burst(incoming);
        }

        public void Close()
        {
            if (_state == State.Idle || _state == State.Closing)
                return;
            _state = State.Closing;
            _t = 0f;
            CicCue.Whoosh(transform.position);
        }

        /// <summary>Something crosses at world point <paramref name="world"/>: a ripple spreads from there.</summary>
        public void Ripple(Vector3 world, float strength = 1f)
        {
            if (!IsOpen)
                return;
            var local = _horizon.transform.InverseTransformPoint(world);
            _horizonMat.SetVector(RippleIds[_ripple], new Vector4(local.x, local.y, Time.timeSinceLevelLoad, strength));
            _ripple = (_ripple + 1) % RippleIds.Length;
            CombatFxKit.Emit(_burst, world, new Color(0.6f, 0.9f, 1f, 1f), 0.35f, 0.5f);
        }

        void ResetLocks()
        {
            foreach (var l in _locks)
                l.sharedMaterial = _lockOff;
            foreach (var g in _glyphs)
                g.color = new Color(0.35f, 0.75f, 0.9f, 0.55f);
        }

        void BeginSpin()
        {
            var symbol = _groups[_group][0];
            var index = Mathf.Max(0, Symbols.IndexOf(char.ToUpperInvariant(symbol)));
            // Bring the symbol under lock _group; alternate the turning direction per group.
            var lockAngle = -_group * 60f;
            var want = lockAngle + index * 10f;
            _trackFrom = _trackAngle;
            var delta = Mathf.DeltaAngle(_trackAngle, want);
            if (_group % 2 == 1 && delta > 0f) delta -= 360f;
            else if (_group % 2 == 0 && delta < 0f) delta += 360f;
            _trackTo = _trackAngle + delta;
            _t = 0f;
        }

        void Burst(bool incoming)
        {
            _state = State.Open;
            _horizon.enabled = true;
            _horizonMat.SetFloat(AlarmId, incoming ? 1f : 0f);
            _open = 0f;
            _surgeT = 0f;
            CicCue.Boom(transform.position, 0.8f);
            CicCue.Whoosh(transform.position + transform.forward * 2f);
            var c = incoming ? new Color(1f, 0.4f, 0.3f, 1f) : new Color(0.55f, 0.85f, 1f, 1f);
            for (var i = 0; i < 40; i++)
                CombatFxKit.Emit(_burst, transform.position + transform.forward * 0.5f + UnityEngine.Random.insideUnitSphere * 1.2f,
                    c, UnityEngine.Random.Range(0.2f, 0.6f), UnityEngine.Random.Range(0.6f, 1.2f),
                    (transform.forward * UnityEngine.Random.Range(4f, 9f) + UnityEngine.Random.insideUnitSphere * 2f));
            _onOpen?.Invoke();
            _onOpen = null;
        }

        void Update()
        {
            var dt = Time.deltaTime;
            switch (_state)
            {
                case State.Dialing:
                {
                    _t += dt;
                    var u = Mathf.Clamp01(_t / SpinSeconds);
                    _trackAngle = Mathf.Lerp(_trackFrom, _trackTo, Utils.MotionEase.SmoothInOut(u));
                    _track.localRotation = Quaternion.Euler(0f, 0f, _trackAngle);
                    if (_t >= SpinSeconds + LockSeconds)
                    {
                        // Lock seated: lamp on, clamp clunk, the symbol lights.
                        _locks[_group].sharedMaterial = _lockOn;
                        var symbol = char.ToUpperInvariant(_groups[_group][0]);
                        var gi = Symbols.IndexOf(symbol);
                        if (gi >= 0)
                            _glyphs[gi].color = new Color(1f, 0.75f, 0.35f, 1f);
                        CicCue.Deploy(_lockBlocks[_group].position);
                        _group++;
                        if (_group >= 6)
                            Burst(false);
                        else
                            BeginSpin();
                    }
                    else if (_t >= SpinSeconds && _t - dt < SpinSeconds)
                    {
                        _lockBlocks[_group].localPosition = new Vector3(0f, Outer - 0.12f, 0f);
                    }

                    break;
                }
                case State.Open:
                {
                    _open = Mathf.MoveTowards(_open, 1f, dt * 3f);
                    _horizonMat.SetFloat(OpenId, _open);
                    if (_surgeT >= 0f)
                    {
                        _surgeT += dt;
                        // Out in 0.22 s, back with a small overshoot over a second.
                        var s = _surgeT < 0.22f
                            ? _surgeT / 0.22f
                            : Mathf.Exp(-(_surgeT - 0.22f) * 3.2f) * Mathf.Cos((_surgeT - 0.22f) * 5f);
                        _horizonMat.SetFloat(SurgeId, s);
                        if (_surgeT > 2f)
                        {
                            _surgeT = -1f;
                            _horizonMat.SetFloat(SurgeId, 0f);
                        }
                    }

                    _light.color = _incoming ? new Color(1f, 0.35f, 0.25f) : new Color(0.45f, 0.7f, 1f);
                    _light.intensity = 2.4f + Mathf.Sin(Time.time * 2.1f) * 0.35f + (_surgeT >= 0f ? 3f : 0f);
                    break;
                }
                case State.Closing:
                {
                    _t += dt;
                    var k = 1f - Mathf.Clamp01(_t / 0.7f);
                    _horizonMat.SetFloat(OpenId, k);
                    _light.intensity = 2.4f * k;
                    if (_t >= 0.7f)
                    {
                        _horizon.enabled = false;
                        _light.intensity = 0f;
                        ResetLocks();
                        for (var i = 0; i < 6; i++)
                            _lockBlocks[i].localPosition = new Vector3(0f, Outer - 0.05f, 0f);
                        _state = State.Idle;
                    }

                    break;
                }
            }
        }
    }
}
