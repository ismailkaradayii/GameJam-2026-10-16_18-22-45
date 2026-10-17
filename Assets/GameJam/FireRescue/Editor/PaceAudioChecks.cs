using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Checks the saved controllers on a temporary flat surface; never saves test objects.</summary>
[InitializeOnLoad]
public static class PaceAudioChecks
{
    const string Pending = "PaceAudioChecks.Pending";
    const string Report = "Logs/PaceAudioChecks.txt";
    static readonly List<string> lines = new List<string>();
    static FireRescuePlayer player;
    static VolcanoMissionDirector mission;
    static AudioSource engine, footsteps;
    static PaceMovementDriver movementDriver;
    static int phase = -1, lastFrame, interruptedRunFrames;
    static float phaseAt, maxSpeed, oldSfx, handMin, handMax, nextAnimationSample;
    static double deadline;
    static bool oldBackground, hadSfx;
    static string runtimeError;

    static PaceAudioChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Run interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Run speed audio checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode first.");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); runtimeError = null; lastFrame = -1;
        lines.Add("LIMITATION: real saved CharacterController, truck, wheels and audio sources on a temporary flat test surface; input supplied through controller APIs. No listening or full village driving-route test.");
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); player = mission.player;
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        hadSfx = PlayerPrefs.HasKey("Ashfall.SfxVolume"); oldSfx = PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1);
        PlayerPrefs.SetFloat("Ashfall.SfxVolume", 1);
        var game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        game.Focus(); game.maximized = true;
        Application.logMessageReceived += Log;
        Next(0, 25);
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = message;
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
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
        switch (phase)
        {
            case 0:
                if (mission.cinematic.IsPlaying || mission.State != VolcanoMissionDirector.FlowState.Preparation) return;
                Check("voice source removed", !mission.evacuation.GetComponent<AudioSource>());
                var movement = player.GetComponent<FireRescueMovementAudio>();
                footsteps = new SerializedObject(movement).FindProperty("footstepSource").objectReferenceValue as AudioSource;
                engine = new SerializedObject(player.drive.GetComponent<FireTruckEngineAudio>()).FindProperty("engineSource").objectReferenceValue as AudioSource;
                Check("movement loop disabled in scene", !movement.enabled && footsteps && !footsteps.enabled && !footsteps.playOnAwake);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "Temporary pace test surface";
                ground.transform.position = new Vector3(6000, 999.5f, 6000);
                ground.transform.localScale = new Vector3(500, 1, 500);
                Physics.SyncTransforms();
                player.enabled = false;
                player.PlaceOnFoot(new Vector3(5975, 1000.2f, 5900), 0);
                movementDriver = player.gameObject.AddComponent<PaceMovementDriver>();
                movementDriver.player = player;
                maxSpeed = 0; Next(1, 8); return;
            case 1:
            case 2:
                if (phase == 2 && age >= nextAnimationSample)
                {
                    nextAnimationSample = age + .2f;
                    var a = player.firstPerson.bodyAnimator;
                    lines.Add($"ANIMATION t={age:F2} ground={player.firstPerson.IsGrounded} speed={player.firstPerson.CurrentSpeed:F2} state={a.GetCurrentAnimatorStateInfo(0).shortNameHash} norm={a.GetCurrentAnimatorStateInfo(0).normalizedTime:F2} transition={a.IsInTransition(0)}");
                }
                movementDriver.sprint = phase == 2;
                if (age > .8f) maxSpeed = Mathf.Max(maxSpeed, player.firstPerson.CurrentSpeed);
                if (phase == 2 && age > .5f)
                {
                    if (player.firstPerson.bodyAnimator.IsInTransition(0) || player.firstPerson.handsAnimator.IsInTransition(0)) interruptedRunFrames++;
                    float z = player.viewCamera.transform.InverseTransformPoint(player.firstPerson.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand).position).z;
                    handMin = Mathf.Min(handMin, z); handMax = Mathf.Max(handMax, z);
                }
                if (age < 2) return;
                Check(phase == 1 ? "walk measured 12 m/s" : "sprint measured 24 m/s",
                    maxSpeed > (phase == 1 ? 11.5f : 23.5f) && maxSpeed < (phase == 1 ? 12.5f : 24.5f), "observed=" + maxSpeed);
                Check(phase == 1 ? "walking silent" : "sprinting silent", !footsteps.isPlaying && !engine.isPlaying);
                if (phase == 1) { maxSpeed = 0; nextAnimationSample = 0; interruptedRunFrames = 0; handMin = float.PositiveInfinity; handMax = float.NegativeInfinity; Next(2, 8); return; }
                Check("unarmed running clip active on body and hands", new[] {player.firstPerson.bodyAnimator, player.firstPerson.handsAnimator}.All(a =>
                    a.GetCurrentAnimatorStateInfo(0).IsName("Unarmed") && a.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Run" && c.weight > .9f)),
                    "grounded=" + player.firstPerson.IsGrounded + " sprint=" + player.firstPerson.IsSprinting + " position=" + player.transform.position + " " +
                    string.Join("; ", new[] {player.firstPerson.bodyAnimator, player.firstPerson.handsAnimator}.Select(a => a.name +
                        " state=" + a.GetCurrentAnimatorStateInfo(0).shortNameHash + " transition=" + a.IsInTransition(0) + " clips=" + string.Join(",", a.GetCurrentAnimatorClipInfo(0).Select(c => c.clip.name + ":" + c.weight)))));
                Check("visible first person running arm swing", handMax - handMin > .16f, "camera-space swing=" + (handMax - handMin));
                Check("running stays continuous", interruptedRunFrames == 0, "interrupted frames=" + interruptedRunFrames);
                ScreenCapture.CaptureScreenshot("Logs/GameplayFeel-unarmed-sprint.png");
                Next(9, 5); return;
            case 9:
                if (age < .2f) return;
                player.AcquirePistol(); Next(7, 8); return;
            case 7:
                movementDriver.sprint = true;
                if (age < 2) return;
                Check("armed running clip and pistol pose active", player.pistol.Equipped && player.firstPerson.bodyAnimator.GetCurrentAnimatorClipInfo(0)
                    .Where(c => c.clip.name.Contains("pistol run")).Sum(c => c.weight) > .8f && player.pistol.Hands.SprintLowering > .8f);
                movementDriver.enabled = false;
                player.firstPerson.SuspendExternalMovement();
                player.drive.body.position = new Vector3(6000, 1005, 5930);
                player.drive.body.rotation = Quaternion.identity;
                player.drive.body.linearVelocity = player.drive.body.angularVelocity = Vector3.zero;
                Physics.SyncTransforms(); Next(3, 10); return;
            case 3:
                if (age < 3) return;
                Check("truck wheels contact surface", player.drive.wheels.Count(w => w.isGrounded) >= 2);
                Check("parked truck silent", !engine.isPlaying);
                player.SetDriving(true); maxSpeed = 0; Next(4, 15); return;
            case 4:
                player.drive.SetInput(1, 0, false);
                maxSpeed = Mathf.Max(maxSpeed, player.drive.SpeedKph);
                if (age < 8) return;
                Check("truck accelerates with real wheel physics", maxSpeed > 60, "8-second maximum=" + maxSpeed + " km/h");
                Check("truck stays upright", Vector3.Dot(player.drive.transform.up, Vector3.up) > .9f);
                Check("engine plays while driving", engine.isPlaying && engine.volume > 0);
                player.drive.SetInput(0, 0, true); Next(5, 10); return;
            case 5:
                if (player.drive.SpeedKph > 2 && age < 5) return;
                Check("truck brakes to stop", player.drive.SpeedKph < 3, "speed=" + player.drive.SpeedKph);
                Check("exit works after braking", player.ExitVehicle());
                Next(6, 5); return;
            case 6:
                // Reproduce a truck still rolling after its driver gets out.
                player.drive.body.linearVelocity = Vector3.forward * 8;
                if (age < .3f) return;
                Check("unoccupied rolling truck silent", !player.drive.IsDriven && !engine.isPlaying && engine.volume == 0);
                Finish(null); return;
        }
    }
    static void Next(int value, double seconds)
    { phase = value; phaseAt = Time.time; deadline = EditorApplication.timeSinceStartup + seconds; Persist(); }
    static void Check(string name, bool pass, string detail = "")
    {
        lines.Add((pass ? "PASS " : "FAIL ") + name + " " + detail); Persist();
        if (!pass) throw new InvalidOperationException(name);
    }
    static void Persist() => File.WriteAllText(Report, string.Join("\n", lines) + "\nPHASE " + phase);
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log;
        Application.runInBackground = oldBackground;
        if (hadSfx) PlayerPrefs.SetFloat("Ashfall.SfxVolume", oldSfx); else PlayerPrefs.DeleteKey("Ashfall.SfxVolume");
        lines.Add(error == null ? "COMPLETED" : "FAILED " + error); Persist();
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}

// Feed movement during the normal gameplay Update, before Animator evaluation.
// EditorApplication.update is for observations and phase changes only.
[DefaultExecutionOrder(-80)]
public sealed class PaceMovementDriver : MonoBehaviour
{
    public FireRescuePlayer player;
    public bool sprint;
    void Update()
    {
        if (player) player.firstPerson.SimulateExternalMovement(Vector2.up, sprint, false, false, true, 0, Vector2.zero);
    }
}
