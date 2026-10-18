using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class EquipmentSlotsChecks
{
    const string Pending = "EquipmentSlotsChecks.Pending";
    static EquipmentSlotsChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
            SessionState.SetBool(Pending, false);
            EditorApplication.delayCall += () => new GameObject("Equipment slots checks").AddComponent<EquipmentSlotsProbe>().Begin();
        };
    }
    [MenuItem("GameJam/Final Loop/Check equipment slots")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory("Logs/EquipmentSlots");
        File.WriteAllText("Logs/EquipmentSlots/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
}

public sealed class EquipmentSlotsProbe : MonoBehaviour
{
    [Serializable] class Check { public string name; public bool passed; }
    [Serializable] class Report
    {
        public string status;
        public List<Check> checks = new List<Check>();
        public List<string> evidence = new List<string>();
        public List<string> errors = new List<string>();
    }
    readonly Report report = new Report();
    FireRescuePlayer player;
    RescueHUD hud;
    Keyboard keyboard;
    Mouse mouse;
    int phase = -1, ammo, ammoBeforeReload, reserveBeforeReload, beganFrame;
    float began, phaseAt;
    bool oldBackground, firstUpdateRecorded;

    public void Begin()
    {
        try
        {
            oldBackground = Application.runInBackground; Application.runInBackground = true;
            Application.logMessageReceived += Log;
            var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
            mission.enabled = false; mission.cinematic.EndShot(); mission.story.CancelOpening(); mission.story.enabled = false;
            mission.encounter.enabled = false; mission.sequence.enabled = false; mission.gameFlow.enabled = false;
            player = mission.player; hud = Object.FindFirstObjectByType<RescueHUD>();
            player.SetCinematicLocked(false); player.SetGameplaySuspended(false); player.SetDriving(false);
            player.drive.body.isKinematic = true;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
            CaptureInput();
            Add("Pistol slot cannot bypass the bedroom pickup", !player.HasPistol && !player.SelectEquipmentSlot(1));
            Add("Pickup selects the existing pistol", player.AcquirePistol() && player.SelectedEquipmentSlot == 1);
            began = Time.unscaledTime; beganFrame = Time.frameCount;
            RecordHudState("After pickup setup");
            Next(0);
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    void Update()
    {
        if (phase < 0) return;
        try
        {
            if (Time.unscaledTime - began > 25f) throw new TimeoutException("Equipment checks exceeded 25 seconds.");
            float age = Time.unscaledTime - phaseAt;
            switch (phase)
            {
                case 0:
                    if (!firstUpdateRecorded)
                    {
                        RecordHudState("First probe Update, before HUD LateUpdate");
                        firstUpdateRecorded = true;
                    }
                    // Editor startup can consume the wall-clock delay before even one HUD
                    // LateUpdate has observed pickup. Require completed frames as well.
                    if (age < .6f || Time.frameCount < beganFrame + 2) return;
                    RecordHudState("First visibility assertion after completed HUD frames");
                    Add("The authored gameplay Canvas has two visible equipment slots", hud.equipmentGroup.activeInHierarchy &&
                        hud.equipmentSlots && hud.equipmentSlots.SlotCount == 2 && hud.canvas.isActiveAndEnabled &&
                        (!hud.gameplayGroup || hud.gameplayGroup.alpha > .99f));
                    Add("Pistol slot uses the imported transparent icon", hud.equipmentSlots.PistolIcon && hud.equipmentSlots.PistolIcon.sprite);
                    Keys(Key.Digit2); Next(1); break;
                case 1:
                    if (age < .1f) return;
                    Keys(); Add("Real key 2 selects empty hands", player.SelectedEquipmentSlot == 2);
                    Next(2); break;
                case 2:
                    if (age < .45f) return;
                    Add("Empty hands finish stowing the pistol", !player.pistol.Equipped && player.pistol.DrawAmount < .05f);
                    player.SetDriving(false);
                    Add("An on-foot setup call does not redraw a holstered pistol", player.SelectedEquipmentSlot == 2);
                    Keys(Key.Digit1); Next(3); break;
                case 3:
                    if (age < .1f) return;
                    Keys(); Add("Real key 1 selects the pistol", player.SelectedEquipmentSlot == 1);
                    Add("Selecting slot 1 again keeps it selected", player.SelectEquipmentSlot(1) && player.SelectedEquipmentSlot == 1);
                    Scroll(120); Next(4); break;
                case 4:
                    if (age < .1f) return;
                    Scroll(0); Add("Mouse wheel switches to empty hands", player.SelectedEquipmentSlot == 2);
                    Next(5); break;
                case 5:
                    if (age < .15f) return;
                    Scroll(-120); Next(6); break;
                case 6:
                    if (age < .1f) return;
                    Scroll(0); Add("Reverse mouse wheel switches back to pistol", player.SelectedEquipmentSlot == 1);
                    Keys(Key.Digit0); Next(7); break;
                case 7:
                    if (age < .1f) return;
                    Keys(); Add("Legacy key 0 still selects empty hands", player.SelectedEquipmentSlot == 2);
                    Time.timeScale = 0; Keys(Key.Digit1); Scroll(120); Next(8); break;
                case 8:
                    if (age < .2f) return;
                    Add("Paused keyboard and wheel cannot change equipment", player.SelectedEquipmentSlot == 2 && !player.SelectEquipmentSlot(1));
                    Keys(); Scroll(0); Time.timeScale = 1;
                    player.SetCinematicLocked(true);
                    Add("A cinematic rejects equipment selection", !player.SelectEquipmentSlot(1) && !player.CycleEquipmentSlot());
                    player.SetCinematicLocked(false); CaptureInput(); Next(9); break;
                case 9:
                    if (age < .5f) return;
                    Add("Cinematic completion preserves empty hands", player.SelectedEquipmentSlot == 2);
                    ammo = player.pistol.Ammo;
                    player.SetDriving(true);
                    Add("Driving disables pistol selection", !player.pistol.Available && !player.SelectEquipmentSlot(1) && !player.CycleEquipmentSlot());
                    Next(10); break;
                case 10:
                    if (age < .2f) return;
                    Add("Actual safe truck exit draws the owned pistol even after empty hands", player.ExitVehicle() && player.pistol.Available && player.SelectedEquipmentSlot == 1);
                    Add("Switching and vehicle transitions preserve ammunition", player.pistol.Ammo == ammo);
                    CaptureInput(); Next(11); break;
                case 11:
                    if (age < .6f) return;
                    Add("The weapon becomes ready after the normal draw animation", player.pistol.Ready);
                    Add("The displayed selected slot matches the real pistol", hud.equipmentSlots.SelectedSlot == 1 && hud.equipmentGroup.activeInHierarchy);
                    Add("A real shot prepares a partial magazine for the reload test", player.pistol.Fire());
                    Next(12); break;
                case 12:
                    if (age < .3f) return;
                    ammoBeforeReload = player.pistol.Ammo; reserveBeforeReload = player.pistol.reserveAmmo;
                    Add("The shot spends exactly one cartridge", ammoBeforeReload == ammo - 1);
                    Add("A real reload starts with a partial magazine", player.pistol.Reload());
                    Add("Empty hands during reload safely queues the physical stow", player.SelectEquipmentSlot(2) &&
                        player.pistol.IsReloading && player.pistol.HolsterPending && player.SelectedEquipmentSlot == 2);
                    Keys(Key.Digit1); Next(13); break;
                case 13:
                    if (age < .1f) return;
                    Keys();
                    Add("Selecting pistol again cancels only the queued stow", player.SelectedEquipmentSlot == 1 &&
                        player.pistol.IsReloading && !player.pistol.HolsterPending && player.pistol.Ammo == ammoBeforeReload);
                    Next(14); break;
                case 14:
                    if (age < player.pistol.reloadDuration + .2f) return;
                    Add("Reload and slot changes transfer ammo once without duplication", !player.pistol.IsReloading && player.pistol.Ready &&
                        player.pistol.Ammo == player.pistol.magazineSize &&
                        player.pistol.reserveAmmo == reserveBeforeReload - (player.pistol.magazineSize - ammoBeforeReload) &&
                        player.pistol.Ammo + player.pistol.reserveAmmo == ammoBeforeReload + reserveBeforeReload);
                    Add("The existing physical hose can be carried at the truck", player.hose.TryPickup(player.hand));
                    player.RefreshEquipmentState();
                    Add("Carrying the hose rejects both equipment slots and wheel changes", player.hose.IsCarried &&
                        !player.pistol.Available && !player.SelectEquipmentSlot(1) && !player.SelectEquipmentSlot(2) && !player.CycleEquipmentSlot());
                    Next(15); break;
                case 15:
                    if (age < .1f) return;
                    Add("Carried hose hides the equipment slots instead of becoming an inventory item", !hud.equipmentGroup.activeInHierarchy);
                    player.hose.Retract(); player.RefreshEquipmentState(); player.SelectEquipmentSlot(1);
                    Next(16); break;
                case 16:
                    if (age < .5f) return;
                    Add("Retracting the physical hose restores normal equipment selection", player.pistol.Ready && hud.equipmentGroup.activeInHierarchy);
                    ScreenCapture.CaptureScreenshot("Logs/EquipmentSlots/slots.png"); Next(17); break;
                case 17:
                    if (age < .3f) return;
                    Finish(null); break;
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    void CaptureInput()
    {
        typeof(FireRescuePlayer).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, true);
        typeof(FireRescuePlayer).GetField("waitForMouseRelease", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, false);
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
    }
    void Keys(params Key[] values) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(values));
    void Scroll(float y) => InputSystem.QueueStateEvent(mouse, new MouseState { scroll = new Vector2(0, y) });
    void Next(int next) { phase = next; phaseAt = Time.unscaledTime; }
    void RecordHudState(string when)
    {
        report.evidence.Add(when + ": elapsed=" + (Time.unscaledTime - began).ToString("F3") +
            ", frames=" + (Time.frameCount - beganFrame) + ", state=" + hud.flow.State +
            ", hasPistol=" + player.HasPistol + ", driving=" + player.IsDriving +
            ", hoseCarried=" + player.hose.IsCarried + ", cinematic=" + VolcanoCinematic.IsPlayingAny +
            ", groupSelf=" + hud.equipmentGroup.activeSelf + ", groupHierarchy=" + hud.equipmentGroup.activeInHierarchy +
            ", canvas=" + hud.canvas.isActiveAndEnabled + ", gameplayAlpha=" + (hud.gameplayGroup ? hud.gameplayGroup.alpha : 1f) +
            ", slotCount=" + (hud.equipmentSlots ? hud.equipmentSlots.SlotCount : 0));
    }
    void Add(string name, bool passed) { report.checks.Add(new Check { name = name, passed = passed }); if (!passed) report.errors.Add(name); }
    void Log(string condition, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(condition + "\n" + stack);
    }
    void Finish(string failure)
    {
        phase = -1;
        if (failure != null) report.errors.Add(failure);
        Time.timeScale = 1;
        Application.logMessageReceived -= Log; Application.runInBackground = oldBackground;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard);
        if (mouse != null) InputSystem.RemoveDevice(mouse);
        report.status = report.errors.Count == 0 ? "SUCCESS" : "FAILED";
        File.WriteAllText("Logs/EquipmentSlots/checks.json", JsonUtility.ToJson(report, true));
        EditorApplication.ExitPlaymode();
    }
}
