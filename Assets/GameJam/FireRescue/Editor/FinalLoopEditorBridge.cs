using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Explicit local editor commands for authoring and reproducible Play Mode checks.
[InitializeOnLoad]
public static class FinalLoopEditorBridge
{
    static double nextPoll;
    static FinalLoopEditorBridge() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 1;
        const string request = "Logs/FinalLoop.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim();
        File.Delete(request);
        try
        {
            if (command == "audit") Audit();
            else if (command == "Apply") FinalLoopSetup.Apply();
            else if (command == "Validate") FinalLoopSetup.Validate();
            else if (command == "stop") EditorApplication.ExitPlaymode();
            else if (!EditorApplication.ExecuteMenuItem("GameJam/Final Loop/" + command))
                throw new InvalidOperationException("Unknown final-loop command: " + command);
            File.WriteAllText("Logs/FinalLoop-command-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/FinalLoop-command-result.txt", ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("GameJam/Final Loop/Audit")]
    public static void Audit()
    {
        var report = new StringBuilder();
        var scene = SceneManager.GetActiveScene();
        report.AppendLine($"SCENE {scene.path} dirty={scene.isDirty} play={EditorApplication.isPlaying}");
        var p = Object.FindFirstObjectByType<FireRescuePlayer>();
        var e = Object.FindFirstObjectByType<EvacuationMission>();
        if (p) report.AppendLine($"BINDINGS player={p.name} fp={p.firstPerson} camera={p.viewCamera} pistol={p.pistol} drive={p.drive} hose={p.hose} flow={p.gameFlow}");
        if (p && p.firstPerson) report.AppendLine("MOVEMENT " + EditorJsonUtility.ToJson(p.firstPerson));
        if (e) foreach (var r in e.residents) if (r) report.AppendLine($"RESIDENT {r.name} mission={r.mission} door={r.door} exit={r.houseExit} safe={r.safePlace} agent={r.GetComponent<UnityEngine.AI.NavMeshAgent>().radius}");
        if (p && p.drive) foreach (var c in p.drive.GetComponentsInChildren<Collider>()) report.AppendLine($"TRUCK {Path(c.transform)} bounds={c.bounds}");
        foreach (var d in Object.FindObjectsByType<InteractiveDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            report.AppendLine($"DOOR {Path(d.transform)} pos={d.transform.position} {EditorJsonUtility.ToJson(d)}");
        foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!component || !component.gameObject.scene.IsValid()) continue;
            if (component is FireRescuePlayer || component is FireTruckDrive || component is EvacuationMission || component is EvacuationResident ||
                component is VolcanoMissionDirector || component is VolcanoEruptionSequence || component is LavaCreatureEncounter || component is RescueHUD ||
                component is BurnableBuilding || component is WaterSourceVolume || component is RescueGameFlow)
                report.AppendLine(component.GetType().Name + " " + Path(component.transform) + " position=" + component.transform.position + " scale=" + component.transform.lossyScale + "\n" + EditorJsonUtility.ToJson(component));
        }
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!t.gameObject.scene.IsValid()) continue;
            if (!new[] {"bed", "cabinet", "table", "exit", "refill", "shelter", "bedroom", "water", "pistol", "canvas"}.Any(n => t.name.ToLowerInvariant().Contains(n))) continue;
            var r = t.GetComponent<Renderer>();
            report.AppendLine("OBJECT " + Path(t) + " pos=" + t.position.ToString("F3") + " local=" + t.localPosition.ToString("F3") + " euler=" + t.eulerAngles + " scale=" + t.lossyScale + (r ? " bounds=" + r.bounds : ""));
        }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/FinalLoop-audit.txt", report.ToString());
    }
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
}
