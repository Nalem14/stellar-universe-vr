using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Core.Editor
{
    /// <summary>
    /// Player builds into Builds/Android, Builds/Windows, Builds/macOS, Builds/Linux —
    /// the folders scripts/deploy.sh and deploy.bat send to itch.io.
    /// </summary>
    public sealed class PlatformBuilds : EditorWindow
    {
        const string Menu = "Stellar Universe/Builds";

        struct Target
        {
            public string Label;
            public string Folder;
            public BuildTarget Player;
            public BuildTargetGroup Group;
            public string FileName;
        }

        static readonly Target[] Targets =
        {
            new()
            {
                Label = "Android",
                Folder = "Android",
                Player = BuildTarget.Android,
                Group = BuildTargetGroup.Android,
                FileName = "Stellar Universe.apk"
            },
            new()
            {
                Label = "Windows",
                Folder = "Windows",
                Player = BuildTarget.StandaloneWindows64,
                Group = BuildTargetGroup.Standalone,
                FileName = "Stellar Universe.exe"
            },
            new()
            {
                Label = "macOS",
                Folder = "macOS",
                Player = BuildTarget.StandaloneOSX,
                Group = BuildTargetGroup.Standalone,
                FileName = "Stellar Universe.app"
            },
            new()
            {
                Label = "Linux",
                Folder = "Linux",
                Player = BuildTarget.StandaloneLinux64,
                Group = BuildTargetGroup.Standalone,
                FileName = "Stellar Universe.x86_64"
            }
        };

        bool _development;

        [MenuItem(Menu)]
        public static void Open()
        {
            var window = GetWindow<PlatformBuilds>("Builds");
            window.minSize = new Vector2(360f, 220f);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Sortie", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Chaque build va dans Builds/<plateforme>, à côté du projet. " +
                "Un dossier manquant est créé. L'APK déjà à la racine de Builds n'est pas déplacé.",
                MessageType.Info);

            _development = EditorGUILayout.Toggle("Development", _development);
            EditorGUILayout.Space(8f);

            for (var i = 0; i < Targets.Length; i++)
            {
                var t = Targets[i];
                if (GUILayout.Button(t.Label, GUILayout.Height(28f)))
                    BuildOne(t);
            }

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Les quatre", GUILayout.Height(32f)))
                BuildAll();
        }

        void BuildOne(Target target)
        {
            if (!Prepare())
                return;
            var report = Run(target);
            ShowOne(target, report);
        }

        void BuildAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "Builds",
                    "Lancer Android, Windows, macOS et Linux à la suite ? L'éditeur change de plateforme à chaque fois.",
                    "Lancer",
                    "Annuler"))
                return;
            if (!Prepare())
                return;

            var lines = new List<string>();
            for (var i = 0; i < Targets.Length; i++)
            {
                var t = Targets[i];
                var report = Run(t);
                var ok = report != null && report.summary.result == BuildResult.Succeeded;
                lines.Add(ok ? t.Label + "  →  " + report.summary.outputPath : t.Label + "  —  échec");
                if (!ok)
                    break;
            }

            EditorUtility.DisplayDialog("Builds", string.Join("\n", lines), "OK");
        }

        static bool Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Builds", "Arrête le Play Mode avant de builder.", "OK");
                return false;
            }

            return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }

        BuildReport Run(Target target)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Builds", "Aucune scène cochée dans Build Settings.", "OK");
                return null;
            }

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", target.Folder));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, target.FileName);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = target.Player,
                targetGroup = target.Group,
                options = _development ? BuildOptions.Development : BuildOptions.None
            };

            Debug.Log("[SU] Build " + target.Label + " → " + path);
            return BuildPipeline.BuildPlayer(options);
        }

        static void ShowOne(Target target, BuildReport report)
        {
            if (report == null)
                return;
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog(
                    "Builds",
                    target.Label + "\n" + summary.outputPath + "\n" + (summary.totalSize / (1024 * 1024)) + " Mo",
                    "OK");
                EditorUtility.RevealInFinder(summary.outputPath);
                return;
            }

            EditorUtility.DisplayDialog(
                "Builds",
                target.Label + " : " + summary.result + " (" + summary.totalErrors + " erreurs). Voir la console.",
                "OK");
        }
    }
}
