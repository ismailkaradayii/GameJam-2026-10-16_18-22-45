using System;
using System.Collections.Generic;
using System.IO;
using GameJam.FireRescue;
using Itfaiye;
using Itfaiye.Editor;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Measures actual wrist motion and grip contact on the saved game's player.</summary>
[InitializeOnLoad]
public static class ComfortableHandsChecks
{
    const string Pending = "ComfortableHandsChecks.Pending";
    const string Folder = "Logs/ComfortableHands";
    static readonly List<string> lines = new List<string>();
    static FireRescuePlayer player;
    static ItfaiyeWeaponReviewDriver driver;
    static ItfaiyeFirstPersonHands hands;
    static int phase = -1, frame, sampleCount, groundedSamples, ammo, shotCount;
    static float started, maxGripError, initialWalkSpeed, initialRunSpeed, walkDepth, walkHeight, pistolWalkWidth, pistolWalkHeight;
    static float previousLeftDepth, previousDepthDelta;
    static double deadline;
    static double leftDepthSum, rightDepthSum, leftDepthSquared, rightDepthSquared, pairedDepthSum;
    static double legSum, legSquared, armLegSum;
    static float maxFingerBend, maxWristBend, minPalmInward;
    static bool background, hasPreviousDepth, capturedRunForward, capturedRunBack, capturedWalkForward, capturedWalkBack;
    static int previousFrameRate, previousVSync;
    static string error;
    static Vector3 minLeft, maxLeft, minRight, maxRight, minGun, maxGun;

