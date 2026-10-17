using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class InteractiveDoorSetup
{
    [Serializable] sealed class Blockage
    {
        public string hierarchy, colliderType;
        public float firstAngle, deepestAngle, maximumDepth;
        public Vector3 position, size;
        public Vector3 deepestSeparationDirection, deepestSeparationInLeaf;
        public int samples;
    }
    [Serializable] sealed class Diagnostic
    {
        public string hierarchy, mesh, note;
        public Vector3 meshCenter, meshSize, worldPosition, scale;
        public float localYaw;
        public List<Blockage> positiveArc = new List<Blockage>(), negativeArc = new List<Blockage>();
        public List<Blockage> hingeTrimmedPositiveArc = new List<Blockage>(), hingeTrimmedNegativeArc = new List<Blockage>();
        public Vector3 fittedCenter, fittedSize;
    }

    [MenuItem("GameJam/Environment/Inspect opendoor swing blockers")]
    public static void InspectBlockers()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Inspect outside Play Mode.");
        var leaf = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Single(t => t.name.Equals("opendoor", StringComparison.OrdinalIgnoreCase));
        Mesh mesh = leaf.GetComponent<MeshFilter>().sharedMesh;
        Bounds bounds = mesh.bounds; Vector3 scale = leaf.lossyScale;
        var result = new Diagnostic { hierarchy = PathOf(leaf), mesh = mesh.name, meshCenter = bounds.center, meshSize = bounds.size,
            worldPosition = leaf.position, scale = scale, localYaw = leaf.localEulerAngles.y,
            note = "Read-only authored leaf inspection. A temporary unsaved fitted box probes both arcs; walls and furniture remain solid." };
        var probeObject = new GameObject("Temporary door sweep diagnostic") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            probeObject.transform.SetPositionAndRotation(leaf.position, leaf.rotation); probeObject.transform.localScale = scale;
            var probe = probeObject.AddComponent<BoxCollider>(); probe.center = bounds.center;
            float inset = .012f / scale.x;
            probe.size = new Vector3(Mathf.Max(bounds.size.x - inset * 2, .025f / scale.x),
                Mathf.Max(.1f, bounds.size.y - inset * 2), Mathf.Max(.1f, bounds.size.z - inset * 2));
            Physics.SyncTransforms();
            result.positiveArc = ScanArc(leaf, probe, 1); result.negativeArc = ScanArc(leaf, probe, -1);
            FitLeafBox(probe, bounds, scale);
            result.fittedCenter = probe.center; result.fittedSize = probe.size;
            Physics.SyncTransforms();
            result.hingeTrimmedPositiveArc = ScanArc(leaf, probe, 1); result.hingeTrimmedNegativeArc = ScanArc(leaf, probe, -1);
        }
        finally { Object.DestroyImmediate(probeObject); Physics.SyncTransforms(); }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/InteractiveDoorObstructions.json", JsonUtility.ToJson(result, true));
        Debug.Log("INTERACTIVE_DOOR_OBSTRUCTIONS: original +90=" + result.positiveArc.Count + "; original -90=" + result.negativeArc.Count +
            "; hinge-trimmed +90=" + result.hingeTrimmedPositiveArc.Count + "; hinge-trimmed -90=" + result.hingeTrimmedNegativeArc.Count + "; report=Logs/InteractiveDoorObstructions.json");
    }

    static List<Blockage> ScanArc(Transform leaf, BoxCollider probe, int direction)
    {
        var records = new Dictionary<Collider, Blockage>(); Vector3 scale = leaf.lossyScale;
        for (int degrees = 0; degrees <= 90; degrees += 2)
        {
            float angle = degrees * direction; Quaternion rotation = leaf.rotation * Quaternion.Euler(0, angle, 0);
            Vector3 center = leaf.position + rotation * Vector3.Scale(probe.center, scale);
            Vector3 half = Vector3.Scale(probe.size * .5f, scale);
            foreach (Collider other in Physics.OverlapBox(center, half, rotation, ~0, QueryTriggerInteraction.Ignore))
            {
                if (other == probe || other.transform.IsChildOf(leaf)) continue;
                if (!Physics.ComputePenetration(probe, leaf.position, rotation, other, other.transform.position, other.transform.rotation, out Vector3 separation, out float depth) || depth <= .008f) continue;
                if (!records.TryGetValue(other, out Blockage record))
                {
                    record = new Blockage { hierarchy = PathOf(other.transform), colliderType = other.GetType().Name,
                        firstAngle = angle, position = other.bounds.center, size = other.bounds.size };
                    records.Add(other, record);
                }
                record.samples++;
                if (depth > record.maximumDepth)
                {
                    record.maximumDepth = depth; record.deepestAngle = angle;
                    record.deepestSeparationDirection = separation; record.deepestSeparationInLeaf = Quaternion.Inverse(rotation) * separation;
                }
            }
        }
        return records.Values.OrderBy(b => Mathf.Abs(b.firstAngle)).ThenByDescending(b => b.maximumDepth).ToList();
    }
    [MenuItem("GameJam/Environment/Configure opendoor")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Configure the door outside Play Mode.");
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Volcano_Demo") throw new InvalidOperationException("Open the saved Volcano_Demo scene first.");
        int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Configure opendoor");
        try
        {
            InteractiveDoor door = ConfigureScene(scene);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            Undo.CollapseUndoOperations(undo);
            Debug.Log("INTERACTIVE_DOOR_READY: " + PathOf(door.transform) + "; hinge=" + door.transform.position + "; openAngle=" + door.openAngle + "; duration=" + door.travelSeconds);
        }
        catch { Undo.RevertAllDownToGroup(undo); throw; }
    }

    public static InteractiveDoor ConfigureScene(Scene scene)
    {
        var matches = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name.Equals("opendoor", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one saved opendoor; found " + matches.Length);
        Transform leaf = matches[0];
        var mesh = leaf.GetComponent<MeshFilter>();
        var handles = leaf.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("door_knob", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!mesh || !mesh.sharedMesh || handles.Length != 2)
            throw new InvalidOperationException("opendoor must be a separate door leaf with its two knobs; refusing an unverified assembly.");
        Bounds bounds = mesh.sharedMesh.bounds;
        Vector3 scale = leaf.lossyScale;
        string geometry = "mesh=" + mesh.sharedMesh.name + "; bounds=" + bounds + "; scale=" + scale + "; localYaw=" + leaf.localEulerAngles.y;
        // The imported leaf has thickness on X, width on +Z, and the knobs at its far +Z edge.
        // Validate those facts before trusting the existing root as a hinge; never infer from the object name alone.
        bool uniform = scale.x > 0 && Mathf.Abs(scale.x - scale.y) < .01f && Mathf.Abs(scale.x - scale.z) < .01f;
        bool hingeAtEdge = bounds.size.z > .5f && bounds.size.x < bounds.size.z * .25f &&
            Mathf.Abs(bounds.min.z) < bounds.size.z * .08f && Mathf.Abs(bounds.center.x) < bounds.size.z * .12f;
        bool handlesAtLatch = handles.All(h => leaf.InverseTransformPoint(h.position).z > bounds.center.z + bounds.size.z * .25f);
        if (!uniform || !hingeAtEdge || !handlesAtLatch)
            throw new InvalidOperationException("Door hinge needs a Unity geometry inspection: " + geometry + "; knobs=" + string.Join(",", handles.Select(h => leaf.InverseTransformPoint(h.position).ToString())));
        if (leaf.GetComponentsInChildren<MeshFilter>(true).Any(f => f != mesh && !f.name.StartsWith("door_knob", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Unexpected frame/geometry under opendoor; refusing to rotate it.");

        Undo.RegisterFullObjectHierarchyUndo(leaf.gameObject, "Author interactive door leaf");
        var door = leaf.GetComponent<InteractiveDoor>();
        Quaternion closed = door ? door.ClosedLocalRotation : leaf.localRotation;
        if (!door) door = Undo.AddComponent<InteractiveDoor>(leaf.gameObject);
        foreach (Transform part in leaf.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(part.gameObject, 0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(part.gameObject);
        }
        foreach (var oldCollider in leaf.GetComponentsInChildren<MeshCollider>(true))
        {
            oldCollider.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldCollider);
        }
        var box = leaf.GetComponent<BoxCollider>();
        if (!box) box = Undo.AddComponent<BoxCollider>(leaf.gameObject);
        FitLeafBox(box, bounds, scale);
        box.isTrigger = false; box.enabled = true;
        var body = leaf.GetComponent<Rigidbody>();
        if (!body) body = Undo.AddComponent<Rigidbody>(leaf.gameObject);
        body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None;
        door.travelSeconds = .65f; door.contactTolerance = .008f;
        door.ConfigureClosedPose(closed, box);
        Physics.SyncTransforms();
        door.openAngle = 90f;
        bool clear = door.TryToggle();
        string positiveBlock = DescribeBlock(door);
        door.ConfigureClosedPose(closed, box);
        if (!clear)
        {
            door.openAngle = -90f;
            clear = door.TryToggle();
            string negativeBlock = DescribeBlock(door);
            door.ConfigureClosedPose(closed, box);
            if (!clear)
                throw new InvalidOperationException("Both door arcs blocked. +90: " + positiveBlock + "; -90: " + negativeBlock + ". Run Inspect opendoor swing blockers. " + geometry);
        }
        foreach (Object value in new Object[] { leaf, box, body, door })
        {
            EditorUtility.SetDirty(value); PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/InteractiveDoorSetup.txt", PathOf(leaf) + "\n" + geometry + "\nClosed pose retained; openAngle=" + door.openAngle +
            "; colliderCenter=" + box.center + "; colliderSize=" + box.size + "; hinge-side collider trimmed, visuals unchanged; solid sweep clear.");
        return door;
    }

    static void FitLeafBox(BoxCollider box, Bounds bounds, Vector3 scale)
    {
        float inset = .012f / scale.x;
        Vector3 minimum = bounds.min + Vector3.one * inset;
        Vector3 maximum = bounds.max - Vector3.one * inset;
        // The imported panel extends 5.6cm behind its hinge at this scene scale. That overhang
        // penetrates the fixed house jamb throughout the swing. Clip only the collision skin
        // just in front of the verified hinge plane; keep the authored mesh/pose and all walls.
        minimum.z = Mathf.Max(minimum.z, .01f);
        box.center = (minimum + maximum) * .5f;
        Vector3 size = maximum - minimum;
        box.size = new Vector3(Mathf.Max(size.x, .025f / scale.x), Mathf.Max(.1f, size.y), Mathf.Max(.1f, size.z));
    }

    static string PathOf(Transform value) => value.parent ? PathOf(value.parent) + "/" + value.name : value.name;
    static string DescribeBlock(InteractiveDoor door) => door.LastBlockingCollider
        ? PathOf(door.LastBlockingCollider.transform) + " [" + door.LastBlockingCollider.GetType().Name + "] depth=" + door.LastBlockingDepth.ToString("F4") + "m angle=" + door.LastBlockingAngle.ToString("F1")
        : "no collider detail (clear or query capacity exceeded)";
}
