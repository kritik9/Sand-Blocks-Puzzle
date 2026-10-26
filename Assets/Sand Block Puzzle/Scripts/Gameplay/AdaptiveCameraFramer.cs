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
            if (targetCamera == null || !targetCamera.orthographic) return;

            // 1. Determine vertical and horizontal content extents on Z = 0
            float topY = 3.5f;
            float bottomY = -9.5f;
            float maxContentWidth = 8.1f;

            if (boundary != null)
            {
                Rect bRect = boundary.Bounds;
                topY = bRect.yMax;
                maxContentWidth = Mathf.Max(maxContentWidth, bRect.width);
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
            // An orthographic camera tilted at angle theta compresses perspective in Y by cos(theta)
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

            // 4. Center camera on the midpoint of the gameplay elements
            // We want the camera line of sight to hit Z = 0 at targetCenterY:
            float targetCenterY = (effectiveTop + effectiveBottom) * 0.5f + verticalCenterOffset;

            // If the camera is tilted, the line of sight ray hits Z = 0 with a Y shift:
            // rayPoint(t) = camPos + t * forward, where t = -camPos.z / forward.z
            // hitY = camPos.y - camPos.z * (forward.y / forward.z)
            // Therefore: camPos.y = targetCenterY + camPos.z * (forward.y / forward.z)
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
