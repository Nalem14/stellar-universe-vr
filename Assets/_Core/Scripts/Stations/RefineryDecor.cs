using Core.Vfx;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The fuel refinery as a place — a bright futuristic hydroponics bay, procedural and on shared materials:
    /// white lab panelling and floor tiles under a lit ceiling, grow-light racks of planted trays along both walls
    /// under violet and magenta bars, ten glass culture columns of glowing algae in front of them (one lit per
    /// culture level), a glass basin in the middle where a swarm of nanobots circles over the cyan liquid (as many
    /// as the nanobots level allows), the catalysis column rising from it to the ceiling with a spinning plasma
    /// core whose colour climbs amber → cyan → white with the catalysis level, ten frosted cryogenic tanks along the
    /// far wall under a window on space (one filled per cryo level, the liquid as high as the stock), and the
    /// transfer pump in front of the docking hatch, its five arms stroking (one per pump level) with fuel lines to
    /// the hatch. A stage not built yet stays dormant (dark liquid, dim lamps), never grey. An amber holo ring
    /// turns over the piece whose stage is being upgraded. One lectern per piece carries its holo panel, the
    /// status console stands at the entrance. Static geometry merged per material (<see cref="MeshBatch"/>); the
    /// level-driven sets are rebuilt only when a level changes; no realtime shadows.
    /// </summary>
    public sealed class RefineryDecor : MonoBehaviour
    {
        public const float HalfWidth = 6f;
        public const float BackZ = -2.4f;
        public const float FarZ = 11.6f;
        public const float Height = 5.4f;
        const float WindowLow = 3.15f;
        const float WindowTop = 4.75f;

        /// <summary>The nanobot basin's centre (room local, floor); the catalysis column rises from it.</summary>
        public static readonly Vector3 Pool = new(0f, 0f, 5.4f);
        const float PoolIn = 0.8f;
        const float PoolOut = 2.15f;
        const float RimTop = 0.72f;
        const float LiquidY = 0.36f;

        public const int Columns = 10;
        const float ColumnX = 4.85f;
        static readonly float[] ColumnZ = { 1.4f, 3.2f, 5.0f, 6.8f, 8.6f };
        const float ColumnR = 0.34f;
        const float ColumnBase = 0.5f;
        const float ColumnTop = 3.45f;

        public const int Tanks = 10;
        const float TankZ = 10.75f;
        const float TankR = 0.36f;
        const float TankBase = 0.26f;
        const float TankTop = 2.55f;
        const float TankLiquid = 2.1f;
        const float HeaderY = 2.95f;

        public const int Arms = 5;
        static readonly Vector3 Pump = new(0f, 0f, 9.7f);
        const float PumpR = 0.42f;
        const float ArmY = 0.85f;
        static readonly float[] ArmAngles = { 180f, 108f, 252f, 36f, 324f };
        const float HatchY = 1.3f;

        static readonly Color AlgaeGlow = new(0.4f, 1f, 0.62f, 1f);
        static readonly Color Violet = new(0.72f, 0.36f, 1f, 1f);
        static readonly Color Magenta = new(1f, 0.32f, 0.82f, 1f);
        static readonly Color Ice = new(0.62f, 0.9f, 1f, 1f);
        static readonly Color PlasmaAmber = new(1f, 0.55f, 0.16f, 1f);
        static readonly Color PlasmaCyan = new(0.3f, 0.92f, 1f, 1f);
        static readonly Color PlasmaWhite = new(0.96f, 0.98f, 1f, 1f);
        static readonly Color PlasmaDormant = new(0.42f, 0.22f, 0.6f, 1f);

        // Screen mounts (a screen parented here faces the player standing at the lectern, local −z).
        public Transform StatusMount { get; private set; }
        Transform[] _stageMounts;

        public Transform StageMount(string stage)
        {
            var i = System.Array.IndexOf(RefineryState.Stages, stage);
            return i >= 0 && _stageMounts != null ? _stageMounts[i] : StatusMount;
        }

        CicArtKit _art;
        Material _algaeOn;
        Material _algaeOff;
        Material _cryoOn;
        Material _cryoOff;
        Material _poolLiquid;
        Material _plasma;
        Material _lampViolet;
        Material _lampGreen;
        Material _lampCyan;
        Material _lampAmber;
        Material _lampOff;
        Material _steel;
        Material _dark;
        Mesh _columnLiquid;
        Mesh _tankLiquid;

        Transform _cultureLive;
        Transform _cryoLive;
        Transform _armsActive;
        Transform _armsIdle;
        Transform _stroke;
        Transform _core;
        Transform _marker;
        ParticleSystem _swarm;
        int _culture = -1;
        int _nanobots = -1;
        int _catalysis = -1;
        int _cryo = -1;
        int _cryoFill = -1;
        int _pump = -1;
        float _coreSpin = 4f;
        Vector3 _markerAt;

        public static RefineryDecor Build(Transform room, CicArtKit art, Color accent)
        {
            var root = new GameObject("RefineryDecor");
            root.transform.SetParent(room, false);
            var d = root.AddComponent<RefineryDecor>();
            d._art = art;
            d.BuildAll(accent);
            return d;
        }

        // ── Materials ─────────────────────────────────────────────────────────────

        /// <summary>SU/BioLiquid, or a lit emissive kit material of the same glow when the shader is missing (never magenta).</summary>
        Material Liquid(string name, Color liquid, Color glow, float mul, float density, float speed, float caustic)
        {
            var shader = Shader.Find("SU/BioLiquid");
            var m = shader != null ? new Material(shader) : new Material(_art.Lit(Texture2D.whiteTexture, glow, mul * 1.5f));
            m.name = name;
            if (shader != null)
            {
                m.SetFloat("_BubbleDensity", density);
                m.SetFloat("_BubbleSpeed", speed);
                m.SetFloat("_Caustic", caustic);
            }

            SetLiquid(m, liquid, glow, mul);
            return m;
        }

        static void SetLiquid(Material m, Color liquid, Color glow, float mul)
        {
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", liquid);
            if (m.HasProperty("_Emission"))
                m.SetColor("_Emission", glow);
            if (m.HasProperty("_EmissionMul"))
                m.SetFloat("_EmissionMul", mul);
        }

        void BuildAll(Color accent)
        {
            var art = _art;
            // Graphite and verdigris, not lab white: the glow comes from the algae, the grow bars and the trims.
            var floor = WorkshopSurfaces.Tiled(art, WorkshopSurfaces.LabTile(), new Color(0.26f, 0.32f, 0.33f), 0.6f, new Vector2(10f, 12f));
            var wallSide = WorkshopSurfaces.Tiled(art, WorkshopSurfaces.LabPanel(), new Color(0.27f, 0.36f, 0.37f), 0.62f, new Vector2(9f, 3.6f));
            var wallEnd = WorkshopSurfaces.Tiled(art, WorkshopSurfaces.LabPanel(), new Color(0.27f, 0.36f, 0.37f), 0.62f, new Vector2(8f, 2.2f));
            var ceiling = WorkshopSurfaces.Tiled(art, WorkshopSurfaces.AcousticCeiling(), new Color(0.2f, 0.25f, 0.27f), 0.7f, new Vector2(10f, 12f));
            var hazard = WorkshopSurfaces.Tiled(art, WorkshopSurfaces.Hazard(), Color.white, 0.6f, new Vector2(6f, 1f));
            _steel = art.Lit(art.Wall, new Color(0.72f, 0.77f, 0.82f, 1f), 0.55f, 2f);
            _dark = art.DarkPanel(0.35f);
            var lacquer = art.Lit(StationSurfaces.Panel(), new Color(0.36f, 0.42f, 0.44f), 0.6f, 1f);
            var trayMat = art.Lit(StationSurfaces.Panel(), new Color(0.2f, 0.24f, 0.26f), 0.4f, 1f);
            var leaf = art.Lit(Texture2D.whiteTexture, new Color(0.3f, 0.85f, 0.38f), 1.1f);
            var leafPale = art.Lit(Texture2D.whiteTexture, new Color(0.55f, 1f, 0.5f), 1.3f);
            var violet = art.Lit(Texture2D.whiteTexture, Violet, 2.6f);
            var magenta = art.Lit(Texture2D.whiteTexture, Magenta, 2.4f);
            var cyan = art.CyanEmit(2.2f);
            var glow = art.Lit(Texture2D.whiteTexture, accent, 2.4f);
            var panelLight = art.Lit(Texture2D.whiteTexture, new Color(0.86f, 0.95f, 1f, 1f), 1.35f);
            var glass = art.Holo(Texture2D.whiteTexture, new Color(0.6f, 0.95f, 1f, 0.11f));
            var frost = art.Holo(Texture2D.whiteTexture, new Color(0.5f, 0.78f, 0.92f, 0.16f));
            _lampViolet = art.Lit(Texture2D.whiteTexture, Violet, 3f);
            _lampGreen = art.Lit(Texture2D.whiteTexture, AlgaeGlow, 2.8f);
            _lampCyan = art.Lit(Texture2D.whiteTexture, Ice, 2.8f);
            _lampAmber = art.AmberEmit(2.6f);
            _lampOff = art.Lit(Texture2D.whiteTexture, new Color(0.16f, 0.2f, 0.28f, 1f), 0.5f);

            _algaeOn = Liquid("SU_RefineryAlgae", new Color(0.06f, 0.42f, 0.26f), AlgaeGlow, 1.3f, 9f, 0.12f, 0.6f);
            _algaeOff = Liquid("SU_RefineryAlgaeDormant", new Color(0.02f, 0.11f, 0.1f), new Color(0.12f, 0.4f, 0.36f), 0.4f, 6f, 0.03f, 0.35f);
            _cryoOn = Liquid("SU_RefineryCryo", new Color(0.24f, 0.44f, 0.6f), Ice, 1.1f, 14f, 0.04f, 0.5f);
            _cryoOff = Liquid("SU_RefineryCryoDormant", new Color(0.05f, 0.08f, 0.12f), new Color(0.22f, 0.32f, 0.46f), 0.35f, 10f, 0.02f, 0.3f);
            _poolLiquid = Liquid("SU_RefineryPool", new Color(0.03f, 0.3f, 0.36f), new Color(0.2f, 0.86f, 1f), 0.6f, 5f, 0.05f, 0.9f);
            _plasma = Liquid("SU_RefineryPlasma", PlasmaDormant * 0.4f, PlasmaDormant, 0.4f, 7f, 0.5f, 0.8f);

            var b = new MeshBatch();
            BuildShell(b, floor, wallSide, wallEnd, ceiling, panelLight, glow, cyan);
            BuildRacks(b, trayMat, leaf, leafPale, violet, magenta);
            BuildColumns(b, glass, cyan);
            BuildPool(b, glass, cyan);
            BuildCatalysis(b, glass, cyan);
            BuildTanks(b, frost);
            BuildPump(b, hazard, cyan);

            // Lecterns: the status console at the entrance, one per piece beside it.
            StatusMount = Lectern(b, "StatusMount", new Vector3(0f, 0f, 1.3f), 1.25f, lacquer, glow, _lampAmber);
            _stageMounts = new[]
            {
                Lectern(b, "CultureMount", new Vector3(-3.3f, 0f, 1.2f), 1.05f, lacquer, glow, _lampAmber),
                Lectern(b, "NanobotsMount", new Vector3(2.2f, 0f, 2.7f), 1.05f, lacquer, glow, _lampAmber),
                Lectern(b, "CatalysisMount", new Vector3(-2.2f, 0f, 2.7f), 1.05f, lacquer, glow, _lampAmber),
                Lectern(b, "CryoMount", new Vector3(-2.9f, 0f, 9.1f), 1.05f, lacquer, glow, _lampAmber),
                Lectern(b, "PumpMount", new Vector3(2.9f, 0f, 9.1f), 1.05f, lacquer, glow, _lampAmber)
            };
            b.Build(transform, "RefineryShell");

            BuildSwarm();
            BuildMarker();
            BuildColliders();
            Apply(null);
        }

        // ── Shell ─────────────────────────────────────────────────────────────────

        void BuildShell(MeshBatch b, Material floor, Material wallSide, Material wallEnd, Material ceiling, Material panelLight, Material glow,
            Material cyan)
        {
            var midZ = (BackZ + FarZ) * 0.5f;
            var len = FarZ - BackZ;
            b.Box(new Vector3(0f, -0.05f, midZ), new Vector3(HalfWidth * 2f + 0.4f, 0.1f, len + 0.4f), floor);
            b.Box(new Vector3(0f, Height + 0.06f, midZ), new Vector3(HalfWidth * 2f + 0.4f, 0.12f, len + 0.4f), ceiling);
            b.Box(new Vector3(0f, Height * 0.5f, BackZ - 0.1f), new Vector3(HalfWidth * 2f + 0.4f, Height, 0.2f), wallEnd);
            for (var s = -1; s <= 1; s += 2)
                b.Box(new Vector3(s * (HalfWidth + 0.1f), Height * 0.5f, midZ), new Vector3(0.2f, Height, len + 0.4f), wallSide);

            // Far wall: the tanks and the hatch below, a band of glass on space above them.
            b.Box(new Vector3(0f, WindowLow * 0.5f, FarZ + 0.1f), new Vector3(HalfWidth * 2f + 0.4f, WindowLow, 0.2f), wallEnd);
            b.Box(new Vector3(0f, (WindowTop + Height) * 0.5f, FarZ + 0.1f), new Vector3(HalfWidth * 2f + 0.4f, Height - WindowTop, 0.2f), wallEnd);
            b.Box(new Vector3(0f, WindowLow, FarZ - 0.08f), new Vector3(HalfWidth * 2f, 0.07f, 0.3f), _steel);
            b.Box(new Vector3(0f, WindowTop, FarZ - 0.05f), new Vector3(HalfWidth * 2f, 0.1f, 0.24f), _steel);
            b.Box(new Vector3(0f, WindowLow + 0.045f, FarZ - 0.22f), new Vector3(HalfWidth * 2f - 0.2f, 0.012f, 0.012f), cyan);
            for (var i = -2; i <= 2; i++)
            {
                var x = i * 2.4f;
                b.Box(new Vector3(x, (WindowLow + WindowTop) * 0.5f, FarZ + 0.02f), new Vector3(0.14f, WindowTop - WindowLow, 0.2f), _steel);
                b.Box(new Vector3(x, (WindowLow + WindowTop) * 0.5f, FarZ - 0.085f), new Vector3(0.02f, WindowTop - WindowLow - 0.2f, 0.01f), glow);
            }

            for (var s = -1; s <= 1; s += 2)
                b.Box(new Vector3(s * (HalfWidth - 0.35f), (WindowLow + WindowTop) * 0.5f, FarZ + 0.02f), new Vector3(0.7f, WindowTop - WindowLow, 0.2f),
                    wallEnd);

            // Cornice and kick lines along the side walls; a lit skirting on the back wall either side of the door.
            for (var s = -1; s <= 1; s += 2)
            {
                b.Box(new Vector3(s * (HalfWidth - 0.1f), Height - 0.2f, midZ), new Vector3(0.2f, 0.14f, len), _steel);
                b.Box(new Vector3(s * (HalfWidth - 0.21f), Height - 0.28f, midZ), new Vector3(0.012f, 0.02f, len - 0.2f), glow);
                b.Box(new Vector3(s * (HalfWidth - 0.02f), 0.08f, midZ), new Vector3(0.04f, 0.03f, len), glow);
                b.Box(new Vector3(s * 3.6f, 0.08f, BackZ + 0.02f), new Vector3(4.4f, 0.03f, 0.04f), glow);
            }

            // Ceiling: a grid of light panels (the bay is bright), and a lit ring over the basin.
            for (var row = 0; row < 4; row++)
                for (var col = -1; col <= 1; col += 2)
                {
                    var p = new Vector3(col * 2.4f, Height - 0.02f, -0.6f + row * 3.2f);
                    b.Box(p + Vector3.up * 0.01f, new Vector3(1.9f, 0.04f, 1.25f), _steel);
                    b.Box(p - Vector3.up * 0.012f, new Vector3(1.75f, 0.012f, 1.1f), panelLight);
                }

            var ring = new LatheMesh(Pool);
            ring.Revolve(new[] { new Vector2(2.3f, Height - 0.07f), new Vector2(2.42f, Height - 0.07f) }, 0f, 360f, false);
            ring.Revolve(new[] { new Vector2(2.42f, Height - 0.22f), new Vector2(2.42f, Height - 0.07f) }, 0f, 360f, true);
            b.Add(ring.ToMesh("SU_RefineryCeilingRing"), Matrix4x4.identity, panelLight);

            // Floor: runners from the door to the basin, a lit circle round it.
            for (var s = -1; s <= 1; s += 2)
                b.Box(new Vector3(s * 0.75f, 0.004f, 0.3f), new Vector3(0.03f, 0.008f, 4.6f), glow);
            var circle = new LatheMesh(Pool);
            circle.Revolve(new[] { new Vector2(PoolOut + 0.42f, 0.004f), new Vector2(PoolOut + 0.48f, 0.004f) }, 0f, 360f, true);
            b.Add(circle.ToMesh("SU_RefineryFloorCircle"), Matrix4x4.identity, glow);
        }

        /// <summary>Grow-light racks along both walls: posts, four shelves of planted trays, violet / magenta bars under each shelf.</summary>
        void BuildRacks(MeshBatch b, Material tray, Material leaf, Material leafPale, Material violet, Material magenta)
        {
            var seed = 41;
            float Rand()
            {
                seed = seed * 1103515245 + 12345 & 0x7fffffff;
                return seed / (float)0x7fffffff;
            }

            float[] shelves = { 0.85f, 1.6f, 2.35f, 3.1f };
            for (var s = -1; s <= 1; s += 2)
            {
                var x = s * (HalfWidth - 0.3f);
                for (var z = 0.5f; z < 9.6f; z += 1.8f)
                    b.Box(new Vector3(x, 1.8f, z), new Vector3(0.06f, 3.6f, 0.06f), _steel);
                foreach (var y in shelves)
                {
                    b.Box(new Vector3(x, y, 5.05f), new Vector3(0.5f, 0.035f, 9.1f), _steel);
                    b.Box(new Vector3(x, y + 0.07f, 5.05f), new Vector3(0.42f, 0.1f, 8.9f), tray);
                    // The light bar under the shelf lights the tray below it.
                    b.Box(new Vector3(x - s * 0.08f, y - 0.03f, 5.05f), new Vector3(0.05f, 0.02f, 8.8f), y > 2f ? violet : magenta);
                    for (var k = 0; k < 26; k++)
                    {
                        var z = 0.75f + k * 0.335f + (Rand() - 0.5f) * 0.1f;
                        var h = 0.08f + Rand() * 0.16f;
                        var w = 0.05f + Rand() * 0.06f;
                        var p = new Vector3(x + (Rand() - 0.5f) * 0.24f, y + 0.12f + h * 0.5f, z);
                        b.Box(p, new Vector3(w, h, w), k % 3 == 0 ? leafPale : leaf, Quaternion.Euler(0f, Rand() * 90f, (Rand() - 0.5f) * 30f));
                    }
                }

                // Top bar over the rack: the grow light that washes the whole wall.
                b.Box(new Vector3(x - s * 0.2f, 3.75f, 5.05f), new Vector3(0.12f, 0.06f, 9.1f), _steel);
                b.Box(new Vector3(x - s * 0.2f, 3.71f, 5.05f), new Vector3(0.08f, 0.02f, 9f), violet);
            }
        }

        // ── Culture columns ───────────────────────────────────────────────────────

        /// <summary>Column <paramref name="i"/> (room local, floor): lit in this order, front first, left then right.</summary>
        static Vector3 ColumnPos(int i) => new((i % 2 == 0 ? -1f : 1f) * ColumnX, 0f, ColumnZ[i / 2]);

        void BuildColumns(MeshBatch b, Material glass, Material cyan)
        {
            var shell = new LatheMesh(Vector3.zero) { Step = 15f };
            for (var i = 0; i < Columns; i++)
            {
                var p = ColumnPos(i);
                // Plinth, cage, cap, the feed going up into the ceiling.
                b.Tube(p + Vector3.up * (ColumnBase * 0.5f), Vector3.up, ColumnR + 0.08f, ColumnBase, _steel);
                b.Tube(p + Vector3.up * 0.12f, Vector3.up, ColumnR + 0.1f, 0.03f, cyan);
                for (var k = 0; k < 3; k++)
                {
                    var d = LatheMesh.Dir(k * 120f + 30f) * (ColumnR + 0.04f);
                    b.Strut(p + d + Vector3.up * ColumnBase, p + d + Vector3.up * ColumnTop, 0.035f, _steel);
                }

                b.Tube(p + Vector3.up * (ColumnTop + 0.12f), Vector3.up, ColumnR + 0.06f, 0.2f, _steel);
                b.Pipe(p + Vector3.up * (ColumnTop + 0.2f), p + Vector3.up * Height, 0.07f, _dark);
                b.Pipe(p + Vector3.up * 0.3f, new Vector3(p.x + Mathf.Sign(p.x) * 0.8f, 0.3f, p.z), 0.05f, _dark);
                shell.Centre = p;
                shell.Revolve(new[] { new Vector2(ColumnR, ColumnBase), new Vector2(ColumnR, ColumnTop) }, 0f, 360f, false);
            }

            b.Add(shell.ToMesh("SU_RefineryColumnGlass"), Matrix4x4.identity, glass);

            var liquid = new LatheMesh(Vector3.zero) { Step = 20f };
            liquid.Revolve(new[]
            {
                new Vector2(ColumnR - 0.07f, ColumnBase + 0.02f), new Vector2(ColumnR - 0.07f, ColumnTop - 0.18f),
                new Vector2(ColumnR - 0.12f, ColumnTop - 0.1f), new Vector2(0f, ColumnTop - 0.08f)
            }, 0f, 360f, false);
            _columnLiquid = liquid.ToMesh("SU_RefineryColumnLiquid");
        }

        void SetCulture(int level)
        {
            if (level == _culture)
                return;
            _culture = level;
            var b = new MeshBatch();
            for (var i = 0; i < Columns; i++)
            {
                var p = ColumnPos(i);
                var on = i < level;
                b.Add(_columnLiquid, Matrix4x4.Translate(p), on ? _algaeOn : _algaeOff);
                // Grow lamp under the cap, status ring at the foot.
                b.Tube(p + Vector3.up * (ColumnTop - 0.01f), Vector3.up, ColumnR + 0.025f, 0.05f, on ? _lampViolet : _lampOff);
                b.Tube(p + Vector3.up * (ColumnBase + 0.01f), Vector3.up, ColumnR + 0.05f, 0.03f, on ? _lampGreen : _lampOff);
            }

            Replace(ref _cultureLive, b, transform, "CultureLive");
        }

        // ── Nanobot basin and the catalysis column ───────────────────────────────

        void BuildPool(MeshBatch b, Material glass, Material cyan)
        {
            var g = new LatheMesh(Pool);
            g.Revolve(new[] { new Vector2(PoolOut, 0.1f), new Vector2(PoolOut, RimTop) }, 0f, 360f, false);
            b.Add(g.ToMesh("SU_RefineryPoolGlass"), Matrix4x4.identity, glass);

            var metal = new LatheMesh(Pool);
            // Foot ring, coping on the glass, the column's plinth in the middle.
            metal.Revolve(new[] { new Vector2(PoolOut + 0.1f, 0f), new Vector2(PoolOut + 0.1f, 0.1f), new Vector2(PoolOut - 0.05f, 0.1f) }, 0f, 360f,
                false);
            metal.Revolve(new[]
            {
                new Vector2(PoolOut - 0.06f, RimTop - 0.04f), new Vector2(PoolOut + 0.1f, RimTop - 0.04f), new Vector2(PoolOut + 0.1f, RimTop + 0.03f),
                new Vector2(PoolOut - 0.06f, RimTop + 0.03f)
            }, 0f, 360f, false);
            metal.Revolve(new[]
            {
                new Vector2(PoolIn, 0f), new Vector2(PoolIn, 0.85f), new Vector2(0.64f, 0.95f), new Vector2(0.5f, 0.95f)
            }, 0f, 360f, false);
            b.Add(metal.ToMesh("SU_RefineryPoolMetal"), Matrix4x4.identity, _steel);

            var lit = new LatheMesh(Pool);
            lit.Revolve(new[] { new Vector2(PoolOut + 0.105f, 0.04f), new Vector2(PoolOut + 0.105f, 0.065f) }, 0f, 360f, false);
            lit.Revolve(new[] { new Vector2(PoolOut + 0.105f, RimTop - 0.02f), new Vector2(PoolOut + 0.105f, RimTop + 0.01f) }, 0f, 360f, false);
            lit.Revolve(new[] { new Vector2(PoolIn + 0.005f, 0.62f), new Vector2(PoolIn + 0.005f, 0.66f) }, 0f, 360f, false);
            b.Add(lit.ToMesh("SU_RefineryPoolLit"), Matrix4x4.identity, cyan);

            // The liquid: its surface between the plinth and the glass, its side just inside the glass.
            var l = new LatheMesh(Pool) { Step = 6f };
            l.Revolve(new[] { new Vector2(PoolIn, LiquidY), new Vector2(PoolOut - 0.02f, LiquidY) }, 0f, 360f, true);
            l.Revolve(new[] { new Vector2(PoolOut - 0.02f, 0.1f), new Vector2(PoolOut - 0.02f, LiquidY) }, 0f, 360f, false);
            LatheMesh.Part(transform, "PoolLiquid", l.ToMesh("SU_RefineryPoolLiquid"), _poolLiquid);
        }

        void BuildCatalysis(MeshBatch b, Material glass, Material cyan)
        {
            const float r = 0.5f;
            const float y0 = 0.95f;
            const float y1 = 4.1f;
            var g = new LatheMesh(Pool);
            g.Revolve(new[] { new Vector2(r, y0), new Vector2(r, y1) }, 0f, 360f, false);
            b.Add(g.ToMesh("SU_RefineryCatalysisGlass"), Matrix4x4.identity, glass);
            var crown = new LatheMesh(Pool);
            crown.Revolve(new[]
            {
                new Vector2(0.62f, y1 - 0.05f), new Vector2(0.62f, y1 + 0.25f), new Vector2(0.32f, y1 + 0.5f), new Vector2(0.24f, Height)
            }, 0f, 360f, false);
            crown.Revolve(new[] { new Vector2(0f, y1 - 0.05f), new Vector2(0.62f, y1 - 0.05f) }, 0f, 360f, false);
            b.Add(crown.ToMesh("SU_RefineryCatalysisCrown"), Matrix4x4.identity, _steel);
            for (var k = 0; k < 4; k++)
            {
                var d = LatheMesh.Dir(k * 90f + 45f) * (r + 0.07f);
                b.Strut(Pool + d + Vector3.up * y0, Pool + d + Vector3.up * y1, 0.05f, _steel);
            }

            foreach (var y in new[] { 1.4f, 2.5f, 3.6f })
                b.Tube(Pool + Vector3.up * y, Vector3.up, r + 0.04f, 0.04f, cyan);

            // The plasma core: a spindle and three orbit bands, turning together (own transform, own material).
            _core = new GameObject("PlasmaCore").transform;
            _core.SetParent(transform, false);
            _core.localPosition = Pool;
            var core = new LatheMesh(Vector3.zero) { Step = 12f };
            core.Revolve(new[]
            {
                new Vector2(0f, y0 + 0.05f), new Vector2(0.12f, y0 + 0.2f), new Vector2(0.17f, 1.6f), new Vector2(0.1f, 2.3f), new Vector2(0.17f, 3.0f),
                new Vector2(0.12f, 3.8f), new Vector2(0f, y1 - 0.05f)
            }, 0f, 360f, false);
            foreach (var y in new[] { 1.6f, 2.55f, 3.5f })
            {
                core.Revolve(new[] { new Vector2(0.3f, y), new Vector2(0.36f, y) }, 0f, 360f, true);
                core.Revolve(new[] { new Vector2(0.3f, y - 0.004f), new Vector2(0.36f, y - 0.004f) }, 0f, 360f, false);
            }

            LatheMesh.Part(_core, "Plasma", core.ToMesh("SU_RefineryPlasma"), _plasma);
        }

        void SetCatalysis(int level)
        {
            if (level == _catalysis)
                return;
            _catalysis = level;
            Color c;
            if (level <= 0)
                c = PlasmaDormant;
            else
            {
                var t = (level - 1) / 9f;
                c = t < 0.5f ? Color.Lerp(PlasmaAmber, PlasmaCyan, t * 2f) : Color.Lerp(PlasmaCyan, PlasmaWhite, (t - 0.5f) * 2f);
            }

            SetLiquid(_plasma, c * (level <= 0 ? 0.35f : 0.55f), c, level <= 0 ? 0.4f : 1.3f + level * 0.14f);
            _coreSpin = level <= 0 ? 4f : 18f + level * 9f;
        }

        void BuildSwarm()
        {
            var go = new GameObject("NanobotSwarm");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Pool + Vector3.up * (LiquidY + 0.13f);
            _swarm = go.AddComponent<ParticleSystem>();
            _swarm.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _swarm.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 10f);
            main.startSpeed = 0f;
            // Big enough to read from the lectern (a few centimetres of glow each), tiny next to the basin.
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.075f);
            main.maxParticles = 12;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var shape = _swarm.shape;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = (PoolIn + PoolOut) * 0.5f;
            shape.donutRadius = (PoolOut - PoolIn) * 0.5f - 0.12f;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.scale = new Vector3(1f, 1f, 0.3f);
            var vel = _swarm.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            vel.y = new ParticleSystem.MinMaxCurve(-0.015f, 0.015f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.01f, 0.01f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = _swarm.noise;
            noise.enabled = true;
            noise.strength = 0.05f;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 0.3f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var col = _swarm.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var shader = Shader.Find("SU/ParticleGlow");
            var mat = shader != null ? new Material(shader) : new Material(_art.Holo(Texture2D.whiteTexture, PlasmaCyan));
            mat.name = "SU_RefineryNanobot";
            if (mat.HasProperty("_MainTex") && _art.ProjectorGlow != null)
                mat.mainTexture = _art.ProjectorGlow;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_EmissionMul"))
                mat.SetFloat("_EmissionMul", 2.6f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void SetNanobots(int level)
        {
            if (level == _nanobots)
                return;
            _nanobots = level;
            var count = level <= 0 ? 12 : 40 + level * 36;
            var main = _swarm.main;
            main.maxParticles = count;
            main.startColor = level <= 0
                ? new ParticleSystem.MinMaxGradient(new Color(0.3f, 0.5f, 0.6f, 0.45f), new Color(0.35f, 0.4f, 0.7f, 0.4f))
                : new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.95f, 1f), new Color(0.4f, 1f, 0.55f, 1f));
            var emission = _swarm.emission;
            emission.rateOverTime = count / 8.5f;
            SetLiquid(_poolLiquid, new Color(0.03f, 0.3f, 0.36f), new Color(0.2f, 0.86f, 1f), level <= 0 ? 0.45f : 0.6f + level * 0.07f);
            if (!_swarm.isPlaying)
                _swarm.Play();
        }

        // ── Cryogenic tanks ───────────────────────────────────────────────────────

        /// <summary>Tank <paramref name="k"/>: filled from the hatch outward, alternately left and right.</summary>
        static Vector3 TankPos(int k) => new((k % 2 == 0 ? -1f : 1f) * (1.75f + k / 2 * 0.8f), 0f, TankZ);

        void BuildTanks(MeshBatch b, Material frost)
        {
            var shell = new LatheMesh(Vector3.zero) { Step = 15f };
            for (var k = 0; k < Tanks; k++)
            {
                var p = TankPos(k);
                b.Tube(p + Vector3.up * (TankBase * 0.5f), Vector3.up, TankR + 0.06f, TankBase, _steel);
                foreach (var y in new[] { 0.7f, 1.5f, 2.3f })
                    b.Tube(p + Vector3.up * y, Vector3.up, TankR + 0.012f, 0.05f, _steel);
                b.Tube(p + Vector3.up * (TankTop + 0.32f), Vector3.up, 0.06f, 0.14f, _dark);
                b.Pipe(p + Vector3.up * (TankTop + 0.36f), new Vector3(p.x, HeaderY, p.z), 0.045f, _dark);
                shell.Centre = p;
                shell.Revolve(new[]
                {
                    new Vector2(TankR, TankBase), new Vector2(TankR, TankTop), new Vector2(TankR * 0.72f, TankTop + 0.2f),
                    new Vector2(0.08f, TankTop + 0.28f)
                }, 0f, 360f, false);
            }

            b.Add(shell.ToMesh("SU_RefineryTankFrost"), Matrix4x4.identity, frost);
            // The header over the tanks, down to the pump.
            b.Pipe(new Vector3(-5.3f, HeaderY, TankZ), new Vector3(5.3f, HeaderY, TankZ), 0.075f, _steel);
            for (var s = -1; s <= 1; s += 2)
            {
                b.Pipe(new Vector3(s * 0.55f, HeaderY, TankZ), new Vector3(s * 0.55f, HeaderY, Pump.z), 0.06f, _steel);
                b.Pipe(new Vector3(s * 0.55f, HeaderY, Pump.z), Pump + new Vector3(s * 0.18f, 1.56f, 0f), 0.06f, _steel);
            }

            var liquid = new LatheMesh(Vector3.zero) { Step = 20f };
            liquid.Revolve(new[] { new Vector2(TankR - 0.07f, 0f), new Vector2(TankR - 0.07f, TankLiquid), new Vector2(0f, TankLiquid) }, 0f, 360f,
                false);
            _tankLiquid = liquid.ToMesh("SU_RefineryTankLiquid");
        }

        void SetCryo(int level, float fill)
        {
            var bucket = Mathf.RoundToInt(Mathf.Clamp01(fill) * 20f);
            if (level == _cryo && bucket == _cryoFill)
                return;
            _cryo = level;
            _cryoFill = bucket;
            var b = new MeshBatch();
            for (var k = 0; k < Tanks; k++)
            {
                var p = TankPos(k);
                var on = k < level;
                // A built tank holds the stock (as full as the refinery); an empty one a dark frost residue.
                var h = on ? Mathf.Lerp(0.16f, TankLiquid, bucket / 20f) : 0.14f;
                b.Add(_tankLiquid, Matrix4x4.TRS(p + Vector3.up * (TankBase + 0.04f), Quaternion.identity, new Vector3(1f, h / TankLiquid, 1f)),
                    on ? _cryoOn : _cryoOff);
                b.Tube(p + Vector3.up * (TankTop + 0.04f), Vector3.up, TankR + 0.02f, 0.05f, on ? _lampCyan : _lampOff);
                b.Box(p + new Vector3(0f, 1.1f, -TankR - 0.02f), new Vector3(0.05f, 0.05f, 0.02f), on ? _lampCyan : _lampOff);
            }

            Replace(ref _cryoLive, b, transform, "CryoLive");
        }

        // ── Transfer pump and docking hatch ──────────────────────────────────────

        void BuildPump(MeshBatch b, Material hazard, Material cyan)
        {
            b.Tube(Pump + Vector3.up * 0.07f, Vector3.up, PumpR + 0.22f, 0.14f, _steel);
            var drum = new LatheMesh(Pump);
            drum.Revolve(new[]
            {
                new Vector2(PumpR, 0.14f), new Vector2(PumpR, 1.3f), new Vector2(PumpR - 0.12f, 1.5f), new Vector2(0.12f, 1.58f)
            }, 0f, 360f, false);
            b.Add(drum.ToMesh("SU_RefineryPumpDrum"), Matrix4x4.identity, _steel);
            foreach (var y in new[] { 0.4f, 1.22f })
                b.Tube(Pump + Vector3.up * y, Vector3.up, PumpR + 0.01f, 0.035f, cyan);
            // Arm housings round the drum (the pistons themselves follow the pump level).
            foreach (var a in ArmAngles)
            {
                var d = LatheMesh.Dir(a);
                b.Pipe(Pump + Vector3.up * ArmY + d * (PumpR - 0.05f), Pump + Vector3.up * ArmY + d * 0.62f, 0.1f, _dark);
            }

            // Fuel lines from the drum to the hatch collar.
            for (var s = -1; s <= 1; s += 2)
            {
                var from = Pump + new Vector3(s * 0.3f, 0.5f, 0.3f);
                var bend = new Vector3(s * 1.15f, 0.5f, FarZ - 0.6f);
                b.Pipe(from, bend, 0.07f, _steel);
                b.Pipe(bend, new Vector3(s * 1.15f, 0.5f, FarZ), 0.07f, _steel);
                b.Tube(bend, Vector3.up, 0.1f, 0.12f, _dark);
            }

            // The docking hatch: collar, amber seal ring, the door, its wheel, a lit lintel, hazard stripes before it.
            var hatch = new Vector3(0f, HatchY, FarZ);
            b.Tube(hatch + Vector3.back * 0.05f, Vector3.forward, 1.15f, 0.14f, _steel);
            b.Tube(hatch + Vector3.back * 0.1f, Vector3.forward, 1.03f, 0.1f, _lampAmber);
            b.Tube(hatch + Vector3.back * 0.12f, Vector3.forward, 0.95f, 0.1f, _dark);
            b.Tube(hatch + Vector3.back * 0.19f, Vector3.forward, 0.32f, 0.05f, _steel);
            for (var k = 0; k < 3; k++)
                b.Box(hatch + Vector3.back * 0.2f, new Vector3(0.76f, 0.05f, 0.03f), _steel, Quaternion.Euler(0f, 0f, k * 60f));
            b.Box(new Vector3(0f, HatchY + 1.25f, FarZ - 0.06f), new Vector3(1.3f, 0.05f, 0.03f), _lampAmber);
            b.Box(new Vector3(0f, 0.006f, FarZ - 0.45f), new Vector3(2.3f, 0.012f, 0.5f), hazard);
        }

        void SetPump(int level)
        {
            if (level == _pump)
                return;
            _pump = level;
            if (_stroke == null)
            {
                _stroke = new GameObject("PumpStroke").transform;
                _stroke.SetParent(transform, false);
                _stroke.localPosition = Pump + Vector3.up * ArmY;
            }

            var active = new MeshBatch();
            var idle = new MeshBatch();
            for (var a = 0; a < Arms; a++)
            {
                var d = LatheMesh.Dir(ArmAngles[a]);
                if (a < level)
                {
                    // Local to the stroke (on the drum's axis at arm height): the whole set breathes outward.
                    active.Pipe(d * 0.6f, d * 0.92f, 0.045f, _steel);
                    active.Tube(d * 0.95f, d, 0.1f, 0.06f, _lampGreen);
                }
                else
                {
                    var c = Pump + Vector3.up * ArmY;
                    idle.Pipe(c + d * 0.6f, c + d * 0.74f, 0.045f, _steel);
                    idle.Tube(c + d * 0.77f, d, 0.1f, 0.06f, _lampOff);
                }
            }

            Replace(ref _armsActive, active, _stroke, "ArmsActive");
            Replace(ref _armsIdle, idle, transform, "ArmsIdle");
        }

        // ── Work in progress marker ──────────────────────────────────────────────

        void BuildMarker()
        {
            var m = new LatheMesh(Vector3.zero) { Step = 6f };
            m.Revolve(new[] { new Vector2(0.9f, 0f), new Vector2(1f, 0f) }, 0f, 360f, true);
            m.Revolve(new[] { new Vector2(1f, -0.06f), new Vector2(1f, 0.06f) }, 0f, 360f, false);
            m.Revolve(new[] { new Vector2(0.78f, 0.1f), new Vector2(0.82f, 0.1f) }, 0f, 300f, true);
            _marker = LatheMesh.Part(transform, "UpgradeMarker", m.ToMesh("SU_RefineryMarker"),
                _art.Holo(Texture2D.whiteTexture, new Color(1f, 0.66f, 0.25f, 0.7f))).transform;
            _marker.gameObject.SetActive(false);
        }

        void SetMarker(string stage, RefineryState s)
        {
            if (string.IsNullOrEmpty(stage) || s == null)
            {
                _marker.gameObject.SetActive(false);
                return;
            }

            var next = Mathf.Max(0, s.Level(stage));
            var (at, size) = stage switch
            {
                "culture" => (ColumnPos(Mathf.Min(next, Columns - 1)) + Vector3.up * (ColumnTop + 0.35f), 0.55f),
                "nanobots" => (Pool + Vector3.up * (RimTop + 0.12f), PoolOut + 0.2f),
                "catalysis" => (Pool + Vector3.up * 4.3f, 0.8f),
                "cryo" => (TankPos(Mathf.Min(next, Tanks - 1)) + Vector3.up * (TankTop + 0.55f), 0.55f),
                _ => (Pump + Vector3.up * 1.8f, 0.8f)
            };
            _markerAt = at;
            _marker.localPosition = at;
            _marker.localScale = Vector3.one * size;
            _marker.gameObject.SetActive(true);
        }

        // ── Lecterns and colliders ───────────────────────────────────────────────

        /// <summary>
        /// A sloped lectern (as <see cref="GateRoomDecor"/>'s desk, but merged into the room's batch) turned to the
        /// entrance; returns the screen mount at its arm's head (a screen seated there faces whoever stands at it).
        /// </summary>
        Transform Lectern(MeshBatch b, string name, Vector3 pos, float width, Material body, Material glow, Material amber)
        {
            var rot = Quaternion.Euler(0f, GateRoomDecor.FaceStand(pos.x, pos.z), 0f);
            var tilt = rot * Quaternion.Euler(-14f, 0f, 0f);
            Vector3 P(float x, float y, float z) => pos + rot * new Vector3(x, y, z);
            b.Box(P(0f, 0.42f, 0.05f), new Vector3(width * 0.8f, 0.84f, 0.5f), body, rot);
            b.Box(P(0f, 0.06f, -0.2f), new Vector3(width * 0.82f, 0.12f, 0.04f), glow, rot);
            b.Box(P(0f, 0.9f, -0.05f), new Vector3(width, 0.05f, 0.62f), _steel, tilt);
            b.Box(P(0f, 0.855f, -0.36f), new Vector3(width, 0.02f, 0.02f), glow, tilt);
            for (var r = 0; r < 3; r++)
                b.Box(P(0f, 0.915f + r * 0.012f, -0.28f + r * 0.06f), new Vector3(width * 0.55f, 0.012f, 0.035f), r == 1 ? amber : glow, tilt);
            for (var side = -1; side <= 1; side += 2)
                b.Box(P(side * width * 0.5f, 0.55f, 0f), new Vector3(0.05f, 1.05f, 0.66f), _steel, rot);
            b.Box(P(0f, 0.93f, 0.26f), new Vector3(0.08f, 0.14f, 0.08f), _steel, rot);
            b.Box(P(0f, 1.0f, 0.26f), new Vector3(0.22f, 0.04f, 0.1f), _steel, rot);

            Solid(P(0f, 0.5f, 0.05f), new Vector3(width, 1f, 0.6f), rot);
            var mount = new GameObject(name).transform;
            mount.SetParent(transform, false);
            mount.localPosition = P(0f, 1.02f, 0.26f);
            mount.localRotation = rot;
            return mount;
        }

        void BuildColliders()
        {
            var midZ = (BackZ + FarZ) * 0.5f;
            var len = FarZ - BackZ;
            Solid(new Vector3(0f, -0.25f, midZ), new Vector3(HalfWidth * 2f, 0.5f, len), Quaternion.identity);
            Solid(new Vector3(0f, 2.5f, BackZ - 0.3f), new Vector3(HalfWidth * 2f, 5f, 0.6f), Quaternion.identity);
            Solid(new Vector3(0f, 2.5f, FarZ + 0.3f), new Vector3(HalfWidth * 2f, 5f, 0.6f), Quaternion.identity);
            for (var s = -1; s <= 1; s += 2)
            {
                Solid(new Vector3(s * (HalfWidth + 0.3f), 2.5f, midZ), new Vector3(0.6f, 5f, len), Quaternion.identity);
                // The culture columns and their racks: one strip along each wall.
                Solid(new Vector3(s * (ColumnX + 0.4f), 1.5f, 5f), new Vector3(1.6f, 3f, 8.4f), Quaternion.identity);
                // The tank rows either side of the hatch.
                Solid(new Vector3(s * 3.35f, 1.5f, TankZ), new Vector3(4f, 3f, 0.9f), Quaternion.identity);
            }

            // The basin: an octagon of two squares round its glass.
            var side = PoolOut * 2f * 0.84f;
            Solid(Pool + Vector3.up * 0.5f, new Vector3(side, 1f, side), Quaternion.identity);
            Solid(Pool + Vector3.up * 0.5f, new Vector3(side, 1f, side), Quaternion.Euler(0f, 45f, 0f));
            Solid(Pump + Vector3.up * 0.8f, new Vector3(1.9f, 1.6f, 1.9f), Quaternion.identity);
        }

        void Solid(Vector3 c, Vector3 size, Quaternion rot)
        {
            var go = new GameObject("RefineryCollider");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = c;
            go.transform.localRotation = rot;
            go.AddComponent<BoxCollider>().size = size;
        }

        // ── State ─────────────────────────────────────────────────────────────────

        /// <summary>Dress the bay for a refinery as the server reports it (null or not built: everything dormant).</summary>
        public void Apply(RefineryState s)
        {
            var built = s != null && s.Built;
            SetCulture(built ? s.Level("culture") : 0);
            SetNanobots(built ? s.Level("nanobots") : 0);
            SetCatalysis(built ? s.Level("catalysis") : 0);
            SetCryo(built ? s.Level("cryo") : 0, built ? s.Fill : 0f);
            SetPump(built ? s.Level("pump") : 0);
            SetMarker(s != null && s.Busy ? s.WorkingStage : null, s);
        }

        /// <summary>Swap a level-driven set: the old one and its baked meshes go, the new batch takes its place.</summary>
        static void Replace(ref Transform slot, MeshBatch b, Transform parent, string name)
        {
            if (slot != null)
            {
                foreach (var mf in slot.GetComponentsInChildren<MeshFilter>(true))
                    Destroy(mf.sharedMesh);
                Destroy(slot.gameObject);
            }

            slot = b.Build(parent, name);
        }

        void Update()
        {
            var t = Time.time;
            if (_stroke != null)
            {
                var k = 1f + 0.12f * (0.5f + 0.5f * Mathf.Sin(t * 3.2f));
                _stroke.localScale = new Vector3(k, 1f, k);
            }

            if (_core != null)
                _core.localRotation = Quaternion.Euler(0f, t * _coreSpin % 360f, 0f);
            if (_marker != null && _marker.gameObject.activeSelf)
            {
                _marker.localRotation = Quaternion.Euler(0f, t * 40f % 360f, 0f);
                _marker.localPosition = _markerAt + Vector3.up * (Mathf.Sin(t * 1.7f) * 0.05f);
            }
        }
    }
}
