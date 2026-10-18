using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run in an isolated batch project, never in the shared gameplay/editor session.
public static class SpiritTruckTacticsChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] class Report { public List<string> passed = new List<string>(); public string error; }
    static Report report;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated Unity batch project for these checks.");
        report = new Report();
        try { CheckRoster(); CheckTruck(); }
        catch (Exception ex) { report.error = ex.ToString(); }
        Directory.CreateDirectory("Logs/SpiritTruckTactics");
        File.WriteAllText("Logs/SpiritTruckTactics/checks.json", JsonUtility.ToJson(report, true));
        Debug.Log(JsonUtility.ToJson(report, true));
        EditorApplication.Exit(report.error == null ? 0 : 1);
    }

    static void CheckRoster()
    {
        var houses = new List<BurnableBuilding>();
        for (int i = 0; i < 3; i++)
        {
            var root = new GameObject("Assigned house " + i);
            root.transform.position = new Vector3(1000 + i * 30, 0, 0);
            var box = root.AddComponent<BoxCollider>(); box.size = new Vector3(8, 8, 8);
            var house = root.AddComponent<BurnableBuilding>(); house.ignitionBounds = box; house.fitFireToBuilding = false;
            Property(house, "IsDefenseFireTarget", true); Call(house, "OnEnable"); houses.Add(house);
        }
        var encounter = new GameObject("Roster probe").AddComponent<LavaCreatureEncounter>();
        encounter.buildingAttackShare = .3f; encounter.waveSizes = new[] { 10 };
        for (int i = 0; i < 10; i++)
        {
            var actor = Creature(new Vector3(2000 + i * 5, 0, 0)); actor.gameObject.SetActive(false);
            var spawn = new LavaCreatureEncounter.Spawn { creature = actor };
            Check((bool)Call(encounter, "Release", spawn, actor), "Release " + i);
        }
        var roster = encounter.LivingCreatures.Select(a => a.GetComponent<LavaCreatureCombat>()).ToArray();
        var attackers = roster.Where(c => c.attackBuildings).ToArray();
        Check(attackers.Length == 3 && roster.Count(c => !c.attackBuildings) == 7, "Exactly three house attackers and seven hunters");
        Check(attackers.Select(c => c.AssignedBuilding).Distinct().Count() == 3 && attackers.All(c => houses.Contains(c.AssignedBuilding)),
            "Each attacker owns a distinct mission house");
        foreach (var attacker in attackers)
        {
            Field(attacker, "retaliationUntil", Time.time + 100f);
            Check(attacker.PursuingBuilding && (bool)Call(attacker, "FindBuilding") && attacker.BuildingTarget == attacker.AssignedBuilding,
                "House assignment survives retaliation");
        }
        Property(attackers[0].AssignedBuilding, "IsRescued", true);
        Check(!(bool)Call(attackers[0], "FindBuilding") && attackers[0].AssignedBuilding, "Rescued house cannot redirect its assigned attacker");
        var shuffle = typeof(LavaCreatureEncounter).GetMethod("CreateAttackOrder", BindingFlags.Static | BindingFlags.NonPublic);
        var patterns = new HashSet<string>();
        for (int i = 0; i < 32; i++)
        {
            var order = (bool[])shuffle.Invoke(null, new object[] { 10, .3f });
            if (order.Count(x => x) != 3) throw new Exception("Incorrect randomized role count");
            patterns.Add(string.Concat(order.Select(x => x ? "H" : "P")));
        }
        Check(patterns.Count > 1, "Random arrivals retain the 3/7 split across 32 shuffles");
        foreach (var actor in encounter.LivingCreatures) Object.DestroyImmediate(actor.gameObject);
        Object.DestroyImmediate(encounter.gameObject);
        foreach (var house in houses) { Call(house, "OnDisable"); Object.DestroyImmediate(house.gameObject); }
    }

    static void CheckTruck()
    {
        var truckRoot = new GameObject("Raised truck chassis");
        var box = truckRoot.AddComponent<BoxCollider>(); box.center = new Vector3(0, 3.5f, 0); box.size = new Vector3(4, 3, 10);
        truckRoot.transform.rotation = Quaternion.Euler(0, 28, 0);
        truckRoot.transform.localScale = new Vector3(1.2f, 1, .9f);
        var truck = truckRoot.AddComponent<FireTruckDrive>(); truck.body = truck.GetComponent<Rigidbody>(); truck.body.isKinematic = true;
        var thermal = truckRoot.AddComponent<ThermalDamage>(); Call(thermal, "Awake"); Call(thermal, "OnEnable"); Property(truck, "Thermal", thermal);
        // Edit-mode fixtures invoke lifecycle explicitly; the live game does this on AddComponent.
        var burnVisual = truckRoot.AddComponent<ThermalBurnVisual>(); Call(burnVisual, "Awake");
        var root = new GameObject("Player");
        var body = root.AddComponent<CharacterController>(); body.center = Vector3.up * .9f; body.height = 1.8f; body.radius = .3f;
        var coordinator = root.AddComponent<FireRescuePlayer>(); coordinator.drive = truck; coordinator.playerBody = body;
        Property(coordinator, "IsDriving", true); body.enabled = false;
        var player = root.AddComponent<CreatureCombatPlayer>(); Call(player, "Awake");
        var actor = Creature(new Vector3(12, 0, 0)); var combat = actor.GetComponent<LavaCreatureCombat>(); combat.target = player;
        Call(combat, "Initialize"); Call(combat, "RefreshTruckFootprint");
        for (int i = 0; i < 16; i++)
        {
            Vector3 from = Quaternion.Euler(0, i * 22.5f, 0) * Vector3.forward * 15f;
            actor.transform.position = from;
            Vector3 goal = (Vector3)Call(combat, "ApproachPoint");
            Check(!SpiritOutdoorNavigation.Inside(box, goal, .8f), "Truck approach remains outside the rotated chassis: " + i);
            Check(SpiritOutdoorNavigation.Blocked(from, -from, .6f, box), "Route cannot pass under chassis: " + i);
        }
        var route = new SpiritOutdoorNavigation();
        Vector3 begin = new Vector3(-12, 0, 0), end = new Vector3(12, 0, 0);
        Vector3 corner = route.Next(begin, end, .8f, box);
        Check(!SpiritOutdoorNavigation.Inside(box, corner, .8f) && !SpiritOutdoorNavigation.Blocked(begin, corner, .8f, box),
            "Visibility route goes around the truck");
        truckRoot.transform.position = corner;
        Vector3 reroute = route.Next(begin, end, .8f, box);
        Check(!SpiritOutdoorNavigation.Inside(box, reroute, .8f), "Moving truck invalidates the old route");
        truckRoot.transform.position = Vector3.zero;
        Physics.SyncTransforms();
        var encounter = new GameObject("Spawn probe").AddComponent<LavaCreatureEncounter>();
        Check(!(bool)Call(encounter, "HasEmergenceClearance", Vector3.zero), "A high chassis still excludes lava births beneath it");
        Check((bool)Call(encounter, "HasEmergenceClearance", new Vector3(-20, 0, 0)), "An open lava bank remains available");
        actor.transform.position = Vector3.zero;
        Call(combat, "EscapeTruckOverlap");
        Check(!SpiritOutdoorNavigation.Inside(box, actor.transform.position, .5f) && !actor.IsDefeated, "Covered spirit escapes the moving truck alive");

        // Exercise the actual swept pounce/contact path without entering or altering Play Mode.
        actor.transform.position = new Vector3(12, 0, 0);
        Vector3 aim = (Vector3)Call(combat, "PlayerPounceAim");
        Field(combat, "pounceStart", actor.transform.position + Vector3.up * .8f); Field(combat, "pounceEnd", aim);
        Field(combat, "pounceArcHeight", 1.5f); Field(combat, "duration", .6f); Field(combat, "elapsed", .6f);
        Field(combat, "attacking", true); Field(combat, "playerPounceLive", true);
        Physics.SyncTransforms(); Call(combat, "TickPlayerPounce", .6f);
        Check(!actor.IsDefeated && combat.State == LavaCreatureCombat.Motion.Settling && thermal.Ignitions == 1,
            "Truck contact retains the hunter and applies existing truck ignition");
        Check(!SpiritOutdoorNavigation.Inside(box, (Vector3)typeof(LavaCreatureCombat).GetField("landing", Private).GetValue(combat), .5f),
            "Vehicle rebound stays outside the chassis");
        Check(Mathf.Approximately(player.Health, 100f), "Vehicle pounce does not damage the seated player");
        Property(coordinator, "IsDriving", false); root.transform.position = new Vector3(15, 0, 0); body.enabled = true;
        Physics.SyncTransforms();
        Check(player.CanBeAttacked && !player.VehicleTarget && player.PursuitPosition == root.transform.position,
            "Exiting the truck restores the existing on-foot target and damage gate");
        Field(combat, "struck", false);
        Call(combat, "ResolvePounceContact", player.AttackPoint, Vector3.left, body);
        Check(actor.IsDefeated && actor.SelfDetonated && Mathf.Approximately(player.Health, 100f - combat.attackDamage),
            "On-foot contact preserves the existing player damage and spirit detonation");
    }

    static LavaCreatureActor Creature(Vector3 position)
    {
        var root = new GameObject("Tactics spirit"); root.transform.position = position;
        root.AddComponent<LavaCreatureAutoJump>();
        var actor = root.AddComponent<LavaCreatureActor>(); Call(actor, "Awake");
        root.AddComponent<LavaCreatureCombat>();
        return actor;
    }
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    static void Field(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static void Property(object owner, string name, object value) => owner.GetType().GetProperty(name).SetValue(owner, value);
    static void Check(bool passed, string name) { if (!passed) throw new Exception(name); report.passed.Add(name); }
}
