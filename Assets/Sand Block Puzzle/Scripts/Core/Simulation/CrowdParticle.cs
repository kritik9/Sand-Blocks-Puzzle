using System;
using Unity.Mathematics;

namespace SandBlocks.Core.Simulation
{
    [Serializable]
    public struct CrowdParticle
    {
        public float2 position;
        public float2 prevPosition;
        public float2 velocity;
        public float radius;
        public int colorId;
        public bool isActive;
        public bool isSettled;
    }
}
