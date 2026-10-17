using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BKPureNature;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class VillageRescuePolishSetup
{
    const string PolishAssets = "Assets/GameJam/TownDressing/VillagePolish";
    static readonly Vector3 Square = new Vector3(63, 0, -570);
    static TownDressingGeometry.Palette villagePalette;
    static Transform village;
    static BurnableBuilding[] villageHouses;
    static ObjectiveNavigation roadNavigation;
    static System.Random variation;
    static readonly List<string> authoring = new List<string>();
    static readonly List<Bounds> placed = new List<Bounds>();

    [MenuItem("GameJam/Final Loop/Refine village lighting")]
    public static void RefineLighting()
    {
        RequireScene(); village = GameObject.Find("11 - Ashfall village").transform;
        LightVillage();
        foreach (var filter in village.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer && filter.sharedMesh && renderer.sharedMaterials.Length > filter.sharedMesh.subMeshCount)
            { renderer.sharedMaterials = renderer.sharedMaterials.Take(filter.sharedMesh.subMeshCount).ToArray(); Record(renderer); }
        }
        DynamicGI.UpdateEnvironment();
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Audit("After"); Capture("After");
    }

    [MenuItem("GameJam/Final Loop/Polish rescue village")]
    public static void AuthorVillage()
    {
        RequireScene();
        Directory.CreateDirectory(Output + "/After");
        if (!File.Exists(Output + "/Before/Scene-in-editor.unity")) throw new InvalidOperationException("Capture the village before authoring.");
        EnsurePolishFolder(PolishAssets);
        variation = new System.Random(2718); MeshSerial = 0; authoring.Clear(); placed.Clear();
        villageHouses = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).OrderBy(h => h.name, StringComparer.Ordinal).ToArray();
        roadNavigation = Object.FindFirstObjectByType<ObjectiveNavigation>();
        var dressing = GameObject.Find(TownDressingSetup.RootName).transform;
        // Replace only the previous exterior pass. The starting room, lake equipment and shelter stay linked.
        foreach (Transform area in dressing.Cast<Transform>().ToArray())
            if (area.name.StartsWith("01 -") || area.name.StartsWith("02 -") || area.name.StartsWith("03 -") ||
                area.name.StartsWith("04 -") || area.name.StartsWith("05 -") || area.name.StartsWith("06 -") ||
                area.name.StartsWith("10 -") || area.name == "11 - Ashfall village") Undo.DestroyObjectImmediate(area.gameObject);
        village = Group("11 - Ashfall village", dressing);
        villagePalette = Palette();
        PrepareHouseTerrace();
        MoveHousesAndResidents();
        RetargetLavaBranches();
        TuneRescueVehicles();
        LightVillage();
        BuildSquare();
        BuildLanesAndCourtyards();
        BuildMarket();
        PlantVillage();
        GroundOldPaths(dressing);
        Physics.SyncTransforms();
        Directory.CreateDirectory(EvacuationSceneAudit.Folder);
        EvacuationMissionSetup.RebuildNavigation();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllLines(Output + "/After/authoring.txt", authoring);
        Audit("After"); Capture("After");
        Debug.Log("Rescue village authored and saved: " + villageHouses.Length + " houses.");
    }

    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Polish rescue village");
        go.transform.SetParent(parent, false); return go.transform;
    }
    static void Record(Object item)
    {
        EditorUtility.SetDirty(item);
        if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
    }
    static void PrepareHouseTerrace()
    {
        // A full building footprint must be supported; sampling only its pivot can bury the back wall in the slope.
        Vector3 centre = new Vector3(109, 0, -681);
        var terrain = Terrain.activeTerrains.First(t => {
            Vector3 p = centre - t.transform.position;
            return p.x >= 0 && p.z >= 0 && p.x <= t.terrainData.size.x && p.z <= t.terrainData.size.z;
        });
        var data = terrain.terrainData; Vector3 origin = terrain.transform.position, size = data.size;
        string sourcePath = AssetDatabase.GetAssetPath(data);
        if (string.IsNullOrEmpty(sourcePath) || !sourcePath.StartsWith("Assets/", StringComparison.Ordinal))
            throw new InvalidOperationException("The village terrain must be an editable project asset.");
        string backup = Output + "/Before/Terrain-before-terrace.asset";
        if (!File.Exists(backup)) File.Copy(sourcePath, backup);
        int resolution = data.heightmapResolution;
        int x0 = Mathf.Clamp(Mathf.FloorToInt((centre.x - 40 - origin.x) / size.x * (resolution - 1)), 0, resolution-1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt((centre.x + 40 - origin.x) / size.x * (resolution - 1)), 0, resolution-1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt((centre.z - 38 - origin.z) / size.z * (resolution - 1)), 0, resolution-1);
        int z1 = Mathf.Clamp(Mathf.CeilToInt((centre.z + 38 - origin.z) / size.z * (resolution - 1)), 0, resolution-1);
        var heights = data.GetHeights(x0,z0,x1-x0+1,z1-z0+1); int changed = 0;
        foreach (var house in villageHouses) if (house.name != "House_Green_Prefab (4)") placed.Add(BoundsOf(house.transform));
        for (int z = 0; z < heights.GetLength(0); z++) for (int x = 0; x < heights.GetLength(1); x++)
        {
            Vector3 p = new Vector3(origin.x + (x+x0)/(float)(resolution-1)*size.x, 0, origin.z + (z+z0)/(float)(resolution-1)*size.z);
            float dx = Mathf.Max(0, Mathf.Abs(p.x-centre.x)-27), dz = Mathf.Max(0, Mathf.Abs(p.z-centre.z)-25);
            float weight = 1-Mathf.SmoothStep(0,1,Mathf.Sqrt(dx*dx+dz*dz)/12f); if (weight <= 0) continue;
            bool protectedRoad = false;
            if (roadNavigation) for (int i=1; i<roadNavigation.lakeRoad.Length; i++)
                if (roadNavigation.lakeRoad[i-1] && roadNavigation.lakeRoad[i] && SegmentDistance(p,roadNavigation.lakeRoad[i-1].position,roadNavigation.lakeRoad[i].position)<18) { protectedRoad=true; break; }
            if (protectedRoad || placed.Any(b => p.x>b.min.x-1 && p.x<b.max.x+1 && p.z>b.min.z-1 && p.z<b.max.z+1)) continue;
            float previous = origin.y+heights[z,x]*size.y, next = Mathf.Lerp(previous,1.05f,weight);
            if (Mathf.Abs(next-previous)<.01f) continue;
            heights[z,x]=Mathf.Clamp01((next-origin.y)/size.y); changed++;
        }
        placed.Clear();
        if (changed > 0)
        {
            Undo.RegisterCompleteObjectUndo(data,"Create a supported village house terrace");
            data.SetHeights(x0,z0,heights); terrain.Flush(); Record(data);
        }
        authoring.Add("Supported hill house terrace: " + changed + " terrain samples; existing houses and the truck road protected.");
    }
    static void MoveHousesAndResidents()
    {
        Vector3[] positions = {
            new Vector3(17.3f, 0, -606.8f), new Vector3(61, 0, -638), new Vector3(73, 0, -521),
            new Vector3(133, 0, -572), new Vector3(109, 0, -681), new Vector3(-61, 0, -615), new Vector3(-60, 0, -519)
        };
        float[] angles = { 348.59f, 5f, 190f, 264f, 339f, 27f, 148f };
        for (int i = 0; i < villageHouses.Length; i++)
        {
            var house = villageHouses[i];
            if (i > 0 && i < positions.Length)
            {
                Undo.RecordObject(house.transform, "Arrange houses around village streets");
                Vector3 next = positions[i]; next.y = Ground(next) + (i == 5 ? -.03f : .01f);
                house.transform.SetPositionAndRotation(next, Quaternion.Euler(0, angles[i], 0)); Record(house.transform);
            }
            var door = HouseDoor(house);
            Vector3 outward = DoorOutward(house, door), side = Vector3.Cross(Vector3.up, outward);
            Vector3 doorway = DoorAnchor(door);
            int residentIndex = 0;
            foreach (var resident in Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None).Where(r => r.door && r.door.transform.IsChildOf(house.transform)).OrderBy(r => r.name))
            {
                Vector3 p = doorway - outward * (5.2f + residentIndex * 1.1f) + side * (residentIndex == 0 ? -.8f : .8f);
                p.y = house.transform.position.y + 1.05f;
                Undo.RecordObject(resident.transform, "Keep villagers inside their relocated home");
                resident.transform.SetPositionAndRotation(p, Quaternion.LookRotation(outward)); Record(resident.transform);
                if (resident.animator)
                {
                    Undo.RecordObject(resident.animator.transform, "Align the character model with its navigation body");
                    Vector3 local = resident.animator.transform.localPosition; local.x = local.z = 0;
                    resident.animator.transform.localPosition = local; Record(resident.animator.transform);
                }
                if (resident.houseExit)
                {
                    Undo.RecordObject(resident.houseExit, "Align rescue exit with the actual front door");
                    Vector3 exit = doorway + outward * 7.5f; exit.y = Ground(exit) + .06f;
                    resident.houseExit.position = exit; Record(resident.houseExit);
                }
                residentIndex++;
            }
            // Use the existing timber variants instead of giving every building the same green siding.
            string timber = i % 3 == 1 ? "OldHouseMapWood02" : i % 3 == 2 ? "OldHouseMapWood01" : "OldHouseMapWoodGreen";
            var siding = AssetDatabase.LoadAssetAtPath<Material>("Assets/ALP_Assets/country house01/Materials/" + timber + ".mat");
            if (siding) foreach (var r in house.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = r.sharedMaterials; bool changed = false;
                var original = PrefabUtility.GetCorrespondingObjectFromSource(r);
                var originalMaterials = original ? original.sharedMaterials : Array.Empty<Material>();
                for (int m = 0; m < materials.Length; m++)
                {
                    if (m >= originalMaterials.Length || !originalMaterials[m]) continue;
                    if (originalMaterials[m].name == "OldHouseMapWoodGreen") { materials[m] = siding; changed = true; }
                    else if (materials[m] && materials[m].name.StartsWith("OldHouseMapWood", StringComparison.Ordinal)) { materials[m] = originalMaterials[m]; changed = true; }
                }
                if (changed) { Undo.RecordObject(r, "Vary village timber finishes"); r.sharedMaterials = materials; Record(r); }
            }
            authoring.Add($"{house.name}: {house.transform.position:F2}, yaw {house.transform.eulerAngles.y:F1}, {residentIndex} resident(s), {timber}");
        }
    }
    static Transform HouseDoor(BurnableBuilding house)
    {
        var children = house.GetComponentsInChildren<Transform>(true);
        return children.FirstOrDefault(t => t.GetComponent<EvacuationDoor>()) ?? children.FirstOrDefault(t => t.name == "opendoor") ?? house.transform;
    }
    static Vector3 DoorOutward(BurnableBuilding house, Transform door)
    {
        Vector3 direction = DoorAnchor(door) - BoundsOf(house.transform).center; direction.y = 0;
        return direction.sqrMagnitude > .1f ? direction.normalized : house.transform.forward;
    }
    static Vector3 DoorAnchor(Transform door)
    {
        var renderer = door.GetComponent<Renderer>() ?? door.GetComponentInChildren<Renderer>();
        return renderer ? renderer.bounds.center : door.position;
    }
    static void RetargetLavaBranches()
    {
        foreach (var flow in Object.FindObjectsByType<ProgressiveLavaFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var house = villageHouses.FirstOrDefault(h => flow.name == "Town Branch - " + h.name);
            if (!house) continue;
            var source = flow.source ? flow.source : flow.GetComponent<BK_Lava>();
            if (!source || !source.m_Container || source.m_Container.Spline.Count < 2) continue;
            var container = source.m_Container; var spline = container.Spline;
            int end = flow.reverse ? 0 : spline.Count - 1;
            Vector3 previous = container.transform.TransformPoint((Vector3)spline[end].Position);
            Vector3 destination = house.transform.position; destination.y = Ground(destination) + .24f;
            Vector3 delta = destination - previous;
            Undo.RecordObject(container, "Keep lava branch connected to the relocated house");
            for (int offset = 0; offset < Mathf.Min(3, spline.Count - 1); offset++)
            {
                int index = end + (flow.reverse ? offset : -offset);
                var knot = spline[index]; Vector3 p = container.transform.TransformPoint((Vector3)knot.Position);
                p += delta * (offset == 0 ? 1f : offset == 1 ? .55f : .15f); p.y = Ground(p) + .24f;
                knot.Position = (Unity.Mathematics.float3)container.transform.InverseTransformPoint(p); spline[index] = knot;
            }
            Record(container); source.RequestRebuild(); source.Rebuild(); Record(source);
            var ignition = flow.GetComponent<LavaIgnitionSource>(); if (ignition) ignition.RefreshSurfaceMesh(true);
            authoring.Add("Lava branch follows " + house.name + " to " + destination.ToString("F2"));
        }
    }
    static void TuneRescueVehicles()
    {
        foreach (var truck in Object.FindObjectsByType<FireTruckDrive>(FindObjectsSortMode.None))
        {
            var damage = truck.GetComponent<ThermalDamage>();
            if (!damage) damage = Undo.AddComponent<ThermalDamage>(truck.gameObject);
            Undo.RecordObject(damage, "Give the fire truck time to fight fires"); damage.ConfigureRescueTruck(); Record(damage);
        }
        const string prefabPath = "Assets/GameJam/FireRescue/Prefabs/Fire Truck.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var damage = prefab.GetComponent<ThermalDamage>() ?? prefab.AddComponent<ThermalDamage>();
            damage.ConfigureRescueTruck(); PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        if (roadNavigation) { Undo.RecordObject(roadNavigation, "Live optional driving guidance"); roadNavigation.drivingMarkerLookAhead = 34f; roadNavigation.roadGuidanceDistance = 38f; Record(roadNavigation); }
        authoring.Add("Truck: 240 health, 3-second heat grace, automatic water suppression, full motor and pump above 60% integrity.");
    }
    static void LightVillage()
    {
        foreach (var manager in Object.FindObjectsByType<BK_EnvironmentManager>(FindObjectsSortMode.None))
        {
            Undo.RecordObject(manager, "Keep authored village lighting");
            manager.overrideAmbientColor = false; manager.overrideSunColor = false; manager.overrideFogColor = false;
            // The pack's opaque cloud volume obscures the sky in this URP project. Eruption ash is authored separately.
            manager.cloudsMaterial = null;
            // Keep the volcanic wind and distant cloud layers above the playable valley.
            manager.Altitude = Mathf.Max(800, manager.Altitude); manager.volumeSize = Mathf.Min(360, manager.volumeSize); Record(manager);
        }
        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            Undo.RecordObjects(new Object[] { sun, sun.transform }, "Warm dusk over Ashfall village");
            sun.transform.rotation = Quaternion.Euler(43, -35, 0); sun.intensity = 2.3f;
            sun.color = new Color(1f, .83f, .66f); sun.shadowStrength = .72f; Record(sun); Record(sun.transform);
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.54f, .64f, .74f);
        RenderSettings.ambientEquatorColor = new Color(.44f, .46f, .47f);
        RenderSettings.ambientGroundColor = new Color(.24f, .23f, .21f); RenderSettings.ambientIntensity = 1.3f;
        if (village)
        {
            var fill = village.Find("Cool dusk fill light"); if (!fill) fill = Group("Cool dusk fill light", village);
            var lamp = fill.GetComponent<Light>(); if (!lamp) lamp = fill.gameObject.AddComponent<Light>();
            fill.rotation = Quaternion.Euler(52, 145, 0); lamp.type = LightType.Directional;
            lamp.color = new Color(.66f, .79f, 1f); lamp.intensity = 1.35f; lamp.shadows = LightShadows.None; Record(lamp); Record(fill);
        }
        var sky = AssetDatabase.LoadAssetAtPath<Material>(PolishAssets + "/Ashfall dusk sky.mat");
        if (!sky)
        {
            sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Ashfall dusk sky" };
            AssetDatabase.CreateAsset(sky, PolishAssets + "/Ashfall dusk sky.mat");
        }
        sky.SetColor("_SkyTint", new Color(.43f, .49f, .57f)); sky.SetColor("_GroundColor", new Color(.2f, .17f, .14f));
        sky.SetFloat("_AtmosphereThickness", 1.25f); sky.SetFloat("_Exposure", 1.1f); sky.SetFloat("_SunSize", .025f);
        RenderSettings.skybox = sky; RenderSettings.sun = sun; Record(sky);
        var atmosphere = Object.FindFirstObjectByType<VolcanoAtmospherePolish>();
        if (atmosphere)
        {
            Undo.RecordObject(atmosphere, "Readable volcanic distance haze");
            atmosphere.clearFogColor = new Color(.3f, .34f, .36f); atmosphere.ashFogColor = new Color(.32f, .28f, .24f);
            atmosphere.clearFogStart = 150; atmosphere.clearFogEnd = 1700; atmosphere.ashFogStart = 115; atmosphere.ashFogEnd = 1400;
            atmosphere.calmVignette = .1f; atmosphere.defenseVignette = .14f; Record(atmosphere);
        }
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = atmosphere ? atmosphere.clearFogColor : new Color(.3f, .34f, .36f);
        RenderSettings.fogStartDistance = 150; RenderSettings.fogEndDistance = 1700;
        foreach (var volume in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            var profile = volume.sharedProfile; if (!profile) continue;
            if (profile.TryGet<ColorAdjustments>(out var grade))
            {
                // The gameplay atmosphere component owns its own exposure during eruption.
                Undo.RecordObject(grade, "Reveal timber and basalt details");
                grade.postExposure.Override(.5f); grade.contrast.Override(8); grade.saturation.Override(3); Record(grade);
            }
        }
        authoring.Add("Warm dusk sun, cool ambient fill, warm practical lights and readable volcanic haze.");
    }
    static TownDressingGeometry.Palette Palette()
    {
        Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/GameJam/TownDressing/Materials/" + name + ".mat");
        return new TownDressingGeometry.Palette { wood = Load("Weathered warm timber"), darkWood = Load("Dark endgrain"), metal = Load("Aged iron"),
            accent = Load("Muted sage paint"), fabric = Load("Natural linen"), foliage = Load("Olive foliage"), stone = Load("Weathered stone"), glow = Load("Amber lantern glass") };
    }
    static Material Stone(string name, Color tint)
    {
        string path = PolishAssets + "/" + name + ".mat"; var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(villagePalette.stone) { name = name }; AssetDatabase.CreateAsset(material, path); }
        material.SetColor("_BaseColor", tint); if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .08f); Record(material); return material;
    }
    static Material[] PavingMaterials() => new[] {
        Stone("Basalt paving", new Color(.33f,.34f,.33f)), Stone("Warm weathered paving", new Color(.43f,.4f,.34f)),
        Stone("Pale edge stones", new Color(.5f,.48f,.42f)) };
    static void BuildSquare()
    {
        var area = Group("01 - Basalt village square", village);
        var mesh = new VillageMesh();
        for (float z = -15; z <= 15; z += 1.35f) for (float x = -23; x <= 23; x += 1.4f)
        {
            if (x*x/(23*23f) + z*z/(15*15f) > 1) continue;
            Vector3 p = Square + new Vector3(x + Next(-.08f,.08f), 0, z + Next(-.08f,.08f)); p.y = Ground(p) + .04f;
            mesh.Box(p - Square, new Vector3(1.29f, .1f, 1.24f), Quaternion.Euler(0, Next(-3,3), 0), variation.Next(0, 2));
        }
        for (int i = 0; i < 112; i++)
        {
            float a = i * Mathf.PI * 2 / 112; Vector3 p = Square + new Vector3(Mathf.Cos(a)*23.3f, 0, Mathf.Sin(a)*15.3f);
            p.y = Ground(p) + .09f; mesh.Box(p - Square, new Vector3(1.12f,.18f,.55f), Quaternion.Euler(0, -a*Mathf.Rad2Deg, 0), 2);
        }
        MeshObject(area, "Hand laid basalt setts", mesh, PavingMaterials(), Square);
        BuildWell(Group("Old village well", area), new Vector3(64, 0, -561));
        Prop("bench", area, "Wellside bench west", new Vector3(55,0,-557), 110, 2.7f, true);
        Prop("bench", area, "Wellside bench east", new Vector3(74,0,-557), 250, 2.7f, true);
        Prop("planter", area, "Wellside herbs", new Vector3(64,0,-551), 0, 3.3f);
        Prop("noticeboard", area, "Village rescue board", new Vector3(83,0,-565), 260, 2.2f, true);
        Lantern(area, "West square lantern", new Vector3(43,0,-562), 0);
        Lantern(area, "East square lantern", new Vector3(84,0,-557), 0);
        authoring.Add("A 46m x 30m stone square, timber well canopy, resting benches, herbs and rescue board.");
    }
    static void BuildWell(Transform parent, Vector3 p)
    {
        p.y = Ground(p); parent.position = p;
        var stone = new VillageMesh(); var timber = new VillageMesh();
        for (int row = 0; row < 3; row++) for (int i = 0; i < 18; i++)
        {
            float a = (i + row*.5f)*Mathf.PI*2/18;
            stone.Box(new Vector3(Mathf.Cos(a)*2.4f,.38f+row*.68f,Mathf.Sin(a)*2.4f), new Vector3(.85f,.65f,.95f), Quaternion.Euler(0,-a*Mathf.Rad2Deg,0), row%2);
        }
        for (int i = 0; i < 18; i++)
        {
            float a=i*Mathf.PI*2/18; stone.Box(new Vector3(Mathf.Cos(a)*2.4f,2.22f,Mathf.Sin(a)*2.4f),new Vector3(1.05f,.22f,1.02f),Quaternion.Euler(0,-a*Mathf.Rad2Deg,0),2);
        }
        foreach(float x in new[]{-3.1f,3.1f}) { timber.Box(new Vector3(x,3.5f,0),new Vector3(.3f,7,.3f),Quaternion.identity,0); timber.Box(new Vector3(x,5.9f,0),new Vector3(.3f,.25f,5.6f),Quaternion.identity,1); }
        timber.Box(new Vector3(0,6.95f,0),new Vector3(7,.3f,.3f),Quaternion.identity,1);
        foreach (int side in new[]{-1,1}) for(int plank=0;plank<11;plank++)
            timber.Box(new Vector3(-3.5f+plank*.7f,6.52f,side*1.35f),new Vector3(.68f,.16f,2.95f),Quaternion.Euler(side*19,0,0),0);
        timber.Box(new Vector3(0,4.2f,0),new Vector3(6.2f,.16f,.16f),Quaternion.identity,1);
        MeshObject(parent,"Basalt well ring",stone,PavingMaterials(),p,true);
        MeshObject(parent,"Weathered timber canopy",timber,new[]{villagePalette.wood,villagePalette.darkWood},p);
        Prop("bucket",parent,"Well bucket",p+new Vector3(2.1f,2.35f,-.6f),0,2,false,false);
        Prop("lantern",parent,"Well hanging lantern",p+new Vector3(-2.8f,3.8f,0),0,1.1f,false,false);
        PointLight(parent,new Vector3(0,4.8f,-.6f),18,5f);
        // A dark, shallow inset gives the open ring depth without a hole in the walking surface.
        var inset = new VillageMesh(); inset.Box(new Vector3(0,.08f,0),new Vector3(3.7f,.1f,3.7f),Quaternion.identity,0);
        MeshObject(parent,"Well interior",inset,new[]{villagePalette.darkWood},p);
    }
    static void BuildLanesAndCourtyards()
    {
        var streets = Group("02 - Connected streets", village);
        foreach (var house in villageHouses)
        {
            var area=Group(house.name+" courtyard",streets); var door=HouseDoor(house); Vector3 forward=DoorOutward(house,door),right=Vector3.Cross(Vector3.up,forward);
            Vector3 doorway=DoorAnchor(door);
            BuildEntryRamp(area,house,door,forward);
            Vector3 start=doorway+forward*7.2f; start.y=Ground(start);
            Vector3 meeting=house.transform.position.x<0 ? new Vector3(-31,0,-565) : Square;
            Vector3 middle=Vector3.Lerp(start,meeting,.45f);
            Path(area,"Worn cobbled doorstep",new[]{start,start+forward*8,middle,meeting},3.7f);
            // Leave a wide front approach and vehicle corridor; side yards provide the detail.
            Prop("bench",area,"Porch bench",door.position+forward*6-right*7,Quaternion.LookRotation(-forward).eulerAngles.y,2.3f,true);
            Prop("planter",area,"Doorstep herb pot",door.position+forward*3+right*4.5f,0,2.2f);
            Prop("barrel",area,"Rainwater barrel",door.position+forward*4-right*10,house.transform.eulerAngles.y,2.6f,true);
            Prop("logs",area,"Winter wood stack",door.position+forward*4+right*11,house.transform.eulerAngles.y,2.6f,true);
            Prop("fence",area,"Left garden boundary",door.position+forward*11-right*12,house.transform.eulerAngles.y,2.5f,true);
            Prop("fence",area,"Right garden boundary",door.position+forward*11+right*12,house.transform.eulerAngles.y,2.5f,true);
            Lantern(area,"Porch light",door.position+forward*4-right*4.5f,house.transform.eulerAngles.y);
        }
        Path(streets,"West village walkway",new[]{new Vector3(-71,0,-553),new Vector3(-38,0,-559),new Vector3(-10,0,-553),new Vector3(22,0,-551),Square},4.3f);
        Path(streets,"Shelter footway",new[]{new Vector3(-35,0,-559),new Vector3(-67,0,-558),new Vector3(-91,0,-561)},3.8f);
        // A single ground mesh follows the proven driving route; there are no road checkpoint colliders.
        if(roadNavigation && roadNavigation.lakeRoad.Length>1)
            Path(streets,"Lake access gravel lane",roadNavigation.lakeRoad.Where(t=>t).Reverse().Select(t=>t.position).ToArray(),12f,true);
        Lantern(streets,"West lane lantern",new Vector3(-32,0,-550),0);
        Lantern(streets,"Starting house street lantern",new Vector3(10,0,-549),0);
        Lantern(streets,"Village lake exit lantern",new Vector3(133,0,-622),0);
    }
    static void BuildEntryRamp(Transform parent,BurnableBuilding house,Transform door,Vector3 outward)
    {
        Vector3 centre=DoorAnchor(door),side=Vector3.Cross(Vector3.up,outward)*2.6f;
        Vector3 inner=centre-outward*.7f,outer=centre+outward*7.2f;
        var renderer=door.GetComponent<Renderer>()??door.GetComponentInChildren<Renderer>();
        inner.y=renderer?renderer.bounds.min.y+.035f:house.transform.position.y+1.06f;
        outer.y=Ground(outer)+.06f;
        var mesh=new VillageMesh();Vector3 origin=centre;origin.y=0;
        mesh.Quad(inner-side-origin,outer-side-origin,inner+side-origin,outer+side-origin,0);
        Vector3 lowInner=inner,lowOuter=outer;lowInner.y=Ground(inner)-.06f;lowOuter.y=Ground(outer)-.06f;
        mesh.Quad(inner-side-origin,lowInner-side-origin,outer-side-origin,lowOuter-side-origin,0);
        mesh.Quad(outer+side-origin,lowOuter+side-origin,inner+side-origin,lowInner+side-origin,0);
        MeshObject(parent,"Stone entry ramp "+house.name,mesh,new[]{villagePalette.stone},origin,true);
    }
    static void BuildMarket()
    {
        var market=Group("03 - Market and rescue supplies",village);
        Stall(market,"Linen covered market stall",new Vector3(34,0,-543),165);
        Stall(market,"Rescue supply stall",new Vector3(94,0,-548),230);
        Prop("supplies",market,"Stacked rescue crates",new Vector3(99,0,-548),20,3,true);
        Prop("barrel",market,"Market rain barrel",new Vector3(28,0,-544),0,2.9f,true);
        Prop("laundry",market,"West washing line",new Vector3(-45,0,-587),35,3f,true);
        Prop("workbench",market,"Village carpenter",new Vector3(140,0,-594),270,2.7f,true);
    }
    static void Stall(Transform parent,string name,Vector3 p,float yaw)
    {
        if(!DecorSafe(p,4)) {authoring.Add("Skipped blocked stall "+name);return;}
        var area=Group(name,parent);p.y=Ground(p);area.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));
        var b=new VillageMesh();
        foreach(float x in new[]{-3.8f,3.8f})foreach(float z in new[]{-1.8f,1.8f})b.Box(new Vector3(x,3.5f,z),new Vector3(.2f,7,.2f),Quaternion.identity,0);
        b.Box(new Vector3(0,2.5f,0),new Vector3(8,.3f,3.6f),Quaternion.identity,0);
        for(int i=0;i<8;i++) b.Box(new Vector3(-3.5f+i,6.9f,0),new Vector3(.98f,.13f,4.8f),Quaternion.Euler(8,0,0),i%2==0?1:2);
        b.Box(new Vector3(0,6.2f,-2.35f),new Vector3(8,.6f,.12f),Quaternion.identity,2);
        MeshObject(area,"Timber stall and linen awning",b,new[]{villagePalette.wood,villagePalette.fabric,villagePalette.accent},Vector3.zero,false,true);
        var boxes=TownDressingGeometry.Create("crate",area,villagePalette);boxes.transform.localScale=Vector3.one*2;boxes.transform.localPosition=new Vector3(-2,2.65f,0);
        var bucket=TownDressingGeometry.Create("bucket",area,villagePalette);bucket.transform.localScale=Vector3.one*2;bucket.transform.localPosition=new Vector3(2,2.65f,0);
        var solid=area.gameObject.AddComponent<BoxCollider>();solid.center=new Vector3(0,1.25f,0);solid.size=new Vector3(8,2.5f,3.6f);
        placed.Add(new Bounds(p+Vector3.up*3,new Vector3(9,6,6)));
    }
    static void PlantVillage()
    {
        var area=Group("04 - Basalt gardens and windbreaks",village);
        Vector3[] trees={new Vector3(-35,0,-643),new Vector3(-93,0,-626),new Vector3(-89,0,-512),new Vector3(-22,0,-524),new Vector3(23,0,-516),new Vector3(111,0,-504),new Vector3(160,0,-549),new Vector3(151,0,-611)};
        for(int i=0;i<trees.Length;i++) ExistingPlant(area,"Trees/Burntree"+(i%4+1),"Wind shaped tree "+(i+1),trees[i],Next(15,21),true);
        foreach(var house in villageHouses)
        {
            var door=HouseDoor(house);var forward=DoorOutward(house,door);var right=Vector3.Cross(Vector3.up,forward);
            for(int side=-1;side<=1;side+=2) for(int i=0;i<9;i++)
            {
                Vector3 p=door.position+forward*Next(5,15)+right*side*Next(7,14);
                ExistingPlant(area,i%3==0?"Plants/Deathbells_1":i%3==1?"Plants/Grass1":"Plants/Grass2",house.name+" garden "+side+"-"+i,p,Next(.6f,1.5f),false);
            }
        }
        Vector3[] edges={new Vector3(-45,0,-638),new Vector3(-91,0,-605),new Vector3(-91,0,-516),new Vector3(-24,0,-523),new Vector3(31,0,-514),new Vector3(114,0,-503),new Vector3(158,0,-550),new Vector3(151,0,-617)};
        for(int i=0;i<edges.Length;i++)for(int j=0;j<3;j++)ExistingPlant(area,"Rocks/Rock_"+(j%3),"Basalt garden rock "+i+"-"+j,edges[i]+new Vector3(Next(-3,3),0,Next(-2,2)),Next(1,2.7f),false);
        authoring.Add("Reused pack trees, grass, deathbells and basalt rocks as small courtyard gardens and settlement edges.");
    }
    static void ExistingPlant(Transform parent,string asset,string name,Vector3 p,float height,bool solid)
    {
        if(!DecorSafe(p,solid?3:1))return;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/BK/PureNature_Volcano/Prefabs/"+asset+".prefab");if(!prefab)return;
        var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,parent);Undo.RegisterCreatedObjectUndo(go,"Plant volcanic village");go.name=name;
        go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,Next(0,360),0));
        Bounds bounds=BoundsOf(go.transform);go.transform.localScale*=height/Mathf.Max(.1f,bounds.size.y);
        bounds=BoundsOf(go.transform);go.transform.position+=Vector3.up*(Ground(p)-bounds.min.y-(asset.StartsWith("Rocks")?.35f:.03f));
        foreach(var collider in go.GetComponentsInChildren<Collider>(true))Object.DestroyImmediate(collider);
        if(solid){var capsule=go.AddComponent<CapsuleCollider>();capsule.height=height*.55f;capsule.radius=.65f;capsule.center=go.transform.InverseTransformPoint(new Vector3(p.x,Ground(p)+height*.275f,p.z));capsule.height/=Mathf.Max(.01f,go.transform.lossyScale.y);capsule.radius/=Mathf.Max(.01f,go.transform.lossyScale.x);}
        Record(go.transform);
    }
    static GameObject Prop(string kind,Transform parent,string name,Vector3 p,float yaw,float scale,bool solid=false,bool ground=true)
    {
        if(ground&&!DecorSafe(p,kind=="fence"?3:1.7f))return null;
        var go=TownDressingGeometry.Create(kind,parent,villagePalette);Undo.RegisterCreatedObjectUndo(go,"Dress volcanic village");go.name=name;
        if(ground)p.y=Ground(p)+.035f;
        go.transform.SetPositionAndRotation(p,Quaternion.Euler(0,yaw,0));go.transform.localScale=Vector3.one*scale;
        if(solid){var box=go.AddComponent<BoxCollider>();var bounds=go.GetComponent<MeshFilter>().sharedMesh.bounds;box.center=bounds.center;box.size=bounds.size;placed.Add(BoundsOf(go.transform));}
        return go;
    }
    static void Lantern(Transform parent,string name,Vector3 p,float yaw)
    {
        var lamp=Prop("lantern",parent,name,p,yaw,2.5f);if(!lamp)return;
        PointLight(lamp.transform,new Vector3(0,2.1f,0),21,4.5f);
    }
    static void PointLight(Transform parent,Vector3 local,float range,float intensity)
    {
        var t=Group("Warm amber practical light",parent);t.localPosition=local;
        var light=t.gameObject.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.64f,.29f);light.intensity=intensity;light.range=range;light.shadows=LightShadows.None;
    }
    static bool DecorSafe(Vector3 p,float radius)
    {
        if(roadNavigation&&roadNavigation.lakeRoad!=null)for(int i=1;i<roadNavigation.lakeRoad.Length;i++)
            if(roadNavigation.lakeRoad[i-1]&&roadNavigation.lakeRoad[i]&&SegmentDistance(p,roadNavigation.lakeRoad[i-1].position,roadNavigation.lakeRoad[i].position)<8.5f+radius)return false;
        foreach(var house in villageHouses)
        {
            var bounds=BoundsOf(house.transform);bounds.Expand(new Vector3(radius*1.5f,0,radius*1.5f));
            if(p.x>bounds.min.x&&p.x<bounds.max.x&&p.z>bounds.min.z&&p.z<bounds.max.z)return false;
        }
        foreach(var bounds in placed)if(new Vector2(p.x-bounds.center.x,p.z-bounds.center.z).magnitude<radius+Mathf.Min(bounds.extents.x,bounds.extents.z))return false;
        foreach(var r in Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None))if(r.houseExit&&EvacuationResident.FlatDistance(p,r.houseExit.position)<4+radius)return false;
        return true;
    }
    static float SegmentDistance(Vector3 p,Vector3 a,Vector3 b)
    {
        Vector2 delta=new Vector2(b.x-a.x,b.z-a.z),offset=new Vector2(p.x-a.x,p.z-a.z);
        return (offset-delta*Mathf.Clamp01(Vector2.Dot(offset,delta)/Mathf.Max(.001f,delta.sqrMagnitude))).magnitude;
    }
    static void Path(Transform parent,string name,Vector3[] route,float width,bool gravel=false)
    {
        if(route.Length<2)return;var b=new VillageMesh();Vector3 origin=route[0];origin.y=0;
        for(int i=1;i<route.Length;i++)
        {
            Vector3 a=route[i-1],c=route[i];Vector3 direction=c-a;direction.y=0;float length=direction.magnitude;if(length<.1f)continue;
            Vector3 right=Vector3.Cross(Vector3.up,direction.normalized);int steps=Mathf.CeilToInt(length/2f);
            for(int j=0;j<steps;j++)
            {
                Vector3 start=Vector3.Lerp(a,c,j/(float)steps),end=Vector3.Lerp(a,c,(j+1)/(float)steps);
                Vector3 v0=start-right*width*.5f,v1=start+right*width*.5f,v2=end-right*width*.5f,v3=end+right*width*.5f;
                v0.y=Ground(v0)+.045f;v1.y=Ground(v1)+.045f;v2.y=Ground(v2)+.045f;v3.y=Ground(v3)+.045f;
                b.Quad(v0-origin,v2-origin,v1-origin,v3-origin,0);
                if(!gravel&&j%2==0)
                    foreach(int side in new[]{-1,1}){Vector3 edge=Vector3.Lerp(start,end,.5f)+right*(width*.5f+.2f)*side;edge.y=Ground(edge)+.09f;b.Box(edge-origin,new Vector3(.42f,.15f,1.2f),Quaternion.LookRotation(direction),1);}
            }
        }
        var trail=AssetDatabase.LoadAssetAtPath<Material>("Assets/GameJam/TownDressing/Materials/Worn footpath.mat");
        MeshObject(parent,name,b,new[]{trail,villagePalette.stone},origin);
    }
    static void GroundOldPaths(Transform dressing)
    {
        foreach(var filter in dressing.GetComponentsInChildren<MeshFilter>(true))
        {
            if(filter.transform.IsChildOf(village)||!filter.sharedMesh||!filter.sharedMesh.isReadable)continue;
            if(!filter.name.Contains("footpath")&&!filter.name.Contains("trail"))continue;
            var vertices=filter.sharedMesh.vertices;bool moved=false;
            for(int i=0;i<vertices.Length;i++){Vector3 p=filter.transform.TransformPoint(vertices[i]);float y=Ground(p)+.055f;if(Mathf.Abs(y-p.y)>.08f)moved=true;p.y=y;vertices[i]=filter.transform.InverseTransformPoint(p);}
            if(!moved)continue;
            var mesh=Object.Instantiate(filter.sharedMesh);mesh.name=filter.name+" grounded";mesh.vertices=vertices;mesh.RecalculateNormals();mesh.RecalculateBounds();
            string path=PolishAssets+"/Grounded-"+filter.GetInstanceID()+".asset";AssetDatabase.CreateAsset(mesh,path);filter.sharedMesh=mesh;Record(filter);
            authoring.Add("Grounded floating path: "+filter.name);
        }
    }
    static void MeshObject(Transform parent,string name,VillageMesh builder,Material[] materials,Vector3 p,bool collider=false,bool local=false)
    {
        var go=Group(name,parent);if(local)go.localPosition=p;else go.position=p;
        var mesh=builder.Finish(name);string path=PolishAssets+"/"+name.Replace(' ','_')+"-"+MeshSerial++ +".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);Object.DestroyImmediate(mesh);mesh=existing;Record(mesh);}else AssetDatabase.CreateAsset(mesh,path);
        go.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials.Take(mesh.subMeshCount).ToArray();renderer.shadowCastingMode=ShadowCastingMode.On;
        GameObjectUtility.SetStaticEditorFlags(go.gameObject,StaticEditorFlags.BatchingStatic);
        if(collider){var c=go.gameObject.AddComponent<MeshCollider>();c.sharedMesh=mesh;}
    }
    static int MeshSerial;
    static float Next(float min,float max)=>(float)(min+(max-min)*variation.NextDouble());
    static void EnsurePolishFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;int index=path.LastIndexOf('/');EnsurePolishFolder(path.Substring(0,index));AssetDatabase.CreateFolder(path.Substring(0,index),path.Substring(index+1));
    }
    sealed class VillageMesh
    {
        readonly List<Vector3> vertices=new List<Vector3>();readonly List<Vector2> uv=new List<Vector2>();readonly List<List<int>> triangles=new List<List<int>>();
        public void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,int material)
        {
            while(triangles.Count<=material)triangles.Add(new List<int>());int n=vertices.Count;vertices.AddRange(new[]{a,b,c,d});
            float w=Vector3.Distance(a,c)*.3f,h=Vector3.Distance(a,b)*.3f;uv.AddRange(new[]{Vector2.zero,new Vector2(0,h),new Vector2(w,0),new Vector2(w,h)});
            triangles[material].AddRange(new[]{n,n+1,n+2,n+2,n+1,n+3});
        }
        public void Box(Vector3 center,Vector3 size,Quaternion rotation,int material)
        {
            Vector3 P(float x,float y,float z)=>center+rotation*Vector3.Scale(new Vector3(x,y,z),size*.5f);
            Quad(P(-1,1,-1),P(-1,1,1),P(1,1,-1),P(1,1,1),material);
            Quad(P(-1,-1,1),P(-1,-1,-1),P(1,-1,1),P(1,-1,-1),material);
            Quad(P(-1,-1,-1),P(-1,1,-1),P(1,-1,-1),P(1,1,-1),material);
            Quad(P(1,-1,1),P(1,1,1),P(-1,-1,1),P(-1,1,1),material);
            Quad(P(-1,-1,1),P(-1,1,1),P(-1,-1,-1),P(-1,1,-1),material);
            Quad(P(1,-1,-1),P(1,1,-1),P(1,-1,1),P(1,1,1),material);
        }
        public Mesh Finish(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=vertices.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.subMeshCount=triangles.Count;
            for(int i=0;i<triangles.Count;i++)mesh.SetTriangles(triangles[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
