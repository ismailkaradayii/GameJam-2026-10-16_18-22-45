#if UNITY_EDITOR
using Ashfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Applies the project's Modular font to existing authored UI without changing bindings or layout.</summary>
public static class ModularFontMigration
{
    const string FontPath = "Assets/Resources/ASHFALL/UI/Fonts/Modular.ttf";
    const string TurkishGlyphs = "ÇĞİÖŞÜçğıöşü";

    [MenuItem("ASHFALL/Typography/Apply Modular To Project UI")]
    public static void ApplyToProjectUi()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("Stop Play Mode before changing project typography.");

        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (!font) throw new System.InvalidOperationException("Modular.ttf is missing at " + FontPath);
        font.RequestCharactersInTexture(TurkishGlyphs + "0123456789", 18, FontStyle.Normal);
        bool supportsTurkish = SupportsTurkish(font);

        int textCount = 0, worldTextCount = 0, prefabCount = 0, sceneCount = 0;
        foreach (string prefabPath in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(prefabPath);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int changed = Apply(root, font, supportsTurkish, ref worldTextCount);
                textCount += changed;
                if (changed > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabCount++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        foreach (string sceneGuid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(sceneGuid);
            Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            int changed = 0;
            foreach (GameObject root in scene.GetRootGameObjects()) changed += Apply(root, font, supportsTurkish, ref worldTextCount);
            textCount += changed;
            if (changed > 0) { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); sceneCount++; }
        }

        AssetDatabase.SaveAssets();
        Debug.Log("MODULAR_FONT_READY: " + textCount + " UI Text components and " + worldTextCount + " world TextMesh components updated across " + prefabCount + " prefabs and " + sceneCount + " scenes. Turkish glyph support: " + supportsTurkish + ".");
    }

    static int Apply(GameObject root, Font font, bool supportsTurkish, ref int worldTextCount)
    {
        int changed = 0;
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
        {
            if (text.font != font) { text.font = font; changed++; }
            if (!supportsTurkish) text.text = ModularTextSanitizer.ToAsciiTurkish(text.text);
            text.raycastTarget = false;
            EditorUtility.SetDirty(text);
        }
        foreach (TextMesh text in root.GetComponentsInChildren<TextMesh>(true))
        {
            if (text.font != font) { text.font = font; worldTextCount++; }
            if (!supportsTurkish) text.text = ModularTextSanitizer.ToAsciiTurkish(text.text);
            EditorUtility.SetDirty(text);
        }
        return changed;
    }

    static bool SupportsTurkish(Font font)
    {
        foreach (char character in TurkishGlyphs)
            if (!font.HasCharacter(character)) return false;
        return true;
    }

}
#endif
