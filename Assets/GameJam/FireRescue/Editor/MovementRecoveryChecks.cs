using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Small isolated physics fixtures; never move actors or enter Play Mode in the user's scene.
public static class MovementRecoveryChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] class Report { public List<string> passed = new List<string>(); public string error; }
    static Report report;
    static readonly List<GameObject> roots = new List<GameObject>();

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated Unity batch project.");
        report = new Report();
        try { CheckRouteCorner(); Clear(); CheckWatchdog(); Clear(); CheckExits(); Clear(); CheckRighting(); }
        catch (Exception ex) { report.error = ex.ToString(); }
        finally { Clear(); }
        Directory.CreateDirectory("Logs/MovementRecovery");
        File.WriteAllText("Logs/MovementRecovery/checks.json", JsonUtility.ToJson(report, true));
        Debug.Log(JsonUtility.ToJson(report, true));
        EditorApplication.Exit(report.error == null ? 0 : 1);
    }

    static void CheckRouteCorner()
    {
        var houseRoot = Root("Route corner house", Vector3.zero);
        var box = houseRoot.AddComponent<BoxCollider>(); box.size = new Vector3(4, 6, 4); box.center = Vector3.up * 3;
        var house = houseRoot.AddComponent<BurnableBuilding>(); house.ignitionBounds = box; house.fitFireToBuilding = false;
        Call(house, "OnEnable");
        var combat = Spirit(new Vector3(2.4f, 0, -3.2f), new Vector3(2.9f, 0, 15));
        var navigation = (SpiritOutdoorNavigation)Get(combat, "navigation");
        var route = (List<Vector3>)Get(navigation, "route");
        route.Add(new Vector3(2.9f, 0, -3.2f)); route.Add(new Vector3(2.9f, 0, 3.2f));
        Field(navigation, "destination", (Vector3)Call(combat, "ApproachPoint")); Field(navigation, "refreshAt", float.MaxValue);
        Physics.SyncTransforms();
        combat.TickCombat(.1f);
        Check(combat.State == LavaCreatureCombat.Motion.Windup,
            "A corner 50 cm away starts a hop instead of waiting in the old 40-65 cm dead zone");
        for (int i = 0; i < 12; i++) combat.TickCombat(.1f);
        Check(combat.transform.position.z > -2.6f, "The spirit clears the corner and continues around the house");
    }

    static void CheckWatchdog()
    {
        var combat = Spirit(Vector3.zero, new Vector3(20, 0, 0));
        var navigation = (SpiritOutdoorNavigation)Get(combat, "navigation");
        ((List<Vector3>)Get(navigation, "route")).Add(Vector3.zero);
        Field(navigation, "destination", (Vector3)Call(combat, "ApproachPoint")); Field(navigation, "refreshAt", float.MaxValue);
        for (int i = 0; i < 45; i++) combat.TickCombat(.1f);
        Check(combat.RouteRecoveries > 0 && combat.transform.position.x > 2f,
            "Unreachable cached waypoint recovers despite the early return and resumes actual movement");

        var idle = Spirit(new Vector3(0, 0, 100), new Vector3(3.4f, 0, 100));
        Field(idle, "lane", 0); Field(idle, "cooldown", 100f);
        for (int i = 0; i < 60; i++) idle.TickCombat(.1f);
        Check(idle.RouteRecoveries == 0, "Waiting at the approach ring during cooldown is not treated as stuck");
        Field(idle, "routeMovementExpected", true); Field(idle, "progressClock", .9f);
        Vector3 before = idle.transform.position;
        for (int i = 0; i < 30; i++) idle.TickCombat(0f);
        Check(idle.transform.position == before && idle.RouteRecoveries == 0 && (float)Get(idle, "progressClock") == .9f,
            "Pause does not advance recovery or move a spirit");

        // A real prop still blocks every swept route; recovery must not teleport through it.
        var trapped = Spirit(new Vector3(100, 0, 0), new Vector3(120, 0, 0));
        var cage = Root("Solid obstruction", new Vector3(100, .8f, 0)).AddComponent<BoxCollider>();
        cage.size = new Vector3(1.5f, 3, 1.5f);
        Physics.SyncTransforms();
        before = trapped.transform.position;
        for (int i = 0; i < 40; i++) trapped.TickCombat(.1f);
        Check(trapped.RouteRecoveries > 0 && Vector3.Distance(before, trapped.transform.position) < .2f,
            "No clear detour retries safely instead of teleporting through a solid obstruction");
    }

    static void CheckExits()
    {
        Vector3 arena = new Vector3(1000, 0, 1000);
        var floor = Root("Exit floor", arena + Vector3.down * .5f).AddComponent<BoxCollider>(); floor.size = new Vector3(100, 1, 100);
        var drive = Truck(arena);
        drive.body.isKinematic = true;
        var playerRoot = Root("Exit player", arena + Vector3.right * 30);
        var capsule = playerRoot.AddComponent<CharacterController>(); capsule.center = Vector3.up * .9f; capsule.height = 1.8f; capsule.radius = .3f;
        capsule.enabled = false;
        var player = playerRoot.AddComponent<FireRescuePlayer>(); player.drive = drive; player.playerBody = capsule;
        var door = new GameObject("Driver door").transform; door.SetParent(drive.transform, false); door.localPosition = new Vector3(-2.1f, 2, 3);
        player.driverDoor = door;
        foreach (float scale in new[] { 1f, 3.3f })
        foreach (float yaw in new[] { 0f, 37f, 90f })
        foreach (float roll in new[] { 0f, 65f, 90f, 120f, 179f, 180f, -90f, -179f })
        {
            playerRoot.transform.localScale = Vector3.one * scale;
            PlaceTruck(drive, arena, Quaternion.Euler(0, yaw, roll));
            bool found = VehicleExitPlacement.TryFind(player, out var feet);
            float radius = capsule.radius * scale, height = capsule.height * scale;
            bool clear = found && !Physics.OverlapCapsule(feet + Vector3.up * radius,
                feet + Vector3.up * (height - radius), radius, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(playerRoot.transform));
            Check(found && clear && Mathf.Abs(feet.y - .12f) < .02f,
                $"Clear grounded exit: player scale {scale}, yaw {yaw}, roll {roll}");
        }

        playerRoot.transform.localScale = Vector3.one;
        foreach (Vector3 euler in new[] { new Vector3(70, 37, 0), new Vector3(90, 37, 0),
            new Vector3(180, 37, 0), new Vector3(35, 37, 120) })
        {
            PlaceTruck(drive, arena, Quaternion.Euler(euler));
            Check(VehicleExitPlacement.TryFind(player, out var feet) && Mathf.Abs(feet.y - .12f) < .02f,
                $"Grounded exit from combined pitch/roll {euler}");
        }
        PlaceTruck(drive, arena, Quaternion.Euler(0, 0, 180));
        var left = Root("Left exit wall", arena + new Vector3(-2.35f, 5, 0)).AddComponent<BoxCollider>(); left.size = new Vector3(.15f, 10, 40);
        var right = Root("Right exit wall", arena + new Vector3(2.35f, 5, 0)).AddComponent<BoxCollider>(); right.size = left.size;
        Physics.SyncTransforms();
        Check(!VehicleExitPlacement.TryFind(player, out _), "Overturned exit cannot cross either enclosing wall");
        left.gameObject.SetActive(false); right.gameObject.SetActive(false); floor.gameObject.SetActive(false); Physics.SyncTransforms();
        Check(!VehicleExitPlacement.TryFind(player, out _), "Overturned exit above a void remains blocked");
        floor.gameObject.SetActive(true);

        // A cab marker penetrating a nearby wall must not become a teleport into its empty interior.
        PlaceTruck(drive, arena, Quaternion.identity);
        left.gameObject.SetActive(true); Physics.SyncTransforms();
        Check(VehicleExitPlacement.TryFind(player, out var otherSide) && otherSide.x > arena.x,
            "Upright blocked driver door still exits on the unobstructed passenger side");
    }

    static void CheckRighting()
    {
        var previous = Physics.simulationMode; Physics.simulationMode = SimulationMode.Script;
        try
        {
            Vector3 arena = new Vector3(2000, 0, 2000);
            var floor = Root("Rollover floor", arena + Vector3.down * .5f).AddComponent<BoxCollider>(); floor.size = new Vector3(100, 1, 100);
            var drive = Truck(arena); drive.arcadeHandling = true; drive.SetDriven(true);
            for (int i = 0; i < 4; i++)
            {
                var wheel = new GameObject("Wheel " + i).AddComponent<WheelCollider>(); wheel.transform.SetParent(drive.transform, false);
                wheel.transform.localPosition = new Vector3(i % 2 == 0 ? -1.8f : 1.8f, .7f, i < 2 ? 3.5f : -3.5f);
                wheel.radius = .5f; wheel.suspensionDistance = .3f; drive.wheels[i] = wheel;
            }
            foreach (float roll in new[] { 90f, 180f, -90f })
            {
                drive.body.linearVelocity = drive.body.angularVelocity = Vector3.zero;
                Field(drive, "rolloverSupportSeconds", 0f);
                PlaceTruck(drive, arena, Quaternion.Euler(0, 23, roll));
                for (int i = 0; i < 500; i++) { Call(drive, "FixedUpdate"); Physics.Simulate(.02f); }
                Check(Vector3.Dot(drive.transform.up, Vector3.up) > .8f && drive.transform.position.y < 3f,
                    $"Physics upright recovery from {roll} degrees; up={drive.transform.up}, position={drive.transform.position}");
            }
            PlaceTruck(drive, arena + Vector3.up * 30, Quaternion.Euler(0, 0, 180));
            drive.body.linearVelocity = drive.body.angularVelocity = Vector3.zero;
            Field(drive, "rolloverSupportSeconds", 1f);
            Call(drive, "RecoverRollover"); Physics.Simulate(.02f);
            Check(drive.body.angularVelocity.sqrMagnitude < .001f, "No rollover torque while airborne far above support");
        }
        finally { Physics.simulationMode = previous; }
    }

    static FireTruckDrive Truck(Vector3 position)
    {
        var root = Root("Truck", position);
        var box = root.AddComponent<BoxCollider>(); box.center = Vector3.up * 2; box.size = new Vector3(4, 3, 10);
        var drive = root.AddComponent<FireTruckDrive>(); drive.body = root.GetComponent<Rigidbody>();
        Call(drive, "ConfigureBody"); return drive;
    }
    static void PlaceTruck(FireTruckDrive drive, Vector3 floor, Quaternion rotation)
    {
        drive.transform.SetPositionAndRotation(floor, rotation); Physics.SyncTransforms();
        drive.transform.position += Vector3.up * (floor.y + .03f - drive.GetComponent<BoxCollider>().bounds.min.y);
        drive.body.position = drive.transform.position; drive.body.rotation = rotation; Physics.SyncTransforms();
    }
    static LavaCreatureCombat Spirit(Vector3 from, Vector3 goal)
    {
        var playerRoot = Root("Target", goal); var controller = playerRoot.AddComponent<CharacterController>();
        controller.center = Vector3.up * .9f; controller.height = 1.8f; controller.radius = .3f;
        var player = playerRoot.AddComponent<CreatureCombatPlayer>(); Call(player, "Awake");
        var root = Root("Recovery spirit", from); root.AddComponent<LavaCreatureAutoJump>();
        var actor = root.AddComponent<LavaCreatureActor>(); Call(actor, "Awake");
        var combat = root.AddComponent<LavaCreatureCombat>(); combat.target = player; combat.attackRange = 1f;
        Call(combat, "Initialize"); root.transform.position = from;
        Property(combat, "State", LavaCreatureCombat.Motion.Searching); Field(combat, "alerted", true); Field(combat, "lane", 0);
        return combat;
    }
    static GameObject Root(string name, Vector3 position) { var root = new GameObject(name); root.transform.position = position; roots.Add(root); return root; }
    static void Clear() { foreach (var root in roots) if (root) { var house = root.GetComponent<BurnableBuilding>(); if (house) Call(house, "OnDisable"); Object.DestroyImmediate(root); } roots.Clear(); }
    static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
    static object Get(object owner, string field) => owner.GetType().GetField(field, Private).GetValue(owner);
    static void Field(object owner, string field, object value) => owner.GetType().GetField(field, Private).SetValue(owner, value);
    static void Property(object owner, string property, object value) => owner.GetType().GetProperty(property).SetValue(owner, value);
    static void Check(bool passed, string name) { if (!passed) throw new Exception(name); report.passed.Add(name); }
}
