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
        [SerializeField] private float cellSize = 1.0f;
        [SerializeField] private Vector2 origin = new Vector2(-2.5f, -8f);

        private VehicleBase[,] grid;
        private List<VehicleBase> registeredVehicles = new List<VehicleBase>();

        public int Columns => columns;
        public int Rows => rows;
        public float CellSize => cellSize;
        public IReadOnlyList<VehicleBase> RegisteredVehicles => registeredVehicles;

        private void Awake()
        {
            grid = new VehicleBase[columns, rows];
        }

        public Vector3 GridToWorld(Vector2Int gridPos, int length, bool isVertical)
        {
            float xOffset = isVertical ? 0f : (length - 1) * 0.5f;
            float yOffset = isVertical ? (length - 1) * 0.5f : 0f;

            float worldX = origin.x + (gridPos.x + xOffset) * cellSize;
            float worldY = origin.y + (gridPos.y + yOffset) * cellSize;
            return new Vector3(worldX, worldY, 0f);
        }

        public Vector2Int WorldToGrid(Vector3 worldPos)
        {
            int gx = Mathf.RoundToInt((worldPos.x - origin.x) / cellSize);
            int gy = Mathf.RoundToInt((worldPos.y - origin.y) / cellSize);
            return new Vector2Int(Mathf.Clamp(gx, 0, columns - 1), Mathf.Clamp(gy, 0, rows - 1));
        }

        public bool CanPlace(VehicleBase vehicle, Vector2Int pos)
        {
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
        }

        public void RemoveVehicle(VehicleBase vehicle)
        {
            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y++)
                {
                    if (grid[x, y] == vehicle) grid[x, y] = null;
                }
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
