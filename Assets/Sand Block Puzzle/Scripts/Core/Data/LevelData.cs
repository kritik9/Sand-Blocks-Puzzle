using System;

using System.Collections.Generic;

using UnityEngine;

using SandBlocks.Core.Boundary;



namespace SandBlocks.Core.Data

{

    public enum BoardOrientation { Horizontal_XZ = 0, Vertical_XY = 1 }

    public enum GameplayMode

    {

        ClassicTwoArea = 0,

        UnifiedGrid = 1

    }



    public enum UnifiedMode

    {

        DestinationDropOff = 0,

        BusDriveAway = 1

    }



    public enum UnifiedCellType

    {

        Walkable = 0,

        Blocked = 1,

        Passenger = 2,

        Destination = 3,

        Hole = 4

    }



    [Serializable]

    public struct UnifiedCellData

    {

        public Vector2Int gridPosition;

        public UnifiedCellType cellType;

        public int colorId;

        public int passengerCount;

        public bool walkableAfterCollection;

        public bool hideVisual;



        public bool isVisuallyRemoved => cellType == UnifiedCellType.Hole || hideVisual;

    }



    public enum LevelPatternType

    {

        LipsShape = 0,

        TextureMap = 2,

        CustomGrid = 3

    }



    [Serializable]

    public struct BusSpawnData

    {

        public string name;

        public int colorId;

        public int capacity;

        public int length;

        [Tooltip("Fixed rotation: 0 = Right, 90 = Up, 180 = Left, 270 = Down")]

        [Range(0, 360)]

        public int rotationDegrees;

        public Vector2Int gridPosition;



        public bool isVertical => (rotationDegrees == 90 || rotationDegrees == 270);

    }



    [CreateAssetMenu(fileName = "Level_01", menuName = "SandBlocks/Level Data")]

    public class LevelData : ScriptableObject

    {

        [Header("Gameplay Approach")]

        [Tooltip("ClassicTwoArea: Separate crowd container + bus parking grid. UnifiedGrid: Single unified grid containing passengers and buses.")]

        public GameplayMode gameplayMode = GameplayMode.ClassicTwoArea;



        [Header("General Settings")]

        public string levelName = "Level 1";

        [Tooltip("Physics collision radius for crowd particles in this level (e.g. 0.18 to 0.28)")]
        public float particleRadius = 0.28f;

        [Tooltip("Visual scale/size of character models for this level (e.g. 0.009). If 0 or negative, uses the default scale configured in InstancedCrowdRenderer.")]
        public float characterScale = 0f;

        [Tooltip("Vertical row spacing multiplier for this level (e.g. 1.25). If 0 or negative, uses the default multiplier configured in InstancedCrowdRenderer.")]
        public float verticalSpacingMultiplier = 0f;

        public int maxParticles = 800;



        [Header("Container Configuration (Shape & Dimensions)")]

        public ContainerShapeType containerShape = ContainerShapeType.Rectangle;

        public float containerWidth = 5.4f;

        public float containerHeight = 5.8f;

        public Vector2 containerCenter = new Vector2(0f, 0.4f);



        [Header("Border Offsets (Left, Right, Top, Bottom)")]

        [Tooltip("Independent X offset for Left border")]

        public float leftBorderOffset = 0f;

        [Tooltip("Independent X offset for Right border")]

        public float rightBorderOffset = 0f;

        [Tooltip("Independent Y offset for Top border")]

        public float topBorderOffset = 0f;

        [Tooltip("Independent Y offset for Bottom border")]

        public float bottomBorderOffset = 0f;



        [Header("Custom Polygon Points (When Shape = CustomPolygon)")]

        [Tooltip("Local vertices defining the boundary polygon")]

        public List<Vector2> customPolygonPoints = new List<Vector2>();



        [Header("Pattern Type")]

        [Tooltip("How crowd colors are distributed in the container")]

        public LevelPatternType patternType = LevelPatternType.LipsShape;



