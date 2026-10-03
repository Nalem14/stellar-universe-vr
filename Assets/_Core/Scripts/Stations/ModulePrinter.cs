using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;

namespace Core.Stations
{
    /// <summary>
    /// The dry dock's module printer, aft of the module store on the starboard wall: a glazed cabinet with
    /// a lit build bed and an XY gantry. While the planet's shipyard builds a module (shipQueue) the part grows
    /// on the bed layer by layer — the shelf block of that type (<see cref="ModuleShelves.BlockMesh"/>) at
    /// print scale, solid below the print front, hologram above it (SU/ModuleBlock _Reveal / _Invert) — the
    /// nozzle rastering over the layer with sparks, its name, progress, time left and the orders behind it on
    /// the cabinet's front. Done: the part glows, dissolves and a shuttle runs it along the overhead rail to the
    /// store (the hangar). Idle: bed dimmed, head parked. Orders are given at its console, the desk beside it
    /// (<see cref="ShipyardPanel"/>: build, queue, cancel, Nova finish). Quest budget: frame merged by static batching, the part in two draws of the shared block
    /// material, one small particle pool, state read twice a second, no allocation per frame.
    /// Local frame: origin on the floor at the wall face, centre of the cabinet; the room lies toward -Z.
    /// </summary>
    public sealed class ModulePrinter : MonoBehaviour
    {
        const float Width = 1.5f;
        const float Depth = 1.0f;
        const float BedY = 0.93f;
        const float FrameTop = 2.2f;
        const float PartSize = 0.56f;
        const float ReadPeriod = 0.5f;
        const float DoneTime = 2.6f;
        const float ShuttleTime = 1.4f;
        static readonly Color Accent = new(0.4f, 0.95f, 0.55f, 1f);
        static readonly Vector3 BedCentre = new(0f, BedY, -Depth * 0.5f);

        static readonly int AccentId = Shader.PropertyToID("_Accent");
        static readonly int HoverId = Shader.PropertyToID("_Hover");
        static readonly int GhostId = Shader.PropertyToID("_Ghost");
        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int InvertId = Shader.PropertyToID("_Invert");

        CicArtKit _art;
        ShipyardPanel _yard;
        MaterialPropertyBlock _mpb;
        Transform _part;
        MeshFilter _solidFilter;
        MeshRenderer _solid;
        MeshFilter _ghostFilter;
        MeshRenderer _ghost;
        Transform _bridge;
        Transform _carriage;
        Transform _nozzle;
        Transform _rod;
        Transform _layer;
        GameObject _bedGlow;
        GameObject _bedIdle;
        Transform _shuttle;
        ParticleSystem _sparks;
        TextMeshPro _title;
        TextMeshPro _detail;
        readonly System.Text.StringBuilder _sb = new(128);

        string _type;
        long _end;
        int _queued;
        float _progress;
        float _shown;
        float _partTop;
        Color _accent = Accent;
        float _nextRead;
        float _doneT = -1f;
        float _shuttleT = -1f;
        float _nextSpark;
        float _nextText;
        Vector3 _rail0;
        Vector3 _rail1;

