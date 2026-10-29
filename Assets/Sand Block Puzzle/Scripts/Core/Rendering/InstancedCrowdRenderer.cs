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
        private MaterialPropertyBlock propertyBlock;
        private readonly Matrix4x4[] batchMatrices = new Matrix4x4[1023];
        private readonly Vector4[] batchColors = new Vector4[1023];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_Prop = Shader.PropertyToID("_Color");

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

                // 1. Position: apply elevation (EffectiveElevationY in Horizontal_XZ)
                instance.transform.position = isHorizontal
                    ? new Vector3(p.position.x, EffectiveElevationY, p.position.y + crowdZOffset)
                    : new Vector3(p.position.x, p.position.y + crowdYOffset, crowdZOffset);

                var pass = (i < pooledPassengers.Count) ? pooledPassengers[i] : instance.GetComponent<SandBlocks.Gameplay.CharacterPassenger>();

                // 2. Rotation: base rotation + back-side flip + slight random organic jitter (yaw & tilt)
                float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                    ? GetStableRandom(i * 11 + 3, rotationJitterYRange.x, rotationJitterYRange.y)
                    : 0f;
                float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                    ? GetStableRandom(i * 13 + 5, rotationJitterZRange.x, rotationJitterZRange.y)
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

                // 3. Scale: uniform base * spawn pop * stable random scale variation
                float randScaleMult = (scaleVariationRange.x != 1f || scaleVariationRange.y != 1f)
                    ? GetStableRandom(i * 7 + 1, scaleVariationRange.x, scaleVariationRange.y)
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

            for (int i = 0; i < count; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
                Vector3 pos = isHorizontal
                    ? new Vector3(p.position.x, EffectiveElevationY, p.position.y + crowdZOffset)
                    : new Vector3(p.position.x, p.position.y + crowdYOffset, crowdZOffset);

                float jitterY = (rotationJitterYRange.x != 0f || rotationJitterYRange.y != 0f)
                    ? GetStableRandom(i * 11 + 3, rotationJitterYRange.x, rotationJitterYRange.y)
                    : 0f;
                float jitterZ = (rotationJitterZRange.x != 0f || rotationJitterZRange.y != 0f)
                    ? GetStableRandom(i * 13 + 5, rotationJitterZRange.x, rotationJitterZRange.y)
                    : 0f;
                float curBaseRotY = isHorizontal
                    ? characterRotationEuler.y + (faceBackSide ? 0f : 180f)
                    : characterRotationEuler.y + (faceBackSide ? 180f : 0f);
                Quaternion rot = Quaternion.Euler(characterRotationEuler.x, curBaseRotY + jitterY, characterRotationEuler.z + jitterZ);

                float randScaleMult = (scaleVariationRange.x != 1f || scaleVariationRange.y != 1f)
                    ? GetStableRandom(i * 7 + 1, scaleVariationRange.x, scaleVariationRange.y)
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
            bool isHorizontal = (SandBlocks.Gameplay.GameManager.CurrentOrientation == SandBlocks.Core.Data.BoardOrientation.Horizontal_XZ);
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

#if UNITY_EDITOR
        private void OnValidate()
        {
            SyncLiveCharacterPositions();
        }
#endif
}
}