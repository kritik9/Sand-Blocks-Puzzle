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
        private Vector2Int lastHighlightedGrid = new Vector2Int(-999, -999);
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

            bool isUnified = false;
            if (SandBlocks.Gameplay.LevelManager.Instance != null && SandBlocks.Gameplay.LevelManager.Instance.CurrentLevel != null)
            {
                isUnified = (SandBlocks.Gameplay.LevelManager.Instance.CurrentLevel.gameplayMode == SandBlocks.Core.Data.GameplayMode.UnifiedGrid);
            }
            else
            {
                var ugm = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance;
                isUnified = (ugm != null && ugm.IsUnifiedActive);
            }

            if (!isUnified)
            {
                if (gameManager != null && (!gameManager.IsGameStarted || gameManager.IsLevelStartAnimating || gameManager.IsLevelCompleted))
                {
                    return;
                }
            }
            else
            {
                var ugm = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance;
                if (ugm != null && (ugm.IsLevelStartAnimating || ugm.IsLevelCompleted))
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
                    lastHighlightedGrid = currentHoveredGrid;

                    currentVehicleFlatPos = isHorizontal
                        ? new Vector2(initialWorldPos.x, initialWorldPos.z)
                        : new Vector2(initialWorldPos.x, initialWorldPos.y);

                    SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusSelect();
                    SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleSelect();

                    // Highlight the tiles on which this bus is initially hovering
                    if (puzzleGrid != null)
                    {
                        puzzleGrid.HighlightHoverTiles(selectedVehicle, currentHoveredGrid);
                    }

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
                        // Cleanly restore to grid immediately so it never enters a detached or floating state
                        puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);

                        if (selectedVehicle is BusVehicle blockedBus)
                        {
                            blockedBus.PlayBlockedShake(Vector3.zero, showToast: false);
                        }
                        SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusBlocked();
                        SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehicleBlocked();
                        SandBlocks.Gameplay.ToastManager.Show("Bus cannot move! Path is blocked.");

                        if (puzzleGrid != null) puzzleGrid.ClearHoverTileHighlights();
                        selectedVehicle.SetSelected(false);
                        selectedVehicle = null;
                        reachablePositions.Clear();
                        currentHoveredGrid = new Vector2Int(-999, -999);
                        return;
                    }
                }
            }
            else if (isHeld && selectedVehicle != null)
            {
                if ((selectedVehicle.IsFull && SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance == null) ||
                    (selectedVehicle is BusVehicle busCheck && busCheck.IsDrivingAway))
                {
                    OnVehicleFullOrDeparting(selectedVehicle);
                }
                else
                {
                    Ray ray = gameCamera.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 rawPointerWorld = ray.GetPoint(enter) + dragOffset;
                    Vector2 pointerFlat = isHorizontal ? new Vector2(rawPointerWorld.x, rawPointerWorld.z) : new Vector2(rawPointerWorld.x, rawPointerWorld.y);

                    float stepX = puzzleGrid.StepX;
                    float stepY = puzzleGrid.StepY;
                    float stepThresholdX = stepX * 0.50f;
                    float stepThresholdY = stepY * 0.50f;

                    // 1. Step-by-step neighbor advancement
                    // The bus can ONLY advance to directly adjacent cells along the path the finger is taking.
                    // If the finger moves across an obstacle or into a blocked cell, the bus STOPS at the obstacle!
                    // It will NEVER automatically trace a detour around obstacles to reach the finger.
                    bool steppedAny = false;
                    int maxStepsThisFrame = 4; // safety cap for fast swipes

                    while (maxStepsThisFrame > 0)
                    {
                        maxStepsThisFrame--;

                        Vector3 cellWorld = puzzleGrid.GridToWorld(selectedVehicle, currentHoveredGrid);
                        Vector2 cellFlat = isHorizontal ? new Vector2(cellWorld.x, cellWorld.z) : new Vector2(cellWorld.x, cellWorld.y);
                        Vector2 toPointer = pointerFlat - cellFlat;

                        Vector2Int primaryStep = Vector2Int.zero;
                        Vector2Int secondaryStep = Vector2Int.zero;

                        if (Mathf.Abs(toPointer.x) >= Mathf.Abs(toPointer.y))
                        {
                            if (Mathf.Abs(toPointer.x) > stepThresholdX)
                                primaryStep = new Vector2Int(toPointer.x > 0 ? 1 : -1, 0);
                            if (Mathf.Abs(toPointer.y) > stepThresholdY)
                                secondaryStep = new Vector2Int(0, toPointer.y > 0 ? 1 : -1);
                        }
                        else
                        {
                            if (Mathf.Abs(toPointer.y) > stepThresholdY)
                                primaryStep = new Vector2Int(0, toPointer.y > 0 ? 1 : -1);
                            if (Mathf.Abs(toPointer.x) > stepThresholdX)
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

                    // Update tile hover highlight ONLY when cell actually changes
                    if (puzzleGrid != null && currentHoveredGrid != lastHighlightedGrid)
                    {
                        puzzleGrid.HighlightHoverTiles(selectedVehicle, currentHoveredGrid);
                        lastHighlightedGrid = currentHoveredGrid;
                    }

                    // 2. Continuous sub-cell visual tracking with directional obstacle resistance
                    Vector3 anchorWorld = puzzleGrid.GridToWorld(selectedVehicle, currentHoveredGrid);
                    Vector2 anchorFlat = isHorizontal ? new Vector2(anchorWorld.x, anchorWorld.z) : new Vector2(anchorWorld.x, anchorWorld.y);
                    Vector2 dragOffsetFromCell = pointerFlat - anchorFlat;

                    // Check which directions from currentHoveredGrid are free or blocked
                    bool canMoveRight = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.right);
                    bool canMoveLeft  = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.left);
                    bool canMoveUp    = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.up);
                    bool canMoveDown  = puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid + Vector2Int.down);

                    float freeRangeX = stepX * 0.48f;
                    float freeRangeY = stepY * 0.48f;
                    float elasticRangeX = stepX * elasticEdgeResistance;
                    float elasticRangeY = stepY * elasticEdgeResistance;

                    // Clamp X offset
                    if (dragOffsetFromCell.x > 0)
                    {
                        float maxAllowed = canMoveRight ? freeRangeX : elasticRangeX;
                        dragOffsetFromCell.x = Mathf.Min(dragOffsetFromCell.x, maxAllowed);
                    }
                    else if (dragOffsetFromCell.x < 0)
                    {
                        float maxAllowed = canMoveLeft ? freeRangeX : elasticRangeX;
                        dragOffsetFromCell.x = Mathf.Max(dragOffsetFromCell.x, -maxAllowed);
                    }

                    // Clamp Y offset
                    if (dragOffsetFromCell.y > 0)
                    {
                        float maxAllowed = canMoveUp ? freeRangeY : elasticRangeY;
                        dragOffsetFromCell.y = Mathf.Min(dragOffsetFromCell.y, maxAllowed);
                    }
                    else if (dragOffsetFromCell.y < 0)
                    {
                        float maxAllowed = canMoveDown ? freeRangeY : elasticRangeY;
                        dragOffsetFromCell.y = Mathf.Max(dragOffsetFromCell.y, -maxAllowed);
                    }

                    Vector2 targetFlatPos = anchorFlat + dragOffsetFromCell;
                    // Mathematically stable exponential decay: 100% frame-rate independent across 30, 60, 90, 120 FPS without overshoot
                    float decay = 1f - Mathf.Exp(-smoothFollowDamping * Time.deltaTime);
                    currentVehicleFlatPos = Vector2.Lerp(currentVehicleFlatPos, targetFlatPos, decay);

                    // 3. Apply position directly from smoothed flat pos (no double-lerp lag or oscillation)
                    Vector3 shakeOffset = (selectedVehicle is BusVehicle bv) ? bv.VisualShakeOffset : Vector3.zero;
                    float liftAmount = 0.20f;
                    Vector3 idealWorldPos = isHorizontal
                        ? new Vector3(currentVehicleFlatPos.x + shakeOffset.x, initialWorldPos.y + liftAmount, currentVehicleFlatPos.y + shakeOffset.z)
                        : new Vector3(currentVehicleFlatPos.x + shakeOffset.x, currentVehicleFlatPos.y + shakeOffset.y, initialWorldPos.z - liftAmount);

                    selectedVehicle.transform.position = idealWorldPos;
                }
                }
            }
            else if (isUp)
            {
                if (selectedVehicle != null)
                {
                    FinishVehicleDrop();
                }
                else
                {
                    if (puzzleGrid != null) puzzleGrid.ClearHoverTileHighlights();
                    currentHoveredGrid = new Vector2Int(-999, -999);
                }
            }
            else if (!isHeld && selectedVehicle != null)
            {
                FinishVehicleDrop();
            }

            if (selectedVehicle == null && currentHoveredGrid.x != -999)
            {
                if (puzzleGrid != null) puzzleGrid.ClearHoverTileHighlights();
                currentHoveredGrid = new Vector2Int(-999, -999);
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

        private void FinishVehicleDrop()
        {
            if (selectedVehicle == null) return;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

            if ((selectedVehicle.IsFull && SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance == null) ||
                (selectedVehicle is BusVehicle busCheck && busCheck.IsDrivingAway))
            {
                OnVehicleFullOrDeparting(selectedVehicle);
                return;
            }

            // Safe fallback: never pass negative or corrupt coordinates to CanPlace
            Vector2Int finalGridPos = (currentHoveredGrid.x >= 0 && currentHoveredGrid.y >= 0)
                ? currentHoveredGrid
                : initialGridPos;

            Vector3 currentWorldPos = selectedVehicle.transform.position;

            if (puzzleGrid.CanPlace(selectedVehicle, finalGridPos))
            {
                puzzleGrid.PlaceVehicle(selectedVehicle, finalGridPos);
                Vector3 snapTarget = puzzleGrid.GridToWorld(selectedVehicle, finalGridPos);
                selectedVehicle.transform.position = currentWorldPos; // Preserve world position for smooth interpolation
                activeSnapRoutine = StartCoroutine(SmoothSnapToSlotRoutine(selectedVehicle, snapTarget));

                SandBlocks.Core.Audio.AudioManager.Instance?.PlayBusPlace();
                SandBlocks.Core.Haptics.HapticManager.Instance?.TriggerVehiclePlace();
            }
            else
            {
                // Fallback to start position with smooth snap
                puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);
                Vector3 snapTarget = puzzleGrid.GridToWorld(selectedVehicle, initialGridPos);
                selectedVehicle.transform.position = currentWorldPos; // Preserve world position for smooth interpolation
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

            if (puzzleGrid != null) puzzleGrid.ClearHoverTileHighlights();
            lastHighlightedGrid = new Vector2Int(-999, -999);
            selectedVehicle.SetSelected(false);
            selectedVehicle = null;
            reachablePositions.Clear();
            currentHoveredGrid = new Vector2Int(-999, -999);
        }

        /// <summary>
        /// Called when a vehicle becomes full or starts departing.
        /// Immediately terminates any active drag on THAT vehicle, unselects it, and clears tile hover highlights.
        /// </summary>
        public void OnVehicleFullOrDeparting(VehicleBase vehicle)
        {
            if (selectedVehicle != null)
            {
                // Only terminate drag if the vehicle that filled or departed IS the one currently being dragged
                if (selectedVehicle == vehicle || vehicle == null || selectedVehicle.IsFull)
                {
                    if (activeSnapRoutine != null)
                    {
                        StopCoroutine(activeSnapRoutine);
                        activeSnapRoutine = null;
                    }

                    selectedVehicle.SetSelected(false);
                    selectedVehicle = null;

                    if (puzzleGrid != null)
                    {
                        puzzleGrid.ClearHoverTileHighlights();
                    }

                    reachablePositions?.Clear();
                    currentHoveredGrid = new Vector2Int(-999, -999);
                }
                else
                {
                    // Another bus on the board became full/departed, opening up grid cells:
                    // Refresh reachable positions for the bus currently being dragged
                    RefreshReachablePositions();
                }
            }
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
                        selectedVehicle.transform.position = puzzleGrid.GridToWorld(selectedVehicle, initialGridPos);
                    }
                }

                selectedVehicle.SetSelected(false);
            }

            if (puzzleGrid != null) puzzleGrid.ClearHoverTileHighlights();
            selectedVehicle = null;
            reachablePositions.Clear();
            currentHoveredGrid = new Vector2Int(-999, -999);
        }

        private void OnDisable()
        {
            if (selectedVehicle != null)
            {
                CancelDrag();
            }
            if (puzzleGrid != null)
            {
                puzzleGrid.ClearHoverTileHighlights();
            }
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
            var cells = vehicle.GetOccupiedCells(gridPos);
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].y >= topRow) return true;
            }
            return false;
        }

        private VehicleBase RaycastVehicle(Vector2 screenPos)
        {
            Ray ray = gameCamera.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, 300f);
            VehicleBase closestVehicle = null;
            float closestDist = float.MaxValue;

            bool isUnified = false;
            if (SandBlocks.Gameplay.LevelManager.Instance != null && SandBlocks.Gameplay.LevelManager.Instance.CurrentLevel != null)
            {
                isUnified = (SandBlocks.Gameplay.LevelManager.Instance.CurrentLevel.gameplayMode == SandBlocks.Core.Data.GameplayMode.UnifiedGrid);
            }
            else
            {
                var ugm = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance;
                isUnified = (ugm != null && ugm.IsUnifiedActive);
            }

            var unifiedManager = SandBlocks.Gameplay.UnifiedGrid.UnifiedGridManager.Instance;
            bool shouldStayForDropOff = isUnified &&
                                        unifiedManager != null &&
                                        unifiedManager.CurrentUnifiedMode == SandBlocks.Core.Data.UnifiedMode.DestinationDropOff;

            for (int i = 0; i < hits.Length; i++)
            {
                var v = hits[i].collider.GetComponentInParent<VehicleBase>();

                if (v != null && (!v.IsFull || shouldStayForDropOff))
                {
                    if (v is BusVehicle busCheck && busCheck.IsDrivingAway)
                    {
                        continue;
                    }

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