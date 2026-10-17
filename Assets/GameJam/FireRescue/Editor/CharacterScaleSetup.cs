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
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class CharacterScaleSetup
{
    public const string Folder = "Library/CharacterScaleReview";
    public const float CastScale = 3.3f;
    public const float NavigationHeight = 6.15f;
    public const float NavigationRadius = .28f * CastScale;
    static double next;
    static CharacterScaleSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        string path = Folder + "/request.txt";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try { if (command == "inspect") Inspect(); else if (command == "apply") Apply(); }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); }
    }
    [MenuItem("GameJam/Evacuation/Match characters to house doors")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before resizing the cast.");
        Directory.CreateDirectory(Folder);
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (!mission || mission.gameObject.scene.path != VolcanoTeamIntegrationSetup.VolcanoPath) throw new InvalidOperationException("Open Volcano_Demo first.");
        File.Delete(Folder + "/error.txt");
        var player = mission.player;
        Undo.RecordObjects(new Object[] { player, player.transform }, "Match player to house doors");
        player.transform.localScale = Vector3.one * CastScale;
        player.eyeHeight = 1.7f * CastScale;
        player.interactionDistance = 6.5f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        EditorUtility.SetDirty(player);
        foreach (var resident in mission.residents)
        {
            var agent = resident.GetComponent<NavMeshAgent>();
            Undo.RecordObjects(new Object[] { resident.transform, agent }, "Match residents to house doors");
            resident.transform.localScale = Vector3.one * CastScale;
            // Agent dimensions inherit the root scale; the bake uses world dimensions.
            agent.height = 1.8f;
            agent.radius = .28f;
            EditorUtility.SetDirty(agent);
        }
        // Runtime animation speed is measured in the model's unscaled units.
        var controller = (AnimatorController)mission.residents[0].animator.runtimeAnimatorController;
        var tree = (BlendTree)controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state.motion;
        Undo.RecordObject(tree, "Keep walking and running at the new body size");
        var children = tree.children;
        children[1].threshold = mission.residents[0].walkingSpeed / CastScale;
        children[2].threshold = mission.residents[0].runningSpeed / CastScale;
        tree.children = children; EditorUtility.SetDirty(tree);

        var settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").First();
        var settings = new SerializedObject(settingsAsset);
        var entry = settings.FindProperty("m_Settings").GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("agentRadius").floatValue = NavigationRadius;
        entry.FindPropertyRelative("agentHeight").floatValue = NavigationHeight;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var doors = mission.residents.Select(r => r.door.GetComponent<NavMeshObstacle>()).Distinct().ToArray();
        var enabled = doors.Select(d => d.enabled).ToArray();
        foreach (var door in doors) door.enabled = false;
        try
        {
            EvacuationMissionSetup.RebuildNavigation();
            EvacuationMissionSetup.ValidateAndSave();
        }
        finally
        {
            for (int i = 0; i < doors.Length; i++) doors[i].enabled = enabled[i];
            EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
            EditorSceneManager.SaveScene(mission.gameObject.scene); AssetDatabase.SaveAssets();
        }
        Inspect();
        var r = mission.residents[0]; var house = r.door.GetComponentInParent<BurnableBuilding>().transform;
        EvacuationSceneAudit.Capture("door-height-cast",house.TransformPoint(new Vector3(-.3f,3.5f,11.5f)),r.transform.position+Vector3.up*3);
    }
    public static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var player = mission.player;
        Physics.SyncTransforms();
        var lines = new List<string> { "playing=" + EditorApplication.isPlaying + "; scene=" + mission.gameObject.scene.path + "; dirty=" + mission.gameObject.scene.isDirty };
        foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(a => a.isHuman))
        {
            var renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Bounds bounds = new Bounds(animator.transform.position, Vector3.zero);
            if (renderers.Length > 0) { bounds = renderers[0].bounds; foreach (var r in renderers) bounds.Encapsulate(r.bounds); }
            lines.Add("HUMAN " + animator.name + " parent=" + animator.transform.parent?.name + " scale=" + animator.transform.lossyScale + " bounds=" + bounds + "; head=" + animator.GetBoneTransform(HumanBodyBones.Head)?.position);
        }
        lines.Add("PLAYER scale=" + player.transform.localScale + "; capsule=" + player.playerBody.height + " radius=" + player.playerBody.radius + "; worldHeight=" + player.playerBody.height * player.transform.lossyScale.y + "; eye=" + (player.viewCamera.transform.position.y - player.transform.position.y));
        foreach (var resident in mission.residents)
        {
            var door = resident.door.GetComponent<Renderer>().bounds;
            var agent = resident.GetComponent<NavMeshAgent>();
            lines.Add(resident.name + "; door=" + door + "; agentWorldHeight=" + agent.height * resident.transform.lossyScale.y + "; worldRadius=" + agent.radius * resident.transform.lossyScale.x + "; scale=" + resident.transform.localScale);
            if (EditorApplication.isPlaying && agent.isOnNavMesh)
            {
                agent.FindClosestEdge(out var edge);
                lines.Add("MOTION " + resident.State + " velocity=" + agent.velocity + "; desired=" + agent.desiredVelocity + "; edge=" + edge.distance + "; corners=" + string.Join("; ", agent.path.corners.Select(v=>v.ToString())));
                var capsule = resident.GetComponent<CapsuleCollider>();
                foreach (var other in Physics.OverlapSphere(resident.transform.position + Vector3.up * 2, 6, ~0, QueryTriggerInteraction.Ignore))
                    if (Physics.ComputePenetration(capsule,capsule.transform.position,capsule.transform.rotation,other,other.transform.position,other.transform.rotation,out var direction,out float distance))
                        lines.Add("OVERLAP " + other.name + "; separation=" + direction + " * " + distance);
                foreach (var obstacle in Object.FindObjectsByType<NavMeshObstacle>(FindObjectsSortMode.None))
                    if (Vector3.Distance(obstacle.transform.position, resident.transform.position)<12)
                        lines.Add("OBSTACLE " + obstacle.name + " pos=" + obstacle.transform.position + " localBox=" + obstacle.center + " size=" + obstacle.size + " scale=" + obstacle.transform.lossyScale + " carve=" + obstacle.carving);
            }
        }
        foreach (var point in mission.residents.Select(r => r.safePlace).Concat(new[] { mission.shelterEntrance }))
        {
            var hits = Physics.RaycastAll(point.position + Vector3.up * .25f, Vector3.up, 40, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Take(3);
            lines.Add("CEILING " + point.name + " at=" + point.position + ": " + string.Join("; ", hits.Select(h => h.collider.name + "=" + h.distance)));
        }
        File.WriteAllLines(Folder + "/measurements.txt", lines);
    }
}
