using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Unlike the small combat fixtures, these checks use the saved player and saved spirit size.
[InitializeOnLoad]
public static class RealSceneContactChecks
{
    const string Pending = "RealSceneContactChecks.Pending";
    public const string Folder = "Logs/RealSceneContact";
    static RealSceneContactProbe probe;
    static double nextPoll;
    static bool reloadLocked;
    static RealSceneContactChecks()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending + ".Pounce", false))
            { SessionState.SetBool(Pending + ".Pounce", false); EditorApplication.LockReloadAssemblies(); reloadLocked = true; }
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode) ReleaseReload();
        };
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string path = "Logs/RealSceneContact.command";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim();
        if ((command == "before" || command == "after" || command == "pounce") && EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(path);
        if (command == "before") Diagnose();
        else if (command == "after") Check();
        else if (command == "pounce")
        {
            SessionState.SetBool(Pending + ".Pounce", true);
            EditorApplication.isPaused = false;
            SpiritPlayerPounceChecks.Begin();
        }
        else if (command == "refresh") AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
    }
    [MenuItem("GameJam/Final Loop/Diagnose actual player contact")]
    public static void Diagnose() => Begin("before");
    [MenuItem("GameJam/Final Loop/Check actual player contact")]
    public static void Check() => Begin("after");
    static void Begin(string name)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        Directory.CreateDirectory(Folder);
        SessionState.SetString(Pending + ".Name", name);
        SessionState.SetBool(Pending + ".Background", PlayerSettings.runInBackground);
        SessionState.SetBool(Pending, true);
        File.WriteAllText(Folder + "/" + name + ".json", "{\"status\":\"STARTING\"}");
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.isPaused = false;
        EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        EditorApplication.isPaused = false;
        EditorApplication.LockReloadAssemblies(); reloadLocked = true;
        probe = player.gameObject.AddComponent<RealSceneContactProbe>();
        probe.Begin(player, SessionState.GetString(Pending + ".Name", "after"));
    }
    public static void Finish()
    {
        ReleaseReload();
        PlayerSettings.runInBackground = SessionState.GetBool(Pending + ".Background", false);
        EditorApplication.ExitPlaymode();
    }
    public static void ReleaseReload()
    { if (reloadLocked) { reloadLocked = false; EditorApplication.UnlockReloadAssemblies(); } }
}

