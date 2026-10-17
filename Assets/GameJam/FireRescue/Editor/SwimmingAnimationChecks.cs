using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameJam.FireRescue;
using Itfaiye;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SwimmingAnimationChecks
{
    const string Folder = "Logs/SwimmingAnimationReview";
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report { public string status = "running", error; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    struct Edge { public Vector2 direction; public float scale; public string name; public bool stationary; }
    static Report report;
    static readonly List<GameObject> hidden = new List<GameObject>();
    static readonly List<Edge> edges = new List<Edge>();
    static Scene previous, practice;
    static ItfaiyeFirstPersonController player;
    static CharacterController motor;
    static WaterSourceVolume water;
    static AuthoredSwimmingReviewDriver driver;
    static int phase = -1, lastFrame, edgeIndex, previousClimbs, samples, visible;
    static int strokesBeforeMoving;
    static float phaseAt, began, originalCapture, originalScale;
    static Vector3 minimum, maximum;
    static float maxPoseDifference, maxHipDistance, maxArmLengthError;
    static bool background, wasPlaying, capturedMoving;
    static GameObject obstruction;
    static double wallStarted;

    static SwimmingAnimationChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("AuthoredSwimming.Pending", false))
            { SessionState.SetBool("AuthoredSwimming.Pending", false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Play Mode interrupted");
        };
    }
    public static void Begin()
    {
        if (phase >= 0) return;
        wasPlaying = EditorApplication.isPlaying;
        if (wasPlaying) Start(); else { SessionState.SetBool("AuthoredSwimming.Pending", true); EditorApplication.EnterPlaymode(); }
    }
    static void Start()
    {
        report = new Report(); Directory.CreateDirectory(Folder);
        try
        {
            originalCapture = Time.captureDeltaTime; background = Application.runInBackground;
            Time.captureDeltaTime = 1f / 60; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            previous = SceneManager.GetActiveScene(); hidden.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++) foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects())
                if (root.activeSelf) { hidden.Add(root); root.SetActive(false); }
            practice = EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/İtfaiye aracı.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            wallStarted = EditorApplication.timeSinceStartup; phase = -2; Save();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void InitializePractice()
    {
        try
        {
            SceneManager.SetActiveScene(practice);
            var coordinator = practice.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FireRescuePlayer>()).Single();
            coordinator.SetDriving(false); coordinator.enabled = false;
            player = coordinator.firstPerson; motor = coordinator.playerBody; motor.enabled = true;
            player.externalControl = true; player.recoverFromFall = false;
            player.pistol.externalControl = true; player.pistol.showHud = false;
            // Visibility samples and 960x720 captures must use the same projection.
            player.playerCamera.aspect = 4f / 3f;
            if (player.handsCamera) player.handsCamera.aspect = 4f / 3f;
            originalScale = player.transform.localScale.y;
            water = practice.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WaterSourceVolume>()).Single();
            var go = new GameObject("Authored swimming review driver"); driver = go.AddComponent<AuthoredSwimmingReviewDriver>(); driver.player = player;
            foreach (var c in practice.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()))
                if (c != player.playerCamera && c != player.handsCamera) c.enabled = false;
            foreach (var listener in practice.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AudioListener>()))
                listener.enabled = listener.transform == player.playerCamera.transform;
            var idle = SwimmingAnimationSetup.Clip(SwimmingAnimationSetup.IdlePath); var swim = SwimmingAnimationSetup.Clip(SwimmingAnimationSetup.SwimPath);
            Add("Both supplied FBX clips are valid looping humanoid animations", idle.humanMotion && idle.isLooping && swim.humanMotion && swim.isLooping);
            Add("Body and first-person hands support authored swimming", player.Swimming.UsesAuthoredAnimations);
            Add("Authored clips cannot move the gameplay root", !player.bodyAnimator.applyRootMotion && !player.handsAnimator.applyRootMotion);
            motor.enabled = false; player.transform.localScale = Vector3.one;
            player.transform.position = new Vector3(water.transform.position.x, water.SurfaceY + 1, water.transform.position.z);
            player.transform.rotation = Quaternion.identity; motor.enabled = true; Physics.SyncTransforms();
            edges.Clear();
            var directions = new[] { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            foreach (float scale in new[] { 1f, 2f, 3.4f }) for (int i = 0; i < directions.Length; i++)
                edges.Add(new Edge { direction = directions[i], scale = scale, name = "side " + i + " scale " + scale });
            foreach (float scale in new[] { 1f, 2f }) foreach (var d in new[] { new Vector2(1, 1), new Vector2(1, -1), new Vector2(-1, -1), new Vector2(-1, 1) })
                edges.Add(new Edge { direction = d.normalized, scale = scale, name = "corner " + d + " scale " + scale });
            edges.Add(new Edge { direction = Vector2.up, scale = 1, stationary = true, name = "Space alone while facing the edge" });
            began = Time.time; wallStarted = EditorApplication.timeSinceStartup; lastFrame = -1; Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if (phase == -2 && EditorApplication.isPlaying)
        {
            if (practice.IsValid() && practice.isLoaded) InitializePractice();
            else if (EditorApplication.timeSinceStartup - wallStarted > 45) Finish("Practice pool load timed out");
            return;
        }
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount;
        try { Tick(); } catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        if (Time.time - began > 85 || EditorApplication.timeSinceStartup - wallStarted > 300) throw new TimeoutException("Swimming phase " + phase);
        float age = Time.time - phaseAt;
        if (phase == 0)
        {
            if (age < 2.4f) return;
            Add("Actual saved pool entry starts swimming", player.IsSwimming && player.handsCamera.enabled);
            Add("Idle clip selected on body and hands", Weight(player.bodyAnimator, SwimmingAnimationSetup.IdlePath) > .95f && Weight(player.handsAnimator, SwimmingAnimationSetup.IdlePath) > .95f);
            minimum = Vector3.one * float.PositiveInfinity; maximum = Vector3.one * float.NegativeInfinity; samples = visible = 0; Next(1);
        }
        else if (phase == 1)
        {
            SamplePose();
            if (age < 3.2f) return;
            Add("Supplied idle animation moves the hands", (maximum - minimum).magnitude > .025f, (maximum - minimum).ToString());
            Add("Idle hands stay visible", samples > 30 && visible >= samples * .8f, visible + "/" + samples);
            Add("Idle paddling splashes from the animated hands", player.Swimming.HandStrokeCount > 0 && water.Effects.HandSplashes > 0,
                "hand strokes=" + player.Swimming.HandStrokeCount + "; splashes=" + water.Effects.HandSplashes);
            strokesBeforeMoving = player.Swimming.HandStrokeCount;
            Capture("01-authored-swim-idle");
            driver.move = Vector2.up; driver.turnRate = 60; maxPoseDifference = maxHipDistance = maxArmLengthError = 0; samples = visible = 0; capturedMoving = false; Next(2);
        }
        else if (phase == 2)
        {
            if (age > .6f)
            {
                SamplePose();
                var hip = player.transform.InverseTransformPoint(player.bodyAnimator.GetBoneTransform(HumanBodyBones.Hips).position);
                maxHipDistance = Mathf.Max(maxHipDistance, new Vector2(hip.x, hip.z).magnitude);
                foreach (bool left in new[] { true, false })
                {
                    var upper = left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                    var hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                    Vector3 bodyArm = player.transform.InverseTransformVector(player.bodyAnimator.GetBoneTransform(hand).position - player.bodyAnimator.GetBoneTransform(upper).position);
                    Vector3 firstPersonArm = player.playerCamera.transform.InverseTransformVector(player.handsAnimator.GetBoneTransform(hand).position - player.handsAnimator.GetBoneTransform(upper).position);
                    maxArmLengthError = Mathf.Max(maxArmLengthError, Mathf.Abs(bodyArm.magnitude - firstPersonArm.magnitude));
                    foreach (var bone in left ? new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal } :
                        new[] { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal })
                        maxPoseDifference = Mathf.Max(maxPoseDifference, Quaternion.Angle(player.bodyAnimator.GetBoneTransform(bone).localRotation, player.handsAnimator.GetBoneTransform(bone).localRotation));
                }
                if (!capturedMoving && age > 1.1f && HandsVisible()) { Capture("02-authored-swimming"); capturedMoving = true; }
            }
            if (age < 5.3f) return;
            Add("Supplied swimming clip selected on body and hands", Weight(player.bodyAnimator, SwimmingAnimationSetup.SwimPath) > .95f && Weight(player.handsAnimator, SwimmingAnimationSetup.SwimPath) > .95f);
            Add("First-person elbows, wrists and fingers preserve the authored clip", maxPoseDifference < .15f && maxArmLengthError < .002f,
                "angle=" + maxPoseDifference.ToString("F4") + "; length=" + maxArmLengthError.ToString("F4"));
            Add("Full swim cycle does not drift the body out of the capsule", maxHipDistance < .75f, maxHipDistance.ToString("F4"));
            Add("Swimming hands remain visible during the stroke", visible >= samples * .35f, visible + "/" + samples);
            Add("Swimming strokes continue to produce hand splashes", player.Swimming.HandStrokeCount >= strokesBeforeMoving + 3,
                "moving strokes=" + (player.Swimming.HandStrokeCount - strokesBeforeMoving));
            driver.move = Vector2.zero; driver.turnRate = 0; Next(3);
        }
        else if (phase == 3)
        {
            if (age < 1f) return;
            Add("Stopping returns to the supplied idle clip", Weight(player.handsAnimator, SwimmingAnimationSetup.IdlePath) > .95f);
            edgeIndex = 0; PrepareEdge(edges[edgeIndex]); Next(4);
        }
        else if (phase == 4)
        {
            if (age < 1.25f) return;
            var edge = edges[edgeIndex];
            bool done = !player.IsSwimming && player.Swimming.CompletedClimbs == previousClimbs + 1 && !water.ContainsSurface(player.transform.position);
            Add("Actual pool exit: " + edge.name, done && motor.height > 1.6f,
                "pos=" + player.transform.position + "; swimming=" + player.IsSwimming + "; completed=" + player.Swimming.CompletedClimbs);
            if (edgeIndex == 0) Capture("03-outside-pool");
            edgeIndex++;
            if (edgeIndex < edges.Count) { PrepareEdge(edges[edgeIndex]); Next(4); return; }
            PrepareEdge(new Edge { direction = Vector2.up, scale = 1 });
            var bounds = water.volume.bounds;
            obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube); obstruction.name = "Blocked pool deck";
            obstruction.transform.position = new Vector3(bounds.center.x, water.SurfaceY + 1.5f, bounds.max.z + 1.3f);
            obstruction.transform.localScale = new Vector3(4, 3, 2); Physics.SyncTransforms(); Next(5);
        }
        else if (phase == 5)
        {
            if (age < .9f) return;
            Add("Occupied deck prevents climbing into a solid obstacle", player.IsSwimming && player.Swimming.CompletedClimbs == previousClimbs && !player.Swimming.IsClimbing,
                "pos=" + player.transform.position + "; swimming=" + player.IsSwimming + "; climbing=" + player.Swimming.IsClimbing + "; count=" + player.Swimming.CompletedClimbs);
            obstruction.transform.position = new Vector3(water.volume.bounds.center.x, water.SurfaceY + 1.25f, water.volume.bounds.max.z + 1.3f);
            obstruction.transform.localScale = new Vector3(4, .2f, 2); Physics.SyncTransforms(); Next(6);
        }
        else if (phase == 6)
        {
            if (age < .9f) return;
            Add("Low headroom keeps the swimmer safely in the pool", player.IsSwimming && player.Swimming.CompletedClimbs == previousClimbs && !player.Swimming.IsClimbing,
                "pos=" + player.transform.position + "; swimming=" + player.IsSwimming + "; climbing=" + player.Swimming.IsClimbing + "; count=" + player.Swimming.CompletedClimbs);
            Object.Destroy(obstruction); obstruction = null;
            PrepareWater(); driver.vertical = 1; driver.move = Vector2.up; Next(7);
        }
        else if (phase == 7)
        {
            if (age < .7f) return;
            Add("Space in the middle of the pool cannot start a distant climb", player.IsSwimming && !player.Swimming.IsClimbing && player.Swimming.CompletedClimbs == previousClimbs);
            Finish(null);
        }
    }
    static void PrepareWater()
    {
        driver.turnRate = 0; driver.move = Vector2.zero; driver.vertical = 0;
        player.Swimming.ResetSwimming(); motor.enabled = false; player.transform.localScale = Vector3.one;
        player.transform.rotation = Quaternion.identity;
        player.transform.position = new Vector3(water.transform.position.x, water.SurfaceY - player.playerCamera.transform.localPosition.y + .24f, water.transform.position.z);
        motor.enabled = true; Physics.SyncTransforms();
        player.SimulateExternalMovement(Vector2.zero, false, false, false, true, 0, Vector2.zero);
        previousClimbs = player.Swimming.CompletedClimbs;
    }
    static void PrepareEdge(Edge edge)
    {
        PrepareWater(); motor.enabled = false;
        player.transform.localScale = Vector3.one * edge.scale;
        Bounds b = water.volume.bounds;
        float inset = motor.radius * edge.scale + .08f * edge.scale;
        Vector3 pos = new Vector3(b.center.x, water.SurfaceY - player.playerCamera.transform.localPosition.y * edge.scale + .24f * edge.scale, b.center.z);
        if (Mathf.Abs(edge.direction.x) > .01f) pos.x += Mathf.Sign(edge.direction.x) * (b.extents.x - inset);
        if (Mathf.Abs(edge.direction.y) > .01f) pos.z += Mathf.Sign(edge.direction.y) * (b.extents.z - inset);
        player.transform.position = pos; motor.enabled = true;
        driver.move = edge.stationary ? Vector2.zero : edge.direction; driver.vertical = 1; Physics.SyncTransforms();
    }
    static float Weight(Animator animator, string path) => animator.GetCurrentAnimatorClipInfo(0).Where(c => AssetDatabase.GetAssetPath(c.clip) == path).Sum(c => c.weight);
    static bool Visible(Vector3 point) { var p = player.playerCamera.WorldToViewportPoint(point); return p.z > .05f && p.x > 0 && p.x < 1 && p.y > 0 && p.y < 1; }
    static bool HandsVisible() => Visible(player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position) || Visible(player.handsAnimator.GetBoneTransform(HumanBodyBones.RightHand).position);
    static void SamplePose()
    {
        Vector3 pos = player.playerCamera.transform.InverseTransformPoint(player.handsAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position);
        minimum = Vector3.Min(minimum, pos); maximum = Vector3.Max(maximum, pos); samples++; if (HandsVisible()) visible++;
    }
    static void Capture(string name) => Itfaiye.Editor.ItfaiyeWeaponFeelReview.Capture(player.playerCamera, Folder + "/" + name + ".png");
    static void Next(int value) { phase = value; phaseAt = Time.time; Save(); }
    static void Add(string name, bool passed, string evidence = "") { report.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Save(); }
    static void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true)); }
    static void Log(string message, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(message + "\n" + trace); }
    static void Finish(string error)
    {
        phase = -1; if (report == null) return;
        report.error = error; report.status = error == null && report.errors.Count == 0 && report.checks.TrueForAll(c => c.passed) ? "passed" : "failed"; Save();
        Application.logMessageReceived -= Log;
        if (driver) { driver.enabled = false; Object.Destroy(driver.gameObject); }
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        if (practice.IsValid() && practice.isLoaded) SceneManager.UnloadSceneAsync(practice);
        foreach (var root in hidden) if (root) root.SetActive(true); hidden.Clear();
        Time.captureDeltaTime = originalCapture; Application.runInBackground = background;
        if (!wasPlaying && EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(-95)]
public sealed class AuthoredSwimmingReviewDriver : MonoBehaviour
{
    public ItfaiyeFirstPersonController player;
    public Vector2 move;
    public float vertical, turnRate;
    void Update()
    {
        if (turnRate != 0) player.transform.Rotate(0, turnRate * Time.deltaTime, 0);
        player.SimulateExternalMovement(move, false, false, false, true, 0, Vector2.zero, null, vertical);
        player.pistol.SetExternalInput(false, false, false, false, false, false);
    }
    void LateUpdate() { player.ApplyExternalView(); }
}
