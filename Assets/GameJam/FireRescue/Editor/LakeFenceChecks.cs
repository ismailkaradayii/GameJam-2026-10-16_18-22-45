using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Actual authored shore coverage, real pump and powered-truck fence collision checks.</summary>
[InitializeOnLoad]
public static class LakeFenceChecks
{
    const string Pending = "LakeFenceChecks.Pending";
    const string Folder = "Logs/LakeFence";
    static readonly List<string> lines = new List<string>();
    static readonly MethodInfo Follow = typeof(FireRescuePlayer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    static FireRescuePlayer player;
    static FireTruckDrive drive;
    static LakeFillStation station;
    static LakeHosePassage passage;
    static Transform fence;
    static HashSet<Collider> blockers;
    static LakeFenceCollisionProbe probe;
    static readonly List<RamCase> rams = new List<RamCase>();
    static int phase = -1, frame, parking, ram;
    static float at, initialLitres, minSigned, peakSpeed, peakGroundOffset;
    static double deadline;
    static bool background, intakeOnly;
    static string runtimeError;
    static Vector3 gate, outward, tangent;
    struct RamCase { public string name; public Vector3 point, outward; public float angle; }
    [Serializable] sealed class CombatReport { public bool allPassed; public CombatCheck[] checks; }
    [Serializable] sealed class CombatCheck { public bool passed; }

    static LakeFenceChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted.", false);
        };
    }
    [MenuItem("GameJam/Final Loop/Check lake fence")]
    public static void Run() => Begin(false);
    [MenuItem("GameJam/Final Loop/Check lake intake")]
    public static void Intake() => Begin(true);
    static void Begin(bool onlyIntake)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use idle Edit Mode.");
        Directory.CreateDirectory(Folder);
        intakeOnly = onlyIntake; SessionState.SetBool("LakeFenceChecks.IntakeOnly", onlyIntake);
        var authored = Object.FindFirstObjectByType<LakeFillStation>();
        var source = authored && authored.water && authored.water.waterSurface ? authored.water.waterSurface.GetComponent<MeshFilter>() : null;
        if (!source || !source.sharedMesh) throw new InvalidOperationException("Actual authored water mesh is required.");
        // Static batching can replace sharedMesh with a combined world-space mesh
        // during Play. Preserve the authored plane's own local bounds beforehand.
        Bounds sourceBounds = source.sharedMesh.bounds;
        SessionState.SetFloat(Pending + ".MinX", sourceBounds.min.x); SessionState.SetFloat(Pending + ".MaxX", sourceBounds.max.x);
        SessionState.SetFloat(Pending + ".MinY", sourceBounds.min.y); SessionState.SetFloat(Pending + ".MaxY", sourceBounds.max.y);
        SessionState.SetFloat(Pending + ".MinZ", sourceBounds.min.z); SessionState.SetFloat(Pending + ".MaxZ", sourceBounds.max.z);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.isDirty)
        {
            Directory.CreateDirectory("Library/LakeFenceReview");
            string copy = "Library/LakeFenceReview/unsaved-preserved-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity";
            if (!EditorSceneManager.SaveScene(scene, copy, true)) throw new IOException("Could not preserve the dirty scene copy.");
        }
        File.WriteAllText(ReportPath, "STARTING " + DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); runtimeError = null; frame = -1; parking = ram = 0;
        intakeOnly = SessionState.GetBool("LakeFenceChecks.IntakeOnly", false);
        SessionState.EraseBool("LakeFenceChecks.IntakeOnly");
        background = Application.runInBackground; Application.runInBackground = true;
        Application.logMessageReceived += Log;
        try
        {
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            if (!player || !player.mission || !player.mission.fillStation) throw new InvalidOperationException("Open the authored volcano mission.");
            drive = player.drive; station = player.mission.fillStation;
            string terrainOverride = SessionState.GetString("LakeFenceChecks.TerrainOverride", "");
            SessionState.EraseString("LakeFenceChecks.TerrainOverride");
            if (!string.IsNullOrEmpty(terrainOverride))
            {
                var replacement = AssetDatabase.LoadAssetAtPath<TerrainData>(terrainOverride);
                if (!replacement) throw new InvalidOperationException("Missing test terrain override: " + terrainOverride);
                var terrain = Terrain.activeTerrains.FirstOrDefault(t => t.terrainData && AssetDatabase.GetAssetPath(t.terrainData) == "Assets/BK/PureNature_Volcano/Models/Terrains/Terrain.asset") ?? Terrain.activeTerrain;
                if (!terrain || !terrain.GetComponent<TerrainCollider>()) throw new InvalidOperationException("Missing terrain and collider for Play Mode override.");
                terrain.terrainData = replacement; terrain.GetComponent<TerrainCollider>().terrainData = replacement;
                terrain.Flush(); Physics.SyncTransforms();
                lines.Add("TERRAIN OVERRIDE (Play Mode only): " + terrainOverride);
            }
            fence = GameObject.Find("Lake Shore Fence")?.transform;
            if (!fence) throw new InvalidOperationException("Authored Lake Shore Fence is missing.");
            passage = fence.GetComponentInChildren<LakeHosePassage>(true);
            if (!passage || !passage.dryGuide || !passage.wetGuide || !passage.intakeTarget)
                throw new InvalidOperationException("Intake opening needs both guides and its intake target.");
            blockers = new HashSet<Collider>(fence.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger));
            player.mission.enabled = false; player.mission.story.enabled = false; player.mission.cinematic.enabled = false;
            player.mission.sequence.enabled = false; player.gameFlow.enabled = false;
            player.SetCinematicLocked(false); player.SetGameplaySuspended(false); player.SetDriving(true); player.enabled = false;
            player.hose.fillingUnlocked = true; Time.timeScale = 1;
            probe = drive.gameObject.AddComponent<LakeFenceCollisionProbe>(); probe.fence = fence;
            gate = (passage.dryGuide.position + passage.wetGuide.position) * .5f;
            outward = Vector3.ProjectOnPlane(passage.dryGuide.position - passage.wetGuide.position, Vector3.up).normalized;
            tangent = Vector3.Cross(Vector3.up, outward).normalized;
            lines.Add("Real authored scene in Play Mode; dirty Edit Mode source preserved by save-as-copy only. Intro/deadline/input are disabled only in this fixture. Pump uses actual lake/tank and elapsed time. Truck is placed only at each ram start; all collision approaches then use normal throttle/steering with saved wheels, body and obstacle-step assistance.");
            CheckStructure(); CheckPerimeter();
            if (!intakeOnly) CaptureViews();
            Park(); Next(0, 30);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new Exception(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase + " parking=" + parking + " ram=" + ram);
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.time - at;
        Follow?.Invoke(player, null);
        if (phase == 0)
        {
            if (age < .35f) return;
            Check("Existing lake interaction starts guided filling " + parking, station.CanFill && station.Interact(), player.hose.Status);
            initialLitres = player.hose.TankLitres; Next(1, 30); return;
        }
        if (phase == 1)
        {
            if (age < 1) return;
            CheckHose();
            Check("Guided intake pumps actual lake water " + parking, player.hose.IsPumping && player.hose.CurrentWaterSource == station.water && station.water.ContainsIntake(player.hose.nozzle.position), player.hose.Status);
            Check("Actual tank gains water over elapsed frames " + parking, player.hose.TankLitres > initialLitres + 20, "litres=" + player.hose.TankLitres);
            if (parking == 0) CaptureViews(true);
            if (intakeOnly) { Finish(null); return; }
            if (parking < 2)
            {
                station.Cancel();
                Check("Cancel preserves original pump and retract behavior " + parking, !station.IsFilling && !player.hose.IsDeployed && !player.hose.PumpEnabled, "cancelled through station");
                parking++; Park(); Next(0, 30); return;
            }
            Next(2, 90); return;
        }
        if (phase == 2)
        {
            if (player.hose.Tank01 < .9999f || station.IsFilling) return;
            Check("Full real tank automatically retracts hose and restores driving", !player.hose.IsDeployed && !player.hose.PumpEnabled && player.IsDriving && Mathf.Abs(player.hose.TankLitres - player.hose.tankCapacityLitres) < .01f,
                "litres=" + player.hose.TankLitres);
            BuildRamCases(); BeginRam(); Next(3, 30); return;
        }
        if (phase == 3)
        {
            drive.SetInput(0, 0, true);
            if (age < 1.2f) return;
            probe.hits = 0; probe.last = "";
            minSigned = float.PositiveInfinity; peakSpeed = peakGroundOffset = 0;
            Next(4, 35); return;
        }
        if (phase == 4)
        {
            drive.SetInput(1, 0, false);
            var test = rams[ram];
            minSigned = Mathf.Min(minSigned, Vector3.Dot(drive.transform.position - test.point, test.outward));
            peakSpeed = Mathf.Max(peakSpeed, drive.SpeedKph);
            peakGroundOffset = Mathf.Max(peakGroundOffset, drive.transform.position.y - Ground(drive.transform.position));
            if (minSigned < -.3f) throw new Exception("Truck crossed lake fence in " + test.name + " at " + drive.transform.position);
            if (age < 7) return;
            drive.SetInput(0, 0, true);
            Check("Powered truck physically contacts barrier: " + test.name, probe.hits > 0 && peakSpeed > 8,
                $"contacts={probe.hits} peak={peakSpeed:F1}km/h last={probe.last}");
            Check("Fence and opening resist original obstacle-step assistance: " + test.name,
                minSigned > .1f && !station.water.ContainsSurface(drive.transform.position),
                $"minimum dry-side centre={minSigned:F2}m max ground offset={peakGroundOffset:F2}m step assist={drive.obstacleStepHeight:F2}m");
            if (ram == 0) ScreenCapture.CaptureScreenshot(Folder + "/02-truck-blocked-at-opening.png");
            if (++ram < rams.Count) { BeginRam(); Next(3, 30); return; }
            Check("Original truck colliders remain enabled", drive.body.detectCollisions && drive.GetComponent<BoxCollider>().enabled && drive.wheels.All(w => w && w.enabled), "no collider disabling or truck scale changes");
            FireCombatChecks.Run();
            var combat = JsonUtility.FromJson<CombatReport>(File.ReadAllText("Logs/FireCombatChecks.json"));
            Check("Existing water, pump and damage regression suite", combat != null && combat.allPassed && combat.checks != null && combat.checks.Length == 39 && combat.checks.All(c => c.passed),
                "passed=" + (combat?.checks?.Count(c => c.passed) ?? 0) + "/39; see FireCombatChecks.json");
            Finish(null);
        }
    }
    static void CheckStructure()
    {
        Check("Whole lake uses physical fence sections", blockers.Count >= 25 && blockers.All(c => c is BoxCollider), "solid boxes=" + blockers.Count);
        Check("Visible lake surface is contained by its intake volume", station.water.ContainsSurface(station.water.transform.position),
            "surfaceY=" + station.water.SurfaceY + " volume=" + station.water.volume.bounds);
        CheckSurfaceFootprint();
        float minimumHeight = float.PositiveInfinity, minimumEmbed = float.PositiveInfinity;
        foreach (var collider in blockers.OfType<BoxCollider>())
        {
            foreach (float x in new[] { -.45f, 0f, .45f })
            {
                Vector3 axis = collider.transform.TransformPoint(collider.center + Vector3.right * (collider.size.x * x));
                float terrain = Ground(axis);
                Vector3 low = collider.transform.TransformPoint(collider.center + new Vector3(collider.size.x * x, -collider.size.y * .5f, 0));
                Vector3 high = collider.transform.TransformPoint(collider.center + new Vector3(collider.size.x * x, collider.size.y * .5f, 0));
                minimumEmbed = Mathf.Min(minimumEmbed, terrain - low.y);
                minimumHeight = Mathf.Min(minimumHeight, high.y - terrain);
            }
        }
        Check("Fence collision reaches the real terrain along every panel", minimumEmbed > -.03f, "minimum embed=" + minimumEmbed.ToString("F3") + "m");
        Check("Every panel is taller than powered truck step assistance", minimumHeight > drive.obstacleStepHeight + .25f,
            $"minimum collision height={minimumHeight:F3}m step assistance={drive.obstacleStepHeight:F3}m");
        Check("Original intake target remains bound", Vector3.Distance(passage.intakeTarget.position, station.intakePoint.position) < .01f && LakeHosePassage.Find(station.intakePoint.position) == passage,
            "intake=" + station.intakePoint.position);
        Vector3 widthPoint = gate; widthPoint.y = Ground(gate) + 1.2f;
        float left = FenceRayDistance(widthPoint, -tangent, 5), right = FenceRayDistance(widthPoint, tangent, 5);
        Check("Hose opening stays narrow and physically clear", left > .25f && right > .25f && left + right > .8f && left + right < 1.8f,
            $"clear width={left + right:F3}m left={left:F3} right={right:F3}");
        bool blocked = Physics.SphereCastAll(passage.dryGuide.position, player.hose.hoseDiameter * .5f,
            (passage.wetGuide.position - passage.dryGuide.position).normalized,
            Vector3.Distance(passage.dryGuide.position, passage.wetGuide.position), ~0, QueryTriggerInteraction.Ignore).Any(h => blockers.Contains(h.collider));
        Check("Actual hose diameter fits through the opening", !blocked, "diameter=" + player.hose.hoseDiameter);
    }
    static void CheckPerimeter()
    {
        Vector3 center = station.water.volume.bounds.center; center.y = station.water.SurfaceY;
        float maximum = station.water.volume.bounds.extents.magnitude + 20;
        int covered = 0, gateRays = 0; var missed = new List<string>();
        // Half-degree offset avoids reusing the authoring sample vertices.
        for (int i = 0; i < 720; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(i * .5f + .25f, Vector3.up) * Vector3.forward;
            float wet = 0;
            for (float r = .5f; r < maximum; r += .5f)
            {
                if (!station.water.ContainsSurface(center + direction * r)) break;
                wet = r;
            }
            if (wet < 3) { missed.Add("no wet centre ray=" + i); continue; }
            float lo = wet, hi = wet + .5f;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                float mid = (lo + hi) * .5f;
                if (station.water.ContainsSurface(center + direction * mid)) lo = mid; else hi = mid;
            }
            Vector3 shore = center + direction * ((lo + hi) * .5f);
            Vector3 origin = shore - direction * 8; origin.y = station.water.SurfaceY;
            // Some dry-bank sections sit farther uphill than the initial setback.
            // Test continuous XZ coverage independently of height; terrain embed and
            // above-ground collision height are verified separately on every panel.
            bool hit = Physics.BoxCastAll(origin, new Vector3(.005f, 100f, .005f), direction,
                Quaternion.identity, 84, ~0, QueryTriggerInteraction.Ignore).Any(h => blockers.Contains(h.collider));
            if (hit) { covered++; continue; }
            // A ray may pass through only the authored clear hose opening.
            float denominator = Vector3.Dot(direction, outward);
            float distance = Mathf.Abs(denominator) > .0001f ? Vector3.Dot(gate - origin, outward) / denominator : -1;
            Vector3 crossing = origin + direction * distance;
            if (distance >= 0 && distance <= 84 && Mathf.Abs(Vector3.Dot(crossing - gate, tangent)) < .8f) gateRays++;
            else if (missed.Count < 20) missed.Add($"ray={i} shore={shore:F3} height={origin.y:F3}");
        }
        Check("Independent shoreline rays find continuous physical fence", missed.Count == 0 && covered > 680,
            $"covered={covered}/720 narrow opening rays={gateRays}; " + string.Join("; ", missed));
    }
    static void CheckSurfaceFootprint()
    {
        var water = station.water;
        var mesh = water.waterSurface ? water.waterSurface.GetComponent<MeshFilter>() : null;
        if (!mesh || !mesh.sharedMesh) throw new InvalidOperationException("Actual visible water mesh is required.");
        var bounds = new Bounds();
        bounds.SetMinMax(new Vector3(SessionState.GetFloat(Pending + ".MinX", 0), SessionState.GetFloat(Pending + ".MinY", 0), SessionState.GetFloat(Pending + ".MinZ", 0)),
            new Vector3(SessionState.GetFloat(Pending + ".MaxX", 0), SessionState.GetFloat(Pending + ".MaxY", 0), SessionState.GetFloat(Pending + ".MaxZ", 0)));
        Vector3 half = water.volume.size * .5f;
        float error = 0;
        foreach (float x in new[] { bounds.min.x, bounds.max.x })
            foreach (float z in new[] { bounds.min.z, bounds.max.z })
            {
                Vector3 corner = water.waterSurface.TransformPoint(new Vector3(x, bounds.center.y, z));
                Vector3 local = water.volume.transform.InverseTransformPoint(corner) - water.volume.center;
                error = Mathf.Max(error, Mathf.Abs(Mathf.Abs(local.x) - half.x), Mathf.Abs(Mathf.Abs(local.z) - half.z));
            }
        float top = water.volume.transform.TransformPoint(water.volume.center + Vector3.up * half.y).y;
        Check("Intake volume matches actual shifted and scaled water plane", error < .02f && Mathf.Abs(top - water.SurfaceY) < .02f,
            $"maximum corner error={error:F4}m top={top:F3}m surface={water.SurfaceY:F3}m");
    }

    [MenuItem("GameJam/Final Loop/Capture lake fence")]
    public static void CaptureViews() => CaptureViews(false);
    static void CaptureViews(bool hoseOnly)
    {
        var lake = Object.FindFirstObjectByType<LakeFillStation>();
        var root = GameObject.Find("Lake Shore Fence");
        var opening = root ? root.GetComponentInChildren<LakeHosePassage>(true) : null;
        if (!lake || !lake.water || !opening) throw new InvalidOperationException("Open the authored fenced lake first.");
        Directory.CreateDirectory(Folder);
        var go = new GameObject("Temporary lake fence review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.scene = lake.gameObject.scene;
        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(lake.gameObject.scene);
        camera.clearFlags = CameraClearFlags.Skybox; camera.nearClipPlane = .2f; camera.farClipPlane = 1200;
        camera.cullingMask = ~(1 << LayerMask.NameToLayer("FirstPersonHands"));
        camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
        camera.GetUniversalAdditionalCameraData().requiresColorTexture = true;
        var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
        var target = new RenderTexture(1920, 1080, 24);
        var previous = RenderTexture.active;
        void Photo(string name, Vector3 from, Vector3 to, float fov)
        {
            camera.fieldOfView = fov; camera.aspect = 16f / 9f;
            camera.transform.position = from; camera.transform.LookAt(to);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply();
            File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG());
        }
        try
        {
            Vector3 gatePoint = opening.transform.position;
            Vector3 dry = Vector3.ProjectOnPlane(opening.dryGuide.position - opening.wetGuide.position, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dry);
            if (hoseOnly)
                Photo("intake-hose", gatePoint + dry * 12 + side * 5 + Vector3.up * 9, gatePoint + Vector3.up * .2f, 55);
            else
            {
                Vector3 center = lake.water.volume.bounds.center; center.y = lake.water.SurfaceY;
                float radius = Mathf.Max(lake.water.volume.bounds.extents.x, lake.water.volume.bounds.extents.z);
                Photo("overview", center + new Vector3(radius * 1.45f, radius * 1.7f, radius * 1.6f), center, 53);
                Photo("intake-opening", gatePoint + dry * 19 + side * 10 + Vector3.up * 13, gatePoint + Vector3.up * 2, 55);
            }
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            Object.DestroyImmediate(go); Object.DestroyImmediate(texture); target.Release(); Object.DestroyImmediate(target);
        }
    }
    static float FenceRayDistance(Vector3 origin, Vector3 direction, float length)
    {
        float minimum = float.PositiveInfinity;
        foreach (var hit in Physics.RaycastAll(origin, direction, length, ~0, QueryTriggerInteraction.Ignore))
            if (blockers.Contains(hit.collider)) minimum = Mathf.Min(minimum, hit.distance);
        return minimum;
    }
    static void Park()
    {
        station.Cancel(); drive.SetInput(0, 0, true); drive.enabled = false; drive.body.isKinematic = true;
        float yaw = parking == 0 ? 0 : parking == 1 ? 18 : -18;
        Place(station.transform.position + Vector3.up * 1.2f, Quaternion.Euler(0, yaw, 0));
        player.hose.SetTankLitres(0); Physics.SyncTransforms();
    }
    static void CheckHose()
    {
        var line = player.hose.hoseLine;
        if (!line || !line.enabled || line.positionCount < 3) throw new Exception("Deployed hose line is missing.");
        float minimum = float.PositiveInfinity, dryDistance = float.PositiveInfinity, wetDistance = float.PositiveInfinity;
        int collisions = 0; Vector3 worst = Vector3.zero;
        float radius = player.hose.hoseDiameter * .5f;
        for (int i = 1; i < line.positionCount; i++)
        {
            Vector3 a = line.GetPosition(i - 1), b = line.GetPosition(i), delta = b - a;
            dryDistance = Mathf.Min(dryDistance, PointSegment(passage.dryGuide.position, a, b));
            wetDistance = Mathf.Min(wetDistance, PointSegment(passage.wetGuide.position, a, b));
            if (delta.sqrMagnitude > .000001f && Physics.SphereCastAll(a, radius * .98f, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore).Any(h => blockers.Contains(h.collider))) collisions++;
            for (int sample = 0; sample <= 4; sample++)
            {
                Vector3 point = Vector3.Lerp(a, b, sample * .25f);
                if (Physics.OverlapSphere(point, radius * .98f, ~0, QueryTriggerInteraction.Ignore).Any(c => blockers.Contains(c))) collisions++;
                float gap = point.y - Ground(point) - radius;
                if (gap < minimum) { minimum = gap; worst = point; }
            }
        }
        Check("Real hose chords and diameter clear fence " + parking, collisions == 0, "fence intersections=" + collisions);
        Check("Hose follows the intake opening guides " + parking, dryDistance < .22f && wetDistance < .22f,
            $"dry guide distance={dryDistance:F3}m wet={wetDistance:F3}m");
        Check("Hose skin clears actual bank and lakebed " + parking, minimum > .012f,
            $"minimum={minimum:F3}m at {worst:F3} vertices={line.positionCount}");
    }
    static float PointSegment(Vector3 point, Vector3 a, Vector3 b)
    { Vector3 delta = b - a; return Vector3.Distance(point, a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / Mathf.Max(.000001f, delta.sqrMagnitude))); }
    static void BuildRamCases()
    {
        rams.Clear();
        foreach (float angle in new[] { 0f, -18f, 18f }) rams.Add(new RamCase { name = "hose opening " + angle + " degrees", point = gate, outward = outward, angle = angle });
        foreach (float side in new[] { -1f, 1f })
        {
            Vector3 wanted = gate + tangent * (side * 13);
            var panel = blockers.OrderBy(c => (Vector3.ProjectOnPlane(c.bounds.center - wanted, Vector3.up)).sqrMagnitude).First();
            Vector3 point = panel.bounds.center;
            Vector3 normal = Vector3.ProjectOnPlane(point - station.water.transform.position, Vector3.up).normalized;
            rams.Add(new RamCase { name = "solid panel " + side, point = point, outward = normal, angle = side * 12 });
        }
    }
    static void BeginRam()
    {
        station.Cancel(); drive.SetInput(0, 0, true); drive.enabled = false; drive.body.isKinematic = true;
        var test = rams[ram];
        Vector3 forward = Quaternion.AngleAxis(test.angle, Vector3.up) * -test.outward;
        Vector3 start = test.point - forward * 29;
        start.y = Ground(start) + 1.4f;
        Place(start, Quaternion.LookRotation(forward));
        drive.body.isKinematic = false; drive.body.linearVelocity = Vector3.zero; drive.body.angularVelocity = Vector3.zero;
        drive.enabled = true; drive.SetDriven(true); drive.SetInput(0, 0, true); Physics.SyncTransforms();
        lines.Add("RAM START " + test.name + " at " + start + " toward " + test.point); Save();
    }
    static void Place(Vector3 position, Quaternion rotation)
    { drive.body.position = position; drive.body.rotation = rotation; drive.transform.SetPositionAndRotation(position, rotation); Follow?.Invoke(player, null); }
    static float Ground(Vector3 point) => TruckLakeRouteTools.Ground(point).y;
    static void Check(string name, bool passed, string evidence)
    {
        lines.Add((passed ? "PASS " : "FAIL ") + name + ": " + evidence); Save();
        if (!passed) throw new Exception(name + ": " + evidence);
    }
    static void Next(int next, double timeout) { phase = next; at = Time.time; deadline = EditorApplication.timeSinceStartup + timeout; }
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = message; }
    static string ReportPath => Folder + (intakeOnly ? "/intake-checks.txt" : "/checks.txt");
    static void Save() => File.WriteAllLines(ReportPath, lines);
    static void Finish(string failure, bool exit = true)
    {
        phase = -1; Application.logMessageReceived -= Log; Application.runInBackground = background;
        if (drive) drive.SetInput(0, 0, true);
        if (station) station.Cancel();
        lines.Add(failure == null ? "COMPLETED " + DateTime.UtcNow.ToString("O") : "FAILED " + failure); Save();
        if (exit && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}

public sealed class LakeFenceCollisionProbe : MonoBehaviour
{
    public Transform fence;
    public int hits;
    public string last;
    void OnCollisionEnter(Collision collision) => Contact(collision);
    void OnCollisionStay(Collision collision) => Contact(collision);
    void Contact(Collision collision)
    {
        if (!fence || !collision.collider || !collision.collider.transform.IsChildOf(fence)) return;
        hits++; last = collision.collider.name;
    }
}
