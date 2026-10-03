using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

namespace Core.App
{
    /// <summary>
    /// Automatic detection and bootstrapper for PC Desktop mode versus VR mode.
    /// If no XR headset is active, or if launched with -desktop / -novr, initializes
    /// first-person desktop controls (keyboard/mouse), PC HUD, and desktop UI event handling.
    /// </summary>
    public static class PcPlatformBoot
    {
        public static bool IsPcDesktop { get; private set; }
        public static bool IsInitialized { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (IsInitialized)
                return;
            IsInitialized = true;

            var args = System.Environment.GetCommandLineArgs();
            bool forceDesktop = System.Array.IndexOf(args, "-desktop") >= 0 || System.Array.IndexOf(args, "-novr") >= 0;
            bool forceVr = System.Array.IndexOf(args, "-vr") >= 0;

            bool vrActive = false;
            #if !UNITY_SERVER
            var xrManager = UnityEngine.XR.Management.XRGeneralSettings.Instance?.Manager;
            vrActive = xrManager != null && xrManager.activeLoader != null;
            #endif

            // On Android (Quest), always VR. On PC / Editor, Desktop unless VR loader is running or forced.
            IsPcDesktop = forceDesktop || (!forceVr && !vrActive && Application.platform != RuntimePlatform.Android);

            Debug.Log($"[PcPlatformBoot] Mode: {(IsPcDesktop ? "PC Desktop (Keyboard/Mouse)" : "VR (OpenXR)")}");

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!IsPcDesktop)
                return;

            EnsureDesktopEventSystem();
            SetupDesktopRig();
            ConfigureWorldCanvases();
        }

        /// <summary>
        /// Replaces or configures EventSystem to use InputSystemUIInputModule for mouse/pointer support on PC.
        /// </summary>
        public static void EnsureDesktopEventSystem()
        {
            if (!IsPcDesktop)
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
        /// Sets up First Person Controller and raycasters on the scene's player rig.
        /// </summary>
        public static void SetupDesktopRig()
        {
            if (!IsPcDesktop)
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

            // Disable TrackedPoseDriver on camera so it does not override mouse look
            var cam = rig.Camera != null ? rig.Camera : Camera.main;
            if (cam != null)
            {
                foreach (var tpd in cam.GetComponents<UnityEngine.InputSystem.XR.TrackedPoseDriver>())
                    tpd.enabled = false;
                foreach (var tpd in cam.GetComponents<UnityEngine.SpatialTracking.TrackedPoseDriver>())
                    tpd.enabled = false;

                // Adjust camera near clip plane for close cockpit interactions
                cam.nearClipPlane = 0.05f;

                // Attach PC Interaction Raycaster
                if (cam.GetComponent<UI.PcInteractionRaycaster>() == null)
                    cam.gameObject.AddComponent<UI.PcInteractionRaycaster>();

                // Attach PC HUD
                UI.PcHud.Ensure();
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

            // Attach PC Desktop Controller
            if (rig.GetComponent<PcDesktopController>() == null)
                rig.gameObject.AddComponent<PcDesktopController>();
        }

        /// <summary>
        /// Ensures all WorldSpace canvases have their event camera set to Camera.main for mouse raycasts.
        /// </summary>
        public static void ConfigureWorldCanvases()
        {
            if (!IsPcDesktop)
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
                if (canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
                {
                    canvas.worldCamera = cam;
                }
            }
        }
    }
}
