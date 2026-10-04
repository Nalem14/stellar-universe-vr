using Core.UI;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Set dressing of the science lab (round hall, the tech constellation ahead), procedural and on shared
    /// materials, after a clean futuristic research floor: brushed-steel lab benches against the back walls,
    /// their cabinets lined with cyan light strips, monitors on stands and sample racks on top; two robotic
    /// arms working over them; specimen holograms (a DNA helix, a molecule) turning before glass display
    /// panes; glass partitions between the bays; square light panels in the ceiling. Every screen stands on
    /// something: the analysis and synthesizer screens on operator desks, the research bonuses on a wall
    /// frame by the entrance. Static pieces are merged per material (<see cref="MeshBatch"/>).
    /// </summary>
    public static class LabDecor
    {
        public sealed class Refs
        {
            public Transform AnalysisMount;
            public Transform SynthMount;
            /// <summary>Wall frame by the entrance: a screen parented here faces the room (local −z).</summary>
            public Transform BonusMount;
        }

        /// <summary>Wall-mounted bonus screen: angle round the hall (0 = ahead, 180 = the entrance) and size.</summary>
        public const float BonusAngle = 157f;
        public static readonly Vector2 BonusSize = new(1.5f, 1.05f);
        const float BonusCentreY = 1.62f;

        static Texture2D _monitorA;
        static Texture2D _monitorB;

        public static Refs Build(Transform room, CicArtKit art, Color accent, float radius, float height,
            Vector3 analysisDesk, Vector3 synthDesk)
        {
            var refs = new Refs();
            var metal = art.MetalPanel(0.55f);
            // Light brushed steel (cabinets, robot shells): the clean lab look, against the dark trims.
            var steel = art.Lit(art.Wall, new Color(0.7f, 0.75f, 0.8f, 1f), 0.5f, 2f);
            var dark = art.DarkPanel(0.3f);
            var cyan = art.CyanEmit(2.2f);
            var violet = art.Lit(Texture2D.whiteTexture, accent, 2.4f);
            var panel = art.Lit(Texture2D.whiteTexture, new Color(0.62f, 0.8f, 0.92f, 1f), 1.15f);
            var glass = art.Holo(Texture2D.whiteTexture, new Color(0.55f, 0.85f, 1f, 0.11f));
            var vialA = art.Lit(Texture2D.whiteTexture, new Color(0.3f, 1f, 0.6f, 1f), 1.8f);
            var vialB = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.4f, 0.55f, 1f), 1.8f);
            // Monitor faces carry their own light (no flat emission on top: it would wash the dark ground out).
            var screenA = new Material(art.Lit(MonitorTexture(ref _monitorA, 1, accent), new Color(1.5f, 1.5f, 1.5f, 1f), 0f))
                { name = "SU_LabMonitorA" };
            var screenB = new Material(art.Lit(MonitorTexture(ref _monitorB, 2, accent), new Color(1.5f, 1.5f, 1.5f, 1f), 0f))
                { name = "SU_LabMonitorB" };
            var b = new MeshBatch();

            // ── Operator desks: every working screen stands on one ────────────────
            refs.AnalysisMount = GateRoomDecor.Desk(room, "AnalysisDesk", analysisDesk,
                GateRoomDecor.FaceStand(analysisDesk.x, analysisDesk.z), 1.15f, metal, dark, violet, cyan);
            refs.SynthMount = GateRoomDecor.Desk(room, "SynthDesk", synthDesk,
                GateRoomDecor.FaceStand(synthDesk.x, synthDesk.z), 1.05f, metal, dark, violet, cyan);

            // ── Benches along the back walls (the entrance at 180°, between them) ─
            var wallR = radius - 0.72f;
            Bench(b, 100f, wallR, 2.2f, steel, metal, dark, cyan, screenA, screenB, vialA, vialB, robot: true);
            Bench(b, 134f, wallR, 2.1f, steel, metal, dark, cyan, screenB, screenA, vialB, vialA, robot: false);
            Bench(b, 226f, wallR, 2.1f, steel, metal, dark, cyan, screenA, screenB, vialA, vialB, robot: false);
            Bench(b, 260f, wallR, 2.2f, steel, metal, dark, cyan, screenB, screenA, vialB, vialA, robot: true);

            // Specimen holograms over the two middle benches, before their glass display panes.
            Specimen(room, art, b, 134f, wallR, glass, metal, cyan, new Color(0.35f, 0.9f, 1f, 0.55f), helix: true);
            Specimen(room, art, b, 226f, wallR, glass, metal, cyan, new Color(1f, 0.42f, 0.52f, 0.55f), helix: false);

            // Robotic arms on the two side benches (animated: <see cref="LabArm"/>).
            LabArm.Build(room, Polar(100f, wallR + 0.05f, 0.91f), 100f, steel, dark, cyan);
            LabArm.Build(room, Polar(260f, wallR + 0.05f, 0.91f), 260f, steel, dark, cyan);

            // ── Glass partitions between the bays, radial from the wall ────────────
            foreach (var a in new[] { 82f, 117f, 243f, 278f })
                Partition(b, a, radius, height, glass, metal, cyan);

            // ── Ceiling: square light panels in a ring, framed ─────────────────────
            for (var i = 0; i < 8; i++)
            {
                var a = 22.5f + i * 45f;
                var p = Polar(a, radius * 0.66f, height - 0.035f);
                var rot = Quaternion.Euler(0f, a, 0f);
                b.Box(p + Vector3.up * 0.01f, new Vector3(1.12f, 0.04f, 1.12f), dark, rot);
                b.Box(p - Vector3.up * 0.012f, new Vector3(0.98f, 0.012f, 0.98f), panel, rot);
            }

            // ── Wall frame by the entrance: the research bonuses screen sits in it ─
            var frameRot = Quaternion.Euler(0f, BonusAngle, 0f);
            var frameAt = Polar(BonusAngle, radius - 0.14f, BonusCentreY);
            b.Box(frameAt, new Vector3(BonusSize.x + 0.12f, BonusSize.y + 0.12f, 0.08f), metal, frameRot);
            b.Box(frameAt + frameRot * new Vector3(0f, -BonusSize.y * 0.5f - 0.08f, -0.06f),
                new Vector3(BonusSize.x * 0.92f, 0.02f, 0.03f), cyan, frameRot);
            for (var s = -1; s <= 1; s += 2)
                b.Box(frameAt + frameRot * new Vector3(s * (BonusSize.x * 0.5f + 0.02f), 0f, 0.02f),
                    new Vector3(0.06f, BonusSize.y + 0.3f, 0.1f), dark, frameRot);
            refs.BonusMount = new GameObject("BonusMount").transform;
            refs.BonusMount.SetParent(room, false);
            refs.BonusMount.localPosition = Polar(BonusAngle, radius - 0.19f, BonusCentreY);
            refs.BonusMount.localRotation = frameRot;

            b.Build(room, "LabDressing");
            return refs;
        }

        /// <summary>Room point at an angle round the hall (0° = +z, ahead; 90° = +x) and a radius.</summary>
        static Vector3 Polar(float angleDeg, float r, float y)
        {
            var a = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
        }

        /// <summary>
        /// A lab bench against the wall at <paramref name="angle"/>, its front (local −z) to the room: steel
        /// cabinet with cyan light strips, a steel top with a lit edge, an upstand and a shelf with a sample
        /// rack, two monitors on stands.
        /// </summary>
        static void Bench(MeshBatch b, float angle, float r, float len, Material steel, Material metal, Material dark,
            Material cyan, Material screen1, Material screen2, Material vial1, Material vial2, bool robot)
        {
            var c = Polar(angle, r, 0f);
            var rot = Quaternion.Euler(0f, angle, 0f);
            void Part(Vector3 local, Vector3 size, Material m, Quaternion? lr = null) =>
                b.Box(c + rot * local, size, m, rot * (lr ?? Quaternion.identity));

            Part(new Vector3(0f, 0.44f, 0f), new Vector3(len, 0.8f, 0.68f), steel);
            Part(new Vector3(0f, 0.04f, -0.31f), new Vector3(len - 0.04f, 0.08f, 0.06f), dark);
            // Front light strips (the cabinet's signature) and drawer seams.
            Part(new Vector3(0f, 0.66f, -0.345f), new Vector3(len * 0.86f, 0.018f, 0.01f), cyan);
            Part(new Vector3(0f, 0.3f, -0.345f), new Vector3(len * 0.86f, 0.018f, 0.01f), cyan);
            for (var k = -1; k <= 1; k += 2)
                Part(new Vector3(k * len * 0.17f, 0.48f, -0.343f), new Vector3(0.01f, 0.28f, 0.008f), metal);
            // Top, lit front edge.
            Part(new Vector3(0f, 0.865f, -0.02f), new Vector3(len + 0.06f, 0.05f, 0.76f), metal);
            Part(new Vector3(0f, 0.85f, -0.405f), new Vector3(len + 0.04f, 0.012f, 0.012f), cyan);
            // Upstand and shelf.
            Part(new Vector3(0f, 1.18f, 0.33f), new Vector3(len, 0.6f, 0.05f), dark);
            Part(new Vector3(0f, 1.46f, 0.24f), new Vector3(len, 0.03f, 0.2f), steel);
            Part(new Vector3(0f, 1.445f, 0.14f), new Vector3(len * 0.95f, 0.008f, 0.008f), cyan);
            // Sample rack on the shelf: two rows of vials in two liquids.
            for (var k = 0; k < 8; k++)
            {
                var x = -len * 0.42f + k * 0.07f;
                Part(new Vector3(x, 1.52f, 0.22f), new Vector3(0.026f, 0.1f, 0.026f), k % 3 == 0 ? vial2 : vial1);
            }

            // Monitors on stands (left and right; the robot works between them).
            for (var s = -1; s <= 1; s += 2)
            {
                var x = s * len * (robot ? 0.33f : 0.26f);
                var tilt = Quaternion.Euler(8f, s * -10f, 0f);
                Part(new Vector3(x, 0.9f, 0.12f), new Vector3(0.18f, 0.012f, 0.12f), dark);
                Part(new Vector3(x, 1.0f, 0.15f), new Vector3(0.035f, 0.2f, 0.035f), metal);
                Part(new Vector3(x, 1.2f, 0.13f), new Vector3(0.5f, 0.32f, 0.03f), dark, tilt);
                var face = c + rot * new Vector3(x, 1.2f, 0.113f);
                b.Add(QuadMesh, Matrix4x4.TRS(face, rot * tilt, new Vector3(0.46f, 0.28f, 1f)),
                    s < 0 ? screen1 : screen2);
            }
        }

        /// <summary>A glass pane with a steel frame on the bench, a hologram turning before it on a lit pad.</summary>
        static void Specimen(Transform room, CicArtKit art, MeshBatch b, float angle, float r, Material glass, Material metal,
            Material cyan, Color tint, bool helix)
        {
            var c = Polar(angle, r, 0f);
            var rot = Quaternion.Euler(0f, angle, 0f);
            // The pane stands at the back of the top, as tall as the shelf it sits before.
            b.Box(c + rot * new Vector3(0f, 1.32f, 0.05f), new Vector3(0.95f, 0.8f, 0.012f), glass, rot);
            b.Box(c + rot * new Vector3(0f, 1.73f, 0.05f), new Vector3(0.97f, 0.025f, 0.03f), metal, rot);
            b.Box(c + rot * new Vector3(0f, 0.91f, 0.05f), new Vector3(0.97f, 0.025f, 0.03f), metal, rot);
            for (var s = -1; s <= 1; s += 2)
                b.Box(c + rot * new Vector3(s * 0.485f, 1.32f, 0.05f), new Vector3(0.025f, 0.84f, 0.03f), metal, rot);
            // Projector pad on the top, its ring lit.
            b.Tube(c + rot * new Vector3(0f, 0.9f, -0.17f), Vector3.up, 0.11f, 0.02f, metal);
            b.Tube(c + rot * new Vector3(0f, 0.912f, -0.17f), Vector3.up, 0.085f, 0.006f, cyan);

            var holo = art.Holo(Texture2D.whiteTexture, tint);
            var spec = new MeshBatch();
            if (helix)
            {
                // Double helix: two strands of beads and the rungs between them, 0.5 m tall.
                const int beads = 22;
                for (var i = 0; i < beads; i++)
                {
                    var t = i / (float)(beads - 1);
                    var a = t * Mathf.PI * 3.2f;
                    var y = t * 0.5f;
                    var p1 = new Vector3(Mathf.Cos(a) * 0.09f, y, Mathf.Sin(a) * 0.09f);
                    var p2 = -new Vector3(p1.x, 0f, p1.z) + Vector3.up * y;
                    spec.Add(MeshBatch.Sphere, Matrix4x4.TRS(p1, Quaternion.identity, Vector3.one * 0.026f), holo);
                    spec.Add(MeshBatch.Sphere, Matrix4x4.TRS(p2, Quaternion.identity, Vector3.one * 0.026f), holo);
                    if (i % 2 == 0)
                        spec.Pipe(p1, p2, 0.005f, holo);
                }
            }
            else
            {
                // Molecule: a ring of six atoms round a core, two side groups, bonds between them.
                var core = Vector3.up * 0.24f;
                spec.Add(MeshBatch.Sphere, Matrix4x4.TRS(core, Quaternion.identity, Vector3.one * 0.07f), holo);
                var prev = Vector3.zero;
                for (var i = 0; i <= 6; i++)
                {
                    var a = i * Mathf.PI / 3f;
                    var p = core + new Vector3(Mathf.Cos(a) * 0.14f, Mathf.Sin(a * 2f) * 0.03f, Mathf.Sin(a) * 0.14f);
                    if (i < 6)
                    {
                        spec.Add(MeshBatch.Sphere, Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * 0.045f), holo);
                        spec.Pipe(core, p, 0.006f, holo);
                        if (i % 3 == 0)
                        {
                            var q = p + (p - core).normalized * 0.11f + Vector3.up * 0.07f;
                            spec.Add(MeshBatch.Sphere, Matrix4x4.TRS(q, Quaternion.identity, Vector3.one * 0.035f), holo);
                            spec.Pipe(p, q, 0.005f, holo);
                        }
                    }

                    if (i > 0)
                        spec.Pipe(prev, p, 0.005f, holo);
                    prev = p;
                }
            }

            var root = spec.Build(room, helix ? "HoloHelix" : "HoloMolecule");
            root.localPosition = c + rot * new Vector3(0f, 0.95f, -0.17f);
            var spin = root.gameObject.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = helix ? 22f : -16f;
            spin.BobMeters = 0.012f;
            spin.SetOrigin(root.localPosition);
        }

        /// <summary>A floor-to-ceiling glass partition from the wall toward the room, framed in steel, lit at its foot.</summary>
        static void Partition(MeshBatch b, float angle, float radius, float height, Material glass, Material metal, Material cyan)
        {
            const float depth = 1.5f;
            var mid = Polar(angle, radius - 0.12f - depth * 0.5f, 0f);
            var rot = Quaternion.Euler(0f, angle + 90f, 0f);
            var h = height - 0.3f;
            b.Box(mid + Vector3.up * (h * 0.5f + 0.06f), new Vector3(depth, h, 0.015f), glass, rot);
            b.Box(mid + Vector3.up * 0.03f, new Vector3(depth + 0.04f, 0.06f, 0.08f), metal, rot);
            b.Box(mid + Vector3.up * (h + 0.08f), new Vector3(depth + 0.04f, 0.05f, 0.06f), metal, rot);
            b.Box(mid + Vector3.up * 0.07f, new Vector3(depth - 0.1f, 0.01f, 0.03f), cyan, rot);
            var inner = Polar(angle, radius - 0.12f - depth, 0f);
            b.Box(inner + Vector3.up * (h * 0.5f + 0.06f), new Vector3(0.06f, h + 0.1f, 0.06f), metal, rot);
        }

        static Mesh _quad;

        static Mesh QuadMesh
        {
            get
            {
                if (_quad != null)
                    return _quad;
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _quad = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
                _quad.name = "SU_LabQuad";
                var white = new Color[_quad.vertexCount];
                for (var i = 0; i < white.Length; i++)
                    white[i] = Color.white;
                _quad.colors = white;
                Object.Destroy(go);
                return _quad;
            }
        }

        /// <summary>
        /// A lab monitor face (256 × 160): dark ground, a circular scan with its sweep, a waveform, level bars —
        /// two layouts so neighbouring screens differ. Baked once, point-sampled-free bilinear.
        /// </summary>
        static Texture2D MonitorTexture(ref Texture2D cache, int variant, Color accent)
        {
            if (cache != null)
                return cache;
            const int w = 256, h = 160;
            var px = new Color32[w * h];
            var ground = new Color32(6, 16, 30, 255);
            var grid = new Color32(14, 38, 60, 255);
            var line = new Color32(80, 215, 250, 255);
            var dim = new Color32(30, 100, 135, 255);
            var hot = new Color32((byte)(accent.r * 255), (byte)(accent.g * 255), (byte)(accent.b * 255), 255);
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    px[y * w + x] = (x % 16 == 0 || y % 16 == 0) ? grid : ground;

            void Dot(int x, int y, Color32 c)
            {
                if (x >= 0 && y >= 0 && x < w && y < h)
                    px[y * w + x] = c;
            }

            var rnd = new System.Random(733 + variant * 31);
            // Circular scan (left on A, right on B) with rings, cross and sweep.
            var cx = variant == 1 ? 70 : 186;
            var cy = 84;
            for (var a = 0; a < 720; a++)
            {
                var t = a * Mathf.PI / 360f;
                for (var ring = 1; ring <= 3; ring++)
                    Dot(cx + Mathf.RoundToInt(Mathf.Cos(t) * 18 * ring), cy + Mathf.RoundToInt(Mathf.Sin(t) * 18 * ring),
                        ring == 3 ? line : dim);
            }

            for (var k = -54; k <= 54; k++)
            {
                Dot(cx + k, cy, dim);
                Dot(cx, cy + k, dim);
            }

            for (var k = 0; k < 52; k++)
                Dot(cx + Mathf.RoundToInt(k * 0.8f), cy + Mathf.RoundToInt(k * 0.6f), line);
            for (var i = 0; i < 9; i++)
                Dot(cx - 40 + rnd.Next(80), cy - 40 + rnd.Next(80), hot);

            // Waveform across the other half, bars under it.
            var x0 = variant == 1 ? 140 : 12;
            var prev = cy;
            for (var x = 0; x < 104; x++)
            {
                var v = cy + 22 + Mathf.RoundToInt(Mathf.Sin(x * 0.21f + variant) * 12f + Mathf.Sin(x * 0.67f) * 5f);
                for (var y = Mathf.Min(prev, v); y <= Mathf.Max(prev, v); y++)
                    Dot(x0 + x, y, line);
                prev = v;
            }

            for (var i = 0; i < 8; i++)
            {
                var bh = 8 + rnd.Next(34);
                for (var y = 0; y < bh; y++)
                    for (var x = 0; x < 9; x++)
                        Dot(x0 + i * 13 + x, 18 + y, i == 5 ? hot : dim);
            }

            // Header strip.
            for (var x = 6; x < w - 6; x++)
                for (var y = h - 14; y < h - 8; y++)
                    Dot(x, y, x < 60 ? hot : x % 24 < 18 ? dim : ground);

            cache = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SU_LabMonitor" + variant, wrapMode = TextureWrapMode.Clamp };
            cache.SetPixels32(px);
            cache.Apply(true, true);
            return cache;
        }
    }

    /// <summary>
    /// A robotic arm on a lab bench, working: base turning, shoulder, elbow and wrist on slow out-of-phase
    /// sines, a lit tool tip. Five transforms, shared materials, no allocation per frame.
    /// </summary>
    public sealed class LabArm : MonoBehaviour
    {
        Transform _yaw;
        Transform _shoulder;
        Transform _elbow;
        Transform _wrist;
        float _phase;

        public static LabArm Build(Transform room, Vector3 pos, float wallAngle, Material shell, Material joint, Material glow)
        {
            var root = new GameObject("LabArm").transform;
            root.SetParent(room, false);
            root.localPosition = pos;
            // Faces the room (the bench front).
            root.localRotation = Quaternion.Euler(0f, wallAngle + 180f, 0f);
            root.localScale = Vector3.one * 1.35f;
            var arm = root.gameObject.AddComponent<LabArm>();
            arm._phase = wallAngle * 0.05f;

            Piece(root, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.2f, 0.03f, 0.2f), joint);
            arm._yaw = Node(root, "Yaw", new Vector3(0f, 0.06f, 0f));
            Piece(arm._yaw, PrimitiveType.Cylinder, new Vector3(0f, 0.08f, 0f), new Vector3(0.13f, 0.08f, 0.13f), shell);
            Piece(arm._yaw, PrimitiveType.Cylinder, new Vector3(0f, 0.005f, 0f), new Vector3(0.16f, 0.006f, 0.16f), glow);
            arm._shoulder = Node(arm._yaw, "Shoulder", new Vector3(0f, 0.17f, 0f));
            Piece(arm._shoulder, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.11f, joint);
            Piece(arm._shoulder, PrimitiveType.Cube, new Vector3(0f, 0.2f, 0f), new Vector3(0.07f, 0.4f, 0.08f), shell);
            arm._elbow = Node(arm._shoulder, "Elbow", new Vector3(0f, 0.4f, 0f));
            Piece(arm._elbow, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.085f, joint);
            Piece(arm._elbow, PrimitiveType.Cube, new Vector3(0f, 0.16f, 0f), new Vector3(0.055f, 0.32f, 0.06f), shell);
            arm._wrist = Node(arm._elbow, "Wrist", new Vector3(0f, 0.32f, 0f));
            Piece(arm._wrist, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.07f, 0.03f, 0.07f), joint);
            for (var s = -1; s <= 1; s += 2)
                Piece(arm._wrist, PrimitiveType.Cube, new Vector3(s * 0.022f, 0.09f, 0f), new Vector3(0.012f, 0.07f, 0.03f), shell);
            Piece(arm._wrist, PrimitiveType.Sphere, new Vector3(0f, 0.1f, 0f), Vector3.one * 0.02f, glow);
            arm.Pose(0f);
            return arm;
        }

        static Transform Node(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static void Piece(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void Pose(float t)
        {
            _yaw.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.23f + _phase) * 40f, 0f);
            // Leaning toward the bench front (local +z), elbow folding back down over the work.
            _shoulder.localRotation = Quaternion.Euler(28f + Mathf.Sin(t * 0.31f + _phase) * 12f, 0f, 0f);
            _elbow.localRotation = Quaternion.Euler(62f + Mathf.Sin(t * 0.41f + 1.3f + _phase) * 18f, 0f, 0f);
            _wrist.localRotation = Quaternion.Euler(30f + Mathf.Sin(t * 0.6f + _phase) * 20f, Mathf.Sin(t * 0.5f) * 60f, 0f);
        }

        void Update() => Pose(Time.time);
    }
}
