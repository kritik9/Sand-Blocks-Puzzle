using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using SandBlocks.Core.Data;
using SandBlocks.Core.Simulation;

namespace SandBlocks.Core.Rendering
{
    public class InstancedCrowdRenderer : MonoBehaviour, ICrowdRenderer
    {
        [Header("Character Model Prefab (Recommended)")]
        [SerializeField] private GameObject characterPrefab;
        [SerializeField] private float prefabScale = 0.08f;

        [Header("Crowd Elevation & Height (Visibly Above)")]
        [Tooltip("Direct reference to ContainerBoundary. If unassigned, will auto-locate in scene.")]
        [SerializeField] private SandBlocks.Core.Boundary.ContainerBoundary containerBoundary;

        [Tooltip("If true, automatically syncs character elevation with ContainerBoundary so characters stand on top of the elevated back plate")]
        [SerializeField] private bool syncWithBoundaryElevation = true;

        [Tooltip("If true, places characters on top of the back plate surface (BackPlateElevationY + thickness * 0.5). If false, aligns directly with BackPlateElevationY.")]
        [SerializeField] private bool alignToPlateSurface = true;

        [Tooltip("Fine-tuning offset added on top of the back plate elevation (e.g. 0.0f)")]
        [SerializeField] private float crowdElevationOffset = 0f;

        [Tooltip("Fallback elevation height on the Y axis above ground plane in Horizontal_XZ mode if boundary sync is disabled or boundary is missing")]
        [SerializeField] private float crowdElevationY = 0.08f;
        [Tooltip("Z offset for crowd characters")]
        [SerializeField] private float crowdZOffset = 0f;
        [Tooltip("Vertical Y offset for crowd characters in legacy Vertical_XY mode")]
        [SerializeField] private float crowdYOffset = 0f;

        public float EffectiveElevationY
        {
            get
            {
                if (syncWithBoundaryElevation)
                {
                    if (containerBoundary == null)
                    {
                        containerBoundary = FindAnyObjectByType<SandBlocks.Core.Boundary.ContainerBoundary>();
                    }
                    if (containerBoundary != null)
                    {
                        float baseElev = alignToPlateSurface ? containerBoundary.SurfaceElevationY : containerBoundary.BackPlateElevationY;
                        return baseElev + crowdElevationOffset;
                    }
                }
                return crowdElevationY;
            }
        }

        public float CrowdElevationY => EffectiveElevationY;
        public SandBlocks.Core.Boundary.ContainerBoundary Boundary { get => containerBoundary; set => containerBoundary = value; }
        public bool SyncWithBoundaryElevation { get => syncWithBoundaryElevation; set => syncWithBoundaryElevation = value; }
        public bool AlignToPlateSurface { get => alignToPlateSurface; set => alignToPlateSurface = value; }
        public float CrowdElevationOffset { get => crowdElevationOffset; set => crowdElevationOffset = value; }

        [Header("Crowd Layout & Spacing (Anti-Overlap)")]
        [Tooltip("Vertical (depth/row) spacing multiplier. 1.0 = standard, 1.15 - 1.4 = spreads rows apart vertically so character meshes and outlines never intersect.")]
        [Range(0.8f, 2.5f)]
        [SerializeField] private float verticalSpacingMultiplier = 1.25f;

        [Tooltip("Additional vertical gap added between rows (world units)")]
        [SerializeField] private float verticalRowSpacing = 0f;

        [Tooltip("Horizontal (column) spacing multiplier. 1.0 = standard.")]
        [Range(0.8f, 2.0f)]
        [SerializeField] private float horizontalSpacingMultiplier = 1.0f;

        [Tooltip("If true, vertical spacing expands upwards from the bottom boundary (keeps bottom row flush with collection line). If false, expands symmetrically from container center.")]
        [SerializeField] private bool anchorVerticalSpacingAtBottom = true;

        public float VerticalSpacingMultiplier { get => verticalSpacingMultiplier; set => verticalSpacingMultiplier = value; }
        public float VerticalRowSpacing { get => verticalRowSpacing; set => verticalRowSpacing = value; }
        public float HorizontalSpacingMultiplier { get => horizontalSpacingMultiplier; set => horizontalSpacingMultiplier = value; }
        public bool AnchorVerticalSpacingAtBottom { get => anchorVerticalSpacingAtBottom; set => anchorVerticalSpacingAtBottom = value; }

