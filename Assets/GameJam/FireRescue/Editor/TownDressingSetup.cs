using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Explicit, additive scene dressing. Audit never authors or saves the scene.</summary>
public static class TownDressingSetup
{
    public const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    public const string RootName = "Town Dressing";
    const string AuditPath = "Logs/TownDressingSpatialAudit.json";
    [Serializable] public sealed class Item
    {
        public string name, hierarchy, id, prefab;
        public Vector3 position, euler, scale;
        public Bounds meshBounds, colliderBounds;
        public bool hasMesh, hasCollider;
    }
    [Serializable] public sealed class House
    {
        public Item root;
        public Bounds ignitionBounds;
        public List<Item> doors = new List<Item>();
        public List<Item> nearbyFurniture = new List<Item>();
    }
    [Serializable] public sealed class SpatialAudit
    {
        public string scene, utc;
        public List<House> houses = new List<House>();
        public List<Item> water = new List<Item>(), lava = new List<Item>(), shelter = new List<Item>();
        public List<Vector3> road = new List<Vector3>();
        public List<Item> namedDoors = new List<Item>();
    }
    [Serializable] sealed class RoadData { public List<Vector3> roadCentre; }
    [MenuItem("GameJam/Town Dressing/1 - Audit current scene (read only)")]
    public static void Audit()
    {
        RequireScene();
        var report = new SpatialAudit { scene = SceneManager.GetActiveScene().path, utc = DateTime.UtcNow.ToString("O") };
        var transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var building in Object.FindObjectsByType<BurnableBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderBy(b => b.transform.position.x))
        {
            var house = new House { root = Describe(building.transform), ignitionBounds = building.ignitionBounds ? building.ignitionBounds.bounds : default };
            foreach (Transform child in building.GetComponentsInChildren<Transform>(true))
                if (child.name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0) house.doors.Add(Describe(child));
            foreach (var t in transforms)
                if (Vector3.Distance(t.position, building.transform.position) < 35f &&
                    new[] { "table", "cabinet", "bed", "chair", "stool" }.Any(n => t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) && t.GetComponent<MeshRenderer>())
                    house.nearbyFurniture.Add(Describe(t));
            report.houses.Add(house);
        }
        foreach (var source in Object.FindObjectsByType<WaterSourceVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None)) report.water.Add(Describe(source.transform));
        foreach (var source in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)) report.lava.Add(Describe(source.transform));
        foreach (Transform t in transforms)
        {
            if (t.name == "opendoor") report.namedDoors.Add(Describe(t));
            if (t.name.IndexOf("Shelter Entrance", StringComparison.OrdinalIgnoreCase) >= 0) report.shelter.Add(Describe(t));
        }
        const string roadFile = "Logs/VolcanoVehiclePolishSetup.json";
        if (File.Exists(roadFile)) report.road = JsonUtility.FromJson<RoadData>(File.ReadAllText(roadFile)).roadCentre;
        Directory.CreateDirectory("Logs"); File.WriteAllText(AuditPath, JsonUtility.ToJson(report, true));
        Debug.Log("TOWN_DRESSING_SPATIAL_AUDIT: " + report.houses.Count + " houses, " + report.namedDoors.Count + " named doors; " + AuditPath);
    }
    static void RequireScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Town dressing authoring requires Edit Mode.");
        if (SceneManager.GetActiveScene().path == ScenePath) return;
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the current scene before opening Volcano_Demo.");
        EditorSceneManager.OpenScene(ScenePath);
    }
    static Item Describe(Transform t)
    {
        var result = new Item { name = t.name, hierarchy = Hierarchy(t), id = GlobalObjectId.GetGlobalObjectIdSlow(t.gameObject).ToString(),
            prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject), position = t.position, euler = t.eulerAngles, scale = t.lossyScale };
        foreach (MeshRenderer renderer in t.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!result.hasMesh) result.meshBounds = renderer.bounds;
            else result.meshBounds.Encapsulate(renderer.bounds);
            result.hasMesh = true;
        }
        foreach (Collider collider in t.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger || !collider.enabled) continue;
            if (!result.hasCollider) result.colliderBounds = collider.bounds;
            else result.colliderBounds.Encapsulate(collider.bounds);
            result.hasCollider = true;
        }
        return result;
    }
    static string Hierarchy(Transform t)
    {
        string result = t.name;
        for (Transform parent = t.parent; parent; parent = parent.parent) result = parent.name + "/" + result;
        return result;
    }

    const string AssetRoot = "Assets/GameJam/TownDressing";
    [Serializable] public sealed class AuthoringReport
    {
        public string status, utc;
        public bool allFiveYardsHaveAtLeastSixGroups;
        public int createdGroups, reusedGroups, newLights, newShadowLights, colliders, pathMeshes;
        public List<string> placements = new List<string>(), skipped = new List<string>(), areaCounts = new List<string>();
        public List<string> rejectionCounts=new List<string>(), rejectedCandidates=new List<string>();
    }
    sealed class LavaFootprint { public Bounds bounds; public Vector3[] vertices; public int[] triangles; }
    static AuthoringReport authored;
    static TownDressingGeometry.Palette palette;
    static Transform dressing;
    static BurnableBuilding[] buildings;
    static WaterSourceVolume[] waterSources;
    static List<Vector3> road;
    static readonly List<Bounds> occupied = new List<Bounds>();
    static readonly List<LavaFootprint> lavaFootprints = new List<LavaFootprint>();
    static readonly List<Vector3> entrances = new List<Vector3>();
    static readonly Collider[] collisionBuffer = new Collider[128];
    static readonly Vector3 TownCentre = new Vector3(79.18f, 0f, -600.96f);
    static Material pathMaterial;
    static string safeFailure;
    static readonly SortedDictionary<string,int> rejectionCounts=new SortedDictionary<string,int>();

    [MenuItem("GameJam/Town Dressing/2 - Add warm lived-in town")]
    public static void Run()
    {
        RequireScene();
        if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save current house/door edits before adding town dressing.");
        if (!File.Exists(AuditPath)) throw new InvalidOperationException("Run the read-only spatial audit before authoring.");
        buildings = Object.FindObjectsByType<BurnableBuilding>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .OrderBy(b => b.name == "House_Green_Prefab" ? "0" : b.name).ToArray();
        if (buildings.Length != 5) throw new InvalidOperationException("Expected the five existing rescue houses.");
        authored = new AuthoringReport();
        rejectionCounts.Clear();
        EnsureFolder(AssetRoot + "/Materials"); EnsureFolder(AssetRoot + "/Meshes");
        palette = MakePalette();
        pathMaterial = MaterialAsset("Worn footpath", new Color(.53f,.47f,.38f), SurfaceTexture("Soil"), 0f);
        RefinePathMaterial();
        var existing = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        dressing = existing ? existing.transform : NewGroup(RootName, null);
        occupied.Clear(); entrances.Clear(); lavaFootprints.Clear();
        foreach (Transform area in dressing) foreach (Transform group in area)
            if (group.GetComponent<MeshFilter>() && AssetDatabase.GetAssetPath(group.GetComponent<MeshFilter>().sharedMesh).EndsWith("-v1.asset")) occupied.Add(Describe(group).meshBounds);
        waterSources = Object.FindObjectsByType<WaterSourceVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        road = File.Exists("Logs/VolcanoVehiclePolishSetup.json") ? JsonUtility.FromJson<RoadData>(File.ReadAllText("Logs/VolcanoVehiclePolishSetup.json")).roadCentre : new List<Vector3>();
        CacheLava();
        foreach (var house in buildings) entrances.Add(Entrance(house).position);
        for (int i = 0; i < buildings.Length; i++) DressYard(buildings[i], i);
        DressCentre(); DressLake(); DressShelter(); DressFirstInterior(buildings[0]); DressEdges();
        foreach(var light in dressing.GetComponentsInChildren<Light>(true))light.shadows=LightShadows.None;
        authored.allFiveYardsHaveAtLeastSixGroups=true;
        foreach(Transform area in dressing)
        {
            int count=area.Cast<Transform>().Count(t=>t.GetComponent<MeshFilter>()&&AssetDatabase.GetAssetPath(t.GetComponent<MeshFilter>().sharedMesh).EndsWith("-v1.asset"));
            authored.areaCounts.Add(area.name+": "+count+" authored prop groups");
            if(area.name.Length>=2&&int.TryParse(area.name.Substring(0,2),out int index)&&index<=5&&count<6)authored.allFiveYardsHaveAtLeastSixGroups=false;
        }
        authored.rejectionCounts=rejectionCounts.Select(pair=>pair.Key+": "+pair.Value).ToList();
        authored.status = "complete"; authored.utc = DateTime.UtcNow.ToString("O");
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene()); AssetDatabase.SaveAssets();
        File.WriteAllText("Logs/TownDressingAuthoring.json", JsonUtility.ToJson(authored, true));
        Debug.Log("TOWN_DRESSING_COMPLETE: " + authored.createdGroups + " new groups, " + authored.reusedGroups + " reused; " + authored.skipped.Count + " skipped placements.");
    }
    static Transform NewGroup(string name, Transform parent)
    {
        var go = new GameObject(name); Undo.RegisterCreatedObjectUndo(go, "Add town dressing");
        go.transform.SetParent(parent, false); return go.transform;
    }
    static Transform Area(string name)
    {
        Transform area = dressing.Find(name); return area ? area : NewGroup(name, dressing);
    }
    static Transform Entrance(BurnableBuilding house)
    {
        var all = house.GetComponentsInChildren<Transform>(true);
        Transform named = all.FirstOrDefault(t => t.name == "opendoor");
        if (named) return named;
        Transform front = all.FirstOrDefault(t => t.name == "Door04_pr");
        if (front) return front;
        var candidates = all.Where(t => t.name.StartsWith("Door0", StringComparison.OrdinalIgnoreCase) && t.GetComponent<MeshFilter>() && t.GetComponent<MeshRenderer>()).ToArray();
        return candidates.Length > 0 ? candidates.OrderBy(t => Horizontal(t.position, TownCentre)).First() : house.transform;
    }
    static Vector3 Outward(BurnableBuilding house, Transform door)
    {
        Vector3 delta = door.position - Describe(house.transform).meshBounds.center; delta.y = 0f;
        if (delta.sqrMagnitude < 1f) delta = TownCentre - house.transform.position;
        return delta.normalized;
    }
    static void DressYard(BurnableBuilding house, int index)
    {
        string[] themes = { "01 - Porch and gathering", "02 - Wood store", "03 - Repair yard", "04 - Laundry garden", "05 - Evacuation supplies" };
        string[][] kinds = {
            new[] { "bench", "planter", "crate", "bucket", "fence", "lantern", "supplies", "planter" },
            new[] { "logs", "crate", "fence", "bench", "bucket", "lantern", "logs", "planter" },
            new[] { "workbench", "crate", "logs", "bucket", "fence", "lantern", "supplies", "planter" },
            new[] { "laundry", "bucket", "planter", "bench", "fence", "lantern", "crate", "planter" },
            new[] { "supplies", "crate", "barrel", "bench", "fence", "lantern", "bucket", "planter" }
        };
        Vector2[] slots = { new Vector2(-5,5), new Vector2(4,4), new Vector2(-8,9), new Vector2(7,8),
            new Vector2(-10,4), new Vector2(9,3), new Vector2(-5,12), new Vector2(7,13) };
        Transform area = Area(themes[index]); Transform door = Entrance(house);
        Vector3 forward = Outward(house, door), right = Vector3.Cross(Vector3.up, forward);
        Quaternion facing = Quaternion.LookRotation(-forward);
        for (int j = 0; j < kinds[index].Length; j++)
        {
            Vector3 desired = door.position + right * slots[j].x + forward * slots[j].y;
            GameObject prop = Place(kinds[index][j], area, (j + 1).ToString("00") + " " + kinds[index][j], desired, facing, true, false, 1.5f);
            if (prop && kinds[index][j] == "lantern" && index == 0) AddWarmLight(prop.transform, new Vector3(0,2f,0), 9f, .85f, false);
        }
        // Fine-grained footsteps remain visual meshes; no terrain edits or new road colliders.
        AddFootpath(area, "Doorstep trail", door.position + forward * 1.2f, door.position + forward * 14f, 1.7f);
        AddSteppingStones(area, door.position + forward * 2f, forward, 5);
        AddWindowGlow(house, area);
        AddHouseNumber(house,area,index+1);
        if(index==0)AddEntryMat(area,door.position+forward*4.8f,Quaternion.LookRotation(forward));
    }
    static void DressCentre()
    {
        Transform area=Area("06 - Village meeting place");
        var board=Place("noticeboard",area,"Village notice board",new Vector3(69,0,-589),Quaternion.Euler(0,155,0),true,false,1.6f);
        if(board) AddLabel(board.transform,"VILLAGE\nMEETING POINT",new Vector3(0,1.7f,-.14f),.052f);
        Place("bench",area,"West meeting bench",new Vector3(64,0,-592),Quaternion.Euler(0,110,0),true,false,1.6f);
        Place("bench",area,"East meeting bench",new Vector3(76,0,-590),Quaternion.Euler(0,235,0),true,false,1.6f);
        Place("planter",area,"Meeting place greenery",new Vector3(73,0,-586),Quaternion.identity,true,false,1.7f);
        Place("crate",area,"Shared produce crate",new Vector3(66,0,-588),Quaternion.Euler(0,18,0),true);
        Place("lantern",area,"Meeting lantern",new Vector3(71,0,-587),Quaternion.identity,true,false,1.5f);
        foreach(var house in buildings)
        {
            Transform door=Entrance(house); Vector3 start=door.position+Outward(house,door)*4f;
            Vector3 end=board?board.transform.position:TownCentre;
            AddFootpath(area,"Village trail - "+house.name,start,end,1.65f);
        }
    }
    static void DressLake()
    {
        Transform area=Area("07 - Lake water station");
        // This bank lies north of the tested hose intake; the intake itself and
        // the broad truck approach are independently protected by Safe().
        var platform=Place("platform",area,"Dry bank loading deck",new Vector3(356,0,-604),Quaternion.Euler(0,90,0),false,false,1.15f,LakeShoulderCandidates(new Vector3(356,0,-604)));
        Vector3 anchor=platform?platform.transform.position:new Vector3(356,0,-604);
        var board=Place("noticeboard",area,"Water collection sign",anchor+new Vector3(3,0,5),Quaternion.Euler(0,90,0),false,false,1.25f,LakeShoulderCandidates(anchor+new Vector3(3,0,5)));
        if(board)
        {
            var renderer=board.GetComponent<MeshRenderer>();Material[] materials=renderer.sharedMaterials;
            Material blue=MaterialAsset("Faded water station blue",new Color(.24f,.39f,.46f),SurfaceTexture("Wood"),0);
            materials[0]=blue;materials[3]=blue;renderer.sharedMaterials=materials;
            AddLabel(board.transform,"WATER\nCOLLECTION POINT",new Vector3(0,1.7f,-.14f),.048f);
        }
        Place("bucket",area,"Intake buckets",anchor+new Vector3(3,0,1),Quaternion.Euler(0,12,0),true,false,1.5f);
        Place("barrel",area,"Water station barrel",anchor+new Vector3(3,0,4),Quaternion.identity,true,false,1.3f);
        Place("bench",area,"Bank resting bench",anchor+new Vector3(2,0,8),Quaternion.Euler(0,90,0),true,false,1.5f);
        Place("planter",area,"Bank greenery north",anchor+new Vector3(-1,0,8),Quaternion.identity,true,false,1.9f);
        Place("planter",area,"Bank greenery south",anchor+new Vector3(4,0,-3),Quaternion.identity,true,false,1.6f);
        Place("lantern",area,"Water station lantern",anchor+new Vector3(5,0,4),Quaternion.identity,true,false,1.5f);
        AddFootpath(area,"Water station footpath",anchor+new Vector3(4,0,3),new Vector3(345,0,-602),1.7f);
    }
    static Vector3[] LakeShoulderCandidates(Vector3 preferred)
    {
        var candidates=new List<Vector3>{preferred};
        for(float x=352;x<=366;x+=2)for(float z=-618;z<=-596;z+=2)candidates.Add(new Vector3(x,0,z));
        return candidates.OrderBy(p=>Horizontal(p,preferred)).ToArray();
    }
    static void DressShelter()
    {
        Transform area=Area("08 - Shelter approach");
        Transform sign=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(t=>t.name=="Shelter Entrance Sign & Lamp");
        Vector3 entrance=sign?sign.position:new Vector3(-106.3f,25.8f,-565);
        Place("bench",area,"Shelter waiting bench",entrance+new Vector3(7,0,5),Quaternion.Euler(0,250,0),true,false,1.6f);
        Place("supplies",area,"Shelter emergency supplies",entrance+new Vector3(7,0,-5),Quaternion.Euler(0,25,0),true,false,1.6f);
        Place("barrel",area,"Shelter reserve water",entrance+new Vector3(11,0,-5),Quaternion.identity,true,false,1.4f);
        Place("lantern",area,"Shelter approach lantern",entrance+new Vector3(11,0,4),Quaternion.identity,true,false,1.5f);
        var board=Place("noticeboard",area,"Shelter direction sign",entrance+new Vector3(14,0,3),Quaternion.Euler(0,270,0),true,false,1.35f);
        if(board)AddLabel(board.transform,"SHELTER\nSAFE ASSEMBLY",new Vector3(0,1.7f,-.14f),.052f);
        MoveShelterPropAside(area,"Shelter waiting bench",new Vector3(-98.3f,0,-550),Quaternion.Euler(0,250,0));
        MoveShelterPropAside(area,"Shelter direction sign",new Vector3(-91.3f,0,-552),Quaternion.Euler(0,270,0));
        AddFootpath(area,"Shelter approach trail",entrance+new Vector3(3,0,0),entrance+new Vector3(18,0,0),1.9f);
    }
    static void MoveShelterPropAside(Transform area,string name,Vector3 desired,Quaternion rotation)
    {
        Transform prop=area.Find(name);if(!prop||Mathf.Abs(prop.position.z+561.44f)>=7f)return;
        Bounds previousBounds=Describe(prop).meshBounds;Vector3 oldPosition=prop.position;Quaternion oldRotation=prop.rotation;
        occupied.RemoveAll(b=>Vector3.Distance(b.center,previousBounds.center)<.02f&&Vector3.Distance(b.size,previousBounds.size)<.02f);
        foreach(Vector3 offset in new[]{Vector3.zero,new Vector3(3,0,0),new Vector3(-3,0,0),new Vector3(0,0,3),new Vector3(3,0,3),new Vector3(6,0,0)})
        {
            Vector3 point=desired+offset;point.y=Ground(point)+.045f;prop.SetPositionAndRotation(point,rotation);
            Bounds bounds=Describe(prop).meshBounds;if(!Safe(bounds,name.Contains("bench")?"bench":"noticeboard"))continue;
            occupied.Add(bounds);authored.placements.Add("Relocated "+name+" off shelter entry axis to "+point);return;
        }
        prop.SetPositionAndRotation(oldPosition,oldRotation);occupied.Add(previousBounds);authored.skipped.Add(name+": side relocation rejected; "+safeFailure);
    }
    static void DressFirstInterior(BurnableBuilding house)
    {
        Transform area=Area("09 - First house interior accents");
        // These positions sit on the audited user furniture, not replacements.
        // No mesh, collider, material or transform on that furniture is changed.
        Place("teacup",area,"Table tea cup",new Vector3(28.2f,3.615f,-594.3f),Quaternion.Euler(0,20,0),false,true,1.8f);
        Place("teacup",area,"Second table tea cup",new Vector3(30.1f,3.615f,-595.4f),Quaternion.Euler(0,150,0),false,true,1.8f);
        Place("book",area,"Table journal",new Vector3(29.5f,3.615f,-594.0f),Quaternion.Euler(0,-20,0),false,true,2f);
        Place("book",area,"Cabinet keepsake book",new Vector3(29.5f,3.78f,-610.4f),Quaternion.Euler(0,16,0),false,true,1.8f);
        Place("planter",area,"Cabinet small plant",new Vector3(30.4f,3.78f,-611.1f),Quaternion.identity,false,true,.65f);
        Place("rug",area,"Woven bedside rug",new Vector3(28,1.013f,-601),Quaternion.Euler(0,-26.36f,0),false,true,2.2f);
        AddWarmLight(area,new Vector3(27,6,-600),13f,.9f,false);
    }
    static void DressEdges()
    {
        Transform area=Area("10 - Low village planting");
        string[] names={"Grass1","Grass2","Deadbush_1","Deathbells_1"};
        string[] assets=AssetDatabase.FindAssets("t:Prefab",new[]{"Assets/BK/PureNature_Volcano/Prefabs"}).Select(AssetDatabase.GUIDToAssetPath).Where(p=>names.Contains(Path.GetFileNameWithoutExtension(p))).ToArray();
        if(assets.Length==0){authored.skipped.Add("Existing village planting prefabs were unavailable");return;}
        for(int h=0;h<buildings.Length;h++)
        {
            Transform door=Entrance(buildings[h]);Vector3 outward=Outward(buildings[h],door),side=Vector3.Cross(Vector3.up,outward);
            for(int j=0;j<4;j++)
            {
                string name="House "+(h+1)+" edge planting "+(j+1);if(area.Find(name)){authored.reusedGroups++;continue;}
                Vector3 p=door.position+outward*(8+j*2)+side*(j%2==0?-10:10);p.y=Ground(p);
                var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(assets[(h+j)%assets.Length]);
                var instance=(GameObject)PrefabUtility.InstantiatePrefab(prefab,area);instance.name=name;
                instance.transform.SetPositionAndRotation(p,Quaternion.Euler(0,h*37+j*67,0));instance.transform.localScale*=.8f;
                Bounds bounds=Describe(instance.transform).meshBounds;
                if(bounds.size.y>3.5f||!Safe(bounds,"planter")){Object.DestroyImmediate(instance);authored.skipped.Add(name+": protected ground");continue;}
                foreach(var c in instance.GetComponentsInChildren<Collider>())Object.DestroyImmediate(c);
                occupied.Add(bounds);authored.createdGroups++;authored.placements.Add(name+" @ "+p);
            }
        }
    }
    static void AddWarmLight(Transform parent,Vector3 localPosition,float range,float intensity,bool shadows)
    {
        if(parent.Find("Warm practical light"))return;
        var go=NewGroup("Warm practical light",parent);go.localPosition=localPosition;
        var light=go.gameObject.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.67f,.32f);light.range=range;light.intensity=intensity;
        light.shadows=shadows&&authored.newShadowLights<2?LightShadows.Soft:LightShadows.None;
        authored.newLights++;if(light.shadows!=LightShadows.None)authored.newShadowLights++;
    }
    static void AddLabel(Transform parent,string text,Vector3 localPosition,float size)
    {
        Transform label=parent.Find("Lettering");if(!label)label=NewGroup("Lettering",parent);label.localPosition=localPosition;
        var mesh=label.GetComponent<TextMesh>();var previousRenderer=label.GetComponent<MeshRenderer>();
        bool alreadyFitted=mesh&&mesh.text==text&&mesh.fontSize==64&&previousRenderer&&previousRenderer.sharedMaterial&&previousRenderer.sharedMaterial.shader.name=="GameJam/WorldLettering";
        if(!mesh)mesh=label.gameObject.AddComponent<TextMesh>();
        if(!alreadyFitted)
        {
            mesh.text=text;mesh.fontSize=64;mesh.characterSize=size;mesh.anchor=TextAnchor.MiddleCenter;mesh.alignment=TextAlignment.Center;
            mesh.color=new Color(.85f,.79f,.64f);mesh.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            mesh.font.RequestCharactersInTexture("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ",64,FontStyle.Normal);
        }
        const string path=AssetRoot+"/Materials/Depth tested world lettering.mat";Material material=AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader=Shader.Find("GameJam/WorldLettering");if(!shader)throw new InvalidOperationException("WorldLettering shader must compile before updating town labels.");
        if(!material){material=new Material(shader){name="Depth tested world lettering"};AssetDatabase.CreateAsset(material,path);}
        // Dynamic font atlases belong in the renderer's property block, not in
        // a persisted material whose dependency hash would change on rebuild.
        if(material.mainTexture){material.mainTexture=null;EditorUtility.SetDirty(material);}
        if(material.GetColor("_Color")!=Color.white){material.SetColor("_Color",Color.white);EditorUtility.SetDirty(material);}
        var renderer=label.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        if(!alreadyFitted)
        {
            Vector3 dimensions=renderer.localBounds.size;float maxWidth=parent.name=="House number"?.48f:1.38f,maxHeight=parent.name=="House number"?.34f:.68f;
            float fit=Mathf.Min(1f,Mathf.Min(maxWidth/Mathf.Max(.001f,dimensions.x),maxHeight/Mathf.Max(.001f,dimensions.y)));mesh.characterSize*=fit;
        }
        var atlas=label.GetComponent<WorldLetteringFontAtlas>();if(!atlas)atlas=label.gameObject.AddComponent<WorldLetteringFontAtlas>();atlas.RefreshAtlas();
    }
    static void AddWindowGlow(BurnableBuilding house,Transform area)
    {
        var source=house.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m=>m.name=="Windows01Glass"&&m.sharedMesh);
        if(!source){authored.skipped.Add(house.name+": separate window glass mesh unavailable");return;}
        var go=area.Find("Warm occupied windows");bool created=!go;if(!go)go=NewGroup("Warm occupied windows",area);
        go.SetPositionAndRotation(source.transform.position,source.transform.rotation);go.localScale=source.transform.lossyScale*1.0005f;
        var filter=go.GetComponent<MeshFilter>();if(!filter)filter=go.gameObject.AddComponent<MeshFilter>();filter.sharedMesh=source.sharedMesh;
        var renderer=go.GetComponent<MeshRenderer>();if(!renderer)renderer=go.gameObject.AddComponent<MeshRenderer>();
        Material warm=MaterialAsset("Soft warm window light",new Color(.24f,.15f,.06f,.18f),null,0);
        // Preserve views through the user's glass. This adds a faint warm tint,
        // never an opaque replacement window or a depth-writing interior wall.
        warm.SetColor("_BaseColor",new Color(.48f,.25f,.07f,.65f));warm.SetFloat("_Surface",1);warm.SetFloat("_Blend",0);warm.SetFloat("_ZWrite",0);warm.SetFloat("_Cull",2);
        warm.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);warm.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        warm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");warm.DisableKeyword("_ALPHAPREMULTIPLY_ON");warm.SetOverrideTag("RenderType","Transparent");warm.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent;
        warm.EnableKeyword("_EMISSION");warm.SetColor("_EmissionColor",new Color(.65f,.27f,.055f));
        warm.SetFloat("_BlendModePreserveSpecular",0);warm.SetFloat("_QueueOffset",0);warm.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
        BaseShaderGUI.SetMaterialKeywords(warm);EditorUtility.SetDirty(warm);
        renderer.sharedMaterials=Enumerable.Repeat(warm,source.sharedMesh.subMeshCount).ToArray();
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        if(created)authored.createdGroups++;authored.placements.Add(house.name+" additive outward warm glass overlay; existing windows untouched");
    }
    static void AddHouseNumber(BurnableBuilding house,Transform area,int number)
    {
        Transform existing=area.Find("House number");if(existing){AddLabel(existing,number.ToString("00"),new Vector3(0,0,-.045f),.095f);return;}
        Transform door=Entrance(house);var filter=door.GetComponentsInChildren<MeshFilter>(true).Where(f=>f.sharedMesh).OrderByDescending(f=>f.sharedMesh.bounds.size.sqrMagnitude).FirstOrDefault();
        Vector3 normal=Outward(house,door);Bounds bounds=Describe(door).meshBounds;
        if(filter)
        {
            Vector3 local=filter.sharedMesh.bounds.extents.x<filter.sharedMesh.bounds.extents.z?Vector3.right:Vector3.forward;
            Vector3 exact=filter.transform.TransformDirection(local);exact.y=0;exact.Normalize();if(Vector3.Dot(exact,normal)<0)exact=-exact;normal=exact;
        }
        Vector3 side=Vector3.Cross(Vector3.up,normal);
        float halfWidth=Mathf.Abs(side.x)*bounds.extents.x+Mathf.Abs(side.z)*bounds.extents.z;
        Vector3 position=bounds.center+side*(halfWidth+.65f);position.y=Ground(position)+2.4f;
        var wall=Physics.RaycastAll(position+normal*4, -normal,8,~0,QueryTriggerInteraction.Ignore)
            .Where(h=>h.collider.transform.IsChildOf(house.transform)).OrderBy(h=>h.distance).FirstOrDefault();
        if(wall.collider){position=wall.point+wall.normal*.065f;normal=wall.normal;}
        else position+=normal*.15f;
        Transform root=NewGroup("House number",area);root.SetPositionAndRotation(position,Quaternion.LookRotation(-normal));root.localScale=Vector3.one*1.4f;
        root.gameObject.AddComponent<MeshFilter>().sharedMesh=NumberPlaqueMesh();root.gameObject.AddComponent<MeshRenderer>().sharedMaterial=palette.accent;
        AddLabel(root,number.ToString("00"),new Vector3(0,0,-.045f),.095f);
        authored.createdGroups++;authored.placements.Add(house.name+" number "+number+" @ "+position);
    }
    static Mesh NumberPlaqueMesh()
    {
        const string path=AssetRoot+"/Meshes/house-number-plaque.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(mesh)return mesh;
        Vector2[] outline={new Vector2(-.31f,-.26f),new Vector2(.31f,-.26f),new Vector2(.35f,-.22f),new Vector2(.35f,.22f),new Vector2(.31f,.26f),new Vector2(-.31f,.26f),new Vector2(-.35f,.22f),new Vector2(-.35f,-.22f)};
        var vertices=new List<Vector3>();var triangles=new List<int>();var uv=new List<Vector2>();
        for(int ring=0;ring<3;ring++)foreach(Vector2 point in outline){Vector2 p=point*(ring==0?.9f:1);vertices.Add(new Vector3(p.x,p.y,ring==0?-.035f:ring==1?-.012f:.025f));uv.Add(new Vector2(p.x+.5f,p.y+.5f));}
        for(int i=0;i<8;i++){int next=(i+1)%8;for(int ring=0;ring<2;ring++){int a=ring*8+i,b=ring*8+next,c=(ring+1)*8+i,d=(ring+1)*8+next;triangles.AddRange(new[]{a,b,c,b,d,c});}}
        for(int i=1;i<7;i++)triangles.AddRange(new[]{0,i+1,i});
        mesh=new Mesh{name="Beveled house number plaque"};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,path);return mesh;
    }
    static void AddEntryMat(Transform area,Vector3 position,Quaternion rotation)
    {
        if(area.Find("Front door woven mat"))return;position.y=Ground(position)+.045f;
        if(!PathPointSafe(position)){authored.skipped.Add("First-house entry mat: obstructed threshold");return;}
        var mat=TownDressingGeometry.Create("rug",area,palette);mat.name="Front door woven mat";mat.transform.SetPositionAndRotation(position,rotation);mat.transform.localScale=new Vector3(2.1f,1,1.7f);
        // The mat is a thin visual on the walking path, with no collider and no door-swing obstruction.
        authored.createdGroups++;authored.placements.Add("First-house entry mat @ "+position);
    }
    static bool PathPointSafe(Vector3 p)
    {
        foreach(var water in waterSources)if(water.ContainsIntake(new Vector3(p.x,water.SurfaceY-.12f,p.z)))return false;
        foreach(var lava in lavaFootprints)
        {
            if(!OverlapXZ(new Bounds(p,new Vector3(2,1,2)),lava.bounds))continue;
            if(lava.vertices==null)return false;
            for(int i=0;i<lava.triangles.Length;i+=3)if(TriangleDistance(XZ(p),XZ(lava.vertices[lava.triangles[i]]),XZ(lava.vertices[lava.triangles[i+1]]),XZ(lava.vertices[lava.triangles[i+2]]))<1.3f)return false;
        }
        int count=Physics.OverlapSphereNonAlloc(p+Vector3.up*.2f,.18f,collisionBuffer,~0,QueryTriggerInteraction.Ignore);
        for(int i=0;i<count;i++)if(!(collisionBuffer[i]is TerrainCollider)&&!collisionBuffer[i].transform.IsChildOf(dressing))return false;
        return count<collisionBuffer.Length;
    }
    static void AddFootpath(Transform area,string name,Vector3 start,Vector3 end,float width)
    {
        if(area.Find(name))return;
        float distance=Horizontal(start,end);if(distance<1)return;
        int steps=Mathf.Clamp(Mathf.CeilToInt(distance/.8f),2,180);
        Vector3 direction=end-start;direction.y=0;direction.Normalize();Vector3 side=Vector3.Cross(Vector3.up,direction);
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int i=0;i<steps;i++)
        {
            Vector3 a=Vector3.Lerp(start,end,(float)i/steps),b=Vector3.Lerp(start,end,(float)(i+1)/steps);
            a.y=Ground(a)+.035f;b.y=Ground(b)+.035f;
            if(!PathPointSafe(a)||!PathPointSafe(b)||Mathf.Abs(a.y-b.y)>.7f)continue;
            float wa=width*(.42f+.09f*Mathf.PerlinNoise(i*.3f,2)),wb=width*(.42f+.09f*Mathf.PerlinNoise((i+1)*.3f,2));
            Vector3[] quad={a-side*wa,a+side*wa,b-side*wb,b+side*wb};
            if(quad.Any(v=>!PathPointSafe(new Vector3(v.x,Ground(v)+.035f,v.z))))continue;
            int n=vertices.Count;foreach(var v in quad)vertices.Add(new Vector3(v.x,Ground(v)+.035f,v.z));
            uv.Add(new Vector2(0,i*.28f));uv.Add(new Vector2(1,i*.28f));uv.Add(new Vector2(0,(i+1)*.28f));uv.Add(new Vector2(1,(i+1)*.28f));
            triangles.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});
        }
        if(vertices.Count==0){authored.skipped.Add(area.name+"/"+name+": no unprotected path surface");return;}
        string meshPath=AssetRoot+"/Meshes/path-"+area.name.Substring(0,2)+"-"+name.Replace(' ','_')+".asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if(!mesh){mesh=new Mesh{name=name};mesh.SetVertices(vertices);mesh.SetUVs(0,uv);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();AssetDatabase.CreateAsset(mesh,meshPath);}
        var root=NewGroup(name,area);root.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=root.gameObject.AddComponent<MeshRenderer>();renderer.sharedMaterial=pathMaterial;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;authored.pathMeshes++;
    }
    static void AddSteppingStones(Transform area,Vector3 start,Vector3 forward,int count)
    {
        if(area.Find("Doorstep stones"))return;Transform root=NewGroup("Doorstep stones",area);
        for(int i=0;i<count;i++)
        {
            Vector3 p=start+forward*i*1.3f;p.y=Ground(p)+.04f;if(!PathPointSafe(p))continue;
            var stone=GameObject.CreatePrimitive(PrimitiveType.Cylinder);stone.name="Worn step "+(i+1);Object.DestroyImmediate(stone.GetComponent<Collider>());
            stone.transform.SetParent(root,false);stone.transform.SetPositionAndRotation(p,Quaternion.Euler(0,31*i,0));stone.transform.localScale=new Vector3(.95f,.035f,.66f);
            stone.GetComponent<Renderer>().sharedMaterial=palette.stone;stone.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
    static GameObject Place(string kind, Transform area, string name, Vector3 desired, Quaternion rotation, bool seek, bool interior = false, float scale = 1.3f, Vector3[] candidates=null)
    {
        Transform existing = area.Find(name);
        if (existing) { authored.reusedGroups++; return existing.gameObject; }
        GameObject prop = TownDressingGeometry.Create(kind, area, palette); prop.name = name;
        prop.transform.localScale = Vector3.one * scale;
        Vector3[] offsets = seek ? new[] { Vector3.zero, Vector3.right*3f, Vector3.left*3f, Vector3.forward*3f, Vector3.back*3f,
            new Vector3(4,0,4), new Vector3(-4,0,4), new Vector3(4,0,-4), new Vector3(-4,0,-4), Vector3.right*7f, Vector3.left*7f, Vector3.forward*7f, Vector3.back*7f,
            new Vector3(8,0,-8), new Vector3(-8,0,-8), new Vector3(12,0,-5), new Vector3(-12,0,-5), Vector3.back*12f, Vector3.back*17f } : new[] { Vector3.zero };
        foreach (Vector3 candidate in candidates??offsets.Select(offset=>desired+rotation*offset).ToArray())
        {
            Vector3 p = candidate;
            if (!interior) p.y = Ground(p) + .045f;
            prop.transform.SetPositionAndRotation(p, rotation);
            Bounds bounds = Describe(prop.transform).meshBounds;
            if (!interior && !Safe(bounds, kind))
            {authored.rejectedCandidates.Add(area.name+"/"+name+" @ "+p+": "+safeFailure);continue;}
            if (interior && occupied.Any(b => b.Intersects(bounds)))
            {safeFailure="overlap with previously authored interior prop";authored.rejectedCandidates.Add(area.name+"/"+name+" @ "+p+": "+safeFailure);continue;}
            if(!interior)FitSupportFeet(prop,kind);
            bounds=Describe(prop.transform).meshBounds;
            occupied.Add(bounds); authored.createdGroups++; authored.placements.Add(area.name + "/" + name + " @ " + p);
            foreach (Renderer renderer in prop.GetComponentsInChildren<Renderer>()) GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,StaticEditorFlags.BatchingStatic);
            if (!interior && new[] { "bench", "crate", "logs", "workbench", "supplies", "barrel", "platform" }.Contains(kind)) AddSimpleCollider(prop, bounds);
            return prop;
        }
        authored.skipped.Add(area.name + "/" + name + ": all candidates rejected; last reason: "+safeFailure); Object.DestroyImmediate(prop); return null;
    }
    static void FitSupportFeet(GameObject prop,string kind)
    {
        Vector2 half;
        switch(kind)
        {
            case "bench":half=new Vector2(.72f,.26f);break;
            case "workbench":half=new Vector2(.86f,.32f);break;
            case "platform":half=new Vector2(1.85f,1.15f);break;
            case "crate":half=new Vector2(.3f,.3f);break;
            case "logs":half=new Vector2(.6f,.45f);break;
            default:return;
        }
        var feet=new List<Vector3>();float top=float.NegativeInfinity;
        for(int x=-1;x<=1;x+=2)for(int z=-1;z<=1;z+=2)
        {Vector3 p=prop.transform.TransformPoint(new Vector3(x*half.x,0,z*half.y));p.y=Ground(p);feet.Add(p);top=Mathf.Max(top,p.y);}
        Vector3 position=prop.transform.position;position.y=top+.035f;prop.transform.position=position;
        foreach(Vector3 foot in feet)
        {
            float height=top-foot.y+.035f;if(height<.075f)continue;
            var pad=GameObject.CreatePrimitive(PrimitiveType.Cylinder);pad.name="Small stone footing";Object.DestroyImmediate(pad.GetComponent<Collider>());
            pad.transform.SetParent(prop.transform,false);pad.transform.position=new Vector3(foot.x,foot.y+height*.5f,foot.z);pad.transform.rotation=Quaternion.Euler(0,23,0);
            float scale=prop.transform.lossyScale.x;pad.transform.localScale=new Vector3(.32f/scale,height*.5f/scale,.32f/scale);pad.GetComponent<Renderer>().sharedMaterial=palette.stone;
        }
    }
    static bool Safe(Bounds bounds, string kind)
    {
        safeFailure="";
        float radius = new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
        // Roof/world AABBs cover open yards of the rotated houses; use the actual
        // solid colliders below, not those oversized boxes, to preserve their walls.
        foreach (Vector3 entry in entrances) if (Horizontal(bounds.center, entry) < radius + 2.4f) return Reject("entrance clearance",entry.ToString());
        if (occupied.Any(b => { b.Expand(.45f); return b.Intersects(bounds); })) return Reject("existing dressing overlap",bounds.center.ToString());
        for (int i = 0; i < road.Count; i++)
        {
            Vector3 direction = road[Mathf.Min(i + 1, road.Count - 1)] - road[Mathf.Max(0, i - 1)]; direction.y = 0f; direction.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, direction), delta = bounds.center - road[i];
            float across = Mathf.Abs(right.x)*bounds.extents.x + Mathf.Abs(right.z)*bounds.extents.z;
            float along = Mathf.Abs(direction.x)*bounds.extents.x + Mathf.Abs(direction.z)*bounds.extents.z;
            if (Mathf.Abs(Vector3.Dot(delta,right)) < 3.2f+across && Mathf.Abs(Vector3.Dot(delta,direction)) < 7.6f+along) return Reject("full truck road sweep","route point "+i+" "+road[i]);
        }
        foreach (var lava in lavaFootprints)
        {
            Bounds expanded = lava.bounds; expanded.Expand(radius*2f+4f);
            if (!OverlapXZ(bounds, expanded)) continue;
            if (lava.vertices == null) return Reject("unreadable lava conservative bounds",lava.bounds.ToString());
            for (int i = 0; i < lava.triangles.Length; i += 3)
                if (TriangleDistance(XZ(bounds.center), XZ(lava.vertices[lava.triangles[i]]), XZ(lava.vertices[lava.triangles[i+1]]), XZ(lava.vertices[lava.triangles[i+2]])) < radius+1.5f) return Reject("actual lava mesh footprint",lava.bounds.center.ToString());
        }
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        for (int x=-1;x<=1;x++) for (int z=-1;z<=1;z++)
        {
            Vector3 p = bounds.center + new Vector3(x*bounds.extents.x,0,z*bounds.extents.z);
            float ground = Ground(p); minimum=Mathf.Min(minimum,ground); maximum=Mathf.Max(maximum,ground);
            foreach (var water in waterSources) if (water.ContainsIntake(new Vector3(p.x,water.SurfaceY-.12f,p.z))) return Reject("actual lake water",water.name+" at "+p);
        }
        if (maximum-minimum > (kind == "planter" ? 1.1f : .7f)) return Reject("uneven terrain","height span "+(maximum-minimum).ToString("F3")+" m");
        // Preserve the tested intake and the low firing lanes leading from the truck approach into town.
        if (Horizontal(bounds.center,new Vector3(334.05f,0,-605.8f)) < radius+3.5f) return Reject("tested water intake clearance",bounds.center.ToString());
        if (bounds.size.y > 1.25f)
            foreach (var house in buildings)
                if (PointSegmentDistance(XZ(bounds.center),new Vector2(120,-623),XZ(house.transform.position)) < radius+1f && Horizontal(bounds.center,house.transform.position) < 25f) return Reject("house firing lane",house.name);
        int count = Physics.OverlapBoxNonAlloc(bounds.center,bounds.extents*.92f,collisionBuffer,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
        if (count == collisionBuffer.Length) return Reject("overlap query saturated",bounds.center.ToString());
        for (int i=0;i<count;i++)
        {
            Collider c=collisionBuffer[i];
            if (c is TerrainCollider || c.transform.IsChildOf(dressing)) continue;
            if (c is CharacterController) continue;
            return Reject("existing solid collider",Hierarchy(c.transform));
        }
        return true;
    }
    static bool Reject(string reason,string detail)
    {
        safeFailure=reason+"; "+detail;
        rejectionCounts[reason]=rejectionCounts.TryGetValue(reason,out int count)?count+1:1;
        return false;
    }
    static void AddSimpleCollider(GameObject prop, Bounds worldBounds)
    {
        var filter=prop.GetComponent<MeshFilter>();
        if(!filter||!filter.sharedMesh)throw new InvalidOperationException("Expected a combined prop mesh for collider fitting.");
        var box = prop.AddComponent<BoxCollider>();
        // Fit the combined authored mesh in its own space. Inverting a rotated
        // world AABB would enlarge it a second time and create invisible walls.
        Bounds local=filter.sharedMesh.bounds;
        box.center=local.center; box.size=local.size; authored.colliders++;
    }
    static float Ground(Vector3 p)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 local=p-terrain.transform.position,size=terrain.terrainData.size;
            if(local.x>=0 && local.z>=0 && local.x<=size.x && local.z<=size.z) return terrain.SampleHeight(p)+terrain.transform.position.y;
        }
        return p.y;
    }
    static float Horizontal(Vector3 a,Vector3 b)=>Vector2.Distance(XZ(a),XZ(b));
    static Vector2 XZ(Vector3 p)=>new Vector2(p.x,p.z);
    static bool OverlapXZ(Bounds a,Bounds b)=>a.min.x<b.max.x && a.max.x>b.min.x && a.min.z<b.max.z && a.max.z>b.min.z;
    static float PointSegmentDistance(Vector2 p,Vector2 a,Vector2 b)
    {
        Vector2 delta=b-a; return Vector2.Distance(p,a+delta*Mathf.Clamp01(Vector2.Dot(p-a,delta)/Mathf.Max(.00001f,delta.sqrMagnitude)));
    }
    static float TriangleDistance(Vector2 p,Vector2 a,Vector2 b,Vector2 c)
    {
        float Cross(Vector2 u,Vector2 v)=>u.x*v.y-u.y*v.x;
        float ab=Cross(b-a,p-a),bc=Cross(c-b,p-b),ca=Cross(a-c,p-c);
        if(Mathf.Abs(Cross(b-a,c-a))>.00001f && ((ab>=0&&bc>=0&&ca>=0)||(ab<=0&&bc<=0&&ca<=0)))return 0;
        return Mathf.Min(PointSegmentDistance(p,a,b),Mathf.Min(PointSegmentDistance(p,b,c),PointSegmentDistance(p,c,a)));
    }
    static void CacheLava()
    {
        foreach(var source in Object.FindObjectsByType<LavaIgnitionSource>(FindObjectsInactive.Include,FindObjectsSortMode.None))
        {
            MeshFilter filter=source.surfaceMesh ? source.surfaceMesh : source.GetComponent<MeshFilter>();
            if(!filter || !filter.sharedMesh)continue;
            Mesh mesh=filter.sharedMesh; var entry=new LavaFootprint();
            if(mesh.isReadable)
            {
                entry.vertices=mesh.vertices.Select(filter.transform.TransformPoint).ToArray();entry.triangles=mesh.triangles;
                if(entry.vertices.Length==0)continue;
                entry.bounds=new Bounds(entry.vertices[0],Vector3.zero);foreach(Vector3 v in entry.vertices)entry.bounds.Encapsulate(v);
            }
            else { var renderer=filter.GetComponent<Renderer>();if(!renderer)continue;entry.bounds=renderer.bounds; }
            lavaFootprints.Add(entry);
        }
    }
    static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }
    static void RefinePathMaterial()
    {
        const string texturePath=AssetRoot+"/Materials/Soft worn trail.asset";
        var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if(!texture)
        {
            texture=new Texture2D(256,256,TextureFormat.RGBA32,true){name="Soft worn trail",wrapModeU=TextureWrapMode.Clamp,wrapModeV=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
            var pixels=new Color[256*256];
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                float u=x/255f,v=y/255f,edge=Mathf.Min(u,1-u);
                float wobble=.035f+.10f*Mathf.PerlinNoise(v*7.7f,2.4f);
                float grain=Mathf.PerlinNoise(x*.16f,y*.16f);
                float alpha=Mathf.SmoothStep(0,1,Mathf.Clamp01((edge-wobble)/.20f))*(.32f+.28f*grain);
                float value=.76f+.18f*grain;pixels[y*256+x]=new Color(value,value,value,alpha);
            }
            texture.SetPixels(pixels);texture.Apply(true,false);AssetDatabase.CreateAsset(texture,texturePath);
        }
        pathMaterial.SetTexture("_BaseMap",texture);pathMaterial.SetColor("_BaseColor",new Color(.25f,.205f,.16f,.82f));
        pathMaterial.SetFloat("_Smoothness",0);pathMaterial.SetFloat("_SpecularHighlights",0);pathMaterial.SetFloat("_EnvironmentReflections",0);
        pathMaterial.SetFloat("_Surface",1);pathMaterial.SetFloat("_Blend",0);pathMaterial.SetFloat("_ZWrite",0);
        pathMaterial.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);pathMaterial.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        pathMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");pathMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");pathMaterial.SetOverrideTag("RenderType","Transparent");pathMaterial.renderQueue=(int)UnityEngine.Rendering.RenderQueue.Transparent-10;
        pathMaterial.SetFloat("_BlendModePreserveSpecular",0);pathMaterial.SetFloat("_QueueOffset",-10);
        BaseShaderGUI.SetMaterialKeywords(pathMaterial);
        EditorUtility.SetDirty(pathMaterial);
    }
    static TownDressingGeometry.Palette MakePalette()=>new TownDressingGeometry.Palette
    {
        wood=MaterialAsset("Weathered warm timber",new Color(.48f,.35f,.23f),SurfaceTexture("Wood"),0),
        darkWood=MaterialAsset("Dark endgrain",new Color(.25f,.19f,.14f),SurfaceTexture("Wood"),0),
        metal=MaterialAsset("Aged iron",new Color(.19f,.21f,.20f),SurfaceTexture("Soil"),.5f),
        accent=MaterialAsset("Muted sage paint",new Color(.33f,.40f,.32f),SurfaceTexture("Wood"),0),
        fabric=MaterialAsset("Natural linen",new Color(.77f,.70f,.57f),SurfaceTexture("Linen"),0),
        foliage=MaterialAsset("Olive foliage",new Color(.30f,.37f,.20f),SurfaceTexture("Soil"),0),
        stone=MaterialAsset("Weathered stone",new Color(.48f,.46f,.41f),SurfaceTexture("Soil"),0),
        glow=MaterialAsset("Amber lantern glass",new Color(1f,.76f,.39f),null,0,true)
    };
    static Material MaterialAsset(string name,Color color,Texture texture,float metallic,bool emission=false)
    {
        string path=AssetRoot+"/Materials/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material)return material;
        material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,enableInstancing=true};
        material.SetColor("_BaseColor",color);if(texture)material.SetTexture("_BaseMap",texture);
        material.SetFloat("_Smoothness",.18f);material.SetFloat("_Metallic",metallic);
        if(emission){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(1f,.5f,.15f)*1.5f);}
        AssetDatabase.CreateAsset(material,path);return material;
    }
    static Texture2D SurfaceTexture(string kind)
    {
        string path=AssetRoot+"/Materials/"+kind+" surface.asset";var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(texture)return texture;
        texture=new Texture2D(256,256,TextureFormat.RGB24,true){name=kind+" surface",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear};
        var colors=new Color[256*256];
        for(int y=0;y<256;y++)for(int x=0;x<256;x++)
        {
            float noise=Mathf.PerlinNoise(x*.17f+3.1f,y*.17f+7.4f),grain;
            if(kind=="Wood")grain=.64f+.19f*Mathf.PerlinNoise(x*.13f,y*.006f)+.07f*Mathf.Sin(x*.71f+Mathf.PerlinNoise(x*.03f,y*.02f)*5f)+noise*.10f;
            else if(kind=="Linen")grain=.78f+noise*.12f+((x%3==0||y%3==0)?-.06f:.03f);
            else grain=.70f+noise*.18f+Mathf.PerlinNoise(x*.028f,y*.028f)*.12f;
            colors[y*256+x]=new Color(grain,grain,grain,1);
        }
        texture.SetPixels(colors);texture.Apply(true,false);AssetDatabase.CreateAsset(texture,path);return texture;
    }
}
