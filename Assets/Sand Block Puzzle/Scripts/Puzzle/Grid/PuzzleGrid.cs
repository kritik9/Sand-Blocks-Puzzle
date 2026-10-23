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
        [SerializeField] private Vector2 origin = new Vector2(-3.4f, -9.2f);

        [Header("Visual Styling")]
        [SerializeField] private bool autoBuildVisuals = true;
        [SerializeField] private Color trayColor = new Color(0.96f, 0.58f, 0.72f, 1f);
        [SerializeField] private Color tileColor = new Color(0.98f, 0.94f, 0.94f, 1f);

        private VehicleBase[,] grid;
        private List<VehicleBase> registeredVehicles = new List<VehicleBase>();
        private GameObject visualRoot;

        public int Columns => columns;
        public int Rows => rows;
        public float CellSize => cellSize;
        public Vector2 Origin => origin;
        public IReadOnlyList<VehicleBase> RegisteredVehicles => registeredVehicles;

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
            grid = new VehicleBase[columns, rows];
            if (autoBuildVisuals)
            {
                BuildVisualGrid();
            }
        }

        private void SafeDestroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }

        public void BuildVisualGrid()
        {
            if (visualRoot != null) SafeDestroy(visualRoot);
            visualRoot = new GameObject("[VisualGrid]");
            visualRoot.transform.SetParent(transform);

            // 1. Board Backing Tray
            float totalW = columns * cellSize + 0.6f;
            float totalH = rows * cellSize + 0.6f;
            Vector3 center = new Vector3(origin.x + (columns - 1) * 0.5f * cellSize, origin.y + (rows - 1) * 0.5f * cellSize, 0.15f);

            var tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tray.name = "BoardTray";
            tray.transform.SetParent(visualRoot.transform);
            tray.transform.position = center;
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
                    Vector3 tilePos = new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0.05f);
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.name = $"Tile_{x}_{y}";
                    tile.transform.SetParent(visualRoot.transform);
                    tile.transform.position = tilePos;
                    tile.transform.localScale = new Vector3(cellSize * 0.92f, cellSize * 0.92f, 0.08f);

                    var col = tile.GetComponent<Collider>();
                    if (col != null) SafeDestroy(col);

                    tile.GetComponent<Renderer>().sharedMaterial = tileMat;
                }
            }
        }

        public Vector3 GridToWorld(Vector2Int gridPos, int length, bool isVertical)
        {
            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            float worldX = origin.x + (gridPos.x + xOffset) * cellSize;
            float worldY = origin.y + (gridPos.y + yOffset) * cellSize;
            return new Vector3(worldX, worldY, 0f);
        }

        public Vector2Int WorldToGrid(Vector3 worldPos, int length = 1, bool isVertical = false)
        {
            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            int gx = Mathf.RoundToInt((worldPos.x - origin.x) / cellSize - xOffset);
            int gy = Mathf.RoundToInt((worldPos.y - origin.y) / cellSize - yOffset);

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
