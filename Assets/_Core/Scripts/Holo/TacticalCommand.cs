using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Holo
{
    /// <summary>
    /// Holo table v2 — point → point (docs/design/HOLOTABLE.md §2). Trigger (ray from afar, or a poke of the
    /// finger) on one of our ships selects it: a ring under it, every valid destination lights up. Aiming
    /// at a lit destination draws the ghost arc with the ETA; trigger again and the quote opens beside the
    /// target (<see cref="OrderConsole.AskAt"/>), then the same server orders as a drop
    /// (<see cref="HoloFleetOrders.Command"/>). Trigger on the ship again, or on empty space, clears the
    /// selection. The grip still grabs and drags a ship (secondary gesture).
    /// Reads the XRI Near-Far / Poke interactors of the rig; the trigger is XRI "Activate" (grip = select).
    /// </summary>
    public sealed class TacticalCommand : MonoBehaviour
    {
        const float RayLength = 4f;
        const float PokeRadius = 0.025f;
        static readonly Color Valid = new(0.45f, 1f, 0.6f, 1f);

        HoloZoneMap _map;
        FocusContext _focus;
        HoloFleetOrders _orders;
        CicArtKit _art;
        NearFarInteractor[] _rays = System.Array.Empty<NearFarInteractor>();
        XRPokeInteractor[] _pokes = System.Array.Empty<XRPokeInteractor>();
        readonly Dictionary<XRPokeInteractor, HoloToken> _touch = new();
        readonly List<GameObject> _cues = new();
        readonly RaycastHit[] _hits = new RaycastHit[16];
        readonly Collider[] _overlap = new Collider[8];
        float _nextScan;

        int _selectedId;
        HoloToken _hover;
        GameObject _selRing;
        LineRenderer _arc;
        TextMeshPro _arcLabel;
        Transform _arcLabelRoot;
        string _lastReadout;

#if UNITY_EDITOR
        public static HoloToken EditorAim;
#endif

        public static TacticalCommand Build(Transform host, HoloZoneMap map, FocusContext focus, HoloFleetOrders orders,
            CicArtKit art)
        {
            var go = new GameObject("TacticalCommand");
            go.transform.SetParent(host, false);
            var c = go.AddComponent<TacticalCommand>();
            c._map = map;
            c._focus = focus;
            c._orders = orders;
            c._art = art;
            c.BuildVisuals();
            if (map != null)
                map.TokensRebuilt += c.OnTokensRebuilt;
            return c;
        }

        void OnDestroy()
        {
            if (_map != null)
                _map.TokensRebuilt -= OnTokensRebuilt;
        }

        void BuildVisuals()
        {
            var ringTex = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;
            _selRing = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _selRing.name = "SelectionRing";
            _selRing.transform.SetParent(transform, false);
            Destroy(_selRing.GetComponent<Collider>());
            _selRing.GetComponent<MeshRenderer>().sharedMaterial = _art.RadarIcon(ringTex, new Color(0.4f, 1f, 1f, 1f));
            var spin = _selRing.AddComponent<HoloSpin>();
            spin.DegreesPerSecond = 60f;
            spin.BobMeters = 0f;
            _selRing.SetActive(false);

            var arcGo = new GameObject("OrderArc");
            arcGo.transform.SetParent(transform, false);
            _arc = arcGo.AddComponent<LineRenderer>();
            _arc.positionCount = 24;
            _arc.useWorldSpace = true;
            _arc.widthMultiplier = 0.005f;
            _arc.numCapVertices = 2;
            _arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _arc.sharedMaterial = _art.Lit(Texture2D.whiteTexture, Valid, 2.6f);
            _arc.enabled = false;
            _arcLabelRoot = new GameObject("ArcLabel").transform;
            _arcLabelRoot.SetParent(transform, false);
            _arcLabelRoot.gameObject.AddComponent<BillboardFace>();
            _arcLabel = UiKit.Label(_arcLabelRoot, "Text", string.Empty, Vector3.zero, 0.6f, 0.022f, UiKit.TextBright);
            _arcLabel.richText = true;
            _arcLabel.outlineWidth = 0.2f;
            _arcLabel.outlineColor = new Color32(2, 10, 16, 230);
            _arcLabelRoot.gameObject.SetActive(false);
        }

        // ── Loop ──────────────────────────────────────────────────────────────────

        void Update()
        {
            if (_map == null)
                return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _rays = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
                _pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsSortMode.None);
            }

            var console = OrderConsole.Instance;
            var busy = (_orders != null && _orders.Busy) || (console != null && console.IsOpen);

            // Aim: the first ray that points at a token (ignoring rays already on a UI panel).
            HoloToken aimed = null;
            NearFarInteractor clicker = null;
            foreach (var ray in _rays)
            {
                if (ray == null || !ray.isActiveAndEnabled)
                    continue;
                var t = RayToken(ray, out var onUi);
                if (onUi)
                    continue;
                if (t != null && aimed == null)
                    aimed = t;
                if (!busy && ray.activateInput.ReadWasPerformedThisFrame())
                {
                    clicker = ray;
                    Click(t);
                    break;
                }
            }

            if (clicker == null && !busy)
                PokeClicks();

#if UNITY_EDITOR
            // Editor checks (no headset): aim forced from a test script.
            if (EditorAim != null)
                aimed = EditorAim;
#endif
            SetHover(busy ? null : aimed);
            UpdateArc();
            FollowSelection();
        }

        HoloToken RayToken(NearFarInteractor ray, out bool onUi)
        {
            onUi = false;
            var origin = ray.transform.position;
            var dir = ray.transform.forward;
            var n = Physics.RaycastNonAlloc(origin, dir, _hits, RayLength, ~0, QueryTriggerInteraction.Collide);
            var best = float.MaxValue;
            HoloToken token = null;
            var blocker = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                var t = h.collider.GetComponentInParent<HoloToken>();
                if (t == null)
                {
                    // Solid room geometry in front stops the aim; triggers (sit zones…) don't.
                    if (!h.collider.isTrigger)
                        blocker = Mathf.Min(blocker, h.distance);
                    continue;
                }

                if (h.distance < best)
                {
                    best = h.distance;
                    token = t;
                }
            }

            if (token != null && blocker < best)
                token = null;
            if (ray.TryGetCurrentUIRaycastResult(out RaycastResult ui) && ui.isValid &&
                (token == null || ui.distance < best))
                onUi = true;
            return onUi ? null : token;
        }

        void PokeClicks()
        {
            foreach (var poke in _pokes)
            {
                if (poke == null || !poke.isActiveAndEnabled)
                    continue;
                var tip = poke.attachTransform != null ? poke.attachTransform.position : poke.transform.position;
                var n = Physics.OverlapSphereNonAlloc(tip, PokeRadius, _overlap, ~0, QueryTriggerInteraction.Collide);
                HoloToken touching = null;
                for (var i = 0; i < n && touching == null; i++)
                    touching = _overlap[i].GetComponentInParent<HoloToken>();
                _touch.TryGetValue(poke, out var before);
                if (touching != null && touching != before)
                    Click(touching);
                _touch[poke] = touching;
            }
        }

        // ── Selection ─────────────────────────────────────────────────────────────

        FocusFleet SelectedFleet => _selectedId > 0 ? _focus?.FindFleet(_selectedId) : null;

        HoloToken SelectedToken
        {
            get
            {
                if (_selectedId <= 0)
                    return null;
                foreach (var t in _map.Tokens)
                    if (t != null && t.Kind == HoloTokenKind.Fleet && t.Id == _selectedId)
                        return t;
                return null;
            }
        }

        void Click(HoloToken token)
        {
            if (token == null)
            {
                if (_selectedId > 0)
                    Deselect();
                return;
            }

            if (token.Kind == HoloTokenKind.Fleet && token.Owned)
            {
                if (token.Id == _selectedId)
                    Deselect();
                else
                    Select(token);
                return;
            }

            var fleet = SelectedFleet;
            if (fleet == null)
            {
                // Nothing selected: inspecting a world / foreign ship names it.
                HoloZoneMap.SetTokenLabelVisible(token, true);
                Readout(token.DisplayName + "  ·  " + Trans.Get("vr.table.pickShip"));
                CicCue.Hover(token.transform.position);
                return;
            }

            var why = Invalid(fleet, token);
            if (why != null)
            {
                CicCue.Fail(token.transform.position);
                Readout(why);
                return;
            }

            AsyncTap.Run(Issue(token));
        }

        void Select(HoloToken token)
        {
            _selectedId = token.Id;
            CicCue.Ok(token.transform.position);
            var fleet = SelectedFleet;
            if (fleet != null && !fleet.CanIssueMove(FleetOrderGate.UnixNow()))
                Readout(token.DisplayName + "  ·  " + Trans.Get(FleetOrderGate.BusyKey(fleet)));
            else
                Readout(Trans.Format("vr.table.selected", token.DisplayName));
            ShowCues();
        }

        void Deselect()
        {
            _selectedId = 0;
            ClearCues();
            _selRing.SetActive(false);
            _arc.enabled = false;
            _arcLabelRoot.gameObject.SetActive(false);
            Readout(Trans.Get("vr.table.pickShip"));
        }

        async Task Issue(HoloToken target)
        {
            var ship = SelectedToken;
            if (ship == null || _orders == null)
                return;
            _arc.enabled = false;
            _arcLabelRoot.gameObject.SetActive(false);
            await _orders.Command(ship, target, dragged: false);
            Deselect();
        }

        /// <summary>Why the selected ship cannot go there (null = valid), as the server would refuse it.</summary>
        string Invalid(FocusFleet fleet, HoloToken target)
        {
            var now = FleetOrderGate.UnixNow();
            switch (target.Kind)
            {
                case HoloTokenKind.Anomaly:
                    var a = AnomalyService.Instance?.Find(target.Id);
                    if (a == null)
                        return Trans.Get("anomaly_not_found");
                    if (!AnomalyService.HasScanner(fleet))
                        return Trans.Get("fleet_lacks_science_module");
                    if (fleet.SystemId != a.SystemId || fleet.IsMoving(now))
                        return Trans.Get("fleet_not_in_system");
                    return null;
                case HoloTokenKind.Planet:
                case HoloTokenKind.Asteroid:
                    if (!fleet.CanIssueMove(now))
                        return Trans.Get(FleetOrderGate.BusyKey(fleet));
                    if (target.Kind == HoloTokenKind.Planet && fleet.PlanetId == target.Id)
                        return Trans.Get("vr.table.alreadyThere");
                    if (target.Kind == HoloTokenKind.Asteroid && fleet.AsteroidId == target.Id)
                        return Trans.Get("vr.table.alreadyThere");
                    return null;
                case HoloTokenKind.System:
                    if (target.Slot < 0 || target.Id == fleet.SystemId)
                        return Trans.Get("vr.table.alreadyThere");
                    return fleet.CanIssueMove(now) ? null : Trans.Get(FleetOrderGate.BusyKey(fleet));
                default:
                    return Trans.Get("vr.table.notATarget");
            }
        }

        // ── Visual feedback ───────────────────────────────────────────────────────

        void ShowCues()
        {
            ClearCues();
            var fleet = SelectedFleet;
            if (fleet == null)
                return;
            var tex = _art.OrbitRing != null ? _art.OrbitRing : Texture2D.whiteTexture;
            var mat = _art.RadarIcon(tex, new Color(Valid.r, Valid.g, Valid.b, 0.85f));
            foreach (var t in _map.Tokens)
            {
                if (t == null || t.Kind == HoloTokenKind.Fleet || Invalid(fleet, t) != null)
                    continue;
                var cue = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cue.name = "TargetCue";
                Destroy(cue.GetComponent<Collider>());
                cue.transform.SetParent(t.transform, false);
                cue.transform.localPosition = Vector3.zero;
                cue.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var size = t.Kind == HoloTokenKind.System ? 0.05f : 0.11f;
                cue.transform.localScale = Vector3.one * size;
                cue.GetComponent<MeshRenderer>().sharedMaterial = mat;
                var spin = cue.AddComponent<HoloSpin>();
                spin.DegreesPerSecond = -35f;
                spin.BobMeters = 0f;
                _cues.Add(cue);
            }
        }

        void ClearCues()
        {
            foreach (var c in _cues)
                if (c != null)
                    Destroy(c);
            _cues.Clear();
        }

        void SetHover(HoloToken token)
        {
            if (token == _hover)
                return;
            if (_hover != null)
            {
                _hover.transform.localScale = Vector3.one;
                if (!_hover.Owned && !(_hover.Kind == HoloTokenKind.Planet && _hover.DisplayName.Length > 0 &&
                                       _hover.transform.Find("OrbitalStation") != null))
                    HoloZoneMap.SetTokenLabelVisible(_hover, false);
            }

            _hover = token;
            if (_hover == null)
                return;
            _hover.transform.localScale = Vector3.one * 1.2f;
            HoloZoneMap.SetTokenLabelVisible(_hover, true);
            CicCue.Hover(_hover.transform.position);
        }

        void FollowSelection()
        {
            var ship = SelectedToken;
            if (ship == null)
            {
                if (_selectedId > 0 && _selRing.activeSelf)
                    _selRing.SetActive(false);
                return;
            }

            // Follows the ship without being its child (tokens are rebuilt on polls).
            var k = ship.transform.lossyScale.x;
            _selRing.transform.position = ship.transform.position + Vector3.down * (0.004f * k);
            _selRing.transform.rotation = Quaternion.Euler(90f, _selRing.transform.eulerAngles.y, 0f);
            _selRing.transform.localScale = Vector3.one * (0.09f * k);
            _selRing.SetActive(true);
        }

        /// <summary>Ghost arc from the selected ship to the aimed valid destination, with the quote on it.</summary>
        void UpdateArc()
        {
            var fleet = SelectedFleet;
            var ship = SelectedToken;
            var target = _hover;
            if (fleet == null || ship == null || target == null || target == ship || Invalid(fleet, target) != null)
            {
                _arc.enabled = false;
                _arcLabelRoot.gameObject.SetActive(false);
                return;
            }

            var a = ship.transform.position;
            var b = target.transform.position;
            var lift = Mathf.Clamp(Vector3.Distance(a, b) * 0.35f, 0.03f, 0.25f);
            for (var i = 0; i < _arc.positionCount; i++)
            {
                var t = i / (float)(_arc.positionCount - 1);
                _arc.SetPosition(i, Vector3.Lerp(a, b, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * lift));
            }

            _arc.enabled = true;
            _arcLabelRoot.gameObject.SetActive(true);
            _arcLabelRoot.position = Vector3.Lerp(a, b, 0.5f) + Vector3.up * (lift + 0.03f);
            _arcLabel.text = Quote(fleet, target);
        }

        static string Quote(FocusFleet fleet, HoloToken target)
        {
            var name = string.IsNullOrEmpty(target.DisplayName) ? target.Kind.ToString() : target.DisplayName;
            switch (target.Kind)
            {
                case HoloTokenKind.Anomaly:
                    return name + "  <color=#b9a4ff>" + Trans.Get("scanAnomaly") + "</color>";
                case HoloTokenKind.Planet:
                case HoloTokenKind.Asteroid:
                    // Server intra-system travel: 1200 / speed.
                    return name + "  <color=#7dffa0>" + TravelPlanner.TimeText(1200f / Mathf.Max(1f, fleet.Speed)) +
                           "</color>";
                default:
                    return name;
            }
        }

        void Readout(string text)
        {
            _lastReadout = text;
            _map?.SetReadout(text);
        }

        void OnTokensRebuilt()
        {
            // The table redraws on polls: keep the selection by id and re-light its destinations.
            if (_selectedId > 0)
            {
                if (SelectedToken == null)
                {
                    Deselect();
                    return;
                }

                ShowCues();
                if (!string.IsNullOrEmpty(_lastReadout))
                    _map.SetReadout(_lastReadout);
            }
            else
            {
                Readout(Trans.Get("vr.table.pickShip"));
            }

            _hover = null;
        }
    }
}
