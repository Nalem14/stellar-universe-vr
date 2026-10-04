using UnityEngine;
using UnityEngine.XR.Management;

namespace Core.App
{
    /// <summary>
    /// Starts OpenXR only where there is a headset to drive. One APK serves the Quest and Android phones, one
    /// player PC screens and PCVR: XR Management's own "initialize on startup" is off for every target, because
    /// on a phone the OpenXR loader came up with no runtime and the game played its music over a black screen.
    /// Here, at the points XR Management would use (loader after the assemblies, subsystems before the first
    /// scene): a Quest (<see cref="PcPlatformBoot.IsQuestDevice"/>) or a player launched with <c>-vr</c> (PCVR).
    /// Everything else stays flat (<see cref="PcPlatformBoot"/>: Mobile / Desktop). The Editor's Play Mode is
    /// the PC path and is left alone.
    /// </summary>
    public static class XrStartup
    {
        public static bool Wanted
        {
            get
            {
#if UNITY_EDITOR
                return false;
#else
                var args = System.Environment.GetCommandLineArgs();
                if (System.Array.IndexOf(args, "-desktop") >= 0 || System.Array.IndexOf(args, "-novr") >= 0 ||
                    System.Array.IndexOf(args, "-mobile") >= 0 || System.Array.IndexOf(args, "-touch") >= 0)
                    return false;
                return System.Array.IndexOf(args, "-vr") >= 0 || PcPlatformBoot.IsQuestDevice();
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void InitializeLoader()
        {
            if (!Wanted)
                return;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || manager.activeLoader != null)
                return;
            manager.InitializeLoaderSync();
            if (manager.activeLoader == null)
                Debug.LogWarning("[SU] XrStartup: no XR runtime answered; staying on the flat screen.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void StartSubsystems()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || manager.activeLoader == null || manager.isInitializationComplete == false)
                return;
            manager.StartSubsystems();
            // Started by hand, stopped by hand: XR Management only tears down what it brought up itself.
            Application.quitting += () =>
            {
                if (manager.activeLoader == null)
                    return;
                manager.StopSubsystems();
                manager.DeinitializeLoader();
            };
        }
    }
}
