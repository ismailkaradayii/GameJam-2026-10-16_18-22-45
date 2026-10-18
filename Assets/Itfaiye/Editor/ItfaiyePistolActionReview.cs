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
    public static class ItfaiyePistolActionReview
    {
        const string Folder = "Library/ItfaiyePistol/Actions";
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static GameObject target;
        static Material material;
        static InputSettings originalInput, temporaryInput;
        static float captureDelta;
        static int phase = -1, lastFrame, sample, errors, burstFrame;
        static float started;
        static bool first, capturedFlash;
        static Vector3 predicted;
        static readonly StringBuilder Report = new StringBuilder();
        static readonly float[] Samples = { .08f, .17f, .25f, .37f, .50f, .60f, .67f, .77f, .84f, .875f, .91f, .98f };
        static ItfaiyePistolActionReview() { EditorApplication.update += Update; }

        [MenuItem("Tools/Itfaiye/Review Pistol Actions %#F2")]
        public static void Start()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Start Play Mode first.");
            player = UnityEngine.Object.FindFirstObjectByType<ItfaiyeFirstPersonController>();
            pistol = player.pistol;
            if (pistol.ShotsFired != 0 || pistol.Equipped) throw new InvalidOperationException("Use a fresh Play Mode session with the pistol holstered.");
            Directory.CreateDirectory(Folder);
            captureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60f;
            originalInput = InputSystem.settings; temporaryInput = UnityEngine.Object.Instantiate(originalInput);
            temporaryInput.hideFlags = HideFlags.HideAndDontSave;
            temporaryInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            temporaryInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = temporaryInput;
            Report.Clear(); errors = 0; phase = 0; first = true; lastFrame = -1; capturedFlash = false; sample = 0;
            Application.logMessageReceived += Log;
            player.SetInputCaptured(true);
            target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.name = "Temporary Pistol Test Target";
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(.15f, .32f, .40f);
            target.GetComponent<Renderer>().sharedMaterial = material;
            PlaceTarget(8f); Input(); Mouse(false);
        }
        static void PlaceTarget(float distance)
        {
            var camera = player.playerCamera.transform;
            target.transform.SetPositionAndRotation(camera.position + camera.forward * distance, camera.rotation);
            target.transform.localScale = new Vector3(5f, 5f, .1f);
            Physics.SyncTransforms();
        }
        static void Update()
        {
            if (phase < 0) return;
            if (!EditorApplication.isPlaying) { Finish("ABORTED"); return; }
            if (EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try { Tick(); } catch (Exception ex) { Finish("FAILED: " + ex); }
        }
        static void Tick()
        {
            player.SetInputCaptured(true);
            if (first) started = Time.time;
            float elapsed = Time.time - started;
            switch (phase)
            {
                case 0:
                    Input(); Mouse(false); if (elapsed < .3f) break; Next(); return;
                case 1:
                    Input(first ? new[] { Key.Digit1 } : Array.Empty<Key>()); if (elapsed < .6f) break;
                    Check("Equipped with action components", pistol.Ready && pistol.slide.name == "Slide Assembly" && pistol.muzzleFlash != null && pistol.muzzleSmoke != null);
                    Check("Far crosshair selects target", pistol.AimCollider == target.GetComponent<Collider>());
                    predicted = pistol.AimPoint; Next(); return;
                case 2:
                    Input(); Mouse(true);
                    if (pistol.ShotsFired == 1 && !capturedFlash)
                    {
                        capturedFlash = true;
                        Check("Shot hits the displayed far target point", pistol.LastShotCollider == target.GetComponent<Collider>() && Vector3.Distance(predicted, pistol.LastShotPoint) < .01f);
                        Check("Flash starts at muzzle", pistol.FlashVisible && Vector3.Distance(pistol.muzzleFlash.transform.position, pistol.muzzle.position) < .0001f);
                        Check("Muzzle is at barrel tip", Vector3.Distance(pistol.muzzle.localPosition, new Vector3(0f, 1.24f, 2.19f)) < .001f);
                        var particles = new ParticleSystem.Particle[16];
                        int count = pistol.muzzleFlash.GetParticles(particles);
                        var details = new StringBuilder();
                        details.AppendLine("Flash scale " + pistol.muzzleFlash.transform.lossyScale + ", age " + pistol.ShotAge + ", muzzle screen " + player.playerCamera.WorldToScreenPoint(pistol.muzzle.position));
                        details.AppendLine("Bounds " + pistol.muzzleFlash.GetComponent<Renderer>().bounds);
                        for (int p = 0; p < count; p++) details.AppendLine("Particle " + particles[p].position + " size " + particles[p].GetCurrentSize(pistol.muzzleFlash) + " color " + particles[p].GetCurrentColor(pistol.muzzleFlash));
                        File.WriteAllText(Folder + "/flash.txt", details.ToString());
                        PistolGripWorkbench.CaptureViews(player, Folder + "/shot");
                    }
                    if (elapsed < .35f) break;
                    Check("Single held click fires one shot", pistol.ShotsFired == 1 && pistol.Ammo == 11);
                    Check("Recoil and slide return", pistol.Recoil < .01f && pistol.SlideAmount < .01f && !pistol.FlashVisible);
                    Mouse(false); PlaceTarget(2f); Next(); return;
                case 3:
                    Input(); Mouse(false); if (elapsed < .2f) break;
                    Check("Near crosshair projects muzzle hit, not fixed centre", pistol.AimCollider == target.GetComponent<Collider>() && Vector2.Distance(pistol.CrosshairScreenPosition, new Vector2(Screen.width * .5f, Screen.height * .5f)) > 5f);
                    predicted = pistol.AimPoint; capturedFlash = false; Next(); return;
                case 4:
                    Mouse(true);
                    if (pistol.ShotsFired == 2 && !capturedFlash)
                    {
                        capturedFlash = true;
                        Check("Near shot agrees with crosshair", pistol.LastShotCollider == target.GetComponent<Collider>() && Vector3.Distance(predicted, pistol.LastShotPoint) < .01f);
                        Capture("near-shot");
                    }
                    if (elapsed < .3f) break;
                    Mouse(false); PlaceTarget(.25f); Next(); return;
                case 5:
                    Mouse(false); if (elapsed < .15f) break;
                    Check("Wall before muzzle blocks the shot path", pistol.MuzzleObstructed && pistol.AimCollider == target.GetComponent<Collider>());
                    PlaceTarget(8f); Next(); return;
                case 6:
                    Input(first ? new[] { Key.R } : Array.Empty<Key>()); Mouse(false);
                    ObserveReload();
                    if (elapsed < pistol.reloadDuration + .2f) break;
                    Check("Reload completes after seating and slide release", !pistol.IsReloading && pistol.Ready && pistol.Ammo == 12 && pistol.reserveAmmo == 46 && pistol.SlideAmount < .01f);
                    Check("Only seated magazine remains", pistol.magazine.gameObject.activeSelf && !pistol.ReplacementMagazine.gameObject.activeSelf);
                    Check("Left hand returns to support grip", GripError(ItfaiyeFirstPersonHands.LeftGrip) < .003f);
                    Next(); burstFrame = 0; return;
                case 7:
                    Input();
                    Mouse(burstFrame % 18 == 0); burstFrame++;
                    if (pistol.Ammo > 0 && elapsed < 10f) break;
                    Mouse(false); Next(); return;
                case 8:
                    Mouse(false); if (elapsed < .25f) break;
                    Check("Empty magazine holds slide back", pistol.Ammo == 0 && pistol.SlideAmount > .99f && !pistol.Fire());
                    Next(); return;
                case 9:
                    Input(first ? new[] { Key.R } : elapsed > .5f && elapsed < .6f ? new[] { Key.Digit2 } : Array.Empty<Key>());
                    if (pistol.IsReloading && pistol.ReloadProgress > .70f && pistol.ReloadProgress < .78f)
                        CheckOnce("Empty reload keeps slide locked until hand reaches it", pistol.SlideAmount > .99f);
                    if (elapsed < pistol.reloadDuration + .5f) break;
                    Check("Queued holster finishes reload without losing ammo", !pistol.Equipped && !pistol.weapon.activeSelf && pistol.Ammo == 12 && pistol.reserveAmmo == 34);
                    Next(); return;
                case 10:
                    Input(first ? new[] { Key.Digit1 } : Array.Empty<Key>()); if (elapsed < .6f) break;
                    Check("Re-equip restores grip and closed slide", pistol.Ready && pistol.SlideAmount < .01f && GripError(ItfaiyeFirstPersonHands.LeftGrip) < .003f);
                    Check("No runtime errors", errors == 0);
                    Finish(Report.ToString().Contains("FAIL") ? "FAILED" : "SUCCESS"); return;
            }
            first = false;
        }
        static void ObserveReload()
        {
            if (!pistol.IsReloading) return;
            float t = pistol.ReloadProgress;
            CheckOnce("Fire and repeated R are blocked during reload", !pistol.Fire() && !pistol.Reload());
            if (sample >= Samples.Length || t < Samples[sample]) return;
            string label = "reload-" + sample.ToString("00");
            Capture(label);
            var hand = player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (t > .18f && t < .68f && (pistol.magazine.gameObject.activeSelf || pistol.ReplacementMagazine.gameObject.activeSelf))
            {
                var held = t < .40f ? pistol.magazine : pistol.ReplacementMagazine;
                var expected = held.position + held.rotation * ItfaiyePistolReloadPose.MagazineWristOffset;
                float mm = Vector3.Distance(expected, hand.position) * 1000f;
                Check("Magazine and left hand stay together at " + t.ToString("F2") + " (mm=" + mm.ToString("F2") + ")", mm < 4f);
            }
            if (t > ItfaiyePistolReloadPose.SlidePullStarts && t < ItfaiyePistolReloadPose.SlideReleasedAt)
            {
                var expected = pistol.slide.position + pistol.weapon.transform.rotation * pistol.Hands.slideGripPosition;
                Check("Left hand pulls actual slide (mm=" + (Vector3.Distance(expected, hand.position) * 1000f).ToString("F2") + ")", Vector3.Distance(expected, hand.position) < .004f);
            }
            if (sample == 2 || sample == 5 || sample == 8)
                PistolGripWorkbench.CaptureViews(player, Folder + "/" + label);
            if (sample == 3) Check("Old magazine leaves grip before replacement appears", !pistol.ReplacementMagazine.gameObject.activeSelf);
            if (sample == 4) Check("Replacement visible with empty magazine well", !pistol.magazine.gameObject.activeSelf && pistol.ReplacementMagazine.gameObject.activeSelf);
            if (sample == 6) Check("Ammo commits once when new magazine seats", pistol.ReloadCommitted && pistol.Ammo == 12 && pistol.reserveAmmo == 46);
            sample++;
        }
        static float GripError(Vector3 grip)
        {
            return Vector3.Distance(player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position,
                pistol.weapon.transform.position + pistol.weapon.transform.rotation * grip);
        }
        static void CheckOnce(string name, bool pass) { if (!Report.ToString().Contains(name)) Check(name, pass); }
        static void Check(string name, bool pass)
        {
            Report.AppendLine((pass ? "PASS " : "FAIL ") + name);
            File.WriteAllText(Folder + "/progress.txt", Report.ToString());
        }
        static void Next() { phase++; first = true; }
        static void Input(params Key[] keys) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        static void Mouse(bool down) { InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, new MouseState { buttons = (ushort)(down ? 1 : 0) }); }
        static void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.GetFullPath(Folder + "/" + name + ".png")); }
        static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; Report.AppendLine(message); } }
        static void Finish(string result)
        {
            phase = -1; Input(); Mouse(false); player.SetInputCaptured(false);
            Time.captureDeltaTime = captureDelta;
            if (originalInput != null) InputSystem.settings = originalInput;
            if (temporaryInput != null) UnityEngine.Object.DestroyImmediate(temporaryInput);
            Application.logMessageReceived -= Log;
            if (target != null) UnityEngine.Object.DestroyImmediate(target);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
            File.WriteAllText(Folder + "/review.txt", result + "\n" + Report);
        }
    }
}
