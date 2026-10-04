using System.Collections.Generic;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// A city's command room — the citadel's Throne Hall, its own place, not a bridge: a basilica crowning the
    /// tower. A broad nave (18 m between the outer walls, 16 m from the narthex to the apse) under a pointed
    /// barrel vault painted with a night sky; arcades on columns open on two aisles lit by tall lancets on the
    /// city, tribunes above them, a clerestory over all; a semicircular apse ahead where the main screen stands
    /// as a gilt retable under five lancets and a half-dome; a rose window over the narthex door behind. The
    /// commander sits a throne under a baldachin (the arm consoles on its arms) facing the map floor — the holo
    /// table rising from a lapis disc with a gold star chart — and the five ministers work at council stalls
    /// facing each other across the nave, as a parliament sits. Gold chandeliers, a turning astrolabe, crimson
    /// banners, guardian statues at the door, braziers in the apse. Interior local metres (+z = forward, the
    /// table at <see cref="WorldScale.CicTableCenterZ"/>); static geometry merged per material; built hidden and
    /// swapped in by <see cref="BridgeDressing"/> in a city (<see cref="Core.App.ViewMode.City"/>).
    /// </summary>
    public static class CitadelHall
    {
        // ── Plan and heights ─────────────────────────────────────────────────────

        /// <summary>Arcade line (nave walls) and outer walls (aisles), |x|.</summary>
        public const float NaveX = 7.0f;
        public const float WallX = 9.2f;
        /// <summary>Narthex wall (the door) and the apse's chord (the screen plane), z.</summary>
        public const float NarthexZ = -10.2f;
        public const float ApseZ = 6.0f;
        public const float ApseR = 7.0f;
        /// <summary>Aisle ceiling = tribune floor; tribune roof; nave wall top; vault apex.</summary>
        const float TribuneY = 5.2f;
        const float TribuneTop = 7.6f;
        const float NaveTop = 10.8f;
        const float VaultTop = 13.2f;
        /// <summary>Columns down the nave (z), between them the arcades' bays.</summary>
        public static readonly float[] Piers = { -10.2f, -7.5f, -4.8f, -2.1f, 0.6f, 3.3f, 6.0f };
        /// <summary>The commander's throne: the ship chair's spot (its seat, arm consoles and stand-up key are kept).</summary>
        public static Vector3 Throne => new(0f, 0f, WorldScale.CicCaptainChairZ);
        /// <summary>Where one steps in from the corridor (inside the narthex door).</summary>
        public static Vector3 DoorStep => new(0f, 0f, NarthexZ + 1.2f);
        public static Vector3 DoorPose => new(0f, 0f, NarthexZ + 0.12f);

        public static readonly Color Gold = new(1f, 0.78f, 0.42f, 1f);
        public static readonly Color Crimson = new(0.62f, 0.07f, 0.1f, 1f);

        /// <summary>Council stalls: the ministers face each other across the nave (x side, z), Helm's empty in a city.</summary>
        static readonly (string name, float x, float z)[] Stalls =
        {
            ("CrewTactical", 5.6f, 2.9f), ("CrewEngineering", 5.6f, 0.6f), ("CrewOps", 5.6f, -1.7f),
            ("CrewHelm", -5.6f, 2.9f), ("CrewScience", -5.6f, 0.6f), ("CrewComms", -5.6f, -1.7f)
        };

        /// <summary>A crew post's pose in the hall: at its stall, facing across the nave.</summary>
        public static bool PostPose(string name, out Vector3 pos, out Quaternion rot)
        {
            foreach (var (n, x, z) in Stalls)
                if (n == name)
                {
                    pos = new Vector3(x, 0f, z);
                    rot = Quaternion.LookRotation(new Vector3(-Mathf.Sign(x), 0f, 0f), Vector3.up);
                    return true;
                }

            pos = default;
            rot = Quaternion.identity;
            return false;
        }

        /// <summary>The deck hands' round: the aisles, by the windows and the narthex.</summary>
        public static (Vector3 at, Vector3 facing, bool post)[] FloorSpots() => new[]
        {
            // Down the port aisle, out through the first arcade, across the narthex (clear of the guardians'
            // plinths), back up the starboard aisle.
            (new Vector3(-8.3f, 0f, 3.2f), Vector3.left, true), (new Vector3(-8.3f, 0f, -1.4f), Vector3.left, true),
            (new Vector3(-8.3f, 0f, -8.9f), Vector3.left, true), (new Vector3(-2.6f, 0f, -8.2f), Vector3.back, false),
            (new Vector3(2.6f, 0f, -8.2f), Vector3.back, false), (new Vector3(8.3f, 0f, -8.9f), Vector3.right, true),
            (new Vector3(8.3f, 0f, -1.4f), Vector3.right, true), (new Vector3(8.3f, 0f, 3.2f), Vector3.right, true)
        };

        /// <summary>
        /// The port tribune's walk (over the aisle), looking down into the nave or out of the windows — one side
        /// only: nothing crosses the nave up there.
        /// </summary>
        public static (Vector3 at, Vector3 facing, bool post)[] TribuneSpots()
        {
            var list = new List<(Vector3, Vector3, bool)>();
            for (var i = 0; i < 5; i++)
                list.Add((new Vector3(-8.5f, TribuneY, -8.0f + i * 3.0f), new Vector3(i % 2 == 0 ? 1f : -1f, 0f, 0f), i % 2 == 0));
            return list.ToArray();
        }

        /// <summary>Two ministers' aides in the aisles (the rotunda's tier operators), facing the lancets.</summary>
        public static (Vector3 pos, Quaternion rot) AidePose(int i) => i == 0
            ? (new Vector3(-8.6f, 0f, 4.6f), Quaternion.LookRotation(Vector3.left))
            : (new Vector3(8.6f, 0f, 4.6f), Quaternion.LookRotation(Vector3.right));

        // ── Openings (pointed arches, a round rose) ──────────────────────────────

        struct Opening
        {
            public float U;
            public float Half;
            public float Sill;
            public float Spring;
            public float Apex;
            public bool Round;
            public float Y;
            public float Radius;

            public float HalfAt(float y)
            {
                if (Round)
                {
                    var dy = y - Y;
                    return Mathf.Abs(dy) >= Radius ? -1f : Mathf.Sqrt(Radius * Radius - dy * dy);
                }

                if (y < Sill || y > Apex)
                    return -1f;
                if (y <= Spring)
                    return Half;
                var t = (y - Spring) / (Apex - Spring);
                // Two arcs meeting in a point.
                return Half * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(t, 1.7f)), 0.62f);
            }
        }

        static Opening Lancet(float u, float half, float sill, float spring) =>
            new() { U = u, Half = half, Sill = sill, Spring = spring, Apex = spring + half * 1.7f };

        static Opening Rose(float u, float y, float r) => new() { U = u, Round = true, Y = y, Radius = r };

        /// <summary>
        /// A flat wall from <paramref name="origin"/> along <paramref name="along"/> (u 0 → length), up to
        /// <paramref name="top"/>(u), its face toward <paramref name="face"/>; solid except the openings. Built in
        /// vertical strips merged where solid; reveals and gold tracery round each opening.
        /// </summary>
        static void FlatWall(LatheMesh wall, LatheMesh reveal, LatheMesh gold, Vector3 origin, Vector3 along, Vector3 face, float length,
            System.Func<float, float> top, List<Opening> ops, float depth = 0.45f)
        {
            Vector3 P(float u, float y, float d = 0f) => origin + along * u + Vector3.up * y - face * d;
            const float du = 0.1f;
            const float dy = 0.1f;
            for (var u0 = 0f; u0 < length - 1e-3f; u0 += du)
            {
                var u1 = Mathf.Min(length, u0 + du);
                var uc = (u0 + u1) * 0.5f;
                var h = top(uc);
                var runStart = -1f;
                for (var y0 = 0f; y0 < h - 1e-3f; y0 += dy)
                {
                    var y1 = Mathf.Min(h, y0 + dy);
                    var yc = (y0 + y1) * 0.5f;
                    var open = false;
                    foreach (var o in ops)
                    {
                        var w = o.HalfAt(yc);
                        if (w > 0f && Mathf.Abs(uc - o.U) < w)
                        {
                            open = true;
                            break;
                        }
                    }

                    if (!open && runStart < 0f)
                        runStart = y0;
                    if (open && runStart >= 0f)
                    {
                        wall.Quad(P(u0, runStart), P(u1, runStart), P(u1, y0), P(u0, y0), face, 1f);
                        runStart = -1f;
                    }
                }

                if (runStart >= 0f)
                    wall.Quad(P(u0, runStart), P(u1, runStart), P(u1, h), P(u0, h), face, 1f);
            }

            foreach (var o in ops)
            {
                var y0 = o.Round ? o.Y - o.Radius : o.Sill;
                var y1 = o.Round ? o.Y + o.Radius : o.Apex;
                var py = y0;
                var pw = Mathf.Max(0f, o.HalfAt(y0 + 0.001f));
                for (var y = y0 + 0.08f; y <= y1 + 0.001f; y += 0.08f)
                {
                    var w = Mathf.Max(0f, o.HalfAt(Mathf.Min(y, y1 - 0.001f)));
                    foreach (var s in new[] { -1f, 1f })
                    {
                        reveal.Quad(P(o.U + s * pw, py), P(o.U + s * pw, py, depth), P(o.U + s * w, y, depth), P(o.U + s * w, y), -s * along, 0.75f);
                        gold.Quad(P(o.U + s * pw, py, -0.004f), P(o.U + s * (pw + 0.05f), py, -0.004f), P(o.U + s * (w + 0.05f), y, -0.004f),
                            P(o.U + s * w, y, -0.004f), face, 1f);
                    }

                    py = y;
                    pw = w;
                }

                if (!o.Round && o.Sill > 0.05f)
                {
                    reveal.Quad(P(o.U - o.Half, o.Sill), P(o.U + o.Half, o.Sill), P(o.U + o.Half, o.Sill, depth), P(o.U - o.Half, o.Sill, depth), Vector3.up, 0.85f);
                    // Tracery: a mullion and a transom set back in the reveal.
                    if (o.Half > 0.35f)
                    {
                        gold.Quad(P(o.U - 0.02f, o.Sill, depth * 0.6f), P(o.U + 0.02f, o.Sill, depth * 0.6f), P(o.U + 0.02f, o.Apex - 0.05f, depth * 0.6f),
                            P(o.U - 0.02f, o.Apex - 0.05f, depth * 0.6f), face, 1f);
                        gold.Quad(P(o.U - o.Half, o.Spring - 0.02f, depth * 0.6f), P(o.U + o.Half, o.Spring - 0.02f, depth * 0.6f),
                            P(o.U + o.Half, o.Spring + 0.02f, depth * 0.6f), P(o.U - o.Half, o.Spring + 0.02f, depth * 0.6f), face, 1f);
                    }
                }

                if (o.Round)
                {
                    // The rose: twelve spokes and an inner ring.
                    for (var k = 0; k < 12; k++)
                    {
                        var a = k * Mathf.PI / 6f;
                        var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                        var n = new Vector2(d.y, -d.x) * 0.025f;
                        var a0 = d * 0.35f;
                        var a1 = d * (o.Radius - 0.02f);
                        gold.Quad(P(o.U + a0.x - n.x, o.Y + a0.y - n.y, depth * 0.5f), P(o.U + a0.x + n.x, o.Y + a0.y + n.y, depth * 0.5f),
                            P(o.U + a1.x + n.x, o.Y + a1.y + n.y, depth * 0.5f), P(o.U + a1.x - n.x, o.Y + a1.y - n.y, depth * 0.5f), face, 1f);
                    }

                    for (var k = 0; k < 24; k++)
                    {
                        var a0 = k * Mathf.PI / 12f;
                        var a1 = (k + 1) * Mathf.PI / 12f;
                        Vector3 R(float r, float a) => P(o.U + Mathf.Sin(a) * r, o.Y + Mathf.Cos(a) * r, depth * 0.5f);
                        gold.Quad(R(0.32f, a0), R(0.38f, a0), R(0.38f, a1), R(0.32f, a1), face, 1f);
                        gold.Quad(R(o.Radius * 0.62f, a0), R(o.Radius * 0.62f + 0.05f, a0), R(o.Radius * 0.62f + 0.05f, a1), R(o.Radius * 0.62f, a1), face, 1f);
                    }
                }
            }
        }

        // ── Pointed vault profile (nave and narthex gable) ────────────────────────

        /// <summary>The vault's height over |x| (nave wall top at NaveX, apex on the axis).</summary>
        static float VaultY(float x)
        {
            var t = Mathf.Clamp01(1f - Mathf.Abs(x) / NaveX);
            return NaveTop + (VaultTop - NaveTop) * Mathf.Pow(Mathf.Sin(t * Mathf.PI * 0.5f), 0.8f);
        }

        // ── Build ────────────────────────────────────────────────────────────────

        public static Transform Build(Transform room, CicArtKit art)
        {
            var root = new GameObject("CitadelShell").transform;
            root.SetParent(room, false);

            var stoneTex = StationSurfaces.Stone();
            var mats = new Mats
            {
                Stone = art.Hull(stoneTex, new Color(0.88f, 0.82f, 0.72f), new Vector2(1.2f, 0.6f), seam: 0.28f, lift: 0.1f),
                StoneDark = art.Hull(stoneTex, new Color(0.52f, 0.46f, 0.4f), new Vector2(0.8f, 0.5f), seam: 0.35f, lift: 0.08f),
                Marble = art.Hull(stoneTex, new Color(0.93f, 0.9f, 0.84f), new Vector2(1f, 1f), tiling: 0.7f, seam: 0.45f, lift: 0.1f),
                MarbleDark = art.Hull(stoneTex, new Color(0.34f, 0.3f, 0.32f), new Vector2(1f, 1f), tiling: 0.7f, seam: 0.45f, lift: 0.08f),
                Lapis = art.Hull(stoneTex, new Color(0.12f, 0.18f, 0.42f), new Vector2(0.9f, 0.9f), tiling: 0.8f, seam: 0.4f, lift: 0.08f),
                Wood = art.Hull(StationSurfaces.Planks(), new Color(0.42f, 0.24f, 0.14f), new Vector2(40f, 40f), tiling: 0.6f, seam: 0f, lift: 0.1f),
                Bronze = art.Hull(StationSurfaces.Panel(), new Color(0.5f, 0.35f, 0.2f), new Vector2(40f, 40f), seam: 0f, lift: 0.12f),
                Velvet = art.Hull(StationSurfaces.Panel(), new Color(0.5f, 0.06f, 0.09f), new Vector2(40f, 40f), seam: 0f, lift: 0.12f),
                Gold = art.Hull(StationSurfaces.Panel(), new Color(1f, 0.74f, 0.36f), new Vector2(40f, 40f), seam: 0f, lift: 0.32f),
                GoldLeaf = art.Lit(Texture2D.whiteTexture, Gold * 0.8f, 0.85f),
                Crimson = art.Lit(Texture2D.whiteTexture, Crimson, 0.55f),
                Light = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.9f, 0.7f), 2.8f),
                Flame = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.6f, 0.24f), 3.4f),
                Sky = art.Lit(NightSky(), Color.white, 0f),
                Leaf = art.Lit(Texture2D.whiteTexture, new Color(0.25f, 0.55f, 0.3f), 0.6f)
            };

            BuildFloors(root, mats);
            BuildWalls(root, mats);
            BuildVaults(root, mats);
            BuildArcades(root, mats);
            BuildApse(root, mats);
            BuildThrone(root, mats);
            BuildCouncil(root, mats);
            BuildStalls(root, mats);
            BuildNarthex(root, mats);
            BuildLights(root, mats);
            BuildHull(root);
            root.gameObject.SetActive(false);
            return root;
        }

        sealed class Mats
        {
            public Material Stone, StoneDark, Marble, MarbleDark, Lapis, Wood, Bronze, Velvet, Gold, GoldLeaf, Crimson, Light, Flame, Sky, Leaf;
        }

        static Texture2D _sky;

        /// <summary>The vault's painted night: deep ultramarine, gold stars of three sizes (generated once).</summary>
        static Texture2D NightSky()
        {
            if (_sky != null)
                return _sky;
            const int n = 256;
            var px = new Color32[n * n];
            var bg = new Color(0.08f, 0.11f, 0.26f);
            for (var i = 0; i < px.Length; i++)
            {
                var y = i / n / (float)n;
                px[i] = Color.Lerp(bg, bg * 1.25f, Mathf.PerlinNoise(i % n * 0.03f, y * 7f) * 0.6f);
            }

            var seed = 4242;
            int Rand(int m)
            {
                seed = seed * 1103515245 + 12345 & 0x7fffffff;
                return seed % m;
            }

            var gold = new Color(1f, 0.85f, 0.5f);
            for (var s = 0; s < 120; s++)
            {
                var cx = Rand(n);
                var cy = Rand(n);
                var r = s % 12 == 0 ? 3 : s % 4 == 0 ? 2 : 1;
                for (var dy = -r; dy <= r; dy++)
                    for (var dx = -r; dx <= r; dx++)
                    {
                        // A small four-point star (a cross), not a dot.
                        if (dx != 0 && dy != 0 && r > 1)
                            continue;
                        var x = (cx + dx + n) % n;
                        var y = (cy + dy + n) % n;
                        px[y * n + x] = Color.Lerp(px[y * n + x], gold, 1f - (Mathf.Abs(dx) + Mathf.Abs(dy)) / (r + 1f));
                    }
            }

            _sky = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SU_CitadelNightSky", wrapMode = TextureWrapMode.Repeat };
            _sky.SetPixels32(px);
            _sky.Apply(true, true);
            return _sky;
        }

        // ── Floors: marble nave and aisles, the lapis map floor, the runner ───────

        static void BuildFloors(Transform root, Mats m)
        {
            var f = new LatheMesh(Vector3.zero) { UvOf = p => new Vector2(p.x, p.z) };
            var dark = new LatheMesh(Vector3.zero) { UvOf = p => new Vector2(p.x, p.z) };
            // Nave and aisles in a checker of pale and grey marble, 1.3 m squares.
            const float sq = 1.3f;
            for (var x = -WallX; x < WallX - 1e-3f; x += sq)
                for (var z = NarthexZ; z < ApseZ - 1e-3f; z += sq)
                {
                    var xe = Mathf.Min(WallX, x + sq);
                    var ze = Mathf.Min(ApseZ, z + sq);
                    var odd = (Mathf.RoundToInt((x + WallX) / sq) + Mathf.RoundToInt((z - NarthexZ) / sq)) % 2 == 1;
                    (odd ? dark : f).Quad(new Vector3(x, 0f, z), new Vector3(xe, 0f, z), new Vector3(xe, 0f, ze), new Vector3(x, 0f, ze), Vector3.up, 1f);
                }

            // The apse: a half disc, raised one step.
            var apse = new LatheMesh(new Vector3(0f, 0f, ApseZ));
            apse.Revolve(new[] { new Vector2(0f, 0.16f), new Vector2(ApseR, 0.16f) }, -90f, 90f, true, LatheMesh.Uv.Planar);
            apse.Quad(new Vector3(-ApseR, 0f, ApseZ - 0.6f), new Vector3(ApseR, 0f, ApseZ - 0.6f), new Vector3(ApseR, 0.16f, ApseZ - 0.6f),
                new Vector3(-ApseR, 0.16f, ApseZ - 0.6f), Vector3.back, 0.7f);
            apse.Quad(new Vector3(-ApseR, 0.16f, ApseZ - 0.6f), new Vector3(ApseR, 0.16f, ApseZ - 0.6f), new Vector3(ApseR, 0.16f, ApseZ),
                new Vector3(-ApseR, 0.16f, ApseZ), Vector3.up, 1f);
            LatheMesh.Part(root, "Deck", f.ToMesh("SU_HallFloor"), m.Marble);
            LatheMesh.Part(root, "DeckDark", dark.ToMesh("SU_HallFloorDark"), m.MarbleDark);
            LatheMesh.Part(root, "ApseFloor", apse.ToMesh("SU_HallApseFloor"), m.Marble, collider: true).name = "Dais";

            // The map floor: a lapis disc round the holo table with a gold star chart and a compass ring.
            var c = new Vector3(0f, 0f, WorldScale.CicTableCenterZ);
            var lapis = new LatheMesh(c) { Step = 6f };
            lapis.Revolve(new[] { new Vector2(0f, 0.006f), new Vector2(2.7f, 0.006f) }, 0f, 360f, true, LatheMesh.Uv.Planar);
            LatheMesh.Part(root, "MapFloor", lapis.ToMesh("SU_HallMapFloor"), m.Lapis);
            var g = new LatheMesh(c);
            foreach (var (r0, r1) in new[] { (1.62f, 1.66f), (2.62f, 2.72f) })
                g.Revolve(new[] { new Vector2(r0, 0.008f), new Vector2(r1, 0.008f) }, 0f, 360f, true);
            for (var k = 0; k < 16; k++)
            {
                var a = k * 22.5f;
                var tip = k % 2 == 0 ? 2.6f : 2.2f;
                g.Tri(g.At(a - 4f, 1.7f, 0.009f), g.At(a, tip, 0.009f), g.At(a + 4f, 1.7f, 0.009f), Vector3.up, 1f);
            }

            // Constellation lines and stars on the lapis between the rings.
            var seed = 77;
            float Rnd()
            {
                seed = seed * 1103515245 + 12345 & 0x7fffffff;
                return seed / (float)0x7fffffff;
            }

            var prev = Vector3.zero;
            for (var k = 0; k < 28; k++)
            {
                var a = Rnd() * 360f;
                var r = 1.75f + Rnd() * 0.8f;
                var p = g.At(a, r, 0.01f);
                g.Quad(p + new Vector3(-0.02f, 0f, -0.02f), p + new Vector3(0.02f, 0f, -0.02f), p + new Vector3(0.02f, 0f, 0.02f), p + new Vector3(-0.02f, 0f, 0.02f),
                    Vector3.up, 1f);
                if (k % 4 != 0 && (p - prev).magnitude < 0.9f)
                {
                    var d = (p - prev).normalized;
                    var s = Vector3.Cross(Vector3.up, d) * 0.004f;
                    g.Quad(prev - s, prev + s, p + s, p - s, Vector3.up, 1f);
                }

                prev = p;
            }

            LatheMesh.Part(root, "MapFloorGold", g.ToMesh("SU_HallMapGold"), m.GoldLeaf);

            // The crimson runner from the door to the throne, gold-bordered; a gold line down the nave's axis to the apse.
            var run = new LatheMesh(Vector3.zero);
            var edge = new LatheMesh(Vector3.zero);
            const float hw = 0.8f;
            var z0 = NarthexZ + 0.25f;
            var z1 = WorldScale.CicCaptainChairZ - 1.25f;
            run.Quad(new Vector3(-hw, 0.006f, z0), new Vector3(hw, 0.006f, z0), new Vector3(hw, 0.006f, z1), new Vector3(-hw, 0.006f, z1), Vector3.up, 1f);
            foreach (var x in new[] { -hw, hw - 0.07f })
                edge.Quad(new Vector3(x, 0.008f, z0), new Vector3(x + 0.07f, 0.008f, z0), new Vector3(x + 0.07f, 0.008f, z1), new Vector3(x, 0.008f, z1), Vector3.up, 1f);
            for (var z = z0 + 0.7f; z < z1 - 0.3f; z += 1.3f)
                edge.Tri(new Vector3(0f, 0.008f, z - 0.18f), new Vector3(0.14f, 0.008f, z), new Vector3(0f, 0.008f, z + 0.18f), Vector3.up, 1f);
            for (var z = z0 + 0.7f; z < z1 - 0.3f; z += 1.3f)
                edge.Tri(new Vector3(0f, 0.008f, z + 0.18f), new Vector3(-0.14f, 0.008f, z), new Vector3(0f, 0.008f, z - 0.18f), Vector3.up, 1f);
            edge.Quad(new Vector3(-0.025f, 0.008f, WorldScale.CicTableCenterZ + 2.75f), new Vector3(0.025f, 0.008f, WorldScale.CicTableCenterZ + 2.75f),
                new Vector3(0.025f, 0.008f, ApseZ - 0.62f), new Vector3(-0.025f, 0.008f, ApseZ - 0.62f), Vector3.up, 1f);
            LatheMesh.Part(root, "Runner", run.ToMesh("SU_HallRunner"), m.Crimson);
            LatheMesh.Part(root, "RunnerGold", edge.ToMesh("SU_HallRunnerGold"), m.GoldLeaf);

            // One walkable slab under everything.
            var slab = new GameObject("Floor");
            slab.transform.SetParent(root, false);
            slab.transform.localPosition = new Vector3(0f, -0.25f, (NarthexZ + ApseZ + ApseR) * 0.5f);
            slab.AddComponent<BoxCollider>().size = new Vector3(WallX * 2f, 0.5f, ApseZ + ApseR - NarthexZ);
        }

        // ── Walls: aisles' outer walls with lancets, nave walls with arcades, tribunes and clerestory ──

        static Vector2 WallUv(Vector3 p) => new(p.x + p.z, p.y);

        static void BuildWalls(Transform root, Mats m)
        {
            var wall = new LatheMesh(Vector3.zero) { UvOf = WallUv };
            var reveal = new LatheMesh(Vector3.zero) { UvOf = WallUv };
            var gold = new LatheMesh(Vector3.zero);
            var length = ApseZ - NarthexZ;
            foreach (var s in new[] { -1f, 1f })
            {
                // Outer wall: a lancet in every bay (aisle) and a smaller one over it (tribune).
                var outer = new List<Opening>();
                var nave = new List<Opening>();
                for (var i = 0; i < Piers.Length - 1; i++)
                {
                    var u = (Piers[i] + Piers[i + 1]) * 0.5f - NarthexZ;
                    var bay = Piers[i + 1] - Piers[i];
                    outer.Add(Lancet(u, Mathf.Min(0.62f, bay * 0.28f), 0.8f, 3.5f));
                    outer.Add(Lancet(u, Mathf.Min(0.42f, bay * 0.2f), TribuneY + 0.6f, TribuneY + 1.3f));
                    // Nave wall: the arcade (open to the aisle), the tribune's arch, a clerestory lancet.
                    nave.Add(Lancet(u, bay * 0.5f - 0.38f, 0f, 3.4f));
                    nave.Add(Lancet(u, bay * 0.5f - 0.55f, TribuneY + 0.02f, TribuneY + 1.25f));
                    nave.Add(Lancet(u, Mathf.Min(0.55f, bay * 0.22f), TribuneTop + 0.7f, TribuneTop + 2.1f));
                }

                // Faces: the outer wall looks into the aisle (−s x), the nave wall into the nave (−s x) — and its
                // back into the aisle (a second, plain face).
                var along = Vector3.forward;
                FlatWall(wall, reveal, gold, new Vector3(s * WallX, 0f, NarthexZ), along, new Vector3(-s, 0f, 0f), length, _ => TribuneTop, outer, 0.28f);
                FlatWall(wall, reveal, gold, new Vector3(s * NaveX, 0f, NarthexZ), along, new Vector3(-s, 0f, 0f), length, _ => NaveTop, nave, 0.6f);
                var back = new List<Opening>(nave.FindAll(o => o.Sill < TribuneTop));
                FlatWall(wall, reveal, gold, new Vector3(s * (NaveX + 0.6f), 0f, NarthexZ), along, new Vector3(s, 0f, 0f), length, _ => TribuneTop, back, 0.01f);
            }

            // The aisles' and tribunes' east ends, beside the apse.
            foreach (var s in new[] { -1f, 1f })
                wall.Quad(new Vector3(s * NaveX, 0f, ApseZ), new Vector3(s * WallX, 0f, ApseZ), new Vector3(s * WallX, TribuneTop + 0.6f, ApseZ),
                    new Vector3(s * NaveX, TribuneTop + 0.6f, ApseZ), Vector3.back, 0.9f);
            LatheMesh.Part(root, "Walls", wall.ToMesh("SU_HallWalls"), m.Stone);
            LatheMesh.Part(root, "Reveals", reveal.ToMesh("SU_HallReveals"), m.StoneDark);
            LatheMesh.Part(root, "Tracery", gold.ToMesh("SU_HallTracery"), m.GoldLeaf);
        }

        // ── Vaults: the nave's pointed barrel, the aisles' and tribunes' ceilings ──

        static void BuildVaults(Transform root, Mats m)
        {
            var v = new LatheMesh(Vector3.zero) { UvOf = p => new Vector2(p.x, p.z) * 0.35f };
            var ribs = new LatheMesh(Vector3.zero);
            var gold = new LatheMesh(Vector3.zero);
            const int segs = 16;
            for (var i = 0; i < segs; i++)
            {
                var x0 = -NaveX + 2f * NaveX * i / segs;
                var x1 = -NaveX + 2f * NaveX * (i + 1) / segs;
                v.Quad(new Vector3(x0, VaultY(x0), NarthexZ), new Vector3(x1, VaultY(x1), NarthexZ), new Vector3(x1, VaultY(x1), ApseZ),
                    new Vector3(x0, VaultY(x0), ApseZ), Vector3.down, 1f);
            }

            // Transverse ribs over every pier, a ridge rib, gold bosses where they cross.
            foreach (var z in Piers)
            {
                for (var i = 0; i < segs; i++)
                {
                    var x0 = -NaveX + 2f * NaveX * i / segs;
                    var x1 = -NaveX + 2f * NaveX * (i + 1) / segs;
                    var a = new Vector3(x0, VaultY(x0) - 0.12f, z);
                    var b = new Vector3(x1, VaultY(x1) - 0.12f, z);
                    ribs.Quad(a + Vector3.back * 0.12f, b + Vector3.back * 0.12f, b + Vector3.forward * 0.12f, a + Vector3.forward * 0.12f, Vector3.down, 1f);
                    ribs.Quad(a + Vector3.back * 0.12f, b + Vector3.back * 0.12f, b + Vector3.back * 0.12f + Vector3.up * 0.12f,
                        a + Vector3.back * 0.12f + Vector3.up * 0.12f, Vector3.back, 0.8f);
                    ribs.Quad(a + Vector3.forward * 0.12f, b + Vector3.forward * 0.12f, b + Vector3.forward * 0.12f + Vector3.up * 0.12f,
                        a + Vector3.forward * 0.12f + Vector3.up * 0.12f, Vector3.forward, 0.8f);
                }

                gold.Quad(new Vector3(-0.22f, VaultTop - 0.14f, z - 0.22f), new Vector3(0.22f, VaultTop - 0.14f, z - 0.22f),
                    new Vector3(0.22f, VaultTop - 0.14f, z + 0.22f), new Vector3(-0.22f, VaultTop - 0.14f, z + 0.22f), Vector3.down, 1f);
            }

            ribs.Quad(new Vector3(-0.1f, VaultTop - 0.1f, NarthexZ), new Vector3(0.1f, VaultTop - 0.1f, NarthexZ), new Vector3(0.1f, VaultTop - 0.1f, ApseZ),
                new Vector3(-0.1f, VaultTop - 0.1f, ApseZ), Vector3.down, 1f);
            // Gold lines along the vault's springing.
            foreach (var s in new[] { -1f, 1f })
                gold.Quad(new Vector3(s * (NaveX - 0.02f), NaveTop + 0.02f, NarthexZ), new Vector3(s * (NaveX - 0.02f), NaveTop + 0.08f, NarthexZ),
                    new Vector3(s * (NaveX - 0.02f), NaveTop + 0.08f, ApseZ), new Vector3(s * (NaveX - 0.02f), NaveTop + 0.02f, ApseZ), new Vector3(-s, 0f, 0f), 1f);
            LatheMesh.Part(root, "Vault", v.ToMesh("SU_HallVault"), m.Sky);
            LatheMesh.Part(root, "VaultRibs", ribs.ToMesh("SU_HallVaultRibs"), m.Stone);
            LatheMesh.Part(root, "VaultGold", gold.ToMesh("SU_HallVaultGold"), m.Gold);

            // Aisles: the tribune floor's underside (coffered by the bays); tribunes: their lean-to roof; the
            // tribune floor with its balustrade over the nave.
            var c = new LatheMesh(Vector3.zero);
            var floor = new LatheMesh(Vector3.zero);
            foreach (var s in new[] { -1f, 1f })
            {
                var xa = s * (NaveX + 0.6f);
                var xb = s * WallX;
                c.Quad(new Vector3(xa, TribuneY - 0.3f, NarthexZ), new Vector3(xb, TribuneY - 0.3f, NarthexZ), new Vector3(xb, TribuneY - 0.3f, ApseZ),
                    new Vector3(xa, TribuneY - 0.3f, ApseZ), Vector3.down, 0.8f);
                c.Quad(new Vector3(xb, TribuneTop, NarthexZ), new Vector3(s * NaveX, TribuneTop + 0.6f, NarthexZ), new Vector3(s * NaveX, TribuneTop + 0.6f, ApseZ),
                    new Vector3(xb, TribuneTop, ApseZ), Vector3.down, 0.85f);
                floor.Quad(new Vector3(xa, TribuneY, NarthexZ), new Vector3(xb, TribuneY, NarthexZ), new Vector3(xb, TribuneY, ApseZ),
                    new Vector3(xa, TribuneY, ApseZ), Vector3.up, 1f);
                for (var z = NarthexZ; z < ApseZ; z += 2.6f)
                    c.Quad(new Vector3(xa, TribuneY - 0.5f, z - 0.12f), new Vector3(xb, TribuneY - 0.5f, z - 0.12f), new Vector3(xb, TribuneY - 0.5f, z + 0.12f),
                        new Vector3(xa, TribuneY - 0.5f, z + 0.12f), Vector3.down, 0.7f);
            }

            LatheMesh.Part(root, "AisleCeilings", c.ToMesh("SU_HallAisleCeilings"), m.StoneDark);
            LatheMesh.Part(root, "TribuneFloors", floor.ToMesh("SU_HallTribuneFloors"), m.Marble);
        }

        // ── Arcades: columns, capitals, the tribunes' balustrades, banners ────────

        static void BuildArcades(Transform root, Mats m)
        {
            var b = new MeshBatch();
            foreach (var s in new[] { -1f, 1f })
            {
                foreach (var z in Piers)
                {
                    if (z <= NarthexZ + 0.01f || z >= ApseZ - 0.01f)
                        continue;
                    var x = s * (NaveX - 0.18f);
                    // A clustered column before the pier: base, shaft, gilt capital; a slimmer shaft up to the vault.
                    b.Box(new Vector3(x, 0.18f, z), new Vector3(0.7f, 0.36f, 0.7f), m.Stone);
                    b.Tube(new Vector3(x, 1.85f, z), Vector3.up, 0.24f, 3.0f, m.Stone);
                    b.Box(new Vector3(x, 3.42f, z), new Vector3(0.66f, 0.16f, 0.66f), m.Gold);
                    b.Tube(new Vector3(s * (NaveX - 0.08f), (3.5f + NaveTop) * 0.5f, z), Vector3.up, 0.1f, NaveTop - 3.5f, m.Stone);
                    b.Box(new Vector3(s * (NaveX - 0.08f), NaveTop - 0.1f, z), new Vector3(0.32f, 0.12f, 0.32f), m.Gold);
                }

                // The tribune's balustrade over the nave: balusters, a rail, a gold line under it.
                for (var z = NarthexZ + 0.2f; z < ApseZ - 0.1f; z += 0.32f)
                    b.Tube(new Vector3(s * (NaveX + 0.05f), TribuneY + 0.42f, z), Vector3.up, 0.045f, 0.8f, m.Stone);
                b.Box(new Vector3(s * (NaveX + 0.05f), TribuneY + 0.86f, (NarthexZ + ApseZ) * 0.5f), new Vector3(0.22f, 0.08f, ApseZ - NarthexZ), m.Stone);
                b.Box(new Vector3(s * (NaveX - 0.06f), TribuneY - 0.05f, (NarthexZ + ApseZ) * 0.5f), new Vector3(0.02f, 0.06f, ApseZ - NarthexZ), m.Gold);

                // A crimson banner hangs from the balustrade in every bay, gold-bordered with the citadel's mark.
                for (var i = 0; i < Piers.Length - 1; i++)
                {
                    var z = (Piers[i] + Piers[i + 1]) * 0.5f;
                    var x = s * (NaveX - 0.12f);
                    b.Box(new Vector3(x, TribuneY + 0.9f, z), new Vector3(0.04f, 0.04f, 1.1f), m.Gold);
                    b.Box(new Vector3(x, TribuneY - 0.3f, z), new Vector3(0.012f, 2.3f, 0.95f), m.Crimson);
                    // The citadel's mark: an eight-point star in a ring of four studs.
                    var mark = new Vector3(x - s * 0.01f, TribuneY - 0.6f, z);
                    b.Box(new Vector3(x - s * 0.01f, TribuneY + 0.65f, z), new Vector3(0.006f, 0.06f, 0.75f), m.GoldLeaf);
                    b.Box(mark, new Vector3(0.006f, 0.34f, 0.34f), m.GoldLeaf);
                    b.Box(mark, new Vector3(0.006f, 0.34f, 0.34f), m.GoldLeaf, Quaternion.Euler(45f, 0f, 0f));
                    b.Box(mark - new Vector3(s * 0.002f, 0f, 0f), new Vector3(0.006f, 0.16f, 0.16f), m.Crimson, Quaternion.Euler(22.5f, 0f, 0f));
                    foreach (var (dy, dz) in new[] { (0.36f, 0f), (-0.36f, 0f), (0f, 0.36f), (0f, -0.36f) })
                        b.Box(mark + new Vector3(0f, dy, dz), new Vector3(0.006f, 0.06f, 0.06f), m.GoldLeaf, Quaternion.Euler(45f, 0f, 0f));
                    b.Box(new Vector3(x, TribuneY - 1.45f, z), new Vector3(0.02f, 0.05f, 0.97f), m.Gold);
                }
            }

            b.Build(root, "Arcades");
        }

        // ── The apse: half-dome, five lancets, the screen's retable, braziers ─────

        static void BuildApse(Transform root, Mats m)
        {
            var c = new Vector3(0f, 0f, ApseZ);
            Vector2 ArcUv(Vector3 p) => new(Mathf.Atan2(p.x, p.z - ApseZ) * ApseR, p.y);
            var wall = new LatheMesh(c) { UvOf = ArcUv };
            var reveal = new LatheMesh(c) { UvOf = ArcUv };
            var gold = new LatheMesh(c);
            float[] lancets = { -60f, -30f, 0f, 30f, 60f };
            const float half = 6.5f;
            const float sill = 3.2f;
            const float spring = 7.6f;
            const float apex = 9.3f;
            float HalfAt(float y) => y < sill || y > apex ? -1f : y <= spring ? half : half * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow((y - spring) / (apex - spring), 1.7f)), 0.62f);
            const float step = 1.5f;
            for (var a = -90f; a < 90f - 0.01f; a += step)
            {
                var a1 = a + step;
                var mid = a + step * 0.5f;
                var runStart = -1f;
                for (var y = 0f; y < NaveTop - 1e-3f; y += 0.1f)
                {
                    var y1 = Mathf.Min(NaveTop, y + 0.1f);
                    var open = false;
                    foreach (var l in lancets)
                    {
                        var w = HalfAt((y + y1) * 0.5f);
                        if (w > 0f && Mathf.Abs(mid - l) < w)
                            open = true;
                    }

                    if (!open && runStart < 0f)
                        runStart = y;
                    if (open && runStart >= 0f)
                    {
                        wall.Quad(wall.At(a, ApseR, runStart), wall.At(a1, ApseR, runStart), wall.At(a1, ApseR, y), wall.At(a, ApseR, y), -LatheMesh.Dir(mid), 1f);
                        runStart = -1f;
                    }
                }

                if (runStart >= 0f)
                    wall.Quad(wall.At(a, ApseR, runStart), wall.At(a1, ApseR, runStart), wall.At(a1, ApseR, NaveTop), wall.At(a, ApseR, NaveTop), -LatheMesh.Dir(mid), 1f);
            }

            foreach (var l in lancets)
            {
                var py = sill;
                var pw = half;
                for (var y = sill + 0.12f; y <= apex + 0.001f; y += 0.12f)
                {
                    var w = Mathf.Max(0f, HalfAt(Mathf.Min(y, apex - 0.001f)));
                    foreach (var s in new[] { -1f, 1f })
                    {
                        reveal.Quad(reveal.At(l + s * pw, ApseR, py), reveal.At(l + s * pw, ApseR + 0.5f, py), reveal.At(l + s * w, ApseR + 0.5f, y),
                            reveal.At(l + s * w, ApseR, y), -s * Vector3.Cross(Vector3.up, LatheMesh.Dir(l)), 0.75f);
                        gold.Quad(gold.At(l + s * pw, ApseR - 0.005f, py), gold.At(l + s * (pw + 0.6f), ApseR - 0.005f, py),
                            gold.At(l + s * (w + 0.6f), ApseR - 0.005f, y), gold.At(l + s * w, ApseR - 0.005f, y), -LatheMesh.Dir(l), 1f);
                    }

                    py = y;
                    pw = w;
                }

                reveal.Revolve(new[] { new Vector2(ApseR, sill), new Vector2(ApseR + 0.5f, sill) }, l - half, l + half, false);
                gold.Bar(l, ApseR + 0.28f, ApseR + 0.32f, sill, apex - 0.05f, 0.02f);
            }

            // The half-dome, painted like the vault, with gold ribs down to each pier of the apse.
            var dome = new Vector2[10];
            for (var i = 0; i < dome.Length; i++)
            {
                var t = i / (dome.Length - 1f);
                dome[i] = new Vector2(ApseR * Mathf.Cos(t * Mathf.PI * 0.5f), NaveTop + (VaultTop - NaveTop) * Mathf.Sin(t * Mathf.PI * 0.5f));
            }

            var d = new LatheMesh(c);
            d.Revolve(dome, -90f, 90f, true);
            var dr = new LatheMesh(c);
            foreach (var a in new[] { -75f, -45f, -15f, 15f, 45f, 75f })
            {
                dr.Rib(dome, a, 0.09f, 0.1f, true);
                dr.Bar(a, ApseR - 0.25f, ApseR, 0f, NaveTop, 0.14f);
            }

            LatheMesh.Part(root, "ApseWall", wall.ToMesh("SU_HallApseWall"), m.Stone);
            LatheMesh.Part(root, "ApseReveals", reveal.ToMesh("SU_HallApseReveals"), m.StoneDark);
            LatheMesh.Part(root, "ApseTracery", gold.ToMesh("SU_HallApseTracery"), m.GoldLeaf);
            LatheMesh.Part(root, "ApseDome", d.ToMesh("SU_HallApseDome"), m.Sky);
            LatheMesh.Part(root, "ApseRibs", dr.ToMesh("SU_HallApseRibs"), m.Stone);

            // The retable round the main screen (the screen itself is the viewscreen's, at the apse chord): a
            // carved altar under it, a gilt frame, a pointed gable with pinnacles over it, candelabra.
            var b = new MeshBatch();
            var z = ApseZ + 0.08f;
            b.Box(new Vector3(0f, 0.6f, z + 0.25f), new Vector3(5.6f, 1.05f, 0.7f), m.StoneDark);
            b.Box(new Vector3(0f, 1.13f, z - 0.05f), new Vector3(5.7f, 0.06f, 0.2f), m.Gold);
            foreach (var s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(s * 2.62f, 2.15f, z), new Vector3(0.22f, 2.2f, 0.2f), m.Gold);
                b.Box(new Vector3(s * 2.62f, 3.6f, z), new Vector3(0.12f, 0.7f, 0.12f), m.Gold);
                b.Box(new Vector3(s * 2.62f, 4.0f, z), new Vector3(0.2f, 0.1f, 0.2f), m.Gold);
                // Candelabra on the apse step.
                var cx = s * 3.6f;
                b.Box(new Vector3(cx, 0.26f, ApseZ - 0.3f), new Vector3(0.36f, 0.2f, 0.36f), m.Bronze);
                b.Tube(new Vector3(cx, 1.2f, ApseZ - 0.3f), Vector3.up, 0.05f, 1.7f, m.Gold);
                b.Box(new Vector3(cx, 2.05f, ApseZ - 0.3f), new Vector3(0.6f, 0.04f, 0.05f), m.Gold);
                foreach (var dx in new[] { -0.28f, 0f, 0.28f })
                    b.Box(new Vector3(cx + dx, 2.18f, ApseZ - 0.3f), new Vector3(0.05f, 0.22f, 0.05f), m.Light);
            }

            b.Box(new Vector3(0f, 2.86f, z), new Vector3(5.46f, 0.12f, 0.2f), m.Gold);
            for (var i = 0; i < 8; i++)
            {
                var t0 = i / 8f;
                var t1 = (i + 1) / 8f;
                foreach (var s in new[] { -1f, 1f })
                {
                    var p0 = new Vector3(s * 2.62f * (1f - t0), 2.92f + t0 * 1.6f, z);
                    var p1 = new Vector3(s * 2.62f * (1f - t1), 2.92f + t1 * 1.6f, z);
                    b.Strut(p0, p1, 0.12f, m.Gold);
                }
            }

            b.Box(new Vector3(0f, 4.7f, z), new Vector3(0.14f, 0.5f, 0.14f), m.Gold);
            b.Box(new Vector3(0f, 3.4f, z + 0.12f), new Vector3(4.6f, 0.9f, 0.06f), m.Velvet);
            b.Build(root, "Retable");
            var altar = new GameObject("RetableCollider");
            altar.transform.SetParent(root, false);
            altar.transform.localPosition = new Vector3(0f, 1.5f, z + 0.3f);
            altar.AddComponent<BoxCollider>().size = new Vector3(5.8f, 3f, 0.8f);

            // Two braziers flanking the apse step.
            var fire = new MeshBatch();
            var flames = new List<Transform>();
            foreach (var s in new[] { -1f, 1f })
            {
                var at = new Vector3(s * 5.4f, 0f, ApseZ - 1.2f);
                for (var k = 0; k < 3; k++)
                {
                    var leg = LatheMesh.Dir(k * 120f) * 0.22f;
                    fire.Strut(at + leg, at + Vector3.up * 0.8f + leg * 0.35f, 0.035f, m.Bronze);
                }

                fire.Tube(at + Vector3.up * 0.85f, Vector3.up, 0.32f, 0.12f, m.Bronze);
                fire.Tube(at + Vector3.up * 0.92f, Vector3.up, 0.34f, 0.03f, m.Gold);
                var f = new GameObject("BrazierFlame").transform;
                f.SetParent(root, false);
                f.localPosition = at + Vector3.up * 0.93f;
                LatheMesh.Part(f, "Flame", Stations.CitadelGallery.Cone(), m.Flame).transform.localScale = new Vector3(0.4f, 0.48f, 0.4f);
                flames.Add(f);
            }

            fire.Build(root, "Braziers");
            root.gameObject.AddComponent<BrazierFlicker>().Flames = flames.ToArray();
        }

        // ── The throne under its baldachin ──────────────────────────────────────

        static void BuildThrone(Transform root, Mats m)
        {
            var t = Throne;
            var b = new MeshBatch();
            // Dais: a broad marble step, then the seat's own (the ship chair's footprint and 0.16 m height, so its
            // kept collider is what one steps on).
            b.Box(t + new Vector3(0f, 0.04f, -0.15f), new Vector3(2.3f, 0.08f, 1.9f), m.Marble);
            b.Box(t + new Vector3(0f, 0.12f, 0f), new Vector3(1.6f, 0.08f, 1.3f), m.Marble);
            b.Box(t + new Vector3(0f, 0.163f, 0.645f), new Vector3(1.56f, 0.008f, 0.012f), m.Gold);
            b.Box(t + new Vector3(0f, 0.083f, 0.8f), new Vector3(2.26f, 0.008f, 0.012f), m.Gold);
            // The seat: crimson velvet on a carved frame (cushion at the chair's 0.63 m), a high back with a gold
            // crest, the arm consoles at the front of the armrests.
            b.Box(t + new Vector3(0f, 0.38f, 0.02f), new Vector3(0.66f, 0.44f, 0.58f), m.Wood);
            b.Box(t + new Vector3(0f, 0.615f, 0.03f), new Vector3(0.56f, 0.03f, 0.5f), m.Velvet);
            b.Box(t + new Vector3(0f, 1.5f, -0.33f), new Vector3(0.78f, 2.6f, 0.14f), m.Wood);
            b.Box(t + new Vector3(0f, 1.25f, -0.255f), new Vector3(0.56f, 1.2f, 0.02f), m.Velvet);
            b.Box(t + new Vector3(0f, 2.82f, -0.33f), new Vector3(0.88f, 0.08f, 0.2f), m.Gold);
            b.Box(t + new Vector3(0f, 1.9f, -0.25f), new Vector3(0.22f, 0.22f, 0.02f), m.Gold);
            for (var i = 0; i < 5; i++)
            {
                var x = (i - 2) * 0.17f;
                var h = 0.2f + (2 - Mathf.Abs(i - 2)) * 0.16f;
                b.Box(t + new Vector3(x, 2.86f + h * 0.5f, -0.33f), new Vector3(0.05f, h, 0.05f), m.Gold);
            }

            foreach (var s in new[] { -1f, 1f })
            {
                b.Box(t + new Vector3(s * 0.36f, 0.47f, 0f), new Vector3(0.1f, 0.62f, 0.56f), m.Wood);
                b.Box(t + new Vector3(s * 0.36f, 0.79f, -0.02f), new Vector3(0.12f, 0.03f, 0.5f), m.Velvet);
                b.Box(t + new Vector3(s * 0.36f, 0.25f, 0.25f), new Vector3(0.14f, 0.06f, 0.1f), m.Gold);
            }

            foreach (var s in new[] { -1f, 1f })
            {
                // Baldachin columns behind the throne only (none beside the commander's line of sight): the canopy
                // reaches forward on gilt brackets, a gold fringe and a crown on top.
                b.Tube(t + new Vector3(s * 1.05f, 1.95f, -1.15f), Vector3.up, 0.07f, 3.9f, m.Gold);
                b.Box(t + new Vector3(s * 1.05f, 0.14f, -1.15f), new Vector3(0.22f, 0.12f, 0.22f), m.Marble);
                b.Strut(t + new Vector3(s * 1.05f, 2.9f, -1.15f), t + new Vector3(s * 1.05f, 3.82f, 0.55f), 0.05f, m.Gold);
            }

            b.Box(t + new Vector3(0f, 3.95f, -0.27f), new Vector3(2.35f, 0.12f, 2.0f), m.Velvet);
            b.Box(t + new Vector3(0f, 3.86f, -0.27f), new Vector3(2.4f, 0.06f, 2.05f), m.Gold);
            for (var i = 0; i < 4; i++)
                b.Strut(t + new Vector3(i % 2 == 0 ? -1.15f : 1.15f, 4.0f, i < 2 ? -1.25f : 0.7f), t + new Vector3(0f, 4.55f, -0.27f), 0.05f, m.Gold);
            b.Box(t + new Vector3(0f, 4.62f, -0.27f), new Vector3(0.24f, 0.14f, 0.24f), m.Gold);
            b.Build(root, "Throne");
        }

        // ── The council table round the holo map ─────────────────────────────────

        /// <summary>The council table's ring (round the holo table) and its open arc toward the throne.</summary>
        const float CouncilInner = 1.36f;
        const float CouncilOuter = 1.98f;
        const float CouncilTop = 0.76f;
        const float CouncilArc = 112f;
        /// <summary>Councillors' chairs: bearings round the table (0 = toward the screen), none ahead or by the throne.</summary>
        static readonly float[] Chairs = { -108f, -74f, -40f, 40f, 74f, 108f };
        const float ChairR = 2.42f;

        static Vector3 TableCentre => new(0f, 0f, WorldScale.CicTableCenterZ);

        /// <summary>Two councillors standing at the table over the map (dressing, <see cref="Core.Crew.CrewLife"/>).</summary>
        public static (Vector3 pos, Quaternion rot) CouncillorPose(int i)
        {
            var deg = i == 0 ? 57f : -91f;
            var d = LatheMesh.Dir(deg);
            return (TableCentre + d * (CouncilOuter + 0.32f), Quaternion.LookRotation(-d, Vector3.up));
        }

        /// <summary>
        /// The holo map rises from the well of a carved council table — a ring of dark wood with a gold inlay
        /// and a lapis band, open on the throne's side so the commander steps up to the map — and the council
        /// sits round it on high-backed chairs, lower than the throne.
        /// </summary>
        static void BuildCouncil(Transform root, Mats m)
        {
            var c = TableCentre;
            Vector2[] section =
            {
                new(CouncilOuter, CouncilTop - 0.09f), new(CouncilOuter + 0.03f, CouncilTop - 0.02f), new(CouncilOuter, CouncilTop),
                new(CouncilInner, CouncilTop), new(CouncilInner, CouncilTop - 0.09f), new(CouncilOuter, CouncilTop - 0.09f)
            };
            var ring = new LatheMesh(c) { Step = 4f };
            ring.Revolve(section, -CouncilArc, CouncilArc, false);
            ring.Cap(section, -CouncilArc, false);
            ring.Cap(section, CouncilArc, true);
            // The apron under the top, and a skirt down to the floor on the outside (the table reads solid).
            ring.Revolve(new[] { new Vector2(CouncilOuter - 0.12f, 0.08f), new Vector2(CouncilOuter - 0.12f, CouncilTop - 0.09f) }, -CouncilArc + 3f, CouncilArc - 3f, false);
            ring.Revolve(new[] { new Vector2(CouncilInner + 0.08f, CouncilTop - 0.09f), new Vector2(CouncilInner + 0.08f, 0.08f) }, -CouncilArc + 3f, CouncilArc - 3f, false);
            LatheMesh.Part(root, "CouncilTable", ring.ToMesh("SU_HallCouncilTable"), m.Wood);

            var inlay = new LatheMesh(c) { Step = 4f };
            inlay.Revolve(new[] { new Vector2(CouncilOuter - 0.06f, CouncilTop + 0.002f), new Vector2(CouncilOuter - 0.08f, CouncilTop + 0.002f) },
                -CouncilArc + 2f, CouncilArc - 2f, false);
            inlay.Revolve(new[] { new Vector2(CouncilInner + 0.06f, CouncilTop + 0.002f), new Vector2(CouncilInner + 0.04f, CouncilTop + 0.002f) },
                -CouncilArc + 2f, CouncilArc - 2f, false);
            LatheMesh.Part(root, "CouncilInlay", inlay.ToMesh("SU_HallCouncilInlay"), m.GoldLeaf);
            var band = new LatheMesh(c) { Step = 4f };
            band.Revolve(new[] { new Vector2(CouncilOuter - 0.11f, CouncilTop + 0.001f), new Vector2(CouncilOuter - 0.21f, CouncilTop + 0.001f) },
                -CouncilArc + 2f, CouncilArc - 2f, false);
            LatheMesh.Part(root, "CouncilBand", band.ToMesh("SU_HallCouncilBand"), m.Lapis);

            var b = new MeshBatch();
            // Plinth under the ring, and a gold rim at the skirt's foot.
            var plinth = new LatheMesh(c) { Step = 4f };
            plinth.Revolve(new[] { new Vector2(CouncilOuter - 0.08f, 0f), new Vector2(CouncilOuter - 0.08f, 0.08f), new Vector2(CouncilInner + 0.04f, 0.08f),
                new Vector2(CouncilInner + 0.04f, 0f) }, -CouncilArc + 2f, CouncilArc - 2f, false);
            LatheMesh.Part(root, "CouncilPlinth", plinth.ToMesh("SU_HallCouncilPlinth"), m.MarbleDark);
            // Carved posts at the open ends, a gold boss at each.
            foreach (var s in new[] { -1f, 1f })
            {
                var d = LatheMesh.Dir(s * CouncilArc);
                var p = c + d * ((CouncilInner + CouncilOuter) * 0.5f);
                b.Box(p + Vector3.up * 0.42f, new Vector3(0.16f, 0.84f, 0.16f), m.Wood, Quaternion.LookRotation(d));
                b.Box(p + Vector3.up * 0.88f, new Vector3(0.1f, 0.1f, 0.1f), m.Gold, Quaternion.LookRotation(d));
            }

            // Councillors' chairs: carved frames, crimson seats and backs, a gold finial — their backs at 1.15 m,
            // well under the throne's.
            foreach (var deg in Chairs)
            {
                var d = LatheMesh.Dir(deg);
                var at = c + d * ChairR;
                var rot = Quaternion.LookRotation(-d, Vector3.up);
                Vector3 L(float x, float y, float z) => at + rot * new Vector3(x, y, z);
                b.Box(L(0f, 0.42f, 0f), new Vector3(0.5f, 0.07f, 0.48f), m.Wood, rot);
                b.Box(L(0f, 0.465f, 0.02f), new Vector3(0.42f, 0.03f, 0.4f), m.Velvet, rot);
                b.Box(L(0f, 0.8f, -0.22f), new Vector3(0.5f, 0.72f, 0.06f), m.Wood, rot);
                b.Box(L(0f, 0.78f, -0.185f), new Vector3(0.36f, 0.5f, 0.015f), m.Velvet, rot);
                b.Box(L(0f, 1.17f, -0.22f), new Vector3(0.54f, 0.05f, 0.08f), m.Gold, rot);
                b.Box(L(0f, 1.24f, -0.22f), new Vector3(0.08f, 0.1f, 0.08f), m.Gold, rot);
                foreach (var sx in new[] { -0.22f, 0.22f })
                {
                    foreach (var sz in new[] { -0.2f, 0.2f })
                        b.Box(L(sx, 0.19f, sz), new Vector3(0.05f, 0.38f, 0.05f), m.Wood, rot);
                    b.Box(L(sx, 0.62f, 0.02f), new Vector3(0.05f, 0.04f, 0.38f), m.Wood, rot);
                }

                var col = new GameObject("ChairCollider");
                col.transform.SetParent(root, false);
                col.transform.SetLocalPositionAndRotation(L(0f, 0.6f, -0.05f), rot);
                col.AddComponent<BoxCollider>().size = new Vector3(0.52f, 1.2f, 0.56f);
            }

            b.Build(root, "Council");

            // The ring keeps one from walking through it (kept under its top: rays to the map pass over).
            for (var a = -CouncilArc + 8f; a <= CouncilArc - 7f; a += 16f)
            {
                var d = LatheMesh.Dir(a);
                var go = new GameObject("CouncilCollider");
                go.transform.SetParent(root, false);
                go.transform.SetLocalPositionAndRotation(c + d * ((CouncilInner + CouncilOuter) * 0.5f) + Vector3.up * 0.34f, Quaternion.LookRotation(d));
                go.AddComponent<BoxCollider>().size = new Vector3(0.6f, 0.68f, CouncilOuter - CouncilInner);
            }
        }

        // ── Council stalls: the ministers' benches facing across the nave ─────────

        static void BuildStalls(Transform root, Mats m)
        {
            var b = new MeshBatch();
            foreach (var s in new[] { -1f, 1f })
            {
                // One long carved stall per side behind the three posts: a back panel with a gilt cornice and
                // pointed canopies over each seat, a bench, a low front with a book ledge.
                var x = s * 6.35f;
                const float z0 = -2.9f;
                const float z1 = 4.1f;
                var zc = (z0 + z1) * 0.5f;
                var len = z1 - z0;
                b.Box(new Vector3(x, 1.2f, zc), new Vector3(0.12f, 2.4f, len), m.Wood);
                b.Box(new Vector3(x - s * 0.02f, 2.42f, zc), new Vector3(0.22f, 0.08f, len + 0.1f), m.Gold);
                b.Box(new Vector3(x - s * 0.2f, 0.42f, zc), new Vector3(0.42f, 0.08f, len), m.Wood);
                b.Box(new Vector3(x - s * 0.2f, 0.2f, zc), new Vector3(0.38f, 0.4f, len - 0.1f), m.Wood);
                for (var z = z0; z <= z1 + 0.01f; z += len / 3f)
                {
                    b.Box(new Vector3(x - s * 0.12f, 1.25f, z), new Vector3(0.14f, 2.5f, 0.12f), m.Wood);
                    b.Box(new Vector3(x - s * 0.12f, 2.6f, z), new Vector3(0.1f, 0.3f, 0.1f), m.Gold);
                }

                foreach (var post in new[] { 2.9f, 0.6f, -1.7f })
                {
                    // A velvet panel behind each minister, a pointed canopy over it.
                    b.Box(new Vector3(x - s * 0.07f, 1.35f, post), new Vector3(0.01f, 1.6f, 1.6f), m.Velvet);
                    for (var i = 0; i < 6; i++)
                    {
                        var t0 = i / 6f;
                        var t1 = (i + 1) / 6f;
                        foreach (var d in new[] { -1f, 1f })
                            b.Strut(new Vector3(x - s * 0.1f, 2.5f + t0 * 0.8f, post + d * 0.95f * (1f - t0)), new Vector3(x - s * 0.1f, 2.5f + t1 * 0.8f, post + d * 0.95f * (1f - t1)),
                                0.06f, m.Wood);
                    }

                    b.Box(new Vector3(x - s * 0.1f, 3.35f, post), new Vector3(0.08f, 0.14f, 0.08f), m.Gold);
                }
            }

            b.Build(root, "CouncilStalls");
            foreach (var s in new[] { -1f, 1f })
            {
                var col = new GameObject("StallCollider");
                col.transform.SetParent(root, false);
                col.transform.localPosition = new Vector3(s * 6.45f, 1.2f, 0.6f);
                col.AddComponent<BoxCollider>().size = new Vector3(0.4f, 2.4f, 7f);
            }
        }

        // ── Narthex: the door's arch, the rose over it, guardian statues ──────────

        static void BuildNarthex(Transform root, Mats m)
        {
            var wall = new LatheMesh(Vector3.zero) { UvOf = WallUv };
            var reveal = new LatheMesh(Vector3.zero) { UvOf = WallUv };
            var gold = new LatheMesh(Vector3.zero);
            // The rose window high over the door (the RoomDoor stands in its own portal on the solid wall).
            var ops = new List<Opening> { Rose(WallX, 8.4f, 1.9f) };
            float Top(float u)
            {
                var x = Mathf.Abs(u - WallX);
                return x <= NaveX ? VaultY(x) : Mathf.Lerp(TribuneTop + 0.6f, TribuneTop, (x - NaveX) / (WallX - NaveX));
            }

            FlatWall(wall, reveal, gold, new Vector3(-WallX, 0f, NarthexZ), Vector3.right, Vector3.forward, WallX * 2f, Top, ops, 0.5f);
            LatheMesh.Part(root, "NarthexWall", wall.ToMesh("SU_HallNarthex"), m.Stone);
            LatheMesh.Part(root, "NarthexReveals", reveal.ToMesh("SU_HallNarthexReveals"), m.StoneDark);
            LatheMesh.Part(root, "NarthexTracery", gold.ToMesh("SU_HallNarthexTracery"), m.GoldLeaf);

            var b = new MeshBatch();
            // The portal round the door: clustered jambs, a pointed hood, a crimson tympanum with a gold sun, the
            // gold keystone.
            const float pj = 1.5f;
            foreach (var s in new[] { -1f, 1f })
            {
                b.Box(new Vector3(s * pj, 1.45f, NarthexZ + 0.14f), new Vector3(0.34f, 2.9f, 0.28f), m.Stone);
                b.Tube(new Vector3(s * (pj - 0.2f), 1.45f, NarthexZ + 0.2f), Vector3.up, 0.06f, 2.9f, m.Stone);
                b.Box(new Vector3(s * pj, 2.92f, NarthexZ + 0.16f), new Vector3(0.42f, 0.1f, 0.32f), m.Gold);
            }

            var tym = new LatheMesh(Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var t0 = i / 8f;
                var t1 = (i + 1) / 8f;
                float Y(float t) => 2.95f + Mathf.Sin(t * Mathf.PI * 0.5f) * 1.25f;
                foreach (var s in new[] { -1f, 1f })
                {
                    b.Strut(new Vector3(s * pj * (1f - t0), Y(t0), NarthexZ + 0.14f), new Vector3(s * pj * (1f - t1), Y(t1), NarthexZ + 0.14f), 0.28f, m.Stone);
                    tym.Quad(new Vector3(s * pj * (1f - t0), 2.95f, NarthexZ + 0.03f), new Vector3(s * pj * (1f - t1), 2.95f, NarthexZ + 0.03f),
                        new Vector3(s * pj * (1f - t1), Y(t1), NarthexZ + 0.03f), new Vector3(s * pj * (1f - t0), Y(t0), NarthexZ + 0.03f), Vector3.forward, 1f);
                }
            }

            LatheMesh.Part(root, "Tympanum", tym.ToMesh("SU_HallTympanum"), m.Velvet);
            var sun = new LatheMesh(new Vector3(0f, 3.45f, NarthexZ + 0.04f));
            for (var k = 0; k < 12; k++)
            {
                var a = k * Mathf.PI / 6f;
                var d = new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f);
                var n = new Vector3(d.y, -d.x, 0f) * 0.05f;
                var o = sun.Centre;
                sun.Tri(o + d * 0.18f - n, o + d * (k % 2 == 0 ? 0.55f : 0.4f), o + d * 0.18f + n, Vector3.forward, 1f);
            }

            for (var k = 0; k < 16; k++)
            {
                var a0 = k * Mathf.PI / 8f;
                var a1 = (k + 1) * Mathf.PI / 8f;
                sun.Tri(sun.Centre, sun.Centre + new Vector3(Mathf.Sin(a0), Mathf.Cos(a0), 0f) * 0.17f, sun.Centre + new Vector3(Mathf.Sin(a1), Mathf.Cos(a1), 0f) * 0.17f,
                    Vector3.forward, 1f);
            }
            LatheMesh.Part(root, "TympanumSun", sun.ToMesh("SU_HallTympanumSun"), m.Gold);
            b.Box(new Vector3(0f, 4.2f, NarthexZ + 0.26f), new Vector3(0.26f, 0.36f, 0.1f), m.Gold);
            b.Box(new Vector3(0f, 4.5f, NarthexZ + 0.06f), new Vector3(5.2f, 0.08f, 0.04f), m.Gold);

            // Two guardians on plinths either side of the door: robed figures with spears, gilt helms.
            foreach (var s in new[] { -1f, 1f })
            {
                var at = new Vector3(s * 5.4f, 0f, NarthexZ + 0.9f);
                b.Box(at + Vector3.up * 0.35f, new Vector3(0.9f, 0.7f, 0.9f), m.StoneDark);
                b.Box(at + Vector3.up * 0.72f, new Vector3(0.96f, 0.05f, 0.96f), m.Gold);
                var statue = new LatheMesh(at + Vector3.up * 0.75f) { Step = 15f };
                statue.Revolve(new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.38f, 0f), new Vector2(0.34f, 0.6f), new Vector2(0.26f, 1.2f), new Vector2(0.22f, 1.5f),
                    new Vector2(0.3f, 1.75f), new Vector2(0.27f, 1.95f), new Vector2(0.1f, 2.02f), new Vector2(0.13f, 2.15f), new Vector2(0.12f, 2.3f),
                    new Vector2(0f, 2.36f)
                }, 0f, 360f, false);
                LatheMesh.Part(root, "Guardian", statue.ToMesh("SU_HallGuardian"), m.Stone);
                b.Box(at + Vector3.up * (0.75f + 2.26f), new Vector3(0.26f, 0.12f, 0.26f), m.Gold);
                b.Tube(at + new Vector3(-s * 0.38f, 0.75f + 1.6f, 0.1f), Vector3.up, 0.025f, 3.0f, m.Bronze);
                b.Box(at + new Vector3(-s * 0.38f, 0.75f + 3.2f, 0.1f), new Vector3(0.08f, 0.24f, 0.02f), m.Gold);
            }

            // Planters with trees in the aisles' narthex corners.
            foreach (var s in new[] { -1f, 1f })
            {
                var at = new Vector3(s * 8.2f, 0f, NarthexZ + 0.9f);
                b.Box(at + Vector3.up * 0.32f, new Vector3(0.8f, 0.64f, 0.8f), m.StoneDark);
                b.Box(at + Vector3.up * 0.65f, new Vector3(0.84f, 0.04f, 0.84f), m.Gold);
                b.Tube(at + Vector3.up * 1.2f, Vector3.up, 0.06f, 1.1f, m.Bronze);
                for (var t = 0; t < 3; t++)
                    b.Add(Stations.CitadelGallery.Cone(), Matrix4x4.TRS(at + Vector3.up * (1.3f + t * 0.45f), Quaternion.Euler(0f, t * 25f, 0f),
                        new Vector3(0.9f - t * 0.2f, 0.65f, 0.9f - t * 0.2f)), m.Leaf);
            }

            b.Build(root, "Narthex");
        }

        // ── Chandeliers and the astrolabe over the map floor ─────────────────────

        static void BuildLights(Transform root, Mats m)
        {
            var b = new MeshBatch();
            foreach (var z in new[] { -6.9f, 3.6f })
            {
                var c = new Vector3(0f, 8.2f, z);
                b.Tube(new Vector3(0f, (8.2f + VaultTop) * 0.5f, z), Vector3.up, 0.02f, VaultTop - 8.2f, m.Gold);
                var ring = new LatheMesh(c) { Step = 7.5f };
                ring.Revolve(new[] { new Vector2(1.25f, 0f), new Vector2(1.25f, 0.12f), new Vector2(1.1f, 0.12f), new Vector2(1.1f, 0f), new Vector2(1.25f, 0f) }, 0f, 360f, false);
                ring.Revolve(new[] { new Vector2(0.7f, -0.6f), new Vector2(0.7f, -0.52f), new Vector2(0.6f, -0.52f), new Vector2(0.6f, -0.6f), new Vector2(0.7f, -0.6f) },
                    0f, 360f, false);
                LatheMesh.Part(root, "Chandelier", ring.ToMesh("SU_HallChandelier"), m.Gold);
                for (var k = 0; k < 12; k++)
                {
                    var d = LatheMesh.Dir(k * 30f);
                    b.Strut(c + d * 1.18f + Vector3.up * 0.12f, new Vector3(0f, 9.4f, z), 0.012f, m.Gold);
                    b.Box(c + d * 1.18f + Vector3.up * 0.2f, new Vector3(0.05f, 0.14f, 0.05f), m.Light);
                    if (k % 2 == 0)
                        b.Box(c + d * 0.65f - Vector3.up * 0.48f, new Vector3(0.04f, 0.12f, 0.04f), m.Light);
                }
            }

            b.Build(root, "Chandeliers");

            // The astrolabe turning high over the map floor.
            var astro = new GameObject("Astrolabe").transform;
            astro.SetParent(root, false);
            astro.localPosition = new Vector3(0f, 7.6f, WorldScale.CicTableCenterZ);
            var chain = new LatheMesh(Vector3.zero) { Step = 45f };
            chain.Revolve(new[] { new Vector2(0.015f, 0.9f), new Vector2(0.015f, VaultTop - 7.6f) }, 0f, 360f, false);
            LatheMesh.Part(astro, "Chain", chain.ToMesh("SU_HallAstroChain"), m.Gold);
            var spin = astro.gameObject.AddComponent<AstrolabeSpin>();
            spin.Rings = new Transform[3];
            for (var i = 0; i < 3; i++)
            {
                var ringRoot = new GameObject("Ring" + i).transform;
                ringRoot.SetParent(astro, false);
                ringRoot.localRotation = Quaternion.Euler(i * 55f + 15f, i * 40f, 0f);
                var ring = new LatheMesh(Vector3.zero) { Step = 6f };
                var r = 0.95f - i * 0.2f;
                ring.Revolve(new[] { new Vector2(r + 0.03f, -0.025f), new Vector2(r + 0.03f, 0.025f), new Vector2(r - 0.03f, 0.025f), new Vector2(r - 0.03f, -0.025f),
                    new Vector2(r + 0.03f, -0.025f) }, 0f, 360f, false);
                LatheMesh.Part(ringRoot, "Band", ring.ToMesh("SU_HallAstroRing" + i), m.Gold);
                spin.Rings[i] = ringRoot;
            }

            var heart = new LatheMesh(Vector3.zero) { Step = 30f };
            heart.Revolve(new[] { new Vector2(0f, -0.18f), new Vector2(0.14f, 0f), new Vector2(0f, 0.18f) }, 0f, 360f, false);
            LatheMesh.Part(astro, "Heart", heart.ToMesh("SU_HallAstroHeart"), m.Light);
        }

        // ── Colliders: the walls, the apse, the piers ────────────────────────────

        static void BuildHull(Transform root)
        {
            void Solid(Vector3 c, Vector3 size)
            {
                var go = new GameObject("HullCollider");
                go.transform.SetParent(root, false);
                go.transform.localPosition = c;
                go.AddComponent<BoxCollider>().size = size;
            }

            var len = ApseZ - NarthexZ;
            foreach (var s in new[] { -1f, 1f })
            {
                Solid(new Vector3(s * (WallX + 0.3f), 3f, (NarthexZ + ApseZ) * 0.5f), new Vector3(0.6f, 6f, len + 1f));
                foreach (var z in Piers)
                    if (z > NarthexZ + 0.01f && z < ApseZ - 0.01f)
                        Solid(new Vector3(s * (NaveX - 0.1f), 2f, z), new Vector3(0.7f, 4f, 0.7f));
            }

            Solid(new Vector3(0f, 3f, NarthexZ - 0.3f), new Vector3(WallX * 2f + 1f, 6f, 0.6f));
            for (var a = -90f; a <= 90f; a += 15f)
            {
                var go = new GameObject("HullCollider");
                go.transform.SetParent(root, false);
                var d = LatheMesh.Dir(a);
                go.transform.localPosition = new Vector3(0f, 3f, ApseZ) + d * (ApseR + 0.3f);
                go.transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
                go.AddComponent<BoxCollider>().size = new Vector3(1.6f, 6f, 0.6f);
            }

            // The aisles' back corners at the apse chord.
            foreach (var s in new[] { -1f, 1f })
                Solid(new Vector3(s * (ApseR + WallX) * 0.5f, 3f, ApseZ + 0.3f), new Vector3(WallX - ApseR + 0.4f, 6f, 0.6f));
        }
    }

    /// <summary>The astrolabe's rings, each turning slowly about its own axis.</summary>
    public sealed class AstrolabeSpin : MonoBehaviour
    {
        public Transform[] Rings;

        void Update()
        {
            if (Rings == null)
                return;
            var dt = Time.deltaTime;
            for (var i = 0; i < Rings.Length; i++)
                if (Rings[i] != null)
                    Rings[i].Rotate(Vector3.up, (6f + i * 4f) * (i % 2 == 0 ? 1f : -1f) * dt, Space.Self);
        }
    }

    /// <summary>The braziers' flames: a slow uneven breathing of their height (no lights: the room's own light them).</summary>
    public sealed class BrazierFlicker : MonoBehaviour
    {
        public Transform[] Flames;

        void Update()
        {
            if (Flames == null)
                return;
            var t = Time.time;
            for (var i = 0; i < Flames.Length; i++)
            {
                var f = Flames[i];
                if (f == null)
                    continue;
                var s = 1f + 0.12f * Mathf.Sin(t * 7.3f + i * 1.9f) + 0.07f * Mathf.Sin(t * 13.1f + i * 0.7f);
                f.localScale = new Vector3(1f, s, 1f);
            }
        }
    }
}
