using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Checks the real saved bedroom and capsule, alongside isolated thermal regressions.
[InitializeOnLoad]
public static class OpeningCoolingPounceChecks
{
    public const string Folder = "Logs/OpeningCoolingPounce";
    const string Pending = "OpeningCoolingPounceChecks.Pending";
    static VolcanoStorySequence story;
    static FireRescuePlayer player;
    static OpeningContactProbe probe;
    static readonly List<string> trace = new List<string>();
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly List<AudioClip> heardVoices = new List<AudioClip>();
    static int frame, phase = -1;
    static float began, nextSample, oldCapture, blockedSeconds, maxViewStep;
    static float gameplayFov;
    static bool oldBackground, captured;
    static double deadline;
    static Vector3 previousPosition, previousEye;

    static OpeningCoolingPounceChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (s == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted");
        };
    }

    [MenuItem("GameJam/Final Loop/Audit opening geometry")]
    public static void Audit()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var p = mission.player; var s = mission.story;
        Directory.CreateDirectory(Folder); Physics.SyncTransforms();
        var log = new StringBuilder();
        log.AppendLine("SCENE " + mission.gameObject.scene.path + " dirty=" + mission.gameObject.scene.isDirty);
        log.AppendLine("BODY " + EditorJsonUtility.ToJson(p.playerBody) + " scale=" + p.transform.lossyScale);
        log.AppendLine("PLAYER " + p.transform.position.ToString("F3") + " CAMERA " + p.viewCamera.transform.position.ToString("F3") +
            " local=" + p.viewCamera.transform.localPosition.ToString("F3"));
        log.AppendLine("STANDING " + mission.standingSpawn.position.ToString("F3") + " WINDOW " + s.windowStop.position.ToString("F3") +
            " euler=" + s.windowStop.eulerAngles + " FOCUS " + s.volcanoLookTarget.position.ToString("F3"));
        log.AppendLine("WAKE " + mission.wakePose.position.ToString("F3") + " rotation=" + mission.wakePose.eulerAngles);
        log.AppendLine("STORY " + EditorJsonUtility.ToJson(s));
        foreach (var t in s.windowRoute) if (t) log.AppendLine("ROUTE " + t.position.ToString("F3"));
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!c.gameObject.scene.IsValid() || c.isTrigger || c is TerrainCollider || c.transform.IsChildOf(p.transform) ||
                c.bounds.SqrDistance(s.windowStop.position + Vector3.up * 3) > 225) continue;
            log.AppendLine("SOLID " + Path(c.transform) + " " + c.GetType().Name + " enabled=" + c.enabled + " bounds=" + c.bounds);
        }
        File.WriteAllText(Folder + "/geometry.txt", log.ToString());
    }

    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;

    [MenuItem("GameJam/Final Loop/Apply stable opening framing")]
    public static void Apply() => OpeningCinematicSetup.Apply();

    [MenuItem("GameJam/Final Loop/Preview clear window panes")]
    public static void PreviewPanes()
    {
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var p = mission.player; var s = mission.story;
        var go = new GameObject("Window framing review") { hideFlags = HideFlags.HideAndDontSave };
        var view = go.AddComponent<Camera>(); view.CopyFrom(p.viewCamera); view.enabled = false;
        view.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        view.GetUniversalAdditionalCameraData().volumeLayerMask = p.viewCamera.GetUniversalAdditionalCameraData().volumeLayerMask;
        var log = new StringBuilder();
        try
        {
            Vector3 eye = s.windowStop.position + Quaternion.Euler(0, s.windowStop.eulerAngles.y, 0) *
                Vector3.Scale(p.viewCamera.transform.localPosition, p.transform.lossyScale);
            var offsets = new[] { new Vector3(-3.6f,-1.4f,0), new Vector3(-2.8f,-1.4f,.3f),
                new Vector3(-2.8f,-.2f,.3f), new Vector3(-1.8f,-.2f,.3f), new Vector3(-3.6f,-.2f,0),
                new Vector3(-2.4f,-1.4f,1.1f), new Vector3(-3.2f,-1.4f,1.1f), new Vector3(-3.2f,-.6f,1.1f) };
            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 position = eye + offsets[i];
                view.transform.SetPositionAndRotation(position, Quaternion.LookRotation(s.volcanoLookTarget.position + Vector3.down * 60 - position));
                view.fieldOfView = 50;
                Capture(view, "pane-" + i + ".png");
                var blockers = Physics.OverlapSphere(position, .14f, ~0, QueryTriggerInteraction.Ignore).Where(c => !c.transform.IsChildOf(p.transform));
                log.AppendLine(i + " eye=" + position.ToString("F3") + " overlaps=" + string.Join(",", blockers.Select(c => c.name)));
            }
            File.WriteAllText(Folder + "/panes.txt", log.ToString());
        }
        finally { Object.DestroyImmediate(go); }
    }

    [MenuItem("GameJam/Final Loop/Review real opening")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Pending, true);
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.isPaused = false; EditorApplication.EnterPlaymode();
    }

    [MenuItem("GameJam/Final Loop/Review opening at low frame rate")]
    public static void BeginCoarse()
    {
        SessionState.SetBool(Pending + ".Coarse", true); Begin();
    }

    static void Start()
    {
        player = Object.FindFirstObjectByType<FireRescuePlayer>(); story = player.mission.story;
        gameplayFov = player.viewCamera.fieldOfView;
        oldCapture = Time.captureDeltaTime; oldBackground = Application.runInBackground;
        Application.runInBackground = true; Time.captureDeltaTime = SessionState.GetBool(Pending + ".Coarse", false) ? 1f / 12f : 1f / 60f;
        trace.Clear(); began = Time.time; phase = 0; nextSample = 0; captured = false;
        checks.Clear(); errors.Clear(); heardVoices.Clear(); Application.logMessageReceived += Log;
        float speechSeconds = story.openingVoiceClips.Where(c => c).Sum(c => c.length + story.voiceLineGap);
        blockedSeconds = maxViewStep = 0;
        deadline = EditorApplication.timeSinceStartup + Mathf.Max(120, (story.approachTimeout + story.ObservationDuration + speechSeconds + 15) * 2);
        previousPosition = player.transform.position; previousEye = player.viewCamera.transform.position;
        probe = player.gameObject.AddComponent<OpeningContactProbe>();
        File.WriteAllText(Folder + "/opening-result.txt", "RUNNING");
    }

    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish("Voice-aware opening timed out"); return; }
            var voiceRoot = story.transform.Find("Opening inner monologue");
            var voice = voiceRoot ? voiceRoot.GetComponent<AudioSource>() : null;
            if (voice && voice.isPlaying && voice.clip && (heardVoices.Count == 0 || heardVoices[heardVoices.Count - 1] != voice.clip)) heardVoices.Add(voice.clip);
            float moved = Vector3.Distance(previousPosition, player.transform.position);
            if (story.IsOpening && !story.AtWindow && moved < .001f) blockedSeconds += Time.deltaTime;
            if (story.AtWindow && story.ObservationElapsed > story.windowZoomSeconds + .2f && story.ObservationElapsed < story.ObservationDuration - 1.5f)
                maxViewStep = Mathf.Max(maxViewStep, Vector3.Distance(previousEye, player.viewCamera.transform.position));
            previousPosition = player.transform.position; previousEye = player.viewCamera.transform.position;
            if (Time.time >= nextSample)
            {
                nextSample = Time.time + .15f;
                trace.Add($"t={Time.time-began:F3} opening={story.IsOpening} atWindow={story.AtWindow} locked={player.CinematicLocked} body={player.transform.position:F4} eye={player.viewCamera.transform.position:F4} rot={player.viewCamera.transform.eulerAngles:F3} wall={probe.lastWall}");
            }
            if (story.AtWindow && story.ObservationElapsed > 3.5f && !captured)
            {
                var view = player.viewCamera;
                Vector3 crater = view.WorldToViewportPoint(story.volcanoLookTarget.position);
                Check("Opening close-up centers the crater and visibly magnifies the mountain", crater.z > 0 &&
                    Vector2.Distance(new Vector2(crater.x, crater.y), new Vector2(.5f, .5f)) < .01f &&
                    view.fieldOfView <= 30f && Mathf.Abs(view.fieldOfView - story.windowFieldOfView) < .1f,
                    "crater=" + crater + " fov=" + view.fieldOfView);
                var atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
                DepthOfField lens = null;
                bool focused = atmosphere && atmosphere.cinematicVolume && atmosphere.cinematicVolume.weight > .99f &&
                    atmosphere.cinematicVolume.profile.TryGet(out lens);
                Check("Mountain stays on the bokeh focus plane while the foreground is defocused", focused &&
                    lens.mode.value == DepthOfFieldMode.Bokeh && lens.aperture.value <= 2f && lens.focalLength.value >= 150f &&
                    Mathf.Abs(lens.focusDistance.value - crater.z) < 1f && view.GetUniversalAdditionalCameraData().renderPostProcessing,
                    focused ? "focus=" + lens.focusDistance.value + " craterDepth=" + crater.z : "missing active focus lens");
                Capture(view, "window.png"); captured = true;
            }
            if (story.OpeningComplete && (!voice || !voice.isPlaying)) Finish(null);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    public static void Capture(Camera camera, string name)
    {
        var rt = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = camera.targetTexture; var active = RenderTexture.active; float aspect = camera.aspect;
        try
        {
            camera.targetTexture = rt; camera.aspect = 16f / 9; camera.Render(); RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            Directory.CreateDirectory(Folder); File.WriteAllBytes(Folder + "/" + name, texture.EncodeToPNG());
        }
        finally { camera.targetTexture = previous; camera.aspect = aspect; RenderTexture.active = active; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(texture); }
    }

    static void Finish(string error)
    {
        if (error != null) errors.Add(error);
        Check("Saved opening finishes and releases movement", story.OpeningComplete && !player.CinematicLocked);
        Check("Normal field of view returns after the observation", Mathf.Abs(player.viewCamera.fieldOfView - gameplayFov) < .1f);
        var atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
        Check("Opening depth of field clears when control returns", atmosphere && atmosphere.cinematicVolume &&
            atmosphere.cinematicVolume.weight == 0 && story.WindowFocusBlend == 0);
        Check("All five opening voices actually play in their authored order", heardVoices.SequenceEqual(story.openingVoiceClips.Take(5)),
            string.Join(",", heardVoices.Select(c => c ? c.name : "missing")));
        Check("Full capsule reaches the authored stop", EvacuationResident.FlatDistance(player.transform.position, story.windowStop.position) <= story.arrivalRadius + .01f);
        Check("No wall contacts or blocked walking frames", probe.wallContacts == 0 && blockedSeconds < .2f,
            "contacts=" + probe.wallContacts + ", blocked=" + blockedSeconds);
        Check("Observation camera stays still", maxViewStep < .001f, "max displacement=" + maxViewStep);
        Check("Wake and observation lens stay out of solid geometry", probe.lensOverlaps == 0, probe.lastLensOverlap);
        if (error == null && !SessionState.GetBool(Pending + ".Coarse", false))
        {
            try
            {
                VolcanoHazardChecks.Run(Check);
                CheckOverlappingColdGround();
                FireCombatChecks.Run();
            }
            catch (Exception ex) { errors.Add(ex.ToString()); }
        }
        File.WriteAllLines(Folder + "/opening-trace.txt", trace);
        string suffix = SessionState.GetBool(Pending + ".Coarse", false) ? "-12fps" : "-60fps";
        File.WriteAllLines(Folder + "/checks" + suffix + ".txt", checks.Concat(errors.Select(e => "ERROR " + e)));
        File.WriteAllText(Folder + "/opening-result" + suffix + ".txt", (errors.Count == 0 && checks.All(c => c.StartsWith("PASS")) ? "SUCCESS" : "FAILED") +
            $"\nblockedSeconds={blockedSeconds:F4}\nmaxSteadyCameraStep={maxViewStep:F6}\nwallContacts={probe.wallContacts}\nlastWall={probe.lastWall}\nbody={player.transform.position:F4}\nwindow={story.windowStop.position:F4}\nreleased={!player.CinematicLocked}");
        File.WriteAllText(Folder + "/opening-result.txt", "FINISHED " + suffix);
        phase = -1; Time.captureDeltaTime = oldCapture; Application.runInBackground = oldBackground;
        Application.logMessageReceived -= Log; SessionState.SetBool(Pending + ".Coarse", false);
        EditorApplication.ExitPlaymode();
    }

    static void Log(string message, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) errors.Add(message + "\n" + stack); }
    static void Check(string name, bool passed, string evidence = "") => checks.Add((passed ? "PASS " : "FAIL ") + name + " " + evidence);

    static void CheckOverlappingColdGround()
    {
        var fixture = new GameObject("Overlapping lava and cold ground regression");
        Mesh mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-80,0,-80), new Vector3(-80,0,80), new Vector3(80,0,80), new Vector3(80,0,-80) };
        mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        Vector3 origin = new Vector3(16000, 1000, 16000);
        try
        {
            LavaIgnitionSource MakeLava(string name, float y)
            {
                var go = new GameObject(name); go.transform.SetParent(fixture.transform); go.transform.position = origin + Vector3.up * y;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>();
                var solid = go.AddComponent<MeshCollider>(); solid.sharedMesh = mesh;
                var source = go.AddComponent<LavaIgnitionSource>(); source.heatCollider = solid;
                return source;
            }
            var top = MakeLava("Lava with cooled cover", 0);
            var underneath = MakeLava("Overlapping hot branch", -.12f);
            var cooling = top.gameObject.AddComponent<LavaCoolingSurface>(); cooling.patchRadius = 28f;
            cooling.steamMaterial = Resources.Load<Material>("LavaCombat/Smoke");
            var root = Object.Instantiate(player.drive.gameObject); root.name = "Actual truck crossing cooled lava";
            root.transform.SetParent(fixture.transform); root.transform.SetPositionAndRotation(origin, Quaternion.identity);
            var truck = root.GetComponent<FireTruckDrive>(); truck.enabled = false; truck.body.isKinematic = true;
            var thermal = truck.Thermal;
            float bottom = float.PositiveInfinity;
            foreach (var wheel in truck.wheels)
            {
                wheel.GetWorldPose(out var position, out _);
                bottom = Mathf.Min(bottom, position.y - wheel.radius * wheel.transform.lossyScale.y);
            }
            root.transform.position += Vector3.up * (origin.y + .03f - bottom);
            Physics.SyncTransforms();
            Check("Actual truck detects uncool lava beneath its tyres", thermal.CheckLavaContact());
            cooling.ApplyWater(origin, 90); Physics.SyncTransforms();
            Check("Visible black crust is a real solid mesh", cooling.GetComponentsInChildren<MeshCollider>().Any(c => c != top.heatCollider && c.enabled && c.sharedMesh && c.Raycast(new Ray(origin + Vector3.up * 2, Vector3.down), out _, 3)));
            Check("Entire cold rim is safe without a hot invisible seam", !top.IsHotAt(origin + Vector3.right * 27.9f));
            Check("Cold cover shields overlapping lava branches", !underneath.IsHotAt(origin - Vector3.up * .12f));
            Check("Uncooled lava beyond the black cover stays hot", top.IsHotAt(origin + Vector3.right * 30));
            float integrity = thermal.Health, water = root.GetComponent<FireHoseSystem>().TankLitres;
            Vector3 truckStart = root.transform.position;
            for (int i = -6; i <= 6; i++)
            { root.transform.position = truckStart + Vector3.right * i; Physics.SyncTransforms(); thermal.TickHeat(.1f); }
            Check("Truck crosses cold rock without ignition, damage or suppression water", !thermal.InHotLava && !thermal.IsBurning && thermal.Health == integrity && root.GetComponent<FireHoseSystem>().TankLitres == water,
                "health=" + thermal.Health + ", burning=" + thermal.IsBurning);

            var human = new GameObject("Actual size player on cold cover"); human.transform.SetParent(fixture.transform);
            human.transform.position = origin - Vector3.up * .3f; human.transform.localScale = player.transform.lossyScale;
            var body = human.AddComponent<CharacterController>(); body.radius = player.playerBody.radius;
            body.height = player.playerBody.height; body.center = player.playerBody.center;
            var health = human.AddComponent<CreatureCombatPlayer>(); health.allowRecovery = false;
            var contact = human.AddComponent<LavaPlayerContact>(); Physics.SyncTransforms();
            contact.TickContact(1);
            Check("Standing or sinking into black cover causes no player damage", !contact.InHotLava && health.Health == 100);
            body.enabled = false; human.transform.position = origin + Vector3.up; body.enabled = true;
            Physics.SyncTransforms(); contact.RefreshCollisionPassThrough(); body.Move(Vector3.down * 2);
            Check("Cold crust supports the full player capsule", body.bounds.min.y > origin.y + .05f, "feet=" + body.bounds.min.y);
            body.enabled = false; human.transform.position = origin + Vector3.right * 40 - Vector3.up * .3f; body.enabled = true;
            Physics.SyncTransforms(); contact.TickContact(.1f);
            Check("Uncooled lava still damages the player", contact.InHotLava && health.Health < 100);
            float before = health.Health; health.TakeHeatDamage(.1f);
            Check("Ongoing lava heat cannot cancel a spirit's impact damage", health.TryTakeDamage(28, origin) && Mathf.Abs(health.Health - (before - .1f - 28)) < .01f);
            Check("Simultaneous spirits still share their short damage gate", !health.TryTakeDamage(28, origin));
            cooling.ResetCooling(); Physics.SyncTransforms();
            Check("Restart removes both crust collision and heat protection", top.IsHotAt(origin) && underneath.IsHotAt(origin - Vector3.up * .12f) && !LavaCoolingSurface.IsSolidCrustAt(origin));
        }
        finally { Object.DestroyImmediate(fixture); Object.DestroyImmediate(mesh); }
    }
}

[DefaultExecutionOrder(10000)]
public sealed class OpeningContactProbe : MonoBehaviour
{
    public int wallContacts;
    public string lastWall;
    public int lensOverlaps;
    public string lastLensOverlap;
    void LateUpdate()
    {
        var player = GetComponent<FireRescuePlayer>(); var cinema = player.mission.cinematic;
        var view = cinema.IsPlaying ? cinema.ShotCamera : player.viewCamera;
        if (!view || !player.CinematicLocked) return;
        foreach (var solid in Physics.OverlapSphere(view.transform.position, .1f, ~0, QueryTriggerInteraction.Ignore))
            if (solid && !solid.transform.IsChildOf(player.transform))
            { lensOverlaps++; lastLensOverlap = solid.name; }
    }
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y > .6f) return;
        wallContacts++; lastWall = hit.collider.name + " normal=" + hit.normal.ToString("F3");
    }
}
