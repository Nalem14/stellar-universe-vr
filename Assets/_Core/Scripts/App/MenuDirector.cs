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
            console.Build();
        }
    }
}
