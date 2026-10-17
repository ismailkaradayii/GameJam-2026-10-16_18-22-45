using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Physical handling checks on temporary surfaces; fixtures never enter the saved scene.</summary>
[InitializeOnLoad]
public static class ArcadeTruckChecks
{
    const string Pending = "ArcadeTruckChecks.Pending";
    static FireRescuePlayer player;
    static FireTruckDrive drive;
    static readonly List<string> lines = new List<string>();
    static readonly MethodInfo Follow = typeof(FireRescuePlayer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    static int phase = -1, frame;
    static float phaseAt, sampleAt, lastYaw, turned, distance, maxTilt;
    static Vector3 previousPosition;
    static Quaternion[] previousWheels;
    static float[] wheelMotion;
    static double deadline;
    static string runtimeError;
    static bool previousBackground, previousMaximized;
    static EditorWindow game;

    static ArcadeTruckChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted");
        };
    }
    [MenuItem("GameJam/Final Loop/Run arcade truck checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Use saved scene in idle Edit Mode.");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); runtimeError = null; frame = -1; sampleAt = 0; maxTilt = 0;
        lines.Add("Saved truck controller, real rigidbody/wheels/collisions. Temporary flat test surface and 1.7m step; fixture placement between tests only. Input via SetInput, not a manual playthrough.");
        player = Object.FindFirstObjectByType<FireRescuePlayer>(); drive = player.drive;
        lines.Add($"TUNING forward={drive.maxForwardSpeedKph:F1} reverse={drive.maxReverseSpeedKph:F1} corner={drive.arcadeCornerSpeedKph:F1}km/h torque={drive.motorTorque:F0} accelerationAssist={drive.groundAccelerationAssist:F1}");
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); previousMaximized = game.maximized; game.maximized = true; game.Focus();
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
            if (runtimeError != null) throw new Exception(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase);
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.time - phaseAt;
        if (phase == 0)
        {
            if (player.mission.cinematic.IsPlaying || player.mission.State != VolcanoMissionDirector.FlowState.Preparation) return;
            player.mission.enabled = false; player.enabled = false;
            player.firstPerson.SuspendExternalMovement(); player.SetDriving(true);
            Cube("Temporary handling ground", new Vector3(6000,999.5f,6000), new Vector3(600,1,600));
            Place(new Vector3(6000,1003,5910)); Next(1, 12); return;
        }
        Follow.Invoke(player, null);
        if (!drive.body.detectCollisions || !drive.GetComponent<BoxCollider>().enabled || drive.wheels.Any(w => !w.enabled)) throw new Exception("Collider disabled");
        float tilt = Vector3.Angle(drive.transform.up, Vector3.up); maxTilt = Mathf.Max(maxTilt, tilt);
        if (tilt > 50) throw new Exception("Truck tipped: " + tilt);
        if (Time.time >= sampleAt)
        {
            sampleAt = Time.time + .5f;
            lines.Add($"phase={phase} age={age:F2} position={drive.transform.position} speed={drive.SpeedKph:F1} yaw={drive.transform.eulerAngles.y:F1} tilt={tilt:F1} grounded={drive.wheels.Count(w=>w.isGrounded)}"); Save();
        }
        switch (phase)
        {
            case 1:
                drive.SetInput(0,0,true); if (age < 2) return;
                Check("all four wheels grounded", drive.wheels.All(w=>w.isGrounded));
                previousWheels = drive.wheelVisuals.Select(v=>v.localRotation).ToArray(); wheelMotion = new float[4];
                Next(2, 8); break;
            case 2:
                drive.SetInput(1,0,false);
                for (int i=0;i<4;i++) { wheelMotion[i] += Quaternion.Angle(previousWheels[i],drive.wheelVisuals[i].localRotation); previousWheels[i]=drive.wheelVisuals[i].localRotation; }
                if(age<2) return;
                Check("responsive acceleration in 2 seconds",drive.SpeedKph>55,$"speed={drive.SpeedKph:F1}km/h");
                Check("four rendered wheels visibly rotate", wheelMotion.All(v=>v>180) && drive.wheelVisuals.All(v=>v.GetComponent<Renderer>().enabled),string.Join(", ",wheelMotion.Select(v=>v.ToString("F0")+"deg")));
                drive.SetInput(0,0,true); Next(3,8); break;
            case 3:
                drive.SetInput(0,0,true); if(drive.SpeedKph>1 && age<3) return;
                Check("strong braking",drive.SpeedKph<1 && age<3,$"stopped in {age:F2}s");
                previousPosition=drive.transform.position; lastYaw=drive.transform.eulerAngles.y; turned=distance=0;
                Next(4,20); break;
            case 4:
                Speed(26,1);
                turned+=Mathf.DeltaAngle(lastYaw,drive.transform.eulerAngles.y); lastYaw=drive.transform.eulerAngles.y;
                distance+=Vector3.Distance(previousPosition,drive.transform.position); previousPosition=drive.transform.position;
                if(turned<180) return;
                float radius=distance/(turned*Mathf.Deg2Rad);
                Check("tight 180 degree turn",radius<12.5f,$"measured centre radius={radius:F2}m, elapsed={age:F2}s, tilt={maxTilt:F1}");
                Next(10,8); break;
            case 10:
                drive.SetInput(1,0,false);
                if(age<2)return;
                lastYaw=drive.transform.eulerAngles.y; turned=0; Next(11,10); break;
            case 11:
                // Full gas and full steering: the same digital W+D input a new player holds.
                drive.SetInput(1,1,false);
                turned+=Mathf.DeltaAngle(lastYaw,drive.transform.eulerAngles.y); lastYaw=drive.transform.eulerAngles.y;
                if(age<4)return;
                Check("full gas turn automatically slows",drive.SpeedKph<drive.arcadeCornerSpeedKph+4 && turned>150,$"speed={drive.SpeedKph:F1}km/h, turn={turned:F1}deg, maximum tilt={maxTilt:F1}");
                drive.SetInput(0,0,true); Next(5,8); break;
            case 5:
                if(age<1.5f)return;
                Cube("Temporary 1.7m rock step",new Vector3(6000,1000.85f,6035),new Vector3(32,1.7f,5));
                Cube("Temporary tall wall",new Vector3(6000,1006,6095),new Vector3(60,12,3));
                Place(new Vector3(6000,1003,5990)); Next(6,30); break;
            case 6:
                if(age<1.5f){drive.SetInput(0,0,true);return;}
                Speed(34,0);
                if(drive.transform.position.z<6053)return;
                Check("crossed 1.7m obstacle forward",Mathf.Abs(drive.transform.position.x-6000)<6,$"crossed in {age:F2}s; position={drive.transform.position}");
                Next(7,20); break;
            case 7:
                drive.SetInput(1,0,false);
                if(age<5)return;
                Check("tall obstacles remain solid",drive.transform.position.z<6080 && drive.SpeedKph<3,$"position={drive.transform.position} speed={drive.SpeedKph:F1}");
                Next(8,40); break;
            case 8:
                Speed(-28,0);
                if(drive.transform.position.z>6014)return;
                Check("crossed 1.7m obstacle in reverse",Mathf.Abs(drive.transform.position.x-6000)<8,$"crossed in {age:F2}s");
                drive.SetInput(0,0,true); Next(9,8); break;
            case 9:
                drive.SetInput(0,0,true);
                if(age<1.5f)return;
                Check("stable finish",drive.SpeedKph<1 && Vector3.Dot(drive.transform.up,Vector3.up)>.95f,$"maximum tilt={maxTilt:F1}");
                Finish(null); break;
        }
    }
    static void Speed(float target,float steering)
    {
        float forward=Vector3.Dot(drive.body.linearVelocity,drive.transform.forward)*3.6f;
        bool brake=Mathf.Sign(target)*forward>Mathf.Abs(target)+1;
        float throttle=brake?0:Mathf.Clamp((Mathf.Abs(target)-Mathf.Sign(target)*forward)*.17f+.08f,.03f,1)*Mathf.Sign(target);
        drive.SetInput(throttle,steering,brake);
    }
    static void Place(Vector3 position)
    {
        drive.body.position=position; drive.body.rotation=Quaternion.identity;
        drive.body.linearVelocity=drive.body.angularVelocity=Vector3.zero;
        drive.SetInput(0,0,true); Physics.SyncTransforms();
    }
    static void Cube(string name,Vector3 position,Vector3 size)
    { var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.transform.position=position;obj.transform.localScale=size; }
    static void Check(string title,bool okay,string detail="")
    { lines.Add((okay?"PASS ":"FAIL ")+title+" "+detail);Save();if(!okay)throw new Exception(title); }
    static void Next(int next,double seconds){phase=next;phaseAt=Time.time;deadline=EditorApplication.timeSinceStartup+seconds;}
    static void Save()=>File.WriteAllLines("Logs/ArcadeTruck-checks.txt",lines);
    static void Finish(string error)
    {
        phase=-1;Application.logMessageReceived-=Log;
        Application.runInBackground=previousBackground;if(game)game.maximized=previousMaximized;
        lines.Add(error==null?"COMPLETED":"FAILED "+error);Save();EditorApplication.ExitPlaymode();
    }
}
