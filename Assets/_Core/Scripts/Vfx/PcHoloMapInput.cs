using Core.App;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Vfx
{
    /// <summary>
    /// The holo table with a mouse (PC), what the two hands do in the headset — while the pointer (crosshair or
    /// free cursor) is on the table:
    /// - wheel: zoom around the point aimed at (galaxy ↔ system at the ends, as the hands do);
    /// - middle-drag: slide the map under the pointer (the galaxy's view, or the system diorama);
    /// - right-drag: turn the system diorama (in the galaxy, slide it like the middle button).
    /// Left click stays the click (pick a ship, give an order: TacticalCommand). Nothing while the table is
    /// locked by an order, during a hex battle, or while the pointer is on the viewscreen.
    /// </summary>
    public sealed class PcHoloMapInput : MonoBehaviour
    {
        HoloMapController _controller;
        HoloZoneMap _map;
        bool _dragging;
        bool _rotating;
        Vector3 _lastPoint;
        Vector2 _lastMouse;

        public void Bind(HoloMapController controller, HoloZoneMap map)
        {
            _controller = controller;
            _map = map;
        }

        void Update()
        {
            if (!PcPlatformBoot.IsPcDesktop)
                return;
            if (_controller == null)
                _controller = GetComponent<HoloMapController>();
            if (_map == null)
                _map = GetComponentInChildren<HoloZoneMap>();
            var mouse = Mouse.current;
            if (_controller == null || _map == null || mouse == null)
                return;

            var mousePos = mouse.position.ReadValue();
            var mouseDelta = mousePos - _lastMouse;
            _lastMouse = mousePos;

            var blocked = _map.InteractionLocked || _controller.Mode == HoloMapMode.HexBattle || PcPlatformBoot.IsTyping ||
                          Core.UI.FlatGrab.Holding || BridgeViewscreen.FlatPointerFrame >= Time.frameCount - 1;
            var onTable = false;
            Vector3 point = default;
            Vector3 pointLocal = default;
            if (!blocked && Core.UI.FlatPointer.TryAim(out var ray))
                onTable = _map.AimPlanePoint(ray.origin, ray.direction, 9f, out point, out pointLocal);

            if (!onTable)
            {
                EndDrag();
                return;
            }

            // 1. Wheel: zoom around the aimed point.
            var wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                var pivot = new Vector2(pointLocal.x, pointLocal.z);
                _controller.Zoom(Mathf.Pow(2f, Mathf.Sign(wheel) * 0.25f), pivot);
            }

            // 2. Middle-drag (or right-drag in the galaxy): the map follows the pointer.
            var galaxy = _controller.Mode == HoloMapMode.Galaxy;
            var panHeld = mouse.middleButton.isPressed || galaxy && mouse.rightButton.isPressed;
            if (panHeld)
            {
                if (_dragging)
                {
                    var d = point - _lastPoint;
                    if (galaxy)
                    {
                        var dl = _map.transform.InverseTransformVector(d);
                        _map.GalaxyPan(new Vector2(dl.x, dl.z));
                    }
                    else if (_map.ContentRoot != null)
                    {
                        _map.ContentRoot.position += new Vector3(d.x, 0f, d.z);
                        Core.Holo.MapManipulator.ClampContent(_map.ContentRoot);
                    }
                }

                _dragging = true;
                _lastPoint = point;
            }
            else if (_dragging)
            {
                EndDrag();
            }

            // 3. Right-drag on the system: turn the diorama about the vertical.
            if (!galaxy && mouse.rightButton.isPressed && _map.ContentRoot != null)
            {
                if (_rotating && Mathf.Abs(mouseDelta.x) > 0.01f)
                {
                    _map.ContentRoot.Rotate(Vector3.up, -mouseDelta.x * 0.3f, Space.Self);
                    Core.Holo.MapManipulator.ClampContent(_map.ContentRoot);
                }

                _rotating = true;
            }
            else
            {
                _rotating = false;
            }
        }

        void EndDrag()
        {
            if (_dragging && _controller != null && _controller.Mode == HoloMapMode.Galaxy)
                _map.GalaxyGestureEnd();
            _dragging = false;
            _rotating = false;
        }
    }
}
