using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashfall.MainMenu;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>One continuous, unaccelerated run through the authored scene. Never ignites buildings or advances lava by hand.</summary>
[InitializeOnLoad]
public static class VolcanoPlaythroughChecks
{
    const string MenuPath = "Assets/Scenes/MainMenu.unity";
    const string GamePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string ReportPath = "Logs/VolcanoPlaythroughChecks.json";
    const string Prefix = "GameJam.VolcanoPlaythrough.";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] public sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] public sealed class House
    {
        public string name;
        public Vector3 position, firingPosition;
        public float ignitedAt = -1, extinguishedAt = -1, reignitedAt = -1, litresUsed, spraySeconds;
        public bool burningEffectsObserved, stoppedEffectsObserved, physicalHitObserved;
    }
    [Serializable] public sealed class Report
    {
        public string status, phase, startedUtc, finishedUtc, error, editorScene, eruptionStage;
        public bool allPassed;
        public float gameSeconds, flowSeconds, tankLitres, realSeconds, truckSpeedKph, maximumFrameSeconds, averageFrameSeconds;
        public int burningHouses;
        public List<Check> checks = new List<Check>();
        public List<House> houses = new List<House>();
        public List<string> runtimeErrors = new List<string>();
        public List<string> setupLimitations = new List<string>();
    }
    static Report report;
    static FireRescuePlayer player;
    static FireTruckDrive drive;
    static FireHoseSystem hose;
    static FireTruckWaterCannon cannon;
    static VolcanoEruptionSequence sequence;
    static RescueGameFlow gameFlow;
    static RescueHUD hud;
    static WaterSourceVolume lake;
    static BurnableBuilding[] houses;
    static Vector3 truckSpawn, driveStart, steerStartForward;
    static Quaternion truckSpawnRotation;
    static float gameStart, phaseEnd, maxSpeed, maxSteer, turnDegrees, reverseDistance, initialTank, fillingStart, fillingTank;
    static float sprayStart, sprayTank, maxSprayHeat, originalTimeScale, settledTruckClearance;
    static int targetIndex, candidateIndex, candidateFrame;
    static int sampledFrame = -1, frameSamples;
    static float frameSum;
    static bool sprayPauseChecked, firstRunDone;
    static string afterPause, pauseLabel;
    static float afterPauseDuration, pauseRealStart, pauseFlowSeconds, pauseTank, victoryHoldStart;
    static Vector3 pauseTruckPosition;
    static bool pauseWeaponEquipped, pausePistolHud, pauseKneeling, pauseReloading;
    static float pauseKneelAmount, pauseCapsuleHeight, pauseReloadProgress;
    static int kneelFrame = -1;
    static int pauseAmmo;
    static bool oldBackground, oldPlayerEnabled, oldKinematic, observedWarning, observedEruption, observedFlow;
    static double deadline, lastPersist;
    static readonly List<Vector3> firingCandidates = new List<Vector3>();

    static VolcanoPlaythroughChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Application.logMessageReceived += LogReceived;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreEnvironment;
    }
    [MenuItem("GameJam/Integration/Run continuous Volcano playthrough", true)]
    static bool CanBegin() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !SessionState.GetBool(Prefix + "Active", false);
    [MenuItem("GameJam/Integration/Run continuous Volcano playthrough")]
    public static void Begin()
    {
        if (!CanBegin()) throw new InvalidOperationException("Run the playthrough from Edit Mode after compilation finishes.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before running the playthrough.");
        report = new Report { status = "running", startedUtc = DateTime.UtcNow.ToString("O") };
        sprayPauseChecked = firstRunDone = observedWarning = observedEruption = observedFlow = false;
        frameSamples = 0; frameSum = 0; sampledFrame = -1;
        report.setupLimitations.Add("The actual menu button and gameplay APIs are exercised. OS keyboard/mouse input is not simulated in this suite.");
        report.setupLimitations.Add("After real dynamic driving, the truck is restored to its authored spawn. The player is placed at the door, rear socket and a validated dry shoreline instead of walking the entire route.");
        report.setupLimitations.Add("For each actual house, a terrain firing station is selected; the truck is held stationary there and its original camera is aimed. This tests real turret limits, collision hits and automatic water delivery, not road navigation to every house.");
        EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
        SessionState.SetBool(Prefix + "Active", true);
        SessionState.SetString(Prefix + "Phase", "WaitMenu");
        SessionState.SetString(Prefix + "Deadline", (EditorApplication.timeSinceStartup + 100).ToString("R", CultureInfo.InvariantCulture));
        Persist();
        EditorApplication.EnterPlaymode();
    }
    static Report Current => report ?? (report = JsonUtility.FromJson<Report>(SessionState.GetString(Prefix + "Report", "{}")) ?? new Report());
    static string Phase => SessionState.GetString(Prefix + "Phase", "");
    static float Elapsed => Time.time - gameStart;
    static void PhaseTo(string phase, float gameSeconds, double maximumWallSeconds = 90)
    {
        phaseEnd = Time.time + gameSeconds;
        deadline = EditorApplication.timeSinceStartup + maximumWallSeconds;
        SessionState.SetString(Prefix + "Phase", phase);
        SessionState.SetString(Prefix + "Deadline", deadline.ToString("R", CultureInfo.InvariantCulture));
        Current.phase = phase;
        Persist();
    }
    static void Update()
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isPaused) return;
        try
        {
            if (Phase == "Stopping") return;
            double wallDeadline = double.Parse(SessionState.GetString(Prefix + "Deadline", "0"), CultureInfo.InvariantCulture);
            if (EditorApplication.timeSinceStartup > wallDeadline) throw new TimeoutException("Phase timed out: " + Phase + "; game elapsed=" + Elapsed + "; " + Progress());
            if (sequence && !firstRunDone) ObserveNaturalProgress();
            if (sequence && sampledFrame != Time.frameCount)
            {
                sampledFrame = Time.frameCount; frameSamples++; frameSum += Time.unscaledDeltaTime;
                Current.maximumFrameSeconds = Mathf.Max(Current.maximumFrameSeconds, Time.unscaledDeltaTime);
                Current.averageFrameSeconds = frameSum / frameSamples;
            }
            if (player && (Phase == "FindFiringStation" || Phase == "SprayHouse" || Phase == "WetCooldown")) InvokePlayer("UpdateDrivingCamera");
            if (EditorApplication.timeSinceStartup - lastPersist > 5) Persist();
            switch (Phase)
            {
                case "WaitMenu":
                    var menu = Object.FindFirstObjectByType<AshfallMenuController>();
                    if (!menu) break;
                    var menuButtons = new SerializedObject(menu).FindProperty("menuButtons");
                    var button = menuButtons != null && menuButtons.arraySize > 0 ? menuButtons.GetArrayElementAtIndex(0).objectReferenceValue as Button : null;
                    if (!button || !button.gameObject.activeInHierarchy || !button.IsInteractable()) break;
                    Require("main_menu_ready", SceneManager.GetActiveScene().path == MenuPath, "Actual New Game button active after intro.");
                    PhaseTo("WaitGameplay", 0, 150);
                    button.onClick.Invoke();
                    break;
                case "WaitGameplay":
                    if (SceneManager.GetActiveScene().path != GamePath) break;
                    gameStart = Time.time;
                    oldBackground = Application.runInBackground; originalTimeScale = Time.timeScale;
                    SessionState.SetBool(Prefix + "Background", oldBackground);
                    SessionState.SetFloat(Prefix + "TimeScale", originalTimeScale);
                    SessionState.SetBool(Prefix + "Environment", true);
                    Application.runInBackground = true;
                    Require("new_game_loads_authored_volcano", true, GamePath);
                    PhaseTo("Settle", 3);
                    break;
                case "Settle":
                    if (Time.time < phaseEnd) break;
                    InspectAndDrive();
                    break;
                case "PauseProbe":
                    if (Time.unscaledTime - pauseRealStart < 1f) break;
                    Require(pauseLabel + "_paused_world_stable", gameFlow.State == RescueGameFlow.GameState.Paused && Time.timeScale == 0 &&
                        Mathf.Abs(sequence.FlowElapsed - pauseFlowSeconds) < .001f && Mathf.Abs(hose.TankLitres - pauseTank) < .001f &&
                        Vector3.Distance(drive.body.position, pauseTruckPosition) < .01f && !cannon.IsSpraying,
                        "Actual unscaled second; flow/tank/truck unchanged, cannon emission stopped.");
                    Require(pauseLabel + "_pause_ui_and_equipment", hud.PauseVisible && Cursor.lockState == CursorLockMode.None &&
                        (!player.pistol || (player.pistol.Equipped == pauseWeaponEquipped && player.pistol.Ammo == pauseAmmo && !player.pistol.showHud)),
                        "Pause panel visible; cursor released; equipment/ammo preserved; weapon IMGUI hidden behind modal.");
                    Require(pauseLabel + "_posture_and_reload_frozen", !player.firstPerson ||
                        (player.firstPerson.IsKneeling == pauseKneeling && Mathf.Abs(player.firstPerson.KneelAmount - pauseKneelAmount) < .001f &&
                        Mathf.Abs(player.playerBody.height - pauseCapsuleHeight) < .001f && (!player.pistol ||
                        (player.pistol.IsReloading == pauseReloading && Mathf.Abs(player.pistol.ReloadProgress - pauseReloadProgress) < .02f))),
                        "kneeling=" + pauseKneeling + "; amount=" + pauseKneelAmount + "; capsuleHeight=" + pauseCapsuleHeight + "; reloading=" + pauseReloading);
                    hud.resumeButton.onClick.Invoke(); gameFlow.Resume();
                    Require(pauseLabel + "_resume_once", gameFlow.State == RescueGameFlow.GameState.Playing && Time.timeScale == 1f,
                        "Actual Resume button plus duplicate Resume call.");
                    Require(pauseLabel + "_resume_preserves_posture_and_restores_weapon_hud", (!player.firstPerson ||
                        (player.firstPerson.IsKneeling == pauseKneeling && Mathf.Abs(player.firstPerson.KneelAmount - pauseKneelAmount) < .001f)) &&
                        (!player.pistol || player.pistol.showHud == pausePistolHud), "Pause did not force standing or reset weapon presentation.");
                    if (afterPause == "DriveStraight") drive.SetInput(.7f, 0, false);
                    if (afterPause == "Steer") drive.SetInput(.6f, .7f, false);
                    if (afterPause == "SprayHouse") cannon.SetSpraying(true);
                    PhaseTo(afterPause, afterPauseDuration, 100);
                    break;
                case "SettleKneeling":
                    if (kneelFrame != Time.frameCount)
                    {
                        kneelFrame = Time.frameCount;
                        player.firstPerson.SimulateExternalMovement(Vector2.zero, false, false, false, true, 8f, Vector2.zero);
                    }
                    if (Time.time < phaseEnd) break;
                    Require("on_foot_crouched_before_pause", player.firstPerson.IsKneeling && player.firstPerson.KneelAmount > .95f && player.playerBody.height < 1.3f,
                        "Actual movement/posture API reached crouched capsule before pause.");
                    Require("reload_started_before_pause", player.pistol && player.pistol.Reload(), "Real equipped pistol reload API, no ammo modification.");
                    BeginPauseProbe("EnterAndDrive", 0, "on_foot_weapon");
                    break;
                case "EnterAndDrive":
                    Require("door_entry_api", player.EnterVehicle(), "Within actual driver-door radius, no deployed hose.");
                    driveStart = drive.transform.position;
                    drive.SetInput(.7f, 0, false); PhaseTo("DriveStraight", 3);
                    break;
                case "DriveStraight":
                    ObserveDrive();
                    if (Time.time < phaseEnd) break;
                    Require("dynamic_forward_drive", maxSpeed > 2 && Vector3.Distance(driveStart, drive.transform.position) > .6f,
                        "maximumKph=" + maxSpeed + "; displacement=" + Vector3.Distance(driveStart, drive.transform.position));
                    steerStartForward = drive.transform.forward;
                    drive.SetInput(.6f, .7f, false);
                    BeginPauseProbe("Steer", 2, "driving");
                    break;
                case "Steer":
                    ObserveDrive();
                    maxSteer = Mathf.Max(maxSteer, Mathf.Abs(drive.wheels[0].steerAngle));
                    turnDegrees = Mathf.Max(turnDegrees, Vector3.Angle(steerStartForward, drive.transform.forward));
                    if (Time.time < phaseEnd) break;
                    Require("dynamic_steering", maxSteer > 5 && turnDegrees > .5f, "frontSteerDegrees=" + maxSteer + "; chassisTurnDegrees=" + turnDegrees);
                    drive.SetInput(0, 0, true); PhaseTo("Brake", 3);
                    break;
                case "Brake":
                    if (Time.time < phaseEnd) break;
                    Require("dynamic_braking", drive.SpeedKph < 2, "speedKph=" + drive.SpeedKph);
                    driveStart = drive.transform.position; steerStartForward = drive.transform.forward;
                    drive.SetInput(-.65f, 0, false); PhaseTo("Reverse", 2);
                    break;
                case "Reverse":
                    reverseDistance = Mathf.Max(reverseDistance, -Vector3.Dot(drive.transform.position - driveStart, steerStartForward));
                    if (Time.time < phaseEnd) break;
                    Require("dynamic_reverse", reverseDistance > .2f, "signedReverseMetres=" + reverseDistance);
                    drive.SetInput(0, 0, true); PhaseTo("StopForLake", 3);
                    break;
                case "StopForLake":
                    if (Time.time < phaseEnd) break;
                    Require("exit_after_stop", player.ExitVehicle(), "speedKph=" + drive.SpeedKph);
                    SetTruckPose(truckSpawn, truckSpawnRotation);
                    PhaseTo("LakeSettle", 1.5f);
                    break;
                case "LakeSettle":
                    if (Time.time < phaseEnd) break;
                    BeginLakeFill();
                    break;
                case "FillLake":
                    if (hose.Tank01 < .99999f) break;
                    float fillSeconds = Time.time - fillingStart;
                    float litresFilled = hose.TankLitres - fillingTank;
                    Require("real_lake_auto_update_fills_tank", litresFilled > 100 && hose.TankLitres >= hose.tankCapacityLitres - .01f,
                        "litresAdded=" + litresFilled + "; gameSeconds=" + fillSeconds + "; configuredRate=" + hose.fillRateLitresPerSecond + "; averageRate=" + litresFilled / Mathf.Max(.01f, fillSeconds));
                    Require("full_tank_stops_pump", !hose.PumpEnabled && !hose.IsPumping, hose.Status);
                    MoveFoot(hose.hoseSocket.position - Vector3.up * .9f);
                    Require("real_retract_before_driving", player.ReloadOrRetract() && !hose.IsDeployed, "R-priority API at the rear socket.");
                    MoveFoot(player.driverDoor.position - Vector3.up * .9f);
                    Require("vehicle_reentry_after_refill", player.EnterVehicle(), "Tank=" + hose.TankLitres);
                    drive.SetInput(0, 0, true);
                    PhaseTo("WaitNaturalFire", 0, 900);
                    break;
                case "WaitNaturalFire":
                    AimOverview();
                    if (Current.houses.All(h => h.ignitedAt >= 0))
                    {
                        Require("all_five_houses_ignite_from_natural_lava", houses.All(h => h.IsBurning), Progress());
                        Require("natural_eruption_stages", observedWarning && observedEruption && observedFlow,
                            "warning=" + observedWarning + "; erupting=" + observedEruption + "; flowing=" + observedFlow);
                        targetIndex = 0; PrepareFiringStation();
                    }
                    break;
                case "FindFiringStation":
                    if (Time.frameCount <= candidateFrame) break;
                    if (cannon.PredictedTarget == houses[targetIndex])
                    {
                        House record = Current.houses[targetIndex];
                        record.firingPosition = drive.transform.position;
                        sprayStart = Time.time; sprayTank = hose.TankLitres; maxSprayHeat = houses[targetIndex].Heat;
                        cannon.SetSpraying(true); PhaseTo("SprayHouse", 0, 50);
                    }
                    else NextFiringCandidate();
                    break;
                case "SprayHouse":
                    if (cannon.HitFire == houses[targetIndex]) Current.houses[targetIndex].physicalHitObserved = true;
                    if (!sprayPauseChecked && targetIndex == 0 && hose.TankLitres < sprayTank - .1f && houses[targetIndex].Heat > .1f)
                    {
                        sprayPauseChecked = true;
                        BeginPauseProbe("SprayHouse", 0, "spraying");
                        break;
                    }
                    if (!houses[targetIndex].IsBurning)
                    {
                        House record = Current.houses[targetIndex];
                        record.extinguishedAt = Elapsed; record.litresUsed = sprayTank - hose.TankLitres; record.spraySeconds = Time.time - sprayStart;
                        record.stoppedEffectsObserved = FireStopped(houses[targetIndex]);
                        cannon.SetSpraying(false);
                        Require("house_" + targetIndex + "_real_cannon_extinguishes", maxSprayHeat > 0 && houses[targetIndex].IsRescued && record.physicalHitObserved && record.litresUsed >= houses[targetIndex].litresToExtinguish - 1,
                            record.name + "; heat=" + houses[targetIndex].Heat + "; litres=" + record.litresUsed + "; seconds=" + record.spraySeconds + "; station=" + record.firingPosition);
                        Require("house_" + targetIndex + "_fire_effects_stop", record.stoppedEffectsObserved, "No flame/smoke emission, fire light disabled.");
                        if (targetIndex == houses.Length - 1) PhaseTo("VerifyVictory", 0, 30);
                        else PhaseTo("WetCooldown", 1);
                    }
                    else if (hose.TankLitres <= 0) throw new InvalidOperationException("Tank emptied before extinguishing " + houses[targetIndex].name + "; hit=" + (cannon.HitFire ? cannon.HitFire.name : "none"));
                    break;
                case "WetCooldown":
                    if (Time.time < phaseEnd) break;
                    Require("house_" + targetIndex + "_wet_protection", !houses[targetIndex].IsBurning && houses[targetIndex].IgnitionBlocked,
                        "Observed one real second after spraying stopped while lava remains active.");
                    targetIndex++;
                    if (targetIndex < houses.Length) PrepareFiringStation();
                    break;
                case "VerifyVictory":
                    if (gameFlow.State != RescueGameFlow.GameState.Won || !hud.VictoryVisible) break;
                    Require("all_five_actual_houses_extinguished", Current.houses.All(h => h.extinguishedAt >= 0), "All actual scene houses reached Heat=0 from physically delivered roof-cannon water.");
                    Require("five_permanent_rescues_and_single_victory", gameFlow.RescuedCount == 5 && gameFlow.TargetCount == 5 && houses.All(h => h.IsRescued && !h.IsBurning),
                        "Exact five targets, all rescued, Won state.");
                    Require("earlier_houses_stay_rescued_under_lava", Current.houses.All(h => h.reignitedAt < 0) && Elapsed - Current.houses[0].extinguishedAt > 5,
                        "First rescue stayed cold beyond old five-second cooldown with active lava; all targets permanently blocked.");
                    Require("water_budget_consumed", hose.TankLitres < hose.tankCapacityLitres - 499,
                        "remainingLitres=" + hose.TankLitres + "; sumDelivered=" + Current.houses.Sum(h => h.litresUsed));
                    Require("victory_pauses_and_releases_cursor", Time.timeScale == 0 && !cannon.IsSpraying && Cursor.lockState == CursorLockMode.None && Cursor.visible && hud.victoryMenuButton.IsInteractable(),
                        "Victory Canvas visible, world frozen, water emission stopped, return button interactable.");
                    gameFlow.Pause(); gameFlow.Resume();
                    Require("won_cannot_resume_gameplay", gameFlow.State == RescueGameFlow.GameState.Won && Time.timeScale == 0, "Pause/Resume ignored after winning.");
                    victoryHoldStart = Time.unscaledTime; PhaseTo("VictoryHold", 0, 30);
                    break;
                case "VictoryHold":
                    if (Time.unscaledTime - victoryHoldStart < 3f) break;
                    Require("victory_remains_single", gameFlow.RescuedCount == 5 && houses.All(h => h.IsRescued && !h.IsBurning), "Three unscaled seconds; no duplicate count or reignition.");
                    firstRunDone = true;
                    PhaseTo("WaitReturnMenu", 0, 150);
                    hud.victoryMenuButton.onClick.Invoke();
                    break;
                case "WaitReturnMenu":
                    if (SceneManager.GetActiveScene().path != MenuPath) break;
                    Require("victory_button_returns_main_menu", Time.timeScale == 1 && Cursor.lockState == CursorLockMode.None, "Actual victory button loaded saved MainMenu and restored time.");
                    PhaseTo("WaitSecondNewGame", 0, 60);
                    break;
                case "WaitSecondNewGame":
                    var nextMenu = Object.FindFirstObjectByType<AshfallMenuController>();
                    if (!nextMenu) break;
                    var nextButtons = new SerializedObject(nextMenu).FindProperty("menuButtons");
                    var newGame = nextButtons.GetArrayElementAtIndex(0).objectReferenceValue as Button;
                    if (!newGame || !newGame.IsInteractable()) break;
                    PhaseTo("WaitFreshGame", 0, 150); newGame.onClick.Invoke();
                    break;
                case "WaitFreshGame":
                    if (SceneManager.GetActiveScene().path != GamePath) break;
                    var freshFlow = Object.FindFirstObjectByType<RescueGameFlow>();
                    var freshSequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
                    var freshHose = Object.FindFirstObjectByType<FireHoseSystem>();
                    if (!freshFlow || !freshSequence || !freshSequence.Initialized || !freshHose) break;
                    Require("new_game_resets_objective_water_and_lava", freshFlow.State == RescueGameFlow.GameState.Playing && freshFlow.RescuedCount == 0 && freshFlow.TargetCount == 5 &&
                        freshFlow.targetBuildings.All(h => !h.IsRescued && !h.IsBurning) && Mathf.Approximately(freshHose.TankLitres, 250) && Time.timeScale == 1 &&
                        freshSequence.Stage == VolcanoEruptionSequence.EruptionStage.Waiting && freshSequence.routes.All(r => r.flow.VisibleDistance <= .001f),
                        "Fresh scene: 0/5 rescues, 250L, hidden lava, Playing, timeScale1.");
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error.ToString()); }
    }
    static void InspectAndDrive()
    {
        player = Object.FindFirstObjectByType<FireRescuePlayer>();
        sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        gameFlow = Object.FindFirstObjectByType<RescueGameFlow>();
        hud = Object.FindFirstObjectByType<RescueHUD>();
        lake = Object.FindFirstObjectByType<WaterSourceVolume>();
        Require("scene_references", player && sequence && lake && player.drive && player.hose && player.cannon, "Unified real scene references.");
        drive = player.drive; hose = player.hose; cannon = player.cannon;
        Require("rescue_objective_ui_initially_zero", gameFlow && hud && gameFlow.IsConfigured && gameFlow.RescuedCount == 0 && gameFlow.State == RescueGameFlow.GameState.Playing && !hud.PauseVisible && !hud.VictoryVisible,
            "Exactly five target bindings; 0/5; no startup victory.");
        houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).OrderBy(h => h.transform.position.z).ToArray();
        Require("five_initially_cold_houses", houses.Length == 5 && houses.All(h => !h.IsBurning), "houseCount=" + houses.Length);
        foreach (var house in houses) Current.houses.Add(new House { name = house.name, position = house.transform.position });
        Require("lava_initially_hidden", sequence.Stage == VolcanoEruptionSequence.EruptionStage.Waiting && sequence.routes.All(r => r.flow && r.flow.VisibleDistance <= .001f), "stage=" + sequence.Stage);
        Require("natural_timing_and_timescale", sequence.autoErupt && Mathf.Approximately(Time.timeScale, 1),
            "timeScale=" + Time.timeScale + "; auto=" + sequence.autoEruptAfter + "; warning=" + sequence.warningSeconds + "; release=" + sequence.lavaReleaseDelay);
        Require("cannon_original_camera_and_pivots", cannon.aimCamera == player.viewCamera && cannon.yawPivot && cannon.pitchPivot && cannon.muzzle && cannon.enabled,
            "Uses original authored aim camera, yaw and pitch pivots, muzzle, collision mask and range.");
        truckSpawn = drive.transform.position; truckSpawnRotation = drive.transform.rotation;
        Require("settled_truck_ground_reference", TryGround(truckSpawn, out float spawnGround), "Authored spawn on actual terrain.");
        settledTruckClearance = truckSpawn.y - spawnGround;
        oldKinematic = drive.body.isKinematic; oldPlayerEnabled = player.enabled; initialTank = hose.TankLitres;
        player.enabled = false; // Only input/camera polling is disabled; physical/gameplay components stay enabled.
        Current.setupLimitations.Add("Coordinator Update/LateUpdate is temporarily disabled to avoid human input/focus cancelling scripted controls. Drive.FixedUpdate, hose.Update, cannon.LateUpdate, eruption.Update and building.Update run normally at timeScale 1.");
        MoveFoot(player.driverDoor.position - Vector3.up * .9f);
        driveStart = drive.transform.position; maxSpeed = maxSteer = turnDegrees = reverseDistance = 0;
        player.RefreshEquipmentState();
        if (player.pistol) player.pistol.Equip();
        Require("kneeling_controller_present", player.firstPerson && player.firstPerson.SetKneeling(true), "Using authored first-person controller.");
        kneelFrame = -1; PhaseTo("SettleKneeling", .8f);
    }
    static void BeginPauseProbe(string nextPhase, float seconds, string label)
    {
        afterPause = nextPhase; afterPauseDuration = seconds; pauseLabel = label;
        pauseFlowSeconds = sequence.FlowElapsed; pauseTank = hose.TankLitres; pauseTruckPosition = drive.body.position;
        pauseWeaponEquipped = player.pistol && player.pistol.Equipped; pauseAmmo = player.pistol ? player.pistol.Ammo : 0;
        pausePistolHud = player.pistol && player.pistol.showHud;
        pauseReloading = player.pistol && player.pistol.IsReloading;
        pauseReloadProgress = player.pistol ? player.pistol.ReloadProgress : 0;
        pauseKneeling = player.firstPerson && player.firstPerson.IsKneeling;
        pauseKneelAmount = player.firstPerson ? player.firstPerson.KneelAmount : 0;
        pauseCapsuleHeight = player.playerBody.height;
        gameFlow.Pause(); gameFlow.Pause();
        Require(label + "_pause_is_idempotent", gameFlow.State == RescueGameFlow.GameState.Paused && Time.timeScale == 0,
            "Two Pause calls produce one paused state.");
        pauseRealStart = Time.unscaledTime; PhaseTo("PauseProbe", 0, 30);
    }
    static void ObserveDrive()
    {
        maxSpeed = Mathf.Max(maxSpeed, drive.SpeedKph);
        player.viewCamera.transform.SetPositionAndRotation(drive.transform.position - drive.transform.forward * 10 + Vector3.up * 5,
            Quaternion.LookRotation(drive.transform.forward * 10 - Vector3.up * 3));
    }
    static void BeginLakeFill()
    {
        Require("authored_tank_starts_250_litres", Mathf.Approximately(initialTank, 250f) && Mathf.Approximately(initialTank, hose.TankLitres),
            "initial=" + initialTank + "; beforePump=" + hose.TankLitres + "; no test water grant or tank reset");
        MoveFoot(hose.hoseSocket.position - Vector3.up * .9f);
        InvokePlayer("InteractWithHose"); player.RefreshEquipmentState();
        Require("rear_pickup_interaction", hose.IsCarried, "Actual player InteractWithHose pickup branch.");
        MoveFoot(player.driverDoor.position - Vector3.up * .9f);
        Require("hose_blocks_vehicle_entry", !player.EnterVehicle() && !player.IsDriving, "Actual EnterVehicle rejects deployed/carried hose.");
        Bounds lakeBounds = lake.volume.bounds;
        var intakes = new List<Vector3>();
        for (int x = 0; x <= 30; x++) for (int z = 0; z <= 30; z++)
        {
            var point = new Vector3(Mathf.Lerp(lakeBounds.min.x, lakeBounds.max.x, x / 30f), lake.SurfaceY - .12f, Mathf.Lerp(lakeBounds.min.z, lakeBounds.max.z, z / 30f));
            if (lake.ContainsIntake(point) && hose.CanReach(point)) intakes.Add(point);
        }
        bool dropped = false;
        foreach (Vector3 intake in intakes.OrderBy(p => (p - hose.hoseSocket.position).sqrMagnitude))
        {
            Vector3 towardTruck = Vector3.ProjectOnPlane(hose.hoseSocket.position - intake, Vector3.up).normalized;
            for (float offset = 1.5f; offset <= 6; offset += .5f)
            {
                Vector3 bank = intake + towardTruck * offset;
                if (!TryGround(bank, out float ground) || ground < lake.SurfaceY + .02f) continue;
                bank.y = ground + .12f;
                Vector3 eye = bank + Vector3.up * player.eyeHeight;
                if (Vector3.Distance(eye, intake) > 7.8f || !hose.CanReach(eye)) continue;
                MoveFoot(bank);
                player.viewCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(intake - eye));
                Physics.SyncTransforms();
                Vector3 rayDrop = (Vector3)InvokePlayer("GetDropPoint");
                if (!lake.ContainsIntake(rayDrop) || !hose.CanReach(rayDrop)) continue;
                InvokePlayer("InteractWithHose"); player.RefreshEquipmentState();
                dropped = hose.IsDeployed && !hose.IsCarried && lake.ContainsIntake(hose.nozzle.position);
                if (dropped)
                {
                    Require("shoreline_drop_ray_hits_real_lake", true, "dryBank=" + bank + "; camera=" + eye + "; submergedIntake=" + hose.nozzle.position + "; hoseMetres=" + Vector3.Distance(hose.hoseSocket.position, hose.nozzle.position));
                    break;
                }
            }
            if (dropped) break;
        }
        Require("real_player_drop_interaction", dropped, "Uses original GetDropPoint eight-metre physics ray and original InteractWithHose; no direct Drop or water-volume modification.");
        hose.TogglePump();
        Require("real_source_pump_enabled", hose.IsPumping && hose.CurrentWaterSource == lake, hose.Status);
        fillingStart = Time.time; fillingTank = hose.TankLitres;
        BeginPauseProbe("FillLake", 0, "lake_pump");
    }
    static void ObserveNaturalProgress()
    {
        observedWarning |= sequence.Stage == VolcanoEruptionSequence.EruptionStage.Warning;
        observedEruption |= sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting;
        observedFlow |= sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing;
        if (houses == null) return;
        for (int i = 0; i < houses.Length; i++)
        {
            var house = houses[i]; var record = Current.houses[i];
            if (record.ignitedAt < 0 && house.IsBurning)
            {
                record.ignitedAt = Elapsed;
                record.burningEffectsObserved = house.flames && house.flames.isEmitting && house.smoke && house.smoke.isEmitting && house.fireLight && house.fireLight.enabled;
                Add("house_" + i + "_natural_ignition_effects", record.burningEffectsObserved, house.name + "; atGameSecond=" + Elapsed + "; flowSecond=" + sequence.FlowElapsed);
            }
            if (record.extinguishedAt >= 0 && record.reignitedAt < 0 && house.IsBurning)
            {
                record.reignitedAt = Elapsed;
                Add("house_" + i + "_rescued_house_never_reignites", false,
                    "Unexpected reignition at " + record.reignitedAt + "; rescued=" + house.IsRescued);
            }
        }
    }
    static void PrepareFiringStation()
    {
        cannon.SetSpraying(false);
        var bounds = houses[targetIndex].ignitionBounds.bounds;
        firingCandidates.Clear(); candidateIndex = 0;
        for (float distance = 14; distance <= 38; distance += 6)
            for (int direction = 0; direction < 24; direction++)
            {
                float angle = direction * Mathf.PI / 12;
                Vector3 position = bounds.center + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * distance;
                if (!TryGround(position, out float ground)) continue;
                position.y = ground + settledTruckClearance;
                // Test the current enlarged truck's actual oriented chassis boxes, not a legacy footprint.
                Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(bounds.center - position, Vector3.up));
                if (FiringStationIsClear(position, rotation)) firingCandidates.Add(position);
            }
        Require("house_" + targetIndex + "_open_terrain_candidates", firingCandidates.Count > 0,
            "candidateCount=" + firingCandidates.Count + "; settledRootGroundClearance=" + settledTruckClearance + "; actualBoxColliders=" + TruckBoxes().Length);
        drive.SetInput(0, 0, true); drive.body.isKinematic = true;
        PhaseTo("FindFiringStation", 0, 90); NextFiringCandidate();
    }
    static BoxCollider[] TruckBoxes() => drive.GetComponentsInChildren<BoxCollider>()
        .Where(box => box && box.enabled && !box.isTrigger && box.attachedRigidbody == drive.body).ToArray();
    static bool FiringStationIsClear(Vector3 position, Quaternion rotation)
    {
        foreach (var box in TruckBoxes())
        {
            Vector3 localOffset = Quaternion.Inverse(drive.transform.rotation) * (box.transform.TransformPoint(box.center) - drive.transform.position);
            Quaternion localRotation = Quaternion.Inverse(drive.transform.rotation) * box.transform.rotation;
            Vector3 scale = box.transform.lossyScale;
            Vector3 half = Vector3.Scale(box.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * .5f + new Vector3(.12f, .04f, .12f);
            var overlaps = Physics.OverlapBox(position + rotation * localOffset, half, rotation * localRotation, ~0, QueryTriggerInteraction.Ignore);
            if (overlaps.Any(c => c && !c.transform.IsChildOf(drive.transform) && !c.transform.IsChildOf(player.transform) &&
                !c.GetComponentInParent<ProgressiveLavaFlow>() && !c.GetComponentInParent<LavaIgnitionSource>())) return false;
        }
        return true;
    }
    static void NextFiringCandidate()
    {
        if (candidateIndex >= firingCandidates.Count)
            throw new InvalidOperationException("No actual turret/collider firing line found for " + houses[targetIndex].name + "; sampled=" + firingCandidates.Count + "; lastHit=" + (cannon.HitFire ? cannon.HitFire.name : "none") + "; lastAimPoint=" + cannon.AimPoint);
        var bounds = houses[targetIndex].ignitionBounds.bounds;
        Vector3 target = bounds.center;
        Vector3 position = firingCandidates[candidateIndex++];
        Vector3 forward = Vector3.ProjectOnPlane(target - position, Vector3.up).normalized;
        SetTruckPose(position, Quaternion.LookRotation(forward));
        Vector3 cameraTarget = position + Vector3.up * (player.followHeight + player.cannonCameraLift);
        Vector3 desiredRay = (target - cameraTarget).normalized;
        float desiredPitch = -Mathf.Asin(desiredRay.y) * Mathf.Rad2Deg;
        float desiredYaw = Mathf.Atan2(desiredRay.x, desiredRay.z) * Mathf.Rad2Deg;
        typeof(FireRescuePlayer).GetField("lookPitch", Private).SetValue(player, Mathf.Clamp(desiredPitch, -10f, 65f));
        typeof(FireRescuePlayer).GetField("orbitYaw", Private).SetValue(player, Mathf.DeltaAngle(drive.transform.eulerAngles.y, desiredYaw));
        typeof(FireRescuePlayer).GetField("aimingCannon", Private).SetValue(player, true);
        typeof(FireRescuePlayer).GetField("snapCamera", Private).SetValue(player, true);
        InvokePlayer("UpdateDrivingCamera");
        Physics.SyncTransforms(); candidateFrame = Time.frameCount;
    }
    static void AimOverview()
    {
        if (!player || houses == null || houses.Length == 0) return;
        Vector3 centre = houses.Aggregate(Vector3.zero, (sum, h) => sum + h.transform.position) / houses.Length;
        Vector3 eye = centre + new Vector3(-35, 24, -36);
        player.viewCamera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(centre - eye));
    }
    static bool TryGround(Vector3 point, out float y)
    {
        foreach (var terrain in Terrain.activeTerrains)
        {
            Vector3 local = point - terrain.transform.position, size = terrain.terrainData.size;
            if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z)
            { y = terrain.SampleHeight(point) + terrain.transform.position.y; return true; }
        }
        y = 0; return false;
    }
    static void SetTruckPose(Vector3 position, Quaternion rotation)
    {
        if (!drive.body.isKinematic) { drive.body.linearVelocity = Vector3.zero; drive.body.angularVelocity = Vector3.zero; }
        drive.body.position = position; drive.body.rotation = rotation;
        drive.transform.SetPositionAndRotation(position, rotation); Physics.SyncTransforms();
    }
    static void MoveFoot(Vector3 position)
    {
        bool enabled = player.playerBody.enabled; player.playerBody.enabled = false;
        player.transform.position = position; player.playerBody.enabled = enabled; Physics.SyncTransforms();
    }
    static object InvokePlayer(string method) => typeof(FireRescuePlayer).GetMethod(method, Private).Invoke(player, null);
    static bool FireStopped(BurnableBuilding house) => (!house.flames || !house.flames.isEmitting) && (!house.smoke || !house.smoke.isEmitting) && (!house.fireLight || !house.fireLight.enabled);
    static string Progress() => "stage=" + (sequence ? sequence.Stage.ToString() : "none") + "; flow=" + (sequence ? sequence.FlowElapsed : 0) + "; burning=" + (houses != null ? houses.Count(h => h.IsBurning) : 0) + "; tank=" + (hose ? hose.TankLitres : 0);
    static void Add(string name, bool passed, string evidence)
    {
        Current.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Persist();
    }
    static void Require(string name, bool passed, string evidence)
    {
        Add(name, passed, evidence); if (!passed) throw new InvalidOperationException(name + ": " + evidence);
    }
    static void Persist()
    {
        if (report == null) return;
        report.phase = Phase;
        if (sequence && !firstRunDone)
        {
            report.gameSeconds = Elapsed; report.flowSeconds = sequence.FlowElapsed; report.eruptionStage = sequence.Stage.ToString();
            report.burningHouses = houses != null ? houses.Count(h => h && h.IsBurning) : 0;
        }
        if (hose) report.tankLitres = hose.TankLitres;
        if (drive) report.truckSpeedKph = drive.SpeedKph;
        if (DateTime.TryParse(report.startedUtc, null, DateTimeStyles.RoundtripKind, out var start)) report.realSeconds = (float)(DateTime.UtcNow - start).TotalSeconds;
        string json = JsonUtility.ToJson(report, true);
        SessionState.SetString(Prefix + "Report", json); Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, json);
        lastPersist = EditorApplication.timeSinceStartup;
    }
    static void LogReceived(string message, string trace, LogType type)
    {
        if (!SessionState.GetBool(Prefix + "Active", false) || !EditorApplication.isPlaying || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        if (Current.runtimeErrors.Count < 30) Current.runtimeErrors.Add(type + ": " + message + "\n" + trace);
        Persist();
    }
    static void RestoreEnvironment()
    {
        if (cannon) cannon.SetSpraying(false);
        if (drive) { drive.SetInput(0, 0, true); drive.body.isKinematic = oldKinematic; }
        if (player) player.enabled = oldPlayerEnabled;
        if (SessionState.GetBool(Prefix + "Environment", false))
        {
            Application.runInBackground = SessionState.GetBool(Prefix + "Background", false);
            Time.timeScale = SessionState.GetFloat(Prefix + "TimeScale", 1);
            SessionState.SetBool(Prefix + "Environment", false);
        }
    }
    static void Finish(string error)
    {
        if (Phase == "Stopping") return;
        if (!string.IsNullOrEmpty(error)) Current.error = error;
        RestoreEnvironment(); Current.status = "stopping_play"; PhaseTo("Stopping", 0);
        EditorApplication.ExitPlaymode();
    }
    static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) RestoreEnvironment();
        if (!SessionState.GetBool(Prefix + "Active", false) || state != PlayModeStateChange.EnteredEditMode) return;
        if (Phase != "Stopping" && string.IsNullOrEmpty(Current.error)) Current.error = "Play Mode ended early in phase " + Phase;
        Current.editorScene = SceneManager.GetActiveScene().path;
        Current.checks.Add(new Check { name = "returns_to_saved_main_menu", passed = Current.editorScene == MenuPath, evidence = Current.editorScene });
        Current.finishedUtc = DateTime.UtcNow.ToString("O");
        Current.allPassed = string.IsNullOrEmpty(Current.error) && Current.runtimeErrors.Count == 0 && Current.checks.All(c => c.passed);
        Current.status = Current.allPassed ? "passed" : "failed"; Persist(); SessionState.SetBool(Prefix + "Active", false);
        Debug.Log("Continuous Volcano playthrough " + Current.status + "; " + Current.checks.Count + " checks. Report: " + ReportPath);
    }
}
