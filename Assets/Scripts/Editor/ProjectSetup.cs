using System.IO;
using Nullroute.UnityFrontend;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Nullroute.EditorTools
{
    /// <summary>
    /// One-click project setup and builds. Also callable from the command line:
    ///   Unity -batchmode -quit -projectPath . -executeMethod Nullroute.EditorTools.ProjectSetup.BuildWindows
    /// </summary>
    public static class ProjectSetup
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Nullroute/Setup Main Scene")]
        public static void SetupScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;

            new GameObject("Terminal").AddComponent<TerminalView>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "nullroute";
            PlayerSettings.productName = "nullroute";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("[nullroute] Main scene ready at " + ScenePath);
        }

        [MenuItem("Nullroute/Build Windows")]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) SetupScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Windows/nullroute.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("[nullroute] Build result: " + report.summary.result + ", " + report.summary.totalSize / (1024 * 1024) + " MB");
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
