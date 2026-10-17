using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

/// <summary>Real rendered frames and real Input System confirmation, in the existing village scene.</summary>
[InitializeOnLoad]
public static class SpiritIntroductionChecks
{
    const string Pending = "SpiritIntroductionChecks.Pending";
    [Serializable] class Result { public string name, evidence; public bool passed; }
    [Serializable] class Report { public string status = "RUNNING", error = ""; public List<Result> checks = new List<Result>(); }
    static Report report;
    static VolcanoMissionDirector mission;
    static FireRescuePlayer player;
    static VolcanoCinematic cinema;
    static ObjectiveNavigation nav;
    static LavaCreatureActor actor;
    static LavaCreatureCombat combat;
    static SpiritIntroductionCapture capture;
    static int phase = -1, frame, originalMission;
    static float phaseAt, worldTime, fixedStep, health, water;
    static Vector3 frozenPosition, entryPosition, truckVelocity, truckAngularVelocity;
    static Quaternion entryRotation;
    static float entryFov;
    static bool oldBackground, oldMaximized, previousKinematic;
    static EditorWindow game;
    static double deadline;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static string runtimeError;
    static Vector3 previousEye;
    static bool sampledEye, capturedWindow;
    static float maximumEyeSpeed;
    static int wallContacts, lensContacts;

