using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit read-only geometry checks against the final authored props and colliders.</summary>
public static class TownDressingPlacementChecks
{
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string ReportPath = "Logs/TownDressingPlacementChecks.json";
    [Serializable] public sealed class Support
    {
        public string prop, support, status, detail;
        public Vector3 baseCentre;
        public int samples, supportedSamples;
        public float minimumGap, maximumGap;
        public bool supported, inInteriorReviewFrame, unobstructedInteriorReviewRay;
    }
    [Serializable] public sealed class Blocker
    {
        public string route, collider;
        public Vector3 position;
        public float parameter, penetration;
    }
    [Serializable] public sealed class Report
    {
        public string utc, status, scope = "Read-only geometry: original furniture mesh support, saved interior camera, final decorative colliders at door/shelter approaches and sampled truck chassis poses. Does not move objects or replace the real driving/door playthrough.";
        public bool allPassed, furnitureSupported, doorSweepsClear, entrancesClear, truckRouteClear;
        public int dressingColliders, doorSamples, entranceSamples, truckSamples;
        public List<Support> furniture = new List<Support>();
        public List<Blocker> blockers = new List<Blocker>();
        public List<string> errors = new List<string>();
    }
    [Serializable] sealed class Road { public List<Vector3> roadCentre; }
    [Serializable] sealed class SettledTruck { public int settledGroundedWheels; public float settledRootAboveSupportPlaneMetres; }
    sealed class MeshSurface
    {
        public Vector3[] vertices;
        public int[] triangles;
    }

