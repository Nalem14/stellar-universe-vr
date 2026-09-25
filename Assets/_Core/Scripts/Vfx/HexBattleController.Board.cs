using System.Collections.Generic;
using System.Text;
using Core.App;
using Core.UI;
using Core.Utils;
using TMPro;
using UnityEngine;

namespace Core.Vfx
{
    /// <summary>The battle board: hex grid mesh, holo hulls, rim console, header and outcome.</summary>
    public partial class HexBattleController
    {
        /// <summary>Hex circumradius on the table (m): 11 × 9 axial cells span ~1.3 × 0.7 m.</summary>
        public const float HexSize = 0.056f;
        const float BoardLift = 0.16f;
        const float ShipHover = 0.03f;
        const float ShipScale = 0.0085f;
        const float RevealSeconds = 0.9f;
        const float FoldSeconds = 0.55f;
        static readonly float Sqrt3 = Mathf.Sqrt(3f);

        static readonly Color GridBase = new(0.14f, 0.42f, 0.5f, 0.22f);
        static readonly Color ZoneMine = new(0.12f, 0.55f, 1f, 0.42f);
        static readonly Color ZoneFoe = new(1f, 0.22f, 0.16f, 0.36f);
        static readonly Color Reach = new(0.08f, 1f, 0.3f, 1f);
        static readonly Color Hostile = new(1f, 0.12f, 0.08f, 1f);
        static readonly Color Friendly = new(0.15f, 1f, 0.5f, 1f);
        static readonly Color Jump = new(0.6f, 0.25f, 1f, 1f);
        static readonly Color Blast = new(1f, 0.62f, 0.2f, 1f);
        static readonly Color MineTint = new(0.3f, 0.95f, 1f, 1f);
        static readonly Color FoeTint = new(1f, 0.36f, 0.26f, 1f);

        Transform _boardRoot;
        Transform _frame;
        Mesh _gridMesh;
        Material _gridMat;
        Material _hullMat;
        Material _barBack;
        Material _barHp;
        Material _barHpLow;
        Material _barShield;
        MaterialPropertyBlock _mpb;
        readonly List<Vector2Int> _cells = new();
        Color[] _cellColor;
        Color[] _vertColor;
        Vector2[] _vertUv;
        int _orientedTeam = int.MinValue;

        class ShipView
        {
            public BattleShip Data;
            public GameObject Root;
            public MeshRenderer Hull;
            public Transform HpFill;
            public Transform ShieldFill;
            public MeshRenderer HpRenderer;
            public TextMeshPro Label;
            public Vector3 From;
            public Vector3 To;
            public float MoveT = 1f;
            public float DeathT = -1f;
        }

        readonly Dictionary<int, ShipView> _ships = new();
        GameObject _activeMarker;

        Transform _console;
        readonly List<PokeButton> _buttons = new();
        string _consoleSig;
        TextMeshPro _card;
        TextMeshPro _header;
        TextMeshPro _timeline;
        TextMeshPro _toast;
        float _toastUntil;
        TextMeshPro _outcome;
        float _outcomeT = -1f;
        PokeButton _rejoin;
        float _headerTick;

        float _reveal;
        int _revealDir;
        bool _folding;

        // ── Build ────────────────────────────────────────────────────────────────

        void BuildBoard()
        {
            if (_mount == null || _boardRoot != null)
                return;
            _boardRoot = new GameObject("HexBattleBoard").transform;
            _boardRoot.SetParent(_mount, false);
            _boardRoot.localPosition = new Vector3(0f, BoardLift, 0f);
            _frame = new GameObject("Frame").transform;
            _frame.SetParent(_boardRoot, false);
            _mpb = new MaterialPropertyBlock();

            BuildGrid();
            BuildActiveMarker();
            BuildTexts();
            _console = new GameObject("BattleConsole").transform;
            _console.SetParent(_boardRoot, false);
            // Near rim (captain side), tilted up to the eye like the table's other rim controls.
            _console.localPosition = new Vector3(0f, 0.12f - BoardLift, -WorldScale.HoloDiscRadius * 0.58f);
            _console.localRotation = Quaternion.Euler(50f, 0f, 0f);
            _card = UiKit.Label(_console, "Card", string.Empty, new Vector3(0f, 0.108f, 0f), 0.7f, 0.014f,
                UiKit.TextBright);
            _card.richText = true;
            _toast = UiKit.Label(_console, "Toast", string.Empty, new Vector3(0f, 0.15f, 0f), 0.7f, 0.016f,
                UiKit.Amber);
            _toast.richText = true;

            _rejoin = PokeButton.Create(_mount, "BattleRejoin", Trans.Get("vr.battle.rejoin"),
                new Vector3(0f, 0.06f, -WorldScale.HoloDiscRadius * 0.72f), Quaternion.Euler(50f, 0f, 0f),
                new Vector2(0.3f, 0.055f), UiKit.Danger, Rejoin);
            _rejoin.gameObject.SetActive(false);

            _boardRoot.gameObject.SetActive(false);
        }

