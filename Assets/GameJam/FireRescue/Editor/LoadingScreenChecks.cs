using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashfall.MainMenu;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

/// <summary>Exercises the actual menu button, video decoding and scene activation without editing either scene.</summary>
[InitializeOnLoad]
public static class LoadingScreenChecks
{
    public const string Folder = "Logs/LoadingScreen";
    const string Pending = "LoadingScreenChecks.Pending";
    const string Restore = "LoadingScreenChecks.Restore";
    static double restoreAfter;

    [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] public sealed class Report
    {
        public string status = "RUNNING", startedUtc, finishedUtc, restoredEditScene;
        public string method = "Actual menu button, duplicate requests, native video replay across the clip boundary, decoded pixels, covered gameplay camera renders, frozen opening timing, real asynchronous scene activation, and isolated missing-clip/decoder-error fallbacks. Video seeking only accelerates the loop check; authored assets and player preferences are unchanged.";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public List<string> videoSamples = new List<string>();
        public float elapsedSeconds, loadingVisibleSeconds;
    }

    static LoadingScreenChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += () => new GameObject("Loading screen integration check").AddComponent<LoadingScreenProbe>().Begin();
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Restore, false))
                restoreAfter = EditorApplication.timeSinceStartup + .5;
        };
        // Poll outside the Play Mode callback: a one-shot delayCall can run while scene restoration is still in progress.
        EditorApplication.update += RestoreSavedScene;
    }

    [MenuItem("GameJam/Final Loop/Check loading screen")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Use saved, idle Edit Mode before checking the menu transition.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.json", "{\"status\":\"STARTING\",\"startedUtc\":\"" + DateTime.UtcNow.ToString("O") + "\"}");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(Pending, true);
        SessionState.SetBool(Restore, true);
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.EnterPlaymode();
    }

    static void RestoreSavedScene()
    {
        if (!SessionState.GetBool(Restore, false) || SessionState.GetBool(Pending, false) ||
            EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.timeSinceStartup < restoreAfter) return;
        try
        {
            EditorSceneManager.OpenScene(FinalLoopSetup.ScenePath);
            SessionState.SetBool(Restore, false);
            var path = Folder + "/checks.json";
            var report = File.Exists(path) ? JsonUtility.FromJson<Report>(File.ReadAllText(path)) : new Report();
            report.restoredEditScene = SceneManager.GetActiveScene().path;
            Save(report);
        }
        catch (Exception ex)
        {
            SessionState.SetBool(Restore, false);
            File.WriteAllText(Folder + "/restore-error.txt", ex.ToString());
        }
    }

    public static void Save(Report report)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true));
    }
}

public sealed class LoadingScreenProbe : MonoBehaviour
{
    readonly LoadingScreenChecks.Report report = new LoadingScreenChecks.Report();
    float began, previousTimeScale, firstVisibleAt = -1, activatedAt = -1;
    bool previousBackground, finished;
    double deadline;
    GameObject loading;
    VideoPlayer video;
    RawImage animation;
    VideoClip authoredClip;
    long firstFrame = -1;
    double nextSample;
    AshfallLoadingScreen screen;
    VolcanoMissionDirector mission;
    RenderTexture decodedTarget;
    int nativeLoops, coveredCameraFrames, loadingMissionFrames, lastCoveredFrame = -1;
    bool activationCovered, activationAnimationVisible, activationLoading, loopReturnedToStart, sceneReadySeen;
    float maximumHiddenIntroElapsed, minimumHiddenPreparation = float.MaxValue;

    public void Begin()
    {
        Object.DontDestroyOnLoad(gameObject);
        began = Time.realtimeSinceStartup;
        report.startedUtc = DateTime.UtcNow.ToString("O");
        previousBackground = Application.runInBackground;
        previousTimeScale = Time.timeScale;
        Application.runInBackground = true;
        deadline = EditorApplication.timeSinceStartup + 120;
        EditorApplication.update += Watch;
        EditorApplication.playModeStateChanged += PlayState;
        Application.logMessageReceived += Log;
        SceneManager.activeSceneChanged += SceneChanged;
        RenderPipelineManager.endCameraRendering += CameraRendered;
        StartCoroutine(Guarded(Checks()));
    }

