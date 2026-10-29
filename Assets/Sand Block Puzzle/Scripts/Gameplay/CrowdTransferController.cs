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
        [SerializeField] private float commuterScale = 0.08f;

        [Header("Jump Flight Arc & Camera Visibility")]
        [Tooltip("Vertical height added to the jump flight trajectory curve")]
        [SerializeField] private float jumpArcHeight = 2.4f;
        [Tooltip("Forward offset (-Z toward camera) added to flight midpoint so character stays in prominent view")]
        [SerializeField] private float jumpForwardBias = 0.75f;
        [Tooltip("Maximum pitch angle (degrees) tilting feet forward toward camera during landing so whole character is visible from top-down camera")]
        [SerializeField] private float landingFeetForwardTilt = 38f;

        [Header("Gizmos & Debug")]
        [Tooltip("Draw gizmo lines in the Scene View showing container floor and tolerance threshold line")]
        [SerializeField] private bool showToleranceGizmo = true;
        [Tooltip("If true, only draws gizmos when this GameObject is selected. If false, always draws in Scene View.")]
        [SerializeField] private bool showGizmoOnlyWhenSelected = false;

        private float transferTimer;
        private readonly List<GameObject> inFlightCommuters = new List<GameObject>();

        /// <summary>
        /// The number of commuters currently flying from the container crowd to the buses.
        /// </summary>
        public int InFlightCount => inFlightCommuters.Count;

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
            if (gameManager != null && (!gameManager.IsGameStarted || gameManager.IsLevelStartAnimating))
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

            var candidates = GetActiveBusesAtTopOfGrid();
            if (candidates == null || candidates.Count == 0) return;

            float bottomY = boundary.BottomY;

            for (int i = 0; i < candidates.Count; i++)
            {
                var bus = candidates[i];
                if (bus == null || bus.IsFull) continue;

                if (!TryGetBusCollectionRange(bus, out float minX, out float maxX))
                {
                    continue;
                }

                // Only extract if people of this bus's color are currently ON THE BOTTOM BOUNDARY LINE
                // and strictly within this bus's column percentage slice [minX, maxX]
                if (!simulation.HasColorOnBottomBoundary(bus.ColorId, bottomY, minX, maxX, boundaryLayerTolerance))
                {
                    continue;
                }

                // Extract matching person within [minX, maxX] closest to bus center
                float busCenterX = bus.transform.position.x;
                if (simulation.TryExtractBottomParticle(bus.ColorId, bottomY, minX, maxX, busCenterX, out Vector3 startPos, boundaryLayerTolerance))
                {
                    if (bus is BusVehicle busVehicle)
                    {
                        if (busVehicle.TryReserveSeat(out Vector3 seatPos))
                        {
                            StartCoroutine(AnimateCommuterFlight(startPos, busVehicle, seatPos, bus.ColorId));
                            break;
                        }
                    }
                    else
                    {
                        if (bus.TryAddPassenger(out Vector3 seatPos))
                        {
                            StartCoroutine(AnimateCommuterFlightLegacy(startPos, bus, seatPos, bus.ColorId));
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Calculates the horizontal percentage of the crowd area covered by the bus based on its grid columns.
        /// A horizontal bus covering columns [colMin, colMax] covers [colMin/totalCols, (colMax+1)/totalCols].
        /// A vertical bus covering 1 column covers [col/totalCols, (col+1)/totalCols].
        /// </summary>
        public bool TryGetBusCollectionRange(VehicleBase bus, out float minX, out float maxX)
        {
            minX = 0f;
            maxX = 0f;
            if (bus == null) return false;

            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (puzzleGrid == null || boundary == null) return false;

            int totalCols = puzzleGrid.Columns > 0 ? puzzleGrid.Columns : 6;

            // Determine occupied columns on the puzzle grid
            int colMin = bus.GridPosition.x;
            if (puzzleGrid.TryGetGridDropPosition(bus.transform.position, bus.Length, bus.IsVertical, out Vector2Int cand))
            {
                colMin = cand.x;
            }

            int colMax = bus.IsVertical ? colMin : (colMin + bus.Length - 1);
            colMin = Mathf.Clamp(colMin, 0, totalCols - 1);
            colMax = Mathf.Clamp(colMax, 0, totalCols - 1);

            // Calculate percentage across the puzzle width [0.0, 1.0]
            float minPct = (float)colMin / totalCols;
            float maxPct = (float)(colMax + 1) / totalCols;

            // Map percentage to crowd container horizontal span
            Rect bounds = boundary.Bounds;
            float crowdLeft = bounds.xMin;
            float crowdWidth = bounds.width;

            minX = crowdLeft + minPct * crowdWidth;
            maxX = crowdLeft + maxPct * crowdWidth;

            // Small tolerance buffer for edge particles (0.12 units)
            float toleranceBuffer = 0.12f;
            minX -= toleranceBuffer;
            maxX += toleranceBuffer;

            return true;
        }

        private List<VehicleBase> GetActiveBusesAtTopOfGrid()
        {
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (dragHandler == null) dragHandler = FindAnyObjectByType<VehicleDragHandler>();

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

            return candidates;
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
            Vector3 baseRot = crowdRenderer != null ? crowdRenderer.CharacterRotationEuler : Vector3.zero;

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

                    Vector3 midPoint = (start + endPos) * 0.5f + Vector3.up * jumpArcHeight - Vector3.forward * jumpForwardBias;
                    float oneMinusT = 1f - t;
                    Vector3 currentPos = oneMinusT * oneMinusT * start + 2f * oneMinusT * t * midPoint + t * t * endPos;
                    commuter.transform.position = currentPos;

                    // Feet comes forward while jumping and landing so the whole character is visible to tilted camera
                    charPassenger.ApplyJumpFlightTilt(t, landingFeetForwardTilt, baseRot);
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
            Vector3 baseRot = crowdRenderer != null ? crowdRenderer.CharacterRotationEuler : Vector3.zero;

            GameObject commuter;
            if (commuterStreamPrefab != null)
            {
                commuter = Instantiate(commuterStreamPrefab, start, Quaternion.Euler(baseRot));
                commuter.transform.localScale = Vector3.one * activeScale;
                var charPassenger = commuter.GetComponent<CharacterPassenger>() ?? commuter.AddComponent<CharacterPassenger>();
                charPassenger.PlayRunning();
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

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (showToleranceGizmo && !showGizmoOnlyWhenSelected)
            {
                DrawToleranceGizmos();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (showToleranceGizmo && showGizmoOnlyWhenSelected)
            {
                DrawToleranceGizmos();
            }
        }

        private void DrawToleranceGizmos()
        {
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (boundary == null) return;

            float bottomY = boundary.BottomY;
            float thresholdY = bottomY + boundaryLayerTolerance;
            Rect bounds = boundary.Bounds;
            float leftX = bounds.xMin;
            float rightX = bounds.xMax;
            float width = bounds.width;

            // 1. Draw Container Floor Line (Solid Red)
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
            Gizmos.DrawLine(new Vector3(leftX, bottomY, 0f), new Vector3(rightX, bottomY, 0f));

            // 2. Draw Tolerance Threshold Line (Vibrant Yellow)
            Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.95f);
            Gizmos.DrawLine(new Vector3(leftX, thresholdY, 0f), new Vector3(rightX, thresholdY, 0f));

            // 3. Draw Semi-transparent Tolerance Band Area (Collectable Zone)
            Vector3 bandCenter = new Vector3((leftX + rightX) * 0.5f, bottomY + boundaryLayerTolerance * 0.5f, 0f);
            Vector3 bandSize = new Vector3(width, boundaryLayerTolerance, 0.05f);

            Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.15f);
            Gizmos.DrawCube(bandCenter, bandSize);

            Gizmos.color = new Color(1f, 0.92f, 0.016f, 0.45f);
            Gizmos.DrawWireCube(bandCenter, bandSize);

            // 3.5 Draw Floor Exception Margin Safety Catch Zone (Below Floor)
            float exceptionMargin = boundary.FloorExceptionMargin;
            float exceptionMarginY = bottomY - exceptionMargin;

            Gizmos.color = new Color(0.96f, 0.25f, 0.55f, 0.95f);
            Gizmos.DrawLine(new Vector3(leftX, exceptionMarginY, 0f), new Vector3(rightX, exceptionMarginY, 0f));

            Vector3 exceptionCenter = new Vector3((leftX + rightX) * 0.5f, bottomY - exceptionMargin * 0.5f, 0f);
            Vector3 exceptionSize = new Vector3(width, exceptionMargin, 0.05f);

            Gizmos.color = new Color(0.96f, 0.25f, 0.55f, 0.12f);
            Gizmos.DrawCube(exceptionCenter, exceptionSize);

            Gizmos.color = new Color(0.96f, 0.25f, 0.55f, 0.45f);
            Gizmos.DrawWireCube(exceptionCenter, exceptionSize);

            // 4. Draw Active Bus Column Slices on the Tolerance Band
            var candidates = GetActiveBusesAtTopOfGrid();
            if (candidates != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var bus = candidates[i];
                    if (bus == null) continue;

                    if (TryGetBusCollectionRange(bus, out float minX, out float maxX))
                    {
                        Color busColor = (palette != null) ? palette.GetColor(bus.ColorId) : Color.cyan;
                        Vector3 busSliceCenter = new Vector3((minX + maxX) * 0.5f, bottomY + boundaryLayerTolerance * 0.5f, 0f);
                        Vector3 busSliceSize = new Vector3(maxX - minX, boundaryLayerTolerance, 0.15f);

                        // Highlight slice in bus color
                        Gizmos.color = new Color(busColor.r, busColor.g, busColor.b, 0.35f);
                        Gizmos.DrawCube(busSliceCenter, busSliceSize);

                        Gizmos.color = busColor;
                        Gizmos.DrawWireCube(busSliceCenter, busSliceSize);

                        // Handles label for slice
                        UnityEditor.Handles.Label(new Vector3(busSliceCenter.x - 0.4f, thresholdY + 0.15f, 0f),
                            $"[{bus.name}] {minX:F1} to {maxX:F1}");
                    }
                }
            }

            // 5. Handles text labels for the lines
            GUIStyle labelStyle = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            labelStyle.normal.textColor = Color.yellow;
            UnityEditor.Handles.Label(new Vector3(rightX + 0.15f, thresholdY, 0f),
                $"Tolerance Line (+{boundaryLayerTolerance:F2})", labelStyle);

            GUIStyle floorStyle = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            floorStyle.normal.textColor = new Color(1f, 0.35f, 0.35f, 1f);
            UnityEditor.Handles.Label(new Vector3(rightX + 0.15f, bottomY, 0f),
                $"Floor (Y: {bottomY:F2})", floorStyle);

            GUIStyle exceptionStyle = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            exceptionStyle.normal.textColor = new Color(0.96f, 0.25f, 0.55f, 1f);
            UnityEditor.Handles.Label(new Vector3(rightX + 0.15f, exceptionMarginY, 0f),
                $"Floor Exception Margin (-{exceptionMargin:F2})", exceptionStyle);
        }
#endif
    }
}
