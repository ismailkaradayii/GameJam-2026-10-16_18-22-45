#if UNITY_EDITOR
using Ashfall.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Applies the lava-edged basalt plate artwork to the existing menu buttons.</summary>
public static class AshfallMenuButtonStyleBuilder
{
    private const string SpritePath = "Assets/Art/UI/AshfallMenuButton.png";
    [MenuItem("ASHFALL/Apply Lava Rock Menu Button Style")]
    public static void ApplyStyle()
    {
        TextureImporter importer = AssetImporter.GetAtPath(SpritePath) as TextureImporter;
        if (importer == null) throw new System.Exception($"Button sprite was not found: {SpritePath}");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.spriteBorder = new Vector4(150f, 100f, 150f, 100f);
        importer.SaveAndReimport();

        Sprite plate = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        if (plate == null) throw new System.Exception("Unity did not import the lava-rock plate as a Sprite.");

        AshfallMenuController controller = Object.FindFirstObjectByType<AshfallMenuController>();
        if (controller == null) throw new System.Exception("The existing AshfallMenuController was not found.");
        SerializedObject serialized = new SerializedObject(controller);
        SerializedProperty buttonsProperty = serialized.FindProperty("menuButtons");
        int styled = 0;

        for (int i = 0; i < buttonsProperty.arraySize; i++)
        {
            Button button = buttonsProperty.GetArrayElementAtIndex(i).objectReferenceValue as Button;
            if (button == null) continue;

            Image image = button.GetComponent<Image>();
            if (image == null) image = button.gameObject.AddComponent<Image>();
            image.sprite = plate;
            // This artwork is a complete plate, not a tileable frame. Drawing it
            // as a single image avoids collapsed 9-slice borders on compact buttons.
            image.type = Image.Type.Simple;
            image.fillCenter = true;
            image.preserveAspect = false;
            image.color = new Color(1f, 1f, 1f, 0f);
            image.raycastTarget = true;

            button.targetGraphic = image;
            button.transition = Selectable.Transition.None;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.88f, 0.72f, 1f);
            colors.pressedColor = new Color(0.78f, 0.5f, 0.32f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.48f, 0.43f, 0.4f, 0.78f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;

            Transform oldAccent = button.transform.Find("Accent");
            if (oldAccent != null) oldAccent.gameObject.SetActive(false);

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.alignment = TextAnchor.MiddleLeft;
                Ashfall.UI.AshfallTypography.Apply(label, Ashfall.UI.AshfallTypography.Role.Button, 48);
                if (Ashfall.UI.AshfallTypography.MainMenuDisplay != null)
                    label.font = Ashfall.UI.AshfallTypography.MainMenuDisplay;
                label.text = label.text.ToUpperInvariant();
                label.raycastTarget = false;
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.offsetMin = new Vector2(112f, 14f);
                labelRect.offsetMax = new Vector2(-72f, -14f);
                labelRect.localScale = Vector3.one;
            }

            if (button.transform.parent != null) button.transform.parent.localScale = Vector3.one;

            EditorUtility.SetDirty(button.gameObject);
            styled++;
        }

        if (styled == 0) throw new System.Exception("No existing main menu buttons were assigned to AshfallMenuController.");
        SerializedProperty mainMenuProperty = serialized.FindProperty("mainMenuGroup");
        CanvasGroup mainMenu = mainMenuProperty != null ? mainMenuProperty.objectReferenceValue as CanvasGroup : null;
        if (mainMenu != null)
        {
            RectTransform menuRect = mainMenu.GetComponent<RectTransform>();
            menuRect.anchorMin = new Vector2(0.055f, 0.12f);
            menuRect.anchorMax = new Vector2(0.43f, 0.60f);
            menuRect.offsetMin = Vector2.zero;
            menuRect.offsetMax = Vector2.zero;
        }
        Scene scene = controller.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"ASHFALL: Applied molten-basalt artwork and aligned labels on {styled} existing menu buttons.");
    }
}
#endif
