using UnityEngine;

namespace SandBlocks.Puzzle.Vehicles
{
    public abstract class VehicleBase : MonoBehaviour, IVehicle
    {
        public const int MaxVehicleCapacity = 56;

        [Header("Vehicle Configuration")]
        [SerializeField] protected int colorId;
        [SerializeField] protected int capacity = 56;
        [SerializeField] protected int length = 2;
        [SerializeField] protected bool isVertical;

        protected int passengerCount;
        protected Vector2Int gridPosition;

        public int ColorId => colorId;
        public int Capacity => capacity;
        public int PassengerCount => passengerCount;
        public bool IsFull => passengerCount >= capacity;
        public bool IsVertical => isVertical;
        public int Length => length;
        public Vector2Int GridPosition
        {
            get => gridPosition;
            set => gridPosition = value;
        }

        [Header("Selection & Outline")]
        [SerializeField] protected SandBlocks.Core.Rendering.SelectionOutline selectionOutline;
        protected bool isSelected;

        public bool IsSelected => isSelected;
        public SandBlocks.Core.Rendering.SelectionOutline SelectionOutline => selectionOutline;

        /// <summary>
        /// Toggles visual selection state and highlights the vehicle with an outline.
        /// </summary>
        public virtual void SetSelected(bool selected)
        {
            isSelected = selected;
            if (selectionOutline == null)
            {
                selectionOutline = GetComponent<SandBlocks.Core.Rendering.SelectionOutline>() 
                                ?? GetComponentInChildren<SandBlocks.Core.Rendering.SelectionOutline>();
                if (selectionOutline == null)
                {
                    selectionOutline = gameObject.AddComponent<SandBlocks.Core.Rendering.SelectionOutline>();
                }
            }

            if (selectionOutline != null)
            {
                selectionOutline.SetOutlineEnabled(selected);
            }
        }

        public abstract bool TryAddPassenger(out Vector3 targetSeatWorldPos);
        public abstract void OnFull();
        public abstract void Initialize(int id, int cap, int len, bool vertical, Vector2Int startPos);
    }
}
