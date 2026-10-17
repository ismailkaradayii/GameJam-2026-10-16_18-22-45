using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VillageFirePreview
{
    static Camera camera;
    static BurnableBuilding house;
    static float began;
    static bool active, background;
    static double deadline;
    static VillageFirePreview()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if(state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("VillageFirePreview.Pending",false))
            { SessionState.SetBool("VillageFirePreview.Pending",false); EditorApplication.delayCall += Start; }
        };
    }
    public static void Begin()
    {
        if(EditorApplication.isPlaying) return;
        SessionState.SetBool("VillageFirePreview.Pending",true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        try
        {
            background=Application.runInBackground; Application.runInBackground=true;
            foreach(var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if(component is VolcanoMissionDirector || component is VolcanoCinematic || component is VolcanoEruptionSequence ||
                    component is FireRescuePlayer || component is EvacuationMission || component is ProgressiveLavaFlow ||
                    component is LavaCreatureEncounter || component is LavaIgnitionSource) component.enabled=false;
            foreach(var view in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) view.enabled=false;
            house=Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).First(h=>h.name=="House_Green_Prefab (2)");
            house.secondsToCollapse=400; house.Ignite();
            camera=new GameObject("Village fire review camera").AddComponent<Camera>(); camera.fieldOfView=58;
            camera.farClipPlane=2000; camera.nearClipPlane=.1f; camera.allowHDR=true;
            Bounds b=house.ignitionBounds.bounds;
            camera.transform.position=b.center+new Vector3(-b.extents.x*1.6f,b.size.y*.5f,b.extents.z*2.1f);
            camera.transform.LookAt(b.center+Vector3.up*2);
            Time.timeScale=1; began=Time.time; deadline=EditorApplication.timeSinceStartup+75; active=true;
        }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        if(!active||!EditorApplication.isPlaying) return;
        if(EditorApplication.timeSinceStartup>deadline) { Finish("Preview timed out"); return; }
        if(Time.time-began<7) return;
        try
        {
            string folder=VillageCombatUpgradeSetup.Folder; Directory.CreateDirectory(folder);
            var rt=new RenderTexture(1440,960,24); var tex=new Texture2D(1440,960,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt;
                tex.ReadPixels(new Rect(0,0,1440,960),0,0); tex.Apply(); File.WriteAllBytes(folder+"/village-fire.png",tex.EncodeToPNG());
            }
            finally { camera.targetTexture=null; RenderTexture.active=previous; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
            Finish("Captured real village house; particles="+house.GetComponent<BuildingFireVisual>().ActiveParticles);
        }
        catch(Exception ex) { Finish(ex.ToString()); }
    }
    static void Finish(string result)
    {
        active=false; Application.runInBackground=background;
        File.WriteAllText(VillageCombatUpgradeSetup.Folder+"/preview.txt",result);
        if(EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
