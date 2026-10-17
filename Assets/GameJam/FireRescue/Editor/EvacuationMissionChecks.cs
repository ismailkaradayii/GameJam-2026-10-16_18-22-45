using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Exercises the authored town and real agents in Play Mode; never saves a running scene.</summary>
[InitializeOnLoad]
public static class EvacuationMissionChecks
{
    const string Pending="EvacuationMissionChecks.Pending", Folder="Library/EvacuationReview";
    [Serializable] public class Check {public string name,evidence;public bool passed;}
    [Serializable] public class Report {public string status,error;public List<Check> checks=new List<Check>();public List<string> errors=new List<string>();public string[] animations;public int rescued,wallCrossings;}
    static Report report;
    static EvacuationMission mission;
    static NavMeshAgent leader;
    static EvacuationResident[] residents;
    static Transform[] exits;
    static HashSet<string> clips=new HashSet<string>();
    static Dictionary<EvacuationResident,Vector3> positions=new Dictionary<EvacuationResident,Vector3>();
    static int phase=-1,lastFrame=-1,house;
    static float phaseAt,began,nextProgress,cinematicAt;
    static int cinematicCaptures;
    static bool background;
    static Keyboard testKeyboard, previousKeyboard;
    static EvacuationMissionChecks()
    {
        EditorApplication.update+=Update;
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false))
            {SessionState.SetBool(Pending,false);EditorApplication.delayCall+=Start;}
            if(state==PlayModeStateChange.ExitingPlayMode&&phase>=0)Finish("Play Mode interrupted.");
        };
    }
    [MenuItem("GameJam/Evacuation/Review complete evacuation (Play Mode)")]
    public static void Begin()
    {
        if(EditorApplication.isPlaying||EditorApplication.isCompiling||phase>=0)return;
        if(!Object.FindFirstObjectByType<EvacuationMission>())throw new InvalidOperationException("Set up evacuation first.");
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        try
        {
            report=new Report{status="running"};clips.Clear();positions.Clear();
            File.WriteAllText(Folder+"/collisions.txt","");
            mission=Object.FindFirstObjectByType<EvacuationMission>();residents=mission.residents;
            exits=residents.Select(r=>r.houseExit).Distinct().ToArray();
            background=Application.runInBackground;Application.runInBackground=true;
            Application.logMessageReceived+=Log;
            float playerHeight=mission.player.playerBody.height*mission.player.transform.lossyScale.y;
            float doorHeight=residents.Min(r=>r.door.GetComponent<Renderer>().bounds.size.y);
            Add("player_and_all_residents_match_door_height",playerHeight>=doorHeight*.9f&&playerHeight<doorHeight&&residents.All(r=>
                r.GetComponent<CapsuleCollider>().bounds.size.y>=doorHeight*.9f&&r.GetComponent<CapsuleCollider>().bounds.size.y<doorHeight&&
                r.animator.GetBoneTransform(HumanBodyBones.Head).position.y-r.transform.position.y>=doorHeight*.78f),"Door="+doorHeight+" m; player="+playerHeight+" m; civilian="+residents[0].Agent.height*residents[0].transform.lossyScale.y+" m.");
            Add("large_player_has_matching_camera_and_capsule",mission.player.viewCamera.transform.position.y-mission.player.transform.position.y>playerHeight*.88f&&
                mission.player.viewCamera.transform.position.y-mission.player.transform.position.y<playerHeight,"Camera stays at eye level inside the full-height body.");
            Add("navigation_bakes_full_height_clearance",NavMesh.GetSettingsByID(0).agentHeight>=playerHeight&&residents.All(r=>
                r.Agent.height*r.transform.lossyScale.y>=r.GetComponent<CapsuleCollider>().bounds.size.y-.01f&&r.Agent.radius>=r.GetComponent<CapsuleCollider>().radius-.01f),"Navigation clearance encloses the enlarged capsules.");
            Vector3 playerStart=mission.PlayerTransform.position;
            mission.player.SetDriving(true);
            Add("large_player_can_exit_the_firetruck",mission.player.ExitVehicle()&&!mission.player.IsDriving,"Exit clearance uses the enlarged capsule.");
            mission.player.playerBody.enabled=false;mission.PlayerTransform.position=playerStart;
            mission.player.enabled=false;
            foreach(var renderer in mission.player.GetComponentsInChildren<Renderer>(true))renderer.enabled=false;
            mission.player.firstPerson.enabled=false;
            mission.player.playerBody.enabled=false;
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            leader=mission.PlayerTransform.gameObject.AddComponent<NavMeshAgent>();leader.speed=4;leader.acceleration=30;leader.radius=mission.player.playerBody.radius;leader.height=mission.player.playerBody.height;leader.stoppingDistance=.6f;leader.avoidancePriority=1;
            leader.Warp(OnMesh(mission.PlayerTransform.position));
            phase=0;phaseAt=began=Time.time;lastFrame=-1;house=0;cinematicAt=0;nextProgress=0;Time.timeScale=3;
            foreach(var r in residents)positions[r]=r.transform.position;
            File.WriteAllText(Folder+"/progress.txt","Started real scene review.");
        }
        catch(Exception ex){Finish(ex.ToString());}
    }
    static void Log(string text,string stack,LogType type)
    {if(report!=null&&(type==LogType.Error||type==LogType.Exception||type==LogType.Assert))report.errors.Add(text);}
    static void Update()
    {
        if(phase<0||!EditorApplication.isPlaying||EditorApplication.isPaused||lastFrame==Time.frameCount)return;
        lastFrame=Time.frameCount;
        try{Tick();}catch(Exception ex){Finish(ex.ToString());}
    }
    static void Tick()
    {
        foreach(var r in residents)
        {
            foreach(var info in r.animator.GetCurrentAnimatorClipInfo(0))if(info.weight>.2f)clips.Add(info.clip.name);
            Vector3 a=positions[r],b=r.transform.position;
            float height=r.Agent.height*r.transform.lossyScale.y;
            if(Vector3.Distance(a,b)>.01f && (Physics.Linecast(a+Vector3.up*height*.5f,b+Vector3.up*height*.5f,out RaycastHit hit,~0,QueryTriggerInteraction.Ignore)||
                Physics.Linecast(a+Vector3.up*(height-.2f),b+Vector3.up*(height-.2f),out hit,~0,QueryTriggerInteraction.Ignore)))
            {report.wallCrossings++;File.AppendAllText(Folder+"/collisions.txt",r.name+" crossed "+hit.collider.name+" from "+a+" to "+b+"\n");}
            positions[r]=b;
        }
        if(Time.time>nextProgress)
        {
            nextProgress=Time.time+6;
            File.WriteAllText(Folder+"/progress.txt","phase="+phase+" house="+house+" seconds="+(Time.time-began)+" rescued="+mission.RescuedCount+" leader="+leader.transform.position+" path="+leader.pathStatus+"\n"+
                string.Join("\n",residents.Select(r=>r.name+" "+r.State+" "+r.transform.position+" path="+r.Agent.pathStatus+" remaining="+r.Agent.remainingDistance)));
        }
        if(Time.time-began>420)throw new TimeoutException("Evacuation did not complete; see progress.txt.");
        float elapsed=Time.time-phaseAt;
        switch(phase)
        {
            case 0:
                if(elapsed<.6f)return;
                Add("seven_valid_humanoids_wait_indoors",residents.Length==7&&residents.All(r=>r.State==EvacuationResident.ResidentState.Waiting&&r.Agent.isOnNavMesh&&r.animator.isHuman),"7 residents on baked indoor navigation.");
                mission.eruption.TickSequence(90);
                Add("eruption_waits_for_rescue",mission.eruption.Stage==VolcanoEruptionSequence.EruptionStage.Waiting&&!mission.eruption.BeginEruption(),"Automatic countdown and V/API eruption blocked during evacuation.");
                Add("inactive_mission_ignores_shout",!mission.IsActive&&!mission.Shout(),"Residents stay indoors before dispatch.");
                mission.BeginMission();
                Add("far_shout_recruits_nobody",mission.Shout()&&mission.CalledCount==0,"Player starts over 200 metres from the houses.");
                Add("shout_has_cooldown",!mission.Shout(),"Repeated call in same frame rejected.");
                Next(1);break;
            case 1:
                if(mission.CooldownRemaining>0)return;
                leader.Warp(OnMesh(exits[0].position+residents[0].door.GetComponentInParent<BurnableBuilding>().transform.forward*4));
                previousKeyboard=Keyboard.current;testKeyboard=InputSystem.AddDevice<Keyboard>();testKeyboard.MakeCurrent();
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                Cursor.lockState=CursorLockMode.Locked;
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState(Key.Q));
                Next(7);break;
            case 7:
                if(elapsed<.2f)return;
                InputSystem.QueueStateEvent(testKeyboard,new KeyboardState());Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
                Add("q_key_calls_nearby_residents",mission.CalledCount>0,"Q was sent through Unity Input System to the real mission Update.");
                Add("nearby_residents_panic",residents.Any(r=>r.State==EvacuationResident.ResidentState.Panicking),"Local shout starts fear before movement.");
                Next(2);break;
            case 2:
                if(elapsed>.5f&&elapsed<.85f)
                {var r=residents[0];EvacuationSceneAudit.Capture("01-fear",r.transform.position+r.transform.forward*10+Vector3.up*4.5f,r.transform.position+Vector3.up*3);}
                if(elapsed<10)return;
                bool outside=residents.Where(r=>r.IsCalled).All(r=>r.State==EvacuationResident.ResidentState.Following);
                if(!outside&&elapsed<30)return;
                Add("called_residents_leave_through_doors",outside,"Each called resident reaches its exterior exit; elapsed="+elapsed);
                EvacuationSceneAudit.Capture("02-outside",leader.transform.position+new Vector3(15,10,10),residents[0].transform.position+Vector3.up*3);
                house=1;leader.SetDestination(OnMesh(exits[house].position));Next(3);break;
            case 3:
                if(leader.pathPending||leader.remainingDistance>1.5f)return;
                if(mission.CooldownRemaining>0)return;
                mission.Shout();Next(4);break;
            case 4:
                if(elapsed<7)return;
                house++;
                if(house<exits.Length){leader.SetDestination(OnMesh(exits[house].position));Next(3);}
                else
                {
                    Add("all_seven_recruited",mission.CalledCount==7,"All five houses visited.");
                    leader.SetDestination(mission.shelterApproach.position);Next(5);
                }
                break;
            case 5:
                if(elapsed>4 && elapsed<4.5f)EvacuationSceneAudit.Capture("03-following",leader.transform.position+new Vector3(-3,7,16),residents[3].transform.position+Vector3.up*3);
                if(leader.pathPending||leader.remainingDistance>1)return;
                // Reproduce the reported failure: the leader goes deep inside instead of waiting outside.
                leader.SetDestination(residents[5].safePlace.position);Next(6);break;
            case 6:
                if(!mission.IsComplete)return;
                Add("seven_physically_inside_shelter",mission.RescuedCount==7&&residents.All(r=>r.IsSafe&&mission.IsInsideShelter(r.transform.position)),"All seven cross the safe interior while the player moves deep inside; exact parking is not a mission gate.");
                int count=mission.RescuedCount;mission.ResidentArrived(residents[0]);
                Add("arrival_count_is_idempotent",count==mission.RescuedCount,"Duplicate arrival does not increase rescued count.");
                Add("all_four_user_animations_played",new[]{"Idle","Walk","Run","Fear"}.All(c=>clips.Contains(c)),string.Join(", ",clips));
                Add("no_wall_crossings",report.wallCrossings==0,"Physics line checks at torso height for every moving resident frame.");
                EvacuationSceneAudit.Capture("04-safe",mission.shelterEntrance.position+new Vector3(6,7,0),residents[3].transform.position+Vector3.up*3);
                leader.isStopped = true;
                var director = mission.eruption.missionDirector;
                director.TickMission();
                Add("rescue_unlocks_refill_not_eruption",director.Phase==VolcanoMissionDirector.MissionPhase.Refill&&!mission.eruption.BeginEruption(),"7/7 advances to filling; V/API cannot skip the empty tank.");
                FillTank(); director.TickMission();
                Add("full_tank_starts_warning",mission.eruption.Stage==VolcanoEruptionSequence.EruptionStage.Warning,"Real lake intake and pump fill the truck before eruption.");
                Time.timeScale=1; cinematicCaptures=0; Next(8); break;
            case 8:
                var cinema=mission.eruption.cinematic;
                if(!cinema.HasPlayed) return;
                if(cinematicAt==0) cinematicAt=Time.time;
                if(cinema.IsPlaying)
                {
                    AddOnce("cinematic_blocks_controls_and_spirits",mission.player.CinematicLocked&&Object.FindFirstObjectByType<LavaCreatureEncounter>().SpawnedCount==0,"Player input is suspended; no enemies spawn during the shot.");
                    if(cinematicCaptures<3 && cinema.Elapsed >= 2+cinematicCaptures*3)
                    {
                        CaptureCinema("cinematic-"+(cinematicCaptures+1),cinema.ShotCamera);
                        cinematicCaptures++;
                    }
                    return;
                }
                Add("cinematic_lasts_ten_seconds",Mathf.Abs(cinema.Elapsed-10)<.001f&&Time.time-cinematicAt>=9.5f,"Shot elapsed="+cinema.Elapsed+"; observed="+(Time.time-cinematicAt));
                Add("camera_and_input_return",!mission.player.CinematicLocked&&mission.player.viewCamera.enabled,"Gameplay camera and controls restored.");
                Add("combat_follows_cinematic",mission.eruption.Stage==VolcanoEruptionSequence.EruptionStage.Flowing,"Lava and encounter start after the full shot.");
                VolcanoHazardChecks.Run((name,passed,evidence)=>Add(name,passed,evidence));
                VolcanoCinematicChecks.CaptureHazardExamples(Add);
                Finish(null);break;
        }
    }
    static Vector3 OnMesh(Vector3 point){if(NavMesh.SamplePosition(point,out NavMeshHit hit,8,NavMesh.AllAreas))return hit.position;throw new Exception("No walkable ground near "+point);}
    static void Next(int p){phase=p;phaseAt=Time.time;}
    static void AddOnce(string name,bool passed,string evidence){if(!report.checks.Any(c=>c.name==name))Add(name,passed,evidence);}
    static void FillTank()
    {
        var hose=mission.player.hose;var source=Object.FindFirstObjectByType<WaterSourceVolume>();
        Vector3 intake=Vector3.zero;bool found=false;float closest=float.PositiveInfinity;Bounds bounds=source.volume.bounds;
        for(int x=0;x<=30;x++)for(int z=0;z<=30;z++)
        {
            Vector3 p=new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,x/30f),source.SurfaceY-.12f,Mathf.Lerp(bounds.min.z,bounds.max.z,z/30f));
            float distance=Vector3.Distance(p,hose.hoseSocket.position);
            if(distance<closest&&hose.CanReach(p)&&source.ContainsIntake(p)){found=true;closest=distance;intake=p;}
        }
        Add("lake_intake_reachable",found,"Nearest lake intake "+closest+" metres.");
        hose.Drop(intake);if(!hose.PumpEnabled)hose.TogglePump();hose.TickPump(1);
        Add("pump_transfers_real_water",hose.IsPumping&&hose.TankLitres>0&&hose.Tank01<1,"Tank="+hose.TankLitres);
        hose.TickPump(hose.tankCapacityLitres/hose.fillRateLitresPerSecond+1);
        Add("pump_stops_at_full_capacity",hose.Tank01==1&&!hose.IsPumping,"Tank="+hose.TankLitres);
    }
    public static void CaptureCinema(string name,Camera camera)
    {
        var target=new RenderTexture(1600,900,24);var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
        var old=RenderTexture.active;var previous=camera.targetTexture;
        try{camera.targetTexture=target;camera.Render();RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,1600,900),0,0);texture.Apply();File.WriteAllBytes(VolcanoMissionSetup.Folder+"/"+name+".png",texture.EncodeToPNG());}
        finally{camera.targetTexture=previous;RenderTexture.active=old;Object.DestroyImmediate(texture);target.Release();Object.DestroyImmediate(target);}
    }
    static void Add(string name,bool passed,string evidence){report.checks.Add(new Check{name=name,passed=passed,evidence=evidence});if(!passed)throw new Exception("FAIL: "+name);}
    static void Finish(string error)
    {
        if(report==null)report=new Report();
        phase=-1;Time.timeScale=1;Application.runInBackground=background;Application.logMessageReceived-=Log;
        if(testKeyboard!=null){InputSystem.RemoveDevice(testKeyboard);testKeyboard=null;}
        if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();
        report.error=error;report.rescued=mission?mission.RescuedCount:0;report.animations=clips.ToArray();report.status=error==null&&report.errors.Count==0?"passed":"failed";
        File.WriteAllText(Folder+"/report.json",JsonUtility.ToJson(report,true));
        if(EditorApplication.isPlaying)EditorApplication.ExitPlaymode();
    }
}