    static SpiritIntroductionChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Application.runInBackground = true;
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Play Mode interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check spirit introduction")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        Directory.CreateDirectory(SpiritIntroductionSetup.Folder);
        File.WriteAllText(SpiritIntroductionSetup.Folder + "/report.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true);
        SessionState.SetBool(Pending + ".Background", Application.runInBackground);
        Application.runInBackground = true;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        EditorApplication.isPaused = false;
        EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        report = new Report(); runtimeError = null;
        oldBackground = SessionState.GetBool(Pending + ".Background", false); Application.runInBackground = true;
        oldInputBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
        oldMaximized = game.maximized; game.maximized = true; game.Focus();
        Application.logMessageReceived += Log;
        mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); player = mission.player;
        originalMission = mission.GetInstanceID(); cinema = mission.cinematic;
        nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        fixedStep = Time.fixedDeltaTime;
        capture = player.gameObject.AddComponent<SpiritIntroductionCapture>();
        sampledEye = capturedWindow = false; maximumEyeSpeed = 0; wallContacts = lensContacts = 0;
        Next(100);
    }
    static void PrepareNavigation()
    {
        cinema.EndShot(); mission.story.CancelOpening(); mission.story.enabled = false;
        mission.enabled = false; mission.sequence.enabled = false; mission.encounter.enabled = false;
        SetAuto(mission, "State", VolcanoMissionDirector.FlowState.Preparation);
        SetAuto(mission, "Phase", VolcanoMissionDirector.MissionPhase.Weapon);
        Check("authored interactions retain glowing circles", nav.directionArrow && nav.markerGlow.enabled && !nav.directionArrow.enabled && !nav.arrow.enabled,
            "Interaction circles are retained; only truck road guidance uses arrows.");
        Check("mission explains the glowing pickup and key", mission.ObjectiveDetail.Contains("mavi halka") && mission.ObjectiveDetail.Contains("silah") && mission.ObjectiveDetail.Contains(player.interactKey.ToString()), mission.ObjectiveDetail);
        Next(0);
    }
    static void Update()
    {
        if (phase < 0 && EditorApplication.isPlaying && !EditorApplication.isCompiling && Time.frameCount > 2 && SessionState.GetBool(Pending, false))
        {
            SessionState.SetBool(Pending, false);
            try { Start(); } catch (Exception ex) { phase = 0; Finish(ex.ToString()); }
        }
        if (phase < 0 || !EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("phase=" + phase + "; state=" + (combat ? combat.State.ToString() : "none") + "; cinema=" + (cinema ? cinema.Elapsed.ToString() : "none"));
            Tick();
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void Tick()
    {
        float age = Time.unscaledTime - phaseAt;
        switch (phase)
        {
            case 100:
                var story = mission.story;
                if (story.IsOpening)
                {
                    if (sampledEye && Time.deltaTime > .001f) maximumEyeSpeed = Mathf.Max(maximumEyeSpeed, Vector3.Distance(player.viewCamera.transform.position, previousEye) / Time.deltaTime);
                    previousEye = player.viewCamera.transform.position; sampledEye = true;
                    foreach (var collider in Physics.OverlapSphere(previousEye, .06f, ~0, QueryTriggerInteraction.Ignore))
                        if (!collider.transform.IsChildOf(player.transform)) lensContacts++;
                    var openingBody = player.playerBody;
                    foreach (var collider in Physics.OverlapBox(openingBody.bounds.center, openingBody.bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (collider.transform.IsChildOf(player.transform) || collider is TerrainCollider) continue;
                        if (Physics.ComputePenetration(openingBody, openingBody.transform.position, openingBody.transform.rotation, collider, collider.transform.position, collider.transform.rotation, out var normal, out var depth) &&
                            Mathf.Abs(normal.y) < .5f && depth > .08f) wallContacts++;
                    }
                    if (!capturedWindow && story.AtWindow && story.ObservationElapsed > 4.5f)
                    {
                        Check("window view zooms in on the volcano", player.viewCamera.fieldOfView < 52 && player.viewCamera.fieldOfView > 35,
                            "FOV=" + player.viewCamera.fieldOfView + "; original=" + player.firstPerson.fieldOfView);
                        var openingHud = Object.FindFirstObjectByType<RescueHUD>();
                        Check("window shot keeps narrative captions and hides task clutter", openingHud.gameplayGroup.alpha == 0 && openingHud.subtitleText.gameObject.activeInHierarchy && openingHud.subtitleText.transform.parent.parent == openingHud.canvas.transform,
                            "The inner thought remains visible independently of the task HUD.");
                        capture.Photo("00-opening-window"); capturedWindow = true;
                    }
                    return;
                }
                if (!story.OpeningComplete || capture.Busy) return;
                Check("opening physically reaches the window and restores control", story.AtWindow && !player.CinematicLocked && mission.PreparationRemaining > 119 && capturedWindow,
                    "Real CharacterController movement and the complete observation ran before preparation began.");
                Check("opening avoids walls and camera clipping", wallContacts == 0 && lensContacts == 0,
                    "Wall penetrations=" + wallContacts + "; lens intersections=" + lensContacts);
                Check("opening camera has no position jumps", maximumEyeSpeed < 14f,
                    "Maximum per-frame camera speed=" + maximumEyeSpeed.ToString("F2") + " m/s, including the gradual window lean.");
                Check("window zoom restores the gameplay projection", Mathf.Abs(player.viewCamera.fieldOfView - player.firstPerson.fieldOfView) < .1f,
                    "Original gameplay field of view restored at handoff.");
                PrepareNavigation(); return;
            case 0:
                if (age < .3f) return;
                Check("weapon destination stays a glowing circle", nav.CurrentTarget == mission.weaponPickup.transform && nav.markerGlow.gameObject.activeInHierarchy && nav.markerGlow.enabled && !nav.directionArrow.enabled && nav.ring.positionCount == 65,
                    "The original pickup is selected and retains its circular world ring and halo.");
                capture.Photo("01-weapon-circle"); Next(1); return;
            case 1:
                if (age < .3f || capture.Busy) return;
                SpawnActor();
                SaveEntry(); cinema.BeginSpirits(actor.transform);
                Check("first encounter locks controls in slow motion", cinema.IsPlaying && player.CinematicLocked && !player.viewCamera.enabled && Time.timeScale > 0 && Time.timeScale < 1,
                    "The real combat controller performs the introduction hop.");
                Next(2); return;
            case 2:
                if (!cinema.SpiritFrozen || !cinema.HasCapturedSpiritFrame) return;
                Check("freeze captures a real airborne spirit", combat.IsIntroductionJump && combat.IntroductionJumpProgress >= .46f && combat.IntroductionJumpProgress < .7f && Time.timeScale == 0,
                    "Airborne progress=" + combat.IntroductionJumpProgress + "; screenshot=" + cinema.HasCapturedSpiritFrame);
                Check("held input cannot skip the first frame", !cinema.ContinueSpiritIntroduction(), "Minimum reading time has not elapsed.");
                frozenPosition = actor.transform.position; worldTime = Time.time; health = player.GetComponent<CreatureCombatPlayer>().Health; water = mission.tank.TankLitres;
                capture.Photo("02-frozen-spirit-no-weapon"); Next(3); return;
            case 3:
                if (age < 4.3f || capture.Busy) return;
                Check("briefing waits for the player without advancing hazards", cinema.SpiritFrozen && cinema.CanContinueSpirit && Time.time == worldTime && Vector3.Distance(actor.transform.position, frozenPosition) < .001f &&
                    player.GetComponent<CreatureCombatPlayer>().Health == health && mission.tank.TankLitres == water,
                    "World time, enemy position, player health and truck water are unchanged while reading.");
                ConfirmWithKeyboard(); Next(4); return;
            case 4:
                if (cinema.SpiritFrozen) return;
                KeyUp();
                Check("Space confirms the card through the Input System", cinema.IsPlaying && player.CinematicLocked && Time.timeScale > 0, "The return shot keeps input locked until the camera is back.");
                Next(5); return;
            case 5:
                if (cinema.IsPlaying) return;
                Check("on-foot camera and physics are restored", player.viewCamera.enabled && !player.CinematicLocked && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep) && Mathf.Abs(player.viewCamera.fieldOfView - entryFov) < .02f,
                    "Camera enabled, input released, original FOV, timescale and fixed step restored.");
                cinema.BeginSpirits(actor.transform);
                Check("the first-contact card is shown once", !cinema.IsPlaying, "Repeated BeginSpirits is ignored.");
                player.AcquirePistol();
                SetAuto(mission, "Phase", VolcanoMissionDirector.MissionPhase.WarnVillage);
                Next(50); return;
            case 50:
                if (age < .25f) return;
                Check("alarm interaction stays a glowing circle", nav.CurrentTarget == mission.villageAlarm.contactPoint && nav.markerGlow.enabled && !nav.directionArrow.enabled && nav.ring.positionCount == 65, nav.CurrentLabel);
                capture.Photo("03-alarm-circle");
                Next(51); return;
            case 51:
                if (capture.Busy || age < .25f) return;
                SetAuto(mission, "Phase", VolcanoMissionDirector.MissionPhase.Truck);
                Next(6); return;
            case 6:
                if (age < .5f) return;
                Check("combat resumes after the introduction jump", !combat.IsIntroductionJump && !actor.IsDefeated, "The same enemy lands and continues its existing combat state machine.");
                combat.BeginIntroductionJump();
                for (int step = 0; step < 8; step++) combat.TickCombat(.1f);
                actor.ApplyBulletDamage(10, actor.transform.position + Vector3.up, Vector3.forward);
                Check("a surviving bullet hit exits the introduction motion", !combat.IsIntroductionJump && combat.State == LavaCreatureCombat.Motion.Staggered && !actor.IsDefeated,
                    "After control returns, a nonlethal hit cannot leave the enemy stuck in introduction jumps.");
                Check("truck entry stays a glowing circle", nav.CurrentTarget == player.driverDoor && nav.markerGlow.gameObject.activeInHierarchy && nav.markerGlow.enabled && !nav.directionArrow.enabled && nav.ring.positionCount == 65, nav.CurrentLabel);
                capture.Photo("03b-truck-circle"); Next(7); return;
            case 7:
                if (capture.Busy || age < .25f) return;
                player.SetDriving(true);
                SetAuto(mission, "Phase", VolcanoMissionDirector.MissionPhase.Refill);
                var road = nav.lakeRoad[nav.lakeRoad.Length / 2];
                player.drive.body.position = road.position + Vector3.up * .5f;
                Physics.SyncTransforms(); Next(70); return;
            case 70:
                if (age < .35f) return;
                Check("driving route uses an arrow between circular destinations", nav.UsesDirectionalArrow && nav.directionArrow.enabled && !nav.markerGlow.enabled && nav.ring.positionCount == 8,
                    "Only the moving road guide is an arrow. Lake parking, buttons, pickups and entry points remain circles.");
                capture.Photo("03c-road-arrow"); Next(71); return;
            case 71:
                if (capture.Busy || age < .25f) return;
                cinema = NewCinema();
                var body = player.drive.body;
                previousKinematic = body.isKinematic;
                truckVelocity = body.linearVelocity; truckAngularVelocity = body.angularVelocity;
                Time.timeScale = .8f; SaveEntry(); cinema.BeginSpirits(actor.transform);
                Check("driving reveal freezes the truck body", body.isKinematic && player.IsDriving && player.CinematicLocked, "No truck movement during the first-contact camera.");
                Next(8); return;
            case 8:
                if (!cinema.SpiritFrozen || !cinema.HasCapturedSpiritFrame) return;
                capture.Photo("04-frozen-spirit-driving"); Next(9); return;
            case 9:
                if (capture.Busy || age < .3f) return;
                cinema.EndShot();
                Check("interruption restores the previous non-default time and truck", Time.timeScale == .8f && Mathf.Approximately(Time.fixedDeltaTime, fixedStep) && player.drive.body.isKinematic == previousKinematic &&
                    Vector3.Distance(player.drive.body.linearVelocity, truckVelocity) < .001f && Vector3.Distance(player.drive.body.angularVelocity, truckAngularVelocity) < .001f && player.viewCamera.enabled && !player.CinematicLocked,
                    "Original .8 timescale, fixed step, rigidbody state, velocity, camera and input restored.");
                Object.Destroy(cinema.gameObject); Time.timeScale = 1;
                player.SetDriving(false);
                player.PlaceOnFoot(mission.story.defensePosition.position, 0);
                cinema = NewCinema(); cinema.BeginSpirits(actor.transform);
                Next(10); return;
            case 10:
                if (!cinema.SpiritFrozen || !cinema.HasCapturedSpiritFrame) return;
                capture.Photo("05-frozen-spirit-armed"); Next(11); return;
            case 11:
                if (capture.Busy || age < .3f) return;
                player.gameFlow.RestartMission(); Next(12); return;
            case 12:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.GetInstanceID() == originalMission) return;
                Check("restart clears the briefing and restores normal time", !fresh.cinematic.HasSpiritsPlayed && !fresh.cinematic.SpiritFrozen && !fresh.cinematic.HasCapturedSpiritFrame && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep),
                    "A fresh mission starts without a stale freeze texture or physics pause.");
                Check("saved scene retains navigation and font bindings", Object.FindFirstObjectByType<ObjectiveNavigation>().directionArrow && Object.FindFirstObjectByType<RescueHUD>().questBodyText.font.name.Contains("SourceSans"),
                    "The changes survive a real scene reload.");
                Finish(null); return;
        }
    }
    static void SpawnActor()
    {
        var template = mission.encounter.spawns.First(s => s.creature).creature;
        var staging = new GameObject("Inactive introduction fixture"); staging.SetActive(false);
        actor = Object.Instantiate(template, staging.transform);
        actor.transform.SetParent(null, true); Object.Destroy(staging);
        Vector3 point = mission.story.defensePosition.position;
        point = SpiritOutdoorNavigation.Outside(point, point, 2f);
        actor.transform.position = point;
        actor.gameObject.SetActive(true);
        combat = actor.GetComponent<LavaCreatureCombat>(); combat.attackBuildings = false;
        player.PlaceOnFoot(point + new Vector3(8, 0, -15), 0);
    }
    static VolcanoCinematic NewCinema()
    {
        var result = new GameObject("Disposable spirit camera lifecycle fixture").AddComponent<VolcanoCinematic>();
        result.player = player; result.spiritTimeScale = .5f; result.spiritViewingDistance = 9f;
        return result;
    }
    static void SaveEntry() { entryPosition = player.viewCamera.transform.position; entryRotation = player.viewCamera.transform.rotation; entryFov = player.viewCamera.fieldOfView; }
    static void SetAuto(object owner, string property, object value) => owner.GetType().GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    static void KeyDown(Key key) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key));
    static void KeyUp() => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
    static void ConfirmWithKeyboard()
    {
        var previous = Keyboard.current;
        var testKeyboard = InputSystem.AddDevice<Keyboard>("SpiritIntroductionKeyboard");
        try
        {
            InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Space));
            var update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(InputUpdateType) }, null);
            if (update == null) throw new MissingMethodException("Dynamic input update");
            update.Invoke(null, new object[] { InputUpdateType.Dynamic });
            testKeyboard.MakeCurrent();
            Check("Space is delivered to the player input buffer", testKeyboard.spaceKey.isPressed && testKeyboard.spaceKey.wasPressedThisFrame,
                "Synthetic keyboard processed through Input System Dynamic, avoiding the separate Editor state buffer.");
            cinema.TickShot(.001f);
            Check("the shot consumes the actual Space press", !cinema.SpiritFrozen && cinema.IsPlaying,
                "The real shot tick enters the return transition after reading Keyboard.current.");
        }
        finally
        {
            InputSystem.RemoveDevice(testKeyboard);
            if (previous != null && previous.added) previous.MakeCurrent();
        }
    }
    static void Next(int value) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 65; File.WriteAllText(SpiritIntroductionSetup.Folder + "/progress.txt", "phase=" + phase); }
    static void Check(string name, bool passed, string evidence)
    {
        report.checks.Add(new Result { name = name, passed = passed, evidence = evidence });
        File.WriteAllText(SpiritIntroductionSetup.Folder + "/report.json", JsonUtility.ToJson(report, true));
        if (!passed) throw new InvalidOperationException(name + ": " + evidence);
    }
    static void Log(string condition, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = condition + "\n" + stack; }
    static void Finish(string failure)
    {
        if (phase < 0) return;
        phase = -1; Application.logMessageReceived -= Log;
        if (cinema) cinema.EndShot();
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        InputSystem.settings.backgroundBehavior = oldInputBackground;
        Application.runInBackground = oldBackground;
        if (game) game.maximized = oldMaximized;
        Time.timeScale = 1;
        report.status = failure == null ? "PASSED" : "FAILED"; report.error = failure ?? "";
        File.WriteAllText(SpiritIntroductionSetup.Folder + "/report.json", JsonUtility.ToJson(report, true));
        EditorApplication.ExitPlaymode();
    }
}

public sealed class SpiritIntroductionCapture : MonoBehaviour
{
    public bool Busy { get; private set; }
    public void Photo(string name) { if (!Busy) StartCoroutine(Capture(name)); }
    IEnumerator Capture(string name)
    {
        Busy = true; yield return new WaitForEndOfFrame();
        var pixels = ScreenCapture.CaptureScreenshotAsTexture();
        if (pixels) { File.WriteAllBytes(SpiritIntroductionSetup.Folder + "/" + name + ".png", pixels.EncodeToPNG()); Destroy(pixels); }
        Busy = false;
    }
}
