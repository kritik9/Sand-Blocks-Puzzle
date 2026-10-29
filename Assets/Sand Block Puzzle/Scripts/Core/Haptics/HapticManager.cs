using System;
using System.Collections;
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace SandBlocks.Core.Haptics
{
    /// <summary>
    /// High-performance, low-latency cross-platform haptic feedback manager.
    /// Provides subtle, crisp micro-haptics with adjustable intensity and duration.
    /// Features hardware-level effects (iOS Taptic Engine and Android API 29+ Predefined effects)
    /// and micro-throttling to eliminate motor lag, buzzing, or frame drops.
    /// </summary>
    public class HapticManager : MonoBehaviour
    {
        private static HapticManager instance;
        public static HapticManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<HapticManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("[HapticManager]");
                        instance = go.AddComponent<HapticManager>();
                        if (Application.isPlaying)
                        {
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return instance;
            }
        }

        [Header("Global Settings")]
        [Tooltip("Global toggle for all vibration and haptic feedback")]
        [SerializeField] private bool enableHaptics = true;

        [Tooltip("Global intensity multiplier (0.1 = very soft/light, 1.0 = standard)")]
        [Range(0.05f, 1.0f)]
        [SerializeField] private float intensityMultiplier = 0.45f;

        [Tooltip("Global duration multiplier (0.1 = micro-tick, 1.0 = standard)")]
        [Range(0.05f, 1.0f)]
        [SerializeField] private float durationMultiplier = 0.5f;

        [Tooltip("Minimum time in seconds between consecutive haptics to prevent motor buzz buildup")]
        [Range(0.01f, 0.2f)]
        [SerializeField] private float minHapticInterval = 0.065f;

        [Header("Base Durations (Milliseconds)")]
        [SerializeField] private long selectionDurationMs = 6;
        [SerializeField] private long lightDurationMs = 8;
        [SerializeField] private long mediumDurationMs = 14;
        [SerializeField] private long heavyDurationMs = 20;

        [Header("Base Amplitudes (1 - 255)")]
        [Range(1, 255)] [SerializeField] private int selectionAmplitude = 25;
        [Range(1, 255)] [SerializeField] private int lightAmplitude = 35;
        [Range(1, 255)] [SerializeField] private int mediumAmplitude = 60;
        [Range(1, 255)] [SerializeField] private int heavyAmplitude = 90;

        [Header("Debug")]
        [Tooltip("Log haptic triggers in Unity Editor for debugging")]
        [SerializeField] private bool debugLogsInEditor = false;

        private float lastHapticTime = -1f;

        // Android predefined VibrationEffect constants
        private const int EFFECT_CLICK = 0;
        private const int EFFECT_DOUBLE_CLICK = 1;
        private const int EFFECT_TICK = 2;
        private const int EFFECT_THUD = 3;
        private const int EFFECT_POP = 4;
        private const int EFFECT_HEAVY_CLICK = 5;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject vibrator;
        private static AndroidJavaClass vibrationEffectClass;
        private static int apiLevel = -1;
        private static bool isAndroidInitialized = false;
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _UnityHapticImpactLight();

        [DllImport("__Internal")]
        private static extern void _UnityHapticImpactMedium();

        [DllImport("__Internal")]
        private static extern void _UnityHapticImpactHeavy();

        [DllImport("__Internal")]
        private static extern void _UnityHapticSelection();

        [DllImport("__Internal")]
        private static extern void _UnityHapticSuccess();
#endif

        public bool EnableHaptics
        {
            get => enableHaptics;
            set => enableHaptics = value;
        }

        public float IntensityMultiplier
        {
            get => intensityMultiplier;
            set => intensityMultiplier = Mathf.Clamp(value, 0.05f, 1.0f);
        }

        public float DurationMultiplier
        {
            get => durationMultiplier;
            set => durationMultiplier = Mathf.Clamp(value, 0.05f, 1.0f);
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                InitPlatform();
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void InitPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (isAndroidInitialized) return;
            isAndroidInitialized = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    apiLevel = version.GetStatic<int>("SDK_INT");
                }
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
                if (apiLevel >= 26)
                {
                    vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HapticManager] Android JNI vibrator initialization failed: {ex.Message}");
            }
