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

        // Unified Grid Editor State
        private readonly string[] tabLabelsClassic = new string[] { "Crowd & Container", "Buses & Parking Grid", "Balance & Scene" };
        private readonly string[] tabLabelsUnified = new string[] { "Unified Grid & Cells", "Unified Buses & Destination", "Unified Balance & Scene" };
        private enum UnifiedTool { Walkable, Blocked, Hole, Passenger, Destination }
        private UnifiedTool activeUnifiedTool = UnifiedTool.Passenger;
        private int unifiedBrushColor = 0;
        private int unifiedPassengerCount = 10;
        private int selectedUnifiedBusIndex = -1;
        private bool unifiedWalkableAfterCollection = false;

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

            // Gameplay Approach Selector
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Gameplay Approach:", EditorStyles.boldLabel, GUILayout.Width(135));
            EditorGUI.BeginChangeCheck();
            selectedLevel.gameplayMode = (GameplayMode)EditorGUILayout.EnumPopup(selectedLevel.gameplayMode);
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(selectedLevel);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(4);
            string[] activeTabLabels = (selectedLevel.gameplayMode == GameplayMode.UnifiedGrid) ? tabLabelsUnified : tabLabelsClassic;
            selectedTab = GUILayout.Toolbar(selectedTab, activeTabLabels, GUILayout.Height(28));
            EditorGUILayout.Space(6);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            if (selectedLevel.gameplayMode == GameplayMode.UnifiedGrid)
            {
                switch (selectedTab)
                {
                    case 0:
                        DrawUnifiedGridLayoutTab();
                        break;
                    case 1:
                        DrawUnifiedBusesTab();
                        break;
                    case 2:
                        DrawUnifiedBalanceTab();
                        break;
                }
            }
            else
            {
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

            var lm = FindAnyObjectByType<LevelManager>();
            if (lm != null)
            {
                lm.SetupScreenForLevel(selectedLevel);
                Debug.Log($"<color=green>[SandBlocks]</color> Applied '{selectedLevel.name}' via LevelManager to active scene.");
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                return;
            }

            var gm = FindAnyObjectByType<GameManager>();
            if (gm != null)
            {
                var soGM = new SerializedObject(gm);
                soGM.FindProperty("currentLevel").objectReferenceValue = selectedLevel;
                soGM.ApplyModifiedProperties();
            }

            if (selectedLevel.gameplayMode == GameplayMode.UnifiedGrid)
            {
                var cb = FindAnyObjectByType<ContainerBoundary>();
                if (cb != null) cb.gameObject.SetActive(false);

                var pg = FindAnyObjectByType<PuzzleGrid>();
                if (pg != null)
                {
                    pg.CenterGridAtZero = true;
                    pg.SetGridRotation(selectedLevel.unifiedGridRotation);
                    pg.ClearBlockedCells();
                    if (selectedLevel.unifiedCells != null)
                    {
                        for (int i = 0; i < selectedLevel.unifiedCells.Count; i++)
                        {
                            var cell = selectedLevel.unifiedCells[i];
                            if (cell.cellType == UnifiedCellType.Blocked || cell.cellType == UnifiedCellType.Hole)
                            {
                                bool hideVisual = (cell.cellType == UnifiedCellType.Hole) || cell.hideVisual;
                                pg.SetCellBlocked(cell.gridPosition.x, cell.gridPosition.y, true, hideVisual: hideVisual);
                            }
                        }
                    }
                    pg.SetGridDimensions(selectedLevel.unifiedGridColumns, selectedLevel.unifiedGridRows);
                }
            }
            else
            {
                var cb = FindAnyObjectByType<ContainerBoundary>();
                if (cb != null)
                {
                    cb.gameObject.SetActive(true);
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

                var pg = FindAnyObjectByType<PuzzleGrid>();
                if (pg != null)
                {
                    pg.CenterGridAtZero = false;
                    pg.SetGridRotation(0);
                    pg.ClearBlockedCells();
                    pg.SetGridDimensions(selectedLevel.puzzleColumns, selectedLevel.puzzleRows);
                }
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
            bool isSelected = (selectedBusIndex == index || selectedUnifiedBusIndex == index);

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

        // =========================================================================
        // UNIFIED GRID SYSTEM TABS & EDITING
        // =========================================================================

        private void DrawUnifiedGridLayoutTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Unified Grid Configuration & Mode", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Unified Gameplay Flow:");
            if (GUILayout.Toggle(selectedLevel.unifiedMode == UnifiedMode.DestinationDropOff, "🏁 Destination Drop-Off", EditorStyles.miniButtonLeft))
            {
                selectedLevel.unifiedMode = UnifiedMode.DestinationDropOff;
            }
            if (GUILayout.Toggle(selectedLevel.unifiedMode == UnifiedMode.BusDriveAway, "🚀 Bus Drive-Away on Full", EditorStyles.miniButtonRight))
            {
                selectedLevel.unifiedMode = UnifiedMode.BusDriveAway;
                if (activeUnifiedTool == UnifiedTool.Destination) activeUnifiedTool = UnifiedTool.Passenger;
            }
            EditorGUILayout.EndHorizontal();

            if (selectedLevel.unifiedMode == UnifiedMode.BusDriveAway)
            {
                EditorGUILayout.HelpBox("Bus Drive-Away Mode: No destination station. Filled buses leave the grid as they are filled. Total bus capacity should match total passengers on the grid.", MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            selectedLevel.unifiedGridColumns = EditorGUILayout.IntSlider("Grid Columns:", selectedLevel.unifiedGridColumns, 4, 24);
            selectedLevel.unifiedGridRows = EditorGUILayout.IntSlider("Grid Rows:", selectedLevel.unifiedGridRows, 4, 24);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Grid Rotation:");
            if (GUILayout.Toggle(selectedLevel.unifiedGridRotation == 0, "0° (Default)", EditorStyles.miniButtonLeft)) selectedLevel.unifiedGridRotation = 0;
            if (GUILayout.Toggle(selectedLevel.unifiedGridRotation == 90, "90° (Up)", EditorStyles.miniButtonMid)) selectedLevel.unifiedGridRotation = 90;
            if (GUILayout.Toggle(selectedLevel.unifiedGridRotation == 180, "180° (Invert)", EditorStyles.miniButtonMid)) selectedLevel.unifiedGridRotation = 180;
            if (GUILayout.Toggle(selectedLevel.unifiedGridRotation == 270, "270° (Down)", EditorStyles.miniButtonRight)) selectedLevel.unifiedGridRotation = 270;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            selectedLevel.unifiedCellSize = EditorGUILayout.Slider("Cell Size:", selectedLevel.unifiedCellSize, 1.0f, 2.5f);
            EditorGUILayout.EndHorizontal();

            if (selectedLevel.unifiedMode == UnifiedMode.DestinationDropOff)
            {
                EditorGUILayout.BeginHorizontal();
                int maxDestX = Mathf.Max(0, selectedLevel.unifiedGridColumns - 1);
                int maxDestY = Mathf.Max(0, selectedLevel.unifiedGridRows - 1);
                selectedLevel.sharedDestinationPos.x = EditorGUILayout.IntSlider($"Destination X (0..{maxDestX}):", selectedLevel.sharedDestinationPos.x, 0, maxDestX);
                selectedLevel.sharedDestinationPos.y = EditorGUILayout.IntSlider($"Destination Y (0..{maxDestY}):", selectedLevel.sharedDestinationPos.y, 0, maxDestY);
                EditorGUILayout.EndHorizontal();
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(selectedLevel);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // Tools & Brush Selector
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("2. Cell Type & Passenger Painting Tools", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Active Tool:", EditorStyles.boldLabel, GUILayout.Width(80));
            if (GUILayout.Toggle(activeUnifiedTool == UnifiedTool.Walkable, "🚶 Walkable", EditorStyles.miniButtonLeft)) activeUnifiedTool = UnifiedTool.Walkable;
            if (GUILayout.Toggle(activeUnifiedTool == UnifiedTool.Blocked, "🧱 Blocked (Solid)", EditorStyles.miniButtonMid)) activeUnifiedTool = UnifiedTool.Blocked;
            if (GUILayout.Toggle(activeUnifiedTool == UnifiedTool.Hole, "🕳️ Hole (Removed)", EditorStyles.miniButtonMid)) activeUnifiedTool = UnifiedTool.Hole;
            if (GUILayout.Toggle(activeUnifiedTool == UnifiedTool.Passenger, "👥 Passenger Cell", EditorStyles.miniButtonMid)) activeUnifiedTool = UnifiedTool.Passenger;
            if (selectedLevel.unifiedMode == UnifiedMode.DestinationDropOff)
            {
                if (GUILayout.Toggle(activeUnifiedTool == UnifiedTool.Destination, "🏁 Set Destination", EditorStyles.miniButtonRight)) activeUnifiedTool = UnifiedTool.Destination;
            }
            EditorGUILayout.EndHorizontal();

            // Blocked Cell Visual Mode Settings (Always visible when Blocked or Hole tool is active)
            if (activeUnifiedTool == UnifiedTool.Blocked || activeUnifiedTool == UnifiedTool.Hole)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    activeUnifiedTool == UnifiedTool.Blocked
                        ? "🧱 Solid Blocked: Renders a raised dark 3D obstacle block on the grid. Impassable for buses."
                        : "🕳️ Hole (Removed): Visually removes this cell completely from the grid (creates custom grid outlines, cutouts, or holes). Impassable for buses.",
                    MessageType.Info);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("Blocked Cell Visual Mode:", EditorStyles.boldLabel, GUILayout.Width(160));
                bool isSolid = (activeUnifiedTool == UnifiedTool.Blocked);
                if (GUILayout.Toggle(isSolid, "🧱 Show Blocked (Solid)", EditorStyles.radioButton)) activeUnifiedTool = UnifiedTool.Blocked;
                if (GUILayout.Toggle(!isSolid, "🕳️ Remove from Grid (Hole)", EditorStyles.radioButton)) activeUnifiedTool = UnifiedTool.Hole;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Make All Blocked Cells Solid", EditorStyles.miniButtonLeft))
                {
                    Undo.RecordObject(selectedLevel, "Convert Blocked Cells to Solid");
                    for (int i = 0; i < selectedLevel.unifiedCells.Count; i++)
                    {
                        var cell = selectedLevel.unifiedCells[i];
                        if (cell.cellType == UnifiedCellType.Hole)
                        {
                            cell.cellType = UnifiedCellType.Blocked;
                            cell.hideVisual = false;
                            selectedLevel.unifiedCells[i] = cell;
                        }
                        else if (cell.cellType == UnifiedCellType.Blocked)
                        {
                            cell.hideVisual = false;
                            selectedLevel.unifiedCells[i] = cell;
                        }
                    }
                    EditorUtility.SetDirty(selectedLevel);
                }
                if (GUILayout.Button("Make All Blocked Cells Holes (Removed)", EditorStyles.miniButtonRight))
                {
                    Undo.RecordObject(selectedLevel, "Convert Blocked Cells to Holes");
                    for (int i = 0; i < selectedLevel.unifiedCells.Count; i++)
                    {
                        var cell = selectedLevel.unifiedCells[i];
                        if (cell.cellType == UnifiedCellType.Blocked)
                        {
                            cell.cellType = UnifiedCellType.Hole;
                            cell.hideVisual = true;
                            selectedLevel.unifiedCells[i] = cell;
                        }
                    }
                    EditorUtility.SetDirty(selectedLevel);
                }
                EditorGUILayout.EndHorizontal();
            }

            if (activeUnifiedTool == UnifiedTool.Passenger)
            {
                EditorGUILayout.Space(4);
                // Passenger Color & Count
                EditorGUILayout.LabelField("Passenger Color:");
                int totalEntries = (palette != null && palette.Entries != null) ? palette.Entries.Length : fallbackColors.Length;
                int perRow = 6;
                for (int i = 0; i < totalEntries; i++)
                {
                    if (i % perRow == 0)
                    {
                        if (i > 0) EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                    }
                    int cId = (palette != null && palette.Entries != null) ? palette.Entries[i].id : i;
                    string cName = (palette != null && palette.Entries != null) ? palette.Entries[i].name : GetColorName(i);
                    Color c = (palette != null && palette.Entries != null) ? palette.Entries[i].color : GetColor(i);

                    Color prevC = GUI.backgroundColor;
                    GUI.backgroundColor = c;
                    bool isSelected = (unifiedBrushColor == cId);
                    if (GUILayout.Button(isSelected ? $"✓ {cName}" : cName, GUILayout.Height(24), GUILayout.MinWidth(55)))
                    {
                        unifiedBrushColor = cId;
                    }
                    GUI.backgroundColor = prevC;
                }
                if (totalEntries > 0) EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                unifiedPassengerCount = EditorGUILayout.IntSlider("Passengers in Cell:", unifiedPassengerCount, 1, 56);
                if (GUILayout.Button("5", GUILayout.Width(30))) unifiedPassengerCount = 5;
                if (GUILayout.Button("10", GUILayout.Width(30))) unifiedPassengerCount = 10;
                if (GUILayout.Button("15", GUILayout.Width(30))) unifiedPassengerCount = 15;
                if (GUILayout.Button("20", GUILayout.Width(30))) unifiedPassengerCount = 20;
                if (GUILayout.Button("28", GUILayout.Width(30))) unifiedPassengerCount = 28;
                if (GUILayout.Button("56", GUILayout.Width(30))) unifiedPassengerCount = 56;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                unifiedWalkableAfterCollection = EditorGUILayout.ToggleLeft(" 🔓 Walkable After Collection (Becomes open path when cleared)", unifiedWalkableAfterCollection);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear All Cells to Walkable"))
            {
                if (EditorUtility.DisplayDialog("Clear Grid", "Reset all cells to walkable?", "Yes", "Cancel"))
                {
                    Undo.RecordObject(selectedLevel, "Clear Unified Cells");
                    selectedLevel.unifiedCells.Clear();
                    EditorUtility.SetDirty(selectedLevel);
                }
            }
            if (selectedLevel.unifiedMode == UnifiedMode.DestinationDropOff)
            {
                if (GUILayout.Button("Set Destination to Bottom-Left (0, 0)"))
                {
                    Undo.RecordObject(selectedLevel, "Set Destination");
                    selectedLevel.sharedDestinationPos = Vector2Int.zero;
                    EditorUtility.SetDirty(selectedLevel);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // Interactive Canvas
            DrawUnifiedGridCanvas();

            EditorGUILayout.Space(6);
            DrawConfiguredPassengerStopsList();
        }

        private void DrawConfiguredPassengerStopsList()
        {
            if (selectedLevel == null || selectedLevel.unifiedCells == null) return;

            var stops = selectedLevel.unifiedCells.FindAll(c => c.cellType == UnifiedCellType.Passenger);
            if (stops.Count == 0) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label($"3. Configured Passenger Stops ({stops.Count}) - Walkable Options", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("🔓 Set All Stops Walkable When Cleared"))
            {
                Undo.RecordObject(selectedLevel, "Set All Passenger Stops Walkable");
                for (int i = 0; i < selectedLevel.unifiedCells.Count; i++)
                {
                    if (selectedLevel.unifiedCells[i].cellType == UnifiedCellType.Passenger)
                    {
                        var cell = selectedLevel.unifiedCells[i];
                        cell.walkableAfterCollection = true;
                        selectedLevel.unifiedCells[i] = cell;
                    }
                }
                EditorUtility.SetDirty(selectedLevel);
            }
            if (GUILayout.Button("🔒 Set All Stops Stays Blocked"))
            {
                Undo.RecordObject(selectedLevel, "Set All Passenger Stops Blocked");
                for (int i = 0; i < selectedLevel.unifiedCells.Count; i++)
                {
                    if (selectedLevel.unifiedCells[i].cellType == UnifiedCellType.Passenger)
                    {
                        var cell = selectedLevel.unifiedCells[i];
                        cell.walkableAfterCollection = false;
                        selectedLevel.unifiedCells[i] = cell;
                    }
                }
                EditorUtility.SetDirty(selectedLevel);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            for (int i = 0; i < selectedLevel.unifiedCells.Count; i++)
            {
                var cell = selectedLevel.unifiedCells[i];
                if (cell.cellType != UnifiedCellType.Passenger) continue;

                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = GetColor(cell.colorId);
                GUILayout.Box("", GUILayout.Width(18), GUILayout.Height(18));
                GUI.backgroundColor = prevBg;

                GUILayout.Label($"Stop at ({cell.gridPosition.x}, {cell.gridPosition.y}) | {GetColorName(cell.colorId)} | Count: {cell.passengerCount}", GUILayout.Width(240));

                bool newWalkable = EditorGUILayout.ToggleLeft(cell.walkableAfterCollection ? "🔓 Walkable When Cleared" : "🔒 Stays Blocked", cell.walkableAfterCollection);
                if (newWalkable != cell.walkableAfterCollection)
                {
                    Undo.RecordObject(selectedLevel, "Toggle Stop Walkable");
                    cell.walkableAfterCollection = newWalkable;
                    selectedLevel.unifiedCells[i] = cell;
                    EditorUtility.SetDirty(selectedLevel);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUnifiedGridCanvas()
        {
            int cols = selectedLevel.unifiedGridColumns;
            int rows = selectedLevel.unifiedGridRows;

            float canvasWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 80f, 260f, 480f);
            float canvasHeight = canvasWidth * ((float)rows / cols);
            float cellW = canvasWidth / cols;
            float cellH = canvasHeight / rows;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Rect canvasRect = GUILayoutUtility.GetRect(canvasWidth, canvasHeight);
            EditorGUI.DrawRect(canvasRect, new Color(0.18f, 0.18f, 0.2f, 1f));

            // Draw cells (Inverted Y: Row 0 is at bottom)
            for (int r = 0; r < rows; r++)
            {
                int visualRow = (rows - 1) - r;
                float y = canvasRect.y + visualRow * cellH;

                for (int c = 0; c < cols; c++)
                {
                    float x = canvasRect.x + c * cellW;
                    Rect cellRect = new Rect(x + 1.5f, y + 1.5f, cellW - 3f, cellH - 3f);
                    Vector2Int pos = new Vector2Int(c, r);

                    var cellData = selectedLevel.GetUnifiedCell(pos);
                    bool isDest = (selectedLevel.unifiedMode == UnifiedMode.DestinationDropOff) && (pos == selectedLevel.sharedDestinationPos || cellData.cellType == UnifiedCellType.Destination);

                    Color tileColor;
                    string label = "";
                    Color textColor = Color.black;

                    if (isDest)
                    {
                        tileColor = new Color(1f, 0.82f, 0.15f, 1f); // Gold
                        label = "DEST";
                        textColor = Color.black;
                    }
                    else if (cellData.cellType == UnifiedCellType.Blocked && !cellData.hideVisual)
                    {
                        tileColor = new Color(0.24f, 0.25f, 0.28f, 1f); // Dark obstacle
                        label = "✕";
                        textColor = new Color(0.6f, 0.6f, 0.65f, 1f);
                    }
                    else if (cellData.cellType == UnifiedCellType.Hole || (cellData.cellType == UnifiedCellType.Blocked && cellData.hideVisual))
                    {
                        tileColor = new Color(0.12f, 0.12f, 0.15f, 0.35f); // Cutout hole
                        label = "🕳️";
                        textColor = new Color(0.5f, 0.5f, 0.55f, 0.8f);
                    }
                    else if (cellData.cellType == UnifiedCellType.Passenger)
                    {
                        tileColor = GetColor(cellData.colorId);
                        string walkTag = cellData.walkableAfterCollection ? "🔓" : "🔒";
                        label = $"{cellData.passengerCount} {walkTag}";
                        textColor = (cellData.colorId == 4 || cellData.colorId == 0) ? Color.white : Color.black;
                    }
                    else
                    {
                        tileColor = new Color(0.92f, 0.92f, 0.95f, 1f); // Walkable
                    }

                    EditorGUI.DrawRect(cellRect, tileColor);

                    if (!string.IsNullOrEmpty(label))
                    {
                        var lblStyle = new GUIStyle(EditorStyles.boldLabel)
                        {
                            alignment = TextAnchor.MiddleCenter,
                            fontSize = Mathf.Clamp(Mathf.FloorToInt(cellH * 0.42f), 8, 14),
                            normal = { textColor = textColor }
                        };
                        GUI.Label(cellRect, label, lblStyle);
                    }
                }
            }

            // Outline
            Handles.color = new Color(0.95f, 0.65f, 0.2f, 0.9f);
            Handles.DrawPolyLine(
                new Vector3(canvasRect.xMin, canvasRect.yMin),
                new Vector3(canvasRect.xMax, canvasRect.yMin),
                new Vector3(canvasRect.xMax, canvasRect.yMax),
                new Vector3(canvasRect.xMin, canvasRect.yMax),
                new Vector3(canvasRect.xMin, canvasRect.yMin)
            );

            // Handle painting input
            Event evt = Event.current;
            if ((evt.type == EventType.MouseDown || evt.type == EventType.MouseDrag) && canvasRect.Contains(evt.mousePosition))
            {
                int clickCol = Mathf.Clamp(Mathf.FloorToInt((evt.mousePosition.x - canvasRect.x) / cellW), 0, cols - 1);
                int visualR = Mathf.Clamp(Mathf.FloorToInt((evt.mousePosition.y - canvasRect.y) / cellH), 0, rows - 1);
                int clickRow = (rows - 1) - visualR;
                Vector2Int clickPos = new Vector2Int(clickCol, clickRow);

                Undo.RecordObject(selectedLevel, "Paint Unified Cell");

                if (evt.button == 1) // Right click erases to Walkable
                {
                    selectedLevel.SetUnifiedCell(clickPos, UnifiedCellType.Walkable, 0, 0);
                }
                else
                {
                    switch (activeUnifiedTool)
                    {
                        case UnifiedTool.Walkable:
                            selectedLevel.SetUnifiedCell(clickPos, UnifiedCellType.Walkable, 0, 0);
                            break;
                        case UnifiedTool.Blocked:
                            selectedLevel.SetUnifiedCell(clickPos, UnifiedCellType.Blocked, 0, 0, false, hideVisual: false);
                            break;
                        case UnifiedTool.Hole:
                            selectedLevel.SetUnifiedCell(clickPos, UnifiedCellType.Hole, 0, 0, false, hideVisual: true);
                            break;
                        case UnifiedTool.Passenger:
                            selectedLevel.SetUnifiedCell(clickPos, UnifiedCellType.Passenger, unifiedBrushColor, unifiedPassengerCount, unifiedWalkableAfterCollection);
                            break;
                        case UnifiedTool.Destination:
                            selectedLevel.SetUnifiedCell(clickPos, UnifiedCellType.Destination, 0, 0);
                            selectedLevel.sharedDestinationPos = clickPos;
                            break;
                    }
                }

                EditorUtility.SetDirty(selectedLevel);
                evt.Use();
                Repaint();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"Unified Grid: {cols}x{rows} | Rotation: {selectedLevel.unifiedGridRotation}° | Destination: ({selectedLevel.sharedDestinationPos.x}, {selectedLevel.sharedDestinationPos.y})", EditorStyles.centeredGreyMiniLabel);
        }

        private void DrawUnifiedBusesTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("1. Unified Buses Placement & Route Preview", EditorStyles.boldLabel);

            if (selectedLevel.unifiedBuses == null) selectedLevel.unifiedBuses = new List<BusSpawnData>();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Total Buses Placed: {selectedLevel.unifiedBuses.Count}", EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("+ Add Bus", GUILayout.Width(100)))
            {
                AddNewUnifiedBus();
            }
            if (GUILayout.Button("Duplicate", GUILayout.Width(80)) && selectedUnifiedBusIndex >= 0 && selectedUnifiedBusIndex < selectedLevel.unifiedBuses.Count)
            {
                DuplicateUnifiedBus(selectedUnifiedBusIndex);
            }
            if (GUILayout.Button("Delete", GUILayout.Width(70)) && selectedUnifiedBusIndex >= 0 && selectedUnifiedBusIndex < selectedLevel.unifiedBuses.Count)
            {
                DeleteUnifiedBus(selectedUnifiedBusIndex);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Visual Board with Buses Overlaid
            DrawVisualUnifiedPuzzleBoard();

            EditorGUILayout.Space(6);

            if (selectedLevel.unifiedBuses.Count > 0)
            {
                DrawUnifiedBusPropertiesEditor();
            }
            else
            {
                EditorGUILayout.HelpBox("No buses defined for this unified grid. Click '+ Add Bus' to place buses.", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawVisualUnifiedPuzzleBoard()
        {
            int gridCols = selectedLevel.unifiedGridColumns;
            int gridRows = selectedLevel.unifiedGridRows;

            float boardWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth - 80f, 260f, 480f);
            float boardHeight = boardWidth * ((float)gridRows / gridCols);
            float cellW = boardWidth / gridCols;
            float cellH = boardHeight / gridRows;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            Rect boardRect = GUILayoutUtility.GetRect(boardWidth, boardHeight);
            EditorGUI.DrawRect(boardRect, new Color(0.15f, 0.16f, 0.18f, 1f));

            // Occupancy map for bus overlaps
            var busOccupancy = new Dictionary<Vector2Int, List<int>>();
            if (selectedLevel.unifiedBuses != null)
            {
                for (int i = 0; i < selectedLevel.unifiedBuses.Count; i++)
                {
                    var bus = selectedLevel.unifiedBuses[i];
                    var cells = LevelData.GetOccupiedCells(bus);
                    for (int c = 0; c < cells.Count; c++)
                    {
                        var cell = cells[c];
                        if (!busOccupancy.ContainsKey(cell)) busOccupancy[cell] = new List<int>();
                        busOccupancy[cell].Add(i);
                    }
                }
            }

            // Draw base tiles
            for (int r = 0; r < gridRows; r++)
            {
                int visualRow = (gridRows - 1) - r;
                float y = boardRect.y + visualRow * cellH;

                for (int c = 0; c < gridCols; c++)
                {
                    float x = boardRect.x + c * cellW;
                    Rect tileRect = new Rect(x + 1.5f, y + 1.5f, cellW - 3f, cellH - 3f);
                    Vector2Int pos = new Vector2Int(c, r);

                    var cellData = selectedLevel.GetUnifiedCell(pos);
                    bool isDest = (pos == selectedLevel.sharedDestinationPos);
                    bool hasBusOverlap = busOccupancy.ContainsKey(pos) && busOccupancy[pos].Count > 1;

                    Color tileColor;
                    string tileText = "";

                    if (isDest)
                    {
                        tileColor = new Color(1f, 0.82f, 0.15f, 1f);
                        tileText = "DEST";
                    }
                    else if (cellData.cellType == UnifiedCellType.Blocked && !cellData.hideVisual)
                    {
                        tileColor = new Color(0.24f, 0.25f, 0.28f, 1f);
                        tileText = "✕";
                    }
                    else if (cellData.cellType == UnifiedCellType.Hole || (cellData.cellType == UnifiedCellType.Blocked && cellData.hideVisual))
                    {
                        tileColor = new Color(0.12f, 0.12f, 0.15f, 0.35f);
                        tileText = "🕳️";
                    }
                    else if (cellData.cellType == UnifiedCellType.Passenger)
                    {
                        tileColor = Color.Lerp(GetColor(cellData.colorId), Color.white, 0.35f);
                        string walkTag = cellData.walkableAfterCollection ? "🔓" : "🔒";
                        tileText = $"P:{cellData.passengerCount} {walkTag}";
                    }
                    else
                    {
                        tileColor = hasBusOverlap ? new Color(1f, 0.3f, 0.3f, 0.9f) : new Color(0.92f, 0.92f, 0.95f, 1f);
                    }

                    EditorGUI.DrawRect(tileRect, tileColor);

                    if (!string.IsNullOrEmpty(tileText) && !busOccupancy.ContainsKey(pos))
                    {
                        var s = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 8, normal = { textColor = Color.black } };
                        GUI.Label(tileRect, tileText, s);
                    }
                }
            }

            // Draw each bus on top
            if (selectedLevel.unifiedBuses != null)
            {
                for (int i = 0; i < selectedLevel.unifiedBuses.Count; i++)
                {
                    var bus = selectedLevel.unifiedBuses[i];
                    DrawBusOnGrid(i, bus, boardRect, cellW, cellH, gridRows);
                }
            }

            // Click selection
            Event evt = Event.current;
            if (evt.type == EventType.MouseDown && boardRect.Contains(evt.mousePosition) && evt.button == 0)
            {
                int clickCol = Mathf.Clamp(Mathf.FloorToInt((evt.mousePosition.x - boardRect.x) / cellW), 0, gridCols - 1);
                int visualR = Mathf.Clamp(Mathf.FloorToInt((evt.mousePosition.y - boardRect.y) / cellH), 0, gridRows - 1);
                int clickRow = (gridRows - 1) - visualR;
                Vector2Int clickPos = new Vector2Int(clickCol, clickRow);

                if (busOccupancy.ContainsKey(clickPos) && busOccupancy[clickPos].Count > 0)
                {
                    selectedUnifiedBusIndex = busOccupancy[clickPos][0];
                }
                else if (selectedUnifiedBusIndex >= 0 && selectedUnifiedBusIndex < selectedLevel.unifiedBuses.Count)
                {
                    var b = selectedLevel.unifiedBuses[selectedUnifiedBusIndex];
                    b.gridPosition = clickPos;
                    selectedLevel.unifiedBuses[selectedUnifiedBusIndex] = b;
                    EditorUtility.SetDirty(selectedLevel);
                }
                evt.Use();
                Repaint();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawUnifiedBusPropertiesEditor()
        {
            if (selectedUnifiedBusIndex < 0 || selectedUnifiedBusIndex >= selectedLevel.unifiedBuses.Count)
            {
                selectedUnifiedBusIndex = 0;
            }

            string[] busLabels = new string[selectedLevel.unifiedBuses.Count];
            for (int i = 0; i < selectedLevel.unifiedBuses.Count; i++)
            {
                var b = selectedLevel.unifiedBuses[i];
                string cName = GetColorName(b.colorId);
                busLabels[i] = $"{i + 1}: {b.name} [{cName}] (Cap: {b.capacity}, Len: {b.length})";
            }

            selectedUnifiedBusIndex = EditorGUILayout.Popup("Selected Bus:", selectedUnifiedBusIndex, busLabels);
            var bus = selectedLevel.unifiedBuses[selectedUnifiedBusIndex];

            EditorGUI.BeginChangeCheck();

            bus.name = EditorGUILayout.TextField("Bus Name:", bus.name);

            // Color Selection
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Bus Color:");
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
            if (busTotalEntries > 0) EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            int maxGridCols = selectedLevel.unifiedGridColumns;
            int maxGridRows = selectedLevel.unifiedGridRows;
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

            // Coordinates
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
                Undo.RecordObject(selectedLevel, "Edit Unified Bus");
                selectedLevel.unifiedBuses[selectedUnifiedBusIndex] = bus;
                EditorUtility.SetDirty(selectedLevel);
            }
        }

        private void AddNewUnifiedBus()
        {
            Undo.RecordObject(selectedLevel, "Add Unified Bus");
            if (selectedLevel.unifiedBuses == null) selectedLevel.unifiedBuses = new List<BusSpawnData>();

            var newBus = new BusSpawnData
            {
                name = $"Bus {selectedLevel.unifiedBuses.Count + 1}",
                colorId = unifiedBrushColor,
                capacity = 10,
                length = 2,
                rotationDegrees = 0,
                gridPosition = new Vector2Int(0, 0)
            };

            selectedLevel.unifiedBuses.Add(newBus);
            selectedUnifiedBusIndex = selectedLevel.unifiedBuses.Count - 1;
            EditorUtility.SetDirty(selectedLevel);
        }

        private void DuplicateUnifiedBus(int index)
        {
            Undo.RecordObject(selectedLevel, "Duplicate Unified Bus");
            var src = selectedLevel.unifiedBuses[index];
            var clone = src;
            clone.name = $"{src.name} (Copy)";
            selectedLevel.unifiedBuses.Add(clone);
            selectedUnifiedBusIndex = selectedLevel.unifiedBuses.Count - 1;
            EditorUtility.SetDirty(selectedLevel);
        }

        private void DeleteUnifiedBus(int index)
        {
            Undo.RecordObject(selectedLevel, "Delete Unified Bus");
            selectedLevel.unifiedBuses.RemoveAt(index);
            selectedUnifiedBusIndex = Mathf.Clamp(selectedUnifiedBusIndex - 1, 0, selectedLevel.unifiedBuses.Count - 1);
            EditorUtility.SetDirty(selectedLevel);
        }

        private void DrawUnifiedBalanceTab()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Unified Grid Passenger & Bus Capacity Balance", EditorStyles.boldLabel);

            int totalPassengers = selectedLevel.GetTotalUnifiedPassengers();
            int totalBusCapacity = 0;
            if (selectedLevel.unifiedBuses != null)
            {
                for (int i = 0; i < selectedLevel.unifiedBuses.Count; i++) totalBusCapacity += selectedLevel.unifiedBuses[i].capacity;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.HelpBox($"Total Passengers in Grid: {totalPassengers} | Total Bus Capacity: {totalBusCapacity}", MessageType.Info);
            if (GUILayout.Button("Auto-Balance Buses", GUILayout.Width(140), GUILayout.Height(38)))
            {
                AutoBalanceUnifiedBuses();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Table of Colors
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Color", EditorStyles.boldLabel, GUILayout.Width(90));
            GUILayout.Label("Passengers", EditorStyles.boldLabel, GUILayout.Width(80));
            GUILayout.Label("Buses", EditorStyles.boldLabel, GUILayout.Width(60));
            GUILayout.Label("Capacity", EditorStyles.boldLabel, GUILayout.Width(70));
            GUILayout.Label("Status", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            var colorIds = new List<int>();
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
                int pCount = selectedLevel.GetUnifiedPassengersByColor(cId);
                int bCap = selectedLevel.GetUnifiedBusCapacityByColor(cId);

                int busCount = 0;
                if (selectedLevel.unifiedBuses != null)
                {
                    for (int b = 0; b < selectedLevel.unifiedBuses.Count; b++)
                    {
                        if (selectedLevel.unifiedBuses[b].colorId == cId) busCount++;
                    }
                }

                if (pCount == 0 && busCount == 0) continue;

                EditorGUILayout.BeginHorizontal();
                Color c = GetColor(cId);
                Color prevC = GUI.color;
                GUI.color = c;
                GUILayout.Label($"■ {GetColorName(cId)}", GUILayout.Width(90));
                GUI.color = prevC;

                GUILayout.Label($"{pCount}", GUILayout.Width(80));
                GUILayout.Label($"{busCount}", GUILayout.Width(60));
                GUILayout.Label($"{bCap}", GUILayout.Width(70));

                if (pCount > 0 && busCount == 0)
                {
                    GUI.color = Color.red;
                    GUILayout.Label("✕ Missing Bus (Cannot Complete)");
                    GUI.color = prevC;
                }
                else if (bCap < pCount)
                {
                    GUI.color = new Color(1f, 0.6f, 0.1f);
                    GUILayout.Label($"⚠ Bus Capacity < Passengers ({bCap}/{pCount}) - Will reuse bus!");
                    GUI.color = prevC;
                }
                else
                {
                    GUI.color = Color.green;
                    GUILayout.Label("✓ Balanced");
                    GUI.color = prevC;
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Apply Unified Level to Active Scene", GUILayout.Height(30)))
            {
                ApplyLevelToScene();
            }

            EditorGUILayout.EndVertical();
        }

        private void AutoBalanceUnifiedBuses()
        {
            Undo.RecordObject(selectedLevel, "Auto-Balance Unified Buses");

            var colorIds = new List<int>();
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
                int pCount = selectedLevel.GetUnifiedPassengersByColor(cId);
                if (pCount <= 0) continue;

                // Check if matching bus exists
                var matchingBuses = new List<int>();
                if (selectedLevel.unifiedBuses != null)
                {
                    for (int b = 0; b < selectedLevel.unifiedBuses.Count; b++)
                    {
                        if (selectedLevel.unifiedBuses[b].colorId == cId) matchingBuses.Add(b);
                    }
                }

                if (matchingBuses.Count == 0)
                {
                    // Add a bus for this color
                    if (selectedLevel.unifiedBuses == null) selectedLevel.unifiedBuses = new List<BusSpawnData>();
                    selectedLevel.unifiedBuses.Add(new BusSpawnData
                    {
                        name = $"{GetColorName(cId)} Bus",
                        colorId = cId,
                        capacity = Mathf.Min(pCount, 56),
                        length = 2,
                        rotationDegrees = 0,
                        gridPosition = new Vector2Int(0, 0)
                    });
                }
                else
                {
                    int capPerBus = Mathf.Max(1, pCount / matchingBuses.Count);
                    for (int m = 0; m < matchingBuses.Count; m++)
                    {
                        var bus = selectedLevel.unifiedBuses[matchingBuses[m]];
                        bus.capacity = Mathf.Clamp(capPerBus, 1, 56);
                        selectedLevel.unifiedBuses[matchingBuses[m]] = bus;
                    }
                }
            }

            EditorUtility.SetDirty(selectedLevel);
            Debug.Log("<color=green>[SandBlocks]</color> Auto-balanced unified buses!");
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
