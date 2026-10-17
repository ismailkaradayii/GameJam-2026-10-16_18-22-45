#if UNITY_EDITOR
using System.Collections.Generic;
using Ashfall.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Rebuilds the existing menu panels in place and wires their controls.</summary>
public static class AshfallSettingsSceneBuilder
{
    private static readonly Color Ink = Hex("101010");
    private static readonly Color Panel = Hex("181716");
    private static readonly Color Card = Hex("22201E");
    private static readonly Color Accent = Hex("E85D2A");
    private static readonly Color AccentLight = Hex("FF9A45");
    private static readonly Color MainText = Hex("F2E9DF");
    private static readonly Color MutedText = Hex("AAA19A");
    private const string LavaPlatePath = "Assets/Art/UI/AshfallMenuButton.png";
    private const string SettingsPanelPath = "Assets/Art/UI/AshfallSettingsPanel.png";
    private const string VolumeHandlePath = "Assets/Art/UI/slotkaydırma.png";
    private static Font uiFont;

    [MenuItem("ASHFALL/Refresh Volcano Gameplay Help")]
    public static void RefreshGameplayHelp()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        Transform canvas = GameObject.Find("Canvas")?.transform;
        Transform panel = canvas != null ? FindChild(canvas, "HowToPlayPanel") : null;
        AshfallMenuController menu = Object.FindFirstObjectByType<AshfallMenuController>();
        if (panel == null || menu == null) throw new System.Exception("Main menu help panel is missing.");
        ClearChildren(panel);
        BuildHowToContents(panel);
        Button back = FindChild(panel, "HowToBackButton").GetComponent<Button>();
        var serialized = new SerializedObject(menu);
        serialized.FindProperty("howToPlayBackButton").objectReferenceValue = back;
        serialized.FindProperty("howToPlayInitialSelection").objectReferenceValue = back;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        HideGroup(Ensure<CanvasGroup>(panel.gameObject));
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("ASHFALL/Build Settings and How to Play UI")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        PrepareLavaPlateSprite();
        PrepareSettingsPanelSprite();
        Transform canvas = GameObject.Find("Canvas")?.transform;
        if (canvas == null) throw new System.Exception("MainMenu has no existing Canvas.");

        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) throw new System.Exception("The existing Canvas has no CanvasScaler.");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        Transform settingsTransform = FindChild(canvas, "SettingsPanel");
        Transform howToTransform = FindChild(canvas, "HowToPlayPanel");
        Transform controllerTransform = GameObject.Find("SettingsController")?.transform;
        if (settingsTransform == null || howToTransform == null || controllerTransform == null)
            throw new System.Exception("Expected existing SettingsPanel, HowToPlayPanel, and SettingsController objects were not found.");

        CanvasGroup settingsGroup = Ensure<CanvasGroup>(settingsTransform.gameObject);
        CanvasGroup howToGroup = Ensure<CanvasGroup>(howToTransform.gameObject);
        AshfallSettingsController settingsController = Ensure<AshfallSettingsController>(controllerTransform.gameObject);

        ClearChildren(settingsTransform);
        ClearChildren(howToTransform);
        StylePanel(settingsTransform, settingsGroup, new Vector2(860f, 930f));
        StylePanel(howToTransform, howToGroup, new Vector2(860f, 900f));

        BuildSettingsContents(settingsTransform, settingsController);
        BuildHowToContents(howToTransform);

        AshfallMenuController menuController = Object.FindFirstObjectByType<AshfallMenuController>();
        if (menuController == null) throw new System.Exception("AshfallMenuController was not found in MainMenu.");
        Button settingsBack = FindChild(settingsTransform, "SettingsBackButton").GetComponent<Button>();
        Button howToBack = FindChild(howToTransform, "HowToBackButton").GetComponent<Button>();
        SerializedObject menuSerialized = new SerializedObject(menuController);
        menuSerialized.FindProperty("settingsPanel").objectReferenceValue = settingsGroup;
        menuSerialized.FindProperty("howToPlayPanel").objectReferenceValue = howToGroup;
        menuSerialized.FindProperty("settingsBackButton").objectReferenceValue = settingsBack;
        menuSerialized.FindProperty("howToPlayBackButton").objectReferenceValue = howToBack;
        menuSerialized.FindProperty("settingsInitialSelection").objectReferenceValue = FindChild(settingsTransform, "Slider")?.GetComponent<Selectable>();
        menuSerialized.FindProperty("howToPlayInitialSelection").objectReferenceValue = howToBack;
        menuSerialized.FindProperty("settingsController").objectReferenceValue = settingsController;
        menuSerialized.ApplyModifiedPropertiesWithoutUndo();

        settingsTransform.SetAsLastSibling();
        howToTransform.SetAsLastSibling();
        HideGroup(settingsGroup);
        HideGroup(howToGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("ASHFALL: Existing SettingsPanel and HowToPlayPanel rebuilt, wired, and saved on the existing Canvas.");
    }

    private static void BuildSettingsContents(Transform panel, AshfallSettingsController controller)
    {
        Text title = MakeText("SettingsTitle", panel, "SETTINGS", 34, MainText, TextAnchor.MiddleLeft);
        Anchored(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -82f), new Vector2(-44f, -34f), new Vector2(0.5f, 1f));
        Text subtitle = MakeText("SettingsSubtitle", panel, "Tune the experience to your system.", 16, MutedText, TextAnchor.MiddleLeft);
        Anchored(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -124f), new Vector2(-44f, -84f), new Vector2(0.5f, 1f));
        MakeRule("SettingsTitleRule", panel, Accent, new Vector2(44f, -148f), new Vector2(-44f, -147f));

        ScrollRect scroll = CreateScrollView("SettingsScrollView", panel, new Vector2(44f, 100f), new Vector2(-44f, -164f));
        Transform content = scroll.content;
        AddSectionTitle(content, "AUDIO");
        Slider master = AddSliderRow(content, "MASTER VOLUME", "Controls all menu and gameplay audio.", out Text masterValue);
        Slider music = AddSliderRow(content, "MUSIC VOLUME", "Ambience and music category.", out Text musicValue);
        Slider sfx = AddSliderRow(content, "SOUND EFFECTS", "Impacts, eruption, and interface sounds.", out Text sfxValue);
        AddSectionTitle(content, "GRAPHICS");
        Dropdown resolution = AddDropdownRow(content, "RESOLUTION", "Choose a supported display resolution.");
        Dropdown display = AddDropdownRow(content, "DISPLAY MODE", "Fullscreen, windowed, or borderless.");
        Dropdown quality = AddDropdownRow(content, "QUALITY", "Uses the quality levels configured in this project.");
        Dropdown vsync = AddDropdownRow(content, "V-SYNC", "Synchronize rendering with the display refresh.");
        Dropdown frameRate = AddDropdownRow(content, "FRAME RATE LIMIT", "Refresh rate and hardware can limit actual FPS.");
        Button defaults = MakeButton("RestoreDefaultsButton", content, "RESTORE DEFAULTS", Accent, MainText, 52f);
        StylePanelButton(defaults, true);

        Button back = MakeButton("SettingsBackButton", panel, "BACK", Card, MainText, 58f);
        StylePanelButton(back, true);
        Anchored(back.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(44f, 28f), new Vector2(-44f, 86f), new Vector2(0.5f, 0f));

        CanvasGroup confirmation = BuildConfirmationDialog(panel, out Text dialogTitle, out Text dialogMessage,
            out Text countdown, out Button confirmButton, out Text confirmLabel, out Button cancelButton, out Text cancelLabel);

        SerializedObject serialized = new SerializedObject(controller);
        Assign(serialized, "masterSlider", master);
        Assign(serialized, "masterValue", masterValue);
        Assign(serialized, "musicSlider", music);
        Assign(serialized, "musicValue", musicValue);
        Assign(serialized, "sfxSlider", sfx);
        Assign(serialized, "sfxValue", sfxValue);
        Assign(serialized, "resolutionDropdown", resolution);
        Assign(serialized, "displayModeDropdown", display);
        Assign(serialized, "qualityDropdown", quality);
        Assign(serialized, "vsyncDropdown", vsync);
        Assign(serialized, "frameRateDropdown", frameRate);
        Assign(serialized, "restoreDefaultsButton", defaults);
        Assign(serialized, "confirmationGroup", confirmation);
        Assign(serialized, "confirmationTitle", dialogTitle);
        Assign(serialized, "confirmationMessage", dialogMessage);
        Assign(serialized, "confirmationCountdown", countdown);
        Assign(serialized, "confirmationConfirmButton", confirmButton);
        Assign(serialized, "confirmationConfirmLabel", confirmLabel);
        Assign(serialized, "confirmationCancelButton", cancelButton);
        Assign(serialized, "confirmationCancelLabel", cancelLabel);

        AudioSource ambience = GameObject.Find("VolcanoAmbience")?.GetComponent<AudioSource>();
        AudioSource eruption = GameObject.Find("EruptionSound")?.GetComponent<AudioSource>();
        SerializedProperty musicArray = serialized.FindProperty("musicSources");
        musicArray.arraySize = ambience != null ? 1 : 0;
        if (musicArray.arraySize > 0) musicArray.GetArrayElementAtIndex(0).objectReferenceValue = ambience;
        SerializedProperty sfxArray = serialized.FindProperty("sfxSources");
        sfxArray.arraySize = eruption != null ? 1 : 0;
        if (sfxArray.arraySize > 0) sfxArray.GetArrayElementAtIndex(0).objectReferenceValue = eruption;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void BuildHowToContents(Transform panel)
    {
        Text title = MakeText("HowToTitle", panel, "HOW TO PLAY", 34, MainText, TextAnchor.MiddleLeft);
        Anchored(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -82f), new Vector2(-44f, -34f), new Vector2(0.5f, 1f));
        Text subtitle = MakeText("HowToSubtitle", panel, "You have 120 seconds to prepare. Then defend the village.", 16, MutedText, TextAnchor.MiddleLeft);
        Anchored(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(44f, -124f), new Vector2(-44f, -84f), new Vector2(0.5f, 1f));
        MakeRule("HowToTitleRule", panel, Accent, new Vector2(44f, -148f), new Vector2(-44f, -147f));

        ScrollRect scroll = CreateScrollView("HowToScrollView", panel, new Vector2(44f, 100f), new Vector2(-44f, -164f));
        Transform content = scroll.content;
        Text status = MakeInfoCard(content, "VOLCANO DEMO — DEFEND THE TOWN", "Take the pistol from your bedroom with E and leave the house. Call villagers with Q and lead them to the GREEN shelter ring. Enter the fire truck behind the house and fill its tank with E at the BLUE lake marker. The volcano erupts after 120 seconds, even if preparations are incomplete. Stop three waves of fire spirits and extinguish burning houses. You win if at least one house survives; you lose if all are destroyed. Stay away from hot lava.");
        status.fontSize = 17;
        var missionCardLayout = status.transform.parent.GetComponent<LayoutElement>();
        missionCardLayout.preferredHeight = -1f; missionCardLayout.minHeight = 138f;
        AddSectionTitle(content, "MOVEMENT AND VEHICLE");
        AddControlRow(content, "MOVE", "WASD", "Move on foot or drive. Look around with the mouse.", true);
        AddControlRow(content, "SPRINT", "SHIFT", "Sprint on foot.", false);
        AddControlRow(content, "ENTER / EXIT", "E / F", "Press E at the driver's door to enter; stop and press F to exit.", false);
        AddControlRow(content, "BRAKE / JUMP", "SPACE", "Brake in the vehicle; jump on foot with the weapon drawn.", false);
        AddSectionTitle(content, "HOSE AND WATER");
        AddControlRow(content, "FILL TANK", "E", "Stop in the BLUE lake ring and press E to connect. The hose retracts when the tank is full.", false);
        AddControlRow(content, "MANUAL HOSE", "E / P", "On foot, take the hose from the rear of the truck, drop its intake in the lake, and press P to start the pump.", false);
        AddControlRow(content, "RETRACT", "R", "Retract the hose at the rear before entering the truck.", false);
        AddControlRow(content, "WATER CANNON", "RMB", "Hold right click in the truck to extinguish houses, cool lava, and spray spirits. The marker shows the predicted impact point; allow time for the water to reach it.", false);
        AddSectionTitle(content, "WEAPON");
        AddControlRow(content, "PICK UP / SELECT", "E / 1", "Press E to take the bedside pistol. Press 1 to draw or holster it, or 0 to holster. Your weapon returns after exiting the truck.", false);
        AddControlRow(content, "FIRE / AIM", "LMB / RMB", "On foot, left click to fire and right click to aim.", false);
        AddControlRow(content, "RELOAD", "R", "Reload when the rear hose interaction is unavailable.", false);
        AddControlRow(content, "CROUCH", "C", "Crouch or stand with the weapon drawn.", false);
        AddSectionTitle(content, "OTHER");
        AddControlRow(content, "PAUSE", "ESC", "Pause the game; resume or return to the main menu.", false);
        AddControlRow(content, "EVACUATE", "Q", "Call out near the houses and lead the villagers to shelter.", false);

        Button back = MakeButton("HowToBackButton", panel, "BACK", Card, MainText, 58f);
        StylePanelButton(back, true);
        Anchored(back.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(44f, 28f), new Vector2(-44f, 86f), new Vector2(0.5f, 0f));
    }

    private static void AddSectionTitle(Transform content, string text)
    {
        Text heading = MakeText(text.Replace(' ', '_') + "Section", content, text, 17, AccentLight, TextAnchor.MiddleLeft);
        heading.font = Ashfall.UI.AshfallTypography.Semibold;
        heading.fontStyle = FontStyle.Normal;
        LayoutElement layout = heading.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 34f;
        layout.minHeight = 34f;
    }

    private static Slider AddSliderRow(Transform content, string title, string description, out Text valueText)
    {
        RectTransform row = MakeRow(content, title + "Row", 76f);
        Transform labels = MakeRect("Labels", row);
        LayoutElement labelsLayout = labels.gameObject.AddComponent<LayoutElement>();
        labelsLayout.flexibleWidth = 1f;
        labelsLayout.minWidth = 150f;
        VerticalLayoutGroup vertical = labels.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 3f;
        vertical.childAlignment = TextAnchor.MiddleLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        MakeText("Label", labels, title, 15, MainText, TextAnchor.MiddleLeft);
        MakeText("Description", labels, description, 12, MutedText, TextAnchor.MiddleLeft);
        Slider slider = CreateSlider("Slider", row, 320f);
        valueText = MakeText("Value", row, "100%", 15, AccentLight, TextAnchor.MiddleRight);
        LayoutElement valueLayout = valueText.gameObject.AddComponent<LayoutElement>();
        valueLayout.preferredWidth = 58f;
        valueLayout.minWidth = 58f;
        return slider;
    }

    private static Dropdown AddDropdownRow(Transform content, string title, string description)
    {
        RectTransform row = MakeRow(content, title + "Row", 78f);
        Transform labels = MakeRect("Labels", row);
        LayoutElement labelsLayout = labels.gameObject.AddComponent<LayoutElement>();
        labelsLayout.flexibleWidth = 1f;
        labelsLayout.minWidth = 150f;
        VerticalLayoutGroup vertical = labels.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 3f;
        vertical.childAlignment = TextAnchor.MiddleLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        MakeText("Label", labels, title, 15, MainText, TextAnchor.MiddleLeft);
        MakeText("Description", labels, description, 12, MutedText, TextAnchor.MiddleLeft);
        return CreateDropdown("Dropdown", row, 310f);
    }

    private static RectTransform MakeRow(Transform parent, string name, float height)
    {
        RectTransform row = MakeRect(name, parent);
        Image bg = row.gameObject.AddComponent<Image>();
        bg.color = new Color(Ink.r, Ink.g, Ink.b, 0.48f);
        bg.raycastTarget = false;
        AddLavaEdge(row, "RowLavaEdge");
        LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = -1f;
        layout.minHeight = Mathf.Max(height, 88f);
        HorizontalLayoutGroup horizontal = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        horizontal.padding = new RectOffset(18, 18, 10, 10);
        horizontal.spacing = 16f;
        horizontal.childAlignment = TextAnchor.MiddleLeft;
        horizontal.childControlWidth = true;
        horizontal.childControlHeight = true;
        horizontal.childForceExpandWidth = false;
        horizontal.childForceExpandHeight = false;
        return row;
    }

    private static void AddControlRow(Transform content, string title, string keys, string description, bool movement)
    {
        RectTransform row = MakeRow(content, title + "ControlRow", movement ? 100f : 84f);
        Transform labels = MakeRect("Labels", row);
        LayoutElement labelsLayout = labels.gameObject.AddComponent<LayoutElement>();
        labelsLayout.flexibleWidth = 1f;
        labelsLayout.minWidth = 120f;
        VerticalLayoutGroup vertical = labels.gameObject.AddComponent<VerticalLayoutGroup>();
        vertical.spacing = 3f;
        vertical.childAlignment = TextAnchor.MiddleLeft;
        vertical.childControlWidth = true;
        vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;
        MakeText("Action", labels, title, 16, MainText, TextAnchor.MiddleLeft);
        MakeText("Description", labels, description, 13, MutedText, TextAnchor.MiddleLeft);
        if (movement)
        {
            RectTransform cluster = MakeRect("WASDKeyCluster", row);
            LayoutElement clusterLayout = cluster.gameObject.AddComponent<LayoutElement>();
            clusterLayout.preferredWidth = 162f;
            clusterLayout.minWidth = 162f;
            clusterLayout.preferredHeight = 46f;
            HorizontalLayoutGroup clusterLayoutGroup = cluster.gameObject.AddComponent<HorizontalLayoutGroup>();
            clusterLayoutGroup.spacing = 5f;
            clusterLayoutGroup.childAlignment = TextAnchor.MiddleCenter;
            clusterLayoutGroup.childControlWidth = true;
            clusterLayoutGroup.childControlHeight = true;
            clusterLayoutGroup.childForceExpandWidth = false;
            clusterLayoutGroup.childForceExpandHeight = false;
            for (int i = 0; i < keys.Length; i++) MakeKeycap(cluster, keys[i].ToString(), 34f);
        }
        else
        {
            MakeKeycap(row, keys, keys.Length > 6 ? 145f : 106f);
        }
    }

    private static void MakeKeycap(Transform parent, string label, float width)
    {
        RectTransform keycap = MakeRect(label + "Keycap", parent);
        Image background = keycap.gameObject.AddComponent<Image>();
        background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LavaPlatePath);
        background.type = Image.Type.Sliced;
        background.color = new Color(0.40f, 0.36f, 0.34f, 0.96f);
        background.raycastTarget = false;
        AddOutline(background, new Color(Accent.r, Accent.g, Accent.b, 0.72f), 1.25f);
        Text keyLabel = MakeText("KeyLabel", keycap, label, label.Length > 6 ? 13 : 15, AccentLight, TextAnchor.MiddleCenter);
        Anchored(keyLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(3f, 2f), new Vector2(-3f, -2f), new Vector2(0.5f, 0.5f));
        LayoutElement layout = keycap.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.minWidth = width;
        layout.preferredHeight = 44f;
        layout.minHeight = 44f;
    }

    private static Text MakeInfoCard(Transform parent, string heading, string body)
    {
        RectTransform card = MakeRect("GameplayInputNotice", parent);
        Image image = card.gameObject.AddComponent<Image>();
        image.color = new Color(Ink.r, Ink.g, Ink.b, 0.84f);
        LayoutElement layout = card.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 88f;
        layout.minHeight = 88f;
        VerticalLayoutGroup group = card.gameObject.AddComponent<VerticalLayoutGroup>();
        group.padding = new RectOffset(18, 18, 10, 10);
        group.spacing = 4f;
        group.childAlignment = TextAnchor.MiddleLeft;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = false;
        AddLavaEdge(card, "NoticeLavaEdge");
        MakeText("NoticeTitle", card, heading, 14, AccentLight, TextAnchor.MiddleLeft);
        return MakeText("NoticeBody", card, body, 13, MainText, TextAnchor.MiddleLeft);
    }

    private static ScrollRect CreateScrollView(string name, Transform parent, Vector2 offsetMin, Vector2 offsetMax)
    {
        RectTransform root = MakeRect(name, parent);
        Anchored(root, Vector2.zero, Vector2.one, offsetMin, offsetMax, new Vector2(0.5f, 0.5f));
        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        Image viewportImage = root.gameObject.AddComponent<Image>();
        viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
        Mask mask = root.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        RectTransform content = MakeRect("Content", root);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 12, 8, 8);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = root;
        scroll.content = content;
        return scroll;
    }

    private static Slider CreateSlider(string name, Transform parent, float width)
    {
        RectTransform root = MakeRect(name, parent);
        LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.minWidth = 180f;
        layout.preferredHeight = 40f;
        layout.minHeight = 36f;
        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;
        RectTransform track = MakeRect("Track", root);
        Anchored(track, Vector2.zero, Vector2.one, new Vector2(0f, 7f), new Vector2(0f, -7f), new Vector2(0.5f, 0.5f));
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LavaPlatePath);
        trackImage.type = Image.Type.Sliced;
        trackImage.pixelsPerUnitMultiplier = 8f;
        trackImage.color = new Color(0.30f, 0.28f, 0.27f, 0.98f);
        AddOutline(trackImage, new Color(0.72f, 0.66f, 0.61f, 0.94f), 1.25f);
        RectTransform fillArea = MakeRect("FillArea", root);
        Anchored(fillArea, Vector2.zero, Vector2.one, new Vector2(10f, 12f), new Vector2(-10f, -12f), new Vector2(0.5f, 0.5f));
        RectTransform fill = MakeRect("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        fill.sizeDelta = Vector2.zero;
        Image fillImage = fill.gameObject.AddComponent<Image>();
        fillImage.color = Accent;
        AddOutline(fillImage, new Color(AccentLight.r, AccentLight.g, AccentLight.b, 0.72f), 0.8f);
        RectTransform handleArea = MakeRect("HandleSlideArea", root);
        Anchored(handleArea, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f), new Vector2(0.5f, 0.5f));
        RectTransform handle = MakeRect("Handle", handleArea);
        handle.anchorMin = handle.anchorMax = new Vector2(0.5f, 0.5f);
        handle.pivot = new Vector2(0.5f, 0.5f);
        handle.sizeDelta = new Vector2(38f, 46f);
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(VolumeHandlePath);
        handleImage.type = Image.Type.Simple;
        handleImage.preserveAspect = true;
        handleImage.color = Color.white;
        handleImage.raycastTarget = true;
        slider.fillRect = fill;
        slider.handleRect = handle;
        slider.targetGraphic = handleImage;
        slider.navigation = Navigation.defaultNavigation;
        return slider;
    }

    private static Dropdown CreateDropdown(string name, Transform parent, float width)
    {
        RectTransform root = MakeRect(name, parent);
        LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.minWidth = 190f;
        layout.preferredHeight = 46f;
        layout.minHeight = 42f;
        Image background = root.gameObject.AddComponent<Image>();
        background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LavaPlatePath);
        background.type = Image.Type.Sliced;
        background.color = new Color(0.36f, 0.33f, 0.31f, 0.96f);
        AddLavaEdge(root, "DropdownLavaEdge");
        Dropdown dropdown = root.gameObject.AddComponent<Dropdown>();
        dropdown.targetGraphic = background;
        Text caption = MakeText("Caption", root, "Select", 14, MainText, TextAnchor.MiddleLeft);
        Anchored(caption.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 2f), new Vector2(-38f, -2f), new Vector2(0.5f, 0.5f));
        Text arrow = MakeText("Arrow", root, "⌄", 18, AccentLight, TextAnchor.MiddleCenter);
        Anchored(arrow.rectTransform, new Vector2(1f, 0f), Vector2.one, new Vector2(-36f, 0f), new Vector2(-4f, 0f), new Vector2(1f, 0.5f));

        RectTransform template = MakeRect("Template", root);
        Anchored(template, Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, -4f), new Vector2(0f, -4f), new Vector2(0.5f, 1f));
        template.sizeDelta = new Vector2(0f, 190f);
        template.gameObject.SetActive(false);
        Image templateImage = template.gameObject.AddComponent<Image>();
        templateImage.color = Panel;
        ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        RectTransform viewport = MakeRect("Viewport", template);
        Anchored(viewport, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f), new Vector2(0.5f, 0.5f));
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.white;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        RectTransform content = MakeRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, 36f);
        VerticalLayoutGroup listLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = true;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        RectTransform item = MakeRect("Item", content);
        LayoutElement itemLayout = item.gameObject.AddComponent<LayoutElement>();
        itemLayout.preferredHeight = 36f;
        itemLayout.minHeight = 36f;
        Toggle toggle = item.gameObject.AddComponent<Toggle>();
        Image itemBackground = item.gameObject.AddComponent<Image>();
        itemBackground.color = Card;
        toggle.targetGraphic = itemBackground;
        RectTransform check = MakeRect("Item Checkmark", item);
        Anchored(check, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, -10f), new Vector2(30f, 10f), new Vector2(0.5f, 0.5f));
        Image checkImage = check.gameObject.AddComponent<Image>();
        checkImage.color = AccentLight;
        toggle.graphic = checkImage;
        Text itemText = MakeText("Item Label", item, "Option", 14, MainText, TextAnchor.MiddleLeft);
        Anchored(itemText.rectTransform, Vector2.zero, Vector2.one, new Vector2(40f, 0f), new Vector2(-8f, 0f), new Vector2(0.5f, 0.5f));
        scroll.viewport = viewport;
        scroll.content = content;
        dropdown.captionText = caption;
        dropdown.template = template;
        dropdown.itemText = itemText;
        dropdown.value = 0;
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static CanvasGroup BuildConfirmationDialog(Transform panel, out Text title, out Text message,
        out Text countdown, out Button confirm, out Text confirmLabel, out Button cancel, out Text cancelLabel)
    {
        RectTransform overlay = MakeRect("SettingsConfirmation", panel);
        Anchored(overlay, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Image shade = overlay.gameObject.AddComponent<Image>();
        shade.color = new Color(0.03f, 0.025f, 0.02f, 0.78f);
        CanvasGroup group = overlay.gameObject.AddComponent<CanvasGroup>();
        RectTransform card = MakeRect("DialogCard", overlay);
        Anchored(card, new Vector2(0.08f, 0.32f), new Vector2(0.92f, 0.68f), Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        Image cardImage = card.gameObject.AddComponent<Image>();
        cardImage.color = Panel;
        title = MakeText("DialogTitle", card, "CONFIRM", 22, MainText, TextAnchor.MiddleLeft);
        Anchored(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(26f, -74f), new Vector2(-26f, -24f), new Vector2(0.5f, 1f));
        message = MakeText("DialogMessage", card, "", 15, MutedText, TextAnchor.UpperLeft);
        Anchored(message.rectTransform, new Vector2(0f, 0.42f), new Vector2(1f, 0.74f), new Vector2(26f, 0f), new Vector2(-26f, 0f), new Vector2(0.5f, 0.5f));
        countdown = MakeText("DialogCountdown", card, "", 14, AccentLight, TextAnchor.MiddleCenter);
        Anchored(countdown.rectTransform, new Vector2(0f, 0.23f), new Vector2(1f, 0.37f), new Vector2(20f, 0f), new Vector2(-20f, 0f), new Vector2(0.5f, 0.5f));
        cancel = MakeButton("CancelButton", card, "CANCEL", Card, MainText, 48f);
        Anchored(cancel.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(8f, 20f), new Vector2(220f, 68f), new Vector2(0.5f, 0f));
        cancelLabel = cancel.GetComponentInChildren<Text>();
        confirm = MakeButton("ConfirmButton", card, "CONFIRM", Accent, MainText, 48f);
        Anchored(confirm.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-220f, 20f), new Vector2(-8f, 68f), new Vector2(0.5f, 0f));
        confirmLabel = confirm.GetComponentInChildren<Text>();
        overlay.SetAsLastSibling();
        HideGroup(group);
        return group;
    }

    private static Button MakeButton(string name, Transform parent, string label, Color background, Color textColor, float height)
    {
        RectTransform rect = MakeRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = background;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.colors = new ColorBlock
        {
            normalColor = background,
            highlightedColor = Color.Lerp(background, AccentLight, 0.22f),
            pressedColor = Accent,
            selectedColor = Color.Lerp(background, Accent, 0.28f),
            disabledColor = new Color(0.2f, 0.19f, 0.18f, 0.65f),
            colorMultiplier = 1f,
            fadeDuration = 0.12f
        };
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
        Text caption = MakeText("Label", rect, label, 15, textColor, TextAnchor.MiddleCenter);
        Anchored(caption.rectTransform, Vector2.zero, Vector2.one, new Vector2(12f, 3f), new Vector2(-12f, -3f), new Vector2(0.5f, 0.5f));
        return button;
    }

    private static void StylePanel(Transform transform, CanvasGroup group, Vector2 size)
    {
        RectTransform rect = transform.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-48f, 0f);
        rect.sizeDelta = size;
        Image background = Ensure<Image>(transform.gameObject);
        background.sprite = null;
        background.color = new Color(0.025f, 0.022f, 0.02f, 0.42f);
        background.raycastTarget = true;
        Outline staleOutline = background.GetComponent<Outline>();
        if (staleOutline != null) Object.DestroyImmediate(staleOutline);
        RectTransform basalt = MakeRect("BasaltPanelSurface", transform).GetComponent<RectTransform>();
        Anchored(basalt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Vector2(0.5f, 0.5f));
        basalt.SetAsFirstSibling();
        Image basaltImage = basalt.gameObject.AddComponent<Image>();
        basaltImage.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SettingsPanelPath);
        basaltImage.type = Image.Type.Sliced;
        basaltImage.pixelsPerUnitMultiplier = 4f;
        basaltImage.color = new Color(0.68f, 0.66f, 0.63f, 0.98f);
        basaltImage.raycastTarget = false;
        RectTransform readingSurface = MakeRect("ReadingSurface", transform);
        Anchored(readingSurface, Vector2.zero, Vector2.one, new Vector2(28f, 26f), new Vector2(-28f, -26f), new Vector2(.5f, .5f));
        Image readingImage = readingSurface.gameObject.AddComponent<Image>();
        readingImage.color = new Color(.018f, .021f, .024f, .76f);
        readingImage.raycastTarget = false;
        Transform oldPanelEdge = transform.Find("PanelLavaEdge");
        if (oldPanelEdge != null) Object.DestroyImmediate(oldPanelEdge.gameObject);
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

    }

    private static void PrepareLavaPlateSprite()
    {
        TextureImporter importer = AssetImporter.GetAtPath(LavaPlatePath) as TextureImporter;
        if (importer == null) throw new System.Exception($"Lava-rock UI sprite was not found: {LavaPlatePath}");
        bool changed = importer.textureType != TextureImporterType.Sprite ||
                       importer.spriteImportMode != SpriteImportMode.Single ||
                       importer.spriteBorder != new Vector4(150f, 100f, 150f, 100f);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.spriteBorder = new Vector4(150f, 100f, 150f, 100f);
        if (changed) importer.SaveAndReimport();
    }

    private static void PrepareSettingsPanelSprite()
    {
        TextureImporter importer = AssetImporter.GetAtPath(SettingsPanelPath) as TextureImporter;
        if (importer == null) throw new System.Exception($"Settings panel sprite was not found: {SettingsPanelPath}");
        bool changed = importer.textureType != TextureImporterType.Sprite ||
                       importer.spriteImportMode != SpriteImportMode.Single ||
                       importer.spriteBorder != new Vector4(140f, 140f, 140f, 140f);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.spriteBorder = new Vector4(140f, 140f, 140f, 140f);
        if (changed) importer.SaveAndReimport();
    }

    private static void MakeRule(string name, Transform parent, Color color, Vector2 offsetMin, Vector2 offsetMax, bool full = false)
    {
        RectTransform rect = MakeRect(name, parent);
        if (full) Anchored(rect, new Vector2(0f, 1f), Vector2.one, offsetMin, offsetMax, new Vector2(0.5f, 1f));
        else Anchored(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), offsetMin, offsetMax, new Vector2(0.5f, 1f));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    private static void AddLavaEdge(Transform parent, string name)
    {
        RectTransform rect = MakeRect(name, parent);
        Anchored(rect, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(4f, 0f), new Vector2(0f, 0.5f));
        Image edge = rect.gameObject.AddComponent<Image>();
        edge.color = new Color(Accent.r, Accent.g, Accent.b, 0.92f);
        edge.raycastTarget = false;
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    private static void AddOutline(Image target, Color color, float thickness)
    {
        Outline outline = target.GetComponent<Outline>();
        if (outline == null) outline = target.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(thickness, -thickness);
        outline.useGraphicAlpha = true;
    }

    private static void StylePanelButton(Button button, bool centered)
    {
        Image image = button.GetComponent<Image>();
        if (image == null) return;
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(LavaPlatePath);
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 8f;
        image.color = new Color(.8f, .78f, .75f);
        Transform oldEdge = button.transform.Find("ButtonLavaEdge");
        if (oldEdge != null) Object.DestroyImmediate(oldEdge.gameObject);
        Transform oldUnderline = button.transform.Find("ButtonLavaUnderline");
        if (oldUnderline != null) Object.DestroyImmediate(oldUnderline.gameObject);
        RectTransform underline = MakeRect("ButtonLavaUnderline", button.transform);
        Anchored(underline, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(12f, 2f), new Vector2(-12f, 4f), new Vector2(0.5f, 0f));
        Image underlineImage = underline.gameObject.AddComponent<Image>();
        underlineImage.color = new Color(AccentLight.r, AccentLight.g, AccentLight.b, 0.82f);
        underlineImage.raycastTarget = false;
        underline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        button.targetGraphic = image;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.82f, 0.66f, 1f);
        colors.pressedColor = new Color(0.72f, 0.35f, 0.18f, 1f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;
        Text label = button.GetComponentInChildren<Text>(true);
        if (label != null)
        {
            label.alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
            Ashfall.UI.AshfallTypography.Apply(label, Ashfall.UI.AshfallTypography.Role.Button, 20);
        }
    }

    private static Text MakeText(string name, Transform parent, string value, int size, Color color, TextAnchor alignment)
    {
        RectTransform rect = MakeRect(name, parent);
        Text text = rect.gameObject.AddComponent<Text>();
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.font = uiFont;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = name == "KeyLabel" ? value : Ashfall.UI.AshfallTypography.NaturalCase(value);
        Ashfall.UI.AshfallTypography.Apply(text, Ashfall.UI.AshfallTypography.Infer(text), size);
        return text;
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Anchored(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Vector2 pivot)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Transform FindChild(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    private static T Ensure<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Object.DestroyImmediate(parent.GetChild(i).gameObject);
    }

    private static void HideGroup(CanvasGroup group)
    {
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        group.gameObject.SetActive(false);
    }

    private static void Assign(SerializedObject target, string propertyName, Object value)
    {
        SerializedProperty property = target.FindProperty(propertyName);
        if (property == null) throw new System.Exception($"Missing serialized field: {propertyName}");
        property.objectReferenceValue = value;
    }

    private static Color Hex(string value)
    {
        ColorUtility.TryParseHtmlString("#" + value, out Color color);
        return color;
    }
}
#endif
