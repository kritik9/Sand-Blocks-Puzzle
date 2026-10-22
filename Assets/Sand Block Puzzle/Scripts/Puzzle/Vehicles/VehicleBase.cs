using UnityEngine;

namespace SandBlocks.Puzzle.Vehicles
{
    public abstract class VehicleBase : MonoBehaviour, IVehicle
    {
        [Header("Vehicle Configuration")]
        [SerializeField] protected int colorId;
        [SerializeField] protected int capacity = 4;
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

        public abstract bool TryAddPassenger(out Vector3 targetSeatWorldPos);
        public abstract void OnFull();
        public abstract void Initialize(int id, int cap, int len, bool vertical, Vector2Int startPos);
    }
}
