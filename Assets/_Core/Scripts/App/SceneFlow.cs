using UnityEngine.SceneManagement;

namespace Core.App
{
    public static class SceneFlow
    {
        public const string Boot = "Boot";
        public const string Menu = "Menu";
        public const string Bridge = "Bridge";

        public static void Go(string sceneName)
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        }
    }
}
