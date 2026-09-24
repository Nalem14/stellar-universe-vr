using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
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
        const float DropRadius = 0.7f;
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
                if (token == null || token.Kind != HoloTokenKind.Fleet || !token.Owned || token.Busy)
                    continue;
                if (_mapCtrl != null && _mapCtrl.MovesLocked)
                    continue;
                if (_focus != null)
                {
                    var locked = false;
                    foreach (var fleet in _focus.Fleets)
                    {
                        if (fleet.Id == token.Id && !fleet.CanIssueMove(UnixNow()))
                        {
                            locked = true;
                            break;
                        }
                    }

                    if (locked)
                        continue;
                }

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

            // Generous aim assist: ~32 cm radius — tabletop grab shouldn't need pixel aim.
            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.32f;
            sphere.center = new Vector3(0f, 0.12f, 0f);
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
            var name = string.IsNullOrEmpty(token.DisplayName) ? "ship " + token.Id : token.DisplayName;
            _map?.SetReadout($"⟶ {name}");
            CicCue.Hover(token.transform.position);
        }

        static void BrightenLabel(HoloToken token, bool on)
        {
            HoloZoneMap.SetTokenLabelVisible(token, on || token.Owned);
            var label = token.transform.Find("Label");
            if (label == null || !label.gameObject.activeSelf)
                return;
            var tmp = label.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            if (tmp == null)
                return;
            tmp.fontSize = on ? 18f : 16f;
            tmp.color = on ? Color.white : new Color(0.85f, 0.98f, 1f, 1f);
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

                if (_focus == null || !AuthManager.Ensure().IsLoggedIn)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(Trans.Get("error_not_logged_in"));
                    return;
                }

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
                }
                else
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(Trans.Get("CommandBridge"));
                    return;
                }

                var dest = string.IsNullOrEmpty(target.DisplayName)
                    ? target.Kind.ToString()
                    : target.DisplayName;
                _map?.SetReadout($"{Trans.Get("Loading")} · {dest}");
                var result = await ActionJs.Get(action, query);
                Core.Crew.BarkDirector.Instance?.OrderResult(CrewDialogue.Role.Helm, action, result, dest);
                if (!result.Ok)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    CicCue.Fail(fleetToken.transform.position);
                    _map?.SetReadout(FormatError(action, result.Error));
                    return;
                }

                fleetToken.transform.localPosition = target.HomeLocalPos + Vector3.up * 0.04f;
                fleetToken.CaptureHome();
                fleetToken.Busy = true;
                RestoreSpin(fleetToken);
                CicCue.Ok(target.transform.position);

                var notice = Trans.Get(result.NoticeKey ?? "vr.common.ok");
                _map?.SetReadout($"{fleetToken.DisplayName} → {dest} · {notice}");

                if (_poller != null)
                    await _poller.PollNow();
            }
            finally
            {
                _ordering = false;
                _map?.SetInteractionLock(false);
            }
        }

        HoloToken FindNearestDropTarget(Vector3 worldPos)
        {
            if (_map == null)
                return null;
            HoloToken best = null;
            var bestDist = DropRadius;
            foreach (var token in _map.Tokens)
            {
                if (token == null)
                    continue;
                if (token.Kind != HoloTokenKind.Planet && token.Kind != HoloTokenKind.Asteroid &&
                    token.Kind != HoloTokenKind.System)
                    continue;
                // Ignore the local star as a MoveFleetToSystem target (same-system map).
                if (token.Kind == HoloTokenKind.System && token.Slot == 0 &&
                    token.HomeLocalPos.sqrMagnitude < 0.05f * 0.05f)
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
            var s = WorldScale.HoloFleetSize;
            var mul = bright ? 4.2f : 2.8f;
            hover.localScale = Vector3.one * (s * mul);

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

        static string FormatError(string action, string error)
        {
            // Server body is error:{localized message}; ActionJs already stripped the prefix.
            return string.IsNullOrEmpty(error) ? Trans.Get("vr.common.error") : error;
        }
    }
}
