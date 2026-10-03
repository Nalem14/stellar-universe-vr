using Unity.XR.CoreUtils;
using UnityEngine;

namespace Core.App
{
    /// <summary>
    /// Safety net for every room: in room-scale the head can walk through a virtual wall, and past the floor's
    /// edge the character body falls into space. The guard remembers the last spot where the player stood still
    /// on a floor (in the rig's parent space — the bridge flies with its ship) and, if the body drops more than
    /// a metre under it, puts the head straight back there. It also absorbs tracking jumps: when the headset's
    /// tracking starts late or the player recenters, the head leaps in tracking space in one frame — no body
    /// moves that fast — and the rig is shifted so the head stays where it was in the room, instead of outside it.
    /// Every <see cref="XrPlacement.PlaceHead"/> hands its spot here: a scene places the head before the camera's
    /// pose driver has delivered the real headset pose, so for a short while after a placement the head is held
    /// on that spot (one correction), and a fall before any safe spot is known lands back on it.
    /// Nothing per frame but a few vector ops.
    /// </summary>
    public sealed class FallGuard : MonoBehaviour
    {
        const float DropLimit = 1.0f;
        const float SettleTime = 0.35f;
        /// <summary>How long after a placement the head is held on its spot (s, unscaled).</summary>
        const float HoldTime = 1.5f;
        /// <summary>Head offset from the placed spot (m) that only a late pose can make that fast.</summary>
        const float HoldSlack = 0.25f;

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

        Transform _spawnParent;
        Vector3 _spawnLocal;
        Vector3 _spawnForward = Vector3.forward;
        bool _hasSpawn;
        float _hold;

        /// <summary>Head travel in tracking space in one frame (m) that only a tracking jump can make.</summary>
        const float TrackingJump = 0.3f;

        public static void Ensure()
        {
            var rig = FindFirstObjectByType<XROrigin>();
            if (rig != null)
                Of(rig);
        }

        public static FallGuard Of(XROrigin rig)
        {
            var guard = rig.GetComponent<FallGuard>();
            return guard != null ? guard : rig.gameObject.AddComponent<FallGuard>();
        }

        /// <summary>The head was just put on <paramref name="floorPoint"/> (world).</summary>
        public void Placed(Vector3 floorPoint, Vector3 forward)
        {
            _spawnParent = transform.parent;
            _spawnLocal = ToLocal(_spawnParent, floorPoint);
            _spawnForward = _spawnParent != null ? _spawnParent.InverseTransformDirection(forward) : forward;
            _hasSpawn = true;
            // The hold waits for a headset's late pose; a flat-screen body may walk straight away.
            _hold = Core.App.PcPlatformBoot.IsFlatScreen ? 0f : HoldTime;
            _still = 0f;
            if (_rig != null && _rig.Camera != null)
            {
                _lastCamLocal = _rig.Camera.transform.localPosition;
                _lastHead = _rig.Camera.transform.position;
            }
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

            if (_hasSpawn && _spawnParent != _parent)
                _hasSpawn = false;

            AbsorbTrackingJump();
            if (HoldOnSpawn())
                return;

            var local = Local(t.position);
            if (_hasSafe && local.y < _safeLocal.y - DropLimit)
            {
                Rescue(_safeLocal, _safeForward);
                return;
            }

            if (!_hasSafe && _hasSpawn && local.y < _spawnLocal.y - DropLimit)
            {
                // Fell before standing anywhere: back to the spot the scene placed the head on.
                Rescue(_spawnLocal, _spawnForward);
                return;
            }

            if (!_hasSafe && !_hasSpawn && local.y < -30f)
            {
                // Dropped into a room before it was ready (no spot at all): back to the room's origin floor.
                Rescue(Vector3.zero, Vector3.forward);
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

        /// <summary>True while the head was put back on the placed spot this frame.</summary>
        bool HoldOnSpawn()
        {
            if (!_hasSpawn || _hold <= 0f)
                return false;
            _hold -= Time.unscaledDeltaTime;
            var head = _rig.Camera.transform.position;
            var spot = World(_spawnLocal);
            var off = new Vector2(head.x - spot.x, head.z - spot.z);
            if (off.magnitude <= HoldSlack)
                return false;
            // A late pose lands in one go: one correction, then the player walks freely.
            _hold = 0f;
            Rescue(_spawnLocal, _spawnForward);
            return true;
        }

        void Rescue(Vector3 floorLocal, Vector3 forwardLocal)
        {
            var spot = World(floorLocal);
            XrPlacement.Snap(_rig, spot, World(floorLocal + forwardLocal) - spot);
            _lastY = Local(_rig.transform.position).y;
            _still = 0f;
            _lastCamLocal = _rig.Camera.transform.localPosition;
            _lastHead = _rig.Camera.transform.position;
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

        static Vector3 ToLocal(Transform parent, Vector3 world) => parent != null ? parent.InverseTransformPoint(world) : world;
        Vector3 Local(Vector3 world) => ToLocal(_parent, world);
        Vector3 World(Vector3 local) => _parent != null ? _parent.TransformPoint(local) : local;
        Vector3 LocalDir(Vector3 world) => _parent != null ? _parent.InverseTransformDirection(world) : world;
    }
}