        public static Vector3 HexLocal(int q, int r) =>
            new(HexSize * (Sqrt3 * q + Sqrt3 * 0.5f * r), 0f, HexSize * 1.5f * r);

        /// <summary>Frame-local point → nearest axial cell (cube rounding).</summary>
        public static Vector2Int LocalHex(Vector3 p)
        {
            var qf = (Sqrt3 / 3f * p.x - p.z / 3f) / HexSize;
            var rf = (2f / 3f * p.z) / HexSize;
            var sf = -qf - rf;
            var q = Mathf.RoundToInt(qf);
            var r = Mathf.RoundToInt(rf);
            var s = Mathf.RoundToInt(sf);
            var dq = Mathf.Abs(q - qf);
            var dr = Mathf.Abs(r - rf);
            var ds = Mathf.Abs(s - sf);
            if (dq > dr && dq > ds)
                q = -r - s;
            else if (dr > ds)
                r = -q - s;
            return new Vector2Int(q, r);
        }

        void BuildGrid()
        {
            for (var q = -BattleSnapshot.GridQ; q <= BattleSnapshot.GridQ; q++)
            for (var r = -BattleSnapshot.GridR; r <= BattleSnapshot.GridR; r++)
                _cells.Add(new Vector2Int(q, r));

            var n = _cells.Count;
            var verts = new Vector3[n * 7];
            var tris = new int[n * 18];
            _vertColor = new Color[n * 7];
            _vertUv = new Vector2[n * 7];
            _cellColor = new Color[n];
            var inset = HexSize * 0.94f;
            for (var i = 0; i < n; i++)
            {
                var c = HexLocal(_cells[i].x, _cells[i].y);
                var v0 = i * 7;
                verts[v0] = c;
                _vertUv[v0] = new Vector2(0f, 0f);
                for (var k = 0; k < 6; k++)
                {
                    var a = Mathf.Deg2Rad * (60f * k - 30f);
                    verts[v0 + 1 + k] = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * inset;
                    _vertUv[v0 + 1 + k] = new Vector2(1f, 0f);
                    var t = i * 18 + k * 3;
                    tris[t] = v0;
                    tris[t + 1] = v0 + 1 + (k + 1) % 6;
                    tris[t + 2] = v0 + 1 + k;
                }
            }

            _gridMesh = new Mesh { name = "HexBattleGrid" };
            _gridMesh.MarkDynamic();
            _gridMesh.vertices = verts;
            _gridMesh.triangles = tris;
            _gridMesh.uv = _vertUv;
            _gridMesh.colors = _vertColor;
            _gridMesh.RecalculateBounds();

            var go = new GameObject("Grid", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_frame, false);
            go.GetComponent<MeshFilter>().sharedMesh = _gridMesh;
            var mr = go.GetComponent<MeshRenderer>();
            var shader = Shader.Find("SU/HoloHexGrid");
            _gridMat = shader != null
                ? new Material(shader)
                : _art.Holo(_art.HexGrid != null ? _art.HexGrid : Texture2D.whiteTexture, GridBase);
            if (_gridMat.HasProperty("_RevealRadius"))
                _gridMat.SetFloat("_RevealRadius", HexLocal(BattleSnapshot.GridQ, BattleSnapshot.GridR).magnitude);
            mr.sharedMaterial = _gridMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            RefreshGrid();
        }