        [Header("Custom Painted Crowd Grid")]

        public int gridColumns = 16;

        public int gridRows = 16;

        [HideInInspector] public int[] customCrowdColors;



        [Header("Optional Image Color Map (Pixel Art)")]

        [Tooltip("If assigned, spawns crowd colors sampled directly from this texture")]

        public Texture2D colorMapTexture;



        [Header("Puzzle Grid Setup")]

        [Tooltip("Number of columns for this level's puzzle grid (default: 6)")]

        public int puzzleColumns = 6;

        [Tooltip("Number of rows for this level's puzzle grid (default: 5)")]

        public int puzzleRows = 5;



        [Header("Puzzle Grid Buses")]

        public List<BusSpawnData> buses = new List<BusSpawnData>

        {

            new BusSpawnData { name = "Blue Bus",    colorId = 0, capacity = 4, length = 2, rotationDegrees = 0,   gridPosition = new Vector2Int(0, 4) },

            new BusSpawnData { name = "Yellow Bus",  colorId = 2, capacity = 4, length = 2, rotationDegrees = 180, gridPosition = new Vector2Int(3, 4) },

            new BusSpawnData { name = "Black Bus L", colorId = 4, capacity = 4, length = 2, rotationDegrees = 90,  gridPosition = new Vector2Int(0, 2) },

            new BusSpawnData { name = "Blue Bus Mid",colorId = 0, capacity = 4, length = 2, rotationDegrees = 0,   gridPosition = new Vector2Int(2, 3) },

            new BusSpawnData { name = "Black Bus R", colorId = 4, capacity = 4, length = 2, rotationDegrees = 270, gridPosition = new Vector2Int(5, 2) },

            new BusSpawnData { name = "White Bus",   colorId = 5, capacity = 6, length = 3, rotationDegrees = 0,   gridPosition = new Vector2Int(1, 2) },

            new BusSpawnData { name = "Orange Bus",  colorId = 6, capacity = 4, length = 2, rotationDegrees = 0,   gridPosition = new Vector2Int(0, 1) },

            new BusSpawnData { name = "Pink Bus",    colorId = 3, capacity = 4, length = 2, rotationDegrees = 0,   gridPosition = new Vector2Int(2, 1) },

            new BusSpawnData { name = "Green Bus",   colorId = 1, capacity = 4, length = 2, rotationDegrees = 180, gridPosition = new Vector2Int(4, 1) },

            new BusSpawnData { name = "Long White",  colorId = 5, capacity = 10, length = 6, rotationDegrees = 0,  gridPosition = new Vector2Int(0, 0) }

        };



        [Header("Unified Grid Configuration")]

        [Tooltip("Columns for the unified grid")]

        public int unifiedGridColumns = 8;

        [Tooltip("Rows for the unified grid")]

        public int unifiedGridRows = 8;

        [Tooltip("Grid rotation in degrees: 0, 90, 180, or 270")]

        [Range(0, 360)]

        public int unifiedGridRotation = 0;

        [Tooltip("Cell size in world units for the unified grid")]

        public float unifiedCellSize = 1.35f;



        [Header("Unified Grid Gameplay Flow")]

        [Tooltip("Unified Grid gameplay flow: DestinationDropOff (buses drop off crowd at station and are reused) or BusDriveAway (filled buses leave the grid, no destination station)")]

        public UnifiedMode unifiedMode = UnifiedMode.DestinationDropOff;

        [Tooltip("Grid position of the shared destination station for passenger drop-off")]

        public Vector2Int sharedDestinationPos = new Vector2Int(0, 0);



        [Tooltip("Configured cells (blocked obstacles, passenger cells, destination) for the unified grid")]

        public List<UnifiedCellData> unifiedCells = new List<UnifiedCellData>();



        [Tooltip("Buses placed on the unified grid")]

        public List<BusSpawnData> unifiedBuses = new List<BusSpawnData>();



        public List<Vector2Int> GetDestinationCells()

