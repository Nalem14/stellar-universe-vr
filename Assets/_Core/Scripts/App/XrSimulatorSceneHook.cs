using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Core.App
{
    /// <summary>
    /// After LoadScene the XR Interaction Simulator keeps destroyed controller
    /// refs and can drop FPS body mode (WASD/Z only moves one hand).
    /// Clear refs, re-enable, and restore FPS targeting on each scene load.
    /// Also strips XRI coaching callouts (AGENTS.md — no MR coaching in product shots).
    /// </summary>
    public sealed class XrSimulatorSceneHook : MonoBehaviour
    {
        static XrSimulatorSceneHook s_Instance;
        /// <summary>Late coaching spawns (lazy tooltips) are caught by a 1 Hz sweep, not a per-frame scene scan.</summary>
        static readonly WaitForSecondsRealtime SweepInterval = new(1f);
        const int LateSweeps = 12;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_Instance != null)
                return;

            var go = new GameObject(nameof(XrSimulatorSceneHook));
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<XrSimulatorSceneHook>();
            SceneManager.sceneLoaded += s_Instance.OnSceneLoaded;
            // AfterSceneLoad already happened for the entry scene — run once now.
            s_Instance.StartCoroutine(s_Instance.RebindAfterLoad());
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying)
                return;

            StopAllCoroutines();
            StartCoroutine(RebindAfterLoad());
        }

        IEnumerator RebindAfterLoad()
        {
            // Wait one frame so the new XR Origin / modality manager exist.
            yield return null;

            var sim = Object.FindFirstObjectByType<XRInteractionSimulator>();
            if (sim != null)
            {
                // Drop destroyed scene refs so OnEnable rebinds to the new origin.
                sim.leftControllerTransform = null;
                sim.rightControllerTransform = null;
                sim.leftHandAimTransform = null;
                sim.rightHandAimTransform = null;

                sim.enabled = false;
                yield return null;
                sim.enabled = true;

                // WASD/Z must move HMD + both controllers (same as Boot/Menu).
                sim.targetedDeviceInput = TargetedDevices.FPS | TargetedDevices.RightDevice;
            }

            for (var i = 0; i < 4; i++)
            {
                SuppressCoachingUi();
                yield return null;
            }

            for (var i = 0; i < LateSweeps; i++)
            {
                yield return SweepInterval;
                SuppressCoachingUi();
            }
        }

        static void SuppressCoachingUi()
        {
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                var tn = mb.GetType().Name;
                // XRI sample coaching: Callout, CalloutGazeController, InputFeedback UI, etc.
                if (tn == "Callout" ||
                    tn.IndexOf("CalloutGaze", System.StringComparison.Ordinal) >= 0 ||
                    tn.IndexOf("InputFeedback", System.StringComparison.Ordinal) >= 0 ||
                    tn.IndexOf("AffordanceCallout", System.StringComparison.Ordinal) >= 0 ||
                    tn.IndexOf("SimulatorUI", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mb.enabled = false;
                    mb.gameObject.SetActive(false);
                }
            }

            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
            {
                var n = t.name;
                if (n.IndexOf("Simulator UI", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Affordance Callout", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Affordance Callouts", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Joystick Affordances", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Lazy Tooltip", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("InputFeedback", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    t.gameObject.SetActive(false);
                }
            }
        }
    }
}
