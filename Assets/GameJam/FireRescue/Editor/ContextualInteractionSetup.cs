using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Explicit, scene-local authoring. Does not rebuild the mission, truck, houses or HUD.</summary>
public static class ContextualInteractionSetup
{
    public const string Root = "Assets/GameJam/FireRescue/UI/Contextual";
    public const string PrefabPath = Root + "/Contextual Interaction UI.prefab";
    public const string ReportFolder = "Logs/ContextualInteraction";
    public const string Glyphs = "ÇĞİÖŞÜçğıöşü ÂâÊêÎîÔôÛû +!?:;,.()[]/%0123456789 ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz–·’\"";

    [MenuItem("GameJam/Final Loop/Import contextual TMP resources")]
    public static void ImportResources()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (!Resources.Load<TMP_Settings>("TMP Settings") || !Shader.Find("TextMeshPro/Mobile/Distance Field"))
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
        }
        Directory.CreateDirectory(ReportFolder);
        File.WriteAllText(ReportFolder + "/resources.txt", "Existing bundled Unity TMP Essential Resources imported; no download or package dependency added.");
    }

    [MenuItem("GameJam/Final Loop/Apply contextual interaction UI")]
    public static void Apply()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode first.");
        if (!Resources.Load<TMP_Settings>("TMP Settings") || !Shader.Find("TextMeshPro/Mobile/Distance Field"))
            throw new InvalidOperationException("Run Import contextual TMP resources first.");
        EnsureFolders(Root);
        var font = EnsureFont();
        var words = AssetDatabase.LoadAssetAtPath<InteractionTextCatalog>(Root + "/Interaction Texts TR.asset");
        if (!words) { words = ScriptableObject.CreateInstance<InteractionTextCatalog>(); AssetDatabase.CreateAsset(words, Root + "/Interaction Texts TR.asset"); }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!prefab)
        {
            var authored = BuildPrefab(font, words);
            prefab = PrefabUtility.SaveAsPrefabAsset(authored, PrefabPath);
            Object.DestroyImmediate(authored);
        }
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (!mission || !mission.player || !mission.evacuation || !mission.fillStation) throw new InvalidOperationException("Missing existing mission references.");
        var player = mission.player;
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        var old = Object.FindFirstObjectByType<ContextualInteractionUI>();
        if (old) throw new InvalidOperationException("Contextual UI is already authored. Edit its Inspector bindings instead of creating a duplicate.");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        var ui = instance.GetComponent<ContextualInteractionUI>();
        ui.player = player; ui.mission = mission; ui.hud = hud; ui.words = words;
        var bindings = new GameObject("Contextual interaction anchors").transform;
        var targets = new List<ContextualInteractionTarget>();
        ContextualInteractionTarget Add(string name, ContextualInteractionKind kind, Transform anchor, Vector3 point, Transform visibility, float range, float angle, float priority)
        {
            var go = new GameObject(name); go.transform.SetParent(bindings, false);
            var t = go.AddComponent<ContextualInteractionTarget>();
            t.kind = kind; t.anchor = anchor; t.localPoint = point; t.visibilityRoot = visibility;
            t.maximumViewDistance = range; t.maximumLookAngle = angle; t.priority = priority;
            targets.Add(t); return t;
        }
        Add("Bedside pistol", ContextualInteractionKind.Pistol, mission.weaponPickup.transform, Vector3.zero,
            mission.weaponPickup.transform, mission.weaponPickup.pickupDistance, 35, 1);
        Add("Starting house exit (automatic)", ContextualInteractionKind.HouseExit, mission.houseExit, Vector3.up * 1.6f,
            null, 11, 28, .1f);
        foreach (var door in Object.FindObjectsByType<InteractiveDoor>(FindObjectsSortMode.None))
        {
            var t = Add("Door - " + door.name, ContextualInteractionKind.Door, door.transform, Vector3.zero,
                door.transform, player.interactionDistance + 2, 65, 1); t.door = door;
        }
        Vector3 entry = player.drive.transform.InverseTransformPoint(player.driverDoor.position);
        Add("Truck driver door", ContextualInteractionKind.TruckEntry, player.drive.transform, entry,
            player.drive.transform, player.interactionDistance + 5, 40, .9f);
        entry.x = -entry.x;
        Add("Truck opposite cab door", ContextualInteractionKind.TruckEntry, player.drive.transform, entry,
            player.drive.transform, player.interactionDistance + 5, 40, .9f);
        Add("Truck exit - E", ContextualInteractionKind.TruckExit, player.drive.transform, new Vector3(0, 2.5f, 1.5f),
            player.drive.transform, 42, 52, .05f);
        var distinctExits = new HashSet<Transform>();
        foreach (var resident in mission.evacuation.residents)
        {
            if (!resident) continue;
            var t = Add("Resident - " + resident.name, ContextualInteractionKind.Resident, resident.transform, Vector3.up * 1.65f,
                resident.transform, 12, 30, .6f); t.resident = resident;
            if (resident.houseExit && distinctExits.Add(resident.houseExit))
                Add("Call at " + resident.houseExit.name, ContextualInteractionKind.CallHouse, resident.houseExit, Vector3.up * 1.8f,
                    null, mission.evacuation.shoutRadius, 24, .2f);
        }
        Add("Shelter entrance (automatic)", ContextualInteractionKind.Shelter, mission.evacuation.shelterEntrance,
            Vector3.up * 1.7f, null, 26, 32, .45f);
        Add("Lake intake connection", ContextualInteractionKind.LakeFill, mission.fillStation.intakePoint,
            Vector3.up * .35f, null, 55, 60, 1);
        Add("Intake pickup", ContextualInteractionKind.HosePickup, player.hose.nozzle, Vector3.zero,
            player.hose.nozzle, player.interactionDistance + 3, 65, .8f);
        Add("Carried intake", ContextualInteractionKind.HoseDrop, player.hose.nozzle, Vector3.zero,
            player.hose.nozzle, 9, 85, 1);
        Add("Rear hose reel", ContextualInteractionKind.HoseSocket, player.hose.hoseSocket, Vector3.zero,
            player.drive.transform, player.interactionDistance + 3, 60, 1);
        foreach (var house in mission.gameFlow.targetBuildings)
        {
            if (!house) continue;
            var t = Add("Water on " + house.name, ContextualInteractionKind.BuildingFire, house.transform, Vector3.up * 3,
                house.transform, 100, 55, 1); t.building = house;
        }
        ui.targets = targets.ToArray();
        BindScenePresentation(ui);
        player.contextualInteractions = ui;
        if (hud) hud.contextualInteractions = ui;
        // The existing fallback prompt panel is disabled at runtime by RescueHUD, not removed.
        Dirty(ui); Dirty(player); Dirty(hud);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Validate();
    }

    static TMP_FontAsset EnsureFont()
    {
        const string path = Root + "/SourceSans3 Turkish SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font) return font;
        var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/ASHFALL/UI/Fonts/SourceSans3-Semibold.ttf");
        if (!source) throw new InvalidOperationException("Existing SourceSans3 font is missing.");
        font = TMP_FontAsset.CreateFontAsset(source, 64, 10, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        font.name = "SourceSans3 Turkish SDF";
        var serializedFont = new SerializedObject(font);
        var clearOnBuild = serializedFont.FindProperty("m_ClearDynamicDataOnBuild");
        if (clearOnBuild != null) { clearOnBuild.boolValue = false; serializedFont.ApplyModifiedPropertiesWithoutUndo(); }
        AssetDatabase.CreateAsset(font, path);
        if (!font.TryAddCharacters(Glyphs, out string missing)) throw new InvalidOperationException("Missing Turkish glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var texture in font.atlasTextures) { texture.name = font.name + " Atlas"; AssetDatabase.AddObjectToAsset(texture, font); }
        var material = font.material;
        material.name = "Contextual text - outline and shadow";
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.035f, .035f, .045f, 1));
        material.SetFloat(ShaderUtilities.ID_OutlineWidth, .19f);
        material.EnableKeyword("UNDERLAY_ON");
        material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(.015f, .015f, .02f, .7f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, .45f); material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.65f);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, .05f); material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .18f);
        AssetDatabase.AddObjectToAsset(material, font); Dirty(font); Dirty(material); AssetDatabase.SaveAssets();
        return font;
    }
    static GameObject BuildPrefab(TMP_FontAsset font, InteractionTextCatalog words)
    {
        var root = new GameObject("Contextual Interaction UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(ContextualInteractionUI));
        var ui = root.GetComponent<ContextualInteractionUI>(); ui.words = words;
        ui.canvas = root.GetComponent<Canvas>(); ui.canvas.renderMode = RenderMode.ScreenSpaceOverlay; ui.canvas.sortingOrder = 42;
        ui.canvasRect = root.GetComponent<RectTransform>();
        var scale = root.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scale.referenceResolution = new Vector2(1920, 1080); scale.matchWidthOrHeight = .5f;
        ui.promptRect = Rect("World prompt", root.transform, new Vector2(420, 240));
        ui.promptGroup = ui.promptRect.gameObject.AddComponent<CanvasGroup>(); ui.promptGroup.alpha = 0; ui.promptGroup.interactable = false; ui.promptGroup.blocksRaycasts = false;
        ui.titleText = Text("Action", ui.promptRect, font, 30, new Vector2(420, 56), new Vector2(0, 76));
        ui.keyText = Text("Actual input", ui.promptRect, font, 42, new Vector2(420, 82), new Vector2(0, 10));
        ui.detailText = Text("Condition", ui.promptRect, font, 22, new Vector2(420, 72), new Vector2(0, -64));
        var progress = Rect("Actual progress", ui.promptRect, new Vector2(140, 4)); progress.anchoredPosition = new Vector2(0, -114);
        var background = progress.gameObject.AddComponent<Image>(); background.color = new Color(.08f, .08f, .1f, .7f); background.raycastTarget = false;
        var fill = Rect("Fill", progress, new Vector2(140, 4)); ui.progressFill = fill.gameObject.AddComponent<Image>();
        ui.progressFill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        ui.progressFill.type = Image.Type.Filled; ui.progressFill.fillMethod = Image.FillMethod.Horizontal; ui.progressFill.fillOrigin = 0;
        ui.progressFill.color = new Color(1, .98f, .92f, 1); ui.progressFill.raycastTarget = false;
        ui.progressGroup = progress.gameObject; ui.progressGroup.SetActive(false);
        ui.notices = new ContextualInteractionUI.FloatingNotice[6];
        for (int i = 0; i < ui.notices.Length; i++)
        {
            var rect = Rect("Floating result " + (i + 1), root.transform, new Vector2(460, 75));
            var group = rect.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0; group.interactable = false; group.blocksRaycasts = false;
            ui.notices[i] = new ContextualInteractionUI.FloatingNotice { rect = rect, group = group,
                text = Text("Real event", rect, font, 28, new Vector2(460, 75), Vector2.zero) };
        }
        return root;
    }
    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.sizeDelta = size;
        return rect;
    }
    static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, float size, Vector2 dimensions, Vector2 position)
    {
        var rect = Rect(name, parent, dimensions); rect.anchoredPosition = position;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.fontSharedMaterial = font.material; text.fontSize = size; text.color = new Color(1, .98f, .92f, 1);
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.Normal; text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false; text.richText = false; text.extraPadding = true; text.text = "";
        return text;
    }
    static void EnsureFolders(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        int slash = folder.LastIndexOf('/'); EnsureFolders(folder.Substring(0, slash)); AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
    }
    static void Dirty(Object item) { if (item) EditorUtility.SetDirty(item); }

    [MenuItem("GameJam/Final Loop/Inspect contextual lake visibility")]
    public static void InspectLake()
    {
        var ui = Object.FindFirstObjectByType<ContextualInteractionUI>();
        Vector3 eye = ui.mission.fillStation.transform.position + new Vector3(0, 10.65f, -23);
        var lake = ui.targets.First(t => t.kind == ContextualInteractionKind.LakeFill);
        var report = new StringBuilder("Lake visibility rays from " + eye + "\n");
        for (float lift = .35f; lift <= 5.4f; lift += 1)
        {
            Vector3 point = lake.anchor.position + Vector3.up * lift, delta = point - eye;
            report.AppendLine("LIFT " + lift + ": " + string.Join("; ", Physics.RaycastAll(eye, delta.normalized, delta.magnitude - .08f, ~0, QueryTriggerInteraction.Ignore)
                .OrderBy(h => h.distance).Select(h => h.collider.name + " @" + h.distance.ToString("F2") + " " + h.point)));
        }
        File.WriteAllText(ReportFolder + "/lake-visibility.txt", report.ToString());
    }

    [MenuItem("GameJam/Final Loop/Refine contextual layout")]
    public static void RefineLayout()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var ui = Object.FindFirstObjectByType<ContextualInteractionUI>();
        if (!ui || ui.gameObject.scene.path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open authored Volcano_Demo first.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var prefab = root.GetComponent<ContextualInteractionUI>();
            prefab.promptRect.sizeDelta = new Vector2(420, 240);
            prefab.titleText.rectTransform.sizeDelta = new Vector2(420, 56);
            prefab.keyText.rectTransform.sizeDelta = new Vector2(420, 82);
            prefab.detailText.rectTransform.sizeDelta = new Vector2(420, 72);
            prefab.keyText.rectTransform.anchoredPosition = new Vector2(0, 10);
            prefab.progressGroup.GetComponent<RectTransform>().anchoredPosition = new Vector2(0, -114);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        BindScenePresentation(ui);
        Dirty(ui); Dirty(ui.words); PrefabUtility.RecordPrefabInstancePropertyModifications(ui);
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(ui.gameObject.scene); EditorSceneManager.SaveScene(ui.gameObject.scene);
        Validate();
    }
    static void BindScenePresentation(ContextualInteractionUI ui)
    {
        var hud = ui.hud;
        var lake = ui.targets.First(t => t.kind == ContextualInteractionKind.LakeFill);
        // The physical intake stays submerged. Present the guided connection at its blue shore ring.
        Vector3 shore = ui.mission.fillStation.transform.position;
        Vector3 towardWater = ui.mission.fillStation.intakePoint.position - shore; towardWater.y = 0;
        shore += towardWater.normalized * ui.mission.fillStation.parkingRadius * .4f + Vector3.up * 2.4f;
        lake.anchor = ui.mission.fillStation.transform;
        lake.localPoint = lake.anchor.InverseTransformPoint(shore); Dirty(lake);
        ui.avoidedHudRegions = new[] { hud.objectiveGroup ? hud.objectiveGroup.GetComponent<RectTransform>() : null,
            hud.waterGroup ? hud.waterGroup.GetComponent<RectTransform>() : null, hud.eruptionText ? hud.eruptionText.transform.parent as RectTransform : null,
            hud.toastText ? hud.toastText.transform.parent as RectTransform : null, hud.subtitleText ? hud.subtitleText.transform.parent as RectTransform : null };
    }

    [MenuItem("GameJam/Final Loop/Validate contextual interaction UI")]
    public static void Validate()
    {
        var errors = new List<string>(); var report = new StringBuilder();
        var all = Object.FindObjectsByType<ContextualInteractionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length != 1) errors.Add("Expected exactly one contextual Canvas, got " + all.Length);
        var ui = all.FirstOrDefault();
        if (ui)
        {
            if (!ui.player || !ui.mission || !ui.words || !ui.hud || !ui.canvas || !ui.canvasRect || !ui.promptRect || !ui.promptGroup || !ui.titleText || !ui.keyText || !ui.detailText || !ui.progressGroup || !ui.progressFill) errors.Add("Missing central prefab / scene reference.");
            if (ui.player.contextualInteractions != ui || ui.hud.contextualInteractions != ui) errors.Add("Input or legacy prompt is not connected.");
            if (ui.notices.Length < 4 || ui.notices.Any(n => !n.rect || !n.text || !n.group)) errors.Add("Floating text pool is incomplete.");
            if (ui.targets.Any(t => !t || !t.anchor)) errors.Add("Missing interaction target / anchor.");
            if (ui.targets.Count(t => t && t.kind == ContextualInteractionKind.Resident) != ui.mission.evacuation.TotalCount) errors.Add("Actual resident count differs from bound targets.");
            foreach (var label in ui.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!label.font || label.raycastTarget) errors.Add("TMP font or raycast setting invalid: " + label.name);
                foreach (char c in Glyphs) if (!char.IsWhiteSpace(c) && !label.font.HasCharacter(c)) errors.Add("Missing glyph " + c);
            }
            if (!PrefabUtility.IsPartOfPrefabInstance(ui)) errors.Add("Canvas is not connected to its reusable prefab.");
            report.AppendLine("RESIDENTS " + ui.mission.evacuation.TotalCount + "; TARGETS " + ui.targets.Length);
            report.AppendLine("INPUT interact=" + ui.player.interactKey + ", exit=" + ui.player.exitVehicleKey + ", call=" + ui.mission.evacuation.callKey + ", pump=" + ui.player.pumpKey + ", retract=" + ui.player.retractHoseKey);
            foreach (var t in ui.targets.Where(t => t)) report.AppendLine(t.kind + " " + t.name + " anchor=" + t.anchor.name + " point=" + t.FocusPosition(ui).ToString("F2"));
        }
        int missing = 0;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
        if (missing != 0) errors.Add("Missing scripts: " + missing);
        report.Insert(0, errors.Count == 0 ? "PASS scene, prefab, TMP Turkish glyphs, input and event bindings\n" : "FAILED\n" + string.Join("\n", errors) + "\n");
        Directory.CreateDirectory(ReportFolder); File.WriteAllText(ReportFolder + "/validation.txt", report.ToString());
        if (errors.Count > 0) throw new InvalidOperationException(string.Join("; ", errors));
    }
}
