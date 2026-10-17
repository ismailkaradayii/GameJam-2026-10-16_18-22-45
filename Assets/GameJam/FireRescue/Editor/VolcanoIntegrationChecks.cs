using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Ashfall.MainMenu;
using GameJam.FireRescue;
using Itfaiye;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Runs the real menu-to-game transition, then bounded Play Mode integration checks.</summary>
[InitializeOnLoad]
public static class VolcanoIntegrationChecks
{
    const string MenuPath = "Assets/Scenes/MainMenu.unity";
    const string GamePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string ReportPath = "Logs/VolcanoIntegrationChecks.json";
    const string ActiveKey = "GameJam.VolcanoIntegration.Active";
    const string StateKey = "GameJam.VolcanoIntegration.State";
    const string ReportKey = "GameJam.VolcanoIntegration.Report";
    const string DeadlineKey = "GameJam.VolcanoIntegration.Deadline";

    [Serializable] public sealed class Check { public string name; public bool passed; public string evidence; }
    [Serializable] public sealed class Report
    {
        public string status, startedUtc, finishedUtc, error, editorScene;
        public bool allPassed;
        public int regressionChecks;
        public List<Check> checks = new List<Check>();
        public List<string> runtimeErrors = new List<string>();
    }
    [Serializable] sealed class SuiteReport { public bool allPassed; public string error; public List<Check> checks; }

    static Report report;
    static FireRescuePlayer player;
    static ItfaiyeFirstPersonController firstPerson;
    static ItfaiyePistol pistol;
    static FireTruckDrive drive;
    static FireHoseSystem hose;
    static VolcanoEruptionSequence sequence;
    static LavaCreatureEncounter encounter;
    static Vector3 truckPosition, footPosition, driveStart;
    static Quaternion truckRotation, footRotation;
    static float maximumSpeed, originalTank;
    static GameObject shotTarget;
    static LavaCreatureActor shotActor;
    static int beforeAmmo, beforeShots;
    static int maximumGroundedWheels, groundingSamples;
    const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    static Keyboard testKeyboard;
    static Mouse testMouse;
    static InputSettings temporaryInputSettings;
    static InputSnapshot inputSnapshot;

    sealed class InputSnapshot
    {
        public Keyboard keyboard;
        public Mouse mouse;
        public InputSettings settings;
        public bool runInBackground;
        public CursorLockMode cursor;
        public bool cursorVisible, focused, captured, weaponAvailable, weaponEquipped, motorEnabled;
        public Vector3 position;
        public Quaternion rotation;
        public float yaw, pitch, orbit, water;
    }

