using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Core.App
{
    /// <summary>
    /// The player's comfort choices, kept on the headset and applied to whichever rig the scene has:
    /// <list type="bullet">
    /// <item>moving: smooth (left stick walks) or teleport (left stick forward aims an arc onto the floor); in
    /// smooth mode, clicking the left stick toggles a run, which drops back to a walk once the player stops;</item>
    /// <item>turning: smooth or snap (right stick; its forward push stays the holo map's zoom, never a teleport);</item>
    /// <item>the tunnelling vignette (the dark ring while moving) on or off;</item>
    /// <item>seated play: the view is lifted to standing height, except in the captain's chair, which sets
    /// its own (see <see cref="CaptainCommandMode.ViewLift"/>).</item>
    /// </list>
    /// Teleport areas are laid on the room floors the first time the arc comes out in each room.
    /// </summary>
    public sealed class ComfortSettings : MonoBehaviour
    {
        const string TeleportKey = "su.move.teleport";
        const string SmoothTurnKey = "su.turn.smooth";
        const string VignetteKey = "su.comfort.vignette";
        const string SeatedKey = "su.seated";

        /// <summary>Standing eye height minus seated eye height (m).</summary>
        public const float SeatedLift = 0.45f;

        /// <summary>Run speed over the rig's walk speed (2.5 → 4.5 m/s).</summary>
        const float RunFactor = 1.8f;
        /// <summary>Stick at rest this long (s) after moving ends the run.</summary>
        const float RunStopDelay = 0.3f;
        /// <summary>A run toggled without moving lapses after this long (s).</summary>
        const float RunIdleLapse = 3f;

        /// <summary>Walkable floors across the rooms (bridge, corridor, quarters, lab, dock, diplomacy, gate, sas).</summary>
        static readonly HashSet<string> Floors = new()
        {
            "Deck", "Floor", "CommandPlate", "CaptainDais", "GalleryFloor", "HallFloor", "BayFloor", "Dais", "Causeway",
            "OpsTier", "SillFloor"
        };

        static ComfortSettings s_Instance;

        /// <summary>The view lift off the captain's chair (m): seated play lifts the view to standing height.</summary>
        public static float StandingLift => Seated ? SeatedLift : 0f;

        /// <summary>The view lift applied right now (m): seated play, out of the captain's chair.</summary>
        public static float CurrentLift { get; private set; }

        public static bool Teleport
        {
            get => PlayerPrefs.GetInt(TeleportKey, 0) == 1;
            set => Save(TeleportKey, value);
        }

        public static bool SmoothTurn
        {
            get => PlayerPrefs.GetInt(SmoothTurnKey, 0) == 1;
            set => Save(SmoothTurnKey, value);
        }

        public static bool Vignette
        {
            get => PlayerPrefs.GetInt(VignetteKey, 1) == 1;
            set => Save(VignetteKey, value);
        }

        public static bool Seated
        {
            get => PlayerPrefs.GetInt(SeatedKey, 0) == 1;
            set => Save(SeatedKey, value);
        }

        static void Save(string key, bool on)
        {
            PlayerPrefs.SetInt(key, on ? 1 : 0);
            PlayerPrefs.Save();
            if (s_Instance != null)
                s_Instance._dirty = true;
        }

        XROrigin _rig;
        ControllerInputActionManager _left;
        ControllerInputActionManager _right;
        XRRayInteractor _leftTeleport;
        InputAction _rightTeleport;
        InputAction _rightTeleportCancel;
        TunnelingVignetteController _vignette;
        bool _dirty = true;
        float _nextTry;
        bool _arcWasOut;
        ContinuousMoveProvider _move;
        float _walkSpeed;
        InputAction _runToggle;
        bool _running;
        bool _movedSinceRun;
        float _stillFor;

        /// <summary>The player is running (left stick clicked, still moving).</summary>
        public static bool Running => s_Instance != null && s_Instance._running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("ComfortSettings");
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<ComfortSettings>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnScene;
            _runToggle ??= new InputAction("RunToggle", InputActionType.Button, "<XRController>{LeftHand}/{Primary2DAxisClick}");
            _runToggle.Enable();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnScene;
            _runToggle?.Disable();
        }

        void OnScene(Scene s, LoadSceneMode m)
        {
            _rig = null;
            _dirty = true;
            _nextTry = 0f;
        }

        void LateUpdate()
        {
            if (_rig == null && !_dirty)
                _dirty = true;
            if (_dirty && Time.unscaledTime >= _nextTry)
            {
                _nextTry = Time.unscaledTime + 1f;
                if (Resolve())
                {
                    ApplyNow();
                    _dirty = false;
                }
            }

            if (_rig == null)
                return;

            // The right stick is turn + holo zoom: its "push forward to teleport" would also blank the right ray.
            if (_rightTeleport != null && _rightTeleport.enabled)
                _rightTeleport.Disable();
            if (_rightTeleportCancel != null && _rightTeleportCancel.enabled)
                _rightTeleportCancel.Disable();

            ApplyLift();
            UpdateRun();

            if (_leftTeleport != null)
            {
                var arc = _leftTeleport.gameObject.activeInHierarchy;
                if (arc && !_arcWasOut)
                    LayFloors(_leftTeleport.interactionLayers);
                _arcWasOut = arc;
            }
        }

        bool Resolve()
        {
            _rig = FindFirstObjectByType<XROrigin>();
            if (_rig == null)
                return false;
            _left = _right = null;
            foreach (var m in _rig.GetComponentsInChildren<ControllerInputActionManager>(true))
            {
                if (m.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _left = m;
                else if (m.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    _right = m;
            }

            const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var type = typeof(ControllerInputActionManager);
            _leftTeleport = _left != null ? type.GetField("m_TeleportInteractor", Flags)?.GetValue(_left) as XRRayInteractor : null;
            _rightTeleport = _right != null ? (type.GetField("m_TeleportMode", Flags)?.GetValue(_right) as InputActionReference)?.action : null;
            _rightTeleportCancel = _right != null ? (type.GetField("m_TeleportModeCancel", Flags)?.GetValue(_right) as InputActionReference)?.action : null;
            if (_right != null && _rightTeleport != null)
            {
                // Unplug the right hand's teleport at the source: a turn pushed slightly forward armed the arc,
                // and on release it teleported (and turned) the player. Re-enabling runs the manager's
                // teardown with the old references, then its setup without them.
                _right.enabled = false;
                type.GetField("m_TeleportMode", Flags)?.SetValue(_right, null);
                type.GetField("m_TeleportModeCancel", Flags)?.SetValue(_right, null);
                _right.enabled = true;
            }

            // Stick back on the right is the holo map's zoom out, never a 180° turn.
            foreach (var snap in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider>(true))
                snap.enableTurnAround = false;
            // The smooth turn has its own "stick back = instant 180°" too: a sideways push read as a pull-back
            // spun the captain round.
            foreach (var smooth in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.ContinuousTurnProvider>(true))
                smooth.enableTurnAround = false;
            // An object held at a distance stays where the ray took it: the stick never spins it or reels it in.
            // With that manipulation on, the starter kit cut turning (and moving) for the whole hold, so a module
            // carried from the dock's store could not be turned round to the assembly table.
            foreach (var nearFar in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.NearFarInteractor>(true))
                if (nearFar.interactionAttachController is UnityEngine.XR.Interaction.Toolkit.Attachment.InteractionAttachController attach)
                {
                    attach.useManipulationInput = false;
                    // The kit's own test reads the input source too (an operator-precedence slip): unplug it as well.
                    if (attach.manipulationInput != null)
                        attach.manipulationInput.inputSourceMode = UnityEngine.XR.Interaction.Toolkit.Inputs.Readers.XRInputValueReader.InputSourceMode.Unused;
                }
            // Straight rays: the curve visual bends toward a hit point it smooths in world space, so aboard a moving
            // ship the end lagged behind and the ray flexed as if dragged. The teleport arc keeps its parabola,
            // without the world-space smoothing either.
            foreach (var curve in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.CurveVisualController>(true))
            {
                Straight(curve.noValidHitProperties);
                Straight(curve.uiHitProperties);
                Straight(curve.uiPressHitProperties);
                Straight(curve.selectHitProperties);
                Straight(curve.hoverHitProperties);
            }

            foreach (var line in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>(true))
                line.smoothMovement = false;
            _vignette = _rig.GetComponentInChildren<TunnelingVignetteController>(true);
            _arcWasOut = false;
            _move = _rig.GetComponentInChildren<ContinuousMoveProvider>(true);
            _walkSpeed = _move != null ? _move.moveSpeed : 0f;
            _running = false;
            return true;
        }

        /// <summary>
        /// Left stick click toggles walk / run (smooth moving only); stopping drops back to a walk, and a run
        /// toggled without moving lapses.
        /// </summary>
        void UpdateRun()
        {
            if (_move == null)
                return;
            var canRun = _move.isActiveAndEnabled && !Teleport;
            if (canRun && _runToggle.WasPressedThisFrame())
            {
                _running = !_running;
                _movedSinceRun = false;
                _stillFor = 0f;
                Core.Audio.SfxBus.Play2D(Core.Audio.SfxSynth.Pip, 0.22f, _running ? 1.25f : 0.85f);
            }

            if (_running)
            {
                var moving = _move.leftHandMoveInput.ReadValue().sqrMagnitude > 0.02f;
                if (moving)
                {
                    _movedSinceRun = true;
                    _stillFor = 0f;
                }
                else
                {
                    _stillFor += Time.unscaledDeltaTime;
                }

                if (!canRun || (_movedSinceRun ? _stillFor > RunStopDelay : _stillFor > RunIdleLapse))
                    _running = false;
            }

            var speed = _walkSpeed * (_running ? RunFactor : 1f);
            if (!Mathf.Approximately(_move.moveSpeed, speed))
                _move.moveSpeed = speed;
        }

        static void Straight(UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.LineProperties p)
        {
            if (p != null)
                p.smoothlyCurveLine = false;
        }

        void ApplyNow()
        {
            var smoothTurn = SmoothTurn;
            if (_left != null)
            {
                _left.smoothMotionEnabled = !Teleport;
                _left.smoothTurnEnabled = smoothTurn;
            }

            if (_right != null)
            {
                _right.smoothMotionEnabled = false;
                _right.smoothTurnEnabled = smoothTurn;
            }

            if (_vignette != null && _vignette.gameObject.activeSelf != Vignette)
                _vignette.gameObject.SetActive(Vignette);

            // Only the chosen turn provider runs: the starter kit arbitrates snap vs smooth through its input
            // actions, which several events re-enable (UI hover, near/far region, re-enable) — an odd snap
            // slipped through while turning smoothly. With the other provider off, it cannot.
            foreach (var snap in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.SnapTurnProvider>(true))
                snap.enabled = !smoothTurn;
            foreach (var smooth in _rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning.ContinuousTurnProvider>(true))
                smooth.enabled = smoothTurn;
        }

        void ApplyLift()
        {
            var offset = _rig.CameraFloorOffsetObject;
            if (offset == null)
                return;
            // In the captain's chair the chair sets the lift (eyes at seated height over the cushion).
            var chairLift = CaptainCommandMode.Instance != null ? CaptainCommandMode.Instance.ViewLift : null;
            var baseY = _rig.CurrentTrackingOriginMode == UnityEngine.XR.TrackingOriginModeFlags.Floor ? 0f : _rig.CameraYOffset;
            CurrentLift = chairLift ?? StandingLift;
            if (PcPlatformBoot.IsFlatScreen)
            {
                // No tracked head on a screen: standing eyes at WorldScale height, only the chair lifts them.
                baseY = Core.Vfx.WorldScale.EyeStanding;
                CurrentLift = chairLift ?? 0f;
            }
            var y = baseY + CurrentLift;
            var p = offset.transform.localPosition;
            if (Mathf.Abs(p.y - y) > 0.001f)
                offset.transform.localPosition = new Vector3(p.x, y, p.z);
        }

        /// <summary>Makes the room floors teleport targets (once each; rooms are built lazily).</summary>
        static void LayFloors(UnityEngine.XR.Interaction.Toolkit.InteractionLayerMask layers)
        {
            foreach (var c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c.isTrigger || !Floors.Contains(c.name) || c.GetComponent<TeleportationArea>() != null)
                    continue;
                var area = c.gameObject.AddComponent<TeleportationArea>();
                area.interactionLayers = layers;
            }
        }
    }
}
