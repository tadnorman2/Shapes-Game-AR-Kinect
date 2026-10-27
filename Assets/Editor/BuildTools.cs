using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShapesKinect.EditorTools
{
    /// <summary>
    /// Builds the game as a Windows program and runs it, from the Shapes Kinect menu.
    ///
    /// With a Kinect streaming, the Unity editor can get stuck in its own USB device scanning
    /// ("Scanning for USB devices : 2000ms" over and over in the log), freezing Play mode. A built
    /// game doesn't do that scanning, so it runs normally.
    /// </summary>
    internal static class BuildTools
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const string BuildPath = "Builds/ShapesKinect/ShapesKinect.exe";

        /// <summary>
        /// Materials kept in a Resources folder so the shaders the game finds by name at run time
        /// are included in the build; Unity leaves out shaders no asset uses.
        /// </summary>
        private static readonly (string shader, string asset)[] Shaders =
        {
            ("Standard", "Assets/Resources/Shaders/Lit.mat"),
            ("Unlit/Texture", "Assets/Resources/Shaders/UnlitTexture.mat"),
            ("Sprites/Default", "Assets/Resources/Shaders/VertexColor.mat"),
        };

        [MenuItem("Shapes Kinect/Build and Run")]
        private static void BuildAndRun()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureScene();
            EnsureShaders();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = BuildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.AutoRunPlayer,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"Shapes Kinect: built {Path.GetFullPath(BuildPath)} and started it. Press Escape in the game to quit.");
            }
            else
            {
                Debug.LogError($"Shapes Kinect: the build didn't succeed ({report.summary.result}, {report.summary.totalErrors} errors). See the Console above.");
            }
        }

        /// <summary>A build needs a scene; the game builds everything itself, so an empty one does.</summary>
        private static void EnsureScene()
        {
            if (File.Exists(ScenePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static void EnsureShaders()
        {
            foreach (var (shaderName, assetPath) in Shaders)
            {
                if (File.Exists(assetPath)) continue;
                var shader = Shader.Find(shaderName);
                if (shader == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
                AssetDatabase.CreateAsset(new Material(shader), assetPath);
            }
            AssetDatabase.SaveAssets();
        }
    }
}
