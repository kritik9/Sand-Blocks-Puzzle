using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SandBlocks.Core.Boundary
{
    public enum ContainerShapeType
    {
        Rectangle,
        Funnel,
        CustomPolygon
    }

    public class ContainerBoundary : MonoBehaviour, IContainerBoundary
    {
        [Header("Shape Configuration")]
        [SerializeField] protected ContainerShapeType shapeType = ContainerShapeType.Rectangle;
        [SerializeField] protected float width = 5.4f;
        [SerializeField] protected float height = 5.8f;
        [SerializeField] protected Vector2 containerCenter = new Vector2(0f, 0.4f);

        [Header("Border Offsets (Left, Right, Top, Bottom)")]
        [Tooltip("Independent X offset for Left border (negative moves left/outward, positive moves right/inward)")]
        [SerializeField] protected float leftBorderOffset = 0f;

        [Tooltip("Independent X offset for Right border (positive moves right/outward, negative moves left/inward)")]
        [SerializeField] protected float rightBorderOffset = 0f;

        [Tooltip("Independent Y offset for Top border (positive moves up/outward, negative moves down/inward)")]
        [SerializeField] protected float topBorderOffset = 0f;

        [Tooltip("Independent Y offset for Bottom border (negative moves down, positive moves up)")]
        [SerializeField] protected float bottomBorderOffset = 0f;

        [Header("Bottom Wall & Exit Enclosure")]
        [Tooltip("If true, encloses the container with a solid bottom wall so particles never fall out automatically from gravity")]
        [SerializeField] protected bool encloseBottomWall = true;
        [Tooltip("Exit opening width if encloseBottomWall is false")]
        [SerializeField] protected float exitWidth = 1.3f;
        [SerializeField] protected float nozzleDetectionRadius = 1.6f;

        [Header("Funnel Specific Settings")]
        [SerializeField] protected float funnelTopY = 3.0f;
        [SerializeField] protected float funnelNozzleY = -3.0f;

        [Header("Visual Styling")]
        [SerializeField] protected bool autoBuildVisuals = true;
        [SerializeField] protected Color borderColor = new Color(0.96f, 0.25f, 0.55f, 1f);
        [SerializeField] protected Color interiorColor = new Color(0.97f, 0.97f, 0.98f, 1f);
        [Tooltip("Thickness of the border beams")]
        [SerializeField] protected float borderBeamThickness = 0.32f;
        [Tooltip("Depth (Z-scale) of the border beams")]
        [SerializeField] protected float borderBeamDepth = 0.5f;
        [Tooltip("If true, creates the interior backplate cube")]
        [SerializeField] protected bool showBackPlate = true;

        protected List<LineSegment> segments = new List<LineSegment>();
        protected GameObject visualRoot;

        public struct LineSegment
        {
            public float2 a;
            public float2 b;
            public float2 normal;
        }

        public ContainerShapeType ShapeType => shapeType;
        public float NozzleRadius => nozzleDetectionRadius;
        public float BottomY => Bounds.yMin;
        public bool EncloseBottomWall => encloseBottomWall;
        public float Width => width;
        public float Height => height;
        public Vector2 ContainerCenter => containerCenter;

        public float LeftBorderOffset => leftBorderOffset;
        public float RightBorderOffset => rightBorderOffset;
        public float TopBorderOffset => topBorderOffset;
        public float BottomBorderOffset => bottomBorderOffset;
        public float BorderBeamThickness => borderBeamThickness;
        public float BorderBeamDepth => borderBeamDepth;

        public float LeftX => transform.position.x + containerCenter.x - width * 0.5f + leftBorderOffset;
        public float RightX => transform.position.x + containerCenter.x + width * 0.5f + rightBorderOffset;
        public float TopY => transform.position.y + containerCenter.y + height * 0.5f + topBorderOffset;
        public float BottomYCoord => transform.position.y + containerCenter.y - height * 0.5f + bottomBorderOffset;

        public void SetContainerShape(ContainerShapeType type, float w, float h, Vector2 center)
        {
            SetContainerShape(type, w, h, center, leftBorderOffset, rightBorderOffset, topBorderOffset, bottomBorderOffset);
        }

        public void SetContainerShape(ContainerShapeType type, float w, float h, Vector2 center,
            float leftOffset, float rightOffset, float topOffset, float bottomOffset = 0f)
        {
            shapeType = type;
            width = Mathf.Max(2f, w);
            height = Mathf.Max(2f, h);
            containerCenter = center;
            leftBorderOffset = leftOffset;
            rightBorderOffset = rightOffset;
            topBorderOffset = topOffset;
            bottomBorderOffset = bottomOffset;
            RebuildSegments();
            if (autoBuildVisuals)
            {
                BuildVisualContainer();
            }
        }

        public float2 NozzlePosition
        {
            get
            {
                if (shapeType == ContainerShapeType.Funnel)
                {
                    return new float2(transform.position.x, transform.position.y + funnelNozzleY + bottomBorderOffset);
                }
                return new float2(transform.position.x + containerCenter.x, transform.position.y + containerCenter.y - height * 0.5f + bottomBorderOffset);
            }
        }

        public Rect Bounds
        {
            get
            {
                if (shapeType == ContainerShapeType.Funnel)
                {
                    float minX = transform.position.x - width * 0.5f + leftBorderOffset;
                    float maxX = transform.position.x + width * 0.5f + rightBorderOffset;
                    float minY = transform.position.y + funnelNozzleY + bottomBorderOffset;
                    float maxY = transform.position.y + funnelTopY + topBorderOffset;
                    return new Rect(minX, minY, Mathf.Max(0.1f, maxX - minX), Mathf.Max(0.1f, maxY - minY));
                }

                float lx = LeftX;
                float rx = RightX;
                float ty = TopY;
                float by = BottomYCoord;

                float xMin = Mathf.Min(lx, rx);
                float xMax = Mathf.Max(lx, rx);
                float yMin = Mathf.Min(by, ty);
                float yMax = Mathf.Max(by, ty);

                return new Rect(xMin, yMin, Mathf.Max(0.1f, xMax - xMin), Mathf.Max(0.1f, yMax - yMin));
            }
        }

        protected virtual void Awake()
        {
            RebuildSegments();
            if (autoBuildVisuals && visualRoot == null)
            {
                BuildVisualContainer();
            }
        }

        protected virtual void OnValidate()
        {
            RebuildSegments();
#if UNITY_EDITOR
            if (autoBuildVisuals && !Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && autoBuildVisuals && !Application.isPlaying)
                    {
                        BuildVisualContainer();
                    }
                };
            }
#endif
        }

        public virtual void RebuildSegments()
        {
            segments.Clear();

            if (shapeType == ContainerShapeType.Rectangle)
            {
                BuildRectangleSegments();
            }
            else
            {
                BuildFunnelSegments();
            }
        }

        private void BuildRectangleSegments()
        {
            float cx = transform.position.x + containerCenter.x;
            float leftX = LeftX;
            float rightX = RightX;
            float topY = TopY;
            float bottomY = BottomYCoord;

            float2 topLeft = new float2(leftX, topY);
            float2 topRight = new float2(rightX, topY);
            float2 bottomRight = new float2(rightX, bottomY);
            float2 bottomLeft = new float2(leftX, bottomY);

            // Top wall (Index 0)
            AddSegment(topLeft, topRight);
            // Right wall (Index 1)
            AddSegment(topRight, bottomRight);

            if (encloseBottomWall)
            {
                // Solid bottom wall (Index 2): Completely enclosed so particles never fall out!
                AddSegment(bottomRight, bottomLeft);
            }
            else
            {
                float halfNozzle = exitWidth * 0.5f;
                float2 nozzleRight = new float2(cx + halfNozzle, bottomY);
                float2 nozzleLeft = new float2(cx - halfNozzle, bottomY);
                AddSegment(bottomRight, nozzleRight);
                AddSegment(nozzleLeft, bottomLeft);
            }

            // Left wall (Index 3 if enclosed, Index 4 if nozzle opening)
            AddSegment(bottomLeft, topLeft);
        }

        private void BuildFunnelSegments()
        {
            float halfN = exitWidth * 0.5f;
            float posX = transform.position.x;
            float posY = transform.position.y;

            float leftX = posX - width * 0.5f + leftBorderOffset;
            float rightX = posX + width * 0.5f + rightBorderOffset;
            float topY = posY + funnelTopY + topBorderOffset;
            float nozzleY = posY + funnelNozzleY + bottomBorderOffset;

            float2 pTopLeft = new float2(leftX, topY);
            float2 pTopRight = new float2(rightX, topY);
            float2 pMidRight = new float2(rightX * 0.95f, (topY + nozzleY) * 0.5f);
            float2 pNozzleRight = new float2(posX + halfN, nozzleY);
            float2 pNozzleLeft = new float2(posX - halfN, nozzleY);
            float2 pMidLeft = new float2(leftX * 0.95f, (topY + nozzleY) * 0.5f);

            AddSegment(pTopLeft, pTopRight);
            AddSegment(pTopRight, pMidRight);
            AddSegment(pMidRight, pNozzleRight);
            if (encloseBottomWall)
            {
                AddSegment(pNozzleRight, pNozzleLeft);
            }
            AddSegment(pNozzleLeft, pMidLeft);
            AddSegment(pMidLeft, pTopLeft);
        }

        protected void AddSegment(float2 a, float2 b)
        {
            float2 dir = b - a;
            float len = math.length(dir);
            if (len < 0.0001f) return;
            dir /= len;
            float2 normal = new float2(-dir.y, dir.x);
            segments.Add(new LineSegment { a = a, b = b, normal = normal });
        }

        public float2 ProjectInside(float2 point, float radius)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                float2 ab = seg.b - seg.a;
                float2 ap = point - seg.a;
                float t = math.clamp(math.dot(ap, ab) / math.lengthsq(ab), 0f, 1f);
                float2 closest = seg.a + ab * t;

                float2 diff = point - closest;
                float dist = math.length(diff);

                if (dist < radius)
                {
                    float2 pushDir = dist > 0.0001f ? (diff / dist) : seg.normal;
                    point = closest + pushDir * radius;
                }
            }
            return point;
        }

        public bool IsInNozzleZone(float2 point, float radius)
        {
            return math.distance(point, NozzlePosition) <= (nozzleDetectionRadius + radius);
        }

        [ContextMenu("Rebuild Visual Container")]
        public void BuildVisualContainer()
        {
            if (visualRoot == null)
            {
                var existing = transform.Find("VisualContainer");
                if (existing != null) visualRoot = existing.gameObject;
            }

            if (visualRoot != null)
            {
                if (Application.isPlaying) Destroy(visualRoot);
                else DestroyImmediate(visualRoot);
            }

            visualRoot = new GameObject("VisualContainer");
            visualRoot.transform.SetParent(transform);
            visualRoot.transform.localPosition = Vector3.zero;

            RebuildSegments();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = borderColor;

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                Vector3 start = new Vector3(seg.a.x, seg.a.y, 0f);
                Vector3 end = new Vector3(seg.b.x, seg.b.y, 0f);
                Vector3 mid = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);

                var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = GetSegmentName(i, segments.Count);
                beam.transform.SetParent(visualRoot.transform);
                beam.transform.position = mid;
                beam.transform.rotation = Quaternion.FromToRotation(Vector3.right, (end - start).normalized);
                beam.transform.localScale = new Vector3(length + borderBeamThickness, borderBeamThickness, borderBeamDepth);

                var col = beam.GetComponent<Collider>();
                if (col != null) DestroyImmediate(col);

                beam.GetComponent<Renderer>().sharedMaterial = mat;
            }

            // Backing plate
            if (showBackPlate)
            {
                var backMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                backMat.color = interiorColor;

                var backPlane = GameObject.CreatePrimitive(PrimitiveType.Cube);
                backPlane.name = "BackPlate";
                backPlane.transform.SetParent(visualRoot.transform);
                Rect b = Bounds;
                backPlane.transform.position = new Vector3(b.center.x, b.center.y, 0.28f);
                backPlane.transform.localScale = new Vector3(b.width, b.height, 0.05f);

                var bCol = backPlane.GetComponent<Collider>();
                if (bCol != null) DestroyImmediate(bCol);
                backPlane.GetComponent<Renderer>().sharedMaterial = backMat;
            }
        }

        private string GetSegmentName(int index, int totalSegments)
        {
            if (totalSegments == 4)
            {
                switch (index)
                {
                    case 0: return "BorderBeam_Top";
                    case 1: return "BorderBeam_Right";
                    case 2: return "BorderBeam_Bottom";
                    case 3: return "BorderBeam_Left";
                }
            }
            else if (totalSegments == 5)
            {
                switch (index)
                {
                    case 0: return "BorderBeam_Top";
                    case 1: return "BorderBeam_Right";
                    case 2: return "BorderBeam_BottomRight";
                    case 3: return "BorderBeam_BottomLeft";
                    case 4: return "BorderBeam_Left";
                }
            }
            return $"BorderBeam_{index}";
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < segments.Count; i++)
            {
                Gizmos.DrawLine(new Vector3(segments[i].a.x, segments[i].a.y, 0f), new Vector3(segments[i].b.x, segments[i].b.y, 0f));
            }
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(new Vector3(NozzlePosition.x, NozzlePosition.y, 0f), nozzleDetectionRadius);
        }
    }
}
