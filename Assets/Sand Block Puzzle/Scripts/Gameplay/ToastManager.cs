using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SandBlocks.Gameplay
{
    /// <summary>
    /// Global reusable Toast notification system.
    /// Provides smooth animated pop-in, hold duration, and fade-out animations.
    /// Accessible anywhere via ToastManager.Show("Your message").
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ToastManager : MonoBehaviour
    {
        public static ToastManager Instance { get; private set; }

        [Header("UI References")]
        [Tooltip("CanvasGroup used for fading alpha (auto-detected if null)")]
        [SerializeField] private CanvasGroup canvasGroup;

        [Tooltip("The main container RectTransform to scale/slide (auto-detected if null)")]
        [SerializeField] private RectTransform containerRect;

        [Tooltip("The TextMeshPro text component displaying the message (auto-detected if null)")]
        [SerializeField] private TMP_Text messageText;

        [Tooltip("Optional background image of the toast")]
        [SerializeField] private Image backgroundImage;

        [Header("Duration & Timing")]
        [Tooltip("Time in seconds the toast message stays visible before fading out")]
        [Range(0.5f, 6.0f)]
        [SerializeField] private float displayDuration = 1.8f;

        [Tooltip("Fade-in and pop-in animation duration in seconds")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float fadeInDuration = 0.2f;

        [Tooltip("Fade-out animation duration in seconds")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float fadeOutDuration = 0.25f;

        [Header("Animation Settings")]
        [Tooltip("Animate scale pop on appear")]
        [SerializeField] private bool animateScale = true;

        [Tooltip("Starting scale multiplier for pop-in animation")]
        [Range(0.4f, 1.0f)]
        [SerializeField] private float startScale = 0.85f;

        [Tooltip("Overshoot multiplier for a juicy elastic pop-in (1.0 = none, 1.12 = 12% overshoot)")]
        [Range(1.0f, 1.3f)]
        [SerializeField] private float popOvershoot = 1.08f;

        [Tooltip("Vertical slide distance in pixels during appear animation")]
        [Range(-100f, 100f)]
        [SerializeField] private float slideDistanceY = 25f;

        [Header("Feedback Settings")]
        [Tooltip("Trigger subtle haptic feedback when a toast appears")]
        [SerializeField] private bool triggerHapticOnShow = false;

        private Coroutine activeToastRoutine;
        private Vector2 originalContainerPos;
        private Vector3 originalContainerScale = Vector3.one;
        private bool hasSavedTransform = false;
        private float lastShowTime = -1f;

        public float DisplayDuration
        {
            get => displayDuration;
            set => displayDuration = Mathf.Clamp(value, 0.5f, 10f);
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                // If another instance exists, prefer the active one
                Instance = this;
            }

            AutoDetectComponents();
            SaveOriginalTransform();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            gameObject.SetActive(false);
        }

        private void OnValidate()
        {
            AutoDetectComponents();
        }

        private void AutoDetectComponents()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            if (containerRect == null && transform.childCount > 0)
            {
                var child = transform.Find("Container");
                containerRect = child != null ? child as RectTransform : transform.GetChild(0) as RectTransform;
            }

            if (messageText == null)
            {
                messageText = GetComponentInChildren<TMP_Text>(true);
            }

            if (backgroundImage == null && containerRect != null)
            {
                backgroundImage = containerRect.GetComponent<Image>();
            }
        }

        private void SaveOriginalTransform()
        {
            if (hasSavedTransform) return;
            if (containerRect != null)
            {
                originalContainerPos = containerRect.anchoredPosition;
                originalContainerScale = containerRect.localScale;
                hasSavedTransform = true;
            }
        }

        /// <summary>
        /// Global static helper to show a toast message from anywhere in the codebase.
        /// </summary>
        /// <param name="message">The text to display.</param>
        /// <param name="duration">Optional custom display duration. Defaults to inspector setting if <= 0.</param>
        public static void Show(string message, float duration = -1f)
        {
            if (Instance == null)
            {
                Instance = FindAnyObjectByType<ToastManager>(FindObjectsInactive.Include);
            }

            if (Instance != null)
            {
                Instance.ShowToast(message, duration);
            }
            else
            {
                Debug.Log($"[Toast] {message}");
            }
        }

        /// <summary>
        /// Shows the toast notification with animated pop-in and fade-out.
        /// </summary>
        public void ShowToast(string message, float duration = -1f)
        {
            if (string.IsNullOrEmpty(message)) return;

            AutoDetectComponents();
            SaveOriginalTransform();

            // Prevent rapid identical toast spam
            if (Time.unscaledTime - lastShowTime < 0.2f && messageText != null && messageText.text == message)
            {
                return;
            }
            lastShowTime = Time.unscaledTime;

            if (activeToastRoutine != null)
            {
                StopCoroutine(activeToastRoutine);
            }

            gameObject.SetActive(true);

            if (messageText != null)
            {
                messageText.text = message;
            }

            // Force layout rebuild so content size fitters resize properly for short or long text
            if (containerRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            }

            if (triggerHapticOnShow)
            {
                Core.Haptics.HapticManager.Instance?.LightImpact();
            }

            float holdTime = (duration > 0f) ? duration : displayDuration;
            activeToastRoutine = StartCoroutine(AnimateToastRoutine(holdTime));
        }

        private IEnumerator AnimateToastRoutine(float holdTime)
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

            Vector2 basePos = hasSavedTransform ? originalContainerPos : (containerRect != null ? containerRect.anchoredPosition : Vector2.zero);
            Vector2 startPos = basePos - new Vector2(0f, slideDistanceY);
            Vector3 baseScale = hasSavedTransform ? originalContainerScale : Vector3.one;

            // Phase 1: Pop-in & Fade In
            float elapsed = 0f;
            float inDur = Mathf.Max(0.05f, fadeInDuration);

            while (elapsed < inDur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / inDur);

                // Smooth ease-out curve
                float alphaEase = Mathf.Sin(t * Mathf.PI * 0.5f);
                if (canvasGroup != null) canvasGroup.alpha = alphaEase;

                if (containerRect != null)
                {
                    // Vertical slide
                    containerRect.anchoredPosition = Vector2.Lerp(startPos, basePos, alphaEase);

                    // Elastic scale pop
                    if (animateScale)
                    {
                        float currentScale;
                        if (t < 0.7f)
                        {
                            float st = t / 0.7f;
                            currentScale = Mathf.Lerp(startScale, popOvershoot, st);
                        }
                        else
                        {
                            float st = (t - 0.7f) / 0.3f;
                            currentScale = Mathf.Lerp(popOvershoot, 1.0f, st);
                        }
                        containerRect.localScale = baseScale * currentScale;
                    }
                }

                yield return null;
            }

            // Lock to exact settled position
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            if (containerRect != null)
            {
                containerRect.anchoredPosition = basePos;
                containerRect.localScale = baseScale;
            }

            // Phase 2: Hold visible
            yield return new WaitForSecondsRealtime(holdTime);

            // Phase 3: Fade out
            elapsed = 0f;
            float outDur = Mathf.Max(0.05f, fadeOutDuration);

            while (elapsed < outDur)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / outDur);
                float ease = t * t; // Ease in

                if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(1f, 0f, ease);

                if (containerRect != null && animateScale)
                {
                    containerRect.localScale = Vector3.Lerp(baseScale, baseScale * 0.92f, ease);
                }

                yield return null;
            }

            // Cleanup & hide
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (containerRect != null)
            {
                containerRect.anchoredPosition = basePos;
                containerRect.localScale = baseScale;
            }

            gameObject.SetActive(false);
            activeToastRoutine = null;
        }

        /// <summary>
        /// Immediately hides any active toast without animation.
        /// </summary>
        public void HideImmediate()
        {
            if (activeToastRoutine != null)
            {
                StopCoroutine(activeToastRoutine);
                activeToastRoutine = null;
            }

            if (canvasGroup != null) canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }
    }
}
