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

        [Header("Character Orientation")]
        [Tooltip("Character rotation in degrees (e.g. 0,0,0 for upright Y-up character)")]
        [SerializeField] private Vector3 characterRotationEuler = Vector3.zero;

        public float CharacterScale => prefabScale;
        public Vector3 CharacterRotationEuler => characterRotationEuler;

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
        private MaterialPropertyBlock propertyBlock;
        private readonly Matrix4x4[] batchMatrices = new Matrix4x4[1023];
        private readonly Vector4[] batchColors = new Vector4[1023];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId_Prop = Shader.PropertyToID("_Color");

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
            }

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

                instance.transform.position = new Vector3(p.position.x, p.position.y, 0f);
                instance.transform.rotation = Quaternion.Euler(characterRotationEuler);
                instance.transform.localScale = Vector3.one * (prefabScale * scaleMult);

                // Apply color
                if (palette != null)
                {
                    Color c = palette.GetColor(p.colorId);
                    var pass = instance.GetComponent<SandBlocks.Gameplay.CharacterPassenger>();
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
            Quaternion rot = Quaternion.Euler(characterRotationEuler);

            for (int i = 0; i < count; i++)
            {
                var p = particles[i];
                if (!p.isActive) continue;

                Vector3 pos = new Vector3(p.position.x, p.position.y, 0f);
                batchMatrices[batchIndex] = Matrix4x4.TRS(pos, rot, characterScale);

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
    }
}
