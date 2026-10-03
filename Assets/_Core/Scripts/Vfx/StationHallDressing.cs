using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// What makes the station's rotunda a place people work in rather than a shell: columns under the gallery at
    /// the bays' jambs, pilasters, vents, wall terminals and a conduit round the upper drum, lockers by the aft
    /// door, viewing benches before the side bays, floor service grilles, and four holo plinths in the pit (a
    /// world, the station, a convoy, the ring lattice) turning slowly. Everything static is merged per material
    /// (<see cref="MeshBatch"/>, a few draw calls); the four holograms are one renderer each.
    /// </summary>
    public static class StationHallDressing
    {
        static float R => StationCommandShell.R;
        static Vector3 Centre => StationCommandShell.Centre;
        const float GalleryY = StationCommandShell.GalleryY;
        const float WallTop = StationCommandShell.WallTop;
        static readonly float[] Jambs = { 66f, 114f, 148f, 212f, 246f, 294f };
        static readonly float[] Beacons = { 45f, 135f, 225f, 315f };
        static readonly float[] Terminals = { 60f, 120f, 240f, 300f };

        static Vector3 P(float deg, float r, float y) => Centre + LatheMesh.Dir(deg) * r + Vector3.up * y;
        static Quaternion Face(float deg) => Quaternion.LookRotation(-LatheMesh.Dir(deg), Vector3.up);

        static bool Near(float deg, IEnumerable<float> set, float within)
        {
            foreach (var s in set)
                if (Mathf.Abs(Mathf.DeltaAngle(deg, s)) < within)
                    return true;
            return false;
        }

        public static void Build(Transform root, CicArtKit art, Material frame, Material dado, Material glow, Material glowSoft)
        {
            var b = new MeshBatch();
            var dark = art.DarkPanel(0.28f);
            var screen = art.Lit(art.ScreenIdle != null ? art.ScreenIdle : Texture2D.whiteTexture, Color.white, 1.25f);
            var amber = art.AmberEmit(1.8f);

            Columns(b, frame, dado, glow);
            UpperWall(b, frame, dark, glowSoft, screen);
            Lockers(b, frame, dark, glow, amber);
            Benches(b, frame, dark, glowSoft);
            Grilles(b, dark, frame, glowSoft);
            Plinths(b, root, art, frame, dark, glow);

            b.Build(root, "HallDressing");
        }

        // Columns carrying the gallery at the bays' jambs: plinth, shaft with a lit inner face, a bracket into
        // the soffit.
        static void Columns(MeshBatch b, Material frame, Material dado, Material glow)
        {
            var r = R - StationCommandShell.GalleryDepth + 0.15f;
            var top = GalleryY - 0.16f;
            foreach (var deg in Jambs)
            {
                var rot = Face(deg);
                var inward = -LatheMesh.Dir(deg);
                b.Box(P(deg, r, 0.14f), new Vector3(0.42f, 0.28f, 0.42f), dado, rot);
                b.Box(P(deg, r, top * 0.5f), new Vector3(0.26f, top, 0.26f), frame, rot);
                b.Box(P(deg, r, top - 0.12f), new Vector3(0.38f, 0.24f, 0.38f), dado, rot);
                b.Box(P(deg, r, top * 0.5f + 0.1f) + inward * 0.132f, new Vector3(0.035f, top - 0.9f, 0.006f), glow, rot);
                b.Strut(P(deg, r + 0.1f, top - 0.6f), P(deg, R - 0.06f, top - 0.02f), 0.07f, frame);
            }
        }

        // The drum above the gallery: pilasters with a lit spine, vent grilles between them, a terminal by each
        // doorway pair, and a conduit pair clamped under the cornice.
        static void UpperWall(MeshBatch b, Material frame, Material dark, Material glowSoft, Material screen)
        {
            const float y0 = GalleryY + 0.05f;
            const float y1 = WallTop - 0.05f;
            var doors = StationCommandShell.GalleryDoors;
            for (var deg = 7.5f; deg < 360f; deg += 15f)
            {
                if (Near(deg, doors, 4.6f))
                    continue;
                var rot = Face(deg);
                var inward = -LatheMesh.Dir(deg);
                b.Box(P(deg, R - 0.06f, (y0 + y1) * 0.5f), new Vector3(0.2f, y1 - y0, 0.12f), frame, rot);
                b.Box(P(deg, R - 0.06f, 4.75f) + inward * 0.062f, new Vector3(0.03f, 0.9f, 0.004f), glowSoft, rot);
            }

            for (var deg = 0f; deg < 360f; deg += 15f)
            {
                if (Near(deg, doors, 7f) || Near(deg, Beacons, 1f) || Near(deg, Terminals, 1f))
                    continue;
                var rot = Face(deg);
                var inward = -LatheMesh.Dir(deg);
                var c = P(deg, R - 0.03f, 4.7f);
                b.Box(c, new Vector3(1.0f, 0.62f, 0.03f), dark, rot);
                for (var i = 0; i < 5; i++)
                    b.Box(c + Vector3.up * (-0.24f + i * 0.12f) + inward * 0.025f, new Vector3(0.94f, 0.035f, 0.04f), frame, rot);
            }

            foreach (var deg in Terminals)
            {
                var rot = Face(deg);
                var inward = -LatheMesh.Dir(deg);
                var c = P(deg, R - 0.04f, GalleryY + 1.5f);
                b.Box(c, new Vector3(0.78f, 0.5f, 0.06f), frame, rot);
                b.Box(c + inward * 0.032f, new Vector3(0.7f, 0.42f, 0.004f), screen, rot * Quaternion.Euler(0f, 180f, 0f));
                b.Box(c + Vector3.down * 0.32f + inward * 0.06f, new Vector3(0.6f, 0.04f, 0.14f), frame, rot);
            }

            for (var k = 0; k < 2; k++)
            {
                var y = WallTop - 0.16f - k * 0.13f;
                const int n = 96;
                for (var i = 0; i < n; i++)
                {
                    var a0 = i * 360f / n;
                    var a1 = (i + 1) * 360f / n;
                    b.Pipe(P(a0, R - 0.13f, y), P(a1, R - 0.13f, y), 0.045f, k == 0 ? frame : dark);
                }
            }

            for (var deg = 0f; deg < 360f; deg += 15f)
                b.Box(P(deg, R - 0.1f, WallTop - 0.225f), new Vector3(0.08f, 0.32f, 0.16f), dark, Face(deg));
        }

        // Tall equipment lockers between the credenzas and the door portal, status lamps on their faces.
        static void Lockers(MeshBatch b, Material frame, Material dark, Material glow, Material amber)
        {
            foreach (var deg in new[] { 169.8f, 190.2f })
            {
                var rot = Face(deg);
                var inward = -LatheMesh.Dir(deg);
                b.Box(P(deg, R - 0.27f, 1.1f), new Vector3(0.5f, 2.2f, 0.46f), frame, rot);
                b.Box(P(deg, R - 0.27f, 1.1f) + inward * 0.232f, new Vector3(0.42f, 2.05f, 0.008f), dark, rot);
                for (var i = 0; i < 3; i++)
                    b.Box(P(deg, R - 0.27f, 0.55f + i * 0.62f) + inward * 0.238f, new Vector3(0.36f, 0.012f, 0.006f), glow, rot);
                b.Box(P(deg, R - 0.27f, 1.95f) + inward * 0.238f, new Vector3(0.05f, 0.05f, 0.006f), amber, rot);
            }
        }

        // Viewing benches before the two aft bays: a long seat facing the glass, slab legs, a light line under it.
        static void Benches(MeshBatch b, Material frame, Material dark, Material glowSoft)
        {
            foreach (var deg in new[] { 131f, 229f })
            {
                var rot = Quaternion.LookRotation(LatheMesh.Dir(deg), Vector3.up);
                var c = P(deg, R - 1.55f, 0f);
                var side = rot * Vector3.right;
                b.Box(c + Vector3.up * 0.43f, new Vector3(1.7f, 0.07f, 0.44f), frame, rot);
                b.Box(c + Vector3.up * 0.39f, new Vector3(1.6f, 0.02f, 0.4f), dark, rot);
                foreach (var s in new[] { -0.68f, 0.68f })
                    b.Box(c + side * s + Vector3.up * 0.2f, new Vector3(0.08f, 0.4f, 0.38f), dark, rot);
                b.Box(c + Vector3.up * 0.37f, new Vector3(1.2f, 0.006f, 0.03f), glowSoft, rot);
            }
        }

        // Floor service grilles under the beacons, slatted, a fine light frame.
        static void Grilles(MeshBatch b, Material dark, Material frame, Material glowSoft)
        {
            foreach (var deg in Beacons)
            {
                var rot = Face(deg);
                var c = P(deg, R - 2f, 0f);
                b.Box(c + Vector3.up * 0.006f, new Vector3(1.0f, 0.012f, 0.7f), dark, rot);
                for (var i = 0; i < 7; i++)
                    b.Box(c + rot * new Vector3(0f, 0.014f, -0.27f + i * 0.09f), new Vector3(0.9f, 0.01f, 0.03f), frame, rot);
                foreach (var (o, s) in new[]
                         {
                             (new Vector3(0f, 0.013f, 0.37f), new Vector3(1.06f, 0.006f, 0.02f)),
                             (new Vector3(0f, 0.013f, -0.37f), new Vector3(1.06f, 0.006f, 0.02f)),
                             (new Vector3(0.52f, 0.013f, 0f), new Vector3(0.02f, 0.006f, 0.72f)),
                             (new Vector3(-0.52f, 0.013f, 0f), new Vector3(0.02f, 0.006f, 0.72f))
                         })
                    b.Box(c + rot * o, s, glowSoft, rot);
            }
        }

        // Four holo plinths in the pit, between the command disc and the tiers, each with a slow hologram.
        static void Plinths(MeshBatch b, Transform root, CicArtKit art, Material frame, Material dark, Material glow)
        {
            const float r = 3.75f;
            const float top = 0.78f;
            var cyan = new Color(0.45f, 0.9f, 1f);
            var wire = art.Lit(Texture2D.whiteTexture, cyan, 1.9f);
            var core = art.Holo(Texture2D.whiteTexture, new Color(cyan.r, cyan.g, cyan.b, 0.35f));
            var builders = new System.Func<Mesh>[] { WorldHolo, StationHolo, ConvoyHolo, LatticeHolo };
            var degs = new[] { 38f, 322f, 142f, 218f };
            for (var i = 0; i < degs.Length; i++)
            {
                var c = P(degs[i], r, 0f);
                var rot = Face(degs[i]);
                b.Tube(c + Vector3.up * 0.03f, Vector3.up, 0.3f, 0.06f, dark);
                b.Tube(c + Vector3.up * 0.38f, Vector3.up, 0.13f, 0.64f, frame);
                b.Tube(c + Vector3.up * (top - 0.04f), Vector3.up, 0.26f, 0.08f, frame);
                b.Tube(c + Vector3.up * top, Vector3.up, 0.2f, 0.012f, glow);
                b.Box(c + Vector3.up * 0.38f + rot * new Vector3(0f, 0f, -0.131f), new Vector3(0.03f, 0.5f, 0.006f), glow, rot);

                var holo = LatheMesh.Part(root, "PlinthHolo", builders[i](), wire).transform;
                holo.localPosition = c + Vector3.up * (top + 0.45f);
                holo.localScale = Vector3.one * 1.5f;
                holo.gameObject.AddComponent<HoloSpin>().DegreesPerSecond = i % 2 == 0 ? 9f : -7f;
                if (i == 0)
                {
                    var globe = LatheMesh.Part(holo, "PlinthHoloCore", MeshBatch.Sphere, core).transform;
                    globe.localScale = Vector3.one * 0.36f;
                }
            }
        }

        // ── Hologram wireframes (thin ribbons, both faces) ─────────────────────────

        sealed class Wire
        {
            readonly List<Vector3> _v = new();
            readonly List<int> _t = new();
            readonly float _w;

            public Wire(float width) => _w = width;

            public void Line(Vector3 a, Vector3 b, Vector3 up)
            {
                var side = Vector3.Cross(b - a, up).normalized * (_w * 0.5f);
                if (side.sqrMagnitude < 1e-8f)
                    side = Vector3.Cross(b - a, Vector3.right).normalized * (_w * 0.5f);
                var i = _v.Count;
                _v.Add(a - side);
                _v.Add(a + side);
                _v.Add(b + side);
                _v.Add(b - side);
                _t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3, i, i + 2, i + 1, i, i + 3, i + 2 });
            }

            public void Circle(Vector3 c, Vector3 axis, float radius, int segments = 40)
            {
                var u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                var v = Vector3.Cross(axis, u);
                var prev = c + u * radius;
                for (var i = 1; i <= segments; i++)
                {
                    var a = i * Mathf.PI * 2f / segments;
                    var p = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                    Line(prev, p, axis);
                    prev = p;
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name };
                m.SetVertices(_v);
                m.SetTriangles(_t, 0);
                var n = new Vector3[_v.Count];
                var col = new Color[_v.Count];
                for (var i = 0; i < n.Length; i++)
                {
                    n[i] = Vector3.up;
                    col[i] = Color.white;
                }

                m.normals = n;
                m.colors = col;
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh WorldHolo()
        {
            var w = new Wire(0.008f);
            const float r = 0.2f;
            for (var k = -2; k <= 2; k++)
            {
                var lat = k * 30f * Mathf.Deg2Rad;
                w.Circle(Vector3.up * (Mathf.Sin(lat) * r), Vector3.up, Mathf.Cos(lat) * r, 36);
            }

            for (var k = 0; k < 6; k++)
                w.Circle(Vector3.zero, Quaternion.Euler(0f, k * 30f, 0f) * Vector3.right, r, 36);
            var tilt = Quaternion.Euler(18f, 0f, 12f) * Vector3.up;
            w.Circle(Vector3.zero, tilt, 0.3f, 48);
            w.Circle(Vector3.zero, tilt, 0.33f, 48);
            return w.ToMesh("SU_HoloWorld");
        }

        static Mesh StationHolo()
        {
            var w = new Wire(0.008f);
            w.Circle(Vector3.zero, Vector3.up, 0.3f, 48);
            w.Circle(Vector3.up * 0.03f, Vector3.up, 0.3f, 48);
            w.Circle(Vector3.zero, Vector3.up, 0.06f, 16);
            for (var k = 0; k < 6; k++)
            {
                var d = Quaternion.Euler(0f, k * 60f, 0f) * Vector3.forward;
                w.Line(d * 0.06f, d * 0.3f, Vector3.up);
                w.Line(d * 0.3f, d * 0.3f + Vector3.up * 0.03f, d);
            }

            w.Line(Vector3.down * 0.22f, Vector3.up * 0.22f, Vector3.forward);
            w.Circle(Vector3.up * 0.16f, Vector3.up, 0.09f, 20);
            w.Circle(Vector3.down * 0.16f, Vector3.up, 0.05f, 16);
            return w.ToMesh("SU_HoloStation");
        }

        static Mesh ConvoyHolo()
        {
            var w = new Wire(0.008f);
            void Chevron(Vector3 c, float s)
            {
                var nose = c + Vector3.forward * s;
                var l = c + new Vector3(-0.6f * s, 0f, -0.5f * s);
                var rr = c + new Vector3(0.6f * s, 0f, -0.5f * s);
                var tail = c + Vector3.back * 0.2f * s;
                w.Line(l, nose, Vector3.up);
                w.Line(nose, rr, Vector3.up);
                w.Line(rr, tail, Vector3.up);
                w.Line(tail, l, Vector3.up);
                w.Line(tail, c + Vector3.up * 0.25f * s, Vector3.forward);
            }

            Chevron(new Vector3(0f, 0.04f, 0.12f), 0.1f);
            Chevron(new Vector3(-0.14f, 0f, -0.04f), 0.075f);
            Chevron(new Vector3(0.14f, -0.02f, -0.06f), 0.075f);
            Chevron(new Vector3(0f, -0.05f, -0.2f), 0.06f);
            w.Circle(Vector3.down * 0.08f, Vector3.up, 0.32f, 48);
            w.Circle(Vector3.down * 0.08f, Vector3.up, 0.2f, 40);
            return w.ToMesh("SU_HoloConvoy");
        }

        static Mesh LatticeHolo()
        {
            var w = new Wire(0.007f);
            for (var k = 0; k < 3; k++)
                w.Circle(Vector3.zero, Quaternion.Euler(k * 60f, k * 40f, 0f) * Vector3.up, 0.27f, 44);
            var pts = new List<Vector3>();
            for (var k = 0; k < 8; k++)
            {
                var a = k * Mathf.PI / 4f;
                pts.Add(new Vector3(Mathf.Cos(a) * 0.16f, (k % 2 == 0 ? 0.08f : -0.08f), Mathf.Sin(a) * 0.16f));
            }

            for (var k = 0; k < pts.Count; k++)
            {
                w.Line(pts[k], pts[(k + 1) % pts.Count], Vector3.up);
                w.Line(pts[k], Vector3.zero, Vector3.up);
            }

            return w.ToMesh("SU_HoloLattice");
        }
    }
}