#endif
        }

        /// <summary>
        /// Enables or disables haptics at runtime.
        /// </summary>
        public void SetHapticsEnabled(bool enabled)
        {
            enableHaptics = enabled;
        }

        /// <summary>
        /// Ultra-short, gentle selection tick.
        /// Ideal for bus touch/select, button taps, and fine touch interactions.
        /// </summary>
        public void Selection()
        {
            if (!CanTrigger(minHapticInterval)) return;

#if UNITY_IOS && !UNITY_EDITOR
            _UnityHapticSelection();
#else
            TriggerHaptic(selectionDurationMs, selectionAmplitude, EFFECT_TICK, "Selection");
#endif
        }

        /// <summary>
        /// Crisp, subtle tactile click with minimal duration.
        /// Ideal for cell drag steps, passenger boarding ticks, and button taps.
        /// </summary>
        public void LightImpact()
        {
            if (!CanTrigger(minHapticInterval)) return;

#if UNITY_IOS && !UNITY_EDITOR
            _UnityHapticImpactLight();
#else
            TriggerHaptic(lightDurationMs, lightAmplitude, EFFECT_TICK, "LightImpact");
#endif
        }

        /// <summary>
        /// Mild, controlled tactile tap.
        /// Ideal for placing a bus on parking lot, bus departure, or passenger drop-off.
        /// </summary>
        public void MediumImpact()
        {
            if (!CanTrigger(minHapticInterval)) return;

#if UNITY_IOS && !UNITY_EDITOR
            _UnityHapticImpactMedium();
#else
            TriggerHaptic(mediumDurationMs, mediumAmplitude, EFFECT_CLICK, "MediumImpact");
#endif
        }

        /// <summary>
        /// Short, firm feedback for invalid actions or blocked moves without prolonged motor vibration.
        /// </summary>
        public void HeavyImpact()
        {
            if (!CanTrigger(minHapticInterval * 0.8f)) return;

#if UNITY_IOS && !UNITY_EDITOR
            _UnityHapticImpactHeavy();
#else
            TriggerHaptic(heavyDurationMs, heavyAmplitude, EFFECT_POP, "HeavyImpact");
#endif
        }

        /// <summary>
        /// Soft, subtle double-pulse haptic pattern for level completion.
        /// </summary>
        public void Success()
        {
            if (!enableHaptics) return;

#if UNITY_IOS && !UNITY_EDITOR
            _UnityHapticSuccess();
#else
            StartCoroutine(SuccessRoutine());
#endif
        }

        private IEnumerator SuccessRoutine()
        {
            TriggerHaptic(10, 45, EFFECT_CLICK, "Success_1");
            yield return new WaitForSecondsRealtime(0.08f);
            TriggerHaptic(14, 65, EFFECT_CLICK, "Success_2");
        }

        private bool CanTrigger(float minInterval)
        {
            if (!enableHaptics) return false;
            float now = Time.realtimeSinceStartup;
            if (now - lastHapticTime < minInterval) return false;
            lastHapticTime = now;
            return true;
        }

        private void TriggerHaptic(long baseMs, int baseAmp, int predefinedEffectId, string debugLabel)
        {
            if (!enableHaptics) return;

            long scaledMs = Math.Max(2, (long)Math.Round(baseMs * durationMultiplier));
            int scaledAmp = Mathf.Clamp((int)Math.Round(baseAmp * intensityMultiplier), 1, 255);

#if UNITY_EDITOR
            if (debugLogsInEditor)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> {debugLabel} ({scaledMs}ms, amp: {scaledAmp}, effectId: {predefinedEffectId})");
            }
#elif UNITY_ANDROID
            if (vibrator == null) return;

            try
            {
                // Prefer modern Android 10+ (API 29+) hardware-calibrated system effects (TICK, CLICK, POP)
                if (apiLevel >= 29 && predefinedEffectId >= 0 && vibrationEffectClass != null)
                {
                    try
                    {
                        using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createPredefined", predefinedEffectId))
                        {
                            vibrator.Call("vibrate", effect);
                            return;
                        }
                    }
                    catch
                    {
                        // Fallback if specific effect is not implemented on custom vendor ROM
                    }
                }

                // Android 8.0+ (API 26+) amplitude and duration controlled single pulse
                if (apiLevel >= 26 && vibrationEffectClass != null)
                {
                    using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", scaledMs, scaledAmp))
                    {
                        vibrator.Call("vibrate", effect);
                        return;
                    }
                }

                // Fallback for older Android: short duration pulse only (never call full Handheld.Vibrate)
                vibrator.Call("vibrate", scaledMs);
            }
            catch (Exception)
            {
                // Silently ignore to avoid motor rumble or exception noise
            }
#elif UNITY_IOS
            // Handled via native HapticBridge
#else
            // Silent fallback on non-mobile platforms
#endif
        }
    }
}