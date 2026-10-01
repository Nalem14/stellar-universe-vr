using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.UI;
using Core.Utils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Core.Vfx
{
    /// <summary>
    /// Grab owned fleet tokens; drop on planet/asteroid/system → MoveFleet*.
    /// Tabletop rules: trigger colliders only, XZ drop snap, rebuild lock while held.
    /// </summary>
    public class HoloFleetOrders : MonoBehaviour
    {
        /// <summary>Horizontal snap radius on the holo disc (meters).</summary>
        const float DropRadius = 0.16f;
        /// <summary>Stars sit a few cm apart on the galaxy: the target must be the one under the hand.</summary>
        const float GalaxyDropRadius = 0.09f;
        const float DragLift = 0.14f;

        HoloZoneMap _map;
        FocusContext _focus;
        FleetPoller _poller;
        HoloMapController _mapCtrl;
        readonly List<XRGrabInteractable> _grabs = new();
        bool _ordering;
        HoloToken _dragging;
        HoloToken _hoverHighlight;
        HoloToken _dropHighlight;
        HoloToken _stickyDrop;
        Vector3 _hoverBaseScale = Vector3.one;
        Vector3 _dropBaseScale = Vector3.one;
        Transform _dragAttach;
        Vector3 _dragGrabScale = Vector3.one;
        HoloOrderPreview _orderPreview;
        HoloToken _previewTarget;
        CicArtKit _art;

        Core.Holo.OrderConsole _console;

        /// <summary>Drops are quoted on this lectern and only sent on Confirm (docs/ROADMAP.md P4).</summary>
        public void BindConsole(Core.Holo.OrderConsole console) => _console = console;

        public void Bind(HoloZoneMap map, FocusContext focus, FleetPoller poller,
            HoloMapController mapCtrl = null)
        {
            _map = map;
            _focus = focus;
            _poller = poller;
            _mapCtrl = mapCtrl;
            if (_map != null)
            {
                _map.TokensRebuilt -= OnTokensRebuilt;
                _map.TokensRebuilt += OnTokensRebuilt;
                WireTokens();
            }
        }

        void OnDestroy()
        {
            if (_map != null)
            {
                _map.TokensRebuilt -= OnTokensRebuilt;
                _map.SetInteractionLock(false);
            }

            UnwireGrabs();
            ClearDropHighlight();
            ClearHoverHighlight();
            HideOrderPreview();
        }

        void OnTokensRebuilt()
        {
            // Never rewire mid-drag — rebuilds are deferred via SetInteractionLock.
            if (_dragging != null)
                return;
            HideOrderPreview();
            WireTokens();
        }

        void LateUpdate()
        {
            if (_dragging == null || _map == null)
                return;

            // Follow the hand above the plate — avoids clipping / ray steal by star mesh.
            if (_dragAttach != null)
            {
                var p = _dragAttach.position;
                var floorY = _map.VolumeRoot != null
                    ? _map.VolumeRoot.position.y + DragLift
                    : p.y;
                p.y = Mathf.Max(p.y, floorY);
                _dragging.transform.position = p;
                _dragging.transform.rotation = Quaternion.Euler(0f, _dragging.transform.eulerAngles.y, 0f);
            }

            var target = FindNearestDropTarget(_dragging.transform.position);
            if (target != null)
                _stickyDrop = target;

            UpdateOrderPreview(target);

            if (target != null)
            {
                _mapCtrl?.ShowMoveGhostWorld(_dragging.transform.position,
                    target.transform.position + Vector3.up * 0.04f);
                SetDropHighlight(target);
                var dest = string.IsNullOrEmpty(target.DisplayName)
                    ? target.Kind.ToString()
                    : target.DisplayName.Replace('\n', ' ');
                _map.SetReadout($"{_dragging.DisplayName.Replace('\n', ' ')} → {dest}");
            }
            else
            {
                _mapCtrl?.HideMoveGhost();
                ClearDropHighlight();
                _map.SetReadout($"{_dragging.DisplayName.Replace('\n', ' ')} → …");
            }
        }

        void HideOrderPreview(HoloToken fleet = null)
        {
            _previewTarget = null;
            var restore = fleet != null ? fleet : _dragging;
            if (restore != null && restore.Owned)
                HoloZoneMap.SetTokenLabelVisible(restore, true);
            if (_orderPreview == null)
                return;
            _orderPreview.Hide();
            if (_orderPreview.transform.parent != null)
                _orderPreview.transform.SetParent(null, true);
        }

        void UpdateOrderPreview(HoloToken target)
        {
            if (_dragging == null)
                return;
            if (_orderPreview == null)
            {
                if (_art == null)
                {
                    var env = FindFirstObjectByType<CicEnvironment>();
                    _art = env != null ? env.Art : null;
                    if (_art == null)
                    {
                        _art = new CicArtKit();
                        _art.Load();
                    }
                }

                _orderPreview = HoloOrderPreview.Ensure(_dragging.transform, _art);
            }

            if (target != _previewTarget)
            {
                _previewTarget = target;
                if (target != null)
                    CicCue.Hover(target.transform.position);
            }

            _orderPreview.ShowPending(_dragging, target);
        }

        void WireTokens()
        {
            UnwireGrabs();
            ClearHoverHighlight();
            ClearDropHighlight();
            if (_map == null)
                return;
            // From a virtual orbital station the table still commands every owned fleet around; the
            // station itself has no fleet token, so there is nothing of it to drag.

            foreach (var token in _map.Tokens)
            {
                if (token == null || token.Kind != HoloTokenKind.Fleet || !token.Owned)
                    continue;
                if (_mapCtrl != null && _mapCtrl.MovesLocked)
                    continue;
                // A ship at work (mining, surveying, sieging) stays in hand: its drop queues the next orders.
                // Under way or in battle it is not grabbed (its token glides / the hex fight owns it) — a
                // point → point pick on the table queues for it instead (TacticalCommand).
                var ship = _focus?.FindFleet(token.Id);
                if (ship == null ? token.Busy : ship.IsMoving(UnixNow()) || ship.IsInBattle)
                    continue;

                var col = EnsureGrabVolume(token.gameObject);
                var grab = token.GetComponent<XRGrabInteractable>();
                if (grab == null)
                    grab = token.gameObject.AddComponent<XRGrabInteractable>();

                // Only the grab volume — child meshes must not drive select/deselect.
                grab.colliders.Clear();
                if (col != null)
                    grab.colliders.Add(col);

                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.selectMode = InteractableSelectMode.Single;
                grab.throwOnDetach = false;
                grab.useDynamicAttach = true;
                grab.trackPosition = true;
                grab.trackRotation = false;
                // Prefer this token when rays skim the plate / neighboring tokens.
                grab.focusMode = InteractableFocusMode.Single;
                grab.distanceCalculationMode = XRBaseInteractable.DistanceCalculationMode.ColliderPosition;

                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
                grab.selectEntered.AddListener(OnSelectEntered);
                grab.selectExited.AddListener(OnSelectExited);
                grab.hoverEntered.AddListener(OnHoverEntered);
                grab.hoverExited.AddListener(OnHoverExited);
                _grabs.Add(grab);

                var spin = token.GetComponent<HoloSpin>();
                if (spin != null)
                    spin.enabled = true;
            }
        }

        static long UnixNow() =>
            (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

        void UnwireGrabs()
        {
            for (var i = 0; i < _grabs.Count; i++)
            {
                var grab = _grabs[i];
                if (grab == null)
                    continue;
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
                grab.hoverEntered.RemoveListener(OnHoverEntered);
                grab.hoverExited.RemoveListener(OnHoverExited);
            }

            _grabs.Clear();
        }

        /// <summary>
        /// Large non-trigger grab volume. XR Ray Interactors default to Ignore triggers,
        /// so a trigger-only fleet was invisible to the ray — grab felt impossible.
        /// </summary>
        static Collider EnsureGrabVolume(GameObject go)
        {
            // Strip any prior root colliders (PlaceFleet box was trigger-only).
            var existing = go.GetComponents<Collider>();
            for (var i = 0; i < existing.Length; i++)
            {
                if (Application.isPlaying)
                    UnityEngine.Object.Destroy(existing[i]);
                else
                    UnityEngine.Object.DestroyImmediate(existing[i]);
            }

            // Aim assist sized to the diorama ship (~8 cm): neighbours no longer steal the ray.
            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.06f;
            sphere.center = Vector3.zero;
            sphere.isTrigger = false;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return sphere;
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            if (token == null || _dragging != null)
                return;
            SetHoverHighlight(token);
            HoloZoneMap.SetTokenLabelVisible(token, true);
            // The status strip belongs to TacticalCommand (point → point); hover only lights the token.
            CicCue.Hover(token.transform.position);
        }

        static void BrightenLabel(HoloToken token, bool on)
        {
            HoloZoneMap.SetTokenLabelVisible(token, on || token.Owned);
            var label = token.transform.Find("Label");
            if (label == null || !label.gameObject.activeSelf)
                return;
            var tmp = label.GetComponentInChildren<TMPro.TMP_Text>();
            if (tmp == null)
                return;
            label.localScale = on ? Vector3.one * 1.3f : Vector3.one;
        }

        void OnHoverExited(HoverExitEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            if (token != null && token == _hoverHighlight)
                ClearHoverHighlight();
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            if (token == null)
                return;
            ClearHoverHighlight();
            _dragging = token;
            _stickyDrop = null;
            _dragAttach = args.interactorObject.transform;
            _map?.SetInteractionLock(true);
            var spin = token.GetComponent<HoloSpin>();
            if (spin != null)
                spin.enabled = false;
            _dragGrabScale = token.transform.localScale;
            token.transform.localScale = _dragGrabScale * 1.2f;
            var name = string.IsNullOrEmpty(token.DisplayName) ? "ship " + token.Id : token.DisplayName;
            _map?.SetReadout($"{name} → …");
            CicCue.Ok(token.transform.position);
            Core.Audio.HoloHum.Excite(0.8f);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();

            // Spurious deselect while grip still held (ray stole hover over star/plate).
            if (_dragging != null && token == _dragging && SelectStillHeld(args.interactorObject))
            {
                var grab = token.GetComponent<XRGrabInteractable>();
                var manager = grab != null ? grab.interactionManager : null;
                if (manager != null && grab != null)
                {
                    manager.SelectEnter(args.interactorObject, (IXRSelectInteractable)grab);
                    return;
                }
            }

            _mapCtrl?.HideMoveGhost();
            ClearDropHighlight();
            HideOrderPreview(token);
            _dragging = null;
            _dragAttach = null;
            if (token == null || _ordering)
            {
                _map?.SetInteractionLock(false);
                _stickyDrop = null;
                return;
            }

            token.transform.localScale = Vector3.one;
            var sticky = _stickyDrop;
            _stickyDrop = null;
            Core.Utils.AsyncTap.Run(ResolveDrop(token, sticky));
        }

        static bool SelectStillHeld(IXRSelectInteractor interactor)
        {
            if (interactor == null)
                return false;
            try
            {
                return interactor.isSelectActive;
            }
            catch
            {
                return false;
            }
        }

        async Task ResolveDrop(HoloToken fleetToken, HoloToken stickyPreferred)
        {
            _ordering = true;
            try
            {
                var target = FindNearestDropTarget(fleetToken.transform.position);
                if (target == null && stickyPreferred != null)
                    target = stickyPreferred;
                if (target == null)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(Trans.Get("CommandBridge"));
                    return;
                }

                await Command(fleetToken, target, dragged: true);
            }
            finally
            {
                _ordering = false;
                _map?.SetInteractionLock(false);
            }
        }

        /// <summary>A ship selected on the table (point → point, or dropped by hand) is ordered to a target.</summary>
        public bool Busy => _ordering;

        /// <summary>Follow-up read after an order sent from elsewhere on the table (speedup…).</summary>
        public Task PollNow() => _poller != null ? _poller.PollNow() : Task.CompletedTask;

        public async Task<bool> Command(HoloToken fleetToken, HoloToken target, bool dragged)
        {
            var nested = _ordering && dragged;
            _ordering = true;
            _map?.SetInteractionLock(true);
            try
            {
                if (_focus == null || !AuthManager.Ensure().IsLoggedIn)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(Trans.Get("error_not_logged_in"));
                    return false;
                }

                if (target.Kind == HoloTokenKind.Anomaly)
                {
                    await ScanDrop(fleetToken, target);
                    return false;
                }

                // Busy (mining, surveying, sieging, under way): nothing flies now, but every target still takes
                // queued steps — the server runs them in order once the ship is idle (ProcessFleetQueue).
                var here = _focus.FindFleet(fleetToken.Id);
                if (here != null && !here.CanIssueMove(UnixNow()))
                    return await QueueOrders(fleetToken, here, target);

                // The world (or rock field) the ship is already at: what it can do there, not a move.
                if (here != null && IsAt(here, target))
                    return await HereOrders(fleetToken, here, target);

                string action;
                var query = new Dictionary<string, string>
                {
                    { "fleet", fleetToken.Id.ToString() }
                };

                if (target.Kind == HoloTokenKind.Planet)
                {
                    action = "MoveFleetToPlanet";
                    query["planet"] = target.Id.ToString();
                }
                else if (target.Kind == HoloTokenKind.Asteroid)
                {
                    action = "MoveFleetToAsteroid";
                    query["asteroid"] = target.Id.ToString();
                }
                else if (target.Kind == HoloTokenKind.System)
                {
                    action = "MoveFleetToSystem";
                    query["pos"] = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "{0}.{1}", target.GalaxyX, target.GalaxyY);
                    // The star by id (preferred by the server): stale coordinates can never misroute it.
                    if (target.Id > 0)
                        query["system"] = target.Id.ToString();
                }
                else
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(Trans.Get("CommandBridge"));
                    return false;
                }

                var dest = string.IsNullOrEmpty(target.DisplayName)
                    ? target.Kind.ToString()
                    : target.DisplayName;
                var destHeader = dest;
                if (target.Kind == HoloTokenKind.Asteroid &&
                    AsteroidService.Reserves(_focus?.FindAsteroid(target.Id)) is { Length: > 0 } reserves)
                    destHeader += "  ·  " + reserves;

                // Quote on the lectern first; nothing leaves before Confirm.
                var fleet = _focus.FindFleet(fleetToken.Id);
                if (target.Kind == HoloTokenKind.System && fleet != null && fleet.SystemId == target.Id)
                {
                    // Put back on its own star (galaxy map): nothing to order.
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    _map?.SetReadout(Trans.Get("galaxy"));
                    return false;
                }

                var mode = Core.Holo.TravelMode.Sublight;
                Core.Holo.JumpChoice jump = null;
                if (_console != null && fleet != null)
                {
                    if (dragged)
                        fleetToken.transform.localPosition = target.HomeLocalPos + Vector3.up * 0.04f;
                    var options = BuildOptions(fleet, target, action, _focus);
                    await PrependGateOptions(fleet, target, options);
                    var choice = await _console.AskAt(target.transform.position,
                        fleetToken.DisplayName.Replace('\n', ' ') + "  →  " + destHeader, options);
                    if (choice == null)
                    {
                        fleetToken.SnapHome();
                        RestoreSpin(fleetToken);
                        _map?.SetReadout(Trans.Get("cancel"));
                        return false;
                    }

                    if (choice is QueueChoice queued)
                    {
                        // Queued, not flown now: the token goes home; the queue path shows the plan.
                        fleetToken.SnapHome();
                        RestoreSpin(fleetToken);
                        var added = await AddQueueStep(fleet, target, queued);
                        if (added.Ok)
                            CicCue.Ok(target.transform.position);
                        else
                            CicCue.Fail(fleetToken.transform.position);
                        _map?.SetReadout(added.Ok ? Trans.Get("stepAdded") : FormatError("AddFleetOrderStep", added.Error));
                        if (added.Ok && _poller != null)
                            await _poller.PollNow();
                        return added.Ok;
                    }

                    if (choice is Core.Holo.TravelMode chosen)
                        mode = chosen;
                    jump = choice as Core.Holo.JumpChoice;
                }

                _map?.SetReadout($"{Trans.Get("Loading")} · {dest}");
                ApiResult result;
                var barkAction = action;
                if (jump != null)
                {
                    action = barkAction = "SendFleetToJumpgate";
                    if (!string.IsNullOrEmpty(jump.Dest.Name))
                        dest = jump.Dest.Name;
                    result = await Core.Holo.JumpgateNetwork.Send(fleet, jump.Dest);
                }
                else if (target.Kind == HoloTokenKind.System && fleet != null)
                {
                    result = await Core.Holo.TravelPlanner.Send(fleet, target.Id, target.GalaxyX, target.GalaxyY, mode);
                    barkAction = Core.Holo.TravelPlanner.BarkAction(mode);
                }
                else
                {
                    result = await ActionJs.Get(action, query);
                }

                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Helm, barkAction, result, dest);
                if (!result.Ok)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(FormatError(action, result.Error));
                    return false;
                }

                // Back to where the ship really is: the next poll lays the course and the token glides there
                // (Core.Holo.HoloGlide) instead of jumping onto the target.
                if (dragged)
                    fleetToken.SnapHome();

                fleetToken.Busy = true;
                RestoreSpin(fleetToken);
                CicCue.Ok(target.transform.position);

                var notice = Trans.Get(result.NoticeKey ?? (jump != null ? "jumpgateFleetSent" : "vr.common.ok"));
                _map?.SetReadout($"{fleetToken.DisplayName} → {dest} · {notice}");

                if (_poller != null)
                    await _poller.PollNow();
                return true;
            }
            finally
            {
                if (!nested)
                {
                    _ordering = false;
                    _map?.SetInteractionLock(false);
                }
            }
        }

        /// <summary>
        /// Ship dropped on an anomaly: survey it where it is (ScanAnomaly needs no travel — the ship must already
        /// be in that system with a scanner module). The lectern shows the rewards before the order goes.
        /// </summary>
        async Task ScanDrop(HoloToken fleetToken, HoloToken target)
        {
            var fleet = _focus.FindFleet(fleetToken.Id);
            var anomaly = AnomalyService.Instance?.Find(target.Id);
            string block = null;
            if (fleet == null || anomaly == null)
                block = Trans.Get("anomaly_not_found");
            else if (!AnomalyService.HasScanner(fleet))
                block = Trans.Get("fleet_lacks_science_module");
            else if (fleet.SystemId != anomaly.SystemId || fleet.IsMoving(FleetOrderGate.UnixNow()))
                block = Trans.Get("fleet_not_in_system");
            else if (!FleetOrderGate.CanMove(fleet))
                block = Trans.Get(FleetOrderGate.BusyKey(fleet)); // server IsFleetBusy (exploring / mining / siege)

            fleetToken.SnapHome();
            RestoreSpin(fleetToken);
            if (block != null)
            {
                CicCue.Fail(target.transform.position);
                _map?.SetReadout(block);
                Core.Crew.BarkDirector.Instance?.Say(CrewDialogue.Role.Science, "fail", 3);
                return;
            }

            var name = Trans.Get(anomaly.NameKey);
            if (_console != null)
            {
                var detail = Trans.Format("vr.anomaly.preview", anomaly.Research, anomaly.Minerals, anomaly.Crystals,
                    anomaly.Difficulty);
                var options = new List<Core.Holo.OrderConsole.Option>
                {
                    new(Trans.Get("scanAnomaly") + "  ·  " + detail, true, HoloZoneMap.AnomalyTint(anomaly.Type), "scan")
                };
                var choice = await _console.AskAt(target.transform.position,
                    fleetToken.DisplayName.Replace('\n', ' ') + "  →  " + name, options);
                if (choice == null)
                {
                    _map?.SetReadout(Trans.Get("cancel"));
                    return;
                }
            }

            _map?.SetReadout($"{Trans.Get("Loading")} · {name}");
            var result = await AnomalyService.Instance.Scan(anomaly, fleet);
            if (!result.Ok)
            {
                CicCue.Fail(target.transform.position);
                _map?.SetReadout(FormatError("ScanAnomaly", result.Error));
                return;
            }

            CicCue.Ok(target.transform.position);
            _map?.SetReadout(name + " · " + AnomalyService.RewardText(result.Body));
        }

        HoloToken FindNearestDropTarget(Vector3 worldPos)
        {
            if (_map == null)
                return null;
            // Galaxy: every star on the plate is a target, not just the ones holding a token.
            if (_map.ShowingGalaxy)
                return _map.GalaxyTargetNear(worldPos, GalaxyDropRadius);
            HoloToken best = null;
            var bestDist = DropRadius;
            foreach (var token in _map.Tokens)
            {
                if (token == null)
                    continue;
                if (token.Kind != HoloTokenKind.Planet && token.Kind != HoloTokenKind.Asteroid &&
                    token.Kind != HoloTokenKind.System && token.Kind != HoloTokenKind.Anomaly)
                    continue;
                // Ignore the local star as a MoveFleetToSystem target (same-system map).
                if (token.Kind == HoloTokenKind.System && token.Slot < 0)
                    continue;

                var a = token.transform.position;
                var d = Vector2.Distance(new Vector2(worldPos.x, worldPos.z), new Vector2(a.x, a.z));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = token;
                }
            }

            return best;
        }

        void SetHoverHighlight(HoloToken token)
        {
            if (token == _hoverHighlight)
                return;
            ClearHoverHighlight();
            _hoverHighlight = token;
            _hoverBaseScale = token.transform.localScale;
            token.transform.localScale = _hoverBaseScale * 1.35f;
            SetHalo(token, true);
            BrightenLabel(token, true);
        }

        void ClearHoverHighlight()
        {
            if (_hoverHighlight != null)
            {
                _hoverHighlight.transform.localScale = _hoverBaseScale;
                SetHalo(_hoverHighlight, false);
                BrightenLabel(_hoverHighlight, false);
            }

            _hoverHighlight = null;
        }

        void SetDropHighlight(HoloToken token)
        {
            if (token == _dropHighlight)
                return;
            ClearDropHighlight();
            _dropHighlight = token;
            _dropBaseScale = token.transform.localScale;
            token.transform.localScale = _dropBaseScale * 1.45f;
            SetHalo(token, true);
            HoloZoneMap.SetTokenLabelVisible(token, true);
        }

        void ClearDropHighlight()
        {
            if (_dropHighlight != null)
            {
                _dropHighlight.transform.localScale = _dropBaseScale;
                SetHalo(_dropHighlight, false);
                // Hide label again unless it's an owned fleet (always visible).
                if (!_dropHighlight.Owned)
                    HoloZoneMap.SetTokenLabelVisible(_dropHighlight, false);
            }

            _dropHighlight = null;
        }

        static void SetHalo(HoloToken token, bool bright)
        {
            var hover = token.transform.Find("HoverRing");
            if (hover == null)
                return;
            var r = bright ? 0.045f : 0.03f;
            hover.localScale = new Vector3(r, 1f, r);

            var label = token.transform.Find("Label");
            if (label != null)
                label.localScale = bright ? Vector3.one * 1.3f : Vector3.one;
        }

        static void RestoreSpin(HoloToken token)
        {
            var spin = token.GetComponent<HoloSpin>();
            if (spin != null)
                spin.enabled = true;
        }

        static bool IsAt(FocusFleet fleet, HoloToken target) =>
            !fleet.IsMoving(FleetOrderGate.UnixNow()) &&
            ((target.Kind == HoloTokenKind.Planet && fleet.PlanetId == target.Id) ||
             (target.Kind == HoloTokenKind.Asteroid && fleet.AsteroidId == target.Id));

        /// <summary>
        /// On-the-spot orders for a ship at <paramref name="target"/> (the world it orbits, the rocks it sits
        /// in) — the same ones, under the same conditions, as the crew consoles: survey, colonize, siege,
        /// cargo, harvest. Empty when there is nothing to do there.
        /// </summary>
        public static List<Core.Holo.OrderConsole.Option> HereOptions(FocusFleet fleet, HoloToken target, FocusContext focus)
        {
            var options = new List<Core.Holo.OrderConsole.Option>();
            if (fleet == null || target == null || focus == null || !IsAt(fleet, target))
                return options;
            if (target.Kind == HoloTokenKind.Asteroid)
            {
                if (FleetOrderGate.CanMine(fleet))
                    options.Add(new Core.Holo.OrderConsole.Option(Trans.Get("harvestAsteroid"), true, UiKit.Cyan, "HarvestAsteroid"));
                return options;
            }

            var planet = focus.FindPlanet(fleet.PlanetId);
            if (FleetOrderGate.CanExplore(fleet) && fleet.HasScienceModule)
                options.Add(new Core.Holo.OrderConsole.Option(Trans.Get("explorePlanet"), true, UiKit.Cyan, "ExplorePlanet"));
            if (planet != null && planet.UserId == 0 && CrewDialogue.ColonyModuleId(fleet) > 0 && FleetOrderGate.CanStance(fleet) &&
                (planet.Habitability == 0 || planet.Habitability >= 6))
                options.Add(new Core.Holo.OrderConsole.Option(Trans.Get("Colonize"), true, UiKit.Amber, "Colonize"));
            if (FleetOrderGate.CanSiege(fleet, focus))
                options.Add(new Core.Holo.OrderConsole.Option(Trans.Get("attackOrbit"), true, new Color(1f, 0.4f, 0.35f), "FleetAttackPlanet"));
            if (FleetOrderGate.CanCargo(fleet, focus) && options.Count < 3)
            {
                options.Add(new Core.Holo.OrderConsole.Option(Trans.Get("depositCargo"), true, UiKit.Cyan, "DepositCargo"));
                options.Add(new Core.Holo.OrderConsole.Option(Trans.Get("withdrawCargo"), true, UiKit.Cyan, "WithdrawCargo"));
            }

            return options;
        }

        async Task<bool> HereOrders(HoloToken fleetToken, FocusFleet fleet, HoloToken target)
        {
            fleetToken.SnapHome();
            RestoreSpin(fleetToken);
            var options = HereOptions(fleet, target, _focus);
            var dest = string.IsNullOrEmpty(target.DisplayName) ? target.Kind.ToString() : target.DisplayName;
            if (options.Count == 0 || _console == null)
            {
                CicCue.Fail(target.transform.position);
                _map?.SetReadout(dest + "  ·  " + Trans.Get("vr.table.alreadyThere"));
                return false;
            }

            var choice = await _console.AskAt(target.transform.position,
                fleetToken.DisplayName.Replace('\n', ' ') + "  ·  " + dest, options);
            if (!(choice is string action))
            {
                _map?.SetReadout(Trans.Get("cancel"));
                return false;
            }

            var query = new Dictionary<string, string>();
            switch (action)
            {
                case "HarvestAsteroid":
                    query["fleet"] = fleet.Id.ToString();
                    query["asteroid"] = fleet.AsteroidId.ToString();
                    break;
                case "Colonize":
                    query["ship"] = CrewDialogue.ColonyModuleId(fleet).ToString();
                    query["planet"] = fleet.PlanetId.ToString();
                    break;
                default:
                    query["fleet"] = fleet.Id.ToString();
                    query["planet"] = fleet.PlanetId.ToString();
                    break;
            }

            // Cargo: which resources and how much, on the transfer pad (the web's three-field popup).
            if (action == "DepositCargo" || action == "WithdrawCargo")
            {
                var amounts = await Core.Holo.CargoPad.Ask(fleet, fleet.PlanetId, dest, action == "WithdrawCargo");
                if (amounts == null)
                {
                    _map?.SetReadout(Trans.Get("cancel"));
                    return false;
                }

                amounts.Value.AddTo(query);
            }

            _map?.SetReadout(Trans.Get("Loading"));
            var result = await ActionJs.Get(action, query);
            var role = action == "ExplorePlanet" ? CrewDialogue.Role.Science
                : action == "FleetAttackPlanet" ? CrewDialogue.Role.Tactical
                : action == "HarvestAsteroid" ? CrewDialogue.Role.Engineering
                : CrewDialogue.Role.Ops;
            Core.Crew.BarkDirector.Instance?.OrderResult(role, action, result, dest);
            if (!result.Ok)
            {
                CicCue.Fail(target.transform.position);
                _map?.SetReadout(FormatError(action, result.Error));
                return false;
            }

            if (action == "ExplorePlanet")
                SurveyBanner.Record(fleet.Id, result.Body);
            if (action == "HarvestAsteroid")
            {
                SurveyBanner.RecordHarvest(fleet.Id, result.Body);
                if (AsteroidService.Instance != null)
                    AsyncTap.Run(AsteroidService.Instance.Refresh());
            }

            CicCue.Ok(target.transform.position);
            _map?.SetReadout(fleetToken.DisplayName + "  ·  " + Trans.Get(result.NoticeKey ?? "vr.common.ok"));
            if (_poller != null)
                await _poller.PollNow();
            return true;
        }

        /// <summary>
        /// Lectern choices: interstellar drops offer every travel mode the ship can use (quoted like the web's
        /// star menu); in-system drops are a single move with the intra-system ETA (server: 1200 / speed).
        /// </summary>
        static List<Core.Holo.OrderConsole.Option> BuildOptions(FocusFleet fleet, HoloToken target, string action,
            FocusContext focus)
        {
            if (target.Kind == HoloTokenKind.System)
            {
                var jump = Core.Holo.TravelPlanner.SystemOptions(fleet, target.GalaxyX, target.GalaxyY);
                if (jump.Count < 5)
                    jump.Add(QueueOption("moveToSystem"));
                return jump;
            }

            var options = new List<Core.Holo.OrderConsole.Option>();
            var verb = Trans.Get(action == "MoveFleetToAsteroid" ? "moveToAsteroidField" : "moveToPlanet");
            var eta = Core.Holo.TravelPlanner.TimeText(1200f / Mathf.Max(1f, fleet.Speed));
            options.Add(new Core.Holo.OrderConsole.Option(verb + "  ·  " + eta, true, UiKit.Cyan, "go"));
            // Automation, as the web right-click "Add to queue" entries (server runs them in order; an idle ship
            // starts the first at once): the go-and-work chains in one press, then the plain queued move.
            options.AddRange(QueueOptions(fleet, target, focus));
            if (options.Count > 5)
                options.RemoveRange(5, options.Count - 5);
            return options;
        }

        /// <summary>
        /// Docked at one of our Jumpgate worlds: every gate world matching the target (that planet, or ours
        /// in that system) goes first on the lectern; travel options fill what is left of its five slots.
        /// </summary>
        static async Task PrependGateOptions(FocusFleet fleet, HoloToken target, List<Core.Holo.OrderConsole.Option> options)
        {
            var origin = Core.Holo.JumpgateNetwork.Origin(fleet);
            if (origin == null || (target.Kind != HoloTokenKind.Planet && target.Kind != HoloTokenKind.System))
                return;
            var all = await Core.Holo.JumpgateNetwork.Destinations(origin.Id);
            var matches = new List<Core.Holo.JumpgateDest>();
            Core.Holo.JumpgateNetwork.Matching(all, target, matches);
            for (var i = matches.Count - 1; i >= 0; i--)
                options.Insert(0, Core.Holo.JumpgateNetwork.Option(fleet, origin, matches[i]));
            if (options.Count > 5)
                options.RemoveRange(5, options.Count - 5);
        }

        /// <summary>A queued order: one step, or the move followed by <see cref="Then"/> (a work step on arrival).</summary>
        sealed class QueueChoice
        {
            public readonly string Type;
            public readonly string Then;

            public QueueChoice(string type, string then = null)
            {
                Type = type;
                Then = then;
            }
        }

        static Core.Holo.OrderConsole.Option QueueOption(string type, string then = null) =>
            new(Core.Holo.OrderQueue.ChainLabel(type, then), true, UiKit.Amber, new QueueChoice(type, then));

        static Task<ApiResult> AddQueueStep(FocusFleet fleet, HoloToken target, QueueChoice q)
        {
            switch (target.Kind)
            {
                case HoloTokenKind.System:
                    return Core.Holo.OrderQueue.AddSystemStep(fleet, target.GalaxyX, target.GalaxyY, target.Id);
                case HoloTokenKind.Asteroid:
                    return q.Type == "moveToAsteroid"
                        ? Core.Holo.OrderQueue.AddAsteroidChain(fleet, target.Id, q.Then == "harvestAsteroid")
                        : Core.Holo.OrderQueue.AddAsteroidStep(fleet, q.Type, target.Id);
                default:
                    return q.Type == "moveToPlanet"
                        ? Core.Holo.OrderQueue.AddPlanetChain(fleet, target.Id, q.Then)
                        : Core.Holo.OrderQueue.AddPlanetStep(fleet, q.Type, target.Id);
            }
        }

        /// <summary>
        /// Queued orders on a target, as the web's right-click "Add to queue" entries — offered to a busy ship
        /// (nothing flies now) and, after the direct move, to an idle one. The chains come first: one press
        /// builds "go to the rocks → harvest", "go home → deposit", "go there → survey".
        /// </summary>
        public static List<Core.Holo.OrderConsole.Option> QueueOptions(FocusFleet fleet, HoloToken target, FocusContext focus)
        {
            var options = new List<Core.Holo.OrderConsole.Option>();
            if (fleet == null || target == null)
                return options;
            switch (target.Kind)
            {
                case HoloTokenKind.System:
                    if (target.Slot >= 0)
                        options.Add(QueueOption("moveToSystem"));
                    break;
                case HoloTokenKind.Asteroid:
                    if (focus?.FindAsteroid(target.Id) is { Gone: true })
                        break;
                    options.Add(QueueOption("moveToAsteroid", "harvestAsteroid"));
                    options.Add(QueueOption("moveToAsteroid"));
                    break;
                case HoloTokenKind.Planet:
                    var planet = focus?.FindPlanet(target.Id);
                    var me = FocusContext.OwnedUserId();
                    var ours = planet != null && me > 0 && planet.UserId == me;
                    if (ours)
                        options.Add(QueueOption("moveToPlanet", "depositCargo"));
                    options.Add(QueueOption("moveToPlanet"));
                    if (ours)
                        options.Add(QueueOption("moveToPlanet", "withdrawCargo"));
                    if (fleet.HasScienceModule)
                        options.Add(QueueOption("moveToPlanet", "explorePlanet"));
                    break;
            }

            return options;
        }

        /// <summary>
        /// A busy ship given a target: the lectern quotes only queued steps (the header says why), and the
        /// chosen chain is appended to its server queue.
        /// </summary>
        async Task<bool> QueueOrders(HoloToken fleetToken, FocusFleet fleet, HoloToken target)
        {
            fleetToken.SnapHome();
            RestoreSpin(fleetToken);
            var dest = string.IsNullOrEmpty(target.DisplayName) ? target.Kind.ToString() : target.DisplayName;
            var options = QueueOptions(fleet, target, _focus);
            if (options.Count == 0 || _console == null)
            {
                CicCue.Fail(target.transform.position);
                _map?.SetReadout(dest + "  ·  " + Trans.Get("vr.table.notATarget"));
                return false;
            }

            var choice = await _console.AskAt(target.transform.position,
                fleetToken.DisplayName.Replace('\n', ' ') + "  →  " + dest + "  ·  " + Trans.Get(FleetOrderGate.BusyKey(fleet)),
                options);
            if (!(choice is QueueChoice queued))
            {
                _map?.SetReadout(Trans.Get("cancel"));
                return false;
            }

            var added = await AddQueueStep(fleet, target, queued);
            if (added.Ok)
                CicCue.Ok(target.transform.position);
            else
                CicCue.Fail(target.transform.position);
            _map?.SetReadout(added.Ok ? Trans.Get("stepAdded") : FormatError("AddFleetOrderStep", added.Error));
            if (added.Ok && _poller != null)
                await _poller.PollNow();
            return added.Ok;
        }

        static string FormatError(string action, string error)
        {
            // Server body is error:{localized message}; ActionJs already stripped the prefix.
            return string.IsNullOrEmpty(error) ? Trans.Get("vr.common.error") : error;
        }
    }
}
