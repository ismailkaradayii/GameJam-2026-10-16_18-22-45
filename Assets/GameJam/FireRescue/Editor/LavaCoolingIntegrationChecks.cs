using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BKPureNature;
using GameJam.FireRescue;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

/// <summary>Exercises water, retained lava, junction supply and the finite mission together.</summary>
[InitializeOnLoad]
public static class LavaCoolingIntegrationChecks
{
    const string Pending = "LavaCoolingIntegrationChecks.Pending", Folder = "Logs/ConnectedLavaCooling";
    [Serializable] sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] sealed class Report
    {
        public string status;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    sealed class Fixture
    {
        public GameObject root;
        public ProgressiveLavaFlow flow;
        public LavaCoolingSurface cooling;
        public LavaIgnitionSource heat;
        public Vector3 At(float distance, float x = 0) => root.transform.TransformPoint(new Vector3(x, 0, distance));
    }
    sealed class FoundationProbe
    {
        public BurnableBuilding house;
        public LavaCoolingSurface surface;
        public Vector3 point, outward;
    }
    static Report report;
    static bool oldBackground;
    static readonly List<GameObject> temporary = new List<GameObject>();

    static LavaCoolingIntegrationChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Run; }
        };
    }

    [MenuItem("GameJam/Final Loop/Check connected lava cooling")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    static void Run()
    {
        report = new Report(); temporary.Clear();
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += Log;
        try
        {
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            mission.enabled = false; mission.cinematic.EndShot(); mission.story.CancelOpening(); mission.story.enabled = false;
            mission.encounter.enabled = false; mission.sequence.enabled = false;
            mission.gameFlow.enabled = false;
            var survival = mission.encounter.GetComponent<VolcanoSurvivalObjective>();
            if (survival) survival.enabled = false;
            Time.timeScale = 1;
            CheckFlow();
            CheckNarrowGap();
            CheckFoundations();
            CheckEarlyVillageBarrier(mission);
            CaptureHouseCooling(mission);
            CaptureDamCooling(mission);
            VolcanoHazardChecks.Run((name, passed, evidence) => Add("Physical lava contact: " + name, passed, evidence));
            ProgressiveLavaChecks.Run();
            Add("Existing progressive mesh, reverse flow, terrain and collision checks still pass",
                File.ReadAllText("Library/ProgressiveLavaChecks.json").Contains("\"allPassed\": true"));
        }
        catch (Exception ex) { report.errors.Add(ex.ToString()); }
        finally
        {
            Time.timeScale = 1;
            foreach (var go in temporary) if (go) Object.DestroyImmediate(go);
            temporary.Clear();
            Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
            report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
            File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true));
            EditorApplication.ExitPlaymode();
        }
    }

    static Fixture MakeFlow(string name, Vector3 origin, float width = 10, float length = 120)
    {
        var root = new GameObject(name); temporary.Add(root); root.transform.position = origin;
        var spline = root.AddComponent<SplineContainer>();
        spline.Spline = new Spline(new[] { new float3(0, 0, 0), new float3(0, 0, length) }, TangentMode.Linear);
        var collider = root.AddComponent<MeshCollider>();
        var bk = root.AddComponent<BK_Lava>();
        bk.m_Width = width; bk.m_WidthSegments = 2; bk.m_LengthSegments = 12; bk.m_BorderHeight = 0; bk.Rebuild();
        var heat = root.AddComponent<LavaIgnitionSource>();
        heat.heatCollider = collider; heat.surfaceMesh = root.GetComponent<MeshFilter>(); heat.surfaceRenderer = root.GetComponent<MeshRenderer>();
        var flow = root.AddComponent<ProgressiveLavaFlow>(); flow.source = bk; flow.ignitionSource = heat;
        flow.canBeStoppedByWater = true; flow.speed = 5; flow.Initialize();
        var cooling = root.AddComponent<LavaCoolingSurface>(); cooling.patchRadius = 13; cooling.litresPerPatch = 100;
        flow.SetDistance(length * .7f); Physics.SyncTransforms();
        return new Fixture { root = root, flow = flow, cooling = cooling, heat = heat };
    }

    static void CheckFlow()
    {
        var fixture = MakeFlow("Cooling cross-section fixture", new Vector3(18000, 300, 18000));
        var flow = fixture.flow; var cooling = fixture.cooling;
        flow.Advance(.6f);
        float hotRate = flow.FlowRate01;
        cooling.ApplyWater(fixture.At(35), cooling.litresPerPatch * .5f);
        float transitionPhase = flow.FlowPhase;
        for (int i = 0; i < 6; i++) flow.Advance(.05f);
        Add("Partial cooling uses the full 0.6 second transition instead of snapping to its target early",
            Mathf.Abs(flow.FlowRate01 - .75f) < .001f && Mathf.Abs(flow.FlowPhase - transitionPhase - .2625f) < .001f,
            $"rateAt0.3s={flow.FlowRate01:F4}, phaseAt0.3s={flow.FlowPhase - transitionPhase:F4}");
        for (int i = 0; i < 6; i++) flow.Advance(.05f);
        Add("The transition reaches half speed at 0.6 seconds with a continuous integrated texture phase",
            Mathf.Abs(flow.FlowRate01 - .5f) < .001f && Mathf.Abs(flow.FlowPhase - transitionPhase - .45f) < .001f,
            $"rateAt0.6s={flow.FlowRate01:F4}, phaseAt0.6s={flow.FlowPhase - transitionPhase:F4}");
        float distance = flow.DistanceTravelled, phase = flow.FlowPhase;
        flow.Advance(.5f);
        float partialDistance = flow.DistanceTravelled - distance, partialPhase = flow.FlowPhase - phase;
        Add("Partial cooling slows both the front and surface phase after the 0.6 second transition",
            !flow.StoppedByCooling && flow.FlowRate01 > .01f && flow.FlowRate01 < hotRate - .05f &&
            partialDistance > 0 && partialDistance < flow.speed * .5f - .05f && partialPhase > 0 && partialPhase < .49f,
            $"hotRate={hotRate:F3}, partialRate={flow.FlowRate01:F3}, distance={partialDistance:F3}, phase={partialPhase:F3}");

        cooling.ApplyWater(fixture.At(35), cooling.litresPerPatch * .5f);
        flow.RefreshCoolingBarrier();
        float heldDistance = flow.VisibleDistance;
        int heldVertices = fixture.heat.surfaceMesh.sharedMesh.vertexCount;
        float barrier = flow.CoolingBarrierDistance;
        Add("A complete cross-section records its location and blocks future movement", flow.StoppedByCooling &&
            float.IsFinite(barrier) && barrier < heldDistance && barrier > 1,
            $"barrier={barrier:F3}, visible={heldDistance:F3}");
        float before = Mathf.Max(0, barrier - 2) / flow.Length, after = Mathf.Min(flow.Length, barrier + 20) / flow.Length;
        Add("A junction before the dam retains supply and a junction after it loses supply",
            flow.GetSupply01(before) > .99f && flow.GetSupply01(after) == 0,
            $"upstream={flow.GetSupply01(before):F3}, downstream={flow.GetSupply01(after):F3}");
        var earlyBranch = MakeFlow("Branch before dam", new Vector3(18300, 300, 18000));
        var lateBranch = MakeFlow("Branch after dam", new Vector3(18600, 300, 18000));
        earlyBranch.flow.ResetFlow(); lateBranch.flow.ResetFlow();
        for (int i = 0; i < 20; i++)
        {
            earlyBranch.flow.Advance(.05f, flow.GetSupply01(before));
            lateBranch.flow.Advance(.05f, flow.GetSupply01(after));
        }
        Add("Only a branch on the supplied side can advance", earlyBranch.flow.DistanceTravelled > 1 && lateBranch.flow.DistanceTravelled == 0);

        flow.Advance(3f);
        float halfway = cooling.CoolingAt(fixture.At(70));
        Add("Downstream residual cools gradually while retaining its geometry", halfway > .3f && halfway < .8f &&
            cooling.IsHotAt(fixture.At(70)) && flow.VisibleDistance == heldDistance && fixture.heat.surfaceMesh.sharedMesh.vertexCount == heldVertices,
            $"coolingAt3s={halfway:F3}, visible={flow.VisibleDistance:F3}");
        float heldCooling = flow.DownstreamCooling01, heldPhase = flow.FlowPhase;
        Time.timeScale = 0; flow.Advance(8f); Time.timeScale = 1;
        Add("Pause freezes thermal progress, animation phase and the front", flow.DownstreamCooling01 == heldCooling &&
            flow.FlowPhase == heldPhase && flow.VisibleDistance == heldDistance);
        flow.Advance(3f); cooling.RefreshCrust();
        Add("After six seconds residual lava is cold, solid and still physically present",
            cooling.CoolingAt(fixture.At(70)) >= .999f && !cooling.IsHotAt(fixture.At(70)) &&
            !fixture.heat.IsHotAt(fixture.At(70)) && flow.VisibleDistance == heldDistance &&
            fixture.heat.heatCollider.enabled && fixture.heat.surfaceMesh.sharedMesh.vertexCount == heldVertices,
            $"coolingAt6s={cooling.CoolingAt(fixture.At(70)):F3}");
        Add("The supplied side of the dam remains hot", cooling.CoolingAt(fixture.At(2)) == 0 && fixture.heat.IsHotAt(fixture.At(2)));
        flow.Advance(90f);
        Add("Waiting cannot advance the front through an obsidian dam", flow.VisibleDistance == heldDistance && flow.FlowRate01 < .001f);
        flow.ResetFlow(); flow.SetDistance(84f); flow.Advance(.6f);
        Add("Restart clears dam, residual cooling and phase state", !flow.StoppedByCooling &&
            float.IsPositiveInfinity(flow.CoolingBarrierDistance) && flow.DownstreamCooling01 == 0 && cooling.CooledPatchCount == 0 &&
            cooling.CoolingAt(fixture.At(35)) == 0 && flow.FlowRate01 > .99f);
    }

    static void CheckNarrowGap()
    {
        var fixture = MakeFlow("Narrow hot-channel fixture", new Vector3(19000, 300, 18000));
        fixture.cooling.patchRadius = 2.5f;
        fixture.cooling.ApplyWater(fixture.At(35, -3), 100);
        fixture.cooling.ApplyWater(fixture.At(35, 2.5f), 100);
        fixture.flow.RefreshCoolingBarrier();
        Add("A half-metre hot channel between cold banks prevents a false complete dam",
            fixture.cooling.DirectCoolingAt(fixture.At(35, -.25f)) < 1 && !fixture.flow.StoppedByCooling,
            $"gapCooling={fixture.cooling.DirectCoolingAt(fixture.At(35, -.25f)):F3}, barrier={fixture.flow.CoolingBarrierDistance}");
        float before = fixture.flow.DistanceTravelled;
        for (int i = 0; i < 20; i++) fixture.flow.Advance(.05f);
        Add("Lava still moves through the narrow open channel", fixture.flow.DistanceTravelled > before);
    }

    static void CheckFoundations()
    {
        Vector3 origin = new Vector3(20000, 300, 18000);
        var lower = MakeFlow("House foundation lower lava", origin, 40);
        var overlap = MakeFlow("House foundation overlapping lava", origin + Vector3.up * .25f, 40);
        var upper = MakeFlow("Unrelated elevated lava", origin + Vector3.up * 10, 40);
        var root = new GameObject("Rotated foundation test house"); temporary.Add(root); root.SetActive(false);
        root.transform.SetPositionAndRotation(origin + new Vector3(0, 2, 35), Quaternion.Euler(0, 37, 0));
        var box = root.AddComponent<BoxCollider>(); box.size = new Vector3(8, 4, 6);
        var house = root.AddComponent<BurnableBuilding>(); house.fitFireToBuilding = false; house.ignitionBounds = box;
        root.SetActive(true); house.Ignite(); Physics.SyncTransforms();
        Vector3 centre = root.transform.TransformPoint(new Vector3(0, -2, 0));
        Vector3 margin = root.transform.TransformPoint(new Vector3(5.2f, -2, 0));
        Vector3 beyond = root.transform.TransformPoint(new Vector3(5.8f, -2, 0));
        Vector3 aabbCorner = box.bounds.max; aabbCorner.y = origin.y;
        house.ApplyWater(house.litresToExtinguish * .5f);
        Add("The same half-dose cools the burning house and its complete foundation halfway",
            Mathf.Abs(house.Heat - .5f) < .001f && Mathf.Abs(lower.cooling.CoolingAt(centre) - .5f) < .001f &&
            Mathf.Abs(lower.cooling.CoolingAt(margin) - .5f) < .001f,
            $"houseHeat={house.Heat:F3}, foundation={lower.cooling.CoolingAt(centre):F3}, margin={lower.cooling.CoolingAt(margin):F3}");
        Add("Foundation water reaches overlapping lava but not a different elevated surface",
            Mathf.Abs(overlap.cooling.CoolingAt(centre + Vector3.up * .25f) - .5f) < .001f &&
            upper.cooling.CoolingAt(centre + Vector3.up * 10) == 0);
        Add("The 1.5 metre footprint follows the rotated box rather than its world AABB",
            lower.cooling.CoolingAt(beyond) == 0 && lower.cooling.CoolingAt(aabbCorner) == 0,
            $"beyond={lower.cooling.CoolingAt(beyond):F3}, aabbCorner={lower.cooling.CoolingAt(aabbCorner):F3}");
        house.ApplyWater(house.litresToExtinguish * .5f);
        lower.cooling.RefreshCrust(); overlap.cooling.RefreshCrust();
        Add("Extinguishing the house leaves no hot foundation or hazard on either overlapping flow", house.IsRescued &&
            !house.IsBurning && !lower.cooling.IsHotAt(centre) && !overlap.cooling.IsHotAt(centre + Vector3.up * .25f) &&
            !lower.heat.IsHotAt(centre) && !overlap.heat.IsHotAt(centre + Vector3.up * .25f));
        Add("Adjacent unwatered ground remains hazardous", lower.cooling.IsHotAt(beyond) && lower.heat.IsHotAt(beyond));

        // A foundation can be protected while its route has not arrived yet.
        var delayed = MakeFlow("Late arriving foundation lava", origin + new Vector3(200, 0, 0), 40);
        delayed.flow.ResetFlow();
        var lateRoot = new GameObject("Prewatered house"); temporary.Add(lateRoot); lateRoot.SetActive(false);
        lateRoot.transform.position = origin + new Vector3(200, 2, 35);
        var lateBox = lateRoot.AddComponent<BoxCollider>(); lateBox.size = new Vector3(8, 4, 6);
        var lateHouse = lateRoot.AddComponent<BurnableBuilding>(); lateHouse.fitFireToBuilding = false; lateHouse.ignitionBounds = lateBox;
        lateRoot.SetActive(true); lateHouse.Ignite(); lateHouse.ApplyWater(lateHouse.litresToExtinguish);
        delayed.flow.SetDistance(84); delayed.cooling.RefreshCrust(); Physics.SyncTransforms();
        Add("Lava arriving after the fire was extinguished keeps the protected foundation cold",
            lateHouse.IsRescued && delayed.cooling.CoolingAt(delayed.At(35)) >= .999f && !delayed.heat.IsHotAt(delayed.At(35)));
    }

    static void CheckEarlyVillageBarrier(VolcanoMissionDirector mission)
    {
        var sequence = mission.sequence; var encounter = mission.encounter;
        sequence.missionDirector = null; sequence.evacuation = null; sequence.cinematic = null;
        sequence.InitializeSequence(); sequence.BeginEruption(true);
        for (int i = 0; i < 500 && sequence.Stage != VolcanoEruptionSequence.EruptionStage.Flowing; i++) sequence.TickSequence(.05f);
        var approach = sequence.routes.Select(r => r.flow).First(f => f && f.name == "Town Approach");
        var route = sequence.routes.First(r => r.flow == approach);
        var upstream = route.feed;
        for (int depth = 0; upstream && depth < sequence.routes.Length; depth++)
        {
            upstream.SetDistance(upstream.Length);
            upstream = sequence.routes.FirstOrDefault(r => r.flow == upstream)?.feed;
        }
        sequence.TickSequence(.1f);
        approach.SetDistance(Mathf.Min(4f, approach.Length));
        Physics.SyncTransforms();
        var cooling = approach.GetComponent<LavaCoolingSurface>();
        Add("The saved village approach supports physical obsidian barriers", approach.canBeStoppedByWater && cooling);
        // Cover the earliest exposed section using the real saved mesh, including both banks.
        for (int column = 0; column <= 12; column++)
            cooling.ApplyWater(approach.PositionAtDistance(1f, column / 12f), cooling.litresPerPatch);
        approach.RefreshCoolingBarrier();
        Add("The arrival regression begins with the main route blocked before its first spawn marker",
            approach.StoppedByCooling && approach.Progress < encounter.spawns.Where(s => s != null && s.feed == approach).Min(s => s.feedProgress),
            $"progress={approach.Progress:F4}, barrier={approach.CoolingBarrierDistance:F2}");
        float stopped = approach.VisibleDistance;
        for (int i = 0; i < 1600 && !encounter.AllWavesReleased; i++)
        {
            sequence.TickSequence(.05f); encounter.TickEncounter(.05f);
            foreach (var actor in encounter.LivingCreatures) actor.GetComponent<LavaCreatureCombat>().enabled = false;
        }
        Add("Stopping the main route immediately still releases all ten finite spirits",
            encounter.PlannedCount == 10 && encounter.SpawnedCount == 10 && encounter.AllWavesReleased && approach.VisibleDistance == stopped,
            $"spawned={encounter.SpawnedCount}/{encounter.PlannedCount}, approachVisible={approach.VisibleDistance:F2}");
        var houses = BurnableBuilding.ActiveBuildings.Where(h => h && h.ignitionBounds && h.gameObject.scene == mission.gameObject.scene).ToArray();
        foreach (var actor in encounter.LivingCreatures)
        {
            Vector3 birth = actor.transform.position;
            float closest = houses.Min(h => Vector3.Distance(birth, h.ignitionBounds.ClosestPoint(birth)));
            Add(actor.name + " emerges within reach of the village after the early dam", closest <= encounter.strandedSpiritDistance,
                $"position={birth:F2}, nearestHouse={closest:F2}");
        }
        Add("The early dam does not award unearned spirit defeats", encounter.DefeatedCount == 0 && !encounter.IsCleared);
        CheckBlockedRouteStraggler(mission);
    }

    static void CheckBlockedRouteStraggler(VolcanoMissionDirector mission)
    {
        var encounter = mission.encounter;
        if (encounter.LivingCreatures.Count == 0) return;
        var survivor = encounter.LivingCreatures.Last();
        foreach (var actor in encounter.LivingCreatures.ToArray()) if (actor != survivor) actor.ApplyDamage(actor.Health + 1);
        survivor.ApplyDamage(5);
        var combat = survivor.GetComponent<LavaCreatureCombat>(); combat.enabled = true;
        survivor.transform.position = mission.story.volcanoLookTarget.position;
        typeof(LavaCreatureCombat).GetField("initialized", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(combat, false);
        combat.TickCombat(.01f); encounter.TickEncounter(.01f);
        float health = survivor.Health; int recoveries = encounter.StragglerRecoveries;
        Vector3 stranded = survivor.transform.position;
        for (int i = 0; i < 160 && encounter.StragglerRecoveries == recoveries; i++)
        { encounter.TickEncounter(.05f); combat.TickCombat(.05f); Physics.SyncTransforms(); }
        combat.enabled = false;
        Add("The last spirit can recover from the crater even with the village inlet blocked",
            encounter.StragglerRecoveries == recoveries + 1 && Vector3.Distance(stranded, survivor.transform.position) > 100 &&
            survivor.Health == health && encounter.SpawnedCount == 10 && encounter.AliveCount == 1 && !encounter.IsCleared,
            $"recoveries={encounter.StragglerRecoveries - recoveries}, from={stranded:F2}, to={survivor.transform.position:F2}, health={survivor.Health:F1}");
    }

    static void CaptureHouseCooling(VolcanoMissionDirector mission)
    {
        foreach (var route in mission.sequence.routes)
            if (route.flow) { route.flow.ResetFlow(); route.flow.SetDistance(route.flow.Length); }
        Physics.SyncTransforms();
        var surfaces = Object.FindObjectsByType<LavaCoolingSurface>(FindObjectsSortMode.None)
            .Where(s => s && s.gameObject.scene == mission.gameObject.scene && !temporary.Contains(s.gameObject)).ToArray();
        var probes = FoundationProbes(mission.gameFlow.defenseFireBuildings, surfaces).ToArray();
        var contact = probes.FirstOrDefault(p => ClearView(p.point + p.outward * 12 + Vector3.up * 6, p.point + Vector3.up * .4f));
        if (contact == null) throw new InvalidOperationException("No visible lava contact found beside a required house foundation.");
        var house = contact.house;
        var houseProbes = probes.Where(p => p.house == house).ToArray();
        house.Ignite();
        Vector3 from = contact.point + contact.outward * 12 + Vector3.up * 6;
        Vector3 focus = contact.point + Vector3.up * .4f;
        Add("Actual mission house has exposed hot lava inside its foundation protection strip",
            houseProbes.Length > 0 && houseProbes.All(p => p.surface.GetComponent<LavaIgnitionSource>().IsHotAt(p.point)),
            $"house={house.name}, probes={houseProbes.Length}, contact={contact.point:F2}, camera={from:F2}");
        Capture("house-hot", from, focus);
        house.ApplyWater(house.litresToExtinguish * .5f);
        foreach (var surface in surfaces) surface.RefreshCrust();
        Add("The actual house's exposed lava cools halfway from its first half-dose",
            houseProbes.All(p => Mathf.Abs(p.surface.CoolingAt(p.point) - .5f) < .001f));
        Capture("house-half-cooled", from, focus);
        house.ApplyWater(house.litresToExtinguish * .5f);
        foreach (var surface in surfaces) surface.RefreshCrust();
        Add("Every sampled exposed foundation contact on the actual extinguished house is cold and harmless",
            house.IsRescued && houseProbes.All(p => p.surface.CoolingAt(p.point) >= .999f &&
                !p.surface.GetComponent<LavaIgnitionSource>().IsHotAt(p.point)));
        Capture("house-obsidian", from, focus);
    }

    static IEnumerable<FoundationProbe> FoundationProbes(IEnumerable<BurnableBuilding> houses, LavaCoolingSurface[] surfaces)
    {
        foreach (var house in houses)
        {
            if (!house || !house.ignitionBounds) continue;
            var box = house.ignitionBounds; var tr = box.transform;
            Vector3 half = box.size * .5f, scale = tr.lossyScale;
            // Sample the visible 1.5 m border, not a roof or the middle of a house.
            foreach (float margin in new[] { 1.15f, .65f, .2f })
            for (int side = 0; side < 4; side++) for (int step = 1; step < 12; step++)
            {
                float along = Mathf.Lerp(-1f, 1f, step / 12f);
                bool xSide = side < 2; float sign = side % 2 == 0 ? -1 : 1;
                Vector3 local = box.center + new Vector3(xSide ? sign * (half.x + margin / Mathf.Abs(scale.x)) : half.x * along,
                    -half.y, xSide ? half.z * along : sign * (half.z + margin / Mathf.Abs(scale.z)));
                Vector3 sample = tr.TransformPoint(local);
                Vector3 outward = tr.TransformDirection(xSide ? Vector3.right * sign : Vector3.forward * sign); outward.y = 0; outward.Normalize();
                foreach (var surface in surfaces)
                {
                    var heat = surface.GetComponent<LavaIgnitionSource>();
                    if (!heat || !heat.IsHotSurfaceActive) continue;
                    Bounds lava = heat.heatCollider.bounds;
                    if (sample.x < lava.min.x || sample.x > lava.max.x || sample.z < lava.min.z || sample.z > lava.max.z) continue;
                    Vector3 start = new Vector3(sample.x, box.bounds.max.y + 2, sample.z);
                    if (!heat.heatCollider.Raycast(new Ray(start, Vector3.down), out var hit, box.bounds.size.y + 6) ||
                        hit.point.y < box.bounds.min.y - .25f || hit.point.y > box.bounds.min.y + 1.5f) continue;
                    yield return new FoundationProbe { house = house, surface = surface, point = hit.point, outward = outward };
                }
            }
        }
    }

    static bool ClearView(Vector3 from, Vector3 to)
    {
        Vector3 ray = to - from;
        if (Physics.CheckSphere(from, .25f, ~0, QueryTriggerInteraction.Ignore)) return false;
        return !Physics.Raycast(from, ray.normalized, ray.magnitude - .5f, ~0, QueryTriggerInteraction.Ignore);
    }

    static void CaptureDamCooling(VolcanoMissionDirector mission)
    {
        var flow = mission.sequence.routes.Select(r => r.flow).First(f => f && f.name == "Town Approach");
        flow.ResetFlow(); flow.SetDistance(Mathf.Min(90, flow.Length)); Physics.SyncTransforms();
        var cooling = flow.GetComponent<LavaCoolingSurface>();
        Vector3 point = default, from = default; bool found = false;
        foreach (float distance in new[] { 55f, 40f, 70f, 25f })
        {
            point = flow.PositionAtDistance(distance);
            Vector3 tangent = (flow.PositionAtDistance(distance + 1) - flow.PositionAtDistance(distance - 1)).normalized;
            Vector3 across = Vector3.Cross(Vector3.up, tangent).normalized;
            foreach (float side in new[] { 1f, -1f })
            {
                from = point + across * (18 * side) + Vector3.up * 10 - tangent * 8;
                if (!ClearView(from, point + Vector3.up * .4f)) continue;
                found = true; break;
            }
            if (found) break;
        }
        if (!found) throw new InvalidOperationException("No exposed approach segment available for dam inspection.");
        Vector3 focus = point + Vector3.up * .4f;
        Capture("dam-hot", from, focus);
        cooling.ApplyWater(point, cooling.litresPerPatch * .5f); flow.Advance(.6f); cooling.RefreshCrust();
        Capture("dam-half-cooled", from, focus);
        cooling.ApplyWater(point, cooling.litresPerPatch * .5f); flow.Advance(6f); cooling.RefreshCrust();
        Capture("dam-obsidian", from, focus);
        Add("The photographed real village dam has stopped its route and cooled its residual",
            flow.StoppedByCooling && flow.DownstreamCooling01 >= .999f && !cooling.IsHotAt(point),
            $"contact={point:F2}, camera={from:F2}, barrier={flow.CoolingBarrierDistance:F2}");
    }

    static void Capture(string name, Vector3 from, Vector3 to)
    {
        Directory.CreateDirectory(EvacuationSceneAudit.Folder);
        EvacuationSceneAudit.Capture("connected-lava-" + name, from, to);
        File.Copy(EvacuationSceneAudit.Folder + "/connected-lava-" + name + ".png", Folder + "/" + name + ".png", true);
    }
    static void Add(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
}
