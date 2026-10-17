using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ArcadeTruckSetup
{
    [MenuItem("GameJam/Final Loop/Audit arcade truck")]
    public static void Audit()
    {
        var drive = Object.FindFirstObjectByType<FireTruckDrive>();
        var lines = new List<string> { "Play=" + EditorApplication.isPlaying, "Drive enabled=" + drive.enabled };
        for (int i = 0; i < 4; i++)
        {
            var visual = drive.wheelVisuals[i]; var wheel = drive.wheels[i];
            lines.Add($"WHEEL {i} visual={Path(visual)} active={visual.gameObject.activeInHierarchy} renderers={visual.GetComponentsInChildren<Renderer>(true).Length} rotation={visual.localEulerAngles} rpm={wheel.rpm} pose={wheel.transform.position}");
        }
        foreach (var renderer in drive.GetComponentsInChildren<Renderer>(true))
        {
            var filter = renderer.GetComponent<MeshFilter>();
            lines.Add($"MESH {Path(renderer.transform)} enabled={renderer.enabled} active={renderer.gameObject.activeInHierarchy} bounds={renderer.bounds} mesh={(filter && filter.sharedMesh ? filter.sharedMesh.name : "none")}");
        }
        File.WriteAllLines("Logs/ArcadeTruck-audit.txt", lines);
    }

    [MenuItem("GameJam/Final Loop/Apply arcade truck")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        const string materialPath = "Assets/GameJam/FireRescue/Materials/TruckBodySlide.physicMaterial";
        var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
        if (!material)
        {
            material = new PhysicsMaterial("Truck body slide");
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.dynamicFriction = 0; material.staticFriction = 0;
        material.frictionCombine = PhysicsMaterialCombine.Minimum; material.bounciness = 0;
        EditorUtility.SetDirty(material);
        var drive = Object.FindFirstObjectByType<FireTruckDrive>();
        Tune(drive, material);
        const string prefabPath = "Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try { Tune(prefab.GetComponent<FireTruckDrive>(), material); PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorSceneManager.MarkSceneDirty(drive.gameObject.scene);
        EditorSceneManager.SaveScene(drive.gameObject.scene);
        AssetDatabase.SaveAssets();
        Audit();
    }
    static void Tune(FireTruckDrive drive, PhysicsMaterial material)
    {
        FinalLoopSetup.ConfigureTruckPace(drive);
        foreach (var box in drive.GetComponents<BoxCollider>())
        { box.sharedMaterial = material; EditorUtility.SetDirty(box); }
        EditorUtility.SetDirty(drive);
        if (PrefabUtility.IsPartOfPrefabInstance(drive)) PrefabUtility.RecordPrefabInstancePropertyModifications(drive);
    }

    [MenuItem("GameJam/Final Loop/Validate arcade truck")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        FinalLoopSetup.Validate();
        var drive = Object.FindFirstObjectByType<FireTruckDrive>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab").GetComponent<FireTruckDrive>();
        var errors = new List<string>();
        if (drive.gameObject.scene.isDirty) errors.Add("Scene has unsaved changes.");
        if (!drive.arcadeHandling || !prefab.arcadeHandling || !drive.wheelSpinFromTravel || !prefab.wheelSpinFromTravel) errors.Add("Arcade settings not saved to scene and prefab.");
        if (drive.transform.lossyScale != Vector3.one || drive.wheels.Any(w=>!w || !w.enabled || w.attachedRigidbody!=drive.body)) errors.Add("Invalid physics references or root scale.");
        if (drive.wheelVisuals.Any(w=>!w || !w.GetComponent<Renderer>())) errors.Add("Missing visible wheel reference.");
        var output = new List<string> { "Arcade truck validation - " + DateTime.Now.ToString("s"), "Scene="+drive.gameObject.scene.path };
        foreach (string report in new[]{"Logs/ArcadeTruck-checks.txt","Logs/TruckLakeRoute-checks.txt"})
        {
            string[] tests = File.ReadAllLines(report);
            if(tests.Length==0 || tests[tests.Length-1]!="COMPLETED") errors.Add("Incomplete physical test: "+report);
            output.Add(report); output.AddRange(tests.Where(l=>l.StartsWith("PASS")));
        }
        output.Insert(0,errors.Count==0?"PASS":"FAIL");output.AddRange(errors);
        output.Add("Scope: automatic Unity Play Mode tests using the real saved truck and collisions. Handling fixture uses temporary flat ground, a 1.7m step and a tall wall. Lake fixture drives the saved village/lake terrain without teleporting the truck and uses E/F through the real input system. Preparation/deadline is disabled only in the isolated route test; this is not a manual full-story playthrough.");
        output.Add("Inspector: FireTruckDrive > Arcade driving assistance controls turn radius, corner speed, grip, upright assistance and obstacle height. Existing suspension, body dimensions, wheels, hose, cannon and E entry/F exit remain connected.");
        output.Add("Manual setup: none. Open the saved Volcano_Demo scene and press Play. WASD drives, SPACE brakes, E enters/refills, F exits.");
        output.Add("Files for this change: FireTruckDrive.cs; FireRescuePlayer.cs; FinalLoopSetup.cs; TruckLakeRouteChecks.cs; ArcadeTruckSetup.cs (+meta); ArcadeTruckChecks.cs (+meta); TruckBodySlide.physicMaterial (+meta); Fire Truck.prefab; Volcano_Demo.unity. Terrain was not edited in this iteration.");
        File.WriteAllLines("Logs/ArcadeTruck-validation.txt",output);
        if(errors.Count>0)throw new InvalidOperationException(string.Join("; ",errors));
    }
    static string Path(Transform value) => value.parent ? Path(value.parent) + "/" + value.name : value.name;
}
