using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

public static class CannonInputChecks
{
    [Serializable] sealed class Check { public string name; public bool passed; public string evidence; }
    [Serializable] sealed class Report
    {
        public string status = "running", phase, error;
        public bool allPassed;
        public float simulationSeconds;
        public List<Check> checks = new List<Check>();
    }
    static FireRescuePlayer player;
    static FireTruckWaterCannon cannon;
    static FireHoseSystem tank;
    static Report report;
    static bool running, captured, originalDriving, playerEnabled, cannonEnabled, cursorVisible;
    static CursorLockMode cursor;
    static Vector3 playerPosition, velocity, angularVelocity;
    static Quaternion playerRotation;
    static Quaternion initialTurretYaw, initialTurretPitch;
    static float originalTank, stoppedTank;
    static int phase;
    static double phaseStarted, started, wallStarted;

    [MenuItem("GameJam/Fire Rescue/Check cannon input (practice Play Mode)")]
    public static void Begin()
    {
        if (running || !EditorApplication.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Run once in an unpaused practice Play scene.");
        running = true; captured = false; report = new Report();
        started = Time.timeAsDouble; wallStarted = EditorApplication.timeSinceStartup;
        try
        {
            player = UnityEngine.Object.FindFirstObjectByType<FireRescuePlayer>();
            cannon = player ? player.cannon : null; tank = cannon ? cannon.tank : null;
            if (!player || !player.playerBody || !player.drive || !cannon || !tank || Keyboard.current == null || Mouse.current == null)
                throw new InvalidOperationException("Missing player/cannon/tank or native input devices.");
            if (tank.IsDeployed) throw new InvalidOperationException("Retract the intake hose before checking cannon inputs.");
            originalTank = tank.TankLitres; originalDriving = player.IsDriving;
            playerEnabled = player.enabled; cannonEnabled = cannon.enabled;
            playerPosition = player.playerBody.transform.position; playerRotation = player.playerBody.transform.rotation;
            velocity = player.drive.body.linearVelocity; angularVelocity = player.drive.body.angularVelocity;
            cursor = Cursor.lockState; cursorVisible = Cursor.visible; captured = true;
            player.enabled = true; cannon.enabled = true; tank.SetTankLitres(500f);
            player.drive.body.linearVelocity = Vector3.zero; player.drive.body.angularVelocity = Vector3.zero;
            player.SetDriving(true); MouseButtons(false); Keys();
            Type gameView = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameView != null) EditorWindow.GetWindow(gameView).Focus();
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Reload;
            Next(0, "focus_settle");
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    static void Tick()
    {
        if (!running) return;
        try
        {
            if (!EditorApplication.isPlaying || !player) throw new InvalidOperationException("Play ended during input checks.");
            if (EditorApplication.timeSinceStartup - wallStarted > 45) throw new TimeoutException("Input checks stalled.");
            if (EditorApplication.isPaused) return;
            double elapsed = Time.timeAsDouble - phaseStarted;
            switch (phase)
            {
                case 0:
                    if (elapsed < .4) return;
                    Require("input_gate_ready", Focused() && Cursor.lockState == CursorLockMode.Locked);
                    Require("cannon_uses_player_camera", cannon.aimCamera == player.viewCamera && cannon.aimCamera != null);
                    if (!cannon.yawPivot || !cannon.pitchPivot) throw new InvalidOperationException("Cannon aim pivots are missing.");
                    initialTurretYaw = cannon.yawPivot.rotation; initialTurretPitch = cannon.pitchPivot.localRotation;
                    // Raise the aim from the default 18 degrees so the monitor leaves its depression limit.
                    MouseButtons(true, new Vector2(300f, 200f)); Next(1, "right_button_hold_and_mouse_aim"); break;
                case 1:
                    if (elapsed < .5) return;
                    Require("right_hold_sprays_and_consumes", cannon.IsSpraying && tank.TankLitres > 0 && tank.TankLitres < 500);
                    float yawChange = Quaternion.Angle(initialTurretYaw, cannon.yawPivot.rotation);
                    float pitchChange = Quaternion.Angle(initialTurretPitch, cannon.pitchPivot.localRotation);
                    Require("mouse_turns_cannon_yaw_and_pitch", yawChange > 5f && pitchChange > 2f, "; yawChangeDegrees=" + yawChange + "; pitchChangeDegrees=" + pitchChange);
                    MouseButtons(false); Next(2, "release_settle"); break;
                case 2:
                    if (elapsed < .2) return;
                    Require("release_stops_jet", !cannon.IsSpraying); stoppedTank = tank.TankLitres;
                    Next(3, "released_water_stable"); break;
                case 3:
                    if (elapsed < .3) return;
                    Require("release_stops_consumption", Mathf.Abs(tank.TankLitres - stoppedTank) < .01f);
                    tank.SetTankLitres(0); MouseButtons(true); Next(4, "empty_tank_hold"); break;
                case 4:
                    if (elapsed < .3) return;
                    Require("empty_tank_blocks_jet", !cannon.IsSpraying && tank.TankLitres == 0);
                    tank.SetTankLitres(100); Next(5, "refilled_held_button"); break;
                case 5:
                    if (elapsed < .3) return;
                    Require("refill_resumes_held_spray", cannon.IsSpraying && tank.TankLitres > 0 && tank.TankLitres < 100);
                    Keys(Key.F); Next(6, "F_exit_while_right_held"); break;
                case 6:
                    if (elapsed < .2) return;
                    Keys(); Next(7, "F_exit_release"); break;
                case 7:
                    if (elapsed < .2) return;
                    Require("F_exit_stops_spray", !player.IsDriving && !cannon.IsSpraying);
                    player.SetDriving(true); tank.SetTankLitres(100); Next(8, "prepare_escape_with_live_jet"); break;
                case 8:
                    if (elapsed < .3) return;
                    Require("jet_active_before_escape", cannon.IsSpraying);
                    Keys(Key.Escape); Next(9, "escape_press"); break;
                case 9:
                    if (elapsed < .2) return;
                    Keys(); MouseButtons(false); stoppedTank = tank.TankLitres; Next(10, "escape_release"); break;
                case 10:
                    if (elapsed < .2) return;
                    Require("escape_unlocks_and_stops", Cursor.lockState == CursorLockMode.None && !cannon.IsSpraying && Mathf.Abs(tank.TankLitres - stoppedTank) < .01f);
                    Finish(null); break;
            }
        }
        catch (Exception error) { Finish(error.ToString()); }
    }

    static bool Focused() => (bool)typeof(FireRescuePlayer).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player);
    static void MouseButtons(bool held, Vector2 delta = default) => InputSystem.QueueStateEvent(Mouse.current, new MouseState { buttons = (ushort)(held ? 2 : 0), delta = delta });
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));
    static void Next(int value, string name) { phase = value; phaseStarted = Time.timeAsDouble; report.phase = name; Write(); }
    static void Require(string name, bool passed, string extra = "")
    {
        string evidence = "litres=" + tank.TankLitres + "; spraying=" + cannon.IsSpraying + "; driving=" + player.IsDriving + "; focused=" + Focused() + "; cursor=" + Cursor.lockState + "; applicationFocused=" + Application.isFocused + "; RMB=" + Mouse.current.rightButton.isPressed + extra;
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Write();
        if (!passed) throw new InvalidOperationException(name + ": " + evidence);
    }
    static void Finish(string error)
    {
        if (!running) return;
        running = false; EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayChanged; AssemblyReloadEvents.beforeAssemblyReload -= Reload;
        report.error = error;
        try
        {
            if (Mouse.current != null) MouseButtons(false); if (Keyboard.current != null) Keys();
            if (captured && player && cannon && tank)
            {
                cannon.SetSpraying(false); tank.SetTankLitres(originalTank);
                player.playerBody.enabled = false; player.playerBody.transform.SetPositionAndRotation(playerPosition, playerRotation);
                player.SetDriving(originalDriving); player.enabled = playerEnabled; cannon.enabled = cannonEnabled;
                player.drive.body.linearVelocity = velocity; player.drive.body.angularVelocity = angularVelocity;
                Physics.SyncTransforms(); Cursor.lockState = cursor; Cursor.visible = cursorVisible;
            }
        }
        catch (Exception cleanup) { report.error = (report.error ?? "") + "\nRestore: " + cleanup; }
        report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
        foreach (Check check in report.checks) report.allPassed &= check.passed;
        report.status = string.IsNullOrEmpty(report.error) ? "completed" : "error"; report.phase = "finished"; Write();
    }
    static void Write()
    {
        report.simulationSeconds = (float)(Time.timeAsDouble - started);
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/CannonInputChecks.json")), JsonUtility.ToJson(report, true));
    }
    static void PlayChanged(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode) Finish("Play ended before completion."); }
    static void Reload() => Finish("Compilation interrupted input checks.");
}
