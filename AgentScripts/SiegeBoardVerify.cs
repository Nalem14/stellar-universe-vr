using System.IO;
using System.Reflection;
using Core.App;
using Core.Entity;
using Core.Vfx;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Ephemeral verifier for the planetary siege board (web 69d40af): the besieged world at (0,0), our fortress
/// beside it, two attackers, one ship gone on a retreat order. Applies an injected GetBattleState body (no
/// server), renders the table into Screenshots/ — the planet's turn (immobile: no Move, no Retreat), then a
/// ship's turn (Retreat key) — in a scratch scene, then reopens the scene that was open.
/// Entry: unity command run_script --file AgentScripts/SiegeBoardVerify.cs --entry SiegeBoardVerify.Run
/// </summary>
public static class SiegeBoardVerify
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
        var root = new GameObject("SiegeVerifyRoot");
        try
        {
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem)).transform.SetParent(root.transform, false);
            foreach (var t in types)
                Preseed(root, t);
            var auth = AuthManager.Ensure();
            typeof(AuthManager).GetProperty("User")!.SetValue(auth, new UserSession { id = 42, token = "verify", username = "verify" });

            var interior = new GameObject("BridgeInterior");
            interior.transform.SetParent(root.transform, false);
            var env = interior.AddComponent<CicEnvironment>();
            env.Layout = CicLayout.Bridge;
            env.Build();
            var focus = new FocusContext();
            focus.SetFromApi(9001,
                @"[{""id"":9001,""name"":""Verify"",""type"":""orange"",""planets"":[{""id"":102,""name"":""Aurelia"",""slot"":3,""userid"":42,""_habitability"":8}]}]",
                "[]");
            var map = env.ZoneMap;
            var mapCtrl = interior.AddComponent<HoloMapController>();
            mapCtrl.Bind(map, focus, null);
            var hex = interior.AddComponent<HexBattleController>();
            hex.Bind(focus, mapCtrl, map != null ? map.transform : interior.transform, env.Art);
            mapCtrl.BindHex(hex);

            var camGo = new GameObject("SiegeShotCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 60f;
            cam.fieldOfView = 62f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.01f, 0.015f, 0.025f);
            camGo.tag = "MainCamera";
            var table = map != null ? map.transform.position : interior.transform.TransformPoint(new Vector3(0f, 1f, WorldScale.CicTableCenterZ));
            camGo.transform.position = table + new Vector3(0f, 0.75f, -0.85f);
            camGo.transform.LookAt(table + Vector3.up * 0.05f);

            var outDir = Path.GetFullPath("Screenshots");
            Directory.CreateDirectory(outDir);
            hex.EditorApply(State(active: 1), 601);
            SettleBoard(hex);
            Capture(cam, Path.Combine(outDir, "siege-planet-turn.png"));
            hex.EditorApply(State(active: 5), 601);
            SettleBoard(hex);
            Capture(cam, Path.Combine(outDir, "siege-ship-turn.png"));
            var s = hex.State;
            return s == null ? "no state" : $"ships={s.Ships.Count} planet={s.Find(1)?.IsPlanet} immobile={s.Find(1)?.Immobile} station={s.Find(2)?.IsStation} retreated={s.Find(6)?.Retreated}";
        }
        finally
        {
            Object.DestroyImmediate(root);
            foreach (var a in Object.FindObjectsByType<AuthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (a != null)
                    Object.DestroyImmediate(a.gameObject);
            typeof(AuthManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)?.SetValue(null, null);
            foreach (var t in types)
                foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
                    if (f.FieldType == t)
                        f.SetValue(null, null);
            HexBattleController.EditorOffline = false;
            if (!string.IsNullOrEmpty(previousPath))
                EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Single);
        }
    }

    /// <summary>The board reveals over time (Update): in edit mode, push it to its final pose by hand.</summary>
    static void SettleBoard(HexBattleController hex)
    {
        var t = typeof(HexBattleController);
        t.GetField("_reveal", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(hex, 1f);
        var anim = t.GetMethod("AnimateBoard", BindingFlags.Instance | BindingFlags.NonPublic);
        for (var i = 0; i < 4; i++)
            anim?.Invoke(hex, null);
    }

    static void Preseed(GameObject root, System.Type type)
    {
        var host = new GameObject(type.Name);
        host.transform.SetParent(root.transform, false);
        var c = host.AddComponent(type);
        foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            if (f.FieldType == type && f.GetValue(null) == null)
                f.SetValue(null, c);
        // What Ensure() would have done after AddComponent (edit mode runs no Awake).
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

    static string State(int active) => @"{
""battle"":{""id"":424242,""state"":1,""planetid"":102},""round"":2,""active_bship_id"":" + active + @",""my_team"":1,
""my_turn"":true,""my_fleet_id"":601,""timeline"":[{""id"":1},{""id"":5},{""id"":2},{""id"":3},{""id"":4}],""teams"":[],""log"":[],
""ships"":[
{""id"":1,""fleet_id"":0,""team"":1,""hex_q"":0,""hex_r"":0,""hp"":820,""max_hp"":1240,""shield_cur"":300,""max_shield"":450,
 ""ap_left"":6,""max_ap"":6,""pm_left"":0,""max_pm"":0,""alive"":1,""is_planet"":1,""planet_id"":102,""is_mine"":true,
 ""is_active"":" + (active == 1 ? "true" : "false") + @",""fleet_name"":""Aurelia (Planète)"",""modules"":[],""status"":{},
 ""skills"":[{""id"":""planetary_battery"",""type"":""attack"",""ap"":3,""damage"":85,""range_min"":1,""range"":6,""cooldown"":1},
            {""id"":""flak_barrage"",""type"":""attack_aoe"",""ap"":3,""damage"":45,""range_min"":1,""range"":4,""aoe_radius"":1,""cooldown"":2},
            {""id"":""garrison_counter"",""type"":""heal_shield"",""ap"":2,""shield"":80,""range"":0,""cooldown"":2}]},
{""id"":2,""fleet_id"":601,""team"":1,""hex_q"":1,""hex_r"":0,""hp"":420,""max_hp"":480,""shield_cur"":200,""max_shield"":260,
 ""ap_left"":6,""max_ap"":6,""pm_left"":0,""max_pm"":0,""alive"":1,""is_station"":1,""planet_id"":102,""is_mine"":true,""is_active"":false,
 ""fleet_name"":""Station Orbitale Aurelia"",""status"":{},
 ""modules"":[{""id"":1,""type"":""StationCore"",""grid_x"":4,""grid_y"":4},{""id"":2,""type"":""OrbitalDefenseBattery"",""grid_x"":4,""grid_y"":3},
              {""id"":3,""type"":""PlanetaryShieldProjector"",""grid_x"":5,""grid_y"":4},{""id"":4,""type"":""CitadelReactor"",""grid_x"":3,""grid_y"":4}],
 ""skills"":[{""id"":""orbital_cannonade"",""type"":""attack"",""ap"":3,""damage"":95,""range_min"":1,""range"":5,""cooldown"":1},
            {""id"":""reactor_overcharge"",""type"":""reactor_overcharge"",""ap"":1,""shield"":100,""range"":0,""cooldown"":2}]},
{""id"":5,""fleet_id"":602,""team"":1,""hex_q"":2,""hex_r"":-1,""hp"":160,""max_hp"":200,""shield_cur"":40,""max_shield"":60,
 ""ap_left"":4,""max_ap"":4,""pm_left"":3,""max_pm"":3,""alive"":1,""is_mine"":true,""is_active"":" + (active == 5 ? "true" : "false") + @",
 ""fleet_name"":""Vigilant"",""status"":{},
 ""modules"":[{""id"":11,""type"":""ShipCore"",""grid_x"":4,""grid_y"":4},{""id"":12,""type"":""LaserCannon"",""grid_x"":4,""grid_y"":3},{""id"":13,""type"":""HyperspaceDrive"",""grid_x"":4,""grid_y"":5}],
 ""skills"":[{""id"":""basic_attack"",""type"":""attack"",""ap"":2,""damage"":30,""range_min"":1,""range"":3,""cooldown"":0}]},
{""id"":3,""fleet_id"":701,""team"":0,""hex_q"":-3,""hex_r"":0,""hp"":300,""max_hp"":320,""shield_cur"":0,""max_shield"":80,
 ""ap_left"":4,""max_ap"":4,""pm_left"":3,""max_pm"":3,""alive"":1,""is_mine"":false,""is_active"":false,""fleet_name"":""Raider"",""status"":{},
 ""modules"":[{""id"":21,""type"":""ShipCore"",""grid_x"":4,""grid_y"":4},{""id"":22,""type"":""PlasmaCannon"",""grid_x"":4,""grid_y"":3}],""skills"":[]},
{""id"":4,""fleet_id"":702,""team"":0,""hex_q"":-4,""hex_r"":2,""hp"":260,""max_hp"":260,""shield_cur"":60,""max_shield"":60,
 ""ap_left"":4,""max_ap"":4,""pm_left"":3,""max_pm"":3,""alive"":1,""is_mine"":false,""is_active"":false,""fleet_name"":""Lancer"",""status"":{""jammed"":1},
 ""modules"":[{""id"":31,""type"":""ShipCore"",""grid_x"":4,""grid_y"":4},{""id"":32,""type"":""MissileCannon"",""grid_x"":4,""grid_y"":3}],""skills"":[]},
{""id"":6,""fleet_id"":703,""team"":0,""hex_q"":-999,""hex_r"":-999,""hp"":40,""max_hp"":200,""shield_cur"":0,""max_shield"":0,
 ""ap_left"":0,""max_ap"":4,""pm_left"":0,""max_pm"":3,""alive"":0,""retreated"":1,""is_mine"":false,""is_active"":false,""fleet_name"":""Fuyard"",
 ""status"":{},""modules"":[],""skills"":[]}
]}";
}
