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

        [Header("Rotation Jitter Target")]
        [Tooltip("Child object in this character prefab to which rotation jitter (yaw & tilt) is applied. If unassigned, jitter is applied to this character's root transform.")]
        [SerializeField] private GameObject rotationTarget;

        [Header("Doll Variations")]
        [Tooltip("Selected doll variation, or Random for crowd diversity")]
        [SerializeField] private DollVariation variation = DollVariation.Random;
        [SerializeField] private GameObject[] variationObjects;

        private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
        private static readonly int IsSittingHash = Animator.StringToHash("IsSitting");
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int OutlineColorProp = Shader.PropertyToID("_Outline_Color");
        private static readonly int OutlineColorAltProp = Shader.PropertyToID("_OutlineColor");
        private static readonly int FirstShadeColorProp = Shader.PropertyToID("_1st_ShadeColor");
        private static readonly int SecondShadeColorProp = Shader.PropertyToID("_2nd_ShadeColor");
        private static readonly int RimLightColorProp = Shader.PropertyToID("_RimLightColor");
        private static readonly int HighColorProp = Shader.PropertyToID("_HighColor");
        private static readonly int RimLightPowerProp = Shader.PropertyToID("_RimLight_Power");
        private static readonly int OutlineWidthProp = Shader.PropertyToID("_Outline_Width");

        [Header("Dynamic Styling & Harmonious Outlines")]
        [Tooltip("If true, dynamically tints outlines, shadows, rim lights, and specular highlights based on the character's color for a polished toy look")]
        [SerializeField] private bool useDynamicStyling = true;

        [Range(0.1f, 0.8f)]
        [Tooltip("Brightness multiplier for dynamic outline (e.g. 0.38 gives deep rich matching color: dark red for red, dark navy for blue, dark gold for yellow)")]
        [SerializeField] private float outlineDarkness = 0.38f;

        [Range(1f, 1.5f)]
        [Tooltip("Saturation boost for dynamic outline so colors stay vivid and stylized rather than dull")]
        [SerializeField] private float outlineSaturationBoost = 1.15f;

        [Tooltip("Optional custom outline width override. Set > 0 to override material outline width per character")]
        [SerializeField] private float customOutlineWidth = 0f;

        [Range(0.3f, 0.95f)]
        [Tooltip("Brightness of 1st shade shadow (e.g. 0.72)")]
        [SerializeField] private float shade1Darkness = 0.72f;

        [Range(0.2f, 0.85f)]
        [Tooltip("Brightness of 2nd shade shadow (e.g. 0.50)")]
        [SerializeField] private float shade2Darkness = 0.50f;

        [Header("Dynamic Rim & Gloss")]
        [Tooltip("If true, injects a soft luminous pastel rim light matching the character's hue")]
        [SerializeField] private bool enableDynamicRimLight = true;

        [Range(0f, 1f)]
        [Tooltip("Power of dynamic rim light (e.g. 0.35)")]
        [SerializeField] private float dynamicRimLightPower = 0.35f;

        [Range(0.2f, 0.9f)]
        [Tooltip("Blend with white for dynamic rim light (0.55 = soft pastel tint matching character)")]
        [SerializeField] private float rimLightTintBlend = 0.55f;

        [Tooltip("If true, applies glossy specular highlight on character heads")]
        [SerializeField] private bool enableDynamicSpecular = true;

        private MaterialPropertyBlock propBlock;

        public Animator Animator => animator;
        public DollVariation Variation => variation;
        public GameObject[] VariationObjects => variationObjects;
        public GameObject RotationTarget { get => rotationTarget; set => rotationTarget = value; }
        public Transform EffectiveRotationTarget => (rotationTarget != null) ? rotationTarget.transform : transform;

        private static Material fallbackSharedMaterial;

        private Quaternion initialTargetLocalRotation = Quaternion.identity;
        private bool hasCachedInitialRotation = false;

        private void Awake()
        {
            CacheInitialTargetRotation();
            ApplyVariation();
            FindComponents();
            EnsureValidMaterials();
            DisableColliders();
        }

        private void CacheInitialTargetRotation()
        {
            if (!hasCachedInitialRotation)
            {
                Transform target = (rotationTarget != null) ? rotationTarget.transform : transform;
                initialTargetLocalRotation = target.localRotation;
                hasCachedInitialRotation = true;
            }
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
            ResetRotationJitter();
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
            ResetRotationJitter();
            if (animator != null)
            {
                animator.SetBool(IsRunningHash, false);
                animator.SetBool(IsSittingHash, true);
            }
        }

        /// <summary>
        /// Applies base rotation to root, and organic jitter (yaw & tilt) to the designated rotation target object.
        /// </summary>
        public void ApplyRotationJitter(Vector3 baseEuler, float jitterY, float jitterZ)
        {
            CacheInitialTargetRotation();
            Transform target = (rotationTarget != null) ? rotationTarget.transform : null;

            if (target != null && target != transform)
            {
                transform.rotation = Quaternion.Euler(baseEuler);
                target.localRotation = initialTargetLocalRotation * Quaternion.Euler(0f, jitterY, jitterZ);
            }
            else
            {
                transform.rotation = Quaternion.Euler(baseEuler.x, baseEuler.y + jitterY, baseEuler.z + jitterZ);
            }
        }

        /// <summary>
        /// Resets the rotation target object back to its initial local rotation (e.g. when flying or seated).
        /// </summary>
        public void ResetRotationJitter()
        {
            Transform target = (rotationTarget != null) ? rotationTarget.transform : null;
            if (target != null && target != transform && hasCachedInitialRotation)
            {
                target.localRotation = initialTargetLocalRotation;
            }
        }

        /// <summary>
        /// Generates a rich, saturated, darker tone of a given color for dynamic outlines and deep shadows.
        /// Preserves hue vibrancy instead of turning muddy or gray.
        /// </summary>
        public static Color GetDarkerHarmoniousColor(Color baseColor, float brightnessMultiplier = 0.38f, float saturationBoost = 1.15f)
        {
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            if (s > 0.05f)
            {
                s = Mathf.Clamp01(s * saturationBoost);
                v = Mathf.Clamp01(v * brightnessMultiplier);
            }
            else
            {
                // Neutral white / gray gets stylish graphite outline
                v = Mathf.Clamp(v * brightnessMultiplier, 0.25f, 0.45f);
            }
            Color result = Color.HSVToRGB(h, s, v);
            result.a = baseColor.a;
            return result;
        }

        /// <summary>
        /// Generates a soft, pastel, luminous tone of a given color for dynamic rim lights and specular highlights.
        /// </summary>
        public static Color GetLighterLuminousColor(Color baseColor, float blendWithWhite = 0.55f)
        {
            Color result = Color.Lerp(baseColor, Color.white, blendWithWhite);
            result.a = baseColor.a;
            return result;
        }

        /// <summary>
        /// Sets the color of the character's clothing/body matching the level palette,
        /// and harmonizes matching dynamic outlines, shaded shadows, rim lights, and gloss.
        /// </summary>
        public void SetColor(Color color)
        {
            FindComponents();
            EnsureValidMaterials();
            if (propBlock == null) propBlock = new MaterialPropertyBlock();

            // 1. Base Albedo & Color
            propBlock.SetColor(BaseColorProp, color);
            propBlock.SetColor(ColorProp, color);

            if (useDynamicStyling)
            {
                // 2. Dynamic Matching Outline Color (Dark Red for Red, Dark Blue for Blue, Dark Amber for Yellow, etc.)
                Color outlineColor = GetDarkerHarmoniousColor(color, outlineDarkness, outlineSaturationBoost);
                propBlock.SetColor(OutlineColorProp, outlineColor);
                propBlock.SetColor(OutlineColorAltProp, outlineColor);

                // 3. Dynamic Matching 1st & 2nd Shade Colors (Rich matching shadows for UTS3)
                Color shade1 = GetDarkerHarmoniousColor(color, shade1Darkness, 1.05f);
                Color shade2 = GetDarkerHarmoniousColor(color, shade2Darkness, 1.10f);
                propBlock.SetColor(FirstShadeColorProp, shade1);
                propBlock.SetColor(SecondShadeColorProp, shade2);

                // 4. Dynamic Tinted Rim Light Color (Soft pastel glow matching character color)
                if (enableDynamicRimLight)
                {
                    Color rimColor = GetLighterLuminousColor(color, rimLightTintBlend);
                    propBlock.SetColor(RimLightColorProp, rimColor);
                    if (dynamicRimLightPower > 0f)
                    {
                        propBlock.SetFloat(RimLightPowerProp, dynamicRimLightPower);
                    }
                }

                // 5. Dynamic HighColor / Specular Gloss
                if (enableDynamicSpecular)
                {
                    Color specColor = GetLighterLuminousColor(color, 0.85f);
                    propBlock.SetColor(HighColorProp, specColor);
                }

                // 6. Optional Per-Character Outline Width Override
                if (customOutlineWidth > 0f)
                {
                    propBlock.SetFloat(OutlineWidthProp, customOutlineWidth);
                }
            }

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
