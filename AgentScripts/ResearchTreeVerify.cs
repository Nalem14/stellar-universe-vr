using System.IO;
using System.Reflection;
using Core.App;
using Core.Stations;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ephemeral verifier for the lab's research constellation: real GetConfigs / GetMeEmpire bodies (saved beside the
/// scratchpad, passed by env SU_VERIFY_DIR), the lab built in a scratch scene, the tree painted and rendered from
/// the stand into Screenshots/research-tree*.png. Run with -executeMethod ResearchTreeVerify.RunBatch.
/// </summary>
public static class ResearchTreeVerify
{
    public static void RunBatch() => Debug.Log("ResearchTreeVerify: " + Run());

    public static string Run()
    {
        var dir = System.Environment.GetEnvironmentVariable("SU_VERIFY_DIR");
        var previousPath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("TreeVerifyRoot");
        ResearchLab lab = null;
        try
        {
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(root.transform, false);
            foreach (var ty in new[] { typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade),
                         typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(ComfortSettings) })
            {
                var host = new GameObject(ty.Name);
                host.transform.SetParent(root.transform, false);
                var c = host.AddComponent(ty);
                foreach (var f in ty.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == ty && f.GetValue(null) == null)
                        f.SetValue(null, c);
                ty.GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null)?.Invoke(c, null);
            }

            GameConfig.Ingest(File.ReadAllText(Path.Combine(dir, "configs.json")));
            var empire = JObject.Parse(File.ReadAllText(Path.Combine(dir, "empire.json")));
            var auth = AuthManager.Ensure();
            typeof(AuthManager).GetProperty("Empire")!.SetValue(auth, empire);
            var interior = new GameObject("BridgeInterior");
            interior.transform.SetParent(root.transform, false);
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();
            var eco = EconomyService.Ensure(root.transform);
            eco.AdoptEmpire(empire, false);
            lab = ResearchLab.Build(env.Art, eco, new FocusContext());
            lab.gameObject.SetActive(true);
            var t = typeof(ResearchLab);
            t.GetField("_empire", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(lab, empire);
            t.GetMethod("PaintTree", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(lab, null);

            var camGo = new GameObject("TreeCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 60f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.012f, 0.03f);
            var lt = lab.transform;
            void Shot(Vector3 eye, Vector3 at, string file, float fov)
            {
                cam.fieldOfView = fov;
                camGo.transform.position = lt.TransformPoint(eye);
                camGo.transform.LookAt(lt.TransformPoint(at));
                Capture(cam, Path.Combine(Path.GetFullPath("Screenshots"), file));
            }

            Directory.CreateDirectory(Path.GetFullPath("Screenshots"));
            Shot(new Vector3(0f, 1.65f, -1.2f), new Vector3(0f, 2.1f, 3.2f), "research-tree.png", 95f);
            Shot(new Vector3(0f, 1.65f, 0f), new Vector3(1.4f, 2.6f, 2.9f), "research-tree-right.png", 60f);
            Shot(new Vector3(0f, 1.65f, 0f), new Vector3(-1.2f, 1.8f, 2.9f), "research-tree-left.png", 60f);
            return "ok nodes=" + ResearchCatalog.All().Count;
        }
        finally
        {
            Object.DestroyImmediate(root);
            if (lab != null)
                Object.DestroyImmediate(lab.gameObject);
            foreach (var a in Object.FindObjectsByType<AuthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (a != null)
                    Object.DestroyImmediate(a.gameObject);
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        }
    }

    static void Capture(Camera cam, string path)
    {
        const int w = 1600, h = 900;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
    }
}
