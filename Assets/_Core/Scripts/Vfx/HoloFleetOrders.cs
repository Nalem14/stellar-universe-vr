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
    /// Grab owned fleet tokens on the holo table; drop on planet/asteroid → MoveFleetTo*.
    /// </summary>
    public class HoloFleetOrders : MonoBehaviour
    {
        const float DropRadius = 0.14f;

        HoloZoneMap _map;
        FocusContext _focus;
        FleetPoller _poller;
        readonly List<XRGrabInteractable> _grabs = new();
        bool _ordering;

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

        HoloMapController _mapCtrl;

        void OnDestroy()
        {
            if (_map != null)
                _map.TokensRebuilt -= OnTokensRebuilt;
            UnwireGrabs();
        }

        void OnTokensRebuilt() => WireTokens();

        void WireTokens()
        {
            UnwireGrabs();
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
                _grabs.Add(grab);

                var spin = token.GetComponent<HoloSpin>();
                if (spin != null)
                    spin.enabled = true;
            }
        }

        static long UnixNow() =>
            (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds;

        void UnwireGrabs()
        {
            for (var i = 0; i < _grabs.Count; i++)
            {
                var grab = _grabs[i];
                if (grab == null)
                    continue;
                grab.selectEntered.RemoveListener(OnSelectEntered);
                grab.selectExited.RemoveListener(OnSelectExited);
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

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            if (token == null)
                return;
            var spin = token.GetComponent<HoloSpin>();
            if (spin != null)
                spin.enabled = false;
            if (_map != null)
                _map.SetReadout(Trans.Get("CommandBridge"));
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            var token = args.interactableObject.transform.GetComponent<HoloToken>();
            if (token == null || _ordering)
                return;
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
                    return;
                }

                if (_focus == null || !AuthManager.Ensure().IsLoggedIn)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    _map?.SetReadout(Trans.Get("error_not_logged_in"));
                    return;
                }

                var action = target.Kind == HoloTokenKind.Planet
                    ? "MoveFleetToPlanet"
                    : target.Kind == HoloTokenKind.Asteroid
                        ? "MoveFleetToAsteroid"
                        : null;
                if (action == null)
                {
                    // Galaxy stub: drop near star token id encoded as system — use MoveFleetToSystem with pos.
                    if (_mapCtrl != null && _mapCtrl.Mode == HoloMapMode.Galaxy &&
                        target.Kind == HoloTokenKind.Fleet == false)
                    {
                        // no-op fallthrough
                    }

                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    return;
                }

                var query = new Dictionary<string, string>
                {
                    { "fleet", fleetToken.Id.ToString() }
                };
                if (target.Kind == HoloTokenKind.Planet)
                    query["planet"] = target.Id.ToString();
                else if (target.Kind == HoloTokenKind.Asteroid)
                    query["asteroid"] = target.Id.ToString();

                _map?.SetReadout(Trans.Get("Loading"));
                var result = await ActionJs.Get(action, query);
                if (!result.Ok)
                {
                    fleetToken.SnapHome();
                    RestoreSpin(fleetToken);
                    _map?.SetReadout(FormatError(action, result.Error));
                    return;
                }

                // Optimistic: park fleet token near target until poll rebuilds.
                fleetToken.transform.localPosition = target.HomeLocalPos + Vector3.up * 0.04f;
                fleetToken.CaptureHome();
                fleetToken.Busy = true;
                RestoreSpin(fleetToken);

                var label = !string.IsNullOrEmpty(_focus.SystemName)
                    ? _focus.SystemName
                    : _focus.SystemId.ToString();
                var suffix = result.Body != null && result.Body.StartsWith("ok:", StringComparison.Ordinal)
                    ? result.Body
                    : "ok";
                _map?.SetReadout($"{Trans.Get("CommandBridge")} · {label} · {suffix}");

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
                if (token.Kind != HoloTokenKind.Planet && token.Kind != HoloTokenKind.Asteroid)
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
            {
                var translated = Trans.Get(key);
                return $"{action} · {translated}";
            }

            return $"{action} · {key}";
        }
    }
}
