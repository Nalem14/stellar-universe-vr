using System;
using System.Collections.Generic;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Stations
{
    /// <summary>
    /// The dry dock's module store: a lit wall of shelves holding one block per module type the picked hull may
    /// carry (every type when none is picked: fortress modules too), each a miniature of the module's own deck
    /// silhouette (<see cref="ShipHullBuilder.ModuleMesh"/>) on a cartridge plinth banded in its family colour.
    /// A type the hangar holds is solid: grab it (grip, or ray + trigger; E / tap on a flat screen) and set it
    /// on a grid cell of the assembly table = PlaceShipModule, or drop it in the recycler = DelShip. A type it
    /// lacks is a hologram: trigger it twice to have the printer (<see cref="ModulePrinter"/>) make one — AddShip
    /// through the shipyard's rules (<see cref="ShipyardPanel"/>); the shuttle delivers it here.
    /// Each bay has a name plate on the shelf lip, sized to be read from the assembly table: name, hangar count,
    /// the hulls that take it (ship / fortress pills). The control column filters the wall by hull (all, ships,
    /// fortresses) and by stock (all, in the hangar only).
    /// More types than the wall holds: a robot gantry runs along the shelves and swaps the page, block by block,
    /// with a replicator sweep (SU/ModuleBlock). Hovering a block shows its name, stock, stats and description on
    /// one shared card. Quest budget: one shared material and one draw per block, the frame merged by static
    /// batching, one text per plate, one card.
    /// Local frame: origin on the floor at the wall face, centre of the rack; the room lies toward -Z.
    /// </summary>
    public sealed class ModuleShelves : MonoBehaviour
    {
        const int Rows = 3;
        const int Cols = 4;
        const int PerPage = Rows * Cols;
        const float Length = 2.7f;
        const float Depth = 0.55f;
        const float Pitch = 0.64f;
        const float RowBase = 0.66f;
        const float RowPitch = 0.56f;
        const float Top = 2.36f;
        const float BlockSize = 0.2f;
        /// <summary>Blocks stand larger on the wall than on the table (read from the assembly table, 4.6 m off).</summary>
        const float ShelfScale = 1.3f;
        /// <summary>Name plate under each bay, on the shelf's front lip: name, stock, hull pills.</summary>
        const float PlateH = 0.17f;
        const float PlinthH = 0.035f;
        const float CycleTime = 1.9f;
        const float SweepWindow = 0.45f;
        const float FlightTime = 0.4f;
        const float Solid = 1.1f;
        static readonly Color Accent = new(0.4f, 0.95f, 0.55f, 1f);
        static readonly Quaternion BlockYaw = Quaternion.Euler(0f, 115f, 0f);
        static readonly Color FilterOff = new(0.3f, 0.4f, 0.46f, 1f);

        static readonly int AccentId = Shader.PropertyToID("_Accent");
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int GhostId = Shader.PropertyToID("_Ghost");
        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly Dictionary<string, Mesh> BlockMeshes = new();
        static Material _blockMat;

        sealed class Slot
        {
            public int Index;
            public Transform Home;
            public GameObject Block;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public BoxCollider BlockCol;
            public BoxCollider PadCol;
            public string Type;
            public string Next;
            public bool Swapped;
            public int Count;
            public float Reveal = Solid;
            public float RevealTarget = Solid;
            public bool ReturnAfterDissolve;
            public bool InProduction;
            public string Refusal;
            public float Flight = -1f;
            public Vector3 FlightFrom;
            public Quaternion FlightFromRot;
        }

        CicArtKit _art;
        Func<Dictionary<string, int>> _stock;
        Func<bool> _canFit;
        Func<string> _selected;
        Action<string> _onPick;
        Func<string, Vector3, bool> _onDrop;
        ShipyardPanel _yard;
        Action<string, bool> _status;
        /// <summary>Why the picked hull may not take this type (native key), or null.</summary>
        Func<string, string> _refusal;
        string _armedType;
        float _armedUntil;
        bool _ordering;

        readonly Slot[] _slots = new Slot[PerPage];
        readonly TextMeshPro[] _plates = new TextMeshPro[PerPage];
        /// <summary>Hull filter: 0 every module, 1 those a ship takes, 2 those a fortress takes.</summary>
        int _hullFilter;
        /// <summary>Only the types the hangar holds.</summary>
        bool _stockOnly;
        readonly PokeButton[] _hullButtons = new PokeButton[3];
        readonly PokeButton[] _stockButtons = new PokeButton[2];
        readonly List<string> _types = new();
        readonly HashSet<ModuleFamily> _pageFamilies = new();
        readonly System.Text.StringBuilder _sb = new(256);
        Dictionary<string, int> _counts = new();
        MaterialPropertyBlock _mpb;
        Transform _gantry;
        Transform _gantryHead;
        GameObject _gantryBeam;
        TextMeshPro _pageLabel;
        TextMeshPro _familyLabel;
        Transform _card;
        TextMeshPro _cardText;
        MeshRenderer _cardBack;
        Slot _cardSlot;
        Slot _held;
        int _page;
        int _pages = 1;
        bool _cycling;
        float _cycleT;
        float _dir = -1f;

        public bool Holding => _held != null;

        /// <summary>Top of the header's aft end (gantry side), where the printer's transfer rail docks.</summary>
        public Vector3 HeaderEndWorld => transform.TransformPoint(new Vector3(Length * 0.5f + 0.08f, Top + 0.24f, -Depth * 0.5f));
        public Vector3 HeldPosition => _held != null ? _held.Block.transform.position : Vector3.zero;

        public static ModuleShelves Build(Transform room, CicArtKit art, Vector3 localPos, Quaternion localRot,
            Func<Dictionary<string, int>> stock, Func<bool> canFit, Func<string> selected,
            Action<string> onPick, Func<string, Vector3, bool> onDrop, ShipyardPanel yard, Action<string, bool> status,
            Func<string, string> refusal)
        {
            var go = new GameObject("ModuleStore");
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var s = go.AddComponent<ModuleShelves>();
            s._art = art;
            s._stock = stock;
            s._canFit = canFit;
            s._selected = selected;
            s._onPick = onPick;
            s._onDrop = onDrop;
            s._yard = yard;
            s._status = status;
            s._refusal = refusal;
            s._mpb = new MaterialPropertyBlock();
            s.BuildFrame();
            s.BuildSlots();
            s.BuildControls();
            s.BuildCard();
            s.enabled = false;
            return s;
        }

        // ── Frame ─────────────────────────────────────────────────────────────────

        GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool solid = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!solid)
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static float SlotX(int col) => (col - (Cols - 1) * 0.5f) * Pitch;
        static float ShelfY(int row) => RowBase + row * RowPitch;

        void BuildFrame()
        {
            var frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);
            var wall = _art.MetalPanel(0.5f);
            var dark = _art.DarkPanel(0.35f);
            var cyan = _art.CyanEmit(2.4f);
            var green = _art.Lit(Texture2D.whiteTexture, Accent, 2.2f);
            var amber = _art.AmberEmit(2f);
            var back = _art.Lit(_art.Panel != null ? _art.Panel : Texture2D.whiteTexture, new Color(0.12f, 0.15f, 0.19f), 0.3f, 2.2f);
            const float half = Length * 0.5f;

            // Carcass: recessed back, posts, plinth and a header carrying the sign.
            Box(frame, "Back", new Vector3(0f, Top * 0.5f, -0.03f), new Vector3(Length + 0.1f, Top, 0.06f), back, true);
            foreach (var side in new[] { -1f, 1f })
            {
                Box(frame, "Post" + side, new Vector3(side * (half + 0.04f), Top * 0.5f, -Depth * 0.5f),
                    new Vector3(0.08f, Top, Depth), dark, true);
                Box(frame, "PostSeam" + side, new Vector3(side * (half + 0.04f), Top * 0.5f, -Depth - 0.002f),
                    new Vector3(0.012f, Top * 0.82f, 0.01f), cyan);
            }

            Box(frame, "Plinth", new Vector3(0f, 0.12f, -Depth * 0.5f), new Vector3(Length, 0.24f, Depth), dark, true);
            Box(frame, "PlinthGlow", new Vector3(0f, 0.245f, -Depth - 0.002f), new Vector3(Length * 0.96f, 0.012f, 0.01f), green);
            Box(frame, "Header", new Vector3(0f, Top + 0.12f, -Depth * 0.5f + 0.04f), new Vector3(Length + 0.16f, 0.24f, Depth - 0.08f), dark);
            Box(frame, "HeaderEdge", new Vector3(0f, Top + 0.005f, -Depth - 0.002f + 0.08f), new Vector3(Length + 0.1f, 0.012f, 0.01f), cyan);

            // Shelves: brushed plate, dark front lip (for the stock row), a cool light strip under each one
            // washing the blocks below, and a lit pad per slot.
            for (var row = 0; row < Rows; row++)
            {
                var y = ShelfY(row);
                Box(frame, "Shelf" + row, new Vector3(0f, y - 0.015f, -Depth * 0.5f), new Vector3(Length, 0.03f, Depth), wall);
                Box(frame, "Lip" + row, new Vector3(0f, y - PlateH * 0.5f, -Depth + 0.01f), new Vector3(Length, PlateH, 0.02f), dark);
                Box(frame, "LipGlow" + row, new Vector3(0f, y - PlateH - 0.003f, -Depth - 0.001f), new Vector3(Length * 0.98f, 0.006f, 0.008f), green);
                for (var col = 0; col < Cols - 1; col++)
                    Box(frame, "LipSplit" + row + col, new Vector3(SlotX(col) + Pitch * 0.5f, y - PlateH * 0.5f, -Depth - 0.001f),
                        new Vector3(0.006f, PlateH * 0.75f, 0.006f), cyan);
                Box(frame, "UnderLight" + row, new Vector3(0f, y - 0.064f, -Depth * 0.55f), new Vector3(Length * 0.95f, 0.006f, 0.05f), cyan);
                for (var col = 0; col < Cols; col++)
                {
                    Box(frame, "Pad" + row + col, new Vector3(SlotX(col), y + 0.002f, -Depth * 0.5f),
                        new Vector3(0.36f, 0.004f, 0.36f), dark);
                    Box(frame, "PadRing" + row + col, new Vector3(SlotX(col), y + 0.0025f, -Depth * 0.5f + 0.182f),
                        new Vector3(0.36f, 0.004f, 0.006f), green);
                }

                // Dividers between slots (small fins), so each block has its bay.
                for (var col = 0; col < Cols - 1; col++)
                    Box(frame, "Fin" + row + col, new Vector3(SlotX(col) + Pitch * 0.5f, y + 0.07f, -Depth * 0.5f),
                        new Vector3(0.012f, 0.14f, Depth * 0.9f), dark);
            }

            // Gantry rails, top and bottom, along the front.
            Box(frame, "RailTop", new Vector3(0f, Top - 0.03f, -Depth - 0.05f), new Vector3(Length + 0.1f, 0.05f, 0.06f), dark);
            Box(frame, "RailBottom", new Vector3(0f, 0.27f, -Depth - 0.05f), new Vector3(Length + 0.1f, 0.05f, 0.06f), dark);
            Box(frame, "RailTopGlow", new Vector3(0f, Top - 0.058f, -Depth - 0.05f), new Vector3(Length, 0.006f, 0.02f), amber);

            // Everything above is fixed: merged into a few batches.
            StaticBatchingUtility.Combine(frame.gameObject);

            // The robot: a vertical gantry riding the rails, a picker head that climbs it, a scan blade.
            _gantry = new GameObject("Gantry").transform;
            _gantry.SetParent(transform, false);
            _gantry.localPosition = new Vector3(half + 0.1f, 0f, -Depth - 0.05f);
            Box(_gantry, "Mast", new Vector3(0f, (Top + 0.27f) * 0.5f, 0f), new Vector3(0.05f, Top - 0.3f, 0.05f), wall);
            Box(_gantry, "MastSeam", new Vector3(0f, (Top + 0.27f) * 0.5f, -0.027f), new Vector3(0.01f, Top - 0.4f, 0.004f), amber);
            _gantryHead = new GameObject("Head").transform;
            _gantryHead.SetParent(_gantry, false);
            _gantryHead.localPosition = new Vector3(0f, 1.2f, -0.02f);
            Box(_gantryHead, "Carriage", Vector3.zero, new Vector3(0.12f, 0.1f, 0.09f), dark);
            Box(_gantryHead, "Grip", new Vector3(0f, -0.02f, 0.07f), new Vector3(0.1f, 0.02f, 0.08f), wall);
            Box(_gantryHead, "Eye", new Vector3(0f, 0.02f, -0.047f), new Vector3(0.06f, 0.014f, 0.004f), amber);
            _gantryBeam = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _gantryBeam.name = "ScanBlade";
            Destroy(_gantryBeam.GetComponent<Collider>());
            _gantryBeam.transform.SetParent(_gantry, false);
            _gantryBeam.transform.localPosition = new Vector3(0f, (Top + 0.27f) * 0.5f, 0.2f);
            _gantryBeam.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            _gantryBeam.transform.localScale = new Vector3(0.42f, Top - 0.35f, 1f);
            _gantryBeam.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(
                _art.ProjectorGlow != null ? _art.ProjectorGlow : Texture2D.whiteTexture, new Color(0.4f, 1f, 0.6f, 0.35f));
            _gantryBeam.SetActive(false);

            // Sign on the header, and the page's families beneath it.
            var sign = UiKit.Label(transform, "Sign", Trans.Get("vr.dock.store.title"),
                new Vector3(0f, Top + 0.165f, -Depth + 0.035f), Length * 0.8f, 0.065f, Accent);
            sign.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            // What the filters show, and the page's families in their colours.
            _familyLabel = UiKit.Label(transform, "Families", string.Empty,
                new Vector3(0f, Top + 0.06f, -Depth + 0.035f), Length * 0.96f, 0.04f, UiKit.TextDim);
            _familyLabel.richText = true;

            // One plate per bay on the shelf lip, readable from the assembly table: name (two lines at most),
            // then the hangar count and which hulls take it.
            for (var i = 0; i < PerPage; i++)
            {
                var row = Rows - 1 - i / Cols;
                var plate = UiKit.Label(transform, "Plate" + i, string.Empty,
                    new Vector3(SlotX(i % Cols), ShelfY(row) - PlateH * 0.5f, -Depth - 0.003f), Pitch - 0.06f, 0.034f,
                    UiKit.TextBright, TextAlignmentOptions.Center, wrap: true);
                plate.rectTransform.sizeDelta = new Vector2((Pitch - 0.06f) * 100f, PlateH * 100f - 1f);
                plate.fontSizeMin = 0.02f * 1400f;
                plate.richText = true;
                _plates[i] = plate;
            }
        }

        // ── Slots / blocks ────────────────────────────────────────────────────────

        void BuildSlots()
        {
            for (var i = 0; i < PerPage; i++)
            {
                // Reading order: top shelf first, left to right as seen from the room.
                var row = Rows - 1 - i / Cols;
                var col = i % Cols;
                var slot = new Slot { Index = i };
                slot.Home = new GameObject("Slot" + i).transform;
                slot.Home.SetParent(transform, false);
                slot.Home.localPosition = new Vector3(SlotX(col), ShelfY(row) + 0.004f, -Depth * 0.5f);

                // Pad: keeps the hover card while the block itself is not grabbable (page swap, dissolving).
                var pad = new GameObject("Pad");
                pad.transform.SetParent(slot.Home, false);
                slot.PadCol = pad.AddComponent<BoxCollider>();
                slot.PadCol.center = new Vector3(0f, 0.17f, 0f);
                slot.PadCol.size = new Vector3(0.42f, 0.36f, 0.42f);
                var pi = pad.AddComponent<XRSimpleInteractable>();
                var s = slot;
                pi.selectEntered.AddListener(_ => OnPad(s));
                pi.hoverEntered.AddListener(_ => ShowCard(s));
                pi.hoverExited.AddListener(_ => HideCard(s));

                var block = new GameObject("Block");
                block.transform.SetParent(slot.Home, false);
                block.transform.localRotation = BlockYaw;
                block.transform.localScale = Vector3.one * ShelfScale;
                slot.Block = block;
                slot.Filter = block.AddComponent<MeshFilter>();
                slot.Renderer = block.AddComponent<MeshRenderer>();
                slot.Renderer.sharedMaterial = BlockMat();
                slot.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                slot.Renderer.receiveShadows = false;
                slot.Renderer.enabled = false;
                slot.BlockCol = block.AddComponent<BoxCollider>();
                slot.BlockCol.enabled = false;
                var body = block.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                var grab = block.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.throwOnDetach = false;
                grab.useDynamicAttach = true;
                grab.selectEntered.AddListener(_ => OnGrab(s));
                grab.selectExited.AddListener(_ => OnRelease(s));
                grab.hoverEntered.AddListener(_ => ShowCard(s));
                grab.hoverExited.AddListener(_ => HideCard(s));
                // Headset: the trigger carries it along the ray as well as the grip grabs it (TriggerSelect).
                block.AddComponent<TriggerGrab>();
                // Flat screens: E (PC) / a tap (mobile) takes the block, it follows the aim, a click / tap sets it
                // down on a grid cell or in the recycler (the same drop as letting go with the hand).
                if (Core.App.PcPlatformBoot.IsFlatScreen)
                {
                    var flat = block.AddComponent<Core.UI.FlatGrabbable>();
                    flat.Begin = () => BeginFlat(s);
                    flat.Label = () => s.Type != null ? Trans.Get(ModuleCatalog.NameKey(s.Type)) : null;
                }

                _slots[i] = slot;
            }
        }

        internal static Material BlockMat()
        {
            if (_blockMat != null)
                return _blockMat;
            var shader = Shader.Find("SU/ModuleBlock");
            if (shader == null)
            {
                // Always Included in GraphicsSettings; reaching this means a broken build setup.
                Debug.LogWarning("[SU] ModuleShelves: SU/ModuleBlock missing, falling back to SU/HullInterior.");
                shader = Shader.Find("SU/HullInterior") ?? Shader.Find("SU/UnlitEmissive");
            }

            _blockMat = new Material(shader) { name = "SU_ModuleBlock", enableInstancing = true };
            return _blockMat;
        }

        /// <summary>
        /// The block of one module type: cartridge plinth (dark body, brushed top, family band) and the module's
        /// deck silhouette in miniature on it, one vertex-coloured mesh; uv.x = height 0..1 for the replicator.
        /// </summary>
        internal static Mesh BlockMesh(string type)
        {
            if (BlockMeshes.TryGetValue(type, out var cached) && cached != null)
                return cached;
            var verts = new List<Vector3>(2048);
            var normals = new List<Vector3>(2048);
            var colors = new List<Color>(2048);
            var tris = new List<int>(4096);
            var accent = ModuleCatalog.Accent(ModuleCatalog.Family(type));
            AddBox(verts, normals, colors, tris, new Vector3(0f, PlinthH * 0.5f, 0f), new Vector3(0.21f, PlinthH, 0.21f),
                new Color(0.2f, 0.23f, 0.27f, 0f));
            AddBox(verts, normals, colors, tris, new Vector3(0f, PlinthH + 0.003f, 0f), new Vector3(0.18f, 0.006f, 0.18f),
                new Color(0.52f, 0.56f, 0.62f, 0f));
            AddBox(verts, normals, colors, tris, new Vector3(0f, PlinthH * 0.55f, 0f), new Vector3(0.216f, 0.007f, 0.216f),
                new Color(accent.r, accent.g, accent.b, 1f));
            foreach (var sx in new[] { -1f, 1f })
                AddBox(verts, normals, colors, tris, new Vector3(sx * 0.098f, PlinthH + 0.012f, 0f),
                    new Vector3(0.014f, 0.024f, 0.06f), new Color(0.3f, 0.33f, 0.38f, 0f));

            var mini = ShipHullBuilder.ModuleMesh(type);
            if (mini != null)
            {
                var b = mini.bounds;
                var s = BlockSize / Mathf.Max(0.01f, Mathf.Max(b.size.x, Mathf.Max(b.size.z, b.size.y * 0.85f)));
                var lift = PlinthH + 0.006f;
                var mv = mini.vertices;
                var mn = mini.normals;
                var mc = mini.colors;
                var mt = mini.triangles;
                var first = verts.Count;
                for (var i = 0; i < mv.Length; i++)
                {
                    verts.Add(new Vector3(mv[i].x * s, mv[i].y * s + lift, mv[i].z * s));
                    normals.Add(i < mn.Length ? mn[i] : Vector3.up);
                    colors.Add(i < mc.Length ? mc[i] : new Color(0.6f, 0.63f, 0.68f, 0f));
                }

                for (var i = 0; i < mt.Length; i++)
                    tris.Add(first + mt[i]);
            }

            var top = 0.01f;
            foreach (var v in verts)
                top = Mathf.Max(top, v.y);
            var uvs = new List<Vector2>(verts.Count);
            foreach (var v in verts)
                uvs.Add(new Vector2(v.y / top, 0f));
            var mesh = new Mesh { name = "ModuleBlock_" + type };
            if (verts.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            BlockMeshes[type] = mesh;
            return mesh;
        }

        static readonly Vector3[] FaceN =
        {
            Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
        };

        static void AddBox(List<Vector3> v, List<Vector3> n, List<Color> c, List<int> t, Vector3 centre, Vector3 size,
            Color color)
        {
            var h = size * 0.5f;
            foreach (var f in FaceN)
            {
                // Two axes spanning the face, wound clockwise seen from outside (Unity front faces).
                var u = f.y != 0f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(f, u);
                var o = centre + Vector3.Scale(f, h);
                var hu = Vector3.Scale(u, h);
                var hw = Vector3.Scale(w, h);
                var i0 = v.Count;
                v.Add(o - hu - hw);
                v.Add(o - hu + hw);
                v.Add(o + hu + hw);
                v.Add(o + hu - hw);
                for (var k = 0; k < 4; k++)
                {
                    n.Add(f);
                    c.Add(color);
                }

                t.Add(i0);
                t.Add(i0 + 1);
                t.Add(i0 + 2);
                t.Add(i0);
                t.Add(i0 + 2);
                t.Add(i0 + 3);
            }
        }

        // ── Controls ──────────────────────────────────────────────────────────────

        void BuildControls()
        {
            // Control column at the rack's north end (nearest the assembly table), hand height: what the wall
            // shows (hull, stock) above, the pages below.
            var x = -(Length * 0.5f + 0.4f);
            var column = new GameObject("Controls").transform;
            column.SetParent(transform, false);
            column.localPosition = new Vector3(x, 0f, -0.2f);
            var dark = _art.DarkPanel(0.35f);
            var cyan = _art.CyanEmit(2.4f);
            Box(column, "Column", new Vector3(0f, 0.7f, 0f), new Vector3(0.64f, 1.4f, 0.3f), dark, true);
            Box(column, "Face", new Vector3(0f, 1.16f, -0.152f), new Vector3(0.6f, 0.5f, 0.004f), _art.MetalPanel(0.45f));
            Box(column, "FaceGlow", new Vector3(0f, 1.405f, -0.152f), new Vector3(0.6f, 0.01f, 0.006f), cyan);
            Box(column, "FaceSplit", new Vector3(0f, 1.085f, -0.153f), new Vector3(0.54f, 0.004f, 0.004f), cyan);
            StaticBatchingUtility.Combine(column.gameObject);

            const float z = -0.16f;
            var hullLabels = new[] { "all", "vr.module.filterShip", "vr.module.filterStation" };
            for (var i = 0; i < 3; i++)
            {
                var hull = i;
                _hullButtons[i] = PokeButton.Create(column, "Hull" + i, Trans.Get(hullLabels[i]),
                    new Vector3(-0.19f + i * 0.19f, 1.33f, z), Quaternion.identity, new Vector2(0.175f, 0.075f),
                    HullColor(i), () => SetHullFilter(hull));
            }

            var stockLabels = new[] { "vr.dock.shelf.showAll", "vr.dock.shelf.showStock" };
            for (var i = 0; i < 2; i++)
            {
                var only = i == 1;
                _stockButtons[i] = PokeButton.Create(column, "Stock" + i, Trans.Get(stockLabels[i]),
                    new Vector3(-0.142f + i * 0.284f, 1.2f, z), Quaternion.identity, new Vector2(0.27f, 0.075f),
                    Accent, () => SetStockFilter(only));
            }

            PokeButton.Create(column, "PagePrev", "‹", new Vector3(-0.2f, 1.0f, z), Quaternion.identity,
                new Vector2(0.12f, 0.08f), Accent, () => Turn(-1));
            PokeButton.Create(column, "PageNext", "›", new Vector3(0.2f, 1.0f, z), Quaternion.identity,
                new Vector2(0.12f, 0.08f), Accent, () => Turn(1));
            _pageLabel = UiKit.Label(column, "Page", "1 / 1", new Vector3(0f, 1.0f, z - 0.002f), 0.24f, 0.034f,
                UiKit.TextBright);
            PaintFilters();
        }

        static Color HullColor(int hull) => hull == 1 ? ModuleCatalog.ShipTagColor : hull == 2 ? ModuleCatalog.StationTagColor : Accent;

        /// <summary>The chosen hull and stock buttons lit in their colour, the others dimmed.</summary>
        void PaintFilters()
        {
            for (var i = 0; i < _hullButtons.Length; i++)
            {
                var on = _hullFilter == i;
                _hullButtons[i].SetAccent(on ? HullColor(i) : FilterOff);
                _hullButtons[i].Label.color = on ? UiKit.TextBright : UiKit.TextDim;
            }

            for (var i = 0; i < _stockButtons.Length; i++)
            {
                var on = _stockOnly == (i == 1);
                _stockButtons[i].SetAccent(on ? Accent : FilterOff);
                _stockButtons[i].Label.color = on ? UiKit.TextBright : UiKit.TextDim;
            }
        }

        void SetHullFilter(int hull)
        {
            if (_held != null || _cycling)
            {
                CicCue.Fail(_gantry.position);
                return;
            }

            _hullFilter = hull;
            _page = 0;
            PaintFilters();
            Refresh(true);
        }

        void SetStockFilter(bool only)
        {
            if (_held != null || _cycling)
            {
                CicCue.Fail(_gantry.position);
                return;
            }

            _stockOnly = only;
            _page = 0;
            PaintFilters();
            Refresh(true);
        }

        /// <summary>The filters let this type on the wall.</summary>
        bool Shown(string type, int count)
        {
            if (_stockOnly && count <= 0)
                return false;
            if (_hullFilter == 0)
                return true;
            var c = ModuleCatalog.Compat(type);
            return _hullFilter == 1 ? c.Ship : c.Station;
        }

        void Turn(int step)
        {
            if (_held != null || _cycling || _pages <= 1)
            {
                CicCue.Fail(_gantry.position);
                return;
            }

            _page = (_page + step + _pages) % _pages;
            Refresh(true);
        }

        // ── Hover card ────────────────────────────────────────────────────────────

        void BuildCard()
        {
            _card = new GameObject("HoverCard").transform;
            _card.SetParent(transform, false);
            // Name · stock · stats · description: tall enough for a two-line description.
            var back = UiKit.MeshPiece(_card, "Back", UiMeshes.RoundedBox(new Vector3(0.7f, 0.36f, 0.012f), 0.014f),
                UiKit.Chassis, new Vector3(0f, 0f, 0.008f));
            _cardBack = back.GetComponent<MeshRenderer>();
            _cardText = UiKit.Label(_card, "Text", string.Empty, new Vector3(0f, 0f, -0.001f), 0.6f, 0.026f,
                UiKit.TextBright, TextAlignmentOptions.MidlineLeft, wrap: true);
            _cardText.richText = true;
            _cardText.enableAutoSizing = true;
            _cardText.fontSizeMin = 0.014f * 1400f;
            _cardText.fontSizeMax = 0.026f * 1400f;
            _cardText.rectTransform.sizeDelta = new Vector2(60f, 31f);
            _card.gameObject.SetActive(false);
        }

        void ShowCard(Slot s)
        {
            if (s.Type == null || _cycling || _held != null)
                return;
            _cardSlot = s;
            CicCue.Hover(s.Home.position);
            // Clear of the shelf lips (their name plates stand at the rack's face), so nothing cuts through it.
            // Above the block, except on the top shelf: there the header and the gantry rail would cover it, so it
            // hangs below the block instead.
            var top = s.Home.localPosition.y > ShelfY(Rows - 1) - 0.05f;
            _card.localPosition = new Vector3(s.Home.localPosition.x, s.Home.localPosition.y + (top ? -0.32f : 0.5f), -Depth - 0.07f);
            _card.gameObject.SetActive(true);
            RenderCard();
            ApplyAll();
        }

        void HideCard(Slot s)
        {
            if (_cardSlot != s)
                return;
            _cardSlot = null;
            _card.gameObject.SetActive(false);
            ApplyAll();
        }

        void RenderCard()
        {
            var s = _cardSlot;
            if (s == null || s.Type == null)
                return;
            var fam = ModuleCatalog.Family(s.Type);
            _sb.Clear();
            _sb.Append("<b>").Append(Trans.Get(ModuleCatalog.NameKey(s.Type))).Append("</b>")
                .Append("  <size=75%><color=#").Append(ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(fam))).Append('>')
                .Append(Trans.Get(ModuleCatalog.FamilyKey(fam))).Append("</color></size>  ")
                .Append(ModuleCatalog.CompatTags(s.Type)).Append("\n<size=85%>");
            if (s.Count > 0 && s.Refusal != null)
            {
                _sb.Append("<color=#7dffa0>").Append(Trans.Format("vr.dock.shelf.inStock", s.Count)).Append("</color> — ");
                _sb.Append("<color=#ffb04a>").Append(Trans.Get(s.Refusal)).Append("</color>");
            }
            else if (s.Count > 0)
            {
                _sb.Append("<color=#7dffa0>").Append(Trans.Format("vr.dock.shelf.inStock", s.Count)).Append("</color> — ");
                _sb.Append(_canFit() ? Trans.Get("vr.dock.shelf.grab") : "<color=#ffb04a>" + Trans.Get("vr.dock.pickShipFirst") + "</color>");
            }
            else if (s.InProduction)
            {
                _sb.Append("<color=#ffb04a>").Append(Trans.Get("vr.dock.shelf.inProduction")).Append("</color>");
            }
            else if (_yard != null)
            {
                var blocker = _yard.BuildBlocker(s.Type);
                if (blocker != null)
                    _sb.Append("<color=#ff6a5a>").Append(blocker).Append("</color>");
                else if (_armedType == s.Type && Time.unscaledTime <= _armedUntil)
                    _sb.Append("<color=#ffb04a>").Append(Trans.Format(_yard.YardBusy ? "vr.dock.shelf.confirmQueue" : "vr.dock.shelf.confirmBuild",
                        Trans.Get(ModuleCatalog.NameKey(s.Type)))).Append("</color>");
                else
                    _sb.Append(Trans.Get("vr.dock.shelf.fabricate"));
            }

            _sb.Append("</size>");
            var stats = ModuleCatalog.StatsLine(s.Type);
            if (stats.Length > 0)
                _sb.Append("\n<size=80%>").Append(stats).Append("</size>");
            _sb.Append("\n<size=70%><color=#9fc4d6>").Append(ModuleCatalog.Description(s.Type)).Append("</color></size>");
            if (s.Count == 0 && _yard != null)
                _sb.Append("\n<size=80%>").Append(_yard.CostText(s.Type)).Append("</size>");
            _cardText.text = _sb.ToString();
            _cardBack.GetPropertyBlock(_mpb);
            _mpb.SetColor(UiKit.AccentId, ModuleCatalog.Accent(fam));
            // A thin family-coloured bevel: brighter and the glow ran over the text.
            _mpb.SetFloat(UiKit.AccentMulId, 0.28f);
            _cardBack.SetPropertyBlock(_mpb);
            _mpb.Clear();
        }

        // ── State ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-read the stock and lay out the current page. <paramref name="animate"/>: the gantry swaps the
        /// blocks whose type changes (page turn); otherwise they change in place.
        /// </summary>
        public void Refresh(bool animate = false)
        {
            if (_cycling)
                return;
            _counts = _stock() ?? new Dictionary<string, int>();
            _types.Clear();
            foreach (var kv in _counts)
                if (Shown(kv.Key, kv.Value))
                    _types.Add(kv.Key);
            // What the hangar holds for this hull first, then what can be printed for it, then the rest (other
            // hull, cores); in unlock order within each.
            int Rank(string t) => _refusal?.Invoke(t) != null ? 2 : _counts[t] > 0 ? 0 : 1;
            _types.Sort((a, b) =>
            {
                var sa = Rank(a);
                var sb = Rank(b);
                if (sa != sb)
                    return sa.CompareTo(sb);
                return ModuleCatalog.CompareUnlock(a, b);
            });
            _pages = Mathf.Max(1, Mathf.CeilToInt(_types.Count / (float)PerPage));
            _page = Mathf.Clamp(_page, 0, _pages - 1);

            var changed = false;
            for (var i = 0; i < PerPage; i++)
            {
                var k = _page * PerPage + i;
                var s = _slots[i];
                s.Next = k < _types.Count ? _types[k] : null;
                if (s.Next != s.Type)
                    changed = true;
            }

            if (changed && animate && _held == null && gameObject.activeInHierarchy)
            {
                StartCycle();
                return;
            }

            for (var i = 0; i < PerPage; i++)
            {
                var s = _slots[i];
                if (s.Next != s.Type && s != _held)
                    SetType(s, s.Next);
                ReadState(s);
            }

            RenderLabels();
            ApplyAll();
            if (_cardSlot != null)
                RenderCard();
        }

        void ReadState(Slot s)
        {
            s.Count = s.Type != null && _counts.TryGetValue(s.Type, out var n) ? n : 0;
            s.InProduction = s.Type != null && s.Count == 0 && _yard != null && _yard.InProduction(s.Type);
            s.Refusal = s.Type != null ? _refusal?.Invoke(s.Type) : null;
            var pickable = s.Type != null && s.Count > 0 && s.Refusal == null && !_cycling;
            s.BlockCol.enabled = pickable;
            s.PadCol.enabled = s.Type != null && !pickable && !_cycling;
        }

        void SetType(Slot s, string type)
        {
            s.Type = type;
            s.Renderer.enabled = type != null;
            if (type == null)
            {
                s.Filter.sharedMesh = null;
                return;
            }

            var mesh = BlockMesh(type);
            s.Filter.sharedMesh = mesh;
            var b = mesh.bounds;
            s.BlockCol.center = b.center;
            s.BlockCol.size = b.size + Vector3.one * 0.02f;
        }

        void RenderLabels()
        {
            _pageLabel.text = (_page + 1) + " / " + _pages;
            // What the filters keep (hull in its colour, stock), how many, then the page's families.
            _sb.Clear();
            if (_hullFilter != 0)
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(HullColor(_hullFilter))).Append("><b>")
                    .Append(Trans.Get(_hullFilter == 1 ? "vr.module.filterShip" : "vr.module.filterStation")).Append("</b></color>  ·  ");
            if (_stockOnly)
                _sb.Append("<color=#7dffa0><b>").Append(Trans.Get("vr.dock.shelf.showStock")).Append("</b></color>  ·  ");
            _sb.Append("<color=#c7e6f5>").Append(_types.Count).Append(' ').Append(Trans.Get("modules")).Append("</color>");
            var families = 0;
            _pageFamilies.Clear();
            for (var i = 0; i < PerPage; i++)
            {
                var t = _slots[i].Next ?? _slots[i].Type;
                if (t == null)
                    continue;
                var f = ModuleCatalog.Family(t);
                if (!_pageFamilies.Add(f))
                    continue;
                _sb.Append(families == 0 ? "   —   " : "  ");
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(f))).Append('>')
                    .Append(Trans.Get(ModuleCatalog.FamilyKey(f))).Append("</color>");
                families++;
            }

            _familyLabel.text = _types.Count > 0 ? _sb.ToString() : Trans.Get("vr.dock.store.empty");

            for (var i = 0; i < PerPage; i++)
            {
                var s = _slots[i];
                var t = s.Next ?? s.Type;
                if (t == null)
                {
                    _plates[i].text = string.Empty;
                    continue;
                }

                var count = _counts.TryGetValue(t, out var n) ? n : 0;
                var refused = _refusal?.Invoke(t) != null;
                _sb.Clear();
                // Name bright when it can go on the picked hull now, dimmed when it cannot.
                _sb.Append(refused ? "<color=#8fa9b8>" : count > 0 ? "<color=#ffffff>" : "<color=#d8e6ee>")
                    .Append("<b>").Append(Trans.Get(ModuleCatalog.NameKey(t))).Append("</b></color>\n<size=78%>");
                if (count > 0)
                    _sb.Append(refused ? "<color=#8fa9b8>×" : "<color=#7dffa0>×").Append(count).Append("</color>");
                else if (_yard != null && _yard.InProduction(t))
                    _sb.Append("<color=#ffb04a>").Append(Trans.Get("vr.dock.shelf.inProduction")).Append("</color>");
                else
                    _sb.Append("<color=#ffb04a>×0</color>");
                _sb.Append("  ").Append(ModuleCatalog.CompatTags(t)).Append("</size>");
                _plates[i].text = _sb.ToString();
            }
        }

        void ApplyAll()
        {
            for (var i = 0; i < PerPage; i++)
                Apply(_slots[i]);
        }

        void Apply(Slot s)
        {
            if (s.Type == null)
                return;
            var accent = ModuleCatalog.Accent(ModuleCatalog.Family(s.Type));
            if (s.Count == 0 && s.InProduction)
                accent = UiKit.Amber;
            var hover = s == _cardSlot ? 1f : s == _held ? 0.8f : s.Type == _selected() ? 0.55f : 0f;
            if (s == _cardSlot && s.Count == 0 && !s.InProduction && _yard != null && !_yard.CanAfford(s.Type))
                accent = UiKit.Danger;
            _mpb.SetColor(AccentId, accent);
            _mpb.SetFloat(HoverId, hover);
            _mpb.SetFloat(GhostId, s.Count == 0 ? 1f : s.Refusal != null ? 0.6f : 0f);
            _mpb.SetFloat(RevealId, s.Reveal);
            s.Renderer.SetPropertyBlock(_mpb);
        }

        // ── Flat-screen grab (PC, mobile) ───────────────────────────────────────

        Core.UI.IFlatGrab BeginFlat(Slot s)
        {
            if (s.Type == null || _held != null || s.BlockCol == null || !s.BlockCol.enabled)
                return null;
            OnGrab(s);
            return new FlatBlock(this, s);
        }

        static readonly RaycastHit[] FlatHits = new RaycastHit[16];

        sealed class FlatBlock : Core.UI.IFlatGrab
        {
            readonly ModuleShelves _shelves;
            readonly Slot _slot;

            public FlatBlock(ModuleShelves shelves, Slot slot)
            {
                _shelves = shelves;
                _slot = slot;
            }

            /// <summary>Over what the aim meets (a cell of the grid, the recycler's maw), else at arm's length.</summary>
            public void Move(Ray aim)
            {
                var block = _slot.Block.transform;
                var at = aim.origin + aim.direction * 0.9f;
                var n = Physics.RaycastNonAlloc(aim, FlatHits, 9f, ~0, QueryTriggerInteraction.Ignore);
                var best = float.MaxValue;
                for (var i = 0; i < n; i++)
                {
                    var h = FlatHits[i];
                    if (h.collider.transform.IsChildOf(block) || h.distance >= best)
                        continue;
                    best = h.distance;
                    var recycler = h.collider.GetComponentInParent<ModuleRecycler>();
                    at = recycler != null ? recycler.MawWorld + Vector3.up * 0.1f : h.point + Vector3.up * 0.05f;
                }

                block.position = at;
            }

            public bool Drop(Ray aim)
            {
                Move(aim);
                _shelves.OnRelease(_slot);
                return true;
            }

            public void Cancel()
            {
                // Back on its shelf, no order (the drop at home is refused and the block flies home).
                _slot.Block.transform.position = _slot.Home.position;
                _shelves.OnRelease(_slot);
            }
        }

        // ── Grab / trigger ────────────────────────────────────────────────────────

        void OnGrab(Slot s)
        {
            if (s.Type == null)
                return;
            _held = s;
            s.Flight = -1f;
            s.Reveal = s.RevealTarget = Solid;
            HideCard(s);
            _card.gameObject.SetActive(false);
            CicCue.Clunk(s.Block.transform.position);
            _onPick?.Invoke(s.Type);
            Apply(s);
            enabled = true;
        }

        void OnRelease(Slot s)
        {
            if (_held != s)
                return;
            _held = null;
            var placed = _onDrop != null && _onDrop(s.Type, s.Block.transform.position);
            if (placed)
            {
                // Taken off the block onto the table: it dissolves there and the rack replicates it at home.
                s.BlockCol.enabled = false;
                s.RevealTarget = 0f;
                s.ReturnAfterDissolve = true;
            }
            else
            {
                s.Flight = 0f;
                s.FlightFrom = s.Block.transform.position;
                s.FlightFromRot = s.Block.transform.rotation;
            }

            enabled = true;
        }

        /// <summary>
        /// The trigger on a block that cannot be taken: in stock but no hull picked (say so), in production (say
        /// so), or a hologram — two triggers within 4 s order it from the printer (resources are spent).
        /// </summary>
        void OnPad(Slot s)
        {
            if (s.Type == null || _cycling)
                return;
            if (s.Count > 0)
            {
                // In stock but not for this hull (or a core: placed by the server, never by hand).
                _status?.Invoke(Trans.Get(s.Refusal ?? "vr.dock.pickShipFirst"), true);
                CicCue.Fail(s.Home.position);
                return;
            }

            if (s.InProduction)
            {
                _status?.Invoke(Trans.Get("vr.dock.shelf.inProduction"), false);
                CicCue.Pip(s.Home.position);
                return;
            }

            if (_yard == null)
                return;
            var blocker = _yard.BuildBlocker(s.Type);
            if (blocker != null)
            {
                _status?.Invoke(blocker, true);
                CicCue.Fail(s.Home.position);
                return;
            }

            if (_armedType != s.Type || Time.unscaledTime > _armedUntil)
            {
                _armedType = s.Type;
                _armedUntil = Time.unscaledTime + 4f;
                _status?.Invoke(Trans.Format(_yard.YardBusy ? "vr.dock.shelf.confirmQueue" : "vr.dock.shelf.confirmBuild",
                    Trans.Get(ModuleCatalog.NameKey(s.Type))), false);
                CicCue.Pip(s.Home.position);
                if (_cardSlot == s)
                    RenderCard();
                return;
            }

            _armedType = null;
            if (!_ordering)
                AsyncTap.Run(Fabricate(s));
        }

        async System.Threading.Tasks.Task Fabricate(Slot s)
        {
            _ordering = true;
            try
            {
                CicCue.Synth(s.Home.position);
                await _yard.Build(s.Type);
            }
            finally
            {
                _ordering = false;
            }

            Refresh();
        }

        /// <summary>Leaving the dock with a block in hand: the hand lets go (it never follows onto the bridge).</summary>
        public void DropHeld()
        {
            if (_held != null && _held.Block.TryGetComponent<XRGrabInteractable>(out var grab) && grab.isSelected)
                grab.interactionManager?.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
            if (_held != null)
                TriggerSelect.Drop(_held.Block);
            ResetAll();
        }

        /// <summary>Entering / leaving the dock: every block back on its shelf, the page settled, no card.</summary>
        public void ResetAll()
        {
            _held = null;
            _armedType = null;
            if (_cycling)
                FinishCycle();
            foreach (var s in _slots)
            {
                if (s == null)
                    continue;
                s.Flight = -1f;
                s.ReturnAfterDissolve = false;
                s.Reveal = s.RevealTarget = Solid;
                if (s.Block.transform.parent != s.Home)
                    s.Block.transform.SetParent(s.Home, false);
                s.Block.transform.localPosition = Vector3.zero;
                s.Block.transform.localRotation = BlockYaw;
                Apply(s);
            }

            if (_card != null)
                _card.gameObject.SetActive(false);
            _cardSlot = null;
        }

        // ── Robot page swap ───────────────────────────────────────────────────────

        void StartCycle()
        {
            _cycling = true;
            _cycleT = 0f;
            _dir = -_dir;
            _card.gameObject.SetActive(false);
            _cardSlot = null;
            foreach (var s in _slots)
            {
                s.Swapped = s.Next == s.Type;
                s.BlockCol.enabled = false;
                s.PadCol.enabled = false;
            }

            _gantryBeam.SetActive(true);
            CicCue.Synth(_gantry.position);
            RenderLabels();
            enabled = true;
        }

        void StepCycle(float dt)
        {
            _cycleT += dt / CycleTime;
            var t = MotionEase.Smooth01(_cycleT);
            var x0 = Length * 0.5f + 0.1f;
            var gx = _dir > 0f ? Mathf.Lerp(-x0, x0, t) : Mathf.Lerp(x0, -x0, t);
            _gantry.localPosition = new Vector3(gx, 0f, _gantry.localPosition.z);
            // The picker head works the shelves up and down as it goes.
            var hy = ShelfY(0) + 0.1f + (ShelfY(Rows - 1) - ShelfY(0)) * (0.5f + 0.5f * Mathf.Sin(_cycleT * Mathf.PI * 7f));
            _gantryHead.localPosition = new Vector3(0f, hy, -0.02f);

            foreach (var s in _slots)
            {
                if (s.Swapped && s.Reveal >= Solid)
                    continue;
                var d = (gx - s.Home.localPosition.x) * _dir / SweepWindow;
                if (d < -1f)
                    continue;
                if (d < 0f)
                {
                    s.Reveal = -d * Solid;
                }
                else
                {
                    if (!s.Swapped)
                    {
                        s.Swapped = true;
                        SetType(s, s.Next);
                        ReadState(s);
                        if (s.Type != null)
                            CicCue.Clunk(s.Home.position);
                    }

                    s.Reveal = Mathf.Min(Solid, d * Solid);
                }

                s.RevealTarget = s.Reveal;
                Apply(s);
            }

            if (_cycleT >= 1f)
                FinishCycle();
        }

        void FinishCycle()
        {
            _cycling = false;
            _gantryBeam.SetActive(false);
            var x0 = Length * 0.5f + 0.1f;
            _gantry.localPosition = new Vector3(_dir > 0f ? x0 : -x0, 0f, _gantry.localPosition.z);
            foreach (var s in _slots)
            {
                if (!s.Swapped)
                    SetType(s, s.Next);
                s.Swapped = true;
                s.Reveal = s.RevealTarget = Solid;
            }

            Refresh();
        }

        // ── Frame loop (sleeps when nothing moves) ────────────────────────────────

        void Update()
        {
            var dt = Time.deltaTime;
            var busy = _held != null;
            if (_cycling)
            {
                StepCycle(dt);
                busy = true;
            }

            foreach (var s in _slots)
            {
                if (s.Flight >= 0f)
                {
                    busy = true;
                    s.Flight += dt / FlightTime;
                    var k = MotionEase.Smooth01(s.Flight);
                    var home = s.Home.TransformPoint(Vector3.zero);
                    var homeRot = s.Home.rotation * BlockYaw;
                    // A shallow arc back onto the shelf, the robot's hand placing it.
                    var p = Vector3.Lerp(s.FlightFrom, home, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.12f);
                    s.Block.transform.SetPositionAndRotation(p, Quaternion.Slerp(s.FlightFromRot, homeRot, k));
                    if (s.Flight >= 1f)
                    {
                        s.Flight = -1f;
                        s.Block.transform.localPosition = Vector3.zero;
                        s.Block.transform.localRotation = BlockYaw;
                        CicCue.Clunk(home);
                    }
                }

                if (!_cycling && !Mathf.Approximately(s.Reveal, s.RevealTarget))
                {
                    busy = true;
                    var speed = s.RevealTarget < s.Reveal ? 3.4f : 1.9f;
                    s.Reveal = Mathf.MoveTowards(s.Reveal, s.RevealTarget, speed * dt);
                    if (s.ReturnAfterDissolve && s.Reveal <= 0f)
                    {
                        // Replicated back on its pad (the stock count follows the server's answer).
                        s.ReturnAfterDissolve = false;
                        s.Block.transform.localPosition = Vector3.zero;
                        s.Block.transform.localRotation = BlockYaw;
                        s.RevealTarget = Solid;
                        CicCue.Crystal(s.Home.position);
                    }

                    Apply(s);
                }
            }

            if (_held != null)
                Apply(_held);
            if (!busy)
                enabled = false;
        }
    }
}
