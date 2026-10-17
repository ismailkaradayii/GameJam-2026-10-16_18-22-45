using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameJam.FireRescue;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Offscreen preview only. Does not enter Play Mode, save scenes or use shared gameplay command files.</summary>
[InitializeOnLoad]
public static class BuildingFireVisualReview
{
    const string Folder = "Logs/BuildingFireReview", Request = Folder + "/request.txt";
    static BuildingFireVisualReview() => EditorApplication.update += Poll;
    static void Poll()
    {
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || File.Exists("Logs/FinalLoop.command")) return;
        File.Delete(Request); Review();
    }
    [MenuItem("GameJam/Fire Rescue/Review building fire visuals (isolated preview)")]
    public static void Review()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Review runs in Edit Mode only.");
        Directory.CreateDirectory(Folder); var report = new List<string>();
        var scene = SceneManager.GetActiveScene(); int roots = scene.rootCount; bool dirty = scene.isDirty;
        PreviewRenderUtility preview = null; Material groundMaterial = null; Mesh ground = null; BuildingFirePresentation presentation = null;
        try
        {
            var source = Object.FindObjectsByType<BurnableBuilding>(FindObjectsSortMode.None).First(b => b.flames && b.smoke && b.ignitionBounds);
            preview = new PreviewRenderUtility();
            var model = Object.Instantiate(source.gameObject,preview.camera.transform,true);
            model.transform.SetParent(null,true); model.name = "Isolated house fire preview"; model.hideFlags = HideFlags.HideAndDontSave;
            preview.AddSingleGO(model);
            var building = model.GetComponent<BurnableBuilding>();
            Physics.SyncTransforms(); presentation = BuildingFirePresentation.Create(building);
            if (presentation == null) throw new InvalidOperationException("House fire materials failed to load.");
            Check(report,"House surfaces provide facade and roof sources",presentation.FlameSourceCount >= 8 && presentation.RoofSourceCount >= 3);
            report.Add("Sources: flames=" + presentation.FlameSourceCount + " roof=" + presentation.RoofSourceCount);
            var bounds = building.ignitionBounds.bounds;
            report.Add("World house bounds="+bounds.size+" rootScale="+building.transform.lossyScale);
            ground = new Mesh {name="Preview ground"}; float radius = Mathf.Max(bounds.size.x,bounds.size.z)*1.5f;
            Vector3 centre = new Vector3(bounds.center.x,bounds.min.y-.06f,bounds.center.z);
            ground.vertices = new[] {centre+new Vector3(-radius,0,-radius),centre+new Vector3(-radius,0,radius),centre+new Vector3(radius,0,radius),centre+new Vector3(radius,0,-radius)};
            ground.triangles = new[] {0,1,2,0,2,3}; ground.RecalculateNormals(); ground.RecalculateBounds();
            groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); groundMaterial.color = new Color(.18f,.15f,.12f);
            preview.cameraFieldOfView = 48;
            preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 500;
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.075f,.065f,.06f);
            Vector3 direction = (building.ignitionBounds.transform.forward + building.ignitionBounds.transform.right*.7f).normalized;
            preview.camera.transform.position = bounds.center + direction * Mathf.Max(bounds.size.x,bounds.size.z) * 2.2f + Vector3.up * 8;
            preview.camera.transform.LookAt(bounds.center + Vector3.up * 4);
            preview.lights[0].intensity = 1.25f; preview.lights[0].transform.rotation = Quaternion.Euler(45,35,0);
            preview.lights[1].intensity = .55f; preview.lights[1].transform.rotation = Quaternion.Euler(25,225,0);
            presentation.SetState(1,true); Simulate(model,presentation,4.5f);
            Check(report,"Full fire has bounded visible flames and rising smoke",building.flames.particleCount > 20 && building.flames.particleCount <= 192 && building.smoke.particleCount > 0);
            Capture(preview,"01-burning",ground,groundMaterial);
            presentation.SetState(.25f,true); Simulate(model,presentation,1.1f);
            Check(report,"Cooling reduces flame population",building.flames.particleCount > 0 && building.flames.particleCount < 60);
            Capture(preview,"02-water-cooling",ground,groundMaterial);
            presentation.SetState(0,true); Simulate(model,presentation,1);
            Check(report,"Extinguished fire clears flames and light while smoke drifts",building.flames.particleCount == 0 && !building.fireLight.enabled && building.smoke.particleCount > 0);
            Capture(preview,"03-extinguished",ground,groundMaterial);
            presentation.SetState(1,true); Simulate(model,presentation,.4f); presentation.Stop();
            Check(report,"Disable/reset clears every particle buffer",model.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount == 0));
            var shader = Resources.Load<Material>("BuildingFire/Flame").shader;
            Check(report,"Dedicated flame shader has no compiler errors",!ShaderUtil.ShaderHasError(shader));
            report.Add("LIMITATION: isolated offscreen Edit Mode clone; presentation heat supplied directly. This is not a Play Mode firefighting or complete mission test.");
        }
        catch (Exception ex) { report.Add("FAILED " + ex); }
        finally
        {
            presentation?.Dispose(); preview?.Cleanup(); if (groundMaterial) Object.DestroyImmediate(groundMaterial); if (ground) Object.DestroyImmediate(ground);
            Check(report,"Main scene and dirty state unchanged",SceneManager.GetActiveScene() == scene && scene.rootCount == roots && scene.isDirty == dirty);
            report.Add(report.Any(l => l.StartsWith("FAIL")) ? "FAILED" : "COMPLETED");
            File.WriteAllLines(Folder + "/checks.txt",report);
        }
    }
    static void Simulate(GameObject root, BuildingFirePresentation presentation, float seconds)
    {
        var particles = root.GetComponentsInChildren<ParticleSystem>(true);
        for (int frame = 0; frame < Mathf.CeilToInt(seconds*60); frame++)
        {
            presentation.Tick(1f/60);
            foreach (var ps in particles) ps.Simulate(1f/60,false,false,false);
        }
    }
    static void Capture(PreviewRenderUtility preview, string name, Mesh ground, Material material)
    {
        preview.DrawMesh(ground,Matrix4x4.identity,material,0);
        preview.BeginPreview(new Rect(0,0,1200,900),GUIStyle.none); preview.Render(true);
        var result = preview.EndPreview(); var previous = RenderTexture.active;
        var copy = RenderTexture.GetTemporary(1200,900,0); var texture = new Texture2D(1200,900,TextureFormat.RGB24,false);
        try { Graphics.Blit(result,copy); RenderTexture.active=copy; texture.ReadPixels(new Rect(0,0,1200,900),0,0); texture.Apply(); File.WriteAllBytes(Folder+"/"+name+".png",texture.EncodeToPNG()); }
        finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(copy); Object.DestroyImmediate(texture); }
    }
    static void Check(List<string> report,string label,bool passed) => report.Add((passed ? "PASS " : "FAIL ")+label);
}
