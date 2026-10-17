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
            if (resumeButton) resumeButton.onClick.AddListener(Resume);
            if (pauseMenuButton) pauseMenuButton.onClick.AddListener(ReturnToMenu);
            if (victoryMenuButton) victoryMenuButton.onClick.AddListener(ReturnToMenu);
            SetPanel(pausePanel, false); SetPanel(victoryPanel, false);
        }
        void OnEnable() { if (flow) flow.ProgressChanged += OnProgress; }
        void OnDisable() { if (flow) flow.ProgressChanged -= OnProgress; }
        void OnDestroy()
        {
            if (resumeButton) resumeButton.onClick.RemoveListener(Resume);
            if (pauseMenuButton) pauseMenuButton.onClick.RemoveListener(ReturnToMenu);
            if (victoryMenuButton) victoryMenuButton.onClick.RemoveListener(ReturnToMenu);
        }
        void Resume() { if (flow) flow.Resume(); }
        void ReturnToMenu() { if (flow) flow.ReturnToMainMenu(); }
        void OnProgress(int saved, int total)
        {
            if (toastText) toastText.text = "EV KURTARILDI  ·  " + saved + " / " + total;
            toastUntil = Time.unscaledTime + 3.2f;
        }
        void LateUpdate()
        {
            if (!flow || !player || !hose) return;
            bool playing = flow.State == RescueGameFlow.GameState.Playing;
            if (objectiveText) objectiveText.text = "KURTARILAN EVLER   " + flow.RescuedCount + " / " + RescueGameFlow.RequiredHouses;
            if (waterText) waterText.text = Mathf.CeilToInt(hose.TankLitres) + " / " + Mathf.RoundToInt(hose.tankCapacityLitres) + " L";
            if (waterFill) waterFill.fillAmount = hose.Tank01;
            if (pumpText) pumpText.text = player.WaterStatus;
            if (speedText) speedText.text = player.IsDriving && drive ? Mathf.RoundToInt(drive.SpeedKph) + " km/sa" : "YAYA";
            if (lowWaterWarning) lowWaterWarning.SetActive(playing && hose.TankLitres < 200f);
            bool empty = hose.TankLitres <= 0f;
            if (lowWaterText) lowWaterText.text = empty ? "DEPO BOŞ  ·  GÖLDEN DOLDUR" : "SU AZ  ·  GÖLDEN DOLDUR";
            if (lowWaterBackground) lowWaterBackground.color = empty ? new Color(.5f, .065f, .035f, .97f) : new Color(.4f, .12f, .04f, .95f);
            if (promptText) promptText.text = playing ? player.InteractionPrompt : "";
            if (toastText) toastText.gameObject.SetActive(playing && Time.unscaledTime < toastUntil);
            UpdateEruption(); UpdateTarget(playing);
            if (previousState != flow.State)
            {
                previousState = flow.State;
                SetPanel(pausePanel, flow.State == RescueGameFlow.GameState.Paused);
                SetPanel(victoryPanel, flow.State == RescueGameFlow.GameState.Won);
                var selection = flow.State == RescueGameFlow.GameState.Paused ? resumeButton : flow.State == RescueGameFlow.GameState.Won ? victoryMenuButton : null;
                if (selection && EventSystem.current) EventSystem.current.SetSelectedGameObject(selection.gameObject);
            }
        }
        void UpdateEruption()
        {
            if (!eruptionText || !eruption) return;
            switch (eruption.Stage)
            {
                case VolcanoEruptionSequence.EruptionStage.Waiting:
                    eruptionText.text = "VOLKAN · HAZIRLIK\n" + Mathf.CeilToInt(eruption.Countdown) + " sn sonra patlama"; break;
                case VolcanoEruptionSequence.EruptionStage.Warning:
                    eruptionText.text = "YER SARSILIYOR\nPatlama yaklaşıyor"; break;
                case VolcanoEruptionSequence.EruptionStage.Erupting:
                    eruptionText.text = "VOLKAN PATLADI\nKasabayı koru"; break;
                default: eruptionText.text = "LAV İLERLİYOR\nYanan evleri suyla söndür"; break;
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
                if (targetText) targetText.text = target.IsRescued ? "EV GÜVENDE" : target.IsBurning ?
                    (cannon.IsSpraying && cannon.HitFire == target ? "YANGIN SÖNDÜRÜLÜYOR" : "YANAN EV · SÖNDÜRME İLERLEMESİ") : "EV HENÜZ YANMIYOR";
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
