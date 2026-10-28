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
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Editor
{
    public static class GameSetupMenu
    {
        [MenuItem("SandBlocks/Setup Structured Scene Hierarchy")]
        public static void SetupStructuredSceneHierarchy()
        {
            // 1. Setup Camera (only set defaults if newly created)
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camObj = new GameObject("Main Camera");
                cam = camObj.AddComponent<Camera>();
                camObj.tag = "MainCamera";
                cam.transform.position = new Vector3(0f, -2.8f, -14.5f);
                cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
                cam.orthographic = true;
                cam.orthographicSize = 8.5f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.98f, 0.65f, 0.78f, 1f);
            }

            // 2. Setup Directional Light
            var light = Object.FindAnyObjectByType<Light>();
            if (light == null)
            {
                var lightObj = new GameObject("Directional Light");
                light = lightObj.AddComponent<Light>();
                light.type = LightType.Directional;
                light.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                light.intensity = 1.25f;
                light.color = Color.white;
                light.shadows = LightShadows.Soft;
            }

            // Remove legacy monolithic object if it exists
            var oldMonolith = GameObject.Find("[GameController]");
            if (oldMonolith != null)
            {
                Object.DestroyImmediate(oldMonolith);
            }

            // Find user prefabs
            var characterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sand Block Puzzle/Prefabs/Character/CharacterModel.prefab");
            var busPrefabObj = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sand Block Puzzle/Prefabs/Bus/BusModel.prefab");
            BusVehicle busVehiclePrefab = busPrefabObj != null ? busPrefabObj.GetComponent<BusVehicle>() : null;

            // Ensure Level asset and Palette exist
            var levelData = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Sand Block Puzzle/ScriptableObjects/Levels/Level_01.asset");
            if (levelData == null)
            {
                levelData = CreateSampleLevelAssetInternal();
            }

            var palette = AssetDatabase.LoadAssetAtPath<CrowdColorPalette>("Assets/Sand Block Puzzle/Settings/CrowdColorPalette.asset");
            if (palette == null)
            {
                palette = CreatePaletteAssetInternal();
            }

            // 3. Container System Hierarchy (Generic Container Boundary)
            var containerRoot = GetOrCreateRoot("[Container System]");
            var boundaryObj = GetOrCreateChild(containerRoot, "Container Boundary");
            var boundary = GetOrAddComponent<ContainerBoundary>(boundaryObj);
            boundary.BuildVisualContainer();

            var simObj = GetOrCreateChild(containerRoot, "Crowd Simulation");
            var simulation = GetOrAddComponent<PBDCrowdSimulation>(simObj);

            var rendObj = GetOrCreateChild(containerRoot, "Crowd Renderer");
            var crowdRenderer = GetOrAddComponent<InstancedCrowdRenderer>(rendObj);
            if (characterPrefab != null)
            {
                var soRend = new SerializedObject(crowdRenderer);
                soRend.FindProperty("characterPrefab").objectReferenceValue = characterPrefab;
                soRend.FindProperty("prefabScale").floatValue = 0.08f;
                soRend.FindProperty("characterRotationEuler").vector3Value = Vector3.zero;
                soRend.ApplyModifiedProperties();
            }

            // 4. Puzzle System Hierarchy
            var puzzleRoot = GetOrCreateRoot("[Puzzle System]");
            var gridObj = GetOrCreateChild(puzzleRoot, "Puzzle Grid");
            var puzzleGrid = GetOrAddComponent<PuzzleGrid>(gridObj);
            puzzleGrid.BuildVisualGrid();

            var inputObj = GetOrCreateChild(puzzleRoot, "Drag Input Handler");
            var dragHandler = GetOrAddComponent<VehicleDragHandler>(inputObj);

            // 5. Gameplay Bridge Hierarchy
            var bridgeRoot = GetOrCreateRoot("[Gameplay Bridge]");
            var transferObj = GetOrCreateChild(bridgeRoot, "Crowd Transfer Controller");
            var transferCtrl = GetOrAddComponent<CrowdTransferController>(transferObj);
            if (characterPrefab != null)
            {
                var soTransfer = new SerializedObject(transferCtrl);
                soTransfer.FindProperty("commuterStreamPrefab").objectReferenceValue = characterPrefab;
                soTransfer.FindProperty("palette").objectReferenceValue = palette;
                soTransfer.FindProperty("commuterScale").floatValue = 0.08f;
                soTransfer.ApplyModifiedProperties();
            }

            var managerObj = GetOrCreateChild(bridgeRoot, "Game Manager");
            var gameManager = GetOrAddComponent<GameManager>(managerObj);

            var soGM = new SerializedObject(gameManager);
            soGM.FindProperty("currentLevel").objectReferenceValue = levelData;
            soGM.FindProperty("palette").objectReferenceValue = palette;
            if (busVehiclePrefab != null)
            {
                soGM.FindProperty("busPrefab").objectReferenceValue = busVehiclePrefab;
            }
            soGM.FindProperty("particleRadius").floatValue = 0.28f;
            soGM.FindProperty("autoPositionCamera").boolValue = false;
            soGM.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("<color=green>[SandBlocks]</color> Scene structured, generic container created, bus rotations & character tilt applied!");
        }

        [MenuItem("SandBlocks/Create New Level Asset")]
        public static void CreateNewLevelAsset()
        {
            var level = CreateSampleLevelAssetInternal();
            Selection.activeObject = level;
        }

        private static LevelData CreateSampleLevelAssetInternal()
        {
            string dir = "Assets/Sand Block Puzzle/ScriptableObjects/Levels";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets/Sand Block Puzzle/ScriptableObjects", "Levels");
            }

            string path = "Assets/Sand Block Puzzle/ScriptableObjects/Levels/Level_01.asset";
            var existing = AssetDatabase.LoadAssetAtPath<LevelData>(path);
            if (existing != null) return existing;

            var level = ScriptableObject.CreateInstance<LevelData>();
            AssetDatabase.CreateAsset(level, path);
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>[SandBlocks]</color> Level asset created at: " + path);
            return level;
        }

        private static CrowdColorPalette CreatePaletteAssetInternal()
        {
            string dir = "Assets/Sand Block Puzzle/Settings";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets/Sand Block Puzzle", "Settings");
            }

            string path = "Assets/Sand Block Puzzle/Settings/CrowdColorPalette.asset";
            var existing = AssetDatabase.LoadAssetAtPath<CrowdColorPalette>(path);
            if (existing != null) return existing;

            var palette = ScriptableObject.CreateInstance<CrowdColorPalette>();
            AssetDatabase.CreateAsset(palette, path);
            AssetDatabase.SaveAssets();
            Debug.Log("<color=green>[SandBlocks]</color> Color Palette created at: " + path);
            return palette;
        }

        private static GameObject GetOrCreateRoot(string name)
        {
            var obj = GameObject.Find(name);
            if (obj == null)
            {
                obj = new GameObject(name);
                obj.transform.position = Vector3.zero;
            }
            return obj;
        }

        private static GameObject GetOrCreateChild(GameObject parent, string childName)
        {
            var child = parent.transform.Find(childName);
            if (child != null) return child.gameObject;

            var newChild = new GameObject(childName);
            newChild.transform.SetParent(parent.transform);
            newChild.transform.localPosition = Vector3.zero;
            return newChild;
        }

        private static T GetOrAddComponent<T>(GameObject obj) where T : Component
        {
            var comp = obj.GetComponent<T>();
            if (comp == null) comp = obj.AddComponent<T>();
            return comp;
        }
    }
}
