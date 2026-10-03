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

            var es = Object.FindFirstObjectByType<EventSystem>();
            if (es == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                Object.DontDestroyOnLoad(go);
            }
            else
            {
                var xrModule = es.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
                if (xrModule != null)
                    xrModule.enabled = false;

                var inputModule = es.GetComponent<InputSystemUIInputModule>();
                if (inputModule == null)
                    inputModule = es.gameObject.AddComponent<InputSystemUIInputModule>();
                inputModule.enabled = true;
            }
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

            // Ensure character controller exists for deck collisions
            var body = rig.GetComponentInChildren<CharacterController>();
            if (body == null)
            {
                body = rig.gameObject.AddComponent<CharacterController>();
                body.height = 1.8f;
                body.radius = 0.35f;
                body.center = new Vector3(0f, 0.9f, 0f);
                body.stepOffset = 0.3f;
                body.skinWidth = 0.04f;
            }

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
}
