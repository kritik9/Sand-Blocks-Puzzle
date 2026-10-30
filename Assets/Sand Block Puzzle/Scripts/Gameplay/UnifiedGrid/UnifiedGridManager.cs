using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using SandBlocks.Core.Data;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Vehicles;
using SandBlocks.Puzzle.Input;

namespace SandBlocks.Gameplay.UnifiedGrid
{
    /// <summary>
    /// Coordinates the Unified Grid gameplay mode.
    /// In this mode, both passengers and buses operate on a single configurable grid.
    /// Features:
    /// - Continuous drag-time & placed passenger boarding when adjacent to matching passenger cells
    /// - Continuous drag-time & placed passenger drop-off when adjacent to grouped destination cells
    /// - Authentic 3D character passenger models with matching colors
    /// - Dice symmetry (1 to 6) and 3-row layout (>6) for passengers on cells and destination platform
    /// - Multi-cell grouped destination station where arrived passengers remain visually standing
    /// - Level victory detection when all passenger cells and buses are emptied
    /// </summary>
    public class UnifiedGridManager : MonoBehaviour
    {
        public static UnifiedGridManager Instance { get; private set; }

        [Header("Scene References")]
        [SerializeField] private PuzzleGrid puzzleGrid;
        [SerializeField] private VehicleDragHandler dragHandler;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private LevelManager levelManager;
        [SerializeField] private CrowdColorPalette palette;
        [SerializeField] private GameObject busPrefab;
        [SerializeField] private GameObject characterPrefab;

        [Header("Character Tuning & Diversity")]
        [Tooltip("Uniform scale applied to passenger figures on grid stops and destination pads")]
        [SerializeField] private float characterScale = 0.08f;
        [Tooltip("Random scale variation range applied to characters (multiplier on base scale)")]
        [SerializeField] private Vector2 scaleVariationRange = new Vector2(0.92f, 1.08f);

        [Header("Character Rotation & Jitter")]
        [Tooltip("If true, rotates characters 180 degrees so their back side faces the camera, revealing their full body")]
        [SerializeField] private bool faceBackSide = true;
        [Tooltip("Base Euler rotation applied to standing passenger figures (X=pitch, Y=yaw, Z=roll)")]
        [SerializeField] private Vector3 characterRotationEuler = Vector3.zero;
        [Tooltip("Random Y-axis yaw rotation jitter range (degrees)")]
        [SerializeField] private Vector2 rotationJitterYRange = new Vector2(-12f, 12f);
        [Tooltip("Random Z-axis lean tilt range (degrees)")]
        [SerializeField] private Vector2 rotationJitterZRange = new Vector2(-3f, 3f);
        [Tooltip("Z elevation offset toward camera in legacy Vertical_XY mode")]
        [SerializeField] private float characterZOffset = -0.15f;

        [Header("Standing Crowd Layout & Spacing")]
        [Tooltip("Horizontal spacing multiplier between passenger columns within a cell (e.g. 1.0 = standard, >1.0 spreads columns wider)")]
        [Range(0.4f, 2.5f)]
        [SerializeField] private float crowdHorizontalSpacingMultiplier = 1.0f;

        [Tooltip("Vertical (depth) spacing multiplier between passenger rows within a cell (e.g. 1.0 = standard, >1.0 spreads rows further apart)")]
        [Range(0.4f, 2.5f)]
        [SerializeField] private float crowdVerticalSpacingMultiplier = 1.0f;

        [Tooltip("Base column spacing ratio relative to cell size")]
        [SerializeField] private float baseColumnSpacingRatio = 0.22f;

        [Tooltip("Base row spacing ratio relative to cell size")]
        [SerializeField] private float baseRowSpacingRatio = 0.26f;

        [Tooltip("If true, automatically scales down passenger figures when a cell has many passengers (e.g. > 6) so they fit comfortably without intersecting")]
        [SerializeField] private bool autoScaleCrowdByCount = true;

        [Tooltip("Scale multiplier applied when cell has > 6 passengers if autoScaleCrowdByCount is enabled")]
        [Range(0.5f, 1.0f)]
        [SerializeField] private float denseCrowdScaleFactor = 0.78f;

        [Header("Passenger Base Pad (CellPad) Dimensions")]
        [Tooltip("Thickness of the cell pad in Horizontal_XZ mode")]
        [SerializeField] private float cellPadThickness = 0.06f;

        [Tooltip("Elevation Y of the cell pad in Horizontal_XZ mode")]
        [SerializeField] private float cellPadElevationY = 0.03f;

        [Tooltip("Extra elevation offset for characters on the pad (fine-tune feet contact)")]
        [SerializeField] private float characterElevationOffset = 0.0f;

        [Tooltip("Thickness of destination station pads in Horizontal_XZ mode")]
        [SerializeField] private float stationPadThickness = 0.08f;

        [Tooltip("Elevation Y of destination station pads in Horizontal_XZ mode")]
        [SerializeField] private float stationPadElevationY = 0.04f;

        [Header("Visual Styling")]
        [SerializeField] private Color destinationColor = new Color(1f, 0.82f, 0.2f, 1f);
        [SerializeField] private Color blockedColor = new Color(0.28f, 0.28f, 0.32f, 1f);

        [Header("Streaming & Animation Tuning")]
        [SerializeField] private float streamTransferInterval = 0.08f;
        [SerializeField] private float passengerFlightDuration = 0.26f;

        [Header("Passenger Jump Arc & Camera Visibility")]
        [Tooltip("Vertical height added to the jump flight trajectory curve")]
        [SerializeField] private float jumpArcHeight = 2.4f;
        [Tooltip("Forward offset (-Z toward camera) added to flight midpoint so character stays in prominent view")]
        [SerializeField] private float jumpForwardBias = 0.75f;
        [Tooltip("Maximum pitch angle (degrees) tilting feet forward toward camera during landing so whole character is visible from top-down camera")]
        [SerializeField] private float landingFeetForwardTilt = 38f;

        [Header("Level Start Sequence")]
        [Tooltip("If true, plays juicy entrance animations on level start/restart")]
        [SerializeField] private bool animateLevelStart = true;
        [Tooltip("Delay in seconds before the first bus pops in")]
        [SerializeField] private float busStartDelay = 0.1f;
        [Tooltip("Time in seconds between each bus spawning onto the grid")]
        [SerializeField] private float busSpawnInterval = 0.06f;
        [Tooltip("Delay in seconds before crowd cells begin popping in")]
        [SerializeField] private float crowdStartDelay = 0.25f;
        [Tooltip("Time in seconds between each passenger stop / cell popping in")]
        [SerializeField] private float crowdSpawnInterval = 0.06f;

        // Runtime Tracking
        private LevelData currentLevel;
        private Transform visualRoot;
        private readonly Dictionary<Vector2Int, RuntimePassengerCell> passengerCells = new Dictionary<Vector2Int, RuntimePassengerCell>();
        private readonly List<BusVehicle> spawnedBuses = new List<BusVehicle>();
        private readonly HashSet<Vector2Int> destinationCells = new HashSet<Vector2Int>();
        private readonly Dictionary<Vector2Int, GameObject> destinationPadObjects = new Dictionary<Vector2Int, GameObject>();
        private readonly Dictionary<Vector2Int, List<GameObject>> destinationCellFigures = new Dictionary<Vector2Int, List<GameObject>>();
        private readonly Dictionary<BusVehicle, float> nextInteractionTime = new Dictionary<BusVehicle, float>();

