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
        [SerializeField] private float prefabScale = 0.1f;

        [Header("Character Orientation")]
        [Tooltip("Character rotation in degrees (e.g. 90 on X so characters lay/tilt on board)")]
        [SerializeField] private Vector3 characterRotationEuler = new Vector3(90f, 0f, 0f);

        public float CharacterScale => prefabScale;
        public Vector3 CharacterRotationEuler => characterRotationEuler;

        [Header("Fallback Instanced Mesh Settings")]
        [SerializeField] private Mesh characterMesh;
        [SerializeField] private Material characterMaterial;
        [SerializeField] private Vector3 characterScale = new Vector3(0.55f, 0.55f, 0.55f);

        private CrowdColorPalette palette;
        private List<GameObject> pooledCharacters = new List<GameObject>();
        private List<Renderer[]> pooledRenderers = new List<Renderer[]>();
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

            // Setup fallback mesh if no prefab
            if (characterPrefab == null)
            {
                if (characterMesh == null)
                {
                    var sphereObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    characterMesh = sphereObj.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(sphereObj);
                }

                if (characterMaterial == null)
                {
                    Shader shader = Shader.Find("SandBlocks/CrowdInstancedURP");
                    if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (shader == null) shader = Shader.Find("Standard");
                    characterMaterial = new Material(shader) { enableInstancing = true };
                }
                else
                {
                    characterMaterial.enableInstancing = true;
                }
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

                pooledCharacters.Add(instance);
                pooledRenderers.Add(instance.GetComponentsInChildren<Renderer>());
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

                if (!instance.activeSelf) instance.SetActive(true);

                instance.transform.position = new Vector3(p.position.x, p.position.y, 0f);
                instance.transform.rotation = Quaternion.Euler(characterRotationEuler);
                instance.transform.localScale = Vector3.one * prefabScale;

                // Apply color
                if (palette != null && i < pooledRenderers.Count)
                {
                    Color c = palette.GetColor(p.colorId);
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
