using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Itfaiye.Editor
{
    // Integration review: feeds the real Input System, motor and Animator in the open scene.
    [InitializeOnLoad]
    public static class ItfaiyePistolReview
    {
        const string Folder = "Library/ItfaiyePistol";
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static Vector3 origin;
        static int phase = -1, lastFrame = -1;
        static float started, peak;
        static bool first;
        static readonly StringBuilder Report = new StringBuilder();
        static readonly HashSet<string> Seen = new HashSet<string>();
        static readonly List<string> Errors = new List<string>();
        static readonly Key[][] Directions = { new[] { Key.W }, new[] { Key.S }, new[] { Key.A }, new[] { Key.D },
            new[] { Key.W, Key.A }, new[] { Key.W, Key.D }, new[] { Key.S, Key.A }, new[] { Key.S, Key.D } };
        static readonly string[] Walk = { "pistol walk", "pistol walk backward", "pistol strafe", "pistol strafe (2)",
            "pistol walk arc (2)", "pistol walk arc", "pistol walk backward arc", "pistol walk backward arc (2)" };
        static readonly string[] Run = { "pistol run", "pistol run backward", "pistol strafe", "pistol strafe (2)",
            "pistol run arc (2)", "pistol run arc", "pistol run backward arc", "pistol run backward arc (2)" };
        static ItfaiyePistolReview() { EditorApplication.update += Update; }

        [MenuItem("Tools/Itfaiye/Review Pistol In Play Mode %#F6")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            pistol = player.pistol;
            if (pistol == null) throw new InvalidOperationException("The player is missing the pistol reference.");
            Directory.CreateDirectory(Folder);
            origin = player.transform.position;
            phase = 0; lastFrame = -1; first = true;
            Seen.Clear(); Errors.Clear(); Report.Clear();
            Application.logMessageReceived += Log;
            player.SetInputCaptured(true);
            Input(); Mouse(false, false);
        }

        static void Update()
        {
            if (phase < 0) return;
            if (!EditorApplication.isPlaying) { Finish("ABORTED"); return; }
            if (EditorApplication.isPaused || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try { Tick(); } catch (Exception ex) { Finish("FAIL: " + ex); }
        }

        static void Tick()
        {
            if (phase != 25 && phase != 26 && phase != 31 && phase != 32) Mouse(false, false);
            if (first) { started = Time.time; peak = 0f; }
            float elapsed = Time.time - started;
            foreach (var info in player.bodyAnimator.GetCurrentAnimatorClipInfo(0))
                if (info.weight > .3f && info.clip.name.StartsWith("pistol")) Seen.Add(info.clip.name);
            peak = Mathf.Max(peak, player.transform.position.y - origin.y);
            if (phase >= 2 && phase <= 17)
            {
                int direction = (phase - 2) % 8;
                bool running = phase >= 10;
                if (first) ResetPosition();
                Input(running ? Directions[direction].Concat(new[] { Key.LeftShift }).ToArray() : Directions[direction]);
                if (elapsed > .75f)
                {
                    string expected = (running ? Run : Walk)[direction];
                    Check((running ? "Run " : "Walk ") + direction + " uses " + expected, Clip() == expected);
                    Check("Movement speed matches motor", Mathf.Abs(player.CurrentSpeed - (running ? player.runSpeed : player.walkSpeed)) < .12f);
                    var hips = player.transform.InverseTransformPoint(player.bodyAnimator.GetBoneTransform(HumanBodyBones.Hips).position);
                    Check("No animation root drift", new Vector2(hips.x, hips.z).magnitude < .8f);
                    CheckGrip();
                    if (phase == 2) Capture("walk");
                    if (phase == 10) Capture("run");
                    Next(); return;
                }
                first = false; return;
            }
            switch (phase)
            {
                case 0:
                    Input(); if (elapsed < .5f) break;
                    Check("Starts unarmed", !pistol.Equipped && !pistol.weapon.activeSelf && Clip() == "Idle");
                    Check("All 20 pistol clips connected", player.bodyAnimator.runtimeAnimatorController.animationClips.Where(c => c.name.StartsWith("pistol")).Select(c => c.name).Distinct().Count() == 20);
                    Next(); return;
                case 1:
                    Input(first ? new[] { Key.Digit1 } : Array.Empty<Key>());
                    if (elapsed < .75f) break;
                    Check("1 equips the pistol", pistol.Ready && pistol.weapon.activeSelf && Clip() == "pistol idle");
                    Check("URP materials supported", pistol.weapon.GetComponentsInChildren<Renderer>().All(r => r.sharedMaterials.All(m => m != null && m.shader.isSupported && m.GetTag("RenderPipeline", false) == "UniversalPipeline")));
                    Check("Pistol uses camera-space grip rig", pistol.weapon.transform.parent == player.playerCamera.transform);
                    Capture("idle"); Next(); return;
                case 18:
                    Input(); if (elapsed < .7f) break;
                    Check("Release movement returns to armed idle", Clip() == "pistol idle" && player.CurrentSpeed < .05f);
                    Check("Barrel follows camera forward (dot=" + Vector3.Dot(pistol.weapon.transform.forward, player.playerCamera.transform.forward).ToString("F3") + ")", Vector3.Dot(pistol.weapon.transform.forward, player.playerCamera.transform.forward) > .98f);
                    ResetPosition(); Next(); return;
                case 19:
                    Input(elapsed > .15f && elapsed < .28f ? new[] { Key.C } : Array.Empty<Key>()); if (elapsed < .5f) break;
                    Check("C starts stand-to-kneel", Clip() == "pistol stand to kneel" && player.IsKneeling); Next(); return;
                case 20:
                    Input(); if (elapsed < 1.1f) break;
                    Check("Kneeling idle settles", Clip() == "pistol kneeling idle" && player.KneelAmount > .98f);
                    Check("Kneeling camera and collider lower", player.playerCamera.transform.localPosition.y < 1.3f && player.GetComponent<CharacterController>().height < 1.3f);
                    CheckGrip(); Capture("kneel"); Next(); return;
                case 21:
                    Input(Key.W); if (elapsed < .35f) break;
                    Check("Movement first plays kneel-to-stand", Clip() == "pistol kneel to stand" && player.CurrentSpeed < .05f); Next(); return;
                case 22:
                    Input(); if (elapsed < 1.5f) break;
                    Check("Standing restores camera and capsule", player.CanChangeWeapon && player.playerCamera.transform.localPosition.y > 1.65f && player.GetComponent<CharacterController>().height > 1.8f);
                    ResetPosition(); Next(); return;
                case 23:
                    Input(elapsed > .15f && elapsed < .28f ? new[] { Key.Space } : Array.Empty<Key>());
                    if (elapsed < 1.3f) break;
                    Check("Standing jump lifts and lands (peak=" + peak.ToString("F2") + ")", peak > .65f && peak < 1.2f && player.IsGrounded && Seen.Contains("pistol jump"));
                    Check("Standing jump returns to locomotion after landing", Clip() == "pistol idle");
                    ResetPosition(); Next(); return;
                case 24:
                    Input(elapsed > .15f && elapsed < .28f ? new[] { Key.W, Key.Space } : (elapsed < .7f ? new[] { Key.W } : Array.Empty<Key>()));
                    if (elapsed < 1.3f) break;
                    Check("Moving jump lifts and lands", peak > .65f && player.IsGrounded && Seen.Contains("pistol jump (2)"));
                    Check("Moving jump returns to locomotion after landing", Clip() == "pistol idle");
                    ResetPosition(); Next(); return;
                case 25:
                    Input(); Mouse(true, false);
                    if (pistol.ShotsFired == 1 && pistol.ShotAge < .025f && pistol.FlashVisible) Capture("fire");
                    if (elapsed < .5f) break;
                    Check("Click fires once; holding is semi-automatic", pistol.Ammo == 11 && pistol.ShotsFired == 1); Next(); return;
                case 26:
                    Input(); Mouse(false, true); if (elapsed < .5f) break;
                    Check("Right mouse aims smoothly", pistol.IsAiming && pistol.AimAmount > .98f && Mathf.Abs(player.playerCamera.fieldOfView - 62f) < .1f);
                    Check("ADS barrel aligned with aim ray", Vector3.Dot(pistol.weapon.transform.forward, player.playerCamera.transform.forward) > .9995f);
                    CheckSightCenter();
                    CheckGrip();
                    Capture("aim"); Next(); return;
                case 27:
                    Input(first ? new[] { Key.R } : Array.Empty<Key>()); if (elapsed < .6f) break;
                    Check("R starts reload", pistol.IsReloading && pistol.Ammo == 11 && !pistol.Fire()); CheckGrip(); Capture("reload"); Next(); return;
                case 28:
                    Input(); if (elapsed < pistol.reloadDuration) break;
                    Check("Reload transfers ammo once", !pistol.IsReloading && pistol.Ammo == 12 && pistol.reserveAmmo == 47); Next(); return;
                case 29:
                    Input(first ? new[] { Key.Digit2 } : Array.Empty<Key>()); if (elapsed < .7f) break;
                    Check("2 selects empty hands and restores unarmed idle", !pistol.Equipped && !pistol.weapon.activeSelf && Clip() == "Idle"); Next(); return;
                case 30:
                    Input(first ? new[] { Key.Digit1 } : Array.Empty<Key>()); if (elapsed < .7f) break;
                    Check("Re-equipping preserves ammo", pistol.Ready && pistol.Ammo == 12 && pistol.reserveAmmo == 47); Next(); return;
                case 31:
                    Input(); Mouse(false, false, first ? new Vector2(0, (player.Pitch - 80f) / player.mouseSensitivity) : Vector2.zero);
                    if (elapsed < .6f) break;
                    foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                    {
                        var foot = player.playerCamera.WorldToViewportPoint(player.bodyAnimator.GetBoneTransform(bone).position);
                        Check(bone + " visible with pistol", foot.z > 0 && foot.x > 0 && foot.x < 1 && foot.y > 0 && foot.y < 1);
                    }
                    Capture("down"); Next(); return;
                case 32:
                    Input(Key.W); Mouse(false, true, first ? new Vector2(0, player.Pitch / player.mouseSensitivity) : Vector2.zero);
                    if (elapsed < .6f) break;
                    Check("Can walk while aiming", player.CurrentSpeed > 1f && pistol.AimAmount > .99f);
                    CheckSightCenter(); CheckGrip(); ResetPosition(); Next(); return;
                case 33:
                    Input(first ? new[] { Key.Digit2 } : Array.Empty<Key>()); if (elapsed < .6f) break;
                    Check("2 holsters from ADS", !pistol.Equipped && !pistol.weapon.activeSelf && pistol.AimAmount < .01f); Next(); return;
                case 34:
                    Input(first ? new[] { Key.Digit1 } : elapsed > .10f && elapsed < .16f ? new[] { Key.Digit2 } : Array.Empty<Key>());
                    if (elapsed < .6f) break;
                    Check("Selecting 2 during draw returns to empty hands", !pistol.Equipped && !pistol.weapon.activeSelf && pistol.Ammo == 12 && pistol.reserveAmmo == 47); Next(); return;
                case 35:
                    Input(first ? new[] { Key.Escape } : Array.Empty<Key>()); if (elapsed < .5f) break;
                    Check("Escape releases input", !player.InputCaptured && player.CurrentSpeed < .05f);
                    Check("All 20 clips exercised through inputs", Seen.Count == 20);
                    Check("No runtime errors", Errors.Count == 0);
                    Finish(Report.ToString().Contains("FAIL") ? "FAILED" : "SUCCESS"); return;
            }
            first = false;
        }

        static void CheckSightCenter()
        {
            foreach (var point in new[] { ItfaiyeFirstPersonHands.RearSight, ItfaiyeFirstPersonHands.FrontSight })
            {
                Vector3 viewport = player.playerCamera.WorldToViewportPoint(pistol.weapon.transform.TransformPoint(point));
                float pixels = new Vector2((viewport.x - .5f) * Screen.width, (viewport.y - .5f) * Screen.height).magnitude;
                Check("ADS sight centered within one pixel (error=" + pixels.ToString("F3") + ")", pixels < 1f);
            }
        }

        static void CheckGrip()
        {
            foreach (bool left in new[] { false, true })
            {
                if (left && pistol.IsReloading) continue;
                Vector3 expected = pistol.weapon.transform.position + pistol.weapon.transform.rotation * (left ? ItfaiyeFirstPersonHands.LeftGrip : ItfaiyeFirstPersonHands.RightGrip);
                Vector3 actual = player.handsAnimator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
                float error = Vector3.Distance(expected, actual);
                Check((left ? "Left" : "Right") + " wrist stays on grip (mm=" + (error * 1000).ToString("F2") + ")", error < .003f);
            }
        }

        static void Input(params Key[] keys)
        { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        static void Mouse(bool left, bool right, Vector2 delta = default)
        { InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, new MouseState { buttons = (ushort)((left ? 1 : 0) | (right ? 2 : 0)), delta = delta }); }
        static string Clip() => player.bodyAnimator.GetCurrentAnimatorClipInfo(0).OrderByDescending(c => c.weight).FirstOrDefault().clip?.name ?? "none";
        static void Next() { phase++; first = true; }
        static void Check(string name, bool pass)
        {
            Report.AppendLine($"{(pass ? "PASS" : "FAIL")} {name}; clip={Clip()}; speed={player.CurrentSpeed:F2}; captured={player.InputCaptured}");
            File.WriteAllText(Folder + "/review-progress.txt", Report.ToString());
        }
        static void ResetPosition()
        {
            var motor = player.GetComponent<CharacterController>(); motor.enabled = false;
            player.transform.SetPositionAndRotation(origin + Vector3.up * .02f, Quaternion.identity); motor.enabled = true;
        }
        static void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/" + name + ".png")); }
        static void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) Errors.Add(message); }
        static void Finish(string result)
        {
            phase = -1;
            Application.logMessageReceived -= Log;
            Input(); Mouse(false, false);
            if (player != null) player.SetInputCaptured(false);
            File.WriteAllText(Folder + "/review.txt", result + "\n" + Report + "\nExercised: " + string.Join(", ", Seen.OrderBy(s => s)) + "\n" + string.Join("\n", Errors));
        }
    }
}
