using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using Itfaiye;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ResidentLocomotionChecks
{
    const string Pending = "ResidentLocomotionChecks.Pending";
    static ResidentLocomotionChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
        };
    }
    [MenuItem("GameJam/Final Loop/Check resident stride and player jump")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(ResidentLocomotionSetup.Folder);
        File.WriteAllText(ResidentLocomotionSetup.Folder + "/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var sources = mission.residents.ToArray();
        float walk = mission.player.firstPerson.walkSpeed, run = mission.player.firstPerson.runSpeed;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects()) root.SetActive(false);
        SceneManager.SetActiveScene(SceneManager.CreateScene("Temporary resident and jump checks"));
        var rootObject = new GameObject("Resident and jump probe");
        rootObject.AddComponent<ResidentJumpProbe>().Begin(sources, walk, run);
    }
}

[DefaultExecutionOrder(10000)]
public sealed class ResidentJumpProbe : MonoBehaviour
{
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Gait
    {
        public string resident;
        public float minForward = 1, minToeHeight = 100, maxRootDrift;
        public Vector3 hipMin = Vector3.one * 100, hipMax = Vector3.one * -100;
        public float walkingRate, runningRate, catchUpRate;
    }
    [Serializable] public class Report
    {
        public string status = "RUNNING";
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
        public List<Gait> gaits = new List<Gait>();
    }
    readonly Report report = new Report();
    readonly List<ResidentLocomotion> motions = new List<ResidentLocomotion>();
    readonly List<Vector3> roots = new List<Vector3>();
    ItfaiyeFirstPersonController player;
    CharacterController motor;
    Camera reviewCamera;
    GameObject ceiling;
    float phaseAt, baseHeight, baseEye, apex, eyeApex, oldCapture;
    int phase;
    bool jumped, airborne, finished, background;
    double deadline;

