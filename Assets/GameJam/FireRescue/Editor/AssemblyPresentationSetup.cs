using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Only authors the existing assembly marker and next-group marker. Destinations and navigation stay in place.
public static class AssemblyPresentationSetup
{
    const string Folder = "Assets/GameJam/FireRescue/GroupRescue/Presentation";
    public const string Review = "Logs/AssemblyPolish";

    [MenuItem("GameJam/Final Loop/Polish assembly area")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (!mission || !mission.assemblyPoint) throw new InvalidOperationException("Open the authored evacuation scene.");
        var parent = mission.assemblyPoint.parent;
        Undo.SetCurrentGroupName("Assembly sign and open rescue markers");
        BuildAssembly(mission, parent);
        BuildGroup(mission, parent);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
        EditorSceneManager.SaveScene(mission.gameObject.scene);
        Directory.CreateDirectory(Review);
        File.WriteAllText(Review + "/applied.txt", "Saved " + mission.gameObject.scene.path + "\nAssembly position and all seven waiting slots preserved.");
    }

    public static void BuildGroup(EvacuationMission mission, Transform parent)
    {
        EnsureFolder();
        var root = Child(parent, "Next rescue group marker");
        var marker = root.GetComponent<RescueGroupMarker>();
        if (!marker) marker = Undo.AddComponent<RescueGroupMarker>(root.gameObject);
        Undo.RecordObject(marker, "Replace marker card with open emblem");
        ClearChildren(root);
        marker.mission = mission; marker.backdrop = null;
        root.position = mission.residents[0].panicGroupAnchor.position + Vector3.up * marker.height;
        root.rotation = Quaternion.identity; root.localScale = Vector3.one;
        var gold = Material("Rescue gold", new Color(1f, .69f, .16f), true);
        var light = Material("Rescue highlight", new Color(1f, .92f, .62f), true);
        var bronze = Material("Rescue contour", new Color(.31f, .14f, .035f), true);
        var symbol = Text(root, "Exclamation", "!", Vector3.zero, .42f, new Color(1f, .82f, .24f));
        symbol.fontStyle = FontStyle.Bold;
        Fit(symbol, .9f, 2.1f);
        symbol.transform.localPosition = -symbol.GetComponent<Renderer>().localBounds.center;
        marker.symbol = symbol;
        var shadow = Text(root, "Glyph contour", "!", symbol.transform.localPosition + new Vector3(.035f, -.035f, .035f), symbol.characterSize * 1.035f, bronze.color);
        shadow.fontStyle = FontStyle.Bold;
        var accents = new List<Renderer> { shadow.GetComponent<Renderer>() };
        // Thin interrupted arcs leave the landscape visible around the glyph.
        accents.Add(Arc(root, "Left open halo", 1.38f, 113f, 247f, .055f, gold));
        accents.Add(Arc(root, "Right open halo", 1.38f, -67f, 67f, .055f, gold));
        accents.Add(Arc(root, "Upper glint", 1.51f, 75f, 105f, .025f, light));
        var tip = Part(root, "Downward locator", PrimitiveType.Cube, new Vector3(0, -1.62f, .02f), new Vector3(.22f, .22f, .09f), gold);
        tip.transform.localRotation = Quaternion.Euler(0, 0, 45);
        accents.Add(tip);
        marker.accents = accents.ToArray();
        symbol.gameObject.SetActive(false);
        foreach (var renderer in marker.accents) renderer.enabled = false;
        EditorUtility.SetDirty(marker);
    }