        private GameObject destinationVisual;
        private int totalTransferredCount = 0;
        private bool isLevelCompleted = false;
        private int activeInFlightCount = 0;
        private Coroutine activeStartSequence;
        private bool isLevelStartAnimating = false;

        public bool IsLevelCompleted => isLevelCompleted;
        public int TotalTransferredCount => totalTransferredCount;
        public bool IsUnifiedActive => currentLevel != null && currentLevel.gameplayMode == GameplayMode.UnifiedGrid;
        public UnifiedMode CurrentUnifiedMode => currentLevel != null ? currentLevel.unifiedMode : UnifiedMode.DestinationDropOff;
        public float CharacterScale => characterScale;
        public Vector3 CharacterRotationEuler => characterRotationEuler;
        public bool IsBusBusy(BusVehicle bus) => false;
        public bool IsLevelStartAnimating => isLevelStartAnimating;
        public bool AnimateLevelStart { get => animateLevelStart; set => animateLevelStart = value; }
        public float CrowdHorizontalSpacingMultiplier { get => crowdHorizontalSpacingMultiplier; set { crowdHorizontalSpacingMultiplier = value; RefreshAllCellVisuals(); } }
        public float CrowdVerticalSpacingMultiplier { get => crowdVerticalSpacingMultiplier; set { crowdVerticalSpacingMultiplier = value; RefreshAllCellVisuals(); } }
        public bool AutoScaleCrowdByCount { get => autoScaleCrowdByCount; set { autoScaleCrowdByCount = value; RefreshAllCellVisuals(); } }
        public float DenseCrowdScaleFactor { get => denseCrowdScaleFactor; set { denseCrowdScaleFactor = value; RefreshAllCellVisuals(); } }
        public float CellPadThickness { get => cellPadThickness; set { cellPadThickness = value; RefreshAllCellVisuals(); } }
        public float CellPadElevationY { get => cellPadElevationY; set { cellPadElevationY = value; RefreshAllCellVisuals(); } }
        public float CharacterElevationOffset { get => characterElevationOffset; set { characterElevationOffset = value; RefreshAllCellVisuals(); } }
        public bool FaceBackSide { get => faceBackSide; set { faceBackSide = value; RefreshAllCellVisuals(); } }
        public Vector2 RotationJitterZRange { get => rotationJitterZRange; set { rotationJitterZRange = value; RefreshAllCellVisuals(); } }

        public Vector3 GetEffectiveBaseEuler()
        {
            bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);
            float curBaseRotY = isHorizontal
                ? characterRotationEuler.y + (faceBackSide ? 0f : 180f)
                : characterRotationEuler.y + (faceBackSide ? 180f : 0f);
            return new Vector3(characterRotationEuler.x, curBaseRotY, characterRotationEuler.z);
        }

        private static float GetStableRandom(int index, float min, float max)
        {
            if (Mathf.Approximately(min, max)) return min;
            uint hash = (uint)index * 2654435761u ^ 0x9e3779b9u;
            hash = (hash ^ (hash >> 16)) * 0x85ebca6bu;
            hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
            hash ^= (hash >> 16);
            float t = (hash & 0xFFFF) / 65535f;
            return Mathf.Lerp(min, max, t);
        }

        private class RuntimePassengerCell
        {
            public Vector2Int gridPos;
            public int colorId;
            public int remainingCount;
            public bool walkableAfterCollection;
            public GameObject rootObject;
            public List<GameObject> miniFigures = new List<GameObject>();
            public Renderer baseRenderer;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this) { Destroy(gameObject); return; }

            FindDependencies();
        }

        private void FindDependencies()
        {
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (dragHandler == null) dragHandler = FindAnyObjectByType<VehicleDragHandler>();
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
            if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();

            if (palette == null && gameManager != null && gameManager.Palette != null)
            {
                palette = gameManager.Palette;
            }
            if (palette == null)
            {
                palette = Resources.Load<CrowdColorPalette>("CrowdColorPalette");
            }
#if UNITY_EDITOR
            if (palette == null)
            {
                palette = UnityEditor.AssetDatabase.LoadAssetAtPath<CrowdColorPalette>("Assets/Sand Block Puzzle/Settings/CrowdColorPalette.asset");
            }
#endif

            if (busPrefab == null && gameManager != null && gameManager.BusPrefab != null)
            {
                busPrefab = gameManager.BusPrefab.gameObject;
            }
#if UNITY_EDITOR
            if (busPrefab == null)
            {
                busPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sand Block Puzzle/Prefabs/Bus/BusModel.prefab");
            }
#endif

#if UNITY_EDITOR
            if (characterPrefab == null)
            {
                characterPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Sand Block Puzzle/Prefabs/Character/CharacterModel.prefab");
            }
#endif
        }

        /// <summary>
        /// Initializes the Unified Grid system for the given LevelData.
        /// Configures grid dimensions, rotation, blocked cells, passenger cells, buses, and grouped destination cells.
        /// </summary>
        public void Initialize(LevelData level, PuzzleGrid grid, CrowdColorPalette pal)
        {
            FindDependencies();
            if (grid != null) puzzleGrid = grid;
            if (pal != null) palette = pal;
            currentLevel = level;
            isLevelCompleted = false;
            totalTransferredCount = 0;
            activeInFlightCount = 0;

            if (dragHandler != null)
            {
                dragHandler.ResetDragState();
            }

            Cleanup();

            if (level == null || puzzleGrid == null) return;

            // 1. Create root container for unified visual objects
            visualRoot = new GameObject("[UnifiedGridVisuals]").transform;
            visualRoot.SetParent(puzzleGrid.transform);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;

            // 2. Configure Grid Dimensions, Rotation, and Centering
            int cols = Mathf.Max(2, level.unifiedGridColumns);
            int rows = Mathf.Max(2, level.unifiedGridRows);

            puzzleGrid.CenterGridAtZero = true;
            puzzleGrid.SetGridRotation(level.unifiedGridRotation);
            puzzleGrid.ClearBlockedCells();

            // 3. Collect Grouped Destination Cells
            destinationCells.Clear();
            destinationPadObjects.Clear();
            destinationCellFigures.Clear();

            var destList = level.GetDestinationCells();
            if (CurrentUnifiedMode == UnifiedMode.DestinationDropOff)
            {
                for (int i = 0; i < destList.Count; i++)
                {
                    Vector2Int pos = destList[i];
                    destinationCells.Add(pos);
                    destinationCellFigures[pos] = new List<GameObject>();
                    // Destination platforms are obstacles that buses cannot drive on; buses park adjacent
                    puzzleGrid.SetCellBlocked(pos.x, pos.y, true);
                }
            }
            else
            {
                destinationCells.Clear();
                destinationCellFigures.Clear();
            }

            // 4. Register Blocked, Hole, and Passenger Cells
            if (level.unifiedCells != null)
            {
                for (int i = 0; i < level.unifiedCells.Count; i++)
                {
                    var cell = level.unifiedCells[i];
                    if (cell.cellType == UnifiedCellType.Blocked || cell.cellType == UnifiedCellType.Hole || cell.cellType == UnifiedCellType.Passenger)
                    {
                        bool hideVisual = (cell.cellType == UnifiedCellType.Hole) || cell.hideVisual;
                        puzzleGrid.SetCellBlocked(cell.gridPosition.x, cell.gridPosition.y, true, hideVisual: hideVisual);
                    }
                }
            }

            puzzleGrid.SetGridDimensions(cols, rows);

            // 5. Build Grouped Destination Station Platform (only in DestinationDropOff mode)
            if (CurrentUnifiedMode == UnifiedMode.DestinationDropOff)
            {
                BuildDestinationStation(destList);
            }
            else
            {
                if (destinationVisual != null) Destroy(destinationVisual);
            }

            // 6. Build Passenger Cells
            if (level.unifiedCells != null)
            {
                for (int i = 0; i < level.unifiedCells.Count; i++)
                {
                    var cell = level.unifiedCells[i];
                    if (cell.cellType == UnifiedCellType.Passenger && cell.passengerCount > 0)
                    {
                        SpawnPassengerCell(cell);
                    }
                }
            }

            // 7. Spawn Unified Buses with exact Editor Colors
            SpawnUnifiedBuses(level.unifiedBuses);

            // 8. Play Level Start Sequence Animation (matching classic mode entrance)
            if (animateLevelStart)
            {
                activeStartSequence = StartCoroutine(PlayLevelStartSequenceRoutine());
            }
            else
            {
                isLevelStartAnimating = false;
            }

            Debug.Log($"<color=cyan>[UnifiedGridManager]</color> Initialized {level.name}: {cols}x{rows}, {passengerCells.Count} passenger cells, {spawnedBuses.Count} buses, {destinationCells.Count} dest cells.");
        }

        #region Symmetry Layout Calculations
        /// <summary>
        /// Calculates symmetrical positions for passengers within a cell of size cellSize.
        /// - For 6 or fewer: Uses authentic Dice Symmetry (1=center, 2=diagonal, 3=diagonal+center, 4=4 corners, 5=5 dot, 6=2x3 columns).
        /// - For more than 6: Divides passengers evenly across three horizontal rows in the same cell.
        /// </summary>
