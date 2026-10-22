using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;
using SandBlocks.Core.Rendering;
using SandBlocks.Core.Simulation;
using SandBlocks.Gameplay;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Input;

namespace SandBlocks.Editor
{
    public static class GameSetupMenu
    {
        [MenuItem("SandBlocks/Setup Structured Scene Hierarchy")]
        public static void SetupStructuredSceneHierarchy()
        {
            // 1. Setup Camera
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camObj = new GameObject("Main Camera");
                cam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
            }
            cam.transform.position = new Vector3(0f, -2.5f, -14f);
            cam.transform.rotation = Quaternion.identity;
            cam.orthographic = true;
            cam.orthographicSize = 7.5f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.98f, 0.65f, 0.78f, 1f);

            // 2. Setup Directional Light
            var light = Object.FindAnyObjectByType<Light>();
            if (light == null)
            {
                var lightObj = new GameObject("Directional Light");
                light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
            }
            light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            light.intensity = 1.2f;
            light.color = Color.white;
            light.shadows = LightShadows.Soft;

            // Remove legacy monolithic object if it exists
            var oldMonolith = GameObject.Find("[GameController]");
            if (oldMonolith != null)
            {
                Object.DestroyImmediate(oldMonolith);
            }

            // 3. Funnel System Hierarchy
            var funnelRoot = GetOrCreateRoot("[Funnel System]");
            var boundaryObj = GetOrCreateChild(funnelRoot, "Funnel Boundary");
            var boundary = GetOrAddComponent<FunnelBoundary>(boundaryObj);

            var simObj = GetOrCreateChild(funnelRoot, "Crowd Simulation");
            var simulation = GetOrAddComponent<PBDCrowdSimulation>(simObj);

            var rendObj = GetOrCreateChild(funnelRoot, "Crowd Renderer");
            var crowdRenderer = GetOrAddComponent<InstancedCrowdRenderer>(rendObj);

            // 4. Puzzle System Hierarchy
            var puzzleRoot = GetOrCreateRoot("[Puzzle System]");
            var gridObj = GetOrCreateChild(puzzleRoot, "Puzzle Grid");
            var puzzleGrid = GetOrAddComponent<PuzzleGrid>(gridObj);

            var inputObj = GetOrCreateChild(puzzleRoot, "Drag Input Handler");
            var dragHandler = GetOrAddComponent<VehicleDragHandler>(inputObj);

            // 5. Gameplay Bridge Hierarchy
            var bridgeRoot = GetOrCreateRoot("[Gameplay Bridge]");
            var transferObj = GetOrCreateChild(bridgeRoot, "Crowd Transfer Controller");
            var transferCtrl = GetOrAddComponent<CrowdTransferController>(transferObj);

            var managerObj = GetOrCreateChild(bridgeRoot, "Game Manager");
            var gameManager = GetOrAddComponent<GameManager>(managerObj);

            // Ensure Level asset exists and is linked
            var levelData = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Sand Block Puzzle/Levels/Level_01.asset");
            if (levelData == null)
            {
                levelData = CreateSampleLevelAssetInternal();
            }

            var so = new SerializedObject(gameManager);
            var levelProp = so.FindProperty("currentLevel");
            if (levelProp != null && levelProp.objectReferenceValue == null)
            {
                levelProp.objectReferenceValue = levelData;
                so.ApplyModifiedProperties();
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("<color=green>[SandBlocks]</color> Scene structured and Level_01 linked successfully!");
        }

        [MenuItem("SandBlocks/Create New Level Asset")]
        public static void CreateNewLevelAsset()
        {
            var level = CreateSampleLevelAssetInternal();
            Selection.activeObject = level;
        }

        private static LevelData CreateSampleLevelAssetInternal()
        {
            string dir = "Assets/Sand Block Puzzle/Levels";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets/Sand Block Puzzle", "Levels");
            }

            string path = "Assets/Sand Block Puzzle/Levels/Level_01.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (existing != null) return existing;

            var level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>[SandBlocks]</color> Level asset created at: " + path);
            return level;
        }

        private static GameObject GetOrCreateRoot(string name)
        {
            var obj = GameObject.Find(name);
            if (obj == null)
            {
                obj = new GameObject(name);
            }
            return obj;
        }

        private static GameObject GetOrCreateChild(GameObject parent, string name)
        {
            Transform child = parent.transform.Find(name);
            if (child != null) return child.gameObject;

            var newChild = new GameObject(name);
            newChild.transform.SetParent(parent.transform);
            newChild.transform.localPosition = Vector3.zero;
            return newChild;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T comp = target.GetComponent<T>();
            if (comp == null)
            {
                comp = target.AddComponent<T>();
            }
            return comp;
        }
    }
}
