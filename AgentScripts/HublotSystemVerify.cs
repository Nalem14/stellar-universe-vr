using System.IO;
using Core.App;
using Core.Entity;
using Core.Vfx;
using UnityEditor;
using UnityEngine;

/// <summary>Ephemeral verifier + trailer hublot shot. Entry: HublotSystemVerify.Run</summary>
public static class HublotSystemVerify
{
    public static string Run()
    {
        var old = GameObject.Find("HublotVerifyRoot");
        if (old != null)
            Object.DestroyImmediate(old);

        var root = new GameObject("HublotVerifyRoot");

        var auth = AuthManager.Ensure();
        typeof(AuthManager).GetProperty("User")!.SetValue(auth,
            new UserSession { id = 42, token = "verify", username = "verify" });

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

        var focus = new FocusContext();
        focus.SetFromApi(9001, BuildSystemsJson(), BuildFleetsJson(), preferredFleetId: 501);
        typeof(FocusContext).GetProperty("ViewFleetId")!.SetValue(focus, 0);

        exterior.Bind(focus);
        viewRig.Bind(focus, exterior, interior.transform);

        var planet = GameObject.Find("Planet_102");
        var star = exterior.transform.position;
        if (planet != null)
        {
            var radial = (planet.transform.position - star).normalized;
            if (radial.sqrMagnitude < 0.01f) radial = Vector3.right;
            var side = Vector3.Cross(Vector3.up, radial).normalized;
            var park = planet.transform.position + radial * WorldScale.FleetStandoff(WorldScale.PlanetRadius(3))
                       + side * WorldScale.FleetLateral;
            var vs = GameObject.Find("ViewShip");
            if (vs != null)
            {
                vs.transform.position = park + Vector3.up * WorldScale.EclipticHeight;
                vs.transform.rotation = Quaternion.LookRotation((-radial).normalized, Vector3.up);
            }
        }

        Transform mount = env.HublotMounts != null && env.HublotMounts.Count > 0
            ? env.HublotMounts[env.HublotMounts.Count / 2]
            : null;

        var camGo = new GameObject("HublotShotCam");
        camGo.transform.SetParent(root.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = WorldScale.BridgeFarClip;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
        cam.fieldOfView = 70f;
        cam.allowHDR = true;
        cam.tag = "MainCamera";

        if (mount != null)
            camGo.transform.position = mount.TransformPoint(new Vector3(0f, 0.02f, -0.42f));
        else
            camGo.transform.position = interior.transform.TransformPoint(new Vector3(0f, 1.65f, 5.2f));

        // Stage fleets / rocks into the look cone for the trailer recipe
        var forward = mount != null
            ? mount.TransformDirection(Vector3.forward)
            : camGo.transform.forward;
        var right = Vector3.Cross(Vector3.up, forward).normalized;
        var origin = camGo.transform.position;
        var own = GameObject.Find("Fleet_501");
        var foe = GameObject.Find("Fleet_502");
        var rock1 = GameObject.Find("Asteroid_201");
        var rock2 = GameObject.Find("Asteroid_202");
        if (own != null)
        {
            own.transform.position = origin + forward * 48f + right * 10f + Vector3.up * -3f;
            own.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        if (foe != null)
        {
            foe.transform.position = origin + forward * 62f + right * -14f + Vector3.up * -1f;
            foe.transform.rotation = Quaternion.LookRotation((star - foe.transform.position).normalized, Vector3.up);
        }

        if (rock1 != null)
            rock1.transform.position = origin + forward * 36f + right * -16f + Vector3.up * 2f;
        if (rock2 != null)
            rock2.transform.position = origin + forward * 42f + right * -20f + Vector3.up * -2f;

        var aim = star;
        if (planet != null) aim = Vector3.Lerp(star, planet.transform.position, 0.2f);
        if (own != null) aim = Vector3.Lerp(aim, own.transform.position, 0.2f);
        if (foe != null) aim = Vector3.Lerp(aim, foe.transform.position, 0.12f);
        camGo.transform.rotation = Quaternion.LookRotation((aim - origin).normalized, Vector3.up);

        // Face billboards toward shot cam
        foreach (var b in Object.FindObjectsByType<BillboardFace>(FindObjectsSortMode.None))
            b.FaceNow();

        for (var i = 0; i < 10; i++)
            foreach (var spin in Object.FindObjectsByType<BodySpin>(FindObjectsSortMode.None))
                spin.transform.Rotate(spin.Axis, spin.DegreesPerSecond * 0.08f, Space.Self);

        var content = exterior.transform.GetChild(0);
        var planets = 0;
        var rocks = 0;
        var fleets = 0;
        foreach (Transform t in content)
        {
            if (t.name.StartsWith("Planet_")) planets++;
            else if (t.name.StartsWith("Asteroid_")) rocks++;
            else if (t.name.StartsWith("Fleet_")) fleets++;
        }

        var mats = new System.Collections.Generic.HashSet<Material>();
        var unlit = 0;
        var starOk = false;
        foreach (var r in exterior.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial == null) continue;
            mats.Add(r.sharedMaterial);
            if (r.name == "Star" && r.sharedMaterial.shader.name.Contains("Star")) starOk = true;
            var sn = r.sharedMaterial.shader.name;
            if (sn == "Unlit/Color" || sn == "Sprites/Default") unlit++;
        }

        Capture(cam, Path.GetFullPath("Assets/Screenshots/hublot-system.png"), 1920, 1080);
        var report =
            $"starOk={starOk} planets={planets} asteroids={rocks} fleets={fleets} uniqueMats={mats.Count} unlit={unlit} mounts={env.HublotMounts.Count}";
        Debug.Log("[HublotSystemVerify] " + report);

        // Never leave verify junk in an open authoring scene (Boot/Menu/Bridge).
        CleanupVerifyArtifacts(root);
        return report;
    }

    static void CleanupVerifyArtifacts(GameObject root)
    {
        if (root != null)
            Object.DestroyImmediate(root);

        // AuthManager.Ensure() in edit mode can stack DontDestroy-ish roots into the open scene.
        var leftovers = Object.FindObjectsByType<AuthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < leftovers.Length; i++)
        {
            if (leftovers[i] != null)
                Object.DestroyImmediate(leftovers[i].gameObject);
        }

        var amField = typeof(AuthManager).GetProperty("Instance");
        // Instance has private setter via auto-prop — clear via reflection if still set
        if (amField != null && amField.CanWrite == false)
        {
            var backing = typeof(AuthManager).GetField("<Instance>k__BackingField",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            backing?.SetValue(null, null);
        }
        else
        {
            amField?.SetValue(null, null);
        }
    }

    static void Capture(Camera cam, string absPath, int w, int h)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        File.WriteAllBytes(absPath, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset("Assets/Screenshots/hublot-system.png");
    }

    static string BuildSystemsJson() =>
        @"[{""id"":9001,""name"":""Verify"",""type"":""orange"",""x"":0,""y"":0,""planets"":[
{""id"":101,""name"":""Ash"",""slot"":1,""userid"":0,""_habitability"":2},
{""id"":102,""name"":""Home"",""slot"":3,""userid"":42,""_habitability"":8},
{""id"":103,""name"":""Giant"",""slot"":5,""userid"":0},
{""id"":104,""name"":""Frost"",""slot"":8,""userid"":77,""_habitability"":2}
],""asteroids"":[
{""id"":201,""slot"":4},{""id"":202,""slot"":4},{""id"":203,""slot"":6},{""id"":204,""slot"":6},{""id"":205,""slot"":7}
]}]";

    static string BuildFleetsJson() =>
        @"[
{""id"":501,""name"":""Own"",""userid"":42,""systemid"":9001,""planetid"":102,""desttime"":0,""ships"":[
{""id"":1,""type"":""ShipCore"",""grid_x"":4,""grid_y"":4},
{""id"":2,""type"":""ShipWeaponLaser"",""grid_x"":4,""grid_y"":2},
{""id"":3,""type"":""ShipEngine"",""grid_x"":4,""grid_y"":6},
{""id"":4,""type"":""ShipArmor"",""grid_x"":3,""grid_y"":4},
{""id"":5,""type"":""ShipArmor"",""grid_x"":5,""grid_y"":4},
{""id"":6,""type"":""ShipCargo"",""grid_x"":3,""grid_y"":5},
{""id"":7,""type"":""ShipWeaponMissile"",""grid_x"":5,""grid_y"":2}
]},
{""id"":502,""name"":""Foe"",""userid"":99,""systemid"":9001,""planetid"":102,""desttime"":0,""ships"":[
{""id"":11,""type"":""ShipCore"",""grid_x"":4,""grid_y"":4},
{""id"":12,""type"":""ShipWeaponPlasma"",""grid_x"":4,""grid_y"":1},
{""id"":13,""type"":""ShipEngine"",""grid_x"":3,""grid_y"":7},
{""id"":14,""type"":""ShipEngine"",""grid_x"":5,""grid_y"":7},
{""id"":15,""type"":""ShipWeaponMissile"",""grid_x"":3,""grid_y"":2},
{""id"":16,""type"":""ShipArmor"",""grid_x"":2,""grid_y"":4}
]}
]";
}
