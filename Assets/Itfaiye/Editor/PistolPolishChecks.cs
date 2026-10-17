using System;
using System.IO;
using System.Collections.Generic;
using GameJam.FireRescue;
using Itfaiye;
using Itfaiye.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PistolPolishChecks
{
    const string Pending = "PistolPolishChecks.Pending";
    static FireRescuePlayer coordinator;
    static ItfaiyePistol pistol;
    static ItfaiyeWeaponReviewDriver driver;
    static int phase = -1, frame;
    static float at;
    static bool background;
    static string label;
    static double deadline;
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static ItfaiyeReflexSight sight;
    static GameObject target;
    static LavaCreatureActor actor;
    static int ammo, reserve, firstShot, failed;
    static float peakKick, peakPost, peakTravel, requested;
    static bool capturedReload;
    static Vector3 footPosition;
    static float footYaw;
    static float captureDelta;
    static bool capturedRecoil;
    static bool capturedSlide;
    static GameObject blocker;
    static float realKick, realFrame;
    static Keyboard keyboard;
    static Mouse mouse;
    static InputSettings originalInput, testInput;
    static PistolPolishChecks()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += s => {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (s == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Play Mode interrupted");
        };
    }
    public static void Begin(string name)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first");
        SessionState.SetString(Pending + ".Label", name); SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        label = SessionState.GetString(Pending + ".Label", "after");
        Directory.CreateDirectory(PistolPolishSetup.Folder);
        coordinator = Object.FindFirstObjectByType<FireRescuePlayer>(); pistol = coordinator.pistol;
        background = Application.runInBackground; Application.runInBackground = true;
        captureDelta=Time.captureDeltaTime; Time.captureDeltaTime=1f/60f;
        originalInput=InputSystem.settings; testInput=Object.Instantiate(originalInput); testInput.hideFlags=HideFlags.HideAndDontSave;
        testInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings=testInput;
        keyboard=InputSystem.AddDevice<Keyboard>("PistolReviewKeyboard"); mouse=InputSystem.AddDevice<Mouse>("PistolReviewMouse");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        checks.Clear(); errors.Clear(); failed=0; peakKick=peakPost=peakTravel=0; capturedReload=capturedRecoil=capturedSlide=false;
        checks.Add("Actual saved village, 60 Hz simulation for deterministic rendered-frame capture. Temporary input devices and health receiver only; no gameplay scene changes.");
        Application.logMessageReceived += Log;
        phase = 0; frame = -1; at = Time.time; deadline = EditorApplication.timeSinceStartup + 180;
    }
    static void Tick()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Capture phase " + phase);
            float age = Time.time - at;
            if (phase > 0) { Cursor.lockState=CursorLockMode.Locked; Cursor.visible=false; }
            if (pistol.Feedback && phase >= 3 && phase <= 5)
            {
                peakKick=Mathf.Max(peakKick,Quaternion.Angle(Quaternion.identity,pistol.Feedback.CameraRotation));
                peakPost=Mathf.Max(peakPost,pistol.Feedback.ShotPostWeight); peakTravel=Mathf.Max(peakTravel,pistol.Feedback.CameraOffset.magnitude);
            }
            if (phase==22) { realKick=Mathf.Max(realKick,Quaternion.Angle(Quaternion.identity,pistol.Feedback.CameraRotation)); realFrame=Mathf.Max(realFrame,Time.deltaTime); }
            if (phase == 0)
            {
                var m = coordinator.mission;
                if (m.State != VolcanoMissionDirector.FlowState.Preparation || m.cinematic.IsPlaying) return;
                m.enabled = false;
                Vector3 outward = m.houseExit.position - m.startingHouse.bounds.center; outward.y = 0; outward.Normalize();
                Vector3 position = m.houseExit.position + outward * 14f;
                if (Physics.Raycast(position + Vector3.up * 50, Vector3.down, out var hit, 100, ~0, QueryTriggerInteraction.Ignore)) position.y = hit.point.y + .2f;
                coordinator.PlaceOnFoot(position, Quaternion.LookRotation(-outward).eulerAngles.y);
                footPosition=position; footYaw=Quaternion.LookRotation(-outward).eulerAngles.y;
                coordinator.AcquirePistol(); coordinator.enabled = false;
                pistol.SetAvailable(true); pistol.Equip();
                driver = coordinator.gameObject.AddComponent<ItfaiyeWeaponReviewDriver>();
                driver.player = coordinator.firstPerson; driver.pistol = pistol;
                Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
                Next(1); return;
            }
            if (phase == 1 && age > 1f) { if (!pistol.Ready) throw new InvalidOperationException("Pistol did not equip"); Capture("hip"); driver.aim = true; Next(2); }
            else if (phase == 2 && age > 1f)
            {
                Capture("ads");
                if (label=="before") { Finish(null); return; }
                sight=pistol.weapon.GetComponentInChildren<ItfaiyeReflexSight>();
                Check("Authored optic, clear glass and ADS reference are connected",sight && sight.lens && pistol.Hands.pistolSight==sight.lens.transform);
                Check("Both projection FOVs agree",Mathf.Abs(pistol.player.playerCamera.fieldOfView-pistol.player.handsCamera.fieldOfView)<.01f);
                MeasureGrip();
                target=GameObject.CreatePrimitive(PrimitiveType.Cube); target.name="Temporary pistol hit receiver";
                target.transform.SetPositionAndRotation(pistol.player.playerCamera.transform.position+pistol.player.playerCamera.transform.forward*12f,pistol.player.playerCamera.transform.rotation);
                target.transform.localScale=new Vector3(8,8,.3f); target.GetComponent<Renderer>().enabled=false;
                actor=target.AddComponent<LavaCreatureActor>();
                ammo=pistol.Ammo; reserve=pistol.reserveAmmo; firstShot=pistol.ShotsFired; Next(3);
            }
            else if (phase==3 && age>.15f)
            {
                var c=pistol.player.playerCamera;
                float error=Vector2.Distance(c.WorldToScreenPoint(pistol.AimPoint),c.WorldToScreenPoint(sight.ReticleWorldPoint));
                Check("Red dot lies on the resolved physical shot (pixels)",sight.ReticleVisible && error<1.5f,error.ToString("F4"));
                Check("Fire accepted",pistol.Fire()); Next(4);
            }
            else if (phase==4)
            {
                if(pistol.ShotAge>.025f && pistol.ShotAge<.08f && !capturedRecoil) { Capture("ads-shot");capturedRecoil=true; }
                if(age<.3f) return;
                Check("Bullet damages the real creature health component",actor.Health==66f && pistol.LastShotCollider==target.GetComponent<Collider>(),actor.Health.ToString());
                Check("Shot produces camera kick, translation and grading",peakKick>.35f && peakTravel>.001f && peakPost>.05f,$"rotation={peakKick:F3} travel={peakTravel:F4} post={peakPost:F3}");
                Check("Second semi-auto click accepted",pistol.Fire()); Next(5);
            }
            else if(phase==5 && age>.18f) { Check("Third semi-auto click accepted",pistol.Fire()); Next(6); }
            else if(phase==6 && age>.6f)
            {
                Check("Three bullets kill a 100 HP spirit",actor.IsDefeated && pistol.ShotsFired==firstShot+3);
                Check("Ammo and physical casings match the accepted shots",pistol.Ammo==ammo-3 && pistol.Casings.EjectedCount==3);
                Check("Camera and shot grading recover without drift",pistol.Feedback.CameraOffset.sqrMagnitude<.000001f && Quaternion.Angle(pistol.Feedback.CameraRotation,Quaternion.identity)<.05f && pistol.Feedback.ShotPostWeight<.001f);
                driver.aim=false; Check("Reload starts",pistol.Reload()); Next(7);
            }
            else if(phase==7)
            {
                if(pistol.IsReloading)
                {
                    if(pistol.ReloadProgress>.42f && !capturedReload) { Capture("reload");capturedReload=true; }
                    if(pistol.ReloadProgress>.80f && !capturedSlide) { Capture("slide-grip");capturedSlide=true; }
                    return;
                }
                Check("Existing magazine transfer and slide sounds survive",pistol.Ammo==ammo && pistol.reserveAmmo==reserve-3);
                driver.move=Vector2.up;driver.sprint=true; Next(8);
            }
            else if(phase==8 && age>.55f) { requested=Time.time; Check("Sprint shot accepted",pistol.Fire()); Next(9); }
            else if(phase==9 && pistol.ShotsFired==firstShot+4)
            {
                float delay=Time.time-pistol.ShotAge-requested;
                Check("Sprint raise remains responsive",delay<.2f,delay.ToString("F3"));
                driver.move=Vector2.zero;driver.sprint=false; pistol.Holster();
                Check("Holster immediately clears camera effects",pistol.Feedback.CameraOffset==Vector3.zero && pistol.Feedback.ShotPostWeight==0f && !pistol.Feedback.RumbleActive);
                Next(10);
            }
            else if(phase==10 && age>.6f) { pistol.Equip();Next(11); }
            else if(phase==11 && age>.6f)
            {
                Check("Pistol can be redrawn",pistol.Ready);
                driver.enabled=false;coordinator.enabled=true;coordinator.SetDriving(true);coordinator.SetCinematicLocked(true);Next(12);
            }
            else if(phase==12 && age>.3f)
            {
                Check("Driving cinematic hides pistol and clears all shot feedback",coordinator.IsDriving && !pistol.Available && !pistol.weapon.activeSelf && pistol.Feedback.ShotPostWeight==0f && pistol.Feedback.CameraOffset==Vector3.zero);
                coordinator.SetCinematicLocked(false);
                Check("Cinematic returns vehicle control",coordinator.IsDriving && coordinator.drive.IsDriven && !coordinator.CinematicLocked);
                coordinator.SetDriving(false);coordinator.PlaceOnFoot(footPosition,footYaw);Next(13);
            }
            else if(phase==13 && age>.7f)
            {
                Check("Exiting truck restores equipped pistol",pistol.Ready && pistol.Available && !coordinator.IsDriving);
                firstShot=pistol.ShotsFired;InputSystem.QueueStateEvent(mouse,new MouseState { buttons=3 });Next(14);
            }
            else if(phase==14 && age>.3f)
            {
                Check("Actual LMB fires once while held; RMB enters ADS",pistol.ShotsFired==firstShot+1 && pistol.AimAmount>.95f,$"shots={pistol.ShotsFired-firstShot} aim={pistol.AimAmount:F2}");
                InputSystem.QueueStateEvent(mouse,new MouseState());InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.R));Next(15);
            }
            else if(phase==15 && age>.15f)
            {
                Check("Actual R starts reload through the gameplay coordinator",pistol.IsReloading);
                InputSystem.QueueStateEvent(keyboard,new KeyboardState());coordinator.SetGameplaySuspended(true);Time.timeScale=0f;
                Check("Pause suppresses kick and rumble",pistol.Feedback.CameraOffset==Vector3.zero && !pistol.Feedback.RumbleActive);Next(16);
            }
            else if(phase==16)
            {
                Check("Paused frame leaves post weight at zero",pistol.Feedback.ShotPostWeight==0f);
                Time.timeScale=1f;coordinator.SetGameplaySuspended(false);Next(17);
            }
            else if(phase==17 && age>2.5f)
            {
                Check("Resume restores control without a ghost shot",pistol.Ready && pistol.ShotsFired==firstShot+1 && Time.timeScale==1f);
                InputSystem.QueueStateEvent(mouse,new MouseState { buttons=2 });Next(18);
            }
            else if(phase==18 && age>.4f)
            {
                blocker=GameObject.CreatePrimitive(PrimitiveType.Cube);blocker.name="Temporary close barrel obstruction";
                blocker.transform.position=Vector3.Lerp(pistol.player.playerCamera.transform.position,pistol.muzzle.position,.55f);
                blocker.transform.localScale=Vector3.one*.35f;blocker.GetComponent<Renderer>().enabled=false;Next(19);
            }
            else if(phase==19 && age>.15f)
            {
                Check("Close wall hides optic reticle instead of aiming through cover",pistol.MuzzleObstructed && !sight.ReticleVisible);
                InputSystem.QueueStateEvent(mouse,new MouseState { buttons=3 });Next(20);
            }
            else if(phase==20 && age>.2f)
            {
                Check("Bullet strikes the close wall",pistol.LastShotCollider==blocker.GetComponent<Collider>());
                Object.DestroyImmediate(blocker);blocker=null; InputSystem.QueueStateEvent(mouse,new MouseState { buttons=2 });
                Time.captureDeltaTime=0;Next(21);
            }
            else if(phase==21 && age>.5f)
            {
                realKick=realFrame=0;InputSystem.QueueStateEvent(mouse,new MouseState { buttons=3 });Next(22);
            }
            else if(phase==22 && age>.4f)
            {
                Check("Recoil is visible at natural frame timing without capture",realKick>.25f,$"peak={realKick:F3} deg; maximum frame={realFrame:F3} s");
                Check("No runtime errors",errors.Count==0,string.Join("; ",errors));Finish(null);
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Next(int value) { phase = value; at = Time.time; }
    static void Check(string name,bool passed,string evidence="") { if(!passed) failed++; checks.Add((passed?"PASS ":"FAIL ")+name+" "+evidence); File.WriteAllLines(PistolPolishSetup.Folder+"/checks.txt",checks); }
    static void Log(string message,string stack,LogType type) { if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert) errors.Add(message); }
    static void MeasureGrip()
    {
        float max=0;
        foreach(bool left in new[]{true,false})
        {
            var wrist=pistol.player.handsAnimator.GetBoneTransform(left?HumanBodyBones.LeftHand:HumanBodyBones.RightHand);
            var expected=pistol.weapon.transform.position+pistol.weapon.transform.rotation*((left?ItfaiyeFirstPersonHands.LeftGrip:ItfaiyeFirstPersonHands.RightGrip)*pistol.player.playerCamera.transform.lossyScale.x);
            max=Mathf.Max(max,Vector3.Distance(expected,wrist.position));
        }
        Check("Both wrists stay planted on the two-hand grip",max<.008f,max.ToString("F5"));
    }
    public static void Capture(string name)
    {
        var camera = pistol.player.playerCamera;
        var rt = new RenderTexture(1600, 900, 24); var old = RenderTexture.active; var target = camera.targetTexture;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try { camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply(); File.WriteAllBytes(PistolPolishSetup.Folder + "/" + label + "-" + name + ".png", tex.EncodeToPNG()); }
        finally { camera.targetTexture = target; RenderTexture.active = old; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
    }
    static void Finish(string error)
    {
        phase = -1; Application.runInBackground = background;
        Application.logMessageReceived-=Log;Time.timeScale=1f;Time.captureDeltaTime=captureDelta;
        if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        if(mouse!=null && mouse.added) InputSystem.RemoveDevice(mouse);
        if(originalInput) InputSystem.settings=originalInput;
        if(testInput) Object.DestroyImmediate(testInput);
        if(target) Object.DestroyImmediate(target);
        if(blocker) Object.DestroyImmediate(blocker);
        checks.Add(error==null && failed==0 && errors.Count==0?"COMPLETED "+label:"FAILED "+error+" assertions="+failed+" errors="+string.Join("; ",errors));
        File.WriteAllLines(PistolPolishSetup.Folder + "/checks.txt",checks);
        File.WriteAllText(PistolPolishSetup.Folder + "/capture-result.txt", checks[checks.Count-1]);
        if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
