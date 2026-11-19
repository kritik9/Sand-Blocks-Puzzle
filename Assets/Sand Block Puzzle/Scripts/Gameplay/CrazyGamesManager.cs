using System;
using UnityEngine;
using CrazyGames;

/// <summary>
/// Central manager for CrazyGames WebGL SDK integration.
/// Handles lifecycle tracking (GameplayStart / GameplayStop), celebrations (HappyTime),
/// midgame interstitial ads between levels with cooldown and level gating,
/// rewarded ads for in-game bonuses, and audio muting on background tab focus.
/// </summary>
public class CrazyGamesManager : MonoBehaviour
{
    public static CrazyGamesManager Instance { get; private set; }

    private static bool isApplicationQuitting = false;

    public bool IsInitialized => !isApplicationQuitting && !CrazySDK.IsShutDown && CrazySDK.IsInitialized;
    public bool IsAvailable => !isApplicationQuitting && CrazySDK.IsAvailable;

    [Header("Ad Control (CrazyGames)")]
    [Tooltip("Global toggle to enable or disable CrazyGames ads. Keep disabled (unchecked) for Basic Launch / ad-free release.")]
    [SerializeField] private bool enableAds = false;

    public bool EnableAds
    {
        get => enableAds;
        set => enableAds = value;
    }

    [Header("Ad Configuration (When Ads Enabled)")]
    [Tooltip("Minimum level index before midgame interstitial ads are allowed (e.g. 2 means ads only trigger after Level 1 is cleared)")]
    [SerializeField] private int minLevelForMidgameAds = 2;

    [Tooltip("Minimum interval in seconds between midgame ads to protect user experience and avoid spam")]
    [SerializeField] private float midgameAdCooldown = 90f;

    private float lastMidgameAdTime = -999f;
    private bool isAdShowing = false;

    public bool IsAdShowing => isAdShowing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        isApplicationQuitting = false;
        DontDestroyOnLoad(gameObject);

