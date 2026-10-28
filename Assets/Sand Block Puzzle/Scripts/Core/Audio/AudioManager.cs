using System.Collections.Generic;
using UnityEngine;

namespace SandBlocks.Core.Audio
{
    /// <summary>
    /// High-performance audio manager featuring zero-allocation AudioSource pooling,
    /// organic pitch variance for repetitive actions, and customizable inspector clips.
    /// Safe for use before audio clips are assigned.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        private static AudioManager instance;
        public static AudioManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindAnyObjectByType<AudioManager>();
                    if (instance == null)
                    {
                        var go = new GameObject("[AudioManager]");
                        instance = go.AddComponent<AudioManager>();
                        if (Application.isPlaying)
                        {
                            DontDestroyOnLoad(go);
                        }
                    }
                }
                return instance;
            }
        }

        [Header("Vehicle Sounds")]
        [Tooltip("Played when player touches / selects a bus")]
        [SerializeField] private AudioClip busSelectClip;

        [Tooltip("Played when bus steps along grid cells or waypoints while dragging")]
        [SerializeField] private AudioClip busDragStepClip;

        [Tooltip("Played when bus is successfully placed into a valid cell")]
        [SerializeField] private AudioClip busPlaceClip;

        [Tooltip("Played when bus collision shake occurs (blocked by obstacle or invalid spot)")]
        [SerializeField] private AudioClip busBlockedClip;

        [Tooltip("Played when a bus reaches maximum passenger capacity")]
        [SerializeField] private AudioClip busFullClip;

        [Tooltip("Played when a filled bus drives away and exits the grid")]
        [SerializeField] private AudioClip busDriveAwayClip;

        [Header("Crowd & Passenger Sounds")]
        [Tooltip("Played when a passenger boards onto a bus seat")]
        [SerializeField] private AudioClip passengerBoardClip;

        [Tooltip("Played when a passenger drops off at the destination station platform")]
        [SerializeField] private AudioClip passengerDropOffClip;

        [Header("Level & UI Sounds")]
        [Tooltip("Played upon completing a level")]
        [SerializeField] private AudioClip levelWinClip;

        [Tooltip("Played when UI buttons (Next, Previous, Restart, Menu) are clicked")]
        [SerializeField] private AudioClip buttonClickClip;

        [Header("Background Music")]
        [Tooltip("Optional background music track")]
        [SerializeField] private AudioClip bgmMusicClip;
        [SerializeField] private bool loopMusic = true;

        [Header("Audio Settings")]
        [Tooltip("Master audio enable/disable toggle")]
        [SerializeField] private bool enableAudio = true;

        [Tooltip("Toggle background music playback")]
        [SerializeField] private bool enableMusic = true;

        [Range(0f, 1f)]
        [SerializeField] private float masterVolume = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float musicVolume = 0.5f;

        [Header("Audio Sources (Assign in Inspector)")]
        [Tooltip("Dedicated AudioSource for background music (BGM)")]
        [SerializeField] private AudioSource musicSource;

        [Tooltip("Pre-created AudioSources for sound effects (SFX) pooling. Recommended: 6 to 8 sources.")]
        [SerializeField] private AudioSource[] sfxSources;

        private int currentSfxIndex = 0;

        // Micro-throttling to prevent audio clipping during rapid passenger streaming
        private float lastBoardSoundTime = -1f;
        private float lastDragStepSoundTime = -1f;
        private const float MinBoardSoundInterval = 0.038f;
        private const float MinDragStepSoundInterval = 0.05f;

        public bool EnableAudio
        {
            get => enableAudio;
            set => SetAudioEnabled(value);
        }

        public bool EnableMusic
        {
            get => enableMusic;
            set => SetMusicEnabled(value);
        }

        public float MasterVolume
        {
            get => masterVolume;
            set => SetMasterVolume(value);
        }

        public float SfxVolume
        {
            get => sfxVolume;
            set => SetSfxVolume(value);
        }

        public float MusicVolume
        {
            get => musicVolume;
            set => SetMusicVolume(value);
        }

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
                InitializeAudioSources();
            }
            else if (instance != this)
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            if (enableMusic && bgmMusicClip != null)
            {
                PlayMusic(bgmMusicClip);
            }
        }

        private void InitializeAudioSources()
        {
            // 1. Music AudioSource
            if (musicSource == null)
            {
                // Fallback: check on this GameObject or children
                var allOnGo = GetComponentsInChildren<AudioSource>(true);
                if (allOnGo != null && allOnGo.Length > 0)
                {
                    musicSource = allOnGo[0];
                }
                else
                {
                    musicSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (musicSource != null)
            {
                musicSource.loop = loopMusic;
                musicSource.playOnAwake = false;
                musicSource.spatialBlend = 0f; // 2D sound
                musicSource.priority = 0;      // Top priority
                musicSource.volume = masterVolume * musicVolume;
            }

            // 2. SFX AudioSources Pool
            var validSfx = new List<AudioSource>();
            if (sfxSources != null)
            {
                for (int i = 0; i < sfxSources.Length; i++)
                {
                    if (sfxSources[i] != null && sfxSources[i] != musicSource)
                    {
                        validSfx.Add(sfxSources[i]);
                    }
                }
            }

            // If none explicitly assigned in inspector array, automatically adopt any other AudioSources in children
            if (validSfx.Count == 0)
            {
                var allSources = GetComponentsInChildren<AudioSource>(true);
                for (int i = 0; i < allSources.Length; i++)
                {
                    if (allSources[i] != musicSource)
                    {
                        validSfx.Add(allSources[i]);
                    }
                }
            }

            // Fallback: If still empty, add AudioSource components directly to this GameObject (zero new GameObjects)
            if (validSfx.Count == 0)
            {
                for (int i = 0; i < 6; i++)
                {
                    validSfx.Add(gameObject.AddComponent<AudioSource>());
                }
            }

            sfxSources = validSfx.ToArray();

            for (int i = 0; i < sfxSources.Length; i++)
            {
                if (sfxSources[i] != null)
                {
                    sfxSources[i].playOnAwake = false;
                    sfxSources[i].loop = false;
                    sfxSources[i].spatialBlend = 0f; // 2D sound
                }
            }
        }

        #region Public SFX Triggers
        public void PlayBusSelect()
        {
            PlaySFX(busSelectClip, volumeScale: 0.9f, pitchVariation: 0.03f);
        }

        public void PlayBusDragStep()
        {
            if (Time.realtimeSinceStartup - lastDragStepSoundTime < MinDragStepSoundInterval) return;
            lastDragStepSoundTime = Time.realtimeSinceStartup;
            PlaySFX(busDragStepClip, volumeScale: 0.7f, pitchVariation: 0.05f);
        }

        public void PlayBusPlace()
        {
            PlaySFX(busPlaceClip, volumeScale: 1.0f, pitchVariation: 0.02f);
        }

        public void PlayBusBlocked()
        {
            PlaySFX(busBlockedClip, volumeScale: 1.0f, pitchVariation: 0.04f);
        }

        public void PlayPassengerBoard()
        {
            if (Time.realtimeSinceStartup - lastBoardSoundTime < MinBoardSoundInterval) return;
            lastBoardSoundTime = Time.realtimeSinceStartup;
            // Slight pitch increase for juicy arcade popping feel
            PlaySFX(passengerBoardClip, volumeScale: 0.85f, pitchVariation: 0.08f);
        }

        public void PlayPassengerDropOff()
        {
            PlaySFX(passengerDropOffClip, volumeScale: 0.85f, pitchVariation: 0.05f);
        }

        public void PlayBusFull()
        {
            PlaySFX(busFullClip, volumeScale: 1.0f);
        }

        public void PlayBusDriveAway()
        {
            PlaySFX(busDriveAwayClip, volumeScale: 1.0f);
        }

        public void PlayLevelWin()
        {
            PlaySFX(levelWinClip, volumeScale: 1.0f);
        }

        public void PlayButtonClick()
        {
            PlaySFX(buttonClickClip, volumeScale: 0.9f);
        }

        /// <summary>
        /// Plays an arbitrary audio clip through the pre-allocated pool with optional pitch randomization.
        /// Zero GC allocations.
        /// </summary>
        public void PlaySFX(AudioClip clip, float volumeScale = 1.0f, float pitchVariation = 0.0f)
        {
            if (!enableAudio || clip == null || sfxSources == null || sfxSources.Length == 0) return;

            // Round-robin selection of an available source
            AudioSource src = sfxSources[currentSfxIndex];
            currentSfxIndex = (currentSfxIndex + 1) % sfxSources.Length;

            src.pitch = (pitchVariation > 0f) ? 1f + Random.Range(-pitchVariation, pitchVariation) : 1f;
            src.volume = masterVolume * sfxVolume * volumeScale;
            src.clip = clip;
            src.Play();
        }

        /// <summary>
        /// Starts or changes the current background music track.
        /// </summary>
        public void PlayMusic(AudioClip clip, bool loop = true)
        {
            if (musicSource == null) return;
            bgmMusicClip = clip;
            loopMusic = loop;
            musicSource.loop = loop;
            musicSource.clip = clip;

            if (enableAudio && enableMusic && clip != null)
            {
                musicSource.volume = masterVolume * musicVolume;
                musicSource.Play();
            }
            else
            {
                musicSource.Stop();
            }
        }
        #endregion

        #region Volume & Control Setters
        public void SetAudioEnabled(bool enabled)
        {
            enableAudio = enabled;
            if (musicSource != null)
            {
                if (enabled && enableMusic && bgmMusicClip != null && !musicSource.isPlaying)
                {
                    musicSource.Play();
                }
                else if (!enabled)
                {
                    musicSource.Stop();
                    StopAllSFX();
                }
            }
        }

        public void SetMusicEnabled(bool enabled)
        {
            enableMusic = enabled;
            if (musicSource != null)
            {
                if (enabled && enableAudio && bgmMusicClip != null)
                {
                    if (!musicSource.isPlaying) musicSource.Play();
                }
                else
                {
                    musicSource.Stop();
                }
            }
        }

        public void SetMasterVolume(float vol)
        {
            masterVolume = Mathf.Clamp01(vol);
            UpdateVolumes();
        }

        public void SetSfxVolume(float vol)
        {
            sfxVolume = Mathf.Clamp01(vol);
        }

        public void SetMusicVolume(float vol)
        {
            musicVolume = Mathf.Clamp01(vol);
            UpdateVolumes();
        }

        private void UpdateVolumes()
        {
            if (musicSource != null)
            {
                musicSource.volume = masterVolume * musicVolume;
            }
        }

        public void StopAllSFX()
        {
            if (sfxSources == null) return;
            for (int i = 0; i < sfxSources.Length; i++)
            {
                if (sfxSources[i] != null && sfxSources[i].isPlaying)
                {
                    sfxSources[i].Stop();
                }
            }
        }
        #endregion
    }
}