        public static ModulePrinter Build(Transform room, CicArtKit art, Vector3 localPos, Quaternion localRot,
            ShipyardPanel yard, Vector3 shelvesEndWorld, float ceiling)
        {
            var go = new GameObject("ModulePrinter");
            go.transform.SetParent(room, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            var p = go.AddComponent<ModulePrinter>();
            p._art = art;
            p._yard = yard;
            p._mpb = new MaterialPropertyBlock();
            p.BuildFrame(shelvesEndWorld, ceiling);
            p.BuildHead();
            p.BuildPart();
            p.BuildLabels();
            p.SetIdle();
            return p;
        }

        // ── Build ─────────────────────────────────────────────────────────────────

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

        GameObject Quad(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        void BuildFrame(Vector3 shelvesEndWorld, float ceiling)
        {
            var frame = new GameObject("Frame").transform;
            frame.SetParent(transform, false);
            var wall = _art.MetalPanel(0.5f);
            var dark = _art.DarkPanel(0.35f);
            var cyan = _art.CyanEmit(2.4f);
            var amber = _art.AmberEmit(2f);
            var green = _art.Lit(Texture2D.whiteTexture, Accent, 2.2f);
            const float hw = Width * 0.5f;
            const float front = -Depth;

            // Base cabinet: dark body, brushed service panel, a lit seam under the bed.
            Box(frame, "Base", new Vector3(0f, 0.45f, -Depth * 0.5f), new Vector3(Width, 0.9f, Depth), dark, true);
            Box(frame, "BasePanel", new Vector3(0f, 0.42f, front - 0.003f), new Vector3(Width - 0.16f, 0.62f, 0.006f), wall);
            Box(frame, "BaseVent", new Vector3(0f, 0.16f, front - 0.007f), new Vector3(Width - 0.36f, 0.05f, 0.006f), dark);
            Box(frame, "BaseSeam", new Vector3(0f, 0.885f, front - 0.004f), new Vector3(Width - 0.06f, 0.012f, 0.008f), green);
            Box(frame, "Kick", new Vector3(0f, 0.03f, front + 0.04f), new Vector3(Width - 0.04f, 0.06f, 0.04f), amber);

            // Build bed: brushed plate, recessed lit grid (one emissive plate + dark grid bars over it).
            Box(frame, "Bed", BedCentre + new Vector3(0f, -0.015f, 0f), new Vector3(Width - 0.24f, 0.03f, Depth - 0.18f), wall);
            for (var i = -3; i <= 3; i++)
            {
                Box(frame, "BedBarX" + i, BedCentre + new Vector3(i * 0.15f, 0.004f, 0f), new Vector3(0.008f, 0.006f, Depth - 0.3f), dark);
                Box(frame, "BedBarZ" + i, BedCentre + new Vector3(0f, 0.004f, i * 0.1f), new Vector3(Width - 0.36f, 0.006f, 0.008f), dark);
            }

            // Corner posts, a seam on each, and the top frame carrying the gantry rails.
            foreach (var sx in new[] { -1f, 1f })
            foreach (var z in new[] { -0.07f, front + 0.07f })
            {
                Box(frame, "Post" + sx + z, new Vector3(sx * (hw - 0.05f), (BedY + FrameTop) * 0.5f, z),
                    new Vector3(0.07f, FrameTop - BedY, 0.07f), dark);
                if (z < -0.5f)
                    Box(frame, "PostSeam" + sx, new Vector3(sx * (hw - 0.05f), (BedY + FrameTop) * 0.5f, z - 0.037f),
                        new Vector3(0.012f, (FrameTop - BedY) * 0.8f, 0.006f), cyan);
            }

            Box(frame, "BeamBack", new Vector3(0f, FrameTop, -0.07f), new Vector3(Width - 0.03f, 0.08f, 0.08f), dark);
            Box(frame, "BeamFront", new Vector3(0f, FrameTop, front + 0.07f), new Vector3(Width - 0.03f, 0.08f, 0.08f), dark);
            foreach (var sx in new[] { -1f, 1f })
            {
                Box(frame, "Rail" + sx, new Vector3(sx * (hw - 0.05f), FrameTop - 0.02f, -Depth * 0.5f),
                    new Vector3(0.06f, 0.05f, Depth - 0.1f), wall);
                Box(frame, "RailGlow" + sx, new Vector3(sx * (hw - 0.05f), FrameTop - 0.048f, -Depth * 0.5f),
                    new Vector3(0.02f, 0.006f, Depth - 0.2f), amber);
            }

            // Header above the frame (the sign) and a canopy light strip washing the bed.
            Box(frame, "Header", new Vector3(0f, FrameTop + 0.16f, front + 0.12f), new Vector3(Width + 0.04f, 0.24f, 0.2f), dark);
            Box(frame, "HeaderEdge", new Vector3(0f, FrameTop + 0.045f, front + 0.018f), new Vector3(Width, 0.012f, 0.01f), cyan);
            Box(frame, "Canopy", new Vector3(0f, FrameTop + 0.045f, -Depth * 0.5f), new Vector3(Width - 0.06f, 0.01f, Depth - 0.1f), dark);
            Box(frame, "CanopyLight", new Vector3(0f, FrameTop + 0.038f, -Depth * 0.5f), new Vector3(Width * 0.7f, 0.004f, 0.06f), cyan);

            // Overhead transfer rail from the printer to the end of the shelves: finished parts ride it home.
            var railY = FrameTop + 0.34f;
            var startL = new Vector3(-(Width * 0.5f - 0.1f), railY, -0.35f);
            var endL = transform.InverseTransformPoint(shelvesEndWorld);
            endL.y = railY;
            endL.z = startL.z;
            var mid = (startL + endL) * 0.5f;
            var len = Mathf.Abs(endL.x - startL.x);
            Box(frame, "TransferRail", mid, new Vector3(len + 0.1f, 0.05f, 0.08f), wall);
            Box(frame, "TransferGlow", mid + new Vector3(0f, -0.028f, -0.03f), new Vector3(len, 0.006f, 0.012f), green);
            Box(frame, "TransferDrop", new Vector3(startL.x, (FrameTop + 0.05f + railY) * 0.5f, startL.z),
                new Vector3(0.06f, railY - FrameTop - 0.05f, 0.06f), dark);
            Box(frame, "TransferHangerA", new Vector3(endL.x, (railY + ceiling) * 0.5f, startL.z),
                new Vector3(0.03f, ceiling - railY, 0.03f), dark);
            Box(frame, "TransferHangerB", new Vector3(mid.x, (railY + ceiling) * 0.5f, startL.z),
                new Vector3(0.03f, ceiling - railY, 0.03f), dark);
            _rail0 = startL + new Vector3(0f, -0.07f, 0f);
            _rail1 = endL + new Vector3(0f, -0.07f, 0f);

            // Glazing: faint panes on the three open sides (blended, three quads).
            var glass = _art.Holo(Texture2D.whiteTexture, new Color(0.6f, 0.9f, 1f, 0.035f));
            var gh = FrameTop - BedY - 0.06f;
            var gy = (BedY + FrameTop) * 0.5f;
            Quad(frame, "GlassFront", new Vector3(0f, gy, front + 0.07f), Quaternion.identity, new Vector2(Width - 0.14f, gh), glass);
            Quad(frame, "GlassW", new Vector3(-(hw - 0.05f), gy, -Depth * 0.5f), Quaternion.Euler(0f, 90f, 0f),
                new Vector2(Depth - 0.14f, gh), glass);
            Quad(frame, "GlassE", new Vector3(hw - 0.05f, gy, -Depth * 0.5f), Quaternion.Euler(0f, -90f, 0f),
                new Vector2(Depth - 0.14f, gh), glass);

            StaticBatchingUtility.Combine(frame.gameObject);

            // Bed glow: active (family green) / idle (dim) — two shared materials, one shown.
            _bedGlow = Box(transform, "BedGlow", BedCentre + new Vector3(0f, 0.001f, 0f),
                new Vector3(Width - 0.3f, 0.004f, Depth - 0.26f), _art.Lit(Texture2D.whiteTexture, new Color(0.22f, 0.6f, 0.36f), 1f));
            _bedIdle = Box(transform, "BedIdle", BedCentre + new Vector3(0f, 0.001f, 0f),
                new Vector3(Width - 0.3f, 0.004f, Depth - 0.26f), _art.Lit(Texture2D.whiteTexture, new Color(0.16f, 0.3f, 0.36f), 0.6f));

            _shuttle = new GameObject("Shuttle").transform;
            _shuttle.SetParent(transform, false);
            Box(_shuttle, "Pod", Vector3.zero, new Vector3(0.16f, 0.09f, 0.12f), wall);
            Box(_shuttle, "PodGlow", new Vector3(0f, -0.05f, 0f), new Vector3(0.12f, 0.012f, 0.09f), green);
            _shuttle.gameObject.SetActive(false);
        }

        void BuildHead()
        {
            var wall = _art.MetalPanel(0.5f);
            var dark = _art.DarkPanel(0.35f);
            var amber = _art.AmberEmit(3f);

            // Bridge riding the side rails (front ↔ back), carriage running along it, nozzle hanging below.
            _bridge = new GameObject("Bridge").transform;
            _bridge.SetParent(transform, false);
            _bridge.localPosition = new Vector3(0f, FrameTop - 0.07f, -Depth * 0.5f);
            Box(_bridge, "Beam", Vector3.zero, new Vector3(Width - 0.12f, 0.045f, 0.07f), wall);
            Box(_bridge, "BeamSeam", new Vector3(0f, -0.025f, -0.036f), new Vector3(Width - 0.2f, 0.006f, 0.004f), amber);

            _carriage = new GameObject("Carriage").transform;
            _carriage.SetParent(_bridge, false);
            Box(_carriage, "Body", new Vector3(0f, -0.02f, 0f), new Vector3(0.12f, 0.1f, 0.12f), dark);
            Box(_carriage, "Eye", new Vector3(0f, 0f, -0.062f), new Vector3(0.07f, 0.014f, 0.004f), amber);
            var rod = Box(_carriage, "Rod", Vector3.zero, Vector3.one, wall);
            _rod = rod.transform;
            _nozzle = new GameObject("Nozzle").transform;
            _nozzle.SetParent(_carriage, false);
            Box(_nozzle, "Cone", new Vector3(0f, 0.04f, 0f), new Vector3(0.08f, 0.07f, 0.08f), wall);
            Box(_nozzle, "Collar", new Vector3(0f, 0.012f, 0f), new Vector3(0.05f, 0.016f, 0.05f), dark);
            Box(_nozzle, "Tip", new Vector3(0f, -0.006f, 0f), new Vector3(0.03f, 0.02f, 0.03f), amber);

            // Layer scan: a sheet of light at the print front.
            var layer = Quad(transform, "LayerScan", BedCentre, Quaternion.Euler(90f, 0f, 0f), new Vector2(PartSize * 1.1f, PartSize * 1.1f),
                _art.Holo(_art.ProjectorGlow != null ? _art.ProjectorGlow : Texture2D.whiteTexture, new Color(0.4f, 1f, 0.6f, 0.13f)));
            _layer = layer.transform;

            _sparks = CombatFxKit.Burst(transform, "PrintSparks", 48, 0.4f, true, true);
        }

        void BuildPart()
        {
            _part = new GameObject("Part").transform;
            _part.SetParent(transform, false);
            _part.localPosition = BedCentre;
            _part.localRotation = Quaternion.Euler(0f, 115f, 0f);
            (_solidFilter, _solid) = PartRenderer("Printed");
            (_ghostFilter, _ghost) = PartRenderer("ToPrint");
        }

        (MeshFilter, MeshRenderer) PartRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_part, false);
            var f = go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = ModuleShelves.BlockMat();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            return (f, r);
        }

