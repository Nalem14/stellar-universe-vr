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
    /// rebuilt, with welding sparks, each time a module goes on. The assembly table in the room carries the
    /// 9×9 grid: pick a module on the hangar rack, a holo crate appears on the dispenser — grab it and set it
    /// on a free cell touching the structure (or point at the cell) → PlaceShipModule. Point twice at a placed
    /// module to take it off (RemoveShipModule). Ships docked at the planet are listed; a ShipCore in the
    /// hangar founds a new one (AddToFleet fleet=0). The dock lives far below the bridge; entering moves the
    /// XR origin here, leaving puts it back on deck.
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
        static readonly Vector3 Dispenser = new(1.15f, 0f, -1.75f);
        const float Cell = 0.2f;
        const float GridHeight = 0.92f;
        const int RackPerPage = 7;
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
        GameObject _crate;
        XRGrabInteractable _crateGrab;
        bool _crateHeld;
        readonly Renderer[,] _cells = new Renderer[ModuleCatalog.Grid, ModuleCatalog.Grid];
        readonly TextMeshPro[,] _cellLabels = new TextMeshPro[ModuleCatalog.Grid, ModuleCatalog.Grid];
        MaterialPropertyBlock _mpb;

        HoloScreen _shipScreen;
        HoloScreen _rackScreen;
        RectTransform _shipBody;
        RectTransform _rackBody;
        RectTransform _rackList;
        RectTransform _yardBody;
        ShipyardPanel _yard;
        /// <summary>Hangar screen tab: 0 hangar rack, 1 shipyard, 2 blueprints.</summary>
        int _tab;
        bool _yardTab => _tab == 1;
        RectTransform _bpBody;
        BlueprintPanel _blueprints;
        Blueprint _preview;
        bool[] _previewOk;
        Material _blueprintMat;
        float _nextTick;
        TMP_Text _status;
        TMP_InputField _nameField;

        int _planetId;
        int _fleetId;
        string _selectedType;
        int _rackPage;
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
        static readonly HashSet<string> Solid = new() { "Floor", "WallS", "WallE", "WallW", "Sill", "BayWindow" };

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

            // Control room: 9 × 7 m, north side open on the bay through a wide window.
            const float w = 9f, d = 7f, h = 3.4f;
            Box("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(w, 0.1f, d), deck);
            Box("Ceiling", new Vector3(0f, h, 0f), new Vector3(w, 0.1f, d), dark);
            Box("WallS", new Vector3(0f, h * 0.5f, -d * 0.5f), new Vector3(w, h, 0.12f), wall);
            Box("WallE", new Vector3(w * 0.5f, h * 0.5f, 0f), new Vector3(0.12f, h, d), wall);
            Box("WallW", new Vector3(-w * 0.5f, h * 0.5f, 0f), new Vector3(0.12f, h, d), wall);
            Box("Sill", new Vector3(0f, 0.45f, d * 0.5f), new Vector3(w, 0.9f, 0.3f), wall);
            Box("SillLight", new Vector3(0f, 0.905f, d * 0.5f - 0.16f), new Vector3(w * 0.96f, 0.02f, 0.03f), cyan);
            Box("Header", new Vector3(0f, h - 0.15f, d * 0.5f), new Vector3(w, 0.3f, 0.3f), dark);
            for (var i = -2; i <= 2; i++)
                Box("Mullion" + i, new Vector3(i * 2.1f, (0.9f + h - 0.3f) * 0.5f, d * 0.5f),
                    new Vector3(0.12f, h - 1.2f, 0.18f), dark);
            var glass = GameObject.CreatePrimitive(PrimitiveType.Quad);
            glass.name = "BayWindow";
            Destroy(glass.GetComponent<Collider>());
            glass.transform.SetParent(transform, false);
            glass.transform.localPosition = new Vector3(0f, (0.9f + h - 0.3f) * 0.5f, d * 0.5f + 0.02f);
            glass.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            glass.transform.localScale = new Vector3(w, h - 1.2f, 1f);
            glass.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture, new Color(0.6f, 0.9f, 1f, 0.015f));
            var pane = glass.AddComponent<BoxCollider>();
            pane.size = new Vector3(1f, 1f, 0.1f);

            for (var i = -1; i <= 1; i++)
                Box("RibLight" + i, new Vector3(i * 2.8f, h - 0.06f, 0f), new Vector3(0.05f, 0.03f, d * 0.9f), cyan);
            Box("FloorStrip", new Vector3(0f, 0.005f, -d * 0.5f + 0.3f), new Vector3(w * 0.9f, 0.01f, 0.05f), amber);

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

            // Module dispenser beside the table, within reach of the stand.
            Box("Dispenser", Dispenser + new Vector3(0f, 0.47f, 0f), new Vector3(0.34f, 0.94f, 0.34f), wall);
            Box("DispenserPad", Dispenser + new Vector3(0f, 0.95f, 0f), new Vector3(0.3f, 0.02f, 0.3f),
                _art.Lit(Texture2D.whiteTexture, Accent, 2.2f));

            Light("KeyOverhead", new Vector3(0f, h - 0.5f, -0.5f), new Color(0.75f, 0.95f, 1f), 1.4f, 7f);
            BuildBay(dark, cyan, amber);
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
            }
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
            for (var x = 0; x < ModuleCatalog.Grid; x++)
            for (var y = 0; y < ModuleCatalog.Grid; y++)
            {
                var onHand = false;
                var m = _preview != null ? PreviewAt(x, y, out onHand) : At(x, y);
                Color c;
                if (_preview != null)
                {
                    // Blueprint projection: green = the part is on hand, red = missing from the hangar.
                    c = m == null ? new Color(0.3f, 0.8f, 1f, 0.08f)
                        : onHand ? new Color(0.35f, 1f, 0.55f, 0.8f)
                        : new Color(1f, 0.3f, 0.25f, 0.85f);
                }
                else if (m != null)
                {
                    c = ModuleCatalog.Accent(ModuleCatalog.Family(m.Type));
                    c.a = 0.85f;
                    if (_armedRemove.HasValue && _armedRemove.Value == (x, y))
                        c = new Color(1f, 0.3f, 0.25f, 1f);
                }
                else if (_fleetId > 0 && _selectedType != null && ModuleCatalog.CanPlace(occupied, x, y))
                    c = new Color(0.4f, 1f, 0.55f, 0.65f);
                else
                    c = new Color(0.3f, 0.8f, 1f, 0.12f);

                if (_hover.HasValue && _hover.Value == (x, y))
                    c = Color.Lerp(c, Color.white, 0.35f);
                _mpb.SetColor("_Color", c);
                _mpb.SetColor("_Emission", new Color(c.r, c.g, c.b, 1f) * 0.6f);
                _cells[x, y].SetPropertyBlock(_mpb);

                var label = _cellLabels[x, y];
                if (m == null)
                {
                    if (label != null)
                        label.gameObject.SetActive(false);
                    continue;
                }

                if (label == null)
                {
                    label = UiKit.Label(_gridRoot, "Tag_" + x + "_" + y, string.Empty,
                        CellLocal(x, y) + new Vector3(0f, 0.004f, 0f), Cell * 0.92f, 0.03f, UiKit.TextBright);
                    // Flat on the cell, read from the stand (not along the turned grid).
                    label.transform.rotation = transform.rotation * Quaternion.Euler(90f, 0f, 0f);
                    _cellLabels[x, y] = label;
                }

                label.gameObject.SetActive(true);
                label.text = Short(Trans.Get(ModuleCatalog.NameKey(m.Type)));
            }
        }

        static string Short(string name) => string.IsNullOrEmpty(name) ? "?" : name.Length <= 12 ? name : name.Substring(0, 11) + "…";

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
                SetStatus(Trans.Get(ModuleCatalog.NameKey(m.Type)) + " — " + Trans.Get(ModuleCatalog.DescKey(m.Type)));
        }

        // ── Screens ───────────────────────────────────────────────────────────────

        void BuildScreens()
        {
            var eye = transform.TransformPoint(Stand + Vector3.up * WorldScale.EyeStanding);

            _shipScreen = HoloScreen.Create(transform, "DockShipScreen", new Vector2(0.95f, 0.8f),
                new Vector3(-2.2f, 1.45f, -1.3f), Quaternion.identity, Trans.Get("vr.dock.title"));
            ScreenMount.FaceViewer(_shipScreen.transform, eye, 1f, 6f);
            _shipScreen.SetAccent(Accent, 0.5f);
            _shipBody = Body(_shipScreen);
            _status = DiegeticUi.HoloLabel(_shipScreen.Content, string.Empty, new Vector2(0f, -350f),
                new Vector2(900f, 60f), 17f, DiegeticUi.CyanDim);
            _status.textWrappingMode = TextWrappingModes.Normal;

            _rackScreen = HoloScreen.Create(transform, "DockHangarRack", new Vector2(0.95f, 0.8f),
                new Vector3(2.2f, 1.45f, -0.9f), Quaternion.identity, Trans.Get("hangar"));
            ScreenMount.FaceViewer(_rackScreen.transform, eye, 1f, 6f);
            _rackScreen.SetAccent(Accent, 0.5f);
            _rackBody = Body(_rackScreen);
            // Two tabs on the hangar screen: finished modules (rack) | building new ones (shipyard).
            _rackList = Sub(_rackBody, "Rack", 0f);
            _yardBody = Sub(_rackBody, "Yard", -45f);
            _yard = new ShipyardPanel(_yardBody, _eco, () => _planetId, (t, e) => SetStatus(t, e), () =>
            {
                RenderRack();
                RenderShipScreen();
            });
            _bpBody = Sub(_rackBody, "Blueprints", -45f);
            _blueprints = new BlueprintPanel(_bpBody, () => _fleetId, Stock, (t, e) => SetStatus(t, e), Preview,
                async () =>
                {
                    await AfterEdit();
                    RenderAll();
                    RefreshCrate();
                });
            _rackTabs = new[]
            {
                DiegeticUi.HoloButton(_rackScreen.Content, Trans.Get("hangar"), new Vector2(-300f, 262f),
                    new Vector2(280f, 44f), () => SetTab(0), DiegeticUi.BtnStyle.Cyan),
                DiegeticUi.HoloButton(_rackScreen.Content, Trans.Get("shipyard"), new Vector2(0f, 262f),
                    new Vector2(280f, 44f), () => SetTab(1), DiegeticUi.BtnStyle.Ghost),
                DiegeticUi.HoloButton(_rackScreen.Content, Trans.Get("shipTemplates"), new Vector2(300f, 262f),
                    new Vector2(280f, 44f), () => SetTab(2), DiegeticUi.BtnStyle.Ghost)
            };

            // Way back: the door in the aft wall, behind the stand (walk through it or use its panel).
            RoomDoor.Build(transform, "DoorToBridge", new Vector3(0f, 0f, -3.44f), 0f, Trans.Get("vr.dock.leave"),
                UiKit.Amber, _art, () => Inside, () => AsyncTap.Run(Leave()));
        }

        UnityEngine.UI.Button[] _rackTabs;

        static RectTransform Sub(RectTransform parent, string name, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = parent.sizeDelta;
            rt.anchoredPosition = new Vector2(0f, y);
            return rt;
        }

        void SetTab(int tab)
        {
            _tab = tab;
            // Leaving the blueprints tab drops the projection: the cradle shows the real ship again.
            if (tab != 2 && _preview != null)
                _blueprints.Deselect();
            for (var i = 0; i < _rackTabs.Length; i++)
            {
                var on = i == tab;
                var label = _rackTabs[i].GetComponentInChildren<TMP_Text>();
                label.color = on ? UiKit.Cyan : new Color(0.7f, 0.85f, 0.92f, 0.8f);
                _rackTabs[i].GetComponent<Image>().color = on ? new Color(0.6f, 1f, 1f, 1f) : new Color(1f, 1f, 1f, 0.55f);
            }

            RenderRack();
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
                Btn(_shipBody, name + "  #" + f.Id, -225f + (i % 2) * 450f, y - (i / 2) * 58f, 430f, 50f,
                    () => SelectFleet(id), id == _fleetId ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            }

            y -= Mathf.CeilToInt(Mathf.Min(docked.Count, 4) / 2f) * 58f;
            var core = HangarRow(ModuleCatalog.Core);
            var newShip = Btn(_shipBody, Trans.Get("vr.dock.newShip"), -225f, y, 430f, 50f,
                () => AsyncTap.Run(NewShip()), core != null ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost);
            newShip.interactable = core != null;
            if (core == null)
                Text(_shipBody, Trans.Get("vr.dock.needCore"), 20f, y, 440f, 15f, DiegeticUi.CyanDim);
            y -= 70f;

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
            Btn(_shipBody, Trans.Get("rename"), 300f, y, 220f, 48f, () => AsyncTap.Run(Rename()), DiegeticUi.BtnStyle.Ghost);
            y -= 70f;

            var s = ModuleCatalog.Sum(_layout);
            var rows = new (string, string)[]
            {
                (Trans.Get("modules"), s.Modules.ToString()),
                (Trans.Get("armor"), Mathf.RoundToInt(s.Armor).ToString()),
                (Trans.Get("shield"), Mathf.RoundToInt(s.Shield).ToString()),
                (Trans.Get("damage"), Mathf.RoundToInt(s.Damage).ToString()),
                (Trans.Get("speed"), Mathf.RoundToInt(s.Speed).ToString()),
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
            if (s.Hyperdrives > 0 || s.PrlBonds > 0)
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

        void RenderRack()
        {
            Clear(_rackList);
            Clear(_yardBody);
            _rackList.gameObject.SetActive(_tab == 0);
            _yardBody.gameObject.SetActive(_tab == 1);
            _bpBody.gameObject.SetActive(_tab == 2);
            if (_tab == 1)
            {
                _yard.Render();
                return;
            }

            if (_tab == 2)
            {
                _blueprints.Render();
                return;
            }

            var groups = HangarGroups();
            if (groups.Count == 0)
            {
                Text(_rackList, Trans.Get("vr.dock.hangarEmpty"), 0f, 60f, 860f, 20f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                return;
            }

            var pages = Mathf.Max(1, Mathf.CeilToInt(groups.Count / (float)RackPerPage));
            _rackPage = Mathf.Clamp(_rackPage, 0, pages - 1);
            var first = _rackPage * RackPerPage;
            for (var i = first; i < groups.Count && i < first + RackPerPage; i++)
            {
                var (type, count) = groups[i];
                var y = 200f - (i - first) * 64f;
                var fam = ModuleCatalog.Family(type);
                var chip = new GameObject("Chip", typeof(RectTransform), typeof(Image));
                chip.transform.SetParent(_rackList, false);
                var crt = chip.GetComponent<RectTransform>();
                crt.sizeDelta = new Vector2(10f, 52f);
                crt.anchoredPosition = new Vector2(-438f, y);
                chip.GetComponent<Image>().color = ModuleCatalog.Accent(fam);
                chip.GetComponent<Image>().raycastTarget = false;

                var t = type;
                var selected = _selectedType == type;
                Btn(_rackList, Trans.Get(ModuleCatalog.NameKey(type)) + "  ×" + count, -115f, y, 620f, 56f,
                    () => SelectModule(t), selected ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
                if (type != ModuleCatalog.Core || count > 0)
                    Btn(_rackList, "×", 380f, y, 70f, 56f, () => AsyncTap.Run(Scrap(t)), DiegeticUi.BtnStyle.Danger);
            }

            if (pages > 1)
            {
                Btn(_rackList, "‹", -80f, -300f, 70f, 46f, () => { _rackPage = (_rackPage - 1 + pages) % pages; RenderRack(); },
                    DiegeticUi.BtnStyle.Ghost);
                Text(_rackList, (_rackPage + 1) + " / " + pages, 0f, -300f, 90f, 18f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                Btn(_rackList, "›", 80f, -300f, 70f, 46f, () => { _rackPage = (_rackPage + 1) % pages; RenderRack(); },
                    DiegeticUi.BtnStyle.Ghost);
            }

            if (_selectedType != null)
                Text(_rackList, Trans.Format("vr.dock.placing", Trans.Get(_selectedType)), 0f, -355f, 880f, 16f,
                    UiKit.Ok, TextAlignmentOptions.Center);
        }

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
                if (m.Type != ModuleCatalog.Core)
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
            _blueprints?.Deselect();
            if (_crate != null)
                Destroy(_crate);
            _crate = null;
            _crateHeld = false;
            // Out into the corridor, in front of this room's door.
            CorridorRoom.ReturnPlayer(CorridorRoom.Slot.DockStarboard);

            gameObject.SetActive(false);
            await fade.FadeIn();
        }


        void SelectFleet(int id) => AsyncTap.Run(LoadFleet(id));

        void SelectModule(string type)
        {
            _selectedType = _selectedType == type ? null : type;
            _armedRemove = null;
            RenderRack();
            PaintGrid();
            RefreshCrate();
            if (_selectedType != null)
                SetStatus(Trans.Get(_selectedType) + " — " + Trans.Get(ModuleCatalog.DescKey(_selectedType)));
        }

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
            RenderRack();
            PaintGrid();
            RebuildHull();
        }

        // ── Holo crate (manual placement) ─────────────────────────────────────────

        /// <summary>The selected module as a grabbable crate on the dispenser (none when nothing is selected).</summary>
        void RefreshCrate()
        {
            if (_crateHeld)
                return;
            if (_selectedType == null || _fleetId <= 0 || HangarRow(_selectedType) == null)
            {
                if (_crate != null)
                    Destroy(_crate);
                _crate = null;
                return;
            }

            if (_crate == null)
            {
                _crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _crate.name = "ModuleCrate";
                _crate.transform.SetParent(transform, false);
                _crate.transform.localScale = Vector3.one * 0.14f;
                var body = _crate.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                _crateGrab = _crate.AddComponent<XRGrabInteractable>();
                _crateGrab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                _crateGrab.throwOnDetach = false;
                _crateGrab.useDynamicAttach = true;
                _crateGrab.selectEntered.AddListener(_ => _crateHeld = true);
                _crateGrab.selectExited.AddListener(_ => OnCrateReleased());
                // Crate-local units (the crate is 0.14 m): a label across its lid.
                var tag = UiKit.Label(_crate.transform, "Tag", string.Empty, new Vector3(0f, 0.51f, 0f),
                    0.95f, 0.22f, UiKit.TextBright);
                tag.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tag.name = "Tag";
            }

            var accent = ModuleCatalog.Accent(ModuleCatalog.Family(_selectedType));
            _crate.GetComponent<MeshRenderer>().sharedMaterial = _art.Lit(Texture2D.whiteTexture, accent, 1.4f);
            var label = _crate.transform.Find("Tag")?.GetComponent<TextMeshPro>();
            if (label != null)
                label.text = Short(Trans.Get(_selectedType));
            _crate.transform.localPosition = Dispenser + new Vector3(0f, 1.05f, 0f);
            _crate.transform.localRotation = Quaternion.identity;
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

        void OnCrateReleased()
        {
            _crateHeld = false;
            var cell = _crate != null ? NearestCell(_crate.transform.position) : null;
            _hover = null;
            if (cell.HasValue && At(cell.Value.x, cell.Value.y) == null)
                OnCell(cell.Value.x, cell.Value.y);
            // Back on the dispenser (or gone if that was the last one): refreshed after the order lands.
            RefreshCrate();
            PaintGrid();
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
                if (m.Type == ModuleCatalog.Core)
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
                SetStatus(Trans.Get("vr.dock.pickModule"));
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
                RenderRack();
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
            if (_selectedType != null && HangarRow(_selectedType) == null)
                _selectedType = null;
            RenderAll();
            if (_pendingWeld.HasValue)
                Weld(_pendingWeld.Value.x, _pendingWeld.Value.y);
            _pendingWeld = null;
            RefreshCrate();
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

        async Task NewShip()
        {
            var core = HangarRow(ModuleCatalog.Core);
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
                Feedback(r, "fleetCreated", transform.position + Vector3.up * GridHeight);
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

        /// <summary>Scrap a hangar module (DelShip): two taps within 4 s.</summary>
        string _armedScrap;
        float _scrapUntil;

        async Task Scrap(string type)
        {
            if (_armedScrap != type || Time.unscaledTime > _scrapUntil)
            {
                _armedScrap = type;
                _scrapUntil = Time.unscaledTime + 4f;
                SetStatus(Trans.Format("vr.dock.confirmScrap", Trans.Get(type)), true);
                return;
            }

            _armedScrap = null;
            var row = HangarRow(type);
            if (row == null)
                return;
            _busy = true;
            try
            {
                var r = await ActionJs.Get("DelShip", new Dictionary<string, string> { { "ship", FocusContext.AsString(row["id"]) } });
                Feedback(r, "vr.dock.scrapped", _rackScreen.transform.position);
            }
            finally
            {
                _busy = false;
            }

            await _eco.RefreshNow();
            if (_selectedType == type && HangarRow(type) == null)
                _selectedType = null;
            RenderRack();
            PaintGrid();
        }

        async Task Rename()
        {
            var name = (_nameField != null ? _nameField.text : string.Empty).Trim();
            if (name.Length == 0 || _fleetId <= 0)
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
            if (_tab == 1 && Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 0.5f;
                _yard.Tick();
            }

            if (_tab == 2)
                _blueprints.Tick();

            if (_armedRemove.HasValue && Time.unscaledTime > _armedUntil)
            {
                _armedRemove = null;
                PaintGrid();
            }

            // Held crate: preview the cell it would land on.
            if (_crateHeld && _crate != null)
            {
                var cell = NearestCell(_crate.transform.position);
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
