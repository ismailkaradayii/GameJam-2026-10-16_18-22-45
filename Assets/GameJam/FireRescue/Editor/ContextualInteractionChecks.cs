using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Saved-scene, real-frame integration review. All temporary fixtures are Play Mode only.</summary>
[InitializeOnLoad]
public static class ContextualInteractionChecks
{
    const string Pending = "GameJam.ContextualInteraction.Pending";
    static readonly List<string> lines = new List<string>();
    static ContextualInteractionUI ui;
    static VolcanoMissionDirector mission;
    static FireRescuePlayer player;
    static ContextualInteractionReviewCamera cameraFixture;
    static ContextualInteractionTarget pickup, lake, exit, door, truck;
    static EditorWindow game;
    static bool oldMaximized, oldBackground, sawIntro;
    static int phase = -1, frame, beforeFeedback, beforeSafe, resolution;
    static float phaseAt, initialTank;
    static double deadline;
    static string runtimeError;
    static GameObject wall;
    static object sizeGroup;
    static readonly List<int> customSizes = new List<int>();
    static int originalSize;
    static int originalMission;
    static string pendingCapture;
    static int captureFrame;
    static readonly Vector2Int[] sizes = { new Vector2Int(1920,1080), new Vector2Int(1280,720), new Vector2Int(1024,768), new Vector2Int(2560,1080) };

