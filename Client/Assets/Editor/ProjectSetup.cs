using OnlineSeverGame.Client.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OnlineSeverGame.Client.EditorTools
{
    // 배치 모드에서도 부를 수 있는 설정·씬 생성·빌드 도구.
    //   Unity.exe -batchmode -projectPath Client -executeMethod OnlineSeverGame.Client.EditorTools.ProjectSetup.Setup -quit
    //   Unity.exe -batchmode -projectPath Client -executeMethod OnlineSeverGame.Client.EditorTools.ProjectSetup.BuildWindows -quit
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Scenes/Main.unity";
        public const string WindowsBuildPath = "Build/Windows/Client.exe";

        // Store release info (docs/work/store.md). The window title, Start menu tile and save location follow these.
        // CompanyName is a temporary publisher name; it only names the save folder
        // (HKCU\Software\<company>\<product>, AppData\LocalLow\<company>\<product>), so changing it later resets
        // everyone's saved name/server/language/volume again. Keep it once released.
        public const string ProductName = "Jelly Survival";
        public const string CompanyName = "JellySurvivalDev";
        public const string Version = "1.0.0";
        public const string IconPath = "Assets/Icon/AppIcon.png";

        [MenuItem("OnlineSeverGame/Setup Project And Scene")]
        public static void Setup()
        {
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;

            ApplyAppInfo();
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.forceSingleInstance = false;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cam.AddComponent<Camera>();
            camera.orthographic = true;
            camera.transform.position = new Vector3(0, 0, -10);
            new GameObject("Game").AddComponent<GameBootstrap>();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] scene saved: " + ScenePath);
        }

        /// <summary>Product name, company, version and the Windows icon. Also run at the start of every build.</summary>
        [MenuItem("OnlineSeverGame/Apply App Info (name, version, icon)")]
        public static void ApplyAppInfo()
        {
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
                Debug.LogWarning("[ProjectSetup] app icon not found: " + IconPath);
            else
                PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] app info: " + ProductName + " " + Version + " (" + CompanyName + ")");
        }

        [MenuItem("OnlineSeverGame/Build Windows")]
        public static void BuildWindows()
        {
            ApplyAppInfo();
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = WindowsBuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[ProjectSetup] build result: " + report.summary.result + " -> " + WindowsBuildPath);
            if (Application.isBatchMode && report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                EditorApplication.Exit(1);
        }
    }
}
