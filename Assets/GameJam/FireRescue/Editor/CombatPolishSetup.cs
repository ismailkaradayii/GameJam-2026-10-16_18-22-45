using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, repeatable authoring for the larger cast and the 25-spirit encounter.</summary>
[InitializeOnLoad]
public static class CombatPolishSetup
{
    const string Folder = "Library/CombatPolishReview";
    static double nextPoll;
    static CombatPolishSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        string request = Folder + "/request.txt";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try { if (command == "configure") Configure(); else if (command == "inspect") Inspect(); }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); }
    }

    [MenuItem("GameJam/Combat/Apply larger cast and 25 spirit polish")]
    public static void Configure()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != VolcanoTeamIntegrationSetup.VolcanoPath)
            throw new InvalidOperationException("Open the volcano scene in Edit Mode first.");
        Directory.CreateDirectory(Folder);
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        Undo.RecordObject(encounter, "25 flame spirits");
        encounter.waveSizes = new[] { 3, 4, 5, 6, 7 };
        encounter.maximumAlive = 10; encounter.secondsBetweenSpawns = 1.8f;
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        Undo.RecordObject(player.transform, "Door-height character scale");
        player.transform.localScale = Vector3.one * CharacterScaleSetup.CastScale;
        player.interactionDistance = 6.5f;
        player.eyeHeight = 1.7f * CharacterScaleSetup.CastScale;
        var feedback = player.pistol.GetComponent<Itfaiye.ItfaiyePistolFeedback>();
        feedback.creatureConfirmVolume = .34f; feedback.creatureImpactKick = .46f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(feedback);
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var shelterSlots = FindShelterSlots(mission);
        foreach (var resident in mission.residents)
        {
            Undo.RecordObject(resident.transform, "Door-height resident scale");
            resident.transform.localScale = Vector3.one * CharacterScaleSetup.CastScale;
            var agent = resident.GetComponent<NavMeshAgent>();
            agent.radius = .28f; agent.height = 1.8f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(resident.transform);
            // Fill the back row first; a settled resident must not block later arrivals.
            int index = resident.formationIndex;
            resident.safePlace.position = shelterSlots[index];
            resident.safePlace.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(mission.shelterEntrance.position - shelterSlots[index], Vector3.up));
        }
        foreach (var spawn in encounter.spawns)
        {
            var actor = spawn.creature;
            var source = actor.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) &&
                !r.name.ToLowerInvariant().Contains("flame") && !r.name.ToLowerInvariant().Contains("ember")).ToArray();
            if (source.Length == 0) continue;
            Bounds bounds = source[0].bounds; foreach (var r in source) bounds.Encapsulate(r.bounds);
            actor.transform.localScale *= 2.3f / Mathf.Max(.01f, bounds.size.y);
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor.transform);
        }
        // Bake the full-height clearance, rather than only making the models larger.
        var settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset").First();
        var settings = new SerializedObject(settingsAsset);
        var entry = settings.FindProperty("m_Settings").GetArrayElementAtIndex(0);
        entry.FindPropertyRelative("agentRadius").floatValue = CharacterScaleSetup.NavigationRadius;
        entry.FindPropertyRelative("agentHeight").floatValue = CharacterScaleSetup.NavigationHeight;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EvacuationMissionSetup.RebuildNavigation();
        EditorUtility.SetDirty(encounter); EditorUtility.SetDirty(player); EditorUtility.SetDirty(feedback);
        Undo.FlushUndoRecordObjects();
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Inspect();
    }

    static Vector3[] FindShelterSlots(EvacuationMission mission)
    {
        var candidates = new System.Collections.Generic.List<Vector3>();
        var path = new NavMeshPath();
        for (float x = -126f; x <= -118.5f; x += .35f)
        for (float z = -569f; z <= -560.8f; z += .35f)
        {
            var point = new Vector3(x, mission.shelterEntrance.position.y, z);
            if (NavMesh.SamplePosition(point, out var hit, .45f, NavMesh.AllAreas) &&
                NavMesh.CalculatePath(mission.shelterEntrance.position, hit.position, NavMesh.AllAreas, path) &&
                path.status == NavMeshPathStatus.PathComplete) candidates.Add(hit.position);
        }
        var chosen = new System.Collections.Generic.List<Vector3>();
        for (int i = 0; i < mission.residents.Length; i++)
        {
            Vector3 desired = new Vector3(-119f - (2 - i / 3) * 2.8f, mission.shelterEntrance.position.y, -567f + i % 3 * 2.8f);
            var available = candidates.Where(p => chosen.All(c => EvacuationResident.FlatDistance(c, p) >= 2.8f))
                .OrderBy(p => (p - desired).sqrMagnitude).ToArray();
            if (available.Length == 0) throw new InvalidOperationException("Shelter needs more room for resident " + i);
            chosen.Add(available[0]);
        }
        return chosen.ToArray();
    }

    static void Inspect()
    {
        Directory.CreateDirectory(Folder);
        var scene = SceneManager.GetActiveScene();
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        Physics.SyncTransforms();
        File.WriteAllText(Folder + "/configuration.txt", "scene=" + scene.path + "; dirty=" + scene.isDirty +
            "; playing=" + EditorApplication.isPlaying + "\nwaves=" + string.Join(",", encounter.waveSizes) +
            "; total=" + encounter.PlannedCount + "; aliveLimit=" + encounter.maximumAlive +
            "; spawnInterval=" + encounter.secondsBetweenSpawns + "\nplayerScale=" + player.transform.localScale +
            "; capsuleHeight=" + player.playerBody.height * player.transform.lossyScale.y +
            "; eyeHeight=" + (player.viewCamera.transform.position.y - player.transform.position.y) +
            "\nnavHeight=" + mission.GetComponent<NavMeshSurface>().GetBuildSettings().agentHeight + "\n" +
            string.Join("\n", mission.residents.Select(r => r.name + "; scale=" + r.transform.localScale +
                "; height=" + r.GetComponent<NavMeshAgent>().height + "; radius=" + r.GetComponent<NavMeshAgent>().radius)));
    }
}
