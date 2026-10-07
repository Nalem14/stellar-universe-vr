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
    /// Science research lab: a round room you walk into through a door of the bridge (web ResearchScene +
    /// research window, rebuilt as a place). The tech tree stands around you as a constellation of crystals
    /// on an arc, roots at the floor and the deepest techs overhead; filaments light up as prerequisites are
    /// met. Point at a crystal to study it on the analysis screen; its sample drops onto the cradle beside
    /// you — grab it and set it into the synthesizer (or press Launch) → ImproveResearch. The running
    /// research spins in the synthesizer's beam, queued ones wait on its pads (pull one off to cancel it:
    /// CancelQueuedResearch); the Nova finish is on the synthesizer screen (SpeedupResearch).
    /// Research is empire-wide, so the lab is open from any view — the order goes to our best lab planet.
    /// </summary>
    public sealed class ResearchLab : MonoBehaviour
    {
        static readonly Vector3 WorldOrigin = new(160f, -3000f, 0f);
        /// <summary>The player stands at the room centre, facing the constellation (+z).</summary>
        static readonly Vector3 Stand = Vector3.zero;
        const float ArcRadius = 3.2f;
        const float ArcHalfDeg = 62f;
        const float TreeBase = 0.9f;
        const float TreeHeight = 2.2f;
        const float RoomRadius = 6.0f;
        const float RoomHeight = 4.4f;
        static readonly Vector3 SynthPos = new(1.4f, 0f, 0.1f);
        const float BeamLow = 0.92f;
        const float BeamHigh = 1.85f;
        static readonly Vector3 Cradle = new(0.55f, 0f, 0.5f);
        /// <summary>Operator desks the two working screens stand on (left: analysis; right, behind the synthesizer: queue).</summary>
        static readonly Vector3 AnalysisDesk = new(-1.5f, 0f, 0.1f);
        static readonly Vector3 SynthDesk = new(2.45f, 0f, -1.15f);
        const float CradleTop = 1.02f;
        const float IntakeRadius = 0.32f;
        const int RingSegments = 32;
        public static readonly Color Accent = new(0.7f, 0.5f, 1f, 1f);
        static readonly Color Locked = new(0.2f, 0.25f, 0.33f, 1f);

        public static ResearchLab Instance { get; private set; }
        public static bool Inside { get; private set; }

        sealed class NodeView
        {
            public TechNode Node;
            public Transform Root;
            public Transform Crystal;
            public Renderer CrystalRenderer;
            public Renderer Halo;
            public TextMeshPro Name;
            public TextMeshPro Level;
            public float Spin;
        }

        sealed class QueueSlot
        {
            public Transform Pad;
            public GameObject Crystal;
            public int QueueId;
            public string Tech;
            public bool Held;
        }

        CicArtKit _art;
        EconomyService _eco;
        FocusContext _focus;
        LabDecor.Refs _decor;
        RectTransform _bonusBody;
        string _bonusSig;
        Material _crystalMat;
        Material _glowMat;
        Mesh _crystalMesh;
        MaterialPropertyBlock _mpb;
        readonly Dictionary<string, NodeView> _nodes = new();
        MeshFilter _links;
        Mesh _linkMesh;

        Transform _coreRoot;
        Transform _activeCrystal;
        Renderer _activeRenderer;
        readonly Renderer[] _ring = new Renderer[RingSegments];
        Material _ringOn;
        Material _ringOff;
        int _ringLit = -1;
        readonly List<QueueSlot> _slots = new();
        TextMeshPro _points;
        ParticleSystem _burst;

        GameObject _sample;
        XRGrabInteractable _sampleGrab;
        bool _sampleHeld;
        float _sampleFlight = 1f;
        Vector3 _sampleFrom;

        HoloScreen _detail;
        RectTransform _detailBody;
        HoloScreen _coreScreen;
        RectTransform _coreBody;
        TMP_Text _status;
        readonly List<(TMP_Text, System.Func<string>)> _live = new();
        readonly List<(Image, System.Func<float>)> _bars = new();

        JObject _empire;
        string _selected;
        int _detailPage = 1;
        string _hover;
        bool _busy;
        float _nextTick;
        float _nextPoll;
        long _refreshAt;

        public static ResearchLab Build(CicArtKit art, EconomyService eco, FocusContext focus)
        {
            var go = new GameObject("ScienceResearchLab");
            go.transform.position = WorldOrigin;
            var lab = go.AddComponent<ResearchLab>();
            lab._art = art;
            lab._eco = eco;
            lab._focus = focus;
            lab._mpb = new MaterialPropertyBlock();
            lab.MakeMaterials();
            lab.BuildRoom();
            lab.BuildCore();
            lab.BuildCradle();
            lab.BuildScreens();
            go.SetActive(false);
            return lab;
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

        // ── Materials / meshes ────────────────────────────────────────────────────

        void MakeMaterials()
        {
            var crystal = Shader.Find("SU/HoloCrystal");
            _crystalMat = crystal != null ? new Material(crystal) : _art.Lit(Texture2D.whiteTexture, Accent, 1.2f);
            _crystalMat.enableInstancing = true;
            var glow = Shader.Find("SU/ParticleGlow");
            _glowMat = glow != null ? new Material(glow) : _art.Holo(Texture2D.whiteTexture, Accent);
            if (_glowMat.HasProperty("_MainTex") && _art.ProjectorGlow != null)
                _glowMat.mainTexture = _art.ProjectorGlow;
            if (_glowMat.HasProperty("_Color"))
                _glowMat.SetColor("_Color", Color.white);
            if (_glowMat.HasProperty("_EmissionMul"))
                _glowMat.SetFloat("_EmissionMul", 1.6f);
            _crystalMesh = CrystalMesh();
            _ringOn = _art.Lit(Texture2D.whiteTexture, Accent, 3f);
            _ringOff = _art.Lit(Texture2D.whiteTexture, new Color(0.14f, 0.12f, 0.22f, 1f), 0.4f);
        }

        /// <summary>Elongated octahedron, flat-shaded (each facet its own normal), unit height.</summary>
        static Mesh CrystalMesh()
        {
            var top = new Vector3(0f, 0.5f, 0f);
            var bottom = new Vector3(0f, -0.5f, 0f);
            var ring = new Vector3[6];
            for (var i = 0; i < 6; i++)
            {
                var a = i * Mathf.PI / 3f;
                ring[i] = new Vector3(Mathf.Cos(a) * 0.3f, (i % 2 == 0 ? 0.05f : -0.05f), Mathf.Sin(a) * 0.3f);
            }

            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c)
            {
                var i = verts.Count;
                verts.Add(a);
                verts.Add(b);
                verts.Add(c);
                tris.Add(i);
                tris.Add(i + 1);
                tris.Add(i + 2);
            }

            for (var i = 0; i < 6; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % 6];
                Face(top, b, a);
                Face(bottom, a, b);
            }

            var mesh = new Mesh { name = "TechCrystal" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        GameObject CrystalObject(Transform parent, string name, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size, size * 1.5f, size);
            go.AddComponent<MeshFilter>().sharedMesh = _crystalMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _crystalMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void Tint(Renderer r, Color color, float emission, float pulse, float rim = 1.6f)
        {
            _mpb.Clear();
            _mpb.SetColor("_Color", color);
            _mpb.SetColor("_Emission", color);
            _mpb.SetFloat("_EmissionMul", emission);
            _mpb.SetFloat("_Pulse", pulse);
            _mpb.SetFloat("_Rim", rim);
            r.SetPropertyBlock(_mpb);
        }

        void Glow(Renderer r, Color color)
        {
            _mpb.Clear();
            _mpb.SetColor("_Color", color);
            r.SetPropertyBlock(_mpb);
        }

        Renderer GlowQuad(Transform parent, string name, Vector3 pos, float size, Quaternion rot)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = rot;
            q.transform.localScale = Vector3.one * size;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = _glowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        // ── Room ──────────────────────────────────────────────────────────────────

        GameObject Box(string name, Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null, bool solid = false,
            Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!solid)
                Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        GameObject Cyl(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        void Decal(string name, Vector3 pos, float size, Material mat, bool down = false)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.localPosition = pos;
            q.transform.localRotation = Quaternion.Euler(down ? -90f : 90f, 0f, 0f);
            q.transform.localScale = Vector3.one * size;
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void BuildRoom()
        {
            // A clean room, each part its own material (WorkshopSurfaces): large resin tiles underfoot, white wall
            // panels with shadow gaps over a lavender dado, a perforated acoustic ceiling, lilac-grey pilasters.
            const float span = RoomRadius * 2.1f;
            var deck = WorkshopSurfaces.Tiled(_art, WorkshopSurfaces.LabTile(), new Color(0.62f, 0.64f, 0.69f), 0.58f,
                new Vector2(span / 2.4f, span / 2.4f));
            var ceiling = WorkshopSurfaces.Tiled(_art, WorkshopSurfaces.AcousticCeiling(), new Color(0.64f, 0.66f, 0.72f), 0.5f,
                new Vector2(span / 1.2f, span / 1.2f));
            var wall = WorkshopSurfaces.Tiled(_art, WorkshopSurfaces.LabPanel(), new Color(0.56f, 0.6f, 0.68f), 0.55f, Vector2.one);
            var dado = _art.Lit(StationSurfaces.Panel(), new Color(0.44f, 0.4f, 0.6f), 0.55f, 1.5f);
            var rib = _art.Lit(StationSurfaces.Panel(), new Color(0.42f, 0.42f, 0.52f), 0.45f, 1.2f);
            var violet = _art.Lit(Texture2D.whiteTexture, Accent, 2.6f);
            var cyan = _art.CyanEmit(2.2f);
            var ring = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;

            Box("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(RoomRadius * 2.1f, 0.1f, RoomRadius * 2.1f), deck,
                solid: true);
            Box("Ceiling", new Vector3(0f, RoomHeight, 0f), new Vector3(RoomRadius * 2.1f, 0.1f, RoomRadius * 2.1f), ceiling);

            // Round hall: 16 wall panels, each with a ribbed pilaster and a violet base / crown line.
            const int panels = 16;
            var width = 2f * RoomRadius * Mathf.Tan(Mathf.PI / panels) + 0.04f;
            for (var i = 0; i < panels; i++)
            {
                var a = i * 360f / panels;
                var rot = Quaternion.Euler(0f, a, 0f);
                var n = rot * Vector3.forward;
                Box("Wall" + i, n * RoomRadius + Vector3.up * RoomHeight * 0.5f, new Vector3(width, RoomHeight, 0.14f), wall,
                    rot, solid: true);
                var edge = Quaternion.Euler(0f, a + 180f / panels, 0f) * Vector3.forward * (RoomRadius - 0.1f);
                Box("Pilaster" + i, edge + Vector3.up * RoomHeight * 0.5f, new Vector3(0.22f, RoomHeight, 0.18f), rib,
                    Quaternion.Euler(0f, a + 180f / panels, 0f));
                Box("Dado" + i, n * (RoomRadius - 0.075f) + Vector3.up * 0.45f, new Vector3(width, 0.9f, 0.02f), dado, rot);
                Box("BaseLine" + i, n * (RoomRadius - 0.09f) + Vector3.up * 0.905f, new Vector3(width * 0.9f, 0.025f, 0.02f),
                    violet, rot);
                Box("CrownLine" + i, n * (RoomRadius - 0.09f) + Vector3.up * (RoomHeight - 0.35f),
                    new Vector3(width * 0.9f, 0.02f, 0.02f), cyan, rot);
            }

            // Floor: a ring around the stand, and the emitter arc the constellation rises from.
            Decal("StandRing", new Vector3(0f, 0.006f, 0f), 2.2f, _art.RadarIcon(ring, new Color(Accent.r, Accent.g, Accent.b, 0.5f)));
            for (var i = 0; i <= 14; i++)
            {
                var ang = Mathf.Lerp(-ArcHalfDeg - 4f, ArcHalfDeg + 4f, i / 14f) * Mathf.Deg2Rad;
                var p = new Vector3(Mathf.Sin(ang) * ArcRadius, 0.03f, Mathf.Cos(ang) * ArcRadius);
                Box("Emitter" + i, p, new Vector3(0.62f, 0.06f, 0.22f), rib, Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f));
                Box("EmitterGlow" + i, p + Vector3.up * 0.032f, new Vector3(0.44f, 0.006f, 0.035f), violet,
                    Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f));
            }

            // Ceiling oculus over the tree, and a wide projector wash on the floor under it.
            Decal("Oculus", new Vector3(0f, RoomHeight - 0.06f, 1.4f), 2.4f,
                _art.RadarIcon(ring, new Color(Accent.r, Accent.g, Accent.b, 0.14f)), down: true);
            var wash = GlowQuad(transform, "TreeWash", new Vector3(0f, 0.012f, ArcRadius * 0.7f), 5.5f,
                Quaternion.Euler(90f, 0f, 0f));
            Glow(wash, new Color(0.35f, 0.22f, 0.6f, 0.35f));

            Light("TreeLight", new Vector3(0f, 2.4f, 1.6f), new Color(0.72f, 0.58f, 1f), 1.6f, 7f);
            Light("KeyOverhead", new Vector3(0f, RoomHeight - 0.5f, -0.8f), new Color(0.75f, 0.92f, 1f), 1.3f, 8.5f);
            // The benches along the back walls: a cool lab light over them.
            Light("BenchFill", new Vector3(0f, RoomHeight - 0.8f, -3.6f), new Color(0.82f, 0.95f, 1f), 1.1f, 6.5f);

            _decor = LabDecor.Build(transform, _art, Accent, RoomRadius, RoomHeight, AnalysisDesk, SynthDesk);
            BuildMotes();
            BuildTree();

            // Way back to the bridge: behind the stand.
            RoomDoor.Build(transform, "DoorToBridge", new Vector3(0f, 0f, -RoomRadius + 0.2f), 0f,
                Trans.Get("vr.lab.leave"), UiKit.Amber, _art, () => Inside, () => AsyncTap.Run(Leave()));
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

        /// <summary>Data motes drifting up through the constellation (one small emitter).</summary>
        void BuildMotes()
        {
            var go = new GameObject("DataMotes");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.55f, 1f, 0.8f), new Color(0.4f, 0.9f, 1f, 0.7f));
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = ps.emission;
            emission.rateOverTime = 11f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Donut;
            shape.radius = ArcRadius;
            shape.donutRadius = 0.5f;
            shape.arc = ArcHalfDeg * 2f;
            shape.rotation = new Vector3(90f, 0f, 90f - ArcHalfDeg);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            vel.y = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = _glowMat;
            ps.Play();

            var burstGo = new GameObject("SynthBurst");
            burstGo.transform.SetParent(transform, false);
            _burst = burstGo.AddComponent<ParticleSystem>();
            _burst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var bm = _burst.main;
            bm.playOnAwake = false;
            bm.loop = false;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
            bm.startColor = new ParticleSystem.MinMaxGradient(Accent, new Color(0.5f, 0.95f, 1f, 1f));
            bm.maxParticles = 120;
            bm.simulationSpace = ParticleSystemSimulationSpace.World;
            var be = _burst.emission;
            be.rateOverTime = 0f;
            var bs = _burst.shape;
            bs.shapeType = ParticleSystemShapeType.Sphere;
            bs.radius = 0.08f;
            burstGo.GetComponent<ParticleSystemRenderer>().sharedMaterial = _glowMat;
        }

        // ── Constellation ─────────────────────────────────────────────────────────

        static float MaxLayoutX()
        {
            var max = ResearchCatalog.LayoutWidth;
            foreach (var n in ResearchCatalog.All())
                max = Mathf.Max(max, n.X);
            return max;
        }

        static float Angle(float x, float maxX) =>
            Mathf.Lerp(-ArcHalfDeg, ArcHalfDeg, Mathf.InverseLerp(80f, maxX, x)) * Mathf.Deg2Rad;

        static float Height(float y) => TreeBase + Mathf.InverseLerp(30f, ResearchCatalog.LayoutHeight, y) * TreeHeight;

        static Vector3 OnArc(float angle, float height) =>
            new(Mathf.Sin(angle) * ArcRadius, height, Mathf.Cos(angle) * ArcRadius);

        void BuildTree()
        {
            var tree = new GameObject("Constellation").transform;
            tree.SetParent(transform, false);
            var maxX = MaxLayoutX();
            foreach (var node in ResearchCatalog.All())
            {
                var ang = Angle(node.X, maxX);
                var root = new GameObject("Tech_" + node.Id).transform;
                root.SetParent(tree, false);
                root.localPosition = OnArc(ang, Height(node.Y));
                // Face the stand: labels and halo read from the centre of the room.
                root.localRotation = Quaternion.Euler(0f, ang * Mathf.Rad2Deg + 180f, 0f);

                var view = new NodeView { Node = node, Root = root, Spin = Random.Range(0f, 360f) };
                var halo = GlowQuad(root, "Halo", Vector3.zero, 0.42f, Quaternion.Euler(0f, 180f, 0f));
                view.Halo = halo;
                var crystal = CrystalObject(root, "Crystal", 0.13f);
                view.Crystal = crystal.transform;
                view.CrystalRenderer = crystal.GetComponent<MeshRenderer>();
                view.Name = UiKit.Label(root, "Name", Trans.Get(node.Id), new Vector3(0f, -0.15f, 0f), 0.9f, 0.05f,
                    UiKit.TextBright);
                view.Level = UiKit.Label(root, "Level", string.Empty, new Vector3(0f, -0.215f, 0f), 0.6f, 0.032f,
                    UiKit.TextDim);
                // Labels face local -Z; the root already faces the stand, so turn them back toward it.
                view.Name.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                view.Level.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                var col = root.gameObject.AddComponent<SphereCollider>();
                col.radius = 0.17f;
                var xi = root.gameObject.AddComponent<XRSimpleInteractable>();
                var id = node.Id;
                xi.selectEntered.AddListener(_ => Select(id));
                xi.hoverEntered.AddListener(_ => SetHover(id));
                xi.hoverExited.AddListener(_ => SetHover(null));
                _nodes[node.Id] = view;
            }

            var links = new GameObject("Filaments");
            links.transform.SetParent(tree, false);
            _links = links.AddComponent<MeshFilter>();
            _linkMesh = new Mesh { name = "TechFilaments" };
            _linkMesh.MarkDynamic();
            _links.sharedMesh = _linkMesh;
            var lr = links.AddComponent<MeshRenderer>();
            lr.sharedMaterial = _glowMat;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Filaments, one mesh for the whole tree: from the parent up to half-way, along the arc, up to the
        /// child (the web's orthogonal routing, wrapped on the arc). Bright in the parent's colour once the
        /// child's prerequisites are met.
        /// </summary>
        void RebuildLinks()
        {
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var maxX = MaxLayoutX();
            foreach (var view in _nodes.Values)
            {
                var child = view.Node;
                var met = Met(child.Id);
                foreach (var (tech, _) in ResearchCatalog.Parents(child.Id))
                {
                    if (!_nodes.TryGetValue(tech, out var parentView))
                        continue;
                    var parent = parentView.Node;
                    Color c;
                    if (met)
                        c = new Color(parent.Color.r, parent.Color.g, parent.Color.b, 0.75f);
                    else if (Level(parent.Id) > 0)
                        c = new Color(parent.Color.r, parent.Color.g, parent.Color.b, 0.22f);
                    else
                        c = new Color(0.35f, 0.4f, 0.55f, 0.1f);
                    var width = met ? 0.018f : 0.012f;

                    var a0 = Angle(parent.X, maxX);
                    var a1 = Angle(child.X, maxX);
                    var h0 = Height(parent.Y) + 0.12f;
                    var h1 = Height(child.Y) - 0.26f;
                    // A smooth S on the arc: leaves the parent upward, swings across, rises into the child.
                    var steps = Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(a1 - a0) * Mathf.Rad2Deg / 3f) + 6, 8, 40);
                    var path = new List<Vector3>(steps + 1);
                    for (var s = 0; s <= steps; s++)
                    {
                        var t = s / (float)steps;
                        var across = t * t * (3f - 2f * t);
                        path.Add(OnArc(Mathf.Lerp(a0, a1, across), Mathf.Lerp(h0, h1, t)));
                    }

                    Ribbon(path, width, c, verts, cols, uvs, tris);
                }
            }

            _linkMesh.Clear();
            _linkMesh.SetVertices(verts);
            _linkMesh.SetColors(cols);
            _linkMesh.SetUVs(0, uvs);
            _linkMesh.SetTriangles(tris, 0);
            _linkMesh.RecalculateBounds();
        }

        static void Ribbon(List<Vector3> path, float width, Color c, List<Vector3> verts, List<Color> cols,
            List<Vector2> uvs, List<int> tris)
        {
            for (var i = 0; i < path.Count - 1; i++)
            {
                var a = path[i];
                var b = path[i + 1];
                var dir = b - a;
                if (dir.sqrMagnitude < 1e-6f)
                    continue;
                // Flat toward the stand (the arc's centre), widened across the run.
                var toCentre = new Vector3(-a.x, 0f, -a.z).normalized;
                var side = Vector3.Cross(dir.normalized, toCentre).normalized * (width * 0.5f);
                // Smooth paths: no joint overlap (additive quads would double up into beads).
                var ext = Vector3.zero;
                var baseIndex = verts.Count;
                verts.Add(a - ext - side);
                verts.Add(a - ext + side);
                verts.Add(b + ext + side);
                verts.Add(b + ext - side);
                // The glow texture is radial: u = 0.5, v across → a soft line.
                uvs.Add(new Vector2(0.5f, 0f));
                uvs.Add(new Vector2(0.5f, 1f));
                uvs.Add(new Vector2(0.5f, 1f));
                uvs.Add(new Vector2(0.5f, 0f));
                for (var k = 0; k < 4; k++)
                    cols.Add(c);
                tris.Add(baseIndex);
                tris.Add(baseIndex + 1);
                tris.Add(baseIndex + 2);
                tris.Add(baseIndex);
                tris.Add(baseIndex + 2);
                tris.Add(baseIndex + 3);
            }
        }

        static readonly Color MasteryGold = new(1f, 0.78f, 0.3f);

        void PaintTree()
        {
            var running = Running();
            foreach (var view in _nodes.Values)
            {
                var id = view.Node.Id;
                var level = Level(id);
                var met = Met(id);
                var max = ResearchCatalog.MaxLevel(id);
                var queued = QueuedOf(id) > 0;
                var selected = _selected == id;
                var hover = _hover == id;
                var c = view.Node.Color;

                var held = level - (id == running ? 1 : 0);
                var stars = 0;
                foreach (var m in ResearchCatalog.Masteries(id))
                    if (held >= m.Level)
                        stars++;
                float scale;
                if (id == running)
                {
                    Tint(view.CrystalRenderer, c, 2.4f, 1.1f, 2f);
                    scale = 1.15f;
                }
                else if (level > 0)
                {
                    Tint(view.CrystalRenderer, c, 1.2f, 0.25f);
                    scale = 1f;
                }
                else if (met)
                {
                    Tint(view.CrystalRenderer, c * 0.6f, 0.35f, 0.6f, 1.2f);
                    scale = 0.85f;
                }
                else
                {
                    Tint(view.CrystalRenderer, Locked, 0.05f, 0.1f, 0.8f);
                    scale = 0.7f;
                }

                if (selected)
                    scale *= 1.35f;
                view.Crystal.localScale = new Vector3(0.13f, 0.195f, 0.13f) * scale;
                var haloAlpha = selected ? 0.9f : hover ? 0.6f : id == running ? 0.55f : level > 0 ? 0.28f : met ? 0.14f : 0f;
                var halo = level > 0 || met || selected ? c : Locked;
                // A research with a mastery earned wears a gold halo (its milestones show on the analysis screen).
                if (stars > 0 && !selected)
                    halo = Color.Lerp(c, MasteryGold, 0.65f);
                Glow(view.Halo, new Color(halo.r, halo.g, halo.b, haloAlpha));
                view.Halo.transform.localScale = Vector3.one * (selected ? 0.55f : 0.42f);

                view.Name.color = level > 0 || met ? UiKit.TextBright : new Color(0.55f, 0.62f, 0.72f, 0.75f);
                var line = level > 0 ? Trans.Get("lvl") + " " + level : string.Empty;
                if (max > 0 && level >= max)
                    line = "MAX";
                if (stars > 0)
                    line += "  <color=#e8c040>★" + (stars > 1 ? stars.ToString() : string.Empty) + "</color>";
                if (id == running)
                    line = "<color=#e8c040>▲ " + Trans.Get("lvl") + " " + (level + 1) + "</color>";
                else if (queued)
                    line += (line.Length > 0 ? "  " : string.Empty) + "<color=#e8c040>+" + QueuedOf(id) + "</color>";
                view.Level.text = line;
            }

            RebuildLinks();
        }

        void SetHover(string tech)
        {
            if (_hover == tech)
                return;
            _hover = tech;
            if (tech != null)
                CicCue.Hover(_nodes[tech].Root.position);
            PaintTree();
        }

        void Select(string tech)
        {
            if (_busy)
                return;
            if (_selected != tech)
                _detailPage = 1;
            _selected = tech;
            CicCue.Ok(_nodes[tech].Root.position);
            PaintTree();
            RenderDetail();
            SpawnSample(tech);
        }

        // ── Synthesizer ───────────────────────────────────────────────────────────

        void BuildCore()
        {
            _coreRoot = new GameObject("Synthesizer").transform;
            _coreRoot.SetParent(transform, false);
            _coreRoot.localPosition = SynthPos;
            var dark = _art.DarkPanel(0.35f);
            var metal = _art.MetalPanel(0.45f);
            var violet = _art.Lit(Texture2D.whiteTexture, Accent, 2.6f);
            var ringTex = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;

            Cyl("Plinth", new Vector3(0f, 0.04f, 0f), new Vector3(1.0f, 0.04f, 1.0f), metal, _coreRoot);
            Cyl("Column", new Vector3(0f, 0.45f, 0f), new Vector3(0.5f, 0.45f, 0.5f), dark, _coreRoot);
            Cyl("Collar", new Vector3(0f, BeamLow - 0.03f, 0f), new Vector3(0.62f, 0.03f, 0.62f), metal, _coreRoot);
            Cyl("CollarGlow", new Vector3(0f, BeamLow, 0f), new Vector3(0.3f, 0.005f, 0.3f), violet, _coreRoot);
            Cyl("Crown", new Vector3(0f, BeamHigh + 0.02f, 0f), new Vector3(0.44f, 0.025f, 0.44f), metal, _coreRoot);
            Cyl("CrownGlow", new Vector3(0f, BeamHigh - 0.006f, 0f), new Vector3(0.24f, 0.004f, 0.24f), violet, _coreRoot);
            for (var i = 0; i < 3; i++)
            {
                var a = i * 120f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                Box("Strut" + i, dir * 0.24f + Vector3.up * ((BeamLow + BeamHigh) * 0.5f),
                    new Vector3(0.03f, BeamHigh - BeamLow, 0.03f), metal, Quaternion.Euler(0f, a, 0f), parent: _coreRoot);
            }

            var beam = Cyl("Beam", new Vector3(0f, (BeamLow + BeamHigh) * 0.5f, 0f),
                new Vector3(0.2f, (BeamHigh - BeamLow) * 0.5f, 0.2f),
                _art.Holo(Texture2D.whiteTexture, new Color(Accent.r, Accent.g, Accent.b, 0.16f)), _coreRoot);
            beam.name = "Beam";

            var intakeRing = GameObject.CreatePrimitive(PrimitiveType.Quad);
            intakeRing.name = "IntakeRing";
            Destroy(intakeRing.GetComponent<Collider>());
            intakeRing.transform.SetParent(_coreRoot, false);
            intakeRing.transform.localPosition = new Vector3(0f, BeamLow + 0.012f, 0f);
            intakeRing.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            intakeRing.transform.localScale = Vector3.one * 0.66f;
            intakeRing.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(ringTex, new Color(Accent.r, Accent.g, Accent.b, 0.7f));

            // Progress ring round the collar: segments light as the running research advances.
            for (var i = 0; i < RingSegments; i++)
            {
                var a = i * 360f / RingSegments;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var seg = Box("Progress" + i, dir * 0.345f + Vector3.up * (BeamLow - 0.005f),
                    new Vector3(0.05f, 0.018f, 0.025f), _ringOff, Quaternion.Euler(0f, a, 0f), parent: _coreRoot);
                _ring[i] = seg.GetComponent<MeshRenderer>();
            }

            var active = CrystalObject(_coreRoot, "ActiveCrystal", 0.11f);
            active.transform.localPosition = new Vector3(0f, (BeamLow + BeamHigh) * 0.5f, 0f);
            _activeCrystal = active.transform;
            _activeRenderer = active.GetComponent<MeshRenderer>();
            active.SetActive(false);

            _points = UiKit.Label(_coreRoot, "ResearchPoints", string.Empty, new Vector3(0f, BeamHigh + 0.24f, 0f), 0.9f,
                0.075f, new Color(0.85f, 0.75f, 1f, 1f));
            _points.richText = true;
            // Facing the stand (labels read along their local -Z).
            var toStand = Stand - SynthPos;
            toStand.y = 0f;
            _points.transform.rotation = transform.rotation * Quaternion.LookRotation(-toStand.normalized, Vector3.up);
        }

        /// <summary>Pads for queued research round the collar: maxQueue − 1 (one slot is the beam itself).</summary>
        void EnsureSlots(int count)
        {
            if (_slots.Count == count)
                return;
            foreach (var s in _slots)
            {
                if (s.Crystal != null)
                    Destroy(s.Crystal);
                if (s.Pad != null)
                    Destroy(s.Pad.gameObject);
            }

            _slots.Clear();
            var metal = _art.MetalPanel(0.45f);
            var glow = _art.Lit(Texture2D.whiteTexture, new Color(0.55f, 0.36f, 0.14f, 1f), 0.9f);
            for (var i = 0; i < count; i++)
            {
                // Spread on the side facing the stand (−x from the synthesizer), so each pad is in easy reach and
                // clear of the queue desk behind the machine.
                var span = Mathf.Min(150f, 38f * Mathf.Max(1, count - 1));
                var a = -90f + Mathf.Lerp(-span * 0.5f, span * 0.5f, count == 1 ? 0.5f : i / (float)(count - 1));
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var pad = Cyl("QueuePad" + i, dir * 0.6f + Vector3.up * (BeamLow - 0.02f), new Vector3(0.12f, 0.012f, 0.12f),
                    metal, _coreRoot).transform;
                Cyl("QueuePadGlow" + i, new Vector3(0f, 1.1f, 0f), new Vector3(0.45f, 0.2f, 0.45f), glow, pad);
                Box("QueueArm" + i, dir * 0.46f + Vector3.up * (BeamLow - 0.03f), new Vector3(0.03f, 0.02f, 0.28f), metal,
                    Quaternion.Euler(0f, a, 0f), parent: _coreRoot);
                _slots.Add(new QueueSlot { Pad = pad });
            }
        }

        Vector3 SlotHome(QueueSlot s) => s.Pad.position + Vector3.up * 0.1f;

        void PaintCore()
        {
            var running = Running();
            _activeCrystal.gameObject.SetActive(running != null);
            if (running != null)
            {
                var c = ResearchCatalog.TryNode(running, out var n) ? n.Color : Accent;
                Tint(_activeRenderer, c, 2.6f, 1.2f, 2f);
            }

            _points.text = "<size=60%>" + Trans.Get("researchPoints") + "</size>\n<b>" + Points().ToString("N0") + "</b>";

            EnsureSlots(Mathf.Max(1, MaxQueue() - 1));
            var queue = Queue();
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.Held)
                    continue;
                var row = queue != null && i < queue.Count ? queue[i] : null;
                var tech = row != null ? QueueTech(row) : null;
                var id = row != null ? FocusContext.AsInt(row["id"]) : 0;
                if (tech == null)
                {
                    if (slot.Crystal != null)
                        Destroy(slot.Crystal);
                    slot.Crystal = null;
                    slot.QueueId = 0;
                    slot.Tech = null;
                    continue;
                }

                if (slot.Crystal == null)
                    slot.Crystal = QueueCrystal(slot);
                slot.QueueId = id;
                slot.Tech = tech;
                slot.Crystal.transform.position = SlotHome(slot);
                var c = ResearchCatalog.TryNode(tech, out var n) ? n.Color : Accent;
                Tint(slot.Crystal.GetComponent<MeshRenderer>(), c, 1.3f, 0.5f);
            }

            UpdateRing(true);
        }

        GameObject QueueCrystal(QueueSlot slot)
        {
            var go = CrystalObject(transform, "QueuedCrystal", 0.075f);
            go.AddComponent<SphereCollider>().radius = 0.45f;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var grab = go.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.useDynamicAttach = true;
            grab.selectEntered.AddListener(_ =>
            {
                slot.Held = true;
                CicCue.Crystal(go.transform.position);
            });
            grab.selectExited.AddListener(_ => OnQueueReleased(slot));
            // Flat screens: E / tap takes it; set it down away from its pad to cancel, right click / Cancel to keep it.
            if (PcPlatformBoot.IsFlatScreen)
            {
                var flat = go.AddComponent<FlatGrabbable>();
                flat.Begin = () =>
                {
                    if (slot.Crystal != go || slot.QueueId <= 0)
                        return null;
                    slot.Held = true;
                    CicCue.Crystal(go.transform.position);
                    return new FlatCarry(go.transform, () => OnQueueReleased(slot), () =>
                    {
                        go.transform.position = SlotHome(slot);
                        OnQueueReleased(slot);
                    });
                };
            }

            return go;
        }

        /// <summary>A queued crystal pulled away from its pad and let go = cancel it (points refunded).</summary>
        void OnQueueReleased(QueueSlot slot)
        {
            slot.Held = false;
            if (slot.Crystal == null)
                return;
            var away = Vector3.Distance(slot.Crystal.transform.position, SlotHome(slot)) > 0.35f;
            if (away && slot.QueueId > 0)
            {
                var id = slot.QueueId;
                Destroy(slot.Crystal);
                slot.Crystal = null;
                AsyncTap.Run(Order("CancelQueuedResearch", new Dictionary<string, string> { { "id", id.ToString() } },
                    "vr.research.cancelled", slot.Tech));
                return;
            }

            slot.Crystal.transform.position = SlotHome(slot);
        }

        float Progress()
        {
            var end = FocusContext.AsLong(_empire?["working"]);
            var start = FocusContext.AsLong(_empire?["workingStart"]);
            var now = FleetOrderGate.UnixNow();
            if (end <= now)
                return 0f;
            // The server's own reading first (covers rows without workingStart).
            if (ServerTimers.Research() is { } server)
                return server;
            if (start <= 0 || start >= end)
                return 0f;
            return Mathf.Clamp01((now - start) / (float)(end - start));
        }

        void UpdateRing(bool force = false)
        {
            var lit = Running() != null ? Mathf.RoundToInt(Progress() * RingSegments) : 0;
            if (!force && lit == _ringLit)
                return;
            _ringLit = lit;
            for (var i = 0; i < RingSegments; i++)
                _ring[i].sharedMaterial = i < lit ? _ringOn : _ringOff;
        }

        // ── Sample cradle ─────────────────────────────────────────────────────────

        void BuildCradle()
        {
            var metal = _art.MetalPanel(0.45f);
            var dark = _art.DarkPanel(0.35f);
            Cyl("CradleBase", Cradle + new Vector3(0f, 0.02f, 0f), new Vector3(0.36f, 0.02f, 0.36f), metal);
            Cyl("CradleStem", Cradle + new Vector3(0f, CradleTop * 0.5f, 0f), new Vector3(0.06f, CradleTop * 0.5f, 0.06f), dark);
            Cyl("CradleCup", Cradle + new Vector3(0f, CradleTop, 0f), new Vector3(0.2f, 0.015f, 0.2f), metal);
            Cyl("CradleGlow", Cradle + new Vector3(0f, CradleTop + 0.016f, 0f), new Vector3(0.14f, 0.002f, 0.14f),
                _art.Lit(Texture2D.whiteTexture, Accent, 2.4f));
        }

        Vector3 CradleHome => transform.TransformPoint(Cradle + new Vector3(0f, CradleTop + 0.13f, 0f));

        /// <summary>The selected tech's sample flies from its crystal onto the cradle, ready to be grabbed.</summary>
        void SpawnSample(string tech)
        {
            if (_sample == null)
            {
                _sample = CrystalObject(transform, "Sample", 0.085f);
                _sample.AddComponent<SphereCollider>().radius = 0.5f;
                var body = _sample.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                _sampleGrab = _sample.AddComponent<XRGrabInteractable>();
                _sampleGrab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                _sampleGrab.throwOnDetach = false;
                _sampleGrab.useDynamicAttach = true;
                _sampleGrab.selectEntered.AddListener(_ =>
                {
                    _sampleHeld = true;
                    _sampleFlight = 1f;
                    CicCue.Crystal(_sample.transform.position);
                });
                _sampleGrab.selectExited.AddListener(_ => OnSampleReleased());
                // Flat screens: E / tap takes the sample, it follows the aim and snaps into the core's intake when
                // the aim passes through it; a click / tap there launches the research.
                if (PcPlatformBoot.IsFlatScreen)
                {
                    var flat = _sample.AddComponent<FlatGrabbable>();
                    flat.Begin = () =>
                    {
                        if (_sample == null || !_sample.activeSelf)
                            return null;
                        _sampleHeld = true;
                        _sampleFlight = 1f;
                        CicCue.Crystal(_sample.transform.position);
                        return new FlatCarry(_sample.transform, OnSampleReleased, () =>
                        {
                            _sampleHeld = false;
                            ReturnSample();
                        })
                        {
                            Snap = aim =>
                            {
                                var intake = _coreRoot.TransformPoint(new Vector3(0f, (BeamLow + BeamHigh) * 0.5f, 0f));
                                var along = Vector3.Dot(intake - aim.origin, aim.direction);
                                var nearest = aim.origin + aim.direction * Mathf.Max(0f, along);
                                return along > 0f && Vector3.Distance(nearest, intake) < IntakeRadius + 0.15f ? intake : (Vector3?)null;
                            }
                        };
                    };
                }
            }

            var c = _nodes.TryGetValue(tech, out var view) ? view.Node.Color : Accent;
            Tint(_sample.GetComponent<MeshRenderer>(), c, 1.8f, 0.8f, 2f);
            _sample.SetActive(true);
            if (!_sampleHeld)
            {
                _sampleFrom = view != null ? view.Root.position : CradleHome;
                _sample.transform.position = _sampleFrom;
                _sampleFlight = 0f;
            }
        }

        void OnSampleReleased()
        {
            _sampleHeld = false;
            if (_sample == null)
                return;
            var intake = _coreRoot.TransformPoint(new Vector3(0f, (BeamLow + BeamHigh) * 0.5f, 0f));
            if (_selected != null && Vector3.Distance(_sample.transform.position, intake) < IntakeRadius + 0.2f)
            {
                AsyncTap.Run(Launch(_selected));
                return;
            }

            ReturnSample();
        }

        void ReturnSample()
        {
            if (_sample == null || !_sample.activeSelf)
                return;
            _sampleFrom = _sample.transform.position;
            _sampleFlight = 0f;
        }

        // ── Screens ───────────────────────────────────────────────────────────────

        void BuildScreens()
        {
            // Every screen stands on something: the analysis and the queue on operator desks, the research bonuses
            // in a wall frame by the entrance.
            _detail = HoloScreen.Create(transform, "LabAnalysis", new Vector2(0.95f, 0.86f), Vector3.zero, Quaternion.identity,
                Trans.Get("researchLaboratory"));
            GateRoomDecor.SeatOnArm(_detail.transform, _decor.AnalysisMount, 0.86f, 14f);
            _detail.SetAccent(Accent, 0.5f);
            _detailBody = Body(_detail);
            _status = DiegeticUi.HoloLabel(_detail.Content, string.Empty, new Vector2(0f, -405f), new Vector2(900f, 40f),
                17f, DiegeticUi.CyanDim);
            _status.textWrappingMode = TextWrappingModes.Normal;

            _coreScreen = HoloScreen.Create(transform, "LabSynthesizer", new Vector2(0.9f, 0.7f), Vector3.zero, Quaternion.identity,
                Trans.Get("researchQueue"));
            GateRoomDecor.SeatOnArm(_coreScreen.transform, _decor.SynthMount, 0.7f, 14f);
            _coreScreen.SetAccent(Accent, 0.5f);
            _coreBody = Body(_coreScreen);

            var bonus = HoloScreen.Create(transform, "LabBonuses", LabDecor.BonusSize, Vector3.zero, Quaternion.identity,
                Trans.Get("vr.research.bonuses"));
            bonus.transform.SetParent(_decor.BonusMount, false);
            bonus.transform.localPosition = Vector3.zero;
            bonus.transform.localRotation = Quaternion.identity;
            bonus.SetAccent(Accent, 0.5f);
            _bonusBody = Body(bonus);
        }

        static RectTransform Body(HoloScreen screen)
        {
            var go = new GameObject("Body", typeof(RectTransform));
            go.transform.SetParent(screen.Content, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = screen.PixelSize;
            return rt;
        }

        static void Clear(RectTransform body)
        {
            for (var i = body.childCount - 1; i >= 0; i--)
                DestroyImmediate(body.GetChild(i).gameObject);
        }

        static TMP_Text Text(RectTransform body, string text, float x, float y, float width, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var left = align is TextAlignmentOptions.MidlineLeft or TextAlignmentOptions.TopLeft;
            var t = DiegeticUi.HoloLabel(body, text, new Vector2(left ? x + width * 0.5f : x, y),
                new Vector2(width, size * 1.9f), size, color, align);
            t.richText = true;
            return t;
        }

        Button Btn(RectTransform body, string label, float x, float y, float w, float h, System.Action act,
            DiegeticUi.BtnStyle style, bool enabled = true)
        {
            var b = DiegeticUi.HoloButton(body, label, new Vector2(x, y), new Vector2(w, h),
                () => { if (!_busy) act(); }, style);
            var t = b.GetComponentInChildren<TMP_Text>();
            t.enableAutoSizing = true;
            t.fontSizeMin = 10f;
            t.fontSizeMax = Mathf.Min(24f, h * 0.45f);
            b.interactable = enabled;
            return b;
        }

        void Bar(RectTransform body, Vector2 pos, Vector2 size, System.Func<float> value)
        {
            var back = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            back.transform.SetParent(body, false);
            var brt = back.GetComponent<RectTransform>();
            brt.sizeDelta = size;
            brt.anchoredPosition = pos;
            var bimg = back.GetComponent<Image>();
            bimg.color = new Color(0.16f, 0.12f, 0.3f, 0.9f);
            bimg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(back.transform, false);
            var frt = fill.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(0f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            var fimg = fill.GetComponent<Image>();
            fimg.color = Accent;
            fimg.raycastTarget = false;
            _bars.Add((fimg, value));
        }

        void SetStatus(string text, bool error = false)
        {
            if (_status == null)
                return;
            _status.text = text ?? string.Empty;
            _status.color = error ? UiKit.Danger : DiegeticUi.CyanDim;
        }

        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        static string Tone(string t, bool ok) => ok ? "<color=#7dffa0>" + t + "</color>" : "<color=#ff6a5a>" + t + "</color>";

        void RenderAll()
        {
            _live.Clear();
            _bars.Clear();
            PaintTree();
            PaintCore();
            RenderDetail();
            RenderCoreScreen();
            RenderBonuses();
        }

        /// <summary>Bonus wall columns: what a stat is for (economy and upkeep, fighting, moving).</summary>
        enum BonusColumn
        {
            Utility,
            Combat,
            Propulsion
        }

        static BonusColumn ColumnOf(string stat) => stat switch
        {
            "damage" or "armor" or "shield" or "scannerRange" => BonusColumn.Combat,
            "speed" or "prlRange" => BonusColumn.Propulsion,
            _ => BonusColumn.Utility
        };

        /// <summary>Without the server's effects table: a tech's column from its branch.</summary>
        static BonusColumn ColumnOfTech(string tech) => tech switch
        {
            "weapon" or "laser" or "ion" or "plasma" or "thermodynamics" or "armor" or "shield" or "radarTech" => BonusColumn.Combat,
            "combustionDrive" or "impulsionDrive" or "fusionDrive" or "hyperspaceDrive" or "prlBond" => BonusColumn.Propulsion,
            _ => BonusColumn.Utility
        };

        static readonly string[] ColumnKeys = { "vr.research.col.utility", "vr.research.col.combat", "vr.research.col.propulsion" };
        static readonly Color[] ColumnTints =
        {
            new(0.45f, 0.95f, 0.6f, 1f),
            new(1f, 0.45f, 0.4f, 1f),
            new(0.45f, 0.75f, 1f, 1f)
        };

        /// <summary>
        /// The research bonuses wall by the entrance, as a three-column table — Utility (economy, building, worlds),
        /// Combat, Propulsion: one row per bonus our finished levels give (summed per stat and module group as the
        /// server applies them; a research in progress does not count yet), its label, the modules it acts on, and
        /// the figure. Without the server's effects table: the researches we hold, in the same columns.
        /// </summary>
        void RenderBonuses()
        {
            if (_bonusBody == null)
                return;
            var running = Running();
            var sig = new System.Text.StringBuilder(128);
            foreach (var n in ResearchCatalog.All())
                sig.Append(Level(n.Id)).Append(',');
            sig.Append(running).Append(ResearchCatalog.HasEffects).Append(Trans.Lang);
            if (sig.ToString() == _bonusSig)
                return;
            _bonusSig = sig.ToString();
            Clear(_bonusBody);

            var columns = new List<(string Label, string Detail, string Value)>[3];
            for (var c = 0; c < 3; c++)
                columns[c] = new List<(string, string, string)>();
            if (ResearchCatalog.HasEffects)
            {
                // Per stat: the general share (effects on every module / empire-wide) and, for module-specific
                // effects, each module type's own total (its share + the general one) — "Hyperspace drive +485 %".
                var general = new Dictionary<string, (ResearchEffect E, float Gain)>();
                var perModule = new Dictionary<string, Dictionary<string, float>>();
                var statOrder = new List<string>();
                foreach (var n in ResearchCatalog.All())
                {
                    var lvl = Level(n.Id) - (running == n.Id ? 1 : 0);
                    if (lvl <= 0)
                        continue;
                    foreach (var e in ResearchCatalog.Effects(n.Id))
                    {
                        var gain = e.Gain(lvl);
                        if (Mathf.Approximately(gain, 0f))
                            continue;
                        if (!statOrder.Contains(e.Stat))
                            statOrder.Add(e.Stat);
                        if (e.Modules.Length == 0)
                        {
                            general[e.Stat] = general.TryGetValue(e.Stat, out var g) ? (g.E, g.Gain + gain) : (e, gain);
                            continue;
                        }

                        if (!perModule.TryGetValue(e.Stat, out var mods))
                            perModule[e.Stat] = mods = new Dictionary<string, float>();
                        foreach (var m in e.Modules)
                            mods[m] = (mods.TryGetValue(m, out var v) ? v : 0f) + gain;
                        if (!general.ContainsKey(e.Stat))
                            general[e.Stat] = (e, 0f);
                    }
                }

                // Rows in a fixed, readable order per column.
                string[] rank =
                {
                    "damage", "armor", "shield", "scannerRange", "speed", "prlRange", "power", "solarPower", "mine", "food", "harvestSpeed",
                    "habitability", "constructionTime", "buildingTime", "moduleBuildTime", "troopTrainingTime", "homeAndFarmBuildingTime", "colonizationTime",
                    "researchTime", "relation"
                };
                statOrder.Sort((a, b) =>
                {
                    var ia = System.Array.IndexOf(rank, a);
                    var ib = System.Array.IndexOf(rank, b);
                    return (ia < 0 ? 99 : ia).CompareTo(ib < 0 ? 99 : ib);
                });

                foreach (var stat in statOrder)
                {
                    var (e, baseGain) = general[stat];
                    var label = Trans.Get("vr.research.stat." + stat);
                    var col = columns[(int)ColumnOf(stat)];
                    var moduleStat = stat is "speed" or "damage" or "armor" or "shield";
                    if (!Mathf.Approximately(baseGain, 0f) || !perModule.ContainsKey(stat))
                        col.Add((label, moduleStat ? Trans.Get(stat == "damage" ? "vr.research.allWeapons" : "vr.research.allModules") : string.Empty,
                            e.Amount(baseGain)));
                    if (!perModule.TryGetValue(stat, out var mods))
                        continue;
                    // Strongest first: what the captain's best engines / cannons get.
                    var list = new List<KeyValuePair<string, float>>(mods);
                    list.Sort((a, b) => b.Value.CompareTo(a.Value));
                    foreach (var kv in list)
                        col.Add((label, Trans.Get(kv.Key), e.Amount(kv.Value + baseGain)));
                }
            }
            else
            {
                foreach (var n in ResearchCatalog.All())
                {
                    var lvl = Level(n.Id) - (running == n.Id ? 1 : 0);
                    if (lvl > 0)
                        columns[(int)ColumnOfTech(n.Id)].Add((Trans.Get(n.Id), string.Empty, Trans.Get("lvl") + " " + lvl));
                }
            }

            var px = _bonusBody.sizeDelta;
            var any = columns[0].Count + columns[1].Count + columns[2].Count > 0;
            const float margin = 30f;
            var colW = (px.x - margin * 4f) / 3f;
            // Under the screen's own title bar.
            var top = px.y * 0.5f - 165f;
            for (var c = 0; c < 3; c++)
            {
                var x0 = -px.x * 0.5f + margin + c * (colW + margin);
                var tint = ColumnTints[c];
                // Header: tinted band and title.
                Panel(_bonusBody, new Vector2(x0 + colW * 0.5f, top), new Vector2(colW, 44f), new Color(tint.r, tint.g, tint.b, 0.22f));
                Panel(_bonusBody, new Vector2(x0 + colW * 0.5f, top - 23f), new Vector2(colW, 3f), new Color(tint.r, tint.g, tint.b, 0.9f));
                Text(_bonusBody, "<b>" + Trans.Get(ColumnKeys[c]) + "</b>", x0 + colW * 0.5f, top, colW, 24f, tint,
                    TextAlignmentOptions.Center);

                var rows = columns[c];
                if (rows.Count == 0)
                {
                    Text(_bonusBody, "—", x0 + colW * 0.5f, top - 70f, colW, 22f, DiegeticUi.CyanDim, TextAlignmentOptions.Center);
                    continue;
                }

                var y = top - 62f;
                var hex = ColorUtility.ToHtmlStringRGB(Color.Lerp(tint, Color.white, 0.35f));
                foreach (var (label, detail, value) in rows)
                {
                    var h = detail.Length > 0 ? 66f : 46f;
                    if (y - h < -px.y * 0.5f + 20f)
                        break;
                    Panel(_bonusBody, new Vector2(x0 + colW * 0.5f, y - h * 0.5f + 8f), new Vector2(colW, h - 6f),
                        new Color(0.05f, 0.09f, 0.16f, 0.55f));
                    Text(_bonusBody, label, x0 + 12f, y - 6f, colW * 0.66f, 19f, UiKit.TextBright);
                    // Right-aligned text takes its centre: the value column ends 12 px inside the row.
                    Text(_bonusBody, "<b><color=#" + hex + ">" + value + "</color></b>", x0 + colW - 12f - colW * 0.18f, y - 6f,
                        colW * 0.36f, 24f,
                        UiKit.TextBright, TextAlignmentOptions.MidlineRight);
                    if (detail.Length > 0)
                        Text(_bonusBody, detail, x0 + 12f, y - 34f, colW - 24f, 15f, DiegeticUi.CyanDim);
                    y -= h;
                }
            }

            if (!any)
                Text(_bonusBody, Trans.Get("vr.research.bonusesEmpty"), 0f, -px.y * 0.5f + 60f, px.x * 0.85f, 20f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
        }

        /// <summary>A flat tinted panel on a screen body (table bands and row backs).</summary>
        static void Panel(RectTransform body, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Band", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(body, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        void RenderDetail()
        {
            Clear(_detailBody);
            var lab = ResearchCatalog.BestLab(_eco, out var labPlanet);
            var labName = labPlanet > 0 && _eco.TryGet(labPlanet, out var lp) && !string.IsNullOrEmpty(lp.Name)
                ? lp.Name
                : labPlanet > 0 ? "#" + labPlanet : "—";
            Text(_detailBody, Trans.Get("researchPoints") + "  <b><color=#d8c8ff>" + Points().ToString("N0") + "</color></b>",
                -445f, 300f, 440f, 19f, UiKit.TextBright);
            Text(_detailBody, Trans.Get("researchLab") + " " + Tone(Trans.Get("lvl") + " " + lab, lab > 0) +
                              "  <size=80%><color=#7fd8ff>" + labName + "</color></size>",
                15f, 300f, 430f, 18f, UiKit.TextBright, TextAlignmentOptions.MidlineRight);

            if (_selected == null || !_nodes.TryGetValue(_selected, out var view))
            {
                Text(_detailBody, Trans.Get("vr.research.pick"), 0f, 60f, 860f, 24f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                return;
            }

            var id = _selected;
            var node = view.Node;
            var level = Level(id);
            var running = Running() == id;
            var q = ResearchCatalog.Quote(_eco, id, level, QueuedOf(id), running, lab, Points());
            var max = q.MaxLevel;

            Text(_detailBody, "<b>" + Trans.Get(id) + "</b>", -445f, 246f, 600f, 34f, node.Color);
            Text(_detailBody, Trans.Get(node.CategoryKey), 170f, 246f, 275f, 17f, DiegeticUi.CyanDim,
                TextAlignmentOptions.MidlineRight);
            var lvlLine = Trans.Get("level") + " <b>" + level + "</b>" + (max > 0 ? " / " + max : string.Empty);
            if (running)
                lvlLine += "   <color=#e8c040>▲ " + Trans.Get("lvl") + " " + (level + 1) + "</color>";
            Text(_detailBody, lvlLine, -445f, 200f, 530f, 22f, UiKit.TextBright);

            // Effect, prerequisites and the full unlock list in one paged text block: built here (on
            // selection / refresh), never per frame; the page buttons only flip TMP's pageToDisplay.
            var info = DiegeticUi.HoloLabel(_detailBody, DetailText(id, level, lab), new Vector2(0f, -36f),
                new Vector2(890f, 428f), 18f, new Color(0.78f, 0.86f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            info.richText = true;
            info.textWrappingMode = TextWrappingModes.Normal;
            info.overflowMode = TextOverflowModes.Page;
            info.ForceMeshUpdate(true);
            var pages = Mathf.Max(1, info.textInfo.pageCount);
            _detailPage = Mathf.Clamp(_detailPage, 1, pages);
            info.pageToDisplay = _detailPage;
            if (pages > 1)
            {
                var pageLabel = Text(_detailBody, _detailPage + " / " + pages, 270f, 200f, 90f, 18f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                Button prev = null, next = null;
                void Turn(int d)
                {
                    _detailPage = Mathf.Clamp(_detailPage + d, 1, pages);
                    info.pageToDisplay = _detailPage;
                    pageLabel.text = _detailPage + " / " + pages;
                    prev.interactable = _detailPage > 1;
                    next.interactable = _detailPage < pages;
                }

                prev = Btn(_detailBody, "‹ " + Trans.Get("previous"), 160f, 200f, 120f, 40f, () => Turn(-1),
                    DiegeticUi.BtnStyle.Ghost, _detailPage > 1);
                next = Btn(_detailBody, Trans.Get("next") + " ›", 380f, 200f, 120f, 40f, () => Turn(1),
                    DiegeticUi.BtnStyle.Ghost, _detailPage < pages);
            }

            // Cost and duration of the next level, then the order.
            if (!q.AtMax)
                Text(_detailBody, Trans.Format("vr.research.nextLevel", q.TargetLevel) + " :  " +
                                  Tone(q.Points.ToString("N0") + " " + Trans.Get("vr.research.pts"), q.Affordable) +
                                  "   ·   " + Core.Holo.TravelPlanner.TimeText(q.Seconds),
                    -445f, -290f, 890f, 22f, UiKit.TextBright);

            string label;
            var style = DiegeticUi.BtnStyle.Cyan;
            var enabled = false;
            var full = Occupied() >= MaxQueue();
            if (q.AtMax)
                label = Trans.Get("maxLevelReached");
            else if (lab <= 0)
                label = Trans.Get("noResearchLab");
            else if (!q.Unlocked)
                label = Trans.Get("notEnoughtResearchLevel");
            else if (full)
                label = Trans.Get("queueFull");
            else if (!q.Affordable)
                label = Trans.Get("notEnoughResearchPoints");
            else
            {
                enabled = true;
                var busy = Running() != null;
                label = busy
                    ? Trans.Get("addToQueue") + "  (" + Trans.Get("lvl") + " " + q.TargetLevel + ")"
                    : Trans.Format("vr.research.launch", q.TargetLevel);
                style = busy ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Cyan;
            }

            if (!enabled)
                style = DiegeticUi.BtnStyle.Ghost;
            Btn(_detailBody, label, -210f, -350f, 440f, 56f, () => AsyncTap.Run(Launch(id)), style, enabled);
            if (enabled)
                Text(_detailBody, Trans.Get("vr.research.insertHint"), 30f, -350f, 415f, 16f, DiegeticUi.CyanDim);
        }

        /// <summary>
        /// The analysis text of a tech: its effect (desc&lt;Tech&gt;), prerequisites, then every unlock by level
        /// with its kind, green once our level reaches it. Rich text for one paged TMP block.
        /// </summary>
        string DetailText(string id, int level, int lab)
        {
            var sb = new System.Text.StringBuilder(1024);
            Section(sb, "vr.research.effect");
            sb.Append(Trans.Get(ResearchCatalog.DescKey(id))).Append('\n');
            // The numbers, as the server computes them: per level, and what our level gives now.
            var held = Mathf.Max(0, level - (Running() == id ? 1 : 0));
            foreach (var e in ResearchCatalog.Effects(id))
            {
                sb.Append("<color=#c8b8ff>• ").Append(e.Describe(e.Amount(e.PerLevel))).Append("</color> <size=85%><color=#9fb8c8>")
                    .Append(Trans.Get("vr.research.perLevelShort")).Append("</color></size>");
                if (held > 0)
                    sb.Append("   <color=#7dffb0>").Append(Trans.Format("vr.research.now", e.Amount(e.Gain(held)))).Append("</color>");
                if (e.HasCap)
                    sb.Append("  <size=85%><color=#9fb8c8>").Append(Trans.Format("vr.research.cap", e.Amount(e.Cap)))
                        .Append("</color></size>");
                sb.Append('\n');
            }

            sb.Append('\n');

            // Prerequisites (researchLab = best planet lab; the rest empire techs).
            Section(sb, "vr.research.requires");
            var any = false;
            foreach (var (key, need) in ResearchCatalog.Requirements(id))
            {
                var have = key == ResearchCatalog.Lab ? lab : Level(key);
                if (any)
                    sb.Append("     ");
                sb.Append("<nobr>").Append(Tone("●", have >= need)).Append(' ').Append(Trans.Get(key))
                    .Append("  <size=85%>").Append(Trans.Get("lvl")).Append(' ').Append(need).Append("</size></nobr>");
                any = true;
            }

            if (!any)
                sb.Append(Tone("●", true)).Append(" —");
            sb.Append("\n\n");

            // Milestones: a share of the points back every step levels, and the masteries at their levels.
            var masteries = ResearchCatalog.Masteries(id);
            var step = ResearchCatalog.MilestoneStep;
            var top = ResearchCatalog.MaxLevel(id);
            if ((masteries.Count > 0 || step > 0) && top != 1)
            {
                Section(sb, "researchMilestones");
                if (step > 0)
                    sb.Append("<size=85%><color=#9fb8c8>")
                        .Append(Trans.Format("researchMilestoneRefund", step,
                            Mathf.RoundToInt(ResearchCatalog.MilestoneRefundShare * 100f)))
                        .Append("</color></size>\n");
                var i = 0;
                while (i < masteries.Count)
                {
                    var at = masteries[i].Level;
                    if (top > 0 && at > top)
                        break;
                    sb.Append("<size=85%>").Append(Tone((held >= at ? "★ " : "☆ ") + Trans.Get("lvl") + " " + at, held >= at))
                        .Append("</size><indent=16%>");
                    var first = true;
                    for (; i < masteries.Count && masteries[i].Level == at; i++)
                    {
                        if (!first)
                            sb.Append(" · ");
                        sb.Append(masteries[i].Label());
                        first = false;
                    }

                    sb.Append("</indent>\n");
                }

                sb.Append('\n');
            }

            // What it opens, straight from the server configs.
            var unlocks = ResearchCatalog.Unlocks(id);
            if (unlocks.Count == 0)
                return sb.ToString();
            Section(sb, "vr.research.unlocks");
            foreach (var u in unlocks)
                sb.Append("<size=85%>").Append(Tone(Trans.Get("lvl") + " " + u.Level, level >= u.Level))
                    .Append("</size><pos=11%><size=80%><color=#b9a4ff>").Append(Trans.Get(u.KindKey))
                    .Append("</color></size><indent=30%>").Append(u.Label).Append("</indent>\n");
            return sb.ToString();
        }

        static void Section(System.Text.StringBuilder sb, string key) =>
            sb.Append("<size=85%><color=#7fd8ff><b>").Append(Trans.Get(key)).Append("</b></color></size>\n");

        void RenderCoreScreen()
        {
            Clear(_coreBody);
            var running = Running();
            var w = _coreBody.sizeDelta.x;
            var left = -w * 0.5f + 25f;
            Text(_coreBody, Trans.Format("vr.ops.queueSlots", Occupied(), MaxQueue()), left, 222f, 380f, 28f,
                DiegeticUi.CyanDim);
            Text(_coreBody, Boosters.QueueHint(), 100f, 222f, 300f, 22f, UiKit.TextBright,
                TextAlignmentOptions.MidlineRight);

            var y = 160f;
            if (running == null)
            {
                Text(_coreBody, Trans.Get("vr.research.idle"), 0f, y - 10f, w - 60f, 30f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.Center);
                y -= 70f;
            }
            else
            {
                var end = FocusContext.AsLong(_empire?["working"]);
                var color = ResearchCatalog.TryNode(running, out var n) ? n.Color : Accent;
                Text(_coreBody, "<b><color=" + Hex(color) + ">" + Trans.Get(running) + "</color></b>  <size=80%>" +
                                Trans.Get("lvl") + " " + (Level(running) + 1) + "</size>", left, y, 480f, 30f, UiKit.TextBright);
                var remain = Text(_coreBody, string.Empty, 130f, y, 235f, 25f, DiegeticUi.CyanDim,
                    TextAlignmentOptions.MidlineRight);
                _live.Add((remain, () => Core.Holo.TravelPlanner.TimeText(end - FleetOrderGate.UnixNow())));
                Bar(_coreBody, new Vector2(0f, y - 34f), new Vector2(w - 50f, 10f), Progress);
                var cost = BuildingCatalog.SpeedupCost(end - FleetOrderGate.UnixNow());
                var nova = FocusContext.AsInt(_empire?["nova"]);
                var canPay = cost == 0 || nova >= cost;
                var finish = cost == 0
                    ? Trans.Format("vr.ops.finish", Trans.Get("free"))
                    : Trans.Format("vr.ops.finish", cost + " " + Trans.Get("nova"));
                Btn(_coreBody, finish, 0f, y - 90f, 420f, 60f, () => AsyncTap.Run(Order("SpeedupResearch",
                        new Dictionary<string, string>(), "speedupResearchSuccess", running)),
                    canPay ? DiegeticUi.BtnStyle.Amber : DiegeticUi.BtnStyle.Ghost, canPay);
                y -= 150f;
            }

            var queue = Queue();
            if (queue == null || queue.Count == 0)
                return;
            for (var i = 0; i < queue.Count && i < 4; i++)
            {
                var row = queue[i];
                var tech = QueueTech(row);
                var qid = FocusContext.AsInt(row["id"]);
                Text(_coreBody, (i + 2) + ".  " + Trans.Get(tech) + "  <size=80%>" + Trans.Get("lvl") + " " +
                                FocusContext.AsInt(row["target_level"]) + "  <color=#7fd8ff>" +
                                Core.Holo.TravelPlanner.TimeText(FocusContext.AsFloat(row["duration"])) + "</color></size>",
                    left, y, 560f, 24f, UiKit.TextBright);
                Btn(_coreBody, "×", w * 0.5f - 60f, y, 80f, 50f, () => AsyncTap.Run(Order("CancelQueuedResearch",
                    new Dictionary<string, string> { { "id", qid.ToString() } }, "vr.research.cancelled", tech)),
                    DiegeticUi.BtnStyle.Danger);
                y -= 60f;
            }
        }

        // ── Empire state (GetMeEmpire: adjusted levels, queue, maxQueue) ──────────

        int Level(string tech) => FocusContext.AsInt(_empire?[tech]);

        int Points() => FocusContext.AsInt(_empire?[ResearchCatalog.Points]);

        string Running()
        {
            if (FocusContext.AsLong(_empire?["working"]) <= FleetOrderGate.UnixNow())
                return null;
            var t = FocusContext.AsString(_empire?["workingtype"]);
            return string.IsNullOrEmpty(t) ? null : t;
        }

        JArray Queue() => _empire?["researchQueue"] as JArray;

        /// <summary>Queue rows carry the tech in `research` (empire_research_queue column).</summary>
        static string QueueTech(JToken row)
        {
            var t = FocusContext.AsString(row?["research"]);
            return string.IsNullOrEmpty(t) ? FocusContext.AsString(row?["research_type"]) : t;
        }

        int QueuedOf(string tech)
        {
            var n = 0;
            var q = Queue();
            if (q == null)
                return 0;
            foreach (var row in q)
                if (QueueTech(row) == tech)
                    n++;
            return n;
        }

        int MaxQueue()
        {
            var n = FocusContext.AsInt(_empire?["maxQueue"]);
            return n > 0 ? n : 2;
        }

        int Occupied() => (Running() != null ? 1 : 0) + (Queue()?.Count ?? 0);

        bool Met(string tech)
        {
            var lab = ResearchCatalog.BestLab(_eco, out _);
            foreach (var (key, need) in ResearchCatalog.Requirements(tech))
                if ((key == ResearchCatalog.Lab ? lab : Level(key)) < need)
                    return false;
            return true;
        }

        async Task Refresh()
        {
            var auth = AuthManager.Ensure();
            var r = await auth.FetchMe();
            if (r.Ok && auth.Empire != null)
            {
                _empire = auth.Empire;
                _eco.AdoptEmpire(_empire, adjusted: true);
            }

            var end = FocusContext.AsLong(_empire?["working"]);
            _refreshAt = end > FleetOrderGate.UnixNow() ? end + 1 : 0;
            _nextPoll = Time.unscaledTime + 8f;
            if (Inside)
                RenderAll();
        }

        async Task Launch(string tech)
        {
            var lab = ResearchCatalog.BestLab(_eco, out var planet);
            if (lab <= 0 || planet <= 0)
            {
                SetStatus(Trans.Get("noResearchLab"), true);
                CicCue.Fail(_coreRoot.position);
                ReturnSample();
                return;
            }

            // The synthesiser takes the sample (the reply lands a moment later).
            CicCue.Synth(_coreRoot.position);
            var ok = await Order("ImproveResearch", new Dictionary<string, string>
            {
                { "research", tech },
                { "planet", planet.ToString() }
            }, null, tech);
            if (ok && _sample != null)
            {
                _burst.transform.position = _coreRoot.TransformPoint(new Vector3(0f, (BeamLow + BeamHigh) * 0.5f, 0f));
                _burst.Emit(80);
                _sample.SetActive(false);
            }
            else
            {
                ReturnSample();
            }
        }

        async Task<bool> Order(string action, Dictionary<string, string> q, string okKey, string tech)
        {
            if (_busy)
                return false;
            _busy = true;
            // Any order may replace the running job: its server progress is read again.
            ServerTimers.Invalidate();
            ApiResult r;
            try
            {
                r = await ActionJs.Get(action, q);
                var at = _coreRoot.position + Vector3.up;
                if (r.Ok)
                {
                    CicCue.Ok(at);
                    if (okKey == null)
                    {
                        // ImproveResearch: empty body = started now; JSON {queued, targetLevel} = added to the queue.
                        var queued = false;
                        var target = 0;
                        try
                        {
                            if (!string.IsNullOrEmpty(r.Body) && r.Body.TrimStart().StartsWith("{"))
                            {
                                var o = JObject.Parse(r.Body);
                                queued = FocusContext.AsBool(o["queued"]);
                                target = FocusContext.AsInt(o["targetLevel"]);
                            }
                        }
                        catch
                        {
                            // Keep the generic line.
                        }

                        if (!queued)
                            int.TryParse(r.Body?.Trim(), out target);
                        var status = queued
                            ? Trans.Format("vr.research.queued", Trans.Get(tech), target)
                            : Trans.Format("vr.research.started", Trans.Get(tech));
                        // A milestone level pays its share back when it starts (a queued one when its turn comes).
                        var refund = queued ? 0 : ResearchCatalog.MilestoneRefund(tech, target);
                        if (refund > 0)
                            status += "\n<color=#e8c040>" + Trans.Format("researchMilestoneReached", refund) + "</color>";
                        SetStatus(status);
                    }
                    else
                    {
                        SetStatus(Trans.Format(okKey, Trans.Get(tech)));
                    }
                }
                else
                {
                    CicCue.Fail(at);
                    SetStatus(string.IsNullOrEmpty(r.Error) ? Trans.Get("vr.common.error") : r.Error, true);
                }

                var bark = Core.Crew.BarkDirector.Instance;
                if (!r.Ok && r.Error == Trans.Get("queueFull"))
                    bark?.Say(CrewDialogue.Role.Science, "queueFull", 3);
                else
                    bark?.OrderResult(CrewDialogue.Role.Science, action, r, Trans.Get(tech));
            }
            finally
            {
                _busy = false;
            }

            await Refresh();
            return r.Ok;
        }

        // ── Enter / leave ─────────────────────────────────────────────────────────

        public async Task Enter()
        {
            if (DiplomacyRoom.InRoomBeyondCorridor)
                return;
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
            _empire = AuthManager.Ensure().Empire;
            await _eco.RefreshNow();
            await Refresh();
            if (_selected == null && Running() is { } running)
                _selected = running;
            RenderAll();
            await fade.FadeIn();
            CicCue.Ok(transform.position + Vector3.up);
        }

        async Task Leave()
        {
            if (!Inside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
            Inside = false;
            _sampleHeld = false;
            if (_sample != null)
                _sample.SetActive(false);
            // Out into the corridor, in front of this room's door.
            CorridorRoom.ReturnPlayer(CorridorRoom.Slot.LabPort);

            gameObject.SetActive(false);
            await fade.FadeIn();
        }

        void Update()
        {
            if (!Inside)
                return;
            var dt = Time.deltaTime;
            var running = Running();
            foreach (var view in _nodes.Values)
            {
                view.Spin += dt * (view.Node.Id == running ? 90f : 14f);
                view.Crystal.localRotation = Quaternion.Euler(0f, view.Spin, 0f);
            }

            if (_activeCrystal.gameObject.activeSelf)
                _activeCrystal.localRotation = Quaternion.Euler(0f, Time.time * 120f, 0f);

            // Sample: flight to the cradle, then a slow bob until grabbed.
            if (_sample != null && _sample.activeSelf && !_sampleHeld)
            {
                if (_sampleFlight < 1f)
                {
                    _sampleFlight = Mathf.Min(1f, _sampleFlight + dt * 1.8f);
                    var t = MotionEase.Smooth01(_sampleFlight);
                    var arc = Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.35f);
                    _sample.transform.position = Vector3.Lerp(_sampleFrom, CradleHome, t) + arc;
                }
                else
                {
                    _sample.transform.position = CradleHome + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.012f);
                }

                _sample.transform.rotation = transform.rotation * Quaternion.Euler(0f, Time.time * 40f, 0f);
            }

            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + 0.5f;
                foreach (var (t, v) in _live)
                {
                    if (t == null)
                        continue;
                    // Only when it changed: an unchanged assignment still rebuilds the text mesh.
                    var s = v();
                    if (t.text != s)
                        t.text = s;
                }
                foreach (var (img, v) in _bars)
                    if (img != null)
                        img.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v()), 1f);
                UpdateRing();

                // The server starts the next queued research lazily: re-read when the running one ends.
                var due = _refreshAt > 0 && FleetOrderGate.UnixNow() >= _refreshAt;
                if (!_busy && (due || Time.unscaledTime >= _nextPoll))
                {
                    _refreshAt = 0;
                    _nextPoll = Time.unscaledTime + 8f;
                    AsyncTap.Run(Refresh());
                }
            }
        }
    }
}
