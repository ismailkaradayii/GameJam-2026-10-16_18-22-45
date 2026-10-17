using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VillageAlarmChecks
{
    const string Pending = "GameJam.VillageAlarm.Check";
    static readonly List<string> lines = new List<string>();
    static FireRescuePlayer player;
    static VolcanoMissionDirector mission;
    static VillageAlarmButton alarm;
    static VillageAlarmInputFixture fixture;
    static int phase = -1, frame, originalMission, ammunition;
    static float phaseAt, pausedProgress, firstVolume, oldSfx;
    static bool oldBackground, oldMaximized;
    static InputSettings.BackgroundBehavior oldInputBackground;
    static EditorWindow game;
    static double deadline;
    static string error;
    static VillageAlarmChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check village alarm button")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Use Volcano_Demo in Edit Mode.");
        VillageAlarmSetup.Validate();
        if (SceneManager.GetActiveScene().isDirty) EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        lines.Clear(); error = null;
        lines.Add("Real-frame saved-scene integration check. Synthetic Input System E events and an aim/focus fixture drive the existing player Update; no physical keyboard or full manual village route is claimed.");
        Bind(); originalMission = mission.GetInstanceID();
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        oldInputBackground = InputSystem.settings.backgroundBehavior; InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        oldSfx = PlayerPrefs.GetFloat("Ashfall.SfxVolume", 1); PlayerPrefs.SetFloat("Ashfall.SfxVolume", 1);
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); oldMaximized = game.maximized; game.Focus(); game.maximized = true;
        Application.logMessageReceived += Log; Next(0);
    }
    static void Bind() { mission = Object.FindFirstObjectByType<VolcanoMissionDirector>(); player = mission.player; alarm = mission.villageAlarm; }
    static void Update()
    {
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
                if (mission.cinematic.IsPlaying || mission.State != VolcanoMissionDirector.FlowState.Preparation || age < 3) return;
                Check("siren is silent before a real strike", !alarm.Activated && alarm.StrikeCount == 0 && alarm.villageSirens.All(s => !s.isPlaying));
                Check("spotlight reaches and points at the user's existing button", alarm.redSpot.range > Vector3.Distance(alarm.redSpot.transform.position, alarm.contactPoint.position) * 2 && alarm.redSpot.enabled);
                Check("existing model position and scale are preserved", Vector3.Distance(alarm.transform.position, new Vector3(28.387f, 3.782f, -596.55f)) < .1f && alarm.transform.localScale.x > 500);
                var samples = new float[4096]; alarm.villageSirens[0].clip.GetData(samples, 12000);
                Check("authored siren contains non-silent PCM audio", samples.Max(Mathf.Abs) > .3f);
                fixture = player.gameObject.AddComponent<VillageAlarmInputFixture>(); fixture.player = player; fixture.alarm = alarm;
                fixture.focus = alarm.contactPoint;
                player.PlaceOnFoot(alarm.approachPose.position, alarm.approachPose.eulerAngles.y); Next(1); return;
            case 1:
                if (age < .4f) return;
                player.contextualInteractions.RefreshSelection();
                var target = player.contextualInteractions.targets.First(t => t.kind == ContextualInteractionKind.VillageAlarm);
                Check("before the pistol, E explains the missing preparation", player.contextualInteractions.Describe(target, out var blocked) && blocked.blocked && blocked.detail == player.contextualInteractions.words.alarmNeedsPistol && !alarm.TryPress());
                fixture.focus = mission.weaponPickup.transform;
                player.PlaceOnFoot(mission.standingSpawn.position, mission.standingSpawn.eulerAngles.y); Next(2); return;
            case 2:
                if (age < .45f) return;
                KeyDown(); Next(3); return;
            case 3:
                if (!player.HasPistol) return;
                KeyUp(); ammunition = player.pistol.Ammo;
                Check("real E input collects the existing pistol", mission.weaponPickup.IsCollected && player.HasPistol);
                Check("pistol immediately reveals objective two inside house one", mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage && !mission.WarningCompleted && !mission.evacuation.IsActive && !mission.LeftStartingHouse);
                fixture.focus = alarm.contactPoint; player.PlaceOnFoot(alarm.approachPose.position, alarm.approachPose.eulerAngles.y); Next(4); return;
            case 4:
                if (age < .5f) return;
                player.contextualInteractions.RefreshSelection();
                if (!fixture.PhotoReady("01-alarm-prompt.png")) return;
                Check("large E prompt selects the real red button", player.contextualInteractions.CurrentTarget && player.contextualInteractions.CurrentTarget.kind == ContextualInteractionKind.VillageAlarm && !player.contextualInteractions.CurrentOffer.blocked, Snapshot());
                Check("the hand rig can physically reach the cap", alarm.InReach && alarm.hands.CanReachButton(alarm.contactPoint.position));
                KeyDown(); Next(5); return;
            case 5:
                if (!alarm.IsPressing) return;
                KeyUp();
                Check("E begins the native open-palm animation", alarm.hands.ButtonSlapActive && player.InteractionBusy && player.firstPerson.handsCamera.enabled);
                Check("the gun is stowed while striking", !player.pistol.Available && !player.pistol.weapon.activeSelf && player.pistol.Ammo == ammunition);
                if (alarm.PressProgress < alarm.contactFraction) { pausedProgress = alarm.PressProgress; player.gameFlow.Pause(); Next(6); }
                else Next(7);
                return;
            case 6:
                if (age < .35f) return;
                Check("pause freezes the palm strike and hides UI", Mathf.Abs(alarm.PressProgress - pausedProgress) < .002f && !player.contextualInteractions.HasVisiblePrompt && !alarm.Activated);
                player.gameFlow.Resume(); Next(7); return;
            case 7:
                if (!alarm.Activated) return;
                Check("only palm contact commits the button", alarm.StrikeCount == 1 && alarm.AlarmStartCount == 1);
                Check("strike advances the real warning mission to evacuation", mission.WarningCompleted && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate && mission.evacuation.IsActive);
                firstVolume = alarm.villageSirens.Max(s => s.volume); Next(8); return;
            case 8:
                if (alarm.IsPressing || age < 1.6f) return;
                fixture.FlushPhotos();
                Check("palm reaches the button surface", fixture.ClosestPalmDistance < .22f, "distance=" + fixture.ClosestPalmDistance.ToString("F3"));
                Check("cap visibly depresses at contact", fixture.MaximumCapTravel > .025f, "travel=" + fixture.MaximumCapTravel.ToString("F3"));
                Check("native impact camera kick is observed", fixture.SawShake);
                Check("three spatial village sirens rise after contact", alarm.villageSirens.All(s => s.isPlaying && s.volume >= .45f) && alarm.villageSirens.Max(s => s.volume) > firstVolume);
                Check("pistol and hand camera recover without spending ammunition", player.pistol.Available && player.pistol.Equipped && player.pistol.Ammo == ammunition && player.firstPerson.handsCamera.enabled && !alarm.hands.ButtonSlapActive);
                Check("E spam cannot repeat the strike or restart the sirens", !alarm.TryPress() && alarm.StrikeCount == 1 && alarm.AlarmStartCount == 1);
                player.gameFlow.Pause(); Next(9); return;
            case 9:
                if (age < .35f) return;
                Check("pause suspends the spatial sirens", alarm.villageSirens.All(s => !s.isPlaying));
                player.gameFlow.Resume(); Next(10); return;
            case 10:
                if (age < .35f) return;
                Check("resume continues the sirens without replaying the trigger", alarm.villageSirens.All(s => s.isPlaying) && alarm.AlarmStartCount == 1);
                mission.TriggerDeadline();
                Check("alarm mission leaves the independent eruption deadline intact", mission.EruptionTriggered && mission.PreparationRemaining == 0 && player.CinematicLocked);
                Next(11); return;
            case 11:
                if (mission.cinematic.IsPlaying || player.CinematicLocked || Time.timeScale != 1) return;
                Check("cinematic restores control without an unfinished slap", !player.InteractionBusy && !alarm.hands.ButtonSlapActive && player.pistol.Available);
                player.gameFlow.RestartMission(); Next(12); return;
            case 12:
                var fresh = Object.FindFirstObjectByType<VolcanoMissionDirector>();
                if (!fresh || fresh.GetInstanceID() == originalMission || fresh.cinematic.IsPlaying || fresh.State != VolcanoMissionDirector.FlowState.Preparation) return;
                Bind();
                Check("restart resets the alarm, mission and all siren emitters", !alarm.Activated && !alarm.IsPressing && alarm.StrikeCount == 0 && alarm.AlarmStartCount == 0 && !mission.WarningCompleted && !player.HasPistol && alarm.villageSirens.All(s => !s.isPlaying));
                Check("one authored alarm survives restart with complete bindings", Object.FindObjectsByType<VillageAlarmButton>(FindObjectsSortMode.None).Length == 1 && mission.villageAlarm.contactPoint && player.contextualInteractions.targets.Count(t => t.kind == ContextualInteractionKind.VillageAlarm) == 1);
                player.AcquirePistol(); mission.TickMission();
                fixture = player.gameObject.AddComponent<VillageAlarmInputFixture>(); fixture.player = player; fixture.alarm = alarm; fixture.focus = alarm.contactPoint;
                player.PlaceOnFoot(alarm.approachPose.position, alarm.approachPose.eulerAngles.y); Next(13); return;
            case 13:
                if (age < .5f) return;
                if (!alarm.IsPressing) { KeyDown(); return; }
                KeyUp(); Check("interruption fixture starts before contact", !alarm.Activated);
                player.SetCinematicLocked(true); Next(14); return;
            case 14:
                if (age < .2f) return;
                Check("cinematic interruption cancels an uncommitted slap without a false alarm", !alarm.IsPressing && !alarm.hands.ButtonSlapActive && !alarm.Activated && alarm.AlarmStartCount == 0 && alarm.villageSirens.All(s => !s.isPlaying));
                Check("cancelled strike releases the additive camera lean", alarm.hands.ButtonViewOffset.sqrMagnitude < .00001f);
                player.SetCinematicLocked(false); Next(15); return;
            case 15:
                if (age < .3f) return;
                Check("cancelled strike restores the equipped pistol and allows retry", player.pistol.Available && player.pistol.Equipped && alarm.CanPress && !player.InteractionBusy);
                Finish(null); return;
        }
    }
    static void KeyDown() => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(player.interactKey));
    static void KeyUp() => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
    static string Snapshot() => !player || !alarm ? "scene=" + SceneManager.GetActiveScene().name : "reach=" + alarm.InReach + "; canPress=" + alarm.CanPress + "; eye=" + player.viewCamera.transform.position + "; target=" + player.contextualInteractions.CurrentTarget;
    static void Check(string label, bool success, string detail = "")
    {
        lines.Add((success ? "PASS " : "FAIL ") + label + " " + detail);
        File.WriteAllLines(VillageAlarmSetup.ReportFolder + "/checks.txt", lines);
        if (!success) throw new InvalidOperationException(label + " " + detail);
    }
    static void Next(int value) { phase = value; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 45; }
    static void Log(string condition, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = condition + "\n" + trace; }
    static void Finish(string message)
    {
        if (phase < 0) return;
        phase = -1; Application.logMessageReceived -= Log;
        if (fixture) fixture.FlushPhotos();
        if (Keyboard.current != null) InputSystem.ResetDevice(Keyboard.current);
        InputSystem.settings.backgroundBehavior = oldInputBackground; Application.runInBackground = oldBackground; PlayerPrefs.SetFloat("Ashfall.SfxVolume", oldSfx);
        if (game) game.maximized = oldMaximized;
        lines.Add(message == null ? "COMPLETED" : "FAILED " + message); File.WriteAllLines(VillageAlarmSetup.ReportFolder + "/checks.txt", lines);
        EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(-100)]
