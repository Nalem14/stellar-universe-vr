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
        const float RoomRadius = 4.9f;
        const float RoomHeight = 4.4f;
        static readonly Vector3 SynthPos = new(1.4f, 0f, 0.1f);
        const float BeamLow = 0.92f;
        const float BeamHigh = 1.85f;
        static readonly Vector3 Cradle = new(0.55f, 0f, 0.5f);
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
        BountyBoard _bounties;
        TMP_Text _boardStatus;
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
            var deck = _art.DeckMat(0.45f);
            var wall = _art.DarkPanel(0.3f);
            var rib = _art.MetalPanel(0.4f);
            var violet = _art.Lit(Texture2D.whiteTexture, Accent, 2.6f);
            var cyan = _art.CyanEmit(2.2f);
            var ring = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;

            Box("Floor", new Vector3(0f, -0.05f, 0f), new Vector3(RoomRadius * 2.1f, 0.1f, RoomRadius * 2.1f), deck,
                solid: true);
            Box("Ceiling", new Vector3(0f, RoomHeight, 0f), new Vector3(RoomRadius * 2.1f, 0.1f, RoomRadius * 2.1f), wall);

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
                Box("BaseLine" + i, n * (RoomRadius - 0.09f) + Vector3.up * 0.12f, new Vector3(width * 0.9f, 0.025f, 0.02f),
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
            Light("KeyOverhead", new Vector3(0f, RoomHeight - 0.5f, -0.8f), new Color(0.75f, 0.92f, 1f), 1.1f, 6f);

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
                Glow(view.Halo, new Color(halo.r, halo.g, halo.b, haloAlpha));
                view.Halo.transform.localScale = Vector3.one * (selected ? 0.55f : 0.42f);

                view.Name.color = level > 0 || met ? UiKit.TextBright : new Color(0.55f, 0.62f, 0.72f, 0.75f);
                var line = level > 0 ? Trans.Get("lvl") + " " + level : string.Empty;
                if (max > 0 && level >= max)
                    line = "MAX";
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
                // Spread on the side facing the stand, so each pad is in easy reach.
                var a = Mathf.Lerp(-150f, -30f, count == 1 ? 0.5f : i / (float)(count - 1)) - 90f;
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                var pad = Cyl("QueuePad" + i, dir * 0.52f + Vector3.up * (BeamLow - 0.02f), new Vector3(0.12f, 0.012f, 0.12f),
                    metal, _coreRoot).transform;
                Cyl("QueuePadGlow" + i, new Vector3(0f, 1.1f, 0f), new Vector3(0.45f, 0.2f, 0.45f), glow, pad);
                Box("QueueArm" + i, dir * 0.42f + Vector3.up * (BeamLow - 0.03f), new Vector3(0.03f, 0.02f, 0.2f), metal,
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
            grab.selectEntered.AddListener(_ => slot.Held = true);
            grab.selectExited.AddListener(_ => OnQueueReleased(slot));
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
                });
                _sampleGrab.selectExited.AddListener(_ => OnSampleReleased());
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
            var eye = transform.TransformPoint(Stand + Vector3.up * WorldScale.EyeStanding);
            _detail = HoloScreen.Create(transform, "LabAnalysis", new Vector2(0.95f, 0.86f),
                new Vector3(-1.35f, 1.42f, 0.2f), Quaternion.identity, Trans.Get("researchLaboratory"));
            ScreenMount.FaceViewer(_detail.transform, eye, 1f, 6f);
            _detail.SetAccent(Accent, 0.5f);
            _detailBody = Body(_detail);
            _status = DiegeticUi.HoloLabel(_detail.Content, string.Empty, new Vector2(0f, -405f), new Vector2(900f, 40f),
                17f, DiegeticUi.CyanDim);
            _status.textWrappingMode = TextWrappingModes.Normal;

            _coreScreen = HoloScreen.Create(transform, "LabSynthesizer", new Vector2(0.9f, 0.7f),
                new Vector3(1.4f, 1.45f, -0.62f), Quaternion.identity, Trans.Get("researchQueue"));
            ScreenMount.FaceViewer(_coreScreen.transform, eye, 1f, 6f);
            _coreScreen.SetAccent(Accent, 0.5f);
            _coreBody = Body(_coreScreen);

            // Contracts board, back-left beside the door: accept a bounty, hand it in on site.
            var board = HoloScreen.Create(transform, "LabContracts", new Vector2(1.25f, 0.88f),
                new Vector3(-1.3f, 1.45f, -1.0f), Quaternion.identity, Trans.Get("bounties"));
            ScreenMount.FaceViewer(board.transform, eye, 1f, 6f);
            board.SetAccent(new Color(1f, 0.45f, 0.4f, 1f), 0.5f);
            var boardBody = Body(board);
            _boardStatus = DiegeticUi.HoloLabel(board.Content, string.Empty, new Vector2(0f, -385f),
                new Vector2(1180f, 40f), 20f, DiegeticUi.CyanDim);
            _boardStatus.textWrappingMode = TextWrappingModes.Normal;
            _bounties = new BountyBoard(boardBody, _focus, (t, e) =>
            {
                _boardStatus.text = t ?? string.Empty;
                _boardStatus.color = e ? UiKit.Danger : DiegeticUi.CyanDim;
            });
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
            Text(_detailBody, lvlLine, -445f, 200f, 880f, 22f, UiKit.TextBright);

            var desc = DiegeticUi.HoloLabel(_detailBody, Trans.Get(ResearchCatalog.DescKey(id)), new Vector2(0f, 128f),
                new Vector2(890f, 100f), 19f, new Color(0.78f, 0.86f, 0.95f, 1f), TextAlignmentOptions.TopLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
            desc.overflowMode = TextOverflowModes.Ellipsis;

            // Prerequisites (researchLab = best planet lab; the rest empire techs).
            var y = 58f;
            Text(_detailBody, Trans.Get("vr.research.requires"), -445f, y, 400f, 17f, DiegeticUi.CyanDim);
            y -= 36f;
            var i = 0;
            foreach (var (key, need) in ResearchCatalog.Requirements(id))
            {
                var have = key == ResearchCatalog.Lab ? lab : Level(key);
                var ok = have >= need;
                Text(_detailBody, Tone("●", ok) + " " + Trans.Get(key) + "  <size=85%>" + Trans.Get("lvl") + " " +
                                  need + "</size>", -445f + (i % 2) * 450f, y - (i / 2) * 34f, 430f, 22f, UiKit.TextBright);
                i++;
            }

            if (i == 0)
                Text(_detailBody, Tone("●", true) + " —", -445f, y, 430f, 19f, UiKit.TextBright);
            y -= Mathf.Max(1, Mathf.CeilToInt(i / 2f)) * 34f + 12f;

            // What it opens, straight from the server configs.
            var unlocks = ResearchCatalog.Unlocks(id);
            if (unlocks.Count > 0)
            {
                Text(_detailBody, Trans.Get("vr.research.unlocks"), -445f, y, 400f, 17f, DiegeticUi.CyanDim);
                y -= 36f;
                for (var u = 0; u < unlocks.Count && u < 6; u++)
                {
                    var (key, need, kind) = unlocks[u];
                    var ok = level >= need;
                    Text(_detailBody, "<size=80%><color=#b9a4ff>" + Trans.Get(kind) + "</color></size>  " + Trans.Get(key) +
                                      "  <size=80%>" + Tone(Trans.Get("lvl") + " " + need, ok) + "</size>",
                        -445f + (u % 2) * 450f, y - (u / 2) * 32f, 430f, 20f, UiKit.TextBright);
                }

                y -= Mathf.CeilToInt(Mathf.Min(unlocks.Count, 6) / 2f) * 28f + 8f;
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

        void RenderCoreScreen()
        {
            Clear(_coreBody);
            var running = Running();
            var w = _coreBody.sizeDelta.x;
            var left = -w * 0.5f + 25f;
            Text(_coreBody, Trans.Format("vr.ops.queueSlots", Occupied(), MaxQueue()), left, 222f, 380f, 28f,
                DiegeticUi.CyanDim);
            if (FocusContext.AsBool(_empire?["hasNovaPass"]))
                Text(_coreBody, "<color=#ffd700>Nova Pass</color>", 100f, 222f, 300f, 26f, UiKit.TextBright,
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

                        SetStatus(queued
                            ? Trans.Format("vr.research.queued", Trans.Get(tech), target)
                            : Trans.Format("vr.research.started", Trans.Get(tech)));
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
            if (DiplomacyRoom.AnyRoomInside)
                return;
            var fade = ViewFade.Ensure();
            await fade.FadeOut();
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
            AsyncTap.Run(_bounties.Refresh());
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
            var rig = FindFirstObjectByType<XROrigin>();
            var bridge = FindFirstObjectByType<BridgeViewRig>();
            if (rig != null && bridge != null && bridge.BridgeMount != null)
            {
                rig.transform.SetParent(bridge.BridgeMount, false);
                bridge.PutPlayerOnDeck();
            }

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
                    if (t != null)
                        t.text = v();
                foreach (var (img, v) in _bars)
                    if (img != null)
                        img.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v()), 1f);
                UpdateRing();
                _bounties.Tick();

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
