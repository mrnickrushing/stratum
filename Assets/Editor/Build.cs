using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Stratum.EditorTools
{
    // Project setup and desktop builds. From the command line:
    //   unity build . --target StandaloneLinux64 --execute-method Stratum.EditorTools.Build.Linux
    //   unity build . --target StandaloneWindows64 --execute-method Stratum.EditorTools.Build.Windows
    public static class Build
    {
        const string Scene = "Assets/Scenes/Main.unity";

        [MenuItem("Stratum/Set Up Project")]
        public static void Setup()
        {
            if (!File.Exists(Scene))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(s, Scene);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            PlayerSettings.companyName = "Rushing Technologies";
            PlayerSettings.productName = "Stratum";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;   // the game pauses itself when the window loses focus (GameRoot.OnApplicationFocus)
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.rushingtech.stratum");
            IncludeShaders("Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit");
            AssetDatabase.SaveAssets();
            Debug.Log("Stratum project set up");
        }

        // Shaders the game only finds by name at runtime (Shader.Find) would be left out of a build.
        static void IncludeShaders(params string[] names)
        {
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset").FirstOrDefault();
            if (gs == null) return;
            var so = new SerializedObject(gs);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            foreach (var n in names)
            {
                var sh = Shader.Find(n);
                if (sh == null) { Debug.LogWarning("shader not found: " + n); continue; }
                bool have = false;
                for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == sh) have = true;
                if (have) continue;
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = sh;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        [MenuItem("Stratum/Build Linux")]
        public static void Linux() => Make(BuildTarget.StandaloneLinux64, "Builds/Linux/Stratum.x86_64");

        [MenuItem("Stratum/Build Windows")]
        public static void Windows() => Make(BuildTarget.StandaloneWindows64, "Builds/Windows/Stratum.exe");

        static void Make(BuildTarget target, string path)
        {
            Setup();
            var dir = Path.GetDirectoryName(path);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            var opts = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            Debug.Log($"Build {target}: {sum.result}, {sum.totalSize / (1024 * 1024)} MB, {sum.totalErrors} errors, at {path}");
            if (Application.isBatchMode) EditorApplication.Exit(sum.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
