using Core.UI;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// Bridge CIC interior: horseshoe deck facing hublots, captain station, alcoves, shared mats.
    /// Structural meshes keep colliders so room-scale locomotion cannot walk into the void.
    /// </summary>
    public static class CicBridgeInterior
    {
        public static HoloZoneMap Build(CicEnvironment host, CicArtKit art)
        {
            BridgeShell.Build(host.transform, art);
            BuildDeckInlays(host, art);
            // At an orbital station the same crew posts stand in a round command hall (BridgeDressing swaps them).
            StationCommandShell.Build(host.transform, art);
            BuildCaptainStation(host, art);
            var map = BuildHoloTable(host, art);
            Core.Audio.AmbienceDirector.Ensure();
            BuildHoloHum(host);
            // Four room lights shade the shell (SU/HullInterior via RoomLightRig): the sky panel over the table,
            // the viewscreen's wash on the bow, and two warm pools by the aft doors.
            host.KeyLight("Fill", new Vector3(0f, 3.4f, 0.5f), new Color(0.62f, 0.8f, 1f), 1.2f, 10f);
            host.KeyLight("HublotWash", new Vector3(0f, 1.9f, 4.9f), CicArtKit.Cyan, 0.9f, 5.5f);
            host.KeyLight("Warm", new Vector3(-3.4f, 2.5f, -4.3f), CicArtKit.Amber, 0.85f, 5.5f);
            host.KeyLight("WarmStbd", new Vector3(3.4f, 2.5f, -4.3f), CicArtKit.Amber, 0.85f, 5.5f);
            RoomLightRig.Attach(host.transform, "Fill", "HublotWash", "Warm", "WarmStbd");
            return map;
        }

        /// <summary>
        /// Deck inlays: a raised command plate (chamfered octagon) under the table and the captain with a lit
        /// edge, and two lit runners from the aft doors to it. Shared meshes, one hull material.
        /// </summary>
        static void BuildDeckInlays(CicEnvironment host, CicArtKit art)
        {
            var plate = new ShellMesh();
            var edge = new ShellMesh();
            var outline = BridgeShell.Chamfered(-1.95f, 1.95f, -2.35f, 2.05f, 0.9f);
            const float h = 0.035f;
            const float bevel = 0.05f;
            var c = Vector3.zero;
            foreach (var p in outline)
                c += new Vector3(p.x, h, p.y);
            c /= outline.Length;
            for (var i = 0; i < outline.Length; i++)
            {
                var a2 = outline[i];
                var b2 = outline[(i + 1) % outline.Length];
                var a = new Vector3(a2.x, 0f, a2.y);
                var b = new Vector3(b2.x, 0f, b2.y);
                var at = Vector3.Lerp(a, new Vector3(c.x, 0f, c.z), bevel / Mathf.Max(0.1f, (a - c).magnitude)) + Vector3.up * h;
                var bt = Vector3.Lerp(b, new Vector3(c.x, 0f, c.z), bevel / Mathf.Max(0.1f, (b - c).magnitude)) + Vector3.up * h;
                plate.Tri(c, at, bt, Vector3.up, new Vector2(c.x, c.z), new Vector2(at.x, at.z), new Vector2(bt.x, bt.z), 1f, 0.95f, 0.95f);
                var outward = ((a + b) * 0.5f - new Vector3(c.x, 0f, c.z)).normalized + Vector3.up;
                plate.Quad(a, b, bt, at, outward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.6f, 0.6f, 0.9f, 0.9f);
                // Lit groove just outside the plate.
                var ga = a + (a - new Vector3(c.x, 0f, c.z)).normalized * 0.05f;
                var gb = b + (b - new Vector3(c.x, 0f, c.z)).normalized * 0.05f;
                edge.Quad(a + Vector3.up * 0.004f, b + Vector3.up * 0.004f, gb + Vector3.up * 0.004f, ga + Vector3.up * 0.004f, Vector3.up,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            }

            foreach (var x in new[] { -0.95f, 0.95f })
            {
                var a = new Vector3(x - 0.03f, 0.004f, -5.85f);
                var b = new Vector3(x + 0.03f, 0.004f, -5.85f);
                edge.Quad(a, b, new Vector3(x + 0.03f, 0.004f, -2.55f), new Vector3(x - 0.03f, 0.004f, -2.55f), Vector3.up,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            }

            Piece(host.transform, "CommandPlate", plate.ToMesh("SU_BridgeCommandPlate"),
                art.Hull(art.Panel, new Color(0.5f, 0.56f, 0.63f), new Vector2(0.9f, 0.9f), tiling: 0.8f, seam: 0.5f), true);
            Piece(host.transform, "DeckStrip", edge.ToMesh("SU_BridgeDeckStrip"), art.CyanEmit(1.0f), false);
        }

        static void Piece(Transform parent, string name, Mesh mesh, Material mat, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider)
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        static void BuildCaptainStation(CicEnvironment host, CicArtKit art)
        {
            // Aft of the table, facing +Z (hublots + table). A command chair at human size: seat top 0.46 m
            // above its dais (0.62 m above the deck), a raked back with headrest, armrests at elbow height,
            // and at the front of each armrest a short arm lifting a tablet toward the seated captain.
            var chairZ = WorldScale.CicCaptainChairZ;
            var station = new GameObject("CaptainStation");
            station.transform.SetParent(host.transform, false);
            station.transform.localPosition = Vector3.zero;

            host.Box("CaptainDais", new Vector3(0f, 0.08f, chairZ),
                new Vector3(1.6f, 0.16f, 1.3f), art.MetalPanel(0.1f), keepCollider: true);
            // Lit edge of the dais, the step one sees walking up to the chair.
            host.Box("CaptainDaisEdge", new Vector3(0f, 0.163f, chairZ + 0.645f),
                new Vector3(1.56f, 0.008f, 0.012f), art.CyanEmit(1.6f), keepCollider: false);
            host.Box("CaptainRailL", new Vector3(-0.75f, 0.55f, chairZ - 0.5f),
                new Vector3(0.08f, 0.78f, 0.08f), art.DarkPanel(0.08f), keepCollider: false);
            host.Box("CaptainRailR", new Vector3(0.75f, 0.55f, chairZ - 0.5f),
                new Vector3(0.08f, 0.78f, 0.08f), art.DarkPanel(0.08f), keepCollider: false);
            host.Box("CaptainRailBar", new Vector3(0f, 0.94f, chairZ - 0.5f),
                new Vector3(1.55f, 0.05f, 0.05f), art.CyanEmit(2.0f), keepCollider: false);

            var shell = art.DarkPanel(0.12f);
            var cushion = art.SoftPanel(0.14f);
            var metal = art.MetalPanel(0.22f);
            // Swivel column and foot on the dais.
            host.Cylinder("CaptainFoot", new Vector3(0f, 0.18f, chairZ), new Vector3(0.52f, 0.02f, 0.52f), metal);
            host.Cylinder("CaptainColumn", new Vector3(0f, 0.34f, chairZ), new Vector3(0.14f, 0.16f, 0.14f), shell);
            // Seat: a shell and its cushion (collider = the seat you point at to sit).
            Rounded(host, "CaptainSeatShell", new Vector3(0f, 0.53f, chairZ + 0.02f), new Vector3(0.62f, 0.08f, 0.56f), 0.03f, shell);
            var seat = Rounded(host, "CaptainSeat", new Vector3(0f, 0.595f, chairZ + 0.03f), new Vector3(0.54f, 0.07f, 0.5f), 0.03f, cushion);
            // One box over seat and back: what the ray points at to sit (CaptainCommandMode).
            var sitBox = seat.AddComponent<BoxCollider>();
            sitBox.size = new Vector3(0.64f, 0.95f, 0.62f);
            sitBox.center = new Vector3(0f, 0.43f, -0.1f);
            // Back, raked 12°: shell, cushion, headrest.
            var back = new GameObject("CaptainBackFrame").transform;
            back.SetParent(host.transform, false);
            back.localPosition = new Vector3(0f, 0.6f, chairZ - 0.25f);
            back.localRotation = Quaternion.Euler(-12f, 0f, 0f);
            UiKit.MeshPiece(back, "CaptainBack", UiMeshes.RoundedBox(new Vector3(0.6f, 0.78f, 0.09f), 0.035f), shell,
                new Vector3(0f, 0.39f, -0.02f));
            UiKit.MeshPiece(back, "CaptainBackCushion", UiMeshes.RoundedBox(new Vector3(0.48f, 0.56f, 0.05f), 0.025f), cushion,
                new Vector3(0f, 0.34f, 0.045f));
            UiKit.MeshPiece(back, "CaptainHeadrest", UiMeshes.RoundedBox(new Vector3(0.34f, 0.15f, 0.09f), 0.04f), metal,
                new Vector3(0f, 0.86f, 0.02f));
            UiKit.MeshPiece(back, "CaptainBackLight", UiMeshes.RoundedBox(new Vector3(0.012f, 0.6f, 0.012f), 0.005f), art.AmberEmit(2.2f),
                new Vector3(0f, 0.38f, -0.07f));
            // Armrests: an upright under each, the rest at elbow height (0.2 m above the cushion).
            foreach (var side in new[] { -1f, 1f })
            {
                var n = side < 0f ? "L" : "R";
                Rounded(host, "CaptainArmPost" + n, new Vector3(side * 0.33f, 0.66f, chairZ + 0.02f), new Vector3(0.05f, 0.14f, 0.05f), 0.02f, metal);
                Rounded(host, "CaptainArm" + n, new Vector3(side * 0.34f, 0.77f, chairZ + 0.02f), new Vector3(0.09f, 0.05f, 0.5f), 0.022f, cushion);
            }

            // Arm consoles at the front end of each armrest: true-size rounded hardware (unscaled); the tablets
            // rise from here (ArmConsole) toward the seated captain's eyes.
            host.Rounded("ArmPadL", new Vector3(-0.34f, 0.81f, chairZ + 0.24f),
                new Vector3(0.12f, 0.03f, 0.1f), 0.01f, CicArtKit.Cyan, 0.4f);
            host.Rounded("ArmPadR", new Vector3(0.34f, 0.81f, chairZ + 0.24f),
                new Vector3(0.12f, 0.03f, 0.1f), 0.01f, CicArtKit.Amber, 0.4f);

            host.KeyLight("CaptainLamp", new Vector3(0f, 2.15f, chairZ), CicArtKit.Amber, 1.05f, 4f);
        }

        static GameObject Rounded(CicEnvironment host, string name, Vector3 pos, Vector3 size, float radius, Material mat) =>
            UiKit.MeshPiece(host.transform, name, UiMeshes.RoundedBox(size, radius), mat, pos);

        static HoloZoneMap BuildHoloTable(CicEnvironment host, CicArtKit art)
        {
            var tablePos = new Vector3(0f, 0f, WorldScale.CicTableCenterZ);
            var diam = WorldScale.CicTableDiameter;
            var radius = diam * 0.5f;
            var h = WorldScale.CicTableHeight;

            host.Cylinder("Pedestal", tablePos + new Vector3(0f, 0.36f, 0f),
                new Vector3(0.52f, 0.36f, 0.52f), art.SoftPanel(0.08f), keepCollider: true);
            host.Box("PedestalRing", tablePos + new Vector3(0f, 0.74f, 0f),
                new Vector3(1.15f, 0.05f, 1.15f), art.DarkPanel(0.1f), keepCollider: false);

            // Projector housing under plate.
            host.Cylinder("ProjectorCore", tablePos + new Vector3(0f, 0.78f, 0f),
                new Vector3(0.35f, 0.06f, 0.35f), art.CyanEmit(1.6f), keepCollider: false);
            host.Cylinder("ProjectorLens", tablePos + new Vector3(0f, 0.82f, 0f),
                new Vector3(0.55f, 0.03f, 0.55f),
                art.Holo(art.ProjectorGlow != null ? art.ProjectorGlow : art.HoloPlate,
                    new Color(0.2f, 0.9f, 1f, 0.45f)),
                keepCollider: false);

            var rim = host.Cylinder("TableRim", tablePos + new Vector3(0f, h - 0.04f, 0f),
                new Vector3(radius + 0.08f, 0.035f, radius + 0.08f), art.MetalPanel(0.25f),
                keepCollider: true);
            _ = rim;

            // Opaque brushed plate — holo map mounts on an UNIFORM scale node
            // (never parent tokens under the scaled cylinder — Y would crush to ~0).
            var table = host.Cylinder("HoloTable", tablePos + new Vector3(0f, h, 0f),
                new Vector3(radius, 0.02f, radius),
                art.DarkPanel(0.15f),
                keepCollider: true);
            host.Table = table;

            // Thin additive veil on the plate surface.
            host.Cylinder("HoloVeil", tablePos + new Vector3(0f, h + 0.015f, 0f),
                new Vector3(radius * 0.96f, 0.006f, radius * 0.96f),
                art.Holo(art.HoloPlate, new Color(0.12f, 0.55f, 0.8f, 0.16f)),
                keepCollider: false);

            // Rim emissive band (subtle).
            host.Cylinder("TableGlowRing", tablePos + new Vector3(0f, h + 0.01f, 0f),
                new Vector3(radius + 0.02f, 0.006f, radius + 0.02f), art.CyanEmit(0.85f),
                keepCollider: false);

            var mapMount = new GameObject("HoloMapMount");
            mapMount.transform.SetParent(host.transform, false);
            mapMount.transform.localPosition = tablePos + new Vector3(0f, h + 0.02f, 0f);
            mapMount.transform.localRotation = Quaternion.identity;
            mapMount.transform.localScale = Vector3.one;

            var map = mapMount.AddComponent<HoloZoneMap>();
            map.EnsureScaffold(mapMount.transform, art);

            host.KeyLight("TableGlow", tablePos + new Vector3(0f, h + 0.45f, 0f), CicArtKit.Cyan, 0.7f, 2.6f);
            host.KeyLight("TableAmber", tablePos + new Vector3(0.4f, h + 0.25f, -0.25f), CicArtKit.Amber, 0.4f, 2.2f);
            host.KeyLight("TableUnder", tablePos + new Vector3(0f, 0.65f, 0f), CicArtKit.Cyan * 0.7f, 0.35f, 1.6f);
            return map;
        }

        /// <summary>The holo table's projector: a faint spatial buzz and shimmer you hear leaning over it.</summary>
        static void BuildHoloHum(CicEnvironment host)
        {
            if (!Application.isPlaying)
                return;
            host.gameObject.AddComponent<Core.Audio.HoloHum>().Bind(host.transform,
                new Vector3(0f, WorldScale.CicTableHeight + 0.2f, WorldScale.CicTableCenterZ));
        }
    }
}
