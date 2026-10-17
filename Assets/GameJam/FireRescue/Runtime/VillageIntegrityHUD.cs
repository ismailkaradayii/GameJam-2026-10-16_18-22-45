using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>After the eruption, the preparation timer becomes the village's remaining health.</summary>
    [DisallowMultipleComponent]
    public sealed class VillageIntegrityHUD : MonoBehaviour
    {
        public RescueGameFlow flow;
        public RectTransform timerPanel;
        public Text timerText;
        public GameObject defenseGroup;
        public Image villageFill;
        public Text percentageText, statusText;
        public Image[] houseFills = Array.Empty<Image>();
        public Text[] houseLabels = Array.Empty<Text>();
        public float preparationHeight = 68f, defenseHeight = 142f;
        public bool compactLabels;
        public float Integrity01 { get; private set; } = 1f;
        public int BurningCount { get; private set; }
        public int LostCount { get; private set; }
        public bool IsVisible => defenseGroup && defenseGroup.activeSelf;
        static readonly Color Healthy = new Color(.31f, .86f, .5f);
        static readonly Color Warning = new Color(1f, .63f, .18f);
        static readonly Color Critical = new Color(1f, .24f, .13f);
        static readonly Color Ruined = new Color(.32f, .31f, .3f);

        public void Refresh(bool afterEruption)
        {
            if (timerText) timerText.gameObject.SetActive(!afterEruption);
            if (defenseGroup) defenseGroup.SetActive(afterEruption);
            if (timerPanel) timerPanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                afterEruption ? defenseHeight : preparationHeight);
            if (!afterEruption) return;
            var houses = flow ? flow.targetBuildings : Array.Empty<BurnableBuilding>();
            float sum = 0; BurningCount = LostCount = 0;
            for (int i = 0; i < houses.Length; i++)
            {
                var house = houses[i];
                float health = house ? house.Integrity01 : 0;
                bool ruined = !house || house.IsBurnedOut;
                if (ruined) LostCount++;
                if (house && house.IsBurning) BurningCount++;
                sum += health;
                Color tint = ruined ? Ruined : HealthColor(health);
                if (house && house.IsBurning)
                    tint = Color.Lerp(tint, Color.white, .09f + .07f * Mathf.Sin(Time.unscaledTime * 5));
                if (i < houseFills.Length && houseFills[i])
                { houseFills[i].fillAmount = health; houseFills[i].color = tint; }
                if (i < houseLabels.Length && houseLabels[i])
                {
                    houseLabels[i].text = compactLabels ? (i + 1) + (ruined ? "  LOST" : "  " + Mathf.RoundToInt(health * 100) + "%") :
                        "EV " + (i + 1) + (ruined ? "  KÜL" : "  %" + Mathf.RoundToInt(health * 100));
                    houseLabels[i].color = ruined ? Ruined : tint;
                }
            }
            Integrity01 = houses.Length > 0 ? sum / houses.Length : 1;
            if (villageFill) { villageFill.fillAmount = Integrity01; villageFill.color = HealthColor(Integrity01); }
            if (percentageText)
            { percentageText.text = "%" + Mathf.RoundToInt(Integrity01 * 100); percentageText.color = HealthColor(Integrity01); }
            if (statusText)
            {
                statusText.text = compactLabels ? "Fires " + BurningCount + "   /   Houses lost " + LostCount + " of " + houses.Length + (Integrity01 <= .3f ? "  — CRITICAL" : "") :
                    houses.Length > 0 && LostCount == houses.Length ? "TÜM EVLER KÜL OLDU · KÖY KAYBEDİLDİ" :
                    "Yanan: " + BurningCount + "   ·   Kül olan: " + LostCount + " / " + houses.Length +
                    (Integrity01 <= .3f ? "   ·   KRİTİK! Evleri söndür." : "   ·   Evler kül oldukça kaybetmeye yaklaşırsın.");
                statusText.color = Integrity01 <= .3f ? Critical : new Color(.86f, .84f, .79f);
            }
        }
        static Color HealthColor(float health) => health <= .3f ? Critical : health <= .6f ? Warning : Healthy;
    }
}
