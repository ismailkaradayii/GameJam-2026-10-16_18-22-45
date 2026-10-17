#if UNITY_EDITOR
using Ashfall.MainMenu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>One-time authoring utility for the checked-in ASHFALL menu scene.</summary>
public static class AshfallMenuSceneBuilder
{
    [MenuItem("ASHFALL/Integrate Menu Background Video")]
    public static void IntegrateVideoBackground()
    {
        const string videoPath = "Assets/Art/BackGround/arkaplan.mp4";
        VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(videoPath);
        if (clip == null)
            throw new System.Exception($"Required menu video was not found or imported: {videoPath}");

        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        GameObject canvasObject = GameObject.Find("Canvas");
        if (canvasObject == null) throw new System.Exception("MainMenu scene has no existing Canvas.");

        RenderTexture target = EnsureBackgroundRenderTexture();
        RenderTexture transition = EnsureTransitionRenderTexture();
        Transform canvas = canvasObject.transform;
        Transform videoTransform = FindChild(canvas, "VideoBackground");
        RawImage rawImage;
        if (videoTransform == null)
        {
            var videoImage = new GameObject("VideoBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            videoImage.transform.SetParent(canvas, false);
            videoTransform = videoImage.transform;
        }
        rawImage = videoTransform.GetComponent<RawImage>();
        if (rawImage == null) rawImage = videoTransform.gameObject.AddComponent<RawImage>();
        RectTransform rect = videoTransform.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rawImage.texture = target;
        rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        rawImage.color = Color.white;
        rawImage.raycastTarget = false;
        rawImage.enabled = false;
        videoTransform.SetAsFirstSibling();

        Transform blendTransform = FindChild(canvas, "VideoLoopBlend");
        if (blendTransform == null)
        {
            var blendObject = new GameObject("VideoLoopBlend", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            blendObject.transform.SetParent(canvas, false);
            blendTransform = blendObject.transform;
        }
        RawImage blendImage = blendTransform.GetComponent<RawImage>();
        if (blendImage == null) blendImage = blendTransform.gameObject.AddComponent<RawImage>();
        RectTransform blendRect = blendTransform.GetComponent<RectTransform>();
        blendRect.anchorMin = Vector2.zero;
        blendRect.anchorMax = Vector2.one;
        blendRect.pivot = new Vector2(0.5f, 0.5f);
        blendRect.anchoredPosition = Vector2.zero;
        blendRect.sizeDelta = Vector2.zero;
        blendRect.offsetMin = Vector2.zero;
        blendRect.offsetMax = Vector2.zero;
        blendImage.texture = transition;
        blendImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        blendImage.color = new Color(1f, 1f, 1f, 0f);
        blendImage.raycastTarget = false;
        blendImage.enabled = false;
        blendTransform.SetSiblingIndex(videoTransform.GetSiblingIndex() + 1);

        GameObject playerObject = GameObject.Find("ASHFALLVideoPlayer");
        if (playerObject == null) playerObject = new GameObject("ASHFALLVideoPlayer");
        VideoPlayer player = playerObject.GetComponent<VideoPlayer>();
        if (player == null) player = playerObject.AddComponent<VideoPlayer>();
        player.source = VideoSource.VideoClip;
        player.clip = clip;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = target;
        player.aspectRatio = VideoAspectRatio.FitOutside;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.playOnAwake = false;
        player.isLooping = false;
        player.waitForFirstFrame = true;
        player.skipOnDrop = true;
        player.sendFrameReadyEvents = true;

        AshfallVideoBackgroundController backgroundController = playerObject.GetComponent<AshfallVideoBackgroundController>();
        if (backgroundController == null) backgroundController = playerObject.AddComponent<AshfallVideoBackgroundController>();
        SetPrivate(backgroundController, "videoPlayer", player);
        SetPrivate(backgroundController, "videoBackground", rawImage);
        SetPrivate(backgroundController, "loopBlendImage", blendImage);
        // The MP4 already contains the eruption. Remove the old still image and
        // disable duplicate particles, glow, and light effects.
        GameObject oldBackground = GameObject.Find("VolcanoBackground");
        if (oldBackground != null) Object.DestroyImmediate(oldBackground);
        GameObject effects = GameObject.Find("VolcanoEffects");
        if (effects != null) effects.SetActive(false);
        GameObject eruptionLight = GameObject.Find("EruptionLight");
        if (eruptionLight != null) eruptionLight.SetActive(false);
        Transform glowTransform = FindChild(canvas, "CraterGlow");
        if (glowTransform != null) glowTransform.gameObject.SetActive(false);

        AshfallMenuController menuController = Object.FindFirstObjectByType<AshfallMenuController>();
        if (menuController != null)
        {
            var serializedMenu = new SerializedObject(menuController);
            foreach (string field in new[] { "ambientEmbers", "eruptionSmoke", "eruptionSparks", "eruptionLight", "craterGlow", "cameraTransform" })
            {
                SerializedProperty property = serializedMenu.FindProperty(field);
                if (property != null) property.objectReferenceValue = null;
            }
            serializedMenu.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"ASHFALL: Integrated {videoPath} as a full-screen VideoPlayer background with a manual crossfade at each loop. The old static background was removed.");
    }

    [MenuItem("ASHFALL/Verify Menu Video Setup")]
    public static void VerifyVideoSetup()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Single);
        GameObject playerObject = GameObject.Find("ASHFALLVideoPlayer");
        Transform canvas = GameObject.Find("Canvas")?.transform;
        Transform rawTransform = canvas != null ? FindChild(canvas, "VideoBackground") : null;
        Transform blendTransform = canvas != null ? FindChild(canvas, "VideoLoopBlend") : null;
        VideoPlayer player = playerObject != null ? playerObject.GetComponent<VideoPlayer>() : null;
        RawImage raw = rawTransform != null ? rawTransform.GetComponent<RawImage>() : null;
        RawImage blend = blendTransform != null ? blendTransform.GetComponent<RawImage>() : null;
        RenderTexture target = player != null ? player.targetTexture : null;
        AshfallMenuController menu = Object.FindFirstObjectByType<AshfallMenuController>();
        bool oneCanvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length == 1;
        bool onePlayer = Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None).Length == 1;
        bool assignedClip = player != null && player.clip == AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Art/BackGround/arkaplan.mp4");
        bool manualLoop = player != null && !player.isLooping;
        bool correctTarget = target != null && target.name == "RT_ASHFALL_Background" && target.width == 1920 && target.height == 1080;
        bool correctImage = raw != null && raw.texture == target && !raw.raycastTarget && rawTransform.GetSiblingIndex() == 0
            && raw.rectTransform.anchorMin == Vector2.zero && raw.rectTransform.anchorMax == Vector2.one;
        bool correctBlend = blend != null && !blend.raycastTarget && blend.texture != null
            && blend.texture.name == "RT_ASHFALL_LoopTransition"
            && blendTransform.GetSiblingIndex() == rawTransform.GetSiblingIndex() + 1
            && blend.rectTransform.anchorMin == Vector2.zero && blend.rectTransform.anchorMax == Vector2.one;
        GameObject effectRoot = FindSceneObject(scene, "VolcanoEffects");
        bool noDuplicateFx = effectRoot == null || !effectRoot.activeInHierarchy;
        bool passed = scene.IsValid() && oneCanvas && onePlayer && GameObject.Find("Canvas") != null
            && playerObject != null && player != null && assignedClip && manualLoop
            && player.renderMode == VideoRenderMode.RenderTexture && correctTarget && correctImage && correctBlend && menu != null && noDuplicateFx;
        bool oldBackgroundRemoved = FindSceneObject(scene, "VolcanoBackground") == null;
        passed &= oldBackgroundRemoved;
        Debug.Log($"ASHFALL video validation detail: scene={scene.IsValid()}, canvasCount={Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length}, playerCount={Object.FindObjectsByType<VideoPlayer>(FindObjectsSortMode.None).Length}, clip={assignedClip}, manualCrossfadeLoop={manualLoop}, renderMode={(player != null ? player.renderMode.ToString() : "missing")}, target={correctTarget}, image={correctImage}, blend={correctBlend}, menu={menu != null}, fxDisabled={noDuplicateFx}, oldBackgroundRemoved={oldBackgroundRemoved}");
        if (!passed) throw new System.Exception("ASHFALL video setup validation failed; see scene references and hierarchy.");
        Debug.Log("ASHFALL video setup validation passed: one Canvas, one MP4 VideoPlayer with manual crossfade looping, assigned 1920x1080 RenderTexture, full-screen non-raycast video and transition RawImages behind UI, old still background removed, and duplicate eruption effects disabled.");
    }

    private static RenderTexture EnsureBackgroundRenderTexture()
    {
        const string folder = "Assets/Art/Background";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Art", "Background");
        const string path = folder + "/RT_ASHFALL_Background.renderTexture";
        RenderTexture texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (texture == null)
        {
            texture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
            {
                name = "RT_ASHFALL_Background",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            AssetDatabase.CreateAsset(texture, path);
        }
        else
        {
            texture.name = "RT_ASHFALL_Background";
            texture.Release();
            texture.width = 1920;
            texture.height = 1080;
            texture.depth = 0;
            texture.format = RenderTextureFormat.ARGB32;
            texture.antiAliasing = 1;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            EditorUtility.SetDirty(texture);
        }
        return texture;
    }

    private static RenderTexture EnsureTransitionRenderTexture()
    {
        const string folder = "Assets/Art/Background";
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Art", "Background");
        const string path = folder + "/RT_ASHFALL_LoopTransition.renderTexture";
        RenderTexture texture = AssetDatabase.LoadAssetAtPath<RenderTexture>(path);
        if (texture == null)
        {
            texture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32)
            {
                name = "RT_ASHFALL_LoopTransition",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            AssetDatabase.CreateAsset(texture, path);
        }
        else
        {
            texture.name = "RT_ASHFALL_LoopTransition";
            texture.Release();
            texture.width = 1920;
            texture.height = 1080;
            texture.depth = 0;
            texture.format = RenderTextureFormat.ARGB32;
            texture.antiAliasing = 1;
            texture.useMipMap = false;
            texture.autoGenerateMips = false;
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            EditorUtility.SetDirty(texture);
        }
        return texture;
    }

    private static Transform FindChild(Transform parent, string childName)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == childName) return child;
        return null;
    }

    private static GameObject FindSceneObject(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == objectName) return child.gameObject;
        return null;
    }

    private static void SetPrivate(object target, string field, object value)
    {
        var info = target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (info == null) throw new System.Exception($"Missing serialized field {field} on {target.GetType().Name}.");
        info.SetValue(target, value);
    }
}
#endif
