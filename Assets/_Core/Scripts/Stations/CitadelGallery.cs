using System.Collections.Generic;
using Core.UI;
using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// A city's hall: the citadel's gallery, a floor of the tower one level under the rotunda — nothing like the
    /// ship's passage or the fortress concourse. A gently curved stone gallery runs 153° round the tower's core
    /// inside the crown, on a 12.6 m radius so it reads as a long hall that bends, never as a ring one walks round (no ring, nothing added to the tower: from outside only its arched windows show, lit, in the
    /// crown's flank). The core wall carries the rooms' doors in stone portals with banners between them; the
    /// outer wall is a row of tall arched windows on the city with gold tracery and deep reveals; a ribbed vault
    /// with hanging lanterns; flagstones with a gold line and a rosette at the stair from the rotunda; stone
    /// benches facing the windows, planted trees, and by the stair a model of the citadel under its own light.
    /// Local frame: origin on the tower's axis at the gallery floor, the mount's axes; the gallery runs from
    /// <see cref="A0"/> (the stair to the rotunda) to <see cref="A1"/> (the gate) at <see cref="RMid"/>.
    /// Static geometry merged per material.
    /// </summary>
    public static class CitadelGallery
    {
        /// <summary>Gallery floor under the rotunda's deck (inside the crown, whose head flares to ROut + 0.65 m from −8.6 m up).</summary>
        public const float FloorBelowDeck = 7.8f;
        public const float RIn = 10.6f;
        public const float ROut = 14.6f;
        public const float RMid = 12.6f;
        const float WallTop = 3.6f;
        const float OuterTop = 3.8f;
        const float Sill = 0.8f;
        const float Spring = 2.55f;
        const float WindowWidth = 1.3f;
        public const float BayDeg = 9f;
        public const float A0 = 186f;
        public const float A1 = A0 + 17 * BayDeg;
        public static float Length => (A1 - A0) * Mathf.Deg2Rad * RMid;

        /// <summary>Warm lantern light; gold of the tower's light lines.</summary>
        public static readonly Color Lantern = new(1f, 0.8f, 0.55f, 1f);
        static Color Gold => CityExterior.CitadelGold;

        static readonly Vector2[] Section =
        {
            new(RIn, 0f), new(ROut, 0f), new(ROut, OuterTop), new(ROut - 0.5f, 4.4f), new(RMid, 4.85f), new(RIn + 0.5f, 4.35f),
            new(RIn, WallTop)
        };

        static readonly Vector2[] Vault =
        {
            new(RIn, WallTop), new(RIn + 0.5f, 4.35f), new(RMid, 4.85f), new(ROut - 0.5f, 4.4f), new(ROut, OuterTop)
        };

        /// <summary>Distance along the gallery (at <see cref="RMid"/>) of each room's door; the gate closes the far end.</summary>
        static float SlotZ(CorridorRoom.Slot slot) => slot switch
        {
            CorridorRoom.Slot.LabPort => 5.5f,
            CorridorRoom.Slot.DockStarboard => 10.5f,
            CorridorRoom.Slot.MarketPort => 15.5f,
            CorridorRoom.Slot.DiplomacyPort => 20.5f,
            CorridorRoom.Slot.QuartersStarboard => 25.5f,
            // Between the quarters and the gate (no banner there: the two portals stand close).
            CorridorRoom.Slot.RefineryStarboard => 28.5f,
            _ => Length
        };

        /// <summary>
        /// The citadel's model: by the core wall at the near end, on the right as one comes down from the rotunda
        /// (before the lab's portal) - it used to stand in the walkway right in front of the gate.
        /// </summary>
        const float ModelZ = 3.4f;

        const float ModelR = RIn + 0.95f;

        public static float Angle(float z) => A0 + z / RMid * Mathf.Rad2Deg;

        static Vector3 At(float z, float r, float y = 0f) => LatheMesh.Dir(Angle(z)) * r + Vector3.up * y;

        /// <summary>Along the gallery at <paramref name="z"/> (toward the gate).</summary>
        static Vector3 Ahead(float z) => LatheMesh.Dir(Angle(z) + 90f);

        public static Pose MountPose() =>
            new(StationCommandShell.Centre + Vector3.down * FloorBelowDeck, Quaternion.identity);

        /// <summary>Door poses: on the core wall facing out into the gallery; the gate across the far end.</summary>
        public static (Vector3 pos, float yaw) DoorPose(CorridorRoom.Slot slot)
        {
            if (slot == CorridorRoom.Slot.GateEnd)
            {
                var z = Length - 0.14f;
                return (At(z, RMid), Angle(z) - 90f);
            }

            var s = SlotZ(slot);
            return (At(s, RIn + 0.12f), Angle(s));
        }

        /// <summary>The stair up to the rotunda, across the gallery's near end.</summary>
        public static (Vector3 pos, float yaw) BridgeDoor => (At(0.14f, RMid), Angle(0.14f) + 90f);

        /// <summary>Where one arrives from the rotunda, and which way one looks.</summary>
        public static (Vector3 pos, Vector3 facing) Stand => (At(1.4f, RMid), Ahead(1.4f));

        public static Vector3 LightPos(int i, int count) => At(Length * (i + 0.5f) / count, RMid, 3.7f);

        /// <summary>The gallery's hands: by the windows, at a bench, before the model.</summary>
        public static readonly (Vector3 at, Vector3 facing, bool post)[] CrewSpots = Spots();

        static (Vector3, Vector3, bool)[] Spots()
        {
            (Vector3, Vector3, bool) S(float z, float r, bool outward, bool post)
            {
                var d = LatheMesh.Dir(Angle(z));
                return (d * r, outward ? d : -d, post);
            }

            return new[]
            {
                S(3f, ROut - 0.8f, true, false), S(8f, RIn + 1.6f, false, false), S(13f, ROut - 0.8f, true, true), S(23f, ROut - 0.9f, true, false),
                S(ModelZ, ModelR + 1.25f, false, true)
            };
        }

        // ── Build ───────────────────────────────────────────────────────────────

        public static Transform Build(Transform hall, CicArtKit art)
        {
            var root = new GameObject("CitadelGallery").transform;
            root.SetParent(hall, false);

            var stoneTex = StationSurfaces.Stone();
            var wall = art.Hull(stoneTex, new Color(0.88f, 0.82f, 0.72f), new Vector2(1.2f, 0.6f), seam: 0.3f, lift: 0.1f);
            var vault = art.Hull(stoneTex, new Color(0.93f, 0.89f, 0.81f), new Vector2(2f, 1.2f), seam: 0.2f, lift: 0.14f);
            var floor = art.Hull(stoneTex, new Color(0.6f, 0.54f, 0.48f), new Vector2(1f, 1f), tiling: 0.8f, seam: 0.5f, lift: 0.08f);
            var bronze = art.Hull(StationSurfaces.Panel(), new Color(0.44f, 0.31f, 0.18f), new Vector2(40f, 40f), seam: 0f, lift: 0.12f);
            var trim = art.Hull(stoneTex, new Color(0.74f, 0.66f, 0.55f), new Vector2(40f, 40f), seam: 0f, lift: 0.1f);
            var gold = art.Lit(Texture2D.whiteTexture, Gold, 2.2f);
            // Tracery and the floor line: gold leaf, not light strips.
            var leafGold = art.Lit(Texture2D.whiteTexture, Gold * 0.8f, 0.9f);
            var glow = art.Lit(Texture2D.whiteTexture, Lantern, 3.4f);
            var cloth = art.Lit(Texture2D.whiteTexture, new Color(0.62f, 0.1f, 0.12f), 0.5f);
            var leaf = art.Lit(Texture2D.whiteTexture, new Color(0.24f, 0.55f, 0.3f), 0.6f);
            var dark = art.Lit(Texture2D.whiteTexture, new Color(0.12f, 0.08f, 0.05f), 0.3f);

            BuildShell(root, wall, vault, floor, trim, leafGold, dark);
            BuildWindows(root, wall, trim, leafGold);
            var b = new MeshBatch();
            BuildPortals(b, trim, gold);
            BuildBanners(b, cloth, gold, bronze);
            BuildLanterns(b, bronze, glow);
            BuildFurniture(b, trim, bronze, leaf, gold, glow);
            b.Build(root, "GalleryDressing");
            BuildModel(root, art, trim, gold, glow);
            root.gameObject.SetActive(false);
            return root;
        }

        static void BuildShell(Transform root, Material wall, Material vault, Material floor, Material trim, Material gold, Material dark)
        {
            var f = new LatheMesh(Vector3.zero) { Step = 2f };
            f.Revolve(new[] { new Vector2(RIn, 0f), new Vector2(ROut, 0f) }, A0, A1, true);
            LatheMesh.Part(root, "GalleryFloor", f.ToMesh("SU_GalleryFloor"), floor, collider: true);

            var w = new LatheMesh(Vector3.zero) { Step = 2f };
            w.Revolve(new[] { new Vector2(RIn, 0f), new Vector2(RIn, WallTop) }, A0, A1, false);
            // The two ends: the stair landing's wall and the gate's.
            w.Cap(Section, A0, true);
            w.Cap(Section, A1, false);
            LatheMesh.Part(root, "GalleryCoreWall", w.ToMesh("SU_GalleryCoreWall"), wall, collider: true);

            var v = new LatheMesh(Vector3.zero) { Step = 2f };
            v.Revolve(Vault, A0, A1, false);
            LatheMesh.Part(root, "GalleryVault", v.ToMesh("SU_GalleryVault"), vault);

            // Transverse ribs on every bay line, a skirting course along both walls, a cornice under the vault.
            var t = new LatheMesh(Vector3.zero) { Step = 2f };
            for (var deg = A0 + BayDeg; deg < A1 - 1f; deg += BayDeg)
            {
                t.Rib(Vault, deg, 0.1f, 0.12f, false);
                if (!NearDoor(deg, 6f))
                    t.Bar(deg, RIn, RIn + 0.16f, 0f, WallTop, 0.13f);
            }

            t.Revolve(new[] { new Vector2(RIn + 0.08f, 0f), new Vector2(RIn + 0.08f, 0.22f), new Vector2(RIn, 0.24f) }, A0, A1, false);
            t.Revolve(new[] { new Vector2(ROut, 0.24f), new Vector2(ROut - 0.08f, 0.22f), new Vector2(ROut - 0.08f, 0f) }, A0, A1, false);
            t.Revolve(new[] { new Vector2(RIn, WallTop - 0.1f), new Vector2(RIn + 0.14f, WallTop - 0.06f), new Vector2(RIn, WallTop + 0.05f) }, A0, A1, false);
            LatheMesh.Part(root, "GalleryRibs", t.ToMesh("SU_GalleryRibs"), trim);

            // The floor's gold line down the middle, dark joint lines by the walls, gold bosses on the ribs' crowns.
            var g = new LatheMesh(Vector3.zero) { Step = 2f };
            g.Revolve(new[] { new Vector2(RMid - 0.04f, 0.004f), new Vector2(RMid + 0.04f, 0.004f) }, A0 + 1.5f, A1 - 1.5f, true);
            for (var deg = A0 + BayDeg; deg < A1 - 1f; deg += BayDeg)
                g.Bar(deg, RMid - 0.12f, RMid + 0.12f, 4.66f, 4.76f, 0.12f);
            LatheMesh.Part(root, "GalleryGold", g.ToMesh("SU_GalleryGold"), gold);
            var j = new LatheMesh(Vector3.zero) { Step = 2f };
            foreach (var r in new[] { RIn + 0.45f, ROut - 0.55f })
                j.Revolve(new[] { new Vector2(r - 0.03f, 0.003f), new Vector2(r + 0.03f, 0.003f) }, A0 + 0.5f, A1 - 0.5f, true);
            LatheMesh.Part(root, "GalleryJoints", j.ToMesh("SU_GalleryJoints"), dark);

            // The rosette at the stair's foot: rings of gold and dark stone round an eight-point star.
            var at = At(1.4f, RMid);
            var ro = new LatheMesh(at) { Step = 6f };
            ro.Revolve(new[] { new Vector2(1.05f, 0.005f), new Vector2(1.15f, 0.005f) }, 0f, 360f, true);
            ro.Revolve(new[] { new Vector2(0.62f, 0.005f), new Vector2(0.68f, 0.005f) }, 0f, 360f, true);
            for (var k = 0; k < 8; k++)
            {
                var a = LatheMesh.Dir(k * 45f);
                var l = LatheMesh.Dir(k * 45f - 22.5f) * 0.22f;
                var r = LatheMesh.Dir(k * 45f + 22.5f) * 0.22f;
                ro.Tri(at + Vector3.up * 0.006f, at + l + Vector3.up * 0.006f, at + a * 0.6f + Vector3.up * 0.006f, Vector3.up, 1f);
                ro.Tri(at + Vector3.up * 0.006f, at + a * 0.6f + Vector3.up * 0.006f, at + r + Vector3.up * 0.006f, Vector3.up, 1f);
            }

            LatheMesh.Part(root, "GalleryRosette", ro.ToMesh("SU_GalleryRosette"), gold);

            // Invisible walls where the windows open (the sill stops a body, this keeps it off the ledge).
            var stop = new LatheMesh(Vector3.zero) { Step = 4f };
            stop.Revolve(new[] { new Vector2(ROut - 0.25f, 0f), new Vector2(ROut - 0.25f, 2.4f) }, A0, A1, true);
            var col = new GameObject("GalleryWindowStop");
            col.transform.SetParent(root, false);
            col.AddComponent<MeshCollider>().sharedMesh = stop.ToMesh("SU_GalleryWindowStop");
            // Solid boxes behind the windows, one per bay (a one-sided mesh can be crossed from behind).
            for (var deg = A0 + BayDeg * 0.5f; deg < A1; deg += BayDeg)
            {
                var d = LatheMesh.Dir(deg);
                var box = new GameObject("GalleryHull");
                box.transform.SetParent(root, false);
                box.transform.localPosition = d * (ROut + 0.3f) + Vector3.up * 2f;
                box.transform.localRotation = Quaternion.LookRotation(d, Vector3.up);
                box.AddComponent<BoxCollider>().size = new Vector3(2f * (ROut + 0.3f) * Mathf.Tan(BayDeg * 0.5f * Mathf.Deg2Rad) + 0.3f, 4f, 0.6f);
            }
        }

        static bool NearDoor(float deg, float pad)
        {
            foreach (var slot in new[]
                     {
                         CorridorRoom.Slot.LabPort, CorridorRoom.Slot.DockStarboard, CorridorRoom.Slot.MarketPort, CorridorRoom.Slot.DiplomacyPort,
                         CorridorRoom.Slot.QuartersStarboard, CorridorRoom.Slot.RefineryStarboard
                     })
                if (Mathf.Abs(Mathf.DeltaAngle(deg, Angle(SlotZ(slot)))) < pad)
                    return true;
            return false;
        }

        /// <summary>
        /// The outer wall: one flat facet per bay with a round-arched opening on the city — piers, sill, spandrels,
        /// deep reveals out to the crown's skin, a stone sill ledge, gold tracery (a mullion and a transom).
        /// </summary>
        static void BuildWindows(Transform root, Material wall, Material trim, Material gold)
        {
            var m = new LatheMesh(Vector3.zero);
            var ledge = new LatheMesh(Vector3.zero);
            var tracery = new LatheMesh(Vector3.zero);
            const int arc = 10;
            const float depth = 0.55f;
            for (var b0 = A0; b0 < A1 - 0.5f; b0 += BayDeg)
            {
                var p0 = LatheMesh.Dir(b0) * ROut;
                var p1 = LatheMesh.Dir(b0 + BayDeg) * ROut;
                var along = (p1 - p0).normalized;
                var len = Vector3.Distance(p0, p1);
                var inward = -LatheMesh.Dir(b0 + BayDeg * 0.5f);
                Vector3 P(float u, float y, float d = 0f) => p0 + along * u + Vector3.up * y - inward * d;
                var uc = len * 0.5f;
                var hw = WindowWidth * 0.5f;
                var ul = uc - hw;
                var ur = uc + hw;
                var top = Spring + hw;
                // Piers, sill wall, the band over the arch.
                m.Quad(P(0f, 0f), P(ul, 0f), P(ul, OuterTop), P(0f, OuterTop), inward, 1f);
                m.Quad(P(ur, 0f), P(len, 0f), P(len, OuterTop), P(ur, OuterTop), inward, 1f);
                m.Quad(P(ul, 0f), P(ur, 0f), P(ur, Sill), P(ul, Sill), inward, 0.9f);
                // Spandrels: from the arch up to the wall's top, and the reveal's soffit round the arch.
                for (var i = 0; i < arc; i++)
                {
                    var a0 = Mathf.PI * i / arc;
                    var a1 = Mathf.PI * (i + 1) / arc;
                    var q0 = new Vector2(uc - Mathf.Cos(a0) * hw, Spring + Mathf.Sin(a0) * hw);
                    var q1 = new Vector2(uc - Mathf.Cos(a1) * hw, Spring + Mathf.Sin(a1) * hw);
                    m.Quad(P(q0.x, q0.y), P(q1.x, q1.y), P(q1.x, OuterTop), P(q0.x, OuterTop), inward, 0.95f);
                    var down = new Vector3(0f, -1f, 0f);
                    m.Quad(P(q0.x, q0.y), P(q1.x, q1.y), P(q1.x, q1.y, depth), P(q0.x, q0.y, depth), down, 0.7f);
                }

                // Straight reveals from the sill to the spring, the sill's floor.
                m.Quad(P(ul, Sill), P(ul, Spring), P(ul, Spring, depth), P(ul, Sill, depth), along, 0.7f);
                m.Quad(P(ur, Sill), P(ur, Spring), P(ur, Spring, depth), P(ur, Sill, depth), -along, 0.7f);
                m.Quad(P(ul, Sill), P(ur, Sill), P(ur, Sill, depth), P(ul, Sill, depth), Vector3.up, 0.85f);
                // Stone ledge inside, under the window.
                var lc = P(uc, Sill - 0.04f, -0.12f);
                ledge.Quad(lc - along * (hw + 0.15f) + inward * 0.12f, lc + along * (hw + 0.15f) + inward * 0.12f,
                    lc + along * (hw + 0.15f) - inward * 0.02f, lc - along * (hw + 0.15f) - inward * 0.02f, Vector3.up, 1f);
                ledge.Quad(lc - along * (hw + 0.15f) + inward * 0.12f, lc + along * (hw + 0.15f) + inward * 0.12f,
                    lc + along * (hw + 0.15f) + inward * 0.12f + Vector3.down * 0.1f, lc - along * (hw + 0.15f) + inward * 0.12f + Vector3.down * 0.1f,
                    inward, 0.8f);
                // Tracery: a slim mullion up to the arch and a transom at the spring, set back in the reveal.
                var td = depth * 0.6f;
                tracery.Quad(P(uc - 0.025f, Sill, td), P(uc + 0.025f, Sill, td), P(uc + 0.025f, top, td), P(uc - 0.025f, top, td), inward, 1f);
                tracery.Quad(P(ul, Spring - 0.025f, td), P(ur, Spring - 0.025f, td), P(ur, Spring + 0.025f, td), P(ul, Spring + 0.025f, td), inward, 1f);
                for (var i = 0; i < arc; i++)
                {
                    var a0 = Mathf.PI * i / arc;
                    var a1 = Mathf.PI * (i + 1) / arc;
                    var o0 = new Vector2(uc - Mathf.Cos(a0) * (hw + 0.06f), Spring + Mathf.Sin(a0) * (hw + 0.06f));
                    var o1 = new Vector2(uc - Mathf.Cos(a1) * (hw + 0.06f), Spring + Mathf.Sin(a1) * (hw + 0.06f));
                    var i0 = new Vector2(uc - Mathf.Cos(a0) * (hw + 0.01f), Spring + Mathf.Sin(a0) * (hw + 0.01f));
                    var i1 = new Vector2(uc - Mathf.Cos(a1) * (hw + 0.01f), Spring + Mathf.Sin(a1) * (hw + 0.01f));
                    tracery.Quad(P(i0.x, i0.y, -0.01f), P(i1.x, i1.y, -0.01f), P(o1.x, o1.y, -0.01f), P(o0.x, o0.y, -0.01f), inward, 1f);
                }
            }

            LatheMesh.Part(root, "GalleryWindowWall", m.ToMesh("SU_GalleryWindowWall"), wall, collider: true);
            LatheMesh.Part(root, "GalleryLedges", ledge.ToMesh("SU_GalleryLedges"), trim);
            LatheMesh.Part(root, "GalleryTracery", tracery.ToMesh("SU_GalleryTracery"), gold);
        }

        /// <summary>A stone portal round each room door: pilasters, a round-arched hood, a gold keystone.</summary>
        static void BuildPortals(MeshBatch b, Material trim, Material gold)
        {
            foreach (var slot in new[]
                     {
                         CorridorRoom.Slot.LabPort, CorridorRoom.Slot.DockStarboard, CorridorRoom.Slot.MarketPort, CorridorRoom.Slot.DiplomacyPort,
                         CorridorRoom.Slot.QuartersStarboard, CorridorRoom.Slot.RefineryStarboard
                     })
            {
                var (pos, yaw) = DoorPose(slot);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 L(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
                foreach (var s in new[] { -1f, 1f })
                {
                    b.Box(L(s * 1.02f, 1.35f, 0.1f), new Vector3(0.24f, 2.7f, 0.22f), trim, rot);
                    b.Box(L(s * 1.02f, 0.08f, 0.14f), new Vector3(0.34f, 0.16f, 0.3f), trim, rot);
                }

                for (var i = 0; i <= 8; i++)
                {
                    var a = Mathf.PI * i / 8f;
                    var c = L(-Mathf.Cos(a) * 1.02f, 2.75f + Mathf.Sin(a) * 0.62f, 0.1f);
                    b.Box(c, new Vector3(0.36f, 0.24f, 0.22f), trim, rot * Quaternion.Euler(0f, 0f, 90f - i * 22.5f));
                }

                b.Box(L(0f, 3.4f, 0.2f), new Vector3(0.18f, 0.26f, 0.06f), gold, rot);
            }
        }

        /// <summary>Banners on the core wall between the portals: a bronze rod, crimson cloth, a gold band and fringe.</summary>
        static void BuildBanners(MeshBatch b, Material cloth, Material gold, Material bronze)
        {
            var doors = new List<float> { 0f };
            foreach (var z in new[] { 5.5f, 10.5f, 15.5f, 20.5f, 25.5f })
                doors.Add(z);
            for (var i = 1; i < doors.Count; i++)
            {
                var z = (doors[i - 1] + doors[i]) * 0.5f;
                if (z < 3f)
                    continue;
                var deg = Angle(z);
                var d = LatheMesh.Dir(deg);
                var rot = Quaternion.LookRotation(d, Vector3.up);
                var c = d * (RIn + 0.06f);
                b.Box(c + Vector3.up * 3.15f, new Vector3(0.95f, 0.04f, 0.04f), bronze, rot);
                b.Box(c + Vector3.up * 2.15f + d * 0.01f, new Vector3(0.78f, 1.95f, 0.012f), cloth, rot);
                b.Box(c + Vector3.up * 2.55f + d * 0.02f, new Vector3(0.62f, 0.06f, 0.006f), gold, rot);
                b.Box(c + Vector3.up * 2.05f + d * 0.02f, new Vector3(0.06f, 0.62f, 0.006f), gold, rot);
                b.Box(c + Vector3.up * 1.17f + d * 0.01f, new Vector3(0.78f, 0.04f, 0.014f), gold, rot);
            }
        }

        /// <summary>A lantern in every bay: chain from the vault, bronze cage, warm glowing core.</summary>
        static void BuildLanterns(MeshBatch b, Material bronze, Material glow)
        {
            for (var deg = A0 + BayDeg * 0.5f; deg < A1; deg += BayDeg)
            {
                var c = LatheMesh.Dir(deg) * RMid;
                b.Box(c + Vector3.up * 4.25f, new Vector3(0.02f, 1.15f, 0.02f), bronze);
                b.Tube(c + Vector3.up * 3.5f, Vector3.up, 0.1f, 0.3f, glow);
                b.Tube(c + Vector3.up * 3.68f, Vector3.up, 0.15f, 0.05f, bronze);
                b.Tube(c + Vector3.up * 3.33f, Vector3.up, 0.13f, 0.04f, bronze);
                for (var k = 0; k < 4; k++)
                {
                    var o = LatheMesh.Dir(deg + k * 90f + 45f) * 0.12f;
                    b.Box(c + o + Vector3.up * 3.5f, new Vector3(0.018f, 0.34f, 0.018f), bronze);
                }
            }
        }

        /// <summary>Stone benches facing the windows, trees in stone planters by the piers.</summary>
        static void BuildFurniture(MeshBatch b, Material trim, Material bronze, Material leaf, Material gold, Material glow)
        {
            var bench = 0;
            for (var deg = A0 + BayDeg * 1.5f; deg < A1 - BayDeg; deg += BayDeg * 2f)
            {
                var d = LatheMesh.Dir(deg);
                var rot = Quaternion.LookRotation(d, Vector3.up);
                var c = d * (ROut - 1.15f);
                if ((bench++ & 1) == 0)
                {
                    b.Box(c + Vector3.up * 0.44f, new Vector3(1.5f, 0.1f, 0.48f), trim, rot);
                    foreach (var s in new[] { -0.58f, 0.58f })
                        b.Box(c + rot * new Vector3(s, 0.2f, 0f), new Vector3(0.14f, 0.4f, 0.42f), trim, rot);
                    b.Box(c + Vector3.up * 0.495f + d * -0.2f, new Vector3(1.5f, 0.012f, 0.02f), gold, rot);
                }
                else
                {
                    // A tree: stone planter, gold rim, trunk, three tiers of foliage.
                    b.Box(c + Vector3.up * 0.3f, new Vector3(0.7f, 0.6f, 0.7f), trim, rot);
                    b.Box(c + Vector3.up * 0.605f, new Vector3(0.62f, 0.02f, 0.62f), bronze, rot);
                    foreach (var s in new[] { -0.36f, 0.36f })
                    {
                        b.Box(c + rot * new Vector3(s, 0.61f, 0f), new Vector3(0.03f, 0.03f, 0.75f), gold, rot);
                        b.Box(c + rot * new Vector3(0f, 0.61f, s), new Vector3(0.75f, 0.03f, 0.03f), gold, rot);
                    }
                    b.Tube(c + Vector3.up * 1.15f, Vector3.up, 0.05f, 1.1f, bronze);
                    for (var t = 0; t < 3; t++)
                        b.Add(Cone(), Matrix4x4.TRS(c + Vector3.up * (1.25f + t * 0.42f), Quaternion.Euler(0f, deg + t * 30f, 0f),
                            new Vector3(0.75f - t * 0.17f, 0.6f, 0.75f - t * 0.17f)), leaf);
                }
            }
        }

        static Mesh _cone;

        /// <summary>An eight-sided foliage cone, base radius 0.5, height 1 (y 0 → 1).</summary>
        internal static Mesh Cone()
        {
            if (_cone != null)
                return _cone;
            var l = new LatheMesh(Vector3.zero) { Step = 45f };
            l.Revolve(new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0.05f), new Vector2(0f, 1f) }, 0f, 360f, false);
            _cone = l.ToMesh("SU_GalleryFoliage");
            return _cone;
        }

        /// <summary>The model of the citadel: tower, crown, gold spire, on a plinth under a lantern ring.</summary>
        static void BuildModel(Transform root, CicArtKit art, Material trim, Material gold, Material glow)
        {
            var c = At(ModelZ, ModelR);
            var plinth = new LatheMesh(c) { Step = 10f };
            plinth.Revolve(new[] { new Vector2(0.75f, 0f), new Vector2(0.75f, 0.1f), new Vector2(0.55f, 0.18f), new Vector2(0.5f, 0.85f),
                new Vector2(0.62f, 0.9f), new Vector2(0.62f, 0.96f), new Vector2(0f, 0.97f) }, 0f, 360f, false);
            LatheMesh.Part(root, "ModelPlinth", plinth.ToMesh("SU_GalleryModelPlinth"), trim);
            var tower = new LatheMesh(c + Vector3.up * 0.97f) { Step = 12f };
            tower.Revolve(new[]
            {
                new Vector2(0.34f, 0f), new Vector2(0.26f, 0.2f), new Vector2(0.17f, 0.75f), new Vector2(0.13f, 1.05f), new Vector2(0.2f, 1.12f),
                new Vector2(0.2f, 1.22f), new Vector2(0.12f, 1.3f), new Vector2(0.02f, 1.36f)
            }, 0f, 360f, false);
            LatheMesh.Part(root, "ModelTower", tower.ToMesh("SU_GalleryModelTower"), art.MetalPanel(0.6f));
            var lights = new LatheMesh(c + Vector3.up * 0.97f) { Step = 12f };
            foreach (var y in new[] { 0.35f, 0.62f, 0.9f, 1.16f })
            {
                var r = y < 1f ? Mathf.Lerp(0.26f, 0.13f, (y - 0.2f) / 0.85f) + 0.004f : 0.204f;
                lights.Revolve(new[] { new Vector2(r, y), new Vector2(r, y + 0.02f) }, 0f, 360f, false);
            }

            lights.Revolve(new[] { new Vector2(0.008f, 1.36f), new Vector2(0.002f, 1.7f) }, 0f, 360f, false);
            LatheMesh.Part(root, "ModelGold", lights.ToMesh("SU_GalleryModelGold"), gold);
            var halo = new LatheMesh(c + Vector3.up * 3.3f) { Step = 10f };
            halo.Revolve(new[] { new Vector2(0.55f, 0f), new Vector2(0.62f, 0f) }, 0f, 360f, true);
            LatheMesh.Part(root, "ModelHalo", halo.ToMesh("SU_GalleryModelHalo"), glow);
        }
    }
}
