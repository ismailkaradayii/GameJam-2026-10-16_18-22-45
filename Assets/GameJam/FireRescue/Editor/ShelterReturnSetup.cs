using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Narrow scene binding and review bridge for the closing shot.</summary>
[InitializeOnLoad]
public static class ShelterReturnSetup
{
    public const string Folder = "Library/ShelterReturnReview";
    static double next;
    static ShelterReturnSetup()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/ready.txt", DateTime.UtcNow.ToString("O"));
        EditorApplication.update += Poll;
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        string path = Folder + "/request.txt";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "inspect") Inspect();
            else if (command == "apply") Apply();
            else if (command == "run") EditorApplication.ExecuteMenuItem("GameJam/Ending/Run closing sequence checks");
            else if (command == "visual") EditorApplication.ExecuteMenuItem("GameJam/Ending/Review closing visuals");
            else if (command == "refine-view") RefineView();
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); Debug.LogException(ex); }
    }
    [MenuItem("GameJam/Ending/Bind shelter return")]
    public static void Apply()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open saved Volcano_Demo in Edit Mode.");
        if (scene.isDirty) throw new InvalidOperationException("Save or preserve your scene edits before applying the ending.");
        if (!File.Exists(Folder + "/Volcano_Demo.before-ending.unity")) File.Copy(scene.path, Folder + "/Volcano_Demo.before-ending.unity");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var ending = mission.gameFlow.GetComponent<ShelterReturnCinematic>();
        if (!ending) ending = Undo.AddComponent<ShelterReturnCinematic>(mission.gameFlow.gameObject);
        Undo.RecordObject(ending, "Bind successful shelter return");
        Undo.RecordObject(mission.gameFlow, "Delay success until the shelter return completes");
        ending.cinematic = mission.cinematic; ending.evacuation = mission.evacuation;
        mission.gameFlow.victoryCinematic = ending;
        Vector3 centre = mission.evacuation.residents.Aggregate(Vector3.zero, (sum, r) => sum + r.safePlace.position) / mission.evacuation.TotalCount;
        Vector3 foot = Vector3.Lerp(mission.evacuation.shelterEntrance.position, centre, .28f);
        if (!NavMesh.SamplePosition(foot, out var mesh, 3f, NavMesh.AllAreas)) throw new InvalidOperationException("Shelter camera has no walkable floor.");
        var root = GameObject.Find("Volcano Story Bindings").transform;
        ending.shelterViewpoint = Point(root, "Shelter return final viewpoint", mesh.position);
        ending.shelterFocus = Point(root, "Sheltered survivors closing focus", centre + Vector3.up * 1.3f);
        ending.shelterViewpoint.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(ending.shelterFocus.position - mesh.position, Vector3.up));
        EditorUtility.SetDirty(ending); EditorUtility.SetDirty(mission.gameFlow);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText(Folder + "/setup.txt", "APPLIED: existing success now returns continuously to the real shelter, with preparation/awareness inner thoughts; result is delayed until observing the survivors. Combat, evacuation, houses, terrain and opening bindings are retained.");
        Inspect();
    }
    static Transform Point(Transform root, string name, Vector3 position)
    {
        var t = root.Find(name);
        if (!t) { t = new GameObject(name).transform; Undo.RegisterCreatedObjectUndo(t.gameObject, "Closing camera target"); t.SetParent(root, false); }
        Undo.RecordObject(t, "Place closing camera target"); t.position = position; EditorUtility.SetDirty(t); return t;
    }
    [MenuItem("GameJam/Ending/Refine survivor view")]
    public static void RefineView()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var ending = Object.FindFirstObjectByType<RescueGameFlow>().victoryCinematic;
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, Folder + "/Volcano_Demo.before-view-refinement.unity", true);
        Vector3[] heads = ending.evacuation.residents.Select(r => r.safePlace.position + Vector3.up * 1.15f).ToArray();
        Vector3 focus = heads.Aggregate(Vector3.zero, (a, b) => a + b) / heads.Length;
        var cameraObject = new GameObject("Temporary closing viewpoint review") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false; camera.fieldOfView = ending.fieldOfView; camera.aspect = 16f / 9f;
        Vector3 best = ending.shelterViewpoint.position; int bestCount = -1; float bestScore = float.NegativeInfinity;
        var log = new StringBuilder();
        try
        {
            for (float x = -116; x >= -122; x -= 1f)
                for (float z = -563; z >= -580; z -= 1f)
                {
                    if (!NavMesh.SamplePosition(new Vector3(x, 1.4f, z), out var mesh, 1.2f, NavMesh.AllAreas) ||
                        !ending.evacuation.IsInsideShelter(mesh.position) || heads.Any(p => EvacuationResident.FlatDistance(p, mesh.position) < 2)) continue;
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(ending.evacuation.shelterApproach.position, mesh.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) continue;
                    camera.transform.position = mesh.position + Vector3.up * ending.eyeHeight;
                    if (Physics.CheckSphere(camera.transform.position, .22f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    camera.transform.LookAt(focus);
                    int count = heads.Count(head =>
                    {
                        Vector3 screen = camera.WorldToViewportPoint(head); Vector3 delta = head - camera.transform.position;
                        return screen.z > 0 && screen.x > .08f && screen.x < .92f && screen.y > .15f && screen.y < .85f &&
                            !Physics.Raycast(camera.transform.position, delta.normalized, delta.magnitude - .1f, ~0, QueryTriggerInteraction.Ignore);
                    });
                    float score = count * 100 - Mathf.Abs(mesh.position.z - focus.z) - Mathf.Abs(Vector3.Distance(camera.transform.position, focus) - 8f);
                    log.AppendLine("candidate=" + mesh.position + " visible=" + count + " score=" + score);
                    if (score <= bestScore) continue;
                    bestScore = score; bestCount = count; best = mesh.position;
                }
        }
        finally { Object.DestroyImmediate(cameraObject); }
        if (bestCount < 5) throw new InvalidOperationException("No clear survivor framing found. Best=" + bestCount);
        Undo.RecordObject(ending.shelterViewpoint, "Frame the real sheltered survivors");
        ending.shelterViewpoint.SetPositionAndRotation(best, Quaternion.LookRotation(Vector3.ProjectOnPlane(focus - best, Vector3.up)));
        EditorUtility.SetDirty(ending.shelterViewpoint);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        log.AppendLine("SELECTED " + best + " visible=" + bestCount);
        File.WriteAllText(Folder + "/viewpoint-review.txt", log.ToString()); Inspect();
    }
    public static void Inspect()
    {
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        var ending = mission.gameFlow.victoryCinematic;
        var log = new StringBuilder();
        log.AppendLine("scene=" + SceneManager.GetActiveScene().path + " dirty=" + SceneManager.GetActiveScene().isDirty + " playing=" + EditorApplication.isPlaying);
        log.AppendLine("flow=" + mission.gameFlow.State + " timeScale=" + Time.timeScale + " cinematic=" + mission.cinematic.Kind + " camera=" + mission.cinematic.IsPlaying);
        log.AppendLine("entrance=" + mission.evacuation.shelterEntrance.position + " approach=" + mission.evacuation.shelterApproach.position + " safe area=" + mission.evacuation.shelterSafeArea.bounds);
        log.AppendLine("player=" + mission.player.transform.position + " view=" + mission.player.viewCamera.transform.position);
        if (ending) log.AppendLine("ENDING playing=" + ending.IsPlaying + " elapsed=" + ending.Elapsed + " distance=" + ending.DistanceTravelled + "/" + ending.RouteLength + " arrived=" + ending.AtShelter + " visible=" + ending.VisibleSurvivors + " observed=" + ending.ObservedSeconds + " line=" + ending.CurrentLine + " viewpoint=" + ending.shelterViewpoint.position);
        foreach (var r in mission.evacuation.residents) log.AppendLine(r.name + " state=" + r.State + " pos=" + r.transform.position + " slot=" + r.safePlace.position);
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name.Contains("Shelter") || t.name.Contains("shelter")))
        {
            var renderer = t.GetComponent<Renderer>();
            log.AppendLine("SHELTER " + t.name + " pos=" + t.position + (renderer ? " bounds=" + renderer.bounds : ""));
        }
        File.WriteAllText(Folder + "/inspect.txt", log.ToString());
    }
}
