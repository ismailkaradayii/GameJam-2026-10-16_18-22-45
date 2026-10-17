using System;
using System.Collections.Generic;
using System.IO;
using BKPureNature;
using GameJam.FireRescue;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

public static class ProgressiveLavaChecks
{
    [Serializable]
    private sealed class Check
    {
        public string name;
        public bool passed;
        public string evidence;
    }

    [Serializable]
    private sealed class Report
    {
        public bool allPassed;
        public string finishedUtc;
        public string error;
        public List<Check> checks = new List<Check>();
    }

    [MenuItem("GameJam/Fire Rescue/Check progressive lava (Play Mode)", true)]
    private static bool CanRun() => EditorApplication.isPlaying;

    [MenuItem("GameJam/Fire Rescue/Check progressive lava (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying)
            throw new InvalidOperationException("Progressive lava checks require Play Mode.");
        var report = new Report();
        var objects = new List<GameObject>();
        TerrainData terrainData = null;
        Mesh combinedStandIn = null;
        Mesh recookProbe = null;
        try
        {
            GameObject fixture = new GameObject("Progressive lava isolated check");
            objects.Add(fixture);
            fixture.transform.SetPositionAndRotation(new Vector3(18000f, 300f, 18000f), Quaternion.Euler(0f, 23f, 0f));
            fixture.transform.localScale = new Vector3(2f, 1f, 2f);
            SplineContainer container = fixture.AddComponent<SplineContainer>();
            container.Spline = new Spline(new[] { new float3(0f, 0f, 0f), new float3(0f, 0f, 20f) }, TangentMode.Linear);
            MeshCollider collider = fixture.AddComponent<MeshCollider>();
            BK_Lava bk = fixture.AddComponent<BK_Lava>();
            bk.m_Width = 4f;
            bk.m_WidthSegments = 2;
            bk.m_LengthSegments = 4;
            bk.m_BorderHeight = 0f;
            bk.Rebuild();
            MeshFilter filter = fixture.GetComponent<MeshFilter>();
            MeshRenderer renderer = fixture.GetComponent<MeshRenderer>();
            Mesh fullMesh = filter.sharedMesh;
            Vector3[] originalVertices = fullMesh.vertices;
            int[] originalIndices = fullMesh.triangles;
            LavaIgnitionSource heat = fixture.AddComponent<LavaIgnitionSource>();
            heat.heatCollider = collider;
            heat.surfaceMesh = filter;
            heat.surfaceRenderer = renderer;
            BurnableBuilding house = MakeHouse(objects, fixture.transform.position + fixture.transform.forward * 12f);

            ProgressiveLavaFlow flow = fixture.AddComponent<ProgressiveLavaFlow>();
            flow.source = bk;
            flow.ignitionSource = heat;
            flow.speed = 5f;
            flow.Initialize();
            Physics.SyncTransforms();
            heat.ScanNow();
            Add(report, "hidden_before_eruption", !renderer.enabled && !collider.enabled &&
                filter.sharedMesh == null && collider.sharedMesh == null && !heat.heatEnabled && !house.IsBurning,
                "renderer=" + renderer.enabled + "; collider=" + collider.enabled + "; heat=" + heat.heatEnabled);
            Add(report, "world_space_length", Mathf.Abs(flow.Length - 40f) < 0.02f,
                "Length=" + flow.Length + "; expected40m from 20m spline with zScale2");
            Add(report, "generator_disabled_without_losing_full_mesh", !bk.enabled && bk.m_Mesh == fullMesh,
                "sourceEnabled=" + bk.enabled + "; retainedOriginalMesh=" + (bk.m_Mesh == fullMesh));

            flow.SetDistance(5.5f);
            Physics.SyncTransforms();
            Vector3 expectedFront = fixture.transform.position + fixture.transform.forward * 5.5f;
            Add(report, "interpolated_front_in_world_metres", Vector3.Distance(flow.FrontPosition, expectedFront) < 0.01f,
                "Front=" + flow.FrontPosition + "; expected=" + expectedFront);
            Add(report, "renderer_and_collision_share_clipped_mesh", renderer.enabled && collider.enabled && heat.heatEnabled &&
                filter.sharedMesh == collider.sharedMesh && filter.sharedMesh != fullMesh &&
                filter.sharedMesh.vertexCount < fullMesh.vertexCount,
                "visibleVertices=" + filter.sharedMesh.vertexCount + "; fullVertices=" + fullMesh.vertexCount);
            Vector3 across = fixture.transform.right * 2f;
            bool behindFront = HitsLava(collider, fixture.transform.position + fixture.transform.forward * 5f + across);
            bool aheadFront = HitsLava(collider, fixture.transform.position + fixture.transform.forward * 6f + across);
            Add(report, "physics_stops_at_visible_front", behindFront && !aheadFront,
                "rayAt5m=" + behindFront + "; rayAt6m=" + aheadFront + "; front5.5m");
            heat.ScanNow();
            Add(report, "unreached_house_stays_unlit", !house.IsBurning, "House12m; front5.5m; heat=" + house.Heat);

            flow.SetDistance(12.5f);
            Physics.SyncTransforms();
            heat.ScanNow();
            Add(report, "arriving_lava_ignites_house", house.IsBurning, "House12m; front12.5m; heat=" + house.Heat);
            Add(report, "source_mesh_unchanged_during_clipping", SameVertices(fullMesh.vertices, originalVertices) &&
                SameIndices(fullMesh.triangles, originalIndices), "Original full mesh arrays compared exactly");

            flow.SetDistance(float.PositiveInfinity);
            Add(report, "full_arrival_restores_all_geometry_in_own_mesh", flow.HasArrived && Mathf.Abs(flow.Progress - 1f) < 0.0001f &&
                SameVertices(filter.sharedMesh.vertices, originalVertices) && SameIndices(filter.sharedMesh.triangles, originalIndices),
                "Distance=" + flow.DistanceTravelled + "; vertices=" + filter.sharedMesh.vertexCount);

            flow.reverse = true;
            flow.ResetFlow();
            Add(report, "reverse_reset_hidden_at_far_end", !renderer.enabled && !collider.enabled && !heat.heatEnabled &&
                Vector3.Distance(flow.FrontPosition, fixture.transform.position + fixture.transform.forward * 40f) < 0.01f,
                "Front=" + flow.FrontPosition);
            flow.SetDistance(5.5f);
            bool reverseBoundBeforeSync = collider.sharedMesh == filter.sharedMesh;
            bool reverseHeatRefsMatch = heat.heatCollider == collider && heat.surfaceMesh == filter;
            Physics.SyncTransforms();
            expectedFront = fixture.transform.position + fixture.transform.forward * 34.5f;
            behindFront = HitsLava(collider, fixture.transform.position + fixture.transform.forward * 35f + across);
            aheadFront = HitsLava(collider, fixture.transform.position + fixture.transform.forward * 34f + across);
            Vector3 reverseCentre = fixture.transform.TransformPoint(filter.sharedMesh.bounds.center);
            bool reverseCentreTop = HitsLava(collider, reverseCentre);
            bool reverseCentreBottom = collider.Raycast(new Ray(reverseCentre - Vector3.up * 2f, Vector3.up), out _, 4f);
            Vector3[] reverseVertices = filter.sharedMesh.vertices;
            int[] reverseTriangles = filter.sharedMesh.triangles;
            Vector3 firstNormal = Vector3.Cross(reverseVertices[reverseTriangles[1]] - reverseVertices[reverseTriangles[0]],
                reverseVertices[reverseTriangles[2]] - reverseVertices[reverseTriangles[0]]).normalized;
            Add(report, "reverse_front_and_collision", Vector3.Distance(flow.FrontPosition, expectedFront) < 0.01f &&
                behindFront && !aheadFront, "Front=" + flow.FrontPosition + "; ray35m=" + behindFront + "; ray34m=" + aheadFront +
                "; enabled=" + collider.enabled + "; sameMesh=" + (collider.sharedMesh == filter.sharedMesh) +
                "; colliderBounds=" + collider.bounds + "; meshBounds=" + filter.sharedMesh.bounds +
                "; firstNormal=" + firstNormal + "; centreTop=" + reverseCentreTop + "; centreBottom=" + reverseCentreBottom +
                "; firstVertex=" + reverseVertices[0] + "; lastVertex=" + reverseVertices[reverseVertices.Length - 1] +
                "; boundBeforeSync=" + reverseBoundBeforeSync + "; heatRefsMatch=" + reverseHeatRefsMatch);
            if (!behindFront)
            {
                collider.sharedMesh = null;
                collider.sharedMesh = filter.sharedMesh;
                Physics.SyncTransforms();
                bool rebound = HitsLava(collider, reverseCentre);
                recookProbe = UnityEngine.Object.Instantiate(filter.sharedMesh);
                collider.sharedMesh = recookProbe;
                Physics.SyncTransforms();
                bool freshMesh = HitsLava(collider, reverseCentre);
                Add(report, "reverse_failure_recook_diagnostic", false,
                    "Explicit rebind centre=" + rebound + "; fresh clone centre=" + freshMesh +
                    "; clone assigned=" + (collider.sharedMesh == recookProbe) + "; clone collider bounds=" + collider.bounds);
            }
            flow.SetDistance(flow.Length);
            Add(report, "reverse_full_mesh_keeps_original_winding", SameVertices(filter.sharedMesh.vertices, originalVertices) &&
                SameIndices(filter.sharedMesh.triangles, originalIndices), "Full forward/reverse meshes both retain original vertex and triangle order");

            flow.reverse = false;
            flow.ResetFlow();
            flow.Advance(0.5f);
            Add(report, "speed_is_metres_per_second", Mathf.Abs(flow.DistanceTravelled - 2.5f) < 0.001f &&
                Vector3.Distance(flow.FrontPosition, fixture.transform.position + fixture.transform.forward * 2.5f) < 0.01f,
                "speed5 * dt0.5; actual=" + flow.DistanceTravelled);
            Mesh sharedRuntimeMesh = filter.sharedMesh;
            Vector3[] beforeThrottledStep = sharedRuntimeMesh.vertices;
            flow.Advance(0.01f);
            Add(report, "mesh_recook_is_throttled", Mathf.Abs(flow.DistanceTravelled - 2.55f) < 0.001f &&
                Mathf.Abs(flow.VisibleDistance - 2.5f) < 0.001f && SameVertices(sharedRuntimeMesh.vertices, beforeThrottledStep),
                "simulated=" + flow.DistanceTravelled + "; visible=" + flow.VisibleDistance);
            flow.Advance(0.1f);
            Add(report, "throttled_mesh_catches_up", Mathf.Abs(flow.VisibleDistance - flow.DistanceTravelled) < 0.001f,
                "simulated=" + flow.DistanceTravelled + "; visible=" + flow.VisibleDistance);
            float beforeInvalid = flow.DistanceTravelled;
            flow.Advance(float.NaN);
            flow.Advance(float.PositiveInfinity);
            flow.Advance(-1f);
            flow.SetDistance(float.NaN);
            Add(report, "invalid_steps_do_not_corrupt_distance", flow.DistanceTravelled == beforeInvalid,
                "Distance=" + flow.DistanceTravelled);
            flow.SetDistance(-1f);
            Add(report, "negative_seek_is_hidden_zero", flow.DistanceTravelled == 0f && !renderer.enabled &&
                !collider.enabled && !heat.heatEnabled && filter.sharedMesh == null, "Distance=" + flow.DistanceTravelled);

            UnityEngine.Object.DestroyImmediate(flow);
            Add(report, "removing_flow_restores_original_components", bk.enabled && renderer.enabled && collider.enabled &&
                heat.heatEnabled && filter.sharedMesh == fullMesh && collider.sharedMesh == fullMesh &&
                heat.heatCollider == collider && heat.surfaceMesh == filter,
                "source=" + bk.enabled + "; renderer=" + renderer.enabled + "; collider=" + collider.enabled + "; fullMesh=" + (filter.sharedMesh == fullMesh));

            terrainData = new TerrainData { heightmapResolution = 33, size = new Vector3(128f, 8f, 128f) };
            var heights = new float[33, 33];
            for (int z = 0; z < 33; z++)
                for (int x = 0; x < 33; x++)
                    heights[z, x] = 0.2f + x * 0.004f + z * 0.003f;
            terrainData.SetHeights(0, 0, heights);
            GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
            objects.Add(terrainObject);
            terrainObject.name = "Progressive lava private terrain check";
            terrainObject.transform.position = fixture.transform.position + new Vector3(-32f, -5f, -32f);
            Terrain terrain = terrainObject.GetComponent<Terrain>();
            ProgressiveLavaFlow conformed = fixture.AddComponent<ProgressiveLavaFlow>();
            conformed.source = bk;
            conformed.ignitionSource = heat;
            conformed.conformToTerrain = terrain;
            conformed.groundClearance = 0.3f;
            conformed.Initialize();
            conformed.SetDistance(conformed.Length);
            float maxHeightError = 0f;
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 world = fixture.transform.TransformPoint(vertex);
                float expectedY = terrain.SampleHeight(world) + terrain.transform.position.y + 0.3f;
                maxHeightError = Mathf.Max(maxHeightError, Mathf.Abs(world.y - expectedY));
            }
            Add(report, "optional_terrain_conform_uses_private_mesh", maxHeightError < 0.005f &&
                SameVertices(fullMesh.vertices, originalVertices) && filter.sharedMesh == collider.sharedMesh,
                "Max world height error=" + maxHeightError + "; original source mesh unchanged");
            UnityEngine.Object.DestroyImmediate(conformed);

            bk.enabled = false;
            combinedStandIn = new Mesh { name = "Progressive lava combined-filter stand-in" };
            combinedStandIn.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward };
            combinedStandIn.triangles = new[] { 0, 2, 1 };
            filter.sharedMesh = combinedStandIn;
            ProgressiveLavaFlow retainedSource = fixture.AddComponent<ProgressiveLavaFlow>();
            retainedSource.source = bk;
            retainedSource.Initialize();
            retainedSource.SetDistance(retainedSource.Length);
            Add(report, "bk_owned_grid_preferred_over_combined_filter", Mathf.Abs(retainedSource.Length - 40f) < 0.02f &&
                SameVertices(filter.sharedMesh.vertices, originalVertices) && bk.m_Mesh == fullMesh,
                "Filter began as a three-vertex non-grid; BK retained its complete grid");
            UnityEngine.Object.DestroyImmediate(retainedSource);
            filter.sharedMesh = fullMesh;

            int originalWidthSegments = bk.m_WidthSegments;
            bk.m_WidthSegments = originalWidthSegments + 1; // Deliberately stale metadata, without rebuilding the source.
            ProgressiveLavaFlow regenerated = fixture.AddComponent<ProgressiveLavaFlow>();
            regenerated.source = bk;
            regenerated.Initialize();
            regenerated.SetDistance(regenerated.Length);
            int expectedVertices = originalVertices.Length / (originalWidthSegments + 1) * (bk.m_WidthSegments + 1);
            Add(report, "legacy_grid_mismatch_rebuilds_private_reference", Mathf.Abs(regenerated.Length - 40f) < 0.02f &&
                filter.sharedMesh.vertexCount == expectedVertices && bk.m_Mesh == fullMesh &&
                SameVertices(fullMesh.vertices, originalVertices) && SameIndices(fullMesh.triangles, originalIndices) &&
                bk.m_WidthSegments == originalWidthSegments + 1,
                "Canonical vertices=" + filter.sharedMesh.vertexCount + "; expected=" + expectedVertices +
                "; original vertices=" + fullMesh.vertexCount + "; source metadata and mesh retained");
            UnityEngine.Object.DestroyImmediate(regenerated);
            bk.m_WidthSegments = originalWidthSegments;
            bk.enabled = true;

            GameObject empty = new GameObject("Progressive lava zero length check");
            objects.Add(empty);
            empty.transform.position = new Vector3(18000f, 300f, 18100f);
            empty.AddComponent<SplineContainer>();
            BK_Lava emptyBK = empty.AddComponent<BK_Lava>();
            ProgressiveLavaFlow emptyFlow = empty.AddComponent<ProgressiveLavaFlow>();
            emptyFlow.source = emptyBK;
            emptyFlow.Initialize();
            emptyFlow.Advance(1f);
            Add(report, "zero_length_route_is_safe_and_complete", emptyFlow.Length == 0f && emptyFlow.HasArrived &&
                !empty.GetComponent<MeshRenderer>().enabled && !empty.GetComponent<MeshCollider>().enabled,
                "Length=" + emptyFlow.Length + "; HasArrived=" + emptyFlow.HasArrived);
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
        }
        finally
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            if (terrainData != null) UnityEngine.Object.DestroyImmediate(terrainData);
            if (combinedStandIn != null) UnityEngine.Object.DestroyImmediate(combinedStandIn);
            if (recookProbe != null) UnityEngine.Object.DestroyImmediate(recookProbe);
            report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
            foreach (Check check in report.checks) report.allPassed &= check.passed;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ProgressiveLavaChecks.json")),
                JsonUtility.ToJson(report, true));
            Debug.Log("Progressive lava checks: " + report.allPassed + ". Evidence: Library/ProgressiveLavaChecks.json");
        }
    }

    private static BurnableBuilding MakeHouse(List<GameObject> objects, Vector3 position)
    {
        GameObject house = new GameObject("Progressive lava arrival target");
        objects.Add(house);
        house.transform.position = position;
        BoxCollider bounds = house.AddComponent<BoxCollider>();
        bounds.size = new Vector3(1f, 2f, 1f);
        BurnableBuilding building = house.AddComponent<BurnableBuilding>();
        building.ignitionBounds = bounds;
        return building;
    }

    private static bool HitsLava(MeshCollider collider, Vector3 surfacePoint) =>
        collider.Raycast(new Ray(surfacePoint + Vector3.up * 2f, Vector3.down), out _, 4f);

    private static bool SameVertices(Vector3[] a, Vector3[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    private static bool SameIndices(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    private static void Add(Report report, string name, bool passed, string evidence) =>
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
}
