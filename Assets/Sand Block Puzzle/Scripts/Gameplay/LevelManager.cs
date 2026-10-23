using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SandBlocks.Core.Boundary;
using SandBlocks.Core.Data;

namespace SandBlocks.Gameplay
{
    /// <summary>
    /// Manages level progression (Current Level, Next Level, Previous Level),
    /// updates UI navigation buttons and level title display,
    /// and configures the screen (container boundary, crowd, buses) for each level.
    /// </summary>
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Level Playlist")]
        [Tooltip("Ordered list of all levels in the game")]
        [SerializeField] private List<LevelData> levels = new List<LevelData>();

        [Tooltip("Active level index (0-based)")]
        [SerializeField] private int currentLevelIndex = 0;

        [Tooltip("If true, Next Level on the last level loops back to Level 1, and Previous Level on Level 1 loops to the last level")]
        [SerializeField] private bool loopLevels = false;

        [Header("Screen & Boundary Setup")]
        [Tooltip("If true, automatically updates the ContainerBoundary visuals and dimensions to match the loaded level")]
        [SerializeField] private bool updateBoundaryOnLevelLoad = true;

        [Header("UI Navigation Controls")]
        [Tooltip("UI Button to advance to the next level")]
        [SerializeField] private Button nextLevelButton;

        [Tooltip("UI Button to return to the previous level")]
        [SerializeField] private Button previousLevelButton;

        [Tooltip("UI Button to restart the current level")]
        [SerializeField] private Button restartLevelButton;

        [Tooltip("Optional TextMeshPro text displaying current level title or number")]
        [SerializeField] private TMP_Text levelTitleText;

        [Header("Dependencies")]
        [SerializeField] private GameManager gameManager;
        [SerializeField] private ContainerBoundary boundary;

        /// <summary>
        /// Event invoked whenever a new level is loaded: (levelIndex, levelData)
        /// </summary>
        public event Action<int, LevelData> OnLevelChanged;

