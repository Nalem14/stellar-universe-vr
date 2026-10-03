using Core.UI;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The station's concourse: the corridor's other body, a calm hall in the habitat ring where people pass,
    /// sit and watch the stars. One long wall is a leaning bay window on the hub (the spokes, the far side of
    /// the ring, the planet), with a low step to stand on against the glass and benches facing it; the other wall
    /// carries the doors in a row of 3 m bays, two of them free for rooms to come (a light garden stands in each
    /// until then). A soft vault, warm cove light, fibre-light planters, a plank deck. Same local frame as the ship corridor: +z from the bridge
    /// door (z = 0) to the gate (z = <see cref="Length"/>), window on −x (toward the hub), doors on +x.
    /// Two hull materials, a few emissive ones, under twenty draw calls.
    /// </summary>
    public static class StationConcourse
    {
        public const float Length = 24f;
        public const float HalfWidth = 3.4f;
        const float WallTop = 3.2f;
        /// <summary>The window step: low enough to walk onto and stand against the glass.</summary>
        const float Sill = 0.16f;
        const float Head = 3.6f;
        const float Lean = 0.3f;
        const float Bay = 3f;

        /// <summary>Door bays on the +x wall (centres, z). Rooms use 0, 1, 3, 4; 2 and 5 wait for new rooms.</summary>
        static readonly float[] Bays = { 4.5f, 7.5f, 10.5f, 13.5f, 16.5f, 19.5f };
        static readonly int[] FreeBays = { 2, 5 };
        static readonly float[] Benches = { 6f, 12f, 18f };
        static readonly Vector2[] Planters = { new(-0.9f, 9f), new(-0.9f, 15f), new(2.3f, 1.6f), new(2.3f, 22.4f) };

        /// <summary>Warm, low hall light (the ship corridor is cool and bright).</summary>
        public static readonly Color Warm = new(1f, 0.88f, 0.74f, 1f);

        /// <summary>Hall-local floor height under the ring's centre line, and the hall's centre line radius from the hub.</summary>
        public const float FloorBelowDeck = 8.4f;
        public static float CentreRadius => WorldScale.StationRingRadius - WorldScale.StationRingSection.x * 0.5f + 0.5f + HalfWidth;
        /// <summary>Bearing of the hall on the ring (degrees from the station's bow): the planet stands clear of the hub.</summary>
        public const float Bearing = 272f;

        public static (Vector3 pos, float yaw) DoorPose(CorridorRoom.Slot slot) => slot switch
        {
            CorridorRoom.Slot.LabPort => (OnDoorWall(Bays[0]), -90f),
            CorridorRoom.Slot.DockStarboard => (OnDoorWall(Bays[1]), -90f),
            CorridorRoom.Slot.DiplomacyPort => (OnDoorWall(Bays[3]), -90f),
            CorridorRoom.Slot.QuartersStarboard => (OnDoorWall(Bays[4]), -90f),
            _ => (new Vector3(0f, 0f, Length - 0.12f), 180f)
        };

        static Vector3 OnDoorWall(float z) => new(HalfWidth - 0.12f, 0f, z);

        /// <summary>Where the hall's crew hand stops (hall-local), and which way they look.</summary>
        public static readonly (Vector3 at, Vector3 facing, bool post)[] CrewSpots =
        {
            (new Vector3(-2.3f, 0f, 2.6f), Vector3.left, false),
            (new Vector3(-2.4f, 0f, 9.9f), new Vector3(-1f, 0f, 0.4f), false),
            (new Vector3(2.4f, 0f, 10.4f), Vector3.right, true),
            (new Vector3(1.8f, 0f, 19.5f), Vector3.right, true),
            (new Vector3(-2.3f, 0f, 21.6f), new Vector3(-1f, 0f, -0.3f), false)
        };

        /// <summary>The hall's pose in the bridge mount's frame (the hub's axis is the rotunda's centre).</summary>
        public static Pose MountPose()
        {
            var right = LatheMesh.Dir(Bearing);
            var forward = Vector3.Cross(right, Vector3.up);
            var mid = StationCommandShell.Centre + right * CentreRadius + Vector3.down * FloorBelowDeck;
            return new Pose(mid - forward * (Length * 0.5f), Quaternion.LookRotation(forward, Vector3.up));
        }

        public static Transform Build(Transform hall, CicArtKit art)
        {
            var root = new GameObject("Concourse").transform;
            root.SetParent(hall, false);

            var panel = StationSurfaces.Panel();
            var wall = art.Hull(panel, new Color(0.7f, 0.69f, 0.67f), new Vector2(1.5f, 0.8f), seam: 0.3f, lift: 0.08f);
            var vault = art.Hull(panel, new Color(0.62f, 0.63f, 0.65f), new Vector2(Bay, 0.9f), seam: 0.25f, lift: 0.1f);
            var deck = art.Hull(StationSurfaces.Planks(), new Color(0.74f, 0.6f, 0.47f), new Vector2(40f, 40f), tiling: 0.5f, seam: 0f,
                lift: 0.07f);
            var frame = art.Hull(panel, new Color(0.3f, 0.31f, 0.34f), new Vector2(40f, 40f), seam: 0f, lift: 0.08f);
            var seat = art.Hull(panel, new Color(0.62f, 0.46f, 0.34f), new Vector2(40f, 40f), seam: 0f, lift: 0.12f);
            var warm = art.Lit(Texture2D.whiteTexture, Warm, 1.0f);
            var cool = art.Lit(Texture2D.whiteTexture, StationCommandShell.Glow, 1.6f);
            var dim = art.Lit(Texture2D.whiteTexture, new Color(0.03f, 0.05f, 0.07f), 0.3f);

            BuildShell(root, wall, vault, deck, frame, warm, cool);
            BuildFurniture(root, seat, frame, warm, cool);
            BuildFreeBays(root, frame, dim, cool, warm);
            BuildHull(root);
            root.gameObject.SetActive(false);
            return root;
        }

        // ── Shell ───────────────────────────────────────────────────────────────

        /// <summary>The vault from the door wall's top over to the window head (x, y).</summary>
        static readonly Vector2[] Vault =
        {
            new(HalfWidth, WallTop), new(HalfWidth - 0.5f, 3.72f), new(2.1f, 4.08f), new(0.9f, 4.28f), new(-0.5f, 4.34f),
            new(-1.9f, 4.24f), new(-2.9f, 4.06f), new(-HalfWidth - Lean, Head + 0.4f)
        };

        static void BuildShell(Transform root, Material wall, Material vault, Material deck, Material frame, Material warm, Material cool)
        {
            var floor = new LatheMesh(Vector3.zero);
            floor.Quad(new Vector3(-HalfWidth + 0.6f, 0f, 0f), new Vector3(HalfWidth, 0f, 0f), new Vector3(HalfWidth, 0f, Length),
                new Vector3(-HalfWidth + 0.6f, 0f, Length), Vector3.up, 1f);
            LatheMesh.Part(root, "HallFloor", floor.ToMesh("SU_ConcourseFloor"), deck, collider: true);

            var walls = new LatheMesh(Vector3.zero);
            // Door wall: bays framed by pilasters (built with the frames), panels between them.
            walls.Quad(new Vector3(HalfWidth, 0f, 0f), new Vector3(HalfWidth, 0f, Length), new Vector3(HalfWidth, WallTop, Length),
                new Vector3(HalfWidth, WallTop, 0f), Vector3.left, 1f);
            // End walls, closed over the vault and down to the sill.
            foreach (var (z, n) in new[] { (0f, Vector3.forward), (Length, Vector3.back) })
            {
                var c = new Vector3(0f, 2f, z);
                var outline = new System.Collections.Generic.List<Vector3>
                {
                    new(-HalfWidth, 0f, z), new(HalfWidth, 0f, z)
                };
                foreach (var p in Vault)
                    outline.Add(new Vector3(p.x, p.y, z));
                outline.Add(new Vector3(-HalfWidth - Lean, Sill, z));
                outline.Add(new Vector3(-HalfWidth, Sill, z));
                for (var i = 0; i < outline.Count; i++)
                    walls.Tri(c, outline[i], outline[(i + 1) % outline.Count], n, 1f);
            }

            LatheMesh.Part(root, "HallWalls", walls.ToMesh("SU_ConcourseWalls"), wall, collider: true);

            var v = new LatheMesh(Vector3.zero);
            for (var i = 0; i < Vault.Length - 1; i++)
            {
                var a = Vault[i];
                var b = Vault[i + 1];
                var down = new Vector3(-(b.y - a.y), -(a.x - b.x), 0f);
                if (down.y > 0f)
                    down = -down;
                v.Quad(new Vector3(a.x, a.y, 0f), new Vector3(a.x, a.y, Length), new Vector3(b.x, b.y, Length), new Vector3(b.x, b.y, 0f),
                    down, 1f);
            }

            LatheMesh.Part(root, "HallVault", v.ToMesh("SU_ConcourseVault"), vault);

            // Window side: the step up to the glass (walk on it, stand against the view), the head beam, slim
            // leaning mullions.
            var f = new LatheMesh(Vector3.zero);
            Box(f, new Vector3(-HalfWidth - Lean * 0.5f, Sill * 0.5f, Length * 0.5f), new Vector3(1.2f + Lean, Sill, Length));
            var step = new GameObject("SillFloor");
            step.transform.SetParent(root, false);
            step.transform.localPosition = new Vector3(-HalfWidth - Lean * 0.5f, Sill * 0.5f, Length * 0.5f);
            step.AddComponent<BoxCollider>().size = new Vector3(1.2f + Lean, Sill, Length);
            Box(f, new Vector3(-HalfWidth - Lean * 0.5f, Head + 0.2f, Length * 0.5f), new Vector3(0.5f + Lean, 0.4f, Length));
            for (var z = Bay; z < Length - 0.1f; z += Bay)
            {
                Leaning(f, z, 0.07f, 0.12f);
                // Ribs across the vault and pilasters down the door wall, on the same 3 m beat.
                for (var i = 0; i < Vault.Length - 2; i++)
                {
                    var a = Vault[i];
                    var b = Vault[i + 1];
                    var n = new Vector3(-(b.y - a.y), -(a.x - b.x), 0f).normalized;
                    if (n.y > 0f)
                        n = -n;
                    var pa = new Vector3(a.x, a.y, z) + n * 0.1f;
                    var pb = new Vector3(b.x, b.y, z) + n * 0.1f;
                    f.Quad(pa + Vector3.back * 0.09f, pa + Vector3.forward * 0.09f, pb + Vector3.forward * 0.09f, pb + Vector3.back * 0.09f, n, 1f);
                    f.Quad(new Vector3(a.x, a.y, z - 0.09f), pa + Vector3.back * 0.09f, pb + Vector3.back * 0.09f, new Vector3(b.x, b.y, z - 0.09f),
                        Vector3.back, 0.7f);
                    f.Quad(new Vector3(a.x, a.y, z + 0.09f), pa + Vector3.forward * 0.09f, pb + Vector3.forward * 0.09f,
                        new Vector3(b.x, b.y, z + 0.09f), Vector3.forward, 0.7f);
                }

                if (z > 1f && z < Length - 1f)
                    Box(f, new Vector3(HalfWidth - 0.11f, WallTop * 0.5f, z), new Vector3(0.22f, WallTop, 0.3f));
            }

            // Door-wall frieze over the bays (signs and lintels sit under it).
            Box(f, new Vector3(HalfWidth - 0.08f, WallTop - 0.2f, Length * 0.5f), new Vector3(0.16f, 0.4f, Length - 0.2f));
            LatheMesh.Part(root, "HallFrames", f.ToMesh("SU_ConcourseFrames"), frame);

            // Light: a warm cove along the door wall's top, a cool line on the sill's lip (the window's own glow),
            // a soft floor line along the window, and the mullions' inner edges.
            var w = new LatheMesh(Vector3.zero);
            w.Quad(new Vector3(HalfWidth - 0.17f, WallTop + 0.02f, 0.2f), new Vector3(HalfWidth - 0.17f, WallTop + 0.02f, Length - 0.2f),
                new Vector3(HalfWidth - 0.24f, WallTop + 0.1f, Length - 0.2f), new Vector3(HalfWidth - 0.24f, WallTop + 0.1f, 0.2f),
                new Vector3(-1f, -1f, 0f), 1f);
            for (var z = 0.3f; z < Length - 0.5f; z += Bay)
                w.Quad(new Vector3(HalfWidth - 0.165f, 2.83f, z), new Vector3(HalfWidth - 0.165f, 2.83f, z + Bay - 0.6f),
                    new Vector3(HalfWidth - 0.165f, 2.86f, z + Bay - 0.6f), new Vector3(HalfWidth - 0.165f, 2.86f, z), Vector3.left, 1f);
            LatheMesh.Part(root, "GlowCove", w.ToMesh("SU_ConcourseCove"), warm);

            var c2 = new LatheMesh(Vector3.zero);
            c2.Quad(new Vector3(-HalfWidth + 0.595f, Sill - 0.06f, 0.15f), new Vector3(-HalfWidth + 0.595f, Sill - 0.06f, Length - 0.15f),
                new Vector3(-HalfWidth + 0.595f, Sill - 0.035f, Length - 0.15f), new Vector3(-HalfWidth + 0.595f, Sill - 0.035f, 0.15f), Vector3.right, 1f);
            c2.Quad(new Vector3(-HalfWidth + 0.9f, 0.004f, 0.4f), new Vector3(-HalfWidth + 0.9f, 0.004f, Length - 0.4f),
                new Vector3(-HalfWidth + 0.94f, 0.004f, Length - 0.4f), new Vector3(-HalfWidth + 0.94f, 0.004f, 0.4f), Vector3.up, 1f);
            for (var z = Bay; z < Length - 0.1f; z += Bay)
                Leaning(c2, z, 0.012f, 0.02f, 0.075f);
            LatheMesh.Part(root, "GlowSill", c2.ToMesh("SU_ConcourseSill"), cool);
        }

        /// <summary>A mullion leaning out with the glass from the sill to the head (half-width, half-depth, inset toward the hall).</summary>
        static void Leaning(LatheMesh m, float z, float hw, float hd, float inset = 0f)
        {
            var b = new Vector3(-HalfWidth + hd + inset, Sill, z);
            var t = new Vector3(-HalfWidth - Lean + hd + inset, Head, z);
            var bx = new Vector3(-hd * 2f, 0f, 0f);
            var dz = new Vector3(0f, 0f, hw);
            m.Quad(b - dz, b + dz, t + dz, t - dz, Vector3.right, 1f);
            if (inset > 0f)
                return;
            m.Quad(b - dz, b - dz + bx, t - dz + bx, t - dz, Vector3.back, 0.7f);
            m.Quad(b + dz, b + dz + bx, t + dz + bx, t + dz, Vector3.forward, 0.7f);
        }

        static void Box(LatheMesh m, Vector3 c, Vector3 size)
        {
            var h = size * 0.5f;
            Vector3 P(float x, float y, float z) => c + new Vector3(x * h.x, y * h.y, z * h.z);
            m.Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up, 1f);
            m.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down, 0.6f);
            m.Quad(P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), Vector3.right, 0.85f);
            m.Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), Vector3.left, 0.85f);
            m.Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), Vector3.forward, 0.8f);
            m.Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), Vector3.back, 0.8f);
        }

        // ── Benches and light gardens ───────────────────────────────────────────

        static void BuildFurniture(Transform root, Material seat, Material frame, Material warm, Material cool)
        {
            var benchMesh = UiMeshes.RoundedBox(new Vector3(0.62f, 0.12f, 3.4f), 0.05f);
            var backMesh = UiMeshes.RoundedBox(new Vector3(0.1f, 0.36f, 3.4f), 0.04f);
            var plinth = UiMeshes.RoundedBox(new Vector3(0.44f, 0.36f, 0.24f), 0.03f);
            var under = UiMeshes.RoundedBox(new Vector3(0.5f, 0.012f, 3.2f), 0.004f);
            foreach (var z in Benches)
            {
                var b = new GameObject("Bench").transform;
                b.SetParent(root, false);
                b.localPosition = new Vector3(-0.9f, 0f, z);
                UiKit.MeshPiece(b, "Seat", benchMesh, seat, new Vector3(0f, 0.42f, 0f));
                // Low back on the hall side: you sit facing the window.
                UiKit.MeshPiece(b, "Back", backMesh, seat, new Vector3(0.3f, 0.66f, 0f));
                UiKit.MeshPiece(b, "PlinthA", plinth, frame, new Vector3(0f, 0.18f, -1.2f));
                UiKit.MeshPiece(b, "PlinthB", plinth, frame, new Vector3(0f, 0.18f, 1.2f));
                UiKit.MeshPiece(b, "Underglow", under, warm, new Vector3(0f, 0.355f, 0f));
                b.gameObject.AddComponent<BoxCollider>().center = new Vector3(0f, 0.4f, 0f);
                b.GetComponent<BoxCollider>().size = new Vector3(0.7f, 0.8f, 3.4f);
            }

            // Light gardens: a planter of fibre stems, each tipped with a soft point of light.
            var tub = UiMeshes.RoundedBox(new Vector3(0.9f, 0.46f, 0.9f), 0.12f);
            var stems = new LatheMesh(Vector3.zero);
            var tips = new LatheMesh(Vector3.zero);
            var seed = 7;
            float Rand()
            {
                seed = seed * 1103515245 + 12345 & 0x7fffffff;
                return seed / (float)0x7fffffff;
            }

            foreach (var at in Planters)
            {
                var c = new Vector3(at.x, 0.46f, at.y);
                for (var k = 0; k < 34; k++)
                {
                    var a = Rand() * Mathf.PI * 2f;
                    var r = Mathf.Sqrt(Rand()) * 0.34f;
                    var foot = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    var h = 0.5f + Rand() * 0.9f;
                    var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (0.08f + r * 0.5f);
                    var top = foot + Vector3.up * h + lean;
                    var side = Vector3.Cross(top - foot, Vector3.up).normalized * 0.006f;
                    stems.Quad(foot - side, foot + side, top + side, top - side, Vector3.Cross(side, top - foot), 1f);
                    var s = 0.016f + Rand() * 0.012f;
                    tips.Quad(top + new Vector3(-s, 0f, 0f), top + new Vector3(0f, s, 0f), top + new Vector3(s, 0f, 0f), top + new Vector3(0f, -s, 0f),
                        Vector3.forward, 1f);
                    tips.Quad(top + new Vector3(0f, 0f, -s), top + new Vector3(0f, s, 0f), top + new Vector3(0f, 0f, s), top + new Vector3(0f, -s, 0f),
                        Vector3.right, 1f);
                }

                var p = UiKit.MeshPiece(root, "Planter", tub, frame, new Vector3(at.x, 0.23f, at.y));
                p.AddComponent<BoxCollider>().size = new Vector3(0.9f, 0.46f, 0.9f);
            }

            DoubleSided(LatheMesh.Part(root, "GardenStems", stems.ToMesh("SU_ConcourseStems"), warm));
            DoubleSided(LatheMesh.Part(root, "GardenTips", tips.ToMesh("SU_ConcourseTips"), cool));
        }

        /// <summary>The stems and tips are flat cards: draw their mesh again flipped (one renderer, two submeshes would cost the same).</summary>
        static void DoubleSided(GameObject part)
        {
            var mf = part.GetComponent<MeshFilter>();
            var mesh = mf.sharedMesh;
            var tris = mesh.triangles;
            var all = new int[tris.Length * 2];
            tris.CopyTo(all, 0);
            for (var i = 0; i < tris.Length; i += 3)
            {
                all[tris.Length + i] = tris[i];
                all[tris.Length + i + 1] = tris[i + 2];
                all[tris.Length + i + 2] = tris[i + 1];
            }

            mesh.triangles = all;
        }

        /// <summary>The bays no room uses yet: a recessed light-fall (a curtain of fibres over a dark back).</summary>
        static void BuildFreeBays(Transform root, Material frame, Material dim, Material cool, Material warm)
        {
            var back = new LatheMesh(Vector3.zero);
            var fall = new LatheMesh(Vector3.zero);
            var trim = new LatheMesh(Vector3.zero);
            var seed = 31;
            float Rand()
            {
                seed = seed * 1103515245 + 12345 & 0x7fffffff;
                return seed / (float)0x7fffffff;
            }

            foreach (var i in FreeBays)
            {
                var z = Bays[i];
                var x = HalfWidth - 0.015f;
                back.Quad(new Vector3(x, 0.2f, z - 1.1f), new Vector3(x, 0.2f, z + 1.1f), new Vector3(x, 2.7f, z + 1.1f), new Vector3(x, 2.7f, z - 1.1f),
                    Vector3.left, 1f);
                for (var k = 0; k < 46; k++)
                {
                    var fz = z - 1f + k * (2f / 45f) + (Rand() - 0.5f) * 0.02f;
                    var y0 = 0.35f + Rand() * 0.9f;
                    var fx = x - 0.02f - Rand() * 0.05f;
                    fall.Quad(new Vector3(fx, y0, fz - 0.004f), new Vector3(fx, y0, fz + 0.004f), new Vector3(fx, 2.62f, fz + 0.004f),
                        new Vector3(fx, 2.62f, fz - 0.004f), Vector3.left, 1f);
                }

                // A low ledge in front (a place to stop) and a warm lintel line.
                Box(trim, new Vector3(HalfWidth - 0.2f, 0.1f, z), new Vector3(0.4f, 0.2f, 2.3f));
            }

            LatheMesh.Part(root, "BayBacks", back.ToMesh("SU_ConcourseBayBack"), dim);
            LatheMesh.Part(root, "BayFall", fall.ToMesh("SU_ConcourseBayFall"), cool);
            LatheMesh.Part(root, "BayLedges", trim.ToMesh("SU_ConcourseBayLedge"), frame);
            _ = warm;
        }

        /// <summary>Solid boxes behind the walls and the glass: a body never slips out of the hall.</summary>
        static void BuildHull(Transform root)
        {
            void Solid(Vector3 c, Vector3 size)
            {
                var go = new GameObject("HullCollider");
                go.transform.SetParent(root, false);
                go.transform.localPosition = c;
                go.AddComponent<BoxCollider>().size = size;
            }

            Solid(new Vector3(HalfWidth + 0.3f, 2f, Length * 0.5f), new Vector3(0.6f, 4.4f, Length + 1f));
            // The glass leans out from the step's edge: stop the body at its foot.
            Solid(new Vector3(-HalfWidth - 0.38f, 2.2f, Length * 0.5f), new Vector3(0.6f, 4.4f, Length + 1f));
            Solid(new Vector3(0f, 2f, -0.3f), new Vector3(HalfWidth * 2f + 1.5f, 4.4f, 0.6f));
            Solid(new Vector3(0f, 2f, Length + 0.3f), new Vector3(HalfWidth * 2f + 1.5f, 4.4f, 0.6f));
        }
    }
}
