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
            AuthManager.Ensure();
            await Trans.EnsureLoaded();
        }

        void Start()
        {
            var env = gameObject.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.BootVoid;
            env.Build();
            PlayAmbience();
            Invoke(nameof(GoMenu), HoldSeconds);
        }

        void GoMenu()
        {
            SceneFlow.Go(SceneFlow.Menu);
        }

        void PlayAmbience()
        {
            var bed = Resources.Load<AudioClip>("CIC/ambient");
            if (bed == null)
                return;
            var source = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.28f;
            source.clip = bed;
            source.Play();
        }
    }
}
