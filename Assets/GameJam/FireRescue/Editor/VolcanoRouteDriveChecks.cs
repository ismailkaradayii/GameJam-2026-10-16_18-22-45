using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Bounded route probe. Navigation failures are reported as test limitations, not gameplay defects.</summary>
[InitializeOnLoad]
public static class VolcanoRouteDriveChecks
{
    const string Active = "GameJam.RouteDrive.Active", State = "GameJam.RouteDrive.State";
    const string Saved = "GameJam.RouteDrive.Report", Background = "GameJam.RouteDrive.Background";
    const string GamePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    const string MenuPath = "Assets/Scenes/MainMenu.unity", ReportPath = "Logs/VolcanoRouteDriveChecks.json";
    const float Cell = 3f, ProbeSpacing = 1.5f, SlopeLimit = 32f;
    [Serializable] public sealed class Sample
    {
        public float seconds, speedKph, signedForwardSpeedMetresPerSecond, requestedThrottle, requestedSteering;
        public bool requestedBrake;
        public Vector3 position, frontPoint, targetPoint;
        public int grounded;
        public float[] rearMotorTorque, forwardSlip;
        public string[] wheelGroundColliders;
        public Vector3[] wheelGroundNormals;
    }
    [Serializable] public sealed class SolidBlocker { public string collider; public int rejectedProbes; }
    [Serializable] public sealed class Rejection
    {
        public string type, collider;
        public Vector3 routeProbe, samplePosition;
        public float terrainHeight, chassisBottomHeight;
    }
    [Serializable] public sealed class Report
    {
        public string status, startedUtc, finishedUtc, detail, editorScene;
        public bool passed, teleportedTruck, changedVelocity;
        public float seconds, travelledMetres, maximumSpeedKph, distanceToTown, closestDistanceToTown;
        public Vector3 spawn, town;
        public int nodesExamined, maximumGrounded, minimumGrounded = 4;
        public float gridCellMetres = Cell, probeSpacingMetres = ProbeSpacing, maximumSlopeDegrees = SlopeLimit;
        public float clearanceHalfWidthMetres, clearanceHalfLengthMetres, closestReachableDistanceToTown;
        public bool fullVehicleFootprintChecked;
        public string routeSource;
        public int settledGroundedWheels;
        public float settledRootAboveSupportPlaneMetres, settledUnderbodyClearanceMetres;
        public Vector3[] settledLocalWheelContacts;
        public bool authoredRoadChecked, authoredApproachClear, authoredRemainderClear;
        public bool authoredApproachDrivenWithTerrainWarnings, bodyCollisionsEnabled;
        public int authoredApproachSegments, authoredRemainderSegments;
        public Vector3 authoredApproachEndpoint;
        public float authoredApproachEndpointDistanceToTown;
        public Rejection firstAuthoredRejection, firstRemainderRejection;
        public List<Rejection> authoredRoadRejections = new List<Rejection>();
        public float truckMassKg, authoredMotorTorquePerRearWheel, rearWheelRadiusMetres, acceleratingThrottle = 1f;
        public Vector3 bodyColliderLocalSize;
        public int rejectedWaterProbes, rejectedSlopeProbes, rejectedSolidProbes, rejectedOutsideTerrainProbes, rejectedChassisTerrainProbes, overlapBufferOverflows;
        public List<SolidBlocker> solidBlockers = new List<SolidBlocker>();
        public List<Vector3> reachableNodes = new List<Vector3>();
        public List<Vector3> plannedRoute = new List<Vector3>();
        public List<Sample> trajectory = new List<Sample>();
        public List<string> runtimeErrors = new List<string>();
    }
    static Report report;
    static FireTruckDrive drive;
    static FireRescuePlayer player;
    static Terrain[] terrains;
    static WaterSourceVolume[] waters;
    static List<Vector3> path;
    static int waypoint;
    static float started, nextSample, lastProgressTime, bestWaypointDistance;
    static Vector3 previousPosition, progressPosition;
    static double wallStarted;
    static bool[] checkedCells, walkable;
    static Vector3[] cells;
    static int nx, nz;
    static float minX, minZ;
    static float halfWidth = 2.15f, halfLength = 6.16f, wheelbase = 7.2f;
    static float settledRootOffset;
    static readonly Vector3[] wheelOffsets = new Vector3[4];
    static readonly Vector3[] supportSamples = new Vector3[4];
    static Vector3 routeProbe;
    static Rejection lastRejection;
    static BoxCollider truckBox;
    static readonly Collider[] overlaps = new Collider[128];
    static readonly MethodInfo FollowCamera = typeof(FireRescuePlayer).GetMethod("UpdateDrivingCamera", BindingFlags.Instance | BindingFlags.NonPublic);

