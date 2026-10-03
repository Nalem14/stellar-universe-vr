using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace Core.App
{
    public enum PlatformMode
    {
        VR,
        Desktop,
        Mobile
    }

    /// <summary>
    /// Automatic detection and bootstrapper for VR, PC Desktop, and Mobile (Android / iOS):
    /// - Detects if running on Quest (VR), PC Desktop (Keyboard/Mouse), or Mobile Phone/Tablet (Touch).
    /// - Configures appropriate player controller, HUD overlay, and EventSystem input modules.
    /// </summary>
    public static class PcPlatformBoot
    {
        public static PlatformMode Mode { get; private set; }
        public static bool IsInitialized { get; private set; }

        public static bool IsVr => Mode == PlatformMode.VR;
        public static bool IsDesktop => Mode == PlatformMode.Desktop;
        public static bool IsMobile => Mode == PlatformMode.Mobile;
        public static bool IsFlatScreen => IsDesktop || IsMobile;

        /// <summary>Backwards compatibility alias for Desktop mode</summary>
        public static bool IsPcDesktop => IsDesktop;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (IsInitialized)
                return;
            IsInitialized = true;

            var args = System.Environment.GetCommandLineArgs();
            bool forceMobile = System.Array.IndexOf(args, "-mobile") >= 0 || System.Array.IndexOf(args, "-touch") >= 0;
            bool forceDesktop = System.Array.IndexOf(args, "-desktop") >= 0 || System.Array.IndexOf(args, "-novr") >= 0;
            bool forceVr = System.Array.IndexOf(args, "-vr") >= 0;

            bool vrActive = false;
            #if !UNITY_SERVER
            var xrManager = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
            vrActive = xrManager != null && xrManager.activeLoader != null;
            #endif

            if (forceMobile)
            {
                Mode = PlatformMode.Mobile;
            }
            else if (forceDesktop)
            {
                Mode = PlatformMode.Desktop;
            }
            else if (forceVr)
            {
                Mode = PlatformMode.VR;
            }
            else if (vrActive)
            {
                Mode = PlatformMode.VR;
            }
            else if (IsQuestDevice())
            {
                // A Quest is never a phone, even if the XR loader comes up after this runs.
                Mode = PlatformMode.VR;
            }
            else if (Application.isMobilePlatform)
            {
                // Android smartphone/tablet or iOS device without active VR headset
                Mode = PlatformMode.Mobile;
            }
            else
            {
                Mode = PlatformMode.Desktop;
            }

            Debug.Log($"[PcPlatformBoot] Active Platform Mode: {Mode}");

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static bool IsQuestDevice()
        {
            if (Application.platform != RuntimePlatform.Android)
                return false;
            var model = (SystemInfo.deviceModel ?? string.Empty).ToLowerInvariant();
            return model.Contains("quest") || model.Contains("oculus") || model.Contains("meta");
        }

        /// <summary>
        /// A text field has the keyboard (search, login, alliance description…): movement, interaction and
        /// shortcut keys must not fire while the player types.
        /// </summary>
        public static bool IsTyping
        {
            get
            {
                var es = EventSystem.current;
                var go = es != null ? es.currentSelectedGameObject : null;
                if (go == null)
                    return false;
                var field = go.GetComponent<TMPro.TMP_InputField>();
                return field != null && field.isFocused;
            }
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (IsVr)
                return;

            EnsureDesktopEventSystem();
            SetupDesktopRig();
            ConfigureWorldCanvases();
        }

        /// <summary>
        /// Replaces or configures EventSystem to use InputSystemUIInputModule for mouse and touch support.
        /// </summary>
        public static void EnsureDesktopEventSystem()
        {
            if (IsVr)
                return;
            EnsureSingleEventSystem();
        }

        /// <summary>
        /// Exactly one EventSystem, kept across scenes, with this platform's input module (XR rays in the headset,
        /// mouse / touch on a flat screen). Two live EventSystems split the input and no field or button answers
        /// (the login form first): every caller goes through here, extras are destroyed.
        /// </summary>
        public static EventSystem EnsureSingleEventSystem()
        {
            var all = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            EventSystem keep = null;
            foreach (var e in all)
                if (keep == null || e.isActiveAndEnabled && !keep.isActiveAndEnabled)
                    keep = e;
            foreach (var e in all)
                if (e != keep)
                {
                    // Off at once (Destroy waits for the frame's end, and two live ones split the input meanwhile).
                    e.gameObject.SetActive(false);
                    Object.Destroy(e.gameObject);
                }

            if (keep == null)
                keep = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            if (keep.transform.parent == null)
                Object.DontDestroyOnLoad(keep.gameObject);
            keep.enabled = true;
            keep.gameObject.SetActive(true);

            var xr = keep.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
            var flat = keep.GetComponent<InputSystemUIInputModule>();
            if (IsVr)
            {
                if (xr == null)
                    xr = keep.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
                xr.enabled = true;
                if (flat != null)
                    flat.enabled = false;
            }
            else
            {
                if (xr != null)
                    xr.enabled = false;
                if (flat == null)
                    flat = keep.gameObject.AddComponent<InputSystemUIInputModule>();
                flat.enabled = true;
            }

            EventSystem.current = keep;
            if (keep.GetComponent<SingleEventSystemGuard>() == null)
                keep.gameObject.AddComponent<SingleEventSystemGuard>();
            return keep;
        }

        /// <summary>
        /// Sets up First Person Controller (PC or Mobile) and raycasters on the scene's player rig.
        /// </summary>
        public static void SetupDesktopRig()
        {
            if (IsVr)
                return;

            var rig = Object.FindFirstObjectByType<XROrigin>();
            if (rig == null)
                return;

            // The XR locomotion (move, turn, teleport, gravity, jump, climb, body transformer) reads controllers and
            // would also push the capsule (double gravity): the flat controllers own the body here.
            foreach (var provider in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true))
                provider.enabled = false;
            foreach (var mediator in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionMediator>(true))
                mediator.enabled = false;
            foreach (var transformer in rig.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>(true))
                transformer.enabled = false;

            // A walking body (the rig prefab's own capsule is the headset's: r 0.1 m, 0.5 m steps).
            var body = rig.GetComponentInChildren<CharacterController>();
            if (body == null)
                body = rig.gameObject.AddComponent<CharacterController>();
            body.height = 1.8f;
            body.radius = 0.35f;
            body.center = new Vector3(0f, 0.9f, 0f);
            body.stepOffset = 0.3f;
            body.skinWidth = 0.04f;

            // Disable TrackedPoseDriver on camera so it does not override camera rotation
            var cam = rig.Camera != null ? rig.Camera : Camera.main;
            if (cam != null)
            {
                foreach (var tpd in cam.GetComponents<UnityEngine.InputSystem.XR.TrackedPoseDriver>())
                    tpd.enabled = false;
                foreach (var tpd in cam.GetComponents<UnityEngine.SpatialTracking.TrackedPoseDriver>())
                    tpd.enabled = false;

                cam.nearClipPlane = 0.05f;

                if (IsDesktop)
                {
                    if (cam.GetComponent<UI.PcInteractionRaycaster>() == null)
                        cam.gameObject.AddComponent<UI.PcInteractionRaycaster>();

                    UI.PcHud.Ensure();
                }
                else if (IsMobile)
                {
                    UI.MobileHud.Ensure();
                }
            }

            // Hide/disable VR controller and hand models that have no tracking
            foreach (var t in rig.GetComponentsInChildren<Transform>(true))
            {
                var n = t.name;
                if (n.Contains("Controller") || n.Contains("Hand Visualizer") || n.Contains("XR Interaction Simulator"))
                {
                    if (t != rig.transform && t != (cam != null ? cam.transform : null))
                        t.gameObject.SetActive(false);
                }
            }

            if (IsDesktop)
            {
                if (rig.GetComponent<PcDesktopController>() == null)
                    rig.gameObject.AddComponent<PcDesktopController>();
            }
            else if (IsMobile)
            {
                if (rig.GetComponent<UI.MobileTouchController>() == null)
                    rig.gameObject.AddComponent<UI.MobileTouchController>();
            }
        }

        /// <summary>
        /// Ensures all WorldSpace canvases have their event camera set to Camera.main for pointer/touch raycasts.
        /// </summary>
        public static void ConfigureWorldCanvases()
        {
            if (IsVr)
                return;

            var cam = Camera.main;
            if (cam == null)
            {
                var rig = Object.FindFirstObjectByType<XROrigin>();
                if (rig != null)
                    cam = rig.Camera;
            }
            if (cam == null)
                return;

            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                // Disable TrackedDeviceGraphicRaycaster before changing camera to prevent XRI KeyNotFoundException
                var trackedRaycasters = canvas.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(true);
                foreach (var tr in trackedRaycasters)
                {
                    tr.enabled = false;
                }

                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                {
                    canvas.worldCamera = cam;
                }

                if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                {
                    canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                }
            }
        }
    }

    /// <summary>
    /// Rigs and prefabs can bring their own EventSystem after a scene has loaded (the boot screen's does): twice a
    /// second, the kept one removes any newcomer so input is never split between two.
    /// </summary>
    public sealed class SingleEventSystemGuard : MonoBehaviour
    {
        float _next;

        void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 0.5f;
            if (Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 1)
                PcPlatformBoot.EnsureSingleEventSystem();
        }
    }
}
