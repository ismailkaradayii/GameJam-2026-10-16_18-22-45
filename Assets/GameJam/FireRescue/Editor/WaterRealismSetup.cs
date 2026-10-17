using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using GameJam.FireRescue;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class WaterRealismSetup
{
    public const string Folder = "Assets/GameJam/FireRescue/Resources/WaterRealism";
    static double nextPoll;
    static WaterRealismSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string path = "Logs/WaterRealism.command";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "assets") BuildAssets();
            else if (command == "audit") Audit();
            else if (command == "checks") WaterRealismChecks.Begin();
            else if (command == "practice") UpgradePracticePool();
            else if (command == "validate") ValidateScenes();
            else if (command == "stop") EditorApplication.ExitPlaymode();
            else throw new InvalidOperationException("Unknown water command " + command);
            File.WriteAllText("Logs/WaterRealism-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/WaterRealism-result.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("GameJam/Water/Build water materials")]
    public static void BuildAssets()
    {
        Directory.CreateDirectory(Folder);
        var drop = Texture("DropletMask", false); var mist = Texture("MistMask", true);
        ParticleMaterial("Droplet", drop, .88f, .95f);
        ParticleMaterial("Mist", mist, .55f, .08f);
        var shader = Shader.Find("GameJam/Water/Pressure Water");
        if (!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Pressure shader failed to compile");
        var jet = Material("Jet", shader); jet.SetColor("_BaseColor", new Color(.78f, .87f, .9f)); jet.SetFloat("_Opacity", .84f); jet.SetFloat("_Flow", 1f);
        var wave = Material("Ripple", shader); wave.SetColor("_BaseColor", new Color(.23f, .43f, .46f)); wave.SetFloat("_Opacity", .52f); wave.SetFloat("_Flow", 0f);
        var surface = Material("PoolSurface", Shader.Find("GameJam/Water/Pool Surface")); surface.SetColor("_DeepColor", new Color(.055f, .24f, .28f)); EditorUtility.SetDirty(surface);
        EditorUtility.SetDirty(jet); EditorUtility.SetDirty(wave); AssetDatabase.SaveAssets();
        UpgradeJumpAnimator();
    }

    static void UpgradeJumpAnimator()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Itfaiye/Animation/ItfaiyePistol.controller");
        foreach (var transition in controller.layers[0].stateMachine.anyStateTransitions)
        {
            var conditions = new System.Collections.Generic.List<AnimatorCondition>(transition.conditions);
            bool jump = conditions.Exists(c => c.parameter == "Jump");
            bool holster = conditions.Exists(c => c.parameter == "Equipped" && c.mode == AnimatorConditionMode.IfNot);
            if (jump) conditions.RemoveAll(c => c.parameter == "Equipped");
            if (holster && !conditions.Exists(c => c.parameter == "Grounded"))
                conditions.Add(new AnimatorCondition { parameter = "Grounded", mode = AnimatorConditionMode.If });
            transition.conditions = conditions.ToArray(); EditorUtility.SetDirty(transition);
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
    }

    static Texture2D Texture(string name, bool mist)
    {
        string path = Folder + "/" + name + ".png";
        var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
        {
            float u = (x + .5f) / 64f - 1, v = (y + .5f) / 64f - 1;
            float r = Mathf.Sqrt(u * u + v * v);
            float noise = Mathf.PerlinNoise(x * .065f + 4.2f, y * .065f + 13.1f);
            float alpha = mist ? Mathf.Pow(Mathf.Clamp01(1f - r), 1.7f) * Mathf.Lerp(.35f, 1, noise)
                : Mathf.SmoothStep(0, 1, Mathf.Clamp01((1f - r) * 7f)) * Mathf.Lerp(.4f, 1f, Mathf.Clamp01(r + .2f));
            tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
        }
        tex.Apply(); File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Material(string name, Shader shader)
    {
        string path = Folder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
        material.shader = shader; return material;
    }

    static void ParticleMaterial(string name, Texture2D texture, float opacity, float smoothness)
    {
        var material = Material(name, Shader.Find("Universal Render Pipeline/Particles/Lit"));
        material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", new Color(.94f, .98f, 1f, opacity));
        material.SetFloat("_Surface", 1); material.SetFloat("_Blend", 0); material.SetFloat("_Cull", 0);
        material.SetFloat("_BlendModePreserveSpecular", 0); material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.SetFloat("_ZWrite", 0); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_Smoothness", smoothness); material.SetFloat("_Metallic", 0); material.SetFloat("_SoftParticlesEnabled", 1);
        material.SetVector("_SoftParticleFadeParams", new Vector4(0, 5, 0, 0));
        material.SetFloat("_SoftParticlesNearFadeDistance", 0); material.SetFloat("_SoftParticlesFarFadeDistance", .2f);
        material.SetFloat("_CameraFadingEnabled", 1); material.SetVector("_CameraFadeParams", new Vector4(.05f, 5, 0, 0));
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.EnableKeyword("_SOFTPARTICLES_ON"); material.EnableKeyword("_FADING_ON");
        material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3010; EditorUtility.SetDirty(material);
    }

    [MenuItem("GameJam/Water/Audit water scenes")]
    public static void Audit()
    {
        var text = new StringBuilder(); text.AppendLine("active=" + SceneManager.GetActiveScene().path + " play=" + EditorApplication.isPlaying);
        foreach (var path in new[] { "Assets/Scenes/İtfaiye aracı.unity", "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity" })
        {
            var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            text.AppendLine("SCENE " + path);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var water in root.GetComponentsInChildren<WaterSourceVolume>(true))
                {
                    text.AppendLine("WATER " + water.name + " " + EditorJsonUtility.ToJson(water));
                    if (water.waterSurface) text.AppendLine("SURFACE " + water.waterSurface.name + " " + water.waterSurface.position + " colliders=" + water.waterSurface.GetComponents<Collider>().Length);
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                        if (c.bounds.Intersects(water.volume.bounds)) text.AppendLine("INTERSECT " + c.name + " " + c.GetType().Name + " trigger=" + c.isTrigger + " " + c.bounds);
                }
                foreach (var p in root.GetComponentsInChildren<FireRescuePlayer>(true)) text.AppendLine("PLAYER " + p.name + " firstPerson=" + p.firstPerson + " " + EditorJsonUtility.ToJson(p));
            }
            if (opened) EditorSceneManager.CloseScene(scene, true);
        }
        File.WriteAllText("Logs/WaterRealism-audit.txt", text.ToString());
    }

    [MenuItem("GameJam/Water/Upgrade practice swimming pool")]
    public static void UpgradePracticePool()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring");
        string path = "Assets/Scenes/İtfaiye aracı.unity";
        var scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        var roots = scene.GetRootGameObjects();
        WaterSourceVolume water = null; FireRescuePlayer original = null; BoxCollider ground = null;
        foreach (var root in roots)
        {
            if (!water) water = root.GetComponentInChildren<WaterSourceVolume>(true);
            if (!original) original = root.GetComponentInChildren<FireRescuePlayer>();
            foreach (var c in root.GetComponentsInChildren<BoxCollider>(true)) if (c.name == "Driving Ground") ground = c;
        }
        if (!water || !original || !ground) throw new InvalidOperationException("Practice pool, ground or player missing");
        var existing = water.transform.parent.Find("Swimming pool geometry");
        if (!existing)
        {
            var geometry = new GameObject("Swimming pool geometry"); geometry.transform.SetParent(water.transform.parent, false);
            Bounds g = ground.bounds, w = water.volume.bounds;
            Material groundMaterial = ground.GetComponent<Renderer>().sharedMaterial;
            // The original solid slab crossed the entire pool at ankle depth. Four slabs leave the exact pool opening.
            Slab(geometry.transform, "West deck", new Vector3((g.min.x + w.min.x) * .5f, g.center.y, g.center.z), new Vector3(w.min.x - g.min.x, g.size.y, g.size.z), groundMaterial);
            Slab(geometry.transform, "East deck", new Vector3((g.max.x + w.max.x) * .5f, g.center.y, g.center.z), new Vector3(g.max.x - w.max.x, g.size.y, g.size.z), groundMaterial);
            Slab(geometry.transform, "North deck", new Vector3(w.center.x, g.center.y, (w.max.z + g.max.z) * .5f), new Vector3(w.size.x, g.size.y, g.max.z - w.max.z), groundMaterial);
            Slab(geometry.transform, "South deck", new Vector3(w.center.x, g.center.y, (g.min.z + w.min.z) * .5f), new Vector3(w.size.x, g.size.y, w.min.z - g.min.z), groundMaterial);
            var tile = Material("PoolTile", Shader.Find("Universal Render Pipeline/Lit")); tile.color = new Color(.24f, .43f, .46f); tile.SetFloat("_Smoothness", .55f); EditorUtility.SetDirty(tile);
            Slab(geometry.transform, "Pool floor", new Vector3(w.center.x, w.min.y - .15f, w.center.z), new Vector3(w.size.x, .3f, w.size.z), tile);
            Slab(geometry.transform, "West pool wall", new Vector3(w.min.x - .15f, w.center.y, w.center.z), new Vector3(.3f, w.size.y, w.size.z), tile);
            Slab(geometry.transform, "East pool wall", new Vector3(w.max.x + .15f, w.center.y, w.center.z), new Vector3(.3f, w.size.y, w.size.z), tile);
            Slab(geometry.transform, "North pool wall", new Vector3(w.center.x, w.center.y, w.max.z + .15f), new Vector3(w.size.x, w.size.y, .3f), tile);
            Slab(geometry.transform, "South pool wall", new Vector3(w.center.x, w.center.y, w.min.z - .15f), new Vector3(w.size.x, w.size.y, .3f), tile);
            ground.enabled = false; ground.GetComponent<Renderer>().enabled = false;
        }
        if (!original.firstPerson)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab");
            var character = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); character.name = "Firefighter with swimming hands";
            character.transform.SetParent(original.transform.parent, true); character.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            var fp = character.GetComponent<Itfaiye.ItfaiyeFirstPersonController>();
            var replacement = character.AddComponent<FireRescuePlayer>(); EditorUtility.CopySerialized(original, replacement);
            replacement.firstPerson = fp; replacement.playerBody = character.GetComponent<CharacterController>();
            replacement.viewCamera = fp.playerCamera; replacement.pistol = fp.pistol;
            var replacements = new System.Collections.Generic.Dictionary<Object, Object> { { original, replacement }, { original.playerBody, replacement.playerBody }, { original.viewCamera, replacement.viewCamera } };
            foreach (var component in original.GetComponents<MonoBehaviour>())
            {
                if (!component || component == original) continue;
                var copy = character.GetComponent(component.GetType()) ?? character.AddComponent(component.GetType());
                EditorUtility.CopySerialized(component, copy); replacements[component] = copy;
            }
            fp.externalControl = true; fp.recoverFromFall = false; fp.walkSpeed = original.walkSpeed; fp.runSpeed = original.sprintSpeed;
            fp.pistol.externalControl = true; fp.pistol.showHud = true;
            var hand = new GameObject("Hose Hand").transform; hand.SetParent(fp.playerCamera.transform, false); hand.localPosition = new Vector3(.28f, -.28f, .65f); replacement.hand = hand;
            if (replacement.cannon) { replacement.cannon.aimCamera = fp.playerCamera; EditorUtility.SetDirty(replacement.cannon); PrefabUtility.RecordPrefabInstancePropertyModifications(replacement.cannon); }
            foreach (var root in scene.GetRootGameObjects()) foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!behaviour || behaviour == original) continue;
                var serialized = new SerializedObject(behaviour); var property = serialized.GetIterator(); bool changed = false;
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.objectReferenceValue && replacements.TryGetValue(property.objectReferenceValue, out var target))
                    { property.objectReferenceValue = target; changed = true; }
                }
                if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            if (original.viewCamera) { original.viewCamera.gameObject.SetActive(false); }
            original.gameObject.SetActive(false);
            var data = fp.playerCamera.GetUniversalAdditionalCameraData(); data.requiresDepthTexture = true; data.requiresColorTexture = true; data.renderPostProcessing = true;
            EditorUtility.SetDirty(fp); EditorUtility.SetDirty(fp.pistol); EditorUtility.SetDirty(data);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fp); PrefabUtility.RecordPrefabInstancePropertyModifications(fp.pistol); PrefabUtility.RecordPrefabInstancePropertyModifications(data);
        }
        if (water.waterSurface)
        {
            var renderer = water.waterSurface.GetComponent<Renderer>();
            if (renderer) { renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/PoolSurface.mat"); EditorUtility.SetDirty(renderer); }
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        if (opened) EditorSceneManager.CloseScene(scene, true);
    }

    static void Slab(Transform parent, string name, Vector3 position, Vector3 size, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
        go.transform.position = position; go.transform.localScale = size; go.GetComponent<Renderer>().sharedMaterial = material;
    }

    [MenuItem("GameJam/Water/Validate saved water scenes")]
    public static void ValidateScenes()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Validate saved scenes outside Play Mode");
        var report = new WaterRealismChecks.Report();
        foreach (var path in new[] { "Assets/Scenes/İtfaiye aracı.unity", "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity" })
        {
            Scene scene = SceneManager.GetSceneByPath(path); bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var players = new System.Collections.Generic.List<FireRescuePlayer>();
                WaterSourceVolume water = null;
                foreach (var root in scene.GetRootGameObjects())
                { players.AddRange(root.GetComponentsInChildren<FireRescuePlayer>()); if (!water) water = root.GetComponentInChildren<WaterSourceVolume>(); }
                string name = System.IO.Path.GetFileNameWithoutExtension(path);
                CheckScene(report, name + ": one active unified player", players.Count == 1 && players[0].firstPerson && players[0].pistol);
                if (players.Count == 1)
                {
                    var player = players[0];
                    CheckScene(report, name + ": both hands and movement bound", player.firstPerson && player.firstPerson.handsCamera && player.firstPerson.handsAnimator && player.playerBody == player.firstPerson.GetComponent<CharacterController>());
                    CheckScene(report, name + ": monitor uses the active camera", player.cannon && player.cannon.muzzle && player.cannon.aimCamera == player.viewCamera);
                    CheckScene(report, name + ": external movement and gun have one input owner", player.firstPerson.externalControl && player.pistol.externalControl);
                }
                CheckScene(report, name + ": valid water volume and surface", water && water.volume && water.volume.isTrigger && water.waterSurface);
                if (water && name != "Volcano_Demo")
                {
                    Physics.SyncTransforms();
                    var center = water.volume.bounds.center; center.y = water.SurfaceY + .15f;
                    bool solid = false; RaycastHit hit = default;
                    foreach (var candidate in Physics.RaycastAll(center, Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        if (candidate.collider.gameObject.scene == scene && (!solid || candidate.distance < hit.distance)) { solid = true; hit = candidate; }
                    CheckScene(report, name + ": pool has a physical floor more than 2m below water", solid && hit.point.y < water.SurfaceY - 2f, solid ? hit.collider.name + " depth=" + (water.SurfaceY - hit.point.y) : "no floor");
                    CheckScene(report, name + ": animated lit water material saved", water.waterSurface.GetComponent<Renderer>().sharedMaterial.shader.name == "GameJam/Water/Pool Surface");
                }
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        foreach (var material in new[] { "Jet", "Droplet", "Mist", "Ripple", "PoolSurface" })
        {
            var m = Resources.Load<Material>("WaterRealism/" + material);
            CheckScene(report, material + ": build-referenced URP shader compiles", m && m.shader && !ShaderUtil.ShaderHasError(m.shader));
        }
        report.status = report.checks.TrueForAll(c => c.passed) ? "passed" : "failed";
        File.WriteAllText("Logs/WaterRealism-scenes.json", JsonUtility.ToJson(report, true));
    }
    static void CheckScene(WaterRealismChecks.Report report, string name, bool passed, string evidence = "") =>
        report.checks.Add(new WaterRealismChecks.Check { name = name, passed = passed, evidence = evidence });
}