    IEnumerator Checks()
    {
        var menu = Object.FindFirstObjectByType<AshfallMenuController>();
        Require(menu, "Main menu controller is required.");
        var serialized = new SerializedObject(menu);
        var clipProperty = serialized.FindProperty("loadingScreenClip");
        authoredClip = clipProperty?.objectReferenceValue as VideoClip;
        Check("The downloaded loading clip is assigned to the authored menu", authoredClip &&
            AssetDatabase.GetAssetPath(authoredClip).EndsWith("LoadingScreen.mp4", StringComparison.OrdinalIgnoreCase),
            authoredClip ? AssetDatabase.GetAssetPath(authoredClip) : "missing clip");
        Check("The gameplay scene remains enabled in Build Settings", EditorBuildSettings.scenes.Any(s =>
            s.enabled && Path.GetFileNameWithoutExtension(s.path) == menu.GameplaySceneName), menu.GameplaySceneName);
        var buttons = serialized.FindProperty("menuButtons");
        var playButton = buttons != null && buttons.arraySize > 0 ? buttons.GetArrayElementAtIndex(0).objectReferenceValue as Button : null;
        Require(playButton, "The actual New game button is required.");
        while (!playButton.interactable) yield return null;
        yield return new WaitForSecondsRealtime(.4f);
        playButton.onClick.Invoke();
        menu.NewGame();
        menu.NewGame();
        yield return null;
        loading = GameObject.Find("Loading screen");
        Require(loading, "The button must create the loading screen.");
        screen = loading.GetComponent<AshfallLoadingScreen>();
        var screens = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.name == "Loading screen").ToArray();
        Check("Repeated NewGame requests create a single loading screen", screens.Length == 1, "canvases=" + screens.Length);
        var canvas = loading.GetComponent<Canvas>();
        Check("The loading canvas covers menu UI in screen space", canvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay &&
            canvas.sortingOrder >= 30000, canvas ? "order=" + canvas.sortingOrder : "missing canvas");
        var black = loading.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.color.r < .001f && i.color.g < .001f && i.color.b < .001f && i.color.a > .999f);
        Check("The background is opaque black and blocks accidental menu clicks", black && black.raycastTarget &&
            CoversScreen(black.rectTransform), black ? black.rectTransform.rect.ToString() : "missing black background");
        animation = loading.GetComponentsInChildren<RawImage>(true).SingleOrDefault(i => i.name == "Loading animation");
        video = loading.GetComponentInChildren<VideoPlayer>(true);
        Require(animation && video, "The loading animation and VideoPlayer are required.");
        video.loopPointReached += VideoLooped;
        Check("The downloaded video loops without audio", video.clip == authoredClip && video.isLooping &&
            video.audioOutputMode == VideoAudioOutputMode.None, "clip=" + video.clip?.name + "; audio=" + video.audioOutputMode);
        Check("The animation is centered and fits inside the display", IsCentered(animation.rectTransform), animation.rectTransform.rect.ToString());
        while (loading && (!video.isPrepared || !video.isPlaying || video.frame < 0 || !animation.enabled || !video.targetTexture || !video.targetTexture.IsCreated()))
        {
            yield return null;
        }
        Require(loading && video && video.frame >= 0, "A decoded video frame must appear before gameplay activation.");
        firstVisibleAt = Time.realtimeSinceStartup;
        firstFrame = video.frame;
        decodedTarget = video.targetTexture;
        Check("A real decoded video texture is displayed", animation.enabled && animation.texture == video.targetTexture &&
            video.targetTexture.IsCreated() && video.isPlaying, "frame=" + video.frame + "; texture=" + video.targetTexture.width + "x" + video.targetTexture.height);
        yield return new WaitForSecondsRealtime(.2f);
        Require(loading && video && animation, "The loading animation must remain visible long enough to read.");
        Check("Gameplay activation remains blocked while the loading animation is visible", SceneManager.GetActiveScene().name == "MainMenu" &&
            animation.enabled && animation.color.a > .95f && loading.GetComponent<CanvasGroup>().alpha > .99f,
            "scene=" + SceneManager.GetActiveScene().name + "; imageAlpha=" + animation.color.a + "; curtain=" + loading.GetComponent<CanvasGroup>().alpha);
        Check("The loading video advances while the gameplay scene is being prepared", video.frame != firstFrame,
            "frames=" + firstFrame + " -> " + video.frame);
        Check("Decoded video pixels contain the visible loading symbol", ContainsVisiblePixels(video.targetTexture));
        Check("The complete downloaded loading animation is ten seconds long", Math.Abs(video.length - 10) < .1,
            "clip seconds=" + video.length.ToString("F3"));
        // Seek near the actual native clip boundary so fast machines also exercise replay.
        // Playback speed stays at the authored 1x and no production readiness condition is delayed.
        video.time = Math.Max(0, video.length - .35);
        yield return new WaitForEndOfFrame();
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        Require(pixels, "The loading screenshot must be captured.");
        File.WriteAllBytes(LoadingScreenChecks.Folder + "/loading-screen.png", pixels.EncodeToPNG());
        Destroy(pixels);
        while (loading && (nativeLoops == 0 || !loopReturnedToStart)) yield return null;
        Check("The native video crosses its final frame and continues another loop", nativeLoops > 0 && loopReturnedToStart,
            "loop events=" + nativeLoops + "; decoded restart=" + loopReturnedToStart);
        while (SceneManager.GetActiveScene().name != "Volcano_Demo") yield return null;
        while (loading) yield return null;
        yield return null;
        yield return null;
        report.loadingVisibleSeconds = Time.realtimeSinceStartup - firstVisibleAt;
        Check("Scene activation preserves the opaque animated loading screen", activatedAt > 0 && activationCovered &&
            activationAnimationVisible && activationLoading,
            "covered=" + activationCovered + "; animation=" + activationAnimationVisible + "; loading=" + activationLoading);
        Check("Gameplay renders behind the loading screen before it is ready to reveal", sceneReadySeen && coveredCameraFrames >= 2,
            "covered gameplay camera frames=" + coveredCameraFrames + "; scene ready=" + sceneReadySeen);
        Check("The opening and mission deadline do not advance behind the loading screen", loadingMissionFrames >= 2 &&
            maximumHiddenIntroElapsed < .001f && mission && minimumHiddenPreparation >= mission.preparationSeconds - .001f,
            "sampled frames=" + loadingMissionFrames + "; hidden intro seconds=" + maximumHiddenIntroElapsed.ToString("F4") +
            "; minimum preparation seconds=" + minimumHiddenPreparation.ToString("F3"));
        Check("The loading screen, decoder and texture are released after readiness", !loading && !video && !decodedTarget &&
            !AshfallLoadingScreen.IsLoading && !GameObject.Find("Loading screen") && !Object.FindFirstObjectByType<AshfallMenuController>());
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        Check("The real authored mission starts with its player and wake-up cinematic intact", mission && mission.player &&
            mission.cinematic && mission.cinematic.HasIntroPlayed && (mission.cinematic.IsPlaying || mission.story && mission.story.OpeningStarted),
            mission ? "phase=" + mission.Phase + "; intro=" + mission.cinematic?.HasIntroPlayed : "missing mission");
        Check("Loading does not advance the eruption or end the mission", mission && !mission.EruptionTriggered &&
            mission.sequence.Stage == VolcanoEruptionSequence.EruptionStage.Waiting && !mission.gameFlow.HasResult &&
            mission.PreparationRemaining > mission.preparationSeconds - 3,
            mission ? "remaining=" + mission.PreparationRemaining + "; eruption=" + mission.sequence.Stage : "missing mission");
        yield return new WaitForEndOfFrame();
        var reveal = ScreenCapture.CaptureScreenshotAsTexture();
        Require(reveal, "The first revealed gameplay screenshot must be captured.");
        File.WriteAllBytes(LoadingScreenChecks.Folder + "/gameplay-reveal.png", reveal.EncodeToPNG());
        Check("The first revealed gameplay image contains the rendered world", reveal.GetPixels32().Count(p =>
            p.r > 45 || p.g > 45 || p.b > 45) > reveal.width * reveal.height / 25);
        Destroy(reveal);
        yield return Guarded(FallbackChecks());
        if (finished) yield break;
        Finish(null);
    }

    IEnumerator FallbackChecks()
    {
        var missing = CreateFallbackFixture(null);
        var label = missing.GetComponentsInChildren<Text>(true).SingleOrDefault(t => t.name == "Loading fallback");
        Check("A missing video keeps a visible loading status instead of an empty curtain", label && label.gameObject.activeSelf &&
            !string.IsNullOrWhiteSpace(label.text) && label.color.a > .99f && !missing.GetComponentInChildren<VideoPlayer>(true));
        Destroy(missing);
        yield return null;
        yield return null;
        Check("The missing-clip fallback releases its canvas", !missing);

        var failed = CreateFallbackFixture(authoredClip);
        var decoder = failed.GetComponentInChildren<VideoPlayer>(true);
        var failedTarget = decoder.targetTexture;
        typeof(AshfallLoadingScreen).GetMethod("VideoError", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(failed.GetComponent<AshfallLoadingScreen>(), new object[] { decoder, "Integration check: simulated decode failure" });
        label = failed.GetComponentsInChildren<Text>(true).SingleOrDefault(t => t.name == "Loading fallback");
        var failedAnimation = failed.GetComponentsInChildren<RawImage>(true).Single(i => i.name == "Loading animation");
        Check("A decoder error replaces the video with a visible loading status", label && label.gameObject.activeSelf &&
            label.color.a > .99f && !failedAnimation.enabled && !decoder.isPlaying);
        Destroy(failed);
        yield return null;
        yield return null;
        Check("The decoder-error fallback releases its canvas and render texture", !failed && !decoder && !failedTarget);
    }

    static GameObject CreateFallbackFixture(VideoClip clip)
    {
        var root = new GameObject("Loading fallback check", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(AshfallLoadingScreen));
        typeof(AshfallLoadingScreen).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(root.GetComponent<AshfallLoadingScreen>(), new object[] { clip });
        root.GetComponent<CanvasGroup>().alpha = 1;
        return root;
    }

    static bool CoversScreen(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        return corners[0].x <= 1 && corners[0].y <= 1 && corners[2].x >= Screen.width - 1 && corners[2].y >= Screen.height - 1;
    }

    static bool IsCentered(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Vector3 center = (corners[0] + corners[2]) * .5f;
        return Mathf.Abs(center.x - Screen.width * .5f) < 2 && Mathf.Abs(center.y - Screen.height * .5f) < 2 &&
            corners[0].x >= -1 && corners[0].y >= -1 && corners[2].x <= Screen.width + 1 && corners[2].y <= Screen.height + 1;
    }

    static bool ContainsVisiblePixels(RenderTexture target)
    {
        var previous = RenderTexture.active;
        var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        try
        {
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            pixels.Apply();
            return pixels.GetPixels32().Count(p => p.r > 45 || p.g > 45 || p.b > 45) > 20;
        }
        finally { RenderTexture.active = previous; Object.Destroy(pixels); }
    }

    IEnumerator Guarded(IEnumerator routine)
    {
        while (!finished)
        {
            object current;
            try { if (!routine.MoveNext()) yield break; current = routine.Current; }
            catch (Exception ex) { Finish(ex.ToString()); yield break; }
            yield return current;
        }
    }

    void VideoLooped(VideoPlayer source) => nativeLoops++;
    void SceneChanged(Scene previous, Scene next)
    {
        if (next.name != "Volcano_Demo") return;
        activatedAt = Time.realtimeSinceStartup;
        activationCovered = loading && loading.GetComponent<CanvasGroup>().alpha > .99f;
        activationAnimationVisible = animation && animation.enabled && animation.color.a > .99f && video && video.isPlaying;
        activationLoading = AshfallLoadingScreen.IsLoading;
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
    }
    void CameraRendered(ScriptableRenderContext context, Camera camera)
    {
        if (!loading || !AshfallLoadingScreen.IsLoading || loading.GetComponent<CanvasGroup>().alpha < .99f ||
            lastCoveredFrame == Time.frameCount) return;
        if (!mission) mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (mission && mission.player && (camera == mission.player.viewCamera || mission.cinematic && camera == mission.cinematic.ShotCamera))
        {
            lastCoveredFrame = Time.frameCount;
            coveredCameraFrames++;
        }
    }
    void LateUpdate()
    {
        if (finished) return;
        if (video && nativeLoops > 0 && video.frame >= 0 && video.time < video.length - .1) loopReturnedToStart = true;
        if (screen && screen.SceneReady) sceneReadySeen = true;
        if (!loading || !AshfallLoadingScreen.IsLoading || !mission) return;
        loadingMissionFrames++;
        if (mission.cinematic) maximumHiddenIntroElapsed = Mathf.Max(maximumHiddenIntroElapsed, mission.cinematic.Elapsed);
        minimumHiddenPreparation = Mathf.Min(minimumHiddenPreparation, mission.PreparationRemaining);
    }
    void Watch()
    {
        if (video && EditorApplication.timeSinceStartup >= nextSample)
        {
            nextSample = EditorApplication.timeSinceStartup + 1;
            report.videoSamples.Add("frame=" + video.frame + "; prepared=" + video.isPrepared + "; playing=" + video.isPlaying +
                "; paused=" + video.isPaused + "; enabled=" + video.enabled + "; active=" + video.gameObject.activeInHierarchy +
                "; time=" + video.time + "; speed=" + video.playbackSpeed + "; clock=" + video.clockTime +
                "; mode=" + video.timeUpdateMode + "; frames=" + video.frameCount + "; length=" + video.length +
                "; reference=" + video.timeReference + "; scale=" + Time.timeScale + "; focus=" + Application.isFocused);
            Save();
        }
        if (!finished && EditorApplication.timeSinceStartup > deadline) Finish("Loading checks exceeded the 120-second wall-clock deadline.");
    }
    void PlayState(PlayModeStateChange state) { if (!finished && state == PlayModeStateChange.ExitingPlayMode) Finish("Play Mode was interrupted.", false); }
    void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        report.errors.Add(message); Save();
    }
    void Check(string name, bool passed, string evidence = "")
    {
        report.checks.Add(new LoadingScreenChecks.Check { name = name, passed = passed, evidence = evidence });
        Save(); Require(passed, name + ": " + evidence);
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    void Save() { report.elapsedSeconds = Time.realtimeSinceStartup - began; LoadingScreenChecks.Save(report); }
    void Finish(string error, bool exitPlay = true)
    {
        if (finished) return;
        finished = true;
        EditorApplication.update -= Watch;
        EditorApplication.playModeStateChanged -= PlayState;
        Application.logMessageReceived -= Log;
        SceneManager.activeSceneChanged -= SceneChanged;
        RenderPipelineManager.endCameraRendering -= CameraRendered;
        if (video) video.loopPointReached -= VideoLooped;
        if (!string.IsNullOrEmpty(error)) report.errors.Add(error);
        report.status = report.errors.Count == 0 ? "COMPLETED" : "FAILED";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        Application.runInBackground = previousBackground;
        Time.timeScale = previousTimeScale;
        Save();
        if (exitPlay && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