        {

            var list = new List<Vector2Int>();

            if (unifiedMode == UnifiedMode.BusDriveAway) return list;

            if (unifiedCells != null)

            {

                for (int i = 0; i < unifiedCells.Count; i++)

                {

                    if (unifiedCells[i].cellType == UnifiedCellType.Destination)

                    {

                        list.Add(unifiedCells[i].gridPosition);

                    }

                }

            }

            if (list.Count == 0)

            {

                list.Add(sharedDestinationPos);

            }

            return list;

        }



        public UnifiedCellData GetUnifiedCell(Vector2Int pos)

        {

            if (unifiedCells != null)

            {

                for (int i = 0; i < unifiedCells.Count; i++)

                {

                    if (unifiedCells[i].gridPosition == pos) return unifiedCells[i];

                }

            }

            return new UnifiedCellData

            {

                gridPosition = pos,

                cellType = (pos == sharedDestinationPos ? UnifiedCellType.Destination : UnifiedCellType.Walkable),

                colorId = 0,

                passengerCount = 0

            };

        }



        public void SetUnifiedCell(Vector2Int pos, UnifiedCellType type, int colorId, int passengerCount, bool walkableAfterCollection = false, bool hideVisual = false)

        {

            if (unifiedCells == null) unifiedCells = new List<UnifiedCellData>();



            for (int i = 0; i < unifiedCells.Count; i++)

            {

                if (unifiedCells[i].gridPosition == pos)

                {

                    if (type == UnifiedCellType.Walkable)

                    {

                        unifiedCells.RemoveAt(i);

                    }

                    else

                    {

                        var cell = unifiedCells[i];

                        cell.cellType = type;

                        cell.colorId = colorId;

                        cell.passengerCount = passengerCount;

                        cell.walkableAfterCollection = walkableAfterCollection;

                        cell.hideVisual = hideVisual || (type == UnifiedCellType.Hole);

                        unifiedCells[i] = cell;

                    }

                    return;

                }

            }



            if (type != UnifiedCellType.Walkable)

            {

                unifiedCells.Add(new UnifiedCellData

                {

                    gridPosition = pos,

                    cellType = type,

                    colorId = colorId,

                    passengerCount = passengerCount,

                    walkableAfterCollection = walkableAfterCollection,

                    hideVisual = hideVisual || (type == UnifiedCellType.Hole)

                });

            }

        }



        public int GetTotalUnifiedPassengers()

        {

            if (unifiedCells == null) return 0;

            int total = 0;

            for (int i = 0; i < unifiedCells.Count; i++)

            {

                if (unifiedCells[i].cellType == UnifiedCellType.Passenger)

                {

                    total += unifiedCells[i].passengerCount;

                }

            }

            return total;

        }



        public int GetUnifiedPassengersByColor(int cId)

        {

            if (unifiedCells == null) return 0;

            int count = 0;

            for (int i = 0; i < unifiedCells.Count; i++)

            {

                if (unifiedCells[i].cellType == UnifiedCellType.Passenger && unifiedCells[i].colorId == cId)

                {

                    count += unifiedCells[i].passengerCount;

                }

            }

            return count;

        }



        public int GetUnifiedBusCapacityByColor(int cId)

        {

            if (unifiedBuses == null) return 0;

            int cap = 0;

            for (int i = 0; i < unifiedBuses.Count; i++)

            {

                if (unifiedBuses[i].colorId == cId)

                {

                    cap += unifiedBuses[i].capacity;

                }

            }

            return cap;

        }



        public int GetColorForPosition(float y, float x, float u, float v)

