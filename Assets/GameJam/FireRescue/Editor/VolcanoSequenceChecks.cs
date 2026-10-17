using System;
using System.Collections.Generic;
using System.IO;
using BKPureNature;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>Isolated deterministic Play Mode checks; never triggers the scene's actual eruption.</summary>
public static class VolcanoSequenceChecks
{
    [Serializable] sealed class Check { public string name; public bool passed; public string evidence; }
    [Serializable] sealed class Report
    {
        public bool allPassed;
        public string error, finishedUtc;
        public List<Check> checks = new List<Check>();
    }
    sealed class HouseState { public BurnableBuilding house; public float heat; }
    sealed class SequenceState { public VolcanoEruptionSequence sequence; public VolcanoEruptionSequence.EruptionStage stage; public float elapsed; }

    [MenuItem("GameJam/Fire Rescue/Check volcano sequence (Play Mode)", true)]
    static bool CanRun() => EditorApplication.isPlaying;

    [MenuItem("GameJam/Fire Rescue/Check volcano sequence (Play Mode)")]
    public static void Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Volcano sequence checks require Play Mode.");
        var report = new Report();
        GameObject fixture = null;
        var realHouses = new List<HouseState>();
        var realSequences = new List<SequenceState>();
        foreach (BurnableBuilding house in BurnableBuilding.ActiveBuildings)
            if (house) realHouses.Add(new HouseState { house = house, heat = house.Heat });
        foreach (VolcanoEruptionSequence actual in UnityEngine.Object.FindObjectsByType<VolcanoEruptionSequence>(FindObjectsSortMode.None))
            realSequences.Add(new SequenceState { sequence = actual, stage = actual.Stage, elapsed = actual.StageElapsed });
        try
        {
            fixture = new GameObject("VolcanoSequenceChecks temporary fixture");
            fixture.transform.position = new Vector3(24000f, 500f, 24000f);
            ProgressiveLavaFlow trunk = CreateFlow(fixture.transform, "Test crater trunk", Vector3.zero);
            ProgressiveLavaFlow branch = CreateFlow(fixture.transform, "Test town branch", Vector3.forward * 10f);
            BurnableBuilding trunkHouse = CreateHouse(fixture.transform, "House ahead of trunk front", new Vector3(0f, 0f, 8f));
            BurnableBuilding branchHouse = CreateHouse(fixture.transform, "House ahead of branch front", new Vector3(0f, 0f, 18f));
            BurnableBuilding poolHouse = CreateHouse(fixture.transform, "House at pool edge", new Vector3(8.4f, 0f, 20f));
            GameObject poolObject = Primitive(fixture.transform, "Test fed lava pool", PrimitiveType.Cube, new Vector3(6f, 0f, 20f));
            poolObject.transform.localScale = new Vector3(6f, .3f, 6f);
            var poolSource = poolObject.AddComponent<LavaIgnitionSource>();
            poolSource.heatCollider = poolObject.GetComponent<BoxCollider>();
            poolSource.heatCollider.isTrigger = true;
            var legacy = Child(fixture.transform, "Suppressed legacy particles", Vector3.zero).AddComponent<ParticleSystem>();
            var ambient = Child(fixture.transform, "Fed branch particles", Vector3.forward * 20f).AddComponent<ParticleSystem>();
            legacy.Play(); ambient.Play();
            Light trunkGlow = Child(fixture.transform, "Trunk light at five metres", new Vector3(0f, 2f, 5f)).AddComponent<Light>();
            Light branchGlow = Child(fixture.transform, "Branch light at eight metres", new Vector3(0f, 2f, 18f)).AddComponent<Light>();
            trunkGlow.type = branchGlow.type = LightType.Point;
            trunkGlow.range = branchGlow.range = 20f;
            trunkGlow.enabled = branchGlow.enabled = true;

            GameObject water = Primitive(fixture.transform, "Unrelated water stays visible", PrimitiveType.Plane, Vector3.right * 30f);
            water.transform.localScale = new Vector3(2f, 1f, 2f);
            Renderer waterRenderer = water.GetComponent<Renderer>();
            Collider waterCollider = water.GetComponent<Collider>();
            Mesh waterMesh = water.GetComponent<MeshFilter>().sharedMesh;
            Vector3 waterScale = water.transform.localScale;
            var waterVolume = Child(water.transform, "Unrelated water intake", Vector3.down).AddComponent<WaterSourceVolume>();
            waterVolume.volume = waterVolume.GetComponent<BoxCollider>();
            waterVolume.volume.isTrigger = true;
            waterVolume.waterSurface = water.transform;

            var sequence = Child(fixture.transform, "Test sequence", Vector3.zero).AddComponent<VolcanoEruptionSequence>();
            sequence.enabled = false; sequence.showHud = false; sequence.autoErupt = false;
            sequence.warningSeconds = 1f; sequence.lavaReleaseDelay = 1.5f; sequence.enableCameraShake = false;
            sequence.effects = Child(fixture.transform, "Test eruption effects", Vector3.zero).AddComponent<VolcanoEruptionEffects>();
            sequence.routes = new[]
            {
                new VolcanoEruptionSequence.Route { flow = trunk },
                new VolcanoEruptionSequence.Route { flow = branch, feed = trunk, feedProgress = 1f }
            };
            sequence.pools = new[] { new VolcanoEruptionSequence.Pool { source = poolSource, feed = branch, feedProgress = 1f, fillSeconds = 2f } };
            sequence.ambientEffects = new[] { new VolcanoEruptionSequence.AmbientEffect { particles = ambient, feed = branch, feedProgress = 1f } };
            sequence.lavaLights = new[]
            {
                new VolcanoEruptionSequence.LavaLight { light = trunkGlow, feed = trunk, feedProgress = .5f },
                new VolcanoEruptionSequence.LavaLight { light = branchGlow, feed = branch, feedProgress = .8f }
            };
            sequence.suppressedLegacyEffects = new[] { legacy };
            sequence.InitializeSequence(); Physics.SyncTransforms(); Scan(sequence);
            Add(report, "initialized_lava_hidden_and_cold", Hidden(trunk) && Hidden(branch) && !poolSource.heatEnabled && !poolSource.surfaceRenderer.enabled && !poolSource.heatCollider.enabled,
                Evidence(sequence, trunk, branch) + "; poolHeat=" + poolSource.heatEnabled);
            Add(report, "initial_houses_cold_and_effects_clear", !trunkHouse.IsBurning && !branchHouse.IsBurning && !poolHouse.IsBurning && !legacy.isPlaying && !ambient.isPlaying && !sequence.effects.IsErupting, "No heat or active eruption particles before trigger.");
            Add(report, "initial_lava_lights_are_hidden", !trunkGlow.enabled && !branchGlow.enabled && !trunkHouse.IsBurning && !branchHouse.IsBurning, "Trunk glow=" + trunkGlow.enabled + "; branch glow=" + branchGlow.enabled);
            Tick(sequence, 3f);
            Add(report, "manual_wait_has_no_heat_or_flow", sequence.Stage == VolcanoEruptionSequence.EruptionStage.Waiting && Hidden(trunk) && !trunkHouse.IsBurning, Evidence(sequence, trunk, branch));
            bool accepted = sequence.BeginEruption(), duplicate = sequence.BeginEruption();
            Add(report, "manual_trigger_is_accepted_only_once", accepted && !duplicate && sequence.Stage == VolcanoEruptionSequence.EruptionStage.Warning, "BeginEruption=" + accepted + "; duplicate=" + duplicate);
            Tick(sequence, .7f);
            Add(report, "warning_precedes_erupting_and_lava", sequence.Stage == VolcanoEruptionSequence.EruptionStage.Warning && Hidden(trunk) && !sequence.effects.IsErupting, Evidence(sequence, trunk, branch));
            TickUntil(sequence, () => sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting);
            Add(report, "eruption_effects_start_before_flow", sequence.effects.IsErupting && Hidden(trunk) && Hidden(branch), Evidence(sequence, trunk, branch));
            Tick(sequence, 1f);
            Add(report, "lava_release_delay_keeps_routes_cold", sequence.Stage == VolcanoEruptionSequence.EruptionStage.Erupting && Hidden(trunk) && !trunkHouse.IsBurning, Evidence(sequence, trunk, branch));
            Add(report, "lava_lights_stay_off_before_flow_release", !trunkGlow.enabled && !branchGlow.enabled, "Stage=" + sequence.Stage + "; trunk glow=" + trunkGlow.enabled + "; branch glow=" + branchGlow.enabled);
            TickUntil(sequence, () => sequence.Stage == VolcanoEruptionSequence.EruptionStage.Flowing);

            Tick(sequence, 3.4f);
            Add(report, "clipped_front_cannot_ignite_downstream_house", trunk.VisibleDistance > 0f && trunk.VisibleDistance < 7.5f && !trunkHouse.IsBurning && branch.VisibleDistance == 0f,
                Evidence(sequence, trunk, branch) + "; trunkHeat=" + trunkHouse.Heat + "; meshMaxZ=" + trunk.GetComponent<MeshRenderer>().bounds.max.z);
            Add(report, "reached_light_illuminates_without_preigniting_houses", trunkGlow.enabled && !branchGlow.enabled && !trunkHouse.IsBurning && !branchHouse.IsBurning,
                "Trunk front=" + trunk.VisibleDistance + "; trunk glow=" + trunkGlow.enabled + "; branch glow=" + branchGlow.enabled + "; house heat=" + trunkHouse.Heat);
            TickUntil(sequence, () => trunk.VisibleDistance >= 8.1f);
            Add(report, "visible_trunk_contact_ignites_first_house", trunkHouse.IsBurning && trunk.ignitionSource.heatEnabled && !branchHouse.IsBurning, "Trunk house heat=" + trunkHouse.Heat + "; branch house heat=" + branchHouse.Heat);
            Add(report, "branch_waits_for_visible_feed_end", !trunk.HasArrived && branch.VisibleDistance == 0f && Hidden(branch) && !poolSource.heatEnabled && !ambient.isPlaying, Evidence(sequence, trunk, branch));
            TickUntil(sequence, () => trunk.HasArrived && branch.VisibleDistance > 0f);
            Add(report, "feed_arrival_releases_branch", trunk.VisibleDistance >= trunk.Length && branch.VisibleDistance > 0f && branch.VisibleDistance < 2f, Evidence(sequence, trunk, branch));
            TickUntil(sequence, () => branch.VisibleDistance >= 6.6f);
            Add(report, "branch_heat_stays_behind_visible_front", !branchHouse.IsBurning && !poolSource.heatEnabled && !poolHouse.IsBurning, Evidence(sequence, trunk, branch));
            Add(report, "ahead_of_front_light_stays_off", !branchGlow.enabled && !branchHouse.IsBurning, "Branch front=" + branch.VisibleDistance + "; light threshold=" + branch.Length * .8f + "; branch glow=" + branchGlow.enabled);
            TickUntil(sequence, () => branch.VisibleDistance >= 8.1f);
            Add(report, "visible_branch_contact_ignites_next_house", branchHouse.IsBurning && !poolSource.heatEnabled, "Branch house heat=" + branchHouse.Heat + "; poolHeat=" + poolSource.heatEnabled);
            Add(report, "arrived_front_enables_branch_light", branchGlow.enabled && branch.VisibleDistance >= branch.Length * .8f, "Branch front=" + branch.VisibleDistance + "; branch glow=" + branchGlow.enabled);
            TickUntil(sequence, () => branch.HasArrived);
            Add(report, "pool_and_ambient_wait_for_branch_then_start_small", poolSource.heatEnabled && poolSource.surfaceRenderer.enabled && poolSource.heatCollider.enabled && poolObject.transform.localScale.x > 0f && poolObject.transform.localScale.x < 1f && !poolHouse.IsBurning && ambient.isPlaying,
                "Pool scale=" + poolObject.transform.localScale + "; edge house heat=" + poolHouse.Heat + "; particles=" + ambient.isPlaying);
            Tick(sequence, .6f);
            float growingScale = poolObject.transform.localScale.x;
            Add(report, "growing_pool_does_not_heat_future_footprint", growingScale > 0f && growingScale < 3f && !poolHouse.IsBurning, "Pool X scale=" + growingScale + "; edge house heat=" + poolHouse.Heat);
            Tick(sequence, 2f);
            Add(report, "full_pool_actual_contact_ignites_edge_house", Mathf.Abs(poolObject.transform.localScale.x - 6f) < .001f && poolHouse.IsBurning, "Pool scale=" + poolObject.transform.localScale + "; edge house heat=" + poolHouse.Heat);
            branchHouse.ApplyWater(branchHouse.litresToExtinguish); Scan(sequence);
            Add(report, "water_extinguishes_house_under_active_lava", !branchHouse.IsBurning && branchHouse.IgnitionBlocked && branch.ignitionSource.heatEnabled, "House heat=" + branchHouse.Heat + "; protected=" + branchHouse.IgnitionBlocked);
            Add(report, "unlisted_static_water_is_unchanged", waterRenderer.enabled && waterCollider.enabled && water.transform.localScale == waterScale && water.GetComponent<MeshFilter>().sharedMesh == waterMesh && !water.GetComponent<LavaIgnitionSource>(), "Water renderer/collider/mesh/scale preserved; never registered as lava.");

            var automatic = Child(fixture.transform, "Automatic countdown check", Vector3.zero).AddComponent<VolcanoEruptionSequence>();
            automatic.enabled = false; automatic.showHud = false; automatic.enableCameraShake = false;
            automatic.autoErupt = true; automatic.autoEruptAfter = 1f; automatic.warningSeconds = .6f;
            automatic.InitializeSequence(); automatic.TickSequence(.7f);
            Add(report, "automatic_countdown_waits", automatic.Stage == VolcanoEruptionSequence.EruptionStage.Waiting && automatic.Countdown > 0f, "Stage=" + automatic.Stage + "; countdown=" + automatic.Countdown);
            automatic.TickSequence(.5f);
            Add(report, "automatic_countdown_begins_warning", automatic.Stage == VolcanoEruptionSequence.EruptionStage.Warning, "Stage=" + automatic.Stage + "; stageElapsed=" + automatic.StageElapsed);
            bool unchanged = true;
            foreach (HouseState state in realHouses) unchanged &= state.house && state.house.Heat == state.heat;
            foreach (SequenceState state in realSequences) unchanged &= state.sequence && state.sequence.Stage == state.stage && state.sequence.StageElapsed == state.elapsed;
            Add(report, "actual_scene_houses_and_sequences_untouched", unchanged, "Original houses=" + realHouses.Count + "; original sequences=" + realSequences.Count);
        }
        catch (Exception error) { report.error = error.ToString(); }
        finally
        {
            if (fixture) UnityEngine.Object.DestroyImmediate(fixture);
            report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
            foreach (Check check in report.checks) report.allPassed &= check.passed;
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/VolcanoSequenceChecks.json", JsonUtility.ToJson(report, true));
            Debug.Log("Volcano sequence checks allPassed=" + report.allPassed + ". Evidence: Logs/VolcanoSequenceChecks.json");
        }
    }

    static ProgressiveLavaFlow CreateFlow(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = Child(parent, name, localPosition);
        var container = go.AddComponent<SplineContainer>();
        var spline = new Spline();
        spline.Add(new BezierKnot(new Unity.Mathematics.float3(0f, 0f, 0f)), TangentMode.Linear);
        spline.Add(new BezierKnot(new Unity.Mathematics.float3(0f, 0f, 10f)), TangentMode.Linear);
        container.Spline = spline;
        var source = go.AddComponent<BK_Lava>();
        source.m_Container = container; source.m_Width = 2f; source.m_WidthSegments = 2; source.m_LengthSegments = 4; source.m_BorderHeight = 0f;
        source.m_Material = AssetDatabase.LoadAssetAtPath<Material>("Assets/BK/PureNature_Volcano/Textures/Lava/Materials/Lava_stream.mat");
        source.Rebuild();
        var ignition = go.AddComponent<LavaIgnitionSource>();
        var flow = go.AddComponent<ProgressiveLavaFlow>();
        flow.source = source; flow.ignitionSource = ignition; flow.speed = 2f; flow.meshUpdateInterval = .1f;
        return flow;
    }
    static BurnableBuilding CreateHouse(Transform parent, string name, Vector3 localPosition)
    {
        GameObject go = Child(parent, name, localPosition);
        var box = go.AddComponent<BoxCollider>(); box.center = Vector3.up * .3f; box.size = new Vector3(.8f, 1f, .8f); box.isTrigger = true;
        var house = go.AddComponent<BurnableBuilding>(); house.ignitionBounds = box; house.startBurning = false; return house;
    }
    static GameObject Child(Transform parent, string name, Vector3 localPosition)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = localPosition; return go;
    }
    static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 localPosition)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false); go.transform.localPosition = localPosition; return go;
    }
    static bool Hidden(ProgressiveLavaFlow flow) => flow.VisibleDistance == 0f && !flow.GetComponent<MeshRenderer>().enabled && !flow.ignitionSource.heatEnabled && !flow.ignitionSource.heatCollider.enabled;
    static void Tick(VolcanoEruptionSequence sequence, float seconds) { sequence.TickSequence(seconds); Physics.SyncTransforms(); Scan(sequence); }
    static void Scan(VolcanoEruptionSequence sequence)
    {
        foreach (var route in sequence.routes) route.flow.ignitionSource.ScanNow();
        foreach (var pool in sequence.pools) pool.source.ScanNow();
    }
    static void TickUntil(VolcanoEruptionSequence sequence, Func<bool> reached)
    {
        for (int i = 0; i < 400 && !reached(); i++) Tick(sequence, .1f);
        if (!reached()) throw new TimeoutException("Isolated sequence did not reach the requested state.");
    }
    static string Evidence(VolcanoEruptionSequence s, ProgressiveLavaFlow trunk, ProgressiveLavaFlow branch) => "Stage=" + s.Stage + "; elapsed=" + s.StageElapsed + "; trunk=" + trunk.VisibleDistance + "/" + trunk.Length + "; branch=" + branch.VisibleDistance + "/" + branch.Length;
    static void Add(Report report, string name, bool passed, string evidence) => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
}
