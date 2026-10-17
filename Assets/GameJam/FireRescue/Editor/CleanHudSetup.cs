using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class CleanHudSetup
{
    public const string Reports = "Logs/CleanHUD";
    static readonly Color Paper = new Color(.94f, .955f, .97f);
    static readonly Color Muted = new Color(.64f, .70f, .76f);
    static readonly Color Slate = new Color(.026f, .034f, .043f, .93f);
    static readonly Color Orange = new Color(1f, .46f, .20f);
    static readonly Color Blue = new Color(.28f, .73f, 1f);
    static Font regular, semibold;

    [MenuItem("GameJam/Final Loop/Apply clean HUD")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use the authored Volcano_Demo in Edit Mode.");
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        if (hud && !hud.villageIntegrity && hud.eruptionText) hud.villageIntegrity = hud.eruptionText.GetComponentInParent<VillageIntegrityHUD>();
        if (!hud || !hud.canvas || !hud.questTitleText || !hud.villageIntegrity) throw new InvalidOperationException("Existing HUD references are required.");
        regular = Ashfall.UI.AshfallTypography.Regular;
        semibold = Ashfall.UI.AshfallTypography.Semibold;
        if (!regular || !semibold) throw new InvalidOperationException("The shared game fonts are missing.");
        Undo.RecordObjects(hud.canvas.GetComponentsInChildren<Component>(true).Where(c => c).Cast<Object>().ToArray(), "Clean gameplay HUD");
        foreach (var sizer in hud.canvas.GetComponentsInChildren<VolcanicRockFrameSizer>(true)) { sizer.enabled = false; Dirty(sizer); }
        foreach (var label in hud.canvas.GetComponentsInChildren<Text>(true))
        {
            bool strong = label.GetComponentInParent<Button>() || label.name.Contains("Title") || label.name.Contains("Value");
            Style(label, Mathf.Max(18, label.fontSize), strong, label.color);
            foreach (var fit in label.GetComponents<ContentSizeFitter>()) { fit.enabled = false; Dirty(fit); }
        }
        foreach (var image in hud.canvas.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "ObsidianCore") { image.gameObject.SetActive(false); Dirty(image.gameObject); continue; }
            string path = AssetDatabase.GetAssetPath(image.sprite);
            if (path.StartsWith("Assets/Art/UI/") && (path.Contains("Ashfall") || path.Contains("VOLKANOVSK")))
                Flat(image, image.GetComponent<Button>() ? new Color(.10f, .15f, .20f, 1f) : Slate);
        }
        foreach (var button in hud.canvas.GetComponentsInChildren<Button>(true))
        {
            if (button.targetGraphic is Image image) Flat(image, new Color(.10f, .15f, .20f, 1f));
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(.6f, .8f, 1f);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.4f, .6f, .8f); colors.fadeDuration = .12f;
            button.colors = colors; Dirty(button);
            foreach (var label in button.GetComponentsInChildren<Text>(true)) Style(label, 24, true, Paper);
        }

        var root = Rect(hud.canvas.transform, "Gameplay HUD");
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        root.localScale = Vector3.one;
        var group = root.GetComponent<CanvasGroup>(); if (!group) group = Undo.AddComponent<CanvasGroup>(root.gameObject);
        group.alpha = 1; group.interactable = group.blocksRaycasts = false;
        foreach (var child in hud.canvas.transform.Cast<Transform>().ToArray())
            if (child != root && child != hud.pausePanel.transform && child != hud.victoryPanel.transform && child != hud.subtitleText.transform.parent) child.SetParent(root, false);
        // Narrative captions remain visible while the opening hides the task HUD.
        hud.subtitleText.transform.parent.SetParent(hud.canvas.transform, false);
        root.SetAsFirstSibling(); hud.gameplayGroup = group; hud.cleanPresentation = true; hud.objectiveWidth = 440;

        var quest = hud.questTitleText.transform.parent as RectTransform;
        Panel(quest, new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, -36), new Vector2(440, 190));
        var kicker = quest.Find("QuestKicker").GetComponent<Text>(); kicker.text = "OBJECTIVE";
        Style(kicker, 16, true, Orange); Local(kicker.rectTransform, 24, 18, 392, 24);
        Style(hud.questTitleText, 32, true, Paper); Style(hud.questBodyText, 24, false, new Color(.84f, .87f, .89f));
        Style(hud.questHintText, 21, false, new Color(.63f, .83f, 1f));
        hud.questTitleText.text = "TAKE YOUR WEAPON";
        hud.questBodyText.text = "Pick up the pistol on the bedside cabinet. Press E when nearby.";
        hud.questHintText.text = "";
        foreach (var label in new[] { hud.questTitleText, hud.questBodyText, hud.questHintText })
        { label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow; Dirty(label); }
        Accent(quest, Orange);
        Bar(hud.questFill, Orange);

        var status = hud.objectiveText.transform.parent as RectTransform;
        // The former status card also owns water labels. Move them as one contextual group.
        var water = hud.waterGroup ? hud.waterGroup.transform as RectTransform : Rect(root, "Vehicle resources");
        hud.waterGroup = water.gameObject;
        var waterLabel = status.Find("WaterLabel") ?? water.Find("WaterLabel");
        if (waterLabel) waterLabel.SetParent(water, false);
        hud.waterText.transform.SetParent(water, false); hud.pumpText.transform.SetParent(water, false);
        hud.waterFill.transform.parent.SetParent(water, false);
        Panel(water, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 60), new Vector2(300, 122));
        if (waterLabel) { Style(waterLabel.GetComponent<Text>(), 13, true, Muted); Local(waterLabel as RectTransform, 18, 12, 264, 20); }
        Style(hud.waterText, 23, true, Paper); Local(hud.waterText.rectTransform, 18, 34, 264, 32);
        Bar(hud.waterFill, Blue); Local(hud.waterFill.transform.parent as RectTransform, 18, 74, 264, 4);
        Style(hud.pumpText, 16, false, Muted); Local(hud.pumpText.rectTransform, 18, 88, 264, 24);
        water.gameObject.SetActive(false);
        Panel(status, Vector2.one, Vector2.one, new Vector2(-36, -36), new Vector2(240, 68));
        var statusKicker = status.Find("StatusKicker"); if (statusKicker) statusKicker.gameObject.SetActive(false);
        Style(hud.objectiveText, 24, true, Paper); hud.objectiveText.text = "Houses   5 / 5";
        Local(hud.objectiveText.rectTransform, 20, 14, 200, 40); hud.objectiveText.alignment = TextAnchor.MiddleCenter;

        var timer = hud.eruptionText.transform.parent as RectTransform;
        Panel(timer, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(480, 76));
        Style(hud.eruptionText, 30, true, Paper); Local(hud.eruptionText.rectTransform, 20, 15, 440, 46);
        hud.eruptionText.alignment = TextAnchor.MiddleCenter; hud.eruptionText.text = "VOLCANO ERUPTION IN: 02:00";
        CompactIntegrity(hud.villageIntegrity);

        var speed = hud.speedText.transform.parent as RectTransform;
        Panel(speed, Vector2.zero, Vector2.zero, new Vector2(28, 28), new Vector2(162, 78));
        var speedKicker = speed.Find("Kicker").GetComponent<Text>(); speedKicker.text = "SPEED";
        Style(speedKicker, 12, true, Muted); Local(speedKicker.rectTransform, 16, 10, 130, 18);
        Style(hud.speedText, 27, true, Paper); Local(hud.speedText.rectTransform, 16, 30, 130, 36);
        speed.gameObject.SetActive(false);
        var health = hud.healthPanel.transform as RectTransform;
        Panel(health, Vector2.zero, Vector2.zero, new Vector2(28, 122), new Vector2(220, 72));
        var healthKicker = health.Find("HealthKicker").GetComponent<Text>(); Style(healthKicker, 13, true, Muted);
        Local(healthKicker.rectTransform, 16, 10, 76, 24); Style(hud.healthText, 19, true, Paper);
        Local(hud.healthText.rectTransform, 96, 10, 108, 24); hud.healthText.alignment = TextAnchor.MiddleRight;
        Bar(hud.healthFill, new Color(.45f, .85f, .65f)); Local(hud.healthFill.transform.parent as RectTransform, 16, 48, 188, 4);

        var equipment = hud.equipmentGroup.transform as RectTransform;
        Panel(equipment, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 60), new Vector2(300, 48));
        Style(hud.equipmentText, 20, true, Paper); Local(hud.equipmentText.rectTransform, 16, 9, 268, 30);
        equipment.gameObject.SetActive(false);
        var pause = root.Find("PauseHint") as RectTransform;
        Panel(pause, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 20), new Vector2(130, 28));
        pause.GetComponent<Image>().color = Color.clear;
        var pauseText = pause.GetComponentInChildren<Text>(); pauseText.text = "Esc  Pause"; Style(pauseText, 16, false, Muted);
        Local(pauseText.rectTransform, 0, 0, 130, 28); pauseText.alignment = TextAnchor.MiddleRight;

        var subtitle = hud.subtitleText.transform.parent as RectTransform;
        Panel(subtitle, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 150), new Vector2(680, 64));
        Style(hud.subtitleText, 26, false, Paper); hud.subtitleText.horizontalOverflow = HorizontalWrapMode.Wrap;
        hud.subtitleText.alignment = TextAnchor.MiddleCenter;
        var toast = hud.toastText.transform.parent as RectTransform;
        Panel(toast, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 150), new Vector2(460, 64));
        Style(hud.toastText, 19, true, new Color(.6f, .9f, .73f)); hud.toastText.alignment = TextAnchor.MiddleCenter;

        var target = hud.targetGroup.transform as RectTransform;
        Panel(target, new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 60), new Vector2(320, 64));
        Style(hud.targetText, 18, true, Paper); Local(hud.targetText.rectTransform, 18, 9, 284, 26); hud.targetText.alignment = TextAnchor.MiddleCenter;
        Bar(hud.targetFill, Blue); Local(hud.targetFill.transform.parent as RectTransform, 18, 46, 284, 3);
        var warning = hud.lowWaterWarning.transform as RectTransform;
        Panel(warning, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 194), new Vector2(300, 44));
        Style(hud.lowWaterText, 17, true, Paper); Local(hud.lowWaterText.rectTransform, 12, 8, 276, 28);

        var navigation = Object.FindFirstObjectByType<ObjectiveNavigation>(); navigation.compactLabels = true;
        navigation.marker.sizeDelta = new Vector2(180, 88); navigation.edgeMargin = 66;
        if (navigation.marker.TryGetComponent<Image>(out var markerBackground)) { markerBackground.color = Color.clear; Dirty(markerBackground); }
        Style(navigation.label, 18, true, Paper); Local(navigation.label.rectTransform, 0, 28, 180, 58); navigation.label.alignment = TextAnchor.UpperCenter;
        navigation.label.verticalOverflow = VerticalWrapMode.Overflow;
        navigation.arrow.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); navigation.arrow.fontSize = 23;
        Local(navigation.arrow.rectTransform, 75, 0, 30, 26); navigation.arrow.alignment = TextAnchor.MiddleCenter;
        foreach (var label in new[] { navigation.label, navigation.arrow })
        {
            var shadow = label.GetComponent<Shadow>(); if (!shadow) shadow = Undo.AddComponent<Shadow>(label.gameObject);
            shadow.enabled = true; shadow.effectColor = new Color(0, 0, 0, .8f); shadow.effectDistance = new Vector2(0, -1); Dirty(shadow);
        }
        Dirty(navigation); Dirty(hud); Dirty(group); Dirty(root);
        if (hud.promptText) hud.promptText.transform.parent.gameObject.SetActive(false);
        Canvas.ForceUpdateCanvases(); hud.RefreshCleanLayout();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Inspect(); Validate();
    }

    static void CompactIntegrity(VillageIntegrityHUD status)
    {
        status.compactLabels = true; status.preparationHeight = 76; status.defenseHeight = 132;
        var root = status.defenseGroup.transform as RectTransform; Local(root, 0, 0, 480, 132);
        var title = root.Find("Title").GetComponent<Text>(); title.text = "VILLAGE INTEGRITY";
        Style(title, 18, true, Muted); Local(title.rectTransform, 24, 12, 310, 28);
        Style(status.percentageText, 26, true, Paper); Local(status.percentageText.rectTransform, 366, 8, 90, 34);
        Bar(status.villageFill, new Color(.45f, .85f, .65f)); Local(status.villageFill.transform.parent as RectTransform, 24, 46, 432, 5);
        float width = (432f - 4f * 12f) / Mathf.Max(1, status.houseFills.Length);
        for (int i = 0; i < status.houseFills.Length; i++)
        {
            float x = 24 + i * (width + 12);
            Style(status.houseLabels[i], 18, false, Muted); Local(status.houseLabels[i].rectTransform, x, 60, width, 24);
            Bar(status.houseFills[i], new Color(.45f, .85f, .65f)); Local(status.houseFills[i].transform.parent as RectTransform, x, 87, width, 3);
        }
        Style(status.statusText, 17, false, Muted); Local(status.statusText.rectTransform, 24, 101, 432, 24);
        status.Refresh(false); Dirty(status);
    }
    static RectTransform Rect(Transform parent, string name)
    {
        var found = parent.Find(name) as RectTransform; if (found) return found;
        var go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Clean HUD layout");
        go.transform.SetParent(parent, false); return go.transform as RectTransform;
    }
    static void Style(Text text, int size, bool strong, Color color)
    {
        text.font = strong ? semibold : regular; text.fontSize = size; text.fontStyle = FontStyle.Normal;
        text.color = color; text.lineSpacing = 1.05f; text.resizeTextForBestFit = false; text.raycastTarget = false;
        foreach (var shadow in text.GetComponents<Shadow>()) { shadow.enabled = false; Dirty(shadow); }
        Dirty(text);
    }
    static void Flat(Image image, Color color)
    {
        image.sprite = null; image.type = Image.Type.Simple; image.pixelsPerUnitMultiplier = 1;
        image.material = null;
        foreach (var shadow in image.GetComponents<Shadow>()) { shadow.enabled = false; Dirty(shadow); }
        image.color = color; image.raycastTarget = image.GetComponent<Button>() || image.GetComponent<CanvasGroup>(); Dirty(image);
    }
    static void Panel(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = rect.GetComponent<Image>(); if (!image) image = Undo.AddComponent<Image>(rect.gameObject);
        Flat(image, Slate); Dirty(rect);
        var outline = rect.GetComponent<Outline>(); if (!outline) outline = Undo.AddComponent<Outline>(rect.gameObject);
        outline.enabled = true; outline.effectColor = new Color(.52f, .62f, .68f, .28f); outline.effectDistance = new Vector2(1, -1); Dirty(outline);
    }
    static void Local(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); Dirty(rect);
    }
    static void Accent(RectTransform parent, Color color)
    {
        var rect = Rect(parent, "Objective accent"); rect.anchorMin = Vector2.zero; rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, .5f); rect.offsetMin = Vector2.zero; rect.offsetMax = new Vector2(2, 0);
        var image = rect.GetComponent<Image>(); if (!image) image = Undo.AddComponent<Image>(rect.gameObject);
        image.color = color; image.raycastTarget = false; Dirty(image); Dirty(rect);
    }
    static void Bar(Image fill, Color color)
    {
        fill.color = color; fill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Horizontal;
        var parent = fill.transform.parent.GetComponent<Image>(); if (parent) { parent.sprite = null; parent.type = Image.Type.Simple; parent.color = new Color(.20f, .25f, .29f, .8f); Dirty(parent); }
        var rect = fill.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        Dirty(fill); Dirty(rect);
    }
    static void Dirty(Object item) => EditorUtility.SetDirty(item);

    [MenuItem("GameJam/Final Loop/Validate clean HUD")]
    public static void Validate()
    {
        var hud = Object.FindFirstObjectByType<RescueHUD>(); var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        if (!hud || !hud.cleanPresentation || !hud.gameplayGroup || !hud.waterGroup || !nav.compactLabels)
            throw new InvalidOperationException("Clean HUD bindings are incomplete.");
        var labels = hud.canvas.GetComponentsInChildren<Text>(true);
        if (labels.Any(t => t.font == Ashfall.UI.AshfallTypography.Modular)) throw new InvalidOperationException("A narrow Modular label remains on the HUD.");
        if (hud.canvas.GetComponentsInChildren<Image>(true).Any(i => AssetDatabase.GetAssetPath(i.sprite).StartsWith("Assets/Art/UI/")))
            throw new InvalidOperationException("Decorative artwork remains on the HUD.");
        if (hud.canvas.GetComponentsInChildren<VolcanicRockFrameSizer>(true).Any(s => s.enabled)) throw new InvalidOperationException("An old frame sizer still controls the layout.");
        if (!hud.questBodyText.font.HasCharacter('ğ') || !hud.questBodyText.font.HasCharacter('ı')) throw new InvalidOperationException("Turkish font glyph missing.");
        FinalLoopSetup.Validate(); ContextualInteractionSetup.Validate();
        Directory.CreateDirectory(Reports); File.WriteAllText(Reports + "/validation.txt", "PASS existing HUD, responsive layout, Turkish glyphs, clean panel surfaces, menus, objective and contextual bindings.\n");
    }
    [MenuItem("GameJam/Final Loop/Inspect clean HUD")]
    public static void Inspect()
    {
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        var text = new StringBuilder("Scene=" + SceneManager.GetActiveScene().path + "; play=" + EditorApplication.isPlaying + "; dirty=" + SceneManager.GetActiveScene().isDirty + "\n");
        text.AppendLine("HUD=" + hud + "; canvas=" + hud.canvas + "; title=" + hud.questTitleText + "; integrity=" + hud.villageIntegrity);
        text.AppendLine("Canvas=" + hud.canvas.name + " scaler=" + EditorJsonUtility.ToJson(hud.canvas.GetComponent<CanvasScaler>()));
        foreach (var t in hud.canvas.GetComponentsInChildren<RectTransform>(true))
        {
            if (t.parent != hud.canvas.transform && !t.GetComponent<Text>() && !t.GetComponent<VolcanicRockFrameSizer>()) continue;
            var label = t.GetComponent<Text>(); var image = t.GetComponent<Image>();
            text.AppendLine(PathOf(t) + " | anchors=" + t.anchorMin + "/" + t.anchorMax + "; pivot=" + t.pivot + "; pos=" + t.anchoredPosition + "; size=" + t.sizeDelta +
                (label ? " TEXT '" + label.text.Replace("\n", " / ") + "' font=" + label.font.name + " size=" + label.fontSize : "") + (image ? " IMAGE " + AssetDatabase.GetAssetPath(image.sprite) : ""));
        }
        Directory.CreateDirectory(Reports); File.WriteAllText(Reports + "/inspection.txt", text.ToString());
    }
    static string PathOf(Transform t) => t.parent ? PathOf(t.parent) + "/" + t.name : t.name;
}
