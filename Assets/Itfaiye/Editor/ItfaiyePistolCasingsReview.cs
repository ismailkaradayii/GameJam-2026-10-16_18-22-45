using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Itfaiye.Editor
{
    [InitializeOnLoad]
    public static class ItfaiyePistolCasingsReview
    {
        const string Folder="Library/ItfaiyePistol/Casings";
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static ItfaiyePistolCasings casings;
        static ItfaiyePistolCasing firstCasing;
        static readonly StringBuilder Report=new StringBuilder();
        static int phase=-1,frame,errors,sample;
        static float began,savedLifetime,savedVolume,savedCaptureDeltaTime;
        static string savedHands;
        static Vector3 spawn;
        static readonly float[] ReloadSamples={.78f,.812f,.85f,.88f,.907f,.97f};
        static ItfaiyePistolCasingsReview() { EditorApplication.update+=Update; }

        [MenuItem("Tools/Itfaiye/Review Casings and Slide %&F12")]
        public static void Start()
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player=UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>(); pistol=player.pistol; casings=pistol.Casings;
            if(casings==null || pistol.ShotsFired!=0) throw new InvalidOperationException("Use a fresh Play session with casings installed.");
            Directory.CreateDirectory(Folder); Report.Clear(); errors=0; frame=-1; sample=0;
            savedHands=EditorJsonUtility.ToJson(pistol.Hands); savedLifetime=casings.casingLifetime; savedVolume=casings.landingVolume;
            savedCaptureDeltaTime=Time.captureDeltaTime;
            Time.captureDeltaTime=1f/60f;
            Application.logMessageReceived+=Log;
            player.SetInputCaptured(false); pistol.Equip(); Next(0);
        }
        static void Next(int next) { phase=next; began=Time.time; }
        static void Update()
        {
            if(phase<0) return;
            if(!EditorApplication.isPlaying) { Finish("ABORTED"); return; }
            if(EditorApplication.isPaused || frame==Time.frameCount) return;
            frame=Time.frameCount;
            try { Tick(); } catch(Exception ex) { Finish("FAILED: "+ex); }
        }
        static void Tick()
        {
            player.SetInputCaptured(false);
            float elapsed=Time.time-began;
            if(elapsed>10) throw new TimeoutException("Casing review stalled at "+phase);
            switch(phase)
            {
                case 0:
                    if(elapsed<.6f) return;
                    Check("Casing mesh, physics and sounds are assigned",casings.casingMesh!=null && casings.casingPhysics!=null && casings.brassMaterial!=null && casings.insideMaterial!=null && casings.hardSurfaceClinks.Length==3 && casings.softSurfaceClink!=null);
                    Check("No casing before firing",casings.EjectedCount==0&&casings.ContactSoundCount==0);
                    Check("Live round fired",pistol.Fire()); Next(1); return;
                case 1:
                    if(casings.EjectedCount==0) return;
                    firstCasing=casings.LastCasing; spawn=casings.LastEjectionPosition;
                    Check("Exactly one empty casing per shot",casings.EjectedCount==1&&pistol.ShotsFired==1&&pistol.Ammo==11);
                    Check("Ejects to pistol right",Vector3.Dot(casings.LastEjectionVelocity,pistol.weapon.transform.right)>1.5f);
                    Check("Casing starts at the right ejection port",Vector3.Distance(spawn,pistol.weapon.transform.position+pistol.weapon.transform.rotation*casings.ejectionPosition)<.035f);
                    Check("Casing uses gravity, spin and continuous collision",firstCasing.Body.useGravity&&firstCasing.Body.angularVelocity.magnitude>10&&firstCasing.Body.collisionDetectionMode==CollisionDetectionMode.ContinuousDynamic);
                    Check("No contact sound while airborne",!firstCasing.HasLanded&&casings.ContactSoundCount==0);
                    Check("Player collision ignored",Physics.GetIgnoreCollision(firstCasing.Collider,player.GetComponent<CharacterController>()));
                    Check("Casing excluded from aiming rays",firstCasing.gameObject.layer==2 && (Physics.DefaultRaycastLayers & (1<<firstCasing.gameObject.layer))==0);
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/ejection.png")); CaptureCase("casing-closeup",firstCasing);
                    Next(2); return;
                case 2:
                    if(elapsed<2f) return;
                    Check("Casing drops to the actual floor",firstCasing.HasLanded&&firstCasing.transform.position.y<spawn.y-.5f);
                    Check("Floor contact plays a clink",casings.ContactSoundCount>0&&firstCasing.ContactsPlayed>0);
                    Check("Bounces do not spam sound",firstCasing.ContactsPlayed<=3);
                    CaptureCase("casing-landed",firstCasing);
                    Check("Reload starts",pistol.Reload()); Next(3); return;
                case 3:
                    ObserveReload();
                    if(elapsed<pistol.reloadDuration+.2f) return;
                    Check("Reload creates no spent casing",casings.EjectedCount==1);
                    Check("Slide pull and release each sound once",casings.SlidePullSoundCount==1&&casings.SlideReleaseSoundCount==1);
                    Check("Magazine, slide and support grip return",pistol.Ready&&pistol.Ammo==12&&pistol.reserveAmmo==47&&pistol.SlideAmount<.001f);
                    Check("Configured hand settings were not changed by playback",savedHands==EditorJsonUtility.ToJson(pistol.Hands));
                    casings.casingLifetime=.2f; casings.landingVolume=0;
                    for(int i=0;i<casings.maximumCasings+4;i++) casings.QueueEjection();
                    Next(4); return;
                case 4:
                    if(elapsed<.07f) return;
                    Check("Casing pool has a fixed maximum",casings.ActiveCasingCount==casings.maximumCasings&&casings.EjectedCount==casings.maximumCasings+5);
                    Next(5); return;
                case 5:
                    if(elapsed<.4f) return;
                    Check("Expired casings return to pool",casings.ActiveCasingCount==0);
                    Check("No runtime errors",errors==0);
                    Finish(Report.ToString().Contains("FAIL ")?"FAILED":"SUCCESS"); return;
            }
        }
        static void ObserveReload()
        {
            if(!pistol.IsReloading || sample>=ReloadSamples.Length || pistol.ReloadProgress<ReloadSamples[sample]) return;
            float t=pistol.ReloadProgress;
            var pose=pistol.ReloadPose;
            if(sample==1)
            {
                var contact=ItfaiyePistolReloadPose.Sample(ItfaiyePistolReloadPose.SlidePullStarts-.001f,pistol.MagazineBase,false,pistol.Hands);
                Check("Fingers close before the pull",contact.slideGrip>.98f&&contact.slidePull<.01f);
            }
            if(sample==2||sample==3)
            {
                var wrist=player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
                var expected=pistol.slide.position+pistol.weapon.transform.rotation*pistol.Hands.slideGripPosition;
                Check("Left wrist stays attached during pull "+t.ToString("F3"),Vector3.Distance(wrist.position,expected)<.004f&&pose.slideGrip>.99f);
            }
            if(sample==3) Check("Visible 40 mm pull and short rear hold",Mathf.Abs(pose.slidePull*.032f-.04f)<.001f);
            if(sample==4) Check("Hand opens as slide springs forward",pose.openHand>.6f&&pose.slideGrip<.4f);
            if(sample==2 || sample==3) PistolGripWorkbench.CaptureViews(player,Folder+"/slide-"+sample);
            sample++;
        }
        static void CaptureCase(string name,ItfaiyePistolCasing casing)
        {
            var go=new GameObject("Temporary Casing Camera"); var camera=go.AddComponent<Camera>(); go.AddComponent<UniversalAdditionalCameraData>(); camera.enabled=false;
            var target=casing.transform.position;
            camera.transform.position=target+casing.transform.rotation*new Vector3(.06f,.05f,-.05f); camera.transform.LookAt(target);
            camera.orthographic=true; camera.orthographicSize=.026f; camera.nearClipPlane=.002f; camera.farClipPlane=.2f;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.1f,.13f,.17f); camera.cullingMask=1<<2;
            var rt=new RenderTexture(640,640,24); var texture=new Texture2D(640,640,TextureFormat.RGB24,false); var old=RenderTexture.active;
            try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,640,640),0,0); texture.Apply(); File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG()); }
            finally { RenderTexture.active=old; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt); }
        }
        static void Check(string name,bool pass) { Report.AppendLine((pass?"PASS ":"FAIL ")+name); File.WriteAllText(Folder+"/progress.txt",Report.ToString()); }
        static void Log(string message,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) { errors++; Report.AppendLine(message); } }
        static void Finish(string result)
        {
            phase=-1; Application.logMessageReceived-=Log;
            Time.captureDeltaTime=savedCaptureDeltaTime;
            if(casings!=null) { casings.casingLifetime=savedLifetime; casings.landingVolume=savedVolume; }
            if(player!=null) player.SetInputCaptured(false);
            File.WriteAllText(Folder+"/review.txt",result+"\n"+Report);
        }
    }
}
