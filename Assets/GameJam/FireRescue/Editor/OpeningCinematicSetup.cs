using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Author a clear bedroom route using the real enlarged player capsule.</summary>
public static class OpeningCinematicSetup
{
    public const string Folder = "Logs/OpeningCinematic";
    static readonly Collider[] overlaps = new Collider[128];
    static FireRescuePlayer player;
    static float radius, height;

    static VolcanoStorySequence Bind()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit Mode.");
        var mission = Object.FindFirstObjectByType<VolcanoMissionDirector>();
        if (!mission || !mission.story) throw new InvalidOperationException("Existing volcano story not found.");
        player = mission.player;
        var body = player.playerBody;
        radius = body.radius * Mathf.Max(body.transform.lossyScale.x, body.transform.lossyScale.z) + .06f;
        height = body.height * body.transform.lossyScale.y;
        Physics.SyncTransforms(); Directory.CreateDirectory(Folder);
        return mission.story;
    }
    [MenuItem("GameJam/Story/Audit opening route")]
    public static void Audit()
    {
        var story = Bind();
        var log = new StringBuilder();
        log.AppendLine("Real capsule: radius=" + radius + " height=" + height);
        Vector3 start = story.mission.standingSpawn.position;
        foreach (var waypoint in story.windowRoute.Concat(new[] { story.windowStop }))
        {
            if (!waypoint) continue;
            log.AppendLine("CURRENT " + start.ToString("F3") + " -> " + waypoint.position.ToString("F3") + " clear=" + SegmentClear(start, waypoint.position));
            start = waypoint.position;
        }
        var route = Plan(story.mission.standingSpawn.position, story.windowStop.position);
        foreach (var point in route) log.AppendLine("SAFE " + point.ToString("F3"));
        File.WriteAllText(Folder + "/route-audit.txt", log.ToString());
    }
    [MenuItem("GameJam/Story/Apply clear opening route")]
    public static void Apply()
    {
        var story = Bind();
        var route = Plan(story.mission.standingSpawn.position, story.windowStop.position);
        Undo.RecordObject(story, "Clear bedroom cinematic route");
        var parent = story.windowStop.parent;
        var waypoints = new List<Transform>();
        for (int i = 1; i < route.Count - 1; i++)
        {
            string name = "Clear bedroom cinematic waypoint " + i;
            var point = parent.Find(name);
            if (!point) { point = new GameObject(name).transform; point.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(point.gameObject, "Clear cinematic waypoint"); }
            Undo.RecordObject(point, "Clear cinematic route"); point.position = route[i]; waypoints.Add(point);
        }
        story.windowRoute = waypoints.ToArray();
        story.approachSpeed = 2.1f; story.approachTimeout = 16f; story.arrivalRadius = .24f;
        story.windowFieldOfView = 46f;
        // The existing lean reaches the lower pane. Its timing is now isolated from walking.
        story.windowEyeOffset = new Vector3(-1.8f, -2.2f, 0);
        if (story.omenSmoke)
        {
            Undo.RecordObject(story.omenSmoke, "Visible first crater smoke");
            var main = story.omenSmoke.main;
            main.startSize = new ParticleSystem.MinMaxCurve(24f, 33f);
            main.startColor = new Color(.28f, .3f, .31f, .88f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 8f);
            var emission = story.omenSmoke.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)9) });
            EditorUtility.SetDirty(story.omenSmoke);
        }
        if (story.omenSparks)
        {
            Undo.RecordObject(story.omenSparks, "Readable first crater sparks");
            var main = story.omenSparks.main; main.startSize = new ParticleSystem.MinMaxCurve(5.5f, 8f);
            EditorUtility.SetDirty(story.omenSparks);
        }
        EditorUtility.SetDirty(story);
        EditorSceneManager.MarkSceneDirty(story.gameObject.scene);
        EditorSceneManager.SaveScene(story.gameObject.scene);
        File.WriteAllLines(Folder + "/applied-route.txt", route.Select(p => p.ToString("F3")));
        Previews();
    }
    static void Capsule(Vector3 feet, out Vector3 lower, out Vector3 upper)
    {
        lower = feet + Vector3.up * (radius + .13f);
        upper = feet + Vector3.up * (height - radius - .08f);
    }
    static bool Relevant(Collider collider, Vector3 feet) => collider && !collider.transform.IsChildOf(player.transform) &&
        !(collider is TerrainCollider) && collider.bounds.max.y > feet.y + .3f;
    static bool Free(Vector3 feet)
    {
        Capsule(feet, out var a, out var b);
        int count = Physics.OverlapCapsuleNonAlloc(a, b, radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (Relevant(overlaps[i], feet)) return false;
        return count < overlaps.Length;
    }
    public static bool SegmentClear(Vector3 from, Vector3 to)
    {
        Vector3 travel = Vector3.ProjectOnPlane(to - from, Vector3.up);
        if (travel.sqrMagnitude < .0001f) return Free(from);
        Capsule(from, out var a, out var b);
        foreach (var hit in Physics.CapsuleCastAll(a, b, radius, travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (Relevant(hit.collider, from) && hit.normal.y < .7f) return false;
        return Free(to);
    }
    static List<Vector3> Plan(Vector3 start, Vector3 end)
    {
        const float step = .3f;
        Vector3 min = Vector3.Min(start, end) - new Vector3(4, 0, 4);
        Vector3 max = Vector3.Max(start, end) + new Vector3(4, 0, 4);
        int nx = Mathf.CeilToInt((max.x - min.x) / step) + 1, nz = Mathf.CeilToInt((max.z - min.z) / step) + 1;
        Vector3 Position(int i) => new Vector3(min.x + i % nx * step, start.y, min.z + i / nx * step);
        int Index(Vector3 p) => Mathf.RoundToInt((p.x - min.x) / step) + nx * Mathf.RoundToInt((p.z - min.z) / step);
        int first = Index(start), last = Index(end), total = nx * nz;
        int[] previous = Enumerable.Repeat(-1, total).ToArray(), free = new int[total];
        float[] score = Enumerable.Repeat(float.PositiveInfinity, total).ToArray();
        var open = new List<int> { first }; var closed = new HashSet<int>(); score[first] = 0;
        bool IsFree(int i) { if (free[i] == 0) free[i] = Free(Position(i)) ? 1 : -1; return free[i] > 0; }
        while (open.Count > 0)
        {
            int current = open.OrderBy(i => score[i] + Vector3.Distance(Position(i), Position(last))).First(); open.Remove(current);
            if (current == last) break;
            if (!closed.Add(current)) continue;
            int x = current % nx, z = current / nx;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0 || x + dx < 0 || x + dx >= nx || z + dz < 0 || z + dz >= nz) continue;
                int next = current + dx + dz * nx;
                if (closed.Contains(next) || !IsFree(next)) continue;
                if (dx != 0 && dz != 0 && (!IsFree(current + dx) || !IsFree(current + dz * nx))) continue;
                float cost = score[current] + step * (dx != 0 && dz != 0 ? 1.414214f : 1f);
                if (cost >= score[next]) continue;
                score[next] = cost; previous[next] = current;
                if (!open.Contains(next)) open.Add(next);
            }
        }
        if (previous[last] < 0) throw new InvalidOperationException("No clear bedroom path for the actual player capsule.");
        var raw = new List<Vector3> { end };
        for (int i = last; i != first; i = previous[i]) raw.Add(Position(i));
        raw.Add(start); raw.Reverse();
        var result = new List<Vector3> { start };
        for (int i = 0; i < raw.Count - 1;)
        {
            int next = i + 1;
            for (int j = raw.Count - 1; j > i + 1; j--) if (SegmentClear(raw[i], raw[j])) { next = j; break; }
            if (!SegmentClear(raw[i], raw[next])) throw new InvalidOperationException("Capsule sweep rejected an opening path segment.");
            result.Add(raw[next]); i = next;
        }
        return result;
    }
    [MenuItem("GameJam/Story/Preview closer window framing")]
    public static void Previews()
    {
        var story = Bind(); var view = player.viewCamera;
        var go = new GameObject("Opening reference camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = go.AddComponent<Camera>(); camera.CopyFrom(view); camera.enabled = false;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        camera.GetUniversalAdditionalCameraData().volumeLayerMask = view.GetUniversalAdditionalCameraData().volumeLayerMask;
        var localEye = player.transform.InverseTransformPoint(view.transform.position);
        Vector3 eye = story.windowStop.position + story.windowStop.rotation * Vector3.Scale(localEye, player.transform.lossyScale) + story.windowEyeOffset;
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(story.volcanoLookTarget.position - eye));
        var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); var active = RenderTexture.active;
        try
        {
            camera.targetTexture = target; camera.aspect = 16f / 9;
            foreach (float fov in new[] { 50f, 46f, 42f })
            {
                camera.fieldOfView = fov; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/window-" + fov + ".png", pixels.EncodeToPNG());
            }
            foreach (float lateral in new[] { -1.8f, -1.5f, -1.2f })
            {
                Vector3 offsetEye = eye + Vector3.right * (lateral - story.windowEyeOffset.x);
                camera.transform.SetPositionAndRotation(offsetEye, Quaternion.LookRotation(story.volcanoLookTarget.position - offsetEye));
                camera.fieldOfView = 46f; camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
                File.WriteAllBytes(Folder + "/window-offset-" + lateral.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png", pixels.EncodeToPNG());
            }
        }
        finally { RenderTexture.active = active; target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(pixels); Object.DestroyImmediate(go); }
    }
}
