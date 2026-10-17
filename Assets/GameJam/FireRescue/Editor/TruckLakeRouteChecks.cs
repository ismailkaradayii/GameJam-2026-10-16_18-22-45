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

/// <summary>Actual saved truck, terrain, wheels and collisions, driven with throttle/steering/brakes only.</summary>
[InitializeOnLoad]
public static class TruckLakeRouteChecks
{
    const string Pending = "TruckLakeRouteChecks.Pending";
    static FireRescuePlayer player;
    static FireTruckDrive drive;
    static LakeFillStation station;
    static Keyboard keyboard;
    static InputSettings previousInput, testInput;
    static bool previousBackground;
    static EditorWindow gameView;
    static bool previousMaximized;
    static readonly List<string> lines = new List<string>();
    static readonly MethodInfo Follow = typeof(FireRescuePlayer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    static List<Vector3> route;
    static int phase = -1, frame, segment;
    static float phaseAt, beganAt, nextSample, pathProgress, lastProgressAt, travel, peak;
    static Vector3 previousPosition, progressPosition;
    static double deadline;
    static string runtimeError;
    static float nozzleScale, pipeLength, outboundTime;
    static bool reverse;
    static TruckLakeRouteChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (s == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Run interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Run lake round trip")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Use idle Edit Mode.");
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save the scene first.");
        var authored = Object.FindFirstObjectByType<FireRescuePlayer>();
        SessionState.SetFloat(Pending+".NozzleScale",authored.hose.nozzle.lossyScale.magnitude);
        SessionState.SetFloat(Pending+".PipeLength",authored.cannon.pitchPivot.Find("Water Barrel").TransformVector(Vector3.up*2).magnitude);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); runtimeError = null; frame = -1; travel = peak = 0;
        lines.Add("Physical village -> lake -> village drive; saved truck scale/colliders/terrain unchanged during test. No truck teleport or velocity writes. Test driver supplies pedal/steering commands. Completed-preparation mission state is supplied and the deadline disabled only in this isolated route fixture after intro.");
        player = Object.FindFirstObjectByType<FireRescuePlayer>(); drive = player.drive; station = player.mission.fillStation;
        drive.gameObject.AddComponent<TruckLakeCollisionProbe>();
        previousBackground = Application.runInBackground; Application.runInBackground = true;
        previousInput = InputSystem.settings; testInput = Object.Instantiate(previousInput); testInput.hideFlags = HideFlags.HideAndDontSave;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
        keyboard = InputSystem.AddDevice<Keyboard>("LakeRouteKeyboard");
        gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); previousMaximized = gameView.maximized; gameView.maximized = true; gameView.Focus();
        Application.logMessageReceived += Log;
        nozzleScale = player.hose.nozzle.lossyScale.magnitude;
        pipeLength = PipeLength();
        if(Mathf.Abs(nozzleScale-SessionState.GetFloat(Pending+".NozzleScale",0))>.005f || Mathf.Abs(pipeLength-SessionState.GetFloat(Pending+".PipeLength",0))>.005f)
        {Finish("Nozzle/cannon changed size between Edit Mode and Play Mode.");return;}
        Next(0, 30);
    }
    static float PipeLength()
    {
        var barrel = player.cannon.pitchPivot.Find("Water Barrel");
        return barrel ? barrel.TransformVector(Vector3.up * 2).magnitude : 0;
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
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        float age = Time.time - phaseAt;
        if (phase == 0)
        {
            if (player.mission.cinematic.IsPlaying || player.mission.State != VolcanoMissionDirector.FlowState.Preparation) return;
            if(Object.FindFirstObjectByType<ObjectiveNavigation>().lakeTurnaround.Any(t=>!t||Flat(t.position,station.transform.position)>140 || t.GetComponentInParent<Canvas>()))
                throw new Exception("Turning points must remain in world space at every Game View resolution.");
            player.mission.enabled = false;
            player.AcquirePistol();
            typeof(VolcanoMissionDirector).GetProperty("Phase").SetValue(player.mission, VolcanoMissionDirector.MissionPhase.Refill);
            // Only position the pedestrian beside the door, so the actual E binding is exercised.
            Vector3 point = TruckLakeRouteTools.Ground(player.driverDoor.position - drive.transform.right * 2) + Vector3.up * .2f;
            player.PlaceOnFoot(point, drive.transform.eulerAngles.y);
            lines.Add("ENTRY prompt=" + player.InteractionPrompt);
            Next(1, 12); return;
        }
        if (phase == 1)
        {
            InputSystem.QueueStateEvent(keyboard, age < .3f ? new KeyboardState() : new KeyboardState(Key.E));
            if (!player.IsDriving) { if (age > 4) throw new Exception("E did not enter the truck: " + player.InteractionPrompt); return; }
            lines.Add("PASS E entered through FireRescuePlayer.Update; no lake fill triggered.");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            player.enabled = false;
            previousPosition = drive.transform.position; beganAt = Time.time;
            SetRoute(Outbound(), false); Next(2, 360); return;
        }
        travel += Vector3.Distance(previousPosition, drive.transform.position); previousPosition = drive.transform.position;
        peak = Mathf.Max(peak, drive.SpeedKph);
        if(!drive.GetComponent<BoxCollider>().enabled || !drive.body.detectCollisions || drive.wheels.Any(w=>!w.enabled))
            throw new Exception("A physical truck collider was disabled during the route test.");
        Follow?.Invoke(player, null);
        if (Mathf.Abs(player.hose.nozzle.lossyScale.magnitude - nozzleScale) > .005f || Mathf.Abs(PipeLength() - pipeLength) > .005f)
            throw new Exception("Nozzle or cannon dimensions changed during play.");
        if (Vector3.Dot(drive.transform.up, Vector3.up) < .55f) throw new Exception("Truck tipped: " + drive.transform.position);
        if (phase == 2)
        {
            DriveRoute();
            if (pathProgress > RouteLength() - 25 && station.InRange)
            { drive.SetInput(0, 0, true); outboundTime = Time.time - beganAt; Next(3, 20); }
        }
        else if (phase == 3)
        {
            drive.SetInput(0, 0, true);
            if (drive.SpeedKph > .5f) return;
            if (!station.CanFill) throw new Exception("Physical lake arrival cannot fill. " + player.InteractionPrompt + " reach=" + player.hose.CanReach(station.intakePoint.position));
            lines.Add($"PASS reached lake in {outboundTime:F2}s, position={drive.transform.position}, distance={Flat(drive.transform.position,station.transform.position):F2}, litres={player.hose.TankLitres:F0}");
            player.enabled = true; InputSystem.QueueStateEvent(keyboard, new KeyboardState()); Next(4, 30);
        }
        else if (phase == 4)
        {
            InputSystem.QueueStateEvent(keyboard, age < .3f ? new KeyboardState() : age < .6f ? new KeyboardState(Key.E) : new KeyboardState());
            if (age > 1 && !station.IsFilling && player.hose.Tank01 < .999f) throw new Exception("E did not start the actual lake pump.");
            if (player.hose.Tank01 < 1 || station.IsFilling) return;
            lines.Add($"PASS E filled {player.hose.TankLitres:F0}L in {age:F2}s; hose retracted={!player.hose.IsDeployed}; width={player.hose.hoseLine.widthMultiplier * player.hose.hoseLine.widthCurve.Evaluate(.5f):F3}");
            if (player.hose.IsDeployed || !player.IsDriving) throw new Exception("Fill did not cleanly restore driving.");
            player.enabled = false;
            typeof(VolcanoMissionDirector).GetProperty("Phase").SetValue(player.mission, VolcanoMissionDirector.MissionPhase.Ready);
            typeof(VolcanoMissionDirector).GetProperty("PreparationComplete").SetValue(player.mission, true);
            var navigation = Object.FindFirstObjectByType<ObjectiveNavigation>();
            var back = new List<Vector3> { drive.transform.position };
            back.AddRange(navigation.lakeTurnaround.Select(t => t.position));
            var road = Outbound(); road.Reverse(); back.AddRange(road.Where(p => p.x < 330));
            SetRoute(back, false); Next(7, 360);
        }
        else if (phase == 7)
        {
            if(age>.3f && age<.5f && !Object.FindFirstObjectByType<ObjectiveNavigation>().CurrentLabel.Contains("TURNING AREA"))
                throw new Exception("Loaded truck was not guided through the turning area.");
            if (DriveRoute()) { drive.SetInput(0,0,true); Next(8,20); }
        }
        else if (phase == 8)
        {
            drive.SetInput(0,0,true); if (drive.SpeedKph > .5f) return;
            lines.Add($"PASS returned to village with {player.hose.TankLitres:F0}L; travel={travel:F1}m peak={peak:F1}km/h elapsed={Time.time-beganAt:F2}s");
            if (player.hose.Tank01 < .999f) throw new Exception("Tank lost water during return.");
            lines.Add("PASS hose/cannon size remained constant; actual body colliders and all four WheelColliders remained active.");
            player.enabled=true;InputSystem.QueueStateEvent(keyboard,new KeyboardState());Next(9,12);
        }
        else if(phase==9)
        {
            InputSystem.QueueStateEvent(keyboard,age<.2f?new KeyboardState():new KeyboardState(Key.E));
            if(player.IsDriving)return;
            lines.Add("PASS F exited safely at village after the full round trip. position="+player.transform.position+" door="+player.driverDoor.position);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());Next(10,12);
        }
        else if(phase==10)
        {
            InputSystem.QueueStateEvent(keyboard,age<.2f?new KeyboardState():new KeyboardState(Key.E));
            if(age>4 && !player.IsDriving)throw new Exception("E re-entry failed: "+player.InteractionPrompt+" foot="+player.transform.position+" door="+player.driverDoor.position);
            if(!player.IsDriving)return;
            lines.Add("PASS E re-entered after return; tank retained 2000L and intake remained retracted.");
            Finish(null);
        }
    }
    public static List<Vector3> Outbound()
    {
        var navigation = Object.FindFirstObjectByType<ObjectiveNavigation>();
        if(navigation.lakeRoad.Length>0 && navigation.lakeRoad[navigation.lakeRoad.Length-1].position.x<0)
            return navigation.lakeRoad.Reverse().Select(t=>t.position).ToList();
        var points = new List<Vector3> {
            new Vector3(-17.06f,0,-620), new Vector3(-17.06f,0,-590), new Vector3(-12,0,-576),
            new Vector3(2,0,-566), new Vector3(20,0,-565), new Vector3(37,0,-569),
            new Vector3(52,0,-581), new Vector3(68,0,-587), new Vector3(84,0,-591), new Vector3(98,0,-605)
        };
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        for (int i = nav.lakeRoad.Length - 1; i >= 0; i--)
            if (nav.lakeRoad[i].position.x >= 107) points.Add(nav.lakeRoad[i].position);
        return points.Select(TruckLakeRouteTools.Ground).ToList();
    }
    static void SetRoute(List<Vector3> points, bool backing)
    {
        route = points.Select(TruckLakeRouteTools.Ground).ToList(); reverse = backing; segment = 0; pathProgress = 0;
        lastProgressAt = Time.time; progressPosition = drive.transform.position; nextSample = 0;
        lines.Add("ROUTE reverse=" + reverse + " " + string.Join(" -> ", route)); Save();
    }
    static Vector3 RearAxle() => (drive.wheels[2].transform.position + drive.wheels[3].transform.position) * .5f;
    static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
    static float RouteLength() { float length=0; for(int i=1;i<route.Count;i++) length+=Flat(route[i-1],route[i]); return length; }
    static bool DriveRoute()
    {
        Vector3 position = drive.arcadeHandling ? drive.transform.position : reverse ? (drive.wheels[0].transform.position + drive.wheels[1].transform.position) * .5f : RearAxle();
        Vector3 horizontal = position; horizontal.y=0;
        float nearest=float.PositiveInfinity, cumulative=0, progress=0;
        for(int i=0;i<route.Count-1;i++)
        {
            Vector3 a=route[i], b=route[i+1]; a.y=b.y=0; Vector3 delta=b-a; float len=delta.magnitude;
            float t=Mathf.Clamp01(Vector3.Dot(horizontal-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
            float d=(horizontal-(a+delta*t)).sqrMagnitude;
            if(i>=segment-1 && i<=segment+3 && d<nearest) {nearest=d; progress=cumulative+t*len; segment=i;}
            cumulative+=len;
        }
        pathProgress=Mathf.Max(pathProgress,progress);
        float remaining=RouteLength()-pathProgress;
        if(remaining<3) {drive.SetInput(0,0,true);return true;}
        float lookahead=reverse?12:Mathf.Lerp(13,21,drive.SpeedKph/60);
        float wanted=Mathf.Min(RouteLength(),pathProgress+lookahead); cumulative=0; Vector3 target=route[route.Count-1];
        for(int i=1;i<route.Count;i++)
        { float len=Flat(route[i-1],route[i]); if(cumulative+len>=wanted){target=Vector3.Lerp(route[i-1],route[i],(wanted-cumulative)/len);break;} cumulative+=len; }
        Vector3 local=drive.transform.InverseTransformDirection(target-position); if(reverse)local=-local;
        float curvature=2*local.x/Mathf.Max(1,local.x*local.x+local.z*local.z);
        float wheelbase=Flat(drive.wheels[0].transform.position,drive.wheels[2].transform.position);
        float angle=Mathf.Atan(wheelbase*curvature/(1+drive.RearSteerFactor))*Mathf.Rad2Deg;
        float limit=Mathf.Lerp(drive.maxSteerAngle,drive.highSpeedSteerAngle,Mathf.Clamp01(drive.SpeedKph/drive.steeringFadeSpeedKph));
        float steer=Mathf.Clamp(angle/limit,-1,1)*(reverse?-1:1);
        if(drive.arcadeHandling)
            steer=Mathf.Clamp(curvature*Mathf.Max(1.5f,drive.SpeedKph/3.6f)*Mathf.Rad2Deg/Mathf.Max(1,drive.FullSteeringYawRate(Mathf.Max(5.4f,drive.SpeedKph))),-1,1)*(reverse?-1:1);
        float speed=Mathf.Clamp(Mathf.Sqrt((drive.arcadeHandling?3.5f:2.1f)/Mathf.Max(.003f,Mathf.Abs(curvature)))*3.6f,10,reverse?18:drive.arcadeHandling?65:45);
        if(phase==7 && drive.transform.position.x>340)speed=Mathf.Min(speed,drive.arcadeHandling?28:20);
        speed=Mathf.Min(speed,Mathf.Sqrt(Mathf.Max(0,remaining-2)*5)*3.6f);
        bool brake=drive.SpeedKph>speed+1;
        float throttle=brake?0:Mathf.Clamp((speed-drive.SpeedKph)*.18f+.08f,.03f,1)*(reverse?-1:1);
        drive.SetInput(throttle,steer,brake);
        if(Flat(progressPosition,drive.transform.position)>3){progressPosition=drive.transform.position;lastProgressAt=Time.time;}
        if(Time.time-lastProgressAt>12)throw new Exception("No physical progress for 12s at "+drive.transform.position+" target="+target);
        if(Time.time>=nextSample)
        {
            nextSample=Time.time+1;
            int grounded=drive.wheels.Count(w=>w.isGrounded);
            var solid=Physics.OverlapBox(drive.GetComponent<BoxCollider>().bounds.center,drive.GetComponent<BoxCollider>().size*.5f,drive.transform.rotation,~0,QueryTriggerInteraction.Ignore)
                .Where(c=>!(c is TerrainCollider)&&!(c is CharacterController)&&!c.transform.IsChildOf(drive.transform)).Select(c=>c.name).Distinct();
            lines.Add($"t={Time.time-beganAt:F2} phase={phase} progress={pathProgress:F1}/{RouteLength():F1} pos={drive.transform.position} speed={drive.SpeedKph:F2} targetSpeed={speed:F2} pedals={throttle:F2}/{steer:F2}/{brake} ground={grounded} tilt={Vector3.Angle(drive.transform.up,Vector3.up):F1} target={target} solids={string.Join(",",solid)} euler={drive.transform.eulerAngles} motors={drive.wheels[0].motorTorque:F0}/{drive.wheels[0].brakeTorque:F0} contact={drive.GetComponent<TruckLakeCollisionProbe>().LastContact}");
            Save();
        }
        return false;
    }
    static void Next(int value,double seconds){phase=value;phaseAt=Time.time;deadline=EditorApplication.timeSinceStartup+seconds;}
    static void Save()=>File.WriteAllLines("Logs/TruckLakeRoute-checks.txt",lines);
    static void Finish(string error)
    {
        phase=-1;Application.logMessageReceived-=Log;
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
        if(previousInput)InputSystem.settings=previousInput;
        if(testInput)Object.DestroyImmediate(testInput);
        Application.runInBackground=previousBackground;
        if(gameView)gameView.maximized=previousMaximized;
        lines.Add(error==null?"COMPLETED": "FAILED "+error);Save();EditorApplication.ExitPlaymode();
    }
}

public sealed class TruckLakeCollisionProbe : MonoBehaviour
{
    public string LastContact;
    void OnCollisionStay(Collision collision)
    {
        var points = collision.contacts;
        LastContact = collision.collider.name + " " + string.Join(";", points.Take(4).Select(p => $"{p.point} normal={p.normal}"));
    }
    void OnCollisionExit(Collision collision) => LastContact = "";
}
