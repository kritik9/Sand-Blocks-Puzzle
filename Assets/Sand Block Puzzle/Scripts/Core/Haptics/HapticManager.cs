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
    /// Supports Android 12+ (VibratorManager), Android 10+ (VibrationEffect predefined),
    /// Android 8+ (createOneShot amplitude control), and iOS Taptic Engine (UIFeedbackGenerator).
    /// Features fallback to Handheld.Vibrate() to ensure 100% device compatibility.
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

        [Tooltip("Global intensity multiplier (0.5 = soft, 1.0 = standard, 1.5 = firm)")]
        [Range(0.1f, 2.0f)]
        [SerializeField] private float intensityMultiplier = 1.0f;

        [Tooltip("Global duration multiplier (0.5 = shorter tick, 1.0 = standard, 1.5 = longer buzz)")]
        [Range(0.1f, 2.0f)]
        [SerializeField] private float durationMultiplier = 1.0f;

        [Tooltip("Minimum time in seconds between consecutive haptics to prevent motor buzz buildup")]
        [Range(0.01f, 0.2f)]
        [SerializeField] private float minHapticInterval = 0.045f;

        [Tooltip("If native JNI/Taptic vibration fails or is unsupported on the device, fall back to Unity Handheld.Vibrate()")]
        [SerializeField] private bool fallbackToHandheldVibrate = true;

        [Header("Base Durations (Milliseconds - Min 12ms for physical motor spin-up)")]
        [SerializeField] private long selectionDurationMs = 18;
        [SerializeField] private long lightDurationMs = 25;
        [SerializeField] private long mediumDurationMs = 45;
        [SerializeField] private long heavyDurationMs = 75;

        [Header("Base Amplitudes (1 - 255)")]
        [Range(1, 255)] [SerializeField] private int selectionAmplitude = 120;
        [Range(1, 255)] [SerializeField] private int lightAmplitude = 160;
        [Range(1, 255)] [SerializeField] private int mediumAmplitude = 210;
        [Range(1, 255)] [SerializeField] private int heavyAmplitude = 255;

        [Header("Debug")]
        [Tooltip("Log haptic triggers in Unity Editor / Device logs for debugging")]
        [SerializeField] private bool debugLogs = false;

        private float lastHapticTime = -1f;

        // Android predefined VibrationEffect constants (API 29+)
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
        private static bool hasVibratorHardware = true;
        private static bool hasAmplitudeControl = false;
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
            set => intensityMultiplier = Mathf.Clamp(value, 0.1f, 2.0f);
        }

        public float DurationMultiplier
        {
            get => durationMultiplier;
            set => durationMultiplier = Mathf.Clamp(value, 0.1f, 2.0f);
        }

        public bool FallbackToHandheldVibrate
        {
            get => fallbackToHandheldVibrate;
            set => fallbackToHandheldVibrate = value;
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

        private void OnValidate()
        {
            // Auto-upgrade legacy micro-durations that are physically below mobile motor threshold
            if (selectionDurationMs < 12) selectionDurationMs = 18;
            if (lightDurationMs < 15) lightDurationMs = 25;
            if (mediumDurationMs < 25) mediumDurationMs = 45;
            if (heavyDurationMs < 40) heavyDurationMs = 75;
            if (selectionAmplitude < 60) selectionAmplitude = 120;
            if (lightAmplitude < 80) lightAmplitude = 160;
            if (mediumAmplitude < 120) mediumAmplitude = 210;
            if (heavyAmplitude < 180) heavyAmplitude = 255;
            if (intensityMultiplier < 0.6f) intensityMultiplier = 1.0f;
            if (durationMultiplier < 0.6f) durationMultiplier = 1.0f;
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
                    // Android 12+ (API 31+) uses VibratorManager to get the default system vibrator
                    if (apiLevel >= 31)
                    {
                        try
                        {
                            using (var vibratorManager = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                            {
                                if (vibratorManager != null)
                                {
                                    vibrator = vibratorManager.Call<AndroidJavaObject>("getDefaultVibrator");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            if (debugLogs) Debug.LogWarning($"[HapticManager] VibratorManager lookup fallback: {ex.Message}");
                        }
                    }

                    // Fallback for API < 31 or if VibratorManager returned null
                    if (vibrator == null)
                    {
                        vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    }
                }

                if (vibrator != null)
                {
                    try
                    {
                        hasVibratorHardware = vibrator.Call<bool>("hasVibrator");
                    }
                    catch
                    {
                        hasVibratorHardware = true;
                    }

                    if (apiLevel >= 26)
                    {
                        try
                        {
                            vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                            hasAmplitudeControl = vibrator.Call<bool>("hasAmplitudeControl");
                        }
                        catch
                        {
                            hasAmplitudeControl = false;
                        }
                    }

                    if (debugLogs)
                    {
                        Debug.Log($"<color=green>[HapticManager]</color> Android Initialized - API: {apiLevel}, HasVibrator: {hasVibratorHardware}, HasAmplitudeControl: {hasAmplitudeControl}");
                    }
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
        [ContextMenu("Test Selection")]
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
        /// Crisp tactile click.
        /// Ideal for cell drag steps, passenger boarding ticks, and button taps.
        /// </summary>
        [ContextMenu("Test Light Impact")]
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
        [ContextMenu("Test Medium Impact")]
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
        /// Short, firm feedback for invalid actions or blocked moves.
        /// </summary>
        [ContextMenu("Test Heavy Impact")]
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
        /// Subtle double-pulse haptic pattern for level completion.
        /// </summary>
        [ContextMenu("Test Success")]
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
            TriggerHaptic(20, 160, EFFECT_CLICK, "Success_1");
            yield return new WaitForSecondsRealtime(0.09f);
            TriggerHaptic(35, 230, EFFECT_HEAVY_CLICK, "Success_2");
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

            long scaledMs = Math.Max(12, (long)Math.Round(baseMs * durationMultiplier));
            int scaledAmp = Mathf.Clamp((int)Math.Round(baseAmp * intensityMultiplier), 1, 255);

#if UNITY_EDITOR
            if (debugLogs)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> {debugLabel} ({scaledMs}ms, amp: {scaledAmp}, effectId: {predefinedEffectId})");
            }
#elif UNITY_ANDROID
            if (vibrator == null || !hasVibratorHardware)
            {
                if (fallbackToHandheldVibrate) Handheld.Vibrate();
                return;
            }

            bool triggered = false;

            // Tier 1: Modern Android 10+ (API 29+) hardware-calibrated system effects (TICK, CLICK, POP)
            if (apiLevel >= 29 && predefinedEffectId >= 0 && vibrationEffectClass != null)
            {
                try
                {
                    using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createPredefined", predefinedEffectId))
                    {
                        if (effect != null)
                        {
                            vibrator.Call("vibrate", effect);
                            triggered = true;
                        }
                    }
                }
                catch
                {
                    // Fallback to createOneShot if vendor HAL has no predefined waveform
                }
            }

            // Tier 2: Android 8.0+ (API 26+) amplitude & duration controlled single pulse
            if (!triggered && apiLevel >= 26 && vibrationEffectClass != null)
            {
                if (hasAmplitudeControl)
                {
                    try
                    {
                        using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", scaledMs, scaledAmp))
                        {
                            if (effect != null)
                            {
                                vibrator.Call("vibrate", effect);
                                triggered = true;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback below
                    }
                }

                // Tier 2b: If device lacks amplitude control, use DEFAULT_AMPLITUDE (-1)
                if (!triggered)
                {
                    try
                    {
                        using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", scaledMs, -1))
                        {
                            if (effect != null)
                            {
                                vibrator.Call("vibrate", effect);
                                triggered = true;
                            }
                        }
                    }
                    catch
                    {
                        // Fallback below
                    }
                }
            }

            // Tier 3: Legacy vibrator single pulse
            if (!triggered)
            {
                try
                {
                    vibrator.Call("vibrate", scaledMs);
                    triggered = true;
                }
                catch
                {
                    // Fall through to Handheld.Vibrate
                }
            }

            // Tier 4: Universal fallback
            if (!triggered && fallbackToHandheldVibrate)
            {
                Handheld.Vibrate();
            }

            if (debugLogs)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> Android {debugLabel} Triggered: {triggered} ({scaledMs}ms)");
            }
#elif UNITY_IOS
            // Handled via native HapticBridge
#else
            // Fallback for any other platforms
            if (fallbackToHandheldVibrate)
            {
                Handheld.Vibrate();
            }
#endif
        }
    }
}
