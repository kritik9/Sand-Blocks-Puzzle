#if UNITY_EDITOR
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SandBlocks.Editor
{
    public static class WebGLBuilder
    {
        private const string WebGLBuildPath = "Builds/WebGL";

        [MenuItem("Build/Build WebGL (Portrait Pillarbox)")]
        public static void BuildWebGLOptimized()
        {
            Debug.Log("[WebGLBuilder] Starting optimized WebGL build process...");
            ConfigureBuildSettings();
            PerformBuild();
        }

        [MenuItem("Build/Configure WebGL Settings Only")]
        public static void ConfigureBuildSettings()
        {
            // Ensure WebGL is the active build target
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            }

            // Configure CrazyGames template and Portrait phone resolution
            PlayerSettings.WebGL.template = "PROJECT:CrazyGames";
            PlayerSettings.defaultWebScreenWidth = 450;
            PlayerSettings.defaultWebScreenHeight = 956;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            // Compression: Disabled for maximum compatibility across Netlify, itch.io, and CrazyGames (prevents SyntaxError tokens)
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;

            // Ensure Forward rendering (Quality Level 0 / Mobile) so UnityToon shaders render with full color instead of black silhouettes
            QualitySettings.SetQualityLevel(0, true);

            // Input handling: 2 = Both (New Input System + Legacy browser pointer events)
            // Stored in PlayerSettings activeInputHandler
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            PlayerSettings.runInBackground = false;

            AssetDatabase.SaveAssets();
            Debug.Log("[WebGLBuilder] WebGL build settings successfully configured for Portrait Pillarbox with CrazyGames template.");
        }

        private static void PerformBuild()
        {
            string buildPath = Path.GetFullPath(WebGLBuildPath);
            if (!Directory.Exists(buildPath))
            {
                Directory.CreateDirectory(buildPath);
            }

            string[] scenes = GetScenesToBuild();
            if (scenes == null || scenes.Length == 0)
            {
                Debug.LogError("[WebGLBuilder] No enabled scenes found in Build Settings!");
                EditorUtility.DisplayDialog("Build Error", "No enabled scenes found in Build Settings. Please add your gameplay scene.", "OK");
                return;
            }

            BuildPlayerOptions buildOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = buildPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            Debug.Log($"[WebGLBuilder] Building {scenes.Length} scene(s) to: {buildPath}");
            BuildReport report = BuildPipeline.BuildPlayer(buildOptions);

            if (report.summary.result == BuildResult.Succeeded)
            {
                string sizeStr = FormatBytes(report.summary.totalSize);
                Debug.Log($"[WebGLBuilder] WebGL build succeeded! Build size: {sizeStr}");
                Debug.Log($"[WebGLBuilder] Build location: {buildPath}");
                EditorUtility.RevealInFinder(buildPath);
                EditorUtility.DisplayDialog("Build Successful", 
                    $"WebGL build completed successfully!\n\nBuild size: {sizeStr}\nLocation: {buildPath}", 
                    "OK");
            }
            else
            {
                Debug.LogError($"[WebGLBuilder] WebGL build failed with result: {report.summary.result}");
                EditorUtility.DisplayDialog("Build Failed", 
                    "WebGL build failed. Check the Console window for details.", 
                    "OK");
            }
        }

        private static string[] GetScenesToBuild()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }
            return scenes.ToArray();
        }

        private static string FormatBytes(ulong bytes)
        {
            const ulong KB = 1024UL;
            const ulong MB = KB * 1024UL;
            const ulong GB = MB * 1024UL;
            if (bytes >= GB) return $"{(double)bytes / GB:F2} GB";
            if (bytes >= MB) return $"{(double)bytes / MB:F2} MB";
            if (bytes >= KB) return $"{(double)bytes / KB:F2} KB";
            return $"{bytes} bytes";
        }
    }
}
#endif
