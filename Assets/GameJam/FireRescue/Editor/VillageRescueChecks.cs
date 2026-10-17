using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// <summary>Gameplay regressions for survivable truck fires, optional guidance and relocated rescue exits.</summary>
[InitializeOnLoad]
public static class VillageRescueChecks
{
    const string Pending = "VillageRescueChecks.Pending";
    [Serializable] sealed class Check { public string name, evidence; public bool passed; }
    [Serializable] sealed class Report { public bool allPassed; public string error; public List<Check> checks = new List<Check>(); }
    static Report report;
    static readonly List<GameObject> fixtures = new List<GameObject>();
    static readonly List<GameObject> hidden = new List<GameObject>();
    static readonly Vector3 Origin = new Vector3(17000, 500, 17000);
    static float oldScale;
    static bool oldBackground;
    static bool waitingForDoors;
    static float doorReadyTime;
    static double doorDeadline;

    static VillageRescueChecks()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += PrepareDoors; }
        };
        EditorApplication.update += () =>
        {
            if (!waitingForDoors || !EditorApplication.isPlaying) return;
            if (Time.time < doorReadyTime && EditorApplication.timeSinceStartup < doorDeadline) return;
            waitingForDoors = false; Run();
        };
    }
    [MenuItem("GameJam/Final Loop/Check village rescue polish")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Start rescue regressions in Edit Mode.");
        Directory.CreateDirectory(VillageRescuePolishSetup.Output + "/After");
        File.WriteAllText(VillageRescuePolishSetup.Output + "/After/gameplay-checks.json", "{\"status\":\"starting\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void PrepareDoors()
    {
        // Navigation through a house is tested after its real door animation, as when a villager is called.
        oldBackground = Application.runInBackground; Time.timeScale = 1; Application.runInBackground = true;
        foreach (var door in Object.FindObjectsByType<EvacuationDoor>(FindObjectsSortMode.None)) door.Open();
        doorReadyTime = Time.time + 2; doorDeadline = EditorApplication.timeSinceStartup + 20; waitingForDoors = true;
    }
    static void Run()
    {
        report = new Report(); oldScale = Time.timeScale; Time.timeScale = 1;
        try
        {
            CheckEvacuation();
            CheckSavedTruck();
            VillageRescuePolishSetup.CaptureGameplay();
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.activeSelf) { hidden.Add(root); root.SetActive(false); }
            CheckGuidance();
            CheckTruckHeat();
            FireCombatChecks.Run();
            string water = File.ReadAllText("Logs/FireCombatChecks.json");
            Add("Existing fire, ballistic water and cooling regressions pass", water.Contains("\"allPassed\": true"), "Logs/FireCombatChecks.json");
        }
        catch (Exception e) { report.error = e.ToString(); Debug.LogException(e); }
        finally
        {
            foreach (var fixture in fixtures) if (fixture) Object.DestroyImmediate(fixture); fixtures.Clear();
            foreach (var root in hidden) if (root) root.SetActive(true); hidden.Clear(); Time.timeScale = oldScale; Application.runInBackground = oldBackground;
            report.allPassed = string.IsNullOrEmpty(report.error) && report.checks.All(c => c.passed);
            File.WriteAllText(VillageRescuePolishSetup.Output + "/After/gameplay-checks.json", JsonUtility.ToJson(report, true));
            EditorApplication.ExitPlaymode();
        }
    }
    static void CheckSavedTruck()
    {
        var truck = Object.FindFirstObjectByType<FireTruckDrive>();
        Add("Saved playable fire truck has the new durability", truck && truck.Thermal && truck.Thermal.maximumHealth == 240f && truck.Thermal.automaticSuppressionLitresPerSecond > 0,
            truck ? JsonUtility.ToJson(truck.Thermal) : "missing truck");
    }
    static void CheckEvacuation()
    {
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        Add("All seven rescue residents retain their mission and real front door", mission && mission.residents.Length == 7 && mission.residents.All(r => r && r.mission == mission && r.door && r.houseExit && r.safePlace), "seven residents and their original references");
        if (!mission) return;
        foreach (var resident in mission.residents)
        {
            var path = new NavMeshPath(); bool start = NavMesh.SamplePosition(resident.transform.position, out var spawn, 3, NavMesh.AllAreas);
            bool exit = NavMesh.SamplePosition(resident.houseExit.position, out var door, 3, NavMesh.AllAreas);
            bool shelter = NavMesh.SamplePosition(mission.shelterEntrance.position, out var safe, 3, NavMesh.AllAreas);
            bool toExit = start && exit && NavMesh.CalculatePath(spawn.position, door.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            bool toShelter = exit && shelter && NavMesh.CalculatePath(door.position, safe.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
            Add(resident.name + " has a complete house exit and shelter route", toExit && toShelter,
                $"spawn={start}, exit={exit}, shelter={shelter}, inside-to-door={toExit}, door-to-shelter={toShelter}");
        }
    }
    static GameObject Fixture(string name)
    {
        var go = new GameObject(name); fixtures.Add(go); go.transform.position = Origin; return go;
    }
    static void CheckGuidance()
    {
        var nav = Fixture("Live road guidance checks").AddComponent<ObjectiveNavigation>(); nav.enabled = false;
        nav.lakeRoad = new Transform[5]; nav.drivingMarkerLookAhead = 32; nav.roadGuidanceDistance = 38;
        for (int i = 0; i < 5; i++) { nav.lakeRoad[i] = Fixture("Road sample " + i).transform; nav.lakeRoad[i].position = Origin + Vector3.right * i * 50; }
        bool outward = nav.TryGetRoadGuidance(Origin + Vector3.right * 125, -1, out var target);
        Add("Lake guide advances from a projected position between road samples", outward && Mathf.Abs(target.x - Origin.x - 93) < .01f, target.ToString("F2"));
        nav.TryGetRoadGuidance(Origin + Vector3.right * 37, -1, out var shortcut);
        Add("Skipping multiple samples cannot leave a lake marker behind the vehicle", Mathf.Abs(shortcut.x - Origin.x - 5) < .01f, shortcut.ToString("F2"));
        bool returning = nav.TryGetRoadGuidance(Origin + Vector3.right * 75, 1, out var home);
        Add("Return guide points ahead without visiting a turning checkpoint", returning && Mathf.Abs(home.x - Origin.x - 107) < .01f, home.ToString("F2"));
        bool far = nav.TryGetRoadGuidance(Origin + new Vector3(90, 0, 60), 1, out _);
        Add("A shortcut away from the road falls back to the actual destination", !far, "60m lateral offset");
        nav.lakeRoad[1] = null; nav.lakeRoad[2].position = nav.lakeRoad[3].position;
        Add("Missing and repeated guide samples remain safe", !Throws(() => nav.TryGetRoadGuidance(Origin + Vector3.right * 155, 1, out _)), "sparse and zero-length segments");
    }
    static bool Throws(Action action) { try { action(); return false; } catch { return true; } }
    static (FireTruckDrive drive, FireHoseSystem tank, ThermalDamage heat) Truck(string name, float water)
    {
        var go = Fixture(name); go.SetActive(false);
        var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(3, 3, 8); box.center = Vector3.up * 1.5f;
        go.AddComponent<Rigidbody>().isKinematic = true;
        var tank = go.AddComponent<FireHoseSystem>(); var socket = new GameObject("Intake socket").transform; socket.SetParent(go.transform, false); tank.hoseSocket = socket;
        var heat = go.AddComponent<ThermalDamage>(); var drive = go.AddComponent<FireTruckDrive>(); heat.ConfigureRescueTruck();
        go.SetActive(true); drive.enabled = tank.enabled = heat.enabled = false;
        // TickHeat intentionally requires an enabled target; this fixture is stepped synchronously before the next frame.
        heat.enabled = true; tank.SetTankLitres(water); return (drive, tank, heat);
    }
    static void Step(ThermalDamage heat, float seconds)
    {
        int ticks = Mathf.CeilToInt(seconds / .25f); for (int i = 0; i < ticks; i++) heat.TickHeat(Mathf.Min(.25f, seconds - i * .25f));
    }
    static void CheckTruckHeat()
    {
        var dry = Truck("Empty rescue truck under sustained fire", 0);
        dry.heat.Ignite(); Step(dry.heat, 3);
        Add("Three seconds of environmental fire allow the truck to escape unharmed", Mathf.Approximately(dry.heat.Health, 240), "health=" + dry.heat.Health);
        Step(dry.heat, 60);
        Add("An empty truck survives a full minute of afterburn with useful motor and pump", !dry.heat.IsDestroyed && dry.heat.Integrity01 > .65f && dry.heat.MotorEfficiency == 1 && dry.heat.PumpEfficiency == 1,
            $"health={dry.heat.Health:F1}, motor={dry.heat.MotorEfficiency:F2}, pump={dry.heat.PumpEfficiency:F2}");
        var wet = Truck("Automatic onboard fire suppression", 1000); wet.heat.Ignite(); Step(wet.heat, 5);
        Add("Onboard suppression extinguishes afterburn using a small, real water budget", !wet.heat.IsBurning && wet.tank.TankLitres >= 955 && wet.tank.TankLitres <= 960 && wet.heat.Integrity01 > .98f,
            $"water={wet.tank.TankLitres:F1}, health={wet.heat.Health:F1}");
        Add("Wet vehicle panels prevent immediate repeated spirit ignition", !wet.heat.IgniteFromSpirit(), "ten-second wet protection");
        var impact = Truck("Repeated fire spirit impacts", 0); impact.heat.IgniteFromSpirit(); float first = impact.heat.Health;
        for (int i = 0; i < 12; i++) impact.heat.IgniteFromSpirit();
        Add("Overlapping spirit explosions cannot stack vehicle damage in one moment", Mathf.Approximately(first, 234) && Mathf.Approximately(impact.heat.Health, first), "health=" + impact.heat.Health);
        var manual = Truck("Manual truck fire suppression", 1000); manual.heat.Ignite(); float before = manual.heat.Health;
        for (int i = 0; i < 4; i++) manual.heat.SuppressFromTank(manual.tank, .25f);
        Add("Holding X still extinguishes fire without repairing damage", !manual.heat.IsBurning && Mathf.Approximately(manual.heat.Health, before) && manual.tank.TankLitres < 1000,
            "water=" + manual.tank.TankLitres);
        var exposed = Truck("Truck touching real hot lava", 1000); exposed.drive.transform.position = Origin + Vector3.right * 40;
        var lava = Fixture("Actual shallow lava contact"); lava.transform.position = exposed.drive.transform.position + Vector3.up * .1f;
        var lavaBox = lava.AddComponent<BoxCollider>(); lavaBox.size = new Vector3(12, .5f, 12); lavaBox.isTrigger = true;
        var source = lava.AddComponent<LavaIgnitionSource>(); source.heatCollider = lavaBox;
        Physics.SyncTransforms(); Step(exposed.heat, 5);
        Add("Real lava contact is detected and no longer destroys the truck in five seconds", exposed.heat.InHotLava && exposed.heat.Health > 220,
            "health=" + exposed.heat.Health);
        Add("Automatic suppression does not waste the tank while still standing in lava", Mathf.Approximately(exposed.tank.TankLitres, 1000), "water=" + exposed.tank.TankLitres);
        exposed.drive.transform.position += Vector3.right * 20; Physics.SyncTransforms(); Step(exposed.heat, 3);
        Add("Leaving lava lets onboard water extinguish the remaining fire", !exposed.heat.InHotLava && !exposed.heat.IsBurning && !exposed.heat.IsDestroyed,
            "health=" + exposed.heat.Health);
        var doomed = Truck("Truck left parked in lava", 0); doomed.drive.transform.position = lava.transform.position; Physics.SyncTransforms(); Step(doomed.heat, 42);
        Add("Sustained lava remains dangerous and eventually disables motor and pump", doomed.heat.IsDestroyed && doomed.heat.MotorEfficiency == 0 && doomed.heat.PumpEfficiency == 0, "health=" + doomed.heat.Health);
        var paused = Truck("Paused truck fire", 1000); paused.heat.Ignite(); Time.timeScale = 0; Step(paused.heat, 20); Time.timeScale = 1;
        Add("Pausing freezes heat damage and automatic water consumption", paused.heat.Health == 240 && paused.tank.TankLitres == 1000 && paused.heat.IsBurning, "pause preservation");
        var civilian = Fixture("Civilian thermal defaults").AddComponent<ThermalDamage>(); civilian.Ignite(); Step(civilian, 1);
        Add("Civilian fire damage keeps its original balance", Mathf.Approximately(civilian.Health, 84), "health=" + civilian.Health);
    }
    static void Add(string name, bool passed, string evidence) => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
}
