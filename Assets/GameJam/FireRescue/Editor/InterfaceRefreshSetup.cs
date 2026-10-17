using System;
using System.IO;
using System.Linq;
using System.Text;
using Ashfall.UI;
using GameJam.FireRescue;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Updates the existing UI bindings and typography without rebuilding the game.</summary>
[InitializeOnLoad]
public static class InterfaceRefreshSetup
{
    public const string Folder = "Library/UIRefreshReview";
    const string Fonts = "Assets/Resources/ASHFALL/UI/Fonts/";
    static double next;
    static InterfaceRefreshSetup()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/ready.txt", DateTime.UtcNow.ToString("O"));
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        string path = Folder + "/request.txt";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "apply") Apply();
            else if (command == "repair")
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.isDirty)
                {
                    EditorSceneManager.SaveScene(scene, Folder + "/UI-before-retry.unity", true);
                    EditorSceneManager.SaveScene(scene);
                }
                Apply();
            }
            else if (command == "inspect") Inspect();
            else if (command == "close")
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
                    throw new InvalidOperationException("Close requires the saved scene in Edit Mode.");
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else if (command == "checks") CleanHudChecks.Run();
            else throw new InvalidOperationException("Unknown interface command: " + command);
            File.WriteAllText(Folder + "/result.txt", "OK " + command + " " + DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/result.txt", ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("GameJam/Final Loop/Refresh interface and project typography")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var active = SceneManager.GetActiveScene();
        if (active.isDirty) throw new InvalidOperationException("Preserve unsaved scene edits before updating the interface.");
        RequireGlyphs(AshfallTypography.Regular); RequireGlyphs(AshfallTypography.Semibold); RequireGlyphs(AshfallTypography.Compact);
        var body = FontAsset("Barlow SDF", AshfallTypography.Regular);
        var display = FontAsset("Chakra SDF", AshfallTypography.Semibold);
        var report = new StringBuilder();
        var paths = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path)
            .Concat(new[] { FinalLoopSetup.ScenePath, "Assets/Scenes/MainMenu.unity" }).Distinct().ToArray();
        foreach (string path in paths)
        {
            var scene = EditorSceneManager.OpenScene(path);
            // Replace the old interaction atlas before the HUD validates Turkish glyphs.
            foreach (var root in scene.GetRootGameObjects()) StyleTree(root, body, display);
            File.WriteAllText(Folder + "/font-binding-before-save.txt", string.Join("\n", Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Select(t => t.name + ": " + t.font.name + " / Turkish=" + t.font.HasCharacter('ğ'))));
            if (path == FinalLoopSetup.ScenePath) CleanHudSetup.Apply();
            foreach (var root in scene.GetRootGameObjects()) StyleTree(root, body, display);
            var story = Object.FindFirstObjectByType<VolcanoStorySequence>();
            if (story) { story.subtitleFont = AshfallTypography.Regular; EditorUtility.SetDirty(story); }
            var hud = Object.FindFirstObjectByType<RescueHUD>();
            if (hud) { hud.RefreshCleanLayout(); CleanHudSetup.Validate(); }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            report.AppendLine("UPDATED " + path);
        }
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/GameJam", "Assets/Itfaiye" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (!root.GetComponentsInChildren<Text>(true).Any() && !root.GetComponentsInChildren<TMP_Text>(true).Any() &&
                    !root.GetComponentsInChildren<TextMesh>(true).Any()) continue;
                StyleTree(root, body, display); PrefabUtility.SaveAsPrefabAsset(root, path); report.AppendLine("UPDATED " + path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(FinalLoopSetup.ScenePath);
        File.WriteAllText(Folder + "/setup.txt", report.ToString()); Inspect();
    }
    static void RequireGlyphs(Font font)
    {
        if (!font) throw new InvalidOperationException("A project font was not imported.");
        const string glyphs = "ÇĞİÖŞÜçğıöşü0123456789";
        font.RequestCharactersInTexture(glyphs, 26, FontStyle.Normal);
        foreach (char c in glyphs) if (!font.HasCharacter(c)) throw new InvalidOperationException(font.name + " is missing " + c);
    }
    static TMP_FontAsset FontAsset(string name, Font source)
    {
        string path = Fonts + name + ".asset";
        string glyphs = new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray()) + ContextualInteractionSetup.Glyphs + "—…×°";
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (existing)
        {
            if (glyphs.All(c => existing.HasCharacter(c)))
            {
                existing.atlasPopulationMode = AtlasPopulationMode.Static;
                return existing;
            }
            var serialized = new SerializedObject(existing);
            serialized.FindProperty("m_AtlasPopulationMode").intValue = (int)AtlasPopulationMode.Dynamic;
            serialized.FindProperty("m_SourceFontFile").objectReferenceValue = source;
            serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!existing.TryAddCharacters(glyphs, out string remaining)) throw new InvalidOperationException(name + " missing glyphs: " + remaining);
            existing.atlasPopulationMode = AtlasPopulationMode.Static;
            foreach (var texture in existing.atlasTextures) EditorUtility.SetDirty(texture);
            EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }
        var font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
        font.name = name; AssetDatabase.CreateAsset(font, path);
        if (!font.TryAddCharacters(glyphs, out string missing)) throw new InvalidOperationException(name + " missing glyphs: " + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        foreach (var texture in font.atlasTextures) { texture.name = name + " Atlas"; AssetDatabase.AddObjectToAsset(texture, font); }
        var material = font.material; material.name = name + " readable shadow";
        material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.02f, .025f, .035f, 1)); material.SetFloat(ShaderUtilities.ID_OutlineWidth, .07f);
        material.EnableKeyword("UNDERLAY_ON"); material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0, 0, 0, .8f));
        material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -.5f); material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .2f);
        AssetDatabase.AddObjectToAsset(material, font); EditorUtility.SetDirty(font); AssetDatabase.SaveAssetIfDirty(font); return font;
    }
    static void StyleTree(GameObject root, TMP_FontAsset body, TMP_FontAsset display)
    {
        foreach (var text in root.GetComponentsInChildren<Text>(true))
        {
            if (text.name == "Direction" || text.name == "Arrow") continue;
            text.font = AshfallTypography.Face(AshfallTypography.Infer(text)); text.fontStyle = FontStyle.Normal;
            Dirty(text);
        }
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            var font = AshfallTypography.UsesBodyFace(text) ? body : display;
            text.font = font; text.fontSharedMaterial = font.material; text.fontStyle = FontStyles.Normal;
            text.characterSpacing = 0; Dirty(text);
        }
        foreach (var text in root.GetComponentsInChildren<TextMesh>(true))
        {
            text.font = AshfallTypography.Semibold; text.fontStyle = FontStyle.Normal;
            var atlas = text.GetComponent<WorldLetteringFontAtlas>(); if (atlas) atlas.RefreshAtlas();
            else if (text.TryGetComponent<MeshRenderer>(out var renderer)) { renderer.sharedMaterial = text.font.material; EditorUtility.SetDirty(renderer); }
            Dirty(text);
        }
        foreach (var ui in root.GetComponentsInChildren<ContextualInteractionUI>(true))
        {
            ui.titleText.fontSize = 30; ui.keyText.fontSize = 34; ui.detailText.fontSize = 22; ui.vehiclePromptWidth = 330;
            Dirty(ui); Dirty(ui.titleText); Dirty(ui.keyText); Dirty(ui.detailText);
        }
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.name == "ObsidianCore") { image.gameObject.SetActive(false); Dirty(image.gameObject); continue; }
            string sprite = AssetDatabase.GetAssetPath(image.sprite), name = image.name.ToLowerInvariant();
            if (!sprite.StartsWith("Assets/Art/UI/") || !(name.Contains("panel") || name.Contains("frame") || name.Contains("background") || name.Contains("plate"))) continue;
            image.sprite = null; image.material = null; image.type = Image.Type.Simple; image.color = new Color(.026f, .034f, .043f, .93f);
            foreach (var shadow in image.GetComponents<Shadow>()) { shadow.enabled = false; Dirty(shadow); }
            Dirty(image);
        }
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            if (!(button.targetGraphic is Image image)) continue;
            string sprite = AssetDatabase.GetAssetPath(image.sprite);
            if (!sprite.StartsWith("Assets/Art/UI/") && image.sprite) continue;
            image.sprite = null; image.material = null; image.type = Image.Type.Simple; image.color = new Color(.06f, .085f, .11f, .97f);
            foreach (var shadow in image.GetComponents<Shadow>()) { shadow.enabled = false; Dirty(shadow); }
            var colors = button.colors; colors.normalColor = Color.white; colors.highlightedColor = new Color(1, .66f, .38f);
            colors.selectedColor = colors.highlightedColor; colors.pressedColor = new Color(.8f, .38f, .18f); button.colors = colors;
            var outline = image.GetComponent<Outline>(); if (!outline) outline = image.gameObject.AddComponent<Outline>();
            outline.enabled = true; outline.effectColor = new Color(.55f, .64f, .7f, .3f); outline.effectDistance = new Vector2(1, -1);
            Dirty(outline); Dirty(image); Dirty(button);
        }
    }
    static void Dirty(Object item)
    {
        EditorUtility.SetDirty(item);
        if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
    }
    public static void Inspect()
    {
        var scene = SceneManager.GetActiveScene(); var hud = Object.FindFirstObjectByType<RescueHUD>();
        var report = new StringBuilder("scene=" + scene.path + " dirty=" + scene.isDirty + " playing=" + EditorApplication.isPlaying + "\n");
        if (hud)
        {
            foreach (var text in new[] { hud.questTitleText, hud.questBodyText, hud.eruptionText, hud.subtitleText })
                report.AppendLine(text.name + " font=" + text.font.name + " size=" + text.fontSize + " rect=" + text.rectTransform.rect);
            report.AppendLine("opening=" + hud.mission.story + " closing=" + hud.flow.victoryCinematic + " villagers=" + hud.evacuation.TotalCount);
            foreach (var item in hud.questTitleText.transform.parent.GetComponentsInChildren<Graphic>(true))
                report.AppendLine("QUEST GRAPHIC " + item.name + " type=" + item.GetType().Name + " color=" + item.color + " material=" + item.material.name + " shader=" + item.material.shader.name + (item is Image image ? " sprite=" + image.sprite + " mode=" + image.type : ""));
        }
        foreach (var label in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine("TMP " + label.name + " font=" + label.font.name + " path=" + AssetDatabase.GetAssetPath(label.font) + " Turkish=" + label.font.HasCharacter('ğ') + " characters=" + label.font.characterTable.Count);
        File.WriteAllText(Folder + "/inspection.txt", report.ToString());
    }
}