public sealed class VillageAlarmInputFixture : MonoBehaviour
{
    public FireRescuePlayer player;
    public VillageAlarmButton alarm;
    public Transform focus;
    public float ClosestPalmDistance { get; private set; } = float.PositiveInfinity;
    public float MaximumCapTravel { get; private set; }
    public bool SawShake { get; private set; }
    static readonly FieldInfo Yaw = typeof(FireRescuePlayer).GetField("lookYaw", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo Pitch = typeof(FireRescuePlayer).GetField("lookPitch", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo Focused = typeof(FireRescuePlayer).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic);
    readonly List<KeyValuePair<string, Texture2D>> photos = new List<KeyValuePair<string, Texture2D>>();
    string requested, completed;
    bool windup, impact;
    Vector3 headRest;
    void Start() { headRest = alarm.movingHead.position; StartCoroutine(Observe()); }
    void Update()
    {
        if (!player || !focus || player.GameplaySuspended) return;
        Focused.SetValue(player, true); Cursor.lockState = CursorLockMode.Locked;
        Vector3 delta = focus.position - (player.transform.position + Vector3.up * player.eyeHeight);
        Yaw.SetValue(player, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
        Pitch.SetValue(player, -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg);
        if (Mouse.current != null) InputSystem.ResetDevice(Mouse.current);
    }
    public bool PhotoReady(string file) { if (completed == file) return true; requested = file; return false; }
    IEnumerator Observe()
    {
        var endOfFrame = new WaitForEndOfFrame();
        while (true)
        {
            yield return endOfFrame;
            if (!string.IsNullOrEmpty(requested))
            {
                var picture = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(VillageAlarmSetup.ReportFolder + "/" + requested, picture.EncodeToPNG()); Object.Destroy(picture);
                completed = requested; requested = null;
            }
            if (!alarm || !alarm.IsPressing) continue;
            float t = alarm.PressProgress;
            MaximumCapTravel = Mathf.Max(MaximumCapTravel, Vector3.Distance(headRest, alarm.movingHead.position));
            SawShake |= alarm.cameraKick.IsShaking;
            if (t >= alarm.contactFraction - .03f && t <= alarm.contactFraction + .12f)
            {
                var animator = player.firstPerson.handsAnimator;
                Vector3 palm = (animator.GetBoneTransform(HumanBodyBones.RightIndexProximal).position + animator.GetBoneTransform(HumanBodyBones.RightLittleProximal).position) * .5f;
                ClosestPalmDistance = Mathf.Min(ClosestPalmDistance, Vector3.Distance(palm, alarm.contactPoint.position));
            }
            if (!windup && t > .12f && t < alarm.contactFraction)
            { photos.Add(new KeyValuePair<string, Texture2D>("02-palm-windup.png", ScreenCapture.CaptureScreenshotAsTexture())); windup = true; }
            if (!impact && t >= alarm.contactFraction && t < .8f)
            { photos.Add(new KeyValuePair<string, Texture2D>("03-palm-impact.png", ScreenCapture.CaptureScreenshotAsTexture())); impact = true; }
        }
    }
    public void FlushPhotos()
    {
        foreach (var item in photos) { if (!item.Value) continue; File.WriteAllBytes(VillageAlarmSetup.ReportFolder + "/" + item.Key, item.Value.EncodeToPNG()); Object.Destroy(item.Value); }
        photos.Clear();
    }
}
