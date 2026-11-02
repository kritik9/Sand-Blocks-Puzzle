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

                    // If bus is trapped with no valid moves anywhere on the board, and not at the top collection row
                    bool isAtTopRow = IsVehicleAtTopRow(selectedVehicle, initialGridPos);
                    if (!isAtTopRow && reachablePositions != null && reachablePositions.Count <= 1)
                    {
                        if (selectedVehicle is BusVehicle blockedBus)
                        {
                            blockedBus.PlayBlockedShake(Vector3.zero, showToast: false);
                        }
                        SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
                        SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleBlocked();
                        SandBlocks.Gameplay.ToastManager.Show("Bus cannot move! Path is blocked.");
                    }
                }
            }
            else if (isHeld && selectedVehicle != null)
            {
                Ray ray = gameCamera.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 rawPointerWorld = ray.GetPoint(enter) + dragOffset;
                    Vector2 pointerFlat = isHorizontal ? new Vector2(rawPointerWorld.x, rawPointerWorld.z) : new Vector2(rawPointerWorld.x, rawPointerWorld.y);

                    float cellSize = puzzleGrid.CellSize;
                    float stepThreshold = cellSize * 0.50f;

                    // 1. Step-by-step neighbor advancement
                    // The bus can ONLY advance to directly adjacent cells along the path the finger is taking.
                    // If the finger moves across an obstacle or into a blocked cell, the bus STOPS at the obstacle!
                    // It will NEVER automatically trace a detour around obstacles to reach the finger.
                    bool steppedAny = false;
                    int maxStepsThisFrame = 4; // safety cap for fast swipes

                    while (maxStepsThisFrame > 0)
                    {
                        maxStepsThisFrame--;

                        Vector3 cellWorld = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector2 cellFlat = isHorizontal ? new Vector2(cellWorld.x, cellWorld.z) : new Vector2(cellWorld.x, cellWorld.y);
                        Vector2 toPointer = pointerFlat - cellFlat;

                        Vector2Int primaryStep = Vector2Int.zero;
                        Vector2Int secondaryStep = Vector2Int.zero;

                        if (Mathf.Abs(toPointer.x) >= Mathf.Abs(toPointer.y))
                        {
                            if (Mathf.Abs(toPointer.x) > stepThreshold)
                                primaryStep = new Vector2Int(toPointer.x > 0 ? 1 : -1, 0);
                            if (Mathf.Abs(toPointer.y) > stepThreshold)
                                secondaryStep = new Vector2Int(0, toPointer.y > 0 ? 1 : -1);
                        }
                        else
                        {
                            if (Mathf.Abs(toPointer.y) > stepThreshold)
                                primaryStep = new Vector2Int(0, toPointer.y > 0 ? 1 : -1);
                            if (Mathf.Abs(toPointer.x) > stepThreshold)
                                secondaryStep = new Vector2Int(toPointer.x > 0 ? 1 : -1, 0);
                        }

                        bool tookStep = false;

                        // Try primary adjacent step
                        if (primaryStep != Vector2Int.zero)
                        {
                            Vector2Int cand = currentHoveredGrid + primaryStep;
                            if (puzzleGrid.CanPlace(selectedVehicle, cand))
                            {
                                currentHoveredGrid = cand;
                                selectedVehicle.GridPosition = currentHoveredGrid;
                                tookStep = true;
                                steppedAny = true;
                            }
                        }

                        // If primary step was blocked, try secondary step
                        if (!tookStep && secondaryStep != Vector2Int.zero)
                        {
                            Vector2Int cand = currentHoveredGrid + secondaryStep;
                            if (puzzleGrid.CanPlace(selectedVehicle, cand))
                            {
                                currentHoveredGrid = cand;
                                selectedVehicle.GridPosition = currentHoveredGrid;
                                tookStep = true;
                                steppedAny = true;
                            }
                        }

                        if (!tookStep)
                        {
                            // Neither step direction was valid or pointer is within threshold -> stop stepping!
                            break;
                        }
                    }

                    if (steppedAny)
                    {
                        SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusDragStep();
                    }

                    // 2. Continuous sub-cell visual tracking with directional obstacle resistance
                    Vector3 anchorWorld = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                    Vector2 anchorFlat = isHorizontal ? new Vector2(anchorWorld.x, anchorWorld.z) : new Vector2(anchorWorld.x, anchorWorld.y);
                    Vector2 dragOffsetFromCell = pointerFlat - anchorFlat;

                    // Check which directions from currentHoveredGrid are free or blocked
                    bool canMoveRight = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.right);
                    bool canMoveLeft  = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.left);
                    bool canMoveUp    = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.up);
                    bool canMoveDown  = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.down);

                    float freeRange = cellSize * 0.48f;
                    float elasticRange = cellSize * elasticEdgeResistance;

                    // Clamp X offset
                    if (dragOffsetFromCell.x > 0)
                    {
                        float maxAllowed = canMoveRight ? freeRange : elasticRange;
                        dragOffsetFromCell.x = Mathf.Min(dragOffsetFromCell.x, maxAllowed);
                    }
                    else if (dragOffsetFromCell.x < 0)
                    {
                        float maxAllowed = canMoveLeft ? freeRange : elasticRange;
                        dragOffsetFromCell.x = Mathf.Max(dragOffsetFromCell.x, -maxAllowed);
                    }

                    // Clamp Y offset
                    if (dragOffsetFromCell.y > 0)
                    {
                        float maxAllowed = canMoveUp ? freeRange : elasticRange;
                        dragOffsetFromCell.y = Mathf.Min(dragOffsetFromCell.y, maxAllowed);
                    }
                    else if (dragOffsetFromCell.y < 0)
                    {
                        float maxAllowed = canMoveDown ? freeRange : elasticRange;
                        dragOffsetFromCell.y = Mathf.Max(dragOffsetFromCell.y, -maxAllowed);
                    }



                    Vector2 targetFlatPos = anchorFlat + dragOffsetFromCell;
                    currentVehicleFlatPos = Vector2.Lerp(currentVehicleFlatPos, targetFlatPos, Time.deltaTime * smoothFollowDamping);

                    // 3. Apply position with smooth lift and visual shake offset
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
                    selectedVehicle = null;
                    return;
                }

                // Place the vehicle cleanly at the cell it reached step-by-step
                Vector2Int finalGridPos = currentHoveredGrid;

                if (puzzleGrid.CanPlace(selectedVehicle, finalGridPos))
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

                    bool wasAtTopRow = IsVehicleAtTopRow(selectedVehicle, initialGridPos);
                    if (!wasAtTopRow)
                    {
                        if (selectedVehicle is BusVehicle bus)
                        {
                            bus.PlayBlockedShake(isHorizontal ? Vector3.forward : Vector3.up, showToast: false);
                        }
                        SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
                        SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleBlocked();
                        SandBlocks.Gameplay.ToastManager.Show("Cannot place bus here! Spot is blocked.");
                    }
                }

                selectedVehicle.SetSelected(false);
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

            if (selectedVehicle != null)
            {
                if (puzzleGrid != null && selectedVehicle.gameObject != null)
                {
                    bool isPlaced = false;
                    var registered = puzzleGrid.RegisteredVehicles;
                    if (registered != null)
                    {
                        for (int i = 0; i < registered.Count; i++)
                        {
                            if (registered[i] == selectedVehicle)
                            {
                                isPlaced = true;
                                break;
                            }
                        }
                    }

                    if (!isPlaced)
                    {
                        puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);
                        selectedVehicle.transform.position = puzzleGrid.GridToWorld(initialGridPos, selectedVehicle.Length, selectedVehicle.IsVertical);
                    }
                }

                selectedVehicle.SetSelected(false);
            }

            selectedVehicle = null;
            reachablePositions.Clear();
            currentHoveredGrid = new Vector2Int(-999, -999);
        }

        public void RefreshReachablePositions()
        {
            if (selectedVehicle != null && puzzleGrid != null)
            {
                var newReachable = puzzleGrid.GetReachablePositions(selectedVehicle, currentHoveredGrid);
                newReachable.UnionWith(puzzleGrid.GetReachablePositions(selectedVehicle, initialGridPos));
                reachablePositions = newReachable;
            }
        }

        private bool IsVehicleAtTopRow(VehicleBase vehicle, Vector2Int gridPos)
        {
            if (vehicle == null || puzzleGrid == null) return false;
            int topRow = puzzleGrid.Rows - 1;
            int maxOccupiedY = vehicle.IsVertical ? (gridPos.y + vehicle.Length - 1) : gridPos.y;
            return maxOccupiedY >= topRow;
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