        public Vector3 GetCalculatedWorldPosition(Unity.Mathematics.float2 simPos)
        {
            if (containerBoundary == null)
            {
                containerBoundary = FindAnyObjectByType<SandBlocks.Core.Boundary.ContainerBoundary>();
            }

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

            float rawX = simPos.x;
            float rawY = simPos.y;

            // Apply horizontal spacing relative to container center
            float centerX = (containerBoundary != null) ? containerBoundary.ContainerCenter.x : 0f;
            float posX = centerX + (rawX - centerX) * horizontalSpacingMultiplier;

            // Apply vertical spacing
            float posY;
            if (anchorVerticalSpacingAtBottom)
            {
                float bottomY = (containerBoundary != null) ? containerBoundary.BottomY : -2.5f;
                posY = bottomY + (rawY - bottomY) * verticalSpacingMultiplier + verticalRowSpacing;
            }
            else
            {
                float centerY = (containerBoundary != null) ? containerBoundary.ContainerCenter.y : 0f;
                posY = centerY + (rawY - centerY) * verticalSpacingMultiplier + verticalRowSpacing;
            }

            if (isHorizontal)
            {
                return new Vector3(posX, EffectiveElevationY, posY + crowdZOffset);
            }
            else
            {
                return new Vector3(posX, posY + crowdYOffset, crowdZOffset);
            }
        }

        [Header("Crowd Scale Variation")]
        [Tooltip("Random scale variation range applied to crowd characters (multiplier on prefabScale, e.g. 0.92 to 1.08) so they don't look identical")]
        [SerializeField] private Vector2 scaleVariationRange = new Vector2(0.92f, 1.08f);

        [Header("Crowd Orientation & Rotation")]
        [Tooltip("If true, rotates characters by 180 degrees so their back side faces the camera, revealing their full body as in the reference image")]
        [SerializeField] private bool faceBackSide = true;

        [Tooltip("Base Euler rotation for characters (default: 0, 0, 0)")]
        [SerializeField] private Vector3 characterRotationEuler = Vector3.zero;

        [Tooltip("Random Y-axis yaw rotation jitter range (degrees, e.g. -14 to +14) so characters stand organically tilted")]
        [SerializeField] private Vector2 rotationJitterYRange = new Vector2(-12f, 12f);

        [Tooltip("Random Z-axis lean tilt range (degrees, e.g. -3 to +3)")]
        [SerializeField] private Vector2 rotationJitterZRange = new Vector2(-3f, 3f);

        public float CharacterScale => prefabScale;
        public Vector3 CharacterRotationEuler => characterRotationEuler;
        public float CrowdZOffset => crowdZOffset;
        public float CrowdYOffset => crowdYOffset;
        public Vector2 ScaleVariationRange => scaleVariationRange;
        public bool FaceBackSide => faceBackSide;
        public Vector2 RotationJitterYRange => rotationJitterYRange;
        public Vector2 RotationJitterZRange => rotationJitterZRange;

        [Header("Rotation Jitter Randomization")]
        [Tooltip("If true, a new random seed is rolled every time the crowd initializes or a level starts/restarts so rotations vary dynamically across plays")]
        [SerializeField] private bool randomizeSeedEveryRun = true;

        [Tooltip("Base seed for rotation jitter. Set or randomized at runtime.")]
        [SerializeField] private int customRandomSeed = 1337;

        private int runtimeRandomSeed = 0;

        public bool RandomizeSeedEveryRun { get => randomizeSeedEveryRun; set => randomizeSeedEveryRun = value; }
        public int CustomRandomSeed { get => customRandomSeed; set => customRandomSeed = value; }

        public void ReshuffleRandomSeeds()
        {
            runtimeRandomSeed = UnityEngine.Random.Range(1000, 9999999);
        }

        [ContextMenu("Reshuffle Rotation Jitter")]
        public void ReshuffleJitterInEditor()
        {
            ReshuffleRandomSeeds();
            SyncLiveCharacterPositions();
        }

        private void Awake()
        {
            if (runtimeRandomSeed == 0)
            {
                runtimeRandomSeed = randomizeSeedEveryRun ? UnityEngine.Random.Range(1000, 9999999) : customRandomSeed;
            }
        }

