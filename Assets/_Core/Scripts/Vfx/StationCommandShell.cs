using Core.UI;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The orbital station's command centre: a tall rotunda round the holo table (the ship's bridge is a tight
    /// octagon; a station has room), built in three levels. The pit: the table and the commander's podium on
    /// the floor, concentric light rings round them. The tiers: two raised crescents (+0.5 m, three steps) to
    /// port and starboard under the data walls, where the six crew posts work facing the walls
    /// (<see cref="StationCommandLayout"/>). The gallery: a glass-railed balcony round the drum above the bays
    /// (doorways onto it, a hand walking it), all but the arc behind the main screen, which stands on a
    /// monolith ahead. Four panoramic bays onto the ring and the world, the corridor door and two displays aft;
    /// over it a stepped dome with hanging light rings and a lit oculus, and an emitter ring over the table
    /// whose beams fall onto the platter's rim. Same kit as the bridge (SU/HullInterior lit by the room's four
    /// lights, shared materials, one mesh per material), built hidden and swapped in by
    /// <see cref="BridgeDressing"/> when the inhabited view is a station. Interior local metres, +z = forward.
    /// </summary>
    public static class StationCommandShell
    {
        public const float Sill = 0.45f;
        public const float Head = 2.95f;
        public const float WallTop = 5.4f;
        const float Reveal = 0.42f;
        const float DomeBase = 6.3f;
        const float DomeTop = 8.4f;
        const float Oculus = 2.8f;
        const float CapY = 8.7f;
        /// <summary>Crew tiers: top height, first step's radius, tread depth (three steps of TierHeight / 3).</summary>
        public const float TierHeight = 0.5f;
        public const float TierInner = 4.4f;
        const float TierTread = 0.3f;
        /// <summary>Gallery deck height and depth from the wall; it spans GalleryFrom → GalleryTo (not behind the monolith).</summary>
        public const float GalleryY = 3.2f;
        internal const float GalleryDepth = 0.95f;
        const float GalleryFrom = 36f;
        const float GalleryTo = 324f;
        const float RailHeight = 0.98f;
        internal static readonly float[] GalleryDoors = { 105f, 145f, 215f, 255f };

        /// <summary>Panoramic bays (degrees from +z toward +x).</summary>
        static readonly Vector2[] Windows = { new(24f, 66f), new(114f, 148f), new(212f, 246f), new(294f, 336f) };
        /// <summary>Solid wall between the bays: ahead (behind the monolith), the two data walls, aft.</summary>
        static readonly Vector2[] Solid = { new(-24f, 24f), new(66f, 114f), new(148f, 212f), new(246f, 294f) };
        static readonly Vector2[] DataWalls = { new(72f, 108f), new(252f, 288f) };
        /// <summary>The crew tiers (degrees): the solid walls between the side bays, under the data walls.</summary>
        public static readonly Vector2[] Tiers = { new(66f, 114f), new(246f, 294f) };
        internal static readonly Vector2[] Credenzas = { new(153f, 167f), new(193f, 207f) };

        /// <summary>The station's light colour: cool white-cyan (the ship's is a deeper cyan).</summary>
        public static readonly Color Glow = new(0.55f, 0.9f, 1f, 1f);

        public static float R => WorldScale.StationHallRadius;
        public static Vector3 Centre => new(0f, 0f, WorldScale.CicTableCenterZ);

        /// <summary>A point on the hall's wall at <paramref name="deg"/>, <paramref name="inset"/> into the room.</summary>
        public static Vector3 OnWall(float deg, float inset, float y) => Centre + LatheMesh.Dir(deg) * (R - inset) + Vector3.up * y;

        /// <summary>Facing into the hall from the wall at <paramref name="deg"/>.</summary>
        public static Quaternion FacingIn(float deg) => Quaternion.LookRotation(-LatheMesh.Dir(deg), Vector3.up);

        public static Transform Build(Transform room, CicArtKit art)
        {
            var root = new GameObject("StationShell").transform;
            root.SetParent(room, false);

            var wall = art.Hull(art.Panel, new Color(0.3f, 0.36f, 0.46f), new Vector2(1.4f, 0.7f));
            var dado = art.Hull(art.Panel, new Color(0.13f, 0.16f, 0.21f), new Vector2(0.7f, 0.45f), seam: 0.55f);
            var ceiling = art.Hull(art.Wall, new Color(0.24f, 0.29f, 0.38f), new Vector2(1.6f, 0.8f));
            var rib = art.Hull(art.Panel, new Color(0.5f, 0.58f, 0.7f), new Vector2(40f, 0.5f), seam: 0.3f, lift: 0.08f);
            var frame = art.Hull(art.Panel, new Color(0.38f, 0.44f, 0.54f), new Vector2(40f, 40f), seam: 0f, lift: 0.06f);
            var deck = art.Hull(art.DeckRib != null ? art.DeckRib : art.Floor, new Color(0.34f, 0.4f, 0.5f), new Vector2(1.2f, 1.2f),
                tiling: 0.42f, seam: 0.35f);
            var plate = art.Hull(art.Panel, new Color(0.2f, 0.24f, 0.31f), new Vector2(0.9f, 0.9f), tiling: 0.8f, seam: 0.5f);
            var desk = art.Hull(art.Panel, new Color(0.18f, 0.22f, 0.28f), new Vector2(0.6f, 40f), seam: 0.4f);
            var glow = art.Lit(Texture2D.whiteTexture, Glow, 2.4f);
            var glowSoft = art.Lit(Texture2D.whiteTexture, Glow, 1.1f);
            var lamp = art.Lit(Texture2D.whiteTexture, new Color(0.82f, 0.94f, 1f), 1.7f);
            var beams = art.Lit(Texture2D.whiteTexture, new Color(0.45f, 0.88f, 1f), 0.7f);

            BuildFloor(root, deck, plate, glowSoft);
            BuildDrum(root, wall, dado, frame, glow);
            BuildCeiling(root, ceiling, rib, frame, glow, lamp, beams, art.Lit(Texture2D.whiteTexture, new Color(0.55f, 0.78f, 0.95f), 0.6f));
            BuildDataWalls(root, art, frame, glow);
            BuildTiers(root, plate, desk, glow);
            BuildGallery(root, art, frame, dado, glow, lamp);
            BuildMonolith(root, art, glow);
            BuildAft(root, frame, glow);
            StationHallDressing.Build(root, art, frame, dado, glow, glowSoft);
            BuildHull(root);
            root.gameObject.SetActive(false);
            return root;
        }

        // ── Floor ────────────────────────────────────────────────────────────────

        static void BuildFloor(Transform root, Material deck, Material plate, Material glow)
        {
            var floor = new LatheMesh(Centre);
            floor.Revolve(new[] { new Vector2(3.05f, 0f), new Vector2(R - 0.1f, 0f) }, 0f, 360f, true, LatheMesh.Uv.Planar);
            LatheMesh.Part(root, "Deck", floor.ToMesh("SU_StationDeck"), deck, collider: true);

            // The command disc under table and chair: darker plating inside the first light ring.
            var disc = new LatheMesh(Centre) { Step = 6f };
            disc.Revolve(new[] { new Vector2(0f, 0f), new Vector2(3.05f, 0f) }, 0f, 360f, true, LatheMesh.Uv.Planar);
            LatheMesh.Part(root, "Deck", disc.ToMesh("SU_StationCommandDisc"), plate, collider: true).name = "Floor";

            var lines = new LatheMesh(Centre);
            foreach (var (r0, r1) in new[] { (1.74f, 1.78f), (3.0f, 3.06f), (4.34f, 4.4f), (4.48f, 4.5f), (R - 0.46f, R - 0.43f) })
                lines.Revolve(new[] { new Vector2(r0, 0.004f), new Vector2(r1, 0.004f) }, 0f, 360f, true);
            // Radial runners from the crew ring to the wall, between the bays and the data walls.
            for (var k = 0; k < 8; k++)
            {
                var deg = k * 45f + 22.5f;
                var s = Vector3.Cross(Vector3.up, LatheMesh.Dir(deg)) * 0.015f;
                var a = lines.At(deg, 4.5f, 0.004f);
                var b = lines.At(deg, R - 0.46f, 0.004f);
                lines.Quad(a - s, a + s, b + s, b - s, Vector3.up, 1f);
            }

            // Two lit runners from the aft door to the crew ring (like the ship's, on a longer walk).
            foreach (var x in new[] { -0.95f, 0.95f })
            {
                var z0 = Centre.z - Mathf.Sqrt(R * R - x * x) + 0.5f;
                var z1 = Centre.z - Mathf.Sqrt(4.34f * 4.34f - x * x);
                lines.Quad(new Vector3(x - 0.02f, 0.004f, z0), new Vector3(x + 0.02f, 0.004f, z0), new Vector3(x + 0.02f, 0.004f, z1),
                    new Vector3(x - 0.02f, 0.004f, z1), Vector3.up, 1f);
            }

            LatheMesh.Part(root, "GlowDeckLines", lines.ToMesh("SU_StationDeckLines"), glow);
        }

        // ── Drum: dado, wall with its bays, reveals and mullions ───────────────────

        static void BuildDrum(Transform root, Material wall, Material dado, Material frame, Material glow)
        {
            var low = new LatheMesh(Centre);
            low.Revolve(new[] { new Vector2(R - 0.1f, 0f), new Vector2(R - 0.05f, 0.12f) }, 0f, 360f, true, ao: new[] { 0.45f, 0.62f });
            low.Revolve(new[] { new Vector2(R - 0.05f, 0.12f), new Vector2(R - 0.05f, Sill - 0.08f) }, 0f, 360f, true, ao: new[] { 0.7f, 0.9f });
            low.Revolve(new[] { new Vector2(R - 0.05f, Sill - 0.08f), new Vector2(R, Sill - 0.08f) }, 0f, 360f, true, ao: new[] { 0.9f, 0.6f });
            LatheMesh.Part(root, "Dado", low.ToMesh("SU_StationDado"), dado, collider: true);

            var m = new LatheMesh(Centre);
            foreach (var s in Solid)
                m.Revolve(new[] { new Vector2(R, Sill - 0.08f), new Vector2(R, Head) }, s.x, s.y, true, ao: new[] { 0.85f, 1f });
            m.Revolve(new[] { new Vector2(R, Head), new Vector2(R, WallTop) }, 0f, 360f, true, ao: new[] { 1f, 0.9f });
            LatheMesh.Part(root, "Walls", m.ToMesh("SU_StationWalls"), wall, collider: true);

            var f = new LatheMesh(Centre);
            var lit = new LatheMesh(Centre);
            foreach (var w in Windows)
            {
                // Sill ledge, proud into the room (its front lit), running out through the reveal.
                f.Revolve(new[] { new Vector2(R - 0.05f, Sill - 0.08f), new Vector2(R - 0.24f, Sill - 0.05f) }, w.x, w.y, true, ao: new[] { 0.5f, 0.7f });
                lit.Revolve(new[] { new Vector2(R - 0.24f, Sill - 0.05f), new Vector2(R - 0.24f, Sill) }, w.x, w.y, true);
                f.Revolve(new[] { new Vector2(R - 0.24f, Sill), new Vector2(R + Reveal, Sill) }, w.x, w.y, true, ao: new[] { 1f, 0.6f });
                f.Revolve(new[] { new Vector2(R + Reveal, Head), new Vector2(R, Head) }, w.x, w.y, true, ao: new[] { 0.5f, 0.9f });
                lit.Revolve(new[] { new Vector2(R - 0.004f, Head + 0.03f), new Vector2(R - 0.004f, Head + 0.055f) }, w.x, w.y, true);
                foreach (var (deg, sign) in new[] { (w.x, 1f), (w.y, -1f) })
                {
                    var d = LatheMesh.Dir(deg);
                    var t = Vector3.Cross(Vector3.up, d) * sign;
                    f.Quad(f.At(deg, R, Sill), f.At(deg, R + Reveal, Sill), f.At(deg, R + Reveal, Head), f.At(deg, R, Head), t, 0.8f);
                }

                // Mullions every ~7°: the bay reads as one curved glazing, not a hole.
                var count = Mathf.Max(2, Mathf.RoundToInt((w.y - w.x) / 7f));
                for (var i = 1; i < count; i++)
                    f.Bar(Mathf.Lerp(w.x, w.y, i / (float)count), R - 0.04f, R + Reveal - 0.04f, Sill, Head, 0.045f);
                f.Bar(w.x - 0.6f, R - 0.14f, R, Sill - 0.08f, WallTop - 0.1f, 0.07f);
                f.Bar(w.y + 0.6f, R - 0.14f, R, Sill - 0.08f, WallTop - 0.1f, 0.07f);
            }

            LatheMesh.Part(root, "Frames", f.ToMesh("SU_StationFrames"), frame);

            // Light lines: the kick, the cornice.
            lit.Revolve(new[] { new Vector2(R - 0.092f, 0.03f), new Vector2(R - 0.073f, 0.075f) }, 0f, 360f, true);
            lit.Revolve(new[] { new Vector2(R - 0.004f, WallTop - 0.09f), new Vector2(R - 0.004f, WallTop - 0.055f) }, 0f, 360f, true);
            LatheMesh.Part(root, "GlowDrum", lit.ToMesh("SU_StationDrumGlow"), glow);
        }

        // ── Ceiling: cornice, soffit, cove, dome, oculus; light rings and the table's emitter ──

        static Vector2 DomePoint(float u)
        {
            var phi = u * Mathf.PI * 0.5f;
            return new Vector2(Oculus + (R - 1f - Oculus) * Mathf.Cos(phi), DomeBase + (DomeTop - DomeBase) * Mathf.Sin(phi));
        }

        static float DomeY(float r)
        {
            var c = Mathf.Clamp01((r - Oculus) / (R - 1f - Oculus));
            return DomeBase + (DomeTop - DomeBase) * Mathf.Sqrt(1f - c * c);
        }

        static void BuildCeiling(Transform root, Material ceiling, Material rib, Material frame, Material glow, Material lamp,
            Material beams, Material oculus)
        {
            var c = new LatheMesh(Centre);
            c.Revolve(new[] { new Vector2(R, WallTop), new Vector2(R - 0.3f, WallTop + 0.12f) }, 0f, 360f, true, ao: new[] { 0.7f, 0.85f });
            c.Revolve(new[] { new Vector2(R - 0.3f, WallTop + 0.12f), new Vector2(R - 1f, WallTop + 0.32f) }, 0f, 360f, true, ao: new[] { 0.85f, 0.6f });
            c.Revolve(new[] { new Vector2(R - 1f, WallTop + 0.32f), new Vector2(R - 1f, DomeBase) }, 0f, 360f, true, ao: new[] { 0.55f, 0.8f });
            var dome = new Vector2[11];
            var ao = new float[dome.Length];
            for (var i = 0; i < dome.Length; i++)
            {
                dome[i] = DomePoint(i / (dome.Length - 1f));
                ao[i] = Mathf.Lerp(0.7f, 1f, i / (dome.Length - 1f));
            }

            c.Revolve(dome, 0f, 360f, true, ao: ao);
            LatheMesh.Part(root, "Dome", c.ToMesh("SU_StationDome"), ceiling);

            var ribs = new LatheMesh(Centre);
            for (var k = 0; k < 16; k++)
                ribs.Rib(dome, k * 22.5f + 11.25f, 0.07f, 0.08f, true);
            LatheMesh.Part(root, "DomeRibs", ribs.ToMesh("SU_StationDomeRibs"), rib);

            // Cove light on the riser, the oculus's lit lip and its light panel.
            var lit = new LatheMesh(Centre);
            lit.Revolve(new[] { new Vector2(R - 1.006f, DomeBase - 0.2f), new Vector2(R - 1.006f, DomeBase - 0.08f) }, 0f, 360f, true);
            lit.Revolve(new[] { new Vector2(Oculus, DomeTop), new Vector2(Oculus, CapY) }, 0f, 360f, true);
            LatheMesh.Part(root, "GlowCove", lit.ToMesh("SU_StationCove"), glow);
            var cap = new LatheMesh(Centre) { Step = 7.5f };
            cap.Revolve(new[] { new Vector2(Oculus, CapY), new Vector2(0f, CapY) }, 0f, 360f, true);
            LatheMesh.Part(root, "LightOculus", cap.ToMesh("SU_StationOculus"), oculus);

            // Two hanging light rings (housing over, light under) and the emitter ring over the table.
            var housing = new LatheMesh(Centre);
            var under = new LatheMesh(Centre);
            void Ring(float r, float y, float half, float h, int hangers)
            {
                housing.Revolve(new[] { new Vector2(r + half, y + h), new Vector2(r - half, y + h) }, 0f, 360f, false);
                housing.Revolve(new[] { new Vector2(r + half, y), new Vector2(r + half, y + h) }, 0f, 360f, false);
                housing.Revolve(new[] { new Vector2(r - half, y + h), new Vector2(r - half, y) }, 0f, 360f, false);
                under.Revolve(new[] { new Vector2(r - half, y), new Vector2(r + half, y) }, 0f, 360f, false);
                var top = hangers > 3 ? DomeY(r) : CapY;
                for (var i = 0; i < hangers; i++)
                    housing.Bar(i * 360f / hangers + 7f, r - 0.012f, r + 0.012f, y + h, top, 0.012f);
            }

            Ring(5.7f, 7.15f, 0.15f, 0.12f, 8);
            Ring(4.1f, 7.75f, 0.12f, 0.1f, 6);
            Ring(1.55f, 4.38f, 0.1f, 0.18f, 3);
            LatheMesh.Part(root, "LightRings", housing.ToMesh("SU_StationRingHousings"), frame);
            LatheMesh.Part(root, "LightRingGlow", under.ToMesh("SU_StationRingGlow"), lamp);

            // The hologram's column: fine beams falling from the emitter ring toward the platter's rim, tapering
            // out over the heads (clear of the captain's view of the table and of hands at the platter).
            var b = new LatheMesh(Centre);
            var rim = WorldScale.CicTableDiameter * 0.5f + 0.02f;
            for (var k = 0; k < 18; k++)
            {
                var deg = k * 20f + 10f;
                var top = b.At(deg, 1.52f, 4.38f);
                var bottom = b.At(deg, rim, WorldScale.CicTableHeight + 0.16f);
                var mid = Vector3.Lerp(top, bottom, 0.38f);
                var tip = Vector3.Lerp(top, bottom, 0.62f);
                var s = Vector3.Cross(Vector3.up, LatheMesh.Dir(deg)) * 0.006f;
                var inward = -LatheMesh.Dir(deg);
                foreach (var n in new[] { inward, -inward })
                {
                    b.Quad(top - s, top + s, mid + s, mid - s, n, 1f);
                    b.Tri(mid - s, mid + s, tip, n, 1f);
                }
            }

            LatheMesh.Part(root, "LightBeams", b.ToMesh("SU_StationBeams"), beams);
        }

        // ── Data walls (port and starboard, over the crew tiers) ──────────────────

        static void BuildDataWalls(Transform root, CicArtKit art, Material frame, Material glow)
        {
            // Textured screens carry their light in the texture (a flat emission would wash their dark ground out).
            var screenMat = new Material(art.Lit(StationScreens.DataWall(), Color.white * 1.7f, 0f)) { name = "SU_StationDataWall" };
            var screens = new LatheMesh(Centre);
            var plate = new LatheMesh(Centre);
            var lit = new LatheMesh(Centre);
            foreach (var w in DataWalls)
            {
                screens.Revolve(new[] { new Vector2(R - 0.09f, 1.2f), new Vector2(R - 0.09f, 2.95f) }, w.x, w.y, true, LatheMesh.Uv.Normalised);
                plate.Revolve(new[] { new Vector2(R - 0.03f, 1.06f), new Vector2(R - 0.03f, 3.08f) }, w.x - 1.6f, w.y + 1.6f, true, ao: new[] { 0.7f, 0.9f });
                plate.Revolve(new[] { new Vector2(R - 0.03f, 2.95f), new Vector2(R - 0.09f, 2.95f) }, w.x, w.y, false);
                plate.Revolve(new[] { new Vector2(R - 0.09f, 1.2f), new Vector2(R - 0.03f, 1.2f) }, w.x, w.y, false);
                foreach (var (deg, sign) in new[] { (w.x, -1f), (w.y, 1f) })
                {
                    var t = Vector3.Cross(Vector3.up, LatheMesh.Dir(deg)) * sign;
                    plate.Quad(plate.At(deg, R - 0.03f, 1.2f), plate.At(deg, R - 0.09f, 1.2f), plate.At(deg, R - 0.09f, 2.95f),
                        plate.At(deg, R - 0.03f, 2.95f), t, 0.8f);
                }

                lit.Revolve(new[] { new Vector2(R - 0.032f, 1.1f), new Vector2(R - 0.032f, 1.13f) }, w.x - 1f, w.y + 1f, true);
            }

            LatheMesh.Part(root, "DataScreens", screens.ToMesh("SU_StationDataScreens"), screenMat)
                .AddComponent<StationScreens.Scroll>().Bind(screenMat, 0.006f);
            LatheMesh.Part(root, "DataFrames", plate.ToMesh("SU_StationDataFrames"), frame);

            LatheMesh.Part(root, "GlowData", lit.ToMesh("SU_StationDataGlow"), glow);
        }

        // ── Crew tiers: two raised crescents under the data walls ─────────────────

        /// <summary>A crew post on a tier: on its top at <paramref name="deg"/>, <paramref name="r"/> from the axis.</summary>
        public static Vector3 OnTier(float deg, float r) => Centre + LatheMesh.Dir(deg) * r + Vector3.up * TierHeight;

        /// <summary>
        /// Three steps up from the pit to a flat top running to the wall; treads walkable (OpsTier), risers facing
        /// the hall, a lit nosing on each step and a kick line under it, the ends closed on the bays' jambs.
        /// </summary>
        static void BuildTiers(Transform root, Material top, Material riser, Material glow)
        {
            var walk = new LatheMesh(Centre);
            var face = new LatheMesh(Centre);
            var lit = new LatheMesh(Centre);
            const float h = TierHeight / 3f;
            foreach (var t in Tiers)
            {
                for (var k = 0; k < 3; k++)
                {
                    var r0 = TierInner + k * TierTread;
                    var r1 = k == 2 ? R - 0.02f : r0 + TierTread;
                    var y = h * (k + 1);
                    walk.Revolve(new[] { new Vector2(r1, y), new Vector2(r0, y) }, t.x, t.y, false, LatheMesh.Uv.Planar);
                    face.Revolve(new[] { new Vector2(r0, y), new Vector2(r0, y - h) }, t.x, t.y, false, ao: new[] { 0.9f, 0.55f });
                    lit.Revolve(new[] { new Vector2(r0 + 0.05f, y + 0.003f), new Vector2(r0 + 0.012f, y + 0.003f) }, t.x + 0.5f, t.y - 0.5f, false);
                    lit.Revolve(new[] { new Vector2(r0 - 0.003f, y - 0.014f), new Vector2(r0 - 0.003f, y - 0.03f) }, t.x + 0.5f, t.y - 0.5f, false);
                    foreach (var (deg, sign) in new[] { (t.x, -1f), (t.y, 1f) })
                    {
                        var tan = Vector3.Cross(Vector3.up, LatheMesh.Dir(deg)) * sign;
                        var rEnd = k == 2 ? R - 0.02f : r1;
                        face.Quad(face.At(deg, r0, 0f), face.At(deg, rEnd, 0f), face.At(deg, rEnd, y), face.At(deg, r0, y), tan, 0.75f);
                    }
                }
            }

            LatheMesh.Part(root, "OpsTier", walk.ToMesh("SU_StationTierTreads"), top, collider: true);
            LatheMesh.Part(root, "OpsTierRisers", face.ToMesh("SU_StationTierRisers"), riser, collider: true);
            LatheMesh.Part(root, "GlowTier", lit.ToMesh("SU_StationTierGlow"), glow);
        }

        // ── Gallery: the balcony round the drum, above the bays ───────────────────

        /// <summary>
        /// Deck slab with a lit fascia and downlights under it, a glass balustrade between posts with a lit top
        /// rail, its two ends closed beside the monolith, and doorways onto it in the upper wall. Seen, not walked:
        /// no collider (one hand walks it, <see cref="Core.Crew.CrewLife"/>).
        /// </summary>
        static void BuildGallery(Transform root, CicArtKit art, Material frame, Material door, Material glow, Material lamp)
        {
            var g = new LatheMesh(Centre);
            var lit = new LatheMesh(Centre);
            var down = new LatheMesh(Centre);
            var glass = new LatheMesh(Centre);
            var panels = new LatheMesh(Centre);
            var gi = R - GalleryDepth;
            const float under = GalleryY - 0.16f;
            const float rail = GalleryY + RailHeight;

            g.Revolve(new[] { new Vector2(R - 0.01f, GalleryY), new Vector2(gi, GalleryY) }, GalleryFrom, GalleryTo, false, LatheMesh.Uv.Planar);
            g.Revolve(new[] { new Vector2(gi, GalleryY), new Vector2(gi, under) }, GalleryFrom, GalleryTo, false);
            g.Revolve(new[] { new Vector2(gi, under), new Vector2(R - 0.01f, under) }, GalleryFrom, GalleryTo, false, ao: new[] { 0.7f, 0.45f });
            foreach (var (deg, sign) in new[] { (GalleryFrom, -1f), (GalleryTo, 1f) })
            {
                var tan = Vector3.Cross(Vector3.up, LatheMesh.Dir(deg)) * sign;
                g.Quad(g.At(deg, gi, under), g.At(deg, R, under), g.At(deg, R, rail), g.At(deg, gi, rail), tan, 0.8f);
            }

            // Balustrade: glass on the deck's edge, posts every 9°, the rail capping them.
            glass.Revolve(new[] { new Vector2(gi + 0.04f, GalleryY), new Vector2(gi + 0.04f, rail - 0.04f) }, GalleryFrom, GalleryTo, true,
                LatheMesh.Uv.Normalised);
            for (var deg = GalleryFrom + 4.5f; deg < GalleryTo; deg += 9f)
                g.Bar(deg, gi, gi + 0.06f, GalleryY, rail, 0.018f);
            g.Revolve(new[] { new Vector2(gi + 0.08f, rail), new Vector2(gi - 0.01f, rail) }, GalleryFrom, GalleryTo, false);
            g.Revolve(new[] { new Vector2(gi - 0.01f, rail), new Vector2(gi - 0.01f, rail - 0.05f) }, GalleryFrom, GalleryTo, false);
            lit.Revolve(new[] { new Vector2(gi - 0.004f, under + 0.05f), new Vector2(gi - 0.004f, under + 0.09f) }, GalleryFrom, GalleryTo, true);
            lit.Revolve(new[] { new Vector2(gi + 0.05f, rail + 0.003f), new Vector2(gi + 0.025f, rail + 0.003f) }, GalleryFrom, GalleryTo, false);
            down.Revolve(new[] { new Vector2(gi + 0.1f, under - 0.003f), new Vector2(gi + 0.2f, under - 0.003f) }, GalleryFrom + 1f, GalleryTo - 1f, false);

            // Doorways in the upper wall: a dark leaf, jambs, a lit header.
            foreach (var d in GalleryDoors)
            {
                const float half = 3.1f;
                const float top = GalleryY + 2.1f;
                panels.Quad(panels.At(d - half, R - 0.015f, GalleryY), panels.At(d + half, R - 0.015f, GalleryY),
                    panels.At(d + half, R - 0.015f, top), panels.At(d - half, R - 0.015f, top), -LatheMesh.Dir(d), 0.8f);
                g.Bar(d - half - 0.3f, R - 0.07f, R, GalleryY, top + 0.1f, 0.035f);
                g.Bar(d + half + 0.3f, R - 0.07f, R, GalleryY, top + 0.1f, 0.035f);
                g.Revolve(new[] { new Vector2(R - 0.07f, top), new Vector2(R - 0.07f, top + 0.1f) }, d - half - 0.3f, d + half + 0.3f, true);
                lit.Revolve(new[] { new Vector2(R - 0.074f, top + 0.035f), new Vector2(R - 0.074f, top + 0.065f) }, d - half, d + half, true);
                lit.Bar(d, R - 0.02f, R - 0.016f, GalleryY + 0.9f, GalleryY + 1.2f, 0.012f);
            }

            LatheMesh.Part(root, "Gallery", g.ToMesh("SU_StationGallery"), frame);
            LatheMesh.Part(root, "GalleryDoors", panels.ToMesh("SU_StationGalleryDoors"), door);
            LatheMesh.Part(root, "GlowGallery", lit.ToMesh("SU_StationGalleryGlow"), glow);
            LatheMesh.Part(root, "GalleryDownlights", down.ToMesh("SU_StationGalleryDown"), lamp);
            LatheMesh.Part(root, "GalleryGlass", glass.ToMesh("SU_StationGalleryGlass"),
                art.Holo(Texture2D.whiteTexture, new Color(0.55f, 0.85f, 1f, 0.022f)));
        }

        /// <summary>Walk spots on the gallery deck, every 30° (the chords stay on the deck), doorways as posts.</summary>
        public static (Vector3 at, Vector3 facing, bool post)[] GallerySpots()
        {
            var list = new System.Collections.Generic.List<(Vector3, Vector3, bool)>();
            for (var deg = GalleryFrom + 9f; deg <= GalleryTo - 9f; deg += 30f)
            {
                var door = false;
                foreach (var d in GalleryDoors)
                    door |= Mathf.Abs(Mathf.DeltaAngle(d, deg)) < 6f;
                var dir = LatheMesh.Dir(deg);
                list.Add((Centre + dir * (R - GalleryDepth * 0.5f) + Vector3.up * GalleryY, door ? dir : -dir, door));
            }

            return list.ToArray();
        }

        // ── The main screen's monolith ─────────────────────────────────────────────

        /// <summary>
        /// The ship's viewscreen hangs in the forward bulkhead; here it stands free, so a dark slab carries it
        /// (behind its housing, which ends 0.45 m behind the screen plane), with a canopy over its hood that the
        /// nameplate hangs from, and lit edges.
        /// </summary>
        static void BuildMonolith(Transform root, CicArtKit art, Material glow)
        {
            var slabMat = art.Hull(art.Panel, new Color(0.17f, 0.2f, 0.26f), new Vector2(1.1f, 0.6f), seam: 0.5f);
            var z = BridgeShell.Plan[BridgeShell.Forward].y;
            var slab = UiKit.MeshPiece(root, "Monolith", UiMeshes.RoundedBox(new Vector3(6.6f, 3.6f, 0.56f), 0.08f), slabMat,
                new Vector3(0f, 1.8f, z + 0.75f));
            slab.AddComponent<BoxCollider>().size = new Vector3(6.6f, 3.6f, 0.56f);
            UiKit.MeshPiece(root, "MonolithCanopy", UiMeshes.RoundedBox(new Vector3(6.9f, 0.26f, 1.9f), 0.07f), slabMat,
                new Vector3(0f, 3.44f, z));
            UiKit.MeshPiece(root, "MonolithFoot", UiMeshes.RoundedBox(new Vector3(7.1f, 0.08f, 1.4f), 0.03f), slabMat,
                new Vector3(0f, 0.04f, z + 0.3f));
            foreach (var (name, pos, size) in new[]
                     {
                         ("MonolithGlowFront", new Vector3(0f, 3.305f, z - 0.94f), new Vector3(6.7f, 0.025f, 0.02f)),
                         ("MonolithGlowL", new Vector3(-3.31f, 1.8f, z + 0.47f), new Vector3(0.02f, 3.2f, 0.03f)),
                         ("MonolithGlowR", new Vector3(3.31f, 1.8f, z + 0.47f), new Vector3(0.02f, 3.2f, 0.03f)),
                         ("MonolithGlowFoot", new Vector3(0f, 0.082f, z - 0.4f), new Vector3(7.0f, 0.006f, 0.02f))
                     })
                UiKit.MeshPiece(root, name, UiMeshes.RoundedBox(size, 0.004f), glow, pos);
        }

        // ── Aft: door portal, credenzas under the displays ─────────────────────────

        static void BuildAft(Transform root, Material frame, Material glow)
        {
            var f = new LatheMesh(Centre);
            var lit = new LatheMesh(Centre);
            // The corridor door's portal: two pylons and a lintel band proud of the curved wall.
            foreach (var deg in new[] { 172.6f, 187.4f })
            {
                f.Bar(deg, R - 0.32f, R, 0f, 3.05f, 0.11f);
                lit.Bar(deg, R - 0.325f, R - 0.3f, 0.25f, 2.85f, 0.018f);
            }

            f.Revolve(new[] { new Vector2(R - 0.32f, 2.75f), new Vector2(R - 0.32f, 3.05f) }, 172.6f, 187.4f, true);
            f.Revolve(new[] { new Vector2(R, 2.75f), new Vector2(R - 0.32f, 2.75f) }, 172.6f, 187.4f, true);
            lit.Revolve(new[] { new Vector2(R - 0.324f, 2.86f), new Vector2(R - 0.324f, 2.89f) }, 173.5f, 186.5f, true);

            Vector2[] section = { new(R - 0.02f, 0f), new(R - 0.02f, 0.86f), new(R - 0.62f, 0.9f), new(R - 0.64f, 0.06f), new(R - 0.58f, 0f) };
            foreach (var c in Credenzas)
            {
                for (var i = 0; i < section.Length - 1; i++)
                    f.Revolve(new[] { section[i], section[i + 1] }, c.x, c.y, false);
                f.Cap(section, c.x, false);
                f.Cap(section, c.y, true);
                lit.Revolve(new[] { new Vector2(R - 0.645f, 0.83f), new Vector2(R - 0.645f, 0.8f) }, c.x + 0.5f, c.y - 0.5f, false);
            }

            LatheMesh.Part(root, "AftFrames", f.ToMesh("SU_StationAft"), frame, collider: false);
            LatheMesh.Part(root, "GlowAft", lit.ToMesh("SU_StationAftGlow"), glow);
        }

        /// <summary>Solid boxes round the drum: a character body never slips through a one-sided wall or a bay.</summary>
        static void BuildHull(Transform root)
        {
            const int n = 36;
            var chord = 2f * R * Mathf.Tan(Mathf.PI / n) + 0.5f;
            for (var i = 0; i < n; i++)
            {
                var deg = i * 360f / n;
                var go = new GameObject("HullCollider");
                go.transform.SetParent(root, false);
                go.transform.localPosition = OnWall(deg, -0.35f, (WallTop + 0.4f) * 0.5f);
                go.transform.localRotation = FacingIn(deg);
                go.AddComponent<BoxCollider>().size = new Vector3(chord, WallTop + 0.4f, 0.6f);
            }
        }
    }
}