public static Vector3[] GetSymmetricalPositions(int count, float cellSize)
        {
            if (count <= 0) return Array.Empty<Vector3>();

            float hMult = (Instance != null) ? Instance.crowdHorizontalSpacingMultiplier : 1.0f;
            float vMult = (Instance != null) ? Instance.crowdVerticalSpacingMultiplier : 1.0f;
            float baseColRatio = (Instance != null) ? Instance.baseColumnSpacingRatio : 0.22f;
            float baseRowRatio = (Instance != null) ? Instance.baseRowSpacingRatio : 0.26f;

            bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);
            float surfaceY = (Instance != null)
                ? (Instance.cellPadElevationY + Instance.cellPadThickness * 0.5f + Instance.characterElevationOffset)
                : 0.06f;
            float zOffset = (Instance != null) ? Instance.characterZOffset : -0.15f;

            var positions = new Vector3[count];

            float hSpacing = cellSize * baseColRatio * hMult;
            float vSpacing = cellSize * baseRowRatio * vMult;
            float maxSpan = cellSize * 0.82f;

            if (count <= 6)
            {
                float dX = Mathf.Min(hSpacing, maxSpan * 0.45f);
                float dV = Mathf.Min(vSpacing, maxSpan * 0.45f);

                switch (count)
                {
                    case 1:
                        positions[0] = MakePos(0f, 0f, isHorizontal, surfaceY, zOffset);
                        break;
                    case 2:
                        positions[0] = MakePos(-dX, 0f, isHorizontal, surfaceY, zOffset);
                        positions[1] = MakePos(dX, 0f, isHorizontal, surfaceY, zOffset);
                        break;
                    case 3:
                        positions[0] = MakePos(-dX, dV * 0.7f, isHorizontal, surfaceY, zOffset);
                        positions[1] = MakePos(0f, -dV * 0.35f, isHorizontal, surfaceY, zOffset);
                        positions[2] = MakePos(dX, dV * 0.7f, isHorizontal, surfaceY, zOffset);
                        break;
                    case 4:
                        positions[0] = MakePos(-dX, dV * 0.6f, isHorizontal, surfaceY, zOffset);
                        positions[1] = MakePos(dX, dV * 0.6f, isHorizontal, surfaceY, zOffset);
                        positions[2] = MakePos(-dX, -dV * 0.6f, isHorizontal, surfaceY, zOffset);
                        positions[3] = MakePos(dX, -dV * 0.6f, isHorizontal, surfaceY, zOffset);
                        break;
                    case 5:
                        positions[0] = MakePos(-dX, dV * 0.65f, isHorizontal, surfaceY, zOffset);
                        positions[1] = MakePos(dX, dV * 0.65f, isHorizontal, surfaceY, zOffset);
                        positions[2] = MakePos(0f, 0f, isHorizontal, surfaceY, zOffset);
                        positions[3] = MakePos(-dX, -dV * 0.65f, isHorizontal, surfaceY, zOffset);
                        positions[4] = MakePos(dX, -dV * 0.65f, isHorizontal, surfaceY, zOffset);
                        break;
                    case 6:
                        float col3Spacing = Mathf.Min(hSpacing * 0.85f, maxSpan / 2f);
                        for (int c = 0; c < 3; c++)
                        {
                            float x = (c - 1f) * col3Spacing;
                            positions[c] = MakePos(x, dV * 0.55f, isHorizontal, surfaceY, zOffset);
                            positions[c + 3] = MakePos(x, -dV * 0.55f, isHorizontal, surfaceY, zOffset);
                        }
                        break;
                }
                return positions;
            }

            // More than 6: 2 rows (for 7-8) or 3 rows (for 9+)
            int rowCount = (count <= 8) ? 2 : 3;
            int basePerRow = count / rowCount;
            int remainder = count % rowCount;

            int[] countsPerRow = new int[rowCount];
            for (int r = 0; r < rowCount; r++)
            {
                countsPerRow[r] = basePerRow + (r < remainder ? 1 : 0);
            }

            float actualRowSpacing = Mathf.Min(vSpacing, maxSpan / Mathf.Max(1, rowCount - 1));

            int index = 0;
            for (int r = 0; r < rowCount; r++)
            {
                int numInRow = countsPerRow[r];
                if (numInRow <= 0) continue;

                float actualColSpacing = (numInRow > 1) ? Mathf.Min(hSpacing, maxSpan / (numInRow - 1)) : 0f;
                float rowPos = (r - (rowCount - 1) * 0.5f) * actualRowSpacing;

                for (int c = 0; c < numInRow; c++)
                {
                    float x = (numInRow > 1) ? (c - (numInRow - 1) * 0.5f) * actualColSpacing : 0f;
                    positions[index++] = MakePos(x, -rowPos, isHorizontal, surfaceY, zOffset);
                }
            }

            return positions;
        }

        private static Vector3 MakePos(float x, float rowOffset, bool isHorizontal, float surfaceY, float zOffset)
        {
            if (isHorizontal)
            {
                return new Vector3(x, surfaceY, rowOffset);
            }
            else
            {
                return new Vector3(x, rowOffset, zOffset);
            }
        }
        #endregion

        #region Setup Visuals
