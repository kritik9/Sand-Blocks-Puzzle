using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SandBlocks.Core.Haptics;
using SandBlocks.Core.Audio;

namespace SandBlocks.Gameplay
{
    /// <summary>
    /// In-game UI controller for game and haptic feedback settings.
    /// Manages master toggles, intensity and duration sliders, amplitude control,
    /// action-specific vibration filters, and persists user choices to PlayerPrefs.
    /// </summary>
    public class SettingsScreenUI : MonoBehaviour
    {
        [Header("Root & Panels")]
        [Tooltip("The main dialog card panel containing settings controls")]
        [SerializeField] private GameObject contentPanel;

        [Tooltip("Button to close/dismiss the settings dialog")]
        [SerializeField] private Button closeButton;

        [Tooltip("Optional background dimmer button to dismiss on tapping outside dialog")]
        [SerializeField] private Button dimmerButton;

        [Tooltip("Button to test the currently configured haptic vibration")]
        [SerializeField] private Button testHapticButton;

        [Header("Master Haptics Toggle")]
        [Tooltip("Toggle to enable or disable all haptic vibrations")]
        [SerializeField] private Toggle hapticsToggle;

        [Tooltip("Label displaying current status (Enabled / Disabled)")]
        [SerializeField] private TMP_Text hapticsStatusText;

        [Header("Intensity Setting")]
        [Tooltip("Slider for overall vibration intensity (0.05 to 1.50)")]
        [SerializeField] private Slider intensitySlider;

        [Tooltip("Text displaying intensity percentage")]
        [SerializeField] private TMP_Text intensityValueText;

        [Header("Duration Setting")]
        [Tooltip("Slider for vibration duration multiplier (0.05 to 1.50)")]
        [SerializeField] private Slider durationSlider;

        [Tooltip("Text displaying duration percentage")]
        [SerializeField] private TMP_Text durationValueText;

        [Header("Amplitude Setting")]
        [Tooltip("Slider for physical motor amplitude (10 to 255)")]
        [SerializeField] private Slider amplitudeSlider;

        [Tooltip("Text displaying amplitude value")]
        [SerializeField] private TMP_Text amplitudeValueText;

        [Header("Action Specific Toggles")]
        [Tooltip("Toggle haptic vibration on UI buttons (Play, Restart, Next/Prev Level)")]
        [SerializeField] private Toggle uiButtonsToggle;

        [Tooltip("Toggle haptic vibration on vehicle drag steps across grid cells")]
        [SerializeField] private Toggle dragStepsToggle;

        [Tooltip("Toggle haptic vibration on successful vehicle placement")]
        [SerializeField] private Toggle vehiclePlaceToggle;

        [Tooltip("Toggle haptic vibration on passenger boarding")]
        [SerializeField] private Toggle passengerBoardToggle;

        [Header("Procedural Generation")]
        [Tooltip("If true, automatically creates a stylish settings card if no child UI exists")]
        [SerializeField] private bool autoBuildIfEmpty = true;

        private bool isInitialized = false;

        private void Awake()
        {
            if (autoBuildIfEmpty && (contentPanel == null || transform.childCount == 0))
            {
                BuildProceduralUI();
            }

            InitializeListeners();
        }

        private void Start()
        {
            SyncWithHapticManager();
        }

        private void OnEnable()
        {
            SyncWithHapticManager();
        }

        /// <summary>
        /// Opens the settings screen, syncs current values, and plays audio feedback.
        /// </summary>
        public void Open()
        {
            gameObject.SetActive(true);
            SyncWithHapticManager();
            AudioManager.Instance?.PlayButtonClick();
        }

        /// <summary>
        /// Closes the settings screen, saves settings to PlayerPrefs, and plays audio feedback.
        /// </summary>
        public void Close()
        {
            HapticManager.Instance?.SavePreferences();
            gameObject.SetActive(false);
            AudioManager.Instance?.PlayButtonClick();
        }

        /// <summary>
        /// Toggles open/close state of the settings dialog.
        /// </summary>
        public void ToggleVisibility()
        {
            if (gameObject.activeSelf) Close();
            else Open();
        }

