using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Itfaiye.Editor
{
    [InitializeOnLoad]
    public static class ItfaiyePistolFeedbackReview
    {
        const string Folder="Library/ItfaiyePistol/Feedback";
        static ItfaiyePistol pistol;
        static ItfaiyePistolFeedback feedback;
        static ItfaiyeFirstPersonController player;
        static GameObject target;
        static Material material;
        static ItfaiyeImpactSurface surface;
        static readonly StringBuilder Report=new StringBuilder();
        static int phase=-1,frame,errors;
        static float began,maximumKick;
        static string grip;
        static Vector3 predicted,markPosition;
        static Quaternion markRotation;
        static Transform mark;
        static bool fired;
        static float savedLife,savedFade,savedShotVolume,savedImpactVolume;
        static ItfaiyePistolFeedbackReview() { EditorApplication.update+=Update; }

        [MenuItem("Tools/Itfaiye/Review Shot Feedback %#F10")]
        public static void Start()
        {
            if(!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player=UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>(); pistol=player.pistol; feedback=pistol.Feedback;
            if(feedback==null || pistol.ShotsFired!=0) throw new InvalidOperationException("Use a fresh Play session with feedback installed.");
            Directory.CreateDirectory(Folder); Report.Clear(); errors=0; maximumKick=0; frame=-1;
            grip=EditorJsonUtility.ToJson(pistol.Hands);
            savedLife=feedback.markLifetime; savedFade=feedback.markFadeTime; savedShotVolume=feedback.shotVolume; savedImpactVolume=feedback.impactVolume;
            Application.logMessageReceived+=Log;
            target=GameObject.CreatePrimitive(PrimitiveType.Cube); target.name="Temporary Feedback Test Wall";
            target.transform.SetPositionAndRotation(player.playerCamera.transform.position+player.playerCamera.transform.forward*2.8f,player.playerCamera.transform.rotation);
            target.transform.localScale=new Vector3(4,3,.15f);
            surface=target.AddComponent<ItfaiyeImpactSurface>();
            material=new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color=new Color(.45f,.44f,.42f);
            target.GetComponent<Renderer>().sharedMaterial=material; Physics.SyncTransforms();
            player.SetInputCaptured(false); pistol.Equip(); Next(0);
        }
        static void Next(int next) { phase=next; began=Time.time; fired=false; }
        static void Update()
        {
            if(phase<0) return;
            if(!EditorApplication.isPlaying) { Finish("ABORTED"); return; }
            if(EditorApplication.isPaused||frame==Time.frameCount) return;
            frame=Time.frameCount;
            try { Tick(); } catch(Exception ex) { Finish("FAILED: "+ex); }
        }
        static void Tick()
        {
            player.SetInputCaptured(false);
            float elapsed=Time.time-began;
            if(elapsed>12f) throw new TimeoutException("Feedback review stalled in phase "+phase);
            maximumKick=Mathf.Max(maximumKick,Quaternion.Angle(Quaternion.identity,feedback.CameraRotation));
            switch(phase)
            {
                case 0:
                    if(elapsed<.6f) return;
                    Check("Material and audio assets are included",feedback.impactMaterial!=null&&feedback.tracerMaterial!=null&&feedback.dustMaterial!=null&&feedback.sparkMaterial!=null&&feedback.shotClips.Length==3&&feedback.concreteImpact!=null&&feedback.metalImpact!=null&&feedback.woodImpact!=null&&feedback.dirtImpact!=null);
                    Check("Impact and tracer shaders compile",!ShaderUtil.ShaderHasError(feedback.impactMaterial.shader)&&!ShaderUtil.ShaderHasError(feedback.tracerMaterial.shader));
                    Check("Current muzzle ray selects test wall",pistol.AimCollider==target.GetComponent<Collider>());
                    predicted=pistol.AimPoint; Next(1); return;
                case 1:
                    if(!fired) { Check("Fire accepted",pistol.Fire()); fired=true; return; }
                    if(feedback.TotalImpacts<1) { if(elapsed>2) throw new Exception("Shot feedback did not run."); return; }
                    Check("Bullet mark uses actual crosshair hit",Vector3.Distance(predicted,pistol.LastShotPoint)<.01f&&feedback.LastMark!=null&&Vector3.Distance(feedback.LastMark.position,pistol.LastShotPoint)<.002f);
                    Check("Mark follows surface normal",Vector3.Dot(feedback.LastMark.forward,pistol.LastShotHit.normal)>.999f);
                    Check("Concrete emits dust/chips",feedback.LastSurface==ItfaiyeSurfaceKind.Concrete&&feedback.LastImpactParticleCount>0);
                    Check("Shot sound is playing",Array.Exists(player.GetComponents<AudioSource>(),a=>a.isPlaying));
                    Check("One click consumes one round",pistol.Ammo==11&&pistol.ShotsFired==1);
                    CaptureGame("concrete-shot"); CaptureImpact("concrete-impact",pistol.LastShotHit);
                    mark=feedback.LastMark; markPosition=mark.localPosition; markRotation=mark.localRotation;
                    Next(2); return;
                case 2:
                    if(elapsed<.4f) return;
                    CaptureImpact("concrete-mark",pistol.LastShotHit);
                    Check("Camera produces a controlled kick",maximumKick>.15f&&maximumKick<1.4f);
                    Check("Camera recovers with no aim drift",Quaternion.Angle(Quaternion.identity,feedback.CameraRotation)<.001f);
                    Check("Recoil keeps barrel ray in sync",Vector3.Angle(pistol.AimRay.direction,pistol.muzzle.forward)<.001f);
                    target.transform.position+=Vector3.right*.3f; target.transform.Rotate(0,12,0); Physics.SyncTransforms();
                    Check("Mark stays attached to moving/rotating target",mark.parent==target.transform&&Vector3.Distance(mark.localPosition,markPosition)<.0001f&&Quaternion.Angle(mark.localRotation,markRotation)<.001f);
                    surface.surface=ItfaiyeSurfaceKind.Metal; material.color=new Color(.18f,.22f,.25f); Next(3); return;
                case 3:
                    if(elapsed<.15f) return;
                    if(!fired) { Check("Second fire accepted",pistol.Fire()); fired=true; return; }
                    if(feedback.TotalImpacts<2) return;
                    Check("Metal selects sparks and metal sound",feedback.LastSurface==ItfaiyeSurfaceKind.Metal&&feedback.LastImpactParticleCount>0);
                    CaptureGame("metal-shot"); CaptureImpact("metal-impact",pistol.LastShotHit); Next(4); return;
                case 4:
                    if(!fired && elapsed>=.06f) { CaptureImpact("metal-sparks",pistol.LastShotHit); fired=true; }
                    if(elapsed<.35f) return;
                    surface.surface=ItfaiyeSurfaceKind.Wood; material.color=new Color(.34f,.22f,.12f); Next(5); return;
                case 5:
                    if(!fired) { Check("Wood shot accepted",pistol.Fire()); fired=true; return; }
                    if(feedback.TotalImpacts<3) return;
                    Check("Wood impact selected",feedback.LastSurface==ItfaiyeSurfaceKind.Wood&&feedback.LastMark!=null); CaptureImpact("wood-impact",pistol.LastShotHit); Next(6); return;
                case 6:
                    if(elapsed<.35f) return;
                    surface.surface=ItfaiyeSurfaceKind.Dirt; material.color=new Color(.28f,.21f,.14f); Next(7); return;
                case 7:
                    if(!fired) { Check("Dirt shot accepted",pistol.Fire()); fired=true; return; }
                    if(feedback.TotalImpacts<4) return;
                    Check("Dirt impact selected",feedback.LastSurface==ItfaiyeSurfaceKind.Dirt&&feedback.LastImpactParticleCount>0); Next(8); return;
                case 8:
                    if(elapsed<.4f) return;
                    feedback.shotVolume=feedback.impactVolume=0;
                    for(int i=0;i<feedback.maximumMarks+5;i++) feedback.PlayShot(pistol.LastShotRay,pistol.LastShotPoint,pistol.LastShotHit,0,false);
                    Check("Mark pool remains bounded after sustained fire",feedback.ActiveMarkCount==feedback.maximumMarks&&target.GetComponentsInChildren<MeshRenderer>().Length==feedback.maximumMarks+1);
                    int count=feedback.TotalImpacts,marks=feedback.ActiveMarkCount;
                    feedback.PlayShot(pistol.LastShotRay,pistol.LastShotRay.GetPoint(100),default,1,false);
                    Check("Miss creates no surface mark or impact",feedback.TotalImpacts==count&&feedback.ActiveMarkCount==marks);
                    feedback.markLifetime=.15f; feedback.markFadeTime=.05f;
                    feedback.PlayShot(pistol.LastShotRay,pistol.LastShotPoint,pistol.LastShotHit,0,false); mark=feedback.LastMark; Next(9); return;
                case 9:
                    if(elapsed<.3f) return;
                    Check("Old marks expire and release their slot",mark!=null&&!mark.gameObject.activeSelf);
                    Check("Hand and reload settings are unchanged",grip==EditorJsonUtility.ToJson(pistol.Hands));
                    Check("No runtime errors",errors==0);
                    Finish(Report.ToString().Contains("FAIL ")?"FAILED":"SUCCESS"); return;
            }
        }
        static void CaptureGame(string name) { ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder+"/"+name+".png")); }
        static void CaptureImpact(string name,RaycastHit hit)
        {
            var go=new GameObject("Temporary Impact Inspection Camera"); var camera=go.AddComponent<Camera>();
            go.AddComponent<UniversalAdditionalCameraData>(); camera.enabled=false;
            camera.transform.SetPositionAndRotation(hit.point+hit.normal*.5f,Quaternion.LookRotation(-hit.normal));
            camera.orthographic=true; camera.orthographicSize=.16f; camera.nearClipPlane=.01f; camera.farClipPlane=1f;
            camera.cullingMask=~(1<<LayerMask.NameToLayer("FirstPersonHands"));
            var rt=new RenderTexture(640,640,24); var texture=new Texture2D(640,640,TextureFormat.RGB24,false); var old=RenderTexture.active;
            try
            {
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; texture.ReadPixels(new Rect(0,0,640,640),0,0); texture.Apply();
                File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG());
            }
            finally { RenderTexture.active=old; camera.targetTexture=null; UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(rt); }
        }
        static void Check(string name,bool pass) { Report.AppendLine((pass?"PASS ":"FAIL ")+name); File.WriteAllText(Folder+"/progress.txt",Report.ToString()); }
        static void Log(string message,string stack,LogType type) { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert) { errors++; Report.AppendLine(message); } }
        static void Finish(string result)
        {
            phase=-1; Application.logMessageReceived-=Log;
            if(feedback!=null) { feedback.markLifetime=savedLife; feedback.markFadeTime=savedFade; feedback.shotVolume=savedShotVolume; feedback.impactVolume=savedImpactVolume; }
            if(player!=null) player.SetInputCaptured(false);
            if(target!=null) UnityEngine.Object.DestroyImmediate(target);
            if(material!=null) UnityEngine.Object.DestroyImmediate(material);
            File.WriteAllText(Folder+"/review.txt",result+"\n"+Report);
        }
    }
}