        [Header("Fallback Instanced Mesh Settings")]
        [SerializeField] private Mesh characterMesh;
        [SerializeField] private Material characterMaterial;
        [SerializeField] private Vector3 characterScale = new Vector3(0.55f, 0.55f, 0.55f);

        [Header("Spawn Pop Animation")]
        [SerializeField] private bool enableSpawnPop = true;
        [Range(0.1f, 0.6f)]
        [SerializeField] private float spawnPopDuration = 0.25f;

        private CrowdColorPalette palette;
        private List<GameObject> pooledCharacters = new List<GameObject>();
        private List<Renderer[]> pooledRenderers = new List<Renderer[]>();
        private List<float> pooledSpawnTimes = new List<float>();
        private List<SandBlocks.Gameplay.CharacterPassenger> pooledPassengers = new List<SandBlocks.Gameplay.CharacterPassenger>();
        private float defaultPrefabScale = -1f;

        public void SetCharacterScale(float scale)
        {
            if (defaultPrefabScale < 0f) defaultPrefabScale = prefabScale;
            if (scale > 0f)
            {
                prefabScale = scale;
            }
            else
            {
                prefabScale = defaultPrefabScale;
            }
        }

        public void ResetCharacterScale()
        {
            if (defaultPrefabScale > 0f)
            {
                prefabScale = defaultPrefabScale;
            }
        }

        private float defaultVerticalSpacingMultiplier = -1f;

        public void SetVerticalSpacingMultiplier(float multiplier)
        {
            if (defaultVerticalSpacingMultiplier < 0f) defaultVerticalSpacingMultiplier = verticalSpacingMultiplier;
            if (multiplier > 0f)
            {
                verticalSpacingMultiplier = multiplier;
            }
            else
            {
                verticalSpacingMultiplier = defaultVerticalSpacingMultiplier;
            }

            if (containerBoundary == null) containerBoundary = FindAnyObjectByType<SandBlocks.Core.Boundary.ContainerBoundary>();
            if (containerBoundary != null && containerBoundary.AutoExtendWithCrowdSpacing)
            {
                containerBoundary.BuildVisualContainer();
            }
        }

        public void ResetVerticalSpacingMultiplier()
        {
            if (defaultVerticalSpacingMultiplier > 0f)
            {
                verticalSpacingMultiplier = defaultVerticalSpacingMultiplier;
            }

            if (containerBoundary == null) containerBoundary = FindAnyObjectByType<SandBlocks.Core.Boundary.ContainerBoundary>();
            if (containerBoundary != null && containerBoundary.AutoExtendWithCrowdSpacing)
            {
                containerBoundary.BuildVisualContainer();
            }
        }
        private MaterialPropertyBlock propertyBlock;
        private readonly Matrix4x4[] batchMatrices = new Matrix4x4[1023];
        private readonly Vector4[] batchColors = new Vector4[1023];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_Prop = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorId = Shader.PropertyToID("_Outline_Color");
        private static readonly int FirstShadeColorId = Shader.PropertyToID("_1st_ShadeColor");
        private static readonly int SecondShadeColorId = Shader.PropertyToID("_2nd_ShadeColor");

        public Vector3 GetEffectiveBaseRotation()
        {
            float rotY = characterRotationEuler.y + (faceBackSide ? 180f : 0f);
            return new Vector3(characterRotationEuler.x, rotY, characterRotationEuler.z);
        }

        private float GetStableRandom(int index, float min, float max)
        {
            if (Mathf.Approximately(min, max)) return min;
            uint hash = (uint)index * 2654435761u ^ 0x9e3779b9u;
            hash = (hash ^ (hash >> 16)) * 0x85ebca6bu;
            hash = (hash ^ (hash >> 13)) * 0xc2b2ae35u;
            hash ^= (hash >> 16);
            float t = (hash & 0xFFFF) / 65535f;
            return Mathf.Lerp(min, max, t);
        }