        void BuildLabels()
        {
            var title = UiKit.Label(transform, "Sign", Trans.Get("vr.dock.printer.title"),
                new Vector3(0f, FrameTop + 0.17f, -Depth + 0.018f), Width * 0.85f, 0.07f, Accent);
            title.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            _title = UiKit.Label(transform, "Job", string.Empty, new Vector3(0f, 0.66f, -Depth - 0.008f),
                Width - 0.3f, 0.05f, UiKit.TextBright);
            _title.richText = true;
            _detail = UiKit.Label(transform, "JobDetail", string.Empty, new Vector3(0f, 0.55f, -Depth - 0.008f),
                Width - 0.3f, 0.035f, UiKit.TextDim);
            _detail.richText = true;
        }

        // ── State ─────────────────────────────────────────────────────────────────

        void SetIdle()
        {
            _type = null;
            _solid.enabled = false;
            _ghost.enabled = false;
            _layer.gameObject.SetActive(false);
            _bedGlow.SetActive(false);
            _bedIdle.SetActive(true);
            _title.text = Trans.Get("vr.dock.printer.idle");
            _detail.text = _queued > 0 ? Trans.Format("vr.dock.printer.queued", _queued) : string.Empty;
        }

        void StartJob(string type)
        {
            _type = type;
            var mesh = ModuleShelves.BlockMesh(type);
            _solidFilter.sharedMesh = mesh;
            _ghostFilter.sharedMesh = mesh;
            var b = mesh.bounds;
            var s = PartSize / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            _part.localScale = Vector3.one * s;
            _partTop = b.max.y * s;
            _accent = ModuleCatalog.Accent(ModuleCatalog.Family(type));
            _shown = _progress;
            _solid.enabled = true;
            _ghost.enabled = true;
            _layer.gameObject.SetActive(true);
            _bedGlow.SetActive(true);
            _bedIdle.SetActive(false);
            _doneT = -1f;
            CicCue.Synth(transform.position + Vector3.up);
        }

