using Unity.Mathematics;
using UnityEngine;

namespace SandBlocks.Core.Boundary
{
    public interface IContainerBoundary
    {
        float2 ProjectInside(float2 point, float radius);
        Rect Bounds { get; }
        Rect VisualBounds => Bounds;
        float BottomY => Bounds.yMin;
        float FloorExceptionMargin => 0.8f;
        float SurfaceElevationY => 0.05f;
        float BackPlateElevationY => 0.02f;
    }
}
