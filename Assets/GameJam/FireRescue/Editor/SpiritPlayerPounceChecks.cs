using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BKPureNature;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

/// <summary>Exercises actual spirit motion, collision and player health in a disposable scene.</summary>
[InitializeOnLoad]
public static class SpiritPlayerPounceChecks
{
    const string Folder = "Logs/SpiritPlayerPounce", Pending = "SpiritPlayerPounceChecks.Pending";
    static readonly string[] Cases = { "direct hit", "sidestep", "bullet during charge", "water in flight", "wall shields player",
        "two spirits", "coarse frame steps", "tall player", "lethal explosion", "nonlethal charge interruption", "nearby house raider",
        "long range leap", "pistol kill in flight", "faster long approach", "near miss", "coarse near miss",
        "target disabled in flight", "retry after dodge" };
    [Serializable] class Result { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status = "RUNNING"; public List<Result> checks = new List<Result>(); public List<string> errors = new List<string>(); }
    static Report report;
    static LavaCreatureActor actor, second;
    static LavaCreatureCombat combat;
    static CreatureCombatPlayer victim;
    static GameObject wall;
    static GameObject distractionHouse;
    static Camera camera;
    static GameObject pistolRig;
    static Itfaiye.ItfaiyePistol pistol;
    static int scenario = -1, frame, shatters, defeated, hitsAtFinish;
    static float started, actionAt, finishedAt, before, oldCapture, oldScale;
    static float launchedAt, launchDistance, maxCoreHeight, shotAt, firstHopAt, feedbackRealAt;
    static int airborneShots;
    static Vector3 cameraRest, hopOrigin, missLanding;
    static Quaternion cameraRestRotation;
    static bool acted, sawCharge, sawFlight, earlyDamage, capturedCharge, capturedFlight, capturedBurst, oldBackground, retried;
    static double deadline;