        public void Initialize(CrowdColorPalette colorPalette)
        {
            if (randomizeSeedEveryRun)
            {
                ReshuffleRandomSeeds();
            }
            else
            {
                runtimeRandomSeed = customRandomSeed;
            }

            palette = colorPalette;
            propertyBlock = new MaterialPropertyBlock();

            // Clear old pool if any
            for (int i = 0; i < pooledCharacters.Count; i++)
            {
                if (pooledCharacters[i] != null) Destroy(pooledCharacters[i]);
            }
            pooledCharacters.Clear();
            pooledRenderers.Clear();
            pooledSpawnTimes.Clear();
            pooledPassengers.Clear();
            if (defaultPrefabScale < 0f) defaultPrefabScale = prefabScale;

            // Setup fallback mesh and material
            if (characterMesh == null && characterPrefab == null)
            {
                var sphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                characterMesh = sphereObj.GetComponent<MeshFilter>().sharedMesh;
                Destroy(sphereObj);
            }

            if (characterMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("SandBlocks/CrowdInstancedURP")
                             ?? Shader.Find("Standard");
                if (shader != null)
                {
                    characterMaterial = new Material(shader) { enableInstancing = true, name = "Crowd_URPLit_Instanced" };
                }
            }
            else
            {
                characterMaterial.enableInstancing = true;
            }
        }

        public void Render(NativeArray<CrowdParticle> particles, int count)
        {
            if (count == 0 || !particles.IsCreated) return;

            // 1. If CharacterModel prefab is assigned, use the pooled GameObject renderer
            if (characterPrefab != null)
            {
                RenderPooledPrefabs(particles, count);
                return;
            }

            // 2. Otherwise, use GPU Instancing fallback
            RenderGPUInstanced(particles, count);
        }

