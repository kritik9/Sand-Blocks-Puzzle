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

        [Header("Grid Rotation & Layout Mode")]
        [SerializeField] private int gridRotationDegrees = 0;
        [SerializeField] private bool centerGridAtZero = false;

        private HashSet<Vector2Int> blockedCells = new HashSet<Vector2Int>();
        private VehicleBase[,] grid;
        private List<VehicleBase> registeredVehicles = new List<VehicleBase>();
        private GameObject visualRoot;

        public event System.Action OnGridDimensionsChanged;

        public int Columns => columns;
        public int Rows => rows;
        public float CellSize => cellSize;
        public Vector2 Origin => origin;
        public IReadOnlyList<VehicleBase> RegisteredVehicles => registeredVehicles;
        public IReadOnlyCollection<Vector2Int> BlockedCells => blockedCells;
        public int GridRotationDegrees => gridRotationDegrees;

        public bool CenterGridAtZero
        {
            get => centerGridAtZero;
            set { centerGridAtZero = value; RecalculateOrigin(); }
        }

        public void SetGridRotation(int degrees)
        {
            gridRotationDegrees = (degrees % 360 + 360) % 360;
            transform.localRotation = Quaternion.Euler(0f, 0f, gridRotationDegrees);
        }

        public bool IsCellBlocked(int x, int y) => blockedCells.Contains(new Vector2Int(x, y));
        public bool IsCellBlocked(Vector2Int pos) => blockedCells.Contains(pos);

        public void SetCellBlocked(int x, int y, bool blocked)
        {
            Vector2Int pos = new Vector2Int(x, y);
            if (blocked) blockedCells.Add(pos);
            else blockedCells.Remove(pos);
        }

        public void ClearBlockedCells()
        {
            blockedCells.Clear();
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

        public float Width => columns * cellSize;
        public float Height => rows * cellSize;
        public float TopY => origin.y + (rows - 1) * cellSize;
        public float BottomY => origin.y;
        public float LeftX => origin.x;
        public float RightX => origin.x + (columns - 1) * cellSize;

        public void RecalculateOrigin()
        {
            if (centerGridAtZero)
            {
                origin.x = -(columns - 1) * 0.5f * cellSize;
                origin.y = -(rows - 1) * 0.5f * cellSize;
            }
            else if (useTopCenterAnchor)
            {
                origin.x = centerX - (columns - 1) * 0.5f * cellSize;
                origin.y = topAnchorY - (rows - 1) * cellSize;
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
            RecalculateOrigin();
            grid = new VehicleBase[columns, rows];
            if (autoBuildVisuals)
            {
                BuildVisualGrid();
            }
        }

        private void OnValidate()
        {
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

            // 1. Board Backing Tray
            float totalW = columns * cellSize + 0.6f;
            float totalH = rows * cellSize + 0.6f;
            Vector3 center = new Vector3(origin.x + (columns - 1) * 0.5f * cellSize, origin.y + (rows - 1) * 0.5f * cellSize, 0.15f);

            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tray.name = "BoardTray";
            tray.transform.SetParent(visualRoot.transform);
            tray.transform.localPosition = center;
            tray.transform.localRotation = Quaternion.identity;
            tray.transform.localScale = new Vector3(totalW, totalH, 0.2f);
            var trayCol = tray.GetComponent<Collider>();
            if (trayCol != null) SafeDestroy(trayCol);

            var trayMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            trayMat.color = trayColor;
            tray.GetComponent<Renderer>().sharedMaterial = trayMat;

            // 2. Individual Grid Tiles
            var tileMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            tileMat.color = tileColor;

            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    bool isBlocked = IsCellBlocked(x, y);
                    Vector3 tilePos = new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, isBlocked ? 0.12f : 0.05f);
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = isBlocked ? $"BlockedTile_{x}_{y}" : $"Tile_{x}_{y}";
                    tile.transform.SetParent(visualRoot.transform);
                    tile.transform.localPosition = tilePos;
                    tile.transform.localRotation = Quaternion.identity;
                    tile.transform.localScale = isBlocked ? new Vector3(cellSize * 0.95f, cellSize * 0.95f, 0.22f) : new Vector3(cellSize * 0.92f, cellSize * 0.92f, 0.08f);

                    var col = tile.GetComponent<Collider>();
                    if (col != null) SafeDestroy(col);

                    if (isBlocked)
                    {
                        var blockedMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                        blockedMat.color = new Color(0.25f, 0.25f, 0.3f, 1f);
                        tile.GetComponent<Renderer>().sharedMaterial = blockedMat;
                    }
                    else
                    {
                        tile.GetComponent<Renderer>().sharedMaterial = tileMat;
                    }
                }
            }
        }

        public Vector3 GridToWorld(Vector2Int gridPos, int length, bool isVertical)
        {
            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            float localX = origin.x + (gridPos.x + xOffset) * cellSize;
            float localY = origin.y + (gridPos.y + yOffset) * cellSize;
            return transform.TransformPoint(new Vector3(localX, localY, 0f));
        }

        public Vector2Int WorldToGrid(Vector3 worldPos, int length = 1, bool isVertical = false)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);

            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            int gx = Mathf.RoundToInt((local.x - origin.x) / cellSize - xOffset);
            int gy = Mathf.RoundToInt((local.y - origin.y) / cellSize - yOffset);

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

            float rawGx = (worldPos.x - origin.x) / cellSize - xOffset;
            float rawGy = (worldPos.y - origin.y) / cellSize - yOffset;

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

                minCoord = GridToWorld(new Vector2Int(pos.x, minGy), vehicle.Length, true).y;
                maxCoord = GridToWorld(new Vector2Int(pos.x, maxGy), vehicle.Length, true).y;
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
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    Vector3 center = new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0f);
                    Gizmos.DrawWireCube(center, new Vector3(cellSize * 0.95f, cellSize * 0.95f, 0.1f));
                }
            }
        }
    }
}
