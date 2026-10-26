using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;
using SandBlocks.Core.Rendering;
using SandBlocks.Core.Simulation;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Input;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Gameplay
{
    public class CrowdTransferController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private PBDCrowdSimulation simulation;
        [SerializeField] private ContainerBoundary boundary;
        [SerializeField] private PuzzleGrid puzzleGrid;
        [SerializeField] private VehicleDragHandler dragHandler;
        [SerializeField] private InstancedCrowdRenderer crowdRenderer;
        [SerializeField] private GameManager gameManager;
        [SerializeField] private CrowdColorPalette palette;
        [SerializeField] private GameObject commuterStreamPrefab;

        [Header("Transfer Tuning")]
        [SerializeField] private float transferInterval = 0.08f;
        [SerializeField] private float flightDuration = 0.35f;
        [SerializeField] private float boundaryLayerTolerance = 0.7f;
        [SerializeField] private float commuterScale = 0.1f;

        private float transferTimer;
        private readonly List<GameObject> inFlightCommuters = new List<GameObject>();

        public void SetCharacterScale(float scale)
        {
            commuterScale = scale;
        }

        public void ClearActiveTransfers()
        {
            StopAllCoroutines();
            for (int i = 0; i < inFlightCommuters.Count; i++)
            {
                if (inFlightCommuters[i] != null)
                {
                    Destroy(inFlightCommuters[i]);
                }
            }
            inFlightCommuters.Clear();
        }

        private void Awake()
        {
            if (simulation == null) simulation = FindAnyObjectByType<PBDCrowdSimulation>();
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (dragHandler == null) dragHandler = FindAnyObjectByType<VehicleDragHandler>();
            if (crowdRenderer == null) crowdRenderer = FindAnyObjectByType<InstancedCrowdRenderer>();
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
            if (crowdRenderer != null) commuterScale = crowdRenderer.CharacterScale;
        }

        private void Update()
        {
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
            if (gameManager != null && gameManager.IsLevelStartAnimating)
            {
                return;
            }

            transferTimer += Time.deltaTime;
            if (transferTimer >= transferInterval)
            {
                transferTimer = 0f;
                ProcessTransfer();
            }
        }

        private void ProcessTransfer()
        {
            if (simulation == null || boundary == null) return;

            VehicleBase bus = GetActiveBusAtTopOfGrid();
            if (bus == null || bus.IsFull) return;

            float bottomY = boundary.BottomY;

            // Only extract if people of this bus's color are currently ON THE BOTTOM BOUNDARY LINE
            if (!simulation.HasColorOnBottomBoundary(bus.ColorId, bottomY, boundaryLayerTolerance))
            {
                return;
            }

            // Extract the matching person closest to the bus horizontally
            if (simulation.TryExtractBottomParticle(bus.ColorId, bottomY, bus.transform.position.x, out Vector3 startPos, boundaryLayerTolerance))
            {
                if (bus is BusVehicle busVehicle)
                {
                    if (busVehicle.TryReserveSeat(out Vector3 seatPos))
                    {
                        StartCoroutine(AnimateCommuterFlight(startPos, busVehicle, seatPos, bus.ColorId));
                    }
                }
                else
                {
                    if (bus.TryAddPassenger(out Vector3 seatPos))
                    {
                        StartCoroutine(AnimateCommuterFlightLegacy(startPos, bus, seatPos, bus.ColorId));
                    }
                }
            }
        }

        private VehicleBase GetActiveBusAtTopOfGrid()
        {
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (dragHandler == null) dragHandler = FindAnyObjectByType<VehicleDragHandler>();

            float bottomY = boundary != null ? boundary.BottomY : -2.5f;

            var candidates = new List<VehicleBase>();

            // 1. Actively hovered/dragged bus has TOP PRIORITY for collection!
            if (dragHandler != null && dragHandler.IsDragging)
            {
                var dragged = dragHandler.HoveredVehicle;
                if (dragged != null && !dragged.IsFull && IsVehicleInCollectionZone(dragged))
                {
                    candidates.Add(dragged);
                }
            }

            // 2. Placed vehicles at the top row of the puzzle grid
            if (puzzleGrid != null)
            {
                int topRow = puzzleGrid.Rows - 1;
                for (int x = 0; x < puzzleGrid.Columns; x++)
                {
                    var v = puzzleGrid.GetVehicleAt(x, topRow);
                    if (v != null && !v.IsFull && !candidates.Contains(v) && IsVehicleInCollectionZone(v))
                    {
                        candidates.Add(v);
                    }
                }
            }

            if (candidates.Count == 0) return null;

            // 3. Select the candidate whose color is waiting on the bottom line of the container
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (simulation != null && simulation.HasColorOnBottomBoundary(c.ColorId, bottomY, boundaryLayerTolerance))
                {
                    return c;
                }
            }

            // Fallback: return the first candidate (e.g. hovered bus)
            return candidates[0];
        }

        private bool IsVehicleInCollectionZone(VehicleBase vehicle)
        {
            if (vehicle == null || vehicle.IsFull) return false;

            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();

            int topRow = puzzleGrid != null ? puzzleGrid.Rows - 1 : 4;
            float cellSize = puzzleGrid != null ? puzzleGrid.CellSize : 1.35f;

            // 1. Grid coordinate check: works for placed vehicles and hovering vehicles whose GridPosition is updated
            int vTopGridY = vehicle.IsVertical 
                ? (vehicle.GridPosition.y + vehicle.Length - 1) 
                : vehicle.GridPosition.y;

            if (vTopGridY == topRow)
            {
                return true;
            }

            // 2. Candidate drop grid check from current world position (for when hovering/dragging)
            if (puzzleGrid != null)
            {
                if (puzzleGrid.TryGetGridDropPosition(vehicle.transform.position, vehicle.Length, vehicle.IsVertical, out Vector2Int cand))
                {
                    int candTopY = vehicle.IsVertical ? (cand.y + vehicle.Length - 1) : cand.y;
                    if (candTopY == topRow)
                    {
                        return true;
                    }
                }
            }

            // 3. World proximity fallback: within half a cell of the top row world Y
            float topRowWorldY = puzzleGrid != null 
                ? (puzzleGrid.Origin.y + topRow * cellSize) 
                : -3.8f;
            float halfSpanY = vehicle.IsVertical ? (vehicle.Length - 1) * 0.5f * cellSize : 0f;
            float busTopWorldY = vehicle.transform.position.y + halfSpanY;

            if (Mathf.Abs(busTopWorldY - topRowWorldY) <= cellSize * 0.65f)
            {
                return true;
            }

            return false;
        }

        private IEnumerator AnimateCommuterFlight(Vector3 start, BusVehicle targetBus, Vector3 targetSeatPos, int colorId)
        {
            float activeScale = crowdRenderer != null ? crowdRenderer.CharacterScale : commuterScale;
            Vector3 baseRot = crowdRenderer != null ? crowdRenderer.CharacterRotationEuler : new Vector3(90f, 0f, 0f);

            GameObject commuter;
            GameObject prefabToUse = commuterStreamPrefab != null ? commuterStreamPrefab : (targetBus != null ? targetBus.PassengerPrefab : null);

            if (prefabToUse != null)
            {
                commuter = Instantiate(prefabToUse, start, Quaternion.Euler(baseRot));
            }
            else
            {
                commuter = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                commuter.transform.position = start;
                var col = commuter.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            inFlightCommuters.Add(commuter);
            try
            {
                commuter.transform.localScale = Vector3.one * activeScale;

                // Manage passenger character animations, color, and colliders
                var charPassenger = commuter.GetComponent<CharacterPassenger>() ?? commuter.AddComponent<CharacterPassenger>();
                charPassenger.PlayRunning();

                if (palette != null)
                {
                    Color c = palette.GetColor(colorId);
                    charPassenger.SetColor(c);
                }

                // Calculate the seat's offset relative to the bus so it dynamically follows if bus is dragged
                Vector3 localSeatOffset = (targetBus != null && targetSeatPos != Vector3.zero)
                    ? targetBus.transform.InverseTransformPoint(targetSeatPos)
                    : Vector3.zero;

                Vector3 endPos = (targetBus != null) ? targetBus.transform.TransformPoint(localSeatOffset) : targetSeatPos;
                Vector3 midPoint = (start + endPos) * 0.5f + Vector3.up * 1.4f - Vector3.forward * 0.5f;
                float elapsed = 0f;

                while (elapsed < flightDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / flightDuration);

                    // Dynamically track the target bus as it moves / hovers
                    if (targetBus != null)
                    {
                        endPos = targetBus.transform.TransformPoint(localSeatOffset);
                    }

                    float oneMinusT = 1f - t;
                    Vector3 currentPos = oneMinusT * oneMinusT * start + 2f * oneMinusT * t * midPoint + t * t * endPos;
                    commuter.transform.position = currentPos;
                    commuter.transform.rotation = Quaternion.Euler(baseRot);
                    yield return null;
                }

                // Enable pre-placed seat passenger on the bus and destroy flying commuter
                if (targetBus != null)
                {
                    targetBus.BoardPassenger();
                }
            }
            finally
            {
                inFlightCommuters.Remove(commuter);
                if (commuter != null)
                {
                    Destroy(commuter);
                }
            }
        }

        private IEnumerator AnimateCommuterFlightLegacy(Vector3 start, VehicleBase targetBus, Vector3 localSeat, int colorId)
        {
            float activeScale = crowdRenderer != null ? crowdRenderer.CharacterScale : commuterScale;
            Vector3 baseRot = crowdRenderer != null ? crowdRenderer.CharacterRotationEuler : new Vector3(90f, 0f, 0f);

            GameObject commuter;
            if (commuterStreamPrefab != null)
            {
                commuter = Instantiate(commuterStreamPrefab, start, Quaternion.Euler(baseRot));
                commuter.transform.localScale = Vector3.one * activeScale;
                var anim = commuter.GetComponentInChildren<Animator>();
                if (anim != null) anim.SetBool("isRunning", true);
                var cols = commuter.GetComponentsInChildren<Collider>();
                for (int c = 0; c < cols.Length; c++) cols[c].enabled = false;
            }
            else
            {
                commuter = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                commuter.transform.position = start;
                commuter.transform.localScale = Vector3.one * 0.45f;
                var col = commuter.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }

            if (palette != null)
            {
                Color c = palette.GetColor(colorId);
                var rends = commuter.GetComponentsInChildren<Renderer>();
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    r.material.color = c;
                }
            }

            Vector3 endPos = targetBus != null ? targetBus.transform.TransformPoint(localSeat) : start;
            Vector3 midPoint = (start + endPos) * 0.5f + Vector3.up * 1.4f - Vector3.forward * 0.5f;
            float elapsed = 0f;

            while (elapsed < flightDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flightDuration);

                if (targetBus != null)
                {
                    endPos = targetBus.transform.TransformPoint(localSeat);
                }

                float oneMinusT = 1f - t;
                Vector3 currentPos = oneMinusT * oneMinusT * start + 2f * oneMinusT * t * midPoint + t * t * endPos;
                commuter.transform.position = currentPos;
                commuter.transform.rotation = Quaternion.Euler(baseRot);
                yield return null;
            }

            Destroy(commuter);
        }
    }
}
