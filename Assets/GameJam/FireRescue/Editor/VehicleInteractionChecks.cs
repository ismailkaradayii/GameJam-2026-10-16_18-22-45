using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VehicleInteractionChecks
{
    const string Pending = "VehicleInteractionChecks.Pending", Report = "Logs/VehicleInteractionChecks.txt";
    static readonly List<string> lines = new List<string>();
    static FireRescuePlayer player;
    static VehicleInteractionInputFixture fixture;
    static Keyboard keyboard, oldKeyboard;
    static bool oldBackground;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static int phase = -1, frame = -1;
    static float phaseAt;
    static double deadline;
    static string error;

    static VehicleInteractionChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check E vehicle interaction")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start in Edit Mode.");
        File.WriteAllText(Report, "STARTING\n");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        try
        {
            lines.Clear(); error = null;
            lines.Add("Saved-scene Play Mode check using synthetic keyboard events and controlled Rigidbody velocity before the real player Update; no full driving route is claimed.");
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            var director = player.mission; director.enabled = false;
            if (director.story) director.story.enabled = false;
            if (director.cinematic) { director.cinematic.EndShot(); director.cinematic.enabled = false; }
            director.sequence.enabled = false; if (director.encounter) director.encounter.enabled = false;
            player.SetCinematicLocked(false); player.SetGameplaySuspended(false);
            Time.timeScale = 1;
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            oldInputBackground = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            oldKeyboard = Keyboard.current; keyboard = InputSystem.AddDevice<Keyboard>(); keyboard.MakeCurrent();
            EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
            Application.logMessageReceived += Log;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "Vehicle input check ground";
            ground.transform.position = new Vector3(19000,499.5f,19000); ground.transform.localScale = new Vector3(100,1,100);
            player.drive.enabled = false; player.drive.body.useGravity = false; player.drive.body.interpolation = RigidbodyInterpolation.None;
            fixture = player.gameObject.AddComponent<VehicleInteractionInputFixture>(); fixture.player = player;
            fixture.anchor = new Vector3(19000,500.7f,19000); fixture.speedKph = 30;
            player.drive.body.position = fixture.anchor; player.drive.body.rotation = Quaternion.identity;
            player.hose.Retract(); player.SetDriving(true); Keys(); Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("phase=" + phase + " driving=" + player.IsDriving);
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.unscaledTime - phaseAt;
        var ui = player.contextualInteractions;
        var exit = ui.targets.First(t => t && t.kind == ContextualInteractionKind.TruckExit);
        switch (phase)
        {
            case 0:
                if (age < .6f) return;
                Check("entry and exit both display E", player.interactKey == Key.E && player.exitVehicleKey == Key.E && ui.Label(InteractionInput.Interact) == "[E]" && ui.Label(InteractionInput.ExitVehicle) == "[E]");
                Keys(Key.E); Next(1); return;
            case 1:
                if (age < .15f) return;
                Check("E cannot exit at 30 km/h", player.IsDriving && player.ActiveNotice.Contains("20") && ui.Describe(exit, out var fastExit) && fastExit.blocked);
                Keys(); fixture.speedKph = 20; Next(2); return;
            case 2:
                if (age < .15f) return;
                Check("exactly 20 km/h remains blocked", player.drive.SpeedKph >= 20 && !player.CanExitAtCurrentSpeed && !player.ExitVehicle());
                fixture.speedKph = 19.9f; Next(3); return;
            case 3:
                if (age < .15f) return;
                Check("prompt permits exit just below 20 km/h", ui.Describe(exit, out var slowExit) && !slowExit.blocked);
                Keys(Key.E); Next(4); return;
            case 4:
                if (age < .3f) return;
                Check("E exits while moving at 19.9 km/h", !player.IsDriving && player.playerBody.enabled && !player.drive.IsDriven);
                Check("holding the exit press does not immediately re-enter", !player.IsDriving);
                Keys(); fixture.speedKph = 0;
                fixture.focus = player.driverDoor;
                Vector3 feet = player.driverDoor.position - player.drive.transform.right * 1.2f; feet.y = 500.12f;
                player.PlaceOnFoot(feet, 0); Next(5); return;
            case 5:
                if (age < .3f) return;
                ui.RefreshSelection();
                Check("real cab prompt offers entry with E", player.CanReachVehicleEntry() && ui.CurrentTarget && ui.CurrentTarget.kind == ContextualInteractionKind.TruckEntry && ui.Label(ui.CurrentOffer.input) == "[E]");
                Keys(Key.E); Next(6); return;
            case 6:
                if (age < .3f) return;
                Check("one E press enters and stays in the vehicle", player.IsDriving && !player.playerBody.enabled && player.drive.IsDriven);
                Keys(); fixture.focus = null; fixture.speedKph = -15; Next(7); return;
            case 7:
                if (age < .15f) return;
                Keys(Key.E); Next(8); return;
            case 8:
                if (age < .2f) return;
                Check("E also exits while reversing below 20 km/h", !player.IsDriving && player.playerBody.enabled);
                Keys(); fixture.speedKph = 0; player.SetDriving(true); Next(9); return;
            case 9:
                if (age < .2f) return;
                Keys(Key.F); Next(10); return;
            case 10:
                if (age < .2f) return;
                Check("old F binding no longer exits the truck", player.IsDriving);
                Keys(); player.hose.Retract(); player.hose.SetTankLitres(0);
                fixture.anchor = player.mission.fillStation.transform.position + Vector3.up * .65f;
                Next(11); return;
            case 11:
                if (age < .3f) return;
                var lake = ui.targets.First(t => t && t.kind == ContextualInteractionKind.LakeFill);
                Check("lake prompt uses F without stealing E", player.mission.fillStation.CanFill && ui.Describe(lake, out var fill) && fill.input == InteractionInput.Pump && ui.Label(fill.input) == "[F]");
                Keys(Key.P); Next(111); return;
            case 111:
                if (age < .2f) return;
                Check("old P binding no longer starts lake filling", !player.mission.fillStation.IsFilling && !player.hose.IsPumping);
                Keys(Key.F); Next(12); return;
            case 12:
                if (age < .4f) return;
                Check("F starts actual water transfer and leaves the driver seated", player.IsDriving && player.mission.fillStation.IsFilling && player.hose.IsPumping && player.hose.TankLitres > 0);
                Keys(); Next(13); return;
            case 13:
                if (age < .15f) return;
                Keys(Key.E); Next(14); return;
            case 14:
                if (age < .2f) return;
                Check("E exits at the lake and immediately disconnects the intake", !player.IsDriving && !player.mission.fillStation.IsFilling && !player.hose.IsDeployed && !player.hose.IsPumping);
                Finish(null); return;
        }
    }
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    static void Next(int value) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 30; }
    static void Check(string label, bool success)
    {
        lines.Add((success ? "PASS " : "FAIL ") + label); File.WriteAllLines(Report,lines);
        if (!success) throw new InvalidOperationException(label + "; phase=" + phase + "; speed=" + player.drive.SpeedKph + "; driving=" + player.IsDriving + "; notice=" + player.ActiveNotice);
    }
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message + "\n" + stack; }
    static void Finish(string failure)
    {
        phase = -1; Application.logMessageReceived -= Log;
        if (keyboard != null) { InputSystem.RemoveDevice(keyboard); keyboard = null; }
        if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        Application.runInBackground = oldBackground; InputSystem.settings.backgroundBehavior = oldInputBackground;
        lines.Add(failure == null ? "COMPLETED" : "FAILED " + failure); File.WriteAllLines(Report,lines);
        EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(-100)]
public sealed class VehicleInteractionInputFixture : MonoBehaviour
{
    public FireRescuePlayer player;
    public Vector3 anchor;
    public float speedKph;
    public Transform focus;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo Focused = typeof(FireRescuePlayer).GetField("focused", Private);
    static readonly FieldInfo Yaw = typeof(FireRescuePlayer).GetField("lookYaw", Private);
    static readonly FieldInfo Pitch = typeof(FireRescuePlayer).GetField("lookPitch", Private);
    void Update()
    {
        if (!player) return;
        player.drive.body.position = anchor;
        player.drive.body.linearVelocity = player.IsDriving ? player.drive.transform.forward * (speedKph / 3.6f) : Vector3.zero;
        player.drive.body.angularVelocity = Vector3.zero;
        Focused.SetValue(player,true); Cursor.lockState = CursorLockMode.Locked;
        if (Mouse.current != null) InputSystem.ResetDevice(Mouse.current);
        if (!focus) return;
        Vector3 delta = focus.position - (player.transform.position + Vector3.up * player.eyeHeight);
        Yaw.SetValue(player,Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg);
        Pitch.SetValue(player,-Mathf.Atan2(delta.y,new Vector2(delta.x,delta.z).magnitude)*Mathf.Rad2Deg);
    }
}
