using Unity.XR.CoreUtils;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Safety net for every room: in room-scale the head can walk through a virtual wall, and past the floor's
    /// edge the character body falls into space. The guard remembers the last spot where the player stood still
    /// on a floor (in the rig's parent space — the bridge flies with its ship) and, if the body drops more than
    /// a metre under it, puts the head straight back there. It also absorbs tracking jumps: when the headset's
    /// tracking starts late (the first frames of the app report the head at the play-space centre, then its
    /// real spot) or the player recenters, the head leaps in tracking space in one frame — no body moves that
    /// fast — and the rig is shifted so the head stays where it was in the room, instead of outside it.
    /// Nothing per frame but a few vector ops.
    /// </summary>
    public sealed class FallGuard : MonoBehaviour
    {
        const float DropLimit = 1.0f;
        const float SettleTime = 0.35f;

        XROrigin _rig;
        CharacterController _body;
        Transform _parent;
        Vector3 _safeLocal;
        Vector3 _safeForward = Vector3.forward;
        bool _hasSafe;
        float _still;
        float _lastY;
        Vector3 _lastCamLocal;
        Vector3 _lastHead;
        bool _tracked;

        /// <summary>Head travel in tracking space in one frame (m) that only a tracking jump can make.</summary>
        const float TrackingJump = 0.3f;

        public static void Ensure()
        {
            var rig = FindFirstObjectByType<XROrigin>();
            if (rig != null && rig.GetComponent<FallGuard>() == null)
                rig.gameObject.AddComponent<FallGuard>();
        }

        void Awake()
        {
            _rig = GetComponent<XROrigin>();
            _body = GetComponentInChildren<CharacterController>();
        }

        void LateUpdate()
        {
            if (_rig == null || _rig.Camera == null)
                return;
            var t = _rig.transform;
            if (t.parent != _parent)
            {
                // Moved to another room (or boarded another bridge): start over there.
                _parent = t.parent;
                _hasSafe = false;
                _still = 0f;
            }

            AbsorbTrackingJump();

            var local = Local(t.position);
            if (_hasSafe && local.y < _safeLocal.y - DropLimit)
            {
                XrPlacement.PlaceHead(_rig, World(_safeLocal), World(_safeLocal + _safeForward) - World(_safeLocal));
                _lastY = Local(t.position).y;
                _still = 0f;
                return;
            }

            if (!_hasSafe && local.y < -30f && _parent != null)
            {
                // Dropped into a room before it was ready (no safe spot yet): back to the room's origin floor.
                XrPlacement.PlaceHead(_rig, _parent.position, _parent.forward);
                _lastY = Local(t.position).y;
                return;
            }

            var grounded = _body == null || !_body.enabled || _body.isGrounded;
            _still = grounded && Mathf.Abs(local.y - _lastY) < 0.004f ? _still + Time.unscaledDeltaTime : 0f;
            _lastY = local.y;
            if (_still < SettleTime)
                return;
            var head = _rig.Camera.transform.position;
            _safeLocal = Local(new Vector3(head.x, t.position.y, head.z));
            var fwd = Vector3.ProjectOnPlane(_rig.Camera.transform.forward, Vector3.up);
            _safeForward = fwd.sqrMagnitude > 1e-4f ? LocalDir(fwd.normalized) : _safeForward;
            _hasSafe = true;
        }

        void AbsorbTrackingJump()
        {
            var cam = _rig.Camera.transform;
            var camLocal = cam.localPosition;
            var head = cam.position;
            if (_tracked)
            {
                var d = camLocal - _lastCamLocal;
                d.y = 0f;
                if (d.magnitude > TrackingJump)
                {
                    // Keep the head where it was in the room: slide the play space under it (horizontal only).
                    var body = _rig.GetComponentInChildren<CharacterController>();
                    if (body != null)
                        body.enabled = false;
                    _rig.transform.position += new Vector3(_lastHead.x - head.x, 0f, _lastHead.z - head.z);
                    if (body != null)
                        body.enabled = true;
                    head = cam.position;
                    _still = 0f;
                }
            }

            _tracked = true;
            _lastCamLocal = camLocal;
            _lastHead = head;
        }

        Vector3 Local(Vector3 world) => _parent != null ? _parent.InverseTransformPoint(world) : world;
        Vector3 World(Vector3 local) => _parent != null ? _parent.TransformPoint(local) : local;
        Vector3 LocalDir(Vector3 world) => _parent != null ? _parent.InverseTransformDirection(world) : world;
    }
}
