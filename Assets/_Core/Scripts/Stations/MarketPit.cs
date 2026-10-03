using System;
using System.Collections.Generic;
using System.Text;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Stations
{
    /// <summary>
    /// The exchange's machinery, everything that moves on the trading floor:
    /// <list type="bullet">
    /// <item>the <b>carousel</b> — the offers of the page float as guild crates around the pit (resource cores in
    /// their colour, modules in miniature); point at one and its route lights on the star map, select it and it
    /// glides to you;</item>
    /// <item>the <b>cradle</b> between the desks — the crate lands, its lid lifts, the goods turn on their core, a
    /// card shows seller, price and hold, and two keys on the rim: buy (the counter opens the convoy composer) or
    /// put it back;</item>
    /// <item>the <b>star map</b> over the pit — our world at the heart, the sellers' stars around it in their true
    /// bearing (distance on a log scale), the route of the offer in hand as a living arc, and our convoys in
    /// flight as freighters creeping along their legs (outbound with the payment, home with the goods);</item>
    /// <item>the <b>launch</b> — on a dispatch, freighters lift off the emitter, climb the route and leave through
    /// the bay on space;</item>
    /// <item>the <b>consignment pad</b> by the counter — the goods of a sale being drafted materialise there
    /// (replicator sweep), and rise into the carousel when the offer is published (escrow); a withdrawn offer
    /// comes back down to it;</item>
    /// <item>the <b>quote ribbon</b> over the bay — the real offers scrolling.</item>
    /// </list>
    /// Pooled objects only (five crates, six freighters, eight map stars, eight convoy blips), shared materials,
    /// no allocation per frame except the ribbon's tick.
    /// </summary>
    public sealed class MarketPit : MonoBehaviour
    {
        public const int Slots = 5;
        const int MaxStars = 8;
        const int MaxBlips = 8;
        const int MaxFreighters = 6;
        const float CrateOrbit = 1.45f;
        const float CrateHeight = 1.62f;
        const float MapRadius = 1.35f;
        const float GlideTime = 0.9f;
        static readonly Vector3 MapCentre = MarketDecor.Pit + new Vector3(0f, 3.25f, 0f);
        /// <summary>The cradle between the desks (room local, floor), and where a crate rests on it.</summary>
        public static readonly Vector3 Cradle = new(0f, 0f, 1.2f);
        static readonly Vector3 CradleRest = Cradle + new Vector3(0f, 1.08f, 0f);
        /// <summary>The consignment pad, right of the counter desk.</summary>
        public static readonly Vector3 Pad = new(2.05f, 0f, -0.02f);
        static readonly Vector3 PadRest = Pad + new Vector3(0f, 1.02f, 0f);

        CicArtKit _art;
        Color _accent;
        Action<int> _onPick;
        Action _onBuy;
        Action _onPutBack;
        Camera _cam;

        // ── Carousel ──────────────────────────────────────────────────────────
        Transform _carousel;
        readonly Crate[] _crates = new Crate[Slots];
        readonly MarketListing[] _rows = new MarketListing[Slots];
        int _hover = -1;

        // ── Cradle ────────────────────────────────────────────────────────────
        Crate _held;
        float _glide = 1f;
        Vector3 _glideFrom;
        bool _returning;
        int _heldSlot = -1;
        MarketListing _heldListing;
        HoloScreen _card;
        TMP_Text _cardTitle;
        TMP_Text _cardBody;
        PokeButton _buyKey;
        PokeButton _backKey;
        Transform _cradleRing;

        // ── Star map ──────────────────────────────────────────────────────────
        Transform _map;
        int _homeSystem;
        readonly Transform[] _stars = new Transform[MaxStars];
        readonly TextMeshPro[] _starLabels = new TextMeshPro[MaxStars];
        readonly int[] _starSystems = new int[MaxStars];
        Transform _homeNode;
        TextMeshPro _homeLabel;
        LineRenderer _route;
        Transform _routePulse;
        int _routeSystem;
        float _scaleMax = 10f;
        readonly Blip[] _blips = new Blip[MaxBlips];
        readonly List<MarketMission> _missions = new();
        readonly Vector3[] _arc = new Vector3[24];

        // ── Launch ────────────────────────────────────────────────────────────
        readonly Freighter[] _freighters = new Freighter[MaxFreighters];

        // ── Consignment pad ───────────────────────────────────────────────────
        Crate _consign;
        float _consignBuild;
        int _consignMotion;
        float _consignT;
        Transform _padSweep;

        // ── Ribbon ────────────────────────────────────────────────────────────
        TextMeshPro _ribbon;
        string _ribbonText = string.Empty;
        int _ribbonAt;
        float _ribbonTick;
        const int RibbonWindow = 96;

        sealed class Crate
        {
            public Transform Root;
            public Transform Spin;
            public Transform Lid;
            public MeshRenderer Core;
            public MeshFilter CoreMesh;
            public MeshRenderer Band;
            public TextMeshPro Label;
            public BoxCollider Collider;
            public float Open;
        }

        sealed class Blip
        {
            public Transform Root;
            public MeshRenderer Body;
            public int Mission;
        }

        sealed class Freighter
        {
            public Transform Root;
            public Transform Streak;
            public float T = -1f;
            public float Delay;
            public Vector3 Lane;
        }

        public MarketListing Held => _heldListing;

        public static MarketPit Build(Transform room, CicArtKit art, Color accent, Action<int> onPick, Action onBuy, Action onPutBack)
        {
            var go = new GameObject("ExchangePit");
            go.transform.SetParent(room, false);
            var pit = go.AddComponent<MarketPit>();
            pit._art = art;
            pit._accent = accent;
            pit._onPick = onPick;
            pit._onBuy = onBuy;
            pit._onPutBack = onPutBack;
            pit.BuildCarousel();
            pit.BuildCradle();
            pit.BuildMap();
            pit.BuildFreighters();
            pit.BuildPad();
            pit.BuildRibbon();
            return pit;
        }

        // ══ Build ═══════════════════════════════════════════════════════════════

        Crate MakeCrate(Transform parent, string name, bool interactive, int slot)
        {
            var c = new Crate();
            c.Root = new GameObject(name).transform;
            c.Root.SetParent(parent, false);
            c.Spin = new GameObject("Spin").transform;
            c.Spin.SetParent(c.Root, false);
            var frame = _art.MetalPanel(0.5f);
            LatheMesh.Part(c.Spin, "Cage", CageMesh(), frame);
            // A dark floor plate: the goods stand on something, the cage stays open to the eye.
            var floor = LatheMesh.Part(c.Spin, "Floor", MeshBatch.Cube, _art.DarkPanel(0.35f));
            floor.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            floor.transform.localScale = new Vector3(0.43f, 0.02f, 0.43f);
            var band = LatheMesh.Part(c.Spin, "Band", BandMesh(), _art.CyanEmit(2f));
            band.transform.localPosition = new Vector3(0f, -0.12f, 0f);
            c.Band = band.GetComponent<MeshRenderer>();
            // The lid: a plate with a lit rim, hinged at the back edge (opens on the cradle).
            var hinge = new GameObject("LidHinge").transform;
            hinge.SetParent(c.Spin, false);
            hinge.localPosition = new Vector3(0f, 0.225f, 0.225f);
            var lid = LatheMesh.Part(hinge, "Lid", MeshBatch.Cube, frame);
            lid.transform.localPosition = new Vector3(0f, 0.012f, -0.225f);
            lid.transform.localScale = new Vector3(0.47f, 0.024f, 0.47f);
            var rim = LatheMesh.Part(hinge, "LidRim", MeshBatch.Cube, _art.Lit(Texture2D.whiteTexture, _accent, 2.4f));
            rim.transform.localPosition = new Vector3(0f, 0.026f, -0.225f);
            rim.transform.localScale = new Vector3(0.26f, 0.008f, 0.03f);
            c.Lid = hinge;
            var core = LatheMesh.Part(c.Spin, "Core", CoreMesh(), _art.CyanEmit(2f));
            c.Core = core.GetComponent<MeshRenderer>();
            c.CoreMesh = core.GetComponent<MeshFilter>();
            c.Label = UiKit.Label(c.Root, "Label", string.Empty, new Vector3(0f, 0.44f, 0f), 0.9f, 0.065f, UiKit.TextBright);
            c.Label.richText = true;
            if (interactive)
            {
                c.Collider = c.Root.gameObject.AddComponent<BoxCollider>();
                c.Collider.size = Vector3.one * 0.5f;
                var xi = c.Root.gameObject.AddComponent<XRSimpleInteractable>();
                xi.selectEntered.AddListener(_ => Pick(slot));
                xi.hoverEntered.AddListener(_ =>
                {
                    _hover = slot;
                    CicCue.Hover(c.Root.position);
                });
                xi.hoverExited.AddListener(_ =>
                {
                    if (_hover == slot)
                        _hover = -1;
                });
            }

            c.Root.gameObject.SetActive(false);
            return c;
        }

        void BuildCarousel()
        {
            _carousel = new GameObject("OfferCarousel").transform;
            _carousel.SetParent(transform, false);
            _carousel.localPosition = MarketDecor.Pit + Vector3.up * CrateHeight;
            for (var i = 0; i < Slots; i++)
                _crates[i] = MakeCrate(_carousel, "Crate" + i, true, i);
        }

        void BuildCradle()
        {
            var root = new GameObject("Cradle").transform;
            root.SetParent(transform, false);
            root.localPosition = Cradle;
            var b = new MeshBatch();
            var metal = _art.MetalPanel(0.45f);
            var dark = _art.DarkPanel(0.3f);
            var glow = _art.Lit(Texture2D.whiteTexture, _accent, 2.4f);
            b.Tube(new Vector3(0f, 0.42f, 0f), Vector3.up, 0.09f, 0.84f, dark);
            b.Tube(new Vector3(0f, 0.04f, 0f), Vector3.up, 0.26f, 0.08f, metal);
            b.Tube(new Vector3(0f, 0.86f, 0f), Vector3.up, 0.2f, 0.05f, metal);
            // Three cradle arms rising to the crate's lower corners.
            for (var i = 0; i < 3; i++)
            {
                var d = LatheMesh.Dir(i * 120f + 60f);
                b.Strut(new Vector3(0f, 0.86f, 0f) + d * 0.12f, new Vector3(0f, 0.86f, 0f) + d * 0.3f + Vector3.up * 0.06f, 0.03f, metal);
                b.Box(new Vector3(0f, 0.92f, 0f) + d * 0.3f, new Vector3(0.05f, 0.03f, 0.05f), glow);
            }

            b.Build(root, "CradleBody");
            var ring = new LatheMesh(Vector3.zero);
            ring.Revolve(new[] { new Vector2(0.22f, 0.885f), new Vector2(0.27f, 0.885f) }, 0f, 360f, false);
            _cradleRing = LatheMesh.Part(root, "CradleRing", ring.ToMesh("SU_MarketCradleRing"), glow).transform;

            // Keys on the rim, facing the stand (button faces are local −z).
            _buyKey = PokeButton.Create(root, "BuyKey", Trans.Get("market_buy_btn"), new Vector3(0.24f, 0.78f, -0.3f),
                Quaternion.Euler(-30f, 0f, 0f), new Vector2(0.24f, 0.075f), _accent, () => _onBuy?.Invoke());
            _backKey = PokeButton.Create(root, "BackKey", Trans.Get("back"), new Vector3(-0.24f, 0.78f, -0.3f),
                Quaternion.Euler(-30f, 0f, 0f), new Vector2(0.18f, 0.075f), UiKit.Cyan, PutBack);
            _buyKey.gameObject.SetActive(false);
            _backKey.gameObject.SetActive(false);

            // The offer's card, low over the crate so the pit stays in sight beyond it.
            _card = HoloScreen.Create(transform, "OfferCard", new Vector2(0.64f, 0.26f), Cradle + new Vector3(0f, 1.47f, 0.05f),
                Quaternion.Euler(8f, 0f, 0f), string.Empty);
            _card.SetAccent(_accent, 0.5f);
            _cardTitle = ScreenKit.Line(_card.Content, string.Empty, 0f, 80f, 26f, UiKit.TextBright, 600f, TextAlignmentOptions.Center);
            _cardBody = ScreenKit.Para(_card.Content, string.Empty, 0f, -25f, 19f, UiKit.TextDim, 600f, 150f, TextAlignmentOptions.Top);
            _card.gameObject.SetActive(false);
        }

        void BuildMap()
        {
            _map = new GameObject("StarMap").transform;
            _map.SetParent(transform, false);
            _map.localPosition = MapCentre;
            // Leaning toward the stand: read as a chart, not edge-on.
            _map.localRotation = Quaternion.Euler(-25f, 0f, 0f);
            var plate = GateRoomDecor.Quad(_map, "MapPlate", Vector3.zero, Vector3.one * MapRadius * 2.2f,
                _art.HoloDetail(_art.OrbitPlate != null ? _art.OrbitPlate : _art.HoloPlate, new Color(_accent.r, _accent.g, _accent.b, 0.4f), 0.14f));
            plate.transform.localRotation = Quaternion.identity;
            var ringTex = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;
            var ring = GateRoomDecor.Quad(_map, "MapRing", new Vector3(0f, 0f, -0.002f), Vector3.one * MapRadius * 2.05f,
                _art.RadarIcon(ringTex, new Color(_accent.r, _accent.g, _accent.b, 0.22f)));
            ring.transform.localRotation = Quaternion.identity;

            var home = _art.Lit(Texture2D.whiteTexture, _accent, 3f);
            _homeNode = LatheMesh.Part(_map, "Home", CoreMesh(), home).transform;
            _homeNode.localScale = Vector3.one * 0.5f;
            _homeLabel = UiKit.Label(_map, "HomeName", string.Empty, new Vector3(0f, -0.14f, -0.02f), 0.6f, 0.06f, _accent);
            var amber = _art.Lit(Texture2D.whiteTexture, UiKit.Amber, 2.6f);
            for (var i = 0; i < MaxStars; i++)
            {
                _stars[i] = LatheMesh.Part(_map, "Seller" + i, CoreMesh(), amber).transform;
                _stars[i].localScale = Vector3.one * 0.3f;
                _starLabels[i] = UiKit.Label(_map, "SellerName" + i, string.Empty, Vector3.zero, 0.6f, 0.055f, UiKit.Amber);
                _stars[i].gameObject.SetActive(false);
                _starLabels[i].gameObject.SetActive(false);
            }

            _route = new GameObject("Route").AddComponent<LineRenderer>();
            _route.transform.SetParent(_map, false);
            _route.useWorldSpace = false;
            _route.positionCount = _arc.Length;
            _route.widthMultiplier = 0.018f;
            _route.numCapVertices = 2;
            _route.sharedMaterial = _art.Lit(Texture2D.whiteTexture, UiKit.Amber, 3f);
            _route.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _route.enabled = false;
            _routePulse = LatheMesh.Part(_map, "RoutePulse", MeshBatch.Sphere, _art.Lit(Texture2D.whiteTexture, Color.white, 3f)).transform;
            _routePulse.localScale = Vector3.one * 0.05f;
            _routePulse.gameObject.SetActive(false);

            var blip = _art.Lit(Texture2D.whiteTexture, UiKit.Cyan, 2.8f);
            for (var i = 0; i < MaxBlips; i++)
            {
                var root = new GameObject("Convoy" + i).transform;
                root.SetParent(_map, false);
                var body = LatheMesh.Part(root, "Hull", ChevronMesh(), blip);
                body.transform.localScale = Vector3.one * 0.11f;
                _blips[i] = new Blip { Root = root, Body = body.GetComponent<MeshRenderer>() };
                root.gameObject.SetActive(false);
            }
        }

        void BuildFreighters()
        {
            var hull = FreighterMesh();
            var streakMat = _art.Lit(Texture2D.whiteTexture, _accent, 3.2f);
            var engine = _art.Lit(Texture2D.whiteTexture, UiKit.Amber, 3.4f);
            for (var i = 0; i < MaxFreighters; i++)
            {
                var root = new GameObject("Freighter" + i).transform;
                root.SetParent(transform, false);
                var body = LatheMesh.Part(root, "Hull", hull != null ? hull : MeshBatch.Cube, ModuleShelves.BlockMat());
                var size = hull != null ? hull.bounds.size : Vector3.one;
                var s = 0.42f / Mathf.Max(0.01f, Mathf.Max(size.x, size.z));
                body.transform.localScale = Vector3.one * s;
                body.transform.localPosition = hull != null ? -hull.bounds.center * s : Vector3.zero;
                var glow = LatheMesh.Part(root, "Engine", MeshBatch.Cube, engine);
                glow.transform.localPosition = new Vector3(0f, 0f, -0.2f);
                glow.transform.localScale = new Vector3(0.12f, 0.05f, 0.03f);
                var streak = LatheMesh.Part(root, "Streak", MeshBatch.Cube, streakMat).transform;
                streak.localScale = new Vector3(0.03f, 0.03f, 0.01f);
                _freighters[i] = new Freighter { Root = root, Streak = streak };
                root.gameObject.SetActive(false);
            }
        }

        void BuildPad()
        {
            var root = new GameObject("ConsignmentPad").transform;
            root.SetParent(transform, false);
            root.localPosition = Pad;
            var lathe = new LatheMesh(Vector3.zero);
            lathe.Revolve(new[]
            {
                new Vector2(0.4f, 0f), new Vector2(0.4f, 0.06f), new Vector2(0.24f, 0.12f), new Vector2(0.16f, 0.68f),
                new Vector2(0.3f, 0.74f), new Vector2(0.3f, 0.78f), new Vector2(0.12f, 0.8f)
            }, 0f, 360f, false);
            LatheMesh.Part(root, "PadBody", lathe.ToMesh("SU_MarketPad"), _art.MetalPanel(0.45f));
            var glow = new LatheMesh(Vector3.zero);
            glow.Revolve(new[] { new Vector2(0.12f, 0.805f), new Vector2(0.29f, 0.785f) }, 0f, 360f, false);
            glow.Revolve(new[] { new Vector2(0.4f, 0.03f), new Vector2(0.405f, 0.05f) }, 0f, 360f, false);
            LatheMesh.Part(root, "PadGlow", glow.ToMesh("SU_MarketPadGlow"), _art.Lit(Texture2D.whiteTexture, _accent, 2.6f));
            // The pad's plate, on the pillar's face toward the stand.
            var toPad = new Vector3(Pad.x, 0f, Pad.z).normalized;
            var label = UiKit.Label(root, "PadName", Trans.Get("market_escrow_title"), -toPad * 0.22f + Vector3.up * 0.45f, 0.42f, 0.04f, _accent);
            label.transform.localRotation = Quaternion.LookRotation(toPad, Vector3.up);
            // The replicator's scan plane, sweeping up through the crate as it forms.
            _padSweep = LatheMesh.Part(transform, "PadSweep", MeshBatch.Cube, _art.Lit(Texture2D.whiteTexture, _accent, 3.4f)).transform;
            _padSweep.localScale = new Vector3(0.52f, 0.006f, 0.52f);
            _padSweep.gameObject.SetActive(false);
            _consign = MakeCrate(transform, "Consignment", false, -1);
        }

        void BuildRibbon()
        {
            var y = 5.02f;
            _ribbon = UiKit.Label(transform, "QuoteRibbon", string.Empty, new Vector3(0f, y, MarketDecor.Depth - 0.16f), 11.4f, 0.22f,
                new Color(0.75f, 1f, 0.85f, 1f));
            _ribbon.richText = true;
            _ribbon.enableAutoSizing = false;
            _ribbon.fontSize = 0.22f * 100f * 10f;
        }

        // ══ Shared meshes ═══════════════════════════════════════════════════════

        static Mesh _cage;
        static Mesh _core;
        static Mesh _chevron;
        static Mesh _band;

        /// <summary>The cargo-colour girdle round the crate: four lit straps on its faces, open inside.</summary>
        static Mesh BandMesh()
        {
            if (_band != null)
                return _band;
            const float h = 0.228f;
            var parts = new List<CombineInstance>();
            for (var i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0f, i * 90f, 0f);
                parts.Add(new CombineInstance
                {
                    mesh = MeshBatch.Cube,
                    transform = Matrix4x4.TRS(rot * new Vector3(0f, 0f, h), rot, new Vector3(2f * h, 0.024f, 0.01f))
                });
            }

            _band = new Mesh { name = "SU_MarketCrateBand" };
            _band.CombineMeshes(parts.ToArray(), true, true);
            _band.RecalculateBounds();
            return _band;
        }
        static Mesh _freighter;

        /// <summary>
        /// A guild freighter in miniature, from real module silhouettes: a core with a cargo hold on each flank, a
        /// cannon at the bow and a drive at the stern (vertex-coloured, one mesh, SU/ModuleBlock).
        /// </summary>
        static Mesh FreighterMesh()
        {
            if (_freighter != null)
                return _freighter;
            var core = ShipHullBuilder.ModuleMesh("ShipCore");
            if (core == null)
                return null;
            var cell = Mathf.Max(core.bounds.size.x, core.bounds.size.z);
            var parts = new List<CombineInstance>();
            void Put(string type, Vector3 at)
            {
                var m = ShipHullBuilder.ModuleMesh(type);
                if (m != null)
                    parts.Add(new CombineInstance { mesh = m, transform = Matrix4x4.Translate(at * cell) });
            }

            Put("ShipCore", Vector3.zero);
            Put("CargoHold", Vector3.left);
            Put("CargoHold", Vector3.right);
            Put("LaserCannon", Vector3.forward);
            Put("FusionThruster", Vector3.back);
            _freighter = new Mesh { name = "SU_MarketFreighter" };
            _freighter.CombineMeshes(parts.ToArray(), true, true);
            _freighter.RecalculateBounds();
            return _freighter;
        }

        static Mesh CageMesh()
        {
            if (_cage != null)
                return _cage;
            const float h = 0.22f;
            const float t = 0.03f;
            var parts = new List<CombineInstance>();
            void Bar(Vector3 c, Vector3 s) => parts.Add(new CombineInstance { mesh = MeshBatch.Cube, transform = Matrix4x4.TRS(c, Quaternion.identity, s) });
            for (var a = -1; a <= 1; a += 2)
                for (var b = -1; b <= 1; b += 2)
                {
                    Bar(new Vector3(0f, a * h, b * h), new Vector3(2f * h + t, t, t));
                    Bar(new Vector3(a * h, 0f, b * h), new Vector3(t, 2f * h + t, t));
                    Bar(new Vector3(a * h, b * h, 0f), new Vector3(t, t, 2f * h + t));
                }

            _cage = new Mesh { name = "SU_MarketCrateCage" };
            _cage.CombineMeshes(parts.ToArray(), true, true);
            _cage.RecalculateBounds();
            return _cage;
        }

        /// <summary>A cut resource core: a stretched octahedron, flat-shaded.</summary>
        static Mesh CoreMesh()
        {
            if (_core != null)
                return _core;
            _core = Faceted("SU_MarketCore", tris =>
            {
                var top = new Vector3(0f, 0.17f, 0f);
                var bottom = new Vector3(0f, -0.17f, 0f);
                for (var i = 0; i < 8; i++)
                {
                    var a = LatheMesh.Dir(i * 45f) * 0.11f;
                    var b = LatheMesh.Dir((i + 1) * 45f) * 0.11f;
                    tris.Add((top, b, a));
                    tris.Add((bottom, a, b));
                }
            });
            return _core;
        }

        /// <summary>A freighter blip on the star map: an arrowhead in the map plane, nose +y.</summary>
        static Mesh ChevronMesh()
        {
            if (_chevron != null)
                return _chevron;
            _chevron = Faceted("SU_MarketChevron", tris =>
            {
                var nose = new Vector3(0f, 0.5f, 0f);
                var l = new Vector3(-0.35f, -0.4f, 0f);
                var r = new Vector3(0.35f, -0.4f, 0f);
                var tail = new Vector3(0f, -0.2f, 0f);
                var up = new Vector3(0f, 0f, -0.12f);
                tris.Add((nose, l, tail + up));
                tris.Add((nose, tail + up, r));
                tris.Add((nose, tail - up, l));
                tris.Add((nose, r, tail - up));
            });
            return _chevron;
        }

        static Mesh Faceted(string name, Action<List<(Vector3, Vector3, Vector3)>> fill)
        {
            var tris = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            fill(tris);
            var verts = new List<Vector3>();
            var idx = new List<int>();
            foreach (var (a, b, c) in tris)
            {
                idx.Add(verts.Count);
                verts.Add(a);
                idx.Add(verts.Count);
                verts.Add(b);
                idx.Add(verts.Count);
                verts.Add(c);
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(idx, 0);
            var white = new Color[verts.Count];
            for (var i = 0; i < white.Length; i++)
                white[i] = Color.white;
            mesh.colors = white;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ══ Data in ═════════════════════════════════════════════════════════════

        /// <summary>Dress one crate as an offer (or as the goods of a sale being drafted).</summary>
        void Dress(Crate c, string category, string itemKey, string title, string caption)
        {
            var color = MarketDecor.CategoryColor(category, itemKey);
            var lit = _art.Lit(Texture2D.whiteTexture, color, 2.4f);
            c.Band.sharedMaterial = lit;
            if (category == "module")
            {
                var mesh = ShipHullBuilder.ModuleMesh(itemKey);
                c.CoreMesh.sharedMesh = mesh != null ? mesh : CoreMesh();
                c.Core.sharedMaterial = mesh != null ? ModuleShelves.BlockMat() : lit;
                var size = mesh != null ? mesh.bounds.size : Vector3.one * 0.34f;
                var s = mesh != null ? 0.3f / Mathf.Max(0.01f, Mathf.Max(size.x, Mathf.Max(size.y, size.z))) : 1f;
                c.Core.transform.localScale = Vector3.one * s;
                c.Core.transform.localPosition = mesh != null ? -mesh.bounds.center * s : Vector3.zero;
            }
            else
            {
                c.CoreMesh.sharedMesh = CoreMesh();
                c.Core.sharedMaterial = lit;
                c.Core.transform.localScale = Vector3.one;
                c.Core.transform.localPosition = Vector3.zero;
            }

            c.Label.text = "<b>" + title + "</b>" + (string.IsNullOrEmpty(caption) ? string.Empty : "\n" + caption);
        }

        static string PriceText(MarketListing l) =>
            "<color=" + ScreenKit.Hex(UiKit.Amber) + ">" + ScreenKit.Num(l.PriceAmount) + " " + Trans.Get(MarketService.ResourceKey(l.PriceCurrency)) + "</color>";

        /// <summary>The page's offers onto the carousel (the one in the cradle stays there).</summary>
        public void ShowOffers(IReadOnlyList<MarketListing> rows)
        {
            for (var i = 0; i < Slots; i++)
            {
                var l = rows != null && i < rows.Count ? rows[i] : null;
                _rows[i] = l;
                var c = _crates[i];
                if (l == null)
                {
                    if (_held != c)
                        c.Root.gameObject.SetActive(false);
                    continue;
                }

                Dress(c, l.Category, l.ItemKey, ScreenKit.Verbatim(l.ItemName) + " ×" + ScreenKit.Num(l.Quantity), PriceText(l));
                c.Root.gameObject.SetActive(true);
            }

            if (_held != null && _heldSlot >= 0 && (_rows[_heldSlot] == null || _heldListing == null || _rows[_heldSlot].Id != _heldListing.Id))
                Release(false);
            PlaceSellers();
        }

        /// <summary>Our world's system: the star map's heart, the distances' origin.</summary>
        public void SetHome(int systemId, string worldName)
        {
            _homeSystem = systemId;
            _homeLabel.text = "<b>" + ScreenKit.Verbatim(worldName) + "</b>";
            PlaceSellers();
        }

        public void SetMissions(IReadOnlyList<MarketMission> missions)
        {
            _missions.Clear();
            if (missions != null)
                _missions.AddRange(missions);
            for (var i = 0; i < MaxBlips; i++)
            {
                var b = _blips[i];
                var m = i < _missions.Count ? _missions[i] : null;
                b.Mission = m != null ? m.Id : 0;
                b.Root.gameObject.SetActive(m != null);
                if (m != null)
                    b.Body.sharedMaterial = _art.Lit(Texture2D.whiteTexture,
                        m.Status == "intercepted" ? UiKit.Danger : m.Status == "traveling_to" ? UiKit.Cyan : UiKit.Ok, 2.8f);
            }

            PlaceSellers();
        }

        /// <summary>Map position of a system: true bearing from home, log-scaled distance.</summary>
        Vector3 MapPoint(int systemId)
        {
            if (systemId == _homeSystem || !GalaxyCatalog.TryGet(systemId, out var s) || !GalaxyCatalog.TryGet(_homeSystem, out var home))
                return Vector3.zero;
            var d = new Vector2(s.X - home.X, s.Y - home.Y);
            var len = d.magnitude;
            if (len < 1e-3f)
                return Vector3.zero;
            var r = MapRadius * 0.92f * Mathf.Log(1f + len) / Mathf.Log(1f + _scaleMax);
            var p = d / len * Mathf.Min(r, MapRadius * 0.95f);
            return new Vector3(p.x, p.y, 0f);
        }

        void PlaceSellers()
        {
            // Scale to the farthest star shown (offers and convoys), at least 10 ly.
            _scaleMax = 10f;
            if (GalaxyCatalog.TryGet(_homeSystem, out var home))
            {
                void Reach(int system)
                {
                    if (GalaxyCatalog.TryGet(system, out var s))
                        _scaleMax = Mathf.Max(_scaleMax, new Vector2(s.X - home.X, s.Y - home.Y).magnitude);
                }

                foreach (var l in _rows)
                    if (l != null)
                        Reach(l.SystemId);
                foreach (var m in _missions)
                    Reach(m.TargetSystemId);
            }

            var n = 0;
            void Star(int system, string name)
            {
                if (system <= 0 || system == _homeSystem || n >= MaxStars)
                    return;
                for (var k = 0; k < n; k++)
                    if (_starSystems[k] == system)
                        return;
                _starSystems[n] = system;
                var at = MapPoint(system);
                _stars[n].localPosition = at;
                _starLabels[n].transform.localPosition = at + new Vector3(0f, -0.11f, -0.02f);
                _starLabels[n].text = ScreenKit.Verbatim(name);
                _stars[n].gameObject.SetActive(true);
                _starLabels[n].gameObject.SetActive(true);
                n++;
            }

            foreach (var l in _rows)
                if (l != null)
                    Star(l.SystemId, l.PlanetName);
            foreach (var m in _missions)
                Star(m.TargetSystemId, m.TargetName);
            for (var k = n; k < MaxStars; k++)
            {
                _starSystems[k] = 0;
                _stars[k].gameObject.SetActive(false);
                _starLabels[k].gameObject.SetActive(false);
            }
        }

        /// <summary>A quadratic arc from a to b bulging out of the map toward the viewer.</summary>
        void Arc(Vector3 a, Vector3 b)
        {
            var mid = (a + b) * 0.5f + new Vector3(0f, 0f, -0.18f - Vector3.Distance(a, b) * 0.2f);
            for (var i = 0; i < _arc.Length; i++)
            {
                var t = i / (float)(_arc.Length - 1);
                _arc[i] = Vector3.Lerp(Vector3.Lerp(a, mid, t), Vector3.Lerp(mid, b, t), t);
            }
        }

        static Vector3 OnArc(Vector3 a, Vector3 b, float t)
        {
            var mid = (a + b) * 0.5f + new Vector3(0f, 0f, -0.18f - Vector3.Distance(a, b) * 0.2f);
            return Vector3.Lerp(Vector3.Lerp(a, mid, t), Vector3.Lerp(mid, b, t), t);
        }

        public void SetRibbon(IReadOnlyList<MarketListing> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                _ribbonText = Trans.Get("market_subtitle") + "   ·   ";
            }
            else
            {
                var sb = new StringBuilder();
                foreach (var l in rows)
                {
                    sb.Append(l.ItemName).Append(" ×").Append(ScreenKit.Num(l.Quantity)).Append("  ")
                        .Append(ScreenKit.Num(l.PriceAmount)).Append(' ').Append(Trans.Get(MarketService.ResourceKey(l.PriceCurrency)))
                        .Append("  · ").Append(l.PlanetName).Append("   ·   ");
                }

                _ribbonText = sb.ToString();
            }

            while (_ribbonText.Length < RibbonWindow)
                _ribbonText += _ribbonText;
            _ribbonAt = 0;
            TickRibbon();
        }

        void TickRibbon()
        {
            if (string.IsNullOrEmpty(_ribbonText))
                return;
            _ribbonAt = (_ribbonAt + 1) % _ribbonText.Length;
            var end = _ribbonAt + RibbonWindow;
            var s = end <= _ribbonText.Length
                ? _ribbonText.Substring(_ribbonAt, RibbonWindow)
                : _ribbonText.Substring(_ribbonAt) + _ribbonText.Substring(0, end - _ribbonText.Length);
            _ribbon.text = "<noparse>" + s + "</noparse>";
        }

        // ══ Interaction ═════════════════════════════════════════════════════════

        void Pick(int slot)
        {
            var l = _rows[slot];
            if (l == null)
                return;
            _onPick?.Invoke(l.Id);
        }

        /// <summary>Bring an offer's crate to the cradle (null: send the one there back).</summary>
        public void Inspect(MarketListing l, bool ours)
        {
            if (l == null)
            {
                Release(true);
                return;
            }

            var slot = -1;
            for (var i = 0; i < Slots; i++)
                if (_rows[i] != null && _rows[i].Id == l.Id)
                    slot = i;
            if (slot < 0)
                return;
            if (_held != null && _heldSlot != slot)
                Release(false);
            _held = _crates[slot];
            _heldSlot = slot;
            _heldListing = l;
            _returning = false;
            _glideFrom = transform.InverseTransformPoint(_held.Root.position);
            _held.Root.SetParent(transform, true);
            _glide = 0f;
            CicCue.Teleport(_held.Root.position);

            _cardTitle.text = "<b><color=" + ScreenKit.Hex(MarketDecor.CategoryColor(l.Category, l.ItemKey)) + ">" + ScreenKit.Verbatim(l.ItemName) +
                              "</color></b> ×" + ScreenKit.Num(l.Quantity);
            _cardBody.text = Trans.Get("market_seller") + " : " + ScreenKit.Verbatim(l.SellerName) +
                             (string.IsNullOrEmpty(l.EmpireName) ? string.Empty : " · " + ScreenKit.Verbatim(l.EmpireName)) + "\n" +
                             ScreenKit.Verbatim(l.PlanetName) + " · " + ScreenKit.Num(l.Distance, 1) + " " + Trans.Get("market_ly") + "\n" +
                             Trans.Get("market_price") + " : " + PriceText(l) + "   " +
                             ScreenKit.Num(Mathf.Max(l.CargoVolume, Mathf.Ceil(l.PriceAmount))) + " m³";
            _card.gameObject.SetActive(true);
            _buyKey.gameObject.SetActive(!ours);
            _backKey.gameObject.SetActive(true);
            _routeSystem = l.SystemId;
        }

        void PutBack()
        {
            CicCue.Ok(_cradleRing.position);
            _onPutBack?.Invoke();
        }

        /// <summary>The crate leaves the cradle: back to its slot (animated) or straight there.</summary>
        void Release(bool animated)
        {
            if (_held == null)
                return;
            var c = _held;
            _card.gameObject.SetActive(false);
            _buyKey.gameObject.SetActive(false);
            _backKey.gameObject.SetActive(false);
            _routeSystem = 0;
            _heldListing = null;
            if (animated)
            {
                _returning = true;
                _glideFrom = transform.InverseTransformPoint(c.Root.position);
                _glide = 0f;
                return;
            }

            Seat(c, _heldSlot);
            _held = null;
            _heldSlot = -1;
        }

        void Seat(Crate c, int slot)
        {
            c.Root.SetParent(_carousel, false);
            c.Root.localPosition = LatheMesh.Dir(slot * 360f / Slots) * CrateOrbit;
            c.Root.localRotation = Quaternion.identity;
            c.Open = 0f;
            c.Lid.localRotation = Quaternion.identity;
            c.Root.gameObject.SetActive(_rows[slot] != null);
        }

        /// <summary>The bought crate dissolves off the cradle and the convoy lifts off toward the seller.</summary>
        public void Launch(int ships)
        {
            if (_held != null)
            {
                var c = _held;
                Release(false);
                c.Root.gameObject.SetActive(false);
            }

            var count = Mathf.Clamp(ships, 1, MaxFreighters);
            for (var i = 0; i < count; i++)
            {
                var f = _freighters[i];
                f.T = 0f;
                f.Delay = i * 0.35f;
                f.Lane = new Vector3((i - (count - 1) * 0.5f) * 0.9f, i % 2 * 0.35f, 0f);
                f.Root.gameObject.SetActive(false);
            }

            CicCue.Dispatch(transform.TransformPoint(MarketDecor.Pit + Vector3.up));
        }

        /// <summary>The goods of a sale being drafted, on the pad (null category: the pad is empty).</summary>
        public void Consign(string category, string itemKey, string title, string caption)
        {
            if (string.IsNullOrEmpty(category))
            {
                if (_consignMotion == 0)
                    _consign.Root.gameObject.SetActive(false);
                return;
            }

            var fresh = !_consign.Root.gameObject.activeSelf || _consignMotion != 0;
            Dress(_consign, category, itemKey, title, caption);
            if (fresh)
            {
                _consignMotion = 0;
                _consign.Root.localPosition = PadRest;
                _consign.Root.localRotation = Quaternion.identity;
                _consignBuild = 0f;
                _consign.Root.gameObject.SetActive(true);
            }
        }

        /// <summary>Published: the escrowed goods rise off the pad into the exchange.</summary>
        public void ConsignPublished()
        {
            if (!_consign.Root.gameObject.activeSelf)
                return;
            _consignMotion = 1;
            _consignT = 0f;
            CicCue.Success(_consign.Root.position);
        }

        /// <summary>Withdrawn: the goods come down from the exchange onto the pad, then home to the planet's stock.</summary>
        public void Recall(string category, string itemKey, string title)
        {
            Dress(_consign, category, itemKey, title, string.Empty);
            _consign.Root.gameObject.SetActive(true);
            _consignMotion = 2;
            _consignT = 0f;
        }

        public void Hide()
        {
            Release(false);
            foreach (var f in _freighters)
            {
                f.T = -1f;
                f.Root.gameObject.SetActive(false);
            }

            _consignMotion = 0;
            _consign.Root.gameObject.SetActive(false);
            _padSweep.gameObject.SetActive(false);
        }

        // ══ Animation ═══════════════════════════════════════════════════════════

        void Update() => Tick(Time.time, Time.deltaTime);

#if UNITY_EDITOR
        /// <summary>Verification: run the animations for <paramref name="seconds"/> on a virtual clock (edit mode has no Update).</summary>
        public void EditorStep(float from, float seconds, Camera eye)
        {
            _cam = eye;
            for (var t = 0f; t < seconds; t += 1f / 30f)
                Tick(from + t, 1f / 30f);
        }

        /// <summary>Verification: point at a carousel slot (what an XR hover does).</summary>
        public void EditorHover(int slot) => _hover = slot;
#endif

        void Tick(float t, float dt)
        {
            if (_cam == null)
                _cam = Camera.main;

            // Carousel: slow turn, crates bob and spin, the pointed one swells.
            _carousel.localRotation = Quaternion.Euler(0f, t * 6f, 0f);
            for (var i = 0; i < Slots; i++)
            {
                var c = _crates[i];
                if (c == _held || !c.Root.gameObject.activeSelf)
                    continue;
                var hot = i == _hover;
                c.Root.localPosition = LatheMesh.Dir(i * 360f / Slots) * CrateOrbit + Vector3.up * (Mathf.Sin(t * 1.3f + i * 1.7f) * 0.03f);
                c.Spin.localRotation = Quaternion.Euler(0f, t * (hot ? 45f : 14f) + i * 40f, 0f);
                c.Spin.localScale = Vector3.one * Mathf.MoveTowards(c.Spin.localScale.x, hot ? 1.22f : 1f, dt * 2f);
                Face(c.Label.transform);
            }

            AnimateHeld(t, dt);
            AnimateMap(t);
            AnimateFreighters(dt);
            AnimateConsign(t, dt);
            _cradleRing.localRotation = Quaternion.Euler(0f, t * 30f, 0f);

            if (t >= _ribbonTick)
            {
                _ribbonTick = t + 0.16f;
                TickRibbon();
            }
        }

        void Face(Transform label)
        {
            if (_cam != null)
                label.rotation = Quaternion.LookRotation(label.position - _cam.transform.position, Vector3.up);
        }

        void AnimateHeld(float t, float dt)
        {
            if (_held == null)
                return;
            var c = _held;
            if (_glide < 1f)
            {
                _glide = Mathf.Min(1f, _glide + dt / GlideTime);
                var e = MotionEase.Smooth01(_glide);
                var to = _returning
                    ? transform.InverseTransformPoint(_carousel.TransformPoint(LatheMesh.Dir(_heldSlot * 360f / Slots) * CrateOrbit))
                    : CradleRest;
                // A lifted arc: up and over, never through the desks.
                var lift = Mathf.Sin(e * Mathf.PI) * 0.5f;
                c.Root.localPosition = Vector3.Lerp(_glideFrom, to, e) + Vector3.up * lift;
                if (_glide >= 1f && _returning)
                {
                    Seat(c, _heldSlot);
                    _held = null;
                    _heldSlot = -1;
                    _returning = false;
                    return;
                }
            }

            // On the cradle the lid opens and the goods turn; heading home it closes.
            var open = !_returning && _glide >= 1f;
            c.Open = Mathf.MoveTowards(c.Open, open ? 1f : 0f, dt * 1.6f);
            c.Lid.localRotation = Quaternion.Euler(105f * MotionEase.Smooth01(c.Open), 0f, 0f);
            c.Spin.localRotation = Quaternion.Euler(0f, t * 22f, 0f);
            c.Spin.localScale = Vector3.one * Mathf.Lerp(1f, 1.15f, c.Open);
            c.Label.gameObject.SetActive(!open);
            Face(c.Label.transform);
        }

        void AnimateMap(float t)
        {
            _homeNode.localRotation = Quaternion.Euler(0f, t * 40f, 0f);
            for (var i = 0; i < MaxStars; i++)
                if (_stars[i].gameObject.activeSelf)
                    _stars[i].localRotation = Quaternion.Euler(0f, t * 25f + i * 30f, 0f);

            // The route of the offer in hand (or the one pointed at on the carousel).
            var system = _routeSystem;
            if (system <= 0 && _hover >= 0 && _rows[_hover] != null)
                system = _rows[_hover].SystemId;
            var show = system > 0 && system != _homeSystem && GalaxyCatalog.TryGet(system, out _);
            _route.enabled = show;
            _routePulse.gameObject.SetActive(show);
            if (show)
            {
                var b = MapPoint(system);
                Arc(Vector3.zero, b);
                _route.SetPositions(_arc);
                _routePulse.localPosition = OnArc(Vector3.zero, b, t * 0.45f % 1f);
            }

            // Convoys in flight: outbound home → seller, inbound seller → home, by their clock.
            var now = FleetOrderGate.UnixNow();
            for (var i = 0; i < MaxBlips; i++)
            {
                var blip = _blips[i];
                if (!blip.Root.gameObject.activeSelf)
                    continue;
                var m = i < _missions.Count ? _missions[i] : null;
                if (m == null)
                    continue;
                var far = MapPoint(m.TargetSystemId);
                float u;
                Vector3 a, b;
                if (m.Status == "traveling_to")
                {
                    a = Vector3.zero;
                    b = far;
                    u = Progress(m.DepartureTime, m.OutboundArrival, now);
                }
                else
                {
                    a = far;
                    b = Vector3.zero;
                    u = m.Status == "intercepted" ? 0.5f : Progress(m.OutboundArrival, m.InboundArrival, now);
                }

                var p = OnArc(a, b, u);
                var ahead = OnArc(a, b, Mathf.Min(1f, u + 0.02f));
                blip.Root.localPosition = p;
                var dir = ahead - p;
                if (dir.sqrMagnitude > 1e-8f)
                    blip.Root.localRotation = Quaternion.LookRotation(Vector3.forward, dir.normalized);
                if (m.Status == "intercepted")
                    blip.Root.localScale = Vector3.one * (0.8f + Mathf.PingPong(t * 3f, 0.5f));
            }
        }

        static float Progress(long from, long to, long now) =>
            to > from ? Mathf.Clamp01((now - from) / (float)(to - from)) : 0f;

        void AnimateFreighters(float dt)
        {
            var emitter = MarketDecor.Pit + Vector3.up * 1f;
            for (var i = 0; i < MaxFreighters; i++)
            {
                var f = _freighters[i];
                if (f.T < 0f)
                    continue;
                if (f.Delay > 0f)
                {
                    f.Delay -= dt;
                    continue;
                }

                f.T += dt / 4.2f;
                if (f.T >= 1f)
                {
                    f.T = -1f;
                    f.Root.gameObject.SetActive(false);
                    continue;
                }

                f.Root.gameObject.SetActive(true);
                // Lift off the emitter, climb over the map, then out through the bay on space, accelerating.
                var e = f.T * f.T;
                var climb = emitter + Vector3.up * 2.2f + f.Lane;
                var bay = new Vector3(f.Lane.x * 1.4f, 3.1f + f.Lane.y, MarketDecor.Depth + 2f);
                var beyond = new Vector3(f.Lane.x * 4f, 6f + f.Lane.y * 2f, MarketDecor.Depth + 90f);
                Vector3 p;
                Vector3 ahead;
                if (f.T < 0.35f)
                {
                    var u = MotionEase.Smooth01(f.T / 0.35f);
                    p = Vector3.Lerp(emitter, climb, u);
                    ahead = climb;
                }
                else
                {
                    var u = (f.T - 0.35f) / 0.65f;
                    var k = u * u;
                    p = Vector3.Lerp(Vector3.Lerp(climb, bay, k), Vector3.Lerp(bay, beyond, k), k);
                    ahead = Vector3.Lerp(bay, beyond, Mathf.Min(1f, k + 0.1f));
                }

                f.Root.localPosition = p;
                var dir = ahead - p;
                if (dir.sqrMagnitude > 1e-6f)
                    f.Root.localRotation = Quaternion.Slerp(f.Root.localRotation, Quaternion.LookRotation(dir.normalized, Vector3.up), dt * 4f);
                // The streak grows with speed (an engine trail without a trail renderer).
                var len = Mathf.Lerp(0.01f, 1.6f, e);
                f.Streak.localScale = new Vector3(0.03f, 0.03f, len);
                f.Streak.localPosition = new Vector3(0f, 0f, -0.2f - len * 0.5f);
            }
        }

        void AnimateConsign(float t, float dt)
        {
            var c = _consign;
            if (!c.Root.gameObject.activeSelf)
            {
                _padSweep.gameObject.SetActive(false);
                return;
            }

            Face(c.Label.transform);
            c.Spin.localRotation = Quaternion.Euler(0f, t * 18f, 0f);
            switch (_consignMotion)
            {
                case 0:
                {
                    // Forming: the scan plane climbs through the crate as it grows out of the pad.
                    _consignBuild = Mathf.Min(1f, _consignBuild + dt / 1.1f);
                    var e = MotionEase.Smooth01(_consignBuild);
                    c.Spin.localScale = new Vector3(1f, Mathf.Max(0.02f, e), 1f);
                    c.Root.localPosition = PadRest + Vector3.up * Mathf.Sin(t * 1.5f) * 0.015f;
                    var sweeping = _consignBuild < 1f;
                    _padSweep.gameObject.SetActive(sweeping);
                    if (sweeping)
                        _padSweep.localPosition = PadRest + Vector3.up * (-0.22f + 0.44f * e);
                    break;
                }
                case 1:
                {
                    // Published: up and over into the carousel, shrinking into it.
                    _padSweep.gameObject.SetActive(false);
                    _consignT = Mathf.Min(1f, _consignT + dt / 1.6f);
                    var e = MotionEase.Smooth01(_consignT);
                    var to = MarketDecor.Pit + Vector3.up * (CrateHeight + 0.3f);
                    c.Root.localPosition = Vector3.Lerp(PadRest, to, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 1.2f;
                    c.Spin.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, e * e);
                    if (_consignT >= 1f)
                    {
                        _consignMotion = 0;
                        c.Root.gameObject.SetActive(false);
                    }

                    break;
                }
                default:
                {
                    // Withdrawn: down from the exchange onto the pad, then dissolved into the planet's stock.
                    _consignT = Mathf.Min(1f, _consignT + dt / 2.2f);
                    var from = MarketDecor.Pit + Vector3.up * (CrateHeight + 0.3f);
                    var land = Mathf.Min(1f, _consignT / 0.65f);
                    var e = MotionEase.Smooth01(land);
                    c.Root.localPosition = Vector3.Lerp(from, PadRest, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 1.2f;
                    var fade = Mathf.Clamp01((_consignT - 0.65f) / 0.35f);
                    c.Spin.localScale = new Vector3(1f, Mathf.Max(0.02f, 1f - fade), 1f) * Mathf.Lerp(0.2f, 1f, e);
                    _padSweep.gameObject.SetActive(fade > 0f && fade < 1f);
                    if (fade > 0f)
                        _padSweep.localPosition = PadRest + Vector3.up * (0.22f - 0.44f * fade);
                    if (_consignT >= 1f)
                    {
                        _consignMotion = 0;
                        c.Root.gameObject.SetActive(false);
                        _padSweep.gameObject.SetActive(false);
                    }

                    break;
                }
            }
        }
    }
}
