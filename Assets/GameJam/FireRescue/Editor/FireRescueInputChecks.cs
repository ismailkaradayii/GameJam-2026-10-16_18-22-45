using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>Runs the actual Update input path in an already-playing truck practice scene.</summary>
public static class FireRescueInputChecks
{
    [Serializable] sealed class Check { public string name; public bool passed; public string evidence; }
    [Serializable] sealed class Report
    {
        public string status = "running";
        public string phase;
        public string scene;
        public string error;
        public string startedUtc;
        public string finishedUtc;
        public float simulationSeconds;
        public bool allPassed;
        public List<Check> checks = new List<Check>();
    }

    static FireRescuePlayer player;
    static FireTruckDrive drive;
    static FireHoseSystem hose;
    static WaterSourceVolume water;
    static Keyboard keyboard;
    static Mouse mouse;
    static Report report;
    static bool running;
    static bool captured;
    static int phase;
    static double phaseStarted;
    static double simulationStarted;
    static double wallStarted;
    static Vector3 playerPosition;
    static Quaternion playerRotation;
    static Vector3 truckPosition;
    static Quaternion truckRotation;
    static Vector3 truckVelocity;
    static Vector3 truckAngularVelocity;
    static bool playerEnabled;
    static bool hoseEnabled;
    static bool originalDriving;
    static float originalTank;
    static float originalYaw;
    static float originalPitch;
    static CursorLockMode originalCursor;
    static bool originalCursorVisible;
    static Vector3 targetIntake;

    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("GameJam/Fire Rescue/Check keyboard and hose (practice scene in Play)", true)]
    static bool CanRun() => EditorApplication.isPlaying && !EditorApplication.isPaused &&
        SceneManager.GetActiveScene().path == "Assets/Scenes/İtfaiye aracı.unity";

    [MenuItem("GameJam/Fire Rescue/Check keyboard and hose (practice scene in Play)")]
    public static void Begin()
    {
        if (running) throw new InvalidOperationException("Input checks are already running.");
        if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Begin requires an unpaused, already-playing practice scene.");
        report = new Report { scene = SceneManager.GetActiveScene().path, startedUtc = DateTime.UtcNow.ToString("O") };
        running = true;
        captured = false;
        simulationStarted = Time.timeAsDouble;
        wallStarted = EditorApplication.timeSinceStartup;
        try
        {
            player = UnityEngine.Object.FindFirstObjectByType<FireRescuePlayer>();
            drive = player ? player.drive : null;
            hose = player ? player.hose : null;
            keyboard = Keyboard.current;
            mouse = Mouse.current;
            if (!player || !player.playerBody || !player.viewCamera || !player.hand || !drive || !drive.body || !hose || !hose.hoseSocket)
                throw new InvalidOperationException("Missing player, truck, hose or scene references.");
            if (keyboard == null || mouse == null)
                throw new InvalidOperationException("The real Keyboard.current and Mouse.current must exist.");
            if (hose.IsDeployed)
                throw new InvalidOperationException("Retract the hose before starting this reversible input check.");

            water = null;
            float nearest = float.PositiveInfinity;
            foreach (WaterSourceVolume source in WaterSourceVolume.ActiveSources)
            {
                if (!source || !source.volume) continue;
                float distance = (source.volume.bounds.center - hose.hoseSocket.position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; water = source; }
            }
            if (!water) throw new InvalidOperationException("No active water source.");

            playerPosition = player.playerBody.transform.position;
            playerRotation = player.playerBody.transform.rotation;
            truckPosition = drive.body.position;
            truckRotation = drive.body.rotation;
            truckVelocity = drive.body.linearVelocity;
            truckAngularVelocity = drive.body.angularVelocity;
            playerEnabled = player.enabled;
            hoseEnabled = hose.enabled;
            originalDriving = player.IsDriving;
            originalTank = hose.TankLitres;
            originalYaw = ReadFloat("lookYaw");
            originalPitch = ReadFloat("lookPitch");
            originalCursor = Cursor.lockState;
            originalCursorVisible = Cursor.visible;
            captured = true;

            player.enabled = true;
            hose.enabled = true;
            hose.Retract();
            hose.SetTankLitres(0f);
            drive.body.linearVelocity = Vector3.zero;
            drive.body.angularVelocity = Vector3.zero;
            player.SetDriving(true);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Type gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType != null) EditorWindow.GetWindow(gameViewType).Focus();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
            EnterPhase(0, "focus_and_settle");
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    static void Update()
    {
        if (!running) return;
        try
        {
            if (!EditorApplication.isPlaying || !player || !drive || !hose)
                throw new InvalidOperationException("Play Mode or a required object ended during input checks.");
            if (EditorApplication.timeSinceStartup - wallStarted > 60d)
                throw new TimeoutException("Input check timed out; inspect focus, pause, time scale and reported gating.");
            if (EditorApplication.isPaused) return;
            double elapsed = Time.timeAsDouble - phaseStarted;
            switch (phase)
            {
                case 0:
                    if (elapsed < 0.6d) return;
                    Require("input_gate_ready", player.enabled && ReadFocused() && Cursor.lockState == CursorLockMode.Locked, GateEvidence());
                    Press(Key.E, 1, "E_exit_press");
                    break;
                case 1:
                    if (elapsed < 0.2d) return;
                    Release(2, "E_exit_release");
                    break;
                case 2:
                    if (elapsed < 0.2d) return;
                    Require("E_exits_vehicle", !player.IsDriving && player.playerBody.enabled && !drive.IsDriven, StateEvidence());
                    Press(Key.E, 3, "E_enter_press");
                    break;
                case 3:
                    if (elapsed < 0.2d) return;
                    Release(4, "E_enter_release");
                    break;
                case 4:
                    if (elapsed < 0.2d) return;
                    Require("E_enters_vehicle", player.IsDriving && !player.playerBody.enabled && drive.IsDriven, StateEvidence());
                    Press(Key.E, 5, "E_exit_again_press");
                    break;
                case 5:
                    if (elapsed < 0.2d) return;
                    Release(6, "E_exit_again_release");
                    break;
                case 6:
                    if (elapsed < 0.2d) return;
                    Require("E_exits_again", !player.IsDriving && player.playerBody.enabled, StateEvidence());
                    PlacePlayer(FindGround(hose.hoseSocket.position + hose.hoseSocket.forward * 1.25f));
                    AimWithMouse(hose.hoseSocket.position);
                    EnterPhase(7, "rear_pickup_position_settle");
                    break;
                case 7:
                    if (elapsed < 0.2d) return;
                    Press(Key.E, 8, "E_pickup_press");
                    break;
                case 8:
                    if (elapsed < 0.2d) return;
                    Release(9, "E_pickup_release");
                    break;
                case 9:
                    if (elapsed < 0.2d) return;
                    Require("E_picks_up_hose", hose.IsCarried && hose.IsDeployed, StateEvidence());
                    PlaceAtWaterEdge();
                    EnterPhase(10, "water_aim_settle");
                    break;
                case 10:
                    if (elapsed < 0.3d) return;
                    Vector3 direction = (targetIntake - player.viewCamera.transform.position).normalized;
                    float aimError = Vector3.Angle(direction, player.viewCamera.transform.forward);
                    Require("mouse_aims_at_water", aimError < 2f, "Aim error degrees=" + aimError + "; target=" + targetIntake + "; camera=" + player.viewCamera.transform.position + "; " + GateEvidence());
                    Press(Key.E, 11, "E_water_drop_press");
                    break;
                case 11:
                    if (elapsed < 0.2d) return;
                    Release(12, "E_water_drop_release");
                    break;
                case 12:
                    if (elapsed < 0.2d) return;
                    Require("E_drops_intake_in_water", hose.IsDeployed && !hose.IsCarried && water.ContainsIntake(hose.nozzle.position), StateEvidence() + "; nozzle=" + hose.nozzle.position + "; surface=" + water.SurfaceY);
                    Press(Key.P, 13, "P_pump_press");
                    break;
                case 13:
                    if (elapsed < 0.2d) return;
                    Release(14, "P_pump_fill_half_second");
                    break;
                case 14:
                    if (elapsed < 0.5d) return;
                    Require("P_pumps_and_fills_tank", hose.PumpEnabled && hose.IsPumping && hose.TankLitres > 0f, StateEvidence());
                    Finish(null);
                    break;
            }
        }
        catch (Exception exception) { Finish(exception.ToString()); }
    }

    static void PlacePlayer(Vector3 feet)
    {
        player.playerBody.enabled = false;
        player.playerBody.transform.position = feet;
        player.playerBody.enabled = true;
        Physics.SyncTransforms();
    }

    static void PlaceAtWaterEdge()
    {
        BoxCollider box = water.volume;
        Vector3 towardTruck = Vector3.ProjectOnPlane(hose.hoseSocket.position - box.bounds.center, Vector3.up).normalized;
        if (towardTruck.sqrMagnitude < 0.01f) towardTruck = Vector3.forward;
        Vector3 localDirection = box.transform.InverseTransformDirection(towardTruck);
        localDirection.y = 0f;
        localDirection.Normalize();
        Vector3 half = box.size * 0.5f;
        float xEdge = Mathf.Abs(localDirection.x) > 0.001f ? half.x / Mathf.Abs(localDirection.x) : float.PositiveInfinity;
        float zEdge = Mathf.Abs(localDirection.z) > 0.001f ? half.z / Mathf.Abs(localDirection.z) : float.PositiveInfinity;
        float edge = Mathf.Min(xEdge, zEdge);
        targetIntake = box.transform.TransformPoint(box.center + localDirection * Mathf.Max(0f, edge - 2f));
        targetIntake.y = water.SurfaceY - 0.12f;
        Vector3 feet = box.transform.TransformPoint(box.center + localDirection * (edge + 1.5f));
        feet.y = water.SurfaceY;
        PlacePlayer(FindGround(feet));
        AimWithMouse(targetIntake);
        Require("reachable_water_fixture", water.ContainsIntake(targetIntake) && hose.CanReach(targetIntake), "Water=" + water.name + "; target=" + targetIntake + "; feet=" + player.playerBody.transform.position + "; hose distance=" + Vector3.Distance(targetIntake, hose.hoseSocket.position));
    }

    static void AimWithMouse(Vector3 target)
    {
        Vector3 eye = player.playerBody.transform.position + Vector3.up * player.eyeHeight;
        Vector3 desired = (target - eye).normalized;
        Vector3 current = player.viewCamera.transform.forward;
        float yaw = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Asin(desired.y) * Mathf.Rad2Deg;
        float currentYaw = Mathf.Atan2(current.x, current.z) * Mathf.Rad2Deg;
        float currentPitch = -Mathf.Asin(current.y) * Mathf.Rad2Deg;
        Vector2 delta = new Vector2(Mathf.DeltaAngle(currentYaw, yaw), currentPitch - pitch) / player.mouseSensitivity;
        InputSystem.QueueStateEvent(mouse, new MouseState { delta = delta });
    }

    static Vector3 FindGround(Vector3 point)
    {
        RaycastHit[] hits = Physics.RaycastAll(point + Vector3.up * 3f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(player.playerBody.transform) || hit.transform.IsChildOf(drive.transform)) continue;
            return hit.point + Vector3.up * 0.12f;
        }
        throw new InvalidOperationException("No ground at the input-test player fixture: " + point);
    }

    static void Press(Key key, int nextPhase, string name)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
        EnterPhase(nextPhase, name);
    }

    static void Release(int nextPhase, string name)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState());
        EnterPhase(nextPhase, name);
    }

    static void EnterPhase(int value, string name)
    {
        phase = value;
        phaseStarted = Time.timeAsDouble;
        report.phase = name;
        WriteReport();
    }

    static bool ReadFocused() => (bool)typeof(FireRescuePlayer).GetField("focused", PrivateInstance).GetValue(player);
    static float ReadFloat(string name) => (float)typeof(FireRescuePlayer).GetField(name, PrivateInstance).GetValue(player);
    static string GateEvidence() => "focused=" + ReadFocused() + "; cursor=" + Cursor.lockState + "; playerEnabled=" + player.enabled + "; applicationFocused=" + Application.isFocused + "; keyboardEnabled=" + keyboard.enabled + "; editorWindow=" + (EditorWindow.focusedWindow ? EditorWindow.focusedWindow.GetType().Name : "none");
    static string StateEvidence() => "driving=" + player.IsDriving + "; speedKph=" + drive.SpeedKph + "; carried=" + hose.IsCarried + "; deployed=" + hose.IsDeployed + "; pumping=" + hose.IsPumping + "; litres=" + hose.TankLitres + "; hoseStatus=" + hose.Status + "; " + GateEvidence();

    static void Require(string name, bool passed, string evidence)
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
        WriteReport();
        if (!passed) throw new InvalidOperationException(name + " failed: " + evidence);
    }

    static void Finish(string error)
    {
        if (!running) return;
        running = false;
        EditorApplication.update -= Update;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= OnAssemblyReload;
        report.error = error;
        try
        {
            if (keyboard != null && keyboard.added) InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            if (captured && player && drive && hose)
            {
                hose.Retract();
                hose.SetTankLitres(originalTank);
                hose.enabled = hoseEnabled;
                drive.body.position = truckPosition;
                drive.body.rotation = truckRotation;
                drive.body.linearVelocity = truckVelocity;
                drive.body.angularVelocity = truckAngularVelocity;
                player.playerBody.enabled = false;
                player.playerBody.transform.SetPositionAndRotation(playerPosition, playerRotation);
                player.SetDriving(originalDriving);
                typeof(FireRescuePlayer).GetField("lookYaw", PrivateInstance).SetValue(player, originalYaw);
                typeof(FireRescuePlayer).GetField("lookPitch", PrivateInstance).SetValue(player, originalPitch);
                player.enabled = playerEnabled;
                Physics.SyncTransforms();
                Cursor.lockState = originalCursor;
                Cursor.visible = originalCursorVisible;
            }
        }
        catch (Exception cleanup) { report.error = (report.error ?? "") + "\nCleanup: " + cleanup; }
        report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
        foreach (Check check in report.checks) report.allPassed &= check.passed;
        report.status = string.IsNullOrEmpty(report.error) ? "completed" : "error";
        report.phase = "finished";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        WriteReport();
        Debug.Log("Fire rescue input checks " + report.status + "; allPassed=" + report.allPassed + ". Evidence: Library/FireRescueInputChecks.json");
    }

    static void WriteReport()
    {
        report.simulationSeconds = (float)(Time.timeAsDouble - simulationStarted);
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/FireRescueInputChecks.json")), JsonUtility.ToJson(report, true));
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode) Finish("Play Mode ended before input checks completed.");
    }

    static void OnAssemblyReload() => Finish("Assembly reload interrupted input checks.");
}
