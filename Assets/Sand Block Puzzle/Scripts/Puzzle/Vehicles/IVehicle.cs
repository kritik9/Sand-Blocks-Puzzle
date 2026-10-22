using UnityEngine;

namespace SandBlocks.Puzzle.Vehicles
{
    public interface IVehicle
    {
        int ColorId { get; }
        int Capacity { get; }
        int PassengerCount { get; }
        bool IsFull { get; }
        bool IsVertical { get; }
        int Length { get; }
        Vector2Int GridPosition { get; set; }
        bool TryAddPassenger(out Vector3 targetSeatWorldPos);
        void OnFull();
    }
}
