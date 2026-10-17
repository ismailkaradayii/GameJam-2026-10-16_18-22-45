using System;
using System.Collections.Generic;
using System.IO;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class FireRescueChecks
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
        public string status = "running";
        public string scene;
        public string phase;
        public string startedUtc;
        public string finishedUtc;
        public bool allPassed;
        public float simulationSeconds;
        public Vector3 initialTruckPosition;
        public string error;
        public List<Check> checks = new List<Check>();
    }

    private static FireTruckDrive drive;
    private static FireHoseSystem hose;
    private static FireRescuePlayer player;
    private static WaterSourceVolume water;
    private static Report report;
    private static bool running;
    private static int phase;
    private static double phaseStarted;
    private static double simulationStarted;
    private static double wallStarted;
    private static Vector3 originalPosition;
    private static Quaternion originalRotation;
    private static Vector3 originalPlayerPosition;
    private static Quaternion originalPlayerRotation;
    private static Vector3 accelerationStart;
    private static Vector3 turnStartForward;
    private static bool originalPlayerEnabled;
    private static bool originalDriving;
    private static bool originalHoseEnabled;
    private static float originalTank;
    private static Transform originalSocket;
    private static GameObject testHand;
    private static GameObject testSocket;
    private static bool capturedState;

    [MenuItem("GameJam/Fire Rescue/Check mechanics (practice scene in Play)", true)]
    private static bool CanRun() => EditorApplication.isPlaying && !EditorApplication.isPaused &&
        SceneManager.GetActiveScene().path == "Assets/Scenes/İtfaiye aracı.unity";

    [MenuItem("GameJam/Fire Rescue/Check mechanics (practice scene in Play)")]
    public static void Begin()
    {
        if (running)
            throw new InvalidOperationException("Fire rescue checks are already running.");
        if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            throw new InvalidOperationException("Begin requires unpaused Play Mode.");

        report = new Report
        {
            scene = SceneManager.GetActiveScene().path,
            startedUtc = DateTime.UtcNow.ToString("O"),
            phase = "discover"
        };
        running = true;
        capturedState = false;
        water = null;
        testHand = null;
        testSocket = null;
        simulationStarted = Time.timeAsDouble;
        wallStarted = EditorApplication.timeSinceStartup;
        try
        {
            drive = UnityEngine.Object.FindFirstObjectByType<FireTruckDrive>();
            player = UnityEngine.Object.FindFirstObjectByType<FireRescuePlayer>();
            hose = drive != null ? drive.GetComponentInChildren<FireHoseSystem>() : null;
            if (hose == null)
                hose = UnityEngine.Object.FindFirstObjectByType<FireHoseSystem>();
            if (drive == null || drive.body == null || player == null || player.playerBody == null ||
                hose == null || hose.hoseSocket == null || hose.nozzle == null)
                throw new InvalidOperationException("The test scene is missing truck, player, hose, or required references.");
            if (drive.wheels == null || drive.wheels.Length != 4)
                throw new InvalidOperationException("Expected four wheel colliders in FL, FR, RL, RR order.");
            foreach (WheelCollider wheel in drive.wheels)
                if (wheel == null)
                    throw new InvalidOperationException("A wheel collider reference is missing.");
            if (player.drive != drive || player.hose != hose)
                throw new InvalidOperationException("Player references do not match the discovered truck and hose.");

            originalPosition = drive.body.position;
            originalRotation = drive.body.rotation;
            originalPlayerPosition = player.playerBody.transform.position;
            originalPlayerRotation = player.playerBody.transform.rotation;
            originalPlayerEnabled = player.enabled;
            originalDriving = player.IsDriving;
            originalHoseEnabled = hose.enabled;
            originalTank = hose.TankLitres;
            originalSocket = hose.hoseSocket;
            report.initialTruckPosition = originalPosition;
            capturedState = true;

            Vector3 expectedWater = originalPosition + new Vector3(0f, 0f, -17f);
            float nearest = float.PositiveInfinity;
            foreach (WaterSourceVolume candidate in UnityEngine.Object.FindObjectsByType<WaterSourceVolume>(FindObjectsSortMode.None))
            {
                if (candidate.volume == null || !candidate.isActiveAndEnabled)
                    continue;
                float distance = Vector3.ProjectOnPlane(candidate.volume.bounds.center - expectedWater, Vector3.up).sqrMagnitude;
                if (distance < nearest)
                {
                    water = candidate;
                    nearest = distance;
                }
            }
            if (water == null)
                throw new InvalidOperationException("No active WaterSourceVolume was found in the test scene.");

            player.enabled = false; // Its OnDisable brake is overwritten by each phase's explicit command.
            hose.enabled = false;   // Pump checks call TickPump with fixed deltas, without Update adding time.
            hose.Retract();
            player.SetDriving(true);
            drive.SetInput(0f, 0f, true);
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnAssemblyReload;
            EnterPhase(0, "settle_2s");
        }
        catch (Exception exception)
        {
            Finish(exception.ToString());
        }
    }

    private static void Update()
    {
        if (!running)
            return;
        try
        {
            if (!EditorApplication.isPlaying || drive == null || drive.body == null)
                throw new InvalidOperationException("Play Mode or the test vehicle ended during verification.");
            if (EditorApplication.timeSinceStartup - wallStarted > 120d)
                throw new TimeoutException("Physics checks exceeded 120 wall-clock seconds; inspect pause/focus/time scale.");
            if (EditorApplication.isPaused)
                return;
            double elapsed = Time.timeAsDouble - phaseStarted;
            switch (phase)
            {
                case 0:
                    if (elapsed < 2d) return;
                    int grounded = GroundedWheels();
                    Add("settled_wheel_contact", grounded == 4,
                        "Grounded=" + grounded + "/4; position=" + drive.body.position + "; speedKph=" + drive.SpeedKph);
                    accelerationStart = drive.body.position;
                    drive.SetDriven(true);
                    drive.SetInput(1f, 0f, false);
                    EnterPhase(1, "accelerate_4s");
                    break;
                case 1:
                    if (elapsed < 4d) return;
                    float movement = Vector3.ProjectOnPlane(drive.body.position - accelerationStart, Vector3.up).magnitude;
                    Add("acceleration", movement > 4f && drive.SpeedKph > 5f,
                        "MovementMetres=" + movement + "; speedKph=" + drive.SpeedKph + "; grounded=" + GroundedWheels());
                    drive.SetInput(0f, 0f, true);
                    EnterPhase(2, "brake_3s");
                    break;
                case 2:
                    if (elapsed < 3d) return;
                    Add("service_brake", drive.SpeedKph < 5f, "SpeedKph=" + drive.SpeedKph + "; expected <5");
                    turnStartForward = Vector3.ProjectOnPlane(drive.transform.forward, Vector3.up).normalized;
                    drive.SetInput(1f, 0.75f, false);
                    EnterPhase(3, "steer_3s");
                    break;
                case 3:
                    if (elapsed < 3d) return;
                    Vector3 forward = Vector3.ProjectOnPlane(drive.transform.forward, Vector3.up).normalized;
                    float headingChange = Vector3.SignedAngle(turnStartForward, forward, Vector3.up);
                    float upright = Vector3.Dot(drive.transform.up, Vector3.up);
                    Add("steering", Mathf.Abs(headingChange) > 5f && upright > 0.75f,
                        "HeadingDeltaDegrees=" + headingChange + "; uprightDot=" + upright + "; speedKph=" + drive.SpeedKph);
                    drive.SetInput(0f, 0f, true);
                    EnterPhase(4, "post_turn_brake_3s");
                    break;
                case 4:
                    if (elapsed < 3d) return;
                    Add("post_turn_brake", drive.SpeedKph < 5f, "SpeedKph=" + drive.SpeedKph + "; expected <5");
                    ResetTruck();
                    EnterPhase(5, "reset_and_settle_2s");
                    break;
                case 5:
                    if (elapsed < 2d) return;
                    report.phase = "interaction_and_deterministic_pump_checks";
                    WriteReport();
                    CheckPlayerGates();
                    CheckHose();
                    Finish(null);
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(exception.ToString());
        }
    }

    private static void CheckPlayerGates()
    {
        hose.Retract();
        player.SetDriving(true);
        drive.body.linearVelocity = drive.transform.forward * 8f;
        float movingSpeed = drive.SpeedKph;
        bool movingExit = player.ExitVehicle();
        Add("exit_blocked_above_20_kph", !movingExit && player.IsDriving,
            "SpeedKph=" + movingSpeed + "; ExitVehicle=" + movingExit + "; IsDriving=" + player.IsDriving);

        drive.body.linearVelocity = Vector3.zero;
        drive.body.angularVelocity = Vector3.zero;
        player.SetDriving(true);
        bool stationaryExit = player.ExitVehicle();
        Add("exit_when_stopped", stationaryExit && !player.IsDriving && !drive.IsDriven,
            "ExitVehicle=" + stationaryExit + "; playerDriving=" + player.IsDriving + "; driveDriven=" + drive.IsDriven);
        player.SetDriving(false);

        PlacePlayerNearDoor(15f);
        bool farEntry = player.EnterVehicle();
        Add("entry_requires_nearby_door", !farEntry && !player.IsDriving,
            "Far EnterVehicle=" + farEntry + "; interactionDistance=" + player.interactionDistance);
        player.SetDriving(false);
        PlacePlayerNearDoor(0f);
        hose.Drop(hose.hoseSocket.position + Vector3.back);
        bool deployedEntry = player.EnterVehicle();
        Add("entry_blocked_with_deployed_hose", !deployedEntry && !player.IsDriving,
            "EnterVehicle=" + deployedEntry + "; hoseDeployed=" + hose.IsDeployed);

        player.SetDriving(false);
        hose.Retract();
        PlacePlayerNearDoor(0f);
        bool retractedEntry = player.EnterVehicle();
        Add("entry_after_hose_retracted", retractedEntry && player.IsDriving && drive.IsDriven,
            "EnterVehicle=" + retractedEntry + "; hoseDeployed=" + hose.IsDeployed + "; driveDriven=" + drive.IsDriven);
        drive.SetDriven(false);
    }

    private static void CheckHose()
    {
        Vector3 submerged = water.volume.bounds.center;
        submerged.y = water.SurfaceY - 0.12f;
        bool validWater = water.ContainsIntake(submerged) && hose.CanReach(submerged);
        Add("water_test_fixture", validWater,
            "Source=" + water.name + "; intake=" + submerged + "; surfaceY=" + water.SurfaceY +
            "; socketDistance=" + Vector3.Distance(hose.hoseSocket.position, submerged));
        if (!validWater)
            throw new InvalidOperationException("The fixture's submerged point is not valid water within hose reach.");

        PrepareHose(submerged + Vector3.up * 1.2f, 100f);
        hose.TickPump(3f);
        Add("dry_intake_does_not_fill", Approximately(hose.TankLitres, 100f) && !hose.IsPumping,
            PumpEvidence(100f));

        PrepareHose(submerged, 100f);
        hose.TickPump(2f);
        float expectedFill = Mathf.Min(hose.tankCapacityLitres, 100f + hose.fillRateLitresPerSecond * 2f);
        Add("submerged_intake_fills", Approximately(hose.TankLitres, expectedFill) && hose.TankLitres > 100f,
            PumpEvidence(expectedFill));

        testHand = new GameObject("FireRescueChecks temporary hand");
        testHand.transform.position = submerged;
        hose.SetTankLitres(100f);
        bool pickedUp = hose.TryPickup(testHand.transform);
        EnablePump();
        hose.TickPump(3f);
        Add("carried_intake_does_not_fill", pickedUp && hose.IsCarried &&
            Approximately(hose.TankLitres, 100f) && !hose.IsPumping, PumpEvidence(100f) + "; pickedUp=" + pickedUp);

        PrepareHose(submerged, 100f);
        testSocket = new GameObject("FireRescueChecks temporary far socket");
        testSocket.transform.position = submerged + Vector3.right * (hose.maxHoseLength + 1f);
        hose.hoseSocket = testSocket.transform;
        try
        {
            hose.TickPump(3f);
            float distance = Vector3.Distance(hose.nozzle.position, hose.hoseSocket.position);
            Add("overlength_intake_does_not_fill", water.ContainsIntake(hose.nozzle.position) &&
                distance > hose.maxHoseLength && Approximately(hose.TankLitres, 100f) && !hose.IsPumping,
                PumpEvidence(100f) + "; distance=" + distance + "; maxLength=" + hose.maxHoseLength + "; intakeStillInWater=true");
        }
        finally
        {
            hose.hoseSocket = originalSocket;
        }

        PrepareHose(submerged, Mathf.Max(0f, hose.tankCapacityLitres - 1f));
        hose.TickPump(100f);
        Add("tank_caps_at_2000_and_pump_stops", Approximately(hose.tankCapacityLitres, 2000f) &&
            Approximately(hose.TankLitres, 2000f) && !hose.IsPumping && !hose.PumpEnabled,
            PumpEvidence(2000f) + "; capacity=" + hose.tankCapacityLitres);

        PrepareHose(submerged, 100f);
        hose.TickPump(0.1f);
        float beforeRetract = hose.TankLitres;
        hose.Retract();
        hose.TickPump(3f);
        Add("retract_stops_filling", !hose.IsDeployed && !hose.IsCarried && !hose.PumpEnabled &&
            !hose.IsPumping && Approximately(hose.TankLitres, beforeRetract), PumpEvidence(beforeRetract));
    }

    private static void PrepareHose(Vector3 point, float litres)
    {
        hose.hoseSocket = originalSocket;
        hose.Retract();
        hose.SetTankLitres(litres);
        hose.Drop(point);
        EnablePump();
    }

    private static void EnablePump()
    {
        if (!hose.PumpEnabled)
            hose.TogglePump();
    }

    private static string PumpEvidence(float expected)
    {
        return "Litres=" + hose.TankLitres + "; expected=" + expected + "; PumpEnabled=" + hose.PumpEnabled +
            "; IsPumping=" + hose.IsPumping + "; status=" + hose.Status;
    }

    private static bool Approximately(float actual, float expected) => Mathf.Abs(actual - expected) <= 0.02f;

    private static void PlacePlayerNearDoor(float extraDistance)
    {
        Transform door = player.driverDoor != null ? player.driverDoor : drive.transform;
        player.playerBody.enabled = false;
        player.playerBody.transform.position = door.position - Vector3.up * 0.9f + drive.transform.right * extraDistance;
        Physics.SyncTransforms();
        player.playerBody.enabled = true;
    }

    private static int GroundedWheels()
    {
        int count = 0;
        foreach (WheelCollider wheel in drive.wheels)
            if (wheel != null && wheel.isGrounded) count++;
        return count;
    }

    private static void ResetTruck()
    {
        drive.SetDriven(false);
        drive.SetInput(0f, 0f, true);
        drive.body.position = originalPosition;
        drive.body.rotation = originalRotation;
        drive.body.linearVelocity = Vector3.zero;
        drive.body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        drive.body.WakeUp();
    }

    private static void EnterPhase(int value, string name)
    {
        phase = value;
        phaseStarted = Time.timeAsDouble;
        report.phase = name;
        WriteReport();
    }

    private static void Add(string name, bool passed, string evidence)
    {
        report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
        WriteReport();
    }

    private static void Finish(string error)
    {
        if (!running) return;
        running = false;
        EditorApplication.update -= Update;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= OnAssemblyReload;
        report.error = error;
        try
        {
            if (capturedState && drive != null && drive.body != null)
            {
                ResetTruck();
                if (hose != null)
                {
                    hose.hoseSocket = originalSocket;
                    hose.Retract();
                    hose.SetTankLitres(originalTank);
                    hose.enabled = originalHoseEnabled;
                }
                if (player != null && player.playerBody != null)
                {
                    player.playerBody.enabled = false;
                    player.playerBody.transform.SetPositionAndRotation(originalPlayerPosition, originalPlayerRotation);
                    player.SetDriving(originalDriving);
                    player.enabled = originalPlayerEnabled;
                }
                Physics.SyncTransforms();
            }
            if (testHand != null) UnityEngine.Object.DestroyImmediate(testHand);
            if (testSocket != null) UnityEngine.Object.DestroyImmediate(testSocket);
        }
        catch (Exception cleanupError)
        {
            report.error = (report.error ?? string.Empty) + "\nCleanup: " + cleanupError;
        }
        report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.Count > 0;
        foreach (Check check in report.checks)
            report.allPassed &= check.passed;
        report.status = string.IsNullOrEmpty(report.error) ? "completed" : "error";
        report.phase = "finished";
        report.finishedUtc = DateTime.UtcNow.ToString("O");
        WriteReport();
        Debug.Log("Fire rescue checks " + report.status + "; allPassed=" + report.allPassed +
            ". Evidence: Library/FireRescueChecks.json");
    }

    private static void WriteReport()
    {
        report.simulationSeconds = (float)(Time.timeAsDouble - simulationStarted);
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/FireRescueChecks.json")),
            JsonUtility.ToJson(report, true));
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            Finish("Play Mode ended before the checks completed.");
    }

    private static void OnAssemblyReload() => Finish("Assembly reload interrupted the checks.");
}
