using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.MainMenu;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks the shared scene and the runtime boundaries touched by team branch merges.</summary>
[InitializeOnLoad]
public static class TeamBranchMergeChecks
{
    const string Active = "GameJam.TeamMerge.Active", Phase = "GameJam.TeamMerge.Phase";
    const string ReportPath = "Logs/TeamBranchMergeChecks.json";
    const string GamePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report
    {
        public string status = "running", error;
        public List<Check> checks = new List<Check>();
        public List<string> runtimeErrors = new List<string>();
    }
    [Serializable] class CombatReport { public bool allPassed; public string error; public Check[] checks; }
    [Serializable] class DoorReport { public string status, error; public Check[] checks; }
    static Report report;
    static double readyAt;
    static TeamBranchMergeChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (!SessionState.GetBool(Active, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { readyAt = EditorApplication.timeSinceStartup + 3; SessionState.SetString(Phase, "test"); }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(Phase, "") == "done")
            {
                SessionState.SetBool(Active, false);
                if (Application.isBatchMode) EditorApplication.Exit(Read().status == "passed" ? 0 : 1);
            }
        };
        EditorApplication.update += Update;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (SessionState.GetBool(Active, false) && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            { Read().runtimeErrors.Add(message); Save(); }
        };
    }
    [MenuItem("GameJam/Integration/Check merged team systems")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes first.");
        Directory.CreateDirectory("Logs"); report = new Report();
        try
        {
            // Help must describe both the new NPC preparation and physical water/rescue flow.
            AshfallSettingsSceneBuilder.RefreshGameplayHelp();
            var menu = Object.FindFirstObjectByType<AshfallMenuController>();
            CheckThat("menu_loads_volcano", new SerializedObject(menu).FindProperty("gameplaySceneName").stringValue == "Volcano_Demo", "New Game scene remains Volcano_Demo.");
            CheckThat("menu_click_audio", Object.FindFirstObjectByType<AshfallMenuAudioFeedback>(), "Eylul menu feedback retained.");
            EditorSceneManager.OpenScene(GamePath);
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CheckThat("scene_has_no_missing_scripts", all.Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "All saved scene MonoBehaviour references resolve.");
            CheckThat("one_town_dressing_root", all.Count(t => !t.parent && t.name == "Town Dressing") == 1, "Ismail's town and first-house details retained.");
            var flow = Object.FindFirstObjectByType<RescueGameFlow>();
            CheckThat("five_distinct_saved_house_targets", flow && flow.targetBuildings.Length == 5 && flow.targetBuildings.Distinct().Count() == 5 && flow.targetBuildings.All(b => b), "Explicit rescue references retained.");
            var savedDoors = Object.FindObjectsByType<InteractiveDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            CheckThat("interactive_opendoor", savedDoors.Any(d => d.name == "opendoor" && d.leafCollider && d.gameObject.activeInHierarchy),
                "Interactive doors: " + string.Join("; ", savedDoors.Select(d => d.name + " active=" + d.gameObject.activeInHierarchy + " leaf=" + (d.leafCollider ? d.leafCollider.name : "none"))) +
                "; named leaves: " + string.Join("; ", all.Where(t => t.name == "opendoor" || t.name == "Door04_pr").Select(t => t.name + " in " + (t.GetComponentInParent<BurnableBuilding>() ? t.GetComponentInParent<BurnableBuilding>().name : "none"))));
            var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
            CheckThat("npc_mission_and_cinematic_references", sequence && sequence.evacuation && sequence.evacuation.TotalCount == 7 && sequence.missionDirector && sequence.cinematic, "Osman's seven residents, mission and eruption camera retained.");
            CheckThat("lava_cooling_surfaces_retained", Object.FindObjectsByType<LavaCoolingSurface>(FindObjectsSortMode.None).Length > 0, "Physical water has cooling targets.");
            CheckThat("fire_and_wind_audio_retained", Object.FindFirstObjectByType<FireAudioAmbience>() && Object.FindFirstObjectByType<WindAudioPulse>(), "Eylul gameplay ambience retained.");
            var truck = Object.FindFirstObjectByType<FireTruckDrive>();
            CheckThat("truck_engine_audio_retained", truck && truck.GetComponentInChildren<FireTruckEngineAudio>(true), "Enlarged truck keeps its engine component.");
            CheckThat("ballistic_cannon_retained", Object.FindFirstObjectByType<FireTruckWaterCannon>().muzzleSpeed == 35f, "35 m/s physical packet stream retained.");
            Save(); SessionState.SetBool(Active, true); SessionState.SetString(Phase, "start");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Update()
    {
        if (!SessionState.GetBool(Active, false) || SessionState.GetString(Phase, "") != "test" || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < readyAt) return;
        SessionState.SetString(Phase, "running-tests");
        try
        {
            var flow = Object.FindFirstObjectByType<RescueGameFlow>();
            var player = flow.player;
            var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
            CheckThat("fresh_game_state", flow.IsConfigured && flow.RescuedCount == 0 && flow.State == RescueGameFlow.GameState.Playing, "Five houses start unrescued.");
            CheckThat("npc_preparation_gates_eruption", sequence.WaitingForPreparation && !sequence.BeginEruption(), "No eruption while residents remain in town.");
            CheckThat("npc_preparation_gates_filling", !player.hose.fillingUnlocked && Mathf.Abs(player.hose.TankLitres - 250f) < .01f, "Original 250 L reserve; new refill objective remains gated.");
            flow.Pause();
            CheckThat("pause_coordinates_new_systems", Time.timeScale == 0 && player.GameplaySuspended && !sequence.evacuation.Shout(), "Paused rescue UI also blocks evacuation input.");
            CheckThat("pause_blocks_creature_damage", !player.GetComponent<CreatureCombatPlayer>().CanBeAttacked, "New combat damage also respects rescue pause.");
            flow.Resume();
            player.SetCinematicLocked(true);
            CheckThat("cinematic_locks_interaction", player.GameplaySuspended && !player.Interact(), "Cinematic and existing E routing share a gameplay gate.");
            player.SetCinematicLocked(false);
            FireCombatChecks.Run();
            var combat = JsonUtility.FromJson<CombatReport>(File.ReadAllText("Logs/FireCombatChecks.json"));
            CheckThat("fire_and_ballistic_regressions", combat.allPassed, "checks=" + combat.checks.Length + "; " + combat.error);
            InteractiveDoorChecks.Run();
            var doors = JsonUtility.FromJson<DoorReport>(File.ReadAllText("Logs/InteractiveDoorChecks.json"));
            CheckThat("door_and_hose_regressions", doors.status == "passed", "checks=" + doors.checks.Length + "; " + doors.error);
            VolcanoHazardChecks.Run(CheckThat);
            // Complete only the five authored targets; the new combat fallback must not
            // preempt or duplicate the established five-house victory screen.
            foreach (var house in flow.targetBuildings) { house.Ignite(); house.ApplyWater(house.litresToExtinguish); }
            typeof(RescueGameFlow).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(flow, null);
            CheckThat("five_houses_still_win", flow.State == RescueGameFlow.GameState.Won && flow.RescuedCount == 5 && Time.timeScale == 0, "New enemies do not replace the approved rescue win condition.");
            var survival = Object.FindFirstObjectByType<VolcanoSurvivalObjective>();
            survival.TickObjective();
            CheckThat("single_victory_owner", !survival.IsFinished && flow.State == RescueGameFlow.GameState.Won, "Standalone combat ending yields to the five-house flow.");
            Finish(null);
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static Report Read() => report ?? (report = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath)));
    static void CheckThat(string name, bool passed, string evidence)
    {
        Read().checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Save();
        if (!passed) throw new InvalidOperationException(name + ": " + evidence);
    }
    static void Save() => File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
    static void Finish(string error)
    {
        Read().error = error;
        report.status = error == null && report.runtimeErrors.Count == 0 && report.checks.All(c => c.passed) ? "passed" : "failed";
        Save(); SessionState.SetString(Phase, "done"); Time.timeScale = 1;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        else { SessionState.SetBool(Active, false); if (Application.isBatchMode) EditorApplication.Exit(report.status == "passed" ? 0 : 1); }
    }
}
