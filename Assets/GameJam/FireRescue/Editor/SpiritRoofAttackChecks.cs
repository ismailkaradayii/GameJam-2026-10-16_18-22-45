using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real house geometry and moving spirits, in a disposable Play Mode scene.</summary>
[InitializeOnLoad]
public static class SpiritRoofAttackChecks
{
    const string Folder = "Logs/SpiritRoofAttack", Pending = "SpiritRoofAttackChecks.Pending";
    [Serializable] class CheckResult { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status = "RUNNING"; public List<CheckResult> checks = new List<CheckResult>(); public List<string> errors = new List<string>(); }
    [Serializable] class FireRegression { public bool allPassed; }
    static readonly string[] Cases = { "roof impact", "kill during charge", "kill in flight", "nonlethal hit in flight", "wet roof", "water after impact", "lost target", "missing roof collider", "coarse frame steps" };
    static Report report;
    static BurnableBuilding template, house;
    static LavaCreatureActor actor;
    static LavaCreatureCombat combat;
    static Camera camera;
    static int scenario = -1, frame, shatters;
    static float started, impactAt, actionAt, maxHeight, oldCapture, oldScale, groundY;
    static bool acted, sawCharge, sawFlight, sawImpact, prematureFire, chargeCapture, flightCapture, fallCapture, fragmentCapture, oldBackground;
    static double deadline;

