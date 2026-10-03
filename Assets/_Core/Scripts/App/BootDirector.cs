using Core.Utils;
using Core.Vfx;
using UnityEngine;

namespace Core.App
{
    public class BootDirector : MonoBehaviour
    {
        public float HoldSeconds = 3.2f;

        async void Awake()
        {
            PcPlatformBoot.Initialize();
            AuthManager.Ensure();
            await Trans.EnsureLoaded();
        }

        void Start()
        {
            var env = gameObject.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.BootVoid;
            env.Build();
            Invoke(nameof(GoMenu), HoldSeconds);
            Core.Audio.AmbienceDirector.Ensure();
            // The CIC powering up under the logo.
            Core.Audio.SfxBus.Play2D(Core.Audio.SfxSynth.PowerUp, 0.5f, priority: Core.Audio.SfxBus.Priority.Alert);
        }

        void GoMenu()
        {
            SceneFlow.Go(SceneFlow.Menu);
        }
    }
}