        {

            if (patternType == LevelPatternType.CustomGrid && customCrowdColors != null && customCrowdColors.Length > 0)

            {

                int col = Mathf.Clamp(Mathf.FloorToInt(u * gridColumns), 0, gridColumns - 1);

                int row = Mathf.Clamp(Mathf.FloorToInt(v * gridRows), 0, gridRows - 1);

                int idx = row * gridColumns + col;

                if (idx >= 0 && idx < customCrowdColors.Length)

                {

                    return customCrowdColors[idx];

                }

            }



            if (patternType == LevelPatternType.TextureMap && colorMapTexture != null)

            {

                Color px = colorMapTexture.GetPixelBilinear(u, v);

                return MatchClosestColor(px);

            }



            if (patternType == LevelPatternType.LipsShape)

            {

                return EvaluateLipsPattern(u, v);

            }



            // Fallback: white background

            return 5;

        }



        public int CalculateFixedCrowdCapacity()

        {

            float radius = particleRadius > 0.05f ? particleRadius : 0.28f;

            float spacing = radius * 2.05f;

            float rowStep = spacing * 0.866f;



            float effH = Mathf.Max(1f, containerHeight - radius * 2.4f);

            float effW = Mathf.Max(1f, containerWidth - radius * 2.0f);



            int rows = Mathf.FloorToInt(effH / rowStep);

            int cols = Mathf.FloorToInt(effW / spacing);

            return Mathf.Max(10, rows * cols);

        }



        public void EnsureDefaultPolygonPoints()

        {

            if (customPolygonPoints == null) customPolygonPoints = new List<Vector2>();

            if (customPolygonPoints.Count >= 3) return;



            customPolygonPoints.Clear();

            float halfW = containerWidth * 0.5f;

            float halfH = containerHeight * 0.5f;

            Vector2 c = containerCenter;



            customPolygonPoints.Add(new Vector2(c.x - halfW, c.y - halfH));

            customPolygonPoints.Add(new Vector2(c.x + halfW, c.y - halfH));

            customPolygonPoints.Add(new Vector2(c.x + halfW, c.y + halfH));

            customPolygonPoints.Add(new Vector2(c.x - halfW, c.y + halfH));

        }



        public void EnsureCustomGridInitialized()

        {

            int requiredLength = gridColumns * gridRows;

            if (customCrowdColors == null || customCrowdColors.Length != requiredLength)

            {

                customCrowdColors = new int[requiredLength];

                // Default to Lips shape or White

                for (int r = 0; r < gridRows; r++)

                {

                    for (int c = 0; c < gridColumns; c++)

                    {

                        float u = (c + 0.5f) / gridColumns;

                        float v = (r + 0.5f) / gridRows;

                        customCrowdColors[r * gridColumns + c] = EvaluateLipsPattern(u, v);

                    }

                }

            }

        }



        public void ResizeGrid(int newCols, int newRows, bool adjustContainerSize = false, bool stretchResample = false)

        {

            if (newCols == gridColumns && newRows == gridRows && customCrowdColors != null && customCrowdColors.Length == newCols * newRows) return;

            newCols = Mathf.Clamp(newCols, 2, 64);

            newRows = Mathf.Clamp(newRows, 2, 64);

            int[] oldColors = customCrowdColors;

            int oldCols = gridColumns;

            int oldRows = gridRows;



            float cellW = containerWidth / Mathf.Max(1, oldCols);

            float cellH = containerHeight / Mathf.Max(1, oldRows);



            gridColumns = newCols;

            gridRows = newRows;

            customCrowdColors = new int[newCols * newRows];



            if (oldColors == null || oldColors.Length == 0)

            {

                FillAll(5);

                return;

            }



            if (stretchResample)

            {

                for (int r = 0; r < newRows; r++)

                {

                    float v = (r + 0.5f) / newRows;

                    int oldR = Mathf.Clamp(Mathf.FloorToInt(v * oldRows), 0, oldRows - 1);

                    for (int c = 0; c < newCols; c++)

                    {

                        float u = (c + 0.5f) / newCols;

                        int oldC = Mathf.Clamp(Mathf.FloorToInt(u * oldCols), 0, oldCols - 1);

                        customCrowdColors[r * newCols + c] = oldColors[oldR * oldCols + oldC];

                    }

                }

            }

            else

            {

                // Preserve exact cells 1:1 without altering inside cell shape or size

                for (int r = 0; r < newRows; r++)

                {

                    for (int c = 0; c < newCols; c++)

                    {

                        if (r < oldRows && c < oldCols)

                        {

                            customCrowdColors[r * newCols + c] = oldColors[r * oldCols + c];

                        }

                        else

                        {

                            customCrowdColors[r * newCols + c] = 5; // White background

                        }

                    }

                }

            }



            if (adjustContainerSize)

            {

                containerWidth = Mathf.Max(1.0f, newCols * cellW);

                containerHeight = Mathf.Max(1.0f, newRows * cellH);

            }

        }



