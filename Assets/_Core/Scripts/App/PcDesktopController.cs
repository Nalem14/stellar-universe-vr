using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

namespace Core.App
{
    /// <summary>
    /// First-person desktop controller for PC:
    /// - Mouse look (yaw on body, pitch on camera with clamp)
    /// - WASD / ZQSD locomotion with sprint (Left Shift)
    /// - CharacterController collision & gravity
    /// - Cursor lock management (Tab / Alt to toggle, or programmatic unlock for UI/Command Mode)
    /// </summary>
    public sealed class PcDesktopController : MonoBehaviour
    {
        public static PcDesktopController Instance { get; private set; }

        const string PrefSens = "su.pc.mousesens";
        const string PrefInvert = "su.pc.inverty";

        [Header("Movement")]
        public float WalkSpeed = 2.8f;
        public float RunSpeed = 4.8f;
        public float Gravity = -14.0f;
        public float StandingEyeHeight = 1.65f;

        [Header("Mouse Look")]
        public float MouseSensitivity = 2.0f;
        public bool InvertY = false;

        XROrigin _origin;
        Camera _camera;
        CharacterController _body;

        float _yaw;
        float _pitch;
        float _verticalVelocity;
        bool _cursorLocked = true;
        bool _manualUnlock = false;

        public bool IsCursorLocked => _cursorLocked;
        public Camera PlayerCamera => _camera;

        void Awake()
        {
            Instance = this;
            MouseSensitivity = PlayerPrefs.GetFloat(PrefSens, 2.0f);
            InvertY = PlayerPrefs.GetInt(PrefInvert, 0) == 1;

            _origin = GetComponent<XROrigin>();
            _body = GetComponentInChildren<CharacterController>();
            if (_body == null)
                _body = gameObject.AddComponent<CharacterController>();

            _camera = _origin != null && _origin.Camera != null ? _origin.Camera : GetComponentInChildren<Camera>();
            if (_camera == null)
                _camera = Camera.main;

            // Initialize camera angles from current rotation
            var rot = transform.localEulerAngles;
            _yaw = rot.y;
            if (_camera != null)
                _pitch = _camera.transform.localEulerAngles.x;
            if (_pitch > 180f)
                _pitch -= 360f;

            SetCursorLock(true);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            SetCursorLock(false);
        }

        void Start()
        {
            AdjustCameraHeight(StandingEyeHeight);
        }

        public void AdjustCameraHeight(float height)
        {
            if (_origin != null && _origin.CameraFloorOffsetObject != null)
            {
                _origin.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, height, 0f);
            }
            else if (_camera != null)
            {
                _camera.transform.localPosition = new Vector3(0f, height, 0f);
            }
        }

        void Update()
        {
            HandleCursorToggle();

            if (_cursorLocked)
            {
                HandleMouseLook();
            }

            HandleMovement();
        }

        void HandleCursorToggle()
        {
            var kb = Keyboard.current;
            if (kb == null)
                return;

            // Tab or Alt toggles free cursor
            if (kb.tabKey.wasPressedThisFrame || kb.leftAltKey.wasPressedThisFrame)
            {
                _manualUnlock = !_manualUnlock;
                SetCursorLock(!_manualUnlock);
            }

            // Clicking into the window re-locks cursor if not manually unlocked or in seated command mode
            var mouse = Mouse.current;
            if (!_cursorLocked && !_manualUnlock && !IsInSeatedCommandMode())
            {
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    // If not clicking on a UI element
                    if (!UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
                    {
                        SetCursorLock(true);
                    }
                }
            }
        }

        public void SetCursorLock(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        bool IsInSeatedCommandMode()
        {
            return CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode;
        }

        void HandleMouseLook()
        {
            var mouse = Mouse.current;
            if (mouse == null || _camera == null)
                return;

            var delta = mouse.delta.ReadValue() * 0.1f * MouseSensitivity;
            if (delta.sqrMagnitude < 1e-4f)
                return;

            _yaw += delta.x;
            var yFactor = InvertY ? 1f : -1f;
            _pitch = Mathf.Clamp(_pitch + delta.y * yFactor, -82f, 82f);

            // Yaw rotates the player rig / body
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            // Pitch tilts the camera
            _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void HandleMovement()
        {
            if (_body == null || !_body.enabled)
                return;

            // When seated in command mode, locomotion is paused
            if (IsInSeatedCommandMode())
                return;

            var kb = Keyboard.current;
            if (kb == null)
                return;

            // Dual ZQSD (French AZERTY) & WASD (English QWERTY) support without remapping
            float moveX = 0f;
            float moveZ = 0f;

            if (kb.wKey.isPressed || kb.zKey.isPressed || kb.upArrowKey.isPressed)
                moveZ += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed)
                moveZ -= 1f;
            if (kb.aKey.isPressed || kb.qKey.isPressed || kb.leftArrowKey.isPressed)
                moveX -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed)
                moveX += 1f;

            var inputDir = new Vector3(moveX, 0f, moveZ).normalized;
            bool isRunning = kb.leftShiftKey.isPressed;
            float currentSpeed = isRunning ? RunSpeed : WalkSpeed;

            // Move relative to the player's yaw
            Vector3 worldMove = transform.TransformDirection(inputDir) * currentSpeed;

            // Handle gravity & ground sticking
            if (_body.isGrounded)
            {
                _verticalVelocity = -2f; // Slight downward push to maintain ground contact
            }
            else
            {
                _verticalVelocity += Gravity * Time.deltaTime;
            }

            worldMove.y = _verticalVelocity;

            _body.Move(worldMove * Time.deltaTime);
        }

        public void SaveSettings(float sens, bool invert)
        {
            MouseSensitivity = sens;
            InvertY = invert;
            PlayerPrefs.SetFloat(PrefSens, sens);
            PlayerPrefs.SetInt(PrefInvert, invert ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
