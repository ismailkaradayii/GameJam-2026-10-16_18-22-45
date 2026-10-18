using System;
using System.IO;
using System.Linq;
using System.Text;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ResidentIdleSetup
{
    public const string Folder = "Logs/ResidentIdleVariety";
    const string AssetsFolder = "Assets/GameJam/FireRescue/Evacuation/";
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    static readonly string[] SourceNames = { "Happy Idle", "Happy Idle (1)", "Old Man Idle", "Orc Idle" };
    static readonly string[] ClipNames = { "Happy Idle", "Happy Idle Alternate", "Old Man Idle", "Orc Idle" };
    const string RuntimePending = "ResidentIdleSetup.RuntimePending";
    static double nextPoll;
    static double runtimeAt;

    static ResidentIdleSetup()
    {
        nextPoll = EditorApplication.timeSinceStartup + 2;
        EditorApplication.update += Poll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(RuntimePending, false)) return;
            runtimeAt = EditorApplication.timeSinceStartup + 1;
            EditorApplication.update += CheckRuntime;
        };
    }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string path = "Logs/ResidentIdleVariety.command";
        if (!File.Exists(path)) return;
        string command = File.ReadAllText(path).Trim(); File.Delete(path);
        try
        {
            if (command == "apply") Apply();
            else if (command == "verify") Verify();
            else if (command == "runtime") BeginRuntime();
            else throw new InvalidOperationException("Unknown resident idle command: " + command);
            File.WriteAllText("Logs/ResidentIdleVariety-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/ResidentIdleVariety-result.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("GameJam/Final Loop/Assign varied resident idles")]
    public static void Apply()
    {
        var mission = Mission();
        Directory.CreateDirectory(Folder);
        // Capture the live scene, including any unsaved authoring, before assigning poses.
        if (!File.Exists(Folder + "/before.unity")) EditorSceneManager.SaveScene(mission.gameObject.scene, Folder + "/before.unity", true);
        var clips = new AnimationClip[5];
        clips[0] = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetsFolder + "Idle.anim");
        for (int i = 0; i < SourceNames.Length; i++) clips[i + 1] = ImportIdle(i);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetsFolder + "CivilianEvacuation.controller");
        var locomotion = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
        var movement = (BlendTree)locomotion.motion;
        if (!controller.parameters.Any(p => p.name == "IdleVariant")) controller.AddParameter("IdleVariant", AnimatorControllerParameterType.Float);
        var idle = movement.children[0].motion as BlendTree;
        if (!idle)
        {
            idle = new BlendTree { name = "Resident idle variations" };
            AssetDatabase.AddObjectToAsset(idle, controller);
        }
        idle.blendType = BlendTreeType.Simple1D; idle.blendParameter = "IdleVariant"; idle.useAutomaticThresholds = false;
        idle.children = clips.Select((clip, index) => new ChildMotion { motion = clip, threshold = index, timeScale = 1 }).ToArray();
        var children = movement.children;
        children[0].motion = idle; movement.children = children;
        // Each house/group has different poses; Kemal gets the old-man posture.
        int[] variants = { 1, 2, 4, 0, 1, 2, 3 };
        float[] phases = { .11f, .43f, .76f, .27f, .59f, .91f, .14f };
        Undo.SetCurrentGroupName("Varied resident idle poses");
        foreach (var resident in mission.residents)
        {
            int index = resident.formationIndex;
            var motion = resident.animator.GetComponent<ResidentLocomotion>();
            if (!motion) motion = Undo.AddComponent<ResidentLocomotion>(resident.animator.gameObject);
            Undo.RecordObject(motion, "Assign individual idle pose and phase");
            motion.resident = resident;
            motion.idleVariant = variants[index % variants.Length]; motion.idlePhase = phases[index % phases.Length];
            PrefabUtility.RecordPrefabInstancePropertyModifications(motion); EditorUtility.SetDirty(motion);
        }
        EditorUtility.SetDirty(idle); EditorUtility.SetDirty(movement); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(mission.gameObject.scene); EditorSceneManager.SaveScene(mission.gameObject.scene);
        File.WriteAllText(Folder + "/assignments.txt", string.Join("\n", mission.residents.OrderBy(r => r.formationIndex).Select(r =>
        {
            var motion = r.animator.GetComponent<ResidentLocomotion>();
            return r.name + ": " + clips[motion.idleVariant].name + " phase=" + motion.idlePhase;
        })));
    }

    static AnimationClip ImportIdle(int index)
    {
        string sourcePath = "Assets/Animation/Ch34_nonPBR@" + SourceNames[index] + ".fbx";
        var importer = AssetImporter.GetAtPath(sourcePath) as ModelImporter;
        if (!importer) throw new InvalidOperationException("Missing idle animation: " + sourcePath);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var imported = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        foreach (var clip in imported)
        {
            clip.loopTime = clip.loopPose = true;
            clip.lockRootRotation = clip.lockRootHeightY = clip.lockRootPositionXZ = true;
            clip.keepOriginalOrientation = clip.keepOriginalPositionXZ = clip.keepOriginalPositionY = false;
            clip.heightFromFeet = true; clip.rotationOffset = 0;
        }
        importer.clipAnimations = imported; importer.SaveAndReimport();
        var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        if (!source.humanMotion) throw new InvalidOperationException("Idle animation is not humanoid: " + sourcePath);
        var copy = Object.Instantiate(source); copy.name = ClipNames[index]; copy.wrapMode = WrapMode.Loop;
        string target = AssetsFolder + ClipNames[index] + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(target);
        if (existing) { EditorUtility.CopySerialized(copy, existing); Object.DestroyImmediate(copy); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(copy, target); return copy;
    }

    static EvacuationMission Mission()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before authoring resident idles.");
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (!mission || mission.gameObject.scene.path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
        return mission;
    }

    [MenuItem("GameJam/Final Loop/Verify varied resident idles")]
    public static void Verify()
    {
        var mission = Mission();
        Directory.CreateDirectory(Folder);
        var report = new StringBuilder();
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            var residents = mission.residents.OrderBy(r => r.formationIndex).ToArray();
            foreach (var resident in residents)
            {
                var model = Object.Instantiate(resident.animator.gameObject);
                SceneManager.MoveGameObjectToScene(model, preview);
                model.transform.SetPositionAndRotation(new Vector3(resident.formationIndex * 2.3f, 0, 0), Quaternion.identity);
                model.transform.localScale = Vector3.one;
                foreach (var lod in model.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
                foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
                var animator = model.GetComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var motion = model.GetComponent<ResidentLocomotion>(); motion.Initialize();
                var graph = PlayableGraph.Create("Resident idle preview"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    var controller = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
                    AnimationPlayableOutput.Create(graph, "Resident pose", animator).SetSourcePlayable(controller);
                    graph.Play();
                    controller.SetFloat("IdleVariant", motion.idleVariant); controller.SetFloat("StrideRate", 1);
                    controller.Play("Base Layer.Locomotion", 0, motion.idlePhase);
                    for (int frame = 0; frame < 90; frame++) { graph.Evaluate(1f / 60); motion.StabilizePreview(1f / 60); }
                    string expected = motion.idleVariant == 0 ? "Idle" : ClipNames[motion.idleVariant - 1];
                    var active = controller.GetCurrentAnimatorClipInfo(0).Where(c => c.weight > .01f).ToArray();
                    Require(active.Length == 1 && active[0].clip.name == expected, resident.name + " plays its assigned " + expected);
                    Require(active[0].clip.humanMotion && active[0].clip.isLooping, expected + " is a looping humanoid clip");
                    Require(Vector3.Distance(model.transform.position, new Vector3(resident.formationIndex * 2.3f, 0, 0)) < .001f, "Idle root stays in place");
                    report.AppendLine(resident.name + " idle=" + expected + " phase=" + motion.idlePhase + " length=" + active[0].clip.length);
                    Capture(preview, model, resident.formationIndex + "-" + expected.Replace(" ", "-") + ".png");
                    controller.SetFloat("Speed", motion.walkStrideSpeed);
                    graph.Evaluate(.3f);
                    Require(controller.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Walk" && c.weight > .95f), "Walking remains bound");
                    controller.SetFloat("Speed", motion.runStrideSpeed);
                    graph.Evaluate(.3f);
                    Require(controller.GetCurrentAnimatorClipInfo(0).Any(c => c.clip.name == "Run" && c.weight > .95f), "Running remains bound");
                    controller.SetBool("Panic", true);
                    for (int frame = 0; frame < 30; frame++) graph.Evaluate(1f / 60);
                    Require(controller.GetCurrentAnimatorStateInfo(0).IsName("Terrified"), "Alarm still enters fear");
                    controller.SetBool("Panic", false); controller.SetFloat("Speed", 0);
                    for (int frame = 0; frame < 30; frame++) graph.Evaluate(1f / 60);
                    Require(controller.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), "Fear still returns to locomotion");
                    controller.SetBool("ShelterRest", true);
                    for (int frame = 0; frame < 300; frame++) graph.Evaluate(1f / 60);
                    Require(controller.GetCurrentAnimatorStateInfo(0).IsName("Shelter Kneeling Idle"), "Shelter still reaches kneeling rest");
                    report.AppendLine("  PASS idle, loop, root, walk, run, panic, recovery, shelter");
                }
                finally { graph.Destroy(); }
                Object.DestroyImmediate(model);
            }
            Require(residents.Select(r => r.animator.GetComponent<ResidentLocomotion>().idleVariant).Distinct().Count() == 5, "All five idle poses are used");
            report.AppendLine("SUCCESS: seven residents use all five idle poses, including all four supplied clips.");
            File.WriteAllText(Folder + "/verification.txt", report.ToString());
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/verification.txt", report + "\nFAILED: " + ex); throw; }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static void Require(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
    }

    [MenuItem("GameJam/Final Loop/Check resident idle startup")]
    public static void BeginRuntime()
    {
        Mission(); Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/runtime.txt", "STARTING");
        SessionState.SetBool(RuntimePending, true); EditorApplication.EnterPlaymode();
    }

    static void CheckRuntime()
    {
        if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < runtimeAt) return;
        EditorApplication.update -= CheckRuntime;
        SessionState.SetBool(RuntimePending, false);
        var report = new StringBuilder();
        try
        {
            var mission = Object.FindFirstObjectByType<EvacuationMission>();
            foreach (var resident in mission.residents.OrderBy(r => r.formationIndex))
            {
                var animator = resident.animator; var motion = animator.GetComponent<ResidentLocomotion>();
                var state = animator.GetCurrentAnimatorStateInfo(0);
                var active = animator.GetCurrentAnimatorClipInfo(0).Where(c => c.weight > .01f).ToArray();
                string expected = motion.idleVariant == 0 ? "Idle" : ClipNames[motion.idleVariant - 1];
                Require(Mathf.Abs(animator.GetFloat("IdleVariant") - motion.idleVariant) < .001f, resident.name + " initializes its idle variant");
                Require(state.IsName("Locomotion") && active.Length == 1 && active[0].clip.name == expected, resident.name + " starts in the assigned pose");
                float phase = Mathf.Repeat(state.normalizedTime - Time.time / state.length, 1f);
                float error = Mathf.Abs(Mathf.DeltaAngle(phase * 360, motion.idlePhase * 360)) / 360;
                Require(error < .08f, resident.name + " starts at its individual phase: actual=" + phase + " expected=" + motion.idlePhase);
                report.AppendLine(resident.name + " PASS " + expected + " variant=" + motion.idleVariant + " startupPhase=" + phase);
            }
            report.AppendLine("SUCCESS: all seven saved-scene NPCs initialize their assigned idle and individual phase in Play Mode.");
        }
        catch (Exception ex) { report.AppendLine("FAILED: " + ex); }
        finally
        {
            File.WriteAllText(Folder + "/runtime.txt", report.ToString());
            EditorApplication.ExitPlaymode();
        }
    }

    static void Capture(Scene scene, GameObject model, string name)
    {
        var cameraObject = new GameObject("Idle review camera"); var lightObject = new GameObject("Idle review light");
        SceneManager.MoveGameObjectToScene(cameraObject, scene); SceneManager.MoveGameObjectToScene(lightObject, scene);
        try
        {
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .19f, .23f);
            camera.orthographic = true; camera.orthographicSize = 1.15f; camera.nearClipPlane = .01f;
            camera.transform.position = model.transform.position + new Vector3(.5f, 1.25f, 3.8f);
            camera.transform.LookAt(model.transform.position + Vector3.up * .9f);
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            Itfaiye.Editor.ItfaiyeWeaponFeelReview.Capture(camera, Folder + "/" + name);
        }
        finally { Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); }
    }
}