        void BuildActiveMarker()
        {
            _activeMarker = new GameObject("ActiveMarker");
            _activeMarker.transform.SetParent(_frame, false);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "Ring";
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(_activeMarker.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.003f, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = Vector3.one * HexSize * 2.1f;
            ring.GetComponent<MeshRenderer>().sharedMaterial =
                _art.RadarIcon(_art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture, UiKit.Amber);
            var spin = ring.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 45f;
            spin.BobMeters = 0f;

            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "Beam";
            Destroy(beam.GetComponent<Collider>());
            beam.transform.SetParent(_activeMarker.transform, false);
            beam.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            beam.transform.localScale = new Vector3(0.004f, 0.09f, 0.004f);
            beam.GetComponent<MeshRenderer>().sharedMaterial = _art.Holo(Texture2D.whiteTexture,
                new Color(1f, 0.7f, 0.3f, 0.35f));
            _activeMarker.SetActive(false);
        }

        void BuildTexts()
        {
            // Header floats over the far edge of the board, facing the captain.
            var head = new GameObject("Header").transform;
            head.SetParent(_boardRoot, false);
            head.localPosition = new Vector3(0f, 0.2f, HexSize * 1.5f * BattleSnapshot.GridR + 0.12f);
            _header = UiKit.Label(head, "Title", string.Empty, Vector3.zero, 1.2f, 0.034f, UiKit.TextBright);
            _header.richText = true;
            _timeline = UiKit.Label(head, "Timeline", string.Empty, new Vector3(0f, -0.055f, 0f), 1.3f, 0.018f,
                UiKit.TextDim);
            _timeline.richText = true;

            var outcome = new GameObject("Outcome").transform;
            outcome.SetParent(_boardRoot, false);
            outcome.localPosition = new Vector3(0f, 0.2f, 0f);
            outcome.gameObject.AddComponent<BillboardFace>();
            _outcome = UiKit.Label(outcome, "Text", string.Empty, Vector3.zero, 1f, 0.08f, UiKit.TextBright);
            _outcome.fontStyle = FontStyles.Bold;
            _outcome.outlineWidth = 0.18f;
            _outcome.outlineColor = new Color32(2, 10, 16, 220);
            outcome.gameObject.SetActive(false);
        }

        /// <summary>Our side always starts on the captain's left, like the web (team 0 left).</summary>
        void OrientBoard(int myTeam)
        {
            if (_orientedTeam == myTeam)
                return;
            _orientedTeam = myTeam;
            _frame.localRotation = Quaternion.Euler(0f, myTeam == 1 ? 180f : 0f, 0f);
            RefreshGrid();
        }

        // ── Ships ────────────────────────────────────────────────────────────────

        void SyncShips(BattleSnapshot prev, BattleSnapshot snap)
        {
            foreach (var s in snap.Ships)
            {
                _ships.TryGetValue(s.Id, out var view);
                if (view == null)
                {
                    if (!s.Alive)
                        continue;
                    view = CreateShip(s);
                    _ships[s.Id] = view;
                }

                var old = view.Data;
                view.Data = s;
                var to = HexLocal(s.Q, s.R) + Vector3.up * ShipHover;
                if ((to - view.To).sqrMagnitude > 1e-6f)
                {
                    view.From = view.Root.transform.localPosition;
                    view.To = to;
                    view.MoveT = prev == null ? 1f : 0f;
                    if (prev != null)
                        CicCue.Whoosh(view.Root.transform.position);
                }

                if (old != null && prev != null)
                {
                    var lost = (old.Hp - s.Hp) + (old.Shield - s.Shield);
                    if (old.Hp > s.Hp || old.Shield > s.Shield)
                        CombatEvents.RaiseHit(s.FleetId, Mathf.Max(0, old.Hp - s.Hp), Mathf.Max(0, old.Shield - s.Shield));
                    if (lost > 0)
                        Floater(view.Root.transform.position + Vector3.up * 0.05f, "-" + lost,
                            s.Hp < old.Hp ? UiKit.Danger : new Color(0.45f, 0.75f, 1f, 1f));
                    else if (s.Hp > old.Hp)
                        Floater(view.Root.transform.position + Vector3.up * 0.05f, "+" + (s.Hp - old.Hp), UiKit.Ok);
                    else if (s.Shield > old.Shield)
                        Floater(view.Root.transform.position + Vector3.up * 0.05f, "+" + (s.Shield - old.Shield),
                            new Color(0.45f, 0.75f, 1f, 1f));
                    if (old.Alive && !s.Alive)
                        Destroyed(view);
                }
                else if (!s.Alive && view.DeathT < 0f)
                    view.Root.SetActive(false);

                PaintShip(view, s.Id == snap.ActiveId);
            }

            // Rows gone from the state (fleet deleted at the end) fade out like a kill.
            var gone = new List<int>();
            foreach (var kv in _ships)
                if (snap.Find(kv.Key) == null)
                    gone.Add(kv.Key);
            foreach (var id in gone)
            {
                if (_ships[id].Root != null)
                    Destroy(_ships[id].Root);
                _ships.Remove(id);
            }

            var active = snap.State == BattleSnapshot.Active ? snap.ActiveShip : null;
            _activeMarker.SetActive(active != null && active.Alive);
            if (active != null)
                _activeMarker.transform.localPosition = HexLocal(active.Q, active.R);
        }

        ShipView CreateShip(BattleShip s)
        {
            var v = new ShipView { Data = s };
            v.Root = new GameObject("BShip_" + s.Id);
            v.Root.transform.SetParent(_frame, false);
            v.To = v.From = HexLocal(s.Q, s.R) + Vector3.up * ShipHover;
            v.Root.transform.localPosition = v.To;
            // Team 0 faces +x (toward team 1), team 1 faces −x.
            v.Root.transform.localRotation = Quaternion.LookRotation(s.Team == 0 ? Vector3.right : Vector3.left, Vector3.up);

            var placed = new List<FocusShipModule>();
            foreach (var m in s.Modules)
                if (m.OnGrid)
                    placed.Add(m);
            var hull = new GameObject("Hull");
            hull.transform.SetParent(v.Root.transform, false);
            var zSign = placed.Count > 0 ? ShipHullBuilder.NoseSignFor(placed) : 1f;
            hull.transform.localScale = new Vector3(ShipScale, ShipScale * 1.8f, ShipScale * zSign);
            hull.AddComponent<MeshFilter>().sharedMesh = ShipHullBuilder.SilhouetteMesh(placed, s.FleetId);
            v.Hull = hull.AddComponent<MeshRenderer>();
            v.Hull.sharedMaterial = HullMat();
            v.Hull.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var bob = hull.AddComponent<HoloSpin>();
            bob.DegreesPerSecond = 0f;
            bob.BobMeters = 0.003f;

            // Hull / shield bars + name, always facing the captain.
            var bars = new GameObject("Bars").transform;
            bars.SetParent(v.Root.transform, false);
            bars.localPosition = new Vector3(0f, 0.038f, 0f);
            bars.gameObject.AddComponent<BillboardFace>();
            Bar(bars, "Back", _barBack ??= _art.Lit(Texture2D.whiteTexture, new Color(0.02f, 0.05f, 0.07f, 1f), 0.4f),
                new Vector3(0f, 0f, 0.0005f), new Vector3(0.058f, 0.011f, 1f));
            v.HpFill = Bar(bars, "Hull", _barHp ??= _art.Lit(Texture2D.whiteTexture, UiKit.Ok, 2.2f),
                new Vector3(0f, 0.002f, 0f), new Vector3(0.054f, 0.004f, 1f));
            v.HpRenderer = v.HpFill.GetComponent<MeshRenderer>();
            v.ShieldFill = Bar(bars, "Shield", _barShield ??= _art.Lit(Texture2D.whiteTexture, new Color(0.35f, 0.7f, 1f, 1f), 2.4f),
                new Vector3(0f, -0.0025f, 0f), new Vector3(0.054f, 0.0025f, 1f));
            v.Label = UiKit.Label(bars, "Name", string.Empty, new Vector3(0f, 0.014f, 0f), 0.16f, 0.0085f,
                UiKit.TextBright);
            v.Label.richText = true;
            v.Label.outlineWidth = 0.2f;
            v.Label.outlineColor = new Color32(2, 10, 16, 230);
            return v;
        }

        static Transform Bar(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(parent, false);
            q.transform.localPosition = pos;
            q.transform.localScale = scale;
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return q.transform;
        }

        Material HullMat()
        {
            if (_hullMat != null)
                return _hullMat;
            var shader = Shader.Find("SU/HoloCrystal");
            _hullMat = shader != null ? new Material(shader) : _art.Lit(Texture2D.whiteTexture, CicArtKit.Cyan, 1.4f);
            _hullMat.enableInstancing = true;
            return _hullMat;
        }

        void PaintShip(ShipView v, bool active)
        {
            var s = v.Data;
            var tint = s.IsMine ? MineTint : FoeTint;
            _mpb.Clear();
            _mpb.SetColor("_Color", tint * 0.55f);
            _mpb.SetColor("_Emission", active ? Color.Lerp(tint, UiKit.Amber, 0.35f) : tint);
            _mpb.SetFloat("_EmissionMul", active ? 1.7f : 1f);
            _mpb.SetFloat("_Rim", 2.2f);
            _mpb.SetFloat("_Pulse", active ? 1.2f : 0.25f);
            v.Hull.SetPropertyBlock(_mpb);

            var hp = Mathf.Clamp01(s.Hp / (float)s.MaxHp);
            Fill(v.HpFill, hp, 0.054f);
            v.HpRenderer.sharedMaterial = hp > 0.3f
                ? _barHp
                : _barHpLow ??= _art.Lit(Texture2D.whiteTexture, UiKit.Danger, 2.4f);
            v.ShieldFill.gameObject.SetActive(s.MaxShield > 0);
            if (s.MaxShield > 0)
                Fill(v.ShieldFill, Mathf.Clamp01(s.Shield / (float)s.MaxShield), 0.054f);

            var sb = new StringBuilder();
            var col = s.IsMine ? "#7fefff" : "#ff8a78";
            sb.Append("<color=").Append(col).Append('>').Append(ShipName(s)).Append("</color>");
            AppendStatus(sb, s, compact: true);
            v.Label.text = sb.ToString();
        }

        static void Fill(Transform bar, float pct, float width)
        {
            var sc = bar.localScale;
            bar.localScale = new Vector3(Mathf.Max(0.0001f, width * pct), sc.y, 1f);
            var p = bar.localPosition;
            bar.localPosition = new Vector3(-width * 0.5f + width * pct * 0.5f, p.y, p.z);
        }

        static string ShipName(BattleShip s) =>
            string.IsNullOrEmpty(s.Name) || s.Name == "ship" ? "#" + s.FleetId : s.Name;

        static readonly string[] Effects = { "ionized", "jammed", "gravity", "overheated" };

        static void AppendStatus(StringBuilder sb, BattleShip s, bool compact)
        {
            foreach (var e in Effects)
            {
                var turns = s.StatusTurns(e);
                if (turns <= 0)
                    continue;
                sb.Append(compact ? " <size=80%><color=#ffc66b>" : "  <color=#ffc66b>")
                    .Append(Trans.Get("vr.battle.status." + e));
                if (!compact)
                    sb.Append(' ').Append(turns);
                sb.Append(compact ? "</color></size>" : "</color>");
            }

            if (s.BuffArmor > 0)
                sb.Append(compact ? " <size=80%><color=#9fd8ff>" : "  <color=#9fd8ff>")
                    .Append(Trans.Get(s.BuffArmor >= 2 ? "vr.battle.status.stealth" : "vr.battle.status.armor"))
                    .Append(compact ? "</color></size>" : "</color>");
        }

        void ClearShips()
        {
            foreach (var v in _ships.Values)
                if (v.Root != null)
                    Destroy(v.Root);
            _ships.Clear();
            _consoleSig = null;
            _orientedTeam = int.MinValue;
            if (_activeMarker != null)
                _activeMarker.SetActive(false);
            if (_outcome != null)
                _outcome.transform.parent.gameObject.SetActive(false);
            ClearConsole();
        }

        // ── Grid states ───────────────────────────────────────────────────────────

        void RefreshGrid()
        {
            if (_gridMesh == null)
                return;
            var s = _state;
            var myTeam = s != null ? s.MyTeam : 0;
            var src = s != null && s.MyTurn && s.State == BattleSnapshot.Active ? s.ActiveShip : null;
            var skill = _pendingSkill;
            var canMove = src != null && skill == null && src.Pm > 0 && src.StatusTurns("gravity") <= 0;
            var hover = _hoverCell;
            var blast = skill != null && skill.Type == "attack_aoe" && hover.HasValue && InRange(src, skill, hover.Value)
                ? hover.Value
                : (Vector2Int?)null;

            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                var color = GridBase;
                var pulse = false;
                // Starting zones, as the web tints them (team 0 left columns, team 1 right).
                if (c.x <= -3)
                    color = myTeam == 1 ? ZoneFoe : ZoneMine;
                else if (c.x >= 3)
                    color = myTeam == 1 ? ZoneMine : ZoneFoe;
                color.a *= 0.5f;

                var occupant = s?.At(c.x, c.y);
                if (src != null)
                {
                    var d = BattleSnapshot.Dist(src.Q, src.R, c.x, c.y);
                    if (canMove && d > 0 && d <= src.Pm && occupant == null)
                    {
                        color = Reach;
                        pulse = true;
                    }
                    else if (skill != null && InRange(src, skill, c))
                    {
                        if (skill.TargetsHex && skill.Type == "teleport")
                        {
                            if (occupant == null)
                            {
                                color = Jump;
                                pulse = true;
                            }
                        }
                        else if (skill.TargetsAlly)
                        {
                            color = Friendly * new Color(1f, 1f, 1f, occupant != null && occupant.Team == src.Team ? 1f : 0.35f);
                            pulse = occupant != null && occupant.Team == src.Team;
                        }
                        else
                        {
                            var enemyHere = occupant != null && occupant.Team != src.Team;
                            color = Hostile * new Color(1f, 1f, 1f, enemyHere || skill.TargetsHex ? 1f : 0.35f);
                            pulse = enemyHere || skill.TargetsHex;
                        }
                    }
                }

                if (blast.HasValue && BattleSnapshot.Dist(blast.Value.x, blast.Value.y, c.x, c.y) <= Mathf.Max(1, skill.Aoe))
                {
                    color = Blast;
                    pulse = false;
                }

                if (occupant != null)
                    color = Color.Lerp(color, occupant.IsMine ? MineTint : FoeTint, 0.35f) + new Color(0f, 0f, 0f, 0.15f);

                if (hover.HasValue && hover.Value == c)
                    color = Color.Lerp(color, Color.white, 0.45f) + new Color(0f, 0f, 0f, 0.3f);

                _cellColor[i] = color;
                var v0 = i * 7;
                for (var k = 0; k < 7; k++)
                {
                    _vertColor[v0 + k] = color;
                    _vertUv[v0 + k].y = pulse ? 1f : 0f;
                }
            }

            _gridMesh.colors = _vertColor;
            _gridMesh.uv = _vertUv;
        }

