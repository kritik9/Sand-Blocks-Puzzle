using System.Collections.Generic;

using UnityEngine;

using SandBlocks.Puzzle.Vehicles;



namespace SandBlocks.Puzzle.Grid

{

    public class PuzzleGrid : MonoBehaviour

    {

        [Header("Grid Setup")]

        [SerializeField] private int columns = 6;

        [SerializeField] private int rows = 5;

        [SerializeField] private float cellSize = 1.35f;



        [Header("Anchoring (Grows Downward & Centered)")]

        [Tooltip("If true, automatically computes origin so top row stays anchored at topAnchorY and columns are centered on centerX")]

        [SerializeField] private bool useTopCenterAnchor = true;

        [Tooltip("World Y coordinate where the top row (collection row) is placed")]

        [SerializeField] private float topAnchorY = -3.8f;

        [Tooltip("World X coordinate where the grid center is aligned")]

        [SerializeField] private float centerX = 0f;



        [SerializeField] private Vector2 origin = new Vector2(-3.4f, -9.2f);



        [Header("Visual Styling")]

        [SerializeField] private bool autoBuildVisuals = true;

        [SerializeField] private Color trayColor = new Color(0.96f, 0.58f, 0.72f, 1f);

        [SerializeField] private Color tileColor = new Color(0.98f, 0.94f, 0.94f, 1f);

        [Header("Tile Model & Spacing")]
        [Tooltip("Prefab or model to use for the visual grid tiles (defaults to Tile_v1 from Models folder).")]
        [SerializeField] private GameObject tilePrefab;

        [Tooltip("Base dimension of the tile model mesh in local units (default 2.0 for Tile_v1). Used to normalize tile size to cellSize.")]
        [SerializeField] private float tileBaseMeshSize = 2.0f;

        [Tooltip("Optional custom scale multiplier applied to the visual tile model.")]
        [SerializeField] private Vector3 tileScaleMultiplier = Vector3.one;

        [Tooltip("Spacing between grid columns (X-axis). Defaults to 0 (no spacing).")]
        [SerializeField] private float columnSpacing = 0f;

        [Tooltip("Spacing between grid rows (Z-axis in Horizontal mode, Y-axis in Vertical mode). Defaults to 0 (no spacing).")]
        [SerializeField] private float rowSpacing = 0f;

        [Tooltip("Vertical elevation offset for tiles (Y in horizontal mode, Z in vertical mode).")]
        [SerializeField] private float tileElevation = 0f;

        [Tooltip("Optional additional position offset applied to visual tiles.")]
        [SerializeField] private Vector3 tilePositionOffset = Vector3.zero;

        [Tooltip("If true, applies tileColor to the visual tiles. Enabled by default.")]
        [SerializeField] private bool applyColorToTilePrefab = true;

