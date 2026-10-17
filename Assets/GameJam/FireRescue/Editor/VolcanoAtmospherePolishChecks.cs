using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ashfall.MainMenu;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Real-frame review of the saved eruption, control handoff and scene fade boundaries.</summary>
[InitializeOnLoad]
public static class VolcanoAtmospherePolishChecks
{
    const string Pending = "GameJam.AtmospherePolish.Pending", Folder = "Logs/VolcanoPolish";
    static readonly List<string> lines = new List<string>();
    static int phase = -1, frame, dustBefore, oldMissionId, movementSamples;
    static float phaseAt, pausedIntensity, fixedStep;
    static double deadline;
    static bool oldBackground, oldMaximized;
    static bool sawDust, sawSpeed;
    static string runtimeError;
    static EditorWindow game;
    static VolcanoMissionDirector mission;
    static FireRescuePlayer player;
    static VolcanoAtmospherePolish atmosphere;
    static PlayerPolishEffects feel;
    static AshfallSceneFade fade;
    static VolcanoPolishMovementDriver driver;
    static Transform spiritSubject;

    static VolcanoAtmospherePolishChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Review interrupted.");
        };
        EditorApplication.update += Update;
    }
    [MenuItem("GameJam/Final Loop/Check eruption polish")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Open the saved Volcano_Demo in Edit Mode first.");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        Directory.CreateDirectory(Folder); lines.Clear(); runtimeError = null; frame = -1;
        lines.Add("LIMITATION: saved scene and real frame simulation; deadline triggered through the director API. Encounter temporarily disabled to observe the full aftershock. Movement, pickup, vehicle entry and scene loading use existing APIs. No manual full driving route or 120-second wait in this polish review.");
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        oldMaximized = game.maximized; game.Focus(); game.maximized = true;
        Application.logMessageReceived += Log; Bind(); fixedStep = Time.fixedDeltaTime; Next(0);
    }
    static void Bind()
    {
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); player = mission.player;
        atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>(); feel = player.GetComponent<PlayerPolishEffects>();
        fade = Object.FindFirstObjectByType<AshfallSceneFade>();
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Review phase " + phase);
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.unscaledTime - phaseAt;
        switch (phase)
        {
            case 0:
                if (mission.cinematic.IsPlaying || mission.State != VolcanoMissionDirector.FlowState.Preparation || age < 3) return;
                Check("fresh fade opens without a stuck overlay", fade && fade.curtain.alpha < .01f && fade.canvas.sortingOrder == 49);
                Check("native eruption layers and camera references survive saving", mission.sequence.effects.ashCrown && mission.sequence.effects.smokePlume && mission.sequence.cameraShake == player.viewCamera.GetComponent<VolcanoCameraShake>() && mission.cinematic.aftermathShake == mission.sequence.cameraShake);
                Check("post effects own runtime profiles", atmosphere && atmosphere.atmosphereVolume.profile != atmosphere.atmosphereVolume.sharedProfile && atmosphere.cinematicVolume.profile != atmosphere.cinematicVolume.sharedProfile && atmosphere.cinematicVolume.weight == 0);
                Check("bedside E pickup plays one bounded sparkle", player.Interact() && player.HasPistol && feel.PickupBursts == 1 && feel.pickupSparkles.particleCount > 0 && !mission.weaponPickup.TryPickUp());
                player.enabled = false;
                player.PlaceOnFoot(Ground(50, -570) + Vector3.up * .2f, 90);
                driver = player.gameObject.AddComponent<VolcanoPolishMovementDriver>(); driver.player = player; driver.move = Vector2.up;
                movementSamples = 0; sawDust = sawSpeed = false;
                Next(1); return;
            case 1:
                movementSamples++; sawDust |= feel.DustBursts > 0 && feel.movementDust.particleCount > 0; sawSpeed |= atmosphere.SpeedAmount > .3f;
                if (age < 1.3f || movementSamples < 12) return;
                lines.Add($"SPRINT frames={movementSamples} dust={feel.DustBursts} liveDustObserved={sawDust} speedLensObserved={sawSpeed}");
                Check("sprinting on terrain emits foot dust", sawDust && sawSpeed);
                dustBefore = feel.DustBursts; driver.move = Vector2.zero;
                // Land on the same open terrain used for the first footsteps, away from house roofs.
                player.PlaceOnFoot(Ground(50, -570) + Vector3.up * 7, 90);
                Next(2); return;
            case 2:
                if (age < 1.5f || !player.firstPerson.IsGrounded) return;
                lines.Add($"LANDING point={player.transform.position} dust={dustBefore}->{feel.DustBursts}");
                Check("landing emits terrain dust", feel.DustBursts > dustBefore);
                Object.Destroy(driver); driver = null; player.enabled = true;
                mission.encounter.enabled = false;
                mission.TriggerDeadline(); Next(3); return;
            case 3:
                if (mission.cinematic.Elapsed < 3f) return;
                Check("eruption freezes foot control while native layers play", player.CinematicLocked && !player.IsDriving && mission.sequence.effects.IsErupting && mission.sequence.effects.smokePlume.particleCount > 0 && mission.sequence.effects.ashCrown.particleCount > 0);
                Check("cinematic focus tracks the crater plume", atmosphere.cinematicVolume.weight > .9f && atmosphere.cinematicVolume.profile.TryGet<DepthOfField>(out var dof) && Mathf.Abs(dof.focusDistance.value - Vector3.Distance(mission.cinematic.ShotCamera.transform.position, mission.cinematic.FocusPosition)) < 10);
                Capture("01-eruption-onset", mission.cinematic.ShotCamera); Next(4); return;
            case 4:
                if (mission.cinematic.Elapsed < 5.5f) return;
                Capture("02-eruption-column", mission.cinematic.ShotCamera);
                Check("eruption is one shot", !mission.sequence.BeginEruption(true) && mission.PreparationRemaining == 0);
                float before = mission.sequence.effects.Elapsed; mission.sequence.effects.BeginEruption();
                Check("duplicate VFX trigger preserves elapsed time", Mathf.Abs(mission.sequence.effects.Elapsed - before) < .001f);
                Next(5); return;
            case 5:
                if (mission.cinematic.IsPlaying) return;
                // Observe at least one complete gameplay frame after the handoff.
                Next(6); return;
            case 6:
                if (age < .1f) return;
                Check("foot controls and sharp gameplay camera return", !player.CinematicLocked && !player.GameplaySuspended && player.viewCamera.enabled && Time.timeScale == 1 && atmosphere.cinematicVolume.weight == 0);
                Check("fresh aftershock continues on gameplay camera", mission.sequence.cameraShake.IsShaking && mission.sequence.cameraShake.Intensity > .07f);
                Capture("03-gameplay-aftershock", player.viewCamera); Next(7); return;
            case 7:
                if (age < 1) return;
                Check("aftershock decays instead of stopping at cinematic end", mission.sequence.cameraShake.Intensity > .03f && mission.sequence.cameraShake.Intensity < mission.cinematic.aftershockStrength);
                player.gameFlow.Pause(); pausedIntensity = mission.sequence.cameraShake.Intensity; Next(8); return;
            case 8:
                if (age < .4f) return;
                Check("pause holds the aftershock envelope", Time.timeScale == 0 && Mathf.Abs(pausedIntensity - mission.sequence.cameraShake.Intensity) < .001f);
                player.gameFlow.Resume(); Next(9); return;
            case 9:
                if (mission.sequence.cameraShake.IsShaking || atmosphere.AshBlend < .95f) return;
                Check("aftershock finishes cleanly", !mission.sequence.cameraShake.IsShaking && mission.sequence.cameraShake.Intensity == 0);
                lines.Add($"WEATHER blend={atmosphere.AshBlend:F2} ash={atmosphere.fallingAsh.particleCount}/{atmosphere.fallingAsh.main.maxParticles} fog={RenderSettings.fogMode} start={RenderSettings.fogStartDistance:F1}");
                Check("ash weather stays bounded and near gameplay stays readable", atmosphere.AshBlend > .95f && atmosphere.fallingAsh.particleCount > 0 && atmosphere.fallingAsh.particleCount <= atmosphere.fallingAsh.main.maxParticles && RenderSettings.fogMode == FogMode.Linear && RenderSettings.fogStartDistance >= 90);
                Check("pickup particles finish", feel.pickupSparkles.particleCount == 0);
                oldMissionId = mission.GetInstanceID(); player.gameFlow.RestartMission(); Next(10); return;
            case 10:
                if (age < .15f) return;
                if (mission) Check("restart fades before loading", fade.curtain.alpha > .1f && player.GameplaySuspended);
                Next(11); return;
            case 11:
                var reloaded = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!reloaded || reloaded.GetInstanceID() == oldMissionId || reloaded.cinematic.IsPlaying || reloaded.State != VolcanoMissionDirector.FlowState.Preparation) return;
                Bind();
                Check("actual restart clears eruption, ash, pickup and aftershock", !mission.EruptionTriggered && !mission.sequence.effects.IsErupting && atmosphere.fallingAsh.particleCount == 0 && !mission.sequence.cameraShake.IsShaking && !player.HasPistol && mission.PreparationRemaining > 115 && Time.timeScale == 1);
                Check("restart fade opens", fade.curtain.alpha < .01f);
                player.PlaceOnFoot(player.driverDoor.position - Vector3.up * .9f, player.drive.transform.eulerAngles.y); player.RefreshEquipmentState();
                Check("actual E-equivalent truck entry still works", player.EnterVehicle()); Next(12); return;
            case 12:
                if (age < .8f) return;
                mission.encounter.enabled = false; mission.TriggerDeadline();
                Check("truck freezes during eruption", player.IsDriving && player.CinematicLocked && player.drive.body.isKinematic);
                Next(13); return;
            case 13:
                if (mission.cinematic.IsPlaying) return;
                Next(14); return;
            case 14:
                if (age < .15f) return;
                Check("truck physics and controls return with aftershock", player.IsDriving && !player.CinematicLocked && !player.GameplaySuspended && !player.drive.body.isKinematic && mission.sequence.cameraShake.Intensity > .07f);
                // A presentation-only stand-in isolates the existing reveal camera/time lease from enemy AI.
                spiritSubject = new GameObject("Temporary spirit reveal focus").transform;
                spiritSubject.position = player.drive.transform.position + player.drive.transform.forward * 25;
                mission.cinematic.BeginSpirits(spiritSubject); Next(15); return;
            case 15:
                if (age < .6f) return;
                Check("spirit reveal owns the expected slow motion", mission.cinematic.IsPlaying && Mathf.Abs(Time.timeScale - mission.cinematic.spiritTimeScale) < .001f && Mathf.Abs(Time.fixedDeltaTime - fixedStep * mission.cinematic.spiritTimeScale) < .001f && player.drive.body.isKinematic);
                Next(16); return;
            case 16:
                if (mission.cinematic.IsPlaying) return;
                Next(17); return;
            case 17:
                if (age < .1f) return;
                Check("spirit reveal restores time, physics and gameplay lens", Time.timeScale == 1 && Mathf.Abs(Time.fixedDeltaTime - fixedStep) < .0001f && !player.CinematicLocked && !player.drive.body.isKinematic && atmosphere.cinematicVolume.weight == 0);
                Object.Destroy(spiritSubject.gameObject); spiritSubject = null;
                player.gameFlow.FinishMission(false); Next(18); return;
            case 18:
                if (age < .7f) return;
                Check("defeat has a stable world fade below the result UI", player.gameFlow.State == RescueGameFlow.GameState.Lost && !mission.sequence.cameraShake.IsShaking && fade.canvas.sortingOrder == 49 && fade.curtain.alpha > .35f);
                player.gameFlow.ReturnToMainMenu(); Next(19); return;
            case 19:
                if (SceneManager.GetActiveScene().name != "MainMenu") return;
                Next(21); return;
            case 21:
                var menu = Object.FindFirstObjectByType<AshfallMenuController>(); var menuFade = Object.FindFirstObjectByType<AshfallSceneFade>();
                if (age < .5f || !menuFade || menuFade.curtain.alpha > .01f) return;
                lines.Add($"MENU controller={(bool)menu} fade={(bool)menuFade} alpha={(menuFade ? menuFade.curtain.alpha : -1)} timescale={Time.timeScale} fog={RenderSettings.fog} leakedAtmosphere={(bool)Object.FindFirstObjectByType<VolcanoAtmospherePolish>()}");
                Check("main menu has no leaked game fog or time scale", menu && menuFade && Time.timeScale == 1 && !RenderSettings.fog && !Object.FindFirstObjectByType<VolcanoAtmospherePolish>() && menuFade.curtain.alpha < .01f);
                menu.NewGame(); Next(20); return;
            case 20:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.cinematic.IsPlaying || fresh.State != VolcanoMissionDirector.FlowState.Preparation) return;
                Bind(); Check("New Game fade loads a clean playable scene", Time.timeScale == 1 && !mission.EruptionTriggered && fade.curtain.alpha < .01f && !player.GameplaySuspended);
                Check("no runtime errors or missing scripts", runtimeError == null && Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0);
                Finish(null); return;
        }
    }
    static Vector3 Ground(float x, float z)
    {
        var terrain = Terrain.activeTerrain; var p = new Vector3(x, 0, z); p.y = terrain.SampleHeight(p) + terrain.transform.position.y; return p;
    }
    static void Capture(string name, Camera camera)
    {
        var target = new RenderTexture(1600, 900, 24); var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        var active = RenderTexture.active; var previous = camera.targetTexture;
        try { camera.targetTexture = target; camera.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); pixels.Apply(); File.WriteAllBytes(Folder + "/" + name + ".png", pixels.EncodeToPNG()); }
        finally { camera.targetTexture = previous; RenderTexture.active = active; Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); }
    }
    static void Next(int value) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 40; Persist(); }
    static void Check(string label, bool passed) { lines.Add((passed ? "PASS " : "FAIL ") + label); Persist(); if (!passed) throw new InvalidOperationException(label); }
    static void Persist() => File.WriteAllLines(Folder + "/checks.txt", lines);
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = message + "\n" + stack; }
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log;
        Application.runInBackground = oldBackground; if (game) game.maximized = oldMaximized;
        Time.timeScale = 1; Time.fixedDeltaTime = fixedStep;
        lines.Add(error == null ? "COMPLETED" : "FAILED " + error); Persist();
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(-80)]
public sealed class VolcanoPolishMovementDriver : MonoBehaviour
{
    public FireRescuePlayer player;
    public Vector2 move;
    void Update() { if (player) player.firstPerson.SimulateExternalMovement(move, true, false, false, true, 0, Vector2.zero); }
}
