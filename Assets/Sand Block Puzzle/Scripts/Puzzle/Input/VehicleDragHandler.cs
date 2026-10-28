using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Puzzle.Input
{
    public class VehicleDragHandler : MonoBehaviour
    {
        [SerializeField] private PuzzleGrid puzzleGrid;
        [SerializeField] private Camera gameCamera;

        [Header("Movement Tuning")]
        [Tooltip("Speed in units/second at which the vehicle moves along grid path waypoints during dragging")]
        [SerializeField] private float pathFollowSpeed = 18f;

        private VehicleBase selectedVehicle;
        private Vector3 dragOffset;
        private Vector3 initialWorldPos;
        private Vector2Int initialGridPos;
        private Vector2Int currentHoveredGrid;
        private HashSet<Vector2Int> reachablePositions = new HashSet<Vector2Int>();
        private Plane dragPlane;

        // Waypoint-based path following
        private List<Vector2Int> activePath = new List<Vector2Int>();
        private int currentWaypointIndex = 0;
        private Vector2Int lastTargetDestination = new Vector2Int(-999, -999);

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

            if (isDown)
            {
                var vehicle = RaycastVehicle(screenPos);
                if (vehicle != null && (!vehicle.IsFull || SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance != null))
                {
                    selectedVehicle = vehicle;
                    selectedVehicle.SetSelected(true);
                    initialWorldPos = vehicle.transform.position;
                    initialGridPos = vehicle.GridPosition;
                    currentHoveredGrid = initialGridPos;

                    activePath.Clear();
                    currentWaypointIndex = 0;
                    lastTargetDestination = new Vector2Int(-999, -999);

                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusSelect();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.Selection();

                    // Temporarily remove vehicle from grid while dragging so its own cells are considered free
                    puzzleGrid.RemoveVehicle(selectedVehicle);

                    // Compute all reachable cells for this vehicle from initialGridPos using BFS
                    reachablePositions = puzzleGrid.GetReachablePositions(selectedVehicle, initialGridPos);

                    dragPlane = new Plane(Vector3.back, new Vector3(0f, 0f, vehicle.transform.position.z));
                    Ray ray = gameCamera.ScreenPointToRay(screenPos);
                    if (dragPlane.Raycast(ray, out float enter))
                    {
                        dragOffset = vehicle.transform.position - ray.GetPoint(enter);
                    }
                    else
                    {
                        dragOffset = Vector3.zero;
                    }

                    // If completely blocked (only start pos is in reachable set), immediately play blocked shake
                    if (reachablePositions.Count <= 1 && selectedVehicle is BusVehicle bus)
                    {
                        bus.PlayBlockedShake(Vector3.zero);
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

                    // Find which grid cell the pointer is trying to aim for
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

                        // If user is actively pulling towards an unreachable/blocked cell, trigger blocked shake
                        Vector3 desiredWorld = puzzleGrid.GridToWorld(desiredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector3 currentWorld = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector3 pullDiff = desiredWorld - currentWorld;

                        if (pullDiff.sqrMagnitude > 0.15f && selectedVehicle is BusVehicle bus)
                        {
                            bus.PlayBlockedShake(pullDiff.normalized);
                        }
                    }

                    // If target destination changed, recalculate the collision-free path of waypoints
                    if (targetDestination != lastTargetDestination)
                    {
                        lastTargetDestination = targetDestination;

                        // Start path from the waypoint we are currently moving towards, or currentHoveredGrid
                        Vector2Int pathStart = currentHoveredGrid;
                        if (activePath != null && currentWaypointIndex < activePath.Count)
                        {
                            pathStart = activePath[currentWaypointIndex];
                        }

                        if (pathStart == targetDestination)
                        {
                            activePath.Clear();
                            activePath.Add(targetDestination);
                            currentWaypointIndex = 0;
                        }
                        else
                        {
                            var newPath = puzzleGrid.FindPath(selectedVehicle, pathStart, targetDestination);
                            if (newPath != null && newPath.Count > 0)
                            {
                                activePath = newPath;
                                currentWaypointIndex = (newPath.Count > 1 && newPath[0] == currentHoveredGrid) ? 1 : 0;
                            }
                        }
                    }

                    // Smoothly advance vehicle along activePath waypoints step-by-step
                    if (activePath != null && currentWaypointIndex < activePath.Count)
                    {
                        Vector2Int nextWaypoint = activePath[currentWaypointIndex];
                        Vector3 targetWorld = puzzleGrid.GridToWorld(nextWaypoint, selectedVehicle.Length, selectedVehicle.IsVertical);

                        Vector3 curPos = selectedVehicle.transform.position;
                        Vector2 curFlat = new Vector2(curPos.x, curPos.y);
                        Vector2 targetFlat = new Vector2(targetWorld.x, targetWorld.y);

                        float remainingWaypoints = activePath.Count - currentWaypointIndex;
                        float dynamicSpeed = Mathf.Max(pathFollowSpeed, remainingWaypoints * 12f);
                        float stepDist = dynamicSpeed * Time.deltaTime;

                        if (Vector2.Distance(curFlat, targetFlat) <= stepDist + 0.02f)
                        {
                            // Arrived at nextWaypoint
                            curFlat = targetFlat;
                            currentHoveredGrid = nextWaypoint;
                            selectedVehicle.GridPosition = currentHoveredGrid;
                            currentWaypointIndex++;

                            SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusDragStep();
                            SandBlocks.Core.Haptics.HapticManager.Instance?.LightImpact();
                        }
                        else
                        {
                            curFlat = Vector2.MoveTowards(curFlat, targetFlat, stepDist);
                        }

                        Vector3 shakeOffset = (selectedVehicle is BusVehicle bv) ? bv.VisualShakeOffset : Vector3.zero;
                        selectedVehicle.transform.position = new Vector3(curFlat.x + shakeOffset.x, curFlat.y + shakeOffset.y, initialWorldPos.z - 0.25f);
                    }
                    else
                    {
                        // Holding position smoothly at currentHoveredGrid
                        Vector3 basePos = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector3 shakeOffset = (selectedVehicle is BusVehicle bv) ? bv.VisualShakeOffset : Vector3.zero;
                        Vector3 targetPos = new Vector3(basePos.x + shakeOffset.x, basePos.y + shakeOffset.y, initialWorldPos.z - 0.25f);
                        selectedVehicle.transform.position = Vector3.Lerp(selectedVehicle.transform.position, targetPos, Time.deltaTime * 32f);
                    }
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

                // Determine final placement cell (nearest waypoint or current cell)
                Vector2Int finalGridPos = currentHoveredGrid;
                if (activePath != null && currentWaypointIndex < activePath.Count)
                {
                    Vector2Int nextWaypoint = activePath[currentWaypointIndex];
                    Vector3 curPos = selectedVehicle.transform.position;
                    Vector3 curGridPos = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                    Vector3 nextGridPos = puzzleGrid.GridToWorld(nextWaypoint, selectedVehicle.Length, selectedVehicle.IsVertical);

                    if (Vector3.Distance(curPos, nextGridPos) < Vector3.Distance(curPos, curGridPos))
                    {
                        finalGridPos = nextWaypoint;
                    }
                }

                if (reachablePositions.Contains(finalGridPos) && puzzleGrid.CanPlace(selectedVehicle, finalGridPos))
                {
                    puzzleGrid.PlaceVehicle(selectedVehicle, finalGridPos);
                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusPlace();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.MediumImpact();
                }
                else
                {
                    // Fallback to start pos
                    puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);
                    if (selectedVehicle is BusVehicle bus)
                    {
                        bus.PlayBlockedShake(Vector3.up);
                    }
                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.HeavyImpact();
                }

                selectedVehicle.SetSelected(false);
                activePath.Clear();
                selectedVehicle = null;
            }
        }

        /// <summary>
        /// Cancels any active drag operation and resets all path/hover states.
        /// Safely called on level transitions or rapid UI clicks to prevent stuck drags or ghost waypoints.
        /// </summary>
        public void ResetDragState()
        {
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
