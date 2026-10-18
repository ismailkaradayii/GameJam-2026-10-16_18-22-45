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

/// <summary>Saved-scene checks for ten mixed spirits, three fires and objective feedback.</summary>
[InitializeOnLoad]
public static class ShortDefenseChecks
{
    const string Pending = "ShortDefenseChecks.Pending", Folder = "Logs/ShortDefense";
    [Serializable] class Check { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static double captureAt, exitAt;
    static bool capturing, oldBackground;

    static ShortDefenseChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Run; }
        };
        EditorApplication.update += Capture;
    }

    [MenuItem("GameJam/Final Loop/Apply short defense")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
        Directory.CreateDirectory(Folder);
        if (!EditorSceneManager.SaveScene(scene, Folder + "/Before-short-defense.unity", true)) throw new IOException("Could not preserve the current editor scene.");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        Undo.RecordObjects(new Object[] { mission.encounter, mission.gameFlow }, "Short defense mission");
        mission.encounter.waveSizes = new[] { 10 }; mission.encounter.maximumAlive = 10;
        mission.encounter.buildingAttackShare = .3f;
        mission.gameFlow.defenseFireBuildings = mission.gameFlow.targetBuildings.Where(h => h).Take(3).ToArray();
        EditorUtility.SetDirty(mission.encounter); EditorUtility.SetDirty(mission.gameFlow);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save short defense configuration.");
    }

    [MenuItem("GameJam/Final Loop/Check short defense")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        if (SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    static void Run()
    {
        report = new Report(); Application.logMessageReceived += Log;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        try
        {
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            var encounter = mission.encounter; var sequence = mission.sequence; var flow = mission.gameFlow;
            var survival = encounter.GetComponent<VolcanoSurvivalObjective>();
            var hud = Object.FindFirstObjectByType<RescueHUD>();
            mission.enabled = false; mission.cinematic.EndShot(); mission.story.CancelOpening();
            mission.story.enabled = false; survival.enabled = false;
            mission.player.PlaceOnFoot(mission.houseExit.position + mission.houseExit.forward * 15, 0);
            sequence.enabled = false; sequence.missionDirector = null; sequence.evacuation = null; sequence.cinematic = null;
            sequence.InitializeSequence(); sequence.BeginEruption(true);
            for (int i = 0; i < 500 && sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing; i++) sequence.TickSequence(.05f);
            foreach (var route in sequence.routes)
                if (route?.flow) { route.flow.Initialize(); route.flow.SetDistance(route.flow.Length); }
            sequence.TickSequence(.1f);
            encounter.enabled = false;
            for (int i = 0; i < 2000; i++) encounter.TickEncounter(.1f);
            Add("Saved scene releases exactly ten spirits in a single wave", encounter.PlannedCount == 10 && encounter.SpawnedCount == 10 && encounter.AliveCount == 10 && encounter.WaveCount == 1 && encounter.AllWavesReleased);
            var houseAttackers = encounter.LivingCreatures.Select(a => a.GetComponent<LavaCreatureCombat>()).Where(c => c.attackBuildings).ToArray();
            Add("The live roster contains exactly three house attackers and seven player hunters", houseAttackers.Length == 3 && encounter.LivingCreatures.Count(a => !a.GetComponent<LavaCreatureCombat>().attackBuildings) == 7,
                string.Concat(encounter.LivingCreatures.Select(a => a.GetComponent<LavaCreatureCombat>().attackBuildings ? "H" : "P")));
            var randomState = UnityEngine.Random.state;
            var patterns = new HashSet<string>(); bool balanced = true;
            try
            {
                var shuffle = typeof(LavaCreatureEncounter).GetMethod("CreateAttackOrder", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                for (int sample = 0; sample < 32; sample++)
                {
                    var order = (bool[])shuffle.Invoke(null, new object[] { 10, .3f });
                    balanced &= order.Length == 10 && order.Count(role => role) == 3;
                    patterns.Add(string.Concat(order.Select(role => role ? "H" : "P")));
                }
            }
            finally { UnityEngine.Random.state = randomState; }
            Add("Shuffled runs retain the exact split while varying the arrival order", balanced && patterns.Count > 1, "Distinct orders across 32 samples: " + patterns.Count);
            foreach (var actor in encounter.LivingCreatures) actor.GetComponent<LavaCreatureCombat>().enabled = false;
            var houses = BurnableBuilding.ActiveBuildings.Where(h => h && h.gameObject.scene == flow.gameObject.scene).ToArray();
            int burningBefore = houses.Count(h => h.IsBurning);
            flow.TickDefenseFires();
            Add("House attackers get a chance to reach their targets before fallback fires start", houses.Count(h => h.IsBurning) == burningBefore);
            foreach (var house in houses) house.Ignite();
            var required = flow.defenseFireBuildings;
            Add("Only the three selected houses can ignite, including contact attempts on every other house", flow.RequiredFireCount == 3 && houses.Count(h => h.IsBurning) == 3 && required.All(h => h.IsBurning));
            var findBuilding = typeof(LavaCreatureCombat).GetMethod("FindBuilding", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            bool allHaveHouses = houseAttackers.All(c => (bool)findBuilding.Invoke(c, null) && required.Contains(c.BuildingTarget));
            Add("Each house attacker owns one of the three distinct mission houses", allHaveHouses &&
                houseAttackers.Select(c => c.AssignedBuilding).Distinct().Count() == 3 &&
                houseAttackers.All(c => c.BuildingTarget == c.AssignedBuilding));
            foreach (var house in houses) house.enabled = false;
            foreach (var house in required) house.enabled = true;
            var first = required[0]; first.ApplyWater(first.litresToExtinguish * .5f); flow.TickDefenseFires();
            Add("Repeated fire-objective updates do not replenish a partially extinguished fire", Mathf.Abs(first.Heat - .5f) < .001f);
            first.ApplyWater(first.litresToExtinguish + 1);
            var firstAttacker = houseAttackers.Single(c => c.AssignedBuilding == first);
            Add("A saved assigned house cannot redirect its attacker to another burning house", !(bool)findBuilding.Invoke(firstAttacker, null) &&
                required.Skip(1).All(h => h.IsBurning) && firstAttacker.AssignedBuilding == first);

            survival.enabled = true;
            foreach (var actor in encounter.LivingCreatures.Take(9).ToArray()) actor.ApplyDamage(actor.Health + 1);
            encounter.TickEncounter(.1f);
            foreach (var house in required.Take(2)) house.ApplyWater(house.litresToExtinguish + 1);
            survival.TickObjective();
            Add("Two extinguished houses and nine cleared spirits cannot finish the game", flow.State == RescueGameFlow.GameState.Playing && flow.RescuedCount == 2 && !flow.FireObjectiveComplete);
            var survivor = encounter.LivingCreatures[0];
            // Completing all fire tasks cannot skip the last live enemy.
            required[2].ApplyWater(required[2].litresToExtinguish + 1);
            survival.TickObjective();
            Add("Three extinguished houses still require the tenth spirit", flow.FireObjectiveComplete && flow.RescuedCount == 3 && flow.State == RescueGameFlow.GameState.Playing && encounter.AliveCount == 1);
            Add("Saved houses cease to be spirit targets", houseAttackers.All(c => !(bool)findBuilding.Invoke(c, null)));
            foreach (var house in required) house.Ignite();
            flow.TickDefenseFires();
            Add("Extinguished mission houses cannot reignite", required.All(h => h.IsRescued && !h.IsBurning));
            foreach (var house in houses.Except(required)) { house.enabled = true; house.Ignite(); }
            Add("Finishing a fire does not permit a fourth house to burn", houses.All(h => !h.IsBurning));

            // Test the real HUD card while gameplay is still visible.
            hud.SendMessage("LateUpdate"); hud.enabled = false;
            var feedback = hud.questTitleText.GetComponentInParent<ObjectiveCardFeedback>();
            var panel = hud.questTitleText.transform.parent as RectTransform;
            feedback.TickFeedback("SHORT DEFENSE CHECK", true, 0);
            int notices = feedback.NotificationCount;
            float startX = panel.anchoredPosition.x;
            feedback.TickFeedback("SHORT DEFENSE CHECK", true, .65f);
            Add("New objective slides in from the left and settles fully visible", panel.anchoredPosition.x > startX + 200 && Mathf.Abs(hud.objectiveGroup.alpha - 1) < .001f && Vector3.Distance(panel.localScale, Vector3.one) < .001f);
            feedback.TickFeedback("SHORT DEFENSE CHECK", true, .1f);
            Add("Updating counters under the same title does not replay the notification", feedback.NotificationCount == notices);
            float age = feedback.AnimationAge;
            feedback.TickFeedback("NEXT OBJECTIVE", false, 5);
            Add("Pause and cinematics defer new objective notifications", feedback.NotificationCount == notices && feedback.AnimationAge == age);
            feedback.TickFeedback("NEXT OBJECTIVE", true, .1f);
            Add("Deferred objective is announced exactly once when gameplay returns", feedback.NotificationCount == notices + 1);
            var audio = feedback.GetComponent<AudioSource>();
            var samples = new float[audio.clip.samples]; audio.clip.GetData(samples, 0);
            Add("Notification has a short non-silent 2D chime respecting SFX volume", audio.spatialBlend == 0 && !audio.loop && audio.clip.length < .5f && samples.Any(s => Mathf.Abs(s) > .01f) && Mathf.Approximately(audio.volume, .28f * Mathf.Clamp01(PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1f))));

            survivor.ApplyDamage(survivor.Health + 1); encounter.TickEncounter(.1f);
            survival.TickObjective();
            Add("Ten cleared spirits and three extinguished houses immediately start the existing finale", encounter.IsCleared && flow.FireObjectiveComplete && (flow.IsVictoryCinematic || flow.State == RescueGameFlow.GameState.Won));
            if (flow.victoryCinematic) flow.victoryCinematic.Cancel();
            survival.enabled = false;
            // Isolate a fresh fire objective to verify that zero live flames cannot bypass
            // a required house that has not burned yet, e.g. because it was pre-wetted.
            var originalScene = SceneManager.GetActiveScene();
            var probeScene = SceneManager.CreateScene("Short defense objective probe");
            SceneManager.SetActiveScene(probeScene);
            var isolated = new GameObject("Unfinished fire objective probe"); isolated.SetActive(false);
            var freshHouse = isolated.AddComponent<BurnableBuilding>(); freshHouse.fitFireToBuilding = false;
            var probe = isolated.AddComponent<RescueGameFlow>(); probe.targetBuildings = new[] { freshHouse }; probe.defenseFireBuildings = new[] { freshHouse };
            isolated.SetActive(true);
            Add("An unlit required house does not count as extinguished", !freshHouse.IsBurning && probe.RequiredFireCount == 1 && !probe.FireObjectiveComplete);
            probe.TickDefenseFires();
            Add("Intercepting all house attackers still starts an outstanding fire objective", freshHouse.IsBurning && !probe.FireObjectiveComplete);
            freshHouse.TickFire(freshHouse.secondsToCollapse + 1); probe.SendMessage("Update");
            Add("A collapsed required house produces a defeat rather than an unwinnable mission", probe.State == RescueGameFlow.GameState.Lost && probe.DefeatReason == RescueGameFlow.LossReason.RequiredHouseLost);
            Object.DestroyImmediate(isolated);
            SceneManager.SetActiveScene(originalScene);
            SceneManager.UnloadSceneAsync(probeScene);

            // Restore a composed HUD for a screenshot of the actual card at rest.
            mission.enabled = false; mission.story.enabled = false; sequence.enabled = false; encounter.enabled = false; flow.enabled = false; survival.enabled = false;
            mission.cinematic.EndShot(); if (flow.victoryCinematic) flow.victoryCinematic.Cancel();
            Time.timeScale = 0; hud.enabled = false; hud.canvas.enabled = true; hud.gameplayGroup.alpha = 1;
            hud.questTitleText.text = "DEFEND THE VILLAGE";
            hud.questBodyText.text = "Stop 10 fire spirits and extinguish 3 burning houses. Use the truck water cannon on the fires.";
            hud.questHintText.text = "Spirits cleared: 0/10\nFires extinguished: 0/3";
            hud.RefreshCleanLayout(); feedback.TickFeedback(hud.questTitleText.text, true, 0); feedback.TickFeedback(hud.questTitleText.text, true, .7f);
            captureAt = EditorApplication.timeSinceStartup + .8; exitAt = captureAt + 1; capturing = true;
        }
        catch (Exception ex) { report.errors.Add(ex.ToString()); Finish(); }
    }
    static void Capture()
    {
        if (!capturing) return;
        if (EditorApplication.timeSinceStartup >= captureAt && captureAt > 0)
        { ScreenCapture.CaptureScreenshot(Folder + "/objective-card.png"); captureAt = -1; }
        if (EditorApplication.timeSinceStartup >= exitAt) { capturing = false; Finish(); }
    }
    static void Finish()
    {
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true));
        Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
        Time.timeScale = 1; EditorApplication.ExitPlaymode();
    }
    static void Add(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
}
