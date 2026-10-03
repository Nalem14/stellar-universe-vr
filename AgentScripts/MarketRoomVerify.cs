using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Core.App;
using Core.Entity;
using Core.Stations;
using Core.Vfx;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ephemeral verifier for the galactic exchange (web marketplace): builds the room in a scratch scene, injects a
/// page of offers, a world's trade context (haulers, hangar, convoys in flight, our offers) and a few stars, then
/// renders into Screenshots/: the floor from the stand (carousel, star map, ribbon), an offer brought to the
/// cradle (lid open, card, keys), the convoy composer, a sale drafted on the pad, and a launch in flight.
/// Entry: unity command run_script --file AgentScripts/MarketRoomVerify.cs --entry MarketRoomVerify.Run
/// </summary>
public static class MarketRoomVerify
{
    public static string Run()
    {
        var previous = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (previous.isDirty)
            return "aborted: the open scene has unsaved changes";
        var previousPath = previous.path;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var types = new[]
        {
            typeof(Core.Audio.AmbienceDirector), typeof(Core.Audio.SfxBus), typeof(ViewFade),
            typeof(Core.UI.TriggerSelect), typeof(Core.UI.HoloKeyboard), typeof(ComfortSettings)
        };
        var root = new GameObject("MarketVerifyRoot");
        var starsField = typeof(GalaxyCatalog).GetField("Stars", BindingFlags.Static | BindingFlags.NonPublic);
        var stars = (List<GalaxyCatalog.Star>)starsField!.GetValue(null);
        var savedStars = new List<GalaxyCatalog.Star>(stars);
        MarketRoom room = null;
        try
        {
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(root.transform, false);
            foreach (var t in types)
                Preseed(root, t);
            var auth = AuthManager.Ensure();
            typeof(AuthManager).GetProperty("User")!.SetValue(auth, new UserSession { id = 42, token = "verify", username = "verify" });

            stars.Clear();
            void Star(int id, string name, float x, float y) => stars.Add(new GalaxyCatalog.Star { Id = id, Name = name, X = x, Y = y });
            Star(100, "Sol", 0f, 0f);
            Star(101, "Vega", 6f, 3f);
            Star(102, "Altaïr", -11f, 7f);
            Star(103, "Deneb", 22f, -14f);
            Star(104, "Rigel", -4f, -18f);
            Star(105, "Mira", 35f, 20f);

            var interior = new GameObject("BridgeInterior");
            interior.transform.SetParent(root.transform, false);
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();
            room = MarketRoom.Build(env.Art, new FocusContext());
            room.transform.SetParent(root.transform, true);

            var page = new MarketPage { Total = 12, Page = 1, Limit = 5 };
            page.Listings.Add(L(1, 7, "mineral", "crystal", "Cristal", 4000, "mineral", 9000, 4000, "Vex", "Kaarn", "Vega IV", 101, 6.7f));
            page.Listings.Add(L(2, 8, "module", "HyperspaceDrive", "Hyperpropulseur", 1, "crystal", 3500, 1000, "Oriel", "Ligue d'Altaïr", "Altaïr II", 102, 13f));
            page.Listings.Add(L(3, 9, "mineral", "biomass", "Biomasse", 12000, "crystal", 2600, 12000, "Mara", string.Empty, "Deneb Prime", 103, 26.1f));
            page.Listings.Add(L(4, 42, "module", "MissileCannon", "Canon à missiles", 2, "mineral", 5000, 1000, "verify", "Verify", "Aurelia", 100, 0f));
            page.Listings.Add(L(5, 10, "mineral", "mineral", "Minerai", 20000, "biomass", 8000, 20000, "Thal", "Concorde", "Rigel b", 104, 18.4f));

            var ctx = new MarketContext { PlanetId = 208, PlanetName = "Aurelia", SystemId = 100, Mineral = 182000, Crystal = 54000, Biomass = 21000, OrbitCargoFree = 7500 };
            ctx.Hangar.Add(("FusionThruster", "Propulseur à fusion", 2));
            ctx.Hangar.Add(("ArmorPlating", "Blindage", 1));
            ctx.Haulers.Add(new MarketHauler { Id = 8035, Name = "SG-A", CargoFree = 2500, CargoTotal = 2500, Speed = 87, HasHyperdrive = true });
            ctx.Haulers.Add(new MarketHauler { Id = 8040, Name = "Mule-3", CargoFree = 5000, CargoTotal = 5000, Speed = 40, HasHyperdrive = false });
            var now = FleetOrderGate.UnixNow();
            ctx.Missions.Add(new MarketMission { Id = 1, FleetId = 8100, FleetName = "Convoi marchand #77", Status = "traveling_to", Phase = "outbound",
                DepartureTime = now - 600, OutboundArrival = now + 900, ItemName = "Cristal", Quantity = 3000, OriginName = "Aurelia", TargetName = "Deneb Prime", TargetSystemId = 103 });
            ctx.Missions.Add(new MarketMission { Id = 2, FleetId = 8101, FleetName = "Convoi marchand #71", Status = "traveling_back", Phase = "inbound",
                DepartureTime = now - 4000, OutboundArrival = now - 2000, InboundArrival = now + 800, ItemName = "Blindage", Quantity = 2, OriginName = "Aurelia", TargetName = "Mira V", TargetSystemId = 105 });
            ctx.Missions.Add(new MarketMission { Id = 3, FleetId = 8102, FleetName = "Convoi marchand #64", Status = "intercepted", Phase = "outbound",
                ItemName = "Minerai", Quantity = 9000, OriginName = "Aurelia", TargetName = "Altaïr II", TargetSystemId = 102 });
            ctx.MyListings.Add(L(4, 42, "module", "MissileCannon", "Canon à missiles", 2, "mineral", 5000, 1000, "verify", "Verify", "Aurelia", 100, 0f));
            room.EditorShow(page, ctx);
            var pit = room.EditorPit;

            var camGo = new GameObject("MarketShotCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 120f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.005f, 0.008f, 0.02f);
            camGo.tag = "MainCamera";
            var rt = room.transform;
            void Shot(Vector3 eye, Vector3 at, string file, float fov = 70f)
            {
                cam.fieldOfView = fov;
                camGo.transform.position = rt.TransformPoint(eye);
                camGo.transform.LookAt(rt.TransformPoint(at));
                Capture(cam, Path.Combine(Path.GetFullPath("Screenshots"), file));
            }

            Directory.CreateDirectory(Path.GetFullPath("Screenshots"));
            pit.EditorHover(2);
            pit.EditorStep(10f, 1.5f, cam);
            Shot(new Vector3(0f, 1.62f, -0.15f), new Vector3(0f, 2.1f, 5f), "market-floor.png", 82f);
            Shot(new Vector3(-4.6f, 2.4f, 0.6f), new Vector3(0.6f, 1.6f, 6f), "market-wide.png", 80f);

            room.EditorSelect(2);
            pit.EditorStep(12f, 2.2f, cam);
            Shot(new Vector3(0f, 1.6f, 0.05f), new Vector3(0f, 1.25f, 1.25f), "market-cradle.png", 70f);

            room.EditorBuy(2);
            Shot(new Vector3(0.55f, 1.62f, 0.15f), new Vector3(1.25f, 1.25f, 0.95f), "market-composer.png", 60f);
            Shot(new Vector3(-0.55f, 1.62f, 0.15f), new Vector3(-1.25f, 1.25f, 0.95f), "market-exchange.png", 60f);

            room.EditorDraft(1, 5000, 12000);
            pit.EditorStep(20f, 0.6f, cam);
            Shot(new Vector3(0.1f, 1.6f, 0.1f), new Vector3(2.05f, 1.05f, -0.02f), "market-pad.png", 64f);

            pit.Launch(2);
            pit.EditorStep(30f, 1.8f, cam);
            Shot(new Vector3(-3.5f, 1.8f, 1f), new Vector3(0f, 3.2f, 7f), "market-launch.png", 80f);
            return "ok: offers=" + page.Listings.Count + " held=" + (pit.Held != null ? pit.Held.Id : 0);
        }
        finally
        {
            MarketRoom.EditorReset();
            stars.Clear();
            stars.AddRange(savedStars);
            Object.DestroyImmediate(root);
            if (room != null)
                Object.DestroyImmediate(room.gameObject);
            foreach (var a in Object.FindObjectsByType<AuthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (a != null)
                    Object.DestroyImmediate(a.gameObject);
            typeof(AuthManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            foreach (var t in types)
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == t)
                        f.SetValue(null, null);
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        }
    }

    static MarketListing L(int id, int user, string cat, string key, string name, float qty, string cur, float price, int volume,
        string seller, string empire, string planet, int system, float distance) => new()
    {
        Id = id, UserId = user, Category = cat, ItemKey = key, ItemName = name, Quantity = qty, PriceCurrency = cur, PriceAmount = price,
        CargoVolume = volume, SellerName = seller, EmpireName = empire, PlanetName = planet, SystemId = system, Distance = distance
    };

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