        public float TileBaseMeshSize
        {
            get => tileBaseMeshSize;
            set { tileBaseMeshSize = Mathf.Max(0.01f, value); if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public GameObject TilePrefab
        {
            get => tilePrefab;
            set { tilePrefab = value; if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public float ColumnSpacing
        {
            get => columnSpacing;
            set { columnSpacing = Mathf.Max(0f, value); RecalculateOrigin(); if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public float RowSpacing
        {
            get => rowSpacing;
            set { rowSpacing = Mathf.Max(0f, value); RecalculateOrigin(); if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public float StepX => cellSize + columnSpacing;
        public float StepY => cellSize + rowSpacing;

        public float TileElevation
        {
            get => tileElevation;
            set { tileElevation = value; if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public Vector3 TilePositionOffset
        {
            get => tilePositionOffset;
            set { tilePositionOffset = value; if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public Vector3 TileScaleMultiplier
        {
            get => tileScaleMultiplier;
            set { tileScaleMultiplier = value; if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public bool ApplyColorToTilePrefab
        {
            get => applyColorToTilePrefab;
            set { applyColorToTilePrefab = value; if (autoBuildVisuals) BuildVisualGrid(); }
        }

        public Color TileColor
        {
            get => tileColor;
            set
            {
                tileColor = value;
                if (autoBuildVisuals) BuildVisualGrid();
            }
        }

        [Header("Tile Hover Highlighting")]
        [Tooltip("If true, highlights grid tiles under a bus when selected and dragged.")]
        [SerializeField] private bool enableTileHoverHighlight = true;

        [Tooltip("If true, tints the hovered tiles with the bus's color. If false, uses defaultHoverTileColor.")]
        [SerializeField] private bool useBusColorForTileHover = true;

        [Tooltip("Default color for tile hover highlighting if bus color is not used or available.")]
        [SerializeField] private Color defaultHoverTileColor = new Color(0.35f, 0.75f, 1.0f, 1f);

        [Tooltip("How strongly the bus color blends with the tile (0 = tile color, 1 = full bus color).")]
        [Range(0.1f, 1.0f)]
        [SerializeField] private float tileHoverColorIntensity = 0.90f;

        [Tooltip("Optional emission glow boost for hovered tiles.")]
        [Range(0.0f, 2.0f)]
        [SerializeField] private float tileHoverEmissionBoost = 0.40f;

        public bool EnableTileHoverHighlight { get => enableTileHoverHighlight; set => enableTileHoverHighlight = value; }
        public bool UseBusColorForTileHover { get => useBusColorForTileHover; set => useBusColorForTileHover = value; }
        public Color DefaultHoverTileColor { get => defaultHoverTileColor; set => defaultHoverTileColor = value; }
        public float TileHoverColorIntensity { get => tileHoverColorIntensity; set => tileHoverColorIntensity = value; }
        public float TileHoverEmissionBoost { get => tileHoverEmissionBoost; set => tileHoverEmissionBoost = value; }

        [Header("Board Backing Tray Customization")]
        [Tooltip("If true, automatically creates and manages the BoardTray backing plate under the grid blocks")]
        [SerializeField] private bool showBoardTray = true;

        [Tooltip("Manual position offset for BoardTray relative to the grid blocks (X = left/right, Y = elevation/height, Z = depth/forward in Horizontal mode)")]
        [SerializeField] private Vector3 trayPositionOffset = Vector3.zero;

        [Tooltip("Extra border padding extending beyond the grid blocks (X = width margin, Y = length margin). Set to (0, 0) for the tray to match the grid blocks exactly with zero offset!")]
        [SerializeField] private Vector2 trayPadding = new Vector2(0.6f, 0.6f);

        [Tooltip("Height / thickness of the BoardTray")]
        [SerializeField] private float trayThickness = 0.2f;

        [Tooltip("Elevation Y offset for BoardTray in Horizontal_XZ mode (default: -0.05 so it sits right under the blocks)")]
        [SerializeField] private float trayElevationY = -0.05f;

        [Tooltip("If true, destroys and recreates the visual grid at runtime in Awake(). If false, keeps whatever manual scene positions or pre-baked visuals you set in the scene!")]
        [SerializeField] private bool rebuildVisualsAtRuntime = false;

        [Tooltip("Optional: reference to an external BoardTray transform in your scene. If assigned, PuzzleGrid will not spawn a separate BoardTray primitive.")]
        [SerializeField] private Transform customBoardTray;

        public bool ShowBoardTray { get => showBoardTray; set { showBoardTray = value; BuildVisualGrid(); } }
        public Vector3 TrayPositionOffset { get => trayPositionOffset; set { trayPositionOffset = value; BuildVisualGrid(); } }
        public Vector2 TrayPadding { get => trayPadding; set { trayPadding = value; BuildVisualGrid(); } }
        public float TrayElevationY { get => trayElevationY; set { trayElevationY = value; BuildVisualGrid(); } }
        public float TrayThickness { get => trayThickness; set { trayThickness = value; BuildVisualGrid(); } }
        public bool RebuildVisualsAtRuntime { get => rebuildVisualsAtRuntime; set => rebuildVisualsAtRuntime = value; }
        public Transform CustomBoardTray { get => customBoardTray; set => customBoardTray = value; }



        [Header("Grid Rotation & Layout Mode")]

        [SerializeField] private int gridRotationDegrees = 0;

        [SerializeField] private bool centerGridAtZero = false;



        private HashSet<Vector2Int> blockedCells = new HashSet<Vector2Int>();

        private HashSet<Vector2Int> hiddenBlockedCells = new HashSet<Vector2Int>();

        private VehicleBase[,] grid;

        private List<VehicleBase> registeredVehicles = new List<VehicleBase>();

        private GameObject visualRoot;
        private readonly Dictionary<Vector2Int, Renderer> tileRendererCache = new Dictionary<Vector2Int, Renderer>();
        private readonly List<Vector2Int> currentHighlightedCells = new List<Vector2Int>();
        private MaterialPropertyBlock hoverTilePropBlock;
        private static readonly int TileBaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TileColorId = Shader.PropertyToID("_Color");
        private static readonly int TileEmissionColorId = Shader.PropertyToID("_EmissionColor");



        public event System.Action OnGridDimensionsChanged;



        public int Columns => columns;

        public int Rows => rows;

        public float CellSize => cellSize;

        public Vector2 Origin => origin;

        public IReadOnlyList<VehicleBase> RegisteredVehicles => registeredVehicles;

        public IReadOnlyCollection<Vector2Int> BlockedCells => blockedCells;

        public IReadOnlyCollection<Vector2Int> HiddenBlockedCells => hiddenBlockedCells;

        public int GridRotationDegrees => gridRotationDegrees;



        public bool CenterGridAtZero

        {

            get => centerGridAtZero;

            set { centerGridAtZero = value; RecalculateOrigin(); }

        }



        public void SetGridRotation(int degrees)
        {
            gridRotationDegrees = (degrees % 360 + 360) % 360;
            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            if (isHorizontal)
            {
                transform.localRotation = Quaternion.Euler(0f, gridRotationDegrees, 0f);
            }
            else
            {
                transform.localRotation = Quaternion.Euler(0f, 0f, gridRotationDegrees);
            }
        }



        public bool IsCellBlocked(int x, int y) => blockedCells.Contains(new Vector2Int(x, y)) || hiddenBlockedCells.Contains(new Vector2Int(x, y));

        public bool IsCellBlocked(Vector2Int pos) => blockedCells.Contains(pos) || hiddenBlockedCells.Contains(pos);

        public bool IsCellHidden(int x, int y) => hiddenBlockedCells.Contains(new Vector2Int(x, y));

        public bool IsCellHidden(Vector2Int pos) => hiddenBlockedCells.Contains(pos);



        public void SetCellBlocked(int x, int y, bool blocked, bool hideVisual = false)

        {

            Vector2Int pos = new Vector2Int(x, y);

            if (blocked)

            {

                if (hideVisual)

                {

                    hiddenBlockedCells.Add(pos);

                    blockedCells.Remove(pos);

                }

                else

                {

                    blockedCells.Add(pos);

                    hiddenBlockedCells.Remove(pos);

                }

            }

            else

            {

                blockedCells.Remove(pos);

                hiddenBlockedCells.Remove(pos);

            }

        }



        public void ClearBlockedCells()

        {

            blockedCells.Clear();

            hiddenBlockedCells.Clear();

        }



        public Vector3 GetTileLocalPosition(int x, int y, bool isBlocked)
        {
            float stepX = cellSize + columnSpacing;
            float stepY = cellSize + rowSpacing;
            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            return isHorizontal
                ? new Vector3(origin.x + x * stepX + tilePositionOffset.x, (isBlocked ? 0.08f : 0.02f) + tileElevation + tilePositionOffset.y, origin.y + y * stepY + tilePositionOffset.z)
                : new Vector3(origin.x + x * stepX + tilePositionOffset.x, origin.y + y * stepY + tilePositionOffset.y, (isBlocked ? 0.12f : 0.05f) + tileElevation + tilePositionOffset.z);
        }

        public void UnblockCell(Vector2Int pos)
        {
            blockedCells.Remove(pos);
            hiddenBlockedCells.Remove(pos);

            if (visualRoot == null)
            {
                Transform existing = transform.Find("[VisualGrid]");
                if (existing != null) visualRoot = existing.gameObject;
            }
            if (visualRoot == null) return;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Vector3 unblockedPos = GetTileLocalPosition(pos.x, pos.y, false);
            Vector3 unblockedScale = GetTileScale(false, isHorizontal);
            Quaternion unblockedRot = isHorizontal ? Quaternion.identity : Quaternion.Euler(-90f, 0f, 0f);

            var tileMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            tileMat.color = tileColor;
            if (tileMat.HasProperty(TileBaseColorId)) tileMat.SetColor(TileBaseColorId, tileColor);
            if (tileMat.HasProperty(TileColorId)) tileMat.SetColor(TileColorId, tileColor);

            Transform blockedTile = visualRoot.transform.Find($"BlockedTile_{pos.x}_{pos.y}");
            Transform existingTile = visualRoot.transform.Find($"Tile_{pos.x}_{pos.y}");

            GameObject targetTile = null;

            if (blockedTile != null)
            {
                blockedTile.name = $"Tile_{pos.x}_{pos.y}";
                Vector3 cur = blockedTile.localPosition;
                if (isHorizontal) cur.y = unblockedPos.y;
                else cur.z = unblockedPos.z;
                blockedTile.localPosition = cur;
                blockedTile.localRotation = unblockedRot;
                blockedTile.localScale = unblockedScale;
                targetTile = blockedTile.gameObject;
            }
            else if (existingTile != null)
            {
                Vector3 cur = existingTile.localPosition;
                if (isHorizontal) cur.y = unblockedPos.y;
                else cur.z = unblockedPos.z;
                existingTile.localPosition = cur;
                existingTile.localRotation = unblockedRot;
                existingTile.localScale = unblockedScale;
                targetTile = existingTile.gameObject;
            }
            else
            {
                // Tile was hidden or not spawned yet: spawn it now so the board floor is always continuous
                if (tilePrefab != null)
                {
                    targetTile = Instantiate(tilePrefab, visualRoot.transform);
                    var cols = targetTile.GetComponentsInChildren<Collider>();
                    for (int c = 0; c < cols.Length; c++) SafeDestroy(cols[c]);
                }
                else
                {
                    targetTile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    var col = targetTile.GetComponent<Collider>();
                    if (col != null) SafeDestroy(col);
                }
                targetTile.name = $"Tile_{pos.x}_{pos.y}";
                targetTile.transform.localPosition = unblockedPos;
                targetTile.transform.localRotation = unblockedRot;
                targetTile.transform.localScale = unblockedScale;
            }

            if (targetTile != null)
            {
                var rend = targetTile.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    if (applyColorToTilePrefab || tilePrefab == null)
                    {
                        rend.sharedMaterial = tileMat;
                    }
                    tileRendererCache[pos] = rend;
                }
            }
        }





        public bool UseTopCenterAnchor

        {

            get => useTopCenterAnchor;

            set { useTopCenterAnchor = value; RecalculateOrigin(); }

        }



        public float TopAnchorY

        {

            get => topAnchorY;

            set { topAnchorY = value; RecalculateOrigin(); }

        }



        public float CenterX

        {

            get => centerX;

            set { centerX = value; RecalculateOrigin(); }

        }



        public float Width => columns * cellSize + Mathf.Max(0, columns - 1) * columnSpacing;

        public float Height => rows * cellSize + Mathf.Max(0, rows - 1) * rowSpacing;

        public float TopY => origin.y + (rows - 1) * StepY;

        public float BottomY => origin.y;

        public float LeftX => origin.x;

        public float RightX => origin.x + (columns - 1) * StepX;

        public void EnsureTilePrefab()
        {
            // Reference-based: tilePrefab is assigned directly via the Inspector
        }

        public Vector3 GetTileScale(bool isBlocked, bool isHorizontal)
        {
            if (tilePrefab != null)
            {
                float blockedScale = isBlocked ? 1.05f : 1.0f;
                return new Vector3(
                    tileScaleMultiplier.x * blockedScale,
                    tileScaleMultiplier.y * (isBlocked ? 1.4f : 1.0f),
                    tileScaleMultiplier.z * blockedScale
                );
            }

            return isHorizontal
                ? (isBlocked ? new Vector3(cellSize * 0.95f, 0.22f, cellSize * 0.95f) : new Vector3(cellSize * 0.92f, 0.06f, cellSize * 0.92f))
                : (isBlocked ? new Vector3(cellSize * 0.95f, cellSize * 0.95f, 0.22f) : new Vector3(cellSize * 0.92f, 0.08f, cellSize * 0.92f));
        }

        public void RecalculateOrigin()
        {
            float stepX = StepX;
            float stepY = StepY;

            if (centerGridAtZero)
            {
                origin.x = -(columns - 1) * 0.5f * stepX;
                origin.y = -(rows - 1) * 0.5f * stepY;
            }
            else if (useTopCenterAnchor)
            {
                origin.x = centerX - (columns - 1) * 0.5f * stepX;
                origin.y = topAnchorY - (rows - 1) * stepY;
            }

            OnGridDimensionsChanged?.Invoke();
        }



        public void SetGridDimensions(int newCols, int newRows)

        {

            columns = Mathf.Max(2, newCols);

            rows = Mathf.Max(2, newRows);

            RecalculateOrigin();

            grid = new VehicleBase[columns, rows];

            if (autoBuildVisuals)

            {

                BuildVisualGrid();

            }

            OnGridDimensionsChanged?.Invoke();

        }



        public void AutoAlignWithBoundary(SandBlocks.Core.Boundary.ContainerBoundary boundary, float verticalGap = 0.9f)

        {

            if (boundary != null)

            {

                centerX = boundary.transform.position.x + boundary.ContainerCenter.x;

                topAnchorY = boundary.BottomY - verticalGap;

                RecalculateOrigin();

                if (autoBuildVisuals)

                {

                    BuildVisualGrid();

                }

            }

        }



        public VehicleBase GetVehicleAt(int x, int y)

        {

            if (grid != null && x >= 0 && x < columns && y >= 0 && y < rows)

            {

                return grid[x, y];

            }

            return null;

        }



        private void Awake()

        {
            EnsureTilePrefab();

            RecalculateOrigin();

            grid = new VehicleBase[columns, rows];

            // Reconnect to existing visual root in scene if present
            if (visualRoot == null)
            {
                Transform existing = transform.Find("[VisualGrid]");
                if (existing != null) visualRoot = existing.gameObject;
            }

            // Only rebuild at runtime if explicitly requested OR if no visual grid exists yet
            if (rebuildVisualsAtRuntime || (autoBuildVisuals && visualRoot == null))
            {
                BuildVisualGrid();
            }

            RefreshTileCache();

        }



        private void OnValidate()

        {
            EnsureTilePrefab();

            RecalculateOrigin();

#if UNITY_EDITOR

            if (autoBuildVisuals && !Application.isPlaying)

            {

                UnityEditor.EditorApplication.delayCall += () =>

                {

                    if (this != null && autoBuildVisuals && !Application.isPlaying)

                    {

                        BuildVisualGrid();

                    }

                };

            }

#endif

        }



        private void SafeDestroy(Object obj)

        {

            if (obj == null) return;

            if (Application.isPlaying) Destroy(obj);

            else DestroyImmediate(obj);

        }



        [ContextMenu("Rebuild Visual Grid")]
        public void BuildVisualGrid()
        {
            // 1. Destroy all existing visual grid instances under this transform
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.name.Contains("[VisualGrid]"))
                {
                    SafeDestroy(child.gameObject);
                }
            }
            visualRoot = null;

            visualRoot = new GameObject("[VisualGrid]");
            visualRoot.transform.SetParent(transform);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

            EnsureTilePrefab();

            float stepX = StepX;
            float stepY = StepY;

            // 1. Board Backing Tray
            if (showBoardTray && customBoardTray == null)
            {
                float totalW = columns * cellSize + Mathf.Max(0, columns - 1) * columnSpacing + trayPadding.x;
                float totalH = rows * cellSize + Mathf.Max(0, rows - 1) * rowSpacing + trayPadding.y;
                Vector3 center = isHorizontal
                    ? new Vector3(origin.x + (columns - 1) * 0.5f * stepX + trayPositionOffset.x, trayElevationY + trayPositionOffset.y, origin.y + (rows - 1) * 0.5f * stepY + trayPositionOffset.z)
                    : new Vector3(origin.x + (columns - 1) * 0.5f * stepX + trayPositionOffset.x, origin.y + (rows - 1) * 0.5f * stepY + trayPositionOffset.y, 0.15f + trayPositionOffset.z);

                var trayMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                trayMat.color = trayColor;

                if (hiddenBlockedCells.Count == 0)
                {
                    var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tray.name = "BoardTray";
                    tray.transform.SetParent(visualRoot.transform);
                    tray.transform.localPosition = center;
                    tray.transform.localRotation = Quaternion.identity;
                    tray.transform.localScale = isHorizontal ? new Vector3(totalW, trayThickness, totalH) : new Vector3(totalW, totalH, trayThickness);
                    var trayCol = tray.GetComponent<Collider>();
                    if (trayCol != null) SafeDestroy(trayCol);
                    tray.GetComponent<Renderer>().sharedMaterial = trayMat;
                }
                else
                {
                    // Custom grid shape: spawn backing tray segments ONLY under non-hidden cells
                    for (int x = 0; x < columns; x++)
                    {
                        for (int y = 0; y < rows; y++)
                        {
                            if (IsCellHidden(x, y)) continue;

                            Vector3 trayPos = isHorizontal
                                ? new Vector3(origin.x + x * stepX + trayPositionOffset.x, trayElevationY + trayPositionOffset.y, origin.y + y * stepY + trayPositionOffset.z)
                                : new Vector3(origin.x + x * stepX + trayPositionOffset.x, origin.y + y * stepY + trayPositionOffset.y, 0.15f + trayPositionOffset.z);
                            var traySeg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            traySeg.name = $"TraySeg_{x}_{y}";
                            traySeg.transform.SetParent(visualRoot.transform);
                            traySeg.transform.localPosition = trayPos;
                            traySeg.transform.localRotation = Quaternion.identity;
                            traySeg.transform.localScale = isHorizontal ? new Vector3(cellSize + trayPadding.x / columns, trayThickness, cellSize + trayPadding.y / rows) : new Vector3(cellSize + trayPadding.x / columns, cellSize + trayPadding.y / rows, trayThickness);
                            var segCol = traySeg.GetComponent<Collider>();
                            if (segCol != null) SafeDestroy(segCol);
                            traySeg.GetComponent<Renderer>().sharedMaterial = trayMat;
                        }
                    }
                }
            }

            var tileMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            tileMat.color = tileColor;
            if (tileMat.HasProperty(TileBaseColorId)) tileMat.SetColor(TileBaseColorId, tileColor);
            if (tileMat.HasProperty(TileColorId)) tileMat.SetColor(TileColorId, tileColor);

            var blockedMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            Color bCol = new Color(0.25f, 0.25f, 0.3f, 1f);
            blockedMat.color = bCol;
            if (blockedMat.HasProperty(TileBaseColorId)) blockedMat.SetColor(TileBaseColorId, bCol);
            if (blockedMat.HasProperty(TileColorId)) blockedMat.SetColor(TileColorId, bCol);

            // 2. Individual Grid Tiles
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (IsCellHidden(x, y))
                    {
                        // Visually removed from the grid entirely: no tile is spawned
                        continue;
                    }

                    bool isBlocked = IsCellBlocked(x, y);
                    Vector3 tilePos = GetTileLocalPosition(x, y, isBlocked);

                    GameObject tile;
                    if (tilePrefab != null)
                    {
                        tile = Instantiate(tilePrefab, visualRoot.transform);
                        var cols = tile.GetComponentsInChildren<Collider>();
                        for (int c = 0; c < cols.Length; c++) SafeDestroy(cols[c]);
                    }
                    else
                    {
                        tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        var col = tile.GetComponent<Collider>();
                        if (col != null) SafeDestroy(col);
                    }

                    tile.name = isBlocked ? $"BlockedTile_{x}_{y}" : $"Tile_{x}_{y}";
                    tile.transform.localPosition = tilePos;
                    tile.transform.localRotation = isHorizontal ? Quaternion.identity : Quaternion.Euler(-90f, 0f, 0f);
                    tile.transform.localScale = GetTileScale(isBlocked, isHorizontal);

                    var rend = tile.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        if (isBlocked)
                        {
                            rend.sharedMaterial = blockedMat;
                        }
                        else if (applyColorToTilePrefab || tilePrefab == null)
                        {
                            rend.sharedMaterial = tileMat;
                        }
                    }
                }
            }

            RefreshTileCache();
        }

        [ContextMenu("Clear Visual Grid")]
        public void ClearVisualGrid()
        {
            tileRendererCache.Clear();
            currentHighlightedCells.Clear();

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.name.Contains("[VisualGrid]"))
                {
                    SafeDestroy(child.gameObject);
                }
            }
            visualRoot = null;
        }

        public Renderer GetTileRenderer(int x, int y)
        {
            Vector2Int key = new Vector2Int(x, y);
            if (tileRendererCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            if (visualRoot == null)
            {
                Transform existing = transform.Find("[VisualGrid]");
                if (existing != null) visualRoot = existing.gameObject;
            }

            if (visualRoot != null)
            {
                Transform tileTrans = visualRoot.transform.Find($"Tile_{x}_{y}") ?? visualRoot.transform.Find($"BlockedTile_{x}_{y}");
                if (tileTrans != null)
                {
                    var rend = tileTrans.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        tileRendererCache[key] = rend;
                        return rend;
                    }
                }
            }

            return null;
        }

        public void RefreshTileCache()
        {
            tileRendererCache.Clear();
            if (visualRoot == null)
            {
                Transform existing = transform.Find("[VisualGrid]");
                if (existing != null) visualRoot = existing.gameObject;
            }

            if (visualRoot != null)
            {
                for (int x = 0; x < columns; x++)
                {
                    for (int y = 0; y < rows; y++)
                    {
                        Transform t = visualRoot.transform.Find($"Tile_{x}_{y}") ?? visualRoot.transform.Find($"BlockedTile_{x}_{y}");
                        if (t != null)
                        {
                            var rend = t.GetComponentInChildren<Renderer>();
                            if (rend != null)
                            {
                                tileRendererCache[new Vector2Int(x, y)] = rend;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Highlights the tiles currently hovered/occupied by a vehicle during selection/dragging.
        /// </summary>
        public void HighlightHoverTiles(VehicleBase vehicle, Vector2Int gridPos)
        {
            if (!enableTileHoverHighlight || vehicle == null)
            {
                ClearHoverTileHighlights();
                return;
            }

            Color highlightColor = defaultHoverTileColor;
            if (useBusColorForTileHover)
            {
                Color vColor = vehicle.VehicleColor;
                if (vColor.a > 0.01f)
                {
                    // Check if the bus is white (or very light near-white)
                    if (vColor.r > 0.85f && vColor.g > 0.85f && vColor.b > 0.85f)
                    {
                        highlightColor = Color.white;
                    }
                    else
                    {
                        highlightColor = Color.Lerp(tileColor, vColor, tileHoverColorIntensity);
                    }
                }
                else
                {
                    highlightColor = defaultHoverTileColor;
                }
            }

            HighlightHoverTiles(gridPos, vehicle.Length, vehicle.IsVertical, highlightColor);
        }

        public void HighlightHoverTiles(Vector2Int gridPos, int length, bool isVertical, Color highlightColor)
        {
            if (!enableTileHoverHighlight) return;

            List<Vector2Int> newCells = new List<Vector2Int>(length);
            for (int i = 0; i < length; i++)
            {
                int cx = isVertical ? gridPos.x : (gridPos.x + i);
                int cy = isVertical ? (gridPos.y + i) : gridPos.y;
                if (cx >= 0 && cx < columns && cy >= 0 && cy < rows)
                {
                    newCells.Add(new Vector2Int(cx, cy));
                }
            }

            // Quick exit if hovered cells have not changed
            if (AreCellsEqual(currentHighlightedCells, newCells))
            {
                return;
            }

            // Reset previous cells that are no longer hovered
            for (int i = 0; i < currentHighlightedCells.Count; i++)
            {
                Vector2Int oldCell = currentHighlightedCells[i];
                if (!newCells.Contains(oldCell))
                {
                    ResetTileColor(oldCell);
                }
            }

            // Apply highlight to new cells
            if (hoverTilePropBlock == null) hoverTilePropBlock = new MaterialPropertyBlock();
            hoverTilePropBlock.SetColor(TileBaseColorId, highlightColor);
            hoverTilePropBlock.SetColor(TileColorId, highlightColor);

            // Give white/light highlights an elevated emission boost so they clearly pop against the tile floor
            bool isWhiteOrLight = (highlightColor.r > 0.85f && highlightColor.g > 0.85f && highlightColor.b > 0.85f);
            float emissionBoost = isWhiteOrLight 
                ? Mathf.Max(tileHoverEmissionBoost, 0.55f) 
                : tileHoverEmissionBoost;

            if (emissionBoost > 0.001f)
            {
                hoverTilePropBlock.SetColor(TileEmissionColorId, highlightColor * emissionBoost);
            }

            for (int i = 0; i < newCells.Count; i++)
            {
                Vector2Int cell = newCells[i];
                var rend = GetTileRenderer(cell.x, cell.y);
                if (rend != null)
                {
                    rend.SetPropertyBlock(hoverTilePropBlock);
                }
            }

            currentHighlightedCells.Clear();
            currentHighlightedCells.AddRange(newCells);
        }

        /// <summary>
        /// Clears all active tile hover highlights back to their default appearance.
        /// </summary>
        public void ClearHoverTileHighlights()
        {
            for (int i = 0; i < currentHighlightedCells.Count; i++)
            {
                ResetTileColor(currentHighlightedCells[i]);
            }
            currentHighlightedCells.Clear();

            // Safeguard: ensure every cached tile renderer has its property block reset
            foreach (var kvp in tileRendererCache)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.SetPropertyBlock(null);
                }
            }
        }

        private void ResetTileColor(Vector2Int cell)
        {
            var rend = GetTileRenderer(cell.x, cell.y);
            if (rend != null)
            {
                rend.SetPropertyBlock(null);
            }
        }

        private bool AreCellsEqual(List<Vector2Int> a, List<Vector2Int> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        public Vector3 GridToWorld(Vector2Int gridPos, int length, bool isVertical)
        {
            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            float localX = origin.x + (gridPos.x + xOffset) * StepX;
            float localY = origin.y + (gridPos.y + yOffset) * StepY;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            if (isHorizontal)
            {
                return transform.TransformPoint(new Vector3(localX, 0.1f, localY));
            }
            return transform.TransformPoint(new Vector3(localX, localY, 0f));
        }



        public Vector2Int WorldToGrid(Vector3 worldPos, int length = 1, bool isVertical = false)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);

            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            float posX = local.x;
            float posY = isHorizontal ? local.z : local.y;

            int gx = Mathf.RoundToInt((posX - origin.x) / StepX - xOffset);
            int gy = Mathf.RoundToInt((posY - origin.y) / StepY - yOffset);



            int maxGx = columns - (isVertical ? 1 : length);

            int maxGy = rows - (isVertical ? length : 1);



            return new Vector2Int(

                Mathf.Clamp(gx, 0, Mathf.Max(0, maxGx)),

                Mathf.Clamp(gy, 0, Mathf.Max(0, maxGy))

            );

        }



        public bool TryGetGridDropPosition(Vector3 worldPos, int length, bool isVertical, out Vector2Int gridPos)
        {
            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Vector3 local = transform.InverseTransformPoint(worldPos);
            float posX = local.x;
            float posY = isHorizontal ? local.z : local.y;

            float rawGx = (posX - origin.x) / StepX - xOffset;
            float rawGy = (posY - origin.y) / StepY - yOffset;



            int maxGx = columns - (isVertical ? 1 : length);

            int maxGy = rows - (isVertical ? length : 1);



            if (rawGx >= -0.75f && rawGx <= maxGx + 0.75f && rawGy >= -0.75f && rawGy <= maxGy + 0.75f)

            {

                int gx = Mathf.Clamp(Mathf.RoundToInt(rawGx), 0, Mathf.Max(0, maxGx));

                int gy = Mathf.Clamp(Mathf.RoundToInt(rawGy), 0, Mathf.Max(0, maxGy));

                gridPos = new Vector2Int(gx, gy);

                return true;

            }



            gridPos = Vector2Int.zero;

            return false;

        }



        public bool CanPlace(VehicleBase vehicle, Vector2Int pos)

        {

            int reqCols = vehicle.IsVertical ? 1 : vehicle.Length;

            int reqRows = vehicle.IsVertical ? vehicle.Length : 1;



            if (pos.x < 0 || pos.x + reqCols > columns) return false;

            if (pos.y < 0 || pos.y + reqRows > rows) return false;



            for (int i = 0; i < vehicle.Length; i++)

            {

                int gx = vehicle.IsVertical ? pos.x : pos.x + i;

                int gy = vehicle.IsVertical ? pos.y + i : pos.y;



                if (gx < 0 || gx >= columns || gy < 0 || gy >= rows) return false;

                if (IsCellBlocked(gx, gy)) return false;



                VehicleBase occupant = grid[gx, gy];

                if (occupant != null && occupant != vehicle) return false;

            }

            return true;

        }



        public void PlaceVehicle(VehicleBase vehicle, Vector2Int pos)

        {

            RemoveVehicle(vehicle);

            vehicle.GridPosition = pos;



            for (int i = 0; i < vehicle.Length; i++)

            {

                int gx = vehicle.IsVertical ? pos.x : pos.x + i;

                int gy = vehicle.IsVertical ? pos.y + i : pos.y;

                if (gx >= 0 && gx < columns && gy >= 0 && gy < rows)

                {

                    grid[gx, gy] = vehicle;

                }

            }



            if (!registeredVehicles.Contains(vehicle)) registeredVehicles.Add(vehicle);

            vehicle.transform.position = GridToWorld(pos, vehicle.Length, vehicle.IsVertical);



            // Align rotation with orientation

            if (vehicle is BusVehicle bus)

            {

                bus.ApplyRotation();

            }

            else if (vehicle.IsVertical)

            {

                vehicle.transform.rotation = Quaternion.Euler(0f, 0f, 90f);

            }

            else

            {

                vehicle.transform.rotation = Quaternion.identity;

            }

        }



        public void RemoveVehicle(VehicleBase vehicle)

        {

            if (vehicle == null) return;

            for (int x = 0; x < columns; x++)

            {

                for (int y = 0; y < rows; y++)

                {

                    if (grid != null && grid[x, y] == vehicle) grid[x, y] = null;

                }

            }

            registeredVehicles.Remove(vehicle);

            if (vehicle.IsFull || (vehicle is SandBlocks.Puzzle.Vehicles.BusVehicle bv && bv.IsDrivingAway))
            {
                ClearHoverTileHighlights();
            }
        }



        /// <summary>

        /// Computes all grid positions the vehicle can navigate to through contiguous empty cells from startPos using BFS.

        /// </summary>

        public HashSet<Vector2Int> GetReachablePositions(VehicleBase vehicle, Vector2Int startPos)

        {

            var reachable = new HashSet<Vector2Int>();

            if (vehicle == null) return reachable;



            var queue = new Queue<Vector2Int>();

            queue.Enqueue(startPos);

            reachable.Add(startPos);



            Vector2Int[] directions = new Vector2Int[]

            {

                Vector2Int.up,

                Vector2Int.down,

                Vector2Int.left,

                Vector2Int.right

            };



            while (queue.Count > 0)

            {

                Vector2Int curr = queue.Dequeue();



                for (int i = 0; i < directions.Length; i++)

                {

                    Vector2Int next = curr + directions[i];

                    if (!reachable.Contains(next) && CanPlace(vehicle, next))

                    {

                        reachable.Add(next);

                        queue.Enqueue(next);

                    }

                }

            }



            return reachable;

        }



        /// <summary>

        /// Finds the shortest collision-free grid path (list of waypoints) from startPos to targetPos for the given vehicle.

        /// Returns an empty list if target is unreachable.

        /// </summary>

        public List<Vector2Int> FindPath(VehicleBase vehicle, Vector2Int startPos, Vector2Int targetPos)

        {

            var path = new List<Vector2Int>();

            if (vehicle == null) return path;



            if (startPos == targetPos)

            {

                path.Add(startPos);

                return path;

            }



            if (!CanPlace(vehicle, targetPos) && targetPos != startPos)

            {

                return path;

            }



            var queue = new Queue<Vector2Int>();

            var parent = new Dictionary<Vector2Int, Vector2Int>();



            queue.Enqueue(startPos);

            parent[startPos] = startPos;



            Vector2Int[] directions = new Vector2Int[]

            {

                Vector2Int.up,

                Vector2Int.down,

                Vector2Int.left,

                Vector2Int.right

            };



            bool found = false;

            while (queue.Count > 0)

            {

                Vector2Int curr = queue.Dequeue();

                if (curr == targetPos)

                {

                    found = true;

                    break;

                }



                for (int i = 0; i < directions.Length; i++)

                {

                    Vector2Int next = curr + directions[i];

                    if (!parent.ContainsKey(next) && CanPlace(vehicle, next))

                    {

                        parent[next] = curr;

                        queue.Enqueue(next);

                    }

                }

            }



            if (!found) return path;



            Vector2Int step = targetPos;

            while (step != startPos)

            {

                path.Add(step);

                step = parent[step];

            }

            path.Add(startPos);

            path.Reverse();



            return path;

        }



        /// <summary>

        /// Finds the closest reachable grid position to the desired position.

        /// </summary>

        public Vector2Int GetClosestReachablePosition(HashSet<Vector2Int> reachable, Vector2Int desired)

        {

            if (reachable == null || reachable.Count == 0) return desired;

            if (reachable.Contains(desired)) return desired;



            Vector2Int bestPos = desired;

            float bestDistSq = float.MaxValue;



            foreach (var pos in reachable)

            {

                float dx = pos.x - desired.x;

                float dy = pos.y - desired.y;

                float distSq = dx * dx + dy * dy;



                if (distSq < bestDistSq)

                {

                    bestDistSq = distSq;

                    bestPos = pos;

                }

            }



            return bestPos;

        }



        /// <summary>

        /// Checks if there is a contiguous collision-free path between fromPos and toPos.

        /// </summary>

        public bool HasFreePath(VehicleBase vehicle, Vector2Int fromPos, Vector2Int toPos)

        {

            if (vehicle == null) return false;

            if (fromPos == toPos) return true;



            var reachable = GetReachablePositions(vehicle, fromPos);

            return reachable.Contains(toPos);

        }



        public void Clear()

        {

            for (int i = 0; i < registeredVehicles.Count; i++)

            {

                if (registeredVehicles[i] != null)

                {

                    SafeDestroy(registeredVehicles[i].gameObject);

                }

            }

            registeredVehicles.Clear();



            if (grid != null)

            {

                for (int x = 0; x < columns; x++)

                {

                    for (int y = 0; y < rows; y++)

                    {

                        grid[x, y] = null;

                    }

                }

            }

        }



        public void GetMoveLimitsWorld(VehicleBase vehicle, out float minCoord, out float maxCoord)

        {

            Vector2Int pos = vehicle.GridPosition;



            if (vehicle.IsVertical)

            {

                int minGy = pos.y;

                while (minGy > 0 && (grid[pos.x, minGy - 1] == null || grid[pos.x, minGy - 1] == vehicle))

                {

                    minGy--;

                }



                int maxGy = pos.y;

                while (maxGy + vehicle.Length < rows && (grid[pos.x, maxGy + vehicle.Length] == null || grid[pos.x, maxGy + vehicle.Length] == vehicle))

                {

                    maxGy++;

                }



                bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
                minCoord = isHorizontal ? GridToWorld(new Vector2Int(pos.x, minGy), vehicle.Length, true).z : GridToWorld(new Vector2Int(pos.x, minGy), vehicle.Length, true).y;
                maxCoord = isHorizontal ? GridToWorld(new Vector2Int(pos.x, maxGy), vehicle.Length, true).z : GridToWorld(new Vector2Int(pos.x, maxGy), vehicle.Length, true).y;

            }

            else

            {

                int minGx = pos.x;

                while (minGx > 0 && (grid[minGx - 1, pos.y] == null || grid[minGx - 1, pos.y] == vehicle))

                {

                    minGx--;

                }



                int maxGx = pos.x;

                while (maxGx + vehicle.Length < columns && (grid[maxGx + vehicle.Length, pos.y] == null || grid[maxGx + vehicle.Length, pos.y] == vehicle))

                {

                    maxGx++;

                }



                minCoord = GridToWorld(new Vector2Int(minGx, pos.y), vehicle.Length, false).x;

                maxCoord = GridToWorld(new Vector2Int(maxGx, pos.y), vehicle.Length, false).x;

            }

        }



        private void OnDrawGizmos()
        {
            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (isHorizontal)
                    {
                        Vector3 center = new Vector3(origin.x + x * StepX, 0.05f, origin.y + y * StepY);
                        Gizmos.DrawWireCube(center, new Vector3(cellSize * 0.95f, 0.02f, cellSize * 0.95f));
                    }
                    else
                    {
                        Vector3 center = new Vector3(origin.x + x * StepX, origin.y + y * StepY, 0f);
                        Gizmos.DrawWireCube(center, new Vector3(cellSize * 0.95f, cellSize * 0.95f, 0.1f));
                    }
                }
            }
        }
    }
}
