using System.IO;
using System.Reflection;
using Core.App;
using Core.Vfx;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Ephemeral: renders the sas with the community plaque (scratch scene) into Screenshots/sas-community.png.</summary>
public static class SasPlaqueVerify
{
    public static string Run()
    {
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (previous.isDirty)
            return "aborted: the open scene has unsaved changes";
        var path = previous.path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var types = new[] { typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade), typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(ComfortSettings) };
        var root = new GameObject("SasVerifyRoot");
        try
        {
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(root.transform, false);
            foreach (var t in types)
            {
                var host = new GameObject(t.Name);
                host.transform.SetParent(root.transform, false);
                var c = host.AddComponent(t);
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == t && f.GetValue(null) == null)
                        f.SetValue(null, c);
                t.GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null)?.Invoke(c, null);
            }

            var roomGo = new GameObject("Sas");
            roomGo.transform.SetParent(root.transform, false);
            var env = roomGo.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.MenuDeck;
            env.Build();
            Core.UI.CommunityPlaque.Build(env.transform, env.Art);
            var cam = new GameObject("Cam").AddComponent<Camera>();
            cam.transform.SetParent(root.transform, false);
            cam.nearClipPlane = 0.02f;
            cam.fieldOfView = 70f;
            cam.transform.position = env.transform.TransformPoint(new Vector3(0.2f, WorldScale.EyeStanding, -0.6f));
            cam.transform.LookAt(env.transform.TransformPoint(new Vector3(1.3f, 1.42f, 0.72f)));
            const int w = 1600, h = 900;
            var rt = new RenderTexture(w, h, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.GetFullPath("Screenshots/sas-community.png"), tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            return "ok";
        }
        finally
        {
            Object.DestroyImmediate(root);
            foreach (var t in types)
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == t)
                        f.SetValue(null, null);
            if (!string.IsNullOrEmpty(path))
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
    }
}
