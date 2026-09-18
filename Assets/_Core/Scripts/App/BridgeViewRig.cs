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
            // Sibling of exterior content — never parented under SystemExterior's cleared root.
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
            var shader = Shader.Find("SU/UnlitEmissive") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", new Color(0.12f, 0.14f, 0.18f));
            if (mat.HasProperty("_EmissionMul"))
                mat.SetFloat("_EmissionMul", 0.2f);
            hull.GetComponent<MeshRenderer>().sharedMaterial = mat;
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

            // Never keep world pose: BridgeMount lives in system space; keeping (0,0,0)
            // leaves the player floating outside the CIC and falling forever.
            _xrOrigin.transform.SetParent(_bridgeMount, false);
            PutPlayerOnDeck();
        }

        void PutPlayerOnDeck()
        {
            if (_xrOrigin == null || _bridgeMount == null)
                return;

            _xrOrigin.transform.localPosition = new Vector3(0f, 0f, 0.4f);
            _xrOrigin.transform.localRotation = Quaternion.identity;

            // Kill residual fall velocity from CharacterController / locomotion.
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

            // Before focus resolves, park at a safe orbit — never leave the CIC at world origin.
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

            // Never sit inside the star — windows must see space, not white fill.
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

            // Keep deck roughly level in world Y so room-scale gravity still hits the floor.
            target.y = WorldScale.EclipticHeight;

            if (force)
                _viewShip.position = target;
            else
                _viewShip.position = Vector3.Lerp(_viewShip.position, target, Time.deltaTime * 3f);

            var look = _exterior.transform.position - _viewShip.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
            {
                var rot = Quaternion.LookRotation(look.normalized, Vector3.up);
                _viewShip.rotation = force
                    ? rot
                    : Quaternion.Slerp(_viewShip.rotation, rot, Time.deltaTime * 2f);
            }

            if (force)
                PutPlayerOnDeck();

            SetHullVisible(false);
        }

        Vector3 FallbackStationPosition()
        {
            var owned = AuthManager.Ensure().User != null ? AuthManager.Ensure().User.id : 0;
            FocusPlanet pick = null;
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
