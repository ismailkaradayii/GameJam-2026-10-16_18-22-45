using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Read-only merged authoring validation, followed by existing physical opening, E-group and wheel-effects fixtures.</summary>
[InitializeOnLoad]
public static class Osman1810MergeChecks
{
    const string Folder = "Logs/Osman1810Merge", Active = "Osman1810MergeChecks.Active";
    [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] public sealed class Report
    {
        public string status = "running", stage, stageStartedUtc, error;
        public List<Check> checks = new List<Check>();
    }
    [Serializable] sealed class GroupReport { public string status; public int rescued; public List<Check> checks; public List<string> errors; }
    [Serializable] sealed class CombatReport { public bool allPassed; public List<Check> checks; }
    static Report report;

    static Osman1810MergeChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Active, false)) EditorApplication.delayCall += Advance;
        };
        EditorApplication.update += () =>
        {
            if (!SessionState.GetBool(Active, false) || EditorApplication.timeSinceStartup < SessionState.GetFloat(Active + ".Deadline", float.MaxValue)) return;
            Fail("Suite timed out during " + Read().stage);
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        };
    }

    [MenuItem("GameJam/Final Loop/Validate Osman1810 merge")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use idle Edit Mode.");
        Directory.CreateDirectory(Folder); report = new Report { stage = "authoring" };
        try
        {
            Require("Correct authored scene is open", SceneManager.GetActiveScene().path == FinalLoopSetup.ScenePath, SceneManager.GetActiveScene().path);
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            Require("One configured mission owns the player, story and evacuation", mission && Count<VolcanoMissionDirector>() == 1 && mission.player && mission.story && mission.evacuation && mission.gameFlow && mission.player.mission == mission, "single shared owner");
            var player = mission.player; var evacuation = mission.evacuation; var story = mission.story;
            Require("Merged preparation is 240 seconds", Mathf.Approximately(mission.preparationSeconds, 240), "seconds=" + mission.preparationSeconds);
            Require("E group rescue replaces the global call", evacuation.groupRescue && evacuation.callKey == Key.None && !evacuation.villageWideCall && !evacuation.callAllAlarmedVillagers && player.interactKey == Key.E, "groups enabled, E coordinator retained");
            Require("The original seven people form ordered groups of two, two and three", evacuation.TotalCount == 7 && evacuation.residents.GroupBy(r => r.rescueGroup).OrderBy(g => g.Key).Select(g => g.Count()).SequenceEqual(new[] { 2, 2, 3 }), "2 / 2 / 3");
            Require("Assembly staging and contextual interaction are fully bound", evacuation.assemblyPoint && evacuation.assemblySlots.Length == 7 && evacuation.assemblySlots.All(s => s) && evacuation.assemblySlots.Distinct().Count() == 7 && evacuation.dialogue && player.contextualInteractions && player.contextualInteractions.targets.Any(t => t && t.kind == ContextualInteractionKind.AssemblyPoint), "seven distinct slots and contextual assembly E target");
            var marker = Object.FindFirstObjectByType<RescueGroupMarker>();
            Require("A single exclamation marker identifies the next group", Count<RescueGroupMarker>() == 1 && marker && marker.symbol && marker.symbol.text == "!", "existing grouped marker");
            Require("Stable window pose and independent omen references survive", story.windowStop && story.windowViewPose && story.volcanoLookTarget && story.windowRoute.All(t => t) && story.omenSmoke && story.omenSparks && story.omenSmoke != mission.sequence.effects.smokePlume && story.omenSparks != mission.sequence.effects.lavaBombs, "separate cinematic pose, small smoke and sparks");
            for (int i = 0; i < 5; i++)
            {
                string path = "ASHFALL/Audio/Opening/talk" + (i + 1).ToString("00");
                var clip = Resources.Load<AudioClip>(path);
                Require("Opening recording " + (i + 1) + " resolves through Resources", clip && clip.length > 0 && (story.openingVoiceClips.Length <= i || !story.openingVoiceClips[i] || story.openingVoiceClips[i] == clip), path);
                if (clip) CheckGuid(clip);
            }
            var ending = mission.gameFlow.victoryCinematic;
            Require("Existing continuous closing and written safety narration survive", ending && ending.cinematic == mission.cinematic && ending.evacuation == evacuation && ending.shelterViewpoint && ending.shelterFocus && ending.thoughts.Length == 6 && ending.thoughts.All(t => !string.IsNullOrEmpty(t.text)) && !string.IsNullOrEmpty(ending.arrivalThought.text), "six return thoughts and survivor arrival");
            Require("Optional closing recordings retain their full duration", ending.thoughts.All(t => !t.voice || t.Duration >= t.voice.length) && (!ending.arrivalThought.voice || ending.arrivalThought.Duration >= ending.arrivalThought.voice.length), "assigned clips=" + (ending.thoughts.Count(t => t.voice) + (ending.arrivalThought.voice ? 1 : 0)) + "; the baseline closing uses written narration");
            Require("Five target houses retain their established game flow", mission.gameFlow.targetBuildings.Length == 5 && mission.gameFlow.targetBuildings.All(h => h) && mission.gameFlow.targetBuildings.Distinct().Count() == 5 && player.gameFlow == mission.gameFlow, "five distinct target references");
            var truck = player.drive; var effects = truck ? truck.GetComponent<FireTruckGroundEffects>() : null;
            Require("Responsive truck and contact-driven tread/dust remain authored", truck && !truck.progressiveHandling && truck.smoothPedals && truck.wheels.Length == 4 && truck.wheels.All(w => w) && effects && effects.tireTrackMaterial && effects.soilDustShader && effects.dustTexture && effects.looseTerrainLayers != null && effects.looseTerrainLayers.Length > 0, "original fast handling with grounded effects");
            Require("Lake fencing preserves the existing pump and hose passage", mission.fillStation && mission.fillStation.water && Object.FindFirstObjectByType<LakeHosePassage>() && player.hose && player.cannon && player.cannon.aimCamera == player.viewCamera, "lake, hose, cannon and fence passage retained");
            foreach (var component in new MonoBehaviour[] { mission, story, evacuation, ending, effects }) CheckGuid(MonoScript.FromMonoBehaviour(component));
            int missing = SceneManager.GetActiveScene().GetRootGameObjects().Sum(root => root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            Require("Merged scene has no missing scripts", missing == 0, "missing=" + missing);
            report.status = "validated"; Save();
        }
        catch (Exception ex) { Fail(ex.ToString()); throw; }
    }

    [MenuItem("GameJam/Final Loop/Check Osman1810 merge")]
    public static void Run()
    {
        if (SessionState.GetBool(Active, false)) throw new InvalidOperationException("A merged-scene suite is already active.");
        Validate();
        SessionState.SetBool(Active, true);
        Stage("opening", 300);
        OpeningCoolingPounceChecks.Begin();
    }

    static void Advance()
    {
        if (!SessionState.GetBool(Active, false)) return;
        try
        {
            switch (Read().stage)
            {
                case "opening":
                    string opening = Fresh(OpeningCoolingPounceChecks.Folder + "/opening-result-60fps.txt");
                    Require("Real opening camera, capsule and five voices pass", opening.StartsWith("SUCCESS", StringComparison.Ordinal), opening);
                    var combat = JsonUtility.FromJson<CombatReport>(Fresh("Logs/FireCombatChecks.json"));
                    Require("Opening cooling and physical water regressions pass", combat != null && combat.allPassed && combat.checks != null && combat.checks.Count == 39 && combat.checks.All(c => c.passed), "39 water/fire checks plus the opening's cooled-lava fixture");
                    Stage("E groups", 600); GroupRescueChecks.Begin(); return;
                case "E groups":
                    var groups = JsonUtility.FromJson<GroupReport>(Fresh(GroupRescueSetup.Folder + "/checks.json"));
                    Require("Actual contextual E collection, staging, departure and shelter traversal pass", groups != null && groups.status == "SUCCESS" && groups.rescued == 7 && groups.checks != null && groups.checks.All(c => c.passed) && groups.errors != null && groups.errors.Count == 0, "all seven use their real agents; closing narration is also exercised");
                    Stage("truck effects", 120); TruckGroundFeelChecks.CaptureOnly(); return;
                case "truck effects":
                    string effects = Fresh("Logs/TruckGroundFeel/checks.txt");
                    Require("Responsive physical truck produces four contact tracks and soil dust", effects.Contains("COMPLETED") && !effects.Contains("FAIL"), "see truck photos, ground contact, old acceleration and steering metrics");
                    report.status = "passed"; report.stage = "completed"; SessionState.SetBool(Active, false); Save(); return;
            }
        }
        catch (Exception ex) { Fail(ex.ToString()); }
    }

    static void CheckGuid(Object asset)
    {
        string path = asset ? AssetDatabase.GetAssetPath(asset) : "";
        string guid = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
        Require("Critical asset GUID resolves: " + (asset ? asset.name : "missing"), !string.IsNullOrEmpty(guid) && AssetDatabase.GUIDToAssetPath(guid) == path, path + " guid=" + guid);
    }
    static int Count<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
    static Report Read() => report ?? (report = JsonUtility.FromJson<Report>(File.ReadAllText(Folder + "/checks.json")));
    static void Stage(string value, float timeout)
    {
        Read().status = "running"; report.stage = value; report.stageStartedUtc = DateTime.UtcNow.ToString("O");
        SessionState.SetFloat(Active + ".Deadline", (float)EditorApplication.timeSinceStartup + timeout); Save();
    }
    static string Fresh(string path)
    {
        if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < DateTime.Parse(Read().stageStartedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind)) throw new InvalidOperationException("Fresh stage output is missing: " + path);
        return File.ReadAllText(path);
    }
    static void Require(string name, bool pass, string evidence)
    {
        Read().checks.Add(new Check { name = name, evidence = evidence, passed = pass }); Save();
        if (!pass) throw new InvalidOperationException(name + ": " + evidence);
    }
    static void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true)); }
    static void Fail(string error) { Read().status = "failed"; report.error = error; SessionState.SetBool(Active, false); Save(); }
}