[DefaultExecutionOrder(10000)]
public sealed class RealSceneContactProbe : MonoBehaviour
{
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report
    {
        public string status = "RUNNING";
        public float minimumWalkingAlignment = 1, maximumWalkingYawRate;
        public string snapshot;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    FireRescuePlayer player;
    CreatureCombatPlayer health;
    VolcanoStorySequence story;
    LavaCreatureActor actor, template;
    LavaCreatureCombat combat;
    LavaSpiritVisual visual;
    readonly Report report = new Report();
    readonly List<string> frames = new List<string>();
    string name;
    int phase, contactCase, lastRender = -1, walkedFrames;
    float began, phaseAt, beforeHealth, previousYaw, launchAt, closestCore, sideOffset;
    float oldCapture;
    float nextSave;
    Vector3 arena;
    bool movedAside, sawPounce, finished, gotYaw;
    double deadline;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    public void Begin(FireRescuePlayer owner, string label)
    {
        player = owner; health = owner.GetComponent<CreatureCombatPlayer>(); story = owner.mission.story; name = label;
        began = phaseAt = Time.time; deadline = EditorApplication.timeSinceStartup + 160;
        oldCapture = Time.captureDeltaTime; Time.captureDeltaTime = 0; Application.runInBackground = true;
        Application.logMessageReceived += Log; RenderPipelineManager.endCameraRendering += Rendered;
        frames.Add("frame,time,phase,waypoint,distance,alignment,yaw,yawRate,actorState,pouncing,coreGap,health");
        Save();
    }
    void LateUpdate()
    {
        if (finished || !player) return;
        try
        {
            if (Time.unscaledTime >= nextSave) { nextSave = Time.unscaledTime + .5f; Save(); }
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Real scene review timed out"); return; }
            if (phase == 0)
            {
                if (!story.OpeningComplete) return;
                Add("Rendered walking view keeps facing forward", walkedFrames > 15 && report.minimumWalkingAlignment > .25f,
                    "frames=" + walkedFrames + ", min alignment=" + report.minimumWalkingAlignment);
                Add("Rendered walking view has no rapid reversals", report.maximumWalkingYawRate < 220,
                    "max yaw degrees/second=" + report.maximumWalkingYawRate);
                player.mission.enabled = false; player.mission.encounter.enabled = false; player.mission.sequence.enabled = false;
                template = player.mission.encounter.spawns.First(s => s != null && s.creature).creature;
                arena = FindArena(); player.enabled = true; player.PlaceOnFoot(arena, 0);
                BeginContactCase(0); phase = 1; return;
            }
            if (phase != 1) return;
            if (combat.IsPlayerPouncing)
            {
                sawPounce = true;
                if (launchAt < 0) launchAt = Time.time;
                if (contactCase > 0 && !movedAside && combat.PlayerPounceProgress > .18f)
                {
                    movedAside = true;
                    var heart = visual.VisualRoot.Find("Molten heart").GetComponent<Renderer>().bounds;
                    float visibleRadius = Mathf.Min(heart.extents.x, heart.extents.z);
                    float playerRadius = player.playerBody.radius * Mathf.Abs(player.transform.lossyScale.x);
                    sideOffset = playerRadius + visibleRadius * (contactCase == 1 ? .68f : 1f) + (contactCase == 2 ? 1f : 0f);
                    player.PlaceOnFoot(arena + Vector3.right * sideOffset, 0);
                }
            }
            if (visual && visual.VisualRoot)
            {
                Vector3 core = visual.VisualRoot.TransformPoint(Vector3.up * .65f);
                closestCore = Mathf.Min(closestCore, Vector3.Distance(core, player.playerBody.ClosestPoint(core)));
            }
            bool settle = sawPounce && !combat.IsAttacking && combat.State != LavaCreatureCombat.Motion.Settling;
            if (!actor.SelfDetonated && !settle && Time.time - phaseAt < 7f) return;
            string prefix = contactCase == 0 ? "Saved spirit direct impact" : contactCase == 1 ? "Saved spirit body brushes player" : "Saved spirit clear miss";
            string evidence = $"size={visual.VisualRoot.lossyScale}, radius={typeof(LavaCreatureCombat).GetProperty("RoofBodyRadius", Private).GetValue(combat)}, pounce={sawPounce}, closest={closestCore}, offset={sideOffset}, state={combat.State}, health={health.Health}, before={beforeHealth}, canAttack={health.CanBeAttacked}, locked={player.CinematicLocked}, driving={player.IsDriving}, bodyEnabled={player.playerBody.enabled}, layer={player.gameObject.layer}";
            if (contactCase < 2)
                Add(prefix + " explodes and removes health", actor.SelfDetonated && Mathf.Abs(health.Health - (beforeHealth - combat.attackDamage)) < .05f, evidence);
            else Add(prefix + " survives without an explosion or health loss", !actor.IsDefeated && !actor.SelfDetonated && Mathf.Abs(health.Health - beforeHealth) < .05f, evidence);
            if (contactCase < 2) BeginContactCase(contactCase + 1);
            else Finish(null);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    Vector3 FindArena()
    {
        var terrain = Terrain.activeTerrain;
        Vector3 origin = story.defensePosition ? story.defensePosition.position : player.mission.houseExit.position + Vector3.forward * 30;
        float radius = player.playerBody.radius * player.transform.lossyScale.x;
        float height = player.playerBody.height * player.transform.lossyScale.y;
        for (int n = 0; n < 80; n++)
        {
            Vector3 candidate = origin + new Vector3(n % 9 * 4 - 16, 0, n / 9 * 4 - 16);
            if (terrain) candidate.y = terrain.SampleHeight(candidate) + terrain.transform.position.y + .12f;
            bool blocked = false;
            for (int z = 0; z <= 1; z++)
                foreach (var solid in Physics.OverlapCapsule(candidate + Vector3.forward * (z * 9) + Vector3.up * (radius + .15f),
                    candidate + Vector3.forward * (z * 9) + Vector3.up * (height - radius), radius + .1f, ~0, QueryTriggerInteraction.Ignore))
                    if (!(solid is TerrainCollider) && !solid.transform.IsChildOf(player.transform) &&
                        solid.bounds.max.y > candidate.y + .3f && !solid.GetComponentInParent<LavaIgnitionSource>()) blocked = true;
            if (!blocked) return candidate;
        }
        throw new InvalidOperationException("No clear real village test position");
    }
    void BeginContactCase(int index)
    {
        if (actor) { actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
        contactCase = index; player.PlaceOnFoot(arena, 0);
        var root = Instantiate(template.gameObject); root.SetActive(false);
        root.transform.localScale = template.transform.lossyScale;
        root.transform.SetPositionAndRotation(arena + Vector3.forward * 8, template.transform.rotation);
        actor = root.GetComponent<LavaCreatureActor>();
        combat = root.GetComponent<LavaCreatureCombat>();
        if (!combat) combat = root.AddComponent<LavaCreatureCombat>();
        combat.target = health; combat.attackBuildings = false;
        root.SetActive(true); visual = root.GetComponent<LavaSpiritVisual>();
        beforeHealth = health.Health; phaseAt = Time.time; launchAt = -1; closestCore = 1000;
        movedAside = sawPounce = false; sideOffset = 0; Physics.SyncTransforms();
    }
    void Rendered(ScriptableRenderContext context, Camera camera)
    {
        if (finished || camera != player.viewCamera || !camera.enabled || lastRender == Time.frameCount) return;
        lastRender = Time.frameCount;
        int waypoint = -1; float distance = 0, alignment = 1, yawRate = 0;
        float yaw = camera.transform.eulerAngles.y;
        if (phase == 0 && story.IsOpening && !story.AtWindow)
        {
            waypoint = (int)typeof(VolcanoStorySequence).GetField("waypoint", Private).GetValue(story);
            Transform destination = waypoint < story.windowRoute.Length ? story.windowRoute[waypoint] : story.windowStop;
            Vector3 travel = Vector3.ProjectOnPlane(destination.position - player.transform.position, Vector3.up);
            distance = travel.magnitude;
            if (distance > .05f && gotYaw)
            {
                alignment = Vector3.Dot(travel.normalized, Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up).normalized);
                yawRate = Mathf.Abs(Mathf.DeltaAngle(previousYaw, yaw)) / Mathf.Max(.001f, Time.deltaTime);
                report.minimumWalkingAlignment = Mathf.Min(report.minimumWalkingAlignment, alignment);
                report.maximumWalkingYawRate = Mathf.Max(report.maximumWalkingYawRate, yawRate); walkedFrames++;
            }
            gotYaw = true;
        }
        previousYaw = yaw;
        frames.Add(string.Join(",", Time.frameCount, Time.time.ToString("F4", CultureInfo.InvariantCulture), phase, waypoint,
            distance.ToString("F4", CultureInfo.InvariantCulture), alignment.ToString("F4", CultureInfo.InvariantCulture),
            yaw.ToString("F4", CultureInfo.InvariantCulture), yawRate.ToString("F4", CultureInfo.InvariantCulture),
            combat ? combat.State.ToString() : "", combat && combat.IsPlayerPouncing, closestCore, health.Health));
        if (Time.frameCount % 30 == 0)
        { Save(); File.WriteAllLines(RealSceneContactChecks.Folder + "/" + name + "-rendered-frames.csv", frames); }
    }
    void Add(string label, bool passed, string evidence)
    { report.checks.Add(new Check { name = label, passed = passed, evidence = evidence }); Save(); }
    void Save()
    {
        report.snapshot = $"phase={phase}, frame={Time.frameCount}, time={Time.time}, paused={EditorApplication.isPaused}, scale={Time.timeScale}, opening={story.IsOpening}, atWindow={story.AtWindow}, complete={story.OpeningComplete}, position={player.transform.position}, camera={player.viewCamera.enabled}, lock={player.CinematicLocked}";
        File.WriteAllText(RealSceneContactChecks.Folder + "/" + name + ".json", JsonUtility.ToJson(report, true));
    }
    void Finish(string error)
    {
        if (finished) return; finished = true;
        if (error != null) report.errors.Add(error);
        report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED"; Save();
        File.WriteAllLines(RealSceneContactChecks.Folder + "/" + name + "-rendered-frames.csv", frames);
        RenderPipelineManager.endCameraRendering -= Rendered; Application.logMessageReceived -= Log;
        Time.captureDeltaTime = oldCapture; RealSceneContactChecks.Finish();
    }
    void Log(string message, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) report.errors.Add(message + "\n" + stack); }
    void OnDestroy()
    {
        if (!finished && name != null)
        {
            report.status = "INTERRUPTED"; Save();
            File.WriteAllLines(RealSceneContactChecks.Folder + "/" + name + "-rendered-frames.csv", frames);
        }
        RenderPipelineManager.endCameraRendering -= Rendered; Application.logMessageReceived -= Log;
        RealSceneContactChecks.ReleaseReload();
    }
}
