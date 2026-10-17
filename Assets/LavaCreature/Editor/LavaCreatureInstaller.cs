using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class LavaCreatureInstaller
{
    const string ModelPath = "Assets/LavaCreature/Models/lava_creature_animated.fbx";
    const string ControllerPath = "Assets/LavaCreature/Animations/LavaCreature.controller";
    const string PrefabPath = "Assets/LavaCreature/Prefabs/LavaCreature.prefab";
    const string SessionKey = "LavaCreatureInstaller_20261016_v17_local_frontflip";

    static LavaCreatureInstaller()
    {
        EditorApplication.delayCall += InstallOnce;
    }

    [MenuItem("Tools/Lava Creature/Rebuild Unity Assets")]
    public static void Rebuild()
    {
        SessionState.EraseBool(SessionKey);
        InstallOnce();
    }

    // Entry point used by command-line verification when the Editor is closed.
    public static void BatchRepair()
    {
        ConfigureModelImporter();
        CreateMaterials();
        CreateControllerAndPrefab();
        EnsureCreatureInEylulScene();
        AssetDatabase.SaveAssets();
        Debug.Log("LAVA_BATCH_REPAIR_COMPLETE");
    }

    static void InstallOnce()
    {
        if (SessionState.GetBool(SessionKey, false) || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (!SessionState.GetBool(SessionKey, false)) EditorApplication.delayCall += InstallOnce;
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ModelPath) == null)
        {
            EditorApplication.delayCall += InstallOnce;
            return;
        }

        SessionState.SetBool(SessionKey, true);
        ConfigureModelImporter();
        CreateMaterials();
        CreateControllerAndPrefab();
        ReplaceDirectModelInstancesInLoadedScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Lava Creature integration complete: " + PrefabPath);
    }

    static void ConfigureModelImporter()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null) return;
        importer.importAnimation = true;
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importCameras = false;
        importer.importLights = false;
        // Bake Blender/FBX axes into geometry and animation. Keeping the axis
        // correction on a transform makes Edit and Play mode disagree.
        importer.bakeAxisConversion = true;

        var source = importer.defaultClipAnimations;
        ModelImporterClipAnimation[] clips;
        if (source != null && source.Length >= 2)
        {
            clips = source;
        }
        else
        {
            clips = new[]
            {
                new ModelImporterClipAnimation { name = "Idle", takeName = "Idle", firstFrame = 1, lastFrame = 73 },
                new ModelImporterClipAnimation { name = "Jump", takeName = "Jump", firstFrame = 1, lastFrame = 20 }
            };
        }
        foreach (var clip in clips)
        {
            bool idle = clip.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0;
            bool jump = clip.name.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0;
            clip.loopTime = idle;
            clip.loopPose = idle;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
            if (jump) { clip.loopTime = false; clip.loopPose = false; }
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static Material MakeMaterial(string name, Color baseColor, Color emission, float smoothness)
    {
        string path = $"Assets/LavaCreature/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (material == null)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        else material.shader = shader;
        material.SetColor("_BaseColor", baseColor);
        material.SetColor("_Color", baseColor);
        material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    static void CreateMaterials()
    {
        MakeMaterial("MAT_Rock_Procedural", new Color(.055f,.018f,.008f,1), Color.black, .18f);
        MakeMaterial("MAT_Lava_Orange", new Color(.8f,.035f,.002f,1), new Color(5.5f,.15f,.01f,1), .2f);
        MakeMaterial("MAT_Lava_Yellow", new Color(1f,.28f,.01f,1), new Color(7f,1.2f,.03f,1), .25f);
        MakeMaterial("MAT_Eye_Core", new Color(1f,.8f,.35f,1), new Color(8f,4f,.5f,1), .3f);
        MakeMaterial("MAT_Flame_Outer", new Color(.9f,.025f,.001f,1), new Color(5f,.08f,.005f,1), .15f);
        MakeMaterial("MAT_Flame_Inner", new Color(1f,.3f,.005f,1), new Color(7f,1.1f,.02f,1), .2f);
    }

    static void CreateControllerAndPrefab()
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        var importedIdle = clips.FirstOrDefault(c => c.name.IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0);
        var importedJump = clips.FirstOrDefault(c => c.name.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0);
        if (importedIdle == null || importedJump == null)
        {
            Debug.LogError("Lava Creature: Idle or Jump clip was not found in FBX.");
            return;
        }

        // FBX axis conversion can leave a constant rotation curve on the
        // armature/root bone. Animator then applies it only in Play mode and
        // lays the whole character down. Copy clips and remove only those root
        // rotation curves; all Body_CTRL, eye and flame curves remain intact.
        var idle = CreateRuntimeSafeClip(importedIdle, "Idle", true);
        var jump = CreateRuntimeSafeClip(importedJump, "Jump", false);

        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
        var sm = controller.layers[0].stateMachine;
        var idleState = sm.AddState("Idle"); idleState.motion = idle;
        var jumpState = sm.AddState("Jump"); jumpState.motion = jump;
        sm.defaultState = idleState;
        var toJump = idleState.AddTransition(jumpState);
        toJump.hasExitTime = false; toJump.duration = .08f;
        toJump.AddCondition(AnimatorConditionMode.If, 0, "Jump");
        var toIdle = jumpState.AddTransition(idleState);
        toIdle.hasExitTime = true; toIdle.exitTime = .95f; toIdle.duration = .1f;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        // Keep gameplay scale on a wrapper that is not animated. FBX animation
        // clips can contain root scale curves and would otherwise reset 0.1 to 1.
        var instance = new GameObject("LavaCreature");
        instance.transform.localScale = Vector3.one * 0.1f;
        // Blender Z-up to this FBX's Unity orientation. This correction lives
        // outside the animated hierarchy, so animation clips cannot overwrite it.
        instance.transform.localRotation = Quaternion.identity;
        // Keep procedural flip rotation on a dedicated parent. Never rotate the
        // Animator object itself because animation evaluation can overwrite it.
        var somersaultVisual = new GameObject("SomersaultVisual");
        somersaultVisual.transform.SetParent(instance.transform, false);
        somersaultVisual.transform.localPosition = Vector3.zero;
        somersaultVisual.transform.localRotation = Quaternion.identity;
        somersaultVisual.transform.localScale = Vector3.one;

        var animatedModel = PrefabUtility.InstantiatePrefab(model) as GameObject;
        animatedModel.name = "AnimatedModel";
        animatedModel.transform.SetParent(somersaultVisual.transform, false);
        animatedModel.transform.localPosition = Vector3.zero;
        animatedModel.transform.localRotation = Quaternion.identity;
        animatedModel.transform.localScale = Vector3.one;
        var animator = animatedModel.GetComponent<Animator>() ?? animatedModel.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        // Gameplay motion belongs to the stable outer wrapper. The animated
        // FBX child remains free to animate its bones without moving the root.
        instance.AddComponent<LavaCreatureAutoJump>();

        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string clean = mats[i].name.Replace(" (Instance)", "");
                var replacement = AssetDatabase.LoadAssetAtPath<Material>($"Assets/LavaCreature/Materials/{clean}.mat");
                if (replacement != null) mats[i] = replacement;
            }
            renderer.sharedMaterials = mats;
        }

        // The refreshed FBX lies on its back at zero rotation. Use the opposite
        // axis correction to stand it upright with its face toward the camera.
        instance.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Debug.Log($"Lava Creature clips configured: Idle {idle.length:F2}s, Jump {jump.length:F2}s");
    }

    static AnimationClip CreateRuntimeSafeClip(AnimationClip source, string clipName, bool loop)
    {
        string path = $"Assets/LavaCreature/Animations/{clipName}.anim";
        AssetDatabase.DeleteAsset(path);
        var clip = UnityEngine.Object.Instantiate(source);
        clip.name = clipName;
        AssetDatabase.CreateAsset(clip, path);

        int removed = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            string p = (binding.path ?? "").Replace('\\', '/');
            bool rootPath = string.IsNullOrEmpty(p)
                || p == "LavaCreature_AnimationRig"
                || p == "Root"
                || p.EndsWith("/Root", StringComparison.Ordinal);
            // The wrapper supplies orientation and gameplay motion. Removing
            // every curve from the FBX/Root transforms prevents Blender's
            // axis-conversion keys from rotating the creature sideways during
            // Jump, while Body_CTRL/eyes/flames keep their squash animation.
            if (rootPath)
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed++;
            }
        }
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.loopBlend = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        Debug.Log($"{clipName}: removed {removed} animated root curves for stable orientation.");
        return clip;
    }

    static void OrientUpright(GameObject instance)
    {
        Quaternion[] candidates =
        {
            Quaternion.identity,
            Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(-90f, 0f, 0f),
            Quaternion.Euler(0f, 0f, 90f), Quaternion.Euler(0f, 0f, -90f)
        };
        Quaternion best = Quaternion.identity;
        float bestHeight = float.MinValue;
        foreach (var candidate in candidates)
        {
            instance.transform.rotation = candidate;
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y > bestHeight)
            {
                bestHeight = bounds.size.y;
                best = candidate;
            }
        }
        // Bounds can determine the vertical axis but cannot distinguish +90
        // from -90. This FBX's feet-to-flame direction requires +90 in Unity.
        if (best.eulerAngles.x > 180f && best.eulerAngles.x < 359.9f)
            best = Quaternion.Euler(90f, 0f, 0f);
        instance.transform.rotation = best;
        Debug.Log($"Lava Creature upright rotation selected: {best.eulerAngles}, height={bestHeight:F3}");
    }

    static void EnsureCreatureInEylulScene()
    {
        const string scenePath = "Assets/Scenes/eylul.unity";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
        {
            string source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            if (source == ModelPath || source == PrefabPath || root.name == "LavaCreature")
                UnityEngine.Object.DestroyImmediate(root);
        }
        var creature = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
        creature.name = "LavaCreature";
        creature.transform.position = Vector3.zero;
        creature.transform.rotation = prefab.transform.rotation;
        creature.transform.localScale = Vector3.one * 0.1f;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Lava Creature placed persistently in " + scenePath);
    }

    static void ReplaceDirectModelInstancesInLoadedScenes()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) return;

        bool ProcessScene(Scene scene)
        {
            bool changed = false;
            foreach (var oldRoot in scene.GetRootGameObjects().ToArray())
            {
                string sourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(oldRoot);
                if (!string.Equals(sourcePath, ModelPath, StringComparison.OrdinalIgnoreCase)) continue;

                Vector3 position = oldRoot.transform.position;
                Quaternion rotation = oldRoot.transform.rotation;
                int sibling = oldRoot.transform.GetSiblingIndex();
                var replacement = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                replacement.transform.position = position;
                replacement.transform.rotation = rotation;
                replacement.transform.localScale = Vector3.one * 0.1f;
                replacement.transform.SetSiblingIndex(sibling);
                UnityEngine.Object.DestroyImmediate(oldRoot);
                changed = true;
            }
            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Replaced direct animated FBX instance with stable-scale LavaCreature prefab in " + scene.path);
            }
            return changed;
        }

        for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
        {
            var scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) continue;
            ProcessScene(scene);
        }

        const string EylulScenePath = "Assets/Scenes/eylul.unity";
        var eylulScene = SceneManager.GetSceneByPath(EylulScenePath);
        if (!eylulScene.IsValid() || !eylulScene.isLoaded)
        {
            eylulScene = EditorSceneManager.OpenScene(EylulScenePath, OpenSceneMode.Additive);
            ProcessScene(eylulScene);
            EditorSceneManager.CloseScene(eylulScene, true);
        }
    }
}