        void Read()
        {
            string type = null;
            var active = _yard != null && _yard.TryActiveJob(out type, out _end, out _progress, out _queued);
            var finished = _type != null && _doneT < 0f && (!active || type != _type);
            if (finished)
            {
                // The job left the bed (built, sped up or cancelled): show it done, then ship it to the shelves.
                _doneT = 0f;
                _progress = 1f;
                CicCue.Crystal(transform.position + Vector3.up);
                return;
            }

            if (_doneT >= 0f)
                return;
            if (active && _type == null)
                StartJob(type);
            else if (!active && _type == null)
                SetIdle();
        }

        void Update()
        {
            var now = Time.unscaledTime;
            if (now >= _nextRead)
            {
                _nextRead = now + ReadPeriod;
                Read();
            }

            var dt = Time.deltaTime;
            AnimateShuttle(dt);
            if (_type == null)
            {
                ParkHead(dt);
                return;
            }

            if (_doneT >= 0f)
            {
                AnimateDone(dt);
                return;
            }

            _shown = Mathf.MoveTowards(_shown, _progress, dt * 0.25f);
            ApplyPart(_shown, 0f);
            Raster(now, dt);
            if (now >= _nextText)
            {
                _nextText = now + 1f;
                RenderText();
            }
        }

        void ApplyPart(float reveal, float hover)
        {
            _mpb.Clear();
            _mpb.SetColor(AccentId, _accent);
            _mpb.SetFloat(HoverId, hover);
            _mpb.SetFloat(GhostId, 0f);
            _mpb.SetFloat(RevealId, reveal);
            _mpb.SetFloat(InvertId, 0f);
            _solid.SetPropertyBlock(_mpb);
            _mpb.SetFloat(GhostId, 1f);
            _mpb.SetFloat(InvertId, 1f);
            _mpb.SetFloat(HoverId, 0.2f);
            _ghost.SetPropertyBlock(_mpb);
        }