        private void InitializeListeners()
        {
            if (isInitialized) return;
            isInitialized = true;

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(Close);
                closeButton.onClick.AddListener(Close);
            }

            if (dimmerButton != null)
            {
                dimmerButton.onClick.RemoveListener(Close);
                dimmerButton.onClick.AddListener(Close);
            }

            if (testHapticButton != null)
            {
                testHapticButton.onClick.RemoveListener(OnTestHapticClicked);
                testHapticButton.onClick.AddListener(OnTestHapticClicked);
            }

            if (hapticsToggle != null)
            {
                hapticsToggle.onValueChanged.RemoveListener(OnHapticsToggleChanged);
                hapticsToggle.onValueChanged.AddListener(OnHapticsToggleChanged);
            }

            if (intensitySlider != null)
            {
                intensitySlider.onValueChanged.RemoveListener(OnIntensitySliderChanged);
                intensitySlider.onValueChanged.AddListener(OnIntensitySliderChanged);
            }

            if (durationSlider != null)
            {
                durationSlider.onValueChanged.RemoveListener(OnDurationSliderChanged);
                durationSlider.onValueChanged.AddListener(OnDurationSliderChanged);
            }

            if (amplitudeSlider != null)
            {
                amplitudeSlider.onValueChanged.RemoveListener(OnAmplitudeSliderChanged);
                amplitudeSlider.onValueChanged.AddListener(OnAmplitudeSliderChanged);
            }

            if (uiButtonsToggle != null)
            {
                uiButtonsToggle.onValueChanged.RemoveListener(OnUIButtonsToggleChanged);
                uiButtonsToggle.onValueChanged.AddListener(OnUIButtonsToggleChanged);
            }

            if (dragStepsToggle != null)
            {
                dragStepsToggle.onValueChanged.RemoveListener(OnDragStepsToggleChanged);
                dragStepsToggle.onValueChanged.AddListener(OnDragStepsToggleChanged);
            }

            if (vehiclePlaceToggle != null)
            {
                vehiclePlaceToggle.onValueChanged.RemoveListener(OnVehiclePlaceToggleChanged);
                vehiclePlaceToggle.onValueChanged.AddListener(OnVehiclePlaceToggleChanged);
            }

            if (passengerBoardToggle != null)
            {
                passengerBoardToggle.onValueChanged.RemoveListener(OnPassengerBoardToggleChanged);
                passengerBoardToggle.onValueChanged.AddListener(OnPassengerBoardToggleChanged);
            }
        }

        /// <summary>
        /// Synchronizes all sliders and toggles with active HapticManager values.
        /// </summary>
        public void SyncWithHapticManager()
        {
            var hm = HapticManager.Instance;
            if (hm == null) return;

            if (hapticsToggle != null) hapticsToggle.SetIsOnWithoutNotify(hm.EnableHaptics);
            if (intensitySlider != null) intensitySlider.SetValueWithoutNotify(hm.IntensityMultiplier);
            if (durationSlider != null) durationSlider.SetValueWithoutNotify(hm.DurationMultiplier);
            if (amplitudeSlider != null) amplitudeSlider.SetValueWithoutNotify(hm.LightAmplitude);

            if (uiButtonsToggle != null) uiButtonsToggle.SetIsOnWithoutNotify(hm.HapticsOnUIButtons);
            if (dragStepsToggle != null) dragStepsToggle.SetIsOnWithoutNotify(hm.HapticsOnVehicleDragStep);
            if (vehiclePlaceToggle != null) vehiclePlaceToggle.SetIsOnWithoutNotify(hm.HapticsOnVehiclePlace);
            if (passengerBoardToggle != null) passengerBoardToggle.SetIsOnWithoutNotify(hm.HapticsOnPassengerBoard);

            UpdateLabels();
            UpdateInteractability();
        }