        InitSDK();
    }

    private void OnApplicationQuit()
    {
        isApplicationQuitting = true;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            isApplicationQuitting = true;
            Instance = null;
        }
    }

    private void InitSDK()
    {
        if (CrazySDK.IsAvailable)
        {
            CrazySDK.Init(() =>
            {
                Debug.Log("<color=cyan>[CrazyGamesManager]</color> CrazySDK Initialized Successfully!");
            });
        }
        else
        {
            Debug.Log("<color=yellow>[CrazyGamesManager]</color> CrazySDK is not active on this platform/domain.");
        }
    }

    /// <summary>
    /// Notifies CrazyGames that active gameplay has started or resumed.
    /// Call when a level is spawned and ready for player input.
    /// </summary>
    public void GameplayStart()
    {
        if (isApplicationQuitting || CrazySDK.IsShutDown) return;
        if (!CrazySDK.IsAvailable || !CrazySDK.IsInitialized) return;

        try
        {
            var game = CrazySDK.Game;
            if (game != null)
            {
                game.GameplayStart();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CrazyGamesManager] GameplayStart error: {ex.Message}");
        }
    }

    /// <summary>
    /// Notifies CrazyGames that active gameplay has stopped.
    /// Call when entering menus, pausing, restarting, or completing a level.
    /// </summary>
    public void GameplayStop()
    {
        if (isApplicationQuitting || CrazySDK.IsShutDown) return;
        if (!CrazySDK.IsAvailable || !CrazySDK.IsInitialized) return;

        try
        {
            var game = CrazySDK.Game;
            if (game != null)
            {
                game.GameplayStop();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CrazyGamesManager] GameplayStop error: {ex.Message}");
        }
    }

    /// <summary>
    /// Triggers the celebratory HappyTime visual effect on the CrazyGames platform.
    /// Call when a player beats a level or accomplishes a major milestone.
    /// </summary>
    public void HappyTime()
    {
        if (isApplicationQuitting || CrazySDK.IsShutDown) return;
        if (!CrazySDK.IsAvailable || !CrazySDK.IsInitialized) return;

        try
        {
            var game = CrazySDK.Game;
            if (game != null)
            {
                game.HappyTime();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CrazyGamesManager] HappyTime error: {ex.Message}");
        }
    }

    /// <summary>
    /// Requests a midgame interstitial advertisement.
    /// Automatically enforces level gating (no ad on Level 1) and cooldown intervals.
    /// If enableAds is false, immediately invokes onCompleted without showing any ad.
    /// </summary>
    public void ShowMidgameAd(int currentLevelNumber, Action onCompleted = null)
    {
        // 1. If ads are disabled globally (e.g. Basic Launch), bypass completely
        if (!enableAds)
        {
            onCompleted?.Invoke();
            return;
        }

        if (isApplicationQuitting || CrazySDK.IsShutDown || !CrazySDK.IsAvailable || !CrazySDK.IsInitialized)
        {
            onCompleted?.Invoke();
            return;
        }

        // 2. Do not show ads during the first level (onboarding experience)
        if (currentLevelNumber < minLevelForMidgameAds)
        {
            Debug.Log($"[CrazyGamesManager] Level {currentLevelNumber} is below minimum ad level ({minLevelForMidgameAds}). Skipping ad.");
            onCompleted?.Invoke();
            return;
        }

        // 3. Respect ad cooldown interval
        float timeSinceLastAd = Time.unscaledTime - lastMidgameAdTime;
        if (timeSinceLastAd < midgameAdCooldown)
        {
            Debug.Log($"[CrazyGamesManager] Midgame ad cooldown active ({timeSinceLastAd:F1}s / {midgameAdCooldown}s). Skipping ad.");
            onCompleted?.Invoke();
            return;
        }

        if (isAdShowing)
        {
            onCompleted?.Invoke();
            return;
        }

        isAdShowing = true;
        lastMidgameAdTime = Time.unscaledTime;
        GameplayStop();

        try
        {
            var ad = CrazySDK.Ad;
            if (ad == null)
            {
                isAdShowing = false;
                onCompleted?.Invoke();
                return;
            }

            ad.RequestAd(
                CrazyAdType.Midgame,
                adStarted: () =>
                {
                    Debug.Log("[CrazyGamesManager] Midgame Ad started.");
                },
                adError: (error) =>
                {
                    Debug.LogWarning($"[CrazyGamesManager] Midgame Ad error: {error}");
                    isAdShowing = false;
                    GameplayStart();
                    onCompleted?.Invoke();
                },
                adFinished: () =>
                {
                    Debug.Log("[CrazyGamesManager] Midgame Ad finished.");
                    isAdShowing = false;
                    GameplayStart();
                    onCompleted?.Invoke();
                }
            );
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CrazyGamesManager] Failed to request midgame ad: {ex.Message}");
            isAdShowing = false;
            onCompleted?.Invoke();
        }
    }

    /// <summary>
    /// Requests a rewarded video advertisement.
    /// Invokes onReward only when the user finishes watching the ad completely.
    /// Invokes onDismiss if the user cancels or an error occurs.
    /// </summary>
    public void ShowRewardedAd(Action onReward, Action onDismiss = null)
    {
        if (!enableAds)
        {
            Debug.Log("[CrazyGamesManager] Ads are currently disabled. Bypassing rewarded ad.");
            onDismiss?.Invoke();
            return;
        }

        if (isApplicationQuitting || CrazySDK.IsShutDown || !CrazySDK.IsAvailable || !CrazySDK.IsInitialized)
        {
#if UNITY_EDITOR
            Debug.Log("[CrazyGamesManager] Rewarded Ad simulated in Editor: granting reward.");
            onReward?.Invoke();
#else
            onDismiss?.Invoke();
#endif
            return;
        }

        if (isAdShowing)
        {
            onDismiss?.Invoke();
            return;
        }

        isAdShowing = true;
        GameplayStop();

        try
        {
            var ad = CrazySDK.Ad;
            if (ad == null)
            {
                isAdShowing = false;
                onDismiss?.Invoke();
                return;
            }

            ad.RequestAd(
                CrazyAdType.Rewarded,
                adStarted: () =>
                {
                    Debug.Log("[CrazyGamesManager] Rewarded Ad started.");
                },
                adError: (error) =>
                {
                    Debug.LogWarning($"[CrazyGamesManager] Rewarded Ad error: {error}");
                    isAdShowing = false;
                    GameplayStart();
                    onDismiss?.Invoke();
                },
                adFinished: () =>
                {
                    Debug.Log("[CrazyGamesManager] Rewarded Ad completed successfully.");
                    isAdShowing = false;
                    GameplayStart();
                    onReward?.Invoke();
                }
            );
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CrazyGamesManager] Failed to request rewarded ad: {ex.Message}");
            isAdShowing = false;
            onDismiss?.Invoke();
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (isApplicationQuitting) return;

        // When tab is hidden/unfocused, mute game audio to comply with browser audio policies
        if (!isAdShowing)
        {
            AudioListener.pause = !hasFocus;
        }
    }
}
