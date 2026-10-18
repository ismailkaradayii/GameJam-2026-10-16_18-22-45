using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using Itfaiye;
using Itfaiye.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ObsidianReloadChecks
{
    const string Pending = "ObsidianReloadChecks.Pending";
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static FireRescuePlayer player;
    static ItfaiyePistol pistol;
    static ItfaiyeWeaponReviewDriver driver;
    static GameObject target;
    static LavaCreatureActor actor;
    static InputSettings originalInput, testInput;
    static int phase = -1, frame, reserve, shots;
    static float at, nextShot, emptyAt, captureDelta;
    static double deadline, realAt;
    static bool background, transferred;
    static ObsidianReloadChecks()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Start; }
            if (state == PlayModeStateChange.ExitingPlayMode && phase >= 0) Finish("Interrupted");
        };
    }
    [MenuItem("GameJam/Final Loop/Check obsidian and auto reload")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        SessionState.SetBool(Pending + ".LavaOnly", false);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    [MenuItem("GameJam/Final Loop/Check obsidian branches")]
    public static void BeginLava()
    {
        Begin(); SessionState.SetBool(Pending + ".LavaOnly", true);
    }
    static void Start()
    {
        checks.Clear(); errors.Clear(); Directory.CreateDirectory(ObsidianReloadSetup.Folder);
        player = Object.FindFirstObjectByType<FireRescuePlayer>(); pistol = player.pistol;
        background = Application.runInBackground; Application.runInBackground = true;
        captureDelta = Time.captureDeltaTime; Time.captureDeltaTime = 1f / 60;
        originalInput = InputSystem.settings; testInput = Object.Instantiate(originalInput);
        testInput.hideFlags = HideFlags.HideAndDontSave;
        testInput.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        testInput.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings = testInput;
        EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
        Application.logMessageReceived += Log;
        deadline = EditorApplication.timeSinceStartup + 180; frame = -1; Next(0);
    }
    static void Tick()
    {
        if (phase < 0 || !EditorApplication.isPlaying || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("phase=" + phase);
            if (phase > 0) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            float age = Time.time - at;
            switch (phase)
            {
                case 0:
                    if (player.mission.State != VolcanoMissionDirector.FlowState.Preparation || player.mission.cinematic.IsPlaying) return;
                    player.mission.enabled = false;
                    if (SessionState.GetBool(Pending + ".LavaOnly", false)) { CheckBranches(); Finish(null); return; }
                    var exit = player.mission.houseExit.position;
                    Vector3 outwards = exit - player.mission.startingHouse.bounds.center; outwards.y = 0; outwards.Normalize();
                    player.PlaceOnFoot(exit + outwards * 16 + Vector3.up, Quaternion.LookRotation(-outwards).eulerAngles.y);
                    player.AcquirePistol(); player.enabled = false; pistol.SetAvailable(true); pistol.Equip();
                    driver = player.gameObject.AddComponent<ItfaiyeWeaponReviewDriver>(); driver.player = player.firstPerson; driver.pistol = pistol;
                    reserve = pistol.reserveAmmo; shots = pistol.ShotsFired; nextShot = Time.time + .7f; Next(1); break;
                case 1:
                    if (pistol.Ammo == 0) { emptyAt = Time.time; Next(2); break; }
                    if (pistol.Ammo == 1 && !target)
                    {
                        target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.name = "Last-bullet health receiver";
                        target.transform.position = pistol.muzzle.position + pistol.muzzle.forward * 9;
                        target.transform.localScale = Vector3.one * 6; target.GetComponent<Renderer>().enabled = false;
                        actor = target.AddComponent<LavaCreatureActor>();
                    }
                    FireWhenReady(); break;
                case 2:
                    if (!pistol.IsReloading) return;
                    Check("Empty magazine starts its existing reload automatically", Time.time - emptyAt < .5f && pistol.ShotsFired == shots + 12);
                    Check("Final round resolves damage and casing before reload", actor.Health == 66 && pistol.Casings.EjectedCount == 12);
                    Check("No instant refill before magazine insertion", pistol.Ammo == 0 && pistol.reserveAmmo == reserve);
                    transferred = false; Next(3); break;
                case 3:
                    if (pistol.IsReloading)
                    {
                        if (pistol.ReloadCommitted) { transferred = true; CheckOnceTransfer(); }
                        return;
                    }
                    Check("One physical reload transfers exactly 12 reserve rounds", transferred && pistol.Ammo == 12 && pistol.reserveAmmo == reserve - 12);
                    Check("Full magazine cannot consume an extra reload", !pistol.Reload());
                    Object.DestroyImmediate(target); target = null; pistol.reserveAmmo = 5; nextShot = Time.time; Next(4); break;
                case 4:
                    if (pistol.Ammo > 0) { FireWhenReady(); return; }
                    driver.enabled = false; player.enabled = true; player.SetDriving(true); Next(5); break;
                case 5:
                    if (age < .5f) return;
                    Check("Vehicle entry holds empty pistol without consuming reserve", !pistol.Available && !pistol.IsReloading && pistol.Ammo == 0 && pistol.reserveAmmo == 5);
                    player.SetCinematicLocked(true); player.SetCinematicLocked(false); player.SetDriving(false);
                    player.enabled = false; pistol.SetAvailable(true); pistol.Equip(); driver.enabled = true; Next(6); break;
                case 6:
                    if (age < .7f || pistol.IsReloading) return;
                    Check("Empty weapon resumes reload on vehicle exit with a partial reserve", pistol.Ammo == 5 && pistol.reserveAmmo == 0);
                    nextShot = Time.time; Next(7); break;
                case 7:
                    if (pistol.Ammo > 0) { FireWhenReady(); return; }
                    Next(8); break;
                case 8:
                    if (age < .6f) return;
                    Check("No infinite ammo or reload loop when reserve is empty", !pistol.IsReloading && pistol.Ammo == 0 && pistol.reserveAmmo == 0);
                    Time.timeScale = 0; pistol.reserveAmmo = 3; realAt = EditorApplication.timeSinceStartup; Next(9); break;
                case 9:
                    if (EditorApplication.timeSinceStartup - realAt < .35) return;
                    Check("Pause blocks starting auto reload", !pistol.IsReloading && pistol.Ammo == 0);
                    Time.timeScale = 1; Next(10); break;
                case 10:
                    if (age < .4f || pistol.IsReloading) return;
                    Check("Resume loads the remaining three rounds exactly once", pistol.Ammo == 3 && pistol.reserveAmmo == 0);
                    VolcanoHazardChecks.Run((name, pass, evidence) => Check(name + ": " + evidence, pass));
                    ProgressiveLavaChecks.Run();
                    Check("Existing progressive lava geometry and collision regression suite", File.ReadAllText("Library/ProgressiveLavaChecks.json").Contains("\"allPassed\": true"));
                    CheckBranches();
                    // Exercise the actual Restart button handler and inspect the newly loaded scene.
                    driver.enabled = false; player.enabled = true; player.gameFlow.RestartMission(); Next(11); break;
                case 11:
                    if (age < .4f) return;
                    var fresh = Object.FindFirstObjectByType<FireRescuePlayer>();
                    Check("Restart clears every obsidian patch and blocked branch", Object.FindObjectsByType<LavaCoolingSurface>(FindObjectsSortMode.None).All(c => c.CooledPatchCount == 0) && Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None).All(f => !f.StoppedByCooling));
                    Check("Restart resets ammo, reserve, timer and time scale", fresh && fresh != player && fresh.pistol.Ammo == 12 && fresh.pistol.reserveAmmo == reserve && Time.timeScale == 1 && !fresh.HasPistol && fresh.mission.PreparationRemaining == fresh.mission.preparationSeconds);
                    Finish(null); break;
            }
        }
        catch (Exception ex) { Finish(ex.ToString()); }
    }
    static void CheckOnceTransfer()
    {
        if (pistol.Ammo != 12 || pistol.reserveAmmo != reserve - 12) throw new Exception("Ammo transfer duplicated during reload");
    }
    static void FireWhenReady()
    {
        if (!pistol.Ready || Time.time < nextShot) return;
        if (!pistol.Fire()) throw new Exception("Fire rejected in phase " + phase);
        nextShot = Time.time + .3f;
    }
    static void CheckBranches()
    {
        var branches = Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None).Where(f => f.canBeStoppedByWater).OrderByDescending(f => f.Length).ToArray();
        Check("Actual village has water-blockable branches", branches.Length > 0);
        var encounter = Object.FindFirstObjectByType<LavaCreatureEncounter>();
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        Check("Village spirits retain a connected upstream emergence source", encounter && encounter.disperseIntoVillage &&
            encounter.spawns.Where(s => s != null && s.feed && s.feed.canBeStoppedByWater)
                .All(s => sequence.routes.Any(r => r != null && r.flow == s.feed && r.feed)));
        for (int b = 0; b < branches.Length; b++)
        {
            var branch = branches[b]; var cooling = branch.GetComponent<LavaCoolingSurface>();
            var feed = sequence.routes.First(r => r.flow == branch).feed;
            if (feed) feed.SetDistance(feed.Length);
            branch.ResetFlow(); branch.SetDistance(branch.Length * .65f);
            Physics.SyncTransforms();
            Vector3 point = ExposedPoint(branch);
            bool preview = branch.name.EndsWith("(3)");
            if (preview) Capture("hot-lava", point);
            var rig = new GameObject("Physical cooling test cannon");
            rig.transform.position = point + Vector3.up * 12; rig.transform.LookAt(point);
            var tank = rig.AddComponent<FireHoseSystem>(); tank.enabled = false; tank.SetTankLitres(1000);
            var cannon = rig.AddComponent<FireTruckWaterCannon>(); cannon.enabled = false; cannon.tank = tank;
            cannon.muzzle = rig.transform; cannon.ignoredCollisionRoot = rig.transform; cannon.muzzleSpeed = 45; cannon.flowLitresPerSecond = 45;
            bool hit = false; Vector3 impact = point;
            try
            {
                cannon.SetSpraying(true); Physics.SyncTransforms();
                for (int i = 0; i < 200; i++)
                {
                    cannon.TickSpray(.02f); branch.Advance(.02f);
                    if (cannon.HitLava == cooling) { hit = true; impact = cannon.LavaImpactPoint; }
                }
                cannon.SetSpraying(false);
                for (int i = 0; i < 80; i++) cannon.TickSpray(.02f);
                cooling.RefreshCrust();
                Physics.Raycast(rig.transform.position, rig.transform.forward, out var obstacle, 30, ~0, QueryTriggerInteraction.Ignore);
                string evidence = $" hit={hit} cold={cooling.CooledPatchCount} stopped={branch.StoppedByCooling} point={point:F2} length={branch.Length:F1} firstRayHit={obstacle.collider}";
                if (!hit || cooling.CooledPatchCount == 0 || !branch.StoppedByCooling) Capture("blocked-test-water", point, true);
                Check(branch.name + " actual water solidifies and blocks upstream supply" + evidence, hit && cooling.CooledPatchCount > 0 && !cooling.IsHotAt(impact) && branch.StoppedByCooling);
                float stopped = branch.VisibleDistance; branch.Advance(90);
                Check(branch.name + " remains blocked after 90 simulated seconds", branch.VisibleDistance == stopped);
                if (b == 0)
                {
                    var crust = branch.GetComponentsInChildren<MeshFilter>().First(f => f.name == "Water-cooled obsidian");
                    var material = crust.GetComponent<Renderer>().sharedMaterial;
                    Check("Cold obsidian has pack albedo, normal map, UVs and zero emission", material.GetTexture("_BaseMap") && material.GetTexture("_BumpMap") && material.GetColor("_EmissionColor").maxColorComponent == 0 && crust.sharedMesh.uv.Length == crust.sharedMesh.vertexCount && crust.sharedMesh.tangents.Length == crust.sharedMesh.vertexCount && crust.sharedMesh.vertexCount > 0);
                    var upstream = feed ? feed : branch;
                    float hotDistance = feed ? feed.VisibleDistance * .25f : Mathf.Max(0, branch.CoolingBarrierDistance - 2f);
                    Vector3 hotPoint = upstream.PositionAtDistance(hotDistance);
                    var hotSource = upstream.ignitionSource;
                    bool upstreamContact = hotSource && hotSource.heatCollider &&
                        hotSource.heatCollider.Raycast(new Ray(hotPoint + Vector3.up * 2, Vector3.down), out var hotHit, 4) &&
                        hotSource.IsHotAt(hotHit.point);
                    Check("The real upstream lava remains hazardous while the blocked branch solidifies", upstreamContact);
                }
                if (preview) Capture("cooled-obsidian", point);
                branch.ResetFlow(); branch.SetDistance(branch.Length * .3f);
                Check(branch.name + " reset restores hot flowing lava", !branch.StoppedByCooling && cooling.CooledPatchCount == 0 && cooling.IsHotAt(branch.FrontPosition));
                branch.ResetFlow();
                if (feed) feed.ResetFlow();
            }
            finally { Object.DestroyImmediate(rig); }
        }
    }
    static Vector3 ExposedPoint(ProgressiveLavaFlow branch)
    {
        // A cannon aimed from a roof cannot cool lava underneath the building.
        // Find a visible part of the actual branch without removing any world colliders.
        foreach (float fraction in new[] { .45f, .2f, .65f, .85f, .1f, .95f })
        {
            float distance = branch.VisibleDistance * fraction;
            Vector3 centre = branch.PositionAtDistance(distance);
            Vector3 tangent = branch.PositionAtDistance(Mathf.Min(distance + .2f, branch.VisibleDistance)) - branch.PositionAtDistance(Mathf.Max(0, distance - .2f));
            Vector3 across = Vector3.Cross(Vector3.up, tangent).normalized;
            foreach (float side in new[] { 0f, -1.8f, 1.8f })
            {
                Vector3 point = centre + across * side;
                if (Physics.Raycast(point + Vector3.up * 12, Vector3.down, out var hit, 16, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.collider.GetComponentInParent<LavaCoolingSurface>() && Mathf.Abs(hit.point.y - point.y) < .75f)
                    return hit.point;
            }
        }
        throw new Exception("No exposed water target on " + branch.name + " start=" + branch.PositionAtDistance(0) + " end=" + branch.FrontPosition);
    }
    static void Capture(string name, Vector3 point, bool above = false)
    {
        Directory.CreateDirectory(EvacuationSceneAudit.Folder);
        EvacuationSceneAudit.Capture(name, point + (above ? new Vector3(1, 35, -1) : new Vector3(8, 15, -18)), point);
        File.Copy(EvacuationSceneAudit.Folder + "/" + name + ".png", ObsidianReloadSetup.Folder + "/" + name + ".png", true);
    }
    static void Next(int value) { phase = value; at = Time.time; }
    static void Check(string name, bool pass)
    {
        checks.Add((pass ? "PASS " : "FAIL ") + name);
        File.WriteAllLines(ObsidianReloadSetup.Folder + "/checks.txt", checks);
        if (!pass) throw new Exception(name);
    }
    static void Log(string message, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message); }
    static void Finish(string error)
    {
        phase = -1; Application.logMessageReceived -= Log;
        Application.runInBackground = background; Time.timeScale = 1; Time.captureDeltaTime = captureDelta;
        if (originalInput) InputSystem.settings = originalInput;
        if (testInput) Object.DestroyImmediate(testInput);
        if (target) Object.DestroyImmediate(target);
        checks.Add(error == null && errors.Count == 0 ? "COMPLETED" : "FAILED " + error + "\n" + string.Join("\n", errors));
        File.WriteAllLines(ObsidianReloadSetup.Folder + "/checks.txt", checks);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }
}
