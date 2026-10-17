using System;
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
public static class ThermalHazardSetup
{
    public const string Folder = "Assets/GameJam/FireRescue/Evacuation";
    public const string MotionPath = "Assets/Animation/Thermal/BeingElectrocuted.fbx";
    const string ScenePath = "Assets/BK/PureNature_Volcano/Scenes/Volcano_Demo.unity";
    static double next;
    static ThermalHazardSetup() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        const string commandPath = "Logs/ThermalHazards.command";
        if (!File.Exists(commandPath)) return;
        string command = File.ReadAllText(commandPath).Trim(); File.Delete(commandPath);
        try
        {
            if (command == "apply") Apply();
            else if (command == "refresh") AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            else if (command == "checks") ThermalHazardChecks.Begin();
            else if (command == "fire checks") FireCombatChecks.Run();
            else if (command == "audit") Audit();
            else throw new InvalidOperationException("Unknown thermal command: " + command);
            File.WriteAllText("Logs/ThermalHazards-result.txt", "OK " + command);
        }
        catch (Exception e) { File.WriteAllText("Logs/ThermalHazards-result.txt", e.ToString()); Debug.LogException(e); }
    }
    [MenuItem("GameJam/Fire Hazards/Configure civilian and vehicle fire")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring hazards.");
        if (SceneManager.GetActiveScene().path != ScenePath) throw new InvalidOperationException("Open Volcano_Demo first.");
        var importer = AssetImporter.GetAtPath(MotionPath) as ModelImporter;
        if (!importer) throw new InvalidOperationException("The downloaded Mixamo reaction is missing.");
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        var imported = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
        foreach (var c in imported)
        {
            c.loopTime = true; c.loopPose = true; c.lockRootRotation = true;
            c.lockRootHeightY = true; c.lockRootPositionXZ = true; c.keepOriginalOrientation = true;
            c.keepOriginalPositionXZ = true; c.keepOriginalPositionY = true;
        }
        importer.clipAnimations = imported; importer.SaveAndReimport();
        var source = AssetDatabase.LoadAllAssetsAtPath(MotionPath).OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        if (!source.humanMotion) throw new InvalidOperationException("Burn reaction must retarget to humanoid villagers.");
        var burn = Object.Instantiate(source); burn.name = "Burning";
        var settings = AnimationUtility.GetAnimationClipSettings(burn);
        settings.loopTime = true; settings.loopBlend = true; AnimationUtility.SetAnimationClipSettings(burn, settings);
        burn = SaveClip(burn, Folder + "/Burning.anim");
        var death = Object.Instantiate(source); death.name = "BurnDeath";
        // Preserve the mocap's protective arm pose, then settle into a folded, still body.
        foreach (var binding in AnimationUtility.GetCurveBindings(death))
        {
            var original = AnimationUtility.GetEditorCurve(source, binding);
            float start = original.Evaluate(.35f), end = original.Evaluate(.65f);
            if (binding.type == typeof(Animator))
            {
                if (binding.propertyName.Contains("Lower Leg Stretch")) end = .75f;
                if (binding.propertyName.Contains("Upper Leg Front-Back")) end = -.07f;
                if (binding.propertyName.Contains("Spine Front-Back") || binding.propertyName.Contains("Chest Front-Back")) end = .08f;
                if (binding.propertyName.Contains("Arm Down-Up")) end = -.75f;
                if (binding.propertyName.Contains("Arm Front-Back")) end = .12f;
                if (binding.propertyName.Contains("Forearm Stretch")) end = .35f;
                if (binding.propertyName.Contains("Shoulder Down-Up")) end = -.15f;
                if (binding.propertyName.StartsWith("RootT.")) end = start;
                if (binding.propertyName.StartsWith("RootQ.")) end = start;
            }
            var curve = new AnimationCurve(new Keyframe(0, start), new Keyframe(.22f, original.Evaluate(.5f)),
                new Keyframe(.85f, end), new Keyframe(1.3f, end));
            AnimationUtility.SetEditorCurve(death, binding, curve);
        }
        AnimationUtility.SetAnimationEvents(death, Array.Empty<AnimationEvent>());
        settings = AnimationUtility.GetAnimationClipSettings(death);
        settings.loopTime = false; settings.loopBlend = false; settings.startTime = 0; settings.stopTime = 1.3f;
        AnimationUtility.SetAnimationClipSettings(death, settings);
        death = SaveClip(death, Folder + "/BurnDeath.anim");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(Folder + "/CivilianEvacuation.controller");
        if (!controller) throw new InvalidOperationException("Civilian controller is missing.");
        State(controller, "Burning", burn); State(controller, "BurnDeath", death);
        Directory.CreateDirectory("Assets/GameJam/FireRescue/Resources/ThermalHazards"); AssetDatabase.Refresh();
        const string audioPath = "Assets/GameJam/FireRescue/Resources/ThermalHazards/Audio.asset";
        var audio = AssetDatabase.LoadAssetAtPath<ThermalAudioProfile>(audioPath);
        if (!audio) { audio = ScriptableObject.CreateInstance<ThermalAudioProfile>(); AssetDatabase.CreateAsset(audio, audioPath); }
        audio.pain = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/acı cekmek.mp3");
        audio.death = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/Lava Death.wav");
        audio.fire = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Art/sesdosya/fire.wav");
        if (!audio.pain || !audio.death || !audio.fire) throw new InvalidOperationException("Existing fire/pain/death audio is missing.");
        EditorUtility.SetDirty(audio);
        foreach (var r in Object.FindObjectsByType<EvacuationResident>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var damage = r.GetComponent<ThermalDamage>() ?? r.gameObject.AddComponent<ThermalDamage>();
            damage.resident = r; damage.fireDamagePerSecond = 16; damage.lavaDamagePerSecond = 48; damage.waterToExtinguish = 12;
            r.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            EditorUtility.SetDirty(damage); EditorUtility.SetDirty(r.animator);
            PrefabUtility.RecordPrefabInstancePropertyModifications(r.animator);
        }
        foreach (var truck in Object.FindObjectsByType<FireTruckDrive>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var damage = truck.GetComponent<ThermalDamage>() ?? truck.gameObject.AddComponent<ThermalDamage>();
            damage.ConfigureRescueTruck();
            EditorUtility.SetDirty(damage);
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Audit();
    }
    static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!existing) { AssetDatabase.CreateAsset(clip, path); return clip; }
        EditorUtility.CopySerialized(clip, existing); Object.DestroyImmediate(clip); EditorUtility.SetDirty(existing); return existing;
    }
    static void State(AnimatorController controller, string name, AnimationClip clip)
    {
        var machine = controller.layers[0].stateMachine;
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (!state) state = machine.AddState(name, new Vector3(450, name == "Burning" ? 200 : 290));
        state.motion = clip; state.writeDefaultValues = true; state.speed = name == "Burning" ? 1.15f : 1f;
        EditorUtility.SetDirty(state);
    }
    public static void Audit()
    {
        Directory.CreateDirectory("Logs/ThermalHazards");
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("scene=" + SceneManager.GetActiveScene().path + " playing=" + EditorApplication.isPlaying);
        foreach (var t in Object.FindObjectsByType<ThermalDamage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            lines.AppendLine(t.name + " civilian=" + (t.resident != null) + " truck=" + (t.truck != null) + " health=" + t.Health + " bounds=" + t.GetComponent<Collider>()?.bounds);
        foreach (var name in new[] { "Burning", "BurnDeath" })
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + "/" + name + ".anim");
            lines.AppendLine(name + " human=" + (clip && clip.humanMotion) + " duration=" + (clip ? clip.length : 0));
        }
        var audio = Resources.Load<ThermalAudioProfile>("ThermalHazards/Audio");
        lines.AppendLine("audio=" + (audio && audio.pain && audio.death && audio.fire));
        File.WriteAllText("Logs/ThermalHazards/audit.txt", lines.ToString());
    }
}
