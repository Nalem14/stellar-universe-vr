using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Core.App;
using Core.Entity;
using Core.Vfx;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ephemeral verifier for the planet view (city + citadel) and the orbital fortress view: builds the bridge in
/// the open scene, switches the view to a city of each kind of world (day and night) and to a fortress, renders
/// a few shots into Screenshots/ (repo root, git-ignored), reports draw-call-ish counts, then cleans up.
/// Entry: unity command run_script --file AgentScripts/CityViewVerify.cs --entry CityViewVerify.Run
/// </summary>
public static class CityViewVerify
{
    const int Me = 42;

    public static string Run()
    {
        // Work in a throwaway empty scene: the bridge rig re-parents any XR Origin it finds, and the open
        // authoring scene must never be touched. Reopened untouched afterwards.
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (previous.isDirty)
            return "aborted: the open scene has unsaved changes";
        var previousPath = previous.path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        try
        {
            return RunInScratch();
        }
        finally
        {
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        }
    }

    static string RunInScratch()
    {
        var old = GameObject.Find("CityVerifyRoot");
        if (old != null)
            Object.DestroyImmediate(old);
        var outDir = Path.GetFullPath("Screenshots");
        Directory.CreateDirectory(outDir);
        var root = new GameObject("CityVerifyRoot");
        var report = new System.Text.StringBuilder();
        try
        {
            var auth = AuthManager.Ensure();
            typeof(AuthManager).GetProperty("User")!.SetValue(auth, new UserSession { id = Me, token = "verify", username = "verify" });

            // The diegetic UI wants an EventSystem; in a scratch edit-mode scene it would make a DontDestroyOnLoad one.
            var events = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            events.transform.SetParent(root.transform, false);
            // Play-mode singletons the bridge creates with DontDestroyOnLoad: pre-seed them on the scratch root.
            foreach (var t in new[]
                     {
                         typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade),
                         typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(ComfortSettings)
                     })
                Preseed(root, t);
            var eco = root.AddComponent<EconomyService>();
            typeof(EconomyService).GetProperty("Instance")!.SetValue(null, eco);

            var world = new GameObject("SystemWorld");
            world.transform.SetParent(root.transform, false);
            var exteriorGo = new GameObject("Exterior");
            exteriorGo.transform.SetParent(world.transform, false);
            var exterior = exteriorGo.AddComponent<SystemExterior>();
            var viewRig = world.AddComponent<BridgeViewRig>();
            var interior = new GameObject("BridgeInterior");
            interior.transform.SetParent(root.transform, false);
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();

            var camGo = new GameObject("CityShotCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = WorldScale.CityFarClip;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
            cam.fieldOfView = 78f;
            cam.allowHDR = true;
            camGo.tag = "MainCamera";

            // One world of each kind (habitability decides Terra / Desert / Ice / Rock; a gas giant by slot hash).
            var gasId = 300;
            while (SystemBodyKit.ClassifyPlanet(5, gasId, 0) != SystemBodyKit.PlanetKind.Gas && gasId < 2000)
                gasId++;
            var worlds = new (string name, int id, int slot, int hab)[]
            {
                ("terra", 102, 3, 8), ("desert", 103, 2, 3), ("ice", 104, 6, 2), ("rock", 105, 1, 1), ("gas", gasId, 5, 0)
            };

            var focus = new FocusContext();
            focus.SetFromApi(9001, SystemsJson(worlds), FleetsJson());
            exterior.Bind(focus);
            viewRig.Bind(focus, exterior, interior.transform);

            foreach (var w in worlds)
            {
                Seed(eco, w.id, rich: w.name != "rock");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                focus.SetViewPlanet(w.id);
                BridgeDressing.Apply(env, focus);
                sw.Stop();
                var city = CityExterior.Current;
                if (city == null)
                {
                    report.Append(w.name).Append(": no city; ");
                    continue;
                }

                var mount = viewRig.BridgeMount;
                // From the 45° bay, eye height, looking out and down over the city.
                Place(camGo.transform, mount, 45f, 7.4f, 1.65f, -16f);
                foreach (var hour in new[] { 0.12f, 0.62f })
                {
                    CityExterior.PinnedHour = hour;
                    city.Relight();
                    Capture(cam, Path.Combine(outDir, $"city-{w.name}-{(hour < 0.5f ? "day" : "night")}.png"));
                }

                if (w.name == "terra")
                {
                    // The whole citadel from outside, mid-air, at dusk.
                    CityExterior.PinnedHour = 0.47f;
                    city.Relight();
                    var tower = mount.TransformPoint(new Vector3(0f, -120f, WorldScale.CicTableCenterZ));
                    camGo.transform.position = tower + new Vector3(330f, 40f, -330f);
                    camGo.transform.LookAt(tower + Vector3.up * 40f);
                    FollowSky(city, camGo.transform);
                    Capture(cam, Path.Combine(outDir, "city-terra-aerial.png"));
                }

                report.Append(w.name).Append(" (").Append(sw.ElapsedMilliseconds).Append(" ms): rings=").Append(city.RingCount).Append(" renderers=")
                    .Append(city.GetComponentsInChildren<Renderer>(false).Length).Append(" tris=")
                    .Append(Triangles(city.transform)).Append("; ");
            }

            // The orbital fortress: its bridge (the rotunda) over its world, modules on the ring.
            CityExterior.PinnedHour = null;
            focus.SetViewFleet(601);
            BridgeDressing.Apply(env, focus);
            Place(camGo.transform, viewRig.BridgeMount, 45f, 7.4f, 1.65f, -10f);
            Capture(cam, Path.Combine(outDir, "fortress-bay.png"));
            report.Append("fortress mode=").Append(focus.Mode);
        }
        finally
        {
            CityExterior.PinnedHour = null;
            Object.DestroyImmediate(root);
            foreach (var a in Object.FindObjectsByType<AuthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (a != null)
                    Object.DestroyImmediate(a.gameObject);
            typeof(AuthManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            typeof(EconomyService).GetProperty("Instance")!.SetValue(null, null);
            foreach (var t in new[]
                     {
                         typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade),
                         typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(ComfortSettings)
                     })
                Unseed(t);
        }

        Debug.Log("[CityViewVerify] " + report);
        return report.ToString();
    }

    /// <summary>In edit mode the sky does not follow the eye (LateUpdate): put it on the camera by hand.</summary>
    static void FollowSky(CityExterior city, Transform cam)
    {
        var sky = city.transform.Find("City/Sky");
        if (sky != null)
            sky.position = cam.position;
    }

    static void Preseed(GameObject root, System.Type type)
    {
        var host = new GameObject(type.Name);
        host.transform.SetParent(root.transform, false);
        var c = host.AddComponent(type);
        foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (f.FieldType == type && f.GetValue(null) == null)
                f.SetValue(null, c);
        foreach (var pr in type.GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (pr.PropertyType == type && pr.CanWrite && pr.GetValue(null) == null)
                pr.SetValue(null, c);
        var backing = type.GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
        if (backing != null && backing.GetValue(null) == null)
            backing.SetValue(null, c);
    }

    static void Unseed(System.Type type)
    {
        foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (f.FieldType == type)
                f.SetValue(null, null);
    }

    static void Place(Transform cam, Transform mount, float bearing, float radius, float eye, float pitch)
    {
        var axis = new Vector3(0f, 0f, WorldScale.CicTableCenterZ);
        var d = LatheMesh.Dir(bearing);
        cam.position = mount.TransformPoint(axis + d * radius + Vector3.up * eye);
        cam.rotation = mount.rotation * Quaternion.LookRotation(d, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
    }

    static void Seed(EconomyService eco, int planetId, bool rich)
    {
        var raw = new JObject
        {
            ["id"] = planetId, ["home"] = rich ? 6 : 1, ["farm"] = rich ? 5 : 1, ["mineralMine"] = 8, ["crystalMine"] = rich ? 6 : 0,
            ["mineralWarehouse"] = 3, ["crystalWarehouse"] = 2, ["solarPlant"] = 4, ["nuclearPlant"] = rich ? 2 : 0,
            ["researchLab"] = rich ? 5 : 0, ["orbitShipyard"] = 4, ["academy"] = 2, ["defenseFactory"] = rich ? 4 : 0,
            ["stargate"] = rich ? 1 : 0, ["jumpgate"] = 0, ["shield"] = rich ? 3 : 0
        };
        var p = new PlanetEconomy { Id = planetId, SystemId = 9001, Name = "Verify", Raw = raw, Defense = rich ? 12 : 0 };
        var dict = (Dictionary<int, PlanetEconomy>)typeof(EconomyService)
            .GetField("_planets", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(eco);
        dict[planetId] = p;
    }

    static int Triangles(Transform t)
    {
        var n = 0;
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(false))
            if (mf.sharedMesh != null)
                n += mf.sharedMesh.isReadable ? mf.sharedMesh.triangles.Length / 3 : (int)mf.sharedMesh.GetIndexCount(0) / 3;
        return n;
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

    static string SystemsJson((string name, int id, int slot, int hab)[] worlds)
    {
        var planets = new JArray();
        foreach (var w in worlds)
            planets.Add(new JObject { ["id"] = w.id, ["name"] = "Verify " + w.name, ["slot"] = w.slot, ["userid"] = Me, ["_habitability"] = w.hab });
        return new JArray(new JObject { ["id"] = 9001, ["name"] = "Verify", ["type"] = "orange", ["planets"] = planets }).ToString();
    }

    static string FleetsJson() =>
        @"[
{""id"":601,""name"":""Station Orbitale Verify"",""userid"":42,""systemid"":9001,""planetid"":102,""isStation"":1,""desttime"":0,""ships"":[
{""id"":1,""type"":""StationCore"",""grid_x"":4,""grid_y"":4},
{""id"":2,""type"":""OrbitalDefenseBattery"",""grid_x"":4,""grid_y"":3},
{""id"":3,""type"":""PlanetaryShieldProjector"",""grid_x"":5,""grid_y"":4},
{""id"":4,""type"":""OrbitalJammingArray"",""grid_x"":3,""grid_y"":4},
{""id"":5,""type"":""OrbitalGantry"",""grid_x"":4,""grid_y"":5},
{""id"":6,""type"":""CitadelReactor"",""grid_x"":5,""grid_y"":5},
{""id"":7,""type"":""OrbitalDefenseBattery"",""grid_x"":3,""grid_y"":3}
]},
{""id"":501,""name"":""Own"",""userid"":42,""systemid"":9001,""planetid"":103,""desttime"":0,""ships"":[
{""id"":11,""type"":""ShipCore"",""grid_x"":4,""grid_y"":4},
{""id"":12,""type"":""LaserCannon"",""grid_x"":4,""grid_y"":3},
{""id"":13,""type"":""HyperspaceDrive"",""grid_x"":4,""grid_y"":5}
]}
]";
}
