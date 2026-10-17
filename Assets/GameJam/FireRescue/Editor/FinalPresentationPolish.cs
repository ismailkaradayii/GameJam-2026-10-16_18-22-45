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
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class FinalPresentationPolish
{
    public const string Folder = "Logs/FinalPresentation";
    const string Request = Folder + "/request.txt";
    static FinalPresentationPolish() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || File.Exists("Logs/FinalLoop.command")) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "apply") Apply();
            else if (command == "check") FinalPresentationChecks.Run();
            else throw new InvalidOperationException("Unknown presentation command: " + command);
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); Debug.LogException(ex); }
    }
    const string UiFolder = "Assets/GameJam/FireRescue/UI/";
    [MenuItem("GameJam/Final Loop/Apply final presentation fixes")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use the existing Volcano_Demo in Edit Mode.");
        Directory.CreateDirectory(Folder);
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        var fade = Object.FindFirstObjectByType<AshfallSceneFade>();
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (!hud || !nav || !fade || !mission) throw new InvalidOperationException("Existing scene bindings missing.");
        var report = new StringBuilder();
        MoveShelterPlaces(mission, report);
        var font = InteractionFont();
        var prefab = PrefabUtility.LoadPrefabContents(ContextualInteractionSetup.PrefabPath);
        try { StyleContext(prefab.GetComponent<ContextualInteractionUI>(), font); PrefabUtility.SaveAsPrefabAsset(prefab, ContextualInteractionSetup.PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var context in Object.FindObjectsByType<ContextualInteractionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None)) StyleContext(context, font);

        Undo.RecordObject(nav, "Brighter navigation");
        var glow = nav.marker.Find("Waypoint halo") as RectTransform;
        if (!glow)
        {
            glow = new GameObject("Waypoint halo", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(glow.gameObject, "Waypoint halo"); glow.SetParent(nav.marker, false);
        }
        var arrow = nav.arrow.rectTransform;
        glow.anchorMin = glow.anchorMax = arrow.anchorMin; glow.pivot = Vector2.one * .5f;
        glow.anchoredPosition = arrow.anchoredPosition + (Vector2.one * .5f - arrow.pivot) * arrow.rect.size;
        glow.sizeDelta = new Vector2(66, 66); glow.SetAsFirstSibling();
        nav.markerGlow = glow.GetComponent<Image>(); nav.markerGlow.sprite = Halo(); nav.markerGlow.raycastTarget = false;
        nav.ringBrightness = 3.2f; nav.arrow.fontSize = 25; nav.arrow.fontStyle = FontStyle.Bold;
        nav.label.font = Resources.Load<Font>("ASHFALL/UI/Fonts/Modular"); nav.label.fontSize = 19;
        var ring = nav.ring.sharedMaterial;
        Undo.RecordObject(ring, "Glowing objective ring");
        ring.SetFloat("_Surface", 1); ring.SetFloat("_Blend", 2); ring.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        ring.SetFloat("_DstBlend", (float)BlendMode.One); ring.SetFloat("_ZWrite", 0);
        ring.SetColor("_BaseColor", new Color(1.5f, 1.5f, 1.5f, 1));
        ring.SetOverrideTag("RenderType", "Transparent"); ring.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); ring.renderQueue = 3000;
        EditorUtility.SetDirty(ring); AssetDatabase.SaveAssetIfDirty(ring);
        foreach (var item in new Component[] { glow, nav.markerGlow, nav, nav.arrow, nav.label }) Dirty(item);

        Undo.RecordObjects(new Object[] { hud, hud.flow, fade }, "Gradual mission ending");
        hud.flow.resultDelaySeconds = 2.4f; hud.outcomeFadeInSeconds = .65f;
        fade.canvas.sortingOrder = 49; Dirty(fade.canvas);
        var ending = fade.canvas.transform.Find("Ending iris") as RectTransform;
        if (!ending)
        {
            ending = new GameObject("Ending iris", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            Undo.RegisterCreatedObjectUndo(ending.gameObject, "Ending iris"); ending.SetParent(fade.canvas.transform, false);
        }
        ending.anchorMin = Vector2.zero; ending.anchorMax = Vector2.one; ending.offsetMin = ending.offsetMax = Vector2.zero;
        fade.endingCurtain = ending.GetComponent<Image>(); fade.endingCurtain.color = Color.white; fade.endingCurtain.raycastTarget = false;
        string path = UiFolder + "Ending iris.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(Shader.Find("UI/Ashfall Ending Iris")); AssetDatabase.CreateAsset(material, path); }
        material.SetFloat("_Progress", 0); EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        fade.endingCurtain.material = material; ending.gameObject.SetActive(false);
        foreach (var text in hud.victoryPanel.GetComponentsInChildren<Text>(true))
        { text.font = nav.label.font; text.fontStyle = FontStyle.Normal; Dirty(text); }
        hud.victoryTitleText.fontSize = 34;
        foreach (var item in new Component[] { hud, hud.flow, fade, ending, fade.endingCurtain, hud.victoryTitleText }) Dirty(item);
        // Save the current authoring state with the scoped changes, preserving all existing edits.
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        report.AppendLine("PASS Bright HDR ring and pulsing halo bound to existing navigation.");
        report.AppendLine("PASS Compact vehicle prompts and Modular Turkish SDF applied to scene and reusable prefab.");
        report.AppendLine("PASS Existing fade canvas owns the ending iris; results wait 2.4s then fade in over 0.65s.");
        File.WriteAllText(Folder + "/setup.txt", report.ToString());
        if (File.Exists(Folder + "/error.txt")) File.Delete(Folder + "/error.txt");
    }

    static void MoveShelterPlaces(EvacuationMission mission, StringBuilder report)
    {
        Vector3[] desired = { new Vector3(-124.5f, 1.7f, -579), new Vector3(-127, 1.8f, -577.2f),
            new Vector3(-124.3f, 1.6f, -575.9f), new Vector3(-127.2f, 1.8f, -573.8f),
            new Vector3(-125, 1.6f, -571.8f), new Vector3(-128.3f, 1.8f, -570.6f), new Vector3(-125.8f, 1.6f, -568.4f) };
        var residents = mission.residents.Where(r => r).OrderBy(r => r.formationIndex).ToArray();
        if (residents.Length != desired.Length) throw new InvalidOperationException("Expected seven existing resident places.");
        var points = new Vector3[desired.Length];
        var filter = new NavMeshQueryFilter { agentTypeID = residents[0].GetComponent<NavMeshAgent>().agentTypeID, areaMask = NavMesh.AllAreas };
        var route = new NavMeshPath();
        for (int i = 0; i < desired.Length; i++)
        {
            if (!residents[i].safePlace || !NavMesh.SamplePosition(desired[i], out var hit, .85f, filter) ||
                !NavMesh.CalculatePath(mission.shelterEntrance.position, hit.position, filter, route) || route.status != NavMeshPathStatus.PathComplete ||
                EvacuationResident.FlatDistance(hit.position, mission.shelterEntrance.position) < 14f)
                throw new InvalidOperationException("Deep shelter place is not reachable: " + i);
            points[i] = hit.position;
            for (int j = 0; j < i; j++) if (EvacuationResident.FlatDistance(points[i], points[j]) < 2.5f)
                throw new InvalidOperationException("Deep shelter places overlap: " + i + "/" + j);
        }
        Undo.RecordObjects(residents.Select(r => (Object)r.safePlace).Append(mission.shelterSafeArea).ToArray(), "Move sheltered residents deeper into cave");
        var area = mission.shelterSafeArea;
        Vector3 min = area.center - area.size * .5f, max = area.center + area.size * .5f;
        for (int i = 0; i < residents.Length; i++)
        {
            var place = residents[i].safePlace;
            place.position = points[i]; place.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(mission.shelterEntrance.position - place.position, Vector3.up)); Dirty(place);
            Vector3 local = area.transform.InverseTransformPoint(place.position);
            min.x = Mathf.Min(min.x, local.x - 1.5f); min.z = Mathf.Min(min.z, local.z - 1.5f);
            max.x = Mathf.Max(max.x, local.x + 1.5f); max.z = Mathf.Max(max.z, local.z + 1.5f);
            report.AppendLine("PASS " + residents[i].name + " personal place=" + place.position.ToString("F2") + "; entrance distance=" + EvacuationResident.FlatDistance(place.position, mission.shelterEntrance.position).ToString("F1") + "m");
        }
        area.center = (min + max) * .5f; area.size = max - min; Dirty(area);
    }

    static TMP_FontAsset InteractionFont()
    {
        string path = UiFolder + "Contextual/Modular Interaction SDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font && font.atlasPopulationMode == AtlasPopulationMode.Static && font.material && font.material.IsKeywordEnabled("UNDERLAY_ON")) return font;
        // Repair only an incomplete font asset from an interrupted setup attempt.
        if (font) AssetDatabase.DeleteAsset(path);
        var source = Resources.Load<Font>("ASHFALL/UI/Fonts/Modular");
        font = TMP_FontAsset.CreateFontAsset(source, 64, 10, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        font.name = "Modular Interaction SDF"; AssetDatabase.CreateAsset(font, path);
        font.TryAddCharacters(ContextualInteractionSetup.Glyphs, out string missing);
        font.fallbackFontAssetTable = new List<TMP_FontAsset> { AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(UiFolder + "Contextual/SourceSans3 Turkish SDF.asset") };
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
        var material = font.material; material.name = "Modular interaction outline";
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.015f, .02f, .03f, 1)); material.SetFloat(ShaderUtilities.ID_OutlineWidth, .16f);
        material.EnableKeyword("UNDERLAY_ON"); material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, .7f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.5f); material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .15f);
        AssetDatabase.AddObjectToAsset(material, font); EditorUtility.SetDirty(font); AssetDatabase.SaveAssetIfDirty(font);
        return font;
    }
    static void StyleContext(ContextualInteractionUI ui, TMP_FontAsset font)
    {
        Undo.RecordObject(ui, "Compact vehicle interactions");
        ui.compactVehiclePrompts = true; ui.vehiclePromptWidth = 300; ui.vehicleScreenHeight = .47f;
        foreach (var label in new[] { ui.titleText, ui.keyText, ui.detailText })
        {
            Undo.RecordObject(label, "Modular interaction font"); label.font = font; label.fontSharedMaterial = font.material;
            label.fontStyle = FontStyles.Normal; label.enableAutoSizing = false; Dirty(label);
        }
        ui.titleText.fontSize = 28; ui.titleText.characterSpacing = 2;
        ui.keyText.fontSize = 36; ui.detailText.fontSize = 20; Dirty(ui);
    }
    static Sprite Halo()
    {
        string path = UiFolder + "Waypoint halo.asset";
        var existing = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault(); if (existing) return existing;
        const int size = 64; var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Waypoint halo", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
        {
            float radius = new Vector2((x + .5f) / size - .5f, (y + .5f) / size - .5f).magnitude * 2;
            pixels[y * size + x] = new Color(1, 1, 1, Mathf.Pow(Mathf.Clamp01(1 - radius), 2));
        }
        texture.SetPixels32(pixels); texture.Apply(); AssetDatabase.CreateAsset(texture, path);
        var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f, 100); sprite.name = "Waypoint halo";
        AssetDatabase.AddObjectToAsset(sprite, texture); EditorUtility.SetDirty(texture); AssetDatabase.SaveAssetIfDirty(texture); return sprite;
    }
    static void Dirty(Component value)
    {
        EditorUtility.SetDirty(value);
        if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
    }
    public static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        var hud = Object.FindFirstObjectByType<RescueHUD>();
        var scene = SceneManager.GetActiveScene();
        var text = new StringBuilder($"Scene={scene.path}; dirty={scene.isDirty}; play={EditorApplication.isPlaying}\n");
        text.AppendLine("Ring=" + AssetDatabase.GetAssetPath(nav.ring.sharedMaterial) + "; shader=" + nav.ring.sharedMaterial.shader.name);
        text.AppendLine("HUD canvas order=" + hud.canvas.sortingOrder + "; screen=" + Screen.width + "x" + Screen.height);
        var font = Resources.Load<Font>("ASHFALL/UI/Fonts/Modular");
        text.AppendLine("Modular Turkish=" + string.Join(" ", "ÇĞİÖŞÜçğıöşü".Select(c => c + ":" + font.HasCharacter(c))));
        Vector3 entrance = mission.shelterEntrance.position;
        text.AppendLine("Entrance=" + entrance);
        var filter = new NavMeshQueryFilter { agentTypeID = mission.residents[0].GetComponent<NavMeshAgent>().agentTypeID, areaMask = NavMesh.AllAreas };
        var path = new NavMeshPath();
        for (int x = -116; x >= -152; x -= 2)
            for (int z = -602; z <= -554; z += 2)
            {
                Vector3 desired = new Vector3(x, entrance.y, z);
                if (NavMesh.SamplePosition(desired, out var hit, .6f, filter) &&
                    NavMesh.CalculatePath(entrance, hit.position, filter, path) && path.status == NavMeshPathStatus.PathComplete)
                    text.AppendLine("GROUND " + hit.position.ToString("F2"));
            }
        File.WriteAllText(Folder + "/inspection.txt", text.ToString());
    }
}
