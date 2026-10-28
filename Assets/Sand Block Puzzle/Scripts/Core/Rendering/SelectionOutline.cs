using System;
using System.Collections.Generic;
using UnityEngine;

namespace SandBlocks.Core.Rendering
{
    /// <summary>
    /// Reusable component that adds a clean, minimal, optimized silhouette outline to any GameObject.
    /// Uses an inverted hull technique with a dedicated lightweight unlit outline shell,
    /// ensuring zero runtime garbage collection allocations and full compatibility with any mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public class SelectionOutline : MonoBehaviour
    {
        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static Material sharedFallbackMaterial;

        [Header("Outline Appearance")]
        [Tooltip("The outline material (uses SandBlocks/SelectionOutline shader). If null, auto-loads the default material.")]
        [SerializeField] private Material outlineMaterial;

        [Tooltip("Base color of the outline highlight.")]
        [SerializeField] private Color outlineColor = Color.white;

        [Tooltip("Thickness of the outline in world units. Default ~0.045f provides a clean, minimal silhouette.")]
        [Range(0.01f, 0.2f)]
        [SerializeField] private float outlineWidth = 0.045f;

        [Header("Filtering")]
        [Tooltip("Child GameObjects containing any of these keywords in their name will NOT be outlined (e.g. seats, text, canvas).")]
        [SerializeField] private List<string> ignoreKeywords = new List<string> { "chair", "seat", "passenger", "text", "canvas", "doll" };

        private class OutlineShell
        {
            public GameObject gameObject;
            public Renderer outlineRenderer;
            public Renderer sourceRenderer;
        }

        private readonly List<OutlineShell> shells = new List<OutlineShell>();
        private MaterialPropertyBlock propBlock;
        private bool isOutlineActive = false;
        private bool isInitialized = false;

        public bool IsOutlineActive => isOutlineActive;
        public Color OutlineColor => outlineColor;
        public float OutlineWidth => outlineWidth;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
            if (isOutlineActive)
            {
                ApplyShellVisibility(true);
            }
        }

