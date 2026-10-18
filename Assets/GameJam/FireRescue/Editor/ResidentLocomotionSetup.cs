using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class ResidentLocomotionSetup
{
    public const string Folder = "Logs/ResidentLocomotion";
    public const string AnimationFolder = "Assets/GameJam/FireRescue/Evacuation/";
    static double nextPoll;
    static ResidentLocomotionSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .5;
        const string request = "Logs/ResidentLocomotion.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "before") Audit();
            else if (command == "after") AuditAfter();
            else if (command == "apply") Apply();
            else if (command == "checks") ResidentLocomotionChecks.Begin();
            else if (command == "refresh") AssetDatabase.Refresh();
            else throw new InvalidOperationException("Unknown resident locomotion command: " + command);
            File.WriteAllText("Logs/ResidentLocomotion-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/ResidentLocomotion-result.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [Serializable] public class Frame
    {
        public float time;
        public Vector3 hips, leftFoot, rightFoot, leftKnee, rightKnee, leftToe, rightToe;
        public Vector3 leftForward, rightForward;
        public float[] legMuscles;
    }
    [Serializable] public class ClipReport
    {
        public string resident, clip;
        public float length, humanScale;
        public Vector3 averageSpeed;
        public List<Frame> frames = new List<Frame>();
    }
    [Serializable] public class Report
    {
        public string[] muscles;
        public List<string> bindings = new List<string>();
        public List<ClipReport> clips = new List<ClipReport>();
    }

    [MenuItem("GameJam/Final Loop/Audit resident locomotion")]
    public static void Audit() => Audit("before");

    [MenuItem("GameJam/Final Loop/Audit corrected resident locomotion")]
    public static void AuditAfter() => Audit("after");

    [MenuItem("GameJam/Final Loop/Apply resident stride and jump fix")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (!mission || mission.gameObject.scene.path != "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity")
            throw new InvalidOperationException("Open Volcano_Demo first.");
        var controller = (AnimatorController)mission.residents[0].animator.runtimeAnimatorController;
        var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Locomotion").state;
        var tree = (BlendTree)state.motion;
        foreach (string name in new[] { "Idle", "Walk", "Run" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + name + ".anim");
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            // Extract horizontal movement and heading from the pose. Navigation owns both.
            settings.loopTime = settings.loopBlend = true;
            settings.loopBlendPositionXZ = false;
            settings.loopBlendOrientation = false;
            settings.loopBlendPositionY = true;
            settings.keepOriginalPositionXZ = false;
            settings.keepOriginalOrientation = false;
            settings.heightFromFeet = true;
            settings.orientationOffsetY = 0;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
        }
        float walk = RootSpeed("Walk"), run = RootSpeed("Run");
        var children = tree.children;
        children[0].threshold = 0;
        children[1].threshold = walk;
        children[2].threshold = run;
        tree.children = children;
        if (!controller.parameters.Any(p => p.name == "StrideRate"))
            controller.AddParameter(new AnimatorControllerParameter { name = "StrideRate", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        state.speedParameter = "StrideRate"; state.speedParameterActive = true; state.iKOnFeet = true;
        foreach (var resident in mission.residents)
        {
            var motion = resident.animator.GetComponent<ResidentLocomotion>();
            if (!motion) motion = Undo.AddComponent<ResidentLocomotion>(resident.animator.gameObject);
            motion.resident = resident; motion.walkStrideSpeed = walk; motion.runStrideSpeed = run;
            resident.animator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(resident.animator);
            EditorUtility.SetDirty(motion); EditorUtility.SetDirty(resident.animator);
        }
        const string prefabPath = "Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab";
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            prefab.GetComponent<Itfaiye.ItfaiyeFirstPersonController>().jumpHeight = 2.4f;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var player = mission.player.firstPerson;
        player.jumpHeight = 2.4f;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player); EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(tree); EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
        EditorSceneManager.SaveScene(mission.gameObject.scene);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Folder + "/setup.txt", "Residents=" + mission.residents.Length + " walkStride=" + walk + " runStride=" + run +
            " jumpHeight=" + player.jumpHeight + " rootMotion=false horizontalRoot=extracted heading=extracted");
    }

    static float RootSpeed(string name)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + name + ".anim");
        var binding = AnimationUtility.GetCurveBindings(clip).First(b => b.propertyName == "RootT.z");
        var curve = AnimationUtility.GetEditorCurve(clip, binding);
        return Mathf.Abs(curve.Evaluate(clip.length) - curve.Evaluate(0)) / clip.length;
    }

    static void Audit(string suffix)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
        Directory.CreateDirectory(Folder);
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        var report = new Report();
        int[] ids = Enumerable.Range(0, HumanTrait.MuscleCount).Where(i => HumanTrait.MuscleName[i].Contains("Leg") || HumanTrait.MuscleName[i].Contains("Foot")).ToArray();
        report.muscles = ids.Select(i => HumanTrait.MuscleName[i]).ToArray();
        var preview = EditorSceneManager.NewPreviewScene();
        try
        {
            foreach (var resident in mission.residents)
            {
                report.bindings.Add(resident.name + " scale=" + resident.transform.lossyScale + " model=" + resident.animator.transform.localPosition +
                    " modelScale=" + resident.animator.transform.localScale + " humanScale=" + resident.animator.humanScale + " avatar=" + AssetDatabase.GetAssetPath(resident.animator.avatar));
                var model = Object.Instantiate(resident.animator.gameObject);
                SceneManager.MoveGameObjectToScene(model, preview);
                model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                model.transform.localScale = Vector3.one;
                var animator = model.GetComponent<Animator>();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                ResidentLocomotion motion = null;
                if (suffix == "after")
                {
                    motion = animator.GetComponent<ResidentLocomotion>();
                    if (!motion) motion = animator.gameObject.AddComponent<ResidentLocomotion>();
                    motion.Initialize();
                }
                AnimationMode.StartAnimationMode();
                try
                {
                    using (var handler = new HumanPoseHandler(animator.avatar, animator.transform))
                    foreach (string name in new[] { "Idle", "Walk", "Run" })
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimationFolder + name + ".anim");
                        var data = new ClipReport { resident = resident.name, clip = name, length = clip.length, averageSpeed = clip.averageSpeed, humanScale = animator.humanScale };
                        for (int i = 0; i <= 48; i++)
                        {
                            AnimationMode.BeginSampling();
                            AnimationMode.SampleAnimationClip(model, clip, clip.length * i / 48f);
                            AnimationMode.EndSampling();
                            if (motion) motion.StabilizePreview(clip.length / 48f);
                            var pose = new HumanPose(); handler.GetHumanPose(ref pose);
                            data.frames.Add(new Frame { time = clip.length * i / 48f,
                                hips = Position(animator, HumanBodyBones.Hips),
                                leftFoot = Position(animator, HumanBodyBones.LeftFoot), rightFoot = Position(animator, HumanBodyBones.RightFoot),
                                leftKnee = Position(animator, HumanBodyBones.LeftLowerLeg), rightKnee = Position(animator, HumanBodyBones.RightLowerLeg),
                                leftToe = Position(animator, HumanBodyBones.LeftToes), rightToe = Position(animator, HumanBodyBones.RightToes),
                                leftForward = (Position(animator, HumanBodyBones.LeftToes) - Position(animator, HumanBodyBones.LeftFoot)).normalized,
                                rightForward = (Position(animator, HumanBodyBones.RightToes) - Position(animator, HumanBodyBones.RightFoot)).normalized,
                                legMuscles = ids.Select(id => pose.muscles[id]).ToArray() });
                            if (resident.formationIndex == 0 && name != "Idle" && i % 12 == 0 && i < 48)
                                Capture(preview, animator, suffix + "-" + name + "-" + i + ".png");
                        }
                        report.clips.Add(data);
                    }
                }
                finally { AnimationMode.StopAnimationMode(); Object.DestroyImmediate(model); }
            }
            File.WriteAllText(Folder + "/" + suffix + ".json", JsonUtility.ToJson(report, true));
        }
        finally { EditorSceneManager.ClosePreviewScene(preview); }
    }

    static Vector3 Position(Animator animator, HumanBodyBones bone) => animator.transform.InverseTransformPoint(animator.GetBoneTransform(bone).position);

    static void Capture(Scene scene, Animator animator, string name)
    {
        var cameraObject = new GameObject("Resident review camera");
        var lightObject = new GameObject("Resident review light");
        SceneManager.MoveGameObjectToScene(cameraObject, scene); SceneManager.MoveGameObjectToScene(lightObject, scene);
        try
        {
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.16f, .19f, .23f);
            camera.orthographic = true; camera.orthographicSize = 1.12f; camera.nearClipPlane = .01f;
            camera.transform.position = new Vector3(2.3f, 1.35f, 3.6f); camera.transform.LookAt(new Vector3(0, .92f, 0));
            var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            Itfaiye.Editor.ItfaiyeWeaponFeelReview.Capture(camera, Folder + "/" + name);
        }
        finally { Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject); }
    }
}
