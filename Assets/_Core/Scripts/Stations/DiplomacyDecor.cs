using System.Collections.Generic;
using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Set dressing of the diplomacy chamber, procedural and on shared materials: a round hall (twenty wall
    /// bays, ribs with lit slits), three curved tiers of a hemicycle on the far side with a seat per empire
    /// (its flag lit on the bench front), our alliance's banners hanging behind the top tier under a crest, a
    /// coffered dome with light rings, floor inlays around the orrery, and the two operator desks at the stand.
    /// Tiers and their light strips are single arc meshes, not stacks of boxes. The lit slits and tier strips
    /// swap to red while the empire is at war.
    /// </summary>
    public static class DiplomacyDecor
    {
        public const int Seats = 16;
        public const int BannerCount = 7;
        const int Bays = 20;
        const float ArcHalf = 118f;
        const float SillTop = 1.7f;
        const float WindowTop = 5.3f;

        /// <summary>Four bays on each flank are windows (63°–135° and 225°–297° around the hall).</summary>
        static bool IsWindow(int bay) => bay is >= 4 and <= 7 or >= 13 and <= 16;

        /// <summary>Middle of the right-hand windows, hall local (the chamber turns the star into it).</summary>
        public static Vector3 RightWindowCentre(Vector3 centre, float radius) =>
            centre + Quaternion.Euler(0f, 99f, 0f) * new Vector3(0f, 0f, radius);

        public sealed class Refs
        {
            public Transform DossierMount;
            public Transform ChancelleryMount;
            public readonly MeshRenderer[] SeatPlates = new MeshRenderer[Seats];
            public readonly TextMeshPro[] SeatNames = new TextMeshPro[Seats];
            public readonly MeshRenderer[] Banners = new MeshRenderer[BannerCount];
            public TextMeshPro Crest;
            public readonly List<Renderer> StateLights = new();
        }

        /// <param name="centre">Hall centre (the orrery), room local.</param>
        public static Refs Build(Transform room, CicArtKit art, Vector3 centre, float radius, float height, Color accent)
        {
            var refs = new Refs();
            var metal = art.MetalPanel(0.4f);
            var dark = art.DarkPanel(0.28f);
            var deck = art.DeckMat(0.45f);
            var gold = art.Lit(Texture2D.whiteTexture, accent, 2.6f);
            var cyan = art.CyanEmit(2f);
            var ring = art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture;

            // ── Shell ─────────────────────────────────────────────────────────────
            // Floor and dome are squares: their corners stay outside the round wall.
            var floor = GateRoomDecor.Box(room, "Floor", centre + new Vector3(0f, -0.05f, 0f),
                new Vector3(radius * 2f + 0.4f, 0.1f, radius * 2f + 0.4f), deck);
            floor.AddComponent<BoxCollider>();

            var bayWidth = 2f * radius * Mathf.Tan(Mathf.PI / Bays) + 0.06f;
            for (var i = 0; i < Bays; i++)
            {
                var yaw = i * 360f / Bays;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                if (IsWindow(i))
                {
                    // Tall window bay, open like the bridge hublots: sill wall, head, a transom; space beyond (the chamber stands
                    // over our ship, the shared exterior around it).
                    var sill = GateRoomDecor.Box(room, "Sill" + i, centre + rot * new Vector3(0f, SillTop * 0.5f, radius),
                        new Vector3(bayWidth, SillTop, 0.2f), dark);
                    sill.transform.localRotation = rot;
                    var col = sill.AddComponent<BoxCollider>();
                    col.size = new Vector3(1f, height / SillTop, 1f);
                    col.center = new Vector3(0f, (height / SillTop - 1f) * 0.5f, 0f);
                    var head = GateRoomDecor.Box(room, "Head" + i, centre + rot * new Vector3(0f, (WindowTop + height) * 0.5f, radius),
                        new Vector3(bayWidth, height - WindowTop, 0.2f), dark);
                    head.transform.localRotation = rot;
                    var ledge = GateRoomDecor.Box(room, "Ledge" + i, centre + rot * new Vector3(0f, SillTop, radius - 0.16f),
                        new Vector3(bayWidth, 0.06f, 0.3f), metal);
                    ledge.transform.localRotation = rot;
                    var ledgeGlow = GateRoomDecor.Box(room, "LedgeGlow" + i, centre + rot * new Vector3(0f, SillTop - 0.04f, radius - 0.31f),
                        new Vector3(bayWidth, 0.015f, 0.01f), cyan);
                    ledgeGlow.transform.localRotation = rot;
                    var transom = GateRoomDecor.Box(room, "Transom" + i, centre + rot * new Vector3(0f, (SillTop + WindowTop) * 0.5f + 0.4f, radius - 0.05f),
                        new Vector3(bayWidth, 0.07f, 0.08f), metal);
                    transom.transform.localRotation = rot;
                }
                else
                {
                    var wall = GateRoomDecor.Box(room, "Bay" + i, centre + rot * new Vector3(0f, height * 0.5f, radius),
                        new Vector3(bayWidth, height, 0.2f), dark);
                    wall.transform.localRotation = rot;
                    wall.AddComponent<BoxCollider>();
                }
                // Seam rib between bays, a lit slit down its face, a cornice under the dome.
                var seam = Quaternion.Euler(0f, yaw + 180f / Bays, 0f);
                var rib = GateRoomDecor.Box(room, "Rib" + i, centre + seam * new Vector3(0f, height * 0.5f, radius - 0.14f),
                    new Vector3(0.26f, height, 0.2f), metal);
                rib.transform.localRotation = seam;
                var slit = GateRoomDecor.Box(room, "Slit" + i, centre + seam * new Vector3(0f, height * 0.42f, radius - 0.245f),
                    new Vector3(0.04f, height * 0.62f, 0.01f), gold);
                slit.transform.localRotation = seam;
                refs.StateLights.Add(slit.GetComponent<MeshRenderer>());
                var cornice = GateRoomDecor.Box(room, "Cornice" + i, centre + rot * new Vector3(0f, height - 0.25f, radius - 0.18f),
                    new Vector3(bayWidth, 0.18f, 0.3f), metal);
                cornice.transform.localRotation = rot;
            }

            // Dome: dark cap, coffer rings of light, a gold oculus ring at the apex.
            GateRoomDecor.Box(room, "Dome", centre + new Vector3(0f, height + 0.06f, 0f),
                new Vector3(radius * 2f + 0.4f, 0.12f, radius * 2f + 0.4f), dark);
            var softGold = art.Lit(Texture2D.whiteTexture, accent, 1.6f);
            for (var k = 0; k < 3; k++)
            {
                var r = radius * (0.88f - k * 0.3f);
                Ring(room, "CofferRing" + k, centre + new Vector3(0f, height - 0.01f, 0f), r, r + 0.05f, false,
                    k == 2 ? gold : softGold);
            }

            // ── Hemicycle: three tiers on the far side, seats on the first ────────
            var tierTop = deck;
            var tierFace = metal;
            for (var t = 0; t < 3; t++)
            {
                var r0 = 3.3f + t * 0.85f;
                var r1 = t == 2 ? radius - 0.3f : r0 + 0.85f;
                var h = 0.26f * (t + 1);
                var slab = ArcSlab("Tier" + t, r0, r1, h, -ArcHalf, ArcHalf, 40);
                var go = new GameObject("Tier" + t);
                go.transform.SetParent(room, false);
                go.transform.localPosition = centre;
                go.AddComponent<MeshFilter>().sharedMesh = slab;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { tierTop, tierFace };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                go.AddComponent<MeshCollider>().sharedMesh = slab;
                // End caps where the arc stops beside the stand.
                for (var e = -1; e <= 1; e += 2)
                {
                    var capRot = Quaternion.Euler(0f, e * ArcHalf, 0f);
                    var cap = GateRoomDecor.Box(room, "TierCap", centre + capRot * new Vector3(0f, h * 0.5f, (r0 + r1) * 0.5f),
                        new Vector3(0.06f, h, r1 - r0), tierFace);
                    cap.transform.localRotation = capRot;
                }

                var strip = new GameObject("TierLight" + t);
                strip.transform.SetParent(room, false);
                strip.transform.localPosition = centre;
                strip.AddComponent<MeshFilter>().sharedMesh = ArcBand("TierLight" + t, r0 - 0.005f, h - 0.05f, h - 0.03f,
                    -ArcHalf, ArcHalf, 40);
                var sr = strip.AddComponent<MeshRenderer>();
                sr.sharedMaterial = gold;
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                refs.StateLights.Add(sr);
            }

            // Seats: a curved senate desk per empire on the first tier, flag plate on its front, name above.
            for (var i = 0; i < Seats; i++)
            {
                var a = Mathf.Lerp(-ArcHalf + 8f, ArcHalf - 8f, i / (float)(Seats - 1));
                var rot = Quaternion.Euler(0f, a, 0f);
                var seat = new GameObject("Seat" + i).transform;
                seat.SetParent(room, false);
                seat.localPosition = centre + rot * new Vector3(0f, 0.26f, 3.72f);
                // Seat local −z faces the centre (the orrery and the stand beyond).
                seat.localRotation = rot;
                GateRoomDecor.Box(seat, "Desk", new Vector3(0f, 0.42f, -0.12f), new Vector3(0.9f, 0.84f, 0.1f), dark);
                GateRoomDecor.Box(seat, "DeskTop", new Vector3(0f, 0.86f, -0.02f), new Vector3(0.95f, 0.04f, 0.32f), metal);
                GateRoomDecor.Box(seat, "Chair", new Vector3(0f, 0.72f, 0.38f), new Vector3(0.5f, 1.05f, 0.08f), dark);
                GateRoomDecor.Box(seat, "ChairEdge", new Vector3(0f, 1.25f, 0.34f), new Vector3(0.5f, 0.02f, 0.02f), cyan);
                var plate = GateRoomDecor.Quad(seat, "FlagPlate", new Vector3(0f, 0.5f, -0.175f), new Vector3(0.42f, 0.28f, 1f), dark);
                refs.SeatPlates[i] = plate.GetComponent<MeshRenderer>();
                var name = UiKit.Label(seat, "SeatName", string.Empty, new Vector3(0f, 0.2f, -0.175f), 0.85f, 0.07f, UiKit.TextDim);
                name.richText = true;
                refs.SeatNames[i] = name;
            }

            // ── Alliance banners behind the top tier, crest in the middle ─────────
            var rod = metal;
            for (var i = 0; i < BannerCount; i++)
            {
                var a = (i - (BannerCount - 1) * 0.5f) * 17f;
                if (i == BannerCount / 2)
                    continue;
                var rot = Quaternion.Euler(0f, a, 0f);
                var b = new GameObject("Banner" + i).transform;
                b.SetParent(room, false);
                b.localPosition = centre + rot * new Vector3(0f, 0f, radius - 0.38f);
                b.localRotation = rot;
                GateRoomDecor.Box(b, "Rod", new Vector3(0f, 4.95f, 0f), new Vector3(1.3f, 0.05f, 0.05f), rod);
                GateRoomDecor.Box(b, "RodCapL", new Vector3(-0.67f, 4.95f, 0f), new Vector3(0.06f, 0.1f, 0.1f), gold);
                GateRoomDecor.Box(b, "RodCapR", new Vector3(0.67f, 4.95f, 0f), new Vector3(0.06f, 0.1f, 0.1f), gold);
                var cloth = GateRoomDecor.Quad(b, "Cloth", new Vector3(0f, 3.55f, -0.01f), new Vector3(1.1f, 2.7f, 1f), dark);
                refs.Banners[i] = cloth.GetComponent<MeshRenderer>();
                GateRoomDecor.Box(b, "Hem", new Vector3(0f, 2.19f, -0.012f), new Vector3(1.1f, 0.03f, 0.01f), gold);
            }

            var crestRoot = new GameObject("Crest").transform;
            crestRoot.SetParent(room, false);
            crestRoot.localPosition = centre + new Vector3(0f, 0f, radius - 0.34f);
            GateRoomDecor.Rounded(crestRoot, "CrestPlate", new Vector3(2.1f, 1.5f, 0.08f), 0.2f, new Vector3(0f, 3.75f, 0f),
                UiKit.Chassis, accent, 0.5f);
            GateRoomDecor.Box(crestRoot, "CrestGlow", new Vector3(0f, 2.95f, -0.05f), new Vector3(1.9f, 0.025f, 0.01f), gold);
            refs.Crest = UiKit.Label(crestRoot, "CrestText", string.Empty, new Vector3(0f, 3.75f, -0.05f), 1.9f, 0.26f,
                new Color(1f, 0.86f, 0.55f), TextAlignmentOptions.Center, true);
            refs.Crest.richText = true;
            refs.Crest.fontStyle = FontStyles.Bold;

            // ── Floor: inlay rings around the orrery, the speaker's mark at the stand ─
            Ring(room, "Inlay", centre + new Vector3(0f, 0.004f, 0f), 2.75f, 2.8f, true, gold);
            Ring(room, "InlayMid", centre + new Vector3(0f, 0.004f, 0f), 2.55f, 2.57f, true, softGold);
            Ring(room, "InlayInner", centre + new Vector3(0f, 0.004f, 0f), 1.12f, 1.16f, true, cyan);
            var mark = GateRoomDecor.Quad(room, "SpeakerMark", new Vector3(0f, 0.005f, 0f), new Vector3(0.9f, 0.9f, 1f),
                art.RadarIcon(ring, new Color(accent.r, accent.g, accent.b, 0.35f)));
            mark.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (var s = -1; s <= 1; s += 2)
                GateRoomDecor.Box(room, "Aisle", new Vector3(s * 0.55f, 0.003f, 0.9f), new Vector3(0.03f, 0.006f, 2.4f), gold);

            // ── Operator desks at the stand, turned to it (dossier left, chancellery right) ─
            refs.DossierMount = GateRoomDecor.Desk(room, "DossierDesk", new Vector3(-1.15f, 0f, 0.8f),
                GateRoomDecor.FaceStand(-1.15f, 0.8f), 1.25f, metal, dark, cyan, gold);
            refs.ChancelleryMount = GateRoomDecor.Desk(room, "ChancelleryDesk", new Vector3(1.15f, 0f, 0.8f),
                GateRoomDecor.FaceStand(1.15f, 0.8f), 1.25f, metal, dark, cyan, gold);
            return refs;
        }

        // ── Meshes ────────────────────────────────────────────────────────────────

        internal static void Ring(Transform room, string name, Vector3 pos, float r0, float r1, bool up, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(room, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = Annulus(name, r0, r1, 96, up);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Flat ring in the XZ plane, facing up (floor inlay) or down (dome).</summary>
        static Mesh Annulus(string name, float r0, float r1, int segs, bool up)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            var normal = up ? Vector3.up : Vector3.down;
            for (var i = 0; i <= segs; i++)
            {
                var a = i / (float)segs * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(dir * r0); n.Add(normal); uv.Add(new Vector2(i / (float)segs, 0f));
                v.Add(dir * r1); n.Add(normal); uv.Add(new Vector2(i / (float)segs, 1f));
            }

            for (var i = 0; i < segs; i++)
            {
                var b = i * 2;
                tris.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            FixWinding(mesh, v, n);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Curved tier: top surface (submesh 0) from <paramref name="r0"/> to <paramref name="r1"/> at height
        /// <paramref name="h"/>, and its riser facing the centre (submesh 1). Angles in degrees around +y, 0 = +z.
        /// </summary>
        static Mesh ArcSlab(string name, float r0, float r1, float h, float a0, float a1, int segs)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var n = new List<Vector3>();
            var top = new List<int>();
            var face = new List<int>();
            for (var i = 0; i <= segs; i++)
            {
                var a = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var u = i / (float)segs * (a1 - a0) * Mathf.Deg2Rad * r0 * 0.5f;
                // Top: inner and outer edge.
                v.Add(dir * r0 + Vector3.up * h); uv.Add(new Vector2(u, 0f)); n.Add(Vector3.up);
                v.Add(dir * r1 + Vector3.up * h); uv.Add(new Vector2(u, (r1 - r0) * 0.5f)); n.Add(Vector3.up);
                // Riser: bottom and top of the inner edge, facing the centre.
                v.Add(dir * r0); uv.Add(new Vector2(u, 0f)); n.Add(-dir);
                v.Add(dir * r0 + Vector3.up * h); uv.Add(new Vector2(u, h * 0.5f)); n.Add(-dir);
            }

            for (var i = 0; i < segs; i++)
            {
                var b = i * 4;
                var c = b + 4;
                // Winding: (a,b,c) faces cross(b−a, c−a). Top faces up, riser faces the centre.
                top.AddRange(new[] { b, b + 1, c, c, b + 1, c + 1 });
                face.AddRange(new[] { b + 2, c + 2, b + 3, b + 3, c + 2, c + 3 });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(top, 0);
            mesh.SetTriangles(face, 1);
            FixWinding(mesh, v, n);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Thin vertical band on a circle of radius <paramref name="r"/>, facing the centre.</summary>
        static Mesh ArcBand(string name, float r, float y0, float y1, float a0, float a1, int segs)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var tris = new List<int>();
            for (var i = 0; i <= segs; i++)
            {
                var a = Mathf.Lerp(a0, a1, i / (float)segs) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                v.Add(dir * r + Vector3.up * y0); n.Add(-dir); uv.Add(new Vector2(i / (float)segs, 0f));
                v.Add(dir * r + Vector3.up * y1); n.Add(-dir); uv.Add(new Vector2(i / (float)segs, 1f));
            }

            for (var i = 0; i < segs; i++)
            {
                var b = i * 2;
                tris.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tris, 0);
            FixWinding(mesh, v, n);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flip any triangle whose winding disagrees with its vertices' normal (robust to arc direction).</summary>
        static void FixWinding(Mesh mesh, List<Vector3> v, List<Vector3> n)
        {
            for (var s = 0; s < mesh.subMeshCount; s++)
            {
                var t = mesh.GetTriangles(s);
                for (var i = 0; i < t.Length; i += 3)
                {
                    var face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (Vector3.Dot(face, n[t[i]]) < 0f)
                        (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
                }

                mesh.SetTriangles(t, s);
            }
        }
    }
}
