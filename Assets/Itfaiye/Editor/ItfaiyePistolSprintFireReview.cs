using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Itfaiye.Editor
{
    [InitializeOnLoad]
    public static class ItfaiyePistolSprintFireReview
    {
        const string Folder = "Library/ItfaiyePistol/SprintFire";
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static ItfaiyeFirstPersonHands hands;
        static InputSettings originalInput, temporaryInput;
        static GameObject target;
        static Transform floor;
        static Vector3 floorScale, idlePosition, idleWrist;
        static string savedHands;
        static Quaternion[] fingers;
        static float started, captureDelta, minX, maxX, maxGripError, maxFingerAngle;
        static int phase = -1, frame, errors;
        static bool observedQueue;
        static readonly StringBuilder Report = new StringBuilder();
        static ItfaiyePistolSprintFireReview() { EditorApplication.update += Update; }

        [MenuItem("Tools/Itfaiye/Review Sprint Raise and Fire %&F8")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            pistol = player.pistol; hands = pistol.Hands;
            if (pistol.Equipped || pistol.ShotsFired != 0) throw new InvalidOperationException("Use a fresh Play session.");
            Directory.CreateDirectory(Folder); Report.Clear(); errors = 0; frame = -1; observedQueue = false;
            maxGripError = maxFingerAngle = 0f; minX = float.PositiveInfinity; maxX = float.NegativeInfinity;
            savedHands = EditorJsonUtility.ToJson(hands);
            captureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60f;
            originalInput = InputSystem.settings; temporaryInput = UnityEngine.Object.Instantiate(originalInput);
            temporaryInput.hideFlags = HideFlags.HideAndDontSave;
            temporaryInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            temporaryInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = temporaryInput;
            floor = GameObject.Find("Plane").transform; floorScale = floor.localScale;
            floor.localScale = new Vector3(floorScale.x * 20f, floorScale.y, floorScale.z * 20f);
            target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.name = "Temporary sprint shot target";
            target.transform.position = player.playerCamera.transform.position + player.transform.forward * 60f;
            target.transform.localScale = new Vector3(15f, 12f, .2f); Physics.SyncTransforms();
            Application.logMessageReceived += Log;
            player.SetInputCaptured(true); Input(false, false); pistol.Equip(); Next(0);
        }
        static void Next(int value) { phase = value; started = Time.time; }
        static void Update()
        {
            if (phase < 0) return;
            if (!EditorApplication.isPlaying) { Finish("ABORTED"); return; }
            if (EditorApplication.isPaused || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try { Tick(); } catch (Exception ex) { Finish("FAILED: " + ex); }
        }
        static void Tick()
        {
            if (phase != 12) player.SetInputCaptured(true);
            float elapsed = Time.time - started;
            if (elapsed > 5f) throw new TimeoutException("Sprint fire review stalled at phase " + phase);
            if (phase > 0 && pistol.Ready) CheckGrip();
            switch (phase)
            {
                case 0:
                    Input(false, false); if (elapsed < .8f) return;
                    idlePosition = player.playerCamera.transform.InverseTransformPoint(pistol.weapon.transform.position);
                    idleWrist = player.playerCamera.transform.InverseTransformPoint(player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand).position);
                    fingers = FingerPose();
                    Check("Idle preserves the saved pose", hands.SprintLowering < .001f);
                    Capture("idle"); Next(1); return;
                case 1:
                    Input(true, false);
                    if (elapsed > .4f) { minX = Mathf.Min(minX, hands.CurrentLocomotionOffset.x); maxX = Mathf.Max(maxX, hands.CurrentLocomotionOffset.x); }
                    if (elapsed < 1.2f) return;
                    Check("Running reaches its existing speed", player.IsSprinting && Mathf.Abs(player.CurrentSpeed - player.runSpeed) < .1f);
                    Check("Running uses a shallow comfortable tilt", hands.SprintLowering > .98f && hands.MovementLowerAngle > 6f && hands.MovementLowerAngle <= 10f && LocalMuzzle().y > -.21f && LocalMuzzle().y < -.07f);
                    var runWrist = player.playerCamera.transform.InverseTransformPoint(player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand).position);
                    Check("Running keeps the hands near their normal height", runWrist.y > idleWrist.y - .075f && runWrist.y < idleWrist.y + .015f && runWrist.z < idleWrist.z + .015f);
                    Check("Lowered gun keeps a visible controlled running rhythm", maxX - minX > .018f && maxX - minX < .04f);
                    PistolGripWorkbench.CaptureViews(player, Folder + "/lowered"); Next(2); return;
                case 2:
                    Input(true, true);
                    if (pistol.SprintShotQueued && !observedQueue)
                    {
                        observedQueue = true;
                        Check("Click queues a raise without consuming ammo", pistol.ShotsFired == 0 && pistol.Ammo == 12);
                    }
                    if (pistol.ShotsFired == 0) return;
                    Check("A real mouse click raises before firing", observedQueue && elapsed > .04f && elapsed < .6f);
                    Check("Shot occurs only after the lowered pose clears", hands.IsMovementPoseRaised);
                    Check("Raised shot points forward", Vector3.Angle(pistol.LastShotRay.direction, player.playerCamera.transform.forward) < 8f);
                    Check("Shot hits the forward target instead of the floor", pistol.LastShotCollider == target.GetComponent<Collider>());
                    Check("Running continues while firing", player.IsSprinting && player.CurrentSpeed > player.runSpeed - .1f);
                    Check("One raised shot consumes one round", pistol.ShotsFired == 1 && pistol.Ammo == 11);
                    Capture("raised-shot"); Next(3); return;
                case 3:
                    Input(true, true); if (elapsed < 1.1f) return;
                    Check("Holding the click does not create automatic fire", pistol.ShotsFired == 1);
                    Check("After firing the gun returns to the shallow running tilt", hands.SprintLowering > .98f && LocalMuzzle().y > -.21f && LocalMuzzle().y < -.07f);
                    Capture("lowered-again"); Next(4); return;
                case 4:
                    Input(true, false); if (elapsed < .1f) return; Next(5); return;
                case 5:
                    Input(true, elapsed < .04f); if (pistol.ShotsFired < 2) return;
                    Check("A quick released click is retained until the gun rises", pistol.ShotsFired == 2 && pistol.Ammo == 10 && hands.IsMovementPoseRaised);
                    Next(6); return;
                case 6:
                    Input(false, false); if (elapsed < 1.1f) return;
                    Check("Stopping fully removes the downward tilt", hands.SprintLowering < .001f && hands.CurrentLocomotionEuler.magnitude < .02f);
                    Check("Stopping restores the exact resting placement", Vector3.Distance(idlePosition, player.playerCamera.transform.InverseTransformPoint(pistol.weapon.transform.position)) < .004f);
                    Capture("stopped"); Next(7); return;
                case 7:
                    Input(true, false); if (elapsed < 1f) return;
                    Check("A second sprint enters the lowered pose", hands.SprintLowering > .98f);
                    Check("Another sprint shot can be requested", pistol.Fire() && pistol.SprintShotQueued);
                    Check("Reload cancels an uncommitted raised shot", pistol.Reload() && !pistol.SprintShotQueued && pistol.Ammo == 10);
                    Next(8); return;
                case 8:
                    Input(true, false); if (elapsed < pistol.reloadDuration + .2f) return;
                    Check("Reload completes without a delayed unwanted shot", !pistol.IsReloading && pistol.ShotsFired == 2 && pistol.Ammo == 12 && pistol.reserveAmmo == 46);
                    Next(9); return;
                case 9:
                    Input(true, false); if (elapsed < .9f) return;
                    Check("Holster test starts with a pending raised shot", pistol.Fire() && pistol.SprintShotQueued);
                    pistol.Holster();
                    Check("Holstering cancels the queued shot", !pistol.SprintShotQueued); Next(10); return;
                case 10:
                    Input(false, false); if (elapsed < .5f) return;
                    Check("Holstered pistol never fires the canceled request", !pistol.Equipped && pistol.ShotsFired == 2 && pistol.Ammo == 12);
                    pistol.Equip(); Next(11); return;
                case 11:
                    Input(true, false); if (elapsed < 1f) return;
                    Check("Focus test starts with a pending raised shot", pistol.Fire() && pistol.SprintShotQueued);
                    player.SetInputCaptured(false); Input(false, false); Next(12); return;
                case 12:
                    Input(false, false); if (elapsed < .4f) return;
                    Check("Losing game input cancels a pending shot", !pistol.SprintShotQueued && pistol.ShotsFired == 2 && pistol.Ammo == 12);
                    Next(13); return;
                case 13:
                    Input(false, false, true); if (elapsed < 1.2f) return;
                    Check("Walking retains the configured speed", !player.IsSprinting && Mathf.Abs(player.CurrentSpeed - player.walkSpeed) < .1f);
                    Check("Walking adds only a slight downward tilt", hands.MovementLowerAngle > 1f && hands.MovementLowerAngle <= 2f && LocalMuzzle().y > -.06f && LocalMuzzle().y < 0f);
                    Check("Walking crosshair still follows the barrel", Vector3.Angle(pistol.AimRay.direction, pistol.muzzle.forward) < .05f);
                    Capture("walking"); Next(14); return;
                case 14:
                    Input(false, elapsed < .04f, true); if (pistol.ShotsFired < 3) return;
                    Check("Walking click raises the weapon before firing", hands.IsMovementPoseRaised && pistol.ShotsFired == 3 && pistol.Ammo == 11);
                    Check("Walking shot points forward", Vector3.Angle(pistol.LastShotRay.direction, player.playerCamera.transform.forward) < 8f);
                    Capture("walking-shot"); Next(15); return;
                case 15:
                    Input(false, false, true, true); if (elapsed < 1f) return;
                    Check("ADS removes the walking tilt", pistol.AimAmount > .99f && hands.MovementLowerAngle < .01f);
                    Check("ADS camera alignment is preserved", Mathf.Abs(player.playerCamera.fieldOfView - 62f) < .01f && Mathf.Abs(player.handsCamera.fieldOfView - 62f) < .01f);
                    Capture("walking-ads"); Next(16); return;
                case 16:
                    Input(false, false); if (elapsed < 1.1f) return;
                    Check("Stopping after walking restores the saved idle pose", hands.MovementLowerAngle < .01f && Vector3.Distance(idlePosition, player.playerCamera.transform.InverseTransformPoint(pistol.weapon.transform.position)) < .004f);
                    Check("Both wrists stay attached in lowered and raised poses", maxGripError < .004f);
                    Check("Normal finger rotations are unchanged", maxFingerAngle < .1f);
                    Check("Serialized grip and view settings are preserved", savedHands == EditorJsonUtility.ToJson(hands));
                    Check("No runtime errors", errors == 0);
                    Finish(Report.ToString().Contains("FAIL ") ? "FAILED" : "SUCCESS"); return;
            }
        }
        static Vector3 LocalMuzzle() { return player.playerCamera.transform.InverseTransformDirection(pistol.muzzle.forward); }
        static void CheckGrip()
        {
            var weapon = pistol.weapon.transform;
            foreach (bool left in new[] { false, true })
            {
                var wrist = player.handsAnimator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                maxGripError = Mathf.Max(maxGripError, Vector3.Distance(wrist.position, weapon.position + weapon.rotation * (left ? ItfaiyeFirstPersonHands.LeftGrip : ItfaiyeFirstPersonHands.RightGrip)));
            }
            if (pistol.ShotAge < .3f) return;
            var pose = FingerPose();
            for (int i = 0; i < pose.Length; i++) maxFingerAngle = Mathf.Max(maxFingerAngle, Quaternion.Angle(fingers[i], pose[i]));
        }
        static Quaternion[] FingerPose()
        {
            int start = (int)HumanBodyBones.LeftThumbProximal;
            var pose = new Quaternion[(int)HumanBodyBones.RightLittleDistal - start + 1];
            for (int i = 0; i < pose.Length; i++) pose[i] = player.handsAnimator.GetBoneTransform((HumanBodyBones)(start + i)).localRotation;
            return pose;
        }
        static void Input(bool run, bool fire, bool walk = false, bool aim = false)
        {
            InputSystem.QueueStateEvent(Keyboard.current, run ? new KeyboardState(Key.W, Key.LeftShift) : walk ? new KeyboardState(Key.W) : new KeyboardState());
            InputSystem.QueueStateEvent(Mouse.current, new MouseState { buttons = (ushort)((fire ? 1 : 0) | (aim ? 2 : 0)) });
        }
        static void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/" + name + ".png")); }
        static void Check(string name, bool pass) { Report.AppendLine((pass ? "PASS " : "FAIL ") + name); File.WriteAllText(Folder + "/progress.txt", Report.ToString()); }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Report.AppendLine(message); } }
        static void Finish(string result)
        {
            phase = -1; Application.logMessageReceived -= Log; Input(false, false); Time.captureDeltaTime = captureDelta;
            if (originalInput != null) InputSystem.settings = originalInput;
            if (temporaryInput != null) UnityEngine.Object.DestroyImmediate(temporaryInput);
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (floor != null) floor.localScale = floorScale;
            if (player != null) player.SetInputCaptured(false);
            File.WriteAllText(Folder + "/review.txt", result + "\n" + Report);
        }
    }
}
