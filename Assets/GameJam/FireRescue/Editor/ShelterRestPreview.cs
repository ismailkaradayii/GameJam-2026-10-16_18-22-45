using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Samples the seven real humanoid rigs offscreen, without entering Play Mode.</summary>
public static class ShelterRestPreview
{
    [MenuItem("GameJam/Evacuation/Review shelter rest (isolated preview)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
            throw new InvalidOperationException("Shelter preview requires Edit Mode, outside animation preview.");
        Directory.CreateDirectory(ShelterRestSetup.Folder);
        var report = new List<string>();
        var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty; int roots = scene.rootCount;
        PreviewRenderUtility preview = null; Material groundMaterial = null; Mesh ground = null;
        try
        {
            var mission = Object.FindFirstObjectByType<EvacuationMission>();
            if (!mission || !mission.shelterEntrance) throw new InvalidOperationException("Existing evacuation mission missing.");
            var sources = mission.residents.Where(r => r).OrderBy(r => r.formationIndex).ToArray();
            preview = new PreviewRenderUtility();
            var animators = new List<Animator>(); var heights = new List<float>();
            foreach (var source in sources)
            {
                var model = Object.Instantiate(source.animator.gameObject, preview.camera.transform, false);
                model.transform.SetParent(null, false); preview.AddSingleGO(model);
                model.transform.SetPositionAndRotation(new Vector3(source.formationIndex % 4 * 5f, 0, -(source.formationIndex / 4) * 6f), Quaternion.identity);
                model.transform.localScale = source.animator.transform.lossyScale;
                foreach (var lod in model.GetComponentsInChildren<LODGroup>()) lod.ForceLOD(0);
                var animator = model.GetComponent<Animator>();
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind(); animator.Play("Base Layer.Locomotion", 0, 0); animator.Update(0);
                heights.Add(animator.GetBoneTransform(HumanBodyBones.Hips).position.y);
                animators.Add(animator);
                bool slot = source.safePlace && mission.IsInsideShelter(source.safePlace.position);
                var filter = new NavMeshQueryFilter { agentTypeID = source.GetComponent<NavMeshAgent>().agentTypeID, areaMask = NavMesh.AllAreas };
                var path = new NavMeshPath();
                bool reachable = slot && NavMesh.CalculatePath(mission.shelterEntrance.position, source.safePlace.position, filter, path) && path.status == NavMeshPathStatus.PathComplete;
                Check(report, source.name + " retains a reachable personal place inside the shelter", reachable);
            }
            ground = new Mesh { name = "Shelter rest preview floor" };
            ground.vertices = new[] { new Vector3(-5, 0, -11), new Vector3(-5, 0, 5), new Vector3(21, 0, 5), new Vector3(21, 0, -11) };
            ground.triangles = new[] { 0, 1, 2, 0, 2, 3 }; ground.RecalculateNormals();
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); groundMaterial.color = new Color(.22f, .25f, .28f);
            preview.cameraFieldOfView = 46; preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 100;
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.09f, .11f, .14f);
            Vector3 centre = new Vector3(7.5f, 2.3f, -2);
            preview.camera.transform.position = centre + new Vector3(8, 8, 26); preview.camera.transform.LookAt(centre);
            preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(45, 30, 0);
            preview.lights[1].intensity = .7f; preview.lights[1].transform.rotation = Quaternion.Euler(20, 220, 0);
            Capture(preview, ground, groundMaterial, animators, "01-standing");
            foreach (var animator in animators) animator.SetBool("ShelterRest", true);
            Advance(animators, .8f); Capture(preview, ground, groundMaterial, animators, "02-kneeling-down");
            Advance(animators, 1.4f); Capture(preview, ground, groundMaterial, animators, "03-resting");
            for (int i = 0; i < animators.Count; i++)
            {
                var animator = animators[i];
                float drop = heights[i] - animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                Check(report, sources[i].name + " reaches the resting loop with lowered hips (drop=" + drop.ToString("F2") + "m)",
                    animator.GetCurrentAnimatorStateInfo(0).IsName("Shelter Kneeling Idle") && drop > .5f);
                float toes = Mathf.Min(animator.GetBoneTransform(HumanBodyBones.LeftToes).position.y,
                    animator.GetBoneTransform(HumanBodyBones.RightToes).position.y);
                Check(report, sources[i].name + " rests at floor level (lowest toe=" + toes.ToString("F2") + "m)", toes >= -.18f && toes <= .8f);
            }
            var positions = animators.Select(a => a.transform.position).ToArray();
            Advance(animators, 9f);
            Check(report, "Resting loop remains active and has no root drift on any civilian rig", animators.Select((a, i) =>
                a.GetCurrentAnimatorStateInfo(0).IsName("Shelter Kneeling Idle") && Vector3.Distance(a.transform.position, positions[i]) < .001f).All(v => v));
            report.Add("LIMITATION: offscreen Edit Mode animator playback and current NavMesh path queries. No live follower movement, complete evacuation or manual Play Mode run.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        finally
        {
            preview?.Cleanup(); if (groundMaterial) Object.DestroyImmediate(groundMaterial); if (ground) Object.DestroyImmediate(ground);
            Check(report, "Main scene and dirty state unchanged", SceneManager.GetActiveScene() == scene && scene.rootCount == roots && scene.isDirty == dirty);
            File.WriteAllLines(ShelterRestSetup.Folder + "/checks.txt", report);
        }
    }
    static void Advance(List<Animator> animators, float seconds)
    {
        for (int frame = 0; frame < Mathf.CeilToInt(seconds * 60); frame++)
            foreach (var animator in animators) animator.Update(1f / 60);
    }
    static void Check(List<string> report, string label, bool passed) => report.Add((passed ? "PASS " : "FAIL ") + label);
    static void Capture(PreviewRenderUtility preview, Mesh ground, Material material, List<Animator> animators, string name)
    {
        const int width = 1440, height = 900;
        var baked = new List<Mesh>();
        // Several poses are evaluated within one Editor frame. Bake each snapshot so
        // the GPU skinning cache cannot display a previous pose over the moved rig.
        foreach (var animator in animators)
            foreach (var skin in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.enabled = false;
                if (!skin.name.EndsWith("lod0", StringComparison.OrdinalIgnoreCase)) continue;
                var mesh = new Mesh(); skin.BakeMesh(mesh); baked.Add(mesh);
                // This avatar's baked vertices already contain the animated root scale.
                var world = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                var materials = skin.sharedMaterials;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                    preview.DrawMesh(mesh, world, materials[Mathf.Min(sub, materials.Length - 1)], sub);
            }
        preview.DrawMesh(ground, Matrix4x4.identity, material, 0);
        preview.BeginPreview(new Rect(0, 0, width, height), GUIStyle.none); preview.Render(true);
        var result = preview.EndPreview(); var previous = RenderTexture.active;
        var copy = RenderTexture.GetTemporary(width, height, 0); var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            Graphics.Blit(result, copy); RenderTexture.active = copy;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0); texture.Apply();
            File.WriteAllBytes(ShelterRestSetup.Folder + "/" + name + ".png", texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; RenderTexture.ReleaseTemporary(copy); Object.DestroyImmediate(texture);
            foreach (var mesh in baked) Object.DestroyImmediate(mesh);
        }
    }
}
