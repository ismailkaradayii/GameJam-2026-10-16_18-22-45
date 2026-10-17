using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VillageCombatUpgradeSetup
{
    public const string Folder = "Logs/VillageCombatUpgrade";
    static double nextPoll;
    static VillageCombatUpgradeSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string file = "Logs/VillageCombatUpgrade.command";
        if (!File.Exists(file)) return;
        string command = File.ReadAllText(file).Trim(); File.Delete(file); Directory.CreateDirectory(Folder);
        try
        {
            if (command == "apply") Apply();
            else if (command == "audit") Audit();
            else if (command == "checks") VillageCombatUpgradeChecks.Begin();
            else if (command == "preview") VillageFirePreview.Begin();
            else if (command == "water") WaterRealismChecks.Begin();
            else if (command == "swimming") SwimmingAnimationChecks.Begin();
            else if (command == "combat") LavaCombatChecks.Begin();
            else if (command == "refresh") AssetDatabase.Refresh();
            else if (command == "stop") EditorApplication.ExitPlaymode();
            else throw new InvalidOperationException(command);
            File.WriteAllText(Folder + "/command-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/command-result.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("GameJam/Village combat/Apply improvements")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode before authoring.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open the Volcano scene before applying.");
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (!encounter || !sequence || !mission || !mission.startingHouse) throw new InvalidOperationException("Village bindings missing.");
        var originals = encounter.spawns.Where(s => s != null && s.creature).Select(s => s.creature).Distinct().ToArray();
        var approach = sequence.routes.Select(r => r.flow).First(f => f && f.name == "Town Approach");
        Func<string, ProgressiveLavaFlow> flow = name => sequence.routes.Select(r => r.flow).First(f => f && f.name == name);
        var feeds = new[] { approach, flow("Town Branch - House_Green_Prefab (3)"), flow("Town Branch - House_Green_Prefab"),
            approach, approach, flow("Town Branch - House_Green_Prefab (1)") };
        float[] progress = { .52f, .18f, .32f, .28f, .78f, .22f };
        var spawns = new LavaCreatureEncounter.Spawn[6];
        Undo.RecordObject(encounter, "Distribute spirit spawn routes");
        for (int i = 0; i < spawns.Length; i++)
        {
            var actor = i < originals.Length ? originals[i] : Object.Instantiate(originals[i % originals.Length], originals[0].transform.parent);
            if (i >= originals.Length) { Undo.RegisterCreatedObjectUndo(actor.gameObject, "Additional spirit emergence source"); actor.name = "Village spirit source " + (i + 1); }
            spawns[i] = new LavaCreatureEncounter.Spawn { creature = actor, feed = feeds[i], feedProgress = progress[i],
                delayAfterArrival = .2f + (i % 3) * .25f, descendFromLava = true, emergenceDepth = 2.2f, descentEndProgress = .9f };
        }
        encounter.spawns = spawns; encounter.disperseIntoVillage = true; encounter.buildingAttackShare = .6f;
        EditorUtility.SetDirty(encounter);

        // Both the pickup cabinet and the room table belong to the user's starting room.
        foreach (var mesh in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
        {
            string name = mesh.name.ToLowerInvariant();
            if ((!name.StartsWith("table02") && !name.StartsWith("cabinet04")) || !mesh.sharedMesh) continue;
            Vector3 p = mesh.transform.position; p.y = mission.startingHouse.bounds.center.y;
            if (!mission.startingHouse.bounds.Contains(p)) continue;
            var colliders = mesh.GetComponents<Collider>();
            if (colliders.Any(c => c.enabled && !c.isTrigger)) continue;
            var collider = Undo.AddComponent<BoxCollider>(mesh.gameObject);
            collider.center = mesh.sharedMesh.bounds.center; collider.size = mesh.sharedMesh.bounds.size;
            collider.isTrigger = false; EditorUtility.SetDirty(collider);
            PrefabUtility.RecordPrefabInstancePropertyModifications(mesh.gameObject);
        }
        var feedback = Object.FindFirstObjectByType<Itfaiye.ItfaiyePistolFeedback>();
        if (feedback)
        {
            Undo.RecordObject(feedback, "Spirit hit response");
            feedback.creatureConfirmVolume = .46f; feedback.creatureImpactKick = .57f; EditorUtility.SetDirty(feedback);
        }
        var shader = Resources.Load<Shader>("BuildingFire/BuildingFire");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Building fire shader did not compile.");
        Physics.SyncTransforms(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Audit();
    }

    static void Audit()
    {
        var report = new StringBuilder(); var scene = SceneManager.GetActiveScene();
        report.AppendLine($"scene={scene.path} dirty={scene.isDirty} play={EditorApplication.isPlaying}");
        foreach (var e in Object.FindObjectsByType<LavaCreatureEncounter>(FindObjectsSortMode.None))
        {
            report.AppendLine($"spawns={e.spawns.Length} independent={e.disperseIntoVillage} buildingShare={e.buildingAttackShare}");
            foreach (var s in e.spawns) report.AppendLine($"source={s.creature.name} feed={s.feed.name} progress={s.feedProgress}");
        }
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None))
            report.AppendLine($"house={house.name} bounds={house.ignitionBounds.bounds}");
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name.StartsWith("Table02") || t.name.StartsWith("Cabinet04"))
                report.AppendLine($"furniture={t.name} position={t.position} solid={t.GetComponents<Collider>().Count(c=>c.enabled&&!c.isTrigger)}");
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/audit.txt", report.ToString());
    }
}
