using System;
using System.Collections.Generic;
using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Core.Vfx
{
    /// <summary>
    /// The watch's holo cluster in the player's real room: smoked glass ~46 cm wide, just under the eye line,
    /// in front of the chest. The main affair on top (what, where, the countdown, a progress bar) with a small
    /// hologram over the glass — the two systems of a jump joined by an arc the ship's pip crawls along, or a
    /// gauge ring for a timer — then up to three other affairs, the crew's last line, and "Aboard". Follows the
    /// player lazily (walk away or turn round and it drifts back in front), never head-locked.
    /// Everything is on the Watch layer, the only one the camera renders during the watch.
    /// </summary>
    public sealed class WatchCluster : MonoBehaviour
    {
        const float Distance = 0.55f;
        const float BelowEye = 0.28f;
        const int ArcPoints = 24;
        static readonly Vector2 Px = new(460f, 330f);

        Action _onBoard;
        int _layer;
        Transform _head;
        TMP_Text _kind;
        TMP_Text _title;
        TMP_Text _detail;
        TMP_Text _countdown;
        RectTransform _fill;
        readonly TMP_Text[] _others = new TMP_Text[3];
        TMP_Text _toast;
        float _toastUntil;
        Button _board;
        Image _boardImage;
        Action<int> _onBuild;
        int _buildPlanet;
        LineRenderer _arc;
        Transform _from;
        Transform _to;
        Transform _pip;
        Transform _holo;
        WatchAffair _main;
        readonly Vector3[] _pts = new Vector3[ArcPoints];
        Vector3 _goalPos;
        Quaternion _goalRot;
        float _drift;
        static Material _dot;
        static Material _beam;

        /// <param name="onBuild">The Build key: the Ops console on that world (0 = the console's own pick).</param>
        public static WatchCluster Build(Transform deck, int layer, Action onBoard, Action<int> onBuild)
        {
            var go = new GameObject("WatchCluster");
            go.transform.SetParent(deck, false);
            var c = go.AddComponent<WatchCluster>();
            c._onBoard = onBoard;
            c._onBuild = onBuild;
            c._layer = layer;
            c.BuildPanel();
            c.BuildHolo();
            SetLayer(go.transform, layer);
            go.SetActive(false);
            Core.Crew.BarkDirector.Spoke += c.OnCrewLine;
            return c;
        }

        void OnDestroy() => Core.Crew.BarkDirector.Spoke -= OnCrewLine;

        /// <summary>Put the cluster in front of the head, just below the eyes, facing it.</summary>
        public void Place(Transform head)
        {
            _head = head;
            gameObject.SetActive(true);
            Goal();
            transform.SetPositionAndRotation(_goalPos, _goalRot);
        }

        public void Hide() => gameObject.SetActive(false);

        void Goal()
        {
            var f = _head.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f)
                f = Vector3.forward;
            f.Normalize();
            _goalPos = _head.position + f * Distance + Vector3.down * BelowEye;
            // Tilted back toward the eyes, like a lectern.
            var look = _goalPos - _head.position;
            _goalRot = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        void LateUpdate()
        {
            if (_head == null)
                return;
            // Lazy follow: once the player has walked off or turned away for a moment, drift back in front.
            var toCluster = transform.position - _head.position;
            var flat = new Vector3(toCluster.x, 0f, toCluster.z);
            var f = _head.forward;
            f.y = 0f;
            var off = flat.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f ? 0f : Vector3.Angle(flat, f);
            var far = flat.magnitude > Distance * 2.2f || flat.magnitude < Distance * 0.5f;
            _drift = off > 50f || far ? _drift + Time.unscaledDeltaTime : 0f;
            if (_drift > 0.8f)
                Goal();
            var k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 3f);
            if ((transform.position - _goalPos).sqrMagnitude > 1e-6f)
                transform.SetPositionAndRotation(Vector3.Lerp(transform.position, _goalPos, k), Quaternion.Slerp(transform.rotation, _goalRot, k));

            Animate();
            if (_toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil)
                _toast.gameObject.SetActive(false);
        }

        // ── Content ─────────────────────────────────────────────────────────────

        public void Show(IReadOnlyList<WatchAffair> affairs)
        {
            var now = FleetOrderGate.UnixNow();
            _main = affairs.Count > 0 ? affairs[0] : null;
            // The world the Build key opens: the construction on top of the watch, else the first one listed.
            _buildPlanet = 0;
            if (_main != null && _main.Kind == WatchKind.Building)
                _buildPlanet = _main.PlanetId;
            for (var i = 0; _buildPlanet == 0 && i < affairs.Count; i++)
                if (affairs[i].Kind == WatchKind.Building)
                    _buildPlanet = affairs[i].PlanetId;
            if (_main == null)
            {
                _kind.text = Trans.Get("vr.watch.link").ToUpperInvariant();
                _title.text = Trans.Get("vr.watch.idle");
                _detail.text = string.Empty;
                _countdown.text = string.Empty;
                _fill.parent.gameObject.SetActive(false);
            }
            else
            {
                _kind.text = Trans.Get(KindKey(_main.Kind)).ToUpperInvariant();
                _kind.color = _main.YourTurn ? UiKit.Amber : UiKit.Cyan;
                _title.text = _main.Title;
                _detail.text = _main.Detail;
                var timed = _main.End > now;
                _countdown.text = timed ? Core.Holo.TravelPlanner.TimeText(_main.End - now) : string.Empty;
                _fill.parent.gameObject.SetActive(timed && _main.Start > 0);
                _fill.anchorMax = new Vector2(Mathf.Max(0.02f, _main.Progress(now)), 1f);
            }

            for (var i = 0; i < _others.Length; i++)
            {
                var a = i + 1 < affairs.Count ? affairs[i + 1] : null;
                _others[i].gameObject.SetActive(a != null);
                if (a == null)
                    continue;
                var left = a.End > now ? "   " + Core.Holo.TravelPlanner.TimeText(a.End - now) : string.Empty;
                _others[i].text = Trans.Get(KindKey(a.Kind)) + " · " + a.Title + left;
            }

            // Our turn in a battle: the way back aboard lights up.
            _boardImage.color = _main != null && _main.YourTurn ? new Color(1f, 0.75f, 0.35f) : Color.white;
            _holo.gameObject.SetActive(_main != null);
        }

        static string KindKey(WatchKind kind) => kind switch
        {
            WatchKind.Battle => "vr.watch.battle",
            WatchKind.Transit => "vr.watch.transit",
            WatchKind.Siege => "vr.watch.siege",
            WatchKind.Harvest => "vr.watch.harvest",
            WatchKind.Explore => "vr.watch.explore",
            WatchKind.Building => "vr.watch.building",
            WatchKind.Shipyard => "vr.watch.shipyard",
            _ => "vr.watch.research"
        };

        void OnCrewLine(string speaker, Color accent, string line)
        {
            if (!WatchMode.Inside || _toast == null)
                return;
            _toast.text = "<color=#" + ColorUtility.ToHtmlStringRGB(accent) + ">" + speaker + "</color>  " + Escape(line);
            _toast.gameObject.SetActive(true);
            _toastUntil = Time.unscaledTime + Mathf.Clamp(line.Length * 0.07f, 3f, 9f);
        }

        static string Escape(string s) => (s ?? string.Empty).Replace("<", "‹").Replace(">", "›");

        void Animate()
        {
            if (_main == null || !_holo.gameObject.activeSelf)
                return;
            var now = FleetOrderGate.UnixNow();
            var t = Time.unscaledTime;
            if (_main.Kind == WatchKind.Transit)
            {
                // Two systems and the ship's pip along the arc between them.
                _from.gameObject.SetActive(true);
                _to.gameObject.SetActive(true);
                _arc.positionCount = ArcPoints;
                for (var i = 0; i < ArcPoints; i++)
                {
                    var u = i / (ArcPoints - 1f);
                    _pts[i] = new Vector3(Mathf.Lerp(-0.15f, 0.15f, u), Mathf.Sin(u * Mathf.PI) * 0.05f, 0f);
                }

                _arc.SetPositions(_pts);
                var p = _main.Start > 0 ? _main.Progress(now) : 0.5f;
                _pip.localPosition = new Vector3(Mathf.Lerp(-0.15f, 0.15f, p), Mathf.Sin(p * Mathf.PI) * 0.05f, 0f);
                _pip.localScale = Vector3.one * (0.028f * (1f + 0.15f * Mathf.Sin(t * 5f)));
                return;
            }

            // A gauge ring filling toward the end; a battle's ring pulses instead.
            _from.gameObject.SetActive(false);
            _to.gameObject.SetActive(false);
            var fill = _main.Kind == WatchKind.Battle ? 1f : (_main.Start > 0 ? _main.Progress(now) : 1f);
            var n = Mathf.Max(2, Mathf.RoundToInt(ArcPoints * fill));
            _arc.positionCount = n;
            for (var i = 0; i < n; i++)
            {
                var a = Mathf.PI * 0.5f - i / (ArcPoints - 1f) * Mathf.PI * 2f;
                _pts[i] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.055f + Vector3.up * 0.03f;
            }

            _arc.SetPositions(_pts);
            var beat = _main.YourTurn ? 1f + 0.25f * Mathf.Sin(t * 6f) : 1f + 0.08f * Mathf.Sin(t * 2f);
            _pip.localPosition = Vector3.up * 0.03f;
            _pip.localScale = Vector3.one * (0.035f * beat);
        }

        // ── Build ───────────────────────────────────────────────────────────────

        void BuildPanel()
        {
            var canvas = DiegeticUi.WorldCanvas(transform, "WatchCanvas", Px, Vector3.zero, Quaternion.identity, 0.001f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, Px);
            var img = frame.GetComponent<Image>();
            img.raycastTarget = false;
            // Smoked glass: the real room shows through.
            img.color = new Color(0.45f, 0.85f, 1f, 0.55f);

            _kind = Label(frame, new Vector2(0f, 136f), new Vector2(410f, 26f), 17f, UiKit.Cyan, FontStyles.Bold);
            _kind.characterSpacing = 8f;
            _title = Label(frame, new Vector2(0f, 104f), new Vector2(410f, 36f), 27f, UiKit.TextBright, FontStyles.Bold);
            _title.richText = false;
            _detail = Label(frame, new Vector2(0f, 72f), new Vector2(410f, 28f), 19f, UiKit.TextDim, FontStyles.Normal);
            _detail.richText = false;
            _countdown = Label(frame, new Vector2(0f, 30f), new Vector2(410f, 50f), 40f, UiKit.Amber, FontStyles.Bold);

            var bar = new GameObject("Progress", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(frame, false);
            var brt = bar.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(380f, 8f);
            brt.anchoredPosition = new Vector2(0f, -4f);
            var bimg = bar.GetComponent<Image>();
            bimg.color = new Color(0.2f, 0.45f, 0.55f, 0.6f);
            bimg.raycastTarget = false;
            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(bar.transform, false);
            _fill = fill.GetComponent<RectTransform>();
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(0.3f, 1f);
            _fill.offsetMin = Vector2.zero;
            _fill.offsetMax = Vector2.zero;
            var fimg = fill.GetComponent<Image>();
            fimg.color = UiKit.Amber;
            fimg.raycastTarget = false;

            for (var i = 0; i < _others.Length; i++)
            {
                _others[i] = Label(frame, new Vector2(0f, -30f - i * 22f), new Vector2(410f, 22f), 15f, UiKit.TextDim, FontStyles.Normal,
                    TextAlignmentOptions.MidlineLeft);
                _others[i].richText = false;
            }

            _toast = Label(frame, new Vector2(0f, -108f), new Vector2(420f, 34f), 15f, UiKit.TextBright, FontStyles.Italic,
                TextAlignmentOptions.MidlineLeft);
            _toast.textWrappingMode = TextWrappingModes.Normal;
            _toast.enableAutoSizing = true;
            _toast.fontSizeMin = 11f;
            _toast.fontSizeMax = 15f;
            _toast.gameObject.SetActive(false);

            _board = DiegeticUi.HoloButton(frame, Trans.Get("vr.watch.board"), new Vector2(-102f, -146f), new Vector2(190f, 40f),
                () => _onBoard?.Invoke(), DiegeticUi.BtnStyle.Amber);
            _boardImage = _board.GetComponent<Image>();
            // The ray must reach it from afar (trigger), not only a finger touching it.
            Core.UI.RayPress.Add(_board);
            var build = DiegeticUi.HoloButton(frame, Trans.Get("build"), new Vector2(102f, -146f), new Vector2(190f, 40f),
                () => _onBuild?.Invoke(_buildPlanet), DiegeticUi.BtnStyle.Cyan);
            Core.UI.RayPress.Add(build);
        }

        static TMP_Text Label(Transform parent, Vector2 pos, Vector2 size, float fontSize, Color color, FontStyles style,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var t = DiegeticUi.HoloLabel(parent, string.Empty, pos, size, fontSize, color, align);
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        void BuildHolo()
        {
            // Hovering just above the glass.
            _holo = new GameObject("Holo").transform;
            _holo.SetParent(transform, false);
            _holo.localPosition = new Vector3(0f, Px.y * 0.0005f + 0.035f, 0f);

            var line = new GameObject("Arc");
            line.transform.SetParent(_holo, false);
            _arc = line.AddComponent<LineRenderer>();
            _arc.useWorldSpace = false;
            _arc.widthMultiplier = 0.006f;
            _arc.sharedMaterial = BeamMat();
            _arc.startColor = _arc.endColor = new Color(0.4f, 0.9f, 1f, 0.9f);
            _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arc.receiveShadows = false;
            _arc.textureMode = LineTextureMode.Stretch;

            _from = Glow(_holo, "From", new Vector3(-0.15f, 0f, 0f), 0.035f, new Color(0.5f, 0.85f, 1f));
            _to = Glow(_holo, "To", new Vector3(0.15f, 0f, 0f), 0.04f, new Color(1f, 0.8f, 0.45f));
            _pip = Glow(_holo, "Pip", Vector3.zero, 0.028f, Color.white);
        }

        static Transform Glow(Transform parent, string name, Vector3 at, float size, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = DotMat();
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", c);
            r.SetPropertyBlock(block);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.AddComponent<BillboardFace>();
            return go.transform;
        }

        static Mesh _quad;

        static Mesh Quad()
        {
            if (_quad != null)
                return _quad;
            _quad = new Mesh
            {
                name = "SU_WatchQuad",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
                uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
                colors = new[] { Color.white, Color.white, Color.white, Color.white },
                triangles = new[] { 0, 2, 1, 2, 3, 1 }
            };
            _quad.RecalculateBounds();
            return _quad;
        }

        static Material DotMat()
        {
            if (_dot != null)
                return _dot;
            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("SU/UnlitEmissive");
            _dot = new Material(shader) { name = "SU_WatchGlow", mainTexture = CombatFxKit.DotTexture() };
            if (_dot.HasProperty("_EmissionMul"))
                _dot.SetFloat("_EmissionMul", 2f);
            return _dot;
        }

        static Material BeamMat()
        {
            if (_beam != null)
                return _beam;
            var shader = Shader.Find("SU/ParticleGlow") ?? Shader.Find("SU/UnlitEmissive");
            _beam = new Material(shader) { name = "SU_WatchArc", mainTexture = CombatFxKit.BeamTexture() };
            if (_beam.HasProperty("_Color"))
                _beam.SetColor("_Color", Color.white);
            if (_beam.HasProperty("_EmissionMul"))
                _beam.SetFloat("_EmissionMul", 1.6f);
            return _beam;
        }

        static void SetLayer(Transform t, int layer)
        {
            if (layer < 0)
                return;
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++)
                SetLayer(t.GetChild(i), layer);
        }
    }
}
