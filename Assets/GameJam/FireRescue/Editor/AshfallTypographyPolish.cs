using Ashfall.UI;
using Ashfall.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Applies the shared typography system to the existing authored UI scenes.</summary>
public static class AshfallTypographyPolish
{
    [MenuItem("ASHFALL/Preview/Pause UI in Play Mode")]
    public static void PreviewPause()
    {
        if (!EditorApplication.isPlaying) return;
        var flow = Object.FindFirstObjectByType<GameJam.FireRescue.RescueGameFlow>();
        if (flow) flow.Pause();
    }

    [MenuItem("ASHFALL/Polish All UI Typography")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode before authoring typography.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Save open scenes before authoring typography.");
        ValidateFont(AshfallTypography.Modular);

        AshfallSettingsSceneBuilder.Build();
        ApplyScene(SceneManager.GetActiveScene());
        VolcanoRescuePolishSetup.Run();
        ApplyScene(SceneManager.GetActiveScene());
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        Debug.Log("ASHFALL_TYPOGRAPHY_READY: both UI scenes saved with Modular, Turkish glyphs, restrained colors, and content padding applied.");
    }

    public static void ApplyScene(Scene scene)
    {
        int count = 0;
        foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.gameObject.scene != scene) continue;
            foreach (var text in canvas.GetComponentsInChildren<Text>(true))
            {
                text.text = text.name == "KeyLabel" ? text.text : AshfallTypography.NaturalCase(text.text);
                AshfallTypography.Apply(text, AshfallTypography.Infer(text));
                EditorUtility.SetDirty(text); count++;
            }
        }
        var controller = Object.FindFirstObjectByType<AshfallMenuController>();
        if (controller && controller.gameObject.scene == scene)
        {
            var source = new SerializedObject(controller);
            var buttons = source.FindProperty("menuButtons");
            for (int i = 0; i < buttons.arraySize; i++)
            {
                var button = buttons.GetArrayElementAtIndex(i).objectReferenceValue as Button;
                if (!button) continue;
                var label = button.GetComponentInChildren<Text>(true);
                if (!label) continue;
                AshfallTypography.Apply(label, AshfallTypography.Role.Button, 48);
                if (AshfallTypography.MainMenuDisplay) label.font = AshfallTypography.MainMenuDisplay;
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(112, 14); label.rectTransform.offsetMax = new Vector2(-72, -14);
                var plate = button.GetComponent<Image>();
                if (plate) plate.color = new Color(.82f, .8f, .77f);
            }
        }
        Canvas.ForceUpdateCanvases();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log("ASHFALL typography authored: " + scene.name + " / " + count + " Text components. Existing Text bindings retained.");
    }

    static void ValidateFont(Font font)
    {
        if (!font) throw new System.InvalidOperationException("Modular font asset is missing.");
        const string characters = "ÇĞİÖŞÜçğıöşü0123456789";
        font.RequestCharactersInTexture(characters, 18, FontStyle.Normal);
        foreach (char character in characters)
            if (!font.HasCharacter(character))
            {
                Debug.LogWarning(font.name + " lacks " + character + "; ModularTextSanitizer will use its ASCII UI equivalent.");
                return;
            }
    }
}
