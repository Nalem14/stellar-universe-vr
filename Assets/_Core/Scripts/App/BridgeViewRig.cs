using Core.Utils;
using Core.Vfx;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace Core.App
{
    /// <summary>
    /// Unity-only view: Bridge (CIC) rides the viewed ship. XR Origin is parented under the
    /// Bridge so the player moves with it. The viewed ship's exterior hull is hidden.
    /// </summary>
    public class BridgeViewRig : MonoBehaviour
    {
        FocusContext _focus;
        SystemExterior _exterior;
        Transform _viewShip;
        Transform _bridgeMount;
        XROrigin _xrOrigin;
        Transform _xrOriginalParent;
        bool _playerParented;
        Vector3 _posVel;
        float _yawVel;
        float _bank;
        float _bankVel;

        public Transform BridgeMount => _bridgeMount;
        public Transform ViewShip => _viewShip;

        public void Bind(FocusContext focus, SystemExterior exterior, Transform bridgeRoot)
        {
            _focus = focus;
            _exterior = exterior;
            EnsureViewShip();
            if (bridgeRoot != null && _bridgeMount != null)
            {
                bridgeRoot.SetParent(_bridgeMount, false);
                bridgeRoot.localPosition = Vector3.zero;
                bridgeRoot.localRotation = Quaternion.identity;
            }

            ParentPlayer();
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.Changed += OnFocusChanged;
                _focus.FleetsChanged -= OnFleetsChanged;
                _focus.FleetsChanged += OnFleetsChanged;
            }

            SnapToViewTarget(force: true);
        }

        void OnDestroy()
        {
            if (_focus != null)
            {
                _focus.Changed -= OnFocusChanged;
                _focus.FleetsChanged -= OnFleetsChanged;
            }

            RestorePlayerParent();
        }

        int _viewFleet = -1;
        int _viewPlanet = -1;

        void OnFocusChanged()
        {
            // Boarding another ship / station puts the captain back on deck; the same ship entering a new
            // system (a trip) leaves the player where they stand.
            var boarded = _focus == null || _focus.ViewFleetId != _viewFleet || _focus.ViewPlanetId != _viewPlanet;
            if (_focus != null)
            {
                _viewFleet = _focus.ViewFleetId;
                _viewPlanet = _focus.ViewPlanetId;
            }

            SnapToViewTarget(force: true, onDeck: boarded);
        }

        void LateUpdate()
        {
            SnapToViewTarget(force: false);
        }

        void EnsureViewShip()
        {
            if (_viewShip != null)
                return;

            var ship = new GameObject("ViewShip");
            ship.transform.SetParent(transform, false);
            _viewShip = ship.transform;

            // Our own hull is the exterior's FleetShipView (hidden aboard, drawn for the viewscreen's outside shots).

            var mount = new GameObject("BridgeMount");
            mount.transform.SetParent(_viewShip, false);
            mount.transform.localPosition = Vector3.zero;
            mount.transform.localRotation = Quaternion.identity;
            _bridgeMount = mount.transform;

            _viewShip.position = transform.position + new Vector3(SystemExterior.OrbitBase * 0.55f,
                WorldScale.EclipticHeight, 0f);
        }

        StationExterior _station;
        CityExterior _city;
        ViewMode _mode;
        int _cityPlanet;

        /// <summary>
        /// What the hall stands in. Aboard an orbital fortress: its hub, spokes and ring round the command hall
        /// (plus its fitted modules). In a city's citadel: the tower, the city and the planet's land around
        /// (<see cref="CityExterior"/>), set far below the system scene so none of space shows. Both are built on
        /// first use and hidden aboard a ship.
        /// </summary>
        public void SetViewMode(ViewMode mode)
        {
            var planet = _focus != null ? _focus.ViewPlanetId : 0;
            var changed = mode != _mode || (mode == ViewMode.City && planet != _cityPlanet);
            _cityPlanet = planet;
            _mode = mode;
            EnsureViewShip();
            var station = mode == ViewMode.Station;
            if (station && _station == null)
                _station = StationExterior.Build(_bridgeMount);
            if (_station != null && _station.gameObject.activeSelf != station)
                _station.gameObject.SetActive(station);
            if (station && _station != null)
                _station.Fit(_focus?.FindViewFleet());

            var city = mode == ViewMode.City;
            if (city)
            {
                if (_city == null)
                    _city = CityExterior.Build(_bridgeMount, _focus);
                _city.Show(_focus != null ? _focus.ViewPlanetId : 0);
            }
            else if (_city != null)
                _city.Hide();

            // The dressing follows the focus change that already snapped the rig: place it again for this mode
            // (the citadel stands far below the system, a fortress on its own orbit).
            if (changed)
                SnapToViewTarget(force: true, onDeck: false);
        }

        /// <summary>The fortress we are aboard changed its fitting (FleetsChanged): refit the exterior modules.</summary>
        void OnFleetsChanged()
        {
            if (_mode == ViewMode.Station && _station != null)
                _station.Fit(_focus?.FindViewFleet());
        }

        void ParentPlayer()
        {
            if (_bridgeMount == null)
                return;
            _xrOrigin = FindFirstObjectByType<XROrigin>();
            if (_xrOrigin == null)
                return;

            if (!_playerParented)
            {
                _xrOriginalParent = _xrOrigin.transform.parent;
                _playerParented = true;
            }

            _xrOrigin.transform.SetParent(_bridgeMount, false);
            PutPlayerOnDeck();
        }

        public void PutPlayerOnDeck()
        {
            if (_xrOrigin == null || _bridgeMount == null)
                return;

            if (CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode)
                return;
            // In the corridor or another room the player stands elsewhere; view changes must not pull them back.
            if (Core.Stations.DiplomacyRoom.AnyRoomInside)
                return;

            _xrOrigin.transform.localPosition = WorldScale.CicCaptainStand;
            _xrOrigin.transform.localRotation = Quaternion.identity;
            // Head on the captain's spot, whatever the headset's offset in the real play space.
            XrPlacement.PlaceHead(_xrOrigin, _bridgeMount.TransformPoint(WorldScale.CicCaptainStand), _bridgeMount.forward);

            var body = _xrOrigin.Origin != null
                ? _xrOrigin.Origin.GetComponent<CharacterController>()
                : _xrOrigin.GetComponent<CharacterController>();
            if (body == null)
                body = _xrOrigin.GetComponentInChildren<CharacterController>();
            if (body != null)
            {
                body.enabled = false;
                body.enabled = true;
            }
        }

        void RestorePlayerParent()
        {
            if (!_playerParented || _xrOrigin == null)
                return;
            if (_xrOrigin.transform != null)
                _xrOrigin.transform.SetParent(_xrOriginalParent, true);
            _playerParented = false;
        }

        void SnapToViewTarget(bool force, bool onDeck = true)
        {
            if (_viewShip == null || _exterior == null || _focus == null)
                return;

            // The citadel: the hall on top of its tower, far below the system scene (out of the far clip), facing
            // the system's star so the day side of the city is the one ahead.
            if (_mode == ViewMode.City && _focus.HasSystem && _focus.ViewPlanetId > 0)
            {
                var ground = CityAnchor();
                var toStar = _exterior.transform.position - ground;
                toStar.y = 0f;
                var yaw = toStar.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toStar.normalized, Vector3.up) : Quaternion.identity;
                if (force || (_viewShip.position - ground).sqrMagnitude > 0.01f)
                {
                    _viewShip.SetPositionAndRotation(ground, yaw);
                    _posVel = Vector3.zero;
                    _bank = 0f;
                    _bankVel = 0f;
                    if (force && onDeck)
                        PutPlayerOnDeck();
                }

                return;
            }

            Vector3 target;
            if (!_focus.HasSystem)
            {
                target = _exterior.transform.position + new Vector3(SystemExterior.OrbitBase * 1.05f,
                    WorldScale.EclipticHeight, 0f);
            }
            else
            {
                var fleet = _focus.FindViewFleet();
                target = fleet != null
                    ? _exterior.ResolveFleetWorldPosition(fleet)
                    : FallbackStationPosition();
            }

            var star = _exterior.transform.position;
            var away = target - star;
            away.y = 0f;
            var minDist = SystemExterior.OrbitBase * 1.05f;
            if (away.magnitude < minDist)
            {
                if (away.sqrMagnitude < 0.01f)
                    away = Vector3.right;
                target = star + away.normalized * minDist + Vector3.up * WorldScale.EclipticHeight;
            }

            target.y = WorldScale.EclipticHeight;

            // Between systems the voyage flies the ship (departure, transit, approach ending on this target).
            var voyage = ShipVoyage.Instance;
            if (voyage != null && voyage.TryPose(target, out var vPos, out var vRot))
            {
                _viewShip.SetPositionAndRotation(vPos, vRot);
                _posVel = Vector3.zero;
                _bank = 0f;
                _bankVel = 0f;
                if (force && onDeck)
                    PutPlayerOnDeck();
                return;
            }

            Vector3 lookDir;
            var fleetMot = _focus.FindViewFleet();
            if (fleetMot != null && fleetMot.IsMoving(UnixNow()) &&
                _exterior.TryGetFleetTravelDirection(fleetMot, out var travel))
            {
                lookDir = travel;
            }
            else
            {
                lookDir = star - target;
                // A fortress's bow is its viewscreen monolith: turn so the world it orbits fills a starboard bay.
                if (_mode == ViewMode.Station)
                    lookDir = Quaternion.Euler(0f, -StationPlanetBearing, 0f) * lookDir;
            }

            lookDir.y = 0f;
            if (lookDir.sqrMagnitude < 0.01f)
                lookDir = Vector3.forward;

            var flatLook = lookDir.normalized;
            var targetYaw = Quaternion.LookRotation(flatLook, Vector3.up);

            // Mild bank from lateral velocity — deck stays mostly level via BridgeMount identity.
            var lateral = Vector3.Dot(_posVel, Vector3.Cross(Vector3.up, flatLook));
            var targetBank = Mathf.Clamp(-lateral * 0.015f, -6f, 6f);

            if (force)
            {
                _viewShip.position = target;
                _posVel = Vector3.zero;
                _viewShip.rotation = targetYaw;
                _bank = 0f;
                _bankVel = 0f;
                if (onDeck)
                    PutPlayerOnDeck();
            }
            else
            {
                _viewShip.position = MotionEase.Damp(_viewShip.position, target, ref _posVel, 0.45f);
                _viewShip.rotation = Quaternion.Slerp(_viewShip.rotation, targetYaw,
                    1f - Mathf.Exp(-3.2f * Time.deltaTime));
                _bank = Mathf.SmoothDamp(_bank, targetBank, ref _bankVel, 0.6f);
                _viewShip.rotation = Quaternion.LookRotation(
                    Vector3.ProjectOnPlane(_viewShip.forward, Vector3.up).normalized, Vector3.up)
                    * Quaternion.Euler(0f, 0f, _bank);
            }

        }

        static long UnixNow() =>
            (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds;

        /// <summary>Degrees from the station's bow to the planet it orbits (inside the 24°–66° bay).</summary>
        const float StationPlanetBearing = 42f;

        /// <summary>
        /// Where the citadel's hall stands: straight under its planet in the system scene, <see cref="WorldScale.CityDepth"/>
        /// down — beyond the bridge's far clip, so the system (star, planets, fleets) never shows in the city.
        /// </summary>
        Vector3 CityAnchor()
        {
            var planet = _focus.FindPlanet(_focus.ViewPlanetId);
            Vector3 at;
            if (_exterior.TryGetPlanet(_focus.ViewPlanetId, out var t))
                at = t.position;
            else if (planet != null)
                at = _exterior.transform.position + SystemExterior.OrbitPosition(Mathf.Max(1, planet.Slot), planet.Id);
            else
                at = _exterior.transform.position;
            at.y = _exterior.transform.position.y - WorldScale.CityDepth;
            return at;
        }

        Vector3 FallbackStationPosition()
        {
            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            FocusPlanet pick = null;
            if (_focus.ViewPlanetId > 0)
                pick = _focus.FindPlanet(_focus.ViewPlanetId);

            if (pick == null)
            {
                foreach (var planet in _focus.Planets)
                {
                    if (owned > 0 && planet.UserId == owned)
                    {
                        pick = planet;
                        break;
                    }

                    if (pick == null)
                        pick = planet;
                }
            }

            if (pick != null)
            {
                var orbit = SystemExterior.OrbitPosition(Mathf.Max(1, pick.Slot), pick.Id);
                var radial = orbit.sqrMagnitude > 0.01f ? orbit.normalized : Vector3.right;
                return _exterior.transform.position + orbit
                    + radial * WorldScale.StationStandoff(WorldScale.PlanetRadius(pick.Slot));
            }

            return _exterior.transform.position + new Vector3(SystemExterior.OrbitBase * 0.55f,
                WorldScale.EclipticHeight, 0f);
        }
    }
}
