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
    /// The dry dock's module store: the planet's finished hangar, as a lit wall of shelves holding one block per
    /// module type in stock, each a miniature of the module's own deck silhouette
    /// (<see cref="ShipHullBuilder.ModuleMesh"/>) on a cartridge plinth banded in its family colour.
    /// It is the one place modules are taken from: grab a block (grip, or ray + trigger) and set it on a grid
    /// cell of the assembly table = PlaceShipModule, or drop it in the recycler = DelShip. It stores, it does
    /// not order: new modules come from the printer (<see cref="ModulePrinter"/>), whose shuttle delivers here.
    /// More types than the wall holds: a robot gantry runs along the shelves and swaps the page, block by block,
    /// with a replicator sweep (SU/ModuleBlock). Hovering a block shows its name, stock, stats and description on
    /// one shared card. Quest budget: one shared material and one draw per block, the frame merged by static
    /// batching, four text rows for the shelf lips, one card.
    /// Local frame: origin on the floor at the wall face, centre of the rack; the room lies toward -Z.
    /// </summary>
    public sealed class ModuleShelves : MonoBehaviour
    {
        const int Rows = 4;
        const int Cols = 5;
        const int PerPage = Rows * Cols;
        const float Length = 2.7f;
        const float Depth = 0.55f;
        const float Pitch = 0.5f;
        const float RowBase = 0.46f;
        const float RowPitch = 0.42f;
        const float Top = 2.36f;
        const float BlockSize = 0.2f;
        const float PlinthH = 0.035f;
        const float CycleTime = 1.9f;
        const float SweepWindow = 0.45f;
        const float FlightTime = 0.4f;
        const float Solid = 1.1f;
        static readonly Color Accent = new(0.4f, 0.95f, 0.55f, 1f);
        static readonly Quaternion BlockYaw = Quaternion.Euler(0f, 115f, 0f);

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

        readonly Slot[] _slots = new Slot[PerPage];
        readonly TextMeshPro[] _lips = new TextMeshPro[Rows];
        readonly List<string> _types = new();
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
            Action<string> onPick, Func<string, Vector3, bool> onDrop)
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
                Box(frame, "Lip" + row, new Vector3(0f, y - 0.03f, -Depth + 0.01f), new Vector3(Length, 0.06f, 0.02f), dark);
                Box(frame, "UnderLight" + row, new Vector3(0f, y - 0.064f, -Depth * 0.55f), new Vector3(Length * 0.95f, 0.006f, 0.05f), cyan);
                for (var col = 0; col < Cols; col++)
                {
                    Box(frame, "Pad" + row + col, new Vector3(SlotX(col), y + 0.002f, -Depth * 0.5f),
                        new Vector3(0.26f, 0.004f, 0.26f), dark);
                    Box(frame, "PadRing" + row + col, new Vector3(SlotX(col), y + 0.0025f, -Depth * 0.5f + 0.132f),
                        new Vector3(0.26f, 0.004f, 0.006f), green);
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
                new Vector3(0f, Top + 0.15f, -Depth + 0.035f), Length * 0.8f, 0.07f, Accent);
            sign.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            _familyLabel = UiKit.Label(transform, "Families", string.Empty,
                new Vector3(0f, Top + 0.055f, -Depth + 0.035f), Length * 0.9f, 0.03f, UiKit.TextDim);
            _familyLabel.richText = true;

            // Stock row on each shelf lip: one text per shelf, each slot a tab stop (<pos>).
            for (var row = 0; row < Rows; row++)
            {
                var lip = UiKit.Label(transform, "Lip" + row, string.Empty,
                    new Vector3(0f, ShelfY(row) - 0.03f, -Depth - 0.002f), Length - 0.1f, 0.022f, UiKit.TextBright,
                    TextAlignmentOptions.MidlineLeft);
                lip.enableAutoSizing = false;
                lip.fontSize = 0.022f * 100f * 14f * 0.8f;
                lip.richText = true;
                _lips[row] = lip;
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
                slot.PadCol.center = new Vector3(0f, 0.12f, 0f);
                slot.PadCol.size = new Vector3(0.3f, 0.26f, 0.3f);
                var pi = pad.AddComponent<XRSimpleInteractable>();
                var s = slot;
                pi.selectEntered.AddListener(_ => OnPad(s));
                pi.hoverEntered.AddListener(_ => ShowCard(s));
                pi.hoverExited.AddListener(_ => HideCard(s));

                var block = new GameObject("Block");
                block.transform.SetParent(slot.Home, false);
                block.transform.localRotation = BlockYaw;
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
            // Control column at the rack's north end (nearest the assembly table), hand height.
            var x = -(Length * 0.5f + 0.27f);
            var column = new GameObject("Controls").transform;
            column.SetParent(transform, false);
            column.localPosition = new Vector3(x, 0f, -0.2f);
            var dark = _art.DarkPanel(0.35f);
            var cyan = _art.CyanEmit(2.4f);
            Box(column, "Column", new Vector3(0f, 0.7f, 0f), new Vector3(0.34f, 1.4f, 0.3f), dark, true);
            Box(column, "Face", new Vector3(0f, 1.2f, -0.152f), new Vector3(0.3f, 0.36f, 0.004f), _art.MetalPanel(0.45f));
            Box(column, "FaceGlow", new Vector3(0f, 1.395f, -0.152f), new Vector3(0.3f, 0.01f, 0.006f), cyan);
            StaticBatchingUtility.Combine(column.gameObject);

            var z = -0.16f;
            PokeButton.Create(column, "PagePrev", "‹", new Vector3(-0.085f, 1.29f, z), Quaternion.identity,
                new Vector2(0.1f, 0.07f), Accent, () => Turn(-1));
            PokeButton.Create(column, "PageNext", "›", new Vector3(0.085f, 1.29f, z), Quaternion.identity,
                new Vector2(0.1f, 0.07f), Accent, () => Turn(1));
            _pageLabel = UiKit.Label(column, "Page", "1 / 1", new Vector3(0f, 1.225f, z - 0.002f), 0.26f, 0.025f,
                UiKit.TextBright);
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
            var back = UiKit.MeshPiece(_card, "Back", UiMeshes.RoundedBox(new Vector3(0.52f, 0.25f, 0.012f), 0.012f),
                UiKit.Chassis, new Vector3(0f, 0f, 0.008f));
            _cardBack = back.GetComponent<MeshRenderer>();
            _cardText = UiKit.Label(_card, "Text", string.Empty, new Vector3(0f, 0f, -0.001f), 0.49f, 0.018f,
                UiKit.TextBright, TextAlignmentOptions.MidlineLeft, wrap: true);
            _cardText.richText = true;
            _cardText.enableAutoSizing = true;
            _cardText.fontSizeMin = 0.011f * 1400f;
            _cardText.fontSizeMax = 0.018f * 1400f;
            _cardText.rectTransform.sizeDelta = new Vector2(49f, 23f);
            _card.gameObject.SetActive(false);
        }

        void ShowCard(Slot s)
        {
            if (s.Type == null || _cycling || _held != null)
                return;
            _cardSlot = s;
            CicCue.Hover(s.Home.position);
            // In front of the block, above it, clear of the shelf lip.
            _card.localPosition = s.Home.localPosition + new Vector3(0f, 0.36f, -Depth * 0.5f - 0.06f);
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
            _sb.Append("<color=#7dffa0>").Append(Trans.Format("vr.dock.shelf.inStock", s.Count)).Append("</color> — ");
            _sb.Append(_canFit() ? Trans.Get("vr.dock.shelf.grab") : "<color=#ffb04a>" + Trans.Get("vr.dock.pickShipFirst") + "</color>");
            _sb.Append("</size>");
            var stats = ModuleCatalog.StatsLine(s.Type);
            if (stats.Length > 0)
                _sb.Append("\n<size=80%>").Append(stats).Append("</size>");
            _sb.Append("\n<size=70%><color=#9fc4d6>").Append(ModuleCatalog.Description(s.Type)).Append("</color></size>");
            _cardText.text = _sb.ToString();
            _cardBack.GetPropertyBlock(_mpb);
            _mpb.SetColor(UiKit.AccentId, ModuleCatalog.Accent(fam));
            _mpb.SetFloat(UiKit.AccentMulId, 1.1f);
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
                if (kv.Value > 0)
                    _types.Add(kv.Key);
            _types.Sort((a, b) =>
            {
                var f = ModuleCatalog.Family(a).CompareTo(ModuleCatalog.Family(b));
                return f != 0 ? f : string.CompareOrdinal(a, b);
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
            var pickable = s.Type != null && s.Count > 0 && !_cycling;
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
            // Families on this page, in their colours.
            _sb.Clear();
            var last = (ModuleFamily)(-1);
            for (var i = 0; i < PerPage; i++)
            {
                var t = _slots[i].Next ?? _slots[i].Type;
                if (t == null)
                    continue;
                var f = ModuleCatalog.Family(t);
                if (f == last)
                    continue;
                if (_sb.Length > 0)
                    _sb.Append("   ");
                _sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ModuleCatalog.Accent(f))).Append('>')
                    .Append(Trans.Get(ModuleCatalog.FamilyKey(f))).Append("</color>");
                last = f;
            }

            _familyLabel.text = _sb.Length > 0 ? _sb.ToString() : Trans.Get("vr.dock.store.empty");

            for (var row = 0; row < Rows; row++)
            {
                _sb.Clear();
                for (var col = 0; col < Cols; col++)
                {
                    var s = _slots[row * Cols + col];
                    if (s.Type == null)
                        continue;
                    // Slot left edge as a share of the lip width (text rect = Length - 0.1 m).
                    var pct = (SlotX(col) - 0.2f + (Length - 0.1f) * 0.5f) / (Length - 0.1f) * 100f;
                    _sb.Append("<pos=").Append(pct.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                        .Append("%>");
                    _sb.Append("<color=#7dffa0><b>×").Append(s.Count).Append("</b></color> ");
                    var name = Trans.Get(ModuleCatalog.NameKey(s.Type));
                    _sb.Append(name.Length <= 14 ? name : name.Substring(0, 13) + "…");
                }

                // Lip rows are indexed bottom-up; slots top-down.
                _lips[Rows - 1 - row].text = _sb.ToString();
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
            var hover = s == _cardSlot ? 1f : s == _held ? 0.8f : s.Type == _selected() ? 0.55f : 0f;
            _mpb.SetColor(AccentId, accent);
            _mpb.SetFloat(HoverId, hover);
            _mpb.SetFloat(GhostId, 0f);
            _mpb.SetFloat(RevealId, s.Reveal);
            s.Renderer.SetPropertyBlock(_mpb);
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

        void OnPad(Slot s)
        {
            if (s.Type == null || _cycling)
                return;
            CicCue.Pip(s.Home.position);
        }

        /// <summary>Leaving the dock with a block in hand: the hand lets go (it never follows onto the bridge).</summary>
        public void DropHeld()
        {
            if (_held != null && _held.Block.TryGetComponent<XRGrabInteractable>(out var grab) && grab.isSelected)
                grab.interactionManager?.CancelInteractableSelection((UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grab);
            ResetAll();
        }

        /// <summary>Entering / leaving the dock: every block back on its shelf, the page settled, no card.</summary>
        public void ResetAll()
        {
            _held = null;
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
