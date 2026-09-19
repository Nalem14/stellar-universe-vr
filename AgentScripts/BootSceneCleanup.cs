using System.Collections.Generic;
using System.Reflection;
using Core.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Purge verify clutter from the open scene hierarchy. Entry: BootSceneCleanup.Run</summary>
public static class BootSceneCleanup
{
    public static string Run()
    {
        var scene = SceneManager.GetActiveScene();
        var removed = new List<string>();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == null) continue;
            var n = go.name;
            if (n != "AuthManager" && n != "HublotVerifyRoot" &&
                !n.StartsWith("XR Origin", System.StringComparison.Ordinal))
                continue;
            removed.Add(n + "#" + go.GetInstanceID());
            Object.DestroyImmediate(go);
        }

        // Also sweep any AuthManager left as DontDestroy orphan while editing
        foreach (var am in Object.FindObjectsByType<AuthManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (am == null) continue;
            removed.Add("AuthManagerDDOL#" + am.GetInstanceID());
            Object.DestroyImmediate(am.gameObject);
        }

        var backing = typeof(AuthManager).GetField("<Instance>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic);
        backing?.SetValue(null, null);

        var left = new List<string>();
        foreach (var go in scene.GetRootGameObjects())
            left.Add(go.name);

        // Disk asset was already clean; only mark dirty if something remains unexpected
        if (scene.path.Contains("Boot") && left.Count == 2 &&
            left.Contains("Directional Light") && left.Contains("BootDirector"))
        {
            // Ensure no accidental dirty save of junk
            return $"cleaned={removed.Count} left=[{string.Join(", ", left)}] disk=Boot ok";
        }

        EditorSceneManager.MarkSceneDirty(scene);
        return $"cleaned={removed.Count} removed=[{string.Join(", ", removed)}] left=[{string.Join(", ", left)}]";
    }
}
