using System.Collections.Generic;
using Core.UI;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Working-ship dressing round the bridge walls, where the crew horseshoe leaves the deck bare: auxiliary
    /// console banks on the port and starboard walls (sloped desks with live screens, wall monitors, status
    /// strips), equipment lockers and stacked cargo cases toward the aft corners, server racks with blinking
    /// indicators on the two aft diagonals, low sideboards under the aft displays, conduit runs under the
    /// cornice. Pure decor, no gameplay. Same kit language as the crew stations (rounded hardware in
    /// SU/ConsoleMetal, cyan / amber light lines); every screen is SU/FakeScreen (drawn in the shader).
    /// Built once and merged per material: five draw calls for the whole dressing, no per-frame work.
    /// </summary>
    public static class BridgeDecor
    {
        sealed class Kit
        {
            readonly Dictionary<Material, List<CombineInstance>> _parts = new();
            readonly List<Mesh> _temp = new();
            public Matrix4x4 Frame = Matrix4x4.identity;

            public void Box(Material mat, Vector3 size, Vector3 pos, Vector3 euler = default, float radius = -1f)
            {
                var r = radius >= 0f ? radius : Mathf.Min(0.02f, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.3f);
                Add(mat, UiMeshes.RoundedBoxSource(size, r), Frame * Matrix4x4.TRS(pos, Quaternion.Euler(euler), Vector3.one));
            }

            /// <summary>A screen quad facing <paramref name="facing"/> (frame-local), <paramref name="up"/> its top.</summary>
            public void Screen(Material mat, Vector3 centre, Vector3 facing, Vector3 up, Vector2 size, int page, float seed, Color tint)
            {
                var mesh = new Mesh { name = "SU_DecorScreen" };
                var hx = size.x * 0.5f;
                var hy = size.y * 0.5f;
                // Front is local −z (like every screen of the kit): the rotation turns −z to the facing.
                mesh.vertices = new[] { new Vector3(-hx, -hy, 0f), new Vector3(hx, -hy, 0f), new Vector3(-hx, hy, 0f), new Vector3(hx, hy, 0f) };
                mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                var info = new Vector2(page, seed);
                mesh.uv2 = new[] { info, info, info, info };
                mesh.colors = new[] { tint, tint, tint, tint };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                _temp.Add(mesh);
                var rot = Quaternion.LookRotation(-facing.normalized, up);
                Add(mat, mesh, Frame * Matrix4x4.TRS(centre, rot, Vector3.one));
            }

            void Add(Material mat, Mesh mesh, Matrix4x4 m)
            {
                if (!_parts.TryGetValue(mat, out var list))
                    _parts[mat] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = mesh, transform = m });
            }

            public void Bake(Transform root)
            {
                foreach (var kv in _parts)
                {
                    var mesh = new Mesh { name = "SU_BridgeDecor_" + kv.Key.name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    var go = new GameObject("Decor_" + kv.Key.name);
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = kv.Key;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    go.isStatic = true;
                }

                foreach (var m in _temp)
                    Object.Destroy(m);
                _temp.Clear();
                UiMeshes.ReleaseSources();
            }
        }

        static readonly Color ScreenCyan = new(0.35f, 0.9f, 1f);
        static readonly Color ScreenAmber = new(1f, 0.66f, 0.3f);
        static readonly Color ScreenGreen = new(0.45f, 1f, 0.6f);

        static Material _screen;

        static Material ScreenMat()
        {
            if (_screen != null)
                return _screen;
            var shader = Shader.Find("SU/FakeScreen");
            if (shader == null)
            {
                // Always Included; reaching this means a broken build setup — keep a lit glass, never magenta.
                Debug.LogWarning("[SU] BridgeDecor: SU/FakeScreen missing.");
                shader = Shader.Find("SU/UnlitEmissive");
            }

            _screen = new Material(shader) { name = "SU_DecorScreen" };
            return _screen;
        }

        public static void Build(Transform room, CicArtKit art)
        {
            var root = new GameObject("BridgeDecor").transform;
            root.SetParent(room, false);
            var k = new Kit();
            var mats = new Mats
            {
                Body = UiKit.Chassis,
                Panel = UiKit.Cap,
                Dark = UiKit.Bezel,
                Cyan = art.CyanEmit(2.4f),
                Amber = art.AmberEmit(2f),
                Screen = ScreenMat()
            };

            // Side walls, aft to fore: a crate stack in the corner, two lockers, the rib, a console bank.
            foreach (var (edge, sign) in new[] { (BridgeShell.Starboard, 1), (BridgeShell.Port, -1) })
            {
                ConsoleBank(k, mats, root, WallFrame(edge, AlongSide(edge, -0.1f)), 1.8f, sign);
                Locker(k, mats, root, WallFrame(edge, AlongSide(edge, -2.0f)), sign * 3);
                Locker(k, mats, root, WallFrame(edge, AlongSide(edge, -2.85f)), sign * 5);
                Crates(k, mats, root, WallFrame(edge, AlongSide(edge, -3.75f)), sign);
                Conduits(k, mats, edge);
            }

            // Aft diagonals: two racks each.
            foreach (var edge in new[] { BridgeShell.AftStarboard, BridgeShell.AftPort })
            {
                var l = BridgeShell.EdgeLength(edge);
                Rack(k, mats, root, WallFrame(edge, l * 0.5f - 0.36f), edge * 7 + 1);
                Rack(k, mats, root, WallFrame(edge, l * 0.5f + 0.36f), edge * 7 + 2);
            }

            // Aft wall: low sideboards under the two displays, either side of the corridor door.
            var aft = BridgeShell.EdgeLength(BridgeShell.AftWall);
            Sideboard(k, mats, root, WallFrame(BridgeShell.AftWall, aft * 0.5f - 2.35f), 11);
            Sideboard(k, mats, root, WallFrame(BridgeShell.AftWall, aft * 0.5f + 2.35f), 13);

            k.Bake(root);
        }

        /// <summary>
        /// The same kit along the corridor walls (docs: the coursive off the bridge): every piece sits between
        /// the ribs, the portholes and the doors, backs to the wall, leaving a 2.5 m walkway. Wall screens,
        /// door status strips, a locker and a fire cabinet, a bench under a readout, stacked cases, a narrow
        /// console bank. Merged per material like the bridge dressing.
        /// </summary>
        public static void BuildCorridor(Transform corridor, CicArtKit art, float halfWidth)
        {
            var root = new GameObject("CorridorDecor").transform;
            root.SetParent(corridor, false);
            var k = new Kit();
            var m = new Mats
            {
                Body = UiKit.Chassis,
                Panel = UiKit.Cap,
                Dark = UiKit.Bezel,
                Cyan = art.CyanEmit(2.4f),
                Amber = art.AmberEmit(2f),
                Screen = ScreenMat()
            };

            Matrix4x4 Side(int side, float z) => Matrix4x4.TRS(new Vector3(side * halfWidth, 0f, z),
                Quaternion.Euler(0f, side > 0 ? -90f : 90f, 0f), Vector3.one);

            // Starboard (+x), bridge end to gate end.
            WallScreen(k, m, Side(1, 1.25f), 1.55f, new Vector2(0.62f, 0.38f), 2, 0.3f, ScreenCyan);
            StatusStrip(k, m, Side(1, 3.25f), 21);
            Locker(k, m, root, Side(1, 6.0f), 7);
            Crates(k, m, root, Side(1, 8.75f), 1);
            StatusStrip(k, m, Side(1, 10.85f), 23);
            ConsoleBank(k, m, root, Side(1, 13.55f), 1.0f, 3);

            // Port (−x).
            FireCabinet(k, m, root, Side(-1, 1.25f));
            WallScreen(k, m, Side(-1, 3.25f), 1.6f, new Vector2(0.5f, 0.34f), 3, 0.6f, ScreenGreen);
            Bench(k, m, root, Side(-1, 6.0f));
            WallScreen(k, m, Side(-1, 6.0f), 1.62f, new Vector2(0.7f, 0.4f), 1, 0.9f, ScreenAmber);
            WallScreen(k, m, Side(-1, 8.75f), 1.55f, new Vector2(0.62f, 0.38f), 0, 1.2f, ScreenCyan);
            StatusStrip(k, m, Side(-1, 10.85f), 25);
            Crates(k, m, root, Side(-1, 13.55f), -1);
            k.Bake(root);
        }

        static void WallScreen(Kit k, Mats m, Matrix4x4 f, float y, Vector2 size, int page, float seed, Color tint)
        {
            k.Frame = f;
            k.Box(m.Dark, new Vector3(size.x + 0.06f, size.y + 0.06f, 0.05f), new Vector3(0f, y, Back - 0.07f));
            k.Screen(m.Screen, new Vector3(0f, y, Back - 0.04f), Vector3.forward, Vector3.up, size, page, seed, tint);
            k.Box(m.Cyan, new Vector3(size.x * 0.8f, 0.01f, 0.01f), new Vector3(0f, y - size.y * 0.5f - 0.05f, Back - 0.05f));
        }

        static void StatusStrip(Kit k, Mats m, Matrix4x4 f, int seed)
        {
            k.Frame = f;
            k.Box(m.Dark, new Vector3(0.2f, 0.56f, 0.04f), new Vector3(0f, 1.45f, Back - 0.08f));
            k.Screen(m.Screen, new Vector3(0f, 1.45f, Back - 0.055f), Vector3.forward, Vector3.up, new Vector2(0.15f, 0.5f), 4, seed * 0.17f, ScreenCyan);
            k.Box(m.Amber, new Vector3(0.14f, 0.02f, 0.01f), new Vector3(0f, 1.78f, Back - 0.06f));
        }

        static void FireCabinet(Kit k, Mats m, Transform root, Matrix4x4 f)
        {
            k.Frame = f;
            const float w = 0.56f;
            const float h = 0.9f;
            const float d = 0.2f;
            k.Box(m.Body, new Vector3(w, h, d), new Vector3(0f, 1.05f, Back + d * 0.5f - 0.02f));
            k.Box(m.Panel, new Vector3(w - 0.06f, h - 0.06f, 0.02f), new Vector3(0f, 1.05f, Back + d - 0.01f));
            // Hazard bands top and bottom, a lit release handle, a status readout.
            k.Box(m.Amber, new Vector3(w - 0.04f, 0.04f, 0.012f), new Vector3(0f, 1.05f + h * 0.5f - 0.06f, Back + d + 0.004f));
            k.Box(m.Amber, new Vector3(w - 0.04f, 0.04f, 0.012f), new Vector3(0f, 1.05f - h * 0.5f + 0.06f, Back + d + 0.004f));
            k.Box(m.Cyan, new Vector3(0.14f, 0.02f, 0.02f), new Vector3(0f, 1.02f, Back + d + 0.012f));
            k.Screen(m.Screen, new Vector3(0f, 1.2f, Back + d + 0.006f), Vector3.forward, Vector3.up, new Vector2(0.2f, 0.1f), 4, 0.77f, ScreenAmber);
            Block(root, f, new Vector3(w, h, d), new Vector3(0f, 1.05f, Back + d * 0.5f));
        }

        static void Bench(Kit k, Mats m, Transform root, Matrix4x4 f)
        {
            k.Frame = f;
            const float w = 1.0f;
            // A wall-hung bench: seat on two brackets, a light line under it.
            k.Box(m.Panel, new Vector3(w, 0.06f, 0.4f), new Vector3(0f, 0.46f, Back + 0.2f), default, 0.02f);
            for (var i = -1; i <= 1; i += 2)
                k.Box(m.Dark, new Vector3(0.05f, 0.3f, 0.36f), new Vector3(i * (w * 0.5f - 0.08f), 0.3f, Back + 0.18f));
            k.Box(m.Cyan, new Vector3(w - 0.1f, 0.01f, 0.01f), new Vector3(0f, 0.42f, Back + 0.4f));
            Block(root, f, new Vector3(w, 0.5f, 0.42f), new Vector3(0f, 0.25f, Back + 0.21f));
        }

        struct Mats
        {
            public Material Body, Panel, Dark, Cyan, Amber, Screen;
        }

        /// <summary>u along a side wall for a room z (starboard runs aft→fore, port fore→aft).</summary>
        static float AlongSide(int edge, float z)
        {
            var a = BridgeShell.Plan[edge];
            return Mathf.Abs(z - a.y);
        }

        /// <summary>Frame on edge e at u: origin on the wall line at deck level, +z into the room, +x along it.</summary>
        static Matrix4x4 WallFrame(int edge, float u) =>
            Matrix4x4.TRS(BridgeShell.EdgePoint(edge, u, 0f, 0f), Quaternion.Euler(0f, BridgeShell.EdgeYaw(edge), 0f), Vector3.one);

        /// <summary>Solid body for the player (room-scale) — never walk through a console.</summary>
        static void Block(Transform root, Matrix4x4 frame, Vector3 size, Vector3 centre)
        {
            var go = new GameObject("DecorBlock");
            go.transform.SetParent(root, false);
            go.transform.localPosition = frame.MultiplyPoint3x4(centre);
            go.transform.localRotation = frame.rotation;
            go.AddComponent<BoxCollider>().size = size;
        }

        // Back of every piece sits just off the dado (0.12 m from the wall line).
        const float Back = 0.13f;

        static void ConsoleBank(Kit k, Mats m, Transform root, Matrix4x4 f, float w, int seed)
        {
            k.Frame = f;
            const float d = 0.52f;
            var z = Back + d * 0.5f;
            k.Box(m.Dark, new Vector3(w - 0.08f, 0.09f, d - 0.08f), new Vector3(0f, 0.045f, z - 0.02f));
            k.Box(m.Body, new Vector3(w, 0.74f, d), new Vector3(0f, 0.46f, z));
            // Cabinet doors and the kick light.
            for (var i = -1; i <= 1; i += 2)
                k.Box(m.Panel, new Vector3(w * 0.5f - 0.08f, 0.52f, 0.02f), new Vector3(i * w * 0.25f, 0.44f, Back + d + 0.004f));
            k.Box(m.Cyan, new Vector3(w - 0.12f, 0.012f, 0.012f), new Vector3(0f, 0.1f, Back + d + 0.006f));

            // Sloped desk, front edge lower, with two live screens and a lit key strip.
            var desk = Matrix4x4.TRS(new Vector3(0f, 0.87f, z + 0.03f), Quaternion.Euler(16f, 0f, 0f), Vector3.one);
            k.Frame = f * desk;
            k.Box(m.Dark, new Vector3(w + 0.04f, 0.05f, d + 0.1f), Vector3.zero);
            k.Box(m.Body, new Vector3(w - 0.06f, 0.012f, d + 0.02f), new Vector3(0f, 0.026f, 0f));
            k.Screen(m.Screen, new Vector3(-w * 0.24f, 0.034f, -0.06f), Vector3.up, Vector3.back, new Vector2(w * 0.42f, 0.26f),
                seed > 0 ? 0 : 1, 0.13f * seed, ScreenCyan);
            k.Screen(m.Screen, new Vector3(w * 0.24f, 0.034f, -0.06f), Vector3.up, Vector3.back, new Vector2(w * 0.42f, 0.26f),
                2, 0.29f * seed, ScreenCyan);
            k.Box(m.Cyan, new Vector3(w * 0.7f, 0.008f, 0.05f), new Vector3(0f, 0.034f, 0.19f));
            for (var i = 0; i < 6; i++)
                k.Box(m.Amber, new Vector3(0.04f, 0.008f, 0.03f), new Vector3(-w * 0.4f + i * 0.07f, 0.034f, 0.12f));

            // Wall monitors over the desk, tilted down toward someone standing at it, and a status strip between.
            k.Frame = f;
            for (var i = -1; i <= 1; i += 2)
            {
                var c = new Vector3(i * w * 0.27f, 1.62f, Back + 0.05f);
                var tilt = Quaternion.Euler(10f, 0f, 0f);
                var face = tilt * Vector3.forward;
                var up = tilt * Vector3.up;
                k.Box(m.Dark, new Vector3(0.72f, 0.46f, 0.05f), c, new Vector3(10f, 0f, 0f));
                k.Screen(m.Screen, c + face * 0.028f, face, up, new Vector2(0.66f, 0.4f), i < 0 ? 3 : 1, 0.41f * seed + i, i < 0 ? ScreenGreen : ScreenAmber);
            }

            k.Box(m.Dark, new Vector3(0.16f, 0.5f, 0.04f), new Vector3(0f, 1.62f, Back + 0.02f));
            k.Screen(m.Screen, new Vector3(0f, 1.62f, Back + 0.042f), Vector3.forward, Vector3.up, new Vector2(0.12f, 0.46f), 4, 0.7f * seed, ScreenCyan);
            k.Box(m.Cyan, new Vector3(w * 0.9f, 0.012f, 0.012f), new Vector3(0f, 1.95f, Back + 0.03f));
            Block(root, f, new Vector3(w, 1f, d), new Vector3(0f, 0.5f, z));
        }

        static void Locker(Kit k, Mats m, Transform root, Matrix4x4 f, int seed)
        {
            k.Frame = f;
            const float w = 0.78f;
            const float h = 2.05f;
            const float d = 0.46f;
            var z = Back + d * 0.5f;
            k.Box(m.Body, new Vector3(w, h, d), new Vector3(0f, h * 0.5f, z));
            for (var i = -1; i <= 1; i += 2)
            {
                k.Box(m.Panel, new Vector3(w * 0.5f - 0.035f, h - 0.2f, 0.02f), new Vector3(i * w * 0.25f, h * 0.5f - 0.02f, Back + d + 0.004f));
                k.Box(m.Cyan, new Vector3(0.012f, 0.22f, 0.012f), new Vector3(i * 0.05f, h * 0.52f, Back + d + 0.02f));
            }

            // Hazard band and a door status readout.
            k.Box(m.Amber, new Vector3(w - 0.06f, 0.03f, 0.01f), new Vector3(0f, h - 0.06f, Back + d + 0.006f));
            k.Box(m.Dark, new Vector3(0.22f, 0.13f, 0.012f), new Vector3(w * 0.25f, 1.45f, Back + d + 0.016f));
            k.Screen(m.Screen, new Vector3(w * 0.25f, 1.45f, Back + d + 0.024f), Vector3.forward, Vector3.up, new Vector2(0.19f, 0.1f),
                seed % 2 == 0 ? 2 : 4, 0.37f * seed, seed > 0 ? ScreenGreen : ScreenAmber);
            k.Box(m.Dark, new Vector3(w - 0.1f, 0.06f, 0.02f), new Vector3(0f, 0.08f, Back + d + 0.004f));
            Block(root, f, new Vector3(w, h, d), new Vector3(0f, h * 0.5f, z));
        }

        static void Crates(Kit k, Mats m, Transform root, Matrix4x4 f, int seed)
        {
            k.Frame = f;
            // Two cases on the deck, a smaller one on top (turned a little), strapped with light bands.
            Case(k, m, new Vector3(-0.2f, 0f, Back + 0.34f), new Vector3(0.62f, 0.46f, 0.56f), 4f * seed);
            Case(k, m, new Vector3(0.36f, 0f, Back + 0.3f), new Vector3(0.44f, 0.38f, 0.48f), -9f * seed);
            Case(k, m, new Vector3(-0.16f, 0.46f, Back + 0.34f), new Vector3(0.46f, 0.32f, 0.42f), 14f * seed);
            Block(root, f, new Vector3(1.05f, 0.8f, 0.6f), new Vector3(0.05f, 0.4f, Back + 0.33f));
        }

        static void Case(Kit k, Mats m, Vector3 at, Vector3 size, float yaw)
        {
            var parent = k.Frame;
            k.Frame = parent * Matrix4x4.TRS(at, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            k.Box(m.Panel, size, new Vector3(0f, size.y * 0.5f, 0f), default, 0.03f);
            k.Box(m.Dark, new Vector3(size.x + 0.012f, 0.05f, size.z + 0.012f), new Vector3(0f, size.y * 0.22f, 0f));
            k.Box(m.Dark, new Vector3(size.x + 0.012f, 0.05f, size.z + 0.012f), new Vector3(0f, size.y * 0.78f, 0f));
            k.Box(m.Amber, new Vector3(size.x * 0.3f, 0.018f, 0.006f), new Vector3(0f, size.y * 0.5f, size.z * 0.5f + 0.004f));
            k.Frame = parent;
        }

        static void Rack(Kit k, Mats m, Transform root, Matrix4x4 f, int seed)
        {
            k.Frame = f;
            const float w = 0.64f;
            const float h = 2.15f;
            const float d = 0.56f;
            var z = Back + d * 0.5f;
            k.Box(m.Body, new Vector3(w, h, d), new Vector3(0f, h * 0.5f, z));
            k.Box(m.Dark, new Vector3(w - 0.07f, h - 0.14f, 0.02f), new Vector3(0f, h * 0.5f, Back + d + 0.004f));
            // Three indicator blades and a drive-bay readout; a lit crown on top.
            for (var i = 0; i < 3; i++)
                k.Screen(m.Screen, new Vector3(0f, 0.5f + i * 0.5f, Back + d + 0.016f), Vector3.forward, Vector3.up,
                    new Vector2(w - 0.16f, 0.4f), 4, seed * 0.31f + i * 0.17f, ScreenCyan);
            k.Screen(m.Screen, new Vector3(0f, 1.9f, Back + d + 0.016f), Vector3.forward, Vector3.up, new Vector2(w - 0.16f, 0.16f), 1,
                seed * 0.53f, ScreenAmber);
            k.Box(m.Cyan, new Vector3(w - 0.1f, 0.014f, 0.014f), new Vector3(0f, h - 0.02f, Back + d - 0.01f));
            k.Box(m.Cyan, new Vector3(0.014f, h - 0.3f, 0.014f), new Vector3(-w * 0.5f + 0.01f, h * 0.5f, Back + d - 0.01f));
            Block(root, f, new Vector3(w, h, d), new Vector3(0f, h * 0.5f, z));
        }

        static void Sideboard(Kit k, Mats m, Transform root, Matrix4x4 f, int seed)
        {
            k.Frame = f;
            const float w = 2.0f;
            const float h = 0.82f;
            const float d = 0.42f;
            var z = Back + d * 0.5f;
            k.Box(m.Dark, new Vector3(w - 0.06f, 0.08f, d - 0.06f), new Vector3(0f, 0.04f, z - 0.02f));
            k.Box(m.Body, new Vector3(w, h - 0.08f, d), new Vector3(0f, 0.08f + (h - 0.08f) * 0.5f, z));
            for (var i = 0; i < 3; i++)
                k.Box(m.Panel, new Vector3(w / 3f - 0.06f, h - 0.3f, 0.02f), new Vector3((i - 1) * w / 3f, 0.44f, Back + d + 0.004f));
            k.Box(m.Cyan, new Vector3(w - 0.1f, 0.012f, 0.012f), new Vector3(0f, h + 0.006f, Back + d - 0.01f));
            // A small readout set into the top at each end, tilted up to be read standing.
            for (var i = -1; i <= 1; i += 2)
            {
                var face = Quaternion.Euler(-60f, 0f, 0f) * Vector3.forward;
                var c = new Vector3(i * (w * 0.5f - 0.3f), h + 0.02f, Back + d * 0.55f);
                k.Box(m.Dark, new Vector3(0.44f, 0.05f, 0.26f), c, new Vector3(30f, 0f, 0f));
                k.Screen(m.Screen, c + face * 0.028f, face, Quaternion.Euler(-60f, 0f, 0f) * Vector3.up, new Vector2(0.38f, 0.2f),
                    i < 0 ? 0 : 2, seed * 0.19f + i, ScreenCyan);
            }

            Block(root, f, new Vector3(w, h, d), new Vector3(0f, h * 0.5f, z));
        }

        /// <summary>Three pipes on brackets just under the cornice, the full length of the wall.</summary>
        static void Conduits(Kit k, Mats m, int edge)
        {
            var l = BridgeShell.EdgeLength(edge);
            // Centred on the wall, so it never depends on which way the edge runs.
            k.Frame = WallFrame(edge, l * 0.5f);
            var pipes = new[] { (0.08f, 2.62f, 0.1f), (0.055f, 2.74f, 0.09f), (0.1f, 2.5f, 0.12f) };
            foreach (var (dia, y, inset) in pipes)
                k.Box(m.Panel, new Vector3(l - 0.5f, dia, dia), new Vector3(0f, y, inset + dia * 0.5f), default, dia * 0.49f);
            for (var x = -l * 0.5f + 0.6f; x < l * 0.5f - 0.4f; x += 1.3f)
            {
                k.Box(m.Dark, new Vector3(0.06f, 0.36f, 0.2f), new Vector3(x, 2.62f, 0.1f));
                k.Box(m.Cyan, new Vector3(0.02f, 0.02f, 0.012f), new Vector3(x, 2.44f, 0.205f));
            }
        }
    }
}
