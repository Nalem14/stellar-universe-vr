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
    /// Holomap modes + pinch zoom stub + multi-select + MOVE ghost path.
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
        }

        void Update()
        {
            if (Time.unscaledTime >= _nextMoveLockRefresh)
            {
                _nextMoveLockRefresh = Time.unscaledTime + 0.25f;
                RefreshMoveLock();
            }

            var scroll = 0f;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
                scroll = mouse.scroll.ReadValue().y * 0.01f;

            var gamepad = UnityEngine.InputSystem.Gamepad.current;
            if (gamepad != null)
                scroll += gamepad.rightStick.ReadValue().y * 0.02f;

            if (!_rightAxisResolved)
            {
                _rightAxis = ResolveRightAxis();
                _rightAxisResolved = true;
            }

            if (_rightAxis != null)
                scroll += _rightAxis.ReadValue().y * 0.025f;

            if (Mathf.Abs(scroll) > 0.01f && _mode != HoloMapMode.HexBattle)
                SetZoom(_zoom + scroll * 0.08f);
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

        public void SetZoom(float zoom)
        {
            _zoom = Mathf.Clamp(zoom, 0.65f, 1.85f);
            if (_map != null && _map.transform != null)
            {
                var t = MotionEase.Smooth01((_zoom - 0.65f) / (1.85f - 0.65f));
                _map.transform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.35f, t);
            }

            if (_zoom > 1.45f && _mode == HoloMapMode.System)
                SetMode(HoloMapMode.Galaxy);
            else if (_zoom < 1.15f && _mode == HoloMapMode.Galaxy)
                SetMode(HoloMapMode.System);
        }

        public void SetMode(HoloMapMode mode)
        {
            if (_mode == mode)
                return;
            var prev = _mode;
            _mode = mode;
            if (_map != null && _map.VolumeRoot != null)
                _map.VolumeRoot.gameObject.SetActive(mode != HoloMapMode.HexBattle);
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
