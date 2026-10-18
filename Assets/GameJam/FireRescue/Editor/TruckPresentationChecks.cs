using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Focused checks for the road guide, physical intake hose and roof monitor presentation.</summary>
[InitializeOnLoad]
public static class TruckPresentationChecks
{
    const string Pending = "TruckPresentationChecks.Pending";
    static readonly List<string> report = new List<string>();
    static readonly MethodInfo Follow = typeof(FireRescuePlayer).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    static FireRescuePlayer player;
    static ObjectiveNavigation navigation;
    static LakeFillStation station;
    static int phase = -1, lastFrame, parking, samples, switches;
    static bool previousBackground, previousArrow;
    static bool capturedHud;
    static float phaseAt, pathLength, minimumArrowClearance, peakGuideSpeed, peakScreenSpeed, lastCannonStep;
    static float settledCannonMovement;
    static Vector3[] route;
    static Vector3 lastGuide;
    static Vector3 worstLinePoint;
    static Vector2 lastMarker;
    static string runtimeError;
    static double deadline;

    static TruckPresentationChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Play Mode interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check truck presentation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Start in saved, idle Edit Mode.");
        Directory.CreateDirectory("Logs/TruckPresentation");
        File.WriteAllText("Logs/TruckPresentation/checks.txt", "STARTING " + DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        try
        {
            report.Clear(); runtimeError = null; lastFrame = -1; parking = 0; capturedHud = false;
            previousBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            navigation = Object.FindFirstObjectByType<ObjectiveNavigation>(); station = player.mission.fillStation;
            player.mission.enabled = false; player.mission.story.enabled = false; player.mission.cinematic.enabled = false;
            player.mission.sequence.enabled = false; player.gameFlow.enabled = false;
            player.SetCinematicLocked(false); player.AcquirePistol(); player.SetDriving(true); player.enabled = false;
            player.drive.enabled = false; player.drive.body.isKinematic = true;
            typeof(VolcanoMissionDirector).GetProperty("State").SetValue(player.mission, VolcanoMissionDirector.FlowState.Preparation);
            typeof(VolcanoMissionDirector).GetProperty("Phase").SetValue(player.mission, VolcanoMissionDirector.MissionPhase.Refill);
            route = navigation.lakeRoad.Reverse().Select(t => t.position).ToArray();
            pathLength = 0; for (int i = 1; i < route.Length; i++) pathLength += EvacuationResident.FlatDistance(route[i - 1], route[i]);
            report.Add("Real rendered Play Mode frames, saved truck/road/shore. A kinematic fixture moves the truck along the route and parks at four angles; this is a presentation/pump regression, not a manual driving playthrough. Intro/deadline and input are disabled only in this fixture.");
            Save();
            minimumArrowClearance = float.PositiveInfinity; peakGuideSpeed = peakScreenSpeed = 0; samples = switches = 0;
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = message; }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new Exception(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase);
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.time - phaseAt;
        if (phase == 0)
        {
            if (age > 1.5f && navigation.UsesDirectionalArrow)
            {
                Vector3 guide = navigation.CurrentTarget.position;
                Vector2 marker = navigation.marker.anchoredPosition;
                if (samples > 0 && Time.deltaTime < .15f)
                {
                    peakGuideSpeed = Mathf.Max(peakGuideSpeed, Vector3.Distance(guide, lastGuide) / Mathf.Max(.001f, Time.deltaTime));
                    peakScreenSpeed = Mathf.Max(peakScreenSpeed, Vector2.Distance(marker, lastMarker) / Mathf.Max(.001f, Time.deltaTime));
                }
                samples++; lastGuide = guide; lastMarker = marker;
                minimumArrowClearance = Mathf.Min(minimumArrowClearance, LineClearance(navigation.ring));
            }
            float progress = Mathf.Min(pathLength, age * 18);
            if (!capturedHud && progress > pathLength * .4f)
            { ScreenCapture.CaptureScreenshot("Logs/TruckPresentation/01-road-hud.png"); capturedHud = true; }
            Place(RoutePoint(progress, out var forward) + Vector3.up * 1.5f, Quaternion.LookRotation(forward));
            if (progress < pathLength) return;
            Check("Continuous road guide on entire saved route", samples > 100 && peakGuideSpeed < 55,
                $"frames={samples} peakGuide={peakGuideSpeed:F2}m/s peakScreen={peakScreenSpeed:F2}px/s");
            Check("HUD guide cannot jump across the view", peakScreenSpeed < 1100, $"peak={peakScreenSpeed:F2} canvas units/s");
            Check("Arrow edges stay above road between vertices", minimumArrowClearance > .09f, $"minimum={minimumArrowClearance:F3}m");
            Capture("01-road-arrow", player.viewCamera.transform.position, player.viewCamera.transform.position + player.viewCamera.transform.forward * 35);
            samples = 0; switches = 0; Next(1); return;
        }
        if (phase == 1)
        {
            // Oscillate around the old route/parking threshold to catch arrow-circle chatter.
            Place(station.transform.position + new Vector3(0, 1.5f, -navigation.drivingMarkerLookAhead * 1.4f + .25f * Mathf.Sin(age * 25)), Quaternion.identity);
            if (age > .6f)
            {
                if (samples > 0 && previousArrow != navigation.UsesDirectionalArrow) switches++;
                previousArrow = navigation.UsesDirectionalArrow; samples++;
            }
            if (age < 2) return;
            Check("Route to lake transition has hysteresis", switches == 0, "mode switches=" + switches);
            Park(); Next(2); return;
        }
        if (phase == 2)
        {
            if (age < .3f) return;
            Check("Original lake interaction connects pump " + parking, station.CanFill && station.Interact(), player.hose.Status);
            Next(3); return;
        }
        if (phase == 3)
        {
            if (age < .4f) return;
            float clearance = LineClearance(player.hose.hoseLine) - player.hose.hoseDiameter * .5f;
            if (clearance <= .015f) Capture("hose-failure-" + parking, station.transform.position + new Vector3(-18, 10, -21), worstLinePoint);
            Check("Hose clears bank including interpolated segments " + parking, clearance > .015f, $"skin clearance={clearance:F3}m vertices={player.hose.hoseLine.positionCount} worst={worstLinePoint:F3}");
            float strainer = StrainerClearance();
            Check("Strainer stays above lakebed " + parking, strainer >= .025f, $"minimum mesh-bounds clearance={strainer:F3}m");
            Check("Grounded intake still pumps real lake water " + parking, player.hose.IsPumping && player.hose.CurrentWaterSource == station.water && station.water.ContainsIntake(player.hose.nozzle.position), player.hose.Status);
            if (parking == 0)
                Capture("02-grounded-lake-hose", station.transform.position + new Vector3(-18, 10, -21), Vector3.Lerp(player.hose.hoseSocket.position, player.hose.nozzle.position, .55f));
            if (++parking < 4) { station.Cancel(); player.hose.SetTankLitres(0); Park(); Next(2); return; }
            Next(4); return;
        }
        if (phase == 4)
        {
            if (player.hose.Tank01 < 1) return;
            if (station.IsFilling) return;
            Check("Tank fills to 2000L and original auto-retract is preserved", !player.hose.IsDeployed && player.IsDriving, "litres=" + player.hose.TankLitres);
            ProbeCloseCannon();
            Check("Close aim cannot alternate opposite barrel poses", lastCannonStep < 3.1f, $"peak yaw change at 60Hz={lastCannonStep:F3} degrees (before fix 179.75)");
            Check("Close, vertical and distant targets settle without oscillation", settledCannonMovement < .15f, $"largest movement in final 20 samples={settledCannonMovement:F3} degrees");
            FireCombatChecks.Run();
            Check("Existing ballistic water and damage regressions", File.ReadAllText("Logs/FireCombatChecks.json").Contains("\"allPassed\": true"), "See FireCombatChecks.json");
            Finish(null);
        }
    }
    static void Park()
    {
        Place(station.transform.position + Vector3.up * 1.5f, Quaternion.Euler(0, parking * 90, 0));
        Follow.Invoke(player, null); Physics.SyncTransforms();
    }
    static void Place(Vector3 point, Quaternion rotation)
    { player.drive.transform.SetPositionAndRotation(point, rotation); Follow.Invoke(player, null); }
    static Vector3 RoutePoint(float progress, out Vector3 forward)
    {
        for (int i = 1; i < route.Length; i++)
        {
            float length = EvacuationResident.FlatDistance(route[i - 1], route[i]);
            if (length >= progress) { forward = Vector3.ProjectOnPlane(route[i] - route[i - 1], Vector3.up).normalized; return Vector3.Lerp(route[i - 1], route[i], progress / Mathf.Max(.001f, length)); }
            progress -= length;
        }
        forward = Vector3.forward; return route[route.Length - 1];
    }
    static float GroundY(Vector3 point) => TruckLakeRouteTools.Ground(point).y;
    static float LineClearance(LineRenderer line)
    {
        float minimum = float.PositiveInfinity;
        for (int i = 1; i < line.positionCount; i++)
            for (int j = 0; j <= 8; j++)
            {
                Vector3 p = Vector3.Lerp(line.GetPosition(i - 1), line.GetPosition(i), j / 8f);
                float clearance = p.y - GroundY(p);
                if (clearance < minimum) { minimum = clearance; worstLinePoint = p; }
            }
        return minimum;
    }
    static float StrainerClearance()
    {
        float minimum = float.PositiveInfinity;
        foreach (var mesh in player.hose.nozzle.GetComponentsInChildren<MeshFilter>())
        {
            Bounds b = mesh.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = mesh.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                minimum = Mathf.Min(minimum, p.y - GroundY(p));
            }
        }
        return minimum;
    }
    static void Check(string name, bool passed, string evidence)
    {
        report.Add((passed ? "PASS " : "FAIL ") + name + ": " + evidence); Save();
        if (!passed) throw new Exception(name + ": " + evidence);
    }
    static void Next(int value) { phase = value; phaseAt = Time.time; deadline = EditorApplication.timeSinceStartup + 150; File.WriteAllText("Logs/TruckPresentation/progress.txt", "phase=" + phase + " parking=" + parking); }
    static void Save() => File.WriteAllLines("Logs/TruckPresentation/checks.txt", report);
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log; Application.runInBackground = previousBackground;
        report.Add(error == null ? "COMPLETED " + DateTime.UtcNow.ToString("O") : "FAILED " + error); Save();
        EditorApplication.ExitPlaymode();
    }
    [MenuItem("GameJam/Final Loop/Probe close cannon")]
    public static void ProbeCloseCannon()
    {
        var root = new GameObject("Temporary near-aim probe") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var truck = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab"), root.transform);
            truck.transform.position = new Vector3(12000, 300, 12000);
            var cannon = truck.GetComponent<FireTruckWaterCannon>(); cannon.enabled = false;
            cannon.ignoredCollisionRoot = truck.transform;
            var camera = new GameObject("Near aim camera").AddComponent<Camera>(); camera.enabled = false; camera.transform.SetParent(root.transform);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(root.transform); cube.transform.localScale = Vector3.one * .1f;
            cannon.aimCamera = camera;
            var lines = new List<string>(); float maxStep = 0; settledCannonMovement = 0;
            foreach (Vector3 offset in new[] { Vector3.forward * .2f, new Vector3(0, -8, .2f), Vector3.down * 8, new Vector3(0, 8, .2f), new Vector3(30, 2, -20) })
            {
                cube.transform.position = cannon.pitchPivot.position + offset;
                camera.transform.position = cube.transform.position + Vector3.up * 4; camera.transform.LookAt(cube.transform.position);
                Physics.SyncTransforms(); float settled = 0;
                for (int i = 0; i < 120; i++)
                {
                    Quaternion before = cannon.yawPivot.localRotation, pitch = cannon.pitchPivot.localRotation;
                    cannon.TickSpray(1f / 60f);
                    float step = Quaternion.Angle(before, cannon.yawPivot.localRotation);
                    maxStep = Mathf.Max(maxStep, step);
                    if (i >= 100) settled += step + Quaternion.Angle(pitch, cannon.pitchPivot.localRotation);
                }
                settledCannonMovement = Mathf.Max(settledCannonMovement, settled);
                lines.Add($"target={offset} yaw={cannon.yawPivot.localEulerAngles.y:F2} settledMovement={settled:F3}");
            }
            lastCannonStep = maxStep; lines.Add("MAX_STEP=" + maxStep);
            Directory.CreateDirectory("Logs/TruckPresentation"); File.WriteAllLines("Logs/TruckPresentation/close-cannon.txt", lines);
        }
        finally { Object.DestroyImmediate(root); }
    }
    [MenuItem("GameJam/Final Loop/Inspect truck presentation")]
    public static void Inspect()
    {
        var p = Object.FindFirstObjectByType<FireRescuePlayer>();
        var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        var lines = new List<string> { $"Scene={p.gameObject.scene.path} dirty={p.gameObject.scene.isDirty} play={Application.isPlaying}" };
        lines.Add("HOSE " + EditorJsonUtility.ToJson(p.hose));
        lines.Add($"LAKE park={p.mission.fillStation.transform.position:F3} intake={p.mission.fillStation.intakePoint.position:F3} waterY={p.mission.fillStation.water.SurfaceY:F3}");
        lines.Add("CANNON " + EditorJsonUtility.ToJson(p.cannon));
        foreach (var r in p.drive.GetComponentsInChildren<MeshRenderer>(true))
            lines.Add($"MESH {Path(r.transform)} enabled={r.enabled} active={r.gameObject.activeInHierarchy} mesh={r.GetComponent<MeshFilter>()?.sharedMesh?.name} bounds={r.bounds} materials={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "null"))}");
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var urp = c.GetUniversalAdditionalCameraData();
            lines.Add($"CAMERA {Path(c.transform)} enabled={c.enabled} mask={c.cullingMask} AA={urp.antialiasing} type={urp.renderType} stack={string.Join(",", urp.cameraStack?.Select(s=>s.name) ?? Array.Empty<string>())}");
        }
        foreach (var t in nav.lakeRoad) lines.Add("ROAD " + t.position.ToString("F3"));
        Directory.CreateDirectory("Logs/TruckPresentation");
        Capture("inspect-truck", p.drive.transform.position + new Vector3(23, 19, 12), p.drive.transform.position + Vector3.up * 9);
        var renderers = p.cannon.yawPivot.parent.GetComponentsInChildren<MeshRenderer>();
        var defaults = renderers.Select(r => r.enabled).ToArray();
        try
        {
            foreach (var r in renderers) r.enabled = false;
            Capture("inspect-body-only", p.drive.transform.position + new Vector3(23, 19, 12), p.drive.transform.position + Vector3.up * 9);
        }
        finally { for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = defaults[i]; }
        Vector3 end = p.mission.fillStation.intakePoint.position;
        Vector3 start = p.mission.fillStation.transform.position + p.drive.transform.TransformVector(p.drive.transform.InverseTransformPoint(p.hose.hoseSocket.position));
        for (int i = 0; i <= 20; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, i / 20f);
            lines.Add($"SHORE {point:F3} terrain={TruckLakeRouteTools.Ground(point).y:F3} hits={string.Join(",", Physics.RaycastAll(point + Vector3.up * 30, Vector3.down, 90, p.hose.hoseCollisionMask, QueryTriggerInteraction.Ignore).Select(h=>$"{h.collider.name}:{h.point.y:F3}"))}");
        }
        File.WriteAllLines("Logs/TruckPresentation/inspect.txt", lines);
    }
    static void Capture(string name, Vector3 from, Vector3 to)
    {
        var go = new GameObject("Truck presentation review camera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>(); cam.enabled = false; cam.transform.position = from; cam.transform.LookAt(to);
        cam.useOcclusionCulling = false; cam.fieldOfView = 50;
        cam.cullingMask &= ~(1 << LayerMask.NameToLayer("FirstPersonHands"));
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var rt = new RenderTexture(1280, 800, 24);
        var tex = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        var old = RenderTexture.active;
        try
        {
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes("Logs/TruckPresentation/" + name + ".png", tex.EncodeToPNG());
        }
        finally { RenderTexture.active = old; Object.DestroyImmediate(go); Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); }
    }
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
}
