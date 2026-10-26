using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;
using SandBlocks.Core.Rendering;
using SandBlocks.Core.Simulation;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Input;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Gameplay
{
    public enum CrowdFillDirection
    {
        BottomToTop,
        TopToBottom
    }

    public class GameManager : MonoBehaviour
    {
        [Header("Level Start Animations")]
        [Tooltip("If true, crowd and buses animate into the scene on level start and restart")]
        [SerializeField] private bool animateLevelStart = true;

        [Tooltip("Delay in seconds before buses start spawning onto the grid")]
        [SerializeField] private float busStartDelay = 0.1f;

        [Tooltip("Time in seconds between each bus spawning onto the grid")]
        [Range(0.01f, 0.25f)]
        [SerializeField] private float busSpawnInterval = 0.06f;

        [Tooltip("Delay in seconds before the crowd container starts filling")]
        [SerializeField] private float crowdStartDelay = 0.25f;

        [Tooltip("Direction to fill the crowd container")]
        [SerializeField] private CrowdFillDirection crowdFillDirection = CrowdFillDirection.BottomToTop;

        [Tooltip("Time in seconds between each row of crowd particles spawning")]
        [Range(0.005f, 0.15f)]
        [SerializeField] private float crowdRowInterval = 0.035f;

        [Header("Data & Configuration")]
        [SerializeField] private CrowdColorPalette palette;
        [SerializeField] private LevelData currentLevel;
        [SerializeField] private int maxParticles = 800;
        [SerializeField] private float particleRadius = 0.28f;

        [Header("Container Configuration")]
        [Tooltip("If true, overrides scene ContainerBoundary with LevelData container settings. If false, respects whatever width, height, center you set on ContainerBoundary in the scene.")]
        [SerializeField] private bool overrideBoundaryFromLevel = false;

        [Header("Restart Configuration")]
        [Tooltip("If true, reloads the scene completely on restart. If false, resets in-place without reloading the scene.")]
        [SerializeField] private bool reloadSceneOnRestart = true;
        [Tooltip("Optional UI Button to bind automatically to restart the level")]
        [SerializeField] private Button restartButton;

        [Header("Camera Control")]
        [Tooltip("If false, preserves whatever camera position and rotation you set in the scene")]
        [SerializeField] private bool autoPositionCamera = false;

        [Header("Hierarchy Containers")]
        [Tooltip("Parent transform for all spawned buses to maintain hierarchy in scene")]
        [SerializeField] private Transform busContainer;

        [Header("Level Management")]
        [SerializeField] private LevelManager levelManager;

        [Header("Components")]
        [SerializeField] private PBDCrowdSimulation simulation;
        [SerializeField] private InstancedCrowdRenderer crowdRenderer;
        [SerializeField] private ContainerBoundary boundary;
        [SerializeField] private PuzzleGrid puzzleGrid;
        [SerializeField] private BusVehicle busPrefab;

        public CrowdColorPalette Palette
        {
            get => palette;
            set => palette = value;
        }

        public LevelData CurrentLevel
        {
            get => currentLevel;
            set => currentLevel = value;
        }

        public Transform BusContainer
        {
            get => busContainer;
            set => busContainer = value;
        }

        private struct ParticleSpawnInfo
        {
            public float2 position;
            public int colorId;
            public float radius;
            public int rowIndex;
            public float y;
        }

        private Coroutine activeStartSequence;
        private List<BusVehicle> spawnedBuses = new List<BusVehicle>();
        private bool isLevelStartAnimating = false;

        public bool IsLevelStartAnimating => isLevelStartAnimating;

        private void Awake()
        {
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
            if (simulation == null) simulation = FindAnyObjectByType<PBDCrowdSimulation>();
            if (crowdRenderer == null) crowdRenderer = FindAnyObjectByType<InstancedCrowdRenderer>();
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();
            if (levelManager == null) levelManager = FindAnyObjectByType<LevelManager>();

            // Auto-detect BusContainer in scene if not assigned in inspector
            if (busContainer == null)
            {
                var foundBusCont = GameObject.Find("BusContainer");
                if (foundBusCont != null)
                {
                    busContainer = foundBusCont.transform;
                }
            }

            // Sync with LevelManager if available
            if (levelManager != null && levelManager.CurrentLevel != null)
            {
                currentLevel = levelManager.CurrentLevel;
            }

            // Auto-ensure AdaptiveCameraFramer is on Camera.main
            Camera cam = Camera.main;
            if (cam != null && cam.GetComponent<AdaptiveCameraFramer>() == null)
            {
                cam.gameObject.AddComponent<AdaptiveCameraFramer>();
            }

            if (autoPositionCamera)
            {
                SetupCamera();
            }
        }

        private void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, -2.8f, -14.5f);
                cam.transform.rotation = Quaternion.Euler(28f, 0f, 0f);
                cam.orthographic = true;
                cam.orthographicSize = 8.5f;
            }
        }

        private void Start()
        {
            SetupPaletteIfNull();

            // If LevelManager is present in the scene, it manages level setup in its Start()
            if (levelManager != null)
            {
                return;
            }

            // Standalone fallback (if LevelManager is not in the scene)
            RestartLevelInPlace();

            if (restartButton != null)
            {
                restartButton.onClick.AddListener(RestartGame);
            }
        }

        private void OnDestroy()
        {
            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(RestartGame);
            }
        }

        private void SetupPaletteIfNull()
        {
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<CrowdColorPalette>();
            }
        }

        private void InitializeSimulation()
        {
            if (overrideBoundaryFromLevel && currentLevel != null && boundary is ContainerBoundary cb)
            {
                if (currentLevel.containerShape == ContainerShapeType.CustomPolygon && currentLevel.customPolygonPoints != null && currentLevel.customPolygonPoints.Count >= 3)
                {
                    cb.SetCustomPolygonPoints(currentLevel.customPolygonPoints);
                }
                else
                {
                    cb.SetContainerShape(currentLevel.containerShape, currentLevel.containerWidth, currentLevel.containerHeight, currentLevel.containerCenter,
                        currentLevel.leftBorderOffset, currentLevel.rightBorderOffset, currentLevel.topBorderOffset, currentLevel.bottomBorderOffset);
                }
            }
            int capacity = currentLevel != null ? currentLevel.maxParticles : maxParticles;
            simulation.Initialize(capacity, boundary);
            crowdRenderer.Initialize(palette);
        }

        private IEnumerator PlayLevelStartSequence(List<ParticleSpawnInfo> preCollectedParticles = null)
        {
            isLevelStartAnimating = true;

            // 1. Spawn prototype vehicles with staggered elastic pop-in animations
            SpawnPrototypeVehicles(animate: true);

            // 2. Use pre-collected crowd particles or collect now
            var particles = preCollectedParticles ?? CollectAllCrowdParticles();

            // 3. Animate crowd filling row-by-row with spring bounce after crowdStartDelay
            yield return StartCoroutine(SpawnCrowdAnimatedRoutine(particles, crowdStartDelay));

            // Wait a brief moment for the last row's spring pop to settle
            yield return new WaitForSeconds(0.2f);

            // Ensure all particles are solidly locked in their pristine positions
            if (simulation != null && simulation.Particles.IsCreated)
            {
                var parts = simulation.Particles;
                for (int i = 0; i < simulation.ActiveCount; i++)
                {
                    var p = parts[i];
                    p.isSettled = true;
                    p.velocity = float2.zero;
                    parts[i] = p;
                }
            }

            isLevelStartAnimating = false;
            activeStartSequence = null;
        }

        private List<ParticleSpawnInfo> CollectAllCrowdParticles()
        {
            float radius = currentLevel != null ? currentLevel.particleRadius : particleRadius;
            Rect rect = boundary != null ? boundary.Bounds : new Rect(-2.7f, -2.5f, 5.4f, 5.8f);

            if (currentLevel != null && currentLevel.patternType == LevelPatternType.CustomGrid && 
                currentLevel.customCrowdColors != null && currentLevel.customCrowdColors.Length > 0)
            {
                return CollectCustomGridParticles(rect, radius);
            }
            else
            {
                return CollectProceduralParticles(rect, radius);
            }
        }

        private List<ParticleSpawnInfo> CollectCustomGridParticles(Rect rect, float radius)
        {
            var list = new List<ParticleSpawnInfo>();
            int cols = currentLevel.gridColumns;
            int rows = currentLevel.gridRows;

            float cellW = rect.width / cols;
            float cellH = rect.height / rows;
            float maxAllowedRadius = Mathf.Min(cellW, cellH) * 0.49f;
            float pRadius = radius > 0.05f ? Mathf.Min(radius, maxAllowedRadius) : maxAllowedRadius;

            for (int r = 0; r < rows; r++)
            {
                float y = rect.yMin + (r + 0.5f) * cellH;

                for (int c = 0; c < cols; c++)
                {
                    float x = rect.xMin + (c + 0.5f) * cellW;
                    float2 pos = new float2(x, y);

                    if (boundary != null)
                    {
                        float2 projected = boundary.ProjectInside(pos, pRadius);
                        if (boundary.ShapeType == ContainerShapeType.CustomPolygon && math.distancesq(pos, projected) > 0.001f)
                        {
                            continue; // Skip grid cells outside the custom polygon
                        }
                        pos = projected;
                    }

                    int colorId = currentLevel.GetPixel(c, r);
                    list.Add(new ParticleSpawnInfo
                    {
                        position = pos,
                        colorId = colorId,
                        radius = pRadius,
                        rowIndex = r,
                        y = y
                    });
                }
            }
            return list;
        }

        private List<ParticleSpawnInfo> CollectProceduralParticles(Rect rect, float radius)
        {
            var list = new List<ParticleSpawnInfo>();
            float spacing = radius * 2.05f;
            float startY = rect.yMin + radius * 1.4f;
            float endY = rect.yMax - radius * 1.0f;
            float minX = rect.xMin + radius * 1.0f;
            float maxX = rect.xMax - radius * 1.0f;

            for (float y = startY; y <= endY; y += spacing * 0.866f)
            {
                int rowIdx = Mathf.RoundToInt((y - startY) / (spacing * 0.866f));
                float xOffset = (rowIdx % 2 == 1) ? radius : 0f;
                float v = Mathf.Clamp01((y - rect.yMin) / rect.height);

                for (float x = minX + xOffset; x <= maxX; x += spacing)
                {
                    float2 pos = new float2(x, y);
                    float2 projected = boundary != null ? boundary.ProjectInside(pos, radius) : pos;

                    if (math.distancesq(pos, projected) < 0.001f)
                    {
                        float u = Mathf.Clamp01((x - rect.xMin) / rect.width);
                        int colorId = currentLevel != null 
                            ? currentLevel.GetColorForPosition(y, x, u, v) 
                            : GetDefaultColor(y, x, u, v);

                        list.Add(new ParticleSpawnInfo
                        {
                            position = pos,
                            colorId = colorId,
                            radius = radius,
                            rowIndex = rowIdx,
                            y = y
                        });
                    }
                }
            }
            return list;
        }

        private IEnumerator SpawnCrowdAnimatedRoutine(List<ParticleSpawnInfo> allParticles, float initialDelay)
        {
            if (initialDelay > 0f)
            {
                yield return new WaitForSeconds(initialDelay);
            }

            var rowsDict = new SortedDictionary<int, List<ParticleSpawnInfo>>();
            for (int i = 0; i < allParticles.Count; i++)
            {
                var p = allParticles[i];
                if (!rowsDict.TryGetValue(p.rowIndex, out var rowList))
                {
                    rowList = new List<ParticleSpawnInfo>();
                    rowsDict[p.rowIndex] = rowList;
                }
                rowList.Add(p);
            }

            var rowKeys = new List<int>(rowsDict.Keys);
            if (crowdFillDirection == CrowdFillDirection.TopToBottom)
            {
                rowKeys.Reverse();
            }

            for (int k = 0; k < rowKeys.Count; k++)
            {
                int r = rowKeys[k];
                var rowParticles = rowsDict[r];
                for (int i = 0; i < rowParticles.Count; i++)
                {
                    var p = rowParticles[i];
                    simulation.SpawnParticle(p.position, p.colorId, p.radius, startSettled: true);
                }

                if (crowdRowInterval > 0f)
                {
                    yield return new WaitForSeconds(crowdRowInterval);
                }
            }
        }

        private void SpawnLayeredCrowdInstant(List<ParticleSpawnInfo> preCollectedParticles = null)
        {
            var allParticles = preCollectedParticles ?? CollectAllCrowdParticles();
            for (int i = 0; i < allParticles.Count; i++)
            {
                var p = allParticles[i];
                simulation.SpawnParticle(p.position, p.colorId, p.radius, startSettled: true);
            }
        }

        private int GetDefaultColor(float y, float x, float u, float v)
        {
            // Evaluate Lips shape: Center lips with white surround matching image 2
            float nx = (u - 0.5f) / 0.42f;
            float ny = (v - 0.52f) / 0.42f;

            float rOuter = (nx * nx) + (ny * ny * 1.3f);
            if (rOuter > 1.0f)
            {
                return 5; // White background people
            }

            if (ny > 0.35f) return 3; // Pink top lip
            float mouth = (nx * nx * 1.8f) + (ny * ny * 5.0f);
            if (mouth < 0.35f) return 4; // Black inside mouth
            if (ny > 0.05f) return 3; // Pink bottom lip
            if (ny > -0.25f) return 2; // Yellow
            if (ny > -0.55f) return 1; // Green
            return 0; // Blue (nozzle exit layer)
        }

        private void SpawnPrototypeVehicles(bool animate = false)
        {
            spawnedBuses.Clear();

            if (puzzleGrid != null && currentLevel != null && currentLevel.puzzleColumns > 0)
            {
                int cols = currentLevel.puzzleColumns > 0 ? currentLevel.puzzleColumns : 6;
                int rows = currentLevel.puzzleRows > 0 ? currentLevel.puzzleRows : 5;
                puzzleGrid.SetGridDimensions(cols, rows);
            }

            if (busPrefab == null)
            {
                busPrefab = CreateProceduralBusPrefab();
            }

            if (currentLevel != null && currentLevel.buses != null && currentLevel.buses.Count > 0)
            {
                for (int i = 0; i < currentLevel.buses.Count; i++)
                {
                    var b = currentLevel.buses[i];
                    var bus = CreateBus(b.colorId, b.capacity, b.length, b.rotationDegrees, b.gridPosition, b.name);
                    spawnedBuses.Add(bus);
                }
            }
            else
            {
                // Default 10-bus layout matching image 2
                spawnedBuses.Add(CreateBus(colorId: 0, capacity: 4, length: 2, rotDegrees: 0,   gridPos: new Vector2Int(0, 4), busName: "Blue Bus"));
                spawnedBuses.Add(CreateBus(colorId: 2, capacity: 4, length: 2, rotDegrees: 180, gridPos: new Vector2Int(3, 4), busName: "Yellow Bus"));
                spawnedBuses.Add(CreateBus(colorId: 4, capacity: 4, length: 2, rotDegrees: 90,  gridPos: new Vector2Int(0, 2), busName: "Black Bus L"));
                spawnedBuses.Add(CreateBus(colorId: 0, capacity: 4, length: 2, rotDegrees: 0,   gridPos: new Vector2Int(3, 3), busName: "Blue Bus Mid"));
                spawnedBuses.Add(CreateBus(colorId: 4, capacity: 4, length: 2, rotDegrees: 270, gridPos: new Vector2Int(5, 2), busName: "Black Bus R"));
                spawnedBuses.Add(CreateBus(colorId: 5, capacity: 4, length: 3, rotDegrees: 0,   gridPos: new Vector2Int(2, 2), busName: "White Bus"));
                spawnedBuses.Add(CreateBus(colorId: 6, capacity: 4, length: 2, rotDegrees: 0,   gridPos: new Vector2Int(0, 1), busName: "Orange Bus"));
                spawnedBuses.Add(CreateBus(colorId: 3, capacity: 4, length: 2, rotDegrees: 0,   gridPos: new Vector2Int(2, 1), busName: "Pink Bus"));
                spawnedBuses.Add(CreateBus(colorId: 1, capacity: 4, length: 2, rotDegrees: 180, gridPos: new Vector2Int(4, 1), busName: "Green Bus"));
                spawnedBuses.Add(CreateBus(colorId: 5, capacity: 10, length: 6, rotDegrees: 0,  gridPos: new Vector2Int(0, 0), busName: "Long White"));
            }

            if (animate)
            {
                for (int i = 0; i < spawnedBuses.Count; i++)
                {
                    if (spawnedBuses[i] != null)
                    {
                        float delay = busStartDelay + i * busSpawnInterval;
                        spawnedBuses[i].PlaySpawnAnimation(delay);
                    }
                }
            }
        }

        private BusVehicle CreateBus(int colorId, int capacity, int length, int rotDegrees, Vector2Int gridPos, string busName = null)
        {
            var bus = busContainer != null 
                ? Instantiate(busPrefab, busContainer) 
                : Instantiate(busPrefab);

            if (!string.IsNullOrEmpty(busName))
            {
                bus.name = busName;
            }
            else
            {
                bus.name = $"Bus_{gridPos.x}_{gridPos.y}";
            }

            bus.Initialize(colorId, capacity, length, rotDegrees, gridPos);
            bus.SetPassengerScale(crowdRenderer != null ? crowdRenderer.CharacterScale : 0.1f);
            bus.ApplyColor(palette);
            puzzleGrid.PlaceVehicle(bus, gridPos);
            return bus;
        }

        private BusVehicle CreateProceduralBusPrefab()
        {
            var busObj = new GameObject("ProceduralBus");
            var bus = busObj.AddComponent<BusVehicle>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(busObj.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(1.2f, 0.8f, 2.4f);

            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            busObj.SetActive(false);
            return bus;
        }

        private void Update()
        {
            if (simulation != null && simulation.IsInitialized)
            {
                // Freeze physics simulation while level intro animation is playing so particles stay 100% stable
                if (!isLevelStartAnimating)
                {
                    simulation.StepSimulation(Time.deltaTime);
                }

                if (crowdRenderer != null)
                {
                    crowdRenderer.Render(simulation.Particles, simulation.ActiveCount);
                }
            }

            // Sync character scale live if changed in Inspector
            if (crowdRenderer != null && busPrefab != null)
            {
                busPrefab.SetPassengerScale(crowdRenderer.CharacterScale);
            }
        }

        /// <summary>
        /// Public restart function that can be assigned directly to a UI Button's OnClick event in the Inspector.
        /// If LevelManager is present, delegates directly to LevelManager to ensure a unified restart path.
        /// </summary>
        public void RestartGame()
        {
            if (levelManager != null)
            {
                levelManager.RestartCurrentLevel();
            }
            else
            {
                RestartLevel();
            }
        }

        /// <summary>
        /// Restarts the level according to the reloadSceneOnRestart setting (scene reload or in-place).
        /// Can also be assigned directly to a UI Button's OnClick event.
        /// </summary>
        public void RestartLevel()
        {
            if (reloadSceneOnRestart)
            {
                string activeSceneName = SceneManager.GetActiveScene().name;
                if (!string.IsNullOrEmpty(activeSceneName))
                {
                    SceneManager.LoadScene(activeSceneName);
                }
                else
                {
                    SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                }
            }
            else
            {
                RestartLevelInPlace();
            }
        }

        /// <summary>
        /// Loads a new level and resets the crowd simulation, buses, and container boundary.
        /// </summary>
        public void LoadLevel(LevelData level, bool syncBoundary = true)
        {
            if (level == null) return;
            currentLevel = level;

            if (syncBoundary && boundary is ContainerBoundary cb)
            {
                if (level.containerShape == ContainerShapeType.CustomPolygon && level.customPolygonPoints != null && level.customPolygonPoints.Count >= 3)
                {
                    cb.SetCustomPolygonPoints(level.customPolygonPoints);
                }
                else
                {
                    cb.SetContainerShape(level.containerShape, level.containerWidth, level.containerHeight, level.containerCenter,
                        level.leftBorderOffset, level.rightBorderOffset, level.topBorderOffset, level.bottomBorderOffset);
                }
                cb.BuildVisualContainer();
            }

            RestartLevelInPlace();
        }

        /// <summary>
        /// Instant in-place restart without reloading the whole scene.
        /// Clears existing buses on the grid and respawns crowd and vehicles.
        /// </summary>
        public void RestartLevelInPlace()
        {
            // 0. Stop all running coroutines on GameManager to terminate any ongoing spawn sequences
            StopAllCoroutines();
            activeStartSequence = null;
            isLevelStartAnimating = false;

            // Stop any active passenger flight routines
            var transfer = FindAnyObjectByType<CrowdTransferController>();
            if (transfer != null)
            {
                transfer.ClearActiveTransfers();
            }

            // 1. Remove all existing buses from grid and scene
            if (puzzleGrid != null)
            {
                puzzleGrid.Clear();
            }

            // Clean up any lingering children in busContainer
            if (busContainer != null)
            {
                for (int i = busContainer.childCount - 1; i >= 0; i--)
                {
                    Transform child = busContainer.GetChild(i);
                    if (child != null)
                    {
                        child.gameObject.SetActive(false);
                        if (Application.isPlaying) Destroy(child.gameObject);
                        else DestroyImmediate(child.gameObject);
                    }
                }
            }

            // 2. Pre-collect all crowd particles to determine exact capacity required
            var particles = CollectAllCrowdParticles();
            int baseCapacity = currentLevel != null ? currentLevel.maxParticles : maxParticles;
            int requiredCapacity = Mathf.Max(baseCapacity, particles.Count + 50);

            // Re-initialize simulation with guaranteed capacity so no particles are ever dropped
            simulation.Initialize(requiredCapacity, boundary);
            crowdRenderer.Initialize(palette);

            // 3. Respawn crowd particles and buses (animated or instant)
            if (animateLevelStart)
            {
                activeStartSequence = StartCoroutine(PlayLevelStartSequence(particles));
            }
            else
            {
                SpawnLayeredCrowdInstant(particles);
                SpawnPrototypeVehicles(animate: false);
            }

            // 4. Ensure camera view is framed
            var framer = FindAnyObjectByType<AdaptiveCameraFramer>();
            if (framer != null)
            {
                framer.FrameView();
            }
        }
    }
}
