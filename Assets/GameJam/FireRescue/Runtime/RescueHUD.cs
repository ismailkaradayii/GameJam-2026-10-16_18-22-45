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

        [Header("Live HUD")]
        public Text objectiveText, waterText, pumpText, speedText, eruptionText, targetText, promptText, toastText, lowWaterText, healthText;
        public Text questTitleText, questBodyText, questHintText, subtitleText;
        public Image waterFill, targetFill, questFill, impactReticle, lowWaterBackground, healthFill;
        public GameObject targetGroup, lowWaterWarning, healthPanel;

        [Header("Pause and outcome panels")]
        public CanvasGroup pausePanel, victoryPanel, pauseMenuGroup, settingsPanel, howToPlayPanel;
        public Button resumeButton, pauseMenuButton, victoryMenuButton;
        public Button outcomeRestartButton;
        public Button pauseSettingsButton, pauseHowToPlayButton, restartButton, quitButton, settingsBackButton, howToPlayBackButton;
        public Text victoryTitleText, victoryDetailText, victoryStatsText;

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
        public bool PauseVisible => pausePanel && pausePanel.alpha > .99f;
        public bool VictoryVisible => victoryPanel && victoryPanel.alpha > .99f;

        void Awake()
        {
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
            SetPanel(pausePanel, false);
            SetPanel(victoryPanel, false);
            SetPanel(settingsPanel, false);
            SetPanel(howToPlayPanel, false);
            SetPanel(pauseMenuGroup, true);
        }

        void OnEnable() { if (flow) flow.ProgressChanged += OnProgress; }
        void OnDisable() { if (flow) flow.ProgressChanged -= OnProgress; }

        void OnDestroy()
        {
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
            if (toastText) toastText.text = "Ev kurtarıldı  ·  " + saved + " / " + total;
            toastUntil = Time.unscaledTime + 3.2f;
        }

        void LateUpdate()
        {
            if (!flow || !player || !hose) return;
            if (!vitals) vitals = survivalObjective ? survivalObjective.vitals : FindFirstObjectByType<CreatureCombatPlayer>();
            if (canvas) canvas.enabled = !VolcanoCinematic.IsPlayingAny || flow.BlocksGameplay;
            bool playing = flow.State == RescueGameFlow.GameState.Playing;
            if (objectiveText) objectiveText.text = "Kurtarılan evler   " + flow.RescuedCount + " / " + Mathf.Max(1, flow.TargetCount);
            if (waterText) waterText.text = Mathf.CeilToInt(hose.TankLitres) + " / " + Mathf.RoundToInt(hose.tankCapacityLitres) + " L";
            if (waterFill) waterFill.fillAmount = hose.Tank01;
            if (pumpText) pumpText.text = Ashfall.UI.AshfallTypography.NaturalCase(player.WaterStatus);
            if (speedText) speedText.text = player.IsDriving && drive ? Mathf.RoundToInt(drive.SpeedKph) + " km/sa" : "Yaya";
            bool showHealth = playing && vitals && vitals.Health < vitals.maximumHealth - .1f;
            if (healthPanel && healthPanel.activeSelf != showHealth) healthPanel.SetActive(showHealth);
            if (showHealth)
            {
                if (healthFill) healthFill.fillAmount = Mathf.Clamp01(vitals.Health / vitals.maximumHealth);
                if (healthText) healthText.text = Mathf.CeilToInt(vitals.Health) + " / " + Mathf.RoundToInt(vitals.maximumHealth);
            }
            UpdateQuest();
            if (lowWaterWarning) lowWaterWarning.SetActive(playing && hose.TankLitres < 200f);
            bool empty = hose.TankLitres <= 0f;
            if (lowWaterText) lowWaterText.text = empty ? "Depo boş  ·  gölden doldur" : "Su az  ·  gölden doldur";
            if (lowWaterBackground) lowWaterBackground.color = empty ? new Color(.5f, .065f, .035f, .97f) : new Color(.4f, .12f, .04f, .95f);
            if (promptText)
            {
                promptText.text = playing ? Ashfall.UI.AshfallTypography.NaturalCase(player.InteractionPrompt) : "";
                promptText.transform.parent.gameObject.SetActive(!string.IsNullOrWhiteSpace(promptText.text));
            }
            if (toastText)
            {
                bool showToast = playing && Time.unscaledTime < toastUntil;
                toastText.gameObject.SetActive(true);
                toastText.transform.parent.gameObject.SetActive(showToast);
            }
            if (subtitleText)
            {
                bool showMessage = playing && evacuation && evacuation.MessageRemaining > 0f;
                subtitleText.transform.parent.gameObject.SetActive(showMessage);
                if (showMessage) subtitleText.text = evacuation.CurrentMessage;
            }
            UpdateEruption(); UpdateTarget(playing);
            bool survivalFinished = survivalObjective && survivalObjective.IsFinished;
            bool showOutcome = flow.State == RescueGameFlow.GameState.Won || survivalFinished;
            SetPanel(victoryPanel, showOutcome);
            if (showOutcome) UpdateOutcome(survivalFinished);

            if (previousState != flow.State || previousSurvivalFinished != survivalFinished)
            {
                previousState = flow.State;
                SetPanel(pausePanel, flow.State == RescueGameFlow.GameState.Paused);
                SetPanel(pauseMenuGroup, flow.State == RescueGameFlow.GameState.Paused);
                SetPanel(settingsPanel, false); SetPanel(howToPlayPanel, false);
                previousSurvivalFinished = survivalFinished;
                var selection = flow.State == RescueGameFlow.GameState.Paused ? resumeButton : showOutcome && survivalObjective && survivalObjective.State == VolcanoSurvivalObjective.MissionState.Lost ? outcomeRestartButton : showOutcome ? victoryMenuButton : null;
                if (selection && EventSystem.current) EventSystem.current.SetSelectedGameObject(selection.gameObject);
            }
        }

        void UpdateOutcome(bool survivalFinished)
        {
            bool survivalWon = survivalFinished && survivalObjective.State == VolcanoSurvivalObjective.MissionState.Won;
            if (victoryTitleText) victoryTitleText.text = survivalFinished ? (survivalWon ? "Bölge güvende" : "Görev başarısız") : "Kasaba güvende";
            if (victoryDetailText) victoryDetailText.text = survivalFinished
                ? (survivalWon ? "Alev dalgaları durduruldu. İtfaiye aracına başarıyla dönüldü." : "Canın tükendi. Hazırlanıp kasabayı yeniden savun.")
                : "Beş ev de kurtarıldı. Tahliye edilen halk güvende.";
            if (victoryStatsText)
            {
                if (survivalFinished && survivalObjective.encounter)
                {
                    var encounter = survivalObjective.encounter;
                    victoryStatsText.text = "Durdurulan alev ruhu  " + encounter.DefeatedCount + " / " + encounter.PlannedCount +
                        "     ·     Dalga  " + encounter.WaveNumber + " / " + encounter.WaveCount;
                }
                else victoryStatsText.text = "Kurtarılan ev  " + (flow ? flow.RescuedCount : 0) + " / " + (flow ? flow.TargetCount : 0);
            }
            if (outcomeRestartButton) outcomeRestartButton.gameObject.SetActive(survivalFinished);
            if (victoryMenuButton) victoryMenuButton.GetComponent<RectTransform>().anchoredPosition = survivalFinished ? new Vector2(400, -340) : new Vector2(170, -340);
        }

        void UpdateQuest()
        {
            if (!questTitleText || !questBodyText || !questHintText || !questFill) return;
            VolcanoMissionDirector.MissionPhase phase = missionDirector ? missionDirector.Phase : VolcanoMissionDirector.MissionPhase.Defend;
            if (evacuation && !evacuation.IsComplete)
            {
                questTitleText.text = "Halkı tahliye et";
                questBodyText.text = "Sığınakta  " + evacuation.RescuedCount + " / " + evacuation.TotalCount +
                    "     çağrılan  " + evacuation.CalledCount + " / " + evacuation.TotalCount + "\n" +
                    (evacuation.CalledCount < evacuation.TotalCount ? "Evlerin yanına git ve Q ile halka seslen." : "Grubu SHELTER tabelalı sığınağa götür.");
                questHintText.text = player.IsDriving ? "F  Araçtan in" : evacuation.CooldownRemaining > 0f ? "Q  Tekrar seslen  ·  " + Mathf.CeilToInt(evacuation.CooldownRemaining) + " sn" : "Q  Herkes dışarıya!  ·  " + evacuation.DestinationHintForHud;
                questFill.fillAmount = evacuation.TotalCount > 0 ? (float)evacuation.RescuedCount / evacuation.TotalCount : 0f;
                return;
            }
            if (phase == VolcanoMissionDirector.MissionPhase.Refill)
            {
                questTitleText.text = "Su deposunu doldur";
                questBodyText.text = "Tahliye tamamlandı  ·  " + (evacuation ? evacuation.TotalCount : 7) + " / " + (evacuation ? evacuation.TotalCount : 7) + " kişi güvende.\nHortumu göle indir ve su pompasını çalıştır.";
                questHintText.text = hose.IsDeployed ? "P  Pompayı aç / kapat   ·   R  Hortumu topla" : "F  Araçtan in   ·   E  Arka hortumu al";
                questFill.fillAmount = hose.Tank01;
                return;
            }
            if (phase <= VolcanoMissionDirector.MissionPhase.Cinematic)
            {
                questTitleText.text = "Volkan uyanıyor";
                questBodyText.text = eruption && eruption.Stage == VolcanoEruptionSequence.EruptionStage.Warning ? "Yer sarsılıyor. Patlama yaklaşıyor." : "Tahliye bitti. Kasabayı savunmaya hazırlan.";
                questHintText.text = eruption && eruption.Stage == VolcanoEruptionSequence.EruptionStage.Waiting ? "Patlamaya  " + Mathf.CeilToInt(eruption.Countdown) + " sn" : "Tehlikeli bölgeden uzaklaş.";
                questFill.fillAmount = flow.RescuedCount / 5f;
                return;
            }
            int burning, lost; float nextCollapse;
            CountHouses(out burning, out lost, out nextCollapse);
            questTitleText.text = "Kasabayı savun";
            questBodyText.text = "Kurtarılan ev  " + flow.RescuedCount + " / " + Mathf.Max(1, flow.TargetCount) + "\n" +
                (burning > 0 ? "Yanan ev: " + burning + "  ·  Kül olan: " + lost + "  ·  En acil: " + Mathf.CeilToInt(nextCollapse) + " sn" : "Yanan evleri ve yaklaşan lavları suyla soğut.");
            questHintText.text = player.IsDriving ? "Fareyle nişan al   ·   Sağ tık basılı: Su püskürt" : "F  İtfaiye aracına bin   ·   E  Etkileşim";
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
            if (!eruptionText || !eruption) return;
            if (survivalObjective && survivalObjective.encounter && survivalObjective.encounter.enableWaves &&
                (survivalObjective.State == VolcanoSurvivalObjective.MissionState.Fighting || survivalObjective.State == VolcanoSurvivalObjective.MissionState.ReturnToTruck))
            {
                var encounter = survivalObjective.encounter;
                eruptionText.text = survivalObjective.State == VolcanoSurvivalObjective.MissionState.ReturnToTruck
                    ? "Son adım  ·  Araca dön\nF ile sürücü kapısından bin"
                    : "Alev dalgası  " + Mathf.Max(1, encounter.WaveNumber) + " / " + encounter.WaveCount +
                      "\nSahada " + encounter.AliveCount + "  ·  Durdurulan " + encounter.DefeatedCount + " / " + encounter.PlannedCount;
                return;
            }
            switch (eruption.Stage)
            {
                case VolcanoEruptionSequence.EruptionStage.Waiting:
                    eruptionText.text = "Volkan  ·  hazırlık\n" + (missionDirector && missionDirector.Phase == VolcanoMissionDirector.MissionPhase.Refill ? "Depo dolunca volkan uyanacak" : "Tahliye ve hazırlık sürüyor"); break;
                case VolcanoEruptionSequence.EruptionStage.Warning: eruptionText.text = "Yer sarsılıyor\nPatlama yaklaşıyor"; break;
                case VolcanoEruptionSequence.EruptionStage.Erupting: eruptionText.text = "Volkan patladı\nKasabayı koru"; break;
                default: eruptionText.text = "Lav ilerliyor\nYanan evleri suyla söndür"; break;
            }
        }

        void UpdateTarget(bool playing)
        {
            var target = cannon ? cannon.PredictedTarget : null;
            bool aiming = playing && player.IsDriving && cannon;
            bool showTarget = aiming && target;
            if (targetGroup) targetGroup.SetActive(showTarget);
            if (showTarget)
            {
                if (targetText) targetText.text = target.IsRescued ? "Ev güvende" : target.IsBurning ?
                    (cannon.IsSpraying && cannon.HitFire == target ? "Yangın söndürülüyor" : "Yanan ev  ·  söndürme ilerlemesi") : "Ev henüz yanmıyor";
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
