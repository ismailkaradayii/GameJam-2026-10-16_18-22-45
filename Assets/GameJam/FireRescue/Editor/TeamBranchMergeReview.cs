using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Narrow binding repair and explicit review of the two team branches.</summary>
[InitializeOnLoad]
public static class TeamBranchMergeReview
{
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string Folder = "Logs/BranchMerge", Pending = "TeamBranchMergeReview.Pending";
    static readonly List<string> report = new List<string>(), errors = new List<string>();
    static int phase = -1, frame;
    static float phaseAt;
    static double deadline;
    static bool oldBackground, oldMaximized;
    static EditorWindow gameView;
    static VolcanoMissionDirector mission;
    static RescueHUD hud;

    static TeamBranchMergeReview()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending + ".FullLoop", false))
            {
                SessionState.SetBool(Pending + ".FullLoop", false);
                EditorApplication.delayCall += FinalLoopChecks.Begin;
            }
        };
    }

    [MenuItem("GameJam/Final Loop/Bind merged team scene")]
    public static void Bind()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before binding.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        hud = Object.FindFirstObjectByType<RescueHUD>();
        hud.missionDirector = mission; hud.flow = mission.gameFlow;
        hud.player = mission.player; hud.evacuation = mission.evacuation;
        hud.survivalObjective = Object.FindFirstObjectByType<VolcanoSurvivalObjective>();
        hud.vitals = mission.player.GetComponent<CreatureCombatPlayer>();
        hud.objectiveGroup = hud.questTitleText.transform.parent.GetComponent<CanvasGroup>();
        if (!hud.objectiveGroup) hud.objectiveGroup = hud.questTitleText.transform.parent.gameObject.AddComponent<CanvasGroup>();
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        nav.mission = mission; nav.canvas = hud.canvas;
        if (!nav.marker || !nav.label || !nav.arrow || !nav.ring || !hud.equipmentText)
            throw new InvalidOperationException("Merged navigation or equipment bindings are missing.");
        Ashfall.UI.AshfallTypography.Apply(hud.equipmentText, Ashfall.UI.AshfallTypography.Role.Label, 19);
        var timer = (RectTransform)hud.eruptionText.transform.parent;
        timer.anchorMin = timer.anchorMax = timer.pivot = new Vector2(.5f, 1f);
        timer.anchoredPosition = new Vector2(0, -26); timer.sizeDelta = new Vector2(660, 96);
        var frameSizer = timer.GetComponent<VolcanicRockFrameSizer>();
        frameSizer.authoredWidth = 660; frameSizer.minWidth = 640; frameSizer.minHeight = 82; frameSizer.maxWidthOfParent = .44f;
        frameSizer.widthFitTexts = Array.Empty<Text>();
        hud.eruptionText.fontSize = 26; hud.eruptionText.alignment = TextAnchor.MiddleCenter;
        hud.eruptionText.rectTransform.anchoredPosition = new Vector2(32, -20);
        hud.eruptionText.rectTransform.sizeDelta = new Vector2(596, 45);
        hud.eruptionText.text = "VOLCANO ERUPTION IN: 02:00";
        foreach (var changed in new Object[] { hud, nav, hud.eruptionText, timer, frameSizer, hud.equipmentText }) EditorUtility.SetDirty(changed);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);

        var menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        foreach (var label in Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (label.transform.parent && label.transform.parent.parent && label.transform.parent.parent.name.Contains("BİN / İN"))
            {
                if (label.name == "Description") label.text = "Sürücü kapısında E ile bin; durduktan sonra F ile in.";
                EditorUtility.SetDirty(label);
            }
        }
        EditorSceneManager.MarkSceneDirty(menu); EditorSceneManager.SaveScene(menu);
        EditorSceneManager.OpenScene(ScenePath);
        Validate();
    }

    [MenuItem("GameJam/Final Loop/Validate merged team scene")]
    public static void Validate()
    {
        report.Clear(); errors.Clear();
        FinalLoopSetup.Validate();
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); hud = Object.FindFirstObjectByType<RescueHUD>();
        Check("Exactly one authored HUD, game flow and objective navigator", Count<RescueHUD>() == 1 && Count<RescueGameFlow>() == 1 && Count<ObjectiveNavigation>() == 1);
        Check("Mission and new volcanic HUD share the same game flow", mission.gameFlow && mission.player.gameFlow == mission.gameFlow && hud.flow == mission.gameFlow && hud.mission == mission);
        Check("World ring and screen-edge marker are wired to the new Canvas", Object.FindFirstObjectByType<ObjectiveNavigation>().canvas == hud.canvas && Object.FindFirstObjectByType<ObjectiveNavigation>().marker);
        Check("Five houses and seven villagers retained", mission.gameFlow.targetBuildings.Length == 5 && mission.evacuation.TotalCount == 7);
        Check("Independent 120 second deadline retained", mission.preparationSeconds == 120 && !mission.sequence.autoErupt);
        Check("Player speed and automatic magazine reload retained", mission.player.firstPerson.walkSpeed >= 12 && mission.player.pistol.autoReloadWhenEmpty);
        Check("Fast arcade truck retained with unit Rigidbody scale", mission.player.drive.maxForwardSpeedKph >= 150 && mission.player.drive.transform.lossyScale == Vector3.one);
        Check("Only one configured water cannon is present", Count<FireTruckWaterCannon>() == 1 && mission.player.cannon.muzzle);
        Check("Water spray, crowd and lava ambience are assigned", Count<FireTruckSprayAudio>() == 1 && Clip(mission.evacuation, "crowdPanicLoop") &&
            Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None).All(f => Clip(f, "bubblingClip")));
        Check("Obsidian material and small spirit death sound retained", File.Exists(ObsidianReloadSetup.CrustPath) && Resources.Load<AudioClip>("LavaCombat/SpiritPoff"));
        Check("New menu typography is available", Ashfall.UI.AshfallTypography.Regular && Ashfall.UI.AshfallTypography.Semibold);
        report.Add(errors.Count == 0 && !report.Any(r => r.StartsWith("FAIL")) ? "COMPLETED" : "FAILED");
        File.WriteAllLines(Folder + "/validation.txt", report.Concat(errors));
        if (report.Last() == "FAILED") throw new InvalidOperationException("Merged scene validation failed.");
    }
    static int Count<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    static bool Clip(Object owner, string property) => new SerializedObject(owner).FindProperty(property)?.objectReferenceValue;

    [MenuItem("GameJam/Final Loop/Check merged team UI")]
    public static void Begin()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
        Validate(); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    [MenuItem("GameJam/Final Loop/Run merged team checks")]
    public static void BeginAll()
    {
        SessionState.SetBool(Pending + ".FullLoop", true);
        Begin();
    }
    static void Start()
    {
        report.Clear(); errors.Clear(); oldBackground = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += Log; deadline = EditorApplication.timeSinceStartup + 90;
        gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        oldMaximized = gameView.maximized; gameView.maximized = true; gameView.Focus();
        Next(0);
    }
    static void Tick()
    {
        if (phase < 0 || !EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Merged UI review phase " + phase);
            float age = Time.unscaledTime - phaseAt;
            mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); hud = Object.FindFirstObjectByType<RescueHUD>();
            if (!mission || !hud) return;
            if (phase == 0 && mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon && age > .3f)
            {
                Check("New quest card follows the first gameplay objective", hud.questTitleText.text == mission.ObjectiveTitle && hud.questBodyText.text == mission.ObjectiveDetail);
                Check("Timer shows preparation time independently of the old eruption sequence", hud.eruptionText.text.StartsWith("VOLCANO ERUPTION IN: 01:") || hud.eruptionText.text == "VOLCANO ERUPTION IN: 02:00");
                Check("Equipment stays contextual before pistol pickup", !hud.equipmentGroup.activeSelf);
                Check("Vehicle water warnings stay hidden on foot", !hud.lowWaterWarning.activeSelf);
                ScreenCapture.CaptureScreenshot(Folder + "/merged-preparation.png");
                Next(10);
            }
            else if (phase == 10 && age > .3f)
            {
                mission.gameFlow.Pause(); Next(1);
            }
            else if (phase == 1 && age > .4f)
            {
                Check("Pause page opens and locks gameplay", hud.PauseVisible && mission.gameFlow.BlocksGameplay && Time.timeScale == 0);
                ScreenCapture.CaptureScreenshot(Folder + "/merged-pause.png");
                Next(11);
            }
            else if (phase == 11 && age > .3f)
            {
                mission.gameFlow.Resume(); Next(2);
            }
            else if (phase == 2 && age > .4f)
            {
                Check("Resume restores player control and time", !hud.PauseVisible && !mission.gameFlow.BlocksGameplay && Time.timeScale == 1);
                // Only terminal UI is forced here. The full loop check covers natural mission transitions.
                mission.gameFlow.FinishMission(false); Next(3);
            }
            else if (phase == 3 && age > .4f)
            {
                Check("Defeat uses the new outcome page", hud.VictoryVisible && hud.victoryTitleText.text.Contains("FAILED") && hud.outcomeRestartButton.gameObject.activeSelf);
                Check("Outcome retains villagers, houses and fire statistics", hud.victoryStatsText.text.Contains("Saved Villagers") && hud.victoryStatsText.text.Contains("Houses Saved") && hud.victoryStatsText.text.Contains("Fires Extinguished"));
                ScreenCapture.CaptureScreenshot(Folder + "/merged-defeat.png");
                Next(13);
            }
            else if (phase == 13 && age > .3f)
            {
                Next(4); hud.outcomeRestartButton.onClick.Invoke();
            }
            else if (phase == 4 && mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon && age > 1)
            {
                Check("Restart returns to a fresh preparation", Time.timeScale == 1 && mission.PreparationRemaining > 115 && !mission.player.HasPistol);
                mission.gameFlow.FinishMission(true); Next(5);
            }
            else if (phase == 5 && age > .4f)
            {
                Check("Victory uses the same new outcome page", hud.VictoryVisible && hud.victoryTitleText.text.Contains("SUCCESS"));
                Check("No runtime errors", errors.Count == 0); Finish(null);
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Next(int value) { phase = value; phaseAt = Time.unscaledTime; }
    static void Check(string label, bool passed) => report.Add((passed ? "PASS " : "FAIL ") + label);
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + stack); }
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
        if (gameView) gameView.maximized = oldMaximized;
        if (error != null) errors.Add(error);
        report.Add(errors.Count == 0 && !report.Any(r => r.StartsWith("FAIL")) ? "COMPLETED" : "FAILED");
        if (report.Last() == "FAILED") SessionState.SetBool(Pending + ".FullLoop", false);
        File.WriteAllLines(Folder + "/ui-playmode.txt", report.Concat(errors));
        EditorApplication.ExitPlaymode();
    }
}