    static VolcanoRouteDriveChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += ModeChanged;
        Application.logMessageReceived += Log;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }
    [MenuItem("GameJam/Integration/Check real truck route to town", true)]
    static bool CanBegin() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !SessionState.GetBool(Active, false);
    [MenuItem("GameJam/Integration/Check real truck route to town")]
    public static void Begin()
    {
        if (!CanBegin()) throw new InvalidOperationException("Start the route check in Edit Mode after compilation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scenes before the route check.");
        report = new Report { status = "running", startedUtc = DateTime.UtcNow.ToString("O"), detail = "Real WheelCollider physics; no truck teleport or velocity writes. Navigation is an Editor test driver." };
        SessionState.SetBool(Background, Application.runInBackground);
        SessionState.SetBool(Active, true);
        SessionState.SetString(State, "starting");
        Save();
        EditorSceneManager.OpenScene(GamePath);
        EditorApplication.EnterPlaymode();
    }
    static Report Current => report ?? (report = JsonUtility.FromJson<Report>(SessionState.GetString(Saved, "{}")) ?? new Report());
    public static Report AuditAuthoredRoadInEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Active, false))
            throw new InvalidOperationException("A geometric road audit requires inactive Edit Mode.");
        var previous = JsonUtility.FromJson<Report>(File.ReadAllText(ReportPath));
        if (previous.settledGroundedWheels < 3) throw new InvalidOperationException("A real settled wheel-support measurement is required first.");
        player = Object.FindFirstObjectByType<FireRescuePlayer>();
        drive = player ? player.drive : null;
        if (!drive) throw new InvalidOperationException("Open the authored Volcano scene before auditing the road.");
        terrains = Terrain.activeTerrains; waters = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
        truckBox = drive.GetComponent<BoxCollider>();
        halfWidth = truckBox.size.x * .5f + .35f; halfLength = truckBox.size.z * .5f + .35f;
        settledRootOffset = previous.settledRootAboveSupportPlaneMetres;
        for (int i = 0; i < drive.wheels.Length; i++)
            wheelOffsets[i] = drive.transform.InverseTransformPoint(drive.wheels[i].transform.TransformPoint(drive.wheels[i].center));
        report = new Report { status = "geometric_audit", spawn = drive.transform.position,
            detail = "Edit Mode corridor geometry using the last real settled suspension measurement; this is not a dynamic drive test.",
            settledRootAboveSupportPlaneMetres = settledRootOffset, fullVehicleFootprintChecked = true,
            clearanceHalfWidthMetres = halfWidth, clearanceHalfLengthMetres = halfLength };
        var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None);
        foreach (var house in houses) report.town += house.transform.position / houses.Length;
        ReadAuthoredRoad();
        report.passed = report.authoredRoadChecked && report.authoredApproachClear && report.authoredRemainderClear;
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText("Logs/VolcanoRoadClearanceAudit.json", JsonUtility.ToJson(report, true));
        return report;
    }
    static void Update()
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying || EditorApplication.isPaused || EditorApplication.isCompiling) return;
        try
        {
            string state = SessionState.GetString(State, "");
            if (state == "starting")
            {
                Application.runInBackground = true;
                started = Time.time; wallStarted = EditorApplication.timeSinceStartup;
                SessionState.SetString(State, "settling");
                return;
            }
            if (state == "settling")
            {
                if (Time.time - started < 3f) return;
                player = Object.FindFirstObjectByType<FireRescuePlayer>();
                drive = player ? player.drive : null;
                if (!drive || !drive.body || !player) throw new InvalidOperationException("Actual truck/player references are missing.");
                player.enabled = false; // Prevent keyboard/focus gating from overriding the test driver's commands.
                player.SetDriving(true);
                terrains = Terrain.activeTerrains;
                waters = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None);
                var houses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None);
                if (houses.Length != 5) throw new InvalidOperationException("Expected the five authored town houses.");
                Current.town = Vector3.zero;
                foreach (var house in houses) Current.town += house.transform.position / houses.Length;
                Current.spawn = drive.body.position;
                Current.truckMassKg = drive.body.mass;
                Current.authoredMotorTorquePerRearWheel = drive.motorTorque;
                Current.rearWheelRadiusMetres = drive.wheels[2].radius;
                truckBox = drive.GetComponent<BoxCollider>();
                if (!truckBox || drive.transform.lossyScale != Vector3.one) throw new InvalidOperationException("A unit-scale truck with its real body collider is required for full-footprint checks.");
                Current.bodyCollisionsEnabled = truckBox.enabled && !truckBox.isTrigger && drive.body.detectCollisions;
                if (!Current.bodyCollisionsEnabled) throw new InvalidOperationException("Real truck body collisions must remain enabled during the drive check.");
                Current.bodyColliderLocalSize = truckBox.size;
                halfWidth = truckBox.size.x * .5f + .35f;
                halfLength = truckBox.size.z * .5f + .35f;
                wheelbase = Vector3.Distance(drive.wheels[0].transform.position, drive.wheels[2].transform.position);
                MeasureSettledSupport();
                Current.clearanceHalfWidthMetres = halfWidth; Current.clearanceHalfLengthMetres = halfLength;
                Current.fullVehicleFootprintChecked = true;
                Current.distanceToTown = Current.closestDistanceToTown = Horizontal(Current.spawn, Current.town);
                Current.closestReachableDistanceToTown = Current.distanceToTown;
                path = ReadAuthoredRoad();
                if (path == null) path = Plan(Current.spawn, Current.town);
                if (path == null) { Finish(false, "navigation_limitation", "No route found with 3m grid, 1.5m probes, full measured chassis footprint plus .35m margin and 32-degree terrain slope cap. Probe counts include repeat edge checks; this does not prove a human cannot drive the route."); return; }
                Current.plannedRoute = path;
                waypoint = 1;
                started = Time.time; nextSample = started; lastProgressTime = started;
                previousPosition = progressPosition = drive.body.position;
                Current.closestDistanceToTown = Horizontal(previousPosition, Current.town);
                bestWaypointDistance = float.PositiveInfinity;
                SessionState.SetString(State, "driving");
                Save();
                return;
            }
            if (state != "driving") return;
            Vector3 position = drive.body.position;
            Current.seconds = Time.time - started;
            Current.travelledMetres += Vector3.Distance(previousPosition, position);
            previousPosition = position;
            Current.distanceToTown = Horizontal(position, Current.town);
            Current.closestDistanceToTown = Mathf.Min(Current.closestDistanceToTown, Current.distanceToTown);
            Current.maximumSpeedKph = Mathf.Max(Current.maximumSpeedKph, drive.SpeedKph);
            if (Current.distanceToTown <= 45f && Current.travelledMetres > 100f)
            { Finish(true, "passed", "Reached within 45m of town using the actual truck, real steering/throttle/brakes and authored spawn; no teleport or velocity changes."); return; }
            if (Current.seconds > 240f || EditorApplication.timeSinceStartup - wallStarted > 600d)
            { Finish(false, "navigation_limitation", "Bounded autopilot timeout (240 simulation / 600 wall seconds); inspect trajectory. Not evidence of a gameplay bug by itself."); return; }
            if (Vector3.Dot(drive.transform.up, Vector3.up) < .25f)
            { Finish(false, "navigation_limitation", "Truck overturned under the test driver's input; inspect route/steering before attributing to vehicle gameplay."); return; }
            while (waypoint < path.Count - 1 && Horizontal(position, path[waypoint]) < 9f) { waypoint++; bestWaypointDistance = float.PositiveInfinity; }
            float distance = Horizontal(position, path[waypoint]);
            if (distance < bestWaypointDistance - .6f || Horizontal(position, progressPosition) > 5f)
            { bestWaypointDistance = Mathf.Min(bestWaypointDistance, distance); progressPosition = position; lastProgressTime = Time.time; }
            if (Time.time - lastProgressTime > 25f)
            { Finish(false, "navigation_limitation", "Autopilot made no progress for 25 simulation seconds. Possible obstacle, steering trap, slope or clearance mismatch; inspect trajectory and vehicle visually."); return; }
            // Pure pursuit, with bounded speed for tight curves. All motion comes from FireTruckDrive.
            Vector3 local = drive.transform.InverseTransformPoint(path[waypoint]);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float steeringAngle = Mathf.Atan2(2f * wheelbase * Mathf.Sin(angle * Mathf.Deg2Rad), Mathf.Max(6f, distance)) * Mathf.Rad2Deg;
            if (local.z < 0f) steeringAngle = Mathf.Sign(angle) * drive.maxSteerAngle;
            float steering = Mathf.Clamp(steeringAngle / drive.maxSteerAngle, -1f, 1f);
            float targetSpeed = Mathf.Lerp(13f, 6f, Mathf.InverseLerp(12f, 65f, Mathf.Abs(angle)));
            bool brake = drive.SpeedKph > targetSpeed + 1.5f;
            // A real keyboard W supplies 1.0; partial throttle would artificially reduce hill climbing ability.
            float throttle = brake ? 0f : drive.SpeedKph < targetSpeed - 1f ? 1f : .1f;
            drive.SetInput(throttle, steering, brake);
            // The disabled coordinator normally owns this camera; retain its actual driving view.
            FollowCamera?.Invoke(player, null);
            if (Time.time >= nextSample)
            {
                int grounded = 0;
                var groundNames = new string[4]; var groundNormals = new Vector3[4]; var slip = new float[4];
                for (int i = 0; i < drive.wheels.Length; i++)
                {
                    var wheel = drive.wheels[i];
                    if (!wheel || !wheel.GetGroundHit(out WheelHit hit)) { groundNames[i] = "airborne"; continue; }
                    grounded++; groundNames[i] = hit.collider ? hit.collider.name : "none";
                    groundNormals[i] = hit.normal; slip[i] = hit.forwardSlip;
                }
                Current.maximumGrounded = Mathf.Max(Current.maximumGrounded, grounded);
                Current.minimumGrounded = Mathf.Min(Current.minimumGrounded, grounded);
                Current.trajectory.Add(new Sample
                {
                    seconds = Current.seconds, position = position, speedKph = drive.SpeedKph, grounded = grounded,
                    signedForwardSpeedMetresPerSecond = Vector3.Dot(drive.body.linearVelocity, drive.transform.forward),
                    requestedThrottle = throttle, requestedSteering = steering, requestedBrake = brake,
                    frontPoint = drive.transform.TransformPoint(Vector3.forward * (Current.bodyColliderLocalSize.z * .5f)),
                    targetPoint = path[waypoint], rearMotorTorque = new[] { drive.wheels[2].motorTorque, drive.wheels[3].motorTorque },
                    wheelGroundColliders = groundNames, wheelGroundNormals = groundNormals, forwardSlip = slip
                });
                nextSample = Time.time + 1f;
                Save();
            }
        }
        catch (Exception error) { Finish(false, "error", error.ToString()); }
    }
    static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    static bool Ground(Vector3 point, out float height)
    {
        foreach (var terrain in terrains)
        {
            if (!terrain || !terrain.terrainData) continue;
            Vector3 p = point - terrain.transform.position, size = terrain.terrainData.size;
            if (p.x >= 0f && p.z >= 0f && p.x <= size.x && p.z <= size.z)
            { height = terrain.SampleHeight(point) + terrain.transform.position.y; return true; }
        }
        height = 0f; return false;
    }
    static bool Clear(Vector3 point)
    {
        if (!Ground(point, out float height)) { Current.rejectedOutsideTerrainProbes++; return Reject("outside terrain", point); }
        point.y = height;
        foreach (var water in waters)
            if (water.ContainsIntake(new Vector3(point.x, water.SurfaceY - .12f, point.z))) { Current.rejectedWaterProbes++; return Reject("water", point, water.name); }
        foreach (Vector3 offset in new[] { Vector3.right * ProbeSpacing, Vector3.left * ProbeSpacing, Vector3.forward * ProbeSpacing, Vector3.back * ProbeSpacing })
        {
            if (!Ground(point + offset, out float h)) { Current.rejectedOutsideTerrainProbes++; return Reject("outside terrain", point + offset); }
            if (Mathf.Abs(h - height) / ProbeSpacing > Mathf.Tan(SlopeLimit * Mathf.Deg2Rad)) { Current.rejectedSlopeProbes++; return Reject("terrain slope", new Vector3(point.x + offset.x, h, point.z + offset.z)); }
        }
        // Heading is not known at a grid node. The full oriented chassis is checked on each edge.
        return true;
    }
    static bool SolidsClear(Vector3 centre, Vector3 extent, Quaternion rotation)
    {
        int count = Physics.OverlapBoxNonAlloc(centre, extent, overlaps, rotation, ~0, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) { Current.overlapBufferOverflows++; return Reject("overlap buffer full", centre); }
        for (int i = 0; i < count; i++)
        {
            var c = overlaps[i];
            if (c is TerrainCollider || c is CharacterController || c.transform.IsChildOf(drive.transform) || c.transform.IsChildOf(player.transform)) continue;
            Current.rejectedSolidProbes++;
            string blocker = c.name + " (" + c.GetType().Name + ")";
            SolidBlocker entry = Current.solidBlockers.Find(item => item.collider == blocker);
            if (entry == null) { entry = new SolidBlocker { collider = blocker }; Current.solidBlockers.Add(entry); }
            entry.rejectedProbes++;
            return Reject("solid collider", centre, blocker);
        }
        return true;
    }
    static bool FootprintClear(Vector3 point, Vector3 forward)
    {
        forward.y = 0f; forward.Normalize();
        if (forward.sqrMagnitude < .9f) forward = drive.transform.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        // Use the actual asymmetric axle positions and track widths, not a chassis-width support plane.
        for (int i = 0; i < wheelOffsets.Length; i++)
        {
            Vector3 wheel = wheelOffsets[i];
            Vector3 contact = point + right * wheel.x + forward * wheel.z;
            if (!Ground(contact, out float height)) { Current.rejectedOutsideTerrainProbes++; return Reject("outside terrain", contact); }
            supportSamples[i] = new Vector3(wheel.x, height, wheel.z);
        }
        Vector3 support = FitSupportPlane(supportSamples, supportSamples.Length);
        float baseY = support.z, lateralGrade = support.x, longitudinalGrade = support.y;
        Vector3 slopeForward = (forward + Vector3.up * longitudinalGrade).normalized;
        Vector3 slopeRight = (right + Vector3.up * lateralGrade).normalized;
        Quaternion pose = Quaternion.LookRotation(slopeForward, Vector3.Cross(slopeForward, slopeRight).normalized);
        Vector3 up = pose * Vector3.up;
        // SettledRootOffset is measured normal to the wheel-contact plane. Preserve it at the candidate pose.
        Vector3 root = new Vector3(point.x, baseY + settledRootOffset / Mathf.Max(.25f, up.y), point.z);
        float localBottom = truckBox.center.y - truckBox.size.y * .5f;
        Rejection terrainRejection = null;
        for (int longitudinal = -1; longitudinal <= 1; longitudinal++) for (int lateral = -1; lateral <= 1; lateral++)
        {
            float along = longitudinal * halfLength, across = lateral * halfWidth;
            Vector3 sample = root + pose * new Vector3(truckBox.center.x + across, localBottom, truckBox.center.z + along);
            if (!Ground(sample, out float height)) { Current.rejectedOutsideTerrainProbes++; return Reject("outside terrain", sample); }
            foreach (var water in waters)
                if (water.ContainsIntake(new Vector3(sample.x, water.SurfaceY - .12f, sample.z))) { Current.rejectedWaterProbes++; return Reject("water under chassis", sample, water.name); }
            if (height > sample.y - .12f)
            {
                Current.rejectedChassisTerrainProbes++;
                Reject("chassis terrain clearance", sample, null, height, sample.y);
                if (terrainRejection == null) terrainRejection = lastRejection;
            }
        }
        // Keep checking all corners and real solid colliders even when the approximate support plane warns.
        if (!SolidsClear(root + pose * truckBox.center,
            new Vector3(halfWidth, truckBox.size.y * .5f + .1f, halfLength), pose)) return false;
        if (terrainRejection != null) { lastRejection = terrainRejection; return false; }
        return true;
    }
    static void MeasureSettledSupport()
    {
        int count = 0;
        for (int i = 0; i < drive.wheels.Length; i++)
        {
            WheelCollider wheel = drive.wheels[i];
            wheelOffsets[i] = drive.transform.InverseTransformPoint(wheel.transform.TransformPoint(wheel.center));
            if (wheel.GetGroundHit(out WheelHit hit)) supportSamples[count++] = drive.transform.InverseTransformPoint(hit.point);
        }
        if (count < 3) throw new InvalidOperationException("At least three settled wheel contacts are required to measure actual chassis clearance.");
        Vector3 plane = FitSupportPlane(supportSamples, count);
        settledRootOffset = -plane.z / Mathf.Sqrt(1f + plane.x * plane.x + plane.y * plane.y);
        Current.settledGroundedWheels = count;
        Current.settledRootAboveSupportPlaneMetres = settledRootOffset;
        Current.settledUnderbodyClearanceMetres = settledRootOffset + truckBox.center.y - truckBox.size.y * .5f;
        Current.settledLocalWheelContacts = new Vector3[count];
        Array.Copy(supportSamples, Current.settledLocalWheelContacts, count);
    }
    // Least-squares plane y = xSlope*x + zSlope*z + height. Four real wheel positions are not symmetric.
    static Vector3 FitSupportPlane(Vector3[] samples, int count)
    {
        Matrix4x4 terms = Matrix4x4.zero;
        Vector4 values = Vector4.zero;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = samples[i];
            terms.m00 += p.x * p.x; terms.m01 += p.x * p.z; terms.m02 += p.x;
            terms.m10 += p.x * p.z; terms.m11 += p.z * p.z; terms.m12 += p.z;
            terms.m20 += p.x; terms.m21 += p.z; terms.m22 += 1f;
            values.x += p.x * p.y; values.y += p.z * p.y; values.z += p.y;
        }
        terms.m33 = 1f;
        if (Mathf.Abs(terms.determinant) < .0001f) throw new InvalidOperationException("Wheel support contacts do not define a stable plane.");
        Vector4 plane = terms.inverse * values;
        return new Vector3(plane.x, plane.y, plane.z);
    }
    static bool Reject(string type, Vector3 sample, string collider = null, float terrainHeight = 0f, float chassisBottomHeight = 0f)
    {
        lastRejection = new Rejection { type = type, collider = collider, routeProbe = routeProbe,
            samplePosition = sample, terrainHeight = terrainHeight, chassisBottomHeight = chassisBottomHeight };
        return false;
    }
    [Serializable] sealed class AuthoredRoadReport { public string status; public List<Vector3> roadCentre; }
    static List<Vector3> ReadAuthoredRoad()
    {
        const string roadFile = "Logs/VolcanoVehiclePolishSetup.json";
        if (!File.Exists(roadFile)) return null;
        var authored = JsonUtility.FromJson<AuthoredRoadReport>(File.ReadAllText(roadFile));
        if (authored == null || authored.status != "complete" || authored.roadCentre == null || authored.roadCentre.Count < 2) return null;
        var route = new List<Vector3> { Current.spawn };
        foreach (var p in authored.roadCentre) if (Horizontal(route[route.Count - 1], p) > .8f) route.Add(p);
        int approachEnd = route.FindIndex(p => Horizontal(p, Current.town) <= 45f);
        if (approachEnd < 1) return null;
        Current.authoredRoadChecked = true;
        Current.authoredApproachClear = Current.authoredRemainderClear = true;
        Current.authoredApproachEndpoint = route[approachEnd];
        Current.authoredApproachEndpointDistanceToTown = Horizontal(route[approachEnd], Current.town);
        bool approachHasHardRejection = false;
        for (int i = 1; i < route.Count; i++)
        {
            bool clear = SegmentClear(route[i - 1], route[i]);
            if (!clear)
            {
                // JsonUtility restores a null inline serializable class as an empty object after Play Mode reload.
                if (Current.firstAuthoredRejection == null || string.IsNullOrEmpty(Current.firstAuthoredRejection.type))
                    Current.firstAuthoredRejection = lastRejection;
                Current.authoredRoadRejections.Add(lastRejection);
            }
            if (i <= approachEnd)
            {
                Current.authoredApproachSegments++;
                Current.authoredApproachClear &= clear;
                if (!clear && lastRejection.type != "chassis terrain clearance") approachHasHardRejection = true;
            }
            else
            {
                Current.authoredRemainderSegments++;
                Current.authoredRemainderClear &= clear;
                if (!clear && (Current.firstRemainderRejection == null || string.IsNullOrEmpty(Current.firstRemainderRejection.type)))
                    Current.firstRemainderRejection = lastRejection;
            }
        }
        if (approachHasHardRejection) return null;
        Current.authoredApproachDrivenWithTerrainWarnings = !Current.authoredApproachClear;
        Current.routeSource = Current.authoredApproachClear
            ? "authored access track to town firing approach; full footprint checked; remaining road checked separately"
            : "authored access track with static chassis-terrain warnings; actual suspension and enabled body collisions decide dynamic drivability";
        return route.GetRange(0, approachEnd + 1);
    }
    static bool Walk(int id)
    {
        if (!checkedCells[id])
        {
            checkedCells[id] = true;
            Vector3 point = new Vector3(minX + id % nx * Cell, 0f, minZ + id / nx * Cell);
            walkable[id] = Clear(point);
            Ground(point, out point.y);
            cells[id] = point;
        }
        return walkable[id];
    }
    static List<Vector3> Plan(Vector3 spawn, Vector3 town)
    {
        Current.routeSource = "bounded terrain A*; full-footprint edge clearance checked";
        minX = Mathf.Min(spawn.x, town.x) - 80f; minZ = Mathf.Min(spawn.z, town.z) - 80f;
        nx = Mathf.CeilToInt((Mathf.Abs(spawn.x - town.x) + 160f) / Cell) + 1;
        nz = Mathf.CeilToInt((Mathf.Abs(spawn.z - town.z) + 160f) / Cell) + 1;
        int total = nx * nz;
        checkedCells = new bool[total]; walkable = new bool[total]; cells = new Vector3[total];
        var costs = new float[total]; var previous = new int[total]; var closed = new bool[total]; var open = new List<int>();
        for (int i = 0; i < total; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; }
        int start = -1; float nearest = float.PositiveInfinity;
        for (int i = 0; i < total; i++)
        {
            Vector3 p = new Vector3(minX + i % nx * Cell, spawn.y, minZ + i / nx * Cell);
            float distance = Horizontal(p, spawn);
            if (distance < nearest && distance <= 16f && Walk(i) && SegmentClear(spawn, cells[i])) { start = i; nearest = distance; }
        }
        if (start < 0) return null;
        costs[start] = 0; open.Add(start); int goal = -1;
        while (open.Count > 0)
        {
            int best = 0; float score = float.PositiveInfinity;
            for (int i = 0; i < open.Count; i++)
            { float candidate = costs[open[i]] + Mathf.Max(0f, Horizontal(cells[open[i]], town) - 35f); if (candidate < score) { score = candidate; best = i; } }
            int at = open[best]; open.RemoveAt(best); closed[at] = true; Current.nodesExamined++;
            Current.reachableNodes.Add(cells[at]);
            Current.closestReachableDistanceToTown = Mathf.Min(Current.closestReachableDistanceToTown, Horizontal(cells[at], town));
            if (Horizontal(cells[at], town) <= 35f) { goal = at; break; }
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                int x = at % nx + dx, z = at / nx + dz;
                if (x < 0 || z < 0 || x >= nx || z >= nz) continue;
                int next = z * nx + x;
                if (closed[next] || !Walk(next) || !SegmentClear(cells[at], cells[next])) continue;
                float cost = costs[at] + Vector3.Distance(cells[at], cells[next]);
                if (cost >= costs[next]) continue;
                costs[next] = cost; previous[next] = at; if (!open.Contains(next)) open.Add(next);
            }
        }
        if (goal < 0) return null;
        var route = new List<Vector3>();
        for (int id = goal; id >= 0; id = previous[id]) route.Add(cells[id]);
        route.Add(spawn); route.Reverse();
        // Short, clear lookahead segments retain bends without asking the truck to turn at every grid corner.
        var smooth = new List<Vector3> { spawn };
        for (int i = 0; i < route.Count - 1;)
        {
            int next = i + 1;
            for (int j = i + 2; j < route.Count && Horizontal(route[i], route[j]) <= 18f; j++)
                if (SegmentClear(route[i], route[j])) next = j;
            smooth.Add(route[next]); i = next;
        }
        return smooth;
    }
    static bool SegmentClear(Vector3 a, Vector3 b)
    {
        int count = Mathf.Max(1, Mathf.CeilToInt(Horizontal(a, b) / ProbeSpacing));
        Vector3 direction = b - a;
        Rejection terrainRejection = null;
        for (int i = 0; i <= count; i++)
        {
            Vector3 point = Vector3.Lerp(a, b, i / (float)count);
            routeProbe = point;
            if (!Clear(point)) return false;
            if (!FootprintClear(point, direction))
            {
                if (lastRejection.type != "chassis terrain clearance") return false;
                if (terrainRejection == null) terrainRejection = lastRejection;
            }
        }
        if (terrainRejection != null) { lastRejection = terrainRejection; return false; }
        return true;
    }
    static void Finish(bool passed, string status, string detail)
    {
        if (drive) { drive.SetInput(0f, 0f, true); drive.SetDriven(false); }
        Current.passed = passed && Current.runtimeErrors.Count == 0;
        Current.status = passed && !Current.passed ? "runtime_error" : status;
        Current.detail = detail; Current.finishedUtc = DateTime.UtcNow.ToString("O");
        Current.solidBlockers.Sort((a, b) => b.rejectedProbes.CompareTo(a.rejectedProbes));
        Application.runInBackground = SessionState.GetBool(Background, false);
        SessionState.SetString(State, "finished"); Save();
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
    static void ModeChanged(PlayModeStateChange mode)
    {
        if (!SessionState.GetBool(Active, false)) return;
        if (mode == PlayModeStateChange.ExitingPlayMode && SessionState.GetString(State, "") != "finished")
            Finish(false, "interrupted", "Play Mode ended before the route check finished.");
        if (mode != PlayModeStateChange.EnteredEditMode) return;
        Application.runInBackground = SessionState.GetBool(Background, false);
        EditorSceneManager.OpenScene(MenuPath);
        Current.editorScene = SceneManager.GetActiveScene().path;
        Save(); SessionState.SetBool(Active, false);
        Debug.Log("Truck route check " + Current.status + "; evidence: " + ReportPath);
    }
    static void BeforeReload()
    {
        if (SessionState.GetBool(Active, false) && EditorApplication.isPlaying)
            Finish(false, "interrupted", "Script reload interrupted the route test.");
    }
    static void Log(string message, string trace, LogType type)
    {
        if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        if (Current.runtimeErrors.Count < 20) Current.runtimeErrors.Add(message + "\n" + trace);
    }
    static void Save()
    {
        string json = JsonUtility.ToJson(Current, true);
        SessionState.SetString(Saved, json); Directory.CreateDirectory("Logs"); File.WriteAllText(ReportPath, json);
    }
}
