using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Forward progress and collision regressions, using both the saved wave and real spirit prefabs.
[InitializeOnLoad]
public static class SpiritMobilityChecks
{
    const string Pending = "SpiritMobilityChecks.Pending", Folder = "Logs/OpeningCoolingPounce";
    [Serializable] class Check { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static SpiritMobilityChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Run; }
        };
    }
    [MenuItem("GameJam/Final Loop/Check spirit mobility")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/mobility.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        report = new Report(); Application.logMessageReceived += Log;
        try
        {
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            var encounter = mission.encounter; var sequence = mission.sequence;
            mission.enabled = false; mission.cinematic.EndShot(); mission.story.CancelOpening();
            mission.player.PlaceOnFoot(mission.houseExit.position + mission.houseExit.forward * 15, 0);
            sequence.enabled = false; sequence.missionDirector = null; sequence.evacuation = null; sequence.cinematic = null;
            sequence.InitializeSequence(); sequence.BeginEruption(true);
            for (int i = 0; i < 500 && sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing; i++) sequence.TickSequence(.05f);
            foreach (var spawn in encounter.spawns)
                if (spawn != null && spawn.feed) { spawn.feed.Initialize(); spawn.feed.SetDistance(spawn.feed.Length); }
            encounter.enabled = false;
            for (int i = 0; i < 100 && encounter.SpawnedCount < 3; i++) encounter.TickEncounter(.1f);
            var living = encounter.LivingCreatures.ToArray();
            Add("Saved encounter releases at least three spirits", living.Length >= 3, "living=" + living.Length);
            var starts = living.Select(a => a.transform.position).ToArray();
            for (int step = 0; step < 120; step++)
                foreach (var actor in living) if (actor && !actor.IsDefeated) actor.GetComponent<LavaCreatureCombat>().TickCombat(.05f);
            for (int i = 0; i < living.Length; i++)
            {
                var actor = living[i]; var combat = actor.GetComponent<LavaCreatureCombat>();
                float travelled = Flat(starts[i], actor.transform.position);
                Add("Village spirit " + (i + 1) + " leaves its spawn and advances", travelled > 6 || actor.ReachedBuilding || actor.SelfDetonated,
                    "travel=" + travelled.ToString("F2") + ", leaps=" + combat.Leaps + ", state=" + combat.State);
            }
            for (int s = 0; s < SceneManager.sceneCount; s++)
                foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects()) root.SetActive(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary forward movement checks"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0, -.3f, 0);
            floor.transform.localScale = new Vector3(200, .5f, 200);
            var human = new GameObject("Movement target"); human.transform.position = new Vector3(0, 0, 70);
            var body = human.AddComponent<CharacterController>(); body.height = 1.8f; body.radius = .3f; body.center = Vector3.up * .9f;
            var target = human.AddComponent<CreatureCombatPlayer>(); target.allowRecovery = false;
            var runner = Creature(Vector3.zero, target);
            for (int step = 0; step < 80; step++) { runner.GetComponent<LavaCreatureCombat>().TickCombat(.05f); Physics.SyncTransforms(); }
            float progress = runner.transform.position.z;
            Add("Unobstructed spirit advances at least 32 metres in four seconds", progress >= 32, "forward=" + progress.ToString("F2"));
            Object.DestroyImmediate(runner.gameObject);

            var houseRoot = GameObject.CreatePrimitive(PrimitiveType.Cube); houseRoot.name = "Rotated route obstacle";
            houseRoot.transform.position = new Vector3(0, 3, 0); houseRoot.transform.localScale = new Vector3(12, 6, 12);
            houseRoot.transform.rotation = Quaternion.Euler(0, 18, 0);
            var house = houseRoot.AddComponent<BurnableBuilding>(); house.ignitionBounds = houseRoot.GetComponent<BoxCollider>();
            house.ApplyWater(1f);
            human.transform.position = new Vector3(0, 0, 24); Physics.SyncTransforms();
            var hunter = Creature(new Vector3(0, 0, -20), target);
            int inside = 0; float nearest = 100, side = 0;
            for (int step = 0; step < 180 && !hunter.IsDefeated; step++)
            {
                hunter.GetComponent<LavaCreatureCombat>().TickCombat(.05f); Physics.SyncTransforms();
                if (SpiritOutdoorNavigation.Contains(hunter.transform.position, 0)) inside++;
                nearest = Mathf.Min(nearest, Flat(hunter.transform.position, target.transform.position));
                side = Mathf.Max(side, Mathf.Abs(hunter.transform.position.x));
            }
            Add("Spirit routes around a rotated house and reaches its target", inside == 0 && side > 6 && (nearest < 3 || hunter.SelfDetonated),
                "inside=" + inside + ", side=" + side.ToString("F2") + ", nearest=" + nearest.ToString("F2"));
            Object.DestroyImmediate(hunter.gameObject);
            // Start outside the solid wall, but inside the extra navigation clearance margin.
            Vector3 atWall = houseRoot.transform.TransformPoint(new Vector3(.53f, -.5f, 0));
            Vector3 escape = houseRoot.transform.TransformPoint(new Vector3(.7f, -.5f, .8f));
            Add("Clearance margin permits an outward escape", !SpiritOutdoorNavigation.Blocked(atWall, escape, .8f));
            human.transform.position = escape + Vector3.forward * 18; Physics.SyncTransforms();
            var stuck = Creature(atWall, target);
            for (int step = 0; step < 80 && !stuck.IsDefeated; step++)
            { stuck.GetComponent<LavaCreatureCombat>().TickCombat(.05f); Physics.SyncTransforms(); }
            Add("A spirit beside a wall can recover and move forward", Flat(atWall, stuck.transform.position) > 8 || stuck.SelfDetonated,
                "distance=" + Flat(atWall, stuck.transform.position).ToString("F2"));
        }
        catch (Exception ex) { report.errors.Add(ex.ToString()); }
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        File.WriteAllText(Folder + "/mobility.json", JsonUtility.ToJson(report, true));
        Application.logMessageReceived -= Log; EditorApplication.ExitPlaymode();
    }
    static LavaCreatureActor Creature(Vector3 position, CreatureCombatPlayer target)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LavaCreature/Prefabs/LavaCreature.prefab"));
        root.transform.position = position;
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
        Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.localScale *= 1.8f / bounds.size.y;
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.position += Vector3.up * (position.y - bounds.min.y);
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var hitbox = new GameObject("Mobility hit capsule"); hitbox.transform.position = bounds.center;
        hitbox.transform.SetParent(root.transform.Find("SomersaultVisual"), true);
        var capsule = hitbox.AddComponent<CapsuleCollider>(); capsule.radius = .35f; capsule.height = 1.8f;
        var actor = root.AddComponent<LavaCreatureActor>(); actor.GetComponent<LavaCreatureCombat>().target = target;
        return actor;
    }
    static void Add(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static float Flat(Vector3 a, Vector3 b) => Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
}
