using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Puzzle.Input
{
    /// <summary>
    /// High-performance, buttery-smooth vehicle drag and pathfinding controller.
    /// Provides continuous sub-cell tracking, multi-waypoint delta consumption (zero frame-dropping),
    /// elastic edge resistance, and organic ease-out slot snapping.
    /// </summary>
    public class VehicleDragHandler : MonoBehaviour
    {
        [SerializeField] private PuzzleGrid puzzleGrid;
        [SerializeField] private Camera gameCamera;

        [Header("Smooth Movement Tuning")]
        [Tooltip("Base speed in units/second at which the vehicle advances along waypoints during dragging")]
        [SerializeField] private float pathFollowSpeed = 36f;

        [Tooltip("Dynamic catch-up multiplier scaling speed based on distance between vehicle and pointer")]
        [SerializeField] private float catchUpMultiplier = 28f;

        [Tooltip("Smooth follow damping factor for responsive, organic bus tracking")]
        [Range(15f, 60f)]
        [SerializeField] private float smoothFollowDamping = 35f;

        [Tooltip("Sub-cell elastic resistance factor when pulling against a blocked edge (0 = rigid wall, 0.25 = soft rubber band)")]
        [Range(0f, 0.35f)]
        [SerializeField] private float elasticEdgeResistance = 0.18f;

        private VehicleBase selectedVehicle;
        private Vector3 dragOffset;
        private Vector3 initialWorldPos;
        private Vector2Int initialGridPos;
        private Vector2Int currentHoveredGrid;
        private Vector2 currentVehicleFlatPos;
        private HashSet<Vector2Int> reachablePositions = new HashSet<Vector2Int>();
        private Plane dragPlane;

        // Waypoint-based path following
        private List<Vector2Int> activePath = new List<Vector2Int>();
        private int currentWaypointIndex = 0;
        private Vector2Int lastTargetDestination = new Vector2Int(-999, -999);
        private Coroutine activeSnapRoutine;

        public VehicleBase HoveredVehicle => selectedVehicle;
        public bool IsDragging => selectedVehicle != null;
        public Vector2Int CurrentHoveredGrid => currentHoveredGrid;

        private SandBlocks.Gameplay.GameManager gameManager;

        private void Awake()
        {
            if (gameCamera == null) gameCamera = Camera.main;
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (gameManager == null) gameManager = FindAnyObjectByType<SandBlocks.Gameplay.GameManager>();
            dragPlane = new Plane(Vector3.back, Vector3.zero);
        }

        private void Update()
        {
            if (gameManager == null) gameManager = FindAnyObjectByType<SandBlocks.Gameplay.GameManager>();

            // In Unified Grid mode, GameManager's classic container/crowd start animations do not apply
            bool isUnified = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance != null &&
                             SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.IsUnifiedActive;

            if (!isUnified)
            {
                if (gameManager != null && (!gameManager.IsGameStarted || gameManager.IsLevelStartAnimating || gameManager.IsLevelCompleted))
                {
                    return;
                }
            }
            else
            {
                if (SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.IsLevelStartAnimating ||
                    SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.IsLevelCompleted)
                {
                    return;
                }
            }

            // If selected vehicle was destroyed or lost while dragging (e.g. rapid level change), reset drag state cleanly
            if (selectedVehicle == null && activePath.Count > 0)
            {
                ResetDragState();
            }

            HandleInput();
        }

        private void HandleInput()
        {
            if (gameCamera == null) gameCamera = Camera.main;
            if (gameCamera == null || puzzleGrid == null) return;

            bool isDown = false;
            bool isHeld = false;
            bool isUp = false;
            Vector2 screenPos = Vector2.zero;

            // 1. Pointer (covers mouse, touch, pen in New Input System)
            if (Pointer.current != null)
            {
                isDown = Pointer.current.press.wasPressedThisFrame;
                isHeld = Pointer.current.press.isPressed;
                isUp = Pointer.current.press.wasReleasedThisFrame;
                screenPos = Pointer.current.position.ReadValue();
            }

            // 2. Mouse fallback
            if (!isDown && !isHeld && !isUp && Mouse.current != null)
            {
                isDown = Mouse.current.leftButton.wasPressedThisFrame;
                isHeld = Mouse.current.leftButton.isPressed;
                isUp = Mouse.current.leftButton.wasReleasedThisFrame;
                screenPos = Mouse.current.position.ReadValue();
            }

            // 3. Touch fallback
            if (!isDown && !isHeld && !isUp && Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                var touch = Touchscreen.current.primaryTouch;
                isDown = touch.press.wasPressedThisFrame;
                isHeld = touch.press.isPressed;
                isUp = touch.press.wasReleasedThisFrame;
                screenPos = touch.position.ReadValue();
            }

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

            if (isDown)
            {
                var vehicle = RaycastVehicle(screenPos);
                if (vehicle != null && (!vehicle.IsFull || SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance != null))
                {
                    if (activeSnapRoutine != null)
                    {
                        StopCoroutine(activeSnapRoutine);
                        activeSnapRoutine = null;
                    }

                    selectedVehicle = vehicle;
                    selectedVehicle.SetSelected(true);
                    initialWorldPos = vehicle.transform.position;
                    initialGridPos = vehicle.GridPosition;
                    currentHoveredGrid = initialGridPos;

                    currentVehicleFlatPos = isHorizontal
                        ? new Vector2(initialWorldPos.x, initialWorldPos.z)
                        : new Vector2(initialWorldPos.x, initialWorldPos.y);

                    activePath.Clear();
                    currentWaypointIndex = 0;
                    lastTargetDestination = initialGridPos;

                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusSelect();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleSelect();

                    // Temporarily remove vehicle from grid while dragging so its own cells are considered free
                    puzzleGrid.RemoveVehicle(selectedVehicle);

                    // Compute all reachable cells for this vehicle from initialGridPos using BFS
                    reachablePositions = puzzleGrid.GetReachablePositions(selectedVehicle, initialGridPos);

                    Vector3 planeNormal = isHorizontal ? Vector3.up : Vector3.back;
                    Vector3 planePoint = isHorizontal ? new Vector3(0f, vehicle.transform.position.y, 0f) : new Vector3(0f, 0f, vehicle.transform.position.z);
                    dragPlane = new Plane(planeNormal, planePoint);
                    Ray ray = gameCamera.ScreenPointToRay(screenPos);
                    if (dragPlane.Raycast(ray, out float enter))
                    {
                        dragOffset = vehicle.transform.position - ray.GetPoint(enter);
                    }
                    else
                    {
                        dragOffset = Vector3.zero;
                    }

                    // If completely blocked (only start pos is in reachable set), play blocked feedback
                    if (reachablePositions.Count <= 1 && selectedVehicle is BusVehicle bus)
                    {
                        bus.PlayBlockedShake(Vector3.zero);
                        SandBlocks.Gameplay.ToastManager.Show("Bus cannot be moved! Path is blocked.");
                    }
                }
            }
            else if (isHeld && selectedVehicle != null)
            {
                bool shouldStayForDropOff = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance != null &&
                                            SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.IsUnifiedActive &&
                                            SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.CurrentUnifiedMode == SandBlocks.Core.Data.UnifiedMode.DestinationDropOff;

                if (selectedVehicle.IsFull && !shouldStayForDropOff)
                {
                    // Bus became full while dragging and is driving away: release gracefully
                    selectedVehicle.SetSelected(false);
                    activePath.Clear();
                    selectedVehicle = null;
                    return;
                }

                Ray ray = gameCamera.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 rawPointerWorld = ray.GetPoint(enter) + dragOffset;
                    Vector2 pointerFlat = isHorizontal ? new Vector2(rawPointerWorld.x, rawPointerWorld.z) : new Vector2(rawPointerWorld.x, rawPointerWorld.y);

                    // Find which grid cell the pointer is aiming for
                    Vector2Int desiredGrid = puzzleGrid.WorldToGrid(rawPointerWorld, selectedVehicle.Length, selectedVehicle.IsVertical);

                    // Determine target destination among reachable cells
                    Vector2Int targetDestination;
                    if (reachablePositions.Contains(desiredGrid))
                    {
                        targetDestination = desiredGrid;
                    }
                    else
                    {
                        targetDestination = puzzleGrid.GetClosestReachablePosition(reachablePositions, desiredGrid);
                    }

                    // If target destination changed, recalculate collision-free path
                    if (targetDestination != lastTargetDestination)
                    {
                        lastTargetDestination = targetDestination;

                        if (currentHoveredGrid == targetDestination)
                        {
                            activePath.Clear();
                            activePath.Add(targetDestination);
                            currentWaypointIndex = 0;
                        }
                        else
                        {
                            var newPath = puzzleGrid.FindPath(selectedVehicle, currentHoveredGrid, targetDestination);
                            if (newPath != null && newPath.Count > 0)
                            {
                                activePath = newPath;
                                currentWaypointIndex = (newPath.Count > 1 && newPath[0] == currentHoveredGrid) ? 1 : 0;
                            }
                        }
                    }

                    // Responsive dynamic travel speed based on distance to pointer
                    float distToTarget = Vector2.Distance(currentVehicleFlatPos, pointerFlat);
                    float dynamicSpeed = Mathf.Max(pathFollowSpeed, distToTarget * catchUpMultiplier);
                    float stepDist = dynamicSpeed * Time.deltaTime;

                    // Advance continuously through waypoints in this frame (no frame-dropping or micro-hitches)
                    while (stepDist > 0.0001f && activePath != null && currentWaypointIndex < activePath.Count)
                    {
                        Vector2Int nextWp = activePath[currentWaypointIndex];
                        Vector3 nextWorld = puzzleGrid.GridToWorld(nextWp, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector2 nextFlat = isHorizontal ? new Vector2(nextWorld.x, nextWorld.z) : new Vector2(nextWorld.x, nextWorld.y);

                        float distToWp = Vector2.Distance(currentVehicleFlatPos, nextFlat);

                        if (distToWp <= stepDist)
                        {
                            currentVehicleFlatPos = nextFlat;
                            stepDist -= distToWp;
                            currentHoveredGrid = nextWp;
                            selectedVehicle.GridPosition = currentHoveredGrid;
                            currentWaypointIndex++;

                            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusDragStep();
                            SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleDragStep();
                        }
                        else
                        {
                            currentVehicleFlatPos = Vector2.MoveTowards(currentVehicleFlatPos, nextFlat, stepDist);
                            stepDist = 0f;
                        }
                    }

                    // Continuous sub-cell tracking when at target destination (zero deadzone)
                    if (activePath == null || currentWaypointIndex >= activePath.Count)
                    {
                        Vector3 curCellWorld = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector2 curCellFlat = isHorizontal ? new Vector2(curCellWorld.x, curCellWorld.z) : new Vector2(curCellWorld.x, curCellWorld.y);
                        Vector2 pointerOffset = pointerFlat - curCellFlat;

                        Vector2 subCellTarget;
                        if (desiredGrid == currentHoveredGrid || reachablePositions.Contains(desiredGrid))
                        {
                            // Free responsive tracking toward pointer within cell boundary
                            float maxOffset = puzzleGrid.CellSize * 0.48f;
                            subCellTarget = curCellFlat + Vector2.ClampMagnitude(pointerOffset, maxOffset);
                        }
                        else
                        {
                            // Elastic rubber-band resistance against blocked edge
                            float maxElastic = puzzleGrid.CellSize * elasticEdgeResistance;
                            subCellTarget = curCellFlat + Vector2.ClampMagnitude(pointerOffset, maxElastic);
                        }

                        currentVehicleFlatPos = Vector2.Lerp(currentVehicleFlatPos, subCellTarget, Time.deltaTime * smoothFollowDamping);
                    }

                    // Apply position with smooth lift and visual shake offset
                    Vector3 shakeOffset = (selectedVehicle is BusVehicle bv) ? bv.VisualShakeOffset : Vector3.zero;
                    float liftAmount = 0.20f;
                    Vector3 idealWorldPos = isHorizontal
                        ? new Vector3(currentVehicleFlatPos.x + shakeOffset.x, initialWorldPos.y + liftAmount, currentVehicleFlatPos.y + shakeOffset.z)
                        : new Vector3(currentVehicleFlatPos.x + shakeOffset.x, currentVehicleFlatPos.y + shakeOffset.y, initialWorldPos.z - liftAmount);

                    selectedVehicle.transform.position = Vector3.Lerp(selectedVehicle.transform.position, idealWorldPos, Time.deltaTime * 45f);
                }
            }
            else if (isUp && selectedVehicle != null)
            {
                if (selectedVehicle.IsFull && SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance == null)
                {
                    selectedVehicle.SetSelected(false);
                    activePath.Clear();
                    selectedVehicle = null;
                    return;
                }

                // Determine final placement cell: find closest reachable position to current visual position
                Vector2Int finalGridPos = currentHoveredGrid;
                float closestDistSqr = float.MaxValue;
                Vector3 curPos = selectedVehicle.transform.position;

                foreach (var reachable in reachablePositions)
                {
                    Vector3 rw = puzzleGrid.GridToWorld(reachable, selectedVehicle.Length, selectedVehicle.IsVertical);
                    float d = (curPos - rw).sqrMagnitude;
                    if (d < closestDistSqr)
                    {
                        closestDistSqr = d;
                        finalGridPos = reachable;
                    }
                }

                if (reachablePositions.Contains(finalGridPos) && puzzleGrid.CanPlace(selectedVehicle, finalGridPos))
                {
                    puzzleGrid.PlaceVehicle(selectedVehicle, finalGridPos);
                    Vector3 snapTarget = puzzleGrid.GridToWorld(finalGridPos, selectedVehicle.Length, selectedVehicle.IsVertical);
                    activeSnapRoutine = StartCoroutine(SmoothSnapToSlotRoutine(selectedVehicle, snapTarget));

                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusPlace();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehiclePlace();
                }
                else
                {
                    // Fallback to start position with smooth snap
                    puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);
                    Vector3 snapTarget = puzzleGrid.GridToWorld(initialGridPos, selectedVehicle.Length, selectedVehicle.IsVertical);
                    activeSnapRoutine = StartCoroutine(SmoothSnapToSlotRoutine(selectedVehicle, snapTarget));

                    if (selectedVehicle is BusVehicle bus)
                    {
                        bus.PlayBlockedShake(isHorizontal ? Vector3.forward : Vector3.up);
                    }
                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleBlocked();
                    SandBlocks.Gameplay.ToastManager.Show("Cannot place bus here! Spot is blocked.");
                }

                selectedVehicle.SetSelected(false);
                activePath.Clear();
                selectedVehicle = null;
            }
        }

        private IEnumerator SmoothSnapToSlotRoutine(VehicleBase vehicle, Vector3 targetPos)
        {
            if (vehicle == null) yield break;
            float elapsed = 0f;
            float duration = 0.08f;
            Vector3 startPos = vehicle.transform.position;

            while (elapsed < duration && vehicle != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                vehicle.transform.position = Vector3.Lerp(startPos, targetPos, t);
                yield return null;
            }

            if (vehicle != null)
            {
                vehicle.transform.position = targetPos;
            }
            activeSnapRoutine = null;
        }

        /// <summary>
        /// Cancels any active drag operation and resets all path/hover states.
        /// Safely called on level transitions or rapid UI clicks to prevent stuck drags or ghost waypoints.
        /// </summary>
        public void CancelDrag() => ResetDragState();

        public void ResetDragState()
        {
            if (activeSnapRoutine != null)
            {
                StopCoroutine(activeSnapRoutine);
                activeSnapRoutine = null;
            }

            if (selectedVehicle != null) selectedVehicle.SetSelected(false);
            selectedVehicle = null;
            activePath.Clear();
            currentWaypointIndex = 0;
            reachablePositions.Clear();
            lastTargetDestination = new Vector2Int(-999, -999);
            currentHoveredGrid = new Vector2Int(-999, -999);
        }

        public void RefreshReachablePositions()
        {
            if (selectedVehicle != null && puzzleGrid != null)
            {
                var newReachable = puzzleGrid.GetReachablePositions(selectedVehicle, currentHoveredGrid);
                newReachable.UnionWith(puzzleGrid.GetReachablePositions(selectedVehicle, initialGridPos));
                reachablePositions = newReachable;
                lastTargetDestination = new Vector2Int(-999, -999);
            }
        }

        private VehicleBase RaycastVehicle(Vector2 screenPos)
        {
            Ray ray = gameCamera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 300f);
            VehicleBase closestVehicle = null;
            float closestDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                var v = hits[i].collider.GetComponentInParent<VehicleBase>();
                bool shouldStayForDropOff = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance != null &&
                                            SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.IsUnifiedActive &&
                                            SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance.CurrentUnifiedMode == SandBlocks.Core.Data.UnifiedMode.DestinationDropOff;

                if (v != null && (!v.IsFull || shouldStayForDropOff))
                {
                    if (hits[i].distance < closestDist)
                    {
                        closestDist = hits[i].distance;
                        closestVehicle = v;
                    }
                }
            }

            return closestVehicle;
        }
    }
}