    static SpiritPlayerPounceChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && scenario >= 0) Finish("Review interrupted");
        };
    }

    [MenuItem("GameJam/Final Loop/Check spirit player pounce")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/report.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    [MenuItem("GameJam/Final Loop/Increase spirit population")]
    public static void ConfigurePopulation()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != VolcanoTeamIntegrationSetup.VolcanoPath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode before updating its waves.");
        if (scene.isDirty) throw new InvalidOperationException("Save the current scene edits before updating its waves.");
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        Undo.RecordObject(encounter, "Increase spirit population by thirty percent");
        encounter.waveSizes = new[] { 4, 4, 5 };
        encounter.maximumAlive = 9;
        EditorUtility.SetDirty(encounter);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/population.json", JsonUtility.ToJson(encounter, true));
    }

    static void Start()
    {
        report = new Report();
        oldCapture = Time.captureDeltaTime; oldScale = Time.timeScale; oldBackground = Application.runInBackground;
        Time.captureDeltaTime = 1f / 60f; Time.timeScale = 1f; Application.runInBackground = true;
        Application.logMessageReceived += Log; deadline = EditorApplication.timeSinceStartup + 150;
        try
        {
            var authoredEncounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary player pounce review"));
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Review ground"; ground.transform.position = new Vector3(0, -.3f, 0); ground.transform.localScale = new Vector3(40, .5f, 40);
            var light = new GameObject("Review light").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 1.8f; light.transform.rotation = Quaternion.Euler(50, -30, 0);
            camera = new GameObject("Pounce review camera").AddComponent<Camera>(); camera.fieldOfView = 48;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.055f, .075f, .1f);
            camera.gameObject.AddComponent<AudioListener>(); camera.transform.position = new Vector3(7, 4, 9);
            camera.transform.LookAt(new Vector3(0, 1, 2.2f));
            CheckPopulation(authoredEncounter);
            BeginCase(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void BeginCase(int index)
    {
        if (actor) { actor.gameObject.SetActive(false); Object.Destroy(actor.gameObject); }
        if (second) { second.gameObject.SetActive(false); Object.Destroy(second.gameObject); }
        if (victim) { victim.gameObject.SetActive(false); Object.Destroy(victim.gameObject); }
        if (wall) { wall.SetActive(false); Object.Destroy(wall); }
        if (distractionHouse) { distractionHouse.SetActive(false); Object.Destroy(distractionHouse); }
        if (pistolRig) { pistolRig.SetActive(false); Object.Destroy(pistolRig); }
        pistolRig = null; pistol = null;
        second = null; wall = null; scenario = index;
        if (index >= Cases.Length) { Finish(null); return; }
        Time.captureDeltaTime = index == 6 || index == 15 ? .1f : 1f / 60f;
        var target = new GameObject("Player health target"); target.transform.position = new Vector3(0, 0, index == 13 ? 30f : index == 11 || index == 12 ? 12f : 4.8f);
        var body = target.AddComponent<CharacterController>(); body.height = index == 7 ? 4.2f : 1.8f;
        body.radius = .3f; body.center = Vector3.up * body.height * .5f;
        if (index == 0)
        {
            var coordinator = target.AddComponent<FireRescuePlayer>(); coordinator.enabled = false; coordinator.showHud = false;
        }
        victim = target.AddComponent<CreatureCombatPlayer>(); victim.allowRecovery = false;
        if (index == 0) target.GetComponent<FireRescuePlayer>().viewCamera = camera;
        var silhouette = GameObject.CreatePrimitive(PrimitiveType.Capsule); silhouette.name = "Player silhouette";
        silhouette.GetComponent<Collider>().enabled = false; silhouette.transform.SetParent(target.transform, false);
        silhouette.transform.localPosition = body.center; silhouette.transform.localScale = new Vector3(.6f, body.height * .5f, .6f);
        actor = Creature(index == 5 ? Vector3.left * .65f : Vector3.zero);
        combat = actor.GetComponent<LavaCreatureCombat>(); combat.target = victim;
        if (index == 5) { second = Creature(Vector3.right * .65f); second.GetComponent<LavaCreatureCombat>().target = victim; }
        if (index == 4)
        {
            wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Wall between blast and player";
            wall.transform.position = new Vector3(0, 2, 4); wall.transform.localScale = new Vector3(12, 5, .3f);
        }
        if (index == 10)
        {
            distractionHouse = new GameObject("Raider's distant house target"); distractionHouse.transform.position = new Vector3(20, 0, 20);
            var box = distractionHouse.AddComponent<BoxCollider>(); box.isTrigger = true; box.center = Vector3.up * 3; box.size = Vector3.one * 6;
            var house = distractionHouse.AddComponent<BurnableBuilding>(); house.ignitionBounds = box;
            combat.attackBuildings = true;
        }
        if (index == 12)
        {
            pistolRig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab"));
            pistolRig.transform.position = new Vector3(3, 0, 14);
            foreach (var listener in pistolRig.GetComponentsInChildren<AudioListener>()) listener.enabled = false;
            foreach (var view in pistolRig.GetComponentsInChildren<Camera>()) view.enabled = false;
            var player = pistolRig.GetComponent<Itfaiye.ItfaiyeFirstPersonController>(); player.externalControl = true; player.SetInputCaptured(true);
            player.GetComponent<CharacterController>().enabled = false;
            pistol = player.pistol; pistol.externalControl = true; pistol.showHud = false; pistol.Equip();
            var aim = pistolRig.AddComponent<SpiritPounceAimFixture>(); aim.pistol = pistol; aim.actor = actor;
        }
        defeated = 0; victim.Defeated += () => defeated++;
        if (index == 8) victim.TakeHeatDamage(90);
        before = victim.Health; shatters = LavaCombatEffects.ShattersPlayed;
        started = Time.time; actionAt = finishedAt = -1;
        launchedAt = firstHopAt = -1f; maxCoreHeight = launchDistance = 0f; shotAt = -100f; airborneShots = 0;
        cameraRest = camera.transform.localPosition; cameraRestRotation = camera.transform.localRotation;
        hopOrigin = actor.transform.position;
        acted = sawCharge = sawFlight = earlyDamage = capturedCharge = capturedFlight = capturedBurst = retried = false;
        Physics.SyncTransforms(); Save();
    }

    static LavaCreatureActor Creature(Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LavaCreature/Prefabs/LavaCreature.prefab");
        var root = Object.Instantiate(prefab); root.name = "Review pouncing spirit"; root.transform.position = position;
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
        Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.localScale *= 1.8f / bounds.size.y;
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.position += Vector3.up * (position.y - bounds.min.y);
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var hitbox = new GameObject("Review hit capsule"); hitbox.transform.position = bounds.center;
        hitbox.transform.SetParent(root.transform.Find("SomersaultVisual"), true);
        var capsule = hitbox.AddComponent<CapsuleCollider>(); capsule.radius = .35f; capsule.height = 1.8f;
        return root.AddComponent<LavaCreatureActor>();
    }

    static void Update()
    {
        if (scenario < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Review timed out"); return; }
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Tick()
    {
        if (Time.time - started > 7)
        { Check("Attack completes", false, "state=" + combat.State + ", health=" + victim.Health + ", position=" + actor.transform.position); BeginCase(scenario + 1); return; }
        if (victim.Health < before && !actor.SelfDetonated && !(second && second.SelfDetonated)) earlyDamage = true;
        maxCoreHeight = Mathf.Max(maxCoreHeight, actor.GetComponent<LavaSpiritVisual>().VisualRoot.TransformPoint(Vector3.up * .65f).y);
        if (scenario == 13 && combat.State == LavaCreatureCombat.Motion.Leaping && !combat.IsPlayerPouncing && firstHopAt < 0f)
            firstHopAt = Time.time;
        if (scenario == 13 && firstHopAt >= 0 && combat.State == LavaCreatureCombat.Motion.Recovery && combat.Leaps == 1)
        {
            float travel = new Vector2(actor.transform.position.x - hopOrigin.x, actor.transform.position.z - hopOrigin.z).magnitude;
            Check("Approach hop covers more than 5.8 metres in under .6 seconds", travel > 5.8f && Time.time - firstHopAt < .6f,
                "distance=" + travel + ", airborneSeconds=" + (Time.time - firstHopAt));
            firstHopAt = -100f;
        }
        if (combat.IsAttacking && combat.State == LavaCreatureCombat.Motion.Windup)
        {
            sawCharge = true;
            if (scenario == 0 && combat.AttackCharge > .65f && !capturedCharge)
            { Capture("01-angry-ground-charge"); capturedCharge = true; }
            if ((scenario == 2 || scenario == 9) && combat.AttackCharge > .5f && !acted)
            {
                acted = true; actionAt = Time.time;
                actor.ApplyBulletDamage(scenario == 2 ? 1000 : 15, actor.GetComponent<LavaSpiritVisual>().VisualRoot.position, Vector3.back);
                if (scenario == 9)
                {
                    Check("Bullet interrupts charge and clears the warning", combat.State == LavaCreatureCombat.Motion.Staggered && !combat.IsAttacking &&
                        actor.GetComponentsInChildren<LineRenderer>().All(r => !r.enabled));
                    Check("Nonlethal interruption preserves the spirit", actor.Health == 85 && !actor.IsDefeated);
                    actor.ApplyWater(100);
                }
            }
        }
        if (combat.IsPlayerPouncing)
        {
            if (!sawFlight)
            {
                launchedAt = Time.time;
                launchDistance = Vector2.Distance(new Vector2(actor.transform.position.x, actor.transform.position.z),
                    new Vector2(victim.transform.position.x, victim.transform.position.z));
            }
            sawFlight = true;
            if (scenario == 0 && !capturedFlight && actor.GetComponent<LavaSpiritVisual>().VisualRoot.position.z > 1.1f)
            { Capture("02-player-pounce"); capturedFlight = true; }
            if ((scenario == 1 || scenario == 3 || scenario >= 14) && !acted)
            {
                acted = true; actionAt = Time.time;
                if (scenario == 1 || scenario == 14 || scenario == 15 || scenario == 17)
                {
                    var body = victim.GetComponent<CharacterController>(); body.enabled = false;
                    victim.transform.position += Vector3.right * (scenario == 14 || scenario == 15 ? 1.1f : 5f);
                    body.enabled = true; Physics.SyncTransforms();
                }
                else if (scenario == 16) victim.enabled = false;
                else actor.ApplyWater(100);
            }
            if (scenario == 12 && combat.PlayerPounceProgress > .12f && airborneShots < 3 && Time.time - shotAt >= .2f)
            {
                Check("Real pistol accepts airborne shot " + (airborneShots + 1), pistol.Ready && pistol.Fire());
                airborneShots++; shotAt = Time.time;
            }
        }
        if (scenario == 12 && actor.IsDefeated)
        {
            if (finishedAt < 0) finishedAt = Time.time;
            if (Time.time - finishedAt < .18f) return;
            Check("Three real muzzle hits defeat the airborne spirit", airborneShots == 3 && !actor.SelfDetonated && actor.Health == 0 &&
                pistol.LastShotCollider && pistol.LastShotCollider.GetComponentInParent<LavaCreatureActor>() == actor,
                "shots=" + airborneShots + ", health=" + actor.Health);
            Check("Midair kill shatters once and prevents player damage", LavaCombatEffects.ShattersPlayed == shatters + 1 &&
                LavaCombatEffects.ActiveFragments > 0 && victim.Health == before);
            Capture("05-shot-apart-in-flight"); BeginCase(scenario + 1); return;
        }
        if (scenario == 2 || scenario == 3 || scenario == 9)
        {
            if (!acted || Time.time - actionAt < .6f) return;
            Check("Defeated spirit cannot explode on the player", actor.IsDefeated && !actor.SelfDetonated && victim.Health == before);
            BeginCase(scenario + 1); return;
        }
        bool missedCase = scenario == 1 || scenario == 4 || scenario == 14 || scenario == 15 || scenario == 16 || scenario == 17;
        if (missedCase && !retried)
        {
            if (!sawFlight || (finishedAt < 0 && !actor.IsDefeated &&
                (combat.IsPlayerPouncing || combat.State != LavaCreatureCombat.Motion.Recovery))) return;
            if (finishedAt < 0) { finishedAt = Time.time; missLanding = actor.transform.position; }
            if (Time.time - finishedAt < .35f) return;
            Check("Miss leaves a living, visible spirit without an explosion", !actor.IsDefeated && !actor.SelfDetonated &&
                actor.Health == actor.maxHealth && LavaCombatEffects.ShattersPlayed == shatters &&
                actor.GetComponentsInChildren<Collider>().Any(c => c.enabled) &&
                actor.GetComponentsInChildren<Renderer>().Any(r => r.enabled));
            Check("No contact means no player damage", victim.Health == before && victim.HitsReceived == 0 && combat.SuccessfulAttacks == 0);
            Check("Miss ends the attack and settles back to the ground", !combat.IsAttacking &&
                Mathf.Abs(missLanding.y - hopOrigin.y) < .05f, missLanding.ToString());
            if (scenario == 1) Capture("06-dodged-spirit-survives");
            if (scenario == 4) Capture("07-wall-impact-without-explosion");
            if (scenario == 17)
            {
                retried = true; finishedAt = -1f;
                var body = victim.GetComponent<CharacterController>(); body.enabled = false;
                victim.transform.position = actor.transform.position + Vector3.forward * 3.5f;
                body.enabled = true; Physics.SyncTransforms();
            }
            else BeginCase(scenario + 1);
            return;
        }
        if (scenario == 5 && !acted && (actor.SelfDetonated || (second && second.SelfDetonated)))
        {
            acted = true; float health = victim.Health;
            Check("Shared damage gate blocks a simultaneous second hit", !victim.TryTakeDamage(14, Vector3.zero) && victim.Health == health);
        }
        if (!actor.SelfDetonated || (second && !second.SelfDetonated)) return;
        if (finishedAt < 0)
        {
            finishedAt = Time.time;
            feedbackRealAt = Time.unscaledTime;
            Check("Ground charge precedes the pounce", sawCharge && sawFlight && !earlyDamage);
            Check("Impact consumes the spirit and shatters once", actor.IsDefeated && LavaCombatEffects.ShattersPlayed == shatters + (second ? 2 : 1));
            Check("Explosion disables hitboxes and rendering", actor.GetComponentsInChildren<Collider>().All(c => !c.enabled) && actor.GetComponentsInChildren<Renderer>().All(r => !r.enabled));
            if (scenario == 0)
            {
                var feedback = victim.GetComponent<PlayerDamageFeedback>(); var shake = camera.GetComponent<VolcanoCameraShake>();
                Check("Contact immediately triggers a red flash and short camera kick", feedback && feedback.IsVisible && feedback.FlashAlpha > .05f &&
                    feedback.EdgeAlpha > .25f && shake && shake.IsShaking);
                Check("Feedback uses the late-bound camera with legacy HUD disabled", !victim.damageCamera &&
                    !victim.GetComponent<FireRescuePlayer>().showHud && feedback && feedback.IsVisible);
                Check("Damage overlay never intercepts clicks", feedback && !feedback.OverlayCanvas.GetComponent<CanvasGroup>().blocksRaycasts &&
                    feedback.OverlayCanvas.GetComponentsInChildren<UnityEngine.UI.Image>().All(i => !i.raycastTarget));
                Capture("03a-red-impact-flash");
            }
            if (scenario == 11)
                Check("Leap starts beyond ten metres with a readable high arc and airtime", launchDistance > 10f && Time.time - launchedAt > .65f &&
                    maxCoreHeight > 2.7f && combat.Leaps == 1, "reach=" + launchDistance + ", flight=" + (Time.time - launchedAt) + ", peak=" + maxCoreHeight);
            if (scenario == 8) Check("Lethal explosion triggers player defeat once", victim.Health == 0 && defeated == 1);
            else if (scenario == 5) Check("Each spirit can deal damage once, with the shared damage gate", victim.HitsReceived >= 1 && victim.HitsReceived <= 2 &&
                Mathf.Approximately(victim.Health, before - victim.HitsReceived * combat.attackDamage) &&
                combat.SuccessfulAttacks + second.GetComponent<LavaCreatureCombat>().SuccessfulAttacks == victim.HitsReceived, "health=" + victim.Health);
            else Check("One explosion deals exactly the configured damage", victim.HitsReceived == 1 && Mathf.Approximately(victim.Health, before - combat.attackDamage), "health=" + victim.Health);
            if (scenario == 17) Check("Surviving a miss allows a later contact attack", retried && combat.Leaps >= 2 && combat.SuccessfulAttacks == 1);
            hitsAtFinish = victim.HitsReceived;
        }
        if (scenario == 0 && !capturedBurst && Time.time - finishedAt > .08f)
        { Capture("03-contact-explosion"); capturedBurst = true; }
        if (Time.time - finishedAt < .85f) return;
        if (scenario == 0 && Time.unscaledTime - feedbackRealAt < .5f) return;
        Check("The consumed spirit cannot deal repeated damage", victim.HitsReceived == hitsAtFinish);
        if (scenario == 0)
        {
            var feedback = victim.GetComponent<PlayerDamageFeedback>(); var shake = camera.GetComponent<VolcanoCameraShake>();
            Check("Red flash and camera shake fade promptly without drift", feedback && !feedback.IsVisible && !shake.IsShaking &&
                Vector3.Distance(cameraRest, camera.transform.localPosition) < .001f && Quaternion.Angle(cameraRestRotation, camera.transform.localRotation) < .01f);
            Capture("04-feedback-cleared");
        }
        BeginCase(scenario + 1);
    }

    static void CheckPopulation(LavaCreatureEncounter authored)
    {
        void Add(string name, bool passed, string evidence = "")
        { report.checks.Add(new Result { name = "population: " + name, passed = passed, evidence = evidence }); Save(); }
        Add("Saved mission has thirteen spirits in three waves", authored && authored.waveSizes.SequenceEqual(new[] { 4, 4, 5 }) && authored.PlannedCount == 13);
        Add("Saved mission permits nine living spirits with the existing spawn pace", authored && authored.maximumAlive == 9 &&
            Mathf.Approximately(authored.secondsBetweenSpawns, 2.2f) && Mathf.Approximately(authored.secondsBetweenWaves, 20f));
        if (!authored) return;
        var fixture = new GameObject("Temporary population review"); fixture.SetActive(false);
        try
        {
            var source = new GameObject("Population lava feed"); source.transform.SetParent(fixture.transform);
            source.transform.position = new Vector3(100, 0, 100);
            var spline = source.AddComponent<SplineContainer>();
            spline.Spline = new Spline(new[] { new float3(0, 0, 0), new float3(0, 0, 20) }, TangentMode.Linear);
            var lava = source.AddComponent<BK_Lava>(); lava.m_Container = spline;
            lava.m_Width = 2; lava.m_WidthSegments = 2; lava.m_LengthSegments = 4; lava.m_BorderHeight = 0;
            lava.Rebuild();
            var feed = source.AddComponent<ProgressiveLavaFlow>(); feed.source = lava;
            var sequence = fixture.AddComponent<VolcanoEruptionSequence>();
            sequence.enabled = false; sequence.autoErupt = false; sequence.enableCameraShake = false; sequence.showHud = false;
            sequence.warningSeconds = sequence.lavaReleaseDelay = 0;
            sequence.routes = new[] { new VolcanoEruptionSequence.Route { flow = feed } };
            var encounter = fixture.AddComponent<LavaCreatureEncounter>();
            encounter.enabled = false; encounter.enableFinalObjective = false; encounter.sequence = sequence;
            encounter.waveSizes = (int[])authored.waveSizes.Clone(); encounter.maximumAlive = authored.maximumAlive;
            encounter.secondsBetweenSpawns = authored.secondsBetweenSpawns; encounter.secondsBetweenWaves = authored.secondsBetweenWaves;
            var template = Creature(new Vector3(100, 0, 100)); template.transform.SetParent(fixture.transform);
            encounter.spawns = new[] { new LavaCreatureEncounter.Spawn { creature = template, feed = feed, feedProgress = .2f } };
            fixture.SetActive(true);
            sequence.InitializeSequence(); sequence.BeginEruption(true); sequence.TickSequence(.3f);
            int[] released = new int[encounter.WaveCount]; int peak = 0;
            void Step(float seconds)
            {
                int before = encounter.SpawnedCount;
                encounter.TickEncounter(seconds); peak = Mathf.Max(peak, encounter.AliveCount);
                if (encounter.SpawnedCount > before) released[encounter.WaveNumber - 1] += encounter.SpawnedCount - before;
            }
            Step(30);
            Add("Increased waves still wait for the lava", encounter.SpawnedCount == 0);
            feed.SetDistance(feed.Length);
            for (int i = 0; i < 250; i++) Step(.5f);
            Add("Nine living spirits hold the remaining queue", encounter.AliveCount == 9 && encounter.SpawnedCount == 9 && !encounter.AllWavesReleased);
            int held = encounter.SpawnedCount; Step(100);
            Add("Waiting at capacity never drops or bursts the queue", encounter.SpawnedCount == held && encounter.AliveCount == 9);
            for (int i = 0; i < 60 && !encounter.AllWavesReleased; i++)
            { encounter.LivingCreatures[0].ApplyDamage(1000); Step(2.2f); }
            Add("All thirteen release as four, four and five", encounter.SpawnedCount == 13 && released.SequenceEqual(new[] { 4, 4, 5 }), string.Join(",", released));
            Add("Living population never exceeds nine", peak == 9);
            foreach (var living in encounter.LivingCreatures.ToArray()) living.ApplyDamage(1000);
            Step(.1f); Step(1000);
            Add("Clearing the finite waves leaves no extra enemies", encounter.IsCleared && encounter.SpawnedCount == 13 && encounter.DefeatedCount == 13);
        }
        finally { Object.DestroyImmediate(fixture); }
    }

    static void Check(string name, bool passed, string evidence = "")
    { report.checks.Add(new Result { name = Cases[scenario] + ": " + name, passed = passed, evidence = evidence }); Save(); }
    static void Save() => File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
    static void Capture(string name)
    {
        var target = new RenderTexture(1200, 800, 24); var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
        var active = RenderTexture.active; var previous = camera.targetTexture;
        var feedback = victim ? victim.GetComponent<PlayerDamageFeedback>() : null;
        Canvas overlay = feedback ? feedback.OverlayCanvas : null;
        var oldMode = overlay ? overlay.renderMode : RenderMode.ScreenSpaceOverlay;
        try
        {
            if (overlay)
            {
                overlay.renderMode = RenderMode.ScreenSpaceCamera; overlay.worldCamera = camera; overlay.planeDistance = camera.nearClipPlane + .05f;
                Canvas.ForceUpdateCanvases();
            }
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply(); File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
        }
        finally
        {
            if (overlay) { overlay.renderMode = oldMode; overlay.worldCamera = null; Canvas.ForceUpdateCanvases(); }
            camera.targetTexture = previous; RenderTexture.active = active; Object.DestroyImmediate(target); Object.DestroyImmediate(image);
        }
    }
    static void Finish(string error)
    {
        scenario = -1;
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log;
        Time.captureDeltaTime = oldCapture; Time.timeScale = oldScale; Application.runInBackground = oldBackground;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}

// Only the review has automatic aim: shots still use the real pistol, moving hitbox and damage path.
[DefaultExecutionOrder(90)]
public sealed class SpiritPounceAimFixture : MonoBehaviour
{
    public Itfaiye.ItfaiyePistol pistol;
    public LavaCreatureActor actor;
    void LateUpdate()
    {
        if (!pistol || !actor || actor.IsDefeated) return;
        var hitbox = actor.GetComponentInChildren<CapsuleCollider>();
        if (hitbox) pistol.muzzle.rotation = Quaternion.LookRotation(hitbox.bounds.center - pistol.muzzle.position);
    }
}
