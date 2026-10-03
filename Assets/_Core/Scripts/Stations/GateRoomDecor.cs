using System.Collections.Generic;
using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Set dressing of the gate base, all procedural and on shared materials:
    /// command gallery — sloped operator desks carrying the holo screens on arms, a central dial pedestal whose
    /// six lamps follow the gate's locks, two technicians at side desks, server racks with LED faces, a wall
    /// monitor over the exit, ceiling cable trays and light bars, hazard edging along the rail;
    /// embarkation hall — a blast door for the teams, cargo stacks, gate spotlights, wall turrets flanking the
    /// gate, floor markings (causeway chevrons, the splash zone in front of the horizon).
    /// </summary>
    public static class GateRoomDecor
    {
        static Texture2D _hazard;
        static Texture2D _leds;

        public sealed class Refs
        {
            public readonly Renderer[] PedestalLamps = new Renderer[6];
            public TextMeshPro WallMonitor;
            public Transform ConsoleMount;
            public Transform JournalMount;
        }

        public static Refs Build(Transform room, CicArtKit art, float hallFloor, float hallWidth, float hallHeight,
            float galleryEdge, Vector3 gatePos, Color accent)
        {
            var refs = new Refs();
            var metal = art.MetalPanel(0.4f);
            var dark = art.DarkPanel(0.3f);
            var chassis = UiKit.Chassis;
            var cyan = art.CyanEmit(2.2f);
            var amber = art.AmberEmit(2.2f);
            var violet = art.Lit(Texture2D.whiteTexture, accent, 2.4f);
            Material Hazard(float tiles)
            {
                var m = art.Lit(HazardTex(), Color.white, 0.55f, 1f);
                m.mainTextureScale = new Vector2(tiles, 1f);
                return m;
            }
            var screenIdle = art.ScreenIdle != null ? art.Lit(art.ScreenIdle, new Color(0.6f, 0.9f, 1f), 1.6f) : cyan;
            var half = hallWidth * 0.5f;

            // ── Command gallery ───────────────────────────────────────────────────
            // Both command desks are turned to the captain's stand (room origin): desk, arm and screen share it.
            refs.ConsoleMount = Desk(room, "CommandDesk", new Vector3(-1.05f, 0f, 0.95f), FaceStand(-1.05f, 0.95f), 1.25f,
                metal, dark, cyan, amber);
            refs.JournalMount = Desk(room, "LogDesk", new Vector3(1.15f, 0f, 0.9f), FaceStand(1.15f, 0.9f), 1.0f,
                metal, dark, cyan, amber);

            // Dial pedestal between the desks: six lamps around a glyph plate, mirrors the gate's locks.
            var ped = new GameObject("DialPedestal").transform;
            ped.SetParent(room, false);
            ped.localPosition = new Vector3(0f, 0f, 1.05f);
            Rounded(ped, "Column", new Vector3(0.34f, 0.8f, 0.34f), 0.06f, new Vector3(0f, 0.4f, 0f), chassis, accent, 0.25f);
            Rounded(ped, "Crown", new Vector3(0.56f, 0.1f, 0.56f), 0.04f, new Vector3(0f, 0.86f, 0f), chassis, accent, 0.5f);
            var plate = Quad(ped, "GlyphPlate", new Vector3(0f, 0.915f, 0f), new Vector3(0.44f, 0.44f, 1f),
                art.RadarIcon(art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture, new Color(accent.r, accent.g, accent.b, 0.8f)));
            plate.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            for (var i = 0; i < 6; i++)
            {
                var a = GateRing.LockAngle(i) * Mathf.Deg2Rad;
                var lamp = Rounded(ped, "Lamp" + i, new Vector3(0.06f, 0.03f, 0.06f), 0.012f,
                    new Vector3(Mathf.Cos(a) * 0.23f, 0.925f, Mathf.Sin(a) * 0.23f), chassis, new Color(0.3f, 0.14f, 0.06f), 0.6f);
                refs.PedestalLamps[i] = lamp.GetComponent<Renderer>();
            }

            // Technicians at side desks, facing the hall.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * 3.6f;
                var desk = Desk(room, side < 0 ? "TechDeskL" : "TechDeskR", new Vector3(x, 0f, 0.8f), side * -8f, 0.7f, metal, dark,
                    cyan, amber);
                // Seated on the arm like the command screens: bottom on the arm head, top leaning back 15°.
                var mon = Quad(desk, "Monitor", new Vector3(0f, 0.185f, 0.05f), new Vector3(0.62f, 0.36f, 1f), screenIdle);
                mon.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                var back = Box(desk, "MonitorBack", new Vector3(0f, 0.18f, 0.075f), new Vector3(0.66f, 0.4f, 0.03f), dark);
                back.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                Chair(room, new Vector3(x, 0f, 0.15f), side * -8f, metal, dark);
                var officer = CrewOfficer.Build(room, new Vector3(x, 0.5f, 0.2f), side < 0 ? accent : CicArtKit.Cyan);
                officer.localRotation = Quaternion.Euler(0f, side * -8f, 0f);
            }

            // Server racks along the back wall, LED faces, and the wall monitor over the exit.
            var leds = art.Lit(Leds(), new Color(0.55f, 0.55f, 0.55f, 1f), 1.1f);
            for (var i = 0; i < 4; i++)
            {
                var x = -5.4f + i * 1.0f;
                Box(room, "Rack" + i, new Vector3(x, 1.1f, -3.1f), new Vector3(0.8f, 2.2f, 0.6f), dark);
                var face = Quad(room, "RackFace" + i, new Vector3(x, 1.1f, -2.795f), new Vector3(0.66f, 1.9f, 1f), leds);
                face.transform.localRotation = Quaternion.Euler(0f, 180f, i * 180f);
            }

            Box(room, "MonitorBezel", new Vector3(2.2f, 3.35f, -3.33f), new Vector3(3.4f, 1.2f, 0.12f), chassis);
            Box(room, "MonitorGlass", new Vector3(2.2f, 3.35f, -3.26f), new Vector3(3.2f, 1.02f, 0.02f),
                art.Lit(Texture2D.whiteTexture, new Color(0.02f, 0.05f, 0.09f), 0.4f));
            Box(room, "MonitorGlow", new Vector3(2.2f, 2.73f, -3.27f), new Vector3(3.2f, 0.02f, 0.02f), violet);
            refs.WallMonitor = UiKit.Label(room, "WallMonitor", string.Empty, new Vector3(2.2f, 3.35f, -3.24f), 3.0f, 0.9f,
                new Color(0.8f, 0.72f, 1f));
            refs.WallMonitor.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            refs.WallMonitor.richText = true;
            refs.WallMonitor.fontSize = 1.5f;
            refs.WallMonitor.enableAutoSizing = true;
            refs.WallMonitor.fontSizeMin = 0.8f;
            refs.WallMonitor.fontSizeMax = 1.5f;
            refs.WallMonitor.enableWordWrapping = true;

            // Hazard edge along the gallery rail; ceiling trays and light bars over the gallery.
            Box(room, "HazardEdge", new Vector3(0f, 0.004f, galleryEdge - 0.25f), new Vector3(hallWidth - 0.6f, 0.01f, 0.3f), Hazard(40f));
            for (var i = -1; i <= 1; i++)
            {
                Box(room, "Tray" + i, new Vector3(i * 2.6f, hallFloor + hallHeight - 0.35f, -0.8f), new Vector3(0.4f, 0.08f, 4.4f), metal);
                Box(room, "LightBar" + i, new Vector3(i * 2.6f, hallFloor + hallHeight - 0.4f, -0.8f), new Vector3(0.18f, 0.02f, 4f), cyan);
            }

            // ── Embarkation hall ──────────────────────────────────────────────────
            var doorZ = 3.6f;
            Box(room, "BlastFrame", new Vector3(-half + 0.2f, hallFloor + 1.5f, doorZ), new Vector3(0.3f, 3f, 2.8f), chassis);
            Box(room, "BlastLeafTop", new Vector3(-half + 0.34f, hallFloor + 2.35f, doorZ), new Vector3(0.1f, 0.7f, 2.2f), metal);
            Box(room, "BlastHazard", new Vector3(-half + 0.4f, hallFloor + 1.95f, doorZ), new Vector3(0.02f, 0.14f, 2.2f), Hazard(16f));
            Box(room, "BlastLamp", new Vector3(-half + 0.4f, hallFloor + 3.15f, doorZ), new Vector3(0.1f, 0.12f, 0.3f), amber);

            // Cargo along the right wall: crate stacks and a pallet, plus a floor loader track.
            var crateMats = new[] { chassis, UiKit.Uniform };
            var rnd = new System.Random(71);
            for (var i = 0; i < 5; i++)
            {
                var z = 3.2f + i * 1.4f;
                var h = 1 + rnd.Next(3);
                for (var k = 0; k < h; k++)
                {
                    var s = new Vector3(1f, 0.7f, 1.1f) * (0.92f + (float)rnd.NextDouble() * 0.12f);
                    Rounded(room, "Crate", s, 0.06f,
                        new Vector3(half - 1.0f + (float)rnd.NextDouble() * 0.2f, hallFloor + 0.35f + k * 0.72f, z),
                        crateMats[(i + k) % 2], i % 2 == 0 ? CicArtKit.Amber : accent, 0.35f);
                }
            }

            Box(room, "LoaderTrack", new Vector3(half - 2.1f, hallFloor + 0.005f, 6f), new Vector3(0.1f, 0.01f, 8f), amber);

            // Gate floodlight fixtures high on the side walls, lens aimed at the ring (emissive: the Quest
            // URP profile has additional lights off, the ring carries its own glow).
            var lens = art.Lit(Texture2D.whiteTexture, new Color(0.85f, 0.9f, 1f), 2.8f);
            for (var side = -1; side <= 1; side += 2)
            {
                var fix = new Vector3(side * (half - 0.4f), hallFloor + hallHeight - 0.8f, gatePos.z - 3.2f);
                var aim = Quaternion.LookRotation(gatePos - fix);
                var housing = Box(room, "SpotHousing", fix, new Vector3(0.4f, 0.4f, 0.55f), chassis);
                housing.transform.localRotation = aim;
                var glass = Box(room, "SpotLens", fix + aim * new Vector3(0f, 0f, 0.28f), new Vector3(0.3f, 0.3f, 0.02f), lens);
                glass.transform.localRotation = aim;
            }

            // Wall turrets flanking the gate, on brackets, barrels toward the horizon.
            for (var side = -1; side <= 1; side += 2)
            {
                var mount = new Vector3(side * (half - 0.6f), hallFloor + 3.2f, gatePos.z - 0.8f);
                Box(room, "TurretBracket", mount + new Vector3(side * 0.3f, 0f, 0f), new Vector3(0.6f, 0.2f, 0.5f), metal);
                var turret = DefensePlatformKit.Create(room, "WallTurret", false);
                turret.transform.localPosition = mount;
                turret.transform.localScale = Vector3.one * 0.55f;
                // Deck against the wall (up = into the hall), barrels down onto the splash zone before the horizon.
                turret.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -2.2f, -1.7f).normalized,
                    new Vector3(-side, 0f, 0f));
            }

            // Floor markings: chevrons at the causeway foot, the splash zone in front of the horizon.
            var chevron = Hazard(10f);
            for (var i = 0; i < 3; i++)
                Box(room, "Chevron" + i, new Vector3(0f, hallFloor + 0.25f, 3.4f + i * 0.35f), new Vector3(2.2f, 0.005f, 0.12f), chevron);

            var zone = Quad(room, "SplashZone", new Vector3(0f, hallFloor + 0.31f, gatePos.z - 1.5f), new Vector3(5f, 2.4f, 1f),
                art.RadarIcon(art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture, new Color(1f, 0.55f, 0.2f, 0.28f)));
            zone.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return refs;
        }

        // ── Structure: up and down ────────────────────────────────────────────────

        /// <summary>
        /// What the hall is built of, over the head and underfoot, merged per material (<see cref="MeshBatch"/>):
        /// roof trusses on the wall ribs with lit bottom chords, purlins, cable runs sagging between trusses, air
        /// ducts along the walls, a crane gantry over the causeway, pendant lamps, an energy halo over the gate;
        /// the gate's power — conduits from wall transformers into the dais (lit with the ring's own energy
        /// material, so they surge as it charges), coolant tanks, wall-base pipes, a floor light grid, a probe cart,
        /// vents under the gallery. Alarm beacons on the walls; returns the alarm horns (positions for the horn).
        /// </summary>
        public static Transform[] BuildStructure(Transform room, CicArtKit art, float hallFloor, float hallWidth, float hallHeight,
            float galleryEdge, float hallEnd, Vector3 gatePos, Material energy)
        {
            var metal = art.MetalPanel(0.4f);
            var dark = art.DarkPanel(0.3f);
            var cyan = art.CyanEmit(1.8f);
            var dim = art.CyanEmit(0.9f);
            var amber = art.AmberEmit(2f);
            var lamp = art.Lit(Texture2D.whiteTexture, new Color(0.85f, 0.92f, 1f), 3f);
            var cable = art.Lit(Texture2D.whiteTexture, new Color(0.05f, 0.06f, 0.07f), 0.15f);
            var f = hallFloor;
            var top = hallFloor + hallHeight;
            var half = hallWidth * 0.5f;
            const float back = -3.45f;
            var b = new MeshBatch();

            // Roof: a truss on each wall rib, top and bottom chords, posts and alternating diagonals.
            var trussZ = new List<float>();
            for (var k = 1; k < 7; k++)
                trussZ.Add(-2.5f + k * 2.8f);
            foreach (var z in trussZ)
            {
                var y0 = top - 0.9f;
                var y1 = top - 0.12f;
                b.Box(new Vector3(0f, y1, z), new Vector3(hallWidth - 0.5f, 0.2f, 0.24f), metal);
                b.Box(new Vector3(0f, y0, z), new Vector3(hallWidth - 0.8f, 0.14f, 0.2f), metal);
                b.Box(new Vector3(0f, y0 - 0.08f, z), new Vector3(hallWidth - 1.2f, 0.02f, 0.05f), cyan);
                for (var i = -4; i <= 4; i++)
                {
                    var x = i * 1.4f;
                    b.Box(new Vector3(x, (y0 + y1) * 0.5f, z), new Vector3(0.08f, y1 - y0, 0.08f), metal);
                    if (i < 4)
                        b.Strut(new Vector3(x, i % 2 == 0 ? y0 : y1, z), new Vector3(x + 1.4f, i % 2 == 0 ? y1 : y0, z), 0.06f, metal);
                }
            }

            // Purlins along the hall, and cables sagging between the trusses.
            foreach (var x in new[] { -4.4f, -2.2f, 2.2f, 4.4f })
                b.Box(new Vector3(x, top - 0.28f, (back + hallEnd) * 0.5f), new Vector3(0.12f, 0.14f, hallEnd - back), metal);
            for (var k = 0; k < trussZ.Count - 1; k++)
                foreach (var x in new[] { -3.3f, -1.15f, 1.15f, 3.3f })
                    b.Cable(new Vector3(x, top - 0.98f, trussZ[k]), new Vector3(x + 0.1f, top - 0.98f, trussZ[k + 1]), 0.32f, 0.03f, cable);

            // Air ducts high along both walls, collared at each rib, hung from the roof.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * (half - 0.55f);
                b.Tube(new Vector3(x, top - 0.6f, (back + hallEnd) * 0.5f), Vector3.forward, 0.28f, hallEnd - back, dark);
                for (var k = 0; k < 7; k++)
                {
                    var z = -2.5f + k * 2.8f;
                    b.Tube(new Vector3(x, top - 0.6f, z), Vector3.forward, 0.32f, 0.12f, metal);
                    b.Box(new Vector3(x, top - 0.22f, z), new Vector3(0.05f, 0.5f, 0.05f), metal);
                }
            }

            // Crane gantry over the causeway: twin rails, a trolley, the hook on its cable, parked by the gallery (clear of the ring from the gallery).
            for (var side = -1; side <= 1; side += 2)
                b.Box(new Vector3(side * 0.5f, top - 1.15f, 6f), new Vector3(0.14f, 0.22f, 9f), metal);
            b.Box(new Vector3(0f, top - 1.3f, 2.3f), new Vector3(1.3f, 0.26f, 0.7f), dark);
            b.Box(new Vector3(0f, top - 1.44f, 2.3f), new Vector3(1.1f, 0.02f, 0.5f), amber);
            b.Pipe(new Vector3(0f, top - 1.45f, 2.3f), new Vector3(0f, f + 4.55f, 2.3f), 0.02f, cable);
            b.Box(new Vector3(0f, f + 4.4f, 2.3f), new Vector3(0.3f, 0.32f, 0.2f), metal);
            b.Box(new Vector3(0f, f + 4.22f, 2.3f), new Vector3(0.32f, 0.04f, 0.22f), Hazard(art, 2f));

            // Pendant lamps over the hall floor.
            foreach (var z in new[] { 3.2f, 7.4f, 12.8f })
            foreach (var x in new[] { -2.2f, 2.2f })
            {
                var y = f + 4.1f;
                b.Pipe(new Vector3(x, top - 0.3f, z), new Vector3(x, y + 0.12f, z), 0.015f, cable);
                b.Tube(new Vector3(x, y + 0.06f, z), Vector3.up, 0.24f, 0.14f, dark);
                b.Tube(new Vector3(x, y - 0.02f, z), Vector3.up, 0.2f, 0.02f, lamp);
            }

            // Energy halo in the roof over the gate.
            var halo = new LatheMesh(new Vector3(0f, 0f, gatePos.z)) { Step = 7.5f };
            halo.Revolve(new[]
            {
                new Vector2(2.3f, top - 0.05f), new Vector2(2.3f, top - 0.32f), new Vector2(1.6f, top - 0.32f), new Vector2(1.6f, top - 0.05f)
            }, 0f, 360f, false);
            b.Add(halo.ToMesh("SU_GateHalo"), Matrix4x4.identity, dark);
            var haloLit = new LatheMesh(new Vector3(0f, 0f, gatePos.z)) { Step = 7.5f };
            haloLit.Revolve(new[] { new Vector2(1.95f, top - 0.335f), new Vector2(1.7f, top - 0.335f) }, 0f, 360f, false);
            b.Add(haloLit.ToMesh("SU_GateHaloLit"), Matrix4x4.identity, energy);

            // ── Underfoot ─────────────────────────────────────────────────────────
            // Power: a transformer cabinet on each wall, a conduit along the floor into the dais, two more to the back wall.
            for (var side = -1; side <= 1; side += 2)
            {
                var wx = side * (half - 0.4f);
                b.Box(new Vector3(wx, f + 1.0f, gatePos.z + 0.8f), new Vector3(0.6f, 2f, 1.5f), dark);
                b.Box(new Vector3(wx - side * 0.31f, f + 1.15f, gatePos.z + 0.8f), new Vector3(0.02f, 1.3f, 0.26f), energy);
                for (var v = 0; v < 5; v++)
                    b.Box(new Vector3(wx - side * 0.31f, f + 0.35f + v * 0.34f, gatePos.z + 0.3f), new Vector3(0.02f, 0.05f, 0.4f), metal);
                b.Box(new Vector3(wx - side * 0.31f, f + 1.95f, gatePos.z + 0.8f), new Vector3(0.03f, 0.06f, 1.3f), Hazard(art, 6f));
                var inner = 4.05f;
                var outer = half - 0.7f;
                var cx = side * (inner + outer) * 0.5f;
                var len = outer - inner;
                b.Box(new Vector3(cx, f + 0.1f, gatePos.z + 0.8f), new Vector3(len, 0.2f, 0.56f), metal);
                b.Box(new Vector3(cx, f + 0.205f, gatePos.z + 0.8f), new Vector3(len, 0.012f, 0.14f), energy);
                foreach (var e in new[] { -1f, 1f })
                    b.Box(new Vector3(cx, f + 0.21f, gatePos.z + 0.8f + e * 0.25f), new Vector3(len, 0.02f, 0.03f), Hazard(art, len * 4f));
                b.Box(new Vector3(side * 1.6f, f + 0.08f, (gatePos.z + 1.2f + hallEnd) * 0.5f), new Vector3(0.4f, 0.16f, hallEnd - gatePos.z - 1.2f), metal);
                b.Box(new Vector3(side * 1.6f, f + 0.165f, (gatePos.z + 1.2f + hallEnd) * 0.5f),
                    new Vector3(0.1f, 0.012f, hallEnd - gatePos.z - 1.2f), energy);
            }

            // Coolant tanks in the far port corner, banded, piped into the wall.
            foreach (var z in new[] { 12.95f, 13.95f })
            {
                var x = -half + 0.85f;
                b.Tube(new Vector3(x, f + 1.4f, z), Vector3.up, 0.42f, 2.6f, metal);
                b.Tube(new Vector3(x, f + 2.75f, z), Vector3.up, 0.32f, 0.12f, dark);
                foreach (var y in new[] { 0.5f, 1.5f, 2.4f })
                    b.Tube(new Vector3(x, f + y, z), Vector3.up, 0.44f, 0.06f, y > 1f && y < 2f ? cyan : dark);
                b.Pipe(new Vector3(x, f + 2.2f, z), new Vector3(-half + 0.1f, f + 2.2f, z), 0.07f, metal);
            }

            b.Box(new Vector3(-half + 0.85f, f + 0.04f, 13.45f), new Vector3(1.2f, 0.08f, 2.3f), Hazard(art, 6f));

            // Pipes along the foot of both walls, on brackets.
            for (var side = -1; side <= 1; side += 2)
            {
                var x = side * (half - 0.2f);
                b.Tube(new Vector3(x, f + 0.28f, (galleryEdge + hallEnd) * 0.5f), Vector3.forward, 0.07f, hallEnd - galleryEdge, metal);
                b.Tube(new Vector3(x - side * 0.04f, f + 0.48f, (galleryEdge + hallEnd) * 0.5f), Vector3.forward, 0.05f, hallEnd - galleryEdge,
                    dark);
                for (var z = galleryEdge + 0.7f; z < hallEnd; z += 1.4f)
                    b.Box(new Vector3(x + side * 0.05f, f + 0.38f, z), new Vector3(0.12f, 0.36f, 0.05f), dark);
            }

            // Floor light grid: lines across the hall at each rib, outside the causeway.
            for (var k = 2; k < 7; k++)
            {
                var z = -2.5f + k * 2.8f;
                if (Mathf.Abs(z - gatePos.z) < 1.4f)
                    continue;
                foreach (var side in new[] { -1f, 1f })
                    b.Box(new Vector3(side * 3.6f, f + 0.006f, z), new Vector3(4.2f, 0.012f, 0.05f), dim);
            }

            // A probe cart parked in the far starboard corner: six wheels, a sensor mast and its lamp.
            {
                var c = new Vector3(half - 2.2f, f, 13.4f);
                b.Box(c + new Vector3(0f, 0.55f, 0f), new Vector3(1.0f, 0.42f, 1.5f), UiKit.Uniform);
                b.Box(c + new Vector3(0f, 0.78f, 0f), new Vector3(0.9f, 0.04f, 1.4f), Hazard(art, 4f));
                for (var w = -1; w <= 1; w++)
                foreach (var s in new[] { -1f, 1f })
                    b.Tube(c + new Vector3(s * 0.56f, 0.22f, w * 0.55f), Vector3.right, 0.2f, 0.14f, dark);
                b.Box(c + new Vector3(0f, 1.25f, -0.5f), new Vector3(0.08f, 0.9f, 0.08f), metal);
                b.Box(c + new Vector3(0f, 1.72f, -0.55f), new Vector3(0.36f, 0.2f, 0.24f), dark);
                b.Box(c + new Vector3(0f, 1.72f, -0.68f), new Vector3(0.12f, 0.12f, 0.02f), lamp);
                b.Box(c + new Vector3(0.14f, 1.86f, -0.55f), new Vector3(0.05f, 0.05f, 0.05f), amber);
            }

            // Under the gallery: the face toward the hall, vent grilles and service panels.
            for (var i = -2; i <= 2; i++)
            {
                var x = i * 2.4f;
                b.Box(new Vector3(x, f + 0.75f, galleryEdge + 0.11f), new Vector3(1.6f, 0.9f, 0.02f), dark);
                for (var s = 0; s < 5; s++)
                    b.Box(new Vector3(x, f + 0.45f + s * 0.15f, galleryEdge + 0.125f), new Vector3(1.4f, 0.03f, 0.01f), s == 2 ? dim : metal);
            }

            b.Build(room, "HallStructure");

            // Alarm beacons: two on the hall walls looking over the gate, two on the gallery's back wall.
            AlertBeacon.MountOnWall(room, "Beacon", new Vector3(-half + 0.1f, f + 4.4f, 6.6f), Vector3.right, art);
            AlertBeacon.MountOnWall(room, "Beacon", new Vector3(half - 0.1f, f + 4.4f, 6.6f), Vector3.left, art, 180f);
            AlertBeacon.MountOnWall(room, "Beacon", new Vector3(-4.6f, 4.2f, back + 0.06f), Vector3.forward, art, 90f);
            AlertBeacon.MountOnWall(room, "Beacon", new Vector3(4.6f, 4.2f, back + 0.06f), Vector3.forward, art, 270f);

            // Alarm horns high on the hall walls either side of the gate: a flared bell on a bracket.
            var horns = new Transform[2];
            for (var side = -1; side <= 1; side += 2)
            {
                var hb = new MeshBatch();
                var root = new GameObject("AlarmHorn").transform;
                root.SetParent(room, false);
                root.localPosition = new Vector3(side * (half - 0.12f), f + 4.6f, gatePos.z - 2.3f);
                root.localRotation = Quaternion.LookRotation(new Vector3(-side, -0.35f, -0.4f).normalized, Vector3.up);
                hb.Box(new Vector3(0f, 0f, -0.05f), new Vector3(0.22f, 0.22f, 0.1f), dark);
                hb.Tube(new Vector3(0f, 0f, 0.12f), Vector3.forward, 0.08f, 0.24f, metal);
                hb.Tube(new Vector3(0f, 0f, 0.27f), Vector3.forward, 0.16f, 0.06f, metal);
                hb.Tube(new Vector3(0f, 0f, 0.3f), Vector3.forward, 0.13f, 0.01f, dark);
                hb.Build(root, "Horn");
                horns[(side + 1) / 2] = root;
            }

            return horns;
        }

        static Material Hazard(CicArtKit art, float tiles)
        {
            var key = Mathf.Max(1, Mathf.RoundToInt(tiles));
            if (HazardMats.TryGetValue(key, out var m) && m != null)
                return m;
            m = new Material(art.Lit(HazardTex(), Color.white, 0.55f, 1f)) { name = "SU_Hazard" + key, mainTextureScale = new Vector2(key, 1f) };
            HazardMats[key] = m;
            return m;
        }

        static readonly Dictionary<int, Material> HazardMats = new();

        // ── Pieces ────────────────────────────────────────────────────────────────

        /// <summary>Yaw that puts a desk's operator side (local −z) toward the stand at the room origin.</summary>
        internal static float FaceStand(float x, float z) => Mathf.Atan2(x, z) * Mathf.Rad2Deg;

        /// <summary>
        /// Sloped operator desk (pedestal, kick plate, angled top with lit edge, key deck, side cheeks) and an arm
        /// rising behind it; returns the screen mount at the arm's head (the caller parents its screen there).
        /// </summary>
        internal static Transform Desk(Transform room, string name, Vector3 pos, float yaw, float width, Material metal, Material dark,
            Material cyan, Material amber)
        {
            var root = new GameObject(name).transform;
            root.SetParent(room, false);
            root.localPosition = pos;
            // Operator side is local −z: facing the gallery's back, toward whoever stands at the desk.
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(root, "Pedestal", new Vector3(0f, 0.42f, 0.05f), new Vector3(width * 0.8f, 0.84f, 0.5f), dark);
            Box(root, "Kick", new Vector3(0f, 0.06f, -0.2f), new Vector3(width * 0.82f, 0.12f, 0.04f), cyan);
            var top = Box(root, "Top", new Vector3(0f, 0.9f, -0.05f), new Vector3(width, 0.05f, 0.62f), metal);
            top.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            var lip = Box(root, "Lip", new Vector3(0f, 0.855f, -0.36f), new Vector3(width, 0.02f, 0.02f), cyan);
            lip.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            // Key deck: rows of backlit keys (one strip per row, not one object per key).
            for (var r = 0; r < 3; r++)
            {
                var k = Box(root, "Keys" + r, new Vector3(0f, 0.915f + r * 0.012f, -0.28f + r * 0.06f),
                    new Vector3(width * 0.55f, 0.012f, 0.035f), r == 1 ? amber : cyan);
                k.transform.localRotation = Quaternion.Euler(-14f, 0f, 0f);
            }

            for (var side = -1; side <= 1; side += 2)
                Box(root, "Cheek", new Vector3(side * width * 0.5f, 0.55f, 0f), new Vector3(0.05f, 1.05f, 0.66f), metal);
            // Short arm: the screen stands just above the desk's back edge, below a standing eye line.
            Box(root, "Arm", new Vector3(0f, 0.93f, 0.26f), new Vector3(0.08f, 0.14f, 0.08f), metal);
            Box(root, "ArmHead", new Vector3(0f, 1.0f, 0.26f), new Vector3(0.22f, 0.04f, 0.1f), metal);
            // Mount = top of the arm, oriented like the desk; a screen parented here sits on it.
            var mount = new GameObject("ScreenMount").transform;
            mount.SetParent(root, false);
            mount.localPosition = new Vector3(0f, 1.02f, 0.26f);
            return mount;
        }

        internal static void Chair(Transform room, Vector3 pos, float yaw, Material metal, Material dark)
        {
            var root = new GameObject("TechChair").transform;
            root.SetParent(room, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Box(root, "Post", new Vector3(0f, 0.22f, 0f), new Vector3(0.08f, 0.44f, 0.08f), metal);
            Box(root, "Seat", new Vector3(0f, 0.47f, 0f), new Vector3(0.48f, 0.07f, 0.46f), dark);
            Box(root, "Back", new Vector3(0f, 0.8f, -0.22f), new Vector3(0.46f, 0.62f, 0.06f), dark);
            Box(root, "Base", new Vector3(0f, 0.02f, 0f), new Vector3(0.5f, 0.04f, 0.5f), metal);
        }

        internal static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        internal static GameObject Quad(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        internal static GameObject Rounded(Transform parent, string name, Vector3 size, float radius, Vector3 pos, Material mat,
            Color accent, float mul)
        {
            var go = UiKit.MeshPiece(parent, name, UiMeshes.RoundedBox(size, radius), mat, pos);
            var block = new MaterialPropertyBlock();
            block.SetColor(UiKit.AccentId, accent);
            block.SetFloat(UiKit.AccentMulId, mul);
            var r = go.GetComponent<MeshRenderer>();
            r.SetPropertyBlock(block);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        const float Recline = 20f;

        /// <summary>
        /// Parent a screen to a desk arm (<see cref="Desk"/>): same heading as the desk, top leaning back
        /// <see cref="Recline"/>°, bottom edge resting on the arm head (screen pivot is its centre).
        /// </summary>
        internal static void SeatOnArm(Transform screen, Transform mount, float height, float recline = Recline)
        {
            screen.SetParent(mount, false);
            var half = height * 0.5f;
            var r = recline * Mathf.Deg2Rad;
            screen.localRotation = Quaternion.Euler(recline, 0f, 0f);
            screen.localPosition = new Vector3(0f, Mathf.Cos(r) * half + 0.01f, Mathf.Sin(r) * half);
        }

        /// <summary>Light a pedestal lamp as its gate lock seats (off / amber / alarm red).</summary>
        public static void SetLamp(Renderer lamp, Color color, float mul)
        {
            if (lamp == null)
                return;
            var block = new MaterialPropertyBlock();
            lamp.GetPropertyBlock(block);
            block.SetColor(UiKit.AccentId, color);
            block.SetFloat(UiKit.AccentMulId, mul);
            lamp.SetPropertyBlock(block);
        }

        // ── Procedural textures ───────────────────────────────────────────────────

        /// <summary>Amber / black diagonal hazard stripes (tiles along U).</summary>
        static Texture2D HazardTex()
        {
            if (_hazard != null)
                return _hazard;
            const int n = 64;
            _hazard = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SU_Hazard", wrapMode = TextureWrapMode.Repeat };
            var a = new Color(1f, 0.62f, 0.12f, 1f);
            var b = new Color(0.06f, 0.06f, 0.07f, 1f);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
                _hazard.SetPixel(x, y, ((x + y) / (n / 2)) % 2 == 0 ? a : b);
            _hazard.Apply(true, true);
            return _hazard;
        }

        /// <summary>Rack face: rows of status LEDs (green / amber / cyan, a few off) on dark metal.</summary>
        static Texture2D Leds()
        {
            if (_leds != null)
                return _leds;
            const int w = 32, h = 96;
            _leds = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SU_RackLeds", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var rnd = new System.Random(5);
            var palette = new[] { new Color(0.3f, 1f, 0.5f), new Color(1f, 0.65f, 0.2f), new Color(0.35f, 0.9f, 1f), new Color(0.05f, 0.07f, 0.08f) };
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var c = new Color(0.07f, 0.08f, 0.1f);
                if (y % 6 == 0)
                    c = new Color(0.14f, 0.16f, 0.19f);
                else if (x % 4 == 1 && y % 6 == 3)
                    c = palette[rnd.Next(palette.Length)];
                _leds.SetPixel(x, y, c);
            }

            _leds.Apply(true, true);
            return _leds;
        }
    }
}
