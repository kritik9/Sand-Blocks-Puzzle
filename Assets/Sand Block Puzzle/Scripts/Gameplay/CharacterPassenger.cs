using UnityEngine;

namespace SandBlocks.Gameplay
{
    /// <summary>
    /// Controls a 3D character passenger with Idle, Running, and Sitting animations,
    /// dynamic palette coloring, scale adjustments, and visibility management.
    /// </summary>
    public class CharacterPassenger : MonoBehaviour
    {
        [Header("Animator Components")]
        [SerializeField] private Animator animator;
        [SerializeField] private Renderer[] renderers;

        private static readonly int IsRunningHash = Animator.StringToHash("isRunning");
        private static readonly int IsSittingHash = Animator.StringToHash("IsSitting");
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock propBlock;

        public Animator Animator => animator;

        private void Awake()
        {
            FindComponents();
            DisableColliders();
        }

        private void FindComponents()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
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
                animator.SetBool(IsRunningHash, false);
                animator.SetBool(IsSittingHash, false);
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
