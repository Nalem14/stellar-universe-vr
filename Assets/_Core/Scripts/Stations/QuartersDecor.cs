using Core.UI;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// Set dressing of the captain's quarters, procedural and on shared materials: a cabin with a panoramic bay
    /// on space ahead (open like the bridge hublots; the room stands over our ship), the captain's desk in
    /// dark wood with a low reclined screen that leaves the view free, the captain's chair behind the stand,
    /// a trophy wall of achievement plaques to port, the Nova showcase and the Comms wall terminal to starboard,
    /// a lounge corner, a carpet with a brass border, the capital's hologram turning on the desk.
    /// </summary>
    public static class QuartersDecor
    {
        public const float HalfWidth = 4.2f;
        public const float Front = 5.2f;
        public const float Back = -2.4f;
        public const float Height = 3.4f;
        public const float Sill = 0.55f;
        public const float WindowHead = 3.0f;
        public const int TrophyColumns = 6;
        public const int TrophyRows = 3;
        public const int Trophies = TrophyColumns * TrophyRows;

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
            var ring = art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture;
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
                for (var k = 0; k < 4; k++)
                    GateRoomDecor.Box(room, "Rib", new Vector3(side * (HalfWidth - 0.12f), Height * 0.5f, Back + 0.6f + k * 2.1f),
                        new Vector3(0.14f, Height, 0.22f), metal);
                GateRoomDecor.Box(room, "Cornice", new Vector3(side * (HalfWidth - 0.14f), Height - 0.18f, midZ),
                    new Vector3(0.16f, 0.12f, depth), metal);
                GateRoomDecor.Box(room, "CorniceGlow", new Vector3(side * (HalfWidth - 0.225f), Height - 0.24f, midZ),
                    new Vector3(0.01f, 0.02f, depth - 0.4f), brass);
            }

            var back = GateRoomDecor.Box(room, "WallAft", new Vector3(0f, Height * 0.5f, Back), new Vector3(width, Height, 0.2f), dark);
            back.AddComponent<BoxCollider>();

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
                GateRoomDecor.Box(room, "CofferBeam", new Vector3(i * 1.6f, Height - 0.06f, midZ), new Vector3(0.18f, 0.12f, depth - 0.4f), metal);
                GateRoomDecor.Box(room, "CeilingLight", new Vector3(i * 1.6f, Height - 0.125f, midZ), new Vector3(0.06f, 0.01f, depth - 1.2f),
                    art.Lit(Texture2D.whiteTexture, new Color(1f, 0.85f, 0.65f), 2.4f));
            }

            // ── Floor: carpet with a brass border under the desk and the stand ────
            var carpetMat = art.Lit(CarpetTex(), Color.white, 0f, 1f);
            var carpet = GateRoomDecor.Quad(room, "Carpet", new Vector3(0f, 0.004f, 0.8f), new Vector3(4.6f, 3.4f, 1f), carpetMat);
            carpet.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            // ── Captain's desk (centre): dark wood, the Empire screen low and reclined ─
            var desk = new GameObject("CaptainDesk").transform;
            desk.SetParent(room, false);
            desk.localPosition = new Vector3(0f, 0f, 1.3f);
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
            // Operator side is the stand (−z of the room is behind the player; the desk faces −z).
            refs.EmpireMount.localRotation = Quaternion.identity;

            refs.Nameplate = UiKit.Label(desk, "Nameplate", string.Empty, new Vector3(0f, 0.66f, -0.47f), 1.4f, 0.05f,
                new Color(1f, 0.85f, 0.6f));
            refs.Nameplate.richText = true;
            refs.Nameplate.fontStyle = FontStyles.Bold;
            GateRoomDecor.Box(desk, "NameplateBack", new Vector3(0f, 0.66f, -0.458f), new Vector3(1.5f, 0.1f, 0.01f), metal);

            // The capital as a hologram on the desk corner.
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
            gr.sharedMaterial = art.Holo(art.OrbitRing != null ? art.OrbitRing : Texture2D.whiteTexture, new Color(0.4f, 0.85f, 1f, 0.5f));
            gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var spin = globe.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 14f;
            spin.BobMeters = 0.006f;
            refs.Globe = globe.transform;

            // Captain's chair, behind the stand, facing the bay.
            var chair = new GameObject("CaptainChair").transform;
            chair.SetParent(room, false);
            chair.localPosition = new Vector3(0f, 0f, -0.75f);
            GateRoomDecor.Box(chair, "Base", new Vector3(0f, 0.03f, 0f), new Vector3(0.6f, 0.06f, 0.6f), metal);
            GateRoomDecor.Box(chair, "Post", new Vector3(0f, 0.25f, 0f), new Vector3(0.1f, 0.4f, 0.1f), metal);
            GateRoomDecor.Rounded(chair, "Seat", new Vector3(0.62f, 0.12f, 0.58f), 0.05f, new Vector3(0f, 0.5f, 0f), leather, accent, 0f);
            GateRoomDecor.Rounded(chair, "Back", new Vector3(0.62f, 0.85f, 0.12f), 0.05f, new Vector3(0f, 0.98f, -0.26f), leather, accent, 0f);
            for (var side = -1; side <= 1; side += 2)
            {
                GateRoomDecor.Box(chair, "Arm", new Vector3(side * 0.34f, 0.7f, 0f), new Vector3(0.08f, 0.06f, 0.5f), metal);
                GateRoomDecor.Box(chair, "ArmGlow", new Vector3(side * 0.34f, 0.735f, 0f), new Vector3(0.02f, 0.008f, 0.4f), brass);
            }

            // ── Side desks: progression (port), Nova showcase desk (starboard) ────
            refs.ProgressMount = GateRoomDecor.Desk(room, "ProgressDesk", new Vector3(-1.6f, 0f, 0.35f),
                GateRoomDecor.FaceStand(-1.6f, 0.35f), 1.2f, metal, dark, cyan, brass);
            refs.ShopMount = GateRoomDecor.Desk(room, "ShopDesk", new Vector3(1.6f, 0f, 0.35f),
                GateRoomDecor.FaceStand(1.6f, 0.35f), 1.2f, metal, dark, cyan, brass);

            // ── Trophy wall (port): plaques for achievements ───────────────────────
            var wallX = -HalfWidth + 0.13f;
            var trophy = new GameObject("TrophyWall").transform;
            trophy.SetParent(room, false);
            trophy.localPosition = new Vector3(wallX, 0f, 2.1f);
            // Local −z = into the room (+x world): face the stand side.
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

            // ── Starboard: Nova showcase and the Comms wall terminal ───────────────
            var show = new GameObject("Showcase").transform;
            show.SetParent(room, false);
            show.localPosition = new Vector3(HalfWidth - 0.7f, 0f, 3.3f);
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

            // Comms terminal on the starboard wall, level with the desk; a wake button under it.
            var comms = new GameObject("CommsTerminal").transform;
            comms.SetParent(room, false);
            comms.localPosition = new Vector3(HalfWidth - 0.13f, 0f, 1.2f);
            comms.localRotation = Quaternion.Euler(0f, 90f, 0f);
            GateRoomDecor.Box(comms, "Bezel", new Vector3(0f, 1.55f, 0.03f), new Vector3(1.26f, 0.86f, 0.05f), UiKit.Chassis);
            GateRoomDecor.Box(comms, "Glow", new Vector3(0f, 1.1f, -0.01f), new Vector3(1.2f, 0.015f, 0.01f), art.Lit(Texture2D.whiteTexture, new Color(0.55f, 0.85f, 0.45f), 2.4f));
            refs.CommsMount = new GameObject("CommsMount").transform;
            refs.CommsMount.SetParent(comms, false);
            refs.CommsMount.localPosition = new Vector3(0f, 1.55f, -0.02f);
            refs.CommsWakeMount = new GameObject("CommsWake").transform;
            refs.CommsWakeMount.SetParent(comms, false);
            refs.CommsWakeMount.localPosition = new Vector3(0f, 0.98f, -0.03f);

            // ── Lounge corner (port aft): couch and low table ─────────────────────
            var lounge = new GameObject("Lounge").transform;
            lounge.SetParent(room, false);
            lounge.localPosition = new Vector3(-HalfWidth + 0.75f, 0f, Back + 1.2f);
            // Local +z (the couch back) against the port wall, the table toward the room.
            lounge.localRotation = Quaternion.Euler(0f, -90f, 0f);
            GateRoomDecor.Rounded(lounge, "CouchSeat", new Vector3(1.8f, 0.4f, 0.7f), 0.08f, new Vector3(0f, 0.22f, 0f), leather, accent, 0f);
            GateRoomDecor.Rounded(lounge, "CouchBack", new Vector3(1.8f, 0.55f, 0.18f), 0.07f, new Vector3(0f, 0.62f, 0.3f), leather, accent, 0f);
            GateRoomDecor.Box(lounge, "Table", new Vector3(0f, 0.36f, -0.85f), new Vector3(1f, 0.04f, 0.5f), wood);
            GateRoomDecor.Box(lounge, "TableLeg", new Vector3(0f, 0.17f, -0.85f), new Vector3(0.12f, 0.34f, 0.12f), metal);

            // Data-crystal shelves on the aft wall, either side of the door.
            for (var side = -1; side <= 1; side += 2)
            {
                if (side > 0)
                    continue;
                for (var s = 0; s < 3; s++)
                {
                    var y = 1.2f + s * 0.45f;
                    GateRoomDecor.Box(room, "Shelf", new Vector3(-1.2f, y, Back + 0.2f), new Vector3(1.8f, 0.03f, 0.25f), wood);
                    for (var k = 0; k < 7; k++)
                    {
                        var hue = (k * 37 + s * 11) % 3;
                        var c = hue == 0 ? CicArtKit.Cyan : hue == 1 ? CicArtKit.Amber : new Color(0.6f, 0.5f, 1f);
                        GateRoomDecor.Box(room, "Crystal", new Vector3(-1.95f + k * 0.24f, y + 0.09f, Back + 0.2f),
                            new Vector3(0.04f, 0.15f + (k % 3) * 0.02f, 0.04f), art.Lit(Texture2D.whiteTexture, c, 1.6f));
                    }
                }
            }

            return refs;
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
