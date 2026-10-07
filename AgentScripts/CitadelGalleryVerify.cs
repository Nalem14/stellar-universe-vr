using System.IO;
using System.Reflection;
using Core.Stations;
using Core.Vfx;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Ephemeral verifier: the citadel gallery alone, rendered from the stair and before the gate (Screenshots/gallery-*.png).</summary>
public static class CitadelGalleryVerify
{
    public static void RunBatch() => Debug.Log("CitadelGalleryVerify: " + Run());

    public static string Run()
    {
        var previousPath = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("GalleryVerifyRoot");
        try
        {
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(root.transform, false);
            foreach (var ty in new[] { typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade),
                         typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(Core.App.ComfortSettings) })
            {
                var host = new GameObject(ty.Name);
                host.transform.SetParent(root.transform, false);
                var c = host.AddComponent(ty);
                foreach (var f in ty.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == ty && f.GetValue(null) == null)
                        f.SetValue(null, c);
                ty.GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null)?.Invoke(c, null);
            }

            var interior = new GameObject("BridgeInterior");
            interior.transform.SetParent(root.transform, false);
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();
            var hall = new GameObject("Hall").transform;
            hall.SetParent(root.transform, false);
            hall.position = new Vector3(500f, -500f, 500f);
            var gallery = CitadelGallery.Build(hall, env.Art);
            gallery.gameObject.SetActive(true);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, 30f, 0f);

            var camGo = new GameObject("Cam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.7f, 0.9f);
            Directory.CreateDirectory(Path.GetFullPath("Screenshots"));
            void Shot(Vector3 eye, Vector3 at, string file)
            {
                camGo.transform.position = hall.TransformPoint(eye);
                camGo.transform.LookAt(hall.TransformPoint(at));
                Capture(cam, Path.Combine(Path.GetFullPath("Screenshots"), file));
            }

            var (standPos, facing) = CitadelGallery.Stand;
            Shot(standPos + Vector3.up * 1.65f, standPos + Vector3.up * 1.4f + facing * 4f + Vector3.Cross(Vector3.up, facing) * -0f, "gallery-arrival.png");
            var (gatePos, _) = CitadelGallery.DoorPose(CorridorRoom.Slot.GateEnd);
            var eye = LatheMesh.Dir(CitadelGallery.Angle(CitadelGallery.Length - 5f)) * CitadelGallery.RMid + Vector3.up * 1.65f;
            Shot(eye, gatePos + Vector3.up * 1.3f, "gallery-gate.png");
            return "ok";
        }
        finally
        {
            Object.DestroyImmediate(root);
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
