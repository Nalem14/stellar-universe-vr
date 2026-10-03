using Core.App;
using Core.Stations;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Core.UI
{
    /// <summary>
    /// Mobile touch controller for Android & iOS:
    /// - Virtual floating joystick on left screen half (WASD movement & sprint)
    /// - Smooth touch swipe on right screen half (mouse look equivalent: yaw & pitch)
    /// - Tap detection on 3D objects (PokeButton, doors, chair, interactables)
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class MobileTouchController : MonoBehaviour
    {
        public static MobileTouchController Instance { get; private set; }

        [Header("Movement")]
        public float WalkSpeed = 2.8f;
        public float RunSpeed = 4.8f;
        public float Gravity = -14.0f;
        public float StandingEyeHeight = Core.Vfx.WorldScale.EyeStanding;
        public float JoystickRadius = 80f;

        [Header("Camera Look")]
        public float TouchSensitivity = 0.15f;
        public bool InvertY = false;

        Camera _camera;
        CharacterController _body;

        float _yaw;
        float _pitch;
        float _verticalVelocity;

        // Joystick tracking
        int _joystickFingerId = -1;
        Vector2 _joystickOrigin;
        Vector2 _joystickCurrent;
        Vector2 _moveInput;

        // Look tracking
        int _lookFingerId = -1;
        Vector2 _lookStartPos;
        float _lookStartTime;

        void Awake()
        {
            Instance = this;
            EnhancedTouchSupport.Enable();

            _body = GetComponentInChildren<CharacterController>();
            if (_body == null)
                _body = gameObject.AddComponent<CharacterController>();

            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
                _camera = Camera.main;

            if (_camera != null)
            {
                var rot = transform.localEulerAngles;
                _yaw = rot.y;
                _pitch = _camera.transform.localEulerAngles.x;
                if (_pitch > 180f)
                    _pitch -= 360f;
            }
        }

        Quaternion _applied = Quaternion.identity;

        /// <summary>Reach of a tap on the world: far enough for the exchange's crates and the holo table's tokens.</summary>
        const float TapReach = 9f;

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!PcPlatformBoot.IsMobile)
                return;

            // Rooms, doors and teleports turn the rig: follow it rather than snapping back to the old heading.
            if (Quaternion.Angle(transform.localRotation, _applied) > 0.01f)
            {
                _yaw = transform.localEulerAngles.y;
                _applied = transform.localRotation;
            }

            HandleTouches();
            // Something carried: it follows the screen centre, a tap on the target drops it (FlatGrab).
            FlatGrab.Tick();
            ApplyMovement();
        }

        static bool Seated => CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode;

        /// <summary>A finger on a HUD or holo-screen button (the Input System module keys pointers by touch id).</summary>
        static bool OverUi(Touch touch)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            return es != null && es.IsPointerOverGameObject(touch.touchId);
        }

        void HandleTouches()
        {
            var activeTouches = Touch.activeTouches;
            bool joystickActive = false;

            for (var i = 0; i < activeTouches.Count; i++)
            {
                var touch = activeTouches[i];
                var fingerId = touch.finger.index;
                var pos = touch.screenPosition;
                // Seated at the command post nobody walks: the whole screen is for looking and tapping the table.
                var isLeftSide = !Seated && pos.x < Screen.width * 0.48f;

                // 1. Manage Move Joystick (Left side)
                if (_joystickFingerId == fingerId)
                {
                    if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                    {
                        joystickActive = true;
                        _joystickCurrent = pos;
                        var delta = pos - _joystickOrigin;
                        float dist = delta.magnitude;
                        var dir = dist > 1e-4f ? delta.normalized : Vector2.zero;
                        float factor = Mathf.Clamp01(dist / JoystickRadius);
                        _moveInput = new Vector2(dir.x * factor, dir.y * factor);

                        MobileHud.Instance?.UpdateJoystickVisual(_joystickOrigin, _joystickOrigin + dir * Mathf.Min(dist, JoystickRadius));
                    }
                    else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        _joystickFingerId = -1;
                        _moveInput = Vector2.zero;
                        MobileHud.Instance?.ShowJoystick(false, Vector2.zero);
                    }
                }
                else if (_joystickFingerId == -1 && isLeftSide && touch.phase == TouchPhase.Began && !OverUi(touch))
                {
                    _joystickFingerId = fingerId;
                    _joystickOrigin = pos;
                    _joystickCurrent = pos;
                    _moveInput = Vector2.zero;
                    joystickActive = true;
                    MobileHud.Instance?.ShowJoystick(true, _joystickOrigin);
                }

                // 2. Manage Camera Look & Tap (Right side)
                if (_lookFingerId == fingerId)
                {
                    // Two fingers at the table are a pinch (MobileHoloMapInput), not a look.
                    if (touch.phase == TouchPhase.Moved && Seated && activeTouches.Count >= 2)
                    {
                        _lookStartPos = new Vector2(-9999f, -9999f);
                    }
                    else if (touch.phase == TouchPhase.Moved)
                    {
                        var delta = touch.delta * TouchSensitivity;
                        _yaw += delta.x;
                        var yFactor = InvertY ? 1f : -1f;
                        _pitch = Mathf.Clamp(_pitch + delta.y * yFactor, -80f, 80f);

                        transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
                        _applied = transform.localRotation;
                        if (_camera != null)
                            _camera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                    }
                    else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        float duration = Time.unscaledTime - _lookStartTime;
                        float moveDist = (pos - _lookStartPos).magnitude;

                        // Quick tap detected (and not on a button: the UI already took it)
                        if (duration < 0.25f && moveDist < 25f && !OverUi(touch))
                        {
                            PerformTap(pos);
                        }

                        _lookFingerId = -1;
                    }
                    else if (touch.phase == TouchPhase.Stationary)
                    {
                    }
                }
                else if (_lookFingerId == -1 && !isLeftSide && touch.phase == TouchPhase.Began)
                {
                    // Check if touching a UI element
                    if (!OverUi(touch))
                    {
                        _lookFingerId = fingerId;
                        _lookStartPos = pos;
                        _lookStartTime = Time.unscaledTime;
                    }
                }
            }

            if (!joystickActive && _joystickFingerId != -1)
            {
                _joystickFingerId = -1;
                _moveInput = Vector2.zero;
                MobileHud.Instance?.ShowJoystick(false, Vector2.zero);
            }
        }

        void ApplyMovement()
        {
            if (_body == null || !_body.enabled)
                return;

            // When seated in command mode, locomotion is paused
            if (CaptainCommandMode.Instance != null && CaptainCommandMode.Instance.IsCommandMode)
                return;

            bool isRunning = _moveInput.magnitude > 0.85f;
            float speed = isRunning ? RunSpeed : WalkSpeed;

            var inputDir = new Vector3(_moveInput.x, 0f, _moveInput.y);
            Vector3 worldMove = transform.TransformDirection(inputDir) * speed;

            if (_body.isGrounded)
                _verticalVelocity = -2f;
            else
                _verticalVelocity += Gravity * Time.deltaTime;

            worldMove.y = _verticalVelocity;
            _body.Move(worldMove * Time.deltaTime);
        }

        public void PerformTap(Vector2 screenPos)
        {
            if (_camera == null)
                return;

            var ray = _camera.ScreenPointToRay(screenPos);
            // Carrying something: this tap is where it goes.
            if (FlatGrab.Holding)
            {
                FlatPointer.PostTap(ray, screenPos);
                return;
            }

            if (!FlatPointer.FindTarget(ray, TapReach, out var hit))
            {
                // Nothing that reacts here: the tap goes to the world systems (holo table, hex board, viewscreen).
                FlatPointer.PostTap(ray, screenPos);
                return;
            }

            var hitGo = hit.collider.gameObject;
            var grab = hitGo.GetComponentInParent<FlatGrabbable>();
            var interactable = hitGo.GetComponentInParent<XRSimpleInteractable>();
            var door = hitGo.GetComponentInParent<RoomDoor>();
            bool isSeat = hitGo.name.Contains("CaptainSeat") || hitGo.name.Contains("SitZone");

            if (grab != null && FlatGrab.TryBegin(grab))
                return;
            if (isSeat)
            {
                CaptainCommandMode.Instance?.EnterCommandMode();
                return;
            }

            if (door != null)
            {
                door.SendMessage("Request", UnityEngine.SendMessageOptions.DontRequireReceiver);
                return;
            }

            if (interactable != null)
            {
                // Press and release, as a poke would (PokeButton included: its cap comes back up).
                try
                {
                    interactable.selectEntered?.Invoke(new SelectEnterEventArgs { interactableObject = interactable });
                    interactable.selectExited?.Invoke(new SelectExitEventArgs { interactableObject = interactable });
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }

                return;
            }

            FlatPointer.PostTap(ray, screenPos);
        }

        /// <summary>Triggered by the onscreen Action button</summary>
        public void PerformCenterAction()
        {
            if (_camera == null)
                return;
            PerformTap(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
        }
    }
}
