using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Real-frame story integration, using the existing interactions, residents and water pump.</summary>
[InitializeOnLoad]
public static class VolcanoStoryChecks
{
    const string Pending = "GameJam.VolcanoStory.Pending";
    static readonly List<string> report = new List<string>();
    static VolcanoMissionDirector mission;
    static VolcanoStorySequence story;
    static FireRescuePlayer player;
    static RescueHUD hud;
    static ObjectiveNavigation navigation;
    static VolcanoStoryInputFixture fixture;
    static NavMeshAgent leader;
    static EditorWindow game;
    static int phase = -1, frame, originalMission;
    static float phaseAt, previousTimer, maximumSpeed;
    static Vector3 start, previousPosition;
    static float walked;
    static bool oldBackground, oldMaximized, sawFollowing;
    static double deadline;
    static string error;
    static InputSettings.BackgroundBehavior oldInputBackground;

    static VolcanoStoryChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted.");
        };
    }
    [MenuItem("GameJam/Story/Run scenario integration checks")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var authored = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (!authored || !authored.story) throw new InvalidOperationException("Apply the volcano story first.");
        SessionState.SetBool(Pending + ".Background", PlayerSettings.runInBackground);
        Application.runInBackground = true;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.isPaused = false;
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        if (phase >= 0) return;
        report.Clear(); error = null; walked = 0; sawFollowing = false;
        report.Add("Real-frame scenario integration. Synthetic Input System E/Q events exercise player interactions. The authored window walk and resident shelter traversal run physically; the player is repositioned between interaction fixtures and the truck is repositioned onto route checkpoints. A complete manually driven lake route and recorded speech are not claimed.");
        Bind(); originalMission = mission.GetInstanceID(); start = previousPosition = player.transform.position;
        oldBackground = SessionState.GetBool(Pending + ".Background", false); Application.runInBackground = true;
        oldInputBackground = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); oldMaximized = game.maximized; game.Focus(); game.maximized = true;
        fixture = player.gameObject.AddComponent<VolcanoStoryInputFixture>(); fixture.player = player;
        Application.logMessageReceived += Log;
        File.WriteAllLines(VolcanoStorySetup.Folder + "/checks.txt", report); Next(0, 30);
    }
    static void Bind()
    {
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); story = mission.story; player = mission.player;
        hud = Object.FindFirstObjectByType<RescueHUD>(); navigation = Object.FindFirstObjectByType<ObjectiveNavigation>();
    }
    static void Update()
    {
        if (phase < 0 && EditorApplication.isPlaying && !EditorApplication.isCompiling && SessionState.GetBool(Pending, false))
        { SessionState.SetBool(Pending, false); Start(); }
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (error != null) throw new InvalidOperationException(error);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("phase=" + phase + "; " + Snapshot());
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
                if (!story.IsOpening) return;
                Check("wake-up hands control to the window scene", mission.Phase == VolcanoMissionDirector.MissionPhase.Window && player.CinematicLocked && !mission.cinematic.IsPlaying);
                Check("story reuses existing smoke and ember materials without eruption references", story.omenSmoke != mission.sequence.effects.smokePlume && story.omenSparks != mission.sequence.effects.lavaBombs &&
                    story.omenSmoke.GetComponent<Renderer>().sharedMaterial == mission.sequence.effects.smokePlume.GetComponent<Renderer>().sharedMaterial);
                Check("original preparation and interaction parameters remain intact", mission.preparationSeconds == 120 && mission.tank.startingTankLitres == 0 && mission.evacuation.callKey == Key.Q && mission.evacuation.shoutRadius == 58 && mission.evacuation.villageWideCall);
                Next(1, 25); return;
            case 1:
                walked += Vector3.Distance(player.transform.position, previousPosition); previousPosition = player.transform.position;
                if (!story.AtWindow) return;
                Check("physical walk reaches the bedroom window without a teleport", walked > 6 && Vector3.Distance(start, player.transform.position) > 6 &&
                    EvacuationResident.FlatDistance(player.transform.position, story.windowStop.position) <= story.arrivalRadius + .01f && story.OwnsMovement, Snapshot());
                Check("window observation does not consume the 120 second preparation", Mathf.Abs(mission.PreparationRemaining - 120f) < .001f && !mission.evacuation.IsActive && !mission.villageAlarm.Activated);
                Next(2); return;
            case 2:
                if (!story.OmenPlayed || story.ObservationElapsed < story.omenDelay + .35f) return;
                Check("only the small omen particles fire", story.omenSmoke.particleCount > 0 && story.omenSparks.particleCount > 0 && !mission.sequence.effects.IsErupting && mission.sequence.Stage == VolcanoEruptionSequence.EruptionStage.Waiting);
                Check("inner thoughts use the existing readable subtitle area", hud.subtitleText.transform.parent.gameObject.activeSelf && hud.subtitleText.font == story.subtitleFont && hud.subtitleText.text.Contains("kıvılcım"));
                if (!fixture.PhotoReady("01-window-omen.png")) return;
                Next(3); return;
            case 3:
                if (story.CurrentThought != VolcanoStorySequence.Thought.Foreseen) return;
                Check("recognition follows the smoke and spark", story.HasSpoken(VolcanoStorySequence.Thought.Unease) && story.HasSpoken(VolcanoStorySequence.Thought.Omen) && story.CurrentLine.Contains("Bu zamanın geleceğini biliyordum"));
                if (!fixture.PhotoReady("02-window-recognition.png")) return;
                Next(4); return;
            case 4:
                if (mission.State != VolcanoMissionDirector.FlowState.Preparation || !story.OpeningComplete || player.CinematicLocked) return;
                Check("opening releases movement and retains the weapon objective", mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon && !player.HasPistol && player.viewCamera.enabled && mission.PreparationRemaining > 119);
                fixture.focus = mission.weaponPickup.transform; player.PlaceOnFoot(mission.standingSpawn.position, mission.standingSpawn.eulerAngles.y); Next(5); return;
            case 5:
                if (age < .5f) return;
                KeyDown(player.interactKey); Next(6); return;
            case 6:
                if (!player.HasPistol) return;
                KeyUp(); Check("existing E pickup still equips slot one", mission.weaponPickup.IsCollected && player.pistol.Equipped && mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage);
                fixture.focus = mission.villageAlarm.contactPoint;
                // The old approach marker overlaps the table's expanded character capsule.
                // Approach its open left edge, as a player would when the prompt says to get closer.
                player.PlaceOnFoot(new Vector3(25.45f, mission.standingSpawn.position.y, -599.5f), 90f); Next(7); return;
            case 7:
                if (age < .5f) return;
                player.contextualInteractions.RefreshSelection();
                Check("existing alarm remains physically reachable after the story", mission.villageAlarm.CanPress, Snapshot());
                KeyDown(player.interactKey); Next(8); return;
            case 8:
                if (!mission.villageAlarm.IsPressing) return;
                KeyUp(); Next(9); return;
            case 9:
                if (!mission.villageAlarm.Activated || mission.villageAlarm.IsPressing) return;
                Check("native palm strike and siren start evacuation once", mission.villageAlarm.StrikeCount == 1 && mission.villageAlarm.AlarmStartCount == 1 && mission.WarningCompleted && mission.evacuation.IsActive);
                fixture.focus = null;
                player.PlaceOnFoot(mission.houseExit.position + mission.houseExit.forward * 3, mission.houseExit.eulerAngles.y);
                Next(10); return;
            case 10:
                if (age < .4f) return;
                KeyDown(Key.Q); Next(11); return;
            case 11:
                if (mission.evacuation.CalledCount != mission.evacuation.TotalCount) return;
                KeyUp();
                Check("one real Q event calls all seven residents", mission.evacuation.TotalCount == 7 && mission.evacuation.CalledCount == 7 && mission.evacuation.residents.All(r => r.IsCalled && !r.IsSafe));
                Check("shelter thought follows the actual siren", story.HasSpoken(VolcanoStorySequence.Thought.Shelter));
                player.enabled = false; player.firstPerson.enabled = false; player.playerBody.enabled = false;
                leader = player.gameObject.AddComponent<NavMeshAgent>(); leader.speed = 8; leader.acceleration = 28; leader.angularSpeed = 720;
                leader.radius = .28f; leader.height = 1.8f; leader.avoidancePriority = 1; leader.stoppingDistance = 1f;
                if (!NavMesh.SamplePosition(player.transform.position, out var mesh, 5, NavMesh.AllAreas)) throw new InvalidOperationException("Leader fixture has no exterior NavMesh.");
                leader.Warp(mesh.position); leader.SetDestination(mission.evacuation.shelterApproach.position);
                Next(12, 100); return;
            case 12:
                sawFollowing |= mission.evacuation.residents.Any(r => r.State == EvacuationResident.ResidentState.Following);
                if (!mission.evacuation.IsComplete) return;
                Check("existing agents follow the route and enter shelter", sawFollowing && mission.evacuation.RescuedCount == 7 && mission.evacuation.residents.All(r => r.IsSafe && mission.evacuation.IsInsideShelter(r.transform.position)), Snapshot());
                if (mission.Phase != VolcanoMissionDirector.MissionPhase.Truck || !story.HasSpoken(VolcanoStorySequence.Thought.Truck)) return;
                Check("shelter completion points to the existing truck", navigation.CurrentTarget == player.driverDoor);
                if (!fixture.PhotoReady("03-shelter-truck.png")) return;
                Object.DestroyImmediate(leader); leader = null;
                player.enabled = true; player.firstPerson.enabled = true; player.playerBody.enabled = true;
                player.PlaceOnFoot(player.driverDoor.position - Vector3.up * .9f, player.drive.transform.eulerAngles.y);
                fixture.focus = player.driverDoor; Next(13); return;
            case 13:
                if (age < .5f) return;
                KeyDown(player.interactKey); Next(14); return;
            case 14:
                if (!player.IsDriving) return;
                KeyUp(); Check("existing truck entry advances to the lake", mission.Phase == VolcanoMissionDirector.MissionPhase.Refill);
                fixture.focus = null; maximumSpeed = 0; player.enabled = false; Next(15); return;
            case 15:
                player.drive.SetInput(.8f, 0, false); maximumSpeed = Mathf.Max(maximumSpeed, player.drive.SpeedKph);
                if (age < 3) return;
                Check("truck still accelerates on its existing wheel physics", maximumSpeed > 4, maximumSpeed.ToString("F2") + " km/h");
                player.drive.SetInput(0, 0, true); Next(16); return;
            case 16:
                player.drive.SetInput(0, 0, true);
                if (player.drive.SpeedKph > 2 && age < 5) return;
                PlaceTruck(mission.fillStation.transform.position + Vector3.up * .7f, Quaternion.Euler(0, 170.54f, 0));
                player.enabled = true; player.SetDriving(true); Next(17); return;
            case 17:
                if (age < 1f) return;
                Check("lake connection still requires the submerged, reachable intake", mission.fillStation.CanFill && mission.fillStation.water.ContainsIntake(mission.fillStation.intakePoint.position));
                KeyDown(player.interactKey); Next(18); return;
            case 18:
                if (!mission.fillStation.IsFilling) return;
                KeyUp(); Check("existing E starts the physical hose and pump", player.hose.IsDeployed && player.hose.IsPumping);
                if (!fixture.PhotoReady("04-lake-filling.png")) return;
                Next(19, 25); return;
            case 19:
                if (player.hose.Tank01 < .999f || mission.fillStation.IsFilling || !mission.PreparationComplete) return;
                Check("real water filling completes and retrieves the intake", !player.hose.IsDeployed && player.hose.TankLitres >= 1998 && mission.Phase == VolcanoMissionDirector.MissionPhase.Ready);
                if (story.CurrentThought != VolcanoStorySequence.Thought.Return) return;
                Check("full tank triggers the return thought", story.CurrentLine.Contains("Lavın geleceği tarafta hazır beklemeliyim"));
                if (!fixture.PhotoReady("05-return-guidance.png")) return;
                previousTimer = mission.PreparationRemaining; player.gameFlow.Pause(); Next(20); return;
            case 20:
                if (age < .35f) return;
                Check("pause freezes the mission countdown and hides inner thoughts", Mathf.Abs(previousTimer - mission.PreparationRemaining) < .001f && !hud.subtitleText.transform.parent.gameObject.activeSelf);
                player.gameFlow.Resume(); Next(21); return;
            case 21:
                if (age < .4f) return; // Let Rigidbody interpolation and navigation observe each checkpoint.
                if (navigation.lakeTurnaround.Contains(navigation.CurrentTarget))
                {
                    var waypoint = navigation.CurrentTarget;
                    if (waypoint) PlaceTruck(waypoint.position + Vector3.up * .7f, player.drive.transform.rotation);
                    phaseAt = Time.unscaledTime;
                    return;
                }
                PlaceTruck(navigation.lakeRoad[navigation.lakeRoad.Length - 1].position + Vector3.up * .7f, player.drive.transform.rotation);
                Next(22); return;
            case 22:
                if (age < .3f) return;
                Check("original turnaround and road guidance resolve to the new defense point", navigation.CurrentTarget == story.defensePosition, "target=" + navigation.CurrentTarget);
                PlaceTruck(story.defensePosition.position + Vector3.up * .7f, story.defensePosition.rotation); Next(23); return;
            case 23:
                if (age < .3f || !story.HasSpoken(VolcanoStorySequence.Thought.Hold)) return;
                Check("defense marker faces the incoming volcano side", Vector3.Dot(story.defensePosition.forward, (story.volcanoLookTarget.position - story.defensePosition.position).normalized) > .8f);
                mission.TriggerDeadline();
                Check("original independent eruption still starts its cinematic", mission.EruptionTriggered && mission.cinematic.IsPlaying && player.CinematicLocked && story.CurrentLine == "" && !story.omenSmoke.IsAlive());
                Next(24, 25); return;
            case 24:
                if (mission.cinematic.IsPlaying) return;
                Check("eruption restores control without a story input lock", !player.CinematicLocked && player.viewCamera.enabled && mission.sequence.effects.IsErupting);
                player.gameFlow.RestartMission(); Next(25, 45); return;
            case 25:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.GetInstanceID() == originalMission || !fresh.story.AtWindow) return;
                Bind();
                Check("restart resets all story beats, siren, residents and water", story.IsOpening && !story.OmenPlayed && !mission.villageAlarm.Activated && mission.evacuation.CalledCount == 0 && mission.tank.Tank01 == 0 && !player.HasPistol && mission.PreparationRemaining == 120);
                Finish(null); return;
        }
    }
    static void PlaceTruck(Vector3 position, Quaternion rotation)
    {
        var body = player.drive.body; body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.position = position; body.rotation = rotation; Physics.SyncTransforms();
    }
    static void KeyDown(Key key) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
    static void KeyUp() => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
    static string Snapshot() => "phase=" + (mission ? mission.Phase.ToString() : "none") + "; player=" + (player ? player.transform.position.ToString("F3") : "none") +
        "; window=" + (story ? story.AtWindow.ToString() : "none") + "; safe=" + (mission ? mission.evacuation.RescuedCount.ToString() : "none") + "; focused=" + Application.isFocused +
        (mission && mission.villageAlarm ? "; alarmReach=" + mission.villageAlarm.InReach + "; alarmCanPress=" + mission.villageAlarm.CanPress +
        "; alarmDistance=" + Vector3.Distance(player.viewCamera.transform.position, mission.villageAlarm.contactPoint.position).ToString("F3") +
        "; armReach=" + mission.villageAlarm.hands.CanReachButton(mission.villageAlarm.contactPoint.position) +
        "; target=" + player.contextualInteractions.CurrentTarget + "; offer=" + player.contextualInteractions.CurrentOffer.detail : "");
    static void Check(string label, bool passed, string detail = "")
    {
        report.Add((passed ? "PASS " : "FAIL ") + label + " " + detail); File.WriteAllLines(VolcanoStorySetup.Folder + "/checks.txt", report);
        if (!passed) throw new InvalidOperationException(label + " " + detail);
    }
    static void Next(int value, float seconds = 35)
    {
        phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + seconds;
        File.WriteAllText(VolcanoStorySetup.Folder + "/progress.txt", "phase=" + phase + "; " + Snapshot());
    }
    static void Log(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = condition + "\n" + stack; }
    static void Finish(string failure)
    {
        if (phase < 0) return;
        phase = -1; Application.logMessageReceived -= Log;
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        InputSystem.settings.backgroundBehavior = oldInputBackground; Application.runInBackground = oldBackground; PlayerSettings.runInBackground = oldBackground;
        if (game) game.maximized = oldMaximized;
        report.Add(failure == null ? "COMPLETED" : "FAILED " + failure); File.WriteAllLines(VolcanoStorySetup.Folder + "/checks.txt", report);
        Time.timeScale = 1; EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(-100)]
