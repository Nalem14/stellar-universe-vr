using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using Core.Vfx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
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
        static readonly Color Queued = new(1f, 0.72f, 0.32f, 1f);
        bool _offering;

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

        public static TacticalCommand Instance { get; private set; }

        /// <summary>Our ship picked on the table (0 = none).</summary>
        public int SelectedFleetId => _selectedId;

        /// <summary>Destination under the aim while a ship is selected (null = none).</summary>
        /// <summary>The token under the captain's pointer on the table (any kind, with or without a selection).</summary>
        public HoloToken Hovered => _hover;

        /// <summary>The order the selected ship would get on <paramref name="target"/> (ETA, reserves), for other displays.</summary>
        public string PreviewFor(HoloToken target) =>
            SelectedFleet != null && target != null && Invalid(SelectedFleet, target) == null ? Quote(SelectedFleet, target) : null;

        public HoloToken AimedTarget => _selectedId > 0 && _hover != null && _hover.Kind != HoloTokenKind.Fleet ? _hover : null;

        /// <summary>Selection or aimed target changed (the queue path and the exterior beacons follow).</summary>
        public event System.Action Changed;

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
            c._reticle = HoloAimReticle.Build(go.transform, art);
            Instance = c;
            if (map != null)
                map.TokensRebuilt += c.OnTokensRebuilt;
            return c;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
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
            // A second line (asteroid reserves) grows upward instead of being truncated.
            _arcLabel.enableAutoSizing = false;
            _arcLabel.fontSize = _arcLabel.fontSizeMax;
            _arcLabel.overflowMode = TextOverflowModes.Overflow;
            _arcLabel.alignment = TextAlignmentOptions.Bottom;
            _arcLabel.outlineWidth = 0.2f;
            _arcLabel.outlineColor = new Color32(2, 10, 16, 230);
            _arcLabelRoot.gameObject.SetActive(false);
        }

        // ── Loop ──────────────────────────────────────────────────────────────────

        // Aim assist (all angles in degrees, from the steadied ray): the target is the token / star with the
        // smallest angle to the ray once its own angular radius is counted, within a small cone around it.
        /// <summary>Assist cone past a target's edge.</summary>
        const float ConeDeg = 2.2f;
        /// <summary>Smallest angular radius a target gets (tiny tokens at zoom-out, far side of the table).</summary>
        const float MinAngleDeg = 0.75f;
        /// <summary>The target held keeps its score × this (hysteresis: no flicker between neighbours).</summary>
        const float Sticky = 0.7f;
        /// <summary>Aim slipping off for this long keeps the target (s).</summary>
        const float LoseGrace = 0.09f;
        /// <summary>Trigger dip: a target held ≥ <see cref="ClickSteady"/> s and left &lt; this (s) before the click wins.</summary>
        const float ClickMemory = 0.1f;
        const float ClickSteady = 0.25f;
        /// <summary>Grip assist (grab a ship the ray only skims): tighter than the hover assist.</summary>
        const float GripScore = 0.55f;
        static readonly Color AimNeutral = new(0.35f, 0.95f, 1f, 1f);
        static readonly Color AimInvalid = new(1f, 0.38f, 0.32f, 1f);

        readonly List<HoloAimRay> _aims = new();
        HoloAimReticle _reticle;
        HoloAimRay _primary;
        int _tintKey = -1;
        int _tintSel = -1;
        Color _tint = AimNeutral;

        void Update()
        {
            if (_map == null)
                return;
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 2f;
                _rays = FindObjectsByType<NearFarInteractor>(FindObjectsSortMode.None);
                _pokes = FindObjectsByType<XRPokeInteractor>(FindObjectsSortMode.None);
                SyncAims();
            }

            var console = OrderConsole.Instance;
            // The Nova-finish offer that pops on picking a ship under way does not hold the table: pointing at a
            // target dismisses it and queues the next order instead.
            var busy = (_orders != null && _orders.Busy) || (console != null && console.IsOpen && !_offering);
            var now = Time.unscaledTime;
            var live = _map.ContentRoot != null && _map.ContentRoot.gameObject.activeInHierarchy;

            // Aim: every ray steadied and snapped; the hover is the best-snapped one (the one already hovering
            // wins ties, so two hands on the table do not fight).
            // Stars the rays hold keep their pooled tokens (another ray's pick never recycles them).
            _map.HoldGalaxyTokens(_aims.Count > 0 ? _aims[0].Target : null, _aims.Count > 1 ? _aims[1].Target : null);
            HoloAimRay best = null;
            var bestScore = float.MaxValue;
            HoloAimRay clicker = null;
            HoloToken clicked = null;
            for (var i = 0; i < _aims.Count; i++)
            {
                var aim = _aims[i];
                var ray = aim.Ray;
                if (ray == null || !ray.isActiveAndEnabled || !live)
                {
                    aim.SetTarget(null, 0f, now);
                    aim.HasPoint = false;
                    aim.OnUi = false;
                    continue;
                }

                aim.Trigger = ray.activateInput.ReadValue();
                aim.Filter(ray.transform.position, ray.transform.forward, now);
                AimRay(aim, ray, now);
                if (aim.OnUi)
                    continue;
                if (aim.Target != null)
                {
                    var sc = aim.Score * (aim == _primary ? 0.8f : 1f);
                    if (sc < bestScore)
                    {
                        bestScore = sc;
                        best = aim;
                    }
                }

                if (busy)
                    continue;
                if (clicker == null && ray.activateInput.ReadWasPerformedThisFrame())
                {
                    clicker = aim;
                    clicked = aim.ClickTarget(now, ClickMemory, ClickSteady);
                }

                AssistGrab(aim, ray);
            }

            _primary = best;
            if (clicker != null)
                Click(clicked);
            else if (!busy)
                PokeClicks();

            var aimed = best != null ? best.Target : null;