        /// <summary>Nozzle tracing the current layer (Lissajous over the part's footprint), sparks at the tip.</summary>
        void Raster(float now, float dt)
        {
            var y = BedY + Mathf.Clamp01(_shown) * _partTop;
            var r = PartSize * 0.42f;
            var x = Mathf.Sin(now * 3.1f) * r;
            var z = -Depth * 0.5f + Mathf.Sin(now * 2.3f + 1.1f) * r;
            MoveHead(new Vector3(x, y + 0.02f, z), dt * 12f);
            _layer.localPosition = new Vector3(0f, y + 0.003f, -Depth * 0.5f);

            if (now >= _nextSpark)
            {
                _nextSpark = now + 0.07f;
                var tip = _nozzle.position;
                var spray = new Vector3(Random.Range(-0.4f, 0.4f), Random.Range(0.1f, 0.6f), Random.Range(-0.4f, 0.4f));
                CombatFxKit.Emit(_sparks, tip, Color.Lerp(new Color(1f, 0.8f, 0.4f), _accent, Random.value * 0.5f),
                    Random.Range(0.008f, 0.018f), Random.Range(0.15f, 0.4f), spray);
            }
        }

        void ParkHead(float dt) => MoveHead(new Vector3(0f, FrameTop - 0.32f, -0.2f), dt * 3f);

