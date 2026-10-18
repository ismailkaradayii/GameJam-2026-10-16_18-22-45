using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class VehicleExitChecks
{
    public const string Folder = "Logs/DoorSideVehicleExit";
    const string Pending = "DoorSideVehicleExit.Pending";
    static double nextPoll;
    [Serializable] public class Check { public string name, evidence; public bool passed; }
    [Serializable] public class Report
    {
        public string status;
        public List<Check> checks = new List<Check>();
        public List<string> errors = new List<string>();
    }
    static Report report;
    static FireRescuePlayer player;
    static readonly Vector3 Arena = new Vector3(19000, 500, 19000);
    static readonly List<GameObject> obstacles = new List<GameObject>();

    static VehicleExitChecks()
    {
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false))
            { SessionState.SetBool(Pending, false); EditorApplication.delayCall += Run; }
        };
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string request = "Logs/VehicleExit.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        if (command == "checks") Begin();
        else if (command == "refresh") AssetDatabase.Refresh();
    }
    [MenuItem("GameJam/Final Loop/Check safe door-side vehicle exits")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/checks.json", "{\"status\":\"STARTING\"}");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        report = new Report { status = "RUNNING" }; obstacles.Clear(); Application.logMessageReceived += Log;
        try
        {
            player = Object.FindFirstObjectByType<FireRescuePlayer>();
            var mission = player.mission;
            mission.enabled = false; mission.story.CancelOpening(); mission.cinematic.EndShot();
            mission.story.enabled = mission.sequence.enabled = mission.encounter.enabled = false;
            player.SetCinematicLocked(false); player.SetGameplaySuspended(false); player.gameFlow = null;
            player.enabled = false; player.firstPerson.enabled = false;
            player.drive.enabled = false; player.drive.body.useGravity = false;
            player.drive.body.interpolation = RigidbodyInterpolation.None;
            var floor = Box("Exit check floor", new Vector3(0, -.5f, 0), new Vector3(100, 1, 100));
            obstacles.Remove(floor);
            foreach (float scale in new[] { 1f, 3.3f })
            foreach (float yaw in new[] { 0f, 37f, 90f, 180f, 270f })
            {
                ResetTruck(scale, yaw);
                CheckExit("Open cab at scale " + scale + ", yaw " + yaw, -1);
            }

            ResetTruck(3.3f, 0);
            Box("Driver side wall", new Vector3(-4.5f, 4, 0), new Vector3(.12f, 8, 35));
            CheckExit("Wall immediately outside the driver door uses the opposite door", 1);

            ResetTruck(3.3f, 37);
            Box("Angled house wall", new Vector3(-4.5f, 4, 0), new Vector3(.12f, 8, 35));
            CheckExit("Rotated wall cannot be crossed during exit", 1);

            ResetTruck(3.3f, 0);
            var footprint = new GameObject("Hollow house containing the old exit point"); footprint.SetActive(false);
            footprint.transform.position = Arena + new Vector3(-8.5f, 4, 0);
            var trigger = footprint.AddComponent<BoxCollider>(); trigger.isTrigger = true; trigger.size = new Vector3(8, 8, 35);
            var house = footprint.AddComponent<BurnableBuilding>(); house.fitFireToBuilding = false; house.ignitionBounds = trigger;
            footprint.SetActive(true); obstacles.Add(footprint);
            Box("Closed house front wall", new Vector3(-4.5f, 4, 0), new Vector3(.12f, 8, 35));
            Physics.SyncTransforms();
            Vector3 oldExit = player.exitPoint.position; oldExit.y = Arena.y + .12f;
            float radius = player.playerBody.radius * 3.3f, height = player.playerBody.height * 3.3f;
            bool oldWasEmpty = !Physics.OverlapCapsule(oldExit + Vector3.up * radius, oldExit + Vector3.up * (height - radius),
                radius, ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(player.drive.transform) && !c.transform.IsChildOf(player.transform));
            Add("Reproduces the old empty-interior teleport trap", oldWasEmpty && trigger.bounds.Contains(oldExit + Vector3.up), "old exit=" + oldExit);
            CheckExit("Closed house interior is rejected even when its floor and capsule are clear", 1);

            ResetTruck(3.3f, 0);
            Box("Blocked driver side", new Vector3(-4.5f, 4, 0), new Vector3(.12f, 8, 35));
            Box("Blocked passenger side", new Vector3(4.5f, 4, 0), new Vector3(.12f, 8, 35));
            Physics.SyncTransforms(); Vector3 before = player.transform.position;
            Add("Two blocked doors keep the player safely seated", !player.ExitVehicle() && player.IsDriving && !player.playerBody.enabled &&
                Vector3.Distance(before, player.transform.position) < .001f, player.ActiveNotice);

            ResetTruck(3.3f, 0); floor.SetActive(false); Physics.SyncTransforms();
            Add("A door above empty space cannot drop the player into the void", !player.ExitVehicle() && player.IsDriving, player.ActiveNotice);
            floor.SetActive(true);

            ResetTruck(3.3f, 0); player.drive.body.linearVelocity = Vector3.forward * (30f / 3.6f);
            Add("The speed limit still prevents exiting at 30 km/h", !player.ExitVehicle() && player.IsDriving, player.ActiveNotice);
            ResetTruck(3.3f, 0); player.drive.body.linearVelocity = Vector3.forward * (19.9f / 3.6f);
            CheckExit("Exiting below 20 km/h remains available", -1);
            report.status = report.errors.Count == 0 && report.checks.All(c => c.passed) ? "SUCCESS" : "FAILED";
        }
        catch (Exception ex) { report.status = "FAILED"; report.errors.Add(ex.ToString()); }
        finally
        {
            Application.logMessageReceived -= Log;
            File.WriteAllText(Folder + "/checks.json", JsonUtility.ToJson(report, true));
            EditorApplication.ExitPlaymode();
        }
    }
    static void ResetTruck(float scale, float yaw)
    {
        foreach (var obstacle in obstacles) if (obstacle) Object.DestroyImmediate(obstacle);
        obstacles.Clear();
        player.transform.localScale = Vector3.one * scale;
        var body = player.drive.body; body.linearVelocity = body.angularVelocity = Vector3.zero;
        body.position = Arena + Vector3.up * .7f; body.rotation = Quaternion.Euler(0, yaw, 0);
        player.drive.transform.SetPositionAndRotation(body.position, body.rotation);
        player.SetDriving(true); Physics.SyncTransforms();
    }
    static GameObject Box(string name, Vector3 localPosition, Vector3 size)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube); box.name = name;
        Quaternion rotation = player ? Quaternion.Euler(0, player.drive.transform.eulerAngles.y, 0) : Quaternion.identity;
        box.transform.SetPositionAndRotation(Arena + rotation * localPosition, rotation); box.transform.localScale = size;
        obstacles.Add(box); return box;
    }
    static void CheckExit(string label, int expectedSide)
    {
        bool exited = player.ExitVehicle();
        Vector3 local = player.drive.transform.InverseTransformPoint(player.transform.position);
        Vector3 door = player.drive.transform.InverseTransformPoint(player.driverDoor.position);
        float radius = player.playerBody.radius * Mathf.Abs(player.transform.lossyScale.x);
        float gap = new Vector2(Mathf.Abs(local.x) - Mathf.Abs(door.x), local.z - door.z).magnitude;
        bool clear = !Physics.OverlapCapsule(player.transform.position + Vector3.up * radius,
            player.transform.position + Vector3.up * (player.playerBody.height * player.transform.lossyScale.y - radius), radius,
            ~0, QueryTriggerInteraction.Ignore).Any(c => !c.transform.IsChildOf(player.transform));
        Vector3 view = player.viewCamera.transform.position - player.transform.position;
        Add(label, exited && !player.IsDriving && player.playerBody.enabled && Mathf.Sign(local.x) == expectedSide && gap <= radius + 1.5f && clear,
            "exited=" + exited + ", local=" + local + ", door gap=" + gap + ", clear=" + clear + ", notice=" + player.ActiveNotice);
        Add(label + " places the camera at the character immediately", exited && new Vector2(view.x, view.z).magnitude < radius + .05f && view.y > 1f,
            "camera offset=" + view);
    }
    static void Add(string name, bool passed, string evidence) => report.checks.Add(new Check { name = name, passed = passed, evidence = evidence });
    static void Log(string text, string stack, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) report.errors.Add(text); }
}