    public void Begin(EvacuationResident[] sources, float walk, float run)
    {
        oldCapture = Time.captureDeltaTime; background = Application.runInBackground;
        Application.runInBackground = true; Time.timeScale = 1; Time.captureDeltaTime = 1f / 60;
        Application.logMessageReceived += Log; deadline = EditorApplication.timeSinceStartup + 150;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Flat validation floor";
        floor.transform.position = new Vector3(0, -.5f, 20); floor.transform.localScale = new Vector3(150, 1, 200);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab");
        player = Object.Instantiate(prefab).GetComponent<ItfaiyeFirstPersonController>();
        player.externalControl = true; player.enabled = false; player.recoverFromFall = false;
        player.walkSpeed = walk; player.runSpeed = run; player.transform.localScale = Vector3.one * 3.3f;
        motor = player.GetComponent<CharacterController>();
        if (player.pistol) player.pistol.enabled = false;
        foreach (var source in sources)
        {
            var model = Object.Instantiate(source.animator.gameObject);
            model.transform.SetPositionAndRotation(new Vector3(-22 + motions.Count * 7, 0, 14), Quaternion.identity);
            model.transform.localScale = Vector3.one * 3.3f;
            foreach (var lod in model.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
            foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
            var motion = model.GetComponent<ResidentLocomotion>(); motion.resident = source;
            motions.Add(motion); roots.Add(model.transform.position);
            report.gaits.Add(new Gait { resident = source.name });
        }
        var light = new GameObject("Review sunlight").AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 2; light.transform.rotation = Quaternion.Euler(40, -30, 0);
        reviewCamera = new GameObject("Stride review camera").AddComponent<Camera>(); reviewCamera.enabled = false;
        reviewCamera.orthographic = true; reviewCamera.orthographicSize = 4;
        reviewCamera.clearFlags = CameraClearFlags.SolidColor; reviewCamera.backgroundColor = new Color(.16f, .19f, .23f);
        reviewCamera.transform.position = roots[0] + new Vector3(7.5f, 4.8f, 12);
        reviewCamera.transform.LookAt(roots[0] + Vector3.up * 3.1f);
        Add("Saved player and prefab use a visible 2.4 metre jump", Mathf.Abs(player.jumpHeight - 2.4f) < .001f, "prefab height=" + player.jumpHeight);
        Next(0);
    }

    void Update()
    {
        if (finished || !player) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Review timed out"); return; }
            float age = Time.time - phaseAt;
            foreach (var motion in motions)
                motion.SetMotion(phase <= 1 ? motion.resident.walkingSpeed : phase < 5 ? motion.resident.runningSpeed : motion.resident.catchUpSpeed, Time.deltaTime);
            bool moving = phase == 2 || phase == 4;
            bool requestJump = phase > 0 && !jumped && age > .2f && motor.isGrounded;
            if (requestJump) { jumped = true; baseHeight = player.transform.position.y; baseEye = player.playerCamera.transform.position.y; }
            player.SimulateExternalMovement(moving ? Vector2.up : Vector2.zero, moving, requestJump, false, true, 0, Vector2.zero);
            player.ApplyExternalView();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    void LateUpdate()
    {
        if (finished || !player) return;
        try
        {
            float age = Time.time - phaseAt;
            if (age > .3f)
            for (int i = 0; i < motions.Count; i++)
            {
                var animator = motions[i].GetComponent<Animator>(); var gait = report.gaits[i];
                if (phase <= 1) gait.walkingRate = animator.GetFloat("StrideRate");
                else if (phase < 5) gait.runningRate = animator.GetFloat("StrideRate");
                else gait.catchUpRate = animator.GetFloat("StrideRate");
                Vector3 hip = animator.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position);
                gait.hipMin = Vector3.Min(gait.hipMin, hip); gait.hipMax = Vector3.Max(gait.hipMax, hip);
                gait.maxRootDrift = Mathf.Max(gait.maxRootDrift, Vector3.Distance(roots[i], animator.transform.position));
                foreach (var pair in new[] { new[] { HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes }, new[] { HumanBodyBones.RightFoot, HumanBodyBones.RightToes } })
                {
                    Vector3 foot = animator.transform.InverseTransformPoint(animator.GetBoneTransform(pair[0]).position);
                    Vector3 toe = animator.transform.InverseTransformPoint(animator.GetBoneTransform(pair[1]).position);
                    gait.minForward = Mathf.Min(gait.minForward, (toe - foot).normalized.z);
                    gait.minToeHeight = Mathf.Min(gait.minToeHeight, toe.y);
                }
            }
            if (phase == 0)
            {
                if (age > .7f) Next(1);
                return;
            }
            if (jumped)
            {
                apex = Mathf.Max(apex, player.transform.position.y - baseHeight);
                eyeApex = Mathf.Max(eyeApex, player.playerCamera.transform.position.y - baseEye);
                if (!player.IsGrounded && apex > .15f) airborne = true;
            }
            if (age < 1.75f || !jumped) return;
            string label = phase == 1 ? "Standing at 60 FPS" : phase == 2 ? "Sprinting at 60 FPS" : phase == 3 ? "Standing at 30 FPS" : phase == 4 ? "Sprinting at 12 FPS" : "Under a low ceiling";
            bool blocked = phase == 5;
            Add(label + " jump reaches the intended height", blocked ? apex > .4f && apex < 1.3f : Mathf.Abs(apex - 2.4f) < .12f,
                "body rise=" + apex + " m; eye rise=" + eyeApex + " m");
            Add(label + " lands and keeps the camera with the body", airborne && motor.isGrounded && Mathf.Abs(eyeApex - apex) < .12f,
                "airborne=" + airborne + ", landed=" + motor.isGrounded);
            if (phase == 1 || phase == 2) Itfaiye.Editor.ItfaiyeWeaponFeelReview.Capture(reviewCamera, ResidentLocomotionSetup.Folder + (phase == 1 ? "/runtime-walk.png" : "/runtime-run.png"));
            if (phase < 5) { Next(phase + 1); return; }
            for (int i = 0; i < report.gaits.Count; i++)
            {
                var gait = report.gaits[i]; Vector3 range = gait.hipMax - gait.hipMin;
                Add(gait.resident + " keeps both boots facing forward", gait.minForward > .25f, "minimum toe forward=" + gait.minForward);
                Add(gait.resident + " stays over its navigation root", gait.maxRootDrift < .001f && range.x < .16f && range.z < .6f,
                    "root drift=" + gait.maxRootDrift + ", hip range=" + range);
                Add(gait.resident + " uses a faster stride to catch up", gait.walkingRate > .35f && gait.runningRate > .35f && gait.catchUpRate > gait.runningRate * 1.4f,
                    "walk=" + gait.walkingRate + ", run=" + gait.runningRate + ", catchup=" + gait.catchUpRate);
            }
            Finish(null);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    void Next(int value)
    {
        phase = value; phaseAt = Time.time; jumped = airborne = false; apex = eyeApex = 0;
        Time.captureDeltaTime = phase == 3 ? 1f / 30 : phase == 4 ? 1f / 12 : 1f / 60;
        player.SuspendExternalMovement(); motor.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(0, .04f, -20), Quaternion.identity); motor.enabled = true;
        if (phase == 5)
        {
            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube); ceiling.name = "Low jump ceiling";
            ceiling.transform.position = new Vector3(0, motor.height * 3.3f + 1.4f, -20);
            ceiling.transform.localScale = new Vector3(8, .5f, 8);
        }
        Physics.SyncTransforms(); Save();
    }
    void Add(string title, bool passed, string evidence) => report.checks.Add(new Check { name = title, passed = passed, evidence = evidence });
    void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(text); }
    void Save() => File.WriteAllText(ResidentLocomotionSetup.Folder + "/checks.json", JsonUtility.ToJson(report, true));
    void Finish(string error)
    {
        if (finished) return; finished = true;
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log; Time.captureDeltaTime = oldCapture; Application.runInBackground = background;
        EditorApplication.ExitPlaymode();
    }
    void OnDisable() { if (!finished) Finish("Review interrupted"); }
}
