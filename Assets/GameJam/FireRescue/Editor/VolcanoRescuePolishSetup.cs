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

/// <summary>Explicit, repeatable authoring of the five-house objective and rescue Canvas.</summary>
public static class VolcanoRescuePolishSetup
{
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    static readonly Color Panel = new Color(.094f, .09f, .086f, .94f);
    static readonly Color Card = new Color(.13f, .125f, .118f, .96f);
    static readonly Color Accent = new Color(.91f, .365f, .165f, 1);
    static readonly Color TextColor = new Color(.95f, .915f, .875f, 1);
    static Font font;
    [MenuItem("GameJam/Integration/Build rescue objective and HUD")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring rescue UI.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before rebuilding rescue UI.");
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ConfigureScene(scene);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log("VOLCANO_RESCUE_POLISH_READY: five houses, pause/victory UI, 250 L starting water.");
    }
    public static void ConfigureScene(Scene scene)
    {
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var eruption = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Where(h => h.gameObject.scene == scene).OrderBy(h => h.transform.position.z).ToArray();
        if (!player || !player.hose || !player.drive || !player.cannon || !eruption || houses.Length != RescueGameFlow.RequiredHouses)
            throw new InvalidOperationException("Expected one complete rescue rig and exactly five authored houses.");
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
        var hud = root.AddComponent<RescueHUD>(); hud.flow = flow; hud.player = player; hud.hose = player.hose;
        hud.drive = player.drive; hud.cannon = player.cannon; hud.eruption = eruption; hud.canvas = canvas;
        player.gameFlow = flow; player.showHud = false; eruption.showHud = false;
        player.hose.startingTankLitres = 250f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.hose);
        PrefabUtility.RecordPrefabInstancePropertyModifications(eruption);
        foreach (var house in houses) { house.startBurning = false; PrefabUtility.RecordPrefabInstancePropertyModifications(house); }
        Transform parent = root.transform;
        var resources = Box(parent, "Resources", new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -28), new Vector2(360, 198), Panel);
        Box(resources, "Accent", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(4, 198), Accent);
        hud.objectiveText = Label(resources, "Objective", "KURTARILAN EVLER   0 / 5", 21, new Vector2(20, -15), new Vector2(325, 32));
        Label(resources, "WaterLabel", "SU DEPOSU", 13, new Vector2(20, -56), new Vector2(140, 24));
        hud.waterText = Label(resources, "WaterLitres", "250 / 2000 L", 18, new Vector2(170, -52), new Vector2(165, 28), TextAnchor.MiddleRight);
        var waterTrack = Box(resources, "WaterTrack", new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -89), new Vector2(315, 8), new Color(.25f, .235f, .22f, 1));
        hud.waterFill = Fill(waterTrack, "WaterFill", new Color(.34f, .67f, .8f, 1));
        hud.pumpText = Label(resources, "PumpStatus", "Hortum araçta.", 16, new Vector2(20, -112), new Vector2(315, 66));
        hud.lowWaterWarning = Box(parent, "LowWaterWarning", new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -238), new Vector2(360, 42), new Color(.4f, .12f, .04f, .95f)).gameObject;
        hud.lowWaterText = Label(hud.lowWaterWarning.transform, "Text", "SU AZ  ·  GÖLDEN DOLDUR", 17, new Vector2(16, -5), new Vector2(330, 32));
        hud.lowWaterBackground = hud.lowWaterWarning.GetComponent<Image>();
        hud.lowWaterWarning.SetActive(false);
        var volcano = Box(parent, "VolcanoStatus", Vector2.one, Vector2.one, new Vector2(-28, -28), new Vector2(320, 100), Panel);
        hud.eruptionText = Label(volcano, "Text", "VOLKAN · HAZIRLIK", 20, new Vector2(20, -16), new Vector2(280, 70));
        var speed = Box(parent, "Speed", Vector2.zero, Vector2.zero, new Vector2(28, 28), new Vector2(170, 54), Panel);
        hud.speedText = Label(speed, "Text", "YAYA", 24, new Vector2(16, -7), new Vector2(138, 40));
        var prompt = Box(parent, "Prompt", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 28), new Vector2(1060, 54), Panel);
        hud.promptText = Label(prompt, "Text", "", 18, new Vector2(18, -7), new Vector2(1024, 40), TextAnchor.MiddleCenter);
        var pauseHint = Box(parent, "PauseHint", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-28, 98), new Vector2(170, 54), Panel);
        Label(pauseHint, "Text", "ESC  ·  DURAKLAT", 15, new Vector2(12, -7), new Vector2(146, 40), TextAnchor.MiddleCenter);
        var target = Box(parent, "TargetFire", new Vector2(.5f, 0), new Vector2(.5f, 0), new Vector2(0, 105), new Vector2(360, 76), Panel);
        hud.targetGroup = target.gameObject;
        hud.targetText = Label(target, "Text", "YANGIN SÖNDÜRÜLÜYOR", 16, new Vector2(16, -10), new Vector2(328, 28), TextAnchor.MiddleCenter);
        var targetTrack = Box(target, "ProgressTrack", new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -50), new Vector2(320, 7), new Color(.25f, .235f, .22f, 1));
        hud.targetFill = Fill(targetTrack, "ProgressFill", Accent); target.gameObject.SetActive(false);
        var toast = Box(parent, "RescueToast", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -36), new Vector2(520, 48), Panel);
        hud.toastText = Label(toast, "Text", "", 22, new Vector2(16, -5), new Vector2(488, 38), TextAnchor.MiddleCenter);
        // The toast's parent follows its text visibility, avoiding an empty plate at startup.
        toast.GetComponent<Image>().enabled = false;
        var reticle = Box(parent, "PredictedWaterImpact", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(18, 2), Color.white);
        Box(reticle, "Vertical", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(2, 18), Color.white);
        hud.impactReticle = reticle.GetComponent<Image>();
        hud.pausePanel = Modal(parent, "PausePanel", "OYUN DURAKLATILDI", "Kasaba seni bekliyor.", false, out Button resume, out Button pauseMenu);
        hud.resumeButton = resume; hud.pauseMenuButton = pauseMenu;
        hud.victoryPanel = Modal(parent, "VictoryPanel", "KAZANDINIZ", "5 / 5 EV KURTARILDI\nKasaba yeniden güvende.", true, out _, out Button victoryMenu);
        hud.victoryMenuButton = victoryMenu;
        if (!Object.FindFirstObjectByType<EventSystem>())
        {
            var events = new GameObject("Rescue EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(events, scene);
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        EditorUtility.SetDirty(player); EditorUtility.SetDirty(player.hose); EditorUtility.SetDirty(eruption); EditorUtility.SetDirty(flow); EditorUtility.SetDirty(hud);
    }
    static CanvasGroup Modal(Transform parent, string name, string title, string detail, bool win, out Button resume, out Button menu)
    {
        var overlay = Box(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color(.025f, .02f, .018f, .82f));
        Stretch(overlay); overlay.GetComponent<Image>().raycastTarget = true;
        var group = overlay.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = group.blocksRaycasts = false;
        var card = Box(overlay, "Panel", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(680, 430), Card);
        Box(card, "Accent", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(680, 5), Accent);
        Label(card, "Brand", "ASHFALL", 16, new Vector2(50, -35), new Vector2(580, 25), TextAnchor.MiddleCenter).color = Accent;
        Label(card, "Title", title, 35, new Vector2(35, -82), new Vector2(610, 60), TextAnchor.MiddleCenter);
        Label(card, "Detail", detail, 20, new Vector2(50, -153), new Vector2(580, 68), TextAnchor.MiddleCenter);
        resume = win ? null : Button(card, "Resume", "DEVAM ET", new Vector2(130, -245));
        menu = Button(card, "ReturnToMenu", "ANA MENÜYE DÖN", new Vector2(130, win ? -280 : -325));
        return group;
    }
    static Button Button(Transform parent, string name, string label, Vector2 position)
    {
        var rect = Box(parent, name, new Vector2(0, 1), new Vector2(0, 1), position, new Vector2(420, 60), Accent);
        var image = rect.GetComponent<Image>(); image.raycastTarget = true;
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/AshfallMenuButton.png");
        if (sprite) { image.sprite = sprite; image.type = Image.Type.Sliced; image.color = Color.white; }
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .76f, .5f); colors.pressedColor = new Color(.75f, .45f, .25f); button.colors = colors;
        Label(rect, "Label", label, 22, new Vector2(12, -6), new Vector2(396, 48), TextAnchor.MiddleCenter);
        return button;
    }
    static RectTransform Box(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor; rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false;
        return rect;
    }
    static Text Label(Transform parent, string name, string text, int size, Vector2 position, Vector2 dimensions, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1); rect.anchoredPosition = position; rect.sizeDelta = dimensions;
        var label = go.GetComponent<Text>(); if (!font) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.font = font; label.text = text; label.fontSize = size; label.color = TextColor; label.alignment = align;
        label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; label.raycastTarget = false;
        return label;
    }
    static Image Fill(Transform parent, string name, Color color)
    {
        var rect = Box(parent, name, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, color); Stretch(rect);
        var image = rect.GetComponent<Image>(); image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillAmount = .125f;
        return image;
    }
    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
