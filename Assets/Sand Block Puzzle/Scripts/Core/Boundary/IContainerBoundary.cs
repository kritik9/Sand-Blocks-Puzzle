using Unity.Mathematics;
using UnityEngine;

namespace SandBlocks.Core.Boundary
{
    public interface IContainerBoundary
    {
        float2 ProjectInside(float2 point, float radius);
        bool IsInNozzleZone(float2 point, float radius);
        float2 NozzlePosition { get; }
        float NozzleRadius { get; }
    }
}
