using System;
using System.Collections;
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

/// <summary>Physical wheel-contact and presentation checks, confined to an unsaved Play Mode fixture.</summary>
[InitializeOnLoad]
public static class TruckGroundFeelChecks
{
    const string Pending = "TruckGroundFeelChecks.Pending";
    const string Folder = "Logs/TruckGroundFeel";
    static readonly Vector3 Origin = new Vector3(12000, 300, 12000);
    static readonly List<string> report = new List<string>();
    static FireRescuePlayer player;
    static FireTruckDrive drive;
    static Component effects;
    static TruckGroundFeelRunner runner;
    static GameObject fixtures;
    static TerrainData terrainData, hillData;
    static bool running, previousBackground, previousFog, captureOnly;
    static float previousTimeScale, restHeight;
    static string runtimeError, stage;
    static double deadline;

    static TruckGroundFeelChecks()
    {
        EditorApplication.update += Watch;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && running) Finish("Interrupted.", false);
            if (state == PlayModeStateChange.EnteredEditMode) RestoreSceneDirtyFlag();
        };
    }

    [MenuItem("GameJam/Final Loop/Check truck ground feel")]
    public static void Run() => Begin(false);

    [MenuItem("GameJam/Final Loop/Capture truck ground feel")]
    public static void CaptureOnly() => Begin(true);

    static void Begin(bool shortCapture)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use idle Edit Mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool originalDirty = scene.isDirty;
        if (originalDirty)
        {
            Directory.CreateDirectory("Library/TruckGroundFeelReview");
            if (!EditorSceneManager.SaveScene(scene, "Library/TruckGroundFeelReview/unsaved-preserved-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".unity", true))
                throw new InvalidOperationException("The unsaved scene backup could not be created.");
        }
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.txt", "STARTING " + DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(Pending + ".OriginalDirty", originalDirty);
        SessionState.SetString(Pending + ".OriginalScenePath", scene.path);
        SessionState.SetBool(Pending + ".CaptureOnly", shortCapture);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void RestoreSceneDirtyFlag()
    {
        if (!SessionState.GetBool(Pending + ".OriginalDirty", false)) return;
        string path = SessionState.GetString(Pending + ".OriginalScenePath", "");
        var scene = string.IsNullOrEmpty(path) ? UnityEngine.SceneManagement.SceneManager.GetActiveScene() : UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        // Unity can restore the live pre-play contents with a cleared dirty flag.
        // Keep those unsaved edits visibly unsaved without writing the source scene.
        if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
        SessionState.SetBool(Pending + ".OriginalDirty", false);
        SessionState.EraseString(Pending + ".OriginalScenePath");
    }

    static void Start()
    {
        report.Clear(); runtimeError = null; running = true;
        captureOnly = SessionState.GetBool(Pending + ".CaptureOnly", false);
        SessionState.EraseBool(Pending + ".CaptureOnly");
        previousBackground = Application.runInBackground; previousTimeScale = Time.timeScale; previousFog = RenderSettings.fog;
        Application.runInBackground = true; Time.timeScale = 1; RenderSettings.fog = false;
        Application.logMessageReceived += Log;
        try
        {
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            if (!player || !player.drive) throw new InvalidOperationException("The authored FireRescuePlayer/truck is required.");
            drive = player.drive;
            player.mission.enabled = false; player.mission.story.enabled = false; player.mission.cinematic.enabled = false;
            player.mission.sequence.enabled = false; player.gameFlow.enabled = false;
            player.SetCinematicLocked(false); player.SetDriving(true); player.enabled = false;
            effects = drive.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == "FireTruckGroundEffects");
            Check("Authored truck creates its wheel-ground effects", effects, "scaled production truck, no replacement wheel rig");
            Check("Original responsive driving is authored", !drive.progressiveHandling && drive.smoothPedals, "progressiveHandling=" + drive.progressiveHandling);
            BuildTerrain();
            Place(Origin);
            report.Add("Actual scene truck, WheelColliders and Rigidbody on temporary Unity Terrain using the scene's terrain layers. Inputs use SetInput; no velocity or rotation writes while measuring driving. Only fixture placement/reset and the airborne initial condition write a body pose/velocity. No saved scene or source terrain is changed.");
            runner = new GameObject("Truck ground feel verification runner").AddComponent<TruckGroundFeelRunner>();
            runner.StartCoroutine(Guarded(Checks()));
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static IEnumerator Checks()
    {
        Stage("settling"); drive.SetInput(0, 0, true);
        yield return new WaitForSeconds(2);
        Check("Four real wheels settle on TerrainCollider", Grounded() == 4, "grounded=" + Grounded());
        restHeight = drive.body.position.y - Origin.y;
        long stationary = Total();
        yield return new WaitForSeconds(.5f);
        Check("Standing truck does not paint tracks or raise soil", Total() == stationary && Emitters() == 0, Counts());

        Stage("pedal and steering response");
        float pedalAt = Time.fixedTime, previousMotionAt = Time.fixedTime, previousSpeed = drive.SpeedKph / 3.6f, peakAcceleration = 0, timeTo50 = -1;
        Action sampleMotion = () =>
        {
            float speed = drive.SpeedKph / 3.6f, dt = Time.fixedTime - previousMotionAt;
            if (dt > .001f) peakAcceleration = Mathf.Max(peakAcceleration, (speed - previousSpeed) / dt);
            previousSpeed = speed; previousMotionAt = Time.fixedTime;
            if (timeTo50 < 0 && drive.SpeedKph >= 50) timeTo50 = Time.fixedTime - pedalAt;
        };
        drive.SetInput(1, 1, false);
        yield return new WaitForFixedUpdate();
        sampleMotion();
        Check(drive.progressiveHandling ? "Digital throttle ramps before reaching full power" : "Original throttle responds promptly", drive.AppliedThrottle > 0 && drive.AppliedThrottle < .8f, "applied=" + drive.AppliedThrottle);
        Check(drive.progressiveHandling ? "First steering step is progressive" : "Original steering responds promptly", drive.AppliedSteering > 0 && drive.AppliedSteering < .7f, "applied=" + drive.AppliedSteering);
        drive.SetInput(1, 0, false);
        Vector3 start = drive.body.position;
        int peakDust = 0, peakEmitters = 0;
        float began = Time.time;
        Stage("physical forward drive");
        while (Time.time - began < 3)
        {
            sampleMotion();
            peakDust = Mathf.Max(peakDust, Particles()); peakEmitters = Mathf.Max(peakEmitters, Emitters());
            yield return new WaitForFixedUpdate();
        }
        Check("Pedal forces move the loaded truck", Vector3.Distance(start, drive.body.position) > 8 && drive.SpeedKph > 12,
            "distance=" + Vector3.Distance(start, drive.body.position).ToString("F2") + "m speed=" + drive.SpeedKph.ToString("F1") + "km/h");
        Check("Rolling grounded tires create visible track geometry", Total() >= stationary + 20 && Active() > 12 && TrackMeshes().Any(m => m.vertexCount >= 16), Counts());
        Check("Each wheel leaves its own contact trace", Enumerable.Range(0, drive.wheels.Length).All(i => Convert.ToInt64(effects.GetType().GetMethod("GetWheelSegmentTotal").Invoke(effects, new object[] { i })) > 0), "FL/FR/RL/RR segment counters");
        Check("Soil dust is emitted from the four contact patches", peakEmitters == 4 && peakDust > 8, "emitters=" + peakEmitters + " particles=" + peakDust);
        yield return new WaitForEndOfFrame(); Capture("01-forward-treads-and-dust", true);
        yield return new WaitForFixedUpdate();
        yield return new WaitForEndOfFrame(); Capture("02-rear-contact-patches", false);
        yield return new WaitForFixedUpdate();
        yield return new WaitForEndOfFrame(); Capture("03-low-rear-dust", false, true);
        if (captureOnly)
        {
            if (!drive.progressiveHandling) { sampleMotion(); CheckAcceleration(timeTo50, peakAcceleration); CheckSteeringRate(); }
            yield break;
        }
        Check("Track geometry sits on the ground", MaximumTrackHeightError() < .25f, "max distance above terrain=" + MaximumTrackHeightError().ToString("F3"));

        Stage("loaded truck acceleration to 50");
        while (timeTo50 < 0 && Time.fixedTime - pedalAt < (drive.progressiveHandling ? 8 : 3))
        {
            sampleMotion();
            yield return new WaitForFixedUpdate();
        }
        sampleMotion();
        CheckAcceleration(timeTo50, peakAcceleration);

        Stage("braking"); float brakeAt = Time.fixedTime, brakeStartSpeed = drive.SpeedKph, brakeDistance = 0;
        Vector3 previousBrakePosition = drive.body.position;
        drive.SetInput(0, 0, true);
        yield return new WaitForFixedUpdate();
        brakeDistance += Vector3.Distance(previousBrakePosition, drive.body.position); previousBrakePosition = drive.body.position;
        Check(drive.progressiveHandling ? "Service brakes build pressure progressively" : "Original brakes respond promptly", drive.AppliedBrake > 0 && drive.AppliedBrake < 1, "applied=" + drive.AppliedBrake);
        began = Time.time;
        while (drive.SpeedKph > .6f && Time.time - began < 4)
        {
            yield return new WaitForFixedUpdate();
            brakeDistance += Vector3.Distance(previousBrakePosition, drive.body.position); previousBrakePosition = drive.body.position;
        }
        Check("Real service brakes stop the truck within a controlled distance", drive.SpeedKph < 1.2f && brakeDistance < 60,
            "from=" + brakeStartSpeed.ToString("F1") + "km/h elapsed=" + (Time.fixedTime - brakeAt).ToString("F2") + "s distance=" + brakeDistance.ToString("F2") + "m final=" + drive.SpeedKph.ToString("F2") + "km/h");
        yield return new WaitForSeconds(.6f);
        stationary = Total(); yield return new WaitForSeconds(.5f);
        Check("Stopped tires stop adding marks and dust", Total() == stationary && Emitters() == 0, Counts());

        Stage("pause");
        Time.timeScale = 0;
        // This coroutine resumes after Update; allow the component's next Update to
        // observe the pause before measuring the frozen particle simulation.
        yield return null;
        long pausedMarks = Total(); int pausedParticles = Particles();
        for (int i = 0; i < 8; i++) yield return null;
        Check("Pause freezes marks and existing dust", Total() == pausedMarks && Particles() == pausedParticles, Counts());
        Time.timeScale = 1;

        if (drive.progressiveHandling)
        {
            Stage("reversing from a blocked forward pedal");
            Place(Origin + Vector3.left * 100, true); drive.SetDriven(true);
            yield return new WaitForSeconds(1.5f);
            Bounds bumper = drive.GetComponent<BoxCollider>().bounds;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary blocked-pedal wall"; wall.transform.SetParent(fixtures.transform);
            wall.transform.localScale = new Vector3(bumper.size.x + 8, bumper.size.y + 10, 1.5f);
            wall.transform.position = new Vector3(bumper.center.x, bumper.center.y, bumper.max.z + .85f);
            Physics.SyncTransforms();
            drive.SetInput(1, 0, false); yield return new WaitForSeconds(1);
            Check("Wall holds the real truck while the forward pedal remains pressed", drive.AppliedThrottle > .9f && Mathf.Abs(Vector3.Dot(drive.body.linearVelocity, drive.transform.forward)) < .8f,
                "pedal=" + drive.AppliedThrottle.ToString("F3") + " signedSpeed=" + Vector3.Dot(drive.body.linearVelocity, drive.transform.forward).ToString("F3") + "m/s");
            drive.SetInput(-1, 0, false); yield return new WaitForFixedUpdate();
            Check("Reverse request never powers the old forward pedal", drive.wheels.All(w => w.motorTorque <= .01f),
                "old pedal=" + drive.AppliedThrottle.ToString("F3") + " torques=" + string.Join(",", drive.wheels.Select(w => w.motorTorque.ToString("F1"))));
            bool forwardKick = false; float reversalAt = Time.time;
            while (Time.time - reversalAt < 1.5f)
            {
                if (drive.AppliedThrottle > .001f) forwardKick |= drive.wheels.Any(w => w.motorTorque > .01f);
                yield return new WaitForFixedUpdate();
            }
            Check("Reverse drive begins after the old pedal releases", !forwardKick && drive.AppliedThrottle < -.1f && Vector3.Dot(drive.body.linearVelocity, drive.transform.forward) < -.5f,
                "pedal=" + drive.AppliedThrottle.ToString("F3") + " signedSpeed=" + Vector3.Dot(drive.body.linearVelocity, drive.transform.forward).ToString("F3") + "m/s");
            Object.DestroyImmediate(wall); drive.SetDriven(false); drive.SetDriven(true); Place(Origin, true);
            yield return new WaitForSeconds(1.5f);
        }

        Stage("reverse"); start = drive.body.position; long reverseStart = Total();
        drive.SetInput(-1, 0, false); yield return new WaitForSeconds(2.5f);
        Check("Reverse drives backward with contact tracks", Vector3.Dot(drive.body.position - start, drive.transform.forward) < -2 && Total() > reverseStart + 8 && Emitters() > 0, Counts());

        Stage("turning"); Place(Origin + Vector3.forward * 150, true);
        yield return new WaitForSeconds(1.5f);
        float yaw = drive.body.rotation.eulerAngles.y, turned = 0, peakTilt = 0;
        began = Time.time;
        while (Time.time - began < 4)
        {
            float speed = Vector3.Dot(drive.body.linearVelocity, drive.transform.forward) * 3.6f;
            drive.SetInput(speed > 27 ? 0 : Mathf.Clamp01((26 - speed) * .13f + .1f), .65f, speed > 30);
            yield return new WaitForFixedUpdate();
            float next = drive.body.rotation.eulerAngles.y;
            turned += Mathf.Abs(Mathf.DeltaAngle(yaw, next)); yaw = next;
            peakTilt = Mathf.Max(peakTilt, Vector3.Angle(drive.transform.up, Vector3.up));
        }
        Check(drive.progressiveHandling ? "Progressive steering turns the real truck without tipping" : "Original steering turns the real truck without tipping", turned > 15 && peakTilt < 35, "turn=" + turned.ToString("F1") + " maximum tilt=" + peakTilt.ToString("F1"));
        CheckSteeringRate();

        Stage("teleport continuity"); Place(Origin + Vector3.right * 110, true);
        yield return new WaitForSeconds(1.5f);
        drive.SetInput(1, 0, false); yield return new WaitForSeconds(1.5f);
        Check("Teleport starts new contact strips without long bridges", MaximumTrackTriangleEdge() < 5, "longest triangle edge=" + MaximumTrackTriangleEdge().ToString("F3") + "m");

        Stage("airborne"); Place(Origin + new Vector3(0, 60, -100), true);
        drive.body.useGravity = false; drive.body.linearVelocity = Vector3.forward * 8;
        yield return new WaitForFixedUpdate(); yield return new WaitForFixedUpdate();
        long airborneStart = Total();
        drive.SetInput(1, 0, false); yield return new WaitForSeconds(.6f);
        Check("Airborne wheels never paint the air or emit soil", Grounded() == 0 && Total() == airborneStart && Emitters() == 0, Counts());
        drive.body.useGravity = true;

        Stage("submerged ground"); BuildWater(); Place(Origin, true);
        yield return new WaitForSeconds(1.5f);
        long wetStart = Total();
        drive.SetInput(1, 0, false); yield return new WaitForSeconds(1.5f);
        Check("Wet lakebed contact does not produce dry tracks or dust", Grounded() == 4 && Total() == wetStart && Emitters() == 0, Counts());
        Object.DestroyImmediate(fixtures.transform.Find("Temporary water exclusion volume").gameObject);

        Stage("hill hold and uphill pull-away");
        Vector3 hillPoint = BuildHill(out var hillRotation);
        Place(hillPoint, true, hillRotation);
        drive.SetInput(0, 0, false);
        yield return new WaitForSeconds(2);
        start = drive.body.position;
        yield return new WaitForSeconds(2);
        Check("Hill hold supports the loaded truck on a real slope", Grounded() == 4 && Vector3.Distance(start, drive.body.position) < .6f && drive.SpeedKph < 1.2f,
            "slope=9.7deg drift=" + Vector3.Distance(start, drive.body.position).ToString("F3") + "m speed=" + drive.SpeedKph.ToString("F2") + "km/h grounded=" + Grounded());
        start = drive.body.position;
        drive.SetInput(.65f, 0, false);
        yield return new WaitForSeconds(2);
        Check("Truck pulls uphill without reversing or locking its brakes", Vector3.Dot(drive.body.position - start, hillRotation * Vector3.forward) > 2 && drive.AppliedBrake < .05f,
            "uphill travel=" + Vector3.Dot(drive.body.position - start, hillRotation * Vector3.forward).ToString("F2") + "m brake=" + drive.AppliedBrake.ToString("F3"));

        Stage("bounded fading fixture");
        drive.SetInput(0, 0, true); Place(Origin + Vector3.right * -160, true);
        Type effectType = effects.GetType(); Object.DestroyImmediate(effects);
        drive.gameObject.SetActive(false);
        effects = drive.gameObject.AddComponent(effectType);
        Set("maximumSegments", 64); Set("trackLifetime", 1.5f); Set("looseTerrainLayers", terrainData.terrainLayers);
        drive.gameObject.SetActive(true); drive.SetDriven(true);
        yield return new WaitForSeconds(1.5f);
        drive.SetInput(1, 0, false); yield return new WaitForSeconds(3.5f);
        Check("Track buffer wraps within its configured allocation", Total() > 64 && Active() <= 64 && TrackMeshes().All(m => m.vertexCount <= 64 * 4), Counts());
        drive.SetInput(0, 0, true); yield return new WaitForSeconds(1.5f);
        yield return new WaitForSeconds(2);
        Check("Old tracks fade out and release the visible slots", Active() == 0, Counts());
        Check("Original wheels and body collision remain enabled", drive.body.detectCollisions && drive.GetComponent<BoxCollider>().enabled && drive.wheels.All(w => w.enabled), "no collider bypass");
    }

    static void CheckAcceleration(float timeTo50, float peakAcceleration)
    {
        bool progressive = drive.progressiveHandling;
        Check(progressive ? "Loaded truck accelerates progressively to road speed" : "Original fast acceleration reaches road speed", timeTo50 >= (progressive ? 1.2f : .35f) && timeTo50 <= (progressive ? 8.2f : 3f),
            "0-50km/h=" + timeTo50.ToString("F2") + "s peak acceleration=" + peakAcceleration.ToString("F2") + "m/s² final=" + drive.SpeedKph.ToString("F1") + "km/h");
        Check(progressive ? "Acceleration remains within a controlled physical range" : "Original acceleration remains within its responsive range", peakAcceleration > 1 && peakAcceleration < (progressive ? 20 : 65),
            "measured fixed-step peak=" + peakAcceleration.ToString("F2") + "m/s²");
    }

    static void CheckSteeringRate()
    {
        float slow = drive.FullSteeringYawRate(30), fast = drive.FullSteeringYawRate(60);
        Check(drive.progressiveHandling ? "Fast steering respects lateral acceleration" : "Original responsive steering rate is retained", drive.progressiveHandling ? fast < slow : fast >= slow,
            "30km/h=" + slow.ToString("F1") + "deg/s 60km/h=" + fast.ToString("F1"));
    }

    static void BuildTerrain()
    {
        fixtures = new GameObject("Temporary truck ground feel fixtures");
        Terrain source = Terrain.activeTerrains.FirstOrDefault(t => t.terrainData && t.terrainData.terrainLayers.Length > 0);
        terrainData = new TerrainData { name = "Temporary contact test TerrainData", heightmapResolution = 33, size = new Vector3(1000, 1, 1000) };
        var loose = effects.GetType().GetField("looseTerrainLayers").GetValue(effects) as TerrainLayer[];
        TerrainLayer[] palette = loose != null && loose.Any(layer => layer) ? loose.Where(layer => layer).ToArray() : source ? source.terrainData.terrainLayers : Array.Empty<TerrainLayer>();
        ApplyTerrainPalette(terrainData, palette);
        var ground = Terrain.CreateTerrainGameObject(terrainData); ground.name = "Temporary real terrain contact surface"; ground.transform.SetParent(fixtures.transform); ground.transform.position = Origin - new Vector3(500, 0, 500);
        if (source) ground.GetComponent<Terrain>().materialTemplate = source.materialTemplate;
        var light = new GameObject("Temporary ground feel fill light").AddComponent<Light>(); light.transform.SetParent(fixtures.transform); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45, -30, 0);
        Physics.SyncTransforms();
    }

    static void ApplyTerrainPalette(TerrainData data, TerrainLayer[] palette)
    {
        if (palette == null || palette.Length == 0) throw new InvalidOperationException("A loose soil TerrainLayer is required for the fixture.");
        data.terrainLayers = palette; data.alphamapResolution = 16;
        var map = new float[16, 16, data.alphamapLayers];
        for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++) map[z, x, 0] = 1;
        data.SetAlphamaps(0, 0, map);
        report.Add("FIXTURE soil=" + palette[0].name);
    }

    static Vector3 BuildHill(out Quaternion rotation)
    {
        const float length = 140, rise = 24;
        hillData = new TerrainData { name = "Temporary 9.7 degree hill TerrainData", heightmapResolution = 33, size = new Vector3(100, 30, length) };
        var height = new float[33, 33];
        for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) height[z, x] = z / 32f * rise / 30f;
        hillData.SetHeights(0, 0, height); ApplyTerrainPalette(hillData, terrainData.terrainLayers);
        var hill = Terrain.CreateTerrainGameObject(hillData); hill.name = "Temporary hill hold TerrainCollider"; hill.transform.SetParent(fixtures.transform);
        hill.transform.position = Origin + new Vector3(270, 20, 200);
        hill.GetComponent<Terrain>().materialTemplate = fixtures.GetComponentInChildren<Terrain>().materialTemplate;
        rotation = Quaternion.LookRotation(new Vector3(0, rise / length, 1).normalized, Vector3.up);
        Physics.SyncTransforms();
        return hill.transform.position + new Vector3(50, rise * .5f, length * .5f);
    }

    static void BuildWater()
    {
        var go = new GameObject("Temporary water exclusion volume"); go.transform.SetParent(fixtures.transform); go.transform.position = Origin;
        var box = go.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(250, 8, 250);
        var source = go.AddComponent<WaterSourceVolume>(); source.volume = box; source.surfaceY = Origin.y + 1; source.rejectDryTerrain = true;
        Physics.SyncTransforms();
    }

    static void Place(Vector3 groundPoint, bool settled = false, Quaternion? rotation = null)
    {
        drive.SetInput(0, 0, true);
        Quaternion pose = rotation ?? Quaternion.identity;
        Vector3 position = groundPoint + pose * Vector3.up * (settled ? restHeight + .12f : 3);
        if (!settled)
        {
            float lowest = drive.wheels.Min(w => w.transform.TransformPoint(w.center).y - w.radius * Mathf.Abs(w.transform.lossyScale.y));
            position.y = groundPoint.y + drive.transform.position.y - lowest + .7f;
        }
        drive.body.position = position; drive.body.rotation = pose; drive.transform.SetPositionAndRotation(position, pose);
        drive.body.linearVelocity = drive.body.angularVelocity = Vector3.zero;
        drive.body.WakeUp(); Physics.SyncTransforms();
    }

    static int Grounded() => drive.wheels.Count(w => w && w.GetGroundHit(out _));
    static long Total() => Read<long>("TotalSegmentsCreated");
    static int Active() => Read<int>("ActiveSegmentCount");
    static int Emitters() => Read<int>("EmittingWheelCount");
    static int Particles() => Read<int>("DustParticleCount");
    static string Counts() => "segments=" + Active() + " total=" + Total() + " emitting=" + Emitters() + " particles=" + Particles() + " grounded=" + Grounded();
    static T Read<T>(string name)
    {
        var property = effects.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null) throw new MissingMemberException(effects.GetType().Name, name);
        return (T)property.GetValue(effects);
    }
    static void Set(string name, object value)
    {
        var field = effects.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null) throw new MissingMemberException(effects.GetType().Name, name);
        field.SetValue(effects, value);
    }
    static IEnumerable<Mesh> TrackMeshes() { Mesh mesh = Read<Mesh>("TrackMesh"); if (mesh) yield return mesh; }
    static IEnumerable<Vector3> TrackVertices()
    {
        foreach (var mesh in TrackMeshes())
        {
            var colors = mesh.colors;
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) if (colors.Length <= i || colors[i].a > .01f) yield return vertices[i];
        }
    }
    static float MaximumTrackHeightError() => TrackVertices().Select(p => Mathf.Abs(p.y - Origin.y)).DefaultIfEmpty(float.PositiveInfinity).Max();
    static float MaximumTrackTriangleEdge()
    {
        float maximum = 0;
        foreach (Mesh mesh in TrackMeshes())
        {
            Vector3[] v = mesh.vertices; int[] triangle = mesh.triangles;
            for (int i = 0; i + 2 < triangle.Length; i += 3)
                for (int j = 0; j < 3; j++) maximum = Mathf.Max(maximum, Vector3.Distance(v[triangle[i + j]], v[triangle[i + (j + 1) % 3]]));
        }
        return maximum;
    }
    static void Capture(string name, bool side, bool lowRear = false)
    {
        var camera = new GameObject("Truck ground feel capture camera").AddComponent<Camera>(); camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.29f, .33f, .36f); camera.nearClipPlane = .15f; camera.farClipPlane = 400; camera.fieldOfView = 52;
        camera.GetUniversalAdditionalCameraData().requiresDepthTexture = true;
        Vector3 target = drive.GetComponent<BoxCollider>().bounds.center; float size = Mathf.Max(10, drive.GetComponent<BoxCollider>().bounds.extents.z);
        Vector3 offset = lowRear ? new Vector3(size * .9f, size * .17f, -size * 2f) : new Vector3(side ? size * 1.9f : size * .45f, size * .8f, -size * 2.1f);
        camera.transform.position = target + drive.transform.rotation * offset;
        camera.transform.LookAt(target - drive.transform.forward * size * (lowRear ? .7f : .3f) - Vector3.up * (lowRear ? size * .4f : 0));
        var rt = new RenderTexture(1600, 1000, 24); var texture = new Texture2D(1600, 1000, TextureFormat.RGB24, false); RenderTexture old = RenderTexture.active;
        bool asynchronousCompilation = ShaderUtil.allowAsyncCompilation;
        try
        {
            ShaderUtil.allowAsyncCompilation = false; camera.targetTexture = rt;
            RenderPhoto(camera, rt, texture, name);
            CaptureDiagnostics(camera, name);
            if (captureOnly) CaptureWithoutEffects(camera, rt, texture, name);
        }
        finally { ShaderUtil.allowAsyncCompilation = asynchronousCompilation; RenderTexture.active = old; camera.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); Object.DestroyImmediate(camera.gameObject); }
    }

    static void RenderPhoto(Camera camera, RenderTexture target, Texture2D texture, string name)
    {
        camera.Render(); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); texture.Apply();
        File.WriteAllBytes(Folder + "/" + name + ".png", texture.EncodeToPNG());
    }

    static Transform EffectsRoot() => effects.GetType().GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(effects) as Transform;

    static void CaptureDiagnostics(Camera camera, string name)
    {
        Transform root = EffectsRoot();
        MeshRenderer tracks = root ? root.GetComponent<MeshRenderer>() : null;
        ParticleSystem particles = root ? root.GetComponentInChildren<ParticleSystem>(true) : null;
        ParticleSystemRenderer dustRenderer = particles ? particles.GetComponent<ParticleSystemRenderer>() : null;
        Mesh mesh = Read<Mesh>("TrackMesh");
        float alpha = mesh ? mesh.colors.Select(c => c.a).DefaultIfEmpty().Max() : 0;
        Plane[] planes = GeometryUtility.CalculateFrustumPlanes(camera);
        report.Add("CAPTURE " + name + " camera=" + camera.transform.position.ToString("F2") + " " + Counts());
        report.Add("TRACK enabled=" + (tracks && tracks.enabled && tracks.gameObject.activeInHierarchy) + " inFrustum=" + (tracks && GeometryUtility.TestPlanesAABB(planes, tracks.bounds)) + " vertexAlphaMax=" + alpha.ToString("F3") + " shader=" + (tracks && tracks.sharedMaterial ? tracks.sharedMaterial.shader.name : "missing"));
        if (particles)
        {
            var values = new ParticleSystem.Particle[particles.main.maxParticles]; int count = particles.GetParticles(values);
            float maxCurrentAlpha = 0;
            for (int i = 0; i < count; i++)
            {
                maxCurrentAlpha = Mathf.Max(maxCurrentAlpha, values[i].GetCurrentColor(particles).a / 255f);
            }
            report.Add("DUST enabled=" + (dustRenderer && dustRenderer.enabled && dustRenderer.gameObject.activeInHierarchy) + " count=" + count + " currentAlphaMax=" + maxCurrentAlpha.ToString("F3") + " inFrustum=" + (dustRenderer && GeometryUtility.TestPlanesAABB(planes, dustRenderer.bounds)) + " shader=" + (dustRenderer && dustRenderer.sharedMaterial ? dustRenderer.sharedMaterial.shader.name : "missing"));
        }
        Save();
    }

    static void CaptureWithoutEffects(Camera camera, RenderTexture target, Texture2D image, string name)
    {
        Transform root = EffectsRoot();
        MeshRenderer tracks = root ? root.GetComponent<MeshRenderer>() : null;
        ParticleSystemRenderer dustRenderer = root ? root.GetComponentInChildren<ParticleSystemRenderer>(true) : null;
        bool tracksEnabled = tracks && tracks.enabled, dustEnabled = dustRenderer && dustRenderer.enabled;
        try
        {
            if (tracks) tracks.enabled = false;
            if (dustRenderer) dustRenderer.enabled = false;
            RenderPhoto(camera, target, image, name + "-without-effects");
        }
        finally
        {
            if (tracks) tracks.enabled = tracksEnabled;
            if (dustRenderer) dustRenderer.enabled = dustEnabled;
        }
    }
    static IEnumerator Guarded(IEnumerator checks)
    {
        while (running)
        {
            object next;
            try { if (!checks.MoveNext()) break; next = checks.Current; }
            catch (Exception ex) { Finish(ex.ToString()); yield break; }
            yield return next;
        }
        if (running) Finish(null);
    }
    static void Stage(string value) { stage = value; deadline = EditorApplication.timeSinceStartup + 60; report.Add("STAGE " + value); Save(); }
    static void Watch() { if (!running) return; if (runtimeError != null) Finish(runtimeError); else if (EditorApplication.timeSinceStartup > deadline) Finish("Timeout: " + stage); }
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Assert || type == LogType.Exception) runtimeError = message; }
    static void Check(string name, bool pass, string evidence) { report.Add((pass ? "PASS " : "FAIL ") + name + ": " + evidence); Save(); if (!pass) throw new Exception(name + ": " + evidence); }
    static void Save() => File.WriteAllLines(Folder + "/checks.txt", report);
    static void Finish(string error, bool exit = true)
    {
        if (!running) return; running = false;
        Application.logMessageReceived -= Log;
        if (drive) { drive.SetInput(0, 0, true); drive.body.useGravity = true; }
        Application.runInBackground = previousBackground; Time.timeScale = previousTimeScale; RenderSettings.fog = previousFog;
        report.Add(error == null ? "COMPLETED " + DateTime.UtcNow.ToString("O") : "FAILED " + error); Save();
        if (fixtures) Object.DestroyImmediate(fixtures);
        if (terrainData) Object.DestroyImmediate(terrainData);
        if (hillData) Object.DestroyImmediate(hillData);
        if (exit) EditorApplication.ExitPlaymode();
    }
}

public sealed class TruckGroundFeelRunner : MonoBehaviour { }
