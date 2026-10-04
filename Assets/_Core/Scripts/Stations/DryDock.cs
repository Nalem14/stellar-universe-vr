using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Stations
{
    /// <summary>
    /// Engineering dry dock of an orbital station — a room you walk into through a door of the bridge (only
    /// at a station over one of our worlds; web ShipBuilderUI + planet shipyard, rebuilt as a place). A control room overlooks a hangar bay through a wide window: the
    /// selected ship sits in its cradle at 1:1 (the real procedural hull, <see cref="ShipHullBuilder"/>) and is
    /// rebuilt, with welding sparks, each time a module goes on. One path per job, each at its own piece of
    /// furniture (no floating screen):
    /// - build: the printer's console on the starboard wall (<see cref="ShipyardPanel"/>: AddShip, queue,
    ///   cancel, Nova finish); the printer beside it (<see cref="ModulePrinter"/>) prints the part, its
    ///   shuttle carries it to the store;
    /// - store: the module store forward of it (<see cref="ModuleShelves"/>) holds the finished hangar, one
    ///   block per type — the only place a module is taken from;
    /// - fit: set a store block on a free cell of the assembly table's 9×9 grid touching the structure →
    ///   PlaceShipModule; point twice at a placed module to take it off (RemoveShipModule);
    /// - scrap: drop a store block in the recycler between the printer and its console → DelShip;
    /// - ship: the console left of the table (docked ships, new ship from a ShipCore = AddToFleet fleet=0,
    ///   rename, design readout); blueprints at the console right of it (<see cref="BlueprintPanel"/>).
    /// The dock lives far below the bridge; entering moves the XR origin here, leaving puts it back on deck.
    /// </summary>
    public sealed class DryDock : MonoBehaviour
    {
        static readonly Vector3 WorldOrigin = new(0f, -3000f, 0f);
        static readonly Vector3 Stand = new(0f, 0f, -2.3f);
        /// <summary>Assembly table centre (control room); grid y runs toward the window / the ship's bow.</summary>
        static readonly Vector3 Table = new(0f, 0f, -0.35f);
        /// <summary>Ship cradle in the bay, seen through the window (hull at 1:1).</summary>
        static readonly Vector3 Cradle = new(0f, 1.1f, 13.5f);
        /// <summary>The ship lies broadside to the window, bow to the right; the table grid turns the same way.</summary>
        static readonly Quaternion Broadside = Quaternion.Euler(0f, 90f, 0f);
        /// <summary>Control room extents: side walls at ±RoomWidth/2, bay window at RoomNorth, exit door in RoomSouth.</summary>
        const float RoomWidth = 11f;
        const float RoomNorth = 3.5f;
        const float RoomSouth = -7.6f;
        const float RoomHeight = 3.4f;
        const float Cell = 0.2f;
        const float GridHeight = 0.92f;
        /// <summary>Starboard wall, fore to aft: module store, printer console, recycler, printer.</summary>
        const float StoreZ = -2.0f;
        const float YardDeskZ = -4.15f;
        const float RecyclerZ = -5.02f;
        const float PrinterZ = -6.15f;
        static readonly Vector3 ShipDesk = new(-2.6f, 0f, -1.3f);
        /// <summary>Forward of the starboard diagonal: from the stand it sits between the ship's bow in the window
        /// and the module store, hiding neither.</summary>
        static readonly Vector3 PlansDesk = new(2.9f, 0f, 0f);
        static readonly Vector2 ScreenSize = new(0.95f, 0.8f);
        static readonly Color Accent = new(0.4f, 0.95f, 0.55f, 1f);

        public static DryDock Instance { get; private set; }
        public static bool Inside { get; private set; }

        CicArtKit _art;
        FocusContext _focus;
        FleetPoller _poller;
        EconomyService _eco;
        Transform _gridRoot;
        Transform _hullBay;
        Transform _hullBuilt;
        ParticleSystem _sparks;
        readonly Renderer[,] _cells = new Renderer[ModuleCatalog.Grid, ModuleCatalog.Grid];
        /// <summary>The fitted module standing on each cell in miniature (the store's block, SU/ModuleBlock).</summary>
        readonly MeshRenderer[,] _minis = new MeshRenderer[ModuleCatalog.Grid, ModuleCatalog.Grid];
        readonly MeshFilter[,] _miniFilters = new MeshFilter[ModuleCatalog.Grid, ModuleCatalog.Grid];
        readonly BoxCollider[,] _miniCols = new BoxCollider[ModuleCatalog.Grid, ModuleCatalog.Grid];
        /// <summary>The module in hand, as a hologram on the cell it would land on (green: it can, red: it cannot).</summary>
        MeshRenderer _ghost;
        MeshFilter _ghostFilter;
        TextMeshPro _guideStep;
        TextMeshPro _guideInfo;
        TextMeshPro _guideHull;
        MaterialPropertyBlock _mpb;
        static readonly int BlockAccentId = Shader.PropertyToID("_Accent");
        static readonly int BlockHoverId = Shader.PropertyToID("_Hover");
        static readonly int BlockGhostId = Shader.PropertyToID("_Ghost");
        static readonly int BlockRevealId = Shader.PropertyToID("_Reveal");
        const float MiniScale = 0.8f;
        /// <summary>The table's miniatures: the store's block shader, under the key light and in family colours.</summary>
        Material _miniMat;

        HoloScreen _shipScreen;
        RectTransform _shipBody;
        HoloScreen _yardScreen;
        RectTransform _yardBody;
        ShipyardPanel _yard;
        TMP_Text _yardStatus;
        RectTransform _bpBody;
        BlueprintPanel _blueprints;
        Blueprint _preview;
        bool[] _previewOk;
        Material _blueprintMat;
        float _nextTick;
        TMP_Text _status;
        TMP_InputField _nameField;
        ModuleShelves _shelves;
        ModuleRecycler _recycler;

        int _planetId;
        int _fleetId;
        string _selectedType;
        (int x, int y)? _armedRemove;
        float _armedUntil;
        (int x, int y)? _hover;
        (int x, int y)? _pendingWeld;
        bool _busy;
        readonly List<FocusShipModule> _layout = new();

        public static DryDock Build(CicArtKit art, FocusContext focus, FleetPoller poller, EconomyService eco)
        {
            var go = new GameObject("EngineeringDryDock");
            go.transform.position = WorldOrigin;
            var dock = go.AddComponent<DryDock>();
            dock._art = art;
            dock._focus = focus;
            dock._poller = poller;
            dock._eco = eco;
            dock._mpb = new MaterialPropertyBlock();
            dock.BuildRoom();
            dock.BuildGrid();
            dock.BuildScreens();
            dock.BuildShelves();
            dock.BuildFabrication();
            go.SetActive(false);
            return dock;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Inside = false;
            }
        }

        // ── Room ──────────────────────────────────────────────────────────────────

        /// <summary>Walkable / solid pieces keep their collider (XR locomotion has gravity).</summary>
        static readonly HashSet<string> Solid = new()
        {
            "Floor", "WallS", "WallE", "WallW", "Sill", "BayWindow", "Locker0", "Locker1", "Bench"
        };

        GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!Solid.Contains(name))
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void BuildRoom()
        {
            var deck = _art.DeckMat(0.55f);
            var wall = _art.MetalPanel(0.5f);
            var dark = _art.DarkPanel(0.35f);
            var cyan = _art.CyanEmit(2.4f);
            var amber = _art.AmberEmit(2f);

            // Control room: 11 × 11.1 m, north side open on the bay through a wide window. The aft half is a
            // work bay (lockers, parts rack, bench) so the exit door sits well behind the stand: ≥ 3 m of floor
            // between the assembly table and the doorway.
            const float w = RoomWidth, h = RoomHeight;
            const float d = RoomNorth - RoomSouth, cz = (RoomNorth + RoomSouth) * 0.5f;
            Box("Floor", new Vector3(0f, -0.05f, cz), new Vector3(w, 0.1f, d), deck);
            Box("Ceiling", new Vector3(0f, h, cz), new Vector3(w, 0.1f, d), dark);
            Box("WallS", new Vector3(0f, h * 0.5f, RoomSouth), new Vector3(w, h, 0.12f), wall);
            Box("WallE", new Vector3(w * 0.5f, h * 0.5f, cz), new Vector3(0.12f, h, d), wall);
            Box("WallW", new Vector3(-w * 0.5f, h * 0.5f, cz), new Vector3(0.12f, h, d), wall);
            Box("Sill", new Vector3(0f, 0.45f, RoomNorth), new Vector3(w, 0.9f, 0.3f), wall);
            Box("SillLight", new Vector3(0f, 0.905f, RoomNorth - 0.16f), new Vector3(w * 0.96f, 0.02f, 0.03f), cyan);
            Box("Header", new Vector3(0f, h - 0.15f, RoomNorth), new Vector3(w, 0.3f, 0.3f), dark);
            for (var i = -2; i <= 2; i++)
                Box("Mullion" + i, new Vector3(i * 2.2f, (0.9f + h - 0.3f) * 0.5f, RoomNorth),
                    new Vector3(0.12f, h - 1.2f, 0.18f), dark);
            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glass.name = "BayWindow";
            Destroy(glass.GetComponent<Collider>());
            glass.transform.SetParent(transform, false);
            glass.transform.localPosition = new Vector3(0f, (0.9f + h - 0.3f) * 0.5f, RoomNorth + 0.02f);
            glass.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            glass.transform.localScale = new Vector3(w, h - 1.2f, 1f);
            glass.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture, new Color(0.6f, 0.9f, 1f, 0.015f));
            var pane = glass.AddComponent<BoxCollider>();
            pane.size = new Vector3(1f, 1f, 0.1f);

            for (var i = -1; i <= 1; i++)
                Box("RibLight" + i, new Vector3(i * 3.2f, h - 0.06f, cz), new Vector3(0.05f, 0.03f, d * 0.92f), cyan);
            BuildWorkBay(wall, dark, cyan, amber, w, h);

            // Assembly table (the "computer"): the grid sits on it, lit rim.
            var ped = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ped.name = "AssemblyTable";
            Destroy(ped.GetComponent<Collider>());
            ped.transform.SetParent(transform, false);
            ped.transform.localPosition = Table + new Vector3(0f, GridHeight * 0.5f, 0f);
            ped.transform.localScale = new Vector3(2.7f, GridHeight * 0.5f, 2.7f);
            ped.GetComponent<MeshRenderer>().sharedMaterial = dark;
            var rim = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rim.name = "TableRim";
            Destroy(rim.GetComponent<Collider>());
            rim.transform.SetParent(transform, false);
            rim.transform.localPosition = Table + new Vector3(0f, GridHeight + 0.006f, 0f);
            rim.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // OrbitRing art is an annulus near its edge: scaled so the ring sits on the table's rim.
            rim.transform.localScale = Vector3.one * (2.7f / 0.9f);
            rim.GetComponent<MeshRenderer>().sharedMaterial = _art.RadarIcon(
                _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture, new Color(Accent.r, Accent.g, Accent.b, 0.35f));
            var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glow.name = "ProjectorGlow";
            Destroy(glow.GetComponent<Collider>());
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = Table + new Vector3(0f, GridHeight + 0.012f, 0f);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localScale = Vector3.one * 2.6f;
            glow.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                _art.ProjectorGlow != null ? _art.ProjectorGlow : Texture2D.whiteTexture, new Color(0.3f, 1f, 0.6f, 0.12f));

            Light("KeyOverhead", new Vector3(0f, h - 0.5f, -0.5f), new Color(0.75f, 0.95f, 1f), 1.4f, 7f);
            Light("AftOverhead", new Vector3(0f, h - 0.5f, RoomSouth + 2.3f), new Color(0.95f, 0.92f, 0.85f), 1.1f, 6.5f);
            BuildBay(dark, cyan, amber);
            // The shelf blocks (SU/ModuleBlock) take the room's own lights, as the bridge shells do.
            RoomLightRig.Attach(transform, "KeyOverhead", "AftOverhead", "BayFill").Radius = 12f;
        }

        /// <summary>
        /// Aft half of the control room: wall pilasters, tool lockers (west), the module store, printer console,
        /// recycler and printer (east, built by <see cref="BuildShelves"/> / <see cref="BuildFabrication"/>), a
        /// bench and coolant tanks either side of the door, and a hazard apron marking the doorway.
        /// </summary>
        void BuildWorkBay(Material wall, Material dark, Material cyan, Material amber, float w, float h)
        {
            var xw = w * 0.5f;
            var cz = (RoomNorth + RoomSouth) * 0.5f;

            // Structural pilasters along both side walls (forward of the lockers), a thin cyan seam on each; the
            // module store stands where the aft starboard one would.
            for (var i = 0; i < 3; i++)
            {
                var z = -1.6f + i * 2.1f;
                foreach (var side in new[] { -1f, 1f })
                {
                    if (i == 0 && side > 0f)
                        continue;
                    Box("Pilaster" + i + side, new Vector3(side * (xw - 0.1f), h * 0.5f, z), new Vector3(0.16f, h, 0.34f), dark);
                    Box("PilasterSeam" + i + side, new Vector3(side * (xw - 0.185f), h * 0.5f, z),
                        new Vector3(0.012f, h * 0.7f, 0.04f), cyan);
                }
            }

            // Skirting light down both walls, the length of the room.
            Box("SkirtW", new Vector3(-xw + 0.07f, 0.08f, cz), new Vector3(0.02f, 0.03f, (RoomNorth - RoomSouth) * 0.96f), cyan);
            Box("SkirtE", new Vector3(xw - 0.07f, 0.08f, cz), new Vector3(0.02f, 0.03f, (RoomNorth - RoomSouth) * 0.96f), cyan);

            // West: two tool lockers, lit handles and status lamps.
            for (var i = 0; i < 2; i++)
            {
                var z = RoomSouth + 1.5f + i * 1.45f;
                Box("Locker" + i, new Vector3(-xw + 0.33f, 1.05f, z), new Vector3(0.5f, 2.1f, 1.3f), wall);
                Box("LockerSplit" + i, new Vector3(-xw + 0.585f, 1.05f, z), new Vector3(0.01f, 1.95f, 0.02f), dark);
                Box("LockerHandleA" + i, new Vector3(-xw + 0.6f, 1.1f, z - 0.12f), new Vector3(0.02f, 0.4f, 0.03f), cyan);
                Box("LockerHandleB" + i, new Vector3(-xw + 0.6f, 1.1f, z + 0.12f), new Vector3(0.02f, 0.4f, 0.03f), cyan);
                Box("LockerLamp" + i, new Vector3(-xw + 0.6f, 1.9f, z), new Vector3(0.02f, 0.05f, 0.5f), i == 0 ? amber : cyan);
            }

            // Aft wall, either side of the door: a work bench (west) and coolant tanks (east).
            Box("Bench", new Vector3(-2.6f, 0.45f, RoomSouth + 0.42f), new Vector3(2.2f, 0.9f, 0.7f), dark);
            Box("BenchTop", new Vector3(-2.6f, 0.915f, RoomSouth + 0.42f), new Vector3(2.26f, 0.03f, 0.76f), wall);
            Box("BenchEdge", new Vector3(-2.6f, 0.9f, RoomSouth + 0.805f), new Vector3(2.2f, 0.02f, 0.01f), cyan);
            Box("ToolBoard", new Vector3(-2.6f, 1.75f, RoomSouth + 0.08f), new Vector3(2.0f, 1.1f, 0.04f), wall);
            for (var i = 0; i < 5; i++)
                Box("Tool" + i, new Vector3(-3.4f + i * 0.4f, 1.72f + (i % 2) * 0.12f, RoomSouth + 0.12f),
                    new Vector3(0.05f, 0.5f - (i % 3) * 0.08f, 0.03f), dark);
            Box("ToolBoardLight", new Vector3(-2.6f, 2.33f, RoomSouth + 0.12f), new Vector3(1.9f, 0.02f, 0.03f), amber);
            for (var i = 0; i < 2; i++)
            {
                var tank = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tank.name = "CoolantTank" + i;
                Destroy(tank.GetComponent<Collider>());
                tank.transform.SetParent(transform, false);
                tank.transform.localPosition = new Vector3(2.3f + i * 0.75f, 0.9f, RoomSouth + 0.45f);
                tank.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
                var tr = tank.GetComponent<MeshRenderer>();
                tr.sharedMaterial = wall;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Box("TankBand" + i, new Vector3(2.3f + i * 0.75f, 1.35f, RoomSouth + 0.45f), new Vector3(0.57f, 0.05f, 0.57f), cyan);
                Box("TankValve" + i, new Vector3(2.3f + i * 0.75f, 0.55f, RoomSouth + 0.74f), new Vector3(0.1f, 0.1f, 0.06f), amber);
            }

            Box("TankPipe", new Vector3(2.675f, 2.2f, RoomSouth + 0.2f), new Vector3(1.2f, 0.08f, 0.08f), dark);

            // Hazard apron at the doorway: the one place on this floor that takes you out.
            Box("DoorApron", new Vector3(0f, 0.004f, RoomSouth + 0.7f), new Vector3(1.9f, 0.008f, 1.2f), dark);
            Box("ApronEdgeN", new Vector3(0f, 0.01f, RoomSouth + 1.3f), new Vector3(1.9f, 0.01f, 0.05f), amber);
            Box("ApronEdgeW", new Vector3(-0.95f, 0.01f, RoomSouth + 0.7f), new Vector3(0.05f, 0.01f, 1.2f), amber);
            Box("ApronEdgeE", new Vector3(0.95f, 0.01f, RoomSouth + 0.7f), new Vector3(0.05f, 0.01f, 1.2f), amber);
        }

        /// <summary>Hangar bay beyond the window: cradle, floodlights, the ship at 1:1.</summary>
        void BuildBay(Material dark, Material cyan, Material amber)
        {
            const float bw = 40f, bd = 34f, floor = -7f, top = 14f;
            var z0 = 3.6f;
            var cz = z0 + bd * 0.5f;
            Box("BayFloor", new Vector3(0f, floor, cz), new Vector3(bw, 0.2f, bd), _art.DeckMat(0.4f));
            Box("BayCeiling", new Vector3(0f, top, cz), new Vector3(bw, 0.2f, bd), dark);
            Box("BayWallN", new Vector3(0f, (floor + top) * 0.5f, z0 + bd), new Vector3(bw, top - floor, 0.3f), dark);
            Box("BayWallE", new Vector3(bw * 0.5f, (floor + top) * 0.5f, cz), new Vector3(0.3f, top - floor, bd), dark);
            Box("BayWallW", new Vector3(-bw * 0.5f, (floor + top) * 0.5f, cz), new Vector3(0.3f, top - floor, bd), dark);
            Box("BayWallS", new Vector3(0f, (floor - 0.1f) * 0.5f + 0f, z0), new Vector3(bw, -floor, 0.3f), dark);
            for (var i = -3; i <= 3; i++)
            {
                Box("BayRib" + i, new Vector3(i * 5.5f, top - 0.8f, cz), new Vector3(0.6f, 1.4f, bd), dark);
                Box("BayRibLight" + i, new Vector3(i * 5.5f, top - 1.55f, cz), new Vector3(0.12f, 0.08f, bd * 0.95f), cyan);
            }

            // Cradle: two beams along the hull and clamp towers under it, amber hazard lights.
            var under = Cradle.y - 2.6f;
            Box("CradleBeamN", new Vector3(0f, under, Cradle.z + 3.2f), new Vector3(22f, 0.6f, 0.9f), dark);
            Box("CradleBeamS", new Vector3(0f, under, Cradle.z - 3.2f), new Vector3(22f, 0.6f, 0.9f), dark);
            for (var i = -1; i <= 1; i++)
            {
                var x = i * 8f;
                var hgt = under - floor;
                foreach (var dz in new[] { -3.2f, 3.2f })
                {
                    Box("Clamp" + i + dz, new Vector3(x, floor + hgt * 0.5f, Cradle.z + dz), new Vector3(0.7f, hgt, 0.7f), dark);
                    Box("Hazard" + i + dz, new Vector3(x, under + 0.34f, Cradle.z + dz), new Vector3(0.75f, 0.08f, 0.75f), amber);
                }
            }

            Box("FloodBarN", new Vector3(0f, top - 3f, Cradle.z + 7f), new Vector3(18f, 0.25f, 0.4f), cyan);
            Box("FloodBarS", new Vector3(0f, top - 3f, Cradle.z - 7f), new Vector3(18f, 0.25f, 0.4f), cyan);

            _hullBay = new GameObject("ShipInCradle").transform;
            _hullBay.SetParent(transform, false);
            _hullBay.localPosition = Cradle;
            _hullBay.localRotation = Broadside;

            Light("BayFlood", Cradle + new Vector3(0f, 8f, -6f), new Color(0.85f, 0.95f, 1f), 4f, 30f);
            Light("BayFill", Cradle + new Vector3(0f, 1.5f, -8f), new Color(0.6f, 0.85f, 1f), 2.5f, 16f);

            // Welding sparks: one shared emitter, moved to the module being fitted and burst.
            var sparksGo = new GameObject("WeldSparks");
            sparksGo.transform.SetParent(transform, false);
            _sparks = sparksGo.AddComponent<ParticleSystem>();
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _sparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.45f), new Color(0.5f, 0.9f, 1f));
            main.gravityModifier = 0.8f;
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = _sparks.emission;
            emission.rateOverTime = 0f;
            var shape = _sparks.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;
            var pr = sparksGo.GetComponent<ParticleSystemRenderer>();
            var sparkShader = Shader.Find("SU/ParticleGlow");
            var sparkMat = sparkShader != null ? new Material(sparkShader) : _art.AmberEmit(3f);
            if (sparkMat.HasProperty("_MainTex") && _art.ProjectorGlow != null)
                sparkMat.mainTexture = _art.ProjectorGlow;
            if (sparkMat.HasProperty("_Color"))
                sparkMat.SetColor("_Color", new Color(1f, 0.7f, 0.3f, 1f));
            if (sparkMat.HasProperty("_Emission"))
                sparkMat.SetColor("_Emission", new Color(1f, 0.62f, 0.22f, 1f));
            if (sparkMat.HasProperty("_EmissionMul"))
                sparkMat.SetFloat("_EmissionMul", 3f);
            pr.sharedMaterial = sparkMat;
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.velocityScale = 0.06f;
        }

        /// <summary>Weld flash on the ship in the cradle, at grid cell (x, y).</summary>
        void Weld(int x, int y)
        {
            if (_sparks == null || _hullBay == null)
                return;
            var c = WorldScale.ShipCell;
            _sparks.transform.position = _hullBay.TransformPoint(new Vector3((x - ModuleCatalog.CoreCell) * c, 1.2f,
                (y - ModuleCatalog.CoreCell) * c));
            _sparks.Emit(90);
            // The module seating, then the torches running along its seam.
            CicCue.Clunk(_sparks.transform.position);
            StartCoroutine(WeldCrackle(_sparks.transform.position));
        }

        System.Collections.IEnumerator WeldCrackle(Vector3 at)
        {
            for (var i = 0; i < 5; i++)
            {
                CicCue.Weld(at + Random.insideUnitSphere * 0.4f);
                yield return new WaitForSeconds(Random.Range(0.12f, 0.26f));
            }
        }

        void Light(string name, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
        }

        // ── Grid + hologram ───────────────────────────────────────────────────────

        void BuildGrid()
        {
            _gridRoot = new GameObject("ModuleGrid").transform;
            _gridRoot.SetParent(transform, false);
            _gridRoot.localPosition = Table + new Vector3(0f, GridHeight + 0.02f, 0f);
            _gridRoot.localRotation = Broadside;
            var mat = _art.RadarIcon(Texture2D.whiteTexture, Color.white);
            for (var x = 0; x < ModuleCatalog.Grid; x++)
            for (var y = 0; y < ModuleCatalog.Grid; y++)
            {
                var cell = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cell.name = "Cell_" + x + "_" + y;
                Destroy(cell.GetComponent<Collider>());
                cell.transform.SetParent(_gridRoot, false);
                cell.transform.localPosition = CellLocal(x, y);
                cell.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cell.transform.localScale = Vector3.one * (Cell * 0.9f);
                var r = cell.GetComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                _cells[x, y] = r;

                var box = cell.AddComponent<BoxCollider>();
                box.size = new Vector3(1f, 1f, 0.2f);
                var xi = cell.AddComponent<XRSimpleInteractable>();
                var cx = x;
                var cy = y;
                xi.selectEntered.AddListener(_ => OnCell(cx, cy));
                xi.hoverEntered.AddListener(_ => SetHover(cx, cy));
                xi.hoverExited.AddListener(_ => SetHover(-1, -1));

                // Grid-aligned as on the hull in the cradle (grid y = toward the bow).
                (_minis[x, y], _miniFilters[x, y]) = Mini("Mini_" + x + "_" + y, CellLocal(x, y) + new Vector3(0f, 0.004f, 0f));
                // The miniature stands 0.2 m tall: a ray aimed at it must find its cell, not the one behind it.
                var miniCol = _minis[x, y].gameObject.AddComponent<BoxCollider>();
                miniCol.enabled = false;
                _miniCols[x, y] = miniCol;
                var mi = _minis[x, y].gameObject.AddComponent<XRSimpleInteractable>();
                mi.selectEntered.AddListener(_ => OnCell(cx, cy));
                mi.hoverEntered.AddListener(_ => SetHover(cx, cy));
                mi.hoverExited.AddListener(_ => SetHover(-1, -1));
            }

            (_ghost, _ghostFilter) = Mini("HeldGhost", Vector3.zero);

            // Which way the bow lies: beyond the last row, read from the stand.
            var bow = UiKit.Label(_gridRoot, "Bow", Trans.Get("vr.dock.guide.bow") + "  »",
                CellLocal(ModuleCatalog.CoreCell, ModuleCatalog.Grid) + new Vector3(0f, 0.004f, 0.05f), 0.6f, 0.05f, Accent);
            bow.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            bow.transform.rotation = transform.rotation * Quaternion.Euler(90f, 0f, 0f);

            BuildGuide();
        }

        (MeshRenderer, MeshFilter) Mini(string name, Vector3 local)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_gridRoot, false);
            go.transform.localPosition = local;
            go.transform.localScale = Vector3.one * MiniScale;
            var r = go.GetComponent<MeshRenderer>();
            if (_miniMat == null)
            {
                // Right under the key light: less gain than on the wall, and the body in its family colour.
                _miniMat = new Material(ModuleShelves.BlockMat()) { name = "SU_ModuleBlock_Table", enableInstancing = true };
                _miniMat.SetFloat("_LightGain", 0.7f);
                _miniMat.SetFloat("_Tint", 0.6f);
            }

            r.sharedMaterial = _miniMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return (r, go.GetComponent<MeshFilter>());
        }

        /// <summary>
        /// The assembly guide, a holo board high over the table's far rim (above the line of sight to the cradle):
        /// the hull being worked on, the step to take now, and the last answer or the module under the ray.
        /// </summary>
        void BuildGuide()
        {
            var board = new GameObject("AssemblyGuide").transform;
            board.SetParent(transform, false);
            board.localPosition = Table + new Vector3(0f, 2.1f, 0.75f);
            board.localRotation = Quaternion.Euler(-8f, 0f, 0f);
            var back = UiKit.MeshPiece(board, "Back", UiMeshes.RoundedBox(new Vector3(1.46f, 0.36f, 0.014f), 0.02f),
                UiKit.Chassis, new Vector3(0f, 0f, 0.01f));
            var br = back.GetComponent<MeshRenderer>();
            br.GetPropertyBlock(_mpb);
            _mpb.SetColor(UiKit.AccentId, Accent);
            _mpb.SetFloat(UiKit.AccentMulId, 0.05f);
            br.SetPropertyBlock(_mpb);
            _mpb.Clear();
            _guideHull = UiKit.Label(board, "Hull", string.Empty, new Vector3(0f, 0.13f, -0.002f), 1.3f, 0.03f,
                UiKit.TextDim, TextAlignmentOptions.MidlineLeft);
            _guideHull.richText = true;
            _guideStep = UiKit.Label(board, "Step", string.Empty, new Vector3(0f, 0.045f, -0.002f), 1.3f, 0.046f,
                UiKit.TextBright, TextAlignmentOptions.MidlineLeft, wrap: true);
            _guideStep.rectTransform.sizeDelta = new Vector2(130f, 12f);
            _guideStep.richText = true;
            _guideInfo = UiKit.Label(board, "Info", string.Empty, new Vector3(0f, -0.105f, -0.002f), 1.3f, 0.03f,
                DiegeticUi.CyanDim, TextAlignmentOptions.MidlineLeft, wrap: true);
            _guideInfo.rectTransform.sizeDelta = new Vector2(130f, 13f);
            _guideInfo.richText = true;
        }

        /// <summary>The board's first two lines follow the dock's state; the third is <see cref="SetStatus"/>.</summary>
        void RenderGuide(int validCells)
        {
            if (_guideStep == null)
                return;
            if (_fleetId <= 0)
                _guideHull.text = string.Empty;
            else
            {
                var fleet = _focus?.FindFleet(_fleetId);
                var name = fleet != null && !string.IsNullOrEmpty(fleet.Name) ? fleet.Name : "#" + _fleetId;
                var station = StationHull;
                var tag = station ? ModuleCatalog.StationTagColor : ModuleCatalog.ShipTagColor;
                _guideHull.text = "<color=#" + ColorUtility.ToHtmlStringRGB(tag) + "><b>[" +
                                  Trans.Get(station ? "vr.module.compatStation" : "vr.module.compatShip") + "]</b></color>  " +
                                  name + "   <color=#7fd8ff>" + _layout.Count + " " + Trans.Get("modules") + "</color>";
            }

            string step;
            if (_preview != null)
                step = "<color=#7fd8ff>" + Trans.Get("vr.dock.guide.preview") + "</color>";
            else if (_fleetId <= 0)
                step = Trans.Get("vr.dock.guide.pickShip");
            else if (_selectedType != null && _shelves != null && _shelves.Holding)
            {
                var refusal = FitRefusal(_selectedType);
                step = refusal != null
                    ? "<color=#ff6a5a>" + Trans.Get(refusal) + "</color>"
                    : Trans.Format("vr.dock.guide.place", "<color=#7dffa0>" + Trans.Get(ModuleCatalog.NameKey(_selectedType)) + "</color>") +
                      "   <size=70%><color=#7fd8ff>" + Trans.Format("vr.dock.guide.cells", validCells) + "</color></size>";
            }
            else
                step = Trans.Get("vr.dock.guide.take") + "\n<size=62%><color=#7fd8ff>" + Trans.Get("vr.dock.guide.remove") + "</color></size>";

            _guideStep.text = step;
        }

        /// <summary>Grid y runs away from the stand (toward the far wall), x to the right.</summary>
        static Vector3 CellLocal(int x, int y) =>
            new((x - ModuleCatalog.CoreCell) * Cell, 0f, (y - ModuleCatalog.CoreCell) * Cell);

        void RebuildHull()
        {
            if (_hullBuilt != null)
                Destroy(_hullBuilt.gameObject);
            _hullBuilt = null;
            var placed = new List<FocusShipModule>();
            foreach (var m in _preview != null ? _preview.Modules : _layout)
                if (m.OnGrid)
                    placed.Add(m);
            if (placed.Count == 0)
                return;
            _hullBuilt = new GameObject("Hull").transform;
            _hullBuilt.SetParent(_hullBay, false);
            // 1:1 in the cradle; mirror Z so grid y always runs toward the bow, as on the table.
            var zSign = ShipHullBuilder.NoseSignFor(placed);
            _hullBuilt.localScale = new Vector3(1f, 1f, zSign);
            ShipHullBuilder.Build(_hullBuilt, placed, owned: true, seed: _fleetId);
            if (_preview == null)
                return;
            // Blueprint: the design stands in the cradle as a hologram of itself (no metal until loaded).
            _hullBuilt.name = "BlueprintHull";
            var mat = BlueprintMat();
            foreach (var r in _hullBuilt.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                {
                    r.enabled = false;
                    continue;
                }

                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++)
                    mats[i] = mat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            foreach (var l in _hullBuilt.GetComponentsInChildren<Light>(true))
                l.enabled = false;
        }

        Material BlueprintMat()
        {
            if (_blueprintMat != null)
                return _blueprintMat;
            var shader = Shader.Find("SU/HoloCrystal");
            _blueprintMat = shader != null
                ? new Material(shader) { name = "SU_Blueprint" }
                : _art.Holo(Texture2D.whiteTexture, new Color(0.3f, 0.85f, 1f, 0.5f));
            if (_blueprintMat.HasProperty("_Color"))
                _blueprintMat.SetColor("_Color", new Color(0.05f, 0.25f, 0.4f, 1f));
            if (_blueprintMat.HasProperty("_Emission"))
                _blueprintMat.SetColor("_Emission", new Color(0.3f, 0.85f, 1f, 1f));
            if (_blueprintMat.HasProperty("_EmissionMul"))
                _blueprintMat.SetFloat("_EmissionMul", 1.3f);
            if (_blueprintMat.HasProperty("_Rim"))
                _blueprintMat.SetFloat("_Rim", 2.6f);
            if (_blueprintMat.HasProperty("_Pulse"))
                _blueprintMat.SetFloat("_Pulse", 0.8f);
            return _blueprintMat;
        }

        void PaintGrid()
        {
            var occupied = Occupancy();
            var holding = _shelves != null && _shelves.Holding;
            var canFit = _fleetId > 0 && _preview == null && _selectedType != null && FitRefusal(_selectedType) == null;
            var valid = 0;
            for (var x = 0; x < ModuleCatalog.Grid; x++)
            for (var y = 0; y < ModuleCatalog.Grid; y++)
            {
                var onHand = false;
                var m = _preview != null ? PreviewAt(x, y, out onHand) : At(x, y);
                var open = m == null && canFit && ModuleCatalog.CanPlace(occupied, x, y);
                if (open)
                    valid++;
                var armed = _armedRemove.HasValue && _armedRemove.Value == (x, y);
                Color c;
                if (_preview != null)
                {
                    // Blueprint projection: green = the part is on hand, red = missing from the hangar.
                    c = m == null ? new Color(0.3f, 0.8f, 1f, 0.08f)
                        : onHand ? new Color(0.35f, 1f, 0.55f, 0.5f)
                        : new Color(1f, 0.3f, 0.25f, 0.6f);
                }
                else if (m != null)
                {
                    // The miniature carries the module; its cell is a dim pad in the family colour.
                    c = ModuleCatalog.Accent(ModuleCatalog.Family(m.Type)) * 0.55f;
                    c.a = 0.45f;
                    if (armed)
                        c = new Color(1f, 0.3f, 0.25f, 1f);
                }
                else if (open)
                    c = new Color(0.4f, 1f, 0.55f, 0.8f);
                else
                    c = new Color(0.3f, 0.8f, 1f, 0.1f);

                var hovered = _hover.HasValue && _hover.Value == (x, y);
                if (hovered)
                    c = holding && m == null && !open ? new Color(1f, 0.3f, 0.25f, 0.85f) : Color.Lerp(c, Color.white, 0.35f);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_Emission", new Color(c.r, c.g, c.b, 1f) * 0.6f);
                _cells[x, y].SetPropertyBlock(_mpb);
                _mpb.Clear();

                var mini = _minis[x, y];
                if (m == null)
                {
                    mini.enabled = false;
                    _miniCols[x, y].enabled = false;
                    continue;
                }

                var mesh = ModuleShelves.BlockMesh(m.Type);
                if (_miniFilters[x, y].sharedMesh != mesh)
                {
                    _miniFilters[x, y].sharedMesh = mesh;
                    _miniCols[x, y].center = mesh.bounds.center;
                    _miniCols[x, y].size = mesh.bounds.size;
                }

                mini.enabled = true;
                _miniCols[x, y].enabled = true;
                var accent = _preview != null ? (onHand ? new Color(0.35f, 1f, 0.55f) : new Color(1f, 0.3f, 0.25f))
                    : armed ? UiKit.Danger
                    : ModuleCatalog.Accent(ModuleCatalog.Family(m.Type));
                _mpb.SetColor(BlockAccentId, accent);
                _mpb.SetFloat(BlockHoverId, hovered || armed ? 1f : 0f);
                _mpb.SetFloat(BlockGhostId, _preview != null ? 1f : 0f);
                _mpb.SetFloat(BlockRevealId, 1.1f);
                mini.SetPropertyBlock(_mpb);
                _mpb.Clear();
            }

            // The module in hand, a hologram on the cell it would land on.
            var target = holding && _selectedType != null && _hover.HasValue && At(_hover.Value.x, _hover.Value.y) == null
                ? _hover : null;
            _ghost.enabled = target.HasValue;
            if (target.HasValue)
            {
                var (tx, ty) = target.Value;
                var ok = canFit && ModuleCatalog.CanPlace(occupied, tx, ty);
                _ghostFilter.sharedMesh = ModuleShelves.BlockMesh(_selectedType);
                _ghost.transform.localPosition = CellLocal(tx, ty) + new Vector3(0f, 0.004f, 0f);
                _mpb.SetColor(BlockAccentId, ok ? new Color(0.4f, 1f, 0.55f) : UiKit.Danger);
                _mpb.SetFloat(BlockHoverId, 1f);
                _mpb.SetFloat(BlockGhostId, 1f);
                _mpb.SetFloat(BlockRevealId, 1.1f);
                _ghost.SetPropertyBlock(_mpb);
                _mpb.Clear();
            }

            RenderGuide(valid);
        }

        FocusShipModule PreviewAt(int x, int y, out bool onHand)
        {
            onHand = false;
            for (var i = 0; i < _preview.Modules.Count; i++)
            {
                var m = _preview.Modules[i];
                if (m.GridX != x || m.GridY != y)
                    continue;
                onHand = _previewOk != null && i < _previewOk.Length && _previewOk[i];
                return m;
            }

            return null;
        }

        FocusShipModule At(int x, int y)
        {
            foreach (var m in _layout)
                if (m.GridX == x && m.GridY == y)
                    return m;
            return null;
        }

        bool[,] Occupancy()
        {
            var o = new bool[ModuleCatalog.Grid, ModuleCatalog.Grid];
            foreach (var m in _layout)
                if (m.OnGrid)
                    o[m.GridX, m.GridY] = true;
            return o;
        }

        void SetHover(int x, int y)
        {
            _hover = x < 0 ? null : (x, y);
            PaintGrid();
            if (x >= 0 && At(x, y) is { } m)
            {
                var fam = ModuleCatalog.Family(m.Type);
                var stats = ModuleCatalog.StatsLine(m.Type);
                SetStatus("<b>" + Trans.Get(ModuleCatalog.NameKey(m.Type)) + "</b>  <color=#" +
                          ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(fam)) + ">" + Trans.Get(ModuleCatalog.FamilyKey(fam)) +
                          "</color>" + (stats.Length > 0 ? "\n<size=85%>" + stats + "</size>" : string.Empty));
            }
        }

        // ── Screens ───────────────────────────────────────────────────────────────

        void BuildScreens()
        {
            var metal = _art.MetalPanel(0.45f);
            var dark = _art.DarkPanel(0.3f);
            var cyan = _art.CyanEmit(2.4f);
            var amber = _art.AmberEmit(2f);

            // Ship console, left of the table, turned to the stand: docked ships, new ship, rename, readout.
            var shipMount = GateRoomDecor.Desk(transform, "ShipDesk", ShipDesk,
                GateRoomDecor.FaceStand(ShipDesk.x - Stand.x, ShipDesk.z - Stand.z), 1.05f, metal, dark, cyan, amber);
            _shipScreen = HoloScreen.Create(transform, "DockShipScreen", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.dock.title"));
            GateRoomDecor.SeatOnArm(_shipScreen.transform, shipMount, ScreenSize.y);
            _shipScreen.SetAccent(Accent, 0.5f);
            _shipBody = Body(_shipScreen);
            _status = DiegeticUi.HoloLabel(_shipScreen.Content, string.Empty, new Vector2(0f, -350f),
                new Vector2(900f, 60f), 17f, DiegeticUi.CyanDim);
            _status.textWrappingMode = TextWrappingModes.Normal;

            // Blueprint console, right of the table: save / project / load designs.
            var plansMount = GateRoomDecor.Desk(transform, "PlansDesk", PlansDesk,
                GateRoomDecor.FaceStand(PlansDesk.x - Stand.x, PlansDesk.z - Stand.z), 1.05f, metal, dark, cyan, amber);
            var plans = HoloScreen.Create(transform, "DockPlansScreen", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("shipTemplates"));
            GateRoomDecor.SeatOnArm(plans.transform, plansMount, ScreenSize.y);
            plans.SetAccent(Accent, 0.5f);
            _bpBody = Sub(Body(plans), "Blueprints", -20f);
            _blueprints = new BlueprintPanel(_bpBody, () => _fleetId, Stock, (t, e) => SetStatus(t, e), Preview,
                async () =>
                {
                    await AfterEdit();
                    RenderAll();
                });
            _blueprints.StationHull = () => StationHull;

            // Printer console on the starboard wall, between the store and the printer, operator facing the
            // wall: the store on the left, the printer on the right.
            var yardMount = GateRoomDecor.Desk(transform, "PrinterDesk", new Vector3(RoomWidth * 0.5f - 0.82f, 0f, YardDeskZ),
                90f, 1.0f, metal, dark, cyan, amber);
            _yardScreen = HoloScreen.Create(transform, "DockYardScreen", ScreenSize, Vector3.zero, Quaternion.identity,
                Trans.Get("shipyard"));
            GateRoomDecor.SeatOnArm(_yardScreen.transform, yardMount, ScreenSize.y);
            _yardScreen.SetAccent(Accent, 0.5f);
            var yardRoot = Body(_yardScreen);
            _yardBody = Sub(yardRoot, "Yard", -20f);
            _yardStatus = DiegeticUi.HoloLabel(_yardScreen.Content, string.Empty, new Vector2(0f, -372f),
                new Vector2(900f, 44f), 16f, DiegeticUi.CyanDim);
            _yardStatus.textWrappingMode = TextWrappingModes.Normal;
            _yard = new ShipyardPanel(_yardBody, _eco, () => _planetId, SetYardStatus, () =>
            {
                RenderPanels();
                RenderShipScreen();
            });
            // Way back: the door in the aft wall, well behind the stand (walk through it facing it, or use its
            // panel). Deliberate: you work here with your back to it, so backing off the table never exits.
            RoomDoor.Build(transform, "DoorToBridge", new Vector3(0f, 0f, RoomSouth + 0.06f), 0f, Trans.Get("vr.dock.leave"),
                UiKit.Amber, _art, () => Inside, () => AsyncTap.Run(Leave()), deliberate: true);
        }

        /// <summary>
        /// Module store on the starboard wall, abeam the table: its face is 4.9 m out from the centreline, 3.5 m
        /// clear of the table's rim; its control column stands at the forward end, nearest the table.
        /// </summary>
        void BuildShelves()
        {
            _shelves = ModuleShelves.Build(transform, _art, new Vector3(RoomWidth * 0.5f - 0.06f, 0f, StoreZ),
                Quaternion.Euler(0f, 90f, 0f), ShelfStock, () => _fleetId > 0 && _preview == null,
                () => _selectedType, OnShelfPick, OnShelfDrop, _yard, (text, error) => SetStatus(text, error),
                type => _fleetId > 0 ? FitRefusal(type) : null);
        }

        /// <summary>
        /// Aft of the printer console on the same wall: the recycler, then the module printer joined to the store
        /// by its overhead transfer rail; the printer ends 0.7 m before the aft wall, clear of the coolant tanks.
        /// </summary>
        void BuildFabrication()
        {
            _recycler = ModuleRecycler.Build(transform, _art, new Vector3(RoomWidth * 0.5f - 0.06f, 0f, RecyclerZ),
                Quaternion.Euler(0f, 90f, 0f), Recycle);
            ModulePrinter.Build(transform, _art, new Vector3(RoomWidth * 0.5f - 0.06f, 0f, PrinterZ),
                Quaternion.Euler(0f, 90f, 0f), _yard, _shelves.HeaderEndWorld, RoomHeight);
        }

        /// <summary>
        /// The store: every module type with the hangar's count (0 = a hologram the printer can make). What the
        /// picked hull may not carry (web ShipBuilderUI 69d40af: a fortress takes no propulsion, a ship no
        /// fortress module, neither a core) stays on the wall, dimmed and not to be taken, but still printable:
        /// a StationCore is printed with a ship on the table, before any fortress exists.
        /// </summary>
        Dictionary<string, int> ShelfStock()
        {
            var d = new Dictionary<string, int>();
            foreach (var type in ModuleCatalog.Types())
                d[type] = 0;
            foreach (var (type, count) in HangarGroups())
                d[type] = count;
            return d;
        }

        /// <summary>The picked hull is an orbital fortress (isStation, or a StationCore at its heart).</summary>
        bool StationHull
        {
            get
            {
                if (_fleetId <= 0)
                    return false;
                if (_focus?.FindFleet(_fleetId) is { IsStation: true })
                    return true;
                foreach (var m in _layout)
                    if (m.Type == ModuleCatalog.StationCore)
                        return true;
                return false;
            }
        }

        /// <summary>Why this module may not go on the picked hull (native key), or null.</summary>
        string FitRefusal(string type)
        {
            if (ModuleCatalog.IsCore(type))
                return "cannotPlaceCore";
            var station = StationHull;
            if (ModuleCatalog.Compat(type).Fits(station))
                return null;
            return station ? "stationCannotEquipPropulsion" : "moduleOnlyForStations";
        }

        /// <summary>A block taken off the store: it becomes the module being fitted (green cells light up).</summary>
        void OnShelfPick(string type)
        {
            _selectedType = type;
            _armedRemove = null;
            PaintGrid();
            SetStatus(Trans.Get(ModuleCatalog.NameKey(type)) + "  " + ModuleCatalog.CompatTags(type) + " — " +
                      Trans.Get(ModuleCatalog.DescKey(type)));
        }

        /// <summary>
        /// A store block let go: in the recycler's mouth = offered for scrapping; over a free cell that touches
        /// the structure = PlaceShipModule (true: the block stays there and dissolves, the store replicates it);
        /// anywhere else it flies back to its shelf.
        /// </summary>
        bool OnShelfDrop(string type, Vector3 world)
        {
            _hover = null;
            if (Inside && _recycler != null && _recycler.Catches(world))
            {
                _selectedType = null;
                _recycler.Offer(type);
                PaintGrid();
                return true;
            }

            var cell = Inside ? NearestCell(world) : null;
            if (!cell.HasValue)
            {
                _selectedType = null;
                PaintGrid();
                return false;
            }

            var (x, y) = cell.Value;
            _selectedType = type;
            var fits = _fleetId > 0 && _preview == null && !_busy && At(x, y) == null &&
                       FitRefusal(type) == null && ModuleCatalog.CanPlace(Occupancy(), x, y) && HangarRow(type) != null;
            // Same path as a ray tap on the cell: it places, or says why not.
            OnCell(x, y);
            if (!fits)
                _selectedType = null;
            PaintGrid();
            return fits;
        }

        static RectTransform Sub(RectTransform parent, string name, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = parent.sizeDelta;
            rt.anchoredPosition = new Vector2(0f, y);
            return rt;
        }

        static RectTransform Body(HoloScreen screen)
        {
            var go = new GameObject("Body", typeof(RectTransform));
            go.transform.SetParent(screen.Content, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = screen.PixelSize;
            return rt;
        }

        void Clear(RectTransform body)
        {
            for (var i = body.childCount - 1; i >= 0; i--)
                DestroyImmediate(body.GetChild(i).gameObject);
        }

        TMP_Text Text(RectTransform body, string text, float x, float y, float width, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var left = align is TextAlignmentOptions.MidlineLeft or TextAlignmentOptions.TopLeft;
            var t = DiegeticUi.HoloLabel(body, text, new Vector2(left ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            return t;
        }

        Button Btn(RectTransform body, string label, float x, float y, float w, float h, System.Action act,
            DiegeticUi.BtnStyle style)
        {
            var b = DiegeticUi.HoloButton(body, label, new Vector2(x, y), new Vector2(w, h),
                () => { if (!_busy) act(); }, style);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 11f;
            t.fontSizeMax = Mathf.Min(20f, h * 0.42f);
            return b;
        }

        void SetStatus(string text, bool error = false)
        {
            if (_status == null)
                return;
            _status.text = text ?? string.Empty;
            _status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
            // The same answer on the assembly guide, where the eyes are while fitting.
            if (_guideInfo != null)
            {
                _guideInfo.text = _status.text;
                _guideInfo.color = error ? UiKit.Danger : UiKit.TextBright;
            }
        }

        void SetYardStatus(string text, bool error)
        {
            if (_yardStatus == null)
                return;
            _yardStatus.text = text ?? string.Empty;
            _yardStatus.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        void RenderShipScreen()
        {
            Clear(_shipBody);
            _eco.TryGet(_planetId, out var planet);
            var planetName = planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + _planetId;
            Text(_shipBody, "<b>" + planetName + "</b>  <size=70%><color=#7fd8ff>" +
                            GalaxyCatalog.Label(planet?.SystemId ?? 0) + "</color></size>",
                -440f, 245f, 880f, 24f, UiKit.TextBright);

            // Ships docked at this planet (fleets.planetid), then "new ship" from a ShipCore in the hangar.
            var docked = Docked();
            var y = 180f;
            for (var i = 0; i < docked.Count && i < 4; i++)
            {
                var f = docked[i];
                var id = f.Id;
                var name = string.IsNullOrEmpty(f.Name) ? "#" + f.Id : f.Name;
                if (f.IsStation)
                    name = "<color=#fbbf24>◆</color> " + name;
                Btn(_shipBody, name + "  #" + f.Id, -225f + (i % 2) * 450f, y - (i / 2) * 58f, 430f, 50f,
                    () => SelectFleet(id), id == _fleetId ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            }

            y -= Mathf.CeilToInt(Mathf.Min(docked.Count, 4) / 2f) * 58f;
            // A new hull starts from its core (AddToFleet fleet=0): ShipCore → ship, StationCore → orbital fortress.
            var core = HangarRow(ModuleCatalog.Core);
            var newShip = Btn(_shipBody, Trans.Get("vr.dock.newShip"), -225f, y, 430f, 50f,
                () => AsyncTap.Run(NewShip(ModuleCatalog.Core)), core != null ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
            newShip.interactable = core != null;
            var stationCore = HangarRow(ModuleCatalog.StationCore);
            var newStation = Btn(_shipBody, Trans.Get("vr.dock.newStation"), 225f, y, 430f, 50f,
                () => AsyncTap.Run(NewShip(ModuleCatalog.StationCore)),
                stationCore != null ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
            newStation.interactable = stationCore != null;
            y -= 40f;
            if (core == null)
                Text(_shipBody, Trans.Get("vr.dock.needCore"), -440f, y, 430f, 14f, DiegeticUi.CyanDim);
            if (stationCore == null)
                Text(_shipBody, Trans.Get("vr.dock.needStationCore"), 10f, y, 430f, 14f, DiegeticUi.CyanDim);
            y -= 40f;

            if (_fleetId <= 0)
            {
                Text(_shipBody, Trans.Get(docked.Count == 0 ? "vr.dock.noShipDocked" : "vr.dock.pickShip"), 0f, y - 40f,
                    880f, 20f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                return;
            }

            // Rename (keyboard) then the design readout.
            _nameField = DiegeticUi.HoloField(_shipBody, "FleetName", Trans.Get("rename"), new Vector2(-170f, y),
                new Vector2(540f, 48f), TouchScreenKeyboardType.Default);
            _nameField.characterLimit = 32;
            _nameField.text = _focus?.FindFleet(_fleetId)?.Name ?? string.Empty;
            // Saved as soon as the keyboard closes; the button stays for an explicit save.
            _nameField.onEndEdit.AddListener(_ => AsyncTap.Run(Rename()));
            Btn(_shipBody, Trans.Get("rename"), 300f, y, 220f, 48f, () => AsyncTap.Run(Rename()), DiegeticUi.BtnStyle.Ghost);
            y -= 70f;

            var s = ModuleCatalog.Sum(_layout);
            var station = StationHull;
            var rows = new (string, string)[]
            {
                (Trans.Get("modules"), s.Modules.ToString()),
                (Trans.Get("armor"), Mathf.RoundToInt(s.Armor).ToString()),
                (Trans.Get("shield"), Mathf.RoundToInt(s.Shield).ToString()),
                (Trans.Get("damage"), Mathf.RoundToInt(s.Damage).ToString()),
                (Trans.Get("speed"), station ? Trans.Get("vr.dock.immobile") : Mathf.RoundToInt(s.Speed).ToString()),
                (Trans.Get("cargo"), Mathf.RoundToInt(s.Cargo).ToString()),
                (Trans.Get("vr.dock.troops"), Mathf.RoundToInt(s.TroopCargo).ToString()),
                (Trans.Get("vr.dock.size"), Mathf.RoundToInt(s.Size).ToString())
            };
            for (var i = 0; i < rows.Length; i++)
            {
                var col = i % 2;
                var row = i / 2;
                var rx = col == 0 ? -440f : 20f;
                var ry = y - row * 40f;
                Text(_shipBody, rows[i].Item1, rx, ry, 250f, 17f, DiegeticUi.CyanDim);
                Text(_shipBody, rows[i].Item2, rx + 250f, ry, 160f, 19f, UiKit.TextBright, TextAlignmentOptions.MidlineRight);
            }

            y -= 4 * 40f + 10f;
            // Jump drives: the server wants one per modulesPerJumpModule modules (GetFleetStats).
            if (!station && (s.Hyperdrives > 0 || s.PrlBonds > 0))
            {
                var hyperOk = s.Hyperdrives >= s.JumpRequired;
                var prlOk = s.PrlBonds >= s.JumpRequired;
                var line = string.Empty;
                if (s.Hyperdrives > 0)
                    line += Trans.Get("HyperspaceDrive") + " " + Tone(s.Hyperdrives + "/" + s.JumpRequired, hyperOk) + "   ";
                if (s.PrlBonds > 0)
                    line += Trans.Get("BondPRLModule") + " " + Tone(s.PrlBonds + "/" + s.JumpRequired, prlOk);
                Text(_shipBody, Trans.Get("jumpModuleRequirementLabel") + " : " + line, -440f, y, 880f, 17f, UiKit.TextBright);
            }
        }

        static string Tone(string t, bool ok) => ok ? "<color=#7dffa0>" + t + "</color>" : "<color=#ff6a5a>" + t + "</color>";

        /// <summary>Store shelves, printer console and blueprint console follow the hangar / ship state.</summary>
        void RenderPanels()
        {
            RefreshShelves();
            _yard.Render();
            _blueprints.Render();
        }

        void RefreshShelves() => _shelves?.Refresh();

        // ── Data ──────────────────────────────────────────────────────────────────

        /// <summary>Finished hangar modules of the planet (GetResource.hangar: fleetid 0, endTime passed).</summary>
        List<JObject> Hangar()
        {
            var list = new List<JObject>();
            if (!_eco.TryGet(_planetId, out var planet) || !(planet.Raw?["hangar"] is JArray rows))
                return list;
            var now = FleetOrderGate.UnixNow();
            foreach (var r in rows)
            {
                if (r is JObject o && FocusContext.AsInt(o["fleetid"]) == 0 && FocusContext.AsLong(o["endTime"]) <= now)
                    list.Add(o);
            }

            return list;
        }

        JObject HangarRow(string type)
        {
            foreach (var r in Hangar())
                if (FocusContext.AsString(r["type"]) == type)
                    return r;
            return null;
        }

        List<(string type, int count)> HangarGroups()
        {
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (var r in Hangar())
            {
                var t = FocusContext.AsString(r["type"]);
                if (!counts.ContainsKey(t))
                {
                    counts[t] = 0;
                    order.Add(t);
                }

                counts[t]++;
            }

            order.Sort((a, b) =>
            {
                var fa = ModuleCatalog.Family(a).CompareTo(ModuleCatalog.Family(b));
                return fa != 0 ? fa : string.CompareOrdinal(a, b);
            });
            var list = new List<(string, int)>();
            foreach (var t in order)
                list.Add((t, counts[t]));
            return list;
        }

        /// <summary>Parts a blueprint can draw on: finished hangar modules + the ship's own (non-core) ones.</summary>
        Dictionary<string, int> Stock()
        {
            var stock = new Dictionary<string, int>();
            foreach (var (type, count) in HangarGroups())
                stock[type] = count;
            foreach (var m in _layout)
                if (!ModuleCatalog.IsCore(m.Type))
                    stock[m.Type] = (stock.TryGetValue(m.Type, out var n) ? n : 0) + 1;
            return stock;
        }

        /// <summary>Project a blueprint (null = back to the real ship) on the table and in the cradle.</summary>
        void Preview(Blueprint bp)
        {
            _preview = bp;
            _previewOk = bp != null ? _blueprints.Availability(bp) : null;
            _armedRemove = null;
            PaintGrid();
            RebuildHull();
            if (bp != null)
                CicCue.Deploy(transform.TransformPoint(Cradle));
        }

        List<FocusFleet> Docked()
        {
            var list = new List<FocusFleet>();
            if (_focus == null)
                return list;
            var now = FleetOrderGate.UnixNow();
            foreach (var f in _focus.Fleets)
                if (_focus.IsMine(f) && f.PlanetId == _planetId && !f.IsMoving(now))
                    list.Add(f);
            list.Sort((a, b) => a.Id.CompareTo(b.Id));
            return list;
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        /// <summary>Walk into the dock for <paramref name="planetId"/> (station planet or the one in orbit).</summary>
        public async Task Enter(int planetId, int fleetId)
        {
            if (Inside || DiplomacyRoom.InRoomBeyondCorridor)
                return;
            // The dock belongs to the orbital station you stand in: only one of our worlds has one here.
            await OwnedPlanets.EnsureLoaded();
            if (!OwnedPlanets.Contains(planetId))
                return;
            _planetId = planetId;
            _fleetId = 0;
            _selectedType = null;
            _shelves?.ResetAll();

            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            if (CorridorRoom.Inside)
                CorridorRoom.Instance.Depart();
            gameObject.SetActive(true);
            var rig = FindFirstObjectByType<XROrigin>();
            if (rig != null)
            {
                rig.transform.SetParent(transform, false);
                rig.transform.localPosition = Stand;
                rig.transform.localRotation = Quaternion.identity;
                XrPlacement.PlaceHead(rig, transform.TransformPoint(Stand), transform.forward);
            }

            Inside = true;
            await _eco.RefreshNow();
            var docked = Docked();
            var pick = docked.Find(f => f.Id == fleetId) ?? (docked.Count > 0 ? docked[0] : null);
            if (pick != null)
                await LoadFleet(pick.Id);
            else
                RenderAll();
            await fade.FadeIn();
            CicCue.Ok(transform.position + Stand);
        }

        async Task Leave()
        {
            if (!Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Inside = false;
            _shelves?.DropHeld();
            _blueprints?.Deselect();
            _recycler?.Clear();
            // Out into the corridor, in front of this room's door.
            CorridorRoom.ReturnPlayer(CorridorRoom.Slot.DockStarboard);

            gameObject.SetActive(false);
            await fade.FadeIn();
        }


        void SelectFleet(int id) => AsyncTap.Run(LoadFleet(id));

        async Task LoadFleet(int fleetId)
        {
            _fleetId = fleetId;
            _armedRemove = null;
            _layout.Clear();
            var r = await ActionJs.Get("GetShipLayout", new Dictionary<string, string> { { "fleet", fleetId.ToString() } });
            if (r.Ok && !string.IsNullOrEmpty(r.Body))
            {
                try
                {
                    FocusContext.ParseShipModules(JToken.Parse(r.Body), _layout);
                }
                catch
                {
                    _layout.Clear();
                }
            }
            else if (!r.Ok)
            {
                SetStatus(Error(r), true);
            }

            _layout.RemoveAll(m => !m.OnGrid);
            FocusContext.CacheLayout(fleetId, _layout);
            RenderAll();
        }

        void RenderAll()
        {
            if (_preview != null)
                _previewOk = _blueprints.Availability(_preview);
            RenderShipScreen();
            RenderPanels();
            PaintGrid();
            RebuildHull();
        }

        (int x, int y)? NearestCell(Vector3 world)
        {
            var local = _gridRoot.InverseTransformPoint(world);
            if (local.y < -0.08f || local.y > 0.45f)
                return null;
            var x = Mathf.RoundToInt(local.x / Cell) + ModuleCatalog.CoreCell;
            var y = Mathf.RoundToInt(local.z / Cell) + ModuleCatalog.CoreCell;
            if (x < 0 || y < 0 || x >= ModuleCatalog.Grid || y >= ModuleCatalog.Grid)
                return null;
            return (x, y);
        }

        // ── Orders ────────────────────────────────────────────────────────────────

        void OnCell(int x, int y)
        {
            if (_preview != null)
            {
                // Editing starts from the real ship: touching the table ends the projection.
                _blueprints.Deselect();
                return;
            }

            if (_busy || _fleetId <= 0)
            {
                if (_fleetId <= 0)
                    SetStatus(Trans.Get("vr.dock.pickShip"), true);
                return;
            }

            var m = At(x, y);
            if (m != null)
            {
                if (ModuleCatalog.IsCore(m.Type))
                {
                    SetStatus(Trans.Get("cannotRemoveCore"), true);
                    CicCue.Fail(_cells[x, y].transform.position);
                    return;
                }

                if (!ModuleCatalog.KeepsConnected(Occupancy(), x, y))
                {
                    SetStatus(Trans.Get("vr.dock.wouldSplit"), true);
                    CicCue.Fail(_cells[x, y].transform.position);
                    return;
                }

                // Two taps: arm, then take the module off (it goes back to the hangar).
                if (!_armedRemove.HasValue || _armedRemove.Value != (x, y) || Time.unscaledTime > _armedUntil)
                {
                    _armedRemove = (x, y);
                    _armedUntil = Time.unscaledTime + 4f;
                    SetStatus(Trans.Format("vr.dock.confirmRemove", Trans.Get(m.Type)), true);
                    PaintGrid();
                    return;
                }

                _armedRemove = null;
                AsyncTap.Run(Remove(m));
                return;
            }

            if (_selectedType == null)
            {
                SetStatus(Trans.Get("vr.dock.takeFromStore"));
                return;
            }

            var refusal = FitRefusal(_selectedType);
            if (refusal != null)
            {
                // Same refusal the server gives (PlaceShipModule 69d40af), without the round trip.
                SetStatus(Trans.Get(refusal), true);
                CicCue.Fail(_cells[x, y].transform.position);
                return;
            }

            if (!ModuleCatalog.CanPlace(Occupancy(), x, y))
            {
                SetStatus(Trans.Get("vr.dock.mustTouch"), true);
                CicCue.Fail(_cells[x, y].transform.position);
                return;
            }

            var row = HangarRow(_selectedType);
            if (row == null)
            {
                _selectedType = null;
                RenderPanels();
                return;
            }

            AsyncTap.Run(Place(FocusContext.AsInt(row["id"]), x, y));
        }

        async Task Place(int shipId, int x, int y)
        {
            _busy = true;
            try
            {
                var r = await ActionJs.Get("PlaceShipModule", new Dictionary<string, string>
                {
                    { "ship", shipId.ToString() },
                    { "fleet", _fleetId.ToString() },
                    { "gx", x.ToString() },
                    { "gy", y.ToString() }
                });
                Feedback(r, "addedToFleet", _cells[x, y].transform.position);
                if (r.Ok)
                    _pendingWeld = (x, y);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Engineering, "PlaceShipModule", r,
                    Trans.Get(_selectedType ?? string.Empty));
            }
            finally
            {
                _busy = false;
            }

            await AfterEdit();
            // The block was set down: nothing is in hand any more.
            if (_shelves == null || !_shelves.Holding)
                _selectedType = null;
            RenderAll();
            if (_pendingWeld.HasValue)
                Weld(_pendingWeld.Value.x, _pendingWeld.Value.y);
            _pendingWeld = null;
        }

        async Task Remove(FocusShipModule m)
        {
            _busy = true;
            try
            {
                var r = await ActionJs.Get("RemoveShipModule", new Dictionary<string, string> { { "ship", m.Id.ToString() } });
                Feedback(r, "vr.dock.removed", _cells[m.GridX, m.GridY].transform.position);
            }
            finally
            {
                _busy = false;
            }

            await AfterEdit();
            RenderAll();
        }

        async Task NewShip(string coreType)
        {
            var core = HangarRow(coreType);
            if (core == null)
                return;
            _busy = true;
            var before = new HashSet<int>();
            foreach (var f in Docked())
                before.Add(f.Id);
            try
            {
                var r = await ActionJs.Get("AddToFleet", new Dictionary<string, string>
                {
                    { "fleet", "0" },
                    { "ship", FocusContext.AsString(core["id"]) },
                    { "planet", _planetId.ToString() }
                });
                Feedback(r, coreType == ModuleCatalog.StationCore ? "orbitalStation" : "fleetCreated",
                    transform.position + Vector3.up * GridHeight);
            }
            finally
            {
                _busy = false;
            }

            await AfterEdit();
            // AddToFleet answers nothing: the new hull is the docked fleet we did not have before.
            foreach (var f in Docked())
                if (!before.Contains(f.Id))
                {
                    await LoadFleet(f.Id);
                    return;
                }

            RenderAll();
        }

        /// <summary>
        /// The recycler's confirmed order: one hangar module of that type → DelShip (no refund). Answers null
        /// when done, else the error the recycler shows.
        /// </summary>
        async Task<string> Recycle(string type)
        {
            var row = HangarRow(type);
            if (row == null)
                return Trans.Get("notFound");
            ApiResult r;
            _busy = true;
            try
            {
                r = await ActionJs.Get("DelShip", new Dictionary<string, string> { { "ship", FocusContext.AsString(row["id"]) } });
                if (r.Ok)
                    CicCue.Ok(_recycler.MawWorld);
                else
                    CicCue.Fail(_recycler.MawWorld);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
            RenderPanels();
            RenderShipScreen();
            PaintGrid();
            return r.Ok ? null : Error(r);
        }

        async Task Rename()
        {
            var name = (_nameField != null ? _nameField.text : string.Empty).Trim();
            if (name.Length == 0 || _fleetId <= 0 || name == (_focus?.FindFleet(_fleetId)?.Name ?? string.Empty))
                return;
            var r = await ActionJs.Get("RenameFleet", new Dictionary<string, string>
            {
                { "id", _fleetId.ToString() },
                { "name", name }
            });
            Feedback(r, "vr.dock.renamed", _shipScreen.transform.position);
            if (_poller != null)
                await _poller.PollNow();
            RenderShipScreen();
        }

        async Task AfterEdit()
        {
            await _eco.RefreshNow();
            if (_poller != null)
                await _poller.PollNow();
            if (_fleetId > 0)
            {
                var id = _fleetId;
                _layout.Clear();
                var r = await ActionJs.Get("GetShipLayout", new Dictionary<string, string> { { "fleet", id.ToString() } });
                if (r.Ok && !string.IsNullOrEmpty(r.Body))
                {
                    try
                    {
                        FocusContext.ParseShipModules(JToken.Parse(r.Body), _layout);
                    }
                    catch
                    {
                        _layout.Clear();
                    }
                }

                _layout.RemoveAll(m => !m.OnGrid);
                FocusContext.CacheLayout(id, _layout);
            }
        }

        /// <summary>Grid actions answer raw keys (error:positionOccupied); others localized text.</summary>
        static string Error(ApiResult r)
        {
            var e = r.Error ?? string.Empty;
            if (e.Length == 0)
                return Trans.Get("vr.common.error");
            return e.IndexOf(' ') < 0 ? Trans.Get(e) : e;
        }

        void Feedback(ApiResult r, string okKey, Vector3 at)
        {
            if (r.Ok)
            {
                CicCue.Ok(at);
                SetStatus(Trans.Get(okKey));
            }
            else
            {
                CicCue.Fail(at);
                SetStatus(Error(r), true);
            }
        }

        void Update()
        {
            if (!Inside)
                return;
            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 0.5f;
                _yard.Tick();
            }

            _blueprints.Tick();

            if (_armedRemove.HasValue && Time.unscaledTime > _armedUntil)
            {
                _armedRemove = null;
                PaintGrid();
            }

            // Store block in hand: preview the cell it would land on.
            if (_shelves != null && _shelves.Holding)
            {
                var cell = NearestCell(_shelves.HeldPosition);
                var h = cell.HasValue ? cell : null;
                if (h != _hover)
                {
                    _hover = h;
                    PaintGrid();
                }
            }
        }
    }
}
