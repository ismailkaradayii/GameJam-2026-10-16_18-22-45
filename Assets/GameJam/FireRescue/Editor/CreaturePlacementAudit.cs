using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit geometry audit. Does not activate, move, save, or create scene objects.
public static class CreaturePlacementAudit
{
    [Serializable] public sealed class Entry
    {
        public string name, nearestHouse;
        public Vector3 rootPosition, capsuleCenter, capsuleBottom;
        public float radius, height, terrainHeight, footClearance, nearestHouseDistance;
        public bool terrainContainsSpawn, inactive, targetAssigned, terrainAssigned;
        public bool clearCandidateFound;
        public Vector3 suggestedPosition;
        public float suggestedHorizontalOffset;
        public string[] solidObstacles, overlappingHouses, overlappingHouseEnvelopes;
        public Vector3[] clearCandidates;
    }
    [Serializable] public sealed class Report
    {
        public string scene;
        public List<Entry> creatures = new List<Entry>();
    }

    [MenuItem("GameJam/Integration/Audit creature placement (read only)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run after the gameplay checks return to Edit Mode.");
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.path.EndsWith("/Volcano_Demo.unity", StringComparison.Ordinal))
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the open scene before auditing Volcano placement.");
            scene = EditorSceneManager.OpenScene("Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity", OpenSceneMode.Single);
        }
        var report = new Report { scene = scene.path };
        var houses = UnityEngine.Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None);
        Physics.SyncTransforms();
        foreach (var actor in UnityEngine.Object.FindObjectsByType<LavaCreatureActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.creatures.Add(Evaluate(actor, houses));
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/CreaturePlacementAudit.json", JsonUtility.ToJson(report, true));
        Debug.Log("CREATURE_PLACEMENT_AUDIT: " + report.creatures.Count + " creatures; solid overlaps=" + report.creatures.Count(e => e.solidObstacles.Length > 0) +
            "; house envelope overlaps=" + report.creatures.Count(e => e.overlappingHouseEnvelopes.Length > 0) + "; Logs/CreaturePlacementAudit.json");
    }

    // Shared with the explicit scene authoring command. This method only calculates a placement;
    // the caller decides whether to apply it. House envelopes include enclosed empty interior space.
    public static Entry Evaluate(LavaCreatureActor actor, BurnableBuilding[] houses)
    {
        var capsule = actor.GetComponentInChildren<CapsuleCollider>(true);
        if (!capsule) throw new InvalidOperationException(actor.name + " has no capsule.");
        Transform t = capsule.transform;
        Vector3 scale = t.lossyScale;
        float axisScale = Mathf.Abs(capsule.direction == 0 ? scale.x : capsule.direction == 1 ? scale.y : scale.z);
        float radialScale = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)) :
            capsule.direction == 1 ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
        float radius = capsule.radius * radialScale;
        float height = Mathf.Max(capsule.height * axisScale, radius * 2f);
        Vector3 axis = t.TransformDirection(capsule.direction == 0 ? Vector3.right : capsule.direction == 1 ? Vector3.up : Vector3.forward).normalized;
        Vector3 center = t.TransformPoint(capsule.center);
        Vector3 a = center + axis * (height * .5f - radius);
        Vector3 b = center - axis * (height * .5f - radius);
        Vector3 bottom = (a.y < b.y ? a : b) - Vector3.up * radius;
        var obstacles = Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore)
            .Where(c => !c.transform.IsChildOf(actor.transform) && !(c is TerrainCollider) && !c.GetComponentInParent<LavaIgnitionSource>()).ToArray();
        Vector3 rootPosition = actor.transform.position;
        var entry = new Entry { name = actor.name, rootPosition = rootPosition, capsuleCenter = center, capsuleBottom = bottom,
            radius = radius, height = height, inactive = !actor.gameObject.activeSelf,
            solidObstacles = obstacles.Select(c => PathOf(c.transform)).Distinct().ToArray(),
            overlappingHouses = obstacles.Select(c => c.GetComponentInParent<BurnableBuilding>()).Where(h => h).Select(h => h.name).Distinct().ToArray(),
            overlappingHouseEnvelopes = houses.Where(h => HouseEnvelopeBlocked(h, center, radius + .5f)).Select(h => h.name).ToArray(),
            clearCandidates = Array.Empty<Vector3>(),
            nearestHouseDistance = float.MaxValue };
        var jump = actor.jump;
        if (jump)
        {
            entry.targetAssigned = new SerializedObject(jump).FindProperty("target").objectReferenceValue != null;
            entry.terrainAssigned = jump.groundTerrain != null;
            if (jump.groundTerrain && jump.groundTerrain.terrainData)
            {
                var terrain = jump.groundTerrain;
                Vector3 local = rootPosition - terrain.transform.position, size = terrain.terrainData.size;
                entry.terrainContainsSpawn = local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z;
                entry.terrainHeight = terrain.SampleHeight(bottom) + terrain.transform.position.y;
                entry.footClearance = bottom.y - entry.terrainHeight;
            }
        }
        foreach (var house in houses)
        {
            if (!house.ignitionBounds) continue;
            float distance = Vector3.Distance(center, house.ignitionBounds.ClosestPoint(center));
            if (distance >= entry.nearestHouseDistance) continue;
            entry.nearestHouseDistance = distance;
            entry.nearestHouse = house.name;
        }
        if (obstacles.Length == 0 && entry.overlappingHouseEnvelopes.Length == 0)
        {
            entry.clearCandidateFound = true;
            entry.suggestedPosition = rootPosition;
        }
        else if (jump && jump.groundTerrain && jump.groundTerrain.terrainData)
        {
            // Small radial search only; keep the authored lava feed/progress untouched.
            // The first clear ring is the nearest sampled offset (0.25m rings, 32 directions).
            var terrain = jump.groundTerrain;
            var candidates = new List<Vector3>();
            for (float distance = .25f; distance <= 8f && !entry.clearCandidateFound; distance += .25f)
            {
                for (int direction = 0; direction < 32; direction++)
                {
                    float angle = direction * Mathf.PI * 2f / 32f;
                    Vector3 candidate = rootPosition + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
                    Vector3 local = candidate - terrain.transform.position, size = terrain.terrainData.size;
                    if (local.x < 0 || local.z < 0 || local.x > size.x || local.z > size.z) continue;
                    candidate.y = terrain.SampleHeight(candidate) + terrain.transform.position.y + Mathf.Max(0f, jump.groundClearance);
                    Vector3 shift = candidate - rootPosition;
                    if (houses.Any(h => HouseEnvelopeBlocked(h, center + shift, radius + .5f))) continue;
                    bool blocked = Physics.OverlapCapsule(a + shift, b + shift, radius + .05f, ~0, QueryTriggerInteraction.Ignore)
                        .Any(c => !c.transform.IsChildOf(actor.transform) && !(c is TerrainCollider) && !c.GetComponentInParent<LavaIgnitionSource>());
                    if (blocked) continue;
                    candidates.Add(candidate);
                    if (!entry.clearCandidateFound)
                    {
                        entry.clearCandidateFound = true;
                        entry.suggestedPosition = candidate;
                        entry.suggestedHorizontalOffset = distance;
                    }
                }
            }
            entry.clearCandidates = candidates.ToArray();
        }
        return entry;
    }

    static bool HouseEnvelopeBlocked(BurnableBuilding house, Vector3 center, float clearanceRadius)
    {
        if (!house || !house.ignitionBounds || !house.ignitionBounds.enabled || !house.gameObject.activeInHierarchy) return false;
        BoxCollider box = house.ignitionBounds;
        // All authored house boxes are upright and may rotate in yaw. Sample at the box's
        // middle height to protect its entire horizontal footprint, including hollow interiors.
        Vector3 probe = new Vector3(center.x, box.bounds.center.y, center.z);
        Vector3 separation = box.ClosestPoint(probe) - probe;
        separation.y = 0f;
        return separation.sqrMagnitude < clearanceRadius * clearanceRadius;
    }

    static string PathOf(Transform value) => value.parent ? PathOf(value.parent) + "/" + value.name : value.name;
}
