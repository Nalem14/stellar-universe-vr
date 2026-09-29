using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Set dressing of the captain's quarters, procedural and on shared materials: a 10 × 9.4 m cabin laid out
    /// like a real one around a panoramic bay on space ahead (open like the bridge hublots; the room stands over
    /// our ship). In from the corridor door (aft wall, starboard), the working wall is at the right hand: the
    /// captain's desk in dark wood against the starboard wall (low reclined Empire screen, nameplate, the
    /// capital's hologram), its chair pulled out, the Comms wall terminal, then the Nova shop console and the
    /// showcase by the bay. The far (port) side is the bedroom: a double bed, headboard to the wall, between two
    /// nightstands with reading lamps and night glows, a star chart over it; forward of it the progression
    /// console beside the trophy wall. The aft wall carries the data-crystal shelves by the door, the wardrobe
    /// and lockers, a bookshelf over the bedside. The lounge (couch facing space, low table) sits before the bay
    /// on its own rug; the middle of the cabin stays open (≥ 0.8 m everywhere between pieces).
    /// </summary>
    public static class QuartersDecor
    {
        public const float HalfWidth = 5.0f;
        public const float Front = 5.9f;
        public const float Back = -3.5f;
        public const float Height = 3.4f;
        public const float Sill = 0.55f;
        public const float WindowHead = 3.0f;
        /// <summary>The corridor door, on the aft wall starboard of centre (room x).</summary>
        public const float DoorX = 3.0f;
        /// <summary>Door plane (room z): just proud of the aft wall.</summary>
        public const float DoorZ = Back + 0.12f;
        public const int TrophyColumns = 6;
        public const int TrophyRows = 3;
        public const int Trophies = TrophyColumns * TrophyRows;

        /// <summary>
        /// Where the captain arrives: in front of the door but past its wake range (it stays shut behind), facing
        /// the bay, the desk at the right hand.
        /// </summary>
        public static readonly Vector3 Entry = new(DoorX, 0f, DoorZ + 1.75f);

        // Inner faces of the shell (walls are 0.2 m thick, centred on the bounds).
        const float WallIn = HalfWidth - 0.1f;
        const float AftIn = Back + 0.1f;
        // Wall-backed furniture: pivot this far from the wall (desk backs and reclined screen tops clear the ribs).
        const float DeskOff = 0.65f;
        // Wall ribs, between the wall-mounted pieces (never behind a board, a terminal or the headboard).
        static readonly float[] PortRibs = { -3.1f, 0.3f, 1.65f, 5.35f };
        static readonly float[] StbdRibs = { -3.1f, -0.75f, 1.2f, 2.95f, 5.35f };

        static Texture2D _wood;
        static Texture2D _carpet;

        public sealed class Refs
        {
            public Transform EmpireMount;
            public Transform ProgressMount;
            public Transform ShopMount;
            public Transform CommsMount;
            public Transform CommsWakeMount;
            public readonly MeshRenderer[] Plaques = new MeshRenderer[Trophies];
            public readonly MeshRenderer[] Emblems = new MeshRenderer[Trophies];
            public readonly TextMeshPro[] TrophyNames = new TextMeshPro[Trophies];
            public TextMeshPro TrophyHeader;
            public TextMeshPro Nameplate;
            public MeshRenderer ShowcaseRing;
            public TextMeshPro ShowcaseTitle;
            public MeshRenderer ShowcaseFlag;
            public Transform Globe;
        }

        public static Refs Build(Transform room, CicArtKit art, Color accent)
        {
            var refs = new Refs();
            var metal = art.MetalPanel(0.4f);
            var dark = art.DarkPanel(0.3f);
            var deck = art.DeckMat(0.4f);
            var brass = art.Lit(Texture2D.whiteTexture, accent, 2.2f);
            var cyan = art.CyanEmit(1.8f);
            var wood = art.Lit(WoodTex(), new Color(0.55f, 0.4f, 0.3f), 0f, 1f);
            var leather = art.Lit(Texture2D.whiteTexture, new Color(0.16f, 0.08f, 0.06f), 0.1f);
            var width = HalfWidth * 2f;
            var depth = Front - Back;
            var midZ = (Front + Back) * 0.5f;

            // ── Shell ─────────────────────────────────────────────────────────────
            var floor = GateRoomDecor.Box(room, "Floor", new Vector3(0f, -0.05f, midZ), new Vector3(width, 0.1f, depth), deck);
            floor.AddComponent<BoxCollider>();
            GateRoomDecor.Box(room, "Ceiling", new Vector3(0f, Height + 0.05f, midZ), new Vector3(width, 0.1f, depth), dark);
            for (var side = -1; side <= 1; side += 2)
            {
                var wall = GateRoomDecor.Box(room, side < 0 ? "WallPort" : "WallStbd", new Vector3(side * HalfWidth, Height * 0.5f, midZ),
                    new Vector3(0.2f, Height, depth), dark);
                wall.AddComponent<BoxCollider>();
                foreach (var z in side < 0 ? PortRibs : StbdRibs)
                    GateRoomDecor.Box(room, "Rib", new Vector3(side * (HalfWidth - 0.12f), Height * 0.5f, z),
                        new Vector3(0.14f, Height, 0.22f), metal);
                GateRoomDecor.Box(room, "Cornice", new Vector3(side * (HalfWidth - 0.14f), Height - 0.18f, midZ),
                    new Vector3(0.16f, 0.12f, depth), metal);
                GateRoomDecor.Box(room, "CorniceGlow", new Vector3(side * (HalfWidth - 0.225f), Height - 0.24f, midZ),
                    new Vector3(0.01f, 0.02f, depth - 0.4f), brass);
            }

            var back = GateRoomDecor.Box(room, "WallAft", new Vector3(0f, Height * 0.5f, Back), new Vector3(width, Height, 0.2f), dark);
            back.AddComponent<BoxCollider>();
            GateRoomDecor.Box(room, "CorniceAft", new Vector3(0f, Height - 0.18f, Back + 0.14f), new Vector3(width - 0.2f, 0.12f, 0.16f), metal);
            GateRoomDecor.Box(room, "CorniceAftGlow", new Vector3(0f, Height - 0.24f, Back + 0.225f), new Vector3(width - 0.6f, 0.02f, 0.01f), brass);

            // Panoramic bay ahead: sill, head, mullions; space beyond.
            var sill = GateRoomDecor.Box(room, "BaySill", new Vector3(0f, Sill * 0.5f, Front), new Vector3(width, Sill, 0.2f), dark);
            var sillCol = sill.AddComponent<BoxCollider>();
            sillCol.size = new Vector3(1f, Height / Sill, 1f);
            sillCol.center = new Vector3(0f, (Height / Sill - 1f) * 0.5f, 0f);
            GateRoomDecor.Box(room, "BayHead", new Vector3(0f, (WindowHead + Height) * 0.5f, Front), new Vector3(width, Height - WindowHead, 0.2f), dark);
            GateRoomDecor.Box(room, "BayLedge", new Vector3(0f, Sill, Front - 0.18f), new Vector3(width - 0.3f, 0.05f, 0.34f), wood);
            GateRoomDecor.Box(room, "BayLedgeGlow", new Vector3(0f, Sill - 0.035f, Front - 0.35f), new Vector3(width - 0.4f, 0.012f, 0.01f), brass);
            for (var i = -3; i <= 3; i++)
                GateRoomDecor.Box(room, "Mullion", new Vector3(i * 1.4f, (Sill + WindowHead) * 0.5f, Front - 0.04f),
                    new Vector3(i == 0 ? 0.1f : 0.06f, WindowHead - Sill, 0.1f), metal);
            GateRoomDecor.Box(room, "Transom", new Vector3(0f, WindowHead - 0.35f, Front - 0.04f), new Vector3(width - 0.2f, 0.05f, 0.08f), metal);

            // Ceiling: coffer frame and warm light panels.
            for (var i = -1; i <= 1; i += 2)
            {
                GateRoomDecor.Box(room, "CofferBeam", new Vector3(i * 2.2f, Height - 0.06f, midZ), new Vector3(0.18f, 0.12f, depth - 0.4f), metal);
                GateRoomDecor.Box(room, "CeilingLight", new Vector3(i * 2.2f, Height - 0.125f, midZ), new Vector3(0.06f, 0.01f, depth - 1.2f),
                    art.Lit(Texture2D.whiteTexture, new Color(1f, 0.85f, 0.65f), 2.4f));
            }

            // ── Floor: brass-bordered rugs under the bed and the lounge ───────────
            var carpetMat = art.Lit(CarpetTex(), Color.white, 0f, 1f);
            Rug(room, "BedRug", new Vector3(-3.3f, 0.004f, -1.3f), new Vector2(3.0f, 2.6f), carpetMat);
            Rug(room, "LoungeRug", new Vector3(-0.3f, 0.004f, 3.55f), new Vector2(3.2f, 2.0f), carpetMat);

            BuildOffice(room, art, accent, refs, metal, dark, cyan, brass, wood, leather);
            BuildProgress(room, art, accent, refs, metal, dark, cyan, brass, wood);
            BuildLounge(room, accent, metal, wood, leather);
            BuildAftWall(room, art, accent, metal, dark, cyan, brass, wood);
            BuildBed(room, art, accent, metal, wood, brass);
            return refs;
        }

        static void Rug(Transform room, string name, Vector3 pos, Vector2 size, Material mat)
        {
            var rug = GateRoomDecor.Quad(room, name, pos, new Vector3(size.x, size.y, 1f), mat);
            rug.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>
        /// The working wall, starboard, first at hand from the door: the captain's desk (Empire screen), its
        /// chair, the Comms wall terminal, the Nova shop console and the showcase by the bay. Every console
        /// has its back to the wall and its operator side (local −z) toward the room.
        /// </summary>
        static void BuildOffice(Transform room, CicArtKit art, Color accent, Refs refs, Material metal, Material dark,
            Material cyan, Material brass, Material wood, Material leather)
        {
            var ring = art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture;

            // ── Captain's desk: dark wood, the Empire screen low and reclined ─────
            // Yaw 90°: the back (local +z) against the starboard wall, the length (local x) along it.
            var desk = new GameObject("CaptainDesk").transform;
            desk.SetParent(room, false);
            desk.localPosition = new Vector3(WallIn - DeskOff, 0f, -0.75f);
            desk.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GateRoomDecor.Box(desk, "Top", new Vector3(0f, 0.78f, 0f), new Vector3(2.3f, 0.06f, 0.9f), wood);
            GateRoomDecor.Box(desk, "TopEdge", new Vector3(0f, 0.755f, -0.455f), new Vector3(2.3f, 0.012f, 0.012f), brass);
            for (var side = -1; side <= 1; side += 2)
            {
                GateRoomDecor.Box(desk, "Pedestal", new Vector3(side * 0.82f, 0.375f, 0.05f), new Vector3(0.6f, 0.75f, 0.76f), wood);
                for (var d = 0; d < 3; d++)
                    GateRoomDecor.Box(desk, "Drawer", new Vector3(side * 0.82f, 0.15f + d * 0.22f, -0.335f), new Vector3(0.5f, 0.012f, 0.01f), brass);
            }

            GateRoomDecor.Box(desk, "ModestyPanel", new Vector3(0f, 0.45f, 0.36f), new Vector3(1.05f, 0.6f, 0.03f), wood);
            GateRoomDecor.Box(desk, "ScreenBase", new Vector3(0f, 0.82f, -0.02f), new Vector3(0.5f, 0.03f, 0.2f), metal);
            refs.EmpireMount = new GameObject("EmpireMount").transform;
            refs.EmpireMount.SetParent(desk, false);
            refs.EmpireMount.localPosition = new Vector3(0f, 0.84f, -0.05f);
            // Operator side is the desk's −z (the room); the reclined screen's top stays clear of the wall.
            refs.EmpireMount.localRotation = Quaternion.identity;

            refs.Nameplate = UiKit.Label(desk, "Nameplate", string.Empty, new Vector3(0f, 0.66f, -0.47f), 1.4f, 0.05f,
                new Color(1f, 0.85f, 0.6f));
            refs.Nameplate.richText = true;
            refs.Nameplate.fontStyle = FontStyles.Bold;
            GateRoomDecor.Box(desk, "NameplateBack", new Vector3(0f, 0.66f, -0.458f), new Vector3(1.5f, 0.1f, 0.01f), metal);

            // The capital as a hologram on the desk's aft corner, the first thing seen from the door.
            var globeBase = GateRoomDecor.Rounded(desk, "GlobeBase", new Vector3(0.18f, 0.05f, 0.18f), 0.04f,
                new Vector3(0.9f, 0.835f, 0.15f), UiKit.Chassis, accent, 0.6f);
            globeBase.name = "GlobeBase";
            var globe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            globe.name = "CapitalGlobe";
            Object.Destroy(globe.GetComponent<Collider>());
            globe.transform.SetParent(desk, false);
            globe.transform.localPosition = new Vector3(0.9f, 1.02f, 0.15f);
            globe.transform.localScale = Vector3.one * 0.22f;
            var gr = globe.GetComponent<MeshRenderer>();
            gr.sharedMaterial = art.Holo(ring, new Color(0.4f, 0.85f, 1f, 0.5f));
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var spin = globe.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 14f;
            spin.BobMeters = 0.006f;
            refs.Globe = globe.transform;

            // Captain's chair, pulled out from the desk's forward end and turned toward it: the spot in front
            // of the screen stays free for a standing captain.
            var chair = new GameObject("CaptainChair").transform;
            chair.SetParent(room, false);
            chair.localPosition = new Vector3(3.3f, 0f, 0.95f);
            chair.localRotation = Quaternion.Euler(0f, 120f, 0f);
            GateRoomDecor.Box(chair, "Base", new Vector3(0f, 0.03f, 0f), new Vector3(0.6f, 0.06f, 0.6f), metal);
            GateRoomDecor.Box(chair, "Post", new Vector3(0f, 0.25f, 0f), new Vector3(0.1f, 0.4f, 0.1f), metal);
            GateRoomDecor.Rounded(chair, "Seat", new Vector3(0.62f, 0.12f, 0.58f), 0.05f, new Vector3(0f, 0.5f, 0f), leather, accent, 0f);
            GateRoomDecor.Rounded(chair, "Back", new Vector3(0.62f, 0.85f, 0.12f), 0.05f, new Vector3(0f, 0.98f, -0.26f), leather, accent, 0f);
            for (var side = -1; side <= 1; side += 2)
            {
                GateRoomDecor.Box(chair, "Arm", new Vector3(side * 0.34f, 0.7f, 0f), new Vector3(0.08f, 0.06f, 0.5f), metal);
                GateRoomDecor.Box(chair, "ArmGlow", new Vector3(side * 0.34f, 0.735f, 0f), new Vector3(0.02f, 0.008f, 0.4f), brass);
            }

            // ── Comms terminal on the starboard wall, forward of the desk; a wake button under it ─
            var comms = new GameObject("CommsTerminal").transform;
            comms.SetParent(room, false);
            comms.localPosition = new Vector3(HalfWidth - 0.13f, 0f, 2.1f);
            comms.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GateRoomDecor.Box(comms, "Bezel", new Vector3(0f, 1.55f, 0.03f), new Vector3(1.26f, 0.86f, 0.05f), UiKit.Chassis);
            GateRoomDecor.Box(comms, "Glow", new Vector3(0f, 1.1f, -0.01f), new Vector3(1.2f, 0.015f, 0.01f), art.Lit(Texture2D.whiteTexture, new Color(0.55f, 0.85f, 0.45f), 2.4f));
            refs.CommsMount = new GameObject("CommsMount").transform;
            refs.CommsMount.SetParent(comms, false);
            refs.CommsMount.localPosition = new Vector3(0f, 1.55f, -0.02f);
            refs.CommsWakeMount = new GameObject("CommsWake").transform;
            refs.CommsWakeMount.SetParent(comms, false);
            refs.CommsWakeMount.localPosition = new Vector3(0f, 0.98f, -0.03f);

            // ── Nova shop console, then its showcase in the corner by the bay ─────
            refs.ShopMount = GateRoomDecor.Desk(room, "ShopDesk", new Vector3(WallIn - DeskOff, 0f, 3.85f), 90f, 1.2f,
                metal, dark, cyan, brass);

            var show = new GameObject("Showcase").transform;
            show.SetParent(room, false);
            show.localPosition = new Vector3(WallIn - DeskOff, 0f, 5.05f);
            show.localRotation = Quaternion.Euler(0f, 90f, 0f);
            // Slim column on a round foot, a lit collar; the fleet colour ring floats over it.
            GateRoomDecor.Rounded(show, "Foot", new Vector3(0.6f, 0.06f, 0.6f), 0.28f, new Vector3(0f, 0.03f, 0f), UiKit.Chassis, accent, 0.3f);
            GateRoomDecor.Rounded(show, "Column", new Vector3(0.22f, 0.86f, 0.22f), 0.1f, new Vector3(0f, 0.49f, 0f), UiKit.Chassis, accent, 0.2f);
            GateRoomDecor.Rounded(show, "Collar", new Vector3(0.42f, 0.05f, 0.42f), 0.2f, new Vector3(0f, 0.93f, 0f), UiKit.Chassis, accent, 0.9f);
            var showRing = GateRoomDecor.Quad(show, "ColourRing", new Vector3(0f, 0.99f, 0f), new Vector3(0.56f, 0.56f, 1f),
                art.RadarIcon(ring, Color.white));
            showRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            refs.ShowcaseRing = showRing.GetComponent<MeshRenderer>();
            var flag = GateRoomDecor.Quad(show, "Flag", new Vector3(0f, 1.3f, 0f), new Vector3(0.45f, 0.3f, 1f), dark);
            refs.ShowcaseFlag = flag.GetComponent<MeshRenderer>();
            var flagSpin = flag.AddComponent<HoloSpin>();
            flagSpin.DegreesPerSecond = 20f;
            flagSpin.BobMeters = 0.01f;
            var flagBack = GateRoomDecor.Quad(flag.transform, "Back", Vector3.zero, Vector3.one, dark);
            flagBack.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            refs.ShowcaseTitle = UiKit.Label(show, "Title", string.Empty, new Vector3(0f, 1.62f, 0f), 1.2f, 0.06f,
                new Color(1f, 0.85f, 0.6f));
            refs.ShowcaseTitle.richText = true;
            refs.ShowcaseTitle.gameObject.AddComponent<BillboardFace>();
        }

        /// <summary>Port wall, forward of the bed: the progression console, and beside it the trophy wall it mirrors.</summary>
        static void BuildProgress(Transform room, CicArtKit art, Color accent, Refs refs, Material metal, Material dark,
            Material cyan, Material brass, Material wood)
        {
            // Yaw −90°: back to the port wall, operator side toward the room.
            refs.ProgressMount = GateRoomDecor.Desk(room, "ProgressDesk", new Vector3(-(WallIn - DeskOff), 0f, 1.2f), -90f, 1.2f,
                metal, dark, cyan, brass);

            var ring = art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture;
            var trophy = new GameObject("TrophyWall").transform;
            trophy.SetParent(room, false);
            trophy.localPosition = new Vector3(-HalfWidth + 0.13f, 0f, 3.5f);
            // Local −z = into the room (+x); local +x runs forward along the wall.
            trophy.localRotation = Quaternion.Euler(0f, -90f, 0f);
            GateRoomDecor.Box(trophy, "Backboard", new Vector3(0f, 1.75f, 0.02f), new Vector3(3.3f, 1.7f, 0.03f), wood);
            GateRoomDecor.Box(trophy, "BoardTop", new Vector3(0f, 2.62f, -0.02f), new Vector3(3.4f, 0.04f, 0.1f), brass);
            GateRoomDecor.Box(trophy, "BoardBottom", new Vector3(0f, 0.88f, -0.02f), new Vector3(3.4f, 0.04f, 0.1f), brass);
            refs.TrophyHeader = UiKit.Label(trophy, "Header", string.Empty, new Vector3(0f, 2.82f, -0.02f), 3.2f, 0.09f,
                new Color(1f, 0.85f, 0.6f));
            refs.TrophyHeader.richText = true;
            refs.TrophyHeader.fontStyle = FontStyles.Bold;
            for (var r = 0; r < TrophyRows; r++)
            for (var c = 0; c < TrophyColumns; c++)
            {
                var i = r * TrophyColumns + c;
                var pos = new Vector3(-1.35f + c * 0.54f, 2.35f - r * 0.52f, -0.02f);
                var plaque = GateRoomDecor.Rounded(trophy, "Plaque" + i, new Vector3(0.42f, 0.3f, 0.03f), 0.03f, pos,
                    UiKit.Chassis, accent, 0.2f);
                refs.Plaques[i] = plaque.GetComponent<MeshRenderer>();
                var emblem = GateRoomDecor.Quad(trophy, "Emblem" + i, pos + new Vector3(0f, 0.04f, -0.018f), new Vector3(0.16f, 0.16f, 1f),
                    art.RadarIcon(ring, new Color(accent.r, accent.g, accent.b, 0.9f)));
                refs.Emblems[i] = emblem.GetComponent<MeshRenderer>();
                var name = UiKit.Label(trophy, "Name" + i, string.Empty, pos + new Vector3(0f, -0.1f, -0.02f), 0.4f, 0.026f,
                    UiKit.TextBright);
                name.richText = true;
                refs.TrophyNames[i] = name;
            }
        }

        /// <summary>Before the bay, on its rug: a couch facing space (arms, cushions) and a low table.</summary>
        static void BuildLounge(Transform room, Color accent, Material metal, Material wood, Material leather)
        {
            var lounge = new GameObject("Lounge").transform;
            lounge.SetParent(room, false);
            lounge.localPosition = new Vector3(-0.3f, 0f, 3.3f);
            // Local +z (the couch back) toward the room, the seat and the table toward the bay.
            lounge.localRotation = Quaternion.Euler(0f, 180f, 0f);
            GateRoomDecor.Rounded(lounge, "CouchSeat", new Vector3(1.8f, 0.4f, 0.7f), 0.08f, new Vector3(0f, 0.22f, 0f), leather, accent, 0f);
            GateRoomDecor.Rounded(lounge, "CouchBack", new Vector3(1.8f, 0.55f, 0.18f), 0.07f, new Vector3(0f, 0.62f, 0.3f), leather, accent, 0f);
            for (var side = -1; side <= 1; side += 2)
            {
                GateRoomDecor.Rounded(lounge, "CouchArm", new Vector3(0.16f, 0.3f, 0.7f), 0.06f, new Vector3(side * 0.98f, 0.37f, 0.02f),
                    leather, accent, 0f);
                GateRoomDecor.Rounded(lounge, "Cushion", new Vector3(0.78f, 0.08f, 0.5f), 0.035f, new Vector3(side * 0.42f, 0.46f, -0.05f),
                    leather, accent, 0f);
            }

            GateRoomDecor.Box(lounge, "Table", new Vector3(0f, 0.36f, -0.85f), new Vector3(1f, 0.04f, 0.5f), wood);
            GateRoomDecor.Box(lounge, "TableLeg", new Vector3(0f, 0.17f, -0.85f), new Vector3(0.12f, 0.34f, 0.12f), metal);
            GateRoomDecor.Box(lounge, "TableFoot", new Vector3(0f, 0.01f, -0.85f), new Vector3(0.4f, 0.02f, 0.3f), metal);
        }

        /// <summary>
        /// Aft wall, from the door to port: data-crystal display shelves, the wardrobe with its column of lockers,
        /// and a bookshelf with a potted plant over the bedside corner.
        /// </summary>
        static void BuildAftWall(Transform room, CicArtKit art, Color accent, Material metal, Material dark, Material cyan,
            Material brass, Material wood)
        {
            var warm = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.78f, 0.52f), 1.4f);

            // Data-crystal shelves, between the wardrobe and the door.
            for (var s = 0; s < 3; s++)
            {
                var y = 1.2f + s * 0.45f;
                GateRoomDecor.Box(room, "Shelf", new Vector3(1.25f, y, AftIn + 0.12f), new Vector3(1.6f, 0.03f, 0.25f), wood);
                for (var k = 0; k < 7; k++)
                {
                    var hue = (k * 37 + s * 11) % 3;
                    var c = hue == 0 ? CicArtKit.Cyan : hue == 1 ? CicArtKit.Amber : new Color(0.6f, 0.5f, 1f);
                    GateRoomDecor.Box(room, "Crystal", new Vector3(0.53f + k * 0.24f, y + 0.09f, AftIn + 0.12f),
                        new Vector3(0.04f, 0.15f + (k % 3) * 0.02f, 0.04f), art.Lit(Texture2D.whiteTexture, c, 1.6f));
                }
            }

            // Wardrobe and lockers: front (local +z) toward the room.
            var robe = new GameObject("Wardrobe").transform;
            robe.SetParent(room, false);
            robe.localPosition = new Vector3(-0.9f, 0f, AftIn + 0.31f);
            GateRoomDecor.Box(robe, "Carcass", new Vector3(0f, 1.15f, 0f), new Vector3(2f, 2.3f, 0.6f), wood);
            GateRoomDecor.Box(robe, "Crown", new Vector3(0f, 2.33f, 0.02f), new Vector3(2.08f, 0.06f, 0.64f), metal);
            GateRoomDecor.Box(robe, "Plinth", new Vector3(0f, 0.04f, 0.305f), new Vector3(1.96f, 0.08f, 0.02f), dark);
            GateRoomDecor.Box(robe, "PlinthGlow", new Vector3(0f, 0.085f, 0.312f), new Vector3(1.9f, 0.01f, 0.01f), warm);
            // Two hanging doors (x −1 … 0.3), a seam, then the locker column (0.3 … 1).
            GateRoomDecor.Box(robe, "DoorSeam", new Vector3(-0.35f, 1.2f, 0.302f), new Vector3(0.012f, 2.1f, 0.01f), metal);
            GateRoomDecor.Box(robe, "LockerSeam", new Vector3(0.3f, 1.2f, 0.303f), new Vector3(0.03f, 2.2f, 0.012f), metal);
            for (var side = -1; side <= 1; side += 2)
                GateRoomDecor.Box(robe, "Handle", new Vector3(-0.35f + side * 0.07f, 1.15f, 0.32f), new Vector3(0.02f, 0.36f, 0.03f), brass);
            for (var i = 0; i < 3; i++)
            {
                var y = 0.45f + i * 0.72f;
                GateRoomDecor.Box(robe, "Locker", new Vector3(0.65f, y, 0.31f), new Vector3(0.62f, 0.66f, 0.02f), metal);
                GateRoomDecor.Box(robe, "LockerLed", new Vector3(0.65f, y + 0.24f, 0.322f), new Vector3(0.1f, 0.012f, 0.01f), cyan);
                GateRoomDecor.Box(robe, "LockerGrip", new Vector3(0.65f, y - 0.1f, 0.33f), new Vector3(0.14f, 0.02f, 0.025f), brass);
            }

            // Bookshelf over the bedside corner (port aft), a potted plant at its end.
            for (var s = 0; s < 2; s++)
            {
                var y = 1.5f + s * 0.42f;
                GateRoomDecor.Box(room, "BookShelf", new Vector3(-3.55f, y, AftIn + 0.12f), new Vector3(1.5f, 0.03f, 0.24f), wood);
                GateRoomDecor.Box(room, "BookShelfGlow", new Vector3(-3.55f, y - 0.02f, AftIn + 0.24f), new Vector3(1.4f, 0.008f, 0.006f), brass);
                var x = -4.22f;
                for (var b = 0; b < 9; b++)
                {
                    var tall = 0.2f + ((b * 7 + s * 3) % 4) * 0.025f;
                    var thick = 0.035f + ((b + s) % 3) * 0.012f;
                    var hue = (b * 5 + s * 2) % 4;
                    var c = hue == 0 ? new Color(0.35f, 0.12f, 0.1f) : hue == 1 ? new Color(0.12f, 0.2f, 0.32f)
                        : hue == 2 ? new Color(0.3f, 0.26f, 0.18f) : new Color(0.15f, 0.25f, 0.2f);
                    GateRoomDecor.Box(room, "Book", new Vector3(x + thick * 0.5f, y + 0.015f + tall * 0.5f, AftIn + 0.11f),
                        new Vector3(thick, tall, 0.17f), art.Lit(Texture2D.whiteTexture, c, 0.05f));
                    x += thick + 0.006f;
                }
            }

            Plant(room, art, accent, new Vector3(-2.95f, 1.98f, AftIn + 0.12f));
        }

        static void Plant(Transform parent, CicArtKit art, Color accent, Vector3 pos)
        {
            var pot = GateRoomDecor.Rounded(parent, "Pot", new Vector3(0.12f, 0.12f, 0.12f), 0.05f, pos, UiKit.Chassis, accent, 0.3f);
            var leaf = art.Lit(Texture2D.whiteTexture, new Color(0.22f, 0.5f, 0.3f), 0.25f);
            for (var l = 0; l < 7; l++)
            {
                var g = GateRoomDecor.Box(pot.transform, "Leaf", new Vector3(0f, 0.1f, 0f), new Vector3(0.03f, 0.16f, 0.006f), leaf);
                g.transform.localRotation = Quaternion.Euler(18f + (l % 3) * 9f, l * 51f, 0f);
                g.transform.localPosition = g.transform.localRotation * new Vector3(0f, 0.08f, 0f) + Vector3.up * 0.04f;
            }
        }

        /// <summary>
        /// The bedroom, far from the door on the port side: a made double bed with its headboard to the wall, a
        /// nightstand either side (the model of a ship on one, a plant on the other), a reading lamp over each,
        /// a warm halo over the headboard and a low night glow along the bed base; a framed star chart above.
        /// </summary>
        static void BuildBed(Transform room, CicArtKit art, Color accent, Material metal, Material wood, Material brass)
        {
            var linen = art.Lit(Texture2D.whiteTexture, new Color(0.62f, 0.66f, 0.72f), 0.08f);
            var blanket = art.Lit(Texture2D.whiteTexture, new Color(0.1f, 0.15f, 0.27f), 0.06f);
            var fold = art.Lit(Texture2D.whiteTexture, new Color(0.16f, 0.22f, 0.36f), 0.06f);
            var warm = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.84f, 0.6f), 2.2f);
            var night = art.Lit(Texture2D.whiteTexture, new Color(1f, 0.72f, 0.45f), 1.2f);

            // Bed frame: local +z runs from the headboard (at the port wall, local z −1.1) toward the room (+x);
            // local +x is aft (−z). 1.66 m wide, 2.1 m long.
            var bed = new GameObject("Bed").transform;
            bed.SetParent(room, false);
            bed.localPosition = new Vector3(-WallIn + 1.1f, 0f, -1.3f);
            bed.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GateRoomDecor.Rounded(bed, "Base", new Vector3(1.66f, 0.3f, 2.04f), 0.04f, new Vector3(0f, 0.17f, 0.03f), UiKit.Chassis, accent, 0.2f);
            for (var side = -1; side <= 1; side += 2)
                GateRoomDecor.Box(bed, "NightGlow", new Vector3(side * 0.835f, 0.05f, 0.03f), new Vector3(0.01f, 0.012f, 1.9f), night);
            GateRoomDecor.Box(bed, "FootTrim", new Vector3(0f, 0.05f, 1.055f), new Vector3(1.56f, 0.012f, 0.01f), brass);
            GateRoomDecor.Rounded(bed, "Mattress", new Vector3(1.58f, 0.18f, 1.98f), 0.06f, new Vector3(0f, 0.41f, 0.03f), linen, accent, 0f);
            GateRoomDecor.Rounded(bed, "Blanket", new Vector3(1.62f, 0.05f, 1.3f), 0.025f, new Vector3(0f, 0.51f, 0.36f), blanket, accent, 0f);
            GateRoomDecor.Rounded(bed, "Fold", new Vector3(1.63f, 0.07f, 0.22f), 0.03f, new Vector3(0f, 0.52f, -0.33f), fold, accent, 0f);
            for (var side = -1; side <= 1; side += 2)
                GateRoomDecor.Rounded(bed, "Pillow", new Vector3(0.64f, 0.12f, 0.36f), 0.05f, new Vector3(side * 0.38f, 0.56f, -0.74f),
                    linen, accent, 0f);
            GateRoomDecor.Rounded(bed, "Headboard", new Vector3(1.9f, 1f, 0.07f), 0.03f, new Vector3(0f, 0.72f, -1.06f), wood, accent, 0f);
            GateRoomDecor.Box(bed, "HeadTrim", new Vector3(0f, 1.2f, -1.02f), new Vector3(1.8f, 0.02f, 0.02f), brass);
            GateRoomDecor.Box(bed, "HeadHalo", new Vector3(0f, 1.27f, -1.085f), new Vector3(1.7f, 0.012f, 0.01f), night);

            // Nightstands either side of the head (fronts toward the foot), a reading lamp on an arm over each.
            for (var side = -1; side <= 1; side += 2)
            {
                var stand = new GameObject("Nightstand").transform;
                stand.SetParent(bed, false);
                stand.localPosition = new Vector3(side * 1.13f, 0f, -0.78f);
                GateRoomDecor.Rounded(stand, "Body", new Vector3(0.5f, 0.5f, 0.44f), 0.03f, new Vector3(0f, 0.25f, 0f), wood, accent, 0f);
                GateRoomDecor.Box(stand, "Drawer", new Vector3(0f, 0.3f, 0.221f), new Vector3(0.36f, 0.14f, 0.01f), UiKit.Chassis);
                GateRoomDecor.Box(stand, "Pull", new Vector3(0f, 0.3f, 0.228f), new Vector3(0.12f, 0.012f, 0.01f), brass);

                GateRoomDecor.Box(bed, "LampArm", new Vector3(side * 1.13f, 1.42f, -0.98f), new Vector3(0.02f, 0.02f, 0.22f), metal);
                GateRoomDecor.Rounded(bed, "LampHead", new Vector3(0.1f, 0.05f, 0.1f), 0.02f, new Vector3(side * 1.13f, 1.4f, -0.86f),
                    UiKit.Chassis, accent, 0.2f);
                GateRoomDecor.Box(bed, "LampGlow", new Vector3(side * 1.13f, 1.372f, -0.86f), new Vector3(0.07f, 0.006f, 0.07f), warm);

                if (side > 0)
                {
                    // Aft nightstand: a plant.
                    Plant(stand, art, accent, new Vector3(0f, 0.56f, 0f));
                    continue;
                }

                // Forward nightstand: the model of a ship on its stand.
                GateRoomDecor.Rounded(stand, "Plinth", new Vector3(0.16f, 0.03f, 0.16f), 0.015f, new Vector3(0f, 0.515f, 0f), UiKit.Chassis, accent, 0.4f);
                GateRoomDecor.Box(stand, "Rod", new Vector3(0f, 0.6f, 0f), new Vector3(0.012f, 0.14f, 0.012f), metal);
                var model = new GameObject("ShipModel");
                model.transform.SetParent(stand, false);
                model.transform.localPosition = new Vector3(0f, 0.69f, 0f);
                model.transform.localRotation = Quaternion.Euler(-8f, 35f, 6f);
                model.transform.localScale = Vector3.one * 0.017f;
                model.AddComponent<MeshFilter>().sharedMesh = ModelShip();
                var mr = model.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ShipHullBuilder.CivilHull();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }

            // A framed star chart on the wall over the headboard.
            GateRoomDecor.Box(bed, "ChartFrame", new Vector3(0f, 2.05f, -1.085f), new Vector3(1.2f, 0.7f, 0.03f), wood);
            var chart = GateRoomDecor.Quad(bed, "Chart", new Vector3(0f, 2.05f, -1.066f), new Vector3(1.1f, 0.6f, 1f),
                art.Lit(art.Stars != null ? art.Stars : Texture2D.whiteTexture, new Color(0.75f, 0.85f, 1f), 1.1f));
            // Quad front is its −z: turned half round it faces the foot of the bed (the room).
            chart.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        static Mesh _model;

        /// <summary>A keepsake model: a 9×9 liner silhouette (drive, holds, core, sensor bow), shared hull art.</summary>
        static Mesh ModelShip()
        {
            if (_model != null)
                return _model;
            var mods = new System.Collections.Generic.List<Core.App.FocusShipModule>
            {
                new() { Id = 1, Type = "HyperspaceDrive", GridX = 4, GridY = 2 },
                new() { Id = 2, Type = "CargoHold", GridX = 3, GridY = 4 },
                new() { Id = 3, Type = "ShipCore", GridX = 4, GridY = 4 },
                new() { Id = 4, Type = "CargoHold", GridX = 5, GridY = 4 },
                new() { Id = 5, Type = "CargoHold", GridX = 4, GridY = 3 },
                new() { Id = 6, Type = "SensorArray", GridX = 4, GridY = 6 },
                new() { Id = 7, Type = "CargoHold", GridX = 4, GridY = 5 }
            };
            return _model = ShipHullBuilder.SilhouetteMesh(mods, 4242);
        }

        // ── Procedural textures ───────────────────────────────────────────────────

        /// <summary>Dark wood: warm stripes with a slow wobble and fine grain.</summary>
        static Texture2D WoodTex()
        {
            if (_wood != null)
                return _wood;
            const int n = 128;
            _wood = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "SU_Wood", wrapMode = TextureWrapMode.Repeat };
            var dark = new Color(0.22f, 0.12f, 0.07f);
            var light = new Color(0.45f, 0.27f, 0.15f);
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                var u = x / (float)n;
                var v = y / (float)n;
                var ring = Mathf.Sin((u * 9f + Mathf.Sin(v * 6.283f * 2f) * 0.35f + Mathf.PerlinNoise(u * 4f, v * 4f) * 0.8f) * 6.283f);
                var grain = Mathf.PerlinNoise(u * 60f, v * 6f) * 0.25f;
                _wood.SetPixel(x, y, Color.Lerp(dark, light, Mathf.Clamp01(0.5f + ring * 0.3f + grain - 0.12f)));
            }

            _wood.Apply(true, true);
            return _wood;
        }

        /// <summary>Navy carpet, brass border and a fine inner line.</summary>
        static Texture2D CarpetTex()
        {
            if (_carpet != null)
                return _carpet;
            const int w = 256, h = 192;
            _carpet = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SU_Carpet", wrapMode = TextureWrapMode.Clamp };
            var navy = new Color(0.05f, 0.08f, 0.16f);
            var brass = new Color(0.75f, 0.52f, 0.25f);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var edge = Mathf.Min(Mathf.Min(x, w - 1 - x), Mathf.Min(y, h - 1 - y));
                var c = navy * (0.9f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.2f);
                if (edge < 10)
                    c = edge < 3 ? navy * 0.6f : brass * 0.8f;
                else if (edge == 16 || edge == 17)
                    c = brass * 0.6f;
                c.a = 1f;
                _carpet.SetPixel(x, y, c);
            }

            _carpet.Apply(true, true);
            return _carpet;
        }
    }
}
