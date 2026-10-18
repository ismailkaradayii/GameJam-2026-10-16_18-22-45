using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using BKPureNature;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

/// <summary>Same actual-house workload before and after optimization; timings are CPU costs, not claimed rendered FPS.</summary>
[InitializeOnLoad]
public static class HouseCoolingPerformanceChecks
{
    const string Pending = "HouseCoolingPerformanceChecks.Pending", Folder = "Logs/HouseCoolingPerformance";
    [Serializable] public sealed class Check { public string name, evidence; public bool passed, skipped; }
    [Serializable] public sealed class Timing
    {
        public string name;
        public int calls;
        public double totalMs, maximumMs, p95Ms;
        public long allocatedBytes;
        [NonSerialized] public readonly List<double> samples = new List<double>();
        public void Add(long start, long bytes)
        {
            double ms = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
            allocatedBytes += GC.GetAllocatedBytesForCurrentThread() - bytes;
            calls++; totalMs += ms; maximumMs = Math.Max(maximumMs, ms); samples.Add(ms);
        }
        public void Finish() { samples.Sort(); p95Ms = samples.Count == 0 ? 0 : samples[(int)((samples.Count - 1) * .95)]; }
    }
    [Serializable] public sealed class Report
    {
        public string status, variant;
        public string method = "Saved-scene defense houses and retained lava meshes; 180 equal water doses per house at 60 Hz, hydraulic/mesh cadence 8 simulation frames, followed by six seconds of residual cooling. Each CPU stage is measured separately. Simulation runs synchronously to isolate the lava hot path from rendering; no rendered-FPS claim.";
        public int waterDoses, simulatedFrames, liveFlowCount, inputVertices, inputTriangles;
        public int crustMeshRebuilds, crustColliderCooks, sourceMeshReads;
        public bool allocationCounterAvailable;
        public string allocationMeasurement;
        public List<string> houses = new List<string>();
        public List<Timing> timings = new List<Timing>();
        public List<Scenario> scenarios = new List<Scenario>();
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    [Serializable] public sealed class Scenario
    {
        public string name, method, baselineComparison;
        public int simulatedFrames, houseWaterDoses, directWaterDoses, waterPatchCount, outputTriangles;
        public int crustMeshRebuilds, crustColliderCooks, sourceMeshReads;
        public List<Timing> timings = new List<Timing>();
        public List<string> surfaceRegions = new List<string>();
    }
    sealed class Contact { public BurnableBuilding house; public LavaCoolingSurface surface; public Vector3 point; }
    static readonly FieldInfo Patches = typeof(LavaCoolingSurface).GetField("patches", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo CrustDirty = typeof(LavaCoolingSurface).GetField("dirty", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo CoolingDirty = typeof(ProgressiveLavaFlow).GetField("coolingDirty", BindingFlags.NonPublic | BindingFlags.Instance);
    static Report report;
    static HouseCoolingPerformanceChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(Pending, "") != "")
                EditorApplication.delayCall += Run;
        };
    }
    [MenuItem("GameJam/Final Loop/Check house cooling performance baseline")]
    public static void Baseline() => Begin("baseline");
    [MenuItem("GameJam/Final Loop/Check house cooling performance optimized")]
    public static void Optimized() => Begin("optimized");
    static void Begin(string variant)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/" + variant + ".json", "{\"status\":\"STARTING\"}");
        SessionState.SetString(Pending, variant); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        string variant = SessionState.GetString(Pending, ""); SessionState.SetString(Pending, "");
        report = new Report { variant = variant };
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        var allocationProbe = new byte[4096];
        long allocationProbeBytes = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        GC.KeepAlive(allocationProbe);
        report.allocationCounterAvailable = allocationProbeBytes >= allocationProbe.Length;
        report.allocationMeasurement = report.allocationCounterAvailable ? "Current-thread managed allocation counter calibrated with a 4096-byte allocation." :
            "UNAVAILABLE: this Unity runtime reported no calibrated managed allocation. allocatedBytes values must not be interpreted as zero allocations or compared for GC savings.";
        bool oldBackground = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += Log;
        try
        {
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            mission.enabled = false; mission.cinematic.EndShot(); mission.story.CancelOpening(); mission.story.enabled = false;
            mission.encounter.enabled = false; mission.sequence.enabled = false; mission.gameFlow.enabled = false;
            Time.timeScale = 1;
            var routes = mission.sequence.routes.Where(r => r != null && r.flow).ToArray();
            foreach (var route in routes) { route.flow.ResetFlow(); route.flow.SetDistance(route.flow.Length); }
            Physics.SyncTransforms();
            var surfaces = Object.FindObjectsByType<LavaCoolingSurface>(FindObjectsSortMode.None)
                .Where(s => s.gameObject.scene == mission.gameObject.scene).ToArray();
            var houses = mission.gameFlow.defenseFireBuildings.Where(h => h && h.ignitionBounds).Distinct().ToArray();
            var contacts = Contacts(houses, surfaces).ToArray();
            report.liveFlowCount = routes.Length;
            foreach (var route in routes)
            {
                var mesh = route.flow.GetComponent<MeshFilter>().sharedMesh;
                if (mesh) { report.inputVertices += mesh.vertexCount; report.inputTriangles += (int)mesh.GetIndexCount(0) / 3; }
            }
            Add("Actual mission houses and live lava contacts are available", houses.Length >= 3 && contacts.Length > 0,
                "houses=" + houses.Length + " contacts=" + contacts.Length + " flows=" + routes.Length);
            Timing water = Metric("Water delivery"), barrier = Metric("Hydraulic cross-sections"), advance = Metric("Flow/residual simulation"),
                crust = Metric("Crust meshes and collider cooking"), frame = Metric("Combined simulated frame");
            var ignition = surfaces.Select(s => s.GetComponent<LavaIgnitionSource>()).Where(s => s).ToArray();
            Timing scanBefore = Metric("Ignition scan before watering"), scanDuring = Metric("Ignition scan during watering"),
                scanAfter = Metric("Ignition scan after rescue");
            for (int i = 0; i < 24; i++) Scan(ignition, scanBefore);
            // Warm up uncooled flow stepping outside the timed workload.
            foreach (var route in routes) route.flow.Advance(.1f, 1f);
            int tick = 0;
            foreach (var house in houses)
            {
                report.houses.Add(house.name);
                typeof(BurnableBuilding).GetProperty("MissionFireAllowed").SetValue(house, true);
                house.Ignite();
                Add(house.name + " starts as a real burning house", house.IsBurning);
                float dose = house.litresToExtinguish / 180f;
                for (int n = 0; n < 180; n++)
                {
                    Tick(house, dose, tick++, routes, surfaces, water, barrier, advance, crust, frame);
                    if (n % 15 == 0) Scan(ignition, scanDuring);
                    if (n == 89)
                    {
                        var local = contacts.Where(c => c.house == house).ToArray();
                        Add(house.name + " records the actual half water dose", Math.Abs(house.FoundationCooling01 - .5f) < .001f);
                        ContactCheck(house.name + " foundation lava follows the actual half water dose", local,
                            c => c.surface.CoolingAt(c.point) >= .499f);
                    }
                }
                foreach (var surface in surfaces) if (HasRegions(surface)) surface.RefreshCrust();
                Physics.SyncTransforms();
                var cold = contacts.Where(c => c.house == house).ToArray();
                Add(house.name + " ends rescued", house.IsRescued);
                ContactCheck(house.name + " has harmless foundation lava", cold, c =>
                    c.surface.CoolingAt(c.point) >= .999f && !c.surface.GetComponent<LavaIgnitionSource>().IsHotAt(c.point));
                ContactCheck(house.name + " cold cover has the same walkable visible mesh", cold, c => c.surface.GetComponentsInChildren<MeshCollider>()
                    .Any(mc => mc != c.surface.GetComponent<LavaIgnitionSource>().heatCollider && mc.enabled && mc.sharedMesh &&
                        mc.Raycast(new Ray(c.point + Vector3.up, Vector3.down), out _, 2f)));
            }
            for (int n = 0; n < 360; n++) Tick(null, 0, tick++, routes, surfaces, water, barrier, advance, crust, frame);
            for (int i = 0; i < 24; i++) Scan(ignition, scanAfter);
            report.crustMeshRebuilds = surfaces.Sum(s => Counter(s, "CrustMeshRebuildCount"));
            report.crustColliderCooks = surfaces.Sum(s => Counter(s, "CrustColliderCookCount"));
            report.sourceMeshReads = surfaces.Sum(s => Counter(s, "SourceMeshReadCount"));
            foreach (var timing in report.timings) timing.Finish();
            Add("Simulation exercises completed houses and their remaining cooling", report.waterDoses == houses.Length * 180 && report.simulatedFrames == houses.Length * 180 + 360);
            CheckCombinedWater(routes, surfaces, houses, contacts);
            CheckGrowingPatches(routes, surfaces);
            if (variant == "optimized" && File.Exists(Folder + "/baseline.json"))
            {
                var baseline = JsonUtility.FromJson<Report>(File.ReadAllText(Folder + "/baseline.json"));
                var before = baseline.timings.FirstOrDefault(t => t.name == frame.name);
                Add("Comparison uses the same scene mesh and dose workload", baseline.inputVertices == report.inputVertices &&
                    baseline.inputTriangles == report.inputTriangles && baseline.waterDoses == report.waterDoses);
                if (before != null) Add("Cooling CPU work improves on the captured baseline", frame.totalMs < before.totalMs * .7,
                    $"before={before.totalMs:F2}ms after={frame.totalMs:F2}ms; p95 before={before.p95Ms:F3} after={frame.p95Ms:F3}");
                foreach (var scenario in report.scenarios)
                {
                    var previous = baseline.scenarios?.FirstOrDefault(s => s.name == scenario.name);
                    if (previous == null) { Skip(scenario.name + " baseline comparison", "No expanded baseline was captured."); continue; }
                    Add(scenario.name + " comparison uses identical dose counts", previous.houseWaterDoses == scenario.houseWaterDoses &&
                        previous.directWaterDoses == scenario.directWaterDoses && previous.simulatedFrames == scenario.simulatedFrames);
                    var oldCost = previous.timings.First(t => t.name == "Combined simulated frame");
                    var newCost = scenario.timings.First(t => t.name == "Combined simulated frame");
                    // Keep individual stress timings as observations: a single baseline cannot
                    // establish a significant throughput change at sub-millisecond frame costs.
                    scenario.baselineComparison = $"Total CPU before={oldCost.totalMs:F2}ms after={newCost.totalMs:F2}ms; " +
                        $"p95 before={oldCost.p95Ms:F3}ms after={newCost.p95Ms:F3}ms. Single-run comparison, not rendered FPS.";
                }
            }
            // Separate from the measured saved-scene workload, so baseline/optimized timings
            // retain identical inputs while a moving front also exercises cache correctness.
            if (variant == "optimized")
            {
                CheckMovingFront(false); CheckMovingFront(true);
                CheckCoolingRegression(false); CheckCoolingRegression(true);
                FireCombatChecks.Run();
                Add("Existing ignition, wet protection and water cannon checks pass",
                    File.ReadAllText("Logs/FireCombatChecks.json").Contains("\"allPassed\": true"));
            }
        }
        catch (Exception ex) { report.errors.Add(ex.ToString()); }
        finally
        {
            foreach (var timing in report.timings) timing.Finish();
            foreach (var scenario in report.scenarios) foreach (var timing in scenario.timings) timing.Finish();
            report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
            Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
            File.WriteAllText(Folder + "/" + variant + ".json", JsonUtility.ToJson(report, true));
            EditorApplication.ExitPlaymode();
        }
    }
    static void Tick(BurnableBuilding house, float dose, int tick, VolcanoEruptionSequence.Route[] routes,
        LavaCoolingSurface[] surfaces, Timing water, Timing barrier, Timing advance, Timing crust, Timing frame)
    {
        long frameBytes = GC.GetAllocatedBytesForCurrentThread(), frameStart = Stopwatch.GetTimestamp(), start, bytes;
        if (house)
        {
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp(); house.ApplyWater(dose); water.Add(start, bytes); report.waterDoses++;
        }
        if (tick % 8 == 0)
        {
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            foreach (var route in routes)
            {
                var cooling = route.flow.GetComponent<LavaCoolingSurface>();
                if (cooling && cooling.HasDirectCooling && (bool)CoolingDirty.GetValue(route.flow)) route.flow.RefreshCoolingBarrier();
            }
            barrier.Add(start, bytes);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
        foreach (var route in routes) route.flow.Advance(1f / 60f, route.feed ? route.feed.GetSupply01(route.feedProgress) : 1f);
        advance.Add(start, bytes);
        if (tick % 8 == 0)
        {
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            foreach (var surface in surfaces) if (HasRegions(surface) && (bool)CrustDirty.GetValue(surface)) surface.RefreshCrust();
            crust.Add(start, bytes);
        }
        frame.Add(frameStart, frameBytes); report.simulatedFrames++;
    }
    static bool HasRegions(LavaCoolingSurface surface) => ((ICollection)Patches.GetValue(surface)).Count > 0;
    static void Scan(LavaIgnitionSource[] sources, Timing timing)
    {
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        foreach (var source in sources) source.ScanNow();
        timing.Add(start, bytes);
    }
    static void ContactCheck(string name, Contact[] contacts, Func<Contact, bool> check)
    {
        if (contacts.Length == 0) { Skip(name, "No measured lava contact at this saved-scene foundation; no coverage assertion made."); return; }
        Add(name, contacts.All(check), "measuredContacts=" + contacts.Length);
    }
    static void ResetRoutes(VolcanoEruptionSequence.Route[] routes)
    {
        foreach (var route in routes) { route.flow.ResetFlow(); route.flow.SetDistance(route.flow.Length); }
        Physics.SyncTransforms();
    }
    static Scenario NewScenario(string name, string method)
    {
        var scenario = new Scenario { name = name, method = method };
        foreach (string stage in new[] { "Water delivery", "Hydraulic cross-sections", "Flow/residual simulation", "Crust meshes and collider cooking", "Combined simulated frame" })
            scenario.timings.Add(new Timing { name = stage });
        report.scenarios.Add(scenario); return scenario;
    }
    static void ScenarioTick(Scenario scenario, BurnableBuilding house, float houseDose, LavaCoolingSurface direct, Vector3 point,
        float directDose, VolcanoEruptionSequence.Route[] routes, LavaCoolingSurface[] surfaces)
    {
        int tick = scenario.simulatedFrames;
        long frameBytes = GC.GetAllocatedBytesForCurrentThread(), frameStart = Stopwatch.GetTimestamp();
        long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
        if (house) { house.ApplyWater(houseDose); scenario.houseWaterDoses++; }
        if (direct) { direct.ApplyWater(point, directDose); scenario.directWaterDoses++; }
        scenario.timings[0].Add(start, bytes);
        if (tick % 8 == 0)
        {
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            foreach (var route in routes)
            {
                var cooling = route.flow.GetComponent<LavaCoolingSurface>();
                if (cooling && cooling.HasDirectCooling && (bool)CoolingDirty.GetValue(route.flow)) route.flow.RefreshCoolingBarrier();
            }
            scenario.timings[1].Add(start, bytes);
        }
        bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
        foreach (var route in routes) route.flow.Advance(1f / 60f, route.feed ? route.feed.GetSupply01(route.feedProgress) : 1f);
        scenario.timings[2].Add(start, bytes);
        if (tick % 8 == 0)
        {
            bytes = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
            foreach (var surface in surfaces) if (HasRegions(surface) && (bool)CrustDirty.GetValue(surface)) surface.RefreshCrust();
            scenario.timings[3].Add(start, bytes);
        }
        scenario.timings[4].Add(frameStart, frameBytes); scenario.simulatedFrames++;
    }
    static void CaptureScenario(Scenario scenario, LavaCoolingSurface[] surfaces)
    {
        scenario.crustMeshRebuilds = surfaces.Sum(s => Counter(s, "CrustMeshRebuildCount"));
        scenario.crustColliderCooks = surfaces.Sum(s => Counter(s, "CrustColliderCookCount"));
        scenario.sourceMeshReads = surfaces.Sum(s => Counter(s, "SourceMeshReadCount"));
        foreach (var surface in surfaces)
        {
            int water = 0, foundations = 0, downstream = 0;
            foreach (var patch in (IEnumerable)Patches.GetValue(surface))
            {
                var type = patch.GetType();
                string kind = type.GetField("kind").GetValue(patch).ToString();
                if (kind == "Water") { scenario.waterPatchCount++; water++; }
                else if (kind == "Foundation") foundations++; else downstream++;
                var mesh = type.GetField("mesh").GetValue(patch) as Mesh;
                if (mesh) for (int i = 0; i < mesh.subMeshCount; i++) scenario.outputTriangles += (int)mesh.GetIndexCount(i) / 3;
            }
            if (water + foundations + downstream > 0)
                scenario.surfaceRegions.Add(surface.name + $": water={water} foundation={foundations} downstream={downstream}");
        }
        foreach (var timing in scenario.timings) timing.Finish();
    }
    static void CheckCombinedWater(VolcanoEruptionSequence.Route[] routes, LavaCoolingSurface[] surfaces,
        BurnableBuilding[] houses, Contact[] contacts)
    {
        // Fresh logical houses keep the authored collider transforms and retained lava meshes.
        // No renderer cloning or fixture creation is charged to the measured water hot path.
        foreach (var house in houses) house.enabled = false;
        ResetRoutes(routes);
        var scenario = NewScenario("House plus direct lava water", "180 doses per measured contacting house, with the same dose also reaching its actual lava contact; no tank is consumed by this isolated CPU fixture. Geometry uses the saved scene, all authored routes fully revealed; 8-frame refresh cadence plus 360 residual steps.");
        var fixtures = new List<GameObject>();
        try
        {
            foreach (var original in houses)
            {
                var contact = contacts.FirstOrDefault(c => c.house == original);
                if (contact == null) continue;
                var go = new GameObject("Combined water fixture " + original.name); go.SetActive(false); fixtures.Add(go);
                var sourceBox = original.ignitionBounds;
                go.transform.SetPositionAndRotation(sourceBox.transform.position, sourceBox.transform.rotation);
                go.transform.localScale = sourceBox.transform.lossyScale;
                var box = go.AddComponent<BoxCollider>(); box.center = sourceBox.center; box.size = sourceBox.size;
                var house = go.AddComponent<BurnableBuilding>(); house.fitFireToBuilding = false; house.ignitionBounds = box;
                house.litresToExtinguish = original.litresToExtinguish; go.SetActive(true); house.Ignite(); Physics.SyncTransforms();
                float dose = house.litresToExtinguish / 180f;
                for (int n = 0; n < 180; n++) ScenarioTick(scenario, house, dose, contact.surface, contact.point, dose, routes, surfaces);
                Add(original.name + " combined water rescues the house and preserves a cold contact", house.IsRescued &&
                    contact.surface.CoolingAt(contact.point) >= .999f && !contact.surface.GetComponent<LavaIgnitionSource>().IsHotAt(contact.point));
            }
            for (int n = 0; n < 360; n++) ScenarioTick(scenario, null, 0, null, default, 0, routes, surfaces);
            foreach (var surface in surfaces) if (HasRegions(surface)) surface.RefreshCrust();
            Physics.SyncTransforms();
            Add("Combined workload creates direct-water regions", surfaces.Any(s => WaterPatchCount(s) > 0));
            int builds = surfaces.Sum(s => s.CrustMeshRebuildCount), cooks = surfaces.Sum(s => s.CrustColliderCookCount);
            foreach (var contact in contacts)
                for (int n = 0; n < 20; n++) contact.surface.ApplyWater(contact.point, 1f);
            foreach (var surface in surfaces) if (HasRegions(surface)) surface.RefreshCrust();
            Add("Repeated spray onto settled foundations neither reheats nor rebuilds rock", builds == surfaces.Sum(s => s.CrustMeshRebuildCount) &&
                cooks == surfaces.Sum(s => s.CrustColliderCookCount) && contacts.All(c => c.surface.CoolingAt(c.point) >= .999f),
                $"extraBuilds={surfaces.Sum(s => s.CrustMeshRebuildCount) - builds} extraCooks={surfaces.Sum(s => s.CrustColliderCookCount) - cooks}");
            CaptureScenario(scenario, surfaces);
        }
        finally { foreach (var fixture in fixtures) Object.DestroyImmediate(fixture); }
    }
    static int WaterPatchCount(LavaCoolingSurface surface)
    {
        int count = 0;
        foreach (var patch in (IEnumerable)Patches.GetValue(surface))
            if (patch.GetType().GetField("kind").GetValue(patch).ToString() == "Water") count++;
        return count;
    }
    static int HydraulicCounter(ProgressiveLavaFlow flow, string name) =>
        typeof(ProgressiveLavaFlow).GetProperty(name)?.GetValue(flow) is int count ? count : -1;
    static float[] Capacities(ProgressiveLavaFlow flow, string field) =>
        (float[])((float[])typeof(ProgressiveLavaFlow).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(flow)).Clone();
    static void CheckHydraulicState(ProgressiveLavaFlow flow, LavaCoolingSurface cooling, string label)
    {
        var capacities = Capacities(flow, "sectionCapacity");
        var prefixes = Capacities(flow, "prefixCapacity");
        int columns = (int)typeof(ProgressiveLavaFlow).GetField("columnCount", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(flow);
        bool matchesReference = true; float prefix = 1;
        for (int section = 0; section < capacities.Length; section++)
        {
            float distance = Mathf.Min(section * .5f, flow.Length), totalHot = 0, totalWidth = 0;
            for (int column = 0; column < columns - 1; column++)
            {
                Vector3 left = flow.PositionAtDistance(distance, column / (columns - 1f));
                Vector3 right = flow.PositionAtDistance(distance, (column + 1f) / (columns - 1f));
                float width = Vector3.Distance(left, right), segmentHot = 0;
                int samples = Mathf.Max(1, Mathf.CeilToInt(width / .25f));
                for (int sample = 0; sample <= samples; sample++)
                    segmentHot += (1 - cooling.DirectCoolingAt(Vector3.Lerp(left, right, sample / (float)samples))) *
                        (sample == 0 || sample == samples ? .5f : 1f);
                totalHot += segmentHot / samples * width; totalWidth += width;
            }
            float expected = totalWidth > .00001f ? Mathf.Clamp01(totalHot / totalWidth) : 1;
            prefix = Mathf.Min(prefix, expected);
            if (Mathf.Abs(expected - capacities[section]) > .00002f || Mathf.Abs(prefix - prefixes[section]) > .00002f) matchesReference = false;
        }
        Add(label + " matches independent full-width hydraulic sampling", matchesReference, "sections=" + capacities.Length);
        flow.NotifyCoolingChanged(); flow.RefreshCoolingBarrier();
        var full = Capacities(flow, "sectionCapacity"); var fullPrefixes = Capacities(flow, "prefixCapacity");
        Add(label + " incremental and forced-full capacities agree", capacities.Zip(full, (a, b) => Mathf.Abs(a - b) <= .00002f).All(v => v) &&
            prefixes.Zip(fullPrefixes, (a, b) => Mathf.Abs(a - b) <= .00002f).All(v => v));
    }
    static void CheckCoolingRegression(bool reverse)
    {
        string label = reverse ? "Reverse hydraulic cache" : "Forward hydraulic cache";
        var root = new GameObject(label + " fixture");
        try
        {
            root.transform.SetPositionAndRotation(new Vector3(reverse ? 24000 : 23000, 500, 22000), Quaternion.Euler(0, 31, 0));
            var spline = root.AddComponent<SplineContainer>();
            spline.Spline = new Spline(new[] { new float3(0, 0, 0), new float3(0, 0, 160) }, TangentMode.Linear);
            var collider = root.AddComponent<MeshCollider>();
            var bk = root.AddComponent<BK_Lava>(); bk.m_Width = 12; bk.m_WidthSegments = 3; bk.m_LengthSegments = 8; bk.m_BorderHeight = 0; bk.Rebuild();
            var heat = root.AddComponent<LavaIgnitionSource>(); heat.heatCollider = collider;
            heat.surfaceMesh = root.GetComponent<MeshFilter>(); heat.surfaceRenderer = root.GetComponent<MeshRenderer>();
            var cooling = root.AddComponent<LavaCoolingSurface>(); cooling.patchRadius = 4; cooling.litresPerPatch = 64;
            var flow = root.AddComponent<ProgressiveLavaFlow>(); flow.source = bk; flow.ignitionSource = heat;
            flow.reverse = reverse; flow.canBeStoppedByWater = true; flow.Initialize(); flow.SetDistance(flow.Length); Physics.SyncTransforms();
            for (int n = 1; n <= 3; n++)
            {
                int before = HydraulicCounter(flow, "HydraulicSectionEvaluationCount");
                cooling.ApplyWater(flow.PositionAtDistance(35), 16); flow.RefreshCoolingBarrier();
                int local = HydraulicCounter(flow, "HydraulicSectionEvaluationCount") - before;
                CheckHydraulicState(flow, cooling, label + " water dose " + n);
                if (before >= 0 && n > 1) Add(label + " local water avoids unrelated sections at dose " + n,
                    local > 0 && local < Capacities(flow, "sectionCapacity").Length, "evaluatedSections=" + local);
            }
            var houseObject = new GameObject("Rotated hydraulic footprint"); houseObject.SetActive(false); houseObject.transform.SetParent(root.transform, false);
            houseObject.transform.SetPositionAndRotation(flow.PositionAtDistance(85) + Vector3.up, Quaternion.Euler(0, 69, 0));
            var box = houseObject.AddComponent<BoxCollider>(); box.size = new Vector3(7, 2, 14);
            var house = houseObject.AddComponent<BurnableBuilding>(); house.fitFireToBuilding = false; house.ignitionBounds = box;
            houseObject.SetActive(true); Physics.SyncTransforms();
            LavaCoolingSurface.CoolBuildingFoundation(house, .5f); flow.RefreshCoolingBarrier();
            CheckHydraulicState(flow, cooling, label + " rotated half-cooled footprint");
            LavaCoolingSurface.CoolBuildingFoundation(house, 1f); flow.RefreshCoolingBarrier();
            CheckHydraulicState(flow, cooling, label + " fully cooled footprint");

            root.transform.position += new Vector3(2, 0, -1); root.transform.rotation = Quaternion.Euler(0, 49, 0);
            root.transform.localScale = new Vector3(1.2f, 1, .9f); Physics.SyncTransforms();
            flow.Advance(.01f); flow.RefreshCoolingBarrier();
            CheckHydraulicState(flow, cooling, label + " translated rotated nonuniformly scaled source");

            flow.ResetFlow(); flow.SetDistance(flow.Length);
            for (int step = 1; step <= 10; step++)
            {
                cooling.SetDownstreamCooling(10, 30, step / 10f);
                cooling.SetDownstreamCooling(30, 50, step / 10f);
                cooling.RefreshCrust();
            }
            Add(label + " adjacent warm residual reaches do not churn geometry", cooling.CrustMeshRebuildCount <= 4 &&
                cooling.CrustColliderCookCount <= 2, $"rebuilds={cooling.CrustMeshRebuildCount} cooks={cooling.CrustColliderCookCount}");
            cooling.SetDownstreamCooling(10, 50, .1f);
            Add(label + " merged cold residual reach never reheats", cooling.CoolingAt(flow.PositionAtDistance(20)) >= .999f &&
                cooling.CoolingAt(flow.PositionAtDistance(40)) >= .999f);
        }
        finally { Object.DestroyImmediate(root); }
    }
    static void CheckGrowingPatches(VolcanoEruptionSequence.Route[] routes, LavaCoolingSurface[] surfaces)
    {
        ResetRoutes(routes);
        var flow = routes.Select(r => r.flow).OrderByDescending(f => f.Length).First();
        var surface = flow.GetComponent<LavaCoolingSurface>();
        int count = Mathf.Clamp(Mathf.FloorToInt(flow.Length / (surface.patchRadius * 2.2f)), 2, 16);
        var points = Enumerable.Range(0, count).Select(i => flow.PositionAtDistance(flow.Length * (i + .5f) / count)).ToArray();
        var scenario = NewScenario("Growing direct-water patches", "Longest retained authored route, " + count + " evenly spaced targets, each with 64 incremental doses to saturation; all routes continue hydraulic and residual updates. No fixture mesh simplification.");
        foreach (var point in points)
            for (int n = 0; n < 64; n++) ScenarioTick(scenario, null, 0, surface, point, surface.litresPerPatch / 64f, routes, surfaces);
        for (int n = 0; n < 360; n++) ScenarioTick(scenario, null, 0, null, default, 0, routes, surfaces);
        foreach (var current in surfaces) if (HasRegions(current)) current.RefreshCrust();
        Physics.SyncTransforms();
        Add("Direct patch stress grows multiple water regions", WaterPatchCount(surface) > 1, "waterPatches=" + WaterPatchCount(surface));
        Add("Every stress spray target becomes harmless", points.All(p => surface.CoolingAt(p) >= .999f && !surface.GetComponent<LavaIgnitionSource>().IsHotAt(p)));
        int builds = surface.CrustMeshRebuildCount, cooks = surface.CrustColliderCookCount;
        foreach (var point in points) for (int n = 0; n < 20; n++) surface.ApplyWater(point, 1f);
        surface.RefreshCrust();
        Add("Settled direct-water patches do not rebuild on repeated spray", surface.CrustMeshRebuildCount == builds && surface.CrustColliderCookCount == cooks);
        CaptureScenario(scenario, surfaces);
    }
    static void CheckMovingFront(bool reverse)
    {
        string label = reverse ? "Reverse front" : "Forward front";
        var root = new GameObject(label + " cooling cache fixture");
        Mesh replacement = null;
        try
        {
            root.transform.position = new Vector3(reverse ? 22000 : 21000, 500, 21000);
            var spline = root.AddComponent<SplineContainer>();
            spline.Spline = new Spline(new[] { new float3(0, 0, 0), new float3(0, 0, 120) }, TangentMode.Linear);
            var collider = root.AddComponent<MeshCollider>();
            var bk = root.AddComponent<BK_Lava>();
            bk.m_Width = 10; bk.m_WidthSegments = 2; bk.m_LengthSegments = 12; bk.m_BorderHeight = 0; bk.Rebuild();
            var heat = root.AddComponent<LavaIgnitionSource>();
            heat.heatCollider = collider; heat.surfaceMesh = root.GetComponent<MeshFilter>(); heat.surfaceRenderer = root.GetComponent<MeshRenderer>();
            var cooling = root.AddComponent<LavaCoolingSurface>(); cooling.patchRadius = 4; cooling.litresPerPatch = 100;
            var flow = root.AddComponent<ProgressiveLavaFlow>(); flow.source = bk; flow.ignitionSource = heat; flow.reverse = reverse;
            flow.Initialize(); flow.SetDistance(55); Physics.SyncTransforms();
            Vector3 upstreamPoint = flow.PositionAtDistance(20);
            cooling.ApplyWater(upstreamPoint, cooling.litresPerPatch);
            var upstream = cooling.GetComponentsInChildren<MeshRenderer>().Single(r => r.transform != root.transform);
            var upstreamMesh = upstream.GetComponent<MeshFilter>().sharedMesh;
            var upstreamVertices = upstreamMesh.vertices;
            int builds = cooling.CrustMeshRebuildCount, cooks = cooling.CrustColliderCookCount;
            foreach (float distance in new[] { 58f, 72f, 100f })
            { flow.SetDistance(distance); cooling.RefreshCrust(); }
            Add(label + " retains completed upstream geometry and collision through moving cells",
                cooling.CrustMeshRebuildCount == builds && cooling.CrustColliderCookCount == cooks &&
                upstreamVertices.SequenceEqual(upstreamMesh.vertices) && SolidAt(upstream, upstreamPoint) && !heat.IsHotAt(upstreamPoint),
                $"rebuilds={cooling.CrustMeshRebuildCount - builds}, colliderCooks={cooling.CrustColliderCookCount - cooks}");

            // BK's authored length-segment setting is a density, not a cell count. Select
            // one measured source interval so both fronts really lie in the same cell.
            var sourceRows = (float[])typeof(ProgressiveLavaFlow).GetField("rowDistances", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flow);
            var travelRows = sourceRows.Select(d => reverse ? flow.Length - d : d).OrderBy(d => d).ToArray();
            int cell = Array.FindLastIndex(travelRows, d => d < 55f);
            float beforeFront = Mathf.Lerp(travelRows[cell], travelRows[cell + 1], .25f);
            float afterFront = Mathf.Lerp(travelRows[cell], travelRows[cell + 1], .75f);
            float betweenFronts = Mathf.Lerp(travelRows[cell], travelRows[cell + 1], .5f);
            flow.SetDistance(beforeFront); cooling.RefreshCrust();
            var houseObject = new GameObject("Front-crossing protected foundation"); houseObject.SetActive(false);
            houseObject.transform.SetParent(root.transform, false);
            houseObject.transform.SetPositionAndRotation(flow.PositionAtDistance(58) + Vector3.up, Quaternion.Euler(0, 25, 0));
            var box = houseObject.AddComponent<BoxCollider>(); box.size = new Vector3(8, 2, 14);
            var house = houseObject.AddComponent<BurnableBuilding>(); house.fitFireToBuilding = false; house.ignitionBounds = box;
            houseObject.SetActive(true); Physics.SyncTransforms();
            LavaCoolingSurface.CoolBuildingFoundation(house, 1);
            var foundation = cooling.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Water-cooled foundation obsidian");
            Vector3 addedPoint = flow.PositionAtDistance(betweenFronts);
            bool initiallyClipped = !SolidAt(foundation, addedPoint);
            builds = cooling.CrustMeshRebuildCount; cooks = cooling.CrustColliderCookCount;
            flow.SetDistance(afterFront); cooling.RefreshCrust(); Physics.SyncTransforms();
            Add(label + " refreshes the front-crossing footprint inside the same grid cell",
                initiallyClipped && SolidAt(foundation, addedPoint) && !heat.IsHotAt(addedPoint) &&
                cooling.CrustMeshRebuildCount == builds + 1 && cooling.CrustColliderCookCount == cooks + 1,
                $"cell=[{travelRows[cell]:F3},{travelRows[cell + 1]:F3}], fronts={beforeFront:F3}->{afterFront:F3}, rebuilds={cooling.CrustMeshRebuildCount - builds}, colliderCooks={cooling.CrustColliderCookCount - cooks}");
            flow.SetDistance(72); cooling.RefreshCrust(); Physics.SyncTransforms();
            Add(label + " preserves cold coverage after crossing multiple grid rows", SolidAt(foundation, flow.PositionAtDistance(65)) &&
                cooling.CoolingAt(flow.PositionAtDistance(65)) >= 1 && !heat.IsHotAt(flow.PositionAtDistance(65)) &&
                heat.IsHotAt(flow.PositionAtDistance(71)));

            var futureObject = new GameObject("Protected foundation ahead of the front"); futureObject.SetActive(false);
            futureObject.transform.SetParent(root.transform, false);
            futureObject.transform.position = flow.PositionAtDistance(90) + Vector3.up;
            var futureBox = futureObject.AddComponent<BoxCollider>(); futureBox.size = new Vector3(6, 2, 6);
            var futureHouse = futureObject.AddComponent<BurnableBuilding>(); futureHouse.fitFireToBuilding = false; futureHouse.ignitionBounds = futureBox;
            futureObject.SetActive(true); Physics.SyncTransforms(); LavaCoolingSurface.CoolBuildingFoundation(futureHouse, 1);
            var future = cooling.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Water-cooled foundation obsidian" && r != foundation);
            bool initiallyEmpty = !future.enabled;
            flow.SetDistance(100); cooling.RefreshCrust(); Physics.SyncTransforms();
            Add(label + " reveals a previously empty protected footprint when lava arrives", initiallyEmpty &&
                SolidAt(future, flow.PositionAtDistance(90)) && !heat.IsHotAt(flow.PositionAtDistance(90)));

            flow.SetDistance(0); cooling.RefreshCrust();
            Add(label + " hides cached cover when lava is hidden", !upstream.enabled && !foundation.enabled &&
                !upstream.GetComponent<MeshCollider>().enabled && !foundation.GetComponent<MeshCollider>().enabled);
            flow.SetDistance(72); cooling.RefreshCrust(); Physics.SyncTransforms();
            Add(label + " restores cached protection after re-reveal", SolidAt(upstream, upstreamPoint) && SolidAt(foundation, addedPoint));

            Mesh original = collider.sharedMesh;
            replacement = Object.Instantiate(original);
            var raised = replacement.vertices;
            for (int i = 0; i < raised.Length; i++) raised[i] += Vector3.up * .4f;
            replacement.vertices = raised; replacement.RecalculateBounds();
            collider.sharedMesh = replacement; heat.surfaceMesh.sharedMesh = replacement;
            // Exercise the ordinary LateUpdate detector, not a test-only explicit notification.
            typeof(LavaCoolingSurface).GetField("nextMesh", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(cooling, 0f);
            typeof(LavaCoolingSurface).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cooling, null);
            Physics.SyncTransforms();
            Add(label + " detects a replaced source mesh without stale cover", SolidAt(upstream, upstreamPoint + Vector3.up * .4f) &&
                upstream.GetComponent<MeshCollider>().Raycast(new Ray(upstreamPoint + Vector3.up * 2, Vector3.down), out var hit, 3f) && hit.point.y > upstreamPoint.y + .5f);
            collider.sharedMesh = original; heat.surfaceMesh.sharedMesh = original;
            flow.ResetFlow();
            Add(label + " reset clears cached cooling and source buffers", cooling.CoolingAt(upstreamPoint) == 0 && cooling.SourceMeshReadCount == 0);
        }
        finally { Object.DestroyImmediate(root); if (replacement) Object.DestroyImmediate(replacement); }
    }
    static bool SolidAt(MeshRenderer renderer, Vector3 point)
    {
        var collider = renderer.GetComponent<MeshCollider>();
        return renderer.enabled && collider.enabled && collider.sharedMesh &&
            collider.Raycast(new Ray(point + Vector3.up, Vector3.down), out _, 2f);
    }
    static int Counter(LavaCoolingSurface surface, string property) =>
        typeof(LavaCoolingSurface).GetProperty(property)?.GetValue(surface) is int value ? value : 0;
    static Timing Metric(string name) { var metric = new Timing { name = name }; report.timings.Add(metric); return metric; }
    static IEnumerable<Contact> Contacts(BurnableBuilding[] houses, LavaCoolingSurface[] surfaces)
    {
        foreach (var house in houses)
        {
            var box = house.ignitionBounds; var tr = box.transform; var half = box.size * .5f; var scale = tr.lossyScale;
            for (int side = 0; side < 4; side++) for (int step = 1; step < 8; step++)
            {
                bool xSide = side < 2; float sign = side % 2 == 0 ? -1 : 1, along = Mathf.Lerp(-1, 1, step / 8f);
                var local = box.center + new Vector3(xSide ? sign * (half.x + .5f / Mathf.Abs(scale.x)) : half.x * along,
                    -half.y, xSide ? half.z * along : sign * (half.z + .5f / Mathf.Abs(scale.z)));
                var point = tr.TransformPoint(local);
                foreach (var surface in surfaces)
                {
                    var heat = surface.GetComponent<LavaIgnitionSource>();
                    if (!heat || !heat.IsHotSurfaceActive || !(heat.heatCollider is MeshCollider)) continue;
                    if (heat.heatCollider.Raycast(new Ray(new Vector3(point.x, box.bounds.max.y + 2, point.z), Vector3.down), out var hit, box.bounds.size.y + 6) &&
                        hit.point.y >= box.bounds.min.y - .25f && hit.point.y <= box.bounds.min.y + 1.5f)
                        yield return new Contact { house = house, surface = surface, point = hit.point };
                }
            }
        }
    }
    static void Add(string name, bool passed, string evidence = "") => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Skip(string name, string evidence) => report.checks.Add(new Check { name = name, passed = true, skipped = true, evidence = evidence });
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
}
