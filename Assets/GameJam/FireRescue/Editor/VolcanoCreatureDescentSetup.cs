using System;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class VolcanoCreatureDescentSetup
{
    [MenuItem("GameJam/Combat/Place spirits in mountain lava")]
    public static void Run()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != VolcanoTeamIntegrationSetup.VolcanoPath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode before configuring the descent.");
        if (scene.isDirty) throw new InvalidOperationException("Save the current scene edits before configuring the descent.");
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>(FindObjectsInactive.Include);
        if (!encounter) throw new InvalidOperationException("Volcano_Demo has no lava encounter.");
        Directory.CreateDirectory("Library/LavaDescentReview");
        File.Copy(scene.path, "Library/LavaDescentReview/Volcano_Demo.before.unity", true);
        Configure(encounter);
        Undo.FlushUndoRecordObjects();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Library/LavaDescentReview/configured.txt", "Five waves emerge from three mountain lava sources, follow Town Approach, and end with extraction in the truck.");
    }

    public static void Configure(LavaCreatureEncounter encounter)
    {
        var townRoute = encounter.sequence.routes.FirstOrDefault(r => r?.flow && r.flow.name == "Town Approach");
        if (townRoute == null || !townRoute.feed || !townRoute.feed.source || !townRoute.feed.source.m_Container)
            throw new InvalidOperationException("Town Approach must have a connected mountain lava feed.");
        var mountain = townRoute.feed;
        Undo.RecordObject(encounter, "Move lava spirit encounter onto the mountain");
        encounter.enableWaves = true;
        encounter.enableFinalObjective = true;
        encounter.waveSizes = new[] { 6, 8, 10, 12, 14 };
        encounter.secondsBetweenWaves = 32f;
        encounter.secondsBetweenSpawns = .85f;
        encounter.maximumAlive = 24;
        ConfigureWaveAmmo();
        for (int i = 0; i < encounter.spawns.Length; i++)
        {
            var spawn = encounter.spawns[i];
            if (spawn?.creature == null) throw new InvalidOperationException("Missing authored lava spirit.");
            spawn.feed = mountain;
            spawn.feedProgress = Mathf.Min(.035f + i * .03f, .3f);
            spawn.delayAfterArrival = .2f + i * .45f;
            spawn.descendFromLava = true;
            spawn.downhillFlows = new[] { townRoute.flow };
            spawn.descentEndProgress = .6f;
            spawn.emergenceDepth = 3.1f;
            var creature = spawn.creature;
            Undo.RecordObject(creature.gameObject, "Name mountain spirit");
            Undo.RecordObject(creature.transform, "Position mountain spirit");
            creature.name = "Mountain Lava Spirit " + (i + 1);
            Vector3 point = PreviewPoint(mountain, spawn.feedProgress);
            var jump = creature.GetComponent<LavaCreatureAutoJump>();
            if (jump && jump.groundTerrain)
                point.y = Mathf.Max(point.y, jump.groundTerrain.SampleHeight(point) + jump.groundTerrain.transform.position.y);
            creature.transform.position = point + Vector3.up * (jump ? jump.groundClearance : 0f);
            creature.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(creature.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(creature.gameObject);
        }
        EditorUtility.SetDirty(encounter);
    }

    public static void ConfigureWaveAmmo()
    {
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        if (!player || !player.pistol) return;
        // Fifty spirits need 150 hits; leave room for missed shots without changing the shared pistol prefab.
        Undo.RecordObject(player.pistol, "Stock ammunition for five lava waves");
        player.pistol.reserveAmmo = Mathf.Max(player.pistol.reserveAmmo, 240);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.pistol);
        EditorUtility.SetDirty(player.pistol);
    }

    static Vector3 PreviewPoint(ProgressiveLavaFlow flow, float progress)
    {
        var points = new Vector3[301];
        var distances = new float[points.Length];
        for (int n = 0; n < points.Length; n++)
        {
            float t = n / (float)(points.Length - 1);
            points[n] = (Vector3)flow.source.m_Container.EvaluatePosition(flow.reverse ? 1f - t : t);
            if (n > 0) distances[n] = distances[n - 1] + Vector3.Distance(points[n - 1], points[n]);
        }
        float desired = progress * distances[distances.Length - 1];
        for (int n = 1; n < points.Length; n++)
            if (distances[n] >= desired)
                return Vector3.Lerp(points[n - 1], points[n], Mathf.InverseLerp(distances[n - 1], distances[n], desired));
        return points[points.Length - 1];
    }
}