        private void OnHapticsToggleChanged(bool isEnabled)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.EnableHaptics = isEnabled;
            }

            UpdateLabels();
            UpdateInteractability();
            HapticManager.Instance?.SavePreferences();
        }

        private void OnIntensitySliderChanged(float value)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.IntensityMultiplier = value;
            }

            UpdateLabels();
            HapticManager.Instance?.SavePreferences();
        }

        private void OnDurationSliderChanged(float value)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.DurationMultiplier = value;
            }

            UpdateLabels();
            HapticManager.Instance?.SavePreferences();
        }

        private void OnAmplitudeSliderChanged(float value)
        {
            int amp = Mathf.Clamp(Mathf.RoundToInt(value), 1, 255);
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.LightAmplitude = amp;
                HapticManager.Instance.SelectionAmplitude = Mathf.Clamp(Mathf.RoundToInt(amp * 0.75f), 1, 255);
            }

            UpdateLabels();
            HapticManager.Instance?.SavePreferences();
        }

        private void OnUIButtonsToggleChanged(bool isEnabled)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.HapticsOnUIButtons = isEnabled;
                HapticManager.Instance.SavePreferences();
            }
        }

        private void OnDragStepsToggleChanged(bool isEnabled)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.HapticsOnVehicleDragStep = isEnabled;
                HapticManager.Instance.SavePreferences();
            }
        }

        private void OnVehiclePlaceToggleChanged(bool isEnabled)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.HapticsOnVehiclePlace = isEnabled;
                HapticManager.Instance.SavePreferences();
            }
        }

        private void OnPassengerBoardToggleChanged(bool isEnabled)
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.HapticsOnPassengerBoard = isEnabled;
                HapticManager.Instance.SavePreferences();
            }
        }

        private void OnTestHapticClicked()
        {
            if (HapticManager.Instance != null)
            {
                HapticManager.Instance.LightImpact();
            }
        }

        private void UpdateLabels()
        {
            var hm = HapticManager.Instance;
            if (hm == null) return;

            if (hapticsStatusText != null)
            {
                hapticsStatusText.text = hm.EnableHaptics ? "<color=#00E5FF>ON</color>" : "<color=#888888>OFF</color>";
            }

            if (intensityValueText != null)
            {
                intensityValueText.text = $"{Mathf.RoundToInt(hm.IntensityMultiplier * 100f)}%";
            }

            if (durationValueText != null)
            {
                durationValueText.text = $"{Mathf.RoundToInt(hm.DurationMultiplier * 100f)}%";
            }

            if (amplitudeValueText != null)
            {
                amplitudeValueText.text = $"{hm.LightAmplitude} / 255";
            }
        }

        private void UpdateInteractability()
        {
            bool isMasterEnabled = (HapticManager.Instance != null && HapticManager.Instance.EnableHaptics);

            if (intensitySlider != null) intensitySlider.interactable = isMasterEnabled;
            if (durationSlider != null) durationSlider.interactable = isMasterEnabled;
            if (amplitudeSlider != null) amplitudeSlider.interactable = isMasterEnabled;
            if (testHapticButton != null) testHapticButton.interactable = isMasterEnabled;
            if (uiButtonsToggle != null) uiButtonsToggle.interactable = isMasterEnabled;
            if (dragStepsToggle != null) dragStepsToggle.interactable = isMasterEnabled;
            if (vehiclePlaceToggle != null) vehiclePlaceToggle.interactable = isMasterEnabled;
            if (passengerBoardToggle != null) passengerBoardToggle.interactable = isMasterEnabled;
        }

        // =========================================================================
        // Procedural UI Construction (Clean modern card dialog)
        // =========================================================================

        [ContextMenu("Build Default Settings UI")]
        public void BuildProceduralUI()
        {
            // Clean up existing children if rebuilding
            while (transform.childCount > 0)
            {
                DestroyImmediate(transform.GetChild(0).gameObject);
            }

            RectTransform rootRect = GetComponent<RectTransform>();
            if (rootRect == null) rootRect = gameObject.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                var existingTmp = FindAnyObjectByType<TMP_Text>();
                if (existingTmp != null) font = existingTmp.font;
            }

            Sprite whiteBox = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));

            // 1. Fullscreen Dimmer Background
            GameObject dimmerGo = new GameObject("Dimmer", typeof(RectTransform), typeof(Image), typeof(Button));
            dimmerGo.transform.SetParent(transform, false);
            RectTransform dimmerRect = dimmerGo.GetComponent<RectTransform>();
            dimmerRect.anchorMin = Vector2.zero;
            dimmerRect.anchorMax = Vector2.one;
            dimmerRect.offsetMin = Vector2.zero;
            dimmerRect.offsetMax = Vector2.zero;

            Image dimmerImg = dimmerGo.GetComponent<Image>();
            dimmerImg.color = new Color(0f, 0f, 0f, 0.70f);
            dimmerButton = dimmerGo.GetComponent<Button>();

            // 2. Central Settings Card Panel
            GameObject cardGo = new GameObject("CardPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            cardGo.transform.SetParent(transform, false);
            contentPanel = cardGo;

            RectTransform cardRect = cardGo.GetComponent<RectTransform>();
            cardRect.anchorMin = new Vector2(0.5f, 0.5f);
            cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(520, 680);

            Image cardImg = cardGo.GetComponent<Image>();
            cardImg.sprite = whiteBox;
            cardImg.color = new Color(0.12f, 0.14f, 0.20f, 0.98f);

            VerticalLayoutGroup vlg = cardGo.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(36, 36, 30, 30);
            vlg.spacing = 14;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 3. Header Title & Subtitle
            CreateTextElement(cardGo.transform, "TitleText", "SETTINGS", 30, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font, 40);
            CreateTextElement(cardGo.transform, "SubtitleText", "Haptic & Vibration Controls", 16, FontStyles.Normal, new Color(0.70f, 0.75f, 0.85f), TextAlignmentOptions.Center, font, 24);

            CreateDivider(cardGo.transform, whiteBox, 2, new Color(1f, 1f, 1f, 0.12f));

            // 4. Master Haptics Row
            var (hapticsRow, hToggle, hStatus) = CreateToggleRow(cardGo.transform, "MasterToggleRow", "Haptics Enabled", whiteBox, font);
            hapticsToggle = hToggle;
            hapticsStatusText = hStatus;

            CreateDivider(cardGo.transform, whiteBox, 1, new Color(1f, 1f, 1f, 0.08f));

            // 5. Sliders (Intensity, Duration, Amplitude)
            var (intRow, intSlider, intVal) = CreateSliderRow(cardGo.transform, "IntensityRow", "Vibration Intensity", 0.05f, 1.50f, 0.35f, false, whiteBox, new Color(0.25f, 0.70f, 1.0f), font);
            intensitySlider = intSlider;
            intensityValueText = intVal;

            var (durRow, durSlider, durVal) = CreateSliderRow(cardGo.transform, "DurationRow", "Vibration Duration", 0.05f, 1.50f, 0.40f, false, whiteBox, new Color(0.20f, 0.85f, 0.65f), font);
            durationSlider = durSlider;
            durationValueText = durVal;

            var (ampRow, ampSlider, ampVal) = CreateSliderRow(cardGo.transform, "AmplitudeRow", "Motor Amplitude", 10f, 255f, 70f, true, whiteBox, new Color(1.0f, 0.75f, 0.20f), font);
            amplitudeSlider = ampSlider;
            amplitudeValueText = ampVal;

            CreateDivider(cardGo.transform, whiteBox, 1, new Color(1f, 1f, 1f, 0.08f));

            // 6. Action Filter Checkboxes
            var (_, btnToggle, _) = CreateToggleRow(cardGo.transform, "UIButtonsToggleRow", "Vibrate on Buttons (Play / Restart)", whiteBox, font, false);
            uiButtonsToggle = btnToggle;

            var (_, dragToggle, _) = CreateToggleRow(cardGo.transform, "DragStepToggleRow", "Vibrate on Dragging Grid Cells", whiteBox, font, false);
            dragStepsToggle = dragToggle;

            // 7. Bottom Button Actions (Test & Close)
            GameObject bottomRow = new GameObject("BottomButtonRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bottomRow.transform.SetParent(cardGo.transform, false);
            RectTransform bottomRect = bottomRow.GetComponent<RectTransform>();
            bottomRect.sizeDelta = new Vector2(0, 56);

            HorizontalLayoutGroup bottomHlg = bottomRow.GetComponent<HorizontalLayoutGroup>();
            bottomHlg.spacing = 16;
            bottomHlg.childControlWidth = true;
            bottomHlg.childControlHeight = true;
            bottomHlg.childForceExpandWidth = true;
            bottomHlg.childForceExpandHeight = true;

            testHapticButton = CreateActionButton(bottomRow.transform, "TestButton", "⚡ Test Vibration", new Color(0.38f, 0.32f, 0.78f), whiteBox, font);
            closeButton = CreateActionButton(bottomRow.transform, "CloseButton", "✓ Done", new Color(0.18f, 0.72f, 0.45f), whiteBox, font);

            isInitialized = false;
            InitializeListeners();
            SyncWithHapticManager();
        }

        private (GameObject, Toggle, TMP_Text) CreateToggleRow(Transform parent, string name, string labelText, Sprite whiteBox, TMP_FontAsset font, bool includeStatusText = true)
        {
            GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(parent, false);
            RectTransform rowRect = row.GetComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0, 38);

            HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.spacing = 10;

            var labelTmp = CreateTextElement(row.transform, "Label", labelText, 18, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft, font);
            RectTransform labelRect = labelTmp.GetComponent<RectTransform>();
            labelRect.sizeDelta = new Vector2(300, 38);

            TMP_Text statusTmp = null;
            if (includeStatusText)
            {
                statusTmp = CreateTextElement(row.transform, "Status", "ON", 16, FontStyles.Bold, new Color(0f, 0.9f, 1f), TextAlignmentOptions.MidlineRight, font);
                RectTransform statusRect = statusTmp.GetComponent<RectTransform>();
                statusRect.sizeDelta = new Vector2(50, 38);
            }

            // Toggle Box
            GameObject toggleGo = new GameObject("Toggle", typeof(RectTransform), typeof(Toggle));
            toggleGo.transform.SetParent(row.transform, false);
            RectTransform toggleRect = toggleGo.GetComponent<RectTransform>();
            toggleRect.sizeDelta = new Vector2(56, 32);

            Toggle toggle = toggleGo.GetComponent<Toggle>();

            // Background
            GameObject bgGo = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(toggleGo.transform, false);
            RectTransform bgRect = bgGo.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            Image bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = whiteBox;
            bgImg.color = new Color(0.22f, 0.25f, 0.35f);

            // Checkmark
            GameObject checkGo = new GameObject("Checkmark", typeof(RectTransform), typeof(Image));
            checkGo.transform.SetParent(bgGo.transform, false);
            RectTransform checkRect = checkGo.GetComponent<RectTransform>();
            checkRect.anchorMin = new Vector2(0.15f, 0.15f);
            checkRect.anchorMax = new Vector2(0.85f, 0.85f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            Image checkImg = checkGo.GetComponent<Image>();
            checkImg.sprite = whiteBox;
            checkImg.color = new Color(0.18f, 0.82f, 0.55f);

            toggle.targetGraphic = bgImg;
            toggle.graphic = checkImg;

            return (row, toggle, statusTmp);
        }

        private (GameObject, Slider, TMP_Text) CreateSliderRow(Transform parent, string name, string labelText, float min, float max, float defVal, bool wholeNums, Sprite whiteBox, Color fillColor, TMP_FontAsset font)
        {
            GameObject container = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup));
            container.transform.SetParent(parent, false);
            RectTransform contRect = container.GetComponent<RectTransform>();
            contRect.sizeDelta = new Vector2(0, 52);

            VerticalLayoutGroup vlg = container.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Header line: Label on left, Value on right
            GameObject headerRow = new GameObject("Header", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            headerRow.transform.SetParent(container.transform, false);
            RectTransform headRect = headerRow.GetComponent<RectTransform>();
            headRect.sizeDelta = new Vector2(0, 22);

            HorizontalLayoutGroup hlg = headerRow.GetComponent<HorizontalLayoutGroup>();
            hlg.childControlWidth = false;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var lbl = CreateTextElement(headerRow.transform, "Label", labelText, 17, FontStyles.Normal, Color.white, TextAlignmentOptions.MidlineLeft, font);
            lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(320, 22);

            var valTxt = CreateTextElement(headerRow.transform, "Value", $"{defVal}", 17, FontStyles.Bold, fillColor, TextAlignmentOptions.MidlineRight, font);
            valTxt.GetComponent<RectTransform>().sizeDelta = new Vector2(120, 22);

            // Slider Bar
            GameObject sliderGo = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            sliderGo.transform.SetParent(container.transform, false);
            RectTransform sliderRect = sliderGo.GetComponent<RectTransform>();
            sliderRect.sizeDelta = new Vector2(0, 22);

            Slider slider = sliderGo.GetComponent<Slider>();
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNums;
            slider.value = defVal;

            // Background
            GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(sliderGo.transform, false);
            RectTransform bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0, 0.25f);
            bgRect.anchorMax = new Vector2(1, 0.75f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            Image bgImg = bg.GetComponent<Image>();
            bgImg.sprite = whiteBox;
            bgImg.color = new Color(0.22f, 0.25f, 0.35f);

            // Fill Area
            GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
            fillArea.transform.SetParent(sliderGo.transform, false);
            RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
            fillAreaRect.anchorMin = new Vector2(0, 0.25f);
            fillAreaRect.anchorMax = new Vector2(1, 0.75f);
            fillAreaRect.offsetMin = new Vector2(0, 0);
            fillAreaRect.offsetMax = new Vector2(-10, 0);

            GameObject fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(fillArea.transform, false);
            RectTransform fillRect = fill.GetComponent<RectTransform>();
            fillRect.sizeDelta = Vector2.zero;

            Image fillImg = fill.GetComponent<Image>();
            fillImg.sprite = whiteBox;
            fillImg.color = fillColor;

            // Handle Area
            GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
            handleArea.transform.SetParent(sliderGo.transform, false);
            RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
            handleAreaRect.anchorMin = Vector2.zero;
            handleAreaRect.anchorMax = Vector2.one;
            handleAreaRect.offsetMin = new Vector2(10, 0);
            handleAreaRect.offsetMax = new Vector2(-10, 0);

            GameObject handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(handleArea.transform, false);
            RectTransform handleRect = handle.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(22, 22);

            Image handleImg = handle.GetComponent<Image>();
            handleImg.sprite = whiteBox;
            handleImg.color = Color.white;

            slider.targetGraphic = handleImg;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;

            return (container, slider, valTxt);
        }

        private Button CreateActionButton(Transform parent, string name, string labelText, Color bgColor, Sprite whiteBox, TMP_FontAsset font)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);

            Image img = btnGo.GetComponent<Image>();
            img.sprite = whiteBox;
            img.color = bgColor;

            Button btn = btnGo.GetComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = bgColor;
            cb.highlightedColor = bgColor * 1.15f;
            cb.pressedColor = bgColor * 0.85f;
            btn.colors = cb;

            CreateTextElement(btnGo.transform, "Label", labelText, 19, FontStyles.Bold, Color.white, TextAlignmentOptions.Center, font);

            return btn;
        }

        private TMP_Text CreateTextElement(Transform parent, string name, string text, float fontSize, FontStyles style, Color color, TextAlignmentOptions alignment, TMP_FontAsset font, float height = 0)
        {
            GameObject textGo = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(parent, false);

            RectTransform rt = textGo.GetComponent<RectTransform>();
            if (height > 0)
            {
                rt.sizeDelta = new Vector2(0, height);
            }
            else
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }

            TextMeshProUGUI tmp = textGo.GetComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;

            return tmp;
        }

        private void CreateDivider(Transform parent, Sprite whiteBox, float height, Color color)
        {
            GameObject div = new GameObject("Divider", typeof(RectTransform), typeof(Image));
            div.transform.SetParent(parent, false);
            RectTransform rt = div.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0, height);

            Image img = div.GetComponent<Image>();
            img.sprite = whiteBox;
            img.color = color;
        }
    }
}
