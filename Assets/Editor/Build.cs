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
            PlayerSettings.bundleVersion = "0.7.4";
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;   // the game pauses itself when the window loses focus (GameRoot.OnApplicationFocus)
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Standalone, "com.rushingtech.stratum");
            IncludeShaders("Universal Render Pipeline/Unlit", "Universal Render Pipeline/Particles/Unlit");
            // The window and taskbar icon (drawn by Tools/brand/icon.py).
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Icon.png");
            if (icon != null) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
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
            // Fog is switched on from code (the scene has none), so "automatic" stripping would drop the fog
            // variants from every shader and builds would draw no fog. Keep linear fog only.
            so.FindProperty("m_FogStripping").intValue = 1;
            so.FindProperty("m_FogKeepLinear").boolValue = true;
            so.FindProperty("m_FogKeepExp").boolValue = false;
            so.FindProperty("m_FogKeepExp2").boolValue = false;
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
            // A build can report "Succeeded" with errors and no player written (a platform module installed while
            // the Editor was open, for one), so check the file too.
            bool ok = sum.result == BuildResult.Succeeded && sum.totalErrors == 0 && File.Exists(path);
            if (ok)
            {
                // The font licence has to travel with the game.
                File.Copy("CREDITS.md", Path.Combine(dir, "CREDITS.md"), true);
                Directory.CreateDirectory(Path.Combine(dir, "licenses"));
                foreach (var f in Directory.GetFiles("licenses")) File.Copy(f, Path.Combine(dir, "licenses", Path.GetFileName(f)), true);
            }
            Debug.Log($"Build {target}: {(ok ? "OK" : "FAILED")} ({sum.result}), {sum.totalSize / (1024 * 1024)} MB, {sum.totalErrors} errors, at {path}");
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            else if (!ok) throw new System.Exception($"{target} build failed; see the Console");
        }
    }
}
