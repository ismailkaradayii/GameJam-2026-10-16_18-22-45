using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Integration checks in the saved scene. Reports explicitly identify setup shortcuts.</summary>
[InitializeOnLoad]
public static class FinalLoopChecks
{
    const string Pending = "FinalLoopChecks.Pending";
    const string ReportPath = "Logs/FinalLoop-playmode.txt";
    static readonly List<string> report = new List<string>();
    static VolcanoMissionDirector mission;
    static FireRescuePlayer player;
    static RescueGameFlow flow;
    static NavMeshAgent leader;
    static LavaCreatureActor runner;
    static BurnableBuilding combatHouse;
    static float nextShot;
    static bool realBulletHit, realWaterHit;
    static Transform[] exits;
    static int phase = -1, lastFrame = -1, destination, seenSpawns;
    static float phaseAt, started, maxSpeed, nextReport, defaultFixedStep;
    static double timeout;
    static bool oldBackground, sawVolcano, sawSpirits, sawSlowMotion;
    static string mode, runtimeError;
    static FinalLoopChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
        };
    }
    [MenuItem("GameJam/Final Loop/Run checks")]
    public static void Begin() => Run("complete");
    [MenuItem("GameJam/Final Loop/Run deadline checks")]
    public static void BeginDeadline() => Run("deadline");
    [MenuItem("GameJam/Final Loop/Run vehicle checks")]
    public static void BeginVehicle() => Run("vehicle");
    [MenuItem("GameJam/Final Loop/Run evacuation checks")]
    public static void BeginEvacuation() => Run("evacuation");
    [MenuItem("GameJam/Final Loop/Run combat checks")]
    public static void BeginCombat() => Run("combat");
    [MenuItem("GameJam/Final Loop/Run defense checks")]
    public static void BeginDefense() => Run("defense");
    static void Run(string testMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        FinalLoopSetup.Validate(); SessionState.SetString("FinalLoopChecks.Mode", testMode);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        report.Clear(); runtimeError = null; mode = SessionState.GetString("FinalLoopChecks.Mode", "complete");
        report.Add("Unity Play Mode integration run: " + mode);
        if (mode == "combat") report.Add("LIMITATION: stationary cloned spirit is aligned with the real muzzle ray; the actual first house is ignited and the tank is prefilled for the isolated cannon check.");
        report.Add("LIMITATION: player is placed at interaction points; a temporary NavMesh leader exercises real NPC following. Truck driving is measured physically, then the truck is placed at the lake. This is not an OS-input or end-to-end human driving test.");
        report.Add("LIMITATION: bullet and water APIs are also exercised deterministically for the result checks. Natural spawns, the 120s deadline, both reveals, agent traversal and restart use real frame updates.");
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); player = mission.player; flow = mission.gameFlow;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        defaultFixedStep = Time.fixedDeltaTime; started = Time.time; nextReport = 0; lastFrame = -1;
        sawVolcano = sawSpirits = sawSlowMotion = false; seenSpawns = 0; destination = 0;
        Application.logMessageReceived += Log;
        var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); game.Focus(); game.maximized = true;
        Next(0, 20);
    }
    static void Log(string message, string stack, LogType type)
    {
        if (phase >= 0 && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
        { if (runtimeError == null) { runtimeError = message; report.Add("RUNTIME ERROR: " + message + "\n" + stack); Persist(); } }
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > timeout) throw new TimeoutException("Phase " + phase + " timed out. " + Status());
            if (Time.unscaledTime >= nextReport) { nextReport = Time.unscaledTime + 3; Persist(); }
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.time - phaseAt;
        switch (phase)
        {
            case 0:
                if (mission.cinematic.IsPlaying)
                {
                    if (age < .5f) Require("intro_locks_controls_and_holds_120", player.CinematicLocked && mission.PreparationRemaining == 120, "Intro is unscaled; countdown has not started.");
                    return;
                }
                if (mission.State != VolcanoMissionDirector.FlowState.Preparation) return;
                Require("intro_restores_camera_and_input", player.viewCamera.enabled && !player.CinematicLocked && Time.timeScale == 1, "Player view restored after wake-up.");
                Require("weapon_requires_pickup", !player.HasPistol && !player.pistol.Available && !player.pistol.Equipped, "Slot 1 locked until bedroom pickup.");
                Require("real_scene_counts", mission.evacuation.TotalCount == 7 && flow.TargetCount == 5, "7 actual residents, 5 actual houses.");
                if (mode == "evacuation") Require("starting_house_has_no_residents", mission.evacuation.residents.All(r =>
                    r.door.GetComponentInParent<BurnableBuilding>() != mission.startingHouse.GetComponentInParent<BurnableBuilding>() &&
                    !mission.startingHouse.bounds.Contains(r.transform.position + Vector3.up)), "All 7 residents are assigned to the other four houses.");
                Capture("01-bedroom");
                if (mode == "deadline") { Next(50, 140); return; }
                if (mode == "combat") { Next(60, 30); return; }
                if (mode == "defense") { report.Add("LIMITATION: defense probe starts the eruption immediately; full and deadline modes separately measure the actual 120s countdown."); player.AcquirePistol(); mission.TriggerDeadline(); Next(10, 30); return; }
                if (mode == "vehicle")
                {
                    player.PlaceOnFoot(player.driverDoor.position - Vector3.up * .9f, player.drive.transform.eulerAngles.y);
                    Require("truck_entry", player.EnterVehicle(), player.drive.transform.position.ToString());
                    player.enabled = false; player.drive.SetDriven(true); maxSpeed = 0; Next(5, 15); return;
                }
                Next(1, 10); return;
            case 1:
                if (age < .6f) return;
                Require("pickup_visible", mission.weaponPickup.visual.activeInHierarchy, "Existing A3500X asset on bedside cabinet.");
                Require("nearby_E_interaction_picks_pistol", player.Interact(), "CanPickUp=" + mission.weaponPickup.CanPickUp + " camera=" + player.viewCamera.transform.position + " pickup=" + mission.weaponPickup.transform.position);
                Require("pistol_in_slot_1_once", player.HasPistol && !mission.weaponPickup.visual.activeSelf && !mission.weaponPickup.TryPickUp(), "Pickup disabled after transfer.");
                mission.TickMission();
                Require("pickup_alone_does_not_complete", mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon, "Must still leave house.");
                var door = mission.startingHouse.GetComponentInParent<BurnableBuilding>().GetComponentInChildren<EvacuationDoor>();
                door.Open();
                Next(2, 10); return;
            case 2:
                if (age < 1) return;
                var safeDoor = mission.startingHouse.GetComponentInParent<BurnableBuilding>().GetComponentInChildren<EvacuationDoor>().hinge.GetComponent<InteractiveDoor>();
                Require("first_house_door_opens", !safeDoor || safeDoor.IsOpen, safeDoor ? "Blocked by " + safeDoor.LastBlockingCollider : "Existing evacuation door.");
                var indoorPath = new NavMeshPath();
                bool indoor = NavMesh.SamplePosition(player.transform.position, out var inStart, 3, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(mission.houseExit.position, out var outEnd, 3, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(inStart.position, outEnd.position, NavMesh.AllAreas, indoorPath) && indoorPath.status == NavMeshPathStatus.PathComplete;
                report.Add((indoor ? "PASS " : "OBSERVATION ") + "bedroom_navmesh_route: " + indoorPath.status + "; player uses CharacterController, route traversal remains a manual check.");
                player.PlaceOnFoot(mission.houseExit.position + mission.houseExit.forward, mission.houseExit.eulerAngles.y);
                mission.TickMission();
                Require("exit_skips_church_starts_evacuation", mission.LeftStartingHouse && mission.WarningCompleted && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate && mission.evacuation.IsActive, "No church marker or requirement.");
                player.enabled = false; player.firstPerson.enabled = false; player.playerBody.enabled = false;
                leader = player.gameObject.AddComponent<NavMeshAgent>(); leader.speed = 11.5f; leader.acceleration = 40; leader.angularSpeed = 720;
                leader.radius = .28f; leader.height = 1.8f; leader.avoidancePriority = 1; leader.stoppingDistance = 1;
                if (NavMesh.SamplePosition(player.transform.position, out var mesh, 4, NavMesh.AllAreas)) leader.Warp(mesh.position);
                exits = mission.evacuation.residents.Select(r => r.houseExit).Distinct().ToArray(); destination = 0;
                leader.SetDestination(exits[0].position); Next(3, 100); return;
            case 3:
                if (mission.EruptionTriggered) throw new InvalidOperationException("Evacuation route exceeded the preparation window.");
                if (destination < exits.Length && !leader.pathPending && leader.remainingDistance <= 3)
                {
                    if (mission.evacuation.CooldownRemaining > 0) return;
                    mission.evacuation.Shout(); destination++;
                    leader.SetDestination(destination < exits.Length ? exits[destination].position : mission.evacuation.shelterApproach.position);
                }
                if (destination == exits.Length && !leader.pathPending && leader.remainingDistance <= 3)
                {
                    Require("all_residents_called", mission.evacuation.CalledCount == mission.evacuation.TotalCount, Status());
                    Next(4, 60);
                }
                return;
            case 4:
                if (!mission.evacuation.IsComplete) return;
                Require("real_agents_reach_safe_area", mission.evacuation.residents.All(r => r.IsSafe && mission.evacuation.IsInsideShelter(r.transform.position)), Status());
                if (mode == "evacuation") { Finish(null); return; }
                Capture("02-shelter"); Object.DestroyImmediate(leader); leader = null;
                player.enabled = true; player.firstPerson.enabled = true; player.playerBody.enabled = true;
                player.PlaceOnFoot(player.driverDoor.position - Vector3.up * .9f, player.drive.transform.eulerAngles.y);
                player.RefreshEquipmentState();
                Require("truck_behind_first_house_entry", player.EnterVehicle(), "Truck=" + player.drive.transform.position + "; door=" + player.driverDoor.position);
                mission.TickMission();
                Require("drive_to_lake_objective", mission.Phase == VolcanoMissionDirector.MissionPhase.Refill, mission.Phase.ToString());
                player.enabled = false; player.drive.SetDriven(true); maxSpeed = 0; Next(5, 15); return;
            case 5:
                player.drive.SetInput(.8f, age > 2 ? .3f : 0, false); maxSpeed = Mathf.Max(maxSpeed, player.drive.SpeedKph);
                if (age < 4) return;
                Require("truck_accelerates_on_real_wheels", maxSpeed > 6, "Maximum observed speed=" + maxSpeed + " km/h; grounded wheels=" + player.drive.wheels.Count(w => w.isGrounded));
                player.drive.SetInput(0, 0, true); Next(6, 10); return;
            case 6:
                if (player.drive.SpeedKph > 2 && age < 5) return;
                Require("truck_brakes", player.drive.SpeedKph < 3, "speed=" + player.drive.SpeedKph);
                var body = player.drive.body;
                body.linearVelocity = body.angularVelocity = Vector3.zero;
                body.position = mission.fillStation.transform.position + Vector3.up * .7f;
                body.rotation = Quaternion.Euler(0, 170.54f, 0); Physics.SyncTransforms();
                player.enabled = true; player.SetDriving(true); Next(7, 10); return;
            case 7:
                if (age < 1) return;
                Require("lake_only_valid_submerged_connection", mission.fillStation.Interact(), "Range=" + mission.fillStation.InRange + "; speed=" + player.drive.SpeedKph + "; reach=" + player.hose.CanReach(mission.fillStation.intakePoint.position));
                Capture("03-lake-fill"); Next(8, 20); return;
            case 8:
                if (player.hose.Tank01 < .999f) return;
                if (mission.fillStation.IsFilling) return;
                Require("real_pump_fills_and_retracts", !player.hose.IsDeployed && (mode == "vehicle" || mission.PreparationComplete), "Tank=" + player.hose.TankLitres + "; seconds remaining=" + mission.PreparationRemaining);
                if (mode == "vehicle") { Finish(null); return; }
                player.enabled = true; Next(9, 150); return;
            case 9:
            case 50:
                if (!mission.EruptionTriggered) return;
                Require("deadline_erupts_at_120", mission.PreparationRemaining == 0 && Time.time - started >= 119, "Elapsed=" + (Time.time - started));
                Require("eruption_locks_current_controller", player.CinematicLocked && !player.viewCamera.enabled, "Driving=" + player.IsDriving);
                Require("eruption_one_shot", !mission.sequence.BeginEruption(true), "Second eruption request rejected.");
                if (mode == "deadline") Require("unfinished_preparation_preserved", !player.HasPistol && !mission.evacuation.IsComplete && player.hose.TankLitres == 0, "No automatic pistol, evac or water credit.");
                sawVolcano = true; Next(10, 30); return;
            case 10:
                if (mission.cinematic.IsPlaying)
                { if (mission.cinematic.Elapsed > 3 && mission.cinematic.Elapsed < 3.1f) Capture("04-volcano"); return; }
                Require("eruption_restores_controls", !player.CinematicLocked && player.viewCamera.enabled && Time.timeScale == 1 && !player.drive.body.isKinematic, "Driving=" + player.IsDriving);
                Next(11, 140); return;
            case 11:
                if (mission.cinematic.IsPlaying && mission.cinematic.Kind == VolcanoCinematic.ShotKind.Spirits)
                {
                    sawSpirits = true; sawSlowMotion |= Time.timeScale < 1 && player.CinematicLocked;
                    if (mission.cinematic.Elapsed > 1 && mission.cinematic.Elapsed < 1.12f) Capture("05-spirits");
                    if (mission.cinematic.CanContinueSpirit) mission.cinematic.ContinueSpiritIntroduction();
                    return;
                }
                if (!sawSpirits) return;
                Require("spirit_reveal_slowmo_restored", sawSlowMotion && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, defaultFixedStep) && player.viewCamera.enabled && !player.CinematicLocked, "Input, physics step and timescale restored.");
                if (mode == "deadline")
                {
                    foreach (var house in flow.targetBuildings) { house.Ignite(); house.TickFire(100); }
                    Next(51, 15); return;
                }
                runner = mission.encounter.LivingCreatures.FirstOrDefault();
                Next(12, 180); return;
            case 12:
                // The short mission starts its three fixed fires independently of spirit impact.
                // Legacy missions still let the first spirit reach a house naturally.
                foreach (var actor in mission.encounter.LivingCreatures.ToArray())
                {
                    if (!actor || actor.IsDefeated) continue;
                    if (actor == runner && !flow.HasFixedFireObjective) continue;
                    actor.ApplyBulletDamage(34, actor.transform.position, Vector3.forward);
                    actor.ApplyBulletDamage(34, actor.transform.position, Vector3.forward);
                    actor.ApplyBulletDamage(34, actor.transform.position, Vector3.forward);
                    seenSpawns++;
                }
                foreach (var house in flow.targetBuildings) if (house.IsBurning &&
                    (!runner || runner.IsDefeated || house != runner.GetComponent<LavaCreatureCombat>().BuildingTarget))
                {
                    Require("house_fire_vfx", house.flames && house.flames.isPlaying, house.name);
                    house.ApplyWater(house.litresToExtinguish); seenSpawns = Mathf.Max(1, seenSpawns);
                }
                if (flow.State != RescueGameFlow.GameState.Won) return;
                if (!Object.FindFirstObjectByType<RescueHUD>().VictoryVisible) return;
                if (!flow.HasFixedFireObjective)
                    Require("unshot_spirit_reaches_house", runner && runner.ReachedBuilding && !runner.Dissipated, "First spirit reached its building through real hop movement.");
                Require("finite_waves_and_clear_fires_win", mission.encounter.IsCleared && flow.FireObjectiveComplete && flow.targetBuildings.Any(h => !h.IsBurnedOut) && flow.targetBuildings.All(h => !h.IsBurning), "Planned=" + mission.encounter.PlannedCount + "; spawned=" + mission.encounter.SpawnedCount + "; fires extinguished=" + flow.RescuedCount);
                Require("result_stats_visible", Object.FindFirstObjectByType<RescueHUD>().VictoryVisible, "Result modal.");
                Capture("06-success"); Next(13, 10); return;
            case 13:
                // Time.time is frozen on results; use the real editor deadline to wait for screenshot rendering.
                if (EditorApplication.timeSinceStartup < timeout - 9) return;
                Object.FindFirstObjectByType<RescueHUD>().restartButton.onClick.Invoke(); Next(14, 25); return;
            case 14:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh == mission) return;
                mission = fresh; player = fresh.player; flow = fresh.gameFlow;
                Require("restart_clean_scene", !fresh.EruptionTriggered && !player.HasPistol && fresh.evacuation.RescuedCount == 0 && fresh.encounter.SpawnedCount == 0 && Time.timeScale == 1, "Fresh scene and transient state.");
                Finish(null); return;
            case 60:
                FireCombatChecks.Run();
                Require("water_ballistics_regression", File.ReadAllText("Logs/FireCombatChecks.json").Contains("\"allPassed\": true"), "Existing cannon suite: actual water packets, obstruction, damage and pool checks.");
                player.AcquirePistol(); player.PlaceOnFoot(new Vector3(5, .3f, -570), 0); player.enabled = false; player.pistol.SetAvailable(true); player.pistol.Equip();
                runner = Object.Instantiate(mission.encounter.spawns[0].creature);
                runner.name = "Integration test stationary spirit"; runner.gameObject.SetActive(true);
                runner.GetComponent<LavaCreatureCombat>().enabled = false;
                if (runner.jump) runner.jump.enabled = false;
                foreach (var animator in runner.GetComponentsInChildren<Animator>()) animator.enabled = false;
                nextShot = Time.time + 1; realBulletHit = false; Next(61, 20); return;
            case 61:
                player.firstPerson.SimulateExternalMovement(Vector2.zero, false, false, false, true, 0, Vector2.zero);
                player.pistol.SetExternalInput(true, true, false, false, false, false);
                var hitbox = runner.GetComponentInChildren<CapsuleCollider>();
                if (player.pistol.LastShotCollider && player.pistol.LastShotCollider.GetComponentInParent<LavaCreatureActor>() == runner) realBulletHit = true;
                if (!runner.IsDefeated)
                {
                    // Stationary cloned target is aligned to the actual posed barrel; no direct damage call.
                    runner.transform.position += player.pistol.muzzle.position + player.pistol.muzzle.forward * 12 - hitbox.bounds.center;
                    Physics.SyncTransforms();
                    if (Time.time >= nextShot && player.pistol.Ready) { player.pistol.Fire(); nextShot = Time.time + .4f; }
                    return;
                }
                Require("real_pistol_ray_kills_spirit", realBulletHit && player.pistol.ShotsFired >= 3 && player.pistol.Ammo < player.pistol.magazineSize, "Shots=" + player.pistol.ShotsFired + "; health=" + runner.Health);
                Object.Destroy(runner.gameObject); runner = null;
                player.SetDriving(true);
                Require("truck_exit_restores_pistol", player.ExitVehicle() && player.pistol.Available && player.pistol.Equipped, "Actual exit clearance and previous slot selection.");
                player.PlaceOnFoot(player.driverDoor.position - Vector3.up * .9f, player.drive.transform.eulerAngles.y);
                Require("reenter_after_pistol_combat", player.EnterVehicle(), "Weapon to vehicle transition.");
                player.drive.body.isKinematic = true; player.hose.SetTankLitres(2000);
                combatHouse = flow.targetBuildings.First(h => h.name == "House_Green_Prefab"); combatHouse.Ignite();
                Vector3 target = combatHouse.ignitionBounds.bounds.center;
                Vector3 eye = player.cannon.muzzle.position + Vector3.up * 3;
                player.viewCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                realWaterHit = false; player.cannon.SetSpraying(true); Next(62, 25); return;
            case 62:
                realWaterHit |= player.cannon.HitFire == combatHouse;
                if (!combatHouse.IsRescued) return;
                player.cannon.SetSpraying(false);
                Require("authored_truck_water_extinguishes_real_house", realWaterHit && !combatHouse.IsBurning && player.hose.TankLitres < 2000, "Actual scaled turret, ballistic hits and tank consumption. Remaining litres=" + player.hose.TankLitres);
                flow.Pause(); Next(63, 10); return;
            case 63:
                if (EditorApplication.timeSinceStartup < timeout - 9) return;
                Require("pause_freezes_time_and_input", Time.timeScale == 0 && player.GameplaySuspended && Object.FindFirstObjectByType<RescueHUD>().PauseVisible, "Pause modal after real second.");
                Object.FindFirstObjectByType<RescueHUD>().resumeButton.onClick.Invoke();
                Require("resume_restores_time", Time.timeScale == 1 && !player.GameplaySuspended, "Actual Resume button.");
                Finish(null); return;
            case 51:
                if (flow.State != RescueGameFlow.GameState.Lost) return;
                Require("required_houses_destroyed_lose", flow.HasFixedFireObjective ?
                    flow.defenseFireBuildings.Any(h => h.IsBurnedOut) && flow.DefeatReason == RescueGameFlow.LossReason.RequiredHouseLost :
                    flow.targetBuildings.All(h => h.IsBurnedOut), "Loss does not wait for enemy waves.");
                Capture("07-failed"); Next(52, 10); return;
            case 52:
                if (EditorApplication.timeSinceStartup < timeout - 9) return;
                Object.FindFirstObjectByType<RescueHUD>().victoryMenuButton.onClick.Invoke(); Next(53, 25); return;
            case 53:
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "MainMenu") return;
                Require("main_menu_button_loads_menu", Time.timeScale == 1, "Actual result button onClick."); Finish(null); return;
        }
    }
    static void Next(int next, double seconds) { phase = next; phaseAt = Time.time; timeout = EditorApplication.timeSinceStartup + seconds; Persist(); }
    static void Require(string name, bool passed, string evidence)
    {
        if (!report.Any(x => x.StartsWith("PASS " + name + ":"))) report.Add((passed ? "PASS " : "FAIL ") + name + ": " + evidence);
        if (!passed) throw new InvalidOperationException(name + ": " + evidence);
    }
    static void Capture(string name) => ScreenCapture.CaptureScreenshot("Logs/FinalLoop-" + name + ".png");
    static string Status() => !mission ? "No mission" : "phase=" + phase + " mission=" + mission.Phase + " time=" + mission.PreparationRemaining +
        (mode == "combat" ? " pistolReady=" + player.pistol.Ready + " shots=" + player.pistol.ShotsFired + " health=" + (runner ? runner.Health : -1) + " hit=" + player.pistol.LastShotCollider : "") +
        " rescued=" + mission.evacuation.RescuedCount + "/" + mission.evacuation.TotalCount + " player=" + player.transform.position + (runner ? "\nRUNNER pos=" + runner.transform.position + " state=" + runner.GetComponent<LavaCreatureCombat>().State + " descent=" + runner.GetComponent<LavaCreatureCombat>().DescentProgress + " target=" + runner.GetComponent<LavaCreatureCombat>().BuildingTarget + " reached=" + runner.ReachedBuilding + " dissipated=" + runner.Dissipated : "") +
        "\n" + string.Join("\n", mission.evacuation.residents.Where(r => r).Select(r => r.name + ": " + r.State + " " + r.transform.position + " path=" + (r.Agent ? r.Agent.pathStatus.ToString() : "none")));
    static void Persist() => File.WriteAllText(ReportPath, string.Join("\n", report) + "\nSTATUS " + Status());
    static void Finish(string error)
    {
        if (error != null) report.Add("FAILED: " + error); else report.Add("COMPLETED");
        Persist(); File.Copy(ReportPath, "Logs/FinalLoop-" + mode + "-checks.txt", true);
        phase = -1; Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
        Time.timeScale = 1; Time.fixedDeltaTime = defaultFixedStep;
        EditorApplication.ExitPlaymode();
    }
}