    public static void BuildAssembly(EvacuationMission mission, Transform parent)
    {
        EnsureFolder();
        var root = Child(parent, "Assembly area visual");
        var marker = root.GetComponent<AssemblyPointMarker>();
        if (!marker) marker = Undo.AddComponent<AssemblyPointMarker>(root.gameObject);
        Undo.RecordObject(marker, "Ground the assembly sign");
        ClearChildren(root);
        marker.mission = mission;
        marker.waitingColor = new Color(1f, .72f, .22f);
        marker.readyColor = new Color(.35f, .91f, .65f);
        var amber = Material("Assembly amber inlay", new Color(.92f, .62f, .22f), true);
        var pale = Material("Warm lettering", new Color(.97f, .92f, .77f), true);
        var wood = Material("Oiled walnut", new Color(.23f, .125f, .057f));
        var woodEdge = Material("Walnut edge", new Color(.36f, .22f, .105f));
        var metal = Material("Bronze fittings", new Color(.16f, .19f, .17f), false, .5f);
        var panel = Material("Olive enamel", new Color(.075f, .17f, .145f), false, .12f);
        var stone = Material("Basalt footings", new Color(.3f, .3f, .27f));
        var line = root.GetComponent<LineRenderer>();
        if (!line) line = Undo.AddComponent<LineRenderer>(root.gameObject);
        line.sharedMaterial = amber; line.useWorldSpace = true; line.loop = true;
        line.positionCount = 128; line.widthMultiplier = .075f;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        for (int i = 0; i < line.positionCount; i++)
        {
            float a = i * Mathf.PI * 2 / line.positionCount;
            var p = mission.assemblyPoint.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 8.35f;
            p.y = Ground(p) + .12f; line.SetPosition(i, p);
        }
        marker.boundary = line;
        var edging = Child(root, "Grounded amber wayfinding");
        for (int i = 0; i < 32; i++)
        {
            float a = i * Mathf.PI * 2 / 32;
            var p = mission.assemblyPoint.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 8.72f;
            p.y = Ground(p) + .04f;
            var dash = Part(edging, "Perimeter inlay " + i, PrimitiveType.Cube, Vector3.zero, new Vector3(.7f, .035f, .13f), i % 4 == 0 ? pale : amber);
            dash.transform.position = p; dash.transform.rotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg - 90f, 0);
        }
        marker.slotMarkers = new Renderer[mission.assemblySlots.Length];
        for (int i = 0; i < marker.slotMarkers.Length; i++)
        {
            var slot = Child(root, "Waiting place marker " + i);
            slot.position = mission.assemblySlots[i].position + Vector3.up * .06f;
            var ring = slot.gameObject.AddComponent<MeshFilter>();
            ring.sharedMesh = RingAsset();
            var renderer = slot.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = amber;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            marker.slotMarkers[i] = renderer;
        }

