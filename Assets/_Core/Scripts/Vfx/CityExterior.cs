using System.Collections.Generic;
using Core.App;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>
    /// The planet view: one of our cities, seen from its citadel. The command rotunda crowns a stepped tower
    /// <see cref="WorldScale.CityTowerHeight"/> above the ground, ringed by the concourse crown
    /// (<see cref="StationExterior"/> in citadel style); round its foot the city falls away in rings of ever
    /// lower buildings to its walls, then the planet's land runs to the mountains (or, on a gas giant, the city
    /// rides a platform over a sea of cloud). Everything is read from the game: the planet's kind and its real
    /// buildings choose the districts (mines → industry, shipyard → spaceport, lab → domes, farms → terraces,
    /// defence factory → walls and batteries, shield → the dome, gates → their rings), its levels the size;
    /// our orbital fortresses cross its sky; a planetary siege lights the batteries and the shield.
    /// <para>
    /// Built procedurally and deterministically (seed = planet id) into a handful of meshes on shared
    /// materials — about 15 draw calls, no realtime shadows, no per-frame allocation. The scene stands
    /// <see cref="WorldScale.CityDepth"/> below the system's (BridgeViewRig.CityAnchor), past every far clip, so
    /// space never shows through it.
    /// </para>
    /// </summary>
    public sealed class CityExterior : MonoBehaviour
    {
        public static readonly Color CitadelGold = new(1f, 0.78f, 0.42f, 1f);

        /// <summary>The city on show (null when none): wall displays and the holo table read its plan.</summary>
        public static CityExterior Current { get; private set; }

        public int RingCount { get; private set; } = 4;

        /// <summary>Verification hook (AgentScripts/CityViewVerify): pin the hour (0..1 of a day), null = the clock.</summary>
        public static float? PinnedHour;
        public int PlanetId => _planetId;

        static readonly int SunDirId = Shader.PropertyToID("_SUCitySunDir");
        static readonly int SunId = Shader.PropertyToID("_SUCitySun");
        static readonly int AmbientId = Shader.PropertyToID("_SUCityAmbient");
        static readonly int BounceId = Shader.PropertyToID("_SUCityBounce");
        static readonly int FogId = Shader.PropertyToID("_SUCityFog");
        static readonly int NightId = Shader.PropertyToID("_SUCityNight");
        static readonly int CentreId = Shader.PropertyToID("_SUCityCentre");

        /// <summary>A full day on a city (s): long enough that a session sees one dusk at most.</summary>
        const float DayLength = 2400f;

        FocusContext _focus;
        StationExterior _crown;
        Transform _built;
        readonly List<Object> _owned = new();
        int _planetId;
        string _sig;
        bool _shown;
        CityKit.Palette _pal;
        SystemBodyKit.PlanetKind _kind;
        Color _starColor = Color.white;
        float _cameraFar = -1f;

        Transform _sky;
        GameObject _dome;
        Material _domeMat;
        float _shieldStrength;
        float _hit;

        // Districts read back by the FX.
        readonly List<Vector3> _turrets = new();
        readonly List<Vector3> _pads = new();
        Vector3 _centre;

        public static CityExterior Build(Transform ride, FocusContext focus)
        {
            var go = new GameObject("CityExterior");
            go.transform.SetParent(ride, false);
            var city = go.AddComponent<CityExterior>();
            city._focus = focus;
            city._crown = StationExterior.Build(go.transform, citadel: true);
            go.SetActive(false);
            CombatEvents.Shot += city.OnShot;
            CombatEvents.Hit += city.OnHit;
            CombatEvents.Bombard += city.OnBombard;
            if (EconomyService.Instance != null)
                EconomyService.Instance.Changed += city.OnEconomy;
            if (focus != null)
                focus.FleetsChanged += city.OnFleets;
            return city;
        }

        void OnDestroy()
        {
            CombatEvents.Shot -= OnShot;
            CombatEvents.Hit -= OnHit;
            CombatEvents.Bombard -= OnBombard;
            if (EconomyService.Instance != null)
                EconomyService.Instance.Changed -= OnEconomy;
            if (_focus != null)
                _focus.FleetsChanged -= OnFleets;
            RestoreCamera();
            Release();
            if (Current == this)
                Current = null;
        }

        // ── Show / hide ────────────────────────────────────────────────────────

        public void Show(int planetId)
        {
            _planetId = planetId;
            gameObject.SetActive(true);
            _shown = true;
            Current = this;
            Rebuild(force: false);
            ApplyCamera();
            Tick(true);
        }

        /// <summary>Re-read the light now (after <see cref="PinnedHour"/> changed, or a jump in time).</summary>
        public void Relight() => Tick(true);

        public void Hide()
        {
            _shown = false;
            RestoreCamera();
            if (Current == this)
                Current = null;
            gameObject.SetActive(false);
        }

        void ApplyCamera()
        {
            var cam = Camera.main;
            if (cam == null)
                return;
            if (_cameraFar < 0f)
                _cameraFar = cam.farClipPlane;
            cam.farClipPlane = WorldScale.CityFarClip;
        }

        void RestoreCamera()
        {
            if (_cameraFar < 0f)
                return;
            var cam = Camera.main;
            if (cam != null)
                cam.farClipPlane = _cameraFar;
            _cameraFar = -1f;
        }

        bool _builtWithLevels;
        bool _pendingRebuild;

        void OnEconomy()
        {
            if (!_shown)
                return;
            // The levels first arrived (boot, behind its fade): build now. A building finished while we watch:
            // the city grows at the next fade (rebuilding costs a few hundred ms on the headset — never in view).
            if (!_builtWithLevels)
                Rebuild(force: false);
            else if (Signature(_planetId, ReadLevels()) != _sig)
                _pendingRebuild = true;
        }

        void OnFleets()
        {
            if (_shown)
                SyncSkyStations();
        }

        // ── What the city is made of ───────────────────────────────────────────

        struct Levels
        {
            public int Home, Farm, MineralMine, CrystalMine, Warehouses, Solar, Nuclear, Lab, Shipyard, Academy,
                Defense, DefenseUnits, Gate, Jumpgate, Shield;

            public int Total => Home + Farm + MineralMine + CrystalMine + Warehouses + Solar + Nuclear + Lab +
                                Shipyard + Academy + Defense + Gate + Jumpgate;
        }

        Levels ReadLevels()
        {
            var l = new Levels();
            var eco = EconomyService.Instance;
            if (eco == null || !eco.TryGet(_planetId, out var p))
                return l;
            l.Home = p.Level("home");
            l.Farm = p.Level("farm");
            l.MineralMine = p.Level("mineralMine");
            l.CrystalMine = p.Level("crystalMine");
            l.Warehouses = p.Level("mineralWarehouse") + p.Level("crystalWarehouse") + p.Level("biomassWarehouse");
            l.Solar = p.Level("solarPlant");
            l.Nuclear = p.Level("nuclearPlant");
            l.Lab = p.Level("researchLab");
            l.Shipyard = p.Level("orbitShipyard");
            l.Academy = p.Level("academy");
            l.Defense = p.Level("defenseFactory");
            l.DefenseUnits = p.Defense;
            l.Gate = p.Level("stargate");
            l.Jumpgate = p.Level("jumpgate");
            l.Shield = p.Level("shield");
            return l;
        }

        static string Signature(int planet, Levels l) =>
            planet + ":" + l.Home + "," + l.Farm + "," + l.MineralMine + "," + l.CrystalMine + "," + l.Warehouses + "," +
            l.Solar + "," + l.Nuclear + "," + l.Lab + "," + l.Shipyard + "," + l.Academy + "," + l.Defense + "," +
            Mathf.Min(l.DefenseUnits, 40) / 5 + "," + l.Gate + "," + l.Jumpgate + "," + l.Shield;

        void Rebuild(bool force)
        {
            var levels = ReadLevels();
            var sig = Signature(_planetId, levels);
            _pendingRebuild = false;
            if (!force && sig == _sig && _built != null)
                return;
            _sig = sig;
            _builtWithLevels = EconomyService.Instance != null && EconomyService.Instance.TryGet(_planetId, out _);
            Release();

            var planet = _focus?.FindPlanet(_planetId);
            _kind = SystemBodyKit.ClassifyPlanet(planet?.Slot ?? 3, _planetId, planet?.Habitability ?? 0);
            _pal = CityKit.For(_kind);
            if (_focus != null)
                _starColor = SystemBodyKit.Star(_focus.SystemTypeKey, _focus.SystemType).Color;

            var root = new GameObject("City");
            root.transform.SetParent(transform, false);
            _built = root.transform;
            var ground = -WorldScale.CityTowerHeight;
            _centre = new Vector3(0f, ground, WorldScale.CicTableCenterZ);

            var rng = new System.Random(_planetId * 7919 + 17);
            BuildTower(levels);
            BuildLand(rng);
            BuildDistricts(levels, rng);
            BuildSky();
            BuildDome(levels);
            BuildTraffic(rng);
            SyncSkyStations();
        }

        void Release()
        {
            foreach (var o in _owned)
                if (o != null)
                    Drop(o);
            _owned.Clear();
            if (_built != null)
                Drop(_built.gameObject);
            _built = null;
            _sky = null;
            _dome = null;
            _traffic = null;
            _turrets.Clear();
            _pads.Clear();
            _skyStations.Clear();
            _stationSig = null;
        }

        static void Drop(Object o)
        {
            if (Application.isPlaying)
                Destroy(o);
            else
                DestroyImmediate(o);
        }

        GameObject Part(string name, Mesh mesh, Material mat)
        {
            _owned.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(_built, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return go;
        }

        static Color32 C(Color c) => c;

        // ── The citadel's tower ────────────────────────────────────────────────

        void BuildTower(Levels levels)
        {
            var g = -WorldScale.CityTowerHeight;
            // Tiered shaft from a flared foot to the crown (StationExterior.CrownFoot): each tier steps in.
            Vector2[] shaft =
            {
                new(54f, g), new(52f, g + 3f), new(42f, g + 9f), new(40f, g + 34f), new(34f, g + 38f), new(32f, g + 74f),
                new(27f, g + 78f), new(25f, g + 122f), new(20f, g + 126f), new(18f, -72f), new(14f, -68f), new(12.5f, -36f),
                new(10f, -32f), new(8.8f, -15f), new(7.6f, StationExterior.CrownFoot)
            };
            var axis = new Vector3(0f, 0f, WorldScale.CicTableCenterZ);
            var body = new LatheMesh(axis) { Step = 7.5f };
            int[] creases = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 };
            for (var k = 0; k < creases.Length - 1; k++)
                body.Revolve(new[] { shaft[creases[k]], shaft[creases[k + 1]] }, 0f, 360f, false);
            Part("TowerShaft", body.ToMesh("SU_CitadelShaft"), StationExterior.CitadelHull());

            // Gold light on every ledge, and eight vertical light lines climbing the shaft.
            var light = new LatheMesh(axis) { Step = 5f };
            foreach (var i in new[] { 3, 5, 7, 9, 11, 13 })
            {
                var p = shaft[i];
                light.Revolve(new[] { new Vector2(p.x + 0.12f, p.y - 0.9f), new Vector2(p.x + 0.12f, p.y - 0.3f) }, 0f, 360f, false);
            }

            for (var k = 0; k < 8; k++)
            {
                var a = k * 45f + 22.5f;
                for (var i = 0; i < shaft.Length - 1; i++)
                {
                    var p0 = shaft[i];
                    var p1 = shaft[i + 1];
                    if (Mathf.Abs(p0.x - p1.x) > 3f)
                        continue; // ledges: no line across them
                    light.Revolve(new[] { new Vector2(p0.x + 0.1f, p0.y + 0.6f), new Vector2(p1.x + 0.1f, p1.y - 1.2f) }, a - 0.35f, a + 0.35f, false);
                }
            }

            Part("TowerLights", light.ToMesh("SU_CitadelLights"), StationExterior.CitadelGlass());

            // Flying buttresses carrying the concourse wing (272°, between two bays) down to the shaft.
            var m = new CityMesh();
            var stone = C(_pal.FacadeA * 0.82f);
            foreach (var deg in new[] { 262f, 272f, 282f })
            {
                var d = LatheMesh.Dir(deg);
                var top = axis + d * (WorldScale.StationRingRadius - 3f) + Vector3.up * (-WorldScale.StationRingDrop - 3.6f);
                var foot = axis + d * 13.5f + Vector3.up * -62f;
                m.Beam(top, foot, deg == 272f ? 4f : 2.6f, stone);
                m.Beam(top + Vector3.up * 0.3f, foot + Vector3.up * 0.3f, 0.5f, C(CitadelGold), band: true);
            }

            // The home world's crown: a tall gold spire on the dome.
            var spire = levels.Home > 0 ? 34f + levels.Home * 2f : 22f;
            m.Prism(axis + Vector3.up * 21f, 0.9f, 0.08f, spire, 6, 0f, C(CitadelGold), 0f, 0f, band: true, cap: false);
            Part("TowerTrim", m.ToMesh("SU_CitadelTrim"), CityKit.Blocks());
        }

        // ── The land ───────────────────────────────────────────────────────────

        void BuildLand(System.Random rng)
        {
            var g = -WorldScale.CityTowerHeight;
            var seaSide = (float)rng.NextDouble() * Mathf.PI * 2f;
            var noiseX = (float)rng.NextDouble() * 200f;
            var noiseY = (float)rng.NextDouble() * 200f;
            if (_pal.Gas)
            {
                // The city rides a platform; the cloud sea lies 120 m under it.
                var plat = new CityMesh();
                plat.Prism(_centre + Vector3.down * 26f, WorldScale.CityRadius + 70f, WorldScale.CityRadius + 82f, 26f, 96, 0f,
                    C(_pal.FacadeB * 0.7f), 0.3f, 0.2f);
                plat.Annulus(_centre + Vector3.up * 0.2f, WorldScale.CityRadius + 70f, WorldScale.CityRadius + 76f, 96,
                    C(CitadelGold * 0.9f), true);
                plat.Annulus(_centre + Vector3.up * 0.05f, 0f, WorldScale.CityRadius + 82f, 96, C(_pal.Paving), false);
                Part("Platform", plat.ToMesh("SU_CityPlatform"), CityKit.Blocks());
            }

            // Polar grid round the city: flat where it is built, rising to hills, then the mountain ring.
            var radii = new List<float> { 0f, 30f, WorldScale.CityPlazaRadius };
            for (var r = 110f; r < WorldScale.CityRadius + 60f; r += 45f)
                radii.Add(r);
            for (var r = WorldScale.CityRadius + 60f; r <= WorldScale.CityLandEdge; r += 38f)
                radii.Add(r);
            const int seg = 120;
            var verts = new List<Vector3>(radii.Count * (seg + 1));
            var cols = new List<Color32>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(radii.Count * seg * 6);
            var baseY = _pal.Gas ? g - 120f : g;
            foreach (var r in radii)
            {
                for (var i = 0; i <= seg; i++)
                {
                    var a = i * Mathf.PI * 2f / seg;
                    var h = _pal.Gas ? CloudHeight(r, a, noiseX, noiseY) : LandHeight(r, a, seaSide, noiseX, noiseY);
                    var p = new Vector3(_centre.x + Mathf.Sin(a) * r, baseY + h, _centre.z + Mathf.Cos(a) * r);
                    verts.Add(p);
                    uvs.Add(new Vector2(p.x, p.z));
                    cols.Add(LandColour(r, h));
                }
            }

            for (var k = 0; k < radii.Count - 1; k++)
            for (var i = 0; i < seg; i++)
            {
                var a = k * (seg + 1) + i;
                var b = a + seg + 1;
                tris.Add(a);
                tris.Add(b);
                tris.Add(a + 1);
                tris.Add(a + 1);
                tris.Add(b);
                tris.Add(b + 1);
            }

            var mesh = new Mesh { name = "SU_CityLand" };
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // The winding above faces down for this layout: keep the land's face toward the sky.
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0f)
            {
                for (var i = 0; i < tris.Count; i += 3)
                    (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
            }

            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            Part("Land", mesh, CityKit.Land(_pal.Gas));

            if (_pal.Sea && !_pal.Gas)
            {
                // The sea fills the low side of the land out to the horizon.
                var water = new CityMesh();
                water.Annulus(new Vector3(_centre.x, g - 2.5f, _centre.z), WorldScale.CityRadius + 40f, WorldScale.CityLandEdge + 40f,
                    96, new Color32(255, 255, 255, 255), false);
                var wm = water.ToMesh("SU_CitySea");
                Part("Sea", wm, CityKit.Water());
            }

            float LandHeight(float r, float a, float sea, float nx, float ny)
            {
                var flat = WorldScale.CityRadius + 50f;
                if (r <= flat)
                    return 0f;
                var hills = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flat, WorldScale.CityHorizon - 120f, r));
                var x = Mathf.Sin(a) * r * 0.004f + nx;
                var z = Mathf.Cos(a) * r * 0.004f + ny;
                var h = hills * (Mathf.PerlinNoise(x, z) * 34f - 6f);
                var ridge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WorldScale.CityHorizon - 200f, WorldScale.CityHorizon, r));
                var rx = Mathf.Sin(a) * 2.6f + nx;
                var rz = Mathf.Cos(a) * 2.6f + ny;
                var ridged = 1f - Mathf.Abs(Mathf.PerlinNoise(rx, rz) * 2f - 1f);
                var peaks = ridged * ridged * _pal.Peaks * (0.55f + 0.45f * Mathf.PerlinNoise(rx * 3.1f, rz * 3.1f));
                h += ridge * peaks;
                if (_pal.Sea)
                {
                    // One side of the horizon opens on the sea.
                    var toward = Mathf.Cos(a - sea);
                    var open = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, toward));
                    h = Mathf.Lerp(h, -12f, open * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flat, flat + 140f, r)));
                }

                return h;
            }

            float CloudHeight(float r, float a, float nx, float ny)
            {
                // Cloud sea: slow swell, and great cloud towers on the horizon.
                var x = Mathf.Sin(a) * r * 0.003f + nx;
                var z = Mathf.Cos(a) * r * 0.003f + ny;
                var swell = Mathf.PerlinNoise(x, z) * 20f;
                var far = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(WorldScale.CityHorizon - 250f, WorldScale.CityHorizon + 100f, r));
                var tower = Mathf.PerlinNoise(Mathf.Sin(a) * 3.4f + nx, Mathf.Cos(a) * 3.4f + ny);
                return swell + far * tower * tower * _pal.Peaks * 1.4f;
            }
        }

        Color32 LandColour(float r, float h)
        {
            if (_pal.Gas)
                return Color.Lerp(_pal.GroundLow, _pal.Snow, Mathf.Clamp01(h / (_pal.Peaks * 1.2f)));
            if (r <= WorldScale.CityRadius + 30f)
                return _pal.Paving;
            if (h < -3f)
                return _pal.Rock * 0.8f;
            var k = Mathf.Clamp01(h / Mathf.Max(1f, _pal.Peaks));
            var c = Color.Lerp(_pal.GroundLow, _pal.GroundHigh, Mathf.Clamp01(h / 30f));
            c = Color.Lerp(c, _pal.Rock, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, k)));
            c = Color.Lerp(c, _pal.Snow, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.9f, k)));
            return c;
        }

        // ── The city ───────────────────────────────────────────────────────────

        /// <summary>The eight boulevards (degrees) — between the rotunda's bays' centres, so they run out from it.</summary>
        static readonly float[] Boulevards = { 22.5f, 67.5f, 112.5f, 157.5f, 202.5f, 247.5f, 292.5f, 337.5f };

        enum District { Admin, Residential, Research, Spaceport, Industry, Crystal, Agri, Military }

        void BuildDistricts(Levels lv, System.Random rng)
        {
            var m = new CityMesh();
            var g = _centre;
            RingCount = Mathf.Clamp(3 + lv.Total / 18, 3, 6);
            var inner = WorldScale.CityPlazaRadius;
            var outer = WorldScale.CityRadius;
            var edges = new float[RingCount + 1];
            for (var i = 0; i <= RingCount; i++)
                edges[i] = inner + (outer - inner) * Mathf.Pow(i / (float)RingCount, 0.85f);
            // The citadel must dominate: the tallest blocks stay well under its crown, falling off fast.
            var hMax = 118f + Mathf.Min(lv.Home, 10) * 3f;
            var gold = C(CitadelGold);
            var road = C(new Color(1f, 0.74f, 0.42f) * 0.9f);
            var cool = C(new Color(0.45f, 0.8f, 1f));

            // Plaza round the tower's foot, its edge lit; ring roads between the districts.
            m.Annulus(g + Vector3.up * 0.25f, inner - 6f, inner - 3.5f, 96, gold, true);
            for (var i = 1; i < RingCount; i++)
            {
                m.Annulus(g + Vector3.up * 0.25f, edges[i] - 5.4f, edges[i] - 4.6f, 128, road, true);
                m.Annulus(g + Vector3.up * 0.25f, edges[i] + 4.6f, edges[i] + 5.4f, 128, road, true);
            }

            // Boulevards: two lines of light each, from the plaza to the walls.
            foreach (var deg in Boulevards)
            {
                var d = LatheMesh.Dir(deg);
                var side = Vector3.Cross(Vector3.up, d) * 7f;
                m.Strip(g + d * inner + side + Vector3.up * 0.3f, g + d * outer + side + Vector3.up * 0.3f, 1.1f, road);
                m.Strip(g + d * inner - side + Vector3.up * 0.3f, g + d * outer - side + Vector3.up * 0.3f, 1.1f, road);
            }

            // Lots, ring by ring, row by row.
            for (var i = 0; i < RingCount; i++)
            {
                var depth = edges[i + 1] - edges[i] - 12f;
                var rows = Mathf.Max(1, Mathf.RoundToInt(depth / (30f + i * 8f)));
                for (var row = 0; row < rows; row++)
                {
                    var rr = edges[i] + 6f + depth * (row + 0.5f) / rows;
                    var lot = 24f + i * 6f;
                    var count = Mathf.Max(8, Mathf.FloorToInt(Mathf.PI * 2f * rr / lot));
                    for (var j = 0; j < count; j++)
                    {
                        var deg = (j + 0.5f) * 360f / count + ((float)rng.NextDouble() - 0.5f) * 4f;
                        if (OnBoulevard(deg, rr))
                            continue;
                        var district = DistrictAt(lv, i, deg);
                        var foot = g + LatheMesh.Dir(deg) * rr;
                        var t = (rr - inner) / (outer - inner + 60f);
                        // The first ring rises close under the citadel's bays (the towers round its foot), then the
                        // city falls away fast toward the walls.
                        var top = i == 0 ? hMax * 1.55f : hMax;
                        var h = top * Mathf.Pow(1f - t, 2.3f) * Mathf.Lerp(0.6f, 1.2f, (float)rng.NextDouble());
                        Lot(m, district, foot, deg, lot, depth / rows, Mathf.Max(9f, h), rng, lv);
                    }
                }
            }

            BuildWalls(m, lv, rng);
            BuildOutskirts(m, lv, rng);
            Part("Districts", m.ToMesh("SU_CityDistricts"), CityKit.Blocks());
        }

        static bool OnBoulevard(float deg, float r)
        {
            foreach (var b in Boulevards)
            {
                var dd = Mathf.Abs(Mathf.DeltaAngle(deg, b)) * Mathf.Deg2Rad * r;
                if (dd < 16f)
                    return true;
            }

            return false;
        }

        /// <summary>Which district a lot belongs to: wedges between boulevards, by the planet's real buildings.</summary>
        District DistrictAt(Levels lv, int ring, float deg)
        {
            var wedge = Mathf.FloorToInt(Mathf.Repeat(deg - 22.5f, 360f) / 45f);
            var outerRing = ring >= RingCount - 2;
            if (ring == 0)
                return lv.Lab > 0 && wedge == 2 ? District.Research : District.Admin;
            if (lv.Shipyard > 0 && wedge == 0 && ring >= 2)
                return District.Spaceport;
            if (lv.Lab > 0 && wedge == 2 && ring <= 2)
                return District.Research;
            if (outerRing && lv.MineralMine > 0 && (wedge == 4 || wedge == 5))
                return District.Industry;
            if (outerRing && lv.CrystalMine > 0 && wedge == 3)
                return District.Crystal;
            if (outerRing && lv.Farm > 0 && (wedge == 6 || wedge == 7))
                return District.Agri;
            if (lv.Academy > 0 && wedge == 1 && ring >= 2)
                return District.Military;
            return ring <= 1 ? District.Admin : District.Residential;
        }

        void Lot(CityMesh m, District district, Vector3 foot, float deg, float lot, float depth, float h, System.Random rng, Levels lv)
        {
            var seed = (float)rng.NextDouble();
            var w = lot * Mathf.Lerp(0.42f, 0.74f, (float)rng.NextDouble());
            var d = depth * Mathf.Lerp(0.42f, 0.74f, (float)rng.NextDouble());
            // Three skins: the world's stone, dark mirrored glass, brushed metal — so no two blocks read the same.
            var skin = rng.NextDouble();
            var facade = skin < 0.5 ? Color.Lerp(_pal.FacadeA, _pal.FacadeB, (float)rng.NextDouble())
                : skin < 0.8 ? Color.Lerp(new Color(0.16f, 0.2f, 0.26f), new Color(0.22f, 0.28f, 0.34f), (float)rng.NextDouble())
                : Color.Lerp(new Color(0.55f, 0.58f, 0.62f), _pal.FacadeB, 0.35f);
            switch (district)
            {
                case District.Admin:
                {
                    // Stepped towers with gold crowns: the heart of the city.
                    var col = C(Color.Lerp(facade, Color.white, 0.25f));
                    var tiers = h > 70f ? 3 : 2;
                    var y = 0f;
                    var tw = w;
                    var td = d;
                    for (var k = 0; k < tiers; k++)
                    {
                        var th = h * (k == 0 ? 0.55f : 0.45f / (tiers - 1));
                        m.Box(foot + Vector3.up * y, tw, td, th, deg, col, seed + k * 0.13f, 0.62f);
                        y += th;
                        tw *= 0.72f;
                        td *= 0.72f;
                        m.Box(foot + Vector3.up * (y - 0.6f), tw * 1.25f, td * 1.25f, 0.6f, deg, C(CitadelGold), 0f, 0f, band: true);
                    }

                    if (h > 90f)
                        Beacon(m, foot + Vector3.up * y);
                    break;
                }
                case District.Residential:
                {
                    var col = C(facade);
                    var pick = rng.NextDouble();
                    if (h > 70f && pick < 0.08)
                    {
                        // A landmark: a slim needle tower with a lit crown and a spire.
                        var r = Mathf.Min(w, d) * 0.42f;
                        m.Prism(foot, r, r * 0.62f, h * 1.25f, 8, deg, col, seed, 0.66f);
                        m.Prism(foot + Vector3.up * (h * 1.25f), r * 0.66f, r * 0.66f, 1.2f, 8, deg, C(CitadelGold), 0f, 0f, band: true);
                        m.Prism(foot + Vector3.up * (h * 1.25f + 1.2f), r * 0.3f, 0.05f, h * 0.3f, 6, deg, col, seed, 0f, cap: false);
                        Beacon(m, foot + Vector3.up * (h * 1.55f));
                    }
                    else if (pick < 0.25)
                        m.Prism(foot, Mathf.Min(w, d) * 0.5f, Mathf.Min(w, d) * 0.46f, h, 10, deg, col, seed, 0.58f);
                    else if (pick < 0.4 && h > 30f)
                    {
                        // Twin slabs joined by a sky bridge.
                        var side = Quaternion.Euler(0f, deg, 0f) * Vector3.right * (w * 0.32f);
                        m.Box(foot - side, w * 0.36f, d, h, deg, col, seed, 0.58f);
                        m.Box(foot + side, w * 0.36f, d, h * 0.86f, deg, col, seed + 0.31f, 0.58f);
                        m.Box(foot + Vector3.up * (h * 0.62f), w * 0.3f, d * 0.4f, 3.4f, deg, col, seed + 0.5f, 0.7f);
                    }
                    else
                        m.Box(foot, w, d, h, deg, col, seed, 0.55f);

                    if (h > 80f)
                        Beacon(m, foot + Vector3.up * h);
                    break;
                }
                case District.Research:
                {
                    // Glass domes glowing cyan at night over low labs.
                    m.Box(foot, w, d, Mathf.Min(h, 18f), deg, C(Color.Lerp(facade, Color.white, 0.4f)), seed, 0.7f);
                    m.Dome(foot + Vector3.up * Mathf.Min(h, 18f), Mathf.Min(w, d) * 0.48f, 14, 5,
                        C(new Color(0.35f, 0.75f, 0.85f) * 0.55f), true);
                    break;
                }
                case District.Spaceport:
                {
                    // Landing pads ringed in amber, a hangar and a gantry crane.
                    var pad = foot + Vector3.up * 0.1f;
                    var r = Mathf.Min(lot, depth) * 0.36f;
                    m.Prism(pad, r, r, 2.4f, 16, deg, C(_pal.FacadeB * 0.8f), seed, 0f);
                    m.Annulus(pad + Vector3.up * 2.5f, r * 0.72f, r * 0.8f, 24, C(new Color(1f, 0.6f, 0.2f)), true);
                    m.Annulus(pad + Vector3.up * 2.5f, r * 0.2f, r * 0.26f, 16, C(new Color(1f, 0.6f, 0.2f)), true);
                    _pads.Add(pad + Vector3.up * 3f);
                    if (rng.NextDouble() < 0.5)
                    {
                        var off = Quaternion.Euler(0f, deg, 0f) * new Vector3(r * 1.1f, 0f, 0f);
                        m.Box(foot + off, r * 0.5f, r * 1.6f, 11f, deg, C(_pal.FacadeB), seed, 0.3f);
                        m.Beam(foot + off + Vector3.up * 11f, foot - off * 0.4f + Vector3.up * 26f, 1.4f, C(_pal.FacadeA * 0.7f));
                        Beacon(m, foot - off * 0.4f + Vector3.up * 26f);
                    }

                    break;
                }
                case District.Industry:
                {
                    // Dark plants, smokestacks with red tips, tanks.
                    var col = C(Color.Lerp(facade, new Color(0.42f, 0.32f, 0.26f), 0.55f));
                    var hh = Mathf.Min(h, 24f);
                    m.Box(foot, w * 1.2f, d, hh, deg, col, seed, 0.25f);
                    var stack = Quaternion.Euler(0f, deg, 0f) * new Vector3(w * 0.35f, 0f, 0f);
                    m.Prism(foot + stack + Vector3.up * hh, 2.2f, 1.6f, 34f + (float)rng.NextDouble() * 20f, 8, 0f, col, seed, 0f);
                    m.Prism(foot - stack, d * 0.22f, d * 0.22f, 12f, 12, 0f, C(facade * 0.8f), seed, 0f);
                    Beacon(m, foot + stack + Vector3.up * (hh + 52f));
                    break;
                }
                case District.Crystal:
                {
                    // Crystal refinery: violet spires glowing out of a low hall.
                    m.Box(foot, w, d, 10f, deg, C(facade * 0.75f), seed, 0.3f);
                    for (var k = 0; k < 3; k++)
                    {
                        var off = Quaternion.Euler(0f, deg + k * 120f, 0f) * new Vector3(w * 0.22f, 0f, 0f);
                        m.Prism(foot + off + Vector3.up * 10f, 2.6f, 0.2f, 18f + k * 7f, 5, k * 40f,
                            C(new Color(0.7f, 0.42f, 1f) * 0.85f), 0f, 0f, band: true, cap: false);
                    }

                    break;
                }
                case District.Agri:
                {
                    // Green terraces and greenhouse domes.
                    var green = C(Color.Lerp(new Color(0.32f, 0.55f, 0.26f), facade, 0.3f));
                    m.Box(foot, w * 1.2f, d * 1.1f, 4f, deg, green, seed, 0.1f);
                    m.Box(foot + Vector3.up * 4f, w * 0.8f, d * 0.75f, 4f, deg, green, seed + 0.2f, 0.1f);
                    m.Dome(foot + Vector3.up * 8f, Mathf.Min(w, d) * 0.3f, 12, 4, C(new Color(0.55f, 0.9f, 0.5f) * 0.35f), true, 0.7f);
                    break;
                }
                case District.Military:
                {
                    // Barracks blocks and a parade ground, a beacon mast.
                    var col = C(Color.Lerp(facade, new Color(0.45f, 0.5f, 0.56f), 0.5f));
                    m.Box(foot, w, d, Mathf.Min(h, 22f), deg, col, seed, 0.45f);
                    Beacon(m, foot + Vector3.up * (Mathf.Min(h, 22f) + 6f));
                    break;
                }
            }
        }

        /// <summary>A red aviation light on a roof.</summary>
        static void Beacon(CityMesh m, Vector3 at)
        {
            m.Box(at, 1.1f, 1.1f, 1.1f, 0f, new Color32(255, 60, 40, 255), 0f, 0f, band: true);
        }

        void BuildWalls(CityMesh m, Levels lv, System.Random rng)
        {
            if (lv.Defense <= 0 && lv.DefenseUnits <= 0)
                return;
            // A wall round the city with a light line on its walk, and gun batteries (defence factory levels).
            var r = WorldScale.CityRadius + 18f;
            var segs = 96;
            var wallCol = C(_pal.FacadeB * 0.75f);
            var h = 16f + Mathf.Min(lv.Defense, 10) * 1.2f;
            for (var i = 0; i < segs; i++)
            {
                var deg = (i + 0.5f) * 360f / segs;
                var len = Mathf.PI * 2f * r / segs + 0.4f;
                m.Box(_centre + LatheMesh.Dir(deg) * r, len, 6f, h, deg, wallCol, 0.1f, 0.15f);
            }

            m.Annulus(_centre + Vector3.up * (h + 0.15f), r - 0.6f, r + 0.6f, segs, C(new Color(1f, 0.45f, 0.3f)), true);
            var turrets = Mathf.Clamp(6 + lv.Defense * 2 + lv.DefenseUnits / 4, 6, 28);
            for (var k = 0; k < turrets; k++)
            {
                var deg = (k + 0.25f) * 360f / turrets;
                var at = _centre + LatheMesh.Dir(deg) * r + Vector3.up * h;
                m.Prism(at, 5f, 4.2f, 4f, 10, deg, wallCol, 0.2f, 0f);
                var dir = (LatheMesh.Dir(deg) * 0.45f + Vector3.up).normalized;
                var muzzle = at + Vector3.up * 4f + dir * 9f;
                m.Beam(at + Vector3.up * 4f, muzzle, 1.1f, C(_pal.FacadeA * 0.5f));
                m.Box(muzzle, 0.9f, 0.9f, 0.9f, 0f, new Color32(255, 110, 70, 255), 0f, 0f, band: true);
                _turrets.Add(muzzle);
            }
        }

        void BuildOutskirts(CityMesh m, Levels lv, System.Random rng)
        {
            var beyond = WorldScale.CityRadius + 90f;
            // Solar fields: dark blue panel rows outside the walls.
            if (lv.Solar > 0)
            {
                var rows = Mathf.Clamp(lv.Solar, 2, 8);
                for (var k = 0; k < rows; k++)
                {
                    var deg = 150f + k * 4.2f;
                    var at = _centre + LatheMesh.Dir(deg) * (beyond + 20f) + Vector3.up * 1.2f;
                    m.Box(at, 60f, 7f, 1.2f, deg + 90f, new Color32(40, 62, 110, 255), 0.5f, 0f);
                }
            }

            // Cooling towers with a warm glow at their lips.
            if (lv.Nuclear > 0)
            {
                for (var k = 0; k < Mathf.Clamp(lv.Nuclear, 1, 4); k++)
                {
                    var at = _centre + LatheMesh.Dir(205f + k * 9f) * (beyond + 40f);
                    m.Prism(at, 26f, 17f, 30f, 18, 0f, C(_pal.FacadeA * 0.85f), 0.2f, 0f, cap: false);
                    m.Prism(at + Vector3.up * 30f, 17f, 20f, 22f, 18, 0f, C(_pal.FacadeA * 0.85f), 0.2f, 0f, cap: false);
                    m.Annulus(at + Vector3.up * 52.2f, 19f, 20f, 24, C(new Color(1f, 0.7f, 0.35f)), true);
                }
            }

            // Warehouses: long low depots by the outer ring road.
            if (lv.Warehouses > 0)
            {
                for (var k = 0; k < Mathf.Clamp(lv.Warehouses, 1, 8); k++)
                {
                    var deg = 245f + k * 6f;
                    m.Box(_centre + LatheMesh.Dir(deg) * (WorldScale.CityRadius - 22f), 40f, 18f, 9f, deg,
                        C(_pal.FacadeB * 0.85f), 0.7f, 0.25f);
                }
            }

            // Stargate / jumpgate: a standing ring outside the walls, glowing violet / cyan.
            if (lv.Gate > 0)
                GateRing(m, 60f, new Color(0.72f, 0.42f, 1f));
            if (lv.Jumpgate > 0)
                GateRing(m, 115f, new Color(0.3f, 0.85f, 0.9f));

            void GateRing(CityMesh mesh, float deg, Color glow)
            {
                var at = _centre + LatheMesh.Dir(deg) * (beyond + 70f);
                var face = LatheMesh.Dir(deg + 90f);
                const float R = 46f;
                const int n = 28;
                for (var i = 0; i < n; i++)
                {
                    var a0 = i * Mathf.PI * 2f / n;
                    var a1 = (i + 1) * Mathf.PI * 2f / n;
                    var p0 = at + Vector3.up * (R + 6f) + face * (Mathf.Cos(a0) * R) + Vector3.up * (Mathf.Sin(a0) * R);
                    var p1 = at + Vector3.up * (R + 6f) + face * (Mathf.Cos(a1) * R) + Vector3.up * (Mathf.Sin(a1) * R);
                    mesh.Beam(p0, p1, 6f, C(_pal.FacadeB * 0.7f));
                    mesh.Beam(p0, p1, 2f, C(glow), band: true);
                }

                mesh.Box(at, 30f, 14f, 8f, deg, C(_pal.FacadeB * 0.7f), 0.3f, 0.2f);
            }
        }

        // ── Sky ────────────────────────────────────────────────────────────────

        void BuildSky()
        {
            var mat = CityKit.Sky();
            mat.SetColor("_Zenith", _pal.Zenith);
            mat.SetColor("_Horizon", _pal.Horizon);
            mat.SetColor("_CloudColor", _pal.Cloud);
            mat.SetFloat("_CloudCover", _pal.CloudCover);
            var go = new GameObject("Sky");
            go.transform.SetParent(_built, false);
            go.transform.localScale = Vector3.one * (WorldScale.CitySkyRadius * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _sky = go.transform;

            // The other worlds of the system hang in the sky as moons.
            if (_focus == null)
                return;
            var shown = 0;
            foreach (var p in _focus.Planets)
            {
                if (p.Id == _planetId || shown >= 3)
                    continue;
                var kind = SystemBodyKit.ClassifyPlanet(p.Slot, p.Id, p.Habitability);
                var own = SystemBodyKit.ResolveOwnership(p.UserId, FocusContext.OwnedUserId());
                var moon = new GameObject("Moon_" + p.Id);
                moon.transform.SetParent(go.transform, false);
                var az = (p.Id * 73 % 360) * Mathf.Deg2Rad;
                var el = (18f + (p.Id * 31 % 40)) * Mathf.Deg2Rad;
                moon.transform.localPosition = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az)) * 0.42f;
                moon.transform.localScale = Vector3.one * (0.012f + 0.004f * (p.Slot % 4));
                moon.AddComponent<MeshFilter>().sharedMesh = SphereMesh.Smooth;
                var mmr = moon.AddComponent<MeshRenderer>();
                mmr.sharedMaterial = SystemBodyKit.PlanetMat(kind, own);
                mmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shown++;
            }
        }

        // ── Shield dome ────────────────────────────────────────────────────────

        void BuildDome(Levels lv)
        {
            _shieldStrength = lv.Shield > 0 ? Mathf.Clamp(0.35f + lv.Shield * 0.08f, 0.35f, 1.2f) : 0f;
            if (_shieldStrength <= 0f)
                return;
            const int seg = 64;
            const int rings = 16;
            var r = WorldScale.CityRadius + 60f;
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (var j = 0; j <= rings; j++)
            for (var i = 0; i <= seg; i++)
            {
                var a = i * Mathf.PI * 2f / seg;
                var t = j * Mathf.PI * 0.5f / rings;
                var d = new Vector3(Mathf.Cos(t) * Mathf.Sin(a), Mathf.Sin(t) * 0.62f, Mathf.Cos(t) * Mathf.Cos(a));
                verts.Add(_centre + d * r);
                norms.Add(new Vector3(d.x, d.y / 0.62f, d.z).normalized);
                uvs.Add(new Vector2(i / (float)seg, j / (float)rings));
            }

            for (var j = 0; j < rings; j++)
            for (var i = 0; i < seg; i++)
            {
                var a = j * (seg + 1) + i;
                var b = a + seg + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }

            var mesh = new Mesh { name = "SU_CityShield" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            _domeMat = CityKit.Dome();
            _dome = Part("ShieldDome", mesh, _domeMat);
        }

        // ── Air traffic ────────────────────────────────────────────────────────

        const int Craft = 150;

        struct Lane
        {
            public float Radius;
            public float Altitude;
            public float Speed;
            public float Phase;
            public Color32 Colour;
            /// <summary>&gt;= 0: a shuttle working that spaceport pad (up to its lane, back down), else a ring lane.</summary>
            public int Pad;
        }

        Lane[] _lanes;
        Vector3[] _trafficVerts;
        Mesh _trafficMesh;
        GameObject _traffic;

        void BuildTraffic(System.Random rng)
        {
            _lanes = new Lane[Craft];
            for (var i = 0; i < Craft; i++)
            {
                var band = i % 5;
                var radius = 110f + band * 95f + (float)rng.NextDouble() * 40f;
                var dir = (band & 1) == 0 ? 1f : -1f;
                _lanes[i] = new Lane
                {
                    Radius = radius,
                    Altitude = 55f + band * 34f + (float)rng.NextDouble() * 18f,
                    Speed = dir * (22f + (float)rng.NextDouble() * 14f) / radius,
                    Phase = (float)rng.NextDouble() * Mathf.PI * 2f,
                    Colour = dir > 0f ? new Color32(255, 236, 200, 255) : new Color32(255, 90, 70, 255),
                    // One craft in six is a shuttle on a spaceport pad (when the world has a shipyard).
                    Pad = _pads.Count > 0 && i % 6 == 0 ? (i / 6) % _pads.Count : -1
                };
                if (_lanes[i].Pad >= 0)
                    _lanes[i].Colour = new Color32(255, 170, 70, 255);
            }

            _trafficVerts = new Vector3[Craft * 4];
            var uv = new Vector2[Craft * 4];
            var cols = new Color32[Craft * 4];
            var tris = new int[Craft * 6];
            for (var i = 0; i < Craft; i++)
            {
                uv[i * 4] = new Vector2(0f, 0f);
                uv[i * 4 + 1] = new Vector2(0f, 1f);
                uv[i * 4 + 2] = new Vector2(1f, 1f);
                uv[i * 4 + 3] = new Vector2(1f, 0f);
                for (var k = 0; k < 4; k++)
                    cols[i * 4 + k] = _lanes[i].Colour;
                tris[i * 6] = i * 4;
                tris[i * 6 + 1] = i * 4 + 1;
                tris[i * 6 + 2] = i * 4 + 2;
                tris[i * 6 + 3] = i * 4;
                tris[i * 6 + 4] = i * 4 + 2;
                tris[i * 6 + 5] = i * 4 + 3;
            }

            _trafficMesh = new Mesh { name = "SU_CityTraffic" };
            _trafficMesh.MarkDynamic();
            _trafficMesh.vertices = _trafficVerts;
            _trafficMesh.uv = uv;
            _trafficMesh.colors32 = cols;
            _trafficMesh.triangles = tris;
            _trafficMesh.bounds = new Bounds(_centre, Vector3.one * (WorldScale.CityRadius * 2.2f));
            _traffic = Part("Traffic", _trafficMesh, CityKit.Traffic());
        }

        void TickTraffic(float t)
        {
            if (_trafficMesh == null || _lanes == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var inv = _built.worldToLocalMatrix;
            var right = inv.MultiplyVector(cam.transform.right).normalized;
            var up = inv.MultiplyVector(cam.transform.up).normalized;
            for (var i = 0; i < Craft; i++)
            {
                var l = _lanes[i];
                Vector3 c;
                if (l.Pad >= 0 && l.Pad < _pads.Count)
                {
                    // Lift off, climb out toward the tower's lanes, come back down: a 40 s round trip.
                    var pad = _pads[l.Pad];
                    var k = Mathf.PingPong(t / 20f + l.Phase, 1f);
                    var climb = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
                    var inward = (_centre - pad);
                    inward.y = 0f;
                    c = pad + Vector3.up * (climb * l.Altitude * 1.4f) + inward.normalized * (Mathf.SmoothStep(0f, 1f, k) * 160f);
                }
                else
                {
                    var a = l.Phase + t * l.Speed;
                    c = _centre + new Vector3(Mathf.Sin(a) * l.Radius, l.Altitude + Mathf.Sin(a * 3f + l.Phase) * 4f,
                        Mathf.Cos(a) * l.Radius);
                }
                const float s = 1.6f;
                var r = right * s;
                var u = up * s;
                _trafficVerts[i * 4] = c - r - u;
                _trafficVerts[i * 4 + 1] = c - r + u;
                _trafficVerts[i * 4 + 2] = c + r + u;
                _trafficVerts[i * 4 + 3] = c + r - u;
            }

            _trafficMesh.vertices = _trafficVerts;
        }

        // ── Our fortresses in the sky ──────────────────────────────────────────

        readonly List<Transform> _skyStations = new();
        string _stationSig;

        void SyncSkyStations()
        {
            if (_built == null || _focus == null)
                return;
            var sb = new System.Text.StringBuilder();
            var list = new List<FocusFleet>();
            foreach (var f in _focus.Fleets)
                if (f.IsStation && f.PlanetId == _planetId && _focus.IsMine(f))
                {
                    list.Add(f);
                    sb.Append(f.Id).Append(':').Append(ShipHullBuilder.Signature(f.Modules)).Append('|');
                }

            var sig = sb.ToString();
            if (sig == _stationSig)
                return;
            _stationSig = sig;
            foreach (var t in _skyStations)
            {
                if (t == null)
                    continue;
                var old = t.GetComponent<MeshFilter>()?.sharedMesh;
                if (old != null)
                {
                    _owned.Remove(old);
                    Drop(old);
                }

                Drop(t.gameObject);
            }

            _skyStations.Clear();
            foreach (var f in list)
            {
                // One merged mesh of its module silhouettes at their grid cells: one draw call per fortress.
                var parts = new List<CombineInstance>();
                foreach (var mod in f.Modules)
                {
                    if (mod == null || !mod.OnGrid)
                        continue;
                    var mm = ShipHullBuilder.ModuleMesh(mod.Type);
                    if (mm == null)
                        continue;
                    var at = new Vector3((mod.GridX - WorldScale.ShipCoreCell) * WorldScale.ShipCell, 0f,
                        (mod.GridY - WorldScale.ShipCoreCell) * WorldScale.ShipCell);
                    parts.Add(new CombineInstance { mesh = mm, transform = Matrix4x4.TRS(at, Quaternion.identity, Vector3.one) });
                }

                if (parts.Count == 0)
                    continue;
                var mesh = new Mesh { name = "SU_SkyFortress_" + f.Id, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), true, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                var go = Part("SkyFortress_" + f.Id, mesh, StationExterior.ModuleMat());
                go.transform.localScale = Vector3.one * WorldScale.CitySkyStationScale;
                _skyStations.Add(go.transform);
            }

            // On its track at once (never a frame at the city's origin).
            TickSkyStations(Time.time);
        }

        void TickSkyStations(float t)
        {
            for (var i = 0; i < _skyStations.Count; i++)
            {
                var tr = _skyStations[i];
                if (tr == null)
                    continue;
                // A slow pass across the sky (one orbit in ~12 min), each fortress on its own track.
                var a = t * (Mathf.PI * 2f / 720f) + i * 2.1f;
                tr.localPosition = _centre + new Vector3(Mathf.Sin(a) * 520f, WorldScale.CitySkyStationAltitude + i * 60f, Mathf.Cos(a) * 520f);
                tr.localRotation = Quaternion.Euler(14f, a * Mathf.Rad2Deg * 0.4f, 6f);
            }
        }

        // ── Siege ──────────────────────────────────────────────────────────────

        const int BeamPool = 6;
        LineRenderer[] _beams;
        float[] _beamT;
        Vector3[] _beamFrom;
        Vector3[] _beamTo;
        Color[] _beamCol;
        ParticleSystem _flak;
        float _siege;

        bool IsUs(int combatant) => combatant < 0 && -combatant == _planetId;

        void EnsureSiegeFx()
        {
            if (_beams != null)
                return;
            _beams = new LineRenderer[BeamPool];
            _beamT = new float[BeamPool];
            _beamFrom = new Vector3[BeamPool];
            _beamTo = new Vector3[BeamPool];
            _beamCol = new Color[BeamPool];
            for (var i = 0; i < BeamPool; i++)
            {
                var lr = new GameObject("SiegeBeam" + i).AddComponent<LineRenderer>();
                lr.transform.SetParent(transform, false);
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCapVertices = 2;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.sharedMaterial = CombatFxKit.Beam();
                lr.enabled = false;
                _beams[i] = lr;
                _beamT[i] = -1f;
            }

            _flak = CombatFxKit.Burst(transform, "CityFlak", 64, 1.1f, gravity: false, stretch: false);
        }

        /// <summary>Our world fires (planetary battery / flak): a battery on the walls shoots into the sky.</summary>
        void OnShot(int src, int dst, Color color, bool heavy)
        {
            if (!_shown || _built == null)
                return;
            if (IsUs(src))
            {
                EnsureSiegeFx();
                var from = _turrets.Count > 0
                    ? transform.TransformPoint(_turrets[Random.Range(0, _turrets.Count)])
                    : transform.TransformPoint(_centre + Vector3.up * (WorldScale.CityTowerHeight * 0.4f));
                var dir = (Random.onUnitSphere * 0.5f + Vector3.up).normalized;
                Fire(from, from + dir * 1200f, color);
                for (var i = 0; i < (heavy ? 5 : 2); i++)
                    CombatFxKit.Emit(_flak, from + dir * Random.Range(500f, 900f) + Random.insideUnitSphere * 60f,
                        new Color(1f, 0.8f, 0.5f, 1f), Random.Range(18f, 34f), 0.7f);
                _siege = 1f;
            }
            else if (IsUs(dst))
            {
                IncomingFire(color, heavy);
            }
        }

        void OnHit(int combatant, int hull, int shield)
        {
            if (!_shown || !IsUs(combatant))
                return;
            _hit = Mathf.Max(_hit, shield > 0 ? 1f : 0.5f);
            _siege = 1f;
        }

        void OnBombard(int fleet, int planet, bool toPlanet)
        {
            if (_shown && toPlanet && planet == _planetId)
                IncomingFire(new Color(1f, 0.45f, 0.2f, 1f), true);
        }

        /// <summary>Fire from orbit: a streak down onto the shield (or the city), a flash where it lands.</summary>
        void IncomingFire(Color color, bool heavy)
        {
            EnsureSiegeFx();
            var r = WorldScale.CityRadius * Random.Range(0.2f, 0.9f);
            var a = Random.Range(0f, Mathf.PI * 2f);
            var land = _centre + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
            if (_shieldStrength > 0f)
                land.y += Mathf.Sqrt(Mathf.Max(0f, 1f - (r * r) / Mathf.Pow(WorldScale.CityRadius + 60f, 2f))) * (WorldScale.CityRadius + 60f) * 0.62f;
            var to = transform.TransformPoint(land);
            var from = to + (Vector3.up + Random.insideUnitSphere * 0.4f).normalized * 1300f;
            Fire(from, to, color);
            CombatFxKit.Emit(_flak, to, _shieldStrength > 0f ? new Color(0.5f, 0.8f, 1f, 1f) : new Color(1f, 0.6f, 0.25f, 1f),
                heavy ? 70f : 40f, 0.9f);
            _hit = Mathf.Max(_hit, _shieldStrength > 0f ? 1f : 0f);
            _siege = 1f;
        }

        void Fire(Vector3 from, Vector3 to, Color color)
        {
            var slot = 0;
            for (var i = 0; i < BeamPool; i++)
                if (_beamT[i] < 0f)
                {
                    slot = i;
                    break;
                }

            _beamFrom[slot] = from;
            _beamTo[slot] = to;
            _beamCol[slot] = color;
            _beamT[slot] = 0f;
            _beams[slot].enabled = true;
        }

        void TickSiegeFx(float dt)
        {
            if (_beams == null)
                return;
            for (var i = 0; i < BeamPool; i++)
            {
                if (_beamT[i] < 0f)
                    continue;
                _beamT[i] += dt;
                var grow = Mathf.Clamp01(_beamT[i] / 0.35f);
                var fade = Mathf.Clamp01((_beamT[i] - 0.35f) / 0.5f);
                var lr = _beams[i];
                var head = Vector3.Lerp(_beamFrom[i], _beamTo[i], grow);
                var tail = Vector3.Lerp(_beamFrom[i], _beamTo[i], Mathf.Max(0f, grow - 0.35f));
                lr.SetPosition(0, tail);
                lr.SetPosition(1, head);
                lr.widthMultiplier = 5f;
                var c = _beamCol[i];
                c.a = 1f - fade;
                lr.startColor = c;
                lr.endColor = new Color(1f, 1f, 1f, c.a);
                if (fade >= 1f)
                {
                    _beamT[i] = -1f;
                    lr.enabled = false;
                }
            }
        }

        // ── Light and time ─────────────────────────────────────────────────────

        float _nextLight;

        void Update()
        {
            if (!_shown || _built == null)
                return;
            if (_pendingRebuild && ViewFade.Alpha > 0.95f)
                Rebuild(force: false);
            var t = Time.time;
            var dt = Time.deltaTime;
            TickTraffic(t);
            TickSkyStations(t);
            TickSiegeFx(dt);
            _hit = Mathf.MoveTowards(_hit, 0f, dt * 1.6f);
            var tactical = SiegeWatch.Instance != null && SiegeWatch.Instance.TryGet(_planetId, out var sg) && sg.Tactical;
            _siege = Mathf.MoveTowards(_siege, tactical ? 1f : 0f, dt * 0.25f);
            if (_domeMat != null)
                _domeMat.SetFloat("_Strength", _shieldStrength * (1f + _siege * 0.6f));
            Tick(false);
        }

        void LateUpdate()
        {
            // The sky follows the eye (never its turn), like the star sky behind it.
            var cam = Camera.main;
            if (_sky != null && cam != null)
                _sky.position = cam.transform.position;
        }

        void Tick(bool force)
        {
            var now = Time.unscaledTime;
            if (force || now >= _nextLight)
            {
                _nextLight = now + 1f;
                ApplyLight();
            }

            var nightVec = Shader.GetGlobalVector(NightId);
            Shader.SetGlobalVector(NightId, new Vector4(nightVec.x, _hit, Time.time, _siege));
        }

        /// <summary>
        /// The hour on this world (server clock, offset by planet): sun height and bearing, its colour (the star's,
        /// reddened low), the sky's light and the haze. Never pitch dark: a deep blue night lit by the city.
        /// </summary>
        void ApplyLight()
        {
            var unix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var phase = PinnedHour ?? Mathf.Repeat((float)(unix % 100000) / DayLength + _planetId * 0.618f, 1f);
            var elev = Mathf.Sin(phase * Mathf.PI * 2f) * 58f + 14f;
            var az = phase * 360f + _planetId * 37f;
            var er = elev * Mathf.Deg2Rad;
            var ar = az * Mathf.Deg2Rad;
            var sunDir = new Vector3(Mathf.Cos(er) * Mathf.Sin(ar), Mathf.Sin(er), Mathf.Cos(er) * Mathf.Cos(ar));
            var day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 12f, elev));
            var night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-10f, 4f, elev));
            var low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 30f, elev));
            var sun = Color.Lerp(_starColor, new Color(1f, 0.55f, 0.3f), low * 0.7f) * (1.05f * day);
            var ambient = Color.Lerp(new Color(0.13f, 0.16f, 0.26f), _pal.Horizon * 0.55f + _pal.Zenith * 0.2f, day);
            var bounce = Color.Lerp(new Color(0.06f, 0.06f, 0.09f), _pal.GroundLow * 0.32f, day);
            var haze = Color.Lerp(new Color(0.07f, 0.1f, 0.17f), Color.Lerp(_pal.Horizon, new Color(1f, 0.62f, 0.42f), low * day * 0.5f), day);
            Shader.SetGlobalVector(SunDirId, new Vector4(sunDir.x, sunDir.y, sunDir.z, day));
            Shader.SetGlobalVector(SunId, sun);
            Shader.SetGlobalVector(AmbientId, ambient);
            Shader.SetGlobalVector(BounceId, bounce);
            Shader.SetGlobalVector(FogId, new Vector4(haze.r, haze.g, haze.b, WorldScale.CityFogDensity));
            Shader.SetGlobalVector(NightId, new Vector4(night, _hit, Time.time, _siege));
            var c = transform.TransformPoint(_centre);
            Shader.SetGlobalVector(CentreId, new Vector4(c.x, c.y, c.z, WorldScale.CityRadius));
        }
    }
}
