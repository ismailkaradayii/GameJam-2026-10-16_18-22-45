using System.Collections;
using GameJam.FireRescue;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace Ashfall.MainMenu
{
    /// <summary>Keeps the loading animation visible through scene activation and the first rendered gameplay frames.</summary>
    [DisallowMultipleComponent]
    public sealed class AshfallLoadingScreen : MonoBehaviour
    {
        const float MinimumVisibleSeconds = 1.5f;
        static AshfallLoadingScreen active;
        public static bool IsLoading => active;
        public bool SceneReady { get; private set; }
        CanvasGroup curtain;
        RawImage animationImage;
        VideoPlayer video;
        RenderTexture target;
        Text fallback;
        bool videoFailed;
        Scene departingScene;
        Camera gameplayCamera;
        int renderedFrames, lastRenderedFrame = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() => active = null;

        public static IEnumerator Load(string sceneName, VideoClip clip)
        {
            if (active) yield break;
            var root = new GameObject("Loading screen", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AshfallLoadingScreen));
            var screen = root.GetComponent<AshfallLoadingScreen>();
            active = screen;
            screen.departingScene = root.scene;
            DontDestroyOnLoad(root);
            screen.Build(clip);
            // The departing menu's coroutine is destroyed at activation; this one must outlive it.
            yield return screen.StartCoroutine(screen.LoadScene(sceneName));
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
            ShowStatus();
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
                    if (sources[i] && sources[i].gameObject.scene == departingScene)
                        sources[i].volume = volumes[i] * (1 - curtain.alpha);
                yield return null;
            }
            curtain.alpha = 1;
            foreach (var background in FindObjectsByType<AshfallVideoBackgroundController>(FindObjectsSortMode.None))
                if (background.gameObject.scene == departingScene) background.enabled = false;

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
            else if (!videoFailed)
            {
                animationImage.enabled = true;
                if (fallback) fallback.gameObject.SetActive(false);
            }
            // Give the animation real rendered frames before potentially expensive deserialization.
            // A long loading frame must not consume the entire minimum display duration.
            float visibleSeconds = 0;
            while (visibleSeconds < .5f)
            {
                visibleSeconds += Mathf.Min(Time.unscaledDeltaTime, .05f);
                yield return null;
            }
            AsyncOperation operation = null;
            try { operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single); }
            catch (System.Exception exception) { Debug.LogException(exception, this); }
            if (operation == null)
            {
                Debug.LogError("Could not load " + sceneName, this);
                Destroy(gameObject);
                yield break;
            }
            operation.allowSceneActivation = false;
            while (operation.progress < .9f || visibleSeconds < MinimumVisibleSeconds)
            {
                visibleSeconds += Mathf.Min(Time.unscaledDeltaTime, .05f);
                yield return null;
            }
            // 90% means assets are loaded, not that Awake/Start or camera rendering has finished.
            // Keep both the video and its opaque background alive across that remaining work.
            RenderPipelineManager.endCameraRendering += CameraRendered;
            Camera.onPostRender += CameraRendered;
            operation.allowSceneActivation = true;
            while (!operation.isDone) yield return null;
            yield return null;
            var mission = FindFirstObjectByType<VolcanoMissionDirector>();
            var sceneFade = FindFirstObjectByType<AshfallSceneFade>();
            while (true)
            {
                bool initialized = !mission || (mission.sequence && mission.sequence.Initialized);
                var readyCamera = mission && mission.cinematic && mission.cinematic.IsPlaying ? mission.cinematic.ShotCamera :
                    mission && mission.player ? mission.player.viewCamera : Camera.main;
                if (gameplayCamera != readyCamera || !initialized)
                { gameplayCamera = readyCamera; renderedFrames = 0; lastRenderedFrame = -1; }
                bool visibleCamera = gameplayCamera && gameplayCamera.isActiveAndEnabled && gameplayCamera.targetTexture == null;
                bool openingFadeFinished = !sceneFade || !sceneFade.curtain || sceneFade.curtain.alpha <= .001f;
                if (initialized && visibleCamera && renderedFrames >= 2 && openingFadeFinished) break;
                yield return null;
            }
            SceneReady = true;
            elapsed = 0;
            while (elapsed < .25f)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
                curtain.alpha = 1 - Mathf.SmoothStep(0, 1, elapsed / .25f);
                yield return null;
            }
            curtain.alpha = 0;
            active = null;
            Destroy(gameObject);
        }

        void CameraRendered(ScriptableRenderContext context, Camera camera) => CameraRendered(camera);
        void CameraRendered(Camera camera)
        {
            if (camera != gameplayCamera || lastRenderedFrame == Time.frameCount) return;
            lastRenderedFrame = Time.frameCount;
            renderedFrames++;
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
            ShowStatus();
        }
        void ShowStatus()
        {
            if (fallback) { fallback.gameObject.SetActive(true); return; }
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
            if (active == this) active = null;
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            Camera.onPostRender -= CameraRendered;
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
