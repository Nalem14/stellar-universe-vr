using System.IO;
using Core.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Core.Editor
{
    public static class FirstSliceBuilder
    {
        const string ScenesRoot = "Assets/_Core/Scenes";
        const string XrOriginGuid = "77e7c27b2c5525e4aa8cc9f99d654486";

        public static void Run()
        {
            EnsureFolder("Assets/_Core");
            EnsureFolder(ScenesRoot);
            ConfigureCicImports();
            ApplyPlayerIdentity();

            var boot = BuildScene("Boot", typeof(BootDirector));
            var menu = BuildScene("Menu", typeof(MenuDirector));
            var bridge = BuildScene("Bridge", typeof(BridgeDirector));

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(boot, true),
                new EditorBuildSettingsScene(menu, true),
                new EditorBuildSettingsScene(bridge, true)
            };

            AssetDatabase.SaveAssets();
            Debug.Log("[SU] FirstSliceBuilder: Boot / Menu / Bridge wired.");
        }

        static string BuildScene(string name, System.Type directorType)
        {
            var path = $"{ScenesRoot}/{name}.unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.55f, 0.7f, 0.85f);
            light.intensity = 0.35f;
            lightGo.transform.rotation = Quaternion.Euler(42f, -30f, 0f);

            SpawnXrOrigin();
            StripSceneJunk();

            var host = new GameObject($"{name}Director");
            host.AddComponent(directorType);

            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.ImportAsset(path);
            return path;
        }

        static void SpawnXrOrigin()
        {
            var prefabPath = AssetDatabase.GUIDToAssetPath(XrOriginGuid);
            if (string.IsNullOrEmpty(prefabPath))
            {
                Debug.LogError("[SU] XR Origin Hands prefab not found.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError("[SU] Failed to load XR Origin prefab at " + prefabPath);
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "XR Origin Hands (XR Rig)";
            instance.transform.position = Vector3.zero;
        }

        static void StripSceneJunk()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                StripRecursive(root.transform);
        }

        static void StripRecursive(Transform t)
        {
            for (var i = t.childCount - 1; i >= 0; i--)
                StripRecursive(t.GetChild(i));

            var n = t.name;
            if (n.IndexOf("Coaching", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.IndexOf("Passthrough", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                n == "Interactables" ||
                n == "Environment")
                t.gameObject.SetActive(false);
        }

        static void ConfigureCicImports()
        {
            ConfigureTexture("Assets/_Core/Resources/CIC/Floor.png", TextureWrapMode.Repeat);
            ConfigureTexture("Assets/_Core/Resources/CIC/Wall.png", TextureWrapMode.Repeat);
            ConfigureTexture("Assets/_Core/Resources/CIC/PanelBrushed.png", TextureWrapMode.Repeat);
            ConfigureTexture("Assets/_Core/Resources/CIC/DeckRib.png", TextureWrapMode.Repeat);
            ConfigureTexture("Assets/_Core/Resources/CIC/ScreenIdle.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/CIC/VentGrill.png", TextureWrapMode.Repeat);
            ConfigureTexture("Assets/_Core/Resources/CIC/HoloTable.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/CIC/ViewportStars.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/CIC/BootTitle.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/Holo/OrbitPlate.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/Holo/TokenSystem.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/Holo/TokenPlanet.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/Holo/TokenFleet.png", TextureWrapMode.Clamp);
            ConfigureTexture("Assets/_Core/Resources/Holo/ProjectorGlow.png", TextureWrapMode.Clamp);
            AssetDatabase.ImportAsset("Assets/_Core/Resources/CIC/ambient.mp3");
            HoloUiSpriteImporter.ImportAll();
        }

        static void ConfigureTexture(string path, TextureWrapMode wrap)
        {
            if (!File.Exists(path))
            {
                Debug.LogWarning("[SU] Missing texture " + path);
                return;
            }

            AssetDatabase.ImportAsset(path);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;
            importer.wrapMode = wrap;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
        }

        static void ApplyPlayerIdentity()
        {
            PlayerSettings.companyName = "StellarUniverse";
            PlayerSettings.productName = "Stellar Universe VR";
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.stellaruniverse.vr");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.stellaruniverse.vr");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.forceInternetPermission = true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            var parts = path.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
