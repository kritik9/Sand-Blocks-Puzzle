using UnityEngine;
using SandBlocks.Core.Boundary;
using SandBlocks.Puzzle.Grid;

namespace SandBlocks.Gameplay
{
    /// <summary>
    /// Automatically adjusts an orthographic camera's size and vertical position
    /// so that both the crowd container and puzzle grid fit completely within the view,
    /// regardless of grid dimensions (columns/rows), camera tilt angle, or device aspect ratio.
    /// </summary>
    [ExecuteAlways]
    public class AdaptiveCameraFramer : MonoBehaviour
    {
        [Header("Target References")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private ContainerBoundary boundary;
        [SerializeField] private PuzzleGrid puzzleGrid;

        [Header("Framing Settings")]
        [Tooltip("If true, automatically adjusts the camera at Start")]
        [SerializeField] private bool autoFrameOnStart = true;

        [Tooltip("Buffer above the container (units) for aesthetics")]
        [SerializeField] private float topMargin = 0.9f;

        [Tooltip("Buffer below the lowest bus/grid line (units) to clear bottom UI buttons (Prev, Next, etc.)")]
        [SerializeField] private float bottomMargin = 2.4f;

        [Tooltip("Buffer on left and right sides (units)")]
        [SerializeField] private float sideMargin = 1.0f;

        [Tooltip("Minimum orthographic size allowed")]
        [SerializeField] private float minOrthographicSize = 8.5f;

        [Tooltip("Maximum orthographic size allowed")]
        [SerializeField] private float maxOrthographicSize = 25.0f;

        [Tooltip("Optional fine-tuning vertical offset")]
        [SerializeField] private float verticalCenterOffset = 0f;

        [Header("Top-View Camera Settings (Horizontal XZ Mode)")]
        [Tooltip("Angle in degrees for the top-view camera tilting down at the ground plane (e.g. 50 to 65 deg). Matches hypercasual reference image.")]
        [Range(25f, 85f)]
        [SerializeField] private float topViewTiltAngle = 55f;

        [Tooltip("Height of the camera above the ground plane in Horizontal_XZ mode")]
        [SerializeField] private float cameraElevation = 18f;

        public float TopViewTiltAngle { get => topViewTiltAngle; set { topViewTiltAngle = value; FrameView(); } }
        public float CameraElevation { get => cameraElevation; set { cameraElevation = value; FrameView(); } }

        private int lastCols = -1;
        private int lastRows = -1;
        private float lastAspect = -1f;

        private void Awake()
        {
            FindReferences();
        }

        private void OnEnable()
        {
            FindReferences();
            if (puzzleGrid != null)
            {
                puzzleGrid.OnGridDimensionsChanged -= FrameView;
                puzzleGrid.OnGridDimensionsChanged += FrameView;
            }
            FrameView();
        }

        private void OnDisable()
        {
            if (puzzleGrid != null)
            {
                puzzleGrid.OnGridDimensionsChanged -= FrameView;
            }
        }

        private void Start()
        {
            FindReferences();
            if (autoFrameOnStart)
            {
                FrameView();
            }
        }

        private void LateUpdate()
        {
            if (targetCamera == null || puzzleGrid == null)
            {
                FindReferences();
            }

            if (targetCamera == null || puzzleGrid == null) return;

            int cols = puzzleGrid.Columns;
            int rows = puzzleGrid.Rows;
            float aspect = targetCamera.aspect;

            if (cols != lastCols || rows != lastRows || Mathf.Abs(aspect - lastAspect) > 0.01f)
            {
                lastCols = cols;
                lastRows = rows;
                lastAspect = aspect;
                FrameView();
            }
        }

        public void FindReferences()
        {
            if (targetCamera == null)
            {
                targetCamera = GetComponent<Camera>();
                if (targetCamera == null) targetCamera = Camera.main;
            }
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
        }

        /// <summary>
        /// Calculates the bounding box of both the container and puzzle grid,
        /// and fits the orthographic camera smoothly to frame all gameplay elements.
        /// Fully compensates for any camera rotation tilt pitch (e.g. -16 degrees or +28 degrees).
        /// </summary>
        [ContextMenu("Frame Camera View Now")]
        public void FrameView()
        {
            FindReferences();
            if (targetCamera == null) return;

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            if (isHorizontal)
            {
                FrameViewHorizontal();
            }
            else
            {
                FrameViewVertical();
            }
        }

        private void FrameViewHorizontal()
        {
            // 1. Determine extents on X and Z
            float topZ = 3.5f;
            float bottomZ = -9.5f;
            float maxContentWidth = 8.1f;
            float centerX = 0f;

            if (boundary != null && boundary.gameObject.activeInHierarchy)
            {
                Rect bRect = boundary.Bounds;
                topZ = bRect.yMax;
                maxContentWidth = Mathf.Max(maxContentWidth, bRect.width);
                centerX = boundary.transform.position.x + boundary.ContainerCenter.x;
            }
            else if (puzzleGrid != null)
            {
                topZ = puzzleGrid.TopY + puzzleGrid.CellSize * 0.5f;
                centerX = puzzleGrid.CenterX;
            }

            if (puzzleGrid != null)
            {
                float gridBottom = puzzleGrid.BottomY - puzzleGrid.CellSize * 0.5f;
                bottomZ = gridBottom;

                float gridWidth = puzzleGrid.Columns * puzzleGrid.CellSize + 0.6f;
                maxContentWidth = Mathf.Max(maxContentWidth, gridWidth);
            }

            // 2. Expand extents by margins
            float effectiveTopZ = topZ + topMargin;
            float effectiveBottomZ = bottomZ - bottomMargin;
            float totalSpanZ = effectiveTopZ - effectiveBottomZ;
            float totalWidthX = maxContentWidth + (sideMargin * 2f);
            float targetCenterZ = (effectiveTopZ + effectiveBottomZ) * 0.5f + verticalCenterOffset;

            // 3. Set camera rotation to top-view pitch tilt
            targetCamera.transform.rotation = Quaternion.Euler(topViewTiltAngle, 0f, 0f);

            // 4. Calculate camera position so line of sight hits ground at targetCenterZ
            float pitchRad = topViewTiltAngle * Mathf.Deg2Rad;
            float sinPitch = Mathf.Sin(pitchRad);
            float cosPitch = Mathf.Cos(pitchRad);

            float camY = cameraElevation;
            float groundOffsetFromPitch = (Mathf.Abs(sinPitch) > 0.001f) ? (camY * (cosPitch / sinPitch)) : 0f;
            float camZ = targetCenterZ - groundOffsetFromPitch;

            targetCamera.transform.position = new Vector3(centerX, camY, camZ);

            // 5. Orthographic size adjustment
            if (targetCamera.orthographic)
            {
                float aspect = Mathf.Max(0.2f, targetCamera.aspect);
                float projectedHeight = totalSpanZ * sinPitch;
                float sizeFromHeight = projectedHeight * 0.5f;
                float sizeFromWidth = (totalWidthX * 0.5f) / aspect;

                float targetSize = Mathf.Clamp(Mathf.Max(sizeFromHeight, sizeFromWidth), minOrthographicSize, maxOrthographicSize);
                targetCamera.orthographicSize = targetSize;
            }
        }

        private void FrameViewVertical()
        {
            if (!targetCamera.orthographic) return;

            // 1. Determine vertical and horizontal content extents on Z = 0
            float topY = 3.5f;
            float bottomY = -9.5f;
            float maxContentWidth = 8.1f;

            if (boundary != null && boundary.gameObject.activeInHierarchy)
            {
                Rect bRect = boundary.Bounds;
                topY = bRect.yMax;
                maxContentWidth = Mathf.Max(maxContentWidth, bRect.width);
            }
            else if (puzzleGrid != null)
            {
                topY = puzzleGrid.TopY + puzzleGrid.CellSize * 0.5f;
            }

            if (puzzleGrid != null)
            {
                float gridBottom = puzzleGrid.BottomY - puzzleGrid.CellSize * 0.5f;
                bottomY = gridBottom;

                float gridWidth = puzzleGrid.Columns * puzzleGrid.CellSize + 0.6f;
                maxContentWidth = Mathf.Max(maxContentWidth, gridWidth);
            }

            // 2. Expand extents by margins
            float effectiveTop = topY + topMargin;
            float effectiveBottom = bottomY - bottomMargin;
            float totalHeight = effectiveTop - effectiveBottom;
            float totalWidth = maxContentWidth + (sideMargin * 2f);

            // 3. Compensation for camera tilt angle (X rotation)
            Vector3 forward = targetCamera.transform.forward;
            float cosAngle = 1f;
            if (Mathf.Abs(forward.z) > 0.001f)
            {
                cosAngle = Mathf.Clamp01(Mathf.Abs(forward.z));
            }

            float aspect = Mathf.Max(0.2f, targetCamera.aspect);
            float sizeFromHeight = (totalHeight * cosAngle) * 0.5f;
            float sizeFromWidth = (totalWidth * 0.5f) / aspect;

            float targetSize = Mathf.Clamp(Mathf.Max(sizeFromHeight, sizeFromWidth), minOrthographicSize, maxOrthographicSize);
            targetCamera.orthographicSize = targetSize;

            float targetCenterY = (effectiveTop + effectiveBottom) * 0.5f + verticalCenterOffset;

            Vector3 camPos = targetCamera.transform.position;
            float pitchAdjustment = 0f;
            if (Mathf.Abs(forward.z) > 0.001f)
            {
                pitchAdjustment = camPos.z * (forward.y / forward.z);
            }

            float newCamY = targetCenterY + pitchAdjustment;
            targetCamera.transform.position = new Vector3(camPos.x, newCamY, camPos.z);
        }
    }
}
