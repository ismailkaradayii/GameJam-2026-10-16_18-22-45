using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Saved-scene integration: real siren, automatic exits, panic, Q recruitment, walking escort, weather.</summary>
[InitializeOnLoad]
public static class AlarmEvacuationChecks
{
    const string Pending = "AlarmEvacuationChecks.Pending";
    static readonly List<string> lines = new List<string>();
    static readonly Dictionary<EvacuationResident, Vector3> previous = new Dictionary<EvacuationResident, Vector3>();
    static readonly Dictionary<EvacuationResident, Vector3> initial = new Dictionary<EvacuationResident, Vector3>();
    static EvacuationMission mission;
    static VolcanoMissionDirector director;
    static VolcanoWeatherTransition weather;
    static EvacuationResident[] residents;
    static NavMeshAgent leader;
    static Keyboard testKeyboard, oldKeyboard;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static EditorWindow game;
    static bool oldBackground, oldMaximized;
    static int phase = -1, frame = -1, wallCrossings;
    static float phaseAt, started, nextProgress, pausedBlend;
    static double deadline;
    static string error;
    static Material openingSky;
    static float openingSun;

    static AlarmEvacuationChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check alarm evacuation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use Volcano_Demo in Edit Mode.");
        if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        try
        {
            lines.Clear(); previous.Clear(); initial.Clear(); error = null; wallCrossings = 0;
            Directory.CreateDirectory(AlarmEvacuationSetup.Output + "/After");
            lines.Add("Saved-scene real-frame integration; synthetic Q and a NavMesh-driven player fixture staged outside after pressing the indoor alarm. No manual traversal of the player's room is claimed. Residents use their normal runtime agents, doors and animations.");
            mission = Object.FindFirstObjectByType<EvacuationMission>(); director = mission.eruption.missionDirector;
            weather = Object.FindFirstObjectByType<VolcanoWeatherTransition>(); residents = mission.residents;
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            oldInputBackground = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); oldMaximized = game.maximized; game.Focus(); game.maximized = true;
            Application.logMessageReceived += Log;
            // Keep the independent 120-second deadline out of this longer path/collision fixture.
            director.preparationSeconds = 10000;
            typeof(VolcanoMissionDirector).GetField("<PreparationRemaining>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, 10000f);
            started = Time.time; nextProgress = 0; Next(0);
            foreach (var resident in residents) { previous[resident] = resident.transform.position; initial[resident] = resident.transform.position; }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup > deadline || Time.time - started > 450) throw new TimeoutException(Snapshot());
            ObserveMovement();
            if (Time.unscaledTime > nextProgress)
            { nextProgress = Time.unscaledTime + 3; File.WriteAllText(AlarmEvacuationSetup.Output + "/progress.txt", Snapshot()); }
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.time - phaseAt;
        switch (phase)
        {
            case 0:
                if (director.cinematic.IsPlaying || director.State != VolcanoMissionDirector.FlowState.Preparation || age < 2) return;
                Check("seven residents wait indoors before the siren", residents.Length == 7 && residents.All(r => r.State == EvacuationResident.ResidentState.Waiting && !r.IsCalled && !r.HasHeardAlarm && Vector3.Distance(r.transform.position, initial[r]) < .1f));
                foreach (var resident in residents)
                {
                    var house = resident.door.GetComponentInParent<BurnableBuilding>();
                    var model = resident.animator.transform;
                    var hips = resident.animator.GetBoneTransform(HumanBodyBones.Hips);
                    var head = resident.animator.GetBoneTransform(HumanBodyBones.Head);
                    Check(resident.name + " visible body aligns with its indoor agent", EvacuationResident.FlatDistance(model.position, resident.transform.position) < .05f &&
                        EvacuationResident.FlatDistance(hips.position, resident.transform.position) < 1f && house.ignitionBounds.bounds.Contains(hips.position) && house.ignitionBounds.bounds.Contains(head.position));
                }
                openingSky = RenderSettings.skybox; openingSun = weather.sun.intensity;
                Check("opening stays sunny with no native cloud deck", weather.Transition01 == 0 && weather.sun.intensity > 2 && weather.sunnyFill.intensity > 1 && !weather.environment.cloudsMaterial && openingSky != weather.volcanicSky);
                CaptureTown("01-sunny-opening");
                mission.player.AcquirePistol(); director.TickMission();
                Check("siren objective follows pistol pickup", director.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage);
                mission.player.PlaceOnFoot(director.villageAlarm.approachPose.position, director.villageAlarm.approachPose.eulerAngles.y);
                // Invoke the actual palm-contact handler, not BeginMission, to verify the siren-to-crowd wiring.
                typeof(VillageAlarmButton).GetMethod("Strike", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director.villageAlarm, null);
                Check("real siren automatically starts fear without Q", director.villageAlarm.Activated && mission.IsActive && mission.CalledCount == 0 && residents.All(r => r.HasHeardAlarm && r.State == EvacuationResident.ResidentState.Panicking));
                var startingDoor = Object.FindObjectsByType<InteractiveDoor>(FindObjectsSortMode.None).OrderBy(d => Vector3.Distance(d.transform.position, director.houseExit.position)).First();
                Check("player fixture opens the real starting-house door before walking out", startingDoor.TryToggle());
                SetupLeader(); Time.timeScale = 2; Next(1); return;
            case 1:
                if (!residents.All(r => r.State == EvacuationResident.ResidentState.Scattering)) return;
                Check("all seven physically leave their doors without Q", mission.CalledCount == 0 && residents.All(r => r.door.IsOpen && r.Agent.isOnNavMesh && !r.HasNavigationProblem && EvacuationResident.FlatDistance(r.transform.position, initial[r]) > 6));
                Check("siren alone does not change the sunny weather", weather.Transition01 == 0 && weather.sun.intensity == openingSun && RenderSettings.skybox == openingSky);
                Next(2); return;
            case 2:
                if (age < 14 || !residents.All(r => r.PanicRuns >= 2)) return;
                Check("uncalled villagers keep running to changing outdoor goals", mission.CalledCount == 0 && residents.All(r => r.State == EvacuationResident.ResidentState.Scattering && r.PanicRuns >= 2));
                CaptureTown("02-alarm-panic");
                Check("panic movement does not cross solid walls", wallCrossings == 0);
                oldKeyboard = Keyboard.current; testKeyboard = InputSystem.AddDevice<Keyboard>(); testKeyboard.MakeCurrent();
                game.Focus(); Cursor.lockState = CursorLockMode.Locked;
                InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Q));
                Next(3); return;
            case 3:
                if (age < .3f) return;
                InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
                Check("one real Q input gathers all seven alarmed villagers", mission.CalledCount == 7 && residents.All(r => r.State == EvacuationResident.ResidentState.Following));
                Check("rally subtitle calls villagers to follow", mission.CurrentMessage.Contains("COME HERE"));
                Check("rally cooldown prevents repeated calls", !mission.Shout());
                leader.speed = 12;
                var escortPath = new NavMeshPath();
                Check("player fixture has a complete physical route to shelter", leader.CalculatePath(OnMesh(mission.shelterApproach.position), escortPath) && escortPath.status == NavMeshPathStatus.PathComplete);
                leader.SetPath(escortPath);
                Next(4); return;
            case 4:
                if (age > 4 && age < 4.3f) CaptureTown("03-following");
                if (leader.pathPending || leader.remainingDistance > 1 || EvacuationResident.FlatDistance(leader.transform.position, mission.shelterApproach.position) > 2) return;
                Check("escort reaches the shelter approach", mission.ShelterOpened && leader.pathStatus == NavMeshPathStatus.PathComplete);
                leader.SetDestination(OnMesh(residents[5].safePlace.position)); Next(5); return;
            case 5:
                if (!mission.IsComplete) return;
                Check("all seven follow the player and enter the real shelter", mission.RescuedCount == 7 && residents.All(r => r.IsSafe && mission.IsInsideShelter(r.transform.position)));
                int saved = mission.RescuedCount; mission.ResidentArrived(residents[0]);
                Check("safe residents cannot be counted twice", mission.RescuedCount == saved);
                Check("complete escort has no wall crossings or navigation failures", wallCrossings == 0 && residents.All(r => !r.HasNavigationProblem));
                leader.isStopped = true; Time.timeScale = 1;
                // Leave enough time to observe the weather without lava reaching the rescued town.
                mission.eruption.lavaReleaseDelay = 300;
                Check("eruption enters warning first", mission.eruption.BeginEruption(true));
                Next(6); return;
            case 6:
                if (mission.eruption.Stage == VolcanoEruptionSequence.EruptionStage.Warning)
                {
                    CheckOnce("warning keeps the sunny opening", weather.Transition01 == 0 && RenderSettings.skybox == openingSky);
                    return;
                }
                if (weather.Transition01 < .15f || director.cinematic.IsPlaying) return;
                Check("actual eruption starts the weather transition", weather.Transition01 > 0 && weather.sun.intensity < openingSun);
                pausedBlend = weather.Transition01; Time.timeScale = 0; Next(7); return;
            case 7:
                weather.TickWeather(.1f);
                Check("pause freezes the weather transition", Mathf.Abs(weather.Transition01 - pausedBlend) < .00001f);
                Time.timeScale = 1; Next(8); return;
            case 8:
                if (weather.Transition01 < .999f || age < 2) return;
                Check("post-eruption sky uses the original volcano asset", RenderSettings.skybox == weather.volcanicSky && weather.volcanicSky.name == "Sky_volcano");
                Check("native dense cloud shader and full coverage are restored", weather.environment.cloudsMaterial && weather.environment.cloudsMaterial.shader == weather.volcanicClouds.shader &&
                    Mathf.Abs(weather.environment.cloudsMaterial.GetFloat("_Coverage") - weather.volcanicClouds.GetFloat("_Coverage")) < .001f);
                Check("original volcanic sun and darker ambient replace the daylight", Mathf.Abs(weather.sun.intensity - weather.volcanicSunIntensity) < .001f && weather.sunnyFill.intensity < .001f &&
                    Quaternion.Angle(weather.sun.transform.rotation, Quaternion.Euler(weather.volcanicSunEuler)) < .1f && RenderSettings.ambientSkyColor.maxColorComponent < .3f);
                CaptureTown("04-volcanic-atmosphere");
                Check("native travelling ash system remains bound", mission.eruption.effects && mission.eruption.effects.driftingAshClouds.Length >= 3);
                Finish(null); return;
        }
    }
    static void SetupLeader()
    {
        var player = mission.player;
        // The button is next to furniture without clearance for a navigation agent. A human player
        // walks out with the CharacterController; stage only the escort fixture on the exterior path.
        player.PlaceOnFoot(director.houseExit.position, director.houseExit.eulerAngles.y);
        player.playerBody.enabled = false; player.enabled = false; player.firstPerson.enabled = false;
        leader = mission.PlayerTransform.gameObject.AddComponent<NavMeshAgent>();
        leader.radius = player.playerBody.radius; leader.height = player.playerBody.height; leader.acceleration = 40;
        leader.speed = 0; leader.stoppingDistance = .6f; leader.avoidancePriority = 1;
        leader.Warp(OnMesh(player.transform.position));
    }
    static Vector3 OnMesh(Vector3 point)
    {
        if (NavMesh.SamplePosition(point, out var hit, 8, NavMesh.AllAreas)) return hit.position;
        throw new InvalidOperationException("No NavMesh near " + point);
    }
    static void ObserveMovement()
    {
        foreach (var resident in residents)
        {
            Vector3 a = previous[resident], b = resident.transform.position; previous[resident] = b;
            if (Vector3.Distance(a, b) < .015f) continue;
            Vector3 raised = Vector3.up * resident.Agent.height * resident.transform.lossyScale.y * .55f;
            foreach (var hit in Physics.RaycastAll(a + raised, b-a, Vector3.Distance(a,b), ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.GetComponentInParent<EvacuationResident>() && !hit.collider.GetComponentInParent<FireRescuePlayer>())
                { wallCrossings++; File.AppendAllText(AlarmEvacuationSetup.Output + "/crossings.txt", resident.name + " crossed " + hit.collider.name + " " + a + " -> " + b + "\n"); }
        }
    }
    static void CaptureTown(string file)
    {
        var go = new GameObject("Alarm review camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.enabled = false;
        camera.transform.position = new Vector3(44,13,-593); camera.transform.LookAt(new Vector3(69,5,-550));
        camera.fieldOfView = 70; camera.farClipPlane = 2200; camera.allowHDR = true;
        var extra = camera.GetUniversalAdditionalCameraData(); extra.renderPostProcessing = true; extra.renderShadows = true; extra.volumeLayerMask = ~0;
        var target = new RenderTexture(1600,1000,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
        var texture = new Texture2D(1600,1000,TextureFormat.RGB24,false); var old = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,1600,1000),0,0); texture.Apply();
            File.WriteAllBytes(AlarmEvacuationSetup.Output + "/After/" + file + ".png", texture.EncodeToPNG());
        }
        finally { RenderTexture.active = old; camera.targetTexture = null; target.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(target); Object.DestroyImmediate(go); }
    }
    static string Snapshot() => "phase=" + phase + " seconds=" + (Time.time-started).ToString("F1") + " called=" + mission.CalledCount + " rescued=" + mission.RescuedCount + " weather=" + weather.Transition01 +
        (leader ? " leader=" + leader.transform.position + " path=" + leader.pathStatus + " remaining=" + leader.remainingDistance : "") + "\n" +
        string.Join("\n", residents.Select(r => r.name + " " + r.State + " " + r.transform.position + " panicRuns=" + r.PanicRuns + " path=" + r.Agent.pathStatus + " remaining=" + r.Agent.remainingDistance));
    static void Next(int value) { phase = value; phaseAt = Time.time; deadline = EditorApplication.timeSinceStartup + 150; }
    static void CheckOnce(string label, bool success) { if (!lines.Any(l => l == "PASS " + label)) Check(label,success); }
    static void Check(string label, bool success)
    {
        lines.Add((success ? "PASS " : "FAIL ") + label); File.WriteAllLines(AlarmEvacuationSetup.Output + "/checks.txt", lines);
        if (!success) throw new InvalidOperationException(label + "\n" + Snapshot());
    }
    static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = text + "\n" + stack; }
    static void Finish(string failure)
    {
        phase = -1; Application.logMessageReceived -= Log; Time.timeScale = 1;
        if (testKeyboard != null) { InputSystem.RemoveDevice(testKeyboard); testKeyboard = null; }
        if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        InputSystem.settings.backgroundBehavior = oldInputBackground; Application.runInBackground = oldBackground;
        if (game) game.maximized = oldMaximized;
        lines.Add(failure == null ? "COMPLETED" : "FAILED " + failure); File.WriteAllLines(AlarmEvacuationSetup.Output + "/checks.txt", lines);
        EditorApplication.ExitPlaymode();
    }
}
