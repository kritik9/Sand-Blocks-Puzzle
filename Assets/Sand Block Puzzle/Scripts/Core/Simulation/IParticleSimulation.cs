using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SandBlocks.Core.Boundary;

namespace SandBlocks.Core.Simulation
{
    public interface IParticleSimulation
    {
        void Initialize(int maxCapacity, IContainerBoundary boundary);
        void SpawnParticle(float2 position, int colorId, float radius, bool startSettled = true);
        void StepSimulation(float deltaTime);
        int ActiveCount { get; }
        bool IsInitialized { get; }
        int GetLowestParticleColor();
        bool TryExtractLowestParticle(int colorId, out Vector3 worldPos);
        bool HasColorOnBottomBoundary(int colorId, float bottomY, float tolerance = 0.65f);
        bool TryExtractBottomParticle(int colorId, float bottomY, float targetX, out Vector3 worldPos, float tolerance = 0.65f);
        NativeArray<CrowdParticle> Particles { get; }
        void Dispose();
    }
}