        // Public Properties
        public LevelData CurrentLevel => (currentLevelIndex >= 0 && currentLevelIndex < levels.Count) ? levels[currentLevelIndex] : null;
        public int CurrentLevelIndex => currentLevelIndex;
        public int TotalLevels => levels.Count;
        public bool HasNextLevel => loopLevels || currentLevelIndex < levels.Count - 1;
        public bool HasPreviousLevel => loopLevels || currentLevelIndex > 0;
        public IReadOnlyList<LevelData> Levels => levels;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            FindDependencies();
            AutoDetectUIElements();
        }

        private void Start()
        {
            SetupButtonListeners();

            // Auto-load first level if GameManager has not initialized one or if synced
            if (levels.Count > 0)
            {
                currentLevelIndex = Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1);
                
                // If gameManager already has a level, align currentLevelIndex
                if (gameManager != null && gameManager.CurrentLevel != null)
                {
                    int matchIdx = levels.IndexOf(gameManager.CurrentLevel);
                    if (matchIdx >= 0)
                    {
                        currentLevelIndex = matchIdx;
                    }
                    else
                    {
                        // Set gameManager's level to our currentLevel
                        SetupScreenForLevel(levels[currentLevelIndex]);
                    }
                }
                else if (gameManager != null && CurrentLevel != null)
                {
                    SetupScreenForLevel(CurrentLevel);
                }
            }

            UpdateUI();
        }

        private void OnDestroy()
        {
            TeardownButtonListeners();
        }

        private void FindDependencies()
        {
            if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
            if (boundary == null) boundary = FindAnyObjectByType<ContainerBoundary>();
        }

        private void AutoDetectUIElements()
        {
            if (nextLevelButton == null)
            {
                var go = GameObject.Find("NextButton") ?? GameObject.Find("NextLevelButton") ?? GameObject.Find("Btn_Next") ?? GameObject.Find("Next");
                if (go != null) nextLevelButton = go.GetComponent<Button>();
            }

            if (previousLevelButton == null)
            {
                var go = GameObject.Find("PreviousButton") ?? GameObject.Find("PrevButton") ?? GameObject.Find("PrevLevelButton") ?? GameObject.Find("Btn_Prev") ?? GameObject.Find("Previous");
                if (go != null) previousLevelButton = go.GetComponent<Button>();
            }

            if (restartLevelButton == null)
            {
                var go = GameObject.Find("RestartButton") ?? GameObject.Find("Btn_Restart") ?? GameObject.Find("Restart");
                if (go != null) restartLevelButton = go.GetComponent<Button>();
            }

            if (levelTitleText == null)
            {
                var go = GameObject.Find("LevelText") ?? GameObject.Find("LevelTitleText") ?? GameObject.Find("Txt_Level") ?? GameObject.Find("LevelTitle");
                if (go != null) levelTitleText = go.GetComponent<TMP_Text>();
            }
        }

        private void SetupButtonListeners()
        {
            if (nextLevelButton != null)
            {
                nextLevelButton.onClick.RemoveListener(NextLevel);
                nextLevelButton.onClick.AddListener(NextLevel);
            }

            if (previousLevelButton != null)
            {
                previousLevelButton.onClick.RemoveListener(PreviousLevel);
                previousLevelButton.onClick.AddListener(PreviousLevel);
            }

            if (restartLevelButton != null)
            {
                restartLevelButton.onClick.RemoveListener(RestartCurrentLevel);
                restartLevelButton.onClick.AddListener(RestartCurrentLevel);
            }
        }

        private void TeardownButtonListeners()
        {
            if (nextLevelButton != null) nextLevelButton.onClick.RemoveListener(NextLevel);
            if (previousLevelButton != null) previousLevelButton.onClick.RemoveListener(PreviousLevel);
            if (restartLevelButton != null) restartLevelButton.onClick.RemoveListener(RestartCurrentLevel);
        }

        // =========================================================================
        // LEVEL NAVIGATION (Can be hooked up to Button OnClick in Inspector)
        // =========================================================================

        /// <summary>
        /// Advances to the next level in the list.
        /// </summary>
        public void NextLevel()
        {
            if (levels.Count == 0)
            {
                Debug.LogWarning("[LevelManager] No levels configured in LevelManager.");
                return;
            }

            int nextIndex = currentLevelIndex + 1;
            if (nextIndex >= levels.Count)
            {
                if (loopLevels)
                {
                    nextIndex = 0;
                }
                else
                {
                    Debug.Log("<color=yellow>[LevelManager]</color> Already at the last level.");
                    return;
                }
            }

            LoadLevel(nextIndex);
        }

        /// <summary>
        /// Returns to the previous level in the list.
        /// </summary>
        public void PreviousLevel()
        {
            if (levels.Count == 0)
            {
                Debug.LogWarning("[LevelManager] No levels configured in LevelManager.");
                return;
            }

            int prevIndex = currentLevelIndex - 1;
            if (prevIndex < 0)
            {
                if (loopLevels)
                {
                    prevIndex = levels.Count - 1;
                }
                else
                {
                    Debug.Log("<color=yellow>[LevelManager]</color> Already at the first level.");
                    return;
                }
            }

            LoadLevel(prevIndex);
        }

        /// <summary>
        /// Restarts the active level from the beginning.
        /// </summary>
        public void RestartCurrentLevel()
        {
            if (CurrentLevel == null) return;
            Debug.Log($"<color=cyan>[LevelManager]</color> Restarting Level {currentLevelIndex + 1} ({CurrentLevel.name})");
            SetupScreenForLevel(CurrentLevel);
        }

        /// <summary>
        /// Loads a specific level by its 0-based index.
        /// </summary>
        public void LoadLevel(int index)
        {
            if (index < 0 || index >= levels.Count)
            {
                Debug.LogWarning($"[LevelManager] Invalid level index {index}. Total levels: {levels.Count}");
                return;
            }

            currentLevelIndex = index;
            LevelData selectedLevel = levels[currentLevelIndex];

            if (selectedLevel == null)
            {
                Debug.LogWarning($"[LevelManager] Level at index {index} is null!");
                return;
            }

            Debug.Log($"<color=green>[LevelManager]</color> Loaded Level {currentLevelIndex + 1}: {selectedLevel.name}");

            // Setup the screen according to the new level
            SetupScreenForLevel(selectedLevel);

            UpdateUI();
            OnLevelChanged?.Invoke(currentLevelIndex, selectedLevel);
        }

        /// <summary>
        /// Loads a specific LevelData object.
        /// </summary>
        public void LoadLevel(LevelData level)
        {
            if (level == null) return;

            int idx = levels.IndexOf(level);
            if (idx >= 0)
            {
                LoadLevel(idx);
            }
            else
            {
                // Unlisted level, add temporarily or load directly
                currentLevelIndex = levels.Count;
                levels.Add(level);
                SetupScreenForLevel(level);
                UpdateUI();
                OnLevelChanged?.Invoke(currentLevelIndex, level);
            }
        }

        /// <summary>
        /// Configures the entire game screen (boundary dimensions, crowd, and vehicles) to match the given LevelData.
        /// </summary>
        public void SetupScreenForLevel(LevelData level)
        {
            if (level == null) return;

            FindDependencies();

            // 1. Update the Container Boundary shape and visual dimensions if enabled
            if (updateBoundaryOnLevelLoad && boundary != null)
            {
                boundary.SetContainerShape(level.containerShape, level.containerWidth, level.containerHeight, level.containerCenter,
                    level.leftBorderOffset, level.rightBorderOffset, level.topBorderOffset, level.bottomBorderOffset);
                boundary.BuildVisualContainer();
            }

            // 2. Instruct GameManager to reload simulation and spawn buses
            if (gameManager != null)
            {
                gameManager.LoadLevel(level, updateBoundaryOnLevelLoad);
            }
        }

        /// <summary>
        /// Refreshes UI button interactable states and level label text.
        /// </summary>
        public void UpdateUI()
        {
            if (previousLevelButton != null)
            {
                previousLevelButton.interactable = HasPreviousLevel;
            }

            if (nextLevelButton != null)
            {
                nextLevelButton.interactable = HasNextLevel;
            }

            if (levelTitleText != null && CurrentLevel != null)
            {
                string displayName = !string.IsNullOrWhiteSpace(CurrentLevel.levelName) 
                    ? CurrentLevel.levelName 
                    : $"Level {currentLevelIndex + 1}";
                levelTitleText.text = displayName;
            }
        }

        // =========================================================================
        // EDITOR HELPERS
        // =========================================================================

#if UNITY_EDITOR
        private void Reset()
        {
            AutoFindLevels();
            FindDependencies();
            AutoDetectUIElements();
        }

        [ContextMenu("Auto-Find All Levels in Project")]
        public void AutoFindLevels()
        {
            levels.Clear();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:LevelData");
            var found = new List<LevelData>();
            foreach (var guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var lvl = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelData>(path);
                if (lvl != null && !found.Contains(lvl))
                {
                    found.Add(lvl);
                }
            }
            // Sort by file name (Level_01, Level_02, Level_03)
            found.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            levels.AddRange(found);
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"<color=green>[LevelManager]</color> Found and populated {levels.Count} levels.");
        }
#endif
    }
}