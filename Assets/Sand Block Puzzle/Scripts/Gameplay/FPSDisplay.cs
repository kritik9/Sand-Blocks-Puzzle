using UnityEngine;
using TMPro;

namespace SandBlocks.Gameplay
{
    public class FPSDisplay : MonoBehaviour
    {
        [Header("UI Reference")]
        [Tooltip("TextMeshPro component where the FPS value will be displayed")]
        [SerializeField] private TMP_Text fpsText;

        [Header("Settings")]
        [SerializeField] private float updateInterval = 0.35f;
        [SerializeField] private bool showFrameTime = false;
        [SerializeField] private bool colorCodeFPS = true;

        [Header("Color Thresholds")]
        [SerializeField] private Color goodColor = new Color(0.2f, 0.95f, 0.4f);
        [SerializeField] private Color okColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color badColor = new Color(1f, 0.3f, 0.3f);

        private float accumTime = 0f;
        private int frameCount = 0;
        private float currentFPS = 60f;
        private float currentFrameTimeMs = 16.6f;

        public TMP_Text TargetText
        {
            get => fpsText;
            set => fpsText = value;
        }

        public float CurrentFPS => currentFPS;

        private void Reset()
        {
            if (fpsText == null)
            {
                fpsText = GetComponent<TMP_Text>();
                if (fpsText == null) fpsText = GetComponentInChildren<TMP_Text>();
            }
        }

        private void Awake()
        {
            if (fpsText == null)
            {
                fpsText = GetComponent<TMP_Text>();
                if (fpsText == null) fpsText = GetComponentInChildren<TMP_Text>();
            }
        }

        private void Update()
        {
            accumTime += Time.unscaledDeltaTime;
            frameCount++;

            if (accumTime >= updateInterval)
            {
                currentFPS = frameCount / accumTime;
                currentFrameTimeMs = (accumTime / frameCount) * 1000f;
                accumTime = 0f;
                frameCount = 0;

                UpdateDisplay();
            }
        }

        private void UpdateDisplay()
        {
            if (fpsText == null) return;

            int roundedFPS = Mathf.RoundToInt(currentFPS);

            if (showFrameTime)
            {
                fpsText.text = $"{roundedFPS} FPS ({currentFrameTimeMs:F1}ms)";
            }
            else
            {
                fpsText.text = $"{roundedFPS} FPS";
            }

            if (colorCodeFPS)
            {
                if (currentFPS >= 50f)
                {
                    fpsText.color = goodColor;
                }
                else if (currentFPS >= 30f)
                {
                    fpsText.color = okColor;
                }
                else
                {
                    fpsText.color = badColor;
                }
            }
        }
    }
}
