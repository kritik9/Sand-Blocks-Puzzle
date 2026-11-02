#if UNITY_EDITOR && UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace SandBlocks.Editor
{
    /// <summary>
    /// Safely ensures required Android permissions (such as VIBRATE) are present in all
    /// generated Gradle AndroidManifest files without overriding or breaking Unity's default
    /// launcher activity, exported flags, application name, or app icons.
    /// </summary>
    public class AndroidBuildPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string basePath)
        {
            var manifestPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Check standard relative locations
            AddIfExists(manifestPaths, Path.Combine(basePath, "src", "main", "AndroidManifest.xml"));
            AddIfExists(manifestPaths, Path.Combine(basePath, "unityLibrary", "src", "main", "AndroidManifest.xml"));
            AddIfExists(manifestPaths, Path.Combine(basePath, "launcher", "src", "main", "AndroidManifest.xml"));

            // Check parent folder if basePath is unityLibrary
            var parent = Directory.GetParent(basePath);
            if (parent != null && parent.Exists)
            {
                AddIfExists(manifestPaths, Path.Combine(parent.FullName, "src", "main", "AndroidManifest.xml"));
                AddIfExists(manifestPaths, Path.Combine(parent.FullName, "unityLibrary", "src", "main", "AndroidManifest.xml"));
                AddIfExists(manifestPaths, Path.Combine(parent.FullName, "launcher", "src", "main", "AndroidManifest.xml"));
            }

            // Also search all AndroidManifest.xml files under basePath
            try
            {
                if (Directory.Exists(basePath))
                {
                    string[] found = Directory.GetFiles(basePath, "AndroidManifest.xml", SearchOption.AllDirectories);
                    foreach (var f in found)
                    {
                        manifestPaths.Add(f);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AndroidBuildPostprocessor] Directory search warning: {ex.Message}");
            }

            foreach (var manifestPath in manifestPaths)
            {
                InjectVibratePermission(manifestPath);
            }
        }

        private static void AddIfExists(HashSet<string> set, string path)
        {
            if (File.Exists(path))
            {
                set.Add(path);
            }
        }

        private static void InjectVibratePermission(string manifestPath)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(manifestPath);

                var manifestNode = doc.SelectSingleNode("/manifest");
                if (manifestNode == null) return;

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("android", "http://schemas.android.com/apk/res/android");

                // Ensure android.permission.VIBRATE
                var existingNode = doc.SelectSingleNode("/manifest/uses-permission[@android:name='android.permission.VIBRATE']", nsmgr);
                if (existingNode == null)
                {
                    var elem = doc.CreateElement("uses-permission");
                    elem.SetAttribute("name", "http://schemas.android.com/apk/res/android", "android.permission.VIBRATE");
                    manifestNode.AppendChild(elem);
                    doc.Save(manifestPath);
                    Debug.Log($"<color=green>[AndroidBuildPostprocessor]</color> Injected 'android.permission.VIBRATE' into: {manifestPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AndroidBuildPostprocessor] Error processing '{manifestPath}': {ex.Message}");
            }
        }
    }
}
#endif