        private void RenderPooledPrefabs(NativeArray<CrowdParticle> particles, int count)
        {
            // Expand pool if needed
            while (pooledCharacters.Count < count)
            {
                var instance = Instantiate(characterPrefab, transform);
                instance.transform.localScale = Vector3.one * prefabScale;

                // Disable colliders on crowd so they don't block bus raycasts
                var cols = instance.GetComponentsInChildren<Collider>();
                for (int c = 0; c < cols.Length; c++) cols[c].enabled = false;

                // Ensure all renderers on the instance have a functional material assigned
                var instRends = instance.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < instRends.Length; r++)
                {
                    if (instRends[r] != null)
                    {
                        var mat = instRends[r].sharedMaterial;
                        if ((mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader") && characterMaterial != null)
                        {
                            instRends[r].sharedMaterial = characterMaterial;
                        }
                    }
                }

                pooledCharacters.Add(instance);
                pooledRenderers.Add(instRends);
                pooledSpawnTimes.Add(Time.time);
                pooledPassengers.Add(instance.GetComponent<SandBlocks.Gameplay.CharacterPassenger>());
            }

            float baseRotY = characterRotationEuler.y + (faceBackSide ? 180f : 0f);

            for (int i = 0; i < count; i++)
            {
                var p = particles[i];
                var instance = pooledCharacters[i];

                if (!p.isActive)
                {
                    if (instance.activeSelf) instance.SetActive(false);
                    continue;
                }

                if (!instance.activeSelf)
                {
                    instance.SetActive(true);
                    if (i < pooledSpawnTimes.Count)
                    {
                        pooledSpawnTimes[i] = Time.time;
                    }
                }

                float scaleMult = 1f;
                if (enableSpawnPop && i < pooledSpawnTimes.Count)
                {
                    float elapsed = Time.time - pooledSpawnTimes[i];
                    if (elapsed < spawnPopDuration)
                    {
                        float t = elapsed / spawnPopDuration;
                        float c1 = 1.70158f;
                        float c3 = c1 + 1f;
                        scaleMult = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                        scaleMult = Mathf.Max(0f, scaleMult);
                    }
                }

                bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

                // 1. Position: apply elevation & spacing (EffectiveElevationY & vertical/horizontal spacing)
                instance.transform.position = GetCalculatedWorldPosition(p.position);

                var pass = (i < pooledPassengers.Count) ? pooledPassengers[i] : instance.GetComponent<SandBlocks.Gameplay.CharacterPassenger>();

                if (runtimeRandomSeed == 0)
                {
                    runtimeRandomSeed = randomizeSeedEveryRun ? UnityEngine.Random.Range(1000, 9999999) : customRandomSeed;
                }

                // 2. Rotation: base rotation + back-side flip + dynamic organic random jitter (yaw & tilt)
                int seedY = i * 2399 + runtimeRandomSeed * 10007 + 3;
                int seedZ = i * 4001 + runtimeRandomSeed * 13337 + 5;
                float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                    ? GetStableRandom(seedY, rotationJitterYRange.x, rotationJitterYRange.y)
                    : 0f;
                float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                    ? GetStableRandom(seedZ, rotationJitterZRange.x, rotationJitterZRange.y)
                    : 0f;
                float curBaseRotY = isHorizontal
                    ? characterRotationEuler.y + (faceBackSide ? 0f : 180f)
                    : characterRotationEuler.y + (faceBackSide ? 180f : 0f);
                Vector3 baseEuler = new Vector3(characterRotationEuler.x, curBaseRotY, characterRotationEuler.z);

                if (pass != null)
                {
                    pass.ApplyRotationJitter(baseEuler, jitterY, jitterZ);
                }
                else
                {
                    instance.transform.rotation = Quaternion.Euler(characterRotationEuler.x, curBaseRotY + jitterY, characterRotationEuler.z + jitterZ);
                }

                // 3. Scale: uniform base * spawn pop * organic random scale variation
                int seedScale = i * 1871 + runtimeRandomSeed * 7919 + 1;
                float randScaleMult = (scaleVariationRange.x != 1f || scaleVariationRange.y != 1f)
                    ? GetStableRandom(seedScale, scaleVariationRange.x, scaleVariationRange.y)
                    : 1f;
                instance.transform.localScale = Vector3.one * (prefabScale * scaleMult * randScaleMult);

                // 4. Color
                if (palette != null)
                {
                    Color c = palette.GetColor(p.colorId);
                    if (pass != null)
                    {
                        pass.SetColor(c);
                    }
                    else if (i < pooledRenderers.Count)
                    {
                        var rends = pooledRenderers[i];
                        for (int r = 0; r < rends.Length; r++)
                        {
                            if (rends[r] == null) continue;
                            propertyBlock.SetColor(BaseColorId, c);
                            propertyBlock.SetColor(ColorId_Prop, c);
                            propertyBlock.SetColor(OutlineColorId, SandBlocks.Gameplay.CharacterPassenger.GetDarkerHarmoniousColor(c, 0.38f, 1.15f));
                            propertyBlock.SetColor(FirstShadeColorId, SandBlocks.Gameplay.CharacterPassenger.GetDarkerHarmoniousColor(c, 0.72f, 1.05f));
                            propertyBlock.SetColor(SecondShadeColorId, SandBlocks.Gameplay.CharacterPassenger.GetDarkerHarmoniousColor(c, 0.50f, 1.10f));
                            rends[r].SetPropertyBlock(propertyBlock);
                        }
                    }
                }
            }

            // Deactivate any extra
            for (int i = count; i < pooledCharacters.Count; i++)
            {
                if (pooledCharacters[i].activeSelf) pooledCharacters[i].SetActive(false);
            }
        }

        private void RenderGPUInstanced(NativeArray<CrowdParticle> particles, int count)
        {
            if (characterMesh == null || characterMaterial == null) return;

            int batchIndex = 0;
            float baseRotY = characterRotationEuler.y + (faceBackSide ? 180f : 0f);
            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);

            for (int i = 0; i < count; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                Vector3 pos = GetCalculatedWorldPosition(p.position);

                if (runtimeRandomSeed == 0)
                {
                    runtimeRandomSeed = randomizeSeedEveryRun ? UnityEngine.Random.Range(1000, 9999999) : customRandomSeed;
                }

                int seedY = i * 2399 + runtimeRandomSeed * 10007 + 3;
                int seedZ = i * 4001 + runtimeRandomSeed * 13337 + 5;
                float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                    ? GetStableRandom(seedY, rotationJitterYRange.x, rotationJitterYRange.y)
                    : 0f;
                float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                    ? GetStableRandom(seedZ, rotationJitterZRange.x, rotationJitterZRange.y)
                    : 0f;
                float curBaseRotY = isHorizontal
                    ? characterRotationEuler.y + (faceBackSide ? 0f : 180f)
                    : characterRotationEuler.y + (faceBackSide ? 180f : 0f);
                Quaternion rot = Quaternion.Euler(characterRotationEuler.x, curBaseRotY + jitterY, characterRotationEuler.z + jitterZ);

                int seedScale = i * 1871 + runtimeRandomSeed * 7919 + 1;
                float randScaleMult = (scaleVariationRange.x != 1f || scaleVariationRange.y != 1f)
                    ? GetStableRandom(seedScale, scaleVariationRange.x, scaleVariationRange.y)
                    : 1f;
                Vector3 instScale = characterScale * randScaleMult;

                batchMatrices[batchIndex] = Matrix4x4.TRS(pos, rot, instScale);

                Color c = palette != null ? palette.GetColor(p.colorId) : Color.white;
                batchColors[batchIndex] = c;
                batchIndex++;

                if (batchIndex == 1023)
                {
                    FlushBatch(batchIndex);
                    batchIndex = 0;
                }
            }

            if (batchIndex > 0)
            {
                FlushBatch(batchIndex);
            }
        }