        static bool InRange(BattleShip src, BattleSkill skill, Vector2Int c)
        {
            if (src == null || skill == null)
                return false;
            var d = BattleSnapshot.Dist(src.Q, src.R, c.x, c.y);
            return d > 0 && d <= skill.Range && d >= skill.RangeMin;
        }

        // ── Console ───────────────────────────────────────────────────────────────

        void RefreshConsole()
        {
            var s = _state;
            var sig = ConsoleSignature(s);
            if (sig == _consoleSig)
            {
                RefreshCard();
                return;
            }

            _consoleSig = sig;
            ClearConsole();
            if (s == null)
                return;

            const float w = 0.118f, h = 0.046f, step = 0.128f;
            if (_outcomeShown)
            {
                Button(Trans.Get("vr.battle.leave"), 0, 2, 1, w * 1.6f, h, UiKit.Cyan, Leave);
            }
            else if (s.State == BattleSnapshot.Pending)
            {
                if (!s.MyReady)
                    Button(Trans.Get("vr.battle.ready"), 0, 0, 2, w * 1.4f, h, UiKit.Ok, () => AsyncTap.Run(ReadyUp()));
                Button(Trans.Get("vr.battle.withdraw"), 1, 0, 2, w * 1.4f, h, UiKit.Danger, () => AsyncTap.Run(Withdraw()));
                Button(Trans.Get("vr.battle.leave"), 0, 2, 1, w * 1.4f, h, UiKit.Cyan, Leave);
            }
            else
            {
                var src = s.MyTurn ? s.ActiveShip : null;
                if (src != null)
                {
                    var slot = 0;
                    var gravity = src.StatusTurns("gravity") > 0;
                    var move = Button(Trans.Get("move") + "\n<size=70%>" + Trans.Format("vr.battle.pmLeft", src.Pm) + "</size>",
                        slot++, 0, 5, w, h, _pendingSkill == null ? UiKit.Ok : UiKit.Cyan, () =>
                        {
                            _pendingSkill = null;
                            CicCue.Hover(_console.position);
                            RefreshGrid();
                            RefreshConsole();
                        });
                    move.Interactive = src.Pm > 0 && !gravity;
                    var ionized = src.StatusTurns("ionized") > 0;
                    foreach (var sk in src.Skills)
                    {
                        if (slot >= 10)
                            break;
                        var skill = sk;
                        var row = slot / 5;
                        var col = slot % 5;
                        var accent = _pendingSkill != null && _pendingSkill.Id == sk.Id ? UiKit.Amber
                            : sk.Hostile ? UiKit.Danger
                            : UiKit.Cyan;
                        var b = Button(SkillLabel(sk), col, row, 5, w, h, accent, () => ChooseSkill(skill));
                        b.Interactive = !ionized && sk.CooldownLeft <= 0 && src.Ap >= sk.Ap;
                        slot++;
                    }

                    Button(Trans.Get("vr.tactical.endTurn"), 0, 2, 2, w * 1.4f, h, UiKit.Amber, () => AsyncTap.Run(EndTurn()));
                    Button(Trans.Get("vr.battle.leave"), 1, 2, 2, w * 1.4f, h, UiKit.Cyan, Leave);
                }
                else
                    Button(Trans.Get("vr.battle.leave"), 0, 2, 1, w * 1.4f, h, UiKit.Cyan, Leave);
            }

            RefreshCard();

            PokeButton Button(string label, int col, int row, int perRow, float bw, float bh, Color accent,
                System.Action onPress)
            {
                var x = (col - (perRow - 1) * 0.5f) * (perRow == 5 ? step : bw + 0.02f);
                var y = 0.06f - row * 0.058f;
                var b = PokeButton.Create(_console, "Btn" + row + "_" + col, label, new Vector3(x, y, 0f),
                    Quaternion.identity, new Vector2(bw, bh), accent, onPress);
                b.Label.richText = true;
                _buttons.Add(b);
                return b;
            }
        }

