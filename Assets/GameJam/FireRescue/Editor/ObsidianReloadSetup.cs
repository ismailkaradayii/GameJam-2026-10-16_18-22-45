using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using Itfaiye;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Object = UnityEngine.Object;

public static class ObsidianReloadSetup
{
    public const string Folder = "Logs/ObsidianReload";
    public const string CrustPath = "Assets/GameJam/FireRescue/Materials/Water Cooled Obsidian.mat";
    const string Pack = "Assets/BK/PureNature_Volcano/Textures/";
    const string PrefabPath = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";

    [MenuItem("GameJam/Final Loop/Inspect cooling routes")]
    public static void InspectRoutes()
    {
        var report = new StringBuilder();
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        foreach (var route in sequence.routes.Where(r => r.flow && r.flow.name == "Town Branch - House_Green_Prefab (1)"))
        {
            report.AppendLine($"FEED {route.feed} at={route.feedProgress}");
            foreach (var flow in new[] { route.feed, route.flow })
            {
                report.AppendLine($"FLOW {flow.name} reverse={flow.reverse}");
                var mesh = flow.source.m_Mesh;
                if (mesh && mesh.vertexCount > 2)
                {
                    var v = mesh.vertices; var t = mesh.triangles; var tr = flow.source.transform;
                    Vector3 a = tr.TransformPoint(v[t[0]]), b = tr.TransformPoint(v[t[1]]), c = tr.TransformPoint(v[t[2]]);
                    report.AppendLine("FIRST NORMAL " + Vector3.Cross(b - a, c - a).normalized);
                }
                var container = flow.source.m_Container;
                foreach (var knot in container.Spline) report.AppendLine("KNOT " + container.transform.TransformPoint((Vector3)knot.Position));
                if (flow.name.Contains("Branch"))
                {
                    Physics.SyncTransforms();
                    foreach (var knot in container.Spline)
                    {
                        Vector3 point = container.transform.TransformPoint((Vector3)knot.Position);
                        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 25, Vector3.down, 30, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                            report.AppendLine($"SURFACE at={point} hit={hit.collider.name} point={hit.point} house={hit.collider.GetComponentInParent<BurnableBuilding>()}");
                    }
                }
            }
        }
        foreach (var house in Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None)) report.AppendLine($"HOUSE {house.name} bounds={house.ignitionBounds.bounds}");
        Directory.CreateDirectory(Folder); File.WriteAllText(Folder + "/routes.txt", report.ToString());
    }

    [MenuItem("GameJam/Final Loop/Apply obsidian and auto reload")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.path.EndsWith("Volcano_Demo.unity")) throw new InvalidOperationException("Open Volcano_Demo first.");
        RepairBuriedEntry();
        var template = AssetDatabase.LoadAssetAtPath<Material>(Pack + "Surfaces/Materials/LavaDry.mat");
        if (!template) throw new InvalidOperationException("Pack LavaDry material missing.");
        var crust = AssetDatabase.LoadAssetAtPath<Material>(CrustPath);
        if (!crust) { crust = new Material(template); AssetDatabase.CreateAsset(crust, CrustPath); }
        else EditorUtility.CopySerialized(template, crust);
        crust.name = "Water Cooled Obsidian";
        crust.SetColor("_BaseColor", Color.white);
        crust.SetColor("_EmissionColor", Color.black);
        crust.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Pack + "Lava/LavaCracks_e.png"));
        crust.EnableKeyword("_EMISSION");
        // The pack's surface material shares a mud mask; glass gets its own dielectric finish.
        crust.SetTexture("_MetallicGlossMap", null); crust.DisableKeyword("_METALLICSPECGLOSSMAP");
        crust.SetTexture("_OcclusionMap", null); crust.DisableKeyword("_OCCLUSIONMAP");
        crust.SetFloat("_Metallic", 0); crust.SetFloat("_Smoothness", .52f); crust.SetFloat("_BumpScale", 1.3f);
        EditorUtility.SetDirty(crust);
        var lava = AssetDatabase.LoadAssetAtPath<Material>("Assets/GameJam/FireRescue/Materials/Town Advancing Lava.mat");
        if (lava)
        {
            // Match Spline (1)'s Lava_stream colour and motion. Depth fades suit the
            // shallow village mesh; displacement must stay below its obsidian crust.
            lava.SetColor("_EmissiveColor", new Color(89.454094f, 6.4146366f, 0f));
            lava.SetFloat("_Smoothness", .5f);
            lava.SetFloat("_Speed", -.3f);
            lava.SetFloat("_DepthDistance", .2f);
            lava.SetFloat("_EdgeDistance1", .08f);
            lava.SetFloat("_Dispacement", .035f);
            EditorUtility.SetDirty(lava);
        }
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try { Configure(prefab.GetComponent<ItfaiyePistol>()); PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        foreach (var pistol in Object.FindObjectsByType<ItfaiyePistol>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Configure(pistol);
        var surfaces = Object.FindObjectsByType<LavaCoolingSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var cooling in surfaces)
        {
            Undo.RecordObject(cooling, "Water forms obsidian");
            cooling.crustMaterial = crust; cooling.litresPerPatch = 90; cooling.textureMetres = 8;
            cooling.crustThickness = .16f; cooling.meshRefreshInterval = .12f;
            cooling.coldTint = new Color(.38f, .43f, .48f, 1);
            var flow = cooling.GetComponent<ProgressiveLavaFlow>();
            if (flow && flow.canBeStoppedByWater && flow.conformToTerrain)
            {
                flow.groundClearance = .24f; cooling.crustThickness = .065f; EditorUtility.SetDirty(flow);
                flow.source.Rebuild(); EditorUtility.SetDirty(flow.source);
                if (flow.source.m_Mesh) EditorUtility.SetDirty(flow.source.m_Mesh);
            }
            EditorUtility.SetDirty(cooling);
            if (PrefabUtility.IsPartOfPrefabInstance(cooling)) PrefabUtility.RecordPrefabInstancePropertyModifications(cooling);
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder("APPLIED\n");
        report.AppendLine($"Scene={scene.path}; cooling surfaces={surfaces.Length}; water per patch=90 L");
        foreach (var flow in Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsSortMode.None).Where(f => f.canBeStoppedByWater))
            report.AppendLine($"BLOCKABLE {flow.name}; speed={flow.speed}; width={flow.source.m_Width}");
        File.WriteAllText(Folder + "/setup.txt", report.ToString());
    }
    static void RepairBuriedEntry()
    {
        var sequence = Object.FindFirstObjectByType<VolcanoEruptionSequence>();
        var route = sequence.routes.FirstOrDefault(r => r.flow && r.flow.name == "Town Branch - House_Green_Prefab (1)");
        if (route == null || !route.feed || !route.flow.source) return;
        var container = route.flow.source.m_Container;
        Vector3 oldStart = container.transform.TransformPoint((Vector3)container.Spline[0].Position);
        if (oldStart.z > -595f && container.Spline.Count > 1 &&
            container.transform.TransformPoint((Vector3)container.Spline[1].Position).x < 70f) return;
        // This house's whole old 12 m branch lay beneath its rooms. Rejoin the same
        // existing feeder in the courtyard, leaving time and space to intercept it.
        var feed = route.feed.source.m_Container;
        float best = float.MaxValue, bestArc = 0, arc = 0;
        Vector3 start = oldStart, previous = feed.EvaluatePosition(0);
        for (int i = 0; i <= 512; i++)
        {
            Vector3 p = feed.EvaluatePosition(i / 512f);
            arc += Vector3.Distance(previous, p); previous = p;
            float distance = (p - new Vector3(76, .22f, -580)).sqrMagnitude;
            if (distance < best) { best = distance; start = p; bestArc = arc; }
        }
        Vector3 end = container.transform.TransformPoint((Vector3)container.Spline[container.Spline.Count - 1].Position);
        Undo.RecordObject(container, "Expose lava entry to water"); Undo.RecordObject(sequence, "Keep lava feeder connected");
        container.Spline.Clear();
        foreach (Vector3 point in new[] { start, new Vector3(64, .22f, -582), new Vector3(62, .22f, -592), new Vector3(63, .22f, -603), new Vector3(65, .22f, -613), end })
            container.Spline.Add(new BezierKnot((float3)container.transform.InverseTransformPoint(point)), TangentMode.Linear);
        route.feedProgress = bestArc / Mathf.Max(.01f, arc);
        route.flow.source.Rebuild();
        EditorUtility.SetDirty(container); EditorUtility.SetDirty(sequence); EditorUtility.SetDirty(route.flow.source);
        if (route.flow.source.m_Mesh) EditorUtility.SetDirty(route.flow.source.m_Mesh);
    }
    static void Configure(ItfaiyePistol pistol)
    {
        if (!pistol) throw new InvalidOperationException("Pistol missing.");
        Undo.RecordObject(pistol, "Automatic empty magazine reload");
        pistol.autoReloadWhenEmpty = true; pistol.autoReloadDelay = .22f;
        EditorUtility.SetDirty(pistol);
        if (PrefabUtility.IsPartOfPrefabInstance(pistol)) PrefabUtility.RecordPrefabInstancePropertyModifications(pistol);
    }
}
