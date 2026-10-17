using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

namespace Itfaiye.Editor
{
    // Runs in the real Play Mode scene. All test obstacles and movement are discarded on exit.
    [InitializeOnLoad]
    public static class ItfaiyePlayerReview
    {
        const string Folder = "Library/ItfaiyePlayer";
        static ItfaiyeFirstPersonController player;
        static Vector3 origin, phaseOrigin, firstFoot;
        static float started, maxFootMotion;
        static int phase = -1, lastFrame = -1;
        static bool phaseStarted;
        static bool requested;
        static GameObject wall;
        static readonly StringBuilder Report = new StringBuilder();
        static readonly List<string> Errors = new List<string>();
        static ItfaiyePlayerReview() { EditorApplication.update += Update; }

        [MenuItem("Tools/Itfaiye/Review Player In Play Mode %#F9")]
        static void RequestReview()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode before running the player review.");
            Directory.CreateDirectory(Folder);
            requested = true;
        }

        static void Update()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                if (phase >= 0) Finish("ABORTED: Play Mode ended during review.");
                return;
            }
            if (phase < 0)
            {
                if (!requested) return;
                requested = false;
                player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
                if (player == null) { File.WriteAllText(Folder + "/review.txt", "FAIL: No player found."); return; }
                origin = player.transform.position;
                Report.Clear(); Errors.Clear();
                Application.logMessageReceived += Log;
                player.SetInputCaptured(true);
                phase = 0; phaseStarted = false; lastFrame = -1;
            }
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try { Tick(); }
            catch (Exception exception) { Finish("FAIL: " + exception); }
        }

        static void Tick()
        {
            bool first = !phaseStarted;
            if (first)
            {
                started = Time.time;
                phaseOrigin = player.transform.position;
                phaseStarted = true;
                if (phase == 3 || phase == 5 || phase == 8 || phase == 9) ResetPosition();
                if (phase == 1) { firstFoot = Foot(); maxFootMotion = 0f; }
                if (phase == 8)
                {
                    wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    wall.name = "Review obstacle (temporary)";
                    wall.transform.position = origin + new Vector3(0f, 1.5f, 1.1f);
                    wall.transform.localScale = new Vector3(3f, 3f, .2f);
                    Physics.SyncTransforms();
                }
                if (phase == 9 && wall != null) UnityEngine.Object.Destroy(wall);
            }
            float elapsed = Time.time - started;
            switch (phase)
            {
                case 0:
                    Input();
                    if (elapsed < .8f) return;
                    Check("Idle clip", Clip() == "Idle");
                    Check("Valid humanoid avatar", player.bodyAnimator.isHuman && player.bodyAnimator.avatar.isValid);
                    Check("Grounded on authored platform", player.IsGrounded);
                    Check("No root motion drift", Vector3.Distance(origin, player.transform.position) < .15f);
                    Check("Animated hands rendered separately", Visible(player.handsAnimator).Length == 1 && Visible(player.handsAnimator)[0].name.Contains("_arms"));
                    Check("Only legs and shoes visible on body", Visible(player.bodyAnimator).All(r => r.name.Contains("_pants_") || r.name.Contains("_shoe_")) && Visible(player.bodyAnimator).Length == 2);
                    Check("One audio listener", UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Count(a => a.enabled) == 1);
                    Screenshot("front.png"); Next(); break;
                case 1:
                    Input(Key.W);
                    maxFootMotion = Mathf.Max(maxFootMotion, Vector3.Distance(firstFoot, Foot()));
                    if (elapsed < .95f) return;
                    Check("W walks", Distance() > 1f && player.CurrentSpeed > 1.6f && player.CurrentSpeed < 2.1f);
                    Check("Walk clip", Clip() == "Walk");
                    Check("Feet animate", maxFootMotion > .12f);
                    Screenshot("walk.png"); Next(); break;
                case 2:
                    Input();
                    if (elapsed < .5f) return;
                    Check("Release returns to Idle", player.CurrentSpeed < .05f && Clip() == "Idle"); Next(); break;
                case 3:
                    Input(Key.W, Key.LeftShift);
                    if (elapsed < .85f) return;
                    Check("Shift runs", Distance() > 2f && player.CurrentSpeed > 4.2f);
                    Check("Run clip", Clip() == "Run");
                    Screenshot("run.png"); Next(); break;
                case 4:
                    Input(); if (elapsed < .5f) return;
                    Check("Stops after running", player.CurrentSpeed < .05f); Next(); break;
                case 5:
                    Input();
                    Mouse(first ? new Vector2(0f, (player.Pitch - player.maximumPitch - 20f) / player.mouseSensitivity) : Vector2.zero);
                    if (elapsed < .65f) return;
                    Check("Mouse look stops at downward clamp", Mathf.Abs(player.Pitch - player.maximumPitch) < .1f);
                    foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    {
                        var foot = player.playerCamera.WorldToViewportPoint(player.bodyAnimator.GetBoneTransform(bone).position);
                        Check(bone + " visible when looking down", foot.z > 0f && foot.x > 0f && foot.x < 1f && foot.y > 0f && foot.y < 1f);
                    }
                    Screenshot("down.png"); Next(); break;
                case 6:
                    Input(Key.A); if (elapsed < .65f) return;
                    Check("A strafes left", player.transform.position.x < phaseOrigin.x - .7f); Next(); break;
                case 7:
                    Input(Key.S); if (elapsed < .65f) return;
                    Check("S moves backward", player.transform.position.z < phaseOrigin.z - .7f); Next(); break;
                case 8:
                    Input(Key.W); if (elapsed < 1.2f) return;
                    Check("Wall blocks capsule", player.transform.position.z < origin.z + .85f && player.CurrentSpeed < .05f);
                    Check("Blocked movement returns to idle", Clip() == "Idle"); Next(); break;
                case 9:
                    Input(Key.W, Key.D); if (elapsed < .65f) return;
                    Check("Diagonal speed normalized", player.CurrentSpeed > 1.6f && player.CurrentSpeed < 2.1f); Next(); break;
                case 10:
                    Input(first ? new[] {Key.Escape} : Array.Empty<Key>());
                    if (elapsed < .5f) return;
                    Check("Escape releases mouse and stops movement", !player.InputCaptured && player.CurrentSpeed < .05f);
                    Check("No runtime errors", Errors.Count == 0);
                    Finish(Report.ToString().Contains("FAIL") ? "FAILED" : "SUCCESS"); break;
            }
        }

        static SkinnedMeshRenderer[] Visible(Animator animator) => animator.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly).ToArray();
        static Vector3 Foot() => player.transform.InverseTransformPoint(player.bodyAnimator.GetBoneTransform(HumanBodyBones.LeftFoot).position);
        static float Distance() => Vector3.ProjectOnPlane(player.transform.position - phaseOrigin, Vector3.up).magnitude;
        static string Clip() => player.bodyAnimator.GetCurrentAnimatorClipInfo(0).OrderByDescending(c => c.weight).First().clip.name;
        static void Input(params Key[] keys) { if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        static void Mouse(Vector2 delta) { if (UnityEngine.InputSystem.Mouse.current != null) InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, new MouseState { delta = delta }); }
        static void Next() { phase++; phaseStarted = false; }
        static void Check(string name, bool success)
        {
            Report.AppendLine($"{(success ? "PASS" : "FAIL")} {name}; speed={player.CurrentSpeed:F3}; clip={Clip()}; pitch={player.Pitch:F1}; position={player.transform.position}");
            File.WriteAllText(Folder + "/review-progress.txt", Report.ToString());
        }
        static void ResetPosition()
        {
            var motor = player.GetComponent<CharacterController>();
            motor.enabled = false;
            player.transform.SetPositionAndRotation(origin + Vector3.up * .02f, Quaternion.identity);
            motor.enabled = true;
            phaseOrigin = player.transform.position;
        }
        static void Screenshot(string name) { ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/" + name)); }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message); }
        static void Finish(string result)
        {
            phase = -1;
            Application.logMessageReceived -= Log;
            Input(); Mouse(Vector2.zero);
            if (player != null) player.SetInputCaptured(false);
            if (wall != null) UnityEngine.Object.Destroy(wall);
            File.WriteAllText(Folder + "/review.txt", result + "\n" + Report + string.Join("\n", Errors));
        }
    }
}
