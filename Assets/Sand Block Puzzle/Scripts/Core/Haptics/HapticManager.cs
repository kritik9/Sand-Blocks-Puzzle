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

        [Tooltip("Global intensity multiplier (0.1 = soft/subtle, 0.35 = gentle, 1.0 = standard)")]
        [Range(0.05f, 2.0f)]
        [SerializeField] private float intensityMultiplier = 0.35f;

        [Tooltip("Global duration multiplier (0.1 = crisp micro-tap, 0.4 = short tick, 1.0 = standard buzz)")]
        [Range(0.05f, 2.0f)]
        [SerializeField] private float durationMultiplier = 0.40f;

        [Tooltip("Minimum time in seconds between consecutive haptics to prevent motor buzz buildup")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float minHapticInterval = 0.08f;

        [Tooltip("If true, uses Android system-predefined waveforms (Tick, Click, Pop). If false, uses custom amplitude/duration pulses (recommended for soft, subtle tactile feel).")]
        [SerializeField] private bool useSystemPredefinedHaptics = false;

        [Tooltip("If native JNI/Taptic vibration fails, fall back to Unity Handheld.Vibrate() (Caution: Handheld.Vibrate is a fixed 500ms max rumble)")]
        [SerializeField] private bool fallbackToHandheldVibrate = false;

        [Header("Granular Action Toggles (Enable/Disable by Action)")]
        [Tooltip("Haptic on UI buttons (Play button on home screen, Restart, Next/Prev Level buttons). Disable to remove haptics from Play button!")]
        [SerializeField] private bool hapticsOnUIButtons = false;

        [Tooltip("Haptic when player touches/selects a vehicle to drag")]
        [SerializeField] private bool hapticsOnVehicleSelect = true;

        [Tooltip("Haptic when vehicle crosses grid cells while dragging. (Disabled by default to prevent high vibration frequency)")]
        [SerializeField] private bool hapticsOnVehicleDragStep = false;

        [Tooltip("Haptic when vehicle is placed successfully in a grid slot")]
        [SerializeField] private bool hapticsOnVehiclePlace = true;

        [Tooltip("Haptic when vehicle drop is blocked / invalid move")]
        [SerializeField] private bool hapticsOnVehicleBlocked = true;

        [Tooltip("Haptic when a passenger boards the bus")]
        [SerializeField] private bool hapticsOnPassengerBoard = true;

        [Tooltip("Haptic when a bus departs (drives away)")]
        [SerializeField] private bool hapticsOnBusDepart = true;

        [Tooltip("Haptic on level completion / victory celebration")]
        [SerializeField] private bool hapticsOnLevelWin = true;

        [Header("Base Durations (Milliseconds)")]
        [SerializeField] private long selectionDurationMs = 12;
        [SerializeField] private long lightDurationMs = 18;
        [SerializeField] private long mediumDurationMs = 30;
        [SerializeField] private long heavyDurationMs = 50;

        [Header("Base Amplitudes (1 - 255)")]
        [Range(1, 255)] [SerializeField] private int selectionAmplitude = 50;
        [Range(1, 255)] [SerializeField] private int lightAmplitude = 70;
        [Range(1, 255)] [SerializeField] private int mediumAmplitude = 110;
        [Range(1, 255)] [SerializeField] private int heavyAmplitude = 160;

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
            set => intensityMultiplier = Mathf.Clamp(value, 0.05f, 2.0f);
        }

        public float DurationMultiplier
        {
            get => durationMultiplier;
            set => durationMultiplier = Mathf.Clamp(value, 0.05f, 2.0f);
        }

        public bool FallbackToHandheldVibrate
        {
            get => fallbackToHandheldVibrate;
            set => fallbackToHandheldVibrate = value;
        }

        public bool UseSystemPredefinedHaptics
        {
            get => useSystemPredefinedHaptics;
            set => useSystemPredefinedHaptics = value;
        }

        public bool HapticsOnUIButtons
        {
            get => hapticsOnUIButtons;
            set => hapticsOnUIButtons = value;
        }

        public bool HapticsOnVehicleSelect
        {
            get => hapticsOnVehicleSelect;
            set => hapticsOnVehicleSelect = value;
        }

        public bool HapticsOnVehicleDragStep
        {
            get => hapticsOnVehicleDragStep;
            set => hapticsOnVehicleDragStep = value;
        }

        public bool HapticsOnVehiclePlace
        {
            get => hapticsOnVehiclePlace;
            set => hapticsOnVehiclePlace = value;
        }

        public bool HapticsOnVehicleBlocked
        {
            get => hapticsOnVehicleBlocked;
            set => hapticsOnVehicleBlocked = value;
        }

        public bool HapticsOnPassengerBoard
        {
            get => hapticsOnPassengerBoard;
            set => hapticsOnPassengerBoard = value;
        }

        public bool HapticsOnBusDepart
        {
            get => hapticsOnBusDepart;
            set => hapticsOnBusDepart = value;
        }

        public bool HapticsOnLevelWin
        {
            get => hapticsOnLevelWin;
            set => hapticsOnLevelWin = value;
        }

        public int SelectionAmplitude
        {
            get => selectionAmplitude;
            set => selectionAmplitude = Mathf.Clamp(value, 1, 255);
        }

        public int LightAmplitude
        {
            get => lightAmplitude;
            set => lightAmplitude = Mathf.Clamp(value, 1, 255);
        }

        public int MediumAmplitude
        {
            get => mediumAmplitude;
            set => mediumAmplitude = Mathf.Clamp(value, 1, 255);
        }

        public int HeavyAmplitude
        {
            get => heavyAmplitude;
            set => heavyAmplitude = Mathf.Clamp(value, 1, 255);
        }

        private const string PREF_KEY_HAPTICS_ENABLED = "Haptics_Enabled";
        private const string PREF_KEY_INTENSITY = "Haptics_Intensity";
        private const string PREF_KEY_DURATION = "Haptics_Duration";
        private const string PREF_KEY_LIGHT_AMP = "Haptics_LightAmplitude";
        private const string PREF_KEY_UI_BUTTONS = "Haptics_UIButtons";
        private const string PREF_KEY_DRAG_STEP = "Haptics_DragStep";

        /// <summary>
        /// Saves all haptic settings to PlayerPrefs so user preferences persist across sessions.
        /// </summary>
        public void SavePreferences()
        {
            PlayerPrefs.SetInt(PREF_KEY_HAPTICS_ENABLED, enableHaptics ? 1 : 0);
            PlayerPrefs.SetFloat(PREF_KEY_INTENSITY, intensityMultiplier);
            PlayerPrefs.SetFloat(PREF_KEY_DURATION, durationMultiplier);
            PlayerPrefs.SetInt(PREF_KEY_LIGHT_AMP, lightAmplitude);
            PlayerPrefs.SetInt(PREF_KEY_UI_BUTTONS, hapticsOnUIButtons ? 1 : 0);
            PlayerPrefs.SetInt(PREF_KEY_DRAG_STEP, hapticsOnVehicleDragStep ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Loads user-configured haptic preferences from PlayerPrefs.
        /// </summary>
        public void LoadPreferences()
        {
            if (PlayerPrefs.HasKey(PREF_KEY_HAPTICS_ENABLED))
                enableHaptics = (PlayerPrefs.GetInt(PREF_KEY_HAPTICS_ENABLED) == 1);
            if (PlayerPrefs.HasKey(PREF_KEY_INTENSITY))
                intensityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(PREF_KEY_INTENSITY), 0.05f, 2.0f);
            if (PlayerPrefs.HasKey(PREF_KEY_DURATION))
                durationMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(PREF_KEY_DURATION), 0.05f, 2.0f);
            if (PlayerPrefs.HasKey(PREF_KEY_LIGHT_AMP))
                lightAmplitude = Mathf.Clamp(PlayerPrefs.GetInt(PREF_KEY_LIGHT_AMP), 1, 255);
            if (PlayerPrefs.HasKey(PREF_KEY_UI_BUTTONS))
                hapticsOnUIButtons = (PlayerPrefs.GetInt(PREF_KEY_UI_BUTTONS) == 1);
            if (PlayerPrefs.HasKey(PREF_KEY_DRAG_STEP))
                hapticsOnVehicleDragStep = (PlayerPrefs.GetInt(PREF_KEY_DRAG_STEP) == 1);
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                LoadPreferences();
                InitPlatform();
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void OnValidate()
        {
            // Keep ranges safe without overriding user-set low/subtle values
            intensityMultiplier = Mathf.Clamp(intensityMultiplier, 0.05f, 2.0f);
            durationMultiplier = Mathf.Clamp(durationMultiplier, 0.05f, 2.0f);
            minHapticInterval = Mathf.Clamp(minHapticInterval, 0.01f, 0.5f);
            selectionDurationMs = Math.Max(1, selectionDurationMs);
            lightDurationMs = Math.Max(1, lightDurationMs);
            mediumDurationMs = Math.Max(1, mediumDurationMs);
            heavyDurationMs = Math.Max(1, heavyDurationMs);
            selectionAmplitude = Mathf.Clamp(selectionAmplitude, 1, 255);
            lightAmplitude = Mathf.Clamp(lightAmplitude, 1, 255);
            mediumAmplitude = Mathf.Clamp(mediumAmplitude, 1, 255);
            heavyAmplitude = Mathf.Clamp(heavyAmplitude, 1, 255);
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

        // ==========================================
        // Action-Specific Trigger Methods (Gated by Inspector Toggles)
        // ==========================================

        /// <summary>
        /// Triggered on UI buttons (Play, Restart, Next/Previous level).
        /// Respects the 'hapticsOnUIButtons' toggle (disabled by default so Play button has no vibration).
        /// </summary>
        public void TriggerUIButton()
        {
            if (!hapticsOnUIButtons) return;
            LightImpact();
        }

        /// <summary>
        /// Triggered when the player touches / selects a vehicle to drag.
        /// Respects the 'hapticsOnVehicleSelect' toggle.
        /// </summary>
        public void TriggerVehicleSelect()
        {
            if (!hapticsOnVehicleSelect) return;
            Selection();
        }

        /// <summary>
        /// Triggered when dragging a vehicle across grid cells.
        /// Respects the 'hapticsOnVehicleDragStep' toggle (disabled by default to prevent high frequency vibration).
        /// </summary>
        public void TriggerVehicleDragStep()
        {
            if (!hapticsOnVehicleDragStep) return;
            LightImpact();
        }

        /// <summary>
        /// Triggered when a vehicle is successfully placed into a valid grid slot.
        /// Respects the 'hapticsOnVehiclePlace' toggle.
        /// </summary>
        public void TriggerVehiclePlace()
        {
            if (!hapticsOnVehiclePlace) return;
            MediumImpact();
        }

        /// <summary>
        /// Triggered when an attempted vehicle move or drop is invalid / blocked.
        /// Respects the 'hapticsOnVehicleBlocked' toggle.
        /// </summary>
        public void TriggerVehicleBlocked()
        {
            if (!hapticsOnVehicleBlocked) return;
            HeavyImpact();
        }

        /// <summary>
        /// Triggered when a passenger boards the bus or reaches destination.
        /// Respects the 'hapticsOnPassengerBoard' toggle.
        /// </summary>
        public void TriggerPassengerBoard()
        {
            if (!hapticsOnPassengerBoard) return;
            LightImpact();
        }

        /// <summary>
        /// Triggered when a bus becomes full and departs.
        /// Respects the 'hapticsOnBusDepart' toggle.
        /// </summary>
        public void TriggerBusDepart()
        {
            if (!hapticsOnBusDepart) return;
            MediumImpact();
        }

        /// <summary>
        /// Triggered on level completion / victory celebration.
        /// Respects the 'hapticsOnLevelWin' toggle.
        /// </summary>
        public void TriggerLevelWin()
        {
            if (!hapticsOnLevelWin) return;
            Success();
        }

        // ==========================================
        // Base Primitive Haptic Methods
        // ==========================================

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
            TriggerHaptic(18, 120, EFFECT_CLICK, "Success_1");
            yield return new WaitForSecondsRealtime(0.09f);
            TriggerHaptic(30, 180, EFFECT_HEAVY_CLICK, "Success_2");
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

            long scaledMs = Math.Max(5, (long)Math.Round(baseMs * durationMultiplier));
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

            // Tier 1: Android 8.0+ (API 26+) amplitude & duration controlled single pulse.
            // When amplitude control is available and system predefined is not forced,
            // createOneShot gives 100% accurate adherence to the Intensity & Duration sliders!
            if (!useSystemPredefinedHaptics && apiLevel >= 26 && vibrationEffectClass != null && hasAmplitudeControl)
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
                catch (Exception ex)
                {
                    if (debugLogs) Debug.LogWarning($"[HapticManager] createOneShot failed: {ex.Message}");
                }
            }

            // Tier 2: Modern Android 10+ (API 29+) hardware-calibrated system effects (TICK, CLICK, POP)
            // Used when useSystemPredefinedHaptics is enabled or as fallback
            if (!triggered && apiLevel >= 29 && predefinedEffectId >= 0 && vibrationEffectClass != null)
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
                    // Fallback to createOneShot
                }
            }

            // Tier 3: Android 8.0+ (API 26+) createOneShot without amplitude control (DEFAULT_AMPLITUDE = -1)
            if (!triggered && apiLevel >= 26 && vibrationEffectClass != null)
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

            // Tier 4: Legacy vibrator single pulse (pre-API 26)
            if (!triggered)
            {
                try
                {
                    vibrator.Call("vibrate", scaledMs);
                    triggered = true;
                }
                catch
                {
                    // Fall through
                }
            }

            // Tier 5: Fallback ONLY if explicitly enabled by user
            if (!triggered && fallbackToHandheldVibrate)
            {
                Handheld.Vibrate();
            }

            if (debugLogs)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> Android {debugLabel} Triggered: {triggered} ({scaledMs}ms, amp: {scaledAmp})");
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