    [MenuItem("GameJam/Town Dressing/Review - Check prop support and final clearance (read only)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Run placement review in Edit Mode after compilation.");
        if (SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open Volcano_Demo before the read-only placement review.");
        var roots = SceneManager.GetActiveScene().GetRootGameObjects();
        Transform dressing = roots.Select(g => g.transform).SingleOrDefault(t => t.name == "Town Dressing");
        if (!dressing) throw new InvalidOperationException("Author Town Dressing before reviewing its placement.");
        var report = new Report { utc = DateTime.UtcNow.ToString("O") };
        Physics.SyncTransforms();
        Collider[] solids = dressing.GetComponentsInChildren<Collider>(true)
            .Where(c => c.enabled && c.gameObject.activeInHierarchy && !c.isTrigger).ToArray();
        report.dressingColliders = solids.Length;
        var originals = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Where(t => !t.IsChildOf(dressing)).ToArray();
        try { CheckFurniture(report, dressing, originals); } catch (Exception e) { report.errors.Add("Furniture: " + e.Message); }
        try { CheckDoors(report, solids, originals); } catch (Exception e) { report.errors.Add("Door/entrance: " + e.Message); }
        try { CheckTruck(report, solids, originals); } catch (Exception e) { report.errors.Add("Truck route: " + e.Message); }
        report.furnitureSupported = report.furniture.Count == 5 && report.furniture.All(s => s.supported);
        report.doorSweepsClear = report.doorSamples > 0 && report.blockers.All(b => b.route != "door sweep");
        report.entrancesClear = report.entranceSamples > 0 && report.blockers.All(b => b.route != "door approach" && b.route != "shelter approach");
        report.truckRouteClear = report.truckSamples > 0 && report.blockers.All(b => b.route != "truck chassis");
        report.allPassed = report.errors.Count == 0 && report.furnitureSupported && report.doorSweepsClear && report.entrancesClear && report.truckRouteClear;
        report.status = report.allPassed ? "passed" : "failed";
        Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        Debug.Log("TOWN_DRESSING_PLACEMENT " + report.status + ": " + ReportPath);
    }

    static void CheckFurniture(Report report, Transform dressing, Transform[] originals)
    {
        Transform area = dressing.Find("09 - First house interior accents");
        if (!area) throw new InvalidOperationException("Missing first-house interior accents.");
        string[] names = { "Table tea cup", "Second table tea cup", "Table journal", "Cabinet keepsake book", "Cabinet small plant" };
        TownDressingReviewChecks.View view = null;
        const string cameraPath = "Logs/TownDressingReview/baseline.json";
        if (File.Exists(cameraPath)) view = JsonUtility.FromJson<TownDressingReviewChecks.Baseline>(File.ReadAllText(cameraPath)).views.FirstOrDefault(v => v.name == "first-interior");
        foreach (string name in names)
        {
            var result = new Support { prop = name, minimumGap = float.PositiveInfinity, maximumGap = float.NegativeInfinity };
            report.furniture.Add(result);
            Transform prop = area.Find(name);
            if (!prop) { result.status = "missing prop"; continue; }
            MeshFilter propMesh = prop.GetComponent<MeshFilter>();
            if (!propMesh || !propMesh.sharedMesh) { result.status = "missing mesh"; continue; }
            bool table = name.StartsWith("Table", StringComparison.Ordinal) || name == "Second table tea cup";
            Transform[] candidates = originals.Where(t => (table ? t.name == "Table02" : t.name.StartsWith("Cabinet04", StringComparison.Ordinal)) &&
                t.GetComponent<MeshFilter>() && Vector3.Distance(t.position, prop.position) < 6f).ToArray();
            if (candidates.Length == 0) { result.status = "missing original support"; continue; }
            var surfaces = new List<MeshSurface>();
            foreach (Transform candidate in candidates)
                foreach (MeshFilter filter in candidate.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!filter.sharedMesh) continue;
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    for (int i = 0; i < vertices.Length; i++) vertices[i] = filter.transform.TransformPoint(vertices[i]);
                    surfaces.Add(new MeshSurface { vertices = vertices, triangles = filter.sharedMesh.triangles });
                }
            result.support = string.Join("; ", candidates.Select(Path));
            Vector3[] propVertices = propMesh.sharedMesh.vertices.Select(v => prop.TransformPoint(v)).ToArray();
            float minimumY = propVertices.Min(v => v.y);
            Vector3[] bottom = propVertices.Where(v => v.y < minimumY + .015f)
                .GroupBy(v => Mathf.RoundToInt(v.x * 1000f) + ":" + Mathf.RoundToInt(v.z * 1000f)).Select(g => g.First()).ToArray();
            result.baseCentre = new Vector3(bottom.Average(v => v.x), minimumY, bottom.Average(v => v.z));
            var probes = new List<Vector3> { result.baseCentre };
            for (int i = 0; i < bottom.Length; i += Mathf.Max(1, Mathf.CeilToInt(bottom.Length / 16f))) probes.Add(bottom[i]);
            foreach (Vector3 probe in probes)
            {
                result.samples++;
                float supportY = float.NegativeInfinity;
                foreach (MeshSurface surface in surfaces)
                    for (int i = 0; i < surface.triangles.Length; i += 3)
                    {
                        Vector3 a = surface.vertices[surface.triangles[i]], b = surface.vertices[surface.triangles[i + 1]], c = surface.vertices[surface.triangles[i + 2]];
                        if (TriangleHeight(probe, a, b, c, out float y) && y <= probe.y + .15f && y >= probe.y - 2f) supportY = Mathf.Max(supportY, y);
                    }
                if (float.IsNegativeInfinity(supportY)) continue;
                float gap = probe.y - supportY;
                result.minimumGap = Mathf.Min(result.minimumGap, gap); result.maximumGap = Mathf.Max(result.maximumGap, gap);
                if (gap >= -.025f && gap <= .09f) result.supportedSamples++;
            }
            result.supported = result.samples > 0 && result.supportedSamples == result.samples;
            result.status = result.supported ? "supported" : "unsupported or intersecting";
            if (float.IsInfinity(result.minimumGap)) result.minimumGap = 999f;
            if (float.IsInfinity(result.maximumGap)) result.maximumGap = 999f;
            if (view != null)
            {
                Vector3 point = prop.GetComponent<Renderer>().bounds.center;
                Quaternion rotation = Quaternion.LookRotation(view.target - view.position, Vector3.up);
                Vector3 local = Quaternion.Inverse(rotation) * (point - view.position);
                float vertical = Mathf.Tan(view.fieldOfView * .5f * Mathf.Deg2Rad);
                result.inInteriorReviewFrame = local.z > .04f && Mathf.Abs(local.y / local.z) <= vertical && Mathf.Abs(local.x / local.z) <= vertical * (16f / 9f);
                Vector3 ray = point - view.position;
                var first = Physics.RaycastAll(view.position, ray.normalized, ray.magnitude, ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => !(h.collider is CharacterController)).OrderBy(h => h.distance).FirstOrDefault();
                result.unobstructedInteriorReviewRay = !first.collider || first.transform.IsChildOf(prop);
                result.detail = "Support uses original mesh triangles and actual bottom vertices; camera visibility is diagnostic, not a support failure.";
            }
        }
    }
    static bool TriangleHeight(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float y)
    {
        float denominator = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
        y = 0f;
        if (Mathf.Abs(denominator) < .000001f) return false;
        float u = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / denominator;
        float v = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / denominator;
        float w = 1f - u - v;
        if (u < -.0001f || v < -.0001f || w < -.0001f) return false;
        y = u * a.y + v * b.y + w * c.y; return true;
    }

