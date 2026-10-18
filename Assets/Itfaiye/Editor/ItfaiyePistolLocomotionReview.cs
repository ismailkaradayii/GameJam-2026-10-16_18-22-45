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
    public static class ItfaiyePistolLocomotionReview
    {
        const string Folder = "Library/ItfaiyePistol/Locomotion";
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static ItfaiyeFirstPersonHands hands;
        static InputSettings originalInput, temporaryInput;
        static Transform floor;
        static GameObject wall;
        static Vector3 floorScale, idlePosition, minimum, maximum, sum;
        static Quaternion[] fingerRotations;
        static string handSettings;
        static float started, captureDelta, walkWidth, runWidth, maxGripError, maxFingerAngle, maxRayAngle;
        static int phase = -1, frame, errors, samples;
        static readonly StringBuilder Report = new StringBuilder();
        static ItfaiyePistolLocomotionReview() { EditorApplication.update += Update; }

        [MenuItem("Tools/Itfaiye/Review Walking and Sprint Pose %&F10")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            pistol = player.pistol; hands = pistol.Hands;
            if (pistol.Equipped || pistol.ShotsFired != 0) throw new InvalidOperationException("Use a fresh Play session.");
            Directory.CreateDirectory(Folder); Report.Clear(); errors = 0; frame = -1;
            maxGripError = maxFingerAngle = maxRayAngle = 0f;
            handSettings = EditorJsonUtility.ToJson(hands);
            captureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60f;
            originalInput = InputSystem.settings; temporaryInput = UnityEngine.Object.Instantiate(originalInput);
            temporaryInput.hideFlags = HideFlags.HideAndDontSave;
            temporaryInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            temporaryInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = temporaryInput;
            // Extend only the live test platform, then restore it in Finish.
            floor = GameObject.Find("Plane").transform; floorScale = floor.localScale;
            floor.localScale = new Vector3(floorScale.x * 6f, floorScale.y, floorScale.z * 6f); Physics.SyncTransforms();
            Application.logMessageReceived += Log;
            player.SetInputCaptured(true); Input(); Mouse(false); pistol.Equip(); Next(0);
        }
        static void Next(int value)
        {
            phase = value; started = Time.time; samples = 0; sum = Vector3.zero;
            minimum = Vector3.one * float.PositiveInfinity; maximum = Vector3.one * float.NegativeInfinity;
        }
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
            player.SetInputCaptured(true);
            float elapsed = Time.time - started;
            if (elapsed > 6f) throw new TimeoutException("Locomotion review stalled at " + phase);
            if (phase > 0) CheckContact();
            switch (phase)
            {
                case 0:
                    Input(); Mouse(false); if (elapsed < .8f) return;
                    idlePosition = player.playerCamera.transform.InverseTransformPoint(pistol.weapon.transform.position);
                    fingerRotations = Fingers();
                    Check("Idle has no added walking or sprint offset", hands.CurrentLocomotionOffset.magnitude < .0001f);
                    Capture("idle"); Next(1); return;
                case 1:
                    Input(Key.W); Mouse(false); if (elapsed > .5f) Sample(); if (elapsed < 2.4f) return;
                    walkWidth = maximum.x - minimum.x;
                    Check("Walking uses actual grounded movement", player.IsGrounded && Mathf.Abs(player.CurrentSpeed - player.walkSpeed) < .1f);
                    Check("Walking has a small, readable wrist sway", walkWidth > .004f && walkWidth < .025f);
                    Check("Walking vertical movement stays gentle", maximum.y - minimum.y > .001f && maximum.y - minimum.y < .012f);
                    Check("Walking lowers the weapon only slightly", Mathf.Abs((sum / samples).y + hands.walkLowerDistance) < .012f && hands.SprintLowering < .001f);
                    Record("walk"); Capture("walking"); Next(2); return;
                case 2:
                    Input(Key.W, Key.LeftShift); Mouse(false); if (elapsed > .6f) Sample(); if (elapsed < 2.4f) return;
                    runWidth = maximum.x - minimum.x;
                    Check("Running reaches the configured speed", player.IsSprinting && Mathf.Abs(player.CurrentSpeed - player.runSpeed) < .1f);
                    Check("Running visibly increases the arm swing", runWidth > walkWidth * 1.4f && runWidth < .04f);
                    Check("Running uses the configured lowering distance", hands.SprintLowering > .98f && Mathf.Abs((sum / samples).y + hands.sprintLowerDistance) < .018f);
                    Check("Running angles the muzzle gently down", hands.CurrentLocomotionEuler.x > 4f);
                    Record("run"); Capture("running"); PistolGripWorkbench.CaptureViews(player, Folder + "/run-grip"); Next(3); return;
                case 3:
                    Input(); Mouse(false); if (elapsed < 1.1f) return;
                    Check("Stopping removes the sprint lowering", hands.SprintLowering < .001f);
                    Check("Stopping settles all added motion", hands.CurrentLocomotionOffset.magnitude < .0002f && hands.CurrentLocomotionEuler.magnitude < .02f);
                    Check("Stopped weapon returns to its saved resting position", Vector3.Distance(idlePosition, player.playerCamera.transform.InverseTransformPoint(pistol.weapon.transform.position)) < .004f);
                    Capture("stopped"); Next(4); return;
                case 4:
                    Input(Key.D); Mouse(false); if (elapsed > .4f) Sample(); if (elapsed < 1.6f) return;
                    Check("Sideways walking also has a restrained step rhythm", maximum.x - minimum.x > .004f && maximum.x - minimum.x < .025f && player.LocalVelocity.x > 1.5f);
                    Capture("strafe"); Next(5); return;
                case 5:
                    Input(Key.W); Mouse(true); if (elapsed > .5f) Sample(); if (elapsed < 1.6f) return;
                    Check("ADS keeps walking motion restrained", pistol.AimAmount > .99f && maximum.x - minimum.x < walkWidth * .25f);
                    Check("ADS retains the existing camera alignment", Mathf.Abs(player.playerCamera.fieldOfView - 62f) < .01f && Mathf.Abs(player.handsCamera.fieldOfView - 62f) < .01f);
                    Check("ADS is never in the lowered sprint pose", hands.SprintLowering < .001f);
                    Capture("ads-walk");
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Temporary movement obstruction";
                    wall.transform.position = player.transform.position + player.transform.forward * 1.2f + Vector3.up * 1.5f;
                    wall.transform.rotation = player.transform.rotation; wall.transform.localScale = new Vector3(5f, 3f, .4f); Physics.SyncTransforms();
                    Next(6); return;
                case 6:
                    Input(Key.W, Key.LeftShift); Mouse(false); if (elapsed < 2f) return;
                    Check("A wall stops real displacement", player.CurrentSpeed < .05f);
                    Check("Holding run against a wall does not bob or lower the weapon", hands.CurrentLocomotionOffset.magnitude < .0005f && hands.SprintLowering < .001f);
                    Check("Both hands remain attached throughout locomotion", maxGripError < .004f);
                    Check("Finger poses are unchanged throughout locomotion", maxFingerAngle < .1f);
                    Check("Crosshair ray follows the moving muzzle", maxRayAngle < .05f);
                    Check("All saved grip and view settings are preserved", handSettings == EditorJsonUtility.ToJson(hands));
                    Check("No runtime errors", errors == 0);
                    Finish(Report.ToString().Contains("FAIL ") ? "FAILED" : "SUCCESS"); return;
            }
        }
        static void Sample()
        {
            Vector3 value = hands.CurrentLocomotionOffset;
            minimum = Vector3.Min(minimum, value); maximum = Vector3.Max(maximum, value); sum += value; samples++;
        }
        static void Record(string label) { File.WriteAllText(Folder + "/" + label + "-motion.txt", "min=" + minimum.ToString("F4") + " max=" + maximum.ToString("F4") + " mean=" + (sum / samples).ToString("F4")); }
        static void CheckContact()
        {
            var weapon = pistol.weapon.transform;
            foreach (bool left in new[] { false, true })
            {
                var wrist = player.handsAnimator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var expected = weapon.position + weapon.rotation * (left ? ItfaiyeFirstPersonHands.LeftGrip : ItfaiyeFirstPersonHands.RightGrip);
                maxGripError = Mathf.Max(maxGripError, Vector3.Distance(wrist.position, expected));
            }
            var fingers = Fingers();
            for (int i = 0; i < fingers.Length; i++) maxFingerAngle = Mathf.Max(maxFingerAngle, Quaternion.Angle(fingerRotations[i], fingers[i]));
            maxRayAngle = Mathf.Max(maxRayAngle, Vector3.Angle(pistol.AimRay.direction, pistol.muzzle.forward));
        }
        static Quaternion[] Fingers()
        {
            int start = (int)HumanBodyBones.LeftThumbProximal;
            var result = new Quaternion[(int)HumanBodyBones.RightLittleDistal - start + 1];
            for (int i = 0; i < result.Length; i++) result[i] = player.handsAnimator.GetBoneTransform((HumanBodyBones)(start + i)).localRotation;
            return result;
        }
        static void Input(params Key[] keys) { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        static void Mouse(bool aim) { if (UnityEngine.InputSystem.Mouse.current != null) InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, new MouseState { buttons = (ushort)(aim ? 2 : 0) }); }
        static void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/" + name + ".png")); }
        static void Check(string name, bool pass) { Report.AppendLine((pass ? "PASS " : "FAIL ") + name); File.WriteAllText(Folder + "/progress.txt", Report.ToString()); }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Report.AppendLine(message); } }
        static void Finish(string result)
        {
            phase = -1; Application.logMessageReceived -= Log; Input(); Mouse(false);
            Time.captureDeltaTime = captureDelta;
            if (originalInput != null) InputSystem.settings = originalInput;
            if (temporaryInput != null) UnityEngine.Object.DestroyImmediate(temporaryInput);
            if (wall != null) UnityEngine.Object.DestroyImmediate(wall);
            if (floor != null) floor.localScale = floorScale;
            if (player != null) player.SetInputCaptured(false);
            File.WriteAllText(Folder + "/review.txt", result + "\n" + Report);
        }
    }
}
