using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class EvacuationSceneAudit
{
    public const string Folder = "Library/EvacuationReview";
    static double poll;
    static EvacuationSceneAudit() { EditorApplication.update += Update; }
    static void Update()
    {
        if (EditorApplication.timeSinceStartup < poll || EditorApplication.isCompiling) return;
        poll = EditorApplication.timeSinceStartup + .5;
        string path = Folder + "/request.txt";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "refresh") AssetDatabase.Refresh();
            else if (command == "setup") Invoke("EvacuationMissionSetup", "Run");
            else if (command == "bake") Invoke("EvacuationMissionSetup", "RebuildNavigation");
            else if (command == "save") Invoke("EvacuationMissionSetup", "ValidateAndSave");
            else if (command == "polish") Invoke("EvacuationMissionSetup", "Polish");
            else if (command == "finish") Invoke("EvacuationMissionSetup", "FinishAuthoring");
            else if (command == "review") Invoke("EvacuationMissionChecks", "Begin");
            else if (command == "stop") EditorApplication.ExitPlaymode();
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); }
    }
    static void Invoke(string type, string method)
    {
        var target = typeof(EvacuationSceneAudit).Assembly.GetType(type, true);
        target.GetMethod(method).Invoke(null, null);
    }
    static string V(Vector3 v) => v.ToString("F3");
    public static void Inspect()
    {
        var lines = new List<string> { "playing=" + EditorApplication.isPlaying, "scene=" + SceneManager.GetActiveScene().path, "dirty=" + SceneManager.GetActiveScene().isDirty };
        var objects = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in objects.Where(t => t.name.StartsWith("House_Green") || t.name.Contains("Shelter") || t.name == "Firefighter" || (t.parent == null && Vector3.Distance(t.position, new Vector3(-106,26,-565)) < 80)))
        {
            lines.Add("ROOT " + t.name + " pos=" + V(t.position) + " rot=" + V(t.eulerAngles) + " scale=" + V(t.lossyScale) + " prefab=" + PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t));
            foreach (var r in t.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)))
                lines.Add("  R " + r.name + " center=" + V(r.bounds.center) + " size=" + V(r.bounds.size));
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
                lines.Add("  C " + c.name + " type=" + c.GetType().Name + " trigger=" + c.isTrigger + " center=" + V(c.bounds.center) + " size=" + V(c.bounds.size));
        }
        foreach (string path in AssetDatabase.FindAssets("t:AnimationClip", new[]{"Assets/Animation"}).Select(AssetDatabase.GUIDToAssetPath).Distinct().Where(p=>!p.Contains("Pistol")))
            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__")))
                lines.Add("CLIP " + path + " name=" + clip.name + " human=" + clip.humanMotion + " length=" + clip.length);
        File.WriteAllLines(Folder + "/inspection.txt", lines);
        var detail = new List<string>();
        foreach (var h in objects.Where(t=>t.name.StartsWith("House_Green")))
        foreach (var t in h.GetComponentsInChildren<Transform>().Where(t=>t.name.Contains("Door04") || t.name == "hallway" || t.name == "floor"))
            detail.Add(h.name+"/"+t.name+" local="+V(h.InverseTransformPoint(t.position))+" parent="+t.parent.name+" rotation="+V(t.localEulerAngles)+" boundsLocal="+V(h.InverseTransformPoint(t.GetComponent<Renderer>().bounds.center)));
        foreach (float z in new[]{-556f,-561.4f,-566f})
        for(float x=-140;x<=-85;x+=2.5f)
        {
            var hits=Physics.RaycastAll(new Vector3(x,60,z),Vector3.down,80,~0,QueryTriggerInteraction.Ignore).OrderBy(hit=>hit.distance);
            detail.Add("ground "+x+","+z+" "+string.Join(" | ",hits.Select(hit=>hit.collider.name+":"+V(hit.point))));
        }
        File.WriteAllLines(Folder+"/geometry.txt",detail);
        if (EditorApplication.isPlaying) return;
        Capture("town", new Vector3(35,160,-615), new Vector3(35,0,-615), true, 130);
        Capture("shelter", new Vector3(-78,40,-590), new Vector3(-106,26,-565));
        var house=objects.FirstOrDefault(t=>t.name=="House_Green_Prefab");
        if(house) Capture("house", house.position+new Vector3(15,12,-15),house.position+Vector3.up*2);
    }
    public static void Capture(string name, Vector3 from, Vector3 to, bool ortho=false, float size=10)
    {
        var go=new GameObject("Evacuation audit camera");
        var cam=go.AddComponent<Camera>(); cam.transform.position=from; cam.transform.LookAt(to);
          cam.useOcclusionCulling=false;
          UnityEngine.Rendering.Universal.UniversalAdditionalCameraData cameraData=UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(cam);
          cameraData.renderPostProcessing=true;
        cam.orthographic=ortho;cam.orthographicSize=size; cam.farClipPlane=2200; cam.nearClipPlane=.1f;
        var rt=new RenderTexture(1280,900,24); var tex=new Texture2D(1280,900,TextureFormat.RGB24,false);
        var old=RenderTexture.active;
        try {cam.targetTexture=rt;cam.Render();RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,900),0,0);tex.Apply();File.WriteAllBytes(Folder+"/"+name+".png",tex.EncodeToPNG());}
        finally {RenderTexture.active=old;Object.DestroyImmediate(go);Object.DestroyImmediate(tex);rt.Release();Object.DestroyImmediate(rt);}
    }
}
