using System;
using UnityEngine;

namespace SandBlocks.Core.Boundary
{
    [Obsolete("Use ContainerBoundary which supports Rectangle, Funnel, and custom container shapes.")]
    public class FunnelBoundary : ContainerBoundary
    {
        protected override void Awake()
        {
            shapeType = ContainerShapeType.Funnel;
            base.Awake();
        }

        public void BuildVisualFunnel()
        {
            shapeType = ContainerShapeType.Funnel;
            BuildVisualContainer();
        }
    }
}
