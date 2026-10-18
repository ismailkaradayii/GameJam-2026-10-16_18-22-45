using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Itfaiye;
using Itfaiye.Editor;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Actual scaled player, hands and truck presentation in saved Volcano_Demo Play Mode.</summary>
[InitializeOnLoad]
public static class CombatViewChecks
{
    const string Pending = "CombatViewChecks.Pending";
    const string Folder = "Logs/CombatView";
    static FireRescuePlayer player;
    static ItfaiyeWeaponReviewDriver driver;
    static int phase = -1, frame;
    static float at;
    static bool background, before;
    static double deadline;
    static string error;
    static readonly List<string> lines = new List<string>();
    static readonly Vector3 Origin = new Vector3(12000,300,12000);
    static GameObject fixtures;
    static LavaCreatureActor target;
    static Keyboard keyboard;
    static Mouse mouse;
    static InputSettings oldInput, testInput;
    static float normalCameraY, trackingError;
    static int ammo, reserve;
    static Vector3 inputTargetPosition;
    static Vector3 originalTruckPosition;
    static Quaternion originalTruckRotation;
    static LavaCreatureActor waterTarget;
    static bool waterHit, waterDefeated;
    static CombatViewChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted");
        };
    }
    [MenuItem("GameJam/Final Loop/Capture combat controls before")]
    public static void Before() => Begin(true);
    [MenuItem("GameJam/Final Loop/Check combat controls")]
    public static void After() => Begin(false);
    [MenuItem("GameJam/Final Loop/Check pistol grip")]
    public static void Grip() => Begin(true);
    static void Begin(bool baseline)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use idle Edit Mode.");
        Directory.CreateDirectory(Folder);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty)
        {
            Directory.CreateDirectory("Library/CombatViewReview");
            EditorSceneManager.SaveScene(scene, "Library/CombatViewReview/unsaved-preserved.unity", true);
        }
        SessionState.SetBool(Pending + ".Before", baseline); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); frame = -1; error = null; before = SessionState.GetBool(Pending + ".Before", false);
        background = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += Log;
        try
        {
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            originalTruckPosition=player.drive.transform.position; originalTruckRotation=player.drive.transform.rotation;
            player.mission.enabled = false; player.mission.story.enabled = false; player.mission.cinematic.enabled = false;
            player.mission.sequence.enabled = false; player.gameFlow.enabled = false; player.SetCinematicLocked(false);
            player.SetDriving(false); player.enabled = false;
            player.PlaceOnFoot(player.mission.story.defensePosition.position + Vector3.up * .15f, 180);
            player.AcquirePistol(); player.pistol.SetAvailable(true); player.pistol.Equip();
            driver = player.gameObject.AddComponent<ItfaiyeWeaponReviewDriver>(); driver.player = player.firstPerson; driver.pistol = player.pistol;
            phase = 0; at = Time.time; deadline = EditorApplication.timeSinceStartup + 120;
            lines.Add("Actual saved player at scene scale. Movement/input and intro are supplied by an isolated Play Mode fixture; no scene authoring."); Save();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (error != null) throw new Exception(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase);
            float age = Time.time - at;
            if (phase >= 3) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            if (phase == 0 && age > 1)
            {
                CaptureGrip(before ? "before" : "after");
                FramePhoto((before ? "before" : "after") + "-hip"); Next(10);
            }
            else if (phase == 10 && age > .4f) { driver.aim = true; Next(1); }
            else if (phase == 1 && age > .8f)
            {
                CaptureGrip((before ? "before" : "after") + "-ads");
                FramePhoto((before ? "before" : "after") + "-aim"); Next(2);
            }
            else if (phase == 2 && age > .4f)
            {
                if (before) { CheckGrip(); Finish(null); return; }
                CheckGrip(); CheckAimRules(); StartInputFixture(); Next(3);
            }
            else if (phase == 3)
            {
                Input(age > .6f, false);
                if (age < 1.6f) return;
                Check("Real RMB input acquires a visible enemy", player.pistol.IsAiming && player.pistolAimAssist.Target == target, "Input System mouse -> coordinator -> soft aim");
                float angle = AimError();
                Check("RMB brings the real camera onto the target", angle < 1.1f, "error=" + angle);
                trackingError = 0; Next(4);
            }
            else if (phase == 4)
            {
                Input(true, false); target.transform.position = inputTargetPosition + Vector3.right * (Mathf.Sin(age * 3f) * .8f);
                if (age > .2f) trackingError = Mathf.Max(trackingError, AimError());
                if (age < 1.2f) return;
                Check("Soft lock follows lateral movement without switching target", player.pistolAimAssist.Target == target && trackingError < 2.5f, "peak error=" + trackingError);
                ammo = player.pistol.Ammo; FramePhoto("after-soft-aim"); Next(5);
            }
            else if (phase == 5)
            {
                Input(true, age < .06f);
                if (age < .45f) return;
                Check("Assisted shot uses the actual muzzle and collider", target.Health < 100 && player.pistol.LastShotCollider && player.pistol.LastShotCollider.GetComponentInParent<LavaCreatureActor>() == target, "health=" + target.Health);
                Check("Assist does not create free bullets or automatic fire", player.pistol.Ammo == ammo - 1, "ammo=" + player.pistol.Ammo);
                reserve = player.pistol.reserveAmmo; Input(false, false, true); Next(6);
            }
            else if (phase == 6)
            {
                Input(false, false, age < .05f);
                if (age < 2.7f) return;
                Check("Releasing RMB clears the target", !player.pistolAimAssist.Target, "no held lock");
                Check("Reload retains magazine and reserve accounting", !player.pistol.IsReloading && player.pistol.Ammo == player.pistol.magazineSize && player.pistol.reserveAmmo == reserve - 1, $"ammo={player.pistol.Ammo} reserve={player.pistol.reserveAmmo}");
                player.drive.body.isKinematic = true; player.drive.enabled = false;
                MoveTruck(Origin + new Vector3(0,.8f,60),Quaternion.identity);
                player.SetDriving(true); Input(false,false); Next(7);
            }
            else if (phase == 7)
            {
                Input(false,false);
                if (age < 1.3f) return;
                normalCameraY = player.viewCamera.transform.position.y;
                FramePhoto("after-truck-normal"); Next(8);
            }
            else if (phase == 8)
            {
                Input(true,false);
                if (age < 1f) return;
                Check("Cannon view is higher and over the cab", player.viewCamera.transform.position.y > normalCameraY + 2f && player.viewCamera.transform.position.z > player.drive.transform.position.z + 8f,
                    $"normalY={normalCameraY:F2} aimed={player.viewCamera.transform.position:F2}");
                Vector3 near = player.drive.transform.position + Vector3.forward * 18 + Vector3.up * 2;
                Vector3 screen = player.viewCamera.WorldToViewportPoint(near);
                Check("A creature just beyond the bumper fits in the aimed view", screen.z > 0 && screen.x > 0 && screen.x < 1 && screen.y > 0 && screen.y < 1, screen.ToString("F3"));
                bool truckBlocks = Physics.RaycastAll(player.viewCamera.transform.position, (near-player.viewCamera.transform.position).normalized, Vector3.Distance(near,player.viewCamera.transform.position), ~0, QueryTriggerInteraction.Ignore)
                    .Any(h => h.collider.transform.IsChildOf(player.drive.transform));
                Check("Truck body no longer blocks the close front target", !truckBlocks, "physical body collision ray");
                Check("Raised monitor has extra downward travel", player.cannon.pitchPivot.localPosition.y > .5f && player.cannon.maximumDepression >= 60, $"pivot={player.cannon.pitchPivot.localPosition.y:F2} depression={player.cannon.maximumDepression}");
                FramePhoto("after-cannon-view");
                waterTarget=Enemy("Close cannon damage receiver",near);Physics.SyncTransforms();
                player.hose.SetTankLitres(300);waterHit=waterDefeated=false;
                Vector3 aimPoint=waterTarget.GetComponent<Collider>().bounds.center;
                Vector3 delta=aimPoint-player.viewCamera.transform.position;
                float desiredPitch=-Mathf.Atan2(delta.y,new Vector2(delta.x,delta.z).magnitude)*Mathf.Rad2Deg;
                float pitch=typeof(FireRescuePlayer).GetField("lookPitch",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(player) is float p?p:50;
                Input(true,false,false,new Vector2(0,-(desiredPitch-pitch)/player.mouseSensitivity));Next(14);
            }
            else if(phase==14)
            {
                Input(true,false);
                waterHit|=waterTarget&&waterTarget.Health<100;waterDefeated|=waterTarget&&waterTarget.IsDefeated;
                if(age<3)return;
                Check("Raised cannon can actually defeat a close front creature",waterHit&&waterDefeated&&player.hose.TankLitres<240,"water remaining="+player.hose.TankLitres);
                Next(9);
            }
            else if (phase == 9)
            {
                Input(false,false);
                if (age < .9f) return;
                Check("Releasing cannon aim restores the chase camera", Mathf.Abs(player.viewCamera.transform.position.y - normalCameraY) < .4f, "returned Y=" + player.viewCamera.transform.position.y);
                MoveTruck(originalTruckPosition,originalTruckRotation);
                player.SetDriving(false); player.SetDriving(true);
                var staging=new GameObject("Inactive authored creature capture");staging.SetActive(false);
                var authored=Object.Instantiate(player.mission.encounter.spawns[0].creature,staging.transform);
                authored.transform.SetParent(fixtures.transform,true);Object.Destroy(staging);
                authored.transform.position=TruckLakeRouteTools.Ground(originalTruckPosition+player.drive.transform.forward*19)+Vector3.up*.06f;
                authored.gameObject.SetActive(true);
                if(authored.jump)authored.jump.enabled=false;
                var combat=authored.GetComponent<LavaCreatureCombat>();if(combat)combat.enabled=false;
                Next(11);
            }
            else if(phase==11) { Input(false,false); if(age>1.3f)Next(12); }
            else if(phase==12) { Input(true,false); if(age>1) { lines.Add($"CAPTURE original truck={originalTruckPosition:F2} current={player.drive.transform.position:F2}");Save();FramePhoto("after-cannon-village");Next(13); } }
            else if(phase==13)
            {
                Input(false,false);if(age<.4f)return;
                FireCombatChecks.Run();Check("Existing water/ballistic/damage regression suite",File.ReadAllText("Logs/FireCombatChecks.json").Contains("\"allPassed\": true"),"39 existing checks");
                Finish(null);
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void CheckGrip()
    {
        var p = player.pistol; var a = player.firstPerson.handsAnimator;
        Vector3 right = a.GetBoneTransform(HumanBodyBones.RightHand).position;
        Vector3 expected = p.weapon.transform.position + p.weapon.transform.rotation * ItfaiyeFirstPersonHands.RightGrip * player.viewCamera.transform.lossyScale.x;
        Check("Right wrist remains seated during ADS", Vector3.Distance(right,expected) < .008f, "error=" + Vector3.Distance(right,expected));
        Vector3 Reference(HumanBodyBones bone) => Quaternion.Inverse(p.weapon.transform.rotation) *
            (a.GetBoneTransform(bone).position-p.weapon.transform.position)/player.viewCamera.transform.lossyScale.x;
        var middle=Reference(HumanBodyBones.RightMiddleProximal);
        // Measured frontstrap below the trigger guard: knuckles must sit below it,
        // rather than checking only that the wrist matches its own authored constant.
        Check("Right palm follows the grip below the trigger guard",middle.y<-.025f && middle.y>-.055f && middle.z<-.04f && middle.z>-.075f && middle.x>.008f && middle.x<.03f,middle.ToString("F4"));
        var thumb=Reference(HumanBodyBones.RightThumbDistal);
        Check("Right thumb stays alongside the frame",thumb.x>-.035f && thumb.x<-.005f && thumb.y>.015f,thumb.ToString("F4"));
        var hands = a.GetComponent<ItfaiyeFirstPersonHands>();
        Check("Authored hip position is closer to centre and less tilted", hands.pistolHipPosition.x <= .06f && Mathf.Abs(hands.pistolHipEuler.z) <= 2.1f, $"hip={hands.pistolHipPosition} roll={hands.pistolHipEuler.z}");
    }
    static LavaCreatureActor Enemy(string name, Vector3 point)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); go.name = name; go.transform.SetParent(fixtures.transform);
        go.transform.position = point; go.transform.localScale = Vector3.one * 2;
        return go.AddComponent<LavaCreatureActor>();
    }
    static void CheckAimRules()
    {
        fixtures = new GameObject("Isolated combat input fixture");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(fixtures.transform);
        floor.transform.position = Origin - Vector3.up; floor.transform.localScale = new Vector3(200,2,200);
        player.firstPerson.recoverFromFall = false; player.PlaceOnFoot(Origin + Vector3.up * .1f,0); player.firstPerson.SetInputCaptured(true);
        target = Enemy("Visible aim-assist target", Origin + new Vector3(4,2,28)); Physics.SyncTransforms();
        var assist = player.pistolAimAssist; float yaw = 0, pitch = 0;
        for(int i=0;i<60;i++) assist.AdjustLook(ref yaw,ref pitch,true,Vector2.zero,1f/60);
        Check("Soft acquisition selects a live visible creature", assist.Target == target && Mathf.Abs(yaw)>4, $"yaw={yaw:F2} pitch={pitch:F2}");
        float previous = yaw; assist.AdjustLook(ref yaw,ref pitch,false,Vector2.zero,1f/60);
        Check("No attraction without RMB", !assist.Target && yaw==previous, "released");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.SetParent(fixtures.transform); wall.transform.position = Origin + new Vector3(2,5,15); wall.transform.localScale = new Vector3(7,12,1); Physics.SyncTransforms();
        yaw=pitch=0; assist.AdjustLook(ref yaw,ref pitch,true,Vector2.zero,1f/60);
        Check("Walls prevent acquisition", !assist.Target && yaw==0, "solid occlusion");
        Object.DestroyImmediate(wall); Physics.SyncTransforms(); assist.Release();
        target.transform.position = Origin + new Vector3(0,2,-25); Physics.SyncTransforms();
        assist.AdjustLook(ref yaw,ref pitch,true,Vector2.zero,1f/60);
        Check("Targets behind the player cannot pull the view", !assist.Target, "outside cone");
        target.transform.position = Origin + new Vector3(4,2,28); Physics.SyncTransforms();
        assist.Release(); assist.AdjustLook(ref yaw,ref pitch,true,new Vector2(8,0),1f/60);
        Check("A deliberate mouse sweep overrides assistance", !assist.Target && yaw==0, "manual control wins");
        assist.Release(); target.Dissipate(); assist.AdjustLook(ref yaw,ref pitch,true,Vector2.zero,1f/60);
        Check("Defeated creatures are excluded", !assist.Target, "dead target");
        target = Enemy("Actual RMB target", Origin + new Vector3(4,2,28)); Physics.SyncTransforms();
        assist.Release(); Time.timeScale=0; assist.AdjustLook(ref yaw,ref pitch,true,Vector2.zero,1f/60); Time.timeScale=1;
        Check("Pause suspends assistance", !assist.Target && yaw==0, "no paused rotation");
        inputTargetPosition = target.transform.position;
    }
    static void StartInputFixture()
    {
        driver.enabled = false; player.enabled = true;
        oldInput = InputSystem.settings; testInput = Object.Instantiate(oldInput); testInput.hideFlags = HideFlags.HideAndDontSave;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; InputSystem.settings = testInput;
        keyboard=InputSystem.AddDevice<Keyboard>("CombatViewKeyboard"); mouse=InputSystem.AddDevice<Mouse>("CombatViewMouse");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        typeof(FireRescuePlayer).GetField("focused",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(player,true);
        player.PlaceOnFoot(Origin + Vector3.up * .1f,0); player.firstPerson.SetInputCaptured(true);
        player.pistolAimAssist.Release(); Input(false,false);
    }
    static void MoveTruck(Vector3 position,Quaternion rotation)
    { player.drive.body.position=position;player.drive.body.rotation=rotation;player.drive.transform.SetPositionAndRotation(position,rotation);Physics.SyncTransforms(); }
    static void Input(bool aim, bool fire, bool reload=false,Vector2 delta=default)
    {
        if (keyboard==null) return;
        InputSystem.QueueStateEvent(keyboard,reload?new KeyboardState(Key.R):new KeyboardState());
        var state = new MouseState { delta=delta }; if(aim) state=state.WithButton(MouseButton.Right); if(fire)state=state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(mouse,state);
    }
    static float AimError() => Vector3.Angle(player.viewCamera.transform.forward,player.pistolAimAssist.TargetPoint-player.viewCamera.transform.position);
    static void Next(int value) { phase=value;at=Time.time;deadline=EditorApplication.timeSinceStartup+90; }
    static void Check(string name,bool pass,string evidence)
    { lines.Add((pass?"PASS ":"FAIL ")+name+": "+evidence);Save();if(!pass)throw new Exception(name+": "+evidence); }
    static void FramePhoto(string name)
    { var c = player.GetComponent<CombatViewCapture>() ?? player.gameObject.AddComponent<CombatViewCapture>(); c.Photo(Folder+"/"+name+".png"); }
    static void CaptureGrip(string name)
    {
        var p = player.pistol; var w = p.weapon.transform; float scale = player.viewCamera.transform.lossyScale.x;
        var animator = player.firstPerson.handsAnimator;
        foreach (var bone in new[] { HumanBodyBones.RightHand, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbDistal, HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleDistal, HumanBodyBones.LeftHand })
            lines.Add(bone + " weapon-reference=" + (Quaternion.Inverse(w.rotation) * (animator.GetBoneTransform(bone).position - w.position) / scale).ToString("F4"));
        var go = new GameObject("Combat grip review camera"); var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f,.19f,.22f);
        camera.cullingMask = 1 << LayerMask.NameToLayer("FirstPersonHands"); camera.nearClipPlane = .01f; camera.farClipPlane = 20;
        camera.orthographic = true; camera.orthographicSize = .17f * scale;
        var rt = new RenderTexture(1000, 900, 24); var texture = new Texture2D(1000,900,TextureFormat.RGB24,false); var old = RenderTexture.active;
        var light = new GameObject("Combat grip review light").AddComponent<Light>(); light.type = LightType.Point; light.range = 10; light.intensity = .4f;
        try
        {
            Vector3 target = w.position + w.rotation * new Vector3(0,-.025f,-.07f) * scale;
            var offsets = new[] { new Vector3(.5f,.12f,-.32f), new Vector3(-.5f,.12f,-.2f), new Vector3(0,.5f,0) };
            for (int i = 0; i < offsets.Length; i++)
            {
                camera.transform.position = target + w.rotation * offsets[i] * scale; camera.transform.LookAt(target, w.up);
                light.transform.position = camera.transform.position; camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0,0,1000,900),0,0); texture.Apply(); File.WriteAllBytes(Folder + "/" + name + "-grip-" + i + ".png", texture.EncodeToPNG());
            }
            camera.orthographic = false;
            camera.transform.SetPositionAndRotation(player.viewCamera.transform.position,player.viewCamera.transform.rotation);
            camera.fieldOfView=player.firstPerson.handsCamera.fieldOfView;camera.aspect=1000f/900f;
            light.transform.position=camera.transform.position;camera.Render();RenderTexture.active=rt;
            texture.ReadPixels(new Rect(0,0,1000,900),0,0);texture.Apply();File.WriteAllBytes(Folder+"/"+name+"-grip-fps.png",texture.EncodeToPNG());
        }
        finally { RenderTexture.active = old; Object.DestroyImmediate(go); Object.DestroyImmediate(light.gameObject); Object.DestroyImmediate(texture); rt.Release(); Object.DestroyImmediate(rt); }
        Save();
    }
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message; }
    static void Save() => File.WriteAllLines(Folder + "/" + (before ? "before" : "after") + "-checks.txt", lines);
    static void Finish(string failure)
    {
        phase = -1; Application.logMessageReceived -= Log; Application.runInBackground = background;
        if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);
        if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);
        if(oldInput)InputSystem.settings=oldInput;
        if(testInput)Object.DestroyImmediate(testInput);
        Time.timeScale=1;
        lines.Add(failure == null ? "COMPLETED " + DateTime.UtcNow.ToString("O") : "FAILED " + failure); Save(); EditorApplication.ExitPlaymode();
    }
}

public sealed class CombatViewCapture : MonoBehaviour
{
    public void Photo(string path) => StartCoroutine(Capture(path));
    System.Collections.IEnumerator Capture(string path)
    {
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        if(image) { File.WriteAllBytes(path,image.EncodeToPNG());Destroy(image); }
    }
}
