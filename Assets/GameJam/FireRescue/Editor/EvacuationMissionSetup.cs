using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using GameJam.FireRescue;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class EvacuationMissionSetup
{
    const string Folder = "Assets/GameJam/FireRescue/Evacuation";
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    [MenuItem("GameJam/Evacuation/Set up seven residents")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring evacuation.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
        var existing = Object.FindFirstObjectByType<EvacuationMission>();
        if (existing) { ValidateAndSave(); return; }
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(EvacuationSceneAudit.Folder);
        // Preserve unsaved authored scene edits before adding the mission.
        EditorSceneManager.SaveScene(scene);
        File.Copy(ScenePath, EvacuationSceneAudit.Folder + "/before-evacuation.unity", true);
        AssetDatabase.Refresh();
        var controller = CreateController();
        var root = new GameObject("Civilian Evacuation Mission");
        var mission = root.AddComponent<EvacuationMission>();
        mission.player = Object.FindFirstObjectByType<FireRescuePlayer>();
        mission.eruption = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        if (!mission.player || !mission.eruption) throw new InvalidOperationException("Player or volcano sequence missing.");
        mission.eruption.evacuation = mission;
        mission.eruption.autoEruptAfter = 15f;
        mission.voice = root.AddComponent<AudioSource>();
        mission.voice.playOnAwake = false; mission.voice.spatialBlend = 0; mission.voice.volume = .9f;
        mission.shoutClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/GameJam/FireRescue/Audio/EveryoneOutside.wav");
        var shelter = new GameObject("Shelter Arrival Points").transform; shelter.SetParent(root.transform);
        mission.shelterApproach = Point(shelter,"Shelter approach",Ground(new Vector3(-100,0,-561.4f)));
        mission.shelterEntrance = Point(shelter,"Shelter threshold",Ground(new Vector3(-111,0,-562f)));
        var slots = new Transform[7];
        for(int i=0;i<7;i++)
        {
            slots[i]=Point(shelter,"Safe place "+(i+1),Ground(new Vector3(-119-(i/3)*2,0,-564+(i%3)*1.65f)));
            slots[i].rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(mission.shelterEntrance.position-slots[i].position,Vector3.up));
        }
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).Where(h=>h.name.StartsWith("House_Green")).OrderBy(h=>h.name,StringComparer.Ordinal).ToArray();
        if(houses.Length!=5) throw new InvalidOperationException("Expected the five authored village houses, found "+houses.Length);
        string[] names={"Deniz","Elif","Murat","Zeynep","Can","Aylin","Kemal"};
        string[] prefabs={"01m_01","01f_01","01m_02","01f_02","02m_01","02f_02","01m_03"};
        int[] counts={2,1,1,2,1};
        var residents = new List<EvacuationResident>();
        for(int h=0;h<houses.Length;h++)
        {
            var house=houses[h].transform;
            var leaf=house.GetComponentsInChildren<Transform>(true).First(t=>t.name=="Door04_pr");
            // The model's own pivot is already on the left hinge.
            leaf.localRotation=Quaternion.Euler(0,90,0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(leaf);
            var door=leaf.gameObject.GetComponent<EvacuationDoor>();if(!door)door=leaf.gameObject.AddComponent<EvacuationDoor>();
            door.hinge=leaf;door.openAngle=-105;
            var modifier=leaf.GetComponent<NavMeshModifier>();if(!modifier)modifier=leaf.gameObject.AddComponent<NavMeshModifier>();
            modifier.ignoreFromBuild=true;
            ConfigureDoor(door);
            var exit=Point(root.transform,"House "+(h+1)+" exit",house.TransformPoint(new Vector3(-.27f,.46f,7.7f)));
            exit.position=Ground(exit.position);
            for(int j=0;j<counts[h];j++)
            {
                int i=residents.Count;
                var npcRoot=new GameObject("Resident "+(i+1)+" - "+names[i]);npcRoot.transform.SetParent(root.transform);
                npcRoot.transform.position=house.TransformPoint(new Vector3(-.35f+j*.65f,.48f,4.7f-j*.25f));
                npcRoot.transform.rotation=house.rotation;
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_"+prefabs[i]+".prefab");
                if(!prefab)throw new InvalidOperationException("Missing resident prefab: "+prefabs[i]);
                var model=(GameObject)PrefabUtility.InstantiatePrefab(prefab,npcRoot.transform);
                model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;model.transform.localScale=Vector3.one;
                var animator=model.GetComponentInChildren<Animator>();
                if(!animator || !animator.avatar || !animator.avatar.isHuman || !animator.avatar.isValid)throw new InvalidOperationException("Resident humanoid avatar invalid: "+prefabs[i]);
                animator.runtimeAnimatorController=controller;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                PrefabUtility.RecordPrefabInstancePropertyModifications(animator);
                var agent=npcRoot.AddComponent<NavMeshAgent>();agent.radius=.32f;agent.height=1.8f;agent.baseOffset=0;agent.speed=5.8f;agent.acceleration=16;agent.angularSpeed=540;agent.stoppingDistance=.3f;agent.autoRepath=true;agent.avoidancePriority=35+i*4;agent.obstacleAvoidanceType=ObstacleAvoidanceType.HighQualityObstacleAvoidance;
                var resident=npcRoot.AddComponent<EvacuationResident>();resident.mission=mission;resident.animator=animator;resident.door=door;resident.houseExit=exit;resident.safePlace=slots[i];resident.formationIndex=i;resident.reactionSeconds=1.25f+i*.17f;
                var capsule=npcRoot.AddComponent<CapsuleCollider>();capsule.center=new Vector3(0,.9f,0);capsule.radius=.28f;capsule.height=1.8f;capsule.isTrigger=true;
                residents.Add(resident);
            }
        }
        mission.residents=residents.ToArray();
        // Low warm light gives the already-authored cave a readable destination.
        var lamp=new GameObject("Shelter safety light");lamp.transform.SetParent(shelter);lamp.transform.position=new Vector3(-118,5,-562);
        var light=lamp.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.76f,.43f);light.range=16;light.intensity=4;light.shadows=LightShadows.None;
        var surface=root.AddComponent<NavMeshSurface>();surface.collectObjects=CollectObjects.Volume;surface.center=new Vector3(110,15,-600);surface.size=new Vector3(570,100,260);surface.useGeometry=NavMeshCollectGeometry.PhysicsColliders;surface.overrideVoxelSize=true;surface.voxelSize=.14f;
        RebuildNavigation();
        ValidateAndSave();
    }
    static Transform Point(Transform parent,string name,Vector3 p)
    {var t=new GameObject(name).transform;t.SetParent(parent);t.position=p;return t;}
    static Vector3 Ground(Vector3 point)
    {
        var terrain=Object.FindFirstObjectByType<Terrain>();
        if(terrain)point.y=terrain.SampleHeight(point)+terrain.transform.position.y+.04f;
        return point;
    }
    static AnimatorController CreateController()
    {
        string path=Folder+"/CivilianEvacuation.controller";
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if(controller)return controller;
        var clips=new[]{CopyClip("ıdle.fbx","Idle"),CopyClip("walk.fbx","Walk"),CopyClip("run.fbx","Run"),CopyClip("Terrified.fbx","Fear")};
        controller=AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed",AnimatorControllerParameterType.Float);controller.AddParameter("Panic",AnimatorControllerParameterType.Bool);
        var sm=controller.layers[0].stateMachine;
        var tree=new BlendTree{name="Idle Walk Run",blendType=BlendTreeType.Simple1D,blendParameter="Speed",useAutomaticThresholds=false};
        AssetDatabase.AddObjectToAsset(tree,controller);tree.AddChild(clips[0],0);tree.AddChild(clips[1],2);tree.AddChild(clips[2],5.8f);
        var locomotion=sm.AddState("Locomotion");locomotion.motion=tree;sm.defaultState=locomotion;
        var panic=sm.AddState("Terrified");panic.motion=clips[3];panic.cycleOffset=.075f;
        var enter=locomotion.AddTransition(panic);enter.hasExitTime=false;enter.duration=.16f;enter.AddCondition(AnimatorConditionMode.If,0,"Panic");
        var leave=panic.AddTransition(locomotion);leave.hasExitTime=false;leave.duration=.2f;leave.AddCondition(AnimatorConditionMode.IfNot,0,"Panic");
        EditorUtility.SetDirty(controller);AssetDatabase.SaveAssets();return controller;
    }
    static AnimationClip CopyClip(string model,string name)
    {
        string path=Folder+"/"+name+".anim";
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);if(existing)return existing;
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Animation/"+model).OfType<AnimationClip>().First(c=>!c.name.StartsWith("__"));
        var clip=Object.Instantiate(source);clip.name=name;clip.wrapMode=WrapMode.Loop;
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=true;settings.loopBlend=true;settings.loopBlendOrientation=true;settings.loopBlendPositionY=true;settings.loopBlendPositionXZ=true;settings.keepOriginalOrientation=true;settings.keepOriginalPositionXZ=true;settings.heightFromFeet=true;
        AnimationUtility.SetAnimationClipSettings(clip,settings);AssetDatabase.CreateAsset(clip,path);return clip;
    }
    static void ConfigureDoor(EvacuationDoor door)
    {
        // Moving leaves cannot remain in the house's static render batch.
        foreach(var child in door.GetComponentsInChildren<Transform>(true))
        {GameObjectUtility.SetStaticEditorFlags(child.gameObject,0);PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);}
        var obstacle=door.GetComponent<NavMeshObstacle>();if(!obstacle)obstacle=door.gameObject.AddComponent<NavMeshObstacle>();
        var mesh=door.GetComponent<MeshFilter>().sharedMesh;
        obstacle.shape=NavMeshObstacleShape.Box;obstacle.center=mesh.bounds.center;obstacle.size=mesh.bounds.size+new Vector3(.02f,0,.02f);
        obstacle.carving=true;obstacle.carveOnlyStationary=false;obstacle.carvingMoveThreshold=.05f;
        EditorUtility.SetDirty(door);EditorUtility.SetDirty(obstacle);
    }
    public static void Polish()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        var mission=Object.FindFirstObjectByType<EvacuationMission>();
        foreach(var door in mission.residents.Select(r=>r.door).Distinct())ConfigureDoor(door);
        var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder+"/CivilianEvacuation.controller");
        controller.layers[0].stateMachine.states.First(s=>s.state.name=="Terrified").state.cycleOffset=.075f;
        EditorUtility.SetDirty(controller);
        // Authoring path validation uses the open passage. Runtime carving tracks the swinging leaf.
        foreach(var door in mission.residents.Select(r=>r.door).Distinct())door.GetComponent<NavMeshObstacle>().enabled=false;
        try {ValidateAndSave();}
        finally
        {
            foreach(var door in mission.residents.Select(r=>r.door).Distinct())door.GetComponent<NavMeshObstacle>().enabled=true;
            EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);EditorSceneManager.SaveScene(mission.gameObject.scene);AssetDatabase.SaveAssets();
        }
    }
    public static void RebuildNavigation()
    {
        var mission=Object.FindFirstObjectByType<EvacuationMission>();
        var surface=mission.GetComponent<NavMeshSurface>();
        Physics.SyncTransforms();surface.BuildNavMesh();
        string path=Folder+"/VillageEvacuationNavMesh.asset";
        var existing=AssetDatabase.LoadAssetAtPath<NavMeshData>(path);
        if(existing)
        {
            var fresh=surface.navMeshData;surface.RemoveData();EditorUtility.CopySerialized(fresh,existing);Object.DestroyImmediate(fresh);surface.navMeshData=existing;EditorUtility.SetDirty(existing);surface.AddData();
        }
        else AssetDatabase.CreateAsset(surface.navMeshData,path);
        var lines=new List<string>();
        foreach(var r in mission.residents)
        {
            bool found=NavMesh.SamplePosition(r.transform.position,out NavMeshHit hit,2,NavMesh.AllAreas);
            if(found)r.transform.position=hit.position;
            Snap(r.houseExit);Snap(r.safePlace);
            lines.Add(r.name+" spawn="+r.transform.position.ToString("F2")+" onMesh="+found+" exit="+r.houseExit.position.ToString("F2")+" route="+Path(r.transform.position,r.houseExit.position));
        }
        Snap(mission.shelterApproach);Snap(mission.shelterEntrance);
        foreach(var r in mission.residents)lines.Add(r.name+" toShelter="+Path(r.houseExit.position,mission.shelterEntrance.position)+" toSlot="+Path(mission.shelterEntrance.position,r.safePlace.position));
        File.WriteAllLines(EvacuationSceneAudit.Folder+"/navigation.txt",lines);AssetDatabase.SaveAssets();
    }
    public static void FinishAuthoring()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        var mission=Object.FindFirstObjectByType<EvacuationMission>();
        foreach(var r in mission.residents)r.safePlace.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(mission.shelterEntrance.position-r.safePlace.position,Vector3.up));
        EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);EditorSceneManager.SaveScene(mission.gameObject.scene);
        const string menuPath="Assets/Scenes/MainMenu.unity";
        var menuScene=SceneManager.GetSceneByPath(menuPath);bool openedHere=!menuScene.isLoaded;
        if(openedHere)menuScene=EditorSceneManager.OpenScene(menuPath,OpenSceneMode.Additive);
        try
        {
            var all=menuScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<UnityEngine.UI.Text>(true)).ToArray();
            all.First(t=>t.name=="HowToSubtitle").text="Önce halkı sığınağa götür; ardından kasabayı yangından koru.";
            all.First(t=>t.name=="NoticeBody").text="Önce evlere yaklaş, Q ile seslen ve 7 sivili SHELTER tabelalı sığınağa götür. Tahliyeden sonra volkan patlar. İtfaiyenin deposunu gölden doldur ve evleri koru.";
            var row=menuScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).First(t=>t.name=="PATLAMAControlRow"||t.name=="TAHLİYEControlRow");
            row.name="TAHLİYEControlRow";
            foreach(var t in row.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                if(t.name=="Action")t.text="TAHLİYE";
                else if(t.name=="Description")t.text="Evlerin yakınında 'Herkes dışarıya!' diye seslen. Halkı sığınağa götür.";
                else if(t.text=="V")t.text="Q";
            }
            EditorSceneManager.MarkSceneDirty(menuScene);EditorSceneManager.SaveScene(menuScene);
            File.WriteAllText(EvacuationSceneAudit.Folder+"/help.txt","PASS: Menu help explains the Q shout, seven residents and pre-eruption evacuation.");
        }
        finally{if(openedHere)EditorSceneManager.CloseScene(menuScene,true);}
        Selection.activeGameObject=mission.residents[0].gameObject;
        EditorGUIUtility.PingObject(mission.gameObject);
        if(SceneView.lastActiveSceneView)
        {
            var r=mission.residents[0];
            SceneView.lastActiveSceneView.LookAt(r.transform.position+Vector3.up,Quaternion.LookRotation(-r.transform.forward),4,false);
            SceneView.lastActiveSceneView.Focus();
        }
        File.WriteAllText(EvacuationSceneAudit.Folder+"/final-state.txt","playing="+EditorApplication.isPlaying+"\nscene="+mission.gameObject.scene.path+"\ndirty="+mission.gameObject.scene.isDirty+"\nresidents="+mission.TotalCount+"\ncontroller clips="+string.Join(",",mission.residents[0].animator.runtimeAnimatorController.animationClips.Select(c=>c.name)));
    }
    static void Snap(Transform t){if(NavMesh.SamplePosition(t.position,out NavMeshHit hit,2,NavMesh.AllAreas))t.position=hit.position;}
    static string Path(Vector3 a,Vector3 b){var p=new NavMeshPath();return NavMesh.CalculatePath(a,b,NavMesh.AllAreas,p)?p.status+" corners="+p.corners.Length:"NO PATH";}
    public static void ValidateAndSave()
    {
        var mission=Object.FindFirstObjectByType<EvacuationMission>();
        if(!mission || mission.TotalCount!=7)throw new InvalidOperationException("Seven residents are required.");
        foreach(var r in mission.residents)
        {
            foreach(var pair in new[]{new[]{r.transform.position,r.houseExit.position},new[]{r.houseExit.position,mission.shelterEntrance.position},new[]{mission.shelterEntrance.position,r.safePlace.position}})
            {
                var p=new NavMeshPath();
                if(!NavMesh.CalculatePath(pair[0],pair[1],NavMesh.AllAreas,p)||p.status!=NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Incomplete evacuation path: "+r.name+" "+pair[0]+" -> "+pair[1]+" ("+p.status+")");
            }
        }
        EditorUtility.SetDirty(mission.eruption);EditorUtility.SetDirty(mission);
        EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);EditorSceneManager.SaveScene(mission.gameObject.scene);AssetDatabase.SaveAssets();
        File.WriteAllText(EvacuationSceneAudit.Folder+"/setup.txt","PASS: 7 residents, 5 houses, 21 complete navigation paths, saved scene and baked NavMesh.");
    }
}
