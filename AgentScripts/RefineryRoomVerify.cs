using System.IO;
using System.Reflection;
using Core.App;
using Core.Stations;
using Core.Vfx;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ephemeral verifier for the fuel refinery (model/fuel.php): builds the bay in a scratch scene, dresses it from
/// injected GetRefinery reads (dormant / built mid-game with a stage in work / maxed) and renders into Screenshots/.
/// Entry: unity command run_script --file AgentScripts/RefineryRoomVerify.cs --entry RefineryRoomVerify.Run
/// (or copied under an Editor folder and run with -executeMethod RefineryRoomVerify.RunBatch).
/// </summary>
public static class RefineryRoomVerify
{
    public static void RunBatch() => Debug.Log("RefineryRoomVerify: " + Run());

    static string Json(bool built, int culture, int nano, int cat, int cryo, int pump, float fuel, string working)
    {
        string Stage(string key, int level, int max) =>
            "\"" + key + "\":{\"level\":" + level + ",\"max\":" + max + ",\"next\":" + (level >= max ? "null"
                : "{\"level\":" + (level + 1) + ",\"max\":" + max + ",\"cost\":{\"mineral\":12000,\"crystal\":6000,\"biomass\":3000},\"time\":2400,\"requiert\":" +
                  (key == "nanobots" ? "{\"nanite\":5}" : "[]") + "}") + "}";
        var now = FleetOrderGate.UnixNow();
        var cap = 1000 + cryo * 2000;
        return "{\"planet\":208,\"built\":" + (built ? "true" : "false") + ",\"unlocked\":true,\"stages\":{" +
               Stage("culture", culture, 10) + "," + Stage("nanobots", nano, 10) + "," + Stage("catalysis", cat, 10) + "," +
               Stage("cryo", cryo, 10) + "," + Stage("pump", pump, 5) + "},\"fuel\":" + fuel + ",\"capacity\":" + cap + "," +
               "\"working\":" + (working == null ? "null" : "{\"stage\":\"" + working + "\",\"until\":" + (now + 1500) + ",\"start\":" + (now - 900) + "}") +
               ",\"perHour\":{\"fuel\":" + culture * 24 + ",\"crystal\":" + culture * 48 + ",\"biomass\":" + culture * 24 + "},\"yield\":0.5," +
               "\"quality\":{\"speedFactor\":" + (1.25f + cat * 0.025f).ToString(System.Globalization.CultureInfo.InvariantCulture) +
               ",\"costFactor\":" + (1f - cat * 0.03f).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"catalysis\":" + cat + "},\"pumpPerMinute\":" + pump * 40 + "}";
    }

    public static string Run()
    {
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var previousPath = previous.path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var types = new[]
        {
            typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade),
            typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(ComfortSettings)
        };
        var root = new GameObject("RefineryVerifyRoot");
        RefineryRoom room = null;
        try
        {
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(root.transform, false);
            foreach (var t in types)
                Preseed(root, t);
            var interior = new GameObject("BridgeInterior");
            interior.transform.SetParent(root.transform, false);
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();
            room = RefineryRoom.Build(env.Art, null);
            room.transform.SetParent(root.transform, true);

            var camGo = new GameObject("RefineryShotCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.005f, 0.008f, 0.02f);
            camGo.tag = "MainCamera";
            var rt = room.transform;
            void Shot(Vector3 eye, Vector3 at, string file, float fov = 75f)
            {
                cam.fieldOfView = fov;
                camGo.transform.position = rt.TransformPoint(eye);
                camGo.transform.LookAt(rt.TransformPoint(at));
                Capture(cam, Path.Combine(Path.GetFullPath("Screenshots"), file));
            }

            Directory.CreateDirectory(Path.GetFullPath("Screenshots"));
            room.EditorShow(RefineryState.Parse(Json(false, 0, 0, 0, 0, 0, 0f, null)));
            Shot(new Vector3(0f, 1.62f, -1.6f), new Vector3(0f, 1.4f, 5f), "refinery-dormant.png", 82f);

            room.EditorShow(RefineryState.Parse(Json(true, 6, 4, 5, 4, 2, 4200f, "catalysis")));
            var swarm = room.GetComponentInChildren<ParticleSystem>();
            if (swarm != null)
                swarm.Simulate(9f, true, true);
            Shot(new Vector3(0f, 1.62f, -1.6f), new Vector3(0f, 1.6f, 5f), "refinery-mid.png", 82f);
            Shot(new Vector3(-4.6f, 2.6f, -1.2f), new Vector3(1.5f, 1.2f, 7f), "refinery-wide.png", 80f);
            Shot(new Vector3(0f, 1.62f, 0.3f), new Vector3(0f, 1.25f, 1.6f), "refinery-status.png", 62f);
            Shot(new Vector3(1.5f, 1.9f, 3.2f), new Vector3(0f, 0.5f, 5.4f), "refinery-pool.png", 70f);

            room.EditorShow(RefineryState.Parse(Json(true, 10, 10, 10, 10, 5, 19000f, null)));
            if (swarm != null)
                swarm.Simulate(9f, true, true);
            Shot(new Vector3(0f, 1.62f, -1.6f), new Vector3(0f, 1.8f, 6f), "refinery-max.png", 82f);
            Shot(new Vector3(2.5f, 1.7f, 7.4f), new Vector3(0f, 1.3f, 11f), "refinery-tanks.png", 78f);
            return "ok";
        }
        finally
        {
            RefineryRoom.EditorReset();
            Object.DestroyImmediate(root);
            if (room != null)
                Object.DestroyImmediate(room.gameObject);
            foreach (var t in types)
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == t)
                        f.SetValue(null, null);
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        }
    }

    static void Preseed(GameObject root, System.Type type)
    {
        var host = new GameObject(type.Name);
        host.transform.SetParent(root.transform, false);
        var c = host.AddComponent(type);
        foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (f.FieldType == type && f.GetValue(null) == null)
                f.SetValue(null, c);
        type.GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null)?.Invoke(c, null);
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
