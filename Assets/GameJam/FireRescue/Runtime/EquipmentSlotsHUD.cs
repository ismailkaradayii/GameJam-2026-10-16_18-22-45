using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Two equipment choices bound to the existing gameplay Canvas and real pistol state.</summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentSlotsHUD : MonoBehaviour
    {
        static readonly Color Paper = new Color(.95f, .91f, .82f);
        static readonly Color Amber = new Color(1f, .64f, .19f);
        static readonly Color Quiet = new Color(.59f, .58f, .55f);
        RescueHUD hud;
        RectTransform panel, content;
        readonly Image[] backgrounds = new Image[2];
        readonly Outline[] borders = new Outline[2];
        readonly Text[] labels = new Text[2];
        readonly Vector3[] corners = new Vector3[4];
        Image pistolIcon;
        Text controls, pistolFallback;
        bool built;
        int shownAmmo = -1, shownReserve = -1, shownState = -1;

        public int SlotCount => built ? 2 : 0;
        public int SelectedSlot => hud && hud.player ? hud.player.SelectedEquipmentSlot : 2;
        public Image PistolIcon => pistolIcon;

        public void Configure(RescueHUD owner)
        {
            hud = owner;
            if (built) return;
            panel = transform as RectTransform;
            if (!panel) return;
            var automaticFrame = panel.GetComponent<VolcanicRockFrameSizer>();
            if (automaticFrame) automaticFrame.enabled = false;
            if (hud.equipmentText) hud.equipmentText.gameObject.SetActive(false);
            content = Rect("Equipment slots", panel);
            content.anchorMin = Vector2.zero; content.anchorMax = Vector2.one;
            content.offsetMin = content.offsetMax = Vector2.zero;
            for (int slot = 0; slot < 2; slot++)
            {
                RectTransform cell = Rect(slot == 0 ? "Slot 1 - Pistol" : "Slot 2 - Empty hands", content);
                backgrounds[slot] = cell.gameObject.AddComponent<Image>();
                backgrounds[slot].raycastTarget = false;
                borders[slot] = cell.gameObject.AddComponent<Outline>();
                borders[slot].effectDistance = new Vector2(1.5f, -1.5f);
                Text key = Label("Key", cell, (slot + 1).ToString(), 19, TextAnchor.MiddleLeft);
                Top(key.rectTransform, 10, 7, 22, 24);
                Text title = Label("Name", cell, slot == 0 ? "PISTOL" : "HANDS", 13, TextAnchor.MiddleLeft);
                Top(title.rectTransform, 34, 8, 84, 22);
                labels[slot] = Label("State", cell, slot == 0 ? "" : "EMPTY HANDS", 14, TextAnchor.MiddleCenter);
                labels[slot].rectTransform.anchorMin = new Vector2(0, 0);
                labels[slot].rectTransform.anchorMax = new Vector2(1, 0);
                labels[slot].rectTransform.pivot = new Vector2(.5f, 0);
                labels[slot].rectTransform.offsetMin = new Vector2(5, 6);
                labels[slot].rectTransform.offsetMax = new Vector2(-5, 29);
                if (slot == 0)
                {
                    RectTransform icon = Rect("Pistol icon", cell);
                    icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(.5f, .5f);
                    icon.sizeDelta = new Vector2(104, 54); icon.anchoredPosition = new Vector2(0, -1);
                    pistolIcon = icon.gameObject.AddComponent<Image>();
                    pistolIcon.raycastTarget = false; pistolIcon.preserveAspect = true;
                    pistolIcon.sprite = Resources.Load<Sprite>("ASHFALL/UI/Icons/PistolSlot");
                    pistolIcon.enabled = pistolIcon.sprite;
                    pistolFallback = Label("Pistol fallback", cell, "PISTOL", 19, TextAnchor.MiddleCenter);
                    pistolFallback.rectTransform.anchorMin = pistolFallback.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    pistolFallback.rectTransform.sizeDelta = new Vector2(110, 42);
                    pistolFallback.rectTransform.anchoredPosition = Vector2.zero;
                    pistolFallback.gameObject.SetActive(!pistolIcon.sprite);
                }
                else
                {
                    Text empty = Label("Empty slot", cell, "—", 30, TextAnchor.MiddleCenter);
                    empty.rectTransform.anchorMin = empty.rectTransform.anchorMax = new Vector2(.5f, .5f);
                    empty.rectTransform.sizeDelta = new Vector2(90, 40);
                    empty.rectTransform.anchoredPosition = Vector2.zero;
                }
            }
            controls = Label("Switch controls", content, "1 / 2  ·  MOUSE WHEEL", 13, TextAnchor.MiddleCenter);
            built = true;
        }

        public void RefreshDisplay()
        {
            if (!built || !hud || !hud.player || !hud.canvas) return;
            FireRescuePlayer player = hud.player;
            float canvasWidth = ((RectTransform)hud.canvas.transform).rect.width;
            float width = Mathf.Min(340f, Mathf.Max(260f, canvasWidth * .28f));
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0);
            panel.sizeDelta = new Vector2(width, 174);
            float bottom = 28f;
            if (hud.waterGroup && hud.waterGroup.activeSelf)
            {
                var water = hud.waterGroup.transform as RectTransform;
                if (water) bottom = Mathf.Max(bottom, water.anchoredPosition.y + water.rect.height + 12f);
            }
            panel.anchoredPosition = new Vector2(-28, bottom);
            // Narrow Game views can put centre-bottom subtitles/prompts beside the
            // slot card. Lift only when their actual Canvas rectangles overlap.
            RaiseAbove(hud.healthPanel ? hud.healthPanel.transform as RectTransform : null);
            RaiseAbove(hud.targetGroup ? hud.targetGroup.transform as RectTransform : null);
            RaiseAbove(hud.promptText ? hud.promptText.transform.parent as RectTransform : null);
            RaiseAbove(hud.subtitleText ? hud.subtitleText.transform.parent as RectTransform : null);
            float gap = 10f, padding = 29f, cellWidth = (width - padding * 2f - gap) * .5f;
            for (int slot = 0; slot < 2; slot++)
            {
                Top(backgrounds[slot].rectTransform, padding + slot * (cellWidth + gap), 25, cellWidth, 105);
                bool selected = player.SelectedEquipmentSlot == slot + 1;
                backgrounds[slot].color = selected ? new Color(.23f, .105f, .045f, .96f) : new Color(.045f, .045f, .043f, .92f);
                borders[slot].effectColor = selected ? Amber : new Color(.32f, .29f, .24f, .9f);
                labels[slot].color = selected ? Paper : Quiet;
            }
            Top(controls.rectTransform, padding, 137, width - padding * 2, 18);
            controls.color = player.CanSelectEquipment ? Paper : Quiet;
            var pistol = player.pistol;
            if (pistol)
            {
                int state = pistol.IsReloading ? 2 : pistol.HolsterPending ? 1 : 0;
                if (shownAmmo != pistol.Ammo || shownReserve != pistol.reserveAmmo || shownState != state)
                {
                    shownAmmo = pistol.Ammo; shownReserve = pistol.reserveAmmo; shownState = state;
                    labels[0].text = state == 2 ? "RELOADING" : state == 1 ? "STOWING" : pistol.Ammo + " / " + pistol.reserveAmmo;
                }
            }
            labels[1].text = player.SelectedEquipmentSlot == 2 ? "SELECTED" : "EMPTY HANDS";
            if (pistolIcon) pistolIcon.color = player.SelectedEquipmentSlot == 1 ? Color.white : new Color(.65f, .65f, .65f, 1);
        }

        void RaiseAbove(RectTransform other)
        {
            if (!other || !other.gameObject.activeInHierarchy) return;
            UnityEngine.Rect card = CanvasBounds(panel), obstacle = CanvasBounds(other);
            if (card.xMin >= obstacle.xMax + 12 || card.xMax <= obstacle.xMin - 12 ||
                card.yMin >= obstacle.yMax + 12 || card.yMax <= obstacle.yMin - 12) return;
            panel.anchoredPosition += Vector2.up * (obstacle.yMax + 12 - card.yMin);
        }

        UnityEngine.Rect CanvasBounds(RectTransform rect)
        {
            rect.GetWorldCorners(corners);
            Vector2 min = hud.canvas.transform.InverseTransformPoint(corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = hud.canvas.transform.InverseTransformPoint(corners[i]);
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }
        static Text Label(string name, Transform parent, string value, int size, TextAnchor alignment)
        {
            var label = Rect(name, parent).gameObject.AddComponent<Text>();
            label.font = Ashfall.UI.AshfallTypography.Semibold;
            label.fontSize = size; label.text = value; label.color = Paper;
            label.alignment = alignment; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        static void Top(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
    }
}