        string ConsoleSignature(BattleSnapshot s)
        {
            if (s == null)
                return "none";
            var sb = new StringBuilder();
            sb.Append(s.State).Append('|').Append(s.MyReady).Append('|').Append(s.MyTurn).Append('|')
                .Append(s.ActiveId).Append('|').Append(_outcomeShown).Append('|').Append(_pendingSkill?.Id);
            var a = s.MyTurn ? s.ActiveShip : null;
            if (a != null)
            {
                sb.Append('|').Append(a.Ap).Append('|').Append(a.Pm).Append('|').Append(a.StatusTurns("ionized"))
                    .Append(a.StatusTurns("gravity"));
                foreach (var k in a.Skills)
                    sb.Append(k.Id).Append(k.CooldownLeft);
            }

            return sb.ToString();
        }

        static string SkillLabel(BattleSkill sk)
        {
            var name = Trans.Get("battleSkill_" + sk.Id);
            var info = sk.CooldownLeft > 0
                ? Trans.Format("vr.battle.cooldown", sk.CooldownLeft)
                : Trans.Format("vr.battle.apCost", sk.Ap) + " · " + (sk.SelfCast
                    ? Trans.Get("vr.battle.self")
                    : Trans.Format("vr.battle.range", sk.RangeMin > 0 ? sk.RangeMin + "-" + sk.Range : sk.Range.ToString()));
            return name + "\n<size=70%>" + info + "</size>";
        }

