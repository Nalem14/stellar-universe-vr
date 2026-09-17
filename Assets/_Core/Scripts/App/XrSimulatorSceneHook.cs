using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Core.App
{
    /// <summary>
    /// XR Interaction Simulator keeps stale controller refs after LoadScene.
    /// Re-enable it so OnEnable rebinds to the new XR Origin.
    /// </summary>
    public static class XrSimulatorSceneHook
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Application.isPlaying)
                return;

            var sim = Object.FindFirstObjectByType<XRInteractionSimulator>();
            if (sim == null)
                return;

            sim.enabled = false;
            sim.enabled = true;
        }
    }
}