        private void OnDisable()
        {
            ApplyShellVisibility(false);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < shells.Count; i++)
            {
                if (shells[i] != null && shells[i].gameObject != null)
                {
                    if (Application.isPlaying) Destroy(shells[i].gameObject);
                    else DestroyImmediate(shells[i].gameObject);
                }
            }
            shells.Clear();
        }

        /// <summary>
        /// Ensures outline shells are created and ready for zero-allocation activation.
        /// </summary>
        public void EnsureInitialized()
        {
            if (isInitialized && shells.Count > 0) return;

            ResolveOutlineMaterial();
            BuildOutlineShells();
            isInitialized = true;
        }

        private void ResolveOutlineMaterial()
        {
            if (outlineMaterial != null) return;

            if (sharedFallbackMaterial == null)
            {
#if UNITY_EDITOR
                outlineMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Sand Block Puzzle/Materials/SelectionOutline.mat");
                if (outlineMaterial != null)
                {
                    sharedFallbackMaterial = outlineMaterial;
                    return;
                }
#endif
                var shader = Shader.Find("SandBlocks/SelectionOutline")
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Standard");

                if (shader != null)
                {
                    sharedFallbackMaterial = new Material(shader)
                    {
                        name = "Runtime_SelectionOutline_Mat",
                        hideFlags = HideFlags.DontSave
                    };
                    sharedFallbackMaterial.SetColor(OutlineColorId, outlineColor);
                    sharedFallbackMaterial.SetFloat(OutlineWidthId, outlineWidth);
                }
            }

            outlineMaterial = sharedFallbackMaterial;
        }

        private void BuildOutlineShells()
        {
            // Clear any previously created shells
            for (int i = 0; i < shells.Count; i++)
            {
                if (shells[i]?.gameObject != null)
                {
                    if (Application.isPlaying) Destroy(shells[i].gameObject);
                    else DestroyImmediate(shells[i].gameObject);
                }
            }
            shells.Clear();

            if (outlineMaterial == null) return;

            var allRenderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                var r = allRenderers[i];
                if (r == null) continue;

                // Skip shells or ignored sub-objects
                string goName = r.gameObject.name.ToLower();
                if (goName.StartsWith("_outline")) continue;

                bool isIgnored = false;
                for (int k = 0; k < ignoreKeywords.Count; k++)
                {
                    if (goName.Contains(ignoreKeywords[k].ToLower()))
                    {
                        isIgnored = true;
                        break;
                    }
                }
                if (isIgnored) continue;

                // Handle MeshRenderer
                if (r is MeshRenderer meshRend)
                {
                    var filter = meshRend.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                    {
                        CreateMeshShell(meshRend, filter.sharedMesh);
                    }
                }
                // Handle SkinnedMeshRenderer
                else if (r is SkinnedMeshRenderer skinnedRend && skinnedRend.sharedMesh != null)
                {
                    CreateSkinnedShell(skinnedRend);
                }
            }

            ApplyProperties();
            ApplyShellVisibility(isOutlineActive);
        }

        private void CreateMeshShell(MeshRenderer source, Mesh sharedMesh)
        {
            var shellGo = new GameObject("_OutlineShell");
            shellGo.hideFlags = HideFlags.DontSave;
            shellGo.transform.SetParent(source.transform, false);
            shellGo.transform.localPosition = Vector3.zero;
            shellGo.transform.localRotation = Quaternion.identity;
            shellGo.transform.localScale = Vector3.one;

            var filter = shellGo.AddComponent<MeshFilter>();
            filter.sharedMesh = sharedMesh;

            var rend = shellGo.AddComponent<MeshRenderer>();
            int submeshCount = Mathf.Max(1, sharedMesh.subMeshCount);
            var mats = new Material[submeshCount];
            for (int m = 0; m < submeshCount; m++) mats[m] = outlineMaterial;
            rend.sharedMaterials = mats;

            // Turn off shadows, probes, and physics for minimal performance cost
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            shellGo.SetActive(false);

            shells.Add(new OutlineShell
            {
                gameObject = shellGo,
                outlineRenderer = rend,
                sourceRenderer = source
            });
        }

        private void CreateSkinnedShell(SkinnedMeshRenderer source)
        {
            var shellGo = new GameObject("_OutlineShell");
            shellGo.hideFlags = HideFlags.DontSave;
            shellGo.transform.SetParent(source.transform, false);
            shellGo.transform.localPosition = Vector3.zero;
            shellGo.transform.localRotation = Quaternion.identity;
            shellGo.transform.localScale = Vector3.one;

            var rend = shellGo.AddComponent<SkinnedMeshRenderer>();
            rend.sharedMesh = source.sharedMesh;
            rend.bones = source.bones;
            rend.rootBone = source.rootBone;

            int submeshCount = Mathf.Max(1, source.sharedMesh.subMeshCount);
            var mats = new Material[submeshCount];
            for (int m = 0; m < submeshCount; m++) mats[m] = outlineMaterial;
            rend.sharedMaterials = mats;

            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            shellGo.SetActive(false);

            shells.Add(new OutlineShell
            {
                gameObject = shellGo,
                outlineRenderer = rend,
                sourceRenderer = source
            });
        }

        private void ApplyShellVisibility(bool visible)
        {
            for (int i = 0; i < shells.Count; i++)
            {
                if (shells[i] != null && shells[i].gameObject != null)
                {
                    shells[i].gameObject.SetActive(visible);
                }
            }
        }

        private void ApplyProperties()
        {
            if (propBlock == null) propBlock = new MaterialPropertyBlock();

            propBlock.SetColor(OutlineColorId, outlineColor);
            propBlock.SetFloat(OutlineWidthId, outlineWidth);

            for (int i = 0; i < shells.Count; i++)
            {
                if (shells[i]?.outlineRenderer != null)
                {
                    shells[i].outlineRenderer.SetPropertyBlock(propBlock);
                }
            }
        }

        /// <summary>
        /// Enables or disables the silhouette outline.
        /// Zero allocations during gameplay.
        /// </summary>
        public void SetOutlineEnabled(bool enabled)
        {
            isOutlineActive = enabled;
            EnsureInitialized();
            ApplyProperties();
            ApplyShellVisibility(enabled);
        }

        /// <summary>
        /// Updates the outline color dynamically (e.g. to match vehicle color or custom state).
        /// </summary>
        public void SetOutlineColor(Color color)
        {
            outlineColor = color;
            ApplyProperties();
        }

        /// <summary>
        /// Updates the outline thickness dynamically.
        /// </summary>
        public void SetOutlineWidth(float width)
        {
            outlineWidth = width;
            ApplyProperties();
        }

        // =========================================================================
        // REUSABLE STATIC CONVENIENCE METHODS (Call from anywhere on any GameObject)
        // =========================================================================

        /// <summary>
        /// Gets the SelectionOutline on the target GameObject, or adds one dynamically if missing.
        /// </summary>
        public static SelectionOutline GetOrAdd(GameObject target)
        {
            if (target == null) return null;
            var outline = target.GetComponent<SelectionOutline>();
            if (outline == null)
            {
                outline = target.AddComponent<SelectionOutline>();
            }
            return outline;
        }

        /// <summary>
        /// One-line utility to highlight or unhighlight any GameObject across the game.
        /// </summary>
        public static void SetHighlighted(GameObject target, bool highlighted, Color? color = null)
        {
            if (target == null) return;
            var outline = GetOrAdd(target);
            if (outline != null)
            {
                if (color.HasValue)
                {
                    outline.SetOutlineColor(color.Value);
                }
                outline.SetOutlineEnabled(highlighted);
            }
        }
    }
}