    static SpiritRoofAttackChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && scenario >= 0) Finish("Review interrupted");
        };
    }

    [MenuItem("GameJam/Final Loop/Check spirit roof attack")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/report.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    static void Start()
    {
        report = new Report();
        oldCapture = Time.captureDeltaTime; oldScale = Time.timeScale; oldBackground = Application.runInBackground;
        Time.captureDeltaTime = 1f / 60f; Time.timeScale = 1f; Application.runInBackground = true;
        Application.logMessageReceived += Log;
        deadline = EditorApplication.timeSinceStartup + 180;
        try
        {
            template = Object.FindObjectsByType<BurnableBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(b => b.ignitionBounds);
            Physics.SyncTransforms();
            foreach (var building in BurnableBuilding.ActiveBuildings)
            {
                if (!building.ignitionBounds) continue;
                var plan = new SpiritRoofFlight();
                Vector3 from = SpiritOutdoorNavigation.Exterior(building.ignitionBounds, building.transform.position + Vector3.forward * 80, 1.7f);
                plan.Plan(building, from, 3.4f, .35f);
                report.checks.Add(new CheckResult { name = "Scene roof is hittable: " + building.name,
                    passed = plan.HasRoofCollider, evidence = plan.RoofPoint.ToString("F2") });
            }
            for (int i = 0; i < SceneManager.sceneCount; i++)
                foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary spirit roof attack review"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Review ground"; floor.transform.position = new Vector3(0, -.3f, 0); floor.transform.localScale = new Vector3(100, .5f, 100);
            var light = new GameObject("Review sunlight").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.7f; light.transform.rotation = Quaternion.Euler(48, -35, 0);
            camera = new GameObject("Roof attack camera").AddComponent<Camera>(); camera.fieldOfView = 48;
            camera.backgroundColor = new Color(.08f, .11f, .15f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.gameObject.AddComponent<AudioListener>();
            BeginCase(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void BeginCase(int index)
    {
        if (actor) Object.Destroy(actor.gameObject);
        if (house) { house.gameObject.SetActive(false); Object.Destroy(house.gameObject); }
        scenario = index;
        if (index >= Cases.Length) { Finish(null); return; }
        var staging = new GameObject("Inactive staging"); staging.SetActive(false);
        house = Object.Instantiate(template, staging.transform); house.gameObject.SetActive(false);
        house.transform.SetParent(null, true); house.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, 31f, 0));
        house.startBurning = false; house.secondsToCollapse = 600f;
        foreach (var behaviour in house.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour != house) behaviour.enabled = false;
        house.gameObject.SetActive(true);
        // Scene static batches cannot be moved into a clone. Render the source prefab at the
        // same transform while retaining the scene-authored colliders and fire configuration.
        foreach (var renderer in house.GetComponentsInChildren<MeshRenderer>()) renderer.enabled = false;
        foreach (var lod in house.GetComponentsInChildren<LODGroup>()) lod.enabled = false;
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ALP_Assets/country house01/Prefabs/House_Green_Prefab.prefab"), house.transform, false);
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); model.transform.localScale = Vector3.one;
        foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
        foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>()) behaviour.enabled = false;
        if (index == 7)
            foreach (var collider in house.GetComponentsInChildren<Collider>()) if (!collider.isTrigger) collider.enabled = false;
        Physics.SyncTransforms();
        Vector3 approach = SpiritOutdoorNavigation.Exterior(house.ignitionBounds, house.ignitionBounds.bounds.center + Vector3.forward * 80, 1.7f);
        approach.y = .02f;
        actor = Creature(approach); combat = actor.GetComponent<LavaCreatureCombat>(); combat.attackBuildings = true;
        combat.maximumLifeSeconds = 30f;
        Object.Destroy(staging);
        Bounds b = house.ignitionBounds.bounds;
        camera.transform.position = approach + new Vector3(22, 14, 26);
        camera.transform.LookAt(Vector3.Lerp(approach, b.center, .35f) + Vector3.up * 9);
        started = Time.time; impactAt = actionAt = -1f; maxHeight = groundY = actor.transform.position.y;
        acted = sawCharge = sawFlight = sawImpact = prematureFire = chargeCapture = flightCapture = fallCapture = fragmentCapture = false;
        shatters = LavaCombatEffects.ShattersPlayed;
        Save();
    }

    static LavaCreatureActor Creature(Vector3 position)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LavaCreature/Prefabs/LavaCreature.prefab");
        var root = Object.Instantiate(prefab); root.name = "Roof attack spirit"; root.transform.position = position;
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
        Bounds bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.localScale *= 1.8f / bounds.size.y;
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        root.transform.position += Vector3.up * (position.y - bounds.min.y);
        bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var hitbox = new GameObject("Review hit capsule"); hitbox.transform.position = bounds.center;
        hitbox.transform.SetParent(root.transform.Find("SomersaultVisual"), true);
        var capsule = hitbox.AddComponent<CapsuleCollider>(); capsule.radius = .35f; capsule.height = 1.8f;
        var result = root.AddComponent<LavaCreatureActor>(); Physics.SyncTransforms(); return result;
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
        float age = Time.time - started;
        if (scenario == 7 && actor.Dissipated)
        {
            Check("Missing roof geometry cannot cause a floating impact or remote fire", !house.IsBurning && !actor.ReachedBuilding);
            BeginCase(scenario + 1); return;
        }
        if (actor.Dissipated && scenario != 6)
        {
            Check("Flight reaches the roof without a false obstruction", false,
                "hit=" + combat.RoofContactCollider + ", maxHeight=" + maxHeight + ", roof=" + combat.RoofImpactPoint);
            BeginCase(scenario + 1); return;
        }
        if (age > 14f)
        {
            Check("Completes within 14 seconds", false, "state=" + combat.State + ", position=" + actor.transform.position + ", roof=" + combat.RoofImpactPoint + ", defeated=" + actor.IsDefeated);
            BeginCase(scenario + 1); return;
        }
        if (scenario == 8 && age > .1f) Time.captureDeltaTime = .1f;
        maxHeight = Mathf.Max(maxHeight, actor.transform.position.y);
        if (house.IsBurning && !sawImpact && !actor.ReachedBuilding) prematureFire = true;
        if (combat.State == LavaCreatureCombat.Motion.Igniting)
        {
            sawCharge = true;
            if (scenario == 0 && combat.RoofAttackCharge > .7f && !chargeCapture)
            { Capture("01-ground-charge"); chargeCapture = true; }
            if (scenario == 1 && combat.RoofAttackCharge > .5f && !acted)
            { actor.ApplyBulletDamage(1000, actor.transform.position, Vector3.forward); acted = true; actionAt = Time.time; }
        }
        if (combat.State == LavaCreatureCombat.Motion.RoofLeaping)
        {
            sawFlight = true;
            float p = combat.RoofFlightProgress;
            if (scenario == 0 && p > .42f && !flightCapture) { Capture("02-airborne"); flightCapture = true; }
            if (scenario == 0 && p > .84f && !fallCapture) { Capture("03-roof-dive"); fallCapture = true; }
            if (p > .32f && !acted && scenario >= 2 && scenario <= 6 && scenario != 5)
            {
                acted = true; actionAt = Time.time;
                if (scenario == 2) actor.ApplyBulletDamage(1000, actor.transform.position, Vector3.forward);
                if (scenario == 3) actor.ApplyBulletDamage(15, actor.transform.position, Vector3.forward);
                if (scenario == 4) { house.wetProtectionSeconds = 5f; house.ApplyWater(1f); }
                if (scenario == 6) house.gameObject.SetActive(false);
            }
        }
        if (actor.ReachedBuilding && !sawImpact)
        {
            sawImpact = true; impactAt = Time.time;
            Check("No fire before roof contact", !prematureFire && !house.IsBurning);
            Check("Breaks apart immediately on contact", actor.IsDefeated && LavaCombatEffects.ShattersPlayed == shatters + 1 && LavaCombatEffects.ActiveFragments > 0);
            if (scenario == 0) Capture("04-roof-shatter");
            if (scenario == 5) { house.wetProtectionSeconds = 5f; house.ApplyWater(1f); acted = true; }
        }
        if (scenario == 0 && sawImpact && !fragmentCapture && Time.time - impactAt >= .08f)
        { Capture("04b-fragments-before-fire"); fragmentCapture = true; Check("Fragments precede house fire", !house.IsBurning); }
        if (scenario == 0 || scenario == 3 || scenario == 8)
        {
            if (!sawImpact || Time.time - impactAt < .65f) return;
            Check("Ground charge and airborne arc are visible", sawCharge && sawFlight && maxHeight > combat.RoofImpactPoint.y + 1f,
                "highest=" + maxHeight.ToString("F2") + ", roof=" + combat.RoofImpactPoint.ToString("F2") + ", ground=" + groundY.ToString("F2"));
            Check("House ignites after the impact", house.IsBurning);
            Check("Consumed spirit has no active hitboxes", actor.GetComponentsInChildren<Collider>().All(c => !c.enabled));
            if (scenario == 3) Check("Surviving a hit preserves the committed flight", acted && sawImpact);
            if (scenario == 0) Capture("05-roof-fire");
            if (scenario == 8) Time.captureDeltaTime = 1f / 60f;
            BeginCase(scenario + 1);
        }
        else if (scenario == 4 || scenario == 5)
        {
            if (!sawImpact || Time.time - impactAt < .5f) return;
            Check("Water protection prevents delayed ignition", acted && !house.IsBurning && !prematureFire);
            BeginCase(scenario + 1);
        }
        else if (acted && Time.time - actionAt > 1.2f)
        {
            Check("Interrupted attack cannot ignite a house", !house.IsBurning && !actor.ReachedBuilding && !prematureFire);
            Check("Killed or targetless spirit is retired", actor.IsDefeated);
            BeginCase(scenario + 1);
        }
    }

    static void Check(string name, bool passed, string evidence = "")
    { report.checks.Add(new CheckResult { name = Cases[scenario] + ": " + name, passed = passed, evidence = evidence }); Save(); }
    static void Save() => File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
    static void Capture(string name)
    {
        var target = new RenderTexture(1200, 800, 24); var image = new Texture2D(1200, 800, TextureFormat.RGB24, false);
        var active = RenderTexture.active; var previous = camera.targetTexture;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1200, 800), 0, 0); image.Apply(); File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
        }
        finally { camera.targetTexture = previous; RenderTexture.active = active; Object.DestroyImmediate(target); Object.DestroyImmediate(image); }
    }
    static void Finish(string error)
    {
        scenario = -1;
        if (error != null) report.errors.Add(error);
        if (error == null)
        {
            try
            {
                FireCombatChecks.Run();
                bool firePassed = JsonUtility.FromJson<FireRegression>(File.ReadAllText("Logs/FireCombatChecks.json")).allPassed;
                report.checks.Add(new CheckResult { name = "Existing fire and water regression", passed = firePassed });
            }
            catch (Exception ex) { report.errors.Add(ex.ToString()); }
        }
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log;
        Time.captureDeltaTime = oldCapture; Time.timeScale = oldScale; Application.runInBackground = oldBackground;
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
