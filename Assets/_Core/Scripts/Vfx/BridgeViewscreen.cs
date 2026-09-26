using System.Collections.Generic;
using Core.App;
using Core.Holo;
using Core.Stations;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// The main viewscreen: a curved panel in the forward bulkhead, concave toward the captain, showing the hull
    /// camera (<see cref="ViewscreenCamera"/>) with its HUD drawn into the same feed — so the target brackets sit
    /// exactly on what the camera films. It chooses what to show, in this order:
    /// <list type="number">
    /// <item>Red alert — a battle on the table, an engagement or a siege here: the foe framed, round, hull, hostiles.</item>
    /// <item>Tracking — the ship picked or aimed at on the holo table, or a bracket the captain has looked at for a
    /// moment (gaze lock): framed, with its card (owner, state, orbit, habitability).</item>
    /// <item>Transit — our ship under way: destination and ETA with progress.</item>
    /// <item>Survey — at an orbital station, the world below; on a ship, straight ahead, then a slow tour of the
    /// system's bodies and contacts, with the system card.</item>
    /// </list>
    /// Ship bridge = cyan, orbital station = amber. Driven by focus events and a 0.5 s evaluation; the markers move
    /// only on the frames the hull camera renders.
    /// </summary>
    public sealed partial class BridgeViewscreen : MonoBehaviour
    {
        enum Mode
        {
            Survey,
            Transit,
            Tracking,
            RedAlert
        }

        const float W = ViewscreenCamera.Width;
        const float H = ViewscreenCamera.Height;
        const int MaxBrackets = 14;
        const int Cols = 40;
        const int Segments = 36;
        const float Sag = 0.28f;
        const float Bezel = 0.13f;
        static float PanelWidth => BridgeShell.ScreenWidth - Bezel * 2f;
        static float PanelBottom => BridgeShell.ScreenBottom + Bezel;
        static float PanelTop => BridgeShell.ScreenTop - Bezel * 0.8f;
        /// <summary>Seconds the screen keeps a table target after the pointer leaves it.</summary>
        const float TableHold = 8f;
        const float GazeLock = 0.9f;
        const float GazeHold = 25f;
        static readonly Color Red = new(1f, 0.26f, 0.22f, 1f);

        sealed class Target
        {
            public int Key;
            public int Kind; // 0 star, 1 planet, 2 fleet
            public int Id;
            public Transform T;
            public float Radius;
            public string Name;
            public Color Tint;
        }

        sealed class Bracket
        {
            public RectTransform Root;
            public Image[] Lines;
            public TMP_Text Label;
            public float Size = -1f;
            public int Key;
        }

        FocusContext _focus;
        SystemExterior _exterior;
        HexBattleController _hex;
        ViewscreenCamera _cam;
        Transform _hud;
        Material _screenMat;
        Material _statusMat;
        CicArtKit _art;
        readonly List<RectTransform> _ticks = new();
        readonly List<TMP_Text> _tickLabels = new();
        Vector3 _screenCentre;
        Vector3 _screenRight;
        Vector3 _screenUp;
        Vector3 _screenNormal;

        TMP_Text _mode;
        TMP_Text _system;
        TMP_Text _clock;
        TMP_Text _status;
        TMP_Text _cardTitle;
        TMP_Text _cardBody;
        Image _cardAccent;
        Image _barBack;
        Image _barFill;
        Image[] _corners;
        Image[] _reticle;
        readonly List<Bracket> _brackets = new();
        readonly List<Target> _targets = new();
        readonly Dictionary<int, long> _transitSeen = new();

        Mode _current = Mode.Survey;
        Target _subject;
        int _subjectKey = -1;
        bool _targetsDirty = true;
        float _nextEval;
        int _tableKey = -1;
        float _tableUntil;
        string _tablePreview;
        int _gazeCandidate;
        float _gazeDwell;
        int _gazeKey;
        float _gazeUntil;
        float _switch;
        float _nextCullFix;
        Color _accent = CicArtKit.Cyan;

        public static BridgeViewscreen Build(CicEnvironment host, FocusContext focus, SystemExterior exterior,
            HexBattleController hex)
        {
            var go = new GameObject("MainViewscreen");
            go.transform.SetParent(host.transform, false);
            var view = go.AddComponent<BridgeViewscreen>();
            view._focus = focus;
            view._exterior = exterior;
            view._hex = hex;
            view._art = host.Art;
            view._cam = ViewscreenCamera.Create(host.transform);
            view.BuildPanel(host.transform);
            view.BuildHud();
            if (focus != null)
            {
                focus.Changed += view.OnFocus;
                focus.FleetsChanged += view.OnFocus;
            }

            if (TacticalCommand.Instance != null)
                TacticalCommand.Instance.Changed += view.OnSelection;
            if (AnomalyService.Instance != null)
                AnomalyService.Instance.Changed += view.OnAnomalies;
            if (CommsService.Instance != null)
                CommsService.Instance.Changed += view.OnSelection;
            view.BindDirection();
            Instance = view;
            return view;
        }

        /// <summary>Focused system: systems have no names, only galaxy coordinates.</summary>
        public static string SystemLabel(FocusContext focus) => GalaxyCatalog.Label(focus.SystemId);

        /// <summary>Planet the virtual station orbits (name, else #id).</summary>
        public static string StationPlanetName(FocusContext focus)
        {
            if (focus == null || focus.ViewPlanetId <= 0)
                return string.Empty;
            var planet = focus.FindPlanet(focus.ViewPlanetId);
            return planet != null && !string.IsNullOrEmpty(planet.Name) ? planet.Name : "#" + focus.ViewPlanetId;
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= OnFocus;
                _focus.FleetsChanged -= OnFocus;
            }

            if (TacticalCommand.Instance != null)
                TacticalCommand.Instance.Changed -= OnSelection;
            if (AnomalyService.Instance != null)
                AnomalyService.Instance.Changed -= OnAnomalies;
            if (CommsService.Instance != null)
                CommsService.Instance.Changed -= OnSelection;
            UnbindDirection();
            if (Instance == this)
                Instance = null;
        }

        void OnFocus()
        {
            _targetsDirty = true;
            _nextEval = 0f;
            WatchVoyage();
        }

        /// <summary>The table changed (pointer on a token, a ship picked): latch it now, the pointer may leave next frame.</summary>
        void OnSelection()
        {
            _nextEval = 0f;
            CaptureTable();
        }

        void CaptureTable()
        {
            var tc = TacticalCommand.Instance;
            if (tc == null || _focus == null)
                return;
            if (_targetsDirty)
                RebuildTargets();
            var aimed = tc.AimedTarget ?? tc.Hovered;
            var k = KeyOf(aimed);
            if (k >= 0 && FindTarget(k) != null)
            {
                _tableKey = k;
                _tableUntil = Time.unscaledTime + TableHold;
                _tablePreview = tc.PreviewFor(aimed);
            }
            else if (tc.SelectedFleetId > 0 && tc.SelectedFleetId != _focus.ViewFleetId && FindTarget(2_000_000 + tc.SelectedFleetId) != null)
            {
                _tableKey = 2_000_000 + tc.SelectedFleetId;
                _tableUntil = Time.unscaledTime + TableHold;
                _tablePreview = null;
            }
        }

        void OnAnomalies(int systemId) => OnFocus();

        // ── The panel ────────────────────────────────────────────────────────────

        /// <summary>A cylindrical panel across the forward aperture: edges flush, centre 0.3 m back (concave to the chair).</summary>
        void BuildPanel(Transform room)
        {
            const int cols = Cols;
            var e = BridgeShell.Forward;
            var len = BridgeShell.EdgeLength(e);
            var u0 = len * 0.5f - PanelWidth * 0.5f;
            var y0 = PanelBottom;
            var y1 = PanelTop;
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var n = new List<Vector3>();
            var t = new List<int>();
            var normal = BridgeShell.EdgeNormal(e);
            for (var i = 0; i <= cols; i++)
            {
                var s = i / (float)cols;
                var x = s * 2f - 1f;
                var depth = Depth(s);
                var u = u0 + s * PanelWidth;
                var bottom = BridgeShell.EdgePoint(e, u, depth, y0);
                var top = BridgeShell.EdgePoint(e, u, depth, y1);
                v.Add(bottom);
                v.Add(top);
                // The edge runs toward −x: the feed's left edge is on the viewer's left (+u end).
                uv.Add(new Vector2(1f - s, 0f));
                uv.Add(new Vector2(1f - s, 1f));
                var tangent = BridgeShell.EdgeDir(e) + normal * (2f * Sag * x * 2f / PanelWidth);
                var nn = Vector3.Cross(Vector3.up, tangent).normalized;
                if (Vector3.Dot(nn, normal) < 0f)
                    nn = -nn;
                n.Add(nn);
                n.Add(nn);
            }

            for (var i = 0; i < cols; i++)
            {
                var a = i * 2;
                // Facing the room (+normal): winding checked against the first quad's face.
                var face = Vector3.Cross(v[a + 2] - v[a], v[a + 1] - v[a]);
                if (Vector3.Dot(face, normal) > 0f)
                    t.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                else
                    t.AddRange(new[] { a, a + 1, a + 2, a + 1, a + 3, a + 2 });
            }

            var mesh = new Mesh { name = "SU_ViewscreenPanel" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();

            var panel = new GameObject("ViewscreenPanel");
            panel.transform.SetParent(transform, false);
            panel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = panel.AddComponent<MeshRenderer>();
            var shader = Shader.Find("SU/Viewscreen");
            _screenMat = shader != null ? new Material(shader) : new Material(Shader.Find("SU/UnlitEmissive"));
            _screenMat.name = "SU_Viewscreen";
            _screenMat.mainTexture = _cam.Texture;
            mr.sharedMaterial = _screenMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _screenCentre = BridgeShell.EdgePoint(e, len * 0.5f, 0f, (y0 + y1) * 0.5f);
            BuildHousing(room);
            _screenRight = -BridgeShell.EdgeDir(e);
            _screenUp = Vector3.up;
            _screenNormal = normal;
            _ = room;
        }

        /// <summary>Depth of the curved panel at s ∈ [0,1] across it (edges flush, centre back toward the hull).</summary>
        static float Depth(float s)
        {
            var x = s * 2f - 1f;
            return -Sag * (1f - x * x) + 0.03f;
        }

        /// <summary>
        /// The screen as a device, not a window: a dark bezel that follows the curve, a hood of emitters over it,
        /// a segmented status bar under it (a light chase in the mode colour) and two lit pylons flanking it.
        /// </summary>
        void BuildHousing(Transform room)
        {
            var e = BridgeShell.Forward;
            var len = BridgeShell.EdgeLength(e);
            var a0 = len * 0.5f - BridgeShell.ScreenWidth * 0.5f;
            var a1 = len * 0.5f + BridgeShell.ScreenWidth * 0.5f;
            var p0 = len * 0.5f - PanelWidth * 0.5f;
            var n = BridgeShell.EdgeNormal(e);
            var dir = BridgeShell.EdgeDir(e);
            var bezel = new ShellMesh();
            var glow = new ShellMesh();
            var metal = new ShellMesh();

            Vector3 P(float s, float lift, float y) => BridgeShell.EdgePoint(e, p0 + s * PanelWidth, Depth(s) + lift, y);
            Vector3 A(float s, float lift, float y) =>
                BridgeShell.EdgePoint(e, a0 + s * (a1 - a0), Depth(Mathf.Clamp01((a0 + s * (a1 - a0) - p0) / PanelWidth)) + lift, y);

            // Top and bottom bezel bands along the curve, with their lips back to the panel and out to the reveal.
            for (var i = 0; i < Cols; i++)
            {
                var sa = i / (float)Cols;
                var sb = (i + 1f) / Cols;
                foreach (var top in new[] { true, false })
                {
                    var yIn = top ? PanelTop : PanelBottom;
                    var yOut = top ? BridgeShell.ScreenTop : BridgeShell.ScreenBottom;
                    var up = top ? Vector3.up : Vector3.down;
                    bezel.Quad(A(sa, 0.12f, yOut), A(sb, 0.12f, yOut), P(sb, 0.12f, yIn), P(sa, 0.12f, yIn), n,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 0.8f, 1f, 1f);
                    bezel.Quad(P(sa, 0.12f, yIn), P(sb, 0.12f, yIn), P(sb, 0f, yIn), P(sa, 0f, yIn), -up,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 0.5f, 0.5f);
                    bezel.Quad(A(sa, 0.12f, yOut), A(sb, 0.12f, yOut), A(sb, -0.45f, yOut), A(sa, -0.45f, yOut), up,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.9f, 0.4f, 0.4f);
                    // A hairline light guide on the inner lip.
                    var yg = top ? yIn + 0.004f : yIn - 0.004f;
                    glow.Quad(P(sa, 0.121f, yg), P(sb, 0.121f, yg), P(sb, 0.121f, yg + (top ? 0.012f : -0.012f)),
                        P(sa, 0.121f, yg + (top ? 0.012f : -0.012f)), n, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
                }
            }

            // Side bezels (flat, at each end of the curve).
            foreach (var (s, sgn) in new[] { (0f, 1f), (1f, -1f) })
            {
                var outerU = s < 0.5f ? a0 : a1;
                var o0 = BridgeShell.EdgePoint(e, outerU, 0.15f, BridgeShell.ScreenBottom);
                var o1 = BridgeShell.EdgePoint(e, outerU, 0.15f, BridgeShell.ScreenTop);
                bezel.Quad(o0, P(s, 0.12f, PanelBottom), P(s, 0.12f, PanelTop), o1, n,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 1f, 1f, 0.8f);
                bezel.Quad(P(s, 0.12f, PanelBottom), P(s, 0f, PanelBottom), P(s, 0f, PanelTop), P(s, 0.12f, PanelTop), dir * sgn,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 0.5f, 0.5f, 1f);
                bezel.Quad(o0, o1, BridgeShell.EdgePoint(e, outerU, -0.45f, BridgeShell.ScreenTop),
                    BridgeShell.EdgePoint(e, outerU, -0.45f, BridgeShell.ScreenBottom), -dir * sgn,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.9f, 0.4f, 0.4f);
            }

            // Hood of emitters: a canopy over the screen whose front bulges toward the room, lit underneath.
            const float hoodY = BridgeShell.ScreenTop + 0.06f;
            const float hoodH = 0.14f;
            Vector3 H0(float s, float y) => BridgeShell.EdgePoint(e, a0 - 0.12f + s * (a1 - a0 + 0.24f), 0f, y);
            Vector3 H1(float s, float y)
            {
                var x = s * 2f - 1f;
                return BridgeShell.EdgePoint(e, a0 - 0.12f + s * (a1 - a0 + 0.24f), 0.26f + 0.2f * (1f - x * x), y);
            }

            for (var i = 0; i < Cols; i++)
            {
                var sa = i / (float)Cols;
                var sb = (i + 1f) / Cols;
                metal.Quad(H0(sa, hoodY), H0(sb, hoodY), H1(sb, hoodY), H1(sa, hoodY), Vector3.down,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.6f, 0.6f, 0.9f, 0.9f);
                metal.Quad(H1(sa, hoodY), H1(sb, hoodY), H1(sb, hoodY + hoodH), H1(sa, hoodY + hoodH), n,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.9f, 0.9f, 1f, 1f);
                metal.Quad(H1(sa, hoodY + hoodH), H1(sb, hoodY + hoodH), H0(sb, hoodY + hoodH + 0.05f), H0(sa, hoodY + hoodH + 0.05f), Vector3.up,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 0.7f, 0.7f);
                // Emitter line under the front edge, shining down on the screen.
                var ga = Vector3.Lerp(H0(sa, hoodY - 0.002f), H1(sa, hoodY - 0.002f), 0.82f);
                var gb = Vector3.Lerp(H0(sb, hoodY - 0.002f), H1(sb, hoodY - 0.002f), 0.82f);
                var gc = Vector3.Lerp(H0(sb, hoodY - 0.002f), H1(sb, hoodY - 0.002f), 0.92f);
                var gd = Vector3.Lerp(H0(sa, hoodY - 0.002f), H1(sa, hoodY - 0.002f), 0.92f);
                glow.Quad(ga, gb, gc, gd, Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            }

            foreach (var s in new[] { 0f, 1f })
                metal.Quad(H0(s, hoodY), H1(s, hoodY), H1(s, hoodY + hoodH), H0(s, hoodY + hoodH + 0.05f), s < 0.5f ? dir : -dir,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.7f, 0.9f, 0.9f, 0.7f);

            // Flanking pylons, dado to cornice, with a lit slit.
            foreach (var u in new[] { a0 - 0.42f, a1 + 0.42f })
            {
                const float hw = 0.16f;
                const float y0 = 0.98f;
                const float y1 = BridgeShell.WallTop - 0.02f;
                Vector3 Q(float du, float d, float y) => BridgeShell.EdgePoint(e, u + du, d, y);
                metal.Quad(Q(-hw, 0.2f, y0), Q(hw, 0.2f, y0), Q(hw, 0.2f, y1), Q(-hw, 0.2f, y1), n,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 0.8f, 1f, 1f);
                metal.Quad(Q(hw, 0.2f, y0), Q(hw, 0f, y0), Q(hw, 0f, y1), Q(hw, 0.2f, y1), -dir,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 0.5f, 0.6f, 0.9f);
                metal.Quad(Q(-hw, 0.2f, y0), Q(-hw, 0f, y0), Q(-hw, 0f, y1), Q(-hw, 0.2f, y1), dir,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 0.5f, 0.6f, 0.9f);
                metal.Quad(Q(-hw, 0.2f, y0), Q(hw, 0.2f, y0), Q(hw, 0f, y0 - 0.12f), Q(-hw, 0f, y0 - 0.12f), n + Vector3.down,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 0.8f, 0.8f, 0.5f, 0.5f);
                glow.Quad(Q(-0.022f, 0.202f, y0 + 0.12f), Q(0.022f, 0.202f, y0 + 0.12f), Q(0.022f, 0.202f, y1 - 0.12f),
                    Q(-0.022f, 0.202f, y1 - 0.12f), n, Vector2.zero, Vector2.right, Vector2.one, Vector2.up, 1f, 1f, 1f, 1f);
            }

            // Status bar: segments under the screen, lit by a chase that runs through a scrolled 1-D texture.
            var status = new ShellMesh();
            const float sy0 = BridgeShell.ScreenBottom - 0.17f;
            const float sy1 = BridgeShell.ScreenBottom - 0.11f;
            for (var i = 0; i < Segments; i++)
            {
                var w = (a1 - a0) / Segments;
                var u = a0 + i * w;
                var uv = new Vector2((i + 0.5f) / Segments, 0.5f);
                status.Quad(BridgeShell.EdgePoint(e, u + w * 0.12f, 0.03f, sy0), BridgeShell.EdgePoint(e, u + w * 0.88f, 0.03f, sy0),
                    BridgeShell.EdgePoint(e, u + w * 0.88f, 0.03f, sy1), BridgeShell.EdgePoint(e, u + w * 0.12f, 0.03f, sy1), n,
                    uv, uv, uv, uv, 1f, 1f, 1f, 1f);
            }

            var dark = _art.Hull(_art.Panel, new Color(0.11f, 0.13f, 0.16f), new Vector2(40f, 40f), seam: 0f, lift: 0.04f);
            var hull = _art.Hull(_art.Panel, new Color(0.34f, 0.38f, 0.44f), new Vector2(0.8f, 40f), seam: 0.5f, lift: 0.05f);
            Part("ViewscreenBezel", bezel.ToMesh("SU_ViewscreenBezel"), dark);
            Part("ViewscreenHood", metal.ToMesh("SU_ViewscreenHood"), hull);
            Part("ViewscreenLights", glow.ToMesh("SU_ViewscreenLights"), _art.Lit(Texture2D.whiteTexture, new Color(0.75f, 0.92f, 1f), 2.6f));
            _statusMat = new Material(Shader.Find("SU/UnlitEmissive")) { name = "SU_ViewscreenStatus", mainTexture = ChaseTexture() };
            _statusMat.SetColor("_Emission", Color.black);
            Part("ViewscreenStatus", status.ToMesh("SU_ViewscreenStatus"), _statusMat);
            _ = room;
        }

        void Part(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>1-D light chase: a dim floor, a bright head and a fading tail (wraps).</summary>
        static Texture2D ChaseTexture()
        {
            var tex = new Texture2D(128, 1, TextureFormat.RGBA32, false) { name = "SU_ViewscreenChase", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            for (var i = 0; i < tex.width; i++)
            {
                var t = i / (float)tex.width;
                var head = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(t - 0.2f) * 14f), 2f);
                var tail = t < 0.2f && t > 0.02f ? Mathf.Pow((t - 0.02f) / 0.18f, 3f) * 0.6f : 0f;
                var v = Mathf.Clamp01(0.22f + head + tail);
                tex.SetPixel(i, 0, new Color(v, v, v, 1f));
            }

            tex.Apply(false, true);
            return tex;
        }

        // ── HUD (drawn by the hull camera into the feed) ─────────────────────────

        void BuildHud()
        {
            var canvasGo = new GameObject("ViewscreenHud", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(_cam.transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _cam.Camera;
            var rt = canvasGo.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(W, H);
            canvasGo.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            _hud = canvasGo.transform;
            FitHud();

            // Bands.
            Bar(_hud, "TopBand", new Vector2(0f, H * 0.5f - 22f), new Vector2(W, 44f), new Color(0.01f, 0.03f, 0.05f, 0.62f));
            Bar(_hud, "TopRule", new Vector2(0f, H * 0.5f - 45f), new Vector2(W - 40f, 2f), new Color(1f, 1f, 1f, 0.35f));
            Bar(_hud, "BottomBand", new Vector2(0f, -H * 0.5f + 18f), new Vector2(W, 36f), new Color(0.01f, 0.03f, 0.05f, 0.55f));
            _mode = Text(_hud, new Vector2(-W * 0.5f + 250f, H * 0.5f - 22f), new Vector2(380f, 40f), 28f, TextAlignmentOptions.Left, true);
            _system = Text(_hud, new Vector2(0f, H * 0.5f - 22f), new Vector2(420f, 40f), 30f, TextAlignmentOptions.Center, true);
            _clock = Text(_hud, new Vector2(W * 0.5f - 250f, H * 0.5f - 22f), new Vector2(380f, 40f), 22f, TextAlignmentOptions.Right, false);
            _status = Text(_hud, new Vector2(0f, -H * 0.5f + 18f), new Vector2(W - 160f, 34f), 23f, TextAlignmentOptions.Center, false);

            // Card on the right (target card).
            var card = Bar(_hud, "Card", new Vector2(W * 0.5f - 175f, -12f), new Vector2(310f, 250f), new Color(0.01f, 0.03f, 0.05f, 0.6f));
            _cardAccent = Bar(card.transform, "Accent", new Vector2(-153f, 0f), new Vector2(4f, 250f), Color.white);
            _cardTitle = Text(card.transform, new Vector2(4f, 100f), new Vector2(280f, 36f), 26f, TextAlignmentOptions.Left, true);
            _cardBody = Text(card.transform, new Vector2(4f, -12f), new Vector2(280f, 180f), 20f, TextAlignmentOptions.TopLeft, false);
            _cardBody.lineSpacing = 8f;
            _barBack = Bar(card.transform, "BarBack", new Vector2(4f, -108f), new Vector2(276f, 8f), new Color(1f, 1f, 1f, 0.15f));
            _barFill = Bar(_barBack.transform, "BarFill", Vector2.zero, new Vector2(276f, 8f), Color.white);
            var fr = _barFill.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(0f, 1f);
            fr.pivot = new Vector2(0f, 0.5f);
            fr.anchoredPosition = Vector2.zero;
            _barBack.gameObject.SetActive(false);

            // Frame corners and a centre reticle for the forward view.
            _corners = new Image[8];
            var k = 0;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sy in new[] { -1f, 1f })
            {
                var c = new Vector2(sx * (W * 0.5f - 14f), sy * (H * 0.5f - 60f));
                _corners[k++] = Bar(_hud, "CornerH", c + new Vector2(-sx * 36f, 0f), new Vector2(72f, 4f), Color.white);
                _corners[k++] = Bar(_hud, "CornerV", c + new Vector2(0f, -sy * 26f), new Vector2(4f, 52f), Color.white);
            }

            _reticle = new Image[4];
            for (var i = 0; i < 4; i++)
            {
                var d = Quaternion.Euler(0f, 0f, i * 90f) * Vector3.right;
                _reticle[i] = Bar(_hud, "Reticle", (Vector2)(d * 30f), i % 2 == 0 ? new Vector2(22f, 2f) : new Vector2(2f, 22f),
                    new Color(1f, 1f, 1f, 0.5f));
            }

            // Faint thirds grid and a bearing tape under the top band: this is a display, not a window.
            foreach (var x in new[] { -W / 6f, W / 6f })
                Bar(_hud, "Grid", new Vector2(x, -10f), new Vector2(1.5f, H - 110f), new Color(1f, 1f, 1f, 0.07f));
            foreach (var y in new[] { -H / 6f + 4f, H / 6f - 24f })
                Bar(_hud, "Grid", new Vector2(0f, y), new Vector2(W - 120f, 1.5f), new Color(1f, 1f, 1f, 0.07f));
            for (var i = 0; i < 40; i++)
                _ticks.Add(Bar(_hud, "Tick", Vector2.zero, new Vector2(2f, 8f), new Color(1f, 1f, 1f, 0.55f)).rectTransform);
            for (var i = 0; i < 10; i++)
            {
                var l = Text(_hud, Vector2.zero, new Vector2(60f, 20f), 13f, TextAlignmentOptions.Center, false);
                l.color = new Color(1f, 1f, 1f, 0.6f);
                _tickLabels.Add(l);
            }

            for (var i = 0; i < MaxBrackets; i++)
                _brackets.Add(NewBracket());
            SetLayer(_hud, ViewscreenCamera.HudLayer);
        }

        /// <summary>Keep the HUD plane exactly filling the hull camera's frustum 1 m ahead (the FOV zooms).</summary>
        void FitHud()
        {
            var cam = _cam.Camera;
            var h = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            _hud.localPosition = new Vector3(0f, 0f, 1f);
            _hud.localRotation = Quaternion.identity;
            _hud.localScale = new Vector3(h * cam.aspect / W, h / H, 1f);
        }

        Bracket NewBracket()
        {
            var root = new GameObject("Bracket", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(_hud, false);
            var b = new Bracket { Root = root, Lines = new Image[8] };
            for (var i = 0; i < 8; i++)
                b.Lines[i] = Bar(root, "Line", Vector2.zero, Vector2.one, Color.white);
            b.Label = Text(root, Vector2.zero, new Vector2(260f, 24f), 16f, TextAlignmentOptions.Center, false);
            root.gameObject.SetActive(false);
            return b;
        }

        static Image Bar(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static TMP_Text Text(Transform parent, Vector2 pos, Vector2 size, float fontSize, TextAlignmentOptions align, bool bold)
        {
            var t = DiegeticUi.HoloLabel(parent, string.Empty, pos, size, fontSize, UiKit.TextBright, align);
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.enableWordWrapping = align == TextAlignmentOptions.TopLeft;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t)
                SetLayer(c, layer);
        }

        // ── Per frame ────────────────────────────────────────────────────────────

        void Update()
        {
            var main = Camera.main;
            if (Time.unscaledTime >= _nextCullFix && main != null)
            {
                // The player's eyes never see the HUD plane out on the hull (it would show through the bow windows).
                main.cullingMask &= ~(1 << ViewscreenCamera.HudLayer);
                _nextCullFix = Time.unscaledTime + 1f;
            }

            _cam.Active = main != null && !DiplomacyRoom.AnyRoomInside &&
                          Vector3.Dot(main.transform.forward, (transform.parent.TransformPoint(_screenCentre) - main.transform.position).normalized) > 0.15f;
            if (!_cam.Active)
                return;

            if (_targetsDirty)
                RebuildTargets();
            if (Time.unscaledTime >= _nextEval)
            {
                _nextEval = Time.unscaledTime + 0.5f;
                Evaluate();
            }

            Gaze(main);
            TickDirection();
            _switch = Mathf.MoveTowards(_switch, 0f, Time.unscaledDeltaTime * 2.2f);
            var pulse = _current == Mode.RedAlert ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 4f) : 1f;
            _screenMat.SetFloat("_Switch", _switch);
            _screenMat.SetColor("_Edge", _accent * pulse);
            _screenMat.SetColor("_Tint", _current == Mode.RedAlert ? new Color(1f, 0.82f, 0.8f, 1f) : Color.white);
            if (_statusMat != null)
            {
                var speed = _current == Mode.RedAlert ? 1.4f : _current == Mode.Transit ? 0.6f : 0.25f;
                _statusMat.mainTextureOffset = new Vector2(-Time.unscaledTime * speed, 0f);
                _statusMat.SetColor("_Color", (_current == Mode.RedAlert ? Red : _accent) * (0.8f + 0.2f * pulse));
            }
        }

        void LateUpdate()
        {
            if (!_cam.RendersThisFrame)
                return;
            FitHud();
            PlaceBrackets();
            PlaceTape();
        }

        /// <summary>Bearing of the hull camera from the ship's bow, as a tape of ticks (every 5°, labels every 15°).</summary>
        void PlaceTape()
        {
            var cam = _cam.Camera;
            var bow = transform.parent.forward;
            var look = Vector3.ProjectOnPlane(cam.transform.forward, transform.parent.up);
            var yaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(bow, transform.parent.up), look, transform.parent.up);
            var hfov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect);
            var step = hfov > 30f ? 5f : hfov > 12f ? 2f : hfov > 5f ? 1f : 0.5f;
            var ppd = W / hfov;
            var first = Mathf.Floor((yaw - hfov * 0.5f) / step) * step;
            var y = H * 0.5f - 56f;
            var label = 0;
            for (var i = 0; i < _ticks.Count; i++)
            {
                var a = first + i * step;
                var x = (a - yaw) * ppd;
                var t = _ticks[i];
                var show = Mathf.Abs(x) < W * 0.5f - 90f;
                if (t.gameObject.activeSelf != show)
                    t.gameObject.SetActive(show);
                if (!show)
                    continue;
                var major = Mathf.Abs(Mathf.Repeat(a, step * 3f)) < step * 0.5f;
                t.anchoredPosition = new Vector2(x, y - (major ? 5f : 3f));
                t.sizeDelta = new Vector2(2f, major ? 12f : 6f);
                if (!major || label >= _tickLabels.Count)
                    continue;
                var l = _tickLabels[label++];
                l.gameObject.SetActive(true);
                l.rectTransform.anchoredPosition = new Vector2(x, y - 22f);
                var deg = Mathf.RoundToInt(Mathf.Repeat(a, 360f) * 10f) / 10f;
                Set(l, deg.ToString(step < 1f ? "000.0" : "000", System.Globalization.CultureInfo.InvariantCulture));
            }

            for (var i = label; i < _tickLabels.Count; i++)
                if (_tickLabels[i].gameObject.activeSelf)
                    _tickLabels[i].gameObject.SetActive(false);
        }

        // ── Targets ──────────────────────────────────────────────────────────────

        void RebuildTargets()
        {
            _targetsDirty = false;
            _targets.Clear();
            if (_focus == null || !_focus.HasSystem || _exterior == null)
                return;
            _targets.Add(new Target
            {
                Key = 0, Kind = 0, T = _exterior.transform, Radius = WorldScale.StarRadius,
                Name = Trans.Get(StarKey(_focus.SystemTypeKey)),
                Tint = new Color(1f, 0.86f, 0.55f, 1f)
            });
            foreach (var p in _focus.Planets)
            {
                if (!_exterior.TryGetPlanet(p.Id, out var t))
                    continue;
                var stance = DiplomacyIndex.Resolve(p.UserId);
                _targets.Add(new Target
                {
                    Key = 1_000_000 + p.Id, Kind = 1, Id = p.Id, T = t, Radius = WorldScale.PlanetRadius(p.Slot),
                    Name = string.IsNullOrEmpty(p.Name) ? "#" + p.Id : p.Name,
                    Tint = p.UserId > 0 ? DiplomacyIndex.Tint(stance) : new Color(0.8f, 0.86f, 0.9f, 1f)
                });
            }

            var now = UnixNow();
            foreach (var f in _focus.Fleets)
            {
                if (f.Id == _focus.ViewFleetId || !_focus.VisibleInFocus(f, now) || !_exterior.TryGetFleet(f.Id, out var t))
                    continue;
                _targets.Add(new Target
                {
                    Key = 2_000_000 + f.Id, Kind = 2, Id = f.Id, T = t, Radius = WorldScale.ShipSpan * 0.5f,
                    Name = string.IsNullOrEmpty(f.Name) ? "#" + f.Id : f.Name,
                    Tint = DiplomacyIndex.Tint(DiplomacyIndex.ResolveFleet(f))
                });
            }

            AddAsteroids();
            // Anomalies have no body out there: a card only (the optics stay on the bow).
            if (AnomalyService.Instance != null)
                foreach (var an in AnomalyService.Instance.For(_focus.SystemId))
                    _targets.Add(new Target
                    {
                        Key = 4_000_000 + an.Id, Kind = 4, Id = an.Id, T = null, Radius = 1f, Name = Trans.Get(an.NameKey),
                        Tint = new Color(0.72f, 0.64f, 1f, 1f)
                    });
        }

        void AddAsteroids()
        {
            foreach (var a in _focus.Asteroids)
            {
                if (a.Gone || !_exterior.TryGetAsteroid(a.Id, out var t))
                    continue;
                _targets.Add(new Target
                {
                    Key = 3_000_000 + a.Id, Kind = 3, Id = a.Id, T = t, Radius = Mathf.Max(WorldScale.AsteroidRadius * 2f, t.lossyScale.x),
                    Name = Trans.Get("asteroidField") + " " + a.Slot, Tint = new Color(0.85f, 0.76f, 0.6f, 1f)
                });
            }
        }

        static int KeyOf(HoloToken token) => token == null ? -1 : token.Kind switch
        {
            HoloTokenKind.Planet => 1_000_000 + token.Id,
            HoloTokenKind.Fleet => 2_000_000 + token.Id,
            HoloTokenKind.Asteroid => 3_000_000 + token.Id,
            HoloTokenKind.Anomaly => 4_000_000 + token.Id,
            _ => -1
        };

        Target FindTarget(int key)
        {
            foreach (var t in _targets)
                if (t.Key == key)
                    return t;
            return null;
        }

        // ── What to show ─────────────────────────────────────────────────────────

        void Evaluate()
        {
            if (_focus == null)
                return;
            var now = UnixNow();
            var fleet = _focus.FindViewFleet();
            var station = fleet == null;
            _accent = station ? CicArtKit.Amber : CicArtKit.Cyan;
            var mode = Mode.Survey;
            Target subject = null;
            string title;
            var body = new System.Text.StringBuilder();
            float? bar = null;
            var intent = false;

            if (AlertHere(out var foe, out var hostiles, out var battleLine, out var hull))
            {
                mode = Mode.RedAlert;
                subject = foe;
                title = Trans.Get("vr.screen.redAlert");
                if (!string.IsNullOrEmpty(battleLine))
                    body.AppendLine(battleLine);
                body.AppendLine(Trans.Format("vr.screen.hostiles", hostiles));
                if (hull.HasValue)
                {
                    bar = hull.Value.x / Mathf.Max(1f, hull.Value.y);
                    body.AppendLine(Trans.Format("vr.screen.hull", (int)hull.Value.x, (int)hull.Value.y));
                }
            }
            else if (TrackedKey(out var key, out var preview) && (subject = FindTarget(key)) != null)
            {
                intent = true;
                // What the captain touches on the holo table (or looked at on the screen): framed, with its card
                // and, with a ship selected, the order it would get.
                mode = Mode.Tracking;
                title = subject.Name;
                Describe(subject, body, now);
                if (!string.IsNullOrEmpty(preview))
                    body.AppendLine().Append("<color=#7dffa0>").Append(Trans.Get("vr.screen.order")).Append("</color>  ").Append(preview);
            }
            else if (fleet != null && fleet.IsMoving(now))
            {
                // Under way: the destination if it is in this system, else straight ahead.
                mode = Mode.Transit;
                subject = ShipObjective(fleet);
                title = subject != null ? subject.Name : Trans.Get("vr.screen.transit");
                if (!_transitSeen.TryGetValue(fleet.Id, out var seen) || seen <= 0 || seen >= fleet.DestTime)
                    _transitSeen[fleet.Id] = seen = now;
                if (fleet.DestSystemId > 0 && fleet.DestSystemId != _focus.SystemId)
                    body.AppendLine(Trans.Format("vr.screen.destination", GalaxyCatalog.Label(fleet.DestSystemId)));
                body.AppendLine(Trans.Format("vr.travel.eta", TravelPlanner.TimeText(fleet.DestTime - now)));
                if (subject != null)
                    Describe(subject, body, now);
                bar = Mathf.Clamp01((now - seen) / (float)Mathf.Max(1, fleet.DestTime - seen));
            }
            else if (fleet != null && (fleet.IsHarvesting(now) || fleet.IsSieging(now) || fleet.IsExploring(now)) &&
                     (subject = ShipObjective(fleet)) != null)
            {
                // Working a target (mining, siege, survey): keep it framed with its state.
                mode = Mode.Tracking;
                title = subject.Name;
                body.AppendLine(Trans.Get(FleetOrderGate.BusyKey(fleet)));
                var end = fleet.IsHarvesting(now) ? fleet.HarvestEndTime : fleet.IsSieging(now) ? fleet.AttackEndTime : fleet.ExploreEndTime;
                body.AppendLine(Trans.Format("vr.screen.remaining", TravelPlanner.TimeText(end - now)));
                Describe(subject, body, now);
            }
            else
            {
                // Idle: a station watches the world below; a ship looks ahead, with the system card.
                title = station ? Trans.Get("vr.view.stationHeader") : Trans.Get("vr.screen.survey");
                subject = station ? FindTarget(1_000_000 + _focus.ViewPlanetId) : null;
                if (station && subject != null)
                    Describe(subject, body, now);
                else
                    Survey(body, now);
            }

            // Live direction: a salvo, a kill, a bombardment, a contact or our jump takes the picture for a few
            // seconds — never over what the captain is pointing at (the card stays on his target).
            if (DirectedShot(intent, out var shot))
                subject = shot;
            ShowCaption();

            if (mode != _current)
                CicCue.Hover(transform.parent.TransformPoint(_screenCentre));
            _current = mode;
            var subjectKey = subject?.Key ?? -1;
            if (subjectKey != _subjectKey)
            {
                _subjectKey = subjectKey;
                _switch = 1f;
            }

            _subject = subject;
            _cam.Track(subject?.T, subject?.Radius ?? 1f);

            var tint = mode == Mode.RedAlert ? Red : _accent;
            Set(_mode, mode switch
            {
                Mode.RedAlert => Trans.Get("vr.screen.redAlert"),
                Mode.Tracking => Trans.Get("vr.screen.tracking"),
                Mode.Transit => Trans.Get("vr.screen.transit"),
                _ => subject == null ? Trans.Get("vr.screen.forward") : Trans.Get("vr.screen.scope")
            });
            _mode.color = tint;
            Set(_system, _focus.HasSystem ? SystemLabel(_focus) : Trans.Get("Loading"));
            var clock = System.DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            var unread = CommsService.Instance != null ? CommsService.Instance.Total : 0;
            Set(_clock, unread > 0 ? Trans.Format("vr.screen.comms", unread) + "   " + clock : clock);
            _clock.color = unread > 0 ? CicArtKit.Amber : UiKit.TextDim;
            Set(_status, StatusLine(fleet));
            Set(_cardTitle, title);
            _cardTitle.color = tint;
            Set(_cardBody, body.ToString().TrimEnd());
            _cardAccent.color = tint;
            _barBack.gameObject.SetActive(bar.HasValue);
            if (bar.HasValue)
            {
                _barFill.rectTransform.sizeDelta = new Vector2(276f * Mathf.Clamp01(bar.Value), 8f);
                _barFill.color = tint;
            }

            foreach (var c in _corners)
                c.color = tint;
            foreach (var r in _reticle)
                r.gameObject.SetActive(subject == null);
        }

        /// <summary>A battle on the table, an engagement, or a siege in this system.</summary>
        bool AlertHere(out Target foe, out int hostiles, out string battleLine, out Vector2? hull)
        {
            foe = null;
            hostiles = 0;
            battleLine = null;
            hull = null;
            var engaged = CombatEvents.IsEngaged;
            if (_hex != null && _hex.IsActive && _hex.State != null)
            {
                var s = _hex.State;
                engaged = true;
                battleLine = Trans.Format("vr.battle.round", s.Round);
                foreach (var ship in s.Ships)
                {
                    if (!ship.Alive)
                        continue;
                    if (ship.IsMine && !hull.HasValue)
                        hull = new Vector2(ship.Hp, ship.MaxHp);
                    if (s.MyTeam >= 0 && ship.Team != s.MyTeam)
                    {
                        hostiles++;
                        foe ??= FindTarget(2_000_000 + ship.FleetId);
                    }
                }
            }

            var siege = false;
            if (SiegeWatch.Instance != null && _focus.HasSystem)
                foreach (var sg in SiegeWatch.Instance.Sieges)
                    if (sg.SystemId == _focus.SystemId && (sg.OurPlanet || sg.OurAttack))
                    {
                        siege = true;
                        foe ??= FindTarget(1_000_000 + sg.PlanetId);
                    }

            if (!engaged && !siege)
                return false;
            if (hostiles == 0)
                foreach (var f in _focus.Fleets)
                {
                    var st = DiplomacyIndex.ResolveFleet(f);
                    if (st is not (EmpireStance.Enemy or EmpireStance.Pirate) || !_focus.VisibleInFocus(f, UnixNow()))
                        continue;
                    hostiles++;
                    foe ??= FindTarget(2_000_000 + f.Id);
                }

            return true;
        }

        /// <summary>
        /// What the captain is pointing at on the holo table — the aimed destination of a selected ship first, then
        /// any token under the pointer, then the selected ship — kept for a few seconds after the pointer leaves so
        /// the screen does not flicker; then a gaze lock on a screen bracket.
        /// </summary>
        bool TrackedKey(out int key, out string preview)
        {
            key = -1;
            preview = null;
            CaptureTable();

            if (_tableKey >= 0 && Time.unscaledTime < _tableUntil && FindTarget(_tableKey) != null)
            {
                key = _tableKey;
                preview = _tablePreview;
                return true;
            }

            if (_gazeKey != 0 && Time.unscaledTime < _gazeUntil && FindTarget(_gazeKey) != null)
            {
                key = _gazeKey;
                return true;
            }

            return false;
        }

        /// <summary>The body our ship is heading for or working on in this system (planet, then asteroid).</summary>
        Target ShipObjective(FocusFleet fleet)
        {
            if (fleet.DestSystemId > 0 && fleet.DestSystemId != _focus.SystemId && fleet.IsMoving(UnixNow()))
                return null;
            if (fleet.AsteroidId > 0 && FindTarget(3_000_000 + fleet.AsteroidId) is { } rock)
                return rock;
            return fleet.PlanetId > 0 ? FindTarget(1_000_000 + fleet.PlanetId) : null;
        }

        void Describe(Target t, System.Text.StringBuilder body, long now)
        {
            switch (t.Kind)
            {
                case 0:
                    body.AppendLine(t.Name);
                    body.AppendLine(Trans.Get("planets") + "  " + _focus.Planets.Count);
                    break;
                case 1:
                {
                    var p = _focus.FindPlanet(t.Id);
                    if (p == null)
                        break;
                    body.AppendLine(Owner(p.UserId));
                    body.AppendLine(Trans.Format("vr.screen.orbit", p.Slot));
                    if (p.Habitability > 0)
                        body.AppendLine(Trans.Get("habitability") + "  " + p.Habitability + "%");
                    if (SiegeWatch.Instance != null && SiegeWatch.Instance.TryGet(p.Id, out _))
                        body.AppendLine(Trans.Get("vr.screen.underSiege"));
                    var ours = 0;
                    foreach (var f in _focus.Fleets)
                        if (f.PlanetId == p.Id && _focus.IsMine(f) && !f.IsMoving(now))
                            ours++;
                    if (ours > 0)
                        body.AppendLine(Trans.Format("vr.screen.oursHere", ours));
                    break;
                }
                case 4:
                {
                    var an = AnomalyService.Instance != null ? AnomalyService.Instance.Find(t.Id) : null;
                    if (an != null)
                        body.AppendLine(Trans.Format("vr.anomaly.preview", an.Research, an.Minerals, an.Crystals, an.Difficulty));
                    break;
                }
                case 3:
                {
                    var reserves = AsteroidService.Reserves(_focus.FindAsteroid(t.Id));
                    if (!string.IsNullOrEmpty(reserves))
                        body.AppendLine(reserves);
                    break;
                }
                case 2:
                {
                    var f = _focus.FindFleet(t.Id);
                    if (f == null)
                        break;
                    body.AppendLine(Owner(f.UserId, f.IsPirate));
                    body.AppendLine(FleetOrderGate.CanMove(f) ? Trans.Get("vr.screen.idle") : Trans.Get(FleetOrderGate.BusyKey(f)));
                    if (f.IsMoving(now))
                        body.AppendLine(Trans.Format("vr.travel.eta", TravelPlanner.TimeText(f.DestTime - now)));
                    break;
                }
            }
        }

        void Survey(System.Text.StringBuilder body, long now)
        {
            if (!_focus.HasSystem)
                return;
            body.AppendLine(Trans.Get(StarKey(_focus.SystemTypeKey)));
            var mine = 0;
            var me = FocusContext.OwnedUserId();
            foreach (var p in _focus.Planets)
                if (me > 0 && p.UserId == me)
                    mine++;
            body.AppendLine(Trans.Get("planets") + "  " + _focus.Planets.Count + (mine > 0 ? "  ·  " + Trans.Format("vr.screen.ours", mine) : string.Empty));
            var contacts = 0;
            var hostile = 0;
            foreach (var f in _focus.Fleets)
            {
                if (f.Id == _focus.ViewFleetId || !_focus.VisibleInFocus(f, now) || _focus.IsMine(f))
                    continue;
                contacts++;
                if (DiplomacyIndex.ResolveFleet(f) is EmpireStance.Enemy or EmpireStance.Pirate)
                    hostile++;
            }

            body.AppendLine(Trans.Format("vr.screen.contacts", contacts));
            if (hostile > 0)
                body.AppendLine(Trans.Format("vr.screen.hostiles", hostile));
            if (_focus.Asteroids.Count > 0)
                body.AppendLine(Trans.Get("asteroidField") + "  " + _focus.Asteroids.Count);
        }

        static string Owner(int userId, bool pirate = false)
        {
            if (pirate)
                return Trans.Get("vr.screen.pirate");
            if (userId <= 0)
                return Trans.Get("vr.screen.unclaimed");
            if (userId == FocusContext.OwnedUserId())
                return Trans.Get("vr.screen.ours1");
            var stance = DiplomacyIndex.Resolve(userId);
            var who = DiplomacyIndex.TryIdentity(userId, out var name, out _) && !string.IsNullOrEmpty(name) ? name : "#" + userId;
            var rel = stance switch
            {
                EmpireStance.Ally => Trans.Get("relation_ally"),
                EmpireStance.Enemy => Trans.Get("relation_enemy"),
                _ => Trans.Get("relation_neutral")
            };
            return "<noparse>" + who + "</noparse>  ·  " + rel;
        }

        string StatusLine(FocusFleet fleet)
        {
            if (fleet == null)
                return Trans.Format("vr.view.orbiting", StationPlanetName(_focus));
            var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
            var state = FleetOrderGate.CanMove(fleet) ? Trans.Get("vr.crew.standby") : Trans.Get(FleetOrderGate.BusyKey(fleet));
            return "<noparse>" + name + "</noparse>  ·  " + state;
        }

        static void Set(TMP_Text t, string s)
        {
            if (t != null && t.text != s)
                t.text = s;
        }

        // ── Markers ──────────────────────────────────────────────────────────────

        void PlaceBrackets()
        {
            var cam = _cam.Camera;
            var tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            var used = 0;
            foreach (var t in _targets)
            {
                if (used >= _brackets.Count)
                    break;
                if (t.T == null || !t.T.gameObject.activeInHierarchy)
                    continue;
                var vp = cam.WorldToViewportPoint(t.T.position);
                if (vp.z <= 0.5f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.12f || vp.y > 0.86f)
                    continue;
                var b = _brackets[used++];
                var r = t.Radius / (vp.z * tanHalf) * (H * 0.5f);
                var locked = t.Key == _subjectKey;
                var gazing = t.Key == _gazeCandidate && _gazeDwell > 0.15f;
                var size = Mathf.Clamp(r * 2.3f, 26f, 300f) * (gazing ? 1f + 0.12f * (_gazeDwell / GazeLock) : 1f);
                b.Root.gameObject.SetActive(true);
                b.Root.anchoredPosition = new Vector2((vp.x - 0.5f) * W, (vp.y - 0.5f) * H);
                var color = locked ? Color.Lerp(t.Tint, Color.white, 0.35f) : t.Tint;
                if (Mathf.Abs(size - b.Size) > 1f || b.Key != t.Key)
                    Shape(b, size, locked ? 4f : 2f);
                foreach (var l in b.Lines)
                    l.color = color;
                if (b.Key != t.Key)
                {
                    b.Key = t.Key;
                    b.Label.text = "<noparse>" + t.Name + "</noparse>";
                }

                b.Label.color = color;
                b.Label.rectTransform.anchoredPosition = new Vector2(0f, -size * 0.5f - 14f);
            }

            for (var i = used; i < _brackets.Count; i++)
                if (_brackets[i].Root.gameObject.activeSelf)
                {
                    _brackets[i].Root.gameObject.SetActive(false);
                    _brackets[i].Key = -1;
                }
        }

        static void Shape(Bracket b, float size, float thick)
        {
            b.Size = size;
            var h = size * 0.5f;
            var arm = Mathf.Max(8f, size * 0.26f);
            var k = 0;
            foreach (var sx in new[] { -1f, 1f })
            foreach (var sy in new[] { -1f, 1f })
            {
                var c = new Vector2(sx * h, sy * h);
                var hl = b.Lines[k++].rectTransform;
                hl.sizeDelta = new Vector2(arm, thick);
                hl.anchoredPosition = c + new Vector2(-sx * arm * 0.5f, -sy * thick * 0.5f);
                var vl = b.Lines[k++].rectTransform;
                vl.sizeDelta = new Vector2(thick, arm);
                vl.anchoredPosition = c + new Vector2(-sx * thick * 0.5f, -sy * arm * 0.5f);
            }
        }

        /// <summary>Look at a bracket on the screen for a moment and the optics lock onto it.</summary>
        void Gaze(Camera main)
        {
            var room = transform.parent;
            var centre = room.TransformPoint(_screenCentre);
            var normal = room.TransformDirection(_screenNormal);
            var ray = new Ray(main.transform.position, main.transform.forward);
            var denom = Vector3.Dot(ray.direction, normal);
            var candidate = 0;
            if (denom < -1e-3f)
            {
                var hit = ray.GetPoint(Vector3.Dot(centre - ray.origin, normal) / denom) - centre;
                var gx = Vector3.Dot(hit, room.TransformDirection(_screenRight)) / BridgeShell.ScreenWidth * W;
                var gy = Vector3.Dot(hit, room.TransformDirection(_screenUp)) / (BridgeShell.ScreenTop - BridgeShell.ScreenBottom) * H;
                var best = 120f * 120f;
                foreach (var b in _brackets)
                {
                    if (!b.Root.gameObject.activeSelf || b.Key <= 0)
                        continue;
                    var d = (b.Root.anchoredPosition - new Vector2(gx, gy)).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        candidate = b.Key;
                    }
                }
            }

            if (candidate == 0 || candidate == _subjectKey)
            {
                _gazeCandidate = 0;
                _gazeDwell = 0f;
                return;
            }

            if (candidate != _gazeCandidate)
            {
                _gazeCandidate = candidate;
                _gazeDwell = 0f;
            }

            _gazeDwell += Time.unscaledDeltaTime;
            if (_gazeDwell < GazeLock)
                return;
            _gazeKey = candidate;
            _gazeUntil = Time.unscaledTime + GazeHold;
            _gazeCandidate = 0;
            _gazeDwell = 0f;
            _nextEval = 0f;
            CicCue.Ok(centre);
        }

        /// <summary>Native star label key: systems.type "yellow" → "yellowStar".</summary>
        static string StarKey(string type) =>
            string.IsNullOrEmpty(type) ? "yellowStar" : type.EndsWith("Star") ? type : type + "Star";

        static long UnixNow() => System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
