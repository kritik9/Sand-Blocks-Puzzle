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
        private Plane dragPlane;

        public VehicleBase HoveredVehicle => selectedVehicle;

        private void Awake()
        {
            if (gameCamera == null) gameCamera = Camera.main;
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            dragPlane = new Plane(Vector3.forward, Vector3.zero);
        }

        private void Update()
        {
            HandleInput();
        }

        private void HandleInput()
        {
            bool isDown = false;
            bool isHeld = false;
            bool isUp = false;
            Vector2 screenPos = Vector2.zero;

            if (Mouse.current != null)
            {
                isDown = Mouse.current.leftButton.wasPressedThisFrame;
                isHeld = Mouse.current.leftButton.isPressed;
                isUp = Mouse.current.leftButton.wasReleasedThisFrame;
                screenPos = Mouse.current.position.ReadValue();
            }
            if (!isDown && !isHeld && !isUp && Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                isDown = touch.press.wasPressedThisFrame;
                isHeld = touch.press.isPressed;
                isUp = touch.press.wasReleasedThisFrame;
                screenPos = touch.position.ReadValue();
            }

            if (isDown)
            {
                Ray ray = gameCamera.ScreenPointToRay(screenPos);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    var vehicle = hit.collider.GetComponentInParent<VehicleBase>();
                    if (vehicle != null && !vehicle.IsFull)
                    {
                        selectedVehicle = vehicle;
                        initialWorldPos = vehicle.transform.position;
                        initialGridPos = vehicle.GridPosition;

                        if (dragPlane.Raycast(ray, out float enter))
                        {
                            dragOffset = vehicle.transform.position - ray.GetPoint(enter);
                        }
                    }
                }
            }
            else if (isHeld && selectedVehicle != null)
            {
                Ray ray = gameCamera.ScreenPointToRay(screenPos);
                if (dragPlane.Raycast(ray, out float enter))
                {
                    Vector3 targetPos = ray.GetPoint(enter) + dragOffset;
                    Vector3 currentPos = selectedVehicle.transform.position;

                    if (selectedVehicle.IsVertical)
                    {
                        currentPos.y = targetPos.y;
                    }
                    else
                    {
                        currentPos.x = targetPos.x;
                    }

                    selectedVehicle.transform.position = currentPos;
                }
            }
            else if (isUp && selectedVehicle != null)
            {
                Vector2Int candidateGrid = puzzleGrid.WorldToGrid(selectedVehicle.transform.position);

                if (puzzleGrid.CanPlace(selectedVehicle, candidateGrid))
                {
                    puzzleGrid.PlaceVehicle(selectedVehicle, candidateGrid);
                }
                else
                {
                    puzzleGrid.PlaceVehicle(selectedVehicle, initialGridPos);
                }

                selectedVehicle = null;
            }
        }
    }
}