    static VolcanoIntegrationChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        Application.logMessageReceived += LogReceived;
        AssemblyReloadEvents.beforeAssemblyReload += RestoreInputDispatch;
    }

    [MenuItem("GameJam/Integration/Check menu and Volcano gameplay", true)]
    static bool CanBegin() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !SessionState.GetBool(ActiveKey, false);

    [MenuItem("GameJam/Integration/Check menu and Volcano gameplay")]
    public static void Begin()
    {
        if (!CanBegin()) throw new InvalidOperationException("Start integration checks in Edit Mode after compilation finishes.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before running integration checks.");
        if (!File.Exists(MenuPath)) throw new FileNotFoundException("MainMenu scene is missing.", MenuPath);
        report = new Report { status = "running", startedUtc = DateTime.UtcNow.ToString("O") };
        EditorSceneManager.OpenScene(MenuPath, OpenSceneMode.Single);
        SessionState.SetBool(ActiveKey, true);
        SetPhase("WaitMenu", 45);
        Persist();
        EditorApplication.EnterPlaymode();
    }

    static Report CurrentReport
    {
        get
        {
            if (report == null)
                report = JsonUtility.FromJson<Report>(SessionState.GetString(ReportKey, "{}")) ?? new Report();
            if (report.checks == null) report.checks = new List<Check>();
            if (report.runtimeErrors == null) report.runtimeErrors = new List<string>();
            return report;
        }
    }

    static void SetPhase(string phase, double seconds)
    {
        SessionState.SetString(StateKey, phase);
        SessionState.SetString(DeadlineKey, (EditorApplication.timeSinceStartup + seconds).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        Persist();
    }

    static bool Expired => EditorApplication.timeSinceStartup >= double.Parse(SessionState.GetString(DeadlineKey, "0"), System.Globalization.CultureInfo.InvariantCulture);

    static void Update()
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isPaused) return;
        string phase = SessionState.GetString(StateKey, "");
        try
        {
            switch (phase)
            {
                case "WaitMenu":
                    var menu = Object.FindFirstObjectByType<AshfallMenuController>();
                    if (menu)
                    {
                        var serialized = new SerializedObject(menu);
                        var buttons = serialized.FindProperty("menuButtons");
                        var button = buttons != null && buttons.arraySize > 0 ? buttons.GetArrayElementAtIndex(0).objectReferenceValue as Button : null;
                        if (button && button.gameObject.activeInHierarchy && button.IsInteractable())
                        {
                            Add("real_menu_ready", SceneManager.GetActiveScene().path == MenuPath, "New Game is active and interactable after the actual intro.");
                            string target = serialized.FindProperty("gameplaySceneName")?.stringValue;
                            Add("menu_targets_volcano", target == "Volcano_Demo", "gameplaySceneName=" + target);
                            SetPhase("WaitGameplay", 45);
                            button.onClick.Invoke();
                            return;
                        }
                    }
                    if (Expired) throw new TimeoutException("The actual MainMenu New Game button did not become interactable within 45 seconds.");
                    break;
                case "WaitGameplay":
                    if (SceneManager.GetActiveScene().path == GamePath)
                    {
                        Add("new_game_loads_actual_volcano", true, SceneManager.GetActiveScene().path);
                        SetPhase("Settle", 2.5); // Allow Awake, Start, animation and wheel contacts to run.
                    }
                    else if (Expired) throw new TimeoutException("New Game did not load Volcano_Demo. Active scene=" + SceneManager.GetActiveScene().path);
                    break;
                case "Settle":
                    if (!Expired) break;
                    InspectScene();
                    maximumGroundedWheels = 0;
                    groundingSamples = 0;
                    SetPhase("ObserveWheelContacts", 3);
                    break;
                case "ObserveWheelContacts":
                    int grounded = drive.wheels == null ? 0 : drive.wheels.Count(w => w && w.isGrounded);
                    maximumGroundedWheels = Mathf.Max(maximumGroundedWheels, grounded);
                    groundingSamples++;
                    if (grounded < 4 && !Expired) break;
                    Add("four_wheels_grounded_at_spawn", maximumGroundedWheels == 4,
                        "maximumSimultaneousGrounded=" + maximumGroundedWheels + "; finalGrounded=" + grounded +
                        "; samples=" + groundingSamples + "; observedUpToSeconds=3 after initial 2.5-second settle");
                    RunRegressions();
                    BeginInputDispatch();
                    break;
                case "InputWeaponDraw":
                    if (pistol.Ready)
                    {
                        CompleteInputDispatch();
                        BeginDriveCheck();
                    }
                    else if (Expired) throw new TimeoutException("Digit 1 was dispatched through the real coordinator, but the pistol did not become ready.");
                    break;
                case "Driving":
                    maximumSpeed = Mathf.Max(maximumSpeed, Mathf.Abs(drive.SpeedKph));
                    if (!Expired) break;
                    Add("real_truck_accelerates_on_spawn_ground", Vector3.Distance(driveStart, drive.transform.position) > .2f && maximumSpeed > 1f,
                        "distance=" + Vector3.Distance(driveStart, drive.transform.position) + "; maximumKph=" + maximumSpeed);
                    drive.SetInput(0f, 0f, true);
                    SetPhase("Braking", 1.5);
                    break;
                case "Braking":
                    if (!Expired) break;
                    Add("real_truck_brakes", Mathf.Abs(drive.SpeedKph) < 5f, "speedKph=" + drive.SpeedKph);
                    RestoreTruck();
                    player.SetDriving(false);
                    MoveFoot(footPosition, footRotation);
                    CheckHoseAndEquipment();
                    // Isolate the real gun from terrain/vehicles while retaining its actual rig and scripts.
                    player.playerBody.enabled = false;
                    firstPerson.transform.SetPositionAndRotation(new Vector3(32000f, 250f, 32000f), Quaternion.identity);
                    firstPerson.playerCamera.transform.localPosition = new Vector3(0f, 1.7f, .2f);
                    firstPerson.playerCamera.transform.localRotation = Quaternion.identity;
                    player.RefreshEquipmentState();
                    pistol.Equip();
                    SetPhase("WaitWeapon", 5);
                    break;
                case "WaitWeapon":
                    if (pistol.Ready)
                    {
                        beforeAmmo = pistol.Ammo;
                        beforeShots = pistol.ShotsFired;
                        shotTarget = new GameObject("Integration check temporary pistol target");
                        shotTarget.transform.position = pistol.muzzle.position + pistol.muzzle.forward * 3f;
                        shotTarget.AddComponent<BoxCollider>().size = Vector3.one * 2f;
                        shotActor = shotTarget.AddComponent<LavaCreatureActor>();
                        Physics.SyncTransforms();
                        Add("real_pistol_accepts_shot_on_foot", pistol.Fire(), "Ready=" + pistol.Ready + "; ammoBefore=" + beforeAmmo);
                        SetPhase("WaitShot", .5);
                    }
                    else if (Expired) throw new TimeoutException("Osman's real pistol did not finish drawing while available on foot.");
                    break;
                case "WaitShot":
                    if (!Expired) break;
                    Add("pistol_shot_consumes_one_round", pistol.Ammo == beforeAmmo - 1 && pistol.ShotsFired == beforeShots + 1,
                        "ammo=" + pistol.Ammo + "; shots=" + pistol.ShotsFired);
                    Add("real_pistol_hits_creature_actor", shotActor && shotActor.Health < shotActor.maxHealth && pistol.LastShotCollider && pistol.LastShotCollider.GetComponentInParent<LavaCreatureActor>() == shotActor,
                        "health=" + (shotActor ? shotActor.Health : -1f) + "; hit=" + (pistol.LastShotCollider ? pistol.LastShotCollider.name : "none"));
                    if (shotTarget) Object.DestroyImmediate(shotTarget);
                    CheckCannonActor();
                    CheckActualEncounter();
                    Finish(null);
                    break;
            }
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    static void InspectScene()
    {
        var players = Object.FindObjectsByType<FireRescuePlayer>(FindObjectsSortMode.None);
        var footPlayers = Object.FindObjectsByType<ItfaiyeFirstPersonController>(FindObjectsSortMode.None);
        Add("one_coordinated_player", players.Length == 1 && footPlayers.Length == 1, "FireRescue=" + players.Length + "; Osman=" + footPlayers.Length);
        if (players.Length != 1 || footPlayers.Length != 1) throw new InvalidOperationException("Expected exactly one coordinated player.");
        player = players[0]; firstPerson = footPlayers[0]; pistol = player.pistol; drive = player.drive; hose = player.hose;
        sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        if (encounter)
        {
            encounter.enableWaves = false;
            var objective = encounter.GetComponent<VolcanoSurvivalObjective>();
            if (objective) objective.enabled = false;
        }
        if (!pistol || !drive || !hose || !sequence || !encounter || !player.playerBody)
            throw new InvalidOperationException("Required player/truck/hose/sequence/encounter references are missing.");
        Add("single_external_input_owner", player.firstPerson == firstPerson && firstPerson.externalControl && pistol.externalControl &&
            player.playerBody == firstPerson.GetComponent<CharacterController>(), "Osman movement and pistol delegate polling to FireRescuePlayer.");
        Add("start_on_foot", !player.IsDriving && player.playerBody.enabled && pistol.Available, "driving=" + player.IsDriving + "; weaponAvailable=" + pistol.Available);
        Add("truck_and_camera_links", player.cannon && player.cannon.tank == hose && player.cannon.aimCamera == player.viewCamera &&
            player.viewCamera == firstPerson.playerCamera && player.driverDoor && player.exitPoint && player.hand, "Truck, intake, cannon and active view references are wired.");
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Where(c => c.isActiveAndEnabled).ToArray();
        int overlays = cameras.Count(c => c.TryGetComponent<UniversalAdditionalCameraData>(out var d) && d.renderType == CameraRenderType.Overlay);
        int listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled);
        Add("one_world_camera_plus_hands_overlay", cameras.Length == 2 && overlays == 1 && listeners == 1,
            "activeCameras=" + cameras.Length + "; overlays=" + overlays + "; listeners=" + listeners);
        Add("mountain_visibility_fix_preserved", !firstPerson.playerCamera.useOcclusionCulling && firstPerson.playerCamera.farClipPlane >= 3000f,
            "occlusion=" + firstPerson.playerCamera.useOcclusionCulling + "; far=" + firstPerson.playerCamera.farClipPlane);
        Add("five_cold_town_houses", Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Length == 5 &&
            Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).All(h => !h.IsBurning), "Five actual town houses start cold.");
        Add("complete_lava_network_and_lake", sequence.Initialized && sequence.routes.Length == 23 && sequence.pools.Length == 12 &&
            Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None).Length > 0,
            "routes=" + sequence.routes.Length + "; pools=" + sequence.pools.Length);
        Add("three_creatures_initially_gated", encounter.Initialized && encounter.spawns.Length == 3 && encounter.SpawnedCount == 0 &&
            encounter.spawns.All(s => s != null && s.creature && s.feed && !s.creature.gameObject.activeSelf), "spawnCount=" + encounter.spawns.Length);
        sequence.enabled = false;
        encounter.enabled = false;
        // The coordinator's public commands are used below; no global keyboard/mouse events are injected.
        player.enabled = false;
        truckPosition = drive.body.position; truckRotation = drive.body.rotation;
        footPosition = firstPerson.transform.position; footRotation = firstPerson.transform.rotation;
        originalTank = hose.TankLitres;
        Persist();
    }

    static void RunRegressions()
    {
        Suite("fire_combat", FireCombatChecks.Run, "Logs/FireCombatChecks.json");
        Suite("progressive_lava", ProgressiveLavaChecks.Run, "Library/ProgressiveLavaChecks.json");
        Suite("volcano_sequence", VolcanoSequenceChecks.Run, "Logs/VolcanoSequenceChecks.json");
        Suite("eruption_effects", EruptionEffectsChecks.Run, "Logs/EruptionEffectsChecks.json");
        Add("at_least_87_existing_regressions", CurrentReport.regressionChecks >= 87, "checks=" + CurrentReport.regressionChecks);
    }

    static void Suite(string name, Action run, string path)
    {
        DateTime started = DateTime.UtcNow;
        run();
        var suite = JsonUtility.FromJson<SuiteReport>(File.ReadAllText(path));
        bool fresh = File.GetLastWriteTimeUtc(path) >= started.AddSeconds(-1);
        int count = suite.checks == null ? 0 : suite.checks.Count;
        CurrentReport.regressionChecks += count;
        Add("regression_" + name, fresh && suite.allPassed && string.IsNullOrEmpty(suite.error) && count > 0,
            "fresh=" + fresh + "; checks=" + count + "; report=" + path + "; error=" + suite.error);
    }

    static void BeginDriveCheck()
    {
        hose.Retract();
        drive.body.linearVelocity = Vector3.zero; drive.body.angularVelocity = Vector3.zero;
        MoveFoot(player.driverDoor.position, truckRotation);
        bool entered = player.EnterVehicle();
        player.RefreshEquipmentState();
        Add("enter_vehicle_hands_and_weapon_yield", entered && player.IsDriving && drive.IsDriven && !player.playerBody.enabled && !pistol.Available && !firstPerson.handsCamera.enabled,
            "entered=" + entered + "; weaponAvailable=" + pistol.Available);
        if (!entered) throw new InvalidOperationException("Could not enter the real truck at its driver door.");
        drive.body.linearVelocity = drive.transform.forward * 2f;
        Add("moving_vehicle_blocks_exit", !player.ExitVehicle() && player.IsDriving, "speedKph=" + drive.SpeedKph);
        drive.body.linearVelocity = Vector3.zero;
        bool exited = player.ExitVehicle();
        player.RefreshEquipmentState();
        Add("stopped_vehicle_exit_restores_osman", exited && !player.IsDriving && player.playerBody.enabled && pistol.Available && firstPerson.handsCamera.enabled,
            "exited=" + exited + "; weaponAvailable=" + pistol.Available);
        if (!exited) throw new InvalidOperationException("The actual truck has no clear exit at its authored spawn.");
        footPosition = firstPerson.transform.position; footRotation = firstPerson.transform.rotation;
        MoveFoot(player.driverDoor.position, truckRotation);
        if (!player.EnterVehicle()) throw new InvalidOperationException("Could not re-enter the truck for the short drive check.");
        driveStart = drive.transform.position; maximumSpeed = 0f;
        drive.SetInput(1f, 0f, false);
        SetPhase("Driving", 1.8);
    }

    static void BeginInputDispatch()
    {
        // Only temporary Unity Input System devices receive synthetic events. Physical devices
        // remain enabled and their state is never overwritten. This does not test OS key injection.
        inputSnapshot = new InputSnapshot
        {
            keyboard = Keyboard.current, mouse = Mouse.current,
            settings = InputSystem.settings, runInBackground = Application.runInBackground,
            cursor = Cursor.lockState, cursorVisible = Cursor.visible,
            focused = ReadPrivate<bool>(player, "focused"), captured = firstPerson.InputCaptured,
            weaponAvailable = pistol.Available, weaponEquipped = pistol.Equipped,
            motorEnabled = player.playerBody.enabled, position = firstPerson.transform.position,
            rotation = firstPerson.transform.rotation, water = hose.TankLitres,
            yaw = ReadPrivate<float>(player, "lookYaw"), pitch = ReadPrivate<float>(player, "lookPitch"),
            orbit = ReadPrivate<float>(player, "orbitYaw")
        };
        // The Editor routes even synthetic keyboard/mouse events by Game View focus.
        // Install only an unsaved copy for this bounded fixture; never mutate the project asset.
        temporaryInputSettings = Object.Instantiate(inputSnapshot.settings);
        temporaryInputSettings.hideFlags = HideFlags.HideAndDontSave;
        temporaryInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        temporaryInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;
        InputSystem.settings = temporaryInputSettings;
        testKeyboard = InputSystem.AddDevice<Keyboard>("VolcanoIntegrationKeyboard");
        testMouse = InputSystem.AddDevice<Mouse>("VolcanoIntegrationMouse");
        WritePrivate(player, "focused", true);
        InvokePrivate(player, "SetCursor", true);
        player.SetDriving(false);
        hose.Retract();
        player.RefreshEquipmentState();
        DispatchInput();
        bool gate = Keyboard.current == testKeyboard && Mouse.current == testMouse && Cursor.lockState == CursorLockMode.Locked && firstPerson.InputCaptured;
        Add("input_dispatch_gate_ready", gate,
            "temporaryKeyboard=" + (Keyboard.current == testKeyboard) + "; temporaryMouse=" + (Mouse.current == testMouse) +
            "; cursor=" + Cursor.lockState + "; captured=" + firstPerson.InputCaptured + "; actualUpdateInvoked=true; " + InputEvidence());
        if (!gate) throw new InvalidOperationException("Unity did not retain the gameplay cursor/input gate. Synthetic dispatch cannot validate OS/Game View focus.");
        DispatchInput(new[] { Key.Digit1 });
        Add("input_digit1_equips_osman_weapon", pistol.Equipped && pistol.Available,
            "wasPressed=" + testKeyboard.digit1Key.wasPressedThisFrame + "; isPressed=" + testKeyboard.digit1Key.isPressed +
            "; value=" + testKeyboard.digit1Key.ReadValue() + "; equipped=" + pistol.Equipped + "; " + InputEvidence());
        if (!testKeyboard.digit1Key.isPressed || !testKeyboard.digit1Key.wasPressedThisFrame)
            throw new InvalidOperationException("The synthetic Digit 1 event was not processed as a new gameplay press. " + InputEvidence());
        DispatchInput();
        SetPhase("InputWeaponDraw", 5);
    }

    static void CompleteInputDispatch()
    {
        try
        {
            float water = hose.TankLitres;
            DispatchInput(rightMouse: true);
            Add("input_foot_rmb_routes_to_ads_only", pistol.IsAiming && !player.cannon.IsSpraying && Mathf.Approximately(water, hose.TankLitres),
                "ADS=" + pistol.IsAiming + "; cannon=" + player.cannon.IsSpraying + "; tank=" + hose.TankLitres);
            DispatchInput();
            DispatchInput(new[] { Key.R });
            Add("input_r_routes_to_weapon_reload_on_foot", pistol.IsReloading && !hose.IsDeployed,
                "reload=" + pistol.IsReloading + "; hoseDeployed=" + hose.IsDeployed);
            DispatchInput();

            drive.body.linearVelocity = Vector3.zero;
            drive.body.angularVelocity = Vector3.zero;
            MoveFoot(player.driverDoor.position, truckRotation);
            DispatchInput(new[] { Key.F });
            Add("input_f_enters_real_vehicle", player.IsDriving && drive.IsDriven && !pistol.Available && !player.playerBody.enabled,
                "driving=" + player.IsDriving + "; weaponAvailable=" + pistol.Available);
            DispatchInput();
            hose.SetTankLitres(100f);
            DispatchInput(rightMouse: true);
            Add("input_vehicle_rmb_routes_to_cannon_only", player.IsDriving && player.cannon.IsSpraying && !pistol.IsAiming && hose.TankLitres < 100f,
                "cannon=" + player.cannon.IsSpraying + "; ADS=" + pistol.IsAiming + "; tank=" + hose.TankLitres);
            float afterSpray = hose.TankLitres;
            DispatchInput();
            Add("input_rmb_release_stops_cannon", !player.cannon.IsSpraying && Mathf.Approximately(afterSpray, hose.TankLitres),
                "cannon=" + player.cannon.IsSpraying + "; tank=" + hose.TankLitres);

            drive.body.linearVelocity = Vector3.zero;
            drive.body.angularVelocity = Vector3.zero;
            DispatchInput(new[] { Key.F });
            Add("input_f_exits_real_vehicle", !player.IsDriving && !drive.IsDriven && player.playerBody.enabled && pistol.Available,
                "driving=" + player.IsDriving + "; motor=" + player.playerBody.enabled);
            DispatchInput();
            MoveFoot(hose.hoseSocket.position, footRotation);
            DispatchInput(); // Update the actual first-person view/hand after teleporting the test fixture.
            DispatchInput(new[] { Key.E });
            Add("input_e_carries_hose_and_hides_weapon", hose.IsCarried && !pistol.Available && !firstPerson.handsCamera.enabled,
                "carried=" + hose.IsCarried + "; weaponAvailable=" + pistol.Available);
            DispatchInput();
            int ammo = pistol.Ammo;
            DispatchInput(new[] { Key.R });
            Add("input_r_retracts_hose_without_reload", !hose.IsDeployed && !pistol.IsReloading && pistol.Ammo == ammo && pistol.Available,
                "deployed=" + hose.IsDeployed + "; reload=" + pistol.IsReloading + "; ammo=" + pistol.Ammo);
            DispatchInput();
        }
        finally
        {
            Keyboard previousKeyboard = inputSnapshot?.keyboard;
            Mouse previousMouse = inputSnapshot?.mouse;
            CursorLockMode previousCursor = inputSnapshot != null ? inputSnapshot.cursor : Cursor.lockState;
            bool previousVisible = inputSnapshot != null ? inputSnapshot.cursorVisible : Cursor.visible;
            InputSettings previousSettings = inputSnapshot?.settings;
            bool previousRunInBackground = inputSnapshot != null && inputSnapshot.runInBackground;
            RestoreInputDispatch();
            Add("temporary_input_devices_and_cursor_restored", Keyboard.current == previousKeyboard && Mouse.current == previousMouse &&
                Cursor.lockState == previousCursor && Cursor.visible == previousVisible && testKeyboard == null && testMouse == null &&
                InputSystem.settings == previousSettings && Application.runInBackground == previousRunInBackground && !temporaryInputSettings,
                "keyboardRestored=" + (Keyboard.current == previousKeyboard) + "; mouseRestored=" + (Mouse.current == previousMouse) +
                "; cursor=" + Cursor.lockState + "; settingsRestored=" + (InputSystem.settings == previousSettings));
        }
    }

    static void DispatchInput(Key[] keys = null, bool rightMouse = false)
    {
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(keys ?? Array.Empty<Key>()));
        InputSystem.QueueStateEvent(testMouse, new MouseState { buttons = (ushort)(rightMouse ? 2 : 0) });
        // Parameterless Update selects Editor when Game View lacks focus, and Editor has a
        // separate state buffer/press counter. Use the package's typed player update overload.
        // That overload is internal in this Input System version; never change project settings.
        MethodInfo update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(InputUpdateType) }, null);
        if (update == null) throw new MissingMethodException("InputSystem.Update(InputUpdateType)");
        try { update.Invoke(null, new object[] { InputUpdateType.Dynamic }); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        if (InputState.currentUpdateType != InputUpdateType.Dynamic)
            throw new InvalidOperationException("Expected Dynamic input processing. " + InputEvidence());
        testKeyboard.MakeCurrent();
        testMouse.MakeCurrent();
        // The disabled coordinator cannot run twice automatically in this frame. Invoke the
        // real poll/dispatch methods in their normal order immediately after processing events.
        InvokePrivate(player, "Update");
        InvokePrivate(pistol, "Update");
        InvokePrivate(player, "LateUpdate");
        InvokePrivate(pistol, "LateUpdate");
        player.cannon.TickSpray(.05f);
    }

    static string InputEvidence() => "update=" + InputState.currentUpdateType + "; applicationFocused=" + Application.isFocused +
        "; keyboardEnabled=" + (testKeyboard != null && testKeyboard.enabled) + "; updateMode=" + InputSystem.settings.updateMode +
        "; editorInputBehavior=" + InputSystem.settings.editorInputBehaviorInPlayMode;

    static void RestoreInputDispatch()
    {
        if (inputSnapshot == null && testKeyboard == null && testMouse == null && !temporaryInputSettings) return;
        InputSnapshot saved = inputSnapshot;
        inputSnapshot = null;
        try
        {
            if (player && saved != null)
            {
                if (player.cannon) player.cannon.SetSpraying(false);
                if (hose) { hose.Retract(); hose.SetTankLitres(saved.water); }
                player.SetDriving(false);
                MoveFoot(saved.position, saved.rotation);
                player.playerBody.enabled = saved.motorEnabled;
                WritePrivate(player, "lookYaw", saved.yaw);
                WritePrivate(player, "lookPitch", saved.pitch);
                WritePrivate(player, "orbitYaw", saved.orbit);
                WritePrivate(player, "focused", saved.focused);
                firstPerson.SuspendExternalMovement();
                firstPerson.SetInputCaptured(saved.captured);
                pistol.SetExternalInput(false, false, false, false, false, false);
                pistol.SetAvailable(false); // Cancel the test reload and any queued weapon actions.
                pistol.SetAvailable(saved.weaponAvailable);
                if (saved.weaponEquipped && saved.weaponAvailable) pistol.Equip();
                InvokePrivate(player, "LateUpdate");
                if (drive) drive.SetInput(0f, 0f, true);
            }
        }
        finally
        {
            if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
            if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
            testKeyboard = null; testMouse = null;
            if (saved != null)
            {
                if (saved.settings) InputSystem.settings = saved.settings;
                Application.runInBackground = saved.runInBackground;
                if (saved.keyboard != null && saved.keyboard.added) saved.keyboard.MakeCurrent();
                if (saved.mouse != null && saved.mouse.added) saved.mouse.MakeCurrent();
                Cursor.lockState = saved.cursor;
                Cursor.visible = saved.cursorVisible;
            }
            if (temporaryInputSettings) Object.DestroyImmediate(temporaryInputSettings);
            temporaryInputSettings = null;
        }
    }

    static T ReadPrivate<T>(object target, string name) => (T)target.GetType().GetField(name, InstancePrivate).GetValue(target);
    static void WritePrivate(object target, string name, object value) => target.GetType().GetField(name, InstancePrivate).SetValue(target, value);
    static void InvokePrivate(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, InstancePrivate);
        if (method == null) throw new MissingMethodException(target.GetType().Name, name);
        try { method.Invoke(target, arguments); }
        catch (TargetInvocationException error) { throw error.InnerException ?? error; }
    }

    static void RestoreTruck()
    {
        drive.SetInput(0f, 0f, true); drive.SetDriven(false);
        drive.body.linearVelocity = Vector3.zero; drive.body.angularVelocity = Vector3.zero;
        drive.body.position = truckPosition; drive.body.rotation = truckRotation;
        Physics.SyncTransforms();
    }

    static void MoveFoot(Vector3 position, Quaternion rotation)
    {
        bool enabled = player.playerBody.enabled;
        player.playerBody.enabled = false;
        firstPerson.transform.SetPositionAndRotation(position, rotation);
        player.playerBody.enabled = enabled;
        Physics.SyncTransforms();
    }

    static void CheckHoseAndEquipment()
    {
        // This fixture isolates equipment behavior after preparation has unlocked the pump.
        hose.fillingUnlocked = true;
        MoveFoot(hose.hoseSocket.position, footRotation);
        bool picked = hose.TryPickup(player.hand);
        player.RefreshEquipmentState();
        int ammo = pistol.Ammo;
        pistol.Equip();
        bool fired = pistol.Fire(), reloaded = pistol.Reload();
        Add("carried_hose_blocks_weapon_and_hands", picked && hose.IsCarried && !pistol.Available && !fired && !reloaded && pistol.Ammo == ammo && !firstPerson.handsCamera.enabled,
            "picked=" + picked + "; fire=" + fired + "; reload=" + reloaded);
        MoveFoot(player.driverDoor.position, footRotation);
        Add("deployed_hose_blocks_vehicle_entry", !player.EnterVehicle() && !player.IsDriving, "deployed=" + hose.IsDeployed);
        MoveFoot(hose.hoseSocket.position, footRotation);
        bool retracted = player.ReloadOrRetract();
        player.RefreshEquipmentState();
        Add("r_priority_retracts_without_reloading", retracted && !hose.IsDeployed && !pistol.IsReloading && pistol.Ammo == ammo && pistol.Available,
            "returned=" + retracted + "; hoseDeployed=" + hose.IsDeployed + "; reloading=" + pistol.IsReloading);

        WaterSourceVolume source = Object.FindFirstObjectByType<WaterSourceVolume>();
        Vector3 intake = Vector3.zero; bool found = false; float closest = float.PositiveInfinity;
        Bounds bounds = source.volume.bounds;
        for (int x = 0; x <= 20; x++) for (int z = 0; z <= 20; z++)
        {
            Vector3 p = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 20f), source.SurfaceY - .12f, Mathf.Lerp(bounds.min.z, bounds.max.z, z / 20f));
            float distance = Vector3.Distance(p, hose.hoseSocket.position);
            if (distance < closest && hose.CanReach(p) && source.ContainsIntake(p)) { found = true; closest = distance; intake = p; }
        }
        Add("authored_lake_reachable_from_truck", found, "nearestValidIntakeDistance=" + closest + "; maximum=" + hose.maxHoseLength);
        if (found)
        {
            hose.SetTankLitres(100f); hose.Drop(intake); if (!hose.PumpEnabled) hose.TogglePump();
            hose.TickPump(1f);
            Add("actual_lake_fills_actual_truck", hose.IsPumping && hose.TankLitres > 100f,
                "source=" + source.name + "; intake=" + intake + "; tank=" + hose.TankLitres);
            float wetTank = hose.TankLitres;
            hose.Drop(hose.hoseSocket.position + Vector3.up * 2f); hose.TickPump(1f);
            Add("dry_intake_does_not_fill", !hose.IsPumping && Mathf.Approximately(wetTank, hose.TankLitres), "tank=" + hose.TankLitres);
        }
        hose.Retract(); hose.SetTankLitres(originalTank); player.RefreshEquipmentState();
    }

    static void CheckCannonActor()
    {
        var objects = new List<GameObject>();
        try
        {
            Vector3 origin = new Vector3(33000f, 250f, 33000f);
            GameObject rig = new GameObject("Integration check temporary cannon"); objects.Add(rig); rig.transform.position = origin;
            FireHoseSystem tank = rig.AddComponent<FireHoseSystem>(); tank.enabled = false; tank.SetTankLitres(50f);
            FireTruckWaterCannon cannon = rig.AddComponent<FireTruckWaterCannon>(); cannon.enabled = false;
            cannon.tank = tank; cannon.muzzle = rig.transform; cannon.ignoredCollisionRoot = rig.transform;
            GameObject target = new GameObject("Integration check water creature"); objects.Add(target); target.transform.position = origin + Vector3.forward * 10f;
            Collider targetCollider = target.AddComponent<BoxCollider>();
            LavaCreatureActor actor = target.AddComponent<LavaCreatureActor>();
            GameObject wall = new GameObject("Integration check water cover"); objects.Add(wall); wall.transform.position = origin + Vector3.forward * 5f;
            BoxCollider cover = wall.AddComponent<BoxCollider>(); cover.size = new Vector3(3f, 3f, .5f);
            Physics.SyncTransforms(); cannon.SetSpraying(true); AdvanceWater(cannon, .2f);
            cannon.SetSpraying(false); AdvanceWater(cannon, 1f);
            Add("cover_blocks_water_damage_to_creatures", Mathf.Approximately(actor.Health, 100f) && Mathf.Approximately(tank.TankLitres, 43f), "health=" + actor.Health + "; tank=" + tank.TankLitres);
            cover.enabled = false; tank.SetTankLitres(50f); Physics.SyncTransforms();
            cannon.SetSpraying(true); AdvanceWater(cannon, 1f);
            cannon.SetSpraying(false); AdvanceWater(cannon, 1f);
            Add("water_cannon_hits_creature_using_actual_litres", Mathf.Abs(actor.Health - 30f) < .01f && Mathf.Abs(tank.TankLitres - 15f) < .01f,
                "health=" + actor.Health + "; tank=" + tank.TankLitres);
            cannon.SetSpraying(true); AdvanceWater(cannon, 1f);
            cannon.SetSpraying(false); AdvanceWater(cannon, 1f);
            Add("partial_tank_defeats_and_stops_at_zero", actor.IsDefeated && !targetCollider.enabled && Mathf.Approximately(tank.TankLitres, 0f) && !cannon.IsSpraying,
                "health=" + actor.Health + "; tank=" + tank.TankLitres + "; spraying=" + cannon.IsSpraying);
            actor.ApplyDamage(-1f); actor.ApplyWater(float.NaN); cannon.TickSpray(1f);
            Add("defeated_creature_stays_defeated", actor.IsDefeated && Mathf.Approximately(actor.Health, 0f), "health=" + actor.Health);
        }
        finally { foreach (GameObject go in objects) if (go) Object.DestroyImmediate(go); }
    }

    static void AdvanceWater(FireTruckWaterCannon cannon, float seconds)
    {
        while (seconds > .00001f)
        {
            float step = Mathf.Min(.02f, seconds);
            cannon.TickSpray(step);
            seconds -= step;
        }
    }

    static void CheckActualEncounter()
    {
        var spawn = encounter.spawns.First(s => s != null && s.creature && s.feed);
        sequence.enableCameraShake = false;
        // Stage the combat review after evacuation; this is a temporary Play Mode fixture.
        sequence.evacuation = null;
        sequence.missionDirector = null; sequence.cinematic = null;
        sequence.BeginEruption();
        sequence.TickSequence(sequence.warningSeconds + sequence.lavaReleaseDelay + .3f);
        spawn.feed.SetDistance(0f); encounter.TickEncounter(1f);
        Add("creature_stays_hidden_before_local_lava", !spawn.creature.gameObject.activeSelf,
            "stage=" + sequence.Stage + "; localVisible=" + spawn.feed.VisibleDistance);
        float threshold = Mathf.Min(spawn.feed.Length, spawn.feed.Length * Mathf.Clamp01(spawn.feedProgress) +
            (spawn.descendFromLava ? LavaCreatureEncounter.EmergenceLavaMargin : 0f));
        if (threshold > .03f)
        {
            spawn.feed.SetDistance(Mathf.Max(0f, threshold - .03f)); encounter.TickEncounter(1f);
            Add("creature_gate_respects_local_threshold", !spawn.creature.gameObject.activeSelf,
                "visible=" + spawn.feed.VisibleDistance + "; threshold=" + threshold);
        }
        spawn.feed.SetDistance(Mathf.Max(.02f, threshold)); encounter.TickEncounter(Mathf.Max(.1f, spawn.delayAfterArrival + .1f));
        Add("local_lava_releases_authored_creature", spawn.creature.gameObject.activeInHierarchy && encounter.SpawnedCount > 0,
            "spawned=" + encounter.SpawnedCount + "; creature=" + spawn.creature.name);
        float before = spawn.creature.Health;
        spawn.creature.ApplyDamage(34f);
        Add("authored_creature_accepts_damage", Mathf.Abs(spawn.creature.Health - Mathf.Max(0f, before - 34f)) < .01f, "health=" + spawn.creature.Health);
        spawn.creature.ApplyWater(1000f);
        Add("authored_creature_defeat_stops_animation_and_collisions", spawn.creature.IsDefeated &&
            spawn.creature.GetComponentsInChildren<Collider>(true).All(c => !c.enabled) && (!spawn.creature.jump || !spawn.creature.jump.enabled),
            "defeated=" + spawn.creature.IsDefeated);
        encounter.TickEncounter(100f);
        Add("encounter_does_not_resurrect_defeated_creature", spawn.creature.IsDefeated && spawn.creature.Health == 0f, "health=" + spawn.creature.Health);
    }

    static void Add(string name, bool passed, string evidence)
    {
        CurrentReport.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
        Persist();
    }

    static void Persist()
    {
        if (report == null) return;
        string json = JsonUtility.ToJson(report, true);
        SessionState.SetString(ReportKey, json);
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, json);
    }

    static void LogReceived(string message, string trace, LogType type)
    {
        if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying ||
            (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        if (CurrentReport.runtimeErrors.Count < 20)
            CurrentReport.runtimeErrors.Add(type + ": " + message + "\n" + trace);
        Persist();
    }

    static void Finish(string error)
    {
        if (SessionState.GetString(StateKey, "") == "Stopping") return;
        if (!string.IsNullOrEmpty(error)) CurrentReport.error = error;
        RestoreInputDispatch();
        if (shotTarget) Object.DestroyImmediate(shotTarget);
        if (drive) drive.SetInput(0f, 0f, true);
        if (player && player.cannon) player.cannon.SetSpraying(false);
        if (hose) hose.Retract();
        CurrentReport.status = "stopping_play";
        SetPhase("Stopping", 30);
        // Exiting Play Mode restores the saved MainMenu. This never closes the Unity Editor.
        EditorApplication.ExitPlaymode();
    }

    static void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) RestoreInputDispatch();
        if (!SessionState.GetBool(ActiveKey, false) || state != PlayModeStateChange.EnteredEditMode) return;
        string phase = SessionState.GetString(StateKey, "");
        if (phase != "Stopping" && string.IsNullOrEmpty(CurrentReport.error))
            CurrentReport.error = "Play Mode ended before the integration run completed (phase=" + phase + ").";
        CurrentReport.editorScene = SceneManager.GetActiveScene().path;
        CurrentReport.checks.Add(new Check { name = "edit_mode_returns_to_main_menu", passed = CurrentReport.editorScene == MenuPath, evidence = CurrentReport.editorScene });
        CurrentReport.finishedUtc = DateTime.UtcNow.ToString("O");
        CurrentReport.allPassed = string.IsNullOrEmpty(CurrentReport.error) && CurrentReport.runtimeErrors.Count == 0 &&
            CurrentReport.checks.Count > 0 && CurrentReport.checks.All(c => c.passed);
        CurrentReport.status = CurrentReport.allPassed ? "passed" : "failed";
        Persist();
        SessionState.SetBool(ActiveKey, false);
        Debug.Log("Volcano integration checks " + CurrentReport.status + "; " + CurrentReport.checks.Count + " checks + " + CurrentReport.regressionChecks + " regression checks. Report: " + ReportPath);
    }
}
