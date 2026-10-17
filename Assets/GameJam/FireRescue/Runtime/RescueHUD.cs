using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Canvas presentation of the rescue objective and existing gameplay state.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(250)]
    public sealed class RescueHUD : MonoBehaviour
    {
        public RescueGameFlow flow;
        public VolcanoMissionDirector mission;
        public Text objectiveDetail, progressText, equipmentText, healthText, resultTitle, resultDetail;
        public Button restartButton;
        public GameObject waterGroup, equipmentGroup;
        public CanvasGroup objectiveGroup;
        float objectiveChangedAt;
        VolcanoMissionDirector.MissionPhase previousPhase;
        bool objectiveSeen;
        public FireRescuePlayer player;
        public FireHoseSystem hose;
        public FireTruckDrive drive;
        public FireTruckWaterCannon cannon;
        public VolcanoEruptionSequence eruption;
        public Canvas canvas;
        public Text objectiveText, waterText, pumpText, speedText, eruptionText, targetText, promptText, toastText, lowWaterText;
        public Image waterFill, targetFill, impactReticle, lowWaterBackground;
        public GameObject targetGroup, lowWaterWarning;
        public CanvasGroup pausePanel, victoryPanel;
        public Button resumeButton, pauseMenuButton, victoryMenuButton;
        public bool PauseVisible => pausePanel && pausePanel.alpha > .99f;
        public bool VictoryVisible => victoryPanel && victoryPanel.alpha > .99f;
        float toastUntil;
        RescueGameFlow.GameState previousState = (RescueGameFlow.GameState)(-1);

        void Awake()
        {
            if (!mission) mission = FindFirstObjectByType<VolcanoMissionDirector>();
            if (restartButton) restartButton.onClick.AddListener(Restart);
            if (resumeButton) resumeButton.onClick.AddListener(Resume);
            if (pauseMenuButton) pauseMenuButton.onClick.AddListener(ReturnToMenu);
            if (victoryMenuButton) victoryMenuButton.onClick.AddListener(ReturnToMenu);
            SetPanel(pausePanel, false); SetPanel(victoryPanel, false);
        }
        void OnEnable() { if (flow) flow.ProgressChanged += OnProgress; }
        void OnDisable() { if (flow) flow.ProgressChanged -= OnProgress; }
        void OnDestroy()
        {
            if (restartButton) restartButton.onClick.RemoveListener(Restart);
            if (resumeButton) resumeButton.onClick.RemoveListener(Resume);
            if (pauseMenuButton) pauseMenuButton.onClick.RemoveListener(ReturnToMenu);
            if (victoryMenuButton) victoryMenuButton.onClick.RemoveListener(ReturnToMenu);
        }
        void Restart() { if (flow) flow.RestartMission(); }
        void Resume() { if (flow) flow.Resume(); }
        void ReturnToMenu() { if (flow) flow.ReturnToMainMenu(); }
        void OnProgress(int saved, int total)
        {
            if (toastText) toastText.text = "FIRE EXTINGUISHED  |  " + saved + " / " + total;
            toastUntil = Time.unscaledTime + 3.2f;
        }
        void LateUpdate()
        {
            if (!flow || !player || !hose) return;
            if (canvas) canvas.enabled = !VolcanoCinematic.IsPlayingAny;
            bool playing = flow.State == RescueGameFlow.GameState.Playing;
            if (objectiveText) objectiveText.text = mission ? mission.ObjectiveTitle : "DEFEND THE VILLAGE";
            if (objectiveDetail) objectiveDetail.text = mission ? mission.ObjectiveDetail : "Shoot spirits and extinguish fires.";
            if (mission && (!objectiveSeen || previousPhase != mission.Phase))
            {
                if (objectiveSeen && !mission.EruptionTriggered && mission.Phase != VolcanoMissionDirector.MissionPhase.Weapon)
                { if (toastText) toastText.text = "OBJECTIVE COMPLETE"; toastUntil = Time.unscaledTime + 2.2f; }
                previousPhase = mission.Phase; objectiveSeen = true; objectiveChangedAt = Time.unscaledTime;
            }
            if (objectiveGroup) objectiveGroup.alpha = Mathf.Lerp(.3f, 1f, Mathf.Clamp01((Time.unscaledTime - objectiveChangedAt) / .35f));
            int saved = 0, burning = 0;
            foreach (var house in flow.targetBuildings) if (house) { if (!house.IsBurnedOut) saved++; if (house.IsBurning) burning++; }
            var evacuation = mission ? mission.evacuation : null;
            var encounter = mission ? mission.encounter : null;
            if (progressText) progressText.text = mission && mission.EruptionTriggered ?
                "Houses standing: " + saved + "/" + flow.TargetCount + "  |  Fires: " + burning +
                (encounter ? "\nWave " + Mathf.Max(1, encounter.WaveNumber) + "/" + encounter.WaveCount + "  |  Spirits remaining: " + (encounter.PlannedCount - encounter.SpawnedCount + encounter.AliveCount) : "") :
                mission && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate && evacuation ? "Villagers Evacuated: " + evacuation.RescuedCount + "/" + evacuation.TotalCount + "  |  Called: " + evacuation.CalledCount :
                mission && mission.Phase == VolcanoMissionDirector.MissionPhase.Refill ? "Water tank: " + Mathf.RoundToInt(hose.Tank01 * 100) + "%" : "";
            if (waterGroup) waterGroup.SetActive(playing && (player.IsDriving || hose.IsDeployed));
            if (equipmentGroup) equipmentGroup.SetActive(playing && player.HasPistol && !player.IsDriving);
            if (equipmentText && player.pistol) equipmentText.text = "1  PISTOL  " + (player.pistol.Equipped ? player.pistol.Ammo + " / " + player.pistol.reserveAmmo : "[HOLSTERED]") +
                (player.pistol.IsReloading ? "  RELOADING " + Mathf.RoundToInt(player.pistol.ReloadProgress * 100) + "%" : "");
            var vitals = player.GetComponent<CreatureCombatPlayer>();
            if (healthText) healthText.text = vitals && vitals.Health < vitals.maximumHealth ? "HEALTH  " + Mathf.CeilToInt(vitals.Health) : "";
            if (waterText) waterText.text = Mathf.CeilToInt(hose.TankLitres) + " / " + Mathf.RoundToInt(hose.tankCapacityLitres) + " L";
            if (waterFill) waterFill.fillAmount = hose.Tank01;
            if (pumpText) pumpText.text = player.WaterStatus;
            if (speedText) speedText.text = player.IsDriving && drive ? Mathf.RoundToInt(drive.SpeedKph) + " km/h" : "";
            if (lowWaterWarning) lowWaterWarning.SetActive(playing && player.IsDriving && hose.TankLitres < 200f);
            bool empty = hose.TankLitres <= 0f;
            if (lowWaterText) lowWaterText.text = empty ? "EMPTY TANK  |  REFILL AT THE LAKE" : "LOW WATER  |  REFILL AT THE LAKE";
            if (lowWaterBackground) lowWaterBackground.color = empty ? new Color(.5f, .065f, .035f, .97f) : new Color(.4f, .12f, .04f, .95f);
            if (promptText) promptText.text = playing ? player.InteractionPrompt : "";
            if (toastText) toastText.gameObject.SetActive(playing && Time.unscaledTime < toastUntil);
            UpdateEruption(); UpdateTarget(playing);
            if (previousState != flow.State)
            {
                previousState = flow.State;
                SetPanel(pausePanel, flow.State == RescueGameFlow.GameState.Paused);
                bool ended = flow.State == RescueGameFlow.GameState.Won || flow.State == RescueGameFlow.GameState.Lost;
                SetPanel(victoryPanel, ended);
                if (ended)
                {
                    if (resultTitle) resultTitle.text = "Final Result: " + (flow.State == RescueGameFlow.GameState.Won ? "SUCCESS" : "FAILED");
                    if (resultDetail) resultDetail.text = "Saved Villagers   " + (evacuation ? evacuation.RescuedCount + " / " + evacuation.TotalCount : "0") +
                        "\nHouses Saved   " + saved + " / " + flow.TargetCount + "\nFires Extinguished   " + flow.RescuedCount;
                }
                var selection = flow.State == RescueGameFlow.GameState.Paused ? resumeButton : (flow.State == RescueGameFlow.GameState.Won || flow.State == RescueGameFlow.GameState.Lost) ? restartButton : null;
                if (selection && EventSystem.current) EventSystem.current.SetSelectedGameObject(selection.gameObject);
            }
        }
        void UpdateEruption()
        {
            if (!eruptionText || !eruption) return;
            if (mission)
            {
                int seconds = Mathf.CeilToInt(mission.PreparationRemaining);
                eruptionText.text = mission.EruptionTriggered ? "VILLAGE DEFENSE" : "VOLCANO ERUPTION IN: " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
                eruptionText.color = !mission.EruptionTriggered && seconds <= 30 ? Color.Lerp(new Color(1, .24f, .12f), Color.white, .2f + .2f * Mathf.Sin(Time.unscaledTime * 6)) : Color.white;
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
                if (targetText) targetText.text = target.IsRescued ? "HOUSE SAVED" : target.IsBurning ?
                    (cannon.IsSpraying && cannon.HitFire == target ? "EXTINGUISHING FIRE" : "BURNING HOUSE") : "HOUSE NOT BURNING";
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
        static void SetPanel(CanvasGroup group, bool visible)
        {
            if (!group) return;
            group.alpha = visible ? 1 : 0; group.interactable = group.blocksRaycasts = visible;
        }
    }
}
