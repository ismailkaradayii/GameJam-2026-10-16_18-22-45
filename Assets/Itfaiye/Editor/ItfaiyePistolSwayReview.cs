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
    public static class ItfaiyePistolSwayReview
    {
        const string Folder = "Library/ItfaiyePistol/Sway";
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static ItfaiyeFirstPersonHands hands;
        static readonly StringBuilder Report = new StringBuilder();
        static int phase = -1, frame, errors;
        static float started, savedCaptureDelta, zoom, tightness, hipSway;
        static bool sway;
        static string savedHands;
        static Vector3 cameraPosition;
        static Quaternion[] oldFingers;
        static InputSettings originalInputSettings, reviewInputSettings;
        static ItfaiyePistolSwayReview() { EditorApplication.update += Update; }

        [MenuItem("Tools/Itfaiye/Review Sway and Close Grip %&F9")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            pistol = player.pistol; hands = pistol.Hands;
            if (pistol.Equipped || pistol.ShotsFired != 0) throw new InvalidOperationException("Use a fresh Play session.");
            Directory.CreateDirectory(Folder); Report.Clear(); errors = 0; frame = -1;
            savedHands = EditorJsonUtility.ToJson(hands);
            savedCaptureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60f;
            // Use a temporary settings copy so synthetic input is delivered to
            // gameplay even while the editor menu/Inspector has keyboard focus.
            originalInputSettings = InputSystem.settings;
            reviewInputSettings = UnityEngine.Object.Instantiate(originalInputSettings);
            reviewInputSettings.hideFlags = HideFlags.HideAndDontSave;
            reviewInputSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            reviewInputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = reviewInputSettings;
            cameraPosition = player.playerCamera.transform.localPosition;
            zoom = hands.pistolViewZoom; tightness = hands.leftGripTightness; sway = hands.weaponSway;
            hands.pistolViewZoom = 0f; hands.weaponSway = false; SetTightness(0f);
            Application.logMessageReceived += Log;
            player.SetInputCaptured(true); Input(); Mouse(); pistol.Equip(); Next(0);
        }
        static void SetTightness(float value)
        {
            var serialized = new SerializedObject(hands);
            serialized.FindProperty("leftGripTightness").floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
            // A menu invocation can release the game's cursor capture. Reacquire
            // it for the synthetic gameplay input, just as a click in Game does.
            player.SetInputCaptured(true);
            float elapsed = Time.time - started;
            if (elapsed > 5f) throw new TimeoutException("Sway review stalled at " + phase);
            switch (phase)
            {
                case 0:
                    Input(); Mouse(); if (elapsed < .7f) return;
                    oldFingers = Fingers();
                    Check("Zero zoom reproduces the old camera view", Mathf.Abs(player.playerCamera.fieldOfView - player.fieldOfView) < .01f);
                    PistolGripWorkbench.CaptureViews(player, Folder + "/before");
                    hands.pistolViewZoom = zoom; hands.weaponSway = sway; SetTightness(tightness); Next(1); return;
                case 1:
                    Input(); Mouse(); if (elapsed < .35f) return;
                    Check("Hip view is gently closer", Mathf.Abs(player.playerCamera.fieldOfView - 75f) < .01f);
                    Check("Both cameras share the same projection", Mathf.Abs(player.handsCamera.fieldOfView - player.playerCamera.fieldOfView) < .001f);
                    Check("Camera position and saved weapon pose stay fixed", Vector3.Distance(cameraPosition, player.playerCamera.transform.localPosition) < .0001f);
                    var fingers = Fingers();
                    bool rightSame = true, thumbSame = true;
                    int rightStart = (int)HumanBodyBones.RightThumbProximal - (int)HumanBodyBones.LeftThumbProximal;
                    for (int i = rightStart; i < fingers.Length; i++) rightSame &= Quaternion.Angle(fingers[i], oldFingers[i]) < .1f;
                    for (int i = 0; i < 3; i++) thumbSame &= Quaternion.Angle(fingers[i], oldFingers[i]) < .1f;
                    int middleTip = (int)HumanBodyBones.LeftMiddleDistal - (int)HumanBodyBones.LeftThumbProximal;
                    Check("Only the left support fingers close more", rightSame && thumbSame && Quaternion.Angle(fingers[middleTip], oldFingers[middleTip]) > 1f);
                    CheckGrip("Idle"); PistolGripWorkbench.CaptureViews(player, Folder + "/after");
                    Next(2); return;
                case 2:
                    Input(); Mouse(129.6f); if (elapsed < .4f) return;
                    File.WriteAllText(Folder + "/input.txt", "Captured=" + player.InputCaptured + ", look=" + player.LookAngularVelocity + ", sway=" + hands.CurrentSwayOffset);
                    hipSway = Mathf.Abs(hands.CurrentSwayOffset.x);
                    Check("Turning right gives a small leftward lag", hands.CurrentSwayOffset.x < -.007f && hands.CurrentSwayEuler.y < -1f);
                    Check("Fast turns stay within controlled limits", hands.CurrentSwayOffset.magnitude < .025f && hands.CurrentSwayEuler.magnitude < 3f);
                    CheckGrip("Right turn"); CheckRay();
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/turn-right.png")); Next(3); return;
                case 3:
                    Input(); Mouse(); if (elapsed < .6f) return;
                    Check("Sway settles without changing the resting grip", hands.CurrentSwayOffset.magnitude < .00005f && hands.CurrentSwayEuler.magnitude < .01f);
                    Next(4); return;
                case 4:
                    Input(); Mouse(-129.6f); if (elapsed < .4f) return;
                    Check("Turning left mirrors the right-turn sway", hands.CurrentSwayOffset.x > .007f && Mathf.Abs(hands.CurrentSwayOffset.x - hipSway) < .0002f);
                    CheckGrip("Left turn"); Next(5); return;
                case 5:
                    Input(Key.D); Mouse(); if (elapsed < .55f) return;
                    Check("Strafing right produces a restrained lean", player.LocalVelocity.x > 1.5f && hands.CurrentSwayEuler.z > .8f && hands.CurrentSwayOffset.x < -.004f);
                    CheckGrip("Strafe"); Next(6); return;
                case 6:
                    Input(); Mouse(129.6f, true); if (elapsed < .55f) return;
                    Check("Aiming reduces sway to the configured amount", hipSway > .007f && Mathf.Abs(Mathf.Abs(hands.CurrentSwayOffset.x) - hipSway * hands.aimSwayMultiplier) < .0002f);
                    Check("ADS retains its original field of view", pistol.AimAmount > .99f && Mathf.Abs(player.playerCamera.fieldOfView - 62f) < .01f);
                    CheckGrip("ADS"); CheckRay();
                    ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/ads.png")); Next(7); return;
                case 7:
                    Input(); Mouse(); if (elapsed < .6f) return;
                    Time.captureDeltaTime = 1f / 30f; Next(8); return;
                case 8:
                    Input(); Mouse(129.6f); if (elapsed < .4f) return;
                    Check("Same turn speed has the same sway at 30 and 60 FPS", hipSway > .007f && Mathf.Abs(Mathf.Abs(hands.CurrentSwayOffset.x) - hipSway) < .0003f);
                    Input(Key.Digit1); Mouse(); Next(9); return;
                case 9:
                    Input(); Mouse(); if (elapsed < .6f) return;
                    Check("Holstering restores the original camera view", !pistol.Equipped && Mathf.Abs(player.playerCamera.fieldOfView - player.fieldOfView) < .01f);
                    Check("Holstering leaves no visible sway", hands.CurrentSwayOffset.sqrMagnitude < .0000001f);
                    Check("Playback preserves every configured hand setting", savedHands == EditorJsonUtility.ToJson(hands));
                    Check("No runtime errors", errors == 0);
                    Finish(Report.ToString().Contains("FAIL ") ? "FAILED" : "SUCCESS"); return;
            }
        }
        static Quaternion[] Fingers()
        {
            int start = (int)HumanBodyBones.LeftThumbProximal;
            var values = new Quaternion[(int)HumanBodyBones.RightLittleDistal - start + 1];
            for (int i = 0; i < values.Length; i++) values[i] = player.handsAnimator.GetBoneTransform((HumanBodyBones)(start + i)).localRotation;
            return values;
        }
        static void CheckGrip(string action)
        {
            var weapon = pistol.weapon.transform;
            var right = player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            var left = player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            float rightError = Vector3.Distance(right.position, weapon.position + weapon.rotation * ItfaiyeFirstPersonHands.RightGrip);
            float leftError = Vector3.Distance(left.position, weapon.position + weapon.rotation * ItfaiyeFirstPersonHands.LeftGrip);
            Check(action + " keeps both wrists on the grip", rightError < .004f && leftError < .004f);
        }
        static void CheckRay()
        {
            Check("Aim ray follows the posed barrel", Vector3.Distance(pistol.AimRay.origin, pistol.muzzle.position) < .0001f && Vector3.Angle(pistol.AimRay.direction, pistol.muzzle.forward) < .01f);
            Check("Crosshair projects through the same camera as the hands", Mathf.Abs(player.playerCamera.fieldOfView - player.handsCamera.fieldOfView) < .001f && Vector3.Distance(player.playerCamera.transform.position, player.handsCamera.transform.position) < .0001f);
        }
        static void Input(params Key[] keys) { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        static void Mouse(float yawSpeed = 0f, bool aim = false)
        {
            if (UnityEngine.InputSystem.Mouse.current == null) return;
            var state = new MouseState { delta = new Vector2(yawSpeed * Time.captureDeltaTime / player.mouseSensitivity, 0f) };
            if (aim) state = state.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, state);
        }
        static void Check(string name, bool pass) { Report.AppendLine((pass ? "PASS " : "FAIL ") + name); File.WriteAllText(Folder + "/progress.txt", Report.ToString()); }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Report.AppendLine(message); } }
        static void Finish(string result)
        {
            phase = -1; Application.logMessageReceived -= Log; Input(); Mouse();
            Time.captureDeltaTime = savedCaptureDelta;
            if (originalInputSettings != null) InputSystem.settings = originalInputSettings;
            if (reviewInputSettings != null) UnityEngine.Object.DestroyImmediate(reviewInputSettings);
            if (hands != null) { hands.pistolViewZoom = zoom; hands.weaponSway = sway; SetTightness(tightness); }
            if (player != null) player.SetInputCaptured(false);
            File.WriteAllText(Folder + "/review.txt", result + "\n" + Report);
        }
    }
}