        /// <summary>Bridge along local Z, carriage along X, nozzle down to <paramref name="target"/>.y; rod spans the drop.</summary>
        void MoveHead(Vector3 target, float k)
        {
            var t = Mathf.Clamp01(k);
            var bp = _bridge.localPosition;
            bp.z = Mathf.Lerp(bp.z, target.z, t);
            _bridge.localPosition = bp;
            var cp = _carriage.localPosition;
            cp.x = Mathf.Lerp(cp.x, target.x, t);
            _carriage.localPosition = cp;
            var drop = Mathf.Max(0.08f, bp.y - target.y);
            var np = _nozzle.localPosition;
            np.y = Mathf.Lerp(np.y, -drop, t);
            _nozzle.localPosition = np;
            _rod.localPosition = new Vector3(0f, np.y * 0.5f + 0.03f, 0f);
            _rod.localScale = new Vector3(0.03f, Mathf.Max(0.01f, -np.y - 0.03f), 0.022f);
        }

        /// <summary>Done: a glow pulse on the whole part, then a downward dissolve and the shuttle leaves.</summary>
        void AnimateDone(float dt)
        {
            if (_doneT == 0f)
            {
                _title.text = "<b>" + Trans.Get(_type) + "</b>   <color=#7fffa0>" + Trans.Get("vr.dock.printer.done") + "</color>";
                _detail.text = _queued > 0 ? Trans.Format("vr.dock.printer.queued", _queued) : string.Empty;
                _layer.gameObject.SetActive(false);
                _ghost.enabled = false;
            }

            _doneT += dt;
            ParkHead(dt);
            var pulse = Mathf.Clamp01(1f - _doneT / 1.2f);
            var dissolve = Mathf.Clamp01((_doneT - 1.4f) / (DoneTime - 1.4f));
            ApplyPart(Mathf.Lerp(1.1f, -0.05f, dissolve), pulse * 1.6f);
            if (dissolve > 0f && _shuttleT < 0f)
            {
                _shuttleT = 0f;
                _shuttle.gameObject.SetActive(true);
                CicCue.Clunk(transform.TransformPoint(_rail0));
            }

            if (_doneT < DoneTime)
                return;
            _doneT = -1f;
            _type = null;
            _nextRead = 0f;
            SetIdle();
        }

        void AnimateShuttle(float dt)
        {
            if (_shuttleT < 0f)
                return;
            _shuttleT += dt;
            var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_shuttleT / ShuttleTime));
            _shuttle.localPosition = Vector3.Lerp(_rail0, _rail1, t);
            if (_shuttleT < ShuttleTime + 0.3f)
                return;
            _shuttleT = -1f;
            _shuttle.gameObject.SetActive(false);
        }

        void RenderText()
        {
            _sb.Clear();
            _sb.Append("<b>").Append(Trans.Get(_type)).Append("</b>   <color=#7fffa0>")
                .Append(Mathf.FloorToInt(Mathf.Clamp01(_progress) * 100f)).Append(" %</color>");
            _title.text = _sb.ToString();
            var left = Core.Holo.TravelPlanner.TimeText(Mathf.Max(0f, _end - FleetOrderGate.UnixNow()));
            _detail.text = _queued > 0
                ? Trans.Format("vr.dock.printer.left", left) + "   ·   " + Trans.Format("vr.dock.printer.queued", _queued)
                : Trans.Format("vr.dock.printer.left", left);
        }

        void OnDisable()
        {
            _shuttleT = -1f;
            if (_shuttle != null)
                _shuttle.gameObject.SetActive(false);
            if (_doneT >= 0f)
            {
                _doneT = -1f;
                _type = null;
                SetIdle();
            }
        }
    }
}
