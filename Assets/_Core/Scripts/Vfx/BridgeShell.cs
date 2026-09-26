using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The bridge's shell: an elongated octagon (aft wall with the three service doors, aft chamfers with the
    /// diplomacy and quarters doors, straight flanks, two raked bow facets with a window each, and the forward
    /// bulkhead that frames the main viewscreen). One wall profile is swept around the plan with mitred corners —
    /// floor kick, a proud dado with a lit rail, the wall, a cornice, a raked soffit, a ceiling ring and a lit cove
    /// riser up to a raised coffer with a sky panel over the table — so there is no right angle between wall and
    /// ceiling anywhere. Ribs stand at every corner and between the doors, and run on across the ceiling to the
    /// sky panel. A dozen meshes on shared SU/HullInterior materials (lit by <see cref="RoomLightRig"/>).
    /// Interior local metres, +z = forward.
    /// </summary>
    public static class BridgeShell
    {
        /// <summary>Plan, clockwise seen from above, from the aft-left corner. Edge i runs Plan[i] → Plan[i+1].</summary>
        public static readonly Vector2[] Plan =
        {
            new(-4.2f, -6f), new(4.2f, -6f), new(6f, -4.2f), new(6f, 1.6f),
            new(3.2f, 6f), new(-3.2f, 6f), new(-6f, 1.6f), new(-6f, -4.2f)
        };

        public const int AftWall = 0, AftStarboard = 1, Starboard = 2, BowStarboard = 3, Forward = 4, BowPort = 5,
            Port = 6, AftPort = 7;

        public const float WallTop = 2.9f;
        public const float SoffitY = 3.4f;
        public const float CofferY = 3.8f;
        const float CofferInset = 2.1f;

        // Main viewscreen aperture on the forward bulkhead (edge-local u from Plan[4], height).
        public const float ScreenWidth = 4.6f;
        public const float ScreenBottom = 1.2f;
        public const float ScreenTop = 2.8f;
        // Bow windows, centred on their facet.
        const float BowWidth = 3.2f;
        const float BowBottom = 1.0f;
        const float BowTop = 2.6f;
        const float Reveal = 0.4f;

        /// <summary>Wall profile (inset from the plan line, height), floor to coffer, with baked occlusion.</summary>
        static readonly Vector2[] Profile =
        {
            new(0.12f, 0f), new(0.045f, 0.12f), new(0.045f, 0.92f), new(0f, 0.98f), new(0f, WallTop),
            new(0.16f, 3.12f), new(0.95f, SoffitY), new(CofferInset, SoffitY), new(CofferInset, CofferY)
        };

        static readonly float[] ProfileAo = { 0.45f, 0.62f, 0.85f, 0.9f, 1f, 0.8f, 0.9f, 0.62f, 0.95f };
        const int WallBandStart = 3; // Profile[3] → Profile[4] is the plain wall, where openings are cut.

        /// <summary>Centre of the sky panel in the coffer (over the holo table).</summary>
        static readonly Vector2 SkyCentre = new(0f, 0.5f);

        public static Vector3 ScreenCentre => new(0f, (ScreenBottom + ScreenTop) * 0.5f, Plan[Forward].y);

        static Vector2[] _normals;
        static Vector2[] _miters;

        public static void Build(Transform room, CicArtKit art)
        {
            Prepare();
            var root = new GameObject("BridgeShell").transform;
            root.SetParent(room, false);

            var wall = art.Hull(art.Panel, new Color(0.55f, 0.61f, 0.69f), new Vector2(1.2f, 0.64f));
            var dado = art.Hull(art.Panel, new Color(0.26f, 0.3f, 0.35f), new Vector2(0.6f, 0.4f), seam: 0.55f);
            var ceiling = art.Hull(art.Wall, new Color(0.46f, 0.52f, 0.6f), new Vector2(1.4f, 0.7f));
            var coffer = art.Hull(art.Panel, new Color(0.28f, 0.32f, 0.38f), new Vector2(1.1f, 1.1f), seam: 0.6f);
            var rib = art.Hull(art.Panel, new Color(0.62f, 0.68f, 0.76f), new Vector2(40f, 0.5f), seam: 0.3f, lift: 0.08f);
            var frame = art.Hull(art.Panel, new Color(0.42f, 0.47f, 0.54f), new Vector2(40f, 40f), seam: 0f, lift: 0.06f);
            var deck = art.Hull(art.DeckRib != null ? art.DeckRib : art.Floor, new Color(0.6f, 0.66f, 0.74f), new Vector2(1.5f, 1.5f),
                tiling: 0.42f, seam: 0.35f);

            // ── Swept profile ────────────────────────────────────────────────────
            var m = new ShellMesh();
            Sweep(m, 0, 3);
            Part(root, "Dado", m.ToMesh("SU_BridgeDado"), dado, collider: true);

            m = new ShellMesh();
            for (var e = 0; e < Plan.Length; e++)
                WallBand(m, e, Openings(e));
            Part(root, "Walls", m.ToMesh("SU_BridgeWalls"), wall, collider: true);

            m = new ShellMesh();
            Sweep(m, 4, 7);
            Part(root, "Soffit", m.ToMesh("SU_BridgeSoffit"), ceiling, collider: false);

            m = new ShellMesh();
            Sweep(m, 7, 8);
            Cap(m);
            Part(root, "Coffer", m.ToMesh("SU_BridgeCoffer"), coffer, collider: false);

            m = new ShellMesh();
            Floor(m);
            Part(root, "Deck", m.ToMesh("SU_BridgeDeck"), deck, collider: true);

            // ── Light lines: kick, dado rail, cornice, cove riser, the sky panel ─
            m = new ShellMesh();
            SweepBand(m, new Vector2(0.105f, 0.03f), new Vector2(0.075f, 0.075f));
            Part(root, "KickStrip", m.ToMesh("SU_BridgeKick"), art.CyanEmit(0.9f), collider: false);

            m = new ShellMesh();
            SweepBand(m, new Vector2(0.05f, 0.9f), new Vector2(0.018f, 0.94f));
            SweepBand(m, new Vector2(CofferInset + 0.012f, SoffitY + 0.13f), new Vector2(CofferInset + 0.012f, SoffitY + 0.18f));
            Part(root, "CoveStrip", m.ToMesh("SU_BridgeCove"), art.CyanEmit(2.2f), collider: false);

            m = new ShellMesh();
            SweepBand(m, new Vector2(0.012f, WallTop - 0.05f), new Vector2(0.012f, WallTop - 0.015f));
            Part(root, "CorniceGlow", m.ToMesh("SU_BridgeCornice"), art.AmberEmit(1.7f), collider: false);

            m = new ShellMesh();
            SkyPanel(m);
            Part(root, "SkyPanel", m.ToMesh("SU_BridgeSky"), art.Lit(Texture2D.whiteTexture, new Color(0.7f, 0.88f, 1f), 1.25f),
                collider: false);

            // ── Ribs: corners, between doors, mid-flank, on across the ceiling ──
            m = new ShellMesh();
            for (var k = 0; k < Plan.Length; k++)
                CornerRib(m, k);
            foreach (var (e, u) in new[] { (AftWall, 2.85f), (AftWall, 5.55f), (Starboard, 2.9f), (Port, 2.9f) })
                EdgeRib(m, e, u);
            for (var k = 0; k < Plan.Length; k++)
                CeilingBeam(m, k);
            Part(root, "Ribs", m.ToMesh("SU_BridgeRibs"), rib, collider: false);

            // ── Viewscreen and bow window frames ─────────────────────────────────
            m = new ShellMesh();
            var glow = new ShellMesh();
            var fwd = Plan[Forward];
            var len = EdgeLength(Forward);
            var s0 = len * 0.5f - ScreenWidth * 0.5f;
            var s1 = len * 0.5f + ScreenWidth * 0.5f;
            // The viewscreen aperture only gets a plain reveal here: the screen brings its own housing
            // (BridgeViewscreen), so it never reads as one more window.
            var rect = Chamfered(s0, s1, ScreenBottom, ScreenTop, 0.001f);
            FrameRing(m, Forward, rect, rect, 0.001f, Reveal);
            foreach (var e in new[] { BowStarboard, BowPort })
            {
                var l = EdgeLength(e);
                var w0 = l * 0.5f - BowWidth * 0.5f;
                var w1 = l * 0.5f + BowWidth * 0.5f;
                FrameRing(m, e, Chamfered(w0 - 0.24f, w1 + 0.24f, BowBottom - 0.2f, BowTop + 0.14f, 0.34f),
                    Chamfered(w0, w1, BowBottom, BowTop, 0.18f), 0.12f, Reveal);
                FrameRing(glow, e, Chamfered(w0 - 0.04f, w1 + 0.04f, BowBottom - 0.04f, BowTop + 0.04f, 0.21f),
                    Chamfered(w0 - 0.015f, w1 + 0.015f, BowBottom - 0.015f, BowTop + 0.015f, 0.19f), 0.125f, 0f);
                for (var i = 1; i <= 2; i++)
                    Mullion(m, e, Mathf.Lerp(w0, w1, i / 3f), BowBottom, BowTop);
                // A deep sill ledge to lean on.
                Ledge(m, e, w0 - 0.1f, w1 + 0.1f, BowBottom - 0.2f, 0.3f);
            }

            Part(root, "Frames", m.ToMesh("SU_BridgeFrames"), frame, collider: false);

            // Solid hull behind every wall (windows and screen included): 0.6 m boxes stop a character body far more
            // reliably than a one-sided mesh, and room-scale steps cannot slip between them at the mitres.
            for (var e = 0; e < Plan.Length; e++)
            {
                var l = EdgeLength(e);
                var wallBox = new GameObject("HullCollider");
                wallBox.transform.SetParent(root, false);
                wallBox.transform.localPosition = EdgePoint(e, l * 0.5f, -0.3f, 1.8f);
                wallBox.transform.localRotation = Quaternion.LookRotation(EdgeNormal(e), Vector3.up);
                wallBox.AddComponent<BoxCollider>().size = new Vector3(l + 1.2f, 3.8f, 0.6f);
            }
            Part(root, "FrameStrip", glow.ToMesh("SU_BridgeFrameGlow"), art.CyanEmit(3f), collider: false);
            _ = fwd;
        }

        // ── Plan geometry ────────────────────────────────────────────────────────

        static void Prepare()
        {
            if (_normals != null)
                return;
            var c = Plan.Length;
            _normals = new Vector2[c];
            _miters = new Vector2[c];
            for (var e = 0; e < c; e++)
            {
                var d = (Plan[(e + 1) % c] - Plan[e]).normalized;
                _normals[e] = new Vector2(-d.y, d.x);
            }

            for (var k = 0; k < c; k++)
            {
                var a = _normals[(k + c - 1) % c];
                var b = _normals[k];
                _miters[k] = (a + b) / (1f + Vector2.Dot(a, b));
            }
        }

        public static float EdgeLength(int e) => Vector2.Distance(Plan[e], Plan[(e + 1) % Plan.Length]);

        /// <summary>Inward normal of edge e (x, z).</summary>
        public static Vector3 EdgeNormal(int e)
        {
            Prepare();
            return new Vector3(_normals[e].x, 0f, _normals[e].y);
        }

        public static Vector3 EdgeDir(int e)
        {
            var d = (Plan[(e + 1) % Plan.Length] - Plan[e]).normalized;
            return new Vector3(d.x, 0f, d.y);
        }

        /// <summary>A point on edge e: u metres from its start, <paramref name="inset"/> into the room, at height y.</summary>
        public static Vector3 EdgePoint(int e, float u, float inset, float y)
        {
            Prepare();
            var a = Plan[e];
            var p = a + (Plan[(e + 1) % Plan.Length] - a).normalized * u + _normals[e] * inset;
            return new Vector3(p.x, y, p.y);
        }

        /// <summary>Yaw that turns local +z to face into the room from edge e.</summary>
        public static float EdgeYaw(int e)
        {
            var n = EdgeNormal(e);
            return Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
        }

        static Vector3 Corner(int k, float inset, float y)
        {
            var p = Plan[k % Plan.Length] + _miters[k % Plan.Length] * inset;
            return new Vector3(p.x, y, p.y);
        }

        static float U(int e, Vector3 p)
        {
            var a = Plan[e];
            var d = (Plan[(e + 1) % Plan.Length] - a).normalized;
            return (p.x - a.x) * d.x + (p.z - a.y) * d.y;
        }

        static Vector3 ProfileNormal(int e, Vector2 from, Vector2 to)
        {
            var s = (to - from).normalized;
            var n = EdgeNormal(e);
            return (n * s.y + Vector3.up * -s.x).normalized;
        }

        // ── Sweeps ───────────────────────────────────────────────────────────────

        /// <summary>Profile points i0..i1 swept around the whole plan, mitred at the corners.</summary>
        static void Sweep(ShellMesh m, int i0, int i1)
        {
            var along = new float[Profile.Length];
            for (var i = 1; i < Profile.Length; i++)
                along[i] = along[i - 1] + Vector2.Distance(Profile[i - 1], Profile[i]);
            for (var e = 0; e < Plan.Length; e++)
            for (var i = i0; i < i1; i++)
            {
                var p0 = Profile[i];
                var p1 = Profile[i + 1];
                var a = Corner(e, p0.x, p0.y);
                var b = Corner(e + 1, p0.x, p0.y);
                var c = Corner(e + 1, p1.x, p1.y);
                var d = Corner(e, p1.x, p1.y);
                m.Quad(a, b, c, d, ProfileNormal(e, p0, p1),
                    new Vector2(U(e, a), along[i]), new Vector2(U(e, b), along[i]),
                    new Vector2(U(e, c), along[i + 1]), new Vector2(U(e, d), along[i + 1]),
                    ProfileAo[i], ProfileAo[i], ProfileAo[i + 1], ProfileAo[i + 1]);
            }
        }

        /// <summary>A thin two-point band swept around the plan (light lines).</summary>
        static void SweepBand(ShellMesh m, Vector2 p0, Vector2 p1)
        {
            for (var e = 0; e < Plan.Length; e++)
            {
                var a = Corner(e, p0.x, p0.y);
                var b = Corner(e + 1, p0.x, p0.y);
                var c = Corner(e + 1, p1.x, p1.y);
                var d = Corner(e, p1.x, p1.y);
                m.Quad(a, b, c, d, ProfileNormal(e, p0, p1), Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            }
        }

        struct Opening
        {
            public float U0, U1, Y0, Y1;
        }

        static List<Opening> Openings(int e)
        {
            var list = new List<Opening>();
            var l = EdgeLength(e);
            if (e == Forward)
                list.Add(new Opening { U0 = l * 0.5f - ScreenWidth * 0.5f, U1 = l * 0.5f + ScreenWidth * 0.5f, Y0 = ScreenBottom, Y1 = ScreenTop });
            else if (e is BowStarboard or BowPort)
                list.Add(new Opening { U0 = l * 0.5f - BowWidth * 0.5f, U1 = l * 0.5f + BowWidth * 0.5f, Y0 = BowBottom, Y1 = BowTop });
            return list;
        }

        /// <summary>The plain wall of edge e (profile band 3→4) with its openings cut out.</summary>
        static void WallBand(ShellMesh m, int e, List<Opening> holes)
        {
            var y0 = Profile[WallBandStart].y;
            var y1 = Profile[WallBandStart + 1].y;
            var l = EdgeLength(e);
            var n = EdgeNormal(e);
            var ao0 = ProfileAo[WallBandStart];
            var ao1 = ProfileAo[WallBandStart + 1];
            var v0 = 0f;
            for (var i = 1; i <= WallBandStart; i++)
                v0 += Vector2.Distance(Profile[i - 1], Profile[i]);

            void Rect(float u0, float u1, float ya, float yb)
            {
                if (u1 - u0 < 1e-3f || yb - ya < 1e-3f)
                    return;
                float Ao(float y) => Mathf.Lerp(ao0, ao1, (y - y0) / (y1 - y0));
                m.Quad(EdgePoint(e, u0, 0f, ya), EdgePoint(e, u1, 0f, ya), EdgePoint(e, u1, 0f, yb), EdgePoint(e, u0, 0f, yb), n,
                    new Vector2(u0, v0 + ya - y0), new Vector2(u1, v0 + ya - y0), new Vector2(u1, v0 + yb - y0), new Vector2(u0, v0 + yb - y0),
                    Ao(ya), Ao(ya), Ao(yb), Ao(yb));
            }

            // The corner ends of the band follow the mitre (inset 0 there, so the plan corner itself).
            var at = 0f;
            holes.Sort((a, b) => a.U0.CompareTo(b.U0));
            foreach (var h in holes)
            {
                Rect(at, h.U0, y0, y1);
                Rect(h.U0, h.U1, y0, h.Y0);
                Rect(h.U0, h.U1, h.Y1, y1);
                at = h.U1;
            }

            Rect(at, l, y0, y1);
        }

        static void Cap(ShellMesh m)
        {
            var c = new Vector3(SkyCentre.x, CofferY, SkyCentre.y);
            for (var k = 0; k < Plan.Length; k++)
            {
                var a = Corner(k, CofferInset, CofferY);
                var b = Corner(k + 1, CofferInset, CofferY);
                m.Tri(c, a, b, Vector3.down, new Vector2(c.x, c.z), new Vector2(a.x, a.z), new Vector2(b.x, b.z), 1f, 0.8f, 0.8f);
            }
        }

        static void SkyPanel(ShellMesh m)
        {
            var c = new Vector3(SkyCentre.x, CofferY - 0.012f, SkyCentre.y);
            for (var k = 0; k < Plan.Length; k++)
            {
                var a = Vector3.Lerp(c, Corner(k, CofferInset, CofferY - 0.012f), 0.34f);
                var b = Vector3.Lerp(c, Corner(k + 1, CofferInset, CofferY - 0.012f), 0.34f);
                m.Tri(c, a, b, Vector3.down, Vector2.zero, Vector2.zero, Vector2.zero, 1f, 1f, 1f);
            }
        }

        static void Floor(ShellMesh m)
        {
            var c = Vector3.zero;
            foreach (var p in Plan)
                c += new Vector3(p.x, 0f, p.y);
            c /= Plan.Length;
            for (var k = 0; k < Plan.Length; k++)
            {
                var a = Corner(k, Profile[0].x, 0f);
                var b = Corner(k + 1, Profile[0].x, 0f);
                m.Tri(c, a, b, Vector3.up, new Vector2(c.x, c.z), new Vector2(a.x, a.z), new Vector2(b.x, b.z), 1f, 0.7f, 0.7f);
            }
        }

        // ── Ribs ─────────────────────────────────────────────────────────────────

        static readonly Vector2[] RibPath =
        {
            new(0f, 0f), new(0f, WallTop), new(0.16f, 3.12f), new(0.95f, SoffitY), new(CofferInset, SoffitY)
        };

        /// <summary>A rib swept up the profile at plan corner k (in the mitre).</summary>
        static void CornerRib(ShellMesh m, int k)
        {
            var miter = _miters[k];
            var dir = new Vector3(miter.x, 0f, miter.y).normalized;
            var side = Vector3.Cross(Vector3.up, dir).normalized;
            var pts = new List<Vector3>();
            var ins = new List<Vector3>();
            for (var i = 0; i < RibPath.Length; i++)
            {
                pts.Add(Corner(k, RibPath[i].x, RibPath[i].y));
                ins.Add(PathNormal(RibPath, i, dir));
            }

            SweepRib(m, pts, ins, side, 0.11f, 0.1f);
        }

        static void EdgeRib(ShellMesh m, int e, float u)
        {
            var n = EdgeNormal(e);
            var pts = new List<Vector3>();
            var ins = new List<Vector3>();
            for (var i = 0; i < RibPath.Length; i++)
            {
                pts.Add(EdgePoint(e, u, RibPath[i].x, RibPath[i].y));
                ins.Add(PathNormal(RibPath, i, n));
            }

            SweepRib(m, pts, ins, EdgeDir(e), 0.08f, 0.08f);
        }

        /// <summary>Across the coffer: from the corner of the riser to the sky panel, under the cap.</summary>
        static void CeilingBeam(ShellMesh m, int k)
        {
            var c = new Vector3(SkyCentre.x, CofferY, SkyCentre.y);
            var a = Corner(k, CofferInset, CofferY);
            var b = Vector3.Lerp(c, a, 0.36f);
            var side = Vector3.Cross(Vector3.up, (b - a).normalized).normalized;
            SweepRib(m, new List<Vector3> { a, b }, new List<Vector3> { Vector3.down, Vector3.down }, side, 0.07f, 0.09f);
        }

        /// <summary>Profile-space normal at point i of a (inset, height) path, in 3D with inward = <paramref name="inward"/>.</summary>
        static Vector3 PathNormal(Vector2[] path, int i, Vector3 inward)
        {
            var e0 = i > 0 ? path[i] - path[i - 1] : path[1] - path[0];
            var e1 = i < path.Length - 1 ? path[i + 1] - path[i] : e0;
            var s = (e0.normalized + e1.normalized).normalized;
            return (inward * s.y + Vector3.up * -s.x).normalized;
        }

        static void SweepRib(ShellMesh m, List<Vector3> pts, List<Vector3> ins, Vector3 side, float halfWidth, float depth)
        {
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                var fa = a + ins[i] * depth;
                var fb = b + ins[i + 1] * depth;
                var vA = i * 0.5f;
                var vB = (i + 1) * 0.5f;
                // Front face, then both sides back to the shell.
                m.Quad(fa - side * halfWidth, fa + side * halfWidth, fb + side * halfWidth, fb - side * halfWidth, ins[i],
                    new Vector2(0f, vA), new Vector2(1f, vA), new Vector2(1f, vB), new Vector2(0f, vB), 1f, 1f, 1f, 1f);
                m.Quad(a + side * halfWidth, fa + side * halfWidth, fb + side * halfWidth, b + side * halfWidth, side,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.6f, 0.9f, 0.9f, 0.6f);
                m.Quad(a - side * halfWidth, fa - side * halfWidth, fb - side * halfWidth, b - side * halfWidth, -side,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.6f, 0.9f, 0.9f, 0.6f);
            }
        }

        // ── Frames ───────────────────────────────────────────────────────────────

        /// <summary>A chamfered rectangle in edge space (u, y), clockwise seen from the room.</summary>
        public static Vector2[] Chamfered(float u0, float u1, float y0, float y1, float c) => new[]
        {
            new Vector2(u0 + c, y0), new Vector2(u1 - c, y0), new Vector2(u1, y0 + c), new Vector2(u1, y1 - c),
            new Vector2(u1 - c, y1), new Vector2(u0 + c, y1), new Vector2(u0, y1 - c), new Vector2(u0, y0 + c)
        };

        /// <summary>
        /// A frame between two contours on edge e: front face at <paramref name="front"/> into the room, the outer
        /// side back to the wall, the inner side on through the wall by <paramref name="reveal"/> (lines an opening).
        /// </summary>
        static void FrameRing(ShellMesh m, int e, Vector2[] outer, Vector2[] inner, float front, float reveal)
        {
            var n = EdgeNormal(e);
            var cu = 0f;
            var cy = 0f;
            foreach (var p in inner)
            {
                cu += p.x;
                cy += p.y;
            }

            var centre = EdgePoint(e, cu / inner.Length, 0f, cy / inner.Length);
            var count = outer.Length;
            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % count;
                var oa = EdgePoint(e, outer[i].x, front, outer[i].y);
                var ob = EdgePoint(e, outer[j].x, front, outer[j].y);
                var ia = EdgePoint(e, inner[i].x, front, inner[i].y);
                var ib = EdgePoint(e, inner[j].x, front, inner[j].y);
                m.Quad(oa, ob, ib, ia, n, outer[i], outer[j], inner[j], inner[i], 1f, 1f, 0.9f, 0.9f);

                var wa = EdgePoint(e, outer[i].x, 0f, outer[i].y);
                var wb = EdgePoint(e, outer[j].x, 0f, outer[j].y);
                var mid = (oa + ob) * 0.5f;
                var outward = mid - centre - n * Vector3.Dot(mid - centre, n);
                m.Quad(wa, wb, ob, oa, outward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.55f, 0.55f, 0.9f, 0.9f);

                if (reveal <= 0f)
                    continue;
                var ra = EdgePoint(e, inner[i].x, -reveal, inner[i].y);
                var rb = EdgePoint(e, inner[j].x, -reveal, inner[j].y);
                var imid = (ia + ib) * 0.5f;
                var toward = centre - imid - n * Vector3.Dot(centre - imid, n);
                m.Quad(ia, ib, rb, ra, toward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.9f, 0.4f, 0.4f);
            }
        }

        static void Mullion(ShellMesh m, int e, float u, float y0, float y1)
        {
            const float hw = 0.045f;
            var n = EdgeNormal(e);
            var d = EdgeDir(e);
            Vector3 P(float du, float inset, float y) => EdgePoint(e, u + du, inset, y);
            m.Quad(P(-hw, 0.05f, y0), P(hw, 0.05f, y0), P(hw, 0.05f, y1), P(-hw, 0.05f, y1), n,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            m.Quad(P(hw, 0.05f, y0), P(hw, -Reveal, y0), P(hw, -Reveal, y1), P(hw, 0.05f, y1), d,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.5f, 0.5f, 0.9f);
            m.Quad(P(-hw, 0.05f, y0), P(-hw, -Reveal, y0), P(-hw, -Reveal, y1), P(-hw, 0.05f, y1), -d,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.5f, 0.5f, 0.9f);
        }

        /// <summary>A sill shelf proud of the wall on edge e, top at y, <paramref name="depth"/> into the room.</summary>
        static void Ledge(ShellMesh m, int e, float u0, float u1, float y, float depth)
        {
            var n = EdgeNormal(e);
            var d = EdgeDir(e);
            m.Quad(EdgePoint(e, u0, 0f, y), EdgePoint(e, u1, 0f, y), EdgePoint(e, u1, depth, y), EdgePoint(e, u0, depth, y), Vector3.up,
                new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, depth), new Vector2(u0, depth), 0.8f, 0.8f, 1f, 1f);
            m.Quad(EdgePoint(e, u0, depth, y), EdgePoint(e, u1, depth, y), EdgePoint(e, u1, depth - 0.06f, y - 0.1f),
                EdgePoint(e, u0, depth - 0.06f, y - 0.1f), n + Vector3.down * 0.5f,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 0.7f, 0.7f);
            m.Quad(EdgePoint(e, u0, depth - 0.06f, y - 0.1f), EdgePoint(e, u1, depth - 0.06f, y - 0.1f), EdgePoint(e, u1, 0f, y - 0.16f),
                EdgePoint(e, u0, 0f, y - 0.16f), Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.7f, 0.7f, 0.5f, 0.5f);
            foreach (var (u, s) in new[] { (u0, -d), (u1, d) })
                m.Quad(EdgePoint(e, u, 0f, y), EdgePoint(e, u, depth, y), EdgePoint(e, u, depth - 0.06f, y - 0.1f), EdgePoint(e, u, 0f, y - 0.16f), s,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 0.8f, 0.7f, 0.6f);
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
            return r;
        }
    }

    /// <summary>Flat-shaded mesh accumulator: quads / triangles wound to face a hint, UVs in metres, AO in vertex colour.</summary>
    public sealed class ShellMesh
    {
        readonly List<Vector3> _v = new();
        readonly List<Vector3> _n = new();
        readonly List<Vector2> _uv = new();
        readonly List<Color> _c = new();
        readonly List<int> _t = new();

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
            float aa, float ab, float ac, float ad)
        {
            var face = Vector3.Cross(b - a, d - a);
            if (face.sqrMagnitude < 1e-10f)
                face = Vector3.Cross(c - b, a - b);
            var flip = Vector3.Dot(face, hint) < 0f;
            var n = (flip ? -face : face).normalized;
            var i = _v.Count;
            _v.AddRange(new[] { a, b, c, d });
            _n.AddRange(new[] { n, n, n, n });
            _uv.AddRange(new[] { ua, ub, uc, ud });
            _c.AddRange(new[] { new Color(aa, aa, aa, 1f), new Color(ab, ab, ab, 1f), new Color(ac, ac, ac, 1f), new Color(ad, ad, ad, 1f) });
            if (flip)
                _t.AddRange(new[] { i, i + 3, i + 2, i, i + 2, i + 1 });
            else
                _t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 hint, Vector2 ua, Vector2 ub, Vector2 uc, float aa, float ab, float ac)
        {
            var face = Vector3.Cross(b - a, c - a);
            var flip = Vector3.Dot(face, hint) < 0f;
            var n = (flip ? -face : face).normalized;
            var i = _v.Count;
            _v.AddRange(new[] { a, b, c });
            _n.AddRange(new[] { n, n, n });
            _uv.AddRange(new[] { ua, ub, uc });
            _c.AddRange(new[] { new Color(aa, aa, aa, 1f), new Color(ab, ab, ab, 1f), new Color(ac, ac, ac, 1f) });
            _t.AddRange(flip ? new[] { i, i + 2, i + 1 } : new[] { i, i + 1, i + 2 });
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (_v.Count > 65000)
                m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(_v);
            m.SetNormals(_n);
            m.SetUVs(0, _uv);
            m.SetColors(_c);
            m.SetTriangles(_t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
