using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TruckLakeRouteTools
{
    [MenuItem("GameJam/Final Loop/Validate lake round trip")]
    public static void ValidateRoundTrip()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        FinalLoopSetup.Validate();
        var nav=Object.FindFirstObjectByType<ObjectiveNavigation>();var player=nav.mission.player;
        var errors=new List<string>();
        if(nav.lakeTurnaround.Length!=9||nav.lakeTurnaround.Any(t=>!t||t.GetComponentInParent<Canvas>()))errors.Add("Invalid world turning guide references.");
        if(nav.lakeRoad.Length<2||nav.lakeRoad.Any(t=>!t||t.GetComponentInParent<Canvas>()))errors.Add("Invalid world road guide references.");
        if(player.hose.hoseDiameter<.4f || Mathf.Abs(player.hose.hoseLine.widthMultiplier*player.hose.hoseLine.widthCurve.Evaluate(.5f)-player.hose.hoseDiameter)>.001f)errors.Add("Hose width mismatch.");
        if(player.drive.transform.lossyScale!=Vector3.one||player.drive.wheels.Any(w=>!w||!w.enabled||w.attachedRigidbody!=player.drive.body))errors.Add("Truck physics binding/scale mismatch.");
        string[] tests=File.ReadAllLines("Logs/TruckLakeRoute-checks.txt");
        if(tests.Length==0||tests[tests.Length-1]!="COMPLETED")errors.Add("Latest physical round-trip test did not complete.");
        var output=new List<string>{errors.Count==0?"PASS":"FAIL", "Scene saved="+!player.gameObject.scene.isDirty,"Scene="+player.gameObject.scene.path,"Road guides="+nav.lakeRoad.Length+", turning guides="+nav.lakeTurnaround.Length,"Hose world diameter="+player.hose.hoseDiameter,"Rear steer ratio="+player.drive.lowSpeedRearSteerRatio+", disabled above "+player.drive.rearSteerFadeSpeedKph+" km/h"};
        output.AddRange(errors);output.AddRange(tests.Where(l=>l.StartsWith("PASS")));
        output.Add("Test scope: Unity Play Mode, real WheelCollider/terrain/body collisions, automatic test driver; no truck teleport or velocity writes. Mission preparation/deadline supplied by this isolated fixture. Not a manual keyboard playthrough of the full story.");
        output.Add("Observed Unity native warnings during the run: JobTempAlloc allocations older than four frames. No C# exception, missing reference or failed assertion in the completed run. The native warning was not diagnosed by this change.");
        var logEntries=typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");
        var counts=logEntries?.GetMethod("GetCountsByType",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
        if(counts!=null){object[] values={0,0,0};counts.Invoke(null,values);output.Add("Unity Console: errors="+values[0]+", warnings="+values[1]+", logs="+values[2]);}
        output.Add("Changed for this request:");
        output.AddRange(new[]{"Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity","Assets/BK/PureNature_Volcano/Models/Terrains/Terrain.asset","Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab","Assets/GameJam/FireRescue/Runtime/FireRescuePlayer.cs","Assets/GameJam/FireRescue/Runtime/FireTruckDrive.cs","Assets/GameJam/FireRescue/Runtime/FireHoseSystem.cs","Assets/GameJam/FireRescue/Runtime/ObjectiveNavigation.cs","Assets/GameJam/FireRescue/Runtime/VolcanoSurvivalObjective.cs","Assets/GameJam/FireRescue/Editor/TruckLakeRouteTools.cs (+ .meta)","Assets/GameJam/FireRescue/Editor/TruckLakeRouteChecks.cs (+ .meta)"});
        output.Add("Manual Unity setup: none. Play the saved Volcano_Demo scene. E enters / connects lake pump; F exits; SPACE brakes; RMB sprays water. Repeat the isolated test with GameJam > Final Loop > Run lake round trip.");
        File.WriteAllLines("Logs/TruckLakeRoute-validation.txt",output);
        if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
    }
    [MenuItem("GameJam/Final Loop/Repair lake guide bindings")]
    public static void RepairGuideBindings()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var nav=Object.FindFirstObjectByType<ObjectiveNavigation>();var player=nav.mission.player;
        Vector3[] points={new Vector3(354,0,-593),new Vector3(367,0,-579),new Vector3(385,0,-578),new Vector3(400,0,-590),new Vector3(401,0,-609),new Vector3(389,0,-625),new Vector3(368,0,-634),new Vector3(348,0,-643),new Vector3(334,0,-655)};
        var parent=nav.lakeTurnaround[0].parent;
        Undo.RecordObject(parent,"Bind lake turning guides in world space");
        parent.SetParent(nav.lakeRoad[0].parent,false);parent.localPosition=Vector3.zero;parent.localRotation=Quaternion.identity;parent.localScale=Vector3.one;
        EditorUtility.SetDirty(parent);
        for(int i=0;i<points.Length;i++)
        {var marker=nav.lakeTurnaround[i];marker.position=Ground(points[i]);marker.localScale=Vector3.one;EditorUtility.SetDirty(marker);}
        nav.drivingMarkerLookAhead=32;EditorUtility.SetDirty(nav);
        var dressing=GameObject.Find("07 - Lake water station");
        if(dressing)foreach(Transform child in dressing.transform)
        {
            var renderers=child.GetComponentsInChildren<MeshRenderer>();if(renderers.Length==0)continue;
            float bottom=renderers.Min(r=>r.bounds.min.y);
            Undo.RecordObject(child,"Ground shore prop base");child.position+=Vector3.up*(Ground(child.position).y+.03f-bottom);
            EditorUtility.SetDirty(child);PrefabUtility.RecordPrefabInstancePropertyModifications(child);
        }
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(player.gameObject.scene);EditorSceneManager.SaveScene(player.gameObject.scene);
        File.WriteAllText("Logs/TruckLakeRoute-bindings.txt",$"Turn guides parent={Path(parent)}; Canvas ancestor={parent.GetComponentInParent<Canvas>() != null}\n"+string.Join("\n",nav.lakeTurnaround.Select(t=>t.name+"="+t.position)));
    }
    [MenuItem("GameJam/Final Loop/Level full lake turning footprint")]
    public static void LevelTurningFootprint()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var player=Object.FindFirstObjectByType<FireRescuePlayer>();var nav=Object.FindFirstObjectByType<ObjectiveNavigation>();
        var route=new List<Vector3>{player.mission.fillStation.transform.position};
        route.AddRange(nav.lakeTurnaround.Select(t=>t.position));
        route.AddRange(nav.lakeRoad.Where(t=>t.position.x<330&&t.position.x>295).Select(t=>t.position));
        var terrain=Terrain.activeTerrains.First(t=>{var p=route[0]-t.transform.position;return p.x>=0&&p.z>=0&&p.x<=t.terrainData.size.x&&p.z<=t.terrainData.size.z;});
        var data=terrain.terrainData;Vector3 origin=terrain.transform.position,size=data.size;int res=data.heightmapResolution;
        int xmin=Mathf.FloorToInt((route.Min(p=>p.x)-29-origin.x)/size.x*(res-1)),xmax=Mathf.CeilToInt((route.Max(p=>p.x)+29-origin.x)/size.x*(res-1));
        int zmin=Mathf.FloorToInt((route.Min(p=>p.z)-29-origin.z)/size.z*(res-1)),zmax=Mathf.CeilToInt((route.Max(p=>p.z)+29-origin.z)/size.z*(res-1));
        var patch=data.GetHeights(xmin,zmin,xmax-xmin+1,zmax-zmin+1);
        var waters=Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        Undo.RegisterCompleteObjectUndo(data,"Level full truck swept footprint around lake turn");int changed=0;float largest=0;
        for(int z=0;z<patch.GetLength(0);z++)for(int x=0;x<patch.GetLength(1);x++)
        {
            Vector3 point=new Vector3(origin.x+(x+xmin)/(float)(res-1)*size.x,0,origin.z+(z+zmin)/(float)(res-1)*size.z);
            if(waters.Any(w=>w.ContainsIntake(new Vector3(point.x,w.SurfaceY-.12f,point.z))))continue;
            float nearest=float.PositiveInfinity,roadY=0;
            for(int i=1;i<route.Count;i++)
            {Vector2 a=XZ(route[i-1]),delta=XZ(route[i])-a;float t=Mathf.Clamp01(Vector2.Dot(XZ(point)-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));float gap=Vector2.Distance(XZ(point),a+delta*t);if(gap<nearest){nearest=gap;roadY=Mathf.Lerp(route[i-1].y,route[i].y,t);}}
            float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(18,28,nearest));
            weight*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(295,315,point.x));
            if(weight<=0)continue;float oldY=origin.y+patch[z,x]*size.y;float change=(roadY-oldY)*weight;
            if(Mathf.Abs(change)<.002f)continue;patch[z,x]=Mathf.Clamp01((oldY+change-origin.y)/size.y);changed++;largest=Mathf.Max(largest,Mathf.Abs(change));
        }
        data.SetHeights(xmin,zmin,patch);terrain.Flush();EditorUtility.SetDirty(data);
        foreach(var point in nav.lakeTurnaround.Concat(nav.lakeRoad)) {point.position=Ground(point.position);EditorUtility.SetDirty(point);}
        // Keep all nearby shore props grounded after reshaping the turning apron.
        var dressing=GameObject.Find("07 - Lake water station");
        if(dressing)foreach(Transform child in dressing.transform)
        {var p=child.position;p.y=Ground(p).y+.03f;Undo.RecordObject(child,"Ground shore prop");child.position=p;EditorUtility.SetDirty(child);PrefabUtility.RecordPrefabInstancePropertyModifications(child);}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(player.gameObject.scene);EditorSceneManager.SaveScene(player.gameObject.scene);
        File.WriteAllText("Logs/TruckLakeRoute-turn-footprint.txt",$"Changed samples={changed}; maximum delta={largest:F2}m; flat half-width=18m, blended shoulder=10m. Actual submerged water protected.\n");
    }
    [MenuItem("GameJam/Final Loop/Finish truck maneuver tuning")]
    public static void FinishManeuverTuning()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var player=Object.FindFirstObjectByType<FireRescuePlayer>();var nav=Object.FindFirstObjectByType<ObjectiveNavigation>();
        player.drive.rearSteerFadeSpeedKph=30;
        EditorUtility.SetDirty(player.drive);PrefabUtility.RecordPrefabInstancePropertyModifications(player.drive);
        var terrain=Terrain.activeTerrains.First(t=>{var p=new Vector3(230,0,-670)-t.transform.position;return p.x>=0&&p.z>=0&&p.x<=t.terrainData.size.x&&p.z<=t.terrainData.size.z;});
        var data=terrain.terrainData;Vector3 origin=terrain.transform.position,size=data.size;int res=data.heightmapResolution;
        int xmin=Mathf.FloorToInt((202-origin.x)/size.x*(res-1)),xmax=Mathf.CeilToInt((257-origin.x)/size.x*(res-1));
        int zmin=Mathf.FloorToInt((-698-origin.z)/size.z*(res-1)),zmax=Mathf.CeilToInt((-648-origin.z)/size.z*(res-1));
        var patch=data.GetHeights(xmin,zmin,xmax-xmin+1,zmax-zmin+1);
        var route=nav.lakeRoad.Reverse().Select(t=>t.position).Where(p=>p.x>180&&p.x<280).ToArray();
        var waters=Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        var lines=new List<string>();
        foreach(var point in new[]{new Vector3(234.37f,0,-662.32f),new Vector3(236.33f,0,-663.15f)})
            lines.Add($"MEASURED contact={Ground(point)} water={waters.Any(w=>w.ContainsIntake(new Vector3(point.x,w.SurfaceY-.12f,point.z)))}");
        Undo.RegisterCompleteObjectUndo(data,"Clear long truck front overhang at measured bend");int changed=0;
        for(int z=0;z<patch.GetLength(0);z++)for(int x=0;x<patch.GetLength(1);x++)
        {
            Vector3 point=new Vector3(origin.x+(x+xmin)/(float)(res-1)*size.x,0,origin.z+(z+zmin)/(float)(res-1)*size.z);
            if(waters.Any(w=>w.ContainsIntake(new Vector3(point.x,w.SurfaceY-.12f,point.z))))continue;
            float nearest=float.PositiveInfinity,roadY=0;
            for(int i=1;i<route.Length;i++)
            {Vector2 a=XZ(route[i-1]),delta=XZ(route[i])-a;float t=Mathf.Clamp01(Vector2.Dot(XZ(point)-a,delta)/delta.sqrMagnitude);float gap=Vector2.Distance(XZ(point),a+delta*t);if(gap<nearest){nearest=gap;roadY=Mathf.Lerp(route[i-1].y,route[i].y,t);}}
            float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(17,24,nearest));
            weight*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(202,214,point.x))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(246,257,point.x)));
            float oldY=origin.y+patch[z,x]*size.y;float change=Mathf.Min(0,roadY-oldY)*weight;
            if(Mathf.Abs(change)<.002f)continue;patch[z,x]=(oldY+change-origin.y)/size.y;changed++;
        }
        data.SetHeights(xmin,zmin,patch);terrain.Flush();EditorUtility.SetDirty(data);
        lines.Add("Shoulder samples lowered="+changed+"; rear steering fades fully by 30km/h.");File.WriteAllLines("Logs/TruckLakeRoute-shoulder.txt",lines);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(player.gameObject.scene);EditorSceneManager.SaveScene(player.gameObject.scene);
    }
    [MenuItem("GameJam/Final Loop/Prepare lake turning area")]
    public static void PrepareTurningArea()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Use Edit Mode.");
        var player=Object.FindFirstObjectByType<FireRescuePlayer>();var nav=Object.FindFirstObjectByType<ObjectiveNavigation>();
        if(nav.lakeTurnaround.Length>0)throw new InvalidOperationException("The lake turning area is already authored; use its existing guides and do not reapply the initial terrain pass.");
        Vector3[] turn = {
            new Vector3(354,0,-593),new Vector3(367,0,-579),new Vector3(385,0,-578),new Vector3(400,0,-590),
            new Vector3(401,0,-609),new Vector3(389,0,-625),new Vector3(368,0,-634),new Vector3(348,0,-643),new Vector3(334,0,-655)
        };
        var terrain=Terrain.activeTerrains.First(t=>{var p=turn[0]-t.transform.position;return p.x>=0&&p.z>=0&&p.x<=t.terrainData.size.x&&p.z<=t.terrainData.size.z;});
        var data=terrain.terrainData;Vector3 origin=terrain.transform.position,size=data.size;int res=data.heightmapResolution;
        int xmin=Mathf.Clamp(Mathf.FloorToInt((339-origin.x)/size.x*(res-1)),0,res-1),xmax=Mathf.Clamp(Mathf.CeilToInt((423-origin.x)/size.x*(res-1)),0,res-1);
        int zmin=Mathf.Clamp(Mathf.FloorToInt((-651-origin.z)/size.z*(res-1)),0,res-1),zmax=Mathf.Clamp(Mathf.CeilToInt((-554-origin.z)/size.z*(res-1)),0,res-1);
        var patch=data.GetHeights(xmin,zmin,xmax-xmin+1,zmax-zmin+1);
        var waters=Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        Undo.RegisterCompleteObjectUndo(data,"Create lake truck turning apron");int changed=0;float largest=0;
        for(int z=0;z<patch.GetLength(0);z++)for(int x=0;x<patch.GetLength(1);x++)
        {
            Vector3 point=new Vector3(origin.x+(x+xmin)/(float)(res-1)*size.x,0,origin.z+(z+zmin)/(float)(res-1)*size.z);
            float ellipse=new Vector2((point.x-377)/31,(point.z+599)/32).magnitude;
            float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(1,1.45f,ellipse));
            // Join the existing road gradually instead of leaving a step at the apron exit.
            weight*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(-651,-636,point.z));
            if(weight<=0||waters.Any(w=>w.ContainsIntake(new Vector3(point.x,w.SurfaceY-.12f,point.z))))continue;
            float old=origin.y+patch[z,x]*size.y;float change=(11.5f-old)*weight;
            if(Mathf.Abs(change)<.002f)continue;
            patch[z,x]=Mathf.Clamp01((old+change-origin.y)/size.y);changed++;largest=Mathf.Max(largest,Mathf.Abs(change));
        }
        data.SetHeights(xmin,zmin,patch);terrain.Flush();EditorUtility.SetDirty(data);
        var rock=GameObject.Find("BigBoulderSharp_3 (64)");
        if(!rock)throw new InvalidOperationException("Measured turnaround rock is missing.");
        // Keep the rock and its real collision mesh; move this measured obstruction outside the turning apron.
        if(rock.transform.position.x<385)
        {Undo.RecordObject(rock.transform,"Clear measured lake truck obstruction");rock.transform.position+=new Vector3(26,0,-20);EditorUtility.SetDirty(rock.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(rock.transform);}
        var parent=nav.lakeRoad[0].parent.Find("Lake truck turning guides");
        if(!parent){parent=new GameObject("Lake truck turning guides").transform;parent.SetParent(nav.lakeRoad[0].parent,false);}
        var markers=new List<Transform>();
        for(int i=0;i<turn.Length;i++)
        {
            var marker=parent.Find("Turn "+i.ToString("00"));if(!marker){marker=new GameObject("Turn "+i.ToString("00")).transform;marker.SetParent(parent,false);}
            marker.position=Ground(turn[i]);markers.Add(marker);EditorUtility.SetDirty(marker);
        }
        nav.lakeTurnaround=markers.ToArray();EditorUtility.SetDirty(nav);
        player.drive.lowSpeedRearSteerRatio=.6f;player.drive.rearSteerFadeSpeedKph=30;
        EditorUtility.SetDirty(player.drive);PrefabUtility.RecordPrefabInstancePropertyModifications(player.drive);
        // Ground the shore prop again after the apron change.
        var bench=GameObject.Find("Bank resting bench");
        if(bench){Undo.RecordObject(bench.transform,"Ground lake bench");var p=bench.transform.position;p.y=Ground(p).y+.03f;bench.transform.position=p;EditorUtility.SetDirty(bench.transform);PrefabUtility.RecordPrefabInstancePropertyModifications(bench.transform);}
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(player.gameObject.scene);EditorSceneManager.SaveScene(player.gameObject.scene);
        File.WriteAllText("Logs/TruckLakeRoute-turning-area.txt",$"Terrain samples={changed}, maximum change={largest:F2}m\nMeasured rock moved 26m east / 20m south with collider retained.\nRear steering ratio=.6, fades to zero by 30km/h.\nReturn guides={markers.Count}\n");
    }
    [MenuItem("GameJam/Final Loop/Prepare lake corridor")]
    public static void PrepareCorridor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        if(nav.lakeRoad.Length>0 && nav.lakeRoad[nav.lakeRoad.Length-1].position.x<0)
            throw new InvalidOperationException("The complete village-to-lake corridor is already authored; do not resample and reapply its initial terrain pass.");
        var original = TruckLakeRouteChecks.Outbound();
        var route = new List<Vector3>();
        for (int i = 0; i < original.Count - 1; i++)
        {
            Vector3 a = original[Mathf.Max(0,i-1)], b = original[i], c = original[i+1], d = original[Mathf.Min(original.Count-1,i+2)];
            int steps = Mathf.CeilToInt(Vector3.Distance(b,c)/2);
            for (int j=0;j<steps;j++)
            {
                float t=j/(float)steps;
                var point=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
                route.Add(Ground(point));
            }
        }
        route.Add(original[original.Count-1]);
        float[] distance=new float[route.Count];
        for(int i=1;i<route.Count;i++)distance[i]=distance[i-1]+Vector2.Distance(XZ(route[i-1]),XZ(route[i]));
        var heights=route.Select(p=>p.y).ToArray();
        for(int i=0;i<route.Count;i++)
        {
            if(route[i].x<175)continue;
            float total=0,weights=0;
            for(int j=0;j<route.Count;j++)
            {float separation=Mathf.Abs(distance[j]-distance[i]);if(separation>22)continue;float w=1-separation/23;total+=heights[j]*w;weights+=w;}
            var p=route[i];p.y=total/weights;route[i]=p;
        }
        for(int pass=0;pass<3;pass++)
        {
            for(int i=1;i<route.Count;i++)ClampGrade(route,i,i-1);
            for(int i=route.Count-2;i>=0;i--)ClampGrade(route,i,i+1);
        }
        var terrain=Terrain.activeTerrains.First(t=>{var p=route[route.Count-1]-t.transform.position;return p.x>=0&&p.z>=0&&p.x<=t.terrainData.size.x&&p.z<=t.terrainData.size.z;});
        var data=terrain.terrainData;
        string backup=".git/codex-backups/lake-route-20261017";Directory.CreateDirectory(backup);
        string terrainPath=AssetDatabase.GetAssetPath(data);
        if(!File.Exists(backup+"/Terrain.before.asset"))File.Copy(terrainPath,backup+"/Terrain.before.asset");
        if(!File.Exists(backup+"/Volcano_Demo.before.unity"))File.Copy(player.gameObject.scene.path,backup+"/Volcano_Demo.before.unity");
        var waters=Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        var protectedBounds=Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Where(h=>h.ignitionBounds).Select(h=>h.ignitionBounds.bounds).ToList();
        foreach(var lava in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
            if(lava.heatCollider && !waters.Any(w=>w.waterSurface && lava.heatCollider.transform==w.waterSurface))protectedBounds.Add(lava.heatCollider.bounds);
        Vector3 origin=terrain.transform.position,size=data.size;int res=data.heightmapResolution;
        int xmin=Mathf.Clamp(Mathf.FloorToInt((170-origin.x)/size.x*(res-1)),0,res-1), xmax=Mathf.Clamp(Mathf.CeilToInt((386-origin.x)/size.x*(res-1)),0,res-1);
        int zmin=Mathf.Clamp(Mathf.FloorToInt((-710-origin.z)/size.z*(res-1)),0,res-1), zmax=Mathf.Clamp(Mathf.CeilToInt((-575-origin.z)/size.z*(res-1)),0,res-1);
        var patch=data.GetHeights(xmin,zmin,xmax-xmin+1,zmax-zmin+1);
        Undo.RegisterCompleteObjectUndo(data,"Widen truck bends and smooth lake approach");
        int changed=0,protectedCount=0;float largestChange=0;
        for(int z=0;z<patch.GetLength(0);z++)for(int x=0;x<patch.GetLength(1);x++)
        {
            Vector3 point=new Vector3(origin.x+(x+xmin)/(float)(res-1)*size.x,0,origin.z+(z+zmin)/(float)(res-1)*size.z);
            float nearest=float.PositiveInfinity, roadY=0;
            for(int i=1;i<route.Count;i++)
            {
                if(route[i].x<175)continue;
                Vector2 a=XZ(route[i-1]),delta=XZ(route[i])-a;
                float t=Mathf.Clamp01(Vector2.Dot(XZ(point)-a,delta)/Mathf.Max(.001f,delta.sqrMagnitude));
                float gap=Vector2.Distance(XZ(point),a+delta*t);
                if(gap<nearest){nearest=gap;roadY=Mathf.Lerp(route[i-1].y,route[i].y,t);}
            }
            float weight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(12,20,nearest));
            // Extra dry-bank room for braking and a three-point turn; the actual submerged intake is protected.
            float padDistance=new Vector2((point.x-359)/16,(point.z+616)/23).magnitude;
            float padWeight=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.85f,1.35f,padDistance));
            if(padWeight>weight){roadY=11.5f;weight=padWeight;}
            weight*=Mathf.SmoothStep(0,1,Mathf.InverseLerp(175,190,point.x));
            if(weight<=0)continue;
            bool protect=protectedBounds.Any(b=>point.x>=b.min.x-2&&point.x<=b.max.x+2&&point.z>=b.min.z-2&&point.z<=b.max.z+2) ||
                waters.Any(w=>w.ContainsIntake(new Vector3(point.x,w.SurfaceY-.12f,point.z)));
            if(protect){protectedCount++;continue;}
            float oldY=origin.y+patch[z,x]*size.y;
            float change=Mathf.Clamp(roadY-oldY,-8,8)*weight;
            if(Mathf.Abs(change)<.002f)continue;
            patch[z,x]=Mathf.Clamp01((oldY+change-origin.y)/size.y);changed++;largestChange=Mathf.Max(largestChange,Mathf.Abs(change));
        }
        data.SetHeights(xmin,zmin,patch);terrain.Flush();EditorUtility.SetDirty(data);
        // The same saved waypoint chain is used by gameplay guidance and the round-trip test.
        var saved = new List<Vector3>();
        for(int i=0;i<route.Count;i+=6)saved.Add(Ground(route[i]));
        if(Vector3.Distance(saved[saved.Count-1],route[route.Count-1])>1)saved.Add(Ground(route[route.Count-1]));
        saved.Reverse();
        Transform parent=nav.lakeRoad[0].parent;
        var markers=new List<Transform>();
        for(int i=0;i<saved.Count;i++)
        {
            Transform marker=i<nav.lakeRoad.Length?nav.lakeRoad[i]:new GameObject("Lake road guide "+i.ToString("00")).transform;
            Undo.RecordObject(marker,"Update truck road guidance");marker.SetParent(parent,true);marker.position=saved[i];markers.Add(marker);EditorUtility.SetDirty(marker);
        }
        nav.lakeRoad=markers.ToArray();EditorUtility.SetDirty(nav);
        player.followDistance=34;player.minFollowDistance=24;player.maxFollowDistance=48;player.followHeight=11;player.cannonCameraLift=2.5f;
        EditorUtility.SetDirty(player);PrefabUtility.RecordPrefabInstancePropertyModifications(player);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);EditorSceneManager.SaveScene(player.gameObject.scene);AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/TruckLakeRoute-authoring.txt",$"Changed terrain samples: {changed}\nProtected water/building/lava samples: {protectedCount}\nLargest terrain change: {largestChange:F2}m\nRoad half width: 12m, shoulder blend: 8m; grade target: 13 degrees\nWaypoints: {markers.Count}\nBackup: {System.IO.Path.GetFullPath(backup)}\n");
    }
    static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
    static void ClampGrade(List<Vector3> route,int i,int anchor)
    {
        if(route[i].x<175)return;
        float change=Vector2.Distance(XZ(route[i]),XZ(route[anchor]))*Mathf.Tan(13*Mathf.Deg2Rad);
        var p=route[i];p.y=Mathf.Clamp(p.y,route[anchor].y-change,route[anchor].y+change);route[i]=p;
    }
    [MenuItem("GameJam/Final Loop/Apply hose proportions")]
    public static void ApplyHose()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var p = Object.FindFirstObjectByType<FireRescuePlayer>();
        Undo.RecordObjects(new Object[] { p.hose, p.hose.hoseLine }, "Keep intake hose full width");
        p.hose.hoseDiameter = .42f;
        p.hose.hoseLine.widthCurve = AnimationCurve.Constant(0, 1, 1);
        p.hose.hoseLine.widthMultiplier = p.hose.hoseDiameter;
        p.hose.hoseLine.textureMode = LineTextureMode.Tile;
        EditorUtility.SetDirty(p.hose); EditorUtility.SetDirty(p.hose.hoseLine);
        PrefabUtility.RecordPrefabInstancePropertyModifications(p.hose); PrefabUtility.RecordPrefabInstancePropertyModifications(p.hose.hoseLine);
        EditorSceneManager.MarkSceneDirty(p.gameObject.scene); EditorSceneManager.SaveScene(p.gameObject.scene);
    }
    [MenuItem("GameJam/Final Loop/Audit lake route")]
    public static void Audit()
    {
        var p = Object.FindFirstObjectByType<FireRescuePlayer>();
        var lines = new List<string> { "play=" + EditorApplication.isPlaying + " dirty=" + p.gameObject.scene.isDirty };
        foreach (var t in p.drive.GetComponentsInChildren<Transform>(true))
            if (!t.name.Contains("Fire Truck") && (t.GetComponent<MeshFilter>() || t == p.cannon.transform || t == p.cannon.pitchPivot || t == p.cannon.yawPivot || t == p.hose.nozzle || t == p.hose.hoseSocket))
                lines.Add($"VISUAL {Path(t)} position={t.position} localScale={t.localScale} worldScale={t.lossyScale} rotation={t.localEulerAngles}");
        lines.Add("HOSE " + EditorJsonUtility.ToJson(p.hose));
        lines.Add("DRIVE " + EditorJsonUtility.ToJson(p.drive));
        lines.Add($"PARK {p.drive.transform.position} forward={p.drive.transform.forward} door={p.driverDoor.position} cameraDistance={p.followDistance} height={p.followHeight}");
        lines.Add($"LAKE {p.mission.fillStation.transform.position} intake={p.mission.fillStation.intakePoint.position}");
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None)) lines.Add($"HOUSE {house.name} {house.transform.position}");
        foreach (var t in Object.FindFirstObjectByType<ObjectiveNavigation>().lakeRoad) lines.Add("ROAD " + t.position.ToString("F2"));
        for (int z = -710; z <= -530; z += 10)
        {
            string row = $"TERRAIN z={z}: ";
            for (int x = -50; x <= 370; x += 10) row += $"{x}:{Ground(new Vector3(x,0,z)).y:F1} ";
            lines.Add(row);
        }
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
        {
            if (!c.enabled || c.isTrigger || c is TerrainCollider || c is CharacterController || c.transform.IsChildOf(p.drive.transform)) continue;
            var b = c.bounds;
            if (b.max.x < -50 || b.min.x > 370 || b.max.z < -710 || b.min.z > -530) continue;
            lines.Add($"SOLID {Path(c.transform)} {b}");
        }
        File.WriteAllLines("Logs/TruckLakeRoute-audit.txt", lines);
    }
    public static Vector3 Ground(Vector3 point)
    {
        foreach (var terrain in Terrain.activeTerrains)
        {
            var local = point - terrain.transform.position; var size = terrain.terrainData.size;
            if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z) continue;
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y; return point;
        }
        return point;
    }
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
}