        public void DeleteRow(int rowToDelete, bool adjustContainerHeight = true)

        {

            if (gridRows <= 2 || customCrowdColors == null) return;

            if (rowToDelete < 0 || rowToDelete >= gridRows) return;



            float cellH = containerHeight / Mathf.Max(1, gridRows);

            int newRows = gridRows - 1;

            int[] newColors = new int[gridColumns * newRows];



            int targetR = 0;

            for (int r = 0; r < gridRows; r++)

            {

                if (r == rowToDelete) continue;

                for (int c = 0; c < gridColumns; c++)

                {

                    newColors[targetR * gridColumns + c] = customCrowdColors[r * gridColumns + c];

                }

                targetR++;

            }



            customCrowdColors = newColors;

            gridRows = newRows;



            if (adjustContainerHeight)

            {

                containerHeight = Mathf.Max(1.0f, newRows * cellH);

            }

        }



        public void DeleteColumn(int colToDelete, bool adjustContainerWidth = true)

        {

            if (gridColumns <= 2 || customCrowdColors == null) return;

            if (colToDelete < 0 || colToDelete >= gridColumns) return;



            float cellW = containerWidth / Mathf.Max(1, gridColumns);

            int newCols = gridColumns - 1;

            int[] newColors = new int[newCols * gridRows];



            for (int r = 0; r < gridRows; r++)

            {

                int targetC = 0;

                for (int c = 0; c < gridColumns; c++)

                {

                    if (c == colToDelete) continue;

                    newColors[r * newCols + targetC] = customCrowdColors[r * gridColumns + c];

                    targetC++;

                }

            }



            customCrowdColors = newColors;

            gridColumns = newCols;



            if (adjustContainerWidth)

            {

                containerWidth = Mathf.Max(1.0f, newCols * cellW);

            }

        }



        public void InsertRow(int insertAtRow, int fillColor = 5, bool adjustContainerHeight = true)

        {

            EnsureCustomGridInitialized();

            insertAtRow = Mathf.Clamp(insertAtRow, 0, gridRows);



            float cellH = containerHeight / Mathf.Max(1, gridRows);

            int newRows = gridRows + 1;

            int[] newColors = new int[gridColumns * newRows];



            int targetR = 0;

            for (int r = 0; r <= gridRows; r++)

            {

                if (r == insertAtRow)

                {

                    for (int c = 0; c < gridColumns; c++)

                    {

                        newColors[targetR * gridColumns + c] = fillColor;

                    }

                    targetR++;

                }



                if (r < gridRows)

                {

                    for (int c = 0; c < gridColumns; c++)

                    {

                        newColors[targetR * gridColumns + c] = customCrowdColors[r * gridColumns + c];

                    }

                    targetR++;

                }

            }



            customCrowdColors = newColors;

            gridRows = newRows;



            if (adjustContainerHeight)

            {

                containerHeight = Mathf.Max(1.0f, newRows * cellH);

            }

        }



        public void InsertColumn(int insertAtCol, int fillColor = 5, bool adjustContainerWidth = true)

