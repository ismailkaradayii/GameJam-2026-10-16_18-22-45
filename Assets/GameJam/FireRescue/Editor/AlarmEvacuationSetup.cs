using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using BKPureNature;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class AlarmEvacuationSetup
{
    public const string Output = "C:/Users/omanc/Documents/ChatGPT/game jam/Artifacts/AlarmEvacuation";

    [MenuItem("GameJam/Final Loop/Apply alarm evacuation")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use Volcano_Demo in Edit Mode.");
        Audit();
        Undo.SetCurrentGroupName("Alarm evacuation and volcanic weather");
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
        var environment = Object.FindFirstObjectByType<BK_EnvironmentManager>();
        if (!mission || !atmosphere || !environment) throw new InvalidOperationException("Missing village bindings.");
        foreach (var resident in mission.residents)
        {
            Undo.RecordObject(resident.animator.transform, "Align resident with indoor navigation");
            var local = resident.animator.transform.localPosition;
            resident.animator.transform.localPosition = new Vector3(0, local.y, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(resident.animator.transform);
            Undo.RecordObject(resident, "Configure alarm response");
            resident.reactionSeconds = .5f + resident.formationIndex * .12f;
            resident.panicRoamRadius = 17f;
            EditorUtility.SetDirty(resident);
        }
        Undo.RecordObject(mission, "Configure alarm evacuation");
        mission.alarmTriggersPanic = true;
        mission.callAllAlarmedVillagers = true;
        EditorUtility.SetDirty(mission);
        var weather = atmosphere.GetComponent<VolcanoWeatherTransition>();
        if (!weather) weather = Undo.AddComponent<VolcanoWeatherTransition>(atmosphere.gameObject);
        Undo.RecordObject(weather, "Configure asset weather after eruption");
        weather.sequence = atmosphere.sequence;
        weather.environment = environment;
        weather.sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional && l.name == "Directional Light");
        weather.sunnyFill = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.name == "Cool dusk fill light");
        weather.volcanicSky = AssetDatabase.LoadAssetAtPath<Material>("Assets/BK/PureNature_Volcano/Textures/Sky/Sky_volcano.mat");
        weather.volcanicClouds = AssetDatabase.LoadAssetAtPath<Material>("Assets/BK/PureNature_Volcano/Textures/Clouds/Materials/Clouds_Dense.mat");
        if (!weather.volcanicSky || !weather.volcanicClouds) throw new InvalidOperationException("Missing native volcanic sky/cloud materials.");
        weather.transitionSeconds = 10f;
        EditorUtility.SetDirty(weather);
        var words = mission.player.contextualInteractions.words;
        Undo.RecordObject(words, "Describe alarm and rally actions");
        words.callVillagers = "Buraya Gelin!";
        words.leadToShelter = "Köylüleri topla, sığınağa götür";
        words.alarmActivated = "Alarm çaldı! Köylüler dışarı çıkıyor.";
        EditorUtility.SetDirty(words);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Audit();
        File.WriteAllText(Output + "/applied.txt", "Saved indoor models, automatic alarm panic, Q rally and eruption weather at " + DateTime.Now.ToString("O"));
    }

    [MenuItem("GameJam/Final Loop/Audit alarm evacuation")]
    public static void Audit()
    {
        Directory.CreateDirectory(Output + "/Before"); Directory.CreateDirectory(Output + "/After");
        var scene = SceneManager.GetActiveScene();
        if (!EditorApplication.isPlaying && !File.Exists(Output + "/Before/Scene.unity"))
            EditorSceneManager.SaveScene(scene, Output + "/Before/Scene.unity", true);
        var sb = new StringBuilder(); sb.AppendLine($"{scene.path} play={EditorApplication.isPlaying} dirty={scene.isDirty}");
        foreach (var r in Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None).OrderBy(r=>r.name))
        {
            var house = r.door.GetComponentInParent<BurnableBuilding>();
            sb.AppendLine($"RESIDENT {r.name} root={r.transform.position:F3} scale={r.transform.lossyScale} house={house.name} houseBounds={house.ignitionBounds.bounds} houseLocal={house.transform.InverseTransformPoint(r.transform.position):F3}");
            sb.AppendLine($" MODEL {r.animator.name} position={r.animator.transform.position:F3} local={r.animator.transform.localPosition:F3} scale={r.animator.transform.lossyScale} humanScale={r.animator.humanScale}");
            foreach(var skin in r.GetComponentsInChildren<SkinnedMeshRenderer>(true)) sb.AppendLine($" SKIN {skin.name} bounds={skin.bounds} rootBone={(skin.rootBone?skin.rootBone.position.ToString("F3"):"none")} local={skin.transform.localPosition:F3}");
            foreach(var bone in new[]{HumanBodyBones.Hips,HumanBodyBones.Head,HumanBodyBones.LeftFoot})
            {var t=r.animator.GetBoneTransform(bone);if(t)sb.AppendLine($" BONE {bone} world={t.position:F3} localToResident={r.transform.InverseTransformPoint(t.position):F3}");}
            foreach(var t in house.GetComponentsInChildren<MeshRenderer>(true).Where(t=>t.name.Contains("floor")||t.name.StartsWith("Room"))) sb.AppendLine($" FLOOR {t.name} world={t.bounds} houseLocal={house.transform.InverseTransformPoint(t.bounds.center):F3}");
            sb.AppendLine(" DOOR "+r.door.transform.position.ToString("F3")+" bounds="+r.door.GetComponent<Renderer>().bounds+" exit="+r.houseExit.position.ToString("F3"));
        }
        var manager = Object.FindFirstObjectByType<BK_EnvironmentManager>();
        var director = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (director && director.villageAlarm)
        {
            var path = new NavMeshPath();
            NavMesh.SamplePosition(director.villageAlarm.approachPose.position, out var from, 8, NavMesh.AllAreas);
            NavMesh.SamplePosition(director.houseExit.position, out var exit, 8, NavMesh.AllAreas);
            NavMesh.SamplePosition(director.evacuation.shelterApproach.position, out var to, 8, NavMesh.AllAreas);
            bool doorPath = NavMesh.CalculatePath(from.position,exit.position,NavMesh.AllAreas,path);
            sb.AppendLine($"STARTING DOOR route={doorPath}/{path.status} from={from.position:F2} exit={exit.position:F2} corners=" + string.Join(";",path.corners.Select(p=>p.ToString("F2"))));
            bool shelterPath = NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,path);
            sb.AppendLine($"ALARM TO SHELTER route={shelterPath}/{path.status} to={to.position:F2} corners=" + string.Join(";",path.corners.Select(p=>p.ToString("F2"))));
        }
        if(manager)sb.AppendLine("ENV "+EditorJsonUtility.ToJson(manager));
        foreach(var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Where(l=>l.type==LightType.Directional)) sb.AppendLine("LIGHT "+EditorJsonUtility.ToJson(light)+" rotation="+light.transform.eulerAngles);
        File.WriteAllText(Output+"/audit.txt",sb.ToString());
        Debug.Log("Alarm evacuation audit saved.");
    }
}
