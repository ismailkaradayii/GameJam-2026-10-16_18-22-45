using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real humanoids, lava geometry, water packets and a spirit pounce in a disposable scene.</summary>
[InitializeOnLoad]
public static class ThermalHazardChecks
{
    const string Folder = "Logs/ThermalHazards", Pending = "ThermalHazardChecks.Pending";
    [Serializable] class Check { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status = "RUNNING"; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static int phase = -1, frame;
    static float started, phaseAt, oldCapture, oldScale, waterHealth, truckSpeed, truckFlow, firstLitres;
    static bool oldBackground, capturedBurn, capturedCorpse, paused;
    static double deadline, pausedAt;
    static GameObject residentTemplate, truckTemplate;
    static EvacuationResident late, afterburn, watered, safe;
    static EvacuationMission mission;
    static FireRescuePlayer originalPlayer;
    static FireTruckWaterCannon waterCannon, truckCannon;
    static FireHoseSystem waterTank, truckTank;
    static FireTruckDrive truck;
    static LavaCreatureActor spirit;
    static CreatureCombatPlayer driver;
    static Camera camera;
    static NavMeshDataInstance navigation;
    static readonly List<GameObject> fixtures = new List<GameObject>();

    static ThermalHazardChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (s == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Checks interrupted");
        };
    }
    [MenuItem("GameJam/Final Loop/Check civilian and vehicle fire")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/report.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        report = new Report(); oldCapture = Time.captureDeltaTime; oldScale = Time.timeScale;
        oldBackground = Application.runInBackground; Time.captureDeltaTime = 1f / 30f; Time.timeScale = 1;
        Application.runInBackground = true; Application.logMessageReceived += Log;
        deadline = EditorApplication.timeSinceStartup + 180;
        try
        {
            var originalMission = Object.FindFirstObjectByType<EvacuationMission>();
            residentTemplate = originalMission.residents[0].gameObject;
            originalPlayer = Object.FindFirstObjectByType<FireRescuePlayer>(); truckTemplate = originalPlayer.drive.gameObject;
            for (int s = 0; s < SceneManager.sceneCount; s++) foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects()) root.SetActive(false);
            SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary thermal hazard review"));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Review ground";
            floor.transform.position = new Vector3(0, -.15f, 0); floor.transform.localScale = new Vector3(100, .3f, 100);
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.color = new Color(.16f, .2f, .21f); floor.GetComponent<Renderer>().sharedMaterial = m;
            var sources = new List<NavMeshBuildSource> { new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(80, .3f, 80), transform = Matrix4x4.TRS(new Vector3(0, -.15f, 0), Quaternion.identity, Vector3.one), area = 0 } };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, new Vector3(100, 20, 100)), Vector3.zero, Quaternion.identity);
            navigation = NavMesh.AddNavMeshData(data);
            var light = new GameObject("Review sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.6f;
            light.transform.rotation = Quaternion.Euler(45, -25, 0); RenderSettings.ambientLight = new Color(.45f, .5f, .6f);
            camera = new GameObject("Thermal review camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.045f, .06f, .08f); camera.fieldOfView = 48; camera.gameObject.AddComponent<AudioListener>();
            mission = Fixture("Civilian test mission").AddComponent<EvacuationMission>(); mission.enabled = false;
            mission.startAutomatically = false; mission.player = originalPlayer; mission.showHud = false;
            late = Resident("Late evacuation", new Vector3(-8, 0, 0));
            afterburn = Resident("Persistent clothing fire", new Vector3(-3, 0, 0));
            watered = Resident("Water rescue", new Vector3(2, 0, 0));
            safe = Resident("Sheltered civilian", new Vector3(7, 0, 0));
            var shelter = Fixture("Safe shelter area").AddComponent<BoxCollider>(); shelter.isTrigger = true;
            shelter.transform.position = safe.transform.position; shelter.center = Vector3.up * 2; shelter.size = new Vector3(3, 8, 3);
            mission.shelterSafeArea = shelter; mission.shelterApproach = shelter.transform; mission.shelterEntrance = shelter.transform;
            safe.safePlace = shelter.transform;
            mission.residents = new[] { late, afterburn, watered, safe }; mission.BeginMission();
            Verify("A late evacuation call is accepted", late.CallOutside()); safe.CallOutside();
            Lava(new Vector3(-8, .12f, 0), false);
            afterburn.Thermal.Ignite(); watered.Thermal.Ignite();
            waterTank = Fixture("Water test supply").AddComponent<FireHoseSystem>(); waterTank.SetTankLitres(500);
            waterCannon = waterTank.gameObject.AddComponent<FireTruckWaterCannon>(); waterCannon.tank = waterTank;
            var muzzle = new GameObject("Water rescue muzzle").transform; muzzle.SetParent(waterTank.transform);
            Physics.SyncTransforms();
            Vector3 aim = watered.transform.TransformPoint(watered.GetComponent<CapsuleCollider>().center);
            muzzle.position = aim + new Vector3(0, .25f, -6); muzzle.rotation = Quaternion.LookRotation(aim - muzzle.position + Vector3.up * .2f);
            waterCannon.muzzle = muzzle; waterCannon.SetSpraying(true);
            Verify("Water aim crosses the civilian's actual hitbox", Physics.RaycastAll(muzzle.position, muzzle.forward, 8f, ~0, QueryTriggerInteraction.Collide)
                .Any(h => h.collider.GetComponentInParent<EvacuationResident>() == watered), "muzzle=" + muzzle.position + " aim=" + aim);
            camera.transform.position = new Vector3(-1, 9, -19); camera.transform.LookAt(new Vector3(-1, 1.4f, 0));
            GeometryChecks();
            started = phaseAt = Time.time; phase = 0; Save();
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static GameObject Fixture(string name) { var go = new GameObject(name); fixtures.Add(go); return go; }
    static EvacuationResident Resident(string label, Vector3 position)
    {
        var root = Object.Instantiate(residentTemplate, position, Quaternion.identity); root.name = label; fixtures.Add(root);
        var r = root.GetComponent<EvacuationResident>(); r.mission = mission; r.reactionSeconds = .01f; r.formationIndex = fixtures.Count;
        r.transform.position = position; r.Agent.Warp(position); r.Agent.isStopped = true;
        r.houseExit = Fixture(label + " exit").transform; r.houseExit.position = position;
        return r;
    }
    static LavaIgnitionSource Lava(Vector3 position, bool gap)
    {
        var go = Fixture(gap ? "Two separated lava patches" : "Hot lava patch"); go.transform.position = position;
        var mesh = new Mesh();
        if (!gap)
        {
            mesh.vertices = new[] { new Vector3(-1.5f, 0, -1.5f), new Vector3(-1.5f, 0, 1.5f), new Vector3(1.5f, 0, 1.5f), new Vector3(1.5f, 0, -1.5f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }
        else
        {
            mesh.vertices = new[] { new Vector3(-5, 0, -1), new Vector3(-5, 0, 1), new Vector3(-3, 0, 1), new Vector3(-3, 0, -1),
                new Vector3(3, 0, -1), new Vector3(3, 0, 1), new Vector3(5, 0, 1), new Vector3(5, 0, -1) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        }
        mesh.RecalculateNormals(); mesh.RecalculateBounds(); go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>(); var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(1, .16f, .015f); mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", new Color(2, .15f, .01f)); renderer.sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh; return go.AddComponent<LavaIgnitionSource>();
    }
    static void GeometryChecks()
    {
        var source = Lava(new Vector3(20, .15f, 0), true);
        var body = Fixture("Geometry heat probe"); var capsule = body.AddComponent<CapsuleCollider>();
        capsule.center = Vector3.up; capsule.height = 2; capsule.radius = .35f;
        var thermal = body.AddComponent<ThermalDamage>(); body.transform.position = new Vector3(20, 0, 0); Physics.SyncTransforms();
        thermal.TickHeat(.1f); Verify("Empty space inside a lava mesh's bounds does not ignite", !thermal.IsBurning);
        body.transform.position = new Vector3(24, 0, 0); Physics.SyncTransforms(); thermal.TickHeat(.1f);
        Verify("Actual concave lava mesh contact ignites", thermal.IsBurning && thermal.Health < 100);
        var raised = Resident("Civilian NavMesh clearance probe", new Vector3(24, 0, 0));
        raised.transform.position = new Vector3(24, .27f, 0); Physics.SyncTransforms();
        Verify("NavMesh clearance above lava still burns civilian feet", raised.Thermal.CheckLavaContact(),
            "feet=" + raised.GetComponent<Collider>().bounds.min.y + " lava=" + source.heatCollider.bounds.max.y);
        raised.gameObject.SetActive(false);
        thermal.ApplyWater(100); var cooling = source.gameObject.AddComponent<LavaCoolingSurface>();
        cooling.ApplyWater(new Vector3(24, .15f, 0), 1000); thermal.TickHeat(.1f);
        Verify("Water-cooled lava does not reignite a wet victim", !thermal.InHotLava && !thermal.IsBurning);
        source.heatEnabled = false; body.SetActive(false); source.gameObject.SetActive(false);
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Thermal checks timed out"); return; }
            Tick();
        }
        catch (Exception e) { Finish(e.ToString()); }
    }
    static void Tick()
    {
        if (paused)
        {
            if (EditorApplication.timeSinceStartup - pausedAt < .3) return;
            Verify("Burn voice and crackle pause with the game", afterburn.GetComponent<ThermalBurnVisual>().AudioPaused);
            Time.timeScale = 1; paused = false; phase = 2; return;
        }
        float age = Time.time - started;
        if (phase < 3)
        {
            if (!capturedBurn && age > .4f)
            {
                capturedBurn = true;
                Verify("Fire uses a retargeted Mixamo animation", afterburn.animator.GetCurrentAnimatorStateInfo(0).IsName("Burning") &&
                    afterburn.animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.humanMotion));
                Capture("01-burning-villagers-and-water-rescue");
            }
            if (phase == 0 && age > 2.3f)
            {
                waterCannon.SetSpraying(false); waterHealth = watered.Thermal.Health;
                Verify("A villager called after lava arrives burns to death", late.IsDead && !late.Agent.enabled && late.Thermal.IsDestroyed);
                Verify("Dead villagers are counted as lost, never rescued", mission.DeadCount == 1 && mission.RescuedCount == 1 && !late.IsSafe && !late.CallOutside());
                Verify("Shelter protects evacuated villagers", safe.IsSafe && safe.Thermal.Health == 100 && !safe.Thermal.IsBurning);
                Verify("Real ballistic water hits extinguish a civilian trigger", !watered.Thermal.IsBurning && !watered.IsDead && waterTank.TankLitres < 490,
                    "health=" + waterHealth + " litres=" + waterTank.TankLitres + " muzzle=" + waterCannon.muzzle.position +
                    " packet=" + waterCannon.NewestPacketPosition + " victim=" + watered.transform.position);
                Verify("Burn pain and death emit spatial screams", late.GetComponent<ThermalBurnVisual>().ScreamsPlayed >= 2 &&
                    afterburn.GetComponent<ThermalBurnVisual>().ScreamsPlayed >= 1);
                float before = afterburn.Thermal.Health, litres = waterTank.TankLitres;
                Time.timeScale = 0; afterburn.Thermal.TickHeat(1); waterCannon.TickSpray(1);
                Verify("Pause freezes heat and water consumption", afterburn.Thermal.Health == before && waterTank.TankLitres == litres);
                pausedAt = EditorApplication.timeSinceStartup; paused = true; phase = 1; return;
            }
            if (phase == 2 && age > 7.8f)
            {
                Verify("Clothing fire remains lethal after leaving lava", afterburn.IsDead && afterburn.Thermal.IsDestroyed);
                Verify("Water saves a civilian without repairing lost health", !watered.IsDead && Mathf.Abs(watered.Thermal.Health - waterHealth) < .01f);
                Verify("The dead body holds a collapsed pose", late.animator.speed == 0 && Quaternion.Angle(late.animator.transform.localRotation, Quaternion.identity) > 50);
                Verify("The corpse settles near the floor without a scale-induced jump", CorpseOnGround(afterburn.animator),
                    "modelHeight=" + afterburn.animator.transform.position.y + " headHeight=" + afterburn.animator.GetBoneTransform(HumanBodyBones.Head).position.y);
                Verify("Losses do not block the shelter queue", mission.CanEnterShelter(late) == false && mission.DeadCount == 2 && mission.RescuedCount == 1);
                Capture("02-collapsed-villagers-and-survivors"); capturedCorpse = true;
                foreach (var fixture in fixtures) if (fixture) fixture.SetActive(false);
                BeginTruck(); phase = 3; phaseAt = Time.time; return;
            }
            return;
        }
        if (phase == 3)
        {
            if ((!truck.Thermal.IsBurning || !spirit.SelfDetonated) && Time.time - phaseAt < 12) return;
            Verify("A spirit actively pounces on the occupied fire truck", truck.Thermal.IsBurning && spirit.SelfDetonated,
                "state=" + spirit.GetComponent<LavaCreatureCombat>().State + " destroyedSpirit=" + spirit.SelfDetonated);
            Verify("The cab shields the driver while the vehicle takes fire damage", driver.Health == driver.maximumHealth && truck.Thermal.Health < truck.Thermal.maximumHealth);
            truckCannon.SetSpraying(true); truckCannon.TickSpray(1); firstLitres = 1000 - truckTank.TankLitres;
            truckSpeed = truckCannon.LaunchVelocity.magnitude; truckFlow = firstLitres;
            Verify("Minor vehicle damage preserves full motor and pump output", truck.MotorEfficiency == 1f && truck.Thermal.PumpEfficiency == 1f);
            for (int i = 0; i < 560; i++) truck.Thermal.TickHeat(.25f);
            truckTank.SetTankLitres(1000); truckCannon.TickSpray(1);
            float damagedFlow = 1000 - truckTank.TankLitres;
            Verify("Engine output and vehicle top speed fall with permanent damage", truck.MotorEfficiency < .8f && truck.EffectiveForwardSpeedKph < truck.maxForwardSpeedKph * .8f);
            Verify("Actual cannon output loses flow and launch pressure", damagedFlow < truckFlow * .9f && truckCannon.LaunchVelocity.magnitude < truckSpeed * .95f,
                "flow=" + truckFlow + " -> " + damagedFlow + " speed=" + truckSpeed + " -> " + truckCannon.LaunchVelocity.magnitude);
            truckCannon.SetSpraying(false);
            phase = 4; phaseAt = Time.time; return;
        }
        if (phase == 4 && Time.time - phaseAt > .8f)
        {
            Capture("03-burning-truck-after-spirit-impact");
            float damagedHealth = truck.Thermal.Health;
            truckTank.SetTankLitres(1000);
            for (int i = 0; i < 10; i++) truck.Thermal.SuppressFromTank(truckTank, .25f);
            truck.Thermal.TickHeat(.2f);
            Verify("Onboard suppression spends real tank water without repairing damage", !truck.Thermal.IsBurning && truckTank.TankLitres < 1000 && truckTank.TankLitres >= 950 && Mathf.Abs(truck.Thermal.Health - damagedHealth) < .01f);
            // Test actual wheel contact rather than an oversized chassis heat volume.
            var wheel = truck.wheels.First(w => w); wheel.GetWorldPose(out var p, out _);
            float radius = wheel.radius * Mathf.Max(.01f, wheel.transform.lossyScale.y);
            var lava = Lava(new Vector3(p.x, p.y - radius + .08f, p.z), false); Physics.SyncTransforms();
            truck.Thermal.TickHeat(.1f);
            Verify("Shallow lava touching a tyre ignites the fire truck", truck.Thermal.InHotLava && truck.Thermal.IsBurning);
            truckTank.SetTankLitres(0);
            Verify("An empty tank cannot suppress the truck fire", truck.Thermal.SuppressFromTank(truckTank, .25f) == 0 && truck.Thermal.IsBurning);
            for (int i = 0; i < 80; i++) truck.Thermal.TickHeat(.25f);
            Verify("A burned-out truck loses its motor and pump", truck.IsWrecked && truck.MotorEfficiency == 0 && truckCannon.EffectiveFlow == 0);
            truckTank.SetTankLitres(1000); truckCannon.SetSpraying(true); truckCannon.TickSpray(1);
            Verify("A destroyed pump emits no water and consumes none", !truckCannon.IsSpraying && truckTank.TankLitres == 1000);
            phase = 5; phaseAt = Time.time; return;
        }
        if (phase == 5 && Time.time - phaseAt > 1f)
        {
            Capture("04-charred-disabled-truck");
            FireCombatChecks.Run();
            Verify("Existing fire and water cannon regression remains green", File.ReadAllText("Logs/FireCombatChecks.json").Contains("\"allPassed\": true"));
            Finish(null);
        }
    }
    static void BeginTruck()
    {
        var root = Object.Instantiate(truckTemplate); root.name = "Actual fire truck under spirit attack";
        root.transform.rotation = Quaternion.identity; root.transform.position = Vector3.zero;
        truck = root.GetComponent<FireTruckDrive>(); truck.body.constraints = RigidbodyConstraints.FreezeAll;
        // Exercise prolonged damage and manual suppression here; the automatic system has its own rescue regression.
        truck.Thermal.automaticSuppressionLitresPerSecond = 0;
        truck.SetDriven(true); truckTank = root.GetComponent<FireHoseSystem>(); truckTank.SetTankLitres(1000);
        truckCannon = root.GetComponent<FireTruckWaterCannon>(); truckCannon.enabled = false; truckCannon.aimCamera = null;
        var human = new GameObject("Driver inside cab");
        human.transform.position = root.transform.position;
        var coordinator = human.AddComponent<FireRescuePlayer>(); coordinator.enabled = false; coordinator.showHud = false; coordinator.drive = truck;
        typeof(FireRescuePlayer).GetField("<IsDriving>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(coordinator, true);
        driver = human.AddComponent<CreatureCombatPlayer>(); driver.allowRecovery = false;
        var bounds = root.GetComponent<Collider>().bounds;
        Vector3 spawn = new Vector3(bounds.center.x, 0, bounds.max.z + 7f);
        spirit = Spirit(spawn); spirit.GetComponent<LavaCreatureCombat>().target = driver;
        camera.transform.position = bounds.center + new Vector3(bounds.size.x * 1.3f + 8, bounds.size.y + 8, bounds.size.z * .8f + 15);
        camera.transform.LookAt(bounds.center); Physics.SyncTransforms();
    }
    static LavaCreatureActor Spirit(Vector3 position)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/LavaCreature/Prefabs/LavaCreature.prefab"));
        root.transform.position = position;
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer) && !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
        Bounds b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds);
        root.transform.localScale *= 1.8f / b.size.y;
        b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); root.transform.position += Vector3.up * (position.y - b.min.y);
        var hit = new GameObject("Spirit hit capsule"); hit.transform.position = position + Vector3.up * .9f;
        hit.transform.SetParent(root.transform.Find("SomersaultVisual"), true); var c = hit.AddComponent<CapsuleCollider>(); c.height = 1.8f; c.radius = .35f;
        return root.GetComponent<LavaCreatureActor>() ?? root.AddComponent<LavaCreatureActor>();
    }
    static void Verify(string name, bool passed, string evidence = "") { report.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Save(); }
    static bool CorpseOnGround(Animator animator)
    {
        float min = float.PositiveInfinity;
        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
        {
            var bone = animator.GetBoneTransform((HumanBodyBones)i);
            if (bone) min = Mathf.Min(min, bone.position.y);
        }
        return min >= .01f && min <= .6f && animator.transform.position.y < 1.5f;
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { report.errors.Add(message + "\n" + stack); Save(); }
    }
    static void Save() { if (report != null) File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true)); }
    static void Capture(string name)
    {
        var rt = new RenderTexture(1440, 900, 24); var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var texture = new Texture2D(1440, 900, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0); texture.Apply();
        File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG());
        camera.targetTexture = oldTarget; RenderTexture.active = previous; Object.Destroy(texture); rt.Release(); Object.Destroy(rt);
    }
    static void Finish(string error)
    {
        if (report == null) report = new Report();
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) && capturedBurn && capturedCorpse ? "SUCCESS" : "FAILED";
        Save(); phase = -1; Application.logMessageReceived -= Log;
        if (navigation.valid) navigation.Remove();
        Time.captureDeltaTime = oldCapture; Time.timeScale = oldScale; Application.runInBackground = oldBackground;
        EditorApplication.ExitPlaymode();
    }
}