        private void FlushBatch(int count)
        {
            propertyBlock.SetVectorArray(BaseColorId, batchColors);
            Graphics.DrawMeshInstanced(
                characterMesh,
                0,
                characterMaterial,
                batchMatrices,
                count,
                propertyBlock,
                UnityEngine.Rendering.ShadowCastingMode.On,
                true
            );
        }
    
        public void SyncLiveCharacterPositions()
        {
            if (pooledCharacters == null || pooledCharacters.Count == 0) return;

            if (runtimeRandomSeed == 0)
            {
                runtimeRandomSeed = randomizeSeedEveryRun ? UnityEngine.Random.Range(1000, 9999999) : customRandomSeed;
            }

            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
            float curBaseRotY = isHorizontal
                ? characterRotationEuler.y + (faceBackSide ? 0f : 180f)
                : characterRotationEuler.y + (faceBackSide ? 180f : 0f);
            Vector3 baseEuler = new Vector3(characterRotationEuler.x, curBaseRotY, characterRotationEuler.z);

            var sim = FindAnyObjectByType<SandBlocks.Core.Simulation.PBDCrowdSimulation>();
            if (sim != null && sim.Particles.IsCreated && sim.ActiveCount > 0)
            {
                int count = Mathf.Min(pooledCharacters.Count, sim.ActiveCount);
                for (int i = 0; i < count; i++)
                {
                    if (pooledCharacters[i] != null)
                    {
                        var p = sim.Particles[i];
                        if (p.isActive)
                        {
                            pooledCharacters[i].transform.position = GetCalculatedWorldPosition(p.position);

                            int seedY = i * 2399 + runtimeRandomSeed * 10007 + 3;
                            int seedZ = i * 4001 + runtimeRandomSeed * 13337 + 5;
                            float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                                ? GetStableRandom(seedY, rotationJitterYRange.x, rotationJitterYRange.y)
                                : 0f;
                            float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                                ? GetStableRandom(seedZ, rotationJitterZRange.x, rotationJitterZRange.y)
                                : 0f;

                            var pass = (i < pooledPassengers.Count) ? pooledPassengers[i] : pooledCharacters[i].GetComponent<SandBlocks.Gameplay.CharacterPassenger>();
                            if (pass != null)
                            {
                                pass.ApplyRotationJitter(baseEuler, jitterY, jitterZ);
                            }
                            else
                            {
                                pooledCharacters[i].transform.rotation = Quaternion.Euler(characterRotationEuler.x, curBaseRotY + jitterY, characterRotationEuler.z + jitterZ);
                            }
                        }
                    }
                }
            }
            else
            {
                float targetY = EffectiveElevationY;
                for (int i = 0; i < pooledCharacters.Count; i++)
                {
                    if (pooledCharacters[i] != null)
                    {
                        Vector3 pos = pooledCharacters[i].transform.position;
                        if (isHorizontal)
                        {
                            pos.y = targetY;
                        }
                        else
                        {
                            pos.z = crowdZOffset;
                        }
                        pooledCharacters[i].transform.position = pos;
                    }
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            SyncLiveCharacterPositions();
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        if (containerBoundary == null) containerBoundary = FindAnyObjectByType<SandBlocks.Core.Boundary.ContainerBoundary>();
                        if (containerBoundary != null && containerBoundary.AutoExtendWithCrowdSpacing)
                        {
                            containerBoundary.BuildVisualContainer();
                        }
                    }
                };
            }
            else
            {
                if (containerBoundary == null) containerBoundary = FindAnyObjectByType<SandBlocks.Core.Boundary.ContainerBoundary>();
                if (containerBoundary != null && containerBoundary.AutoExtendWithCrowdSpacing)
                {
                    containerBoundary.BuildVisualContainer();
                }
            }
        }
#endif
}
}
