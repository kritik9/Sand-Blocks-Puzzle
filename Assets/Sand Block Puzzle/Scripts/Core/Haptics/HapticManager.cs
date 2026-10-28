using System;
using System.Collections;
using UnityEngine;

namespace SandBlocks.Core.Haptics
{
    /// <summary>
    /// High-performance, low-latency cross-platform haptic feedback manager.
    /// Features micro-throttling to prevent motor overload or frame drops during rapid gameplay streams.
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

        [Header("Configuration")]
        [Tooltip("Global toggle for all vibration and haptic feedback")]
        [SerializeField] private bool enableHaptics = true;

        [Tooltip("Minimum time in seconds between consecutive haptic calls to prevent motor lag / battery drain")]
        [Range(0.01f, 0.2f)]
        [SerializeField] private float minHapticInterval = 0.045f;

        [Tooltip("Log haptic triggers in Unity Editor for debugging")]
        [SerializeField] private bool debugLogsInEditor = false;

        private float lastHapticTime = -1f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject vibrator;
        private static AndroidJavaClass vibrationEffectClass;
        private static int apiLevel = -1;
        private static bool isAndroidInitialized = false;
#endif

        public bool EnableHaptics
        {
            get => enableHaptics;
            set => enableHaptics = value;
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
        /// Crisp, subtle tactile click (15ms, soft amplitude).
        /// Ideal for cell drag steps, passenger boarding ticks, and button taps.
        /// </summary>
        public void LightImpact()
        {
            if (!CanTrigger(minHapticInterval)) return;
            TriggerHaptic(15, 75, "LightImpact");
        }

        /// <summary>
        /// Firm tactile tap (35ms, medium amplitude).
        /// Ideal for placing a vehicle, bus full state, and passenger drop-off.
        /// </summary>
        public void MediumImpact()
        {
            if (!CanTrigger(minHapticInterval)) return;
            TriggerHaptic(35, 160, "MediumImpact");
        }

        /// <summary>
        /// Strong tactile thump (65ms, high amplitude).
        /// Ideal for vehicle blocked collisions, shaking obstacles, or invalid moves.
        /// </summary>
        public void HeavyImpact()
        {
            if (!CanTrigger(minHapticInterval * 0.8f)) return;
            TriggerHaptic(65, 255, "HeavyImpact");
        }

        /// <summary>
        /// Quick selection tick (10ms, gentle amplitude).
        /// Ideal for picking up or touching a bus on pointer down.
        /// </summary>
        public void Selection()
        {
            if (!CanTrigger(minHapticInterval)) return;
            TriggerHaptic(10, 50, "Selection");
        }

        /// <summary>
        /// Rewarding double-pulse haptic pattern for level completion or bus departure.
        /// </summary>
        public void Success()
        {
            if (!enableHaptics) return;
            StartCoroutine(SuccessRoutine());
        }

        private IEnumerator SuccessRoutine()
        {
            TriggerHaptic(25, 140, "Success_1");
            yield return new WaitForSecondsRealtime(0.06f);
            TriggerHaptic(45, 220, "Success_2");
        }

        private bool CanTrigger(float minInterval)
        {
            if (!enableHaptics) return false;
            float now = Time.realtimeSinceStartup;
            if (now - lastHapticTime < minInterval) return false;
            lastHapticTime = now;
            return true;
        }

        private void TriggerHaptic(long milliseconds, int amplitude, string debugLabel)
        {
            if (!enableHaptics) return;

#if UNITY_EDITOR
            if (debugLogsInEditor)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> {debugLabel} ({milliseconds}ms, amp: {amplitude})");
            }
#elif UNITY_ANDROID
            try
            {
                if (apiLevel >= 26 && vibrationEffectClass != null && vibrator != null)
                {
                    amplitude = Mathf.Clamp(amplitude, 1, 255);
                    using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude))
                    {
                        vibrator.Call("vibrate", effect);
                    }
                }
                else if (vibrator != null)
                {
                    vibrator.Call("vibrate", milliseconds);
                }
                else
                {
                    Handheld.Vibrate();
                }
            }
            catch
            {
                Handheld.Vibrate();
            }
#elif UNITY_IOS
            Handheld.Vibrate();
#else
            // Fallback for standalone / other platforms
            Handheld.Vibrate();
#endif
        }
    }
}
