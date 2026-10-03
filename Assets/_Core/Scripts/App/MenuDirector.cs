using Core.UI;
using Core.Vfx;
using UnityEngine;

namespace Core.App
{
    public class MenuDirector : MonoBehaviour
    {
        void Awake()
        {
            AuthManager.Ensure();
        }

        void Start()
        {
            var env = gameObject.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.MenuDeck;
            env.Build();
            var console = gameObject.AddComponent<MainMenuConsole>();
            console.Bind(env);
            console.Build();
            // Head on the sas spot the scene's rig marks, whatever the headset's offset in the real play space
            // (a late tracking start is absorbed by the fall guard).
            var rig = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (rig != null)
                XrPlacement.PlaceHead(rig, rig.transform.position, rig.transform.forward);
            FallGuard.Ensure();
            PcPlatformBoot.SetupDesktopRig();
            PcPlatformBoot.ConfigureWorldCanvases();
            Core.Audio.AmbienceDirector.Ensure();
        }
    }
}
