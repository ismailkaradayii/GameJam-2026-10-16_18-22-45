using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ResidentPaceChecks
{
    const string Pending = "ResidentPaceChecks.Pending";
    public const string Folder = "Logs/ResidentNaturalPace";
    static double nextPoll;
    static ResidentPaceChecks()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
        };
    }
    static void Poll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string request = "Logs/ResidentPace.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        if (command == "checks") Begin();
        else if (command == "refresh") AssetDatabase.Refresh();
    }
    [MenuItem("GameJam/Final Loop/Check natural NPC pace")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        var residents = Object.FindFirstObjectByType<EvacuationMission>().residents.ToArray();
        foreach (var resident in residents) { resident.enabled = false; resident.Agent.enabled = false; }
        for (int s = 0; s < SceneManager.sceneCount; s++)
            foreach (var root in SceneManager.GetSceneAt(s).GetRootGameObjects()) root.SetActive(false);
        var scene = SceneManager.CreateScene("Temporary real NPC pace checks"); SceneManager.SetActiveScene(scene);
        var rootObject = new GameObject("Natural NPC pace probe");
        rootObject.AddComponent<ResidentPaceProbe>().Begin(residents, scene);
    }
}

[DefaultExecutionOrder(10000)]
public sealed class ResidentPaceProbe : MonoBehaviour
{
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Sample
    {
        public string resident, gait;
        public int fps;
        public float elapsed, distance, meanSpeed, targetSpeed, cyclesPerSecond, minimumPlayback = 10, minimumFootForward = 1;
        public float plantedFootSpeed, stanceSamples;
    }
    [Serializable] public class Report
    {
        public string status = "RUNNING";
        public List<Sample> samples = new List<Sample>();
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    readonly Report report = new Report();
    EvacuationResident[] residents;
    ResidentLocomotion[] motions;
    Vector3[] previous, previousLeft, previousRight;
    float[] previousCycle;
    Sample[] samples;
    NavMeshDataInstance navigation;
    NavMeshData data;
    Camera review;
    int phase;
    float phaseAt, oldCapture, oldScale;
    bool background, finished;
    double deadline;

    public void Begin(EvacuationResident[] actors, Scene scene)
    {
        oldCapture = Time.captureDeltaTime; oldScale = Time.timeScale; background = Application.runInBackground;
        Time.timeScale = 1; Time.captureDeltaTime = 1f / 60; Application.runInBackground = true;
        Application.logMessageReceived += Log; deadline = EditorApplication.timeSinceStartup + 150;
        try
        {
            residents = actors;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "NPC pace test ground";
            floor.transform.position = new Vector3(0, -.15f, 0); floor.transform.localScale = new Vector3(110, .3f, 600);
            data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), new List<NavMeshBuildSource> {
                new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(100, .3f, 580),
                    transform = Matrix4x4.TRS(new Vector3(0, -.15f, 0), Quaternion.identity, Vector3.one) }
            }, new Bounds(Vector3.zero, new Vector3(110, 30, 600)), Vector3.zero, Quaternion.identity);
            navigation = NavMesh.AddNavMeshData(data);
            motions = new ResidentLocomotion[actors.Length]; previous = new Vector3[actors.Length];
            previousLeft = new Vector3[actors.Length]; previousRight = new Vector3[actors.Length]; previousCycle = new float[actors.Length];
            for (int i = 0; i < actors.Length; i++)
            {
                var resident = actors[i]; resident.transform.SetParent(null, true); SceneManager.MoveGameObjectToScene(resident.gameObject, scene);
                resident.transform.SetPositionAndRotation(new Vector3(-24 + i * 8, .1f, -200), Quaternion.identity);
                motions[i] = resident.animator.GetComponent<ResidentLocomotion>();
                resident.animator.SetBool("Panic", false); resident.animator.SetBool("ShelterRest", false);
                resident.animator.Play("Locomotion", 0, 0);
                foreach (var lod in resident.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
                foreach (var renderer in resident.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
                resident.Agent.enabled = true; resident.Agent.Warp(resident.transform.position);
                resident.Agent.autoBraking = false; resident.Agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            }
            var light = new GameObject("NPC pace review sunlight").AddComponent<Light>(); light.type = LightType.Directional;
            light.intensity = 2; light.transform.rotation = Quaternion.Euler(40, -30, 0);
            review = new GameObject("Moving NPC review camera").AddComponent<Camera>(); review.enabled = false;
            review.orthographic = true; review.orthographicSize = 4;
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    void Update()
    {
        if (finished || residents == null) return;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Pace check timed out"); return; }
            for (int i = 0; i < residents.Length; i++)
            {
                var resident = residents[i];
                float speed = EvacuationResident.FlatDistance(resident.transform.position, previous[i]) / Time.deltaTime;
                motions[i].SetMotion(speed, Time.deltaTime);
                if (Time.time - phaseAt > .75f)
                {
                    samples[i].elapsed += Time.deltaTime;
                    samples[i].distance += EvacuationResident.FlatDistance(resident.transform.position, previous[i]);
                }
                previous[i] = resident.transform.position;
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    void LateUpdate()
    {
        if (finished || residents == null) return;
        try
        {
            bool sampling = Time.time - phaseAt > .75f;
            for (int i = 0; i < residents.Length; i++)
            {
                var resident = residents[i]; var animator = resident.animator; var sample = samples[i];
                float cycle = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                if (sampling)
                {
                    sample.cyclesPerSecond += Mathf.Max(0, cycle - previousCycle[i]);
                    sample.minimumPlayback = Mathf.Min(sample.minimumPlayback, animator.GetFloat("StrideRate"));
                }
                previousCycle[i] = cycle;
                foreach (bool left in new[] { true, false })
                {
                    Vector3 foot = animator.GetBoneTransform(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot).position;
                    Vector3 toe = animator.GetBoneTransform(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes).position;
                    Vector3 previousFoot = left ? previousLeft[i] : previousRight[i];
                    if (sampling)
                    {
                        sample.minimumFootForward = Mathf.Min(sample.minimumFootForward, Vector3.Dot((toe - foot).normalized, resident.transform.forward));
                        float localHeight = animator.transform.InverseTransformPoint(foot).y;
                        if (localHeight < .2f && Mathf.Abs(foot.y - previousFoot.y) / Time.deltaTime < 1f && phase % 4 < 2)
                        {
                            sample.plantedFootSpeed += EvacuationResident.FlatDistance(foot, previousFoot) / Time.deltaTime;
                            sample.stanceSamples++;
                        }
                    }
                    if (left) previousLeft[i] = foot; else previousRight[i] = foot;
                }
            }
            if (Time.time - phaseAt < 2.4f) return;
            foreach (var sample in samples)
            {
                sample.meanSpeed = sample.distance / Mathf.Max(.001f, sample.elapsed);
                sample.cyclesPerSecond /= Mathf.Max(.001f, sample.elapsed);
                sample.plantedFootSpeed /= Mathf.Max(1, sample.stanceSamples);
                string name = sample.resident + " " + sample.gait + " at " + sample.fps + " FPS";
                Add(name + " covers the distance its gait calls for", Mathf.Abs(sample.meanSpeed - sample.targetSpeed) < Mathf.Max(.1f, sample.targetSpeed * .04f),
                    "speed=" + sample.meanSpeed + ", target=" + sample.targetSpeed + ", distance=" + sample.distance);
                Add(name + " retains a natural animation tempo", sample.minimumPlayback >= .99f &&
                    (phase % 4 == 3 || sample.cyclesPerSecond > (phase % 4 == 0 ? .85f : 1.3f)),
                    "minimum playback=" + sample.minimumPlayback + ", cycles/second=" + sample.cyclesPerSecond);
                Add(name + " keeps the boots facing forward", sample.minimumFootForward > .25f, "minimum forward=" + sample.minimumFootForward);
                report.samples.Add(sample);
            }
            if (phase == 0 || phase == 1)
            {
                Vector3 centre = residents[0].transform.position + Vector3.up * 3;
                review.transform.position = centre + new Vector3(8, 2, 13); review.transform.LookAt(centre);
                Itfaiye.Editor.ItfaiyeWeaponFeelReview.Capture(review, ResidentPaceChecks.Folder + (phase == 0 ? "/walk.png" : "/run.png"));
            }
            if (phase < 7) Next(phase + 1); else Finish(null);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    void Next(int value)
    {
        phase = value; phaseAt = Time.time; Time.captureDeltaTime = phase < 4 ? 1f / 60 : 1f / 12;
        samples = new Sample[residents.Length];
        for (int i = 0; i < residents.Length; i++)
        {
            var resident = residents[i]; float speed = phase % 4 == 0 ? resident.walkingSpeed : phase % 4 == 1 ? resident.runningSpeed : phase % 4 == 2 ? resident.catchUpSpeed : 0;
            resident.Agent.speed = speed; resident.Agent.isStopped = speed == 0;
            if (speed > 0) resident.Agent.SetDestination(new Vector3(resident.transform.position.x, 0, 260));
            previous[i] = resident.transform.position;
            samples[i] = new Sample { resident = resident.name, gait = new[] { "walk", "run", "catchup", "stop" }[phase % 4], fps = phase < 4 ? 60 : 12, targetSpeed = speed };
        }
        Save();
    }
    void Add(string name, bool passed, string evidence) => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(text); }
    void Save() => File.WriteAllText(ResidentPaceChecks.Folder + "/checks.json", JsonUtility.ToJson(report, true));
    void Finish(string error)
    {
        if (finished) return; finished = true;
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        Save(); Application.logMessageReceived -= Log;
        if (navigation.valid) navigation.Remove(); if (data) Object.Destroy(data);
        Time.captureDeltaTime = oldCapture; Time.timeScale = oldScale; Application.runInBackground = background;
        EditorApplication.ExitPlaymode();
    }
    void OnDisable() { if (!finished) Finish("Pace check interrupted"); }
}
