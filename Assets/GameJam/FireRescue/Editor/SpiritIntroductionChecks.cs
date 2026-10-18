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
    static Vector3 frozenPosition, entryPosition;
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
    static VolcanoMusicDirector music;
    static AudioSource frozenScore;
    static AudioClip frozenScoreClip;
    static float frozenScoreTime, previousMusicSetting;
    static bool hadMusicSetting;
    static int shotsBeforeConfirmation;
    static Keyboard testKeyboard, previousKeyboard;
    static Mouse testMouse, previousMouse;
    static bool testMouseHeld;
    static string authoredQuestFontGuid;

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
        music = Object.FindFirstObjectByType<VolcanoMusicDirector>();
        hadMusicSetting = PlayerPrefs.HasKey("Ashfall.MusicVolume");
        previousMusicSetting = PlayerPrefs.GetFloat("Ashfall.MusicVolume", .75f);
        PlayerPrefs.SetFloat("Ashfall.MusicVolume", 1f);
        Check("authored score is available for the real freeze frame", music && music.mission == mission && music.passage && music.horizon,
            "Both existing recordings are bound to the mission's adaptive music director.");
        originalMission = mission.GetInstanceID(); cinema = mission.cinematic;
        nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
        authoredQuestFontGuid = QuestFontGuid(Object.FindFirstObjectByType<RescueHUD>());
        Check("active quest typography uses an authored font asset", !string.IsNullOrEmpty(authoredQuestFontGuid),
            "Runtime HUD font asset GUID=" + authoredQuestFontGuid);
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
        Check("mission explains the glowing pickup and key", mission.ObjectiveDetail.Contains("glowing blue ring") && mission.ObjectiveDetail.Contains("pistol") && mission.ObjectiveDetail.Contains(player.interactKey.ToString()), mission.ObjectiveDetail);
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
                frozenScore = music.IsHorizon ? music.HorizonSource : music.PassageSource;
                frozenScoreClip = frozenScore.clip; frozenScoreTime = frozenScore.time;
                shotsBeforeConfirmation = player.pistol.ShotsFired;
                capture.Photo("02-frozen-spirit-no-weapon"); Next(3); return;
            case 3:
                if (age < 4.3f || capture.Busy) return;
                Check("briefing waits for the player without advancing hazards", cinema.SpiritFrozen && cinema.CanContinueSpirit && Time.time == worldTime && Vector3.Distance(actor.transform.position, frozenPosition) < .001f &&
                    player.GetComponent<CreatureCombatPlayer>().Health == health && mission.tank.TankLitres == water,
                    "World time, enemy position, player health and truck water are unchanged while reading.");
                float musicAdvance = Mathf.Repeat(frozenScore.time - frozenScoreTime, frozenScoreClip.length);
                Check("frozen briefing keeps the same recording advancing at normal pitch", frozenScore == (music.IsHorizon ? music.HorizonSource : music.PassageSource) &&
                    frozenScore.clip == frozenScoreClip && frozenScore.isPlaying && Mathf.Abs(frozenScore.pitch - 1f) < .001f &&
                    musicAdvance > 1f && musicAdvance < age + 1.5f && Time.time == worldTime,
                    "Actual frozen world; audio advanced=" + musicAdvance.ToString("F2") + " s in " + age.ToString("F2") + " real seconds.");
                float calmVolume = music.IsHorizon ? music.horizonVolume : music.passageVolume;
                Check("briefing music recovers its regular level despite frozen foreground sounds", music.TargetDuck > .999f && music.CurrentDuck > .98f &&
                    frozenScore.volume > calmVolume * .92f,
                    "Gain=" + frozenScore.volume.ToString("F3") + "; calm=" + calmVolume.ToString("F3") + "; duck=" + music.CurrentDuck.ToString("F3") + "/" + music.TargetDuck.ToString("F3"));
                capture.Photo("02b-ready-controls"); Next(30); return;
            case 30:
                if (capture.Busy) return;
                ConfirmWithKeyboard(); Next(4); return;
            case 4:
                if (cinema.SpiritFrozen) return;
                Check("Esc confirms without opening pause or firing", cinema.IsPlaying && player.CinematicLocked && Time.timeScale > 0 &&
                    player.gameFlow.State == RescueGameFlow.GameState.Playing && player.pistol.ShotsFired == shotsBeforeConfirmation,
                    "The actual Escape input enters the return shot while gameplay and the pause menu remain locked.");
                RestoreKeyboard();
                Next(5); return;
            case 5:
                if (cinema.IsPlaying) return;
                Check("on-foot camera and physics are restored", player.viewCamera.enabled && !player.CinematicLocked && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep) && Mathf.Abs(player.viewCamera.fieldOfView - entryFov) < .02f,
                    "Camera enabled, input released, original FOV, timescale and fixed step restored.");
                Check("Esc leaves gameplay running after the camera returns", player.gameFlow.State == RescueGameFlow.GameState.Playing &&
                    player.pistol.ShotsFired == shotsBeforeConfirmation, "No pause or weapon event leaks out of the confirmation.");
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
                Check("driving route uses an arrow between circular destinations", nav.UsesDirectionalArrow && nav.directionArrow.enabled && !nav.markerGlow.enabled && nav.ring.positionCount > 8,
                    "Only the moving road guide is an arrow. Lake parking, buttons, pickups and entry points remain circles.");
                capture.Photo("03c-road-arrow"); Next(71); return;
            case 71:
                if (capture.Busy || age < .25f) return;
                cinema = NewCinema();
                var body = player.drive.body;
                previousKinematic = body.isKinematic;
                Time.timeScale = .8f; SaveEntry(); cinema.BeginSpirits(actor.transform);
                Check("driving reveal freezes the truck body", body.isKinematic && player.IsDriving && player.CinematicLocked, "No truck movement during the first-contact camera.");
                Next(8); return;
            case 8:
                if (!cinema.SpiritFrozen || !cinema.HasCapturedSpiritFrame) return;
                shotsBeforeConfirmation = player.pistol.ShotsFired;
                capture.Photo("04-frozen-spirit-driving"); Next(9); return;
            case 9:
                if (capture.Busy || age < cinema.spiritSeconds + .25f) return;
                ConfirmWithMouse(); Next(90); return;
            case 90:
                if (player.pistol.ShotsFired != shotsBeforeConfirmation) throw new InvalidOperationException("The held confirmation click fired during the return shot.");
                if (cinema.IsPlaying) return;
                Check("left click returns to the truck with its original physics and time", Time.timeScale == .8f && Mathf.Approximately(Time.fixedDeltaTime, fixedStep) && player.drive.body.isKinematic == previousKinematic &&
                    player.viewCamera.enabled && !player.CinematicLocked && player.IsDriving,
                    "Original .8 timescale, fixed step, rigidbody mode, driving camera and input restored through the real return transition.");
                var resumedVelocity = player.drive.body.linearVelocity;
                var resumedAngularVelocity = player.drive.body.angularVelocity;
                var interrupted = NewCinema(); interrupted.BeginSpirits(actor.transform); interrupted.EndShot();
                Check("interrupting a truck reveal preserves its existing momentum", Time.timeScale == .8f && player.drive.body.isKinematic == previousKinematic &&
                    Vector3.Distance(player.drive.body.linearVelocity, resumedVelocity) < .001f && Vector3.Distance(player.drive.body.angularVelocity, resumedAngularVelocity) < .001f,
                    "Cancel restores the exact pre-reveal velocity and angular velocity before the next physics tick.");
                Object.Destroy(interrupted.gameObject); mission.cinematic = cinema;
                player.SetDriving(false);
                player.PlaceOnFoot(mission.story.defensePosition.position, 0);
                Next(91); return;
            case 91:
                if (age < .3f) return;
                ProcessDynamicInput(); testMouse.MakeCurrent();
                Check("holding the confirmation click cannot fire or recapture on-foot input", testMouse.leftButton.isPressed && !player.firstPerson.InputCaptured &&
                    player.pistol.ShotsFired == shotsBeforeConfirmation && player.gameFlow.State == RescueGameFlow.GameState.Playing,
                    MouseConfirmationEvidence());
                testMouseHeld = false;
                InputSystem.QueueStateEvent(testMouse, new MouseState()); ProcessDynamicInput();
                Next(92); return;
            case 92:
                if (age < .3f) return;
                ProcessDynamicInput(); testMouse.MakeCurrent();
                Check("releasing the confirmation click restores control without a shot", !testMouse.leftButton.isPressed && player.firstPerson.InputCaptured &&
                    player.pistol.ShotsFired == shotsBeforeConfirmation && player.gameFlow.State == RescueGameFlow.GameState.Playing,
                    MouseConfirmationEvidence());
                RestoreMouse(); Object.Destroy(cinema.gameObject);
                cinema = NewCinema(); cinema.BeginSpirits(actor.transform);
                Next(10); return;
            case 10:
                if (!cinema.SpiritFrozen || !cinema.HasCapturedSpiritFrame) return;
                capture.Photo("05-frozen-spirit-armed"); Next(11); return;
            case 11:
                if (capture.Busy || age < .3f) return;
                cinema.EndShot();
                Check("interruption restores the previous non-default time and camera", Time.timeScale == .8f && Mathf.Approximately(Time.fixedDeltaTime, fixedStep) &&
                    player.viewCamera.enabled && !player.CinematicLocked,
                    "Canceling a separate armed briefing also restores the inherited .8 timescale and controls.");
                Object.Destroy(cinema.gameObject); Time.timeScale = 1;
                cinema = NewCinema(); cinema.BeginSpirits(actor.transform);
                Next(110); return;
            case 110:
                if (!cinema.SpiritFrozen || !cinema.HasCapturedSpiritFrame) return;
                Next(111); return;
            case 111:
                if (age < .3f) return;
                player.gameFlow.RestartMission(); Next(12); return;
            case 12:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.GetInstanceID() == originalMission) return;
                Check("restart clears the briefing and restores normal time", !fresh.cinematic.HasSpiritsPlayed && !fresh.cinematic.SpiritFrozen && !fresh.cinematic.HasCapturedSpiritFrame && Time.timeScale == 1 && Mathf.Approximately(Time.fixedDeltaTime, fixedStep),
                    "A fresh mission starts without a stale freeze texture or physics pause.");
                Next(13); return;
            case 13:
                if (age < .3f) return;
                var freshNav = Object.FindFirstObjectByType<ObjectiveNavigation>();
                var freshHud = Object.FindFirstObjectByType<RescueHUD>();
                string restoredFontGuid = QuestFontGuid(freshHud);
                Check("saved scene retains navigation and font bindings", freshNav && freshNav.directionArrow &&
                    !string.IsNullOrEmpty(authoredQuestFontGuid) && restoredFontGuid == authoredQuestFontGuid,
                    "Font asset GUID before=" + authoredQuestFontGuid + "; after=" + restoredFontGuid +
                    "; arrow=" + (freshNav && freshNav.directionArrow) + "; fresh HUD initialized before comparison.");
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
        mission.cinematic = result;
        return result;
    }
    static void SaveEntry() { entryPosition = player.viewCamera.transform.position; entryRotation = player.viewCamera.transform.rotation; entryFov = player.viewCamera.fieldOfView; }
    static string QuestFontGuid(RescueHUD hud) => hud && hud.questBodyText && hud.questBodyText.font ?
        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(hud.questBodyText.font)) : "";
    static void SetAuto(object owner, string property, object value) => owner.GetType().GetField("<" + property + ">k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    static void ConfirmWithKeyboard()
    {
        previousKeyboard = Keyboard.current;
        testKeyboard = InputSystem.AddDevice<Keyboard>("SpiritIntroductionKeyboard");
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Escape)); ProcessDynamicInput();
        testKeyboard.MakeCurrent();
        Check("Esc is delivered to the player input buffer", testKeyboard.escapeKey.isPressed && testKeyboard.escapeKey.wasPressedThisFrame,
            "Synthetic keyboard processed through Input System Dynamic, avoiding the separate Editor state buffer.");
        cinema.TickShot(.001f);
        Check("the shot consumes the actual Esc press", !cinema.SpiritFrozen && cinema.IsPlaying,
            "The production shot tick enters the return transition after reading Keyboard.current.");
    }
    static void ConfirmWithMouse()
    {
        previousMouse = Mouse.current;
        testMouse = InputSystem.AddDevice<Mouse>("SpiritIntroductionMouse");
        testMouseHeld = true;
        InputSystem.onBeforeUpdate += MaintainTestMouseState;
        InputSystem.onAfterUpdate += KeepTestMouseCurrent;
        InputSystem.QueueStateEvent(testMouse, new MouseState().WithButton(MouseButton.Left)); ProcessDynamicInput();
        testMouse.MakeCurrent();
        Check("left click is delivered to the player input buffer", testMouse.leftButton.isPressed && testMouse.leftButton.wasPressedThisFrame,
            "Real Input System mouse state is held until the test explicitly releases it.");
        cinema.TickShot(.001f);
        Check("the shot consumes the actual left click", !cinema.SpiritFrozen && cinema.IsPlaying && player.CinematicLocked,
            "Left click starts the return transition while the player remains locked.");
    }
    static void MaintainTestMouseState()
    {
        if (testMouse == null || !testMouse.added) return;
        var state = new MouseState();
        if (testMouseHeld) state = state.WithButton(MouseButton.Left);
        InputSystem.QueueStateEvent(testMouse, state);
    }
    static void KeepTestMouseCurrent()
    {
        if (testMouse != null && testMouse.added) testMouse.MakeCurrent();
    }
    static string MouseConfirmationEvidence() => "Synthetic held=" + testMouse.leftButton.isPressed +
        "; currentIsFixture=" + (Mouse.current == testMouse) + "; captured=" + player.firstPerson.InputCaptured +
        "; shots=" + player.pistol.ShotsFired + "/" + shotsBeforeConfirmation + "; flow=" + player.gameFlow.State +
        "; cinematicLocked=" + player.CinematicLocked + "; cursor=" + Cursor.lockState;
    static void ProcessDynamicInput()
    {
        var update = typeof(InputSystem).GetMethod("Update", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(InputUpdateType) }, null);
        if (update == null) throw new MissingMethodException("Dynamic input update");
        update.Invoke(null, new object[] { InputUpdateType.Dynamic });
    }
    static void RestoreKeyboard()
    {
        if (testKeyboard != null && testKeyboard.added) InputSystem.RemoveDevice(testKeyboard);
        testKeyboard = null;
        if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        previousKeyboard = null;
    }
    static void RestoreMouse()
    {
        InputSystem.onBeforeUpdate -= MaintainTestMouseState;
        InputSystem.onAfterUpdate -= KeepTestMouseCurrent;
        if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
        testMouse = null;
        testMouseHeld = false;
        if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        previousMouse = null;
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
        RestoreKeyboard(); RestoreMouse();
        if (hadMusicSetting) PlayerPrefs.SetFloat("Ashfall.MusicVolume", previousMusicSetting); else PlayerPrefs.DeleteKey("Ashfall.MusicVolume");
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
