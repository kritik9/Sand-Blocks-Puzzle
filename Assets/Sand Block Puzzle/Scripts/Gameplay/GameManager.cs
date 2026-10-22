using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;
using SandBlocks.Core.Rendering;
using SandBlocks.Core.Simulation;
using SandBlocks.Puzzle.Grid;
using SandBlocks.Puzzle.Input;
using SandBlocks.Puzzle.Vehicles;

namespace SandBlocks.Gameplay
{
    public class GameManager : MonoBehaviour
    {
        [Header("Data & Configuration")]
        [SerializeField] private CrowdColorPalette palette;
        [SerializeField] private LevelData currentLevel;
        [SerializeField] private int maxParticles = 600;
        [SerializeField] private float particleRadius = 0.18f;

        [Header("Components")]
        [SerializeField] private PBDCrowdSimulation simulation;
        [SerializeField] private InstancedCrowdRenderer crowdRenderer;
        [SerializeField] private FunnelBoundary boundary;
        [SerializeField] private PuzzleGrid puzzleGrid;
        [SerializeField] private BusVehicle busPrefab;

        private void Awake()
        {
            if (boundary == null) boundary = FindAnyObjectByType<FunnelBoundary>();
            if (simulation == null) simulation = FindAnyObjectByType<PBDCrowdSimulation>();
            if (crowdRenderer == null) crowdRenderer = FindAnyObjectByType<InstancedCrowdRenderer>();
            if (puzzleGrid == null) puzzleGrid = FindAnyObjectByType<PuzzleGrid>();

            SetupCamera();
        }

        private void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(0f, -2.5f, -14f);
                cam.orthographic = true;
                cam.orthographicSize = 7.5f;
            }
        }

        private void Start()
        {
            SetupPaletteIfNull();
            InitializeSimulation();
            SpawnLayeredCrowd();
            SpawnPrototypeVehicles();
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
            int capacity = currentLevel != null ? currentLevel.maxParticles : maxParticles;
            simulation.Initialize(capacity, boundary);
            crowdRenderer.Initialize(palette);
        }

        private void SpawnLayeredCrowd()
        {
            float radius = currentLevel != null ? currentLevel.particleRadius : particleRadius;
            float spacing = radius * 2.05f;
            float startY = -2.6f;
            float endY = 2.4f;
            float width = 4.2f;

            for (float y = startY; y <= endY; y += spacing * 0.866f)
            {
                int rowIdx = Mathf.RoundToInt((y - startY) / (spacing * 0.866f));
                float xOffset = (rowIdx % 2 == 1) ? radius : 0f;

                int colorId = currentLevel != null 
                    ? currentLevel.GetColorForPosition(y, 0f) 
                    : GetDefaultColor(y);

                for (float x = -width * 0.5f + xOffset; x <= width * 0.5f; x += spacing)
                {
                    float2 pos = new float2(x, y);
                    float2 projected = boundary.ProjectInside(pos, radius);

                    // Only spawn if inside the funnel
                    if (math.distancesq(pos, projected) < 0.001f)
                    {
                        simulation.SpawnParticle(pos, colorId, radius);
                    }
                }
            }
        }

        private int GetDefaultColor(float y)
        {
            if (y < -1.6f) return 0;      // Blue
            if (y < -0.8f) return 1;      // Green
            if (y < 0.2f) return 2;       // Yellow
            if (y < 1.4f) return 3;       // Pink
            return 4;                     // Black
        }

        private void SpawnPrototypeVehicles()
        {
            if (busPrefab == null)
            {
                busPrefab = CreateProceduralBusPrefab();
            }

            if (currentLevel != null && currentLevel.buses != null && currentLevel.buses.Count > 0)
            {
                for (int i = 0; i < currentLevel.buses.Count; i++)
                {
                    var b = currentLevel.buses[i];
                    CreateBus(b.colorId, b.capacity, b.length, b.isVertical, b.gridPosition);
                }
            }
            else
            {
                CreateBus(colorId: 0, capacity: 4, length: 2, isVertical: false, gridPos: new Vector2Int(0, 4));
                CreateBus(colorId: 2, capacity: 4, length: 2, isVertical: false, gridPos: new Vector2Int(3, 4));
                CreateBus(colorId: 1, capacity: 4, length: 2, isVertical: true,  gridPos: new Vector2Int(0, 1));
                CreateBus(colorId: 3, capacity: 4, length: 2, isVertical: false, gridPos: new Vector2Int(2, 2));
                CreateBus(colorId: 4, capacity: 6, length: 3, isVertical: false, gridPos: new Vector2Int(1, 0));
            }
        }

        private void CreateBus(int colorId, int capacity, int length, bool isVertical, Vector2Int gridPos)
        {
            var bus = Instantiate(busPrefab);
            bus.Initialize(colorId, capacity, length, isVertical, gridPos);
            bus.ApplyColor(palette);
            puzzleGrid.PlaceVehicle(bus, gridPos);
        }

        private BusVehicle CreateProceduralBusPrefab()
        {
            var busObj = new GameObject("ProceduralBus");
            var bus = busObj.AddComponent<BusVehicle>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(busObj.transform);
            body.transform.localPosition = Vector3.zero;
            body.transform.localScale = new Vector3(1.8f, 0.8f, 0.5f);

            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            busObj.SetActive(false);
            return bus;
        }

        private void Update()
        {
            simulation.StepSimulation(Time.deltaTime);
        }

        private void LateUpdate()
        {
            crowdRenderer.Render(simulation.Particles, simulation.ActiveCount);
        }
    }
}
