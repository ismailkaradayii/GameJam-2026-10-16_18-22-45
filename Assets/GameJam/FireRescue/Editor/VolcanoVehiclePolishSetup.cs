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

/// <summary>Explicit, once-only authoring of the enlarged truck and its local access track.</summary>
public static class VolcanoVehiclePolishSetup
{
    const string PrefabPath = "Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab";
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string Marker = "VehiclePolish_Authoring_v1";
    const float Factor = 1.15f;
    const string ReportPath = "Logs/VolcanoVehiclePolishSetup.json";
    [Serializable] public sealed class Report
    {
        public string status, finishedUtc, backupDirectory, terrainAsset;
        public bool prefabEnlargedThisRun, sceneAlreadyAuthored;
        public float enlargement = Factor, truckWidth, truckLength, roadLength, maximumHeightChange, maximumRoadCentreSlopeDegrees;
        public int changedHeightSamples, protectedHeightSamples;
        public List<Vector3> roadCentre = new List<Vector3>();
    }
    sealed class ChildState
    {
        public Transform child; public Vector3 position, scale;
        public bool[] positionOverrides, scaleOverrides;
        public WheelCollider wheel; public float radius;
    }

    [MenuItem("GameJam/Integration/Polish enlarged truck and access track")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before vehicle authoring.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save open scenes before vehicle authoring.");
        string backup = Path.GetFullPath(".git/codex-backups/vehicle-polish-15");
        if (!Directory.Exists(".git")) throw new InvalidOperationException("Expected the main checkout's .git directory for protected original backups.");
        Directory.CreateDirectory(backup);
        Backup(PrefabPath, Path.Combine(backup, "Fire Truck.original.prefab"));
        Backup(ScenePath, Path.Combine(backup, "Volcano_Demo.original.unity"));
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        if (!player || !player.drive || !player.hose) throw new InvalidOperationException("Shared player/truck/hose links are missing.");
        var drive = player.drive;
        if (drive.transform.localScale != Vector3.one) throw new InvalidOperationException("Truck physics root must remain scale 1.");
        bool sceneDone = scene.GetRootGameObjects().Any(g => g.name == Marker);
        var report = new Report { backupDirectory = backup, sceneAlreadyAuthored = sceneDone };
        if (sceneDone && File.Exists(ReportPath))
        {
            var previous = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
            if (previous != null) { report.roadCentre = previous.roadCentre; report.roadLength = previous.roadLength; report.maximumRoadCentreSlopeDegrees = previous.maximumRoadCentreSlopeDegrees; }
        }
        var snapshots = CaptureChildren(drive.transform);

        GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        bool enlarged;
        try
        {
            var prefabDrive = prefab.GetComponent<FireTruckDrive>();
            var box = prefab.GetComponent<BoxCollider>();
            if (!prefabDrive || !box || prefab.transform.localScale != Vector3.one) throw new InvalidOperationException("Unexpected truck prefab structure.");
            enlarged = Approximately(box.size.x, 3.6f) && Approximately(box.size.z, 12.32f);
            bool already = Approximately(box.size.x, 3.6f * Factor) && Approximately(box.size.z, 12.32f * Factor);
            if (!enlarged && !already) throw new InvalidOperationException("Truck dimensions differ from both approved baseline and 15% enlargement; inspect rather than scale twice.");
            if (enlarged) Enlarge(prefabDrive);
            Tune(prefabDrive);
            prefab.GetComponent<FireHoseSystem>().startingTankLitres = 250f;
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            report.prefabEnlargedThisRun = enlarged;
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }

        Undo.RegisterFullObjectHierarchyUndo(drive.gameObject, "Polish fire truck");
        if (!sceneDone)
        {
            // Saving the prefab updates non-overridden values automatically. Capture/restore from
            // before that save so custom Volcano roof and intake offsets receive exactly one scale.
            foreach (var state in snapshots)
            {
                if (!state.child) continue;
                Vector3 position = state.child.localPosition, scale = state.child.localScale;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (enlarged || state.positionOverrides[axis]) position[axis] = state.position[axis] * Factor;
                    if (!state.wheel && (enlarged || state.scaleOverrides[axis])) scale[axis] = state.scale[axis] * Factor;
                }
                state.child.localPosition = position;
                state.child.localScale = state.wheel ? Vector3.one : scale;
                if (state.wheel && enlarged) state.wheel.radius = state.radius * Factor;
                PrefabUtility.RecordPrefabInstancePropertyModifications(state.child);
                if (state.wheel) PrefabUtility.RecordPrefabInstancePropertyModifications(state.wheel);
            }
            ScalePlayerCamera(player);
        }
        Tune(drive);
        player.hose.startingTankLitres = 250f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(drive);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.hose);

        Terrain terrain = Terrain.activeTerrains.FirstOrDefault(t => t && Contains(t, drive.transform.position));
        if (!terrain) throw new InvalidOperationException("No terrain beneath the authored truck spawn.");
        report.terrainAsset = AssetDatabase.GetAssetPath(terrain.terrainData);
        if (!report.terrainAsset.StartsWith("Assets/", StringComparison.Ordinal)) throw new InvalidOperationException("Terrain must be an editable project asset.");
        Backup(report.terrainAsset, Path.Combine(backup, "Terrain.original.asset"));
        if (!sceneDone)
        {
            SmoothAccessTrack(terrain, report);
            Vector3 spawn = drive.transform.position;
            spawn.y = terrain.SampleHeight(spawn) + terrain.transform.position.y + .6f;
            drive.transform.position = spawn;
            PrefabUtility.RecordPrefabInstancePropertyModifications(drive.transform);
            Vector3 feet = player.transform.position;
            feet.y = terrain.SampleHeight(feet) + terrain.transform.position.y + .1f;
            player.transform.position = feet;
            PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
            var marker = new GameObject(Marker) { tag = "EditorOnly" };
            SceneManager.MoveGameObjectToScene(marker, scene);
            Undo.RegisterCreatedObjectUndo(marker, "Record vehicle authoring version");
        }
        BoxCollider finalBox = drive.GetComponent<BoxCollider>();
        report.truckWidth = finalBox.size.x; report.truckLength = finalBox.size.z;
        report.status = "complete"; report.finishedUtc = DateTime.UtcNow.ToString("O");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
        Debug.Log("VOLCANO_VEHICLE_POLISH_COMPLETE: 15% truck, AWD, 250L start and preserved-terrain access track. " + ReportPath);
    }
    static bool Approximately(float a, float b) => Mathf.Abs(a - b) < .01f;
    static void Backup(string source, string target) { if (!File.Exists(target)) File.Copy(source, target, false); }
    static List<ChildState> CaptureChildren(Transform root)
    {
        var result = new List<ChildState>();
        foreach (Transform child in root)
        {
            var data = new SerializedObject(child);
            var item = new ChildState { child = child, position = child.localPosition, scale = child.localScale,
                positionOverrides = new bool[3], scaleOverrides = new bool[3], wheel = child.GetComponent<WheelCollider>() };
            for (int axis = 0; axis < 3; axis++)
            {
                string suffix = "." + "xyz"[axis];
                item.positionOverrides[axis] = data.FindProperty("m_LocalPosition" + suffix).prefabOverride;
                item.scaleOverrides[axis] = data.FindProperty("m_LocalScale" + suffix).prefabOverride;
            }
            if (item.wheel) item.radius = item.wheel.radius;
            result.Add(item);
        }
        return result;
    }
    static void Enlarge(FireTruckDrive drive)
    {
        foreach (Transform child in drive.transform)
        {
            child.localPosition *= Factor;
            var wheel = child.GetComponent<WheelCollider>();
            if (wheel) { wheel.radius *= Factor; wheel.center *= Factor; child.localScale = Vector3.one; }
            else child.localScale *= Factor;
        }
        foreach (BoxCollider box in drive.GetComponents<BoxCollider>()) { box.center *= Factor; box.size *= Factor; }
    }
    static void Tune(FireTruckDrive drive)
    {
        drive.allWheelDrive = true;
        drive.motorTorque = 4000f;
        drive.lowSpeedTorqueMultiplier = 1.5f;
        drive.torqueBoostFadeSpeedKph = 20f;
        drive.vehicleMass = 6800f;
        if (drive.body)
        {
            drive.body.mass = drive.vehicleMass;
            if (PrefabUtility.IsPartOfPrefabInstance(drive.body)) PrefabUtility.RecordPrefabInstancePropertyModifications(drive.body);
            EditorUtility.SetDirty(drive.body);
        }
        drive.maxForwardSpeedKph = 55f; drive.maxReverseSpeedKph = 14f;
        drive.brakeTorque = 22000f; drive.parkingBrakeTorque = 28000f; drive.coastBrakeTorque = 100f;
        drive.smoothPedals = true; drive.throttleResponsePerSecond = 3f; drive.throttleReleasePerSecond = 6f;
        drive.hillHoldAssist = true;
        drive.maxSteerAngle = 32f; drive.highSpeedSteerAngle = 9f; drive.steeringSpeed = 70f;
        drive.centerOfMassOffset = new Vector3(0f, -1.2f, 0f); drive.antiRollForce = 12000f;
        foreach (var wheel in drive.wheels)
        {
            wheel.suspensionDistance = .62f;
            JointSpring spring = wheel.suspensionSpring;
            spring.spring = 100000f; spring.damper = 17000f; spring.targetPosition = .5f;
            wheel.suspensionSpring = spring;
            var forward = wheel.forwardFriction; forward.stiffness = 1.6f; wheel.forwardFriction = forward;
            var sideways = wheel.sidewaysFriction; sideways.stiffness = 1.8f; wheel.sidewaysFriction = sideways;
            if (PrefabUtility.IsPartOfPrefabInstance(wheel)) PrefabUtility.RecordPrefabInstancePropertyModifications(wheel);
        }
        EditorUtility.SetDirty(drive);
    }
    static void ScalePlayerCamera(FireRescuePlayer player)
    {
        Undo.RecordObject(player, "Resize truck camera");
        player.followDistance *= Factor; player.followHeight *= Factor; player.cannonCameraLift *= Factor;
        var data = new SerializedObject(player);
        foreach (string field in new[] { "minFollowDistance", "maxFollowDistance" })
        {
            SerializedProperty value = data.FindProperty(field);
            if (value != null) value.floatValue *= Factor;
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);
    }
    static bool Contains(Terrain terrain, Vector3 p)
    {
        Vector3 local = p - terrain.transform.position, size = terrain.terrainData.size;
        return local.x >= 0f && local.z >= 0f && local.x <= size.x && local.z <= size.z;
    }

    // This centreline follows the measured, successful 296m physical drive, around the south lake bank.
    static readonly Vector3[] ApprovedRoute =
    {
        new Vector3(348.586f,0f,-604.767f), new Vector3(344.18f,0f,-621.767f), new Vector3(341.18f,0f,-636.767f),
        new Vector3(332.18f,0f,-648.767f), new Vector3(320.18f,0f,-660.767f), new Vector3(308.18f,0f,-672.767f),
        new Vector3(293.18f,0f,-681.767f), new Vector3(275.18f,0f,-681.767f), new Vector3(260.18f,0f,-684.767f),
        new Vector3(242.18f,0f,-684.767f), new Vector3(230.18f,0f,-672.767f), new Vector3(215.18f,0f,-663.767f),
        new Vector3(197.18f,0f,-663.767f), new Vector3(179.18f,0f,-663.767f), new Vector3(164.18f,0f,-660.767f),
        new Vector3(149.18f,0f,-651.767f), new Vector3(137.18f,0f,-642.767f), new Vector3(125.18f,0f,-630.767f),
        new Vector3(113.18f,0f,-618.767f), new Vector3(110.18f,0f,-615.767f), new Vector3(96f,0f,-608f)
    };
    static void SmoothAccessTrack(Terrain terrain, Report report)
    {
        TerrainData data = terrain.terrainData;
        var route = new List<Vector3>();
        for (int i = 0; i < ApprovedRoute.Length - 1; i++)
        {
            Vector3 a = ApprovedRoute[Mathf.Max(0, i - 1)], b = ApprovedRoute[i], c = ApprovedRoute[i + 1], d = ApprovedRoute[Mathf.Min(ApprovedRoute.Length - 1, i + 2)];
            int steps = Mathf.CeilToInt(Vector3.Distance(b, c) / 2f);
            for (int j = 0; j < steps; j++)
            {
                float t = j / (float)steps, t2 = t * t, t3 = t2 * t;
                Vector3 p = .5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
                p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
                route.Add(p);
            }
        }
        Vector3 end = ApprovedRoute[ApprovedRoute.Length - 1]; end.y = terrain.SampleHeight(end) + terrain.transform.position.y; route.Add(end);
        float[] distance = new float[route.Count], heights = route.Select(p => p.y).ToArray();
        for (int i = 1; i < route.Count; i++) distance[i] = distance[i - 1] + Vector2.Distance(XZ(route[i - 1]), XZ(route[i]));
        for (int i = 0; i < route.Count; i++)
        {
            float total = 0f, weights = 0f;
            for (int j = 0; j < route.Count; j++)
            {
                float separation = Mathf.Abs(distance[j] - distance[i]);
                if (separation > 15f) continue;
                float weight = 1f - separation / 16f; total += heights[j] * weight; weights += weight;
            }
            Vector3 p = route[i]; p.y = total / weights; route[i] = p;
        }
        // Limit longitudinal grades without introducing vertical ledges at the road shoulders.
        float grade = Mathf.Tan(17f * Mathf.Deg2Rad);
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 1; i < route.Count; i++) ClampGrade(route, i, i - 1, grade);
            for (int i = route.Count - 2; i >= 0; i--) ClampGrade(route, i, i + 1, grade);
        }
        var waters = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        var protectedBounds = new List<Bounds>();
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None))
            if (house.ignitionBounds) { Bounds bounds = house.ignitionBounds.bounds; bounds.Expand(16f); protectedBounds.Add(bounds); }
        foreach (var source in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
            if (source.heatCollider) { Bounds bounds = source.heatCollider.bounds; bounds.Expand(6f); protectedBounds.Add(bounds); }
        int resolution = data.heightmapResolution;
        Vector3 origin = terrain.transform.position, size = data.size;
        int xMin = Mathf.Clamp(Mathf.FloorToInt((route.Min(p => p.x) - 10f - origin.x) / size.x * (resolution - 1)), 0, resolution - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt((route.Max(p => p.x) + 10f - origin.x) / size.x * (resolution - 1)), 0, resolution - 1);
        int zMin = Mathf.Clamp(Mathf.FloorToInt((route.Min(p => p.z) - 10f - origin.z) / size.z * (resolution - 1)), 0, resolution - 1);
        int zMax = Mathf.Clamp(Mathf.CeilToInt((route.Max(p => p.z) + 10f - origin.z) / size.z * (resolution - 1)), 0, resolution - 1);
        float[,] patch = data.GetHeights(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1);
        Undo.RegisterCompleteObjectUndo(data, "Smooth protected fire truck access track");
        for (int z = 0; z < patch.GetLength(0); z++) for (int x = 0; x < patch.GetLength(1); x++)
        {
            Vector3 p = new Vector3(origin.x + (x + xMin) / (float)(resolution - 1) * size.x, 0f, origin.z + (z + zMin) / (float)(resolution - 1) * size.z);
            float nearest = float.PositiveInfinity, roadY = 0f;
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 a = XZ(route[i - 1]), delta = XZ(route[i]) - a;
                float t = Mathf.Clamp01(Vector2.Dot(XZ(p) - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                float gap = Vector2.Distance(XZ(p), a + delta * t);
                if (gap < nearest) { nearest = gap; roadY = Mathf.Lerp(route[i - 1].y, route[i].y, t); }
            }
            float core = 3f;
            for (int i = 1; i < ApprovedRoute.Length - 1; i++)
            {
                float turn = Vector3.Angle(ApprovedRoute[i] - ApprovedRoute[i - 1], ApprovedRoute[i + 1] - ApprovedRoute[i]);
                if (turn > 15f) core = Mathf.Max(core, 3f + 1.5f * Mathf.Clamp01(1f - Vector2.Distance(XZ(p), XZ(ApprovedRoute[i])) / 14f));
            }
            if (nearest >= core + 4f) continue;
            if (Protected(p, waters, protectedBounds)) { report.protectedHeightSamples++; continue; }
            float original = origin.y + patch[z, x] * size.y;
            float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, core + 4f, nearest));
            float change = Mathf.Clamp(roadY - original, -2.5f, 2.5f) * weight;
            if (Mathf.Abs(change) < .002f) continue;
            patch[z, x] = Mathf.Clamp01((original + change - origin.y) / size.y);
            report.maximumHeightChange = Mathf.Max(report.maximumHeightChange, Mathf.Abs(change)); report.changedHeightSamples++;
        }
        data.SetHeights(xMin, zMin, patch);
        EditorUtility.SetDirty(data);
        terrain.Flush();
        for (int i = 1; i < route.Count; i++)
        {
            float rise = Mathf.Abs(terrain.SampleHeight(route[i]) - terrain.SampleHeight(route[i - 1]));
            float run = Vector2.Distance(XZ(route[i]), XZ(route[i - 1]));
            report.maximumRoadCentreSlopeDegrees = Mathf.Max(report.maximumRoadCentreSlopeDegrees, Mathf.Atan2(rise, run) * Mathf.Rad2Deg);
        }
        report.roadLength = distance[distance.Length - 1]; report.roadCentre = route;
    }
    static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    static void ClampGrade(List<Vector3> route, int at, int anchor, float grade)
    {
        Vector3 p = route[at]; float change = Vector2.Distance(XZ(p), XZ(route[anchor])) * grade;
        p.y = Mathf.Clamp(p.y, route[anchor].y - change, route[anchor].y + change); route[at] = p;
    }
    [Serializable] public sealed class ClearanceRepairReport
    {
        public string status, finishedUtc, backupPath, terrainAsset;
        public int repairClusters, changedHeightSamples, protectedHeightSamples;
        public float maximumAdditionalHeightChange, maximumCentreSlopeBefore, maximumCentreSlopeAfter;
        public List<Vector3> clusterCentres = new List<Vector3>();
        public bool approachClearAfter, remainingRoadClearAfter;
    }
    [MenuItem("GameJam/Integration/Repair measured truck corridor clearance")]
    public static void RepairMeasuredClearance()
    {
        const string repairReportPath = "Logs/VolcanoRoadClearanceRepair.json";
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before road repair.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scenes before road repair.");
        if (File.Exists(repairReportPath) && JsonUtility.FromJson<ClearanceRepairReport>(File.ReadAllText(repairReportPath)).status == "complete")
            throw new InvalidOperationException("This bounded clearance repair has already run. Review its report before any further terrain edits.");
        var failed = JsonUtility.FromJson<VolcanoRouteDriveChecks.Report>(File.ReadAllText("Logs/VolcanoRouteDriveChecks.json"));
        var road = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        if (failed == null || failed.authoredRoadRejections == null || failed.authoredRoadRejections.Count == 0 || road.roadCentre.Count < 2)
            throw new InvalidOperationException("A measured authored-road clearance rejection and its road profile are required.");
        EditorSceneManager.OpenScene(ScenePath);
        Terrain terrain = Terrain.activeTerrains.FirstOrDefault(t => t && Contains(t, road.roadCentre[0]));
        if (!terrain) throw new InvalidOperationException("Authored access-track terrain is missing.");
        TerrainData data = terrain.terrainData;
        var result = new ClearanceRepairReport { terrainAsset = AssetDatabase.GetAssetPath(data) };
        result.backupPath = Path.GetFullPath(".git/codex-backups/vehicle-polish-15/Terrain.before-clearance-repair.asset");
        Backup(result.terrainAsset, result.backupPath);
        var route = road.roadCentre.Select(p => new Vector3(p.x, terrain.SampleHeight(p) + terrain.transform.position.y, p.z)).ToList();
        float[] distance = new float[route.Count];
        for (int i = 1; i < route.Count; i++) distance[i] = distance[i - 1] + Vector2.Distance(XZ(route[i - 1]), XZ(route[i]));
        var failureDistances = new List<float>();
        foreach (var rejection in failed.authoredRoadRejections)
        {
            if (rejection.type != "chassis terrain clearance") throw new InvalidOperationException("This repair handles measured terrain clearance only; review other blockers separately.");
            foreach (Vector3 point in new[] { rejection.routeProbe, rejection.samplePosition })
            {
                int nearest = 0;
                for (int i = 1; i < route.Count; i++) if (Vector2.Distance(XZ(point), XZ(route[i])) < Vector2.Distance(XZ(point), XZ(route[nearest]))) nearest = i;
                failureDistances.Add(distance[nearest]);
            }
        }
        failureDistances.Sort();
        var centres = new List<float>();
        for (int start = 0; start < failureDistances.Count;)
        {
            int end = start;
            while (end + 1 < failureDistances.Count && failureDistances[end + 1] - failureDistances[end] < 20f) end++;
            centres.Add((failureDistances[start] + failureDistances[end]) * .5f);
            start = end + 1;
        }
        if (centres.Count > 2) throw new InvalidOperationException("Expected at most the two reviewed local repair clusters.");
        result.repairClusters = centres.Count;
        foreach (float centre in centres)
        {
            int nearest = Enumerable.Range(0, route.Count).OrderBy(i => Mathf.Abs(distance[i] - centre)).First();
            result.clusterCentres.Add(route[nearest]);
        }
        // A 35m support window softens the short valley curvature beneath the 14.168m chassis.
        // Blend into the existing road over another 8m at either end; edits are limited to these two windows.
        float[] heights = route.Select(p => p.y).ToArray();
        for (int i = 0; i < route.Count; i++)
        {
            float influence = RepairInfluence(distance[i], centres);
            if (influence <= 0f) continue;
            float total = 0f, weights = 0f;
            for (int j = 0; j < route.Count; j++)
            {
                float gap = Mathf.Abs(distance[i] - distance[j]);
                if (gap > 17.5f) continue;
                float weight = 1f - gap / 18.5f; total += heights[j] * weight; weights += weight;
            }
            Vector3 p = route[i]; p.y += Mathf.Clamp(total / weights - p.y, -1.25f, 1.25f) * influence; route[i] = p;
        }
        var waters = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        var protectedBounds = new List<Bounds>();
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None))
            if (house.ignitionBounds) { Bounds bounds = house.ignitionBounds.bounds; bounds.Expand(16f); protectedBounds.Add(bounds); }
        foreach (var lava in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
            if (lava.heatCollider) { Bounds bounds = lava.heatCollider.bounds; bounds.Expand(6f); protectedBounds.Add(bounds); }
        int resolution = data.heightmapResolution;
        Vector3 origin = terrain.transform.position, size = data.size;
        float[,] patch = data.GetHeights(0, 0, resolution, resolution);
        for (int i = 1; i < route.Count; i++)
            result.maximumCentreSlopeBefore = Mathf.Max(result.maximumCentreSlopeBefore, Mathf.Atan2(Mathf.Abs(heights[i] - heights[i - 1]), distance[i] - distance[i - 1]) * Mathf.Rad2Deg);
        Undo.RegisterCompleteObjectUndo(data, "Repair two measured truck clearance transitions");
        int xMin = Mathf.Clamp(Mathf.FloorToInt((route.Min(p => p.x) - 10f - origin.x) / size.x * (resolution - 1)), 0, resolution - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt((route.Max(p => p.x) + 10f - origin.x) / size.x * (resolution - 1)), 0, resolution - 1);
        int zMin = Mathf.Clamp(Mathf.FloorToInt((route.Min(p => p.z) - 10f - origin.z) / size.z * (resolution - 1)), 0, resolution - 1);
        int zMax = Mathf.Clamp(Mathf.CeilToInt((route.Max(p => p.z) + 10f - origin.z) / size.z * (resolution - 1)), 0, resolution - 1);
        for (int z = zMin; z <= zMax; z++) for (int x = xMin; x <= xMax; x++)
        {
            Vector3 p = new Vector3(origin.x + x / (float)(resolution - 1) * size.x, 0f, origin.z + z / (float)(resolution - 1) * size.z);
            float nearest = float.PositiveInfinity, roadY = 0f, along = 0f;
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 a = XZ(route[i - 1]), delta = XZ(route[i]) - a;
                float t = Mathf.Clamp01(Vector2.Dot(XZ(p) - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                float gap = Vector2.Distance(XZ(p), a + delta * t);
                if (gap < nearest) { nearest = gap; roadY = Mathf.Lerp(route[i - 1].y, route[i].y, t); along = Mathf.Lerp(distance[i - 1], distance[i], t); }
            }
            float influence = RepairInfluence(along, centres);
            if (influence <= 0f) continue;
            float core = 3f;
            for (int i = 1; i < ApprovedRoute.Length - 1; i++)
                if (Vector3.Angle(ApprovedRoute[i] - ApprovedRoute[i - 1], ApprovedRoute[i + 1] - ApprovedRoute[i]) > 15f)
                    core = Mathf.Max(core, 3f + 1.5f * Mathf.Clamp01(1f - Vector2.Distance(XZ(p), XZ(ApprovedRoute[i])) / 14f));
            if (nearest >= core + 4f) continue;
            if (Protected(p, waters, protectedBounds)) { result.protectedHeightSamples++; continue; }
            float original = origin.y + patch[z, x] * size.y;
            float shoulder = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, core + 4f, nearest));
            float change = Mathf.Clamp(roadY - original, -1.25f, 1.25f) * shoulder * influence;
            if (Mathf.Abs(change) < .002f) continue;
            patch[z, x] = Mathf.Clamp01((original + change - origin.y) / size.y);
            result.changedHeightSamples++;
            result.maximumAdditionalHeightChange = Mathf.Max(result.maximumAdditionalHeightChange, Mathf.Abs(change));
        }
        data.SetHeights(0, 0, patch); terrain.Flush(); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        for (int i = 0; i < route.Count; i++) { Vector3 p = route[i]; p.y = terrain.SampleHeight(p) + origin.y; route[i] = p; }
        for (int i = 1; i < route.Count; i++)
            result.maximumCentreSlopeAfter = Mathf.Max(result.maximumCentreSlopeAfter, Mathf.Atan2(Mathf.Abs(route[i].y - route[i - 1].y), distance[i] - distance[i - 1]) * Mathf.Rad2Deg);
        road.roadCentre = route; road.maximumRoadCentreSlopeDegrees = result.maximumCentreSlopeAfter;
        File.WriteAllText(ReportPath, JsonUtility.ToJson(road, true));
        var audit = VolcanoRouteDriveChecks.AuditAuthoredRoadInEditMode();
        result.approachClearAfter = audit.authoredApproachClear; result.remainingRoadClearAfter = audit.authoredRemainderClear;
        result.status = "complete"; result.finishedUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(repairReportPath, JsonUtility.ToJson(result, true));
        Debug.Log("VOLCANO_ROAD_CLEARANCE_REPAIR_COMPLETE: " + result.changedHeightSamples + " samples; approach clear=" + result.approachClearAfter + "; " + repairReportPath);
    }
    static float RepairInfluence(float along, List<float> centres)
    {
        float gap = centres.Min(c => Mathf.Abs(along - c));
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17.5f, 25.5f, gap));
    }
    [Serializable] public sealed class ShoreBypassReport
    {
        public string status, finishedUtc, backupPath;
        public Vector3 centre;
        public float maximumShiftMetres = 3.5f, blendLengthMetres = 80f, maximumHeightChange;
        public int changedHeightSamples, protectedHeightSamples, protectedCoreSamples;
        public float maximumCrossSlopeBefore, maximumCrossSlopeAfter;
        public bool approachClearAfter, remainingRoadClearAfter;
    }
    [MenuItem("GameJam/Integration/Move truck corridor away from protected shore")]
    public static void RerouteProtectedShore()
    {
        const string output = "Logs/VolcanoShoreBypass.json";
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before corridor authoring.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scenes before corridor authoring.");
        if (File.Exists(output) && JsonUtility.FromJson<ShoreBypassReport>(File.ReadAllText(output)).status == "complete")
            throw new InvalidOperationException("The shoreline bypass is already authored; inspect its report before further changes.");
        var failed = JsonUtility.FromJson<VolcanoRouteDriveChecks.Report>(File.ReadAllText("Logs/VolcanoRouteDriveChecks.json"));
        var inspection = JsonUtility.FromJson<CorridorInspection>(File.ReadAllText("Logs/VolcanoStoppedCorridorInspection.json"));
        if (failed.trajectory.Count == 0 || !inspection.samples.Any(s => s.label == "estimated wheel FR" && s.protectedBy.Any(p => p.StartsWith("water:"))))
            throw new InvalidOperationException("Expected the measured right-wheel shoreline conflict.");
        var road = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        var original = road.roadCentre;
        EditorSceneManager.OpenScene(ScenePath);
        Terrain terrain = Terrain.activeTerrains.FirstOrDefault(t => t && Contains(t, inspection.stoppedRoot));
        if (!terrain) throw new InvalidOperationException("The measured corridor terrain is missing.");
        TerrainData data = terrain.terrainData;
        var result = new ShoreBypassReport { centre = inspection.stoppedRoot,
            backupPath = Path.GetFullPath(".git/codex-backups/vehicle-polish-15/Terrain.before-shore-bypass.asset") };
        Backup(AssetDatabase.GetAssetPath(data), result.backupPath);
        Backup(ReportPath, Path.GetFullPath(".git/codex-backups/vehicle-polish-15/Road.before-shore-bypass.json"));
        float[] distance = new float[original.Count];
        for (int i = 1; i < original.Count; i++) distance[i] = distance[i - 1] + Vector2.Distance(XZ(original[i]), XZ(original[i - 1]));
        int centreIndex = Enumerable.Range(0, original.Count).OrderBy(i => Vector2.Distance(XZ(original[i]), XZ(inspection.stoppedRoot))).First();
        float Influence(float along) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(18f, 40f, Mathf.Abs(along - distance[centreIndex])));
        var route = new List<Vector3>();
        for (int i = 0; i < original.Count; i++)
        {
            Vector3 tangent = original[Mathf.Min(i + 1, original.Count - 1)] - original[Mathf.Max(0, i - 1)]; tangent.y = 0f; tangent.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            Vector3 shifted = original[i] - right * (3.5f * Influence(distance[i]));
            route.Add(shifted);
            if (Influence(distance[i]) > .5f)
            {
                float delta = terrain.SampleHeight(original[i] + right * 1.8f) - terrain.SampleHeight(original[i] - right * 1.8f);
                result.maximumCrossSlopeBefore = Mathf.Max(result.maximumCrossSlopeBefore, Mathf.Atan2(Mathf.Abs(delta), 3.6f) * Mathf.Rad2Deg);
            }
        }
        var waters = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        var protectedBounds = new List<Bounds>();
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None))
            if (house.ignitionBounds) { Bounds bounds = house.ignitionBounds.bounds; bounds.Expand(16f); protectedBounds.Add(bounds); }
        foreach (var lava in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
            if (lava.heatCollider) { Bounds bounds = lava.heatCollider.bounds; bounds.Expand(6f); protectedBounds.Add(bounds); }
        int resolution = data.heightmapResolution;
        Vector3 origin = terrain.transform.position, size = data.size;
        var localRoute = route.Where((p, i) => Influence(distance[i]) > 0f).ToList();
        int xMin = Mathf.Clamp(Mathf.FloorToInt((localRoute.Min(p => p.x) - 10f - origin.x) / size.x * (resolution - 1)), 0, resolution - 1);
        int xMax = Mathf.Clamp(Mathf.CeilToInt((localRoute.Max(p => p.x) + 10f - origin.x) / size.x * (resolution - 1)), 0, resolution - 1);
        int zMin = Mathf.Clamp(Mathf.FloorToInt((localRoute.Min(p => p.z) - 10f - origin.z) / size.z * (resolution - 1)), 0, resolution - 1);
        int zMax = Mathf.Clamp(Mathf.CeilToInt((localRoute.Max(p => p.z) + 10f - origin.z) / size.z * (resolution - 1)), 0, resolution - 1);
        float[,] patch = data.GetHeights(xMin, zMin, xMax - xMin + 1, zMax - zMin + 1);
        Undo.RegisterCompleteObjectUndo(data, "Move full-width truck corridor away from protected shore");
        for (int z = 0; z < patch.GetLength(0); z++) for (int x = 0; x < patch.GetLength(1); x++)
        {
            Vector3 p = new Vector3(origin.x + (x + xMin) / (float)(resolution - 1) * size.x, 0f, origin.z + (z + zMin) / (float)(resolution - 1) * size.z);
            float nearest = float.PositiveInfinity, roadY = 0f, along = 0f;
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 a = XZ(route[i - 1]), delta = XZ(route[i]) - a;
                float t = Mathf.Clamp01(Vector2.Dot(XZ(p) - a, delta) / Mathf.Max(.001f, delta.sqrMagnitude));
                float gap = Vector2.Distance(XZ(p), a + delta * t);
                if (gap < nearest) { nearest = gap; roadY = Mathf.Lerp(route[i - 1].y, route[i].y, t); along = Mathf.Lerp(distance[i - 1], distance[i], t); }
            }
            float influence = Influence(along), core = 3f;
            if (influence <= 0f) continue;
            for (int i = 1; i < ApprovedRoute.Length - 1; i++)
                if (Vector3.Angle(ApprovedRoute[i] - ApprovedRoute[i - 1], ApprovedRoute[i + 1] - ApprovedRoute[i]) > 15f)
                    core = Mathf.Max(core, 3f + 1.5f * Mathf.Clamp01(1f - Vector2.Distance(XZ(p), XZ(ApprovedRoute[i])) / 14f));
            if (nearest >= core + 4f) continue;
            if (Protected(p, waters, protectedBounds))
            {
                result.protectedHeightSamples++;
                if (nearest <= 2.42f && influence > .9f) result.protectedCoreSamples++;
                continue;
            }
            float height = origin.y + patch[z, x] * size.y;
            float shoulder = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(core, core + 4f, nearest));
            float change = Mathf.Clamp(roadY - height, -1.25f, 1.25f) * shoulder * influence;
            if (Mathf.Abs(change) < .002f) continue;
            patch[z, x] = Mathf.Clamp01((height + change - origin.y) / size.y);
            result.changedHeightSamples++; result.maximumHeightChange = Mathf.Max(result.maximumHeightChange, Mathf.Abs(change));
        }
        data.SetHeights(xMin, zMin, patch); terrain.Flush(); EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
        for (int i = 0; i < route.Count; i++)
        {
            Vector3 p = route[i]; p.y = terrain.SampleHeight(p) + origin.y; route[i] = p;
            if (Influence(distance[i]) <= .5f) continue;
            Vector3 tangent = route[Mathf.Min(i + 1, route.Count - 1)] - route[Mathf.Max(0, i - 1)]; tangent.y = 0f; tangent.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, tangent);
            float delta = terrain.SampleHeight(p + right * 1.8f) - terrain.SampleHeight(p - right * 1.8f);
            result.maximumCrossSlopeAfter = Mathf.Max(result.maximumCrossSlopeAfter, Mathf.Atan2(Mathf.Abs(delta), 3.6f) * Mathf.Rad2Deg);
        }
        road.roadCentre = route; road.roadLength = 0f;
        for (int i = 1; i < route.Count; i++) road.roadLength += Vector2.Distance(XZ(route[i]), XZ(route[i - 1]));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(road, true));
        var audit = VolcanoRouteDriveChecks.AuditAuthoredRoadInEditMode();
        result.approachClearAfter = audit.authoredApproachClear; result.remainingRoadClearAfter = audit.authoredRemainderClear;
        result.status = "complete"; result.finishedUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(output, JsonUtility.ToJson(result, true));
        Debug.Log("VOLCANO_SHORE_BYPASS_COMPLETE: shifted3.5m; cross slope " + result.maximumCrossSlopeBefore + "→" + result.maximumCrossSlopeAfter + "; " + output);
    }
    [Serializable] public sealed class ProtectedContributor
    {
        public string kind, name;
        public Bounds actualBounds, protectedBounds;
        public bool colliderEnabled, active;
        public int readableMeshTriangles;
    }
    [Serializable] public sealed class CorridorHeightSample
    {
        public string label;
        public float along, across, slopeDegrees;
        public Vector3 position;
        public string nearestActualLava;
        public float distanceToActualLavaFootprint = -1f;
        public List<string> protectedBy = new List<string>();
    }
    [Serializable] public sealed class CorridorInspection
    {
        public string status, finishedUtc, detail;
        public Vector3 stoppedRoot, estimatedForward;
        public List<ProtectedContributor> contributors = new List<ProtectedContributor>();
        public List<CorridorHeightSample> samples = new List<CorridorHeightSample>();
    }
    sealed class LavaFootprint { public string name; public Vector3[] vertices; public int[] triangles; }
    [MenuItem("GameJam/Integration/Inspect stopped truck terrain corridor")]
    public static void InspectStoppedCorridor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before corridor inspection.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scenes before corridor inspection.");
        var previous = JsonUtility.FromJson<VolcanoRouteDriveChecks.Report>(File.ReadAllText("Logs/VolcanoRouteDriveChecks.json"));
        if (previous.trajectory.Count == 0) throw new InvalidOperationException("A real route trajectory is required.");
        var stopped = previous.trajectory[previous.trajectory.Count - 1];
        EditorSceneManager.OpenScene(ScenePath);
        Terrain terrain = Terrain.activeTerrains.FirstOrDefault(t => t && Contains(t, stopped.position));
        if (!terrain) throw new InvalidOperationException("No terrain at the stopped truck.");
        var result = new CorridorInspection { status = "read_only", stoppedRoot = stopped.position,
            detail = "Terrain samples at last driven pose. Wheel XZ estimates use logged forward/pitch without unrecorded roll; no terrain or scene edits." };
        Vector3 forward = stopped.frontPoint - stopped.position; forward.y = 0f; forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        result.estimatedForward = forward;
        void AddContributor(string kind, Component owner, Collider collider, float expansion)
        {
            if (!collider) return;
            Bounds bounds = collider.bounds, protectedBounds = bounds; protectedBounds.Expand(expansion);
            if (protectedBounds.max.x < stopped.position.x - 35f || protectedBounds.min.x > stopped.position.x + 35f ||
                protectedBounds.max.z < stopped.position.z - 35f || protectedBounds.min.z > stopped.position.z + 35f) return;
            string name = HierarchyName(owner.transform);
            result.contributors.Add(new ProtectedContributor { kind = kind, name = name,
                actualBounds = bounds, protectedBounds = protectedBounds, colliderEnabled = collider.enabled, active = collider.gameObject.activeInHierarchy });
        }
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None)) AddContributor("house", house, house.ignitionBounds, 16f);
        var lavaFootprints = new List<LavaFootprint>();
        foreach (var lava in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsSortMode.None))
        {
            AddContributor("lava", lava, lava.heatCollider, 6f);
            var contributor = result.contributors.Find(c => c.kind == "lava" && c.name == HierarchyName(lava.transform));
            if (contributor == null) continue;
            Mesh mesh = lava.surfaceMesh ? lava.surfaceMesh.sharedMesh : (lava.heatCollider as MeshCollider)?.sharedMesh;
            Transform meshTransform = lava.surfaceMesh ? lava.surfaceMesh.transform : lava.heatCollider.transform;
            if (!mesh || !mesh.isReadable) continue;
            int[] triangles = mesh.triangles; contributor.readableMeshTriangles = triangles.Length / 3;
            lavaFootprints.Add(new LavaFootprint { name = contributor.name, triangles = triangles,
                vertices = mesh.vertices.Select(meshTransform.TransformPoint).ToArray() });
        }
        var waters = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        foreach (var water in waters) AddContributor("water", water, water.volume, 8f);
        void Sample(Vector3 p, string label, float along, float across)
        {
            p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
            Vector3 normalized = p - terrain.transform.position;
            float steepness = terrain.terrainData.GetSteepness(normalized.x / terrain.terrainData.size.x, normalized.z / terrain.terrainData.size.z);
            var sample = new CorridorHeightSample { label = label, along = along, across = across, position = p, slopeDegrees = steepness };
            float nearestLava = float.PositiveInfinity;
            foreach (var footprint in lavaFootprints)
                for (int i = 0; i < footprint.triangles.Length; i += 3)
                {
                    float distance = TriangleFootprintDistance(XZ(p), XZ(footprint.vertices[footprint.triangles[i]]),
                        XZ(footprint.vertices[footprint.triangles[i + 1]]), XZ(footprint.vertices[footprint.triangles[i + 2]]));
                    if (distance < nearestLava) { nearestLava = distance; sample.nearestActualLava = footprint.name; }
                }
            if (!float.IsPositiveInfinity(nearestLava)) sample.distanceToActualLavaFootprint = nearestLava;
            foreach (var contributor in result.contributors)
                if (contributor.kind != "water" && p.x >= contributor.protectedBounds.min.x && p.x <= contributor.protectedBounds.max.x &&
                    p.z >= contributor.protectedBounds.min.z && p.z <= contributor.protectedBounds.max.z) sample.protectedBy.Add(contributor.kind + ":" + contributor.name);
            foreach (var water in waters)
            {
                bool covered = false;
                for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                    covered |= water.ContainsIntake(new Vector3(p.x + x * 4f, water.SurfaceY - .12f, p.z + z * 4f));
                if (covered) sample.protectedBy.Add("water:" + water.name);
            }
            result.samples.Add(sample);
        }
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        for (int i = 0; i < player.drive.wheels.Length; i++)
        {
            var wheel = player.drive.wheels[i];
            Vector3 local = player.drive.transform.InverseTransformPoint(wheel.transform.TransformPoint(wheel.center));
            Sample(stopped.position + right * local.x + forward * local.z, "estimated wheel " + new[] { "FL", "FR", "RL", "RR" }[i], local.z, local.x);
        }
        for (int along = -12; along <= 12; along++) for (int across = -12; across <= 12; across++)
            Sample(stopped.position + forward * (along * 1.5f) + right * (across * .75f), "grid", along * 1.5f, across * .75f);
        result.finishedUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText("Logs/VolcanoStoppedCorridorInspection.json", JsonUtility.ToJson(result, true));
        Debug.Log("VOLCANO_STOPPED_CORRIDOR_INSPECTED: " + result.contributors.Count + " nearby protected contributors; Logs/VolcanoStoppedCorridorInspection.json");
    }
    static string HierarchyName(Transform transform)
    {
        string name = transform.name;
        for (Transform parent = transform.parent; parent; parent = parent.parent) name = parent.name + "/" + name;
        return name;
    }
    static float TriangleFootprintDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float Cross(Vector2 x, Vector2 y) => x.x * y.y - x.y * y.x;
        float area = Cross(b - a, c - a);
        float ab = Cross(b - a, p - a), bc = Cross(c - b, p - b), ca = Cross(a - c, p - c);
        if (Mathf.Abs(area) > .00001f && ((ab >= 0f && bc >= 0f && ca >= 0f) || (ab <= 0f && bc <= 0f && ca <= 0f))) return 0f;
        float Edge(Vector2 start, Vector2 end)
        {
            Vector2 delta = end - start;
            return Vector2.Distance(p, start + delta * Mathf.Clamp01(Vector2.Dot(p - start, delta) / Mathf.Max(.00001f, delta.sqrMagnitude)));
        }
        return Mathf.Min(Edge(a, b), Mathf.Min(Edge(b, c), Edge(c, a)));
    }
    static bool Protected(Vector3 p, WaterSourceVolume[] waters, List<Bounds> bounds)
    {
        foreach (Bounds box in bounds) if (p.x >= box.min.x && p.x <= box.max.x && p.z >= box.min.z && p.z <= box.max.z) return true;
        foreach (var water in waters)
            for (int x = -1; x <= 1; x++) for (int z = -1; z <= 1; z++)
                if (water.ContainsIntake(new Vector3(p.x + x * 4f, water.SurfaceY - .12f, p.z + z * 4f))) return true;
        return false;
    }
}
