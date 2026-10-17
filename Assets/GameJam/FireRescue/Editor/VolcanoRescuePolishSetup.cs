using System;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Builds the shared volcanic Canvas HUD and pause pages for Volcano_Demo.</summary>
public static class VolcanoRescuePolishSetup
{
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string PanelPath = "Assets/Art/UI/AshfallSettingsPanel.png";
    const string HudPanelPath = "Assets/Art/UI/VOLKANOVSKı-removebg-preview.png";
    const string ButtonPath = "Assets/Art/UI/AshfallMenuButton.png";
    static readonly Color PanelTint = new Color(1f, 1f, 1f, .98f);
    static readonly Color Accent = new Color(1f, .39f, .12f, 1f);
    static readonly Color HotAccent = new Color(1f, .61f, .23f, 1f);
    static readonly Color TextColor = new Color(.96f, .92f, .86f, 1f);
    static readonly Color MutedText = new Color(.73f, .68f, .62f, 1f);
    static Font font;
    static Sprite panelSprite, hudPanelSprite, buttonSprite;

    [MenuItem("ASHFALL/UI/Apply VOLKANOVSKI HUD Cards")]
    public static void ApplyVolkanovskiHudCards()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before updating HUD card artwork.");

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(HudPanelPath);
        if (!sprite) throw new InvalidOperationException("VOLKANOVSKı HUD frame is not imported as a Sprite.");

        string[] cardNames = { "QuestPanel", "StatusPanel", "VolcanoBadge", "PlayerBadge", "PlayerHealth", "ActionBar", "PauseHint", "TargetFire", "LowWaterWarning", "RescueToast", "MissionSubtitle" };
        int changed = 0;
        foreach (string cardName in cardNames)
        {
            GameObject card = GameObject.Find(cardName);
            if (!card || card.scene != scene) continue;
            Image image = card.GetComponent<Image>();
            if (!image) continue;
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = card.GetComponent<RectTransform>().rect.height < 120f ? 6f : 3f;
            image.color = Color.white;
            EditorUtility.SetDirty(image);
            changed++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("VOLKANOVSKI_HUD_CARDS_READY: " + changed + " gameplay HUD cards updated. Pause, Settings and How To Play were not changed.");
    }

    [MenuItem("GameJam/Integration/Build rescue objective and HUD")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring rescue UI.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before rebuilding rescue UI.");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ConfigureScene(scene);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log("VOLCANO_RESCUE_POLISH_READY: volcanic HUD, dynamic mission data, pause/settings/how-to-play and outcome UI authored.");
    }

