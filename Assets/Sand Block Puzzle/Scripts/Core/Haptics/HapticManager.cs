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
    /// Features foolproof fallback to Handheld.Vibrate() to ensure 100% device compatibility.
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

        [Tooltip("Global intensity multiplier (1.0 = standard, 0.5 = soft, 1.5 = strong)")]
        [Range(0.10f, 2.0f)]
        [SerializeField] private float intensityMultiplier = 1.0f;

        [Tooltip("Global duration multiplier (1.0 = standard, 0.5 = short, 1.5 = long)")]
        [Range(0.10f, 2.0f)]
        [SerializeField] private float durationMultiplier = 1.0f;

        [Tooltip("Minimum time in seconds between consecutive haptics to prevent motor buzz buildup")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float minHapticInterval = 0.08f;

        [Tooltip("If true, uses Android system-predefined waveforms (Tick, Click, Pop). If false, uses custom amplitude/duration pulses.")]
        [SerializeField] private bool useSystemPredefinedHaptics = false;

        [Tooltip("If native JNI vibration fails, fall back to Unity Handheld.Vibrate() to guarantee vibration on all devices")]
        [SerializeField] private bool fallbackToHandheldVibrate = true;

        [Header("Granular Action Toggles (Enable/Disable by Action)")]
        [Tooltip("Haptic on UI buttons (Play button on home screen, Restart, Next/Prev Level buttons)")]
        [SerializeField] private bool hapticsOnUIButtons = true;

        [Tooltip("Haptic when player touches/selects a vehicle to drag")]
        [SerializeField] private bool hapticsOnVehicleSelect = true;

        [Tooltip("Haptic when vehicle crosses grid cells while dragging")]
        [SerializeField] private bool hapticsOnVehicleDragStep = false;

        [Tooltip("Haptic when vehicle is placed successfully in a grid slot")]
        [SerializeField] private bool hapticsOnVehiclePlace = true;

        [Tooltip("Haptic when vehicle drop is blocked / invalid move")]
        [SerializeField] private bool hapticsOnVehicleBlocked = true;

        [Tooltip("Haptic when a passenger boards the bus")]
        [SerializeField] private bool hapticsOnPassengerBoard = true;

        [Header("Rapid Crowd Boarding Throttling")]
        [Tooltip("If true, throttles haptics during rapid/instantaneous crowd boarding to produce a rhythmic 2-3 vibrations per second instead of buzzing on every passenger.")]
        [SerializeField] private bool throttleRapidBoardingHaptics = true;

        [Tooltip("Minimum time (in seconds) between vibrations when crowd is boarding rapidly/instantaneously. E.g. 0.38s produces ~2-3 vibrations per second when collecting 5-10 passengers/sec.")]
        [Range(0.15f, 1.0f)]
        [SerializeField] private float rapidBoardingHapticInterval = 0.38f;

        [Tooltip("Time threshold (in seconds) to consider boarding events part of a rapid/instantaneous group. If passengers board within this interval of each other, rapid throttling applies.")]
        [Range(0.10f, 1.0f)]
        [SerializeField] private float rapidBoardingDetectionWindow = 0.45f;

        [Tooltip("Haptic when a bus departs (drives away)")]
        [SerializeField] private bool hapticsOnBusDepart = true;

        [Tooltip("Haptic on level completion / victory celebration")]
        [SerializeField] private bool hapticsOnLevelWin = true;

        [Header("Base Durations (Milliseconds - Calibrated for Physical Motor Inertia)")]
        [SerializeField] private long selectionDurationMs = 25;
        [SerializeField] private long lightDurationMs = 35;
        [SerializeField] private long mediumDurationMs = 55;
        [SerializeField] private long heavyDurationMs = 90;

        [Header("Base Amplitudes (1 - 255)")]
        [Range(1, 255)] [SerializeField] private int selectionAmplitude = 120;
        [Range(1, 255)] [SerializeField] private int lightAmplitude = 160;
        [Range(1, 255)] [SerializeField] private int mediumAmplitude = 210;
        [Range(1, 255)] [SerializeField] private int heavyAmplitude = 255;

        [Header("Debug")]
        [Tooltip("Log haptic triggers in Unity Editor / Device logs for debugging")]
        [SerializeField] private bool debugLogs = false;

        private float lastHapticTime = -1f;
        private float lastPassengerBoardCallTime = -10f;
        private float lastPassengerBoardHapticTime = -10f;

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
            set => intensityMultiplier = Mathf.Clamp(value, 0.10f, 2.0f);
        }

        public float DurationMultiplier
        {
            get => durationMultiplier;
            set => durationMultiplier = Mathf.Clamp(value, 0.10f, 2.0f);
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

        public bool ThrottleRapidBoardingHaptics
        {
            get => throttleRapidBoardingHaptics;
            set => throttleRapidBoardingHaptics = value;
        }

        public float RapidBoardingHapticInterval
        {
            get => rapidBoardingHapticInterval;
            set => rapidBoardingHapticInterval = Mathf.Clamp(value, 0.10f, 2.0f);
        }

        public float RapidBoardingDetectionWindow
        {
            get => rapidBoardingDetectionWindow;
            set => rapidBoardingDetectionWindow = Mathf.Clamp(value, 0.05f, 2.0f);
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

        private const string PREF_KEY_VERSION = "Haptics_Pref_Version_v3";
        private const int CURRENT_PREF_VERSION = 3;
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
            PlayerPrefs.SetInt(PREF_KEY_VERSION, CURRENT_PREF_VERSION);
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
        /// Automatically migrates older/sub-threshold preferences to solid perceptible defaults.
        /// </summary>
        public void LoadPreferences()
        {
            int savedVersion = PlayerPrefs.GetInt(PREF_KEY_VERSION, 0);
            if (savedVersion < CURRENT_PREF_VERSION)
            {
                // Automatic migration for devices that had old sub-threshold values
                enableHaptics = true;
                intensityMultiplier = 1.0f;
                durationMultiplier = 1.0f;
                lightAmplitude = 160;
                selectionAmplitude = 120;
                fallbackToHandheldVibrate = true;
                hapticsOnUIButtons = true;
                hapticsOnVehicleDragStep = false;
                SavePreferences();
                return;
            }

            if (PlayerPrefs.HasKey(PREF_KEY_HAPTICS_ENABLED))
                enableHaptics = (PlayerPrefs.GetInt(PREF_KEY_HAPTICS_ENABLED) == 1);
            if (PlayerPrefs.HasKey(PREF_KEY_INTENSITY))
                intensityMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(PREF_KEY_INTENSITY), 0.10f, 2.0f);
            if (PlayerPrefs.HasKey(PREF_KEY_DURATION))
                durationMultiplier = Mathf.Clamp(PlayerPrefs.GetFloat(PREF_KEY_DURATION), 0.10f, 2.0f);
            if (PlayerPrefs.HasKey(PREF_KEY_LIGHT_AMP))
                lightAmplitude = Mathf.Clamp(PlayerPrefs.GetInt(PREF_KEY_LIGHT_AMP), 50, 255);
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
            intensityMultiplier = Mathf.Clamp(intensityMultiplier, 0.10f, 2.0f);
            durationMultiplier = Mathf.Clamp(durationMultiplier, 0.10f, 2.0f);
            minHapticInterval = Mathf.Clamp(minHapticInterval, 0.01f, 0.5f);
            selectionDurationMs = Math.Max(15, selectionDurationMs);
            lightDurationMs = Math.Max(20, lightDurationMs);
            mediumDurationMs = Math.Max(30, mediumDurationMs);
            heavyDurationMs = Math.Max(40, heavyDurationMs);
            selectionAmplitude = Mathf.Clamp(selectionAmplitude, 40, 255);
            lightAmplitude = Mathf.Clamp(lightAmplitude, 50, 255);
            mediumAmplitude = Mathf.Clamp(mediumAmplitude, 60, 255);
            heavyAmplitude = Mathf.Clamp(heavyAmplitude, 80, 255);
        }

        private static void EnsurePlatformInitialized()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (vibrator == null)
            {
                InitPlatform();
            }
#endif
        }

        private static void InitPlatform()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (apiLevel < 0)
                {
                    using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        apiLevel = version.GetStatic<int>("SDK_INT");
                    }
                }

                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    if (currentActivity != null)
                    {
                        // 1. Context.VIBRATOR_SERVICE - universally available across ALL Android versions
                        try
                        {
                            vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[HapticManager] getSystemService('vibrator') exception: {ex.Message}");
                        }

                        // 2. Fallback on Android 12+ (API 31+) if vibrator is null
                        if (vibrator == null && apiLevel >= 31)
                        {
                            try
                            {
                                using (var vm = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator_manager"))
                                {
                                    if (vm != null)
                                    {
                                        vibrator = vm.Call<AndroidJavaObject>("getDefaultVibrator");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[HapticManager] VibratorManager exception: {ex.Message}");
                            }
                        }
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

                    Debug.Log($"<color=green>[HapticManager]</color> Android Initialized - API: {apiLevel}, HasVibrator: {hasVibratorHardware}, HasAmplitudeControl: {hasAmplitudeControl}");
                }
                else
                {
                    Debug.LogWarning("[HapticManager] Android vibrator service returned null; Handheld.Vibrate() fallback is active.");
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
        // Action-Specific Trigger Methods
        // ==========================================

        /// <summary>
        /// Triggered on UI buttons (Play, Restart, Next/Previous level).
        /// </summary>
        public void TriggerUIButton()
        {
            if (!hapticsOnUIButtons) return;
            LightImpact();
        }

        /// <summary>
        /// Triggered when the player touches / selects a vehicle to drag.
        /// </summary>
        public void TriggerVehicleSelect()
        {
            if (!hapticsOnVehicleSelect) return;
            Selection();
        }

        /// <summary>
        /// Triggered when dragging a vehicle across grid cells.
        /// </summary>
        public void TriggerVehicleDragStep()
        {
            if (!hapticsOnVehicleDragStep) return;
            LightImpact();
        }

        /// <summary>
        /// Triggered when a vehicle is successfully placed into a valid grid slot.
        /// </summary>
        public void TriggerVehiclePlace()
        {
            if (!hapticsOnVehiclePlace) return;
            MediumImpact();
        }

        /// <summary>
        /// Triggered when an attempted vehicle move or drop is invalid / blocked.
        /// </summary>
        public void TriggerVehicleBlocked()
        {
            if (!hapticsOnVehicleBlocked) return;
            HeavyImpact();
        }

        /// <summary>
        /// Triggered when a passenger boards the bus or reaches destination.
        /// When crowd enters instantaneously or in a rapid stream (e.g. 5+ passengers in 1 second),
        /// haptics are throttled to a clean, rhythmic 2-3 pulses per second.
        /// Isolated or slow boarding events vibrate normally for every passenger.
        /// </summary>
        public void TriggerPassengerBoard()
        {
            if (!hapticsOnPassengerBoard) return;

            float now = Time.realtimeSinceStartup;

            if (throttleRapidBoardingHaptics)
            {
                float timeSinceLastCall = now - lastPassengerBoardCallTime;
                lastPassengerBoardCallTime = now;

                // If crowd is boarding in rapid succession (within detection window)
                if (timeSinceLastCall <= rapidBoardingDetectionWindow)
                {
                    // Throttle: only trigger haptic if the rapid boarding interval has elapsed
                    if (now - lastPassengerBoardHapticTime < rapidBoardingHapticInterval)
                    {
                        return; // Skip this passenger's vibration during rapid/instantaneous collection
                    }
                }
            }

            lastPassengerBoardHapticTime = now;
            LightImpact();
        }

        /// <summary>
        /// Triggered when a bus becomes full and departs.
        /// </summary>
        public void TriggerBusDepart()
        {
            if (!hapticsOnBusDepart) return;
            MediumImpact();
        }

        /// <summary>
        /// Triggered on level completion / victory celebration.
        /// </summary>
        public void TriggerLevelWin()
        {
            if (!hapticsOnLevelWin) return;
            Success();
        }

        /// <summary>
        /// Explicit test haptic method called by Settings UI test button.
        /// Bypasses rate limits and delivers a crisp, unmistakable vibration pulse.
        /// </summary>
        public void TriggerTestHaptic()
        {
            if (!enableHaptics) return;

#if UNITY_IOS && !UNITY_EDITOR
            _UnityHapticImpactMedium();
#else
            TriggerHaptic(50, lightAmplitude, EFFECT_CLICK, "TestVibration", bypassCooldown: true);
#endif
        }

        // ==========================================
        // Base Primitive Haptic Methods
        // ==========================================

        /// <summary>
        /// Short, gentle selection tick.
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
        /// Firm feedback for invalid actions or blocked moves.
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
        /// Satisfying double-pulse haptic pattern for level completion.
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
            TriggerHaptic(35, 160, EFFECT_CLICK, "Success_1", bypassCooldown: true);
            yield return new WaitForSecondsRealtime(0.09f);
            TriggerHaptic(60, 255, EFFECT_HEAVY_CLICK, "Success_2", bypassCooldown: true);
        }

        private bool CanTrigger(float minInterval)
        {
            if (!enableHaptics) return false;
            float now = Time.realtimeSinceStartup;
            if (now - lastHapticTime < minInterval) return false;
            lastHapticTime = now;
            return true;
        }

        private void TriggerHaptic(long baseMs, int baseAmp, int predefinedEffectId, string debugLabel, bool bypassCooldown = false)
        {
            if (!enableHaptics) return;

            if (!bypassCooldown)
            {
                lastHapticTime = Time.realtimeSinceStartup;
            }

            // Enforce minimum motor spin-up inertia thresholds (at least 20ms and 50 amplitude)
            // This guarantees physical phone motors overcome rotor inertia and produce tactile feedback
            long scaledMs = Math.Max(20, (long)Math.Round(baseMs * durationMultiplier));
            int scaledAmp = Mathf.Clamp((int)Math.Round(baseAmp * intensityMultiplier), 50, 255);

#if UNITY_EDITOR
            if (debugLogs)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> {debugLabel} ({scaledMs}ms, amp: {scaledAmp}, effectId: {predefinedEffectId})");
            }
#elif UNITY_ANDROID
            EnsurePlatformInitialized();

            if (vibrator == null || !hasVibratorHardware)
            {
                if (fallbackToHandheldVibrate)
                {
                    Handheld.Vibrate();
                }
                return;
            }

            bool triggered = false;

            // Tier 1: Hardware predefined effects (Android 10+ / API 29+) if explicitly requested
            if (useSystemPredefinedHaptics && apiLevel >= 29 && predefinedEffectId >= 0 && vibrationEffectClass != null)
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
                    // Fall through to Tier 2
                }
            }

            // Tier 2: Amplitude & duration controlled single pulse (Android 8.0+ / API 26+)
            if (!triggered && apiLevel >= 26 && vibrationEffectClass != null && hasAmplitudeControl)
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
                    if (debugLogs) Debug.LogWarning($"[HapticManager] createOneShot with amplitude failed: {ex.Message}");
                }
            }

            // Tier 3: Duration pulse with system default amplitude (Android 8.0+ / API 26+)
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
                catch (Exception ex)
                {
                    if (debugLogs) Debug.LogWarning($"[HapticManager] createOneShot default failed: {ex.Message}");
                }
            }

            // Tier 4: Universal legacy vibrate (Android API 1+)
            if (!triggered)
            {
                try
                {
                    vibrator.Call("vibrate", scaledMs);
                    triggered = true;
                }
                catch (Exception ex)
                {
                    if (debugLogs) Debug.LogWarning($"[HapticManager] legacy vibrate failed: {ex.Message}");
                }
            }

            // Tier 5: Absolute guarantee fallback to Unity Handheld.Vibrate()
            if (!triggered && fallbackToHandheldVibrate)
            {
                Handheld.Vibrate();
                triggered = true;
            }

            if (debugLogs)
            {
                Debug.Log($"<color=orange>[HapticManager]</color> Android {debugLabel} Triggered: {triggered} ({scaledMs}ms, amp: {scaledAmp})");
            }
#elif UNITY_IOS
            // Handled via native HapticBridge
#else
            if (fallbackToHandheldVibrate)
            {
                Handheld.Vibrate();
            }
#endif
        }
    }
}
