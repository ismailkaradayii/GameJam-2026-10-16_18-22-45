using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameJam.FireRescue
{
    /// <summary>Eylul's original artwork over the current typography and content-driven HUD.</summary>
    [CreateAssetMenu(menuName = "ASHFALL/Volcanic HUD Theme")]
    public sealed class VolcanicHudTheme : ScriptableObject
    {
        public Sprite cardBackground, pageBackground, keyGlow;
        public float horizontalPadding = 44f, verticalPadding = 36f;
        [Range(.5f, 1f)] public float backgroundOpacity = .82f;
        Sprite fittedCard, fittedPage;
        Sprite Card => fittedCard ? fittedCard : (fittedCard = Fitted(cardBackground, new Vector4(300, 230, 300, 230)));
        Sprite Page => fittedPage ? fittedPage : (fittedPage = Fitted(pageBackground, new Vector4(220, 220, 220, 220)));
        static VolcanicHudTheme current;
        public static VolcanicHudTheme Current => current ? current : (current = Resources.Load<VolcanicHudTheme>("ASHFALL/UI/VolcanicHudTheme"));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => current = null;

        public void Apply(RescueHUD hud)
        {
            if (!hud || !hud.cleanPresentation) return;
            foreach (var label in new[] { hud.questTitleText, hud.objectiveText, hud.eruptionText,
                hud.subtitleText, hud.toastText, hud.targetText, hud.lowWaterText, hud.equipmentText })
                if (label) Frame(label.transform.parent as RectTransform, Card);
            if (hud.speedText)
            {
                NoBackground(hud.speedText.transform.parent as RectTransform);
                TextShadow(hud.speedText);
                var caption = hud.speedText.transform.parent.Find("Kicker");
                if (caption) TextShadow(caption.GetComponent<Text>());
            }
            if (hud.waterGroup) Frame(hud.waterGroup.transform as RectTransform, Card);
            if (hud.healthPanel) Frame(hud.healthPanel.transform as RectTransform, Card);
            foreach (var page in new[] { hud.pauseMenuGroup, hud.settingsPanel, hud.howToPlayPanel })
                if (page) Frame(page.transform as RectTransform, Page, 6f);
            if (hud.victoryTitleText) Frame(hud.victoryTitleText.transform.parent as RectTransform, Page, 6f);
            if (hud.pausePanel)
                foreach (var button in hud.pausePanel.GetComponentsInChildren<Button>(true)) StyleButton(button);
            if (hud.victoryPanel)
                foreach (var button in hud.victoryPanel.GetComponentsInChildren<Button>(true)) StyleButton(button);
            if (hud.howToPlayPanel)
                foreach (var label in hud.howToPlayPanel.GetComponentsInChildren<Text>(true)) HudInputEmphasis.Apply(label);
            if (hud.villageIntegrity)
            { hud.villageIntegrity.preparationHeight = 116; hud.villageIntegrity.defenseHeight = 204; }
            if (hud.contextualInteractions) ApplyPrompt(hud.contextualInteractions);
        }

        public void Layout(RescueHUD hud)
        {
            if (!hud || !hud.canvas || !hud.questTitleText || !hud.questBodyText || !hud.questHintText) return;
            HudInputEmphasis.Apply(hud.questBodyText); HudInputEmphasis.Apply(hud.questHintText); HudInputEmphasis.Apply(hud.promptText);
            float canvasWidth = ((RectTransform)hud.canvas.transform).rect.width;
            var quest = hud.questTitleText.transform.parent as RectTransform;
            float width = Mathf.Min(hud.objectiveWidth, canvasWidth * .32f), inner = width - horizontalPadding * 2;
            Size(quest, width, quest.rect.height);
            var kicker = quest.Find("QuestKicker") as RectTransform;
            if (kicker) Top(kicker, horizontalPadding, verticalPadding, inner, 24);
            float y = verticalPadding + 30;
            y += Text(hud.questTitleText, horizontalPadding, y, inner, 38) + 10;
            y += Text(hud.questBodyText, horizontalPadding, y, inner, 30);
            bool progress = !string.IsNullOrEmpty(hud.questHintText.text);
            hud.questHintText.gameObject.SetActive(progress);
            if (progress) y += 14 + Text(hud.questHintText, horizontalPadding, y + 14, inner, 26);
            if (hud.questFill && hud.questFill.transform.parent is RectTransform track)
            { track.gameObject.SetActive(progress); if (progress) { Top(track, horizontalPadding, y + 14, inner, 3); y += 17; } }
            Size(quest, width, y + verticalPadding);
            if (hud.eruptionText)
            {
                var timer = hud.eruptionText.transform.parent as RectTransform;
                Size(timer, Mathf.Min(540, canvasWidth * .36f), hud.villageIntegrity && hud.villageIntegrity.IsVisible ? 204 : 116);
                if (hud.villageIntegrity) hud.villageIntegrity.preparationHeight = 116;
                Top(hud.eruptionText.rectTransform, 44, 32, timer.rect.width - 88, 52);
                hud.eruptionText.alignment = TextAnchor.MiddleCenter;
                LayoutIntegrity(hud.villageIntegrity, timer.rect.width);
            }
            if (hud.objectiveText) { var panel = hud.objectiveText.transform.parent as RectTransform; Size(panel, 260, 104); Top(hud.objectiveText.rectTransform, 44, 32, 172, 40); }
            if (hud.speedText)
            {
                var panel = hud.speedText.transform.parent as RectTransform;
                NoBackground(panel); Size(panel, 220, 140);
                var title = panel.Find("Kicker") as RectTransform; if (title) Top(title, 44, 32, 132, 22);
                Top(hud.speedText.rectTransform, 44, 60, 132, 42);
            }
            LayoutResources(hud);
            Notification(hud.subtitleText, Mathf.Min(800, canvasWidth - 96), 36, 32);
            Notification(hud.toastText, 460, 36, 28);
        }

        public static void ConfigurePauseMenu(RescueHUD hud)
        {
            if (!hud || !hud.pauseMenuGroup || !hud.resumeButton || !hud.pauseSettingsButton || !hud.quitButton) return;
            // Keep the authored references for existing setup tools; only the three pause actions are exposed.
            foreach (var removed in new[] { hud.pauseHowToPlayButton, hud.restartButton, hud.pauseMenuButton })
                if (removed) removed.gameObject.SetActive(false);

            var page = (RectTransform)hud.pauseMenuGroup.transform;
            page.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 500f);
            var sizer = page.GetComponent<VolcanicRockFrameSizer>();
            if (sizer) { sizer.minHeight = 500f; sizer.bottomPadding = 54f; }

            var buttons = new[] { hud.resumeButton, hud.pauseSettingsButton, hud.quitButton };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].gameObject.SetActive(true);
                Top((RectTransform)buttons[i].transform, (page.rect.width - 420f) * .5f, 220f + i * 78f, 420f, 62f);
                // Controller/keyboard focus visits the same three visible actions as the mouse.
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = buttons[(i + buttons.Length - 1) % buttons.Length],
                    selectOnDown = buttons[(i + 1) % buttons.Length]
                };
            }
            var quitLabel = hud.quitButton.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (quitLabel) quitLabel.text = "Quit";
        }

        void LayoutResources(RescueHUD hud)
        {
            float waterTop = 60;
            if (hud.waterGroup)
            {
                var water = hud.waterGroup.transform as RectTransform;
                var caption = water.Find("WaterLabel") as RectTransform;
                if (caption) Top(caption, 44, 34, 252, 24);
                if (hud.waterText) Top(hud.waterText.rectTransform, 44, 64, 252, 32);
                if (hud.waterFill) Top(hud.waterFill.transform.parent as RectTransform, 44, 108, 252, 4);
                if (hud.pumpText) HudInputEmphasis.Apply(hud.pumpText);
                float description = hud.pumpText ? Text(hud.pumpText, 44, 126, 252, 28) : 28;
                float drivingControls = hud.LayoutDrivingHint(44, 126 + description + 10, 252);
                Size(water, 340, 126 + description + 36 + drivingControls);
                if (water.gameObject.activeSelf) waterTop = water.anchoredPosition.y + water.rect.height + 12;
            }
            if (hud.lowWaterWarning)
            {
                var warning = hud.lowWaterWarning.transform as RectTransform; Size(warning, 340, 96);
                var background = warning.GetComponent<Image>();
                if (background) { Color tint = background.color; tint.a = backgroundOpacity; background.color = tint; }
                Position(warning, new Vector2(-28, waterTop));
                if (hud.lowWaterText) Top(hud.lowWaterText.rectTransform, 44, 32, 252, 32);
                if (hud.lowWaterWarning.activeSelf) waterTop += 108;
            }
            if (hud.equipmentGroup)
            {
                var panel = hud.equipmentGroup.transform as RectTransform;
                if (!hud.equipmentSlots)
                {
                    Size(panel, 340, 104); Position(panel, new Vector2(-28, waterTop));
                    if (hud.equipmentText) Top(hud.equipmentText.rectTransform, 44, 34, 252, 36);
                }
            }
            if (hud.healthPanel)
            {
                var panel = hud.healthPanel.transform as RectTransform;
                Size(panel, 260, 126); Position(panel, new Vector2(28, 184));
                var caption = panel.Find("HealthKicker") as RectTransform;
                if (caption) Top(caption, 44, 30, 70, 30);
                if (hud.healthText) Top(hud.healthText.rectTransform, 128, 30, 88, 30);
                if (hud.healthFill) Top(hud.healthFill.transform.parent as RectTransform, 44, 84, 172, 4);
            }
            if (hud.targetGroup)
            {
                var panel = hud.targetGroup.transform as RectTransform; Size(panel, 360, 126);
                if (hud.targetText) Top(hud.targetText.rectTransform, 44, 30, 272, 38);
                if (hud.targetFill) Top(hud.targetFill.transform.parent as RectTransform, 44, 84, 272, 4);
            }
        }

        static void LayoutIntegrity(VillageIntegrityHUD status, float width)
        {
            if (!status || !status.defenseGroup) return;
            var root = status.defenseGroup.transform as RectTransform; Size(root, width, 204);
            var title = root.Find("Title") as RectTransform; if (title) Top(title, 44, 36, width - 176, 30);
            if (status.percentageText) Top(status.percentageText.rectTransform, width - 132, 32, 88, 36);
            if (status.villageFill) Top(status.villageFill.transform.parent as RectTransform, 44, 80, width - 88, 5);
            float cell = (width - 88 - 48) / Mathf.Max(1, status.houseFills.Length);
            for (int i = 0; i < status.houseFills.Length; i++)
            {
                if (i < status.houseLabels.Length && status.houseLabels[i]) Top(status.houseLabels[i].rectTransform, 44 + i * (cell + 12), 100, cell, 28);
                if (status.houseFills[i]) Top(status.houseFills[i].transform.parent as RectTransform, 44 + i * (cell + 12), 132, cell, 3);
            }
            if (status.statusText) Top(status.statusText.rectTransform, 44, 150, width - 88, 28);
        }
        static void Notification(Text label, float width, float padding, float minimum)
        {
            if (!label) return;
            var panel = label.transform.parent as RectTransform;
            float height = Text(label, 44, padding, width - 88, minimum);
            Size(panel, width, height + padding * 2);
        }
        void StyleButton(Button button)
        { if (button && button.targetGraphic is Image image) { image.sprite = Card; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 14; image.color = new Color(1, 1, 1, backgroundOpacity); foreach (var shadow in image.GetComponents<Shadow>()) shadow.enabled = false; } }
        void Frame(RectTransform panel, Sprite sprite, float density = 8)
        {
            if (!panel || !sprite) return;
            var image = panel.GetComponent<Image>(); if (!image) image = panel.gameObject.AddComponent<Image>();
            image.sprite = sprite; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = density;
            image.color = new Color(1, 1, 1, backgroundOpacity); image.raycastTarget = false;
            foreach (var shadow in image.GetComponents<Shadow>()) shadow.enabled = false;
            var core = panel.Find("ObsidianCore"); if (core) core.gameObject.SetActive(false);
            var accent = panel.Find("Accent"); if (accent) accent.gameObject.SetActive(false);
            accent = panel.Find("Objective accent"); if (accent) accent.gameObject.SetActive(false);
        }

        public void ApplyPrompt(ContextualInteractionUI ui)
        {
            if (!ui || !ui.promptRect) return;
            NoBackground(ui.promptRect);
            ui.detailText.richText = true;
            if (!ui.hud) return;
            Avoid(ui, ui.hud.waterGroup); Avoid(ui, ui.hud.equipmentGroup);
            Avoid(ui, ui.hud.lowWaterWarning); Avoid(ui, ui.hud.healthPanel);
        }
        static void NoBackground(RectTransform panel)
        {
            if (!panel) return;
            var image = panel.GetComponent<Image>(); if (image) image.enabled = false;
            HideDecoration(panel, "ObsidianCore"); HideDecoration(panel, "Accent"); HideDecoration(panel, "Objective accent");
        }
        static void HideDecoration(RectTransform panel, string name)
        { var decoration = panel.Find(name); if (decoration) decoration.gameObject.SetActive(false); }
        static void TextShadow(Text label)
        {
            if (!label) return;
            var shadow = label.GetComponent<Shadow>(); if (!shadow) shadow = label.gameObject.AddComponent<Shadow>();
            shadow.enabled = true; shadow.effectColor = new Color(0, 0, 0, .85f);
            shadow.effectDistance = new Vector2(1, -1);
        }
        static void Avoid(ContextualInteractionUI ui, GameObject panel)
        {
            if (!panel || !(panel.transform is RectTransform rect)) return;
            if (ui.avoidedHudRegions == null) ui.avoidedHudRegions = System.Array.Empty<RectTransform>();
            foreach (var existing in ui.avoidedHudRegions) if (existing == rect) return;
            int count = ui.avoidedHudRegions.Length;
            System.Array.Resize(ref ui.avoidedHudRegions, count + 1); ui.avoidedHudRegions[count] = rect;
        }

        public void LayoutPrompt(ContextualInteractionUI ui, bool vehicle, bool hasKey,
            float worldWidth, float worldTitle, float worldKey, float worldDetail)
        {
            ApplyPrompt(ui);
            float width = vehicle ? ui.vehiclePromptWidth : worldWidth, inner = width - 88;
            ui.titleText.fontSize = vehicle ? 24 : worldTitle;
            ui.keyText.fontSize = vehicle ? 30 : Mathf.Max(38, worldKey);
            ui.keyText.textWrappingMode = TextWrappingModes.NoWrap;
            float preferredKeyWidth = ui.keyText.GetPreferredValues(ui.keyText.text).x;
            if (preferredKeyWidth > inner - 28)
                ui.keyText.fontSize = Mathf.Max(18, ui.keyText.fontSize * (inner - 28) / preferredKeyWidth);
            ui.detailText.fontSize = vehicle ? 19 : worldDetail;
            ui.titleText.alignment = ui.detailText.alignment = vehicle ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            ui.keyText.alignment = TextAlignmentOptions.Center;
            float y = 36;
            y += Tmp(ui.titleText, y, inner, 34) + 12;
            float keyHeight = hasKey ? Mathf.Max(54, ui.keyText.fontSize * 1.45f) : 0;
            float keyTop = y;
            if (hasKey) { TmpRect(ui.keyText.rectTransform, y, inner, keyHeight); y += keyHeight + 14; }
            if (!string.IsNullOrEmpty(ui.detailText.text)) y += Tmp(ui.detailText, y, inner, 28) + 10;
            if (ui.progressGroup && ui.progressGroup.activeSelf) { TmpRect(ui.progressGroup.transform as RectTransform, y, Mathf.Min(140, inner), 4); y += 12; }
            Size(ui.promptRect, width, y + 26);
            var badge = ChildImage(ui.promptRect, "Input key badge"); badge.gameObject.SetActive(hasKey);
            var glow = ChildImage(ui.promptRect, "Input key glow"); glow.gameObject.SetActive(hasKey);
            if (!hasKey) return;
            float badgeWidth = Mathf.Clamp(ui.keyText.GetPreferredValues(ui.keyText.text).x + 28, 68, inner);
            TmpRect(badge.rectTransform, keyTop, badgeWidth, keyHeight);
            TmpRect(glow.rectTransform, keyTop - 16, badgeWidth + 40, keyHeight + 32);
            badge.color = new Color(.025f, .12f, .18f, .96f);
            var outline = badge.GetComponent<Outline>(); if (!outline) outline = badge.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1, -1); outline.effectColor = new Color(.25f, .78f, 1, .85f);
            glow.sprite = keyGlow; glow.color = new Color(.12f, .68f, 1, .62f);
            glow.rectTransform.SetAsFirstSibling(); badge.rectTransform.SetSiblingIndex(1);
        }
        static Image ChildImage(RectTransform root, string name)
        {
            var child = root.Find(name) as RectTransform;
            if (!child) { child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>(); child.SetParent(root, false); }
            var image = child.GetComponent<Image>(); image.raycastTarget = false; return image;
        }
        static float Tmp(TMP_Text text, float y, float width, float minimum)
        {
            float height = Mathf.Max(minimum, Mathf.Ceil(text.GetPreferredValues(text.text, width, 0).y));
            TmpRect(text.rectTransform, y, width, height); return height;
        }
        static void TmpRect(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
            Position(rect, new Vector2(0, -y)); Size(rect, width, height);
        }
        static float Text(Text text, float x, float y, float width, float minimum)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Overflow;
            Top(text.rectTransform, x, y, width, minimum);
            float height = Mathf.Max(minimum, Mathf.Ceil(text.preferredHeight));
            Size(text.rectTransform, width, height); return height;
        }
        static void Top(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            Position(rect, new Vector2(x, -y)); Size(rect, width, height);
        }
        static void Position(RectTransform rect, Vector2 position) { if (rect.anchoredPosition != position) rect.anchoredPosition = position; }
        static void Size(RectTransform rect, float width, float height)
        {
            if (!Mathf.Approximately(rect.rect.width, width)) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            if (!Mathf.Approximately(rect.rect.height, height)) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        }
        static Sprite Fitted(Sprite source, Vector4 border)
        {
            if (!source) return null;
            // The original PNG stays intact. Include the complete molten edge in the fixed slices,
            // so it cannot stretch into the text area when a long objective makes a card taller.
            var sprite = Sprite.Create(source.texture, source.rect, source.pivot / source.rect.size,
                source.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
            sprite.name = source.name + " (content-safe slices)"; sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
        void OnDisable()
        {
            if (fittedCard) { if (Application.isPlaying) Destroy(fittedCard); else DestroyImmediate(fittedCard); }
            if (fittedPage) { if (Application.isPlaying) Destroy(fittedPage); else DestroyImmediate(fittedPage); }
        }
    }
}
