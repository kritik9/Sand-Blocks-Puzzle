using System.Collections;
using UnityEngine;

namespace SandBlocks.Gameplay
{
    /// <summary>
    /// Smooth breathing scale in/out animation component for UI buttons (e.g. Play Button on HomeScreen).
    /// </summary>
    public class UIButtonPulse : MonoBehaviour
    {
        [Header("Pulse Settings")]
        [Tooltip("Enable or disable pulsing animation")]
        [SerializeField] private bool enablePulse = true;

        [Tooltip("Speed of the breathing pulse cycle")]
        [Range(0.5f, 8f)]
        [SerializeField] private float pulseSpeed = 2.4f;

        [Tooltip("Minimum scale multiplier during the pulse cycle")]
        [Range(0.8f, 1.0f)]
        [SerializeField] private float minScale = 0.94f;

        [Tooltip("Maximum scale multiplier during the pulse cycle")]
        [Range(1.0f, 1.3f)]
        [SerializeField] private float maxScale = 1.08f;

        [Tooltip("Use unscaled time so animation runs smoothly even when game timescale is paused")]
        [SerializeField] private bool useUnscaledTime = true;

        private Vector3 originalScale = Vector3.one;
        private bool hasSavedScale = false;
        private Coroutine pulseRoutine;

        public bool EnablePulse
        {
            get => enablePulse;
            set
            {
                enablePulse = value;
                if (!enablePulse) StopPulse();
                else if (gameObject.activeInHierarchy) StartPulse();
            }
        }

        public float PulseSpeed { get => pulseSpeed; set => pulseSpeed = value; }
        public float MinScale { get => minScale; set => minScale = value; }
        public float MaxScale { get => maxScale; set => maxScale = value; }
        public bool UseUnscaledTime { get => useUnscaledTime; set => useUnscaledTime = value; }

        public void Configure(float speed, float min, float max, bool enabled = true)
        {
            pulseSpeed = speed;
            minScale = min;
            maxScale = max;
            EnablePulse = enabled;
        }

        private void Awake()
        {
            SaveScale();
        }

        private void OnEnable()
        {
            SaveScale();
            if (enablePulse)
            {
                StartPulse();
            }
        }

        private void OnDisable()
        {
            StopPulse();
        }

        private void SaveScale()
        {
            if (!hasSavedScale)
            {
                originalScale = transform.localScale;
                if (originalScale == Vector3.zero) originalScale = Vector3.one;
                hasSavedScale = true;
            }
        }

        public void StartPulse()
        {
            StopPulse();
            if (gameObject.activeInHierarchy)
            {
                pulseRoutine = StartCoroutine(PulseRoutine());
            }
        }

        public void StopPulse()
        {
            if (pulseRoutine != null)
            {
                StopCoroutine(pulseRoutine);
                pulseRoutine = null;
            }
            if (hasSavedScale)
            {
                transform.localScale = originalScale;
            }
        }

        private IEnumerator PulseRoutine()
        {
            while (enablePulse)
            {
                float time = useUnscaledTime ? Time.unscaledTime : Time.time;
                float wave = Mathf.Sin(time * pulseSpeed);
                float smoothT = (wave + 1f) * 0.5f;
                // SmoothStep provides gentle organic easing at peaks and troughs
                smoothT = smoothT * smoothT * (3f - 2f * smoothT);

                float multiplier = Mathf.Lerp(minScale, maxScale, smoothT);
                transform.localScale = originalScale * multiplier;

                yield return null;
            }

            if (hasSavedScale)
            {
                transform.localScale = originalScale;
            }
            pulseRoutine = null;
        }
    }
}
