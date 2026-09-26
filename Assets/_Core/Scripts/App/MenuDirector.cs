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
            FallGuard.Ensure();
            PlayAmbience();
        }

        void PlayAmbience()
        {
            var bed = Resources.Load<AudioClip>("CIC/ambient");
            if (bed == null)
                return;
            if (!TryGetComponent<AudioSource>(out var source))
                source = gameObject.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.22f;
            source.clip = bed;
            source.Play();
        }
    }
}