        void ClearConsole()
        {
            foreach (var b in _buttons)
                if (b != null)
                    Destroy(b.gameObject);
            _buttons.Clear();
        }

        void RefreshCard()
        {
            if (_card == null)
                return;
            var s = _state;
            var ship = _inspect > 0 ? s?.Find(_inspect) : null;
            if (ship == null && s != null && s.MyTurn)
                ship = s.ActiveShip;
            if (ship == null || s == null || s.State != BattleSnapshot.Active)
            {
                _card.text = string.Empty;
                return;
            }

            var sb = new StringBuilder();
            sb.Append(ship.IsMine ? "<color=#7fefff>" : "<color=#ff8a78>").Append("<b>").Append(ShipName(ship))
                .Append("</b></color>   ")
                .Append(Trans.Format("vr.battle.stats", ship.Ap, ship.MaxAp, ship.Pm, ship.MaxPm))
                .Append("  ·  ").Append(Trans.Get("vr.battle.hull")).Append(' ').Append(ship.Hp).Append('/').Append(ship.MaxHp);
            if (ship.MaxShield > 0)
                sb.Append("  ·  ").Append(Trans.Get("shield")).Append(' ').Append(ship.Shield).Append('/').Append(ship.MaxShield);
            AppendStatus(sb, ship, compact: false);
            if (s.MyTurn && ship == s.ActiveShip)
            {
                var hint = _pendingSkill == null ? "vr.battle.pickMove"
                    : _pendingSkill.TargetsAlly ? "vr.battle.pickAlly"
                    : _pendingSkill.TargetsHex ? "vr.battle.pickHex"
                    : "vr.battle.pickTarget";
                sb.Append("\n<size=80%><color=#9fd8e8>").Append(Trans.Get(hint)).Append("</color></size>");
            }

            _card.text = sb.ToString();
        }

