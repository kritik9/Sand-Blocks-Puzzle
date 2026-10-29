using System.Collections.Generic;
using UnityEngine;

namespace SandBlocks.Gameplay
{
    public enum DollVariation
    {
        Random = -1,
        Doll1 = 0,
        Doll2 = 1,
        Doll3 = 2
    }

    /// <summary>
    /// Controls a 3D character passenger with Idle, Running, and Sitting animations,
    /// dynamic palette coloring, scale adjustments, and visibility management.
    /// Supports multi-variation character models (e.g. Doll1, Doll2, Doll3).
    /// </summary>
    public class CharacterPassenger : MonoBehaviour
    {
        [Header("Animator Components")]
        [SerializeField] private Animator animator;
        [SerializeField] private Renderer[] renderers;

        [Header("Doll Variations")]
        [Tooltip("Selected doll variation, or Random for crowd diversity")]
        [SerializeField] private DollVariation variation = DollVariation.Random;
        [SerializeField] private GameObject[] variationObjects;

        private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
        private static readonly int IsSittingHash = Animator.StringToHash("IsSitting");
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock propBlock;

        public Animator Animator => animator;
        public DollVariation Variation => variation;
        public GameObject[] VariationObjects => variationObjects;

        private static Material fallbackSharedMaterial;

        private void Awake()
        {
            ApplyVariation();
            FindComponents();
            EnsureValidMaterials();
            DisableColliders();
        }

        /// <summary>
        /// Selects and activates the designated variation object, deactivating the others.
        /// </summary>
        public void ApplyVariation()
        {
            if (variationObjects == null || variationObjects.Length == 0)
            {
                var childCount = transform.childCount;
                if (childCount > 1)
                {
                    var list = new List<GameObject>();
                    for (int i = 0; i < childCount; i++) list.Add(transform.GetChild(i).gameObject);
                    variationObjects = list.ToArray();
                }
            }

            if (variationObjects != null && variationObjects.Length > 0)
            {
                int selectedIndex = (variation == DollVariation.Random)
                    ? UnityEngine.Random.Range(0, variationObjects.Length)
                    : Mathf.Clamp((int)variation, 0, variationObjects.Length - 1);

                for (int i = 0; i < variationObjects.Length; i++)
                {
                    if (variationObjects[i] != null)
                    {
                        variationObjects[i].SetActive(i == selectedIndex);
                    }
                }
            }
        }

        /// <summary>
        /// Explicitly sets the variation mode (e.g. Doll1, Doll2, Doll3, or Random).
        /// </summary>
        public void SetVariation(DollVariation v)
        {
            variation = v;
            ApplyVariation();
            FindComponents();
        }

        /// <summary>
        /// Sets a specific variation index (e.g. 0 for Doll1, 1 for Doll2, 2 for Doll3).
        /// </summary>
        public void SetVariationIndex(int index)
        {
            if (variationObjects == null || variationObjects.Length == 0) ApplyVariation();
            if (variationObjects != null && variationObjects.Length > 0)
            {
                int selectedIndex = Mathf.Clamp(index % variationObjects.Length, 0, variationObjects.Length - 1);
                for (int i = 0; i < variationObjects.Length; i++)
                {
                    if (variationObjects[i] != null)
                    {
                        variationObjects[i].SetActive(i == selectedIndex);
                    }
                }
                FindComponents();
            }
        }

        private void FindComponents()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            // Find all renderers in children (include inactive so SetColor caches all variations)
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        /// <summary>
        /// Validates that all renderers have a functional material assigned.
        /// If a material is missing or invalid, assigns a shared URP Lit instanced material.
        /// </summary>
        public void EnsureValidMaterials()
        {
            if (renderers == null || renderers.Length == 0) return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;

                var mat = r.sharedMaterial;
                bool isMissing = (mat == null) || (mat.shader == null) || (mat.shader.name == "Hidden/InternalErrorShader");

                if (isMissing)
                {
                    if (fallbackSharedMaterial == null)
                    {
                        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                                     ?? Shader.Find("SandBlocks/CrowdInstancedURP")
                                     ?? Shader.Find("Standard");
                        if (shader != null)
                        {
                            fallbackSharedMaterial = new Material(shader)
                            {
                                name = "Runtime_Doll_LitFallback",
                                enableInstancing = true
                            };
                            fallbackSharedMaterial.SetColor(BaseColorProp, Color.white);
                        }
                    }

                    if (fallbackSharedMaterial != null)
                    {
                        r.sharedMaterial = fallbackSharedMaterial;
                    }
                }
            }
        }

        private void DisableColliders()
        {
            // Characters should not block drag raycasts or physics
            var colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = false;
            }
        }

        /// <summary>
        /// Plays the standing idle animation.
        /// </summary>
        public void PlayIdle()
        {
            FindComponents();
            if (animator != null)
            {
                animator.enabled = true;
                animator.SetBool(IsSittingHash, false);
                animator.SetBool(IsRunningHash, false);
            }
        }

        /// <summary>
        /// Plays the running animation (used during commuter flight to the bus).
        /// </summary>
        public void PlayRunning()
        {
            FindComponents();
            if (animator != null)
            {
                animator.enabled = true;
                animator.SetBool(IsSittingHash, false);
                animator.SetBool(IsRunningHash, true);
            }
        }

        /// <summary>
        /// Plays the sitting animation (used when seated in the bus chair).
        /// </summary>
        public void PlaySitting()
        {
            FindComponents();
            if (animator != null)
            {
                animator.SetBool(IsRunningHash, false);
                animator.SetBool(IsSittingHash, true);
            }
        }

        /// <summary>
        /// Sets the color of the character's clothing/body matching the level palette.
        /// </summary>
        public void SetColor(Color color)
        {
            FindComponents();
            EnsureValidMaterials();
            if (propBlock == null) propBlock = new MaterialPropertyBlock();
            propBlock.SetColor(BaseColorProp, color);
            propBlock.SetColor(ColorProp, color);

            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                    {
                        renderers[i].SetPropertyBlock(propBlock);
                    }
                }
            }
        }

        /// <summary>
        /// Adjusts the character's pitch tilt during jump flight so that feet swing forward
        /// and the entire front body is clearly visible from top-down tilted camera angles.
        /// </summary>
        /// <param name="tiltProgress">Normalized flight progress (0 = upright takeoff, 1 = feet fully forward on landing).</param>
        /// <param name="maxTiltAngle">Maximum pitch angle in degrees (e.g. 35 to 40 deg).</param>
        /// <param name="baseEuler">Base local Euler rotation (e.g. Vector3.zero).</param>
        public void ApplyJumpFlightTilt(float tiltProgress, float maxTiltAngle, Vector3 baseEuler)
        {
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tiltProgress));
            float tilt = eased * maxTiltAngle;
            transform.rotation = Quaternion.Euler(baseEuler.x + tilt, baseEuler.y, baseEuler.z);
        }

        /// <summary>
        /// Adjusts the character's uniform scale.
        /// </summary>
        public void SetScale(float uniformScale)
        {
            transform.localScale = Vector3.one * uniformScale;
        }

        /// <summary>
        /// Shows or hides the character GameObject.
        /// </summary>
        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }
    }
}
