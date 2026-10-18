using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LakeFenceTools
{
    public const string RootName = "Lake Shore Fence";
    public const string PrefabPath = "Assets/GameJam/FireRescue/Prefabs/Lake Shore Fence.prefab";
    [Serializable] public sealed class SurveyData
    {
        public Vector3 center, parking, intake;
        public float waterY;
        public List<Vector3> shoreline = new List<Vector3>(), fence = new List<Vector3>();
        public List<string> samples = new List<string>();
    }
    [MenuItem("GameJam/Final Loop/Survey lake fence")]
    public static void Survey()
    {
        var station=Object.FindFirstObjectByType<LakeFillStation>();var water=station.water;
        var surfaceMesh=water.waterSurface.GetComponent<MeshFilter>().sharedMesh;
        var data=new SurveyData { center=water.waterSurface.TransformPoint(surfaceMesh.bounds.center),parking=station.transform.position,intake=station.intakePoint.position,waterY=water.SurfaceY };
        data.center.y=water.SurfaceY-.025f;
        Directory.CreateDirectory("Logs/LakeFence");
        File.WriteAllText("Logs/LakeFence/volume.txt",EditorJsonUtility.ToJson(water)+"\n"+EditorJsonUtility.ToJson(water.volume)+"\ncenter="+data.center+" ground="+TruckLakeRouteTools.Ground(data.center)+" water="+water.SurfaceY+" active="+water.isActiveAndEnabled);
        bool Wet(Vector3 point)
        {
            var local=water.waterSurface.InverseTransformPoint(point)-surfaceMesh.bounds.center;
            return Mathf.Abs(local.x)<=surfaceMesh.bounds.extents.x&&Mathf.Abs(local.z)<=surfaceMesh.bounds.extents.z&&TruckLakeRouteTools.Ground(point).y<water.SurfaceY-.02f;
        }
        if(!Wet(data.center)) throw new InvalidOperationException("Lake centre must be wet for a shoreline contour; see Logs/LakeFence/volume.txt.");
        var towards=Vector3.ProjectOnPlane(data.intake-data.center,Vector3.up).normalized;
        for(int i=0;i<360;i++)
        {
            var direction=Quaternion.AngleAxis(i,Vector3.up)*towards;
            float last=0;
            for(float r=.25f;r<140;r+=.25f) { if(!Wet(data.center+direction*r))break;last=r; }
            if(last<5)throw new InvalidOperationException("Broken shoreline ray "+i);
            var shore=data.center+direction*last;data.shoreline.Add(shore);
            var dry=shore+direction*1.8f;dry=TruckLakeRouteTools.Ground(dry);
            for(int n=0;n<240&&dry.y<water.SurfaceY+.02f;n++)dry=TruckLakeRouteTools.Ground(dry+direction*.25f);
            if(dry.y<water.SurfaceY+.02f)throw new InvalidOperationException("Cannot find dry fence bank for ray "+i);
            data.fence.Add(dry);
        }
        foreach(var mesh in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            if(!mesh.sharedMesh||Vector3.Distance(mesh.transform.position,data.center)>130)continue;
            if(mesh.sharedMesh.name.IndexOf("fence",StringComparison.OrdinalIgnoreCase)<0)continue;
            data.samples.Add(mesh.name+" position="+mesh.transform.position+" scale="+mesh.transform.lossyScale+" materials="+string.Join(",",mesh.GetComponent<Renderer>().sharedMaterials.Select(m=>m?AssetDatabase.GetAssetPath(m):"null")));
        }
        Directory.CreateDirectory("Logs/LakeFence");File.WriteAllText("Logs/LakeFence/survey.json",JsonUtility.ToJson(data,true));
    }

    [MenuItem("GameJam/Final Loop/Build lake shore fence")]
    public static void Build()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!=TownDressingSetup.ScenePath)throw new InvalidOperationException("Open Volcano_Demo.");
        var existing=GameObject.Find(RootName);
        if(existing&&PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(existing)!=PrefabPath)throw new InvalidOperationException("Unrecognized existing fence; preserve it.");
        Directory.CreateDirectory("Library/LakeFenceReview");
        if(!File.Exists("Library/LakeFenceReview/before-unsaved.unity"))EditorSceneManager.SaveScene(scene,"Library/LakeFenceReview/before-unsaved.unity",true);
        if(!File.Exists("Library/LakeFenceReview/before-saved.unity"))File.Copy(scene.path,"Library/LakeFenceReview/before-saved.unity",true);
        Survey();var data=JsonUtility.FromJson<SurveyData>(File.ReadAllText("Logs/LakeFence/survey.json"));
        var station=Object.FindFirstObjectByType<LakeFillStation>();var water=station.water;
        Undo.RecordObjects(new Object[]{water,water.volume,water.volume.transform},"Align lake intake volume to water surface");
        float bottom=water.volume.center.y-water.volume.size.y*.5f;
        var surfaceBounds=water.waterSurface.GetComponent<MeshFilter>().sharedMesh.bounds;
        var volumePosition=water.volume.transform.position;volumePosition.x=data.center.x;volumePosition.z=data.center.z;water.volume.transform.position=volumePosition;
        water.volume.transform.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(water.waterSurface.forward,Vector3.up),Vector3.up);
        var surfacePoint=water.volume.transform.position;surfacePoint.y=water.SurfaceY;
        float top=water.volume.transform.InverseTransformPoint(surfacePoint).y;
        var size=water.volume.size;size.y=top-bottom;
        size.x=surfaceBounds.size.x*Mathf.Abs(water.waterSurface.lossyScale.x/water.volume.transform.lossyScale.x);
        size.z=surfaceBounds.size.z*Mathf.Abs(water.waterSurface.lossyScale.z/water.volume.transform.lossyScale.z);water.volume.size=size;
        var centre=Vector3.zero;centre.y=(top+bottom)*.5f;water.volume.center=centre;water.surfaceY=water.SurfaceY;
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>("Assets/GameJam/TownDressing/Meshes/fence-v1.asset");
        var sample=Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None).First(f=>f.sharedMesh==mesh);
        var materials=sample.GetComponent<Renderer>().sharedMaterials;
        var preview=EditorSceneManager.NewPreviewScene();var root=new GameObject(RootName);SceneManager.MoveGameObjectToScene(root,preview);
        root.transform.position=new Vector3(data.center.x,0,data.center.z);
        try
        {
            var contour=data.fence;var lengths=new float[contour.Count+1];
            for(int i=1;i<=contour.Count;i++)lengths[i]=lengths[i-1]+Flat(contour[i-1],contour[i%contour.Count]);
            Vector3 Point(float along)
            {
                along=Mathf.Repeat(along,lengths[lengths.Length-1]);int i=Array.FindLastIndex(lengths,l=>l<=along);i=Mathf.Min(i,contour.Count-1);
                return TruckLakeRouteTools.Ground(Vector3.Lerp(contour[i],contour[(i+1)%contour.Count],(along-lengths[i])/Mathf.Max(.001f,lengths[i+1]-lengths[i])));
            }
            float start=.7f,finish=lengths[lengths.Length-1]-.7f;int panels=0;
            while(start<finish-.01f)
            {
                float remaining=finish-start;
                float step=remaining>10.8f&&remaining<16.2f?remaining*.5f:Mathf.Min(10.8f,remaining);
                float next=start+step;var a=Point(start);var b=Point(next);
                while((Mathf.Abs(a.y-b.y)>2.5f||Flat(a,b)<(next-start)*.9f)&&next-start>5.4f){next=(start+next)*.5f;b=Point(next);}
                var mid=TruckLakeRouteTools.Ground((a+b)*.5f);float low=float.PositiveInfinity,high=float.NegativeInfinity;
                for(int n=0;n<=8;n++){float y=TruckLakeRouteTools.Ground(Vector3.Lerp(a,b,n/8f)).y;low=Mathf.Min(low,y);high=Mathf.Max(high,y);}
                var panel=new GameObject("Shore fence "+(++panels).ToString("000"));panel.transform.SetParent(root.transform);
                panel.transform.position=new Vector3(mid.x,low-.08f,mid.z);
                Vector3 tangent=Vector3.ProjectOnPlane(b-a,Vector3.up).normalized;
                panel.transform.rotation=Quaternion.LookRotation(Vector3.Cross(tangent,Vector3.up),Vector3.up);
                float span=Flat(a,b);panel.transform.localScale=new Vector3(span/mesh.bounds.size.x,4.58f,2.5f);
                panel.AddComponent<MeshFilter>().sharedMesh=mesh;panel.AddComponent<MeshRenderer>().sharedMaterials=materials;
                var box=panel.AddComponent<BoxCollider>();float bottomY=low-.5f,topY=high+5.725f;
                box.center=new Vector3(0,((topY+bottomY)*.5f-panel.transform.position.y)/4.58f,0);
                box.size=new Vector3(mesh.bounds.size.x+.1f/panel.transform.localScale.x,(topY-bottomY)/4.58f,.36f);
                GameObjectUtility.SetStaticEditorFlags(panel,StaticEditorFlags.BatchingStatic|StaticEditorFlags.OccluderStatic|StaticEditorFlags.OccludeeStatic);
                start=next;
            }
            var gate=Point(0);var outward=Vector3.ProjectOnPlane(gate-data.center,Vector3.up).normalized;
            var passageGO=new GameObject("Intake hose opening");passageGO.transform.SetParent(root.transform);passageGO.transform.position=gate;
            passageGO.transform.rotation=Quaternion.LookRotation(-outward,Vector3.up);
            var passage=passageGO.AddComponent<LakeHosePassage>();
            Transform Marker(string name,Vector3 p){var g=new GameObject(name);g.transform.SetParent(passageGO.transform);g.transform.position=p;return g.transform;}
            passage.dryGuide=Marker("Dry bank hose guide",TruckLakeRouteTools.Ground(gate+outward*1.8f)+Vector3.up*.16f);
            passage.wetGuide=Marker("Water side hose guide",TruckLakeRouteTools.Ground(gate-outward*1.8f)+Vector3.up*.16f);
            passage.intakeTarget=Marker("Existing submerged intake target",data.intake);
            var asset=PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
            var instance=existing?existing:(GameObject)PrefabUtility.InstantiatePrefab(asset,scene);
            Undo.RecordObject(instance.transform,"Align fence to visible lake");instance.transform.position=root.transform.position;
            if(!existing)Undo.RegisterCreatedObjectUndo(instance,"Fence lake shoreline");
            // Replace only the user's two sample lake sections; courtyard fences remain intact.
            foreach(var fence in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                if(fence.sharedMesh==mesh&&fence.gameObject.scene==scene&&!fence.transform.IsChildOf(instance.transform)&&Flat(fence.transform.position,data.center)<90)
                    Undo.DestroyObjectImmediate(fence.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene,"Library/LakeFenceReview/after-unsaved.unity",true);
            PersistOnlyLakeRecords(scene.path,PrefabPath);
            Physics.SyncTransforms();
            File.WriteAllText("Logs/LakeFence/authoring.txt",$"COMPLETED {DateTime.UtcNow:O}\nPanels={panels}; opening=1.4m visual, approximately1.3m clear collision; gate={gate:F3}; outward={outward:F4}\nWater surface={water.SurfaceY}; volume top aligned, original bottom preserved.\nUnrelated unsaved scene state preserved; backups Library/LakeFenceReview/.\n");
        }
        finally{EditorSceneManager.ClosePreviewScene(preview);}
    }
    static float Flat(Vector3 a,Vector3 b)=>Vector3.ProjectOnPlane(a-b,Vector3.up).magnitude;

    [MenuItem("GameJam/Final Loop/Persist lake shore fence")]
    public static void Persist()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var scene=SceneManager.GetActiveScene();var root=GameObject.Find(RootName);
        if(!root||scene.path!=TownDressingSetup.ScenePath)throw new InvalidOperationException("Missing authored fence.");
        EditorSceneManager.SaveScene(scene,"Library/LakeFenceReview/after-unsaved.unity",true);
        PersistOnlyLakeRecords(scene.path,PrefabPath);
        var passage=root.GetComponentInChildren<LakeHosePassage>();
        File.WriteAllText("Logs/LakeFence/authoring.txt",$"COMPLETED {DateTime.UtcNow:O}\nPanels={root.GetComponentsInChildren<BoxCollider>().Length}; gate={passage.transform.position:F3}; visual gap1.4m, collision gap about1.3m.\nOnly fence root and related water records persisted. Unrelated unsaved scene state remains in the editor. Backups Library/LakeFenceReview/.\n");
    }

    static void PersistOnlyLakeRecords(string path,string prefab)
    {
        var original=File.ReadAllText(path);var snapshot=File.ReadAllText("Library/LakeFenceReview/after-unsaved.unity");
        var pattern=@"(?ms)^--- !u!(?<type>\d+) &(?<id>-?\d+)[^\r\n]*\r?\n.*?(?=^--- !u!|\z)";
        var oldBlocks=Regex.Matches(original,pattern).Cast<Match>().ToDictionary(m=>m.Groups["id"].Value);
        var newBlocks=Regex.Matches(snapshot,pattern).Cast<Match>().ToDictionary(m=>m.Groups["id"].Value);
        string Ref(string value,string field)=>Regex.Match(value,@"(?m)^  "+field+@": \{fileID: (-?\d+)").Groups[1].Value;
        var waterScript=newBlocks.Values.Single(m=>m.Value.Contains("guid: 6bf72603636902849ab661abd1a95cbe"));
        string volumeId=Ref(waterScript.Value,"volume");
        var volumeGO=newBlocks[Ref(newBlocks[volumeId].Value,"m_GameObject")];
        string volumeTransform=Regex.Match(volumeGO.Value,@"- component: \{fileID: (\d+)").Groups[1].Value;
        var replace=new HashSet<string>{waterScript.Groups["id"].Value,volumeId,volumeTransform};
        var surface=newBlocks[Ref(waterScript.Value,"waterSurface")];
        replace.Add(Ref(surface.Value,"m_PrefabInstance"));
        string guid=AssetDatabase.AssetPathToGUID(prefab);
        var instance=newBlocks.Values.Single(m=>m.Groups["type"].Value=="1001"&&m.Value.Contains("guid: "+guid));
        string id=instance.Groups["id"].Value;
        var stripped=newBlocks.Values.SingleOrDefault(m=>m.Groups["type"].Value=="4"&&Ref(m.Value,"m_PrefabInstance")==id);
        // Unity 6 may reference a root prefab instance directly without a stripped Transform.
        string transformId=stripped!=null?stripped.Groups["id"].Value:id;
        bool exists=oldBlocks.ContainsKey(id);
        if(exists&&!oldBlocks[id].Value.Contains("guid: "+guid))throw new InvalidOperationException("Fence IDs collide with saved scene.");
        if(exists)replace.Add(id);
        if(stripped!=null&&oldBlocks.ContainsKey(transformId))replace.Add(transformId);
        foreach(string key in replace)if(!oldBlocks.ContainsKey(key))throw new InvalidOperationException("Missing original lake record "+key);
        string output=Regex.Replace(original,pattern,m=>replace.Contains(m.Groups["id"].Value)?newBlocks[m.Groups["id"].Value].Value:m.Value);
        int roots=output.IndexOf("--- !u!1660057539 ",StringComparison.Ordinal);
        if(roots<0)throw new InvalidOperationException("Missing SceneRoots.");
        if(!exists)
        {
            output=output.Insert(roots,instance.Value+(stripped!=null?stripped.Value:""));
            output=output.TrimEnd()+"\n  - {fileID: "+transformId+"}\n";
        }
        File.WriteAllText(path,output,new UTF8Encoding(false));
    }
}
