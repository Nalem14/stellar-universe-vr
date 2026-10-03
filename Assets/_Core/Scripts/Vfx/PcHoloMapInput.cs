using Core.App;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core.Vfx
{
    /// <summary>
    /// Desktop keyboard & mouse controls for the holographic table (HoloZoneMap / HoloMapController):
    /// - Mouse Wheel: Smooth zoom between System and Galaxy views
    /// - Middle Click (or Left Click on empty space) + Drag: Pan the map horizontally
    /// - Right Click + Drag: Rotate the map around the Y axis
    /// </summary>
    public sealed class PcHoloMapInput : MonoBehaviour
    {
        HoloMapController _controller;
        HoloZoneMap _map;
        Vector2 _lastMousePos;
        bool _isPanning;
        bool _isRotating;

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

            if (_controller == null || _map == null)
                return;

            var mouse = Mouse.current;
            if (mouse == null)
                return;

            var currentMousePos = mouse.position.ReadValue();
            var mouseDelta = currentMousePos - _lastMousePos;
            _lastMousePos = currentMousePos;

            // Only process table manipulation when cursor is free (Command Mode or Tab unlocked)
            var pcCtrl = PcDesktopController.Instance;
            if (pcCtrl != null && pcCtrl.IsCursorLocked)
                return;

            // 1. Zoom via mouse scroll wheel
            float scroll = mouse.scroll.ReadValue().y * 0.001f;
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                HandleZoom(scroll);
            }

            // 2. Pan via middle mouse drag
            if (mouse.middleButton.wasPressedThisFrame)
                _isPanning = true;
            else if (mouse.middleButton.wasReleasedThisFrame)
                _isPanning = false;

            if (_isPanning && _map.ContentRoot != null)
            {
                var panDelta = new Vector3(mouseDelta.x, 0f, mouseDelta.y) * 0.0015f;
                _map.ContentRoot.localPosition += panDelta;
            }

            // 3. Rotate via right mouse drag
            if (mouse.rightButton.wasPressedThisFrame)
                _isRotating = true;
            else if (mouse.rightButton.wasReleasedThisFrame)
                _isRotating = false;

            if (_isRotating && _map.ContentRoot != null)
            {
                float rotAmount = mouseDelta.x * 0.35f;
                _map.ContentRoot.Rotate(Vector3.up, -rotAmount, Space.Self);
            }
        }

        void HandleZoom(float delta)
        {
            if (_controller == null)
                return;

            if (_controller.Mode == HoloMapMode.System)
            {
                if (delta < -0.05f)
                {
                    // Zooming out past system limit switches to galaxy
                    _controller.SetMode(HoloMapMode.Galaxy);
                }
            }
            else if (_controller.Mode == HoloMapMode.Galaxy)
            {
                if (delta > 0.05f)
                {
                    // Zooming in past galaxy limit switches back to system
                    _controller.SetMode(HoloMapMode.System);
                }
            }
        }
    }
}
