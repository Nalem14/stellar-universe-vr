using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    /// <summary>
    /// Grab owned fleet tokens; drop on planet/asteroid/system → MoveFleet*.
    /// Hover / ghost / readout / cue feedback while ordering.
    /// </summary>
    public class HoloFleetOrders : MonoBehaviour
    {
        const float DropRadius = 0.18f;

        HoloZoneMap _map;
        FocusContext _focus;
        FleetPoller _poller;
        HoloMapController _mapCtrl;
        readonly List<XRGrabInteractable> _grabs = new();
        bool _ordering;
        HoloToken _dragging;
        HoloToken _hoverHighlight;
        HoloToken _dropHighlight;
        Vector3 _hoverBaseScale = Vector3.one;
        Vector3 _dropBaseScale = Vector3.one;

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
                _map.TokensRebuilt -= OnTokensRebuilt;
            UnwireGrabs();
            ClearDropHighlight();
            ClearHoverHighlight();
        }

        void OnTokensRebuilt() => WireTokens();

        void Update()
        {
            if (_dragging == null || _map == null)
                return;
            var target = FindNearestDropTarget(_dragging.transform.position);
            if (target != null)
            {
                _mapCtrl?.ShowMoveGhost(_dragging.HomeLocalPos, target.HomeLocalPos);
                SetDropHighlight(target);
                var dest = string.IsNullOrEmpty(target.DisplayName)
                    ? target.Kind.ToString()
                    : target.DisplayName;
                _map.SetReadout($"{_dragging.DisplayName} → {dest}");
            }
            else
            {
                _mapCtrl?.HideMoveGhost();
                ClearDropHighlight();
            }
        }

        void WireTokens()
        {
            UnwireGrabs();
            ClearHoverHighlight();
            ClearDropHighlight();
            if (_map == null)
                return;

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

                EnsurePhysics(token.gameObject);
                var grab = token.GetComponent<XRGrabInteractable>();
                if (grab == null)
                    grab = token.gameObject.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
                grab.selectMode = InteractableSelectMode.Single;
                grab.throwOnDetach = false;
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

        static void EnsurePhysics(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
            {
                col = go.AddComponent<BoxCollider>();
                ((BoxCollider)col).size = Vector3.one * 0.08f;
            }

            col.isTrigger = false;
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        void OnHoverEntered(HoverEnterEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            if (token == null || _dragging != null)
                return;
            SetHoverHighlight(token);
            var name = string.IsNullOrEmpty(token.DisplayName) ? "ship " + token.Id : token.DisplayName;
            _map?.SetReadout($"{Trans.Get("CommandBridge")} · {name}");
            CicCue.Hover(token.transform.position);
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
            var spin = token.GetComponent<HoloSpin>();
            if (spin != null)
                spin.enabled = false;
            token.transform.localScale = token.transform.localScale * 1.15f;
            var name = string.IsNullOrEmpty(token.DisplayName) ? "ship " + token.Id : token.DisplayName;
            _map?.SetReadout($"{Trans.Get("CommandBridge")} · {name}");
            CicCue.Ok(token.transform.position);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            _dragging = null;
            _mapCtrl?.HideMoveGhost();
            ClearDropHighlight();
            if (token == null || _ordering)
                return;
            token.transform.localScale = Vector3.one;
            _ = ResolveDrop(token);
        }

        async Task ResolveDrop(HoloToken fleetToken)
        {
            _ordering = true;
            try
            {
                var target = FindNearestDropTarget(fleetToken.transform.position);
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
                    return;
                }

                var dest = string.IsNullOrEmpty(target.DisplayName)
                    ? target.Kind.ToString()
                    : target.DisplayName;
                _map?.SetReadout($"{Trans.Get("Loading")} · {dest}");
                var result = await ActionJs.Get(action, query);
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

                var suffix = result.Body != null && result.Body.StartsWith("ok:", StringComparison.Ordinal)
                    ? result.Body
                    : "ok";
                _map?.SetReadout($"{fleetToken.DisplayName} → {dest} · {suffix}");

                if (_poller != null)
                    await _poller.PollNow();
            }
            finally
            {
                _ordering = false;
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
                var d = Vector3.Distance(worldPos, token.transform.position);
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
            token.transform.localScale = _hoverBaseScale * 1.2f;
            SetHalo(token, true);
        }

        void ClearHoverHighlight()
        {
            if (_hoverHighlight != null)
            {
                _hoverHighlight.transform.localScale = _hoverBaseScale;
                SetHalo(_hoverHighlight, false);
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
            token.transform.localScale = _dropBaseScale * 1.35f;
        }

        void ClearDropHighlight()
        {
            if (_dropHighlight != null)
                _dropHighlight.transform.localScale = _dropBaseScale;
            _dropHighlight = null;
        }

        static void SetHalo(HoloToken token, bool bright)
        {
            var halo = token.transform.Find("GrabHalo");
            if (halo == null)
                return;
            var s = WorldScale.HoloFleetSize;
            var mul = bright ? 1.9f : 1.6f;
            halo.localScale = new Vector3(s * mul, 0.003f, s * mul);
        }

        static void RestoreSpin(HoloToken token)
        {
            var spin = token.GetComponent<HoloSpin>();
            if (spin != null)
                spin.enabled = true;
        }

        static string FormatError(string action, string error)
        {
            if (string.IsNullOrEmpty(error))
                return $"{action} · {Trans.Get("error_not_logged_in")}";
            var key = error.Trim().Trim('{', '}');
            if (key.StartsWith("error_", StringComparison.Ordinal))
                return $"{action} · {Trans.Get(key)}";
            return $"{action} · {key}";
        }
    }
}
