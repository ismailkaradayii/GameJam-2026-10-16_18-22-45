using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Volcanic-themed Canvas presentation for the existing rescue mission systems.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(250)]
    public sealed class RescueHUD : MonoBehaviour
    {
        const string MasterKey = "Ashfall.MasterVolume";
        const string MusicKey = "Ashfall.MusicVolume";
        const string SfxKey = "Ashfall.SfxVolume";
        const string SensitivityKey = "Ashfall.MouseSensitivity";
        const string QualityKey = "Ashfall.Quality";
        const string FullscreenKey = "Ashfall.Fullscreen";

        public RescueGameFlow flow;
        public FireRescuePlayer player;
        public FireHoseSystem hose;
        public FireTruckDrive drive;
        public FireTruckWaterCannon cannon;
        public VolcanoEruptionSequence eruption;
        public VolcanoMissionDirector missionDirector;
        public VolcanoSurvivalObjective survivalObjective;
        public CreatureCombatPlayer vitals;
        public EvacuationMission evacuation;
        public Canvas canvas;
        public ContextualInteractionUI contextualInteractions;

        [Header("Clean gameplay presentation")]
        public bool cleanPresentation;
        public CanvasGroup gameplayGroup;
        [Min(280f)] public float objectiveWidth = 400f;
        RectTransform cleanQuest, cleanSpeed, cleanSubtitle, cleanToast;
        string laidOutTitle, laidOutBody, laidOutHint, laidOutSubtitle, laidOutToast;
        float laidOutWidth;
        int layoutScreenWidth, layoutScreenHeight;

        [Header("Integrated gameplay")]
        public Text equipmentText;
        public GameObject equipmentGroup, waterGroup;
        public CanvasGroup objectiveGroup;
        // Keep the existing authoring tools compatible with the volcanic Canvas bindings.
        public VolcanoMissionDirector mission { get => missionDirector; set => missionDirector = value; }
        public Text objectiveDetail { get => questBodyText; set => questBodyText = value; }
        public Text progressText { get => questHintText; set => questHintText = value; }
        public Text resultTitle { get => victoryTitleText; set => victoryTitleText = value; }
        public Text resultDetail { get => victoryStatsText; set => victoryStatsText = value; }
        VolcanoMissionDirector.MissionPhase previousPhase;
        float objectiveChangedAt;
        bool objectiveSeen;

        [Header("Live HUD")]
        public Text objectiveText, waterText, pumpText, speedText, eruptionText, targetText, promptText, toastText, lowWaterText, healthText;
        public VillageIntegrityHUD villageIntegrity;
        public Text questTitleText, questBodyText, questHintText, subtitleText;
        public Image waterFill, targetFill, questFill, impactReticle, lowWaterBackground, healthFill;
        public GameObject targetGroup, lowWaterWarning, healthPanel;

        [Header("Pause and outcome panels")]
        public CanvasGroup pausePanel, victoryPanel, pauseMenuGroup, settingsPanel, howToPlayPanel;
        public Button resumeButton, pauseMenuButton, victoryMenuButton;
        public Button outcomeRestartButton;
        public Button pauseSettingsButton, pauseHowToPlayButton, restartButton, quitButton, settingsBackButton, howToPlayBackButton;
        [SerializeField] AudioClip pauseClickClip;
        public Text victoryTitleText, victoryDetailText, victoryStatsText;
        [Min(.1f)] public float outcomeFadeInSeconds = .65f;

        [Header("Pause settings")]
        public Slider masterSlider, musicSlider, sfxSlider, sensitivitySlider;
        public Text masterValue, musicValue, sfxValue, sensitivityValue;
        public Dropdown qualityDropdown;
        public Toggle fullscreenToggle;
        public AudioSource[] musicSources;

        float[] musicSourceBaseVolumes;
        float toastUntil;
        RescueGameFlow.GameState previousState = (RescueGameFlow.GameState)(-1);
        bool previousSurvivalFinished;
        bool previousOutcomeVisible;
        public bool PauseVisible => pausePanel && pausePanel.alpha > .99f;
        public bool VictoryVisible => victoryPanel && victoryPanel.alpha > .99f;

        void Awake()
        {
            if (!missionDirector) missionDirector = FindFirstObjectByType<VolcanoMissionDirector>();
            if (!survivalObjective) survivalObjective = FindFirstObjectByType<VolcanoSurvivalObjective>();
            if (!evacuation && missionDirector) evacuation = missionDirector.evacuation;
            if (pauseClickClip) pauseClickClip.LoadAudioData();
            // Register the sound before actions that may immediately load another scene.
            foreach (Button button in MenuButtons())
            {
                if (!button) continue;
                Listen(button, PlayMenuClick);
                var pointerAudio = button.GetComponent<Ashfall.MainMenu.AshfallMenuButtonPointerAudio>();
                if (!pointerAudio) pointerAudio = button.gameObject.AddComponent<Ashfall.MainMenu.AshfallMenuButtonPointerAudio>();
                pointerAudio.Configure(pauseClickClip, .8f, this);
            }
            Listen(resumeButton, Resume);
            Listen(pauseMenuButton, ReturnToMenu);
            Listen(victoryMenuButton, ReturnToMenu);
            Listen(outcomeRestartButton, RestartMission);
            Listen(pauseSettingsButton, OpenSettings);
            Listen(pauseHowToPlayButton, OpenHowToPlay);
            Listen(restartButton, RestartMission);
            Listen(quitButton, QuitGame);
            Listen(settingsBackButton, BackToPauseMenu);
            Listen(howToPlayBackButton, BackToPauseMenu);
            if (masterSlider) { masterSlider.onValueChanged.RemoveListener(SetMasterVolume); masterSlider.onValueChanged.AddListener(SetMasterVolume); }
            if (musicSlider) { musicSlider.onValueChanged.RemoveListener(SetMusicVolume); musicSlider.onValueChanged.AddListener(SetMusicVolume); }
            if (sfxSlider) { sfxSlider.onValueChanged.RemoveListener(SetSfxVolume); sfxSlider.onValueChanged.AddListener(SetSfxVolume); }
            if (sensitivitySlider) { sensitivitySlider.onValueChanged.RemoveListener(SetSensitivity); sensitivitySlider.onValueChanged.AddListener(SetSensitivity); }
            if (qualityDropdown) { qualityDropdown.onValueChanged.RemoveListener(SetQuality); qualityDropdown.onValueChanged.AddListener(SetQuality); }
            if (fullscreenToggle) { fullscreenToggle.onValueChanged.RemoveListener(SetFullscreen); fullscreenToggle.onValueChanged.AddListener(SetFullscreen); }

            musicSourceBaseVolumes = CacheVolumes(musicSources);
            ApplySavedSettings();
            ConfigureReadableHudText();
            if (VolcanicHudTheme.Current) VolcanicHudTheme.Current.Apply(this);
            VolcanicHudTheme.ConfigurePauseMenu(this);
            HideTransientCenterNotifications();
            SetPanel(pausePanel, false);
            SetPanel(victoryPanel, false);
            SetPanel(settingsPanel, false);
            SetPanel(howToPlayPanel, false);
            SetPanel(pauseMenuGroup, true);
        }

        void ConfigureReadableHudText()
        {
            FitText(questTitleText, 24, cleanPresentation ? 32 : 25, true);
            FitText(questBodyText, 20, cleanPresentation ? 24 : 20, true);
            FitText(questHintText, 18, cleanPresentation ? 21 : 19, true);
            if (cleanPresentation)
            {
                // The clean layout measures wrapped text and grows the card to fit it.
                if (questTitleText) questTitleText.resizeTextForBestFit = false;
                if (questBodyText) questBodyText.resizeTextForBestFit = false;
                if (questHintText) questHintText.resizeTextForBestFit = false;
            }
            if (questTitleText)
                FitText(questTitleText.transform.parent.Find("QuestKicker")?.GetComponent<Text>(), 14, 17, false);
            FitText(objectiveText, 19, cleanPresentation ? 24 : 19, false);
            FitText(speedText, 15, 19, false);
            FitText(eruptionText, 24, cleanPresentation ? 30 : 27, false);

            FitText(victoryTitleText, 24, 34, true);
            FitText(victoryDetailText, 17, 22, true);
            FitText(victoryStatsText, 17, 21, true);
            FitButtonText(outcomeRestartButton, 17, 22);
            FitButtonText(victoryMenuButton, 17, 22);

            if (!villageIntegrity) return;
            FitText(villageIntegrity.percentageText, 18, 25, false);
            FitText(villageIntegrity.statusText, 13, 17, false);
            if (villageIntegrity.houseLabels != null)
                foreach (Text label in villageIntegrity.houseLabels) FitText(label, 11, 16, false);
            if (villageIntegrity.defenseGroup)
                FitText(villageIntegrity.defenseGroup.transform.Find("Title")?.GetComponent<Text>(), 17, 24, false);
        }

        static void FitButtonText(Button button, int minimum, int preferred)
        {
            if (button) FitText(button.GetComponentInChildren<Text>(true), minimum, preferred, false);
        }

        static void FitText(Text label, int minimum, int preferred, bool wrap)
        {
            if (!label) return;
            label.fontSize = preferred;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = minimum;
            label.resizeTextMaxSize = preferred;
            label.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
        }

        void HideTransientCenterNotifications()
        {
            SetTextContainerActive(toastText, false);
            SetTextContainerActive(subtitleText, false);
        }

        static void SetTextContainerActive(Text label, bool active)
        {
            if (!label) return;
            Transform container = label.transform.parent;
            (container ? container.gameObject : label.gameObject).SetActive(active);
        }

        void OnEnable() { if (flow) flow.ProgressChanged += OnProgress; }
        void OnDisable() { if (flow) flow.ProgressChanged -= OnProgress; }

        void OnDestroy()
        {
            foreach (Button button in MenuButtons()) Unlisten(button, PlayMenuClick);
            Unlisten(resumeButton, Resume); Unlisten(pauseMenuButton, ReturnToMenu); Unlisten(victoryMenuButton, ReturnToMenu);
            Unlisten(outcomeRestartButton, RestartMission);
            Unlisten(pauseSettingsButton, OpenSettings); Unlisten(pauseHowToPlayButton, OpenHowToPlay);
            Unlisten(restartButton, RestartMission); Unlisten(quitButton, QuitGame);
            Unlisten(settingsBackButton, BackToPauseMenu); Unlisten(howToPlayBackButton, BackToPauseMenu);
            if (masterSlider) masterSlider.onValueChanged.RemoveListener(SetMasterVolume);
            if (musicSlider) musicSlider.onValueChanged.RemoveListener(SetMusicVolume);
            if (sfxSlider) sfxSlider.onValueChanged.RemoveListener(SetSfxVolume);
            if (sensitivitySlider) sensitivitySlider.onValueChanged.RemoveListener(SetSensitivity);
            if (qualityDropdown) qualityDropdown.onValueChanged.RemoveListener(SetQuality);
            if (fullscreenToggle) fullscreenToggle.onValueChanged.RemoveListener(SetFullscreen);
        }

        Button[] MenuButtons() => new[] { resumeButton, pauseMenuButton, pauseSettingsButton,
            pauseHowToPlayButton, restartButton, quitButton, settingsBackButton, howToPlayBackButton,
            victoryMenuButton, outcomeRestartButton };

        void PlayMenuClick()
        {
            if (isActiveAndEnabled) Ashfall.MainMenu.AshfallMenuAudioFeedback.Play(pauseClickClip, .8f);
        }

        void Resume() { if (flow) flow.Resume(); }
        void ReturnToMenu() { if (flow) flow.ReturnToMainMenu(); }
        void RestartMission()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path)) SceneManager.LoadScene(scene.path);
            else SceneManager.LoadScene(scene.name);
        }
        void QuitGame() { Application.Quit(); }
        void OpenSettings() { SetPanel(pauseMenuGroup, false); SetPanel(howToPlayPanel, false); SetPanel(settingsPanel, true); }
        void OpenHowToPlay() { SetPanel(pauseMenuGroup, false); SetPanel(settingsPanel, false); SetPanel(howToPlayPanel, true); }
        void BackToPauseMenu() { SetPanel(settingsPanel, false); SetPanel(howToPlayPanel, false); SetPanel(pauseMenuGroup, true); }

        void OnProgress(int saved, int total)
        {
            if (toastText) toastText.text = "House saved  ·  " + saved + " / " + total;
            toastUntil = Time.unscaledTime + 3.2f;
        }

        void LateUpdate()
        {
            if (!flow || !player || !hose) return;
            if (!vitals) vitals = survivalObjective ? survivalObjective.vitals : FindFirstObjectByType<CreatureCombatPlayer>();
            if (canvas) canvas.enabled = !VolcanoCinematic.IsPlayingAny || flow.BlocksGameplay;
            bool playing = flow.State == RescueGameFlow.GameState.Playing;
            bool openingShot = missionDirector && missionDirector.story && missionDirector.story.OwnsMovement;
            if (gameplayGroup) gameplayGroup.alpha = playing && !VolcanoCinematic.IsPlayingAny && !openingShot ? 1f : 0f;
            bool showMessage = playing && evacuation && evacuation.MessageRemaining > 0f;
            int standing = 0, burning = 0;
            foreach (var house in flow.targetBuildings) if (house) { if (!house.IsBurnedOut) standing++; if (house.IsBurning) burning++; }
            if (objectiveText) objectiveText.text = (cleanPresentation ? "Houses   " : "Houses standing   ") + standing + " / " + Mathf.Max(1, flow.TargetCount);
            if (missionDirector && (!objectiveSeen || previousPhase != missionDirector.Phase))
            {
                if (objectiveSeen && !missionDirector.EruptionTriggered && missionDirector.Phase != VolcanoMissionDirector.MissionPhase.Weapon)
                { if (toastText) toastText.text = "Objective complete"; toastUntil = Time.unscaledTime + 2.2f; }
                previousPhase = missionDirector.Phase; objectiveSeen = true; objectiveChangedAt = Time.unscaledTime;
            }
            if (objectiveGroup) objectiveGroup.alpha = Mathf.Lerp(.3f, 1f, Mathf.Clamp01((Time.unscaledTime - objectiveChangedAt) / .35f));
            bool showWater = playing && (player.IsDriving || hose.IsDeployed);
            if (waterGroup) waterGroup.SetActive(showWater);
            else
            {
                if (waterText) waterText.gameObject.SetActive(showWater);
                if (pumpText) pumpText.gameObject.SetActive(showWater);
                if (waterFill) waterFill.transform.parent.gameObject.SetActive(showWater);
                var label = waterText ? waterText.transform.parent.Find("WaterLabel") : null;
                if (label) label.gameObject.SetActive(showWater);
            }
            if (equipmentGroup) equipmentGroup.SetActive(playing && player.HasPistol && !player.IsDriving && (!cleanPresentation || !hose.IsCarried));
            if (cleanPresentation && equipmentGroup)
            {
                var equipmentRect = equipmentGroup.transform as RectTransform;
                Vector2 position = new Vector2(-28, showWater ? 194 : 60);
                if (equipmentRect && equipmentRect.anchoredPosition != position) equipmentRect.anchoredPosition = position;
            }
            if (equipmentText && player.pistol) equipmentText.text = "1  PISTOL  " + (player.pistol.Equipped ? player.pistol.Ammo + " / " + player.pistol.reserveAmmo : "[HOLSTERED]") +
                (player.pistol.IsReloading ? "  RELOADING " + Mathf.RoundToInt(player.pistol.ReloadProgress * 100) + "%" : "");
            if (cleanPresentation && equipmentText && player.pistol) equipmentText.text = "1   PISTOL    " +
                (player.pistol.IsReloading ? "RELOADING" : player.pistol.Equipped ? player.pistol.Ammo + " / " + player.pistol.reserveAmmo : "HOLSTERED");
            if (waterText) waterText.text = Mathf.CeilToInt(hose.TankLitres) + " / " + Mathf.RoundToInt(hose.tankCapacityLitres) + " L";
            if (waterFill) waterFill.fillAmount = hose.Tank01;
            if (pumpText) pumpText.text = Ashfall.UI.AshfallTypography.NaturalCase(player.WaterStatus);
            if (pumpText && drive && drive.Thermal && drive.Thermal.Integrity01 < .999f)
                pumpText.text = (drive.IsWrecked ? "TRUCK INOPERABLE" : drive.Thermal.IsBurning ? "TRUCK ON FIRE!" : "Truck damaged") +
                    "  ·  Integrity " + Mathf.CeilToInt(drive.Thermal.Integrity01 * 100f) +
                    "%  ·  Pump " + Mathf.RoundToInt(drive.Thermal.PumpEfficiency * 100f) + "%";
            if (pumpText && player.IsDriving && drive && drive.Thermal && drive.Thermal.IsBurning && !drive.IsWrecked)
                pumpText.text += drive.Thermal.InHotLava ? "  ·  LEAVE THE LAVA!" :
                    drive.Thermal.AutomaticSuppressionActive ? "  ·  Automatic suppression active" :
                    hose && hose.TankLitres > 0f ? "  ·  Hold X to extinguish" : "  ·  Water required to extinguish";
            if (speedText) speedText.text = player.IsDriving && drive ? Mathf.RoundToInt(drive.SpeedKph) + " km/h" : "On foot";
            bool showHealth = playing && vitals && vitals.Health < vitals.maximumHealth - .1f;
            if (healthPanel && healthPanel.activeSelf != showHealth) healthPanel.SetActive(showHealth);
            if (showHealth)
            {
                if (healthFill) healthFill.fillAmount = Mathf.Clamp01(vitals.Health / vitals.maximumHealth);
                if (healthText) healthText.text = Mathf.CeilToInt(vitals.Health) + " / " + Mathf.RoundToInt(vitals.maximumHealth);
            }
            UpdateQuest();
            if (lowWaterWarning) lowWaterWarning.SetActive(playing && player.IsDriving && hose.TankLitres < 200f);
            bool empty = hose.TankLitres <= 0f;
            if (lowWaterText) lowWaterText.text = empty ? "Tank empty  ·  refill at lake" : "Low water  ·  refill at lake";
            if (lowWaterBackground) lowWaterBackground.color = empty ? new Color(.5f, .065f, .035f, .97f) : new Color(.4f, .12f, .04f, .95f);
            if (promptText)
            {
                promptText.text = playing && (!contextualInteractions || !contextualInteractions.isActiveAndEnabled)
                    ? Ashfall.UI.AshfallTypography.NaturalCase(player.InteractionPrompt) : "";
                promptText.transform.parent.gameObject.SetActive(!string.IsNullOrWhiteSpace(promptText.text));
            }
            // Keep generic progress toasts suppressed; only authored inner thoughts use subtitles.
            SetTextContainerActive(toastText, false);
            var story = missionDirector ? missionDirector.story : null;
            bool rescueLine = evacuation && evacuation.groupRescue && evacuation.MessageRemaining > 0f && !string.IsNullOrEmpty(evacuation.CurrentMessage);
            bool showThought = playing && !VolcanoCinematic.IsPlayingAny && (rescueLine || story && !string.IsNullOrEmpty(story.CurrentLine));
            SetTextContainerActive(subtitleText, showThought);
            if (showThought && subtitleText)
            {
                subtitleText.text = rescueLine ? evacuation.CurrentMessage : story.CurrentLine;
            }
            UpdateEruption(); UpdateTarget(playing);
            if (cleanPresentation)
            {
                RefreshCleanLayout();
                if (cleanSpeed) cleanSpeed.gameObject.SetActive(playing && player.IsDriving);
                if (objectiveText) objectiveText.transform.parent.gameObject.SetActive(playing && (!missionDirector || !missionDirector.EruptionTriggered));
            }
            bool survivalFinished = survivalObjective && survivalObjective.IsFinished;
            bool showOutcome = !flow.IsVictoryCinematic && (flow.HasResult || survivalFinished) && (!flow.HasResult || flow.ResultReady);
            PresentOutcome(showOutcome);
            if (showOutcome) UpdateOutcome(survivalFinished);

            if (previousState != flow.State || previousSurvivalFinished != survivalFinished || previousOutcomeVisible != showOutcome)
            {
                previousState = flow.State;
                SetPanel(pausePanel, flow.State == RescueGameFlow.GameState.Paused);
                SetPanel(pauseMenuGroup, flow.State == RescueGameFlow.GameState.Paused);
                SetPanel(settingsPanel, false); SetPanel(howToPlayPanel, false);
                previousSurvivalFinished = survivalFinished;
                previousOutcomeVisible = showOutcome;
                var selection = flow.State == RescueGameFlow.GameState.Paused ? resumeButton : showOutcome && (flow.State == RescueGameFlow.GameState.Lost || survivalObjective && survivalObjective.State == VolcanoSurvivalObjective.MissionState.Lost) ? outcomeRestartButton : showOutcome ? victoryMenuButton : null;
                if (selection && EventSystem.current) EventSystem.current.SetSelectedGameObject(selection.gameObject);
            }
        }

        void PresentOutcome(bool visible)
        {
            if (!victoryPanel) return;
            if (visible && !victoryPanel.gameObject.activeSelf) victoryPanel.gameObject.SetActive(true);
            victoryPanel.alpha = visible && flow && flow.HasResult ? Mathf.SmoothStep(0, 1, flow.ResultRevealElapsed / Mathf.Max(.1f, outcomeFadeInSeconds)) :
                Mathf.MoveTowards(victoryPanel.alpha, visible ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(.1f, outcomeFadeInSeconds));
            victoryPanel.interactable = visible && victoryPanel.alpha >= .99f;
            victoryPanel.blocksRaycasts = victoryPanel.interactable;
            if (!visible && victoryPanel.alpha <= 0 && victoryPanel.gameObject.activeSelf) victoryPanel.gameObject.SetActive(false);
        }

        /// <summary>Fits the existing objective and notifications to their real content.</summary>
        public void RefreshCleanLayout()
        {
            if (!cleanPresentation || !questTitleText || !questBodyText || !questHintText || !canvas) return;
            if (!cleanQuest) cleanQuest = questTitleText.transform.parent as RectTransform;
            if (!cleanSpeed && speedText) cleanSpeed = speedText.transform.parent as RectTransform;
            if (!cleanSubtitle && subtitleText) cleanSubtitle = subtitleText.transform.parent as RectTransform;
            if (!cleanToast && toastText) cleanToast = toastText.transform.parent as RectTransform;
            if (VolcanicHudTheme.Current)
            {
                VolcanicHudTheme.Current.Layout(this);
                return;
            }
            if (layoutScreenWidth != Screen.width || layoutScreenHeight != Screen.height)
            {
                layoutScreenWidth = Screen.width; layoutScreenHeight = Screen.height;
                laidOutTitle = laidOutBody = laidOutHint = laidOutSubtitle = laidOutToast = null;
            }
            float canvasWidth = ((RectTransform)canvas.transform).rect.width;
            float width = Mathf.Min(objectiveWidth, canvasWidth * .32f);
            if (laidOutTitle != questTitleText.text || laidOutBody != questBodyText.text || laidOutHint != questHintText.text || !Mathf.Approximately(width, laidOutWidth))
            {
                laidOutTitle = questTitleText.text; laidOutBody = questBodyText.text; laidOutHint = questHintText.text; laidOutWidth = width;
                cleanQuest.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                float textWidth = width - 48f;
                var kicker = cleanQuest.Find("QuestKicker") as RectTransform;
                if (kicker) kicker.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
                float y = 48f;
                y += PlaceText(questTitleText, 24, y, textWidth, 38) + 10f;
                y += PlaceText(questBodyText, 24, y, textWidth, 30);
                bool progress = !string.IsNullOrEmpty(questHintText.text);
                questHintText.gameObject.SetActive(progress);
                if (progress) y += 14f + PlaceText(questHintText, 24, y + 14f, textWidth, 26);
                if (questFill && questFill.transform.parent is RectTransform track)
                {
                    track.gameObject.SetActive(progress);
                    if (progress) { track.anchoredPosition = new Vector2(24, -y - 14f); track.sizeDelta = new Vector2(textWidth, 3); y += 17f; }
                }
                cleanQuest.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y + 24f);
            }
            if (cleanSubtitle && laidOutSubtitle != subtitleText.text)
            {
                laidOutSubtitle = subtitleText.text;
                float subtitleWidth = Mathf.Min(800, canvasWidth - 96);
                cleanSubtitle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, subtitleWidth);
                cleanSubtitle.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, PlaceText(subtitleText, 24, 18, subtitleWidth - 48, 32) + 36);
            }
            if (cleanToast && laidOutToast != toastText.text)
            {
                laidOutToast = toastText.text;
                cleanToast.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, PlaceText(toastText, 24, 16, 412, 28) + 32);
            }
        }
        static float PlaceText(Text text, float x, float y, float width, float minimumHeight)
        {
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            float height = Mathf.Max(minimumHeight, Mathf.Ceil(text.preferredHeight));
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            return height;
        }

        void UpdateOutcome(bool survivalFinished)
        {
            if (flow.State == RescueGameFlow.GameState.Lost &&
                (flow.DefeatReason == RescueGameFlow.LossReason.AllVillagersLost || flow.DefeatReason == RescueGameFlow.LossReason.VillageBurned))
            {
                bool villageBurned = flow.DefeatReason == RescueGameFlow.LossReason.VillageBurned;
                OutcomeTitleGlow.SetResult(victoryTitleText, villageBurned ? "VILLAGE DESTROYED" : "ALL VILLAGERS LOST", false);
                if (victoryDetailText) victoryDetailText.text = villageBurned ? "All houses burned down. The village was lost." : "All villagers were lost in the fire.";
                int standing = 0;
                foreach (var house in flow.targetBuildings) if (house && !house.IsBurnedOut) standing++;
                if (victoryStatsText) victoryStatsText.text = "Villagers saved   " + (evacuation ? evacuation.RescuedCount + " / " + evacuation.TotalCount : "0") +
                    "\nVillagers lost   " + (evacuation ? evacuation.DeadCount : 0) + "\nHouses standing   " + standing + " / " + flow.TargetCount;
                if (outcomeRestartButton) outcomeRestartButton.gameObject.SetActive(true);
                if (victoryMenuButton) victoryMenuButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(400, -340);
                return;
            }
            if (missionDirector)
            {
                bool won = flow.State == RescueGameFlow.GameState.Won;
                int saved = 0;
                foreach (var house in flow.targetBuildings) if (house && !house.IsBurnedOut) saved++;
                OutcomeTitleGlow.SetResult(victoryTitleText, "Final Result: " + (won ? "SUCCESS" : "FAILED"), won);
                if (victoryDetailText) victoryDetailText.text = won ? (evacuation && evacuation.DeadCount > 0 ? "The spirits were stopped. " + evacuation.DeadCount + " villagers were lost." : "The spirits were stopped. The village is safe.") :
                    saved == 0 ? "All houses burned down. Extinguish fires before the buildings collapse." : "Try again to save the village.";
                if (victoryStatsText) victoryStatsText.text = "Saved Villagers   " + (evacuation ? evacuation.RescuedCount + " / " + evacuation.TotalCount : "0") +
                    "\nVillagers Lost   " + (evacuation ? evacuation.DeadCount : 0) +
                    "\nHouses Saved   " + saved + " / " + flow.TargetCount + "\nFires Extinguished   " + flow.RescuedCount;
                if (outcomeRestartButton) outcomeRestartButton.gameObject.SetActive(true);
                return;
            }
            bool survivalWon = survivalFinished && survivalObjective.State == VolcanoSurvivalObjective.MissionState.Won;
            OutcomeTitleGlow.SetResult(victoryTitleText, survivalFinished ? (survivalWon ? "Area secure" : "Mission failed") : "Town secure", !survivalFinished || survivalWon);
            if (victoryDetailText) victoryDetailText.text = survivalFinished
                ? (survivalWon ? "The spirits were stopped and you returned to the truck." : "You ran out of health. Prepare and defend the town again.")
                : "All five houses were saved. The evacuees are safe.";
            if (victoryStatsText)
            {
                if (survivalFinished && survivalObjective.encounter)
                {
                    var encounter = survivalObjective.encounter;
                    victoryStatsText.text = "Spirits defeated  " + encounter.DefeatedCount + " / " + encounter.PlannedCount +
                        "     ·     Wave  " + encounter.WaveNumber + " / " + encounter.WaveCount;
                }
                else victoryStatsText.text = "Houses saved  " + (flow ? flow.RescuedCount : 0) + " / " + (flow ? flow.TargetCount : 0);
            }
            if (outcomeRestartButton) outcomeRestartButton.gameObject.SetActive(survivalFinished);
            if (victoryMenuButton) victoryMenuButton.GetComponent<RectTransform>().anchoredPosition = survivalFinished ? new Vector2(400, -340) : new Vector2(170, -340);
        }

        void UpdateQuest()
        {
            if (!questTitleText || !questBodyText || !questHintText || !questFill) return;
            if (missionDirector)
            {
                var mission = missionDirector;
                var encounter = mission.encounter;
                questTitleText.text = mission.ObjectiveTitle;
                questBodyText.text = mission.ObjectiveDetail;
                if (evacuation && (mission.EruptionTriggered || evacuation.DeadCount > 0))
                    questBodyText.text += "\nSheltered: " + evacuation.RescuedCount + "/" + evacuation.TotalCount + "  ·  Lost: " + evacuation.DeadCount + "  ·  Outside: " + evacuation.RemainingCount;
                questHintText.text = mission.EruptionTriggered ? "Wave " + Mathf.Max(1, encounter ? encounter.WaveNumber : 0) + "/" + (encounter ? encounter.WaveCount : 0) +
                    "  |  Spirits remaining: " + (encounter ? encounter.PlannedCount - encounter.SpawnedCount + encounter.AliveCount : 0) :
                    mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate && evacuation ? evacuation.groupRescue ? evacuation.GroupStatus : "Villagers Evacuated: " + evacuation.RescuedCount + "/" + evacuation.TotalCount + "  |  Called: " + evacuation.CalledCount :
                    mission.Phase == VolcanoMissionDirector.MissionPhase.Refill ? "Water tank: " + Mathf.RoundToInt(hose.Tank01 * 100) + "%" : "";
                questFill.fillAmount = mission.EruptionTriggered ? encounter && encounter.PlannedCount > 0 ? Mathf.Clamp01((float)(encounter.SpawnedCount - encounter.AliveCount) / encounter.PlannedCount) : 0 :
                    mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate && evacuation ? evacuation.groupRescue ? evacuation.GroupProgress : (float)evacuation.RescuedCount / Mathf.Max(1, evacuation.TotalCount) :
                    mission.Phase == VolcanoMissionDirector.MissionPhase.Refill ? hose.Tank01 : mission.PreparationComplete ? 1f : 0f;
                return;
            }
            VolcanoMissionDirector.MissionPhase phase = missionDirector ? missionDirector.Phase : VolcanoMissionDirector.MissionPhase.Defend;
            if (evacuation && !evacuation.IsComplete)
            {
                if (evacuation.groupRescue)
                {
                    questTitleText.text = evacuation.GroupObjective;
                    questBodyText.text = evacuation.GroupDetail;
                    questHintText.text = evacuation.GroupStatus;
                    questFill.fillAmount = evacuation.GroupProgress;
                    return;
                }
                questTitleText.text = "Evacuate the villagers";
                questBodyText.text = "Sheltered  " + evacuation.RescuedCount + " / " + evacuation.TotalCount +
                    "     Called  " + evacuation.CalledCount + " / " + evacuation.TotalCount + "\n" +
                    (evacuation.CalledCount < evacuation.TotalCount ? "The alarm is sounding. Press Q to call the villagers." : "Lead the villagers to the shelter.");
                questHintText.text = player.IsDriving ? (player.contextualInteractions ? player.contextualInteractions.Label(InteractionInput.ExitVehicle) : "E") + "  Exit vehicle" : evacuation.CooldownRemaining > 0f ? "Q  Call again  ·  " + Mathf.CeilToInt(evacuation.CooldownRemaining) + " sec" : "Q  Buraya gelin!  ·  " + evacuation.DestinationHintForHud;
                questFill.fillAmount = evacuation.TotalCount > 0 ? (float)evacuation.RescuedCount / evacuation.TotalCount : 0f;
                return;
            }
            if (phase == VolcanoMissionDirector.MissionPhase.Refill)
            {
                questTitleText.text = "Fill the water tank";
                questBodyText.text = "Evacuation complete  ·  " + (evacuation ? evacuation.TotalCount : 7) + " / " + (evacuation ? evacuation.TotalCount : 7) + " safe.\nLower the hose into the lake and start the pump.";
                questHintText.text = hose.IsDeployed ? "P  Toggle pump   ·   R  Retrieve hose" : "E  Exit vehicle   ·   E  Take hose";
                questFill.fillAmount = hose.Tank01;
                return;
            }
            if (phase <= VolcanoMissionDirector.MissionPhase.Cinematic)
            {
                questTitleText.text = "The volcano awakens";
                questBodyText.text = eruption && eruption.Stage == VolcanoEruptionSequence.EruptionStage.Warning ? "The ground is shaking. An eruption is near." : "Evacuation complete. Prepare to defend the town.";
                questHintText.text = eruption && eruption.Stage == VolcanoEruptionSequence.EruptionStage.Waiting ? "Eruption in  " + Mathf.CeilToInt(eruption.Countdown) + " sec" : "Leave the danger zone.";
                questFill.fillAmount = flow.RescuedCount / 5f;
                return;
            }
            int burning, lost; float nextCollapse;
            CountHouses(out burning, out lost, out nextCollapse);
            questTitleText.text = "Defend the town";
            questBodyText.text = "Houses saved  " + flow.RescuedCount + " / " + Mathf.Max(1, flow.TargetCount) + "\n" +
                (burning > 0 ? "Burning: " + burning + "  ·  Lost: " + lost + "  ·  Next collapse: " + Mathf.CeilToInt(nextCollapse) + " sec" : "Cool burning houses and approaching lava with water.");
            questHintText.text = player.IsDriving ? "Aim with mouse   ·   Hold right click to spray" : "E  Enter fire truck   ·   E  Interact";
            questFill.fillAmount = flow.TargetCount > 0 ? (float)flow.RescuedCount / flow.TargetCount : 0f;
        }

        static void CountHouses(out int burning, out int lost, out float nextCollapse)
        {
            burning = 0; lost = 0; nextCollapse = float.PositiveInfinity;
            foreach (var house in BurnableBuilding.ActiveBuildings)
                if (house) { if (house.IsBurnedOut) lost++; else if (house.IsBurning) { burning++; nextCollapse = Mathf.Min(nextCollapse, house.SecondsUntilCollapse); } }
        }

        void UpdateEruption()
        {
            bool afterEruption = missionDirector ? missionDirector.EruptionTriggered : eruption &&
                (eruption.Stage == VolcanoEruptionSequence.EruptionStage.Erupting || eruption.Stage == VolcanoEruptionSequence.EruptionStage.Flowing);
            if (villageIntegrity)
            {
                villageIntegrity.Refresh(afterEruption);
                if (afterEruption) return;
            }
            if (!eruptionText || !eruption) return;
            if (missionDirector)
            {
                int seconds = Mathf.CeilToInt(missionDirector.PreparationRemaining);
                eruptionText.text = missionDirector.EruptionTriggered ? "VILLAGE DEFENSE" : "VOLCANO ERUPTION IN: " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
                eruptionText.color = !missionDirector.EruptionTriggered && seconds <= 30 ? Color.Lerp(new Color(1, .24f, .12f), Color.white, .2f + .2f * Mathf.Sin(Time.unscaledTime * 6)) : Ashfall.UI.AshfallTypography.Paper;
                return;
            }
            if (survivalObjective && survivalObjective.encounter && survivalObjective.encounter.enableWaves &&
                (survivalObjective.State == VolcanoSurvivalObjective.MissionState.Fighting || survivalObjective.State == VolcanoSurvivalObjective.MissionState.ReturnToTruck))
            {
                var encounter = survivalObjective.encounter;
                eruptionText.text = survivalObjective.State == VolcanoSurvivalObjective.MissionState.ReturnToTruck
                    ? "Final step  ·  Return to the truck\nPress E at the driver's door"
                    : "Spirit wave  " + Mathf.Max(1, encounter.WaveNumber) + " / " + encounter.WaveCount +
                      "\nActive " + encounter.AliveCount + "  ·  Defeated " + encounter.DefeatedCount + " / " + encounter.PlannedCount;
                return;
            }
            switch (eruption.Stage)
            {
                case VolcanoEruptionSequence.EruptionStage.Waiting:
                    eruptionText.text = "Volcano  ·  preparation\n" + (missionDirector && missionDirector.Phase == VolcanoMissionDirector.MissionPhase.Refill ? "Fill the tank" : "Evacuation in progress"); break;
                case VolcanoEruptionSequence.EruptionStage.Warning: eruptionText.text = "The ground is shaking\nEruption approaching"; break;
                case VolcanoEruptionSequence.EruptionStage.Erupting: eruptionText.text = "The volcano has erupted\nProtect the town"; break;
                default: eruptionText.text = "Lava advancing\nExtinguish burning houses"; break;
            }
        }

        void UpdateTarget(bool playing)
        {
            var target = cannon ? cannon.PredictedTarget : null;
            bool aiming = playing && player.IsDriving && cannon;
            bool contextualProgress = contextualInteractions && contextualInteractions.HasVisiblePrompt && contextualInteractions.CurrentTarget &&
                contextualInteractions.CurrentTarget.kind == ContextualInteractionKind.BuildingFire;
            bool showTarget = aiming && target && !contextualProgress;
            if (targetGroup) targetGroup.SetActive(showTarget);
            if (showTarget)
            {
                if (targetText) targetText.text = target.IsRescued ? "House safe" : target.IsBurning ?
                    (cannon.IsSpraying && cannon.HitFire == target ? "Extinguishing fire" : "House on fire  ·  progress") : "House is not burning";
                if (targetFill) targetFill.fillAmount = target.IsRescued ? 1f : target.IsBurning ? 1f - target.Heat : 0f;
            }
            if (!impactReticle) return;
            bool visible = aiming && cannon.HasPredictedImpact && player.viewCamera && canvas;
            Vector3 screen = visible ? player.viewCamera.WorldToScreenPoint(cannon.AimPoint) : Vector3.zero;
            visible &= screen.z > 0 && player.viewCamera && player.viewCamera.pixelRect.Contains(new Vector2(screen.x, screen.y));
            impactReticle.gameObject.SetActive(visible);
            if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, screen,
                canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out Vector2 local))
            {
                impactReticle.rectTransform.anchoredPosition = local;
                Color reticleColor = cannon.IsAimObstructed ? new Color(1f, .3f, .2f, .95f) : target && target.IsBurning ? new Color(1f, .65f, .25f, .95f) : new Color(.6f, .87f, 1f, .85f);
                impactReticle.color = reticleColor;
                if (impactReticle.transform.childCount > 0)
                {
                    var vertical = impactReticle.transform.GetChild(0).GetComponent<Image>();
                    if (vertical) vertical.color = reticleColor;
                }
            }
        }

        void ApplySavedSettings()
        {
            float master = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
            float music = Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, .75f));
            float sfx = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxKey, 1f));
            float sensitivity = Mathf.Clamp(player ? PlayerPrefs.GetFloat(SensitivityKey, player.mouseSensitivity) : .12f, .02f, .3f);
            AudioListener.volume = master;
            ApplyMusic(music);
            if (player) player.mouseSensitivity = sensitivity;
            int quality = Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, QualitySettings.GetQualityLevel()), 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            QualitySettings.SetQualityLevel(quality, true);
            bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            if (masterSlider) masterSlider.SetValueWithoutNotify(master);
            if (musicSlider) musicSlider.SetValueWithoutNotify(music);
            if (sfxSlider) sfxSlider.SetValueWithoutNotify(sfx);
            if (sensitivitySlider) sensitivitySlider.SetValueWithoutNotify(sensitivity);
            if (qualityDropdown && qualityDropdown.options.Count > 0) qualityDropdown.SetValueWithoutNotify(Mathf.Min(quality, qualityDropdown.options.Count - 1));
            if (fullscreenToggle) fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
            SetValue(masterValue, master * 100f, "%"); SetValue(musicValue, music * 100f, "%");
            SetValue(sfxValue, sfx * 100f, "%"); SetValue(sensitivityValue, sensitivity, "");
        }

        public void SetMasterVolume(float value)
        { value = Mathf.Clamp01(value); AudioListener.volume = value; PlayerPrefs.SetFloat(MasterKey, value); PlayerPrefs.Save(); SetValue(masterValue, value * 100f, "%"); }
        public void SetMusicVolume(float value)
        { value = Mathf.Clamp01(value); ApplyMusic(value); PlayerPrefs.SetFloat(MusicKey, value); PlayerPrefs.Save(); SetValue(musicValue, value * 100f, "%"); }
        public void SetSfxVolume(float value)
        { value = Mathf.Clamp01(value); PlayerPrefs.SetFloat(SfxKey, value); PlayerPrefs.Save(); SetValue(sfxValue, value * 100f, "%"); }
        public void SetSensitivity(float value)
        { value = Mathf.Clamp(value, .02f, .3f); if (player) player.mouseSensitivity = value; PlayerPrefs.SetFloat(SensitivityKey, value); PlayerPrefs.Save(); SetValue(sensitivityValue, value, ""); }
        public void SetQuality(int value)
        { if (QualitySettings.names.Length == 0) return; value = Mathf.Clamp(value, 0, QualitySettings.names.Length - 1); QualitySettings.SetQualityLevel(value, true); PlayerPrefs.SetInt(QualityKey, value); PlayerPrefs.Save(); }
        public void SetFullscreen(bool value)
        { Screen.fullScreen = value; PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); PlayerPrefs.Save(); }

        void ApplyMusic(float value)
        {
            if (musicSources == null || musicSourceBaseVolumes == null) return;
            for (int i = 0; i < musicSources.Length; i++) if (musicSources[i]) musicSources[i].volume = musicSourceBaseVolumes[i] * value;
        }
        static float[] CacheVolumes(AudioSource[] sources)
        {
            if (sources == null) return System.Array.Empty<float>();
            var result = new float[sources.Length];
            for (int i = 0; i < sources.Length; i++) result[i] = sources[i] ? sources[i].volume : 0f;
            return result;
        }
        static void SetValue(Text label, float value, string suffix) { if (label) label.text = (suffix == "%" ? Mathf.RoundToInt(value).ToString() : value.ToString("0.00")) + suffix; }
        static void SetPanel(CanvasGroup group, bool visible)
        {
            if (!group) return;
            if (group.gameObject.activeSelf != visible) group.gameObject.SetActive(visible);
            group.alpha = visible ? 1f : 0f;
            group.interactable = group.blocksRaycasts = visible;
        }
        static void Listen(Button button, UnityEngine.Events.UnityAction action) { if (button) { button.onClick.RemoveListener(action); button.onClick.AddListener(action); } }
        static void Unlisten(Button button, UnityEngine.Events.UnityAction action) { if (button) button.onClick.RemoveListener(action); }
    }
}
