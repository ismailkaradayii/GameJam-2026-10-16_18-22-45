using System;
using System.Collections.Generic;
using System.IO;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated saved-scene review: real renderer and existing eruption clock, no authored scene edits.</summary>
[InitializeOnLoad]
public static class VolcanoFogChecks
{
    const string Folder = "Library/VolcanoFogReview", Pending = "GameJam.VolcanoFogReview";
    static readonly List<string> report = new List<string>();
    static VolcanoAtmospherePolish atmosphere;
    static VolcanoDescendingAshFog fog;
    static VolcanoMissionDirector mission;
    static VolcanoEruptionSequence sequence;
    static VolcanoWeatherTransition weather;
    static Camera camera;
    static int phase = -1, frame, originalMission;
    static double deadline, nextPoll;
    static float phaseAt, pausedTime, pausedEnd;
    static Vector3 pausedFront, initialFront;
    static bool oldBackground;
    static string error;

    static VolcanoFogChecks()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/ready.txt", DateTime.UtcNow.ToString("O"));
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Review interrupted.");
        };
    }

    static void Poll()
    {
        if (phase >= 0) { Tick(); return; }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        string path = Folder + "/request.txt";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "run") Run();
            else if (command == "inspect") File.WriteAllText(Folder + "/state.txt", "playing=" + EditorApplication.isPlaying + " dirty=" + SceneManager.GetActiveScene().isDirty + " scene=" + SceneManager.GetActiveScene().path);
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/command-error.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("GameJam/Final Loop/Review descending ash fog")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Use the saved Volcano_Demo in Edit Mode; no live or unsaved session is interrupted.");
        if (SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath) throw new InvalidOperationException("Open Volcano_Demo.");
        report.Clear(); File.WriteAllText(Folder + "/checks.txt", "STARTING " + DateTime.UtcNow.ToString("O"));
        SessionState.SetBool(Pending + ".Background", PlayerSettings.runInBackground);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }

    static void Start()
    {
        try { Prepare(); }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Prepare()
    {
        report.Clear(); error = null;
        report.Add("Started UTC: " + DateTime.UtcNow.ToString("O"));
        report.Add("Saved-scene visual fixture. Existing eruption APIs are stepped in bounded increments; camera viewpoints, disabled hazards and player placement isolate the atmospheric change. This is not a full manual battle or a performance benchmark.");
        oldBackground = SessionState.GetBool(Pending + ".Background", false); Application.runInBackground = true;
        Application.logMessageReceived += Log;
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); originalMission = mission.GetInstanceID();
        sequence = mission.sequence; atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
        fog = atmosphere.descendingFog; weather = Object.FindFirstObjectByType<VolcanoWeatherTransition>();
        mission.cinematic.EndShot(); mission.story.CancelOpening(); mission.enabled = false;
        mission.encounter.StopEncounter(); mission.encounter.enabled = false; mission.gameFlow.enabled = false;
        mission.player.enabled = false; mission.player.firstPerson.enabled = false;
        sequence.enabled = false; sequence.enableCameraShake = false; sequence.cinematic = null;
        sequence.routes = Array.Empty<VolcanoEruptionSequence.Route>(); sequence.pools = Array.Empty<VolcanoEruptionSequence.Pool>();
        sequence.lavaLights = Array.Empty<VolcanoEruptionSequence.LavaLight>(); sequence.ambientEffects = Array.Empty<VolcanoEruptionSequence.AmbientEffect>();
        foreach (var thermal in Object.FindObjectsByType<ThermalDamage>(FindObjectsSortMode.None)) thermal.enabled = false;
        foreach (var source in LavaIgnitionSource.ActiveSources) if (source) source.heatEnabled = false;
        camera = new GameObject("Fog review camera").AddComponent<Camera>(); camera.CopyFrom(mission.player.viewCamera);
        camera.enabled = false; camera.fieldOfView = 65; camera.farClipPlane = 3500;
        var data = camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing = true;
        data.volumeLayerMask = mission.player.viewCamera.GetUniversalAdditionalCameraData().volumeLayerMask;
        Check("fog automatically binds to the saved mission", fog && fog.IsReady && fog.UsesNativeSmoke);
        initialFront = fog.FrontPosition;
        Step(2);
        Check("preparation has no descending smoke or storm coverage", fog.LiveParticles == 0 && fog.FrontProgress == 0 && atmosphere.AshBlend == 0);
        Check("indoor exposure lifts the room without a global exposure jump", atmosphere.IndoorBlend > .9f && atmosphere.atmosphereVolume.profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var grade) && grade.postExposure.value >= .95f);
        camera.transform.SetPositionAndRotation(mission.player.viewCamera.transform.position, Quaternion.LookRotation(mission.story.windowStop.position + Vector3.up * 2 - mission.player.viewCamera.transform.position));
        Capture("01-room-balanced");
        SetVillageView(); MovePlayerOutside(); Step(2);
        Capture("02-village-before");
        Check("outdoor view smoothly restores the ordinary exposure", atmosphere.IndoorBlend < .01f);
        sequence.BeginEruption(true); Step(sequence.warningSeconds * .5f);
        Check("the warning does not start the fog", !sequence.effects.IsErupting && fog.LiveParticles == 0);
        Step(sequence.warningSeconds + 2);
        Check("eruption onset keeps the town clear while the fog is still uphill", fog.FrontProgress == 0 && atmosphere.AshBlend < .01f);
        Next(0);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Fog phase " + phase);
            if (Time.unscaledTime - phaseAt < .3f) return;
            if (phase == 0)
            {
                Step(18);
                Check("a real smoke front descends from the crater before reaching town", fog.FrontProgress > .2f && fog.FrontProgress < .6f && fog.FrontPosition.z < initialFront.z - 60 && fog.VillageCoverage == 0 && fog.LiveParticles > 0);
                camera.transform.position = new Vector3(420, 210, -930); camera.transform.LookAt(new Vector3(10, 180, -300));
                Capture("03-descending-front"); Next(1);
            }
            else if (phase == 1)
            {
                Step(70); SetVillageView();
                Check("fog settles over the town gradually", fog.FrontProgress == 1 && fog.VillageCoverage == 1 && atmosphere.AshBlend > .99f);
                Check("near gameplay remains visible within a dense distant fog", RenderSettings.fogStartDistance >= 30 && RenderSettings.fogEndDistance >= 260 && RenderSettings.fogEndDistance <= 450);
                Check("the fog remains inside its fixed particle budget", fog.LiveParticles > 0 && fog.LiveParticles <= VolcanoDescendingAshFog.MaximumParticles);
                float end = RenderSettings.fogEndDistance;
                atmosphere.enabled = true; weather.enabled = true;
                atmosphere.TickAtmosphere(.1f); weather.TickWeather(.1f);
                Check("weather cannot overwrite the coordinated distance fog", Mathf.Abs(RenderSettings.fogEndDistance - end) < .01f);
                Capture("04-village-in-ash");
                Check("bloom and chromatic effects stay restrained", atmosphere.atmosphereVolume.profile.TryGet<Bloom>(out var bloom) && bloom.intensity.value <= .30f && bloom.threshold.value >= 1.3f && atmosphere.atmosphereVolume.profile.TryGet<ChromaticAberration>(out var chromatic) && chromatic.intensity.value <= .012f);
                mission.gameFlow.Pause(); pausedTime = sequence.effects.Elapsed; pausedFront = fog.FrontPosition; pausedEnd = RenderSettings.fogEndDistance;
                Next(2);
            }
            else if (phase == 2)
            {
                atmosphere.TickAtmosphere(0); fog.TickFog();
                Check("pause freezes the front and the atmospheric transition", Time.timeScale == 0 && sequence.effects.Elapsed == pausedTime && fog.FrontPosition == pausedFront && Mathf.Approximately(RenderSettings.fogEndDistance, pausedEnd));
                mission.gameFlow.Resume();
                mission.gameFlow.RestartMission(); Next(3);
            }
            else if (phase == 3)
            {
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.GetInstanceID() == originalMission) return;
                var current = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
                Check("restart restores clear preparation and releases the old fog", current && current.descendingFog && current.AshBlend == 0 && current.descendingFog.LiveParticles == 0 && Time.timeScale == 1);
                Check("saved mission actors and cinematic bindings are unchanged", fresh.gameFlow.targetBuildings.Length == 5 && fresh.evacuation.TotalCount == 7 && fresh.story && fresh.gameFlow.victoryCinematic);
                Finish(null);
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }

    static void Step(float seconds)
    {
        int frames = Mathf.CeilToInt(seconds * 10);
        for (int i = 0; i < frames; i++)
        {
            sequence.TickSequence(.1f); atmosphere.TickAtmosphere(.1f); weather.TickWeather(.1f);
            foreach (var ps in fog.GetComponentsInChildren<ParticleSystem>()) if (ps.isPlaying) ps.Simulate(.1f, false, false, false);
            foreach (var cloud in sequence.effects.driftingAshClouds) if (cloud.particles && cloud.particles.isPlaying) cloud.particles.Simulate(.1f, false, false, false);
        }
    }
    static void MovePlayerOutside() => mission.player.PlaceOnFoot(new Vector3(50, 1.4f, -570), 0);
    static void SetVillageView()
    {
        Vector3 point = new Vector3(-10, 0, -565);
        if (Terrain.activeTerrain) point.y = Terrain.activeTerrain.SampleHeight(point) + Terrain.activeTerrain.transform.position.y;
        point.y += 7; camera.transform.position = point; camera.transform.LookAt(new Vector3(92, 7, -610));
    }
    static void Capture(string name)
    {
        // Write display-referred sRGB pixels, matching the Game view rather than raw linear HDR values.
        var target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var previous = camera.targetTexture; var active = RenderTexture.active;
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply();
            File.WriteAllBytes(Folder + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally { camera.targetTexture = previous; RenderTexture.active = active; RenderTexture.ReleaseTemporary(target); Object.Destroy(pixels); }
    }
    static void Next(int value) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 40; }
    static void Check(string label, bool passed)
    {
        report.Add((passed ? "PASS " : "FAIL ") + label); File.WriteAllLines(Folder + "/checks.txt", report);
        if (!passed) throw new InvalidOperationException(label);
    }
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message + "\n" + stack; }
    static void Finish(string failure)
    {
        phase = -1; Application.logMessageReceived -= Log;
        if (camera) Object.Destroy(camera.gameObject);
        Time.timeScale = 1; Application.runInBackground = oldBackground; PlayerSettings.runInBackground = oldBackground;
        report.Add(failure == null ? "COMPLETED" : "FAILED " + failure); File.WriteAllLines(Folder + "/checks.txt", report);
        EditorApplication.ExitPlaymode();
    }
}
