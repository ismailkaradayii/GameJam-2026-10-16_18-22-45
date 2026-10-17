using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit review of the real mountain, lava mesh and encounter. Play Mode is never saved.</summary>
[InitializeOnLoad]
public static class LavaDescentChecks
{
    const string Folder = "Library/LavaDescentReview", Pending = "LavaDescentChecks.Pending";
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report
    {
        public string status, error;
        public float simulatedSeconds;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    static Report report;
    static LavaCreatureEncounter encounter;
    static VolcanoEruptionSequence sequence;
    static Camera camera;
    static readonly HashSet<LavaCreatureActor> appeared = new HashSet<LavaCreatureActor>();
    static readonly Dictionary<LavaCreatureActor, float> initialHeights = new Dictionary<LavaCreatureActor, float>();
    static double nextPoll, began;
    static int phase = -1, lastFrame = -1;
    static bool wasBackground, capturedEmergence, capturedLeap;
    static float minGroundGap;

    static LavaDescentChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
        };
    }

    static void Update()
    {
        if (EditorApplication.timeSinceStartup > nextPoll && !EditorApplication.isCompiling)
        {
            nextPoll = EditorApplication.timeSinceStartup + .5;
            string request = Folder + "/request.txt";
            if (File.Exists(request))
            {
                string command = File.ReadAllText(request).Trim(); File.Delete(request);
                try
                {
                    if (command == "configure") VolcanoCreatureDescentSetup.Run();
                    if (command == "run") Begin();
                }
                catch (Exception ex) { File.WriteAllText(Folder + "/command-error.txt", ex.ToString()); }
            }
        }
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
    }

    [MenuItem("GameJam/Combat/Review mountain lava descent")]
    public static void Begin()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || phase >= 0) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.path != VolcanoTeamIntegrationSetup.VolcanoPath)
            throw new InvalidOperationException("Open Volcano_Demo before running this review.");
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Start()
    {
        try
        {
            report = new Report { status = "running" };
            Application.logMessageReceived += Log;
            wasBackground = Application.runInBackground; Application.runInBackground = true;
            appeared.Clear(); initialHeights.Clear(); minGroundGap = float.PositiveInfinity;
            capturedEmergence = capturedLeap = false;
            encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
            // This focused route check follows the three authored seeds. Waves have a separate review.
            encounter.enableWaves = false;
            var objective = encounter.GetComponent<VolcanoSurvivalObjective>();
            if (objective) objective.enabled = false;
            sequence = encounter.sequence;
            sequence.enabled = false; encounter.enabled = false;
            sequence.autoErupt = false; sequence.enableCameraShake = false;
            // This Play Mode fixture starts at the post-rescue lava stage; never saves the scene.
            sequence.evacuation = null;
            sequence.missionDirector = null; sequence.cinematic = null;
            sequence.InitializeSequence(); encounter.InitializeEncounter();
            var player = Object.FindFirstObjectByType<FireRescuePlayer>();
            if (player) { player.enabled = false; if (player.firstPerson) player.firstPerson.enabled = false; }
            camera = new GameObject("Temporary mountain review camera").AddComponent<Camera>();
            camera.enabled = false; camera.fieldOfView = 53f; camera.farClipPlane = 3000f;
            camera.useOcclusionCulling = false;
            encounter.TickEncounter(20f);
            Add("No spirits before eruption", encounter.SpawnedCount == 0 && encounter.spawns.All(s => !s.creature.gameObject.activeSelf));
            sequence.BeginEruption();
            while (sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing)
            { sequence.TickSequence(.05f); encounter.TickEncounter(.05f); }
            Add("Warning and eruption precede emergence", encounter.SpawnedCount == 0);
            Add("All spawns use the upstream mountain lava", encounter.spawns.All(s => s.descendFromLava && s.feed && s.feed.name != "Town Approach" && s.feedProgress < .15f && s.downhillFlows.Length == 1 && s.downhillFlows[0].name == "Town Approach"));
            began = EditorApplication.timeSinceStartup; phase = 0; Save();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - began > 90) throw new TimeoutException("Mountain review timed out.");
        // First show emergence at normal animation speed, then accelerate simulation of the full route.
        int steps = phase == 0 ? 1 : 16;
        for (int k = 0; k < steps; k++)
        {
            sequence.TickSequence(.05f); encounter.TickEncounter(.05f);
            report.simulatedSeconds += .05f;
            foreach (var spawn in encounter.spawns)
            {
                var actor = spawn.creature;
                if (!actor.gameObject.activeSelf) continue;
                var ai = actor.GetComponent<LavaCreatureCombat>();
                if (appeared.Add(actor))
                {
                    initialHeights[actor] = actor.transform.position.y;
                    Add(actor.name + " waits for visible lava around its body", spawn.feed.VisibleDistance + .005f >= spawn.feed.Length * spawn.feedProgress + 4f,
                        "front=" + spawn.feed.VisibleDistance + "; birth=" + spawn.feed.Length * spawn.feedProgress);
                    ai.TickCombat(.001f);
                    float surface = spawn.feed.PositionAtDistance(spawn.feed.Length * spawn.feedProgress).y;
                    Add(actor.name + " starts submerged", actor.transform.position.y < surface - .5f,
                        "rootY=" + actor.transform.position.y + "; lavaY=" + surface);
                }
                if (phase > 0) ai.TickCombat(.05f);
                if (ai.State != LavaCreatureCombat.Motion.Emerging)
                {
                    var jump = actor.jump;
                    float ground = jump.groundTerrain.SampleHeight(actor.transform.position) + jump.groundTerrain.transform.position.y + jump.groundClearance;
                    minGroundGap = Mathf.Min(minGroundGap, actor.transform.position.y - ground);
                }
            }
            Physics.SyncTransforms();
        }
        var first = encounter.spawns[0].creature;
        var combat = first.GetComponent<LavaCreatureCombat>();
        if (combat && first.gameObject.activeSelf)
        {
            var source = encounter.spawns[0];
            float birthSurface = source.feed.PositionAtDistance(source.feed.Length * source.feedProgress).y;
            if (!capturedEmergence && combat.State == LavaCreatureCombat.Motion.Emerging &&
                first.transform.position.y > birthSurface + .2f)
            { Capture(first, "01-lava-emergence"); capturedEmergence = true; }
            if (!capturedLeap && combat.State == LavaCreatureCombat.Motion.Leaping)
            { Capture(first, "02-mountain-hop"); capturedLeap = true; }
        }
        if (phase == 0 && appeared.Count == encounter.spawns.Length && capturedLeap &&
            encounter.spawns.All(s => s.creature.GetComponent<LavaCreatureCombat>().State != LavaCreatureCombat.Motion.Emerging))
        {
            Add("Early eruption releases a staggered group", sequence.FlowElapsed < 18f, "flowSeconds=" + sequence.FlowElapsed);
            Add("Emergence and mountain hop captured", capturedEmergence && capturedLeap);
            first.ApplyBulletDamage(12f, first.transform.position, Vector3.back);
            Add("A hit interrupts a downhill hop", combat.State == LavaCreatureCombat.Motion.Staggered);
            phase = 1;
        }
        if (phase == 1 && (encounter.spawns.All(s => !s.creature.GetComponent<LavaCreatureCombat>().IsDescending) || report.simulatedSeconds > 180f))
        {
            foreach (var spawn in encounter.spawns)
            {
                var actor = spawn.creature; var ai = actor.GetComponent<LavaCreatureCombat>();
                Add(actor.name + " completes mountain route", !ai.IsDescending && ai.DescentProgress > .98f,
                    "progress=" + ai.DescentProgress + "; position=" + actor.transform.position + "; leaps=" + ai.Leaps);
                Add(actor.name + " descends by repeated hops", ai.Leaps > 45 && initialHeights[actor] - actor.transform.position.y > 220f);
                Add(actor.name + " keeps the real player as target", ai.target && ai.target.GetComponent<Itfaiye.ItfaiyeFirstPersonController>());
            }
            Add("Hops stay above the mountain surface", minGroundGap > -.07f, "minimumGroundGap=" + minGroundGap);
            Capture(first, "03-at-mountain-foot");
            first.ApplyDamage(1000f);
            encounter.TickEncounter(20f);
            Add("Defeated spirits do not emerge again", first.IsDefeated && encounter.SpawnedCount == encounter.spawns.Length);
            Add("No runtime errors", report.errors.Count == 0);
            Finish(null); return;
        }
        Save();
    }

    static void Capture(LavaCreatureActor actor, string name)
    {
        Vector3 focus = actor.transform.position - Vector3.up * .7f;
        camera.transform.position = focus + new Vector3(7f, 4.5f, -10f);
        camera.transform.LookAt(focus);
        var rt = new RenderTexture(1200, 780, 24);
        var texture = new Texture2D(1200, 780, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1200, 780), 0, 0); texture.Apply();
            File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); }
    }
    static void Add(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
    static void Save() => File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));
    static void Finish(string error)
    {
        phase = -1; if (report == null) report = new Report();
        report.error = error;
        report.status = error == null && report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log;
        Application.runInBackground = wasBackground;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
