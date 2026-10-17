using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real-scene wave gates, finite capacity, extraction, defeat and actual scene reloads.</summary>
[InitializeOnLoad]
public static class LavaWaveChecks
{
    const string Folder = "Library/LavaWaveReview", Pending = "LavaWaveChecks.Pending";
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report
    {
        public string status, error;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public int[] releasedPerWave;
        public int peakAlive;
    }
    static Report report;
    static LavaCreatureEncounter encounter;
    static VolcanoEruptionSequence sequence;
    static VolcanoSurvivalObjective objective;
    static FireRescuePlayer player;
    static double nextPoll, started, phaseAt;
    static int phase = -1, lastFrame = -1, oldEncounterId;
    static bool previousBackground;

    static LavaWaveChecks()
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
                    if (command == "configure") Configure();
                    else if (command == "run") Begin();
                }
                catch (Exception ex) { File.WriteAllText(Folder + "/command-error.txt", ex.ToString()); }
            }
        }
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
    }

    [MenuItem("GameJam/Combat/Configure five lava waves and finale")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before saving wave settings.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != VolcanoTeamIntegrationSetup.VolcanoPath) throw new InvalidOperationException("Open Volcano_Demo first.");
        Directory.CreateDirectory(Folder);
        File.Copy(scene.path, Folder + "/Volcano_Demo.before-waves.unity", true);
        var manager = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        Undo.RecordObject(manager, "Configure finite eruption waves");
        manager.enableWaves = manager.enableFinalObjective = true;
        manager.waveSizes = new[] { 3, 4, 5, 6, 7 };
        manager.secondsBetweenWaves = 32f; manager.secondsBetweenSpawns = 1.8f; manager.maximumAlive = 10;
        VolcanoCreatureDescentSetup.ConfigureWaveAmmo();
        Undo.FlushUndoRecordObjects(); EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Folder + "/configured.txt", "3, 4, 5, 6, 7 spirits; 32 seconds between waves; 10 alive; clear all and board the truck.");
    }

    [MenuItem("GameJam/Combat/Review waves and game endings")]
    public static void Begin()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling || phase >= 0) return;
        if (SceneManager.GetActiveScene().path != VolcanoTeamIntegrationSetup.VolcanoPath)
            throw new InvalidOperationException("Open Volcano_Demo before running the review.");
        Directory.CreateDirectory(Folder); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    static void Start()
    {
        try
        {
            report = new Report { status = "running", releasedPerWave = new int[5] };
            Application.logMessageReceived += Log;
            previousBackground = Application.runInBackground; Application.runInBackground = true;
            encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>(); encounter.enabled = false;
            sequence = encounter.sequence; sequence.enabled = false; sequence.autoErupt = false; sequence.enableCameraShake = false;
            objective = encounter.GetComponent<VolcanoSurvivalObjective>(); objective.enabled = false;
            player = Object.FindFirstObjectByType<FireRescuePlayer>(); player.enabled = false; player.firstPerson.enabled = false;
            player.SetDriving(false);
            Add("Default wave sizes and finite total", encounter.waveSizes.SequenceEqual(new[] { 3, 4, 5, 6, 7 }) && encounter.PlannedCount == 25);
            Add("Mission ammunition supports all waves with missed shots", player.pistol.reserveAmmo >= 240);
            sequence.InitializeSequence(); Step(10f);
            Add("No waves before eruption", encounter.SpawnedCount == 0 && encounter.WaveNumber == 0);
            Add("Evacuation still gates the volcano", sequence.WaitingForEvacuation && !sequence.BeginEruption());
            // The rest of this fixture begins after the independently reviewed evacuation.
            sequence.evacuation = null; sequence.missionDirector = null; sequence.cinematic = null; sequence.BeginEruption();
            for (int i = 0; i < 400 && sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing; i++) sequence.TickSequence(.05f);
            Add("Eruption reaches flowing stage", sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing);
            foreach (var spawn in encounter.spawns) spawn.feed.SetDistance(0f);
            Step(20f); Add("Flowing stage alone cannot spawn on dry ground", encounter.SpawnedCount == 0);
            var first = encounter.spawns[0];
            first.feed.SetDistance(first.feed.Length * first.feedProgress + LavaCreatureEncounter.EmergenceLavaMargin - .03f);
            Step(10f); Add("Waves wait for lava to cover the whole birth footprint", encounter.SpawnedCount == 0);
            first.feed.SetDistance(first.feed.Length);
            for (int i = 0; i < 120 && encounter.SpawnedCount < 3; i++) Step(.1f);
            Add("First wave has three distinct live spirits", encounter.SpawnedCount == 3 && encounter.AliveCount == 3 &&
                encounter.LivingCreatures.Select(a => a.GetInstanceID()).Distinct().Count() == 3);
            Add("Wave copies retain animation and hitboxes", encounter.LivingCreatures.All(a =>
                a.GetComponentInChildren<Animator>() && a.GetComponentInChildren<Collider>() && a.GetComponent<LavaCreatureCombat>()));
            Add("Every wave copy has exactly one living flame visual", encounter.LivingCreatures.All(a =>
                a.GetComponentsInChildren<LavaSpiritVisual>().Length == 1 && a.GetComponent<LavaSpiritVisual>().IsBuilt));
            var hearts = encounter.LivingCreatures.Select(a => a.GetComponent<LavaSpiritVisual>().VisualRoot.Find("Molten heart").GetComponent<MeshFilter>()).ToArray();
            Add("Wave spirits share mesh and material assets", hearts.All(h => h.sharedMesh == hearts[0].sharedMesh &&
                h.GetComponent<Renderer>().sharedMaterial == hearts[0].GetComponent<Renderer>().sharedMaterial));
            foreach (var actor in encounter.LivingCreatures) actor.GetComponent<LavaCreatureCombat>().TickCombat(.001f);
            Add("Every wave spirit starts its downhill route", encounter.LivingCreatures.All(a => a.GetComponent<LavaCreatureCombat>().IsDescending));
            int before = encounter.SpawnedCount; Step(1f);
            Add("A completed wave waits before the next one", encounter.SpawnedCount == before && encounter.WaveNumber == 1);
            started = EditorApplication.timeSinceStartup; Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - started > 100) throw new TimeoutException("Wave/end review phase " + phase);
        double age = EditorApplication.timeSinceStartup - phaseAt;
        if (phase == 0)
        {
            if (age < 4) return;
            Add("All three live wave spirits repeatedly hop down the mountain", encounter.LivingCreatures.All(a =>
                a.GetComponent<LavaCreatureCombat>().Leaps >= 2 && a.GetComponent<LavaCreatureCombat>().IsDescending));
            CaptureGroup();
            for (int n = 0; n < 500; n++) Step(.5f);
            Add("Later waves arrive while earlier spirits are alive", encounter.WaveNumber >= 3);
            Add("Living population is bounded without dropping queued enemies", encounter.AliveCount == 10 && report.peakAlive == 10 && !encounter.AllWavesReleased);
            var original = encounter.spawns[0].creature;
            original.ApplyDamage(1000f); int before = encounter.SpawnedCount; Step(2f);
            Add("Freeing a slot releases another fresh spirit", encounter.SpawnedCount == before + 1 && encounter.AliveCount == 10);
            var replacement = encounter.LivingCreatures[encounter.LivingCreatures.Count - 1];
            Add("Copies never inherit a dead body's disabled state", replacement != original && replacement.Health == replacement.maxHealth &&
                replacement.GetComponentInChildren<Collider>() && replacement.GetComponentInChildren<Animator>().enabled && !replacement.IsDefeated);
            Add("Replacement spirit renders its own fresh molten heart", replacement.GetComponent<LavaSpiritVisual>().IsBuilt &&
                replacement.GetComponent<LavaSpiritVisual>().VisualRoot.Find("Molten heart").GetComponent<Renderer>().enabled);
            for (int n = 0; n < 1000 && !encounter.AllWavesReleased; n++)
            {
                if (encounter.AliveCount >= encounter.maximumAlive) encounter.LivingCreatures[0].ApplyDamage(1000f);
                Step(1f);
            }
            Add("Exactly five increasing waves are released", report.releasedPerWave.SequenceEqual(new[] { 3, 4, 5, 6, 7 }), string.Join(",", report.releasedPerWave));
            Add("No endless sixth wave", encounter.AllWavesReleased && encounter.SpawnedCount == 25 && encounter.WaveNumber == 5);
            Step(1000f); Add("Long waits do not create more enemies", encounter.SpawnedCount == 25);
            objective.enabled = true;
            player.SetDriving(true); objective.TickObjective();
            Add("Entering the truck early cannot win", !objective.IsFinished && objective.State == VolcanoSurvivalObjective.MissionState.Fighting);
            player.SetDriving(false);
            while (encounter.AliveCount > 1) { encounter.LivingCreatures[0].ApplyDamage(1000f); Step(.1f); }
            objective.TickObjective(); Add("The last living spirit still blocks extraction", !encounter.IsCleared && objective.State == VolcanoSurvivalObjective.MissionState.Fighting);
            encounter.LivingCreatures[0].ApplyDamage(1000f); Step(.1f); objective.TickObjective();
            Add("Clearing all twenty five unlocks return to truck", encounter.IsCleared && encounter.DefeatedCount == 25 && objective.State == VolcanoSurvivalObjective.MissionState.ReturnToTruck);
            oldEncounterId = encounter.GetInstanceID();
            player.SetDriving(true); objective.TickObjective();
            Add("Boarding the truck completes the mission", objective.State == VolcanoSurvivalObjective.MissionState.Won);
            Add("Victory stops spawning, freezes play and unlocks the cursor", encounter.IsStopped && Time.timeScale == 0f && Cursor.lockState == CursorLockMode.None && Cursor.visible);
            ScreenCapture.CaptureScreenshot(Folder + "/02-victory.png"); Next(1); return;
        }
        if (phase == 1 && age > 1)
        {
            Add("Replay and menu scenes are included in the build", Application.CanStreamedLevelBeLoaded("Volcano_Demo") && Application.CanStreamedLevelBeLoaded("MainMenu"));
            objective.RestartMission(); Next(2); return;
        }
        if (phase == 2 && age > 1)
        {
            encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
            if (!encounter || encounter.GetInstanceID() == oldEncounterId) return;
            objective = encounter.GetComponent<VolcanoSurvivalObjective>(); player = Object.FindFirstObjectByType<FireRescuePlayer>();
            objective.TickObjective();
            Add("Replay creates a fresh zero-wave mission", encounter.SpawnedCount == 0 && encounter.WaveNumber == 0 && encounter.DefeatedCount == 0 && !encounter.IsStopped);
            Add("Replay restores time, player controls and health", Time.timeScale == 1f && player.enabled && objective.vitals.Health == objective.vitals.maximumHealth);
            Add("Replay restores the civilian rescue phase", encounter.sequence.WaitingForEvacuation);
            player.SetDriving(false);
            objective.vitals.TryTakeDamage(1000f, player.transform.position + Vector3.forward);
            Add("Lethal damage opens defeat instead of auto-respawning", objective.State == VolcanoSurvivalObjective.MissionState.Lost && objective.vitals.Health == 0f && !objective.vitals.allowRecovery && Time.timeScale == 0f);
            ScreenCapture.CaptureScreenshot(Folder + "/03-defeat.png"); Next(3); return;
        }
        if (phase == 3 && age > 1) { objective.ReturnToMenu(); Next(4); return; }
        if (phase == 4 && age > 1)
        {
            if (SceneManager.GetActiveScene().name != "MainMenu") return;
            Add("Defeat menu action actually loads MainMenu", Time.timeScale == 1f && !Object.FindFirstObjectByType<VolcanoSurvivalObjective>());
            Add("No runtime errors", report.errors.Count == 0, string.Join("; ", report.errors));
            Finish(null);
        }
    }

    static void Step(float dt)
    {
        int before = encounter.SpawnedCount;
        encounter.TickEncounter(dt);
        if (encounter.SpawnedCount > before) report.releasedPerWave[encounter.WaveNumber - 1] += encounter.SpawnedCount - before;
        report.peakAlive = Mathf.Max(report.peakAlive, encounter.AliveCount);
    }

    static void CaptureGroup()
    {
        var first = encounter.LivingCreatures[0];
        var camera = new GameObject("Temporary wave review camera").AddComponent<Camera>();
        camera.enabled = false; camera.fieldOfView = 58; camera.farClipPlane = 3000; camera.useOcclusionCulling = false;
        Vector3 center = first.transform.position;
        camera.transform.position = center + new Vector3(18, 10, -22); camera.transform.LookAt(center + Vector3.back * 10f);
        var rt = new RenderTexture(1200, 780, 24); var texture = new Texture2D(1200, 780, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1200, 780), 0, 0); texture.Apply();
            File.WriteAllBytes(Folder + "/01-mountain-wave.png", texture.EncodeToPNG());
            var visual = first.GetComponent<LavaSpiritVisual>().VisualRoot;
            Vector3 lookAt = visual.TransformPoint(new Vector3(0, .88f, 0));
            camera.transform.position = lookAt + visual.forward * 4.4f + visual.right * 1.5f + Vector3.up * .65f;
            camera.transform.LookAt(lookAt); camera.fieldOfView = 42f;
            camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1200, 780), 0, 0); texture.Apply();
            File.WriteAllBytes(Folder + "/04-spirit-closeup.png", texture.EncodeToPNG());
        }
        finally { camera.targetTexture = null; RenderTexture.active = old; Object.DestroyImmediate(texture); Object.DestroyImmediate(rt); Object.Destroy(camera.gameObject); }
    }
    static void Add(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Next(int value) { phase = value; phaseAt = EditorApplication.timeSinceStartup; Save(); }
    static void Log(string text, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(text + "\n" + trace); }
    static void Save() => File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));
    static void Finish(string error)
    {
        phase = -1; if (report == null) report = new Report(); report.error = error;
        report.status = error == null && report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log; Application.runInBackground = previousBackground;
        Time.timeScale = 1f;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
