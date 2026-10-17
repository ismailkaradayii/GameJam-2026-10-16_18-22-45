using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Reviews the scene's existing asset clouds, their travel, settling and eruption timing.</summary>
[InitializeOnLoad]
public static class NativeAshCloudChecks
{
    const string Folder = NativeAshCloudSetup.Folder, Pending = "NativeAshCloudChecks.Pending";
    [Serializable] class Check { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status = "RUNNING"; public List<Check> checks = new List<Check>(); public List<string> errors = new List<string>(); }
    static Report report;
    static VolcanoEruptionSequence sequence;
    static VolcanoEruptionEffects effects;
    static VolcanoAtmospherePolish atmosphere;
    static Camera camera;
    static bool background;
    static float oldScale;

    static NativeAshCloudChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Run; }
        };
    }
    [MenuItem("GameJam/Final Loop/Check native ash cloud travel")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/report.json", "{\"status\":\"STARTING\"}");
        var fx = Object.FindFirstObjectByType<VolcanoEruptionEffects>();
        var weather = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
        // Unity strips scene prefab links in Play Mode; record provenance before entering it.
        SessionState.SetString(Pending + ".CloudSources", string.Join("\n", fx.driftingAshClouds.Select(c => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(c.particles))));
        SessionState.SetString(Pending + ".AshSource", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(weather.fallingAsh));
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        report = new Report(); background = Application.runInBackground; oldScale = Time.timeScale;
        Application.runInBackground = true; Time.timeScale = 1; Application.logMessageReceived += Log;
        try
        {
            sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>(); effects = sequence.effects;
            atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
            var player = atmosphere.player; var flow = player.gameFlow;
            if (sequence.cinematic) { sequence.cinematic.EndShot(); sequence.cinematic.enabled = false; }
            sequence.cinematic = null; sequence.enabled = false; sequence.autoErupt = false;
            var director = Object.FindFirstObjectByType<VolcanoMissionDirector>(); if (director) director.enabled = false;
            var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>(); if (encounter) { encounter.StopEncounter(); encounter.enabled = false; }
            player.enabled = false; player.firstPerson.enabled = false; player.drive.body.isKinematic = true;
            foreach (var source in LavaIgnitionSource.ActiveSources) if (source) source.heatEnabled = false;
            foreach (var thermal in ThermalDamage.ActiveTargets.ToArray()) if (thermal) thermal.enabled = false;
            foreach (var resident in Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None)) resident.enabled = false;
            foreach (var view in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) view.enabled = false;
            foreach (var hud in Object.FindObjectsByType<RescueHUD>(FindObjectsSortMode.None)) hud.enabled = false;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) canvas.enabled = false;
            sequence.routes = Array.Empty<VolcanoEruptionSequence.Route>(); sequence.pools = Array.Empty<VolcanoEruptionSequence.Pool>();
            sequence.ambientEffects = Array.Empty<VolcanoEruptionSequence.AmbientEffect>(); sequence.lavaLights = Array.Empty<VolcanoEruptionSequence.LavaLight>();
            sequence.enableCameraShake = false;
            typeof(VolcanoEruptionSequence).GetField("audioTimeline", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(sequence, null);
            foreach (var audio in Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) audio.Stop();
            atmosphere.enabled = false;
            camera = new GameObject("Native cloud review camera").AddComponent<Camera>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            camera.farClipPlane = 3000; camera.fieldOfView = 65; camera.allowHDR = true;
            SetWideView();

            var cloudSources = SessionState.GetString(Pending + ".CloudSources", "").Split('\n');
            Verify("Three cloud banks reuse native prefab instances", effects.driftingAshClouds.Length == 3 && cloudSources.Length == 3 && cloudSources.All(p => p.EndsWith("/VolcanoSmoke.prefab")), string.Join(" | ", cloudSources));
            Verify("Every cloud keeps the pack's original smoke material", effects.driftingAshClouds.All(c => AssetDatabase.GetAssetPath(c.particles.GetComponent<ParticleSystemRenderer>().sharedMaterial) == "Assets/BK/PureNature_Volcano/Textures/Fx/Textures/Materials/Smoke.mat"));
            Verify("Falling ash reuses the existing native Ashes prefab", SessionState.GetString(Pending + ".AshSource", "").EndsWith("/Ashes.prefab") && atmosphere.anchorAshToVillage);
            Verify("Synthetic ground haze and ash layers stay disabled", atmosphere.groundHaze.Length == 0 &&
                !atmosphere.transform.Find("Airborne ash flecks").gameObject.activeSelf && Enumerable.Range(0, 3).All(i => !atmosphere.transform.Find("Low ground haze " + i).gameObject.activeSelf));
            effects.ResetEffects();
            atmosphere.TickAtmosphere(.1f);
            Verify("Preparation starts without approaching clouds or fallout", effects.driftingAshClouds.All(c => c.particles.particleCount == 0 && !c.particles.isPlaying) && atmosphere.AshBlend == 0 && atmosphere.fallingAsh.particleCount == 0);
            Capture("01-before-eruption");
            sequence.BeginEruption(true); sequence.TickSequence(sequence.warningSeconds * .5f);
            Verify("The warning does not start the dust cloud early", !effects.IsErupting && effects.driftingAshClouds.All(c => c.particles.particleCount == 0));
            while (sequence.Stage == VolcanoEruptionSequence.EruptionStage.Warning) sequence.TickSequence(.1f);
            AdvanceTo(4);
            Verify("Clouds wait for the eruption and their own short delay", effects.IsErupting && effects.driftingAshClouds.All(c => c.particles.particleCount == 0));
            AdvanceTo(28);
            var first = effects.driftingAshClouds[0];
            Verify("Clouds gradually move toward the village and descend", effects.driftingAshClouds.All(c =>
                Vector3.Distance(c.particles.transform.position, c.villagePosition) < Vector3.Distance(c.eruptionPosition, c.villagePosition) &&
                c.particles.transform.position.y < c.eruptionPosition.y && c.particles.transform.position.z < c.eruptionPosition.z));
            Verify("The existing smoke texture forms live rolling clouds", effects.driftingAshClouds.All(c => c.particles.particleCount > 0 && c.particles.main.simulationSpace == ParticleSystemSimulationSpace.Local));
            Verify("Fallout does not appear instantly over the village", atmosphere.AshBlend < .01f && atmosphere.fallingAsh.particleCount == 0);
            Capture("02-clouds-approach-from-volcano");
            AdvanceTo(65);
            Verify("Ashfall builds as the front approaches town", atmosphere.AshBlend > .3f && atmosphere.fallingAsh.particleCount > 0 && effects.AshArrival01 > .5f,
                "blend=" + atmosphere.AshBlend + " particles=" + atmosphere.fallingAsh.particleCount + " front=" + first.particles.transform.position);
            Capture("03-clouds-descend-over-village");
            AdvanceTo(112);
            Verify("All cloud banks settle over the village after their gradual journey", effects.driftingAshClouds.All(c => Vector3.Distance(c.particles.transform.position, c.villagePosition) < .01f) && atmosphere.AshBlend > .99f);
            Verify("Cloud particle budgets stay bounded", effects.driftingAshClouds.All(c => c.particles.particleCount <= 64) && atmosphere.fallingAsh.particleCount <= 900);
            VerifyAshDescent();
            camera.transform.position = atmosphere.villageAshOrigin + new Vector3(30, -40, -155);
            camera.transform.LookAt(atmosphere.villageAshOrigin + new Vector3(0, 15, 30)); camera.fieldOfView = 62;
            Capture("04-village-under-existing-ash-clouds");
            Vector3 pausedPosition = first.particles.transform.position; float pausedTime = effects.Elapsed, pausedBlend = atmosphere.AshBlend;
            flow.Pause(); sequence.SendMessage("Update"); atmosphere.TickAtmosphere(1);
            Verify("Pause freezes cloud travel and weather", Time.timeScale == 0 && effects.Elapsed == pausedTime && first.particles.transform.position == pausedPosition && atmosphere.AshBlend == pausedBlend);
            flow.Resume();
            effects.ResetEffects();
            Verify("Reset clears and rewinds every existing cloud", effects.AshArrival01 == 0 && effects.driftingAshClouds.All(c => c.particles.particleCount == 0 && c.particles.transform.position == c.eruptionPosition));
            effects.BeginEruption(); effects.TickEffect(40); var longFrame = effects.driftingAshClouds.Select(c => c.particles.transform.position).ToArray();
            effects.ResetEffects(); effects.BeginEruption(); for (int i = 0; i < 400; i++) effects.TickEffect(.1f);
            Verify("Cloud travel is independent of frame partitioning", effects.driftingAshClouds.Select((c, i) => Vector3.Distance(c.particles.transform.position, longFrame[i])).All(d => d < .02f));
        }
        catch (Exception ex) { report.errors.Add(ex.ToString()); }
        finally
        {
            report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
            Save(); Application.logMessageReceived -= Log; Application.runInBackground = background; Time.timeScale = oldScale;
            EditorApplication.ExitPlaymode();
        }
    }
    static void AdvanceTo(float seconds)
    {
        var sources = new[] { effects.eruptionBurst, effects.lavaBombs, effects.smokePlume, effects.ashCrown, effects.fireFountain };
        while (effects.Elapsed + .0001f < seconds)
        {
            sequence.TickSequence(.1f); atmosphere.TickAtmosphere(.1f);
            foreach (var source in sources) if (source) source.Simulate(.1f, true, false, false);
            foreach (var cloud in effects.driftingAshClouds)
                if (effects.Elapsed >= cloud.delay) cloud.particles.Simulate(.1f, true, false, false);
            if (atmosphere.AshBlend > 0) atmosphere.fallingAsh.Simulate(.1f, true, false, false);
        }
    }
    static void VerifyAshDescent()
    {
        var ash = atmosphere.fallingAsh; var before = new ParticleSystem.Particle[ash.main.maxParticles];
        int count = ash.GetParticles(before); var bySeed = before.Take(count).Where(p => p.position.y > 15 && p.remainingLifetime > 3).GroupBy(p => p.randomSeed).ToDictionary(g => g.Key, g => g.First());
        var terrain = Terrain.activeTerrain;
        Verify("Native ash reaches ground level in the village", terrain && before.Take(count).Any(p =>
            p.position.y - terrain.SampleHeight(p.position) - terrain.transform.position.y < 3 && p.position.y >= terrain.transform.position.y - .1f));
        ash.Simulate(1, false, false, false);
        var after = new ParticleSystem.Particle[ash.main.maxParticles]; int afterCount = ash.GetParticles(after);
        var drops = after.Take(afterCount).Where(p => bySeed.ContainsKey(p.randomSeed)).Select(p => bySeed[p.randomSeed].position.y - p.position.y).ToArray();
        Verify("Native ash particles actually descend toward the ground", drops.Length > 10 && drops.Average() > 1.5f,
            "tracked=" + drops.Length + " meanDrop=" + (drops.Length > 0 ? drops.Average() : 0) + " before=" + count + " after=" + afterCount);
    }
    static void SetWideView()
    {
        camera.transform.position = new Vector3(420, 210, -930); camera.transform.LookAt(new Vector3(10, 180, -300));
    }
    static void Capture(string name)
    {
        var rt = new RenderTexture(1440, 810, 24); var previous = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        File.WriteAllBytes(Folder + "/" + name + ".png", image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous; Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
    }
    static void Verify(string name, bool passed, string evidence = "") { report.checks.Add(new Check { name = name, passed = passed, evidence = evidence }); Save(); }
    static void Save() => File.WriteAllText(Folder + "/report.json", JsonUtility.ToJson(report, true));
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { report.errors.Add(message + "\n" + stack); Save(); }
    }
}
