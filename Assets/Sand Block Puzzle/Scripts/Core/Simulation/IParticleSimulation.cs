using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SandBlocks.Core.Boundary;

namespace SandBlocks.Core.Simulation
{
    public interface IParticleSimulation
    {
        void Initialize(int maxCapacity, IContainerBoundary boundary);
        void SpawnParticle(float2 position, int colorId, float radius);
        void StepSimulation(float deltaTime);
        int ActiveCount { get; }
        int GetLowestParticleColor();
        bool TryExtractLowestParticle(int colorId, out Vector3 worldPos);
        NativeArray<CrowdParticle> Particles { get; }
        void Dispose();
    }
}