    public static void ConfigureScene(Scene scene)
    {
        panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelPath);
        hudPanelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(HudPanelPath);
        buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonPath);
        if (!panelSprite || !hudPanelSprite || !buttonSprite) throw new InvalidOperationException("Volcanic UI sprites are missing from Assets/Art/UI.");
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var eruption = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var evacuation = Object.FindFirstObjectByType<EvacuationMission>();
        var missionDirector = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var survivalObjective = Object.FindFirstObjectByType<VolcanoSurvivalObjective>();
        var vitals = Object.FindFirstObjectByType<CreatureCombatPlayer>();
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Where(h => h.gameObject.scene == scene).OrderBy(h => h.transform.position.z).ToArray();
        if (!player || !player.hose || !player.drive || !player.cannon || !eruption || houses.Length != RescueGameFlow.RequiredHouses)
            throw new InvalidOperationException("Expected one complete rescue rig and exactly five authored town houses.");

        foreach (var existing in Object.FindObjectsByType<RescueHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (existing.gameObject.scene == scene) Object.DestroyImmediate(existing.gameObject);
        foreach (var existing in Object.FindObjectsByType<RescueGameFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (existing.gameObject.scene == scene) Object.DestroyImmediate(existing);

        var root = new GameObject("Rescue Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, scene);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 50;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;

        var flow = root.AddComponent<RescueGameFlow>(); flow.player = player; flow.targetBuildings = houses;
        var hud = root.AddComponent<RescueHUD>();
        var hudData = new SerializedObject(hud);
        hudData.FindProperty("pauseClickClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/click.mp3");
        hudData.ApplyModifiedPropertiesWithoutUndo();
        hud.flow = flow; hud.player = player; hud.hose = player.hose; hud.drive = player.drive; hud.cannon = player.cannon;
        hud.eruption = eruption; hud.missionDirector = missionDirector; hud.survivalObjective = survivalObjective; hud.evacuation = evacuation; hud.canvas = canvas;
        hud.vitals = survivalObjective && survivalObjective.vitals ? survivalObjective.vitals : vitals;
        player.gameFlow = flow; player.showHud = false; eruption.showHud = false;
        if (evacuation) evacuation.canvasHudOwnsPanel = true;
        if (survivalObjective) { survivalObjective.showHud = false; PrefabUtility.RecordPrefabInstancePropertyModifications(survivalObjective); }
        player.hose.startingTankLitres = 250f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.hose);
        PrefabUtility.RecordPrefabInstancePropertyModifications(eruption);
        if (evacuation) PrefabUtility.RecordPrefabInstancePropertyModifications(evacuation);
        foreach (var house in houses) { house.startBurning = false; PrefabUtility.RecordPrefabInstancePropertyModifications(house); }

        hud.musicSources = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(source => source.gameObject.scene == scene && source.clip && IsMusicSource(source)).ToArray();
        BuildLiveHud(root.transform, hud);
        BuildPausePages(root.transform, hud);
        BuildVictoryPage(root.transform, hud);

        if (!Object.FindFirstObjectByType<EventSystem>())
        {
            var events = new GameObject("Rescue EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(events, scene);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        EditorUtility.SetDirty(player); EditorUtility.SetDirty(player.hose); EditorUtility.SetDirty(eruption);
        if (evacuation) EditorUtility.SetDirty(evacuation);
        EditorUtility.SetDirty(flow); EditorUtility.SetDirty(hud);
    }

    static void BuildLiveHud(Transform parent, RescueHUD hud)
    {
        var quest = HudPanel(parent, "QuestPanel", new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -26), new Vector2(560, 236));
        ConfigureFrame(quest, 520, 188);
        Label(quest, "QuestKicker", "ASHFALL  /  GÖREV", 13, new Vector2(52, -24), new Vector2(450, 24), TextAnchor.MiddleLeft, Accent);
        hud.questTitleText = Label(quest, "QuestTitle", "GÖREV  ·  HALKI TAHLİYE ET", 21, new Vector2(52, -49), new Vector2(450, 34), TextAnchor.MiddleLeft, HotAccent);
        hud.questBodyText = Label(quest, "QuestDescription", "SIĞINAKTA  0 / 7     ÇAĞRILAN  0 / 7\nEvlerin yanına git ve Q ile halka seslen.", 17, new Vector2(52, -91), new Vector2(458, 62), TextAnchor.UpperLeft, TextColor);
        var questTrack = Box(quest, "QuestProgressTrack", new Vector2(0, 1), new Vector2(0, 1), new Vector2(52, -162), new Vector2(456, 9), new Color(.06f, .045f, .035f, .98f));
        hud.questFill = Fill(questTrack, "QuestProgressFill", Accent);
        hud.questHintText = Label(quest, "QuestHint", "Q  HERKES DIŞARIYA!  ·  EN YAKIN EV: 212 m", 14, new Vector2(52, -182), new Vector2(456, 27), TextAnchor.MiddleLeft, MutedText);
        quest.GetComponent<VolcanicRockFrameSizer>().SetVerticalFlow(hud.questBodyText, questTrack, hud.questHintText.rectTransform);

        var resources = HudPanel(parent, "StatusPanel", Vector2.one, Vector2.one, new Vector2(-28, -26), new Vector2(425, 221));
        ConfigureFrame(resources, 400, 172);
        Label(resources, "StatusKicker", "KASABA DURUMU", 13, new Vector2(38, -24), new Vector2(345, 23), TextAnchor.MiddleLeft, Accent);
        hud.objectiveText = Label(resources, "SavedHouses", "KURTARILAN EVLER   0 / 5", 19, new Vector2(38, -51), new Vector2(348, 31), TextAnchor.MiddleLeft, TextColor);
        Label(resources, "WaterLabel", "SU DEPOSU", 14, new Vector2(38, -94), new Vector2(140, 22), TextAnchor.MiddleLeft, MutedText);
        hud.waterText = Label(resources, "WaterLitres", "250 / 2000 L", 17, new Vector2(-38, -91), new Vector2(230, 25), TextAnchor.MiddleRight, TextColor);
        hud.waterText.rectTransform.anchorMin = hud.waterText.rectTransform.anchorMax = new Vector2(1, 1);
        hud.waterText.rectTransform.pivot = new Vector2(1, 1);
        var waterTrack = Box(resources, "WaterTrack", new Vector2(0, 1), new Vector2(0, 1), new Vector2(38, -121), new Vector2(348, 9), new Color(.07f, .045f, .035f, 1));
        hud.waterFill = Fill(waterTrack, "WaterFill", Accent);
        hud.pumpText = Label(resources, "WaterStatus", "Hortum araçta.", 14, new Vector2(38, -138), new Vector2(348, 36), TextAnchor.MiddleLeft, MutedText);

        var volcano = HudPanel(parent, "VolcanoBadge", new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -262), new Vector2(350, 96));
        hud.eruptionText = Label(volcano, "Text", "VOLKAN  ·  HAZIRLIK", 16, new Vector2(38, -29), new Vector2(276, 45), TextAnchor.MiddleLeft, HotAccent);
        ConfigureFrame(volcano, 250, 66, hud.eruptionText);
        volcano.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .32f;
        var speed = HudPanel(parent, "PlayerBadge", Vector2.zero, Vector2.zero, new Vector2(28, 28), new Vector2(208, 74));
        var speedKicker = Label(speed, "Kicker", "Oyuncu", 14, new Vector2(24, -18), new Vector2(130, 20), TextAnchor.MiddleLeft, MutedText);
        hud.speedText = Label(speed, "Text", "Yaya", 20, new Vector2(24, -40), new Vector2(145, 27), TextAnchor.MiddleLeft, TextColor);
        ConfigureFrame(speed, 148, 58, speedKicker, hud.speedText);
        speed.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .2f;

        var health = HudPanel(parent, "PlayerHealth", Vector2.zero, Vector2.zero, new Vector2(28, 132), new Vector2(236, 72));
        hud.healthPanel = health.gameObject;
        Label(health, "HealthKicker", "Can", 14, new Vector2(24, -18), new Vector2(70, 24), TextAnchor.MiddleLeft, MutedText);
        hud.healthText = Label(health, "HealthValue", "100 / 100", 18, new Vector2(-24, -18), new Vector2(100, 24), TextAnchor.MiddleRight, TextColor);
        hud.healthText.rectTransform.anchorMin = hud.healthText.rectTransform.anchorMax = new Vector2(1, 1);
        hud.healthText.rectTransform.pivot = new Vector2(1, 1);
        var healthTrack = Box(health, "HealthTrack", new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -48), new Vector2(168, 8), new Color(.06f, .045f, .035f, 1));
        hud.healthFill = Fill(healthTrack, "HealthFill", HotAccent);
        ConfigureFrame(health, 190, 58);
        health.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .24f;
        health.gameObject.SetActive(false);

        var action = HudPanel(parent, "ActionBar", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 28), new Vector2(1040, 76));
        ConfigureFrame(action, 760, 68);
        action.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .88f;
        Label(action, "ActionKicker", "ETKİLEŞİM", 12, new Vector2(28, -18), new Vector2(120, 18), TextAnchor.MiddleLeft, Accent);
        hud.promptText = Label(action, "Prompt", "", 17, new Vector2(28, -38), new Vector2(956, 34), TextAnchor.MiddleCenter, TextColor);
        action.GetComponent<VolcanicRockFrameSizer>().minWidth = 320;
        action.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .58f;
        action.GetComponent<VolcanicRockFrameSizer>().widthFitTexts = new[] { hud.promptText };

        var hint = HudPanel(parent, "PauseHint", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 28), new Vector2(220, 62));
        var pauseHintText = Label(hint, "Text", "ESC  ·  Duraklat", 17, new Vector2(24, -18), new Vector2(172, 30), TextAnchor.MiddleCenter, TextColor);
        ConfigureFrame(hint, 160, 48, pauseHintText);
        hint.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .2f;

        var target = HudPanel(parent, "TargetFire", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 124), new Vector2(394, 86));
        ConfigureFrame(target, 340, 68);
        hud.targetGroup = target.gameObject;
        hud.targetText = Label(target, "Text", "YANGIN SÖNDÜRÜLÜYOR", 15, new Vector2(32, -16), new Vector2(330, 28), TextAnchor.MiddleCenter, TextColor);
        var targetTrack = Box(target, "ProgressTrack", new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -55), new Vector2(330, 8), new Color(.06f, .045f, .035f, 1));
        hud.targetFill = Fill(targetTrack, "ProgressFill", Accent); target.gameObject.SetActive(false);

        var warning = HudPanel(parent, "LowWaterWarning", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 116), new Vector2(370, 64));
        hud.lowWaterWarning = warning.gameObject;
        hud.lowWaterText = Label(warning, "Text", "Su az · Gölden doldur", 18, new Vector2(24, -18), new Vector2(310, 30), TextAnchor.MiddleCenter, TextColor);
        ConfigureFrame(warning, 280, 52, hud.lowWaterText);
        warning.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .3f;
        hud.lowWaterBackground = warning.GetComponent<Image>(); warning.gameObject.SetActive(false);

        var toast = HudPanel(parent, "RescueToast", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -274), new Vector2(440, 58));
        hud.toastText = Label(toast, "Text", "", 18, new Vector2(24, -18), new Vector2(370, 34), TextAnchor.MiddleCenter, TextColor);
        ConfigureFrame(toast, 220, 48, hud.toastText);
        toast.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .46f;
        toast.gameObject.SetActive(false);
        var subtitle = HudPanel(parent, "MissionSubtitle", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -348), new Vector2(690, 84));
        hud.subtitleText = Label(subtitle, "Text", "", 17, new Vector2(42, -18), new Vector2(606, 50), TextAnchor.MiddleCenter, TextColor);
        ConfigureFrame(subtitle, 360, 64, hud.subtitleText);
        subtitle.GetComponent<VolcanicRockFrameSizer>().maxWidthOfParent = .7f;
        subtitle.gameObject.SetActive(false);

        var reticle = Box(parent, "PredictedWaterImpact", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(18, 2), HotAccent);
        Box(reticle, "Vertical", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(2, 18), HotAccent);
        hud.impactReticle = reticle.GetComponent<Image>();
    }

    static void BuildPausePages(Transform parent, RescueHUD hud)
    {
        var overlay = Box(parent, "PausePanel", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.01f, .012f, .016f, .76f));
        Stretch(overlay); overlay.GetComponent<Image>().raycastTarget = true;
        var rootGroup = overlay.gameObject.AddComponent<CanvasGroup>(); rootGroup.alpha = 0; rootGroup.interactable = rootGroup.blocksRaycasts = false;
        hud.pausePanel = rootGroup;

        var pausePage = PageGroup(overlay, "PauseMenu", new Vector2(720, 700)); hud.pauseMenuGroup = pausePage.GetComponent<CanvasGroup>();
        ConfigureFrame(pausePage, 620, 600);
        Label(pausePage, "Brand", "ASHFALL  /  BEKLEME NOKTASI", 14, new Vector2(90, -54), new Vector2(540, 24), TextAnchor.MiddleCenter, Accent);
        Label(pausePage, "Title", "OYUN DURAKLATILDI", 34, new Vector2(84, -87), new Vector2(552, 60), TextAnchor.MiddleCenter, TextColor);
        Label(pausePage, "Detail", "Kasaba seni bekliyor. Hazır olduğunda devam et.", 17, new Vector2(80, -152), new Vector2(560, 35), TextAnchor.MiddleCenter, MutedText);
        hud.resumeButton = Button(pausePage, "Resume", "DEVAM ET", new Vector2(150, -220), 420);
        hud.pauseSettingsButton = Button(pausePage, "Settings", "AYARLAR", new Vector2(150, -298), 420);
        hud.pauseHowToPlayButton = Button(pausePage, "HowToPlay", "NASIL OYNANIR", new Vector2(150, -376), 420);
        hud.restartButton = Button(pausePage, "Restart", "GÖREVİ YENİDEN BAŞLAT", new Vector2(150, -454), 420);
        hud.pauseMenuButton = Button(pausePage, "ReturnToMenu", "ANA MENÜYE DÖN", new Vector2(150, -532), 420);
        hud.quitButton = Button(pausePage, "Quit", "OYUNDAN ÇIK", new Vector2(150, -610), 420);

        var settings = PageGroup(overlay, "SettingsPanel", new Vector2(1000, 830)); hud.settingsPanel = settings.GetComponent<CanvasGroup>();
        ConfigureFrame(settings, 900, 740);
        Label(settings, "Title", "AYARLAR", 34, new Vector2(70, -42), new Vector2(860, 54), TextAnchor.MiddleLeft, TextColor);
        Label(settings, "Subtitle", "Ses, görüntü ve kamera tercihlerini düzenle.", 16, new Vector2(72, -94), new Vector2(850, 28), TextAnchor.MiddleLeft, MutedText);
        Divider(settings, 140, 850);
        var settingsSurfaces = Box(settings, "SettingsRows", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.clear); Stretch(settingsSurfaces);
        for (int i = 0; i < 3; i++) SettingSurface(settingsSurfaces, "AudioRow" + i, 194 + i * 80, 66, 884);
        for (int i = 0; i < 3; i++) SettingSurface(settingsSurfaces, "GraphicsRow" + i, 474 + i * 60, 52, 884);
        settingsSurfaces.SetSiblingIndex(1);
        Label(settings, "AudioHeader", "SES", 18, new Vector2(72, -158), new Vector2(820, 28), TextAnchor.MiddleLeft, Accent);
        hud.masterSlider = SettingSlider(settings, "MasterVolume", "ANA SES", "Oyun ve menü sesleri", new Vector2(72, -200), out hud.masterValue);
        hud.musicSlider = SettingSlider(settings, "MusicVolume", "MÜZİK", "Ortam ve müzik kategorisi", new Vector2(72, -280), out hud.musicValue);
        hud.sfxSlider = SettingSlider(settings, "SfxVolume", "SES EFEKTLERİ", "Etkileşimler ve çevre sesleri", new Vector2(72, -360), out hud.sfxValue);
        Divider(settings, 420, 850);
        Label(settings, "GraphicsHeader", "GÖRÜNTÜ", 18, new Vector2(72, -440), new Vector2(820, 28), TextAnchor.MiddleLeft, Accent);
        Label(settings, "QualityLabel", "GRAFİK KALİTESİ", 15, new Vector2(72, -484), new Vector2(430, 36), TextAnchor.MiddleLeft, TextColor);
        hud.qualityDropdown = Dropdown(settings, "Quality", new Vector2(600, -480), new Vector2(300, 46));
        if (QualitySettings.names.Length > 0) hud.qualityDropdown.AddOptions(QualitySettings.names.ToList());
        Label(settings, "FullscreenLabel", "TAM EKRAN", 15, new Vector2(72, -544), new Vector2(430, 36), TextAnchor.MiddleLeft, TextColor);
        hud.fullscreenToggle = Toggle(settings, "Fullscreen", new Vector2(600, -544));
        Label(settings, "SensitivityLabel", "FARE HASSASİYETİ", 15, new Vector2(72, -602), new Vector2(430, 30), TextAnchor.MiddleLeft, TextColor);
        hud.sensitivitySlider = Slider(settings, "Sensitivity", new Vector2(577, -610), 290, .02f, .3f, .12f);
        hud.sensitivityValue = Label(settings, "SensitivityValue", "0.12", 18, new Vector2(874, -602), new Vector2(54, 28), TextAnchor.MiddleRight, TextColor);
        hud.settingsBackButton = Button(settings, "Back", "GERİ", new Vector2(285, -704), 430);

        var help = PageGroup(overlay, "HowToPlayPanel", new Vector2(930, 760)); hud.howToPlayPanel = help.GetComponent<CanvasGroup>();
        ConfigureFrame(help, 860, 700);
        var helpSurfaces = Box(help, "HelpRows", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Color.clear); Stretch(helpSurfaces);
        for (int i = 0; i < 6; i++) SettingSurface(helpSurfaces, "HelpRow" + i, 168 + i * 76, 64, 790);
        helpSurfaces.SetSiblingIndex(1);
        Label(help, "Title", "NASIL OYNANIR", 32, new Vector2(76, -44), new Vector2(778, 52), TextAnchor.MiddleLeft, TextColor);
        Label(help, "Subtitle", "Kasabayı tahliye et, suyu doldur ve evleri lavlardan koru.", 16, new Vector2(78, -96), new Vector2(770, 34), TextAnchor.MiddleLeft, MutedText);
        Divider(help, 140, 775);
        HelpRow(help, "HAREKET", "W A S D ile yürü veya aracı sür. Shift ile koş, Space ile zıpla.", 184);
        HelpRow(help, "KAMERA", "Fareyi hareket ettirerek çevreye bak ve nişan al.", 260);
        HelpRow(help, "TAHLİYE", "Evlerin yanına yaklaş, Q ile seslen ve halkı SHELTER tabelalı sığınağa götür.", 336);
        HelpRow(help, "ETKİLEŞİM", "E ile yakındaki kapı veya hortumla etkileş. F ile itfaiye aracına bin ya da in.", 412);
        HelpRow(help, "SU DEPOSU", "Hortum ucunu suya bırak, P ile pompayı aç. R ile hortumu topla.", 488);
        HelpRow(help, "YANGIN", "Araçtayken fareyle nişan al; sağ tık basılı tutarak su püskürt.", 564);
        Label(help, "Hazard", "Lav akışlarından ve çöken evlerden uzak dur.", 15, new Vector2(78, -637), new Vector2(770, 30), TextAnchor.MiddleLeft, HotAccent);
        hud.howToPlayBackButton = Button(help, "Back", "GERİ", new Vector2(255, -680), 420);

        SetPanel(rootGroup, false);
        SetPanel(settings.GetComponent<CanvasGroup>(), false);
        SetPanel(help.GetComponent<CanvasGroup>(), false);
        SetPanel(pausePage.GetComponent<CanvasGroup>(), true);
    }

    static void BuildVictoryPage(Transform parent, RescueHUD hud)
    {
        var overlay = Box(parent, "VictoryPanel", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.01f, .012f, .016f, .78f));
        Stretch(overlay); overlay.GetComponent<Image>().raycastTarget = true;
        var group = overlay.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false; hud.victoryPanel = group;
        var card = RockPanel(overlay, "VictoryCard", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(760, 520));
        ConfigureFrame(card, 640, 440);
        Label(card, "Brand", "ASHFALL  /  GÖREV RAPORU", 14, new Vector2(80, -55), new Vector2(600, 28), TextAnchor.MiddleCenter, Accent);
        hud.victoryTitleText = Label(card, "Title", "KASABA GÜVENDE", 34, new Vector2(75, -101), new Vector2(610, 60), TextAnchor.MiddleCenter, TextColor);
        hud.victoryDetailText = Label(card, "Detail", "Beş ev de kurtarıldı. Tahliye edilen halk güvende.", 18, new Vector2(80, -180), new Vector2(600, 60), TextAnchor.MiddleCenter, MutedText);
        hud.victoryStatsText = Label(card, "MissionStats", "Kurtarılan ev  5 / 5", 16, new Vector2(80, -252), new Vector2(600, 34), TextAnchor.MiddleCenter, HotAccent);
        hud.outcomeRestartButton = Button(card, "Retry", "YENİDEN DENE", new Vector2(80, -340), 300);
        hud.victoryMenuButton = Button(card, "ReturnToMenu", "ANA MENÜYE DÖN", new Vector2(400, -340), 300);
        hud.outcomeRestartButton.gameObject.SetActive(false);
    }

    static bool IsMusicSource(AudioSource source)
    {
        string identity = (source.name + " " + (source.clip ? source.clip.name : "")).ToLowerInvariant();
        return identity.Contains("music") || identity.Contains("bgm") || identity.Contains("theme");
    }

    static RectTransform PageGroup(Transform parent, string name, Vector2 size)
    {
        var panel = RockPanel(parent, name, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, size);
        var group = panel.gameObject.AddComponent<CanvasGroup>(); group.alpha = 1; group.interactable = group.blocksRaycasts = true;
        return panel;
    }

    static RectTransform RockPanel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rect = Box(parent, name, anchor, pivot, position, size, PanelTint);
        var image = rect.GetComponent<Image>(); image.sprite = panelSprite; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = size.y < 120 ? 8f : 4f; image.color = new Color(.72f, .70f, .67f, .98f);
        image.raycastTarget = false;
        var sizer = rect.gameObject.AddComponent<VolcanicRockFrameSizer>(); sizer.frame = rect;
        var core = Box(rect, "ObsidianCore", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.018f, .021f, .024f, .76f));
        Stretch(core); Vector2 inset = size.y < 120 ? new Vector2(18, 14) : new Vector2(28, 26); core.offsetMin = inset; core.offsetMax = -inset;
        return rect;
    }

    // Gameplay HUD only. Pause, settings, help and outcome pages continue using the existing panel artwork.
    static RectTransform HudPanel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        var rect = RockPanel(parent, name, anchor, pivot, position, size);
        var image = rect.GetComponent<Image>();
        image.sprite = buttonSprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = size.y < 120 ? 9f : 6f;
        image.color = Color.white;
        return rect;
    }

    static void ConfigureFrame(RectTransform panel, float minimumWidth, float minimumHeight, params Text[] widthFitTexts)
    {
        var sizer = panel.GetComponent<VolcanicRockFrameSizer>();
        if (!sizer) sizer = panel.gameObject.AddComponent<VolcanicRockFrameSizer>();
        sizer.frame = panel;
        sizer.authoredWidth = panel.sizeDelta.x;
        sizer.minWidth = minimumWidth;
        sizer.minHeight = minimumHeight;
        sizer.horizontalPadding = 28f;
        sizer.bottomPadding = panel.sizeDelta.y < 120 ? 16f : 26f;
        sizer.widthFitTexts = widthFitTexts ?? System.Array.Empty<Text>();
    }

    static void HelpRow(Transform parent, string title, string description, float y)
    {
        Label(parent, title + "Label", title, 17, new Vector2(78, -y), new Vector2(190, 28), TextAnchor.MiddleLeft, Accent);
        Label(parent, title + "Description", description, 15, new Vector2(280, -y), new Vector2(560, 46), TextAnchor.UpperLeft, TextColor);
    }

    static void SettingSurface(Transform parent, string name, float top, float height, float width)
    {
        var row = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(58, -top), new Vector2(width, height), new Color(.018f, .02f, .022f, .58f));
        Box(row, "MagmaSeam", Vector2.zero, Vector2.zero, new Vector2(2, 6), new Vector2(3, height - 12), new Color(1f, .28f, .09f, .92f));
    }

    static Slider SettingSlider(Transform parent, string name, string title, string description, Vector2 position, out Text value)
    {
        Label(parent, name + "Label", title, 15, position, new Vector2(360, 24), TextAnchor.MiddleLeft, TextColor);
        Label(parent, name + "Description", description, 17, position + new Vector2(0, -26), new Vector2(380, 24), TextAnchor.MiddleLeft, MutedText);
        var slider = Slider(parent, name, position + new Vector2(505, -17), 290, 0, 1, 1);
        value = Label(parent, name + "Value", "100%", 18, position + new Vector2(802, -10), new Vector2(54, 28), TextAnchor.MiddleRight, TextColor);
        return slider;
    }

    static Slider Slider(Transform parent, string name, Vector2 position, float width, float min, float max, float initial)
    {
        var rect = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(width, 18), new Color(.08f, .06f, .05f, 1));
        var background = rect.GetComponent<Image>(); background.color = new Color(.08f, .06f, .05f, 1);
        var slider = rect.gameObject.AddComponent<Slider>(); slider.minValue = min; slider.maxValue = max; slider.value = Mathf.Clamp(initial, min, max);
        slider.direction = UnityEngine.UI.Slider.Direction.LeftToRight; slider.transition = Selectable.Transition.ColorTint;
        var fillArea = Box(rect, "Fill Area", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear); Stretch(fillArea);
        fillArea.offsetMin = new Vector2(0, 2); fillArea.offsetMax = new Vector2(-8, -2);
        var fill = Box(fillArea, "Fill", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Accent); Stretch(fill);
        var handleArea = Box(rect, "Handle Slide Area", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Color.clear); Stretch(handleArea);
        handleArea.offsetMin = new Vector2(5, 0); handleArea.offsetMax = new Vector2(-5, 0);
        var handle = Box(handleArea, "Handle", new Vector2(0, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(34, 42), new Color(.43f, .37f, .32f, 1));
        var handleImage = handle.GetComponent<Image>(); handleImage.sprite = buttonSprite; handleImage.type = Image.Type.Sliced; handleImage.pixelsPerUnitMultiplier = 12f; handleImage.raycastTarget = true;
        var handleCore = Box(handle, "ObsidianGrip", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.08f, .065f, .06f, .96f)); Stretch(handleCore);
        handleCore.offsetMin = new Vector2(10, 7); handleCore.offsetMax = new Vector2(-10, -7);
        var handleGlow = Box(handle, "MagmaGrip", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -6), new Vector2(12, 3), HotAccent);
        slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handleImage;
        slider.colors = new ColorBlock { normalColor = Color.white, highlightedColor = new Color(1f, .78f, .53f), pressedColor = new Color(.85f, .45f, .24f), selectedColor = Color.white, disabledColor = Color.gray, colorMultiplier = 1f, fadeDuration = .1f };
        return slider;
    }

    static Dropdown Dropdown(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var rect = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), position, size, new Color(.055f, .045f, .04f, .98f));
        var image = rect.GetComponent<Image>(); image.color = new Color(.055f, .045f, .04f, .98f); image.raycastTarget = true;
        var dropdown = rect.gameObject.AddComponent<Dropdown>(); dropdown.targetGraphic = image;
        var caption = Label(rect, "Label", "", 18, new Vector2(14, -6), new Vector2(size.x - 42, size.y - 12), TextAnchor.MiddleLeft, TextColor); dropdown.captionText = caption;
        var template = Box(rect, "Template", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -size.y), new Vector2(size.x, 160), new Color(.055f, .045f, .04f, .99f));
        template.gameObject.SetActive(false);
        var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.vertical = true;
        var viewport = Box(template, "Viewport", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Color.white); Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Box(viewport, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(size.x, 30), Color.clear);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>(); fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained; fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var item = Box(content, "Item", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(size.x, 30), new Color(.12f, .09f, .07f, 1));
        item.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
        var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item.GetComponent<Image>();
        var check = Box(item, "Item Checkmark", new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(12, 0), new Vector2(12, 12), Accent); toggle.graphic = check.GetComponent<Image>();
        var itemText = Label(item, "Item Label", "Option", 18, new Vector2(32, -4), new Vector2(size.x - 42, 28), TextAnchor.MiddleLeft, TextColor);
        scroll.viewport = viewport; scroll.content = content;
        dropdown.template = template; dropdown.itemText = itemText; dropdown.itemImage = item.GetComponent<Image>();
        return dropdown;
    }

    static Toggle Toggle(Transform parent, string name, Vector2 position)
    {
        var rect = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(46, 30), new Color(.07f, .055f, .045f, 1));
        var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        var toggle = rect.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = image;
        var check = Box(rect, "Checkmark", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(16, 16), Accent);
        toggle.graphic = check.GetComponent<Image>();
        return toggle;
    }

    static void Divider(Transform parent, float y, float width)
    { Box(parent, "Divider", new Vector2(0, 1), new Vector2(0, 1), new Vector2(72, -y), new Vector2(width, 2), new Color(1f, .27f, .09f, .95f)); }

    static Button Button(Transform parent, string name, string label, Vector2 position, float width)
    {
        var rect = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(width, 62), Color.white);
        var image = rect.GetComponent<Image>(); image.sprite = buttonSprite; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 8f; image.color = new Color(.8f, .78f, .75f); image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1f, .82f, .62f); colors.pressedColor = new Color(.8f, .54f, .4f); button.colors = colors;
        Label(rect, "Label", label, 22, new Vector2(28, -9), new Vector2(width - 56, 44), TextAnchor.MiddleCenter, TextColor);
        return button;
    }

    static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        return rect;
    }

    static Text Label(Transform parent, string name, string text, int size, Vector2 position, Vector2 dimensions, TextAnchor align, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        var label = go.GetComponent<Text>(); if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.font = font; label.text = text; label.fontSize = size; label.color = color; label.alignment = align;
        label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Overflow; label.raycastTarget = false;
        label.text = Ashfall.UI.AshfallTypography.NaturalCase(text);
        Ashfall.UI.AshfallTypography.Apply(label, Ashfall.UI.AshfallTypography.Infer(label), size);
        return label;
    }

    static Image Fill(Transform parent, string name, Color color)
    {
        var rect = Box(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, color); Stretch(rect);
        var image = rect.GetComponent<Image>(); image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillAmount = 0f;
        return image;
    }

    static void SetPanel(CanvasGroup group, bool visible)
    {
        if (!group) return;
        group.gameObject.SetActive(visible);
        group.alpha = visible ? 1f : 0f;
        group.interactable = group.blocksRaycasts = visible;
    }

    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
