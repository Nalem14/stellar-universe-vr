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
                "Chaque build va dans Builds/<plateforme>. " +
                "Le script choisit le player installé pour la plateforme, puis remet le réglage du projet.",
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
            var report = Run(target, out var note);
            ShowOne(target, report, note);
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

            // Held across every platform so switching the Windows player cannot reload
            // the domain and drop the macOS and Linux builds that follow.
            var lines = new List<string>();
            EditorApplication.LockReloadAssemblies();
            try
            {
                for (var i = 0; i < Targets.Length; i++)
                {
                    var t = Targets[i];
                    var report = Run(t, out var note);
                    var ok = report != null && report.summary.result == BuildResult.Succeeded;
                    var suffix = string.IsNullOrEmpty(note) ? "" : " (" + note + ")";
                    lines.Add(ok
                        ? t.Label + suffix + "  →  " + report.summary.outputPath
                        : t.Label + suffix + "  —  échec");
                    if (!ok)
                        break;
                }
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
            }

            EditorUtility.DisplayDialog("Builds", string.Join("\n", lines), "OK");
        }

        /// <summary>
        /// The four players in a row with no dialog (an agent or a script drives the editor): each result, then
        /// DONE, in Builds/build-report.txt. Stops at the first failure, like "Les quatre".
        /// </summary>
        public static void BuildAllUnattended()
        {
            var report = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "build-report.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllText(report, "START " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
            var builder = CreateInstance<PlatformBuilds>();
            EditorApplication.LockReloadAssemblies();
            try
            {
                foreach (var t in Targets)
                {
                    var result = builder.Run(t, out var note);
                    var ok = result != null && result.summary.result == BuildResult.Succeeded;
                    File.AppendAllText(report, (ok ? "OK " : "FAIL ") + t.Label + (string.IsNullOrEmpty(note) ? "" : " (" + note + ")") +
                                               (ok ? " " + result.summary.outputPath + " " + result.summary.totalSize / (1024 * 1024) + " Mo"
                                                   : result != null ? " " + result.summary.result + ", " + result.summary.totalErrors + " erreurs" : "") +
                                               " " + System.DateTime.Now.ToString("HH:mm:ss") + "\n");
                    if (!ok)
                        break;
                }
            }
            finally
            {
                EditorApplication.UnlockReloadAssemblies();
                DestroyImmediate(builder);
                File.AppendAllText(report, "DONE\n");
            }
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

        BuildReport Run(Target target, out string note)
        {
            note = null;
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

            var named = NamedTarget(target);
            var projectBackend = PlayerSettings.GetScriptingBackend(named);
            var backend = ChooseBackend(target, projectBackend);
            if (backend != projectBackend)
                note = backend == ScriptingImplementation.Mono2x ? "Mono" : "IL2CPP";

            Debug.Log("[SU] Build " + target.Label + (note == null ? "" : " (" + note + ")") + " → " + path);
            EditorApplication.LockReloadAssemblies();
            try
            {
                if (backend != projectBackend)
                    PlayerSettings.SetScriptingBackend(named, backend);
                return BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                if (PlayerSettings.GetScriptingBackend(named) != projectBackend)
                    PlayerSettings.SetScriptingBackend(named, projectBackend);
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        static UnityEditor.Build.NamedBuildTarget NamedTarget(Target target)
        {
            return target.Group == BuildTargetGroup.Android
                ? UnityEditor.Build.NamedBuildTarget.Android
                : UnityEditor.Build.NamedBuildTarget.Standalone;
        }

        /// <summary>
        /// The project asks for one backend for every standalone player. This Mac editor
        /// ships Windows as Mono only, while macOS and Linux are IL2CPP. Use the installed
        /// player for the target being built.
        /// </summary>
        static ScriptingImplementation ChooseBackend(Target target, ScriptingImplementation projectBackend)
        {
            if (target.Group != BuildTargetGroup.Standalone)
                return projectBackend;
            if (PlayerInstalled(target, projectBackend))
                return projectBackend;

            var other = projectBackend == ScriptingImplementation.Mono2x
                ? ScriptingImplementation.IL2CPP
                : ScriptingImplementation.Mono2x;
            return PlayerInstalled(target, other) ? other : projectBackend;
        }

        static bool PlayerInstalled(Target target, ScriptingImplementation backend)
        {
            string support;
            string arch;
            switch (target.Player)
            {
                case BuildTarget.StandaloneWindows64:
                    support = "WindowsStandaloneSupport";
                    arch = "win64";
                    break;
                case BuildTarget.StandaloneOSX:
                    support = "MacStandaloneSupport";
                    arch = "macos";
                    break;
                case BuildTarget.StandaloneLinux64:
                    support = "LinuxStandaloneSupport";
                    arch = "linux64";
                    break;
                default:
                    return true;
            }

            var token = backend == ScriptingImplementation.Mono2x ? "mono" : "il2cpp";
            var roots = new[]
            {
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", support, "Variations"),
                Path.GetFullPath(Path.Combine(
                    EditorApplication.applicationContentsPath, "..", "..", "PlaybackEngines", support, "Variations"))
            };
            foreach (var variations in roots)
            {
                if (!Directory.Exists(variations))
                    continue;
                foreach (var dir in Directory.GetDirectories(variations))
                {
                    var name = Path.GetFileName(dir);
                    if (name.Contains(arch) && name.Contains("player") && name.Contains(token))
                        return true;
                }
            }

            return false;
        }

        static void ShowOne(Target target, BuildReport report, string note)
        {
            if (report == null)
                return;
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                var backend = string.IsNullOrEmpty(note) ? "" : "\n" + note;
                EditorUtility.DisplayDialog(
                    "Builds",
                    target.Label + backend + "\n" + summary.outputPath + "\n" + (summary.totalSize / (1024 * 1024)) + " Mo",
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
