using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class GameplayFeelChecks
{
    const string Pending = "GameplayFeelChecks.Pending";
    static readonly List<string> lines = new List<string>();
    static FireRescuePlayer player;
    static VolcanoMissionDirector mission;
    static Keyboard keyboard;
    static InputSettings originalInput, testInput;
    static bool oldBackground;
    static int phase = -1, frame;
    static float phaseAt, nextSample, peak;
    static Vector3 start;
    static double deadline;
    static string runtimeError;
    static GameplayFeelChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (s == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Run interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Probe truck input")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); runtimeError = null; frame = -1;
        lines.Add("Actual saved truck parking and terrain, unchanged colliders, input through a temporary Input System keyboard. No truck teleport or velocity writes.");
        player = Object.FindFirstObjectByType<FireRescuePlayer>(); mission = player.mission;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        originalInput = InputSystem.settings; testInput = Object.Instantiate(originalInput);
        testInput.hideFlags = HideFlags.HideAndDontSave;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings = testInput; keyboard = InputSystem.AddDevice<Keyboard>("GameplayFeelKeyboard");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        Application.logMessageReceived += Log;
        Next(0, 30);
    }
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = message; }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase);
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.time - phaseAt;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        if (phase == 0)
        {
            if (mission.cinematic.IsPlaying || mission.State != VolcanoMissionDirector.FlowState.Preparation) return;
            GameplayFeelSetup.Audit();
            player.SetDriving(true); start = player.drive.transform.position; peak = 0; nextSample = 0;
            Next(1, 15); return;
        }
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(phase == 1 ? Key.W : Key.Space));
        peak = Mathf.Max(peak, player.drive.SpeedKph);
        if (age >= nextSample)
        {
            nextSample += .5f;
            var d = player.drive;
            lines.Add($"t={age:F2} phase={phase} pos={d.transform.position} speed={d.SpeedKph:F2} dt={Time.deltaTime:F3} input={keyboard.wKey.isPressed} locked={Cursor.lockState} drive={d.IsDriven} suspended={player.GameplaySuspended}");
            foreach (var w in d.wheels)
                lines.Add($"  {w.name} grounded={w.GetGroundHit(out var hit)} motor={w.motorTorque:F0} brake={w.brakeTorque:F0} rpm={w.rpm:F0} hit={hit.collider} force={hit.force:F0} slip={hit.forwardSlip:F2}");
            File.WriteAllLines("Logs/GameplayFeel-drive-probe.txt", lines);
        }
        if (phase == 1 && age > 5) { nextSample = 0; Next(2, 10); return; }
        if (phase == 2 && (player.drive.SpeedKph < 2 || age > 4))
        {
            lines.Add($"RESULT peak={peak:F2} travel={Vector3.Distance(start, player.drive.transform.position):F2} stop={player.drive.SpeedKph:F2}");
            Finish(null);
        }
    }
    static void Next(int value, double seconds) { phase = value; phaseAt = Time.time; deadline = EditorApplication.timeSinceStartup + seconds; }
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if (originalInput) InputSystem.settings = originalInput;
        if (testInput) Object.DestroyImmediate(testInput);
        Application.runInBackground = oldBackground;
        lines.Add(error == null ? "COMPLETED" : "FAILED " + error); File.WriteAllLines("Logs/GameplayFeel-drive-probe.txt", lines);
        EditorApplication.ExitPlaymode();
    }
}
