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

        private VehicleBase selectedVehicle;
        private Vector3 dragOffset;
        private Vector3 initialWorldPos;
        private Vector2Int initialGridPos;
        private Vector2Int currentHoveredGrid;
        private HashSet<Vector2Int> reachablePositions = new HashSet<Vector2Int>();
        private Plane dragPlane;

        public VehicleBase HoveredVehicle => selectedVehicle;
        public bool IsDragging => selectedVehicle != null;
        public Vector2Int CurrentHoveredGrid => currentHoveredGrid;

        private void Awake()
        {
            if (gameCamera == null) gameCamera = Camera.main;
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            dragPlane = new Plane(Vector3.back, Vector3.zero);
        }

        private void Update()
        {
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
                if (vehicle != null && !vehicle.IsFull)
                {
                    selectedVehicle = vehicle;
                    initialWorldPos = vehicle.transform.position;
                    initialGridPos = vehicle.GridPosition;
                    currentHoveredGrid = initialGridPos;

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
                if (selectedVehicle.IsFull)
                {
                    // Bus became full while hovering and is driving away: release gracefully
                    selectedVehicle = null;
                    return;
                }

                Ray ray = gameCamera.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 rawPointerWorld = ray.GetPoint(enter) + dragOffset;

                    // Find which grid cell the pointer is trying to aim for
                    Vector2Int desiredGrid = puzzleGrid.WorldToGrid(rawPointerWorld, selectedVehicle.Length, selectedVehicle.IsVertical);

                    // Check if desired grid is reachable through a free contiguous path
                    if (reachablePositions.Contains(desiredGrid))
                    {
                        currentHoveredGrid = desiredGrid;
                    }
                    else
                    {
                        // Path is blocked or out of reach: clamp to closest reachable cell along the free path
                        currentHoveredGrid = puzzleGrid.GetClosestReachablePosition(reachablePositions, desiredGrid);

                        // If user is actively pulling towards an unreachable/blocked cell, trigger blocked shake
                        Vector3 desiredWorld = puzzleGrid.GridToWorld(desiredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector3 currentWorld = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);
                        Vector3 pullDiff = desiredWorld - currentWorld;

                        if (pullDiff.sqrMagnitude > 0.15f && selectedVehicle is BusVehicle bus)
                        {
                            bus.PlayBlockedShake(pullDiff.normalized);
                        }
                    }

                    // Keep GridPosition updated so crowd transfer controller knows where the bus is hovering
                    selectedVehicle.GridPosition = currentHoveredGrid;

                    // Calculate world position for currentHoveredGrid
                    Vector3 basePos = puzzleGrid.GridToWorld(currentHoveredGrid, selectedVehicle.Length, selectedVehicle.IsVertical);

                    // Add visual shake offset if blocked shake is active
                    Vector3 shakeOffset = Vector3.zero;
                    if (selectedVehicle is BusVehicle busVehicle)
                    {
                        shakeOffset = busVehicle.VisualShakeOffset;
                    }

                    Vector3 targetPos = new Vector3(basePos.x + shakeOffset.x, basePos.y + shakeOffset.y, initialWorldPos.z - 0.25f);
                    selectedVehicle.transform.position = Vector3.Lerp(selectedVehicle.transform.position, targetPos, Time.deltaTime * 32f);
                }
            }
            else if (isUp && selectedVehicle != null)
            {
                if (selectedVehicle.IsFull)
                {
                    selectedVehicle = null;
                    return;
                }

                if (reachablePositions.Contains(currentHoveredGrid) && puzzleGrid.CanPlace(selectedVehicle, currentHoveredGrid))
                {
                    // Placed successfully into the reached slot!
                    puzzleGrid.PlaceVehicle(selectedVehicle, currentHoveredGrid);
                }
                else
                {
                    // Invalid location or blocked: return safely to initial slot
                    puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);
                    if (selectedVehicle is BusVehicle bus)
                    {
                        bus.PlayBlockedShake(Vector3.up);
                    }
                }

                selectedVehicle = null;
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
                if (v != null && !v.IsFull)
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
