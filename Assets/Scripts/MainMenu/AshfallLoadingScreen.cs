using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Ashfall.MainMenu
{
    /// <summary>A menu-owned curtain; the mission only starts once scene activation is released.</summary>
    [DisallowMultipleComponent]
    public sealed class AshfallLoadingScreen : MonoBehaviour
    {
        const float MinimumVisibleSeconds = 1.5f;
        CanvasGroup curtain;
        RawImage animationImage;
        VideoPlayer video;
        RenderTexture target;
        Text fallback;
        bool videoFailed;

        public static IEnumerator Load(string sceneName, VideoClip clip)
        {
            var root = new GameObject("Loading screen", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AshfallLoadingScreen));
            var screen = root.GetComponent<AshfallLoadingScreen>();
            screen.Build(clip);
            yield return screen.LoadScene(sceneName);
        }

        void Build(VideoClip clip)
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            curtain = GetComponent<CanvasGroup>();
            curtain.alpha = 0;
            curtain.blocksRaycasts = true;

            var black = new GameObject("Black background", typeof(RectTransform), typeof(Image));
            black.transform.SetParent(transform, false);
            var background = black.GetComponent<Image>();
            background.color = Color.black;
            var full = black.GetComponent<RectTransform>();
            full.anchorMin = Vector2.zero; full.anchorMax = Vector2.one;
            full.offsetMin = full.offsetMax = Vector2.zero;

            var imageObject = new GameObject("Loading animation", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(transform, false);
            animationImage = imageObject.GetComponent<RawImage>();
            animationImage.raycastTarget = false;
            animationImage.enabled = false;
            var rect = animationImage.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            float aspect = clip && clip.height > 0 ? (float)clip.width / clip.height : 1;
            var fit = imageObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = aspect;

            if (!clip) { ShowFallback(); return; }
            int width = Mathf.Min(1024, (int)clip.width);
            int height = Mathf.Max(1, Mathf.RoundToInt(width / aspect));
            target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Loading animation (runtime)", useMipMap = false,
                filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
            };
            target.Create();
            animationImage.texture = target;
            var playerObject = new GameObject("Loading video player");
            playerObject.transform.SetParent(transform, false);
            video = playerObject.AddComponent<VideoPlayer>();
            video.playOnAwake = false;
            video.source = VideoSource.VideoClip;
            video.clip = clip;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.targetTexture = target;
            video.aspectRatio = VideoAspectRatio.FitInside;
            video.audioOutputMode = VideoAudioOutputMode.None;
            video.controlledAudioTrackCount = 0;
            video.isLooping = true;
            video.playbackSpeed = 1f;
            video.waitForFirstFrame = true;
            video.skipOnDrop = true;
            video.timeUpdateMode = VideoTimeUpdateMode.DSPTime;
            video.errorReceived += VideoError;
        }

        IEnumerator LoadScene(string sceneName)
        {
            // Fade only sources belonging to the departing menu; the mission owns its own mix.
            var sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var volumes = new float[sources.Length];
            for (int i = 0; i < sources.Length; i++) volumes[i] = sources[i].volume;
            float elapsed = 0;
            while (elapsed < .25f)
            {
                elapsed += Time.unscaledDeltaTime;
                curtain.alpha = Mathf.SmoothStep(0, 1, elapsed / .25f);
                for (int i = 0; i < sources.Length; i++)
                    if (sources[i] && sources[i].gameObject.scene == gameObject.scene)
                        sources[i].volume = volumes[i] * (1 - curtain.alpha);
                yield return null;
            }
            curtain.alpha = 1;
            foreach (var background in FindObjectsByType<AshfallVideoBackgroundController>(FindObjectsSortMode.None))
                if (background.gameObject.scene == gameObject.scene) background.enabled = false;

            float deadline = Time.realtimeSinceStartup + 5;
            // Play performs native preparation and starts playback in the same request.
            if (video) video.Play();
            bool retried = false;
            while (!videoFailed && (!video.isPrepared || !video.isPlaying || video.frame < 2) && Time.realtimeSinceStartup < deadline)
            {
                if (video.isPrepared && video.isPaused && !retried) { video.Play(); retried = true; }
                yield return null;
            }
            if (!videoFailed && (!video.isPrepared || !video.isPlaying || video.frame < 2)) ShowFallback();
            else if (!videoFailed) animationImage.enabled = true;
            // Give the animation real rendered frames before potentially expensive deserialization.
            // A long loading frame must not consume the entire minimum display duration.
            float visibleSeconds = 0;
            while (visibleSeconds < .5f)
            {
                visibleSeconds += Mathf.Min(Time.unscaledDeltaTime, .05f);
                yield return null;
            }
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null) { Debug.LogError("Could not load " + sceneName, this); yield break; }
            operation.allowSceneActivation = false;
            while (operation.progress < .9f || visibleSeconds < MinimumVisibleSeconds)
            {
                visibleSeconds += Mathf.Min(Time.unscaledDeltaTime, .05f);
                yield return null;
            }
            elapsed = 0;
            while (elapsed < .18f)
            {
                elapsed += Time.unscaledDeltaTime;
                float alpha = 1 - Mathf.SmoothStep(0, 1, elapsed / .18f);
                animationImage.color = new Color(1, 1, 1, alpha);
                if (fallback) fallback.color = new Color(1, 1, 1, alpha);
                yield return null;
            }
            // The menu owns this overlay, so unloading it reveals the mission's existing opening fade.
            operation.allowSceneActivation = true;
            while (!operation.isDone) yield return null;
        }

        void VideoError(VideoPlayer source, string message)
        {
            Debug.LogWarning("Loading animation could not play: " + message, this);
            ShowFallback();
        }
        void ShowFallback()
        {
            videoFailed = true;
            animationImage.enabled = false;
            if (video) video.Stop();
            if (fallback) return;
            var label = new GameObject("Loading fallback", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(transform, false);
            fallback = label.GetComponent<Text>();
            fallback.text = "Loading...";
            fallback.font = Ashfall.UI.AshfallTypography.Regular;
            fallback.fontSize = 24;
            fallback.color = Color.white;
            fallback.alignment = TextAnchor.MiddleCenter;
            fallback.raycastTarget = false;
            fallback.rectTransform.sizeDelta = new Vector2(480, 80);
        }
        void OnDestroy()
        {
            if (video)
            {
                video.errorReceived -= VideoError;
                video.Stop();
                video.targetTexture = null;
            }
            if (target) { target.Release(); Destroy(target); }
        }
    }
}