public sealed class VolcanoStoryInputFixture : MonoBehaviour
{
    public FireRescuePlayer player;
    public Transform focus;
    string requested, completed;
    static readonly FieldInfo Yaw = typeof(FireRescuePlayer).GetField("lookYaw", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo Pitch = typeof(FireRescuePlayer).GetField("lookPitch", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo Focused = typeof(FireRescuePlayer).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic);
    void Start() => StartCoroutine(Photos());
    void Update()
    {
        if (!player || player.GameplaySuspended) return;
        Focused.SetValue(player, true); Cursor.lockState = CursorLockMode.Locked;
        if (focus)
        {
            Vector3 delta = focus.position - (player.transform.position + Vector3.up * player.eyeHeight);
            Yaw.SetValue(player, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
            Pitch.SetValue(player, -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg);
        }
        if (Mouse.current != null) InputSystem.ResetDevice(Mouse.current);
    }
    public bool PhotoReady(string name) { if (completed == name) return true; requested = name; return false; }
    IEnumerator Photos()
    {
        while (true)
        {
            yield return new WaitForEndOfFrame();
            if (requested == null) continue;
            var image = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(VolcanoStorySetup.Folder + "/" + requested, image.EncodeToPNG()); Object.Destroy(image);
            if (requested == "01-window-omen.png")
            {
                var story = player.mission.story;
                var details = new List<string> { "eye=" + player.viewCamera.transform.position + " rotation=" + player.viewCamera.transform.eulerAngles };
                foreach (var particles in new[] { story.omenSmoke, story.omenSparks })
                {
                    var live = new ParticleSystem.Particle[24]; int count = particles.GetParticles(live);
                    details.Add(particles.name + " count=" + count + " visible=" + particles.GetComponent<Renderer>().isVisible + " source=" + particles.transform.position + " viewport=" + player.viewCamera.WorldToViewportPoint(particles.transform.position));
                    for (int i = 0; i < count; i++) details.Add("particle=" + live[i].position + " viewport=" + player.viewCamera.WorldToViewportPoint(live[i].position));
                }
                File.WriteAllLines(VolcanoStorySetup.Folder + "/omen-projection.txt", details);
            }
            completed = requested; requested = null;
        }
    }
}
