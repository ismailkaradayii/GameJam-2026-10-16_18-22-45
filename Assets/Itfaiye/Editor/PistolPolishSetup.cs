using System.IO;
using System.Text;
using System.Collections.Generic;
using Itfaiye;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class PistolPolishSetup
{
    public const string Folder = "Logs/PistolPolish";
    const string Root = "Assets/Itfaiye/";
    const string PlayerPrefab = Root + "Prefabs/ItfaiyeFirstPersonPlayer.prefab";
    const string GradePath = Root + "Materials/CombatGrade.asset";
    const string ShotPath = Root + "Materials/ShotGrade.asset";

    [MenuItem("GameJam/Final Loop/Apply pistol presentation")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Stop Play Mode first.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (!scene.path.EndsWith("Volcano_Demo.unity")) throw new System.InvalidOperationException("Open Volcano_Demo first.");
        var grade = Profile(GradePath);
        var color = Component<ColorAdjustments>(grade); color.postExposure.Override(.8f); color.contrast.Override(12f); color.saturation.Override(-4f);
        var bloom = Component<Bloom>(grade); bloom.intensity.Override(.26f); bloom.threshold.Override(1.15f); bloom.scatter.Override(.55f);
        var vignette = Component<Vignette>(grade); vignette.intensity.Override(.14f); vignette.smoothness.Override(.65f);
        Component<Tonemapping>(grade).mode.Override(TonemappingMode.ACES);
        Component<MotionBlur>(grade).intensity.Override(0f);
        Component<DepthOfField>(grade).mode.Override(DepthOfFieldMode.Off);
        var shot = Profile(ShotPath);
        Component<ColorAdjustments>(shot).postExposure.Override(1.06f);
        Component<Vignette>(shot).intensity.Override(.24f);
        Component<Bloom>(shot).intensity.Override(.38f);
        var prefab = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try { Configure(prefab.GetComponent<ItfaiyePistol>(), shot); PrefabUtility.SaveAsPrefabAsset(prefab, PlayerPrefab); }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var pistol = Object.FindFirstObjectByType<ItfaiyePistol>();
        Configure(pistol, shot);
        var volume = GameObject.Find("Pistol Combat Grading");
        if (!volume) volume = new GameObject("Pistol Combat Grading");
        var v = volume.GetComponent<Volume>() ?? volume.AddComponent<Volume>();
        v.isGlobal = true; v.priority = 10f; v.weight = 1f; v.sharedProfile = grade;
        foreach (var c in new[] { pistol.player.playerCamera, pistol.player.handsCamera })
        {
            var data = c.GetUniversalAdditionalCameraData();
            if (data.renderType == CameraRenderType.Base)
            {
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.antialiasingQuality = AntialiasingQuality.High;
            }
            EditorUtility.SetDirty(data);
        }
        EditorUtility.SetDirty(grade); EditorUtility.SetDirty(shot);
        foreach (var c in grade.components) EditorUtility.SetDirty(c);
        foreach (var c in shot.components) EditorUtility.SetDirty(c);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene);
        Audit();
        File.WriteAllText(Folder + "/authoring.txt", "SAVED: player prefab + actual scene; physical reflex optic, two-hand pose, recoil, shot grading, scene post-process.");
    }

    static void Configure(ItfaiyePistol p, VolumeProfile shot)
    {
        var h = p.player.handsAnimator.GetComponent<ItfaiyeFirstPersonHands>();
        h.pistolHipPosition = new Vector3(.055f, -.155f, .38f);
        h.pistolHipEuler = new Vector3(0, 0, -2f); h.pistolAimDistance = .35f;
        h.swayAmount = 1.1f; h.aimSwayMultiplier = .06f; h.swaySmoothTime = .075f;
        h.walkBobAmount = .8f; h.runBobAmount = 1.45f; h.leftGripTightness = .45f;
        p.shotInterval = .14f; p.recoilAngle = 11.2f; p.recoilTravel = .062f; p.aimedRecoil = .42f;
        p.reloadDuration = 2.1f;
        var feedback = p.GetComponent<ItfaiyePistolFeedback>();
        feedback.cameraKick = 1.8f; feedback.aimedKickMultiplier = .45f; feedback.kickRecovery = .20f;
        feedback.cameraTravel = .012f; feedback.shotShake = .24f; feedback.shotFovKick = .65f;
        feedback.shotVolume = .8f; feedback.shotPostProfile = shot; feedback.shotPostIntensity = .65f;
        // The saved village uses the player prefab. Author the optic once there,
        // then keep that inherited instance instead of adding another scene copy.
        if (PrefabUtility.IsPartOfPrefabInstance(p))
        {
            foreach (var extra in p.weapon.GetComponentsInChildren<ItfaiyeReflexSight>(true))
                if (PrefabUtility.IsAddedGameObjectOverride(extra.gameObject)) Object.DestroyImmediate(extra.gameObject);
            var inherited = p.weapon.GetComponentsInChildren<ItfaiyeReflexSight>(true);
            if (inherited.Length != 1 || !inherited[0].lens) throw new System.InvalidOperationException("Expected exactly one inherited reflex optic, found " + inherited.Length);
            h.pistolSight = inherited[0].lens.transform;
            SaveSettings(h,p,feedback); return;
        }
        var old = p.weapon.transform.Find("Slide Assembly/Micro Reflex Sight");
        if (old) Object.DestroyImmediate(old.gameObject);
        var optic = new GameObject("Micro Reflex Sight"); optic.transform.SetParent(p.slide, false);
        optic.transform.localPosition = new Vector3(0, 1.50f, -.95f);
        var body = Lit("ReflexGraphite", new Color(.065f,.075f,.085f), .75f, .42f);
        var steel = Lit("ReflexSteel", new Color(.24f,.26f,.28f), .85f, .55f);
        var rubber = Lit("ReflexRubber", new Color(.018f,.021f,.026f), .05f, .25f);
        Box("Mounting shoe", optic.transform, new Vector3(0,-.015f,.1f), new Vector3(.88f,.1f,.85f), body);
        Box("Battery housing", optic.transform, new Vector3(0,.065f,.18f), new Vector3(.76f,.12f,.72f), body);
        var frame = new GameObject("Chamfered aluminium hood"); frame.transform.SetParent(optic.transform, false); frame.transform.localPosition = new Vector3(0,.37f,-.1f);
        frame.AddComponent<MeshFilter>().sharedMesh = Housing(); frame.AddComponent<MeshRenderer>().sharedMaterial = body;
        var lens = GameObject.CreatePrimitive(PrimitiveType.Quad); lens.name = "Optical glass"; Object.DestroyImmediate(lens.GetComponent<Collider>());
        lens.transform.SetParent(frame.transform,false); lens.transform.localScale = new Vector3(.70f,.53f,1);
        var glass = Material("ReflexGlass", "Itfaiye/Reflex Glass");
        lens.GetComponent<MeshRenderer>().sharedMaterial = glass; h.pistolSight = lens.transform;
        for (int i=-1;i<=1;i+=2)
        {
            Box("Mount screw",optic.transform,new Vector3(i*.30f,.14f,.42f),new Vector3(.10f,.025f,.10f),steel);
            Box("Screw slot",optic.transform,new Vector3(i*.30f,.157f,.42f),new Vector3(.07f,.008f,.018f),rubber);
            Box(i<0?"Brightness minus":"Brightness plus",optic.transform,new Vector3(i*.41f,.21f,-.06f),new Vector3(.045f,.12f,.15f),rubber);
        }
        var sight = optic.AddComponent<ItfaiyeReflexSight>(); sight.pistol=p; sight.lens=lens.GetComponent<MeshRenderer>(); sight.dotRadius=.012f;
        foreach (var t in optic.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=LayerMask.NameToLayer("FirstPersonHands");
        foreach (var r in optic.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode=ShadowCastingMode.Off; r.receiveShadows=false; }
        SaveSettings(h,p,feedback);
    }
    static void SaveSettings(ItfaiyeFirstPersonHands h,ItfaiyePistol p,ItfaiyePistolFeedback feedback)
    {
        EditorUtility.SetDirty(h); EditorUtility.SetDirty(p); EditorUtility.SetDirty(feedback);
        if (PrefabUtility.IsPartOfPrefabInstance(p))
        { PrefabUtility.RecordPrefabInstancePropertyModifications(h); PrefabUtility.RecordPrefabInstancePropertyModifications(p); PrefabUtility.RecordPrefabInstancePropertyModifications(feedback); }
    }
    static VolumeProfile Profile(string path)
    {
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile) { profile=ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile,path); }
        return profile;
    }
    static T Component<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet<T>(out var c)) { c=p.Add<T>(false); AssetDatabase.AddObjectToAsset(c,p); }
        return c;
    }
    static Material Material(string name,string shader)
    {
        string path=Root+"Materials/"+name+".mat";
        var m=AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m=new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m,path); }
        return m;
    }
    static Material Lit(string name,Color color,float metallic,float smoothness)
    { var m=Material(name,"Universal Render Pipeline/Lit"); m.SetColor("_BaseColor",color); m.SetFloat("_Metallic",metallic); m.SetFloat("_Smoothness",smoothness); EditorUtility.SetDirty(m); return m; }
    static void Box(string name,Transform parent,Vector3 position,Vector3 scale,Material material)
    {
        var box=GameObject.CreatePrimitive(PrimitiveType.Cube); box.name=name; Object.DestroyImmediate(box.GetComponent<Collider>());
        box.transform.SetParent(parent,false); box.transform.localPosition=position; box.transform.localScale=scale; box.GetComponent<Renderer>().sharedMaterial=material;
    }
    static Mesh Housing()
    {
        string path=Root+"Models/ReflexHousing.asset";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
        var verts=new List<Vector3>(); var triangles=new List<int>();
        for (int side=0;side<2;side++) for (int inner=0;inner<2;inner++)
        {
            float w=inner==0?.41f:.35f, h=inner==0?.32f:.265f, c=.065f, z=side==0?-.12f:.12f;
            verts.AddRange(new[]{new Vector3(-w+c,-h,z),new Vector3(w-c,-h,z),new Vector3(w,-h+c,z),new Vector3(w,h-c,z),new Vector3(w-c,h,z),new Vector3(-w+c,h,z),new Vector3(-w,h-c,z),new Vector3(-w,-h+c,z)});
        }
        void Quad(int a,int b,int c,int d) { triangles.AddRange(new[]{a,b,c,a,c,d}); }
        for(int i=0;i<8;i++) { int j=(i+1)%8; Quad(i,i+8,j+8,j); Quad(i+16,j+16,j+24,i+24); Quad(i,j,j+16,i+16); Quad(i+8,i+24,j+24,j+8); }
        bool created=!mesh; if(created) mesh=new Mesh { name="Micro reflex machined housing" }; else mesh.Clear();
        // Separate face vertices retain crisp machined edges instead of smoothing the hood like a tube.
        var flat=new List<Vector3>();var indices=new List<int>();
        for(int i=0;i<triangles.Count;i++) { flat.Add(verts[triangles[i]]); indices.Add(i); }
        mesh.SetVertices(flat); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
        if(created) AssetDatabase.CreateAsset(mesh,path); else EditorUtility.SetDirty(mesh); return mesh;
    }
    [MenuItem("GameJam/Final Loop/Audit pistol presentation")]
    public static void Audit()
    {
        Directory.CreateDirectory(Folder);
        var b = new StringBuilder();
        var p = Object.FindFirstObjectByType<ItfaiyePistol>();
        b.AppendLine("PLAY=" + EditorApplication.isPlaying + " SCENE=" + p.gameObject.scene.path);
        foreach (var c in new Component[] { p, p.GetComponent<ItfaiyePistolFeedback>(), p.player, p.player.handsAnimator.GetComponent<ItfaiyeFirstPersonHands>() })
            b.AppendLine(c.GetType().Name + " " + EditorJsonUtility.ToJson(c));
        b.AppendLine("PLAYER scale=" + p.transform.lossyScale + " WEAPON=" + p.weapon.transform.localScale);
        b.AppendLine("OPTIC_COUNT=" + p.weapon.GetComponentsInChildren<ItfaiyeReflexSight>(true).Length);
        foreach (var t in p.weapon.GetComponentsInChildren<Transform>(true))
        {
            var r = t.GetComponent<Renderer>();
            b.AppendLine("GUN " + t.name + " local=" + t.localPosition + " scale=" + t.localScale + (r ? " bounds=" + r.localBounds + " material=" + r.sharedMaterial : ""));
        }
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var d = c.GetUniversalAdditionalCameraData();
            b.AppendLine("CAMERA " + c.name + " enabled=" + c.enabled + " post=" + d.renderPostProcessing + " mask=" + d.volumeLayerMask.value + " stack=" + d.renderType + " fov=" + c.fieldOfView);
        }
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            b.AppendLine("VOLUME " + v.name + " enabled=" + v.enabled + " global=" + v.isGlobal + " layer=" + v.gameObject.layer + " priority=" + v.priority + " weight=" + v.weight + " profile=" + AssetDatabase.GetAssetPath(v.sharedProfile));
        File.WriteAllText(Folder + "/audit.txt", b.ToString());
    }

    [MenuItem("GameJam/Final Loop/Capture pistol before")]
    public static void Before() => PistolPolishChecks.Begin("before");
    [MenuItem("GameJam/Final Loop/Check pistol presentation")]
    public static void After() => PistolPolishChecks.Begin("after");
}
