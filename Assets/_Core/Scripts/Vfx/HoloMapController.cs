using System.Collections.Generic;
using System.Threading.Tasks;
using Core.App;
using Core.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Core.Vfx
{
    public enum HoloMapMode
    {
        System = 0,
        Galaxy = 1,
        HexBattle = 2
    }

    /// <summary>
    /// Holomap modes, zoom / pan and the MOVE ghost path.
    /// Zoom: both grips held over the table (hands apart = closer, together = wider; moving both hands
    /// together slides the galaxy), or the right thumbstick (up = closer). Zooming out past the system
    /// view opens the galaxy; zooming in past its closest level comes back to the system.
    /// </summary>
    public class HoloMapController : MonoBehaviour
    {
        HoloZoneMap _map;
        FocusContext _focus;
        FleetPoller _poller;
        HoloMapMode _mode = HoloMapMode.System;
        readonly HashSet<int> _selected = new();
        LineRenderer _ghost;
        float _zoom = 1f;
        const float SystemZoomMin = 0.8f;
        const float SystemZoomMax = 1.35f;
        /// <summary>Extra push past a zoom limit before the map changes level (no flicker at the edge).</summary>
        const float LevelSwitchPush = 1.22f;
        float _push = 1f;
        HexBattleController _hex;
        bool _movesLocked;

        public HoloMapMode Mode => _mode;
        public IReadOnlyCollection<int> SelectedFleetIds => _selected;
        public event System.Action<HoloMapMode> ModeChanged;

        public void Bind(HoloZoneMap map, FocusContext focus, FleetPoller poller)
        {
            _map = map;
            _focus = focus;
            _poller = poller;
            EnsureGhost();
            // Zoom: mouse wheel (Editor) + XR right thumbstick Y — no cube "zoom pokes".
            if (_map != null)
            {
                _map.TokensRebuilt -= OnTokensRebuilt;
                _map.TokensRebuilt += OnTokensRebuilt;
            }
        }

        public void BindHex(HexBattleController hex) => _hex = hex;

        void OnDestroy()
        {
            if (_map != null)
                _map.TokensRebuilt -= OnTokensRebuilt;
        }

        UnityEngine.InputSystem.Controls.Vector2Control _rightAxis;
        bool _rightAxisResolved;
        float _nextMoveLockRefresh;

        void OnEnable()
        {
            UnityEngine.InputSystem.InputSystem.onDeviceChange += OnDeviceChange;
        }

        void OnDisable()
        {
            UnityEngine.InputSystem.InputSystem.onDeviceChange -= OnDeviceChange;
        }

        void OnDeviceChange(UnityEngine.InputSystem.InputDevice device, UnityEngine.InputSystem.InputDeviceChange change)
        {
            // Controllers come and go (sleep, hand tracking): re-resolve the thumbstick lazily.
            _rightAxis = null;
            _rightAxisResolved = false;
            _handsResolved = false;
            EndGesture();
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextMoveLockRefresh)
            {
                _nextMoveLockRefresh = Time.unscaledTime + 0.25f;
                RefreshMoveLock();
            }

            if (_mode == HoloMapMode.HexBattle)
            {
                EndGesture();
                return;
            }

            if (UpdateTwoHandGesture())
                return;

            var stick = 0f;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
                stick = mouse.scroll.ReadValue().y * 0.05f;

            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null)
                stick += gamepad.rightStick.ReadValue().y;

            if (!_rightAxisResolved)
            {
                _rightAxis = ResolveRightAxis();
                _rightAxisResolved = true;
            }

            if (_rightAxis != null)
                stick += _rightAxis.ReadValue().y;

            if (Mathf.Abs(stick) > 0.15f)
                Zoom(Mathf.Pow(2f, Mathf.Clamp(stick, -1f, 1f) * 1.4f * Time.unscaledDeltaTime), Vector2.zero);
            else
                _push = 1f;
        }

        /// <summary>Scale the map by <paramref name="factor"/> around a table-local pivot; switches level at the ends.</summary>
        public void Zoom(float factor, Vector2 pivotLocal)
        {
            if (_map == null || factor <= 0f)
                return;
            if (_mode == HoloMapMode.Galaxy)
            {
                if (factor > 1f && _map.GalaxyAtMaxZoom)
                {
                    _push *= factor;
                    if (_push > LevelSwitchPush)
                    {
                        _push = 1f;
                        SetMode(HoloMapMode.System);
                        SetZoom(SystemZoomMin);
                    }

                    return;
                }

                _push = 1f;
                _map.GalaxyZoom(factor, pivotLocal);
                return;
            }

            if (factor < 1f && _zoom <= SystemZoomMin + 1e-3f)
            {
                _push /= factor;
                if (_push > LevelSwitchPush)
                {
                    _push = 1f;
                    SetZoom(1f);
                    SetMode(HoloMapMode.Galaxy);
                }

                return;
            }

            _push = 1f;
            SetZoom(_zoom * factor);
        }

        // ── Two-hand grip gesture ────────────────────────────────────────────────
        const float GestureReach = 1.25f;
        UnityEngine.InputSystem.Controls.Vector3Control _leftPos, _rightPos;
        UnityEngine.InputSystem.Controls.ButtonControl _leftGrip, _rightGrip;
        bool _handsResolved;
        bool _gesturing;
        Vector2 _gPrevA, _gPrevB;
        Unity.XR.CoreUtils.XROrigin _origin;

        bool UpdateTwoHandGesture()
        {
            if (!_handsResolved)
            {
                ResolveHands();
                _handsResolved = true;
            }

            if (_leftGrip == null || _rightGrip == null || _leftPos == null || _rightPos == null ||
                !_leftGrip.isPressed || !_rightGrip.isPressed || _map == null || _map.InteractionLocked)
            {
                EndGesture();
                return false;
            }

            if (_origin == null)
                _origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            var space = _origin != null && _origin.CameraFloorOffsetObject != null
                ? _origin.CameraFloorOffsetObject.transform
                : null;
            if (space == null)
            {
                EndGesture();
                return false;
            }

            var mount = _map.transform;
            var la = mount.InverseTransformPoint(space.TransformPoint(_leftPos.ReadValue()));
            var lb = mount.InverseTransformPoint(space.TransformPoint(_rightPos.ReadValue()));
            var a = new Vector2(la.x, la.z);
            var b = new Vector2(lb.x, lb.z);
            if (!_gesturing)
            {
                // Only over the table: two grips elsewhere (a console, the chair) are not a map gesture.
                if (a.magnitude > GestureReach || b.magnitude > GestureReach || la.y < -0.25f || lb.y < -0.25f ||
                    la.y > 0.9f || lb.y > 0.9f)
                    return false;
                _gesturing = true;
                _gPrevA = a;
                _gPrevB = b;
                _push = 1f;
                CicCue.Hover(mount.position);
                return true;
            }

            var prevDist = Mathf.Max(0.02f, (_gPrevB - _gPrevA).magnitude);
            var dist = Mathf.Max(0.02f, (b - a).magnitude);
            var mid = (a + b) * 0.5f;
            var prevMid = (_gPrevA + _gPrevB) * 0.5f;
            Zoom(dist / prevDist, mid);
            if (_mode == HoloMapMode.Galaxy)
                _map.GalaxyPan(mid - prevMid);
            _gPrevA = a;
            _gPrevB = b;
            return true;
        }

        void EndGesture()
        {
            if (!_gesturing)
                return;
            _gesturing = false;
            _push = 1f;
            _map?.GalaxyGestureEnd();
        }

        static bool HasUsage(UnityEngine.InputSystem.InputDevice device,
            UnityEngine.InputSystem.Utilities.InternedString usage)
        {
            foreach (var u in device.usages)
            {
                if (u == usage)
                    return true;
            }

            return false;
        }

        void ResolveHands()
        {
            _leftPos = _rightPos = null;
            _leftGrip = _rightGrip = null;
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device == null || !device.added)
                    continue;
                var left = HasUsage(device, UnityEngine.InputSystem.CommonUsages.LeftHand);
                var right = HasUsage(device, UnityEngine.InputSystem.CommonUsages.RightHand);
                if (!left && !right)
                    continue;
                var pos = device.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector3Control>("devicePosition");
                var grip = device.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("gripPressed");
                if (pos == null || grip == null)
                    continue;
                if (left && _leftPos == null)
                {
                    _leftPos = pos;
                    _leftGrip = grip;
                }
                else if (right && _rightPos == null)
                {
                    _rightPos = pos;
                    _rightGrip = grip;
                }
            }
        }

        /// <summary>OpenXR / Quest right controller primary2DAxis (thumbstick / trackpad).</summary>
        static UnityEngine.InputSystem.Controls.Vector2Control ResolveRightAxis()
        {
            foreach (var device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device == null || !device.added)
                    continue;
                var n = device.name ?? string.Empty;
                if (n.IndexOf("RightHand", System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.IndexOf("Right Controller", System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                var axis = device.TryGetChildControl<UnityEngine.InputSystem.Controls.Vector2Control>("primary2DAxis");
                if (axis != null)
                    return axis;
            }

            return null;
        }

        void RefreshMoveLock()
        {
            _movesLocked = false;
            if (_focus == null)
                return;
            var now = UnixNow();
            foreach (var fleet in _focus.Fleets)
            {
                if (fleet.Id == _focus.ViewFleetId && fleet.IsInBattle)
                {
                    _movesLocked = true;
                    break;
                }
            }
        }

        public bool MovesLocked => _movesLocked || _mode == HoloMapMode.HexBattle;

        /// <summary>System map scale on the table (galaxy zoom is the map's own view scale).</summary>
        public void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, SystemZoomMin, SystemZoomMax);
            if (_map != null)
                _map.transform.localScale = Vector3.one * (_mode == HoloMapMode.System ? _zoom : 1f);
        }

        public void SetMode(HoloMapMode mode)
        {
            if (_mode == mode)
                return;
            var prev = _mode;
            _mode = mode;
            if (_map != null && _map.VolumeRoot != null)
                _map.VolumeRoot.gameObject.SetActive(mode != HoloMapMode.HexBattle);
            if (_map != null)
                _map.transform.localScale = Vector3.one * (mode == HoloMapMode.System ? _zoom : 1f);
            if (mode == HoloMapMode.Galaxy && prev != HoloMapMode.Galaxy)
                _map?.ShowGalaxyAsync();
            else if (mode == HoloMapMode.System && prev == HoloMapMode.Galaxy)
                _map?.ShowSystemMap();
            ModeChanged?.Invoke(mode);
            if (_map != null)
            {
                var key = mode == HoloMapMode.Galaxy ? "galaxy"
                    : mode == HoloMapMode.HexBattle ? "battle"
                    : "system";
                _map.SetReadout(Trans.Get(key));
            }
            if (mode == HoloMapMode.HexBattle)
                _hex?.Show();
            else if (prev == HoloMapMode.HexBattle)
                _hex?.Hide();
        }

        public void ToggleSelect(int fleetId)
        {
            if (!_selected.Add(fleetId))
                _selected.Remove(fleetId);
        }

        public void ClearSelection() => _selected.Clear();

        public void ShowMoveGhost(Vector3 fromLocal, Vector3 toLocal)
        {
            EnsureGhost();
            if (_ghost == null || _map == null)
                return;
            _ghost.enabled = true;
            _ghost.positionCount = 2;
            _ghost.SetPosition(0, _map.transform.TransformPoint(fromLocal));
            _ghost.SetPosition(1, _map.transform.TransformPoint(toLocal));
        }

        /// <summary>Ghost from the token in-hand to the drop target (live preview while gripped).</summary>
        public void ShowMoveGhostWorld(Vector3 fromWorld, Vector3 toWorld)
        {
            EnsureGhost();
            if (_ghost == null)
                return;
            _ghost.enabled = true;
            _ghost.positionCount = 2;
            _ghost.SetPosition(0, fromWorld);
            _ghost.SetPosition(1, toWorld);
        }

        public void HideMoveGhost()
        {
            if (_ghost != null)
                _ghost.enabled = false;
        }

        void OnTokensRebuilt()
        {
            HideMoveGhost();
        }

        void EnsureGhost()
        {
            if (_ghost != null || _map == null)
                return;
            var go = new GameObject("MoveGhost");
            go.transform.SetParent(_map.transform, false);
            _ghost = go.AddComponent<LineRenderer>();
            var art = _map.GetComponentInParent<CicEnvironment>()?.Art;
            if (art != null)
                _ghost.sharedMaterial = art.Holo(
                    art.MoveGhost != null ? art.MoveGhost : Texture2D.whiteTexture,
                    new Color(0.25f, 1f, 1f, 0.9f));
            else
                _ghost.sharedMaterial = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color"));
            _ghost.startColor = new Color(1f, 0.72f, 0.25f, 0.95f);
            _ghost.endColor = new Color(0.2f, 1f, 1f, 0.9f);
            _ghost.startWidth = 0.038f;
            _ghost.endWidth = 0.018f;
            _ghost.useWorldSpace = true;
            _ghost.enabled = false;

            // Destroy legacy zoom cubes if an old session left them.
            var legacy = _map.transform.Find("ZoomPokes");
            if (legacy != null)
                Destroy(legacy.gameObject);
        }

        static long UnixNow() =>
            (long)(System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds;
    }
}
