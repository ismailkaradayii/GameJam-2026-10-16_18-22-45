using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GameplayFeelSetup
{
    [MenuItem("GameJam/Final Loop/Apply gameplay feel")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != FinalLoopSetup.ScenePath)
            throw new InvalidOperationException("Open Volcano_Demo in Edit Mode first.");
        var player = Object.FindFirstObjectByType<FireRescuePlayer>();
        ConfigureSprint(player);
        NormalizeTruckScale(player.drive);
        FinalLoopSetup.ConfigureTruckPace(player.drive);
        player.drive.antiRollForce = 18000;
        Dirty(player.drive);
        RelocateResidents(player.mission);
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);
        AssetDatabase.SaveAssets();
        Audit();
    }
    static void ConfigureSprint(FireRescuePlayer player)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Itfaiye/Animation/ItfaiyePistol.controller");
        var clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Animation/run.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__"));
        var machine = controller.layers[0].stateMachine;
        var unarmed = machine.states.First(s => s.state.name == "Unarmed").state;
        // Keep the stride continuous through the existing idle/walk/run blend.
        var tree = (BlendTree)unarmed.motion;
        var children = tree.children;
        children[2].motion = clip; children[2].threshold = 4.57f; children[2].timeScale = 1.15f;
        tree.children = children; unarmed.iKOnFeet = true;
        foreach (var animator in new[] {player.firstPerson.bodyAnimator, player.firstPerson.handsAnimator})
        {
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Dirty(animator);
        }
        var hands = player.firstPerson.handsAnimator.GetComponent<Itfaiye.ItfaiyeFirstPersonHands>();
        hands.unarmedSprintSwing = new Vector3(.04f, .09f, .17f); hands.unarmedSprintBlendSeconds = .12f;
        Dirty(hands); Dirty(tree); Dirty(unarmed); Dirty(machine); Dirty(controller);
    }
    static void NormalizeTruckScale(FireTruckDrive drive)
    {
        // Preserve the current visible dimensions and position while giving the
        // Rigidbody and WheelColliders a unit-scale physics root.
        Vector3 scale = drive.transform.localScale;
        if ((scale - Vector3.one).sqrMagnitude < .00001f) return;
        if (Mathf.Abs(scale.x - scale.y) > .001f || Mathf.Abs(scale.x - scale.z) > .001f)
            throw new InvalidOperationException("Truck scale must be uniform before baking its physics dimensions.");
        float factor = scale.x;
        foreach (Transform child in drive.transform)
        {
            child.localPosition *= factor;
            var wheel = child.GetComponent<WheelCollider>();
            if (wheel)
            {
                wheel.center *= factor; wheel.radius *= factor; wheel.suspensionDistance *= factor;
                child.localScale = Vector3.one; Dirty(wheel);
            }
            else child.localScale *= factor;
            Dirty(child);
        }
        foreach (var box in drive.GetComponents<BoxCollider>()) { box.center *= factor; box.size *= factor; Dirty(box); }
        drive.centerOfMassOffset *= factor;
        drive.transform.localScale = Vector3.one;
        Dirty(drive.transform); Dirty(drive);
    }
    static void RelocateResidents(VolcanoMissionDirector mission)
    {
        var evacuation = mission.evacuation;
        var startingHouse = mission.startingHouse.GetComponentInParent<BurnableBuilding>();
        var moving = evacuation.residents.Where(r => r.door.GetComponentInParent<BurnableBuilding>() == startingHouse).ToArray();
        string[] destinations = {"House_Green_Prefab (1)", "House_Green_Prefab (2)"};
        for (int i = 0; i < moving.Length; i++)
        {
            var template = evacuation.residents.First(r => r.door.GetComponentInParent<BurnableBuilding>().name == destinations[i % destinations.Length]);
            var house = template.door.GetComponentInParent<BurnableBuilding>();
            Vector3 destination = template.transform.position;
            bool found = false;
            foreach (var offset in new[] {house.transform.right * 2.2f, -house.transform.right * 2.2f, -house.transform.forward * 2.2f})
            {
                if (!NavMesh.SamplePosition(destination + offset, out var hit, 1.5f, NavMesh.AllAreas)) continue;
                if (!house.ignitionBounds.bounds.Contains(hit.position + Vector3.up)) continue;
                if (evacuation.residents.Any(r => r != moving[i] && Vector3.Distance(r.transform.position, hit.position) < 1.7f)) continue;
                destination = hit.position; found = true; break;
            }
            if (!found) throw new InvalidOperationException("No clear indoor NavMesh spawn for " + moving[i].name);
            moving[i].transform.SetPositionAndRotation(destination, template.transform.rotation);
            moving[i].door = template.door; moving[i].houseExit = template.houseExit;
            Dirty(moving[i].transform); Dirty(moving[i]);
        }
        // The player's exit remains the first house, independently of NPC order.
        mission.houseExit = GameObject.Find("House 1 exit").transform;
        Dirty(mission); Dirty(evacuation);
    }
    static void Dirty(Object item)
    {
        EditorUtility.SetDirty(item);
        if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
    }
    [MenuItem("GameJam/Final Loop/Feel audit")]
    public static void Audit()
    {
        var p = Object.FindFirstObjectByType<FireRescuePlayer>();
        var d = p.drive;
        var lines = new List<string> { $"scene={SceneManager.GetActiveScene().path} play={Application.isPlaying} dirty={SceneManager.GetActiveScene().isDirty}",
            $"player={p.transform.position} fp.body={p.firstPerson.bodyAnimator} fp.hands={p.firstPerson.handsAnimator} fp.camera={p.firstPerson.playerCamera} pistol={p.firstPerson.pistol}",
            $"truck={d.transform.position} scale={d.transform.lossyScale} body={d.body} mass={d.vehicleMass} speed={d.SpeedKph} driven={d.IsDriven}" };
        foreach (var a in p.GetComponentsInChildren<Animator>(true))
        {
            lines.Add($"ANIM {Path(a.transform)} enabled={a.enabled} active={a.gameObject.activeInHierarchy} human={a.isHuman} avatar={a.avatar} controller={AssetDatabase.GetAssetPath(a.runtimeAnimatorController)} rootMotion={a.applyRootMotion} culling={a.cullingMode}");
            if (a.runtimeAnimatorController) foreach (var clip in a.runtimeAnimatorController.animationClips.Distinct())
                lines.Add($"CLIP {clip.name} path={AssetDatabase.GetAssetPath(clip)} humanoid={clip.humanMotion} loop={clip.isLooping} duration={clip.length}");
        }
        foreach (var c in d.GetComponentsInChildren<Collider>(true))
        {
            lines.Add($"COLLIDER {Path(c.transform)} {c.GetType().Name} enabled={c.enabled} trigger={c.isTrigger} bounds={c.bounds}");
            if (c is WheelCollider w)
            {
                Vector3 centre = w.transform.TransformPoint(w.center);
                lines.Add($"WHEEL centre={centre} radius={w.radius} suspension={w.suspensionDistance} spring={w.suspensionSpring.spring} damper={w.suspensionSpring.damper} target={w.suspensionSpring.targetPosition} grounded={w.isGrounded}");
                if (Terrain.activeTerrain) lines.Add("TERRAIN beneath=" + (Terrain.activeTerrain.SampleHeight(centre) + Terrain.activeTerrain.transform.position.y));
            }
            if (c is BoxCollider b) foreach (var hit in Physics.OverlapBox(b.bounds.center, b.bounds.extents * .99f, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(d.transform)) lines.Add($"OVERLAP {Path(hit.transform)} {hit.GetType().Name} bounds={hit.bounds}");
        }
        foreach (var r in Object.FindObjectsByType<EvacuationResident>(FindObjectsSortMode.None))
            lines.Add($"NPC {r.name} pos={r.transform.position} door={Path(r.door.transform)} exit={r.houseExit.name} exitPos={r.houseExit.position}");
        File.WriteAllLines("Logs/GameplayFeel-audit.txt", lines);
    }
    static string Path(Transform t) => t.parent ? Path(t.parent) + "/" + t.name : t.name;
}
