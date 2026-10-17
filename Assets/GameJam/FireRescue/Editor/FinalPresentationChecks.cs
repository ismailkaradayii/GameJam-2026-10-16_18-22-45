using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GameJam.FireRescue;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Isolated layout, shader and result-clock checks; does not start or stop the user's Play Mode.</summary>
public static class FinalPresentationChecks
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [MenuItem("GameJam/Final Loop/Review final presentation fixes (isolated)")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Review requires Edit Mode.");
        Directory.CreateDirectory(FinalPresentationPolish.Folder);
        var scene = SceneManager.GetActiveScene(); bool dirty = scene.isDirty; int roots = scene.rootCount; float scale = Time.timeScale;
        var report = new List<string>(); var previewScene = EditorSceneManager.NewPreviewScene();
        try
        {
            var mission = Object.FindFirstObjectByType<EvacuationMission>();
            var nav = Object.FindFirstObjectByType<ObjectiveNavigation>();
            var fade = Object.FindFirstObjectByType<AshfallSceneFade>();
            var mainHud = Object.FindFirstObjectByType<RescueHUD>();
            Check(report, "Waypoint has an HDR ring and a separate halo", nav.ringBrightness > 2 && nav.markerGlow && nav.markerGlow.sprite && nav.ring.sharedMaterial.GetFloat("_DstBlend") == 1);
            Check(report, "Ending iris is bound below the existing result Canvas", fade.endingCurtain && fade.endingCurtain.material && fade.canvas.sortingOrder < mainHud.canvas.sortingOrder);
            var path = new NavMeshPath();
            foreach (var resident in mission.residents.Where(r => r))
                Check(report, resident.name + " rests deeper inside on a complete path (" + EvacuationResident.FlatDistance(resident.safePlace.position, mission.shelterEntrance.position).ToString("F1") + "m)",
                    mission.IsInsideShelter(resident.safePlace.position) && EvacuationResident.FlatDistance(resident.safePlace.position, mission.shelterEntrance.position) >= 14 &&
                    NavMesh.CalculatePath(mission.shelterEntrance.position, resident.safePlace.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ContextualInteractionSetup.PrefabPath);
            var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previewScene); clone.SetActive(false);
            var ui = clone.GetComponent<ContextualInteractionUI>();
            var layout = typeof(ContextualInteractionUI).GetMethod("ApplyPromptLayout", Hidden);
            layout.Invoke(ui, new object[] { true, true });
            ui.titleText.text = "Yangını Söndür"; ui.keyText.text = "[Right Button]";
            ui.detailText.text = "Su topunu yangına doğrult ve basılı tut";
            Check(report, "Turkish action labels use the new Modular face", ui.titleText.font.name.Contains("Modular") && "ÇĞİÖŞÜçğıöşü".All(c => ui.titleText.font.HasCharacter(c, true)));
            Check(report, "Vehicle title and input fit the compact gutter", ui.titleText.GetPreferredValues(ui.titleText.text).x <= ui.vehiclePromptWidth &&
                ui.keyText.GetPreferredValues(ui.keyText.text).x <= ui.vehiclePromptWidth && ui.titleText.fontSize == 24 && ui.keyText.fontSize == 23);
            Check(report, "Vehicle hint wraps inside its allocated height", ui.detailText.GetPreferredValues(ui.detailText.text, ui.vehiclePromptWidth, 0).y <= ui.detailText.rectTransform.rect.height + 1);
            foreach (var resolution in new[] { new Vector2(1920, 1080), new Vector2(1280, 720), new Vector2(1024, 768) })
            {
                float factor = Mathf.Sqrt(resolution.x / 1920 * (resolution.y / 1080));
                Vector2 canvas = resolution / factor;
                Rect dock = new Rect(canvas.x - ui.vehiclePromptWidth - ui.screenMargin.x - 12, canvas.y * ui.vehicleScreenHeight - 77, ui.vehiclePromptWidth, 154);
                Check(report, "Right gutter leaves the centre and water HUD clear at " + resolution, dock.xMin > canvas.x * .65f && dock.yMin > 200 && dock.yMax < canvas.y - 130 && dock.xMax < canvas.x);
            }
            layout.Invoke(ui, new object[] { false, true });
            Check(report, "On-foot prompts regain their world layout", ui.promptRect.sizeDelta.x == 420 && ui.titleText.alignment == TextAlignmentOptions.Center && ui.keyText.fontSize == 36);

            var flow = clone.AddComponent<RescueGameFlow>(); flow.resultDelaySeconds = 2.4f;
            var hud = clone.AddComponent<RescueHUD>(); hud.flow = flow; hud.victoryPanel = ui.promptGroup; hud.outcomeFadeInSeconds = .65f;
            var present = typeof(RescueHUD).GetMethod("PresentOutcome", Hidden);
            var finished = typeof(RescueGameFlow).GetField("finishedAt", Hidden);
            foreach (var result in new[] { RescueGameFlow.GameState.Won, RescueGameFlow.GameState.Lost })
            {
                hud.victoryPanel.alpha = 0;
                typeof(RescueGameFlow).GetProperty("State").SetValue(flow, result);
                finished.SetValue(flow, Time.unscaledTime - 1.2f);
                present.Invoke(hud, new object[] { flow.ResultReady });
                Check(report, result + " keeps statistics hidden during edge closure", !flow.ResultReady && flow.EndingProgress > .45f && flow.EndingProgress < .55f && hud.victoryPanel.alpha == 0);
                finished.SetValue(flow, Time.unscaledTime - 2.7f);
                present.Invoke(hud, new object[] { flow.ResultReady });
                Check(report, result + " reveals results gradually after closure", flow.ResultReady && hud.victoryPanel.alpha > .1f && hud.victoryPanel.alpha < .9f && !hud.victoryPanel.interactable);
                finished.SetValue(flow, Time.unscaledTime - 3.2f);
                present.Invoke(hud, new object[] { flow.ResultReady });
                Check(report, result + " enables the result controls after the fade", hud.VictoryVisible && hud.victoryPanel.interactable);
            }
            Iris(report, fade.endingCurtain.material);
            report.Add("LIMITATION: isolated Edit Mode layout, result-clock and GPU shader checks plus current scene NavMesh paths. Not a manual vehicle/evacuation/ending Play Mode run.");
        }
        catch (Exception ex) { report.Add("FAIL " + ex); }
        finally
        {
            EditorSceneManager.ClosePreviewScene(previewScene);
            Check(report, "Active scene, dirty state, roots and time scale preserved", SceneManager.GetActiveScene() == scene && scene.isDirty == dirty && scene.rootCount == roots && Time.timeScale == scale);
            File.WriteAllLines(FinalPresentationPolish.Folder + "/checks.txt", report);
        }
    }
    static void Iris(List<string> report, Material source)
    {
        var preview = new PreviewRenderUtility(); var material = new Material(source); var mesh = new Mesh();
        try
        {
            mesh.vertices = new[] { new Vector3(-1.7778f, -1, 0), new Vector3(-1.7778f, 1, 0), new Vector3(1.7778f, 1, 0), new Vector3(1.7778f, -1, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white }; mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            preview.camera.orthographic = true; preview.camera.orthographicSize = 1; preview.camera.aspect = 16f / 9;
            preview.camera.nearClipPlane = .1f; preview.camera.farClipPlane = 10;
            preview.camera.transform.SetPositionAndRotation(new Vector3(0, 0, -2), Quaternion.identity);
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = Color.white;
            Check(report, "Ending shader compiles without errors", !ShaderUtil.ShaderHasError(material.shader));
            for (int stage = 0; stage < 3; stage++)
            {
                material.SetFloat("_Progress", stage * .5f);
                preview.DrawMesh(mesh, Matrix4x4.identity, material, 0);
                preview.BeginPreview(new Rect(0, 0, 640, 360), GUIStyle.none); preview.Render(true); var rendered = preview.EndPreview();
                var target = RenderTexture.GetTemporary(640, 360, 0); var previous = RenderTexture.active; var picture = new Texture2D(640, 360, TextureFormat.RGB24, false);
                try
                {
                    Graphics.Blit(rendered, target); RenderTexture.active = target;
                    picture.ReadPixels(new Rect(0, 0, 640, 360), 0, 0); picture.Apply();
                    float centre = picture.GetPixel(320, 180).grayscale, edge = picture.GetPixel(10, 10).grayscale;
                    bool correct = stage == 0 ? centre > .95f && edge > .95f : stage == 1 ? centre > .9f && edge < .1f : centre < .02f && edge < .02f;
                    Check(report, "Iris GPU stage " + stage + " (centre=" + centre.ToString("F2") + ", edge=" + edge.ToString("F2") + ")", correct);
                    File.WriteAllBytes(FinalPresentationPolish.Folder + "/ending-" + stage + ".png", picture.EncodeToPNG());
                }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); Object.DestroyImmediate(picture); }
            }
        }
        finally { preview.Cleanup(); Object.DestroyImmediate(material); Object.DestroyImmediate(mesh); }
    }
    static void Check(List<string> report, string label, bool passed) => report.Add((passed ? "PASS " : "FAIL ") + label);
}