    static ComfortableHandsChecks()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += Start;
            }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted");
        };
    }

    [MenuItem("GameJam/Final Loop/Check comfortable hands")]
    public static void Begin()
    {
        if (phase >= 0 || EditorApplication.isCompiling || (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying))
            throw new InvalidOperationException("Wait for the current hands check or editor transition to finish.");
        if (!Object.FindFirstObjectByType<FireRescuePlayer>())
            throw new InvalidOperationException("Open the authored Volcano_Demo scene first.");
        Directory.CreateDirectory(Folder);
        if (EditorApplication.isPlaying)
        {
            SessionState.SetBool(Pending, false);
            EditorApplication.isPaused = false;
            Start();
            return;
        }
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Start()
    {
        lines.Clear(); error = null; frame = -1; maxGripError = 0f;
        capturedRunForward = capturedRunBack = capturedWalkForward = capturedWalkBack = false;
        walkDepth = walkHeight = pistolWalkWidth = pistolWalkHeight = 0f;
        background = Application.runInBackground; Application.runInBackground = true;
        previousFrameRate = Application.targetFrameRate; previousVSync = QualitySettings.vSyncCount;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
        Application.logMessageReceived += Log;
        try
        {
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            player.mission.enabled = false;
            player.mission.story.enabled = false;
            player.mission.cinematic.enabled = false;
            player.mission.sequence.enabled = false;
            player.gameFlow.enabled = false;
            player.SetCinematicLocked(false); player.SetDriving(false); player.enabled = false;
            player.firstPerson.recoverFromFall = false;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Temporary comfortable hands test floor";
            floor.transform.position = new Vector3(3000, 298, 3000);
            floor.transform.localScale = new Vector3(1000, 4, 1000);
            Physics.SyncTransforms();
            player.PlaceOnFoot(new Vector3(3000, 300.2f, 3000), 0f);
            player.AcquirePistol(); player.pistol.SetAvailable(true); player.pistol.Holster();
            hands = player.pistol.Hands;
            initialWalkSpeed = player.firstPerson.walkSpeed; initialRunSpeed = player.firstPerson.runSpeed;
            driver = player.gameObject.AddComponent<ItfaiyeWeaponReviewDriver>();
            driver.player = player.firstPerson; driver.pistol = player.pistol;
            lines.Add("Saved player on an isolated temporary platform; scene and prefab are never saved.");
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Tick()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (error != null) throw new Exception(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase);
            float age = Time.time - started;
            if (age > .7f) Sample();
            switch (phase)
            {
                case 0:
                    if (age < 1.1f) return;
                    Check("Unarmed idle settles", !player.pistol.Equipped && hands.UnarmedSprintWeight < .001f);
                    driver.move = Vector2.up; Next(1); break;
                case 1:
                    if (age < 2.4f) return;
                    lines.Add("walk speed=" + player.firstPerson.CurrentSpeed.ToString("F3") + " target=" + initialWalkSpeed
                        + " samples=" + sampleCount + " grounded=" + groundedSamples + " dt=" + Time.deltaTime.ToString("F4"));
                    Check("Unarmed walking uses the existing movement speed", Mathf.Abs(player.firstPerson.CurrentSpeed - initialWalkSpeed) < .15f);
                    CheckUnarmed("walk", false); Capture("unarmed-walk");
                    driver.sprint = true; Next(2); break;
                case 2:
                    if (age < 2.4f) return;
                    Check("Unarmed running uses the existing movement speed", player.firstPerson.IsSprinting && Mathf.Abs(player.firstPerson.CurrentSpeed - initialRunSpeed) < .15f);
                    CheckUnarmed("run", true); Capture("unarmed-run");
                    Check("Running captures both opposed arm-swing extrema", capturedRunForward && capturedRunBack);
                    driver.move = Vector2.zero; driver.sprint = false; Next(3); break;
                case 3:
                    if (age < 1.2f) return;
                    Check("Unarmed hands settle when movement stops", hands.UnarmedSprintWeight < .005f && (maxLeft - minLeft).magnitude < .012f);
                    player.pistol.Equip(); Next(4); break;
                case 4:
                    if (age < 1.1f) return;
                    Check("Pistol returns to its authored grip", player.pistol.DrawAmount > .99f && maxGripError < .008f);
                    driver.move = Vector2.up; Next(5); break;
                case 5:
                    if (age < 2.4f) return;
                    CheckGun("walk"); Capture("pistol-walk");
                    driver.sprint = true; Next(6); break;
                case 6:
                    if (age < 2.4f) return;
                    CheckGun("run"); Capture("pistol-run");
                    Check("Running uses a shallow lowered pose", hands.MovementLowerAngle > 6f && hands.MovementLowerAngle <= 10f);
                    ammo = player.pistol.Ammo; shotCount = player.pistol.ShotsFired;
                    Check("Sprint fire request is accepted", player.pistol.Fire()); Next(7); break;
                case 7:
                    if (player.pistol.ShotsFired == shotCount) return;
                    Sample();
                    Check("Weapon raises before firing while running", age < .5f && hands.IsMovementPoseRaised && player.pistol.Ammo == ammo - 1);
                    Check("Both hands remain seated through movement and firing", maxGripError < .008f);
                    driver.sprint = false; driver.aim = true; Next(8); break;
                case 8:
                    if (age < 1.8f) return;
                    Check("Aiming remains steady while walking", player.pistol.AimAmount > .99f && (maxGun - minGun).magnitude < .006f && hands.MovementLowerAngle < .02f);
                    Check("Gameplay speeds have not changed", player.firstPerson.walkSpeed == initialWalkSpeed && player.firstPerson.runSpeed == initialRunSpeed);
                    driver.aim = false; driver.move = Vector2.zero;
                    Check("Reload remains available", player.pistol.Reload()); Next(9); break;
                case 9:
                    if (age < player.pistol.reloadDuration + .3f) return;
                    Check("Reload completes normally", !player.pistol.IsReloading && player.pistol.Ammo == player.pistol.magazineSize);
                    Check("No runtime errors", error == null);
                    Finish(null); break;
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Sample()
    {
        var view = player.viewCamera.transform;
        var animator = player.firstPerson.handsAnimator;
        Vector3 left = view.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
        Vector3 right = view.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.RightHand).position);
        minLeft = Vector3.Min(minLeft, left); maxLeft = Vector3.Max(maxLeft, left);
        minRight = Vector3.Min(minRight, right); maxRight = Vector3.Max(maxRight, right);
        minGun = Vector3.Min(minGun, hands.CurrentLocomotionOffset); maxGun = Vector3.Max(maxGun, hands.CurrentLocomotionOffset);
        leftDepthSum += left.z; rightDepthSum += right.z;
        leftDepthSquared += left.z * left.z; rightDepthSquared += right.z * right.z;
        pairedDepthSum += left.z * right.z;
        if (phase == 1 || phase == 2)
        {
            CaptureSwingExtrema(left, right);
            var thigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            var knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            float leg = Vector3.Dot((knee.position - thigh.position).normalized, player.transform.forward);
            legSum += leg; legSquared += leg * leg; armLegSum += left.z * leg;
            SampleRelaxedHand(animator, true); SampleRelaxedHand(animator, false);
        }
        sampleCount++;
        if (player.firstPerson.IsGrounded) groundedSamples++;
        if (player.pistol.DrawAmount < .999f || player.pistol.IsReloading) return;
        var weapon = player.pistol.weapon.transform;
        Quaternion localRotation = Quaternion.Inverse(view.rotation) * weapon.rotation;
        Vector3 localPosition = view.InverseTransformPoint(weapon.position);
        maxGripError = Mathf.Max(maxGripError, Vector3.Distance(left, localPosition + localRotation * ItfaiyeFirstPersonHands.LeftGrip));
        maxGripError = Mathf.Max(maxGripError, Vector3.Distance(right, localPosition + localRotation * ItfaiyeFirstPersonHands.RightGrip));
    }

    static void SampleRelaxedHand(Animator animator, bool left)
    {
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var index = animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        var little = animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
        Vector3 a = index.position - hand.position, b = little.position - hand.position;
        maxWristBend = Mathf.Max(maxWristBend, Vector3.Angle(a + b, hand.position - forearm.position));
        Vector3 palm = (left ? Vector3.Cross(a, b) : Vector3.Cross(b, a)).normalized;
        minPalmInward = Mathf.Min(minPalmInward, Vector3.Dot(palm, player.viewCamera.transform.right * (left ? 1f : -1f)));
        int start = (int)(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
        for (int i = 0; i < 4; i++)
        {
            var proximal = animator.GetBoneTransform((HumanBodyBones)(start + i * 3));
            var middle = animator.GetBoneTransform((HumanBodyBones)(start + i * 3 + 1));
            var distal = animator.GetBoneTransform((HumanBodyBones)(start + i * 3 + 2));
            maxFingerBend = Mathf.Max(maxFingerBend, Vector3.Angle(middle.position - proximal.position, distal.position - middle.position));
        }
    }

    static void CaptureSwingExtrema(Vector3 left, Vector3 right)
    {
        float delta = hasPreviousDepth ? left.z - previousLeftDepth : 0f;
        bool running = phase == 2;
        string gait = running ? "run" : "walk";
        // Capture when a measured wrist actually reverses at the end of its arc.
        // This avoids two arbitrary frames that could show nearly the same pose.
        if (hasPreviousDepth && previousDepthDelta > 0f && delta <= 0f &&
            left.z > hands.leftRestPosition.z + .015f && right.z < hands.rightRestPosition.z - .015f && !(running ? capturedRunForward : capturedWalkForward))
        {
            if (running) capturedRunForward = true; else capturedWalkForward = true;
            lines.Add(gait + " left-forward/right-back extremum: left=" + left.ToString("F4") + " right=" + right.ToString("F4"));
            Capture("unarmed-" + gait + "-forward"); Save();
        }
        if (hasPreviousDepth && previousDepthDelta < 0f && delta >= 0f &&
            left.z < hands.leftRestPosition.z - .015f && right.z > hands.rightRestPosition.z + .015f && !(running ? capturedRunBack : capturedWalkBack))
        {
            if (running) capturedRunBack = true; else capturedWalkBack = true;
            lines.Add(gait + " left-back/right-forward extremum: left=" + left.ToString("F4") + " right=" + right.ToString("F4"));
            Capture("unarmed-" + gait + "-back"); Save();
        }
        previousDepthDelta = delta; previousLeftDepth = left.z; hasPreviousDepth = true;
    }

    static void CheckUnarmed(string gait, bool running)
    {
        Vector3 left = maxLeft - minLeft, right = maxRight - minRight;
        lines.Add(gait + " actual wrist excursion left=" + left.ToString("F4") + " right=" + right.ToString("F4"));
        Check("Unarmed " + gait + " stays grounded throughout the sample", groundedSamples == sampleCount);
        float minHeight = running ? .15f : .10f, maxHeight = running ? .45f : .30f;
        float minDepth = running ? .10f : .06f, maxDepth = running ? .30f : .20f;
        Check("Unarmed " + gait + " has visible vertical arm movement", sampleCount > 10 && left.y > minHeight && right.y > minHeight && left.y < maxHeight && right.y < maxHeight);
        Check("Unarmed " + gait + " visibly swings forward and back", left.z > minDepth && right.z > minDepth && left.z < maxDepth && right.z < maxDepth);
        double covariance = pairedDepthSum - leftDepthSum * rightDepthSum / sampleCount;
        double leftVariance = leftDepthSquared - leftDepthSum * leftDepthSum / sampleCount;
        double rightVariance = rightDepthSquared - rightDepthSum * rightDepthSum / sampleCount;
        double correlation = covariance / Math.Sqrt(Math.Max(1e-12, leftVariance * rightVariance));
        lines.Add(gait + " left/right fore-aft correlation=" + correlation.ToString("F4"));
        Check("Unarmed " + gait + " swings the arms in opposite directions", correlation < -.8);
        double legVariance = legSquared - legSum * legSum / sampleCount;
        double armLegCorrelation = (armLegSum - leftDepthSum * legSum / sampleCount)
            / Math.Sqrt(Math.Max(1e-12, leftVariance * legVariance));
        lines.Add(gait + " arm/leg correlation=" + armLegCorrelation.ToString("F4") + " max wrist bend=" + maxWristBend.ToString("F2")
            + " max finger bend=" + maxFingerBend.ToString("F2") + " inward palm=" + minPalmInward.ToString("F3"));
        Check("Unarmed " + gait + " counter-swings with the leg on the same side", armLegCorrelation < -.65);
        Check("Unarmed " + gait + " keeps wrists aligned with forearms", maxWristBend < 15f);
        Check("Unarmed " + gait + " keeps the palms inward", minPalmInward > .85f);
        Check("Unarmed " + gait + " keeps fingers loosely curled", maxFingerBend < 45f);
        Check("Unarmed " + gait + " alternates which arm is forward",
            maxLeft.z > hands.leftRestPosition.z + .012f && minLeft.z < hands.leftRestPosition.z - .012f &&
            maxRight.z > hands.rightRestPosition.z + .012f && minRight.z < hands.rightRestPosition.z - .012f);
        Check("Unarmed " + gait + " leaves the centre of the view clear", maxLeft.y < -.12f && maxRight.y < -.12f);
        if (running)
            Check("Running has a visibly stronger arm swing than walking", Mathf.Min(left.z, right.z) > walkDepth * 1.1f && Mathf.Min(left.y, right.y) > walkHeight * 1.25f);
        else
        {
            walkDepth = Mathf.Max(left.z, right.z);
            walkHeight = Mathf.Max(left.y, right.y);
        }
    }

    static void CheckGun(string gait)
    {
        Vector3 excursion = maxGun - minGun;
        bool running = gait == "run";
        lines.Add(gait + " pistol excursion=" + excursion.ToString("F4"));
        Check("Pistol " + gait + " stays grounded throughout the sample", groundedSamples == sampleCount);
        Check("Pistol " + gait + " keeps a controlled visible rhythm", sampleCount > 10 && excursion.x > .002f && excursion.x < (running ? .04f : .025f) && excursion.y < (running ? .03f : .014f));
        if (running)
            Check("Pistol running visibly increases the stride motion", excursion.x > pistolWalkWidth * 1.4f && excursion.y > pistolWalkHeight * 1.4f);
        else
        {
            pistolWalkWidth = excursion.x;
            pistolWalkHeight = excursion.y;
        }
        Check("Pistol " + gait + " keeps roll below one degree", Mathf.Abs(hands.CurrentLocomotionEuler.z) < 1f);
        Check("Pistol " + gait + " preserves palm contact", maxGripError < .008f);
    }

    static void Next(int next)
    {
        phase = next; started = Time.time; deadline = EditorApplication.timeSinceStartup + 45;
        sampleCount = groundedSamples = 0; minLeft = minRight = minGun = Vector3.one * float.PositiveInfinity;
        maxLeft = maxRight = maxGun = Vector3.one * float.NegativeInfinity;
        leftDepthSum = rightDepthSum = leftDepthSquared = rightDepthSquared = pairedDepthSum = 0;
        legSum = legSquared = armLegSum = 0;
        maxFingerBend = maxWristBend = 0f; minPalmInward = 1f;
        previousLeftDepth = previousDepthDelta = 0f; hasPreviousDepth = false;
        Save();
    }
    static void Capture(string name)
    {
        var capture = player.GetComponent<CombatViewCapture>() ?? player.gameObject.AddComponent<CombatViewCapture>();
        capture.Photo(Folder + "/" + name + ".png");
    }
    static void Check(string name, bool pass)
    {
        lines.Add((pass ? "PASS " : "FAIL ") + name); Save();
        if (!pass) throw new Exception(name);
    }
    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message; }
    static void Save() => File.WriteAllLines(Folder + "/checks.txt", lines);
    static void Finish(string failure)
    {
        phase = -1; Application.logMessageReceived -= Log;
        Application.runInBackground = background;
        Application.targetFrameRate = previousFrameRate; QualitySettings.vSyncCount = previousVSync;
        lines.Add(failure == null ? "COMPLETED " + DateTime.UtcNow.ToString("O") : "FAILED " + failure);
        Save();
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
