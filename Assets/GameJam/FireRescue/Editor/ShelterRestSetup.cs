using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Extends the existing civilian controller; never moves residents or saves the gameplay scene.</summary>
[InitializeOnLoad]
public static class ShelterRestSetup
{
    public const string Folder = "Logs/ShelterRest";
    const string AssetsFolder = "Assets/GameJam/FireRescue/Evacuation/";
    const string Pack = "Assets/Animation/Pistol_Handgun Locomotion Pack/";
    const string Request = Folder + "/request.txt";
    static ShelterRestSetup() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || File.Exists("Logs/FinalLoop.command")) return;
        string command = File.ReadAllText(Request).Trim(); File.Delete(Request);
        try
        {
            if (command == "configure") Configure();
            else if (command == "preview") ShelterRestPreview.Run();
            else throw new InvalidOperationException("Unknown shelter rest command: " + command);
        }
        catch (Exception ex) { File.WriteAllText(Folder + "/error.txt", ex.ToString()); Debug.LogException(ex); }
    }

    [MenuItem("GameJam/Evacuation/Configure shelter rest animations")]
    public static void Configure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
            throw new InvalidOperationException("Configure shelter rest in Edit Mode, outside animation preview.");
        Directory.CreateDirectory(Folder);
        var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty;
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetsFolder + "CivilianEvacuation.controller");
        if (!controller) throw new InvalidOperationException("Existing civilian controller missing.");
        var down = UnarmedClip("pistol stand to kneel.fbx", "Shelter Kneel Down", false);
        var idle = UnarmedClip("pistol kneeling idle.fbx", "Shelter Kneeling Idle", true);
        if (!controller.parameters.Any(p => p.name == "ShelterRest"))
            controller.AddParameter("ShelterRest", AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        var walk = machine.states.First(s => s.state.name == "Locomotion").state;
        var kneel = State(machine, "Shelter Kneel Down", new Vector3(470, 0), down);
        var resting = State(machine, "Shelter Kneeling Idle", new Vector3(720, 0), idle);
        if (!walk.transitions.Any(t => t.destinationState == kneel))
        {
            var enter = walk.AddTransition(kneel); enter.hasExitTime = false; enter.duration = .25f;
            enter.hasFixedDuration = true; enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0, "ShelterRest");
        }
        if (!kneel.transitions.Any(t => t.destinationState == resting))
        {
            var settle = kneel.AddTransition(resting); settle.hasExitTime = true; settle.exitTime = .92f;
            settle.duration = .16f; settle.hasFixedDuration = true; settle.canTransitionToSelf = false;
        }
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(machine);
        AssetDatabase.SaveAssetIfDirty(controller);
        var report = new List<string> { "Configured existing civilian controller with unarmed kneel-down and resting loop." };
        var mission = Object.FindFirstObjectByType<EvacuationMission>();
        if (mission)
        {
            foreach (var resident in mission.residents.Where(r => r))
            {
                bool bound = resident.animator && resident.animator.runtimeAnimatorController == controller && resident.safePlace;
                bool onMesh = resident.safePlace && NavMesh.SamplePosition(resident.safePlace.position, out var hit, .6f, NavMesh.AllAreas) &&
                    EvacuationResident.FlatDistance(resident.safePlace.position, hit.position) < .5f;
                report.Add((bound && onMesh ? "PASS " : "FAIL ") + resident.name + " personal place=" +
                    (resident.safePlace ? resident.safePlace.position.ToString("F2") : "MISSING") + "; controller=" + bound + "; NavMesh=" + onMesh);
            }
            report.Add((mission.residents.Where(r => r && r.safePlace).Select(r => r.safePlace).Distinct().Count() == mission.TotalCount ? "PASS " : "FAIL ") + "Every resident has a distinct existing shelter place.");
        }
        report.Add((SceneManager.GetActiveScene() == scene && scene.isDirty == dirty ? "PASS " : "FAIL ") + "Gameplay scene and dirty state preserved; no scene save or Play Mode.");
        File.WriteAllLines(Folder + "/setup.txt", report);
    }

    static AnimatorState State(AnimatorStateMachine machine, string name, Vector3 position, Motion motion)
    {
        var state = machine.states.FirstOrDefault(s => s.state.name == name).state;
        if (!state) state = machine.AddState(name, position);
        state.motion = motion; state.writeDefaultValues = true; state.iKOnFeet = false;
        EditorUtility.SetDirty(state); return state;
    }

    static AnimationClip UnarmedClip(string model, string name, bool loop)
    {
        var source = AssetDatabase.LoadAllAssetsAtPath(Pack + model).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        var unarmed = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetsFolder + "Idle.anim");
        if (!unarmed || !source.isHumanMotion) throw new InvalidOperationException("Existing humanoid animation sources missing.");
        var copy = Object.Instantiate(source); copy.name = name; copy.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
        // Keep the authored legs, hips and lowering motion, with relaxed civilian arms.
        // Pistol hand IK goals must not drag the hands back into a weapon grip.
        foreach (var binding in AnimationUtility.GetCurveBindings(copy))
            if (binding.type == typeof(Animator) && (binding.propertyName.StartsWith("LeftHandT.") ||
                binding.propertyName.StartsWith("LeftHandQ.") || binding.propertyName.StartsWith("RightHandT.") || binding.propertyName.StartsWith("RightHandQ.")))
                AnimationUtility.SetEditorCurve(copy, binding, null);
        foreach (var binding in AnimationUtility.GetCurveBindings(unarmed))
        {
            string property = binding.propertyName;
            if (binding.type != typeof(Animator) || !(property.Contains(" Arm ") || property.Contains(" Forearm ") ||
                property.Contains(" Shoulder ") || property.Contains(" Hand ") || property.StartsWith("LeftHand.") || property.StartsWith("RightHand."))) continue;
            float value = AnimationUtility.GetEditorCurve(unarmed, binding).Evaluate(.5f);
            AnimationUtility.SetEditorCurve(copy, binding, AnimationCurve.Constant(0, source.length, value));
        }
        AnimationUtility.SetAnimationEvents(copy, Array.Empty<AnimationEvent>());
        var settings = AnimationUtility.GetAnimationClipSettings(copy);
        settings.loopTime = loop; settings.loopBlend = loop; settings.loopBlendPositionY = true;
        settings.loopBlendPositionXZ = true; settings.keepOriginalPositionXZ = true; settings.keepOriginalOrientation = true;
        AnimationUtility.SetAnimationClipSettings(copy, settings);
        string path = AssetsFolder + name + ".anim";
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (existing) { EditorUtility.CopySerialized(copy, existing); Object.DestroyImmediate(copy); }
        else { AssetDatabase.CreateAsset(copy, path); existing = copy; }
        EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
        return existing;
    }
}
