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
        importer.spriteBorder = Vector4.zero;
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
            image.type = Image.Type.Simple;
            image.fillCenter = true;
            image.preserveAspect = false;
            image.color = Color.white;
            image.raycastTarget = true;

            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.82f, 0.66f, 1f);
            colors.pressedColor = new Color(0.72f, 0.35f, 0.18f, 1f);
            colors.selectedColor = new Color(1f, 0.9f, 0.78f, 1f);
            colors.disabledColor = new Color(0.48f, 0.43f, 0.4f, 0.78f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.12f;
            button.colors = colors;

            Transform oldAccent = button.transform.Find("Accent");
            if (oldAccent != null) oldAccent.gameObject.SetActive(false);

            Text label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.alignment = TextAnchor.MiddleLeft;
                label.fontStyle = FontStyle.Bold;
                label.fontSize = 39;
                label.color = new Color(1f, 0.94f, 0.87f, 1f);
                label.raycastTarget = false;
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.pivot = new Vector2(0.5f, 0.5f);
                labelRect.offsetMin = new Vector2(42f, 0f);
                labelRect.offsetMax = new Vector2(-24f, 0f);
            }

            EditorUtility.SetDirty(button.gameObject);
            styled++;
        }

        if (styled == 0) throw new System.Exception("No existing main menu buttons were assigned to AshfallMenuController.");
        Scene scene = controller.gameObject.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"ASHFALL: Applied molten-basalt artwork and aligned labels on {styled} existing menu buttons.");
    }
}
#endif
