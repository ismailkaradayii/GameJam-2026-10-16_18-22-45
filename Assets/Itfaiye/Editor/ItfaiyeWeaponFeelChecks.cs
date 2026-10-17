using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Itfaiye.Editor
{
    [InitializeOnLoad]
    public static class ItfaiyeWeaponFeelChecks
    {
        const string Folder = ItfaiyeWeaponFeelReview.Folder;
        const string Pending = "ItfaiyeWeaponFeelChecks.Pending";
        [Serializable] public class Check { public string name, evidence; public bool passed; }
        [Serializable] public class Report { public string status, error; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
        static Report report;
        static Scene previous, review;
        static readonly List<GameObject> hidden = new List<GameObject>();
        static ItfaiyeFirstPersonController player;
        static ItfaiyePistol pistol;
        static Camera bodyCamera;
        static ItfaiyeWeaponReviewDriver driver;
        static Material groundMaterial;
        static float phaseAt, began, captureDelta, maxGrip, maxYaw, maxFootDelta, shotRequested, shotDelay;
        static Quaternion lastLeftFoot;
        static int phase = -1, lastFrame, requestedShots, initialAmmo;
        static bool wasPlaying, wasBackground, capturedShot, queued, savedCursor, footSampled;
        static CursorLockMode savedLock;

        static ItfaiyeWeaponFeelChecks()
        {
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
                { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
                if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Play Mode interrupted");
            };
        }

        public static void Begin()
        {
            if (phase >= 0 || EditorApplication.isCompiling) return;
            SessionState.SetBool(Pending + ".WasPlaying", EditorApplication.isPlaying);
            if (EditorApplication.isPlaying) Start();
            else { SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode(); }
        }

        static void Start()
        {
            report = new Report { status = "running" };
            try
            {
                wasPlaying = SessionState.GetBool(Pending + ".WasPlaying", false);
                wasBackground = Application.runInBackground; Application.runInBackground = true;
                savedLock = Cursor.lockState; savedCursor = Cursor.visible;
                captureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60f;
                Application.logMessageReceived += Log;
                previous = SceneManager.GetActiveScene(); hidden.Clear();
                for (int i = 0; i < SceneManager.sceneCount; i++) foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
                    if (root.activeSelf) { hidden.Add(root); root.SetActive(false); }
                review = SceneManager.CreateScene("Temporary weapon and feet review"); SceneManager.SetActiveScene(review);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Review ground";
                floor.transform.position = new Vector3(0, -.25f, 0); floor.transform.localScale = new Vector3(200, .5f, 200);
                groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); groundMaterial.color = new Color(.15f, .18f, .21f);
                floor.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Bullet receiver";
                wall.transform.position = new Vector3(0, 5f, 65f); wall.transform.localScale = new Vector3(160, 10, .3f);
                var light = new GameObject("Review light").AddComponent<Light>(); light.type = LightType.Directional;
                light.intensity = 1.6f; light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var container = new GameObject("Review player container"); container.SetActive(false);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab");
                var rootPlayer = Object.Instantiate(prefab, container.transform); rootPlayer.transform.localScale = Vector3.one * 2f;
                rootPlayer.transform.localPosition = new Vector3(0, .04f, 0); rootPlayer.transform.localRotation = Quaternion.identity;
                player = rootPlayer.GetComponent<ItfaiyeFirstPersonController>(); player.externalControl = true; player.recoverFromFall = false;
                player.walkSpeed = 4f; player.runSpeed = 6f;
                pistol = player.pistol; pistol.externalControl = true; pistol.showHud = false;
                container.SetActive(true);
                foreach (var r in player.bodyAnimator.GetComponentsInChildren<SkinnedMeshRenderer>())
                    if (r.enabled) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                player.playerCamera.clearFlags = CameraClearFlags.SolidColor; player.playerCamera.backgroundColor = new Color(.08f, .10f, .13f);
                driver = container.AddComponent<ItfaiyeWeaponReviewDriver>(); driver.player = player; driver.pistol = pistol;
                var cameraObject = new GameObject("Review body camera"); bodyCamera = cameraObject.AddComponent<Camera>();
                bodyCamera.enabled = false; bodyCamera.clearFlags = CameraClearFlags.SolidColor; bodyCamera.backgroundColor = new Color(.13f, .16f, .2f);
                bodyCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("FirstPersonHands")); bodyCamera.fieldOfView = 45f;
                pistol.Equip(); initialAmmo = pistol.Ammo; maxGrip = maxYaw = maxFootDelta = 0f;
                capturedShot = queued = false; requestedShots = 0; lastFrame = -1; began = Time.time;
                Next(0);
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        static void Update()
        {
            if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
        }

        static void Tick()
        {
            float age = Time.time - phaseAt;
            if (Time.time - began > 45f) throw new TimeoutException("Phase " + phase);
            if (pistol.DrawAmount > .99f && !pistol.IsReloading) MeasureGrip();
            if (phase == 0)
            {
                if (age < 1f) return;
                Add("Scaled player equips and both wrists reach the weapon", pistol.Ready && maxGrip < .006f, maxGrip.ToString("F5"));
                CaptureView("01-ready"); driver.move = Vector2.up; Next(1);
            }
            else if (phase == 1)
            {
                if (age < .7f) return;
                if (!queued) {
                    Add("Gameplay walk speed selects the authored walk", Mathf.Abs(player.bodyAnimator.GetFloat("MoveY") - 1.87f) < .08f);
                    shotRequested = Time.time; queued = pistol.Fire(); return;
                }
                if (pistol.ShotsFired == 0) return;
                shotDelay = Time.time - shotRequested;
                Add("Walking shot fires within 80 ms", shotDelay <= .081f, shotDelay.ToString("F3"));
                Add("Shot is accepted by the physical barrel", pistol.LastShotCollider && pistol.LastShotCollider.name == "Bullet receiver");
                driver.sprint = true; queued = false; Next(2);
            }
            else if (phase == 2)
            {
                if (age > .7f) MeasureFeet();
                if (age < 2f) return;
                Add("Both running ankles face forward without inversion", maxYaw < 35f, maxYaw.ToString("F2"));
                Add("Left ankle stays continuous through running cycles", maxFootDelta < 30f, maxFootDelta.ToString("F2"));
                Add("Sprint blend uses authored run threshold", Mathf.Abs(player.bodyAnimator.GetFloat("MoveY") - 4.57f) < .08f);
                CaptureBody("02-running-feet"); CaptureView("03-sprint");
                shotRequested = Time.time; queued = pistol.Fire(); Next(3);
            }
            else if (phase == 3)
            {
                if (pistol.ShotsFired < 2) return;
                if (pistol.ShotAge < .025f) return;
                float latency = Time.time - shotRequested - pistol.ShotAge;
                Add("Sprint raise fires within 140 ms", latency <= .141f, latency.ToString("F3"));
                Add("Sprint shot preserves movement speed", player.CurrentSpeed > 5.8f);
                CaptureView("04-recoil"); driver.move = Vector2.zero; driver.sprint = false; Next(4);
            }
            else if (phase == 4)
            {
                if (age < .7f) return;
                Add("Recoil returns to rest without lingering drift", pistol.Recoil < .003f);
                pistol.Fire(); requestedShots = pistol.ShotsFired; queued = false; Next(5);
            }
            else if (phase == 5)
            {
                if (!queued && age >= .11f)
                { queued = pistol.Fire(); Add("Early semiautomatic click is buffered", queued && pistol.SprintShotQueued); return; }
                if (age < .45f) return;
                Add("Buffered click fires exactly once", pistol.ShotsFired == requestedShots + 1);
                driver.aim = true; Next(6);
            }
            else if (phase == 6)
            {
                if (age < .8f) return;
                Add("ADS aligns the sights with camera projection", pistol.AimAmount > .99f && Mathf.Abs(player.playerCamera.fieldOfView - player.handsCamera.fieldOfView) < .01f);
                CaptureView("05-aim"); driver.aim = false;
                Add("Physical reload starts", pistol.Reload()); Next(7);
            }
            else if (phase == 7)
            {
                if (pistol.IsReloading) { if (age > .8f && !capturedShot) { CaptureView("06-reload"); capturedShot = true; } return; }
                Add("Reload transfers exactly the ammunition spent", pistol.Ammo == initialAmmo && pistol.reserveAmmo == 48 - pistol.ShotsFired);
                driver.move = new Vector2(1, 1).normalized; driver.sprint = true; Next(8);
            }
            else if (phase == 8)
            {
                if (age < .4f) return;
                MeasureFeet();
                if (age < 2f) return;
                Add("Diagonal run has stable foot orientation", maxYaw < 35f);
                Add("Both hands stay on the grips at double scale", maxGrip < .006f, maxGrip.ToString("F5"));
                CaptureBody("07-diagonal-feet");
                queued = pistol.Fire(); pistol.SetExternalInput(false, false, false, false, false, false);
                Add("Losing input cancels the queued shot", !pistol.SprintShotQueued);
                driver.move = Vector2.zero; pistol.SetAvailable(false); Next(9);
            }
            else if (phase == 9)
            {
                if (age < .3f) return;
                Add("Tool switching clears recoil and hides the weapon", !pistol.Equipped && pistol.Recoil == 0f && !pistol.weapon.activeSelf);
                Add("No runtime exceptions", report.errors.Count == 0);
                Finish(null);
            }
        }

        static void MeasureGrip()
        {
            var weapon = pistol.weapon.transform; float scale = player.playerCamera.transform.lossyScale.x;
            foreach (bool left in new[] { true, false })
            {
                var hand = player.handsAnimator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var expected = weapon.position + weapon.rotation * ((left ? ItfaiyeFirstPersonHands.LeftGrip : ItfaiyeFirstPersonHands.RightGrip) * scale);
                maxGrip = Mathf.Max(maxGrip, Vector3.Distance(hand.position, expected));
            }
        }
        static void MeasureFeet()
        {
            foreach (bool left in new[] { true, false })
            {
                var foot = player.bodyAnimator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                var toe = player.bodyAnimator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                var forward = player.transform.InverseTransformDirection(toe.position - foot.position);
                maxYaw = Mathf.Max(maxYaw, Mathf.Abs(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg));
                if (left) { if (footSampled) maxFootDelta = Mathf.Max(maxFootDelta, Quaternion.Angle(lastLeftFoot, foot.localRotation)); lastLeftFoot = foot.localRotation; footSampled = true; }
            }
        }
        static void CaptureView(string name) => ItfaiyeWeaponFeelReview.Capture(player.playerCamera, Folder + "/" + name + ".png");
        static void CaptureBody(string name)
        {
            var center = player.transform.position + Vector3.up * 1.5f;
            bodyCamera.transform.position = center + new Vector3(3.5f, 1.2f, 7.5f); bodyCamera.transform.LookAt(center);
            ItfaiyeWeaponFeelReview.Capture(bodyCamera, Folder + "/" + name + ".png");
        }
        static void Next(int value) { phase = value; phaseAt = Time.time; footSampled = false; Save(); }
        static void Add(string name, bool passed, string evidence = "") { report.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); }
        static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message); }
        static void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/runtime-report.json", JsonUtility.ToJson(report, true)); }
        static void Finish(string error)
        {
            phase = -1;
            if (report == null) return;
            report.error = error;
            report.status = error == null && report.errors.Count == 0 && report.checks.TrueForAll(c => c.passed) ? "SUCCESS" : "FAILED";
            Save(); Application.logMessageReceived -= Log;
            if (driver) driver.enabled = false;
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (review.IsValid() && review.isLoaded) SceneManager.UnloadSceneAsync(review);
            foreach (var root in hidden) if (root) root.SetActive(true); hidden.Clear();
            if (groundMaterial) Object.Destroy(groundMaterial);
            Time.captureDeltaTime = captureDelta; Application.runInBackground = wasBackground;
            Cursor.lockState = savedLock; Cursor.visible = savedCursor;
            if (!wasPlaying && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
        }
    }

    [DefaultExecutionOrder(-95)]
    public sealed class ItfaiyeWeaponReviewDriver : MonoBehaviour
    {
        public ItfaiyeFirstPersonController player;
        public ItfaiyePistol pistol;
        public Vector2 move;
        public bool sprint, aim;
        void Update()
        {
            player.SimulateExternalMovement(move, sprint, false, false, true, 0f, Vector2.zero);
            pistol.SetExternalInput(true, aim, false, false, false, false);
        }
        void LateUpdate() { player.ApplyExternalView(); }
    }
}
