using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Itfaiye;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class SwimmingAnimationSetup
{
    public const string IdlePath = "Assets/Animation/swiminig  ıdle.fbx";
    public const string SwimPath = "Assets/Animation/swimming.fbx";
    public const string ControllerPath = "Assets/Itfaiye/Animation/ItfaiyePistol.controller";
    static double next;
    static SwimmingAnimationSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        const string request = "Logs/SwimmingAnimation.command";
        if (!File.Exists(request)) return;
        string command = File.ReadAllText(request).Trim(); File.Delete(request);
        try
        {
            if (command == "audit") Audit();
            else if (command == "apply") Apply();
            else if (command == "checks") SwimmingAnimationChecks.Begin();
            else throw new InvalidOperationException("Unknown swimming command " + command);
            File.WriteAllText("Logs/SwimmingAnimation-result.txt", "OK " + command);
        }
        catch (Exception ex) { File.WriteAllText("Logs/SwimmingAnimation-result.txt", ex.ToString()); Debug.LogException(ex); }
    }

    public static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__"));

    [MenuItem("GameJam/Water/Use authored swimming animations")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Configure animation assets outside Play Mode");
        foreach (var path in new[] { IdlePath, SwimPath })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                clip.loopTime = true; clip.loopPose = true;
                clip.lockRootRotation = true; clip.keepOriginalOrientation = true;
                clip.lockRootHeightY = true; clip.keepOriginalPositionY = true; clip.heightFromFeet = false;
                clip.lockRootPositionXZ = false; clip.keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips; importer.SaveAndReimport();
            if (!Clip(path).humanMotion) throw new InvalidOperationException("Swimming clip must be humanoid: " + path);
        }
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AddParameter(controller, "Swimming", AnimatorControllerParameterType.Bool);
        AddParameter(controller, "SwimSpeed", AnimatorControllerParameterType.Float);
        AddParameter(controller, "SwimPlaybackRate", AnimatorControllerParameterType.Float);
        var parameters = controller.parameters;
        foreach (var p in parameters) if (p.name == "SwimPlaybackRate") p.defaultFloat = 1;
        controller.parameters = parameters;
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Swimming");
        if (!state) state = machine.AddState("Swimming", new Vector3(50, 450, 0));
        var tree = state.motion as BlendTree;
        if (!tree) { tree = new BlendTree { name = "Authored swim idle and movement" }; AssetDatabase.AddObjectToAsset(tree, controller); }
        tree.blendType = BlendTreeType.Simple1D; tree.blendParameter = "SwimSpeed"; tree.useAutomaticThresholds = false;
        tree.children = new[] { new ChildMotion { motion = Clip(IdlePath), threshold = 0, timeScale = 1 }, new ChildMotion { motion = Clip(SwimPath), threshold = 1, timeScale = 1 } };
        state.motion = tree; state.iKOnFeet = false; state.writeDefaultValues = true;
        state.speedParameterActive = true; state.speedParameter = "SwimPlaybackRate";
        var entry = machine.anyStateTransitions.FirstOrDefault(t => t.destinationState == state);
        if (!entry) entry = machine.AddAnyStateTransition(state);
        entry.conditions = new[] { new AnimatorCondition { parameter = "Swimming", mode = AnimatorConditionMode.If } };
        entry.duration = .18f; entry.hasExitTime = false; entry.canTransitionToSelf = false;
        foreach (var transition in machine.anyStateTransitions)
            if (transition != entry && !transition.conditions.Any(c => c.parameter == "Swimming")) transition.AddCondition(AnimatorConditionMode.IfNot, 0, "Swimming");
        var unarmed = machine.states.Select(s => s.state).First(s => s.name == "Unarmed");
        var exit = state.transitions.FirstOrDefault(t => t.destinationState == unarmed);
        if (!exit) exit = state.AddTransition(unarmed);
        exit.conditions = new[] { new AnimatorCondition { parameter = "Swimming", mode = AnimatorConditionMode.IfNot } };
        exit.duration = .2f; exit.hasExitTime = false;
        EditorUtility.SetDirty(tree); EditorUtility.SetDirty(state); EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
    }

    static void AddParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, type);
    }

    public static void Audit()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Audit clips outside Play Mode");
        var report = new StringBuilder();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Itfaiye/Prefabs/ItfaiyeFirstPersonPlayer.prefab");
        var instance = Object.Instantiate(prefab); instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var player = instance.GetComponent<ItfaiyeFirstPersonController>();
            foreach (var path in new[] { IdlePath, SwimPath })
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path); var modelAnimator = model.GetComponent<Animator>();
                report.AppendLine(path + " type=" + importer.animationType + " validAvatar=" + (modelAnimator && modelAnimator.avatar && modelAnimator.avatar.isValid));
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    report.AppendLine("CLIP " + clip.name + " duration=" + clip.length + " humanoid=" + clip.humanMotion + " loop=" + clip.isLooping);
                var animation = Clip(path);
                foreach (var animator in new[] { player.bodyAnimator, player.handsAnimator })
                {
                    report.AppendLine("RIG " + animator.name + " pos=" + animator.transform.localPosition + " rotation=" + animator.transform.localEulerAngles + " camera=" + player.playerCamera.transform.localPosition);
                    for (int sample = 0; sample < 4; sample++)
                    {
                        AnimationMode.StartAnimationMode();
                        try
                        {
                            AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(animator.gameObject, animation, animation.length * sample / 4f); AnimationMode.EndSampling();
                            report.AppendLine("t=" + sample / 4f + " hips=" + instance.transform.InverseTransformPoint(animator.GetBoneTransform(HumanBodyBones.Hips).position));
                            foreach (var bone in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand })
                            {
                                var t = animator.GetBoneTransform(bone);
                                report.AppendLine(bone + " camera=" + player.playerCamera.transform.InverseTransformPoint(t.position) + " root=" + instance.transform.InverseTransformPoint(t.position));
                            }
                        }
                        finally { AnimationMode.StopAnimationMode(); }
                    }
                }
            }
        }
        finally { Object.DestroyImmediate(instance); }
        File.WriteAllText("Logs/SwimmingAnimation-audit.txt", report.ToString());
    }
}