        void Toast(string text)
        {
            if (_toast == null)
                return;
            _toast.text = text;
            _toastUntil = Time.unscaledTime + 3.5f;
        }

        // ── Header ────────────────────────────────────────────────────────────────

        void SetHeader(string title, string sub)
        {
            if (_header != null)
                _header.text = title;
            if (_timeline != null)
                _timeline.text = sub;
        }

        void TickHeader(bool force = false)
        {
            if (!force && Time.unscaledTime < _headerTick)
                return;
            _headerTick = Time.unscaledTime + 0.5f;
            var s = _state;
            if (s == null || _outcomeShown)
                return;
            var now = FleetOrderGate.UnixNow();
            if (s.State == BattleSnapshot.Pending)
            {
                SetHeader(Trans.Format("vr.battle.pending", Clock(s.StartDeadline - now)),
                    s.MyReady ? Trans.Get("vr.battle.waitingFoe") : string.Empty);
                return;
            }

            var active = s.ActiveShip;
            var turn = s.MyTurn
                ? "<color=#8dff9f>" + Trans.Get("vr.battle.yourTurn") + "</color>"
                : "<color=#ffc66b>" + Trans.Format("vr.battle.theirTurn", active != null ? ShipName(active) : "…") + "</color>";
            var title = Trans.Format("vr.battle.round", s.Round) + "  —  " + turn + "   <color=#ffd27a>" +
                        Clock(s.TurnDeadline - now) + "</color>";

            // Initiative order from the active ship onwards (web left panel).
            var sb = new StringBuilder();
            var start = Mathf.Max(0, s.Timeline.IndexOf(s.ActiveId));
            for (var i = 0; i < s.Timeline.Count && i < 8; i++)
            {
                var ship = s.Find(s.Timeline[(start + i) % s.Timeline.Count]);
                if (ship == null)
                    continue;
                if (sb.Length > 0)
                    sb.Append("  <color=#4a6a78>›</color>  ");
                sb.Append(ship.IsMine ? "<color=#7fefff>" : "<color=#ff8a78>");
                if (i == 0)
                    sb.Append("<b>▶ ");
                sb.Append(ShipName(ship));
                if (i == 0)
                    sb.Append("</b>");
                sb.Append("</color>");
            }

            SetHeader(title, sb.ToString());
        }

