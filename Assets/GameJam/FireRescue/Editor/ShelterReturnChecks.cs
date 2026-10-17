using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real-frame closing shot, existing win evaluator, actual shelter residents and delayed result Canvas.</summary>
[InitializeOnLoad]
public static class ShelterReturnChecks
{
    const string Pending = "ShelterReturnChecks.Pending";
    static readonly List<string> report = new List<string>();
    static VolcanoMissionDirector mission;
    static VolcanoSurvivalObjective survival;
    static RescueHUD hud;
    static ShelterReturnCinematic ending;
    static RescueGameFlow flow;
    static FireRescuePlayer player;
    static EditorWindow game;
    static ShelterReturnCaptureFixture photos;
    static int phase = -1, frame = -1, oldMission;
    static float phaseAt, previousShotAt, maximumStep, previousScale;
    static Vector3 entryPosition, previousShot;
    static Quaternion entryRotation;
    static bool oldBackground, oldMaximized, sawPreparation, sawWarning, visualOnly;
    static double deadline, nextReport;
    static string runtimeError;

    static ShelterReturnChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted."); };
    }
    [MenuItem("GameJam/Ending/Run closing sequence checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use saved Volcano_Demo in Edit Mode.");
        var authored = Object.FindFirstObjectByType<RescueGameFlow>();
        if (!authored || !authored.victoryCinematic) throw new InvalidOperationException("Bind the shelter return first.");
        SessionState.SetBool(Pending + ".Background", PlayerSettings.runInBackground);
        Application.runInBackground = true;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.isPaused = false; SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    [MenuItem("GameJam/Ending/Review closing visuals")]
    public static void ReviewVisuals() { SessionState.SetBool(Pending + ".VisualOnly", true); Run(); }
    static void Start()
    {
        report.Clear(); runtimeError = null; maximumStep = 0; sawPreparation = sawWarning = false;
        visualOnly = SessionState.GetBool(Pending + ".VisualOnly", false); SessionState.SetBool(Pending + ".VisualOnly", false);
        Bind(); oldMission = mission.GetInstanceID();
        oldBackground = SessionState.GetBool(Pending + ".Background", false); previousScale = Time.timeScale;
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); oldMaximized = game.maximized; game.Focus(); game.maximized = true;
        report.Add("Started UTC: " + DateTime.UtcNow.ToString("O"));
        report.Add(visualOnly ? "Visual review: full closing journey and success screen; restart/defeat are covered in full-checks.txt." : "Full closing, restart and defeat integration.");
        report.Add("Real-frame closing integration in the authored scene. Existing house-water APIs, finite-wave release/bullet-damage APIs and the real survival evaluator earn victory. NPC agents are arranged at their actual shelter slots; this is not a repeat of the complete manual battle/evacuation. The continuous camera journey is observed without scene loading or camera teleports.");
        Application.logMessageReceived += Log; Next(0, 45);
        photos = flow.gameObject.AddComponent<ShelterReturnCaptureFixture>();
    }
    static void Bind()
    {
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); player = mission.player; flow = mission.gameFlow;
        ending = flow.victoryCinematic; survival = Object.FindFirstObjectByType<VolcanoSurvivalObjective>(); hud = Object.FindFirstObjectByType<RescueHUD>();
    }
    static void Update()
    {
        if (phase < 0 && EditorApplication.isPlaying && !EditorApplication.isCompiling && SessionState.GetBool(Pending, false))
        { SessionState.SetBool(Pending, false); Start(); }
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException(Status());
            if (EditorApplication.timeSinceStartup > nextReport) { nextReport = EditorApplication.timeSinceStartup + 2; File.WriteAllText(ShelterReturnSetup.Folder + "/progress.txt", Status()); }
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.unscaledTime - phaseAt;
        switch (phase)
        {
            case 0:
                if (!mission.story.OpeningComplete) return;
                Check("existing opening finishes before gameplay and does not trigger a closing shot", !ending.IsPlaying && !flow.HasResult && !player.CinematicLocked);
                player.AcquirePistol(); mission.enabled = false; survival.enabled = false;
                mission.evacuation.BeginMission(); mission.evacuation.Shout();
                foreach (var resident in mission.evacuation.residents) resident.Agent.Warp(resident.safePlace.position);
                foreach (var house in flow.targetBuildings) { house.Ignite(); house.ApplyWater(house.litresToExtinguish); }
                Check("saving houses alone cannot start the cinematic while enemies remain", flow.targetBuildings.All(h => h.IsRescued) && !ending.IsPlaying && !flow.HasResult);
                mission.sequence.enabled = false; mission.sequence.BeginEruption(true); mission.cinematic.EndShot();
                mission.sequence.TickSequence(130); mission.cinematic.EndShot();
                // Let the existing encounter release its normal finite roster, without waiting between fixture waves.
                mission.encounter.enabled = false;
                player.PlaceOnFoot(mission.evacuation.shelterApproach.position, 235);
                Next(1, 40); return;
            case 1:
                foreach (var actor in mission.encounter.LivingCreatures.ToArray()) if (actor && !actor.IsDefeated)
                    actor.ApplyBulletDamage(1000, actor.transform.position, Vector3.forward);
                mission.encounter.TickEncounter(40);
                if (!mission.encounter.IsCleared || !mission.evacuation.IsComplete) return;
                Check("all finite waves are defeated, houses are cool and all actual residents are safe", mission.encounter.DefeatedCount == mission.encounter.PlannedCount &&
                    flow.targetBuildings.All(h => !h.IsBurning && !h.IsBurnedOut) && mission.evacuation.RescuedCount == 7);
                var weather = Object.FindFirstObjectByType<VolcanoWeatherTransition>();
                for (int i = 0; i < 110; i++) weather.TickWeather(.1f);
                player.PlaceOnFoot(new Vector3(-22, 1.05f, -545), 235);
                entryPosition = player.viewCamera.transform.position; entryRotation = player.viewCamera.transform.rotation;
                survival.enabled = true; survival.TickObjective();
                Check("the existing survival evaluator starts the return instead of an immediate result", flow.IsVictoryCinematic && ending.IsPlaying && !flow.HasResult && !survival.IsFinished && Time.timeScale == 1);
                Check("the first closing frame exactly preserves the live camera", Vector3.Distance(entryPosition, mission.cinematic.ShotCamera.transform.position) < .001f &&
                    Quaternion.Angle(entryRotation, mission.cinematic.ShotCamera.transform.rotation) < .01f);
                previousShot = entryPosition; previousShotAt = Time.unscaledTime; Next(2, 100); return;
            case 2:
                if (flow.HasResult) throw new InvalidOperationException("Result appeared before observing the sheltered survivors.");
                Check("closing holds gameplay and the result curtain while the world remains visible", player.CinematicLocked && player.GameplaySuspended && !hud.VictoryVisible &&
                    !Object.FindFirstObjectByType<AshfallSceneFade>().endingCurtain.gameObject.activeSelf);
                Vector3 current = mission.cinematic.ShotCamera.transform.position;
                float dt = Time.unscaledTime - previousShotAt;
                if (dt > 0 && ending.Elapsed > .15f) maximumStep = Mathf.Max(maximumStep, Vector3.Distance(current, previousShot) / dt);
                previousShot = current; previousShotAt = Time.unscaledTime;
                sawPreparation |= ending.CurrentLine.Contains("tahliye planımızı"); sawWarning |= ending.CurrentLine.Contains("uyarı");
                if (ending.Elapsed > 12 && !File.Exists(ShelterReturnSetup.Folder + "/02-returning.png")) Capture("02-returning.png");
                if (!ending.AtShelter || ending.ObservedSeconds < 2f) return;
                Check("continuous camera route arrives inside the real shelter", mission.evacuation.IsInsideShelter(player.transform.position) && ending.DistanceTravelled > 70 && maximumStep < 12f, "peak camera speed=" + maximumStep.ToString("F2"));
                Check("prevention and awareness thoughts played on the way", sawPreparation && sawWarning);
                if (!photos.Ready("03-sheltered-survivors.png")) return;
                Check("the cinematic lens focuses on the survivors instead of the volcano", Vector3.Distance(mission.cinematic.FocusPosition, ending.shelterFocus.position) < 10f);
                Check("the closing camera actually sees the real sheltered people", ending.VisibleSurvivors >= 5, "visible=" + ending.VisibleSurvivors);
                Next(3, 20); return;
            case 3:
                if (!flow.HasResult) return;
                Check("victory begins only after the survivor observation", flow.State == RescueGameFlow.GameState.Won && ending.HasCompleted && ending.ObservedSeconds >= ending.observeSeconds);
                Check("the final camera does not jump back to the battle", !mission.cinematic.IsPlaying && !VolcanoCinematic.IsPlayingAny && player.viewCamera.enabled &&
                    Vector3.Distance(player.viewCamera.transform.position, ending.FinalPosition) < .001f && Quaternion.Angle(player.viewCamera.transform.rotation, ending.FinalRotation) < .01f && Time.timeScale == 0);
                Next(4, 15); return;
            case 4:
                if (!flow.ResultReady || !hud.VictoryVisible) return;
                Check("existing success Canvas, cursor and stats appear after the final shot", Cursor.visible && Cursor.lockState == CursorLockMode.None && mission.evacuation.RescuedCount == 7 && flow.RescuedCount == 5);
                if (!photos.Ready("04-success.png")) return;
                if (visualOnly) { Finish(null); return; }
                player.gameFlow.RestartMission(); Next(5, 50); return;
            case 5:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.GetInstanceID() == oldMission || !fresh.story.OpeningComplete) return;
                Bind(); oldMission = mission.GetInstanceID();
                Check("restart clears the ending and restores the opening", !ending.HasCompleted && !ending.IsPlaying && flow.State == RescueGameFlow.GameState.Playing && Time.timeScale == 1);
                mission.enabled = false;
                player.PlaceOnFoot(mission.evacuation.shelterApproach.position + Vector3.right * 8, 240);
                player.drive.body.position = player.transform.position; player.drive.body.rotation = Quaternion.identity;
                player.SetDriving(true); Next(6, 10); return;
            case 6:
                if (age < .6f) return;
                entryPosition = player.viewCamera.transform.position; entryRotation = player.viewCamera.transform.rotation;
                flow.FinishMission(true);
                Check("winning from the truck also starts without a cut", ending.IsPlaying && !player.IsDriving && Vector3.Distance(entryPosition, mission.cinematic.ShotCamera.transform.position) < .001f &&
                    Quaternion.Angle(entryRotation, mission.cinematic.ShotCamera.transform.rotation) < .01f);
                Next(7, 12); return;
            case 7:
                if (age < 4) return;
                Check("close-to-shelter victory still waits for the closing thoughts", !flow.HasResult && ending.IsPlaying && ending.Elapsed >= 3);
                flow.RestartMission(); Next(8, 35); return;
            case 8:
                var restarted = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!restarted || restarted.GetInstanceID() == oldMission || !restarted.story.IsOpening) return;
                Bind();
                Check("restart during the closing shot releases its camera, time and input lease", Time.timeScale == 1 && !ending.IsPlaying && !ending.HasCompleted && mission.cinematic.Kind != VolcanoCinematic.ShotKind.ShelterReturn);
                flow.FinishMission(false);
                Check("defeat keeps its existing direct result and does not play a success cinematic", flow.State == RescueGameFlow.GameState.Lost && !ending.IsPlaying && Time.timeScale == 0);
                Finish(null); return;
        }
    }
    static string Status() => "phase=" + phase + " flow=" + (flow ? flow.State.ToString() : "none") + " ending=" + (ending ? ending.Elapsed + " distance=" + ending.DistanceTravelled + "/" + ending.RouteLength + " arrived=" + ending.AtShelter + " visible=" + ending.VisibleSurvivors + " observed=" + ending.ObservedSeconds + " line=" + ending.CurrentLine : "none") +
        " rescued=" + (mission ? mission.evacuation.RescuedCount : -1) + " enemies=" + (mission ? mission.encounter.DefeatedCount + "/" + mission.encounter.PlannedCount : "none");
    static void Check(string label, bool passed, string detail = "")
    {
        if (!report.Any(r => r.StartsWith("PASS " + label))) report.Add((passed ? "PASS " : "FAIL ") + label + " " + detail);
        File.WriteAllLines(ShelterReturnSetup.Folder + "/checks.txt", report); if (!passed) throw new InvalidOperationException(label + " " + detail);
    }
    static void Next(int value, double seconds) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + seconds; }
    static void Capture(string name) => ScreenCapture.CaptureScreenshot(ShelterReturnSetup.Folder + "/" + name);
    static void Log(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = condition + "\n" + stack; }
    static void Finish(string failure)
    {
        if (phase < 0) return;
        phase = -1; Application.logMessageReceived -= Log;
        report.Add(failure == null ? "COMPLETED" : "FAILED " + failure); File.WriteAllLines(ShelterReturnSetup.Folder + "/checks.txt", report);
        File.WriteAllLines(ShelterReturnSetup.Folder + (visualOnly ? "/visual-checks.txt" : "/full-checks.txt"), report);
        if (game) game.maximized = oldMaximized;
        Application.runInBackground = oldBackground; PlayerSettings.runInBackground = oldBackground; Time.timeScale = previousScale;
        EditorApplication.ExitPlaymode();
    }
}

public sealed class ShelterReturnCaptureFixture : MonoBehaviour
{
    string requested, completed;
    public bool Ready(string name) { if (completed == name) return true; requested = name; return false; }
    IEnumerator Start()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            if (requested == null) continue;
            var pixels = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(ShelterReturnSetup.Folder + "/" + requested, pixels.EncodeToPNG()); Destroy(pixels);
            completed = requested; requested = null;
        }
    }
}