    static ContextualInteractionChecks()
    {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Review interrupted.");
        };
    }
    [MenuItem("GameJam/Final Loop/Check contextual interaction UI")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath || SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Open the saved Volcano_Demo in Edit Mode first.");
        ContextualInteractionSetup.Validate();
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Start()
    {
        Directory.CreateDirectory(ContextualInteractionSetup.ReportFolder); lines.Clear(); runtimeError = null; sawIntro = false; customSizes.Clear();
        lines.Add("LIMITATION: real-frame Play Mode review of the saved scene. Mechanics invoked through existing APIs; temporary camera, wall, NavMesh warps and lake parking fixtures isolate UI behavior. This is not a manual keyboard playthrough or full truck road test.");
        ui = Object.FindFirstObjectByType<ContextualInteractionUI>(); mission = ui.mission; player = ui.player;
        originalMission = mission.GetInstanceID();
        pickup = Find(ContextualInteractionKind.Pistol); lake = Find(ContextualInteractionKind.LakeFill); exit = Find(ContextualInteractionKind.TruckExit);
        door = Find(ContextualInteractionKind.Door); truck = Find(ContextualInteractionKind.TruckEntry);
        oldBackground = Application.runInBackground; Application.runInBackground = true;
        game = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")); oldMaximized = game.maximized;
        game.Focus(); game.maximized = true;
        Application.logMessageReceived += Log; Next(0);
    }
    static ContextualInteractionTarget Find(ContextualInteractionKind kind) => ui.targets.First(t => t && t.kind == kind);
    static void Update()
    {
        if (phase < 0 || !EditorApplication.isPlaying || EditorApplication.isPaused || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (runtimeError != null) throw new InvalidOperationException(runtimeError);
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Phase " + phase + "; " + Snapshot());
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
                if (mission.cinematic.IsPlaying) { sawIntro |= !ui.HasVisiblePrompt && ui.ActiveFeedbackCount == 0; return; }
                if (mission.State != VolcanoMissionDirector.FlowState.Preparation || age < 3) return;
                Check("intro hides contextual labels", sawIntro);
                Check("one bound reusable Canvas and seven resident anchors", Object.FindObjectsByType<ContextualInteractionUI>(FindObjectsSortMode.None).Length == 1 &&
                    ui.targets.Count(t => t.kind == ContextualInteractionKind.Resident) == 7 && mission.evacuation.TotalCount == 7);
                mission.enabled = false; mission.encounter.enabled = false;
                cameraFixture = player.gameObject.AddComponent<ContextualInteractionReviewCamera>(); cameraFixture.viewCamera = player.viewCamera;
                Foot(mission.standingSpawn.position, pickup.FocusPosition(ui));
                ui.RefreshSelection(); Check("bedside target uses E within the existing pickup range", ui.CurrentTarget == pickup && ui.Label(ui.CurrentOffer.input) == "[E]", Snapshot());
                Next(1); return;
            case 1:
                if (age < .65f) return;
                if (!Capture("01-pistol-prompt.png")) return;
                Check("TMP label fades in without a background panel", ui.HasVisiblePrompt && ui.promptGroup.alpha > .95f && ui.titleText.text == ui.words.takePistol);
                Check("large E key really produces visible TMP glyphs", ui.keyText.isActiveAndEnabled && ui.keyText.text == "[E]" && ui.keyText.textInfo.characterInfo.Take(ui.keyText.textInfo.characterCount).Count(c => c.isVisible) == 3);
                Check("legacy bottom interaction panel is suppressed; quest HUD stays active", !ui.hud.promptText.transform.parent.gameObject.activeSelf && ui.hud.questTitleText.isActiveAndEnabled);
                Check("Turkish glyphs exist in the actual prompt font", "ÇĞİÖŞÜçğıöşü".All(c => ui.titleText.font.HasCharacter(c)));
                Vector3 eye = player.viewCamera.transform.position, focus = pickup.FocusPosition(ui);
                cameraFixture.Look(eye, eye - (focus - eye)); ui.RefreshSelection();
                Check("looking away removes the pickup candidate", ui.CurrentTarget != pickup);
                cameraFixture.Look(eye, focus);
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Review temporary occluder";
                wall.transform.position = Vector3.Lerp(eye, focus, .5f); wall.transform.localScale = new Vector3(.7f, 1.5f, .7f); Physics.SyncTransforms();
                ui.RefreshSelection(); Check("solid wall occludes the pistol", ui.CurrentTarget != pickup && !mission.weaponPickup.CanPickUp);
                wall.GetComponent<Collider>().enabled = false; Object.Destroy(wall); wall = null; Physics.SyncTransforms();
                Foot(mission.standingSpawn.position + Vector3.right * 25, focus); ui.RefreshSelection();
                Check("outside pickup range there is no pickup prompt", ui.CurrentTarget != pickup);
                Foot(mission.standingSpawn.position, focus); ui.RefreshSelection();
                player.interactKey = Key.G;
                Check("Inspector input change updates the real Input System key label", ui.Label(InteractionInput.Interact) == "[G]");
                player.interactKey = Key.E; beforeFeedback = ui.FeedbackCount;
                Check("selected E action collects the real pistol into slot 1", player.Interact() && player.HasPistol && mission.weaponPickup.IsCollected && player.pistol.Available);
                Check("one real pickup produces one floating result", ui.FeedbackCount == beforeFeedback + 1 && ui.ActiveFeedbackCount > 0);
                Check("duplicate pickup cannot produce duplicate feedback", !mission.weaponPickup.TryPickUp() && ui.FeedbackCount == beforeFeedback + 1);
                mission.TickMission();
                Check("pickup respects the authored second objective", mission.villageAlarm ? mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage && !mission.WarningCompleted :
                    mission.Phase == VolcanoMissionDirector.MissionPhase.Weapon && !mission.LeftStartingHouse);
                Next(2); return;
            case 2:
                if (age < .35f) return;
                if (!Capture("02-pickup-feedback.png")) return;
                Vector3 center = door.FocusPosition(ui);
                Vector3 approach = center - door.door.transform.forward * 2f - Vector3.up * player.eyeHeight;
                Foot(approach, center); ui.RefreshSelection();
                Check("solid door owns the exact forward interaction ray", ui.CurrentTarget == door && ui.CurrentOffer.input == InteractionInput.Interact, Snapshot());
                bool toggled = player.Interact();
                Check("existing door toggle or actual obstruction feedback is preserved", toggled || door.door.LastBlocked);
                beforeFeedback = ui.FeedbackCount;
                Foot(mission.houseExit.position + mission.houseExit.forward * 4, mission.houseExit.position + Vector3.up * 2);
                mission.TickMission();
                if (mission.villageAlarm)
                {
                    Check("leaving the house cannot skip the new alarm mission", mission.LeftStartingHouse && !mission.evacuation.IsActive && mission.Phase == VolcanoMissionDirector.MissionPhase.WarnVillage);
                    Foot(mission.villageAlarm.approachPose.position, mission.villageAlarm.contactPoint.position);
                    Next(15); return;
                }
                Check("real exit condition advances to evacuation; skipped church shows no prompt", mission.LeftStartingHouse && mission.evacuation.IsActive && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate);
                Check("real objective completion emits floating feedback", ui.FeedbackCount > beforeFeedback);
                goto case 16;
            case 15:
                if (age < .5f) return;
                if (!mission.villageAlarm.Activated && !mission.villageAlarm.IsPressing)
                {
                    ui.RefreshSelection();
                    Check("central E prompt activates the real village alarm", ui.CurrentTarget && ui.CurrentTarget.kind == ContextualInteractionKind.VillageAlarm && player.Interact(), Snapshot());
                    return;
                }
                if (mission.villageAlarm.IsPressing) return;
                Check("only the real alarm strike advances evacuation", mission.villageAlarm.Activated && mission.WarningCompleted && mission.evacuation.IsActive && mission.Phase == VolcanoMissionDirector.MissionPhase.Evacuate);
                Check("alarm completion emits real floating feedback", ui.FeedbackCount > beforeFeedback);
                goto case 16;
            case 16:
                var call = Find(ContextualInteractionKind.CallHouse);
                Foot(call.anchor.position + Vector3.forward * 5, call.FocusPosition(ui)); ui.RefreshSelection();
                Check("village call is Q, never a fabricated E rescue", ui.Describe(call, out var offer) && offer.input == InteractionInput.CallVillagers && ui.Label(offer.input) == "[Q]");
                Check("real shout calls residents", mission.evacuation.Shout() && mission.evacuation.CalledCount > 0);
                var called = ui.targets.First(t => t.kind == ContextualInteractionKind.Resident && t.resident.IsCalled);
                Check("following is automatic and has no interaction key", ui.Describe(called, out var following) && following.input == InteractionInput.None);
                beforeSafe = mission.evacuation.RescuedCount; beforeFeedback = ui.FeedbackCount;
                foreach (var resident in mission.evacuation.residents)
                {
                    if (!resident.IsCalled) resident.CallOutside();
                    if (!NavMesh.SamplePosition(resident.safePlace.position, out var safe, 2, resident.Agent.areaMask) || !resident.Agent.Warp(safe.position))
                        throw new InvalidOperationException("Cannot place rescue UI fixture on the actual shelter NavMesh: " + resident.name);
                }
                var shelter = Find(ContextualInteractionKind.Shelter);
                Foot(shelter.anchor.position + Vector3.forward * 5, shelter.FocusPosition(ui));
                Check("shelter admission has no artificial input", ui.Describe(shelter, out var shelterOffer) && shelterOffer.input == InteractionInput.None);
                Next(3); return;
            case 3:
                if (!mission.evacuation.IsComplete) return;
                Check("seven actual shelter arrivals update the real evacuation count", mission.evacuation.RescuedCount == 7 && beforeSafe == 0);
                Check("each counted arrival emits feedback without awarding invented score", ui.FeedbackCount == beforeFeedback + 7);
                int counted = ui.FeedbackCount;
                foreach (var resident in mission.evacuation.residents) mission.evacuation.ResidentArrived(resident);
                Check("duplicate shelter notifications do not duplicate floating results", ui.FeedbackCount == counted && mission.evacuation.RescuedCount == 7);
                mission.TickMission();
                Check("evacuation advances the existing truck objective", mission.Phase == VolcanoMissionDirector.MissionPhase.Truck);
                Vector3 doorPoint = truck.FocusPosition(ui);
                Vector3 feet = doorPoint - player.drive.transform.right * 1.2f; feet.y = player.drive.transform.position.y + .1f;
                Foot(feet, doorPoint); ui.RefreshSelection();
                Check("truck entry prompt is selected at the cab", ui.CurrentTarget && ui.CurrentTarget.kind == ContextualInteractionKind.TruckEntry && ui.Label(ui.CurrentOffer.input) == "[E]", Snapshot());
                Next(4); return;
            case 4:
                if (age < .45f) return;
                if (!Capture("03-truck-entry.png")) return;
                player.hose.Drop(player.hose.hoseSocket.position); ui.RefreshSelection();
                Check("deployed hose explains why entry is blocked; E cannot perform another action", ui.Describe(truck, out var blockedEntry) && blockedEntry.blocked && blockedEntry.detail.Contains("[R]") && !player.Interact());
                player.hose.Retract(); player.RefreshEquipmentState(); ui.RefreshSelection();
                Check("visible E prompt invokes the existing enter-vehicle mechanic", player.Interact() && player.IsDriving, Snapshot());
                mission.TickMission();
                Check("truck objective advances to refill", mission.Phase == VolcanoMissionDirector.MissionPhase.Refill);
                cameraFixture.Look(player.drive.transform.position - player.drive.transform.forward * 23 + Vector3.up * 8, exit.FocusPosition(ui)); ui.RefreshSelection();
                Check("vehicle exit shows E", ui.CurrentTarget == exit && ui.Label(ui.CurrentOffer.input) == "[E]", Snapshot());
                player.gameFlow.Pause(); Next(5); return;
            case 5:
                if (age < .25f) return;
                Check("pause hides the prompt and all feedback", !ui.HasVisiblePrompt && ui.ActiveFeedbackCount == 0);
                player.gameFlow.Resume(); Next(6); return;
            case 6:
                if (age < .35f) return;
                player.SetCinematicLocked(true); Next(7); return;
            case 7:
                if (age < .2f) return;
                Check("cinematic control lock hides prompt and feedback", !ui.HasVisiblePrompt && ui.ActiveFeedbackCount == 0);
                player.SetCinematicLocked(false);
                var body = player.drive.GetComponent<Rigidbody>();
                body.position = mission.fillStation.transform.position + Vector3.up * .65f; body.rotation = Quaternion.identity;
                body.linearVelocity = body.angularVelocity = Vector3.zero; player.drive.SetInput(0, 0, true); Physics.SyncTransforms();
                cameraFixture.Look(body.position + new Vector3(0, 10, -23), lake.FocusPosition(ui)); Next(8); return;
            case 8:
                if (age < 1.5f) return;
                ui.RefreshSelection();
                if (!Capture("05-lake-eligibility.png")) return;
                Check("lake prompt uses real guided fill eligibility", ui.CurrentTarget == lake && ui.CurrentOffer.input == InteractionInput.Pump && !ui.CurrentOffer.blocked, Snapshot());
                initialTank = player.hose.Tank01;
                Check("guided fill starts the actual pump", mission.fillStation.Interact() && mission.fillStation.IsFilling && player.hose.IsPumping);
                Next(9); return;
            case 9:
                if (age < .8f) return;
                ui.RefreshSelection();
                if (!Capture("04-lake-progress.png")) return;
                Check("automatic fill displays actual tank progress with P disconnect", ui.CurrentTarget == lake && ui.CurrentOffer.showProgress && ui.CurrentOffer.input == InteractionInput.Pump &&
                    player.hose.Tank01 > initialTank && Mathf.Abs(ui.CurrentOffer.progress - player.hose.Tank01) < .002f, Snapshot());
                Check("progress is rendered by the prefab", ui.progressGroup.activeSelf && ui.progressFill.fillAmount > 0);
                Check("P disconnect cancels the original pump and retracts the hose", mission.fillStation.Interact() && !mission.fillStation.IsFilling && !player.hose.IsDeployed);
                // Camera and input remain live for multi-resolution projection checks.
                resolution = 0; SetResolution(sizes[resolution]); Next(10); return;
            case 10:
                if (age < .6f) return;
                ui.RefreshSelection();
                Check("prompt fits actual Game View " + Screen.width + "x" + Screen.height, Screen.width == sizes[resolution].x && Screen.height == sizes[resolution].y && Fits(ui.promptRect), Snapshot());
                Check("world position tracks the target at this resolution", ui.CurrentTarget == lake && ui.promptGroup.alpha > .95f);
                if (++resolution < sizes.Length) { SetResolution(sizes[resolution]); Next(10); return; }
                RestoreResolution();
                var temporary = new GameObject("Review destroyed target").AddComponent<ContextualInteractionTarget>();
                var original = ui.targets; ui.targets = original.Concat(new[] { temporary }).ToArray(); Object.DestroyImmediate(temporary);
                ui.RefreshSelection(); ui.targets = original;
                Check("destroyed target reference is ignored safely", ui.CurrentTarget == lake);
                var fire = ui.targets.First(t => t.kind == ContextualInteractionKind.BuildingFire);
                fire.building.Ignite(); beforeFeedback = ui.FeedbackCount;
                fire.building.ApplyWater(fire.building.litresToExtinguish * .25f);
                Check("partial water does not invent extinguished feedback", fire.building.IsBurning && ui.FeedbackCount == beforeFeedback);
                fire.building.ApplyWater(fire.building.litresToExtinguish);
                Check("actual extinguish event emits one floating result", fire.building.IsRescued && ui.FeedbackCount == beforeFeedback + 1);
                fire.building.ApplyWater(fire.building.litresToExtinguish);
                Check("already extinguished fire cannot duplicate feedback", ui.FeedbackCount == beforeFeedback + 1);
                player.gameFlow.FinishMission(false); Next(11); return;
            case 11:
                if (age < player.gameFlow.resultDelaySeconds + ui.hud.outcomeFadeInSeconds + .2f) return;
                Check("FAILED result hides all contextual UI", !ui.HasVisiblePrompt && ui.ActiveFeedbackCount == 0 && ui.hud.VictoryVisible);
                Check("no missing references or TMP geometry errors during review", runtimeError == null);
                player.gameFlow.RestartMission(); Next(12); return;
            case 12:
                var fresh = Object.FindFirstObjectByType<ContextualInteractionUI>();
                if (!fresh || fresh.mission.GetInstanceID() == originalMission || fresh.mission.cinematic.IsPlaying || fresh.mission.State != VolcanoMissionDirector.FlowState.Preparation) return;
                ui = fresh; mission = ui.mission; player = ui.player;
                pickup = Find(ContextualInteractionKind.Pistol); lake = Find(ContextualInteractionKind.LakeFill);
                Check("restart loads one clean prefab with no old rescue or feedback state", Object.FindObjectsByType<ContextualInteractionUI>(FindObjectsSortMode.None).Length == 1 &&
                    ui.FeedbackCount == 0 && ui.mission.evacuation.RescuedCount == 0 && !player.HasPistol && player.contextualInteractions == ui && ui.hud.contextualInteractions == ui);
                player.gameFlow.FinishMission(true); Next(13); return;
            case 13:
                if (age < player.gameFlow.resultDelaySeconds + ui.hud.outcomeFadeInSeconds + .2f) return;
                Check("SUCCESS result hides all contextual UI", !ui.HasVisiblePrompt && ui.ActiveFeedbackCount == 0 && ui.hud.VictoryVisible);
                player.gameFlow.ReturnToMainMenu(); Next(14); return;
            case 14:
                if (SceneManager.GetActiveScene().name != "MainMenu") return;
                Check("Main Menu has no carried-over contextual Canvas or anchors", !Object.FindFirstObjectByType<ContextualInteractionUI>() && !Object.FindFirstObjectByType<ContextualInteractionTarget>());
                Check("UI review leaves no timescale or input capture behind in the menu", Mathf.Approximately(Time.timeScale, 1) && Cursor.lockState != CursorLockMode.Locked);
                Finish(null); return;
        }
    }
    static void Foot(Vector3 feet, Vector3 focus)
    {
        Vector3 flat = focus - feet; flat.y = 0;
        player.PlaceOnFoot(feet, Quaternion.LookRotation(flat.normalized).eulerAngles.y);
        cameraFixture.Look(feet + Vector3.up * player.eyeHeight, focus);
        Cursor.lockState = CursorLockMode.Locked; Physics.SyncTransforms();
    }
    static bool Fits(RectTransform rect)
    {
        var corners = new Vector3[4]; rect.GetWorldCorners(corners); var safe = Screen.safeArea;
        return corners.All(c => c.x >= safe.xMin - 1 && c.x <= safe.xMax + 1 && c.y >= safe.yMin - 1 && c.y <= safe.yMax + 1);
    }
    static void SetResolution(Vector2Int size)
    {
        var assembly = typeof(Editor).Assembly;
        Type sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
        object sizesObject = singleton.GetProperty("instance").GetValue(null);
        object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizesObject);
        sizeGroup = sizesType.GetMethod("GetGroup").Invoke(sizesObject, new[] { groupType });
        Type group = sizeGroup.GetType();
        int count = (int)group.GetMethod("GetTotalCount").Invoke(sizeGroup, null);
        if (customSizes.Count == 0) originalSize = (int)game.GetType().GetProperty("selectedSizeIndex").GetValue(game);
        Type sizeType = assembly.GetType("UnityEditor.GameViewSize"), modeType = assembly.GetType("UnityEditor.GameViewSizeType");
        object item = Activator.CreateInstance(sizeType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { Enum.Parse(modeType, "FixedResolution"), (object)size.x, size.y, "Contextual UI review " + size }, null);
        group.GetMethod("AddCustomSize").Invoke(sizeGroup, new[] { item }); customSizes.Add(count);
        game.GetType().GetProperty("selectedSizeIndex").SetValue(game, count); game.Repaint();
    }
    static void RestoreResolution()
    {
        if (customSizes.Count == 0 || sizeGroup == null) return;
        game.GetType().GetProperty("selectedSizeIndex").SetValue(game, originalSize);
        foreach (int index in customSizes.AsEnumerable().Reverse()) sizeGroup.GetType().GetMethod("RemoveCustomSize").Invoke(sizeGroup, new object[] { index });
        customSizes.Clear(); sizeGroup = null;
    }

    [MenuItem("GameJam/Final Loop/Clean contextual review sizes")]
    public static void CleanReviewSizes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var assembly = typeof(Editor).Assembly;
        var sizesType = assembly.GetType("UnityEditor.GameViewSizes");
        object sizesObject = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
        object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizesObject);
        object group = sizesType.GetMethod("GetGroup").Invoke(sizesObject, new[] { groupType });
        Type type = group.GetType();
        int total = (int)type.GetMethod("GetTotalCount").Invoke(group, null);
        int builtin = (int)type.GetMethod("GetBuiltinCount").Invoke(group, null);
        int removed = 0;
        for (int i = total - 1; i >= builtin; i--)
        {
            object item = type.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
            string name = (string)item.GetType().GetProperty("baseText", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(item);
            if (!name.StartsWith("Contextual UI review ", StringComparison.Ordinal)) continue;
            type.GetMethod("RemoveCustomSize").Invoke(group, new object[] { i }); removed++;
        }
        File.WriteAllText(ContextualInteractionSetup.ReportFolder + "/review-cleanup.txt", "Removed only temporary Contextual UI review sizes: " + removed);
    }
    static bool Capture(string file)
    {
        if (pendingCapture != file)
        {
            pendingCapture = file; captureFrame = Time.frameCount;
            ScreenCapture.CaptureScreenshot(ContextualInteractionSetup.ReportFolder + "/" + file); return false;
        }
        return Time.frameCount > captureFrame + 1;
    }
    static void Check(string name, bool success, string detail = null)
    {
        lines.Add((success ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " | " + detail));
        File.WriteAllLines(ContextualInteractionSetup.ReportFolder + "/checks.txt", lines);
        if (!success) throw new InvalidOperationException(name + "; " + detail);
    }
    static string Snapshot() => !ui || !player ? "scene=" + SceneManager.GetActiveScene().name : "target=" + (ui.CurrentTarget ? ui.CurrentTarget.name : "none") + "; title=" + ui.CurrentOffer.title + "; present=" + ui.CanPresent +
        "; camera=" + player.viewCamera.transform.position.ToString("F2") + "; pickup=" + mission.weaponPickup.CanPickUp + "; LOS=" + ui.HasLineOfSight(pickup, pickup.FocusPosition(ui)) +
        (lake ? "; lake inRange=" + mission.fillStation.InRange + ", canFill=" + mission.fillStation.CanFill + ", description=" + ui.Describe(lake, out _) + ", LOS=" + ui.HasLineOfSight(lake, lake.FocusPosition(ui)) + ", speed=" + player.drive.SpeedKph.ToString("F2") : "");
    static void Next(int next) { phase = next; phaseAt = Time.unscaledTime; deadline = EditorApplication.timeSinceStartup + 35; }
    static void Log(string condition, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) runtimeError = condition + "\n" + trace;
        if (type == LogType.Warning && (condition.Contains("character") || condition.Contains("font") || condition.Contains("Font"))) runtimeError = condition;
    }
    static void Finish(string error)
    {
        if (phase < 0) return;
        phase = -1; Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
        try { RestoreResolution(); } catch (Exception ex) { lines.Add("FAIL restoring Game View size: " + ex); }
        if (game) game.maximized = oldMaximized;
        lines.Add(error == null ? "COMPLETED" : "FAILED " + error);
        File.WriteAllLines(ContextualInteractionSetup.ReportFolder + "/checks.txt", lines);
        EditorApplication.ExitPlaymode();
    }
}

[DefaultExecutionOrder(295)]
public sealed class ContextualInteractionReviewCamera : MonoBehaviour
{
    public Camera viewCamera;
    Vector3 position;
    Quaternion rotation;
    public void Look(Vector3 eye, Vector3 focus)
    { position = eye; rotation = Quaternion.LookRotation(focus - eye); Apply(); }
    void LateUpdate() => Apply();
    void Apply() { if (viewCamera) viewCamera.transform.SetPositionAndRotation(position, rotation); }
}