#if UNITY_EDITOR
            // Editor checks (no headset): aim forced from a test script.
            if (EditorAim != null)
                aimed = EditorAim;
#endif
            SetHover(busy ? null : aimed);
            UpdateArc();
            FollowSelection();
            UpdateReticle(busy || !live, best);
        }

        /// <summary>One aim state per ray, kept across scans (the find order is not stable).</summary>
        void SyncAims()
        {
            for (var i = 0; i < _aims.Count; i++)
            {
                if (_aims[i].Ray != null && System.Array.IndexOf(_rays, _aims[i].Ray) < 0)
                    _aims[i].Bind(null, Time.unscaledTime);
            }

            for (var r = 0; r < _rays.Length; r++)
            {
                var ray = _rays[r];
                HoloAimRay free = null;
                var known = false;
                for (var i = 0; i < _aims.Count && !known; i++)
                {
                    if (_aims[i].Ray == ray)
                        known = true;
                    else if (free == null && _aims[i].Ray == null)
                        free = _aims[i];
                }

                if (known)
                    continue;
                if (free == null)
                {
                    free = new HoloAimRay();
                    _aims.Add(free);
                }

                free.Bind(ray, Time.unscaledTime);
            }
        }

        /// <summary>
        /// Snap one steadied ray: room geometry, a lectern / panel or a queue waypoint in front win; else the
        /// token or galaxy star nearest the ray in angle inside the assist cone; and the map-plane point under it.
        /// </summary>
        void AimRay(HoloAimRay aim, NearFarInteractor ray, float now)
        {
            aim.OnUi = false;
            var origin = aim.Origin;
            var dir = aim.Dir;
            var n = Physics.RaycastNonAlloc(origin, dir, _hits, RayLength, ~0, QueryTriggerInteraction.Collide);
            var blocker = RayLength;
            var nodeDist = float.MaxValue;
            for (var i = 0; i < n; i++)
            {
                var h = _hits[i];
                if (h.collider.GetComponentInParent<HoloQueueNode>() != null)
                {
                    nodeDist = Mathf.Min(nodeDist, h.distance);
                    continue;
                }

                // Solid room geometry in front stops the aim; triggers (sit zones…) and tokens don't.
                if (!h.collider.isTrigger && h.collider.GetComponentInParent<HoloToken>() == null)
                    blocker = Mathf.Min(blocker, h.distance);
            }

            // A target may sit a little behind the surface the ray grazes (a ship over the plate rim).
            var reach = Mathf.Min(RayLength, blocker + 0.03f);
            var galaxy = _map.ShowingGalaxy;
            // Zoomed out, the galaxy's stars are pin-points: a wider cone (nearest-in-angle still decides).
            var cone = galaxy ? ConeDeg * Mathf.Lerp(1.45f, 1f, _map.GalaxyZoom01) : ConeDeg;
            var stickyKey = aim.TargetKey;
            var noPick = _selectedId <= 0;

            HoloToken token = null;
            var score = float.MaxValue;
            var along = 0f;
            var tokens = _map.Tokens;
            for (var i = 0; i < tokens.Count; i++)
            {
                var t = tokens[i];
                // Galaxy stars come from the star grid below (the pooled tokens are only a few of them).
                if (t == null || (galaxy && t.Kind == HoloTokenKind.System) || !t.gameObject.activeInHierarchy)
                    continue;
                var v = t.transform.position - origin;
                var a = Vector3.Dot(v, dir);
                if (a <= 0.02f || a > reach)
                    continue;
                var perp = (v - dir * a).magnitude;
                var ang = Mathf.Atan2(perp, a) * Mathf.Rad2Deg;
                var radius = _map.AimRadiusOf(t) * t.transform.lossyScale.x;
                var angR = Mathf.Max(Mathf.Atan2(radius, a) * Mathf.Rad2Deg, MinAngleDeg);
                var sc = ang / (angR + cone);
                if (sc >= 1.5f)
                    continue;
                if (HoloAimRay.KeyOf(t) == stickyKey)
                    sc *= Sticky;
                // Nothing picked yet: our own ships are what the captain reaches for first.
                if (noPick && t.Kind == HoloTokenKind.Fleet && t.Owned)
                    sc *= 0.9f;
                if (sc < 1f && sc < score)
                {
                    score = sc;
                    token = t;
                    along = a;
                }
            }

            if (galaxy)
            {
                const int sysTag = (int)HoloTokenKind.System + 1;
                var stickyStar = (stickyKey >> 24) == sysTag ? stickyKey ^ (sysTag << 24) : 0;
                var idx = _map.GalaxyPickRay(origin, dir, reach, cone, MinAngleDeg, stickyStar, Sticky, out var sc,
                    out var starWorld);
                if (idx >= 0 && sc < score)
                {
                    var star = _map.GalaxyTokenForStar(idx);
                    if (star != null)
                    {
                        token = star;
                        score = sc;
                        along = Vector3.Dot(starWorld - origin, dir);
                    }
                }
            }

            // Slipped off for a moment: hold the target (a tremor, a token bobbing out of the cone).
            if (token == null && aim.Target != null && aim.Target.isActiveAndEnabled &&
                HoloAimRay.KeyOf(aim.Target) == aim.TargetKey && now - aim.LastSeen < LoseGrace)
            {
                aim.HasPoint = _map.AimPlanePoint(origin, dir, reach, out aim.Point, out aim.PointLocal);
                return;
            }

            var front = token != null ? along : reach;
            // A queue waypoint in front of the aim: that trigger belongs to the waypoint (QueuePathView).
            if (nodeDist < blocker && nodeDist < front)
                aim.OnUi = true;
            // A lectern / panel canvas in front (the XRI UI ray).
            if (ray.TryGetCurrentUIRaycastResult(out RaycastResult ui) && ui.isValid && ui.distance < front)
                aim.OnUi = true;
            if (aim.OnUi)
            {
                aim.SetTarget(null, 0f, now);
                aim.HasPoint = false;
                return;
            }

            aim.SetTarget(token, score, now);
            aim.HasPoint = _map.AimPlanePoint(origin, dir, reach, out aim.Point, out aim.PointLocal);
        }

        /// <summary>
        /// Grip while the assist holds one of our ships the XRI ray only skims: grab it as if the ray were on it
        /// (HoloFleetOrders takes the drag from there). A grip that already hovers something is left to XRI.
        /// </summary>
        void AssistGrab(HoloAimRay aim, NearFarInteractor ray)
        {
            var t = aim.Target;
            if (t == null || t.Kind != HoloTokenKind.Fleet || !t.Owned || aim.Score > GripScore || ray.hasSelection ||
                !ray.selectInput.ReadWasPerformedThisFrame() || ray.interactablesHovered.Count > 0 ||
                _map.InteractionLocked || ray.interactionManager == null)
                return;
            var grab = t.GetComponent<XRGrabInteractable>();
            if (grab == null || !grab.isActiveAndEnabled || grab.isSelected)
                return;
            ray.interactionManager.SelectEnter((IXRSelectInteractor)ray, (IXRSelectInteractable)grab);
        }

        /// <summary>Lock ring on the hover, a crosshair under each ray on the plate, galaxy coordinates.</summary>
        void UpdateReticle(bool hidden, HoloAimRay primary)
        {
            if (_reticle == null)
                return;
            if (hidden || _map.InteractionLocked)
            {
                _reticle.HideAll();
                return;
            }

            var cam = Camera.main;
            var eye = cam != null ? cam.transform.position : transform.position + Vector3.up;
            var target = _hover;
            var flat = _map.ContentRoot != null ? _map.ContentRoot.rotation : Quaternion.identity;

            if (target != null)
            {
                var key = HoloAimRay.KeyOf(target);
                if (key != _tintKey || _selectedId != _tintSel)
                {
                    _tintKey = key;
                    _tintSel = _selectedId;
                    _tint = AimTint(target);
                }

                var pos = target.transform.position;
                var dist = Vector3.Distance(eye, pos);
                var radius = Mathf.Max(_map.AimRadiusOf(target) * target.transform.lossyScale.x,
                    dist * Mathf.Tan(1.1f * Mathf.Deg2Rad));
                var snapped = primary != null && primary.Target == target && primary.ChangedAt >= Time.unscaledTime;
                _reticle.ShowRing(pos, radius, _tint, snapped, eye);
            }
            else
            {
                _tintKey = -1;
                _reticle.HideRing();
            }

            var slot = 0;
            var coordsShown = false;
            for (var i = 0; i < _aims.Count && slot < 2; i++)
            {
                var aim = _aims[i];
                if (aim.Ray == null || !aim.HasPoint || aim.OnUi)
                    continue;
                var dist = Vector3.Distance(eye, aim.Point);
                var size = Mathf.Max(0.009f, dist * Mathf.Tan(0.9f * Mathf.Deg2Rad));
                var mine = aim == primary && target != null;
                _reticle.ShowPoint(slot, aim.Point, flat, size, mine ? _tint : AimNeutral);
                if (mine && aim.Target != null)
                {
                    var to = aim.Target.transform.position;
                    if ((to - aim.Point).sqrMagnitude > size * size)
                        _reticle.ShowTether(aim.Point, to, _tint);
                    else
                        _reticle.HideTether();
                }

                // Galaxy, nothing snapped under this ray: where it points, in grid coordinates.
                if (!coordsShown && _map.ShowingGalaxy && aim.Target == null)
                {
                    _reticle.ShowCoords(aim.Point, _map.GalaxyGridAt(aim.PointLocal), size * 1.6f);
                    coordsShown = true;
                }

                slot++;
            }

            for (var i = slot; i < 2; i++)
                _reticle.HidePoint(i);
            if (target == null || primary == null || !primary.HasPoint)
                _reticle.HideTether();
            if (!coordsShown)
                _reticle.HideCoords();
        }

        /// <summary>Ring colour: cyan to inspect / pick, green (amber when it queues) for a valid order, red for a refusal.</summary>
        Color AimTint(HoloToken target)
        {
            var fleet = SelectedFleet;
            if (fleet == null || target.Kind == HoloTokenKind.Fleet)
                return AimNeutral;
            if (Invalid(fleet, target) != null)
                return AimInvalid;
            return target.Kind != HoloTokenKind.Anomaly && !fleet.IsStation && !fleet.CanIssueMove(FleetOrderGate.UnixNow()) ? Queued : Valid;
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
                // Galaxy, a ship already picked: pointing at another of ours elsewhere means "go where it is"
                // (its token sits on its star and hides it), not "pick that one instead".
                var picked = SelectedFleet;
                var other = _focus?.FindFleet(token.Id);
                if (picked != null && other != null && token.Id != _selectedId && _map.ShowingGalaxy)
                {
                    var now = FleetOrderGate.UnixNow();
                    var there = other.IsMoving(now) && other.DestSystemId > 0 ? other.DestSystemId : other.SystemId;
                    var here = picked.IsMoving(now) && picked.DestSystemId > 0 ? picked.DestSystemId : picked.SystemId;
                    var star = there != here ? _map.GalaxyTargetForSystem(there) : null;
                    if (star != null)
                    {
                        ClickTarget(star);
                        return;
                    }
                }

                if (token.Id == _selectedId)
                    Deselect();
                else
                    Select(token);
                return;
            }

            ClickTarget(token);
        }

        /// <summary>A world, rock, star or foreign ship clicked: the order for the picked ship (or its name).</summary>
        void ClickTarget(HoloToken token)
        {
            var fleet = SelectedFleet;
            if (fleet == null && token.Kind == HoloTokenKind.System && _map.ShowingGalaxy)
            {
                // A star, no ship picked: what to do with it (fly the view there, or send one of ours).
                AsyncTap.Run(StarMenu(token));
                return;
            }

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
            Changed?.Invoke();
            CicCue.Ok(token.transform.position);
            var fleet = SelectedFleet;
            if (fleet != null && fleet.IsStation)
                Readout(token.DisplayName + "  ·  " + Trans.Get("stationCannotMove"));
            else if (fleet != null && !fleet.CanIssueMove(FleetOrderGate.UnixNow()))
                Readout(token.DisplayName + "  ·  " + Trans.Get(FleetOrderGate.BusyKey(fleet)) + "  ·  " +
                        Trans.Get("vr.queue.busyHint"));
            else
                Readout(Trans.Format("vr.table.selected", token.DisplayName));
            ShowCues();
            if (TravelSpeedup.Offered(fleet))
                AsyncTap.Run(OfferSpeedup(token, fleet));
            else if (JumpgateNetwork.Origin(fleet) is { } origin)
                AsyncTap.Run(LoadGates(token.Id, origin));
        }

        /// <summary>Under way: the lectern opens beside the ship with the Nova finish (Cancel = keep flying).</summary>
        async Task OfferSpeedup(HoloToken token, FocusFleet fleet)
        {
            _offering = true;
            bool sent;
            ApiResult result;
            try
            {
                (sent, result) = await TravelSpeedup.AskAndSend(fleet, token != null ? token.transform.position : null);
            }
            finally
            {
                _offering = false;
            }

            if (!sent)
                return;
            var name = string.IsNullOrEmpty(fleet.Name) ? "#" + fleet.Id : fleet.Name;
            Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Helm, "SpeedupFleetTravel", result, name);
            if (result.Ok)
                CicCue.Ok(transform.position);
            else
                CicCue.Fail(transform.position);
            Readout(result.Ok ? name + "  ·  " + Trans.Get("speedupFleetSuccess")
                : string.IsNullOrEmpty(result.Error) ? Trans.Get("vr.common.error") : result.Error);
            if (result.Ok && _orders != null)
                await _orders.PollNow();
        }

        /// <summary>Docked at a gate world: read the network, then light the gate worlds in violet.</summary>
        async Task LoadGates(int fleetId, Core.App.PlanetEconomy origin)
        {
            var list = await JumpgateNetwork.Destinations(origin.Id);
            if (_selectedId != fleetId)
                return;
            var fleet = SelectedFleet;
            var left = JumpgateNetwork.RechargeLeft(origin);
            var gate = list.Count == 0 ? Trans.Get("vr.jumpgate.none")
                : left > 0 ? Trans.Format("vr.jumpgate.recharging", TravelPlanner.TimeText(left))
                : Trans.Format("vr.jumpgate.ready", list.Count);
            if (fleet != null && fleet.CanIssueMove(FleetOrderGate.UnixNow()))
                Readout(Trans.Format("vr.table.selected", fleet.Name.Length > 0 ? fleet.Name : "#" + fleet.Id) + "  ·  " + gate);
            ShowCues();
        }

        void Deselect()
        {
            _selectedId = 0;
            Changed?.Invoke();
            ClearCues();
            _selRing.SetActive(false);
            _arc.enabled = false;
            _arcLabelRoot.gameObject.SetActive(false);
            Readout(Trans.Get("vr.table.pickShip"));
        }

        /// <summary>
        /// Star menu on the lectern: "zoom here", then up to three of our free ships to send there (each goes on
        /// to the usual travel choice with its modes and quotes).
        /// </summary>
        async Task StarMenu(HoloToken star)
        {
            var console = OrderConsole.Instance;
            if (console == null || _focus == null)
                return;
            var options = new List<OrderConsole.Option>
            {
                new(Trans.Get("vr.galaxy.zoomHere"), true, UiKit.Cyan, "zoom")
            };
            var now = FleetOrderGate.UnixNow();
            var me = FocusContext.OwnedUserId();
            foreach (var f in _focus.Fleets)
            {
                if (options.Count >= 4)
                    break;
                if (f == null || f.UserId != me || f.SystemId == star.Id || !f.CanIssueMove(now))
                    continue;
                var name = string.IsNullOrEmpty(f.Name) ? "#" + f.Id : f.Name;
                options.Add(new OrderConsole.Option(Trans.Format("vr.galaxy.send", name), true, UiKit.Amber, f.Id));
            }

            var choice = await console.AskAt(star.transform.position, star.DisplayName, options);
            if (choice is string s && s == "zoom")
            {
                _map.GalaxyFocus(star.Id, 1.8f);
                CicCue.Ok(star.transform.position);
                return;
            }

            if (!(choice is int fleetId) || _focus.FindFleet(fleetId) is not { } fleet)
                return;
            var (sent, result, barkAction) = await TravelPlanner.AskAndSend(fleet, star.Id, star.GalaxyX, star.GalaxyY, star.DisplayName,
                star.transform.position);
            if (!sent)
                return;
            Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Helm, barkAction, result, star.DisplayName);
            if (result.Ok)
                CicCue.Ok(star.transform.position);
            else
                CicCue.Fail(star.transform.position);
            Readout(result.Ok ? Trans.Get(result.NoticeKey ?? "vr.common.ok")
                : string.IsNullOrEmpty(result.Error) ? Trans.Get("vr.common.error") : result.Error);
            if (result.Ok && _orders != null)
                await _orders.PollNow();
        }

        async Task Issue(HoloToken target)
        {
            var ship = SelectedToken;
            if (ship == null || _orders == null)
                return;
            _arc.enabled = false;
            _arcLabelRoot.gameObject.SetActive(false);
            var sent = await _orders.Command(ship, target, dragged: false);
            // Chaining: the ship stays picked after an order, so the next target pointed at is its next queued
            // step (asteroid → harvest, then home → deposit…) without picking it again.
            if (sent && SelectedFleet is { } still && _focus.IsMine(still))
            {
                ShowCues();
                Readout(ship.DisplayName.Replace('\n', ' ') + "  ·  " + Trans.Get("vr.queue.chainHint"));
                return;
            }

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
                    if (target.Kind == HoloTokenKind.Asteroid && _focus?.FindAsteroid(target.Id) is { Gone: true })
                        return Trans.Get("asteroidDepleted");
                    // Anchored fortress: only its own planet, and only for what it can do there.
                    if (fleet.IsStation)
                        return target.Kind == HoloTokenKind.Planet && fleet.PlanetId == target.Id &&
                               HoloFleetOrders.HereOptions(fleet, target, _focus).Count > 0
                            ? null
                            : Trans.Get("stationCannotMove");
                    // Busy: valid — the order joins its queue (HoloFleetOrders.QueueOrders).
                    if (!fleet.CanIssueMove(now))
                        return null;
                    // Where the ship already is: valid when there is something to do on the spot.
                    if ((target.Kind == HoloTokenKind.Planet && fleet.PlanetId == target.Id) ||
                        (target.Kind == HoloTokenKind.Asteroid && fleet.AsteroidId == target.Id))
                        return HoloFleetOrders.HereOptions(fleet, target, _focus).Count > 0
                            ? null
                            : Trans.Get("vr.table.alreadyThere");
                    if (target.Kind == HoloTokenKind.Asteroid && _focus?.FindAsteroid(target.Id) is { Gone: true })
                        return Trans.Get("asteroidDepleted");
                    return null;
                case HoloTokenKind.System:
                    if (fleet.IsStation)
                        return Trans.Get("stationCannotMove");
                    if (target.Slot < 0 || target.Id == fleet.SystemId)
                        return Trans.Get("vr.table.alreadyThere");
                    // Busy: a queued moveToSystem step.
                    return null;
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
            // Busy ship: its targets take queued steps — amber rings (the queue's colour) instead of green.
            var tint = fleet.IsStation || fleet.CanIssueMove(FleetOrderGate.UnixNow()) ? Valid : Queued;
            var mat = _art.RadarIcon(tex, new Color(tint.r, tint.g, tint.b, 0.85f));
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

            ShowGateCues(fleet, tex);
        }

        readonly List<JumpgateDest> _gateMatch = new();
        Material _gateCueMat;

        /// <summary>Our other gate worlds (known once the network is read): a wider violet ring, turning the other way.</summary>
        void ShowGateCues(FocusFleet fleet, Texture tex)
        {
            var origin = JumpgateNetwork.Origin(fleet);
            if (origin == null || !JumpgateNetwork.TryCached(origin.Id, out var all) || all.Count == 0)
                return;
            if (_gateCueMat == null)
                _gateCueMat = _art.RadarIcon(tex, JumpgateNetwork.JumpTint);
            foreach (var t in _map.Tokens)
            {
                if (t == null || (t.Kind != HoloTokenKind.Planet && t.Kind != HoloTokenKind.System))
                    continue;
                JumpgateNetwork.Matching(all, t, _gateMatch);
                if (_gateMatch.Count == 0)
                    continue;
                var cue = GameObject.CreatePrimitive(PrimitiveType.Quad);
                cue.name = "GateCue";
                Destroy(cue.GetComponent<Collider>());
                cue.transform.SetParent(t.transform, false);
                cue.transform.localPosition = Vector3.up * 0.002f;
                cue.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                cue.transform.localScale = Vector3.one * (t.Kind == HoloTokenKind.System ? 0.075f : 0.16f);
                cue.GetComponent<MeshRenderer>().sharedMaterial = _gateCueMat;
                var spin = cue.AddComponent<HoloSpin>();
                spin.DegreesPerSecond = 50f;
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
            Changed?.Invoke();
            // Galaxy: a card over the aimed star (holder, worlds, our ships, trip time for the picked ship).
            if (_hover != null && _hover.Kind == HoloTokenKind.System && _map.ShowingGalaxy)
                _map.ShowStarCard(_hover, SelectedFleet);
            else
                _map.HideStarCard();
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
            // Busy: the pick is a queued step, not a flight — say so instead of an ETA.
            if (target.Kind != HoloTokenKind.Anomaly && !fleet.IsStation && !fleet.CanIssueMove(FleetOrderGate.UnixNow()))
                return name + "  <color=#ffb866>" + Trans.Get("addToQueue") + "</color>";
            switch (target.Kind)
            {
                case HoloTokenKind.Anomaly:
                    return name + "  <color=#b9a4ff>" + Trans.Get("scanAnomaly") + "</color>";
                case HoloTokenKind.Planet:
                case HoloTokenKind.Asteroid:
                    // Server intra-system travel: 1200 / speed.
                    var eta = name + "  <color=#7dffa0>" + TravelPlanner.TimeText(1200f / Mathf.Max(1f, fleet.Speed)) +
                              "</color>";
                    var reserves = target.Kind == HoloTokenKind.Asteroid
                        ? AsteroidService.Reserves(FocusContext.Current?.FindAsteroid(target.Id))
                        : string.Empty;
                    return reserves.Length > 0 ? eta + "\n<size=75%><color=#d8c6a3>" + reserves + "</color></size>" : eta;
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