        var sign = Child(root, "Freestanding assembly sign");
        var position = mission.assemblyPoint.position + new Vector3(0, 0, 7.5f);
        position.y = Ground(position); sign.position = position;
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 3.18f;
            Part(sign, "Stone post footing", PrimitiveType.Cube, new Vector3(x, .16f, 0), new Vector3(.7f, .5f, .7f), stone);
            Part(sign, "Timber post", PrimitiveType.Cube, new Vector3(x, 2.5f, 0), new Vector3(.22f, 5.1f, .28f), wood);
            Part(sign, "Post cap", PrimitiveType.Cube, new Vector3(x, 5.06f, 0), new Vector3(.32f, .12f, .38f), metal);
            Part(sign, "Post collar", PrimitiveType.Cube, new Vector3(x, .76f, 0), new Vector3(.245f, .3f, .31f), metal);
        }
        Part(sign, "Timber sign frame", PrimitiveType.Cube, new Vector3(0, 3.65f, 0), new Vector3(6.9f, 2.64f, .26f), woodEdge);
        Part(sign, "Enamel inset", PrimitiveType.Cube, new Vector3(0, 3.65f, 0), new Vector3(6.58f, 2.34f, .29f), panel);
        Part(sign, "Protective top rail", PrimitiveType.Cube, new Vector3(0, 5.03f, 0), new Vector3(7.05f, .14f, .54f), metal);
        var face = SignFace(sign, "Front lettering", false, pale, amber);
        var back = SignFace(sign, "Back lettering", true, pale, amber);
        // Select only the changing status; the title remains an authored part of the sign.
        marker.label = face.Find("Assembly status").GetComponent<TextMesh>();
        marker.reverseLabel = back.Find("Assembly status").GetComponent<TextMesh>();
        marker.occupancyLights = new Renderer[7];
        for (int i = 0; i < 7; i++)
            marker.occupancyLights[i] = Part(face, "Arrival indicator " + (i + 1), PrimitiveType.Sphere,
                new Vector3(-1.17f + i * .39f, 2.72f, -.19f), new Vector3(.14f, .14f, .04f), amber);
        Obstacle(sign, new Vector3(0, 2.5f, 0), new Vector3(7.1f, 5f, .65f));

        for (int side = -1; side <= 1; side += 2)
        {
            var bench = Child(root, side < 0 ? "West waiting bench" : "East waiting bench");
            var p = mission.assemblyPoint.position + new Vector3(side * 6.3f, 0, 5.5f);
            p.y = Ground(p); bench.position = p;
            bench.rotation = Quaternion.Euler(0, side * -25f, 0);
            for (int i = 0; i < 4; i++)
                Part(bench, "Seat plank", PrimitiveType.Cube, new Vector3(0, 1.1f, -.42f + i * .28f), new Vector3(3.4f, .14f, .23f), i % 2 == 0 ? wood : woodEdge);
            for (int i = 0; i < 3; i++)
                Part(bench, "Backrest plank", PrimitiveType.Cube, new Vector3(0, 1.64f + i * .28f, .57f), new Vector3(3.4f, .23f, .14f), wood);
            for (int leg = -1; leg <= 1; leg += 2)
            {
                Part(bench, "Bench support", PrimitiveType.Cube, new Vector3(leg * 1.26f, .5f, 0), new Vector3(.18f, 1.12f, 1.06f), metal);
                Part(bench, "Back support", PrimitiveType.Cube, new Vector3(leg * 1.26f, 1.63f, .67f), new Vector3(.13f, 1.3f, .13f), metal);
            }
            Obstacle(bench, new Vector3(0, 1.15f, .1f), new Vector3(3.6f, 2.4f, 1.35f));
        }
        EditorUtility.SetDirty(marker); EditorUtility.SetDirty(line);
    }

    static Transform SignFace(Transform sign, string name, bool reverse, Material pale, Material amber)
    {
        var root = Child(sign, name);
        if (reverse) root.localRotation = Quaternion.Euler(0, 180, 0);
        Fit(Text(root, "Service caption", "VILLAGE  /  EVACUATION", new Vector3(0, 4.55f, -.16f), .052f, new Color(1f, .75f, .31f)), 5.5f, .23f);
        Fit(Text(root, "Assembly title", "ASSEMBLY POINT", new Vector3(0, 4.08f, -.16f), .108f, pale.color), 5.5f, .48f);
        Part(root, "Title rule", PrimitiveType.Cube, new Vector3(0, 3.7f, -.17f), new Vector3(5.55f, .026f, .016f), amber);
        var status = Text(root, "Assembly status", "7 / 7  ASSEMBLED\nTHE GROUP IS TAKING ITS PLACES", new Vector3(0, 3.22f, -.17f), .057f, pale.color);
        status.lineSpacing = 1.2f;
        Fit(status, 5.55f, .67f);
        status.text = "0 / 7  ASSEMBLED\nBRING THE GROUPS HERE";
        for (int side = -1; side <= 1; side += 2)
            foreach (float y in new[] { 2.65f, 4.66f })
                Part(root, "Brass fastener", PrimitiveType.Sphere, new Vector3(side * 3.07f, y, -.163f), new Vector3(.085f, .085f, .045f), amber);
        return root;
    }

    static TextMesh Text(Transform parent, string name, string value, Vector3 position, float size, Color color)
    {
        var go = Child(parent, name).gameObject; go.transform.localPosition = position;
        var text = go.AddComponent<TextMesh>();
        text.text = value; text.font = Ashfall.UI.AshfallTypography.Semibold; text.fontSize = 96;
        text.anchor = TextAnchor.MiddleCenter; text.alignment = TextAlignment.Center; text.characterSize = size; text.color = color;
        const string path = Folder + "/Depth tested lettering.mat";
        var ink = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!ink) { ink = new Material(Shader.Find("GameJam/WorldLettering")); AssetDatabase.CreateAsset(ink, path); }
        var renderer = text.GetComponent<MeshRenderer>(); renderer.sharedMaterial = ink;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        go.AddComponent<WorldLetteringFontAtlas>().RefreshAtlas();
        return text;
    }
    static void Fit(TextMesh text, float width, float height)
    {
        text.font.RequestCharactersInTexture(text.text, text.fontSize, text.fontStyle);
        Vector3 size = text.GetComponent<Renderer>().localBounds.size;
        text.characterSize *= Mathf.Min(1f, width / Mathf.Max(.001f, size.x), height / Mathf.Max(.001f, size.y));
        text.GetComponent<WorldLetteringFontAtlas>().RefreshAtlas();
    }
    static Renderer Arc(Transform parent, string name, float radius, float start, float end, float width, Material material)
    {
        var line = Child(parent, name).gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false; line.positionCount = 40; line.widthMultiplier = width;
        line.sharedMaterial = material; line.numCapVertices = 3; line.alignment = LineAlignment.TransformZ;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        for (int i = 0; i < line.positionCount; i++)
        {
            float a = Mathf.Lerp(start, end, i / (float)(line.positionCount - 1)) * Mathf.Deg2Rad;
            line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), .03f) * radius);
        }
        return line;
    }
    static Renderer Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.localPosition = position; go.transform.localScale = size;
        var renderer = go.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        return renderer;
    }
    static void Obstacle(Transform root, Vector3 center, Vector3 size)
    {
        var collider = root.gameObject.AddComponent<BoxCollider>(); collider.center = center; collider.size = size;
        var obstacle = root.gameObject.AddComponent<NavMeshObstacle>(); obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.center = center; obstacle.size = size; obstacle.carving = true; obstacle.carveOnlyStationary = true;
    }
    static Transform Child(Transform parent, string name)
    {
        var t = parent.Find(name);
        if (!t) { t = new GameObject(name).transform; t.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(t.gameObject, "Assembly presentation"); }
        return t;
    }
    static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);
    }
    static Material Material(string name, Color color, bool unlit = false, float metallic = 0)
    {
        string path = Folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", color);
        if (!unlit) { mat.SetFloat("_Metallic", metallic); mat.SetFloat("_Smoothness", metallic > 0 ? .27f : .15f); }
        EditorUtility.SetDirty(mat); return mat;
    }
    static Mesh RingAsset()
    {
        const string path = Folder + "/Waiting place inlay.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh) return mesh;
        mesh = new Mesh { name = "Open waiting place inlay" };
        var vertices = new List<Vector3>(); var triangles = new List<int>();
        for (int i = 0; i < 48; i++)
        {
            if (i % 12 >= 10) continue;
            float a = i * Mathf.PI * 2 / 48, b = (i + 1) * Mathf.PI * 2 / 48;
            int j = vertices.Count;
            vertices.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .64f);
            vertices.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .58f);
            vertices.Add(new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * .58f);
            vertices.Add(new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * .64f);
            triangles.AddRange(new[] { j, j + 1, j + 2, j, j + 2, j + 3 });
        }
        mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path); return mesh;
    }
    static float Ground(Vector3 point)
    {
        foreach (var terrain in Terrain.activeTerrains)
        {
            Vector3 local = point - terrain.transform.position;
            if (local.x >= 0 && local.z >= 0 && local.x <= terrain.terrainData.size.x && local.z <= terrain.terrainData.size.z)
                return terrain.SampleHeight(point) + terrain.transform.position.y + .13f;
        }
        return point.y;
    }
    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/GameJam/FireRescue/GroupRescue", "Presentation");
    }
}
