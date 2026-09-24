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
            var size = WorldScale.CicDeck;
            var half = size * 0.5f;
            var wallH = WorldScale.CicCeiling;
            var wallMid = wallH * 0.5f;

            BuildShell(host, art, size, half, wallH, wallMid);
            BuildCeilingArchitecture(host, art, size, half, wallH);
            BuildDeckDetail(host, art, size, half);
            BuildSideConsoles(host, art, half, wallH);
            BuildCaptainStation(host, art);
            BuildAlcoves(host, art, half);
            var map = BuildHoloTable(host, art);
            BuildAmbient(host, art);
            // Unlit CIC: brightness comes from emissive surfaces + a few soft point washes.
            host.KeyLight("Fill", new Vector3(0f, wallH - 0.7f, 0.2f), CicArtKit.Cyan, 1.15f, size * 0.95f);
            host.KeyLight("Warm", new Vector3(-2.0f, 2.15f, -1.4f), CicArtKit.Amber, 0.85f, 7f);
            host.KeyLight("WarmStbd", new Vector3(2.0f, 2.0f, -0.8f), CicArtKit.Amber, 0.55f, 6f);
            host.KeyLight("HublotWash", new Vector3(0f, 1.75f, half - 1.0f), CicArtKit.Cyan, 1.1f, 5.5f);
            host.KeyLight("DeckBounce", new Vector3(0f, 0.9f, 0.6f), new Color(0.55f, 0.7f, 0.85f), 0.65f, 5f);
            return map;
        }

        static void BuildShell(CicEnvironment host, CicArtKit art, float size, float half, float wallH, float wallMid)
        {
            // Deck with collider — brighter unlit base so the room reads inhabited.
            host.Quad("Deck", new Vector3(0f, 0f, 0f), new Vector3(size, size, 1f),
                art.DeckRib != null ? art.DeckRib : art.Floor, CicArtKit.DeckTint, 0.55f,
                tiling: 5f, rotateX: 90f, keepCollider: true,
                materialOverride: art.DeckMat(0.55f));

            // Horseshoe overhead — slightly lower aft soffit, open toward hublots.
            host.Quad("OverheadFwd", new Vector3(0f, wallH, 1.2f), new Vector3(size, size * 0.55f, 1f),
                art.Panel ?? art.Wall, new Color(0.18f, 0.22f, 0.26f), 0.28f,
                tiling: 3f, rotateX: -90f,
                materialOverride: art.DarkPanel(0.32f));
            host.Box("SoffitAft", new Vector3(0f, wallH - 0.18f, -half + 1.6f),
                new Vector3(size - 0.4f, 0.22f, 3.2f), art.SoftPanel(0.4f), keepCollider: false);

            // Floor / ceiling emissive trim.
            host.TrimRing("FloorTrim", 0.035f, size - 0.25f, CicArtKit.Cyan * 0.7f, 2.4f);
            host.TrimRing("CeilTrim", wallH - 0.05f, size - 0.3f, CicArtKit.Amber * 0.55f, 1.8f);

            // Forward windowed bulkhead (real holes).
            host.BuildWindowedForwardWall(half, wallH, wallMid, 3);

            // Aft + flanks — keep colliders (solid ship hull).
            host.Box("Aft", new Vector3(0f, wallMid, -half), new Vector3(size + 0.25f, wallH, 0.22f),
                art.MetalPanel(0.07f), keepCollider: true);
            host.Box("Port", new Vector3(-half, wallMid, 0f), new Vector3(0.22f, wallH, size + 0.25f),
                art.MetalPanel(0.07f), keepCollider: true);
            host.Box("Starboard", new Vector3(half, wallMid, 0f), new Vector3(0.22f, wallH, size + 0.25f),
                art.MetalPanel(0.07f), keepCollider: true);

            // Corner structural ribs + mid-ship frames (horseshoe read).
            float rib = half - 0.14f;
            foreach (var xz in new[]
                     {
                         new Vector3(rib, wallMid, rib), new Vector3(-rib, wallMid, rib),
                         new Vector3(rib, wallMid, -rib), new Vector3(-rib, wallMid, -rib),
                         new Vector3(rib, wallMid, 0f), new Vector3(-rib, wallMid, 0f),
                         new Vector3(0f, wallMid, -rib)
                     })
            {
                host.Box("Frame", xz, new Vector3(0.16f, wallH, 0.16f), art.DarkPanel(0.04f), keepCollider: true);
            }

            // Hublot apertures + mounts (centre one sits under the main viewscreen).
            for (var i = 0; i < 3; i++)
            {
                var t = (i + 1f) / 4f;
                var x = Mathf.Lerp(-half + 1.4f, half - 1.4f, t);
                host.Viewport(new Vector3(x, WorldScale.CicHublotCenterY, half - 0.04f),
                    new Vector3(WorldScale.CicHublotWidth, WorldScale.CicHublotHeight, 1f),
                    registerMount: true, openHole: true);
            }

            host.StripLight(new Vector3(0f, wallH - 0.06f, 0.8f), size * 0.55f);
        }

        static void BuildCeilingArchitecture(CicEnvironment host, CicArtKit art, float size, float half, float wallH)
        {
            // Transverse barrots.
            for (var i = 0; i < 5; i++)
            {
                var z = Mathf.Lerp(-half + 1.2f, half - 1.5f, i / 4f);
                host.Box("Barrot_" + i, new Vector3(0f, wallH - 0.12f, z),
                    new Vector3(size - 0.5f, 0.1f, 0.14f), art.DarkPanel(0.05f), keepCollider: false);
            }

            // Longitudinal runners.
            host.Box("RunnerL", new Vector3(-2.4f, wallH - 0.2f, 0.2f),
                new Vector3(0.1f, 0.08f, size - 1.2f), art.SoftPanel(0.08f), keepCollider: false);
            host.Box("RunnerR", new Vector3(2.4f, wallH - 0.2f, 0.2f),
                new Vector3(0.1f, 0.08f, size - 1.2f), art.SoftPanel(0.08f), keepCollider: false);

            // Overhead light panels (emissive bounce for unlit materials).
            host.Box("LightPanelC", new Vector3(0f, wallH - 0.1f, 1.0f),
                new Vector3(2.8f, 0.04f, 0.55f), art.CyanEmit(2.8f), keepCollider: false);
            host.Box("LightPanelP", new Vector3(-2.6f, wallH - 0.12f, -0.5f),
                new Vector3(1.4f, 0.04f, 0.4f), art.CyanEmit(2.2f), keepCollider: false);
            host.Box("LightPanelS", new Vector3(2.6f, wallH - 0.12f, -0.5f),
                new Vector3(1.4f, 0.04f, 0.4f), art.AmberEmit(1.9f), keepCollider: false);

            // Cable conduits along runners.
            for (var i = 0; i < 6; i++)
            {
                var z = -4f + i * 1.5f;
                host.Cylinder("CableL_" + i, new Vector3(-2.55f, wallH - 0.42f, z),
                    new Vector3(0.04f, 0.35f, 0.04f), art.Lit(art.Wall, CicArtKit.DarkMetal, 0.15f),
                    keepCollider: false);
                host.Cylinder("CableR_" + i, new Vector3(2.55f, wallH - 0.42f, z),
                    new Vector3(0.04f, 0.35f, 0.04f), art.Lit(art.Wall, CicArtKit.DarkMetal, 0.15f),
                    keepCollider: false);
            }

            // Vent grilles on flank bulkheads.
            var ventMat = art.Lit(art.Vent != null ? art.Vent : art.Wall, Color.white, 0.25f);
            var vp = host.Quad("VentPort", new Vector3(-half + 0.12f, 2.35f, 0.5f), new Vector3(0.9f, 0.55f, 1f),
                art.Vent, Color.white, 0.2f, materialOverride: ventMat);
            vp.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            var vs = host.Quad("VentStbd", new Vector3(half - 0.12f, 2.35f, 0.5f), new Vector3(0.9f, 0.55f, 1f),
                art.Vent, Color.white, 0.2f, materialOverride: ventMat);
            vs.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        }

        static void BuildDeckDetail(CicEnvironment host, CicArtKit art, float size, float half)
        {
            // Raised walk plates / coursives toward hublots (U path).
            host.Box("CoursePort", new Vector3(-3.6f, 0.04f, 1.2f),
                new Vector3(1.4f, 0.08f, 7.5f), art.SoftPanel(0.65f), keepCollider: true);
            host.Box("CourseStbd", new Vector3(3.6f, 0.04f, 1.2f),
                new Vector3(1.4f, 0.08f, 7.5f), art.SoftPanel(0.65f), keepCollider: true);
            host.Box("CourseFwd", new Vector3(0f, 0.04f, half - 1.6f),
                new Vector3(8.2f, 0.08f, 1.5f), art.SoftPanel(0.65f), keepCollider: true);

            // Center command plate under table / captain.
            host.Box("CommandPlate", new Vector3(0f, 0.03f, -0.3f),
                new Vector3(3.4f, 0.06f, 4.6f), art.MetalPanel(0.7f), keepCollider: true);

            // Floor cable trenches (visual).
            host.Box("TrenchL", new Vector3(-1.1f, 0.02f, 0.2f),
                new Vector3(0.12f, 0.03f, 4.5f), art.CyanEmit(1.4f), keepCollider: false);
            host.Box("TrenchR", new Vector3(1.1f, 0.02f, 0.2f),
                new Vector3(0.12f, 0.03f, 4.5f), art.AmberEmit(1.0f), keepCollider: false);

            // Fire bottles / lockers aft.
            host.Box("LockerA", new Vector3(-4.2f, 0.55f, -half + 0.55f),
                new Vector3(0.55f, 1.1f, 0.4f), art.DarkPanel(0.08f), keepCollider: true);
            host.Box("LockerB", new Vector3(4.2f, 0.55f, -half + 0.55f),
                new Vector3(0.55f, 1.1f, 0.4f), art.DarkPanel(0.08f), keepCollider: true);
            host.Cylinder("ExtinguisherL", new Vector3(-3.5f, 0.55f, -half + 0.5f),
                new Vector3(0.12f, 0.45f, 0.12f), art.Lit(Texture2D.whiteTexture, new Color(0.75f, 0.15f, 0.12f), 0.3f),
                keepCollider: false);
            host.Cylinder("ExtinguisherR", new Vector3(3.5f, 0.55f, -half + 0.5f),
                new Vector3(0.12f, 0.45f, 0.12f), art.Lit(Texture2D.whiteTexture, new Color(0.75f, 0.15f, 0.12f), 0.3f),
                keepCollider: false);
        }

        static void BuildSideConsoles(CicEnvironment host, CicArtKit art, float half, float wallH)
        {
            // Port / starboard data columns + idle screens.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * (half - 0.55f);
                for (var i = 0; i < 3; i++)
                {
                    var z = -2.2f + i * 2.2f;
                    host.Box($"DataCol_{(side < 0 ? "P" : "S")}_{i}",
                        new Vector3(x, 1.15f, z), new Vector3(0.35f, 2.1f, 0.55f),
                        art.DarkPanel(0.06f), keepCollider: true);
                    host.Box($"Screen_{(side < 0 ? "P" : "S")}_{i}",
                        new Vector3(x - side * 0.2f, 1.35f, z),
                        new Vector3(0.04f, 0.7f, 0.42f),
                        art.Lit(art.ScreenIdle != null ? art.ScreenIdle : art.Wall,
                            new Color(0.15f, 0.35f, 0.45f), 0.55f),
                        keepCollider: false);
                    host.Box($"Edge_{(side < 0 ? "P" : "S")}_{i}",
                        new Vector3(x - side * 0.22f, 1.72f, z),
                        new Vector3(0.03f, 0.03f, 0.4f),
                        side < 0 ? art.CyanEmit(2.8f) : art.AmberEmit(2.2f),
                        keepCollider: false);
                }

            }

            // Overhead strip accents.
            host.Box("StripCyan", new Vector3(0f, wallH - 0.08f, 2.5f),
                new Vector3(6f, 0.04f, 0.08f), art.CyanEmit(3.5f), keepCollider: false);
            host.Box("StripAmber", new Vector3(0f, wallH - 0.08f, -2.8f),
                new Vector3(4.5f, 0.04f, 0.08f), art.AmberEmit(2.4f), keepCollider: false);
        }

        static void BuildCaptainStation(CicEnvironment host, CicArtKit art)
        {
            // Aft of table, facing +Z (hublots + table). Not a cinema seat.
            var chairZ = WorldScale.CicCaptainChairZ;
            var station = new GameObject("CaptainStation");
            station.transform.SetParent(host.transform, false);
            station.transform.localPosition = Vector3.zero;

            host.Box("CaptainDais", new Vector3(0f, 0.08f, chairZ),
                new Vector3(1.6f, 0.16f, 1.3f), art.MetalPanel(0.1f), keepCollider: true);
            host.Box("CaptainRailL", new Vector3(-0.75f, 0.55f, chairZ - 0.15f),
                new Vector3(0.08f, 0.7f, 0.08f), art.DarkPanel(0.08f), keepCollider: false);
            host.Box("CaptainRailR", new Vector3(0.75f, 0.55f, chairZ - 0.15f),
                new Vector3(0.08f, 0.7f, 0.08f), art.DarkPanel(0.08f), keepCollider: false);
            host.Box("CaptainRailBar", new Vector3(0f, 0.9f, chairZ - 0.15f),
                new Vector3(1.55f, 0.06f, 0.06f), art.CyanEmit(2.0f), keepCollider: false);

            // Seat shell.
            host.Box("CaptainSeat", new Vector3(0f, 0.52f, chairZ + 0.05f),
                new Vector3(0.7f, 0.12f, 0.65f), art.DarkPanel(0.12f), keepCollider: true);
            host.Box("CaptainBack", new Vector3(0f, 0.95f, chairZ - 0.28f),
                new Vector3(0.7f, 0.85f, 0.12f), art.DarkPanel(0.1f), keepCollider: true);
            host.Box("CaptainArmL", new Vector3(-0.42f, 0.62f, chairZ + 0.05f),
                new Vector3(0.1f, 0.18f, 0.55f), art.SoftPanel(0.15f), keepCollider: false);
            host.Box("CaptainArmR", new Vector3(0.42f, 0.62f, chairZ + 0.05f),
                new Vector3(0.1f, 0.18f, 0.55f), art.SoftPanel(0.15f), keepCollider: false);
            host.Box("CaptainHeadrest", new Vector3(0f, 1.35f, chairZ - 0.28f),
                new Vector3(0.45f, 0.18f, 0.1f), art.MetalPanel(0.2f), keepCollider: false);

            // Arm consoles: true-size rounded hardware (unscaled) so buttons sit on their top face.
            host.Rounded("ArmPadL", new Vector3(-0.42f, 0.74f, chairZ + 0.15f),
                new Vector3(0.18f, 0.03f, 0.28f), 0.01f, CicArtKit.Cyan, 0.4f);
            host.Rounded("ArmPadR", new Vector3(0.42f, 0.74f, chairZ + 0.15f),
                new Vector3(0.18f, 0.03f, 0.28f), 0.01f, CicArtKit.Amber, 0.4f);

            host.KeyLight("CaptainLamp", new Vector3(0f, 2.15f, chairZ), CicArtKit.Amber, 1.05f, 4f);
        }

        static void BuildAlcoves(CicEnvironment host, CicArtKit art, float half)
        {
            // Astrometry — near port hublot.
            BuildAlcoveShell(host, art, "AlcoveAstrometry", new Vector3(-4.0f, 0f, half - 2.4f),
                yaw: 35f, accent: CicArtKit.Cyan);
            host.Box("AstroScope", new Vector3(-4.0f, 1.15f, half - 2.1f),
                new Vector3(0.9f, 0.08f, 0.55f), art.SoftPanel(0.15f), keepCollider: true);
            host.Cylinder("AstroLens", new Vector3(-4.0f, 1.35f, half - 1.95f),
                new Vector3(0.22f, 0.08f, 0.22f), art.Holo(art.HoloPlate, new Color(0.2f, 0.9f, 1f, 0.5f)),
                keepCollider: false);

            // Planet desk — starboard forward.
            BuildAlcoveShell(host, art, "AlcovePlanet", new Vector3(4.0f, 0f, half - 2.4f),
                yaw: -35f, accent: CicArtKit.Amber);
            host.Cylinder("PlanetMaquette", new Vector3(4.0f, 1.2f, half - 2.15f),
                new Vector3(0.28f, 0.14f, 0.28f),
                art.Holo(Texture2D.whiteTexture, new Color(0.35f, 0.75f, 1f, 0.65f)),
                keepCollider: false);

            var planetSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            planetSphere.name = "PlanetGlobe";
            planetSphere.transform.SetParent(host.transform, false);
            planetSphere.transform.localPosition = new Vector3(4.0f, 1.45f, half - 2.15f);
            planetSphere.transform.localScale = Vector3.one * 0.32f;
            CicEnvironment.DropColliderStatic(planetSphere);
            planetSphere.GetComponent<MeshRenderer>().sharedMaterial =
                art.Holo(Texture2D.whiteTexture, new Color(0.3f, 0.7f, 1f, 0.7f));
            var spin = planetSphere.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 18f;
            spin.BobMeters = 0.01f;

            // Shipyard 9×9 maquette — port aft.
            BuildAlcoveShell(host, art, "AlcoveShipyard", new Vector3(-4.2f, 0f, -2.8f),
                yaw: 90f, accent: CicArtKit.Cyan);
            BuildMiniGrid(host, art, new Vector3(-4.15f, 1.05f, -2.8f));

            // Comms mural — starboard aft.
            BuildAlcoveShell(host, art, "AlcoveComms", new Vector3(4.2f, 0f, -2.8f),
                yaw: -90f, accent: CicArtKit.Amber);
            host.Box("CommsScreen", new Vector3(half - 0.35f, 1.55f, -2.8f),
                new Vector3(0.06f, 1.2f, 1.6f),
                art.Lit(art.ScreenIdle != null ? art.ScreenIdle : art.Wall,
                    new Color(0.12f, 0.4f, 0.5f), 0.7f),
                keepCollider: false);
            host.Box("CommsBezel", new Vector3(half - 0.4f, 1.55f, -2.8f),
                new Vector3(0.05f, 1.35f, 1.75f), art.DarkPanel(0.08f), keepCollider: true);
            host.Box("CommsEdge", new Vector3(half - 0.42f, 2.2f, -2.8f),
                new Vector3(0.04f, 0.04f, 1.7f), art.AmberEmit(2.8f), keepCollider: false);
        }

        static void BuildAlcoveShell(CicEnvironment host, CicArtKit art, string name, Vector3 pos, float yaw,
            Color accent)
        {
            var root = new GameObject(name);
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = pos;
            root.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            desk.name = "Desk";
            desk.transform.SetParent(root.transform, false);
            desk.transform.localPosition = new Vector3(0f, 0.9f, 0.15f);
            desk.transform.localScale = new Vector3(1.35f, 0.1f, 0.7f);
            desk.GetComponent<MeshRenderer>().sharedMaterial = art.MetalPanel(0.12f);

            var baseCol = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseCol.name = "Pedestal";
            baseCol.transform.SetParent(root.transform, false);
            baseCol.transform.localPosition = new Vector3(0f, 0.4f, 0.15f);
            baseCol.transform.localScale = new Vector3(0.55f, 0.8f, 0.45f);
            baseCol.GetComponent<MeshRenderer>().sharedMaterial = art.DarkPanel(0.06f);

            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "Accent";
            strip.transform.SetParent(root.transform, false);
            strip.transform.localPosition = new Vector3(0f, 0.96f, -0.15f);
            strip.transform.localScale = new Vector3(1.2f, 0.03f, 0.04f);
            CicEnvironment.DropColliderStatic(strip);
            strip.GetComponent<MeshRenderer>().sharedMaterial =
                art.Lit(Texture2D.whiteTexture, accent, 2.6f);

            var hood = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hood.name = "Hood";
            hood.transform.SetParent(root.transform, false);
            hood.transform.localPosition = new Vector3(0f, 1.35f, -0.05f);
            hood.transform.localScale = new Vector3(1.4f, 0.08f, 0.85f);
            hood.GetComponent<MeshRenderer>().sharedMaterial = art.SoftPanel(0.08f);
        }

        static void BuildMiniGrid(CicEnvironment host, CicArtKit art, Vector3 pos)
        {
            var root = new GameObject("YardMaquette");
            root.transform.SetParent(host.transform, false);
            root.transform.localPosition = pos;

            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "GridBoard";
            board.transform.SetParent(root.transform, false);
            board.transform.localPosition = Vector3.zero;
            board.transform.localScale = new Vector3(0.72f, 0.04f, 0.72f);
            board.GetComponent<MeshRenderer>().sharedMaterial = art.DarkPanel(0.1f);

            const int n = 9;
            const float cell = 0.07f;
            var origin = -cell * (n - 1) * 0.5f;
            for (var gx = 0; gx < n; gx++)
            {
                for (var gy = 0; gy < n; gy++)
                {
                    // Sparse occupied cells — silhouette of a small hull (stable).
                    var occupied = (gx == 4 && gy >= 2 && gy <= 6) ||
                                   (gy == 4 && gx >= 3 && gx <= 5) ||
                                   (gx == 4 && gy == 7) ||
                                   ((gx == 3 || gx == 5) && gy == 2);
                    if (!occupied)
                        continue;
                    var cellGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cellGo.name = $"Cell_{gx}_{gy}";
                    cellGo.transform.SetParent(root.transform, false);
                    cellGo.transform.localPosition = new Vector3(origin + gx * cell, 0.06f, origin + gy * cell);
                    cellGo.transform.localScale = new Vector3(cell * 0.85f, 0.08f, cell * 0.85f);
                    CicEnvironment.DropColliderStatic(cellGo);
                    var engine = gy <= 2;
                    cellGo.GetComponent<MeshRenderer>().sharedMaterial =
                        engine ? art.AmberEmit(2.2f) : art.CyanEmit(2.0f);
                }
            }
        }

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

        static void BuildAmbient(CicEnvironment host, CicArtKit art)
        {
            if (art.Ambient == null)
                return;
            var go = new GameObject("BridgeAmbient");
            go.transform.SetParent(host.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, 1f);
            var src = go.AddComponent<AudioSource>();
            src.clip = art.Ambient;
            src.loop = true;
            src.playOnAwake = true;
            src.spatialBlend = 0.65f;
            src.volume = 0.28f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 14f;
            src.dopplerLevel = 0f;
            if (Application.isPlaying)
                src.Play();
        }
    }
}
