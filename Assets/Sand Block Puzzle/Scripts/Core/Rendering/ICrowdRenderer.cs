using Unity.Collections;
using SandBlocks.Core.Data;
using SandBlocks.Core.Simulation;

namespace SandBlocks.Core.Rendering
{
    public interface ICrowdRenderer
    {
        void Initialize(CrowdColorPalette palette);
        void Render(NativeArray<CrowdParticle> particles, int count);
    }
}
