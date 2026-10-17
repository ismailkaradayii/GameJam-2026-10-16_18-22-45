using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ashfall.MainMenu
{
    /// <summary>Loads, applies, and persists the ASHFALL audio and display preferences.</summary>
    public sealed class AshfallSettingsController : MonoBehaviour
    {
        private enum DialogMode { None, RestoreDefaults, DisplayChange }

        private const string MasterKey = "Ashfall.MasterVolume";
        private const string MusicKey = "Ashfall.MusicVolume";
        private const string SfxKey = "Ashfall.SfxVolume";
        private const string ResolutionWidthKey = "Ashfall.ResolutionWidth";
        private const string ResolutionHeightKey = "Ashfall.ResolutionHeight";
        private const string ResolutionRefreshKey = "Ashfall.ResolutionRefresh";
        private const string DisplayModeKey = "Ashfall.DisplayMode";
        private const string QualityKey = "Ashfall.Quality";
        private const string VSyncKey = "Ashfall.VSync";
        private const string FrameRateKey = "Ashfall.FrameRate";
        private const float DefaultMusic = 0.75f;
        private const float DefaultMaster = 1f;
        private const float DefaultSfx = 1f;

        [Header("Audio")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Text masterValue;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Text musicValue;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Text sfxValue;
        [SerializeField] private AudioSource[] musicSources;
        [SerializeField] private AudioSource[] sfxSources;

        [Header("Graphics")]
        [SerializeField] private Dropdown resolutionDropdown;
        [SerializeField] private Dropdown displayModeDropdown;
        [SerializeField] private Dropdown qualityDropdown;
        [SerializeField] private Dropdown vsyncDropdown;
        [SerializeField] private Dropdown frameRateDropdown;
        [SerializeField] private Button restoreDefaultsButton;

        [Header("Confirmation")]
        [SerializeField] private CanvasGroup confirmationGroup;
        [SerializeField] private Text confirmationTitle;
        [SerializeField] private Text confirmationMessage;
        [SerializeField] private Text confirmationCountdown;
        [SerializeField] private Button confirmationConfirmButton;
        [SerializeField] private Text confirmationConfirmLabel;
        [SerializeField] private Button confirmationCancelButton;
        [SerializeField] private Text confirmationCancelLabel;

        private readonly List<Resolution> availableResolutions = new List<Resolution>();
        private float[] baseMusicVolumes;
        private float[] baseSfxVolumes;
        private bool refreshing;
        private DialogMode dialogMode;
        private Coroutine displayCountdown;
        private DisplaySnapshot previousDisplay;

        private struct DisplaySnapshot
        {
            public int width;
            public int height;
            public int refreshRate;
            public FullScreenMode mode;
            public int quality;
            public int vsync;
            public int frameRate;
        }

        private void Awake()
        {
            CacheSourceVolumes();
            WireCallbacks();
            ConfigureDropdownOptions();
            ApplySavedSettings();
            Refresh();
            HideConfirmation();
        }

        private void CacheSourceVolumes()
        {
            baseMusicVolumes = CacheBaseVolumes(musicSources);
            baseSfxVolumes = CacheBaseVolumes(sfxSources);
            if (!HasAssignedSource(musicSources))
                Debug.LogWarning("ASHFALL music volume will be saved, but no music AudioSource with an assigned clip is connected yet.", this);
            if (!HasAssignedSource(sfxSources))
                Debug.LogWarning("ASHFALL sound effects volume will be saved, but no SFX AudioSource with an assigned clip is connected yet.", this);
        }

        private static bool HasAssignedSource(AudioSource[] sources)
        {
            if (sources == null) return false;
            for (int i = 0; i < sources.Length; i++)
                if (sources[i] != null && sources[i].clip != null) return true;
            return false;
        }

        private static float[] CacheBaseVolumes(AudioSource[] sources)
        {
            if (sources == null) return System.Array.Empty<float>();
            var values = new float[sources.Length];
            for (int i = 0; i < sources.Length; i++)
                values[i] = sources[i] != null ? sources[i].volume : 1f;
            return values;
        }

        private void WireCallbacks()
        {
            if (masterSlider != null) { masterSlider.onValueChanged.RemoveListener(SetMasterVolume); masterSlider.onValueChanged.AddListener(SetMasterVolume); }
            if (musicSlider != null) { musicSlider.onValueChanged.RemoveListener(SetMusicVolume); musicSlider.onValueChanged.AddListener(SetMusicVolume); }
            if (sfxSlider != null) { sfxSlider.onValueChanged.RemoveListener(SetSfxVolume); sfxSlider.onValueChanged.AddListener(SetSfxVolume); }
            if (resolutionDropdown != null) { resolutionDropdown.onValueChanged.RemoveListener(SetResolution); resolutionDropdown.onValueChanged.AddListener(SetResolution); }
            if (displayModeDropdown != null) { displayModeDropdown.onValueChanged.RemoveListener(SetDisplayMode); displayModeDropdown.onValueChanged.AddListener(SetDisplayMode); }
            if (qualityDropdown != null) { qualityDropdown.onValueChanged.RemoveListener(SetQuality); qualityDropdown.onValueChanged.AddListener(SetQuality); }
            if (vsyncDropdown != null) { vsyncDropdown.onValueChanged.RemoveListener(SetVSync); vsyncDropdown.onValueChanged.AddListener(SetVSync); }
            if (frameRateDropdown != null) { frameRateDropdown.onValueChanged.RemoveListener(SetFrameRate); frameRateDropdown.onValueChanged.AddListener(SetFrameRate); }
            if (restoreDefaultsButton != null)
            {
                restoreDefaultsButton.onClick.RemoveListener(OpenRestoreDefaultsConfirmation);
                restoreDefaultsButton.onClick.AddListener(OpenRestoreDefaultsConfirmation);
            }
            if (confirmationConfirmButton != null)
            {
                confirmationConfirmButton.onClick.RemoveListener(ConfirmDialog);
                confirmationConfirmButton.onClick.AddListener(ConfirmDialog);
            }
            if (confirmationCancelButton != null)
            {
                confirmationCancelButton.onClick.RemoveListener(CancelDialog);
                confirmationCancelButton.onClick.AddListener(CancelDialog);
            }
        }

        private void ConfigureDropdownOptions()
        {
            availableResolutions.Clear();
            Resolution[] systemResolutions = Screen.resolutions;
            for (int i = 0; i < systemResolutions.Length; i++)
            {
                Resolution candidate = systemResolutions[i];
                bool duplicate = false;
                for (int j = 0; j < availableResolutions.Count; j++)
                {
                    Resolution known = availableResolutions[j];
                    if (known.width == candidate.width && known.height == candidate.height
                        && Mathf.RoundToInt((float)known.refreshRateRatio.value) == Mathf.RoundToInt((float)candidate.refreshRateRatio.value))
                    { duplicate = true; break; }
                }
                if (!duplicate) availableResolutions.Add(candidate);
            }
            if (availableResolutions.Count == 0)
                availableResolutions.Add(Screen.currentResolution);

            SetOptions(resolutionDropdown, ResolutionLabels());
            SetOptions(displayModeDropdown, new List<string> { "Fullscreen", "Windowed", "Borderless" });
            SetOptions(qualityDropdown, new List<string>(QualitySettings.names));
            SetOptions(vsyncDropdown, new List<string> { "Off", "On" });
            SetOptions(frameRateDropdown, new List<string> { "30 fps", "60 fps", "120 fps", "Unlimited" });
        }

        private List<string> ResolutionLabels()
        {
            var labels = new List<string>(availableResolutions.Count);
            for (int i = 0; i < availableResolutions.Count; i++)
            {
                Resolution resolution = availableResolutions[i];
                labels.Add($"{resolution.width} × {resolution.height}  ·  {Mathf.RoundToInt((float)resolution.refreshRateRatio.value)} Hz");
            }
            return labels;
        }

        private static void SetOptions(Dropdown dropdown, List<string> options)
        {
            if (dropdown == null) return;
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.RefreshShownValue();
        }

        public void Refresh()
        {
            refreshing = true;
            SetSlider(masterSlider, masterValue, PlayerPrefs.GetFloat(MasterKey, DefaultMaster));
            SetSlider(musicSlider, musicValue, PlayerPrefs.GetFloat(MusicKey, DefaultMusic));
            SetSlider(sfxSlider, sfxValue, PlayerPrefs.GetFloat(SfxKey, DefaultSfx));
            SetDropdown(resolutionDropdown, FindCurrentResolutionIndex());
            SetDropdown(displayModeDropdown, CurrentDisplayModeIndex());
            SetDropdown(qualityDropdown, Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()), 0, Mathf.Max(0, QualitySettings.names.Length - 1)));
            SetDropdown(vsyncDropdown, PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount > 0 ? 1 : 0));
            SetDropdown(frameRateDropdown, FrameRateIndex(PlayerPrefs.GetInt(FrameRateKey, Application.targetFrameRate)));
            refreshing = false;
        }

        private void ApplySavedSettings()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, DefaultMaster));
            ApplyCategoryVolumes(musicSources, baseMusicVolumes, Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, DefaultMusic)));
            ApplyCategoryVolumes(sfxSources, baseSfxVolumes, Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, DefaultSfx)));

            if (QualitySettings.names.Length > 0)
            {
                int quality = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()), 0, QualitySettings.names.Length - 1);
                QualitySettings.SetQualityLevel(quality, true);
            }
            QualitySettings.vSyncCount = Mathf.Clamp(PlayerPrefs.GetInt(VSyncKey, QualitySettings.vSyncCount), 0, 1);
            Application.targetFrameRate = NormalizeFrameRate(PlayerPrefs.GetInt(FrameRateKey, Application.targetFrameRate));

            int displayMode = Mathf.Clamp(PlayerPrefs.GetInt(DisplayModeKey, CurrentDisplayModeIndex()), 0, 2);
            int width = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.currentResolution.width);
            int height = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.currentResolution.height);
            int refresh = PlayerPrefs.GetInt(ResolutionRefreshKey, Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value));
            Resolution resolution = FindResolution(width, height, refresh);
            ApplyDisplay(resolution, displayMode);
        }

        public void SetMasterVolume(float value)
        {
            if (refreshing) return;
            value = Mathf.Clamp01(value);
            AudioListener.volume = value;
            SaveFloat(MasterKey, value);
            UpdatePercentage(masterValue, value);
        }

        public void SetMusicVolume(float value)
        {
            if (refreshing) return;
            value = Mathf.Clamp01(value);
            ApplyCategoryVolumes(musicSources, baseMusicVolumes, value);
            SaveFloat(MusicKey, value);
            UpdatePercentage(musicValue, value);
        }

        public void SetSfxVolume(float value)
        {
            if (refreshing) return;
            value = Mathf.Clamp01(value);
            ApplyCategoryVolumes(sfxSources, baseSfxVolumes, value);
            SaveFloat(SfxKey, value);
            UpdatePercentage(sfxValue, value);
        }

        public void SetResolution(int index)
        {
            if (refreshing || availableResolutions.Count == 0) return;
            index = Mathf.Clamp(index, 0, availableResolutions.Count - 1);
            BeginDisplayChange(availableResolutions[index], CurrentDisplayModeIndex());
        }

        public void SetDisplayMode(int mode)
        {
            if (refreshing) return;
            mode = Mathf.Clamp(mode, 0, 2);
            Resolution resolution = CurrentResolutionFromScreen();
            BeginDisplayChange(resolution, mode);
        }

        public void SetQuality(int value)
        {
            if (refreshing || QualitySettings.names.Length == 0) return;
            value = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1);
            QualitySettings.SetQualityLevel(value, true);
            SaveInt(QualityKey, value);
        }

        public void SetVSync(int value)
        {
            if (refreshing) return;
            value = Mathf.Clamp(value, 0, 1);
            QualitySettings.vSyncCount = value;
            if (value > 0) Application.targetFrameRate = -1;
            else Application.targetFrameRate = NormalizeFrameRate(PlayerPrefs.GetInt(FrameRateKey, -1));
            SaveInt(VSyncKey, value);
        }

        public void SetFrameRate(int index)
        {
            if (refreshing) return;
            int[] choices = { 30, 60, 120, -1 };
            index = Mathf.Clamp(index, 0, choices.Length - 1);
            int target = choices[index];
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = target;
            SaveInt(VSyncKey, 0);
            SaveInt(FrameRateKey, target);
            refreshing = true;
            SetDropdown(vsyncDropdown, 0);
            refreshing = false;
        }

        public void OpenRestoreDefaultsConfirmation()
        {
            if (dialogMode != DialogMode.None) return;
            dialogMode = DialogMode.RestoreDefaults;
            ShowConfirmation("Restore defaults", "Reset audio and graphics preferences? Your gameplay progress and saves will not be changed.", "Confirm", "Cancel");
        }

        public bool TryCancelDialog()
        {
            if (dialogMode == DialogMode.None) return false;
            CancelDialog();
            return true;
        }

        private void BeginDisplayChange(Resolution resolution, int mode)
        {
            if (!IsValidResolution(resolution)) return;
            if (dialogMode != DialogMode.DisplayChange) previousDisplay = CaptureDisplay();
            ApplyDisplay(resolution, mode);
            dialogMode = DialogMode.DisplayChange;
            ShowConfirmation("Keep display settings?", "The display will return to its previous configuration when the timer ends.", "Keep changes", "Revert");
            if (displayCountdown != null) StopCoroutine(displayCountdown);
            displayCountdown = StartCoroutine(DisplayConfirmationCountdown(10f));
        }

        private IEnumerator DisplayConfirmationCountdown(float seconds)
        {
            float remaining = seconds;
            while (remaining > 0f && dialogMode == DialogMode.DisplayChange)
            {
                if (confirmationCountdown != null) confirmationCountdown.text = $"Reverting in {Mathf.CeilToInt(remaining)} seconds";
                remaining -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (dialogMode == DialogMode.DisplayChange) RevertDisplayChanges();
            displayCountdown = null;
        }

        public void ConfirmDialog()
        {
            if (dialogMode == DialogMode.RestoreDefaults)
            {
                ResetPreferencesToDefaults();
            }
            else if (dialogMode == DialogMode.DisplayChange)
            {
                SaveDisplayPreferences();
                HideConfirmation();
            }
        }

        public void CancelDialog()
        {
            if (dialogMode == DialogMode.DisplayChange) RevertDisplayChanges();
            else HideConfirmation();
        }

        private void ResetPreferencesToDefaults()
        {
            PlayerPrefs.SetFloat(MasterKey, DefaultMaster);
            PlayerPrefs.SetFloat(MusicKey, DefaultMusic);
            PlayerPrefs.SetFloat(SfxKey, DefaultSfx);
            PlayerPrefs.SetInt(QualityKey, Mathf.Max(0, QualitySettings.names.Length - 1));
            PlayerPrefs.SetInt(VSyncKey, 1);
            PlayerPrefs.SetInt(FrameRateKey, -1);
            Resolution native = Screen.currentResolution;
            PlayerPrefs.SetInt(ResolutionWidthKey, native.width);
            PlayerPrefs.SetInt(ResolutionHeightKey, native.height);
            PlayerPrefs.SetInt(ResolutionRefreshKey, Mathf.RoundToInt((float)native.refreshRateRatio.value));
            PlayerPrefs.SetInt(DisplayModeKey, 2);
            PlayerPrefs.Save();
            QualitySettings.SetQualityLevel(Mathf.Max(0, QualitySettings.names.Length - 1), true);
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
            AudioListener.volume = DefaultMaster;
            ApplyCategoryVolumes(musicSources, baseMusicVolumes, DefaultMusic);
            ApplyCategoryVolumes(sfxSources, baseSfxVolumes, DefaultSfx);
            ApplyDisplay(native, 2);
            HideConfirmation();
            Refresh();
        }

        private void RevertDisplayChanges()
        {
            if (displayCountdown != null)
            {
                StopCoroutine(displayCountdown);
                displayCountdown = null;
            }
            ApplySnapshot(previousDisplay);
            HideConfirmation();
            Refresh();
        }

        private void SaveDisplayPreferences()
        {
            Resolution current = CurrentResolutionFromScreen();
            SaveInt(ResolutionWidthKey, current.width, false);
            SaveInt(ResolutionHeightKey, current.height, false);
            SaveInt(ResolutionRefreshKey, Mathf.RoundToInt((float)current.refreshRateRatio.value), false);
            SaveInt(DisplayModeKey, CurrentDisplayModeIndex(), false);
            PlayerPrefs.Save();
            if (displayCountdown != null) StopCoroutine(displayCountdown);
            displayCountdown = null;
            HideConfirmation();
            Refresh();
        }

        private void ShowConfirmation(string title, string message, string confirm, string cancel)
        {
            if (confirmationTitle != null) confirmationTitle.text = title;
            if (confirmationMessage != null) confirmationMessage.text = message;
            if (confirmationConfirmLabel != null) confirmationConfirmLabel.text = confirm;
            if (confirmationCancelLabel != null) confirmationCancelLabel.text = cancel;
            if (confirmationCountdown != null) confirmationCountdown.text = dialogMode == DialogMode.DisplayChange ? "Reverting in 10 seconds" : string.Empty;
            if (confirmationGroup != null)
            {
                confirmationGroup.gameObject.SetActive(true);
                confirmationGroup.alpha = 1f;
                confirmationGroup.interactable = true;
                confirmationGroup.blocksRaycasts = true;
            }
            if (EventSystem.current != null && confirmationConfirmButton != null)
                EventSystem.current.SetSelectedGameObject(confirmationConfirmButton.gameObject);
        }

        private void HideConfirmation()
        {
            dialogMode = DialogMode.None;
            if (confirmationGroup == null) return;
            confirmationGroup.alpha = 0f;
            confirmationGroup.interactable = false;
            confirmationGroup.blocksRaycasts = false;
            confirmationGroup.gameObject.SetActive(false);
        }

        private DisplaySnapshot CaptureDisplay() => new DisplaySnapshot
        {
            width = Screen.width,
            height = Screen.height,
            refreshRate = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value),
            mode = Screen.fullScreenMode,
            quality = QualitySettings.GetQualityLevel(),
            vsync = QualitySettings.vSyncCount,
            frameRate = Application.targetFrameRate
        };

        private static void ApplySnapshot(DisplaySnapshot snapshot)
        {
            Screen.SetResolution(snapshot.width, snapshot.height, snapshot.mode, ToRefreshRate(snapshot.refreshRate));
            QualitySettings.SetQualityLevel(Mathf.Clamp(snapshot.quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1)), true);
            QualitySettings.vSyncCount = snapshot.vsync;
            Application.targetFrameRate = snapshot.frameRate;
        }

        private static void ApplyDisplay(Resolution resolution, int mode)
        {
            FullScreenMode fullScreenMode = mode == 1 ? FullScreenMode.Windowed
                : mode == 2 ? FullScreenMode.FullScreenWindow : FullScreenMode.ExclusiveFullScreen;
            Screen.SetResolution(resolution.width, resolution.height, fullScreenMode, resolution.refreshRateRatio);
        }

        private static RefreshRate ToRefreshRate(int value) => new RefreshRate
        {
            numerator = (uint)Mathf.Max(1, value),
            denominator = 1
        };

        private Resolution FindResolution(int width, int height, int refresh)
        {
            for (int i = 0; i < availableResolutions.Count; i++)
                if (availableResolutions[i].width == width && availableResolutions[i].height == height
                    && Mathf.RoundToInt((float)availableResolutions[i].refreshRateRatio.value) == refresh) return availableResolutions[i];
            for (int i = 0; i < availableResolutions.Count; i++)
                if (availableResolutions[i].width == width && availableResolutions[i].height == height) return availableResolutions[i];
            return Screen.currentResolution;
        }

        private bool IsValidResolution(Resolution resolution) => resolution.width >= 640 && resolution.height >= 480;

        private int FindCurrentResolutionIndex()
        {
            for (int i = 0; i < availableResolutions.Count; i++)
                if (availableResolutions[i].width == Screen.width && availableResolutions[i].height == Screen.height) return i;
            return Mathf.Clamp(availableResolutions.Count - 1, 0, availableResolutions.Count - 1);
        }

        private Resolution CurrentResolutionFromScreen()
        {
            for (int i = 0; i < availableResolutions.Count; i++)
                if (availableResolutions[i].width == Screen.width && availableResolutions[i].height == Screen.height) return availableResolutions[i];
            return Screen.currentResolution;
        }

        private int CurrentDisplayModeIndex()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed) return 1;
            if (Screen.fullScreenMode == FullScreenMode.FullScreenWindow) return 2;
            return 0;
        }

        private static int FrameRateIndex(int value) => value <= 0 ? 3 : value <= 30 ? 0 : value <= 60 ? 1 : 2;
        private static int NormalizeFrameRate(int value) => value <= 0 ? -1 : value;

        private static void SetSlider(Slider slider, Text valueLabel, float value)
        {
            value = Mathf.Clamp01(value);
            if (slider != null) slider.SetValueWithoutNotify(value);
            UpdatePercentage(valueLabel, value);
        }

        private static void SetDropdown(Dropdown dropdown, int value)
        {
            if (dropdown == null || dropdown.options.Count == 0) return;
            dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, dropdown.options.Count - 1));
            dropdown.RefreshShownValue();
        }

        private static void UpdatePercentage(Text label, float value)
        {
            if (label != null) label.text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        private static void ApplyCategoryVolumes(AudioSource[] sources, float[] baseVolumes, float value)
        {
            if (sources == null) return;
            for (int i = 0; i < sources.Length; i++)
                if (sources[i] != null) sources[i].volume = (i < baseVolumes.Length ? baseVolumes[i] : 1f) * value;
        }

        private static void SaveFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }

        private static void SaveInt(string key, int value, bool flush = true)
        {
            PlayerPrefs.SetInt(key, value);
            if (flush) PlayerPrefs.Save();
        }
    }
}
