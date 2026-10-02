using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.App;
using Core.Stations;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// The watch's consoles: a small launcher beside the cluster brings the crew stations into the player's
    /// real room — the six repeaters (Helm course, Tactical contacts, Engineering hold / mining, Science, Comms
    /// traffic, Ops stocks; the bridge's own <see cref="CrewStationScreen"/>) as smoked holo glass, and the
    /// full Comms and Ops consoles themselves, borrowed from the bridge while the watch lasts. Each panel
    /// hangs over a grab bar (grip, near or along the ray) that carries it anywhere, turning it to face the
    /// player as it moves; × puts it away. Poses are kept in the play space (the XR origin, i.e. the real
    /// room), in PlayerPrefs, and the panels open at the next watch where they were left.
    /// Light: nothing exists until spawned; each panel refreshes at its own console's cadence (repeaters 2 s
    /// and on focus events, Comms / Ops as on the bridge); the borrowed consoles' rebuilt rows are moved to
    /// the Watch layer and made ray-pressable by one 4 Hz pass over their hierarchy, only while they are out.
    /// </summary>
    public sealed class WatchConsoles : MonoBehaviour
    {
        const string PrefsKey = "su.watch.consoles.v1";
        const float RepeaterScale = 0.62f;
        const float ConsoleScale = 0.7f;
        const float HousingBorder = 0.022f;
        const float TickEvery = 0.25f;
        const float MaxRestoreDistance = 5f;
        const float BarWidth = 0.16f;
        static readonly Vector2 RepeaterSize = new(0.64f, 0.34f);
        static readonly Vector2 ConsoleSize = new(1.1f, 0.7f);
        static readonly Vector2 LauncherPx = new(150f, 346f);
        static readonly Color Glass = new(0.45f, 0.85f, 1f, 0.6f);
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        enum Kind
        {
            Repeater,
            Comms,
            Ops
        }

        sealed class Def
        {
            public string Id;
            public string TitleKey;
            public Kind Kind;
            public CrewDialogue.Role Role;
            public Color Accent;
            /// <summary>Where a first spawn lands around the player (degrees from the gaze).</summary>
            public float Yaw;
            public Button Key;
        }

        sealed class Saved
        {
            public bool Open;
            public Vector3 Pos;
            public Quaternion Rot;
        }

        sealed class Panel
        {
            public Def Def;
            public Transform Root;
            public Transform Bar;
            public MeshRenderer BarRenderer;
            public XRGrabInteractable Grab;
            public Transform ConsoleHome;
            public LayerKeeper Layers;
        }

        Transform _deck;
        Transform _rig;
        Transform _head;
        FocusContext _focus;
        int _layer;
        float _nextTick;
        readonly List<Def> _defs = new();
        readonly List<Panel> _panels = new();
        readonly Dictionary<string, Saved> _saved = new();
        static MaterialPropertyBlock _block;

        public static WatchConsoles Build(Transform deck, Transform cluster, int layer, FocusContext focus)
        {
            var go = new GameObject("WatchConsoles");
            go.transform.SetParent(deck, false);
            var w = go.AddComponent<WatchConsoles>();
            w._deck = deck;
            w._layer = layer;
            w._focus = focus;
            w.BuildDefs();
            w.BuildLauncher(cluster);
            w.Load();
            return w;
        }

        /// <summary>The watch begins: the panels left open last time come back where they were.</summary>
        public void Restore(Transform rig, Transform head)
        {
            _rig = rig != null ? rig : _deck;
            _head = head;
            foreach (var def in _defs)
                if (_saved.TryGetValue(def.Id, out var s) && s.Open && Find(def) == null && Available(def))
                    Spawn(def);
            RefreshKeys();
        }

        /// <summary>The watch ends: remember the layout, give the consoles back to the bridge.</summary>
        public void Stash()
        {
            for (var i = _panels.Count - 1; i >= 0; i--)
            {
                var p = _panels[i];
                Remember(p, true);
                Despawn(p);
            }

            Save();
            RefreshKeys();
        }

        // ── Launcher ────────────────────────────────────────────────────────────

        void BuildDefs()
        {
            var roles = new[]
            {
                CrewDialogue.Role.Helm, CrewDialogue.Role.Tactical, CrewDialogue.Role.Engineering,
                CrewDialogue.Role.Science, CrewDialogue.Role.Comms, CrewDialogue.Role.Ops
            };
            var yaws = new[] { -38f, 38f, -62f, 62f, -86f, 86f };
            for (var i = 0; i < roles.Length; i++)
            {
                foreach (var st in CrewStationsBuilder.Stations)
                {
                    if (st.Role != roles[i])
                        continue;
                    _defs.Add(new Def
                    {
                        Id = st.Role.ToString().ToLowerInvariant(), TitleKey = st.TitleKey, Kind = Kind.Repeater, Role = st.Role,
                        Accent = st.Accent, Yaw = yaws[i]
                    });
                }
            }

            _defs.Add(new Def
            {
                Id = "commsConsole", TitleKey = "vr.watch.console.comms", Kind = Kind.Comms, Role = CrewDialogue.Role.Comms,
                Accent = new Color(0.55f, 0.85f, 0.45f), Yaw = -28f
            });
            _defs.Add(new Def
            {
                Id = "opsConsole", TitleKey = "vr.watch.console.ops", Kind = Kind.Ops, Role = CrewDialogue.Role.Ops,
                Accent = new Color(0.35f, 0.75f, 1f), Yaw = 28f
            });
        }

        void BuildLauncher(Transform cluster)
        {
            // A narrow column on the cluster's right edge (cluster glass is 46 cm wide), turned toward the eyes.
            var canvas = DiegeticUi.WorldCanvas(cluster, "ConsoleLauncher", LauncherPx, new Vector3(0.318f, 0f, -0.012f),
                Quaternion.Euler(0f, 18f, 0f), 0.001f);
            var frame = DiegeticUi.HoloFrame(canvas.transform, LauncherPx);
            var img = frame.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = Glass;

            var title = DiegeticUi.HoloLabel(frame, Trans.Get("vr.watch.consoles").ToUpperInvariant(), new Vector2(0f, 150f),
                new Vector2(136f, 24f), 14f, UiKit.Cyan);
            title.fontStyle = FontStyles.Bold;
            title.characterSpacing = 4f;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Ellipsis;

            var y = 116f;
            for (var i = 0; i < _defs.Count; i++)
            {
                var def = _defs[i];
                if (i > 0 && def.Kind != Kind.Repeater && _defs[i - 1].Kind == Kind.Repeater)
                    y -= 10f;
                def.Key = DiegeticUi.HoloButton(frame, Trans.Get(def.TitleKey), new Vector2(0f, y), new Vector2(134f, 30f),
                    () => Toggle(def), DiegeticUi.BtnStyle.Ghost);
                var label = def.Key.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.enableAutoSizing = true;
                    label.fontSizeMin = 10f;
                    label.fontSizeMax = 15f;
                    label.textWrappingMode = TextWrappingModes.NoWrap;
                }

                RayPress.Add(def.Key);
                y -= 34f;
            }

            SetLayer(canvas.transform, _layer);
        }

        void RefreshKeys()
        {
            foreach (var def in _defs)
            {
                if (def.Key == null)
                    continue;
                def.Key.interactable = Available(def);
                DiegeticUi.Restyle(def.Key, Find(def) != null ? DiegeticUi.BtnStyle.Cyan : DiegeticUi.BtnStyle.Ghost);
            }
        }

        static bool Available(Def def) => def.Kind switch
        {
            Kind.Comms => CommsConsole.Instance != null,
            Kind.Ops => OpsConsole.Instance != null,
            _ => true
        };

        void Toggle(Def def)
        {
            if (!WatchMode.Inside)
                return;
            var open = Find(def);
            if (open != null)
                Close(open);
            else if (Available(def))
                Spawn(def);
            RefreshKeys();
        }

        Panel Find(Def def)
        {
            foreach (var p in _panels)
                if (p.Def == def)
                    return p;
            return null;
        }

        // ── Panels ──────────────────────────────────────────────────────────────

        void Spawn(Def def)
        {
            var root = new GameObject("WatchConsole_" + def.Id).transform;
            root.SetParent(_deck, false);
            Pose(def, out var pos, out var rot);
            root.SetPositionAndRotation(pos, rot);
            var panel = new Panel { Def = def, Root = root };
            var half = HalfHeight(def);

            // The bar first, then the grab: XRI collects the colliders present at Awake — the bar's only, so
            // a press on the screen's buttons never grabs the panel.
            var barY = -half - 0.032f;
            var bar = UiKit.MeshPiece(root, "GrabBar", UiMeshes.RoundedBox(new Vector3(BarWidth, 0.016f, 0.016f), 0.008f),
                UiKit.Cap, new Vector3(0f, barY, 0f));
            panel.Bar = bar.transform;
            panel.BarRenderer = bar.GetComponent<MeshRenderer>();
            bar.AddComponent<BoxCollider>().size = new Vector3(BarWidth + 0.04f, 0.05f, 0.05f);
            Glow(panel.BarRenderer, def.Accent, 0.9f);

            var body = root.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var grab = root.gameObject.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            grab.throwOnDetach = false;
            grab.trackRotation = false;
            grab.trackScale = false;
            grab.useDynamicAttach = false;
            grab.farAttachMode = InteractableFarAttachMode.Far;
            // Held by the bar: the bar follows the hand (or stays on the ray), the panel pivots about it.
            var attach = new GameObject("Attach").transform;
            attach.SetParent(root, false);
            attach.localPosition = new Vector3(0f, barY, 0f);
            grab.attachTransform = attach;
            grab.hoverEntered.AddListener(_ => Glow(panel.BarRenderer, Color.white, 1.6f));
            grab.hoverExited.AddListener(_ => Glow(panel.BarRenderer, def.Accent, 0.9f));
            grab.selectEntered.AddListener(_ => CicCue.Ok(root.position));
            grab.selectExited.AddListener(_ =>
            {
                Remember(panel, true);
                Save();
            });
            panel.Grab = grab;

            // × beside the bar.
            var closeCanvas = DiegeticUi.WorldCanvas(root, "Close", new Vector2(44f, 44f),
                new Vector3(BarWidth * 0.5f + 0.04f, barY, -0.004f), Quaternion.identity, 0.001f);
            var x = DiegeticUi.HoloButton(closeCanvas.transform, "×", Vector2.zero, new Vector2(44f, 44f), () => Close(panel),
                DiegeticUi.BtnStyle.Danger);
            RayPress.Add(x);

            var mount = new GameObject("Mount").transform;
            mount.SetParent(root, false);
            switch (def.Kind)
            {
                case Kind.Repeater:
                    mount.localScale = Vector3.one * RepeaterScale;
                    BuildRepeater(mount, def);
                    break;
                case Kind.Comms:
                    mount.localScale = Vector3.one * ConsoleScale;
                    var comms = CommsConsole.Instance;
                    comms.Dock(mount);
                    panel.Layers = new LayerKeeper(comms.transform);
                    break;
                case Kind.Ops:
                    mount.localScale = Vector3.one * ConsoleScale;
                    var ops = OpsConsole.Instance;
                    panel.ConsoleHome = ops.transform.parent;
                    ops.Open(null, PreferredPlanet());
                    ops.transform.SetParent(mount, false);
                    ops.transform.localPosition = Vector3.zero;
                    ops.transform.localRotation = Quaternion.identity;
                    panel.Layers = new LayerKeeper(ops.transform);
                    break;
            }

            // The borrowed console first, so its own layers are recorded before the whole panel goes to Watch.
            panel.Layers?.Apply(_layer);
            SetLayer(root, _layer);
            _panels.Add(panel);
            if (_saved.TryGetValue(def.Id, out var s))
                s.Open = true;
            else
                Remember(panel, true);
            Save();
        }

        void BuildRepeater(Transform mount, Def def)
        {
            var screen = HoloScreen.Create(mount, "Repeater", RepeaterSize, Vector3.zero, Quaternion.identity, Trans.Get(def.TitleKey));
            screen.SetAccent(def.Accent, 0.45f);
            // Holo glass over the real room, like the cluster: no gunmetal housing, the station colour on the title.
            var housing = screen.transform.Find("Housing");
            if (housing != null && housing.TryGetComponent<MeshRenderer>(out var r))
                r.enabled = false;
            var frame = screen.Content.GetComponent<Image>();
            if (frame != null)
                frame.color = Glass;
            if (screen.Header != null)
                screen.Header.color = def.Accent;
            screen.gameObject.AddComponent<CrewStationScreen>().Bind(screen, _focus, def.Accent, def.Role);
        }

        static float HalfHeight(Def def) => def.Kind == Kind.Repeater
            ? RepeaterSize.y * 0.5f * RepeaterScale
            : (ConsoleSize.y * 0.5f + HousingBorder) * ConsoleScale;

        /// <summary>
        /// The cluster's Build key: the Ops console on that world's buildings, set out in the room or, already
        /// out, turned to it. <paramref name="planetId"/> 0 = the console's usual pick.
        /// </summary>
        public void OpenOps(int planetId)
        {
            if (!WatchMode.Inside)
                return;
            var def = _defs.Find(d => d.Kind == Kind.Ops);
            if (def == null || !Available(def))
                return;
            _opsPlanet = planetId;
            if (Find(def) == null)
            {
                Spawn(def);
            }
            else
            {
                var ops = OpsConsole.Instance;
                var mount = ops.transform.parent;
                ops.Open(null, PreferredPlanet());
                ops.transform.SetParent(mount, false);
                ops.transform.localPosition = Vector3.zero;
                ops.transform.localRotation = Quaternion.identity;
            }

            _opsPlanet = 0;
            RefreshKeys();
        }

        int _opsPlanet;

        int PreferredPlanet()
        {
            if (_opsPlanet > 0)
                return _opsPlanet;
            if (_focus == null)
                return 0;
            return _focus.ViewPlanetId > 0 ? _focus.ViewPlanetId : _focus.FindViewFleet()?.PlanetId ?? 0;
        }

        /// <summary>× (or the console's own Close): the panel goes, its place is remembered for next time.</summary>
        void Close(Panel panel)
        {
            if (!_panels.Contains(panel))
                return;
            Remember(panel, false);
            Despawn(panel);
            Save();
            RefreshKeys();
        }

        void Despawn(Panel panel)
        {
            _panels.Remove(panel);
            if (panel.Grab != null && panel.Grab.isSelected && panel.Grab.interactionManager != null)
                panel.Grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)panel.Grab);
            switch (panel.Def.Kind)
            {
                case Kind.Comms:
                    var comms = CommsConsole.Instance;
                    panel.Layers?.Restore();
                    if (comms != null && comms.Docked && panel.Root != null && comms.transform.IsChildOf(panel.Root))
                        comms.Undock();
                    break;
                case Kind.Ops:
                    var ops = OpsConsole.Instance;
                    panel.Layers?.Restore();
                    if (ops != null && panel.Root != null && ops.transform.IsChildOf(panel.Root))
                    {
                        if (ops.IsOpen)
                            ops.Close();
                        ops.transform.SetParent(panel.ConsoleHome, false);
                    }

                    break;
            }

            if (panel.Root != null)
                Destroy(panel.Root.gameObject);
        }

        void Pose(Def def, out Vector3 pos, out Quaternion rot)
        {
            var head = _head != null ? _head : _deck;
            if (_saved.TryGetValue(def.Id, out var s) && _rig != null)
            {
                pos = _rig.TransformPoint(s.Pos);
                rot = _rig.rotation * s.Rot;
                // Recentred or moved since: too far to be "where I left it" — spawn in front instead.
                if ((pos - head.position).sqrMagnitude < MaxRestoreDistance * MaxRestoreDistance)
                    return;
            }

            var f = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f)
                f = Vector3.forward;
            var dir = Quaternion.Euler(0f, def.Yaw, 0f) * f.normalized;
            var console = def.Kind != Kind.Repeater;
            pos = head.position + dir * (console ? 1f : 0.75f) + Vector3.up * (console ? 0.04f : -0.1f);
            rot = Face(pos, head.position);
        }

        static Quaternion Face(Vector3 at, Vector3 eye)
        {
            var look = at - eye;
            if (look.sqrMagnitude < 1e-4f)
                look = Vector3.forward;
            // Toward the eyes, a lectern's tilt at most.
            var flat = Vector3.ProjectOnPlane(look, Vector3.up);
            var pitch = Mathf.Clamp(Vector3.SignedAngle(flat, look, Vector3.Cross(Vector3.up, flat)), -35f, 35f);
            return Quaternion.LookRotation(flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized, Vector3.up) *
                   Quaternion.Euler(pitch, 0f, 0f);
        }

        void LateUpdate()
        {
            if (!WatchMode.Inside)
                return;
            // While carried: pivot about the bar so the screen keeps facing the player.
            if (_head != null)
            {
                var k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
                foreach (var p in _panels)
                {
                    if (p.Grab == null || !p.Grab.isSelected)
                        continue;
                    var barAt = p.Bar.position;
                    p.Root.rotation = Quaternion.Slerp(p.Root.rotation, Face(p.Root.position, _head.position), k);
                    p.Root.position += barAt - p.Bar.position;
                }
            }

            if (Time.unscaledTime < _nextTick)
                return;
            _nextTick = Time.unscaledTime + TickEvery;
            for (var i = _panels.Count - 1; i >= 0; i--)
            {
                var p = _panels[i];
                if (p.Layers == null)
                    continue;
                // The console's own Close was pressed (or it went away): the panel goes with it.
                var open = p.Def.Kind == Kind.Comms
                    ? CommsConsole.Instance != null && CommsConsole.Instance.IsOpen
                    : OpsConsole.Instance != null && OpsConsole.Instance.IsOpen;
                if (!open)
                {
                    Close(p);
                    continue;
                }

                p.Layers.Apply(_layer);
            }
        }

        static void Glow(MeshRenderer r, Color c, float mul)
        {
            if (r == null)
                return;
            _block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetColor(UiKit.AccentId, c);
            _block.SetFloat(UiKit.AccentMulId, mul);
            r.SetPropertyBlock(_block);
        }

        static void SetLayer(Transform t, int layer)
        {
            if (layer < 0 || t == null)
                return;
            t.gameObject.layer = layer;
            for (var i = 0; i < t.childCount; i++)
                SetLayer(t.GetChild(i), layer);
        }

        // ── Layout persistence (play-space local: the real room) ────────────────

        void Remember(Panel p, bool open)
        {
            if (p.Root == null)
                return;
            var space = _rig != null ? _rig : _deck;
            if (!_saved.TryGetValue(p.Def.Id, out var s))
                _saved[p.Def.Id] = s = new Saved();
            s.Open = open;
            s.Pos = space.InverseTransformPoint(p.Root.position);
            s.Rot = Quaternion.Inverse(space.rotation) * p.Root.rotation;
        }

        void Save()
        {
            var sb = new StringBuilder();
            foreach (var kv in _saved)
            {
                var s = kv.Value;
                if (sb.Length > 0)
                    sb.Append(';');
                sb.Append(kv.Key).Append(',').Append(s.Open ? '1' : '0');
                Append(sb, s.Pos.x);
                Append(sb, s.Pos.y);
                Append(sb, s.Pos.z);
                Append(sb, s.Rot.x);
                Append(sb, s.Rot.y);
                Append(sb, s.Rot.z);
                Append(sb, s.Rot.w);
            }

            PlayerPrefs.SetString(PrefsKey, sb.ToString());
            PlayerPrefs.Save();
        }

        static void Append(StringBuilder sb, float v) => sb.Append(',').Append(v.ToString("0.####", Inv));

        void Load()
        {
            _saved.Clear();
            var raw = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return;
            foreach (var entry in raw.Split(';'))
            {
                var f = entry.Split(',');
                if (f.Length != 9)
                    continue;
                var v = new float[7];
                var ok = true;
                for (var i = 0; i < 7 && ok; i++)
                    ok = float.TryParse(f[i + 2], NumberStyles.Float, Inv, out v[i]);
                if (!ok)
                    continue;
                var q = new Quaternion(v[3], v[4], v[5], v[6]);
                if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 0.5f)
                    q = Quaternion.identity;
                _saved[f[0]] = new Saved { Open = f[1] == "1", Pos = new Vector3(v[0], v[1], v[2]), Rot = Quaternion.Normalize(q) };
            }
        }

        // ── Borrowed consoles: Watch layer + ray presses while they are out ─────

        /// <summary>
        /// A bridge console carried into the watch: its hierarchy (and the rows it rebuilds) on the Watch layer,
        /// its buttons and fields pressable by the ray (<see cref="RayPress"/>); all undone when it goes back.
        /// </summary>
        sealed class LayerKeeper
        {
            readonly Transform _root;
            readonly int _homeLayer;
            readonly Dictionary<Transform, int> _layers = new();
            readonly List<Component> _added = new();
            readonly Stack<Transform> _walk = new();

            public LayerKeeper(Transform root)
            {
                _root = root;
                _homeLayer = root.gameObject.layer;
            }

            public void Apply(int layer)
            {
                if (_root == null || layer < 0)
                    return;
                _walk.Clear();
                _walk.Push(_root);
                while (_walk.Count > 0)
                {
                    var t = _walk.Pop();
                    var go = t.gameObject;
                    if (go.layer != layer)
                    {
                        _layers.TryAdd(t, go.layer);
                        go.layer = layer;
                    }

                    if (t.TryGetComponent<Button>(out var b) && !t.TryGetComponent<XRSimpleInteractable>(out _))
                    {
                        var hadCol = t.TryGetComponent<BoxCollider>(out _);
                        var rp = RayPress.Add(b);
                        _added.Add(rp);
                        _added.Add(t.GetComponent<XRSimpleInteractable>());
                        if (!hadCol)
                            _added.Add(t.GetComponent<BoxCollider>());
                    }
                    else if (t.TryGetComponent<TMP_InputField>(out var field) && !t.TryGetComponent<XRSimpleInteractable>(out _))
                    {
                        AddFieldPress(field);
                    }

                    for (var i = 0; i < t.childCount; i++)
                        _walk.Push(t.GetChild(i));
                }
            }

            /// <summary>A text field the ray can focus (trigger): opens the Quest keyboard as a poke would.</summary>
            void AddFieldPress(TMP_InputField field)
            {
                var rt = (RectTransform)field.transform;
                var hadCol = field.TryGetComponent<BoxCollider>(out var col);
                if (!hadCol)
                {
                    col = field.gameObject.AddComponent<BoxCollider>();
                    col.size = new Vector3(rt.rect.width, rt.rect.height, 8f);
                    col.center = new Vector3((0.5f - rt.pivot.x) * rt.rect.width, (0.5f - rt.pivot.y) * rt.rect.height, 0f);
                    _added.Add(col);
                }

                var xi = field.gameObject.AddComponent<XRSimpleInteractable>();
                xi.colliders.Clear();
                xi.colliders.Add(col);
                xi.selectEntered.AddListener(_ =>
                {
                    if (field == null || !field.IsInteractable())
                        return;
                    field.Select();
                    field.ActivateInputField();
                });
                _added.Add(xi);
            }

            public void Restore()
            {
                foreach (var c in _added)
                    if (c != null)
                        Destroy(c);
                _added.Clear();
                if (_root == null)
                    return;
                _walk.Clear();
                _walk.Push(_root);
                while (_walk.Count > 0)
                {
                    var t = _walk.Pop();
                    t.gameObject.layer = _layers.TryGetValue(t, out var l) ? l : _homeLayer;
                    for (var i = 0; i < t.childCount; i++)
                        _walk.Push(t.GetChild(i));
                }

                _layers.Clear();
            }
        }
    }
}
