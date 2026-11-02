#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace SandBlocks.Editor
{
    /// <summary>
    /// Safely ensures required Android permissions (such as VIBRATE) are present in the
    /// generated Gradle AndroidManifest without overriding or breaking Unity's default
    /// launcher activity, exported flags, application name, or app icons.
    /// </summary>
    public class AndroidBuildPostprocessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 999;

        public void OnPostGenerateGradleAndroidProject(string basePath)
        {
            // Unity exports gradle project structure:
            // basePath is typically the root or unityLibrary
            string manifestPath = Path.Combine(basePath, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(basePath, "unityLibrary", "src", "main", "AndroidManifest.xml");
            }
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(basePath, "launcher", "src", "main", "AndroidManifest.xml");
            }
            if (!File.Exists(manifestPath)) return;

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
                    Debug.Log("<color=green>[AndroidBuildPostprocessor]</color> Injected 'android.permission.VIBRATE' into generated AndroidManifest.xml");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AndroidBuildPostprocessor] Error processing AndroidManifest: {ex.Message}");
            }
        }
    }
}
#endif