private void SpawnPassengerCell(UnifiedCellData data)
        {
            var cell = new RuntimePassengerCell
            {
                gridPos = data.gridPosition,
                colorId = data.colorId,
                remainingCount = data.passengerCount,
                walkableAfterCollection = data.walkableAfterCollection
            };

            var cellRoot = new GameObject($"PassengerCell_{data.gridPosition.x}_{data.gridPosition.y}");
            cellRoot.transform.SetParent(visualRoot.transform);
            cellRoot.transform.position = puzzleGrid.GridToWorld(data.gridPosition, 1, false);
            cellRoot.transform.localRotation = Quaternion.identity;
            cell.rootObject = cellRoot;

            Color c = GetPaletteColor(data.colorId);
            bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);

            // Base platform pad
            var basePad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePad.name = "CellPad";
            basePad.transform.SetParent(cellRoot.transform);
            if (isHorizontal)
            {
                basePad.transform.localPosition = new Vector3(0f, cellPadElevationY, 0f);
                basePad.transform.localScale = animateLevelStart
                    ? Vector3.zero
                    : new Vector3(puzzleGrid.CellSize * 0.90f, cellPadThickness, puzzleGrid.CellSize * 0.90f);
            }
            else
            {
                basePad.transform.localPosition = new Vector3(0f, 0f, 0.08f);
                basePad.transform.localScale = animateLevelStart
                    ? Vector3.zero
                    : new Vector3(puzzleGrid.CellSize * 0.90f, puzzleGrid.CellSize * 0.90f, 0.12f);
            }

            var padCol = basePad.GetComponent<Collider>();
            if (padCol != null) Destroy(padCol);

            var padMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            padMat.color = c * 0.85f;
            basePad.GetComponent<Renderer>().sharedMaterial = padMat;
            cell.baseRenderer = basePad.GetComponent<Renderer>();

            // Symmetrical passenger figures standing on cell
            int figureCount = Mathf.Min(data.passengerCount, 18);
            Vector3[] positions = GetSymmetricalPositions(figureCount, puzzleGrid.CellSize);
            float figBaseScale = (autoScaleCrowdByCount && figureCount > 6) ? characterScale * denseCrowdScaleFactor : characterScale;

            for (int i = 0; i < figureCount; i++)
            {
                var fig = CreatePassengerFigure(data.colorId, i, data.gridPosition);
                fig.name = $"Figure_{i}";
                fig.transform.SetParent(cellRoot.transform);
                fig.transform.localPosition = positions[i];
                if (animateLevelStart)
                {
                    fig.transform.localScale = Vector3.zero;
                }
                else
                {
                    fig.transform.localScale = Vector3.one * figBaseScale;
                }
                cell.miniFigures.Add(fig);
            }

            passengerCells[data.gridPosition] = cell;
        }

                private void BuildDestinationStation(List<Vector2Int> cells)
        {
            if (destinationVisual != null) Destroy(destinationVisual);

            destinationVisual = new GameObject("DestinationStation_Group");
            destinationVisual.transform.SetParent(visualRoot.transform);
            destinationVisual.transform.localPosition = Vector3.zero;
            destinationVisual.transform.localRotation = Quaternion.identity;

            if (cells == null || cells.Count == 0) return;

            Vector3 centroid = Vector3.zero;

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int pos = cells[i];
                Vector3 worldPos = puzzleGrid.GridToWorld(pos, 1, false);
                centroid += worldPos;

                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = $"StationPad_{pos.x}_{pos.y}";
                pad.transform.SetParent(destinationVisual.transform);
                pad.transform.position = worldPos;
                bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);
                if (isHorizontal)
                {
                    pad.transform.localPosition = new Vector3(pad.transform.localPosition.x, stationPadElevationY, pad.transform.localPosition.z);
                    pad.transform.localScale = animateLevelStart ? Vector3.zero : new Vector3(puzzleGrid.CellSize * 0.96f, stationPadThickness, puzzleGrid.CellSize * 0.96f);
                }
                else
                {
                    pad.transform.localPosition = new Vector3(pad.transform.localPosition.x, pad.transform.localPosition.y, 0.12f);
                    pad.transform.localScale = animateLevelStart ? Vector3.zero : new Vector3(puzzleGrid.CellSize * 0.96f, puzzleGrid.CellSize * 0.96f, 0.20f);
                }
                var col = pad.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var padMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                padMat.color = destinationColor;
                pad.GetComponent<Renderer>().sharedMaterial = padMat;

                destinationPadObjects[pos] = pad;
                if (!destinationCellFigures.ContainsKey(pos)) destinationCellFigures[pos] = new List<GameObject>();
            }

            centroid /= cells.Count;

            // Station Canopy Arch placed centrally across destination group
            var arch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arch.name = "StationCanopy";
            arch.transform.SetParent(destinationVisual.transform);
            arch.transform.position = centroid;
            arch.transform.localPosition = new Vector3(arch.transform.localPosition.x, arch.transform.localPosition.y + puzzleGrid.CellSize * 0.35f, -0.4f);
            arch.transform.localScale = animateLevelStart ? Vector3.zero : new Vector3(puzzleGrid.CellSize * 0.88f, 0.20f, 0.35f);
            var aCol = arch.GetComponent<Collider>();
            if (aCol != null) Destroy(aCol);

            var archMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            archMat.color = new Color(0.95f, 0.95f, 0.98f, 1f);
            arch.GetComponent<Renderer>().sharedMaterial = archMat;

        }

        private void SpawnUnifiedBuses(List<BusSpawnData> busDatas)
        {
            spawnedBuses.Clear();
            nextInteractionTime.Clear();
            if (busDatas == null || busDatas.Count == 0) return;

            Transform busParent = puzzleGrid.transform.Find("BusContainer");
            if (busParent == null)
            {
                var bpGo = new GameObject("BusContainer");
                bpGo.transform.SetParent(puzzleGrid.transform);
                bpGo.transform.localPosition = Vector3.zero;
                bpGo.transform.localRotation = Quaternion.identity;
                busParent = bpGo.transform;
            }

            GameObject prefabToUse = busPrefab;
            if (prefabToUse == null)
            {
                var existingBus = FindAnyObjectByType<BusVehicle>();
                if (existingBus != null) prefabToUse = existingBus.gameObject;
            }

            for (int i = 0; i < busDatas.Count; i++)
            {
                var data = busDatas[i];
                GameObject busGo;
                if (prefabToUse != null)
                {
                    busGo = Instantiate(prefabToUse, busParent);
                }
                else
                {
                    busGo = new GameObject(data.name);
                    busGo.transform.SetParent(busParent);
                    busGo.AddComponent<BusVehicle>();
                }

                busGo.name = data.name;
                var bus = busGo.GetComponent<BusVehicle>();
                if (bus != null)
                {
                    bus.Initialize(data.colorId, data.capacity, data.length, data.rotationDegrees, data.gridPosition);
                    bus.ClearPassengers();
                    bus.ApplyColor(palette);
                    bus.OnBusFullOverride = null;

                    puzzleGrid.PlaceVehicle(bus, data.gridPosition);
                    spawnedBuses.Add(bus);
                    nextInteractionTime[bus] = 0f;
                }
            }
        }
        #endregion

        #region Interaction Loop (Dragging & Placed)
        private void Update()
        {
            if (isLevelCompleted || isLevelStartAnimating || currentLevel == null || currentLevel.gameplayMode != GameplayMode.UnifiedGrid) return;

            // 1. Check actively dragged bus first (smooth drag-time collection and drop-off)
            if (dragHandler != null && dragHandler.IsDragging && dragHandler.HoveredVehicle is BusVehicle draggedBus)
            {
                ProcessBusInteractions(draggedBus);
            }

            // 2. Check all placed buses
            for (int i = 0; i < spawnedBuses.Count; i++)
            {
                var bus = spawnedBuses[i];
                if (bus == null) continue;
                if (dragHandler != null && dragHandler.IsDragging && dragHandler.HoveredVehicle == bus) continue;

                ProcessBusInteractions(bus);
            }

            // 3. Victory Check
            if (activeInFlightCount == 0 && !isLevelCompleted)
            {
                CheckLevelCompletion();
            }
        }

        private void ProcessBusInteractions(BusVehicle bus)
        {
            if (bus == null) return;
            if (!nextInteractionTime.TryGetValue(bus, out float nextTime)) nextTime = 0f;
            if (Time.time < nextTime) return;

            // A. If in DestinationDropOff mode and bus has passengers and is adjacent to Destination station -> Stream drop-off!
            if (CurrentUnifiedMode == UnifiedMode.DestinationDropOff && bus.PassengerCount > 0 && IsAdjacentToDestination(bus, out Vector2Int destCell))
            {
                nextInteractionTime[bus] = Time.time + streamTransferInterval;
                DropoffOnePassenger(bus, destCell);
                return;
            }

            // B. If bus is NOT full and is adjacent to matching passenger cell -> Stream collection!
            if (!bus.IsFull)
            {
                var adjCell = FindAdjacentMatchingCell(bus);
                if (adjCell != null && adjCell.remainingCount > 0)
                {
                    nextInteractionTime[bus] = Time.time + streamTransferInterval;
                    CollectOnePassenger(bus, adjCell);
                }
            }
        }

        private bool IsAdjacentToDestination(BusVehicle bus, out Vector2Int destCell)
        {
            destCell = Vector2Int.zero;
            if (bus == null) return false;

            for (int i = 0; i < bus.Length; i++)
            {
                int gx = bus.IsVertical ? bus.GridPosition.x : bus.GridPosition.x + i;
                int gy = bus.IsVertical ? bus.GridPosition.y + i : bus.GridPosition.y;
                Vector2Int cellPos = new Vector2Int(gx, gy);

                Vector2Int[] neighbors = new Vector2Int[]
                {
                    cellPos + Vector2Int.up,
                    cellPos + Vector2Int.down,
                    cellPos + Vector2Int.left,
                    cellPos + Vector2Int.right
                };

                for (int n = 0; n < neighbors.Length; n++)
                {
                    if (destinationCells.Contains(neighbors[n]))
                    {
                        destCell = neighbors[n];
                        return true;
                    }
                }
            }
            return false;
        }

        private RuntimePassengerCell FindAdjacentMatchingCell(BusVehicle bus)
        {
            if (bus == null) return null;

            for (int i = 0; i < bus.Length; i++)
            {
                int gx = bus.IsVertical ? bus.GridPosition.x : bus.GridPosition.x + i;
                int gy = bus.IsVertical ? bus.GridPosition.y + i : bus.GridPosition.y;
                Vector2Int cellPos = new Vector2Int(gx, gy);

                Vector2Int[] neighbors = new Vector2Int[]
                {
                    cellPos + Vector2Int.up,
                    cellPos + Vector2Int.down,
                    cellPos + Vector2Int.left,
                    cellPos + Vector2Int.right
                };

                for (int n = 0; n < neighbors.Length; n++)
                {
                    Vector2Int neighbor = neighbors[n];
                    if (passengerCells.TryGetValue(neighbor, out var pCell))
                    {
                        if (pCell.colorId == bus.ColorId && pCell.remainingCount > 0)
                        {
                            return pCell;
                        }
                    }
                }
            }
            return null;
        }
        #endregion

        #region Passenger Boarding & Transfer Execution
        private void CollectOnePassenger(BusVehicle bus, RuntimePassengerCell cell)
        {
            if (bus == null || cell == null || cell.remainingCount <= 0 || bus.IsFull) return;

            cell.remainingCount--;

            UpdatePassengerCellFigures(cell);

            if (bus.TryReserveSeat(out Vector3 seatPos))
            {
                StartCoroutine(AnimatePassengerFlightToBus(cell.rootObject.transform.position, bus, seatPos, cell.colorId, () =>
                {
                    bus.BoardPassenger();
                }));
            }

            if (cell.remainingCount <= 0)
            {
                if (cell.walkableAfterCollection)
                {
                    if (puzzleGrid != null)
                    {
                        puzzleGrid.UnblockCell(cell.gridPos);
                    }

                    if (cell.rootObject != null)
                    {
                        StartCoroutine(FadeAndDestroyClearedPad(cell.rootObject));
                    }

                    if (dragHandler != null && dragHandler.IsDragging)
                    {
                        dragHandler.RefreshReachablePositions();
                    }
                }
                else
                {
                    // Remains a blocked obstacle after collection
                    if (cell.baseRenderer != null)
                    {
                        cell.baseRenderer.material.color = Color.gray;
                    }
                }
            }
        }

        private void DropoffOnePassenger(BusVehicle bus, Vector2Int fallbackDestCell)
        {
            if (bus == null || bus.PassengerCount <= 0) return;

            if (bus.TryUnboardPassenger(out Vector3 seatWorldPos))
            {
                Vector2Int targetDestCell = GetLeastPopulatedDestinationCell(fallbackDestCell);

                StartCoroutine(AnimatePassengerFlightToDestination(bus, seatWorldPos, targetDestCell, bus.ColorId, (arrivedFlyer) =>
                {
                    // Character remains visually standing at the destination cell in symmetrical order!
                    AddCharacterToDestinationCell(targetDestCell, arrivedFlyer);

                    totalTransferredCount++;

                    CheckLevelCompletion();
                }));
            }
        }

        private Vector2Int GetLeastPopulatedDestinationCell(Vector2Int fallback)
        {
            Vector2Int best = fallback;
            int minCount = int.MaxValue;

            foreach (var pos in destinationCells)
            {
                int count = destinationCellFigures.TryGetValue(pos, out var list) ? list.Count : 0;
                if (count < minCount)
                {
                    minCount = count;
                    best = pos;
                }
            }
            return best;
        }

        private void UpdatePassengerCellFigures(RuntimePassengerCell cell)
        {
            int targetVisible = Mathf.Min(cell.remainingCount, 18);

            // Remove extra figures if count decreased
            while (cell.miniFigures.Count > targetVisible)
            {
                int lastIdx = cell.miniFigures.Count - 1;
                var fig = cell.miniFigures[lastIdx];
                cell.miniFigures.RemoveAt(lastIdx);
                if (fig != null) Destroy(fig);
            }

            // Reposition remaining figures according to dice (<=6) or 3-row (>6) symmetry
            Vector3[] newPositions = GetSymmetricalPositions(cell.miniFigures.Count, puzzleGrid.CellSize);
            for (int i = 0; i < cell.miniFigures.Count; i++)
            {
                if (cell.miniFigures[i] != null && i < newPositions.Length)
                {
                    cell.miniFigures[i].transform.localPosition = newPositions[i];
                }
            }
        }

        private void AddCharacterToDestinationCell(Vector2Int destCell, GameObject character)
        {
            if (character == null) return;

            Transform padTransform = destinationVisual.transform;
            if (destinationPadObjects.TryGetValue(destCell, out var padObj) && padObj != null)
            {
                padTransform = padObj.transform;
            }

            character.name = $"TerminalPassenger_{totalTransferredCount}";
            character.transform.SetParent(padTransform);
            Vector3 baseEuler = GetEffectiveBaseEuler();
            var destCp = character.GetComponent<CharacterPassenger>();
            if (destCp != null)
            {
                destCp.PlayIdle();
                destCp.ApplyRotationJitter(baseEuler, 0f, 0f);
            }
            else
            {
                character.transform.localRotation = Quaternion.Euler(baseEuler);
            }
            character.transform.localScale = Vector3.one * characterScale;

            if (!destinationCellFigures.ContainsKey(destCell)) destinationCellFigures[destCell] = new List<GameObject>();
            var list = destinationCellFigures[destCell];
            list.Add(character);

            // Re-arrange all figures on this destination cell according to dice / 3-row symmetry
            Vector3[] positions = GetSymmetricalPositions(list.Count, puzzleGrid.CellSize);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && i < positions.Length)
                {
                    list[i].transform.localPosition = positions[i];
                }
            }
        }
        #endregion

        private IEnumerator FadeAndDestroyClearedPad(GameObject padObj)
        {
            if (padObj == null) yield break;

            float duration = 0.25f;
            float elapsed = 0f;
            Vector3 startScale = padObj.transform.localScale;
            Vector3 startPos = padObj.transform.localPosition;
            Vector3 endPos = startPos + new Vector3(0f, 0f, 0.2f);

            while (elapsed < duration)
            {
                if (padObj == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                padObj.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                padObj.transform.localPosition = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }

            if (padObj != null)
            {
                Destroy(padObj);
            }
        }

        #region Commuter Flight Routines (Dynamic Tracking while Dragging)
        private IEnumerator AnimatePassengerFlightToBus(Vector3 startPos, BusVehicle targetBus, Vector3 targetSeatPos, int colorId, Action onArrived)
        {
            activeInFlightCount++;

            GameObject flyer = CreatePassengerFigure(colorId);
            flyer.name = "InFlight_ToBus";
            flyer.transform.position = startPos;
            var cpFlyer = flyer.GetComponent<CharacterPassenger>();
            if (cpFlyer != null) cpFlyer.PlayRunning();

            Vector3 localSeatOffset = (targetBus != null && targetSeatPos != Vector3.zero)
                ? targetBus.transform.InverseTransformPoint(targetSeatPos)
                : Vector3.zero;

            Vector3 endPos = (targetBus != null) ? targetBus.transform.TransformPoint(localSeatOffset) : targetSeatPos;
            float elapsed = 0f;
            while (elapsed < passengerFlightDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / passengerFlightDuration);

                // Dynamically track moving bus as player drags it across the board
                if (targetBus != null)
                {
                    endPos = targetBus.transform.TransformPoint(localSeatOffset);
                }

                Vector3 midPoint = (startPos + endPos) * 0.5f + Vector3.up * jumpArcHeight - Vector3.forward * jumpForwardBias;
                float oneMinusT = 1f - t;
                Vector3 currentPos = oneMinusT * oneMinusT * startPos + 2f * oneMinusT * t * midPoint + t * t * endPos;
                flyer.transform.position = currentPos;

                if (cpFlyer != null)
                {
                    cpFlyer.ApplyJumpFlightTilt(t, landingFeetForwardTilt, characterRotationEuler);
                }
                yield return null;
            }

            Destroy(flyer);
            activeInFlightCount--;
            onArrived?.Invoke();
        }

        private IEnumerator AnimatePassengerFlightToDestination(BusVehicle sourceBus, Vector3 sourceSeatPos, Vector2Int destCell, int colorId, Action<GameObject> onArrived)
        {
            activeInFlightCount++;

            GameObject flyer = CreatePassengerFigure(colorId);
            flyer.name = "InFlight_ToDest";
            var cpFlyer = flyer.GetComponent<CharacterPassenger>();
            if (cpFlyer != null) cpFlyer.PlayRunning();

            Vector3 localSeatOffset = (sourceBus != null && sourceSeatPos != Vector3.zero)
                ? sourceBus.transform.InverseTransformPoint(sourceSeatPos)
                : Vector3.zero;

            Vector3 startPos = (sourceBus != null) ? sourceBus.transform.TransformPoint(localSeatOffset) : sourceSeatPos;
            flyer.transform.position = startPos;

            Vector3 destWorld = puzzleGrid.GridToWorld(destCell, 1, false);
            float elapsed = 0f;
            while (elapsed < passengerFlightDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / passengerFlightDuration);

                if (sourceBus != null)
                {
                    startPos = sourceBus.transform.TransformPoint(localSeatOffset);
                }

                Vector3 midPoint = (startPos + destWorld) * 0.5f + Vector3.up * jumpArcHeight - Vector3.forward * jumpForwardBias;
                float oneMinusT = 1f - t;
                Vector3 currentPos = oneMinusT * oneMinusT * startPos + 2f * oneMinusT * t * midPoint + t * t * destWorld;
                flyer.transform.position = currentPos;

                if (cpFlyer != null)
                {
                    // Feet forward during flight and descent; straightens upright on touchdown
                    float tiltProg = t < 0.82f ? (t / 0.82f) : Mathf.Lerp(1f, 0f, (t - 0.82f) / 0.18f);
                    cpFlyer.ApplyJumpFlightTilt(tiltProg, landingFeetForwardTilt, characterRotationEuler);
                }
                yield return null;
            }

            activeInFlightCount--;
            SandBlocks.Core.Audio.AudioManager.Instance?.PlayPassengerDropOff();
            SandBlocks.Core.Haptics.HapticManager.Instance?.LightImpact();
            onArrived?.Invoke(flyer);
        }