        {

            EnsureCustomGridInitialized();

            insertAtCol = Mathf.Clamp(insertAtCol, 0, gridColumns);



            float cellW = containerWidth / Mathf.Max(1, gridColumns);

            int newCols = gridColumns + 1;

            int[] newColors = new int[newCols * gridRows];



            for (int r = 0; r < gridRows; r++)

            {

                int targetC = 0;

                for (int c = 0; c <= gridColumns; c++)

                {

                    if (c == insertAtCol)

                    {

                        newColors[r * newCols + targetC] = fillColor;

                        targetC++;

                    }



                    if (c < gridColumns)

                    {

                        newColors[r * newCols + targetC] = customCrowdColors[r * gridColumns + c];

                        targetC++;

                    }

                }

            }



            customCrowdColors = newColors;

            gridColumns = newCols;



            if (adjustContainerWidth)

            {

                containerWidth = Mathf.Max(1.0f, newCols * cellW);

            }

        }



        public int CountColorPixels(int colorId)

        {

            if (customCrowdColors == null) return 0;

            int count = 0;

            for (int i = 0; i < customCrowdColors.Length; i++)

            {

                if (customCrowdColors[i] == colorId) count++;

            }

            return count;

        }



        public static List<Vector2Int> GetOccupiedCells(BusSpawnData bus)

        {

            var cells = new List<Vector2Int>();

            for (int i = 0; i < bus.length; i++)

            {

                int gx = bus.isVertical ? bus.gridPosition.x : bus.gridPosition.x + i;

                int gy = bus.isVertical ? bus.gridPosition.y + i : bus.gridPosition.y;

                cells.Add(new Vector2Int(gx, gy));

            }

            return cells;

        }



        public void SetPixel(int col, int row, int colorId)

        {

            EnsureCustomGridInitialized();

            if (col >= 0 && col < gridColumns && row >= 0 && row < gridRows)

            {

                customCrowdColors[row * gridColumns + col] = colorId;

            }

        }



        public int GetPixel(int col, int row)

        {

            EnsureCustomGridInitialized();

            if (col >= 0 && col < gridColumns && row >= 0 && row < gridRows)

            {

                return customCrowdColors[row * gridColumns + col];

            }

            return 5;

        }



        public void FillAll(int colorId)

        {

            EnsureCustomGridInitialized();

            for (int i = 0; i < customCrowdColors.Length; i++)

            {

                customCrowdColors[i] = colorId;

            }

        }



        public void BakeLipsShape()

        {

            EnsureCustomGridInitialized();

            for (int r = 0; r < gridRows; r++)

            {

                for (int c = 0; c < gridColumns; c++)

                {

                    float u = (c + 0.5f) / gridColumns;

                    float v = (r + 0.5f) / gridRows;

                    customCrowdColors[r * gridColumns + c] = EvaluateLipsPattern(u, v);

                }

            }

        }







        private int EvaluateLipsPattern(float u, float v)

        {

            float nx = (u - 0.5f) / 0.42f;

            float ny = (v - 0.52f) / 0.42f;



            float rOuter = (nx * nx) + (ny * ny * 1.3f);

            if (rOuter > 1.0f)

            {

                return 5; // White background

            }



            if (ny > 0.35f) return 3; // Pink

            float mouthEllipse = (nx * nx * 1.8f) + (ny * ny * 5.0f);

            if (mouthEllipse < 0.35f)

            {

                return 4; // Black mouth

            }

            if (ny > 0.05f) return 3; // Pink

            if (ny > -0.25f) return 2; // Yellow

            if (ny > -0.55f) return 1; // Green

            return 0; // Blue

        }



        private int MatchClosestColor(Color c)

        {

            if (c.a < 0.1f) return 5;

            float r = c.r, g = c.g, b = c.b;

            if (r > 0.8f && g > 0.8f && b > 0.8f) return 5;

            if (r < 0.25f && g < 0.25f && b < 0.25f) return 4;

            if (b > r && b > g) return 0;

            if (g > r && g > b) return 1;

            if (r > 0.7f && g > 0.6f && b < 0.3f) return 2;

            if (r > 0.7f && g < 0.5f && b > 0.4f) return 3;

            if (r > 0.7f && g > 0.3f && b < 0.2f) return 6;

            return 0;

        }

    }

}

