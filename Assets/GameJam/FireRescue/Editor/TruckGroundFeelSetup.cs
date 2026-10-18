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

/// <summary>Retains the approved quick driving and authors wheel-ground presentation, preserving other unsaved scene edits.</summary>
public static class TruckGroundFeelSetup
{
    const string PrefabPath = "Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab";
    const string MaterialPath = "Assets/GameJam/FireRescue/Materials/Fire truck tire tracks.mat";
    const string Folder = "Library/TruckGroundFeelReview";
    static readonly Dictionary<string, float> Tune = new Dictionary<string, float>
    {
        // Original approved values from before the tire/dust presentation update.
        { "motorTorque", 20800 }, { "maxForwardSpeedKph", 156 }, { "maxReverseSpeedKph", 58.5f },
        { "brakeTorque", 46800 }, { "lowSpeedTorqueMultiplier", 2 },
        { "throttleResponsePerSecond", 18 }, { "throttleReleasePerSecond", 20 },
        { "groundAccelerationAssist", 18.2f }, { "arcadeLateralGrip", 13 },
        { "arcadeBrakeAcceleration", 31.2f }, { "arcadeCornerSpeedKph", 54.6f }
    };
    static readonly string[] Switches = { "progressiveHandling", "smoothPedals" };

    [MenuItem("GameJam/Final Loop/Survey truck ground feel")]
    public static void Survey()
    {
        Directory.CreateDirectory("Logs/TruckGroundFeel");
        var drive = Object.FindFirstObjectByType<FireRescuePlayer>().drive;
        var text = new StringBuilder($"SURVEY {DateTime.UtcNow:O}\nScene={SceneManager.GetActiveScene().path}; dirty={SceneManager.GetActiveScene().isDirty}; playing={EditorApplication.isPlaying}\n");
        text.AppendLine($"Truck position={drive.transform.position:F3}; scale={drive.transform.lossyScale:F4}; mass={drive.vehicleMass}; COM={drive.centerOfMassOffset}; antiRoll={drive.antiRollForce}");
        foreach (var terrain in Terrain.activeTerrains)
        {
            text.AppendLine($"Terrain {terrain.name}: {terrain.transform.position:F2}");
            foreach (var layer in terrain.terrainData.terrainLayers)
                text.AppendLine($"Layer {layer.name}: {AssetDatabase.GetAssetPath(layer)}");
        }
        foreach (var wheel in drive.wheels) text.AppendLine($"Wheel {wheel.name}: world radius={wheel.radius * Mathf.Abs(wheel.transform.lossyScale.y):F3}");
        File.WriteAllText("Logs/TruckGroundFeel/survey.txt", text.ToString());
    }

    [MenuItem("GameJam/Final Loop/Apply truck ground feel")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use idle Edit Mode.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != TownDressingSetup.ScenePath) throw new InvalidOperationException("Open the volcano mission scene.");
        Directory.CreateDirectory(Folder); Directory.CreateDirectory("Logs/TruckGroundFeel");
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        File.Copy(scene.path, Folder + "/saved-before-" + stamp + ".unity", true);
        File.Copy(PrefabPath, Folder + "/prefab-before-" + stamp + ".prefab", true);
        if (!EditorSceneManager.SaveScene(scene, Folder + "/unsaved-before-" + stamp + ".unity", true))
            throw new InvalidOperationException("Could not preserve the unsaved scene.");

        var shader = Shader.Find("GameJam/Fire Truck Tire Tracks");
        if (!shader) throw new InvalidOperationException("The tire track shader must be imported first.");
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material) { material = new Material(shader) { name = "Fire truck tire tracks" }; AssetDatabase.CreateAsset(material, MaterialPath); }
        else material.shader = shader;
        EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
        var layers = Terrain.activeTerrains.SelectMany(t => t.terrainData.terrainLayers).Where(l => l &&
            l.name.IndexOf("mud", StringComparison.OrdinalIgnoreCase) >= 0 &&
            l.name.IndexOf("lava", StringComparison.OrdinalIgnoreCase) < 0).Distinct().ToArray();
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var drive = prefab.GetComponent<FireTruckDrive>();
            ApplyTune(drive);
            var effects = prefab.GetComponent<FireTruckGroundEffects>() ?? prefab.AddComponent<FireTruckGroundEffects>();
            effects.drive = drive; effects.tireTrackMaterial = material;
            effects.dustMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d36687a2ca5f1224f9758f18a438f7fe"));
            effects.soilDustShader = Shader.Find("GameJam/Fire Truck Soil Dust");
            effects.dustTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("5407ae27152b432aad928ef038cf94ea"));
            effects.looseTerrainLayers = layers;
            effects.trackTint = new Color(.055f, .035f, .018f, 1f);
            effects.trackOpacity = .62f;
            effects.dustTint = new Color(.46f, .355f, .25f, .55f);
            if (!effects.dustMaterial || !effects.soilDustShader || !effects.dustTexture) throw new InvalidOperationException("Missing soil presentation assets.");
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        var live = Object.FindFirstObjectByType<FireRescuePlayer>().drive;
        Undo.RecordObject(live, "Restore approved quick truck driving");
        // Revert only our tuning overrides so this scene inherits future tuning from the prefab.
        var serialized = new SerializedObject(live);
        foreach (string property in Tune.Keys.Concat(Switches))
        {
            var value = serialized.FindProperty(property);
            if (value != null && value.prefabOverride) PrefabUtility.RevertPropertyOverride(value, InteractionMode.AutomatedAction);
        }
        EditorUtility.SetDirty(live);
        EditorSceneManager.MarkSceneDirty(scene);
        PersistOnlyTuneOverrides(scene.path);
        Survey();
        File.WriteAllText("Logs/TruckGroundFeel/authoring.txt", $"COMPLETED {DateTime.UtcNow:O}\nOriginal quick handling restored. Four contact traces and soil dust retained on the existing truck prefab.\nLoose terrain layers: {string.Join(", ", layers.Select(l => l.name))}\nOnly selected drive overrides removed from saved scene; other saved and unsaved edits preserved. Backups in {Folder}.\n");
    }

    static void ApplyTune(FireTruckDrive drive)
    {
        var serialized = new SerializedObject(drive);
        foreach (var pair in Tune) serialized.FindProperty(pair.Key).floatValue = pair.Value;
        serialized.FindProperty("smoothPedals").boolValue = true;
        serialized.FindProperty("progressiveHandling").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    static void PersistOnlyTuneOverrides(string path)
    {
        string original = File.ReadAllText(path);
        string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<FireTruckDrive>();
        if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out _, out long driveId))
            throw new InvalidOperationException("Missing drive source identifier.");
        var fields = new HashSet<string>(Tune.Keys.Concat(Switches));
        string pattern = @"(?m)^    - target: \{fileID: " + driveId + @", guid: " + guid + @", type: 3\}\r?\n      propertyPath: (?<field>[^\r\n]+)\r?\n      value: [^\r\n]*\r?\n      objectReference: [^\r\n]*\r?\n";
        int removed = 0;
        string output = Regex.Replace(original, pattern, m =>
        {
            if (!fields.Contains(m.Groups["field"].Value)) return m.Value;
            removed++; return "";
        });
        File.WriteAllText(path, output, new UTF8Encoding(false));
        File.WriteAllText("Logs/TruckGroundFeel/scoped-scene-write.txt", $"Removed {removed} selected driving overrides from {path}; prefab retains the authored tune.\n");
    }
}