        static string Clock(long seconds)
        {
            if (seconds < 0)
                seconds = 0;
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        void ShowOutcome(string text, Color color)
        {
            SetHeader(text, string.Empty);
            _outcome.text = text;
            _outcome.color = color;
            _outcome.transform.parent.gameObject.SetActive(true);
            _outcomeT = 0f;
            _card.text = string.Empty;
            _pendingSkill = null;
        }

        // ── Rejoin (board closed, fight still on) ──────────────────────────────────

        void SetRejoinVisible(bool on)
        {
            if (_rejoin != null && _rejoin.gameObject.activeSelf != on)
            {
                _rejoin.gameObject.SetActive(on);
                if (on)
                    CicCue.Chime(_rejoin.transform.position);
            }
        }

        void PulseRejoin()
        {
            if (_rejoin == null || !_rejoin.gameObject.activeSelf)
                return;
            var k = 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.04f;
            _rejoin.transform.localScale = Vector3.one * k;
        }

        // ── Unfold / fold ───────────────────────────────────────────────────────────

        void StartReveal(bool open)
        {
            _revealDir = open ? 1 : -1;
            if (open)
                _reveal = 0f;
            CicCue.Deploy(_boardRoot != null ? _boardRoot.position : transform.position);
        }

        void BeginFold()
        {
            if (_folding || !_visible)
                return;
            _folding = true;
            ResetAim();
            ClearConsole();
            _consoleSig = null;
            StartReveal(false);
        }

        void AnimateBoard()
        {
            if (_boardRoot == null || !_boardRoot.gameObject.activeSelf)
            {
                if (_folding)
                    _folding = false;
                return;
            }

            if (_revealDir != 0)
            {
                _reveal = Mathf.Clamp01(_reveal + _revealDir * Time.unscaledDeltaTime /
                    (_revealDir > 0 ? RevealSeconds : FoldSeconds));
                if (_gridMat != null && _gridMat.HasProperty("_Reveal"))
                    _gridMat.SetFloat("_Reveal", _reveal);
                var front = _reveal * (HexLocal(BattleSnapshot.GridQ, BattleSnapshot.GridR).magnitude + 0.12f);
                foreach (var v in _ships.Values)
                {
                    if (v.Root == null || v.DeathT >= 0f)
                        continue;
                    var d = new Vector2(v.To.x, v.To.z).magnitude;
                    v.Root.transform.localScale = Vector3.one * MotionEase.SmoothOut(Mathf.Clamp01((front - d) / 0.12f));
                }

                if ((_revealDir > 0 && _reveal >= 1f) || (_revealDir < 0 && _reveal <= 0f))
                {
                    var closing = _revealDir < 0;
                    _revealDir = 0;
                    if (closing && _folding)
                    {
                        _folding = false;
                        if (_mapCtrl != null && _mapCtrl.Mode == HoloMapMode.HexBattle)
                            _mapCtrl.SetMode(HoloMapMode.System);
                        else
                            Hide();
                        if (_ongoingBattle > 0 && !_finished.Contains(_ongoingBattle))
                            SetRejoinVisible(true);
                        return;
                    }
                }
            }
            else if (_reveal >= 1f)
            {
                // Ships created after the unfold pop in at full size.
                foreach (var v in _ships.Values)
                    if (v.Root != null && v.DeathT < 0f && v.Root.transform.localScale.x < 1f)
                        v.Root.transform.localScale = Vector3.MoveTowards(v.Root.transform.localScale, Vector3.one,
                            Time.unscaledDeltaTime * 4f);
            }

            UpdateShipMotion();
            UpdateFx();

            if (_toast != null && _toast.text.Length > 0 && Time.unscaledTime > _toastUntil)
                _toast.text = string.Empty;

            if (_outcomeT >= 0f && _outcome != null)
            {
                _outcomeT += Time.unscaledDeltaTime;
                var k = MotionEase.SmoothOut(Mathf.Clamp01(_outcomeT / 0.5f));
                _outcome.transform.parent.localScale = Vector3.one * Mathf.Lerp(0.3f, 1f, k);
                _outcome.alpha = k;
            }
        }

        void UpdateShipMotion()
        {
            foreach (var v in _ships.Values)
            {
                if (v.Root == null)
                    continue;
                if (v.MoveT < 1f)
                {
                    v.MoveT = Mathf.Min(1f, v.MoveT + Time.unscaledDeltaTime / 0.6f);
                    var u = MotionEase.SmoothInOut(v.MoveT);
                    // A shallow hop over the board: the move reads as a manoeuvre, not a teleport.
                    var p = Vector3.Lerp(v.From, v.To, u) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 0.025f);
                    v.Root.transform.localPosition = p;
                }
            }
        }
    }
}
