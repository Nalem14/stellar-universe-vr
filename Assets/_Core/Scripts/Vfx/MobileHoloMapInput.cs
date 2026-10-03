using Core.App;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Core.Vfx
{
    /// <summary>
    /// Mobile touch gestures for the holographic table (HoloZoneMap / HoloMapController):
    /// - 2-finger pinch: Zoom between System and Galaxy
    /// - 2-finger rotate: Rotate map on table
    /// - 2-finger pan: Drag map horizontally
    /// </summary>
    public sealed class MobileHoloMapInput : MonoBehaviour
    {
        HoloMapController _controller;
        HoloZoneMap _map;

        float _prevPinchDist = -1f;
        float _prevAngle = 0f;
        Vector2 _prevCenter;

        public void Bind(HoloMapController controller, HoloZoneMap map)
        {
            _controller = controller;
            _map = map;
        }

        bool _pinchFlipped;
        /// <summary>Finger spread when the pinch began: the flip reads the whole gesture, not one frame.</summary>
        float _pinchStart;

        void Update()
        {
            if (!PcPlatformBoot.IsMobile)
                return;

            if (_controller == null)
                _controller = GetComponent<HoloMapController>();
            if (_map == null)
                _map = GetComponentInChildren<HoloZoneMap>();

            if (_controller == null || _map == null)
                return;

            var touches = Touch.activeTouches;
            // The table is in reach only from the command seat: two fingers while walking are the joystick and
            // the look, not a pinch.
            var seated = CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode;
            if (touches.Count < 2 || !seated)
            {
                _prevPinchDist = -1f;
                _pinchFlipped = false;
                return;
            }

            var t0 = touches[0];
            var t1 = touches[1];

            var pos0 = t0.screenPosition;
            var pos1 = t1.screenPosition;

            float dist = Vector2.Distance(pos0, pos1);
            var center = (pos0 + pos1) * 0.5f;
            var dir = pos1 - pos0;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            if (_prevPinchDist <= 0f)
                _pinchStart = dist;
            else
            {
                // 1. Pinch zoom
                float distRatio = dist / Mathf.Max(1f, _pinchStart);
                // One galaxy / system flip per pinch, not one per frame.
                if (!_pinchFlipped && Mathf.Abs(distRatio - 1f) > 0.04f)
                {
                    if (distRatio > 1.25f && _controller.Mode == HoloMapMode.Galaxy)
                    {
                        _controller.SetMode(HoloMapMode.System);
                        _pinchFlipped = true;
                    }
                    else if (distRatio < 0.8f && _controller.Mode == HoloMapMode.System)
                    {
                        _controller.SetMode(HoloMapMode.Galaxy);
                        _pinchFlipped = true;
                    }
                }

                // 2. Rotate table
                float angleDelta = Mathf.DeltaAngle(_prevAngle, angle);
                if (Mathf.Abs(angleDelta) > 0.4f && _map.ContentRoot != null)
                {
                    _map.ContentRoot.Rotate(Vector3.up, -angleDelta * 0.8f, Space.Self);
                }

                // 3. Pan table
                var panDelta = center - _prevCenter;
                if (panDelta.sqrMagnitude > 4f && _map.ContentRoot != null)
                {
                    _map.ContentRoot.localPosition += new Vector3(panDelta.x, 0f, panDelta.y) * 0.0008f;
                }
            }

            _prevPinchDist = dist;
            _prevAngle = angle;
            _prevCenter = center;
        }
    }
}