private GameObject CreatePassengerFigure(int colorId, int cellIndex = 0, Vector2Int gridPos = default)
        {
            GameObject fig;
            Color c = GetPaletteColor(colorId);

            Vector3 baseEuler = GetEffectiveBaseEuler();

            int seedY = cellIndex * 2399 + (gridPos.x * 37 + gridPos.y * 101) * 10007 + 3;
            int seedZ = cellIndex * 4001 + (gridPos.x * 53 + gridPos.y * 131) * 13337 + 5;
            float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                ? GetStableRandom(seedY, rotationJitterYRange.x, rotationJitterYRange.y)
                : 0f;
            float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                ? GetStableRandom(seedZ, rotationJitterZRange.x, rotationJitterZRange.y)
                : 0f;

            if (characterPrefab != null)
            {
                fig = Instantiate(characterPrefab);
                float randScaleMult = (scaleVariationRange.x != 1f || scaleVariationRange.y != 1f)
                    ? UnityEngine.Random.Range(scaleVariationRange.x, scaleVariationRange.y)
                    : 1f;
                fig.transform.localScale = Vector3.one * (characterScale * randScaleMult);

                var col = fig.GetComponent<Collider>();
                if (col != null) Destroy(col);

                var cp = fig.GetComponent<CharacterPassenger>() ?? fig.AddComponent<CharacterPassenger>();
                cp.SetColor(c);
                cp.PlayIdle();
                cp.ApplyRotationJitter(baseEuler, jitterY, jitterZ);
            }
            else
            {
                fig = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);
                fig.transform.localScale = isHorizontal ? new Vector3(0.24f, 0.28f, 0.24f) : new Vector3(0.24f, 0.28f, 0.24f);
                fig.transform.localRotation = Quaternion.Euler(baseEuler.x, baseEuler.y + jitterY, baseEuler.z + jitterZ);
                var col = fig.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = c;
                fig.GetComponent<Renderer>().sharedMaterial = mat;
            }

            return fig;
        }
        #endregion

        #region Victory & Level Advancement
        private void CheckLevelCompletion()
        {
            if (isLevelCompleted) return;

            foreach (var kvp in passengerCells)
            {
                if (kvp.Value.remainingCount > 0) return;
            }

            if (CurrentUnifiedMode == UnifiedMode.DestinationDropOff)
            {
                for (int i = 0; i < spawnedBuses.Count; i++)
                {
                    if (spawnedBuses[i] != null && spawnedBuses[i].PassengerCount > 0) return;
                }
            }
            else // BusDriveAway: all buses must have filled and driven away
            {
                for (int i = 0; i < spawnedBuses.Count; i++)
                {
                    if (spawnedBuses[i] != null) return;
                }
            }

            if (activeInFlightCount > 0) return;

            isLevelCompleted = true;
            Debug.Log("<color=gold>[UnifiedGridManager]</color> LEVEL COMPLETED! All passengers transferred to destination.");

            SandBlocks.Core.Audio.AudioManager.Instance?.PlayLevelWin();
            SandBlocks.Core.Haptics.HapticManager.Instance?.Success();

            StartCoroutine(VictorySequenceRoutine());
        }

        private IEnumerator VictorySequenceRoutine()
        {
            yield return new WaitForSeconds(1.2f);
            if (levelManager != null && levelManager.HasNextLevel)
            {
                levelManager.NextLevel();
            }
        }
        #endregion

        #region Palette & Cleanup
        public Color GetPaletteColor(int id)
        {
            if (palette != null && palette.Entries != null)
            {
                for (int i = 0; i < palette.Entries.Length; i++)
                {
                    if (palette.Entries[i].id == id) return palette.Entries[i].color;
                }
            }

            Color[] fallbacks = new Color[]
            {
                new Color(0.18f, 0.55f, 1f, 1f),
                new Color(0.2f, 0.85f, 0.35f, 1f),
                new Color(1f, 0.82f, 0.1f, 1f),
                new Color(1f, 0.35f, 0.65f, 1f),
                new Color(0.18f, 0.18f, 0.22f, 1f),
                new Color(0.92f, 0.92f, 0.95f, 1f),
                new Color(1f, 0.5f, 0.1f, 1f),
                new Color(1f, 0.208f, 0.102f, 1f),
                new Color(0.102f, 1f, 0.674f, 1f)
            };
            return (id >= 0 && id < fallbacks.Length) ? fallbacks[id] : Color.white;
        }

        #region Level Start Animation
        private IEnumerator PlayLevelStartSequenceRoutine()
        {
            isLevelStartAnimating = true;
            bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);

            // 1. Destination platform entrance (if in DestinationDropOff mode)
            if (CurrentUnifiedMode == UnifiedMode.DestinationDropOff && destinationVisual != null && puzzleGrid != null)
            {
                Vector3 destPadScale = isHorizontal
                    ? new Vector3(puzzleGrid.CellSize * 0.96f, stationPadThickness, puzzleGrid.CellSize * 0.96f)
                    : new Vector3(puzzleGrid.CellSize * 0.96f, puzzleGrid.CellSize * 0.96f, 0.20f);
                int padIdx = 0;
                foreach (var pad in destinationPadObjects.Values)
                {
                    if (pad != null)
                    {
                        StartCoroutine(PopInTransform(pad.transform, destPadScale, delay: padIdx * 0.04f, duration: 0.30f));
                        padIdx++;
                    }
                }
                var arch = destinationVisual.transform.Find("StationCanopy");
                if (arch != null)
                {
                    StartCoroutine(PopInTransform(arch, new Vector3(puzzleGrid.CellSize * 0.88f, 0.20f, 0.35f), delay: padIdx * 0.04f, duration: 0.32f));
                }
            }

            // 2. Animate buses with staggered elastic bounce pop-in (matching classic mode entrance)
            for (int i = 0; i < spawnedBuses.Count; i++)
            {
                if (spawnedBuses[i] != null)
                {
                    float delay = busStartDelay + i * busSpawnInterval;
                    spawnedBuses[i].PlaySpawnAnimation(delay);
                }
            }

            // 3. Crowd / Passenger Stops & Dolls Entrance (after crowdStartDelay)
            if (crowdStartDelay > 0f)
            {
                yield return new WaitForSeconds(crowdStartDelay);
            }

            // Sort passenger cells by row (gridPos.y) then column (gridPos.x) for an orderly entrance
            var sortedCells = new List<RuntimePassengerCell>(passengerCells.Values);
            sortedCells.Sort((a, b) =>
            {
                int yComp = a.gridPos.y.CompareTo(b.gridPos.y);
                return yComp != 0 ? yComp : a.gridPos.x.CompareTo(b.gridPos.x);
            });

            Vector3 targetBaseScale = (puzzleGrid != null)
                ? (isHorizontal
                    ? new Vector3(puzzleGrid.CellSize * 0.90f, cellPadThickness, puzzleGrid.CellSize * 0.90f)
                    : new Vector3(puzzleGrid.CellSize * 0.90f, puzzleGrid.CellSize * 0.90f, 0.12f))
                : Vector3.one;

            float maxAnimDuration = 0f;

            for (int c = 0; c < sortedCells.Count; c++)
            {
                var cell = sortedCells[c];
                if (cell == null) continue;

                float cellDelay = c * crowdSpawnInterval;

                // Stop base pad pops up
                if (cell.baseRenderer != null)
                {
                    StartCoroutine(PopInTransform(cell.baseRenderer.transform, targetBaseScale, delay: cellDelay, duration: 0.28f));
                    maxAnimDuration = Mathf.Max(maxAnimDuration, cellDelay + 0.28f);
                }

                float cellFigScale = (autoScaleCrowdByCount && cell.miniFigures.Count > 6)
                    ? characterScale * denseCrowdScaleFactor
                    : characterScale;
                Vector3 targetFigScale = characterPrefab != null
                    ? Vector3.one * cellFigScale
                    : new Vector3(0.24f, 0.28f, 0.24f);

                // Dolls pop in with micro-stagger
                for (int f = 0; f < cell.miniFigures.Count; f++)
                {
                    var fig = cell.miniFigures[f];
                    if (fig != null)
                    {
                        float figDelay = cellDelay + f * 0.02f;
                        StartCoroutine(PopInTransform(fig.transform, targetFigScale, delay: figDelay, duration: 0.30f));
                        maxAnimDuration = Mathf.Max(maxAnimDuration, figDelay + 0.30f);
                    }
                }
            }

            // Wait for all animations to complete
            float busMaxTime = busStartDelay + Mathf.Max(0, spawnedBuses.Count - 1) * busSpawnInterval + 0.35f;
            float remainingWait = Mathf.Max(maxAnimDuration, busMaxTime - crowdStartDelay);

            if (remainingWait > 0f)
            {
                yield return new WaitForSeconds(remainingWait);
            }

            yield return new WaitForSeconds(0.15f); // settling buffer

            // Solidify all pristine scales
            RestoreAllElementsToFullScale();

            isLevelStartAnimating = false;
            activeStartSequence = null;
        }

        private IEnumerator PopInTransform(Transform target, Vector3 targetScale, float delay, float duration = 0.32f, float overshoot = 1.70158f)
        {
            if (target == null) yield break;
            target.localScale = Vector3.zero;

            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (target == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (target == null) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // EaseOutBack curve matching classic bus spawn bounce
                float c1 = overshoot;
                float c3 = c1 + 1f;
                float curve = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                curve = Mathf.Max(0f, curve);

                target.localScale = targetScale * curve;
                yield return null;
            }

            if (target != null)
            {
                target.localScale = targetScale;
            }
        }

        /// <summary>
        /// Immediately cancels any active level start animation, stops start coroutines, and resets scales.
        /// Unblocks input handlers when skipping or restarting levels rapidly.
        /// </summary>
        public void CancelLevelStartAnimation()
        {
            if (activeStartSequence != null)
            {
                StopCoroutine(activeStartSequence);
                activeStartSequence = null;
            }

            isLevelStartAnimating = false;
            RestoreAllElementsToFullScale();

            if (dragHandler != null)
            {
                dragHandler.ResetDragState();
            }
        }

        private void RestoreAllElementsToFullScale()
        {
            // 1. Buses
            for (int i = 0; i < spawnedBuses.Count; i++)
            {
                if (spawnedBuses[i] != null)
                {
                    spawnedBuses[i].CancelSpawnAnimation();
                }
            }

            // 2. Passenger Cells & Figures
            bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);
            Vector3 targetBaseScale = (puzzleGrid != null)
                ? (isHorizontal
                    ? new Vector3(puzzleGrid.CellSize * 0.90f, cellPadThickness, puzzleGrid.CellSize * 0.90f)
                    : new Vector3(puzzleGrid.CellSize * 0.90f, puzzleGrid.CellSize * 0.90f, 0.12f))
                : Vector3.one;

            foreach (var kvp in passengerCells)
            {
                var cell = kvp.Value;
                if (cell == null) continue;

                if (cell.baseRenderer != null)
                {
                    cell.baseRenderer.transform.localScale = targetBaseScale;
                }

                float cellFigScale = (autoScaleCrowdByCount && cell.miniFigures.Count > 6)
                    ? characterScale * denseCrowdScaleFactor
                    : characterScale;
                Vector3 targetFigScale = characterPrefab != null
                    ? Vector3.one * cellFigScale
                    : new Vector3(0.24f, 0.28f, 0.24f);

                for (int f = 0; f < cell.miniFigures.Count; f++)
                {
                    if (cell.miniFigures[f] != null)
                    {
                        cell.miniFigures[f].transform.localScale = targetFigScale;
                    }
                }
            }

            // 3. Destination Station (if present)
            if (puzzleGrid != null)
            {
                Vector3 destPadScale = new Vector3(puzzleGrid.CellSize * 0.96f, puzzleGrid.CellSize * 0.96f, 0.20f);
                foreach (var pad in destinationPadObjects.Values)
                {
                    if (pad != null) pad.transform.localScale = destPadScale;
                }

                if (destinationVisual != null)
                {
                    var arch = destinationVisual.transform.Find("StationCanopy");
                    if (arch != null)
                    {
                        arch.localScale = new Vector3(puzzleGrid.CellSize * 0.88f, 0.20f, 0.35f);
                    }
                }
            }
        }
        #endregion

        public void Cleanup()
        {
            if (activeStartSequence != null)
            {
                StopCoroutine(activeStartSequence);
                activeStartSequence = null;
            }
            isLevelStartAnimating = false;

            if (dragHandler != null)
            {
                dragHandler.ResetDragState();
            }

            StopAllCoroutines();
            passengerCells.Clear();
            destinationCells.Clear();
            destinationPadObjects.Clear();
            destinationCellFigures.Clear();
            nextInteractionTime.Clear();

            for (int i = 0; i < spawnedBuses.Count; i++)
            {
                if (spawnedBuses[i] != null) Destroy(spawnedBuses[i].gameObject);
            }
            spawnedBuses.Clear();

            if (destinationVisual != null)
            {
                Destroy(destinationVisual);
                destinationVisual = null;
            }

            if (visualRoot != null)
            {
                Destroy(visualRoot.gameObject);
                visualRoot = null;
            }
        }

        private void OnDestroy()
        {
            Cleanup();
        }
        #endregion
    
        [ContextMenu("Refresh All Cell Visuals")]
        public void RefreshAllCellVisuals()
        {
            if (passengerCells == null || passengerCells.Count == 0 || puzzleGrid == null) return;

            bool isHorizontal = (GameManager.CurrentOrientation == BoardOrientation.Horizontal_XZ);
            Vector3 padScale = isHorizontal
                ? new Vector3(puzzleGrid.CellSize * 0.90f, cellPadThickness, puzzleGrid.CellSize * 0.90f)
                : new Vector3(puzzleGrid.CellSize * 0.90f, puzzleGrid.CellSize * 0.90f, 0.12f);
            Vector3 padPos = isHorizontal
                ? new Vector3(0f, cellPadElevationY, 0f)
                : new Vector3(0f, 0f, 0.08f);

            Vector3 baseEuler = GetEffectiveBaseEuler();

            foreach (var cell in passengerCells.Values)
            {
                if (cell == null || cell.rootObject == null) continue;

                if (cell.baseRenderer != null)
                {
                    cell.baseRenderer.transform.localScale = padScale;
                    cell.baseRenderer.transform.localPosition = padPos;
                }

                int count = cell.miniFigures.Count;
                if (count == 0) continue;

                Vector3[] positions = GetSymmetricalPositions(count, puzzleGrid.CellSize);
                float figScale = (autoScaleCrowdByCount && count > 6) ? characterScale * denseCrowdScaleFactor : characterScale;

                for (int i = 0; i < count; i++)
                {
                    var fig = cell.miniFigures[i];
                    if (fig == null) continue;

                    if (i < positions.Length) fig.transform.localPosition = positions[i];
                    fig.transform.localScale = Vector3.one * figScale;

                    int seedY = i * 2399 + (cell.gridPos.x * 37 + cell.gridPos.y * 101) * 10007 + 3;
                    int seedZ = i * 4001 + (cell.gridPos.x * 53 + cell.gridPos.y * 131) * 13337 + 5;
                    float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                        ? GetStableRandom(seedY, rotationJitterYRange.x, rotationJitterYRange.y)
                        : 0f;
                    float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                        ? GetStableRandom(seedZ, rotationJitterZRange.x, rotationJitterZRange.y)
                        : 0f;

                    var cp = fig.GetComponent<CharacterPassenger>();
                    if (cp != null)
                    {
                        cp.ApplyRotationJitter(baseEuler, jitterY, jitterZ);
                    }
                    else
                    {
                        fig.transform.localRotation = Quaternion.Euler(baseEuler.x, baseEuler.y + jitterY, baseEuler.z + jitterZ);
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                RefreshAllCellVisuals();
            }
        }
#endif

}
}
