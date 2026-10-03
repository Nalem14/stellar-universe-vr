using Core.App;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Core.Vfx
{
    /// <summary>
    /// The holo table with two fingers (mobile), what the two hands do in the headset — from the command seat
    /// (the table in reach; two fingers while walking are the joystick and the look):
    /// - pinch: zoom around the point between the fingers (galaxy ↔ system at the ends);
    /// - slide both fingers: the map follows them (the galaxy's view, or the system diorama);
    /// - twist: turn the system diorama.
    /// A single tap stays the tap (pick a ship, give an order: TacticalCommand). Nothing while the table is locked
    /// by an order, during a hex battle, or over the viewscreen.
    /// </summary>
    public sealed class MobileHoloMapInput : MonoBehaviour
    {
        HoloMapController _controller;
        HoloZoneMap _map;
        bool _active;
        float _prevDist;
        float _prevAngle;
        Vector3 _prevPoint;

        public void Bind(HoloMapController controller, HoloZoneMap map)
        {
            _controller = controller;
            _map = map;
        }

        void Update()
        {
            if (!PcPlatformBoot.IsMobile)
                return;
            if (!EnhancedTouchSupport.enabled)
                EnhancedTouchSupport.Enable();
            if (_controller == null)
                _controller = GetComponent<HoloMapController>();
            if (_map == null)
                _map = GetComponentInChildren<HoloZoneMap>();
            if (_controller == null || _map == null)
                return;

            var seated = CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode;
            var touches = Touch.activeTouches;
            var cam = Camera.main;
            if (!seated || touches.Count < 2 || cam == null || _map.InteractionLocked || _controller.Mode == HoloMapMode.HexBattle ||
                Core.UI.FlatGrab.Holding || BridgeViewscreen.FlatPointerFrame >= Time.frameCount - 1)
            {
                End();
                return;
            }

            var p0 = touches[0].screenPosition;
            var p1 = touches[1].screenPosition;
            var ray = cam.ScreenPointToRay((p0 + p1) * 0.5f);
            if (!_map.AimPlanePoint(ray.origin, ray.direction, 9f, out var point, out var local))
            {
                End();
                return;
            }

            var dist = Mathf.Max(1f, Vector2.Distance(p0, p1));
            var dir = p1 - p0;
            var angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            if (_active)
            {
                // Pinch: continuous zoom around the fingers' midpoint.
                var ratio = dist / _prevDist;
                if (Mathf.Abs(ratio - 1f) > 0.002f)
                    _controller.Zoom(ratio, new Vector2(local.x, local.z));

                // Slide: the map follows the midpoint.
                var d = point - _prevPoint;
                if (_controller.Mode == HoloMapMode.Galaxy)
                {
                    var dl = _map.transform.InverseTransformVector(d);
                    _map.GalaxyPan(new Vector2(dl.x, dl.z));
                }
                else if (_map.ContentRoot != null)
                {
                    _map.ContentRoot.position += new Vector3(d.x, 0f, d.z);
                    // Twist: turn the diorama.
                    var turn = Mathf.DeltaAngle(_prevAngle, angle);
                    if (Mathf.Abs(turn) > 0.2f)
                        _map.ContentRoot.Rotate(Vector3.up, -turn, Space.Self);
                    Core.Holo.MapManipulator.ClampContent(_map.ContentRoot);
                }
            }

            _active = true;
            _prevDist = dist;
            _prevAngle = angle;
            // Re-aim after the zoom / slide moved the map under the fingers.
            _prevPoint = _map.AimPlanePoint(ray.origin, ray.direction, 9f, out var after, out _) ? after : point;
        }

        void End()
        {
            if (_active && _controller != null && _controller.Mode == HoloMapMode.Galaxy && _map != null)
                _map.GalaxyGestureEnd();
            _active = false;
        }
    }
}