    static void CheckDoors(Report report, Collider[] solids, Transform[] originals)
    {
        foreach (InteractiveDoor door in originals.Select(t => t.GetComponent<InteractiveDoor>()).Where(d => d))
        {
            if (!door.leafCollider) throw new InvalidOperationException("Door has no fitted leaf collider.");
            int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(door.openAngle) / 2f));
            for (int i = 0; i <= steps; i++)
            {
                float angle = door.openAngle * i / steps;
                Quaternion local = door.ClosedLocalRotation * Quaternion.AngleAxis(angle, Vector3.up);
                Quaternion rotation = door.transform.parent ? door.transform.parent.rotation * local : local;
                report.doorSamples++;
                foreach (Collider solid in solids)
                    if (Physics.ComputePenetration(door.leafCollider, door.transform.position, rotation, solid, solid.transform.position, solid.transform.rotation, out _, out float depth) && depth > door.contactTolerance)
                        Record(report, "door sweep", solid, door.transform.position, angle, depth);
            }
        }
        foreach (BurnableBuilding house in originals.Select(t => t.GetComponent<BurnableBuilding>()).Where(h => h))
        {
            Transform door = house.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "opendoor") ??
                house.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Door04_pr");
            if (!door) continue;
            Renderer[] renderers = door.GetComponentsInChildren<Renderer>(true);
            Vector3 centre = renderers.Length > 0 ? renderers[0].bounds.center : door.position;
            Vector3 outward = Vector3.ProjectOnPlane(centre - house.transform.position, Vector3.up).normalized;
            CheckWalkway(report, solids, centre + outward * .4f, centre + outward * 6f, "door approach");
        }
        Transform shelter = originals.FirstOrDefault(t => t.name == "Shelter Entrance Sign & Lamp");
        if (shelter) CheckWalkway(report, solids, shelter.position - shelter.forward * 1f, shelter.position - shelter.forward * 18f, "shelter approach");
    }
    static void CheckWalkway(Report report, Collider[] solids, Vector3 start, Vector3 end, string route)
    {
        int steps = Mathf.CeilToInt(Vector3.Distance(Vector3.ProjectOnPlane(start, Vector3.up), Vector3.ProjectOnPlane(end, Vector3.up)) / .5f);
        for (int i = 0; i <= steps; i++)
        {
            Vector3 p = Vector3.Lerp(start, end, i / (float)Mathf.Max(1, steps)); p.y = Ground(p);
            report.entranceSamples++;
            foreach (Collider hit in Physics.OverlapCapsule(p + Vector3.up * .45f, p + Vector3.up * 1.65f, .4f, ~0, QueryTriggerInteraction.Ignore))
                if (solids.Contains(hit)) Record(report, route, hit, p, i * .5f, 0f);
        }
    }
    static void CheckTruck(Report report, Collider[] solids, Transform[] originals)
    {
        FireTruckDrive drive = originals.Select(t => t.GetComponent<FireTruckDrive>()).FirstOrDefault(d => d);
        if (!drive) throw new InvalidOperationException("Missing original fire truck.");
        BoxCollider body = drive.GetComponent<BoxCollider>();
        if (!body || !body.enabled) throw new InvalidOperationException("Missing enabled truck chassis collider.");
        const string roadPath = "Logs/VolcanoVehiclePolishSetup.json", settledPath = "Logs/VolcanoRouteDriveChecks.json";
        if (!File.Exists(roadPath) || !File.Exists(settledPath)) throw new InvalidOperationException("Road authoring and settled real-route evidence are required.");
        Road road = JsonUtility.FromJson<Road>(File.ReadAllText(roadPath));
        SettledTruck settled = JsonUtility.FromJson<SettledTruck>(File.ReadAllText(settledPath));
        if (road.roadCentre == null || road.roadCentre.Count < 2 || settled.settledGroundedWheels < 3)
            throw new InvalidOperationException("No usable road or settled wheel-support evidence.");
        for (int segment = 0; segment + 1 < road.roadCentre.Count; segment++)
        {
            Vector3 a = road.roadCentre[segment], b = road.roadCentre[segment + 1];
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .75f));
            Quaternion rotation = Quaternion.LookRotation((b - a).normalized, Vector3.up);
            for (int i = 0; i <= steps; i++)
            {
                Vector3 position = Vector3.Lerp(a, b, i / (float)steps);
                position.y = Ground(position) + settled.settledRootAboveSupportPlaneMetres;
                report.truckSamples++;
                foreach (Collider solid in solids)
                    if (Physics.ComputePenetration(body, position, rotation, solid, solid.transform.position, solid.transform.rotation, out _, out float depth) && depth > .01f)
                        Record(report, "truck chassis", solid, position, segment + i / (float)steps, depth);
            }
        }
    }
    static float Ground(Vector3 p)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 local = p - terrain.transform.position, size = terrain.terrainData.size;
            if (local.x >= 0 && local.z >= 0 && local.x <= size.x && local.z <= size.z) return terrain.SampleHeight(p) + terrain.transform.position.y;
        }
        throw new InvalidOperationException("No terrain under clearance sample " + p);
    }
    static void Record(Report report, string route, Collider solid, Vector3 position, float parameter, float penetration)
    {
        string path = Path(solid.transform);
        if (report.blockers.Any(b => b.route == route && b.collider == path)) return;
        report.blockers.Add(new Blocker { route = route, collider = path, position = position, parameter = parameter, penetration = penetration });
    }
    static string Path(Transform t)
    {
        string result = t.name;
        for (Transform parent = t.parent; parent; parent = parent.parent) result = parent.name + "/" + result;
        return result;
    }
}
