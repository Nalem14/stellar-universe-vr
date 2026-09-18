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
    /// </summary>
    public sealed class XrSimulatorSceneHook : MonoBehaviour
    {
        static XrSimulatorSceneHook s_Instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_Instance != null)
                return;

            var go = new GameObject(nameof(XrSimulatorSceneHook));
            DontDestroyOnLoad(go);
            s_Instance = go.AddComponent<XrSimulatorSceneHook>();
            SceneManager.sceneLoaded += s_Instance.OnSceneLoaded;
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
            if (sim == null)
                yield break;

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
    }
}
