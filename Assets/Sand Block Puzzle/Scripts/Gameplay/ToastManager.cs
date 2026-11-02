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
                Instance = this;
            }

            AutoDetectComponents();
            EnsureCanvasSorting();
            SaveOriginalTransform();

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
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

        /// <summary>
        /// Guarantees the toast is rendered above all dialogs, popups, and HUD screens.
        /// If nested under InGameUI (which can be disabled), reparents directly to the root Canvas.
        /// </summary>
        public void EnsureCanvasSorting()
        {
            // If parented to InGameUI or sub-panels, reparent to root Canvas so HUD toggles never deactivate Toast
            if (transform.parent != null && (transform.parent.name == "InGameUI" || transform.parent.name.Contains("Panel")))
            {
                var rootCanvas = GetComponentInParent<Canvas>(true);
                if (rootCanvas != null && rootCanvas.transform != transform.parent)
                {
                    transform.SetParent(rootCanvas.transform, false);
                }
            }

            // Ensure Toast has its own Canvas with max sorting order so nothing obscures it
            var canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9999;

            var raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null)
            {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            transform.SetAsLastSibling();
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
        /// Immediately dismisses any active toast.
        /// </summary>
        public static void DismissCurrent()
        {
            Instance?.HideImmediate();
        }

        /// <summary>
        /// Global static helper to show a toast message from anywhere in the codebase.
        /// Auto-finds or dynamically instantiates Toast UI if not present in the scene.
        /// </summary>
        /// <param name="message">The text to display.</param>
        /// <param name="duration">Optional custom display duration. Defaults to inspector setting if <= 0.</param>
        public static void Show(string message, float duration = -1f)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (Instance == null)
            {
                Instance = FindAnyObjectByType<ToastManager>(FindObjectsInactive.Include);
                if (Instance == null)
                {
                    Instance = CreateDynamicFallback();
                }
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

        private static ToastManager CreateDynamicFallback()
        {
            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null) return null;

            var toastObj = new GameObject("ToastMessage", typeof(RectTransform), typeof(CanvasGroup), typeof(ToastManager));
            toastObj.transform.SetParent(canvas.transform, false);
            var rect = toastObj.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            var containerObj = new GameObject("Container", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            containerObj.transform.SetParent(toastObj.transform, false);
            var contRect = containerObj.GetComponent<RectTransform>();
            contRect.anchorMin = new Vector2(0.5f, 0.5f);
            contRect.anchorMax = new Vector2(0.5f, 0.5f);
            contRect.sizeDelta = new Vector2(720f, 0f);
            contRect.anchoredPosition = Vector2.zero;

            var img = containerObj.GetComponent<Image>();
            img.color = new Color(0.12f, 0.12f, 0.16f, 0.94f);

            var vlg = containerObj.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(40, 40, 24, 24);
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            var csf = containerObj.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textObj = new GameObject("MessageText", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(containerObj.transform, false);
            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 32f;
            tmp.color = Color.white;

            var tm = toastObj.GetComponent<ToastManager>();
            tm.canvasGroup = toastObj.GetComponent<CanvasGroup>();
            tm.containerRect = contRect;
            tm.messageText = tmp;
            tm.backgroundImage = img;
            tm.displayDuration = 1.8f;
            Instance = tm;
            return tm;
        }

        /// <summary>
        /// Shows the toast notification with animated pop-in and fade-out.
        /// </summary>
        public void ShowToast(string message, float duration = -1f)
        {
            if (string.IsNullOrEmpty(message)) return;

            EnsureCanvasSorting();
            AutoDetectComponents();
            SaveOriginalTransform();

            // Prevent rapid identical spam within fraction of second
            if (Time.unscaledTime - lastShowTime < 0.25f && messageText != null && messageText.text == message && activeToastRoutine != null)
            {
                return;
            }
            lastShowTime = Time.unscaledTime;

            if (activeToastRoutine != null)
            {
                StopCoroutine(activeToastRoutine);
                activeToastRoutine = null;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
            transform.SetAsLastSibling();

            if (messageText != null)
            {
                messageText.text = message;
            }

            if (containerRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            }

            if (triggerHapticOnShow)
            {
                Core.Haptics.HapticManager.Instance?.LightImpact();
            }

            float holdTime = (duration > 0f) ? duration : Mathf.Max(displayDuration, 1.8f);
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

                float alphaEase = Mathf.Sin(t * Mathf.PI * 0.5f);
                if (canvasGroup != null) canvasGroup.alpha = alphaEase;

                if (containerRect != null)
                {
                    containerRect.anchoredPosition = Vector2.Lerp(startPos, basePos, alphaEase);

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

            // Settle exactly
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
                float ease = t * t;

                if (canvasGroup != null) canvasGroup.alpha = Mathf.Lerp(1f, 0f, ease);

                if (containerRect != null && animateScale)
                {
                    containerRect.localScale = Vector3.Lerp(baseScale, baseScale * 0.92f, ease);
                }

                yield return null;
            }

            // Cleanup & hide
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }
            if (containerRect != null)
            {
                containerRect.anchoredPosition = basePos;
                containerRect.localScale = baseScale;
            }

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

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }
        }
    }
}
