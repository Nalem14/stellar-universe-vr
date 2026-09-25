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
        Transform _hull;
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
            }

            SnapToViewTarget(force: true);
        }

        void OnDestroy()
        {
            if (_focus != null)
                _focus.Changed -= OnFocusChanged;
            RestorePlayerParent();
        }

        void OnFocusChanged()
        {
            SnapToViewTarget(force: true);
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

            var hull = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hull.name = "Hull";
            hull.transform.SetParent(_viewShip, false);
            hull.transform.localPosition = new Vector3(0f, -2.2f, 0f);
            hull.transform.localScale = new Vector3(14f, 3.2f, 22f);
            var col = hull.GetComponent<Collider>();
            if (col != null)
                Destroy(col);
            hull.GetComponent<MeshRenderer>().sharedMaterial = SharedHiddenHullMat();
            _hull = hull.transform;
            SetHullVisible(false);

            var mount = new GameObject("BridgeMount");
            mount.transform.SetParent(_viewShip, false);
            mount.transform.localPosition = Vector3.zero;
            mount.transform.localRotation = Quaternion.identity;
            _bridgeMount = mount.transform;

            _viewShip.position = transform.position + new Vector3(SystemExterior.OrbitBase * 0.55f,
                WorldScale.EclipticHeight, 0f);
        }

        static Material _hiddenHullMat;

        static Material SharedHiddenHullMat()
        {
            if (_hiddenHullMat != null)
                return _hiddenHullMat;
            var shader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            _hiddenHullMat = new Material(shader);
            if (_hiddenHullMat.HasProperty("_Color"))
                _hiddenHullMat.SetColor("_Color", new Color(0.12f, 0.14f, 0.18f));
            if (_hiddenHullMat.HasProperty("_EmissionMul"))
                _hiddenHullMat.SetFloat("_EmissionMul", 0.2f);
            return _hiddenHullMat;
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
            // In the dry dock / lab the player stands in another room; view changes must not pull them back.
            if (Core.Stations.DryDock.Inside || Core.Stations.ResearchLab.Inside)
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

        void SnapToViewTarget(bool force)
        {
            if (_viewShip == null || _exterior == null || _focus == null)
                return;

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

            SetHullVisible(false);
        }

        static long UnixNow() =>
            (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds;

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
                    + radial * WorldScale.FleetStandoff(WorldScale.PlanetRadius(pick.Slot));
            }

            return _exterior.transform.position + new Vector3(SystemExterior.OrbitBase * 0.55f,
                WorldScale.EclipticHeight, 0f);
        }

        void SetHullVisible(bool visible)
        {
            if (_hull == null)
                return;
            var r = _hull.GetComponent<Renderer>();
            if (r != null)
                r.enabled = visible;
        }
    }
}
