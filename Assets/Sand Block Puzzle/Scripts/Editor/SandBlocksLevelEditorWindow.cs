using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;
using SandBlocks.Gameplay;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Editor
{
    public class SandBlocksLevelEditorWindow : EditorWindow
    {
        private LevelData selectedLevel;
        private CrowdColorPalette palette;
        private Vector2 scrollPos;

        // Toolbar Tabs
        private int selectedTab = 0;
        private readonly string[] tabLabels = new string[] { "Crowd & Container", "Buses & Parking Grid", "Balance & Scene" };

        // Crowd Painting State
        private int activeBrushColor = 0; // Default Blue
        private int brushSize = 1;
        private bool autoSyncCapacity = true;
        private bool showAddColorSection = false;
        private string newColorName = "Purple";
        private Color newColorValue = new Color(0.65f, 0.25f, 0.95f, 1f);

        // Crowd Painting & Row/Col Manipulation State
        private enum CrowdPaintTool { Brush, Eraser, DeleteRow, DeleteColumn }
        private CrowdPaintTool activePaintTool = CrowdPaintTool.Brush;
        private bool autoAdjustContainerDimensions = true;
        private bool lockSquareCells = true;
        private bool showDeleteHeaders = true;
        private int manualDeleteRowIndex = 0;
        private int manualDeleteColIndex = 0;

        // Bus Editor State
        private int selectedBusIndex = -1;

        // Image Import State
        private bool showImageImportSection = true;
        private int importGridCols = 16;
        private int importGridRows = 16;
        private float importMarginPercent = 4.5f;
        private float whiteThreshold = 0.82f;
        private bool flipVertical = false;

        // Default colors if palette asset not loaded
        private readonly Color[] fallbackColors = new Color[]
        {
            new Color(0.18f, 0.55f, 1f, 1f),    // 0: Blue
            new Color(0.2f, 0.85f, 0.35f, 1f),  // 1: Green
            new Color(1f, 0.82f, 0.1f, 1f),     // 2: Yellow
            new Color(1f, 0.35f, 0.65f, 1f),    // 3: Pink
            new Color(0.18f, 0.18f, 0.22f, 1f), // 4: Black
            new Color(0.92f, 0.92f, 0.95f, 1f), // 5: White
            new Color(1f, 0.5f, 0.1f, 1f)       // 6: Orange
        };

        private readonly string[] colorNames = new string[]
        {
            "Blue", "Green", "Yellow", "Pink", "Black", "White", "Orange"
        };

        [MenuItem("SandBlocks/Level Editor Window")]
        [MenuItem("Window/Sand Blocks Level Editor")]
        public static void OpenWindow()
        {
            var win = GetWindow<SandBlocksLevelEditorWindow>("Level Editor");
            win.minSize = new Vector2(620, 680);
            win.Show();
        }

        private void OnEnable()
        {
            wantsMouseMove = true;
            LoadPalette();
            LoadDefaultLevel();
        }

        private void LoadPalette()
        {
            if (palette == null)
            {
                palette = AssetDatabase.LoadAssetAtPath<CrowdColorPalette>("Assets/Sand Block Puzzle/Settings/CrowdColorPalette.asset");
            }
        }

        private void LoadDefaultLevel()
        {
            if (selectedLevel == null)
            {
                selectedLevel = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Sand Block Puzzle/ScriptableObjects/Levels/Level_01.asset");
            }
            if (selectedLevel != null)
            {
                selectedLevel.EnsureCustomGridInitialized();
            }
        }

        private Color GetColor(int colorId)
        {
            if (palette != null)
            {
                return palette.GetColor(colorId);
            }
            if (colorId >= 0 && colorId < fallbackColors.Length)
            {
                return fallbackColors[colorId];
            }
            return Color.white;
        }

        private string GetColorName(int colorId)
        {
            if (palette != null)
            {
                return palette.GetColorName(colorId);
            }
            if (colorId >= 0 && colorId < colorNames.Length)
            {
                return colorNames[colorId];
            }
            return $"Color {colorId}";
        }

        private void OnGUI()
        {
            DrawHeader();

            if (selectedLevel == null)
            {
                EditorGUILayout.HelpBox("Select or create a LevelData asset above to start editing.", MessageType.Info);
                return;
            }

            selectedLevel.EnsureCustomGridInitialized();

            EditorGUILayout.Space(4);
            selectedTab = GUILayout.Toolbar(selectedTab, tabLabels, GUILayout.Height(28));
            EditorGUILayout.Space(6);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            switch (selectedTab)
            {
                case 0:
                    DrawContainerAndCrowdTab();
                    break;
                case 1:
                    DrawBusesAndParkingTab();
                    break;
                case 2:
                    DrawBalanceAndSceneTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        // =========================================================================
        // HEADER & ASSET CONTROLS
        // =========================================================================

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Sand Blocks Level Editor", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("New Level", GUILayout.Width(80), GUILayout.Height(22)))
            {
                CreateNewLevel();
            }

            if (GUILayout.Button("Save Asset", GUILayout.Width(80), GUILayout.Height(22)))
            {
                SaveCurrentLevel();
            }

            if (GUILayout.Button("Apply to Scene", GUILayout.Width(95), GUILayout.Height(22)))
            {
                ApplyLevelToScene();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            selectedLevel = (LevelData)EditorGUILayout.ObjectField("Active Level:", selectedLevel, typeof(LevelData), false);
            palette = (CrowdColorPalette)EditorGUILayout.ObjectField("Color Palette:", palette, typeof(CrowdColorPalette), false);
            EditorGUILayout.EndHorizontal();

            if (selectedLevel != null)
            {
                string path = AssetDatabase.GetAssetPath(selectedLevel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Asset Path:", path, EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Select Asset in Project", GUILayout.Width(160), GUILayout.Height(18)))
                {
                    Selection.activeObject = selectedLevel;
                    EditorGUIUtility.PingObject(selectedLevel);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void CreateNewLevel()
        {
            string dir = "Assets/Sand Block Puzzle/ScriptableObjects/Levels";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets/Sand Block Puzzle/ScriptableObjects", "Levels");
            }

            // Find next available level number
            int index = 1;
            string path;
            do
            {
                path = $"{dir}/Level_{index:D2}.asset";
                index++;
            } while (AssetDatabase.LoadAssetAtPath<LevelData>(path) != null);

            var newLevel = CreateInstance<LevelData>();
            newLevel.levelName = $"Level {index - 1}";
            newLevel.patternType = LevelPatternType.CustomGrid;
            newLevel.EnsureCustomGridInitialized();
            newLevel.BakeLipsShape();

            AssetDatabase.CreateAsset(newLevel, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            selectedLevel = newLevel;
            Selection.activeObject = newLevel;
            Debug.Log($"<color=green>[SandBlocks]</color> Created new level at: {path}");
        }

        private void SaveCurrentLevel()
        {
            if (selectedLevel != null)
            {
                EditorUtility.SetDirty(selectedLevel);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"<color=green>[SandBlocks]</color> Saved level '{selectedLevel.name}' successfully!");
            }
        }

        private void ApplyLevelToScene()
        {
            if (selectedLevel == null) return;

            SaveCurrentLevel();

            // 1. Assign to GameManager
            var gm = FindAnyObjectByType<GameManager>();
            if (gm != null)
            {
                var soGM = new SerializedObject(gm);
                soGM.FindProperty("currentLevel").objectReferenceValue = selectedLevel;
                soGM.ApplyModifiedProperties();
                Debug.Log($"<color=green>[SandBlocks]</color> Assigned '{selectedLevel.name}' to GameManager in active scene.");
            }

            // 2. Update visual boundary in scene
            var cb = FindAnyObjectByType<ContainerBoundary>();
            if (cb != null)
            {
                if (selectedLevel.containerShape == ContainerShapeType.CustomPolygon && selectedLevel.customPolygonPoints != null && selectedLevel.customPolygonPoints.Count >= 3)
                {
                    cb.SetCustomPolygonPoints(selectedLevel.customPolygonPoints);
                }
                else
                {
                    cb.SetContainerShape(selectedLevel.containerShape, selectedLevel.containerWidth, selectedLevel.containerHeight, selectedLevel.containerCenter,
                        selectedLevel.leftBorderOffset, selectedLevel.rightBorderOffset, selectedLevel.topBorderOffset, selectedLevel.bottomBorderOffset);
                }
                cb.BuildVisualContainer();
                EditorUtility.SetDirty(cb);
                Debug.Log($"<color=green>[SandBlocks]</color> Updated scene ContainerBoundary to {selectedLevel.containerShape} ({selectedLevel.containerWidth}x{selectedLevel.containerHeight}).");
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        // =========================================================================
        // TAB 1: CONTAINER & CROWD CANVAS
        // =========================================================================

        private void DrawContainerAndCrowdTab()
        {
            // Container Shape & Dimensions Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Container Geometry & Crowd Capacity", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            selectedLevel.containerShape = (ContainerShapeType)EditorGUILayout.EnumPopup("Shape Type:", selectedLevel.containerShape);
            selectedLevel.containerWidth = EditorGUILayout.Slider("Width:", selectedLevel.containerWidth, 2.5f, 10f);
            selectedLevel.containerHeight = EditorGUILayout.Slider("Height:", selectedLevel.containerHeight, 2.5f, 12f);
            selectedLevel.containerCenter = EditorGUILayout.Vector2Field("Center Offset:", selectedLevel.containerCenter);
            selectedLevel.particleRadius = EditorGUILayout.Slider("Particle Radius:", selectedLevel.particleRadius, 0.15f, 0.45f);

            int calculatedCap = selectedLevel.CalculateFixedCrowdCapacity();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox($"Container size ({selectedLevel.containerWidth:F1} x {selectedLevel.containerHeight:F1}) has fixed capacity of ~{calculatedCap} people.", MessageType.Info);
            
            EditorGUILayout.BeginVertical(GUILayout.Width(140));
            autoSyncCapacity = EditorGUILayout.ToggleLeft("Auto-Sync Cap", autoSyncCapacity);
            if (GUILayout.Button("Set Max Cap"))
            {
                selectedLevel.maxParticles = calculatedCap;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (autoSyncCapacity)
            {
                selectedLevel.maxParticles = calculatedCap;
            }

            selectedLevel.maxParticles = EditorGUILayout.IntField("Max Particles (Spawned):", selectedLevel.maxParticles);

            EditorGUILayout.Space(2);
            GUILayout.Label("Independent Border Offsets:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            selectedLevel.leftBorderOffset = EditorGUILayout.FloatField("Left Offset:", selectedLevel.leftBorderOffset);
            selectedLevel.rightBorderOffset = EditorGUILayout.FloatField("Right Offset:", selectedLevel.rightBorderOffset);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            selectedLevel.topBorderOffset = EditorGUILayout.FloatField("Top Offset:", selectedLevel.topBorderOffset);
            selectedLevel.bottomBorderOffset = EditorGUILayout.FloatField("Bottom Offset:", selectedLevel.bottomBorderOffset);
            EditorGUILayout.EndHorizontal();

            if (selectedLevel.containerShape == ContainerShapeType.CustomPolygon)
            {
                selectedLevel.EnsureDefaultPolygonPoints();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label($"Custom Polygon Vertices: {selectedLevel.customPolygonPoints.Count} points", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Select 'ContainerBoundary' in the Hierarchy to drag, add (+ button on edges), and delete (Shift+Click) points in the Scene view.", MessageType.Info);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Select Container In Scene"))
                {
                    var cb = FindAnyObjectByType<ContainerBoundary>();
                    if (cb != null) Selection.activeGameObject = cb.gameObject;
                }
                if (GUILayout.Button("Reset Box"))
                {
                    Undo.RecordObject(selectedLevel, "Reset Polygon Box");
                    selectedLevel.customPolygonPoints.Clear();
                    selectedLevel.EnsureDefaultPolygonPoints();
                    EditorUtility.SetDirty(selectedLevel);
                }
                if (GUILayout.Button("Reset Hexagon"))
                {
                    Undo.RecordObject(selectedLevel, "Reset Polygon Hexagon");
                    selectedLevel.customPolygonPoints.Clear();
                    float rad = Mathf.Min(selectedLevel.containerWidth, selectedLevel.containerHeight) * 0.5f;
                    Vector2 c = selectedLevel.containerCenter;
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * 60f * Mathf.Deg2Rad;
                        selectedLevel.customPolygonPoints.Add(new Vector2(c.x + rad * Mathf.Cos(ang), c.y + rad * Mathf.Sin(ang)));
                    }
                    EditorUtility.SetDirty(selectedLevel);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Pull Shape From Scene Container"))
            {
                var cb = FindAnyObjectByType<ContainerBoundary>();
                if (cb != null)
                {
                    Undo.RecordObject(selectedLevel, "Pull Container From Scene");
                    selectedLevel.containerShape = cb.ShapeType;
                    selectedLevel.containerWidth = cb.Width;
                    selectedLevel.containerHeight = cb.Height;
                    selectedLevel.containerCenter = cb.ContainerCenter;
                    selectedLevel.leftBorderOffset = cb.LeftBorderOffset;
                    selectedLevel.rightBorderOffset = cb.RightBorderOffset;
                    selectedLevel.topBorderOffset = cb.TopBorderOffset;
                    selectedLevel.bottomBorderOffset = cb.BottomBorderOffset;
                    if (cb.ShapeType == ContainerShapeType.CustomPolygon && cb.CustomPolygonPoints != null && cb.CustomPolygonPoints.Count >= 3)
                    {
                        selectedLevel.customPolygonPoints = new List<Vector2>(cb.CustomPolygonPoints);
                    }
                    EditorUtility.SetDirty(selectedLevel);
                    Debug.Log($"<color=green>[SandBlocks]</color> Pulled shape from Scene: {cb.Width}x{cb.Height}, Center {cb.ContainerCenter}");
                }
            }
            if (GUILayout.Button("Push Shape To Scene Container"))
            {
                var cb = FindAnyObjectByType<ContainerBoundary>();
                if (cb != null)
                {
                    if (selectedLevel.containerShape == ContainerShapeType.CustomPolygon && selectedLevel.customPolygonPoints != null && selectedLevel.customPolygonPoints.Count >= 3)
                    {
                        cb.SetCustomPolygonPoints(selectedLevel.customPolygonPoints);
                    }
                    else
                    {
                        cb.SetContainerShape(selectedLevel.containerShape, selectedLevel.containerWidth, selectedLevel.containerHeight, selectedLevel.containerCenter,
                            selectedLevel.leftBorderOffset, selectedLevel.rightBorderOffset, selectedLevel.topBorderOffset, selectedLevel.bottomBorderOffset);
                    }
                    cb.BuildVisualContainer();
                    EditorUtility.SetDirty(cb);
                    Debug.Log($"<color=green>[SandBlocks]</color> Pushed shape to Scene: {selectedLevel.containerWidth}x{selectedLevel.containerHeight}");
                }
            }
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(selectedLevel);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Crowd Painting Matrix Section
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("2. Visual Crowd Color Painting", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            selectedLevel.patternType = (LevelPatternType)EditorGUILayout.EnumPopup("Mode:", selectedLevel.patternType, GUILayout.Width(150));
            EditorGUILayout.EndHorizontal();

            if (selectedLevel.patternType != LevelPatternType.CustomGrid)
            {
                EditorGUILayout.HelpBox($"Currently using '{selectedLevel.patternType}' procedural generator. Switch to 'CustomGrid' mode to visually paint custom shapes or pixel art.", MessageType.Warning);
                if (GUILayout.Button("Switch to CustomGrid (Visual Painting)"))
                {
                    selectedLevel.patternType = LevelPatternType.CustomGrid;
                    selectedLevel.EnsureCustomGridInitialized();
                    EditorUtility.SetDirty(selectedLevel);
                }
            }

            // Grid Resolution Controls
            EditorGUILayout.BeginHorizontal();
            int newCols = EditorGUILayout.IntSlider("Columns (Width):", selectedLevel.gridColumns, 4, 32);
            int newRows = EditorGUILayout.IntSlider("Rows (Height):", selectedLevel.gridRows, 4, 32);
            if (newCols != selectedLevel.gridColumns || newRows != selectedLevel.gridRows)
            {
                Undo.RecordObject(selectedLevel, "Resize Crowd Grid");
                selectedLevel.ResizeGrid(newCols, newRows, autoAdjustContainerDimensions, false);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // Color Palette Brush Swatches
            DrawPaletteBrushBar();

            EditorGUILayout.Space(4);

            // Quick Canvas Actions
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill Active Color"))
            {
                Undo.RecordObject(selectedLevel, "Fill Crowd Color");
                selectedLevel.FillAll(activeBrushColor);
                EditorUtility.SetDirty(selectedLevel);
            }
            if (GUILayout.Button("Clear (White)"))
            {
                Undo.RecordObject(selectedLevel, "Clear Crowd");
                selectedLevel.FillAll(5); // 5 = White
                EditorUtility.SetDirty(selectedLevel);
            }
            if (GUILayout.Button("Preset: Lips Shape"))
            {
                Undo.RecordObject(selectedLevel, "Bake Lips");
                selectedLevel.BakeLipsShape();
                EditorUtility.SetDirty(selectedLevel);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            DrawImageImportSection();

            EditorGUILayout.Space(6);

            // Row & Column Controls & Active Painting Tools
            DrawCrowdRowColControls();

            EditorGUILayout.Space(6);

            // Interactive Painting Canvas
            DrawInteractiveCrowdCanvas();

            EditorGUILayout.Space(4);

            // Color Distribution Statistics
            DrawColorDistributionStats();

            EditorGUILayout.EndVertical();
        }

        private void DrawImageImportSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            showImageImportSection = EditorGUILayout.Foldout(showImageImportSection, "3. Import Grid Image / Pixel Art (Auto-Map Image)", true, EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Auto-Balance Buses", EditorStyles.miniButton, GUILayout.Width(130)))
            {
                AutoBalanceBusCapacities();
            }
            EditorGUILayout.EndHorizontal();

            if (showImageImportSection)
            {
                EditorGUILayout.HelpBox("Pass any grid image or pixel art chart (PNG/JPG). The editor automatically detects cells, ignores grid lines, matches each cell's color to your palette, and converts it into the level's crowd grid!", MessageType.None);

                EditorGUILayout.BeginHorizontal();
                selectedLevel.colorMapTexture = (Texture2D)EditorGUILayout.ObjectField("Grid Image Texture:", selectedLevel.colorMapTexture, typeof(Texture2D), false);
                if (GUILayout.Button("Pick File...", GUILayout.Width(80)))
                {
                    string path = EditorUtility.OpenFilePanel("Select Grid / Pixel Art Image", "", "png,jpg,jpeg");
                    if (!string.IsNullOrEmpty(path))
                    {
                        if (path.StartsWith(Application.dataPath))
                        {
                            string rel = "Assets" + path.Substring(Application.dataPath.Length);
                            selectedLevel.colorMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(rel);
                        }
                        else
                        {
                            byte[] bytes = System.IO.File.ReadAllBytes(path);
                            Texture2D loaded = new Texture2D(2, 2);
                            if (loaded.LoadImage(bytes))
                            {
                                selectedLevel.colorMapTexture = loaded;
                            }
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                importGridCols = EditorGUILayout.IntSlider("Image Grid Columns:", importGridCols, 4, 32);
                importGridRows = EditorGUILayout.IntSlider("Image Grid Rows:", importGridRows, 4, 32);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                importMarginPercent = EditorGUILayout.Slider("Outer Border Trim %:", importMarginPercent, 0f, 20f);
                whiteThreshold = EditorGUILayout.Slider("Background Brightness:", whiteThreshold, 0.7f, 0.98f);
                EditorGUILayout.EndHorizontal();

                flipVertical = EditorGUILayout.Toggle("Flip Y (Invert Rows):", flipVertical);

                EditorGUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.2f, 0.85f, 0.35f, 1f);
                if (GUILayout.Button("Auto-Convert Image to Crowd Grid", GUILayout.Height(30)))
                {
                    ImportImageToCrowdGrid();
                }
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("Reset to Lips", GUILayout.Width(100), GUILayout.Height(30)))
                {
                    Undo.RecordObject(selectedLevel, "Bake Lips");
                    selectedLevel.BakeLipsShape();
                    EditorUtility.SetDirty(selectedLevel);
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void ImportImageToCrowdGrid()
        {
            if (selectedLevel == null)
            {
                EditorUtility.DisplayDialog("Error", "No active Level selected.", "OK");
                return;
            }

            if (selectedLevel.colorMapTexture == null)
            {
                EditorUtility.DisplayDialog("Missing Image", "Please assign a Grid Image Texture first.", "OK");
                return;
            }

            Texture2D source = selectedLevel.colorMapTexture;
            Texture2D readable = null;
            bool createdTemp = false;

            try
            {
                if (source.isReadable)
                {
                    readable = source;
                }
                else
                {
                    RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(source, rt);
                    RenderTexture prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                    readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                    readable.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    createdTemp = true;
                }

                Undo.RecordObject(selectedLevel, "Import Grid Image");

                selectedLevel.ResizeGrid(importGridCols, importGridRows);

                float marginU = importMarginPercent / 100f;
                float marginV = importMarginPercent / 100f;
                float u0 = marginU;
                float u1 = 1f - marginU;
                float v0 = marginV;
                float v1 = 1f - marginV;

                float cellU = (u1 - u0) / importGridCols;
                float cellV = (v1 - v0) / importGridRows;

                for (int r = 0; r < importGridRows; r++)
                {
                    int sampleR = flipVertical ? (importGridRows - 1 - r) : r;
                    float v = v0 + (sampleR + 0.5f) * cellV;

                    for (int c = 0; c < importGridCols; c++)
                    {
                        float u = u0 + (c + 0.5f) * cellU;

                        // Sample a center cluster around cell center to eliminate grid line interference
                        float du = cellU * 0.15f;
                        float dv = cellV * 0.15f;
                        Color s0 = readable.GetPixelBilinear(u, v);
                        Color s1 = readable.GetPixelBilinear(u - du, v - dv);
                        Color s2 = readable.GetPixelBilinear(u + du, v + dv);
                        Color avg = (s0 * 2f + s1 + s2) * 0.25f;

                        int colorId = ClassifyColorToPalette(avg);
                        selectedLevel.SetPixel(c, r, colorId);
                    }
                }

                selectedLevel.patternType = LevelPatternType.CustomGrid;
                EditorUtility.SetDirty(selectedLevel);
                Debug.Log($"<color=green>[SandBlocks]</color> Successfully converted image into {importGridCols}x{importGridRows} crowd grid!");
            }
            finally
            {
                if (createdTemp && readable != null)
                {
                    DestroyImmediate(readable);
                }
            }
        }

        private int ClassifyColorToPalette(Color c)
        {
            // If near white or light gray / background with low saturation, treat as background (ID 5)
            float maxC = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float minC = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            float delta = maxC - minC;

            if (maxC >= whiteThreshold && delta < 0.12f)
            {
                return 5; // White Background
            }

            // Match against palette entries
            if (palette != null && palette.Entries != null && palette.Entries.Length > 0)
            {
                int bestId = 5;
                float bestDist = float.MaxValue;

                for (int i = 0; i < palette.Entries.Length; i++)
                {
                    var entry = palette.Entries[i];
                    if (entry.id == 5) continue; // Skip white background in color matching

                    Color ec = entry.color;
                    // Weighted RGB Euclidean distance (perception weighted)
                    float dr = (c.r - ec.r) * 0.3f;
                    float dg = (c.g - ec.g) * 0.59f;
                    float db = (c.b - ec.b) * 0.11f;
                    float dist = dr * dr + dg * dg + db * db;

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestId = entry.id;
                    }
                }
                return bestId;
            }

            // Fallback matching
            if (c.r > 0.75f && c.g < 0.35f && c.b < 0.35f) return 7; // Red
            if (c.r > 0.8f && c.g < 0.6f && c.b > 0.5f) return 3;    // Pink
            if (c.r > 0.8f && c.g > 0.7f && c.b < 0.25f) return 2;   // Yellow
            if (c.r < 0.3f && c.g < 0.3f && c.b < 0.35f) return 4;   // Black
            if (c.b > c.r && c.b > c.g) return 0;                    // Blue
            if (c.g > c.r && c.g > c.b) return 1;                    // Green
            if (c.r > 0.8f && c.g > 0.4f) return 6;                  // Orange

            return 5;
        }

        private void DrawPaletteBrushBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Header row: Label, Brush Size, New Color toggle, Edit Palette asset button
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Crowd Palette Brushes:", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            // Brush Size
            GUILayout.Label("Size:", GUILayout.Width(35));
            if (GUILayout.Toggle(brushSize == 1, "1x1", EditorStyles.miniButtonLeft, GUILayout.Width(35))) brushSize = 1;
            if (GUILayout.Toggle(brushSize == 2, "2x2", EditorStyles.miniButtonRight, GUILayout.Width(35))) brushSize = 2;

            EditorGUILayout.Space(6);

            if (GUILayout.Button(showAddColorSection ? "▲ Close" : "+ New Color", EditorStyles.miniButton, GUILayout.Width(82)))
            {
                showAddColorSection = !showAddColorSection;
            }

            if (GUILayout.Button("Edit Palette ↗", EditorStyles.miniButton, GUILayout.Width(92)))
            {
                if (palette == null) LoadPalette();
                if (palette != null)
                {
                    Selection.activeObject = palette;
                    EditorGUIUtility.PingObject(palette);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Inline Add Color Drawer
            if (showAddColorSection)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.BeginVertical("box");
                GUILayout.Label("Add New Color to Crowd Palette", EditorStyles.miniBoldLabel);
                EditorGUILayout.BeginHorizontal();
                newColorName = EditorGUILayout.TextField("Name:", newColorName, GUILayout.Width(180));
                newColorValue = EditorGUILayout.ColorField(GUIContent.none, newColorValue, true, false, false, GUILayout.Width(50));
                if (GUILayout.Button("Add to Palette", GUILayout.Width(110)))
                {
                    if (palette == null) LoadPalette();
                    if (palette != null && !string.IsNullOrWhiteSpace(newColorName))
                    {
                        Undo.RecordObject(palette, "Add Color to Palette");
                        palette.AddColor(newColorName.Trim(), newColorValue);
                        EditorUtility.SetDirty(palette);
                        AssetDatabase.SaveAssets();
                        if (palette.Entries != null && palette.Entries.Length > 0)
                        {
                            activeBrushColor = palette.Entries[palette.Entries.Length - 1].id;
                        }
                        showAddColorSection = false;
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(4);

            // Swatches
            int totalEntries = (palette != null && palette.Entries != null) ? palette.Entries.Length : fallbackColors.Length;
            int buttonsPerRow = 6;

            for (int i = 0; i < totalEntries; i++)
            {
                if (i % buttonsPerRow == 0)
                {
                    if (i > 0) EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                int cId = (palette != null && palette.Entries != null) ? palette.Entries[i].id : i;
                string cName = (palette != null && palette.Entries != null) ? palette.Entries[i].name : GetColorName(i);
                Color col = (palette != null && palette.Entries != null) ? palette.Entries[i].color : GetColor(i);

                bool isActive = (activeBrushColor == cId);

                Color prevColor = GUI.backgroundColor;
                GUI.backgroundColor = col;

                string label = isActive ? $"✓ {cName}" : cName;
                if (GUILayout.Button(label, GUILayout.Height(26), GUILayout.MinWidth(60)))
                {
                    activeBrushColor = cId;
                }

                GUI.backgroundColor = prevColor;
            }

            if (totalEntries > 0)
            {
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void SyncContainerBoundaryToScene()
        {
            var cb = FindAnyObjectByType<ContainerBoundary>();
            if (cb != null)
            {
                if (selectedLevel.containerShape == ContainerShapeType.CustomPolygon && selectedLevel.customPolygonPoints != null && selectedLevel.customPolygonPoints.Count >= 3)
                {
                    cb.SetCustomPolygonPoints(selectedLevel.customPolygonPoints);
                }
                else
                {
                    cb.SetContainerShape(selectedLevel.containerShape, selectedLevel.containerWidth, selectedLevel.containerHeight, selectedLevel.containerCenter,
                        selectedLevel.leftBorderOffset, selectedLevel.rightBorderOffset, selectedLevel.topBorderOffset, selectedLevel.bottomBorderOffset);
                }
                cb.BuildVisualContainer();
                EditorUtility.SetDirty(cb);
            }
        }

        private void DrawCrowdRowColControls()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Active Paint Tool Selector
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Active Tool:", EditorStyles.boldLabel, GUILayout.Width(80));
            if (GUILayout.Toggle(activePaintTool == CrowdPaintTool.Brush, "🖌 Paint Brush", EditorStyles.miniButtonLeft)) activePaintTool = CrowdPaintTool.Brush;
            if (GUILayout.Toggle(activePaintTool == CrowdPaintTool.Eraser, "🧼 Eraser (White)", EditorStyles.miniButtonMid)) activePaintTool = CrowdPaintTool.Eraser;
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = (activePaintTool == CrowdPaintTool.DeleteRow) ? new Color(1f, 0.4f, 0.4f, 1f) : prevBg;
            if (GUILayout.Toggle(activePaintTool == CrowdPaintTool.DeleteRow, "🗑 Delete Row Tool", EditorStyles.miniButtonMid)) activePaintTool = CrowdPaintTool.DeleteRow;
            GUI.backgroundColor = (activePaintTool == CrowdPaintTool.DeleteColumn) ? new Color(1f, 0.4f, 0.4f, 1f) : prevBg;
            if (GUILayout.Toggle(activePaintTool == CrowdPaintTool.DeleteColumn, "🗑 Delete Col Tool", EditorStyles.miniButtonRight)) activePaintTool = CrowdPaintTool.DeleteColumn;
            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();

            // Sizing Behavior Options
            EditorGUILayout.BeginHorizontal();
            autoAdjustContainerDimensions = EditorGUILayout.ToggleLeft("Auto-Adjust Container Size (Keep Cell Shape & Size)", autoAdjustContainerDimensions);
            lockSquareCells = EditorGUILayout.ToggleLeft("Lock Square Cells (1:1)", lockSquareCells);
            showDeleteHeaders = EditorGUILayout.ToggleLeft("Header ✕ Delete Buttons", showDeleteHeaders);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // Quick Trim / Expand Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Rows Trim/Add:", EditorStyles.miniBoldLabel, GUILayout.Width(95));
            if (GUILayout.Button("- Top Row", EditorStyles.miniButtonLeft))
            {
                Undo.RecordObject(selectedLevel, "Delete Top Row");
                selectedLevel.DeleteRow(selectedLevel.gridRows - 1, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("+ Top Row", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(selectedLevel, "Add Top Row");
                selectedLevel.InsertRow(selectedLevel.gridRows, 5, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("- Bottom (Exit)", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(selectedLevel, "Delete Bottom Row");
                selectedLevel.DeleteRow(0, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("+ Bottom (Exit)", EditorStyles.miniButtonRight))
            {
                Undo.RecordObject(selectedLevel, "Add Bottom Row");
                selectedLevel.InsertRow(0, 5, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Cols Trim/Add:", EditorStyles.miniBoldLabel, GUILayout.Width(95));
            if (GUILayout.Button("- Left Col", EditorStyles.miniButtonLeft))
            {
                Undo.RecordObject(selectedLevel, "Delete Left Column");
                selectedLevel.DeleteColumn(0, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("+ Left Col", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(selectedLevel, "Add Left Column");
                selectedLevel.InsertColumn(0, 5, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("- Right Col", EditorStyles.miniButtonMid))
            {
                Undo.RecordObject(selectedLevel, "Delete Right Column");
                selectedLevel.DeleteColumn(selectedLevel.gridColumns - 1, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("+ Right Col", EditorStyles.miniButtonRight))
            {
                Undo.RecordObject(selectedLevel, "Add Right Column");
                selectedLevel.InsertColumn(selectedLevel.gridColumns, 5, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            // Specific Row/Col Index Deletion
            EditorGUILayout.BeginHorizontal();
            manualDeleteRowIndex = EditorGUILayout.IntSlider($"Delete Row (0..{selectedLevel.gridRows - 1}):", manualDeleteRowIndex, 0, Mathf.Max(0, selectedLevel.gridRows - 1));
            if (GUILayout.Button("Delete Row", GUILayout.Width(80)))
            {
                Undo.RecordObject(selectedLevel, "Delete Row");
                selectedLevel.DeleteRow(manualDeleteRowIndex, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                manualDeleteRowIndex = Mathf.Clamp(manualDeleteRowIndex, 0, Mathf.Max(0, selectedLevel.gridRows - 1));
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            manualDeleteColIndex = EditorGUILayout.IntSlider($"Delete Col (0..{selectedLevel.gridColumns - 1}):", manualDeleteColIndex, 0, Mathf.Max(0, selectedLevel.gridColumns - 1));
            if (GUILayout.Button("Delete Col", GUILayout.Width(80)))
            {
                Undo.RecordObject(selectedLevel, "Delete Column");
                selectedLevel.DeleteColumn(manualDeleteColIndex, autoAdjustContainerDimensions);
                if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                manualDeleteColIndex = Mathf.Clamp(manualDeleteColIndex, 0, Mathf.Max(0, selectedLevel.gridColumns - 1));
                EditorUtility.SetDirty(selectedLevel);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            if (activePaintTool == CrowdPaintTool.DeleteRow)
            {
                EditorGUILayout.HelpBox("Delete Row Tool Active: Click on any row in the canvas (or the left ✕ header) to delete it. Remaining cells stay 100% untouched.", MessageType.Warning);
            }
            else if (activePaintTool == CrowdPaintTool.DeleteColumn)
            {
                EditorGUILayout.HelpBox("Delete Column Tool Active: Click on any column in the canvas (or top ✕ header) to delete it. Remaining cells stay 100% untouched.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInteractiveCrowdCanvas()
        {
            int cols = selectedLevel.gridColumns;
            int rows = selectedLevel.gridRows;

            float cellW, cellH, canvasWidth, canvasHeight;
            float rowHeaderW = showDeleteHeaders ? 24f : 0f;
            float colHeaderH = showDeleteHeaders ? 18f : 0f;

            if (lockSquareCells)
            {
                float availW = Mathf.Clamp(EditorGUIUtility.currentViewWidth - rowHeaderW - 70f, 180f, 440f);
                float maxCanvasH = 440f;
                float cellSize = Mathf.Min(availW / Mathf.Max(1, cols), maxCanvasH / Mathf.Max(1, rows));
                cellSize = Mathf.Clamp(cellSize, 8f, 26f);
                cellW = cellSize;
                cellH = cellSize;
                canvasWidth = cols * cellW;
                canvasHeight = rows * cellH;
            }
            else
            {
                float aspect = selectedLevel.containerWidth / Mathf.Max(1f, selectedLevel.containerHeight);
                canvasWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth - rowHeaderW - 70f, 200f, 440f);
                canvasHeight = Mathf.Clamp(canvasWidth / aspect, 200f, 460f);
                cellW = canvasWidth / cols;
                cellH = canvasHeight / rows;
            }

            float totalW = canvasWidth + rowHeaderW;
            float totalH = canvasHeight + colHeaderH;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Rect totalRect = GUILayoutUtility.GetRect(totalW, totalH);
            Rect canvasRect = new Rect(totalRect.x + rowHeaderW, totalRect.y + colHeaderH, canvasWidth, canvasHeight);

            Event evt = Event.current;
            Vector2 mousePos = evt.mousePosition;
            bool mouseInCanvas = canvasRect.Contains(mousePos);

            int hoveredCol = -1;
            int hoveredVisualRow = -1;
            int hoveredRow = -1;

            if (mouseInCanvas)
            {
                hoveredCol = Mathf.Clamp(Mathf.FloorToInt((mousePos.x - canvasRect.x) / cellW), 0, cols - 1);
                hoveredVisualRow = Mathf.Clamp(Mathf.FloorToInt((mousePos.y - canvasRect.y) / cellH), 0, rows - 1);
                hoveredRow = (rows - 1) - hoveredVisualRow;
                if (evt.type == EventType.MouseMove) Repaint();
            }

            // Draw Column Delete Headers (above canvas)
            if (showDeleteHeaders)
            {
                Rect cornerRect = new Rect(totalRect.x, totalRect.y, rowHeaderW, colHeaderH);
                EditorGUI.DrawRect(cornerRect, new Color(0.18f, 0.19f, 0.22f, 1f));
                var miniHStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 8,
                    normal = { textColor = Color.gray }
                };
                GUI.Label(cornerRect, "R/C", miniHStyle);

                for (int c = 0; c < cols; c++)
                {
                    Rect colHeaderRect = new Rect(canvasRect.x + c * cellW, totalRect.y, cellW, colHeaderH - 1f);
                    bool isHoveredCol = (hoveredCol == c && (activePaintTool == CrowdPaintTool.DeleteColumn || colHeaderRect.Contains(mousePos)));

                    Color headerBg = isHoveredCol ? new Color(0.9f, 0.25f, 0.25f, 1f) : new Color(0.22f, 0.24f, 0.28f, 1f);
                    EditorGUI.DrawRect(colHeaderRect, headerBg);

                    string colText = (cellW >= 18f && !isHoveredCol) ? $"{c}" : "✕";
                    var headerStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = (cellW >= 16f) ? 9 : 8,
                        normal = { textColor = isHoveredCol ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1f) }
                    };
                    GUI.Label(colHeaderRect, colText, headerStyle);

                    if (evt.type == EventType.MouseDown && colHeaderRect.Contains(mousePos) && evt.button == 0)
                    {
                        Undo.RecordObject(selectedLevel, "Delete Column");
                        selectedLevel.DeleteColumn(c, autoAdjustContainerDimensions);
                        if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                        EditorUtility.SetDirty(selectedLevel);
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            // Draw Row Delete Headers (left of canvas)
            if (showDeleteHeaders)
            {
                for (int r = 0; r < rows; r++)
                {
                    int visualRow = (rows - 1) - r;
                    Rect rowHeaderRect = new Rect(totalRect.x, canvasRect.y + visualRow * cellH, rowHeaderW - 1f, cellH);
                    bool isHoveredRow = (hoveredRow == r && (activePaintTool == CrowdPaintTool.DeleteRow || rowHeaderRect.Contains(mousePos)));

                    Color headerBg = isHoveredRow ? new Color(0.9f, 0.25f, 0.25f, 1f) : new Color(0.22f, 0.24f, 0.28f, 1f);
                    EditorGUI.DrawRect(rowHeaderRect, headerBg);

                    string rowText = isHoveredRow ? "✕" : $"{r}";
                    var headerStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 9,
                        normal = { textColor = isHoveredRow ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1f) }
                    };
                    GUI.Label(rowHeaderRect, rowText, headerStyle);

                    if (evt.type == EventType.MouseDown && rowHeaderRect.Contains(mousePos) && evt.button == 0)
                    {
                        Undo.RecordObject(selectedLevel, "Delete Row");
                        selectedLevel.DeleteRow(r, autoAdjustContainerDimensions);
                        if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                        EditorUtility.SetDirty(selectedLevel);
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            // Dark background for canvas
            EditorGUI.DrawRect(canvasRect, new Color(0.12f, 0.13f, 0.16f, 1f));

            // Draw cells (Inverted Y: row 0 is at bottom, row rows-1 is at top)
            for (int r = 0; r < rows; r++)
            {
                int visualRow = (rows - 1) - r;
                float cellY = canvasRect.y + visualRow * cellH;

                for (int c = 0; c < cols; c++)
                {
                    float cellX = canvasRect.x + c * cellW;
                    Rect cellRect = new Rect(cellX + 1f, cellY + 1f, cellW - 2f, cellH - 2f);

                    int colorId = selectedLevel.GetPixel(c, r);
                    Color cellColor = GetColor(colorId);

                    // Dim cells outside polygon if CustomPolygon
                    if (selectedLevel.containerShape == ContainerShapeType.CustomPolygon && selectedLevel.customPolygonPoints != null && selectedLevel.customPolygonPoints.Count >= 3)
                    {
                        float localX = Mathf.Lerp(selectedLevel.containerCenter.x - selectedLevel.containerWidth * 0.5f,
                                                  selectedLevel.containerCenter.x + selectedLevel.containerWidth * 0.5f,
                                                  (c + 0.5f) / cols);
                        float localY = Mathf.Lerp(selectedLevel.containerCenter.y - selectedLevel.containerHeight * 0.5f,
                                                  selectedLevel.containerCenter.y + selectedLevel.containerHeight * 0.5f,
                                                  (r + 0.5f) / rows);
                        if (!IsPointInPolygon(new Vector2(localX, localY), selectedLevel.customPolygonPoints))
                        {
                            cellColor = Color.Lerp(cellColor, new Color(0.1f, 0.1f, 0.1f, 1f), 0.75f);
                        }
                    }

                    EditorGUI.DrawRect(cellRect, cellColor);
                }
            }

            // Hover Highlights for Delete Row / Column Mode
            if (mouseInCanvas)
            {
                if (activePaintTool == CrowdPaintTool.DeleteRow && hoveredVisualRow >= 0)
                {
                    float y = canvasRect.y + hoveredVisualRow * cellH;
                    Rect highlightRect = new Rect(canvasRect.x, y, canvasWidth, cellH);
                    EditorGUI.DrawRect(highlightRect, new Color(1f, 0.2f, 0.2f, 0.45f));
                }
                else if (activePaintTool == CrowdPaintTool.DeleteColumn && hoveredCol >= 0)
                {
                    float x = canvasRect.x + hoveredCol * cellW;
                    Rect highlightRect = new Rect(x, canvasRect.y, cellW, canvasHeight);
                    EditorGUI.DrawRect(highlightRect, new Color(1f, 0.2f, 0.2f, 0.45f));
                }
            }

            // Outline around the container
            Handles.color = new Color(0.96f, 0.25f, 0.55f, 0.9f);
            Handles.DrawPolyLine(
                new Vector3(canvasRect.xMin, canvasRect.yMin),
                new Vector3(canvasRect.xMax, canvasRect.yMin),
                new Vector3(canvasRect.xMax, canvasRect.yMax),
                new Vector3(canvasRect.xMin, canvasRect.yMax),
                new Vector3(canvasRect.xMin, canvasRect.yMin)
            );

            // Container Exit indicator at bottom
            float exitW = canvasRect.width * 0.3f;
            float exitX = canvasRect.x + (canvasRect.width - exitW) * 0.5f;
            Handles.color = Color.yellow;
            Handles.DrawLine(new Vector3(exitX, canvasRect.yMax, 0), new Vector3(exitX + exitW, canvasRect.yMax, 0));

            // Mouse Interaction on Canvas
            if (mouseInCanvas)
            {
                if (activePaintTool == CrowdPaintTool.DeleteRow)
                {
                    if (evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        Undo.RecordObject(selectedLevel, "Delete Row");
                        selectedLevel.DeleteRow(hoveredRow, autoAdjustContainerDimensions);
                        if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                        EditorUtility.SetDirty(selectedLevel);
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                }
                else if (activePaintTool == CrowdPaintTool.DeleteColumn)
                {
                    if (evt.type == EventType.MouseDown && evt.button == 0)
                    {
                        Undo.RecordObject(selectedLevel, "Delete Column");
                        selectedLevel.DeleteColumn(hoveredCol, autoAdjustContainerDimensions);
                        if (autoAdjustContainerDimensions) SyncContainerBoundaryToScene();
                        EditorUtility.SetDirty(selectedLevel);
                        evt.Use();
                        GUIUtility.ExitGUI();
                    }
                }
                else // Paint or Erase
                {
                    if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) && (evt.button == 0 || evt.button == 1))
                    {
                        int colorToApply = (activePaintTool == CrowdPaintTool.Eraser || evt.button == 1) ? 5 : activeBrushColor;
                        Undo.RecordObject(selectedLevel, "Paint Pixel");
                        PaintBrushAt(hoveredCol, hoveredRow, colorToApply);
                        EditorUtility.SetDirty(selectedLevel);
                        evt.Use();
                        Repaint();
                    }
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Exit (Top of Puzzle Grid) ↓", EditorStyles.centeredGreyMiniLabel);
        }

        private void PaintBrushAt(int centerCol, int centerRow, int colorId)
        {
            int cols = selectedLevel.gridColumns;
            int rows = selectedLevel.gridRows;

            for (int dy = 0; dy < brushSize; dy++)
            {
                for (int dx = 0; dx < brushSize; dx++)
                {
                    int c = centerCol + dx;
                    int r = centerRow + dy;
                    if (c >= 0 && c < cols && r >= 0 && r < rows)
                    {
                        selectedLevel.SetPixel(c, r, colorId);
                    }
                }
            }
        }

        private void DrawColorDistributionStats()
        {
            if (selectedLevel.customCrowdColors == null || selectedLevel.customCrowdColors.Length == 0) return;

            int total = selectedLevel.customCrowdColors.Length;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Painted Crowd Distribution:", EditorStyles.miniBoldLabel, GUILayout.Width(160));

            int totalColors = (palette != null && palette.Entries != null) ? palette.Entries.Length : fallbackColors.Length;
            int countDisplayed = 0;
            for (int i = 0; i < totalColors; i++)
            {
                int cId = (palette != null && palette.Entries != null) ? palette.Entries[i].id : i;
                int count = selectedLevel.CountColorPixels(cId);
                if (count == 0) continue;

                float pct = (float)count / total * 100f;
                Color col = GetColor(cId);
                string cName = GetColorName(cId);

                Color prevCol = GUI.backgroundColor;
                GUI.backgroundColor = Color.Lerp(col, Color.white, 0.4f);
                GUILayout.Label($"{cName}: {count} ({pct:F0}%)", EditorStyles.helpBox, GUILayout.Height(18));
                GUI.backgroundColor = prevCol;

                countDisplayed++;
                if (countDisplayed % 5 == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(165);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // =========================================================================
        // TAB 2: BUSES & PARKING GRID
        // =========================================================================

        private void DrawBusesAndParkingTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            int currentCols = selectedLevel.puzzleColumns > 0 ? selectedLevel.puzzleColumns : 6;
            int currentRows = selectedLevel.puzzleRows > 0 ? selectedLevel.puzzleRows : 5;

            GUILayout.Label($"1. Puzzle Grid Visualization ({currentCols} Columns x {currentRows} Rows)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Click any bus in the grid or list below to edit its properties, rotation, length, or capacity. Overlaps are highlighted in red.", MessageType.None);

            EditorGUILayout.BeginHorizontal();
            int newPCols = EditorGUILayout.IntSlider("Grid Columns:", currentCols, 4, 16);
            int newPRows = EditorGUILayout.IntSlider("Grid Rows:", currentRows, 4, 16);
            if (newPCols != currentCols || newPRows != currentRows)
            {
                selectedLevel.puzzleColumns = newPCols;
                selectedLevel.puzzleRows = newPRows;
                EditorUtility.SetDirty(selectedLevel);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            DrawVisualPuzzleBoard();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Bus Inspector & Modification Panel
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("2. Bus Inspector & Placement", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Add New Bus", GUILayout.Width(110)))
            {
                AddNewBus();
            }
            if (GUILayout.Button("Duplicate", GUILayout.Width(80)) && selectedBusIndex >= 0 && selectedBusIndex < selectedLevel.buses.Count)
            {
                DuplicateBus(selectedBusIndex);
            }
            if (GUILayout.Button("- Delete Bus", GUILayout.Width(90)) && selectedBusIndex >= 0 && selectedBusIndex < selectedLevel.buses.Count)
            {
                DeleteBus(selectedBusIndex);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            if (selectedLevel.buses == null || selectedLevel.buses.Count == 0)
            {
                EditorGUILayout.HelpBox("No buses defined. Click '+ Add New Bus' to place a bus on the puzzle board.", MessageType.Info);
            }
            else
            {
                DrawBusPropertiesEditor();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVisualPuzzleBoard()
        {
            int gridCols = selectedLevel.puzzleColumns > 0 ? selectedLevel.puzzleColumns : 6;
            int gridRows = selectedLevel.puzzleRows > 0 ? selectedLevel.puzzleRows : 5;

            float boardWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 80f, 300f, 480f);
            float boardHeight = boardWidth * ((float)gridRows / gridCols);
            float cellW = boardWidth / gridCols;
            float cellH = boardHeight / gridRows;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Rect boardRect = GUILayoutUtility.GetRect(boardWidth, boardHeight);
            EditorGUI.DrawRect(boardRect, new Color(0.96f, 0.58f, 0.72f, 0.35f)); // Tray background

            // Map occupied cells to detect overlaps
            var occupancy = new Dictionary<Vector2Int, List<int>>();
            if (selectedLevel.buses != null)
            {
                for (int i = 0; i < selectedLevel.buses.Count; i++)
                {
                    var bus = selectedLevel.buses[i];
                    var cells = LevelData.GetOccupiedCells(bus);
                    for (int c = 0; c < cells.Count; c++)
                    {
                        var cell = cells[c];
                        if (!occupancy.ContainsKey(cell)) occupancy[cell] = new List<int>();
                        occupancy[cell].Add(i);
                    }
                }
            }

            // Draw 6x5 grid tiles
            for (int r = 0; r < gridRows; r++)
            {
                // Inverted Y: Row 4 is top line (near container nozzle), Row 0 is bottom line
                int visualRow = (gridRows - 1) - r;
                float y = boardRect.y + visualRow * cellH;

                for (int c = 0; c < gridCols; c++)
                {
                    float x = boardRect.x + c * cellW;
                    Rect tileRect = new Rect(x + 2f, y + 2f, cellW - 4f, cellH - 4f);

                    Vector2Int pos = new Vector2Int(c, r);
                    bool hasOverlap = occupancy.ContainsKey(pos) && occupancy[pos].Count > 1;

                    Color tileColor = hasOverlap ? new Color(1f, 0.3f, 0.3f, 0.8f) : new Color(0.98f, 0.94f, 0.94f, 0.85f);
                    EditorGUI.DrawRect(tileRect, tileColor);

                    if (hasOverlap)
                    {
                        var style = new GUIStyle(EditorStyles.boldLabel) { fontSize = 9, normal = { textColor = Color.red } };
                        GUI.Label(tileRect, "OVERLAP", style);
                    }
                }
            }

            // Top exit boundary indicator
            Handles.color = new Color(0.2f, 0.8f, 0.2f, 0.8f);
            Handles.DrawLine(new Vector3(boardRect.xMin, boardRect.yMin), new Vector3(boardRect.xMax, boardRect.yMin));

            // Draw each bus on top
            if (selectedLevel.buses != null)
            {
                for (int i = 0; i < selectedLevel.buses.Count; i++)
                {
                    var bus = selectedLevel.buses[i];
                    DrawBusOnGrid(i, bus, boardRect, cellW, cellH, gridRows);
                }
            }

            // Handle Click Selection on Puzzle Grid
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && boardRect.Contains(evt.mousePosition) && evt.button == 0)
            {
                int clickCol = Mathf.FloorToInt((evt.mousePosition.x - boardRect.x) / cellW);
                int visualR = Mathf.FloorToInt((evt.mousePosition.y - boardRect.y) / cellH);
                int clickRow = (gridRows - 1) - visualR;
                Vector2Int clickPos = new Vector2Int(clickCol, clickRow);

                if (occupancy.ContainsKey(clickPos) && occupancy[clickPos].Count > 0)
                {
                    selectedBusIndex = occupancy[clickPos][0];
                }
                else
                {
                    // Clicked empty cell: move selected bus root there if valid
                    if (selectedBusIndex >= 0 && selectedBusIndex < selectedLevel.buses.Count)
                    {
                        var b = selectedLevel.buses[selectedBusIndex];
                        b.gridPosition = clickPos;
                        selectedLevel.buses[selectedBusIndex] = b;
                        EditorUtility.SetDirty(selectedLevel);
                    }
                }
                evt.Use();
                Repaint();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"↑ Row {gridRows - 1} (Container Exit / Top Line) | Row 0 (Back of Puzzle) ↓", EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawBusOnGrid(int index, BusSpawnData bus, Rect boardRect, float cellW, float cellH, int gridRows)
        {
            var cells = LevelData.GetOccupiedCells(bus);
            if (cells.Count == 0) return;

            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].x < minX) minX = cells[i].x;
                if (cells[i].x > maxX) maxX = cells[i].x;
                if (cells[i].y < minY) minY = cells[i].y;
                if (cells[i].y > maxY) maxY = cells[i].y;
            }

            // Invert Y for visual rendering
            int visualTopRow = (gridRows - 1) - maxY;
            int rowSpan = (maxY - minY) + 1;
            int colSpan = (maxX - minX) + 1;

            float busX = boardRect.x + minX * cellW + 3f;
            float busY = boardRect.y + visualTopRow * cellH + 3f;
            float busW = colSpan * cellW - 6f;
            float busH = rowSpan * cellH - 6f;

            Rect busRect = new Rect(busX, busY, busW, busH);

            Color busColor = GetColor(bus.colorId);
            bool isSelected = (selectedBusIndex == index);

            // Draw bus body
            EditorGUI.DrawRect(busRect, busColor);

            // Draw border (Gold if selected, Dark Gray otherwise)
            Color borderColor = isSelected ? new Color(1f, 0.85f, 0.1f, 1f) : new Color(0.15f, 0.15f, 0.15f, 0.7f);
            float borderThick = isSelected ? 2.5f : 1.2f;

            Handles.color = borderColor;
            Handles.DrawPolyLine(
                new Vector3(busRect.xMin, busRect.yMin),
                new Vector3(busRect.xMax, busRect.yMin),
                new Vector3(busRect.xMax, busRect.yMax),
                new Vector3(busRect.xMin, busRect.yMax),
                new Vector3(busRect.xMin, busRect.yMin)
            );

            // Direction arrow text
            string dirSymbol = GetRotationArrow(bus.rotationDegrees);
            string busLabel = $"{dirSymbol} Cap:{bus.capacity}";

            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                normal = { textColor = (bus.colorId == 4 || bus.colorId == 0) ? Color.white : Color.black }
            };

            GUI.Label(busRect, busLabel, labelStyle);
        }

        private string GetRotationArrow(int rot)
        {
            switch (rot)
            {
                case 0: return "→";
                case 90: return "↑";
                case 180: return "←";
                case 270: return "↓";
                default: return "→";
            }
        }

        private void DrawBusPropertiesEditor()
        {
            if (selectedBusIndex < 0 || selectedBusIndex >= selectedLevel.buses.Count)
            {
                selectedBusIndex = 0;
            }

            // Dropdown to pick bus
            string[] busLabels = new string[selectedLevel.buses.Count];
            for (int i = 0; i < selectedLevel.buses.Count; i++)
            {
                var b = selectedLevel.buses[i];
                string cName = GetColorName(b.colorId);
                busLabels[i] = $"{i + 1}: {b.name} [{cName}] (Cap: {b.capacity}, Len: {b.length})";
            }

            selectedBusIndex = EditorGUILayout.Popup("Selected Bus:", selectedBusIndex, busLabels);

            var bus = selectedLevel.buses[selectedBusIndex];

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            bus.name = EditorGUILayout.TextField("Bus Name:", bus.name);
            EditorGUILayout.EndHorizontal();

            // Color Selection
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Bus Color (Matches Crowd):");
            int busTotalEntries = (palette != null && palette.Entries != null) ? palette.Entries.Length : fallbackColors.Length;
            int busPerRow = 6;
            for (int i = 0; i < busTotalEntries; i++)
            {
                if (i % busPerRow == 0)
                {
                    if (i > 0) EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }

                int cId = (palette != null && palette.Entries != null) ? palette.Entries[i].id : i;
                string cName = (palette != null && palette.Entries != null) ? palette.Entries[i].name : GetColorName(i);
                Color c = (palette != null && palette.Entries != null) ? palette.Entries[i].color : GetColor(i);

                Color prevC = GUI.backgroundColor;
                GUI.backgroundColor = c;
                bool isBusColor = (bus.colorId == cId);
                if (GUILayout.Button(isBusColor ? $"✓ {cName}" : cName, GUILayout.Height(22), GUILayout.MinWidth(55)))
                {
                    bus.colorId = cId;
                }
                GUI.backgroundColor = prevC;
            }
            if (busTotalEntries > 0)
            {
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            int maxGridCols = selectedLevel.puzzleColumns > 0 ? selectedLevel.puzzleColumns : 6;
            int maxGridRows = selectedLevel.puzzleRows > 0 ? selectedLevel.puzzleRows : 5;
            int maxGridX = Mathf.Max(0, maxGridCols - 1);
            int maxGridY = Mathf.Max(0, maxGridRows - 1);
            int maxAllowedLength = Mathf.Max(6, Mathf.Max(maxGridCols, maxGridRows));

            bus.capacity = EditorGUILayout.IntSlider("Capacity (Passengers):", bus.capacity, 1, 56);
            bus.length = EditorGUILayout.IntSlider("Length (Grid Cells):", bus.length, 1, maxAllowedLength);

            // Rotation Buttons
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Rotation Direction:");
            if (GUILayout.Toggle(bus.rotationDegrees == 0, "Right (0° →)", EditorStyles.miniButtonLeft)) bus.rotationDegrees = 0;
            if (GUILayout.Toggle(bus.rotationDegrees == 90, "Up (90° ↑)", EditorStyles.miniButtonMid)) bus.rotationDegrees = 90;
            if (GUILayout.Toggle(bus.rotationDegrees == 180, "Left (180° ←)", EditorStyles.miniButtonMid)) bus.rotationDegrees = 180;
            if (GUILayout.Toggle(bus.rotationDegrees == 270, "Down (270° ↓)", EditorStyles.miniButtonRight)) bus.rotationDegrees = 270;
            EditorGUILayout.EndHorizontal();

            // Grid Coordinates & Quick Nudge
            EditorGUILayout.BeginHorizontal();
            bus.gridPosition.x = EditorGUILayout.IntSlider($"Grid X (0..{maxGridX}):", bus.gridPosition.x, 0, maxGridX);
            bus.gridPosition.y = EditorGUILayout.IntSlider($"Grid Y (0..{maxGridY}):", bus.gridPosition.y, 0, maxGridY);
            EditorGUILayout.EndHorizontal();

            // Nudge D-Pad
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Nudge Bus Position:", EditorStyles.miniLabel);
            if (GUILayout.Button("◄", GUILayout.Width(28)) && bus.gridPosition.x > 0) bus.gridPosition.x--;
            if (GUILayout.Button("►", GUILayout.Width(28)) && bus.gridPosition.x < maxGridX) bus.gridPosition.x++;
            if (GUILayout.Button("▲", GUILayout.Width(28)) && bus.gridPosition.y < maxGridY) bus.gridPosition.y++;
            if (GUILayout.Button("▼", GUILayout.Width(28)) && bus.gridPosition.y > 0) bus.gridPosition.y--;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(selectedLevel, "Edit Bus");
                selectedLevel.buses[selectedBusIndex] = bus;
                EditorUtility.SetDirty(selectedLevel);
            }
        }

        private void AddNewBus()
        {
            Undo.RecordObject(selectedLevel, "Add Bus");
            if (selectedLevel.buses == null) selectedLevel.buses = new List<BusSpawnData>();

            var newBus = new BusSpawnData
            {
                name = $"Bus {selectedLevel.buses.Count + 1}",
                colorId = activeBrushColor,
                capacity = 56,
                length = 2,
                rotationDegrees = 0,
                gridPosition = new Vector2Int(0, 0)
            };

            selectedLevel.buses.Add(newBus);
            selectedBusIndex = selectedLevel.buses.Count - 1;
            EditorUtility.SetDirty(selectedLevel);
        }

        private void DuplicateBus(int index)
        {
            Undo.RecordObject(selectedLevel, "Duplicate Bus");
            var src = selectedLevel.buses[index];
            var clone = src;
            clone.name = $"{src.name} (Copy)";
            selectedLevel.buses.Add(clone);
            selectedBusIndex = selectedLevel.buses.Count - 1;
            EditorUtility.SetDirty(selectedLevel);
        }

        private void DeleteBus(int index)
        {
            Undo.RecordObject(selectedLevel, "Delete Bus");
            selectedLevel.buses.RemoveAt(index);
            if (selectedBusIndex >= selectedLevel.buses.Count)
            {
                selectedBusIndex = selectedLevel.buses.Count - 1;
            }
            EditorUtility.SetDirty(selectedLevel);
        }

        // =========================================================================
        // TAB 3: BALANCE & SCENE PREVIEW
        // =========================================================================

        private void DrawBalanceAndSceneTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Level Balance Analysis (Crowd vs Bus Seats)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Verify that bus capacities match the painted crowd colors so all passengers can be collected.", MessageType.None);

            DrawBalanceTable();

            EditorGUILayout.Space(6);

            if (GUILayout.Button("Auto-Balance Bus Capacities (Match Crowd)"))
            {
                AutoBalanceBusCapacities();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            // Scene Quick Controls
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("2. Scene Synchronization & Playtest", EditorStyles.boldLabel);

            if (GUILayout.Button("Apply Active Level to Scene GameManager", GUILayout.Height(30)))
            {
                ApplyLevelToScene();
            }

            if (GUILayout.Button("Update Scene Container Visuals Live", GUILayout.Height(26)))
            {
                var cb = FindAnyObjectByType<ContainerBoundary>();
                if (cb != null)
                {
                    if (selectedLevel.containerShape == ContainerShapeType.CustomPolygon && selectedLevel.customPolygonPoints != null && selectedLevel.customPolygonPoints.Count >= 3)
                    {
                        cb.SetCustomPolygonPoints(selectedLevel.customPolygonPoints);
                    }
                    else
                    {
                        cb.SetContainerShape(selectedLevel.containerShape, selectedLevel.containerWidth, selectedLevel.containerHeight, selectedLevel.containerCenter,
                            selectedLevel.leftBorderOffset, selectedLevel.rightBorderOffset, selectedLevel.topBorderOffset, selectedLevel.bottomBorderOffset);
                    }
                    cb.BuildVisualContainer();
                    EditorUtility.SetDirty(cb);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBalanceTable()
        {
            List<int> colorIds = new List<int>();
            if (palette != null && palette.Entries != null)
            {
                for (int i = 0; i < palette.Entries.Length; i++)
                {
                    colorIds.Add(palette.Entries[i].id);
                }
            }
            else
            {
                for (int i = 0; i < fallbackColors.Length; i++)
                {
                    colorIds.Add(i);
                }
            }

            // Also check any extra colorId in buses
            if (selectedLevel.buses != null)
            {
                for (int i = 0; i < selectedLevel.buses.Count; i++)
                {
                    int cid = selectedLevel.buses[i].colorId;
                    if (!colorIds.Contains(cid)) colorIds.Add(cid);
                }
            }

            // Tally bus seats per color
            Dictionary<int, int> busSeats = new Dictionary<int, int>();
            for (int i = 0; i < colorIds.Count; i++) busSeats[colorIds[i]] = 0;

            if (selectedLevel.buses != null)
            {
                for (int i = 0; i < selectedLevel.buses.Count; i++)
                {
                    int cId = selectedLevel.buses[i].colorId;
                    if (!busSeats.ContainsKey(cId)) busSeats[cId] = 0;
                    busSeats[cId] += selectedLevel.buses[i].capacity;
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Color", EditorStyles.boldLabel, GUILayout.Width(70));
            GUILayout.Label("Crowd Count", EditorStyles.boldLabel, GUILayout.Width(100));
            GUILayout.Label("Bus Seats", EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.Label("Status", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < colorIds.Count; i++)
            {
                int cId = colorIds[i];
                int crowdCount = selectedLevel.CountColorPixels(cId);
                int seats = busSeats.ContainsKey(cId) ? busSeats[cId] : 0;

                if (crowdCount == 0 && seats == 0) continue;

                EditorGUILayout.BeginHorizontal();
                Color c = GetColor(cId);
                Color prevC = GUI.backgroundColor;
                GUI.backgroundColor = Color.Lerp(c, Color.white, 0.3f);
                GUILayout.Label(GetColorName(cId), EditorStyles.helpBox, GUILayout.Width(70));
                GUI.backgroundColor = prevC;

                GUILayout.Label($"{crowdCount} pixels", GUILayout.Width(100));
                GUILayout.Label($"{seats} seats", GUILayout.Width(80));

                if (seats == crowdCount)
                {
                    GUILayout.Label("✓ Balanced", EditorStyles.boldLabel, GUILayout.Width(100));
                }
                else if (seats < crowdCount)
                {
                    GUILayout.Label($"⚠ Need +{crowdCount - seats}", EditorStyles.boldLabel, GUILayout.Width(100));
                }
                else
                {
                    GUILayout.Label($"+{seats - crowdCount} Extra", EditorStyles.miniLabel, GUILayout.Width(100));
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void AutoBalanceBusCapacities()
        {
            if (selectedLevel.buses == null || selectedLevel.buses.Count == 0) return;

            Undo.RecordObject(selectedLevel, "Auto-Balance Bus Seats");

            List<int> colorIds = new List<int>();
            if (palette != null && palette.Entries != null)
            {
                for (int i = 0; i < palette.Entries.Length; i++) colorIds.Add(palette.Entries[i].id);
            }
            else
            {
                for (int i = 0; i < fallbackColors.Length; i++) colorIds.Add(i);
            }

            for (int i = 0; i < colorIds.Count; i++)
            {
                int cId = colorIds[i];
                int crowdCount = selectedLevel.CountColorPixels(cId);
                if (crowdCount == 0) continue;

                // Find all buses of this color
                var matchingBuses = new List<int>();
                for (int b = 0; b < selectedLevel.buses.Count; b++)
                {
                    if (selectedLevel.buses[b].colorId == cId)
                    {
                        matchingBuses.Add(b);
                    }
                }

                if (matchingBuses.Count > 0)
                {
                    int capPerBus = Mathf.Max(1, crowdCount / matchingBuses.Count);
                    int remainder = crowdCount % matchingBuses.Count;

                    for (int m = 0; m < matchingBuses.Count; m++)
                    {
                        var bus = selectedLevel.buses[matchingBuses[m]];
                        bus.capacity = Mathf.Clamp(capPerBus + (m == 0 ? remainder : 0), 1, 56);
                        selectedLevel.buses[matchingBuses[m]] = bus;
                    }
                }
            }

            EditorUtility.SetDirty(selectedLevel);
            Debug.Log("<color=green>[SandBlocks]</color> Auto-balanced bus capacities to match crowd counts!");
        }

        private static bool IsPointInPolygon(Vector2 pt, List<Vector2> poly)
        {
            if (poly == null || poly.Count < 3) return true;
            bool inside = false;
            int j = poly.Count - 1;
            for (int i = 0; i < poly.Count; j = i++)
            {
                if (((poly[i].y > pt.y) != (poly[j].y > pt.y)) &&
                    (pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
