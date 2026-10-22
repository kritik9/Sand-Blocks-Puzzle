using Unity.Collections;
using UnityEngine;
using SandBlocks.Core.Data;
using SandBlocks.Core.Simulation;

namespace SandBlocks.Core.Rendering
{
    public class InstancedCrowdRenderer : MonoBehaviour, ICrowdRenderer
    {
        [Header("Mesh & Material Settings")]
        [SerializeField] private Mesh characterMesh;
        [SerializeField] private Material characterMaterial;
        [SerializeField] private Vector3 characterScale = new Vector3(0.4f, 0.4f, 0.4f);

        private CrowdColorPalette palette;
        private MaterialPropertyBlock propertyBlock;
        private readonly Matrix4x4[] batchMatrices = new Matrix4x4[1023];
        private readonly Vector4[] batchColors = new Vector4[1023];
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Initialize(CrowdColorPalette colorPalette)
        {
            palette = colorPalette;
            propertyBlock = new MaterialPropertyBlock();

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

        public void Render(NativeArray<CrowdParticle> particles, int count)
        {
            if (count == 0 || !particles.IsCreated || characterMesh == null || characterMaterial == null) return;

            int batchIndex = 0;
            Quaternion rot = Quaternion.identity;

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
