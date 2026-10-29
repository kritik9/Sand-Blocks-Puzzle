using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace SandBlocks.Core.Boundary
{
    public enum ContainerShapeType
    {
        Rectangle = 0,
        CustomPolygon = 1
    }

    public class ContainerBoundary : MonoBehaviour, IContainerBoundary
    {
        [Header("Shape Configuration")]
        [SerializeField] protected ContainerShapeType shapeType = ContainerShapeType.Rectangle;
        [SerializeField] protected float width = 8.58f;
        [SerializeField] protected float height = 7.13f;
        [SerializeField] protected Vector2 containerCenter = new Vector2(0f, 0.63f);

        [Header("Border Offsets (Left, Right, Top, Bottom)")]
        [Tooltip("Independent X offset for Left border (negative moves left/outward, positive moves right/inward)")]
        [SerializeField] protected float leftBorderOffset = 0f;

        [Tooltip("Independent X offset for Right border (positive moves right/outward, negative moves left/inward)")]
        [SerializeField] protected float rightBorderOffset = 0f;

        [Tooltip("Independent Y offset for Top border (positive moves up/outward, negative moves down/inward)")]
        [SerializeField] protected float topBorderOffset = 0f;

        [Tooltip("Independent Y offset for Bottom border (negative moves down, positive moves up)")]
        [SerializeField] protected float bottomBorderOffset = 0f;

        [Header("Floor Exception Margin")]
        [Tooltip("Depth of the safety exception margin below the floor. Any particle pushed deeper than the floor line within this margin is captured, protected from falling, and restored to the floor.")]
        [SerializeField] protected float floorExceptionMargin = 0.8f;

        [Header("Custom Polygon Points (Local Coordinates)")]
        [Tooltip("Custom polygon vertices. You can move, add, and shape them directly in the Scene view with handles.")]
        [SerializeField] protected List<Vector2> customPolygonPoints = new List<Vector2>();

        [Header("Visual Styling")]
        [SerializeField] protected bool autoBuildVisuals = true;
        [SerializeField] protected Color borderColor = new Color(0.96f, 0.25f, 0.55f, 1f);
        [SerializeField] protected Color interiorColor = new Color(0.97f, 0.97f, 0.98f, 1f);
        [Tooltip("Thickness of the border beams")]
        [SerializeField] protected float borderBeamThickness = 0.32f;
        [Tooltip("Depth (Z-scale) of the border beams")]
        [SerializeField] protected float borderBeamDepth = 0.5f;
        [Tooltip("If true, creates the interior backplate")]
        [SerializeField] protected bool showBackPlate = true;

        [Header("Horizontal XZ Orientation Heights")]
        [Tooltip("Elevation Y offset for the back plate in Horizontal_XZ mode so it lies on the tabletop")]
        [SerializeField] protected float backPlateElevationY = 0.02f;
        [Tooltip("Height / thickness of the back plate in Horizontal_XZ mode")]
        [SerializeField] protected float backPlateThickness = 0.05f;
        [Tooltip("Elevation Y offset for the border beams in Horizontal_XZ mode")]
        [SerializeField] protected float borderBeamElevationY = 0.25f;
        [Tooltip("Height / vertical thickness of border beams in Horizontal_XZ mode")]
        [SerializeField] protected float borderBeamHeight = 0.5f;

        public float BackPlateElevationY { get => backPlateElevationY; set => backPlateElevationY = value; }
        public float BackPlateThickness { get => backPlateThickness; set => backPlateThickness = value; }
        public float BorderBeamElevationY { get => borderBeamElevationY; set => borderBeamElevationY = value; }
        public float BorderBeamHeight { get => borderBeamHeight; set => borderBeamHeight = value; }
        public bool ShowBackPlate => showBackPlate;
        public float SurfaceElevationY => backPlateElevationY + (showBackPlate ? backPlateThickness * 0.5f : 0f);

        protected List<LineSegment> segments = new List<LineSegment>();
        protected GameObject visualRoot;

        public struct LineSegment
        {
            public float2 a;
            public float2 b;
            public float2 normal;
        }

        public ContainerShapeType ShapeType => shapeType;
        public float BottomY => Bounds.yMin;
        public float TopY => Bounds.yMax;
        public float Width => width;
        public float Height => height;
        public Vector2 ContainerCenter => containerCenter;

        public float LeftBorderOffset => leftBorderOffset;
        public float RightBorderOffset => rightBorderOffset;
        public float TopBorderOffset => topBorderOffset;
        public float BottomBorderOffset => bottomBorderOffset;
        public float FloorExceptionMargin => floorExceptionMargin;
        public float BorderBeamThickness => borderBeamThickness;
        public float BorderBeamDepth => borderBeamDepth;

        public float LeftX => transform.position.x + containerCenter.x - width * 0.5f + leftBorderOffset;
        public float RightX => transform.position.x + containerCenter.x + width * 0.5f + rightBorderOffset;
        public float TopYCoord => transform.position.y + containerCenter.y + height * 0.5f + topBorderOffset;
        public float BottomYCoord => transform.position.y + containerCenter.y - height * 0.5f + bottomBorderOffset;

        public List<Vector2> CustomPolygonPoints => customPolygonPoints;

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

        public void SetCustomPolygonPoints(List<Vector2> points)
        {
            if (points == null || points.Count < 3) return;
            customPolygonPoints = new List<Vector2>(points);
            shapeType = ContainerShapeType.CustomPolygon;
            RebuildSegments();
            if (autoBuildVisuals)
            {
                BuildVisualContainer();
            }
        }

        public Rect Bounds
        {
            get
            {
                if (shapeType == ContainerShapeType.CustomPolygon && customPolygonPoints != null && customPolygonPoints.Count >= 3)
                {
                    float minX = float.MaxValue, maxX = float.MinValue;
                    float minY = float.MaxValue, maxY = float.MinValue;
                    Vector2 originPos = transform.position;

                    for (int i = 0; i < customPolygonPoints.Count; i++)
                    {
                        Vector2 p = originPos + customPolygonPoints[i];
                        if (p.x < minX) minX = p.x;
                        if (p.x > maxX) maxX = p.x;
                        if (p.y < minY) minY = p.y;
                        if (p.y > maxY) maxY = p.y;
                    }

                    return new Rect(minX, minY, Mathf.Max(0.1f, maxX - minX), Mathf.Max(0.1f, maxY - minY));
                }

                float lx = LeftX;
                float rx = RightX;
                float ty = TopYCoord;
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
            if (shapeType == ContainerShapeType.CustomPolygon)
            {
                EnsureDefaultPolygonPoints();
            }
            RebuildSegments();
            if (autoBuildVisuals && visualRoot == null)
            {
                BuildVisualContainer();
            }
        }

        protected virtual void OnValidate()
        {
            if (shapeType == ContainerShapeType.CustomPolygon)
            {
                EnsureDefaultPolygonPoints();
            }
            RebuildSegments();
#if UNITY_EDITOR
            if (autoBuildVisuals && !Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        if (autoBuildVisuals && !Application.isPlaying)
                        {
                            BuildVisualContainer();
                        }
                        var crowdRenderer = FindAnyObjectByType<SandBlocks.Core.Rendering.InstancedCrowdRenderer>();
                        if (crowdRenderer != null)
                        {
                            crowdRenderer.SyncLiveCharacterPositions();
                        }
                    }
                };
            }
            else
            {
                var crowdRenderer = FindAnyObjectByType<SandBlocks.Core.Rendering.InstancedCrowdRenderer>();
                if (crowdRenderer != null)
                {
                    crowdRenderer.SyncLiveCharacterPositions();
                }
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
                BuildCustomPolygonSegments();
            }
        }

        private void BuildRectangleSegments()
        {
            float leftX = LeftX;
            float rightX = RightX;
            float topY = TopYCoord;
            float bottomY = BottomYCoord;

            float2 topLeft = new float2(leftX, topY);
            float2 topRight = new float2(rightX, topY);
            float2 bottomRight = new float2(rightX, bottomY);
            float2 bottomLeft = new float2(leftX, bottomY);

            // Inward normals for rectangle (CCW traversal):
            // Bottom wall (Left -> Right): normal points UP (0, 1)
            AddSegment(bottomLeft, bottomRight);
            // Right wall (Bottom -> Top): normal points LEFT (-1, 0)
            AddSegment(bottomRight, topRight);
            // Top wall (Right -> Left): normal points DOWN (0, -1)
            AddSegment(topRight, topLeft);
            // Left wall (Top -> Bottom): normal points RIGHT (1, 0)
            AddSegment(topLeft, bottomLeft);
        }

        private void BuildCustomPolygonSegments()
        {
            EnsureDefaultPolygonPoints();
            if (customPolygonPoints.Count < 3) return;

            int n = customPolygonPoints.Count;

            // 1. Calculate signed area to detect winding order (Shoelace formula)
            float signedArea = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = customPolygonPoints[i];
                Vector2 b = customPolygonPoints[(i + 1) % n];
                signedArea += (a.x * b.y - b.x * a.y);
            }
            signedArea *= 0.5f;

            // If Counter-Clockwise (signedArea > 0): left-hand normal (-dir.y, dir.x) points INWARD
            // If Clockwise (signedArea < 0): right-hand normal (dir.y, -dir.x) points INWARD
            bool isCCW = signedArea >= 0f;

            // 2. Generate edges in world coordinates
            Vector2 originPos = transform.position;
            for (int i = 0; i < n; i++)
            {
                Vector2 pA = originPos + customPolygonPoints[i];
                Vector2 pB = originPos + customPolygonPoints[(i + 1) % n];

                float2 a = new float2(pA.x, pA.y);
                float2 b = new float2(pB.x, pB.y);

                float2 dir = b - a;
                float len = math.length(dir);
                if (len < 0.0001f) continue;
                dir /= len;

                // Guaranteed inward-pointing normal
                float2 normal = isCCW ? new float2(-dir.y, dir.x) : new float2(dir.y, -dir.x);
                segments.Add(new LineSegment { a = a, b = b, normal = normal });
            }
        }

        public void EnsureDefaultPolygonPoints()
        {
            if (customPolygonPoints == null) customPolygonPoints = new List<Vector2>();
            if (customPolygonPoints.Count >= 3) return;

            customPolygonPoints.Clear();
            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            Vector2 c = containerCenter;

            // Default CCW rectangular polygon
            customPolygonPoints.Add(new Vector2(c.x - halfW, c.y - halfH)); // Bottom-Left
            customPolygonPoints.Add(new Vector2(c.x + halfW, c.y - halfH)); // Bottom-Right
            customPolygonPoints.Add(new Vector2(c.x + halfW, c.y + halfH)); // Top-Right
            customPolygonPoints.Add(new Vector2(c.x - halfW, c.y + halfH)); // Top-Left
        }

        public void ResetToDefaultBox()
        {
            customPolygonPoints.Clear();
            EnsureDefaultPolygonPoints();
            RebuildSegments();
            BuildVisualContainer();
        }

        public void ResetToHexagon()
        {
            customPolygonPoints.Clear();
            float radius = Mathf.Min(width, height) * 0.5f;
            Vector2 c = containerCenter;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                customPolygonPoints.Add(new Vector2(c.x + radius * Mathf.Cos(angle), c.y + radius * Mathf.Sin(angle)));
            }
            RebuildSegments();
            BuildVisualContainer();
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
            if (shapeType == ContainerShapeType.Rectangle)
            {
                float left = LeftX + radius;
                float right = RightX - radius;
                float bottom = BottomYCoord + radius;
                float top = TopYCoord - radius;

                point.x = math.clamp(point.x, left, right);

                // Floor Exception Margin:
                // Catch any particle that penetrates below the bottom floor and firmly restore it
                if (point.y < bottom)
                {
                    point.y = bottom;
                }
                else if (point.y > top)
                {
                    point.y = top;
                }
                return point;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                float2 ab = seg.b - seg.a;
                float2 ap = point - seg.a;
                float t = math.clamp(math.dot(ap, ab) / math.lengthsq(ab), 0f, 1f);
                float2 closest = seg.a + ab * t;

                float2 diff = point - closest;
                // Signed distance along inward normal: positive is inside, negative is outside
                float signedDist = math.dot(diff, seg.normal);

                if (signedDist < radius)
                {
                    // Push firmly inward along seg.normal to restore radius clearance
                    point += seg.normal * (radius - signedDist);
                }
            }

            // Global safety floor exception clamp
            float globalFloor = Bounds.yMin + radius;
            if (point.y < globalFloor)
            {
                point.y = globalFloor;
            }

            return point;
        }

        [ContextMenu("Rebuild Visual Container")]
        public void BuildVisualContainer()
        {
            // Destroy all existing visual containers under this transform to avoid duplicates
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child != null && child.name.Contains("VisualContainer"))
                {
                    if (Application.isPlaying) Destroy(child.gameObject);
                    else DestroyImmediate(child.gameObject);
                }
            }
            visualRoot = null;

            visualRoot = new GameObject("VisualContainer");
            visualRoot.transform.SetParent(transform);
            visualRoot.transform.localPosition = Vector3.zero;
            visualRoot.transform.localRotation = Quaternion.identity;

            RebuildSegments();

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color = borderColor;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                Vector3 start = isHorizontal ? new Vector3(seg.a.x, borderBeamElevationY, seg.a.y) : new Vector3(seg.a.x, seg.a.y, 0f);
                Vector3 end = isHorizontal ? new Vector3(seg.b.x, borderBeamElevationY, seg.b.y) : new Vector3(seg.b.x, seg.b.y, 0f);
                Vector3 mid = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);

                var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = $"BorderBeam_{i}";
                beam.transform.SetParent(visualRoot.transform);
                beam.transform.position = mid;
                beam.transform.rotation = Quaternion.FromToRotation(Vector3.right, (end - start).normalized);
                beam.transform.localScale = isHorizontal 
                    ? new Vector3(length + borderBeamThickness, borderBeamHeight, borderBeamThickness)
                    : new Vector3(length + borderBeamThickness, borderBeamThickness, borderBeamDepth);

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
                backPlane.transform.position = isHorizontal 
                    ? new Vector3(b.center.x, backPlateElevationY, b.center.y) 
                    : new Vector3(b.center.x, b.center.y, 0.28f);
                backPlane.transform.localScale = isHorizontal
                    ? new Vector3(b.width, backPlateThickness, b.height)
                    : new Vector3(b.width, b.height, 0.05f);

                var bCol = backPlane.GetComponent<Collider>();
                if (bCol != null) DestroyImmediate(bCol);
                backPlane.GetComponent<Renderer>().sharedMaterial = backMat;
            }
        }

        private void OnDrawGizmos()
        {
            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            Gizmos.color = Color.cyan;
            for (int i = 0; i < segments.Count; i++)
            {
                if (isHorizontal)
                {
                    Gizmos.DrawLine(new Vector3(segments[i].a.x, 0.05f, segments[i].a.y), new Vector3(segments[i].b.x, 0.05f, segments[i].b.y));
                }
                else
                {
                    Gizmos.DrawLine(new Vector3(segments[i].a.x, segments[i].a.y, 0f), new Vector3(segments[i].b.x, segments[i].b.y, 0f));
                }
            }

            if (shapeType == ContainerShapeType.CustomPolygon && customPolygonPoints != null)
            {
                Gizmos.color = new Color(0.96f, 0.25f, 0.55f, 0.9f);
                Vector3 originPos = transform.position;
                for (int i = 0; i < customPolygonPoints.Count; i++)
                {
                    if (isHorizontal)
                    {
                        Gizmos.DrawWireSphere(originPos + new Vector3(customPolygonPoints[i].x, 0.05f, customPolygonPoints[i].y), 0.12f);
                    }
                    else
                    {
                        Gizmos.DrawWireSphere(originPos + (Vector3)customPolygonPoints[i], 0.12f);
                    }
                }
            }
        }
    }
}
