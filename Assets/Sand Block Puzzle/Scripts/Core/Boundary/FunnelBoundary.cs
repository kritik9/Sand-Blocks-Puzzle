using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SandBlocks.Core.Boundary
{
    public class FunnelBoundary : MonoBehaviour, IContainerBoundary
    {
        [Header("Funnel Geometry")]
        [SerializeField] private float width = 5.2f;
        [SerializeField] private float nozzleWidth = 1.2f;
        [SerializeField] private float nozzleY = -3.0f;
        [SerializeField] private float topY = 3.0f;

        [Header("Nozzle Zone")]
        [SerializeField] private float nozzleDetectionRadius = 1.4f;

        private List<LineSegment> segments = new List<LineSegment>();

        public struct LineSegment
        {
            public float2 a;
            public float2 b;
            public float2 normal;
        }

        public float2 NozzlePosition => new float2(transform.position.x, transform.position.y + nozzleY);
        public float NozzleRadius => nozzleDetectionRadius;

        private void Awake()
        {
            RebuildSegments();
        }

        private void OnValidate()
        {
            RebuildSegments();
        }

        public void RebuildSegments()
        {
            segments.Clear();
            float halfW = width * 0.5f;
            float halfN = nozzleWidth * 0.5f;
            float cx = transform.position.x;
            float cy = transform.position.y;

            // Lips / Funnel polygon points (Left and Right symmetric wall)
            float2 topLeft = new float2(cx - halfW, cy + topY);
            float2 topRight = new float2(cx + halfW, cy + topY);
            float2 midLeft = new float2(cx - halfW * 1.15f, cy + (topY + nozzleY) * 0.45f);
            float2 midRight = new float2(cx + halfW * 1.15f, cy + (topY + nozzleY) * 0.45f);
            float2 neckLeft = new float2(cx - halfN, cy + nozzleY);
            float2 neckRight = new float2(cx + halfN, cy + nozzleY);
            float2 nozzleBottom = new float2(cx, cy + nozzleY - 0.2f);

            // Left wall
            AddSegment(topLeft, midLeft);
            AddSegment(midLeft, neckLeft);

            // Right wall
            AddSegment(topRight, midRight);
            AddSegment(midRight, neckRight);

            // Bottom gate / nozzle support
            AddSegment(neckLeft, nozzleBottom);
            AddSegment(nozzleBottom, neckRight);

            // Top boundary
            AddSegment(topLeft, topRight);
        }

        private void AddSegment(float2 a, float2 b)
        {
            float2 dir = math.normalize(b - a);
            float2 n = new float2(-dir.y, dir.x);
            segments.Add(new LineSegment { a = a, b = b, normal = n });
        }

        public float2 ProjectInside(float2 point, float radius)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                float2 ab = s.b - s.a;
                float t = math.clamp(math.dot(point - s.a, ab) / math.lengthsq(ab), 0f, 1f);
                float2 closest = s.a + t * ab;
                float2 diff = point - closest;
                float dist = math.length(diff);

                if (dist < radius)
                {
                    float2 pushDir = dist > 0.0001f ? diff / dist : s.normal;
                    point = closest + pushDir * radius;
                }
            }
            return point;
        }

        public bool IsInNozzleZone(float2 point, float radius)
        {
            return math.distance(point, NozzlePosition) <= (nozzleDetectionRadius + radius);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            RebuildSegments();
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                Gizmos.DrawLine(new Vector3(s.a.x, s.a.y, 0f), new Vector3(s.b.x, s.b.y, 0f));
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(new Vector3(NozzlePosition.x, NozzlePosition.y, 0f), nozzleDetectionRadius);
        }
    }
}
